import unittest

import numpy as np

from tongue_visibility_calibration import choose_visibility_gate


class TongueVisibilityCalibrationTests(unittest.TestCase):
    def test_perfect_easy_validation_does_not_push_threshold_to_085(self):
        camera = np.asarray([0.05, 0.10, 0.90, 0.95])
        native = camera.copy()
        truth = np.asarray([0.0, 0.0, 1.0, 1.0])
        _weight, threshold, result = choose_visibility_gate(camera, native, truth)
        self.assertAlmostEqual(threshold, 0.5)
        self.assertEqual(result["f1"], 1.0)

    def test_noisy_native_reference_can_be_ignored(self):
        camera = np.asarray([0.05, 0.12, 0.83, 0.91])
        native = np.asarray([0.90, 0.81, 0.18, 0.10])
        truth = np.asarray([0.0, 0.0, 1.0, 1.0])
        weight, threshold, result = choose_visibility_gate(camera, native, truth)
        self.assertEqual(weight, 1.0)
        self.assertAlmostEqual(threshold, 0.5)
        self.assertEqual(result["f1"], 1.0)


if __name__ == "__main__":
    unittest.main()
