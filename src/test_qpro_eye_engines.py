"""Engine identity and detector ownership regressions, without headset access."""

import unittest
from unittest import mock

import qpro_eye_engines as engines
from eye_detector_guard import DetectorCallGuard


def event(kind, thread=10, timestamp=1.0, *, tag=0, pointer=0x10000, ready=1):
    return (
        f"FaceCam-{thread} [001] .... {timestamp:.6f}: {kind}: "
        f"tag=0x{tag:x} ready={ready} pointer=0x{pointer:x}"
    )


class EngineIdentityTests(unittest.TestCase):
    def test_catalog_covers_every_approved_firmware_with_unique_identities(self):
        from prepare_eye_model import APPROVED_FIRMWARE_BUILDS
        aliases = [build for item in engines.ENGINE_PROFILES for build in item.builds]
        self.assertEqual(set(aliases), set(APPROVED_FIRMWARE_BUILDS))
        self.assertEqual(len(aliases), len(set(aliases)))
        identities = [(item.size, item.sha256) for item in engines.ENGINE_PROFILES]
        self.assertEqual(len(identities), len(set(identities)))
        for item in engines.ENGINE_PROFILES:
            with self.subTest(profile=item.profile):
                self.assertIn(item.profile, item.builds)
                self.assertTrue(0 <= item.offset < item.size)
                if item.entry_offset is not None:
                    self.assertTrue(0 <= item.entry_offset < item.offset)
                    self.assertEqual(item.arguments, engines.EYEDATA_ARGUMENTS)
                else:
                    self.assertEqual(item.arguments, engines.STACK_ARGUMENTS)

    def test_every_catalog_entry_selects_exactly_by_size_and_hash(self):
        for profile in engines.ENGINE_PROFILES:
            with self.subTest(profile=profile.profile):
                self.assertIs(engines.select_engine_profile(profile.size, profile.sha256), profile)
                with self.assertRaises(engines.EngineCompatibilityError):
                    engines.select_engine_profile(profile.size + 1, profile.sha256)
                with self.assertRaises(engines.EngineCompatibilityError):
                    engines.select_engine_profile(profile.size, "0" * 64)

    def test_duplicate_identity_is_refused(self):
        profile = engines.ENGINE_PROFILES[0]
        with mock.patch.object(engines, "ENGINE_PROFILES", (profile, profile)):
            with self.assertRaisesRegex(engines.EngineCompatibilityError, "ambiguous"):
                engines.select_engine_profile(profile.size, profile.sha256)

    def test_readiness_fetch_matches_the_detectors_byte_field(self):
        self.assertIn("ready=+0x35c(%x1):u8", engines.ENTRY_ARGUMENTS)
        self.assertIn("ready=+0x35c(%x19):u8", engines.EYEDATA_ARGUMENTS)
        self.assertNotIn("ready=+0x35c(%x1):u32", engines.ENTRY_ARGUMENTS)
        self.assertNotIn("ready=+0x35c(%x19):u32", engines.EYEDATA_ARGUMENTS)

    def test_no_size_only_or_malformed_hash_fallback(self):
        for digest in ("", "a" * 63, "a" * 65, "A" * 64, "not-a-hash"):
            with self.subTest(digest=digest), self.assertRaises(engines.EngineCompatibilityError):
                engines.select_engine_profile(engines.ENGINE_PROFILES[0].size, digest)


class DetectorOwnershipTests(unittest.TestCase):
    def setUp(self):
        self.guard = DetectorCallGuard()

    def test_valid_entry_accepts_one_matching_output(self):
        self.assertIsNone(self.guard.observe(event("detector_entry")))
        output = event("detector_output", timestamp=1.001)
        self.assertTrue(self.guard.observe(output))
        self.assertFalse(self.guard.observe(output))

    def test_no_output_entry_does_not_accept_stale_registers(self):
        self.guard.observe(event("detector_entry", ready=0, pointer=0x20000))
        self.assertFalse(self.guard.observe(event("detector_output", timestamp=1.001)))

    def test_valid_entry_is_replaced_by_invalid_entry(self):
        self.guard.observe(event("detector_entry"))
        self.guard.observe(event("detector_entry", timestamp=1.001, ready=0))
        self.assertFalse(self.guard.observe(event("detector_output", timestamp=1.002)))

    def test_interleaved_threads_keep_their_own_eyes(self):
        self.guard.observe(event("detector_entry", thread=10, tag=0, pointer=0x10000))
        self.guard.observe(event("detector_entry", thread=11, tag=1, pointer=0x20000))
        self.assertTrue(self.guard.observe(event("detector_output", thread=11, timestamp=1.001, tag=1, pointer=0x20000)))
        self.assertTrue(self.guard.observe(event("detector_output", thread=10, timestamp=1.002)))

    def test_pointer_tag_readiness_and_thread_mismatches_are_rejected(self):
        for changes in ({"pointer": 0x20000}, {"tag": 1}, {"ready": 0}, {"thread": 11}):
            with self.subTest(changes=changes):
                guard = DetectorCallGuard()
                guard.observe(event("detector_entry"))
                self.assertFalse(guard.observe(event("detector_output", timestamp=1.001, **changes)))

    def test_missing_entry_stale_entry_and_reverse_timestamps_are_rejected(self):
        self.assertFalse(self.guard.observe(event("detector_output")))
        for timestamp in (1.101, 0.999):
            self.guard.observe(event("detector_entry"))
            self.assertFalse(self.guard.observe(event("detector_output", timestamp=timestamp)))

    def test_nested_calls_cannot_consume_the_outer_eye(self):
        self.guard.observe(event("detector_entry"))
        self.guard.observe(event("detector_entry", timestamp=1.001, pointer=0x20000, tag=1))
        self.assertFalse(self.guard.observe(event("detector_output", timestamp=1.002, pointer=0x20000, tag=1)))
        self.assertFalse(self.guard.observe(event("detector_output", timestamp=1.003)))

    def test_lost_trace_events_clear_pending_calls(self):
        self.guard.observe(event("detector_entry"))
        self.guard.observe("CPU:0 [LOST 2 EVENTS]")
        self.assertFalse(self.guard.observe(event("detector_output", timestamp=1.001)))

    def test_malformed_or_duplicated_fields_are_rejected(self):
        for suffix in ("", " tag=0x0", " ready=1", " pointer=0x10000"):
            with self.subTest(suffix=suffix):
                guard = DetectorCallGuard()
                guard.observe(event("detector_entry"))
                line = event("detector_output", timestamp=1.001) + suffix
                if suffix:
                    self.assertFalse(guard.observe(line))
                else:
                    self.assertTrue(guard.observe(line))
        self.guard.observe(event("detector_entry").replace(" pointer=0x10000", ""))
        self.assertFalse(self.guard.observe(event("detector_output", timestamp=1.001)))


if __name__ == "__main__":
    unittest.main()
