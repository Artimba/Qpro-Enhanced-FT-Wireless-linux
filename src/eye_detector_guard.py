"""Check detector-call ownership before accepting an epilogue trace sample.

Some engines also reach the epilogue when no eye output was computed. The
entry event identifies the current EyeData; an output must belong to that same
call and pointer. This validates samples without changing their gaze angles.
"""

from __future__ import annotations

from dataclasses import dataclass
import re


HEADER = re.compile(
    r"^.*-(?P<thread>\d+)\s+\[\d+\].*?"
    r"(?P<time>\d+\.\d+):\s+(?:qpro_raw_eye:)?"
    r"(?P<event>detector_entry|detector_output):\s*(?P<fields>.*)$"
)
FIELD = re.compile(r"\b(tag|ready|pointer)=(0x[0-9a-fA-F]+|\d+)\b")


@dataclass(frozen=True)
class DetectorCall:
    time: float
    pointer: int
    tag: int
    eligible: bool


class DetectorCallGuard:
    """Consume each valid entry once; reject stale or ambiguous trace events."""

    def __init__(self, *, max_call_seconds: float = 0.1, max_threads: int = 64):
        self.max_call_seconds = max_call_seconds
        self.max_threads = max_threads
        self._calls: dict[int, list[DetectorCall]] = {}

    def observe(self, line: str) -> bool | None:
        """Return True for accepted output, False for a rejected event, else None."""
        if "LOST" in line and "EVENT" in line:
            self._calls.clear()
            return None
        match = HEADER.match(line)
        if match is None:
            if "detector_entry:" in line or "detector_output:" in line:
                self._calls.clear()
                return False
            return None
        thread = int(match["thread"])
        timestamp = float(match["time"])
        fields = FIELD.findall(match["fields"])
        values = {name: int(value, 16 if value.startswith("0x") else 10) for name, value in fields}
        complete = len(fields) == len(values) == 3
        pointer = values.get("pointer", 0)
        tag = values.get("tag", 255) & 0xFF
        eligible = complete and pointer != 0 and tag in (0, 1) and values.get("ready") == 1
        if match["event"] == "detector_entry":
            if thread not in self._calls and len(self._calls) >= self.max_threads:
                oldest = min(self._calls, key=lambda key: self._calls[key][-1].time)
                del self._calls[oldest]
            stack = self._calls.setdefault(thread, [])
            if stack and timestamp - stack[-1].time > self.max_call_seconds:
                stack.clear()
            # Reentrant calls are unexpected here. Discard every involved
            # output rather than associate a nested return with the outer eye.
            if stack:
                stack[:] = [DetectorCall(item.time, item.pointer, item.tag, False) for item in stack]
                eligible = False
            if len(stack) >= 8:
                stack.clear()
                eligible = False
            stack.append(DetectorCall(timestamp, pointer, tag, eligible))
            return None if eligible else False
        stack = self._calls.get(thread)
        if not stack:
            return False
        call = stack.pop()
        if not stack:
            del self._calls[thread]
        return (
            eligible and call.eligible
            and 0 <= timestamp - call.time <= self.max_call_seconds
            and pointer == call.pointer and tag == call.tag
        )
