using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Streams;

public sealed record Count(int To, Journal Journal) : IStreamRequest<int>, IJournaled;

public sealed class CountHandler : IStreamRequestHandler<Count, int>
{
    public async IAsyncEnumerable<int> Handle(Count request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 1; i <= request.To; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.Journal.Add("yield:" + i);
            yield return i;
            await Task.Yield();
        }
    }
}

public sealed record Orphan : IStreamRequest<int>;

public sealed class DoublingBehavior : IStreamPipelineBehavior<Count, int>
{
    public async IAsyncEnumerable<int> Handle(Count request, StreamHandlerDelegate<int> next, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        request.Journal.Add("doubling-start");
        await foreach (var item in next().WithCancellation(cancellationToken))
        {
            yield return item * 2;
        }

        request.Journal.Add("doubling-end");
    }
}

public sealed class LoggingStreamBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : IJournaled
{
    public async IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        request.Journal.Add("log-start");
        await foreach (var item in next().WithCancellation(cancellationToken))
        {
            request.Journal.Add("log:" + item);
            yield return item;
        }

        request.Journal.Add("log-end");
    }
}

public class StreamTests
{
    [Fact]
    public async Task CreateStream_yields_handler_items()
    {
        await using var provider = TestHost.Build<StreamTests>();

        var items = await provider.GetRequiredService<ISender>().CreateStream(new Count(3, new Journal())).ToListAsync();

        Assert.Equal([1, 2, 3], items);
    }

    [Fact]
    public async Task Stream_behaviors_wrap_in_registration_order()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<StreamTests>(cfg =>
        {
            cfg.AddOpenStreamBehavior(typeof(LoggingStreamBehavior<,>));
            cfg.AddStreamBehavior<DoublingBehavior>();
        });

        var items = await provider.GetRequiredService<ISender>().CreateStream(new Count(2, journal)).ToListAsync();

        Assert.Equal([2, 4], items);
        Assert.Equal(
            ["log-start", "doubling-start", "yield:1", "log:2", "yield:2", "log:4", "doubling-end", "log-end"],
            journal.Entries);
    }

    [Fact]
    public async Task CreateStream_object_dispatches_by_runtime_type()
    {
        await using var provider = TestHost.Build<StreamTests>();
        object request = new Count(2, new Journal());

        var items = await provider.GetRequiredService<ISender>().CreateStream(request).ToListAsync();

        Assert.Equal([1, 2], items.Cast<int>());
    }

    [Fact]
    public async Task CreateStream_object_that_is_not_a_stream_request_throws_on_enumeration()
    {
        await using var provider = TestHost.Build<StreamTests>();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in provider.GetRequiredService<ISender>().CreateStream(new object()))
            {
            }
        });
    }

    [Fact]
    public async Task CreateStream_without_handler_throws_on_enumeration()
    {
        await using var provider = TestHost.Build<StreamTests>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<ISender>().CreateStream(new Orphan()).ToListAsync().AsTask());
    }

    [Fact]
    public async Task CreateStream_honours_cancellation()
    {
        await using var provider = TestHost.Build<StreamTests>();
        using var cts = new CancellationTokenSource();
        var received = new List<int>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in provider.GetRequiredService<ISender>().CreateStream(new Count(100, new Journal()), cts.Token))
            {
                received.Add(item);
                if (item == 3)
                {
                    await cts.CancelAsync();
                }
            }
        });

        Assert.Equal([1, 2, 3], received);
    }
}

internal static class AsyncEnumerableExtensions
{
    public static async ValueTask<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }
}
