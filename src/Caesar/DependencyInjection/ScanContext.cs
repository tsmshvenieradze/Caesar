using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Caesar.DependencyInjection;

/// <summary>State shared by the registrations of one <c>AddCaesar</c> scan.</summary>
internal sealed class ScanContext
{
    /// <summary>
    /// Processors added with <c>AddRequestPreProcessor</c> / <c>AddRequestPostProcessor</c>, keyed by service and implementation
    /// type. When scanning finds one of them it registers the explicit descriptor in its place, so the explicit lifetime wins
    /// and the processor keeps its scanned position; the explicit registration that follows is then a no-op.
    /// </summary>
    private readonly Dictionary<(Type Service, Type Implementation), ServiceDescriptor>? _explicitProcessors;

    private readonly HashSet<Type> _registeredServiceTypes;
    private readonly HashSet<(Type Service, Type Implementation)> _registeredEnumerables;

    private ScannedOpenGenericHandlers? _scannedOpenGenerics;
    private bool _scannedOpenGenericsLookedUp;

    public ScanContext(IServiceCollection services, CaesarServiceConfiguration configuration)
    {
        Services = services;
        Lifetime = configuration.Lifetime;
        _registeredServiceTypes = new HashSet<Type>(services.Count);
        _registeredEnumerables = new HashSet<(Type Service, Type Implementation)>(services.Count);

        foreach (var existing in services)
        {
            if (existing.IsKeyedService)
            {
                continue;
            }

            _registeredServiceTypes.Add(existing.ServiceType);
            if (GetImplementationType(existing) is { } existingImplementation)
            {
                _registeredEnumerables.Add((existing.ServiceType, existingImplementation));
            }
        }

        if (!configuration.AutoRegisterRequestProcessors)
        {
            return;
        }

        foreach (var descriptor in configuration.RequestPreProcessorsToRegister.Concat(configuration.RequestPostProcessorsToRegister))
        {
            if (!descriptor.IsKeyedService && descriptor.ImplementationType is { } implementationType)
            {
                // First wins, as TryAddEnumerable would have kept it.
                (_explicitProcessors ??= []).TryAdd((descriptor.ServiceType, implementationType), descriptor);
            }
        }
    }

    public IServiceCollection Services { get; }

    /// <summary>The lifetime for scanned registrations: <see cref="CaesarServiceConfiguration.Lifetime"/>.</summary>
    public ServiceLifetime Lifetime { get; }

    /// <summary>The explicitly added descriptor for this service and implementation, or a new one with the scan lifetime.</summary>
    public ServiceDescriptor ExplicitOrNew(Type serviceType, Type implementationType)
        => _explicitProcessors is not null && _explicitProcessors.TryGetValue((serviceType, implementationType), out var explicitDescriptor)
            ? explicitDescriptor
            : new ServiceDescriptor(serviceType, implementationType, Lifetime);

    public void TryAdd(Type serviceType, Type implementationType)
    {
        if (_registeredServiceTypes.Add(serviceType))
        {
            Services.Add(new ServiceDescriptor(serviceType, implementationType, Lifetime));
        }
    }

    public void TryAddEnumerable(Type serviceType, Type implementationType)
    {
        if (_registeredEnumerables.Add((serviceType, implementationType)))
        {
            _registeredServiceTypes.Add(serviceType);
            Services.Add(ExplicitOrNew(serviceType, implementationType));
        }
    }

    public void TryAddEnumerable(ServiceDescriptor descriptor)
    {
        if (!descriptor.IsKeyedService
            && descriptor.ImplementationType is { } implementationType
            && implementationType != typeof(object)
            && implementationType != descriptor.ServiceType)
        {
            if (_registeredEnumerables.Add((descriptor.ServiceType, implementationType)))
            {
                _registeredServiceTypes.Add(descriptor.ServiceType);
                Services.Add(descriptor);
            }

            return;
        }

        Services.TryAddEnumerable(descriptor);
    }

    /// <summary><see langword="true"/> when an earlier scan, in this or a previous <c>AddCaesar</c> call, added <paramref name="descriptor"/>.</summary>
    public bool WasScanned(ServiceDescriptor descriptor) => ScannedOpenGenerics?.Contains(descriptor) == true;

    /// <summary>Remembers that scanning added the open-generic single-handler <paramref name="descriptor"/>.</summary>
    public void RecordScanned(ServiceDescriptor descriptor)
    {
        _registeredServiceTypes.Add(descriptor.ServiceType);
        _scannedOpenGenerics = ScannedOpenGenerics ?? ScannedOpenGenericHandlers.GetOrAdd(Services);
        _scannedOpenGenerics.Add(descriptor);
    }

    /// <summary>Looked up lazily: most scans find no open-generic single handler and never add the record to the collection.</summary>
    private ScannedOpenGenericHandlers? ScannedOpenGenerics
    {
        get
        {
            if (!_scannedOpenGenericsLookedUp)
            {
                _scannedOpenGenerics = ScannedOpenGenericHandlers.Find(Services);
                _scannedOpenGenericsLookedUp = true;
            }

            return _scannedOpenGenerics;
        }
    }

    private static Type? GetImplementationType(ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationType is { } implementationType)
        {
            return implementationType;
        }

        if (descriptor.ImplementationInstance is { } instance)
        {
            return instance.GetType();
        }

        if (descriptor.ImplementationFactory is { } factory)
        {
            return factory.GetType().GenericTypeArguments[1];
        }

        return null;
    }
}
