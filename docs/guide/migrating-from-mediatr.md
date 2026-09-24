# Migrating from MediatR

Caesar follows MediatR's programming model, so most code moves over by changing namespaces and the registration call.

## Steps

1. Replace the packages: `MediatR.Contracts` becomes `Caesar.Abstractions` in the Application layer, and `MediatR`
   becomes `Caesar` in the host.
2. Replace `using MediatR;` with `using Caesar;`. Processors and exception handling types live in `Caesar.Pipeline`,
   and publishers in `Caesar.NotificationPublishers`.
3. Replace `services.AddMediatR(cfg => ...)` with `services.AddCaesar(cfg => ...)`.
4. Check where the mediator is resolved. Caesar registers it as **scoped** by default, while MediatR registers it as
   transient: resolve it inside a scope, or set `cfg.MediatorLifetime = ServiceLifetime.Transient`. See
   [Lifetimes](configuration.md#lifetimes).
5. Build. Open-generic handlers the container cannot close now fail at `AddCaesar` instead of at the first request; see
   [Open-generic handlers](configuration.md#open-generic-handlers).

## Type map

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

## Running both side by side

During a gradual migration a project can reference both packages. The container keeps them apart, since
`MediatR.IMediator` and `Caesar.IMediator` are different types, but a file with both `using MediatR;` and
`using Caesar;` gets `CS0104` ambiguous-reference errors on names such as `IRequest` and `Unit`. Keep each file on one
of them, or use an alias (`using CaesarRequest = Caesar.IRequest;`).

Handlers are not shared: Caesar only scans for its own interfaces, so a handler must be moved to Caesar's interfaces to
be found. Third-party packages built on MediatR's interfaces need a Caesar equivalent.
