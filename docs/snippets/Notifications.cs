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

#region base-type-handler
// A base class shared by several notifications.
public abstract record CustomerEvent(Guid CustomerId) : INotification;

public sealed record CustomerDeactivated(Guid CustomerId) : CustomerEvent(CustomerId);

// Runs for CustomerDeactivated and for every other notification derived from CustomerEvent.
public sealed class ProjectCustomerEvents : INotificationHandler<CustomerEvent>
{
    public Task Handle(CustomerEvent notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

// Runs for every notification, after the handlers for its own type and its base classes.
public sealed class AuditAllNotifications : INotificationHandler<INotification>
{
    public Task Handle(INotification notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Published {notification.GetType().Name}");
        return Task.CompletedTask;
    }
}
#endregion

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
