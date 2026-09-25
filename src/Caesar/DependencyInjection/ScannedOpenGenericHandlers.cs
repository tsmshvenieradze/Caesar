using Microsoft.Extensions.DependencyInjection;

namespace Caesar.DependencyInjection;

/// <summary>
/// Registration-time record of the open-generic single-handler descriptors that scanning added, kept in the service
/// collection itself so that a later <c>AddCaesar</c> call can tell them apart from registrations the user made by hand.
/// A second, different scanned open generic for the same interface is an error; a manual registration keeps precedence.
/// </summary>
internal sealed class ScannedOpenGenericHandlers
{
    private readonly HashSet<ServiceDescriptor> _descriptors = new(ReferenceEqualityComparer.Instance);

    /// <summary><see langword="true"/> when scanning added <paramref name="descriptor"/>, rather than the user.</summary>
    public bool Contains(ServiceDescriptor descriptor) => _descriptors.Contains(descriptor);

    public void Add(ServiceDescriptor descriptor) => _descriptors.Add(descriptor);

    /// <summary>Returns the record held by <paramref name="services"/>, or <see langword="null"/> when scanning has not added one yet.</summary>
    public static ScannedOpenGenericHandlers? Find(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(ScannedOpenGenericHandlers) && !descriptor.IsKeyedService)
            {
                return descriptor.ImplementationInstance as ScannedOpenGenericHandlers;
            }
        }

        return null;
    }

    /// <summary>Returns the record held by <paramref name="services"/>, adding one on first use.</summary>
    public static ScannedOpenGenericHandlers GetOrAdd(IServiceCollection services)
    {
        if (Find(services) is { } existing)
        {
            return existing;
        }

        var created = new ScannedOpenGenericHandlers();
        services.Add(new ServiceDescriptor(typeof(ScannedOpenGenericHandlers), created));
        return created;
    }
}
