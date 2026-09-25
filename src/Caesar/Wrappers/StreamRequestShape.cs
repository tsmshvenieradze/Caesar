using Caesar.DependencyInjection;

namespace Caesar.Wrappers;

/// <summary>
/// What one closed stream request type needs to know about one container, worked out once from its
/// <see cref="CaesarRegistry"/>. It is an open-generic singleton, so the container keeps one per request type.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The item type.</typeparam>
internal sealed class StreamRequestShape<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    /// <summary>Creates the shape. Called by the container.</summary>
    /// <param name="registry">What the container's service collection registers.</param>
    public StreamRequestShape(CaesarRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var handler = registry.GetAvailability(typeof(IStreamRequestHandler<TRequest, TResponse>));
        ProbeHandler = handler != ServiceAvailability.ConstraintViolation;
        HasHandler = handler == ServiceAvailability.Registered;
    }

    /// <summary>
    /// <see langword="false"/> when the only registration for <see cref="IStreamRequestHandler{TRequest, TResponse}"/> is
    /// an open generic whose constraints reject <typeparamref name="TRequest"/>, so asking the container would throw.
    /// </summary>
    public bool ProbeHandler { get; }

    /// <summary><see langword="true"/> when <see cref="IStreamRequestHandler{TRequest, TResponse}"/> itself is registered and can be resolved.</summary>
    public bool HasHandler { get; }
}
