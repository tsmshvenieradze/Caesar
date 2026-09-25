# Configuration

Everything is configured in the `AddCaesar` callback, through <xref:Caesar.DependencyInjection.CaesarServiceConfiguration>.

## Options

[!code-csharp[](../snippets/Configuration.cs#all-options)]

| Option | Default | Purpose |
| --- | --- | --- |
| `RegisterServicesFromAssembly*` | required | Assemblies to scan for handlers, processors, exception handlers and actions. |
| `TypeEvaluator` | accept all | Filter scanned types. |
| `Lifetime` | `Transient` | Lifetime for scanned handlers, processors, exception handlers and actions. An explicitly added processor keeps its own lifetime. |
| `MediatorLifetime` | `Scoped` | Lifetime for `IMediator`, `ISender`, `IPublisher` and `INotificationPublisher`. |
| `MediatorImplementationType` | `Mediator` | Subclass `Mediator` and override `PublishCore` to intercept publishes. |
| `NotificationPublisherType` / `NotificationPublisher` | `ForeachAwaitPublisher` | Publish strategy by type or instance. |
| `AutoRegisterRequestProcessors` | `true` | Register scanned pre/post-processors automatically. |
| `RequestExceptionActionProcessorStrategy` | `ApplyForUnhandledExceptions` | Where exception actions sit relative to exception handlers. |
| `BypassExceptionHandlingOnCallerCancellation` | `false` | Let the caller's own cancellation skip exception handlers and actions. See [Cancellation](exception-handling.md#cancellation). |
| `AddBehavior` / `AddOpenBehavior` / `AddOpenBehaviors` | | Request behaviors, in execution order. |
| `AddStreamBehavior` / `AddOpenStreamBehavior` | | Stream behaviors. |
| `AddRequestPreProcessor` / `AddOpenRequestPreProcessor` | | Explicit pre-processors. |
| `AddRequestPostProcessor` / `AddOpenRequestPostProcessor` | | Explicit post-processors. |

`AddCaesar` throws `ArgumentException` when no assembly was given, or when `MediatorImplementationType` or
`NotificationPublisherType` is not a concrete class the container can create as the expected interface: when it is
`null`, an interface, abstract, an open generic, not a class, or does not implement `IMediator` or
`INotificationPublisher`. `NotificationPublisherType` is not checked when a `NotificationPublisher` instance is set,
because the instance is used instead.

## Registration rules

- Registration is idempotent: calling `AddCaesar` twice, or adding a processor that scanning also found, does not
  duplicate it. An explicitly added processor that scanning in the same `AddCaesar` call also finds is registered once,
  with the lifetime you gave it, at the position scanning found it; one an earlier call already registered keeps that
  registration.
- Calling `AddCaesar` more than once, for example once per module, is safe. The built-in stages are worked out for
  each request type when a container first dispatches it, so neither the number nor the order of the calls changes
  them, and processors, exception handlers, exception actions and notification handlers you register after
  `AddCaesar` are seen too.
- Caesar works them out from the service collection of the last `AddCaesar` call and from the container itself, which
  it asks through `IServiceProviderIsService` for every closed service type. Processors and handlers therefore count
  even when the container was built from a copy of that collection, or when they are registered natively in a
  third-party container such as Autofac. Exception handlers and actions, which are looked up per exception type, count
  when the collection has one for the request, or a catch-all one for `Exception` that the container reports; in a
  third-party container, whenever the collection has any exception handler or action at all. Notification handlers for
  a notification's base types must be registered in the collection.
- `MediatorLifetime`, `MediatorImplementationType`, `NotificationPublisher`, `NotificationPublisherType`,
  `RequestExceptionActionProcessorStrategy` and `BypassExceptionHandlingOnCallerCancellation` apply to the whole
  container, so the first `AddCaesar` call decides them. A later call that sets one of them to a different value throws
  `InvalidOperationException` naming the option; leaving it unset, or setting the same value again, is fine. Two
  publishers agree when they are of the same type, whether given as an instance or as a type. Set these options in the
  first call.
- The first scanned closed handler for a request type wins. For open-generic handlers of `IRequestHandler<,>`,
  `IRequestHandler<>` or `IStreamRequestHandler<,>`, the container can only use one, so `AddCaesar` throws
  `InvalidOperationException`, naming both types, when scanning finds a second, different one, in the same call or a
  later one. An open-generic handler you registered by hand takes precedence and is not an error.
- Registrations you make **before** `AddCaesar` take precedence over scanning.
- Nested types are scanned when code in their assembly can name them: a `public`, `internal` or `protected internal`
  handler nested in another class, such as a vertical slice's `internal sealed class Handler`, is registered. `private`
  and `protected` nested types, typically test doubles inside a test class, are not; register them by hand if they are
  meant to run. A non-public nested type that cannot be registered, for example one nested in a generic class, is
  skipped rather than rejected. Compiler-generated types (closures, state machines) are always skipped. Use
  [`TypeEvaluator`](#filtering-the-scan) to keep any other type out.
- `AddCaesar` also adds a few internal services of its own, which you see when enumerating the `IServiceCollection`.
  Do not remove or depend on them.

## Lifetimes

`Lifetime` and `MediatorLifetime` are separate because they answer different questions.

Handlers default to `Transient`: they are stateless, and two `Send` calls in one scope should not share an instance.

The mediator defaults to `Scoped`. A transient `ISender` can be injected into a singleton without the container
objecting, and the captured mediator then holds the **root** provider. With scope validation on, the first request
needing a scoped dependency such as a `DbContext` then fails at runtime, far from the constructor that caused it;
with it off, that `DbContext` is silently resolved from the root and lives for the whole application. With a scoped
mediator, the container reports the capturing singleton at startup instead, provided both `ValidateScopes` and
`ValidateOnBuild` are enabled. ASP.NET Core and the generic host enable both in the Development environment.

The cost is that `ISender` is no longer resolvable straight from the root provider. Resolve it inside a scope:

[!code-csharp[](../snippets/Configuration.cs#scope)]

A `BackgroundService` is a singleton, so inject `IServiceScopeFactory` and open a scope per unit of work:

[!code-csharp[](../snippets/Configuration.cs#background-service)]

If you genuinely need a root-resolvable mediator, for example in a short console app with no scoped dependencies, set
`cfg.MediatorLifetime = ServiceLifetime.Transient`. A mediator resolved from the root resolves handlers from the root
too, and the root provider keeps every disposable transient it creates until it is disposed itself.

> [!WARNING]
> Avoid `ServiceLifetime.Singleton` for the mediator in an application that uses scopes, such as a web application. A
> singleton mediator resolves every handler and its dependencies from the root provider, even when `ISender` is
> injected inside a scope: a scoped `DbContext`, current user or tenant is then shared by every request and every
> user, and disposable transient handlers pile up in the root provider until the application shuts down. With scope
> validation on, the first request that needs a scoped service fails instead. Inject `IServiceScopeFactory` into
> singletons, as shown above.

## Open-generic handlers

An open generic class is registered against the open interface when it implements that interface with its own type
parameters in declaration order, which is the shape the Microsoft container can close at runtime:

[!code-csharp[](../snippets/Configuration.cs#open-generic-handler)]

Generic constraints are honoured. For notification handlers, processors, exception handlers and actions, the container
skips closings that violate them. For request and stream handlers, dispatch skips an open-generic handler whose
constraints reject the request: a command falls back to its other handler interface (`IRequestHandler<T>` or
`IRequestHandler<T, Unit>`), and otherwise the request fails with "No handler was found" rather than with the
container's `ArgumentException`.

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

To keep a type out of the scan, whether a deliberately shaped handler, a nested handler that is not meant to be
registered, or one you register by hand, exclude it with `TypeEvaluator`:

[!code-csharp[](../snippets/Configuration.cs#type-evaluator)]
