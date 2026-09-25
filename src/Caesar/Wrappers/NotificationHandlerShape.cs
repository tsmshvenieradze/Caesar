using Caesar.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Wrappers;

/// <summary>
/// Which other notification types' handlers also handle <typeparamref name="TNotification"/> in one container, worked
/// out once from its <see cref="CaesarRegistry"/>: the base classes and interfaces of <typeparamref name="TNotification"/>
/// that have closed <see cref="INotificationHandler{TNotification}"/> registrations. It is an open-generic singleton,
/// so the container keeps one per notification type.
/// </summary>
/// <remarks>
/// <see cref="INotificationHandler{TNotification}"/> is contravariant, so a handler for a base type can handle the
/// notification, but the container only answers for the exact service type it is asked for. In the common case, where
/// nothing is registered for a base type, the shape has no levels and publishing asks the container for nothing
/// beyond the shape itself.
/// </remarks>
/// <typeparam name="TNotification">The runtime type of a published notification.</typeparam>
internal sealed class NotificationHandlerShape<TNotification>
    where TNotification : INotification
{
    private readonly NotificationHandlerLevel[] _baseLevels;

    /// <summary>Creates the shape. Called by the container.</summary>
    /// <param name="registry">What the container's service collection registers.</param>
    public NotificationHandlerShape(CaesarRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var levels = new List<NotificationHandlerLevel>();
        foreach (var baseType in BaseNotificationTypes(typeof(TNotification)))
        {
            // Only closed registrations make a level. Open-generic handlers are resolved for the runtime type alone, so
            // none of them runs once per level.
            var service = typeof(INotificationHandler<>).MakeGenericType(baseType);
            if (registry.HasClosed(service))
            {
                levels.Add(NotificationHandlerLevel.Create(baseType, registry.GetOpenGenericClosings(service)));
            }
        }

        _baseLevels = [.. levels];
    }

    /// <summary><see langword="true"/> when a base class or interface of <typeparamref name="TNotification"/> has handlers of its own.</summary>
    public bool HasBaseLevels => _baseLevels.Length != 0;

    /// <summary>
    /// Executors for <paramref name="handlers"/>, the runtime type's own, followed by those registered for its base
    /// classes, most derived first, then for its interfaces. A handler class that already ran for a more specific type
    /// is skipped, so a class registered for several of them runs once.
    /// </summary>
    public NotificationHandlerExecutor[] CreateExecutors(INotificationHandler<TNotification>[] handlers, IServiceProvider serviceProvider)
    {
        // Resolve every level first, so the executors go into one array sized for all of them.
        var resolved = new object[_baseLevels.Length][];
        var capacity = handlers.Length;
        for (var level = 0; level < _baseLevels.Length; level++)
        {
            resolved[level] = _baseLevels[level].Resolve(serviceProvider);
            capacity += resolved[level].Length;
        }

        if (capacity == 0)
        {
            return [];
        }

        var executors = new NotificationHandlerExecutor[capacity];
        var count = 0;
        for (var i = 0; i < handlers.Length; i++)
        {
            executors[count++] = NotificationHandlerWrapperImpl<TNotification>.CreateExecutor(handlers[i]);
        }

        for (var level = 0; level < _baseLevels.Length; level++)
        {
            // Each level is taken as the container reports it, like the runtime type's own handlers, less the classes
            // that ran for a more specific type and the handlers the container closed from an open generic.
            var moreSpecific = count;
            foreach (var handler in resolved[level])
            {
                var handlerType = handler.GetType();
                if (!_baseLevels[level].IsOpenGenericClosing(handlerType) && !Contains(executors, moreSpecific, handlerType))
                {
                    executors[count++] = _baseLevels[level].CreateExecutor(handler);
                }
            }
        }

        if (count != executors.Length)
        {
            Array.Resize(ref executors, count);
        }

        return executors;
    }

    /// <summary>Whether one of the first <paramref name="count"/> executors runs a handler of class <paramref name="handlerType"/>. A publish has a handful of handlers, so a scan beats a set.</summary>
    private static bool Contains(NotificationHandlerExecutor[] executors, int count, Type handlerType)
    {
        for (var i = 0; i < count; i++)
        {
            if (executors[i].HandlerInstance.GetType() == handlerType)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The notification types other than <paramref name="notificationType"/> whose handlers can handle it: its base
    /// classes, most derived first, then its interfaces, a derived interface before the ones it extends, so
    /// <see cref="INotification"/> comes last. Types that are not notifications cannot have handlers and are left out.
    /// </summary>
    private static List<Type> BaseNotificationTypes(Type notificationType)
    {
        var result = new List<Type>();

        // A class that is not a notification has no base class that is one.
        for (var current = notificationType.BaseType; current is not null && typeof(INotification).IsAssignableFrom(current); current = current.BaseType)
        {
            result.Add(current);
        }

        // A derived interface implements more interfaces than any interface it extends, so sorting by that count,
        // descending, puts it first. Insertion sort is stable, so unrelated interfaces keep the order GetInterfaces reports.
        var firstInterface = result.Count;
        var depths = new List<int>();
        foreach (var candidate in notificationType.GetInterfaces())
        {
            if (!typeof(INotification).IsAssignableFrom(candidate))
            {
                continue;
            }

            var depth = candidate.GetInterfaces().Length;
            var index = depths.Count;
            while (index > 0 && depths[index - 1] < depth)
            {
                index--;
            }

            depths.Insert(index, depth);
            result.Insert(firstInterface + index, candidate);
        }

        return result;
    }
}

/// <summary>The handlers registered for one base class or interface of a published notification.</summary>
internal abstract class NotificationHandlerLevel
{
    /// <summary>Creates the level for <paramref name="notificationType"/>.</summary>
    /// <param name="notificationType">The base class or interface.</param>
    /// <param name="openGenericClosings">Handler types the container closes from open-generic registrations, which this level skips.</param>
    public static NotificationHandlerLevel Create(Type notificationType, Type[] openGenericClosings)
        => (NotificationHandlerLevel)Activator.CreateInstance(
            typeof(NotificationHandlerLevel<>).MakeGenericType(notificationType),
            new object[] { openGenericClosings })!;

    /// <summary>Resolves every handler registered for this level's notification type, in the container's order.</summary>
    public abstract object[] Resolve(IServiceProvider serviceProvider);

    /// <summary>
    /// <see langword="true"/> when <paramref name="handlerType"/> is one the container closes from an open-generic
    /// registration for this level, rather than one registered for it.
    /// </summary>
    public abstract bool IsOpenGenericClosing(Type handlerType);

    /// <summary>An executor for <paramref name="handler"/>, one of the handlers <see cref="Resolve"/> returned.</summary>
    public abstract NotificationHandlerExecutor CreateExecutor(object handler);
}

/// <summary>The handlers registered for <typeparamref name="TNotification"/>, a base class or interface of a published notification.</summary>
/// <typeparam name="TNotification">The base class or interface.</typeparam>
/// <param name="openGenericClosings">
/// Handler types the container closes from open-generic registrations. They are skipped: open-generic handlers are
/// resolved for the published notification's runtime type alone, and would otherwise run once more for every level.
/// </param>
internal sealed class NotificationHandlerLevel<TNotification>(Type[] openGenericClosings) : NotificationHandlerLevel
    where TNotification : INotification
{
    /// <inheritdoc />
    public override object[] Resolve(IServiceProvider serviceProvider)
    {
        var resolved = serviceProvider.GetServices<INotificationHandler<TNotification>>();

        // Microsoft.Extensions.DependencyInjection hands back an array, which is read here and never written.
        return resolved as INotificationHandler<TNotification>[] ?? [.. resolved];
    }

    /// <inheritdoc />
    public override bool IsOpenGenericClosing(Type handlerType)
        => openGenericClosings.Length != 0 && Array.IndexOf(openGenericClosings, handlerType) >= 0;

    /// <inheritdoc />
    public override NotificationHandlerExecutor CreateExecutor(object handler)
        => NotificationHandlerWrapperImpl<TNotification>.CreateExecutor((INotificationHandler<TNotification>)handler);
}
