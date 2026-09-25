using Caesar.DependencyInjection;
using Caesar.NotificationPublishers;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.OptionConflicts;

public sealed record Ping : IRequest<string>;

public sealed class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken) => Task.FromResult("pong");
}

public sealed class ModuleMediator(IServiceProvider serviceProvider) : Mediator(serviceProvider);

public class OptionConflictTests
{
    private static readonly INotificationPublisher SharedPublisher = new TaskWhenAllPublisher();

    private static void AddCaesar(IServiceCollection services, Action<CaesarServiceConfiguration>? configure = null)
        => services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<OptionConflictTests>();
            cfg.TypeEvaluator = static t => t.Namespace == typeof(OptionConflictTests).Namespace;
            configure?.Invoke(cfg);
        });

    private static void SetDifferentValue(CaesarServiceConfiguration cfg, string option)
    {
        switch (option)
        {
            case nameof(CaesarServiceConfiguration.MediatorLifetime):
                cfg.MediatorLifetime = ServiceLifetime.Transient;
                break;
            case nameof(CaesarServiceConfiguration.MediatorImplementationType):
                cfg.MediatorImplementationType = typeof(ModuleMediator);
                break;
            case nameof(CaesarServiceConfiguration.NotificationPublisher):
                cfg.NotificationPublisher = new ForeachAwaitContinueOnFailurePublisher();
                break;
            case nameof(CaesarServiceConfiguration.NotificationPublisherType):
                cfg.NotificationPublisherType = typeof(TaskWhenAllPublisher);
                break;
            case nameof(CaesarServiceConfiguration.RequestExceptionActionProcessorStrategy):
                cfg.RequestExceptionActionProcessorStrategy = RequestExceptionActionProcessorStrategy.ApplyForAllExceptions;
                break;
            case nameof(CaesarServiceConfiguration.BypassExceptionHandlingOnCallerCancellation):
                cfg.BypassExceptionHandlingOnCallerCancellation = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(option), option, null);
        }
    }

    [Theory]
    [InlineData(nameof(CaesarServiceConfiguration.MediatorLifetime))]
    [InlineData(nameof(CaesarServiceConfiguration.MediatorImplementationType))]
    [InlineData(nameof(CaesarServiceConfiguration.NotificationPublisher))]
    [InlineData(nameof(CaesarServiceConfiguration.NotificationPublisherType))]
    [InlineData(nameof(CaesarServiceConfiguration.RequestExceptionActionProcessorStrategy))]
    [InlineData(nameof(CaesarServiceConfiguration.BypassExceptionHandlingOnCallerCancellation))]
    public void Later_call_that_sets_a_global_option_to_a_different_value_throws(string option)
    {
        var services = new ServiceCollection();
        AddCaesar(services);

        var exception = Assert.Throws<InvalidOperationException>(() => AddCaesar(services, cfg => SetDifferentValue(cfg, option)));

        Assert.Contains(option, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Later_call_that_leaves_global_options_at_their_defaults_keeps_the_first_calls_values()
    {
        var services = new ServiceCollection();
        AddCaesar(services, static cfg =>
        {
            cfg.MediatorLifetime = ServiceLifetime.Singleton;
            cfg.NotificationPublisherType = typeof(TaskWhenAllPublisher);
            cfg.RequestExceptionActionProcessorStrategy = RequestExceptionActionProcessorStrategy.ApplyForAllExceptions;
        });

        AddCaesar(services);

        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, static d => d.ServiceType == typeof(IMediator)).Lifetime);
        Assert.Equal(typeof(TaskWhenAllPublisher), Assert.Single(services, static d => d.ServiceType == typeof(INotificationPublisher)).ImplementationType);
    }

    [Fact]
    public void Later_call_that_repeats_the_same_global_options_does_not_throw()
    {
        var services = new ServiceCollection();
        Action<CaesarServiceConfiguration> configure = static cfg =>
        {
            cfg.MediatorLifetime = ServiceLifetime.Transient;
            cfg.MediatorImplementationType = typeof(ModuleMediator);
            cfg.NotificationPublisher = SharedPublisher;
            cfg.NotificationPublisherType = typeof(TaskWhenAllPublisher);
            cfg.RequestExceptionActionProcessorStrategy = RequestExceptionActionProcessorStrategy.ApplyForAllExceptions;
        };

        AddCaesar(services, configure);
        AddCaesar(services, configure);

        Assert.Single(services, static d => d.ServiceType == typeof(IMediator));
    }

    [Fact]
    public void Separate_instances_of_one_publisher_type_do_not_conflict()
    {
        var services = new ServiceCollection();
        AddCaesar(services, static cfg => cfg.NotificationPublisher = new TaskWhenAllPublisher());

        AddCaesar(services, static cfg => cfg.NotificationPublisher = new TaskWhenAllPublisher());

        Assert.Single(services, static d => d.ServiceType == typeof(INotificationPublisher));
    }

    [Fact]
    public void An_instance_and_the_same_publisher_type_do_not_conflict()
    {
        var services = new ServiceCollection();
        AddCaesar(services, static cfg => cfg.NotificationPublisher = new TaskWhenAllPublisher());

        AddCaesar(services, static cfg => cfg.NotificationPublisherType = typeof(TaskWhenAllPublisher));

        Assert.Single(services, static d => d.ServiceType == typeof(INotificationPublisher));
    }

    [Fact]
    public void A_different_publisher_type_names_both_publishers_in_effect()
    {
        var services = new ServiceCollection();
        AddCaesar(services, static cfg => cfg.NotificationPublisher = new TaskWhenAllPublisher());

        var exception = Assert.Throws<InvalidOperationException>(
            () => AddCaesar(services, static cfg => cfg.NotificationPublisherType = typeof(ForeachAwaitContinueOnFailurePublisher)));

        Assert.Contains(nameof(TaskWhenAllPublisher), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ForeachAwaitContinueOnFailurePublisher), exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ForeachAwaitPublisher) + " ", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Per_call_scanning_options_may_differ_between_calls()
    {
        var services = new ServiceCollection();
        AddCaesar(services);

        AddCaesar(services, static cfg =>
        {
            cfg.Lifetime = ServiceLifetime.Scoped;
            cfg.AutoRegisterRequestProcessors = false;
            cfg.TypeEvaluator = static _ => false;
        });

        Assert.Single(services, static d => d.ServiceType == typeof(IMediator));
    }
}
