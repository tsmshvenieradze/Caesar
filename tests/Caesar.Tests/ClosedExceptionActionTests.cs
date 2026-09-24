using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.ExceptionActionFanOut.Closed;

public sealed record Explode(Journal Journal) : IRequest<string>;

public sealed class ExplodeHandler : IRequestHandler<Explode, string>
{
    public Task<string> Handle(Explode request, CancellationToken cancellationToken)
        => throw new ArgumentNullException(nameof(request));
}

/// <summary>The shape the exception-handling guide recommends for reporting every failure of a request.</summary>
public sealed class ReportExplodeFailures : IRequestExceptionAction<Explode>
{
    public Task Execute(Explode request, Exception exception, CancellationToken cancellationToken)
    {
        request.Journal.Add(exception.GetType().Name);
        return Task.CompletedTask;
    }
}

public class ClosedExceptionActionTests
{
    [Fact]
    public async Task Closed_catch_all_action_runs_once_per_failure()
    {
        var journal = new Journal();
        await using var provider = TestHost.Build<ClosedExceptionActionTests>();

        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.GetRequiredService<ISender>().Send(new Explode(journal)));

        Assert.Equal(["ArgumentNullException"], journal.Entries);
    }
}
