namespace Caesar;

/// <summary>
/// Handles a streaming request.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The item type.</typeparam>
public interface IStreamRequestHandler<in TRequest, out TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    /// <summary>Handles the request and yields items.</summary>
    /// <param name="request">The request instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}
