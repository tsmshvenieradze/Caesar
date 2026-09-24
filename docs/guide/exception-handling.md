# Exception handling

Caesar has two ways to react to an exception thrown while handling a request:

- An **exception handler** can recover: it supplies a fallback response and the caller never sees the exception.
- An **exception action** runs side effects, such as logging or metrics, and the exception is always rethrown.

## Exception handlers

Implement <xref:Caesar.Pipeline.IRequestExceptionHandler%603> for a request, its response and an exception type. Call
`state.SetHandled(response)` to recover:

[!code-csharp[](../snippets/ExceptionHandling.cs#exception-handler)]

If no handler calls `SetHandled`, the exception propagates. <xref:Caesar.Pipeline.RequestExceptionHandlerState%601>
records whether it was handled and with which response.

## Exception actions

Implement <xref:Caesar.Pipeline.IRequestExceptionAction%602>. Actions cannot recover; they observe:

[!code-csharp[](../snippets/ExceptionHandling.cs#exception-action)]

## Resolution by exception type

Handlers and actions are resolved for the thrown exception type first, then for each of its base types. A handler for
`CustomerNotFoundException` runs before one for `Exception`, so a handler for `Exception` acts as a catch-all.

`IRequestExceptionHandler<TRequest, TResponse>` and `IRequestExceptionAction<TRequest>` are shorthands for the
`Exception` case.

An exception nobody recovers from is rethrown with its original stack trace, so logs point at the line that failed, not
at Caesar.

## When actions run

`cfg.RequestExceptionActionProcessorStrategy` decides where actions sit relative to handlers:

| Value | Actions run for |
| --- | --- |
| `ApplyForUnhandledExceptions` (default) | exceptions no handler recovered from |
| `ApplyForAllExceptions` | every exception, even ones a handler recovers from afterwards |

[!code-csharp[](../snippets/ExceptionHandling.cs#strategy)]

## Registration

Exception handlers and actions are found by scanning, including open generics such as `ReportFailures<,>` above. The
built-in behaviors that run them are added only when at least one handler or action exists.
