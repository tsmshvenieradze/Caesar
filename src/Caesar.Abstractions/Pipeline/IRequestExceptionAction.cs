namespace Caesar.Pipeline;

/// <summary>
/// Runs side effects (logging, metrics, compensation) when an exception of type <typeparamref name="TException"/>
/// (or a derived type) is thrown while handling <typeparamref name="TRequest"/>. The exception is always rethrown afterwards.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TException">The exception type.</typeparam>
public interface IRequestExceptionAction<in TRequest, in TException>
    where TRequest : notnull
    where TException : Exception
{
    /// <summary>Executes the action.</summary>
    /// <param name="request">The request that failed.</param>
    /// <param name="exception">The exception.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task Execute(TRequest request, TException exception, CancellationToken cancellationToken);
}

/// <summary>
/// Shorthand for acting on every <see cref="Exception"/> for a request.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
public interface IRequestExceptionAction<in TRequest> : IRequestExceptionAction<TRequest, Exception>
    where TRequest : notnull;
