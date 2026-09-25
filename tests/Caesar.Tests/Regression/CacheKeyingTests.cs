using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.CacheKeying;

// Wrappers are cached process-wide, so every test uses request types of its own: a call made by one test must not
// decide whether a call in another test works.

public class Animal(string name)
{
    public string Name { get; } = name;
}

public sealed class Dog(string name) : Animal(name);

public sealed class Cat(string name) : Animal(name);

public sealed record GetDog : IRequest<Dog>;

public sealed class GetDogHandler : IRequestHandler<GetDog, Dog>
{
    public Task<Dog> Handle(GetDog request, CancellationToken cancellationToken) => Task.FromResult(new Dog("rex"));
}

public sealed record GetDogFirst : IRequest<Dog>;

public sealed class GetDogFirstHandler : IRequestHandler<GetDogFirst, Dog>
{
    public Task<Dog> Handle(GetDogFirst request, CancellationToken cancellationToken) => Task.FromResult(new Dog("fido"));
}

public sealed record Both : IRequest<int>, IRequest<string>;

public sealed class BothHandler : IRequestHandler<Both, int>, IRequestHandler<Both, string>
{
    Task<int> IRequestHandler<Both, int>.Handle(Both request, CancellationToken cancellationToken) => Task.FromResult(1);

    Task<string> IRequestHandler<Both, string>.Handle(Both request, CancellationToken cancellationToken) => Task.FromResult("one");
}

public sealed record BothObjectFirst : IRequest<int>, IRequest<string>;

public sealed class BothObjectFirstHandler : IRequestHandler<BothObjectFirst, int>, IRequestHandler<BothObjectFirst, string>
{
    Task<int> IRequestHandler<BothObjectFirst, int>.Handle(BothObjectFirst request, CancellationToken cancellationToken) => Task.FromResult(2);

    Task<string> IRequestHandler<BothObjectFirst, string>.Handle(BothObjectFirst request, CancellationToken cancellationToken) => Task.FromResult("two");
}

public sealed record CommandAndQuery(Journal Journal) : IRequest, IRequest<int>;

public sealed class CommandAndQueryHandler : IRequestHandler<CommandAndQuery>, IRequestHandler<CommandAndQuery, int>
{
    Task IRequestHandler<CommandAndQuery>.Handle(CommandAndQuery request, CancellationToken cancellationToken)
    {
        request.Journal.Add("command");
        return Task.CompletedTask;
    }

    Task<int> IRequestHandler<CommandAndQuery, int>.Handle(CommandAndQuery request, CancellationToken cancellationToken)
    {
        request.Journal.Add("query");
        return Task.FromResult(42);
    }
}

public sealed record DogOrCat : IRequest<Dog>, IRequest<Cat>;

public sealed class DogOrCatHandler : IRequestHandler<DogOrCat, Dog>, IRequestHandler<DogOrCat, Cat>
{
    Task<Dog> IRequestHandler<DogOrCat, Dog>.Handle(DogOrCat request, CancellationToken cancellationToken) => Task.FromResult(new Dog("d"));

    Task<Cat> IRequestHandler<DogOrCat, Cat>.Handle(DogOrCat request, CancellationToken cancellationToken) => Task.FromResult(new Cat("c"));
}

public sealed record Dogs : IStreamRequest<Dog>;

public sealed class DogsHandler : IStreamRequestHandler<Dogs, Dog>
{
    public IAsyncEnumerable<Dog> Handle(Dogs request, CancellationToken cancellationToken) => Yield(new Dog("a"), new Dog("b"));

