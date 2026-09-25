# Streams

A stream request returns a sequence instead of a single response, as an `IAsyncEnumerable<T>`. Use it for exports,
paging through large result sets, or any producer that yields items over time, without buffering everything in memory.

## Define and handle

Implement <xref:Caesar.IStreamRequest%601> and <xref:Caesar.IStreamRequestHandler%602>. Mark the token parameter with
`[EnumeratorCancellation]` so that a token passed to `WithCancellation` by the consumer reaches the handler:

[!code-csharp[](../snippets/Streams.cs#stream-request)]

## Consume

[!code-csharp[](../snippets/Streams.cs#consume-stream)]

`CreateStream` is lazy: it resolves and runs nothing. Stream behaviors and the handler are resolved and started when
enumeration starts, so every failure, a missing handler included, surfaces from the enumerator (`MoveNextAsync`), not
from the `CreateStream` call. Each enumeration builds its own pipeline, so enumerating the same stream twice runs the
handler twice. The token passed to `CreateStream` and the one passed to `WithCancellation` are combined: cancelling
either one stops the stream.

`CreateStream(object)` accepts a request whose type is only known at runtime and yields `object?` items. Like
`Send(object)`, it throws `ArgumentException` straight away for an object that does not declare exactly one
`IStreamRequest<T>`. A stream request can also be created through a base item type, `IStreamRequest<Animal>` for a
request declared as `IStreamRequest<Dog>`: it goes to a handler for `Animal` when one is registered, and to the
handler for `Dog` otherwise. If the call site's item type fits several `IStreamRequest<T>` the request declares, the
typed `CreateStream` throws `ArgumentException` straight away, as `Send` does.

## Stream behaviors

Stream requests do not go through request behaviors; they have their own pipeline of
<xref:Caesar.IStreamPipelineBehavior%602>. See [Pipeline behaviors → Stream behaviors](pipeline-behaviors.md#stream-behaviors).

Pre- and post-processors, exception handlers and exception actions do not run for stream requests either. Handle a
stream's errors in a stream behavior, or where the stream is consumed.
