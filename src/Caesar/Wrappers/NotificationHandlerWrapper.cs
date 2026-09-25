using Caesar.NotificationPublishers;
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

/// <summary>
/// Resolves every <see cref="INotificationHandler{TNotification}"/> that handles <typeparamref name="TNotification"/>:
/// those registered for <typeparamref name="TNotification"/> itself, then those registered for its base classes and
/// the interfaces it implements, <see cref="INotification"/> included.
/// </summary>
/// <typeparam name="TNotification">The notification type.</typeparam>
public sealed class NotificationHandlerWrapperImpl<TNotification> : NotificationHandlerWrapper
    where TNotification : INotification
{
    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The handlers run in this order: those for <typeparamref name="TNotification"/>, open-generic handlers closed
    /// over it included, in registration order; then those for its base classes, most derived first; then those for
    /// its interfaces, a derived interface before the ones it extends, so <see cref="INotification"/> comes last. For
    /// a base class or interface only closed registrations count, so an open-generic handler runs once. A handler
    /// class registered for several of these types runs once, for the most specific of them. Base classes and
    /// interfaces are only considered when the container was set up with <c>AddCaesar</c>.
    /// </para>
    /// <para>
    /// Never throws: a handler that cannot be resolved (a throwing constructor, a missing dependency) and a
    /// publisher that throws before returning both surface through the returned task, like any handler failure.
    /// </para>
    /// </remarks>
    public override Task Handle(
        INotification notification,
        IServiceProvider serviceProvider,
        Func<IEnumerable<NotificationHandlerExecutor>, INotification, CancellationToken, Task> publish,
        CancellationToken cancellationToken)
    {
        try
        {
            return publish(CreateExecutors(serviceProvider), notification, cancellationToken);
        }
#pragma warning disable CA1031 // Every failure is reported through the task; Publish validates its arguments before this point.
        catch (Exception e)
#pragma warning restore CA1031
        {
            return NotificationTasks.FromException(e);
        }
    }

    private static NotificationHandlerExecutor[] CreateExecutors(IServiceProvider serviceProvider)
    {
        var resolved = serviceProvider.GetServices<INotificationHandler<TNotification>>();

        // Microsoft.Extensions.DependencyInjection hands back an array; other containers may not.
        var handlers = resolved as INotificationHandler<TNotification>[] ?? [.. resolved];

        // Null when the mediator is used without AddCaesar: only the handlers for the runtime type run then.
        if (serviceProvider.GetService<NotificationHandlerShape<TNotification>>() is { HasBaseLevels: true } shape)
        {
            return shape.CreateExecutors(handlers, serviceProvider);
        }

        if (handlers.Length == 0)
        {
            return [];
        }

        var executors = new NotificationHandlerExecutor[handlers.Length];
        for (var i = 0; i < handlers.Length; i++)
        {
            executors[i] = CreateExecutor(handlers[i]);
        }

        return executors;
    }

    /// <summary>An executor that invokes <paramref name="handler"/> with the notification cast to <typeparamref name="TNotification"/>.</summary>
    internal static NotificationHandlerExecutor CreateExecutor(INotificationHandler<TNotification> handler)
        => new(handler, (n, t) => handler.Handle((TNotification)n, t));
}
