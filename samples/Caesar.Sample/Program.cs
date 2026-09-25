using Caesar;
using Caesar.Sample.Application.Behaviors;
using Caesar.Sample.Application.Customers;
using Caesar.Sample.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Services.AddSingleton<ICustomerRepository, InMemoryCustomerRepository>();
builder.Services.AddTransient<IValidator<CreateCustomer>, CreateCustomerValidator>();

// Composition root: only the host references the Caesar package. The Application layer references Caesar.Abstractions.
builder.Services.AddCaesar(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<CreateCustomer>();
    cfg.Lifetime = ServiceLifetime.Scoped;

    // Outermost first. Pre/post processors and exception handlers found by scanning are wired automatically.
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    cfg.AddOpenStreamBehavior(typeof(StreamLoggingBehavior<,>));
});

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

// 1. Command with a response.
var id = await mediator.Send(new CreateCustomer("Nino", "nino@example.ge"));
Console.WriteLine($"Created customer {id}");

// 2. Query.
var customer = await mediator.Send(new GetCustomer(id));
Console.WriteLine($"Loaded: {customer?.Name} <{customer?.Email}>");

// 3. Command without a response (IRequest / IRequestHandler<T>).
await mediator.Send(new DeactivateCustomer(id));

// 4. Validation failure short-circuited by a behavior.
try
{
    await mediator.Send(new CreateCustomer("", "not-an-email"));
}
catch (ValidationException e)
{
    Console.WriteLine($"Validation failed: {string.Join("; ", e.Errors)}");
}

// 5. Exception recovered by an IRequestExceptionHandler.
var missing = await mediator.Send(new GetCustomer(Guid.Empty));
Console.WriteLine($"Missing customer resolved to: {(missing is null ? "null" : missing.Name)}");

// 6. Streaming.
await foreach (var page in mediator.CreateStream(new ExportCustomers(PageSize: 1)))
{
    Console.WriteLine($"Exported page with {page.Count} customer(s)");
}

Console.WriteLine("Done.");
