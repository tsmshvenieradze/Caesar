using Caesar.DependencyInjection;
using Caesar.NotificationPublishers;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Registration;

public sealed record Ping : IRequest<string>;

public sealed class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken) => Task.FromResult("pong");
}

public sealed class SecondPingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken) => Task.FromResult("second");
}

public abstract class AbstractHandler : IRequestHandler<Ping, string>
{
    public abstract Task<string> Handle(Ping request, CancellationToken cancellationToken);
}

public sealed class CustomMediator(IServiceProvider serviceProvider, INotificationPublisher publisher) : Mediator(serviceProvider, publisher)
{
    public static int Publishes { get; private set; }

    protected override Task PublishCore(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        Publishes++;
        return base.PublishCore(handlerExecutors, notification, cancellationToken);
    }
}

public sealed class SingletonCaptor(ISender sender)
{
    public ISender Sender { get; } = sender;
}

public sealed record Event : INotification;

public class RegistrationTests
{
    [Fact]
    public void ISender_and_IPublisher_resolve_to_the_same_mediator_instance_within_a_scope()
    {
        using var provider = TestHost.Build<RegistrationTests>(cfg => cfg.Lifetime = ServiceLifetime.Scoped);
        using var scope = provider.CreateScope();

        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();

        Assert.Same(mediator, sender);
        Assert.Same(mediator, publisher);
    }

    [Fact]
    public void Transient_lifetime_produces_new_handler_instances()
    {
        using var provider = TestHost.Build<RegistrationTests>();

        Assert.NotSame(
            provider.GetRequiredService<IRequestHandler<Ping, string>>(),
            provider.GetRequiredService<IRequestHandler<Ping, string>>());
    }

    [Fact]
    public void Singleton_lifetime_is_supported()
    {
        using var provider = TestHost.Build<RegistrationTests>(cfg =>
        {
            cfg.Lifetime = ServiceLifetime.Singleton;
            cfg.MediatorLifetime = ServiceLifetime.Singleton;
        });

        Assert.Same(provider.Root.GetRequiredService<IMediator>(), provider.Root.GetRequiredService<ISender>());
        Assert.Same(provider.GetRequiredService<IRequestHandler<Ping, string>>(), provider.GetRequiredService<IRequestHandler<Ping, string>>());
    }

