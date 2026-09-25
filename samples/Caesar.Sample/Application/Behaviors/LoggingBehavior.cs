using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Caesar.Sample.Application.Behaviors;

public sealed partial class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var started = Stopwatch.GetTimestamp();
        LogHandling(name);
        try
        {
            var response = await next(cancellationToken);
            LogHandled(name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return response;
        }
        catch (Exception exception)
        {
            // Log and rethrow: an exception handler further out may still recover from it.
            LogFailed(exception, name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {Request}")]
    private partial void LogHandling(string request);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {Request} in {Elapsed:0.0} ms")]
    private partial void LogHandled(string request, double elapsed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Request} failed after {Elapsed:0.0} ms")]
    private partial void LogFailed(Exception exception, string request, double elapsed);
}

public sealed class StreamLoggingBehavior<TRequest, TResponse>(ILogger<StreamLoggingBehavior<TRequest, TResponse>> logger)
    : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var count = 0;
        await foreach (var item in next().WithCancellation(cancellationToken))
        {
            count++;
            yield return item;
        }

        logger.LogInformation("{Request} streamed {Count} item(s)", typeof(TRequest).Name, count);
    }
}
