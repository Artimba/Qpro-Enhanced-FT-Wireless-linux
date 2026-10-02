"""HTTP previews should not consume camera-processing time when unused."""

import unittest
from unittest import mock

import numpy as np

from receiver import MjpegHandler, SharedPreview


class ReceiverPreviewTests(unittest.TestCase):
    def setUp(self) -> None:
        self.strip = np.hstack([
            np.full((400, 400), value, dtype=np.uint8)
            for value in (10, 20, 30, 40, 50)
        ])

    @staticmethod
    def encode(_extension, image, _parameters):
        return True, np.asarray([image[0, 0]], dtype=np.uint8)

    def test_no_http_clients_do_not_encode_jpegs(self) -> None:
        shared = SharedPreview()
        with mock.patch("receiver.cv2.imencode") as encode:
            shared.update(self.strip, [0, 1, 2, 3, 4])
        encode.assert_not_called()
        self.assertEqual(shared.available, [0, 1, 2, 3, 4])
        self.assertEqual(shared.jpegs, {})

    def test_selected_client_follows_camera_changes(self) -> None:
        shared = SharedPreview()
        shared.subscribe("selected")
        with mock.patch("receiver.cv2.imencode", side_effect=self.encode) as encode:
            shared.update(self.strip, [0, 1, 2, 3, 4])
            self.assertEqual(shared.jpegs, {"camera3": bytes([40])})
            self.assertTrue(shared.select(1))
            shared.update(self.strip, [0, 1, 2, 3, 4])
        self.assertEqual(encode.call_count, 2)
        self.assertEqual(shared.jpegs, {"camera1": bytes([20])})

    def test_requested_streams_are_encoded_once(self) -> None:
        shared = SharedPreview()
        for requested in ("selected", "camera3", "camera3", "strip"):
            shared.subscribe(requested)
        with mock.patch("receiver.cv2.imencode", side_effect=self.encode) as encode:
            shared.update(self.strip, [0, 1, 2, 3, 4])
        self.assertEqual(encode.call_count, 2)
        self.assertEqual(set(shared.jpegs), {"camera3", "strip"})
        shared.unsubscribe("camera3")
        self.assertEqual(shared.subscribers["camera3"], 1)
        shared.unsubscribe("camera3")
        self.assertNotIn("camera3", shared.subscribers)

    def test_disconnect_releases_subscription(self) -> None:
        shared = SharedPreview(available=[3], jpegs={"camera3": b"jpeg"})
        handler = MjpegHandler.__new__(MjpegHandler)
        handler.shared = shared
        handler.path = "/selected.mjpg"
        handler.send_response = mock.Mock()
        handler.send_header = mock.Mock()
        handler.end_headers = mock.Mock()
        handler.wfile = mock.Mock()
        handler.wfile.write.side_effect = BrokenPipeError()
        handler.do_GET()
        self.assertEqual(shared.subscribers, {})
        with mock.patch("receiver.cv2.imencode") as encode:
            shared.update(self.strip, [0, 1, 2, 3, 4])
        encode.assert_not_called()

    def test_selected_strip_and_missing_camera_need_no_extra_encodes(self) -> None:
        shared = SharedPreview(selected="strip")
        shared.subscribe("selected")
        shared.subscribe("camera4")
        with mock.patch("receiver.cv2.imencode", side_effect=self.encode) as encode:
            shared.update(self.strip[:, :800], [0, 1])
        self.assertEqual(encode.call_count, 1)
        self.assertEqual(set(shared.jpegs), {"strip"})


if __name__ == "__main__":
    unittest.main()
