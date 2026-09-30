## Before you install

A **rooted Meta Quest Pro** and the **latest [VRCFaceTracking on Steam](https://store.steampowered.com/app/3329480/VRCFaceTracking/)** are required. Download **QproFaceTracking V2.1.0.zip** and extract the entire folder before opening the Hub. GitHub's automatically generated source archives do not contain the ready-to-run app.

Follow the PDF guide included in the ZIP, or use the [text setup instructions](https://github.com/Fwooffy/Qpro-Enhanced-FT-Wireless/blob/main/src/RELEASE_INSTRUCTIONS.md).

**Close VRCFaceTracking before installing, switching, or uninstalling a Qpro module.** Reopen it afterward. Installing the module for your selected streaming app removes the other Qpro source module.

## What's in V2.1.0

- **Steam Link:** choose Virtual Desktop or Steam Link in the Hub and install its matching module. Steam Link uses OSC port **9015** with eye and face sharing enabled. Native tongue output remains available when Qpro camera tracking is off.
- **Cheek controls:** toggle individual cheek puff or suck. Puff styles are **Calibrated**, **1/0**, and **Balanced**; turning the feature off restores native source values. Guided cheek calibration and separate developer baselines are included for both streaming apps.
- **Face response:** eyebrow sensitivity controls, stronger one-sided smirks, smoother tongue extension/retraction, and brief tongue visibility holds.
- **Tongue training:** a 22-card **Focused diagonals + facial hair** capture and expanded 58-card **Full dataset** capture. Quick and Focused training extend the model selected in Live tracking.
- **Runtime setup:** private Python works alongside existing installations, more detailed Activity progress, non-interactive setup launches, clearer failures, and discrete GPU checks. Experimental ROCm 10.0 setup is available for mapped AMD RX 6000, 7000, and 9000 cards; it is enabled only after training and inference checks pass.
- **ROCm detection:** fixed GPU rejection caused by treating the HIP compiler version as the ROCm release. Setup reuses verified installed packages, reports both version numbers, and includes the RX 6700 and RX 6650 XT device mappings.
- **Hub and gaze:** layout fixes, dataset deletion, source-specific module buttons, gaze off by default, headset-aware gaze preparation, and automatic recovery when the gaze process fails.

## Mustachio: optional and highly experimental

Mustachio extends a copy of developer v8 using a focused recording from **one bearded and moustached wearer**. Independent clean-shaven validation and broader wearer testing are still pending. It may perform worse for some people and cannot recover tongue detail completely covered by hair.

**Developer v8 remains the default.** Select Mustachio explicitly under **Live tracking > Tongue model** to try it. Quick and Focused refinements made from it retain their experimental status through renaming, export, and import. Only model weights are included; personal camera recordings are not included.

## Upgrading and known limits

- Working V2.0 through V2.0.2 tongue models can be exported from **Model manager** in the old version and imported into V2.1.0. You do not need to retrain. Models from before V2.0 should be recorded and trained again.
- Independent gaze and convergence depend on the exact Horizon OS build. Applying or restoring the Hub's gaze model briefly restarts the headset's tracking service and can look like a crash. Singularity's Magisk gaze route has been reported working on **Horizon OS v2.7**; use one gaze method at a time.
- Relative pupil dilation is an animation estimate, and AMD ROCm integration remains experimental. NVIDIA CUDA and the existing RX 7900 XTX ROCm path have been live tested; the new ROCm path has not been tested on every mapped card. See the setup guide for driver and firmware requirements.

For help, share relevant **Activity** lines in the [community Discord](https://discord.gg/ghvuJTpRu4). This server is not owned by Fwooffy.
