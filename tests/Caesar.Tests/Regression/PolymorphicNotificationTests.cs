using Caesar.NotificationPublishers;
using Microsoft.Extensions.DependencyInjection;

// INotificationHandler<in TNotification> is contravariant, so, as in MediatR, a handler registered for a base class of
// the published notification, for an interface it implements, or for INotification itself runs too. Handlers for the
// runtime type come first, then base classes (most derived first), then interfaces.

namespace Caesar.Tests.Regression.PolymorphicNotifications.Hierarchy
{
    /// <summary>An event interface between the concrete events and <see cref="INotification"/>.</summary>
    public interface IDomainEvent : INotification, IJournaled;

    public abstract record DomainEvent(Journal Journal) : IDomainEvent;

    /// <summary>Also implements <see cref="IJournaled"/>, which is not a notification type and so has no handlers.</summary>
    public sealed record OrderPlaced(Journal Journal) : DomainEvent(Journal);

    /// <summary>Has no handler of its own; only the catch-all sees it.</summary>
    public sealed record Heartbeat(Journal Journal) : INotification, IJournaled;

    /// <summary>A value-type notification, which only the catch-all handles.</summary>
    public readonly record struct Tick(Journal Journal) : INotification, IJournaled;

    public sealed class OrderPlacedHandler : INotificationHandler<OrderPlaced>
    {
        public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
        {
            notification.Journal.Add("exact");
            return Task.CompletedTask;
        }
    }

    public sealed class DomainEventHandler : INotificationHandler<DomainEvent>
    {
        public Task Handle(DomainEvent notification, CancellationToken cancellationToken)
        {
            notification.Journal.Add("base-class");
            return Task.CompletedTask;
        }
    }

    public sealed class DomainEventInterfaceHandler : INotificationHandler<IDomainEvent>
    {
        public Task Handle(IDomainEvent notification, CancellationToken cancellationToken)
        {
            notification.Journal.Add("interface");
            return Task.CompletedTask;
        }
    }

    /// <summary>An audit or outbox handler that wants every notification.</summary>
    public sealed class AuditAll : INotificationHandler<INotification>
    {
        public Task Handle(INotification notification, CancellationToken cancellationToken)
        {
            ((IJournaled)notification).Journal.Add($"audit-all:{notification.GetType().Name}");
            return Task.CompletedTask;
        }
    }

    public class HierarchyTests
    {
        public static TheoryData<Type> Publishers =>
        [
            typeof(ForeachAwaitPublisher),
            typeof(TaskWhenAllPublisher),
            typeof(ForeachAwaitContinueOnFailurePublisher),
        ];

        [Theory]
        [MemberData(nameof(Publishers))]
        public async Task Publish_runs_handlers_for_the_runtime_type_then_base_classes_then_interfaces(Type publisher)
        {
            var journal = new Journal();
            await using var provider = TestHost.Build<HierarchyTests>(cfg => cfg.NotificationPublisherType = publisher);

            await provider.GetRequiredService<IPublisher>().Publish(new OrderPlaced(journal));

            Assert.Equal(["exact", "base-class", "interface", "audit-all:OrderPlaced"], journal.Entries);
        }

        [Theory]
        [MemberData(nameof(Publishers))]
        public async Task Publish_object_runs_the_same_handlers(Type publisher)
        {
            var journal = new Journal();
            await using var provider = TestHost.Build<HierarchyTests>(cfg => cfg.NotificationPublisherType = publisher);
            object notification = new OrderPlaced(journal);

            await provider.GetRequiredService<IPublisher>().Publish(notification);

            Assert.Equal(["exact", "base-class", "interface", "audit-all:OrderPlaced"], journal.Entries);
        }

        [Fact]
        public async Task A_catch_all_handler_runs_for_a_notification_without_handlers_of_its_own()
        {
            var journal = new Journal();
            await using var provider = TestHost.Build<HierarchyTests>();
            var publisher = provider.GetRequiredService<IPublisher>();

            await publisher.Publish(new Heartbeat(journal));
            await publisher.Publish(new Tick(journal));
            await publisher.Publish((object)new Tick(journal));

            Assert.Equal(["audit-all:Heartbeat", "audit-all:Tick", "audit-all:Tick"], journal.Entries);
        }

        [Fact]
        public async Task A_custom_publisher_receives_the_executors_in_order()
        {
            var publisher = new CapturingPublisher();
            await using var provider = TestHost.Build<HierarchyTests>(cfg => cfg.NotificationPublisher = publisher);

            await provider.GetRequiredService<IPublisher>().Publish(new OrderPlaced(new Journal()));

            Assert.NotNull(publisher.Captured);
            Assert.Collection(
                publisher.Captured,
                e => Assert.IsType<OrderPlacedHandler>(e.HandlerInstance),
                e => Assert.IsType<DomainEventHandler>(e.HandlerInstance),
                e => Assert.IsType<DomainEventInterfaceHandler>(e.HandlerInstance),
                e => Assert.IsType<AuditAll>(e.HandlerInstance));
        }

        [Fact]
        public async Task Mediator_used_without_AddCaesar_runs_only_the_runtime_types_handlers()
        {
            var journal = new Journal();
            var services = new ServiceCollection();
            services.AddTransient<INotificationHandler<OrderPlaced>, OrderPlacedHandler>();
            services.AddTransient<INotificationHandler<INotification>, AuditAll>();
            await using var provider = services.BuildServiceProvider();

            await new Mediator(provider).Publish(new OrderPlaced(journal));

            Assert.Equal(["exact"], journal.Entries);
        }

