# Pre- and post-processors

Processors are a lighter alternative to behaviors when you only need to run code before or after a handler and never
change the result. A pre-processor runs before the handler; a post-processor runs after the handler returns
successfully and receives its response.

## Pre-processors

Implement <xref:Caesar.Pipeline.IRequestPreProcessor%601>. An open generic pre-processor runs for every request:

[!code-csharp[](../snippets/Processors.cs#pre-processor)]

## Post-processors

Implement <xref:Caesar.Pipeline.IRequestPostProcessor%602>. For a command, the response type is <xref:Caesar.Unit>:

[!code-csharp[](../snippets/Processors.cs#post-processor)]

A post-processor does not run when the handler throws. Use an [exception action](exception-handling.md) for that.

Processors run for requests sent with `Send`, not for stream requests.

## Registration

Processors found by scanning are registered automatically (`cfg.AutoRegisterRequestProcessors = true` by default).
MediatR defaults to `false`, so set it to `false` when migrating if some processors were meant to run only when you
registered them yourself.

The built-in pre- and post-processor stages are added to a request type's pipeline only when that request type has at
least one processor: a closed one for it, or an open-generic one, which applies to every request. Processors you
register by hand count too, including ones registered after `AddCaesar`.

To control processors explicitly, turn auto-registration off and add them yourself:

[!code-csharp[](../snippets/Processors.cs#explicit-processors)]

Adding a processor that scanning also found does not register it twice. It is registered once, with the lifetime
you asked for (the scanned `Lifetime` does not override it), and keeps the position scanning gave it, so the order
processors run in does not change.
