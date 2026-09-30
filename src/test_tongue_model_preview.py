import unittest
from unittest import mock

import cv2
import numpy as np
import torch

from tongue_image_processing import preprocess_stereo_images
from tongue_model_preview import LiveTongueModelPreview


class _RecordingModel(torch.nn.Module):
    def __init__(self):
        super().__init__()
        self.inputs = []

    def forward(self, inputs):
        self.inputs.append(inputs)
        return torch.tensor([[0.9, 0.4, 0.2]], device=inputs.device)


class TongueModelPreviewInputTests(unittest.TestCase):
    def setUp(self):
        # Distinct low-contrast panels catch a swapped view or processing the
        # 800-pixel strip before each branch's resize.
        rows, columns = np.indices((400, 400))
        left = (70 + (rows // 7 + columns // 11) % 35).astype(np.uint8)
        right = (120 + (rows // 13 + columns // 5) % 20).astype(np.uint8)
        self.strip = np.concatenate((left, right), axis=1)

    def checkpoint(self, size=64, mode=None):
        result = {
            "targetNames": ["visibility", "extension", "horizontal"],
            "imageSize": size,
            "modelState": {},
        }
        if mode is not None:
            result["inputPreprocessing"] = mode
        return result

    def load_preview(self, gate, direction=None):
        checkpoints = [gate] if direction is None else [gate, direction]
        models = [_RecordingModel() for _ in checkpoints]
        with (
            mock.patch("tongue_model_preview.torch.load", side_effect=checkpoints),
            mock.patch("tongue_model_preview.create_model", side_effect=models),
            mock.patch("tongue_model_preview.validated_torch_device_name", return_value="cpu"),
        ):
            preview = LiveTongueModelPreview(
                "gate.pt", device_name="cpu",
                direction_checkpoint_path="direction.pt" if direction is not None else None,
            )
        return preview, models

    def expected_inputs(self, size, mode):
        resized = np.stack([
            cv2.resize(
                self.strip[:, view * 400:(view + 1) * 400], (size, size),
                interpolation=cv2.INTER_AREA,
            )
            for view in range(2)
        ])
        processed = preprocess_stereo_images(resized, mode)
        return processed.astype(np.float32)[None] / 255.0

    def test_legacy_checkpoint_matches_original_raw_pipeline_exactly(self):
        preview, models = self.load_preview(self.checkpoint())
        preview.predict(self.strip, None, [])
        original = np.empty((1, 2, 64, 64), dtype=np.float32)
        for view in range(2):
            original[0, view] = cv2.resize(
                self.strip[:, view * 400:(view + 1) * 400], (64, 64),
                interpolation=cv2.INTER_AREA,
            ).astype(np.float32) / 255.0
        self.assertEqual(preview.input_preprocessing, "raw-v1")
        np.testing.assert_array_equal(models[0].inputs[0].numpy(), original)

    def test_each_branch_uses_its_own_preprocessing_contract(self):
        for gate_mode, direction_mode in (("raw-v1", "clahe-v1"), ("clahe-v1", "raw-v1")):
            with self.subTest(gate=gate_mode, direction=direction_mode):
                preview, models = self.load_preview(
                    self.checkpoint(mode=gate_mode), self.checkpoint(mode=direction_mode)
                )
                preview.predict(self.strip, None, [])
                np.testing.assert_array_equal(
                    models[0].inputs[0].numpy(), self.expected_inputs(64, gate_mode)
                )
                np.testing.assert_array_equal(
                    models[1].inputs[0].numpy(), self.expected_inputs(64, direction_mode)
                )
                self.assertIsNot(models[0].inputs[0], models[1].inputs[0])

    def test_matching_branch_contract_reuses_inputs(self):
        preview, models = self.load_preview(
            self.checkpoint(mode="clahe-v1"), self.checkpoint(mode="clahe-v1")
        )
        preview.predict(self.strip, None, [])
        self.assertIs(models[0].inputs[0], models[1].inputs[0])

    def test_different_branch_sizes_resize_before_processing(self):
        preview, models = self.load_preview(
            self.checkpoint(size=64, mode="clahe-v1"),
            self.checkpoint(size=48, mode="clahe-v1"),
        )
        preview.predict(self.strip, None, [])
        self.assertIsNot(models[0].inputs[0], models[1].inputs[0])
        for model, size in zip(models, (64, 48)):
            np.testing.assert_array_equal(
                model.inputs[0].numpy(), self.expected_inputs(size, "clahe-v1")
            )

    def test_legacy_direction_does_not_inherit_contrast_gate_tag(self):
        preview, models = self.load_preview(
            self.checkpoint(mode="clahe-v1"), self.checkpoint()
        )
        self.assertEqual(preview.direction_input_preprocessing, "raw-v1")
        preview.predict(self.strip, None, [])
        np.testing.assert_array_equal(
            models[1].inputs[0].numpy(), self.expected_inputs(64, "raw-v1")
        )

    def test_unknown_tag_fails_for_either_branch(self):
        for gate, direction in (
            (self.checkpoint(mode="unknown-v1"), None),
            (self.checkpoint(), self.checkpoint(mode="unknown-v1")),
        ):
            with self.subTest(direction=direction is not None):
                with self.assertRaises(ValueError):
                    self.load_preview(gate, direction)


if __name__ == "__main__":
    unittest.main()
