using System.Reflection;
using Caesar.NotificationPublishers;
using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.DependencyInjection;

/// <summary>
/// Options for <c>AddCaesar</c>: which assemblies to scan, which behaviors to add, and how services are registered.
/// </summary>
public sealed class CaesarServiceConfiguration
{
    private readonly List<Assembly> _assemblies = [];

    /// <summary>Filters the types found while scanning. Return <see langword="false"/> to skip a type. Default: accept everything.</summary>
    public Func<Type, bool> TypeEvaluator { get; set; } = static _ => true;

    /// <summary>The <see cref="IMediator"/> implementation to register. Default: <see cref="Mediator"/>.</summary>
    public Type MediatorImplementationType { get; set; } = typeof(Mediator);

    /// <summary>
    /// A ready-made publisher instance. Takes precedence over <see cref="NotificationPublisherType"/> when set.
    /// </summary>
    public INotificationPublisher? NotificationPublisher { get; set; }

    /// <summary>The publisher type to register. Default: <see cref="ForeachAwaitPublisher"/>.</summary>
    public Type NotificationPublisherType { get; set; } = typeof(ForeachAwaitPublisher);

    /// <summary>Lifetime for every scanned handler, processor and exception handler. Default: <see cref="ServiceLifetime.Transient"/>.</summary>
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Transient;

    /// <summary>
    /// Lifetime for <see cref="IMediator"/>, <see cref="ISender"/>, <see cref="IPublisher"/> and
    /// <see cref="INotificationPublisher"/>. Default: <see cref="ServiceLifetime.Scoped"/>, so that a singleton
    /// capturing the mediator is reported by container validation at startup instead of failing on the first request.
    /// Set to <see cref="ServiceLifetime.Transient"/> or <see cref="ServiceLifetime.Singleton"/> when the mediator
    /// must be resolvable straight from the root provider, for example in a console application.
    /// </summary>
    public ServiceLifetime MediatorLifetime { get; set; } = ServiceLifetime.Scoped;

    /// <summary>
    /// When <see langword="true"/> (default) scanned <see cref="IRequestPreProcessor{TRequest}"/> and
    /// <see cref="IRequestPostProcessor{TRequest, TResponse}"/> implementations are registered automatically.
    /// Set to <see langword="false"/> to register processors only explicitly via <c>AddRequestPreProcessor</c> / <c>AddRequestPostProcessor</c>.
    /// </summary>
    public bool AutoRegisterRequestProcessors { get; set; } = true;

    /// <summary>Where exception actions sit in the pipeline. Default: <see cref="RequestExceptionActionProcessorStrategy.ApplyForUnhandledExceptions"/>.</summary>
    public RequestExceptionActionProcessorStrategy RequestExceptionActionProcessorStrategy { get; set; }
        = RequestExceptionActionProcessorStrategy.ApplyForUnhandledExceptions;

    /// <summary>Assemblies that will be scanned for handlers.</summary>
    public IReadOnlyList<Assembly> AssembliesToRegister => _assemblies;

    /// <summary>Behaviors added explicitly, in the order they will run (first is outermost).</summary>
    internal List<ServiceDescriptor> BehaviorsToRegister { get; } = [];

    /// <summary>Stream behaviors added explicitly, in the order they will run (first is outermost).</summary>
    internal List<ServiceDescriptor> StreamBehaviorsToRegister { get; } = [];

    /// <summary>Pre-processors added explicitly.</summary>
    internal List<ServiceDescriptor> RequestPreProcessorsToRegister { get; } = [];

    /// <summary>Post-processors added explicitly.</summary>
    internal List<ServiceDescriptor> RequestPostProcessorsToRegister { get; } = [];

    /// <summary>Adds the assembly that contains <typeparamref name="T"/> to the scan list.</summary>
    public CaesarServiceConfiguration RegisterServicesFromAssemblyContaining<T>() => RegisterServicesFromAssembly(typeof(T).Assembly);

    /// <summary>Adds the assembly that contains <paramref name="type"/> to the scan list.</summary>
    public CaesarServiceConfiguration RegisterServicesFromAssemblyContaining(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return RegisterServicesFromAssembly(type.Assembly);
    }

