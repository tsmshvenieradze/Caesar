# Caesar Documentation Site Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish versioned Caesar documentation (guides + generated API reference + changelog) at `https://caesar.tsezar.io`, built with DocFX and served by GitHub Pages from a `gh-pages` branch.

**Architecture:** `docs/` holds DocFX config, Markdown guides, a compiled snippets project and a brand template. API metadata comes from the Release DLLs + XML docs. A Python script (`.github/scripts/publish_version.py`) lays a build into `gh-pages` as `X.Y/` (+ `latest/`, `versions.json`, root files); `docs.yml` runs it, called by `release.yml` after a stable release or dispatched by hand.

**Tech Stack:** DocFX 2.80.1 (local dotnet tool, `modern` template), .NET 10 SDK, Python 3 stdlib (scripts + `unittest`), GitHub Actions, GitHub Pages, Cloudflare DNS.

**Spec:** `docs/superpowers/specs/2026-09-24-caesar-docs-site-design.md`

## Global Constraints

- Domain: `caesar.tsezar.io`; repo `tsmshvenieradze/Caesar`; Pages source: branch `gh-pages`, root.
- Authorship: `© Tsezari Mshvenieradze · tsezar.io`; no GPIH/GPI Holding anywhere.
- English only.
- Brand: dark bg `#0A0A0F`, text `#FFFFFF`, muted `#7A7A85`, accent `#00E5FF`; light-mode accent `#00838F`; fonts Space Grotesk (text) and JetBrains Mono (code).
- One docs folder per minor version `X.Y`; prerelease versions (containing `-`) never publish docs.
- `docfx` always runs with `--warningsAsErrors`.
- No long-lived secrets; workflows use `GITHUB_TOKEN` only.
- Repo code style: `TreatWarningsAsErrors`, `AnalysisLevel latest-recommended`, file-scoped namespaces, LF line endings, 4-space C# / 2-space json/yml.
- Local machine has an unreachable global `GPIH` NuGet source; the repo `nuget.config` (Task 1) clears it.

## Review Focus

1. A patch to an older minor released after a newer minor exists (10.1.2 after 10.2.0) must refresh `/10.1/` only and leave `/latest/` on 10.2 — pinned by `test_older_minor_does_not_replace_latest` (Task 6).
2. Minor ordering is numeric, so 10.10 is newer than 10.9 — pinned by `test_numeric_ordering` (Task 6).
3. A prerelease release (`10.2.0-preview.1`) must not publish docs or touch `latest/`, and must not be blocked by the changelog guard — pinned by `test_prerelease_rejected` (Task 6) and the `docs` job condition plus guard `case` in Task 8.
4. Re-running a deploy with unchanged content must produce no `gh-pages` commit and leave other versions untouched — pinned by `test_republish_is_idempotent` and `test_republish_replaces_only_its_folder` (Task 6).
5. The version dropdown on a page missing from the target version, or with no `versions.json` (local `--serve`), must fall back to the version home or hide itself — pinned by the manual checks in Task 7 Step 4 (no JS test harness in this repo).

---

### Task 1: Build hygiene and DocFX skeleton

**Files:**
- Create: `nuget.config`
- Create: `.config/dotnet-tools.json`
- Create: `docs/docfx.json`, `docs/index.md`, `docs/toc.yml`
- Modify: `.gitignore`

**Interfaces:**
- Produces: `dotnet docfx docs/docfx.json --warningsAsErrors` builds `docs/_site/` with API pages under `docs/_site/api/`. Later tasks add content entries to `docs/docfx.json` `build.content[0].files`.

- [ ] **Step 1: Reproduce the local restore failure**

Run: `dotnet restore Caesar.slnx`
Expected: FAIL with `NU1507 ... sources are defined: nuget.org, GPIH`.

- [ ] **Step 2: Add `nuget.config`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

- [ ] **Step 3: Verify restore, build and tests pass**

Run: `dotnet build Caesar.slnx -c Release && dotnet test Caesar.slnx -c Release --no-build`
Expected: build succeeds, `Passed: 77`.

- [ ] **Step 4: Add the DocFX local tool**

Run: `dotnet new tool-manifest && dotnet tool install docfx --version 2.80.1`
Expected: `.config/dotnet-tools.json` lists `docfx` `2.80.1`.

- [ ] **Step 5: Add `docs/docfx.json`**

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/docfx/main/schemas/docfx.schema.json",
  "metadata": [
    {
      "src": [
        {
          "src": "../src",
          "files": [
            "Caesar.Abstractions/bin/Release/net10.0/Caesar.Abstractions.dll",
            "Caesar/bin/Release/net10.0/Caesar.dll"
          ]
        }
      ],
      "dest": "api"
    }
  ],
  "build": {
    "content": [
      {
        "files": ["index.md", "toc.yml", "api/**.yml"]
      }
    ],
    "resource": [
      {
        "files": ["images/**"]
      }
    ],
    "xref": ["https://learn.microsoft.com/en-us/dotnet/.xrefmap.json"],
    "template": ["default", "modern"],
    "output": "_site",
    "globalMetadata": {
      "_appTitle": "Caesar",
      "_appName": "Caesar",
      "_enableSearch": true
    }
  }
}
```

- [ ] **Step 6: Add stub `docs/index.md` and `docs/toc.yml`**

`docs/index.md`:

```markdown
# Caesar

A lightweight in-process mediator for .NET.
```

`docs/toc.yml`:

```yaml
- name: API
  href: api/
```

- [ ] **Step 7: Ignore generated output**

Append to `.gitignore`:

```
docs/_site/
docs/api/
```

- [ ] **Step 8: Build the docs strictly**

Run: `dotnet tool restore && dotnet docfx docs/docfx.json --warningsAsErrors`
Expected: `Build succeeded. 0 warning(s) 0 error(s)`; `docs/_site/api/Caesar.ISender.html` exists.
If the `xref` map download fails offline, retry online; it is required for BCL links.

- [ ] **Step 9: Commit**

```bash
git add nuget.config .config/dotnet-tools.json docs/docfx.json docs/index.md docs/toc.yml .gitignore
git commit -m "Add DocFX skeleton and pin package sources to nuget.org"
```

---

### Task 2: Compiled snippets project

**Files:**
- Create: `docs/snippets/Caesar.Docs.Snippets.csproj`
- Create: `docs/snippets/Shared.cs`, `GettingStarted.cs`, `Requests.cs`, `Notifications.cs`, `Behaviors.cs`, `Processors.cs`, `ExceptionHandling.cs`, `Streams.cs`, `Configuration.cs`, `CleanArchitecture.cs`
- Modify: `Caesar.slnx`

**Interfaces:**
- Produces: `#region` names used by Task 3 guides (listed per file below). All types live in `namespace Caesar.Docs.Snippets;`.

- [ ] **Step 1: Add the project**

`docs/snippets/Caesar.Docs.Snippets.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- Example code shown in the documentation. It is compiled in CI so a breaking API change fails the build,
       but it is never packed or run. Rules suppressed here favour readable examples over library conventions. -->
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CA1303;CA1515;CA1812;CA1822;CA1873;CA2007;CA1031;CA1062</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/Caesar/Caesar.csproj" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
  </ItemGroup>

</Project>
```

Add to `Caesar.slnx` inside `<Solution>`:

```xml
  <Folder Name="/docs/">
    <Project Path="docs/snippets/Caesar.Docs.Snippets.csproj" />
  </Folder>
```

- [ ] **Step 2: `Shared.cs` (domain types used by several guides, no regions)**

```csharp
namespace Caesar.Docs.Snippets;

public sealed record Customer(Guid Id, string Name, string Email);

public interface ICustomerRepository
{
    Task Save(Customer customer, CancellationToken cancellationToken);

    Task<Customer?> Find(Guid id, CancellationToken cancellationToken);

    Task Deactivate(Guid id, CancellationToken cancellationToken);

    IAsyncEnumerable<Customer> All(CancellationToken cancellationToken);
}

public sealed class CustomerNotFoundException(Guid id) : Exception($"Customer {id} was not found.")
{
    public Guid CustomerId { get; } = id;
}

public sealed class ValidationException(IReadOnlyList<string> errors) : Exception(string.Join("; ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public interface IValidator<in T>
{
    IReadOnlyList<string> Validate(T instance);
}
```