    internal static async IAsyncEnumerable<T> Yield<T>(params T[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}

public sealed record DogsFirst : IStreamRequest<Dog>;

public sealed class DogsFirstHandler : IStreamRequestHandler<DogsFirst, Dog>
{
    public IAsyncEnumerable<Dog> Handle(DogsFirst request, CancellationToken cancellationToken) => DogsHandler.Yield(new Dog("c"));
}

public sealed record BothStream : IStreamRequest<int>, IStreamRequest<string>;

public sealed class BothStreamHandler : IStreamRequestHandler<BothStream, int>, IStreamRequestHandler<BothStream, string>
{
    IAsyncEnumerable<int> IStreamRequestHandler<BothStream, int>.Handle(BothStream request, CancellationToken cancellationToken) => DogsHandler.Yield(1, 2);

    IAsyncEnumerable<string> IStreamRequestHandler<BothStream, string>.Handle(BothStream request, CancellationToken cancellationToken) => DogsHandler.Yield("x");
}

public class CacheKeyingTests
{
    /// <summary>A generic dispatch helper: the call site sees only the covariant <c>IRequest&lt;T&gt;</c>.</summary>
    private static Task<T> SendAs<T>(ISender sender, IRequest<T> request) => sender.Send(request);

    private static IAsyncEnumerable<T> StreamAs<T>(ISender sender, IStreamRequest<T> request) => sender.CreateStream(request);

    [Fact]
    public async Task Covariant_send_dispatches_to_the_declared_handler_and_does_not_break_later_calls()
    {
        await using var provider = TestHost.Build<CacheKeyingTests>();
        var sender = provider.GetRequiredService<ISender>();

        var animal = await SendAs<Animal>(sender, new GetDog());
        var asObject = await SendAs<object>(sender, new GetDog());
        var dog = await sender.Send(new GetDog());
        var boxed = await sender.Send((object)new GetDog());

        Assert.Equal("rex", Assert.IsType<Dog>(animal).Name);
        Assert.IsType<Dog>(asObject);
        Assert.Equal("rex", dog.Name);
        Assert.IsType<Dog>(boxed);

        // The cache is process-wide: a brand-new container must not inherit a broken entry either.
        await using var other = TestHost.Build<CacheKeyingTests>();
        Assert.Equal("rex", (await other.GetRequiredService<ISender>().Send(new GetDog())).Name);
    }

    [Fact]
    public async Task Exact_send_first_does_not_break_a_later_covariant_send()
    {
        await using var provider = TestHost.Build<CacheKeyingTests>();
        var sender = provider.GetRequiredService<ISender>();

        var dog = await sender.Send(new GetDogFirst());
        var animal = await SendAs<Animal>(sender, new GetDogFirst());

        Assert.Equal("fido", dog.Name);
        Assert.Equal("fido", animal.Name);
    }

    [Fact]
    public async Task Request_declaring_two_responses_works_through_each_typed_send()
    {
        await using var provider = TestHost.Build<CacheKeyingTests>();
        var sender = provider.GetRequiredService<ISender>();

        Assert.Equal(1, await SendAs<int>(sender, new Both()));
        Assert.Equal("one", await SendAs<string>(sender, new Both()));
        Assert.Equal(1, await SendAs<int>(sender, new Both()));
    }

    [Fact]
    public async Task Send_object_rejects_a_request_declaring_two_responses_without_poisoning_typed_sends()
    {
        await using var provider = TestHost.Build<CacheKeyingTests>();
        var sender = provider.GetRequiredService<ISender>();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => sender.Send((object)new BothObjectFirst()));

        Assert.Contains("IRequest<Int32>", exception.Message, StringComparison.Ordinal);
        Assert.Contains("IRequest<String>", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, await SendAs<int>(sender, new BothObjectFirst()));
        Assert.Equal("two", await SendAs<string>(sender, new BothObjectFirst()));
    }

    [Fact]
    public async Task Command_that_is_also_a_query_works_through_every_typed_overload_in_any_order()
    {
        await using var provider = TestHost.Build<CacheKeyingTests>();
        var sender = provider.GetRequiredService<ISender>();
        var journal = new Journal();

        await Assert.ThrowsAsync<ArgumentException>(() => sender.Send((object)new CommandAndQuery(journal)));
        var answer = await SendAs<int>(sender, new CommandAndQuery(journal));
        await sender.Send(new CommandAndQuery(journal));
        var unit = await SendAs<Unit>(sender, new CommandAndQuery(journal));

        Assert.Equal(42, answer);
        Assert.Equal(Unit.Value, unit);
        Assert.Equal(["query", "command", "command"], journal.Entries);
    }

    [Fact]
    public async Task Covariant_send_that_matches_two_declared_responses_is_rejected()
    {
        await using var provider = TestHost.Build<CacheKeyingTests>();
        var sender = provider.GetRequiredService<ISender>();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => SendAs<Animal>(sender, new DogOrCat()));

        Assert.Contains("IRequest<Dog>", exception.Message, StringComparison.Ordinal);
        Assert.Contains("IRequest<Cat>", exception.Message, StringComparison.Ordinal);
        Assert.Equal("d", (await SendAs<Dog>(sender, new DogOrCat())).Name);
    }

    [Fact]
    public async Task Covariant_stream_dispatches_to_the_declared_handler_and_does_not_break_later_calls()
    {
        await using var provider = TestHost.Build<CacheKeyingTests>();
        var sender = provider.GetRequiredService<ISender>();

        var animals = await StreamAs<Animal>(sender, new Dogs()).ToListAsync();
        var dogs = await sender.CreateStream(new Dogs()).ToListAsync();
        var boxed = await sender.CreateStream((object)new Dogs()).ToListAsync();

        Assert.Equal(["a", "b"], animals.Select(static a => a.Name));
        Assert.Equal(["a", "b"], dogs.Select(static d => d.Name));
        Assert.All(boxed, static item => Assert.IsType<Dog>(item));
    }

    [Fact]
    public async Task Exact_stream_first_does_not_break_a_later_covariant_stream()
    {
        await using var provider = TestHost.Build<CacheKeyingTests>();
        var sender = provider.GetRequiredService<ISender>();

        var dogs = await sender.CreateStream(new DogsFirst()).ToListAsync();
        var animals = await StreamAs<Animal>(sender, new DogsFirst()).ToListAsync();

        Assert.Equal(["c"], dogs.Select(static d => d.Name));
        Assert.Equal(["c"], animals.Select(static a => a.Name));
    }

    [Fact]
    public async Task Stream_request_declaring_two_item_types_works_typed_and_is_rejected_as_object()
    {
        await using var provider = TestHost.Build<CacheKeyingTests>();
        var sender = provider.GetRequiredService<ISender>();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => sender.CreateStream((object)new BothStream()).ToListAsync().AsTask());

        Assert.Contains("IStreamRequest<Int32>", exception.Message, StringComparison.Ordinal);
        Assert.Equal([1, 2], await StreamAs<int>(sender, new BothStream()).ToListAsync());
        Assert.Equal(["x"], await StreamAs<string>(sender, new BothStream()).ToListAsync());
    }
}
