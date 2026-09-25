using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

// A request declared as IRequest<Dog> sent through IRequest<Animal> goes to the handler registered for the call-site
// response type when there is one, as in MediatR; only when there is none does it fall back to the declared one.

namespace Caesar.Tests.Regression.CovariantCallSite;

public class Animal(string name)
{
    public string Name { get; } = name;
}

public sealed class Dog(string name) : Animal(name);

public sealed record GetPet : IRequest<Dog>;

public sealed class GetPetAsDog : IRequestHandler<GetPet, Dog>
{
    public Task<Dog> Handle(GetPet request, CancellationToken cancellationToken) => Task.FromResult(new Dog("declared"));
}

public sealed class GetPetAsAnimal : IRequestHandler<GetPet, Animal>
{
    public Task<Animal> Handle(GetPet request, CancellationToken cancellationToken) => Task.FromResult(new Animal("call-site"));
}

public sealed record StreamPets : IStreamRequest<Dog>;

public sealed class StreamPetsAsDogs : IStreamRequestHandler<StreamPets, Dog>
{
    public async IAsyncEnumerable<Dog> Handle(StreamPets request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return new Dog("declared");
    }
}

public sealed class StreamPetsAsAnimals : IStreamRequestHandler<StreamPets, Animal>
{
    public async IAsyncEnumerable<Animal> Handle(StreamPets request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return new Animal("call-site");
    }
}

public class CovariantCallSiteTests
{
    private static Task<T> SendAs<T>(ISender sender, IRequest<T> request) => sender.Send(request);

    private static async Task<List<string>> StreamAs<T>(ISender sender, IStreamRequest<T> request)
        where T : Animal
    {
        var names = new List<string>();
        await foreach (var item in sender.CreateStream(request))
        {
            names.Add(item.Name);
        }

        return names;
    }

    [Fact]
    public async Task A_covariant_send_prefers_the_handler_registered_for_the_call_site_response_type()
    {
        await using var provider = TestHost.Build<CovariantCallSiteTests>();
        var sender = provider.GetRequiredService<ISender>();

        var asAnimal = await SendAs<Animal>(sender, new GetPet());
        var asDog = await sender.Send(new GetPet());

        Assert.Equal("call-site", asAnimal.Name);
        Assert.Equal("declared", asDog.Name);
    }

    [Fact]
    public async Task A_covariant_stream_prefers_the_handler_registered_for_the_call_site_item_type()
    {
        await using var provider = TestHost.Build<CovariantCallSiteTests>();
        var sender = provider.GetRequiredService<ISender>();

        Assert.Equal(["call-site"], await StreamAs<Animal>(sender, new StreamPets()));
        Assert.Equal(["declared"], await StreamAs<Dog>(sender, new StreamPets()));
    }
}
