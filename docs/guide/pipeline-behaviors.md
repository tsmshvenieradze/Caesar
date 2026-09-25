# Pipeline behaviors

A pipeline behavior wraps the handler of a request. It runs code before and after the handler, and it can replace the
result or skip the handler entirely. Behaviors are where cross-cutting concerns live: logging, validation,
authorization, caching, transactions, metrics.

## Writing a behavior

Implement <xref:Caesar.IPipelineBehavior%602>. Call `next` to continue down the pipeline:

[!code-csharp[](../snippets/Behaviors.cs#logging-behavior)]

`next` is a <xref:Caesar.RequestHandlerDelegate%601>. `next()` without a token keeps the token currently flowing;
`next(otherToken)` replaces it for the rest of the chain, for example to add a timeout.

`CancellationToken.None` is equal to `default`, so `next(default)` and `next(CancellationToken.None)` are the same call
as `next()`: they keep the flowing token rather than detach from it. To let the rest of the chain run to completion
when the caller cancels, for example a commit that has already started, pass a token that can never be cancelled, the
token of a `CancellationTokenSource` that is never cancelled:

[!code-csharp[](../snippets/Behaviors.cs#detach-token)]

## Short-circuiting

Not calling `next` stops the pipeline: the handler never runs. That is how validation, authorization and caching
behaviors work:

[!code-csharp[](../snippets/Behaviors.cs#validation-behavior)]

`Send` reports a behavior's exception through the task it returns, even when the behavior throws before returning a
task. Writing the behavior as an `async` method, or returning `Task.FromException`, still keeps it well-behaved when
something other than `Send` calls it, such as a unit test.

## Open and closed behaviors

An **open** behavior is a generic type definition that applies to every request, added with `AddOpenBehavior`. A
**closed** behavior implements `IPipelineBehavior<TRequest, TResponse>` for specific types and applies only to those,
added with `AddBehavior`:

[!code-csharp[](../snippets/Behaviors.cs#closed-behavior)]

An open behavior must implement the interface with its own type parameters in the same order,
`class X<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>`; `AddOpenBehavior` throws otherwise.

## Order

Registration order is execution order: the first behavior added is the outermost.

[!code-csharp[](../snippets/Behaviors.cs#register-behaviors)]

Caesar adds its own stages to a request type's pipeline only when that request type has something for them to run: an
exception action, an exception handler, a pre-processor or a post-processor that applies to it, whether found by
scanning or registered by hand, before or after `AddCaesar`. The complete order, outermost first, is the same however
many times `AddCaesar` is called:

1. Behaviors you registered by hand in the service collection **before** the first `AddCaesar` call, in registration
   order
2. <xref:Caesar.Pipeline.RequestExceptionActionProcessorBehavior%602> (or after step 3, see
   [Exception handling](exception-handling.md#when-actions-run))
3. <xref:Caesar.Pipeline.RequestExceptionProcessorBehavior%602>
4. <xref:Caesar.Pipeline.RequestPreProcessorBehavior%602>
5. <xref:Caesar.Pipeline.RequestPostProcessorBehavior%602>
6. Behaviors you added with `AddBehavior` / `AddOpenBehavior`, and any registered after the first `AddCaesar` call
7. The handler

So exception handlers see failures from your behaviors, and pre-processors run before your behaviors. A behavior
registered before `AddCaesar` stays outside all of them, as it did when Caesar appended its own behaviors to the
collection: exception handlers do not see its failures, and it runs before the pre-processors.

The built-in stages are not registered as `IPipelineBehavior<,>`, so resolving
`IEnumerable<IPipelineBehavior<TRequest, TResponse>>` returns only your behaviors, and removing a built-in behavior's
descriptor from the collection is neither possible nor needed: a stage only runs when something is registered for it,
so remove the processors, exception handlers or actions instead. The stages are composed by Caesar's own `Mediator` in
a container set up with `AddCaesar`; a custom `IMediator` that does not derive from `Mediator` does not get them. If
you register one of the built-in behavior classes yourself by type (`AddBehavior`, `AddOpenBehavior` or an
implementation-type descriptor), Caesar leaves its own stage out and the behavior runs once, at the position you gave
it. A built-in behavior registered through a factory is not recognised and would run twice.

## Stream behaviors

Streaming requests have their own pipeline. Implement <xref:Caesar.IStreamPipelineBehavior%602> and add it with
`AddStreamBehavior` / `AddOpenStreamBehavior`. Request behaviors, processors, exception handlers and exception actions
do not run for streams:

[!code-csharp[](../snippets/Behaviors.cs#stream-behavior)]
