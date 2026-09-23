namespace Caesar.Pipeline;

/// <summary>
/// Handles an exception of type <typeparamref name="TException"/> (or a derived type) thrown while handling
/// <typeparamref name="TRequest"/>. Call <see cref="RequestExceptionHandlerState{TResponse}.SetHandled"/>
/// to swallow the exception and supply a fallback response; otherwise the exception is rethrown.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <typeparam name="TException">The exception type.</typeparam>
public interface IRequestExceptionHandler<in TRequest, TResponse, in TException>
    where TRequest : notnull
    where TException : Exception
{
    /// <summary>Handles the exception.</summary>
    /// <param name="request">The request that failed.</param>
    /// <param name="exception">The exception.</param>
    /// <param name="state">State used to mark the exception as handled and provide a response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task Handle(TRequest request, TException exception, RequestExceptionHandlerState<TResponse> state, CancellationToken cancellationToken);
}

/// <summary>
/// Shorthand for handling every <see cref="Exception"/> for a request.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestExceptionHandler<in TRequest, TResponse> : IRequestExceptionHandler<TRequest, TResponse, Exception>
    where TRequest : notnull;

/// <summary>
/// Tracks whether an exception was handled and which response to return in its place.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class RequestExceptionHandlerState<TResponse>
{
    /// <summary><see langword="true"/> once a handler called <see cref="SetHandled"/>.</summary>
    public bool Handled { get; private set; }

    /// <summary>The fallback response supplied via <see cref="SetHandled"/>.</summary>
    public TResponse? Response { get; private set; }

    /// <summary>Marks the exception as handled and stores the response to return.</summary>
    /// <param name="response">The response to return instead of throwing.</param>
    public void SetHandled(TResponse response)
    {
        Handled = true;
        Response = response;
    }
}
