namespace Caesar;

/// <summary>
/// Continues the request pipeline: invokes the next behavior or, at the end, the handler.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="cancellationToken">
/// Optional token that replaces the one flowing through the pipeline for the remainder of the chain.
/// Leave it at its default to keep the original token.
/// </param>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken = default);

/// <summary>
/// Wraps the handling of a request. Behaviors are executed in registration order and form a
/// Russian-doll chain around the <see cref="IRequestHandler{TRequest, TResponse}"/>.
/// Register an open-generic behavior to apply it to every request.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Handles the request, calling <paramref name="next"/> to continue the pipeline.</summary>
    /// <param name="request">The request.</param>
    /// <param name="next">Continues the pipeline. Not calling it short-circuits the request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}
