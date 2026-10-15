namespace Caesar.NotificationPublishers;

/// <summary>
/// Default strategy: awaits each handler one after another, in the order the executors are given.
/// The first exception stops the publish and propagates to the caller.
/// </summary>
public sealed class ForeachAwaitPublisher : INotificationPublisher
{
    /// <inheritdoc />
    public Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        if (handlerExecutors is null)
        {
            return Task.FromException(new ArgumentNullException(nameof(handlerExecutors)));
        }

        // The mediator passes an array; indexing it avoids boxing an enumerator on every publish.
        if (handlerExecutors is NotificationHandlerExecutor[] executors)
        {
            for (var i = 0; i < executors.Length; i++)
            {
                Task task;
                try
                {
                    task = Invoke(executors[i], notification, cancellationToken);
                }
#pragma warning disable CA1031 // Every failure is reported through the returned task, as an async method would.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    return NotificationTasks.FromException(exception);
                }

                if (task is not { IsCompletedSuccessfully: true })
                {
                    return PublishRemainingAsync(executors, i, task, notification, cancellationToken);
                }
            }

            return Task.CompletedTask;
        }

        return PublishEnumerableAsync(handlerExecutors, notification, cancellationToken);
    }

    private static async Task PublishRemainingAsync(
        NotificationHandlerExecutor[] executors,
        int index,
        Task pending,
        INotification notification,
        CancellationToken cancellationToken)
    {
        await pending.ConfigureAwait(false);

        for (var i = index + 1; i < executors.Length; i++)
        {
            await Invoke(executors[i], notification, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task PublishEnumerableAsync(
        IEnumerable<NotificationHandlerExecutor> handlerExecutors,
        INotification notification,
        CancellationToken cancellationToken)
    {
        foreach (var executor in handlerExecutors)
        {
            await Invoke(executor, notification, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task Invoke(NotificationHandlerExecutor executor, INotification notification, CancellationToken cancellationToken)
        => executor.HandlerCallback(notification, cancellationToken) ?? throw NotificationTasks.NullTask(executor);
}
