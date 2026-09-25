using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

// The built-in behaviors skip their async machinery on the success path (exception behaviors) or when there is
// nothing to run (processor behaviors). These tests pin that the shortcuts change nothing observable.

namespace Caesar.Tests.Regression.BuiltInFastPath.SyncThrow
{
    public sealed class GuardException() : Exception("guard");

    public sealed record Req(Journal Journal, bool Throw) : IRequest<string>;

    public sealed class ReqHandler : IRequestHandler<Req, string>
    {
        public Task<string> Handle(Req request, CancellationToken cancellationToken) => Task.FromResult("ok");
    }

    /// <summary>A non-async user behavior that throws before any Task exists.</summary>
    public sealed class SyncGuardBehavior : IPipelineBehavior<Req, string>
    {
        public Task<string> Handle(Req request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
            => request.Throw ? throw new GuardException() : next(cancellationToken);
    }

    public sealed class Recover : IRequestExceptionHandler<Req, string, GuardException>
    {
        public Task Handle(Req request, GuardException exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
        {
            request.Journal.Add("handler");
            state.SetHandled("recovered");
            return Task.CompletedTask;
        }
    }

    public sealed class Log : IRequestExceptionAction<Req>
    {
        public Task Execute(Req request, Exception exception, CancellationToken cancellationToken)
        {
            request.Journal.Add("action:" + exception.GetType().Name);
            return Task.CompletedTask;
        }
    }

    public class SyncThrowTests
    {
        [Fact]
        public async Task Synchronous_success_passes_through_both_exception_behaviors()
        {
            var journal = new Journal();
            await using var provider = TestHost.Build<SyncThrowTests>(static cfg => cfg.AddBehavior<SyncGuardBehavior>());

            Assert.Equal("ok", await provider.GetRequiredService<ISender>().Send(new Req(journal, Throw: false)));
            Assert.Empty(journal.Entries);
        }

        [Fact]
        public async Task Synchronous_throw_from_the_inner_pipeline_is_routed_to_handlers()
        {
            var journal = new Journal();
            await using var provider = TestHost.Build<SyncThrowTests>(static cfg => cfg.AddBehavior<SyncGuardBehavior>());

            Assert.Equal("recovered", await provider.GetRequiredService<ISender>().Send(new Req(journal, Throw: true)));
            Assert.Equal(["handler"], journal.Entries);
        }

        [Fact]
        public async Task Synchronous_throw_from_the_inner_pipeline_is_routed_to_actions()
        {
            var journal = new Journal();
            await using var provider = TestHost.Build<SyncThrowTests>(static cfg =>
            {
                cfg.AddBehavior<SyncGuardBehavior>();
                cfg.TypeEvaluator = static t => t == typeof(ReqHandler) || t == typeof(Log);
            });

            var thrown = await Record.ExceptionAsync(() => provider.GetRequiredService<ISender>().Send(new Req(journal, Throw: true)));

            Assert.IsType<GuardException>(thrown);
            Assert.Contains(nameof(SyncGuardBehavior), thrown.StackTrace, StringComparison.Ordinal);
            Assert.Equal(["action:GuardException"], journal.Entries);
        }
    }
}

namespace Caesar.Tests.Regression.BuiltInFastPath.EmptyProcessors
{
    public sealed record Plain(Journal Journal, bool Fail) : IRequest<string>;

    public sealed record Processed(Journal Journal) : IRequest<string>;

    public sealed class PlainHandler : IRequestHandler<Plain, string>
    {
        public Task<string> Handle(Plain request, CancellationToken cancellationToken)
        {
            request.Journal.Add("handler");
            return request.Fail ? throw new InvalidOperationException("plain failed") : Task.FromResult("plain");
        }
    }

    public sealed class ProcessedHandler : IRequestHandler<Processed, string>
    {
        public Task<string> Handle(Processed request, CancellationToken cancellationToken)
        {
            request.Journal.Add("handler");
            return Task.FromResult("processed");
        }
    }

    public sealed class Pre : IRequestPreProcessor<Processed>
    {
        public Task Process(Processed request, CancellationToken cancellationToken)
        {
            request.Journal.Add("pre");
            return Task.CompletedTask;
        }
    }

    public sealed class Post : IRequestPostProcessor<Processed, string>
    {
        public Task Process(Processed request, string response, CancellationToken cancellationToken)
        {
            request.Journal.Add("post:" + response);
            return Task.CompletedTask;
        }
    }

    public class EmptyProcessorTests
    {
        [Fact]
        public async Task Request_without_processors_passes_straight_through()
        {
            var journal = new Journal();
            await using var provider = TestHost.Build<EmptyProcessorTests>();

            Assert.Equal("plain", await provider.GetRequiredService<ISender>().Send(new Plain(journal, Fail: false)));
            Assert.Equal(["handler"], journal.Entries);
        }

        [Fact]
        public async Task Request_without_processors_still_surfaces_handler_failures()
        {
            var journal = new Journal();
            await using var provider = TestHost.Build<EmptyProcessorTests>();

            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.GetRequiredService<ISender>().Send(new Plain(journal, Fail: true)));

            Assert.Equal("plain failed", thrown.Message);
        }

        [Fact]
        public async Task Request_with_processors_still_runs_them()
        {
            var journal = new Journal();
            await using var provider = TestHost.Build<EmptyProcessorTests>();

            Assert.Equal("processed", await provider.GetRequiredService<ISender>().Send(new Processed(journal)));
            Assert.Equal(["pre", "handler", "post:processed"], journal.Entries);
        }
    }
}
