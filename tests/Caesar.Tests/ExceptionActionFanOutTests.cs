using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.ExceptionActionFanOut.Open;

// An open generic action over TException is closed for every type in the thrown exception's hierarchy, as the
// exception-handling guide warns. The recommended closed shape is pinned in ClosedExceptionActionTests.

public interface IJournaled
{
    Journal Journal { get; }
}

public sealed record Explode(Journal Journal) : IRequest<string>, IJournaled;

public sealed class ExplodeHandler : IRequestHandler<Explode, string>
{
    public Task<string> Handle(Explode request, CancellationToken cancellationToken)
        => throw new ArgumentNullException(nameof(request));
}

public sealed class ReportFailures<TRequest, TException> : IRequestExceptionAction<TRequest, TException>
    where TRequest : notnull, IJournaled
    where TException : Exception
{
    public Task Execute(TRequest request, TException exception, CancellationToken cancellationToken)
    {
        request.Journal.Add(typeof(TException).Name);
        return Task.CompletedTask;
    }
}

public class OpenExceptionActionTests
{
    [Fact]
    public async Task Open_generic_action_runs_once_for_each_type_in_the_exception_hierarchy()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<OpenExceptionActionTests>();

        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.GetRequiredService<ISender>().Send(new Explode(journal)));

        Assert.Equal(["ArgumentNullException", "ArgumentException", "SystemException", "Exception"], journal.Entries);
    }
}
