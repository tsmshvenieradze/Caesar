using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Generics;

public sealed record Ping(Journal Journal) : INotification, IJournaled;

public sealed record Pong(Journal Journal) : INotification, IJournaled;

/// <summary>Open-generic notification handler: closes over every published notification type.</summary>
public sealed class AuditEverything<TNotification> : INotificationHandler<TNotification>
    where TNotification : INotification, IJournaled
{
    public Task Handle(TNotification notification, CancellationToken cancellationToken)
    {
        notification.Journal.Add("audit:" + typeof(TNotification).Name);
        return Task.CompletedTask;
    }
}

public sealed record Command(Journal Journal) : IRequest<string>, IJournaled;

public sealed class CommandHandler : IRequestHandler<Command, string>
{
    public Task<string> Handle(Command request, CancellationToken cancellationToken) => Task.FromResult("ok");
}

/// <summary>Open-generic pre-processor discovered by scanning.</summary>
public sealed class StampAll<TRequest> : IRequestPreProcessor<TRequest>
    where TRequest : IJournaled
{
    public Task Process(TRequest request, CancellationToken cancellationToken)
    {
        request.Journal.Add("stamp");
        return Task.CompletedTask;
    }
}

/// <summary>Open-generic exception action discovered by scanning.</summary>
public sealed class ReportAll<TRequest, TException> : IRequestExceptionAction<TRequest, TException>
    where TRequest : IJournaled
    where TException : Exception
{
    public Task Execute(TRequest request, TException exception, CancellationToken cancellationToken)
    {
        request.Journal.Add("report:" + typeof(TException).Name);
        return Task.CompletedTask;
    }
}

public class GenericHandlerTests
{
    [Fact]
    public async Task Open_generic_notification_handler_is_registered_for_every_notification()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<GenericHandlerTests>();
        var publisher = provider.GetRequiredService<IPublisher>();

        await publisher.Publish(new Ping(journal));
        await publisher.Publish(new Pong(journal));

        Assert.Equal(["audit:Ping", "audit:Pong"], journal.Entries);
    }

    [Fact]
    public async Task Open_generic_pre_processor_is_registered_by_scanning()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<GenericHandlerTests>();

        await provider.GetRequiredService<ISender>().Send(new Command(journal));

        Assert.Equal(["stamp"], journal.Entries);
    }

    [Fact]
    public void Open_generic_exception_action_is_registered_by_scanning()
    {
        using var provider = TestHost.Build<GenericHandlerTests>();

        var actions = provider.GetServices<IRequestExceptionAction<Command, InvalidOperationException>>();

        Assert.Single(actions);
    }
}
