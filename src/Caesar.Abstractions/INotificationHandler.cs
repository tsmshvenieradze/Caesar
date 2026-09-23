namespace Caesar;

/// <summary>
/// Handles a notification. Multiple handlers may exist for the same notification type.
/// </summary>
/// <typeparam name="TNotification">The notification type.</typeparam>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>Handles the notification.</summary>
    /// <param name="notification">The notification instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}

/// <summary>
/// Convenience base class for synchronous notification handlers.
/// </summary>
/// <typeparam name="TNotification">The notification type.</typeparam>
public abstract class NotificationHandler<TNotification> : INotificationHandler<TNotification>
    where TNotification : INotification
{
    Task INotificationHandler<TNotification>.Handle(TNotification notification, CancellationToken cancellationToken)
    {
        Handle(notification);
        return Task.CompletedTask;
    }

    /// <summary>Handles the notification synchronously.</summary>
    /// <param name="notification">The notification instance.</param>
    protected abstract void Handle(TNotification notification);
}
