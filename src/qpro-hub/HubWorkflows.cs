using System.Diagnostics;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Media;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace QproFaceTracking.Hub;

internal sealed partial class HubForm
{
    private void ReloadProfiles()
    {
        ImportAdjacentPersonalModels();
        var selectedEye = (_eyeProfiles.SelectedItem as FileChoice)?.Primary;
        var selectedTongue = (_tongueModels.SelectedItem as FileChoice)?.Primary;
        var selectedManaged = (_modelList.SelectedItem as FileChoice)?.Primary;
        _eyeProfiles.Items.Clear();
        var calibrationDir = Path.Combine(_root, "calibration");
        if (Directory.Exists(calibrationDir))
        {
            foreach (var path in Directory.GetFiles(calibrationDir, "qpro-independent-visual-axis-v*.json")
                         .OrderByDescending(VersionFromPath))
            {
                var version = VersionFromPath(path);
                _eyeProfiles.Items.Add(new FileChoice(version == 2 ? "Developer mapping v2 (current)" : $"Visual-axis mapping v{version}", path));
            }
        }
        SelectOrFirst(_eyeProfiles, selectedEye);

        _tongueModels.Items.Clear();
        _modelList.Items.Clear();
        var modelsDir = Path.Combine(_root, "models");
        if (Directory.Exists(modelsDir))
        {
            foreach (var gate in Directory.GetFiles(modelsDir, "qpro-stereo-tongue-v*-gate.pt").OrderByDescending(VersionFromPath))
            {
                var version = VersionFromPath(gate);
                var direction = Path.Combine(modelsDir, $"qpro-stereo-tongue-v{version}-direction.pt");
                if (File.Exists(direction))
                {
                    var choice = new FileChoice(ModelDisplayName(version), gate, direction);
                    _tongueModels.Items.Add(choice);
                    _modelList.Items.Add(choice);
                }
            }
        }
        SelectOrFirst(_tongueModels, selectedTongue);
        SelectOrFirst(_modelList, selectedManaged);
        _modelEmpty.Visible = _modelList.Items.Count == 0;
        if (_modelEmpty.Visible) _modelEmpty.BringToFront();
        UpdateTongueModelNote();
        ReloadDatasetQueues();
        UpdateControlState();
    }

