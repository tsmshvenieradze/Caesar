# Caesar

Caesar is a lightweight in-process mediator for .NET 10, built on the same principles as MediatR:
requests go to exactly one handler, notifications go to every handler, and a pipeline of behaviors wraps
each request. It is wired through `Microsoft.Extensions.DependencyInjection` and designed for Clean Architecture
solutions where the Application layer must stay free of infrastructure concerns.

| Package | Reference it from | Contents |
| --- | --- | --- |
| `Caesar.Abstractions` | Application layer | `IRequest`, `INotification`, `IStreamRequest`, handler and behavior interfaces, `ISender` / `IPublisher` / `IMediator`, `Unit`. No dependencies. |
| `Caesar` | API / composition root | `Mediator`, publish strategies, built-in behaviors and `services.AddCaesar(...)`. Depends only on `Microsoft.Extensions.DependencyInjection.Abstractions`. |

## Features

- Request / response (`IRequest<TResponse>`) and commands without a response (`IRequest`).
- Notifications with pluggable publish strategies: sequential, parallel (`Task.WhenAll`), or sequential continue-on-failure.
- Streaming requests (`IStreamRequest<T>`) returning `IAsyncEnumerable<T>` with their own behavior pipeline.
- Pipeline behaviors (open generic or per request), pre-processors, post-processors.
- Exception handlers that can recover with a fallback response, and exception actions for side effects, resolved by exception type hierarchy.
- Runtime-typed dispatch: `Send(object)`, `Publish(object)`, `CreateStream(object)`.
- Assembly scanning with lifetime control, type filtering, open-generic handler support and idempotent registration;
  an open-generic handler the container could never close is reported at `AddCaesar`, not on the first request.
- Cached handler wrappers; no reflection on the hot path after the first call for a request type.

## Installation

```xml
<!-- Application project -->
<PackageReference Include="Caesar.Abstractions" Version="10.0.0" />

<!-- API / Host project -->
<PackageReference Include="Caesar" Version="10.0.0" />
```

## Quick start

### 1. Define a request and its handler (Application layer)

```csharp
using Caesar;

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
```

A command with no result implements `IRequest` and is handled by `IRequestHandler<TRequest>`:

```csharp
public sealed record DeactivateCustomer(Guid Id) : IRequest;

public sealed class DeactivateCustomerHandler : IRequestHandler<DeactivateCustomer>
{
    public Task Handle(DeactivateCustomer request, CancellationToken cancellationToken) => ...;
}
```

### 2. Register in the composition root (API layer)

```csharp
builder.Services.AddCaesar(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<CreateCustomer>();
    cfg.Lifetime = ServiceLifetime.Scoped;                     // scanned handlers; default: Transient
    cfg.NotificationPublisherType = typeof(TaskWhenAllPublisher); // default: ForeachAwaitPublisher

    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));           // outermost
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});
```

### 3. Send

```csharp
public sealed class CustomersController(ISender sender) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateCustomer command, CancellationToken cancellationToken)
        => await sender.Send(command, cancellationToken);
}
```

Inject `ISender` when a component only sends, `IPublisher` when it only publishes, and `IMediator` when it does both.

## Notifications

```csharp
public sealed record CustomerCreated(Guid CustomerId) : INotification;

public sealed class SendWelcomeEmail : INotificationHandler<CustomerCreated> { ... }
public sealed class UpdateCrm        : INotificationHandler<CustomerCreated> { ... }

await publisher.Publish(new CustomerCreated(id), cancellationToken);
```

Publish strategies (`cfg.NotificationPublisherType` or `cfg.NotificationPublisher`):

| Strategy | Behavior |
| --- | --- |
| `ForeachAwaitPublisher` (default) | Handlers run one after another; the first exception stops the publish. |
| `TaskWhenAllPublisher` | Handlers start together; all exceptions are collected into an `AggregateException`. |
| `ForeachAwaitContinueOnFailurePublisher` | Handlers run one after another; every handler runs even if one fails, then failures are thrown together. |

Implement `INotificationPublisher` for anything else (for example fire-and-forget or channel-based dispatch).
`NotificationHandler<T>` is a base class for synchronous handlers.

## Pipeline behaviors

```csharp
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        logger.LogInformation("Handling {Request}", typeof(TRequest).Name);
        var response = await next(cancellationToken);
        logger.LogInformation("Handled {Request}", typeof(TRequest).Name);
        return response;
    }
}
```

- `AddOpenBehavior(typeof(X<,>))` applies to every request; `AddBehavior<X>()` applies to the request types `X` closes over.
- Registration order is execution order: the first behavior added is the outermost.
- Not calling `next` short-circuits the handler (caching, authorization, validation).
- `next()` without a token keeps the token currently flowing; `next(otherToken)` replaces it for the rest of the chain.

Built-in behaviors are registered automatically only when the corresponding handler type exists in the container.
The complete order, outermost first, is:

