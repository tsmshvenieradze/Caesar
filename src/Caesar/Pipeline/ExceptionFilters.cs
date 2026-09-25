namespace Caesar.Pipeline;

/// <summary>Shared exception filters for the built-in exception behaviors.</summary>
internal static class ExceptionFilters
{
    /// <summary>
    /// <see langword="true"/> when <paramref name="exception"/> is the caller's own cancellation: an
    /// <see cref="OperationCanceledException"/> while <paramref name="cancellationToken"/>, the token the behavior
    /// received, is cancelled. With <see cref="DependencyInjection.CaesarServiceConfiguration.BypassExceptionHandlingOnCallerCancellation"/>
    /// set, such an exception is rethrown without reaching exception handlers or actions; cancellation from any other
    /// token (an inner timeout, say) is always routed like any other failure.
    /// </summary>
    public static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken)
        => exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
}
