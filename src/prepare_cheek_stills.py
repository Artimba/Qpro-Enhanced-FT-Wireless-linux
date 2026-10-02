"""Prepare manually labeled cheek pairs without using native cheek weights."""

import argparse
import hashlib
import json
from collections import Counter
from pathlib import Path

import cv2
import numpy as np

from capture_format import scan_stereo_mouth_stills, inspect_capture
from cheek_still_capture import (
    CHEEK_STILL_PROMPTS, CHEEK_TARGET_NAMES, LOWER_FACE_SESSION_TYPES,
    prompts_for_lower_face_session,
)


def validate_cheek_session(session: dict, frame_count: int | None = None) -> tuple[list[dict], int]:
    """Validate every journal label; return usable cheek samples and their offset.

    Combined captures retain their original tongue cards as a prefix. Their
    cheek suffix has its own targets: a tongue-out cheek card must never become
    a hidden-tongue example just because it lacks a visibility target.
    """
    session_type = session.get("sessionType")
    if session_type not in LOWER_FACE_SESSION_TYPES | {"cheek-stereo-stills-v1"} or session.get("completed") is not True:
        raise ValueError("Complete the cheek camera cards before training")
    expected_cards = (prompts_for_lower_face_session(session_type)
                      if session_type in LOWER_FACE_SESSION_TYPES else CHEEK_STILL_PROMPTS)
    cheek_offset = len(expected_cards) - len(CHEEK_STILL_PROMPTS)
    cards = session.get("prompts", [])
    if not isinstance(cards, list) or len(cards) != len(expected_cards) or any(
        not isinstance(card, dict) or card.get("name") != expected.name or
        card.get("targets") != expected.targets
        for card, expected in zip(cards, expected_cards)
    ):
        raise ValueError("The cheek session does not match the capture curriculum")

    def integer(value):
        if type(value) is not int:
            raise ValueError("Cheek frame and pose indices must be integers")
        return value

    skipped = session.get("skippedPrompts", [])
    if not isinstance(skipped, list) or any(
        not 0 <= integer(step) < len(cards) for step in skipped
    ):
        raise ValueError("The cheek session has invalid skipped-card indices")
    if any(step >= cheek_offset for step in skipped):
        raise ValueError("Recapture skipped cheek cards before training")
    samples = session.get("samples", [])
    if not isinstance(samples, list) or not samples or any(not isinstance(sample, dict) for sample in samples):
        raise ValueError("The cheek session contains no labeled pairs")
    frame_indices = [integer(sample.get("frameIndex")) for sample in samples]
    if any(index < 0 for index in frame_indices) or len(set(frame_indices)) != len(frame_indices) or (
        frame_count is not None and set(frame_indices) != set(range(frame_count))
    ):
        raise ValueError("Every cheek still must have one matching prompt label")
    for sample in samples:
        step = integer(sample.get("promptIndex"))
        if not 0 <= step < len(cards) or sample.get("targets") != expected_cards[step].targets or \
                sample.get("promptName") != expected_cards[step].name:
            raise ValueError("A captured cheek label does not match its pose card")
        if type(sample.get("excluded", False)) is not bool:
            raise ValueError("Cheek exclusion flags must be boolean")
    usable = [sample for sample in samples if sample["promptIndex"] >= cheek_offset and not sample.get("excluded")]
    counts = Counter(sample["promptIndex"] for sample in usable)
    for index, card in enumerate(CHEEK_STILL_PROMPTS):
        if counts[index + cheek_offset] < card.minimum_captures:
            raise ValueError(f"{card.name} needs at least {card.minimum_captures} usable stills")
    return usable, cheek_offset


def prepare(capture_path: Path, session_path: Path, output: Path, size: int = 224) -> dict:
    session = json.loads(session_path.read_text(encoding="utf-8"))
    # Journal errors are rejected before opening the camera file or cache.
    validate_cheek_session(session)
    summary = inspect_capture(capture_path)
    if not summary["completed"] or summary["truncated"]:
        raise ValueError("The cheek camera recording is incomplete")
    entries, width = scan_stereo_mouth_stills(capture_path)
    usable, cheek_offset = validate_cheek_session(session, len(entries))
    # Validate everything before writing a cache, including skipped/edited cards.
    targets, step_ids = [], []
    for sample in usable:
        step = sample["promptIndex"]
        targets.append([sample["targets"][key] for key in CHEEK_TARGET_NAMES])
        step_ids.append(step - cheek_offset)
    if not 128 <= size <= 320:
        raise ValueError("Cheek input size must be between 128 and 320")
    if output.exists() and any(output.iterdir()):
        raise ValueError("Use an empty output directory; existing training data will not be overwritten")
    output.mkdir(parents=True, exist_ok=True)
    images = np.lib.format.open_memmap(output / "images.npy", mode="w+", dtype=np.uint8,
                                     shape=(len(usable), 2, size, size))
    with capture_path.open("rb") as stream:
        for index, sample in enumerate(usable):
            offset, _ = entries[int(sample["frameIndex"])]
            stream.seek(offset)
            pixels = stream.read(width * 400)
            if len(pixels) != width * 400:
                raise ValueError("A cheek camera pair is truncated")
            strip = np.frombuffer(pixels, dtype=np.uint8).reshape(400, width)
            for eye in range(2):
                images[index, eye] = cv2.resize(strip[:, eye * 400:(eye + 1) * 400],
                    (size, size), interpolation=cv2.INTER_AREA)
    images.flush()
    np.save(output / "targets.npy", np.asarray(targets, dtype=np.float32))
    np.save(output / "step_ids.npy", np.asarray(step_ids, dtype=np.int16))
    capture_id = hashlib.sha256(capture_path.read_bytes()).hexdigest()
    metadata = {"version": 1, "datasetType": "manual-cheek-stills", "targetSource": "prompted-cheek-poses",
                "targetNames": CHEEK_TARGET_NAMES, "captureId": capture_id, "imageSize": size,
                "frames": len(usable), "complete": True, "cameraOrder": [2, 3],
                "strengthLabels": "wearer-performed approximate fractions, not measured pressure"}
    metadata["sessionType"] = session["sessionType"]
    (output / "metadata.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
    print(f"Prepared {len(usable)} cheek pairs. Native cheek weights were not used as training targets.", flush=True)
    return metadata


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--session", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--size", type=int, default=224)
    args = parser.parse_args()
    prepare(args.capture, args.session or args.capture.with_suffix(".qpsession.json"), args.output, args.size)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