    /// <summary>Adds <paramref name="assembly"/> to the scan list.</summary>
    public CaesarServiceConfiguration RegisterServicesFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (!_assemblies.Contains(assembly))
        {
            _assemblies.Add(assembly);
        }

        return this;
    }

    /// <summary>Adds several assemblies to the scan list.</summary>
    public CaesarServiceConfiguration RegisterServicesFromAssemblies(params IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        foreach (var assembly in assemblies)
        {
            RegisterServicesFromAssembly(assembly);
        }

        return this;
    }

    // ---- Behaviors -------------------------------------------------------------------------------------------

    /// <summary>Adds a closed behavior. The service type is every <see cref="IPipelineBehavior{TRequest, TResponse}"/> it implements.</summary>
    public CaesarServiceConfiguration AddBehavior<TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Transient)
        where TImplementation : class
        => AddBehavior(typeof(TImplementation), lifetime);

    /// <summary>Adds a closed behavior for one specific service type.</summary>
    public CaesarServiceConfiguration AddBehavior<TService, TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Transient)
        where TService : class
        where TImplementation : class, TService
        => AddBehavior(typeof(TService), typeof(TImplementation), lifetime);

    /// <summary>Adds a closed behavior. The service type is every <see cref="IPipelineBehavior{TRequest, TResponse}"/> it implements.</summary>
    public CaesarServiceConfiguration AddBehavior(Type implementationType, ServiceLifetime lifetime = ServiceLifetime.Transient)
        => AddClosedImplementation(implementationType, typeof(IPipelineBehavior<,>), BehaviorsToRegister, lifetime);

    /// <summary>Adds a closed behavior for one specific service type.</summary>
    public CaesarServiceConfiguration AddBehavior(Type serviceType, Type implementationType, ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(implementationType);
        BehaviorsToRegister.Add(new ServiceDescriptor(serviceType, implementationType, lifetime));
        return this;
    }

    /// <summary>
    /// Adds an open-generic behavior such as <c>typeof(LoggingBehavior&lt;,&gt;)</c> that applies to every request.
    /// The type must implement <see cref="IPipelineBehavior{TRequest, TResponse}"/> with its own two type parameters.
    /// </summary>
    public CaesarServiceConfiguration AddOpenBehavior(Type openBehaviorType, ServiceLifetime lifetime = ServiceLifetime.Transient)
        => AddOpenImplementation(openBehaviorType, typeof(IPipelineBehavior<,>), BehaviorsToRegister, lifetime);

    /// <summary>Adds several open-generic behaviors.</summary>
    public CaesarServiceConfiguration AddOpenBehaviors(IEnumerable<Type> openBehaviorTypes, ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        ArgumentNullException.ThrowIfNull(openBehaviorTypes);
        foreach (var type in openBehaviorTypes)
        {
            AddOpenBehavior(type, lifetime);
        }

        return this;
    }

    // ---- Stream behaviors ------------------------------------------------------------------------------------

    /// <summary>Adds a closed stream behavior.</summary>
    public CaesarServiceConfiguration AddStreamBehavior<TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Transient)
        where TImplementation : class
        => AddStreamBehavior(typeof(TImplementation), lifetime);

    /// <summary>Adds a closed stream behavior.</summary>
    public CaesarServiceConfiguration AddStreamBehavior(Type implementationType, ServiceLifetime lifetime = ServiceLifetime.Transient)
        => AddClosedImplementation(implementationType, typeof(IStreamPipelineBehavior<,>), StreamBehaviorsToRegister, lifetime);

    /// <summary>Adds a closed stream behavior for one specific service type.</summary>
    public CaesarServiceConfiguration AddStreamBehavior(Type serviceType, Type implementationType, ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(implementationType);
        StreamBehaviorsToRegister.Add(new ServiceDescriptor(serviceType, implementationType, lifetime));
        return this;
    }

    /// <summary>Adds an open-generic stream behavior that applies to every stream request.</summary>
    public CaesarServiceConfiguration AddOpenStreamBehavior(Type openBehaviorType, ServiceLifetime lifetime = ServiceLifetime.Transient)
        => AddOpenImplementation(openBehaviorType, typeof(IStreamPipelineBehavior<,>), StreamBehaviorsToRegister, lifetime);

    // ---- Processors ------------------------------------------------------------------------------------------

    /// <summary>Adds a closed pre-processor.</summary>
    public CaesarServiceConfiguration AddRequestPreProcessor<TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Transient)
        where TImplementation : class
        => AddRequestPreProcessor(typeof(TImplementation), lifetime);

    /// <summary>Adds a closed pre-processor.</summary>
    public CaesarServiceConfiguration AddRequestPreProcessor(Type implementationType, ServiceLifetime lifetime = ServiceLifetime.Transient)
        => AddClosedImplementation(implementationType, typeof(IRequestPreProcessor<>), RequestPreProcessorsToRegister, lifetime);

    /// <summary>Adds a closed pre-processor for one specific service type.</summary>
    public CaesarServiceConfiguration AddRequestPreProcessor(Type serviceType, Type implementationType, ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(implementationType);
        RequestPreProcessorsToRegister.Add(new ServiceDescriptor(serviceType, implementationType, lifetime));
        return this;
    }

    /// <summary>Adds an open-generic pre-processor that applies to every request.</summary>
    public CaesarServiceConfiguration AddOpenRequestPreProcessor(Type openProcessorType, ServiceLifetime lifetime = ServiceLifetime.Transient)
        => AddOpenImplementation(openProcessorType, typeof(IRequestPreProcessor<>), RequestPreProcessorsToRegister, lifetime);

    /// <summary>Adds a closed post-processor.</summary>
    public CaesarServiceConfiguration AddRequestPostProcessor<TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Transient)
        where TImplementation : class
        => AddRequestPostProcessor(typeof(TImplementation), lifetime);

    /// <summary>Adds a closed post-processor.</summary>
    public CaesarServiceConfiguration AddRequestPostProcessor(Type implementationType, ServiceLifetime lifetime = ServiceLifetime.Transient)
        => AddClosedImplementation(implementationType, typeof(IRequestPostProcessor<,>), RequestPostProcessorsToRegister, lifetime);

    /// <summary>Adds a closed post-processor for one specific service type.</summary>
    public CaesarServiceConfiguration AddRequestPostProcessor(Type serviceType, Type implementationType, ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(implementationType);
        RequestPostProcessorsToRegister.Add(new ServiceDescriptor(serviceType, implementationType, lifetime));
        return this;
    }

    /// <summary>Adds an open-generic post-processor that applies to every request.</summary>
    public CaesarServiceConfiguration AddOpenRequestPostProcessor(Type openProcessorType, ServiceLifetime lifetime = ServiceLifetime.Transient)
        => AddOpenImplementation(openProcessorType, typeof(IRequestPostProcessor<,>), RequestPostProcessorsToRegister, lifetime);

    // ---- Helpers ---------------------------------------------------------------------------------------------

    private CaesarServiceConfiguration AddClosedImplementation(Type implementationType, Type openServiceType, List<ServiceDescriptor> target, ServiceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(implementationType);

        var serviceTypes = implementationType.FindClosedInterfaces(openServiceType).ToList();
        if (serviceTypes.Count == 0)
        {
            throw new InvalidOperationException($"{implementationType.Name} must implement {openServiceType.FullName}.");
        }

        foreach (var serviceType in serviceTypes)
        {
            target.Add(new ServiceDescriptor(serviceType, implementationType, lifetime));
        }

        return this;
    }

    private CaesarServiceConfiguration AddOpenImplementation(Type openImplementationType, Type openServiceType, List<ServiceDescriptor> target, ServiceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(openImplementationType);

        if (!openImplementationType.IsGenericTypeDefinition)
        {
            throw new InvalidOperationException($"{openImplementationType.Name} must be an open generic type definition, e.g. typeof(LoggingBehavior<,>).");
        }

        if (!openImplementationType.ImplementsOpenGenericWithOwnParameters(openServiceType))
        {
            throw new InvalidOperationException(
                $"{openImplementationType.Name} must implement {openServiceType.Name} using its own generic parameters in the same order, e.g. class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>.");
        }

        target.Add(new ServiceDescriptor(openServiceType, openImplementationType, lifetime));
        return this;
    }
}
