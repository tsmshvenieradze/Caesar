using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Caesar.Docs.Snippets;

#region logging-behavior
public sealed partial class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        LogHandling(typeof(TRequest).Name);
        var response = await next(cancellationToken);
        LogHandled(typeof(TRequest).Name);
        return response;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {Request}")]
    private partial void LogHandling(string request);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {Request}")]
    private partial void LogHandled(string request);
}
#endregion

#region validation-behavior
// Not calling next() short-circuits the pipeline: the handler never runs.
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var errors = validators.SelectMany(v => v.Validate(request)).ToList();
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);   // an async method reports it through the returned task
        }

        return await next(cancellationToken);
    }
}
#endregion

#region closed-behavior
// Applies only to CreateCustomer. Register with cfg.AddBehavior<NormalizeEmail>().
// Not async, so the failure is returned as a faulted task rather than thrown.
public sealed class NormalizeEmail : IPipelineBehavior<CreateCustomer, Guid>
{
    public Task<Guid> Handle(CreateCustomer request, RequestHandlerDelegate<Guid> next, CancellationToken cancellationToken)
        => request.Email == request.Email.Trim()
            ? next(cancellationToken)
            : Task.FromException<Guid>(new ValidationException(["Email has surrounding whitespace."]));
}
#endregion

#region detach-token
// Lets a commit that has started finish even when the caller gives up. next(CancellationToken.None) would not:
// None is the same value as default, so it keeps the caller's token.
public sealed class CommitEvenIfCancelled<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => next(Detached.Token);
}

// A token that is never cancelled but is not CancellationToken.None: its source is never cancelled or disposed.
public static class Detached
{
    private static readonly CancellationTokenSource Source = new();

    public static CancellationToken Token => Source.Token;
}
#endregion

#region stream-behavior
public sealed class CountItemsBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var count = 0;
        await foreach (var item in next().WithCancellation(cancellationToken))
        {
            count++;
            yield return item;
        }

        Console.WriteLine($"{typeof(TRequest).Name} streamed {count} item(s)");
    }
}
#endregion

public static class BehaviorRegistration
{
    public static void Register(IServiceCollection services)
    {
        #region register-behaviors
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CreateCustomer>();

            // Registration order is execution order: the first behavior is the outermost.
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddBehavior<NormalizeEmail>();

            cfg.AddOpenStreamBehavior(typeof(CountItemsBehavior<,>));
        });
        #endregion
    }
}
