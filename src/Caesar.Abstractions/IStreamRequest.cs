namespace Caesar;

/// <summary>
/// A request whose handler produces a stream of <typeparamref name="TResponse"/> items
/// as an <see cref="IAsyncEnumerable{T}"/>.
/// </summary>
/// <typeparam name="TResponse">The item type.</typeparam>
#pragma warning disable CA1040 // Marker interface is the contract.
public interface IStreamRequest<out TResponse> : IBaseRequest;
#pragma warning restore CA1040
