"""Choose a tongue visibility gate from held-out prompted examples."""

from __future__ import annotations

import numpy as np


def classification_at_threshold(
    values: np.ndarray, target: np.ndarray, threshold: float
) -> dict[str, float]:
    predicted = values >= threshold
    expected = target >= 0.5
    true_positive = int(np.count_nonzero(predicted & expected))
    false_positive = int(np.count_nonzero(predicted & ~expected))
    false_negative = int(np.count_nonzero(~predicted & expected))
    true_negative = int(np.count_nonzero(~predicted & ~expected))
    precision = true_positive / max(1, true_positive + false_positive)
    recall = true_positive / max(1, true_positive + false_negative)
    return {
        "f1": 2.0 * true_positive / max(
            1, 2 * true_positive + false_positive + false_negative
        ),
        "precision": precision,
        "recall": recall,
        "falsePositiveRate": false_positive / max(1, false_positive + true_negative),
        "falseNegativeRate": false_negative / max(1, false_negative + true_positive),
    }


def f1_at_threshold(values: np.ndarray, target: np.ndarray, threshold: float) -> float:
    return classification_at_threshold(values, target, threshold)["f1"]


def choose_visibility_gate(
    camera: np.ndarray, native: np.ndarray, target: np.ndarray
) -> tuple[float, float, dict[str, float]]:
    """Prefer a central threshold on equal scores, and permit camera-only use."""
    if not (len(camera) == len(native) == len(target)) or not len(target):
        raise ValueError("Visibility calibration needs equally sized nonempty arrays")
    if not (np.all(np.isfinite(camera)) and np.all(np.isfinite(native))
            and np.all(np.isfinite(target))):
        raise ValueError("Visibility calibration contains non-finite values")

    best_rank: tuple[float, float, float, float] | None = None
    best: tuple[float, float, dict[str, float]] | None = None
    for camera_weight in np.linspace(0.50, 1.0, 11):
        fused = camera_weight * camera + (1.0 - camera_weight) * native
        for threshold in np.linspace(0.15, 0.85, 71):
            result = classification_at_threshold(fused, target, float(threshold))
            # The previous tuple comparison preferred 0.85 whenever a range
            # of thresholds had the same F1/precision. That missed weaker but
            # valid tongue appearances. Keep a central 0.5 operating point on
            # ties; prefer camera evidence if native labels add no benefit.
            rank = (
                result["f1"], result["precision"],
                -abs(float(threshold) - 0.5), float(camera_weight),
            )
            if best_rank is None or rank > best_rank:
                best_rank = rank
                best = (float(camera_weight), float(threshold), result)
    assert best is not None
    return best
