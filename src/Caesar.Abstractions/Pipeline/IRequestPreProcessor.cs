namespace Caesar.Pipeline;

/// <summary>
/// Runs before the handler of <typeparamref name="TRequest"/>. Cannot alter the response or short-circuit;
/// use an <see cref="IPipelineBehavior{TRequest, TResponse}"/> for that.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
public interface IRequestPreProcessor<in TRequest>
    where TRequest : notnull
{
    /// <summary>Processes the request before the handler runs.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task Process(TRequest request, CancellationToken cancellationToken);
}
