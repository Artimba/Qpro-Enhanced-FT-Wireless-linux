# Tests and diagnostics

Run these commands from `src/` in the source checkout or GitHub source export:

| Check | Command | Purpose |
| --- | --- | --- |
| Python unit tests | `python -m unittest discover -p "test_*.py"` | Test capture, calibration, training, and runtime code with synthetic inputs. |
| VRCFaceTracking mapping tests | `dotnet run --project tests/steam-osc/SteamOscSourceTests.csproj -c Release` | Test Steam Link OSC parsing and face expression mapping without a headset. Requires the .NET 10 SDK. |
| Live override lifecycle | `dotnet run --project tests/live-overlays/LiveOverlayTests.csproj -c Release -p:VrcftInstallDir="<Steam VRCFaceTracking folder>"` | Exercise the production module with private test maps, loopback ports and the installed VRCFT API. Requires Windows and the .NET 10 SDK; does not install a module. |
| Gaze recovery | `powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File test_gaze_recovery.ps1` | Exercise the production recovery helpers against a fake headset, including interrupted sessions, ownership checks and reboot recovery. |
| Release ZIP integrity | `python tests/check-release-zip.py <release.zip>` | Read the ZIP and verify its file list, hashes, and local Python imports. |

The runnable release ZIP contains the app and runtime, not these source tests. A passing synthetic test does not establish that a particular headset firmware, streaming app, or avatar works live.
