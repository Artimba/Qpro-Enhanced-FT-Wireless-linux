# QproFaceTracking V2.0.2: text setup guide

> **Before you start: you need a rooted Meta Quest Pro and the latest VRCFaceTracking from Steam.** This program will not work on an unrooted headset or a different Quest model. If you still need to root your Quest Pro, use [Fwooffy and glorpette's beginner root guide](https://github.com/glorpette/quest-guides/blob/main/root_guide_by_fwooffy.md). Check that your headset's exact software version is supported by [Singularity](https://github.com/Lumince/singularity) before following a root guide. [Install or update VRCFaceTracking through Steam](https://store.steampowered.com/app/3329480/VRCFaceTracking/) before using Qpro's module.
>
> **If you used a QproFaceTracking version before V2.0, record and train your tongue model again.** Do not import a pre-V2.0 model and assume it will work correctly. A working V2.0 or V2.0.1 model can be exported and imported into V2.0.2. The included developer model is only a starting point.

> **Steam Link is experimental in the new test build.** The public V2.0.2 release ZIP uses Virtual Desktop. If your Hub does not show a **Face-tracking source** choice, follow the Virtual Desktop steps below.

Start with the headset's eye tracking, then set up the PC software. The **Hub** is the `QproFaceTracking.exe` program. **ADB** is the connection it uses to talk to your Quest. The **Qpro module** sends tracking results to VRCFaceTracking.

## 1. Set up eye tracking and independent gaze first

1. On the rooted Quest Pro, open **Settings > Movement Settings**. Turn on **Eye Tracking** and **Natural Facial Expressions**, then run eye calibration if offered. If these are already enabled, leave them on. Keep Developer Mode enabled.
2. If **Magisk OverlayFS** is not installed yet, open **Singularity > Apps/Modules > Magisk repo** and install it. Follow any reboot prompts. If it is already enabled in Magisk, you can skip reinstalling it.
3. In the same Singularity menu, install **Quest Pro Independent Eye Gaze** if it is not installed yet. Reboot if prompted, then check in Magisk that both modules are enabled. This is the headset-side independent gaze route.
4. Once your selected headset streaming app and VRCFaceTracking are installed in section 3, check that left and right gaze and convergence move correctly. The Magisk route has been reported working on **Horizon OS v2.7**, but exact firmware builds can behave differently.

**Use one independent gaze method at a time.** While the Magisk module is active, leave **Independent Eye Gaze** unchecked in the Hub and skip **Prepare gaze** in section 5. If you want to try the Hub's temporary gaze method instead, disable the Magisk gaze module and reboot first. **Stop tracking** only reverses the Hub's changes; it does not disable a Magisk module.

## 2. Download and open the Hub

1. Download the QproFaceTracking **ZIP file** from [this project's Releases page](https://github.com/Fwooffy/Qpro-Enhanced-FT-Wireless/releases). Choose the named QproFaceTracking ZIP, **not** GitHub's “Source code” ZIP.
2. In Windows File Explorer, right-click the ZIP and choose **Extract All**. Open the extracted folder. Keep `QproFaceTracking.exe`, `Helpers`, and `QproRuntime` together.
3. Double-click `QproFaceTracking.exe`. It opens on **First-time setup** the first time. You can return to that page from the menu on the left.

You need an internet connection and several gigabytes of free space for setup. The ZIP already includes ADB. The Hub installs the Python parts it needs, so you do not have to install ADB or Python separately for this program.

## 3. Get the PC apps ready

If Magisk asks whether **Shell / ADB Shell** may have root access, allow it. Keep the Quest awake while setting up the Hub.

For the published V2.0.2 ZIP, install [Virtual Desktop and its PC Streamer](https://www.vrdesktop.net/), [SteamVR](https://store.steampowered.com/app/250820/SteamVR/), and the [latest VRCFaceTracking from Steam](https://store.steampowered.com/app/3329480/VRCFaceTracking/). The experimental test build can use Steam Link in place of Virtual Desktop. **The current Steam VRCFaceTracking is required for either source.** Let Steam finish updating it before installing Qpro's module. Try ordinary face and blink tracking in VRCFaceTracking first. If that does not work, fix it before turning on Qpro's extra features.

### Steam Link source in the experimental test build

You can choose **Steam Link** under **Face-tracking source** instead of Virtual Desktop. SteamVR and the latest VRCFaceTracking are still required. In Steam Link on the headset, open **Advanced Settings**, turn on **OSC**, **Share eye tracking data**, and **Share face tracking data**, then set **OSC Output Port** to **9015**. These are Steam Link's tracking settings; the Hub's **Connection type** still chooses USB or wireless ADB for Qpro's headset camera stream.

Close VRCFaceTracking and wait for its module process to exit before switching sources. In **First-time setup**, choose **Steam Link** and press **Install Steam Link module**; reopen VRCFaceTracking afterward. Installing this Qpro module removes the Qpro Virtual Desktop module. A separate LinkFT or Steam Link VRCFaceTracking module still needs to be removed through VRCFaceTracking because it competes for the same OSC port and eye/face slots. Steam Link face, blink, and mouth tracking worked after a headset restart, Qpro's camera tongue override moved in VRChat, and native Steam TongueOut worked with VRCFaceTracking running on Steam Link. To return to Virtual Desktop, choose it in the Hub, press **Install Virtual Desktop module**, then reopen VRCFaceTracking.

If you want to stream the **Virtual Desktop picture over a USB cable**, Fwooffy's working setup used the Virtual Desktop **Beta** channel. In the Meta Horizon phone app or the headset's app library, long-press Virtual Desktop, open **Settings > Release Channels**, choose **Beta**, and update it. This changes VR video streaming. The Hub's USB or wireless choice below controls **tracking data**, which is separate.

### Hand and controller tracking with Virtual Desktop

Virtual Desktop sends Quest hand input through its SteamVR driver; Qpro's VRCFaceTracking module handles face and eye expressions and does not control VRChat's hands. For finger tracking, enable hand tracking in Quest settings and **Avatars Use Finger Tracking** in VRChat's **Controls** menu. In Virtual Desktop, tap the two controllers together twice, set them on a flat surface, and hold your empty hands where the headset cameras can see them, as described in [VRChat's finger-tracking guide](https://wiki.vrchat.com/wiki/Finger_Tracking). This restored finger tracking in a live Quest Pro check. Picking up the controllers returns to controller input.

Singularity's **Simultaneous Hands Controllers** AIO tweak alone did not make finger motion reach VRChat while controllers were held in that check. With one controller held, Virtual Desktop also returned to controller input for both hands. Mixed input needs support from the headset app and its SteamVR driver; it is not a Qpro face-tracking setting. [Singularity currently labels the tweak buggy](https://github.com/Lumince/singularity). If empty-hand tracking still shows controller gestures, first confirm the Quest and VRChat settings and repeat the double-tap switch before changing the Qpro module.

## 4. Connect your Quest to the Hub

Choose **one** connection type on **First-time setup**. If this is your first time using the Hub, USB is the easiest way to check that it works. You can switch to wireless later.

### Option A: USB cable

1. Plug the Quest Pro into the PC and put the headset on.
2. If a **USB debugging** message appears in the headset, choose **Allow**.
3. In the Hub, set **Connection type** to **USB cable**. If the Hub says it can see the headset but has no root access, open Magisk on the Quest and allow **Shell / ADB Shell**. Then use **Refresh connection status** on the Live tracking page.

### Option B: Wireless ADB

1. Connect the Quest and PC to the same private Wi-Fi network. On **First-time setup**, choose **Wireless ADB (Wi-Fi)**.
2. Enter the Quest's Wi-Fi address in **Quest IP:port**. It usually looks like `192.168.1.25:5555`. The numbers before `:5555` are *your Quest's* address, so do not copy that example exactly. Press **Connect to Quest**.
3. A **Quest connected** pop-up means wireless ADB and Magisk root are ready. Activity will also say **Wireless Quest ready**. **Do not press Pair and connect.** You can unplug the USB cable, if one was attached. If a **Quest connection failed** pop-up appears, check the Quest's current IP and port, keep it awake, and read **Activity** for the exact error.
4. If the wireless connection is off but USB works, plug in USB once and press **Enable from USB**. After the Hub reports success, unplug USB and connect to the saved Quest address.

**Pair and connect** is only for a headset that shows a **six-digit wireless debugging pairing code**. Keep the pairing message open in the headset. Put the short-lived address shown next to the code into **Pairing IP:port**, put the code into **Six-digit code**, and keep the normal Quest address in **Quest IP:port**. The two port numbers are different. For example, `192.168.1.25:5555` is a normal connection address, **not** a pairing address. Use your Quest's actual address.

If pairing says `protocol fault`, open a new pairing message on the headset and use its new code and pairing port. If **Connect to Quest** already said ready, skip pairing. Your Quest's Wi-Fi address may change after a reboot; update it in the Hub if it does. Use wireless ADB only on a trusted network.

## 5. Press the setup buttons

On **First-time setup**, work down the three numbered cards:

1. On the **PC runtime** card, press **Install runtime** and wait for Activity to say it has finished. This prepares Qpro's private Python and downloads the PC parts needed for tracking. The Hub stays at your current place on the page while it runs. You do not need to install, repair, or remove another Python installation.
2. **Close VRCFaceTracking** and wait for its module process to exit. In the experimental test Hub, choose **Face-tracking source**, press **Install Virtual Desktop module** or **Install Steam Link module** for that source, wait for completion, then reopen VRCFaceTracking. Installing one Qpro module uninstalls the other. For the published V2.0.2 ZIP, leave the source on **Virtual Desktop** and use its existing install button.
3. If you chose the Hub's independent gaze method **instead of the Magisk module**, press **Prepare gaze** while the rooted Quest is connected. The Hub reads the stock eye model and tracking-engine details from that headset, then prepares a temporary model on the PC. You do not need to supply an engine file. Skip this button if the Magisk gaze module is enabled, or if you only want tongue or pupil tracking. Prepare gaze again after a headset firmware update.

The Hub checks the prepared model and headset build before restarting tracking. If the build is unsupported or changed, Activity explains why and the Hub leaves headset tracking alone. Use the Singularity module instructions in section 1, or keep ordinary eye tracking from your selected source. Note your Quest's **exact firmware build** when asking for help. On a supported build, applying or restoring the Hub's temporary eye model restarts Meta trackingservice. The headset may briefly look frozen and lose positional tracking; this is an expected restart, not a headset crash. Wait for Activity to confirm that tracking has returned.

To remove Qpro's VRCFaceTracking add-on later, close VRCFaceTracking, wait for its module process to exit, and press **Uninstall Qpro module** on the **VRCFT module** card. Confirm the prompt, wait for **Setup step complete**, then restart VRCFaceTracking. The Hub removes Qpro's installed source module and restores any Virtual Desktop modules this extracted copy saved during installation. It does not delete your tongue recordings or models. If ordinary Virtual Desktop face tracking is missing afterward, install its official VRCFaceTracking module again.

**AMD GPU:** After **Install runtime** succeeds, press **Install ROCm 10.0** if the Hub detects one of these discrete Radeon models on Windows 11. If an older verified ROCm 7.2.1 environment is already present, the button says **Upgrade to ROCm 10.0** instead:

| Series | Models mapped for ROCm 10.0 |
| --- | --- |
| RX 6000 | 6950 XT, 6900 XT, 6800 XT, 6800; 6750 XT, 6700 XT; 6600 XT, 6600 |
| RX 7000 | 7900 XTX, 7900 XT, 7900 GRE; 7800 XT, 7700 XT, 7700; 7600 XT, 7600 |
| RX 9000 | 9070 XT, 9070, 9070 GRE; 9060 XT, 9060 |
| Radeon PRO | AI PRO R9700, PRO W7900, PRO W7900 Dual Slot |

Qpro installs [AMD TheRock ROCm 10.0 packages](https://github.com/ROCm/TheRock/blob/main/RELEASES.md) for the detected card's [GPU target](https://rocm.docs.amd.com/en/latest/reference/gpu-specs.html). ROCm 10.0 is AMD's latest stable series in this test build, while its use in Qpro remains **experimental**. AMD's stable Windows installation guide does not explicitly list the RX 6750/6700 (gfx1031) or RX 6600 (gfx1032) device packages. Setup attempts those targets and will report a failure if a package or GPU check is unavailable. Confirm the Hub prompt and allow time for a large download. [AMD's ROCm 10.0 matrix](https://rocm.docs.amd.com/en/latest/compatibility/compatibility-matrix.html) validates Windows 11 25H2 with Adrenalin 26.8.1; other Windows 11 builds or drivers may fail the GPU checks. Qpro enables the new environment only after GPU training and model inference tests pass. Integrated graphics and unlisted Radeon models are not eligible. If setup fails, read the full **Activity** error.

The new packages use `.venv-rocm-experimental`. This folder name preserves compatibility with earlier test builds; it does not mean AMD's packages are a preview release. If this extracted Qpro copy already has a **verified** `.venv-rocm` installation on a card in [AMD's Windows ROCm 7.2.1 list](https://rocm.docs.amd.com/projects/radeon-ryzen/en/docs-7.2.1/docs/compatibility/compatibilityrad/windows/windows_compatibility.html), Qpro keeps it as a fallback when ROCm 10.0 is not ready. The Hub shows whether 10.0 is verified or the older fallback is available. An RX 7900 XTX was live-tested with the older ROCm 7.2.1 path; the ROCm 10.0 Qpro path still needs live tests on the listed cards.

**NVIDIA GPU or CPU:** skip the AMD button. NVIDIA CUDA hardware has been live-tested and is functional. When tracking starts, **Activity** tells you whether the tongue model is using AMD, NVIDIA, or CPU.

## 6. Record and train your tongue model

**Train if you have no personal model or used a version before V2.0. You can import a working V2.0 or V2.0.1 model into V2.0.2.** The bundled model was trained for someone else's face.

1. Open **Personalize** in the Hub.
2. Choose **Quick refinement** for the shorter guided session, or **Full dataset** for more thorough coverage. Press **1. Record refinement** or **1. Record full dataset**.
3. Follow each on-screen pose card. Include the side and **diagonal** tongue positions. Missing a pose can make tracking briefly disappear there.
4. When recording finishes, choose the new recording in the list. Press **2. Train personalized copy** or **2. Train new personal model**. Wait for Activity to say training finished.

If you have facial hair or wear a bandage near the mouth, keep it as you normally wear it during tracking. Record the hidden tongue and every visible pose, including several slightly different jaw and headset positions. The capture screen waits for fresh VRCFaceTracking factory packets; it does not require you to make the native face values move before recording. Training varies local image shading, but a personal recording is still needed to check whether your tongue remains visible to the cameras.

After training, **Activity** lists the weakest held-out pose cards and diagonal corners. `missed` counts visible-tongue frames that the selected visibility gate marked hidden; `fnr` is that count divided by visible frames. These are checks on held-out frames from the same recording, so try the model live as well.

The **Model manager** can export a model as a backup. Import working V2.0 or V2.0.1 exports into V2.0.2; for pre-V2.0 exports, make a new capture and train again. Camera recordings are personal data, so share them only if you want to.

To remove a recording you no longer need, stay on **Personalize**. Under the matching **Quick refinement** or **Full dataset** card, choose it from **Recorded datasets (including trained)** and press **Delete selected dataset…**. Read the confirmation before choosing **Yes**: this permanently removes the recording, its labels and session details, and its prepared training cache from this extracted copy. A tongue model already trained from that recording remains available in **Model manager**. The training dropdown above only lists datasets still waiting to be trained.

## 7. Start tracking

1. Start your selected **Virtual Desktop** or **Steam Link** app on the Quest, then SteamVR and VRCFaceTracking on the PC. Steam Link users should confirm its OSC and eye/face sharing settings above.
2. In the Hub, open **Live tracking**. Independent Eye Gaze starts off; select it, tongue tracking, pupil dilation, or the combination you want. **If you installed Singularity's Independent Eye Gaze Magisk module, leave the Hub's Independent Eye Gaze option off.** For tongue tracking, choose the **new model you just trained** under **Tongue model**. If tracking is already running, press **Stop tracking** first so the new model loads when tracking restarts.
3. Press **Start tracking**. Open **Activity** if you want to see whether the camera connected and which device is running the tongue model.
4. When you finish, press **Stop tracking**. Wait until Activity says the camera has stopped and the stock eye model has been restored.

**Preview tracking cameras** lets you hide the camera windows without turning tracking off. Change it before starting the next session. **Pupil response** makes avatar pupil changes stronger or weaker; start near the default and adjust slowly. Pupil values are estimates for avatar animation, **not** measured eye health data. Your VRChat avatar must support pupil animation for changes to appear.

**Individual cheek puff** is on **Live tracking** and starts on by default. **Strong individual (1/0)** makes a clear one-cheek puff full strength on that side and zero on the other, while a two-cheek puff still moves both. Choose **Balanced** for gentler separation, or turn **Individual cheek puff** off to use the original cheek values from Virtual Desktop or Steam Link. You can change this while tracking is running. If the VRCFaceTracking preview separates the cheeks but the avatar does not, check the avatar's parameters and blendshapes.

**Individual cheek suck** uses the separate left and right cheek-suck signals from Virtual Desktop or Steam Link. It starts on with **Strong individual (1/0)**, which emphasizes the stronger side while leaving a deliberate two-cheek suck on both sides. Choose **Balanced** for a softer effect, or turn it off to pass through the streaming app's original values. It does not use negative cheek-puff values.

**Adjust eyebrow movement** starts off, so brows use the selected source's original values. Turn it on and adjust **Eyebrow sensitivity** to amplify or soften the existing left and right inner raise, outer raise, and lower/pinch expressions. This cannot add movements the headset does not detect. The avatar needs matching brow parameters and blendshapes to show that detail. If the expressions move in VRCFaceTracking's preview but the avatar shows only a single brow motion, check the avatar's face tracking setup.

If Independent Eye Gaze fails to start or stops unexpectedly, the Hub turns it off automatically and remembers that choice. Tongue and pupil tracking continue if selected. Check **Activity** for the recovery result before trying gaze again; select **Independent Eye Gaze** manually to retry.

## Common problems

| What you see | What to try |
| --- | --- |
| “Root access unavailable” | Keep the Quest awake. In Magisk, allow **Shell / ADB Shell**, then refresh the connection. |
| **Quest connected** pop-up, followed by a pairing error | You are already connected. Skip **Pair and connect** and start tracking. |
| **Quest connection failed** pop-up | Check the Quest's current Wi-Fi IP and port, wake the headset, and read **Activity** for the specific error. |
| `protocol fault` during pairing | Use a fresh six-digit code and the **temporary pairing port** shown on the Quest, not the usual `:5555` port. |
| Wireless stopped working after a reboot | Check the Quest's current Wi-Fi address. Wireless ADB may need to be enabled again. |
| AMD installer says PC runtime is missing | Finish **Install runtime** first, then return to **Install ROCm 10.0**. |
| **Install runtime** stays on “Starting PC runtime setup” | Keep the Hub open and read **Activity**. It now shows the PowerShell launch, current setup phase, and a still-running message every 15 seconds when output stops. If PowerShell never prints its first script message, check Windows Security for a blocked `powershell.exe`, then share the Activity lines, including the PowerShell PID. |
| **Install runtime** ends with code 1 | Open **Activity** and read the first error above the exit code. If it mentions a missing DLL, `VCRUNTIME`, or a Python import failure, install or repair [Microsoft's latest x64 Visual C++ Redistributable](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170#latest-supported-redistributable-version), then reopen the Hub and run **Install runtime** again. Code 1 has other causes too, so share the full error if it repeats. Do not delete the whole `%LOCALAPPDATA%\QproFaceTracking` folder just because the exit code is 1. |
| Steam Link source selected but no face or training labels arrive | In Steam Link **Advanced Settings**, turn on **OSC**, **Share eye tracking data**, and **Share face tracking data**; set **OSC Output Port** to **9015**. Close VRCFaceTracking and wait for its module process to exit, then press **Install Steam Link module** in the Hub. Reopen VRCFaceTracking. Remove other Steam Link VRCFaceTracking modules that would use the same port. |
| Launcher says the tracking source does not match | Close VRCFaceTracking and wait for its module process to exit. Choose the intended **Face-tracking source** on **First-time setup**, install its Qpro module, reopen VRCFaceTracking, then retry. |
| AMD Ryzen integrated graphics appears during ROCm setup | Qpro checks for one of the exact discrete Radeon models in the ROCm 10.0 table above. An integrated GPU alone is not enough. Check that Windows and the AMD driver can see the discrete card; otherwise use **Install runtime** for NVIDIA CUDA or CPU. |
| Independent gaze or convergence fails but ordinary face tracking works | Check the Quest's exact firmware build. Follow the [Singularity Magisk setup in section 1](#1-set-up-eye-tracking-and-independent-gaze-first); it has been reported working on Horizon OS v2.7. |
| Tongue disappears in some positions | Check that your **newly trained model** actually loaded in Activity. Record those positions again and retrain. |
| Facial hair or a bandage makes tongue training unreliable | Record a personal dataset with the same face appearance you use for tracking. Capture hidden and visible tongue poses, then check the trained model in Live tracking. If native visibility flickers while the cameras still see your tongue, try **Camera only** under **Visibility**. This may help but is not guaranteed for every face or headset fit. |
| Pupils seem too jumpy | Lower **Pupil response** and hold your gaze steady while tracking warms up. |

If Windows will not let you delete an older extracted Qpro folder, close its Hub and camera preview first. If the folder is still in use, open **Task Manager > Details**, end a leftover `adb.exe` process, then try again. Ending `adb.exe` temporarily disconnects any other Android tools using that ADB server.

If you need help, copy the relevant lines from **Activity** and tell us your Quest firmware version, whether you used USB or wireless, and which release ZIP you downloaded. You can ask in the [community Discord](https://discord.gg/ghvuJTpRu4). The server is not owned by Fwooffy.

The experimental Steam Link source follows the OSC mapping documented by [danwillm](https://github.com/danwillm/VRCFT-SteamLink) and the [LinkFT project](https://github.com/ykeara/LinkFT). Qpro does not require either third-party module alongside its own module.
