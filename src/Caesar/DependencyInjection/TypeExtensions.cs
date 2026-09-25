using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

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

        var ownParameterCount = openImplementation.GetGenericArguments().Length;

        foreach (var candidate in openImplementation.GetInterfaces())
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == openInterface && candidate.IsClosedOverOwnParameters(ownParameterCount))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <see langword="true"/> when the generic interface <paramref name="implementedInterface"/>, as implemented by an open generic
    /// with <paramref name="ownParameterCount"/> type parameters, takes exactly those parameters in declaration order.
    /// </summary>
    public static bool IsClosedOverOwnParameters(this Type implementedInterface, int ownParameterCount)
    {
        var arguments = implementedInterface.GetGenericArguments();
        if (arguments.Length != ownParameterCount)
        {
            return false;
        }

        for (var i = 0; i < arguments.Length; i++)
        {
            if (!arguments[i].IsGenericParameter || arguments[i].GenericParameterPosition != i)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary><see langword="true"/> when <paramref name="type"/> implements <paramref name="openInterface"/> in any shape, closed or open.</summary>
    public static bool ImplementsOpenGeneric(this Type type, Type openInterface)
        => type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openInterface);

    public static bool IsConcrete(this Type type) => type is { IsClass: true, IsAbstract: false };

    /// <summary>
    /// <see langword="true"/> when <paramref name="type"/>, or a type it is nested in, carries <see cref="CompilerGeneratedAttribute"/>:
    /// closures, iterator and async state machines. They never implement Caesar interfaces and are not worth scanning.
    /// </summary>
    public static bool IsCompilerGenerated(this Type type)
    {
        // Closures, state machines and anonymous types have names no source can declare, such as <>c__DisplayClass0_0.
        // The [CompilerGenerated] attribute is not a reliable sign: source generators put it on partial declarations
        // of ordinary classes, and it then applies to the whole class.
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (current.Name.Contains('<', StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <see langword="true"/> when code anywhere in the type's assembly can name <paramref name="type"/>: it and every
    /// type it is nested in are public, internal or protected internal. Private and protected nested types are not.
    /// </summary>
    public static bool IsVisibleInAssembly(this Type type)
    {
        for (var current = type; current.IsNested; current = current.DeclaringType!)
        {
            if (!(current.IsNestedPublic || current.IsNestedAssembly || current.IsNestedFamORAssem))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Renders <paramref name="type"/> as it is written in source, with its namespace, declaring types and type arguments,
    /// e.g. <c>Acme.Outer&lt;T&gt;.Handler</c> or <c>Caesar.IRequestHandler&lt;Ping, Int32&gt;</c>. Type arguments are rendered
    /// without their namespace to keep messages readable.
    /// </summary>
    public static string DisplayName(this Type type)
    {
        var builder = new StringBuilder();
        AppendDisplayName(builder, type, includeNamespace: true);
        return builder.ToString();
    }

    private static void AppendDisplayName(StringBuilder builder, Type type, bool includeNamespace)
    {
        if (type.IsGenericParameter)
        {
            builder.Append(type.Name);
            return;
        }

        if (type.HasElementType)
        {
            AppendDisplayName(builder, type.GetElementType()!, includeNamespace);
            builder.Append(type.IsArray ? $"[{new string(',', type.GetArrayRank() - 1)}]" : type.IsPointer ? "*" : "&");
            return;
        }

        if (includeNamespace && !string.IsNullOrEmpty(type.Namespace))
        {
            builder.Append(type.Namespace).Append('.');
        }

        // A nested type's generic arguments include its declaring types' arguments, outermost first. Each name's `N suffix
        // says how many of them belong to that level, so Outer<T>.Handler<TRequest> splits [T, TRequest] into [T] and [TRequest].
        var declaringTypes = new Stack<Type>();
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            declaringTypes.Push(current);
        }

        var arguments = type.IsGenericType ? type.GetGenericArguments() : Type.EmptyTypes;
        var used = 0;
        var first = true;

        foreach (var level in declaringTypes)
        {
            if (!first)
            {
                builder.Append('.');
            }

            first = false;

            var name = level.Name;
            var tick = name.IndexOf('`', StringComparison.Ordinal);
            if (tick < 0 || !int.TryParse(name.AsSpan(tick + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var arity))
            {
                builder.Append(name);
                continue;
            }

            builder.Append(name, 0, tick);
            if (arity == 0 || used + arity > arguments.Length)
            {
                continue;
            }

            builder.Append('<');
            for (var i = 0; i < arity; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                AppendDisplayName(builder, arguments[used + i], includeNamespace: false);
            }

            builder.Append('>');
            used += arity;
        }
    }
}
