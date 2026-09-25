# Notifications

A notification announces that something happened. It goes to every handler registered for it, zero or more, and has no
response. Use notifications for side effects that should not be coupled to the code that caused them: sending an email,
updating a read model, notifying another module.

## Define and handle

Implement <xref:Caesar.INotification> and one <xref:Caesar.INotificationHandler%601> per reaction:

[!code-csharp[](../snippets/Notifications.cs#notification)]

## Publish

Inject <xref:Caesar.IPublisher> (or <xref:Caesar.IMediator>) and publish:

[!code-csharp[](../snippets/Notifications.cs#publish)]

Publishing a notification with no handlers is not an error; it completes immediately. `Publish(object)` accepts a
notification whose type is only known at runtime and throws `ArgumentException` for an object that does not implement
`INotification`.

Apart from those argument checks, `Publish` never throws: every failure comes back through the returned task. That
includes a handler the container cannot create (a throwing constructor, a missing dependency) and a publisher that
throws before returning a task. A handler whose `Handle` returns `null` instead of a task fails with an
`InvalidOperationException` that names it, under every built-in publish strategy.

## Handlers for base types

<xref:Caesar.INotificationHandler%601> is contravariant, and Caesar uses that: a handler registered for a base class of
the notification, for an interface it implements, or for `INotification` itself also runs when the notification is
published.

[!code-csharp[](../snippets/Notifications.cs#base-type-handler)]

Publishing `CustomerDeactivated` runs its own handlers, then `ProjectCustomerEvents`, then `AuditAllNotifications`. The
order is always:

1. Handlers for the notification's own type, in registration order, open-generic handlers closed over it included.
2. Handlers for its base classes, the most derived first.
3. Handlers for its interfaces, a derived interface before the interfaces it extends, so `INotification` comes last.

Some rules keep a handler from running twice:

- A handler class registered for several of these types runs once, for the most specific of them.
- For base classes and interfaces only closed registrations count. An open-generic handler such as
  `AuditEverything<TNotification>` runs once, for the notification's own type. If its constraints reject that type, it
  does not run for a base type they would accept either.

Registering the same class twice for one type is not de-duplicated: it runs once per registration, as it always has for
the notification's own type. Scanning never registers a class twice.

Handlers for base types are found in a container set up with `AddCaesar`, including ones you register by hand after
`AddCaesar`. A `Mediator` built over a container without `AddCaesar` runs only the handlers for the notification's own
type.

## Publish strategies

How the handlers run is decided by the notification publisher. Pick one with `cfg.NotificationPublisherType`, or pass an
instance with `cfg.NotificationPublisher`:

| Strategy | Behavior |
| --- | --- |
| <xref:Caesar.NotificationPublishers.ForeachAwaitPublisher> (default) | Handlers run one after another; the first exception stops the publish. |
| <xref:Caesar.NotificationPublishers.TaskWhenAllPublisher> | Handlers run concurrently; all exceptions are collected. |
| <xref:Caesar.NotificationPublishers.ForeachAwaitContinueOnFailurePublisher> | Handlers run one after another; every handler runs even if one fails, then failures are thrown together. |

Every strategy rethrows a single failure as-is, with its original stack trace. With the last two, two or more failures
are thrown together as an `AggregateException`.

`TaskWhenAllPublisher` starts each handler in turn; a handler runs on the publishing thread until its first `await`,
and the rest of it runs concurrently with the others.

> [!WARNING]
> Handlers are resolved from the scope of the code that publishes, so with `TaskWhenAllPublisher` concurrent handlers
> share its scoped services. Two handlers that use the same Entity Framework Core `DbContext` then fail with "A second
> operation was started on this context". Use `TaskWhenAllPublisher` only for handlers that share no scoped service
> that is not thread-safe, or have each such handler create a scope of its own with `IServiceScopeFactory`.

`ForeachAwaitContinueOnFailurePublisher` stops invoking handlers once the publish token is cancelled, including when it
was cancelled before `Publish` was called. If nothing has failed by then, the publish is canceled; otherwise the
failures collected so far are thrown and the cancellation is dropped. An `OperationCanceledException` caused by the
publish token is never collected as a failure.

## Synchronous handlers

For a handler with no asynchronous work, derive from <xref:Caesar.NotificationHandler%601> and override the `void`
method:

[!code-csharp[](../snippets/Notifications.cs#sync-handler)]

## A custom publisher

Implement <xref:Caesar.INotificationPublisher> to decide how handlers run. Each
<xref:Caesar.NotificationHandlerExecutor> gives you the handler instance and a callback that runs it, in the order
described in [Handlers for base types](#handlers-for-base-types):

[!code-csharp[](../snippets/Notifications.cs#custom-publisher)]

Register it with `cfg.NotificationPublisherType = typeof(TimedPublisher)`. The publisher is resolved from the container,
so it can take dependencies such as a logger.

> [!WARNING]
> The executors hold handler instances resolved from the caller's scope, and the token is the caller's. Run them before
> the task your publisher returns completes. A fire-and-forget publisher, or one that writes executors to a channel,
> runs handlers after the caller's scope may already be disposed: a handler that uses a scoped `DbContext` then throws
> `ObjectDisposedException`, and the caller's token may be cancelled when its request ends. For background processing,
> queue the notification itself, and have the consumer create its own scope with `IServiceScopeFactory` and call
> `IPublisher.Publish` there, with its own token.

## Intercepting publishes

To act on every publish centrally, for example to persist notifications to an outbox, subclass
<xref:Caesar.Mediator> and override `PublishCore`. It receives the same executors a publisher does, handlers for base
types included:

[!code-csharp[](../snippets/Notifications.cs#custom-mediator)]
