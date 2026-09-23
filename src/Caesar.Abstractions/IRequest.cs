namespace Caesar;

/// <summary>
/// A request that produces a response of type <typeparamref name="TResponse"/>.
/// Exactly one <see cref="IRequestHandler{TRequest, TResponse}"/> handles it.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
#pragma warning disable CA1040 // Marker interfaces are the point of the request contract.
public interface IRequest<out TResponse> : IBaseRequest;

/// <summary>
/// A request without a meaningful response (a command). Its response type is <see cref="Unit"/>.
/// Handle it with <see cref="IRequestHandler{TRequest}"/>.
/// </summary>
public interface IRequest : IRequest<Unit>;
#pragma warning restore CA1040
