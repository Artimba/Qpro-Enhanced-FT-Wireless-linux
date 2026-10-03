import math
import struct
import unittest
from pathlib import Path
from unittest import mock
import subprocess
import native_raw_eye_probe as native_reader

from native_eye_probe import NativeEyeSample, parse_memory_client1
from native_eye_pupil_probe import KernelToPcMonotonicClock
from native_raw_eye_probe import (
    DetectorOutputParser,
    PreMergeParser,
    RawEyeSample,
    TracePairParser,
    float_from_trace_hex,
)


class NativeEyeProbeTests(unittest.TestCase):
    def test_trace_cleanup_uses_eof_instead_of_remote_pkill(self) -> None:
        source = Path("native_raw_eye_probe.py").read_text(encoding="utf-8")
        self.assertIn("free_buffer", source)
        self.assertNotIn("pkill -f '^cat", source)

    def test_trace_control_uses_one_persistent_magisk_shell(self) -> None:
        source = Path("native_raw_eye_probe.py").read_text(encoding="utf-8")
        self.assertIn("class PersistentAdbRootShell", source)
        self.assertIn("__QPRO_ROOT_DONE_", source)
        self.assertNotIn('[self.adb, "shell", "su", "-c", command]', source)

    def test_detector_reports_true_ray_separation(self) -> None:
        angle = math.radians(10.0)
        sample = RawEyeSample(
            pc_monotonic_ns=1,
            kernel_time_s=1.0,
            left_valid=True,
            right_valid=True,
            left_vector=(0.0, 0.0, 1.0),
            right_vector=(math.sin(angle), 0.0, math.cos(angle)),
        )
        self.assertAlmostEqual(sample.disparity, 10.0, places=5)
        self.assertAlmostEqual(sample.pitch_difference, 0.0, places=5)
        self.assertAlmostEqual(sample.angular_separation, 10.0, places=5)

    def test_kernel_clock_translation_removes_variable_usb_batch_delay(self) -> None:
        clock = KernelToPcMonotonicClock(window=16)
        clock_offset = 50_000_000_000
        delays = [80_000_000, 45_000_000, 0, 70_000_000]
        translated = []
        for index, delay in enumerate(delays):
            kernel_ns = 10_000_000_000 + index * 13_000_000
            arrival = kernel_ns + clock_offset + delay
            translated.append(clock.translate(kernel_ns / 1_000_000_000, arrival))
        self.assertEqual(clock.estimated_offset_ns, clock_offset)
        self.assertEqual(translated[-1], 10_039_000_000 + clock_offset)

    def test_independent_pose_disparity_is_computed_before_combined_point(self) -> None:
        sample = NativeEyeSample(
            pc_monotonic_ns=1,
            mode=0,
            valid=True,
            source_time=1.0,
            arrival_time=1.0,
            processing_end_time=1.0,
            left_position=(-0.032, 0.0, 0.0),
            left_orientation=(0.0, 0.0, 0.0, 1.0),
            right_position=(0.032, 0.0, 0.0),
            right_orientation=(0.0, 0.0871557, 0.0, 0.9961947),
            combined_point=(0.0, 0.0, -1.1),
        )
        self.assertAlmostEqual(sample.left_angles[0], 0.0, places=4)
        self.assertAlmostEqual(sample.right_angles[0], -10.0, places=3)
        self.assertAlmostEqual(sample.disparity, -10.0, places=3)

    def test_memory_client_parser_decodes_both_eye_records(self) -> None:
        payload = bytearray(256)
        payload[0] = 1
        struct.pack_into("<Q", payload, 8, 1_000_000_000)
        struct.pack_into("<Q", payload, 16, 2_000_000_000)
        struct.pack_into("<Q", payload, 24, 3_000_000_000)
        struct.pack_into("<4f", payload, 40, 0.0, 0.0, 0.0, 1.0)
        struct.pack_into("<3f", payload, 56, -0.032, 0.0, 0.0)
        struct.pack_into("<4f", payload, 104, 0.0, 0.1, 0.0, 0.995)
        struct.pack_into("<3f", payload, 120, 0.032, 0.0, 0.0)
        struct.pack_into("<3f", payload, 200, 0.2, -0.1, -1.1)
        sample = parse_memory_client1(
            "kind=client1 mode=3 status=1 bytes=" + payload.hex(), 99
        )
        self.assertIsNotNone(sample)
        assert sample is not None
        self.assertEqual(sample.mode, 3)
        self.assertEqual(sample.pc_monotonic_ns, 99)
        self.assertAlmostEqual(sample.source_time, 1.0)
        self.assertAlmostEqual(sample.left_position[0], -0.032, places=5)
        self.assertAlmostEqual(sample.right_orientation[1], 0.1, places=5)
        self.assertAlmostEqual(sample.combined_point[2], -1.1, places=5)

    def test_raw_trace_parser_pairs_eyes_and_deduplicates_publishers(self) -> None:
        parser = TracePairParser()
        left = (
            "FaceCam-10 [001] .... 12.345000: qpro_left: (0x1) "
            "x=0x3f000000 y=0x00000000 z=0x3f800000 valid=1"
        )
        right = (
            "FaceCam-10 [001] .... 12.345010: qpro_right: (0x2) "
            "x=0xbf000000 y=0x00000000 z=0x3f800000 valid=1"
        )
        self.assertIsNone(parser.parse(left, 100))
        sample = parser.parse(right, 101)
        self.assertIsNotNone(sample)
        assert sample is not None
        self.assertAlmostEqual(sample.left_vector[0], 0.5)
        self.assertAlmostEqual(sample.right_vector[0], -0.5)
        self.assertTrue(sample.valid)
        self.assertLess(sample.disparity, 0.0)
        self.assertIsNone(parser.parse(left, 102))
        self.assertIsNone(parser.parse(right, 103))

    def test_trace_hex_conversion_preserves_signed_float_bits(self) -> None:
        self.assertAlmostEqual(float_from_trace_hex("bec89662"), -0.3917723, places=5)

    def test_premerge_parser_maps_x0_right_and_x1_left(self) -> None:
        parser = PreMergeParser()
        line = (
            "FaceCam-10 [001] .... 12.500000: qpro_inputs: (0x1) "
            "ax=0x3f000000 ay=0x00000000 az=0x3f800000 av=1 "
            "bx=0xbf000000 by=0x00000000 bz=0x3f800000 bv=1"
        )
        sample = parser.parse(line, 99)
        self.assertIsNotNone(sample)
        assert sample is not None
        self.assertAlmostEqual(sample.right_vector[0], 0.5)
        self.assertAlmostEqual(sample.left_vector[0], -0.5)
        self.assertTrue(sample.valid)
        self.assertIsNone(parser.parse(line, 100))

    def test_detector_parser_pairs_eye_tags_zero_and_one(self) -> None:
        parser = DetectorOutputParser()
        left = (
            "FaceCam-10 [001] .... 12.500000: detector_output: (0x1) "
            "x=0x3f000000 y=0x00000000 z=0x3f800000 tag=0x61630000"
        )
        right = (
            "FaceCam-10 [001] .... 12.500100: detector_output: (0x1) "
            "x=0xbf000000 y=0x00000000 z=0x3f800000 tag=0x61630001"
        )
        self.assertIsNone(parser.parse(left, 100))
        sample = parser.parse(right, 101)
        self.assertIsNotNone(sample)
        assert sample is not None
        self.assertAlmostEqual(sample.left_vector[0], 0.5)
        self.assertAlmostEqual(sample.right_vector[0], -0.5)
        self.assertTrue(sample.valid)

    def test_guarded_parser_preserves_valid_eye_values(self) -> None:
        parser = DetectorOutputParser(guarded=True)
        lines = [
            "FaceCam-10 [001] .... 12.500000: detector_entry: tag=0x61630000 ready=1 pointer=0x10000",
            "FaceCam-10 [001] .... 12.500050: detector_output: x=0x3f000000 y=0x0 z=0x3f800000 tag=0x61630000 pointer=0x10000 ready=1",
            "FaceCam-10 [001] .... 12.500100: detector_entry: tag=0x61630001 ready=1 pointer=0x20000",
            "FaceCam-10 [001] .... 12.500150: detector_output: x=0xbf000000 y=0x0 z=0x3f800000 tag=0x61630001 pointer=0x20000 ready=1",
        ]
        results = [parser.parse(line, 100 + index) for index, line in enumerate(lines)]
        self.assertEqual(results[:3], [None, None, None])
        sample = results[3]
        self.assertIsNotNone(sample)
        self.assertEqual(sample.left_vector, (0.5, 0.0, 1.0))
        self.assertEqual(sample.right_vector, (-0.5, 0.0, 1.0))

    def test_invalid_entry_cannot_finish_a_pending_eye_pair(self) -> None:
        parser = DetectorOutputParser(guarded=True)
        lines = [
            "FaceCam-10 [001] .... 12.500000: detector_entry: tag=0 ready=1 pointer=0x10000",
            "FaceCam-10 [001] .... 12.500010: detector_output: x=0x0 y=0x0 z=0x3f800000 tag=0 pointer=0x10000 ready=1",
            "FaceCam-10 [001] .... 12.500020: detector_entry: tag=1 ready=0 pointer=0x20000",
            "FaceCam-10 [001] .... 12.500030: detector_output: x=0x0 y=0x0 z=0x3f800000 tag=1 pointer=0x10000 ready=1",
        ]
        self.assertTrue(all(parser.parse(line, index) is None for index, line in enumerate(lines)))

    def test_grouped_detector_events_preserve_valid_pair(self) -> None:
        parser = DetectorOutputParser(guarded=True)
        sample = None
        for eye in (0, 1):
            base = 12.5 + eye * 0.001
            pointer = 0x10000 + eye * 0x10000
            entry = f"FaceCam-10 [001] .... {base:.6f}: qpro_raw_eye:detector_entry: tag={eye} ready=1 pointer=0x{pointer:x}"
            output = f"FaceCam-10 [001] .... {base + 0.0001:.6f}: qpro_raw_eye:detector_output: x=0x0 y=0x0 z=0x3f800000 tag=0x{eye:x} pointer=0x{pointer:x} ready=1"
            self.assertIsNone(parser.parse(entry, 1))
            sample = parser.parse(output, 2)
        self.assertIsNotNone(sample)
        self.assertEqual(sample.left_vector, (0.0, 0.0, 1.0))
        self.assertEqual(sample.right_vector, (0.0, 0.0, 1.0))

    def test_faulted_output_cannot_leave_a_stale_eye_pending(self) -> None:
        parser = DetectorOutputParser(guarded=True)
        for index, (eye, fault) in enumerate(((0, False), (0, True), (1, False))):
            base = 12.5 + index * 0.001
            pointer = 0x10000 + eye * 0x10000
            entry = f"FaceCam-10 [001] .... {base:.6f}: detector_entry: tag={eye} ready=1 pointer=0x{pointer:x}"
            xyz = "x=(fault) y=(fault) z=(fault)" if fault else "x=0x0 y=0x0 z=0x3f800000"
            output = f"FaceCam-10 [001] .... {base + 0.0001:.6f}: detector_output: {xyz} tag=0x{eye:x} pointer=0x{pointer:x} ready=1"
            self.assertIsNone(parser.parse(entry, index))
            self.assertIsNone(parser.parse(output, index))

    def test_lost_events_clear_pending_legacy_eye(self) -> None:
        parser = DetectorOutputParser()
        left = "FaceCam-10 [001] .... 12.500000: detector_output: x=0x0 y=0x0 z=0x3f800000 tag=0x0"
        right = left.replace("12.500000", "12.501000").replace("tag=0x0", "tag=0x1")
        self.assertIsNone(parser.parse(left, 1))
        self.assertIsNone(parser.parse("CPU:0 [LOST 2 EVENTS]", 2))
        self.assertIsNone(parser.parse(right, 3))

    def test_unknown_engine_closes_root_shell_before_installing_probes(self) -> None:
        shell = mock.Mock()
        shell.run.side_effect = [
            subprocess.CompletedProcess([], 0, "47418280", ""),
            subprocess.CompletedProcess([], 0, "0" * 64 + "  /odm/lib64/libtrackingengines.so", ""),
        ]
        reader = native_reader.RawTraceEyeReader("adb.exe")
        with mock.patch.object(native_reader.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "device", "")), mock.patch.object(native_reader, "PersistentAdbRootShell", return_value=shell), mock.patch.object(reader, "_write_event") as write, mock.patch.object(reader, "_cleanup") as cleanup:
            with self.assertRaises(ValueError):
                reader.start()
        shell.close.assert_called_once()
        write.assert_not_called()
        cleanup.assert_not_called()
        self.assertIsNone(reader._root_shell)

    def test_root_shell_is_closed_even_if_trace_cleanup_fails(self) -> None:
        reader = native_reader.RawTraceEyeReader("adb.exe")
        shell = mock.Mock()
        reader._root_shell = shell
        with mock.patch.object(reader, "_cleanup", side_effect=RuntimeError("trace workspace busy")):
            with self.assertRaisesRegex(RuntimeError, "trace workspace busy"):
                reader.close()
        shell.close.assert_called_once()
        self.assertIsNone(reader._root_shell)

    def test_failed_root_shell_bootstrap_closes_its_process(self) -> None:
        for failure in (
            RuntimeError("Timed out waiting for persistent headset root shell"),
            subprocess.CompletedProcess([], 0, "uid=2000(shell)", ""),
        ):
            with self.subTest(failure=failure):
                if isinstance(failure, Exception):
                    run_arguments = {"side_effect": failure}
                else:
                    run_arguments = {"return_value": failure}
                with mock.patch.object(native_reader.subprocess, "Popen"), mock.patch.object(native_reader.threading, "Thread"), mock.patch.object(native_reader.PersistentAdbRootShell, "run", **run_arguments), mock.patch.object(native_reader.PersistentAdbRootShell, "close") as close:
                    with self.assertRaises(RuntimeError):
                        native_reader.PersistentAdbRootShell("adb.exe")
                close.assert_called_once()


if __name__ == "__main__":
    unittest.main()
