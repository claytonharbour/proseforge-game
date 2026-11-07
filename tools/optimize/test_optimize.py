"""Tests for the release picture optimizer. Run: python3 -m unittest discover tools/optimize"""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import optimize as o  # noqa: E402


class Fake:
    """Stands in for cwebp: writes a small file and counts what it converted."""
    def __init__(self):
        self.calls = []

    def __call__(self, src, dst, quality):
        self.calls.append(Path(src).name)
        Path(dst).write_bytes(b"RIFF....WEBP" + Path(src).read_bytes()[:4])


class OptimizeTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        self.src, self.out = root / "src", root / "out"
        pack = self.src / "themes/bake"
        (pack / "art").mkdir(parents=True)
        (pack / "art/bg.png").write_bytes(b"PNG-background")
        (pack / "art/icon.png").write_bytes(b"PNG-icon")
        (pack / "audio").mkdir()
        (pack / "audio/hi.wav").write_bytes(b"RIFF-wave")
        (pack / "theme.json").write_text(json.dumps(
            {"background": "art/bg.png", "powerUps": [{"icon": "art/icon.png"}, {"icon": "art/gone.png"}]}))
        (self.src / "gallery/images").mkdir(parents=True)
        (self.src / "gallery/images/a.webp").write_bytes(b"RIFF-gallery")
        (self.src / "gallery/index.json").write_text("{}")

    def tearDown(self):
        self.tmp.cleanup()

    def run_build(self):
        fake = Fake()
        o.build(self.src, self.out, convert=fake)
        return fake

    def test_converts_pngs_copies_the_rest_and_rewrites_paths(self):
        fake = self.run_build()
        self.assertEqual(sorted(fake.calls), ["bg.png", "icon.png"])
        self.assertTrue((self.out / "themes/bake/art/bg.webp").exists())
        self.assertFalse((self.out / "themes/bake/art/bg.png").exists())
        self.assertEqual((self.out / "themes/bake/audio/hi.wav").read_bytes(), b"RIFF-wave")
        self.assertTrue((self.out / "gallery/images/a.webp").exists())
        theme = json.loads((self.out / "themes/bake/theme.json").read_text())
        self.assertEqual(theme["background"], "art/bg.webp")
        self.assertEqual(theme["powerUps"][0]["icon"], "art/icon.webp")
        # A picture with no master stays as written: the theme check reports it, a placeholder shows.
        self.assertEqual(theme["powerUps"][1]["icon"], "art/gone.png")
        self.assertEqual(o.check(self.src, self.out), [])

    def test_an_unchanged_master_is_not_converted_again(self):
        self.run_build()
        self.assertEqual(self.run_build().calls, [])

    def test_a_changed_master_is_converted_again_even_with_an_older_timestamp(self):
        self.run_build()
        (self.src / "themes/bake/art/icon.png").write_bytes(b"PNG-icon-v2")
        self.assertIn("changed since the last optimize", " ".join(o.check(self.src, self.out)))
        self.assertEqual(self.run_build().calls, ["icon.png"])
        self.assertEqual(o.check(self.src, self.out), [])

    def test_a_deleted_master_removes_its_staged_copy(self):
        self.run_build()
        (self.src / "themes/bake/art/bg.png").unlink()
        self.assertIn("was deleted", " ".join(o.check(self.src, self.out)))
        self.run_build()
        self.assertFalse((self.out / "themes/bake/art/bg.webp").exists())

    def test_check_catches_a_new_master_a_missing_output_and_a_stray_file(self):
        self.run_build()
        (self.src / "themes/bake/art/new.png").write_bytes(b"PNG-new")
        (self.out / "themes/bake/art/icon.webp").unlink()
        (self.out / "themes/bake/stray.txt").write_text("?")
        problems = " ".join(o.check(self.src, self.out))
        self.assertIn("new.png is new", problems)
        self.assertIn("icon.webp is missing", problems)
        self.assertIn("stray.txt isn't from any master", problems)

    def test_check_fails_with_no_staged_copy_at_all(self):
        self.assertIn("missing or unreadable", o.check(self.src, self.out)[0])

    def test_a_staged_theme_still_naming_a_png_fails(self):
        self.run_build()
        (self.out / "themes/bake/theme.json").write_text(json.dumps({"background": "art/bg.png"}))
        self.assertIn("still names art/bg.png", " ".join(o.check(self.src, self.out)))

    def test_the_budget_catches_one_big_picture_and_a_big_total(self):
        self.run_build()
        (self.out / "themes/bake/art/bg.webp").write_bytes(b"x" * 3000)
        self.assertIn("over the 2 KB budget", " ".join(o.check(self.src, self.out, max_picture_kb=2)))
        self.assertIn("over the 0.001 MB budget", " ".join(o.check(self.src, self.out, max_total_mb=0.001)))

    def test_it_refuses_an_output_folder_it_did_not_make(self):
        self.out.mkdir()
        (self.out / "coverage.xml").write_text("someone else's work")
        with self.assertRaises(SystemExit):
            o.build(self.src, self.out, convert=Fake())
        self.assertTrue((self.out / "coverage.xml").exists())

    def test_someone_elses_manifest_doesnt_make_the_folder_ours(self):
        self.out.mkdir()
        (self.out / "manifest.json").write_text('{"name": "some web app"}')
        (self.out / "index.html").write_text("someone else's work")
        with self.assertRaises(SystemExit):
            o.build(self.src, self.out, convert=Fake())
        self.assertTrue((self.out / "index.html").exists())

    def test_a_new_quality_converts_everything_again(self):
        self.run_build()
        fake = Fake()
        o.build(self.src, self.out, quality=50, convert=fake)
        self.assertEqual(sorted(fake.calls), ["bg.png", "icon.png"])


if __name__ == "__main__":
    unittest.main()
