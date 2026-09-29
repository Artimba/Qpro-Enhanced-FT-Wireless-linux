"""Choose a supported discrete GPU for Qpro's PyTorch workloads.

ROCm exposes its devices through PyTorch's ``cuda`` API.  On a hybrid PC,
device zero may be a Ryzen integrated GPU, so availability alone is not enough.
The older Radeon allowlist matches the ROCm 7.2.1 Windows PyTorch matrix.
Mapped RX 6000, 7000, and 9000 cards can use AMD's stable ROCm 10.0
packages with a device package selected for the card's gfx target. This Qpro
integration remains experimental until its GPU checks pass on each PC.
"""

from __future__ import annotations

import re
import json
import os
import sys
from pathlib import Path


_SUPPORTED_RADEON_721 = frozenset(
    name.casefold()
    for name in (
        "Radeon RX 9070 XT",
        "Radeon RX 9070",
        "Radeon AI PRO R9700",
        "Radeon RX 9060 XT",
        "Radeon RX 7900 XTX",
        "Radeon PRO W7900",
        "Radeon PRO W7900 Dual Slot",
        "Radeon RX 7700",
    )
)

# TheRock publishes Windows device packages for these gfx targets. This is a
# Qpro experiment: readiness still depends on training and inference checks.
_EXPERIMENTAL_RADEON_TARGETS = {
    name.casefold(): gfx
    for gfx, names in {
        "gfx1030": ("Radeon RX 6950 XT", "Radeon RX 6900 XT", "Radeon RX 6800 XT", "Radeon RX 6800"),
        "gfx1031": ("Radeon RX 6750 XT", "Radeon RX 6700 XT"),
        "gfx1032": ("Radeon RX 6600 XT", "Radeon RX 6600"),
        "gfx1100": (
            "Radeon RX 7900 XTX", "Radeon RX 7900 XT", "Radeon RX 7900 GRE",
            "Radeon PRO W7900", "Radeon PRO W7900 Dual Slot",
        ),
        "gfx1101": ("Radeon RX 7800 XT", "Radeon RX 7700 XT", "Radeon RX 7700"),
        "gfx1102": ("Radeon RX 7600 XT", "Radeon RX 7600"),
        "gfx1201": (
            "Radeon RX 9070 XT", "Radeon RX 9070", "Radeon RX 9070 GRE",
            "Radeon AI PRO R9700",
        ),
        "gfx1200": ("Radeon RX 9060 XT", "Radeon RX 9060"),
    }.items()
    for name in names
}


def _normalized_gpu_name(name: str) -> str:
    normalized = re.sub(r"\((?:TM|R)\)", " ", str(name), flags=re.IGNORECASE)
    normalized = re.sub(r"\s+", " ", normalized).strip()
    normalized = re.sub(r"^AMD\s+", "", normalized, flags=re.IGNORECASE)
    normalized = re.sub(r"^RX\s+", "Radeon RX ", normalized, flags=re.IGNORECASE)
    return normalized.casefold()


def experimental_rocm_target_for_gpu_name(name: str) -> str | None:
    """Return the ROCm 10 device package target for a mapped Radeon card."""
    return _EXPERIMENTAL_RADEON_TARGETS.get(_normalized_gpu_name(name))


def _expected_experimental_target() -> str | None:
    """Accept a verified marker, or an installer-only pre-marker smoke target."""
    marker = Path(sys.prefix) / "qpro-rocm-ready.json"
    try:
        if marker.is_file():
            record = json.loads(marker.read_text(encoding="utf-8-sig"))
            if not isinstance(record, dict):
                return None
            target = str(record.get("gfxTarget") or "").strip().lower()
            if (
                record.get("schema") == 1
                and record.get("supportTier") == "experimental-rocm-10"
                and str(record.get("rocmVersion") or "").startswith("10.0")
                and target in _EXPERIMENTAL_RADEON_TARGETS.values()
            ):
                return target
            return None
    except (OSError, ValueError, TypeError):
        return None
    if os.environ.get("QPRO_ROCM_INSTALL_SMOKE_TEST") == "1":
        requested = os.environ.get("QPRO_ROCM_EXPECTED_GFX_TARGET", "").strip().lower()
        if requested in _EXPERIMENTAL_RADEON_TARGETS.values():
            return requested
    return None


def is_supported_rocm_gpu_name(name: str, hip_version: str | None = "7.2.1") -> bool:
    """Check the discrete GPU against the allowlist for its ROCm build."""
    normalized = _normalized_gpu_name(name)
    version = str(hip_version or "")
    if version.startswith("7.2.1"):
        return normalized in _SUPPORTED_RADEON_721
    if version.startswith("10.0"):
        return normalized in _EXPERIMENTAL_RADEON_TARGETS
    return False


