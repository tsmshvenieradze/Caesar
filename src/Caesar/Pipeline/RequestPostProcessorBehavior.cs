namespace Caesar.Pipeline;

/// <summary>
/// Runs every registered <see cref="IRequestPostProcessor{TRequest, TResponse}"/> after the handler.
/// </summary>
/// <remarks>
/// This stage is not registered as an <see cref="IPipelineBehavior{TRequest, TResponse}"/>. In a container set up with
/// <c>AddCaesar</c>, the mediator composes it into the pipeline of each request type that has at least one
/// post-processor, and of no other, in the documented order: inside the exception stages and the pre-processors,
/// outside every pipeline behavior. Neither the number nor the order of <c>AddCaesar</c> calls changes that, and
/// post-processors registered after <c>AddCaesar</c> count too. If this behavior is registered as a pipeline behavior
/// anyway, the mediator leaves its own stage out, so the post-processors run once, at that registration's position.
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class RequestPostProcessorBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IRequestPostProcessor<TRequest, TResponse>> _postProcessors;

    /// <summary>Creates the behavior.</summary>
    /// <param name="postProcessors">The post-processors to run, in registration order.</param>
    public RequestPostProcessorBehavior(IEnumerable<IRequestPostProcessor<TRequest, TResponse>> postProcessors)
        => _postProcessors = postProcessors ?? throw new ArgumentNullException(nameof(postProcessors));

    /// <inheritdoc />
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        // The mediator only adds this stage when there are post-processors, but a registration as an ordinary pipeline
        // behavior runs for every request, and most requests have none.
        return _postProcessors is ICollection<IRequestPostProcessor<TRequest, TResponse>> { Count: 0 }
            ? next(cancellationToken)
            : HandleAsync(request, next, cancellationToken);
    }

    private async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken).ConfigureAwait(false);

        foreach (var processor in _postProcessors)
        {
            await processor.Process(request, response, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }
}
