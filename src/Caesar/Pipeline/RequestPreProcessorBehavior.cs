namespace Caesar.Pipeline;

/// <summary>
/// Runs every registered <see cref="IRequestPreProcessor{TRequest}"/> before the handler.
/// Registered automatically by <c>AddCaesar</c> when at least one pre-processor exists.
/// </summary>
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
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        foreach (var processor in _preProcessors)
        {
            await processor.Process(request, cancellationToken).ConfigureAwait(false);
        }

        return await next(cancellationToken).ConfigureAwait(false);
    }
}
