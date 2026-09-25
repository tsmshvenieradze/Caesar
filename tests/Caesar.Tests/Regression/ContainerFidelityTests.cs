using Autofac;
using Autofac.Extensions.DependencyInjection;
using Caesar.DependencyInjection;
using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// The built-in stages and handler probes must follow what the container that dispatches can resolve, not only the
// IServiceCollection AddCaesar was called on: a copied collection, a collection changed after the container was built,
// and registrations made natively in a third-party container all have to behave as the container does.

namespace Caesar.Tests.Regression.ContainerFidelity
{
    internal static class Build
    {
        /// <summary>A collection that ran AddCaesar over the marker's namespace only.</summary>
        public static ServiceCollection Collection<TMarker>()
        {
            var ns = typeof(TMarker).Namespace!;
            var services = new ServiceCollection();
            services.AddCaesar(cfg =>
            {
                cfg.RegisterServicesFromAssemblyContaining<TMarker>();
                cfg.TypeEvaluator = t => t.Namespace == ns;
            });
            return services;
        }

        public static ServiceCollection CopyOf(IServiceCollection services)
        {
            var copy = new ServiceCollection();
            foreach (var descriptor in services)
            {
                copy.Add(descriptor);
            }

            return copy;
        }
    }
}

namespace Caesar.Tests.Regression.ContainerFidelity.Copied
{
    public sealed record Ping(Journal Journal) : IRequest<string>;

    public sealed class PingHandler : IRequestHandler<Ping, string>
    {
        public Task<string> Handle(Ping request, CancellationToken cancellationToken)
        {
            request.Journal.Add("handler");
            return Task.FromResult("ok");
        }
    }

    public sealed record Cmd(Journal Journal) : IRequest;

    public sealed class UnitCmdHandler : IRequestHandler<Cmd, Unit>
    {
        public Task<Unit> Handle(Cmd request, CancellationToken cancellationToken)
        {
            request.Journal.Add("unit");
            return Task.FromResult(Unit.Value);
        }
    }

    public class CopiedCollectionTests
    {
        [Fact]
        public async Task A_processor_added_only_to_a_copied_collection_runs()
        {
            var journal = new Journal();
            var copy = Build.CopyOf(Build.Collection<CopiedCollectionTests>());
            copy.AddTransient<IRequestPreProcessor<Ping>, Manual.PingPre>();

            await using var provider = copy.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Ping(journal));

            Assert.Equal(["pre", "handler"], journal.Entries);
        }

        [Fact]
        public async Task A_void_command_handler_added_only_to_a_copied_collection_wins_over_the_unit_handler()
        {
            var journal = new Journal();
            var copy = Build.CopyOf(Build.Collection<CopiedCollectionTests>());
            copy.AddTransient<IRequestHandler<Cmd>, Manual.VoidCmdHandler>();

            await using var provider = copy.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Cmd(journal));

            Assert.Equal(["void"], journal.Entries);
        }

        [Fact]
        public async Task A_provider_keeps_its_processors_when_the_collection_changes_after_it_was_built()
        {
            var journal = new Journal();
            var services = Build.Collection<CopiedCollectionTests>();
            services.AddTransient<IRequestPreProcessor<Ping>, Manual.PingPre>();

            await using var before = services.BuildServiceProvider();
            services.RemoveAll<IRequestPreProcessor<Ping>>();
            await using var after = services.BuildServiceProvider();

            await using (var scope = before.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Ping(journal));
            }

            Assert.Equal(["pre", "handler"], journal.Entries);

            var afterJournal = new Journal();
            await using (var scope = after.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Ping(afterJournal));
            }

            Assert.Equal(["handler"], afterJournal.Entries);
        }
    }
}

namespace Caesar.Tests.Regression.ContainerFidelity.Copied.Manual
{
    public sealed class PingPre : IRequestPreProcessor<Ping>
    {
        public Task Process(Ping request, CancellationToken cancellationToken)
        {
            request.Journal.Add("pre");
            return Task.CompletedTask;
        }
    }

    public sealed class VoidCmdHandler : IRequestHandler<Cmd>
    {
        public Task Handle(Cmd request, CancellationToken cancellationToken)
        {
            request.Journal.Add("void");
            return Task.CompletedTask;
        }
    }
}

namespace Caesar.Tests.Regression.ContainerFidelity.Native
{
    public sealed record Ping(Journal Journal) : IRequest<string>;

    public sealed class PingHandler : IRequestHandler<Ping, string>
    {
        public Task<string> Handle(Ping request, CancellationToken cancellationToken)
        {
            request.Journal.Add("handler");
            return Task.FromResult("ok");
        }
    }

    public sealed record Boom(Journal Journal) : IRequest<string>;

    public sealed class BoomHandler : IRequestHandler<Boom, string>
    {
        public Task<string> Handle(Boom request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }

    public sealed record Other : IRequest<string>;

    public sealed class OtherHandler : IRequestHandler<Other, string>
    {
        public Task<string> Handle(Other request, CancellationToken cancellationToken) => Task.FromResult("other");
    }

    /// <summary>Puts exception handling in the collection for another request, as a real application would have.</summary>
    public sealed class RecoverOther : IRequestExceptionHandler<Other, string, Exception>
    {
        public Task Handle(Other request, Exception exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
        {
            state.SetHandled("other-recovered");
            return Task.CompletedTask;
        }
    }

