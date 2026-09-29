import hashlib
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parent
EXPECTED_PYTHON_SHA256 = "0eb85c2dfccccf1b17352de4c397f69194035b7d37149eacc16f1147d93de3b8"


class ReleaseBootstrapTests(unittest.TestCase):
    def test_bundled_python_archive_matches_pinned_hash(self) -> None:
        archive = ROOT / "artifacts" / "python-package" / "python.3.12.10.nupkg"
        if not archive.is_file():
            self.skipTest("The official Python archive is a release asset, not a Git-tracked source file")
        self.assertEqual(hashlib.sha256(archive.read_bytes()).hexdigest(), EXPECTED_PYTHON_SHA256)

    def test_runtime_setup_is_private_and_does_not_require_path_python(self) -> None:
        source = (ROOT / "setup-runtime.ps1").read_text(encoding="utf-8")
        self.assertIn('Install-QproPrivatePython', source)
        self.assertIn('ExtractToDirectory', source)
        self.assertNotIn('Start-Process -FilePath $bundledPythonInstaller', source)
        self.assertIn('python-runtime\\python.3.12.10.nupkg', source)
        self.assertIn(EXPECTED_PYTHON_SHA256, source)
        self.assertIn('runtime-ready.json', source)
        self.assertIn('Test-PythonCommand', source)

    def test_release_builder_and_self_test_require_the_bootstrap(self) -> None:
        builder = (ROOT / "build-release.ps1").read_text(encoding="utf-8")
        hub = "\n".join(path.read_text(encoding="utf-8") for path in (ROOT / "qpro-hub").glob("*.cs"))
        self.assertIn('python.3.12.10.nupkg', builder)
        self.assertIn('python-runtime\\\\python.3.12.10.nupkg', hub)
        self.assertIn('runtime-ready.json', hub)

    def test_hub_has_setup_progress_and_actionable_gaze_preflight(self) -> None:
        hub = "\n".join(path.read_text(encoding="utf-8") for path in (ROOT / "qpro-hub").glob("*.cs"))
        for expected in (
            "Setup progress",
            "IsIndeterminate",
            "This can take several minutes",
            "Quest Pro not found over ADB",
            "Quest Pro root access is unavailable",
            "grant Superuser access to Shell / ADB Shell",
        ):
            self.assertIn(expected, hub)


if __name__ == "__main__":
    unittest.main()
