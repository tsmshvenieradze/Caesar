using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

// A failing exception action must not replace the original exception or stop the remaining actions. Failures are
// attached to the original exception's Data under "Caesar.ExceptionActionFailures".

namespace Caesar.Tests.Regression.ExceptionActionFailures.Continue
{
    public class BaseDomainException(string message) : Exception(message);

    public sealed class DomainException(string message) : BaseDomainException(message);

    public sealed record Req(Journal Journal, DomainException ToThrow) : IRequest<string>;

    public sealed class ReqHandler : IRequestHandler<Req, string>
    {
        public Task<string> Handle(Req request, CancellationToken cancellationToken) => throw request.ToThrow;
    }

    /// <summary>Most specific level: throws before returning a Task.</summary>
    public sealed class SyncThrowingAction : IRequestExceptionAction<Req, DomainException>
    {
        public Task Execute(Req request, DomainException exception, CancellationToken cancellationToken)
        {
            request.Journal.Add("sync");
            throw new TimeoutException("sink down");
        }
    }

    /// <summary>Middle level: fails asynchronously.</summary>
    public sealed class AsyncThrowingAction : IRequestExceptionAction<Req, BaseDomainException>
    {
        public async Task Execute(Req request, BaseDomainException exception, CancellationToken cancellationToken)
        {
            request.Journal.Add("async");
            await Task.Yield();
            throw new InvalidOperationException("async failure");
        }
    }

    /// <summary>Least specific level: must still run.</summary>
    public sealed class LastAction : IRequestExceptionAction<Req>
    {
        public Task Execute(Req request, Exception exception, CancellationToken cancellationToken)
        {
            request.Journal.Add("last");
            return Task.CompletedTask;
        }
    }

    public class ContinueAfterFailureTests
    {
        [Fact]
        public async Task Every_action_runs_and_the_original_exception_is_rethrown_with_the_failures_attached()
        {
            var journal = new Journal();
            var original = new DomainException("boom");
            await using var provider = TestHost.Build<ContinueAfterFailureTests>();

            var thrown = await Record.ExceptionAsync(() => provider.GetRequiredService<ISender>().Send(new Req(journal, original)));

            Assert.Same(original, thrown);
            Assert.Contains(nameof(ReqHandler), thrown.StackTrace, StringComparison.Ordinal);
            Assert.Equal(["sync", "async", "last"], journal.Entries);

            var failures = Assert.IsType<IReadOnlyList<Exception>>(thrown.Data["Caesar.ExceptionActionFailures"], exactMatch: false);
            Assert.Collection(
                failures,
                static f => Assert.Equal("sink down", Assert.IsType<TimeoutException>(f).Message),
                static f => Assert.Equal("async failure", Assert.IsType<InvalidOperationException>(f).Message));
        }
    }
}

namespace Caesar.Tests.Regression.ExceptionActionFailures.Rethrow
{
    public sealed class DomainException() : Exception("boom");

    public sealed record Req(Journal Journal) : IRequest<string>;

    public sealed class ReqHandler : IRequestHandler<Req, string>
    {
        public Task<string> Handle(Req request, CancellationToken cancellationToken) => throw new DomainException();
    }

    /// <summary>Hands the original exception back instead of failing on its own.</summary>
    public sealed class RethrowingAction : IRequestExceptionAction<Req, DomainException>
    {
        public Task Execute(Req request, DomainException exception, CancellationToken cancellationToken)
        {
            request.Journal.Add("rethrow");
            return Task.FromException(exception);
        }
    }

    public sealed class LastAction : IRequestExceptionAction<Req>
    {
        public Task Execute(Req request, Exception exception, CancellationToken cancellationToken)
        {
            request.Journal.Add("last");
            return Task.CompletedTask;
        }
    }

    public class RethrowOriginalTests
    {
        [Fact]
        public async Task Action_rethrowing_the_original_is_not_recorded_as_a_failure()
        {
            var journal = new Journal();
            await using var provider = TestHost.Build<RethrowOriginalTests>();

            var thrown = await Record.ExceptionAsync(() => provider.GetRequiredService<ISender>().Send(new Req(journal)));

            Assert.IsType<DomainException>(thrown);
            Assert.False(thrown.Data.Contains("Caesar.ExceptionActionFailures"));
            Assert.Equal(["rethrow", "last"], journal.Entries);
        }
    }
}

namespace Caesar.Tests.Regression.ExceptionActionFailures.Nested
{
    public sealed class DomainException() : Exception("inner failure");

    public sealed record Outer : IRequest<string>;

    public sealed record Inner : IRequest<string>;

    public sealed class OuterHandler(ISender sender) : IRequestHandler<Outer, string>
    {
        public Task<string> Handle(Outer request, CancellationToken cancellationToken) => sender.Send(new Inner(), cancellationToken);
    }

    public sealed class InnerHandler : IRequestHandler<Inner, string>
    {
        public Task<string> Handle(Inner request, CancellationToken cancellationToken) => throw new DomainException();
    }

    public sealed class InnerAction : IRequestExceptionAction<Inner>
    {
        public Task Execute(Inner request, Exception exception, CancellationToken cancellationToken)
            => Task.FromException(new TimeoutException("inner action"));
    }

    public sealed class OuterAction : IRequestExceptionAction<Outer>
    {
        public Task Execute(Outer request, Exception exception, CancellationToken cancellationToken)
            => Task.FromException(new TimeoutException("outer action"));
    }

    public class NestedSendFailureTests
    {
        [Fact]
        public async Task Failures_from_a_nested_send_are_appended_to_not_replaced()
        {
            await using var provider = TestHost.Build<NestedSendFailureTests>();

            var thrown = await Record.ExceptionAsync(() => provider.GetRequiredService<ISender>().Send(new Outer()));

            Assert.IsType<DomainException>(thrown);
            var failures = Assert.IsType<IReadOnlyList<Exception>>(thrown.Data["Caesar.ExceptionActionFailures"], exactMatch: false);
            Assert.Equal(["inner action", "outer action"], failures.Select(static f => f.Message));
        }
    }
}
