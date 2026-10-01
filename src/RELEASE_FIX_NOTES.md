# QproFaceTracking V2.1.0 Fix Test

## Before you install

A rooted Quest Pro and the latest [VRCFaceTracking on Steam](https://store.steampowered.com/app/3329480/VRCFaceTracking/) are required. Extract the entire **QproFaceTracking V2.1.0 Fix Test.zip** folder before opening the Hub. GitHub's automatically generated source archives do not contain the ready-to-run app.

Follow the PDF guide included in the ZIP, or use the [text setup instructions](https://github.com/Fwooffy/Qpro-Enhanced-FT-Wireless/blob/main/src/RELEASE_INSTRUCTIONS.md).

## Bug fixes

- **AMD ROCm setup:** Added checks for the required PyTorch host, device and SDK packages. Missing `torchgen` or incomplete host-package metadata triggers a targeted repair. Python import failures are now reported separately from GPU detection failures.
- **AMD GPU selection:** Improved recognition of Radeon driver name variants, including `RX6700XT`. Qpro checks actual HIP device order, excludes integrated graphics and verifies that the selected card matches the installed GPU target. GPU visibility settings inherited from another application are cleared only within Qpro's ROCm process.
- **Cheek calibration:** Calibration now reads the original cheek signals and records relaxed, left, right and both-cheek poses. It measures overlap between the sides, supports unequal channel ranges and rejects weak or unstable samples with clearer retry messages.
- **Calibrated cheek movement:** Improved intermediate puff strengths and uneven two-cheek puffs. Added a short confirmation period when switching sides and reduced movement remaining on the relaxed cheek.
- **Hub shutdown:** Closing the Hub waits for tracking cleanup and stock eye-model restoration, then stops the configured PC ADB server. Pending status checks cannot restart it. Other Android tools using that shared server will disconnect.
- **Gaze preparation diagnostics:** Unsupported engines now report the exact firmware build and explain that tracking was left unchanged. A read-only diagnostic mode reports compatibility information without copying headset models. This does not add support for the reported Horizon OS V2.4 engine.
- **Setup guide:** Updated the PDF and text instructions to explain using one independent gaze method at a time, the temporary headset freeze when applying the Hub's gaze method, and what **Stop tracking** restores.

## Updating and testing

Extract this build into a new folder. **Close VRCFaceTracking**, install the matching **Virtual Desktop** or **Steam Link** module from this Hub, then reopen VRCFaceTracking. The new cheek calibration requires the updated module's raw cheek feed.

Use **Calibrate cheek puff** on **Live tracking** and complete all four poses. Each streaming app keeps its own profile. Existing profiles remain compatible, but recalibrating uses the new method.

Working tongue models from V2.0 onward can be exported through **Model manager** in the old version and imported into this build as a `.qptonguemodel` file. No tongue retraining is required for these fixes. Preserve your original folder and recordings while testing.

## Test status

This is a fix test build. Offline cheek, GPU routing, ROCm package repair, ADB shutdown and gaze preparation checks pass. Live cheek calibration and physical RX 6700 XT testing remain pending. AMD ROCm support remains experimental; a mapped discrete Radeon, a compatible AMD driver, Windows 11 and internet access are needed for a new installation.
