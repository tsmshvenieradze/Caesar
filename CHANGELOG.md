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
