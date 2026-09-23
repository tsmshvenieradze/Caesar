using System.Runtime.CompilerServices;

namespace Caesar.Sample.Application.Customers;

public sealed record ExportCustomers(int PageSize) : IStreamRequest<IReadOnlyList<Customer>>;

public sealed class ExportCustomersHandler(ICustomerRepository repository) : IStreamRequestHandler<ExportCustomers, IReadOnlyList<Customer>>
{
    public async IAsyncEnumerable<IReadOnlyList<Customer>> Handle(ExportCustomers request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var page = new List<Customer>(request.PageSize);

        await foreach (var customer in repository.All(cancellationToken).WithCancellation(cancellationToken))
        {
            page.Add(customer);
            if (page.Count == request.PageSize)
            {
                yield return page;
                page = new List<Customer>(request.PageSize);
            }
        }

        if (page.Count > 0)
        {
            yield return page;
        }
    }
}
