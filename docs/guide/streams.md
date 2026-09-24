# Streams

A stream request returns a sequence instead of a single response, as an `IAsyncEnumerable<T>`. Use it for exports,
paging through large result sets, or any producer that yields items over time, without buffering everything in memory.

## Define and handle

Implement <xref:Caesar.IStreamRequest%601> and <xref:Caesar.IStreamRequestHandler%602>. Mark the token parameter with
`[EnumeratorCancellation]` so that a token passed to `WithCancellation` by the consumer reaches the handler:

[!code-csharp[](../snippets/Streams.cs#stream-request)]

## Consume

[!code-csharp[](../snippets/Streams.cs#consume-stream)]

The handler starts when enumeration starts, not when `CreateStream` is called. `CreateStream(object)` accepts a request
whose type is only known at runtime and yields `object?` items.

## Stream behaviors

Stream requests do not go through request behaviors; they have their own pipeline of
<xref:Caesar.IStreamPipelineBehavior%602>. See [Pipeline behaviors → Stream behaviors](pipeline-behaviors.md#stream-behaviors).
