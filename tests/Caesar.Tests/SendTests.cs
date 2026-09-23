using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Caesar.Tests.Send;

public sealed record Ping(string Message) : IRequest<Pong>;
public sealed record Pong(string Message);

public sealed class PingHandler : IRequestHandler<Ping, Pong>
{
    public Task<Pong> Handle(Ping request, CancellationToken cancellationToken) => Task.FromResult(new Pong(request.Message + " Pong"));
}

public sealed record VoidCommand(Journal Journal) : IRequest;

public sealed class VoidCommandHandler : IRequestHandler<VoidCommand>
{
    public Task Handle(VoidCommand request, CancellationToken cancellationToken)
    {
        request.Journal.Add("void-handler");
        return Task.CompletedTask;
    }
}

public sealed record UnitCommand(Journal Journal) : IRequest;

public sealed class UnitCommandHandler : IRequestHandler<UnitCommand, Unit>
{
    public Task<Unit> Handle(UnitCommand request, CancellationToken cancellationToken)
    {
        request.Journal.Add("unit-handler");
        return Unit.Task;
    }
}

public sealed record Orphan : IRequest<string>;

public sealed record TokenProbe : IRequest<CancellationToken>;

public sealed class TokenProbeHandler : IRequestHandler<TokenProbe, CancellationToken>
{
    public Task<CancellationToken> Handle(TokenProbe request, CancellationToken cancellationToken) => Task.FromResult(cancellationToken);
}

public sealed record NeedsDependency : IRequest<string>;

public sealed class NeedsDependencyHandler(Journal journal) : IRequestHandler<NeedsDependency, string>
{
    public Task<string> Handle(NeedsDependency request, CancellationToken cancellationToken)
    {
        journal.Add("resolved");
        return Task.FromResult("ok");
    }
}

public class SendTests
{
    [Fact]
    public async Task Send_returns_handler_response()
    {
        await using var provider = TestHost.Build<SendTests>();
        var mediator = provider.GetRequiredService<IMediator>();

        var response = await mediator.Send(new Ping("Ping"));

        Assert.Equal("Ping Pong", response.Message);
    }

    [Fact]
    public async Task Send_void_command_uses_IRequestHandler_of_TRequest()
    {
        await using var provider = TestHost.Build<SendTests>();
        var journal = new Journal();

        await provider.GetRequiredService<ISender>().Send(new VoidCommand(journal));

        Assert.Equal(["void-handler"], journal.Entries);
    }

    [Fact]
    public async Task Send_void_command_falls_back_to_Unit_handler()
    {
        await using var provider = TestHost.Build<SendTests>();
        var journal = new Journal();

        await provider.GetRequiredService<ISender>().Send(new UnitCommand(journal));

        Assert.Equal(["unit-handler"], journal.Entries);
    }

    [Fact]
    public async Task Send_void_command_through_IRequest_of_Unit_overload_also_works()
    {
        await using var provider = TestHost.Build<SendTests>();
        var journal = new Journal();
        IRequest<Unit> request = new VoidCommand(journal);

        var unit = await provider.GetRequiredService<ISender>().Send(request);

        Assert.Equal(Unit.Value, unit);
        Assert.Equal(["void-handler"], journal.Entries);
    }

    [Fact]
    public async Task Send_object_resolves_response_type_at_runtime()
    {
        await using var provider = TestHost.Build<SendTests>();
        object request = new Ping("Dynamic");

        var response = await provider.GetRequiredService<ISender>().Send(request);

        var pong = Assert.IsType<Pong>(response);
        Assert.Equal("Dynamic Pong", pong.Message);
    }

    [Fact]
    public async Task Send_object_void_command_returns_Unit()
    {
        await using var provider = TestHost.Build<SendTests>();
        object request = new VoidCommand(new Journal());

        var response = await provider.GetRequiredService<ISender>().Send(request);

        Assert.IsType<Unit>(response);
    }

    [Fact]
    public async Task Send_object_that_is_not_a_request_throws()
    {
        await using var provider = TestHost.Build<SendTests>();

        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetRequiredService<ISender>().Send(new object()));
    }

    [Fact]
    public async Task Send_without_registered_handler_throws_helpful_error()
    {
        await using var provider = TestHost.Build<SendTests>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<ISender>().Send(new Orphan()));

        Assert.Contains("Register your handlers", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(Orphan), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_null_throws()
    {
        await using var provider = TestHost.Build<SendTests>();
        var sender = provider.GetRequiredService<ISender>();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sender.Send<Pong>(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => sender.Send((object)null!));
    }

    [Fact]
    public async Task Send_passes_cancellation_token_to_handler()
    {
        await using var provider = TestHost.Build<SendTests>();
        using var cts = new CancellationTokenSource();

        var token = await provider.GetRequiredService<ISender>().Send(new TokenProbe(), cts.Token);

        Assert.Equal(cts.Token, token);
    }

    [Fact]
    public async Task Handlers_receive_their_own_dependencies_from_the_container()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<SendTests>(services: s => s.AddSingleton(journal));

        var result = await provider.GetRequiredService<ISender>().Send(new NeedsDependency());

        Assert.Equal("ok", result);
        Assert.Equal(["resolved"], journal.Entries);
    }

    [Fact]
    public async Task Explicitly_registered_handler_instance_wins_over_scanning()
    {
        var mock = new Mock<IRequestHandler<Ping, Pong>>();
        mock.Setup(h => h.Handle(It.IsAny<Ping>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pong("mocked"));

        await using var provider = TestHost.Build<SendTests>(services: s => s.AddSingleton(mock.Object));

        var response = await provider.GetRequiredService<ISender>().Send(new Ping("x"));

        Assert.Equal("mocked", response.Message);
        mock.Verify(h => h.Handle(It.Is<Ping>(p => p.Message == "x"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Mediator_can_be_used_without_the_container_extensions()
    {
        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<Ping, Pong>, PingHandler>();
        await using var provider = services.BuildServiceProvider();

        var mediator = new Mediator(provider);

        Assert.Equal("Raw Pong", (await mediator.Send(new Ping("Raw"))).Message);
    }
}
