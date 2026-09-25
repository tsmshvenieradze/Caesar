# Changelog

All notable changes to Caesar are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/), with the major version tracking the targeted .NET version.

## [Unreleased]

## [10.2.0] - 2026-09-25

### Breaking changes

These can change what an existing application sees after upgrading; the entries below give the details.

- The built-in stages are no longer `IPipelineBehavior<,>` registrations. Code that resolves or removes them from the
  service collection, or a custom `IMediator` that does not derive from `Mediator` and relied on them, no longer finds
  them.
- `Send`, `Publish` and `CreateStream` report failures through the returned task or enumerator instead of throwing
  from the call. A test that wraps the call itself in `Assert.Throws` must await it instead.
- Notification handlers registered for a base class or interface of a notification, `INotification` included, now
  run for it.
- Nested `internal` and `protected internal` handlers, processors and exception handlers are now scanned and run.
- A later `AddCaesar` call that sets a global option to a different value throws, as does scanning two different
  open-generic handlers for one request interface.
- A failing exception action no longer replaces the original exception, and an exception handler or action class
  implemented for several exception types runs once.
- `Send(object)` and `CreateStream(object)` throw `ArgumentException` for a type that declares several request
  interfaces.

### Added

- `CaesarServiceConfiguration.BypassExceptionHandlingOnCallerCancellation` (default `false`). When set, an
  `OperationCanceledException` thrown while the token passed to `Send` is cancelled goes straight to the caller without
  reaching exception handlers or actions, so a catch-all handler cannot turn a client abort into a fallback response.
  Cancellation from any other token, such as a timeout inside the pipeline, is still routed. By default cancellations
  reach handlers and actions like any other exception, as in MediatR.

- Notification handlers registered for a base class of the published notification, for an interface it implements, or
  for `INotification` itself now run, through `Publish<T>` and `Publish(object)` and with every publisher, custom
  publishers and `PublishCore` overrides included. Before, they were registered but never ran, with no error.
  - Order: the handlers for the runtime type first, in registration order, open-generic ones included; then base
    classes, most derived first; then interfaces, a derived interface before the ones it extends, `INotification` last.
  - A handler class registered for several of these types runs once, for the most specific one. Duplicate
    registrations for one type still run once each.
  - For base classes and interfaces only closed registrations count, so an open-generic handler runs once, for the
    runtime type, and never for a base type its constraints accept when they reject the runtime type.
  - Requires a container set up with `AddCaesar`; handlers for base types registered by hand after `AddCaesar` are
    included. A `Mediator` over a container without `AddCaesar` runs only the runtime type's handlers, as before.
- The exceptions thrown by failing exception actions are recorded on the original exception, in
  `exception.Data["Caesar.ExceptionActionFailures"]` as an `IReadOnlyList<Exception>`, in the order the actions ran and
  after those of a nested `Send` the exception passed through. The list keeps the 16 most recent failures, so an
  exception object that is thrown again by later sends does not collect them without bound.
- Dependabot configuration: weekly updates for GitHub Actions and NuGet packages. Major updates of
  `Microsoft.Extensions.*` and `Microsoft.SourceLink.*` are ignored, since the major version tracks .NET.

### Changed

- **Pipeline composition.** The built-in stages (exception actions, exception handlers, pre-processors and
  post-processors) are no longer registered as `IPipelineBehavior<,>`, so resolving the pipeline behaviors returns only
  your own. The mediator adds each stage per request type, only when something that applies to that request is
  registered (a closed registration for it, or an open-generic one), always in the documented order. Neither the number
  nor the order of `AddCaesar` calls changes it any more. Behaviors registered by hand before the first `AddCaesar`
  call still run outside the built-in stages, as before; the others run inside them. A built-in behavior class you
  register yourself as a pipeline behavior by type (`AddBehavior`, `AddOpenBehavior` or an implementation-type
  descriptor) makes Caesar leave its own stage out, so it runs once. Caesar creates the stages itself, through their
  public constructors, after your behaviors are resolved. The stages are composed by `Mediator` (and classes derived
  from it) in a container set up with `AddCaesar`.
- What a request type needs is worked out from the service collection of the last `AddCaesar` call and from the
  container itself, asked through `IServiceProviderIsService`. So processors and handlers count when the container was
  built from a copy of the collection, when the collection changed after the container was built, and when they are
  registered natively in a third-party container such as Autofac. Only Caesar's own service types are indexed.
- The public built-in behaviors (`RequestPreProcessorBehavior`, `RequestPostProcessorBehavior`,
  `RequestExceptionProcessorBehavior`, `RequestExceptionActionProcessorBehavior`) are no longer `async` methods: with no
  processors, or when `next` completes synchronously and successfully, they return `next`'s task directly.
- **`Send` never throws synchronously**, except `ArgumentNullException` for a `null` request and `ArgumentException`
  for a request whose handler cannot be chosen from its type. A missing handler, a throwing handler, behavior or
  processor, and container errors fault the returned task; an `OperationCanceledException` cancels it.
