using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.FastPath;

public sealed record Ping : IRequest<int>;

public sealed class PingHandler : IRequestHandler<Ping, int>
{
    private static readonly Task<int> Response = Task.FromResult(7);

    public Task<int> Handle(Ping request, CancellationToken cancellationToken) => Response;
}

public class FastPathTests
{
    private const int Iterations = 20_000;

    /// <summary>Sends <paramref name="request"/> repeatedly and returns the bytes this thread allocated per call.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double BytesPerSend(ISender sender, Ping request)
    {
        // Warm up first, so tiered compilation and the container's first-use work are not measured.
        for (var i = 0; i < Iterations; i++)
        {
            Consume(sender.Send(request));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
        {
            Consume(sender.Send(request));
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / (double)Iterations;
    }

    private static void Consume(Task<int> task)
    {
        if (!task.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException($"Expected a completed send, got {task.Status}.");
        }
    }

    [Fact]
    public async Task Send_with_no_behaviors_allocates_nothing_on_the_mediator_side()
    {
        // Singleton handler and mediator, and a cached response task: whatever is allocated comes from dispatch itself.
        await using var provider = TestHost.Build<FastPathTests>(static cfg =>
        {
            cfg.Lifetime = ServiceLifetime.Singleton;
            cfg.MediatorLifetime = ServiceLifetime.Singleton;
        });

        var perSend = BytesPerSend(provider.Root.GetRequiredService<ISender>(), new Ping());

        Assert.True(perSend < 1, $"Expected no allocation per Send, measured {perSend:F1} bytes.");
    }
}
