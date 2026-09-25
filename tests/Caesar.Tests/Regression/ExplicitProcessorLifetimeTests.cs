using Caesar.DependencyInjection;
using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.ExplicitProcessorLifetime;

public sealed record Ping : IRequest<string>;

public sealed class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken) => Task.FromResult("pong");
}

public sealed class FirstPre : IRequestPreProcessor<Ping>
{
    public Task Process(Ping request, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class AuditPre : IRequestPreProcessor<Ping>
{
    public Task Process(Ping request, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class AuditPost : IRequestPostProcessor<Ping, string>
{
    public Task Process(Ping request, string response, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class OpenPre<TRequest> : IRequestPreProcessor<TRequest>
    where TRequest : notnull
{
    public Task Process(TRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Scanning used to register a processor first with <see cref="CaesarServiceConfiguration.Lifetime"/>, and TryAddEnumerable
/// then dropped the explicit descriptor, silently discarding the lifetime the caller asked for.
/// </summary>
public class ExplicitProcessorLifetimeTests
{
    private static ServiceCollection Register()
    {
        var services = new ServiceCollection();
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<ExplicitProcessorLifetimeTests>();
            cfg.TypeEvaluator = t => t.Namespace == typeof(ExplicitProcessorLifetimeTests).Namespace;
            cfg.Lifetime = ServiceLifetime.Transient;
            cfg.AddRequestPreProcessor<AuditPre>(ServiceLifetime.Singleton);
            cfg.AddRequestPostProcessor<AuditPost>(ServiceLifetime.Singleton);
            cfg.AddOpenRequestPreProcessor(typeof(OpenPre<>), ServiceLifetime.Singleton);
        });

        return services;
    }

    [Fact]
    public void Explicit_processor_lifetime_wins_over_the_scanned_registration_without_duplicates()
    {
        var services = Register();

        var pre = Assert.Single(services, static d => d.ImplementationType == typeof(AuditPre));
        var post = Assert.Single(services, static d => d.ImplementationType == typeof(AuditPost));
        var open = Assert.Single(services, static d => d.ImplementationType == typeof(OpenPre<>));
        Assert.Equal(ServiceLifetime.Singleton, pre.Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, post.Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, open.Lifetime);
        Assert.Equal(ServiceLifetime.Transient, Assert.Single(services, static d => d.ImplementationType == typeof(FirstPre)).Lifetime);
    }

    [Fact]
    public void Explicit_processor_keeps_its_scanned_position()
    {
        var services = Register();

        Assert.Equal(
            [typeof(FirstPre), typeof(AuditPre), typeof(OpenPre<>)],
            services.Where(static d => d.ServiceType == typeof(IRequestPreProcessor<Ping>) || d.ServiceType == typeof(IRequestPreProcessor<>))
                .Select(static d => d.ImplementationType));
    }

    [Fact]
    public void Singleton_processor_is_shared_across_scopes()
    {
        using var provider = Register().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.Same(
            first.ServiceProvider.GetServices<IRequestPreProcessor<Ping>>().OfType<AuditPre>().Single(),
            second.ServiceProvider.GetServices<IRequestPreProcessor<Ping>>().OfType<AuditPre>().Single());
    }

    [Fact]
    public void A_processor_registered_before_AddCaesar_still_wins()
    {
        var services = new ServiceCollection();
        services.AddScoped<IRequestPreProcessor<Ping>, AuditPre>();
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<ExplicitProcessorLifetimeTests>();
            cfg.TypeEvaluator = t => t.Namespace == typeof(ExplicitProcessorLifetimeTests).Namespace;
            cfg.AddRequestPreProcessor<AuditPre>(ServiceLifetime.Singleton);
        });

        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, static d => d.ImplementationType == typeof(AuditPre)).Lifetime);
    }
}
