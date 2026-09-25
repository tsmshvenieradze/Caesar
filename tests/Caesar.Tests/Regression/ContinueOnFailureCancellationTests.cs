using Caesar.NotificationPublishers;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.ContinueOnFailureCancellation;

/// <summary>What each handler does, by position: ok, fail, cancel, fail-and-cancel, cancel-and-observe, unrelated-oce.</summary>
public sealed record Relayed(Journal Journal, CancellationTokenSource Cts, params string[] Steps) : INotification;

internal static class Step
{
    public static Task Run(int index, Relayed notification, CancellationToken cancellationToken)
    {
        notification.Journal.Add($"h{index}");
        switch (index < notification.Steps.Length ? notification.Steps[index] : "ok")
        {
            case "fail":
                throw new InvalidOperationException($"h{index} failed");
            case "fail-and-cancel":
                notification.Cts.Cancel();
                throw new InvalidOperationException($"h{index} failed");
            case "cancel":
                // Cancels the publish but ignores the token itself.
                notification.Cts.Cancel();
                return Task.CompletedTask;
            case "cancel-and-observe":
                notification.Cts.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            case "unrelated-oce":
                // An internal timeout, not the publish token.
                throw new OperationCanceledException("h" + index + " timed out");
            default:
                return Task.CompletedTask;
        }
    }
}

public sealed class H0 : INotificationHandler<Relayed>
{
    public Task Handle(Relayed notification, CancellationToken cancellationToken) => Step.Run(0, notification, cancellationToken);
}

public sealed class H1 : INotificationHandler<Relayed>
{
    public Task Handle(Relayed notification, CancellationToken cancellationToken) => Step.Run(1, notification, cancellationToken);
}

public sealed class H2 : INotificationHandler<Relayed>
{
    public Task Handle(Relayed notification, CancellationToken cancellationToken) => Step.Run(2, notification, cancellationToken);
}

public sealed class H3 : INotificationHandler<Relayed>
{
    public Task Handle(Relayed notification, CancellationToken cancellationToken) => Step.Run(3, notification, cancellationToken);
}

/// <summary>F30: cancellation stops <see cref="ForeachAwaitContinueOnFailurePublisher"/> without losing the failures collected so far.</summary>
public class ContinueOnFailureCancellationTests
{
    private static TestProvider Build() =>
        TestHost.Build<ContinueOnFailureCancellationTests>(cfg => cfg.NotificationPublisherType = typeof(ForeachAwaitContinueOnFailurePublisher));

    [Fact]
    public async Task Cancellation_without_failures_stops_the_publish_and_propagates()
    {
        var journal = new Journal();
        using var cts = new CancellationTokenSource();
        await using var provider = Build();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetRequiredService<IPublisher>().Publish(new Relayed(journal, cts, "cancel"), cts.Token));

        Assert.Equal(["h0"], journal.Entries);
    }

    [Fact]
    public async Task An_already_cancelled_token_invokes_no_handler()
    {
        var journal = new Journal();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await using var provider = Build();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetRequiredService<IPublisher>().Publish(new Relayed(journal, cts), cts.Token));

        Assert.Equal(cts.Token, exception.CancellationToken);
        Assert.Empty(journal.Entries);
    }

    [Fact]
    public async Task A_failure_before_cancellation_is_surfaced_and_later_handlers_are_skipped()
    {
        var journal = new Journal();
        using var cts = new CancellationTokenSource();
        await using var provider = Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<IPublisher>().Publish(new Relayed(journal, cts, "fail-and-cancel"), cts.Token));

        Assert.Equal("h0 failed", exception.Message);
        Assert.Contains(nameof(Step) + "." + nameof(Step.Run), exception.StackTrace, StringComparison.Ordinal);
        Assert.Equal(["h0"], journal.Entries);
    }

    [Fact]
    public async Task A_failure_wins_over_a_handler_that_observes_cancellation()
    {
        var journal = new Journal();
        using var cts = new CancellationTokenSource();
        await using var provider = Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<IPublisher>().Publish(new Relayed(journal, cts, "fail", "cancel-and-observe"), cts.Token));

        Assert.Equal("h0 failed", exception.Message);
        Assert.Equal(["h0", "h1"], journal.Entries);
    }

    [Fact]
    public async Task Several_failures_before_cancellation_are_aggregated()
    {
        var journal = new Journal();
        using var cts = new CancellationTokenSource();
        await using var provider = Build();

        var aggregate = await Assert.ThrowsAsync<AggregateException>(
            () => provider.GetRequiredService<IPublisher>().Publish(new Relayed(journal, cts, "fail", "fail-and-cancel"), cts.Token));

        Assert.Collection(
            aggregate.InnerExceptions,
            e => Assert.Equal("h0 failed", e.Message),
            e => Assert.Equal("h1 failed", e.Message));
        Assert.Equal(["h0", "h1"], journal.Entries);
    }

    [Fact]
    public async Task A_cancellation_unrelated_to_the_publish_token_is_collected_like_any_failure()
    {
        var journal = new Journal();
        using var cts = new CancellationTokenSource();
        await using var provider = Build();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetRequiredService<IPublisher>().Publish(new Relayed(journal, cts, "unrelated-oce"), cts.Token));

        Assert.Equal("h0 timed out", exception.Message);
        Assert.Equal(["h0", "h1", "h2", "h3"], journal.Entries);
    }
}
