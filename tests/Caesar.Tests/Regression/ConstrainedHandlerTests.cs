using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.ConstrainedHandlers;

public interface IAudited;

public interface IQueryA;

public sealed record AuditedCommand(Journal Journal) : IRequest, IAudited, IJournaled;

public sealed record PlainCommand(Journal Journal) : IRequest;

public sealed class PlainCommandHandler : IRequestHandler<PlainCommand, Unit>
{
    public Task<Unit> Handle(PlainCommand request, CancellationToken cancellationToken)
    {
        request.Journal.Add("plain");
        return Unit.Task;
    }
}

public sealed record QueryA : IRequest<string>, IQueryA;

public sealed record Orphan : IRequest<string>;

public sealed record StreamA : IStreamRequest<int>, IQueryA;

public sealed record OrphanStream : IStreamRequest<int>;

public class ConstrainedHandlerTests
{
    // The constrained open generics are private and registered by hand: scanning skips non-public nested types, so
    // no test that scans the whole assembly picks them up, and these tests pin what dispatch does with such a
    // registration whatever AddCaesar's scanner would decide to do with them.

    private sealed class AuditedHandler<TRequest> : IRequestHandler<TRequest>
        where TRequest : IRequest, IAudited, IJournaled
    {
        public Task Handle(TRequest request, CancellationToken cancellationToken)
        {
            request.Journal.Add("audited");
            return Task.CompletedTask;
        }
    }

    private sealed class QueryAHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
        where TRequest : IRequest<TResponse>, IQueryA
    {
        public Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken) => Task.FromResult(default(TResponse)!);
    }

    private sealed class StreamAHandler<TRequest, TResponse> : IStreamRequestHandler<TRequest, TResponse>
        where TRequest : IStreamRequest<TResponse>, IQueryA
    {
        public async IAsyncEnumerable<TResponse> Handle(TRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return default!;
        }
    }

    private static TestProvider Build()
        => TestHost.Build<ConstrainedHandlerTests>(services: static s =>
        {
            s.AddTransient(typeof(IRequestHandler<>), typeof(AuditedHandler<>));
            s.AddTransient(typeof(IRequestHandler<,>), typeof(QueryAHandler<,>));
            s.AddTransient(typeof(IStreamRequestHandler<,>), typeof(StreamAHandler<,>));
        });

    [Fact]
    public async Task Constrained_open_command_handler_does_not_hide_a_Unit_handler_for_another_command()
    {
        await using var provider = Build();
        var sender = provider.GetRequiredService<ISender>();
        var journal = new Journal();

        await sender.Send(new PlainCommand(journal));
        await sender.Send(new AuditedCommand(journal));

        Assert.Equal(["plain", "audited"], journal.Entries);
    }

    [Fact]
    public async Task Request_that_a_constrained_open_handler_cannot_close_reports_no_handler()
    {
        await using var provider = Build();
        var sender = provider.GetRequiredService<ISender>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(new Orphan()));

        Assert.Contains("No handler was found", exception.Message, StringComparison.Ordinal);
        Assert.Null(await sender.Send(new QueryA()));
    }

    [Fact]
    public async Task Stream_that_a_constrained_open_handler_cannot_close_reports_no_handler()
    {
        await using var provider = Build();
        var sender = provider.GetRequiredService<ISender>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.CreateStream(new OrphanStream()).ToListAsync().AsTask());

        Assert.Contains("No handler was found", exception.Message, StringComparison.Ordinal);
        Assert.Equal([0], await sender.CreateStream(new StreamA()).ToListAsync());
    }
}
