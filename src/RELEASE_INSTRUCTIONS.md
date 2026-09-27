# QproFaceTracking V2.0: text setup guide

> **Before you start: you need a rooted Meta Quest Pro and the latest VRCFaceTracking from Steam.** This program will not work on an unrooted headset or a different Quest model. If you still need to root your Quest Pro, use [Fwooffy and glorpette's beginner root guide](https://github.com/glorpette/quest-guides/blob/main/root_guide_by_fwooffy.md). Check that your headset's exact software version is supported by [Singularity](https://github.com/Lumince/singularity) before following a root guide. [Install or update VRCFaceTracking through Steam](https://store.steampowered.com/app/3329480/VRCFaceTracking/) before using Qpro's bridge.
>
> **If you used an older QproFaceTracking version, record and train your tongue model again in this version.** Do not import an old tongue model and assume it will work correctly. Keep the old model as a backup, but make a new capture and train a new model before using tongue tracking. The included developer model is only a starting point.

Start with the headset's eye tracking, then set up the PC software. The **Hub** is the `QproFaceTracking.exe` program. **ADB** is the connection it uses to talk to your Quest. The **bridge** sends Qpro's tracking results to VRCFaceTracking.

## 1. Set up eye tracking and independent gaze first

1. On the rooted Quest Pro, open **Settings > Movement Settings**. Turn on **Eye Tracking** and **Natural Facial Expressions**, then run eye calibration if offered. If these are already enabled, leave them on. Keep Developer Mode enabled.
2. If **Magisk OverlayFS** is not installed yet, open **Singularity > Apps/Modules > Magisk repo** and install it. Follow any reboot prompts. If it is already enabled in Magisk, you can skip reinstalling it.
3. In the same Singularity menu, install **Quest Pro Independent Eye Gaze** if it is not installed yet. Reboot if prompted, then check in Magisk that both modules are enabled. This is the headset-side independent gaze route.
4. Once Virtual Desktop and VRCFaceTracking are installed in section 3, check that left and right gaze and convergence move correctly. The Magisk route has been reported working on **Horizon OS v2.7**, but exact firmware builds can behave differently.

**Use one independent gaze method at a time.** While the Magisk module is active, leave **Independent Eye Gaze** unchecked in the Hub and skip **Prepare gaze** in section 5. If you want to try the Hub's temporary gaze method instead, disable the Magisk gaze module and reboot first. **Stop tracking** only reverses the Hub's changes; it does not disable a Magisk module.

## 2. Download and open the Hub

1. Download the QproFaceTracking **ZIP file** from [this project's Releases page](https://github.com/Fwooffy/Qpro-Enhanced-FT-Wireless/releases). Choose the named QproFaceTracking ZIP, **not** GitHub's “Source code” ZIP.
2. In Windows File Explorer, right-click the ZIP and choose **Extract All**. Open the extracted folder. Keep `QproFaceTracking.exe`, `Helpers`, and `QproRuntime` together.
3. Double-click `QproFaceTracking.exe`. It opens on **First-time setup** the first time. You can return to that page from the menu on the left.

You need an internet connection and several gigabytes of free space for setup. The ZIP already includes ADB. The Hub installs the Python parts it needs, so you do not have to install ADB or Python separately for this program.

## 3. Get the PC apps ready

If Magisk asks whether **Shell / ADB Shell** may have root access, allow it. Keep the Quest awake while setting up the Hub.

For VRChat tracking, install [Virtual Desktop and its PC Streamer](https://www.vrdesktop.net/), [SteamVR](https://store.steampowered.com/app/250820/SteamVR/), and the [latest VRCFaceTracking from Steam](https://store.steampowered.com/app/3329480/VRCFaceTracking/). **The current Steam VRCFaceTracking is required.** Let Steam finish updating it before installing Qpro's bridge. Try ordinary face and blink tracking in VRCFaceTracking first. If that does not work, fix it before turning on Qpro's extra features.

If you want to stream the **Virtual Desktop picture over a USB cable**, Fwooffy's working setup used the Virtual Desktop **Beta** channel. In the Meta Horizon phone app or the headset's app library, long-press Virtual Desktop, open **Settings > Release Channels**, choose **Beta**, and update it. This changes VR video streaming. The Hub's USB or wireless choice below controls **tracking data**, which is separate.

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

1. On the **PC runtime** card, press **Install runtime** and wait for Activity to say it has finished. This downloads the PC parts needed for tracking. The Hub stays at your current place on the page while it runs. An existing Python 3.12 installation does **not** need to be removed.
2. **Close VRCFaceTracking**, press **Install bridge**, wait for it to finish, and then reopen VRCFaceTracking.
3. If you chose the Hub's independent gaze method **instead of the Magisk module**, press **Prepare gaze** while the rooted Quest is connected. The Hub prepares a temporary eye model from your own headset. Skip this button if the Magisk gaze module is enabled, or if you only want tongue or pupil tracking.

The Hub's gaze method can fail on some Quest Pro firmware builds even when ordinary face, tongue, and pupil tracking work. If it fails, use the Singularity module instructions in section 1, or keep ordinary Virtual Desktop eye tracking. Note your Quest's **exact firmware build** when asking for help. Applying or restoring the Hub's temporary eye model restarts Meta trackingservice. The headset may briefly look frozen and lose positional tracking; this is an expected restart, not a headset crash. Wait for Activity to confirm that tracking has returned.

To remove Qpro's VRCFaceTracking add-on later, close VRCFaceTracking and press **Uninstall bridge** on the **VRCFT bridge** card. Confirm the prompt, wait for **Setup step complete**, then restart VRCFaceTracking. The Hub removes only Qpro's bridge and restores any Virtual Desktop modules this extracted copy saved during installation. It does not delete your tongue recordings or models. If ordinary Virtual Desktop face tracking is missing afterward, install its official VRCFaceTracking module again.

**AMD GPU:** After **Install runtime** succeeds, check the [AMD GPU support list linked in the Hub](https://rocm.docs.amd.com/projects/radeon-ryzen/en/docs-7.2.1/docs/compatibility/compatibilityrad/windows/windows_compatibility.html). If your GPU is supported, press **Install AMD ROCm** and wait for its checks to pass. This adds GPU support for tongue tracking and training. **NVIDIA GPU or CPU:** skip the AMD button. When tracking starts, Activity tells you whether the tongue model is using AMD, NVIDIA, or CPU. AMD has been tested on an RX 7900 XTX, and NVIDIA CUDA hardware has also been live-tested and is functional.

## 6. Record and train your tongue model

**Do this again even if you trained a model in an older version. Do not use Import as a shortcut for this step.** The bundled developer model can show that tracking starts, but it was trained for someone else's face.

1. Open **Personalize** in the Hub.
2. Choose **Quick refinement** for the shorter guided session, or **Full dataset** for more thorough coverage. Press **1. Record refinement** or **1. Record full dataset**.
3. Follow each on-screen pose card. Include the side and **diagonal** tongue positions. Missing a pose can make tracking briefly disappear there.
4. When recording finishes, choose the new recording in the list. Press **2. Train personalized copy** or **2. Train new personal model**. Wait for Activity to say training finished.

The **Model manager** can export your *new* model as a backup. Old `.qptonguemodel` files should be kept separately; they do not replace training again in this version. Camera recordings are personal data, so share them only if you want to.

To remove a recording you no longer need, stay on **Personalize**. Under the matching **Quick refinement** or **Full dataset** card, choose it from **Recorded datasets (including trained)** and press **Delete selected dataset…**. Read the confirmation before choosing **Yes**: this permanently removes the recording, its labels and session details, and its prepared training cache from this extracted copy. A tongue model already trained from that recording remains available in **Model manager**. The training dropdown above only lists datasets still waiting to be trained.

## 7. Start tracking

1. Start Virtual Desktop on the Quest, then SteamVR and VRCFaceTracking on the PC.
2. In the Hub, open **Live tracking**. Select Independent Eye Gaze, tongue tracking, pupil dilation, or the combination you want. **If you installed Singularity's Independent Eye Gaze Magisk module, leave the Hub's Independent Eye Gaze option off.** For tongue tracking, choose the **new model you just trained** under **Tongue model**. If tracking is already running, press **Stop tracking** first so the new model loads when tracking restarts.
3. Press **Start tracking**. Open **Activity** if you want to see whether the camera connected and which device is running the tongue model.
4. When you finish, press **Stop tracking**. Wait until Activity says the camera has stopped and the stock eye model has been restored.

**Preview tracking cameras** lets you hide the camera windows without turning tracking off. Change it before starting the next session. **Pupil response** makes avatar pupil changes stronger or weaker; start near the default and adjust slowly. Pupil values are estimates for avatar animation, **not** measured eye health data. Your VRChat avatar must support pupil animation for changes to appear.

If Independent Eye Gaze fails to start or stops unexpectedly, the Hub turns it off automatically and remembers that choice. Tongue and pupil tracking continue if selected. Check **Activity** for the recovery result before trying gaze again; select **Independent Eye Gaze** manually to retry.

## Common problems

| What you see | What to try |
| --- | --- |
| “Root access unavailable” | Keep the Quest awake. In Magisk, allow **Shell / ADB Shell**, then refresh the connection. |
| **Quest connected** pop-up, followed by a pairing error | You are already connected. Skip **Pair and connect** and start tracking. |
| **Quest connection failed** pop-up | Check the Quest's current Wi-Fi IP and port, wake the headset, and read **Activity** for the specific error. |
| `protocol fault` during pairing | Use a fresh six-digit code and the **temporary pairing port** shown on the Quest, not the usual `:5555` port. |
| Wireless stopped working after a reboot | Check the Quest's current Wi-Fi address. Wireless ADB may need to be enabled again. |
| AMD installer says PC runtime is missing | Finish **Install runtime** first, then return to **Install AMD ROCm**. |
| Independent gaze or convergence fails but ordinary face tracking works | Check the Quest's exact firmware build. Follow the [Singularity Magisk setup in section 1](#1-set-up-eye-tracking-and-independent-gaze-first); it has been reported working on Horizon OS v2.7. |
| Tongue disappears in some positions | Check that your **newly trained model** actually loaded in Activity. Record those positions again and retrain. |
| Pupils seem too jumpy | Lower **Pupil response** and hold your gaze steady while tracking warms up. |

If Windows will not let you delete an older extracted Qpro folder, close its Hub and camera preview first. If the folder is still in use, open **Task Manager > Details**, end a leftover `adb.exe` process, then try again. Ending `adb.exe` temporarily disconnects any other Android tools using that ADB server.

If you need help, copy the relevant lines from **Activity** and tell us your Quest firmware version, whether you used USB or wireless, and which release ZIP you downloaded. You can ask in the [community Discord](https://discord.gg/ghvuJTpRu4). The server is not owned by Fwooffy.
