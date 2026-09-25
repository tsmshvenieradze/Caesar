using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

// Exception actions keep only the most recent failures on an exception, so an exception instance that several sends
// share (a memoized failure) does not collect failures without bound. De-duplication by implementation type applies across
// the exception's hierarchy only: distinct registrations of one class at the same level all run.

namespace Caesar.Tests.Regression.ExceptionActionPass.Shared
{
    public sealed record Load(Journal Journal) : IRequest<string>;

    public sealed class LoadHandler : IRequestHandler<Load, string>
    {
        /// <summary>A failure cached by the handler and thrown again on every call.</summary>
        public static readonly InvalidOperationException Memoized = new("init failed");

        public Task<string> Handle(Load request, CancellationToken cancellationToken) => Task.FromException<string>(Memoized);
    }

    public sealed class BrokenTelemetry : IRequestExceptionAction<Load, InvalidOperationException>
    {
        public Task Execute(Load request, InvalidOperationException exception, CancellationToken cancellationToken)
            => Task.FromException(new TimeoutException("sink down"));
    }

    public class SharedExceptionTests
    {
        [Fact]
        public async Task A_shared_exception_keeps_a_bounded_number_of_action_failures()
        {
            await using var provider = TestHost.Build<SharedExceptionTests>();
            var sender = provider.GetRequiredService<ISender>();

            for (var i = 0; i < 100; i++)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(new Load(new Journal())));
            }

            var failures = Assert.IsAssignableFrom<IReadOnlyList<Exception>>(LoadHandler.Memoized.Data["Caesar.ExceptionActionFailures"]);
            Assert.Equal(16, failures.Count);
            Assert.All(failures, static f => Assert.IsType<TimeoutException>(f));
        }
    }
}

namespace Caesar.Tests.Regression.ExceptionActionPass.Instances
{
    public sealed record Fail(Journal Journal) : IRequest<string>;

    public sealed class FailHandler : IRequestHandler<Fail, string>
    {
        public Task<string> Handle(Fail request, CancellationToken cancellationToken) => Task.FromException<string>(new ArgumentException("bad"));
    }

    /// <summary>One class, registered twice with different configuration, like one sink per telemetry backend.</summary>
    public sealed class SinkAction(string sink) : IRequestExceptionAction<Fail, ArgumentException>
    {
        public Task Execute(Fail request, ArgumentException exception, CancellationToken cancellationToken)
        {
            request.Journal.Add(sink);
            return Task.CompletedTask;
        }
    }

    public class DistinctInstanceTests
    {
        [Fact]
        public async Task Distinct_registrations_of_one_action_class_at_the_same_level_all_run()
        {
            var journal = new Journal();
            await using var provider = TestHost.Build<DistinctInstanceTests>(
                cfg => cfg.TypeEvaluator = static _ => false,
                services =>
                {
                    services.AddTransient<IRequestHandler<Fail, string>, FailHandler>();
                    services.AddTransient<IRequestExceptionAction<Fail, ArgumentException>>(_ => new SinkAction("audit"));
                    services.AddTransient<IRequestExceptionAction<Fail, ArgumentException>>(_ => new SinkAction("metrics"));
                });

            await Assert.ThrowsAsync<ArgumentException>(() => provider.GetRequiredService<ISender>().Send(new Fail(journal)));

            Assert.Equal(["audit", "metrics"], journal.Entries);
        }
    }
}
