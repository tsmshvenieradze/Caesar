using System.Reflection;
using Caesar.Tests.Fixtures.Registration.DuplicateOpen.RequestA;
using Caesar.Tests.Fixtures.Registration.DuplicateOpen.RequestB;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.DuplicateOpenGenericHandlers;

/// <summary>
/// The container resolves a single-handler interface to one implementation, so a second, different open-generic
/// implementation found by scanning used to be dropped silently by TryAdd. It is now reported at AddCaesar.
/// </summary>
public class DuplicateOpenGenericHandlerTests
{
    private const string Prefix = "Caesar.Tests.Fixtures.Registration.DuplicateOpen.";

    private static readonly Assembly Fixtures = typeof(FirstRequestHandler<,>).Assembly;

    private static void Scan(IServiceCollection services, params string[] scenarios)
        => services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Fixtures);
            cfg.TypeEvaluator = t => scenarios.Any(s => t.Namespace == Prefix + s);
        });

    [Theory]
    [InlineData("Request", "FirstRequestHandler<TRequest, TResponse>", "SecondRequestHandler<TRequest, TResponse>", "IRequestHandler<TRequest, TResponse>")]
    [InlineData("Void", "FirstVoidHandler<TRequest>", "SecondVoidHandler<TRequest>", "IRequestHandler<TRequest>")]
    [InlineData("Stream", "FirstStreamHandler<TRequest, TResponse>", "SecondStreamHandler<TRequest, TResponse>", "IStreamRequestHandler<TRequest, TResponse>")]
    public void Two_different_open_generic_single_handlers_in_one_scan_throw_naming_both(string kind, string first, string second, string service)
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => Scan(services, kind + "A", kind + "B"));

        Assert.Contains(first, exception.Message, StringComparison.Ordinal);
        Assert.Contains(second, exception.Message, StringComparison.Ordinal);
        Assert.Contains(service, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_open_generic_scanned_by_a_later_AddCaesar_call_throws()
    {
        var services = new ServiceCollection();
        Scan(services, "RequestA");

        var exception = Assert.Throws<InvalidOperationException>(() => Scan(services, "RequestB"));

        Assert.Contains("FirstRequestHandler<TRequest, TResponse>", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SecondRequestHandler<TRequest, TResponse>", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scanning_the_same_open_generic_twice_does_not_throw_or_duplicate()
    {
        var services = new ServiceCollection();

        Scan(services, "RequestA");
        Scan(services, "RequestA");

        var descriptor = Assert.Single(services, static d => d.ServiceType == typeof(IRequestHandler<,>));
        Assert.Equal(typeof(FirstRequestHandler<,>), descriptor.ImplementationType);
    }

    [Fact]
    public void A_manual_registration_made_before_AddCaesar_keeps_precedence_without_throwing()
    {
        var services = new ServiceCollection();
        services.AddScoped(typeof(IRequestHandler<,>), typeof(SecondRequestHandler<,>));

        Scan(services, "RequestA");
        Scan(services, "RequestA");

        var descriptor = Assert.Single(services, static d => d.ServiceType == typeof(IRequestHandler<,>));
        Assert.Equal(typeof(SecondRequestHandler<,>), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void A_manual_registration_of_the_scanned_type_does_not_throw()
    {
        var services = new ServiceCollection();
        services.AddScoped(typeof(IRequestHandler<,>), typeof(FirstRequestHandler<,>));

        Scan(services, "RequestA");

        var descriptor = Assert.Single(services, static d => d.ServiceType == typeof(IRequestHandler<,>));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void Open_generic_single_handlers_for_different_interfaces_do_not_conflict()
    {
        var services = new ServiceCollection();

        Scan(services, "RequestA", "VoidA", "StreamA");

        Assert.Single(services, static d => d.ServiceType == typeof(IRequestHandler<,>));
        Assert.Single(services, static d => d.ServiceType == typeof(IRequestHandler<>));
        Assert.Single(services, static d => d.ServiceType == typeof(IStreamRequestHandler<,>));
    }
}
