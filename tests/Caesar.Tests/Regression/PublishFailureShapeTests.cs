using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.PublishFailureShape;

public sealed record Unbuildable : INotification;

public sealed record Unresolvable : INotification;

public sealed record Plain : INotification;

public sealed class UnbuildableHandler : INotificationHandler<Unbuildable>
{
    public UnbuildableHandler() => throw new InvalidOperationException("constructor failed");

    public Task Handle(Unbuildable notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Deliberately not registered, so <see cref="UnresolvableHandler"/> cannot be built.</summary>
public interface IMissingDependency;

public sealed class UnresolvableHandler(IMissingDependency dependency) : INotificationHandler<Unresolvable>
{
    public IMissingDependency Dependency { get; } = dependency;

    public Task Handle(Unresolvable notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class PlainHandler : INotificationHandler<Plain>
{
    public Task Handle(Plain notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>A non-async publisher that throws before returning a task.</summary>
public sealed class EagerlyThrowingPublisher(Exception exception) : INotificationPublisher
{
    public Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
        => throw exception;
}

/// <summary>C02: <see cref="IPublisher.Publish{TNotification}"/> reports failures through the returned task, never synchronously.</summary>
public class PublishFailureShapeTests
{
    [Fact]
    public async Task A_handler_constructor_failure_faults_the_task()
    {
        await using var provider = TestHost.Build<PublishFailureShapeTests>();
        var publisher = provider.GetRequiredService<IPublisher>();

        var (task, synchronous) = Start(() => publisher.Publish(new Unbuildable()));

        Assert.Null(synchronous);
        Assert.True(task.IsFaulted);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal("constructor failed", exception.Message);
    }

    [Fact]
    public async Task A_missing_handler_dependency_faults_the_task()
    {
        await using var provider = TestHost.Build<PublishFailureShapeTests>();
        var publisher = provider.GetRequiredService<IPublisher>();

        var (task, synchronous) = Start(() => publisher.Publish((object)new Unresolvable()));

        Assert.Null(synchronous);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Contains(nameof(IMissingDependency), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_publisher_that_throws_synchronously_faults_the_task()
    {
        var thrown = new InvalidOperationException("publisher failed");
        await using var provider = TestHost.Build<PublishFailureShapeTests>(cfg => cfg.NotificationPublisher = new EagerlyThrowingPublisher(thrown));
        var publisher = provider.GetRequiredService<IPublisher>();

        var (task, synchronous) = Start(() => publisher.Publish(new Plain()));

        Assert.Null(synchronous);
        Assert.True(task.IsFaulted);
        Assert.Same(thrown, await Record.ExceptionAsync(() => task));
    }

    [Fact]
    public async Task A_publisher_that_cancels_synchronously_cancels_the_task()
    {
        var thrown = new OperationCanceledException("publisher cancelled");
        await using var provider = TestHost.Build<PublishFailureShapeTests>(cfg => cfg.NotificationPublisher = new EagerlyThrowingPublisher(thrown));
        var publisher = provider.GetRequiredService<IPublisher>();

        var (task, synchronous) = Start(() => publisher.Publish(new Plain()));

        Assert.Null(synchronous);
        Assert.True(task.IsCanceled);
        Assert.Same(thrown, await Record.ExceptionAsync(() => task));
    }

    [Fact]
    public async Task Invalid_arguments_still_throw_synchronously()
    {
        await using var provider = TestHost.Build<PublishFailureShapeTests>();
        var publisher = provider.GetRequiredService<IPublisher>();

        Assert.Throws<ArgumentNullException>(() => { _ = publisher.Publish<Plain>(null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = publisher.Publish((object)null!); });
        Assert.Throws<ArgumentException>(() => { _ = publisher.Publish(new object()); });
    }

    /// <summary>Calls <paramref name="publish"/> and separates a synchronous throw from the task it returns.</summary>
    private static (Task Task, Exception? Synchronous) Start(Func<Task> publish)
    {
        try
        {
            return (publish(), null);
        }
        catch (Exception e)
        {
            return (Task.CompletedTask, e);
        }
    }
}
