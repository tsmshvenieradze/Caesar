using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Caesar.Docs.Snippets;

public static class CompositionRoot
{
    public static IHost Build(string[] args)
    {
        #region composition-root
        var builder = Host.CreateApplicationBuilder(args);

        // Infrastructure implementations of Application-layer ports.
        builder.Services.AddScoped<ICustomerRepository, SqlCustomerRepository>();

        // Only the host references the Caesar package; the Application project references Caesar.Abstractions.
        builder.Services.AddCaesar(cfg => cfg.RegisterServicesFromAssemblyContaining<CreateCustomer>());

        return builder.Build();
        #endregion
    }
}

public sealed class SqlCustomerRepository : ICustomerRepository
{
    public Task Save(Customer customer, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<Customer?> Find(Guid id, CancellationToken cancellationToken) => Task.FromResult<Customer?>(null);

    public Task Deactivate(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

    public async IAsyncEnumerable<Customer> All([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }
}
