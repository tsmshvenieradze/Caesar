using System.Text;

namespace Caesar;

/// <summary>Renders types the way they are written in source, for error messages.</summary>
internal static class TypeNames
{
    /// <summary>
    /// Renders <paramref name="type"/> with its generic arguments, e.g. <c>GetPage&lt;Customer&gt;</c> rather than
    /// <c>GetPage`1</c>. Only the outermost type is namespace-qualified, and only when <paramref name="qualified"/> is set.
    /// </summary>
    public static string Of(Type type, bool qualified = false)
    {
        var builder = new StringBuilder();
        Append(builder, type, qualified);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, Type type, bool qualified)
    {
        if (type.IsArray)
        {
            Append(builder, type.GetElementType()!, qualified);
            builder.Append('[').Append(',', type.GetArrayRank() - 1).Append(']');
            return;
        }

        if (type.IsGenericParameter)
        {
            builder.Append(type.Name);
            return;
        }

        if (qualified && !string.IsNullOrEmpty(type.Namespace))
        {
            builder.Append(type.Namespace).Append('.');
        }

        if (type.IsNested && type.DeclaringType is { } declaringType)
        {
            builder.Append(StripArity(declaringType.Name)).Append('.');
        }

        builder.Append(StripArity(type.Name));

        if (type.IsGenericType)
        {
            var arguments = type.GetGenericArguments();
            builder.Append('<');
            for (var i = 0; i < arguments.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                Append(builder, arguments[i], qualified: false);
            }

            builder.Append('>');
        }
    }

    private static string StripArity(string name)
    {
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? name : name[..tick];
    }
}
