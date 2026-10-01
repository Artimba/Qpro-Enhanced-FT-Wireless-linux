# QproFaceTracking V2.1.0 Gaze Recovery Test

## Before you install

A rooted Quest Pro and the latest [VRCFaceTracking on Steam](https://store.steampowered.com/app/3329480/VRCFaceTracking/) are required. Extract the entire **QproFaceTracking V2.1.0 Gaze Recovery Test.zip** folder before opening the Hub. GitHub's automatically generated source archives do not contain the ready-to-run app.

Follow the PDF guide included in the ZIP, or use the [text setup instructions](https://github.com/Fwooffy/Qpro-Enhanced-FT-Wireless/blob/main/src/RELEASE_INSTRUCTIONS.md).

## Bug fixes

- **Interrupted gaze recovery:** Qpro records the previous headset eye-model selection before applying its temporary patch. A verified interrupted session can be recovered after a reboot, even when its temporary mount is gone. Recovery checks the headset, owning process, original model and temporary source before changing anything; foreign mounts and unknown legacy settings are left alone.
- **Stop tracking messages:** The Hub now requires an explicit gaze recovery result instead of treating a successful process exit as proof of restoration. Stopping live overrides does not uninstall the Qpro VRCFT module or disable a Magisk module.
- **Native Virtual Desktop fallback:** Corrected the native face flag used for mouth availability and the alternate tongue layout. Mouth tracking can continue when lower-face data is valid but eye-following expressions are unavailable.
- **Expired live overrides:** Tongue, gaze and pupil overrides release their last custom values when stopped or expired, including when native tracking is unavailable. Inactive modules discard queued packets before becoming active again.
- **Gaze preview cost:** Disabling **Preview tracking cameras** now hides the gaze preview too. Visible gaze previews render at up to 20 FPS while live output retains its sample cadence. Headset tracing and ADB streaming still add processing work.
- **Model selection:** Model and session settings stay locked while tracking runs. Stop tracking, change the selection, then start again to load another model.
- **AMD ROCm setup:** Added checks for the required PyTorch host, device and SDK packages. Missing `torchgen` or incomplete host-package metadata triggers a targeted repair. Python import failures are now reported separately from GPU detection failures.
- **AMD GPU selection:** Improved recognition of Radeon driver name variants, including `RX6700XT`. Qpro checks actual HIP device order, excludes integrated graphics and verifies that the selected card matches the installed GPU target. GPU visibility settings inherited from another application are cleared only within Qpro's ROCm process.
- **Cheek calibration:** Calibration now reads the original cheek signals and records relaxed, left, right and both-cheek poses. It measures overlap between the sides, supports unequal channel ranges and rejects weak or unstable samples with clearer retry messages.
- **Calibrated cheek movement:** Improved intermediate puff strengths and uneven two-cheek puffs. Added a short confirmation period when switching sides and reduced movement remaining on the relaxed cheek.
- **Hub shutdown:** Closing the Hub waits for tracking cleanup and stock eye-model restoration, then stops the configured PC ADB server. Pending status checks cannot restart it. Other Android tools using that shared server will disconnect.
- **Gaze preparation diagnostics:** Unsupported engines now report the exact firmware build and explain that tracking was left unchanged. A read-only diagnostic mode reports compatibility information without copying headset models. This does not add support for the reported Horizon OS V2.4 engine.
- **Setup guide:** Updated the PDF and text instructions to explain using one independent gaze method at a time, the temporary headset freeze when applying the Hub's gaze method, and what **Stop tracking** restores.

## Updating and testing

Extract this build into a new folder. **Close VRCFaceTracking**, install the matching **Virtual Desktop** or **Steam Link** module from this Hub, then reopen VRCFaceTracking. The new cheek calibration requires the updated module's raw cheek feed.

Use **Recover Qpro gaze** in **First-time setup** to check an interrupted gaze session. If an older build left the experimental eye-model selection enabled without a recovery record, Qpro reports that the previous state is unknown. Do not combine the Hub's gaze method with an active independent-gaze Magisk module.

To compare ordinary face tracking, close VRCFaceTracking and use **Uninstall Qpro module**, then run exactly one official module for your streaming app. Hub **Stop tracking** ends the PC tongue model's live output; saved cheek, smirk and eyebrow adjustments belong to the installed Qpro module and can still run with the Hub closed.

Use **Calibrate cheek puff** on **Live tracking** and complete all four poses. Each streaming app keeps its own profile. Existing profiles remain compatible, but recalibrating uses the new method.

Working tongue models from V2.0 onward can be exported through **Model manager** in the old version and imported into this build as a `.qptonguemodel` file. No tongue retraining is required for these fixes. Preserve your original folder and recordings while testing.

## Test status

This is a test build. Offline cheek, GPU routing, ROCm package repair, ADB shutdown, gaze preparation, interrupted gaze recovery and production-module override checks pass. Live gaze recovery, the reported convergence behavior, cheek calibration and physical RX 6700 XT testing remain pending. These fixes do not establish the cause of every user's report or add support for an unsupported gaze engine. AMD ROCm support remains experimental; a mapped discrete Radeon, a compatible AMD driver, Windows 11 and internet access are needed for a new installation.
