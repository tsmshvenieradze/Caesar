using Caesar.NotificationPublishers;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.WhenAllSemantics;

/// <summary>
/// What each handler does, by position: ok, sync-throw, sync-oce, null, async-fail, async-cancel, faulted-oce.
/// <see cref="Oce"/> is the exception the sync-oce, async-cancel and faulted-oce steps use, so tests can check its identity.
/// </summary>
public sealed record Mixed(Journal Journal, params string[] Steps) : INotification
{
    public OperationCanceledException Oce { get; } = new("handler cancelled");
}

/// <summary>A notification whose only handler returns one task faulted with two exceptions, as a handler returning an un-awaited Task.WhenAll can.</summary>
public sealed record Doubled : INotification;

/// <summary>A notification with a single handler, which <see cref="TaskWhenAllPublisher"/> awaits without Task.WhenAll.</summary>
public sealed record Solo(string Step) : INotification
{
    public Mixed Inner { get; } = new(new Journal(), Step);
}

internal static class Step
{
    public static Task Run(int index, Mixed notification)
    {
        notification.Journal.Add($"w{index}-start");
        switch (index < notification.Steps.Length ? notification.Steps[index] : "ok")
        {
            case "sync-throw":
                throw new InvalidOperationException($"w{index} threw");
            case "sync-oce":
                throw notification.Oce;
            case "null":
                return null!;
            case "async-fail":
                return FailLater(index);
            case "async-cancel":
                return CancelLater(notification);
            case "faulted-oce":
                return Task.FromException(notification.Oce);
            default:
                notification.Journal.Add($"w{index}-end");
                return Task.CompletedTask;
        }
    }

    private static async Task FailLater(int index)
    {
        await Task.Yield();
        throw new InvalidOperationException($"w{index} failed");
    }

    private static async Task CancelLater(Mixed notification)
    {
        await Task.Yield();
        throw notification.Oce;
    }
}

public sealed class W0 : INotificationHandler<Mixed>
{
    public Task Handle(Mixed notification, CancellationToken cancellationToken) => Step.Run(0, notification);
}

public sealed class W1 : INotificationHandler<Mixed>
{
    public Task Handle(Mixed notification, CancellationToken cancellationToken) => Step.Run(1, notification);
}

public sealed class W2 : INotificationHandler<Mixed>
{
    public Task Handle(Mixed notification, CancellationToken cancellationToken) => Step.Run(2, notification);
}

public sealed class SoloHandler : INotificationHandler<Solo>
{
    public Task Handle(Solo notification, CancellationToken cancellationToken) => Step.Run(0, notification.Inner);
}

public sealed class DoubledHandler : INotificationHandler<Doubled>
{
    public Task Handle(Doubled notification, CancellationToken cancellationToken)
    {
        var source = new TaskCompletionSource();
        source.SetException([new InvalidOperationException("first"), new InvalidOperationException("second")]);
        return source.Task;
    }
}

/// <summary>Pins how <see cref="TaskWhenAllPublisher"/> starts handlers and surfaces their outcome (guards F22's rewrite).</summary>
public class TaskWhenAllSemanticsTests
{
    private static TestProvider Build(Type? publisherType = null) =>
        TestHost.Build<TaskWhenAllSemanticsTests>(cfg => cfg.NotificationPublisherType = publisherType ?? typeof(TaskWhenAllPublisher));

    [Fact]
    public async Task A_handler_that_throws_synchronously_does_not_stop_the_others_from_starting()
    {
        var journal = new Journal();
        await using var provider = Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<IPublisher>().Publish(new Mixed(journal, "sync-throw")));

