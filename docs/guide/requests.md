# Requests

A request is a message that goes to exactly one handler and produces one response. Use requests for commands (change
state) and queries (read state); Caesar treats both the same way.

## Requests with a response

Implement <xref:Caesar.IRequest%601> and handle it with <xref:Caesar.IRequestHandler%602>:

[!code-csharp[](../snippets/Requests.cs#query)]

`sender.Send(new GetCustomer(id), cancellationToken)` returns `Task<Customer?>`; the response type is inferred from the
request.

## Commands without a response

A command that returns nothing implements <xref:Caesar.IRequest>, which is shorthand for `IRequest<Unit>`. Its handler
implements <xref:Caesar.IRequestHandler%601> and returns a plain `Task`:

[!code-csharp[](../snippets/GettingStarted.cs#command)]

<xref:Caesar.Unit> is the "no value" response. You meet it when a behavior or post-processor sees every request,
including commands: for a command, `TResponse` is `Unit`.

## One handler per request

Each request type resolves exactly one handler. If scanning finds more than one handler for the same request, the first
one found wins. A handler you register yourself **before** calling `AddCaesar` takes precedence over anything scanning
finds, which is useful for replacing a handler in tests.

## Runtime-typed dispatch

When the request type is only known at runtime, for example after deserializing a message from a queue, use
`Send(object)`:

[!code-csharp[](../snippets/Requests.cs#runtime-dispatch)]

It returns the handler's response boxed as `object?`, or `Unit.Value` for a command. Passing an object that implements
neither `IRequest` nor `IRequest<T>` throws `ArgumentException`. The same pattern exists for notifications
(`Publish(object)`) and streams (`CreateStream(object)`).

## Performance

The first `Send` for a request type builds and caches a small wrapper for it. After that, dispatch does no reflection.
