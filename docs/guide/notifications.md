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

## Publish strategies

How the handlers run is decided by the notification publisher. Pick one with `cfg.NotificationPublisherType`, or pass an
instance with `cfg.NotificationPublisher`:

| Strategy | Behavior |
| --- | --- |
| <xref:Caesar.NotificationPublishers.ForeachAwaitPublisher> (default) | Handlers run one after another; the first exception stops the publish. |
| <xref:Caesar.NotificationPublishers.TaskWhenAllPublisher> | Handlers start together; all exceptions are collected into an `AggregateException`. |
| <xref:Caesar.NotificationPublishers.ForeachAwaitContinueOnFailurePublisher> | Handlers run one after another; every handler runs even if one fails, then failures are thrown together. |

With the last two, a single failure is rethrown as-is; two or more are rethrown as an `AggregateException`.
`ForeachAwaitContinueOnFailurePublisher` does not collect an `OperationCanceledException` caused by the publish token:
cancellation stops the publish immediately.

## Synchronous handlers

For a handler with no asynchronous work, derive from <xref:Caesar.NotificationHandler%601> and override the `void`
method:

[!code-csharp[](../snippets/Notifications.cs#sync-handler)]

## A custom publisher

Implement <xref:Caesar.INotificationPublisher> for anything else: fire-and-forget, a channel, a transactional outbox.
Each <xref:Caesar.NotificationHandlerExecutor> gives you the handler instance and a callback that runs it:

[!code-csharp[](../snippets/Notifications.cs#custom-publisher)]

Register it with `cfg.NotificationPublisherType = typeof(TimedPublisher)`. The publisher is resolved from the container,
so it can take dependencies such as a logger.

## Intercepting publishes

To act on every publish centrally, for example to persist notifications to an outbox, subclass
<xref:Caesar.Mediator> and override `PublishCore`:

[!code-csharp[](../snippets/Notifications.cs#custom-mediator)]
