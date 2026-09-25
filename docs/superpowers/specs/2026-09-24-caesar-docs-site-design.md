# Caesar documentation site — design

Date: 2026-09-24
Status: approved in conversation, pending written-spec review

## Goal

A versioned documentation site for Caesar at `https://caesar.tsezar.io`. A developer arriving from NuGet or GitHub
can install Caesar and get a first request working from the guides, and can look up any public type in a generated
API reference. The site also carries the project changelog.

Success criteria:

- `https://caesar.tsezar.io` serves the docs over valid HTTPS; its root is a landing page linking to `/latest/`.
- Guides cover every feature the README covers today, with code examples that are compiled in CI.
- Every public type in `Caesar.Abstractions` and `Caesar` has a generated API page.
- Each minor version has its own frozen docs folder, selectable from a version dropdown.
- A release publishes its docs with no manual step; a doc-only fix can be republished without a NuGet release.

Constraints:

- Personal project: authorship is Tsezari Mshvenieradze; no GPIH branding anywhere.
- English only.
- Hosted on GitHub Pages from the Caesar repo; DNS stays on Cloudflare (`tsezar.io` zone).
- No long-lived secrets (matches the keyless NuGet Trusted Publishing setup).

## Tooling

- **DocFX**, pinned as a local .NET tool in `.config/dotnet-tools.json`.
  Local preview: `dotnet tool restore` then `dotnet docfx docs/docfx.json --serve`.
- **API metadata** is generated from the compiled Release assemblies and their XML documentation files
  (`src/Caesar.Abstractions/bin/Release/net10.0/Caesar.Abstractions.dll`, `src/Caesar/bin/Release/net10.0/Caesar.dll`),
  not from `.csproj` files, to avoid DocFX's MSBuild workspace lagging the .NET 10 SDK.
  The first implementation step verifies this; if assembly-based metadata fails, fall back to project-based metadata.
- **Strict build**: `docfx` runs with `--warningsAsErrors`; broken links, unresolved `xref`s and bad TOC entries fail.

## Repository layout

```
.config/dotnet-tools.json          docfx local tool
CHANGELOG.md                       Keep a Changelog; source for site page and GitHub Release notes
docs/
  docfx.json                       metadata + build config; excludes superpowers/ and snippets/ bin/obj
  index.md                         landing: what Caesar is, install, 30-second example
  toc.yml                          top nav: Guide · API · Changelog · GitHub · NuGet
  changelog.md                     includes ../CHANGELOG.md
  guide/
    toc.yml
    getting-started.md             install, first request + handler, AddCaesar, Send
    requests.md                    commands/queries, Unit, Send(object) runtime dispatch
    notifications.md               handlers, ForeachAwait / TaskWhenAll / ContinueOnFailure publishers, custom publisher
    pipeline-behaviors.md          open vs closed behaviors, ordering, stream behaviors
    processors.md                  pre/post-processors
    exception-handling.md          exception handlers vs actions, type hierarchy, strategy option
    streams.md                     IStreamRequest, CreateStream
    configuration.md               all options, Lifetime vs MediatorLifetime, open-generic rules, idempotent registration
    clean-architecture.md          Abstractions in Application layer, DI in composition root (from the sample)
    migrating-from-mediatr.md
  snippets/
    Caesar.Docs.Snippets.csproj    compiled example code; IsPackable=false; in Caesar.slnx
    *.cs                           #region blocks referenced from guides
  template/                        overrides on top of DocFX "modern"
    public/main.css                brand tokens, fonts
    public/main.js                 version dropdown
    (logo / favicon assets)
  api/                             generated; gitignored
  superpowers/specs/               design docs; excluded from the site build
```

Guides are written from the current README sections, split one topic per page and expanded where the README is terse.
Code blocks in guides come from `docs/snippets` via DocFX code includes (`[!code-csharp[](../snippets/File.cs#region)]`),
so an API change that breaks an example fails CI.

The API reference covers public types only, grouped by namespace: `Caesar`, `Caesar.Pipeline`,
`Caesar.NotificationPublishers`, `Caesar.DependencyInjection`.

## Theme

DocFX `modern` template plus `docs/template` overrides, matching tsezar.io:

- Dark mode default: background `#0A0A0F`, text `#FFFFFF`, muted `#7A7A85`, accent `#00E5FF`.
- Light/dark toggle kept; light mode accent `#00838F` (cyan on white fails contrast).
- Fonts: Space Grotesk (text, headings), JetBrains Mono (code), from Google Fonts.
- Logo/favicon: "C" monogram in the style of tsezar.io's "T" favicon.
- Footer: `© Tsezari Mshvenieradze · tsezar.io` (linked).
- Version dropdown in the header, injected by `main.js` from `/versions.json`; newest entry labelled "latest".
  Switching versions keeps the current page path when it exists in the target version, otherwise opens that version's home.
- Must remain usable at phone width.

## Versioning

Published site layout (served from the `gh-pages` branch root):

