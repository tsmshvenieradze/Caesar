using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Docs.Snippets;

#region pre-processor
// Runs before the handler for every request type.
public sealed class TraceRequests<TRequest> : IRequestPreProcessor<TRequest>
    where TRequest : notnull
{
    public Task Process(TRequest request, CancellationToken cancellationToken)
    {
        Console.WriteLine($"-> {typeof(TRequest).Name}");
        return Task.CompletedTask;
    }
}
#endregion

#region post-processor
// Runs after the DeactivateCustomer handler succeeds. Commands respond with Unit.
public sealed class AuditDeactivation : IRequestPostProcessor<DeactivateCustomer, Unit>
{
    public Task Process(DeactivateCustomer request, Unit response, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Customer {request.Id} deactivated");
        return Task.CompletedTask;
    }
}
#endregion

public static class ProcessorRegistration
{
    public static void Register(IServiceCollection services)
    {
        #region explicit-processors
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CreateCustomer>();
            cfg.AutoRegisterRequestProcessors = false;

            cfg.AddOpenRequestPreProcessor(typeof(TraceRequests<>));
            cfg.AddRequestPostProcessor<AuditDeactivation>();
        });
        #endregion
    }
}
