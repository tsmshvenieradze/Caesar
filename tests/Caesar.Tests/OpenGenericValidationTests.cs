using System.Reflection;
using Caesar.DependencyInjection;
using Caesar.Tests.Fixtures.ArityMismatch;
using Caesar.Tests.Fixtures.Benign;
using Caesar.Tests.Fixtures.ReversedParameters;
using Caesar.Tests.Fixtures.WrappedNotification;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.OpenGenerics;

public class OpenGenericValidationTests
{
    private static readonly Assembly Fixtures = typeof(Box<>).Assembly;

    private static ServiceCollection Scan(Type fixture)
    {
        var services = new ServiceCollection();
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Fixtures);
            cfg.TypeEvaluator = t => t.Namespace == fixture.Namespace;
        });

        return services;
    }

    [Fact]
    public void Open_generic_request_handler_the_container_cannot_close_throws_at_registration()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Scan(typeof(ArityMismatchHandler<>)));

        Assert.Contains("ArityMismatchHandler", exception.Message, StringComparison.Ordinal);
        Assert.Contains("IRequestHandler", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(CaesarServiceConfiguration.TypeEvaluator), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_generic_handler_with_reversed_type_parameters_throws_at_registration()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Scan(typeof(ReversedParametersHandler<,>)));

        Assert.Contains("ReversedParametersHandler", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_generic_notification_handler_the_container_cannot_close_throws_at_registration()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Scan(typeof(EnvelopeHandler<>)));

        Assert.Contains("EnvelopeHandler", exception.Message, StringComparison.Ordinal);
        Assert.Contains("INotificationHandler", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generic_types_that_implement_no_Caesar_interface_are_ignored()
    {
        var services = Scan(typeof(Box<>));

        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(Box<>));
    }

    [Fact]
    public void TypeEvaluator_can_exclude_an_unregisterable_open_generic()
    {
        var services = new ServiceCollection();

        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Fixtures);
            cfg.TypeEvaluator = t => t != typeof(ArityMismatchHandler<>)
                && t != typeof(ReversedParametersHandler<,>)
                && t != typeof(EnvelopeHandler<>);
        });

        Assert.Contains(services, d => d.ServiceType == typeof(IMediator));
    }
}