- [ ] **Step 3: `GettingStarted.cs`** — regions `request`, `command`, `register`, `send`

```csharp
using Caesar.NotificationPublishers;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Docs.Snippets;

#region request
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
#endregion

#region command
public sealed record DeactivateCustomer(Guid Id) : IRequest;

public sealed class DeactivateCustomerHandler(ICustomerRepository repository) : IRequestHandler<DeactivateCustomer>
{
    public Task Handle(DeactivateCustomer request, CancellationToken cancellationToken)
        => repository.Deactivate(request.Id, cancellationToken);
}
#endregion

public static class GettingStartedRegistration
{
    public static IServiceCollection Register(IServiceCollection services)
    {
        #region register
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CreateCustomer>();
            cfg.NotificationPublisherType = typeof(TaskWhenAllPublisher);

            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));    // outermost
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        #endregion

        return services;
    }
}

#region send
public sealed class CustomerEndpoints(ISender sender)
{
    public Task<Guid> Create(CreateCustomer command, CancellationToken cancellationToken)
        => sender.Send(command, cancellationToken);

    public Task Deactivate(Guid id, CancellationToken cancellationToken)
        => sender.Send(new DeactivateCustomer(id), cancellationToken);
}
#endregion
```

- [ ] **Step 4: `Requests.cs`** — regions `query`, `runtime-dispatch`

```csharp
namespace Caesar.Docs.Snippets;

#region query
public sealed record GetCustomer(Guid Id) : IRequest<Customer?>;

public sealed class GetCustomerHandler(ICustomerRepository repository) : IRequestHandler<GetCustomer, Customer?>
{
    public Task<Customer?> Handle(GetCustomer request, CancellationToken cancellationToken)
        => repository.Find(request.Id, cancellationToken);
}
#endregion

public static class RuntimeDispatch
{
    #region runtime-dispatch
    // The request type is only known at runtime, e.g. deserialized from a message queue.
    public static async Task<object?> Dispatch(ISender sender, object message, CancellationToken cancellationToken)
    {
        // Returns the handler's response, or Unit.Value for a command that implements IRequest.
        return await sender.Send(message, cancellationToken);
    }
    #endregion
}
```

- [ ] **Step 5: `Notifications.cs`** — regions `notification`, `publish`, `sync-handler`, `custom-publisher`, `custom-mediator`

```csharp
using Microsoft.Extensions.Logging;

namespace Caesar.Docs.Snippets;

#region notification
public sealed record CustomerCreated(Guid CustomerId) : INotification;

public sealed class SendWelcomeEmail : INotificationHandler<CustomerCreated>
{
    public Task Handle(CustomerCreated notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class UpdateCrm : INotificationHandler<CustomerCreated>
{
    public Task Handle(CustomerCreated notification, CancellationToken cancellationToken) => Task.CompletedTask;
}
#endregion

public static class PublishExample
{
    public static async Task Run(IPublisher publisher, Guid id, CancellationToken cancellationToken)
    {
        #region publish
        await publisher.Publish(new CustomerCreated(id), cancellationToken);
        #endregion
    }
}

#region sync-handler
public sealed class CountCustomers : NotificationHandler<CustomerCreated>
{
    public int Count { get; private set; }

    protected override void Handle(CustomerCreated notification) => Count++;
}
#endregion

#region custom-publisher
// Runs handlers one after another and logs how long each one took.
public sealed partial class TimedPublisher(ILogger<TimedPublisher> logger) : INotificationPublisher
{
    public async Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        foreach (var executor in handlerExecutors)
        {
            var started = TimeProvider.System.GetTimestamp();
            await executor.HandlerCallback(notification, cancellationToken);
            LogHandled(executor.HandlerInstance.GetType().Name, TimeProvider.System.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Handler} took {Elapsed} ms")]
    private partial void LogHandled(string handler, double elapsed);
}
#endregion

#region custom-mediator
// Register with cfg.MediatorImplementationType = typeof(OutboxMediator).
public sealed class OutboxMediator(IServiceProvider serviceProvider, INotificationPublisher publisher)
    : Mediator(serviceProvider, publisher)
{
    protected override Task PublishCore(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        // Inspect, reorder or persist the notification here, then publish as usual.
        return base.PublishCore(handlerExecutors, notification, cancellationToken);
    }
}
#endregion
```

- [ ] **Step 6: `Behaviors.cs`** — regions `logging-behavior`, `validation-behavior`, `closed-behavior`, `register-behaviors`, `stream-behavior`

```csharp
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Caesar.Docs.Snippets;

#region logging-behavior
public sealed partial class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        LogHandling(typeof(TRequest).Name);
        var response = await next();
        LogHandled(typeof(TRequest).Name);
        return response;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {Request}")]
    private partial void LogHandling(string request);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {Request}")]
    private partial void LogHandled(string request);
}
#endregion

#region validation-behavior
// Not calling next() short-circuits the pipeline: the handler never runs.
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var errors = validators.SelectMany(v => v.Validate(request)).ToList();
        return errors.Count == 0 ? next() : throw new ValidationException(errors);
    }
}
#endregion

#region closed-behavior
// Applies only to CreateCustomer. Register with cfg.AddBehavior<NormalizeEmail>().
public sealed class NormalizeEmail : IPipelineBehavior<CreateCustomer, Guid>
{
    public Task<Guid> Handle(CreateCustomer request, RequestHandlerDelegate<Guid> next, CancellationToken cancellationToken)
        => request.Email == request.Email.Trim() ? next() : throw new ValidationException(["Email has surrounding whitespace."]);
}
#endregion

#region stream-behavior
public sealed class CountItemsBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var count = 0;
        await foreach (var item in next().WithCancellation(cancellationToken))
        {
            count++;
            yield return item;
        }

        Console.WriteLine($"{typeof(TRequest).Name} streamed {count} item(s)");
    }
}
#endregion

public static class BehaviorRegistration
{
    public static void Register(IServiceCollection services)
    {
        #region register-behaviors
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CreateCustomer>();

            // Registration order is execution order: the first behavior is the outermost.
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddBehavior<NormalizeEmail>();

            cfg.AddOpenStreamBehavior(typeof(CountItemsBehavior<,>));
        });
        #endregion
    }
}
```

- [ ] **Step 7: `Processors.cs`** — regions `pre-processor`, `post-processor`, `explicit-processors`

```csharp
using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Docs.Snippets;

#region pre-processor
// Runs before the handler for every request type.
public sealed class TraceRequests<TRequest> : IRequestPreProcessor<TRequest>
    where TRequest : notnull
{
    public Task Process(TRequest request, CancellationToken cancellationToken)
    {
        Console.WriteLine($"-> {typeof(TRequest).Name}");
        return Task.CompletedTask;
    }
}
#endregion

#region post-processor
// Runs after the DeactivateCustomer handler succeeds. Commands respond with Unit.
public sealed class AuditDeactivation : IRequestPostProcessor<DeactivateCustomer, Unit>
{
    public Task Process(DeactivateCustomer request, Unit response, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Customer {request.Id} deactivated");
        return Task.CompletedTask;
    }
}
#endregion

public static class ProcessorRegistration
{
    public static void Register(IServiceCollection services)
    {
        #region explicit-processors
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CreateCustomer>();
            cfg.AutoRegisterRequestProcessors = false;

            cfg.AddOpenRequestPreProcessor(typeof(TraceRequests<>));
            cfg.AddRequestPostProcessor<AuditDeactivation>();
        });
        #endregion
    }
}
```

- [ ] **Step 8: `ExceptionHandling.cs`** — regions `exception-handler`, `exception-action`, `strategy`

