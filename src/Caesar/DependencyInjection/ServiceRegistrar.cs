using System.Reflection;
using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Caesar.DependencyInjection;

/// <summary>
/// Scans assemblies for Caesar handlers and wires the mediator, publisher and built-in behaviors into the container.
/// </summary>
internal static class ServiceRegistrar
{
    /// <summary>Single-instance services: the last word goes to whoever registered first (TryAdd).</summary>
    private static readonly Type[] SingleHandlerInterfaces =
    [
        typeof(IRequestHandler<,>),
        typeof(IRequestHandler<>),
        typeof(IStreamRequestHandler<,>),
    ];

    /// <summary>Multi-instance services: every implementation is kept (TryAddEnumerable).</summary>
    private static readonly Type[] MultiHandlerInterfaces =
    [
        typeof(INotificationHandler<>),
        typeof(IRequestExceptionHandler<,,>),
        typeof(IRequestExceptionAction<,>),
    ];

    private static readonly Type[] ProcessorInterfaces =
    [
        typeof(IRequestPreProcessor<>),
        typeof(IRequestPostProcessor<,>),
    ];

    public static void AddCaesarClasses(IServiceCollection services, CaesarServiceConfiguration configuration)
    {
        var multi = configuration.AutoRegisterRequestProcessors
            ? [.. MultiHandlerInterfaces, .. ProcessorInterfaces]
            : MultiHandlerInterfaces;

        foreach (var type in GetCandidateTypes(configuration))
        {
            if (type.IsGenericTypeDefinition)
            {
                RegisterOpenGeneric(services, type, SingleHandlerInterfaces, configuration.Lifetime, single: true);
                RegisterOpenGeneric(services, type, multi, configuration.Lifetime, single: false);
            }
            else
            {
                RegisterClosed(services, type, SingleHandlerInterfaces, configuration.Lifetime, single: true);
                RegisterClosed(services, type, multi, configuration.Lifetime, single: false);
            }
        }

        foreach (var descriptor in configuration.RequestPreProcessorsToRegister)
        {
            services.TryAddEnumerable(descriptor);
        }

        foreach (var descriptor in configuration.RequestPostProcessorsToRegister)
        {
            services.TryAddEnumerable(descriptor);
        }
    }

    public static void AddRequiredServices(IServiceCollection services, CaesarServiceConfiguration configuration)
    {
        var lifetime = configuration.MediatorLifetime;

        services.TryAdd(new ServiceDescriptor(typeof(IMediator), configuration.MediatorImplementationType, lifetime));
        services.TryAdd(new ServiceDescriptor(typeof(ISender), static sp => sp.GetRequiredService<IMediator>(), lifetime));
        services.TryAdd(new ServiceDescriptor(typeof(IPublisher), static sp => sp.GetRequiredService<IMediator>(), lifetime));

        if (configuration.NotificationPublisher is { } publisherInstance)
        {
            services.TryAdd(new ServiceDescriptor(typeof(INotificationPublisher), publisherInstance));
        }
        else
        {
            services.TryAdd(new ServiceDescriptor(typeof(INotificationPublisher), configuration.NotificationPublisherType, lifetime));
        }

        // Built-in behaviors. Registration order == execution order (first is outermost).
        if (configuration.RequestExceptionActionProcessorStrategy == RequestExceptionActionProcessorStrategy.ApplyForUnhandledExceptions)
        {
            AddBuiltInBehaviorWhenUsed(services, typeof(RequestExceptionActionProcessorBehavior<,>), typeof(IRequestExceptionAction<,>));
            AddBuiltInBehaviorWhenUsed(services, typeof(RequestExceptionProcessorBehavior<,>), typeof(IRequestExceptionHandler<,,>));
        }
        else
        {
            AddBuiltInBehaviorWhenUsed(services, typeof(RequestExceptionProcessorBehavior<,>), typeof(IRequestExceptionHandler<,,>));
            AddBuiltInBehaviorWhenUsed(services, typeof(RequestExceptionActionProcessorBehavior<,>), typeof(IRequestExceptionAction<,>));
        }

        AddBuiltInBehaviorWhenUsed(services, typeof(RequestPreProcessorBehavior<,>), typeof(IRequestPreProcessor<>));
        AddBuiltInBehaviorWhenUsed(services, typeof(RequestPostProcessorBehavior<,>), typeof(IRequestPostProcessor<,>));

        foreach (var descriptor in configuration.BehaviorsToRegister)
        {
            services.TryAddEnumerable(descriptor);
        }

        foreach (var descriptor in configuration.StreamBehaviorsToRegister)
        {
            services.TryAddEnumerable(descriptor);
        }
    }

