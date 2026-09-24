# Getting started

This page takes you from an empty solution to a request that is sent, handled and returns a result. It assumes a
layered solution with an Application project and a host (API, worker or console) project, but everything works in a
single project too.

## Install

Reference the contracts from the Application layer and the implementation from the host:

```bash
dotnet add src/MyApp.Application package Caesar.Abstractions
dotnet add src/MyApp.Api package Caesar
```

`Caesar.Abstractions` has no dependencies, so the Application layer stays free of infrastructure. `Caesar` depends only
on `Microsoft.Extensions.DependencyInjection.Abstractions`.

## Define a request and its handler

A request is a message with exactly one handler. Implement <xref:Caesar.IRequest%601> with the response type, and
<xref:Caesar.IRequestHandler%602> to handle it:

[!code-csharp[](../snippets/GettingStarted.cs#request)]

A command that returns nothing implements <xref:Caesar.IRequest> and is handled by <xref:Caesar.IRequestHandler%601>:

[!code-csharp[](../snippets/GettingStarted.cs#command)]

## Register Caesar

Call `AddCaesar` in the composition root and point it at the assemblies that contain your handlers. Scanning finds
handlers, notification handlers, processors and exception handlers; behaviors are added explicitly because their order
matters.

[!code-csharp[](../snippets/GettingStarted.cs#register)]

Every option is described in [Configuration](configuration.md).

## Send

[!code-csharp[](../snippets/GettingStarted.cs#send)]

Inject the narrowest interface a component needs:

| Interface | Use it when the component |
| --- | --- |
| <xref:Caesar.ISender> | only sends requests or creates streams |
| <xref:Caesar.IPublisher> | only publishes notifications |
| <xref:Caesar.IMediator> | does both |

## Resolve inside a scope

The mediator is registered as **scoped** by default, so it is resolved from a request scope, which ASP.NET Core
creates for you. Outside a request, for example in a console app or a test, create the scope yourself:

[!code-csharp[](../snippets/Configuration.cs#scope)]

[Configuration → Lifetimes](configuration.md#lifetimes) explains why, and how to change it.
