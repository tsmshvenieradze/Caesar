using Caesar.Pipeline;
using Microsoft.Extensions.Logging;

namespace Caesar.Sample.Application.Customers;

public sealed record GetCustomer(Guid Id) : IRequest<Customer?>;

public sealed class CustomerNotFoundException(Guid id) : Exception($"Customer {id} was not found.")
{
    public Guid Id { get; } = id;
}

public sealed class GetCustomerHandler(ICustomerRepository repository) : IRequestHandler<GetCustomer, Customer?>
{
    public async Task<Customer?> Handle(GetCustomer request, CancellationToken cancellationToken)
        => await repository.Find(request.Id, cancellationToken) ?? throw new CustomerNotFoundException(request.Id);
}

/// <summary>Turns a "not found" failure into a null response instead of letting it bubble up.</summary>
public sealed class CustomerNotFoundHandler(ILogger<CustomerNotFoundHandler> logger)
    : IRequestExceptionHandler<GetCustomer, Customer?, CustomerNotFoundException>
{
    public Task Handle(GetCustomer request, CustomerNotFoundException exception, RequestExceptionHandlerState<Customer?> state, CancellationToken cancellationToken)
    {
        logger.LogWarning("Customer {CustomerId} not found; returning null", exception.Id);
        state.SetHandled(null);
        return Task.CompletedTask;
    }
}
