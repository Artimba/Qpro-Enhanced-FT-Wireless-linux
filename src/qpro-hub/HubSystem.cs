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
    private ProcessStartInfo PowerShellStart(string script, IEnumerable<string> args, bool hidden)
        => _scripts.Create(script, args, hidden);

    private async Task RefreshStatusAsync()
    {
        if (_closingInProgress || _statusRefreshBusy || IsDisposed || Disposing) return;
        _statusRefreshBusy = true;
        try
        {
            var quest = await HasQuestAsync();
            if (_closingInProgress || IsDisposed || Disposing) return;
            var steam = Process.GetProcessesByName("vrserver").Any();
            var vrcft = Process.GetProcessesByName("VRCFaceTracking").Any();
            SetStatus(_usbStatus, quest ? StatusKind.Good : StatusKind.Bad,
                quest ? (_environment.WirelessSelected ? "Wi-Fi connected" : "USB connected")
                    : (_environment.WirelessSelected ? "Wi-Fi not connected" : "USB not connected"));
            SetStatus(_steamStatus, steam ? StatusKind.Good : StatusKind.Bad, steam ? "Running" : "Not running");
            SetStatus(_vrcftStatus, vrcft ? StatusKind.Good : StatusKind.Bad, vrcft ? "Running" : "Not running");
            var bridgeReady = CurrentBridgeInstalled();
            SetStatus(_bridgeStatus, bridgeReady ? StatusKind.Good : StatusKind.Warning,
                bridgeReady ? "Ready" : BridgeInstalled() ? "Update needed" : "Setup needed");
            SetStatus(_runtimeStatus, BackendReady() ? StatusKind.Good : StatusKind.Warning, BackendReady() ? "Ready" : "Setup needed");
            SetStatus(_gazeStatus, EyeModelReady() ? StatusKind.Good : StatusKind.Warning, EyeModelReady() ? "Prepared" : "Setup needed");
            UpdateSetupStepStyles();
            UpdateControlState();
        }
        finally { _statusRefreshBusy = false; }
    }

    private void UpdateSetupStepStyles()
    {
        var ready = new[] { BackendReady(), CurrentBridgeInstalled(), EyeModelReady() };
        var next = Array.FindIndex(ready, value => !value);
        StyleSetupStep(_setupRuntimeButton, _setupRuntimeStatus, "Install runtime", ready[0], next == 0);
        StyleSetupButton(_setupBridgeButton, "Install Virtual Desktop module",
            ready[1] && !_environment.SteamLinkSelected, next == 1 && !_environment.SteamLinkSelected);
        StyleSetupButton(_setupSteamLinkModuleButton, "Install Steam Link module",
            ready[1] && _environment.SteamLinkSelected, next == 1 && _environment.SteamLinkSelected);
        UpdateModuleInstallButtonState(!_setupActionRunning);
        _setupBridgeStatus.Text = ready[1] ? "● Complete" : next == 1 ? "● Next step" : "○ Waiting";
        _setupBridgeStatus.ForeColor = ready[1] ? Good : next == 1 ? Warning : Muted;
        _uninstallBridgeButton.Enabled = !_setupActionRunning && BridgeUninstallAvailable();
        StyleSetupStep(_setupGazeButton, _setupGazeStatus, "Prepare gaze", ready[2], next == 2);
        var latestReady = AmdInstallEligible && LatestRocmInstalled();
        var legacyReady = AmdInstallEligible && LegacyRocmInstalled();
        var latestEnvironmentExists = AmdInstallEligible && LatestRocmEnvironmentExists();
        _setupAmdStatus.Text = _rocmInstallRunning ? "◌ Installing and verifying ROCm 10.0…" :
            latestReady ? legacyReady ? "● ROCm 10.0 verified; ROCm 7.2.1 fallback ready" : "● ROCm 10.0 verified; GPU tests passed" :
            legacyReady ? latestEnvironmentExists ? "● ROCm 7.2.1 fallback ready; verify ROCm 10.0" : "● ROCm 7.2.1 fallback ready; ROCm 10.0 available" :
            latestEnvironmentExists ? "○ ROCm 10.0 environment found; verify or repair setup" :
            AmdInstallEligible ? "○ ROCm 10.0 available after PC runtime" : "○ Eligible discrete Radeon required";
        _setupAmdStatus.ForeColor = _rocmInstallRunning || latestEnvironmentExists && !latestReady
            ? Warning : latestReady || legacyReady ? Good : AmdInstallEligible ? Warning : Muted;
        _setupAmdButton.Text = latestReady ? "Repair ROCm 10.0" :
            latestEnvironmentExists ? "Verify / repair ROCm 10.0" :
            legacyReady ? "Upgrade to ROCm 10.0" : "Install ROCm 10.0";
        _setupAmdButton.OutlineColor = latestReady ? Good : AmdInstallEligible && _setupPulseOn ? Accent : Border;
        _setupAmdButton.OutlineWidth = latestReady || AmdInstallEligible && _setupPulseOn ? 2 : 1;
        _setupAmdButton.Enabled = !_setupActionRunning && AmdInstallEligible && BackendReady();
    }

    private void StyleSetupStep(DarkButton button, Label status, string label, bool complete, bool attention)
    {
        StyleSetupButton(button, label, complete, attention);
        status.Text = complete ? "● Complete" : attention ? "● Next step" : "○ Waiting";
        status.ForeColor = complete ? Good : attention ? Warning : Muted;
    }

    private void StyleSetupButton(DarkButton button, string label, bool complete, bool attention)
    {
        button.Text = complete ? "✓  " + label : label;
        button.OutlineColor = complete ? Good : attention && _setupPulseOn ? Accent : Border;
        button.OutlineWidth = complete || attention && _setupPulseOn ? 2 : 1;
        button.Invalidate();
    }

    private void UpdateModuleInstallButtonState(bool enabled)
    {
        _setupBridgeButton.Enabled = enabled && !_environment.SteamLinkSelected;
        _setupSteamLinkModuleButton.Enabled = enabled && _environment.SteamLinkSelected;
    }

    private void PlaySfx(string fileName)
    {
        var path = Path.Combine(_root, "SFX", fileName);
        if (!File.Exists(path)) return;
        try
        {
            _soundPlayer?.Stop();
            _soundPlayer?.Dispose();
            _soundPlayer = new SoundPlayer(path);
            _soundPlayer.Play();
        }
        catch (Exception error)
        {
            AppendLog($"Sound could not play: {error.Message}");
        }
    }

    private string? GetConfiguredAdbTarget() => _environment.GetConfiguredAdbTarget();
    private Task<bool> HasQuestAsync() => _environment.HasQuestAsync();
    private Task<(bool Completed, int ExitCode, string Output)> RunAdbProbeAsync(string adb, IEnumerable<string> arguments, int timeoutSeconds = 4)
        => _environment.RunAdbProbeAsync(adb, arguments, timeoutSeconds);
    private string? FindAdb() => _environment.FindAdb();
    private bool BridgeInstalled() => _environment.BridgeInstalled();
    private static bool VrcftModuleProcessRunning() =>
        new[] { "VRCFaceTracking", "VRCFaceTracking.ModuleProcess", "ModuleProcess" }
            .Any(name => Process.GetProcessesByName(name).Length > 0);
    private bool BridgeUninstallAvailable()
    {
        if (BridgeInstalled()) return true;
        var research = Path.Combine(_root, "research");
        if (!Directory.Exists(research)) return false;
        if (Directory.Exists(Path.Combine(research, "vrcft-legacy-registry-module-backup")) ||
            Directory.Exists(Path.Combine(research, "vrcft-official-virtual-desktop-backup"))) return true;
        return Directory.EnumerateDirectories(research, "vrcft-official-virtual-desktop-backup-*").Any();
    }
    private bool CurrentBridgeInstalled() => _environment.CurrentBridgeInstalled();
    private bool BackendReady() => _environment.BackendReady();
    private bool EyeModelReady() => _environment.EyeModelReady();
    private string VisibilityModeValue() => _visibilityMode.SelectedIndex switch { 1 => "camera", 2 => "native", 3 => "agreement", _ => "weighted" };
    private string PupilSensitivityValue() => ((10 + 2 * Math.Max(0, _pupilSensitivity.SelectedIndex)) / 10.0)
        .ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    private string? FindPythonRuntime() => _environment.FindPythonRuntime();

    private void UpdateControlState()
    {
        var running = _trackingProcesses.Any(p => !p.HasExited);
        // These values are passed once to the child process. Keep the controls
        // locked until Stop, rather than implying that a live model was reloaded.
        var sessionEditable = !running && !_stopping && !_starting;
        _gaze.Enabled = sessionEditable;
        _tongue.Enabled = sessionEditable;
        _pupil.Enabled = sessionEditable;
        _eyeProfiles.Enabled = _gaze.Checked && sessionEditable;
        _tongueModels.Enabled = _tongue.Checked && sessionEditable;
        _fps.Enabled = (_tongue.Checked || _pupil.Checked) && sessionEditable;
        _pupilSensitivity.Enabled = _pupil.Checked && sessionEditable;
        _cameraPreview.Enabled = sessionEditable;
        _connectionMode.Enabled = !_setupActionRunning && !_utilityActionRunning &&
            !_datasetOperationBusy && !_trackingProcesses.Any(p => !p.HasExited) &&
            !_stopping && !_starting;
        _trackingSourceSetup.Enabled = !_setupActionRunning && !_utilityActionRunning &&
            !_datasetOperationBusy && !_trackingProcesses.Any(p => !p.HasExited) &&
            !_stopping && !_starting;
        _trackingSourceLive.Enabled = _trackingSourceSetup.Enabled;
        _smoothing.Enabled = _tongue.Checked && sessionEditable;
        _visibilityMode.Enabled = _tongue.Checked && sessionEditable;
        _recoverGazeButton.Enabled = sessionEditable && !_setupActionRunning && !_utilityActionRunning && !_datasetOperationBusy && !_gazeRecoveryRunning;
        _inspectGazeButton.Enabled = _recoverGazeButton.Enabled;
        _resetLegacyGazeButton.Enabled = _recoverGazeButton.Enabled;
        var canStart = !running && !_stopping && !_starting && !_setupActionRunning &&
            !_utilityActionRunning && !_datasetOperationBusy;
        _start.Enabled = canStart;
        _stop.Enabled = (running || _starting) && !_stopping;
        StyleRunButton(_start, canStart);
        StyleRunButton(_stop, (running || _starting) && !_stopping);
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        e.Cancel = true;
        if (_closingInProgress || UtilityActionIsBusy()) return;
        _closingInProgress = true;
        Enabled = false;
        try
        {
            await _environment.SuspendAdbProbesAsync();
            if (_starting || _trackingProcesses.Any(p => !p.HasExited))
                await StopTrackingAsync();
            // Startup may still be returning from its canceled ADB check.
            // Do not stop ADB until the tracking scripts have restored headset overrides.
            for (var attempt = 0; attempt < 200 && _starting; attempt++)
                await Task.Delay(50);
            if (_starting || _trackingProcesses.Any(p => !p.HasExited))
            {
                AppendLog("Hub shutdown is waiting for tracking cleanup. Stop tracking, then close the Hub again.");
                return;
            }
            AppendLog("Stopping the ADB server before closing the Hub…");
            var result = await _environment.StopAdbServerAsync();
            AppendLog(result.Completed && result.ExitCode == 0 ? "ADB server stopped."
                : $"ADB server shutdown could not be confirmed: {result.Output}");
            FormClosing -= OnClosing;
            Close();
        }
        catch (Exception error) { AppendLog("Hub shutdown could not finish: " + error.Message); }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                _closingInProgress = false;
                _environment.ResumeAdbProbes();
                Enabled = true;
                UpdateControlState();
            }
        }
    }

    private void AppendLog(string text)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => AppendLog(text)); }
            catch (InvalidOperationException) { /* The window closed before the log arrived. */ }
            return;
        }
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {text}{Environment.NewLine}");
        _log.SelectionStart = _log.TextLength; _log.ScrollToCaret();
    }

    private static int VersionFromPath(string path) => Regex.Match(Path.GetFileName(path), @"-v(\d+)").Success && int.TryParse(Regex.Match(Path.GetFileName(path), @"-v(\d+)").Groups[1].Value, out var v) ? v : 0;
    private static void SelectOrFirst(ComboBox box, string? previous)
    {
        for (var i = 0; i < box.Items.Count; i++) if ((box.Items[i] as FileChoice)?.Primary == previous) { box.SelectedIndex = i; return; }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }
    private static void SelectOrFirst(ListBox box, string? previous)
    {
        for (var i = 0; i < box.Items.Count; i++) if ((box.Items[i] as FileChoice)?.Primary == previous) { box.SelectedIndex = i; return; }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }
    private enum StatusKind { Good, Warning, Bad }
}
