using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.PublishCoreCoverage;

public sealed record Unheard : INotification;

public sealed record Heard : INotification;

public sealed class HeardHandler : INotificationHandler<Heard>
{
    public Task Handle(Heard notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class CountingMediator(IServiceProvider serviceProvider, INotificationPublisher publisher, Journal journal)
    : Mediator(serviceProvider, publisher)
{
    protected override Task PublishCore(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        journal.Add($"{notification.GetType().Name}:{handlerExecutors.Count()}");
        return base.PublishCore(handlerExecutors, notification, cancellationToken);
    }
}

/// <summary>An overridden PublishCore sees every publish, including one with no handlers (guards F22's early return).</summary>
public class PublishCoreCoverageTests
{
    [Fact]
    public async Task PublishCore_runs_for_notifications_with_and_without_handlers()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<PublishCoreCoverageTests>(
            cfg => cfg.MediatorImplementationType = typeof(CountingMediator),
            services => services.AddSingleton(journal));
        var publisher = provider.GetRequiredService<IPublisher>();

        await publisher.Publish(new Unheard());
        await publisher.Publish(new Heard());
        await publisher.Publish(new Unheard());

        Assert.Equal(["Unheard:0", "Heard:1", "Unheard:0"], journal.Entries);
    }
}
