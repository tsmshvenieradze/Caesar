using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

// Exception handlers and actions are invoked through typed delegates, so a synchronous throw surfaces as itself
// rather than wrapped in TargetInvocationException, and a null Task is reported clearly.

namespace Caesar.Tests.Regression.ExceptionInvocation.SyncHandler
{
    public sealed class OriginalException() : Exception("original");

    public sealed class MappedException() : Exception("mapped");

    public sealed record Req(Journal Journal) : IRequest<string>;

    public sealed class ReqHandler : IRequestHandler<Req, string>
    {
        public Task<string> Handle(Req request, CancellationToken cancellationToken) => throw new OriginalException();
    }

    /// <summary>Translates the failure without the async keyword, so the throw happens before a Task exists.</summary>
    public sealed class SyncThrowingHandler : IRequestExceptionHandler<Req, string, OriginalException>
    {
        public Task Handle(Req request, OriginalException exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
            => throw new MappedException();
    }

    public sealed class ObservingAction : IRequestExceptionAction<Req>
    {
        public Task Execute(Req request, Exception exception, CancellationToken cancellationToken)
        {
            request.Journal.Add("action:" + exception.GetType().Name);
            return Task.CompletedTask;
        }
    }

    public class SyncThrowingHandlerTests
    {
        [Fact]
        public async Task Synchronous_throw_from_a_handler_surfaces_as_itself_and_reaches_actions_unwrapped()
        {
            var journal = new Journal();
            await using var provider = TestHost.Build<SyncThrowingHandlerTests>();

            var thrown = await Record.ExceptionAsync(() => provider.GetRequiredService<ISender>().Send(new Req(journal)));

            Assert.IsType<MappedException>(thrown);
            Assert.Equal(["action:MappedException"], journal.Entries);
        }
    }
}

namespace Caesar.Tests.Regression.ExceptionInvocation.SyncAction
{
    public sealed class OriginalException() : Exception("original");

    public sealed record Req : IRequest<string>;

    public sealed class ReqHandler : IRequestHandler<Req, string>
    {
        public Task<string> Handle(Req request, CancellationToken cancellationToken) => throw new OriginalException();
    }

    public sealed class SyncThrowingAction : IRequestExceptionAction<Req, OriginalException>
    {
        public Task Execute(Req request, OriginalException exception, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull((object?)null, "x");
            return Task.CompletedTask;
        }
    }

    public class SyncThrowingActionTests
    {
        [Fact]
        public async Task Synchronous_throw_from_an_action_is_recorded_unwrapped_and_the_original_is_rethrown()
        {
            await using var provider = TestHost.Build<SyncThrowingActionTests>();

            var thrown = await Record.ExceptionAsync(() => provider.GetRequiredService<ISender>().Send(new Req()));

            var original = Assert.IsType<OriginalException>(thrown);
            var failures = Assert.IsType<IReadOnlyList<Exception>>(original.Data["Caesar.ExceptionActionFailures"], exactMatch: false);
            Assert.IsType<ArgumentNullException>(Assert.Single(failures));
        }
    }
}

namespace Caesar.Tests.Regression.ExceptionInvocation.NullTaskHandler
{
    public sealed class OriginalException() : Exception("original");

    public sealed record Req : IRequest<string>;

    public sealed class ReqHandler : IRequestHandler<Req, string>
    {
        public Task<string> Handle(Req request, CancellationToken cancellationToken) => throw new OriginalException();
    }

    public sealed class NullTaskHandler : IRequestExceptionHandler<Req, string, OriginalException>
    {
        public Task Handle(Req request, OriginalException exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
            => null!;
    }

    public class NullTaskHandlerTests
    {
        [Fact]
        public async Task Handler_returning_a_null_task_fails_with_a_message_naming_it()
        {
            await using var provider = TestHost.Build<NullTaskHandlerTests>();

            var thrown = await Record.ExceptionAsync(() => provider.GetRequiredService<ISender>().Send(new Req()));

            var invalid = Assert.IsType<InvalidOperationException>(thrown);
            Assert.Contains(typeof(NullTaskHandler).FullName!, invalid.Message, StringComparison.Ordinal);
            Assert.IsType<OriginalException>(invalid.InnerException);
        }
    }
}

namespace Caesar.Tests.Regression.ExceptionInvocation.NullTaskAction
{
    public sealed class OriginalException() : Exception("original");

    public sealed record Req : IRequest<string>;

    public sealed class ReqHandler : IRequestHandler<Req, string>
    {
        public Task<string> Handle(Req request, CancellationToken cancellationToken) => throw new OriginalException();
    }

    public sealed class NullTaskAction : IRequestExceptionAction<Req, OriginalException>
    {
        public Task Execute(Req request, OriginalException exception, CancellationToken cancellationToken) => null!;
    }

    public class NullTaskActionTests
    {
        [Fact]
        public async Task Action_returning_a_null_task_is_recorded_as_a_failure_naming_it()
        {
            await using var provider = TestHost.Build<NullTaskActionTests>();

            var thrown = await Record.ExceptionAsync(() => provider.GetRequiredService<ISender>().Send(new Req()));

            var original = Assert.IsType<OriginalException>(thrown);
            var failures = Assert.IsType<IReadOnlyList<Exception>>(original.Data["Caesar.ExceptionActionFailures"], exactMatch: false);
            var invalid = Assert.IsType<InvalidOperationException>(Assert.Single(failures));
            Assert.Contains(typeof(NullTaskAction).FullName!, invalid.Message, StringComparison.Ordinal);
        }
    }
}
