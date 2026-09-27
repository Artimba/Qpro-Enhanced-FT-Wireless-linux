using System.Diagnostics;
using System.Net;

namespace QproFaceTracking.Hub;

internal sealed partial class HubForm
{
    private readonly ComboBox _connectionMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Label _connectionModeNote = new() { AutoSize = true, ForeColor = Muted, Tag = "responsive-info" };
    private readonly TableLayoutPanel _wirelessSetup = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Visible = false };
    private readonly TextBox _wirelessAddress = new() { Dock = DockStyle.Fill };
    private readonly TextBox _pairingEndpoint = new() { Dock = DockStyle.Fill };
    private readonly TextBox _pairingCode = new() { Dock = DockStyle.Fill, MaxLength = 6, UseSystemPasswordChar = true };
    private readonly DarkButton _enableWirelessButton = SetupButton("Enable from USB");
    private readonly DarkButton _connectWirelessButton = SetupButton("Connect to Quest");
    private readonly DarkButton _pairWirelessButton = SetupButton("Pair and connect");
    private readonly DarkButton _disableWirelessButton = SetupButton("Disable Wi-Fi ADB");
    private readonly Label _setupAmdStatus = SetupStatusLabel();
    private readonly Label _amdGpuStatus = new() { AutoSize = true, ForeColor = Muted, Margin = new Padding(5, 3, 5, 8), Tag = "responsive-info" };
    private readonly CheckBox _amdManualConfirm = new() { Text = "I checked that my GPU is on AMD's Windows ROCm 7.2.1 list", Dock = DockStyle.Top, Height = 34, ForeColor = Muted, Visible = false };
    private readonly DarkButton _setupAmdButton = SetupButton("Install AMD ROCm");
    private bool _amdGpuSupported;
    private bool _amdGpuDetected;
    private bool _rocmInstallRunning;
    private bool _connectionSelectionUpdating;
    private bool AmdInstallEligible => Environment.OSVersion.Version.Build >= 22000
        && _amdGpuDetected && (_amdGpuSupported || _amdManualConfirm.Checked);

    private void InitializeIntegratedSetup()
    {
        _setupAmdButton.Enabled = false;
        _amdManualConfirm.CheckedChanged += (_, _) => SetSetupButtonsEnabled(true);
        _connectionMode.Items.AddRange(["USB cable", "Wireless ADB (Wi-Fi)"]);
        _connectionMode.SelectedIndex = _environment.WirelessSelected ? 1 : 0;
        _wirelessAddress.Text = _environment.GetSavedWirelessTarget() ?? "";
        _connectionMode.SelectedIndexChanged += async (_, _) =>
        {
            if (_connectionSelectionUpdating) return;
            try
            {
                _environment.SelectConnection(_connectionMode.SelectedIndex == 1);
                UpdateConnectionModeUi();
                AppendLog($"Quest connection selected: {(_environment.WirelessSelected ? "wireless ADB" : "USB")}.");
                await RefreshStatusAsync();
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, "Could not save connection choice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _connectionSelectionUpdating = true;
                _connectionMode.SelectedIndex = _environment.WirelessSelected ? 1 : 0;
                _connectionSelectionUpdating = false;
            }
        };
        _enableWirelessButton.Click += async (_, _) => { await RunConnectionStepAsync("Enable wireless ADB from USB", "Enable-QproWireless.ps1"); };
        _connectWirelessButton.Click += async (_, _) =>
        {
            var target = NormalizeQuestAddress(_wirelessAddress.Text);
            if (target is null) return;
            _wirelessAddress.Text = target;
            if (await RunConnectionStepAsync("Connect wireless Quest", "Connect-QproWireless.ps1", "-AdbTarget", target))
                MessageBox.Show(this,
                    $"Connected to your Quest at {target}.\n\nWireless ADB and Magisk root are ready. You can start tracking; pairing is not needed.",
                    "Quest connected", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        _pairWirelessButton.Click += async (_, _) =>
        {
            var target = NormalizeQuestAddress(_wirelessAddress.Text);
            if (target is null) return;
            if (string.Equals(target, _environment.GetSavedWirelessTarget(), StringComparison.OrdinalIgnoreCase)
                && await HasQuestAsync())
            {
                AppendLog("Wireless Quest is already connected. Pairing is unnecessary; verifying ADB and Magisk root instead.");
                await RunConnectionStepAsync("Connect wireless Quest", "Connect-QproWireless.ps1", "-AdbTarget", target);
                return;
            }
            var endpoint = NormalizeQuestAddress(_pairingEndpoint.Text, requirePort: true);
            if (endpoint is null) return;
            if (string.Equals(endpoint, target, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this,
                    "The pairing address must use the temporary port shown with the six-digit code. " +
                    "The regular Quest IP:port is for Connect to Quest.",
                    "Use the pairing port", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(_pairingCode.Text, "^[0-9]{6}$"))
            {
                MessageBox.Show(this, "Enter the six-digit code shown in the headset pairing dialog.", "Pairing code needed");
                return;
            }
            try
            {
                await RunConnectionStepAsync("Pair wireless Quest", "Pair-QproWireless.ps1",
                    "-PairingEndpoint", endpoint, "-AdbTarget", target, "-PairingCode", _pairingCode.Text);
            }
            finally { _pairingCode.Clear(); }
        };
        _disableWirelessButton.Click += async (_, _) => { await RunConnectionStepAsync("Disable wireless ADB", "Disable-QproWireless.ps1"); };
        _setupAmdButton.Click += async (_, _) =>
        {
            if (!BackendReady())
            {
                MessageBox.Show(this, "Install the PC runtime first, then install AMD ROCm.", "PC runtime needed");
                return;
            }
            if (!AmdInstallEligible)
            {
                MessageBox.Show(this, "AMD ROCm requires a supported AMD GPU. On an NVIDIA-only PC, use Install runtime for the NVIDIA/CUDA path.", "AMD GPU needed");
                return;
            }
            _rocmInstallRunning = true;
            try
            {
                await RunSetupStepAsync("AMD ROCm setup", "Install-QproRocm.ps1",
                    "ROCm passed its GPU checks. Tongue inference and training will use it automatically.",
                    "You can now start tracking or train a personal model.");
            }
            finally
            {
                _rocmInstallRunning = false;
                UpdateSetupStepStyles();
            }
        };
        UpdateConnectionModeUi();
        _ = DetectAmdGpuAsync();
    }

    private void UpdateConnectionModeUi()
    {
        _wirelessSetup.Visible = _environment.WirelessSelected;
        _connectionModeNote.Text = _environment.WirelessSelected
            ? "Enter the Quest's Wi-Fi address below. The headset and PC must be on the same trusted network."
            : "Connect the rooted Quest by USB and approve its debugging prompt in the headset.";
        _connectionModeNote.ForeColor = Muted;
    }

    private string? NormalizeQuestAddress(string value, bool requirePort = false)
    {
        value = value.Trim();
        var parts = value.Split(':');
        if ((!requirePort && parts.Length == 1) && IPAddress.TryParse(parts[0], out var ipOnly)
            && ipOnly.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return value + ":5555";
        if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var ip)
            && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            && int.TryParse(parts[1], out var port) && port is >= 1024 and <= 65535)
            return value;
        MessageBox.Show(this, requirePort
            ? "Enter the exact IPv4 address and port shown by Wireless debugging, for example 192.168.1.50:37123."
            : "Enter the Quest's IPv4 address, with its ADB port if it is not 5555.",
            "Invalid Quest address");
        return null;
    }

    private async Task<bool> RunConnectionStepAsync(string label, string script, params string[] args)
    {
        BeginSetupProgress(label);
        var succeeded = await RunUtilityAsync(label, script, args);
        FinishSetupProgress(succeeded, label);
        if (!succeeded) return false;
        if (script.Equals("Disable-QproWireless.ps1", StringComparison.OrdinalIgnoreCase))
        {
            _wirelessAddress.Clear();
            _connectionMode.SelectedIndex = 0;
        }
        else _wirelessAddress.Text = _environment.GetSavedWirelessTarget() ?? _wirelessAddress.Text;
        AppendLog($"{label} completed. The Hub will use the selected connection for tracking.");
        if (script.Equals("Connect-QproWireless.ps1", StringComparison.OrdinalIgnoreCase))
            _ = RefreshStatusAsync();
        else
            await RefreshStatusAsync();
        return true;
    }

    private bool RocmEnvironmentExists() => File.Exists(Path.Combine(_root, ".venv-rocm", "Scripts", "python.exe"));
    private bool RocmInstalled() => RocmEnvironmentExists()
        && File.Exists(Path.Combine(_root, ".venv-rocm", "qpro-rocm-ready.json"));

    private async Task DetectAmdGpuAsync()
    {
        _amdGpuStatus.Text = "Checking installed GPU against AMD's Windows ROCm 7.2.1 list…";
        try
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var arg in new[] { "-NoProfile", "-Command", "Get-CimInstance Win32_VideoController | ForEach-Object { $_.Name + '|' + $_.PNPDeviceID }" })
                info.ArgumentList.Add(arg);
            using var process = new Process { StartInfo = info };
            if (!process.Start()) throw new InvalidOperationException("GPU query did not start");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                throw new TimeoutException("GPU query timed out");
            }
            var controllers = (await output).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    var separator = line.IndexOf('|');
                    return (Name: separator < 0 ? line : line[..separator],
                        PnpId: separator < 0 ? string.Empty : line[(separator + 1)..]);
                }).ToArray();
            if (process.ExitCode != 0) throw new InvalidOperationException((await error).Trim());
            var supported = new[]
            {
                "Radeon RX 9070 XT", "Radeon RX 9070", "Radeon AI PRO R9700",
                "Radeon RX 9060 XT", "Radeon RX 7900 XTX", "Radeon PRO W7900",
                "Radeon RX 7700",
            };
            var match = controllers.FirstOrDefault(controller => supported.Any(model =>
                System.Text.RegularExpressions.Regex.IsMatch(controller.Name.Trim(),
                    System.Text.RegularExpressions.Regex.Escape(model) + @"(?:\s*\([^)]*\))?\s*$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)));
            _amdGpuDetected = controllers.Any(controller => controller.PnpId.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase)
                || controller.Name.Contains("AMD", StringComparison.OrdinalIgnoreCase)
                || controller.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase));
            _amdGpuSupported = match.Name is not null && Environment.OSVersion.Version.Build >= 22000;
            _amdGpuStatus.Text = match.Name is not null
                ? _amdGpuSupported ? $"Supported AMD GPU detected: {match.Name.Trim()}" : "AMD ROCm 7.2.1 requires Windows 11."
                : _amdGpuDetected
                    ? "This AMD GPU is not listed for bundled ROCm 7.2.1; CPU runtime remains available."
                    : controllers.Any(controller => controller.PnpId.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase)
                        || controller.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                        ? "NVIDIA GPU detected. Use Install runtime for CUDA; AMD ROCm is unavailable."
                        : "No AMD GPU detected. AMD ROCm is unavailable on this PC.";
        }
        catch (Exception error)
        {
            _amdGpuStatus.Text = "GPU detection unavailable. Check AMD's list, then confirm your model below.";
            AppendLog("GPU detection unavailable: " + error.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());
            _amdGpuSupported = false;
            _amdGpuDetected = false;
        }
        if (IsDisposed || Disposing) return;
        _amdManualConfirm.Visible = _amdGpuDetected && !_amdGpuSupported && Environment.OSVersion.Version.Build >= 22000;
        UpdateSetupStepStyles();
        SetSetupButtonsEnabled(true);
    }
}
