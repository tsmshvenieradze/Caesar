using Microsoft.Extensions.DependencyInjection;

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

    private ScannedOpenGenericHandlers? _scannedOpenGenerics;
    private bool _scannedOpenGenericsLookedUp;

    public ScanContext(IServiceCollection services, CaesarServiceConfiguration configuration)
    {
        Services = services;
        Lifetime = configuration.Lifetime;

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

    /// <summary><see langword="true"/> when an earlier scan, in this or a previous <c>AddCaesar</c> call, added <paramref name="descriptor"/>.</summary>
    public bool WasScanned(ServiceDescriptor descriptor) => ScannedOpenGenerics?.Contains(descriptor) == true;

    /// <summary>Remembers that scanning added the open-generic single-handler <paramref name="descriptor"/>.</summary>
    public void RecordScanned(ServiceDescriptor descriptor)
    {
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
}
