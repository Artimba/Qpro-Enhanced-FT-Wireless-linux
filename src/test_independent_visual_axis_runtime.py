import math
import queue
import struct
import sys
import unittest
from contextlib import ExitStack
from unittest import mock

import numpy as np

import independent_visual_axis_runtime as gaze_runtime
from independent_visual_axis_runtime import (
    PACKET_FORMAT,
    PACKET_MAGIC,
    PACKET_SIZE,
    PACKET_VERSION,
    calibrated_angles,
    encode_packet,
    vrcft_angles,
)
from native_raw_eye_probe import RawEyeSample


class RuntimeContractTests(unittest.TestCase):
    def test_v2_calibration_maps_detector_tags_to_physical_eyes(self):
        identity = {"coefficients": [[0.0, 0.0], [1.0, 0.0], [0.0, 1.0]]}
        calibration = {
            "left": identity,
            "right": identity,
            "detector_tag_mapping": {
                "physical_left": "trace_tag_1",
                "physical_right": "trace_tag_0",
            },
        }
        sample = RawEyeSample(
            pc_monotonic_ns=1,
            kernel_time_s=1.0,
            left_valid=True,
            right_valid=True,
            left_vector=(0.1, -0.2, 1.0),
            right_vector=(-0.3, 0.4, 1.0),
        )
        left, right = calibrated_angles(calibration, sample)
        self.assertTrue(np.allclose(left, sample.right_angles))
        self.assertTrue(np.allclose(right, sample.left_angles))

    def test_vrcft_conversion_does_not_mirror_calibrated_yaw(self):
        x, y = vrcft_angles(np.asarray([-30.0, 15.0]))
        self.assertTrue(math.isclose(x, math.radians(-30.0)))
        self.assertTrue(math.isclose(y, math.radians(15.0)))

    def test_packet_contract_and_validity_bits(self):
        packet = encode_packet(np.asarray([10.0, -5.0]), np.asarray([-20.0, 7.0]))
        self.assertEqual(len(packet), PACKET_SIZE)
        magic, version, flags, reserved, lx, ly, rx, ry = struct.unpack(
            PACKET_FORMAT, packet
        )
        self.assertEqual(magic, PACKET_MAGIC)
        self.assertEqual(version, PACKET_VERSION)
        self.assertEqual(flags, 3)
        self.assertEqual(reserved, 0)
        # The packet fields are VRCFT channel names, whose physical ownership
        # is reversed by the final VRChat boundary test.
        self.assertTrue(math.isclose(lx, math.radians(-20.0), abs_tol=1e-6))
        self.assertTrue(math.isclose(ly, math.radians(7.0), abs_tol=1e-6))
        self.assertTrue(math.isclose(rx, math.radians(10.0), abs_tol=1e-6))
        self.assertTrue(math.isclose(ry, math.radians(-5.0), abs_tol=1e-6))

    def test_packet_validity_bits_follow_crossed_vrcft_channels(self):
        packet = encode_packet(
            np.asarray([1.0, 2.0]), np.asarray([3.0, 4.0]),
            left_valid=True, right_valid=False,
        )
        _magic, _version, flags, *_values = struct.unpack(PACKET_FORMAT, packet)
        self.assertEqual(flags, 2)

    def test_headless_runtime_handles_valid_and_idle_loops(self):
        self.assertEqual(self._run_with_one_sample(headless=True), 0)

    def test_visual_runtime_handles_valid_and_idle_loops(self):
        self.assertEqual(self._run_with_one_sample(headless=False), 0)

    def _run_with_one_sample(self, *, headless: bool) -> int:
        sample = RawEyeSample(
            pc_monotonic_ns=1,
            kernel_time_s=1.0,
            left_valid=True,
            right_valid=True,
            left_vector=(0.1, 0.0, 1.0),
            right_vector=(-0.1, 0.0, 1.0),
        )
        reader = mock.Mock()
        reader.samples = queue.Queue()
        reader.samples.put(sample)
        reader.errors = queue.Queue()
        calibration = {"quality_gate": {"convergence_pass": False}}
        arguments = ["gaze", "--adb", "test", "--calibration", "test"]
        if headless:
            arguments.extend(["--headless-seconds", "0.03"])

        with ExitStack() as patches:
            patches.enter_context(mock.patch.object(sys, "argv", arguments))
            patches.enter_context(mock.patch.object(gaze_runtime, "load_calibration", return_value=calibration))
            patches.enter_context(mock.patch.object(gaze_runtime, "RawTraceEyeReader", return_value=reader))
            patches.enter_context(mock.patch.object(
                gaze_runtime, "calibrated_angles",
                return_value=(np.array([1.0, 2.0]), np.array([-1.0, 2.0])),
            ))
            patches.enter_context(mock.patch.object(
                gaze_runtime, "IndependentEyeFilter",
                return_value=mock.Mock(update=mock.Mock(return_value=(
                    np.array([1.0, 2.0]), np.array([-1.0, 2.0])
                ))),
            ))
            if not headless:
                for name in ("namedWindow", "resizeWindow", "imshow", "destroyWindow"):
                    patches.enter_context(mock.patch.object(gaze_runtime.cv2, name))
                patches.enter_context(mock.patch.object(gaze_runtime.cv2, "waitKey", side_effect=[-1, ord("q")]))
                patches.enter_context(mock.patch.object(gaze_runtime, "put_text"))
                patches.enter_context(mock.patch.object(gaze_runtime, "gaze_panel"))
            result = gaze_runtime.main()

        reader.start.assert_called_once_with()
        reader.close.assert_called_once_with()
        return result


if __name__ == "__main__":
    unittest.main()
