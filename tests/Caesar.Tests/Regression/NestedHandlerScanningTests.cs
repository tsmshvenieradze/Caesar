using System.Runtime.CompilerServices;
using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.NestedScanning;

/// <summary>Vertical-slice layout: the request and its handler live together in a static class.</summary>
public static class CreateOrder
{
    public sealed record Command(int Quantity) : IRequest<int>;

    internal sealed class Handler : IRequestHandler<Command, int>
    {
        public Task<int> Handle(Command request, CancellationToken cancellationToken) => Task.FromResult(request.Quantity * 2);
    }
}

public static class OrderPlaced
{
    public sealed record Event(Journal Journal) : INotification;

    private sealed class Handler : INotificationHandler<Event>
    {
        public Task Handle(Event notification, CancellationToken cancellationToken)
        {
            notification.Journal.Add("private");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A partial handler one of whose declarations a source generator marked [CompilerGenerated]: the attribute applies to
    /// the whole class, which is still an ordinary, scannable handler.
    /// </summary>
    [CompilerGenerated]
    internal sealed partial class GeneratedPart : INotificationHandler<Event>
    {
        public Task Handle(Event notification, CancellationToken cancellationToken)
        {
            notification.Journal.Add("generated-part");
            return Task.CompletedTask;
        }
    }
}

public static class OrderPaid
{
    public sealed record Event(Journal Journal) : INotification;

    internal sealed class Handler : INotificationHandler<Event>
    {
        public Task Handle(Event notification, CancellationToken cancellationToken)
        {
            notification.Journal.Add("internal");
            return Task.CompletedTask;
        }
    }
}

public sealed record Changed(Journal Journal) : INotification;

/// <summary>A generic test base whose nested types are generic only because it is: none of them can be registered.</summary>
#pragma warning disable CA1812 // Only ever scanned.
public abstract class RepositoryTests<TEntity>
{
    private sealed class RecordingHandler : INotificationHandler<Changed>
    {
        public Task Handle(Changed notification, CancellationToken cancellationToken)
        {
            notification.Journal.Add("private-in-generic");
            return Task.CompletedTask;
        }
    }

    internal sealed class InternalHandler : INotificationHandler<Changed>
    {
        public Task Handle(Changed notification, CancellationToken cancellationToken)
        {
            notification.Journal.Add("internal-in-generic");
            return Task.CompletedTask;
        }
    }
}
#pragma warning restore CA1812

#pragma warning disable CA1052 // A protected nested type needs a non-static declaring class.
public class OrderShipped
#pragma warning restore CA1052
{
    public sealed record Event(Journal Journal) : INotification;

    protected sealed class Handler : INotificationHandler<Event>
    {
        public Task Handle(Event notification, CancellationToken cancellationToken)
        {
            notification.Journal.Add("protected");
            return Task.CompletedTask;
        }
    }
}

public static class AuditedOrder
{
    public sealed record Command(Journal Journal) : IRequest<string>;

    public sealed class Handler : IRequestHandler<Command, string>
    {
        public Task<string> Handle(Command request, CancellationToken cancellationToken)
        {
            request.Journal.Add("handler");
            return Task.FromResult("ok");
        }
    }

    internal sealed class PreProcessor : IRequestPreProcessor<Command>
    {
        public Task Process(Command request, CancellationToken cancellationToken)
        {
            request.Journal.Add("pre");
            return Task.CompletedTask;
        }
    }
}

/// <summary>Scanning used to skip every nested type that was not public; MediatR scans them, so migrated slices broke at runtime.</summary>
public class NestedHandlerScanningTests
{
    [Fact]
    public async Task Nested_internal_request_handler_is_scanned()
    {
        await using var provider = TestHost.Build<NestedHandlerScanningTests>();

        Assert.Equal(6, await provider.GetRequiredService<ISender>().Send(new CreateOrder.Command(3)));
    }

    [Fact]
    public async Task Nested_internal_notification_handler_is_scanned()
    {
        await using var provider = TestHost.Build<NestedHandlerScanningTests>();
        var journal = new Journal();

        await provider.GetRequiredService<IPublisher>().Publish(new OrderPaid.Event(journal));

        Assert.Equal(["internal"], journal.Entries);
    }

    [Fact]
    public async Task Nested_private_and_protected_handlers_are_not_scanned()
    {
        await using var provider = TestHost.Build<NestedHandlerScanningTests>();
        var journal = new Journal();

        await provider.GetRequiredService<IPublisher>().Publish(new OrderPlaced.Event(journal));
        await provider.GetRequiredService<IPublisher>().Publish(new OrderShipped.Event(journal));

        Assert.DoesNotContain("private", journal.Entries);
        Assert.DoesNotContain("protected", journal.Entries);
    }

    [Fact]
    public async Task A_partial_handler_marked_CompilerGenerated_by_a_generator_is_scanned()
    {
        await using var provider = TestHost.Build<NestedHandlerScanningTests>();
        var journal = new Journal();

        await provider.GetRequiredService<IPublisher>().Publish(new OrderPlaced.Event(journal));

        Assert.Equal(["generated-part"], journal.Entries);
    }

    [Fact]
    public async Task Non_public_types_nested_in_a_generic_type_are_skipped_without_failing_AddCaesar()
    {
        await using var provider = TestHost.Build<NestedHandlerScanningTests>();
        var journal = new Journal();

        await provider.GetRequiredService<IPublisher>().Publish(new Changed(journal));

        Assert.Empty(journal.Entries);
    }

    [Fact]
    public async Task Nested_internal_pre_processor_is_scanned()
    {
        await using var provider = TestHost.Build<NestedHandlerScanningTests>();
        var journal = new Journal();

        await provider.GetRequiredService<ISender>().Send(new AuditedOrder.Command(journal));

        Assert.Equal(["pre", "handler"], journal.Entries);
    }

    [Fact]
    public async Task TypeEvaluator_can_still_exclude_nested_non_public_types()
    {
        var ns = typeof(NestedHandlerScanningTests).Namespace;
        await using var provider = TestHost.Build<NestedHandlerScanningTests>(cfg => cfg.TypeEvaluator = t =>
            t.Namespace == ns
            && t != typeof(CreateOrder.Handler)
            && t.DeclaringType != typeof(OrderPlaced)
            && t != typeof(AuditedOrder.PreProcessor));
        var journal = new Journal();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<ISender>().Send(new CreateOrder.Command(1)));
        await provider.GetRequiredService<IPublisher>().Publish(new OrderPlaced.Event(journal));
        await provider.GetRequiredService<ISender>().Send(new AuditedOrder.Command(journal));

        Assert.Contains("No handler was found", exception.Message, StringComparison.Ordinal);
        Assert.Equal(["handler"], journal.Entries);
    }
}
