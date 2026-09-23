namespace Caesar;

/// <summary>
/// Continues the stream pipeline: invokes the next behavior or, at the end, the stream handler.
/// </summary>
/// <typeparam name="TResponse">The item type.</typeparam>
public delegate IAsyncEnumerable<TResponse> StreamHandlerDelegate<out TResponse>();

/// <summary>
/// Wraps the handling of a streaming request.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The item type.</typeparam>
public interface IStreamPipelineBehavior<in TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Handles the request, calling <paramref name="next"/> to obtain the inner stream.</summary>
    /// <param name="request">The request.</param>
    /// <param name="next">Continues the pipeline.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}
