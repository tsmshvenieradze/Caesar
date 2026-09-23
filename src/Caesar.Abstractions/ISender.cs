namespace Caesar;

/// <summary>
/// Sends requests to their single handler and creates response streams.
/// </summary>
public interface ISender
{
    /// <summary>Sends a request and returns its response after running the pipeline.</summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>Sends a command (a request without a response) after running the pipeline.</summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest;

    /// <summary>
    /// Sends a request whose type is only known at runtime. The object must implement
    /// <see cref="IRequest{TResponse}"/>; the response is returned as <see cref="object"/>.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<object?> Send(object request, CancellationToken cancellationToken = default);

    /// <summary>Creates a stream for a streaming request, running the stream pipeline.</summary>
    /// <typeparam name="TResponse">The item type.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a stream for a streaming request whose type is only known at runtime.
    /// The object must implement <see cref="IStreamRequest{TResponse}"/>.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default);
}
