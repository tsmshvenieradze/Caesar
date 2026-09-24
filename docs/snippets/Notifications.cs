using Microsoft.Extensions.Logging;

namespace Caesar.Docs.Snippets;

#region notification
public sealed record CustomerCreated(Guid CustomerId) : INotification;

public sealed class SendWelcomeEmail : INotificationHandler<CustomerCreated>
{
    public Task Handle(CustomerCreated notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class UpdateCrm : INotificationHandler<CustomerCreated>
{
    public Task Handle(CustomerCreated notification, CancellationToken cancellationToken) => Task.CompletedTask;
}
#endregion

public static class PublishExample
{
    public static async Task Run(IPublisher publisher, Guid id, CancellationToken cancellationToken)
    {
        #region publish
        await publisher.Publish(new CustomerCreated(id), cancellationToken);
        #endregion
    }
}

#region sync-handler
public sealed class CountCustomers : NotificationHandler<CustomerCreated>
{
    public int Count { get; private set; }

    protected override void Handle(CustomerCreated notification) => Count++;
}
#endregion

#region custom-publisher
// Runs handlers one after another and logs how long each one took.
public sealed partial class TimedPublisher(ILogger<TimedPublisher> logger) : INotificationPublisher
{
    public async Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        foreach (var executor in handlerExecutors)
        {
            var started = TimeProvider.System.GetTimestamp();
            await executor.HandlerCallback(notification, cancellationToken);
            LogHandled(executor.HandlerInstance.GetType().Name, TimeProvider.System.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Handler} took {Elapsed} ms")]
    private partial void LogHandled(string handler, double elapsed);
}
#endregion

#region custom-mediator
// Register with cfg.MediatorImplementationType = typeof(OutboxMediator).
public sealed class OutboxMediator(IServiceProvider serviceProvider, INotificationPublisher publisher)
    : Mediator(serviceProvider, publisher)
{
    protected override Task PublishCore(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        // Inspect, reorder or persist the notification here, then publish as usual.
        return base.PublishCore(handlerExecutors, notification, cancellationToken);
    }
}
#endregion
