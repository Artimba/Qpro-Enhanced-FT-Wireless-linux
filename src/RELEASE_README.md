# QproFaceTracking V2.0.2 — AMD/NVIDIA Wireless Edition

This is a clean derivative of [Qpro-Enhanced-FT](https://github.com/n0tmast3r/Qpro-Enhanced-FT) v0.1.10-poc. V2.0 added AMD ROCm for tongue-model tracking and training, while NVIDIA CUDA has also been live-tested and is functional. A CPU fallback remains available. It carries headset camera and eye data over USB or wireless ADB on a trusted Wi-Fi network. Independent gaze and relative pupil dilation remain experimental features. A rooted Quest Pro is required.

V2.0.2 restores Python modules missing from the previous release ZIP, so Quick refinement and Full dataset tongue training can reach the model stages. The AMD installer now reports success only after its GPU checks pass, and it declines ROCm setup on an NVIDIA-only PC. A working V2.0 or V2.0.1 tongue model can be exported and imported into V2.0.2 without retraining.

**Upgrading from a version before V2.0? Record and train a new tongue model.** Keep older models as backups. Working V2.0 and V2.0.1 models can be transferred through Model manager. See the [beginner PDF guide](Quest_Pro_Enhanced_Face_Tracking_Guide.pdf) for the recording steps.

**Required:** install the [latest VRCFaceTracking from Steam](https://store.steampowered.com/app/3329480/VRCFaceTracking/) and let Steam finish updating it before installing Qpro's module.

## What's in this package

- The original app, bundled Android Platform-Tools, private Python runtime archive, VRCFaceTracking module, and developer v8 demonstration tongue model.
- Local changes for AMD Radeon GPU inference and training, including the Windows ROCm training-exit fix.
- The developer eye-mapping demo profile, with local file paths removed.

The package contains **no personal camera captures, image arrays, training cache, v9 personalized model, generated headset eye patch, or machine-specific Python environment**. It starts with empty `QproRuntime/captures/` and `QproRuntime/training/` folders. The model supplied here is the upstream developer's v8 demonstration model; personalize it for a different wearer.

Start with the [beginner PDF guide](Quest_Pro_Enhanced_Face_Tracking_Guide.pdf), or use the [text setup instructions](RELEASE_INSTRUCTIONS.md) in the source repository. The Hub opens on **First-time setup** the first time you launch this extracted copy and on **Live tracking** afterward. Setup remains available in the sidebar. Keep `QproFaceTracking.exe`, `Helpers`, and `QproRuntime` together. Supporting scripts, models, and bundled Android tools live in `QproRuntime`; optional command launchers live in `Helpers` in the release ZIP (see the [helper notes](RELEASE_HELPERS_README.md)); licenses and upstream documentation live in `Docs`.

## Setup inside the Hub

Choose **USB cable** or **Wireless ADB (Wi-Fi)** in First-time setup. For Wi-Fi, enter the Quest's address and press **Connect to Quest**; use **Pair and connect** if the headset shows a pairing code, or **Enable from USB** for a one-time cable setup. The Hub saves the selected transport and uses it for tracking. Switching to USB makes the Hub target the cable connection.

Press **Install runtime**. Then install the VRCFaceTracking module for your selected source and prepare gaze if you use the Hub's independent eye gaze method. The experimental test Hub has **Install Virtual Desktop module** and **Install Steam Link module** buttons; installing one removes the other. Close VRCFaceTracking and wait for its module process to exit before pressing either button, then reopen it. The published V2.0.2 ZIP uses Virtual Desktop and keeps its original install button. The Hub's gaze method can fail on some Quest Pro firmware builds. If it does, the [beginner PDF guide](Quest_Pro_Enhanced_Face_Tracking_Guide.pdf) explains the Singularity Magisk module workaround reported working on Horizon OS v2.7. If the Hub recognizes your discrete AMD GPU, press **Install ROCm 10.0** after the PC runtime is ready. The installer validates GPU inference and training before marking the new environment ready. The helper commands below remain available for troubleshooting.

The **VRCFT module** card also has **Uninstall Qpro module** in the experimental test Hub. Close VRCFaceTracking and wait for its module process to exit before pressing it. The uninstall script removes Qpro's source module and restores previous Virtual Desktop modules backed up by this extracted copy when their original locations are unoccupied. It leaves personal captures and models alone. Restart VRCFaceTracking afterward; if ordinary Virtual Desktop face tracking is missing, install its official module again.

On **Personalize**, each capture mode has **Recorded datasets (including trained)** and **Delete selected dataset…**. Deletion requires confirmation and removes only that recording, its labels and session details, and its prepared training cache from the extracted release folder. Existing trained models stay available.

## Install on AMD Radeon

1. Open `QproFaceTracking.exe` and use **First-time setup > Install runtime**.
2. If the Hub shows an eligible AMD GPU, press **Install ROCm 10.0** on that same page. If this extracted copy already has a verified ROCm 7.2.1 installation, the button says **Upgrade to ROCm 10.0**. Mapped RX 6000, 7000, and 9000 cards use [TheRock's architecture-specific ROCm 10.0 wheels](https://github.com/ROCm/TheRock/blob/main/RELEASES.md) in `.venv-rocm-experimental`. That folder name preserves compatibility with earlier test builds; AMD's packages are stable while Qpro's integration is experimental. Allow several gigabytes of free space and an internet connection. [AMD's ROCm 10.0 matrix](https://rocm.docs.amd.com/en/latest/compatibility/compatibility-matrix.html) validates Windows 11 25H2 with Adrenalin 26.8.1; other Windows 11 builds or drivers may fail the GPU checks.
3. After the Hub reports that the GPU training and inference checks passed, choose tongue tracking. The training and live tracking scripts automatically select ROCm. Activity should name the AMD GPU when the model loads.

The separate `Helpers/Install-AMD-ROCm.cmd` and `Helpers/Launch-QproRocm.cmd` files are troubleshooting helpers. An existing verified `.venv-rocm` with PyTorch 2.9.1 + ROCm 7.2.1 remains a compatibility fallback on the eight cards in [AMD's Windows 7.2.1 list](https://rocm.docs.amd.com/projects/radeon-ryzen/en/docs-7.2.1/docs/compatibility/compatibilityrad/windows/windows_compatibility.html); that path was live-tested on an RX 7900 XTX. ROCm 10.0 in Qpro has not yet been live-tested on every mapped GPU and becomes active only after training and inference checks pass. AMD's stable Windows installation guide does not explicitly list the RX 6750/6700 (gfx1031) or RX 6600 (gfx1032) device packages, so those experimental attempts may fail. If checks fail, Qpro can use the 7.2.1 fallback where available, or the shared CPU or NVIDIA runtime.

## Install on NVIDIA or CPU

Use **First-time setup > Install runtime** in `QproFaceTracking.exe` and skip AMD ROCm. The setup detects NVIDIA hardware and installs its CUDA 12.8 PyTorch build. The original CPU path remains available when no supported GPU runtime is detected, though training is slower. NVIDIA CUDA hardware has been live-tested and is functional in this edition. See [PyTorch's Windows installation guide](https://pytorch.org/get-started/locally/) for CUDA requirements.

### Python on the PC

You do **not** need to install or remove Python yourself. The release includes a 64-bit Python 3.12 archive that Qpro extracts into `%LOCALAPPDATA%\QproFaceTracking\runtime\python-3.12.10`. It does not run the Windows Python installer, enter **Modify** mode, change PATH, or alter another Python installation. Qpro creates `%LOCALAPPDATA%\QproFaceTracking\runtime\.venv` from that private base and installs its tracking packages there. AMD's ROCm 10.0 setup creates `.venv-rocm-experimental` from the same base; it keeps any existing `.venv-rocm` fallback separate.

An existing working Qpro environment is reused. If that environment becomes unusable because its former base Python was removed or damaged, **Install runtime** rebuilds it from the bundled private Python archive. Keep the full extracted Qpro release folder available when repairing the runtime.

If **Install runtime** exits with code 1, read the first error in **Activity** rather than treating the code as a diagnosis. A missing `VCRUNTIME` or native DLL can require [Microsoft's latest x64 Visual C++ Redistributable](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170#latest-supported-redistributable-version). Repairing that prerequisite and rerunning **Install runtime** is safer than deleting the entire `%LOCALAPPDATA%\QproFaceTracking` folder. Other failures need their own Activity details.

## Wireless tracking without USB after rooting

1. Keep the rooted Quest and PC on the same trusted Wi-Fi network. On the Hub's **First-time setup** page, select **Wireless ADB (Wi-Fi)**.
2. If the Quest already exposes wireless ADB, enter its Wi-Fi IP and port (usually `5555`) and press **Connect to Quest**. The Hub verifies ADB and Magisk root before saving the address. A **Quest connected** pop-up and “Wireless Quest ready” in Activity mean you can start tracking without pairing. A **Quest connection failed** pop-up points you to Activity for the exact error. Use **Pair and connect** only if the headset requires Android's six-digit pairing flow. Enter the temporary **pairing IP:port** shown beside that code, as well as the regular **connection IP:port**. The two ports are different. A “protocol fault” during pairing often means the temporary pairing dialog closed or the regular connection port was entered instead; reopen the dialog for a fresh address and code.
3. If wireless ADB is off, connect an authorized Quest by USB once and press **Enable from USB**. The Hub checks Magisk Shell root, enables ADB over Wi-Fi, and saves the Quest address. Unplug USB afterward; the Hub uses the selected Wi-Fi connection.
4. Start Virtual Desktop on the Quest and connect to the PC over Wi-Fi. Start SteamVR and VRCFaceTracking, then apply the selected tracking modes in the Hub. Wi-Fi quality and router settings affect camera latency and stability.

On the tested Quest Pro, Singularity kept ADB TCP port `5555` enabled after reboot. If your port is closed after reboot, open **Singularity > Root Terminal** on the headset and run `su -c 'setprop service.adb.tcp.port 5555; stop adbd; start adbd'`, approving root in Magisk if prompted. This manual command affects the current boot only. Then connect to the current Quest IP again in the Hub. Some headsets instead expose Android's pairing-code flow. Use a trusted private network: the ADB TCP port remains reachable on the local network until disabled or the headset reboots.

To return to cable tracking, choose **USB cable** on the setup page. **Disable Wi-Fi ADB** can switch the connected Quest back to USB mode for the current boot. The helper `.cmd` files remain available for troubleshooting. Wireless camera and eye relay is separate from Virtual Desktop's PCVR video stream; both can use Wi-Fi. If connection fails, check for router client isolation and verify the Quest's current IP and port.

## Camera preview windows

On **Live tracking**, **Preview tracking cameras** controls whether the camera and tongue model windows appear. Turn it off before starting a session to hide the live camera windows. Tongue and pupil output keep running; the cameras are still used for tracking. The preference is saved for later launches and takes effect on the next tracking start. Guided tongue capture still opens its prompt window because the capture workflow needs it.

## Experimental relative pupil dilation

The **Live tracking** page shows the active PC tongue inference backend after its model loads: **CPU**, **NVIDIA CUDA**, or **AMD ROCm**. It reads the loaded PyTorch runtime, so a GPU being installed does not by itself make the indicator say GPU. Pupil camera processing has a separate **CPU** indicator. Independent gaze is processed on the Quest headset.

**Stop tracking** requests a clean shutdown of the camera preview and independent gaze. The camera preview checks for Stop even when the headset stream pauses. The gaze process may take several more seconds to restore Meta's stock model. Wait for the Activity log to confirm restoration before starting another session. If cleanup is still running after the Hub's wait period, the stop request remains active and the button can be pressed again.

The Hub has an optional **Experimental relative pupil dilation (eye cameras)** checkbox, off by default. Enable it alongside gaze and/or tongue tracking, or on its own, then press **Start tracking**. It requires the rooted headset's eye cameras, enabled eye tracking, the PC runtime, and the **Qpro VRCFaceTracking module** installed from the Hub. When tongue tracking is also selected, both features share one camera stream. A wireless camera stream can use more bandwidth when all cameras are selected.

**Pupil response** controls how strongly relative changes move the avatar value away from neutral. It runs from 1.0× to 3.0× in 0.2× steps, with 1.4× as the default. The pupil filter now responds faster to real changes while still rejecting single-frame diameter errors. A gradual curve keeps larger response settings from hitting the animation limit as abruptly. The Qpro VRCFaceTracking module now maps the output's 2–8 relative range to the full normalized 0–1 dilation animation range, with neutral at 0.5. Start with 1.4× and increase it gradually; use a lower setting if the avatar looks jumpy. The setting changes the animation value, not the camera detector or a measured pupil diameter. Your avatar must have a pupil dilation animation mapped in VRCFaceTracking for the change to be visible.

Independent gaze briefly restarts Meta trackingservice when its temporary eye model is applied and again when stock is restored. The headset may look frozen and lose positional tracking for a moment during those two transitions. It has not crashed; wait for the service to return and check Activity before starting another session. Pupil-only tracking does not request that service restart.

If the independent-gaze process fails during startup, stops unexpectedly, or receives no paired eye samples for 20 seconds, the Hub unchecks **Independent Eye Gaze** and saves that choice for later launches. It checks for a remaining Qpro eye-model mount and restores stock if needed. Other selected tracking can continue. Read **Activity** to confirm recovery; if the headset is disconnected, reconnect it before retrying gaze. Select the option manually when you want to retry.

For each eye, 12 consecutive, steady valid frames establish a session baseline shown as approximately 5 mm in VRCFaceTracking. Look straight ahead and keep your gaze steady while the pupil status warms up. Later values describe *relative change for avatar animation*; they are **not measured millimetres, medical data, or a calibrated absolute pupil size**. The camera preview outlines accepted pupil candidates and the Activity log prints `PUPIL_STATUS` with the current estimates and each eye's state. If an eye is occluded or moves far from its calibrated gaze position, its value returns to the module's neutral 5 mm; stopping the stream also restores neutral values. Changes in headset fit, lighting, reflections, and eyelid position may affect detection. The detector has passed synthetic tests and replay of two short live Quest Pro eye-camera captures, including blinks and gaze motion. Relative pupil animation has also been live tested in VRChat on a Quest Pro; the visible result depends on the avatar's pupil mapping.

The AMD ROCm 10.0 setup downloads packages from `stable.repo.amd.com`; the original runtime setup downloads Python dependencies and PyTorch. No personal headset data is included in this archive. New captures and models are created only after you use the app. For help, use the [community Discord](https://discord.gg/ghvuJTpRu4); this server is not owned by Fwooffy.

## Changes from the upstream package

The modified files include `build-and-run.ps1`, `receiver.py`, `pupil_dilation.py`, the Qpro VRCFaceTracking module, the tongue training scripts, `train_tongue_model.py`, `tongue_model_preview.py`, `native_raw_eye_probe.py`, and the Hub source. The Hub executable is rebuilt from this project's source so its setup and tracking checks accept the selected wireless ADB headset. The AMD runtime installer and wireless pair/connect/setup/launch/disable helpers are included. `SHA256SUMS.txt` lists the exact contents of the V2.0.2 ZIP.

This edition is not an official release of the upstream repository. See [LICENSE](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.md).
