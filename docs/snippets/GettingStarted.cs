using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Docs.Snippets;

#region request
public sealed record CreateCustomer(string Name, string Email) : IRequest<Guid>;

public sealed class CreateCustomerHandler(ICustomerRepository repository) : IRequestHandler<CreateCustomer, Guid>
{
    public async Task<Guid> Handle(CreateCustomer request, CancellationToken cancellationToken)
    {
        var customer = new Customer(Guid.NewGuid(), request.Name, request.Email);
        await repository.Save(customer, cancellationToken);
        return customer.Id;
    }
}
#endregion

#region command
public sealed record DeactivateCustomer(Guid Id) : IRequest;

public sealed class DeactivateCustomerHandler(ICustomerRepository repository) : IRequestHandler<DeactivateCustomer>
{
    public Task Handle(DeactivateCustomer request, CancellationToken cancellationToken)
        => repository.Deactivate(request.Id, cancellationToken);
}
#endregion

public static class GettingStartedRegistration
{
    public static IServiceCollection Register(IServiceCollection services)
    {
        #region register
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CreateCustomer>();

            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));    // outermost
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        #endregion

        return services;
    }

    public static IServiceCollection RegisterMinimal(IServiceCollection services)
    {
        #region register-minimal
        services.AddCaesar(cfg => cfg.RegisterServicesFromAssemblyContaining<CreateCustomer>());
        #endregion

        return services;
    }
}

#region send
public sealed class CustomerEndpoints(ISender sender)
{
    public Task<Guid> Create(CreateCustomer command, CancellationToken cancellationToken)
        => sender.Send(command, cancellationToken);

    public Task Deactivate(Guid id, CancellationToken cancellationToken)
        => sender.Send(new DeactivateCustomer(id), cancellationToken);
}
#endregion
