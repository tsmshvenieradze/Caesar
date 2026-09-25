using Caesar.DependencyInjection;
using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.PipelineComposition;

public sealed class ValidationException(string message) : Exception(message);

public sealed class DomainException(string message) : Exception(message);

public sealed record Validated(Journal Journal) : IRequest<string>, IJournaled;

public sealed class ValidatedHandler : IRequestHandler<Validated, string>
{
    public Task<string> Handle(Validated request, CancellationToken cancellationToken)
    {
        request.Journal.Add("handler");
        return Task.FromResult("ok");
    }
}

/// <summary>A user behavior that rejects every journaled request.</summary>
public sealed class RejectingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IJournaled
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        request.Journal.Add("validation");
        throw new ValidationException("invalid");
    }
}

public sealed class RecoverValidated : IRequestExceptionHandler<Validated, string, ValidationException>
{
    public Task Handle(Validated request, ValidationException exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
    {
        request.Journal.Add("recover");
        state.SetHandled("recovered");
        return Task.CompletedTask;
    }
}

public sealed record Failing(Journal Journal) : IRequest<string>;

public sealed class FailingHandler : IRequestHandler<Failing, string>
{
    public Task<string> Handle(Failing request, CancellationToken cancellationToken)
    {
        request.Journal.Add("handler");
        throw new DomainException("boom");
    }
}

public sealed class RecoverFailing : IRequestExceptionHandler<Failing, string, DomainException>
{
    public Task Handle(Failing request, DomainException exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
    {
        request.Journal.Add("recover");
        state.SetHandled("recovered");
        return Task.CompletedTask;
    }
}

public sealed class FailingAction : IRequestExceptionAction<Failing>
{
    public Task Execute(Failing request, Exception exception, CancellationToken cancellationToken)
    {
        request.Journal.Add("action");
        return Task.CompletedTask;
    }
}

public sealed record Ordered(Journal Journal, bool Fail) : IRequest<string>, IJournaled;

public sealed class OrderedHandler : IRequestHandler<Ordered, string>
{
    public Task<string> Handle(Ordered request, CancellationToken cancellationToken)
    {
        request.Journal.Add("handler");
        return request.Fail ? throw new DomainException("boom") : Task.FromResult("ok");
    }
}

public sealed class TracingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IJournaled
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        request.Journal.Add("before");
        var response = await next(cancellationToken);
        request.Journal.Add("after");
        return response;
    }
}

public sealed class OrderedPre : IRequestPreProcessor<Ordered>
{
    public Task Process(Ordered request, CancellationToken cancellationToken)
    {
        request.Journal.Add("pre");
        return Task.CompletedTask;
    }
}

public sealed class OrderedPost : IRequestPostProcessor<Ordered, string>
{
    public Task Process(Ordered request, string response, CancellationToken cancellationToken)
    {
        request.Journal.Add("post");
        return Task.CompletedTask;
    }
}

