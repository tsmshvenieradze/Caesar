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
