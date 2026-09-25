using System.Runtime.ExceptionServices;

namespace Caesar.NotificationPublishers;

/// <summary>
/// Awaits handlers one after another like <see cref="ForeachAwaitPublisher"/>, but keeps going after a
/// failing handler and throws an <see cref="AggregateException"/> at the end if any handler failed.
/// Useful when every subscriber must get the event even if one of them is broken.
/// </summary>
/// <remarks>
/// A single failure is rethrown as-is, with its original stack trace. Once <c>cancellationToken</c> is
/// cancelled no further handler is invoked: the publish is canceled if nothing failed so far, otherwise the
/// failures collected until then are thrown and the cancellation is dropped, as <see cref="Task.WhenAll(Task[])"/>
/// lets faults win over cancellation.
/// </remarks>
public sealed class ForeachAwaitContinueOnFailurePublisher : INotificationPublisher
{
    /// <inheritdoc />
    public async Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handlerExecutors);

        List<Exception>? failures = null;

        // The mediator passes an array; indexing it avoids boxing an enumerator on every publish.
        var array = handlerExecutors as NotificationHandlerExecutor[];
        using var enumerator = array is null ? handlerExecutors.GetEnumerator() : null;

        for (var i = 0; array is not null ? i < array.Length : enumerator!.MoveNext(); i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                if (failures is null)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                break;
            }

            var executor = array is not null ? array[i] : enumerator!.Current;
            try
            {
                await (executor.HandlerCallback(notification, cancellationToken) ?? throw NotificationTasks.NullTask(executor))
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (failures is null)
                {
                    throw;
                }

                break;
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
            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Throw(failures[0]);
            }

            throw new AggregateException(failures);
        }
    }
}
