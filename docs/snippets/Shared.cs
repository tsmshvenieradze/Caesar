namespace Caesar.Docs.Snippets;

public sealed record Customer(Guid Id, string Name, string Email);

public interface ICustomerRepository
{
    Task Save(Customer customer, CancellationToken cancellationToken);

    Task<Customer?> Find(Guid id, CancellationToken cancellationToken);

    Task Deactivate(Guid id, CancellationToken cancellationToken);

    IAsyncEnumerable<Customer> All(CancellationToken cancellationToken);
}

public sealed class CustomerNotFoundException(Guid id) : Exception($"Customer {id} was not found.")
{
    public Guid CustomerId { get; } = id;
}

public sealed class ValidationException(IReadOnlyList<string> errors) : Exception(string.Join("; ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public interface IValidator<in T>
{
    IReadOnlyList<string> Validate(T instance);
}
