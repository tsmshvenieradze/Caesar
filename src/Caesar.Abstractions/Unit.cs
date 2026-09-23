using System.Diagnostics.CodeAnalysis;

namespace Caesar;

/// <summary>
/// Represents a void return type. Used as the response of <see cref="IRequest"/>.
/// </summary>
public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>, IComparable
{
    /// <summary>The single <see cref="Unit"/> value.</summary>
    public static readonly Unit Value;

    /// <summary>A completed task whose result is <see cref="Value"/>.</summary>
    public static readonly Task<Unit> Task = System.Threading.Tasks.Task.FromResult(Value);

    /// <inheritdoc />
    public int CompareTo(Unit other) => 0;

    /// <inheritdoc />
    int IComparable.CompareTo(object? obj) => 0;

    /// <inheritdoc />
    public override int GetHashCode() => 0;

    /// <inheritdoc />
    public bool Equals(Unit other) => true;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Unit;

    /// <summary>Always <see langword="true"/>.</summary>
    public static bool operator ==(Unit first, Unit second) => true;

    /// <summary>Always <see langword="false"/>.</summary>
    public static bool operator !=(Unit first, Unit second) => false;

    /// <summary>Always <see langword="false"/>.</summary>
    public static bool operator <(Unit left, Unit right) => false;

    /// <summary>Always <see langword="true"/>.</summary>
    public static bool operator <=(Unit left, Unit right) => true;

    /// <summary>Always <see langword="false"/>.</summary>
    public static bool operator >(Unit left, Unit right) => false;

    /// <summary>Always <see langword="true"/>.</summary>
    public static bool operator >=(Unit left, Unit right) => true;

    /// <inheritdoc />
    public override string ToString() => "()";
}
