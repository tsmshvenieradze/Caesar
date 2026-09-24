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

## Registration

Processors found by scanning are registered automatically (`cfg.AutoRegisterRequestProcessors = true` by default), and
the built-in pre- and post-processor behaviors are added only when at least one processor exists.

To control processors explicitly, turn auto-registration off and add them yourself:

[!code-csharp[](../snippets/Processors.cs#explicit-processors)]

Adding a processor that scanning also found does not register it twice.
