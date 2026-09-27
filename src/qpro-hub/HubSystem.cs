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
        if (_statusRefreshBusy || IsDisposed || Disposing) return;
        _statusRefreshBusy = true;
        try
        {
            var quest = await HasQuestAsync();
            if (IsDisposed || Disposing) return;
            var steam = Process.GetProcessesByName("vrserver").Any();
            var vrcft = Process.GetProcessesByName("VRCFaceTracking").Any();
            SetStatus(_usbStatus, quest ? StatusKind.Good : StatusKind.Bad,
                quest ? (_environment.WirelessSelected ? "Wi-Fi connected" : "USB connected")
                    : (_environment.WirelessSelected ? "Wi-Fi not connected" : "USB not connected"));
            SetStatus(_steamStatus, steam ? StatusKind.Good : StatusKind.Bad, steam ? "Running" : "Not running");
            SetStatus(_vrcftStatus, vrcft ? StatusKind.Good : StatusKind.Bad, vrcft ? "Running" : "Not running");
            SetStatus(_bridgeStatus, BridgeInstalled() ? StatusKind.Good : StatusKind.Warning, BridgeInstalled() ? "Installed" : "Setup needed");
            SetStatus(_runtimeStatus, BackendReady() ? StatusKind.Good : StatusKind.Warning, BackendReady() ? "Ready" : "Setup needed");
            SetStatus(_gazeStatus, EyeModelReady() ? StatusKind.Good : StatusKind.Warning, EyeModelReady() ? "Prepared" : "Setup needed");
            UpdateSetupStepStyles();
            UpdateControlState();
        }
        finally { _statusRefreshBusy = false; }
    }

    private void UpdateSetupStepStyles()
    {
        var ready = new[] { BackendReady(), BridgeInstalled(), EyeModelReady() };
        var next = Array.FindIndex(ready, value => !value);
        StyleSetupStep(_setupRuntimeButton, _setupRuntimeStatus, "Install runtime", ready[0], next == 0);
        StyleSetupStep(_setupBridgeButton, _setupBridgeStatus, "Install bridge", ready[1], next == 1);
        _uninstallBridgeButton.Enabled = !_setupActionRunning && BridgeUninstallAvailable();
        StyleSetupStep(_setupGazeButton, _setupGazeStatus, "Prepare gaze", ready[2], next == 2);
        var rocmReady = RocmInstalled();
        var rocmEnvironmentExists = RocmEnvironmentExists();
        _setupAmdStatus.Text = _rocmInstallRunning ? "◌ Installing and verifying AMD ROCm…" :
            rocmReady ? "● ROCm installed and GPU tests passed" :
            rocmEnvironmentExists ? "○ ROCm environment found; verify or repair setup" :
            AmdInstallEligible ? "○ Available after PC runtime" : "○ AMD GPU required";
        _setupAmdStatus.ForeColor = _rocmInstallRunning || rocmEnvironmentExists && !rocmReady
            ? Warning : rocmReady ? Good : AmdInstallEligible ? Warning : Muted;
        _setupAmdButton.Text = rocmReady ? "Repair AMD ROCm" :
            rocmEnvironmentExists ? "Verify / repair AMD ROCm" : "Install AMD ROCm";
        _setupAmdButton.OutlineColor = rocmReady ? Good : AmdInstallEligible && _setupPulseOn ? Accent : Border;
        _setupAmdButton.OutlineWidth = rocmReady || AmdInstallEligible && _setupPulseOn ? 2 : 1;
        _setupAmdButton.Enabled = !_setupActionRunning && AmdInstallEligible && BackendReady();
    }

    private void StyleSetupStep(DarkButton button, Label status, string label, bool complete, bool attention)
    {
        button.Text = complete ? "✓  " + label : label;
        button.OutlineColor = complete ? Good : attention && _setupPulseOn ? Accent : Border;
        button.OutlineWidth = complete || attention && _setupPulseOn ? 2 : 1;
        status.Text = complete ? "● Complete" : attention ? "● Next step" : "○ Waiting";
        status.ForeColor = complete ? Good : attention ? Warning : Muted;
        button.Invalidate();
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
    private static Task<(bool Completed, int ExitCode, string Output)> RunAdbProbeAsync(string adb, IEnumerable<string> arguments, int timeoutSeconds = 4)
        => HubEnvironment.RunAdbProbeAsync(adb, arguments, timeoutSeconds);
    private string? FindAdb() => _environment.FindAdb();
    private bool BridgeInstalled() => _environment.BridgeInstalled();
    private bool BridgeUninstallAvailable()
    {
        if (BridgeInstalled()) return true;
        var research = Path.Combine(_root, "research");
        if (!Directory.Exists(research)) return false;
        if (Directory.Exists(Path.Combine(research, "vrcft-legacy-registry-module-backup")) ||
            Directory.Exists(Path.Combine(research, "vrcft-official-virtual-desktop-backup"))) return true;
        return Directory.EnumerateDirectories(research, "vrcft-official-virtual-desktop-backup-*").Any();
    }
    private bool PupilBridgeInstalled() => _environment.PupilBridgeInstalled();
    private bool BackendReady() => _environment.BackendReady();
    private bool EyeModelReady() => _environment.EyeModelReady();
    private string VisibilityModeValue() => _visibilityMode.SelectedIndex switch { 1 => "camera", 2 => "native", 3 => "agreement", _ => "weighted" };
    private string PupilSensitivityValue() => ((10 + 2 * Math.Max(0, _pupilSensitivity.SelectedIndex)) / 10.0)
        .ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    private string? FindPythonRuntime() => _environment.FindPythonRuntime();

    private void UpdateControlState()
    {
        _eyeProfiles.Enabled = _gaze.Checked;
        _tongueModels.Enabled = _tongue.Checked;
        _fps.Enabled = _tongue.Checked || _pupil.Checked;
        _pupilSensitivity.Enabled = _pupil.Checked;
        _cameraPreview.Enabled = !_trackingProcesses.Any(p => !p.HasExited) && !_stopping && !_starting;
        _smoothing.Enabled = _tongue.Checked;
        _visibilityMode.Enabled = _tongue.Checked;
        var running = _trackingProcesses.Any(p => !p.HasExited);
        _start.Enabled = !running && !_stopping && !_starting;
        _stop.Enabled = (running || _starting) && !_stopping;
        StyleRunButton(_start, !running && !_stopping && !_starting);
        StyleRunButton(_stop, (running || _starting) && !_stopping);
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_trackingProcesses.Any(p => !p.HasExited)) return;
        e.Cancel = true;
        await StopTrackingAsync();
        if (!_trackingProcesses.Any(p => !p.HasExited)) { FormClosing -= OnClosing; Close(); }
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
