---
title: Caesar
---

# Caesar

Caesar is a lightweight in-process mediator for .NET 10. Requests go to exactly one handler, notifications go to every
handler, and a pipeline of behaviors wraps each request. It is wired through `Microsoft.Extensions.DependencyInjection`
and designed for Clean Architecture solutions where the Application layer must stay free of infrastructure concerns.

| Package | Reference it from | Contents |
| --- | --- | --- |
| [`Caesar.Abstractions`](https://www.nuget.org/packages/Caesar.Abstractions) | Application layer | Requests, notifications, streams, handler and behavior interfaces, `ISender` / `IPublisher` / `IMediator`, `Unit`. No dependencies. |
| [`Caesar`](https://www.nuget.org/packages/Caesar) | API / composition root | `Mediator`, publish strategies, built-in behaviors and `services.AddCaesar(...)`. |

## Install

```bash
dotnet add src/MyApp.Application package Caesar.Abstractions
dotnet add src/MyApp.Api package Caesar
```

## In 30 seconds

Define a request and its handler in the Application layer:

[!code-csharp[](snippets/GettingStarted.cs#request)]

Register Caesar in the composition root:

[!code-csharp[](snippets/GettingStarted.cs#register-minimal)]

Send it from anywhere that can inject <xref:Caesar.ISender>:

[!code-csharp[](snippets/GettingStarted.cs#send)]

## Next steps

- [Getting started](guide/getting-started.md) walks through the whole setup.
- The [guides](guide/requests.md) cover notifications, behaviors, exception handling, streams and configuration.
- The [API reference](xref:Caesar) documents every public type.
- Coming from MediatR? See [Migrating from MediatR](guide/migrating-from-mediatr.md).
