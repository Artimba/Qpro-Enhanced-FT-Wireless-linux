"""Preparation rejects edited labels and incomplete experimental cheek captures."""

import copy
import json
import tempfile
import unittest
from unittest.mock import patch
from dataclasses import asdict
from pathlib import Path

import numpy as np

from capture_format import CaptureWriter, FILE_HEADER, TRANSPORT_HEADER
from cheek_still_capture import (
    CHEEK_STILL_PROMPTS, CHEEK_TARGET_NAMES, LOWER_FACE_REFINEMENT_PROMPTS, LOWER_FACE_STILL_PROMPTS,
)
from prepare_cheek_stills import prepare
from tongue_still_capture import TONGUE_REFINEMENT_PROMPTS, TONGUE_STILL_PROMPTS


class CheekPreparationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.directory = tempfile.TemporaryDirectory()
        cls.root = Path(cls.directory.name)
        cls.session = {"version": 1, "sessionType": "cheek-stereo-stills-v1", "completed": True,
                       "skippedPrompts": [], "prompts": [asdict(card) for card in CHEEK_STILL_PROMPTS], "samples": []}
        cls.capture = cls.root / "input.qpcap"
        writer = CaptureWriter(cls.capture)
        # Face layout includes camera4; preprocessing must keep cameras2/3.
        payload = np.hstack([np.full((400, 400), value, np.uint8) for value in (22, 66, 200)]).tobytes()
        try:
            for step, card in enumerate(CHEEK_STILL_PROMPTS):
                for _ in range(card.minimum_captures + 1):
                    index = writer.frame_count
                    header = TRANSPORT_HEADER.pack(b"QPLIVE3\0", 3, TRANSPORT_HEADER.size, index + 1, 2,
                                                  1200, 400, 1200, 1, len(payload), 0x1C, 0)
                    writer.write(header, payload, 1_000_000_000 + index * 50_000_000, index + 20)
                    cls.session["samples"].append({"frameIndex": index, "promptIndex": step,
                        "promptName": card.name, "targets": dict(card.targets), "excluded": False})
        finally:
            writer.close()

    @classmethod
    def tearDownClass(cls):
        cls.directory.cleanup()

    def run_prepare(self, session=None):
        case = Path(tempfile.mkdtemp(dir=self.root))
        journal = case / "session.json"
        journal.write_text(json.dumps(session if session is not None else self.session), encoding="utf-8")
        output = case / "cache"
        return prepare(self.capture, journal, output, 128), output

    def assert_rejected_before_cache(self, session):
        case = Path(tempfile.mkdtemp(dir=self.root))
        journal, output = case / "session.json", case / "cache"
        journal.write_text(json.dumps(session), encoding="utf-8")
        with self.assertRaises(ValueError):
            prepare(self.capture, journal, output, 128)
        self.assertFalse(output.exists())

    def test_complete_dataset_uses_mouth_cameras_and_subjective_strength_metadata(self):
        metadata, output = self.run_prepare()
        images = np.load(output / "images.npy", mmap_mode="r")
        self.assertEqual(images.shape, (len(self.session["samples"]), 2, 128, 128))
        self.assertTrue(np.all(images[:, 0] == 22))
        self.assertTrue(np.all(images[:, 1] == 66))
        self.assertEqual(metadata["cameraOrder"], [2, 3])
        self.assertEqual(metadata["targetNames"], CHEEK_TARGET_NAMES)
        self.assertIn("approximate", metadata["strengthLabels"])
        targets = np.load(output / "targets.npy")
        step_ids = np.load(output / "step_ids.npy")
        for index, step in enumerate(step_ids):
            np.testing.assert_array_equal(targets[index], [CHEEK_STILL_PROMPTS[step].targets[key] for key in CHEEK_TARGET_NAMES])

    def test_incomplete_session_is_rejected(self):
        session = copy.deepcopy(self.session)
        session["completed"] = False
        self.assert_rejected_before_cache(session)

    def test_incomplete_camera_recording_is_rejected(self):
        with self.capture.open("r+b") as stream:
            original = stream.read(FILE_HEADER.size)
            fields = list(FILE_HEADER.unpack(original))
            fields[6] = 0
            stream.seek(0)
            stream.write(FILE_HEADER.pack(*fields))
        try:
            self.assert_rejected_before_cache(self.session)
        finally:
            with self.capture.open("r+b") as stream:
                stream.write(original)

    def test_skipped_card_is_rejected(self):
        session = copy.deepcopy(self.session)
        session["skippedPrompts"] = [0]
        self.assert_rejected_before_cache(session)

    def test_wrong_target_and_edited_curriculum_are_rejected(self):
        for field in ("sample", "card"):
            session = copy.deepcopy(self.session)
            values = session["samples"][0] if field == "sample" else session["prompts"][0]
            values["targets"]["cheekPuffLeft"] = 0.5
            self.assert_rejected_before_cache(session)

    def test_fractional_and_boolean_indices_are_rejected(self):
        for field in ("frameIndex", "promptIndex"):
            for value in (0.5, False):
                session = copy.deepcopy(self.session)
                session["samples"][0][field] = value
                self.assert_rejected_before_cache(session)

    def test_wrong_prompt_name_and_excluded_edited_label_are_rejected(self):
        session = copy.deepcopy(self.session)
        session["samples"][0]["promptName"] = CHEEK_STILL_PROMPTS[-1].name
        self.assert_rejected_before_cache(session)
        session = copy.deepcopy(self.session)
        session["samples"][0]["excluded"] = True
        session["samples"][0]["targets"]["cheekPuffLeft"] = 1.0
        self.assert_rejected_before_cache(session)

    def test_missing_duplicate_and_undercaptured_labels_are_rejected(self):
        for mode in ("missing", "duplicate", "undercaptured"):
            session = copy.deepcopy(self.session)
            if mode == "missing":
                session["samples"].pop()
            elif mode == "duplicate":
                session["samples"][0]["frameIndex"] = 1
            else:
                for sample in session["samples"]:
                    if sample["promptIndex"] == 0:
                        sample["excluded"] = True
            self.assert_rejected_before_cache(session)

    def test_existing_cache_is_preserved(self):
        case = Path(tempfile.mkdtemp(dir=self.root))
        output = case / "cache"
        output.mkdir()
        marker = output / "user-training-data.txt"
        marker.write_text("preserve this")
        journal = case / "session.json"
        journal.write_text(json.dumps(self.session))
        with self.assertRaises(ValueError):
            prepare(self.capture, journal, output, 128)
        self.assertEqual(marker.read_text(), "preserve this")

    def combined_session(self, refinement=True):
        session = copy.deepcopy(self.session)
        original = TONGUE_REFINEMENT_PROMPTS if refinement else TONGUE_STILL_PROMPTS
        combined = LOWER_FACE_REFINEMENT_PROMPTS if refinement else LOWER_FACE_STILL_PROMPTS
        session["sessionType"] = "lower-face-refinement-v1" if refinement else "lower-face-stills-v1"
        session["prompts"] = [asdict(card) for card in combined]
        for sample in session["samples"]:
            sample["promptIndex"] += len(original)
        # One recorded visible-tongue prefix frame, then all required cheek
        # cards. This tests supervision isolation, not tongue training coverage.
        step = next(index for index, card in enumerate(original) if card.targets.get("visibility"))
        session["samples"][0].update(promptIndex=step, promptName=original[step].name,
                                     targets=dict(original[step].targets))
        return session

    def test_both_combined_curricula_extract_cheeks_only(self):
        for refinement in (True, False):
            session = self.combined_session(refinement)
            metadata, output = self.run_prepare(session)
            self.assertEqual(metadata["frames"], len(session["samples"]) - 1)
            self.assertEqual(metadata["sessionType"], session["sessionType"])
            steps = np.load(output / "step_ids.npy")
            self.assertEqual((int(steps.min()), int(steps.max())), (0, 20))
            self.assertEqual(metadata["targetSource"], "prompted-cheek-poses")

    def test_combined_rejects_missing_or_changed_cheek_suffix(self):
        for edit in ("missing", "reordered", "skipped", "undercaptured"):
            session = self.combined_session()
            offset = len(TONGUE_REFINEMENT_PROMPTS)
            if edit == "missing":
                session["prompts"].pop()
            elif edit == "reordered":
                session["prompts"][-1], session["prompts"][-2] = session["prompts"][-2], session["prompts"][-1]
            elif edit == "skipped":
                session["skippedPrompts"] = [offset]
            else:
                for sample in session["samples"]:
                    if sample["promptIndex"] == offset:
                        sample["excluded"] = True
            self.assert_rejected_before_cache(session)

    def test_combined_accepts_optional_legacy_tongue_skip(self):
        session = self.combined_session()
        session["skippedPrompts"] = [0]
        metadata, output = self.run_prepare(session)
        self.assertEqual(metadata["frames"], len(session["samples"]) - 1)

    def test_combined_tongue_preparation_masks_cheeks_and_does_not_require_native_cheek_labels(self):
        from prepare_tongue_stills import main
        from tongue_calibration import TONGUE_TARGET_NAMES
        session = self.combined_session()
        case = Path(tempfile.mkdtemp(dir=self.root))
        journal, output, labels = case / "session.json", case / "tongue-cache", case / "labels.jsonl"
        journal.write_text(json.dumps(session))
        # Factory references stop before the cheek section. Tongue frame0 is
        # exactly aligned; every cheek row is >35ms from the last native label.
        labels.write_text(json.dumps({"type": "schema", "names": ["TongueOut"]}) + "\n" +
                          json.dumps({"type": "sample", "arrivalMonotonicNs": 1_000_000_000, "values": [0.314]}) + "\n")
        with patch("sys.argv", ["prepare_tongue_stills", str(self.capture), "--session", str(journal),
                                "--labels", str(labels), "--output", str(output), "--size", "128"]):
            self.assertEqual(main(), 0)
        metadata = json.loads((output / "metadata.json").read_text())
        tongue = np.load(output / "targets.npy")
        tongue_mask = np.load(output / "trainable.npy")
        cheeks = np.load(output / "cheek_targets.npy")
        cheek_mask = np.load(output / "cheek_trainable.npy")
        self.assertEqual(metadata["targetNames"], list(TONGUE_TARGET_NAMES))
        self.assertEqual(tongue.shape, (len(session["samples"]), len(TONGUE_TARGET_NAMES)))
        self.assertEqual(int(tongue_mask.sum()), 1)
        self.assertTrue(tongue_mask[0])
        self.assertEqual(int(cheek_mask.sum()), len(session["samples"]) - 1)
        self.assertFalse(cheek_mask[0])
        self.assertFalse(np.any(tongue_mask & cheek_mask))
        self.assertEqual(metadata["maximumLabelErrorMs"], 0.0)
        for index, sample in enumerate(session["samples"][1:], 1):
            np.testing.assert_array_equal(cheeks[index], [sample["targets"][key] for key in CHEEK_TARGET_NAMES])


if __name__ == "__main__":
    unittest.main()
