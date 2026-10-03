"""Offline supervisor/protocol tests. No headset, SteamVR or installation used."""
from pathlib import Path
import importlib.util
import json
import math
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace
import io

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('controller_components', HERE / 'controller-input' / 'manage.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ComponentTests(unittest.TestCase):
    def packet(self, sequence=1, flags=3, x=.25, force=.7):
        return module.PACKET.pack(b'QPTP', 1, 64, sequence, 100, flags, x, -.5, force, 50,
                                  1, 0, 0, 0, 0)

    def test_roundtrip_and_replay(self):
        self.assertEqual(len(self.packet()), 64)
        self.assertEqual(module.validate_packet(self.packet(), 0), 1)
        with self.assertRaises(ValueError):
            module.validate_packet(self.packet(), 1)

    def test_invalid_packet_never_becomes_input(self):
        for packet in (self.packet(flags=4), self.packet(flags=2), self.packet(x=math.nan),
                       self.packet(x=1.01), self.packet(force=-.1), b'x' * 64,
                       self.packet() + b'x', self.packet()[:-1]):
            with self.subTest(packet=packet), self.assertRaises(ValueError):
                module.validate_packet(packet, 0)

    def test_atomic_settings_preserve_other_tuning(self):
        with tempfile.TemporaryDirectory(dir=HERE / 'artifacts') as temporary:
            addon = Path(temporary)
            module.atomic_json(addon / 'qpro-owner.json', {'format': module.OWNER_FORMAT})
            module.atomic_json(addon / 'resources' / 'settings.json', {'smoothingMs': 25, 'enabled': True})
            module.set_input_enabled(addon, False, 'joystick')
            saved = json.loads((addon / 'resources' / 'settings.json').read_text())
            self.assertEqual(saved, {'smoothingMs': 25, 'enabled': False, 'mode': 'joystick'})

    def test_foreign_addon_settings_are_untouched(self):
        with tempfile.TemporaryDirectory(dir=HERE / 'artifacts') as temporary:
            path = Path(temporary)
            with self.assertRaises(RuntimeError):
                module.set_input_enabled(path, True)
            self.assertFalse((path / 'resources').exists())

    def test_usb_selection_requires_exactly_one(self):
        for text in ('List of devices attached\n', 'a device\nb device\n', 'wifi:5555 device\n'):
            with patch.object(module, 'command', return_value=text), self.assertRaises(RuntimeError):
                module.select_target(Path('fake-adb'), None)
        with patch.object(module, 'command', return_value='a device\nb unauthorized\n'):
            self.assertEqual(module.select_target(Path('fake-adb'), None), 'a')

    def test_target_not_interpolated_into_shell(self):
        with patch.object(module, 'command') as execute, self.assertRaises(RuntimeError):
            module.select_target(Path('fake-adb'), 'serial;bad')
        execute.assert_not_called()

    def test_existing_stop_refuses_workers(self):
        with tempfile.TemporaryDirectory(dir=HERE / 'artifacts') as temporary:
            stop = Path(temporary) / 'stop'
            stop.touch()
            args = SimpleNamespace(hands=True, touchpad=False, stop_file=str(stop))
            with patch.object(module.subprocess, 'Popen') as launch, self.assertRaises(RuntimeError):
                module.run(args, Path(temporary), Path('fake-adb'), 'quest')
            launch.assert_not_called()

    def test_stop_write_failure_still_closes_worker_pipe(self):
        with tempfile.TemporaryDirectory(dir=HERE / 'artifacts') as temporary:
            local = Path(temporary)
            hands, _ = module.managed_paths(local)
            (hands / '.venv' / 'Scripts').mkdir(parents=True)
            (hands / '.venv' / 'Scripts' / 'python.exe').touch()
            (hands / 'ready.json').touch()
            pipe = io.BytesIO()
            child = SimpleNamespace(stdin=pipe, pid=123, returncode=1, poll=lambda: 1)
            args = SimpleNamespace(hands=True, touchpad=False, parent_stdin=False,
                                   stop_file=str(local / 'stop'), root=str(HERE))
            with patch.object(module.subprocess, 'Popen', return_value=child), \
                 patch.object(module.Path, 'write_text', side_effect=PermissionError('test unwritable')), \
                 self.assertRaisesRegex(RuntimeError, 'cleanup was not confirmed'):
                module.run(args, local, Path('fake-adb'), 'quest')
            self.assertTrue(pipe.closed)


if __name__ == '__main__':
    unittest.main()