- **`Publish` never throws synchronously** after its argument checks. A handler the container cannot create and a
  publisher or `PublishCore` override that throws before returning now fault (or, for an `OperationCanceledException`,
  cancel) the returned task.
- **`CreateStream` is fully lazy.** Behaviors and the handler are resolved and run when enumeration starts, so a missing
  handler fails on `MoveNextAsync`, not at the call. Each enumeration builds its own pipeline, and the token given to
  `CreateStream` is combined with the enumerator's.
- Handler resolution errors are no longer relabelled: a registered handler with a missing dependency, a scope violation
  or a throwing constructor surfaces the container's own exception. "No handler was found" is reported only when
  nothing that can handle the request resolves (nothing registered, or only an open generic whose constraints reject
  it), and it, like the `ArgumentException` messages of `Send(object)` and `CreateStream(object)`, renders generic
  types as written in source.
- `Send(object)` and `CreateStream(object)` throw `ArgumentException` for a type that declares more than one
  `IRequest<T>` or `IStreamRequest<T>` (`IRequest` together with `IRequest<int>` counts as two) instead of silently
  picking one.
- An exception action that fails, synchronously, with a faulted task or with a `null` task, no longer replaces the
  original exception or stops the remaining actions: all of them run, then the original is rethrown with its stack
  trace. This differs from MediatR, where the action's exception replaces the original; translate exceptions in an
  exception handler instead.
- An exception handler or action class implemented for several levels of the exception's hierarchy runs once, for the
  most specific level, as in MediatR. Several registrations of one class for the same exception type all run.
- An exception handler that returns a `null` task fails with an `InvalidOperationException` naming it, with the original
  exception as `InnerException`, instead of a `NullReferenceException`.
- A notification handler that returns a `null` task fails with an `InvalidOperationException` naming it under all three
  built-in publishers, instead of a `NullReferenceException`.
- `ForeachAwaitContinueOnFailurePublisher` invokes no further handler once the token is cancelled, including a token
  cancelled before `Publish`. With no failures so far the publish is canceled; otherwise those failures are thrown and
  the cancellation is dropped.
- **Registration.**
  - Scanning registers nested types that code in their assembly can name: `public`, `internal` and
    `protected internal` ones, such as a vertical slice's internal handler. `private` and `protected` nested types,
    typically test doubles, are still skipped, and a non-public nested type that cannot be registered (one nested in a
    generic class, say) is skipped rather than rejected. Compiler-generated types are recognised by their names, so a
    partial class a source generator marked `[CompilerGenerated]` is scanned. Use `TypeEvaluator` to exclude a type.
  - A processor added with `AddRequestPreProcessor`, `AddRequestPostProcessor` or their open-generic variants that
    scanning in the same `AddCaesar` call also finds is registered once, with the explicitly requested lifetime, at its
    scanned position.
  - A later `AddCaesar` call that sets `MediatorLifetime`, `MediatorImplementationType`, `NotificationPublisher`,
    `NotificationPublisherType`, `RequestExceptionActionProcessorStrategy` or
    `BypassExceptionHandlingOnCallerCancellation` to a value other than the first call's throws
    `InvalidOperationException` naming the option. Before, the value was silently ignored. Publishers are compared by
    the type in effect, so two instances of one publisher, or an instance and its type, agree.
  - `AddCaesar` throws `InvalidOperationException`, naming both types, when scanning finds a second, different
    open-generic implementation of `IRequestHandler<,>`, `IRequestHandler<>` or `IStreamRequestHandler<,>`, in the
    same call or a later one. Before, one of them was silently dropped.
  - `AddCaesar` throws `ArgumentException`, naming the option, when `MediatorImplementationType` or
    `NotificationPublisherType` is `null`, an interface, abstract, an open generic, not a class, or does not implement
    its interface, instead of a `NullReferenceException` or accepting it.
  - The "cannot close" error names nested types with their declaring types (`Outer<T>.Handler`) and the interface
    actually implemented, and explains when a type is generic only because it is nested in a generic type.
  - `AddCaesar` adds internal descriptors that are visible when enumerating the `IServiceCollection`:
    `CaesarRegistrationState`, `CaesarRegistry`, the open-generic singletons `RequestPipelineShape<,>`,
    `StreamRequestShape<,>` and `NotificationHandlerShape<>`, and, when scanning finds an open-generic single handler,
    `ScannedOpenGenericHandlers`.
- **Performance.** A request with no behaviors and no built-in stages calls its handler directly, and `Send` allocates
  nothing of its own for it. Processors, exception handlers and actions registered for other requests no longer add
  stages to a request. The exception stages call handlers and actions through cached typed delegates instead of
  `MethodInfo.Invoke`. Notification publishing allocates less, and `TaskWhenAllPublisher` skips `Task.WhenAll` when
  every handler completed synchronously. A command whose only handler is `IRequestHandler<T, Unit>` no longer looks up
  `IRequestHandler<T>` on every `Send`. A typed `CreateStream` hands out the pipeline's own enumerator, with no extra
  layer per item. Assembly scanning is about a third faster and allocates far less.
