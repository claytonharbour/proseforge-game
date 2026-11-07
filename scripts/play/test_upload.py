#!/usr/bin/env python3
"""Offline tests for upload.py's refusal guards.

⛔ These exist because every guard in this file protects against a SILENT success: Play accepts
   images for a locale that is not on the listing by creating one, so the wrong-locale run
   commits cleanly, prints ✅, and changes nothing a user can see. A guard against that is
   worthless until it has been watched to fire.

Run: scripts/play/.venv/bin/python scripts/play/test_upload.py
"""
import os, sys, tempfile, unittest
from unittest import mock

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import upload


def png_dir(n):
    """A directory of n files that pass the extension and count checks."""
    d = tempfile.mkdtemp()
    for i in range(n):
        with open(os.path.join(d, f"{i:02d}.png"), "wb") as f:
            f.write(b"\x89PNG\r\n\x1a\n" + b"\0" * 100)
    return d


class LocaleGuard(unittest.TestCase):
    """The guard added after the en-CA default would have written to a new empty locale."""

    def _run(self, language, listed):
        """Drive cmd_images with the network stubbed; return every API call it made."""
        calls = []

        def fake_call(method, url, token, body=None, data=None, content_type=None):
            calls.append((method, url))
            if url.endswith("/edits"):
                return {"id": "EDIT1"}
            if url.endswith("/listings"):
                return {"listings": [{"language": L} for L in listed]}
            return {}

        with mock.patch.object(upload, "call", fake_call), \
             mock.patch.object(upload, "access_token", lambda p: "TOK"), \
             mock.patch.dict(os.environ, {"GOOGLE_PLAY_SERVICE_ACCOUNT_JSON": "/dev/null"}):
            try:
                upload.cmd_images(png_dir(3), "phoneScreenshots", language, dry_run=False)
            except SystemExit as e:
                return calls, e.code
        return calls, 0

    def test_unknown_locale_refuses_before_any_delete(self):
        calls, code = self._run("en-CA", listed=["en-US"])
        self.assertEqual(code, 1, "must exit non-zero")
        deletes = [u for m, u in calls if m == "DELETE" and "phoneScreenshots" in u]
        self.assertEqual(deletes, [], "must not clear the listing for a locale it refused")
        self.assertFalse([u for m, u in calls if u.endswith(":commit")],
                         "must not commit")

    def test_known_locale_proceeds_through_delete_and_commit(self):
        # ⚑ THE NEGATIVE CONTROL. Without this, a guard that refuses EVERYTHING passes the test
        #    above and the tool never works — the failure the first test cannot distinguish.
        calls, code = self._run("en-US", listed=["en-US"])
        self.assertEqual(code, 0)
        self.assertTrue([u for m, u in calls if m == "DELETE" and "phoneScreenshots" in u],
                        "the happy path must still clear before uploading")
        self.assertTrue([u for m, u in calls if u.endswith(":commit")],
                        "the happy path must commit")


class CountGuard(unittest.TestCase):
    def test_one_screenshot_is_refused(self):
        with self.assertRaises(SystemExit):
            upload.cmd_images(png_dir(1), "phoneScreenshots", "en-US", dry_run=True)

    def test_empty_directory_is_refused(self):
        with self.assertRaises(SystemExit):
            upload.cmd_images(png_dir(0), "phoneScreenshots", "en-US", dry_run=True)

    def test_two_is_enough(self):
        upload.cmd_images(png_dir(2), "phoneScreenshots", "en-US", dry_run=True)  # must not raise


if __name__ == "__main__":
    unittest.main(verbosity=2, buffer=True)