    private static IEnumerable<Type> GetCandidateTypes(CaesarServiceConfiguration configuration)
        => configuration.AssembliesToRegister
            .Distinct()
            .SelectMany(GetLoadableTypes)
            .Where(static t => t.IsConcrete() && (!t.IsNested || t.IsNestedPublic))
            .Where(configuration.TypeEvaluator)
            .Distinct();

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.DefinedTypes;
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(static t => t is not null)!;
        }
    }

    private static void RegisterClosed(IServiceCollection services, Type implementationType, Type[] openInterfaces, ServiceLifetime lifetime, bool single)
    {
        foreach (var openInterface in openInterfaces)
        {
            foreach (var serviceType in implementationType.FindClosedInterfaces(openInterface))
            {
                var descriptor = new ServiceDescriptor(serviceType, implementationType, lifetime);
                if (single)
                {
                    services.TryAdd(descriptor);
                }
                else
                {
                    services.TryAddEnumerable(descriptor);
                }
            }
        }
    }

    private static void RegisterOpenGeneric(IServiceCollection services, Type openImplementation, Type[] openInterfaces, ServiceLifetime lifetime, bool single)
    {
        foreach (var openInterface in openInterfaces)
        {
            if (!openImplementation.ImplementsOpenGenericWithOwnParameters(openInterface))
            {
                // Skipping is correct for a generic type that has nothing to do with Caesar. A type that does
                // implement the interface, but in a shape the container cannot close, would never be resolved at
                // dispatch time -- fail here instead of on the first request that needs it.
                if (openImplementation.ImplementsOpenGeneric(openInterface))
                {
                    throw new InvalidOperationException(
                        $"{Describe(openImplementation)} implements {Describe(openInterface)} in a shape the container cannot close. "
                        + "An open generic handler must implement the interface with its own type parameters, in declaration order, "
                        + "e.g. class MyHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>. "
                        + $"Reshape it, register a closed implementation instead, or exclude it with {nameof(CaesarServiceConfiguration)}.{nameof(CaesarServiceConfiguration.TypeEvaluator)}.");
                }

                continue;
            }

            var descriptor = new ServiceDescriptor(openInterface, openImplementation, lifetime);
            if (single)
            {
                services.TryAdd(descriptor);
            }
            else
            {
                services.TryAddEnumerable(descriptor);
            }
        }
    }

    /// <summary>Renders a generic type as it is written in source, e.g. <c>Acme.PingHandler&lt;T&gt;</c>.</summary>
    private static string Describe(Type type)
    {
        var name = type.Name.Split('`')[0];
        return type.IsGenericType
            ? $"{type.Namespace}.{name}<{string.Join(", ", type.GetGenericArguments().Select(static a => a.Name))}>"
            : $"{type.Namespace}.{name}";
    }

    private static void AddBuiltInBehaviorWhenUsed(IServiceCollection services, Type openBehaviorType, Type openHandlerInterface)
    {
        var hasImplementations = services.Any(d =>
            d.ServiceType == openHandlerInterface
            || (d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == openHandlerInterface));

        if (hasImplementations)
        {
            services.TryAddEnumerable(new ServiceDescriptor(typeof(IPipelineBehavior<,>), openBehaviorType, ServiceLifetime.Transient));
        }
    }
}
