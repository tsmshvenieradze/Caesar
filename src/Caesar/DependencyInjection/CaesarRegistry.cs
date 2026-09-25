using System.Diagnostics.CodeAnalysis;
using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.DependencyInjection;

/// <summary>How the container would answer a request for a single closed service.</summary>
internal enum ServiceAvailability
{
    /// <summary>Nothing in the service collection provides it.</summary>
    None,

    /// <summary>A closed registration provides it, or the open-generic registration the container would use closes over it.</summary>
    Registered,

    /// <summary>
    /// Only an open-generic registration provides it, and its generic constraints reject these type arguments.
    /// Asking the container for the service would throw <see cref="ArgumentException"/> rather than return <see langword="null"/>.
    /// </summary>
    ConstraintViolation,
}

/// <summary>
/// What one container registers, as far as Caesar's dispatch needs to know. One instance exists per container (a
/// factory-registered singleton). It reads the service collection of the last <c>AddCaesar</c> call on first use, by
/// which time the container has been built, so registrations made after <c>AddCaesar</c> are seen too.
/// </summary>
/// <remarks>
/// The collection is not the whole truth: a container can be built from a copy of it, the collection can change after the
/// container was built, and a third-party container can hold registrations of its own. Wherever a closed service type is
/// asked about, the container's own <see cref="IServiceProviderIsService"/> is consulted as well, so a registration the
/// container can resolve is not missed. Only what cannot be asked that way (an exception handler for any exception type)
/// relies on the collection.
/// </remarks>
internal sealed class CaesarRegistry
{
    /// <summary>The generic service types Caesar's dispatch asks about. Nothing else in the collection is indexed.</summary>
    private static readonly HashSet<Type> IndexedDefinitions =
    [
        typeof(IRequestHandler<,>),
        typeof(IRequestHandler<>),
        typeof(IStreamRequestHandler<,>),
        typeof(INotificationHandler<>),
        typeof(IRequestPreProcessor<>),
        typeof(IRequestPostProcessor<,>),
        typeof(IRequestExceptionHandler<,,>),
        typeof(IRequestExceptionAction<,>),
        typeof(IPipelineBehavior<,>),
    ];

    private readonly CaesarRegistrationState _state;
    private readonly Lazy<Lookup> _lookup;
    private readonly IServiceProviderIsService? _container;
    private readonly bool _thirdPartyContainer;

    public CaesarRegistry(IServiceCollection services, CaesarRegistrationState state, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _lookup = new Lazy<Lookup>(() => new Lookup(services));
        _container = serviceProvider.GetService<IServiceProviderIsService>();

        // Microsoft's container is built from exactly the descriptors it was given. Any other container may hold
        // registrations of its own that no descriptor describes.
        _thirdPartyContainer = serviceProvider.GetType().Assembly.GetName().Name != "Microsoft.Extensions.DependencyInjection";
    }

    /// <summary>The exception-action strategy chosen by the first <c>AddCaesar</c> call.</summary>
    public RequestExceptionActionProcessorStrategy RequestExceptionActionProcessorStrategy => _state.RequestExceptionActionProcessorStrategy;

    /// <summary>Whether the caller's own cancellation bypasses exception handlers and actions, as chosen by the first <c>AddCaesar</c> call.</summary>
    public bool BypassExceptionHandlingOnCallerCancellation => _state.BypassExceptionHandlingOnCallerCancellation;

    /// <summary>
    /// How resolving the single service <paramref name="closedServiceType"/> would go, mirroring the container's rules:
    /// an exact closed registration wins, otherwise the last open-generic registration for its definition is closed
    /// over its type arguments.
    /// </summary>
    public ServiceAvailability GetAvailability(Type closedServiceType)
    {
        var lookup = _lookup.Value;

        if (lookup.Descriptors.ContainsKey(closedServiceType))
        {
            return ServiceAvailability.Registered;
        }

        if (!closedServiceType.IsConstructedGenericType
            || !lookup.Descriptors.TryGetValue(closedServiceType.GetGenericTypeDefinition(), out var open))
        {
            // Not in the collection, but the container may still have it: built from a copy, or registered natively.
            return ContainerHas(closedServiceType) ? ServiceAvailability.Registered : ServiceAvailability.None;
        }

        // For a single service the container uses the last open-generic registration and throws when that one cannot close.
        return CanClose(open[^1], closedServiceType.GenericTypeArguments)
            ? ServiceAvailability.Registered
            : ServiceAvailability.ConstraintViolation;
    }

