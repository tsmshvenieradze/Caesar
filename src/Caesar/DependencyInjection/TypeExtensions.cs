namespace Caesar.DependencyInjection;

internal static class TypeExtensions
{
    /// <summary>Returns every closed interface of <paramref name="type"/> whose generic definition is <paramref name="openInterface"/>.</summary>
    public static IEnumerable<Type> FindClosedInterfaces(this Type type, Type openInterface)
        => type.GetInterfaces()
            .Where(i => i.IsGenericType && !i.ContainsGenericParameters && i.GetGenericTypeDefinition() == openInterface);

    /// <summary>
    /// <see langword="true"/> when the open generic <paramref name="openImplementation"/> implements <paramref name="openInterface"/>
    /// with its own type parameters in declaration order, which is the shape Microsoft.Extensions.DependencyInjection can close at runtime.
    /// </summary>
    public static bool ImplementsOpenGenericWithOwnParameters(this Type openImplementation, Type openInterface)
    {
        if (!openImplementation.IsGenericTypeDefinition)
        {
            return false;
        }

        var ownParameters = openImplementation.GetGenericArguments();

        foreach (var candidate in openImplementation.GetInterfaces())
        {
            if (!candidate.IsGenericType || candidate.GetGenericTypeDefinition() != openInterface)
            {
                continue;
            }

            var arguments = candidate.GetGenericArguments();
            if (arguments.Length != ownParameters.Length)
            {
                continue;
            }

            var matches = true;
            for (var i = 0; i < arguments.Length; i++)
            {
                if (!arguments[i].IsGenericParameter || arguments[i].GenericParameterPosition != i)
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><see langword="true"/> when <paramref name="type"/> implements <paramref name="openInterface"/> in any shape, closed or open.</summary>
    public static bool ImplementsOpenGeneric(this Type type, Type openInterface)
        => type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openInterface);

    public static bool IsConcrete(this Type type) => type is { IsClass: true, IsAbstract: false };
}
