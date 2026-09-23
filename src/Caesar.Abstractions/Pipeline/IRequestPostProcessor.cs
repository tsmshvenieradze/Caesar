namespace Caesar.Pipeline;

/// <summary>
/// Runs after the handler of <typeparamref name="TRequest"/> has produced a response.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestPostProcessor<in TRequest, in TResponse>
    where TRequest : notnull
{
    /// <summary>Processes the request and its response after the handler ran.</summary>
    /// <param name="request">The request.</param>
    /// <param name="response">The response produced by the handler.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task Process(TRequest request, TResponse response, CancellationToken cancellationToken);
}
