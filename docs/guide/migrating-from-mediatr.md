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
5. Check processors. Caesar registers scanned pre- and post-processors automatically
   (`AutoRegisterRequestProcessors` defaults to `true`), while MediatR defaults to `false`. A processor you used to add
   only in some environments now runs for every request it applies to. Set `cfg.AutoRegisterRequestProcessors = false`
   to keep MediatR's behavior.
6. Check exception actions that translate an exception by throwing another one. Caesar rethrows the original and
   records the action's exception in `exception.Data`; translate in an exception handler instead. See
   [Exception actions](exception-handling.md).
7. Build. Open-generic handlers the container cannot close now fail at `AddCaesar` instead of at the first request; see
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

## Behavior differences

| Area | Caesar compared with MediatR |
| --- | --- |
| Mediator lifetime | **Differs.** Scoped by default; MediatR registers it as transient. See [Lifetimes](configuration.md#lifetimes). |
| `AutoRegisterRequestProcessors` | **Differs.** Defaults to `true`; MediatR defaults to `false`. |
| Nested handler types | **Differs.** Nested `public`, `internal` and `protected internal` types are scanned; `private` and `protected` ones, typically test doubles, are not. Register those by hand. |
| Open-generic handlers the container cannot close | **Differs.** `AddCaesar` throws; MediatR fails on the first request that needs one. |
| Two scanned open-generic handlers for one request interface | **Differs.** `AddCaesar` throws; otherwise only one of them could ever run. |
| Handlers for a notification's base classes and interfaces | Supported. Caesar resolves them when publishing, each handler class once, the notification's own handlers first. See [Handlers for base types](notifications.md#handlers-for-base-types). |
| `TaskWhenAllPublisher` with several failures | **Differs.** Caesar throws an `AggregateException` with every failure; awaiting MediatR's rethrows only the first. |
| Exception handler or action class implemented for several exception types | Same. It runs once, for the most specific type. |
| Caller cancellation in exception handlers and actions | Same by default: it reaches them like any exception. `BypassExceptionHandlingOnCallerCancellation` makes it skip them. See [Cancellation](exception-handling.md#cancellation). |
| A failing exception action | **Differs.** Caesar runs the remaining actions and rethrows the original exception, with the actions' failures in `exception.Data["Caesar.ExceptionActionFailures"]`; MediatR rethrows the action's exception. |
| `next(CancellationToken.None)` | `next` optionally takes a token; `default` and `CancellationToken.None` keep the current one. See [Writing a behavior](pipeline-behaviors.md#writing-a-behavior). |
| Failures from `Send` | Reported through the returned task, never thrown synchronously, except for a `null` or undispatchable request. |
| `Send(object)` for a type with several `IRequest<T>` | Throws `ArgumentException` rather than pick one. |
| `CreateStream` | Fully lazy: nothing is resolved until enumeration starts. |

## Running both side by side

During a gradual migration a project can reference both packages. The container keeps them apart, since
`MediatR.IMediator` and `Caesar.IMediator` are different types, but a file with both `using MediatR;` and
`using Caesar;` gets `CS0104` ambiguous-reference errors on names such as `IRequest` and `Unit`. Keep each file on one
of them, or use an alias (`using CaesarRequest = Caesar.IRequest;`).

Handlers are not shared: Caesar only scans for its own interfaces, so a handler must be moved to Caesar's interfaces to
be found. Third-party packages built on MediatR's interfaces need a Caesar equivalent.
