using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.CommandHandlerProbe;

public sealed record UnitOnly(Journal Journal) : IRequest;

public sealed class UnitOnlyHandler : IRequestHandler<UnitOnly, Unit>
{
    public Task<Unit> Handle(UnitOnly request, CancellationToken cancellationToken)
    {
        request.Journal.Add("unit");
        return Unit.Task;
    }
}

public sealed record BothVariants(Journal Journal) : IRequest;

public sealed class BothVariantsVoidHandler : IRequestHandler<BothVariants>
{
    public Task Handle(BothVariants request, CancellationToken cancellationToken)
    {
        request.Journal.Add("void");
        return Task.CompletedTask;
    }
}

public sealed class BothVariantsUnitHandler : IRequestHandler<BothVariants, Unit>
{
    public Task<Unit> Handle(BothVariants request, CancellationToken cancellationToken)
    {
        request.Journal.Add("unit");
        return Unit.Task;
    }
}

/// <summary>Records every service the mediator asks for, then defers to the real container.</summary>
public sealed class RecordingProvider(IServiceProvider inner) : IServiceProvider
{
    private readonly List<Type> _requested = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<Type> Requested
    {
        get
        {
            lock (_lock)
            {
                return [.. _requested];
            }
        }
    }

    public object? GetService(Type serviceType)
    {
        lock (_lock)
        {
            _requested.Add(serviceType);
        }

        return inner.GetService(serviceType);
    }
}

public class CommandHandlerProbeTests
{
    [Fact]
    public async Task Command_with_only_a_Unit_handler_does_not_look_up_the_void_handler()
    {
        await using var provider = TestHost.Build<CommandHandlerProbeTests>();
        var recording = new RecordingProvider(provider);
        var journal = new Journal();

        await new Mediator(recording).Send(new UnitOnly(journal));
        await new Mediator(recording).Send(new UnitOnly(journal));

        Assert.Equal(["unit", "unit"], journal.Entries);
        Assert.DoesNotContain(typeof(IRequestHandler<UnitOnly>), recording.Requested);
    }

    [Fact]
    public async Task Void_handler_wins_when_both_command_handler_variants_are_registered()
    {
        await using var provider = TestHost.Build<CommandHandlerProbeTests>();
        var sender = provider.GetRequiredService<ISender>();
        var journal = new Journal();

        await sender.Send(new BothVariants(journal));
        await sender.Send((object)new BothVariants(journal));
        await sender.Send((IRequest<Unit>)new BothVariants(journal));

        Assert.Equal(["void", "void", "void"], journal.Entries);
    }
}
