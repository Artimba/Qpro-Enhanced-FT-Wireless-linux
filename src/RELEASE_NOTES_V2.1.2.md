# QproFaceTracking V2.1.2

## Before you install

A rooted Quest Pro and the latest [VRCFaceTracking on Steam](https://store.steampowered.com/app/3329480/VRCFaceTracking/) are required. Extract the entire **QproFaceTracking V2.1.2.zip** folder before opening the Hub. GitHub's automatically generated source archives do not contain the ready-to-run app.

Follow the PDF guide included in the ZIP, or use the [text setup instructions](https://github.com/Fwooffy/Qpro-Enhanced-FT-Wireless/blob/main/src/RELEASE_INSTRUCTIONS.md).

## What's in V2.1.2

- **Interrupted gaze recovery:** Qpro records the previous headset eye-model selection before applying its temporary patch. A verified interrupted session can be recovered after a reboot, even when its temporary mount is gone. Recovery checks the headset, owning process, original model and temporary source before changing anything; foreign mounts and unknown legacy settings are left alone.
- **Legacy gaze reset:** Added **Reset legacy gaze** for an older session with no recovery record. After explicit confirmation, it chooses the normal nonexperimental headset gaze selection. It refuses active independent-gaze Magisk modules and unverified eye-model or tracking-engine overlays. It does not uninstall modules or overwrite firmware.
- **Gaze setup checks:** **Check gaze setup** now comes before **Prepare gaze**. Its result popup identifies the detected gaze method and gives the next step. Both the popup and setup card explain **Recover Qpro gaze** and **Reset legacy gaze**. Preparation and startup detect conflicting Magisk gaze methods even when experimental-model selection is off. Recovery checks both the ADB and tracking-service views before changing tracking and after restarting it. An enabled Magisk gaze module is not reported as stock tracking.
- **Headset command handling:** Compound root commands now preserve quotes and normalize Windows line endings. This fixes command parsing failures during gaze inspection, session recording and recovery.
- **Stop tracking messages:** The Hub now requires an explicit gaze recovery result instead of treating a successful process exit as proof of restoration. Stopping live overrides does not uninstall the Qpro VRCFT module or disable a Magisk module.
- **Native Virtual Desktop fallback:** Corrected the native face flag used for mouth availability and the alternate tongue layout. Mouth tracking can continue when lower-face data is valid but eye-following expressions are unavailable.
- **Expired live overrides:** Tongue, gaze and pupil overrides release their last custom values when stopped or expired, including when native tracking is unavailable. Inactive modules discard queued packets before becoming active again.
- **Gaze preview cost:** Disabling **Preview tracking cameras** now hides the gaze preview too. Visible gaze previews render at up to 20 FPS while live output retains its sample cadence. Headset tracing and ADB streaming still add processing work.
- **Model selection:** Model and session settings stay locked while tracking runs. Stop tracking, change the selection, then start again to load another model.
- **AMD ROCm setup:** Added checks for the required PyTorch host, device and SDK packages. Missing `torchgen` or incomplete host-package metadata triggers a targeted repair. Python import failures are now reported separately from GPU detection failures.
- **ROCm long-path installation failure:** New installations use a compact per-user Qpro folder instead of the extracted release's nested path. The installer checks its path budget before downloading, and the Hub, tracking and training helpers find the shared environment after it passes GPU checks. Existing environments and recordings are kept. Long-path, download-index and other package failures now have separate messages.
- **AMD GPU selection:** Improved recognition of Radeon driver name variants, including `RX6700XT`. Qpro checks actual HIP device order, excludes integrated graphics and verifies that the selected card matches the installed GPU target. GPU visibility settings inherited from another application are cleared only within Qpro's ROCm process.
- **Cheek calibration rollback:** Restored the earlier three-pose calibration: relaxed, left cheek and right cheek. Removed the four-pose overlap fit and restored its previous Balanced signal mapping and calibrated response. Native calibration remains available; the new camera-cheek model is a separate opt-in path.
- **Saved cheek profiles:** Earlier three-pose profiles remain usable. Four-pose test profiles are kept on disk but are not applied; Calibrated uses the selected source's bundled developer baseline until a new three-pose calibration is saved. The 1/0, Balanced and native options remain available.
- **Hub shutdown:** Closing the Hub waits for tracking cleanup and recovery of the recorded pre-Qpro eye-model state, then stops the configured PC ADB server. Pending status checks cannot restart it. Other Android tools using that shared server will disconnect.
- **Gaze preparation diagnostics:** Unsupported engines now report the exact firmware build and explain that tracking was left unchanged. A read-only diagnostic mode reports compatibility information without copying headset models. This does not add support for the reported Horizon OS V2.4 engine.
- **Setup guide:** Updated the PDF and text instructions to explain using one independent gaze method at a time, the temporary headset freeze when applying the Hub's gaze method, and what **Stop tracking** restores.

## Lower-face and pupil updates

- **Lower-face calibration:** Quick refinement and Full dataset append 21 cheek camera cards. Their labels come from the requested pose instead of relying on native cheek detection. Focused tongue captures and older tongue-only models remain compatible.
- **Experimental developer cheeks:** Bundled a separate Developer tongue + cheeks copy of v8, trained on one wearer. Added light and half-strength cheek examples after the initial live test showed weak gentle puffs. The revised model preserves the original tongue outputs. A short VRChat check on the training wearer confirmed better gentle right-puff response and separate sides. Quantitative strength checks and broader wearer validation are pending. The original v8 remains the default.
- **Camera cheek puff:** Added optional left/right camera predictions with continuous strengths between 0 and 1. The new control can run alongside tongue tracking or by itself. Stopped, disabled or expired camera output falls back to the selected native cheek settings.
- **Preserved tongue models:** Adding cheek outputs freezes the parent tongue model. Training creates a new paired model; a failed cheek stage does not publish an incomplete result. Existing recordings and models stay saved.
- **GPU pupil filtering:** Both eye images can use a verified NVIDIA CUDA or AMD ROCm runtime. Contour fitting, quality checks and smoothing remain on CPU. GPU errors fall back to CPU with an Activity message.
- **Camera workload:** Eye processing uses the latest frame pair and drops superseded work. Unused MJPEG previews are no longer encoded. Activity includes backend, processing time, frame age and dropped-frame diagnostics.

On the connected RX 7900 XTX, the pupil-only live check maintained approximately 24 FPS with 9–13 ms processing time. A recorded 161-frame comparison matched the CPU pupil results exactly and reduced median processing time from 14.6 ms to 9.9 ms. A three-minute combined check with tongue, camera cheeks and pupils maintained a median 23.9 FPS across all five cameras; median pupil processing was 12.7 ms. This is one PC; NVIDIA and slower-PC performance still need physical testing.

## Updating and testing

Extract this build into a new folder. **Close VRCFaceTracking**, install the matching **Virtual Desktop** or **Steam Link** module from this Hub, then reopen VRCFaceTracking. The restored cheek calibration needs this build's matching module and its Balanced calibration feed.

Use **Recover Qpro gaze** in **First-time setup** to check an interrupted gaze session and restore its verified recorded previous state. If an older build left the experimental eye-model selection enabled without a recovery record, that previous state is unknown. **Reset legacy gaze** lets you explicitly choose the normal nonexperimental selection after confirming the prompt. It refuses active gaze Magisk modules and unverified eye-model or tracking-engine overlays. To return to ordinary headset eye tracking with a gaze Magisk module installed, disable that module and reboot first. Do not combine the Hub's gaze method with an active independent-gaze Magisk module.

To compare ordinary face tracking, stop Qpro tracking, close VRCFaceTracking and wait for its module process to exit. Use **Uninstall Qpro module**, then run exactly one official module for your selected streaming app, installing the official Virtual Desktop or Steam Link module if needed. Hub **Stop tracking** ends the PC tongue model's live output; saved cheek, smirk and eyebrow adjustments belong to the installed Qpro module and can still run with the Hub closed. Neither stopping nor uninstalling the PC module disables a headset Magisk module.

Use **Calibrate cheek puff** on **Live tracking** and complete all three poses when your headset is available. Each streaming app keeps its own profile. Earlier three-pose profiles remain compatible; four-pose test profiles are ignored without deleting them.

Working tongue models from V2.0 onward can be exported through **Model manager** in the old version and imported into this build as a `.qptonguemodel` file. No tongue retraining is required to keep using their tongue outputs. Camera cheek outputs require a new cheek or combined lower-face capture. Preserve your original folder and recordings while testing.

## Test status

This is a test build. Offline cheek, GPU routing, ROCm package repair and compact-path routing, ADB shutdown, gaze preparation, interrupted gaze recovery and production-module override checks pass. USB checks on a working Quest Pro with build `51503870024400340` passed controlled interrupted-session recovery, recovery after a headset reboot and a confirmed legacy selection reset. The controlled recovery used an identical copy of the headset's existing model to test restoration; it did not validate a new gaze model. The headset's original Magisk configuration was restored afterward.

The affected users' convergence behavior, cheek calibration, a fresh ROCm install on the reported RX 7900 XT PC and physical RX 6700 XT testing remain unverified. These fixes do not establish the cause of every user's report or add support for an unsupported gaze engine. AMD ROCm support remains experimental; a mapped discrete Radeon, a compatible AMD driver, Windows 11 and internet access are needed for a new installation.
