using System.Collections.Concurrent;

namespace Caesar.Pipeline;

/// <summary>
/// Yields an exception type followed by its base types, most specific first, stopping before <see cref="object"/>.
/// Cached per exception type because the hierarchy never changes.
/// </summary>
internal static class ExceptionTypeHierarchy
{
    private static readonly ConcurrentDictionary<Type, Type[]> Cache = new();

    public static Type[] Of(Type exceptionType) => Cache.GetOrAdd(exceptionType, static type =>
    {
        var result = new List<Type>();
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            result.Add(current);
        }

        return [.. result];
    });
}
