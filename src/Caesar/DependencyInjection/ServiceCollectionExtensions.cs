using System.Reflection;
using Caesar;
using Caesar.DependencyInjection;

// ReSharper disable once CheckNamespace -- placed here so `AddCaesar` is discoverable next to `AddLogging`, `AddOptions`, etc.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers Caesar with <see cref="IServiceCollection"/>.
/// </summary>
public static class CaesarServiceCollectionExtensions
{
    /// <summary>
    /// Registers the mediator, scans the configured assemblies for handlers and adds the configured behaviors.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures assemblies to scan, behaviors, lifetime and the publish strategy.</param>
    /// <exception cref="ArgumentException">No assembly was registered for scanning.</exception>
    public static IServiceCollection AddCaesar(this IServiceCollection services, Action<CaesarServiceConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var configuration = new CaesarServiceConfiguration();
        configure(configuration);
        return services.AddCaesar(configuration);
    }

    /// <summary>
    /// Registers the mediator and scans <paramref name="assemblies"/> for handlers using default options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">Assemblies to scan.</param>
    public static IServiceCollection AddCaesar(this IServiceCollection services, params IEnumerable<Assembly> assemblies)
        => services.AddCaesar(configuration => configuration.RegisterServicesFromAssemblies(assemblies));

    /// <summary>
    /// Registers the mediator using a prepared <see cref="CaesarServiceConfiguration"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <exception cref="ArgumentException">No assembly was registered for scanning.</exception>
    public static IServiceCollection AddCaesar(this IServiceCollection services, CaesarServiceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.AssembliesToRegister.Count == 0)
        {
            throw new ArgumentException(
                "No assemblies found to scan. Supply at least one assembly, e.g. cfg.RegisterServicesFromAssemblyContaining<Program>().",
                nameof(configuration));
        }

        if (!typeof(IMediator).IsAssignableFrom(configuration.MediatorImplementationType))
        {
            throw new ArgumentException(
                $"{configuration.MediatorImplementationType.FullName} must implement {typeof(IMediator).FullName}.",
                nameof(configuration));
        }

        if (configuration.NotificationPublisher is null && !typeof(INotificationPublisher).IsAssignableFrom(configuration.NotificationPublisherType))
        {
            throw new ArgumentException(
                $"{configuration.NotificationPublisherType.FullName} must implement {typeof(INotificationPublisher).FullName}.",
                nameof(configuration));
        }

        ServiceRegistrar.AddCaesarClasses(services, configuration);
        ServiceRegistrar.AddRequiredServices(services, configuration);

        return services;
    }
}
