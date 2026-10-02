import copy
import json
import os
import tempfile
import unittest
from pathlib import Path

import numpy as np
import torch

from train_cheek_model import (
    CheekFrames, assert_parent_unchanged, cheek_metrics, create_combined_model,
    extract_features, main, save_combined_checkpoint, split_cheek_indices,
    validate_independent_captures, validate_pose_coverage,
)
from train_tongue_model import CHEEK_TARGET_NAMES, create_model


NAMES = ["visibility", "horizontal", "vertical", "extension"]
ARCHITECTURES = ("legacy-late-fusion-v1", "spatial-stereo-resnet-v2")


def parent_checkpoint(architecture=ARCHITECTURES[0], size=32):
    base = create_model(architecture, NAMES).eval()
    return {
        "architecture": architecture, "targetNames": NAMES, "imageSize": size,
        "inputPreprocessing": "raw-v1", "modelState": copy.deepcopy(base.state_dict()),
        "visibilityGate": {"cameraWeight": 1.0, "threshold": .43},
    }


def make_cache(path, size=32, capture="first"):
    path.mkdir(parents=True, exist_ok=True)
    # Whole manual repetitions from each card; synthetic pixels exercise the
    # trainer's contracts, not the quality of real-world cheek inference.
    poses = np.asarray([(0, 0), (1, 0), (0, 1), (1, 1)], np.float32)
    np.save(path / "targets.npy", np.repeat(poses, 6, axis=0))
    np.save(path / "step_ids.npy", np.repeat(np.arange(4), 6))
    pixels = np.random.default_rng(77).integers(0, 255, (24, 2, size, size), dtype=np.uint8)
    np.save(path / "images.npy", pixels)
    (path / "metadata.json").write_text(json.dumps({
        "targetNames": list(CHEEK_TARGET_NAMES), "targetSource": "prompted-cheek-poses",
        "datasetType": "manual-cheek-stills", "captureId": capture,
    }), encoding="utf-8")
    return pixels


class CombinedCheekTrainingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        torch.set_num_threads(2)
        torch.manual_seed(91)

    def test_plain_models_match_their_original_forward_formulas(self):
        cameras = torch.rand(2, 2, 32, 32)
        for architecture in ARCHITECTURES:
            model = create_model(architecture, NAMES).eval()
            with torch.no_grad():
                left = model.encoder(cameras[:, 0:1])
                right = model.encoder(cameras[:, 1:2])
                fused = torch.cat((left, right, torch.abs(left - right), left * right), dim=1)
                if architecture == ARCHITECTURES[0]:
                    logits = model.fusion(fused)
                else:
                    spatial = model.stereo_fusion(fused)
                    pooled = torch.cat((
                        torch.nn.functional.adaptive_avg_pool2d(spatial, 1).flatten(1),
                        torch.nn.functional.adaptive_max_pool2d(spatial, 1).flatten(1),
                    ), dim=1)
                    logits = model.head(pooled)
                expected = torch.where(model.signed_mask, torch.tanh(logits), torch.sigmoid(logits))
                self.assertTrue(torch.equal(expected, model(cameras)), architecture)

    def test_training_cheek_head_keeps_tongue_parameters_statistics_and_outputs_exact(self):
        cameras = torch.rand(3, 2, 32, 32)
        for architecture in ARCHITECTURES:
            parent = parent_checkpoint(architecture)
            model = create_combined_model(parent)
            base = create_model(architecture, NAMES).eval()
            base.load_state_dict(parent["modelState"])
            expected = base(cameras).detach()
            old_head = copy.deepcopy(model.cheek_head.state_dict())
            optimizer = torch.optim.Adam(model.cheek_head.parameters(), lr=.01)
            model.train()
            self.assertTrue(model.training)
            self.assertFalse(model.parent.training)
            self.assertTrue(all(not layer.training for layer in model.parent.modules()))
            self.assertTrue(all(not parameter.requires_grad for parameter in model.parent.parameters()))
            for _ in range(3):
                optimizer.zero_grad()
                prediction = model(cameras)
                self.assertTrue(torch.equal(prediction[:, :-2], expected))
                prediction[:, -2:].square().mean().backward()
                optimizer.step()
            assert_parent_unchanged(model, parent["modelState"])
            self.assertTrue(torch.equal(model(cameras)[:, :-2], expected))
            self.assertTrue(any(not torch.equal(value, old_head[name])
                                for name, value in model.cheek_head.state_dict().items()))

    def test_parent_drift_and_mismatched_refinement_are_rejected(self):
        parent = parent_checkpoint()
        model = create_combined_model(parent)
        initial = {
            "architecture": "cheek-augmented-" + parent["architecture"],
            "targetNames": NAMES + list(CHEEK_TARGET_NAMES),
            "imageSize": parent["imageSize"], "modelState": copy.deepcopy(model.state_dict()),
        }
        self.assertIsNotNone(create_combined_model(parent, initial))
        initial["modelState"]["parent.encoder.network.0.weight"].add_(.1)
        with self.assertRaisesRegex(RuntimeError, "frozen tongue state changed"):
            create_combined_model(parent, initial)
        initial["imageSize"] = 64
        with self.assertRaisesRegex(ValueError, "input contract"):
            create_combined_model(parent, initial)

    def test_manual_split_preserves_unseen_repetitions_and_pose_coverage(self):
        with tempfile.TemporaryDirectory() as directory:
            cache = Path(directory)
            make_cache(cache)
            data = CheekFrames([cache], 32)
            try:
                train, validation = split_cheek_indices(data)
                self.assertFalse(set(train) & set(validation))
                self.assertEqual(len(train), 20)
                self.assertEqual(len(validation), 4)
                for card in range(4):
                    self.assertIn(card, data.steps[train])
                    self.assertIn(card, data.steps[validation])
            finally:
                data.close()
        with self.assertRaisesRegex(ValueError, "both cheeks"):
            validate_pose_coverage(np.asarray([(0, 0), (1, 0), (0, 1)]), "Example")

    def test_native_coupled_values_are_not_accepted_as_targets(self):
        with tempfile.TemporaryDirectory() as directory:
            cache = Path(directory)
            make_cache(cache)
            metadata = json.loads((cache / "metadata.json").read_text())
            metadata["targetSource"] = "factory-labels"
            (cache / "metadata.json").write_text(json.dumps(metadata))
            with self.assertRaisesRegex(ValueError, "not native face labels"):
                CheekFrames([cache], 32)

    def test_copied_cache_cannot_be_claimed_as_independent_capture(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            make_cache(root / "train", capture="train-session")
            make_cache(root / "validation", capture="other-session")
            training = CheekFrames([root / "train"], 32)
            validation = CheekFrames([root / "validation"], 32)
            try:
                with self.assertRaisesRegex(ValueError, "copy of the training images"):
                    validate_independent_captures(training, validation)
            finally:
                training.close()
                validation.close()

    def test_precomputed_features_can_train_and_never_modify_image_cache(self):
        with tempfile.TemporaryDirectory() as directory:
            cache = Path(directory)
            original = make_cache(cache)
            data = CheekFrames([cache], 32)
            try:
                model = create_combined_model(parent_checkpoint())
                features, targets = extract_features(
                    model, data, np.arange(4), "raw-v1", torch.device("cpu"), 2, 2,
                )
                self.assertEqual(tuple(features.shape), (8, 384))
                self.assertEqual(tuple(targets.shape), (8, 2))
                model.cheek_head(features).sum().backward()
                self.assertTrue(all(parameter.grad is None for parameter in model.parent.parameters()))
                np.testing.assert_array_equal(np.load(cache / "images.npy"), original)
            finally:
                data.close()

    def test_feature_normalization_prevents_large_frozen_features_from_collapsing_the_head(self):
        torch.manual_seed(311)
        model = create_combined_model(parent_checkpoint())
        target = torch.tensor([(0., 0.), (1., 0.), (0., 1.), (1., 1.)]).repeat_interleave(16, 0)
        features = torch.randn(64, 384) * .05 + 20
        features[:, 0] += target[:, 0] * 4
        features[:, 1] += target[:, 1] * 4
        original = copy.deepcopy(model.parent.state_dict())
        model.fit_cheek_feature_normalization(features)
        optimizer = torch.optim.AdamW(model.cheek_head.parameters(), lr=.005)
        for _ in range(70):
            optimizer.zero_grad()
            logits = model.cheek_logits_from_features(features)
            loss = torch.nn.functional.binary_cross_entropy_with_logits(logits, target)
            loss.backward()
            optimizer.step()
        prediction = model.cheeks_from_features(features).detach()
        self.assertLess(float((prediction - target).abs().mean()), .03)
        self.assertTrue(torch.all(model.cheek_feature_scale >= .01))
        assert_parent_unchanged(model, original)

    def test_legacy_combined_checkpoint_defaults_to_identity_normalization(self):
        model = create_combined_model(parent_checkpoint())
        old_state = {name: value for name, value in model.state_dict().items()
                     if name not in ("cheek_feature_mean", "cheek_feature_scale")}
        restored = create_combined_model(parent_checkpoint())
        restored.load_state_dict(old_state)
        features = torch.rand(3, 384)
        self.assertTrue(torch.equal(model.cheek_head(features), restored.cheeks_from_features(features)))
        self.assertTrue(torch.equal(restored.cheek_feature_mean, torch.zeros(384)))
        self.assertTrue(torch.equal(restored.cheek_feature_scale, torch.ones(384)))
        invalid = dict(restored.state_dict())
        invalid["cheek_feature_scale"] = torch.zeros(384)
        with self.assertRaisesRegex(ValueError, "positive scales"):
            restored.load_state_dict(invalid)

    def test_metrics_include_inactive_leakage_and_intermediate_strengths(self):
        target = np.asarray([(0, 0), (1, 0), (0, 1), (1, 1), (.5, 0)], np.float32)
        metrics = cheek_metrics(target.copy(), target)
        self.assertEqual(metrics["mae"], 0)
        self.assertEqual(metrics["perTarget"]["cheekPuffLeft"]["inactiveP95"], 0)
        self.assertEqual(len(metrics["perPose"]), 5)
        self.assertEqual(metrics["balancedPoseMae"], 0)
        self.assertEqual(metrics["checkpointScore"], 0)

    def test_checkpoint_selection_does_not_hide_one_sided_failure_behind_neutral_samples(self):
        target = np.asarray([(0, 0)] * 100 + [(1, 0), (0, 1), (1, 1)], np.float32)
        prediction = np.zeros_like(target)
        metrics = cheek_metrics(prediction, target)
        self.assertLess(metrics["mae"], .025)
        self.assertEqual(metrics["balancedPoseMae"], .5)
        self.assertGreaterEqual(metrics["checkpointScore"], .5)

    def test_checkpoint_roundtrip_retains_gate_and_labels_session_limits(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            parent = parent_checkpoint()
            parent_path = root / "developer.pt"
            torch.save(parent, parent_path)
            model = create_combined_model(parent)
            output = root / "combined.pt"
            save_combined_checkpoint(output, model, parent, parent_path, {"mae": .1}, 3, 20, 4, False)
            loaded = torch.load(output, weights_only=True)
            restored = create_model(loaded["architecture"], loaded["targetNames"])
            restored.load_state_dict(loaded["modelState"])
            cameras = torch.rand(1, 2, 32, 32)
            self.assertTrue(torch.equal(model.eval()(cameras), restored.eval()(cameras)))
            self.assertEqual(loaded["visibilityGate"], parent["visibilityGate"])
            self.assertTrue(loaded["experimental"])
            training = loaded["cheekTraining"]
            self.assertEqual(training["validationGrade"], "same-session-card-repetitions")
            self.assertFalse(training["independentWearerValidation"])
            self.assertFalse(training["developerPromotionApproved"])
            self.assertNotIn(str(root), str({key: value for key, value in loaded.items() if key != "modelState"}))

    def test_cpu_training_smoke_saves_a_loadable_model_without_parent_changes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            cache = root / "cache"
            make_cache(cache)
            parent = parent_checkpoint()
            parent_path = root / "developer.pt"
            torch.save(parent, parent_path)
            output = root / "combined.pt"
            self.assertEqual(main([
                str(cache), "--parent-checkpoint", str(parent_path), "--output", str(output),
                "--epochs", "2", "--batch-size", "8", "--device", "cpu",
            ]), 0)
            loaded = torch.load(output, weights_only=True)
            model = create_combined_model(parent, loaded)
            assert_parent_unchanged(model, parent["modelState"])

    @unittest.skipUnless(os.environ.get("QPRO_TEST_PARENT"), "Set QPRO_TEST_PARENT for bundled developer parity")
    def test_actual_bundled_developer_output_is_bitwise_identical(self):
        parent = torch.load(os.environ["QPRO_TEST_PARENT"], map_location="cpu", weights_only=True)
        base = create_model(parent["architecture"], list(parent["targetNames"])).eval()
        base.load_state_dict(parent["modelState"])
        combined = create_combined_model(parent).eval()
        cameras = torch.rand(1, 2, int(parent["imageSize"]), int(parent["imageSize"]))
        with torch.no_grad():
            self.assertTrue(torch.equal(base(cameras), combined(cameras)[:, :-2]))
        assert_parent_unchanged(combined, parent["modelState"])


if __name__ == "__main__":
    unittest.main()
