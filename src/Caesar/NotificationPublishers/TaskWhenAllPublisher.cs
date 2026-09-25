using System.Runtime.ExceptionServices;

namespace Caesar.NotificationPublishers;

/// <summary>
/// Starts every handler immediately and awaits them all. Exceptions from all failing handlers are
/// collected into a single <see cref="AggregateException"/> so no failure is lost.
/// </summary>
/// <remarks>
/// <para>
/// A single failure is rethrown as-is, with its original stack trace. A fault wins over a cancellation;
/// if handlers were only canceled, the publish is canceled.
/// </para>
/// <para>
/// Each handler is started in turn and runs synchronously until its first <c>await</c>; the rest runs concurrently.
/// The handlers come from the same scope, so they must not share a scoped service that is not thread-safe, such as an
/// Entity Framework Core <c>DbContext</c>.
/// </para>
/// </remarks>
public sealed class TaskWhenAllPublisher : INotificationPublisher
{
    /// <inheritdoc />
    public async Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handlerExecutors);

        var executors = handlerExecutors as NotificationHandlerExecutor[] ?? [.. handlerExecutors];
        if (executors.Length == 0)
        {
            return;
        }

        if (executors.Length == 1)
        {
            // Awaiting the only handler yields the same outcome as Task.WhenAll over it, without the array.
            await Start(executors[0], notification, cancellationToken).ConfigureAwait(false);
            return;
        }

        var tasks = new Task[executors.Length];
        var allSucceeded = true;
        for (var i = 0; i < executors.Length; i++)
        {
            var task = Start(executors[i], notification, cancellationToken);
            allSucceeded &= task.IsCompletedSuccessfully;
            tasks[i] = task;
        }

        // Handlers that finished synchronously need no Task.WhenAll.
        if (allSucceeded)
        {
            return;
        }

        var whenAll = Task.WhenAll(tasks);
        await whenAll.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (whenAll.IsCompletedSuccessfully)
        {
            return;
        }

        if (HasUnusualFault(tasks))
        {
            // Report each handler the way awaiting it would: a task faulted with several exceptions contributes
            // its first one, and one faulted with an OperationCanceledException counts as canceled.
            for (var i = 0; i < tasks.Length; i++)
            {
                if (tasks[i].Exception is { } fault)
                {
                    tasks[i] = NotificationTasks.FromException(fault.InnerExceptions[0]);
                }
            }

            whenAll = Task.WhenAll(tasks);
            await whenAll.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        if (whenAll.Exception is { } aggregate)
        {
            // A single failure keeps its original stack trace; several are rethrown together rather than
            // only the first inner exception that `await` would surface.
            if (aggregate.InnerExceptions.Count == 1)
            {
                ExceptionDispatchInfo.Throw(aggregate.InnerExceptions[0]);
            }

            throw aggregate;
        }

        // Canceled: rethrows the handler's own OperationCanceledException, so the publish is canceled too.
        await whenAll.ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the handler's task without an async wrapper. A handler that throws synchronously gives a faulted
    /// (or, for an <see cref="OperationCanceledException"/>, canceled) task, so the handlers after it still start.
    /// </summary>
    private static Task Start(NotificationHandlerExecutor executor, INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            return executor.HandlerCallback(notification, cancellationToken) ?? Task.FromException(NotificationTasks.NullTask(executor));
        }
#pragma warning disable CA1031 // The failure is collected with the other handlers' outcomes.
        catch (Exception e)
#pragma warning restore CA1031
        {
            return NotificationTasks.FromException(e);
        }
    }

    private static bool HasUnusualFault(Task[] tasks)
    {
        foreach (var task in tasks)
        {
            if (task.Exception is { } fault && (fault.InnerExceptions.Count != 1 || fault.InnerExceptions[0] is OperationCanceledException))
            {
                return true;
            }
        }

        return false;
    }
}
