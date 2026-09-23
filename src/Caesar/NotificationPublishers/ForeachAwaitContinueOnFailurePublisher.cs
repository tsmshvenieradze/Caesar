namespace Caesar.NotificationPublishers;

/// <summary>
/// Awaits handlers one after another like <see cref="ForeachAwaitPublisher"/>, but keeps going after a
/// failing handler and throws an <see cref="AggregateException"/> at the end if any handler failed.
/// Useful when every subscriber must get the event even if one of them is broken.
/// </summary>
public sealed class ForeachAwaitContinueOnFailurePublisher : INotificationPublisher
{
    /// <inheritdoc />
    public async Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handlerExecutors);

        List<Exception>? failures = null;

        foreach (var executor in handlerExecutors)
        {
            try
            {
                await executor.HandlerCallback(notification, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // Collecting every handler failure is the purpose of this publisher.
            catch (Exception e)
#pragma warning restore CA1031
            {
                (failures ??= []).Add(e);
            }
        }

        if (failures is not null)
        {
            throw failures.Count == 1 ? failures[0] : new AggregateException(failures);
        }
    }
}
