using System.Runtime.ExceptionServices;

namespace Caesar.NotificationPublishers;

/// <summary>Task helpers shared by the notification wrapper and the built-in publishers.</summary>
internal static class NotificationTasks
{
    /// <summary>
    /// Returns <paramref name="exception"/> as a completed task the way an <c>async</c> method would:
    /// an <see cref="OperationCanceledException"/> gives a canceled task that rethrows that same exception
    /// when awaited, anything else gives a faulted task.
    /// </summary>
    public static Task FromException(Exception exception)
        => exception is OperationCanceledException
            ? Rethrow(ExceptionDispatchInfo.Capture(exception))
            : Task.FromException(exception);

    /// <summary>The error reported when a handler returns <see langword="null"/> instead of a task.</summary>
    public static InvalidOperationException NullTask(NotificationHandlerExecutor executor)
        => new($"The notification handler {executor.HandlerInstance?.GetType().FullName ?? "(unknown)"} returned a null Task.");

#pragma warning disable CS1998 // No await on purpose: the async builder turns the rethrow into a canceled task.
    private static async Task Rethrow(ExceptionDispatchInfo exception) => exception.Throw();
#pragma warning restore CS1998
}
