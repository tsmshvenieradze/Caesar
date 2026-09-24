# Clean Architecture

Caesar is split into two packages so that the dependency rule of Clean Architecture holds: inner layers never reference
outer ones.

| Project | References | Contains |
| --- | --- | --- |
| Domain | nothing | entities, value objects |
| Application | `Caesar.Abstractions`, Domain | requests, notifications, handlers, behaviors, processors, ports such as `ICustomerRepository` |
| Infrastructure | Application | implementations of the ports: EF Core, HTTP clients, queues |
| Host (API, worker) | `Caesar`, Application, Infrastructure | composition root: `AddCaesar`, DI registrations, endpoints |

`Caesar.Abstractions` contains only interfaces and <xref:Caesar.Unit>, with no dependencies, so referencing it from the
Application layer adds no infrastructure concerns. Only the host references `Caesar`, which contains the
<xref:Caesar.Mediator> and the DI extensions.

## The composition root

[!code-csharp[](../snippets/CleanArchitecture.cs#composition-root)]

Point `RegisterServicesFromAssemblyContaining` at a type in the Application assembly. Add behaviors here too, so the
host decides the order of cross-cutting concerns.

## Where things go

- **Handlers** live next to their request in the Application layer, one feature per folder.
- **Behaviors** for cross-cutting concerns are Application code when they only depend on Application ports (validation,
  authorization), and Infrastructure code when they depend on infrastructure (transactions around a `DbContext`).
- **Endpoints** in the host only translate HTTP to a request and call <xref:Caesar.ISender>.

## Sample

The repository's [`samples/Caesar.Sample`](https://github.com/tsmshvenieradze/Caesar/tree/main/samples/Caesar.Sample)
is a runnable console app laid out this way: commands, queries, notifications, streams, behaviors and exception
handling.
