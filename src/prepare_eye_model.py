#!/usr/bin/env python3
"""Prepare and verify an independent-eye patch from this Quest Pro's stock model.

Only the two engine profiles already supported by native_raw_eye_probe.py are
eligible. No firmware, engine binary, or stock model is bundled with the app.
Preparation refuses other active gaze methods and overlaid model/engine paths;
that safety check does not make an unsupported tracking engine compatible.
"""

from __future__ import annotations

import argparse
import base64
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
MODEL_FAMILY = f"{MODEL_ROOT}/Seacliff_V1_5/fbnet/int8"
# native-eye-local-branch-test.ps1 enables Meta's experimental-model property
# before restarting trackingservice, then bind-mounts its patch at this path.
# The ordinary and vPrevious files can coexist on the same firmware build.
EXPERIMENTAL_MODEL_PATH = f"{MODEL_FAMILY}/experimental/bolt/bolt.ptl"
KNOWN_MODEL_PATHS = frozenset(
    {
        f"{MODEL_FAMILY}/bolt/bolt.ptl",
        EXPERIMENTAL_MODEL_PATH,
        f"{MODEL_FAMILY}/vPrevious/bolt/bolt.ptl",
    }
)
PATCHED_NAME = "bolt-independent-axes.ptl"
MANIFEST_NAME = "bolt-independent-axes.manifest.json"
OUTPUT_DIR = Path(__file__).resolve().parent / "research" / "seacliff_eye_model"
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
MODEL_PATH_RE = re.compile(
    r"^/odm/etc/eyetracking/runtime/models/Seacliff_V1_5/"
    r"[A-Za-z0-9_./-]*/bolt/bolt\.ptl$"
)
EXPERIMENTAL_PROPERTY = (
    "persist.device_config.oculus_shared_vision.oculus_eyetracking_enable_experimental_model"
)
MAGISK_MODULE_ROOT = "/data/adb/modules"
GAZE_MODULE_IDS = frozenset({"questpro_independent_gaze"})
OVERLAYFS_ODM_UPPER = "/dev/mount_overlayfs/upper/odm"
OVERLAYFS_MODULE_IDS = frozenset({"magisk_overlayfs", "overlayfs"})
OVERLAYFS_ODM_UPPER_ALIASES = frozenset({
    "/debug_ramdisk/overlayfs_mnt/upper/odm",
    "/sbin/overlayfs_mnt/upper/odm",
})
MODULE_PATH_RE = re.compile(r"^/data/adb/modules/[A-Za-z0-9_.-]{1,128}$")
GAZE_REFERENCE_RE = re.compile(
    r"independent[\s_-]*(?:eye[\s_-]*)?gaze|independent[\s_-]*eye|"
    r"eye[\s_-]*tracking|eyetracking|bolt\.ptl|libtrackingengines",
    flags=re.IGNORECASE,
)
# This is a fixed read-only command: module scripts are read as text, never
# sourced or executed. Limits and the final sentinel make an incomplete scan
# a refusal, rather than evidence that no other gaze method exists.
GAZE_ENVIRONMENT_SCAN = r"""set -eu
printf 'QPRO_GAZE_SCAN_V1\n'
test -d /data/adb/modules && test -r /data/adb/modules && test -x /data/adb/modules || { printf 'Magisk modules directory is unavailable\n' >&2; exit 1; }
count=0
overlayfs_enabled=0
for module in /data/adb/modules/* /data/adb/modules/.[!.]* /data/adb/modules/..?*; do
    test -d "$module" || continue
    count=$((count + 1))
    test "$count" -le 128 || { printf 'Too many Magisk modules to verify\n' >&2; exit 1; }
    test -r "$module" && test -x "$module" || exit 1
    printf 'QPRO_MODULE_BEGIN %s\n' "$module"
    enabled=1
    if test -e "$module/disable"; then enabled=0; fi
    pending_remove=0
    if test -e "$module/remove"; then pending_remove=1; fi
    printf 'QPRO_MODULE_ENABLED %s\n' "$enabled"
    printf 'QPRO_MODULE_PENDING_REMOVE %s\n' "$pending_remove"
    for file in module.prop service.sh post-fs-data.sh system.prop sepolicy.rule; do
        path="$module/$file"
        if test "$file" = module.prop || test -e "$path"; then
            test -f "$path" && test -r "$path" || exit 1
            size=$(wc -c < "$path") || exit 1
            test "$size" -le 65536 || { printf 'Magisk metadata or policy file is too large\n' >&2; exit 1; }
            printf 'QPRO_FILE_BEGIN %s\n' "$file"
            cat "$path" || exit 1
            printf '\nQPRO_FILE_END\n'
        fi
    done
    module_id=$(sed -n 's/^id=//p' "$module/module.prop") || exit 1
    module_id=$(printf '%s' "$module_id" | tr -d '\r') || exit 1
    if test "$enabled" = 1; then
        case "$module_id" in magisk_overlayfs|overlayfs) overlayfs_enabled=1 ;; esac
        case "$module" in /data/adb/modules/magisk_overlayfs|/data/adb/modules/overlayfs) overlayfs_enabled=1 ;; esac
    fi
    printf 'QPRO_MODULE_END\n'
done
test -r /proc/mounts || exit 1
size=$(wc -c < /proc/mounts) || exit 1
test "$size" -le 1048576 || exit 1
printf 'QPRO_MOUNTS_BEGIN\n'
cat /proc/mounts || exit 1
printf '\nQPRO_MOUNTS_END\n'
upper=/dev/mount_overlayfs/upper/odm
# OverlayFS can remove its temporary mount while retaining the same backing
# directory under Magisk's mount namespace. Never treat a missing path as empty.
if ! test -e "$upper" && test "$overlayfs_enabled" = 1; then
    magisk_path=$(magisk --path) || exit 1
    case "$magisk_path" in
        /debug_ramdisk|/sbin) upper="$magisk_path/overlayfs_mnt/upper/odm" ;;
        *) printf 'Unrecognized Magisk temporary path\n' >&2; exit 1 ;;
    esac
fi
printf 'QPRO_OVERLAY_UPPER_PATH_BEGIN\n%s\nQPRO_OVERLAY_UPPER_PATH_END\nQPRO_OVERLAY_UPPER_BEGIN\n' "$upper"
if test -e "$upper"; then
    test -d "$upper" && test -r "$upper" && test -x "$upper" || exit 1
    entry=$(find "$upper" -mindepth 1 -maxdepth 1 -print -quit) || exit 1
    if test -z "$entry"; then printf 'empty\n'; else printf 'notEmpty\n'; fi
else
    printf 'absent\n'
fi
printf 'QPRO_OVERLAY_UPPER_END\nQPRO_EXPERIMENTAL_BEGIN\n'
getprop persist.device_config.oculus_shared_vision.oculus_eyetracking_enable_experimental_model || exit 1
printf '\nQPRO_EXPERIMENTAL_END\nQPRO_GAZE_SCAN_COMPLETE\n'
"""

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
        # A single encoded shell argument survives Windows and ADB quoting.
        # Only our supplied query reaches the root shell; downloaded module
        # scripts are inspected by the query as data, never run as commands.
        normalized = command.replace("\r\n", "\n").replace("\r", "\n")
        payload = base64.b64encode(normalized.encode("utf-8")).decode("ascii")
        request = f"printf %s {payload} | base64 -d | su -c sh"
        return self._run(["-s", self.serial, "shell", request], timeout=timeout)

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


