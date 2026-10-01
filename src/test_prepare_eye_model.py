"""Offline checks for stock eye-model discovery and firmware provenance."""

from __future__ import annotations

import ast
import base64
import hashlib
import io
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
    GAZE_ENVIRONMENT_SCAN,
    MANIFEST_NAME,
    MODEL_ROOT,
    OVERLAYFS_ODM_UPPER,
    PATCHED_NAME,
    PreparationError,
    _discover_model,
    check_prepared,
    diagnose,
    main,
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
        self.modules: list[tuple[str, bool, dict[str, str]]] = []
        self.pending_removal: set[str] = set()
        self.experimental_property = "false"
        self.extra_mounts = ""
        self.overlay_upper_state = "absent"
        self.overlay_upper_path = OVERLAYFS_ODM_UPPER
        self.scan_output: str | None = None
        self.scan_error: PreparationError | None = None
        self.copy_count = 0
        self.commands: list[str] = []

    def getprop(self, name: str) -> str:
        return self.properties[name]

    def root(self, command: str, *, timeout: int = 20) -> str:
        self.commands.append(command)
        if command == "id":
            return "uid=0(root) gid=0(root)"
        if command == GAZE_ENVIRONMENT_SCAN:
            self.assert_scan_timeout = timeout
            if self.scan_error:
                raise self.scan_error
            if self.scan_output is not None:
                return self.scan_output
            output = ["QPRO_GAZE_SCAN_V1"]
            for directory, enabled, files in self.modules:
                output.extend([
                    f"QPRO_MODULE_BEGIN /data/adb/modules/{directory}",
                    f"QPRO_MODULE_ENABLED {int(enabled)}",
                    f"QPRO_MODULE_PENDING_REMOVE {int(directory in self.pending_removal)}",
                ])
                for filename, content in files.items():
                    output.extend([f"QPRO_FILE_BEGIN {filename}", content, "QPRO_FILE_END"])
                output.append("QPRO_MODULE_END")
            mounts = "/dev/block/dm-4 /odm ext4 ro 0 0"
            if self.mounted:
                mounts += f"\n/data/local/tmp/patch.ptl {MODEL_PATH} ext4 rw 0 0"
            if self.extra_mounts:
                mounts += "\n" + self.extra_mounts
            output.extend([
                "QPRO_MOUNTS_BEGIN", mounts, "QPRO_MOUNTS_END",
                "QPRO_OVERLAY_UPPER_PATH_BEGIN", self.overlay_upper_path, "QPRO_OVERLAY_UPPER_PATH_END",
                "QPRO_OVERLAY_UPPER_BEGIN", self.overlay_upper_state, "QPRO_OVERLAY_UPPER_END",
                "QPRO_EXPERIMENTAL_BEGIN", self.experimental_property,
                "QPRO_EXPERIMENTAL_END", "QPRO_GAZE_SCAN_COMPLETE",
            ])
            return "\n".join(output)
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
        self.copy_count += 1
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

    def test_unknown_reported_engine_stays_rejected_with_exact_build_details(self):
        self.quest.engine_size = 44_198_016
        self.quest.engine_hash = "96fdebc377b475df55d59f7added04c5014c4069aa1d636cf3d6f15d8fe27f1e"
        self.quest.properties["ro.build.version.incremental"] = "unknown-v24-build"
        self.quest.properties["ro.build.display.id"] = "unknown-v24-display"
        with self.assertRaises(PreparationError) as caught:
            prepare(self.quest, self.output_dir)
        message = str(caught.exception)
        for detail in (
            "44198016", self.quest.engine_hash, "unknown-v24-build", "unknown-v24-display",
            self.quest.properties["ro.build.fingerprint"], "leave Independent Eye Gaze off",
            "No headset tracking was changed", "this exact firmware",
        ):
            self.assertIn(detail, message)
        self.assertFalse((self.output_dir / PATCHED_NAME).exists())
        self.assertFalse((self.output_dir / MANIFEST_NAME).exists())
        self.assertNotIn(f"find {MODEL_ROOT} -type f -name bolt.ptl", self.quest.commands)

    def test_diagnostic_reports_unsupported_engine_without_private_device_ids(self):
        self.quest.engine_size = 44_198_016
        self.quest.engine_hash = "9" * 64
        report = diagnose(self.quest)
        self.assertFalse(report["engineSupported"])
        self.assertEqual(report["engine"]["size"], self.quest.engine_size)
        self.assertEqual(report["engine"]["sha256"], self.quest.engine_hash)
        self.assertEqual(report["modelPath"], MODEL_PATH)
        self.assertFalse(report["headsetTrackingChanged"])
        self.assertFalse(report["modelPatchValidated"])
        serialized = json.dumps(report)
        self.assertNotIn(self.quest.serial, serialized)
        self.assertNotIn(self.quest.properties["ro.boot.serialno"], serialized)
        self.assertEqual(list(self.output_dir.iterdir()), [])
        self.assertTrue(all(
            command == "id" or command.startswith(("stat -c %s ", "sha256sum ", "find ", "cat /proc/mounts"))
            or command == GAZE_ENVIRONMENT_SCAN
            for command in self.quest.commands
        ))

    def test_diagnostic_distinguishes_engine_support_from_model_readiness(self):
        self.quest.model_paths = [STANDARD_MODEL_PATH, PREVIOUS_MODEL_PATH]
        report = diagnose(self.quest)
        self.assertTrue(report["engineSupported"])
        self.assertEqual(report["engine"]["profile"], ENGINE_PROFILES[1]["profile"])
        self.assertIn("ambiguous", report["modelDiscoveryError"])
        self.assertIsNone(report["modelPath"])
        self.assertFalse(report["modelPatchValidated"])

    def test_cli_diagnostic_completes_without_enabling_unsupported_gaze(self):
        self.quest.engine_size = 44_198_016
        with mock.patch("prepare_eye_model.AdbClient", return_value=self.quest), mock.patch("sys.stdout", new_callable=io.StringIO) as output:
            self.assertEqual(main(["--diagnose", "--adb", "adb.exe"]), 0)
        report = json.loads(output.getvalue())
        self.assertFalse(report["engineSupported"])
        self.assertFalse(report["headsetTrackingChanged"])
        self.assertEqual(list(self.output_dir.iterdir()), [])

    def test_diagnostic_is_not_a_preparation_bypass(self):
        with mock.patch("sys.stderr", new_callable=io.StringIO) as output:
            with self.assertRaises(SystemExit) as caught:
                main(["--diagnose", "--check-prepared"])
        self.assertEqual(caught.exception.code, 2)
        self.assertIn("not allowed with argument", output.getvalue())

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

    def test_mount_blocks_preparation_and_preapply_check(self):
        prepare(self.quest, self.output_dir)
        self.quest.mounted = True
        self.quest.model = b"currently bind-mounted patched bytes"
        with self.assertRaisesRegex(PreparationError, "currently mounted over"):
            check_prepared(self.quest, self.output_dir)
        with self.assertRaisesRegex(PreparationError, "currently mounted over"):
            prepare(self.quest, self.output_dir)

    def test_active_known_magisk_gaze_module_blocks_even_with_property_false(self):
        prepare(self.quest, self.output_dir)
        self.quest.copy_count = 0
        self.quest.commands.clear()
        self.quest.modules = [(
            "questpro_independent_gaze", True,
            {"module.prop": "id=questpro_independent_gaze\nname=Quest Pro Gaze\nversion=1.1"},
        )]
        for operation in (prepare, check_prepared):
            with self.subTest(operation=operation.__name__):
                with self.assertRaisesRegex(PreparationError, "active Magisk gaze module"):
                    operation(self.quest, self.output_dir)
        self.assertEqual(self.quest.copy_count, 0)
        self.assertEqual(self.quest.experimental_property, "false")
        self.assertNotIn(f"sha256sum '{MODEL_PATH}'", self.quest.commands)

    def test_renamed_gaze_module_is_detected_from_metadata_and_policy(self):
        for files in (
            {"module.prop": "id=custom\nname=Independent Eye Gaze"},
            {"module.prop": "id=custom\nname=Custom model", "service.sh": "mount /data/local/tmp/bolt.ptl /odm/etc/eyetracking/model"},
            {"module.prop": "id=custom", "system.prop": "persist.device_config.oculus_shared_vision.oculus_eyetracking_enable_experimental_model=true"},
        ):
            with self.subTest(files=files):
                self.quest.modules = [("custom", True, files)]
                with self.assertRaisesRegex(PreparationError, "active Magisk gaze module"):
                    prepare(self.quest, self.output_dir)
        self.assertEqual(self.quest.copy_count, 0)

    def test_disabled_gaze_module_with_no_remaining_mounts_is_allowed(self):
        self.quest.modules = [(
            "questpro_independent_gaze", False,
            {"module.prop": "id=questpro_independent_gaze\nname=Independent Eye Gaze"},
        )]
        manifest = prepare(self.quest, self.output_dir)
        self.assertEqual(check_prepared(self.quest, self.output_dir), manifest)
        report = diagnose(self.quest)
        self.assertFalse(report["gazeEnvironment"]["modules"][0]["enabled"])
        self.assertTrue(report["gazePreflightPassed"])

    def test_pending_remove_gaze_module_is_still_active_until_disabled_or_rebooted(self):
        self.quest.modules = [("questpro_independent_gaze", True, {"module.prop": "id=questpro_independent_gaze"})]
        self.quest.pending_removal.add("questpro_independent_gaze")
        with self.assertRaisesRegex(PreparationError, "active Magisk gaze module"):
            prepare(self.quest, self.output_dir)
        report = diagnose(self.quest)
        module = report["gazeEnvironment"]["modules"][0]
        self.assertTrue(module["enabled"])
        self.assertTrue(module["pendingRemoval"])
        self.assertFalse(report["gazePreflightPassed"])
        self.assertEqual(self.quest.copy_count, 0)
        self.quest.modules = [("questpro_independent_gaze", False, {"module.prop": "id=questpro_independent_gaze"})]
        prepare(self.quest, self.output_dir)

    def test_disabled_module_still_blocks_until_its_overlay_is_removed(self):
        self.quest.modules = [(
            "questpro_independent_gaze", False,
            {"module.prop": "id=questpro_independent_gaze"},
        )]
        self.quest.extra_mounts = "overlay /odm overlay ro,lowerdir=/odm,upperdir=/data/overlay 0 0"
        with self.assertRaisesRegex(PreparationError, "reboot so its mounts are removed"):
            prepare(self.quest, self.output_dir)
        self.assertEqual(self.quest.copy_count, 0)

    def test_empty_readonly_magisk_odm_overlay_is_allowed_after_module_disabled(self):
        self.quest.modules = [(
            "questpro_independent_gaze", False,
            {"module.prop": "id=questpro_independent_gaze"},
        )]
        self.quest.overlay_upper_state = "empty"
        options = "ro,seclabel,relatime,lowerdir=/odm,upperdir=/dev/mount_overlayfs/upper/odm,workdir=/dev/mount_overlayfs/worker/64781/2"
        self.quest.extra_mounts = "\n".join(f"overlay {target} overlay {options} 0 0" for target in ("/odm/etc", "/odm/lib64"))
        manifest = prepare(self.quest, self.output_dir)
        self.assertEqual(check_prepared(self.quest, self.output_dir), manifest)
        report = diagnose(self.quest)
        self.assertTrue(report["gazePreflightPassed"])
        self.assertEqual(report["gazeEnvironment"]["relevantMounts"], [])
        self.assertEqual(len(report["gazeEnvironment"]["transparentOverlayMounts"]), 2)
        self.assertEqual(report["gazeEnvironment"]["overlayfsOdmUpperState"], "empty")

    def test_overlay_exception_never_bypasses_an_active_gaze_module(self):
        self.quest.modules = [("questpro_independent_gaze", True, {"module.prop": "id=questpro_independent_gaze"})]
        self.quest.overlay_upper_state = "empty"
        self.quest.extra_mounts = "overlay /odm/etc overlay ro,lowerdir=/odm,upperdir=/dev/mount_overlayfs/upper/odm,workdir=/dev/mount_overlayfs/worker/64781/2 0 0"
        with self.assertRaisesRegex(PreparationError, "active Magisk gaze module"):
            prepare(self.quest, self.output_dir)
        self.assertEqual(self.quest.copy_count, 0)

    def test_retained_magisk_upper_alias_can_prove_an_empty_overlay(self):
        self.quest.modules = [
            ("questpro_independent_gaze", False, {"module.prop": "id=questpro_independent_gaze"}),
            ("magisk_overlayfs", True, {"module.prop": "id=magisk_overlayfs\nname=Magisk OverlayFS"}),
        ]
        self.quest.overlay_upper_state = "empty"
        self.quest.extra_mounts = "overlay /odm/etc overlay ro,lowerdir=/odm,upperdir=/dev/mount_overlayfs/upper/odm,workdir=/dev/mount_overlayfs/worker/64781/2 0 0"
        for prefix in ("/debug_ramdisk", "/sbin"):
            with self.subTest(prefix=prefix):
                self.quest.overlay_upper_path = prefix + "/overlayfs_mnt/upper/odm"
                manifest = prepare(self.quest, self.output_dir)
                self.assertEqual(check_prepared(self.quest, self.output_dir), manifest)
                report = diagnose(self.quest)
                self.assertTrue(report["gazePreflightPassed"])
                self.assertEqual(report["gazeEnvironment"]["overlayfsOdmUpperInspectedPath"], self.quest.overlay_upper_path)

    def test_missing_or_nonempty_magisk_upper_alias_is_never_counted_empty(self):
        self.quest.modules = [("magisk_overlayfs", True, {"module.prop": "id=magisk_overlayfs"})]
        self.quest.overlay_upper_path = "/debug_ramdisk/overlayfs_mnt/upper/odm"
        self.quest.extra_mounts = "overlay /odm/lib64 overlay ro,lowerdir=/odm,upperdir=/dev/mount_overlayfs/upper/odm,workdir=/dev/mount_overlayfs/worker/64781/2 0 0"
        for state in ("absent", "notEmpty"):
            with self.subTest(state=state):
                self.quest.overlay_upper_state = state
                with self.assertRaisesRegex(PreparationError, "currently mounted over"):
                    prepare(self.quest, self.output_dir)
        self.assertEqual(self.quest.copy_count, 0)

    def test_alias_exception_requires_enabled_overlayfs_module(self):
        self.quest.overlay_upper_state = "empty"
        self.quest.overlay_upper_path = "/debug_ramdisk/overlayfs_mnt/upper/odm"
        self.quest.extra_mounts = "overlay /odm/etc overlay ro,lowerdir=/odm,upperdir=/dev/mount_overlayfs/upper/odm,workdir=/dev/mount_overlayfs/worker/64781/2 0 0"
        for modules in ([], [("magisk_overlayfs", False, {"module.prop": "id=magisk_overlayfs"})]):
            with self.subTest(modules=modules):
                self.quest.modules = modules
                with self.assertRaisesRegex(PreparationError, "currently mounted over"):
                    prepare(self.quest, self.output_dir)
        self.assertEqual(self.quest.copy_count, 0)

    def test_unknown_magisk_alias_and_path_traversal_refuse_preparation(self):
        self.quest.overlay_upper_state = "empty"
        for path in (
            "/data/local/tmp/overlayfs_mnt/upper/odm",
            "/debug_ramdisk/../data/overlayfs_mnt/upper/odm",
            "/debug_ramdisk/overlayfs_mnt/upper/odm/..",
        ):
            with self.subTest(path=path):
                self.quest.overlay_upper_path = path
                with self.assertRaisesRegex(PreparationError, "unrecognized Magisk temporary path"):
                    prepare(self.quest, self.output_dir)
        self.assertEqual(self.quest.copy_count, 0)

    def test_nonempty_unreadable_or_other_overlay_shape_stays_blocked(self):
        expected = "ro,lowerdir=/odm,upperdir=/dev/mount_overlayfs/upper/odm,workdir=/dev/mount_overlayfs/worker/64781/2"
        for upper_state, options in (
            ("notEmpty", expected), ("absent", expected),
            ("empty", expected.replace("lowerdir=/odm", "lowerdir=/dev/mount_loop/1/odm:/odm")),
            ("empty", expected.replace("ro,", "rw,")),
            ("empty", expected.replace("upper/odm", "upper/other")),
            ("empty", expected.replace("worker/64781/2", "worker/../2")),
            ("empty", expected + ",lowerdir=/data/gaze"),
        ):
            with self.subTest(upper_state=upper_state, options=options):
                self.quest.overlay_upper_state = upper_state
                self.quest.extra_mounts = f"overlay /odm/etc overlay {options} 0 0"
                with self.assertRaisesRegex(PreparationError, "currently mounted over"):
                    prepare(self.quest, self.output_dir)
        self.quest.overlay_upper_state = "unreadable"
        with self.assertRaisesRegex(PreparationError, "readable and complete"):
            prepare(self.quest, self.output_dir)
        self.assertEqual(self.quest.copy_count, 0)

    def test_relevant_model_engine_and_ancestor_mounts_block_preparation(self):
        for mount in (
            f"/data/model {STANDARD_MODEL_PATH} ext4 rw 0 0",
            f"/data/model {MODEL_ROOT} ext4 rw 0 0",
            f"/data/engine {ENGINE_PATH} ext4 rw 0 0",
            "/data/engine /odm/lib64 ext4 rw 0 0",
            "tmpfs /odm/etc tmpfs ro 0 0",
            "overlay / overlay ro 0 0",
            "tmpfs /odm tmpfs ro 0 0",
            "/dev/block/loop2 /odm ext4 ro 0 0",
        ):
            with self.subTest(mount=mount):
                self.quest.extra_mounts = mount
                with self.assertRaisesRegex(PreparationError, "currently mounted over"):
                    prepare(self.quest, self.output_dir)
        self.assertEqual(self.quest.copy_count, 0)

    def test_unrelated_magisk_module_and_system_overlay_are_diagnostic_only(self):
        self.quest.modules = [(
            "social_filtering0", True,
            {"module.prop": "id=social_filtering0\nname=Social filtering switch"},
        ), ("overlayfs", True, {"module.prop": "id=overlayfs\nname=Magisk OverlayFS"})]
        self.quest.extra_mounts = "overlay /system overlay rw 0 0"
        prepare(self.quest, self.output_dir)
        report = diagnose(self.quest)
        self.assertEqual([module["id"] for module in report["gazeEnvironment"]["modules"]], ["social_filtering0", "overlayfs"])
        self.assertTrue(report["gazePreflightPassed"])

    def test_enabled_experimental_property_blocks_without_magisk_module(self):
        self.quest.experimental_property = "true"
        with self.assertRaisesRegex(PreparationError, "selection is already enabled"):
            prepare(self.quest, self.output_dir)
        self.assertEqual(self.quest.copy_count, 0)

    def test_incomplete_or_unreadable_scan_refuses_both_preparation_and_preapply(self):
        prepare(self.quest, self.output_dir)
        complete = self.quest.root(GAZE_ENVIRONMENT_SCAN)
        for output in (
            "", complete.replace("QPRO_GAZE_SCAN_COMPLETE", ""),
            complete.replace("QPRO_MOUNTS_BEGIN", "MOUNTS_UNREADABLE"),
            complete.replace("/dev/block/dm-4 /odm ext4 ro 0 0", ""),
            complete.replace("/dev/block/dm-4 /odm ext4 ro 0 0", "Permission denied"),
            complete.replace("QPRO_EXPERIMENTAL_END", ""),
        ):
            self.quest.scan_output = output
            for operation in (prepare, check_prepared):
                with self.subTest(output=output, operation=operation.__name__):
                    with self.assertRaisesRegex(PreparationError, "readable and complete"):
                        operation(self.quest, self.output_dir)
        self.quest.scan_output = None
        self.quest.scan_error = PreparationError("Permission denied reading module.prop")
        with self.assertRaisesRegex(PreparationError, "Permission denied"):
            prepare(self.quest, self.output_dir)

    def test_diagnostic_reports_active_method_without_running_module_scripts(self):
        self.quest.modules = [(
            "questpro_independent_gaze", True,
            {"module.prop": "id=questpro_independent_gaze\nname=Independent Eye Gaze", "service.sh": "exit 99 # eyetracking"},
        )]
        report = diagnose(self.quest)
        self.assertTrue(report["engineSupported"])
        self.assertFalse(report["gazePreflightPassed"])
        self.assertTrue(report["gazeEnvironment"]["scanComplete"])
        self.assertTrue(report["gazeEnvironment"]["modules"][0]["enabled"])
        self.assertIn("service.sh", report["gazeEnvironment"]["modules"][0]["relevantFiles"])
        self.assertIn("active Magisk gaze module", report["gazeEnvironmentError"])
        self.assertFalse(report["headsetTrackingChanged"])
        self.assertEqual(self.quest.copy_count, 0)
        self.assertEqual(list(self.output_dir.iterdir()), [])
        self.assertEqual(self.quest.assert_scan_timeout, 20)
        self.assertNotIn("exit 99", json.dumps(report))

    def test_diagnostic_reports_scan_failure_instead_of_claiming_stock(self):
        self.quest.scan_error = PreparationError("ADB safety scan timed out")
        report = diagnose(self.quest)
        self.assertIsNone(report["gazeEnvironment"])
        self.assertFalse(report["gazePreflightPassed"])
        self.assertIn("timed out", report["gazeEnvironmentError"])

    def test_missing_module_metadata_cannot_be_treated_as_an_unrelated_module(self):
        self.quest.modules = [("custom", True, {"service.sh": "echo harmless"})]
        with self.assertRaisesRegex(PreparationError, "could not read a module's metadata"):
            prepare(self.quest, self.output_dir)
        self.assertEqual(self.quest.copy_count, 0)

    def test_method_becoming_active_during_copy_preserves_existing_patch(self):
        prepare(self.quest, self.output_dir)
        original_patch = (self.output_dir / PATCHED_NAME).read_bytes()
        original_manifest = (self.output_dir / MANIFEST_NAME).read_bytes()
        original_copy = self.quest.copy_model

        def copy_then_enable(remote_path, destination):
            original_copy(remote_path, destination)
            self.quest.modules = [("questpro_independent_gaze", True, {"module.prop": "id=questpro_independent_gaze"})]

        with mock.patch.object(self.quest, "copy_model", side_effect=copy_then_enable):
            with self.assertRaisesRegex(PreparationError, "active Magisk gaze module"):
                prepare(self.quest, self.output_dir)
        self.assertEqual((self.output_dir / PATCHED_NAME).read_bytes(), original_patch)
        self.assertEqual((self.output_dir / MANIFEST_NAME).read_bytes(), original_manifest)

    def test_adb_adapter_selects_target_and_streams_binary_model(self):
        calls = []
        model = self.quest.model

        def fake_run(command, **kwargs):
            calls.append(command)
            if command[1:] == ["devices"]:
                return subprocess.CompletedProcess(command, 0, b"List of devices attached\r\nQUESTPRO123\tdevice\r\n", b"")
            if command[3] == "shell":
                encoded = command[4].removeprefix("printf %s ").removesuffix(" | base64 -d | su -c sh")
                if base64.b64decode(encoded).decode("utf-8") == "id":
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

    def test_adb_root_transport_preserves_compound_quotes_and_normalizes_line_endings(self):
        queries = [
            "id", "printf '%s\\n' \"two words: '$value'\"\r\ncat '/proc/mounts'\r",
            "printf '%s' 'non-ASCII: café'\n" + GAZE_ENVIRONMENT_SCAN,
        ]
        calls = []

        def fake_run(command, **kwargs):
            calls.append((command, kwargs))
            self.assertEqual(command[:4], ["adb.exe", "-s", "QUESTPRO123", "shell"])
            self.assertEqual(len(command), 5)
            request = command[4]
            self.assertTrue(request.startswith("printf %s "))
            self.assertTrue(request.endswith(" | base64 -d | su -c sh"))
            payload = request.removeprefix("printf %s ").removesuffix(" | base64 -d | su -c sh")
            self.assertRegex(payload, r"^[A-Za-z0-9+/=]+$")
            decoded = base64.b64decode(payload, validate=True).decode("utf-8")
            expected = queries[len(calls) - 1].replace("\r\n", "\n").replace("\r", "\n")
            self.assertEqual(decoded, expected)
            return subprocess.CompletedProcess(command, 0, b"query complete\n", b"")

        client = AdbClient("adb.exe", runner=fake_run, serial="QUESTPRO123")
        for query in queries:
            self.assertEqual(client.root(query, timeout=17), "query complete")
        self.assertTrue(all(kwargs["timeout"] == 17 for _, kwargs in calls))

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
