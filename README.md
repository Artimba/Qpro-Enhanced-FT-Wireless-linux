# QproFaceTracking V2.0.2

A Windows Hub for enhanced face tracking on a **rooted Meta Quest Pro**, based on [Qpro-Enhanced-FT by n0tmast3r](https://github.com/n0tmast3r/Qpro-Enhanced-FT). V2.0.2 supports USB and wireless ADB, tongue tracking with AMD ROCm or NVIDIA CUDA, and optional independent eye gaze and relative pupil animation. This update restores missing tongue training modules to the release ZIP and improves AMD ROCm setup status and GPU checks.

**Download the runnable ZIP from [Releases](https://github.com/Fwooffy/Qpro-Enhanced-FT-Wireless/releases/latest).** Extract it before opening `QproFaceTracking.exe`. GitHub's automatic source ZIP does not include the Hub executable or packaged models and tools.

The **latest [VRCFaceTracking from Steam](https://store.steampowered.com/app/3329480/VRCFaceTracking/)** is required. If you used a Qpro version before V2.0, record and train a new tongue model. A working V2.0 or V2.0.1 model can be exported and imported into V2.0.2.

## Guides

- [Beginner PDF guide](src/Quest_Pro_Enhanced_Face_Tracking_Guide.pdf)
- [Text setup instructions](src/RELEASE_INSTRUCTIONS.md)
- [Detailed release notes](src/RELEASE_README.md)
- [Quest Pro rooting guide by glorpette and Fwooffy](https://github.com/glorpette/quest-guides/blob/main/root_guide_by_fwooffy.md)

## Repository layout

The runnable release keeps the Hub and guide in its main folder. This GitHub repository keeps the development source, tests, and build scripts together in [`src/`](src/). Run `src/build-release.ps1` from the source folder when building your own package; it needs the binary assets from an existing release or the original package.

For help, share relevant Hub **Activity** lines in the [community Discord](https://discord.gg/ghvuJTpRu4). The server is not owned by Fwooffy. QproFaceTracking is unaffiliated with Meta, Virtual Desktop, VRCFaceTracking, and VRChat.

## Credits

- [n0tmast3r](https://github.com/n0tmast3r/Qpro-Enhanced-FT) created the original Qpro-Enhanced-FT project that this edition is based on.
- Fwooffy made this V2.0 edition and its beginner face tracking guide.
- [Lumince and the Singularity contributors](https://github.com/Lumince/singularity) created the headset root project used by this workflow.
- Fwooffy wrote the original [beginner Quest root guide](https://github.com/glorpette/quest-guides/blob/main/root_guide_by_fwooffy.md), and [glorpette](https://github.com/glorpette/quest-guides) made and hosts its GitHub version.
