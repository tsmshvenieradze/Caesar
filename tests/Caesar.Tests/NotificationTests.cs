using Caesar.NotificationPublishers;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Caesar.Tests.Notifications;

public sealed record Pinged(Journal Journal, TimeSpan Delay = default, string? FailIn = null) : INotification;

public sealed class FirstPingedHandler : INotificationHandler<Pinged>
{
    public async Task Handle(Pinged notification, CancellationToken cancellationToken)
    {
        notification.Journal.Add("first-start");
        if (notification.Delay > TimeSpan.Zero)
        {
            await Task.Delay(notification.Delay, cancellationToken);
        }

        if (notification.FailIn is "first" or "both")
        {
            throw new InvalidOperationException("first failed");
        }

        notification.Journal.Add("first-end");
    }
}

public sealed class SecondPingedHandler : INotificationHandler<Pinged>
{
    public Task Handle(Pinged notification, CancellationToken cancellationToken)
    {
        notification.Journal.Add("second-start");
        if (notification.FailIn is "second" or "both")
        {
            throw new InvalidOperationException("second failed");
        }

        notification.Journal.Add("second-end");
        return Task.CompletedTask;
    }
}

public sealed class SyncPingedHandler : NotificationHandler<Pinged>
{
    protected override void Handle(Pinged notification) => notification.Journal.Add("sync");
}

public sealed record Silent : INotification;

public class NotificationTests
{
    [Fact]
    public async Task Publish_invokes_every_handler_sequentially_by_default()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<NotificationTests>();

        await provider.GetRequiredService<IPublisher>().Publish(new Pinged(journal, TimeSpan.FromMilliseconds(50)));

        Assert.Equal(["first-start", "first-end", "second-start", "second-end", "sync"], journal.Entries);
    }

    [Fact]
    public async Task Default_publisher_stops_at_the_first_failure()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<NotificationTests>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<IPublisher>().Publish(new Pinged(journal, FailIn: "first")));

        Assert.Equal(["first-start"], journal.Entries);
    }

    [Fact]
    public async Task TaskWhenAll_publisher_runs_handlers_concurrently()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<NotificationTests>(cfg => cfg.NotificationPublisherType = typeof(TaskWhenAllPublisher));

        await provider.GetRequiredService<IPublisher>().Publish(new Pinged(journal, TimeSpan.FromMilliseconds(50)));

        // The second handler completes while the first is still delayed.
        Assert.Equal(["first-start", "second-start", "second-end", "sync", "first-end"], journal.Entries);
    }

    [Fact]
    public async Task TaskWhenAll_publisher_aggregates_multiple_failures()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<NotificationTests>(cfg => cfg.NotificationPublisher = new TaskWhenAllPublisher());

        var aggregate = await Assert.ThrowsAsync<AggregateException>(
            () => provider.GetRequiredService<IPublisher>().Publish(new Pinged(journal, FailIn: "both")));

        Assert.Equal(2, aggregate.InnerExceptions.Count);
    }

    [Fact]
    public async Task TaskWhenAll_publisher_unwraps_a_single_failure()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<NotificationTests>(cfg => cfg.NotificationPublisher = new TaskWhenAllPublisher());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<IPublisher>().Publish(new Pinged(journal, FailIn: "second")));

        Assert.Equal("second failed", exception.Message);
    }

    [Fact]
    public async Task ContinueOnFailure_publisher_runs_all_handlers_then_throws()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<NotificationTests>(cfg => cfg.NotificationPublisherType = typeof(ForeachAwaitContinueOnFailurePublisher));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<IPublisher>().Publish(new Pinged(journal, FailIn: "first")));

        Assert.Equal("first failed", exception.Message);
        Assert.Equal(["first-start", "second-start", "second-end", "sync"], journal.Entries);
    }

    [Fact]
    public async Task Publish_with_no_handlers_is_a_no_op()
    {
        await using var provider = TestHost.Build<NotificationTests>();

        await provider.GetRequiredService<IPublisher>().Publish(new Silent());
    }

    [Fact]
    public async Task Publish_object_dispatches_by_runtime_type()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<NotificationTests>();
        object notification = new Pinged(journal);

        await provider.GetRequiredService<IPublisher>().Publish(notification);

        Assert.Equal(["first-start", "first-end", "second-start", "second-end", "sync"], journal.Entries);
    }

    [Fact]
    public async Task Publish_object_that_is_not_a_notification_throws()
    {
        await using var provider = TestHost.Build<NotificationTests>();

        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetRequiredService<IPublisher>().Publish(new object()));
    }

    [Fact]
    public async Task Custom_publisher_receives_one_executor_per_handler()
    {
        var publisher = new Mock<INotificationPublisher>();
        IEnumerable<NotificationHandlerExecutor>? captured = null;
        publisher
            .Setup(p => p.Publish(It.IsAny<IEnumerable<NotificationHandlerExecutor>>(), It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<NotificationHandlerExecutor>, INotification, CancellationToken>((executors, _, _) => captured = executors)
            .Returns(Task.CompletedTask);

        await using var provider = TestHost.Build<NotificationTests>(cfg => cfg.NotificationPublisher = publisher.Object);

        await provider.GetRequiredService<IPublisher>().Publish(new Pinged(new Journal()));

        Assert.NotNull(captured);
        Assert.Collection(
            captured,
            e => Assert.IsType<FirstPingedHandler>(e.HandlerInstance),
            e => Assert.IsType<SecondPingedHandler>(e.HandlerInstance),
            e => Assert.IsType<SyncPingedHandler>(e.HandlerInstance));
    }

    [Fact]
    public async Task Publish_forwards_the_cancellation_token()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<NotificationTests>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetRequiredService<IPublisher>().Publish(new Pinged(journal, TimeSpan.FromSeconds(5)), cts.Token));
    }
}
