using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Caesar.Sample.Application.Customers;

namespace Caesar.Sample.Infrastructure;

public sealed class InMemoryCustomerRepository : ICustomerRepository
{
    private readonly ConcurrentDictionary<Guid, Customer> _store = new();

    public Task<Customer?> Find(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_store.GetValueOrDefault(id));

    public Task Save(Customer customer, CancellationToken cancellationToken)
    {
        _store[customer.Id] = customer;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<Customer> All([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var customer in _store.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return customer;
            await Task.Yield();
        }
    }
}
