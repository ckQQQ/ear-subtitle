"""Regression coverage for denied downloads and incomplete model archives."""
import io
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import urllib.error
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import backend


class DownloadTests(unittest.TestCase):
    def test_denied_source_falls_back_to_valid_archive(self):
        content = io.BytesIO()
        with zipfile.ZipFile(content, "w") as archive:
            archive.writestr("model/metadata.json", "{}")
        with tempfile.TemporaryDirectory() as temp:
            destination = Path(temp) / "en-zh.argosmodel"
            denied = urllib.error.HTTPError("https://example.invalid/a", 403, "Forbidden", {}, None)
            with patch("backend.urllib.request.urlopen", side_effect=[denied, io.BytesIO(content.getvalue())]) as fetch:
                backend.download_translation(["https://example.invalid/a", "https://example.invalid/b"], destination)
            self.assertEqual(fetch.call_count, 2)
            self.assertTrue(zipfile.is_zipfile(destination))
            self.assertFalse(destination.with_suffix(".part").exists())

    def test_html_error_response_is_not_installed_as_model(self):
        with tempfile.TemporaryDirectory() as temp:
            destination = Path(temp) / "en-zh.argosmodel"
            with patch("backend.urllib.request.urlopen", return_value=io.BytesIO(b"<html>Forbidden</html>")):
                with self.assertRaisesRegex(RuntimeError, "translate-en_zh-1_9"):
                    backend.download_translation(["https://example.invalid/a"], destination)
            self.assertFalse(destination.exists())


if __name__ == "__main__":
    unittest.main()
