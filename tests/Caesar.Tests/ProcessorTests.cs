using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Processors;

public sealed record Ping(Journal Journal) : IRequest<string>, IJournaled;

public sealed class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken)
    {
        request.Journal.Add("handler");
        return Task.FromResult("pong");
    }
}

public sealed class FirstPreProcessor : IRequestPreProcessor<Ping>
{
    public Task Process(Ping request, CancellationToken cancellationToken)
    {
        request.Journal.Add("pre-1");
        return Task.CompletedTask;
    }
}

public sealed class SecondPreProcessor : IRequestPreProcessor<Ping>
{
    public Task Process(Ping request, CancellationToken cancellationToken)
    {
        request.Journal.Add("pre-2");
        return Task.CompletedTask;
    }
}

public sealed class PingPostProcessor : IRequestPostProcessor<Ping, string>
{
    public Task Process(Ping request, string response, CancellationToken cancellationToken)
    {
        request.Journal.Add("post:" + response);
        return Task.CompletedTask;
    }
}

public sealed class TracingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IJournaled
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        request.Journal.Add("behavior-before");
        var response = await next(cancellationToken);
        request.Journal.Add("behavior-after");
        return response;
    }
}

public class ProcessorTests
{
    [Fact]
    public async Task Scanned_pre_and_post_processors_run_around_the_handler()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<ProcessorTests>();

        await provider.GetRequiredService<ISender>().Send(new Ping(journal));

        Assert.Equal(["pre-1", "pre-2", "handler", "post:pong"], journal.Entries);
    }

    [Fact]
    public async Task Processors_run_outside_user_behaviors()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<ProcessorTests>(cfg => cfg.AddOpenBehavior(typeof(TracingBehavior<,>)));

        await provider.GetRequiredService<ISender>().Send(new Ping(journal));

        Assert.Equal(["pre-1", "pre-2", "behavior-before", "handler", "behavior-after", "post:pong"], journal.Entries);
    }

    [Fact]
    public async Task Auto_registration_can_be_disabled()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<ProcessorTests>(cfg => cfg.AutoRegisterRequestProcessors = false);

        await provider.GetRequiredService<ISender>().Send(new Ping(journal));

        Assert.Equal(["handler"], journal.Entries);
        Assert.Empty(provider.GetServices<IPipelineBehavior<Ping, string>>());
    }

    [Fact]
    public async Task Processors_can_be_added_explicitly_when_auto_registration_is_off()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<ProcessorTests>(cfg =>
        {
            cfg.AutoRegisterRequestProcessors = false;
            cfg.AddRequestPreProcessor<SecondPreProcessor>();
            cfg.AddRequestPostProcessor<PingPostProcessor>();
        });

        await provider.GetRequiredService<ISender>().Send(new Ping(journal));

        Assert.Equal(["pre-2", "handler", "post:pong"], journal.Entries);
    }

    [Fact]
    public async Task Explicit_and_scanned_registrations_of_the_same_processor_are_not_duplicated()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<ProcessorTests>(cfg => cfg.AddRequestPreProcessor<FirstPreProcessor>());

        await provider.GetRequiredService<ISender>().Send(new Ping(journal));

        Assert.Single(journal.Entries, "pre-1");
    }
}
