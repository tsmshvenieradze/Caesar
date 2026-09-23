namespace Caesar.NotificationPublishers;

/// <summary>
/// Default strategy: awaits each handler one after another in registration order.
/// The first exception stops the publish and propagates to the caller.
/// </summary>
public sealed class ForeachAwaitPublisher : INotificationPublisher
{
    /// <inheritdoc />
    public async Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handlerExecutors);

        foreach (var executor in handlerExecutors)
        {
            await executor.HandlerCallback(notification, cancellationToken).ConfigureAwait(false);
        }
    }
}
