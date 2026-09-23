using Caesar.Pipeline;
using Microsoft.Extensions.Logging;

namespace Caesar.Sample.Application.Customers;

public sealed record DeactivateCustomer(Guid Id) : IRequest;

public sealed class DeactivateCustomerHandler(ICustomerRepository repository) : IRequestHandler<DeactivateCustomer>
{
    public async Task Handle(DeactivateCustomer request, CancellationToken cancellationToken)
    {
        var customer = await repository.Find(request.Id, cancellationToken)
            ?? throw new CustomerNotFoundException(request.Id);

        await repository.Save(customer with { IsActive = false }, cancellationToken);
    }
}

/// <summary>Post-processor: runs after the handler for this one request type.</summary>
public sealed class AuditDeactivation(ILogger<AuditDeactivation> logger) : IRequestPostProcessor<DeactivateCustomer, Unit>
{
    public Task Process(DeactivateCustomer request, Unit response, CancellationToken cancellationToken)
    {
        logger.LogInformation("Audit: customer {CustomerId} deactivated", request.Id);
        return Task.CompletedTask;
    }
}
