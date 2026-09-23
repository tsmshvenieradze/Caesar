using Caesar.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Behaviors;

public sealed record Ping(Journal Journal) : IRequest<string>;

public sealed class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken)
    {
        request.Journal.Add("handler");
        return Task.FromResult("pong");
    }
}

public sealed record Other(Journal Journal) : IRequest<string>;

public sealed class OtherHandler : IRequestHandler<Other, string>
{
    public Task<string> Handle(Other request, CancellationToken cancellationToken)
    {
        request.Journal.Add("other-handler");
        return Task.FromResult("other");
    }
}

public sealed class OuterBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IJournaled
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        request.Journal.Add("outer-before");
        var response = await next(cancellationToken);
        request.Journal.Add("outer-after");
        return response;
    }
}

public sealed class InnerBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IJournaled
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        request.Journal.Add("inner-before");
        var response = await next(cancellationToken);
        request.Journal.Add("inner-after");
        return response;
    }
}

public sealed class PingOnlyBehavior : IPipelineBehavior<Ping, string>
{
    public async Task<string> Handle(Ping request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
    {
        request.Journal.Add("ping-only");
        return (await next(cancellationToken)).ToUpperInvariant();
    }
}

public sealed class ShortCircuitBehavior : IPipelineBehavior<Ping, string>
{
    public Task<string> Handle(Ping request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
    {
        request.Journal.Add("short-circuit");
        return Task.FromResult("cached");
    }
}

public sealed record TokenProbe : IRequest<CancellationToken>;

public sealed class TokenProbeHandler : IRequestHandler<TokenProbe, CancellationToken>
{
    public Task<CancellationToken> Handle(TokenProbe request, CancellationToken cancellationToken) => Task.FromResult(cancellationToken);
}

public sealed class ReplaceTokenBehavior : IPipelineBehavior<TokenProbe, CancellationToken>
{
    public static readonly CancellationTokenSource Replacement = new();

    public Task<CancellationToken> Handle(TokenProbe request, RequestHandlerDelegate<CancellationToken> next, CancellationToken cancellationToken)
        => next(Replacement.Token);
}

public sealed class PassThroughBehavior : IPipelineBehavior<TokenProbe, CancellationToken>
{
#pragma warning disable CA2016 // Deliberately calls next() without a token to prove the original token is kept.
    public Task<CancellationToken> Handle(TokenProbe request, RequestHandlerDelegate<CancellationToken> next, CancellationToken cancellationToken) => next();
#pragma warning restore CA2016
}

public class PipelineBehaviorTests
{
    [Fact]
    public async Task Open_behaviors_run_in_registration_order_around_the_handler()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<PipelineBehaviorTests>(cfg =>
        {
            cfg.AddOpenBehavior(typeof(OuterBehavior<,>));
            cfg.AddOpenBehavior(typeof(InnerBehavior<,>));
        });
        var request = new JournaledRequest(journal);

        var response = await provider.GetRequiredService<ISender>().Send(request);

        Assert.Equal("pong", response);
        Assert.Equal(["outer-before", "inner-before", "handler", "inner-after", "outer-after"], journal.Entries);
    }

    [Fact]
    public async Task Closed_behavior_applies_only_to_its_request_type()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<PipelineBehaviorTests>(cfg => cfg.AddBehavior<PingOnlyBehavior>());
        var sender = provider.GetRequiredService<ISender>();

        var ping = await sender.Send(new Ping(journal));
        var other = await sender.Send(new Other(journal));

        Assert.Equal("PONG", ping);
        Assert.Equal("other", other);
        Assert.Equal(["ping-only", "handler", "other-handler"], journal.Entries);
    }

    [Fact]
    public async Task Behavior_can_short_circuit_without_calling_the_handler()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<PipelineBehaviorTests>(cfg => cfg.AddBehavior<ShortCircuitBehavior>());

        var response = await provider.GetRequiredService<ISender>().Send(new Ping(journal));

        Assert.Equal("cached", response);
        Assert.Equal(["short-circuit"], journal.Entries);
    }

    [Fact]
    public async Task Behavior_can_replace_the_cancellation_token_for_the_rest_of_the_chain()
    {
        await using var provider = TestHost.Build<PipelineBehaviorTests>(cfg =>
        {
            cfg.AddBehavior<ReplaceTokenBehavior>();
            cfg.AddBehavior<PassThroughBehavior>();
        });
        using var original = new CancellationTokenSource();

        var seen = await provider.GetRequiredService<ISender>().Send(new TokenProbe(), original.Token);

        Assert.Equal(ReplaceTokenBehavior.Replacement.Token, seen);
    }

    [Fact]
    public async Task Calling_next_without_a_token_keeps_the_original_token()
    {
        await using var provider = TestHost.Build<PipelineBehaviorTests>(cfg => cfg.AddBehavior<PassThroughBehavior>());
        using var original = new CancellationTokenSource();

        var seen = await provider.GetRequiredService<ISender>().Send(new TokenProbe(), original.Token);

        Assert.Equal(original.Token, seen);
    }

    [Fact]
    public void AddOpenBehavior_rejects_closed_types()
    {
        var configuration = new CaesarServiceConfiguration();

        Assert.Throws<InvalidOperationException>(() => configuration.AddOpenBehavior(typeof(PingOnlyBehavior)));
    }

    [Fact]
    public void AddOpenBehavior_rejects_types_that_do_not_implement_the_interface()
    {
        var configuration = new CaesarServiceConfiguration();

        Assert.Throws<InvalidOperationException>(() => configuration.AddOpenBehavior(typeof(List<>)));
    }

    [Fact]
    public void AddBehavior_rejects_types_that_do_not_implement_the_interface()
    {
        var configuration = new CaesarServiceConfiguration();

        Assert.Throws<InvalidOperationException>(() => configuration.AddBehavior<PingHandler>());
    }
}

public sealed record JournaledRequest(Journal Journal) : IRequest<string>, IJournaled;

public sealed class JournaledRequestHandler : IRequestHandler<JournaledRequest, string>
{
    public Task<string> Handle(JournaledRequest request, CancellationToken cancellationToken)
    {
        request.Journal.Add("handler");
        return Task.FromResult("pong");
    }
}
