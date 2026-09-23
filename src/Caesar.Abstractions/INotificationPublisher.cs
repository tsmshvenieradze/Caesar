namespace Caesar;

/// <summary>
/// Strategy that decides how the handlers of a notification are invoked
/// (sequentially, in parallel, fire-and-forget, ...).
/// </summary>
public interface INotificationPublisher
{
    /// <summary>Invokes the handler executors for <paramref name="notification"/>.</summary>
    /// <param name="handlerExecutors">One executor per resolved handler, in registration order.</param>
    /// <param name="notification">The notification being published.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken);
}

/// <summary>
/// A resolved notification handler together with a callback that invokes it.
/// </summary>
/// <param name="HandlerInstance">The handler instance (useful for logging or filtering).</param>
/// <param name="HandlerCallback">Invokes the handler for a notification.</param>
public sealed record NotificationHandlerExecutor(object HandlerInstance, Func<INotification, CancellationToken, Task> HandlerCallback);
