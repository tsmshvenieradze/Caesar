namespace Caesar.Pipeline;

/// <summary>
/// Runs every registered <see cref="IRequestPostProcessor{TRequest, TResponse}"/> after the handler.
/// Registered automatically by <c>AddCaesar</c> when at least one post-processor exists.
/// </summary>
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
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var response = await next(cancellationToken).ConfigureAwait(false);

        foreach (var processor in _postProcessors)
        {
            await processor.Process(request, response, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }
}
