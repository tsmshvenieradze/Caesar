namespace Caesar.Sample.Application.Customers;

public sealed record Customer(Guid Id, string Name, string Email, bool IsActive);

public interface ICustomerRepository
{
    Task<Customer?> Find(Guid id, CancellationToken cancellationToken);

    Task Save(Customer customer, CancellationToken cancellationToken);

    IAsyncEnumerable<Customer> All(CancellationToken cancellationToken);
}