    private void ImportAdjacentPersonalModels()
    {
        if (_adjacentModelsImported) return;
        _adjacentModelsImported = true;

        var releaseParent = Directory.GetParent(_root);
        if (releaseParent is null || !releaseParent.Name.Equals("dist", StringComparison.OrdinalIgnoreCase)) return;

        var destination = Path.Combine(_root, "models");
        Directory.CreateDirectory(destination);
        var imported = new List<int>();
        foreach (var sibling in releaseParent.GetDirectories("QproFaceTracking-*").OrderByDescending(directory => directory.LastWriteTimeUtc))
        {
            if (string.Equals(sibling.FullName.TrimEnd(Path.DirectorySeparatorChar), _root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) continue;
            var source = Path.Combine(sibling.FullName, "models");
            if (!Directory.Exists(source)) continue;
            foreach (var gate in Directory.GetFiles(source, "qpro-stereo-tongue-v*-gate.pt").OrderByDescending(VersionFromPath))
            {
                var version = VersionFromPath(gate);
                if (version <= 0 || version == 8) continue;
                var sourceDirection = Path.Combine(source, $"qpro-stereo-tongue-v{version}-direction.pt");
                var destinationGate = Path.Combine(destination, Path.GetFileName(gate));
                var destinationDirection = Path.Combine(destination, Path.GetFileName(sourceDirection));
                if (!File.Exists(sourceDirection) || File.Exists(destinationGate) || File.Exists(destinationDirection)) continue;

                File.Copy(gate, destinationGate, false);
                File.Copy(sourceDirection, destinationDirection, false);
                foreach (var companion in Directory.GetFiles(source, $"qpro-stereo-tongue-v{version}.*")
                             .Concat(Directory.GetFiles(source, $"qpro-stereo-tongue-v{version}-*.torchscript.pt")))
                {
                    var target = Path.Combine(destination, Path.GetFileName(companion));
                    if (!File.Exists(target)) File.Copy(companion, target, false);
                }
                imported.Add(version);
            }
        }
        if (imported.Count > 0)
            AppendLog($"Carried personal tongue model{(imported.Count == 1 ? string.Empty : "s")} v{string.Join(", v", imported.Distinct().Order())} forward from the previous release.");
    }

    private void UpdateTongueModelNote()
    {
        var model = _tongueModels.SelectedItem as FileChoice;
        var version = model is null ? 0 : VersionFromPath(model.Primary);
        _tongueModelNote.Text = version == 8
            ? "Trained only on the developer. It is suitable for a first demo; quick refinement is recommended for another wearer."
            : model is null
                ? "No complete gate/direction model pair was found."
                : "Personal model discovered in this release folder. The bundled developer v8 remains unchanged.";
    }

    private async Task ConfirmCaptureAsync(bool quick)
    {
        var missing = new List<string>();
        if (FindAdb() is null) missing.Add("re-extract the release; bundled platform-tools\\adb.exe is missing");
        else if (!await HasQuestAsync()) missing.Add("connect and authorize the rooted Quest Pro over USB or wireless ADB");
        if (!Process.GetProcessesByName("vrserver").Any()) missing.Add("start SteamVR");
        if (!Process.GetProcessesByName("VRCFaceTracking").Any()) missing.Add("start VRCFaceTracking and confirm Virtual Desktop face tracking is flowing");
        if (!BackendReady()) missing.Add("run First-time setup: Set up PC runtime");
        if (missing.Count > 0)
        {
            MessageBox.Show(
                this,
                "Before recording:\n\n• " + string.Join("\n• ", missing) +
                "\n\nThe current trainer uses Virtual Desktop's native TongueOut confidence as a reference label, so SteamVR and VRCFaceTracking are required during capture.",
                "Capture is not ready",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        var title = quick ? "Start quick tongue refinement?" : "Start full tongue capture?";
        var estimate = quick ? "about 10–20 minutes" : "about 45–90 minutes";
        var purpose = quick
            ? "This creates a correction dataset that fine-tunes a new copy of the developer model. It is faster, but cannot replace the breadth of a full personal dataset."
            : "This records a much broader personal dataset and is the best-quality option, but it requires many carefully held poses.";
        var choice = MessageBox.Show(
            this,
            $"Estimated capture time: {estimate}.\n\n{purpose}\n\nA guided camera window will open. Press Q at any point to stop safely. Existing captures and models will not be overwritten.\n\nStart now?",
            title,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (choice != DialogResult.Yes) return;
        if (_datasetOperationBusy) return;
        _datasetOperationBusy = true;
        try
        {
            var started = DateTime.UtcNow;
            var succeeded = await RunUtilityAsync(
                quick ? "Quick refinement capture" : "Full tongue capture",
                "build-and-run.ps1",
                quick ? "-TongueRefinementCalibration" : "-TongueStillCalibration");
            if (!succeeded) return;
            var dataset = FindLatestDataset(quick, requireCompleted: false, newerThan: started.AddSeconds(-3));
            if (dataset is null || dataset.SampleCount == 0) return;
            var proposed = dataset.DisplayName.StartsWith("Dataset ", StringComparison.Ordinal)
                ? (quick ? "My tongue refinement" : "My full tongue dataset")
                : dataset.DisplayName;
            var name = PromptForText(
                "Name this dataset",
                "Give this capture a friendly name so you can identify the model trained from it later.",
                proposed);
            if (name is not null)
            {
                SetDatasetDisplayName(dataset.SessionPath, name);
                AppendLog($"Dataset saved as “{name}”.");
            }
            ReloadDatasetQueues();
        }
        finally { _datasetOperationBusy = false; }
    }

    private async Task RunSetupStepAsync(string label, string script, string completed, string next)
    {
        BeginSetupProgress(label);
        var succeeded = await RunUtilityAsync(label, script);
        if (script.Equals("Install-QproRocm.ps1", StringComparison.OrdinalIgnoreCase))
            _rocmInstallRunning = false;
        FinishSetupProgress(succeeded, label);
        if (!succeeded) return;
        UpdateSetupStepStyles();
        PlaySfx("succeed.wav");
        MessageBox.Show(this, completed + "\n\n" + next, "Setup step complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task PrepareGazeAsync()
    {
        var adb = FindAdb();
        if (adb is null)
        {
            PlaySfx("warning.wav");
            MessageBox.Show(
                this,
                "The bundled Android tools could not be found.\n\nRe-extract the complete QproFaceTracking release and confirm that platform-tools\\adb.exe is present, then try Prepare gaze again.",
                "Android tools are missing",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var connection = await RunAdbProbeAsync(adb, ["devices"]);
        var deviceLines = connection.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        var connected = await HasQuestAsync();
        if (!connection.Completed || !connected)
        {
            var stateHint = deviceLines.Any(line => line.Contains("\tunauthorized", StringComparison.OrdinalIgnoreCase))
                ? "The headset is listed as unauthorized. Put it on and accept the debugging authorization prompt."
                : deviceLines.Any(line => line.Contains("\toffline", StringComparison.OrdinalIgnoreCase))
                    ? "The headset is listed as offline. Reconnect wireless ADB or the USB cable and try again."
                    : "No authorized headset was found over ADB.";
            PlaySfx("warning.wav");
            MessageBox.Show(
                this,
                stateHint + "\n\nFor wireless use, run Connect-QproWireless.cmd and then Launch-QproWireless.cmd. Confirm the headset is awake and Magisk Shell access is granted. USB is also supported. Then press Prepare gaze again.",
                "Quest Pro not found over ADB",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var target = GetConfiguredAdbTarget();
        var rootArguments = string.IsNullOrWhiteSpace(target)
            ? new[] { "shell", "su", "-c", "id" }
            : new[] { "-s", target, "shell", "su", "-c", "id" };
        var root = await RunAdbProbeAsync(adb, rootArguments, 8);
        if (!root.Completed || root.ExitCode != 0 || !root.Output.Contains("uid=0", StringComparison.OrdinalIgnoreCase))
        {
            PlaySfx("warning.wav");
            MessageBox.Show(
                this,
                "ADB can see your Quest Pro, but root access was not granted.\n\nIndependent gaze requires a rooted headset. Confirm that the headset is rooted, then open Magisk and grant Superuser access to Shell / ADB Shell (com.android.shell). Keep the headset awake and try Prepare gaze again.",
                "Quest Pro root access is unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        await RunSetupStepAsync(
            "Local gaze preparation",
            "prepare-eye-model.ps1",
            "Independent-gaze support is prepared.",
            "First-time setup is complete. Start Virtual Desktop, SteamVR, and VRCFaceTracking before applying tracking.");
    }

    private void BeginSetupProgress(string label)
    {
        // The clicked setup button is about to be disabled. Leave keyboard focus
        // on the form so WinForms does not focus a later control and scroll there.
        ActiveControl = null;
        _setupActionRunning = true;
        _setupProgressContainer.Visible = true;
        _setupProgress.IsIndeterminate = true;
        _setupProgress.Value = 0;
        _setupProgressStatus.Text = label.Equals("PC runtime setup", StringComparison.OrdinalIgnoreCase)
            ? "Installing and verifying the private PC runtime… This can take several minutes."
            : label + " is running… Please keep this window open.";
        _setupProgressStatus.ForeColor = Warning;
        SetSetupButtonsEnabled(false);
    }

    private void FinishSetupProgress(bool succeeded, string label)
    {
        _setupActionRunning = false;
        _setupProgress.IsIndeterminate = false;
        _setupProgress.Value = succeeded ? 100 : 0;
        _setupProgressStatus.Text = succeeded ? label + " completed successfully." : label + " did not complete. See Activity for details.";
        _setupProgressStatus.ForeColor = succeeded ? Good : Bad;
        SetSetupButtonsEnabled(true);
    }

    private void SetSetupButtonsEnabled(bool enabled)
    {
        enabled &= !_setupActionRunning;
        _setupRuntimeButton.Enabled = enabled;
        _setupBridgeButton.Enabled = enabled;
        _uninstallBridgeButton.Enabled = enabled && BridgeUninstallAvailable();
        _setupGazeButton.Enabled = enabled;
        _enableWirelessButton.Enabled = enabled;
        _connectWirelessButton.Enabled = enabled;
        _pairWirelessButton.Enabled = enabled;
        _disableWirelessButton.Enabled = enabled;
        _setupAmdButton.Enabled = enabled && AmdInstallEligible && BackendReady();
    }

    private async Task TrainTongueAsync(bool quick)
    {
        var queue = quick ? _quickDatasets : _fullDatasets;
        var dataset = (queue.SelectedItem as DatasetChoice)?.Dataset;
        if (dataset is null || dataset.SampleCount == 0 || !dataset.Completed)
        {
            MessageBox.Show(
                this,
                "You did not capture any completed data yet! Capture first to train.",
                "No training data",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        if (_datasetOperationBusy) return;
        _datasetOperationBusy = true;
        try
        {
            BeginTrainingProgress(quick);
            var versionsBefore = TongueModelVersions().ToHashSet();
            var succeeded = await RunUtilityAsync(
                quick ? "Personal refinement training" : "Full tongue training",
                quick ? "train-latest-tongue-refinement.ps1" : "train-latest-tongue-stills.ps1",
                "-SessionPath", dataset.SessionPath);
            if (!succeeded) { FinishTrainingProgress(false); return; }
            var created = TongueModelVersions().Where(version => !versionsBefore.Contains(version)).OrderDescending().FirstOrDefault();
            if (created > 0)
            {
                WriteModelMetadata(created, dataset.DisplayName, dataset.SessionPath, quick ? "quick refinement" : "full personal dataset");
                AppendLog($"Model v{created} named “{dataset.DisplayName}”.");
            }
            ReloadProfiles();
            if (created > 0)
            {
                var trainedGate = Path.Combine(_root, "models", $"qpro-stereo-tongue-v{created}-gate.pt");
                for (var index = 0; index < _tongueModels.Items.Count; index++)
                {
                    if (_tongueModels.Items[index] is FileChoice choice &&
                        string.Equals(choice.Primary, trainedGate, StringComparison.OrdinalIgnoreCase))
                    {
                        _tongueModels.SelectedIndex = index;
                        AppendLog($"Selected new tongue model v{created} for the next tracking session.");
                        break;
                    }
                }
            }
            FinishTrainingProgress(created > 0);
            if (created > 0)
            {
                PlaySfx("trainingComplete.wav");
                MessageBox.Show(this, $"Training is complete. “{dataset.DisplayName}” is now available as tongue model v{created}.", "Tongue model ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        finally { _datasetOperationBusy = false; }
    }

    private async Task StartTrackingAsync()
    {
        if (_starting || _stopping) return;
        _trackingProcesses.RemoveAll(p => p.HasExited);
        if (_trackingProcesses.Any(p => !p.HasExited)) { MessageBox.Show(this, "Tracking is already running."); return; }
        if (!_gaze.Checked && !_tongue.Checked && !_pupil.Checked) { PlaySfx("warning.wav"); MessageBox.Show(this, "Select at least one tracking feature."); return; }
        var missing = new List<string>();
        if (FindAdb() is null) missing.Add("the bundled Android tools — re-extract the complete release");
        else if (!await HasQuestAsync()) missing.Add(_environment.WirelessSelected
            ? "an authorized wireless Quest — use First-time setup to connect or pair it"
            : "an authorized Quest over USB — connect the cable and approve debugging");
        if (!Process.GetProcessesByName("vrserver").Any()) missing.Add("SteamVR");
        if (!Process.GetProcessesByName("VRCFaceTracking").Any()) missing.Add("VRCFaceTracking");
        if (!BridgeInstalled()) missing.Add("the combined Qpro VRCFT bridge — use First-time setup step 2");
        else if (_pupil.Checked && !PupilBridgeInstalled()) missing.Add("the updated pupil-capable Qpro bridge — close VRCFaceTracking, use First-time setup step 2, then restart it");
        if (!BackendReady()) missing.Add("the PC runtime — use First-time setup step 1");
        if (_gaze.Checked && !EyeModelReady()) missing.Add("the locally prepared gaze patch — use First-time setup step 3: Prepare independent gaze");
        if (_gaze.Checked && _eyeProfiles.SelectedItem is null) missing.Add("an eye profile");
        if (_tongue.Checked && _tongueModels.SelectedItem is null) missing.Add("a paired tongue model");
        if (_pupil.Checked && !File.Exists(Path.Combine(_root, "pupil_dilation.py"))) missing.Add("the pupil estimation script — re-extract the complete release");
        if (missing.Count > 0) { PlaySfx("warning.wav"); MessageBox.Show(this, "Before starting tracking, start or provide:\n\n• " + string.Join("\n• ", missing), "Not ready"); return; }

        _starting = true;
        _gazeFailureHandled = false;
        File.Delete(_stopFile);
        _start.Enabled = false; _stop.Enabled = true;
        _runStatus.Text = "● Starting…"; _runStatus.ForeColor = Warning;
        SetStatus(_inferenceStatus, StatusKind.Warning, _tongue.Checked ? "Detecting…" : "Idle");
        SetStatus(_pupilStatus, StatusKind.Warning, _pupil.Checked ? "CPU starting…" : "Idle");
        var startCancellation = new CancellationTokenSource();
        _startCancellation = startCancellation;
        try
        {
            if (_gaze.Checked)
            {
                _gazeStartupInProgress = true;
                Process? gazeProcess = null;
                try
                {
                    var eye = (FileChoice)_eyeProfiles.SelectedItem!;
                    _runStatus.Text = "● Applying gaze model — headset tracking may pause briefly";
                    AppendLog("Independent gaze loads a temporary eye model by restarting Meta trackingservice. A brief headset pose pause is expected during apply and restore.");
                    gazeProcess = StartManaged("Independent gaze", "native-eye-local-branch-test.ps1", "-RuntimePreview", "-VrcftOutput", "-CalibrationOutput", eye.Primary, "-StopFile", _stopFile);
                    AppendLog("Waiting for Meta trackingservice to return before starting cameras…");
                    await Task.Delay(7000, startCancellation.Token);
                    if (gazeProcess.HasExited) throw new InvalidOperationException("The independent-gaze process exited during startup. See Activity.");
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    await DisableFailedGazeAsync(error.Message);
                }
                finally { _gazeStartupInProgress = false; }
                if (!_gazeFailureHandled && gazeProcess?.HasExited == true && !startCancellation.IsCancellationRequested)
                    await DisableFailedGazeAsync("The independent-gaze process exited during startup. See Activity.");
            }
            startCancellation.Token.ThrowIfCancellationRequested();
            if (_tongue.Checked)
            {
                var model = (FileChoice)_tongueModels.SelectedItem!;
                AppendLog($"Tongue model selected for this session: v{VersionFromPath(model.Primary)} (gate: {Path.GetFileName(model.Primary)}; direction: {Path.GetFileName(model.Secondary)}).");
                var args = new List<string> { "-TonguePreview", "-EnableTongueOutput", "-MaxFps", (_fps.SelectedItem?.ToString() ?? "24"), "-TongueSmoothing", _smoothing.Value.ToString(), "-TongueVisibilityMode", VisibilityModeValue(), "-TongueModelPath", model.Primary, "-TongueDirectionModelPath", model.Secondary! };
                if (!_cameraPreview.Checked) args.Add("-NoWindow");
                if (_pupil.Checked) args.AddRange(["-PupilOutput", "-PupilSensitivity", PupilSensitivityValue()]);
                args.AddRange(["-StopFile", _stopFile]);
                StartManaged("Camera tracking", "build-and-run.ps1", args.ToArray());
            }
            else if (_pupil.Checked)
            {
                var args = new List<string> { "-PupilOutput", "-PupilSensitivity", PupilSensitivityValue(), "-MaxFps", (_fps.SelectedItem?.ToString() ?? "24"), "-StopFile", _stopFile };
                if (!_cameraPreview.Checked) args.Add("-NoWindow");
                StartManaged("Pupil tracking", "build-and-run.ps1", args.ToArray());
            }
            if (!_gazeFailureHandled)
            {
                _runStatus.Text = "● Selected overrides active";
                _runStatus.ForeColor = Good;
            }
            UpdateControlState();
        }
        catch (OperationCanceledException) when (startCancellation.IsCancellationRequested)
        {
            AppendLog("Tracking startup cancelled by Stop tracking.");
        }
        catch (Exception error)
        {
            AppendLog("START FAILED: " + error.Message);
            await StopTrackingAsync();
            PlaySfx("warning.wav");
            MessageBox.Show(this, error.Message, "Tracking did not start");
        }
        finally
        {
            _starting = false;
            if (ReferenceEquals(_startCancellation, startCancellation)) _startCancellation = null;
            startCancellation.Dispose();
            UpdateControlState();
        }
    }

    private async Task StopTrackingAsync()
    {
        if (_stopping) return;
        _stopping = true;
        _startCancellation?.Cancel();
        _runStatus.Text = "● Stopping cleanly…"; _runStatus.ForeColor = Warning;
        try
        {
            File.WriteAllText(_stopFile, DateTimeOffset.Now.ToString("O"));
            var deadline = DateTime.UtcNow.AddSeconds(18);
            while (_trackingProcesses.Any(p => !p.HasExited) && DateTime.UtcNow < deadline)
                await Task.Delay(250);
            if (_trackingProcesses.Any(p => !p.HasExited))
                AppendLog("Tracking cleanup is still running. The stop request remains active; press Stop tracking again if needed, or Q if the preview is still open.");
            else
            {
                File.Delete(_stopFile);
                AppendLog("All selected overrides stopped; stock tracking restored.");
            }
        }
        finally
        {
            _trackingProcesses.RemoveAll(p => p.HasExited);
            _stopping = false; _start.Enabled = true; _stop.Enabled = false;
            _runStatus.Text = _trackingProcesses.Count == 0 ? "● Idle — stock tracking is untouched" : "● Waiting for tracking cleanup";
            _runStatus.ForeColor = _trackingProcesses.Count == 0 ? Good : Warning;
            if (_trackingProcesses.Count == 0) ResetInferenceStatus();
            UpdateControlState();
        }
    }

    private Process StartManaged(string label, string script, params string[] arguments)
    {
        var start = PowerShellStart(script, arguments, hidden: true);
        start.RedirectStandardOutput = true; start.RedirectStandardError = true;
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { AppendLog($"[{label}] {e.Data}"); HandleInferenceStatus(label, e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { AppendLog($"[{label}] {e.Data}"); HandleInferenceStatus(label, e.Data); } };
        process.Exited += (_, _) =>
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try
            {
                BeginInvoke(() =>
                {
                    if (IsDisposed || Disposing) return;
                    AppendLog($"[{label}] exited with code {process.ExitCode}.");
                    if (label == "Independent gaze" && !_stopping)
                    {
                        if (!_gazeStartupInProgress && !_gazeFailureHandled)
                            _ = DisableFailedGazeAsync($"The independent-gaze process stopped with code {process.ExitCode}.");
                        UpdateControlState();
                        return;
                    }
                    if (!_stopping)
                    {
                        _runStatus.Text = $"● {label} stopped — check Activity";
                        _runStatus.ForeColor = Warning;
                        if (label == "Camera tracking") SetStatus(_inferenceStatus, StatusKind.Warning, "Stopped");
                        if (_pupil.Checked && label is ("Camera tracking" or "Pupil tracking")) SetStatus(_pupilStatus, StatusKind.Warning, "Stopped");
                    }
                    UpdateControlState();
                });
            }
            catch (InvalidOperationException) { /* The window closed during process exit. */ }
        };
        if (!process.Start()) throw new InvalidOperationException($"Could not start {label}.");
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        _trackingProcesses.Add(process);
        AppendLog($"Starting {label}…");
        return process;
    }

    private async Task DisableFailedGazeAsync(string reason)
    {
        if (_gazeFailureHandled || _stopping || IsDisposed || Disposing) return;
        _gazeFailureHandled = true;
        _gaze.Checked = false;
        AppendLog($"Independent Eye Gaze disabled automatically: {reason}");
        AppendLog("Tongue and pupil tracking will continue if selected. Checking that the temporary eye model was removed…");
        _runStatus.Text = "● Independent gaze disabled — checking stock eye model";
        _runStatus.ForeColor = Warning;
        UpdateControlState();
        try
        {
            var start = PowerShellStart("native-eye-local-branch-test.ps1", ["-RestoreIfActive"], hidden: true);
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            using var process = new Process { StartInfo = start };
            if (!process.Start()) throw new InvalidOperationException("The stock eye-model recovery check could not start.");
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* Process already ended. */ }
                throw new TimeoutException("The stock eye-model recovery check timed out.");
            }
            foreach (var line in (await output + Environment.NewLine + await errors).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                AppendLog("[Independent gaze recovery] " + line);
            if (process.ExitCode != 0) throw new InvalidOperationException($"Stock eye-model recovery failed with code {process.ExitCode}.");
            if (_stopping || IsDisposed || Disposing || (_starting && _startCancellation?.IsCancellationRequested == true)) return;
            _runStatus.Text = _trackingProcesses.Any(p => !p.HasExited)
                ? "● Independent gaze disabled — other tracking continues"
                : "● Independent gaze disabled — stock eye model checked";
        }
        catch (Exception error)
        {
            AppendLog("Independent gaze recovery needs attention: " + error.Message);
            if (_stopping || IsDisposed || Disposing || (_starting && _startCancellation?.IsCancellationRequested == true)) return;
            _runStatus.Text = "● Independent gaze disabled — check eye-model recovery in Activity";
        }
        _runStatus.ForeColor = Warning;
        UpdateControlState();
    }

    private void ResetInferenceStatus()
    {
        SetStatus(_inferenceStatus, StatusKind.Warning, "Idle");
        SetStatus(_pupilStatus, StatusKind.Warning, "Idle");
    }

    private void HandleInferenceStatus(string label, string line)
    {
        if (label is not ("Camera tracking" or "Pupil tracking")) return;
        if (InvokeRequired) { BeginInvoke(() => HandleInferenceStatus(label, line)); return; }
        if (IsDisposed || Disposing || _stopping) return;

        if (label == "Camera tracking")
        {
            var match = Regex.Match(line, @"INFERENCE_BACKEND feature=tongue backend=(?<backend>[\w-]+) device=(?<device>\S+) name=(?<name>.*)");
            if (match.Success)
            {
                var backend = match.Groups["backend"].Value;
                var name = match.Groups["name"].Value.Trim();
                var description = backend switch
                {
                    "amd-rocm" => "AMD ROCm",
                    "nvidia-cuda" => "NVIDIA CUDA",
                    "cpu" => "CPU",
                    _ => "Other (" + match.Groups["device"].Value + ")"
                };
                SetStatus(_inferenceStatus, backend == "cpu" ? StatusKind.Warning : StatusKind.Good, description);
                _inferenceStatus.AccessibleDescription = name;
            }
        }
        if (_pupil.Checked && line.Contains("PUPIL_STATUS", StringComparison.Ordinal))
        {
            var leftValid = Regex.IsMatch(line, @"\bleft=\d");
            var rightValid = Regex.IsMatch(line, @"\bright=\d");
            var pupilLabel = leftValid && rightValid ? "CPU (2 eyes)" :
                leftValid || rightValid ? "CPU (1 eye)" : "CPU (warming)";
            SetStatus(_pupilStatus, leftValid && rightValid ? StatusKind.Good : StatusKind.Warning, pupilLabel);
            _pupilStatus.AccessibleDescription = line;
        }
    }

    private async Task<bool> RunUtilityAsync(string label, string script, params string[] args)
    {
        if (_trackingProcesses.Any(p => !p.HasExited)) { MessageBox.Show(this, "Stop live tracking before starting this action."); return false; }
        try
        {
            AppendLog($"Starting {label}…");
            var start = PowerShellStart(script, args, hidden: true);
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            using var process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) { AppendLog($"[{label}] {e.Data}"); HandleTrainingProgress(label, e.Data); } };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { AppendLog($"[{label}] {e.Data}"); HandleTrainingProgress(label, e.Data); } };
            if (!process.Start()) throw new InvalidOperationException($"Could not start {label}. See Activity for details.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            AppendLog($"{label} finished with code {process.ExitCode}.");
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"{label} failed with code {process.ExitCode}. See Activity for the exact error and suggested fix.");
            ReloadProfiles();
            // The wireless connection script already verifies ADB and Magisk root.
            // Show its result without waiting on a second network status probe.
            if (label != "Connect wireless Quest") await RefreshStatusAsync();
            return true;
        }
        catch (Exception error)
        {
            AppendLog($"{label} failed: {error.Message}");
            if (label == "Connect wireless Quest")
                MessageBox.Show(this,
                    "The Quest did not connect over wireless ADB. Check its current Wi-Fi IP and port, keep the headset awake, and allow Shell / ADB Shell in Magisk if prompted. See Activity for the exact error.",
                    "Quest connection failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                MessageBox.Show(this, error.Message, label + " failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private void BeginTrainingProgress(bool quick)
    {
        _trainingProgressContainer.Visible = true;
        _trainingStage = 0;
        _trainingStageCount = 2;
        _trainingProgress.Value = 1;
        _trainingProgressStatus.Text = $"Preparing the selected {(quick ? "refinement" : "full")} dataset…";
        _trainingProgressStatus.ForeColor = Warning;
    }

    private void HandleTrainingProgress(string label, string line)
    {
        if (!label.Contains("training", StringComparison.OrdinalIgnoreCase)) return;
        if (InvokeRequired) { BeginInvoke(() => HandleTrainingProgress(label, line)); return; }

        if (line.Contains("TRAIN_STATUS phase=preparing", StringComparison.Ordinal))
        {
            _trainingProgress.Value = 2;
            _trainingProgressStatus.Text = "Preparing and validating the selected stereo stills…";
            _trainingProgressStatus.ForeColor = Warning;
            return;
        }
        var deviceMatch = Regex.Match(line, @"TRAIN_DEVICE device=(?<device>\S+) batch=(?<batch>\d+)");
        if (deviceMatch.Success)
        {
            var device = deviceMatch.Groups["device"].Value;
            _trainingProgress.Value = Math.Max(_trainingProgress.Value, 5);
            _trainingProgressStatus.Text = device == "cpu"
                ? "CPU fallback active — training is working but slower. NVIDIA owners can rerun setup step 1 afterward."
                : "NVIDIA CUDA acceleration active — beginning model training…";
            _trainingProgressStatus.ForeColor = device == "cpu" ? Warning : Good;
            return;
        }
        var stageMatch = Regex.Match(line, @"TRAIN_STAGE index=(?<index>\d+) total=(?<total>\d+) name=(?<name>\S+) epochs=(?<epochs>\d+) device=(?<device>\S+)");
        if (stageMatch.Success)
        {
            _trainingStage = int.Parse(stageMatch.Groups["index"].Value);
            _trainingStageCount = Math.Max(1, int.Parse(stageMatch.Groups["total"].Value));
            var name = stageMatch.Groups["name"].Value;
            _trainingProgressStatus.Text = $"Training {name} checkpoint · stage {_trainingStage} of {_trainingStageCount} · 0/{stageMatch.Groups["epochs"].Value} epochs";
            _trainingProgressStatus.ForeColor = stageMatch.Groups["device"].Value == "cpu" ? Warning : Good;
            _trainingProgress.Value = Math.Clamp((int)Math.Round(5 + 94.0 * (_trainingStage - 1) / _trainingStageCount), 1, 99);
            return;
        }
        var epochMatch = Regex.Match(line, @"TRAIN_EPOCH current=(?<current>\d+) total=(?<total>\d+) focus=(?<focus>\S+)");
        if (!epochMatch.Success) return;
        var current = int.Parse(epochMatch.Groups["current"].Value);
        var total = Math.Max(1, int.Parse(epochMatch.Groups["total"].Value));
        var stage = Math.Max(1, _trainingStage);
        var overall = 5 + 94.0 * ((stage - 1) + Math.Clamp((double)current / total, 0, 1)) / Math.Max(1, _trainingStageCount);
        _trainingProgress.Value = Math.Clamp((int)Math.Round(overall), 1, 99);
        _trainingProgressStatus.Text = $"Training {epochMatch.Groups["focus"].Value} checkpoint · stage {stage} of {_trainingStageCount} · {current}/{total} epochs · {_trainingProgress.Value}%";
    }

    private void FinishTrainingProgress(bool succeeded)
    {
        _trainingProgress.Value = succeeded ? 100 : 0;
        _trainingProgressStatus.Text = succeeded
            ? "Training complete — the new model is ready in the model manager."
            : "Training stopped or failed — review Activity for the exact cause.";
        _trainingProgressStatus.ForeColor = succeeded ? Good : Bad;
    }
}
