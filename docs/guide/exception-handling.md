# Exception handling

Caesar has two ways to react to an exception thrown while handling a request:

- An **exception handler** can recover: it supplies a fallback response and the caller never sees the exception.
- An **exception action** runs side effects, such as logging or metrics, and the exception is always rethrown.

Both apply to requests sent with `Send`. They do not run for stream requests: a stream's pipeline has only
[stream behaviors](pipeline-behaviors.md#stream-behaviors), so handle a stream's errors in an
<xref:Caesar.IStreamPipelineBehavior%602>. An exception handler or action declared for an `IStreamRequest<T>` compiles
and is registered by scanning, but never runs.

## Exception handlers

Implement <xref:Caesar.Pipeline.IRequestExceptionHandler%603> for a request, its response and an exception type. Call
`state.SetHandled(response)` to recover:

[!code-csharp[](../snippets/ExceptionHandling.cs#exception-handler)]

If no handler calls `SetHandled`, the exception propagates. <xref:Caesar.Pipeline.RequestExceptionHandlerState%601>
records whether it was handled and with which response.

A handler that returns a `null` task instead of one fails the request with an `InvalidOperationException` that names
the handler, with the original exception as its `InnerException`.

## Exception actions

Implement <xref:Caesar.Pipeline.IRequestExceptionAction%602>. Actions cannot recover; they observe:

[!code-csharp[](../snippets/ExceptionHandling.cs#exception-action)]

Every matching action runs, even when an earlier one fails. An action that throws, returns a faulted task or returns
`null` does not replace the original exception: once all actions have run, the original is rethrown with its original
stack trace. This differs from MediatR, where the action's exception replaces the original; to translate an exception
into another one, throw it from an exception handler instead. The actions' own failures are attached to the original
as an `IReadOnlyList<Exception>`, in the order the actions ran, after those of a nested `Send` the same exception
passed through. The list keeps the 16 most recent failures, so an exception object that is thrown again and again, for
example a cached initialization failure, does not collect them without bound:

```csharp
catch (Exception e) when (e.Data["Caesar.ExceptionActionFailures"] is IReadOnlyList<Exception> actionFailures)
{
    // e is the original exception; actionFailures are the exceptions the actions threw.
}
```

Handlers and actions are called through typed delegates, so an exception one of them throws surfaces as itself, never
wrapped in a `TargetInvocationException`.

## Resolution by exception type

Handlers and actions are resolved for the thrown exception type first, then for each of its base types. A handler for
`CustomerNotFoundException` runs before one for `Exception`, so a handler for `Exception` acts as a catch-all.

A class that implements the interface for several types in the hierarchy, for example for both
`CustomerNotFoundException` and `Exception`, runs once, for the most specific of them, as in MediatR. Several
registrations of one class for the same exception type, for example two instances configured for different sinks, all
run.

`IRequestExceptionHandler<TRequest, TResponse>` and `IRequestExceptionAction<TRequest>` are shorthands for the
`Exception` case. Implement the shorthand on a closed class, like `ReportGetCustomerFailures` above, when you want an
action that runs exactly once for any failure of a request.

> [!WARNING]
> An open generic over the exception type, such as `ReportFailures<TRequest, TException> : IRequestExceptionAction<TRequest, TException>`,
> is closed by the container for **every** type in the hierarchy, and each closing is a different class. It runs once
> per level: an `ArgumentNullException` triggers it for `ArgumentNullException`, `ArgumentException`,
> `SystemException` and `Exception`, so a logger built this way writes four entries for one failure. A one-parameter
> open generic such as `ReportAll<TRequest> : IRequestExceptionAction<TRequest>` cannot be closed by the container and
> is rejected by `AddCaesar` (see [Open-generic handlers](configuration.md#open-generic-handlers)).

An exception nobody recovers from is rethrown with its original stack trace, so logs point at the line that failed, not
at Caesar.

## What reaches handlers and actions

The exception stages sit outside the processors and your behaviors, so they see every failure thrown while those run:
the handler's, your behaviors' (authorization and validation failures included), and the processors'. A catch-all
handler for `Exception` therefore recovers from all of them; make it check the exception type if some failures must
still reach the caller. The exceptions are behaviors you registered by hand before the first `AddCaesar` call, which
run outside the exception stages (see [Order](pipeline-behaviors.md#order)), and a behavior or processor the container
cannot create, which fails while the pipeline is being built; both still come back through the task `Send` returns.

A request with no handler also fails inside the pipeline, with an `InvalidOperationException` whose message starts
with "No handler was found". It reaches exception handlers and actions like any other failure, so a catch-all handler
turns a missing registration into a fallback response too. The same goes for a handler the container cannot create,
for example because a dependency is missing; that surfaces as the container's own exception.

### Cancellation

By default an `OperationCanceledException` reaches exception handlers and actions like any other exception, as in
MediatR, including one thrown because the caller's token, the one passed to `Send`, was cancelled. A catch-all handler
for `Exception` then turns a client that disconnects into a fallback response. Every handler receives the caller's
token, so it can tell the two apart itself:

```csharp
if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
{
    return Task.CompletedTask; // the caller gave up; let the cancellation propagate
}
```

To make that the rule for the whole container, set `cfg.BypassExceptionHandlingOnCallerCancellation = true`. An
`OperationCanceledException` thrown while the caller's token is cancelled then goes straight to the caller: no
exception handler or action sees it. Cancellation from any other token is still an ordinary failure and reaches
handlers and actions. With the option set, that decides where a timeout belongs:

- To make a timeout behave like the caller giving up, put it on the token you pass to `Send`, for example with
  `CancellationTokenSource.CancelAfter`. Handlers and actions are then skipped.
- To turn a timeout into a fallback response, or to have actions record it, apply it inside the pipeline, for example
  in a behavior that calls `next(timeoutToken)` with a linked token source, and handle `OperationCanceledException`
  (which `TaskCanceledException` derives from) in an exception handler.

## When actions run

`cfg.RequestExceptionActionProcessorStrategy` decides where actions sit relative to handlers:

| Value | Actions run for |
| --- | --- |
| `ApplyForUnhandledExceptions` (default) | exceptions no handler recovered from |
| `ApplyForAllExceptions` | every exception, even ones a handler recovers from afterwards |

[!code-csharp[](../snippets/ExceptionHandling.cs#strategy)]

## Registration

Exception handlers and actions are found by scanning, and you can also register them by hand, before or after
`AddCaesar`. The stage that runs them is added to a request type's pipeline when that request has a closed handler or
action of its own, whatever its exception type, or when an open-generic one exists, which applies to every request.
