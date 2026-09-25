using Microsoft.Extensions.DependencyInjection;

namespace Caesar.DependencyInjection;

/// <summary>The options of <see cref="CaesarServiceConfiguration"/> that apply to the whole container rather than to one scan.</summary>
[Flags]
internal enum GlobalOptions
{
    None = 0,
    MediatorImplementationType = 1 << 0,
    NotificationPublisher = 1 << 1,
    NotificationPublisherType = 1 << 2,
    MediatorLifetime = 1 << 3,
    RequestExceptionActionProcessorStrategy = 1 << 4,
    BypassExceptionHandlingOnCallerCancellation = 1 << 5,
}

/// <summary>
/// The global options chosen by the first <c>AddCaesar</c> call on a service collection. It is stored in that
/// collection, so every later call on the same collection finds it and is checked against it.
/// </summary>
internal sealed class CaesarRegistrationState
{
    private readonly HashSet<ServiceDescriptor> _behaviorsBeforeFirstCall;

    private CaesarRegistrationState(CaesarServiceConfiguration configuration, HashSet<ServiceDescriptor> behaviorsBeforeFirstCall)
    {
        _behaviorsBeforeFirstCall = behaviorsBeforeFirstCall;
        MediatorImplementationType = configuration.MediatorImplementationType;
        NotificationPublisher = configuration.NotificationPublisher;
        NotificationPublisherType = configuration.NotificationPublisherType;
        MediatorLifetime = configuration.MediatorLifetime;
        RequestExceptionActionProcessorStrategy = configuration.RequestExceptionActionProcessorStrategy;
        BypassExceptionHandlingOnCallerCancellation = configuration.BypassExceptionHandlingOnCallerCancellation;
    }

    public Type MediatorImplementationType { get; }

    public INotificationPublisher? NotificationPublisher { get; }

    public Type NotificationPublisherType { get; }

    public ServiceLifetime MediatorLifetime { get; }

    public RequestExceptionActionProcessorStrategy RequestExceptionActionProcessorStrategy { get; }

    public bool BypassExceptionHandlingOnCallerCancellation { get; }

    /// <summary>
    /// <see langword="true"/> when <paramref name="descriptor"/> is a pipeline behavior that was in the collection before
    /// the first <c>AddCaesar</c> call. Such behaviors run outside Caesar's built-in stages, as they did when the
    /// built-in behaviors were appended to the collection by that call.
    /// </summary>
    public bool IsBehaviorRegisteredBeforeFirstCall(ServiceDescriptor descriptor) => _behaviorsBeforeFirstCall.Contains(descriptor);

    /// <summary>
    /// Returns the state of the first <c>AddCaesar</c> call on <paramref name="services"/>, recording
    /// <paramref name="configuration"/> as that state when this is the first call.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A later call explicitly set a global option to a value other than the one the first call used.
    /// Options a later call leaves at their defaults are not compared, so a module can call
    /// <c>AddCaesar(cfg =&gt; cfg.RegisterServicesFromAssembly(...))</c> on its own.
    /// </exception>
    public static CaesarRegistrationState Register(IServiceCollection services, CaesarServiceConfiguration configuration)
    {
        foreach (var descriptor in services)
        {
            if (!descriptor.IsKeyedService
                && descriptor.ServiceType == typeof(CaesarRegistrationState)
                && descriptor.ImplementationInstance is CaesarRegistrationState existing)
            {
                existing.EnsureCompatible(configuration);
                return existing;
            }
        }

        var behaviors = new HashSet<ServiceDescriptor>(ReferenceEqualityComparer.Instance);
        foreach (var descriptor in services)
        {
            if (!descriptor.IsKeyedService && IsPipelineBehaviorService(descriptor.ServiceType))
            {
                behaviors.Add(descriptor);
            }
        }

        var state = new CaesarRegistrationState(configuration, behaviors);
        services.Add(ServiceDescriptor.Singleton(state));
        return state;
    }

    /// <summary><see langword="true"/> for <see cref="IPipelineBehavior{TRequest, TResponse}"/>, open or closed.</summary>
    public static bool IsPipelineBehaviorService(Type serviceType)
        => serviceType == typeof(IPipelineBehavior<,>)
           || (serviceType.IsConstructedGenericType && serviceType.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));

    private void EnsureCompatible(CaesarServiceConfiguration configuration)
    {
        var set = configuration.ExplicitGlobalOptions;

        Check(set, GlobalOptions.MediatorImplementationType, MediatorImplementationType, configuration.MediatorImplementationType);
        CheckPublisher(set, configuration);
        Check(set, GlobalOptions.MediatorLifetime, MediatorLifetime, configuration.MediatorLifetime);
        Check(set, GlobalOptions.RequestExceptionActionProcessorStrategy, RequestExceptionActionProcessorStrategy, configuration.RequestExceptionActionProcessorStrategy);
        Check(set, GlobalOptions.BypassExceptionHandlingOnCallerCancellation, BypassExceptionHandlingOnCallerCancellation, configuration.BypassExceptionHandlingOnCallerCancellation);
    }

    /// <summary>
    /// The publisher in effect is an instance's type, or the type when no instance is set. Two calls that end up with the
    /// same publisher type agree, however they said it; the first call's publisher is the one registered.
    /// </summary>
    private void CheckPublisher(GlobalOptions set, CaesarServiceConfiguration configuration)
    {
        if ((set & (GlobalOptions.NotificationPublisher | GlobalOptions.NotificationPublisherType)) == 0)
        {
            return;
        }

        var first = NotificationPublisher?.GetType() ?? NotificationPublisherType;
        var later = configuration.NotificationPublisher?.GetType() ?? configuration.NotificationPublisherType;
        if (first == later)
        {
            return;
        }

        throw new InvalidOperationException(
            $"AddCaesar was called more than once with different notification publishers "
            + $"({nameof(CaesarServiceConfiguration)}.{nameof(CaesarServiceConfiguration.NotificationPublisher)} / {nameof(CaesarServiceConfiguration.NotificationPublisherType)}): "
            + $"the first call used {TypeNames.Of(first, qualified: true)}, a later call set {TypeNames.Of(later, qualified: true)}. "
            + "Global options apply to the whole container, so set them in one AddCaesar call (later calls can leave them at their defaults) "
            + "or give them the same value in every call.");
    }

    private static void Check<T>(GlobalOptions set, GlobalOptions option, T first, T later)
    {
        if ((set & option) == 0 || EqualityComparer<T>.Default.Equals(first, later))
        {
            return;
        }

        throw new InvalidOperationException(
            $"AddCaesar was called more than once with different values for {nameof(CaesarServiceConfiguration)}.{option}: "
            + $"the first call used {Describe(first)}, a later call set {Describe(later)}. "
            + "Global options apply to the whole container, so set them in one AddCaesar call (later calls can leave them at their defaults) "
            + "or give them the same value in every call.");
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        Type type => TypeNames.Of(type, qualified: true),
        INotificationPublisher publisher => $"an instance of {TypeNames.Of(publisher.GetType(), qualified: true)}",
        _ => value.ToString() ?? string.Empty,
    };
}