- **Release workflow.** It is split into a read-only build job and a publish job that alone gets the `nuget.org`
  environment, OIDC and `contents: write`, and only when there is something to release, so a no-op merge mints no NuGet
  key.
  - The tag is created through the GitHub API on the workflow's commit (still annotated, "Caesar X.Y.Z").
  - On merge, a version already on nuget.org without its `v` tag now fails the run ("published but untagged") instead
    of tagging the merge commit. A manual run also fails when the tag already exists.
  - Re-runs are idempotent: when the tag already points at the commit, the GitHub Release is created or completed and
    the docs are published. `--skip-duplicate` is used only when the packages on nuget.org were built from the same
    commit, or on a re-run of the run that pushed them; any other duplicate fails the push. The "published but
    untagged" error points at re-running the original run.
  - A prerelease version is published as a GitHub prerelease and never marked Latest.
  - The publish job refuses to run on any ref other than `refs/heads/main`.
  - The nuget.org pre-check fails on any status other than 200 or 404, retries transient errors and uses the
    lowercased version. Pack must produce exactly the two expected packages and their symbol packages.
  - The `packages-X.Y.Z` artifact now holds `packages/` and `release-notes.md`. The concurrency group is per event,
    so a merge's run can no longer replace a pending manual run; a push run and a manual run can now run at the same
    time.
- The docs workflow builds the site in a read-only job and publishes it from a separate job; refs under `refs/pull/`
  are rejected.
- `changelog_section.py` accepts linked version headings, ignores version-heading lines inside code fences and keeps
  link references inside a section.

### Fixed

- A typed `Send` or `CreateStream` through a covariant view (a request declared as `IRequest<Dog>`, sent as
  `IRequest<Animal>` or with `TResponse` of `object`) goes to the handler registered for the call site's type when
  there is one, as before, and otherwise to the declared handler and behaviors. Before, a call site without its own
  handler failed with "No handler was found" and then broke that request type for the rest of the process with
  `InvalidCastException`.
- A type that declares several `IRequest<T>` interfaces works through every typed `Send` overload.
- Processors, exception handlers and exception actions registered after `AddCaesar` now run.
- A constrained open-generic request or stream handler that cannot close for a request no longer throws
  `ArgumentException`: dispatch skips it and falls back to the command's other handler interface, or reports
  "No handler was found".
- An exception handler or action that throws synchronously surfaces its own exception instead of
  `TargetInvocationException`, and an outer exception action sees the real exception type.
- `TaskWhenAllPublisher` and `ForeachAwaitContinueOnFailurePublisher` rethrow a single failure with its original stack
  trace instead of one that starts in the publisher.
- The static caches (request, stream and notification wrappers, exception type hierarchies and the exception stages'
  per-type delegates) no longer keep a collectible `AssemblyLoadContext` alive, including when a request fails with
  an exception type from that context.
- Docs and sample: the getting-started registration and the sample use the default sequential publisher, since
  `TaskWhenAllPublisher` runs handlers from one scope concurrently; the sample's logging behavior no longer logs failed
  requests as handled; the validation behavior examples report failures through the task.

### Security

- The Release workflow validates a manually entered version as strict SemVer 2.0 over the whole string (no leading
  zeros, build metadata, empty identifiers or newlines) before using it, and logs a rejected value escaped.
- Workflow checkouts no longer persist credentials, and every action is pinned to a commit SHA. The jobs that publish
  packages or docs do not build or test the repository.

## [10.1.1] - 2026-09-23

### Added

- `CaesarServiceConfiguration.MediatorLifetime` (default `Scoped`) controls the lifetime of `IMediator`, `ISender`,
  `IPublisher` and `INotificationPublisher`. A singleton that captures `ISender` is now reported at
  startup by container validation (`ValidateScopes` + `ValidateOnBuild`, on by default in Development) instead of
  failing on the first request that needs a scoped dependency.
- `AddCaesar` throws when a scanned open-generic type implements a Caesar interface in a shape the container can
  never close (different arity or reordered type parameters), instead of failing on the first request.

### Changed

- `Lifetime` now applies only to scanned handlers, processors, exception handlers and actions.
- `ISender` / `IMediator` are no longer resolvable from the root provider by default; resolve them inside a scope,
  or set `MediatorLifetime = ServiceLifetime.Transient`.
- Package authorship metadata now names Tsezari Mshvenieradze.

[Unreleased]: https://github.com/tsmshvenieradze/Caesar/compare/v10.2.0...HEAD
[10.2.0]: https://github.com/tsmshvenieradze/Caesar/releases/tag/v10.2.0
[10.1.1]: https://github.com/tsmshvenieradze/Caesar/releases/tag/v10.1.1
