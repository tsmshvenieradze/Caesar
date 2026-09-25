namespace Caesar;

/// <summary>
/// Strategy that decides how the handlers of a notification are invoked (sequentially, in parallel, ...).
/// </summary>
/// <remarks>
/// The executors hold handler instances resolved from the publishing caller's scope, and the token is the caller's.
/// Run them before the returned task completes: a publisher that returns first and runs them later, for example on a
/// background thread or through a channel, may run handlers whose scoped dependencies were already disposed. For
/// background processing, queue the notification itself and publish it from a scope of the consumer's own.
/// </remarks>
public interface INotificationPublisher
{
    /// <summary>Invokes the handler executors for <paramref name="notification"/>.</summary>
    /// <param name="handlerExecutors">
    /// One executor per resolved handler: those for the notification's runtime type, in registration order, then those
    /// registered for its base classes and interfaces.
    /// </param>
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
