#!/usr/bin/env python3
"""Prepare and verify an independent-eye patch from this Quest Pro's stock model.

Only the two engine profiles already supported by native_raw_eye_probe.py are
eligible. No firmware, engine binary, or stock model is bundled with the app.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
import zipfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from research.patch_seacliff_independent_axes import patch


MODEL_ROOT = "/odm/etc/eyetracking/runtime/models"
ENGINE_PATH = "/odm/lib64/libtrackingengines.so"
PATCHED_NAME = "bolt-independent-axes.ptl"
MANIFEST_NAME = "bolt-independent-axes.manifest.json"
OUTPUT_DIR = Path(__file__).resolve().parent / "research" / "seacliff_eye_model"
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
MODEL_PATH_RE = re.compile(
    r"^/odm/etc/eyetracking/runtime/models/Seacliff_V1_5/"
    r"[A-Za-z0-9_./-]*/bolt/bolt\.ptl$"
)

# Keep these sizes and the pinned hash aligned with native_raw_eye_probe.py.
ENGINE_PROFILES = (
    {"profile": "51483620027600340", "size": 47_724_232, "sha256": None},
    {
        "profile": "51503870024400340",
        "size": 47_418_280,
        "sha256": "0fb6f54a3e190bec791d757ea18d32a8ecc1af4a861992d04b1703c93293cd03",
    },
)


class PreparationError(RuntimeError):
    """A safe, user-facing preparation or preflight failure."""


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _decode(output: bytes | str | None) -> str:
    if isinstance(output, bytes):
        return output.decode("utf-8", errors="replace").strip()
    return (output or "").strip()


class AdbClient:
    def __init__(self, adb: str, *, runner: Any = subprocess.run, serial: str | None = None):
        self.adb = adb
        self.runner = runner
        self.serial = serial or self._select_device()

    def _run(
        self, arguments: list[str], *, timeout: int = 20, output_file: Any = None
    ) -> str:
        command = [self.adb, *arguments]
        try:
            result = self.runner(
                command,
                stdout=output_file if output_file is not None else subprocess.PIPE,
                stderr=subprocess.PIPE,
                timeout=timeout,
                check=False,
                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
            )
        except (OSError, subprocess.TimeoutExpired) as error:
            raise PreparationError(f"ADB did not complete {' '.join(arguments[:3])}: {error}") from error
        if result.returncode != 0:
            detail = _decode(result.stderr) or _decode(result.stdout)
            raise PreparationError(f"Headset ADB command failed ({' '.join(arguments[:3])}): {detail}")
        return _decode(result.stdout)

    def _select_device(self) -> str:
        output = self._run(["devices"], timeout=10)
        devices: dict[str, str] = {}
        for line in output.splitlines():
            match = re.match(r"^([^\s]+)\s+(device|offline|unauthorized)(?:\s|$)", line.strip())
            if match:
                devices[match.group(1)] = match.group(2)
        requested = os.environ.get("ANDROID_SERIAL", "").strip()
        if requested:
            if devices.get(requested) != "device":
                raise PreparationError(
                    f"ADB target {requested!r} is not connected and authorized."
                )
            return requested
        authorized = [serial for serial, state in devices.items() if state == "device"]
        if len(authorized) != 1:
            raise PreparationError(
                "Connect exactly one authorized rooted Quest Pro, or select one with ANDROID_SERIAL."
            )
        return authorized[0]

    def root(self, command: str, *, timeout: int = 20) -> str:
        return self._run(["-s", self.serial, "shell", "su", "-c", command], timeout=timeout)

    def getprop(self, name: str) -> str:
        return self.root(f"getprop {name}").strip()

    def copy_model(self, remote_path: str, destination: Path) -> None:
        with destination.open("wb") as output:
            self._run(
                ["-s", self.serial, "exec-out", "su", "-c", f"cat '{remote_path}'"],
                timeout=120,
                output_file=output,
            )


def _read_hash(client: AdbClient, path: str) -> str:
    output = client.root(f"sha256sum '{path}'")
    value = output.split()[0].lower() if output.split() else ""
    if not SHA256_RE.fullmatch(value):
        raise PreparationError(f"Could not read a valid SHA-256 for {path}.")
    return value


def _device_identity(client: AdbClient) -> dict[str, str]:
    hardware_serial = client.getprop("ro.boot.serialno")
    if not hardware_serial or hardware_serial.lower() in {"unknown", "none", "null"}:
        hardware_serial = client.getprop("ro.serialno")
    if hardware_serial.lower() in {"unknown", "none", "null"}:
        hardware_serial = ""
    identity = {
        "serial": client.serial,
        "hardwareSerial": hardware_serial,
        "model": client.getprop("ro.product.model"),
        "productDevice": client.getprop("ro.product.device"),
        "buildFingerprint": client.getprop("ro.build.fingerprint"),
        "buildIncremental": client.getprop("ro.build.version.incremental"),
        "buildDisplayId": client.getprop("ro.build.display.id"),
    }
    if not identity["buildFingerprint"] or not identity["buildIncremental"]:
        raise PreparationError("The headset did not report a complete firmware build identity.")
    if identity["model"].lower() != "quest pro" and identity["productDevice"].lower() != "seacliff":
        raise PreparationError(
            f"Connected device is not identified as a Quest Pro ({identity['model']!r}, "
            f"{identity['productDevice']!r})."
        )
    return identity


def _engine_identity(client: AdbClient, device: dict[str, str]) -> dict[str, Any]:
    output = client.root(f"stat -c %s '{ENGINE_PATH}'")
    try:
        size = int(output.splitlines()[-1].strip())
    except (IndexError, ValueError) as error:
        raise PreparationError("Could not read the headset tracking-engine size.") from error
    digest = _read_hash(client, ENGINE_PATH)
    candidates = [profile for profile in ENGINE_PROFILES if profile["size"] == size]
    if len(candidates) != 1:
        raise PreparationError(
            f"Unsupported tracking-engine size {size} and SHA-256 {digest}. "
            "This firmware needs its own validated eye profile."
        )
    profile = candidates[0]
    expected_hash = profile["sha256"]
    if expected_hash is not None and digest != expected_hash:
        raise PreparationError(
            f"Tracking-engine SHA-256 {digest} differs from supported "
            f"profile {profile['profile']} ({expected_hash})."
        )
    if expected_hash is None:
        # The older runtime profile has only a validated binary size. Constrain
        # it to its known OS build, and pin the actual hash to this preparation.
        build_values = (
            device["buildFingerprint"],
            device["buildIncremental"],
            device["buildDisplayId"],
        )
        if not any(re.search(rf"(?<!\d){profile['profile']}(?!\d)", value) for value in build_values):
            raise PreparationError(
                f"Tracking-engine size {size} belongs to an older profile without "
                f"a pinned hash, but the reported build is not {profile['profile']}."
            )
    return {"path": ENGINE_PATH, "size": size, "sha256": digest, "profile": profile["profile"]}


def _discover_model(client: AdbClient) -> str:
    output = client.root(f"find {MODEL_ROOT} -type f -name bolt.ptl", timeout=30)
    candidates = sorted({line.strip() for line in output.splitlines() if line.strip()})
    if not candidates:
        raise PreparationError("No stock bolt.ptl eye model was found under the headset's ODM models directory.")
    if len(candidates) != 1:
        raise PreparationError(
            "More than one bolt.ptl eye model was found; firmware selection is ambiguous: "
            + ", ".join(candidates)
        )
    path = candidates[0]
    if not MODEL_PATH_RE.fullmatch(path) or ".." in path.split("/"):
        raise PreparationError(
            f"The discovered eye model path is outside the supported Seacliff_V1_5 layout: {path}"
        )
    return path


def _is_mounted(client: AdbClient, model_path: str) -> bool:
    # /proc/mounts uses whitespace-delimited fields; the discovered path is
    # restricted above so it cannot contain escaped mount-point whitespace.
    mounts = client.root("cat /proc/mounts")
    return any(
        len(fields) >= 2 and fields[1] == model_path
        for fields in (line.split() for line in mounts.splitlines())
    )


def inspect_headset(client: AdbClient) -> tuple[dict[str, str], dict[str, Any], str, bool]:
    root_id = client.root("id")
    if "uid=0(root)" not in root_id:
        raise PreparationError("Grant Magisk Superuser access to Shell / ADB Shell, then retry.")
    device = _device_identity(client)
    engine = _engine_identity(client, device)
    model_path = _discover_model(client)
    return device, engine, model_path, _is_mounted(client, model_path)


def prepare(client: AdbClient, output_dir: Path = OUTPUT_DIR) -> dict[str, Any]:
    device, engine, model_path, mounted = inspect_headset(client)
    if mounted:
        raise PreparationError(
            "The stock eye-model path is currently mounted over. Stop the gaze test and restore stock before preparing."
        )
    source_hash = _read_hash(client, model_path)
    output_dir.mkdir(parents=True, exist_ok=True)
    destination = output_dir / PATCHED_NAME
    manifest_path = output_dir / MANIFEST_NAME
    with tempfile.TemporaryDirectory(prefix="qpro-stock-eye-") as temporary_dir:
        stock_copy = Path(temporary_dir) / "stock-bolt.ptl"
        client.copy_model(model_path, stock_copy)
        if stock_copy.stat().st_size < 100_000:
            raise PreparationError("The headset returned an incomplete eye model.")
        if file_sha256(stock_copy) != source_hash:
            raise PreparationError("The downloaded stock eye model did not match the headset SHA-256.")
        with tempfile.TemporaryDirectory(prefix="qpro-patch-", dir=output_dir) as stage_dir:
            staged_model = Path(stage_dir) / PATCHED_NAME
            try:
                result = patch(stock_copy, staged_model)
            except (OSError, ValueError, KeyError, IndexError, TypeError, zipfile.BadZipFile) as error:
                raise PreparationError(f"Stock eye model graph is incompatible: {error}") from error
            if result.get("alreadyPatched"):
                raise PreparationError(
                    "The headset model already contains the independent-eye patch; restore the stock firmware model first."
                )
            patched_hash = file_sha256(staged_model)
            if result.get("sha256") != patched_hash:
                raise PreparationError("The generated eye model failed its local SHA-256 check.")
            # Ensure the source was stable throughout extraction and patching.
            if _read_hash(client, model_path) != source_hash or _is_mounted(client, model_path):
                raise PreparationError("The headset's stock eye model changed during preparation; retry after restoring stock.")
            manifest: dict[str, Any] = {
                "schemaVersion": 1,
                "modelPath": model_path,
                "patchedModelPath": PATCHED_NAME,
                "sourceSha256": source_hash,
                "patchedSha256": patched_hash,
                "engine": engine,
                "device": device,
                "createdUtc": datetime.now(timezone.utc).isoformat(),
            }
            staged_manifest = Path(stage_dir) / MANIFEST_NAME
            staged_manifest.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
            os.replace(staged_model, destination)
            os.replace(staged_manifest, manifest_path)
    return manifest


def check_prepared(client: AdbClient, output_dir: Path = OUTPUT_DIR) -> dict[str, Any]:
    destination = output_dir / PATCHED_NAME
    manifest_path = output_dir / MANIFEST_NAME
    if not destination.is_file() or not manifest_path.is_file():
        raise PreparationError("The eye model or its provenance manifest is missing. Use Prepare gaze first.")
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise PreparationError("The prepared eye-model manifest is unreadable. Use Prepare gaze again.") from error
    if not isinstance(manifest, dict) or manifest.get("schemaVersion") != 1:
        raise PreparationError("The prepared eye-model manifest has an unsupported schema. Use Prepare gaze again.")
    if manifest.get("patchedModelPath") != PATCHED_NAME:
        raise PreparationError("The manifest names a different patched eye model. Use Prepare gaze again.")
    patched_hash = manifest.get("patchedSha256")
    source_hash = manifest.get("sourceSha256")
    if not isinstance(patched_hash, str) or not SHA256_RE.fullmatch(patched_hash):
        raise PreparationError("The manifest has no valid patched model SHA-256. Use Prepare gaze again.")
    if not isinstance(source_hash, str) or not SHA256_RE.fullmatch(source_hash):
        raise PreparationError("The manifest has no valid stock model SHA-256. Use Prepare gaze again.")
    if file_sha256(destination) != patched_hash:
        raise PreparationError("The prepared eye model changed since preparation. Use Prepare gaze again.")
    device, engine, model_path, mounted = inspect_headset(client)
    if manifest.get("modelPath") != model_path:
        raise PreparationError("The headset eye-model path differs from the prepared model. Use Prepare gaze again.")
    if manifest.get("engine") != engine:
        raise PreparationError("The headset tracking engine differs from the prepared build. Use Prepare gaze again.")
    prepared_device = manifest.get("device")
    if not isinstance(prepared_device, dict):
        raise PreparationError("The prepared gaze manifest has no device identity. Use Prepare gaze again.")
    # USB and wireless ADB use different transport serials for the same Quest.
    # Permit that switch only when both readings have the same physical serial.
    prepared_hardware_serial = prepared_device.get("hardwareSerial")
    same_hardware = bool(prepared_hardware_serial and prepared_hardware_serial == device["hardwareSerial"])
    conflicting_hardware = bool(
        prepared_hardware_serial and device["hardwareSerial"]
        and prepared_hardware_serial != device["hardwareSerial"]
    )
    if any(
        prepared_device.get(key) != value
        for key, value in device.items()
        if key not in {"serial", "hardwareSerial"}
    ) or conflicting_hardware or (not same_hardware and prepared_device.get("serial") != device["serial"]):
        raise PreparationError("The connected Quest Pro or its firmware build differs from preparation. Use Prepare gaze again.")
    if not mounted and _read_hash(client, model_path) != source_hash:
        raise PreparationError("The headset stock eye model differs from preparation. Use Prepare gaze again.")
    return manifest


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", default="adb", help="ADB executable path")
    parser.add_argument("--check-prepared", action="store_true", help="Verify this PC model against the connected headset")
    arguments = parser.parse_args(argv)
    try:
        client = AdbClient(arguments.adb)
        if arguments.check_prepared:
            manifest = check_prepared(client)
            print(f"Prepared independent gaze verified for {manifest['device']['serial']} ({manifest['engine']['profile']}).")
        else:
            manifest = prepare(client)
            print(f"Independent-eye support prepared locally: {OUTPUT_DIR / PATCHED_NAME}")
            print(f"Verified stock model: {manifest['modelPath']}")
            print(f"Verified tracking-engine profile: {manifest['engine']['profile']}")
        return 0
    except PreparationError as error:
        print(f"Eye model preparation failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
