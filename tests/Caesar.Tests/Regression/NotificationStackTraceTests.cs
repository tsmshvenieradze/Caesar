using System.Runtime.CompilerServices;
using Caesar.NotificationPublishers;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.NotificationStackTrace;

public sealed record Faulty : INotification;

public sealed class HealthyFaultyHandler : INotificationHandler<Faulty>
{
    public Task Handle(Faulty notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class BoomFaultyHandler : INotificationHandler<Faulty>
{
    public async Task Handle(Faulty notification, CancellationToken cancellationToken)
    {
        await Task.Yield();
        ThrowFromBoomHandler();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromBoomHandler() => throw new InvalidOperationException("boom");
}

/// <summary>F11: a single handler failure must keep the handler's frames in its stack trace, whatever the publisher.</summary>
public class NotificationStackTraceTests
{
    public static TheoryData<Type> Publishers =>
    [
        typeof(ForeachAwaitPublisher),
        typeof(TaskWhenAllPublisher),
        typeof(ForeachAwaitContinueOnFailurePublisher),
    ];

    [Theory]
    [MemberData(nameof(Publishers))]
    public async Task A_single_failure_keeps_the_handler_stack_trace(Type publisherType)
    {
        await using var provider = TestHost.Build<NotificationStackTraceTests>(cfg => cfg.NotificationPublisherType = publisherType);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<IPublisher>().Publish(new Faulty()));

        Assert.Equal("boom", exception.Message);
        Assert.Contains(nameof(BoomFaultyHandler) + ".ThrowFromBoomHandler", exception.StackTrace, StringComparison.Ordinal);
    }
}
