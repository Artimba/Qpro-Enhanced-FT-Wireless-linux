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

internal sealed class HubEnvironment
{
    private readonly string _root;
    private string? _usbSerial;
    private readonly object _runtimeProbeLock = new();
    private readonly RuntimeProbeState _configuredRuntimeProbe = new();
    private readonly RuntimeProbeState _sharedRuntimeProbe = new();

    private sealed class RuntimeProbeState
    {
        internal string? Identity;
        internal Task<bool>? Task;
        internal DateTime StartedUtc;
        internal bool LastKnownReady;
    }
    internal bool WirelessSelected { get; private set; }
    internal bool SteamLinkSelected { get; private set; }
    internal string TrackingSourceArgument => SteamLinkSelected ? "SteamLink" : "VirtualDesktop";
    private static string TrackingSourcePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QproFaceTracking", "config", "tracking-source.txt");
    internal bool CameraPreviewEnabled { get; private set; } = true;
    internal bool IndependentGazeEnabled { get; private set; }
    internal bool HasOpenedBefore => File.Exists(Path.Combine(_root, "config", "hub-opened.txt"));

    internal HubEnvironment(string root)
    {
        _root = root;
        var modePath = Path.Combine(_root, "config", "connection-mode.txt");
        if (File.Exists(modePath))
        {
            try { WirelessSelected = File.ReadAllText(modePath).Trim().Equals("wireless", StringComparison.OrdinalIgnoreCase); }
            catch { WirelessSelected = false; }
        }
        else
        {
            WirelessSelected = File.Exists(Path.Combine(_root, "config", "wireless-headset.json"));
        }
        ReloadTrackingSource();
        var previewPath = Path.Combine(_root, "config", "camera-preview.txt");
        if (File.Exists(previewPath))
        {
            try { CameraPreviewEnabled = !File.ReadAllText(previewPath).Trim().Equals("off", StringComparison.OrdinalIgnoreCase); }
            catch { CameraPreviewEnabled = true; }
        }
        var gazePath = Path.Combine(_root, "config", "independent-gaze.txt");
        if (File.Exists(gazePath))
        {
            try { IndependentGazeEnabled = !File.ReadAllText(gazePath).Trim().Equals("off", StringComparison.OrdinalIgnoreCase); }
            catch { IndependentGazeEnabled = false; }
        }
    }

