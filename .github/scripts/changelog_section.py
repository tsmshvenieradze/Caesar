"""Print the body of one version's section from a Keep a Changelog file (used as GitHub Release notes)."""

from __future__ import annotations

import re
import sys
from pathlib import Path

_ANY_SECTION = re.compile(r"^## \[")
_LINK_REFERENCE = re.compile(r"^\[[^\]]+\]:")
_FENCE = re.compile(r"^ {0,3}(`{3,}|~{3,})")


def _outside_fences(lines: list[str]) -> list[bool]:
    """For each line, whether it is outside a fenced code block (fence lines themselves count as inside)."""
    result: list[bool] = []
    fence: str | None = None
    for line in lines:
        match = _FENCE.match(line)
        if fence is None:
            if match:
                fence = match.group(1)
                result.append(False)
            else:
                result.append(True)
        else:
            result.append(False)
            closing = re.match(rf"^ {{0,3}}{re.escape(fence[0])}{{{len(fence)},}}\s*$", line)
            if closing:
                fence = None
    return result


def extract(text: str, version: str) -> str | None:
    # "## [X.Y.Z]", optionally linked "## [X.Y.Z](url)", then a space (e.g. " - date") or the end of the line.
    heading = re.compile(rf"^## \[{re.escape(version)}\](\([^)]*\))?(\s|$)")
    lines = text.splitlines()
    outside = _outside_fences(lines)
    start = next((i for i, line in enumerate(lines) if outside[i] and heading.match(line)), None)
    if start is None:
        return None

    body: list[str] = []
    reached_end_of_file = True
    for i in range(start + 1, len(lines)):
        if outside[i] and _ANY_SECTION.match(lines[i]):
            reached_end_of_file = False
            break
        body.append(lines[i])

    if reached_end_of_file:
        # The last section is followed by the file's link reference definitions ([X.Y.Z]: url), which are not
        # part of it. References inside a section, or before the next heading, are kept so its links still resolve.
        while body and (not body[-1].strip() or _LINK_REFERENCE.match(body[-1])):
            body.pop()

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
