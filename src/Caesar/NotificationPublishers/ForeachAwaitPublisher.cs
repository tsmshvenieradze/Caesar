namespace Caesar.NotificationPublishers;

/// <summary>
/// Default strategy: awaits each handler one after another, in the order the executors are given.
/// The first exception stops the publish and propagates to the caller.
/// </summary>
public sealed class ForeachAwaitPublisher : INotificationPublisher
{
    /// <inheritdoc />
    public async Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handlerExecutors);

        // The mediator passes an array; indexing it avoids boxing an enumerator on every publish.
        if (handlerExecutors is NotificationHandlerExecutor[] executors)
        {
            for (var i = 0; i < executors.Length; i++)
            {
                await Invoke(executors[i], notification, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        foreach (var executor in handlerExecutors)
        {
            await Invoke(executor, notification, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task Invoke(NotificationHandlerExecutor executor, INotification notification, CancellationToken cancellationToken)
        => executor.HandlerCallback(notification, cancellationToken) ?? throw NotificationTasks.NullTask(executor);
}
