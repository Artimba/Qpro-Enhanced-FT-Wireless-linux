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

internal sealed partial class HubForm : Form
{
    private readonly string _root;
    private readonly HubEnvironment _environment;
    private readonly HubScriptFactory _scripts;
    private readonly string _stopFile;
    private readonly CheckBox _gaze = FeatureToggle("Independent eye gaze + convergence", true);
    private readonly CheckBox _tongue = FeatureToggle("Experimental tongue tracking", false);
    private readonly CheckBox _pupil = FeatureToggle("Experimental relative pupil dilation (eye cameras)", false);
    private readonly CheckBox _cameraPreview = FeatureToggle("Preview tracking cameras", true);
    private readonly ComboBox _eyeProfiles = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _tongueModels = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _quickDatasets = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly ComboBox _fullDatasets = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly ComboBox _quickRecordedDatasets = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly ComboBox _fullRecordedDatasets = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly Label _quickQueueStatus = new() { AutoSize = true, ForeColor = Muted };
    private readonly Label _fullQueueStatus = new() { AutoSize = true, ForeColor = Muted };
    private readonly DarkProgressBar _trainingProgress = new() { Dock = DockStyle.Fill, Height = 18, Margin = new Padding(4, 5, 4, 2) };
    private readonly Label _trainingProgressStatus = new() { Text = "Training idle — select a recorded dataset when ready.", AutoSize = true, ForeColor = Muted, Margin = new Padding(4, 2, 4, 3) };
    private readonly TableLayoutPanel _trainingProgressContainer = new() { Visible = false };
    private readonly ListBox _modelList = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false };
    private readonly Label _modelEmpty = new() { Text = "No paired tongue models found. Extract the complete release or import a .qptonguemodel package.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Muted, Padding = new Padding(24) };
    private readonly Label _tongueModelNote = new() { AutoSize = true, MaximumSize = new Size(650, 0), ForeColor = Muted, Margin = new Padding(24, 2, 0, 4) };
    private readonly ComboBox _fps = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 76 };
    private readonly ComboBox _pupilSensitivity = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly DarkSlider _smoothing = new() { Minimum = 0, Maximum = 100, Value = 55, Width = 180, Height = 30 };
    private readonly ComboBox _visibilityMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 265 };
    private readonly Label _usbStatus = StatusLabel();
    private readonly Label _steamStatus = StatusLabel();
    private readonly Label _vrcftStatus = StatusLabel();
    private readonly Label _bridgeStatus = StatusLabel();
    private readonly Label _runtimeStatus = StatusLabel();
    private readonly Label _gazeStatus = StatusLabel();
    private readonly Label _inferenceStatus = StatusLabel();
    private readonly Label _pupilStatus = StatusLabel();
    private readonly Label _setupRuntimeStatus = SetupStatusLabel();
    private readonly Label _setupBridgeStatus = SetupStatusLabel();
    private readonly Label _setupGazeStatus = SetupStatusLabel();
    private readonly DarkButton _setupRuntimeButton = SetupButton("Install runtime");
    private readonly DarkButton _setupBridgeButton = SetupButton("Install bridge");
    private readonly DarkButton _uninstallBridgeButton = SetupButton("Uninstall bridge");
    private readonly DarkButton _setupGazeButton = SetupButton("Prepare gaze");
    private readonly DarkProgressBar _setupProgress = new() { Dock = DockStyle.Fill, Height = 18, Margin = new Padding(4, 5, 4, 2) };
    private readonly Label _setupProgressStatus = new() { Text = "Setup idle.", AutoSize = true, ForeColor = Muted, Margin = new Padding(4, 2, 4, 3), Tag = "responsive-info" };
    private readonly TableLayoutPanel _setupProgressContainer = new();
    private readonly Label _runStatus = new() { Text = "● Idle — stock tracking is untouched", AutoSize = true, ForeColor = Good };
    private readonly RichTextBox _log = new() { ReadOnly = true, BackColor = Inset, ForeColor = Color.WhiteSmoke, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, ScrollBars = RichTextBoxScrollBars.Vertical, HideSelection = false };
    private readonly Button _start = PrimaryButton("Start tracking");
    private readonly Button _stop = SecondaryButton("Stop tracking");
    private readonly List<Process> _trackingProcesses = [];
    private SoundPlayer? _soundPlayer;
    private bool _stopping;
    private bool _starting;
    private bool _gazeStartupInProgress;
    private bool _gazeFailureHandled;
    private bool _datasetOperationBusy;
    private CancellationTokenSource? _startCancellation;
    private bool _statusRefreshBusy;
    private bool _setupPulseOn;
    private bool _setupActionRunning;
    private bool _adjacentModelsImported;
    private int _trainingStage;
    private int _trainingStageCount = 2;

    internal static readonly Color Background = Color.FromArgb(33, 57, 66);      // #213942
    internal static readonly Color Panel = Color.FromArgb(25, 47, 55);           // #192F37
    internal static readonly Color Inset = Color.FromArgb(17, 35, 42);
    internal static readonly Color Raised = Color.FromArgb(44, 75, 85);          // #2C4B55
    internal static readonly Color Selected = Color.FromArgb(52, 92, 101);       // #345C65
    internal static readonly Color RaisedHover = Color.FromArgb(59, 98, 108);    // #3B626C
    internal static readonly Color Border = Color.FromArgb(82, 124, 132);        // #527C84
    internal static readonly Color Accent = Color.FromArgb(114, 208, 198);       // #72D0C6
    internal static readonly Color Muted = Color.FromArgb(201, 220, 222);        // #C9DCDE
    internal static readonly Color DisabledText = Color.FromArgb(167, 189, 195);
    internal static readonly Color Good = Color.FromArgb(145, 228, 184);         // #91E4B8
    internal static readonly Color Warning = Color.FromArgb(240, 208, 138);      // #F0D08A
    internal static readonly Color Bad = Color.FromArgb(243, 141, 133);          // #F38D85
    private static readonly string UiFontName = FontFamily.Families.Any(font => font.Name.Equals("Lexend", StringComparison.OrdinalIgnoreCase)) ? "Lexend" : "Segoe UI";

    public HubForm(string root)
    {
        _root = root;
        _environment = new HubEnvironment(root);
        _cameraPreview.Checked = _environment.CameraPreviewEnabled;
        _gaze.Checked = _environment.IndependentGazeEnabled;
        _scripts = new HubScriptFactory(root, _environment);
        _stopFile = Path.Combine(root, ".qpro-hub-stop");
        Text = "QproFaceTracking V2.0 Hub";
        MinimumSize = new Size(780, 580);
        Size = new Size(1140, 850);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        BackColor = Background;
        ForeColor = Color.WhiteSmoke;
        Font = new Font(UiFontName, 10F);
        StartPosition = FormStartPosition.CenterScreen;
        HandleCreated += (_, _) => EnableDarkTitleBar(Handle);
        Shown += (_, _) =>
        {
            var workArea = Screen.FromHandle(Handle).WorkingArea;
            if (Width <= workArea.Width && Height <= workArea.Height) return;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, workArea.Width), Math.Min(MinimumSize.Height, workArea.Height));
            Size = new Size(Math.Min(Width, workArea.Width), Math.Min(Height, workArea.Height));
            Location = new Point(workArea.Left + (workArea.Width - Width) / 2, workArea.Top + (workArea.Height - Height) / 2);
        };

        Controls.Add(BuildLayout());
        Shown += (_, _) =>
        {
            try { _environment.MarkOpened(); }
            catch (Exception error) { AppendLog("Could not save first-launch state: " + error.Message); }
        };
        InitializeIntegratedSetup();
        _start.Click += async (_, _) => await StartTrackingAsync();
        _stop.Click += async (_, _) => await StopTrackingAsync();
        _gaze.CheckedChanged += (_, _) =>
        {
            try { _environment.SelectIndependentGaze(_gaze.Checked); }
            catch (Exception error) { AppendLog("Could not save independent gaze preference: " + error.Message); }
            UpdateControlState();
        };
        _tongue.CheckedChanged += (_, _) => UpdateControlState();
        _pupil.CheckedChanged += (_, _) => UpdateControlState();
        _cameraPreview.CheckedChanged += (_, _) =>
        {
            try { _environment.SelectCameraPreview(_cameraPreview.Checked); }
            catch (Exception error) { AppendLog("Could not save camera preview preference: " + error.Message); }
            UpdateToggleStyle(_cameraPreview);
        };
        _tongueModels.SelectedIndexChanged += (_, _) => UpdateTongueModelNote();
        _fps.Items.AddRange(["12", "15", "18", "20", "24", "30", "36", "48", "60", "72"]);
        _fps.SelectedItem = "24";
        _pupilSensitivity.Items.AddRange(Enumerable.Range(0, 11)
            .Select(index => $"{1.0 + index * 0.2:0.0}×" + (index == 2 ? " (Balanced)" : ""))
            .ToArray());
        _pupilSensitivity.SelectedIndex = 2;
        _visibilityMode.Items.AddRange(["Weighted camera + native", "Camera only", "Native only", "Conservative agreement"]);
        _visibilityMode.SelectedIndex = 0;
        ConfigureDropDown(_eyeProfiles);
        ConfigureDropDown(_tongueModels);
        ConfigureDropDown(_fps);
        ConfigureDropDown(_pupilSensitivity);
        ConfigureDropDown(_visibilityMode);
        ConfigureDropDown(_quickDatasets);
        ConfigureDropDown(_fullDatasets);
        ConfigureDropDown(_quickRecordedDatasets);
        ConfigureDropDown(_fullRecordedDatasets);
        ConfigureModelList(_modelList);
        UpdateToggleStyle(_gaze);
        UpdateToggleStyle(_tongue);
        UpdateToggleStyle(_pupil);
        UpdateToggleStyle(_cameraPreview);
        SetStatus(_inferenceStatus, StatusKind.Warning, "Idle");
        SetStatus(_pupilStatus, StatusKind.Warning, "Idle");
        _gaze.CheckedChanged += (_, _) => UpdateToggleStyle(_gaze);
        _tongue.CheckedChanged += (_, _) => UpdateToggleStyle(_tongue);
        _pupil.CheckedChanged += (_, _) => UpdateToggleStyle(_pupil);
        FormClosing += OnClosing;

        ReloadProfiles();
        _ = RefreshStatusAsync();
        var timer = new System.Windows.Forms.Timer { Interval = 2500 };
        timer.Tick += async (_, _) => await RefreshStatusAsync();
        timer.Start();
        var pulseTimer = new System.Windows.Forms.Timer { Interval = 550 };
        pulseTimer.Tick += (_, _) => { _setupPulseOn = !_setupPulseOn; UpdateSetupStepStyles(); };
        pulseTimer.Start();
        var setupProgressTimer = new System.Windows.Forms.Timer { Interval = 45 };
        setupProgressTimer.Tick += (_, _) => { if (_setupProgress.IsIndeterminate) _setupProgress.AdvanceAnimation(); };
        setupProgressTimer.Start();
        FormClosed += (_, _) =>
        {
            timer.Stop(); timer.Dispose();
            pulseTimer.Stop(); pulseTimer.Dispose();
            setupProgressTimer.Stop(); setupProgressTimer.Dispose();
            _soundPlayer?.Dispose();
            foreach (var process in _trackingProcesses) process.Dispose();
        };
        AppendLog("Hub ready. Nothing is applied until you press Start tracking.");
    }
}