        Assert.Equal("w0 threw", exception.Message);
        Assert.Equal(["w0-start", "w1-start", "w1-end", "w2-start", "w2-end"], journal.Entries);
    }

    [Fact]
    public async Task Several_synchronous_throws_are_aggregated_in_handler_order()
    {
        var journal = new Journal();
        await using var provider = Build();

        var aggregate = await Assert.ThrowsAsync<AggregateException>(
            () => provider.GetRequiredService<IPublisher>().Publish(new Mixed(journal, "sync-throw", "ok", "sync-throw")));

        Assert.Collection(
            aggregate.InnerExceptions,
            e => Assert.Equal("w0 threw", e.Message),
            e => Assert.Equal("w2 threw", e.Message));
    }

    [Fact]
    public async Task A_synchronous_OperationCanceledException_cancels_the_publish()
    {
        var journal = new Journal();
        var notification = new Mixed(journal, "sync-oce");
        await using var provider = Build();

        var task = provider.GetRequiredService<IPublisher>().Publish(notification);
        var exception = await Record.ExceptionAsync(() => task);

        Assert.True(task.IsCanceled);
        Assert.Same(notification.Oce, exception);
        Assert.Equal(["w0-start", "w1-start", "w1-end", "w2-start", "w2-end"], journal.Entries);
    }

    [Fact]
    public async Task An_asynchronous_cancellation_cancels_the_publish()
    {
        var notification = new Mixed(new Journal(), "async-cancel");
        await using var provider = Build();

        var task = provider.GetRequiredService<IPublisher>().Publish(notification);
        var exception = await Record.ExceptionAsync(() => task);

        Assert.True(task.IsCanceled);
        Assert.Same(notification.Oce, exception);
    }

    [Fact]
    public async Task A_failure_wins_over_a_cancellation()
    {
        var notification = new Mixed(new Journal(), "async-cancel", "async-fail", "ok");
        await using var provider = Build();

        var task = provider.GetRequiredService<IPublisher>().Publish(notification);
        var exception = await Record.ExceptionAsync(() => task);

        Assert.True(task.IsFaulted);
        Assert.Equal("w1 failed", Assert.IsType<InvalidOperationException>(exception).Message);
    }

    [Fact]
    public async Task A_handler_task_faulted_with_several_exceptions_contributes_its_first_one()
    {
        await using var provider = Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<IPublisher>().Publish(new Doubled()));

        Assert.Equal("first", exception.Message);
    }

    [Fact]
    public async Task A_handler_task_faulted_with_an_OperationCanceledException_cancels_the_publish()
    {
        var notification = new Mixed(new Journal(), "faulted-oce");
        await using var provider = Build();

        var task = provider.GetRequiredService<IPublisher>().Publish(notification);
        var exception = await Record.ExceptionAsync(() => task);

        Assert.True(task.IsCanceled);
        Assert.Same(notification.Oce, exception);
    }

    [Theory]
    [InlineData(typeof(ForeachAwaitPublisher), new[] { "w0-start" })]
    [InlineData(typeof(TaskWhenAllPublisher), new[] { "w0-start", "w1-start", "w1-end", "w2-start", "w2-end" })]
    [InlineData(typeof(ForeachAwaitContinueOnFailurePublisher), new[] { "w0-start", "w1-start", "w1-end", "w2-start", "w2-end" })]
    public async Task A_handler_returning_a_null_task_fails_the_publish_and_names_the_handler(Type publisherType, string[] expectedJournal)
    {
        var journal = new Journal();
        await using var provider = Build(publisherType);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<IPublisher>().Publish(new Mixed(journal, "null")));

        Assert.Contains(typeof(W0).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Equal(expectedJournal, journal.Entries);
    }
    [Theory]
    [InlineData("sync-throw", "Step.Run")]
    [InlineData("async-fail", "Step.FailLater")]
    [InlineData("null", null)]
    [InlineData("sync-oce", null)]
    [InlineData("async-cancel", null)]
    [InlineData("faulted-oce", null)]
    public async Task A_single_handler_has_the_same_outcome_as_several(string step, string? thrownFrom)
    {
        var notification = new Solo(step);
        await using var provider = Build();

        var task = provider.GetRequiredService<IPublisher>().Publish(notification);
        var exception = await Record.ExceptionAsync(() => task);

        if (step.EndsWith("oce", StringComparison.Ordinal) || step.EndsWith("cancel", StringComparison.Ordinal))
        {
            Assert.True(task.IsCanceled);
            Assert.Same(notification.Inner.Oce, exception);
        }
        else
        {
            Assert.True(task.IsFaulted);
            Assert.IsType<InvalidOperationException>(exception);
            Assert.Contains(thrownFrom ?? typeof(SoloHandler).FullName!, thrownFrom is null ? exception.Message : exception.StackTrace, StringComparison.Ordinal);
        }
    }
}
