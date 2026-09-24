using System.Runtime.CompilerServices;

namespace Caesar.Docs.Snippets;

#region stream-request
public sealed record ExportCustomers(int PageSize) : IStreamRequest<IReadOnlyList<Customer>>;

public sealed class ExportCustomersHandler(ICustomerRepository repository)
    : IStreamRequestHandler<ExportCustomers, IReadOnlyList<Customer>>
{
    public async IAsyncEnumerable<IReadOnlyList<Customer>> Handle(ExportCustomers request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var page = new List<Customer>(request.PageSize);
        await foreach (var customer in repository.All(cancellationToken))
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
#endregion

public static class ConsumeStream
{
    public static async Task Run(ISender sender, CancellationToken cancellationToken)
    {
        #region consume-stream
        await foreach (var page in sender.CreateStream(new ExportCustomers(PageSize: 100), cancellationToken))
        {
            Console.WriteLine($"Exported {page.Count} customer(s)");
        }
        #endregion
    }
}
