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
    /// <exception cref="ArgumentException">
    /// No assembly was registered for scanning, or <see cref="CaesarServiceConfiguration.MediatorImplementationType"/> or
    /// <see cref="CaesarServiceConfiguration.NotificationPublisherType"/> is not a concrete class (not an interface, abstract or an open generic) that implements its interface.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A scanned open-generic handler implements a Caesar interface in a shape the container cannot close, or two different
    /// scanned open generics implement the same single-handler interface.
    /// </exception>
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

        if (FindImplementationTypeProblem(configuration.MediatorImplementationType, typeof(IMediator)) is { } mediatorProblem)
        {
            throw new ArgumentException(
                $"{nameof(CaesarServiceConfiguration)}.{nameof(CaesarServiceConfiguration.MediatorImplementationType)} must be a concrete "
                + $"class that implements {typeof(IMediator).FullName}, but {mediatorProblem}.",
                nameof(configuration));
        }

        // A ready-made publisher instance takes precedence, so the type is not used and not validated.
        if (configuration.NotificationPublisher is null
            && FindImplementationTypeProblem(configuration.NotificationPublisherType, typeof(INotificationPublisher)) is { } publisherProblem)
        {
            throw new ArgumentException(
                $"{nameof(CaesarServiceConfiguration)}.{nameof(CaesarServiceConfiguration.NotificationPublisherType)} must be a concrete "
                + $"class that implements {typeof(INotificationPublisher).FullName}, but {publisherProblem}.",
                nameof(configuration));
        }

        ServiceRegistrar.AddCaesarClasses(services, configuration);
        ServiceRegistrar.AddRequiredServices(services, configuration);

        return services;
    }

    /// <summary>
    /// Says why the container could not construct <paramref name="implementationType"/> as <paramref name="serviceType"/>,
    /// or returns <see langword="null"/> when it can.
    /// </summary>
    private static string? FindImplementationTypeProblem(Type? implementationType, Type serviceType) => implementationType switch
    {
        null => "it is null",
        { IsInterface: true } => $"{implementationType.DisplayName()} is an interface",
        { IsAbstract: true } => $"{implementationType.DisplayName()} is abstract",
        { ContainsGenericParameters: true } => $"{implementationType.DisplayName()} is an open generic type",
        { IsClass: false } => $"{implementationType.DisplayName()} is not a class",
        _ when !serviceType.IsAssignableFrom(implementationType) => $"{implementationType.DisplayName()} does not implement it",
        _ => null,
    };
}
