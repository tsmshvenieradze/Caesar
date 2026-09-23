using Caesar.Sample.Application.Behaviors;

namespace Caesar.Sample.Application.Customers;

public sealed record CreateCustomer(string Name, string Email) : IRequest<Guid>;

public sealed class CreateCustomerValidator : IValidator<CreateCustomer>
{
    public IEnumerable<string> Validate(CreateCustomer request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            yield return "Name is required.";
        }

        if (!request.Email.Contains('@', StringComparison.Ordinal))
        {
            yield return "Email is invalid.";
        }
    }
}

public sealed class CreateCustomerHandler(ICustomerRepository repository, IPublisher publisher) : IRequestHandler<CreateCustomer, Guid>
{
    public async Task<Guid> Handle(CreateCustomer request, CancellationToken cancellationToken)
    {
        var customer = new Customer(Guid.NewGuid(), request.Name, request.Email, IsActive: true);
        await repository.Save(customer, cancellationToken);
        await publisher.Publish(new CustomerCreated(customer.Id, customer.Email), cancellationToken);
        return customer.Id;
    }
}
