using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Wrappers;

/// <summary>Non-generic entry point for publishing a notification whose type is only known at runtime.</summary>
public abstract class NotificationHandlerWrapper
{
    /// <summary>Resolves the handlers of <paramref name="notification"/> and hands them to <paramref name="publish"/>.</summary>
    public abstract Task Handle(
        INotification notification,
        IServiceProvider serviceProvider,
        Func<IEnumerable<NotificationHandlerExecutor>, INotification, CancellationToken, Task> publish,
        CancellationToken cancellationToken);
}

/// <summary>Resolves every <see cref="INotificationHandler{TNotification}"/> for <typeparamref name="TNotification"/>.</summary>
/// <typeparam name="TNotification">The notification type.</typeparam>
public sealed class NotificationHandlerWrapperImpl<TNotification> : NotificationHandlerWrapper
    where TNotification : INotification
{
    /// <inheritdoc />
    public override Task Handle(
        INotification notification,
        IServiceProvider serviceProvider,
        Func<IEnumerable<NotificationHandlerExecutor>, INotification, CancellationToken, Task> publish,
        CancellationToken cancellationToken)
    {
        var handlers = serviceProvider
            .GetServices<INotificationHandler<TNotification>>()
            .Select(static handler => new NotificationHandlerExecutor(
                handler,
                (n, t) => handler.Handle((TNotification)n, t)))
            .ToArray();

        return publish(handlers, notification, cancellationToken);
    }
}
