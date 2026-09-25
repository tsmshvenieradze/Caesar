namespace Caesar.Pipeline;

/// <summary>
/// Runs every registered <see cref="IRequestPreProcessor{TRequest}"/> before the handler.
/// </summary>
/// <remarks>
/// This stage is not registered as an <see cref="IPipelineBehavior{TRequest, TResponse}"/>. In a container set up with
/// <c>AddCaesar</c>, the mediator composes it into the pipeline of each request type that has at least one
/// pre-processor, and of no other, in the documented order: inside the exception stages, outside the post-processors
/// and every pipeline behavior. Neither the number nor the order of <c>AddCaesar</c> calls changes that, and
/// pre-processors registered after <c>AddCaesar</c> count too. If this behavior is registered as a pipeline behavior
/// anyway, the mediator leaves its own stage out, so the pre-processors run once, at that registration's position.
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class RequestPreProcessorBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IRequestPreProcessor<TRequest>> _preProcessors;

    /// <summary>Creates the behavior.</summary>
    /// <param name="preProcessors">The pre-processors to run, in registration order.</param>
    public RequestPreProcessorBehavior(IEnumerable<IRequestPreProcessor<TRequest>> preProcessors)
        => _preProcessors = preProcessors ?? throw new ArgumentNullException(nameof(preProcessors));

    /// <inheritdoc />
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        // The mediator only adds this stage when there are pre-processors, but a registration as an ordinary pipeline
        // behavior runs for every request, and most requests have none.
        return _preProcessors is ICollection<IRequestPreProcessor<TRequest>> { Count: 0 }
            ? next(cancellationToken)
            : HandleAsync(request, next, cancellationToken);
    }

    private async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        foreach (var processor in _preProcessors)
        {
            await processor.Process(request, cancellationToken).ConfigureAwait(false);
        }

        return await next(cancellationToken).ConfigureAwait(false);
    }
}
