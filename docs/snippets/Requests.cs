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
