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

A command can also be handled by an `IRequestHandler<TCommand, Unit>`. When both are registered,
`IRequestHandler<TCommand>` wins.

## One handler per request

Each request type resolves exactly one handler. If scanning finds more than one closed handler for the same request, the
first one found wins. A handler you register yourself **before** calling `AddCaesar` takes precedence over anything
scanning finds, which is useful for replacing a handler in tests.

Two different open-generic handlers for the same interface cannot both be used, so `AddCaesar` throws when scanning
finds a second one (see [Registration rules](configuration.md#registration-rules)). An open-generic handler whose
generic constraints reject a request is skipped for that request: a command then falls back to its other handler
interface, and otherwise the request fails with "No handler was found".

## Failures

`Send` reports every failure through the task it returns. It throws straight away only for invalid arguments:
`ArgumentNullException` for a `null` request, and `ArgumentException` for a request whose handler cannot be chosen from
its type (see below). A missing handler, an exception from a handler, behavior or processor, and a container error all
fault the task; an `OperationCanceledException` cancels it. So `requests.Select(r => sender.Send(r)).ToList()` always
gets one task per request.

When nothing is registered for a request, the task fails with an `InvalidOperationException` that names the request
and the handler interface it expected, for example `IRequestHandler<GetCustomer, Customer>`. When a handler is
registered but the container cannot create it, because a dependency is missing, a scoped service is resolved from the
root provider, or its constructor throws, the container's own exception comes through unchanged. Both happen inside the
pipeline, so [exception handlers and actions](exception-handling.md#what-reaches-handlers-and-actions) see them.

## Sending through a base response type

<xref:Caesar.IRequest%601> is covariant, so a request declared as `IRequest<Dog>` is also an `IRequest<Animal>`, and it
can be sent through that view:

[!code-csharp[](../snippets/Requests.cs#covariant-send)]

If a handler is registered for the call site's response type, here `IRequestHandler<AdoptDog, Animal>`, that handler
and its behaviors run, as in MediatR. Otherwise the request goes to the handler and behaviors for the response type it
declares, here `IRequestHandler<AdoptDog, Dog>` and `IPipelineBehavior<AdoptDog, Dog>`, and the response is returned as
the type the call site expects. The same applies to streams.

A request type may declare more than one `IRequest<T>`. Each typed `Send` goes to the handler for the response type it
asks for. If a call site's type fits several of them, `Send` throws `ArgumentException`, since the handler to use is
ambiguous.

## Runtime-typed dispatch

When the request type is only known at runtime, for example after deserializing a message from a queue, use
`Send(object)`:

[!code-csharp[](../snippets/Requests.cs#runtime-dispatch)]

It returns the handler's response boxed as `object?`, or `Unit.Value` for a command. It throws `ArgumentException` for
an object that implements neither `IRequest` nor `IRequest<T>`, and for one that declares more than one `IRequest<T>`,
because the object alone does not say which response to use; a type that implements both `IRequest` and
`IRequest<int>` counts as two. Send such a request through a typed overload instead. The same pattern exists for
notifications (`Publish(object)`) and streams (`CreateStream(object)`).

## Performance

The first `Send` for a request type builds and caches a small wrapper for it, and the first `Send` of that type in each
container works out which built-in stages its pipeline needs. After that, dispatch does no reflection, except that the
exception stages reflect once for each request type and exception type pair they see. A request with no behaviors and no
built-in stages calls its handler directly.
