using System.Reflection;
using Caesar.Tests.Fixtures.Registration.NestedInGeneric;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.UncloseableShapeMessage;

/// <summary>
/// The "cannot close" error used to render only the innermost type name and the open interface definition,
/// so <c>Outer&lt;T&gt;.Handler : INotificationHandler&lt;Evt&gt;</c> was reported as <c>Handler&lt;T&gt; implements INotificationHandler&lt;TNotification&gt;</c>.
/// </summary>
public class UncloseableShapeMessageTests
{
    private const string Prefix = "Caesar.Tests.Fixtures.Registration.";

    private static readonly Assembly Fixtures = typeof(Evt).Assembly;

    private static InvalidOperationException Scan(string scenario)
        => Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Fixtures);
            cfg.TypeEvaluator = t => t.Namespace == Prefix + scenario;
        }));

    [Fact]
    public void Handler_nested_in_a_generic_class_is_named_with_its_declaring_type_and_actual_interface()
    {
        var exception = Scan("NestedInGeneric");

        Assert.StartsWith(
            "Caesar.Tests.Fixtures.Registration.NestedInGeneric.Outer<T>.Handler implements Caesar.INotificationHandler<Evt> in a shape the container cannot close.",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains("nested in a generic type", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("TNotification", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generic_handler_nested_in_a_generic_class_splits_type_parameters_between_the_two()
    {
        var exception = Scan("NestedGenericInGeneric");

        Assert.StartsWith(
            "Caesar.Tests.Fixtures.Registration.NestedGenericInGeneric.Outer<T>.Handler<TRequest> implements Caesar.IRequestHandler<TRequest, T> in a shape the container cannot close.",
            exception.Message,
            StringComparison.Ordinal);
    }
}
