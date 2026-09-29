import unittest
from dataclasses import asdict

from prepare_tongue_stills import validate_focused_arc_session
from tongue_still_capture import TONGUE_ARC_PROMPTS


def focused_session():
    prompts = [asdict(card) for card in TONGUE_ARC_PROMPTS]
    samples = []
    for index, card in enumerate(prompts):
        if card["context"].startswith("facial hair / ") or card["name"].startswith("Mid diagonal "):
            for _ in range(card["minimum_captures"]):
                samples.append({
                    "promptIndex": index,
                    "targets": dict(card["targets"]),
                    "excluded": False,
                })
    return {
        "sessionType": "tongue-stereo-arc-v3",
        "prompts": prompts,
        "samples": samples,
    }


class FocusedArcPreparationTests(unittest.TestCase):
    def test_complete_focus_cards_are_accepted(self):
        validate_focused_arc_session(focused_session())

    def test_old_arc_journal_remains_accepted(self):
        session = focused_session()
        session["prompts"] = session["prompts"][:10]
        session["samples"] = []
        validate_focused_arc_session(session)

    def test_missing_hair_pair_is_rejected(self):
        session = focused_session()
        session["prompts"].pop()
        with self.assertRaisesRegex(ValueError, "facial-hair hidden/visible pair"):
            validate_focused_arc_session(session)

    def test_undercaptured_card_is_rejected(self):
        session = focused_session()
        index = next(
            index for index, card in enumerate(session["prompts"])
            if card["name"] == "Facial hair, neutral, tongue tip"
        )
        sample = next(
            sample for sample in session["samples"] if sample["promptIndex"] == index
        )
        sample["excluded"] = True
        with self.assertRaisesRegex(ValueError, "at least 4 are required"):
            validate_focused_arc_session(session)

    def test_mislabeled_focus_sample_is_rejected(self):
        session = focused_session()
        index = next(
            index for index, card in enumerate(session["prompts"])
            if card["name"] == "Mid diagonal upper-left"
        )
        sample = next(
            sample for sample in session["samples"] if sample["promptIndex"] == index
        )
        sample["targets"]["horizontal"] = 0.5
        with self.assertRaisesRegex(ValueError, "mismatched target labels"):
            validate_focused_arc_session(session)


if __name__ == "__main__":
    unittest.main()
