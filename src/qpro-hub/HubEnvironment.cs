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
    internal bool WirelessSelected { get; private set; }
    internal bool CameraPreviewEnabled { get; private set; } = true;
    internal bool IndependentGazeEnabled { get; private set; } = true;
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
            catch { IndependentGazeEnabled = true; }
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

    internal bool BridgeInstalled() => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCFaceTracking", "CustomLibs", "000-Qpro.IndependentGaze.dll"));

    internal bool PupilBridgeInstalled()
    {
        var supplied = Path.Combine(_root, "vrcft-gaze-bridge", "bin", "Release", "net10.0", "Qpro.GazeBridge.dll");
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCFaceTracking", "CustomLibs", "000-Qpro.IndependentGaze.dll");
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
    internal bool EyeModelReady() => File.Exists(Path.Combine(_root, "research", "seacliff_eye_model", "bolt-independent-axes.ptl"));
    internal string? FindPythonRuntime()
    {
        var environments = new List<string>
        {
            Path.Combine(_root, ".venv"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QproFaceTracking", "runtime", ".venv")
        };
        var sharedEnvironment = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QproFaceTracking", "runtime", ".venv");
        var sharedReadyMarker = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QproFaceTracking", "runtime", "runtime-ready.json");
        var parent = new DirectoryInfo(_root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Parent;
        if (parent is not null)
        {
            try
            {
                environments.AddRange(Directory.GetDirectories(parent.FullName, "QproFaceTracking-*")
                    .OrderByDescending(path => path)
                    .Select(path => Path.Combine(path, ".venv")));
            }
            catch { }
            if (parent.Name.Equals("dist", StringComparison.OrdinalIgnoreCase) && parent.Parent is not null)
                environments.Add(Path.Combine(parent.Parent.FullName, ".venv"));
        }
        foreach (var environment in environments.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(Path.GetFullPath(environment), Path.GetFullPath(sharedEnvironment), StringComparison.OrdinalIgnoreCase)
                && !File.Exists(sharedReadyMarker)) continue;
            foreach (var name in new[] { "python.exe", "qpro-python-console.exe" })
            {
                var candidate = Path.Combine(environment, "Scripts", name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
