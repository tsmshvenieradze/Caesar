namespace Caesar.Docs.Snippets;

#region query
public sealed record GetCustomer(Guid Id) : IRequest<Customer?>;

public sealed class GetCustomerHandler(ICustomerRepository repository) : IRequestHandler<GetCustomer, Customer?>
{
    public Task<Customer?> Handle(GetCustomer request, CancellationToken cancellationToken)
        => repository.Find(request.Id, cancellationToken);
}
#endregion

public static class RuntimeDispatch
{
    #region runtime-dispatch
    // The request type is only known at runtime, e.g. deserialized from a message queue.
    public static async Task<object?> Dispatch(ISender sender, object message, CancellationToken cancellationToken)
    {
        // Returns the handler's response, or Unit.Value for a command that implements IRequest.
        return await sender.Send(message, cancellationToken);
    }
    #endregion
}

#region covariant-send
public abstract record Animal(string Name);

public sealed record Dog(string Name) : Animal(Name);

public sealed record AdoptDog(string Name) : IRequest<Dog>;

public sealed class AdoptDogHandler : IRequestHandler<AdoptDog, Dog>
{
    public Task<Dog> Handle(AdoptDog request, CancellationToken cancellationToken) => Task.FromResult(new Dog(request.Name));
}

public static class Shelter
{
    // IRequest<out TResponse> is covariant, so an AdoptDog is also an IRequest<Animal>. It still goes to
    // IRequestHandler<AdoptDog, Dog> and the behaviors for <AdoptDog, Dog>; the Dog comes back as an Animal.
    public static Task<Animal> Admit(ISender sender, IRequest<Animal> request, CancellationToken cancellationToken)
        => sender.Send(request, cancellationToken);
}
#endregion
