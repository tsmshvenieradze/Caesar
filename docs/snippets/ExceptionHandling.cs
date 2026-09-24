using Caesar.DependencyInjection;
using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Docs.Snippets;

#region exception-handler
// Recovers from CustomerNotFoundException thrown by the GetCustomer handler with a null result.
public sealed class CustomerNotFoundHandler : IRequestExceptionHandler<GetCustomer, Customer?, CustomerNotFoundException>
{
    public Task Handle(GetCustomer request, CustomerNotFoundException exception,
        RequestExceptionHandlerState<Customer?> state, CancellationToken cancellationToken)
    {
        state.SetHandled(null);
        return Task.CompletedTask;
    }
}
#endregion

#region exception-action
// Runs once for every exception GetCustomer throws. Side effects only: the exception is rethrown afterwards
// with its original stack trace.
public sealed class ReportGetCustomerFailures : IRequestExceptionAction<GetCustomer>
{
    public Task Execute(GetCustomer request, Exception exception, CancellationToken cancellationToken)
    {
        Console.Error.WriteLine($"GetCustomer({request.Id}) failed: {exception.Message}");
        return Task.CompletedTask;
    }
}
#endregion

public static class ExceptionStrategy
{
    public static void Register(IServiceCollection services)
    {
        #region strategy
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CreateCustomer>();

            // Default: actions run only for exceptions no handler recovered from.
            cfg.RequestExceptionActionProcessorStrategy = RequestExceptionActionProcessorStrategy.ApplyForAllExceptions;
        });
        #endregion
    }
}
