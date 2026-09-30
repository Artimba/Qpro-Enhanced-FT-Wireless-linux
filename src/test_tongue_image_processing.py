import unittest

import cv2
import numpy as np

from tongue_image_processing import (
    MODES,
    preprocess_stereo_images,
    resolve_input_preprocessing,
)


class TongueImageProcessingTests(unittest.TestCase):
    def test_legacy_checkpoint_preserves_every_input_pixel(self):
        images = np.arange(256, dtype=np.uint8).reshape(2, 8, 16)
        mode = resolve_input_preprocessing({"imageSize": 224})
        self.assertEqual(mode, "raw-v1")
        np.testing.assert_array_equal(preprocess_stereo_images(images, mode), images)

    def test_checkpoint_modes_are_explicit_and_versioned(self):
        for mode in MODES:
            with self.subTest(mode=mode):
                self.assertEqual(resolve_input_preprocessing({"inputPreprocessing": mode}), mode)
        for mode in ("auto", "clahe-v2", None, 1, ["raw-v1"]):
            with self.subTest(mode=mode):
                with self.assertRaises(ValueError):
                    resolve_input_preprocessing({"inputPreprocessing": mode})
        with self.assertRaises(TypeError):
            resolve_input_preprocessing(None)

    def test_preprocessing_does_not_mutate_input_even_for_noncontiguous_views(self):
        images = np.random.default_rng(7).integers(0, 256, (2, 64, 96), dtype=np.uint8)
        images = images[:, :, ::2]
        original = images.copy()
        for mode in MODES:
            with self.subTest(mode=mode):
                preprocess_stereo_images(images, mode)
                np.testing.assert_array_equal(images, original)

    def test_conservative_blend_matches_each_camera_independently(self):
        images = np.random.default_rng(11).integers(1, 255, (2, 64, 96), dtype=np.uint8)
        actual = preprocess_stereo_images(images, "clahe-v1")
        clahe = cv2.createCLAHE(clipLimit=1.5, tileGridSize=(8, 8))
        expected = np.stack([
            cv2.addWeighted(view, 0.5, clahe.apply(view), 0.5, 0.0)
            for view in images
        ])
        np.testing.assert_array_equal(actual, expected)
        reversed_cameras = preprocess_stereo_images(images[::-1], "clahe-v1")
        np.testing.assert_array_equal(reversed_cameras, actual[::-1])

    def test_preserves_geometry_uint8_bounds_and_clipped_pixel_locations(self):
        images = np.random.default_rng(19).integers(0, 256, (2, 31, 47), dtype=np.uint8)
        images[0, 1::3, 2::4] = 0
        images[1, 2::4, 1::3] = 255
        processed = preprocess_stereo_images(images, "clahe-v1")
        self.assertEqual(processed.shape, images.shape)
        self.assertEqual(processed.dtype, np.uint8)
        self.assertGreaterEqual(int(processed.min()), 0)
        self.assertLessEqual(int(processed.max()), 255)
        clipped = (images == 0) | (images == 255)
        np.testing.assert_array_equal(processed[clipped], images[clipped])

    def test_shadow_texture_contrast_increases_without_reconstructing_detail(self):
        # A recorded dark stripe pattern has real, small intensity differences.
        # Check their separability, not whether hidden anatomy can be recovered.
        view = np.tile(np.asarray([12, 18], dtype=np.uint8), (128, 64))
        images = np.stack([view, view])
        processed = preprocess_stereo_images(images, "clahe-v1")
        before_difference = float(images[:, :, 1::2].mean() - images[:, :, ::2].mean())
        after_difference = float(processed[:, :, 1::2].mean() - processed[:, :, ::2].mean())
        self.assertGreater(after_difference, before_difference)
        self.assertLessEqual(after_difference, before_difference * 2.0)

    def test_invalid_mode_array_dtype_and_shape_fail_clearly(self):
        valid = np.zeros((2, 16, 16), dtype=np.uint8)
        for mode in ("unknown", None, 1):
            with self.subTest(mode=mode):
                with self.assertRaises(ValueError):
                    preprocess_stereo_images(valid, mode)
        with self.assertRaises(TypeError):
            preprocess_stereo_images([[[0]], [[0]]], "raw-v1")
        with self.assertRaises(TypeError):
            preprocess_stereo_images(valid.astype(np.float32), "clahe-v1")
        for shape in ((16, 16), (1, 16, 16), (3, 16, 16), (2, 0, 16), (2, 16, 0)):
            with self.subTest(shape=shape):
                with self.assertRaises(ValueError):
                    preprocess_stereo_images(np.zeros(shape, dtype=np.uint8), "raw-v1")


if __name__ == "__main__":
    unittest.main()
