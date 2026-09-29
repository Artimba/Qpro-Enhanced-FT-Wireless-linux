"""Offline checks for stock eye-model discovery and firmware provenance."""

from __future__ import annotations

import ast
import hashlib
import json
import os
import re
import subprocess
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock

from prepare_eye_model import (
    AdbClient,
    ENGINE_PATH,
    ENGINE_PROFILES,
    EXPERIMENTAL_MODEL_PATH,
    MANIFEST_NAME,
    MODEL_ROOT,
    PATCHED_NAME,
    PreparationError,
    _discover_model,
    check_prepared,
    prepare,
)


MODEL_PATH = EXPERIMENTAL_MODEL_PATH
STANDARD_MODEL_PATH = f"{MODEL_ROOT}/Seacliff_V1_5/fbnet/int8/bolt/bolt.ptl"
PREVIOUS_MODEL_PATH = f"{MODEL_ROOT}/Seacliff_V1_5/fbnet/int8/vPrevious/bolt/bolt.ptl"


def stock_archive() -> bytes:
    graph = {
        "version": "HEXAGON synthetic test graph",
        "node": [
            {"id": 18, "output": [{"shape": [2, 2]}]},
            {
                "id": 52,
                "name": "211_reshape",
                "op": "OP_Reshape",
                "padding": "NN_PAD_NA",
                "input": [[50, 0], [51, 0]],
                "output": [{"shape": [1, 4]}],
            },
        ],
        "output": [
            {"shape": [1, 4]},
            {"shape": [1, 6]},
            {"shape": [1, 6]},
            {"shape": [1, 1]},
            {"shape": [1, 1]},
        ],
    }
    with tempfile.TemporaryDirectory() as directory:
        archive_path = Path(directory) / "stock.ptl"
        with zipfile.ZipFile(archive_path, "w") as archive:
            archive.writestr("model/data.pkl", b"pickle-prefix" + json.dumps(graph).encode())
            archive.writestr("model/weights.bin", b"w" * 101_000)
        return archive_path.read_bytes()


class FakeQuest:
    def __init__(self):
        self.serial = "QUESTPRO123"
        self.properties = {
            "ro.boot.serialno": "PHYSICALQUEST123",
            "ro.serialno": "PHYSICALQUEST123",
            "ro.product.model": "Quest Pro",
            "ro.product.device": "seacliff",
            "ro.build.fingerprint": "meta/seacliff/51503870024400340:user/release-keys",
            "ro.build.version.incremental": "51503870024400340",
            "ro.build.display.id": "51503870024400340",
        }
        self.engine_size = ENGINE_PROFILES[1]["size"]
        self.engine_hash = ENGINE_PROFILES[1]["sha256"]
        self.model_paths = [MODEL_PATH]
        self.model = stock_archive()
        self.mounted = False

    def getprop(self, name: str) -> str:
        return self.properties[name]

    def root(self, command: str, *, timeout: int = 20) -> str:
        if command == "id":
            return "uid=0(root) gid=0(root)"
        if command == f"stat -c %s '{ENGINE_PATH}'":
            return str(self.engine_size)
        if command == f"sha256sum '{ENGINE_PATH}'":
            return f"{self.engine_hash}  {ENGINE_PATH}"
        if command == f"find {MODEL_ROOT} -type f -name bolt.ptl":
            return "\n".join(self.model_paths)
        if command == "cat /proc/mounts":
            return f"/data/local/tmp/patch.ptl {MODEL_PATH} ext4 rw 0 0" if self.mounted else ""
        if command == f"sha256sum '{MODEL_PATH}'":
            return f"{hashlib.sha256(self.model).hexdigest()}  {MODEL_PATH}"
        raise AssertionError(f"Unexpected root command: {command}")

    def copy_model(self, remote_path: str, destination: Path) -> None:
        assert remote_path == MODEL_PATH
        destination.write_bytes(self.model)


class PrepareEyeModelTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.output_dir = Path(self.temp.name)
        self.quest = FakeQuest()

    def test_prepares_from_stock_and_writes_provenance(self):
        manifest = prepare(self.quest, self.output_dir)
        self.assertEqual(manifest["modelPath"], MODEL_PATH)
        self.assertEqual(manifest["device"]["serial"], self.quest.serial)
        self.assertEqual(manifest["engine"]["sha256"], self.quest.engine_hash)
        self.assertEqual(manifest["sourceSha256"], hashlib.sha256(self.quest.model).hexdigest())
        self.assertTrue((self.output_dir / PATCHED_NAME).is_file())
        self.assertEqual(
            json.loads((self.output_dir / MANIFEST_NAME).read_text()), manifest
        )
        self.assertEqual(check_prepared(self.quest, self.output_dir), manifest)

    def test_rejects_ambiguous_discovery_without_writing_model(self):
        self.quest.model_paths = [STANDARD_MODEL_PATH, PREVIOUS_MODEL_PATH]
        with self.assertRaisesRegex(PreparationError, "ambiguous"):
            prepare(self.quest, self.output_dir)
        self.assertFalse((self.output_dir / PATCHED_NAME).exists())

    def test_selects_experimental_target_from_three_firmware_models(self):
        # Current Seacliff builds contain normal, experimental, and vPrevious
        # models. Qpro enables the experimental branch when applying its patch.
        self.quest.model_paths = [PREVIOUS_MODEL_PATH, STANDARD_MODEL_PATH, MODEL_PATH]
        manifest = prepare(self.quest, self.output_dir)
        self.assertEqual(manifest["modelPath"], MODEL_PATH)
        self.assertEqual(check_prepared(self.quest, self.output_dir), manifest)

    def test_rejects_unknown_extra_branch_even_with_experimental_model(self):
        self.quest.model_paths = [MODEL_PATH, f"{MODEL_ROOT}/Seacliff_V1_5/other/bolt/bolt.ptl"]
        with self.assertRaisesRegex(PreparationError, "ambiguous"):
            prepare(self.quest, self.output_dir)

    def test_single_legacy_model_path_stays_eligible(self):
        self.quest.model_paths = [STANDARD_MODEL_PATH]
        self.assertEqual(_discover_model(self.quest), STANDARD_MODEL_PATH)

    def test_discovery_target_matches_headset_launcher(self):
        launcher = (Path(__file__).parent / "native-eye-local-branch-test.ps1").read_text(encoding="utf-8")
        match = re.search(r'^\$targetModel = "([^"]+)"', launcher, flags=re.MULTILINE)
        self.assertIsNotNone(match)
        self.assertEqual(match.group(1), EXPERIMENTAL_MODEL_PATH)

    def test_rejects_changed_engine_hash(self):
        self.quest.engine_hash = "a" * 64
        with self.assertRaisesRegex(PreparationError, "differs from supported"):
            prepare(self.quest, self.output_dir)

    def test_rejects_old_size_on_unknown_build(self):
        self.quest.engine_size = ENGINE_PROFILES[0]["size"]
        with self.assertRaisesRegex(PreparationError, "reported build is not"):
            prepare(self.quest, self.output_dir)

    def test_old_size_profile_requires_known_build_and_pins_actual_hash(self):
        self.quest.engine_size = ENGINE_PROFILES[0]["size"]
        self.quest.engine_hash = "b" * 64
        self.quest.properties["ro.build.version.incremental"] = "51483620027600340"
        manifest = prepare(self.quest, self.output_dir)
        self.assertEqual(manifest["engine"]["sha256"], "b" * 64)
        self.assertEqual(check_prepared(self.quest, self.output_dir), manifest)

    def test_preflight_detects_changed_device_firmware_and_model(self):
        prepare(self.quest, self.output_dir)
        self.quest.properties["ro.build.version.incremental"] = "different-build"
        with self.assertRaisesRegex(PreparationError, "firmware build differs"):
            check_prepared(self.quest, self.output_dir)
        self.quest.properties["ro.build.version.incremental"] = "51503870024400340"
        self.quest.model = self.quest.model + b"changed"
        with self.assertRaisesRegex(PreparationError, "stock eye model differs"):
            check_prepared(self.quest, self.output_dir)

    def test_usb_to_wireless_requires_same_hardware_serial(self):
        prepare(self.quest, self.output_dir)
        self.quest.serial = "192.0.2.20:5555"
        check_prepared(self.quest, self.output_dir)
        self.quest.properties["ro.boot.serialno"] = "DIFFERENTQUEST"
        with self.assertRaisesRegex(PreparationError, "connected Quest Pro"):
            check_prepared(self.quest, self.output_dir)

    def test_unknown_boot_serial_uses_secondary_hardware_serial(self):
        self.quest.properties["ro.boot.serialno"] = "unknown"
        self.quest.properties["ro.serialno"] = "QUESTPRO123"
        manifest = prepare(self.quest, self.output_dir)
        self.assertEqual(manifest["device"]["hardwareSerial"], "QUESTPRO123")
        self.quest.serial = "192.0.2.20:5555"
        check_prepared(self.quest, self.output_dir)

    def test_missing_hardware_serial_requires_same_adb_target(self):
        self.quest.properties["ro.boot.serialno"] = ""
        self.quest.properties["ro.serialno"] = ""
        prepare(self.quest, self.output_dir)
        check_prepared(self.quest, self.output_dir)
        self.quest.serial = "192.0.2.20:5555"
        with self.assertRaisesRegex(PreparationError, "connected Quest Pro"):
            check_prepared(self.quest, self.output_dir)

    def test_transient_missing_hardware_serial_keeps_same_adb_target(self):
        prepare(self.quest, self.output_dir)
        self.quest.properties["ro.boot.serialno"] = ""
        self.quest.properties["ro.serialno"] = ""
        check_prepared(self.quest, self.output_dir)
        self.quest.serial = "192.0.2.20:5555"
        with self.assertRaisesRegex(PreparationError, "connected Quest Pro"):
            check_prepared(self.quest, self.output_dir)

    def test_preflight_detects_modified_local_patch(self):
        prepare(self.quest, self.output_dir)
        with (self.output_dir / PATCHED_NAME).open("ab") as output:
            output.write(b"changed")
        with self.assertRaisesRegex(PreparationError, "changed since preparation"):
            check_prepared(self.quest, self.output_dir)

    def test_mount_blocks_preparation_and_skips_stock_hash_in_preflight(self):
        prepare(self.quest, self.output_dir)
        self.quest.mounted = True
        self.quest.model = b"currently bind-mounted patched bytes"
        check_prepared(self.quest, self.output_dir)
        with self.assertRaisesRegex(PreparationError, "currently mounted over"):
            prepare(self.quest, self.output_dir)

    def test_adb_adapter_selects_target_and_streams_binary_model(self):
        calls = []
        model = self.quest.model

        def fake_run(command, **kwargs):
            calls.append(command)
            if command[1:] == ["devices"]:
                return subprocess.CompletedProcess(command, 0, b"List of devices attached\r\nQUESTPRO123\tdevice\r\n", b"")
            if command[3:6] == ["shell", "su", "-c"] and command[-1] == "id":
                return subprocess.CompletedProcess(command, 0, b"uid=0(root)\n", b"")
            if command[3:6] == ["exec-out", "su", "-c"] and command[-1] == f"cat '{MODEL_PATH}'":
                kwargs["stdout"].write(model)
                return subprocess.CompletedProcess(command, 0, None, b"")
            raise AssertionError(f"Unexpected ADB invocation: {command}")

        with mock.patch.dict(os.environ, {"ANDROID_SERIAL": ""}):
            client = AdbClient("adb", runner=fake_run)
        self.assertEqual(client.serial, "QUESTPRO123")
        self.assertIn("uid=0(root)", client.root("id"))
        destination = self.output_dir / "downloaded.ptl"
        client.copy_model(MODEL_PATH, destination)
        self.assertEqual(destination.read_bytes(), model)
        self.assertEqual(calls[-1][1:4], ["-s", "QUESTPRO123", "exec-out"])

    def test_profile_gate_stays_aligned_with_native_eye_probe(self):
        native_source = (Path(__file__).parent / "native_raw_eye_probe.py").read_text(encoding="utf-8")
        module = ast.parse(native_source)
        assignment = next(
            node for node in module.body
            if isinstance(node, ast.Assign)
            and any(isinstance(target, ast.Name) and target.id == "ENGINE_PROFILES" for target in node.targets)
        )
        native_profiles = [
            (
                ast.literal_eval(call.args[0]),
                ast.literal_eval(call.args[3]) if len(call.args) > 3 else next(
                    (ast.literal_eval(item.value) for item in call.keywords if item.arg == "sha256"),
                    None,
                ),
            )
            for call in assignment.value.elts
        ]
        preparation_profiles = [(item["size"], item["sha256"]) for item in ENGINE_PROFILES]
        self.assertEqual(preparation_profiles, native_profiles)


if __name__ == "__main__":
    unittest.main()
