using Caesar.DependencyInjection;
using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Exceptions;

public sealed class DomainException(string message) : Exception(message);

public sealed class OtherException(string message) : Exception(message);

public sealed record Failing(Journal Journal, Exception ToThrow) : IRequest<string>;

public sealed class FailingHandler : IRequestHandler<Failing, string>
{
    public Task<string> Handle(Failing request, CancellationToken cancellationToken)
    {
        request.Journal.Add("handler");
        throw request.ToThrow;
    }
}

/// <summary>Handles only <see cref="DomainException"/> and recovers.</summary>
public sealed class DomainExceptionHandler : IRequestExceptionHandler<Failing, string, DomainException>
{
    public Task Handle(Failing request, DomainException exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
    {
        request.Journal.Add("domain-handler");
        state.SetHandled("recovered:" + exception.Message);
        return Task.CompletedTask;
    }
}

/// <summary>Sees everything (via the shorthand interface) but only observes.</summary>
public sealed class ObservingExceptionHandler : IRequestExceptionHandler<Failing, string>
{
    public Task Handle(Failing request, Exception exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
    {
        request.Journal.Add("observer:" + exception.GetType().Name);
        return Task.CompletedTask;
    }
}

public sealed class LogAction : IRequestExceptionAction<Failing>
{
    public Task Execute(Failing request, Exception exception, CancellationToken cancellationToken)
    {
        request.Journal.Add("action:" + exception.GetType().Name);
        return Task.CompletedTask;
    }
}

public class ExceptionHandlingTests
{
    [Fact]
    public async Task Specific_exception_handler_recovers_and_returns_its_response()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<ExceptionHandlingTests>();

        var response = await provider.GetRequiredService<ISender>().Send(new Failing(journal, new DomainException("boom")));

        Assert.Equal("recovered:boom", response);
        Assert.Equal(["handler", "domain-handler"], journal.Entries);
    }

    [Fact]
    public async Task Unhandled_exception_is_rethrown_with_its_original_type_after_handlers_and_actions()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<ExceptionHandlingTests>();

        var thrown = await Assert.ThrowsAsync<OtherException>(
            () => provider.GetRequiredService<ISender>().Send(new Failing(journal, new OtherException("nope"))));

        Assert.Equal("nope", thrown.Message);
        Assert.Contains(nameof(FailingHandler), thrown.StackTrace, StringComparison.Ordinal);
        Assert.Equal(["handler", "observer:OtherException", "action:OtherException"], journal.Entries);
    }

    [Fact]
    public async Task Default_strategy_skips_actions_for_handled_exceptions()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<ExceptionHandlingTests>();

        await provider.GetRequiredService<ISender>().Send(new Failing(journal, new DomainException("boom")));

        Assert.DoesNotContain(journal.Entries, e => e.StartsWith("action", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyForAllExceptions_runs_actions_even_when_handled()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<ExceptionHandlingTests>(cfg =>
            cfg.RequestExceptionActionProcessorStrategy = RequestExceptionActionProcessorStrategy.ApplyForAllExceptions);

        var response = await provider.GetRequiredService<ISender>().Send(new Failing(journal, new DomainException("boom")));

        Assert.Equal("recovered:boom", response);
        Assert.Equal(["handler", "action:DomainException", "domain-handler"], journal.Entries);
    }

    [Fact]
    public async Task Exception_behaviors_are_not_registered_when_no_handlers_exist()
    {
        await using var provider = TestHost.Build<ExceptionHandlingTests>(cfg => cfg.TypeEvaluator = t => t == typeof(FailingHandler));

        var behaviors = provider.GetServices<IPipelineBehavior<Failing, string>>();

        Assert.Empty(behaviors);
    }
}
