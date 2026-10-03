using System.Diagnostics;
using System.Text.Json;

namespace QproFaceTracking.Hub;

internal sealed partial class HubForm
{
    private readonly CheckBox _hybridHands = FeatureToggle("Experimental hands + controllers", false);
    private readonly CheckBox _controllerTouchpad = FeatureToggle("Experimental Touch Pro thumb-rest input", false);
    private readonly ComboBox _touchpadMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Label _handsStatus = new() { AutoSize = true, ForeColor = Muted, Tag = "responsive-info" };
    private readonly DarkButton _installHandsButton = SetupButton("Install hand/controller components");
    private readonly DarkButton _removeHandsButton = SetupButton("Uninstall controller add-on");
    private readonly DarkButton _checkHandsButton = SecondaryButton("Check hand/controller compatibility");
    private bool _handsReady;
    private bool _touchpadReady;

    private string ControllerComponentsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QproFaceTracking", "hands");
    private string ControllerAddonRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QproFaceTracking", "controller-addon");

    private void InitializeControllerInputUi()
    {
        _touchpadMode.Items.AddRange(["Trackpad", "Relative joystick", "Swipe", "Desktop mouse"]);
        _touchpadMode.SelectedIndex = 0;
        ConfigureDropDown(_touchpadMode);
        _hybridHands.CheckedChanged += (_, _) => { UpdateToggleStyle(_hybridHands); UpdateControllerInputAvailability(); };
        _controllerTouchpad.CheckedChanged += (_, _) => { UpdateToggleStyle(_controllerTouchpad); UpdateControllerInputAvailability(); };
        UpdateToggleStyle(_hybridHands);
        UpdateToggleStyle(_controllerTouchpad);
        _installHandsButton.Click += async (_, _) =>
        {
            if (Process.GetProcessesByName("vrserver").Length != 0)
            {
                MessageBox.Show(this, "Close SteamVR before installing these optional components, then reopen SteamVR when setup finishes.",
                    "Close SteamVR first", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            await RunSetupStepAsync("Hand/controller components", "controller-input.ps1",
                "Optional components installed.", "Reopen SteamVR through Virtual Desktop, then run Check hand/controller compatibility. These features remain experimental.");
        };
        _removeHandsButton.Click += async (_, _) =>
        {
            if (Process.GetProcessesByName("vrserver").Length != 0)
            {
                MessageBox.Show(this, "Close SteamVR before uninstalling the controller add-on. Reopen SteamVR afterward to reload the normal controller profile.",
                    "Close SteamVR first", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            await RunUtilityAsync("Uninstall controller add-on", "controller-input.ps1", "-Action", "uninstall");
        };
        _checkHandsButton.Click += async (_, _) =>
            await RunUtilityAsync("Hand/controller compatibility", "controller-input.ps1", "-Action", "check");
        UpdateControllerInputAvailability();
    }

    private string TouchpadModeValue() => _touchpadMode.SelectedIndex switch
    {
        1 => "joystick", 2 => "swipe", 3 => "mouse", _ => "trackpad",
    };

    private void UpdateControllerInputAvailability()
    {
        bool idle = !_starting && !_stopping && !_utilityActionRunning && !_setupActionRunning &&
            !_datasetOperationBusy && !_trackingProcesses.Any(process => !process.HasExited);
        bool supportedSource = !_environment.SteamLinkSelected;
        _hybridHands.Enabled = _controllerTouchpad.Enabled = idle && supportedSource;
        _touchpadMode.Enabled = idle && supportedSource && _controllerTouchpad.Checked;
        _installHandsButton.Enabled = idle && supportedSource;
        _removeHandsButton.Enabled = idle && Directory.Exists(ControllerAddonRoot);
        _checkHandsButton.Enabled = idle && supportedSource && BackendReady();
        if (idle)
        {
            if (!supportedSource)
            {
                _hybridHands.Checked = _controllerTouchpad.Checked = false;
                _handsStatus.Text = "Experimental controller inputs currently require Virtual Desktop. Steam Link compatibility has not been verified.";
            }
            else
                _handsStatus.Text = "Optional, off by default. Check compatibility before use. Finger routing and thumb-rest input require a rooted Quest Pro; supported runtime versions are listed in the controller guide.";
        }
    }

    private void AddControllerPrerequisites(List<string> missing)
    {
        if (!_hybridHands.Checked && !_controllerTouchpad.Checked) return;
        if (_environment.SteamLinkSelected) missing.Add("Virtual Desktop for the experimental hand/controller features");
        if (_hybridHands.Checked && !File.Exists(Path.Combine(ControllerComponentsRoot, "ready.json")))
            missing.Add("the optional hand components — close SteamVR and use Install hand/controller components");
        if (_controllerTouchpad.Checked && !File.Exists(Path.Combine(ControllerAddonRoot, "qpro-owner.json")))
            missing.Add("the controller add-on — close SteamVR and use Install hand/controller components");
    }

    private void StartControllerInput()
    {
        if (!_hybridHands.Checked && !_controllerTouchpad.Checked) return;
        var arguments = new List<string> { "-Action", "run", "-StopFile", _stopFile };
        if (_hybridHands.Checked) arguments.Add("-Hands");
        if (_controllerTouchpad.Checked) arguments.Add("-Touchpad");
        arguments.AddRange(["-Mode", TouchpadModeValue()]);
        _handsStatus.Text = "Checking controller compatibility; waiting for valid input…";
        _handsReady = _touchpadReady = false;
        if (_controllerTouchpad.Checked && TouchpadModeValue() == "mouse")
            AppendLog("Desktop mouse mode selected: thumb-rest gestures can move the Windows pointer. Stop tracking disables this input.");
        StartManaged("Hand/controller input", "controller-input.ps1", arguments.ToArray());
    }

    private void ObserveControllerInput(string label, string line)
    {
        if (label != "Hand/controller input" || IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => ObserveControllerInput(label, line)); }
            catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated)
            {
                // The form can close between the process callback and UI dispatch.
            }
            return;
        }
        if (line.StartsWith("HANDS_READY ", StringComparison.Ordinal))
        {
            _handsReady = true;
            _handsStatus.Text = "Valid optical fingers are routed alongside the physical controllers.";
        }
        else if (line.StartsWith("TOUCHPAD_READY ", StringComparison.Ordinal))
        {
            _touchpadReady = true;
            _handsStatus.Text = "Valid thumb-rest packets are being forwarded to SteamVR. Confirm the controller add-on loaded and check your app's input binding.";
        }
        else if (line.StartsWith("CONTROLLER_CLEANUP ", StringComparison.Ordinal))
        {
            _handsReady = _touchpadReady = false;
            try
            {
                using var result = JsonDocument.Parse(line["CONTROLLER_CLEANUP ".Length..]);
                bool pending = result.RootElement.TryGetProperty("restoration", out var restoration) && restoration.GetString() == "still-running";
                bool failed = restoration.ValueKind == JsonValueKind.String && restoration.GetString() == "stop-file-failed" ||
                    result.RootElement.TryGetProperty("reader", out var reader) && reader.GetString() == "unconfirmed";
                _handsStatus.Text = pending ? "Waiting for controller adapters to restore. Keep the Hub open."
                    : failed ? "Controller cleanup was not confirmed. Check Activity before restarting."
                    : "Controller input stopped. Reopen SteamVR after uninstalling the add-on to reload the normal profile.";
                _handsStatus.ForeColor = pending || failed ? Warning : Muted;
            }
            catch (JsonException) { _handsStatus.Text = "Controller cleanup needs attention; check Activity."; }
        }
        bool inputReadyLine = line.StartsWith("HANDS_READY ", StringComparison.Ordinal) || line.StartsWith("TOUCHPAD_READY ", StringComparison.Ordinal);
        if (inputReadyLine && (!_hybridHands.Checked || _handsReady) && (!_controllerTouchpad.Checked || _touchpadReady) && !_stopping)
        {
            _runStatus.Text = "● Selected overrides active";
            _runStatus.ForeColor = Good;
        }
    }
}
