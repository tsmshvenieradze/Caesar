namespace Caesar;

/// <summary>
/// Handles a request that returns a response.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Handles the request.</summary>
    /// <param name="request">The request instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response.</returns>
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Handles a request that has no response (a command).
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
public interface IRequestHandler<in TRequest>
    where TRequest : IRequest
{
    /// <summary>Handles the request.</summary>
    /// <param name="request">The request instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task Handle(TRequest request, CancellationToken cancellationToken);
}
