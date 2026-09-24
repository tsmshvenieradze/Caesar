# Pipeline behaviors

A pipeline behavior wraps the handler of a request. It runs code before and after the handler, and it can replace the
result or skip the handler entirely. Behaviors are where cross-cutting concerns live: logging, validation,
authorization, caching, transactions, metrics.

## Writing a behavior

Implement <xref:Caesar.IPipelineBehavior%602>. Call `next` to continue down the pipeline:

[!code-csharp[](../snippets/Behaviors.cs#logging-behavior)]

`next` is a <xref:Caesar.RequestHandlerDelegate%601>. `next()` without a token keeps the token currently flowing;
`next(otherToken)` replaces it for the rest of the chain, for example to add a timeout.

## Short-circuiting

Not calling `next` stops the pipeline: the handler never runs. That is how validation, authorization and caching
behaviors work:

[!code-csharp[](../snippets/Behaviors.cs#validation-behavior)]

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

Caesar adds its own behaviors only when the matching handler type exists in the container. The complete order,
outermost first, is:

1. <xref:Caesar.Pipeline.RequestExceptionActionProcessorBehavior%602> (or after step 2, see
   [Exception handling](exception-handling.md#when-actions-run))
2. <xref:Caesar.Pipeline.RequestExceptionProcessorBehavior%602>
3. <xref:Caesar.Pipeline.RequestPreProcessorBehavior%602>
4. <xref:Caesar.Pipeline.RequestPostProcessorBehavior%602>
5. Behaviors you added with `AddBehavior` / `AddOpenBehavior`
6. The handler

So exception handlers see failures from your behaviors, and pre-processors run before your behaviors.

## Stream behaviors

Streaming requests have their own pipeline. Implement <xref:Caesar.IStreamPipelineBehavior%602> and add it with
`AddStreamBehavior` / `AddOpenStreamBehavior`:

[!code-csharp[](../snippets/Behaviors.cs#stream-behavior)]
