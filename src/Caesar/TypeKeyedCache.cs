using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Caesar;

/// <summary>
/// A process-wide cache keyed by <see cref="Type"/> that does not keep collectible types alive.
/// Ordinary types live in a <see cref="ConcurrentDictionary{TKey, TValue}"/>. A type that belongs to a collectible
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>, or is built from one, lives in a
/// <see cref="ConditionalWeakTable{TKey, TValue}"/> instead, so the context can still be unloaded once the host lets go of it.
/// </summary>
/// <typeparam name="TValue">The cached value.</typeparam>
internal sealed class TypeKeyedCache<TValue>
    where TValue : class
{
    private readonly ConcurrentDictionary<Type, TValue> _types = new();
    private readonly ConditionalWeakTable<Type, TValue> _collectibleTypes = new();

    /// <summary>Returns the value cached for <paramref name="key"/>, computing it with <paramref name="factory"/> on a miss.</summary>
    /// <remarks>A factory that throws caches nothing, so a later call computes the value again.</remarks>
    public TValue GetOrAdd(Type key, Func<Type, TValue> factory)
        => _types.TryGetValue(key, out var value) || _collectibleTypes.TryGetValue(key, out value)
            ? value
            : Add(key, factory);

    // Kept out of GetOrAdd: the lambda below captures factory, and the compiler allocates that closure as soon as the
    // declaring method starts, which would cost every cache hit an allocation.
    private TValue Add(Type key, Func<Type, TValue> factory)
        => IsCollectible(key)
            ? _collectibleTypes.GetValue(key, k => factory(k))
            : _types.GetOrAdd(key, factory);

    /// <summary>
    /// <see langword="true"/> when <paramref name="type"/>, its element type, or any of its generic arguments
    /// (recursively) comes from an assembly that can be unloaded.
    /// </summary>
    internal static bool IsCollectible(Type type)
    {
        if (type.IsCollectible || type.Assembly.IsCollectible)
        {
            return true;
        }

        if (type.HasElementType)
        {
            return IsCollectible(type.GetElementType()!);
        }

        if (type.IsConstructedGenericType)
        {
            foreach (var argument in type.GenericTypeArguments)
            {
                if (IsCollectible(argument))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