1. `RequestExceptionActionProcessorBehavior` (or after step 2, see `RequestExceptionActionProcessorStrategy`)
2. `RequestExceptionProcessorBehavior`
3. `RequestPreProcessorBehavior`
4. `RequestPostProcessorBehavior`
5. Behaviors you added with `AddBehavior` / `AddOpenBehavior`
6. The handler

## Pre- and post-processors

```csharp
public sealed class StampCorrelationId<TRequest> : IRequestPreProcessor<TRequest> where TRequest : notnull { ... }

public sealed class AuditDeactivation : IRequestPostProcessor<DeactivateCustomer, Unit> { ... }
```

Processors found by scanning are registered automatically (`cfg.AutoRegisterRequestProcessors = true` by default).
Set it to `false` and use `AddRequestPreProcessor` / `AddOpenRequestPreProcessor` / `AddRequestPostProcessor` / `AddOpenRequestPostProcessor` to control them explicitly.

## Exception handling

```csharp
// Recover from a specific exception with a fallback response.
public sealed class CustomerNotFoundHandler : IRequestExceptionHandler<GetCustomer, Customer?, CustomerNotFoundException>
{
    public Task Handle(GetCustomer request, CustomerNotFoundException exception,
                       RequestExceptionHandlerState<Customer?> state, CancellationToken cancellationToken)
    {
        state.SetHandled(null);
        return Task.CompletedTask;
    }
}

// Side effects only; the exception is always rethrown.
public sealed class LogFailures<TRequest, TException> : IRequestExceptionAction<TRequest, TException>
    where TRequest : notnull where TException : Exception { ... }
```

Handlers and actions are resolved for the thrown exception type first, then its base types, so a handler for
`Exception` acts as a catch-all. `IRequestExceptionHandler<TRequest, TResponse>` and `IRequestExceptionAction<TRequest>`
are shorthands for the `Exception` case. Unhandled exceptions are rethrown with their original stack trace.

`cfg.RequestExceptionActionProcessorStrategy`:

- `ApplyForUnhandledExceptions` (default): actions run only for exceptions no handler recovered from.
- `ApplyForAllExceptions`: actions run for every exception, even ones that are recovered afterwards.

## Streams

```csharp
public sealed record ExportCustomers(int PageSize) : IStreamRequest<IReadOnlyList<Customer>>;

public sealed class ExportCustomersHandler : IStreamRequestHandler<ExportCustomers, IReadOnlyList<Customer>>
{
    public async IAsyncEnumerable<IReadOnlyList<Customer>> Handle(ExportCustomers request,
        [EnumeratorCancellation] CancellationToken cancellationToken) { ... yield return page; ... }
}

await foreach (var page in sender.CreateStream(new ExportCustomers(100), cancellationToken)) { ... }
```

Stream behaviors implement `IStreamPipelineBehavior<TRequest, TResponse>` and are added with `AddStreamBehavior` / `AddOpenStreamBehavior`.

## Configuration reference

| Option | Default | Purpose |
| --- | --- | --- |
| `RegisterServicesFromAssembly*` | required | Assemblies to scan for handlers, processors, exception handlers and actions. |
| `TypeEvaluator` | accept all | Filter scanned types (`t => t.Namespace!.StartsWith("Contoso.Application")`). |
| `Lifetime` | `Transient` | Lifetime for scanned handlers, processors, exception handlers and actions. |
| `MediatorLifetime` | `Scoped` | Lifetime for `IMediator`, `ISender`, `IPublisher` and `INotificationPublisher`. |
| `MediatorImplementationType` | `Mediator` | Subclass `Mediator` and override `PublishCore` to intercept publishes. |
| `NotificationPublisherType` / `NotificationPublisher` | `ForeachAwaitPublisher` | Publish strategy by type or instance. |
| `AutoRegisterRequestProcessors` | `true` | Register scanned pre/post-processors automatically. |
| `RequestExceptionActionProcessorStrategy` | `ApplyForUnhandledExceptions` | Where exception actions sit relative to exception handlers. |
| `AddBehavior` / `AddOpenBehavior` / `AddOpenBehaviors` | | Request behaviors, in execution order. |
| `AddStreamBehavior` / `AddOpenStreamBehavior` | | Stream behaviors. |
| `AddRequestPreProcessor` / `AddOpenRequestPreProcessor` | | Explicit pre-processors. |
| `AddRequestPostProcessor` / `AddOpenRequestPostProcessor` | | Explicit post-processors. |

Registration is idempotent: calling `AddCaesar` twice, or adding a processor that scanning also found, does not duplicate it.
The first scanned handler for a request type wins; explicit registrations made before `AddCaesar` take precedence over scanning.

### Lifetimes

`Lifetime` and `MediatorLifetime` are separate because they answer different questions.

Handlers default to `Transient`: they are stateless, and two `Send` calls in one scope should not share an instance.
The mediator defaults to `Scoped`. A transient `ISender` can be injected into a singleton without the container
objecting, and the captured mediator then holds the **root** provider -- so the first request needing a scoped
dependency such as a `DbContext` fails at runtime, far from the constructor that caused it. With a scoped mediator,
`ValidateOnBuild` reports that singleton at startup instead.

