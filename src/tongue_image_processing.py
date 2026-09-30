"""Versioned grayscale preprocessing shared by tongue training and inference."""

import cv2
import numpy as np


MODES = ("raw-v1", "clahe-v1")


def _validate_mode(mode: str) -> None:
    if not isinstance(mode, str) or mode not in MODES:
        raise ValueError(f"Unsupported tongue input preprocessing: {mode!r}")


def resolve_input_preprocessing(checkpoint: dict) -> str:
    """Keep legacy checkpoints raw; explicit modes must be understood."""
    if not isinstance(checkpoint, dict):
        raise TypeError("The tongue checkpoint metadata must be a dictionary.")
    mode = checkpoint.get("inputPreprocessing", "raw-v1")
    _validate_mode(mode)
    return mode


def preprocess_stereo_images(images: np.ndarray, mode: str) -> np.ndarray:
    """Process two uint8 grayscale views without changing their geometry.

    Raw mode returns the unmodified input. CLAHE increases local contrast in
    recorded pixels; it cannot recover detail hidden by hair or saturation.
    Callers resize first and apply this same mode in training and inference.
    """
    _validate_mode(mode)
    if not isinstance(images, np.ndarray):
        raise TypeError("Tongue images must be a NumPy array.")
    if images.dtype != np.uint8:
        raise TypeError("Tongue images must have uint8 dtype.")
    if images.ndim != 3 or images.shape[0] != 2:
        raise ValueError("Tongue images must have shape (2, height, width).")
    if images.shape[1] == 0 or images.shape[2] == 0:
        raise ValueError("Tongue image height and width must be positive.")
    if mode == "raw-v1":
        return images

    clahe = cv2.createCLAHE(clipLimit=1.5, tileGridSize=(8, 8))
    processed = np.empty_like(images)
    for index, original in enumerate(images):
        original = np.ascontiguousarray(original)
        equalized = clahe.apply(original)
        blended = cv2.addWeighted(original, 0.5, equalized, 0.5, 0.0)
        # Clipped pixels contain no recoverable detail. Keep their original
        # endpoints rather than making the display imply recovered texture.
        clipped = (original == 0) | (original == 255)
        blended[clipped] = original[clipped]
        processed[index] = blended
    return processed