        private sealed class CapturingPublisher : INotificationPublisher
        {
            public NotificationHandlerExecutor[]? Captured { get; private set; }

            public async Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
            {
                Captured = [.. handlerExecutors];
                foreach (var executor in Captured)
                {
                    await executor.HandlerCallback(notification, cancellationToken);
                }
            }
        }
    }
}

namespace Caesar.Tests.Regression.PolymorphicNotifications.Deduplication
{
    public abstract record DomainEvent(Journal Journal) : INotification;

    public sealed record OrderPlaced(Journal Journal) : DomainEvent(Journal);

    /// <summary>Has no handler of its own, so the projection's most specific level is <see cref="DomainEvent"/>.</summary>
    public sealed record OrderCancelled(Journal Journal) : DomainEvent(Journal);

    /// <summary>One class registered for three levels of the hierarchy.</summary>
    public sealed class OrderProjection : INotificationHandler<OrderPlaced>, INotificationHandler<DomainEvent>, INotificationHandler<INotification>
    {
        public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
        {
            notification.Journal.Add("projection:OrderPlaced");
            return Task.CompletedTask;
        }

        public Task Handle(DomainEvent notification, CancellationToken cancellationToken)
        {
            notification.Journal.Add("projection:DomainEvent");
            return Task.CompletedTask;
        }

        public Task Handle(INotification notification, CancellationToken cancellationToken)
        {
            ((DomainEvent)notification).Journal.Add("projection:INotification");
            return Task.CompletedTask;
        }
    }

    public class DeduplicationTests
    {
        [Fact]
        public async Task A_class_registered_for_several_levels_runs_once_for_the_most_specific()
        {
            var journal = new Journal();
            await using var provider = TestHost.Build<DeduplicationTests>();
            var publisher = provider.GetRequiredService<IPublisher>();

            await publisher.Publish(new OrderPlaced(journal));
            await publisher.Publish(new OrderCancelled(journal));

            Assert.Equal(["projection:OrderPlaced", "projection:DomainEvent"], journal.Entries);
        }
    }
}

namespace Caesar.Tests.Regression.PolymorphicNotifications.OpenGeneric
{
    public abstract record DomainEvent(Journal Journal) : INotification, IJournaled;

    public sealed record OrderPlaced(Journal Journal) : DomainEvent(Journal);

    /// <summary>Closes over every level of the hierarchy, so it would run once per level if base levels asked for it.</summary>
    public sealed class LogEvery<TNotification> : INotificationHandler<TNotification>
        where TNotification : INotification
    {
        public Task Handle(TNotification notification, CancellationToken cancellationToken)
        {
            ((IJournaled)notification).Journal.Add($"log:{typeof(TNotification).Name}");
            return Task.CompletedTask;
        }
    }

    public sealed class DomainEventHandler : INotificationHandler<DomainEvent>
    {
        public Task Handle(DomainEvent notification, CancellationToken cancellationToken)
        {
            notification.Journal.Add("base-class");
            return Task.CompletedTask;
        }
    }

    public sealed class AuditAll : INotificationHandler<INotification>
    {
        public Task Handle(INotification notification, CancellationToken cancellationToken)
        {
            ((IJournaled)notification).Journal.Add("audit-all");
            return Task.CompletedTask;
        }
    }

    public class OpenGenericTests
    {
        [Fact]
        public async Task An_open_generic_handler_runs_once_closed_over_the_runtime_type()
        {
            var journal = new Journal();
            await using var provider = TestHost.Build<OpenGenericTests>();

            await provider.GetRequiredService<IPublisher>().Publish(new OrderPlaced(journal));

            Assert.Equal(["log:OrderPlaced", "base-class", "audit-all"], journal.Entries);
        }
    }
}

namespace Caesar.Tests.Regression.PolymorphicNotifications.Manual
{
    public sealed record Pinged(Journal Journal) : INotification;

    public sealed class PingedHandler : INotificationHandler<Pinged>
    {
        public Task Handle(Pinged notification, CancellationToken cancellationToken)
        {
            notification.Journal.Add("exact");
            return Task.CompletedTask;
        }
    }

    public class ManualRegistrationTests
    {
        [Fact]
        public async Task A_catch_all_handler_registered_by_hand_after_AddCaesar_runs()
        {
            var journal = new Journal();
            var services = new ServiceCollection();
            services.AddCaesar(cfg =>
            {
                cfg.RegisterServicesFromAssemblyContaining<ManualRegistrationTests>();
                // The hand-registered handlers are nested in this class, so scanning is kept away from them.
                cfg.TypeEvaluator = static t => t.Namespace == typeof(ManualRegistrationTests).Namespace && !t.IsNested;
            });
            services.AddSingleton<INotificationHandler<INotification>>(new ByInstance());
            services.AddScoped<INotificationHandler<INotification>>(_ => new ByFactory());
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();

            await scope.ServiceProvider.GetRequiredService<IPublisher>().Publish(new Pinged(journal));

            Assert.Equal(["exact", "by-instance", "by-factory"], journal.Entries);
        }

        private sealed class ByInstance : INotificationHandler<INotification>
        {
            public Task Handle(INotification notification, CancellationToken cancellationToken)
            {
                ((Pinged)notification).Journal.Add("by-instance");
                return Task.CompletedTask;
            }
        }

        private sealed class ByFactory : INotificationHandler<INotification>
        {
            public Task Handle(INotification notification, CancellationToken cancellationToken)
            {
                ((Pinged)notification).Journal.Add("by-factory");
                return Task.CompletedTask;
            }
        }
    }
}