The cost is that `ISender` is no longer resolvable straight from the root provider. Resolve it inside a scope:

```csharp
using var scope = host.Services.CreateScope();
var sender = scope.ServiceProvider.GetRequiredService<ISender>();
```

In a `BackgroundService`, inject `IServiceScopeFactory` and open a scope per unit of work. If you genuinely need a
root-resolvable mediator -- a short console app with no scoped dependencies -- set
`cfg.MediatorLifetime = ServiceLifetime.Transient`.

### Open-generic handlers

An open generic class is registered against the open interface when it implements that interface with its own type
parameters in declaration order, which is the shape the Microsoft container can close at runtime:

```csharp
public sealed class AuditEverything<TNotification> : INotificationHandler<TNotification> where TNotification : INotification { ... }
public sealed class ReportAll<TRequest, TException> : IRequestExceptionAction<TRequest, TException> where TRequest : notnull where TException : Exception { ... }
```

Generic constraints are honoured: the container skips closings that violate them.

An open generic that implements one of those interfaces in any other shape -- a different arity, or type parameters out
of order -- can never be closed by the container. `AddCaesar` throws rather than leave a handler that fails on the first
request that needs it:

```csharp
// throws at AddCaesar: one type parameter, but IRequestHandler<,> takes two
public sealed class QueryHandler<T> : IRequestHandler<Query<T>, T> { ... }
```

Generic types that implement none of Caesar's interfaces are ignored, as before. To keep a deliberately shaped handler
out of the scan, exclude it with `TypeEvaluator`.

## Migrating from MediatR

| MediatR | Caesar |
| --- | --- |
| `using MediatR;` | `using Caesar;` (processors and exception types live in `Caesar.Pipeline`) |
| `services.AddMediatR(cfg => ...)` | `services.AddCaesar(cfg => ...)` |
| `MediatRServiceConfiguration` | `CaesarServiceConfiguration` |
| `IRequest`, `IRequest<T>`, `INotification`, `IStreamRequest<T>` | same |
| `IRequestHandler<,>`, `IRequestHandler<>`, `INotificationHandler<>`, `IStreamRequestHandler<,>` | same |
| `IPipelineBehavior<,>`, `RequestHandlerDelegate<T>` | same; `next` optionally takes a `CancellationToken` |
| `IRequestPreProcessor<>`, `IRequestPostProcessor<,>`, `IRequestExceptionHandler<,,>`, `IRequestExceptionAction<,>` | same (`Caesar.Pipeline`) |
| `ForeachAwaitPublisher`, `TaskWhenAllPublisher` | same (`Caesar.NotificationPublishers`), plus `ForeachAwaitContinueOnFailurePublisher` |
| `Unit`, `ISender`, `IPublisher`, `IMediator` | same |

## Repository layout

```
src/Caesar.Abstractions   contracts (Application layer dependency)
src/Caesar                mediator, DI, publishers, built-in behaviors
tests/Caesar.Tests        xUnit + Moq test suite
tests/Caesar.Tests.Fixtures  handlers shaped so the container cannot close them, for the registration guards
samples/Caesar.Sample     console sample: commands, queries, notifications, streams, behaviors, exception handling
```

```bash
dotnet build Caesar.slnx
dotnet test Caesar.slnx
dotnet run --project samples/Caesar.Sample
dotnet pack Caesar.slnx -c Release -o artifacts/packages
```

## Contributing and releasing

`main` is protected: changes land through pull requests that pass the **Build & Test** check and a code-owner review
(see `.github/CODEOWNERS`).

Releases are produced by the [Release workflow](.github/workflows/release.yml), which builds, tests, packs and pushes
`Caesar` and `Caesar.Abstractions` to nuget.org, then tags the commit `vX.Y.Z` and creates a GitHub release with the
packages attached. There are two ways to trigger it:

- **Manual (recommended).** Actions, Release, *Run workflow*, type the version (for example `10.0.1` or
  `10.1.0-preview.1`) and run it on `main`. Nothing in the repository needs to change. The run fails early if that
  version already exists on nuget.org.
- **On merge.** Every merge to `main` also runs the workflow with `<VersionPrefix>` from `Directory.Build.props`.
  If that version is already published the push is skipped, so ordinary merges are a safe no-op. Bump the value in
  your pull request when you want the merge itself to ship.

The major version tracks the .NET version the library targets (10.x for .NET 10), so a target framework upgrade is a
major bump. nuget.org never accepts the same version twice, so each release must use a higher number.

Publishing authenticates with nuget.org **Trusted Publishing** (GitHub OIDC), so no API key is stored anywhere:

1. On nuget.org: Account, Trusted Publishing, Add. Repository owner `tsmshvenieradze`, repository `Caesar`,
   workflow file `release.yml`, environment `nuget.org`.
2. On GitHub: Settings, Environments, create `nuget.org` and add `NUGET_USER` (variable or secret) set to your nuget.org username.

The workflow requests a short-lived key at run time via `NuGet/login`; nothing expires and nothing needs rotating.

## License

MIT
