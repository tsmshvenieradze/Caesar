// Loaded a second time into a collectible AssemblyLoadContext by CollectibleContextTests and
// CollectibleExceptionTests, which check that dispatching these messages does not keep the context alive after it is
// unloaded. Nothing else scans this namespace.

using Caesar.Pipeline;

namespace Caesar.Tests.Fixtures.Collectible;

public sealed record Echo(string Text) : IRequest<string>;

public sealed class EchoHandler : IRequestHandler<Echo, string>
{
    public Task<string> Handle(Echo request, CancellationToken cancellationToken) => Task.FromResult(request.Text);
}

public sealed record Touch : IRequest;

public sealed class TouchHandler : IRequestHandler<Touch>
{
    public Task Handle(Touch request, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record Announced : INotification;

public sealed class AnnouncedHandler : INotificationHandler<Announced>
{
    public Task Handle(Announced notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>An exception type that exists only in the plugin.</summary>
public sealed class PluginException : Exception
{
    public PluginException()
    {
    }

    public PluginException(string message)
        : base(message)
    {
    }

    public PluginException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Fails with <see cref="PluginException"/>; <see cref="Recover"/> says whether <see cref="FailRecovery"/> handles it.</summary>
public sealed record Fail(bool Recover, ICollection<string> Log) : IRequest<string>;

public sealed class FailHandler : IRequestHandler<Fail, string>
{
    public Task<string> Handle(Fail request, CancellationToken cancellationToken) => Task.FromException<string>(new PluginException("plugin failed"));
}

public sealed class FailRecovery : IRequestExceptionHandler<Fail, string, PluginException>
{
    public Task Handle(Fail request, PluginException exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
    {
        request.Log.Add("handler");
        if (request.Recover)
        {
            state.SetHandled("recovered");
        }

        return Task.CompletedTask;
    }
}

public sealed class FailObserver : IRequestExceptionAction<Fail, PluginException>
{
    public Task Execute(Fail request, PluginException exception, CancellationToken cancellationToken)
    {
        request.Log.Add("action");
        return Task.CompletedTask;
    }
}

public sealed record Numbers(int Count) : IStreamRequest<int>;

public sealed class NumbersHandler : IStreamRequestHandler<Numbers, int>
{
    public IAsyncEnumerable<int> Handle(Numbers request, CancellationToken cancellationToken) => new Sequence(request.Count);

    /// <summary>A synchronous sequence, so the test can drain it without blocking on real asynchrony.</summary>
    private sealed class Sequence(int count) : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        public int Current { get; private set; }

        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

        public ValueTask<bool> MoveNextAsync()
        {
            if (Current >= count)
            {
                return ValueTask.FromResult(false);
            }

            Current++;
            return ValueTask.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