    public class AutofacTests
    {
        private static AutofacServiceProvider BuildAutofac(Action<ContainerBuilder> native)
        {
            var builder = new ContainerBuilder();
            builder.Populate(Build.Collection<AutofacTests>());
            native(builder);
            return new AutofacServiceProvider(builder.Build());
        }

        [Fact]
        public async Task A_pre_processor_registered_natively_in_Autofac_runs()
        {
            var journal = new Journal();
            await using var provider = BuildAutofac(b => b.RegisterType<Registered.NativePre>().As<IRequestPreProcessor<Ping>>());
            await using var scope = provider.CreateAsyncScope();

            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Ping(journal));

            Assert.Equal(["pre", "handler"], journal.Entries);
        }

        [Fact]
        public async Task An_exception_handler_registered_natively_in_Autofac_recovers()
        {
            await using var provider = BuildAutofac(b => b.RegisterType<Registered.RecoverBoom>().As<IRequestExceptionHandler<Boom, string, InvalidOperationException>>());
            await using var scope = provider.CreateAsyncScope();

            var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Boom(new Journal()));

            Assert.Equal("recovered", result);
        }
    }
}

namespace Caesar.Tests.Regression.ContainerFidelity.Native.Registered
{
    public sealed class NativePre : IRequestPreProcessor<Ping>
    {
        public Task Process(Ping request, CancellationToken cancellationToken)
        {
            request.Journal.Add("pre");
            return Task.CompletedTask;
        }
    }

    public sealed class RecoverBoom : IRequestExceptionHandler<Boom, string, InvalidOperationException>
    {
        public Task Handle(Boom request, InvalidOperationException exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
        {
            state.SetHandled("recovered");
            return Task.CompletedTask;
        }
    }
}

namespace Caesar.Tests.Regression.ContainerFidelity.ByHand
{
    public sealed class ValidationException(string message) : Exception(message);

    public interface IChecked
    {
        Journal Journal { get; }

        bool Reject { get; }
    }

    public sealed record Ping(Journal Journal, bool Reject = false) : IRequest<string>, IChecked;

    public sealed class PingHandler : IRequestHandler<Ping, string>
    {
        public Task<string> Handle(Ping request, CancellationToken cancellationToken)
        {
            request.Journal.Add("handler");
            return Task.FromResult("ok");
        }
    }

    public sealed class PingPre : IRequestPreProcessor<Ping>
    {
        public Task Process(Ping request, CancellationToken cancellationToken)
        {
            request.Journal.Add("pre");
            return Task.CompletedTask;
        }
    }

    public sealed class Fallback : IRequestExceptionHandler<Ping, string, Exception>
    {
        public Task Handle(Ping request, Exception exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
        {
            request.Journal.Add("fallback");
            state.SetHandled("fallback");
            return Task.CompletedTask;
        }
    }

    public class BehaviorsRegisteredBeforeAddCaesarTests
    {
        private static TestProvider Build(Action<CaesarServiceConfiguration>? configure = null)
            => TestHost.Build<BehaviorsRegisteredBeforeAddCaesarTests>(
                configure,
                services => services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ByHandBehavior.Outer<,>)));

        [Fact]
        public async Task A_behavior_registered_by_hand_before_AddCaesar_stays_outside_the_built_in_stages()
        {
            var journal = new Journal();
            await using var provider = Build(cfg => cfg.AddOpenBehavior(typeof(ByHandBehavior.Inner<,>)));

            await provider.GetRequiredService<ISender>().Send(new Ping(journal));

            Assert.Equal(["outer", "pre", "inner", "handler"], journal.Entries);
        }

        [Fact]
        public async Task Open_and_closed_behaviors_registered_by_hand_before_AddCaesar_keep_their_order_outside_the_built_in_stages()
        {
            var journal = new Journal();
            await using var provider = TestHost.Build<BehaviorsRegisteredBeforeAddCaesarTests>(
                cfg => cfg.AddOpenBehavior(typeof(ByHandBehavior.Inner<,>)),
                services =>
                {
                    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ByHandBehavior.Outer<,>));
                    services.AddTransient<IPipelineBehavior<Ping, string>, ByHandBehavior.ClosedOuter>();
                });

            await provider.GetRequiredService<ISender>().Send(new Ping(journal));

            Assert.Equal(["outer", "closed-outer", "pre", "inner", "handler"], journal.Entries);
        }

        [Fact]
        public async Task Exception_handlers_do_not_see_failures_of_a_behavior_registered_by_hand_before_AddCaesar()
        {
            var journal = new Journal();
            await using var provider = Build();

            await Assert.ThrowsAsync<ValidationException>(() => provider.GetRequiredService<ISender>().Send(new Ping(journal, Reject: true)));
            Assert.Equal(["outer"], journal.Entries);
        }
    }
}

namespace Caesar.Tests.Regression.ContainerFidelity.ByHandBehavior
{
    public sealed class Outer<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : ByHand.IChecked
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            request.Journal.Add("outer");
            return request.Reject ? throw new ByHand.ValidationException("rejected") : next(cancellationToken);
        }
    }

    public sealed class ClosedOuter : IPipelineBehavior<ByHand.Ping, string>
    {
        public Task<string> Handle(ByHand.Ping request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            request.Journal.Add("closed-outer");
            return next(cancellationToken);
        }
    }

    public sealed class Inner<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : ByHand.IChecked
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            request.Journal.Add("inner");
            return next(cancellationToken);
        }
    }
}
