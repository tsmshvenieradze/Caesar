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

    // Global options apply to the whole container, not to one AddCaesar call. Their setters record that they were
    // set, so that a later AddCaesar call can tell a deliberate, conflicting value from one merely left at its default.
    private Type _mediatorImplementationType = typeof(Mediator);
    private INotificationPublisher? _notificationPublisher;
    private Type _notificationPublisherType = typeof(ForeachAwaitPublisher);
    private ServiceLifetime _mediatorLifetime = ServiceLifetime.Scoped;
    private RequestExceptionActionProcessorStrategy _requestExceptionActionProcessorStrategy = RequestExceptionActionProcessorStrategy.ApplyForUnhandledExceptions;
    private bool _bypassExceptionHandlingOnCallerCancellation;

    /// <summary>Filters the types found while scanning. Return <see langword="false"/> to skip a type. Default: accept everything.</summary>
    public Func<Type, bool> TypeEvaluator { get; set; } = static _ => true;

    /// <summary>
    /// The <see cref="IMediator"/> implementation to register. Default: <see cref="Mediator"/>.
    /// A global option: when <c>AddCaesar</c> is called more than once, a later call may not set a different value.
    /// </summary>
    public Type MediatorImplementationType
    {
        get => _mediatorImplementationType;
        set
        {
            _mediatorImplementationType = value;
            ExplicitGlobalOptions |= GlobalOptions.MediatorImplementationType;
        }
    }

    /// <summary>
    /// A ready-made publisher instance. Takes precedence over <see cref="NotificationPublisherType"/> when set.
    /// A global option: when <c>AddCaesar</c> is called more than once, a later call may not set a different value.
    /// </summary>
    public INotificationPublisher? NotificationPublisher
    {
        get => _notificationPublisher;
        set
        {
            _notificationPublisher = value;
            ExplicitGlobalOptions |= GlobalOptions.NotificationPublisher;
        }
    }

    /// <summary>
    /// The publisher type to register. Default: <see cref="ForeachAwaitPublisher"/>.
    /// A global option: when <c>AddCaesar</c> is called more than once, a later call may not set a different value.
    /// </summary>
    public Type NotificationPublisherType
    {
        get => _notificationPublisherType;
        set
        {
            _notificationPublisherType = value;
            ExplicitGlobalOptions |= GlobalOptions.NotificationPublisherType;
        }
    }

    /// <summary>
    /// Lifetime for every scanned handler, processor, exception handler and exception action. Default:
    /// <see cref="ServiceLifetime.Transient"/>. A processor also added with <c>AddRequestPreProcessor</c> or
    /// <c>AddRequestPostProcessor</c> keeps the lifetime given there.
    /// </summary>
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Transient;

    /// <summary>
    /// Lifetime for <see cref="IMediator"/>, <see cref="ISender"/>, <see cref="IPublisher"/> and
    /// <see cref="INotificationPublisher"/>. Default: <see cref="ServiceLifetime.Scoped"/>, so that a singleton
    /// capturing the mediator is reported by container validation at startup instead of failing on the first request.
    /// Set to <see cref="ServiceLifetime.Transient"/> when the mediator must be resolvable straight from the root
    /// provider, for example in a short console application with no scoped dependencies.
    /// A global option: when <c>AddCaesar</c> is called more than once, a later call may not set a different value.
    /// </summary>
    /// <remarks>
    /// Avoid <see cref="ServiceLifetime.Singleton"/> in an application that uses scopes, such as a web application. A
    /// singleton mediator resolves every handler and its dependencies from the root provider, even when
    /// <see cref="ISender"/> is injected inside a scope: scoped services such as a <c>DbContext</c> or the current user
    /// are then shared by every request and every user, and disposable transient handlers are kept by the root provider
    /// until the application shuts down. A singleton that needs to send requests should inject
    /// <see cref="IServiceScopeFactory"/> and resolve <see cref="ISender"/> from a scope it creates.
    /// </remarks>
    public ServiceLifetime MediatorLifetime
    {
        get => _mediatorLifetime;
        set
        {
            _mediatorLifetime = value;
            ExplicitGlobalOptions |= GlobalOptions.MediatorLifetime;
        }
    }

    /// <summary>
    /// When <see langword="true"/> (default) scanned <see cref="IRequestPreProcessor{TRequest}"/> and
    /// <see cref="IRequestPostProcessor{TRequest, TResponse}"/> implementations are registered automatically.
    /// Set to <see langword="false"/> to register processors only explicitly via <c>AddRequestPreProcessor</c> / <c>AddRequestPostProcessor</c>.
    /// </summary>
    public bool AutoRegisterRequestProcessors { get; set; } = true;

    /// <summary>
    /// Where exception actions sit in the pipeline. Default: <see cref="RequestExceptionActionProcessorStrategy.ApplyForUnhandledExceptions"/>.
    /// A global option: when <c>AddCaesar</c> is called more than once, a later call may not set a different value.
    /// </summary>
    public RequestExceptionActionProcessorStrategy RequestExceptionActionProcessorStrategy
    {
        get => _requestExceptionActionProcessorStrategy;
        set
        {
            _requestExceptionActionProcessorStrategy = value;
            ExplicitGlobalOptions |= GlobalOptions.RequestExceptionActionProcessorStrategy;
        }
    }

    /// <summary>
    /// When <see langword="true"/>, an <see cref="OperationCanceledException"/> thrown while the caller's cancellation token
    /// is cancelled propagates without reaching exception handlers or exception actions, so a catch-all handler cannot
    /// turn an aborted request into a fallback response. Cancellation from any other token, such as a timeout a behavior
    /// adds, is still routed. Default: <see langword="false"/>: cancellations reach handlers and actions like any other
    /// exception, as in MediatR, and each handler receives the token to tell them apart itself.
    /// A global option: when <c>AddCaesar</c> is called more than once, a later call may not set a different value.
    /// </summary>
    public bool BypassExceptionHandlingOnCallerCancellation
    {
        get => _bypassExceptionHandlingOnCallerCancellation;
        set
        {
            _bypassExceptionHandlingOnCallerCancellation = value;
            ExplicitGlobalOptions |= GlobalOptions.BypassExceptionHandlingOnCallerCancellation;
        }
    }

    /// <summary>Assemblies that will be scanned for handlers.</summary>
    public IReadOnlyList<Assembly> AssembliesToRegister => _assemblies;

    /// <summary>The global options this configuration set explicitly, whatever the value.</summary>
    internal GlobalOptions ExplicitGlobalOptions { get; private set; }

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