```csharp
using Caesar.DependencyInjection;
using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Docs.Snippets;

#region exception-handler
// Recovers from CustomerNotFoundException thrown by the GetCustomer handler with a null result.
public sealed class CustomerNotFoundHandler : IRequestExceptionHandler<GetCustomer, Customer?, CustomerNotFoundException>
{
    public Task Handle(GetCustomer request, CustomerNotFoundException exception,
        RequestExceptionHandlerState<Customer?> state, CancellationToken cancellationToken)
    {
        state.SetHandled(null);
        return Task.CompletedTask;
    }
}
#endregion

#region exception-action
// Side effects only: the exception is rethrown afterwards with its original stack trace.
public sealed class ReportFailures<TRequest, TException> : IRequestExceptionAction<TRequest, TException>
    where TRequest : notnull
    where TException : Exception
{
    public Task Execute(TRequest request, TException exception, CancellationToken cancellationToken)
    {
        Console.Error.WriteLine($"{typeof(TRequest).Name} failed: {exception.Message}");
        return Task.CompletedTask;
    }
}
#endregion

public static class ExceptionStrategy
{
    public static void Register(IServiceCollection services)
    {
        #region strategy
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CreateCustomer>();

            // Default: actions run only for exceptions no handler recovered from.
            cfg.RequestExceptionActionProcessorStrategy = RequestExceptionActionProcessorStrategy.ApplyForAllExceptions;
        });
        #endregion
    }
}
```

- [ ] **Step 9: `Streams.cs`** — regions `stream-request`, `consume-stream`

```csharp
using System.Runtime.CompilerServices;

namespace Caesar.Docs.Snippets;

#region stream-request
public sealed record ExportCustomers(int PageSize) : IStreamRequest<IReadOnlyList<Customer>>;

public sealed class ExportCustomersHandler(ICustomerRepository repository)
    : IStreamRequestHandler<ExportCustomers, IReadOnlyList<Customer>>
{
    public async IAsyncEnumerable<IReadOnlyList<Customer>> Handle(ExportCustomers request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var page = new List<Customer>(request.PageSize);
        await foreach (var customer in repository.All(cancellationToken))
        {
            page.Add(customer);
            if (page.Count == request.PageSize)
            {
                yield return page;
                page = new List<Customer>(request.PageSize);
            }
        }

        if (page.Count > 0)
        {
            yield return page;
        }
    }
}
#endregion

public static class ConsumeStream
{
    public static async Task Run(ISender sender, CancellationToken cancellationToken)
    {
        #region consume-stream
        await foreach (var page in sender.CreateStream(new ExportCustomers(PageSize: 100), cancellationToken))
        {
            Console.WriteLine($"Exported {page.Count} customer(s)");
        }
        #endregion
    }
}
```

- [ ] **Step 10: `Configuration.cs`** — regions `all-options`, `scope`, `background-service`, `open-generic-handler`, `type-evaluator`

```csharp
using Caesar.DependencyInjection;
using Caesar.NotificationPublishers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Caesar.Docs.Snippets;

public static class ConfigurationExamples
{
    public static void AllOptions(IServiceCollection services)
    {
        #region all-options
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CreateCustomer>();       // required, at least one assembly
            cfg.TypeEvaluator = type => type.Namespace?.StartsWith("Contoso.Application", StringComparison.Ordinal) == true;

            cfg.Lifetime = ServiceLifetime.Transient;                            // scanned handlers, processors, exception handlers
            cfg.MediatorLifetime = ServiceLifetime.Scoped;                       // IMediator, ISender, IPublisher, INotificationPublisher
            cfg.MediatorImplementationType = typeof(Mediator);
            cfg.NotificationPublisherType = typeof(ForeachAwaitPublisher);
            cfg.AutoRegisterRequestProcessors = true;
            cfg.RequestExceptionActionProcessorStrategy = RequestExceptionActionProcessorStrategy.ApplyForUnhandledExceptions;
        });
        #endregion
    }

    public static async Task Scope(IHost host)
    {
        #region scope
        using var scope = host.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        await sender.Send(new DeactivateCustomer(Guid.NewGuid()));
        #endregion
    }

    public static void TypeEvaluator(IServiceCollection services)
    {
        #region type-evaluator
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CreateCustomer>();
            cfg.TypeEvaluator = type => type != typeof(CountCustomers);   // keep this handler out of the scan
        });
        #endregion
    }
}

#region background-service
// A BackgroundService is a singleton: open a scope per unit of work instead of injecting ISender.
public sealed class NightlyExport(IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                await foreach (var page in sender.CreateStream(new ExportCustomers(500), stoppingToken))
                {
                    Console.WriteLine($"Exported {page.Count}");
                }
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
#endregion

#region open-generic-handler
// Registered against INotificationHandler<> and closed by the container for every notification type.
public sealed class AuditEverything<TNotification> : INotificationHandler<TNotification>
    where TNotification : INotification
{
    public Task Handle(TNotification notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Published {typeof(TNotification).Name}");
        return Task.CompletedTask;
    }
}
#endregion
```

- [ ] **Step 11: `CleanArchitecture.cs`** — region `composition-root`

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Caesar.Docs.Snippets;

public static class CompositionRoot
{
    public static IHost Build(string[] args)
    {
        #region composition-root
        var builder = Host.CreateApplicationBuilder(args);

        // Infrastructure implementations of Application-layer ports.
        builder.Services.AddScoped<ICustomerRepository, SqlCustomerRepository>();

        // Only the host references the Caesar package; the Application project references Caesar.Abstractions.
        builder.Services.AddCaesar(cfg => cfg.RegisterServicesFromAssemblyContaining<CreateCustomer>());

        return builder.Build();
        #endregion
    }
}

