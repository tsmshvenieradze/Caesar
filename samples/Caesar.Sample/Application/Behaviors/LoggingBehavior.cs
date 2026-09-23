using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Caesar.Sample.Application.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Handling {Request}", name);
        try
        {
            return await next(cancellationToken);
        }
        finally
        {
            logger.LogInformation("Handled {Request} in {Elapsed} ms", name, stopwatch.ElapsedMilliseconds);
        }
    }
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
