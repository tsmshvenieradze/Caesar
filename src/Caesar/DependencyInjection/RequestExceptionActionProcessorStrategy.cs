namespace Caesar.DependencyInjection;

/// <summary>
/// Controls where <see cref="Pipeline.RequestExceptionActionProcessorBehavior{TRequest, TResponse}"/> sits relative to
/// <see cref="Pipeline.RequestExceptionProcessorBehavior{TRequest, TResponse}"/> in the pipeline.
/// </summary>
public enum RequestExceptionActionProcessorStrategy
{
    /// <summary>
    /// Actions run only for exceptions that no <see cref="Pipeline.IRequestExceptionHandler{TRequest, TResponse, TException}"/> handled.
    /// The action behavior is the outermost.
    /// </summary>
    ApplyForUnhandledExceptions,

    /// <summary>
    /// Actions run for every exception, even ones that an exception handler subsequently handles.
    /// The action behavior sits directly inside the handler behavior.
    /// </summary>
    ApplyForAllExceptions,
}