/// <summary>Sees every failure but never recovers, so the action behind it still runs.</summary>
public sealed class OrderedObserver : IRequestExceptionHandler<Ordered, string, DomainException>
{
    public Task Handle(Ordered request, DomainException exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
    {
        request.Journal.Add("observer");
        return Task.CompletedTask;
    }
}

public sealed class OrderedAction : IRequestExceptionAction<Ordered>
{
    public Task Execute(Ordered request, Exception exception, CancellationToken cancellationToken)
    {
        request.Journal.Add("action");
        return Task.CompletedTask;
    }
}

public sealed record Late(Journal Journal) : IRequest<string>;

public sealed class LateHandler : IRequestHandler<Late, string>
{
    public Task<string> Handle(Late request, CancellationToken cancellationToken)
    {
        request.Journal.Add("handler");
        return Task.FromResult("ok");
    }
}

public sealed class LatePre : IRequestPreProcessor<Late>
{
    public Task Process(Late request, CancellationToken cancellationToken)
    {
        request.Journal.Add("late-pre");
        return Task.CompletedTask;
    }
}

public sealed record LateBoom(Journal Journal) : IRequest<string>;

public sealed class LateBoomHandler : IRequestHandler<LateBoom, string>
{
    public Task<string> Handle(LateBoom request, CancellationToken cancellationToken) => throw new InvalidOperationException("late boom");
}

public sealed class LateRecover : IRequestExceptionHandler<LateBoom, string, InvalidOperationException>
{
    public Task Handle(LateBoom request, InvalidOperationException exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
    {
        request.Journal.Add("late-recover");
        state.SetHandled("recovered");
        return Task.CompletedTask;
    }
}

public sealed record LateUnrecovered(Journal Journal) : IRequest<string>;

public sealed class LateUnrecoveredHandler : IRequestHandler<LateUnrecovered, string>
{
    public Task<string> Handle(LateUnrecovered request, CancellationToken cancellationToken) => throw new InvalidOperationException("unrecovered");
}

public sealed class LateAction : IRequestExceptionAction<LateUnrecovered, InvalidOperationException>
{
    public Task Execute(LateUnrecovered request, InvalidOperationException exception, CancellationToken cancellationToken)
    {
        request.Journal.Add("late-action");
        return Task.CompletedTask;
    }
}

public sealed record Plain : IRequest<string>;

public sealed class PlainHandler : IRequestHandler<Plain, string>
{
    public async Task<string> Handle(Plain request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        throw new DomainException("plain failed");
    }
}

public sealed record Other(Journal Journal) : IRequest<string>;

public sealed class OtherHandler : IRequestHandler<Other, string>
{
    public Task<string> Handle(Other request, CancellationToken cancellationToken) => throw new DomainException("other failed");
}

public sealed class OtherPre : IRequestPreProcessor<Other>
{
    public Task Process(Other request, CancellationToken cancellationToken)
    {
        request.Journal.Add("other-pre");
        return Task.CompletedTask;
    }
}

public sealed class OtherPost : IRequestPostProcessor<Other, string>
{
    public Task Process(Other request, string response, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class OtherRecover : IRequestExceptionHandler<Other, string, DomainException>
{
    public Task Handle(Other request, DomainException exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
    {
        request.Journal.Add("other-recover");
        state.SetHandled("recovered");
        return Task.CompletedTask;
    }
}

public sealed class OtherAction : IRequestExceptionAction<Other>
{
    public Task Execute(Other request, Exception exception, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record Guarded(Journal Journal) : IRequest<string>;

public sealed class GuardedHandler : IRequestHandler<Guarded, string>
{
    public Task<string> Handle(Guarded request, CancellationToken cancellationToken)
    {
        request.Journal.Add("handler");
        return Task.FromResult("ok");
    }
}

public sealed class GuardedPre : IRequestPreProcessor<Guarded>
{
    public Task Process(Guarded request, CancellationToken cancellationToken)
    {
        request.Journal.Add("pre");
        return Task.CompletedTask;
    }
}

public class PipelineCompositionTests
{
    private static readonly Type[] AlwaysScanned = [typeof(ValidatedHandler), typeof(FailingHandler), typeof(OrderedHandler), typeof(LateHandler), typeof(LateBoomHandler), typeof(LateUnrecoveredHandler), typeof(PlainHandler), typeof(OtherHandler), typeof(GuardedHandler)];

    /// <summary>One AddCaesar call that scans the request handlers plus <paramref name="scanned"/>.</summary>
    private static void AddCaesar(IServiceCollection services, Type[] scanned, Action<CaesarServiceConfiguration>? configure = null)
        => services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<PipelineCompositionTests>();
            cfg.TypeEvaluator = t => AlwaysScanned.Contains(t) || scanned.Contains(t);
            configure?.Invoke(cfg);
        });

    private static TestProvider Build(IServiceCollection services)
        => new(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }));

    [Fact]
    public async Task Exception_handler_added_by_a_later_AddCaesar_call_still_wraps_earlier_user_behaviors()
    {
        var services = new ServiceCollection();
        AddCaesar(services, [], cfg => cfg.AddOpenBehavior(typeof(RejectingBehavior<,>)));
        AddCaesar(services, [typeof(RecoverValidated)]);
        await using var provider = Build(services);
        var journal = new Journal();

        var response = await provider.GetRequiredService<ISender>().Send(new Validated(journal));

        Assert.Equal("recovered", response);
        Assert.Equal(["validation", "recover"], journal.Entries);
    }

    [Fact]
    public async Task Exception_action_added_by_a_later_AddCaesar_call_stays_outside_the_exception_handlers()
    {
        var services = new ServiceCollection();
        AddCaesar(services, [typeof(RecoverFailing)]);
        AddCaesar(services, [typeof(FailingAction)]);
        await using var provider = Build(services);
        var journal = new Journal();

        var response = await provider.GetRequiredService<ISender>().Send(new Failing(journal));

        Assert.Equal("recovered", response);
        Assert.Equal(["handler", "recover"], journal.Entries);
    }

    [Fact]
    public async Task Built_in_stages_keep_the_documented_order_across_two_AddCaesar_calls()
    {
        var services = new ServiceCollection();
        AddCaesar(services, [typeof(OrderedAction)], cfg => cfg.AddOpenBehavior(typeof(TracingBehavior<,>)));
        AddCaesar(services, [typeof(OrderedPre), typeof(OrderedPost), typeof(OrderedObserver)]);
        await using var provider = Build(services);
        var sender = provider.GetRequiredService<ISender>();
        var succeeded = new Journal();
        var failed = new Journal();

        await sender.Send(new Ordered(succeeded, Fail: false));
        await Assert.ThrowsAsync<DomainException>(() => sender.Send(new Ordered(failed, Fail: true)));

        Assert.Equal(["pre", "before", "handler", "after", "post"], succeeded.Entries);
        Assert.Equal(["pre", "before", "handler", "observer", "action"], failed.Entries);
    }

    [Fact]
    public async Task ApplyForAllExceptions_puts_actions_inside_the_exception_handlers_across_two_calls()
    {
        var services = new ServiceCollection();
        AddCaesar(services, [typeof(RecoverFailing)], cfg => cfg.RequestExceptionActionProcessorStrategy = RequestExceptionActionProcessorStrategy.ApplyForAllExceptions);
        AddCaesar(services, [typeof(FailingAction)]);
        await using var provider = Build(services);
        var journal = new Journal();

        var response = await provider.GetRequiredService<ISender>().Send(new Failing(journal));

        Assert.Equal("recovered", response);
        Assert.Equal(["handler", "action", "recover"], journal.Entries);
    }

    [Fact]
    public async Task Processors_exception_handlers_and_actions_registered_after_AddCaesar_run()
    {
        var services = new ServiceCollection();
        AddCaesar(services, []);
        services.AddTransient<IRequestPreProcessor<Late>, LatePre>();
        services.AddTransient<IRequestExceptionHandler<LateBoom, string, InvalidOperationException>, LateRecover>();
        services.AddTransient<IRequestExceptionAction<LateUnrecovered, InvalidOperationException>, LateAction>();
        await using var provider = Build(services);
        var sender = provider.GetRequiredService<ISender>();
        var journal = new Journal();

        await sender.Send(new Late(journal));
        var recovered = await sender.Send(new LateBoom(journal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(new LateUnrecovered(journal)));

        Assert.Equal("recovered", recovered);
        Assert.Equal(["late-pre", "handler", "late-recover", "late-action"], journal.Entries);
    }

    [Fact]
    public async Task A_second_provider_built_from_the_same_collection_sees_registrations_added_in_between()
    {
        var services = new ServiceCollection();
        AddCaesar(services, []);
        await using (var first = Build(services))
        {
            var journal = new Journal();
            await first.GetRequiredService<ISender>().Send(new Late(journal));
            Assert.Equal(["handler"], journal.Entries);
        }

        services.AddTransient<IRequestPreProcessor<Late>, LatePre>();
        await using var second = Build(services);
        var later = new Journal();

        await second.GetRequiredService<ISender>().Send(new Late(later));

        Assert.Equal(["late-pre", "handler"], later.Entries);
    }

    [Fact]
    public async Task Processors_and_exception_handlers_of_another_request_add_no_stages_to_this_one()
    {
        var services = new ServiceCollection();
        AddCaesar(services, [typeof(OtherPre), typeof(OtherPost), typeof(OtherRecover), typeof(OtherAction)]);
        await using var provider = Build(services);
        var sender = provider.GetRequiredService<ISender>();
        var journal = new Journal();

        var exception = await Assert.ThrowsAsync<DomainException>(() => sender.Send(new Plain()));
        var other = await sender.Send(new Other(journal));

        // Every built-in stage lives in Caesar.Pipeline; none of them may have wrapped Plain's handler.
        Assert.DoesNotContain("Caesar.Pipeline.", exception.StackTrace, StringComparison.Ordinal);
        Assert.Equal("recovered", other);
        Assert.Equal(["other-pre", "other-recover"], journal.Entries);
    }

    [Fact]
    public async Task Built_in_behavior_registered_by_the_user_as_an_open_behavior_runs_once()
    {
        var services = new ServiceCollection();
        AddCaesar(services, [typeof(GuardedPre)]);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RequestPreProcessorBehavior<,>));
        await using var provider = Build(services);
        var journal = new Journal();

        await provider.GetRequiredService<ISender>().Send(new Guarded(journal));

        Assert.Equal(["pre", "handler"], journal.Entries);
    }

    [Fact]
    public async Task Built_in_behavior_registered_by_the_user_as_a_closed_behavior_runs_once()
    {
        var services = new ServiceCollection();
        AddCaesar(services, [typeof(GuardedPre)]);
        services.AddTransient<IPipelineBehavior<Guarded, string>, RequestPreProcessorBehavior<Guarded, string>>();
        await using var provider = Build(services);
        var journal = new Journal();

        await provider.GetRequiredService<ISender>().Send(new Guarded(journal));

        Assert.Equal(["pre", "handler"], journal.Entries);
    }
}
