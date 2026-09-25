"""Lay a DocFX build into the gh-pages tree as one minor version of the Caesar docs.

The published tree is:
  /<X.Y>/          one folder per minor version (replaced on every publish of that minor)
  /latest/         copy of the highest minor
  /versions.json   [{"version": "X.Y", "latest": bool}, ...], newest first
  /index.html      landing page linking to latest/ (no automatic redirect)
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

# A real page rather than an instant redirect to latest/: a new host whose root only bounces visitors elsewhere
# looks like a phishing kit to Safe Browsing. No scripts and no third-party requests.
_LANDING = """<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Caesar &middot; in-process mediator for .NET</title>
  <meta name="description" content="Caesar is a lightweight in-process mediator for .NET 10: requests, notifications, streams and pipeline behaviors, wired through Microsoft.Extensions.DependencyInjection.">
  <link rel="icon" href="latest/images/logo.svg" type="image/svg+xml">
  <style>
    :root {
      color-scheme: dark;
      --bg: #0A0A0F;
      --fg: #FFFFFF;
      --muted: #7A7A85;
      --surface: #12121A;
      --border: #24242E;
      --accent: #00E5FF;
      --accent-hover: #7DF3FF;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      display: flex;
      flex-direction: column;
      background: var(--bg);
      color: var(--fg);
      font-family: "Space Grotesk", system-ui, -apple-system, "Segoe UI", sans-serif;
      line-height: 1.6;
    }
    main {
      flex: 1;
      width: 100%;
      max-width: 42rem;
      margin: 0 auto;
      padding: 14vh 16px 3rem;
    }
    h1 {
      margin: 1rem 0 0.5rem;
      font-size: clamp(2.25rem, 8vw, 3rem);
      line-height: 1.1;
      letter-spacing: -0.02em;
    }
    .lead { margin: 0 0 1.5rem; font-size: 1.125rem; color: #D0D0D8; }
    pre {
      margin: 0;
      padding: 0.75rem 1rem;
      overflow-x: auto;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 6px;
      color: var(--accent-hover);
      font: 0.9rem/1.6 "JetBrains Mono", ui-monospace, SFMono-Regular, Consolas, monospace;
    }
    .comment { color: var(--muted); }
    nav { display: flex; flex-wrap: wrap; gap: 0.75rem; margin-top: 2rem; }
    nav a {
      padding: 0.6rem 1.1rem;
      border: 1px solid var(--border);
      border-radius: 6px;
      color: var(--fg);
      font-weight: 600;
      text-decoration: none;
    }
    nav a:hover { border-color: var(--accent); }
    nav a.primary { background: var(--accent); border-color: var(--accent); color: var(--bg); }
    nav a.primary:hover { background: var(--accent-hover); border-color: var(--accent-hover); }
    a:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
    footer {
      padding: 1.5rem 16px;
      border-top: 1px solid var(--border);
      color: var(--muted);
      font-size: 0.875rem;
      text-align: center;
    }
    footer a { color: var(--accent); }
  </style>
</head>
<body>
  <main>
    <svg xmlns="http://www.w3.org/2000/svg" width="56" height="56" viewBox="0 0 32 32" aria-hidden="true"><rect width="32" height="32" rx="6" fill="#16161F"/><text x="16" y="22" text-anchor="middle" font-family="Space Grotesk, sans-serif" font-size="20" font-weight="700" fill="#00E5FF">C</text></svg>
    <h1>Caesar</h1>
    <p class="lead">A lightweight in-process mediator for .NET 10. Requests go to exactly one handler, notifications go to every handler, and a pipeline of behaviors wraps each request.</p>
    <pre><code>dotnet add package Caesar.Abstractions  <span class="comment"># Application layer</span>
dotnet add package Caesar               <span class="comment"># composition root</span></code></pre>
    <nav aria-label="Caesar links">
      <a class="primary" href="latest/">Read the documentation</a>
      <a href="https://github.com/tsmshvenieradze/Caesar">GitHub</a>
      <a href="https://www.nuget.org/packages/Caesar">NuGet</a>
    </nav>
  </main>
  <footer>&copy; Tsezari Mshvenieradze &middot; <a href="https://tsezar.io">tsezar.io</a></footer>
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
    _write_if_changed(pages / "index.html", _LANDING)
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