public sealed class SqlCustomerRepository : ICustomerRepository
{
    public Task Save(Customer customer, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<Customer?> Find(Guid id, CancellationToken cancellationToken) => Task.FromResult<Customer?>(null);

    public Task Deactivate(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

    public async IAsyncEnumerable<Customer> All([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }
}
```

- [ ] **Step 12: Build and fix analyzer findings**

Run: `dotnet build Caesar.slnx -c Release`
Expected: success. If an analyzer rule fires that is inherent to example code (not a real bug), add it to the csproj `NoWarn` list; otherwise fix the code. Never suppress `IDE0005` (unused usings) — remove the using.

- [ ] **Step 13: Verify tests and packing are unaffected**

Run: `dotnet test Caesar.slnx -c Release --no-build && dotnet pack Caesar.slnx -c Release --no-build -o "$TEMP/pack-check" && ls "$TEMP/pack-check"`
Expected: `Passed: 77`; only `Caesar.*` and `Caesar.Abstractions.*` packages (no `Caesar.Docs.Snippets`). Delete `$TEMP/pack-check`.

- [ ] **Step 14: Commit**

```bash
git add docs/snippets Caesar.slnx
git commit -m "Add compiled documentation snippets project"
```

---

### Task 3: Guides and landing page

**Files:**
- Modify: `docs/index.md`, `docs/toc.yml`, `docs/docfx.json` (content files)
- Create: `docs/guide/toc.yml` and the ten guide pages below

**Interfaces:**
- Consumes: snippet regions from Task 2, referenced as `[!code-csharp[](../snippets/<File>.cs#<region>)]` (from `docs/index.md` use `snippets/<File>.cs#<region>`).
- Produces: `docs/guide/*.md` pages; `docs/toc.yml` top nav.

- [ ] **Step 1: Wire content into `docs/docfx.json`**

Replace `build.content[0].files` with:

```json
["index.md", "toc.yml", "guide/**.md", "guide/toc.yml", "api/**.yml"]
```

- [ ] **Step 2: Top nav `docs/toc.yml`**

```yaml
- name: Guide
  href: guide/
- name: API
  href: api/
- name: NuGet
  href: https://www.nuget.org/packages/Caesar
```

- [ ] **Step 3: `docs/guide/toc.yml`**

```yaml
- name: Getting started
  href: getting-started.md
- name: Requests
  href: requests.md
- name: Notifications
  href: notifications.md
- name: Pipeline behaviors
  href: pipeline-behaviors.md
- name: Pre- and post-processors
  href: processors.md
- name: Exception handling
  href: exception-handling.md
- name: Streams
  href: streams.md
- name: Configuration
  href: configuration.md
- name: Clean Architecture
  href: clean-architecture.md
- name: Migrating from MediatR
  href: migrating-from-mediatr.md
```

- [ ] **Step 4: Write the pages**

Prose comes from the matching README sections (README.md at the start of this branch), rewritten as standalone pages: each page opens with one paragraph saying what the feature is for, uses the listed snippet regions for every code block, and links API types with `<xref:...>` (uid = full type name, e.g. `<xref:Caesar.ISender>`, generic `` <xref:Caesar.IRequestHandler`2> ``). No hand-typed C# except one-line fragments inside prose.

| Page | Must cover | Snippet regions |
| --- | --- | --- |
| `index.md` | pitch (README intro), the two-package table, install `dotnet add package Caesar.Abstractions` / `Caesar`, 30-second example, links to Getting started and API | `GettingStarted.cs#request`, `#register`, `#send` |
| `getting-started.md` | install per layer; define request+handler; command without response; register; send; which of `ISender`/`IPublisher`/`IMediator` to inject; resolve inside a scope | `GettingStarted.cs#request`, `#command`, `#register`, `#send`; `Configuration.cs#scope` |
| `requests.md` | `IRequest<T>` vs `IRequest`, `Unit`, one handler per request (first scanned wins, explicit registration before `AddCaesar` wins), `Send(object)` and the `ArgumentException` for non-requests | `Requests.cs#query`, `#runtime-dispatch`; `GettingStarted.cs#command` |
| `notifications.md` | zero-to-many handlers; publish; strategy table (3 publishers, exception semantics exactly as README); `NotificationPublisher` instance vs type; `NotificationHandler<T>`; custom publisher; `Mediator.PublishCore` override; `Publish(object)` | `Notifications.cs#notification`, `#publish`, `#sync-handler`, `#custom-publisher`, `#custom-mediator` |
| `pipeline-behaviors.md` | open vs closed behaviors; order = registration order; short-circuit; `next()` vs `next(token)`; built-in behavior order list (6 items, as README); stream behaviors | `Behaviors.cs#logging-behavior`, `#validation-behavior`, `#closed-behavior`, `#register-behaviors`, `#stream-behavior` |
| `processors.md` | pre vs post; auto-registration flag; explicit `Add*Processor` methods; commands use `Unit` | `Processors.cs#pre-processor`, `#post-processor`, `#explicit-processors` |
| `exception-handling.md` | handlers recover via `state.SetHandled`; actions are side effects; resolution by exception type then base types; `Exception` catch-all and the 2-arity shorthands; original stack trace; strategy option | `ExceptionHandling.cs#exception-handler`, `#exception-action`, `#strategy` |
| `streams.md` | `IStreamRequest<T>`, `IAsyncEnumerable<T>`, `[EnumeratorCancellation]`, consuming, `CreateStream(object)`, stream behaviors link | `Streams.cs#stream-request`, `#consume-stream` |
| `configuration.md` | full option table (README), idempotent registration, Lifetimes section (README text), BackgroundService pattern, open-generic rules incl. the `AddCaesar` exception for unclosable shapes, `TypeEvaluator` exclusion | `Configuration.cs#all-options`, `#scope`, `#background-service`, `#open-generic-handler`, `#type-evaluator` |
| `clean-architecture.md` | which project references which package; handlers/behaviors in Application; composition root in the host; link to `samples/Caesar.Sample` on GitHub | `CleanArchitecture.cs#composition-root` |
| `migrating-from-mediatr.md` | README migration table; steps: swap packages, `using MediatR;`→`using Caesar;` (+ `Caesar.Pipeline`), `AddMediatR`→`AddCaesar`, `MediatorLifetime` default difference (Scoped); ambiguous-reference note (CS0104) when both are referenced | none |

For the unclosable open-generic example in `configuration.md`, use a fenced block marked as "does not compile into a working registration" — it is intentionally not in the snippets project because `AddCaesar` would throw on scan.

- [ ] **Step 5: Build strictly**

Run: `dotnet build Caesar.slnx -c Release && dotnet docfx docs/docfx.json --warningsAsErrors`
Expected: 0 warnings. An `InvalidFileLink`, `UidNotFound` or code-snippet warning means a wrong path, region or xref — fix it.

- [ ] **Step 6: Commit**

```bash
git add docs/index.md docs/toc.yml docs/docfx.json docs/guide
git commit -m "Write documentation guides and landing page"
```

---

### Task 4: Changelog

**Files:**
- Create: `CHANGELOG.md`, `docs/changelog.md`
- Modify: `docs/docfx.json`, `docs/toc.yml`

**Interfaces:**
- Produces: `CHANGELOG.md` with `## [X.Y.Z] - YYYY-MM-DD` headings, consumed by `changelog_section.py` (Task 6) and `release.yml` (Task 8).

- [ ] **Step 1: `CHANGELOG.md`**

```markdown
# Changelog

All notable changes to Caesar are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/), with the major version tracking the targeted .NET version.

## [Unreleased]

## [10.1.1] - 2026-09-23

### Added

- `CaesarServiceConfiguration.MediatorLifetime` (default `Scoped`) controls the lifetime of `IMediator`, `ISender`,
  `IPublisher` and `INotificationPublisher`. A singleton that captures `ISender` is now reported by
  `ValidateOnBuild` at startup instead of failing on the first request that needs a scoped dependency.
- `AddCaesar` throws when a scanned open-generic type implements a Caesar interface in a shape the container can
  never close (different arity or reordered type parameters), instead of failing on the first request.

### Changed

- `Lifetime` now applies only to scanned handlers, processors, exception handlers and actions.
- `ISender` / `IMediator` are no longer resolvable from the root provider by default; resolve them inside a scope,
  or set `MediatorLifetime = ServiceLifetime.Transient`.
- Package authorship metadata now names Tsezari Mshvenieradze.

[Unreleased]: https://github.com/tsmshvenieradze/Caesar/compare/v10.1.1...HEAD
[10.1.1]: https://github.com/tsmshvenieradze/Caesar/releases/tag/v10.1.1
```

- [ ] **Step 2: `docs/changelog.md`**

```markdown
---
title: Changelog
---

[!INCLUDE [changelog](../CHANGELOG.md)]
```

- [ ] **Step 3: Wire it in**

Add `"changelog.md"` to `build.content[0].files` in `docs/docfx.json`. In `docs/toc.yml` insert after the API entry:

```yaml
- name: Changelog
  href: changelog.md
```

- [ ] **Step 4: Build strictly and check the page**

Run: `dotnet docfx docs/docfx.json --warningsAsErrors && grep -c "MediatorLifetime" docs/_site/changelog.html`
Expected: 0 warnings; count ≥ 1.

- [ ] **Step 5: Commit**

```bash
git add CHANGELOG.md docs/changelog.md docs/docfx.json docs/toc.yml
git commit -m "Add changelog and publish it on the docs site"
```

---

### Task 5: Brand theme

**Files:**
- Create: `docs/template/public/main.css`, `docs/template/public/main.js`, `docs/images/logo.svg`
- Modify: `docs/docfx.json` (`template`, `globalMetadata`)

**Interfaces:**
- Consumes: `/versions.json` (array of `{ "version": "X.Y", "latest": bool }`, newest first) produced by Task 6 on the deployed site.
- Produces: `select.caesar-version` element inserted after `a.navbar-brand`.

- [ ] **Step 1: `docs/images/logo.svg`**

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32"><rect width="32" height="32" rx="6" fill="#0A0A0F"/><text x="16" y="22" text-anchor="middle" font-family="Space Grotesk, sans-serif" font-size="20" font-weight="700" fill="#00E5FF">C</text></svg>
```

- [ ] **Step 2: `docs/template/public/main.css`**

```css
@import url("https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;600&family=Space+Grotesk:wght@400;600;700&display=swap");

:root {
  --bs-font-sans-serif: "Space Grotesk", system-ui, -apple-system, "Segoe UI", sans-serif;
  --bs-font-monospace: "JetBrains Mono", ui-monospace, SFMono-Regular, Consolas, monospace;
  --bs-link-color: #00838F;
  --bs-link-color-rgb: 0, 131, 143;
  --bs-link-hover-color: #006064;
  --bs-link-hover-color-rgb: 0, 96, 100;
}

[data-bs-theme="dark"] {
  --bs-body-bg: #0A0A0F;
  --bs-body-bg-rgb: 10, 10, 15;
  --bs-body-color: #FFFFFF;
  --bs-body-color-rgb: 255, 255, 255;
  --bs-emphasis-color: #FFFFFF;
  --bs-secondary-color: #7A7A85;
  --bs-secondary-bg: #16161F;
  --bs-tertiary-bg: #12121A;
  --bs-border-color: #24242E;
  --bs-link-color: #00E5FF;
  --bs-link-color-rgb: 0, 229, 255;
  --bs-link-hover-color: #7DF3FF;
  --bs-link-hover-color-rgb: 125, 243, 255;
  --bs-code-color: #7DF3FF;
}

.navbar {
  border-bottom: 1px solid var(--bs-border-color);
}

.navbar-brand img {
  border-radius: 6px;
}

.caesar-version {
  width: auto;
  margin-inline: 0.5rem 1rem;
  font-family: var(--bs-font-monospace);
}
```

- [ ] **Step 3: `docs/template/public/main.js`**

```js
// Version picker for the versioned site layout: /<X.Y>/... and /latest/..., listed in /versions.json.
// Hidden when the page is not served from that layout (e.g. `docfx --serve`) or versions.json is unavailable.

function versionSegment(pathname) {
  const first = pathname.split('/').filter(Boolean)[0]
  return first === 'latest' || /^\d+\.\d+$/.test(first ?? '') ? first : null
}

async function switchVersion(target, current) {
  const rest = location.pathname.slice(`/${current}/`.length)
  const candidate = `/${target}/${rest}`
  try {
    const response = await fetch(candidate, { method: 'HEAD' })
    location.href = response.ok ? candidate : `/${target}/`
  } catch {
    location.href = `/${target}/`
  }
}

async function addVersionPicker() {
  const current = versionSegment(location.pathname)
  const brand = document.querySelector('a.navbar-brand')
  if (!current || !brand) {
    return
  }

  let versions
  try {
    const response = await fetch('/versions.json', { cache: 'no-cache' })
    if (!response.ok) {
      return
    }
    versions = await response.json()
  } catch {
    return
  }
  if (!Array.isArray(versions) || versions.length === 0) {
    return
  }

  const latest = versions.find(v => v.latest)?.version
  const selected = current === 'latest' ? latest : current
  const select = document.createElement('select')
  select.className = 'form-select form-select-sm caesar-version'
  select.setAttribute('aria-label', 'Documentation version')
  for (const { version } of versions) {
    select.add(new Option(version === latest ? `${version} (latest)` : version, version, false, version === selected))
  }
  select.addEventListener('change', () => switchVersion(select.value === latest ? 'latest' : select.value, current))
  brand.after(select)
}

export default {
  defaultTheme: 'dark',
  iconLinks: [
    { icon: 'github', href: 'https://github.com/tsmshvenieradze/Caesar', title: 'GitHub' },
  ],
  start: () => {
    addVersionPicker()
  },
}
```

- [ ] **Step 4: Register template and metadata in `docs/docfx.json`**

Set `"template": ["default", "modern", "template"]` and replace `globalMetadata` with:

```json
{
  "_appTitle": "Caesar",
  "_appName": "Caesar",
  "_appLogoPath": "images/logo.svg",
  "_appFaviconPath": "images/logo.svg",
  "_appFooter": "© Tsezari Mshvenieradze · <a href=\"https://tsezar.io\">tsezar.io</a>",
  "_enableSearch": true,
  "_gitContribute": {
    "repo": "https://github.com/tsmshvenieradze/Caesar",
    "branch": "main"
  }
}
```

- [ ] **Step 5: Build and preview**

Run: `dotnet docfx docs/docfx.json --warningsAsErrors --serve` and open `http://localhost:8080`.
Expected: dark theme by default with `#0A0A0F` background and cyan links; Space Grotesk text, JetBrains Mono code; "C" logo; footer text; theme toggle switches to light with teal links; no version picker (not a versioned path); layout usable at 375px width (browser devtools). Stop the server.

- [ ] **Step 6: Commit**

```bash
git add docs/template docs/images docs/docfx.json
git commit -m "Apply tsezar.io brand theme and version picker to the docs"
```

---

### Task 6: Publish and changelog scripts (TDD)

**Files:**
- Create: `.github/scripts/publish_version.py`, `.github/scripts/test_publish_version.py`
- Create: `.github/scripts/changelog_section.py`, `.github/scripts/test_changelog_section.py`

**Interfaces:**
- Produces:
  - `publish_version.minor_of(version: str) -> str` — `"10.1.1"`/`"10.1"` → `"10.1"`; raises `ValueError` for prerelease or malformed input.
  - `publish_version.publish(site: Path, pages: Path, version: str, cname: str = "caesar.tsezar.io") -> str` — returns the minor.
  - CLI: `python3 .github/scripts/publish_version.py --site DIR --pages DIR --version V` prints the minor.
  - `changelog_section.extract(text: str, version: str) -> str | None`.
  - CLI: `python3 .github/scripts/changelog_section.py CHANGELOG.md VERSION` prints the section body; exit 1 if missing/empty.

- [ ] **Step 1: Write failing tests `test_publish_version.py`**

```python
import json
import tempfile
import unittest
from pathlib import Path

from publish_version import minor_of, publish


def make_site(root: Path, marker: str) -> Path:
    site = root / f"site-{marker}"
    (site / "guide").mkdir(parents=True)
    (site / "index.html").write_text(f"home {marker}", encoding="utf-8")
    (site / "guide" / "page.html").write_text(f"page {marker}", encoding="utf-8")
    return site


def snapshot(root: Path) -> dict[str, str]:
    return {str(p.relative_to(root)): p.read_text(encoding="utf-8") for p in sorted(root.rglob("*")) if p.is_file()}


def versions(pages: Path) -> list[dict]:
    return json.loads((pages / "versions.json").read_text(encoding="utf-8"))


class MinorOfTests(unittest.TestCase):
    def test_full_and_minor_versions_map_to_minor(self):
        self.assertEqual(minor_of("10.1.1"), "10.1")
        self.assertEqual(minor_of("10.1"), "10.1")

    def test_prerelease_rejected(self):
        with self.assertRaises(ValueError):
            minor_of("10.2.0-preview.1")

    def test_malformed_rejected(self):
        for bad in ["", "10", "v10.1.1", "10.1.x", "10.1.1.1", "../10.1"]:
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                minor_of(bad)


class PublishTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.root = Path(self._tmp.name)
        self.pages = self.root / "pages"
        self.pages.mkdir()

    def tearDown(self):
        self._tmp.cleanup()

    def test_first_publish_creates_version_latest_and_root_files(self):
        self.assertEqual(publish(make_site(self.root, "a"), self.pages, "10.1.1"), "10.1")

        self.assertEqual((self.pages / "10.1" / "index.html").read_text(encoding="utf-8"), "home a")
        self.assertEqual((self.pages / "latest" / "index.html").read_text(encoding="utf-8"), "home a")
        self.assertEqual(versions(self.pages), [{"version": "10.1", "latest": True}])
        self.assertEqual((self.pages / "CNAME").read_text(encoding="utf-8"), "caesar.tsezar.io\n")
        self.assertTrue((self.pages / ".nojekyll").exists())
        self.assertIn('url=latest/', (self.pages / "index.html").read_text(encoding="utf-8"))

    def test_newer_minor_replaces_latest_and_keeps_older_folder(self):
        publish(make_site(self.root, "a"), self.pages, "10.1")
        publish(make_site(self.root, "b"), self.pages, "10.2.0")

        self.assertEqual((self.pages / "10.1" / "index.html").read_text(encoding="utf-8"), "home a")
        self.assertEqual((self.pages / "latest" / "index.html").read_text(encoding="utf-8"), "home b")
        self.assertEqual(versions(self.pages), [{"version": "10.2", "latest": True}, {"version": "10.1", "latest": False}])

    def test_older_minor_does_not_replace_latest(self):
        publish(make_site(self.root, "a"), self.pages, "10.2")
        publish(make_site(self.root, "b"), self.pages, "10.1.2")

        self.assertEqual((self.pages / "latest" / "index.html").read_text(encoding="utf-8"), "home a")
        self.assertEqual((self.pages / "10.1" / "index.html").read_text(encoding="utf-8"), "home b")
        self.assertEqual(versions(self.pages)[0], {"version": "10.2", "latest": True})

    def test_numeric_ordering(self):
        publish(make_site(self.root, "a"), self.pages, "10.10")
        publish(make_site(self.root, "b"), self.pages, "10.9")

        self.assertEqual([v["version"] for v in versions(self.pages)], ["10.10", "10.9"])
        self.assertEqual((self.pages / "latest" / "index.html").read_text(encoding="utf-8"), "home a")

    def test_republish_replaces_only_its_folder(self):
        publish(make_site(self.root, "a"), self.pages, "10.1")
        publish(make_site(self.root, "b"), self.pages, "10.2")
        (self.pages / "10.2" / "stale.html").write_text("stale", encoding="utf-8")
        before_10_1 = snapshot(self.pages / "10.1")

        publish(make_site(self.root, "c"), self.pages, "10.2.1")

        self.assertFalse((self.pages / "10.2" / "stale.html").exists())
        self.assertEqual((self.pages / "10.2" / "index.html").read_text(encoding="utf-8"), "home c")
        self.assertEqual(snapshot(self.pages / "10.1"), before_10_1)

    def test_republish_is_idempotent(self):
        site = make_site(self.root, "a")
        publish(site, self.pages, "10.1")
        first = snapshot(self.pages)

        publish(site, self.pages, "10.1")

        self.assertEqual(snapshot(self.pages), first)

    def test_unrelated_files_in_pages_are_kept(self):
        (self.pages / ".git").mkdir()
        (self.pages / ".git" / "HEAD").write_text("ref", encoding="utf-8")

        publish(make_site(self.root, "a"), self.pages, "10.1")

        self.assertEqual((self.pages / ".git" / "HEAD").read_text(encoding="utf-8"), "ref")


if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 2: Run to verify failure**

Run: `cd .github/scripts && python -m unittest test_publish_version -v`
Expected: FAIL — `ModuleNotFoundError: No module named 'publish_version'`.

- [ ] **Step 3: Implement `publish_version.py`**

```python
"""Lay a DocFX build into the gh-pages tree as one minor version of the Caesar docs.

The published tree is:
  /<X.Y>/          one folder per minor version (replaced on every publish of that minor)
  /latest/         copy of the highest minor
  /versions.json   [{"version": "X.Y", "latest": bool}, ...], newest first
  /index.html      redirect to latest/
  /CNAME, /.nojekyll
Other minors' folders are never touched.
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
from pathlib import Path

_VERSION = re.compile(r"^(\d+)\.(\d+)(?:\.(\d+))?$")

_REDIRECT = """<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Caesar documentation</title>
  <meta http-equiv="refresh" content="0; url=latest/">
  <link rel="canonical" href="latest/">
</head>
<body>
  <a href="latest/">Caesar documentation</a>
</body>
</html>
"""


def minor_of(version: str) -> str:
    """Map X.Y or X.Y.Z to X.Y. Prerelease and malformed versions raise ValueError."""
    match = _VERSION.match(version)
    if not match:
        raise ValueError(f"Expected X.Y or X.Y.Z without a prerelease suffix, got {version!r}")
    return f"{int(match.group(1))}.{int(match.group(2))}"


def _key(minor: str) -> tuple[int, int]:
    major, minor_part = minor.split(".")
    return int(major), int(minor_part)


def _replace_dir(source: Path, target: Path) -> None:
    if target.exists():
        shutil.rmtree(target)
    shutil.copytree(source, target)


def _write_if_changed(path: Path, content: str) -> None:
    if not path.exists() or path.read_text(encoding="utf-8") != content:
        path.write_text(content, encoding="utf-8", newline="\n")


def publish(site: Path, pages: Path, version: str, cname: str = "caesar.tsezar.io") -> str:
    minor = minor_of(version)
    if not (site / "index.html").is_file():
        raise ValueError(f"{site} does not look like a DocFX build (no index.html)")

    versions_file = pages / "versions.json"
    known = {entry["version"] for entry in json.loads(versions_file.read_text(encoding="utf-8"))} if versions_file.exists() else set()
    known.add(minor)
    ordered = sorted(known, key=_key, reverse=True)

    _replace_dir(site, pages / minor)
    if ordered[0] == minor:
        _replace_dir(site, pages / "latest")

    entries = [{"version": v, "latest": v == ordered[0]} for v in ordered]
    _write_if_changed(versions_file, json.dumps(entries, indent=2) + "\n")
    _write_if_changed(pages / "index.html", _REDIRECT)
    _write_if_changed(pages / "CNAME", f"{cname}\n")
    _write_if_changed(pages / ".nojekyll", "")
    return minor


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--site", type=Path, required=True, help="DocFX output directory (docs/_site)")
    parser.add_argument("--pages", type=Path, required=True, help="gh-pages working tree")
    parser.add_argument("--version", required=True, help="X.Y or X.Y.Z")
    parser.add_argument("--cname", default="caesar.tsezar.io")
    args = parser.parse_args()
    print(publish(args.site, args.pages, args.version, args.cname))


if __name__ == "__main__":
    main()
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd .github/scripts && python -m unittest test_publish_version -v`
Expected: all tests PASS.

- [ ] **Step 5: Write failing tests `test_changelog_section.py`**

```python
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from changelog_section import extract

CHANGELOG = """# Changelog

## [Unreleased]

- next thing

## [10.1.10] - 2026-12-01

### Fixed

- ten

## [10.1.1] - 2026-09-23

### Added

- one
- two

## [10.1.0] - 2026-09-22

## [10.0.0] - 2026-09-01

- first

[10.1.1]: https://example.test/v10.1.1
"""


class ExtractTests(unittest.TestCase):
    def test_returns_section_body_without_heading(self):
        self.assertEqual(extract(CHANGELOG, "10.1.1"), "### Added\n\n- one\n- two")

    def test_version_prefix_does_not_match_longer_version(self):
        self.assertEqual(extract(CHANGELOG, "10.1.10"), "### Fixed\n\n- ten")

    def test_last_section_stops_before_link_references(self):
        self.assertEqual(extract(CHANGELOG, "10.0.0"), "- first")

    def test_missing_version_returns_none(self):
        self.assertIsNone(extract(CHANGELOG, "9.9.9"))

    def test_empty_section_returns_none(self):
        self.assertIsNone(extract(CHANGELOG, "10.1.0"))

    def test_cli_exit_codes(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "CHANGELOG.md"
            path.write_text(CHANGELOG, encoding="utf-8")
            script = Path(__file__).with_name("changelog_section.py")

            found = subprocess.run([sys.executable, script, path, "10.1.1"], capture_output=True, text=True, check=False)
            missing = subprocess.run([sys.executable, script, path, "9.9.9"], capture_output=True, text=True, check=False)

        self.assertEqual(found.returncode, 0)
        self.assertEqual(found.stdout.strip(), "### Added\n\n- one\n- two")
        self.assertEqual(missing.returncode, 1)
        self.assertIn("9.9.9", missing.stderr)


if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 6: Run to verify failure**

Run: `cd .github/scripts && python -m unittest test_changelog_section -v`
Expected: FAIL — `ModuleNotFoundError: No module named 'changelog_section'`.

- [ ] **Step 7: Implement `changelog_section.py`**

```python
"""Print the body of one version's section from a Keep a Changelog file (used as GitHub Release notes)."""

from __future__ import annotations

import re
import sys
from pathlib import Path

_ANY_SECTION = re.compile(r"^## \[")
_LINK_REFERENCE = re.compile(r"^\[[^\]]+\]:\s")


def extract(text: str, version: str) -> str | None:
    heading = re.compile(rf"^## \[{re.escape(version)}\](\s|$)")
    lines = text.splitlines()
    start = next((i for i, line in enumerate(lines) if heading.match(line)), None)
    if start is None:
        return None

    body: list[str] = []
    for line in lines[start + 1:]:
        if _ANY_SECTION.match(line) or _LINK_REFERENCE.match(line):
            break
        body.append(line)

    section = "\n".join(body).strip()
    return section or None


def main() -> int:
    if len(sys.argv) != 3:
        print("usage: changelog_section.py CHANGELOG.md VERSION", file=sys.stderr)
        return 2
    path, version = Path(sys.argv[1]), sys.argv[2]
    section = extract(path.read_text(encoding="utf-8"), version)
    if section is None:
        print(f"{path} has no non-empty '## [{version}]' section.", file=sys.stderr)
        return 1
    print(section)
    return 0


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 8: Run all script tests**

Run: `python -m unittest discover -s .github/scripts -p "test_*.py" -v`
Expected: all PASS.

- [ ] **Step 9: Check the real changelog**

Run: `python .github/scripts/changelog_section.py CHANGELOG.md 10.1.1`
Expected: prints the `### Added` / `### Changed` body of 10.1.1, exit 0.

- [ ] **Step 10: Commit**

```bash
git add .github/scripts
git commit -m "Add scripts that publish a docs version and extract changelog sections"
```

---

### Task 7: Docs workflow and CI docs job

**Files:**
- Create: `.github/workflows/docs.yml`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: `publish_version.py` CLI (Task 6); `docs/docfx.json` (Tasks 1–5).
- Produces: reusable workflow `./.github/workflows/docs.yml` with inputs `ref` (string) and `version` (string), requiring `contents: write`.

- [ ] **Step 1: `.github/workflows/docs.yml`**

```yaml
name: Docs

# Publishes one minor version of the documentation to the gh-pages branch (served at caesar.tsezar.io).
#   - Called by release.yml after a stable release, with ref vX.Y.Z.
#   - Run by hand (Actions > Docs > Run workflow) to republish a version from any ref, e.g. main -> 10.1,
#     for documentation-only fixes. Other versions on the site are never touched.

on:
  workflow_call:
    inputs:
      ref:
        required: true
        type: string
      version:
        required: true
        type: string
  workflow_dispatch:
    inputs:
      ref:
        description: 'Git ref to build (tag or branch), e.g. v10.1.1 or main'
        required: true
        default: main
        type: string
      version:
        description: 'Docs version to publish, X.Y or X.Y.Z (e.g. 10.1)'
        required: true
        type: string

concurrency:
  group: docs
  cancel-in-progress: false

permissions:
  contents: write

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      # Scripts come from the workflow's own commit; the site is built from the requested ref.
      - uses: actions/checkout@v4

      - uses: actions/checkout@v4
        with:
          ref: ${{ inputs.ref }}
          path: site-src

      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: site-src/global.json

      - name: Build
        working-directory: site-src
        run: dotnet build Caesar.slnx --configuration Release

      - name: Build docs
        working-directory: site-src
        run: |
          dotnet tool restore
          dotnet docfx docs/docfx.json --warningsAsErrors

      - name: Check out gh-pages
        run: |
          if git ls-remote --exit-code --heads origin gh-pages >/dev/null; then
            git fetch --depth 1 origin gh-pages
            git worktree add -B gh-pages "$RUNNER_TEMP/pages" origin/gh-pages
          else
            git worktree add --orphan -b gh-pages "$RUNNER_TEMP/pages"
          fi

      - name: Publish version
        env:
          VERSION: ${{ inputs.version }}
        run: |
          MINOR=$(python3 .github/scripts/publish_version.py --site site-src/docs/_site --pages "$RUNNER_TEMP/pages" --version "$VERSION")
          echo "MINOR=$MINOR" >> "$GITHUB_ENV"

      - name: Commit and push
        working-directory: ${{ runner.temp }}/pages
        env:
          REF: ${{ inputs.ref }}
        run: |
          git add -A
          if git diff --cached --quiet; then
            echo "Docs $MINOR unchanged; nothing to publish."
            exit 0
          fi
          git config user.name "github-actions[bot]"
          git config user.email "github-actions[bot]@users.noreply.github.com"
          git commit -m "docs: publish $MINOR from $REF"
          git push origin gh-pages
```

- [ ] **Step 2: Add the docs job to `.github/workflows/ci.yml`**

Append under `jobs:` (same indentation as `build:`):

```yaml
  docs:
    name: Docs
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Script tests
        run: python3 -m unittest discover -s .github/scripts -p "test_*.py" -v

      - name: Build
        run: dotnet build Caesar.slnx --configuration Release

      - name: Build docs
        run: |
          dotnet tool restore
          dotnet docfx docs/docfx.json --warningsAsErrors
```

- [ ] **Step 3: Rehearse the deploy locally**

```bash
P="$TEMP/pages-rehearsal"; rm -rf "$P"; mkdir -p "$P"
dotnet docfx docs/docfx.json --warningsAsErrors
python .github/scripts/publish_version.py --site docs/_site --pages "$P" --version 10.1
python .github/scripts/publish_version.py --site docs/_site --pages "$P" --version 10.0
cat "$P/versions.json"
```

Expected: `versions.json` lists `10.1` (latest) then `10.0`; `$P/latest`, `$P/10.1`, `$P/10.0` exist.

- [ ] **Step 4: Check the version picker against the rehearsal tree**

Run: `python -m http.server 8090 --directory "$TEMP/pages-rehearsal"` and open `http://localhost:8090/`.
Expected:
- `/` redirects to `/latest/`; picker shows `10.1 (latest)` selected, and `10.0`.
- On `/latest/guide/notifications.html` choose `10.0` → lands on `/10.0/guide/notifications.html`.
- Delete `$TEMP/pages-rehearsal/10.0/guide/notifications.html`, repeat → lands on `/10.0/` (fallback).
- Rename `versions.json` → picker disappears, page still works.
Stop the server and delete `$TEMP/pages-rehearsal`.

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/docs.yml .github/workflows/ci.yml
git commit -m "Add docs publishing workflow and a docs check in CI"
```

---

### Task 8: Release workflow integration

**Files:**
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: `changelog_section.py` CLI (Task 6); `docs.yml` reusable workflow (Task 7).
- Produces: job outputs `released` (`'true'` when a new tag + release were created) and `version`.

- [ ] **Step 1: Add job outputs**

Under `jobs.release`, after `environment: nuget.org`:

```yaml
    outputs:
      released: ${{ steps.tag.outputs.released }}
      version: ${{ steps.version.outputs.version }}
```

- [ ] **Step 2: Add the changelog guard after "Resolve package version"**

```yaml
      - name: Require a changelog entry for a new stable version
        id: changelog
        env:
          VERSION: ${{ steps.version.outputs.version }}
        run: |
          if git ls-remote --exit-code --tags origin "v$VERSION" >/dev/null 2>&1; then
            echo "Tag v$VERSION already exists; no new release, changelog not required."
            exit 0
          fi
          case "$VERSION" in
            *-*) echo "Prerelease $VERSION: changelog entry optional, notes are generated."; exit 0 ;;
          esac
          if ! python3 .github/scripts/changelog_section.py CHANGELOG.md "$VERSION" > "$RUNNER_TEMP/release-notes.md"; then
            echo "CHANGELOG.md needs a '## [$VERSION]' section before $VERSION can be released." >&2
            exit 1
          fi
          echo "notes=$RUNNER_TEMP/release-notes.md" >> "$GITHUB_OUTPUT"
```

- [ ] **Step 3: Update "Tag and create GitHub release"**

Give the step `id: tag`, pass the notes file, and emit `released`:

```yaml
      - name: Tag and create GitHub release
        id: tag
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          VERSION: ${{ steps.version.outputs.version }}
          NOTES: ${{ steps.changelog.outputs.notes }}
        run: |
          TAG="v$VERSION"
          if git rev-parse -q --verify "refs/tags/$TAG" >/dev/null || git ls-remote --exit-code --tags origin "$TAG" >/dev/null 2>&1; then
            echo "Tag $TAG already exists; skipping release creation."
            exit 0
          fi
          git config user.name "github-actions[bot]"
          git config user.email "github-actions[bot]@users.noreply.github.com"
          git tag -a "$TAG" -m "Caesar $VERSION"
          git push origin "$TAG"
          if [ -n "$NOTES" ]; then NOTES_ARGS=(--notes-file "$NOTES"); else NOTES_ARGS=(--generate-notes); fi
          gh release create "$TAG" artifacts/packages/*.nupkg artifacts/packages/*.snupkg \
            --title "Caesar $VERSION" \
            "${NOTES_ARGS[@]}"
          echo "released=true" >> "$GITHUB_OUTPUT"
```

- [ ] **Step 4: Add the docs job at the end of `jobs:`**

```yaml
  docs:
    needs: release
    # A tag pushed with GITHUB_TOKEN does not trigger other workflows, so publish the docs explicitly.
    if: needs.release.outputs.released == 'true' && !contains(needs.release.outputs.version, '-')
    uses: ./.github/workflows/docs.yml
    with:
      ref: v${{ needs.release.outputs.version }}
      version: ${{ needs.release.outputs.version }}
    permissions:
      contents: write
```

Update the header comment of `release.yml`: add "3. A stable release also publishes its docs (docs.yml) and takes its GitHub Release notes from CHANGELOG.md, which must have a `## [X.Y.Z]` section."

- [ ] **Step 5: Validate workflow syntax**

Run: `python -c "import yaml,sys; [yaml.safe_load(open(f, encoding='utf-8')) for f in sys.argv[1:]]; print('ok')" .github/workflows/release.yml .github/workflows/docs.yml .github/workflows/ci.yml`
Expected: `ok` (if PyYAML is missing: `python -m pip install pyyaml` into a scratch venv, or rely on the PR's Actions run).
Run the guard logic by hand: `python .github/scripts/changelog_section.py CHANGELOG.md 10.1.2; echo $?` → `1`.

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "Release: require a changelog entry, use it as notes, publish docs"
```

---

### Task 9: README and package metadata

**Files:**
- Modify: `README.md`, `Directory.Build.props:26`

- [ ] **Step 1: Point `PackageProjectUrl` at the docs**

In `Directory.Build.props` replace `<PackageProjectUrl>https://github.com/tsmshvenieradze/Caesar</PackageProjectUrl>` with `<PackageProjectUrl>https://caesar.tsezar.io</PackageProjectUrl>` (`RepositoryUrl` stays on GitHub).

- [ ] **Step 2: Rewrite `README.md`**

Keep, in this order: the title and intro paragraph; a line `**Documentation: [caesar.tsezar.io](https://caesar.tsezar.io)**`; the package table; the Features list; Installation (versions `10.1.1`); a Quick start made of the three current Quick start code blocks with one sentence each and a closing link "More in the [guides](https://caesar.tsezar.io/latest/guide/getting-started.html)"; Repository layout (add `docs/  DocFX site: guides, snippets, theme` and `docs/snippets  compiled examples used by the guides`); Contributing and releasing (as today, adding: "Before bumping `<VersionPrefix>`, add a `## [X.Y.Z] - YYYY-MM-DD` section to `CHANGELOG.md`; the Release workflow refuses a stable version without one, uses it as the GitHub Release notes, and publishes that version's docs." and a sub-list "Docs: `dotnet tool restore`, then `dotnet docfx docs/docfx.json --serve`"); License.
Remove the sections now covered by the guides: Notifications, Pipeline behaviors, Pre- and post-processors, Exception handling, Streams, Configuration reference (incl. Lifetimes and Open-generic handlers), Migrating from MediatR. Also drop the stale "main is protected ... CODEOWNERS" sentence (the recreated repo has no branch protection or CODEOWNERS file).

- [ ] **Step 3: Verify build, pack and docs**

Run: `dotnet build Caesar.slnx -c Release && dotnet pack src/Caesar/Caesar.csproj -c Release --no-build -o "$TEMP/pack-check" && unzip -p "$TEMP/pack-check/Caesar.10.1.1.nupkg" Caesar.nuspec | grep projectUrl && dotnet docfx docs/docfx.json --warningsAsErrors`
Expected: `<projectUrl>https://caesar.tsezar.io/</projectUrl>` (trailing slash may be added); docs build 0 warnings. Delete `$TEMP/pack-check`.

- [ ] **Step 4: Commit**

```bash
git add README.md Directory.Build.props
git commit -m "Slim the README down and link the documentation site"
```

---

### Task 10: Ship and launch

**Files:** none (GitHub, Cloudflare, Pages configuration)

- [ ] **Step 1: Full local verification**

Run: `dotnet build Caesar.slnx -c Release && dotnet test Caesar.slnx -c Release --no-build && python -m unittest discover -s .github/scripts -p "test_*.py" && dotnet docfx docs/docfx.json --warningsAsErrors`
Expected: all green, `Passed: 77`, 0 docfx warnings.

- [ ] **Step 2: Push and open the PR**

```bash
git push -u origin docs/site
gh pr create --base main --title "Documentation site at caesar.tsezar.io" --body-file <notes>
```

Expected: CI `Build & Test` and `Docs` jobs green. The owner merges. (Release on merge is a no-op: v10.1.1 exists.)

- [ ] **Step 3: First publish (manual, from main)**

Run: `gh workflow run docs.yml -f ref=main -f version=10.1` then `gh run watch <id> --exit-status`.
Expected: success; `gh-pages` branch exists with `10.1/`, `latest/`, `versions.json`, `index.html`, `CNAME`, `.nojekyll`.

- [ ] **Step 4: Enable Pages from gh-pages**

```bash
gh api -X POST repos/tsmshvenieradze/Caesar/pages -f "source[branch]=gh-pages" -f "source[path]=/"
gh api -X PUT repos/tsmshvenieradze/Caesar/pages -f cname=caesar.tsezar.io
```

Expected: `gh api repos/tsmshvenieradze/Caesar/pages -q .cname` → `caesar.tsezar.io`.

- [ ] **Step 5: Owner: DNS and domain verification**

The owner adds in Cloudflare (zone `tsezar.io`): CNAME `caesar` → `tsmshvenieradze.github.io`, **DNS only**; and in GitHub → Settings (account) → Pages → Add verified domain `tsezar.io`, then the TXT record `_github-pages-challenge-tsmshvenieradze` with the shown value, then Verify.

- [ ] **Step 6: Enforce HTTPS once the certificate is issued**

Poll: `gh api repos/tsmshvenieradze/Caesar/pages -q '.https_certificate.state'` until `approved`, then `gh api -X PUT repos/tsmshvenieradze/Caesar/pages -F https_enforced=true`.

- [ ] **Step 7: Verify the live site**

```bash
curl -sI https://caesar.tsezar.io | head -1
curl -s https://caesar.tsezar.io/ | grep -o 'url=latest/'
for p in 10.1/ latest/ latest/changelog.html latest/api/Caesar.ISender.html versions.json; do echo "$p $(curl -s -o /dev/null -w '%{http_code}' https://caesar.tsezar.io/$p)"; done
```

Expected: HTTP 200 over valid TLS; redirect present; every path `200`.

- [ ] **Step 8: Verify idempotence**

Run `gh workflow run docs.yml -f ref=main -f version=10.1` again and watch it.
Expected: log says `Docs 10.1 unchanged; nothing to publish.`; no new `gh-pages` commit (`git ls-remote origin gh-pages` hash unchanged).
