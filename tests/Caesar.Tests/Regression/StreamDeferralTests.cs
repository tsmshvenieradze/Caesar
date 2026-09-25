using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.StreamDeferral;

public sealed record Unhandled : IStreamRequest<int>;

public sealed record Eager(Journal Journal) : IStreamRequest<int>;

/// <summary>Not an iterator: its body runs when Handle is called, not when the sequence is enumerated.</summary>
public sealed class EagerHandler : IStreamRequestHandler<Eager, int>
{
    public IAsyncEnumerable<int> Handle(Eager request, CancellationToken cancellationToken)
    {
        request.Journal.Add("handler-ran");
        return Items();

        static async IAsyncEnumerable<int> Items()
        {
            await Task.Yield();
            yield return 1;
        }
    }
}

public sealed record Counted : IStreamRequest<int>;

/// <summary>Records every construction, so a test can tell whether each enumeration resolved its own handler.</summary>
public sealed class CountedHandler : IStreamRequestHandler<Counted, int>
{
    public CountedHandler(Journal journal) => journal.Add("constructed");

    public async IAsyncEnumerable<int> Handle(Counted request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return 1;
    }
}

public sealed record Endless : IStreamRequest<int>;

public sealed class EndlessHandler : IStreamRequestHandler<Endless, int>
{
    public async IAsyncEnumerable<int> Handle(Endless request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; ; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }
}

public class StreamDeferralTests
{
    [Fact]
    public async Task Typed_CreateStream_without_a_handler_fails_on_enumeration_not_on_the_call()
    {
        await using var provider = TestHost.Build<StreamDeferralTests>();
        var sender = provider.GetRequiredService<ISender>();

        var typed = Record.Exception(() => sender.CreateStream(new Unhandled()));
        var untyped = Record.Exception(() => sender.CreateStream((object)new Unhandled()));

        Assert.Null(typed);
        Assert.Null(untyped);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.CreateStream(new Unhandled()).ToListAsync().AsTask());
    }

    [Fact]
    public async Task Typed_CreateStream_does_not_run_the_handler_until_enumeration()
    {
        await using var provider = TestHost.Build<StreamDeferralTests>();
        var sender = provider.GetRequiredService<ISender>();
        var journal = new Journal();

        var stream = sender.CreateStream(new Eager(journal));

        Assert.Empty(journal.Entries);
        Assert.Equal([1], await stream.ToListAsync());
        Assert.Equal(["handler-ran"], journal.Entries);
    }

    [Fact]
    public async Task Each_enumeration_of_a_typed_stream_resolves_a_fresh_handler()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<StreamDeferralTests>(services: s => s.AddSingleton(journal));
        var stream = provider.GetRequiredService<ISender>().CreateStream(new Counted());

        await stream.ToListAsync();
        await stream.ToListAsync();

        Assert.Equal(["constructed", "constructed"], journal.Entries);
    }

    [Fact]
    public async Task Typed_stream_observes_both_the_CreateStream_token_and_the_enumerator_token()
    {
        await using var provider = TestHost.Build<StreamDeferralTests>();
        var sender = provider.GetRequiredService<ISender>();

        using (var createToken = new CancellationTokenSource())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var item in sender.CreateStream(new Endless(), createToken.Token).WithCancellation(CancellationToken.None))
                {
                    if (item == 2)
                    {
                        await createToken.CancelAsync();
                    }
                }
            });
        }

        using (var enumeratorToken = new CancellationTokenSource())
        using (var unrelated = new CancellationTokenSource())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var item in sender.CreateStream(new Endless(), unrelated.Token).WithCancellation(enumeratorToken.Token))
                {
                    if (item == 2)
                    {
                        await enumeratorToken.CancelAsync();
                    }
                }
            });
        }
    }
}