    internal void SelectCameraPreview(bool enabled)
    {
        var directory = Path.Combine(_root, "config");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "camera-preview.txt"), enabled ? "on" : "off");
        CameraPreviewEnabled = enabled;
    }

    internal void SelectIndependentGaze(bool enabled)
    {
        var directory = Path.Combine(_root, "config");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "independent-gaze.txt"), enabled ? "on" : "off");
        IndependentGazeEnabled = enabled;
    }

    internal void MarkOpened()
    {
        var directory = Path.Combine(_root, "config");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "hub-opened.txt"), "opened");
    }

    internal void SelectConnection(bool wireless)
    {
        var directory = Path.Combine(_root, "config");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "connection-mode.txt"), wireless ? "wireless" : "usb");
        WirelessSelected = wireless;
        _usbSerial = null;
    }

    internal void SelectTrackingSource(bool steamLink)
    {
        var directory = Path.GetDirectoryName(TrackingSourcePath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(TrackingSourcePath, steamLink ? "steam-link" : "virtual-desktop");
        SteamLinkSelected = steamLink;
    }

    internal void ReloadTrackingSource()
    {
        try
        {
            SteamLinkSelected = File.Exists(TrackingSourcePath) &&
                File.ReadAllText(TrackingSourcePath).Trim().Equals("steam-link", StringComparison.OrdinalIgnoreCase);
        }
        catch { SteamLinkSelected = false; }
    }

    internal bool TrackingSourceRequiresVrcftRestart()
    {
        if (!File.Exists(TrackingSourcePath)) return false;
        var selectedAt = File.GetLastWriteTimeUtc(TrackingSourcePath);
        foreach (var process in Process.GetProcessesByName("VRCFaceTracking"))
        {
            using (process)
            {
                try
                {
                    if (process.StartTime.ToUniversalTime() < selectedAt) return true;
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { return true; }
            }
        }
        return false;
    }

    internal string? GetConfiguredAdbTarget()
    {
        if (!WirelessSelected) return _usbSerial;
        var saved = GetSavedWirelessTarget();
        if (!string.IsNullOrWhiteSpace(saved)) return saved;
        foreach (var value in new[] { Environment.GetEnvironmentVariable("QPRO_ADB_TARGET"), Environment.GetEnvironmentVariable("ANDROID_SERIAL") })
            if (!string.IsNullOrWhiteSpace(value) && value.Contains(':')) return value.Trim();
        return null;
    }

    internal string? GetSavedWirelessTarget()
    {
        var configPath = Path.Combine(_root, "config", "wireless-headset.json");
        try
        {
            if (File.Exists(configPath))
                return JsonNode.Parse(File.ReadAllText(configPath))?["adbTarget"]?.GetValue<string>()?.Trim();
        }
        catch { /* An invalid local config must not crash the hub. */ }
        return null;
    }

    internal async Task<bool> HasQuestAsync()
    {
        var adb = FindAdb();
        if (adb is null) return false;
        if (!WirelessSelected)
        {
            var devices = await RunAdbProbeAsync(adb, ["devices"], 3);
            if (!devices.Completed || devices.ExitCode != 0) return false;
            var usb = devices.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                .Where(parts => parts.Length >= 2 && parts[1].Equals("device", StringComparison.OrdinalIgnoreCase)
                    && !parts[0].Contains(':'))
                .Select(parts => parts[0]).ToArray();
            _usbSerial = usb.Length == 1 ? usb[0] : null;
            return _usbSerial is not null;
        }
        var target = GetConfiguredAdbTarget();
        if (!string.IsNullOrWhiteSpace(target))
        {
            var state = await RunAdbProbeAsync(adb, ["-s", target, "get-state"], 3);
            if (state.Completed && state.ExitCode == 0 && state.Output.Trim() == "device") return true;
            if (target.Contains(':'))
            {
                await RunAdbProbeAsync(adb, ["connect", target], 5);
                state = await RunAdbProbeAsync(adb, ["-s", target, "get-state"], 3);
                return state.Completed && state.ExitCode == 0 && state.Output.Trim() == "device";
            }
            return false;
        }
        return false;
    }

    internal static async Task<(bool Completed, int ExitCode, string Output)> RunAdbProbeAsync(string adb, IEnumerable<string> arguments, int timeoutSeconds = 4)
    {
        try
        {
            var info = new ProcessStartInfo(adb)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = info };
            if (!process.Start()) return (false, -1, "ADB could not start.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                return (false, -1, "ADB timed out.");
            }
            var output = string.Join("\n", new[] { await standardOutput, await standardError }.Where(value => !string.IsNullOrWhiteSpace(value)));
            return (true, process.ExitCode, output);
        }
        catch (Exception error)
        {
            return (false, -1, error.Message);
        }
    }

    internal string? FindAdb()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("QPRO_ADB"),
            Path.Combine(_root, "platform-tools", "adb.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe")
        };
        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    private static string CustomLibsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCFaceTracking", "CustomLibs");
    internal bool BridgeInstalled() => new[]
    {
        "000-Qpro.VirtualDesktop.dll", "000-Qpro.SteamLink.dll", "000-Qpro.IndependentGaze.dll"
    }.Any(name => File.Exists(Path.Combine(CustomLibsPath, name)));

    internal bool CurrentBridgeInstalled()
    {
        var supplied = Path.Combine(_root, "vrcft-gaze-bridge", "bin", "Release", "net10.0", "Qpro.GazeBridge.dll");
        var installed = Path.Combine(CustomLibsPath,
            SteamLinkSelected ? "000-Qpro.SteamLink.dll" : "000-Qpro.VirtualDesktop.dll");
        var other = Path.Combine(CustomLibsPath,
            SteamLinkSelected ? "000-Qpro.VirtualDesktop.dll" : "000-Qpro.SteamLink.dll");
        var legacy = Path.Combine(CustomLibsPath, "000-Qpro.IndependentGaze.dll");
        if (File.Exists(other) || File.Exists(legacy)) return false;
        if (!File.Exists(supplied) || !File.Exists(installed)) return false;
        try
        {
            using var suppliedStream = File.OpenRead(supplied);
            using var installedStream = File.OpenRead(installed);
            return SHA256.HashData(suppliedStream).AsSpan().SequenceEqual(SHA256.HashData(installedStream));
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    internal bool BackendReady() => FindPythonRuntime() is not null;
    internal bool EyeModelReady()
    {
        var prepared = Path.Combine(_root, "research", "seacliff_eye_model");
        return File.Exists(Path.Combine(prepared, "bolt-independent-axes.ptl"))
            && File.Exists(Path.Combine(prepared, "bolt-independent-axes.manifest.json"));
    }
    internal string? FindPythonRuntime()
    {
        var configuredPython = Environment.GetEnvironmentVariable("QPRO_PYTHON");
        if (!string.IsNullOrWhiteSpace(configuredPython) && File.Exists(configuredPython))
        {
            var configuredReady = VerifiedPythonRuntime(configuredPython, "configured", _configuredRuntimeProbe);
            if (configuredReady is not null) return configuredReady;
        }
        // A stale explicit override should not hide a healthy Qpro runtime.

        var sharedRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QproFaceTracking", "runtime");
        var sharedPython = Path.Combine(sharedRoot, ".venv", "Scripts", "python.exe");
        var sharedReadyMarker = Path.Combine(sharedRoot, "runtime-ready.json");
        if (!File.Exists(sharedPython) || !File.Exists(sharedReadyMarker)) return null;
        try
        {
            var markerFile = new FileInfo(sharedReadyMarker);
            if (markerFile.Length > 4096) return null;
            var markerText = File.ReadAllText(sharedReadyMarker);
            var marker = JsonNode.Parse(markerText);
            var recordedPython = marker?["python"]?.GetValue<string>();
            return marker?["format"]?.GetValue<string>() == "qpro-runtime-ready-v1"
                && !string.IsNullOrWhiteSpace(recordedPython)
                && string.Equals(Path.GetFullPath(recordedPython), Path.GetFullPath(sharedPython), StringComparison.OrdinalIgnoreCase)
                ? VerifiedPythonRuntime(sharedPython, markerText, _sharedRuntimeProbe) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private string? VerifiedPythonRuntime(string python, string markerIdentity, RuntimeProbeState cache)
    {
        try
        {
            var executable = new FileInfo(python);
            var identity = $"{executable.FullName}|{executable.Length}|{executable.LastWriteTimeUtc.Ticks}|{markerIdentity}";
            lock (_runtimeProbeLock)
            {
                var now = DateTime.UtcNow;
                if (cache.Identity != identity)
                {
                    cache.Identity = identity;
                    cache.LastKnownReady = false;
                    cache.StartedUtc = now;
                    cache.Task = Task.Run(() => ProbePythonRuntimeAsync(executable.FullName));
                    return null;
                }
                if (cache.Task?.IsCompleted == true)
                    cache.LastKnownReady = cache.Task.IsCompletedSuccessfully && cache.Task.Result;
                // A changed readiness marker invalidates the cached result;
                // timed retries also notice repairs made outside the Hub.
                var retryAfter = cache.LastKnownReady ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(30);
                // Importing PyTorch may take seconds. Start it off the UI thread,
                // and reuse the result across the Hub's frequent status refreshes.
                if (cache.Task is null ||
                    (cache.Task.IsCompleted && now - cache.StartedUtc >= retryAfter))
                {
                    cache.StartedUtc = now;
                    cache.Task = Task.Run(() => ProbePythonRuntimeAsync(executable.FullName));
                    // Keep the previous verified status while the periodic
                    // recheck runs; only a changed marker clears it at once.
                }
                return cache.LastKnownReady ? executable.FullName : null;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static async Task<bool> ProbePythonRuntimeAsync(string python)
    {
        try
        {
            var start = new ProcessStartInfo(python)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("import cv2,numpy,torch; assert hasattr(cv2,'namedWindow')");
            using var process = new Process { StartInfo = start };
            if (!process.Start()) return false;
            var output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            var errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            // A damaged import can hang; do not let it stall future status checks.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return false;
            }
            await Task.WhenAll(output, errors).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch { return false; }
    }
}
