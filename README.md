# Caesar

Caesar is a lightweight in-process mediator for .NET 10, built on the same principles as MediatR:
requests go to exactly one handler, notifications go to every handler, and a pipeline of behaviors wraps
each request. It is wired through `Microsoft.Extensions.DependencyInjection` and designed for Clean Architecture
solutions where the Application layer must stay free of infrastructure concerns.

**Documentation: [caesar.tsezar.io](https://caesar.tsezar.io)**

| Package | Reference it from | Contents |
| --- | --- | --- |
| `Caesar.Abstractions` | Application layer | `IRequest`, `INotification`, `IStreamRequest`, handler and behavior interfaces, `ISender` / `IPublisher` / `IMediator`, `Unit`. No dependencies. |
| `Caesar` | API / composition root | `Mediator`, publish strategies, built-in behaviors and `services.AddCaesar(...)`. Depends only on `Microsoft.Extensions.DependencyInjection.Abstractions`. |

## Features

- Request / response (`IRequest<TResponse>`) and commands without a response (`IRequest`).
- Notifications with pluggable publish strategies: sequential, parallel (`Task.WhenAll`), or sequential continue-on-failure.
- Streaming requests (`IStreamRequest<T>`) returning `IAsyncEnumerable<T>` with their own behavior pipeline.
- Pipeline behaviors (open generic or per request), pre-processors, post-processors.
- Exception handlers that can recover with a fallback response, and exception actions for side effects, resolved by exception type hierarchy.
- Runtime-typed dispatch: `Send(object)`, `Publish(object)`, `CreateStream(object)`.
- Assembly scanning with lifetime control, type filtering, open-generic handler support and idempotent registration;
  an open-generic handler the container could never close is reported at `AddCaesar`, not on the first request.
- Cached handler wrappers; no reflection on the hot path after the first call for a request type.

## Installation

```xml
<!-- Application project -->
<PackageReference Include="Caesar.Abstractions" Version="10.1.1" />

<!-- API / Host project -->
<PackageReference Include="Caesar" Version="10.1.1" />
```

## Quick start

Define a request and its handler in the Application layer:

```csharp
using Caesar;

public sealed record CreateCustomer(string Name, string Email) : IRequest<Guid>;

public sealed class CreateCustomerHandler(ICustomerRepository repository) : IRequestHandler<CreateCustomer, Guid>
{
    public async Task<Guid> Handle(CreateCustomer request, CancellationToken cancellationToken)
    {
        var customer = new Customer(Guid.NewGuid(), request.Name, request.Email);
        await repository.Save(customer, cancellationToken);
        return customer.Id;
    }
}
```

Register Caesar in the composition root:

```csharp
builder.Services.AddCaesar(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<CreateCustomer>();
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
});
```

Send it from anything that can inject `ISender`:

```csharp
var id = await sender.Send(new CreateCustomer("Nino", "nino@example.ge"), cancellationToken);
```

More in the [guides](https://caesar.tsezar.io/latest/guide/getting-started.html): notifications, pipeline behaviors,
exception handling, streams, configuration and migrating from MediatR.

## Repository layout

```
src/Caesar.Abstractions      contracts (Application layer dependency)
src/Caesar                   mediator, DI, publishers, built-in behaviors
tests/Caesar.Tests           xUnit + Moq test suite
tests/Caesar.Tests.Fixtures  handlers shaped so the container cannot close them, for the registration guards
samples/Caesar.Sample        console sample: commands, queries, notifications, streams, behaviors, exception handling
docs/                        DocFX site: guides, theme and configuration
docs/snippets                compiled examples used by the guides
```

```bash
dotnet build Caesar.slnx
dotnet test Caesar.slnx
dotnet run --project samples/Caesar.Sample
dotnet pack Caesar.slnx -c Release -o artifacts/packages
```

## Contributing and releasing

Changes land through pull requests that pass the **Build & Test** and **Docs** checks.

Docs: `dotnet tool restore`, then `dotnet docfx docs/docfx.json --serve` to preview at `http://localhost:8080`.

Releases are produced by the [Release workflow](.github/workflows/release.yml), which builds, tests, packs and pushes
`Caesar` and `Caesar.Abstractions` to nuget.org, then tags the commit `vX.Y.Z` and creates a GitHub release with the
packages attached. There are two ways to trigger it:

- **Manual (recommended).** Actions, Release, *Run workflow*, type the version (for example `10.0.1` or
  `10.1.0-preview.1`) and run it on `main`. Nothing in the repository needs to change. The run fails early if that
  version already exists on nuget.org.
- **On merge.** Every merge to `main` also runs the workflow with `<VersionPrefix>` from `Directory.Build.props`.
  If that version is already published the push is skipped, so ordinary merges are a safe no-op. Bump the value in
  your pull request when you want the merge itself to ship.

Before releasing a stable version, add a `## [X.Y.Z] - YYYY-MM-DD` section to [CHANGELOG.md](CHANGELOG.md). The
Release workflow refuses a stable version without one, uses it as the GitHub Release notes, and publishes that
version's docs to `caesar.tsezar.io/X.Y/`. Documentation-only fixes can be republished without a release from
Actions, *Docs*, *Run workflow*.

The major version tracks the .NET version the library targets (10.x for .NET 10), so a target framework upgrade is a
major bump. nuget.org never accepts the same version twice, so each release must use a higher number.

Publishing authenticates with nuget.org **Trusted Publishing** (GitHub OIDC), so no API key is stored anywhere:

1. On nuget.org: Account, Trusted Publishing, Add. Policy owner: the account that owns the packages
   (`tsmshvenieradze`); a policy owned by another account gets a key that is refused with 403. Repository owner
   `tsmshvenieradze`, repository `Caesar`, workflow file `release.yml`, environment `nuget.org`. The policy is bound to
   the repository's ID, so a deleted and recreated repository needs a new policy.
2. On GitHub: Settings, Environments, create `nuget.org` and add `NUGET_USER` (variable or secret) set to your nuget.org username.

The workflow requests a short-lived key at run time via `NuGet/login`; nothing expires and nothing needs rotating.

## License

MIT