def is_rocm_721_torch_build(torch_module: object) -> bool:
    """Match the pinned Windows wheel and its HIP 7.2 runtime build."""
    return (
        str(getattr(torch_module, "__version__", "")) == "2.9.1+rocm7.2.1"
        and str(getattr(torch_module.version, "hip", "") or "").startswith("7.2.")
    )


def _rocm_build_version(torch_module: object) -> str:
    """Identify the ROCm wheel release; HIP can report its own build number."""
    hip = str(getattr(torch_module.version, "hip", "") or "")
    if is_rocm_721_torch_build(torch_module):
        return "7.2.1"
    return hip


def supported_rocm_device_name(torch_module: object) -> str | None:
    """Find a supported Radeon device, including when an iGPU is device zero."""
    if not getattr(torch_module.version, "hip", None):
        return None
    try:
        if not torch_module.cuda.is_available():
            return None
        experimental = str(torch_module.version.hip).startswith("10.0")
        rocm_build = _rocm_build_version(torch_module)
        expected_target = _expected_experimental_target() if experimental else None
        if experimental and expected_target is None:
            return None

        def supported_at(index: int) -> bool:
            try:
                name = torch_module.cuda.get_device_name(index)
            except Exception:
                return False
            return is_supported_rocm_gpu_name(name, rocm_build) and (
                expected_target is None or experimental_rocm_target_for_gpu_name(name) == expected_target
            )

        # Some relocated Windows ROCm environments can still query and use
        # device zero, while their offload-arch launcher breaks device_count().
        # A successful name query identifies the actual Torch/HIP device and
        # still excludes integrated graphics through the discrete allowlist.
        if supported_at(0):
            return "cuda:0"
        for index in range(1, torch_module.cuda.device_count()):
            if supported_at(index):
                return f"cuda:{index}"
    except Exception:
        return None
    return None


def preferred_torch_device_name(torch_module: object) -> str:
    """Prefer supported discrete acceleration, otherwise use CPU."""
    if getattr(torch_module.version, "hip", None):
        return supported_rocm_device_name(torch_module) or "cpu"
    try:
        if getattr(torch_module.version, "cuda", None) and torch_module.cuda.is_available():
            # NVIDIA CUDA does not expose AMD or Intel integrated graphics.
            return "cuda:0"
    except Exception:
        pass
    return "cpu"


def require_rocm_device_name(torch_module: object) -> str:
    """Fail before work starts if ROCm cannot see a supported discrete GPU."""
    device = supported_rocm_device_name(torch_module)
    if device is None:
        experimental = str(getattr(torch_module.version, "hip", "")).startswith("10.0")
        expected_target = _expected_experimental_target() if experimental else None
        if experimental and expected_target is None:
            raise RuntimeError(
                "ROCm 10.0 has no valid Qpro readiness record for a discrete GPU "
                "target. Run Install AMD ROCm again before using this environment."
            )
        target_hint = f" The experimental environment was prepared for {expected_target}." if expected_target else ""
        raise RuntimeError(
            "ROCm cannot see a discrete Radeon supported by this Qpro ROCm build. "
            f"Ryzen integrated graphics are not supported.{target_hint} Check the AMD driver, "
            "the selected ROCm build, or use the CPU runtime."
        )
    return device


def validated_torch_device_name(torch_module: object, requested: str) -> str:
    """Validate an explicit CUDA choice so it cannot target a ROCm iGPU."""
    if requested == "auto":
        return preferred_torch_device_name(torch_module)
    if not requested.startswith("cuda"):
        return requested
    if not torch_module.cuda.is_available():
        raise RuntimeError("GPU acceleration was requested, but PyTorch cannot access a GPU")
    if getattr(torch_module.version, "hip", None):
        index = int(requested.split(":", 1)[1]) if ":" in requested else 0
        if index < 0:
            raise RuntimeError(f"ROCm device {requested} is unavailable")
        try:
            # The indexed name query validates the selected device directly.
            # device_count() may fail through a stale offload-arch launcher even
            # when Torch can access this device.
            name = torch_module.cuda.get_device_name(index)
        except Exception as exc:
            raise RuntimeError(f"ROCm device {requested} is unavailable") from exc
        experimental = str(torch_module.version.hip).startswith("10.0")
        expected_target = _expected_experimental_target() if experimental else None
        if experimental and expected_target is None:
            raise RuntimeError("ROCm 10.0 has no valid Qpro readiness record for a discrete GPU target")
        if not is_supported_rocm_gpu_name(name, _rocm_build_version(torch_module)) or (
            expected_target is not None and experimental_rocm_target_for_gpu_name(name) != expected_target
        ):
            raise RuntimeError(
                f"ROCm device {requested} ({name}) is not a supported discrete "
                "Radeon GPU; choose the supported GPU or CPU"
            )
    return requested
