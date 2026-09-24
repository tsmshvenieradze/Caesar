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