    /// <summary>
    /// <see langword="true"/> when resolving every <paramref name="closedServiceType"/> would find at least one:
    /// an exact closed registration, or any open-generic one that can be closed over its type arguments.
    /// </summary>
    public bool HasAny(Type closedServiceType)
    {
        var lookup = _lookup.Value;

        if (lookup.Descriptors.ContainsKey(closedServiceType))
        {
            return true;
        }

        if (closedServiceType.IsConstructedGenericType
            && lookup.Descriptors.TryGetValue(closedServiceType.GetGenericTypeDefinition(), out var open))
        {
            foreach (var descriptor in open)
            {
                if (CanClose(descriptor, closedServiceType.GenericTypeArguments))
                {
                    return true;
                }
            }
        }

        return ContainerHas(closedServiceType);
    }

    /// <summary>
    /// <see langword="true"/> when <paramref name="closedServiceType"/> has a registration of its own: a closed one, not
    /// an open-generic one that the container would close over its type arguments.
    /// </summary>
    public bool HasClosed(Type closedServiceType) => _lookup.Value.Descriptors.ContainsKey(closedServiceType);

    /// <summary>
    /// The implementation types that open-generic registrations add when every <paramref name="closedServiceType"/> is
    /// resolved: each one that can be closed over its type arguments, closed. A type that a closed registration of
    /// <paramref name="closedServiceType"/> also names is left out. Empty when there are none.
    /// </summary>
    public Type[] GetOpenGenericClosings(Type closedServiceType)
    {
        var lookup = _lookup.Value;

        if (!closedServiceType.IsConstructedGenericType
            || !lookup.Descriptors.TryGetValue(closedServiceType.GetGenericTypeDefinition(), out var open))
        {
            return [];
        }

        lookup.Descriptors.TryGetValue(closedServiceType, out var closed);

        List<Type>? closings = null;
        foreach (var descriptor in open)
        {
            if (descriptor.ImplementationType is { IsGenericTypeDefinition: true } implementation
                && TryClose(implementation, closedServiceType.GenericTypeArguments, out var closing)
                && !Names(closed, closing))
            {
                (closings ??= []).Add(closing);
            }
        }

        return closings is null ? [] : [.. closings];

        static bool Names(List<ServiceDescriptor>? descriptors, Type implementationType)
        {
            foreach (var descriptor in descriptors ?? [])
            {
                if (descriptor.ImplementationType == implementationType || descriptor.ImplementationInstance?.GetType() == implementationType)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// <see langword="true"/> when <paramref name="openServiceType"/> has an open-generic registration, or a closed one
    /// whose leading type arguments are <paramref name="leadingArguments"/>. Used where the remaining argument, such as
    /// the exception type of an exception handler, is only known at run time, so the container cannot be asked directly.
    /// It is still asked for <paramref name="catchAllServiceType"/>, the registration for <see cref="Exception"/> itself.
    /// A third-party container is assumed to have registrations of its own whenever the collection has any of that kind.
    /// </summary>
    public bool HasAnyStartingWith(Type openServiceType, Type catchAllServiceType, params ReadOnlySpan<Type> leadingArguments)
    {
        var lookup = _lookup.Value;

        if (lookup.Descriptors.ContainsKey(openServiceType))
        {
            return true;
        }

        if (lookup.ClosedByDefinition.TryGetValue(openServiceType, out var closedTypes))
        {
            if (_thirdPartyContainer)
            {
                return true;
            }

            foreach (var closedType in closedTypes)
            {
                if (StartsWith(closedType.GenericTypeArguments, leadingArguments))
                {
                    return true;
                }
            }
        }

        return ContainerHas(catchAllServiceType);
    }

    /// <summary>
    /// How many of the pipeline behaviors the container resolves for <paramref name="closedBehaviorService"/>, counted
    /// from the first, were registered before the first <c>AddCaesar</c> call. Those run outside the built-in stages.
    /// </summary>
    public int CountBehaviorsRegisteredBeforeFirstCall(Type closedBehaviorService)
    {
        var count = 0;
        foreach (var descriptor in _lookup.Value.PipelineBehaviors)
        {
            var applies = descriptor.ServiceType == closedBehaviorService
                || (descriptor.ServiceType == typeof(IPipelineBehavior<,>) && CanClose(descriptor, closedBehaviorService.GenericTypeArguments));

            if (!applies)
            {
                continue;
            }

            if (!_state.IsBehaviorRegisteredBeforeFirstCall(descriptor))
            {
                break;
            }

            count++;
        }

        return count;
    }

    private bool ContainerHas(Type closedServiceType) => _container?.IsService(closedServiceType) == true;

    private static bool StartsWith(Type[] arguments, ReadOnlySpan<Type> leading)
    {
        if (arguments.Length < leading.Length)
        {
            return false;
        }

        for (var i = 0; i < leading.Length; i++)
        {
            if (arguments[i] != leading[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <see langword="true"/> when <paramref name="closedBehaviorImplementation"/>, one of Caesar's built-in behaviors
    /// closed over a request, was registered as an <see cref="IPipelineBehavior{TRequest, TResponse}"/> by the user,
    /// either closed for this request or as its open-generic definition.
    /// </summary>
    public bool IsRegisteredAsPipelineBehavior(Type closedServiceType, Type closedBehaviorImplementation)
    {
        var lookup = _lookup.Value;

        if (lookup.Descriptors.TryGetValue(closedServiceType, out var closed))
        {
            foreach (var descriptor in closed)
            {
                if (descriptor.ImplementationType == closedBehaviorImplementation
                    || descriptor.ImplementationInstance?.GetType() == closedBehaviorImplementation)
                {
                    return true;
                }
            }
        }

        if (lookup.Descriptors.TryGetValue(closedServiceType.GetGenericTypeDefinition(), out var open))
        {
            var openBehavior = closedBehaviorImplementation.GetGenericTypeDefinition();
            foreach (var descriptor in open)
            {
                if (descriptor.ImplementationType == openBehavior)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Closes the descriptor's open implementation the way the container does: positionally, honouring constraints.</summary>
    private static bool CanClose(ServiceDescriptor openDescriptor, Type[] arguments)
    {
        // An open-generic service needs an open implementation type; anything else is rejected when the container is
        // built, so there is nothing to predict here.
        return openDescriptor.ImplementationType is not { IsGenericTypeDefinition: true } implementation
            || TryClose(implementation, arguments, out _);
    }

    private static bool TryClose(Type openImplementation, Type[] arguments, [NotNullWhen(true)] out Type? closed)
    {
        try
        {
            closed = openImplementation.MakeGenericType(arguments);
            return true;
        }
        catch (ArgumentException)
        {
            // Wrong arity or a violated constraint: the container cannot close it either.
            closed = null;
            return false;
        }
    }

    /// <summary>
    /// Caesar's service types in the collection, indexed once. Keyed registrations are skipped, since they never answer an
    /// unkeyed request, and so is everything that is not a Caesar service type, so a large application pays only for its
    /// Caesar registrations.
    /// </summary>
    private sealed class Lookup
    {
        public Lookup(IServiceCollection services)
        {
            // Indexed rather than enumerated, so a collection changed while the first dispatch reads it does not throw.
            for (var i = 0; i < services.Count; i++)
            {
                var descriptor = services[i];
                if (descriptor.IsKeyedService)
                {
                    continue;
                }

                var serviceType = descriptor.ServiceType;
                var closed = serviceType.IsConstructedGenericType;
                var definition = closed ? serviceType.GetGenericTypeDefinition() : serviceType;
                if (!IndexedDefinitions.Contains(definition))
                {
                    continue;
                }

                Add(Descriptors, serviceType, descriptor);

                if (closed)
                {
                    Add(ClosedByDefinition, definition, serviceType);
                }

                if (definition == typeof(IPipelineBehavior<,>))
                {
                    PipelineBehaviors.Add(descriptor);
                }
            }
        }

        /// <summary>Pipeline behavior registrations, open and closed, in registration order.</summary>
        public List<ServiceDescriptor> PipelineBehaviors { get; } = [];

        /// <summary>Descriptors by service type, in registration order. Open generics are keyed by their definition.</summary>
        public Dictionary<Type, List<ServiceDescriptor>> Descriptors { get; } = [];

        /// <summary>Closed generic service types by their generic type definition.</summary>
        public Dictionary<Type, List<Type>> ClosedByDefinition { get; } = [];

        private static void Add<TValue>(Dictionary<Type, List<TValue>> map, Type key, TValue value)
        {
            if (!map.TryGetValue(key, out var list))
            {
                list = [];
                map.Add(key, list);
            }

            list.Add(value);
        }
    }
}
