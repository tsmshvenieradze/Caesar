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
