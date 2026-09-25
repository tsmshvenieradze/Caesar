using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

// As in MediatR 12, a class that implements an exception action or handler for several levels of the thrown
// exception's hierarchy runs once, for its most specific level.

namespace Caesar.Tests.Regression.ExceptionDeduplication.Actions
{
    public sealed record Pay(Journal Journal) : IRequest<string>;

    public sealed class PayHandler : IRequestHandler<Pay, string>
    {
        public Task<string> Handle(Pay request, CancellationToken cancellationToken) => throw new ArgumentException("declined");
    }

    public sealed class Audit : IRequestExceptionAction<Pay, ArgumentException>, IRequestExceptionAction<Pay, Exception>
    {
        public Task Execute(Pay request, ArgumentException exception, CancellationToken cancellationToken)
        {
            request.Journal.Add("audit:ArgumentException");
            return Task.CompletedTask;
        }

        public Task Execute(Pay request, Exception exception, CancellationToken cancellationToken)
        {
            request.Journal.Add("audit:Exception");
            return Task.CompletedTask;
        }
    }

    public class ActionDeduplicationTests
    {
        [Fact]
        public async Task Action_implementing_several_levels_runs_once_for_the_most_specific()
        {
            var journal = new Journal();
            await using var provider = TestHost.Build<ActionDeduplicationTests>();

            await Assert.ThrowsAsync<ArgumentException>(() => provider.GetRequiredService<ISender>().Send(new Pay(journal)));

            Assert.Equal(["audit:ArgumentException"], journal.Entries);
        }
    }
}

namespace Caesar.Tests.Regression.ExceptionDeduplication.Handlers
{
    public sealed record Pay(Journal Journal) : IRequest<string>;

    public sealed class PayHandler : IRequestHandler<Pay, string>
    {
        public Task<string> Handle(Pay request, CancellationToken cancellationToken) => throw new ArgumentException("declined");
    }

    /// <summary>Observes without recovering, so every level would be visited if not de-duplicated.</summary>
    public sealed class Observer : IRequestExceptionHandler<Pay, string, ArgumentException>, IRequestExceptionHandler<Pay, string, Exception>
    {
        public Task Handle(Pay request, ArgumentException exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
        {
            request.Journal.Add("observer:ArgumentException");
            return Task.CompletedTask;
        }

        public Task Handle(Pay request, Exception exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
        {
            request.Journal.Add("observer:Exception");
            return Task.CompletedTask;
        }
    }

    public class HandlerDeduplicationTests
    {
        [Fact]
        public async Task Handler_implementing_several_levels_runs_once_for_the_most_specific()
        {
            var journal = new Journal();
            await using var provider = TestHost.Build<HandlerDeduplicationTests>();

            await Assert.ThrowsAsync<ArgumentException>(() => provider.GetRequiredService<ISender>().Send(new Pay(journal)));

            Assert.Equal(["observer:ArgumentException"], journal.Entries);
        }
    }
}
