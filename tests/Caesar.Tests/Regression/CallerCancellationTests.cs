using Caesar.DependencyInjection;
using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.CallerCancellation;

// By default an OperationCanceledException from the caller's own token reaches exception handlers and actions like any
// other failure, as in MediatR. With BypassExceptionHandlingOnCallerCancellation it propagates without reaching them,
// whether it is thrown synchronously or from an awaited task. Cancellation from any other token is always routed.

public enum Failure
{
    /// <summary>Cancel the caller's token, then observe it.</summary>
    CallerCancelled,

    /// <summary>Time out on a token the handler owns; the caller's token stays live.</summary>
    InnerTimeout,

    /// <summary>Cancel the caller's token, then fail with something other than cancellation.</summary>
    CallerCancelledThenOtherFailure,
}

public sealed record Slow(Journal Journal, CancellationTokenSource Caller, Failure Failure) : IRequest<string>;

public sealed class SlowHandler : IRequestHandler<Slow, string>
{
    public async Task<string> Handle(Slow request, CancellationToken cancellationToken)
    {
        await Task.Yield();

        switch (request.Failure)
        {
            case Failure.CallerCancelled:
                await request.Caller.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
                break;
            case Failure.InnerTimeout:
                using (var timeout = new CancellationTokenSource())
                {
                    await timeout.CancelAsync();
                    timeout.Token.ThrowIfCancellationRequested();
                }

                break;
            case Failure.CallerCancelledThenOtherFailure:
                await request.Caller.CancelAsync();
                throw new InvalidOperationException("not cancellation");
        }

        return "done";
    }
}

/// <summary>Cancels the caller and throws before returning a task, so the stages see a synchronous throw.</summary>
public sealed record Immediate(Journal Journal, CancellationTokenSource Caller) : IRequest<string>;

public sealed class ImmediateHandler : IRequestHandler<Immediate, string>
{
    public Task<string> Handle(Immediate request, CancellationToken cancellationToken)
    {
        request.Caller.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("done");
    }
}

public sealed class CatchAll : IRequestExceptionHandler<Slow, string>
{
    public Task Handle(Slow request, Exception exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
    {
        request.Journal.Add("handler:" + exception.GetType().Name);
        state.SetHandled("fallback:" + exception.GetType().Name);
        return Task.CompletedTask;
    }
}

public sealed class LogAll : IRequestExceptionAction<Slow>
{
    public Task Execute(Slow request, Exception exception, CancellationToken cancellationToken)
    {
        request.Journal.Add("action:" + exception.GetType().Name);
        return Task.CompletedTask;
    }
}

public sealed class CatchAllImmediate : IRequestExceptionHandler<Immediate, string>
{
    public Task Handle(Immediate request, Exception exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
    {
        request.Journal.Add("handler:" + exception.GetType().Name);
        state.SetHandled("fallback");
        return Task.CompletedTask;
    }
}

public sealed class LogAllImmediate : IRequestExceptionAction<Immediate>
{
    public Task Execute(Immediate request, Exception exception, CancellationToken cancellationToken)
    {
        request.Journal.Add("action:" + exception.GetType().Name);
        return Task.CompletedTask;
    }
}

public class CallerCancellationTests
{
    private static TestProvider Build(RequestExceptionActionProcessorStrategy strategy, bool bypass)
        => TestHost.Build<CallerCancellationTests>(cfg =>
        {
            cfg.RequestExceptionActionProcessorStrategy = strategy;
            cfg.BypassExceptionHandlingOnCallerCancellation = bypass;
        });

    [Theory]
    [InlineData(RequestExceptionActionProcessorStrategy.ApplyForUnhandledExceptions)]
    [InlineData(RequestExceptionActionProcessorStrategy.ApplyForAllExceptions)]
    public async Task By_default_caller_cancellation_is_routed_like_any_other_failure(RequestExceptionActionProcessorStrategy strategy)
    {
        var journal = new Journal();
        using var caller = new CancellationTokenSource();
        await using var provider = Build(strategy, bypass: false);

        var response = await provider.GetRequiredService<ISender>().Send(new Slow(journal, caller, Failure.CallerCancelled), caller.Token);

        Assert.Equal("fallback:OperationCanceledException", response);
        Assert.Contains("handler:OperationCanceledException", journal.Entries);
    }

    [Theory]
    [InlineData(RequestExceptionActionProcessorStrategy.ApplyForUnhandledExceptions)]
    [InlineData(RequestExceptionActionProcessorStrategy.ApplyForAllExceptions)]
    public async Task With_the_bypass_caller_cancellation_reaches_no_handler_or_action(RequestExceptionActionProcessorStrategy strategy)
    {
        var journal = new Journal();
        using var caller = new CancellationTokenSource();
        await using var provider = Build(strategy, bypass: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetRequiredService<ISender>().Send(new Slow(journal, caller, Failure.CallerCancelled), caller.Token));

        Assert.Empty(journal.Entries);
    }

    [Theory]
    [InlineData(RequestExceptionActionProcessorStrategy.ApplyForUnhandledExceptions)]
    [InlineData(RequestExceptionActionProcessorStrategy.ApplyForAllExceptions)]
    public async Task With_the_bypass_a_synchronously_thrown_caller_cancellation_reaches_no_handler_or_action(RequestExceptionActionProcessorStrategy strategy)
    {
        var journal = new Journal();
        using var caller = new CancellationTokenSource();
        await using var provider = Build(strategy, bypass: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetRequiredService<ISender>().Send(new Immediate(journal, caller), caller.Token));

        Assert.Empty(journal.Entries);
    }

    [Fact]
    public async Task Without_the_bypass_a_synchronously_thrown_caller_cancellation_is_routed()
    {
        var journal = new Journal();
        using var caller = new CancellationTokenSource();
        await using var provider = Build(RequestExceptionActionProcessorStrategy.ApplyForAllExceptions, bypass: false);

        var response = await provider.GetRequiredService<ISender>().Send(new Immediate(journal, caller), caller.Token);

        Assert.Equal("fallback", response);
        Assert.Equal(["action:OperationCanceledException", "handler:OperationCanceledException"], journal.Entries);
    }

    [Fact]
    public async Task With_the_bypass_cancellation_from_an_inner_token_is_still_routed()
    {
        var journal = new Journal();
        using var caller = new CancellationTokenSource();
        await using var provider = Build(RequestExceptionActionProcessorStrategy.ApplyForAllExceptions, bypass: true);

        var response = await provider.GetRequiredService<ISender>().Send(new Slow(journal, caller, Failure.InnerTimeout), caller.Token);

        Assert.Equal("fallback:OperationCanceledException", response);
        Assert.Equal(["action:OperationCanceledException", "handler:OperationCanceledException"], journal.Entries);
    }

    [Fact]
    public async Task With_the_bypass_other_failures_are_still_routed_after_the_caller_cancelled()
    {
        var journal = new Journal();
        using var caller = new CancellationTokenSource();
        await using var provider = Build(RequestExceptionActionProcessorStrategy.ApplyForUnhandledExceptions, bypass: true);

        var response = await provider.GetRequiredService<ISender>().Send(
            new Slow(journal, caller, Failure.CallerCancelledThenOtherFailure), caller.Token);

        Assert.Equal("fallback:InvalidOperationException", response);
    }
}
