# Configuration

Everything is configured in the `AddCaesar` callback, through <xref:Caesar.DependencyInjection.CaesarServiceConfiguration>.

## Options

[!code-csharp[](../snippets/Configuration.cs#all-options)]

| Option | Default | Purpose |
| --- | --- | --- |
| `RegisterServicesFromAssembly*` | required | Assemblies to scan for handlers, processors, exception handlers and actions. |
| `TypeEvaluator` | accept all | Filter scanned types. |
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

`AddCaesar` throws `ArgumentException` when no assembly was given, or when `MediatorImplementationType` or
`NotificationPublisherType` does not implement the expected interface.

## Registration rules

- Registration is idempotent: calling `AddCaesar` twice, or adding a processor that scanning also found, does not
  duplicate it.
- The first scanned handler for a request type wins.
- Registrations you make **before** `AddCaesar` take precedence over scanning.

## Lifetimes

`Lifetime` and `MediatorLifetime` are separate because they answer different questions.

Handlers default to `Transient`: they are stateless, and two `Send` calls in one scope should not share an instance.

The mediator defaults to `Scoped`. A transient `ISender` can be injected into a singleton without the container
objecting, and the captured mediator then holds the **root** provider, so the first request needing a scoped
dependency such as a `DbContext` fails at runtime, far from the constructor that caused it. With a scoped mediator,
`ValidateOnBuild` reports that singleton at startup instead.

The cost is that `ISender` is no longer resolvable straight from the root provider. Resolve it inside a scope:

[!code-csharp[](../snippets/Configuration.cs#scope)]

A `BackgroundService` is a singleton, so inject `IServiceScopeFactory` and open a scope per unit of work:

[!code-csharp[](../snippets/Configuration.cs#background-service)]

If you genuinely need a root-resolvable mediator, for example in a short console app with no scoped dependencies, set
`cfg.MediatorLifetime = ServiceLifetime.Transient`.

## Open-generic handlers

An open generic class is registered against the open interface when it implements that interface with its own type
parameters in declaration order, which is the shape the Microsoft container can close at runtime:

[!code-csharp[](../snippets/Configuration.cs#open-generic-handler)]

Generic constraints are honoured: the container skips closings that violate them.

An open generic that implements one of Caesar's interfaces in any other shape, with a different arity or with type
parameters out of order, can never be closed by the container. `AddCaesar` throws `InvalidOperationException` rather
than leave a handler that fails on the first request that needs it. This example is intentionally not a working
registration:

```csharp
// Throws at AddCaesar: one type parameter, but IRequestHandler<,> takes two.
public sealed class QueryHandler<T> : IRequestHandler<Query<T>, T> { ... }
```

Generic types that implement none of Caesar's interfaces are ignored.

## Filtering the scan

To keep a type out of the scan, whether a deliberately shaped handler or one you register by hand, exclude it with
`TypeEvaluator`:

[!code-csharp[](../snippets/Configuration.cs#type-evaluator)]