```
/index.html        landing page linking to /latest/; no automatic redirect, which on a new host is a
                   phishing-kit pattern (Safe Browsing flagged the first deploy as deceptive)
/CNAME             caesar.tsezar.io
/.nojekyll
/versions.json     [{ "version": "10.1", "latest": true }, ...] newest first
/latest/           copy of the newest minor's build
/10.1/             docs for the newest 10.1.x release
/10.2/ ...         one folder per minor
```

- One docs folder per **minor** version (`X.Y`); a patch release replaces its minor's folder.
- Folders for other versions are never touched by a deploy.
- `latest/` is refreshed only when the deployed version is the highest in `versions.json` (semantic comparison).
- The changelog page is part of every build, so it is current under `/latest/changelog.html`; the top nav links there.
- First published version: `10.1`.

## Workflows

### `docs.yml` (new)

Triggers:

- `workflow_call` with input `ref` (tag or branch) and `version` (full `X.Y.Z` or `X.Y`), called by `release.yml`.
- `workflow_dispatch` with the same inputs, for doc-only fixes (e.g. ref `main`, version `10.1`).

Permissions: `contents: write`. Concurrency group `docs`, `cancel-in-progress: false`.

Steps:

1. Check out `ref`; setup .NET from `global.json`; `dotnet build Caesar.slnx -c Release`.
2. `dotnet tool restore`; `dotnet docfx docs/docfx.json --warningsAsErrors` → `docs/_site`.
3. Derive `X.Y` from `version`; validate it matches `^[0-9]+\.[0-9]+$` after derivation.
4. Check out `gh-pages` into a second directory (create it as an orphan branch if missing).
5. Replace `X.Y/` with `_site`; update `versions.json`; if `X.Y` is the highest, replace `latest/` too.
6. Ensure the `index.html` landing page, `CNAME`, `.nojekyll` exist.
7. Commit (`docs: publish X.Y from <ref>`) and push only if something changed.

GitHub Pages is configured as "Deploy from a branch: `gh-pages` / root".

### `release.yml` (changed)

- **Changelog guard**: when the version's tag does not exist yet, require a `## [X.Y.Z]` section in `CHANGELOG.md`;
  fail before Pack/Push if missing. Runs where the tag already exists (ordinary merges) are unaffected.
- GitHub Release notes: extract that section to a file and pass `--notes-file` instead of `--generate-notes`.
- After the tag and release are created, call `docs.yml` with `ref: vX.Y.Z` and `version: X.Y.Z`.
  (A tag pushed with `GITHUB_TOKEN` does not trigger other workflows, so the call is explicit.)
  The call is skipped when release creation was skipped because the tag already existed.

### `ci.yml` (changed)

- Add a docs job on pull requests and pushes: build Release, `dotnet tool restore`, strict `docfx` build. No deploy.

## DNS and HTTPS (done by the owner in Cloudflare and GitHub)

| Type  | Name                                      | Value                       | Proxy               |
| ----- | ----------------------------------------- | --------------------------- | ------------------- |
| CNAME | `caesar`                                  | `tsmshvenieradze.github.io` | DNS only (grey)     |
| TXT   | `_github-pages-challenge-tsmshvenieradze` | code shown by GitHub        | —                   |

- The TXT record verifies `tsezar.io` on the GitHub account (Settings → Pages → Verified domains) to prevent
  subdomain takeover.
- The CNAME stays DNS-only so GitHub can issue and renew the Let's Encrypt certificate; then enable "Enforce HTTPS".

## README and package metadata

- README shrinks to: pitch, feature list, installation, a ~20-line quick start, a prominent link to
  `https://caesar.tsezar.io`, and the existing "Contributing and releasing" section (plus a line on adding a
  `CHANGELOG.md` entry before bumping `VersionPrefix`). The README is also the NuGet package page.
- `PackageProjectUrl` in `Directory.Build.props` becomes `https://caesar.tsezar.io` (effective from the next release).

## Changelog

`CHANGELOG.md` at the repo root in Keep a Changelog format (`Added` / `Changed` / `Fixed` / `Removed`), with an
`Unreleased` section on top. First entry: `10.1.1` — mediator scoped by default via `MediatorLifetime`, open-generic
handlers the container cannot close rejected at `AddCaesar`, package authorship metadata.

## Launch

The `v10.1.1` tag predates `docs/`, so the first `/10.1/` is published by a manual `docs.yml` run from `main` with
version `10.1` after this work merges. Later releases publish automatically.

## Verification

- Local: strict `docfx` build passes; snippets project compiles; `--serve` check of guides, API pages, search,
  version dropdown, dark/light toggle, phone width.
- CI: docs job green on the PR.
- After first deploy: `https://caesar.tsezar.io` shows the landing page linking to `/latest/`; `/10.1/`,
  `/latest/changelog.html` and `/versions.json` return 200; the `ISender` API page exists; certificate valid.
- Idempotence: a second manual deploy of the same ref produces no `gh-pages` commit, and other version folders are
  unchanged.

## Out of scope

Georgian translation, PR preview deployments, analytics, blog/news, per-patch docs versions, Cloudflare proxying.
