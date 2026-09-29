import unittest

import numpy as np
import torch

from train_tongue_model import (
    SpatialStereoTongueModel,
    StereoTongueModel,
    balanced_step_weights,
    blocked_train_validation_split,
    classification_at_threshold,
    heldout_pose_metrics,
    shade_local_mouth_area,
    target_loss,
)


class TongueTrainingTests(unittest.TestCase):
    def test_split_reports_no_trainable_card_without_numpy_concatenate_error(self):
        steps = np.asarray([0, 0, 0, 1, 1, 1])
        trainable = np.asarray([True, True, True, False, False, False])
        training, validation = blocked_train_validation_split(
            steps, trainable, dataset_type="manual-stereo-stills"
        )
        self.assertEqual(training.dtype, np.int64)
        self.assertEqual(validation.dtype, np.int64)
        self.assertEqual(len(training), 0)
        self.assertEqual(len(validation), 0)

    def test_local_mouth_shading_preserves_stereo_geometry_and_evidence(self):
        images = torch.ones(2, 64, 64)
        result = shade_local_mouth_area(images, 18, 17, 22, 24, 0.28)
        self.assertEqual(tuple(result.shape), (2, 64, 64))
        self.assertTrue(torch.equal(result[0], result[1]))
        self.assertEqual(float(result[:, 0, 0].min()), 1.0)
        self.assertLess(float(result[:, 28, 28].max()), 1.0)
        self.assertGreaterEqual(float(result.min()), 0.72)

    def test_split_excludes_untrainable_frames(self):
        steps = np.repeat(np.arange(3), 100)
        trainable = np.ones(len(steps), dtype=bool)
        trainable[200:] = False
        training, validation = blocked_train_validation_split(steps, trainable, 20)
        self.assertTrue(np.all(training < 200))
        self.assertTrue(np.all(validation < 200))
        self.assertFalse(set(training) & set(validation))

    def test_balancing_equalizes_prompt_mass(self):
        steps = np.asarray([0] * 10 + [1] * 100)
        indices = np.arange(len(steps))
        weights = balanced_step_weights(steps, indices)
        self.assertAlmostEqual(float(np.sum(weights[:10])), float(np.sum(weights[10:])))

    def test_model_bounds_signed_and_unsigned_outputs(self):
        names = [
            "visibility", "extension", "horizontal", "vertical", "curl_up",
            "bend_down", "roll", "flat", "squish", "twist",
        ]
        model = StereoTongueModel(names).eval()
        output = model(torch.zeros(2, 2, 160, 160))
        self.assertEqual(tuple(output.shape), (2, 10))
        self.assertTrue(torch.all(output[:, :2] >= 0))
        self.assertTrue(torch.all(output[:, :2] <= 1))
        self.assertTrue(torch.all(output[:, 2:4] >= -1))
        self.assertTrue(torch.all(output[:, 2:4] <= 1))

    def test_spatial_stereo_model_preserves_output_contract(self):
        names = [
            "visibility", "extension", "horizontal", "vertical", "curl_up",
            "bend_down", "roll", "flat", "squish", "twist",
        ]
        model = SpatialStereoTongueModel(names).eval()
        output = model(torch.zeros(1, 2, 224, 224))
        self.assertEqual(tuple(output.shape), (1, 10))
        self.assertTrue(torch.all(output[:, [0, 1, 4, 5, 6, 7, 8]] >= 0))
        self.assertTrue(torch.all(output[:, [0, 1, 4, 5, 6, 7, 8]] <= 1))

    def test_manual_still_split_holds_out_repetitions_per_card(self):
        steps = np.repeat(np.arange(3), 6)
        trainable = np.ones(len(steps), dtype=bool)
        training, validation = blocked_train_validation_split(
            steps, trainable, dataset_type="manual-stereo-stills"
        )
        self.assertEqual(len(training), 15)
        self.assertEqual(len(validation), 3)
        self.assertFalse(set(training) & set(validation))

    def test_visibility_loss_stays_finite_at_float16_probability_limits(self):
        names = [
            "visibility", "extension", "horizontal", "vertical", "curl_up",
            "bend_down", "roll", "flat", "squish", "twist",
        ]
        prediction = torch.zeros(2, 10, dtype=torch.float16)
        prediction[0, 0] = 1.0
        prediction[1, 0] = 0.0
        target = torch.zeros(2, 10, dtype=torch.float16)
        target[0, 0] = 1.0
        self.assertTrue(torch.isfinite(target_loss(prediction, target, names)))

    def test_visibility_and_direction_checkpoints_optimize_different_errors(self):
        names = ["visibility", "extension", "horizontal", "vertical"]
        target = torch.tensor([[1.0, 1.0, 0.8, 0.0]])
        missed_visibility = torch.tensor([[0.1, 1.0, 0.8, 0.0]])
        missed_direction = torch.tensor([[1.0, 0.2, 0.0, 0.0]])
        self.assertGreater(
            target_loss(missed_visibility, target, names, "visibility"),
            target_loss(missed_visibility, target, names, "direction"),
        )
        self.assertGreater(
            target_loss(missed_direction, target, names, "direction"),
            target_loss(missed_direction, target, names, "visibility"),
        )

    def test_classification_metrics_expose_false_positive_rate(self):
        values = np.asarray([0.9, 0.8, 0.7, 0.1])
        target = np.asarray([1.0, 0.0, 1.0, 0.0])
        metrics = classification_at_threshold(values, target, 0.5)
        self.assertAlmostEqual(metrics["precision"], 2 / 3)
        self.assertEqual(metrics["recall"], 1.0)
        self.assertEqual(metrics["falsePositiveRate"], 0.5)
        self.assertEqual(metrics["falseNegativeRate"], 0.0)

    def test_heldout_pose_metrics_expose_missed_diagonals_by_card(self):
        names = ["visibility", "horizontal", "vertical"]
        target = np.asarray([
            [0.0, 0.0, 0.0], [0.0, 0.0, 0.0],
            [1.0, -0.75, 0.75], [1.0, -0.75, 0.75],
            [1.0, 0.75, -0.75], [1.0, 0.0, 0.0],
        ])
        prediction = np.asarray([
            [0.1, 0.0, 0.0], [0.9, 0.0, 0.0],
            [0.4, -0.75, 0.75], [0.8, -0.25, 0.25],
            [0.3, 0.5, -0.25], [0.9, 0.1, -0.1],
        ])
        step_ids = np.asarray([0, 0, 1, 1, 2, 3])
        native = np.zeros(len(target))
        report = heldout_pose_metrics(
            prediction, target, native, step_ids, names, 1.0, 0.5
        )
        cards = {card["promptId"]: card for card in report["perPrompt"]}
        self.assertEqual(cards[0]["visibleSamples"], 0)
        self.assertIsNone(cards[0]["visibilityFalseNegativeRate"])
        self.assertEqual(cards[0]["visibilityFalsePositiveRate"], 0.5)
        self.assertEqual(cards[1]["missedVisible"], 1)
        self.assertEqual(cards[1]["visibilityFalseNegativeRate"], 0.5)
        self.assertAlmostEqual(cards[1]["directionMae"], 0.25)
        self.assertEqual(cards[2]["visibilityFalseNegativeRate"], 1.0)
        corners = {corner["corner"]: corner for corner in report["diagonalCorners"]}
        self.assertEqual(corners["upper-left"]["visibleSamples"], 2)
        self.assertEqual(corners["upper-left"]["missedVisible"], 1)
        self.assertAlmostEqual(corners["lower-right"]["directionMae"], 0.375)
        self.assertIsNone(corners["upper-right"]["directionMae"])

        # The audit uses the selected global gate; it does not refit each card.
        native[2] = 1.0
        blended = heldout_pose_metrics(
            prediction, target, native, step_ids, names, 0.8, 0.5
        )
        self.assertEqual(blended["perPrompt"][1]["missedVisible"], 0)

    def test_heldout_pose_metrics_reject_misaligned_prompt_ids(self):
        with self.assertRaisesRegex(ValueError, "aligned"):
            heldout_pose_metrics(
                np.zeros((2, 3)), np.zeros((2, 3)), np.zeros(2),
                np.asarray([0]), ["visibility", "horizontal", "vertical"],
                1.0, 0.5,
            )


if __name__ == "__main__":
    unittest.main()
