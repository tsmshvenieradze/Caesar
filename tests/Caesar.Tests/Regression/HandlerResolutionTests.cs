using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.HandlerResolution;

public interface IMissing;

public sealed record NeedsMissing : IRequest<string>;

public sealed class NeedsMissingHandler(IMissing missing) : IRequestHandler<NeedsMissing, string>
{
    public Task<string> Handle(NeedsMissing request, CancellationToken cancellationToken) => Task.FromResult(missing.ToString()!);
}

public sealed record StreamNeedsMissing : IStreamRequest<int>;

public sealed class StreamNeedsMissingHandler(IMissing missing) : IStreamRequestHandler<StreamNeedsMissing, int>
{
    public IAsyncEnumerable<int> Handle(StreamNeedsMissing request, CancellationToken cancellationToken)
        => throw new InvalidOperationException(missing.ToString());
}

public sealed record Broken : IRequest<string>;

public sealed class BrokenHandler : IRequestHandler<Broken, string>
{
    public BrokenHandler() => throw new InvalidOperationException("ctor: connection string missing");

    public Task<string> Handle(Broken request, CancellationToken cancellationToken) => Task.FromResult("unreachable");
}

public sealed record Page<T> : IRequest<List<T>>;

public sealed record PageStream<T> : IStreamRequest<T>;

public class HandlerResolutionTests
{
    [Fact]
    public async Task Registered_handler_with_a_missing_dependency_reports_the_container_error()
    {
        await using var provider = TestHost.Build<HandlerResolutionTests>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<ISender>().Send(new NeedsMissing()));

        Assert.Contains("Unable to resolve service for type", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IMissing), exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("No handler was found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Registered_stream_handler_with_a_missing_dependency_reports_the_container_error()
    {
        await using var provider = TestHost.Build<HandlerResolutionTests>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<ISender>().CreateStream(new StreamNeedsMissing()).ToListAsync().AsTask());

        Assert.Contains("Unable to resolve service for type", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("No handler was found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handler_constructor_failure_propagates_unchanged()
    {
        await using var provider = TestHost.Build<HandlerResolutionTests>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<ISender>().Send(new Broken()));

        Assert.Equal("ctor: connection string missing", exception.Message);
    }

    [Fact]
    public async Task Missing_handler_message_names_generic_types_as_written_in_source()
    {
        await using var provider = TestHost.Build<HandlerResolutionTests>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<ISender>().Send(new Page<Dictionary<string, int>>()));

        Assert.Contains("No handler was found", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            "request of type Caesar.Tests.Regression.HandlerResolution.Page<Dictionary<String, Int32>>.",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains("IRequestHandler<Page<Dictionary<String, Int32>>, List<Dictionary<String, Int32>>>", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("`", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Register your handlers", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_stream_handler_message_names_generic_types_as_written_in_source()
    {
        await using var provider = TestHost.Build<HandlerResolutionTests>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<ISender>().CreateStream(new PageStream<List<int>>()).ToListAsync().AsTask());

        Assert.Contains("IStreamRequestHandler<PageStream<List<Int32>>, List<Int32>>", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("`", exception.Message, StringComparison.Ordinal);
    }
}
