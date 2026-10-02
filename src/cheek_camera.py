"""Opt-in camera cheek output; expiration restores the native cheek source."""

import math
import socket
import struct
import threading
import time


CHEEK_PACKET = struct.Struct("<4sBBHff")


def encode_cheek_packet(left: float, right: float, enabled: bool) -> bytes:
    if not all(math.isfinite(value) and 0 <= value <= 1 for value in (left, right)):
        raise ValueError("Camera cheek strengths must be finite values in 0..1")
    return CHEEK_PACKET.pack(b"QPCO", 1, int(bool(enabled)), 0, left, right)


class CameraCheekBroadcaster:
    def __init__(self, port: int = 27279, enabled: bool = False):
        self.enabled = enabled
        self._address = ("127.0.0.1", port)
        self._socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._lock = threading.Lock()
        self._closed = False
        self._last_sent = 0.0
        self._last_enabled = None

    def send_prediction(self, prediction, target_names):
        with self._lock:
            if self._closed:
                return
            names = ("cheekPuffLeft", "cheekPuffRight")
            if any(name not in target_names for name in names):
                raise ValueError("The selected camera model has no trained cheek outputs")
            values = [float(prediction.values[target_names.index(name)]) for name in names]
            # An old inference result cannot establish a new camera override.
            age = prediction.pipeline_ms
            enabled = bool(self.enabled and math.isfinite(age) and 0 <= age <= 500)
            now = time.perf_counter()
            if enabled != self._last_enabled or now - self._last_sent >= 1 / 24:
                self._socket.sendto(encode_cheek_packet(*values, enabled), self._address)
                self._last_sent = now
                self._last_enabled = enabled

    def close(self):
        with self._lock:
            if self._closed:
                return
            self._closed = True
            try:
                self._socket.sendto(encode_cheek_packet(0, 0, False), self._address)
            finally:
                self._socket.close()
