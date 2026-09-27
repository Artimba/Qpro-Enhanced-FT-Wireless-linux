# QproFaceTracking V2.0 — AMD/NVIDIA Wireless Edition

Face-tracking tools for a **rooted Quest Pro** on Windows. V2.0 builds on [n0tmast3r's Qpro-Enhanced-FT](https://github.com/n0tmast3r/Qpro-Enhanced-FT) and adds wireless ADB support and an optional AMD ROCm path for tongue tracking and training. NVIDIA CUDA has also been live-tested and is functional; a CPU fallback remains available.

**[Download the latest release](https://github.com/Fwooffy/Qpro-Enhanced-FT-Wireless/releases/latest)** · [Beginner PDF guide](Quest_Pro_Enhanced_Face_Tracking_Guide.pdf) · [Text setup instructions](RELEASE_INSTRUCTIONS.md) · [Detailed technical notes](RELEASE_README.md) · [Community Discord](https://discord.gg/ghvuJTpRu4)

Download the **ZIP asset** from Releases and extract the entire folder. GitHub's automatically generated source archives do not include the runnable Hub, demonstration model, Android tools, or bundled Python installer.

## New to rooting a Quest Pro?

Start with [**Root Your Meta Quest with Singularity — a beginner guide by Fwooffy and glorpette**](https://github.com/glorpette/quest-guides/blob/main/root_guide_by_fwooffy.md). Fwooffy created the original guide, and glorpette made the GitHub version in the `glorpette/quest-guides` repository. It covers a Meta developer account, ADB, checking the headset's exact firmware build, Singularity, wireless debugging, and verifying root.

Before changing your headset, compare its **exact model and firmware build** with the current [Singularity documentation and releases](https://github.com/Lumince/singularity). The root guide covers several Quest models; **this face-tracking app is for Quest Pro**.

## What the public release does

- Preserves Virtual Desktop's normal face, brow, jaw, and blink tracking while adding optional independent eye gaze and convergence.
- Adds stereo camera tongue tracking with a demonstration model that can be personalized through a quick refinement or full capture.
- Carries headset camera and eye data over USB or wireless ADB. Wireless ADB requires Magisk root access for Shell / ADB Shell and a trusted private network.
- Runs the tongue model on AMD ROCm, NVIDIA CUDA, or CPU when the corresponding runtime is available. The Hub's Activity log reports the backend used.

**Experimental relative pupil dilation** is included in V2.0 and has been live-tested in VRChat. It is an avatar animation estimate, not a calibrated pupil measurement.

## Requirements

- Windows 10 or 11, x64.
- A rooted Quest Pro with eye and face tracking enabled, Developer Mode on, and an authorized ADB connection.
- Magisk Superuser access granted to Shell / ADB Shell.
- Virtual Desktop, SteamVR, and the [latest VRCFaceTracking from Steam](https://store.steampowered.com/app/3329480/VRCFaceTracking/) for the tracking workflow. Let Steam finish updating it before installing the Qpro bridge.
- Internet access and free disk space for the PC runtime. The release bundles ADB, so a separate ADB installation is not needed for the Hub.

The wireless transport and AMD inference were tested on a Quest Pro with build `51503870024400340` and a Radeon RX 7900 XTX. NVIDIA CUDA hardware has also been live-tested and is functional. Eye convergence can vary by firmware; a successful root or eye-camera connection does not establish convergence support on every build. If the Hub's independent-gaze patch fails, [Singularity's Quest Pro Independent Eye Gaze Magisk module](https://github.com/Lumince/singularity) is an alternative reported working on Horizon OS v2.7. Install Magisk OverlayFS first, then the gaze module through **Singularity > Apps/Modules > Magisk repo**; use only one gaze method at a time.

## First run

1. Extract the complete release ZIP. Open `QproFaceTracking.exe` from the extracted folder, not from inside the ZIP.
2. In **First-time setup**, choose **USB cable** or **Wireless ADB (Wi-Fi)**. If you choose wireless, enter the Quest's IP and use the built-in connect or pairing controls. The Hub saves your choice and uses it for tracking.
3. Use **Install runtime**. It creates a separate Qpro Python environment. If a compatible 64-bit Python 3.12 is already installed, setup can use it as the base without replacing its packages. On a supported AMD GPU, use **Install AMD ROCm** in the same page afterward; the Hub then selects that runtime automatically for tongue inference and training. NVIDIA and CPU users skip ROCm.
4. Close VRCFaceTracking, use **Install bridge** in the Hub, then restart VRCFaceTracking.
5. For independent gaze, connect the rooted headset and use **Prepare gaze**. The Hub makes a temporary patch from your headset's own eye model.
6. Start Virtual Desktop, SteamVR, and VRCFaceTracking. Confirm ordinary face tracking works, select the features you want in the Hub, then press **Start tracking**.
7. When finished, press **Stop tracking** and wait for the Activity log to confirm cleanup.

Applying or restoring the Hub's independent gaze model briefly restarts Meta trackingservice. The headset can appear frozen and momentarily lose positional tracking while it returns; this is expected, not a headset crash. Wait for the Hub's Activity log to confirm tracking has resumed.

The Hub contains the setup controls and opens on First-time setup on the first launch of an extracted copy, then on Live tracking. The `.cmd` troubleshooting helpers are grouped in `Helpers` in the ZIP. Live tracking has a **Preview tracking cameras** switch for hiding camera windows without stopping tracking. The release ZIP has the [beginner PDF guide](Quest_Pro_Enhanced_Face_Tracking_Guide.pdf) in its main folder. It covers USB, wireless pairing, the PC runtime, and tracking; the [technical notes](RELEASE_README.md) give more detail.

If Windows will not let you delete an older extracted Qpro folder, close its Hub and camera preview first. If the folder is still in use, open Task Manager > Details and end a leftover `adb.exe` process, then try deleting the old folder again. Ending `adb.exe` temporarily disconnects other Android tools using that ADB server.

## Models, data, and source

The bundled tongue model and eye profile are **developer demonstrations**, so alignment and tongue detection may differ for another wearer. Quick refinement is a practical starting point. Personal captures, trained models, saved headset addresses, and generated eye patches are not included in the release ZIP. Camera captures are sensitive: share them only with the wearer's permission.

**For this version, record and train a new tongue model even if you used an older Qpro release.** Keep old model exports as backups; importing one does not replace a new capture and training run. The [beginner PDF guide](Quest_Pro_Enhanced_Face_Tracking_Guide.pdf) walks through the steps.

This repository holds the editable source. Its release build also needs larger assets distributed with the ZIP. See [contributing](CONTRIBUTING.md), [third-party notices](THIRD_PARTY_NOTICES.md), and the [license](LICENSE) before redistributing changes. The [upstream README](UPSTREAM-README.md) retains the original project's notes, including older USB-oriented instructions.

## Credits

- [n0tmast3r](https://github.com/n0tmast3r/Qpro-Enhanced-FT) created the original Qpro-Enhanced-FT project.
- [Lumince and Singularity contributors](https://github.com/Lumince/singularity) created the headset root project used by this workflow.
- **Fwooffy** created the original [beginner Quest root guide](https://github.com/glorpette/quest-guides/blob/main/root_guide_by_fwooffy.md). **[glorpette](https://github.com/glorpette/quest-guides)** made and hosts its GitHub version.

This project is unaffiliated with Meta, Virtual Desktop, VRCFaceTracking, or VRChat.
