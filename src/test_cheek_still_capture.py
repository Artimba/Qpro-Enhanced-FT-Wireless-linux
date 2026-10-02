"""Prompted cheek capture journals exact synchronized stills, not native labels."""

import json
import tempfile
import unittest
from pathlib import Path

import numpy as np

from capture_format import CaptureWriter, TRANSPORT_HEADER, inspect_capture
from cheek_still_capture import (
    CHEEK_STILL_PROMPTS, CHEEK_TARGET_NAMES, CheekStillCaptureSession,
    LOWER_FACE_REFINEMENT_PROMPTS, LOWER_FACE_STILL_PROMPTS, LowerFaceCaptureSession,
)
from tongue_still_capture import TONGUE_REFINEMENT_PROMPTS, TONGUE_STILL_PROMPTS


class CheekCaptureTests(unittest.TestCase):
    def test_cards_cover_individual_intermediate_and_bilateral_strengths(self):
        self.assertEqual(len(CHEEK_STILL_PROMPTS), 21)
        self.assertEqual(len({card.name for card in CHEEK_STILL_PROMPTS}), 21)
        targets = {(card.targets["cheekPuffLeft"], card.targets["cheekPuffRight"])
                   for card in CHEEK_STILL_PROMPTS}
        for level in (0.25, 0.5, 1.0):
            self.assertIn((level, 0), targets)
            self.assertIn((0, level), targets)
            self.assertIn((level, level), targets)
        for card in CHEEK_STILL_PROMPTS:
            self.assertEqual(set(card.targets), set(CHEEK_TARGET_NAMES))

    def test_space_saves_one_pair_and_undo_preserves_raw_frame(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            writer = CaptureWriter(root / "sample.qpcap")
            session = CheekStillCaptureSession(root / "sample.qpsession.json")
            payload = np.hstack((np.full((400, 400), 32, np.uint8),
                                 np.full((400, 400), 87, np.uint8))).tobytes()
            header = TRANSPORT_HEADER.pack(b"QPLIVE3\0", 3, TRANSPORT_HEADER.size, 1, 2,
                                           800, 400, 800, 1, len(payload), 0x0C, 0)
            try:
                self.assertEqual(session.handle_key(" "), "started")
                self.assertTrue(session.consume_frame(writer, header, payload, 10, 20))
                self.assertFalse(session.consume_frame(writer, header, payload, 11, 21))
                self.assertEqual(session.handle_key("x"), "back")
                session.finish(False)
            finally:
                writer.close(False)
            saved = json.loads(session.path.read_text())
            self.assertEqual(saved["sessionType"], "cheek-stereo-stills-v1")
            self.assertEqual(saved["samples"][0]["targets"], CHEEK_STILL_PROMPTS[0].targets)
            self.assertTrue(saved["samples"][0]["excluded"])
            self.assertFalse(saved["completed"])
            self.assertEqual(inspect_capture(writer.path)["scanned_frames"], 1)

    def test_next_requires_minimum_repetitions(self):
        with tempfile.TemporaryDirectory() as directory:
            session = CheekStillCaptureSession(Path(directory) / "session.json")
            self.assertEqual(session.handle_key("\r"), "not_ready")
            self.assertEqual(session.current_index, 0)
            self.assertFalse(session.completed)

    def test_cannot_finish_curriculum_by_skipping_all_cards(self):
        with tempfile.TemporaryDirectory() as directory:
            session = CheekStillCaptureSession(Path(directory) / "session.json")
            for _ in CHEEK_STILL_PROMPTS:
                session.handle_key("k")
            self.assertFalse(session.completed)

    def test_lower_face_curricula_append_cheeks_without_changing_legacy_tongue_cards(self):
        for combined, original in ((LOWER_FACE_REFINEMENT_PROMPTS, TONGUE_REFINEMENT_PROMPTS),
                                   (LOWER_FACE_STILL_PROMPTS, TONGUE_STILL_PROMPTS)):
            self.assertEqual(combined[:len(original)], original)
            self.assertEqual(combined[len(original):], CHEEK_STILL_PROMPTS)
            self.assertEqual(len(combined), len(original) + 21)

    def test_combined_session_switches_guidance_and_requires_every_cheek_card(self):
        with tempfile.TemporaryDirectory() as directory:
            for refinement in (False, True):
                session = LowerFaceCaptureSession(Path(directory) / f"session-{refinement}.json",
                                                  refinement=refinement)
                self.assertFalse(session.is_cheek_card)
                self.assertEqual(session.session_type, "lower-face-refinement-v1" if refinement else "lower-face-stills-v1")
                for _ in range(session.tongue_card_count):
                    session.handle_key("k")
                self.assertTrue(session.is_cheek_card)
                self.assertEqual(session.current, CHEEK_STILL_PROMPTS[0])
                self.assertEqual(session.handle_key("k"), "not_ready")
                self.assertFalse(session.completed)
                rendered = session.render(np.full((400, 800), 100, np.uint8), False)
                self.assertEqual(rendered.shape, (820, 1280, 3))


if __name__ == "__main__":
    unittest.main()
