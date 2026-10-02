# QproFaceTracking Hub

This is the native Windows control app for the Quest Pro tracking package. The Hub is a WinForms application targeting .NET 10. It keeps the existing PowerShell, Python, headset, and VRCFaceTracking components as the tracking back end.

## Pages

- **Live tracking:** connection status; independent gaze, tongue, individual cheek puff and suck controls, eyebrow response, and experimental relative pupil controls; persistent Start tracking and Stop tracking buttons. Cheek puff styles are Calibrated, 1/0 (the default), and Balanced. Turning Individual cheek puff off passes through the streaming app's original values.
- **First-time setup:** USB/Wi-Fi connection selection and wireless enable, connect, and pairing controls; PC runtime, optional AMD ROCm installer, separate Virtual Desktop and Steam Link Qpro VRCFaceTracking module buttons, and gaze preparation. Installing one source module removes the other.
- **Lower-face calibration (Personalize):** Quick refinement and Full dataset add 21 cheek camera cards after their tongue cards. Focused diagonals and facial hair remain tongue-only. Each has a separate dataset queue and publishes a complete new model pair. A separate cheek-only capture can extend an existing tongue model without changing its tongue weights.
- **Model manager:** rename, export, import, and delete paired lower-face or historical tongue models.
- **Activity:** setup, training, tracking, and error output.

## Source layout

- `Program.cs`: entry point, release self-test, and preview capture.
- `HubForm.cs` and `HubLayoutV2.cs`: window state and page layout.
- `HubWorkflows.cs` and `HubData.cs`: setup, tracking, capture, training, datasets, and model operations.
- `HubEnvironment.cs`: ADB connection and local dependency discovery.
- `HubScriptFactory.cs`: launches package scripts with the selected Python and ADB environment.
- `HubSystem.cs`, `HubTheme.cs`, and `HubControls.cs`: status updates, styling, and custom controls.
- `CheekPuffCalibrationDialog.cs`: guided relaxed, left-only, and right-only cheek holds using live values from the selected Qpro module. A profile is saved only when all three poses pass; Virtual Desktop and Steam Link profiles are separate.

Build with `dotnet build qpro-hub/QproFaceTracking.Hub.csproj -c Release` from `src/`. Pass `--root <complete release folder>` when launching a build outside the packaged release. The Hub does not bundle its headset binaries, pretrained models, Python runtime, or Android tools; those remain in the complete release ZIP.

The interface uses per-monitor DPI awareness. Text widths and the sidebar respond to window size; content pages scroll vertically at the minimum size, while Start tracking and Stop tracking remain visible at the bottom. The preview command can render compact windows for layout checks, for example `QproFaceTracking.Hub.exe --root <release folder> --render-preview <output.png> --preview-viewport 700 500 --preview-page setup --preview-scroll-bottom`. Compact previews do not replace testing on a real high-DPI monitor.

Calibrated cheek puff maps relaxed and full strengths to a continuous 0..1 response with brief smoothing. Separate bundled developer baselines were measured on one Quest Pro through Virtual Desktop and Steam Link. These give a starting point until a personal profile is available for the selected source; response varies by wearer. Calibrate cheek puff with the headset streaming and the selected Qpro module tracking in VRCFaceTracking; Qpro camera tracking is not needed.

The pupil option is experimental. Its image and packet tests pass, and relative pupil animation has been live tested on a Quest Pro in VRChat. A Hub build or screenshot preview alone does not verify camera and avatar behavior on other systems.

**Camera cheek puff (experimental)** uses normalized camera predictions independently of native cheek calibration. It needs a combined model selected under Lower-face model and starts off. Disable it to use the selected native cheek style. Camera UDP has a 500 ms lease; stopped or expired output falls back to native values.

Pupil image preprocessing can use a verified GPU runtime. CPU handles contour geometry and smoothing. Latest-frame workers and subscriber-only JPEG encoding bound queued work; Activity reports backend and timing.
