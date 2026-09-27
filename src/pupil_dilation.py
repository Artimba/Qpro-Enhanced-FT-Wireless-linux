"""Experimental relative pupil-size estimates from Quest Pro eye cameras.

The camera stream has no calibrated millimetre scale. Each eye's first valid
frames establish a session baseline, represented as 5 mm for VRCFT animation.
These values are relative animation estimates, not clinical measurements.
"""

from __future__ import annotations

import socket
import struct
import time
from collections import deque
from dataclasses import dataclass

import cv2
import numpy as np


CAMERA_WIDTH = 400
CAMERA_HEIGHT = 400
PUPIL_PACKET = struct.Struct("<4sBBHff")
PUPIL_MAGIC = b"QPDI"


@dataclass(frozen=True)
class PupilDetection:
    center: tuple[float, float]
    diameter_px: float
    confidence: float


def detect_pupil(
    image: np.ndarray,
    previous_center: tuple[float, float] | None = None,
) -> PupilDetection | None:
    """Find a dark pupil despite small tracking-LED reflections."""
    if image.shape != (CAMERA_HEIGHT, CAMERA_WIDTH) or image.dtype != np.uint8:
        raise ValueError("Pupil detection expects one 400x400 grayscale eye frame")
    x0, y0 = 55, 75
    roi = image[y0:355, x0:390]
    if np.ptp(roi) < 18 or np.mean(roi) < 8:
        return None
    # Median filtering removes bright headset LED glints inside the pupil.
    clean = cv2.medianBlur(roi, 7)
    low = int(np.min(clean))
    thresholds = sorted(set(
        [low + 10, low + 20]
        + [int(v) for v in np.percentile(clean, (5, 10, 15, 20, 25, 30))]
    ))
    close_kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (7, 7))
    open_kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3))
    best: tuple[float, PupilDetection] | None = None
    for threshold in thresholds:
        dark = cv2.threshold(clean, threshold, 255, cv2.THRESH_BINARY_INV)[1]
        dark = cv2.morphologyEx(dark, cv2.MORPH_CLOSE, close_kernel)
        dark = cv2.morphologyEx(dark, cv2.MORPH_OPEN, open_kernel)
        contours, _ = cv2.findContours(dark, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        for contour in contours:
            area = cv2.contourArea(contour)
            if not 100 <= area <= 6500 or len(contour) < 5:
                continue
            (x, y), axes, angle = cv2.fitEllipse(contour)
            major, minor = max(axes), min(axes)
            if not 14 <= major <= 75 or minor < 10 or minor / major < 0.48:
                continue
            if not 15 <= x <= roi.shape[1] - 15 or not 15 <= y <= roi.shape[0] - 15:
                continue
            ellipse_area = np.pi * major * minor / 4
            fill = area / ellipse_area
            if not 0.60 <= fill <= 1.30:
                continue
            perimeter = cv2.arcLength(contour, True)
            circularity = 4 * np.pi * area / (perimeter * perimeter) if perimeter else 0
            if circularity < 0.42:
                continue
            # Require the fitted shape to be darker than its immediate annulus.
            inner = np.zeros(roi.shape, np.uint8)
            outer = np.zeros(roi.shape, np.uint8)
            cv2.ellipse(inner, ((x, y), (axes[0] * 0.70, axes[1] * 0.70), angle), 255, -1)
            cv2.ellipse(outer, ((x, y), (axes[0] * 1.90, axes[1] * 1.90), angle), 255, -1)
            cv2.ellipse(outer, ((x, y), (axes[0] * 1.20, axes[1] * 1.20), angle), 0, -1)
            inside = clean[inner != 0]
            ring = clean[outer != 0]
            if inside.size < 20 or ring.size < 30:
                continue
            inside_median = float(np.median(inside))
            contrast = float(np.median(ring) - inside_median)
            if inside_median > 45 or contrast < 25:
                continue
            center = (float(x + x0), float(y + y0))
            prior_penalty = (
                0.15 * np.hypot(center[0] - previous_center[0], center[1] - previous_center[1])
                if previous_center is not None else 0
            )
            location_penalty = 0.07 * np.hypot(center[0] - 275, center[1] - 215)
            oversize_penalty = 1.5 * max(0, major - 48)
            score = (
                contrast + 20 * circularity + 7 * fill
                - prior_penalty - location_penalty - oversize_penalty
            )
            detection = PupilDetection(
                center=center,
                diameter_px=float(major),
                confidence=float(np.clip(contrast / 50 * circularity, 0, 1)),
            )
            if best is None or score > best[0]:
                best = (score, detection)
    return best[1] if best is not None else None


class RelativePupilEye:
    def __init__(self, baseline_frames: int = 12, sensitivity: float = 1.4) -> None:
        if not 1.0 <= sensitivity <= 3.0:
            raise ValueError("Pupil sensitivity must be between 1.0 and 3.0")
        self.sensitivity = sensitivity
        self._baseline_samples: deque[float] = deque(maxlen=baseline_frames)
        self._baseline_centers: deque[tuple[float, float]] = deque(maxlen=baseline_frames)
        # A five-frame median removes small contour changes magnified at 3x.
        self._recent_sizes: deque[float] = deque(maxlen=5)
        self._baseline: float | None = None
        self._baseline_center: tuple[float, float] | None = None
        self._smoothed: float | None = None
        self._invalid_frames = 0
        self.status = "warming"

    def _invalid(self, reason: str) -> float | None:
        self._recent_sizes.clear()
        if self._baseline is None:
            self._baseline_samples.clear()
            self._baseline_centers.clear()
            self.status = reason
            return None
        self._invalid_frames += 1
        if self._smoothed is None:
            self.status = reason
            return None
        if self._invalid_frames <= 5:
            self.status = f"{reason}; holding last estimate"
            return self._smoothed
        # Ease toward neutral during longer occlusion instead of jumping to 5.
        self._smoothed += 0.14 * (5.0 - self._smoothed)
        self.status = f"{reason}; easing to neutral"
        return self._smoothed if abs(self._smoothed - 5.0) >= 0.05 else None

    def update(self, detection: PupilDetection | None) -> float | None:
        if detection is None or detection.confidence < 0.20:
            return self._invalid("pupil not visible")
        if self._baseline_center is not None and np.hypot(
            detection.center[0] - self._baseline_center[0],
            detection.center[1] - self._baseline_center[1],
        ) > 35:
            return self._invalid("gaze outside calibrated area")
        self._recent_sizes.append(detection.diameter_px)
        size = float(np.median(self._recent_sizes))
        if self._baseline is None:
            self._baseline_samples.append(size)
            self._baseline_centers.append(detection.center)
            if len(self._baseline_samples) < self._baseline_samples.maxlen:
                self.status = f"warming {len(self._baseline_samples)}/{self._baseline_samples.maxlen}"
                return None
            centers = np.asarray(self._baseline_centers, dtype=np.float32)
            center_spread = np.ptp(centers, axis=0)
            size_spread = max(self._baseline_samples) - min(self._baseline_samples)
            half = len(self._baseline_samples) // 2
            size_trend = abs(
                float(np.median(list(self._baseline_samples)[:half]))
                - float(np.median(list(self._baseline_samples)[half:]))
            )
            if np.max(center_spread) > 20 or size_spread > 10 or size_trend > 4:
                self.status = "warming: hold gaze steady"
                return None
            self._baseline = float(np.median(self._baseline_samples))
            self._baseline_center = tuple(np.median(centers, axis=0))
        ratio = size / self._baseline
        if not 0.50 <= ratio <= 1.70:
            return self._invalid("size outside plausible range")
        self._invalid_frames = 0
        # Match the old gain near neutral, then ease toward the limits so a
        # high response setting still has visible range instead of flatlining.
        relative_change = self.sensitivity * (ratio - 1.0)
        span = 3.0
        estimate = float(5.0 + span * np.tanh(5.0 * relative_change / span))
        if self._smoothed is None:
            self._smoothed = estimate
        else:
            response = 0.32 - 0.06 * (self.sensitivity - 1.0)
            step = response * (estimate - self._smoothed)
            self._smoothed += float(np.clip(step, -0.27, 0.27))
        self.status = "tracking"
        return self._smoothed


class RelativePupilTracker:
    def __init__(self, sensitivity: float = 1.4) -> None:
        self.eyes = (RelativePupilEye(sensitivity=sensitivity), RelativePupilEye(sensitivity=sensitivity))
        self.detections: tuple[PupilDetection | None, PupilDetection | None] = (None, None)
        self._previous_centers: list[tuple[float, float] | None] = [None, None]

    def update(
        self, strip: np.ndarray, camera_ids: list[int]
    ) -> tuple[float | None, float | None]:
        results: list[float | None] = []
        detections: list[PupilDetection | None] = []
        for eye_id in (0, 1):
            if eye_id not in camera_ids:
                detection = None
            else:
                offset = camera_ids.index(eye_id) * CAMERA_WIDTH
                detection = detect_pupil(
                    strip[:, offset:offset + CAMERA_WIDTH],
                    self._previous_centers[eye_id],
                )
            if detection is not None and detection.confidence >= 0.20:
                self._previous_centers[eye_id] = detection.center
            detections.append(detection)
            results.append(self.eyes[eye_id].update(detection))
        self.detections = (detections[0], detections[1])
        return results[0], results[1]


def encode_pupil_packet(values: tuple[float | None, float | None]) -> bytes:
    left, right = values
    flags = int(left is not None) | (int(right is not None) << 1)
    return PUPIL_PACKET.pack(PUPIL_MAGIC, 1, flags, 0, left or 0.0, right or 0.0)


class PupilBroadcaster:
    def __init__(self, port: int = 27277) -> None:
        self._address = ("127.0.0.1", port)
        self._socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._last_sent = 0.0

    def send(self, values: tuple[float | None, float | None]) -> None:
        now = time.perf_counter()
        if now - self._last_sent < 1 / 24:
            return
        self._socket.sendto(encode_pupil_packet(values), self._address)
        self._last_sent = now

    def close(self) -> None:
        try:
            self._socket.sendto(encode_pupil_packet((None, None)), self._address)
        finally:
            self._socket.close()
