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

    /// <summary>
    /// Every open interface scanning looks for, in registration order: single handlers, multi handlers, then processors.
    /// A type's interfaces are registered grouped by this order, and within a group in <see cref="Type.GetInterfaces"/> order.
    /// </summary>
    private static readonly Type[] ScannedInterfaces = [.. SingleHandlerInterfaces, .. MultiHandlerInterfaces, .. ProcessorInterfaces];

    public static void AddCaesarClasses(IServiceCollection services, CaesarServiceConfiguration configuration)
    {
        var scan = new ScanContext(services, configuration);

        foreach (var type in GetCandidateTypes(configuration))
        {
            var matches = MatchScannedInterfaces(type, configuration.AutoRegisterRequestProcessors);
            if (matches is null)
            {
                continue;
            }

            if (type.IsGenericTypeDefinition)
            {
                RegisterOpenGeneric(scan, type, matches);
            }
            else
            {
                RegisterClosed(scan, type, matches);
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
        // The first call decides the global options; a later call that sets one to something else throws here.
        var state = CaesarRegistrationState.Register(services, configuration);
        var lifetime = state.MediatorLifetime;

        services.TryAdd(new ServiceDescriptor(typeof(IMediator), state.MediatorImplementationType, lifetime));
        services.TryAdd(new ServiceDescriptor(typeof(ISender), static sp => sp.GetRequiredService<IMediator>(), lifetime));
        services.TryAdd(new ServiceDescriptor(typeof(IPublisher), static sp => sp.GetRequiredService<IMediator>(), lifetime));

        if (state.NotificationPublisher is { } publisherInstance)
        {
            services.TryAdd(new ServiceDescriptor(typeof(INotificationPublisher), publisherInstance));
        }
        else
        {
            services.TryAdd(new ServiceDescriptor(typeof(INotificationPublisher), state.NotificationPublisherType, lifetime));
        }

        // The built-in stages (exception actions and handlers, pre- and post-processors) are not registered as
        // behaviors. The request wrapper composes them per request type, in the documented order, from what the
        // container's registry reports, so neither the number and order of AddCaesar calls nor registrations made
        // after AddCaesar can change them. The registry is a factory singleton so that every container built from
        // this collection reads it for itself, at first use. It is replaced on every call, so that a collection copied
        // from another one and given its own AddCaesar call is read instead of the original. The notification shape tells
        // the notification wrapper which base classes and interfaces of a notification have handlers of their own.
        services.RemoveAll<CaesarRegistry>();
        services.AddSingleton(sp => new CaesarRegistry(services, state, sp));
        services.TryAdd(ServiceDescriptor.Singleton(typeof(Wrappers.RequestPipelineShape<,>), typeof(Wrappers.RequestPipelineShape<,>)));
        services.TryAdd(ServiceDescriptor.Singleton(typeof(Wrappers.StreamRequestShape<,>), typeof(Wrappers.StreamRequestShape<,>)));
        services.TryAdd(ServiceDescriptor.Singleton(typeof(Wrappers.NotificationHandlerShape<>), typeof(Wrappers.NotificationHandlerShape<>)));

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
            // Nested types are scanned when the assembly can see them (public, internal or protected internal), so a
            // vertical slice's internal handler is found. Private and protected nested types, typically test doubles
            // nested in a test class, are left out, as are compiler-generated types (closures, state machines).
            .Where(static t => t.IsConcrete() && t.IsVisibleInAssembly() && !t.IsCompilerGenerated())
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

    /// <summary>
    /// Matches <paramref name="type"/>'s interfaces against <see cref="ScannedInterfaces"/> in one pass, calling
    /// <see cref="Type.GetInterfaces"/> once. Returns them ordered by rank, stable within a rank, or <see langword="null"/>
    /// when the type implements none, which is the common case for a scanned type.
    /// </summary>
    private static List<(int Rank, Type Interface)>? MatchScannedInterfaces(Type type, bool includeProcessors)
    {
        var rankLimit = includeProcessors ? ScannedInterfaces.Length : ScannedInterfaces.Length - ProcessorInterfaces.Length;
        List<(int Rank, Type Interface)>? matches = null;

        foreach (var implemented in type.GetInterfaces())
        {
            if (!implemented.IsGenericType)
            {
                continue;
            }

            var rank = Array.IndexOf(ScannedInterfaces, implemented.GetGenericTypeDefinition(), 0, rankLimit);
            if (rank < 0)
            {
                continue;
            }

            // Insertion sort: a handler implements a handful of interfaces, and List.Sort is not stable.
            matches ??= [];
            var index = matches.Count;
            while (index > 0 && matches[index - 1].Rank > rank)
            {
                index--;
            }

            matches.Insert(index, (rank, implemented));
        }

        return matches;
    }

    private static bool IsSingleHandler(int rank) => rank < SingleHandlerInterfaces.Length;

    private static void RegisterClosed(ScanContext scan, Type implementationType, List<(int Rank, Type Interface)> matches)
    {
        foreach (var (rank, serviceType) in matches)
        {
            if (serviceType.ContainsGenericParameters)
            {
                continue;
            }

            if (IsSingleHandler(rank))
            {
                scan.Services.TryAdd(new ServiceDescriptor(serviceType, implementationType, scan.Lifetime));
            }
            else
            {
                scan.Services.TryAddEnumerable(scan.ExplicitOrNew(serviceType, implementationType));
            }
        }
    }

    private static void RegisterOpenGeneric(ScanContext scan, Type openImplementation, List<(int Rank, Type Interface)> matches)
    {
        var ownParameterCount = openImplementation.GetGenericArguments().Length;

        // Matches are grouped by rank, i.e. by open interface: the interface is registered once when any of the type's
        // implementations of it takes the type's own parameters in order.
        for (var start = 0; start < matches.Count;)
        {
            var rank = matches[start].Rank;
            var end = start;
            var closeable = false;
            while (end < matches.Count && matches[end].Rank == rank)
            {
                closeable |= matches[end].Interface.IsClosedOverOwnParameters(ownParameterCount);
                end++;
            }

            if (!closeable)
            {
                // A non-public nested type was not scanned before nested types were: skip it rather than turn an
                // upgrade into a startup failure. Register it by hand if it is meant to be a handler.
                if (openImplementation.IsNested && !openImplementation.IsNestedPublic)
                {
                    start = end;
                    continue;
                }

                // Skipping is correct for a generic type that has nothing to do with Caesar. A type that does
                // implement the interface, but in a shape the container cannot close, would never be resolved at
                // dispatch time -- fail here instead of on the first request that needs it.
                throw new InvalidOperationException(
                    $"{Describe(openImplementation)} implements {Describe(matches[start].Interface)} in a shape the container cannot close. "
                    + DescribeNestedInGeneric(openImplementation)
                    + "An open generic handler must implement the interface with its own type parameters, in declaration order, "
                    + "e.g. class MyHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>. "
                    + $"Reshape it, register a closed implementation instead, or exclude it with {nameof(CaesarServiceConfiguration)}.{nameof(CaesarServiceConfiguration.TypeEvaluator)}.");
            }

            var openInterface = ScannedInterfaces[rank];
            if (IsSingleHandler(rank))
            {
                RegisterOpenSingleHandler(scan, openInterface, openImplementation);
            }
            else
            {
                scan.Services.TryAddEnumerable(scan.ExplicitOrNew(openInterface, openImplementation));
            }

            start = end;
        }
    }

    /// <summary>
    /// TryAdd for an open-generic single handler, except that a second, different open generic found by scanning is an
    /// error: the container resolves one implementation per request type, so TryAdd would silently drop it. A registration
    /// the user made by hand keeps precedence, and scanning the same type again is a no-op.
    /// </summary>
    private static void RegisterOpenSingleHandler(ScanContext scan, Type openInterface, Type openImplementation)
    {
        ServiceDescriptor? scannedConflict = null;
        foreach (var existing in scan.Services)
        {
            if (existing.ServiceType != openInterface || existing.IsKeyedService)
            {
                continue;
            }

            if (existing.ImplementationType == openImplementation || !scan.WasScanned(existing))
            {
                return;
            }

            scannedConflict ??= existing;
        }

        if (scannedConflict is not null)
        {
            throw new InvalidOperationException(
                $"{Describe(scannedConflict.ImplementationType!)} and {Describe(openImplementation)} were both found by scanning as open-generic "
                + $"implementations of {Describe(openInterface)}. The container resolves a single handler per request type, so only the first would ever run. "
                + $"Keep one, register closed handlers for the other's requests, or exclude it with {nameof(CaesarServiceConfiguration)}.{nameof(CaesarServiceConfiguration.TypeEvaluator)}.");
        }

        var descriptor = new ServiceDescriptor(openInterface, openImplementation, scan.Lifetime);
        scan.Services.Add(descriptor);
        scan.RecordScanned(descriptor);
    }

    /// <summary>Explains why a type without type parameters of its own is an open generic, e.g. <c>Outer&lt;T&gt;.Handler</c>.</summary>
    private static string DescribeNestedInGeneric(Type openImplementation)
        => openImplementation.DeclaringType is { IsGenericType: true } declaringType && !openImplementation.Name.Contains('`', StringComparison.Ordinal)
            ? $"It is generic only because it is nested in a generic type, {Describe(declaringType)}; move it out of that type. "
            : string.Empty;

    /// <summary>Renders a type as it is written in source, e.g. <c>Acme.PingHandler&lt;T&gt;</c> or <c>Acme.Outer&lt;T&gt;.Handler</c>.</summary>
    private static string Describe(Type type)
    {
        return type.DisplayName();
    }
}
