# Experimental Touch Pro thumb-rest input

This independently authored prototype adds a two-dimensional thumb-rest input to
the existing Quest Pro controllers in SteamVR. It preserves their native driver,
poses, haptics, button paths and `oculus_touch` controller type. It does not add
hand tracking or trigger-slide input.

**Live validation is pending.** The ARM64 reader and Windows driver have been
compiled, and packet/filter/lifetime tests run without a headset. That does not
prove sensor orientation, Android execution, profile adoption or application
bindings on a real headset. The initial adoption rule is limited to Virtual
Desktop controllers reporting a `quest_pro` render model and the exact native
`{oculus}/input/touch_profile.json` profile. Steam Link is unverified.

## Requirements and boundaries

- Rooted Quest Pro, Touch Pro controllers and SteamVR on Windows x64.
- Explicit experimental reader profile `legacy-51503870024400340`. The reader
  checks the exact incremental firmware build `51503870024400340`; other builds
  exit before mapping memory. This identifies a proposed layout, not validated
  firmware support.
- Both named 4 KiB controller shared pages must be present in `trackingservice`.
  They are opened read-only through `/proc/<pid>/map_files`. The reader neither
  modifies these pages nor restarts or injects into a headset service.
- No external server or global SteamVR settings changes are used. The PC relay
  owns the ADB forwarding, foreground reader and this add-on's settings file.

The server host interception uses the pinned public OpenVR driver ABI. It is
still an experimental hook: other add-ons that alter the same host method could
conflict. Cleanup compare-restores the slot only if it still points to Qpro's
method, and compare-restores controller profiles only if they are still Qpro's.
Install and uninstall with **SteamVR closed**. Hot unloading is unsupported;
restart SteamVR after removing the add-on.

## Reader CLI

```text
qpro-controller-input --profile legacy-51503870024400340 --check
qpro-controller-input --profile legacy-51503870024400340 --port 27063 --rate 60 --stop-file /data/local/tmp/<owned-session>.stop
```

`--check` reads for at most three seconds and needs a fresh, valid pair of sensor
samples. Keep both controllers awake. The continuous mode listens only on headset
loopback TCP, with rates from 40 to 90 Hz. The port is acquired before mapping,
so a duplicate reader cannot attach while that port is owned. No first client
within 15 seconds exits with code 8. A disconnect allows two seconds to reconnect,
then closes the socket and releases both mappings. SIGTERM, SIGINT and the owned
stop file also end the foreground reader; it attempts a bounded neutral packet.

Exit codes: 2 arguments/profile, 3 build mismatch, 4 root required, 5 source
unavailable, 6 invalid/stale samples, 7 port unavailable, 8 initial client timeout.

## Packet and settings contract

The relay forwards complete 64-byte `QPTP` v1 packets from TCP 27063 to Windows
loopback UDP **27064**. All fields are little endian:

| Offset | Field |
| --- | --- |
| 0 | `QPTP` magic |
| 4 / 6 | uint16 version 1 / uint16 length 64 |
| 8 / 16 | uint64 sequence / uint64 headset monotonic nanoseconds |
| 24 / 44 | Left / right: uint32 flags, float x, y, force, size |

Flags: bit 0 valid, bit 1 contact; no other bits. Position is -1 to 1, force is
0 to 1 and contact size is bounded from 0 to 512. Nonfinite values and invalid
flags are rejected. Inputs expire **200 ms after local arrival**, clear outputs
and release any mouse button. Sequence and source time must both increase within
an active lease. Once expired, a new reader/headset clock epoch may be accepted;
an exact duplicate cannot renew it. The loopback relay is a trust boundary, not
an authenticated or cryptographically replay-proof channel.

The add-on reads only its own `resources/settings.json` once per second. Missing
or invalid settings disable it. The shipped file is:

```json
{ "enabled": false, "mode": "trackpad" }
```

The supervisor should atomically write `enabled:true` only for an opted-in run
and write `enabled:false` in its cleanup. The driver can discover already active
controllers after enabling. If SteamVR caches the old profile or bindings, a
controller reconnect or SteamVR restart may be necessary; this awaits live
validation. Disabling clears Qpro outputs, but the custom profile may remain
until driver cleanup or SteamVR restart.

Available `mode` values:

- `trackpad`: smoothed absolute thumb position and pressure click.
- `joystick`: displacement from where the thumb first touched, clamped to the
  normal range. This still uses the added trackpad components; native joystick
  components are unchanged.
- `swipe`: movement velocity with short decay, cleared immediately on stale data.
- `mouse`: right-thumb relative Windows cursor movement and left click. This affects the
  desktop and is off until explicitly selected. Touch release, stale data,
  disable and cleanup release its held button. Added SteamVR components are
  neutral while mouse mode is selected.

Optional settings: `smoothingMs` (0–250), `clickOn` and `clickOff` (0–1, off below
on), `joystickSpan` (0.1–2), `swipeGain` (0.01–5), `glideMs` (0–2000), `mouseScale`
(0–4000), `ignoreRestingThumb` (boolean), `restingSize` (0–512), and
`intentionalForce` (0–1). Defaults and bounds are defined in `settings.hpp`.

## SteamVR application bindings

The added components are `/input/trackpad/{x,y,force,touch,click}`. Open SteamVR's
controller bindings for the target application to bind them to an action. The
profile retains ordinary Touch inputs, skeletons, haptics and poses and does not
replace user bindings or ship a forced VRChat action binding. Actual profile
refresh and ordinary-input parity need the first live test.

## Build and offline checks

Use a portable Zig 0.15.2 compiler and the pinned Valve header in
`third_party/openvr`. No compiler installation or system settings change is
required:

```powershell
& .\controller-input\build-native.ps1 -Zig 'C:\path\to\zig.exe'
```

The script compiles the static ARM64 Linux/musl reader, Windows x64 driver and two
Windows-only offline test programs. Tests cover packet validation, independent
sides, active-lease ordering, reboot epochs, neutral/release behavior, contact
hysteresis, modes, settings parsing, double-buffer pages and abandonment deadlines.
It does not execute the reader, load the driver in SteamVR, or install anything.
The ARM64 artifact's Android syscall compatibility still needs device validation.

OpenVR is distributed under its included license. No QFTPlus code is included;
that project's published controller interface informed the layout investigation.
Qpro's surrounding repository license applies to these independently authored
source files.
