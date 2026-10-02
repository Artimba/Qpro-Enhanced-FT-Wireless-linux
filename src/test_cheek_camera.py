import math
import threading
import unittest
from types import SimpleNamespace
from unittest import mock

from cheek_camera import CHEEK_PACKET, CameraCheekBroadcaster, encode_cheek_packet


class _Socket:
    def __init__(self):
        self.packets = []
        self.closed = 0

    def sendto(self, packet, address):
        if self.closed:
            raise AssertionError("Output was sent after the socket closed")
        self.packets.append((CHEEK_PACKET.unpack(packet), address))

    def close(self):
        self.closed += 1


class CameraCheekOutputTests(unittest.TestCase):
    def setUp(self):
        self.socket = _Socket()
        patch = mock.patch("cheek_camera.socket.socket", return_value=self.socket)
        patch.start()
        self.addCleanup(patch.stop)
        self.broadcaster = CameraCheekBroadcaster(enabled=True)
        self.addCleanup(self.broadcaster.close)
        self.names = ["visibility", "cheekPuffRight", "extension", "cheekPuffLeft"]

    def prediction(self, age=10):
        return SimpleNamespace(values=[0.9, 0.25, 0.7, 0.75], pipeline_ms=age)

    def test_wire_contract_contains_only_two_named_cheek_strengths(self):
        packet = encode_cheek_packet(0.75, 0.25, True)
        self.assertEqual(len(packet), 16)
        self.assertEqual(CHEEK_PACKET.unpack(packet), (b"QPCO", 1, 1, 0, 0.75, 0.25))
        with mock.patch("cheek_camera.time.perf_counter", return_value=1.0):
            self.broadcaster.send_prediction(self.prediction(), self.names)
        fields, address = self.socket.packets[-1]
        self.assertEqual(fields, (b"QPCO", 1, 1, 0, 0.75, 0.25))
        self.assertEqual(address, ("127.0.0.1", 27279))

    def test_invalid_strengths_are_rejected(self):
        for bad in (math.nan, math.inf, -0.1, 1.1):
            for values in ((bad, 0.5), (0.5, bad)):
                with self.subTest(values=values), self.assertRaises(ValueError):
                    encode_cheek_packet(*values, True)

    def test_a_tongue_only_checkpoint_cannot_send_cheek_output(self):
        with self.assertRaisesRegex(ValueError, "no trained cheek outputs"):
            self.broadcaster.send_prediction(self.prediction(), ["visibility", "extension"])
        self.assertEqual(self.socket.packets, [])

    def test_stale_or_invalid_age_cannot_renew_camera_override(self):
        for age, enabled in ((500, True), (500.01, False), (-1, False),
                             (math.nan, False), (math.inf, False)):
            with self.subTest(age=age):
                self.broadcaster._last_sent = 0
                with mock.patch("cheek_camera.time.perf_counter", return_value=1.0):
                    self.broadcaster.send_prediction(self.prediction(age), self.names)
                self.assertEqual(self.socket.packets[-1][0][2], int(enabled))

    def test_disabled_packet_is_immediate_even_inside_frame_rate_limit(self):
        with mock.patch("cheek_camera.time.perf_counter", side_effect=[1.0, 1.001]):
            self.broadcaster.send_prediction(self.prediction(), self.names)
            self.broadcaster.send_prediction(self.prediction(501), self.names)
        self.assertEqual([packet[0][2] for packet in self.socket.packets], [1, 0])

    def test_unchanged_enabled_state_obeys_the_24_hz_limit(self):
        with mock.patch("cheek_camera.time.perf_counter", side_effect=[1.0, 1.001, 1.05]):
            for _ in range(3):
                self.broadcaster.send_prediction(self.prediction(), self.names)
        self.assertEqual(len(self.socket.packets), 2)

    def test_close_disables_once_and_later_predictions_are_ignored(self):
        with mock.patch("cheek_camera.time.perf_counter", return_value=1.0):
            self.broadcaster.send_prediction(self.prediction(), self.names)
        self.broadcaster.close()
        self.broadcaster.close()
        self.broadcaster.send_prediction(None, [])
        self.assertEqual(self.socket.closed, 1)
        self.assertEqual(len(self.socket.packets), 2)
        self.assertEqual(self.socket.packets[-1][0], (b"QPCO", 1, 0, 0, 0, 0))

    def test_concurrent_close_and_send_have_no_packet_after_final_disable(self):
        barrier = threading.Barrier(2)

        def send():
            barrier.wait()
            self.broadcaster.send_prediction(self.prediction(), self.names)

        thread = threading.Thread(target=send)
        thread.start()
        barrier.wait()
        self.broadcaster.close()
        thread.join(timeout=1)
        self.assertFalse(thread.is_alive())
        self.assertEqual(self.socket.packets[-1][0][2], 0)
        self.assertEqual(self.socket.closed, 1)


if __name__ == "__main__":
    unittest.main()
