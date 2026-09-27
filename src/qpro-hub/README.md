# QproFaceTracking Hub

This is the native Windows control app for the Quest Pro tracking package. The Hub is a WinForms application targeting .NET 10. It keeps the existing PowerShell, Python, headset, and VRCFaceTracking components as the tracking back end.

## Pages

- **Live tracking:** connection status; independent gaze, tongue, and experimental relative pupil controls; persistent Apply and Stop buttons.
- **First-time setup:** USB/Wi-Fi connection selection and wireless enable, connect, and pairing controls; PC runtime, optional AMD ROCm installer, combined VRCFaceTracking bridge, and gaze preparation.
- **Tongue personalization:** quick refinement and full dataset capture and training.
- **Model manager:** rename, export, import, and delete personal tongue models.
- **Activity:** setup, training, tracking, and error output.

## Source layout

- `Program.cs`: entry point, release self-test, and preview capture.
- `HubForm.cs` and `HubLayoutV2.cs`: window state and page layout.
- `HubWorkflows.cs` and `HubData.cs`: setup, tracking, capture, training, datasets, and model operations.
- `HubEnvironment.cs`: ADB connection and local dependency discovery.
- `HubScriptFactory.cs`: launches package scripts with the selected Python and ADB environment.
- `HubSystem.cs`, `HubTheme.cs`, and `HubControls.cs`: status updates, styling, and custom controls.

Build with `dotnet build qpro-hub/QproFaceTracking.Hub.csproj -c Release` from `src/`. Pass `--root <complete release folder>` when launching a build outside the packaged release. The Hub does not bundle its headset binaries, pretrained models, Python runtime, or Android tools; those remain in the complete release ZIP.

The interface uses per-monitor DPI awareness. Text widths and the sidebar respond to window size; content pages scroll vertically at the minimum size, while Apply and Stop remain visible at the bottom. The preview command can render compact windows for layout checks, for example `QproFaceTracking.Hub.exe --root <release folder> --render-preview <output.png> --preview-viewport 700 500 --preview-page setup --preview-scroll-bottom`. Compact previews do not replace testing on a real high-DPI monitor.

The pupil option is experimental. Its image and packet tests pass, and relative pupil animation has been live tested on a Quest Pro in VRChat. A Hub build or screenshot preview alone does not verify camera and avatar behavior on other systems.
