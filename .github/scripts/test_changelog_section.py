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

    def test_shorter_version_does_not_match_longer_heading(self):
        text = "## [10.1.10] - 2026-12-01\n\n- ten\n"
        self.assertIsNone(extract(text, "10.1.1"))
        self.assertIsNone(extract("## [10.1.10](https://example.test/v10.1.10) - 2026-12-01\n\n- ten\n", "10.1.1"))

    def test_linked_heading_with_date(self):
        text = "## [10.2.0](https://example.test/v10.2.0) - 2026-10-01\n\n- a\n\n## [10.1.1] - 2026-09-23\n\n- b\n"
        self.assertEqual(extract(text, "10.2.0"), "- a")

    def test_linked_heading_without_date(self):
        self.assertEqual(extract("## [10.2.0](https://example.test/v10.2.0)\n\n- a\n", "10.2.0"), "- a")

    def test_link_reference_inside_section_is_kept(self):
        text = "## [10.2.0] - 2026-10-01\n\n- fixed [#12]\n\n[#12]: https://example.test/12\n\n- more\n\n## [10.1.1] - 2026-09-23\n\n- b\n"
        self.assertEqual(extract(text, "10.2.0"), "- fixed [#12]\n\n[#12]: https://example.test/12\n\n- more")

    def test_trailing_link_references_without_space_are_dropped(self):
        text = "## [10.2.0] - 2026-10-01\n\n- a\n\n[10.2.0]:https://example.test/v10.2.0\n[Unreleased]: https://example.test/compare\n"
        self.assertEqual(extract(text, "10.2.0"), "- a")

    def test_section_heading_inside_code_fence_does_not_end_section(self):
        text = "## [10.2.0] - 2026-10-01\n\n- a\n\n```md\n## [x]\n```\n\n- b\n\n## [10.1.1] - 2026-09-23\n\n- c\n"
        self.assertEqual(extract(text, "10.2.0"), "- a\n\n```md\n## [x]\n```\n\n- b")

    def test_tilde_fence_and_longer_backtick_fence(self):
        text = "## [10.2.0]\n\n~~~\n## [10.1.1]\n~~~\n````\n```\n## [9.0.0]\n````\n- b\n## [10.1.1]\n\n- c\n"
        self.assertEqual(extract(text, "10.2.0"), "~~~\n## [10.1.1]\n~~~\n````\n```\n## [9.0.0]\n````\n- b")

    def test_heading_inside_code_fence_is_not_a_section_start(self):
        text = "## [10.2.0]\n\n```\n## [10.1.1] - fake\n```\n\n## [10.1.1] - 2026-09-23\n\n- real\n"
        self.assertEqual(extract(text, "10.1.1"), "- real")

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
