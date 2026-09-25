using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.SynchronousFailures;

public sealed record Find(int Id) : IRequest<int>;

/// <summary>Not async: an invalid id throws straight out of Handle instead of returning a faulted task.</summary>
public sealed class FindHandler : IRequestHandler<Find, int>
{
    public Task<int> Handle(Find request, CancellationToken cancellationToken)
        => request.Id < 0 ? throw new KeyNotFoundException("no such id") : Task.FromResult(request.Id);
}

public sealed record Unhandled : IRequest<int>;

public sealed record UnhandledCommand : IRequest;

public sealed record Delete : IRequest;

public sealed class DeleteHandler : IRequestHandler<Delete>
{
    public Task Handle(Delete request, CancellationToken cancellationToken) => throw new KeyNotFoundException("nothing to delete");
}

public sealed record Abandon : IRequest<int>;

public sealed class AbandonHandler : IRequestHandler<Abandon, int>
{
    public static readonly OperationCanceledException Thrown = new("abandoned by the handler");

    public Task<int> Handle(Abandon request, CancellationToken cancellationToken) => throw Thrown;
}

public sealed record Guarded : IRequest<int>;

public sealed class GuardedHandler : IRequestHandler<Guarded, int>
{
    public Task<int> Handle(Guarded request, CancellationToken cancellationToken) => Task.FromResult(1);
}

public sealed class DenyBehavior : IPipelineBehavior<Guarded, int>
{
    public Task<int> Handle(Guarded request, RequestHandlerDelegate<int> next, CancellationToken cancellationToken)
        => throw new UnauthorizedAccessException("denied");
}

public sealed record Broken : IRequest<int>;

public sealed class BrokenHandler : IRequestHandler<Broken, int>
{
    public BrokenHandler() => throw new InvalidOperationException("ctor failed");

    public Task<int> Handle(Broken request, CancellationToken cancellationToken) => Task.FromResult(0);
}

public class SynchronousFailureTests
{
    /// <summary>
    /// Runs <paramref name="call"/> and returns what it threw synchronously, without awaiting anything it returned.
    /// xUnit's Record.Exception would await a returned task, which is exactly what these tests must not do.
    /// </summary>
    private static Exception? ThrownSynchronously(Action call)
    {
        try
        {
            call();
            return null;
        }
#pragma warning disable CA1031 // Any exception is the observation under test.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return exception;
        }
    }

    private static async Task<TException> AssertFaultedAsync<TException>(Func<Task> send)
        where TException : Exception
    {
        Task? task = null;
        var thrown = ThrownSynchronously(() => task = send());

        Assert.Null(thrown);
        Assert.NotNull(task);
        Assert.True(task.IsFaulted, $"Expected a faulted task, got {task.Status}.");
        return await Assert.ThrowsAsync<TException>(() => task);
    }

    [Fact]
    public async Task Handler_throwing_synchronously_yields_a_faulted_task_from_every_overload()
    {
        await using var provider = TestHost.Build<SynchronousFailureTests>();
        var sender = provider.GetRequiredService<ISender>();

        await AssertFaultedAsync<KeyNotFoundException>(() => sender.Send(new Find(-1)));
        await AssertFaultedAsync<KeyNotFoundException>(() => sender.Send((object)new Find(-1)));
        await AssertFaultedAsync<KeyNotFoundException>(() => sender.Send(new Delete()));
        await AssertFaultedAsync<KeyNotFoundException>(() => sender.Send((object)new Delete()));
    }

    [Fact]
    public async Task Missing_handler_yields_a_faulted_task_from_every_overload()
    {
        await using var provider = TestHost.Build<SynchronousFailureTests>();
        var sender = provider.GetRequiredService<ISender>();

        await AssertFaultedAsync<InvalidOperationException>(() => sender.Send(new Unhandled()));
        await AssertFaultedAsync<InvalidOperationException>(() => sender.Send((object)new Unhandled()));
        await AssertFaultedAsync<InvalidOperationException>(() => sender.Send(new UnhandledCommand()));
        await AssertFaultedAsync<InvalidOperationException>(() => sender.Send((IRequest<Unit>)new UnhandledCommand()));
    }

    [Fact]
    public async Task Behavior_throwing_synchronously_yields_a_faulted_task()
    {
        await using var provider = TestHost.Build<SynchronousFailureTests>(cfg => cfg.AddBehavior<DenyBehavior>());

        await AssertFaultedAsync<UnauthorizedAccessException>(() => provider.GetRequiredService<ISender>().Send(new Guarded()));
    }

    [Fact]
    public async Task Handler_constructor_failure_yields_a_faulted_task()
    {
        await using var provider = TestHost.Build<SynchronousFailureTests>();

        var exception = await AssertFaultedAsync<InvalidOperationException>(() => provider.GetRequiredService<ISender>().Send(new Broken()));

        Assert.Equal("ctor failed", exception.Message);
    }

    [Fact]
    public async Task Synchronous_cancellation_yields_a_canceled_task_carrying_the_original_exception()
    {
        await using var provider = TestHost.Build<SynchronousFailureTests>();
        var sender = provider.GetRequiredService<ISender>();
        Task<int>? task = null;

        var thrown = ThrownSynchronously(() => task = sender.Send(new Abandon()));

        Assert.Null(thrown);
        Assert.NotNull(task);
        Assert.True(task.IsCanceled, $"Expected a canceled task, got {task.Status}.");
        Assert.Same(AbandonHandler.Thrown, await Assert.ThrowsAsync<OperationCanceledException>(() => task));
    }

    [Fact]
    public async Task Null_request_is_still_rejected_synchronously()
    {
        await using var provider = TestHost.Build<SynchronousFailureTests>();
        var sender = provider.GetRequiredService<ISender>();

        Assert.IsType<ArgumentNullException>(ThrownSynchronously(() => sender.Send<int>(null!)));
        Assert.IsType<ArgumentNullException>(ThrownSynchronously(() => sender.Send((object)null!)));
        Assert.IsType<ArgumentNullException>(ThrownSynchronously(() => sender.Send((Delete)null!)));
    }
}