    [Fact]
    public void Handlers_are_registered_with_the_configured_lifetime()
    {
        var services = new ServiceCollection();
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<RegistrationTests>();
            cfg.TypeEvaluator = t => t.Namespace == typeof(RegistrationTests).Namespace;
            cfg.Lifetime = ServiceLifetime.Scoped;
        });

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IRequestHandler<Ping, string>));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(PingHandler), descriptor.ImplementationType);
    }

    [Fact]
    public void First_scanned_request_handler_wins_and_abstract_types_are_skipped()
    {
        var services = new ServiceCollection();
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<RegistrationTests>();
            cfg.TypeEvaluator = t => t.Namespace == typeof(RegistrationTests).Namespace;
        });

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IRequestHandler<Ping, string>));
        Assert.NotEqual(typeof(AbstractHandler), descriptor.ImplementationType);
    }

    [Fact]
    public async Task TypeEvaluator_can_exclude_handlers()
    {
        await using var provider = TestHost.Build<RegistrationTests>(cfg => cfg.TypeEvaluator = t => t != typeof(PingHandler) && t != typeof(SecondPingHandler));

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<ISender>().Send(new Ping()));
    }

    [Fact]
    public void AddCaesar_without_assemblies_throws()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddCaesar(_ => { }));

        Assert.Contains("No assemblies", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddCaesar_rejects_invalid_mediator_type()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<RegistrationTests>();
            cfg.MediatorImplementationType = typeof(string);
        }));
    }

    [Fact]
    public void AddCaesar_rejects_invalid_publisher_type()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<RegistrationTests>();
            cfg.NotificationPublisherType = typeof(string);
        }));
    }

    [Fact]
    public async Task Custom_mediator_implementation_is_used()
    {
        await using var provider = TestHost.Build<RegistrationTests>(cfg => cfg.MediatorImplementationType = typeof(CustomMediator));
        var before = CustomMediator.Publishes;

        var mediator = provider.GetRequiredService<IMediator>();
        await mediator.Publish(new Event());

        Assert.IsType<CustomMediator>(mediator);
        Assert.Equal(before + 1, CustomMediator.Publishes);
    }

    [Fact]
    public void Default_publisher_is_ForeachAwait()
    {
        using var provider = TestHost.Build<RegistrationTests>();

        Assert.IsType<ForeachAwaitPublisher>(provider.GetRequiredService<INotificationPublisher>());
    }

    [Fact]
    public void Calling_AddCaesar_twice_does_not_duplicate_registrations()
    {
        var services = new ServiceCollection();
        Action<CaesarServiceConfiguration> configure = cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<RegistrationTests>();
            cfg.TypeEvaluator = t => t.Namespace == typeof(RegistrationTests).Namespace;
        };

        services.AddCaesar(configure);
        services.AddCaesar(configure);

        Assert.Single(services, d => d.ServiceType == typeof(IMediator));
        Assert.Single(services, d => d.ServiceType == typeof(IRequestHandler<Ping, string>));
    }

    [Fact]
    public void Assemblies_overload_scans_with_defaults()
    {
        var services = new ServiceCollection();

        services.AddCaesar(typeof(RegistrationTests).Assembly);

        Assert.Contains(services, d => d.ServiceType == typeof(IMediator));
        Assert.Contains(services, d => d.ServiceType == typeof(IRequestHandler<Ping, string>));
    }

    [Fact]
    public void Configuration_deduplicates_assemblies()
    {
        var configuration = new CaesarServiceConfiguration();

        configuration.RegisterServicesFromAssemblyContaining<RegistrationTests>();
        configuration.RegisterServicesFromAssembly(typeof(RegistrationTests).Assembly);
        configuration.RegisterServicesFromAssemblies(typeof(RegistrationTests).Assembly, typeof(Mediator).Assembly);

        Assert.Equal(2, configuration.AssembliesToRegister.Count);
    }

    [Fact]
    public void Mediator_is_scoped_by_default_while_handlers_stay_transient()
    {
        var services = new ServiceCollection();
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<RegistrationTests>();
            cfg.TypeEvaluator = t => t.Namespace == typeof(RegistrationTests).Namespace;
        });

        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == typeof(IMediator)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == typeof(ISender)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == typeof(IPublisher)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == typeof(INotificationPublisher)).Lifetime);
        Assert.Equal(ServiceLifetime.Transient, Assert.Single(services, d => d.ServiceType == typeof(IRequestHandler<Ping, string>)).Lifetime);
    }

    [Fact]
    public void Singleton_capturing_ISender_is_rejected_when_the_container_is_validated()
    {
        var services = new ServiceCollection();
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<RegistrationTests>();
            cfg.TypeEvaluator = t => t.Namespace == typeof(RegistrationTests).Namespace;
        });
        services.AddSingleton<SingletonCaptor>();

        var exception = Assert.Throws<AggregateException>(
            () => services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));

        Assert.Contains(nameof(ISender), exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MediatorLifetime_overrides_the_scoped_default()
    {
        var services = new ServiceCollection();
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<RegistrationTests>();
            cfg.TypeEvaluator = t => t.Namespace == typeof(RegistrationTests).Namespace;
            cfg.MediatorLifetime = ServiceLifetime.Singleton;
        });

        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, d => d.ServiceType == typeof(IMediator)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, d => d.ServiceType == typeof(ISender)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, d => d.ServiceType == typeof(IPublisher)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, d => d.ServiceType == typeof(INotificationPublisher)).Lifetime);
    }
}
