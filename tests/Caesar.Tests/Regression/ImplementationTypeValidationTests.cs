using Caesar.DependencyInjection;
using Caesar.NotificationPublishers;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.ImplementationTypeValidation;

public abstract class AbstractMediator(IServiceProvider serviceProvider, INotificationPublisher publisher) : Mediator(serviceProvider, publisher);

public sealed class GenericMediator<T>(IServiceProvider serviceProvider, INotificationPublisher publisher) : Mediator(serviceProvider, publisher);

public abstract class AbstractPublisher : INotificationPublisher
{
    public abstract Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken);
}

public sealed class GenericPublisher<T> : INotificationPublisher
{
    public Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Validation used to check only IsAssignableFrom: interfaces, abstract classes and open generics passed, and a
/// <see langword="null"/> type threw NullReferenceException while the error message was being built.
/// </summary>
public class ImplementationTypeValidationTests
{
    private const string Ns = "Caesar.Tests.Regression.ImplementationTypeValidation.";

    public static TheoryData<Type?, string> InvalidMediatorTypes => new()
    {
        { null, "null" },
        { typeof(IMediator), "Caesar.IMediator" },
        { typeof(AbstractMediator), Ns + "AbstractMediator" },
        { typeof(GenericMediator<>), Ns + "GenericMediator<T>" },
        { typeof(string), "System.String" },
    };

    public static TheoryData<Type?, string> InvalidPublisherTypes => new()
    {
        { null, "null" },
        { typeof(INotificationPublisher), "Caesar.INotificationPublisher" },
        { typeof(AbstractPublisher), Ns + "AbstractPublisher" },
        { typeof(GenericPublisher<>), Ns + "GenericPublisher<T>" },
        { typeof(string), "System.String" },
    };

    private static void Register(Action<CaesarServiceConfiguration> configure)
        => new ServiceCollection().AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<ImplementationTypeValidationTests>();
            cfg.TypeEvaluator = static t => t.Namespace == typeof(ImplementationTypeValidationTests).Namespace;
            configure(cfg);
        });

    [Theory]
    [MemberData(nameof(InvalidMediatorTypes))]
    public void Invalid_mediator_type_throws_ArgumentException_naming_it(Type? type, string expectedName)
    {
        var exception = Assert.Throws<ArgumentException>(() => Register(cfg => cfg.MediatorImplementationType = type!));

        Assert.Contains(nameof(CaesarServiceConfiguration.MediatorImplementationType), exception.Message, StringComparison.Ordinal);
        Assert.Contains(expectedName, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(IMediator).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidPublisherTypes))]
    public void Invalid_publisher_type_throws_ArgumentException_naming_it(Type? type, string expectedName)
    {
        var exception = Assert.Throws<ArgumentException>(() => Register(cfg => cfg.NotificationPublisherType = type!));

        Assert.Contains(nameof(CaesarServiceConfiguration.NotificationPublisherType), exception.Message, StringComparison.Ordinal);
        Assert.Contains(expectedName, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(INotificationPublisher).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Publisher_instance_takes_precedence_over_an_invalid_publisher_type()
    {
        var instance = new TaskWhenAllPublisher();
        var services = new ServiceCollection();

        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<ImplementationTypeValidationTests>();
            cfg.TypeEvaluator = static t => t.Namespace == typeof(ImplementationTypeValidationTests).Namespace;
            cfg.NotificationPublisher = instance;
            cfg.NotificationPublisherType = null!;
        });

        Assert.Same(instance, Assert.Single(services, static d => d.ServiceType == typeof(INotificationPublisher)).ImplementationInstance);
    }

    [Fact]
    public void A_concrete_subclass_of_Mediator_is_accepted()
    {
        var services = new ServiceCollection();

        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<ImplementationTypeValidationTests>();
            cfg.TypeEvaluator = static t => t.Namespace == typeof(ImplementationTypeValidationTests).Namespace;
            cfg.MediatorImplementationType = typeof(GenericMediator<int>);
        });

        Assert.Equal(typeof(GenericMediator<int>), Assert.Single(services, static d => d.ServiceType == typeof(IMediator)).ImplementationType);
    }
}
