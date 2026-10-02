"""Training orchestration safety checks; no headset, downloads or GPU required."""

import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import torch

import lower_face_training as lower
import train_cheek_pair as pair


def plain():
    return {"architecture": "spatial-stereo-resnet-v2", "imageSize": 224,
            "targetNames": ["visible", "horizontal", "vertical"],
            "inputPreprocessing": "raw-v1", "modelState": {"encoder.weight": torch.tensor([1., 2.])}}


def combined(parent, changed=False):
    return {**parent, "architecture": lower.CHEEK_ARCHITECTURE_PREFIX + parent["architecture"],
            "baseArchitecture": parent["architecture"],
            "targetNames": parent["targetNames"] + list(lower.CHEEK_TARGET_NAMES),
            "modelState": {"parent." + key: value + int(changed) for key, value in parent["modelState"].items()},
            "experimental": True, "cheekTraining": {"frozenTongueParent": True}}


class TrainingSafetyTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.stage = self.root / "training" / "lower-face-run-test"
        (self.stage / "models").mkdir(parents=True)
        self.direction = self.stage / "models" / "qpro-stereo-tongue-v7-direction.pt"
        self.gate = self.stage / "models" / "qpro-stereo-tongue-v7-gate.pt"
        self.session = self.root / "sample.qpsession.json"
        self.session.write_text("{}", encoding="utf-8")
        torch.save(plain(), self.direction)
        torch.save(plain(), self.gate)

    def tearDown(self):
        self.temporary.cleanup()

    def trainer(self, arguments, changed=False):
        parent = torch.load(arguments[arguments.index("--parent-checkpoint") + 1], weights_only=True)
        torch.save(combined(parent, changed), arguments[arguments.index("--output") + 1])
        return 0

    def test_unwrap_plain_and_combined_keeps_original_tensors(self):
        parent = plain()
        self.assertIs(lower.tongue_parent(parent), parent)
        checkpoint = combined(parent)
        restored = lower.tongue_parent(checkpoint)
        self.assertEqual(restored["targetNames"], parent["targetNames"])
        self.assertTrue(torch.equal(restored["modelState"]["encoder.weight"], parent["modelState"]["encoder.weight"]))
        self.assertNotIn("cheekTraining", restored)
        self.assertNotIn("experimental", restored)
        self.assertIn("cheekTraining", checkpoint)

    def test_unwrap_rejects_missing_parent_and_ambiguous_schema(self):
        for broken in ({**combined(plain()), "modelState": {}},
                       {**combined(plain()), "baseArchitecture": "other"},
                       {**combined(plain()), "targetNames": ["visible", "cheekPuffRight", "cheekPuffLeft"]}):
            with self.assertRaises(ValueError):
                lower.tongue_parent(broken)

    def test_attach_refuses_published_models(self):
        with self.assertRaisesRegex(ValueError, "private training"):
            lower.attach_cheeks(self.session, self.direction, self.root, 7)

    def test_attach_training_failure_keeps_plain_parent(self):
        before = self.direction.read_bytes()
        with patch.object(lower, "prepare"), patch.object(lower, "train_head", return_value=1):
            with self.assertRaises(RuntimeError):
                lower.attach_cheeks(self.session, self.direction, self.stage, 7)
        self.assertEqual(self.direction.read_bytes(), before)
        self.assertFalse(self.direction.with_name("qpro-stereo-tongue-v7.metadata.json").exists())

    def test_attach_rejects_mutated_tongue_candidate(self):
        before = self.direction.read_bytes()
        with patch.object(lower, "prepare"), patch.object(lower, "train_head", side_effect=lambda args: self.trainer(args, True)):
            with self.assertRaisesRegex(ValueError, "changed its tongue"):
                lower.attach_cheeks(self.session, self.direction, self.stage, 7)
        self.assertEqual(self.direction.read_bytes(), before)

    def test_attach_then_publish_combined_pair_omits_stale_script(self):
        old_script = self.direction.with_suffix(".torchscript.pt")
        old_script.write_bytes(b"plain tongue only")
        with patch.object(lower, "prepare"), patch.object(lower, "train_head", side_effect=self.trainer):
            lower.attach_cheeks(self.session, self.direction, self.stage, 7)
        self.assertFalse(old_script.exists())
        lower.publish_pair(self.stage, self.root, 7)
        model_root = self.root / "models"
        self.assertEqual(len(list(model_root.iterdir())), 3)
        metadata = json.loads((model_root / "qpro-stereo-tongue-v7.metadata.json").read_text())
        self.assertIs(metadata["hasCameraCheeks"], True)
        self.assertTrue(torch.equal(lower.tongue_parent(torch.load(model_root / self.direction.name, weights_only=True))["modelState"]["encoder.weight"], plain()["modelState"]["encoder.weight"]))

    def test_publish_plain_pair_keeps_matching_scripts(self):
        script = self.gate.with_suffix(".torchscript.pt")
        script.write_bytes(b"gate script")
        lower.publish_pair(self.stage, self.root, 7)
        self.assertEqual((self.root / "models" / script.name).read_bytes(), b"gate script")

    def test_publish_rejects_combined_without_capability_metadata(self):
        torch.save(combined(plain()), self.direction)
        with self.assertRaisesRegex(ValueError, "capability metadata"):
            lower.publish_pair(self.stage, self.root, 7)
        self.assertFalse((self.root / "models").exists())

    def test_publish_preserves_any_existing_version_artifact(self):
        models = self.root / "models"
        models.mkdir()
        occupied = models / "qpro-stereo-tongue-v7-direction.torchscript.pt"
        occupied.write_bytes(b"keep")
        with self.assertRaisesRegex(ValueError, "already occupied"):
            lower.publish_pair(self.stage, self.root, 7)
        self.assertEqual(occupied.read_bytes(), b"keep")
        self.assertEqual(len(list(models.iterdir())), 1)

    def test_publish_failure_rolls_back_only_our_files(self):
        import os
        real_move = os.rename if os.name == "nt" else os.link
        calls = 0

        def interrupted(source, destination):
            nonlocal calls
            calls += 1
            if calls == 2:
                Path(destination).write_bytes(b"another trainer won")
                raise FileExistsError("race")
            return real_move(source, destination)

        operation = "rename" if os.name == "nt" else "link"
        with patch.object(lower.os, operation, side_effect=interrupted):
            with self.assertRaises(FileExistsError):
                lower.publish_pair(self.stage, self.root, 7)
        models = self.root / "models"
        self.assertEqual([path.name for path in models.iterdir()], [self.direction.name])
        self.assertEqual((models / self.direction.name).read_bytes(), b"another trainer won")

    def test_attach_metadata_failure_restores_staged_direction(self):
        before = self.direction.read_bytes()
        real_replace = Path.replace

        def fail_metadata(path, target):
            if path.name == "metadata.json":
                raise OSError("simulated metadata failure")
            return real_replace(path, target)

        with patch.object(lower, "prepare"), patch.object(lower, "train_head", side_effect=self.trainer), patch.object(Path, "replace", fail_metadata):
            with self.assertRaises(OSError):
                lower.attach_cheeks(self.session, self.direction, self.stage, 7)
        self.assertEqual(self.direction.read_bytes(), before)

    def test_standalone_pair_can_extend_combined_parent_without_replacing_it(self):
        torch.save(combined(plain()), self.direction)
        self.direction.with_name("qpro-stereo-tongue-v7.metadata.json").write_text(
            json.dumps({"modelKind": "mustachio-experimental", "displayName": "Mustachio", "isExperimental": True}),
            encoding="utf-8")
        original = self.direction.read_bytes()
        arguments_seen = []

        def train(arguments):
            arguments_seen.extend(arguments)
            return self.trainer(arguments)

        with patch.object(pair, "prepare"), patch.object(pair, "train_head", side_effect=train):
            pair.train_pair(self.session, self.direction, self.gate, 8, self.root)
        self.assertEqual(self.direction.read_bytes(), original)
        self.assertIn("--initial-checkpoint", arguments_seen)
        metadata = json.loads((self.root / "models" / "qpro-stereo-tongue-v8.metadata.json").read_text())
        self.assertTrue(metadata["hasCameraCheeks"])
        self.assertTrue(metadata["isExperimental"])
        self.assertEqual(metadata["parentModelKind"], "mustachio-experimental")
        self.assertEqual(metadata["parentVersion"], 7)
        self.assertEqual(metadata["parentDisplayName"], "Mustachio")
        self.assertEqual(len(metadata["tongueParentSha256"]), 64)


if __name__ == "__main__":
    unittest.main()