def _read_engine_identity(client: AdbClient) -> dict[str, Any]:
    output = client.root(f"stat -c %s '{ENGINE_PATH}'")
    try:
        size = int(output.splitlines()[-1].strip())
    except (IndexError, ValueError) as error:
        raise PreparationError("Could not read the headset tracking-engine size.") from error
    return {"path": ENGINE_PATH, "size": size, "sha256": _read_hash(client, ENGINE_PATH)}


def _engine_failure(detail: str, device: dict[str, str]) -> PreparationError:
    # The consumer needs its own validated native probe layout. Matching the
    # model graph alone cannot establish the engine's address/register layout.
    firmware = (
        f"build {device['buildIncremental']}; display {device['buildDisplayId']}; "
        f"fingerprint {device['buildFingerprint']}"
    )
    return PreparationError(
        f"{detail} Firmware: {firmware}. "
        "The Hub's independent gaze is unavailable for this build; leave Independent Eye Gaze off. "
        "No headset tracking was changed. Check a Magisk gaze module's support for this exact "
        "firmware before using it, and use one gaze method at a time as described in the setup guide."
    )


def _validate_engine_identity(engine: dict[str, Any], device: dict[str, str]) -> dict[str, Any]:
    size = engine["size"]
    digest = engine["sha256"]
    candidates = [profile for profile in ENGINE_PROFILES if profile["size"] == size]
    if len(candidates) != 1:
        raise _engine_failure(
            f"Unsupported tracking-engine size {size} and SHA-256 {digest}. "
            "This firmware needs its own validated eye profile.",
            device,
        )
    profile = candidates[0]
    expected_hash = profile["sha256"]
    if expected_hash is not None and digest != expected_hash:
        raise _engine_failure(
            f"Tracking-engine SHA-256 {digest} differs from supported "
            f"profile {profile['profile']} ({expected_hash}).",
            device,
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
            raise _engine_failure(
                f"Tracking-engine size {size} belongs to an older profile without "
                f"a pinned hash, but the reported build is not {profile['profile']}.",
                device,
            )
    return {**engine, "profile": profile["profile"]}


def _engine_identity(client: AdbClient, device: dict[str, str]) -> dict[str, Any]:
    return _validate_engine_identity(_read_engine_identity(client), device)


def _discover_model(client: AdbClient) -> str:
    output = client.root(f"find {MODEL_ROOT} -type f -name bolt.ptl", timeout=30)
    candidates = sorted({line.strip() for line in output.splitlines() if line.strip()})
    if not candidates:
        raise PreparationError("No stock bolt.ptl eye model was found under the headset's ODM models directory.")
    for path in candidates:
        if not MODEL_PATH_RE.fullmatch(path) or ".." in path.split("/"):
            raise PreparationError(
                f"The discovered eye model path is outside the supported Seacliff_V1_5 layout: {path}"
            )
    if len(candidates) == 1:
        # Retain support for older layouts with one unambiguous model path.
        return candidates[0]
    if EXPERIMENTAL_MODEL_PATH in candidates and set(candidates) <= KNOWN_MODEL_PATHS:
        # The launcher explicitly selects this branch. Choosing the ordinary
        # or vPrevious model here would patch different bytes than it mounts.
        return EXPERIMENTAL_MODEL_PATH
    raise PreparationError(
        "More than one bolt.ptl eye model was found; firmware selection is ambiguous: "
        + ", ".join(candidates)
    )


def _is_mounted(client: AdbClient, model_path: str) -> bool:
    # /proc/mounts uses whitespace-delimited fields; the discovered path is
    # restricted above so it cannot contain escaped mount-point whitespace.
    mounts = client.root("cat /proc/mounts")
    return any(
        len(fields) >= 2 and fields[1] == model_path
        for fields in (line.split() for line in mounts.splitlines())
    )


def _parse_gaze_environment(output: str) -> dict[str, Any]:
    lines = output.splitlines()
    if not lines or lines[0] != "QPRO_GAZE_SCAN_V1" or lines[-1] != "QPRO_GAZE_SCAN_COMPLETE":
        raise PreparationError("The Magisk and gaze-mount safety scan did not complete.")
    modules: list[dict[str, Any]] = []
    index = 1
    policy_files = {"module.prop", "service.sh", "post-fs-data.sh", "system.prop", "sepolicy.rule"}

    def take_section(end_marker: str) -> str:
        nonlocal index
        start = index
        while index < len(lines) and lines[index] != end_marker:
            index += 1
        if index >= len(lines):
            raise PreparationError("The Magisk and gaze-mount safety scan was truncated.")
        content = "\n".join(lines[start:index]).strip()
        index += 1
        return content

    try:
        while lines[index].startswith("QPRO_MODULE_BEGIN "):
            path = lines[index].removeprefix("QPRO_MODULE_BEGIN ")
            if not MODULE_PATH_RE.fullmatch(path) or path.rsplit("/", 1)[-1] in {".", ".."}:
                raise PreparationError("The Magisk safety scan reported an invalid module directory.")
            index += 1
            enabled_line = lines[index]
            if enabled_line not in {"QPRO_MODULE_ENABLED 0", "QPRO_MODULE_ENABLED 1"}:
                raise PreparationError("The Magisk safety scan did not report a module's enabled state.")
            index += 1
            pending_remove_line = lines[index]
            if pending_remove_line not in {"QPRO_MODULE_PENDING_REMOVE 0", "QPRO_MODULE_PENDING_REMOVE 1"}:
                raise PreparationError("The Magisk safety scan did not report a module's pending-removal state.")
            index += 1
            contents: dict[str, str] = {}
            while lines[index].startswith("QPRO_FILE_BEGIN "):
                filename = lines[index].removeprefix("QPRO_FILE_BEGIN ")
                if filename not in policy_files or filename in contents:
                    raise PreparationError("The Magisk safety scan reported invalid policy metadata.")
                index += 1
                contents[filename] = take_section("QPRO_FILE_END")
                if len(contents[filename]) > 65536:
                    raise PreparationError("The Magisk safety scan exceeded its metadata limit.")
            if lines[index] != "QPRO_MODULE_END" or "module.prop" not in contents:
                raise PreparationError("The Magisk safety scan could not read a module's metadata.")
            index += 1
            properties = {
                key.strip(): value.strip()
                for line in contents["module.prop"].splitlines()
                if "=" in line and not line.lstrip().startswith("#")
                for key, value in [line.split("=", 1)]
            }
            directory_name = path.rsplit("/", 1)[-1]
            relevant_files = sorted(
                filename for filename, content in contents.items()
                if GAZE_REFERENCE_RE.search(content)
            )
            known_gaze_id = any(
                value.lower() in GAZE_MODULE_IDS
                for value in (directory_name, properties.get("id", ""))
            )
            modules.append({
                "directory": directory_name,
                "id": properties.get("id", directory_name)[:128],
                "name": properties.get("name", "")[:256],
                "enabled": enabled_line.endswith(" 1"),
                "pendingRemoval": pending_remove_line.endswith(" 1"),
                "gazeRelevant": known_gaze_id or bool(relevant_files),
                "relevantFiles": relevant_files,
            })
            if len(modules) > 128:
                raise PreparationError("The Magisk safety scan exceeded its module limit.")
        if lines[index] != "QPRO_MOUNTS_BEGIN":
            raise PreparationError("The Magisk safety scan did not include the mount table.")
        index += 1
        mounts = take_section("QPRO_MOUNTS_END")
        if not mounts or len(mounts) > 1048576:
            raise PreparationError("The gaze safety scan could not read a complete mount table.")
        if lines[index] != "QPRO_OVERLAY_UPPER_PATH_BEGIN":
            raise PreparationError("The gaze safety scan did not identify its OverlayFS upper directory.")
        index += 1
        upper_path = take_section("QPRO_OVERLAY_UPPER_PATH_END")
        if upper_path not in {OVERLAYFS_ODM_UPPER, *OVERLAYFS_ODM_UPPER_ALIASES}:
            raise PreparationError("The gaze safety scan reported an unrecognized Magisk temporary path.")
        if lines[index] != "QPRO_OVERLAY_UPPER_BEGIN":
            raise PreparationError("The gaze safety scan did not verify the OverlayFS upper directory.")
        index += 1
        upper_state = take_section("QPRO_OVERLAY_UPPER_END")
        if upper_state not in {"empty", "notEmpty", "absent"}:
            raise PreparationError("The gaze safety scan could not read the OverlayFS upper directory.")
        if lines[index] != "QPRO_EXPERIMENTAL_BEGIN":
            raise PreparationError("The gaze safety scan did not include the experimental-model property.")
        index += 1
        experimental = take_section("QPRO_EXPERIMENTAL_END")
        if index != len(lines) - 1:
            raise PreparationError("The gaze safety scan contained unexpected trailing data.")
    except IndexError as error:
        raise PreparationError("The Magisk and gaze-mount safety scan was truncated.") from error
    relevant_mounts: list[dict[str, str]] = []
    transparent_mounts: list[dict[str, str]] = []
    overlayfs_enabled = any(
        module["enabled"] and any(value.lower() in OVERLAYFS_MODULE_IDS for value in (module["id"], module["directory"]))
        for module in modules
    )
    for line in mounts.splitlines():
        fields = line.split()
        if len(fields) != 6 or not fields[1].startswith("/"):
            raise PreparationError("The gaze safety scan contained an unreadable mount-table entry.")
        source, target, filesystem = fields[:3]
        # Relevant descendants and intermediate directory mounts can hide
        # stock bytes too. Ordinary physical /odm mounts are expected; an
        # overlay/tmpfs or data-backed replacement of that ancestor is not.
        ancestor = target == "/" or any(
            path == target or path.startswith(target.rstrip("/") + "/")
            for path in (MODEL_ROOT, ENGINE_PATH)
        )
        model_descendant = target.startswith(MODEL_ROOT + "/")
        intermediate_mount = ancestor and target not in {"/", "/odm"}
        replaced_ancestor = ancestor and (
            filesystem in {"overlay", "overlayfs", "tmpfs"}
            or source.startswith(("/data/", "/dev/block/loop", "/dev/loop"))
        )
        if model_descendant or intermediate_mount or replaced_ancestor:
            mount = {"source": source, "target": target, "filesystem": filesystem, "options": fields[3]}
            options = fields[3].split(",")
            values = {key: value for option in options if "=" in option for key, value in [option.split("=", 1)]}
            workdir = values.get("workdir", "")
            # Magisk OverlayFS can remain after disabling the gaze module and
            # rebooting. Only its observed, read-only ODM shape is transparent:
            # one original lower layer and an entirely empty upper directory.
            # Its retained alias is accepted only with the OverlayFS module
            # enabled and Magisk's temporary root restricted by the scan.
            # An active gaze module is refused independently of this exception.
            transparent = (
                filesystem == "overlay" and source == "overlay"
                and target in {"/odm", "/odm/etc", "/odm/lib64"}
                and "ro" in options and "rw" not in options
                and values.get("lowerdir") == "/odm"
                and values.get("upperdir") == OVERLAYFS_ODM_UPPER
                and upper_state == "empty"
                and (upper_path == OVERLAYFS_ODM_UPPER or overlayfs_enabled)
                and workdir.startswith("/dev/mount_overlayfs/worker/")
                and re.fullmatch(r"/dev/mount_overlayfs/worker/[A-Za-z0-9_./-]+", workdir) is not None
                and not any(part in {".", "..", ""} for part in workdir.split("/")[1:])
                and all(sum(option.startswith(key + "=") for option in options) == 1 for key in ("lowerdir", "upperdir", "workdir"))
            )
            if transparent:
                transparent_mounts.append(mount)
            else:
                relevant_mounts.append(mount)
    if experimental.lower() not in {"", "0", "1", "false", "true"}:
        raise PreparationError("The gaze safety scan could not interpret the experimental-model property.")
    return {
        "scanComplete": True,
        "modules": modules,
        "relevantMounts": relevant_mounts,
        "transparentOverlayMounts": transparent_mounts,
        "overlayfsOdmUpperState": upper_state,
        "overlayfsOdmUpperInspectedPath": upper_path,
        "experimentalModelProperty": experimental,
    }


def _gaze_environment(client: AdbClient) -> dict[str, Any]:
    try:
        return _parse_gaze_environment(client.root(GAZE_ENVIRONMENT_SCAN, timeout=20))
    except PreparationError as error:
        raise PreparationError(
            "Could not verify Magisk modules and gaze mounts; independent gaze was not prepared or applied. "
            f"The safety scan must be readable and complete. {error}"
        ) from error


def _require_stock_gaze_environment(environment: dict[str, Any]) -> None:
    active = [module for module in environment["modules"] if module["enabled"] and module["gazeRelevant"]]
    if active:
        names = ", ".join(module["id"] for module in active)
        raise PreparationError(
            f"An active Magisk gaze module was found ({names}). Use one independent gaze method at a time. "
            "Leave Independent Eye Gaze off in the Hub while that module is active, or disable the module "
            "in Magisk and reboot before preparing the Hub's temporary method. No headset tracking was changed."
        )
    if environment["relevantMounts"]:
        paths = ", ".join(mount["target"] for mount in environment["relevantMounts"])
        raise PreparationError(
            f"The eye model or tracking engine is currently mounted over ({paths}). "
            "Stop the gaze test and restore stock before preparing or checking the Hub's gaze model. "
            "If a Magisk module was disabled, reboot so its mounts are removed. No headset tracking was changed."
        )
    if environment["experimentalModelProperty"].lower() in {"1", "true"}:
        raise PreparationError(
            "The experimental eye-model selection is already enabled. Stop the existing gaze method and "
            "restore stock before preparing or checking the Hub's gaze model. No headset tracking was changed."
        )


def inspect_headset(client: AdbClient) -> tuple[dict[str, str], dict[str, Any], str, bool]:
    root_id = client.root("id")
    if "uid=0(root)" not in root_id:
        raise PreparationError("Grant Magisk Superuser access to Shell / ADB Shell, then retry.")
    device = _device_identity(client)
    _require_stock_gaze_environment(_gaze_environment(client))
    engine = _engine_identity(client, device)
    model_path = _discover_model(client)
    return device, engine, model_path, _is_mounted(client, model_path)


def diagnose(client: AdbClient) -> dict[str, Any]:
    """Report compatibility metadata without copying models or changing tracking.

    Firmware identity and engine hashes are enough to identify a new build.
    Omit hardware/ADB serials so the output can be shared without those IDs.
    This deliberately does not claim that a discovered model graph is valid.
    """
    if "uid=0(root)" not in client.root("id"):
        raise PreparationError("Grant Magisk Superuser access to Shell / ADB Shell, then retry.")
    device = _device_identity(client)
    engine = _read_engine_identity(client)
    reason = None
    try:
        engine = _validate_engine_identity(engine, device)
    except PreparationError as error:
        reason = str(error)
    model_path = None
    mounted = None
    model_error = None
    environment = None
    environment_error = None
    try:
        environment = _gaze_environment(client)
        _require_stock_gaze_environment(environment)
    except PreparationError as error:
        environment_error = str(error)
    try:
        model_path = _discover_model(client)
        mounted = _is_mounted(client, model_path)
    except PreparationError as error:
        model_error = str(error)
    return {
        "format": "qpro-gaze-compatibility-diagnostic-v1",
        "firmware": {
            key: device[key]
            for key in ("model", "productDevice", "buildIncremental", "buildDisplayId", "buildFingerprint")
        },
        "engine": engine,
        "engineSupported": reason is None,
        "engineCompatibilityReason": reason,
        "modelPath": model_path,
        "modelPathMounted": mounted,
        "modelDiscoveryError": model_error,
        "gazeEnvironment": environment,
        "gazeEnvironmentError": environment_error,
        "gazePreflightPassed": environment is not None and environment_error is None,
        "modelPatchValidated": False,
        "headsetTrackingChanged": False,
    }


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
            _require_stock_gaze_environment(_gaze_environment(client))
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
    if _read_hash(client, model_path) != source_hash:
        raise PreparationError("The headset stock eye model differs from preparation. Use Prepare gaze again.")
    return manifest


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", default="adb", help="ADB executable path")
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument("--check-prepared", action="store_true", help="Verify this PC model against the connected headset")
    modes.add_argument("--diagnose", action="store_true", help="Print shareable firmware/engine metadata without changing headset tracking")
    arguments = parser.parse_args(argv)
    try:
        client = AdbClient(arguments.adb)
        if arguments.diagnose:
            print(json.dumps(diagnose(client), indent=2), flush=True)
        elif arguments.check_prepared:
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
