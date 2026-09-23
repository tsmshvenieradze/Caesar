namespace Caesar.NotificationPublishers;

/// <summary>
/// Starts every handler immediately and awaits them all. Exceptions from all failing handlers are
/// collected into a single <see cref="AggregateException"/> so no failure is lost.
/// </summary>
public sealed class TaskWhenAllPublisher : INotificationPublisher
{
    /// <inheritdoc />
    public async Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handlerExecutors);

        var tasks = handlerExecutors
            .Select(executor => InvokeAsync(executor, notification, cancellationToken))
            .ToArray();

        if (tasks.Length == 0)
        {
            return;
        }

        var whenAll = Task.WhenAll(tasks);
        try
        {
            await whenAll.ConfigureAwait(false);
        }
        catch (Exception) when (whenAll.Exception is { } aggregate)
        {
            // Rethrow the full aggregate rather than only the first inner exception that `await` surfaces.
            throw aggregate.InnerExceptions.Count == 1 ? aggregate.InnerExceptions[0] : aggregate;
        }
    }

    /// <summary>Turns a handler that throws synchronously into a faulted task so it is collected with the others.</summary>
    private static async Task InvokeAsync(NotificationHandlerExecutor executor, INotification notification, CancellationToken cancellationToken)
        => await executor.HandlerCallback(notification, cancellationToken).ConfigureAwait(false);
}
