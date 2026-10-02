using System.Net;
using System.Net.Sockets;
using Qpro.Shared;

namespace QproFaceTracking.Hub;

internal sealed class CheekPuffCalibrationDialog : Form
{
    private const int HoldMilliseconds = 3000;
    private const int MaximumSampleGap = 500;
    private const int MinimumSamples = 30;
    private readonly string _source;
    private readonly byte _expectedSource;
    private readonly Label _step = new() { AutoSize = true, ForeColor = Color.White };
    private readonly Label _instruction = new() { AutoSize = true, ForeColor = HubForm.Muted };
    private readonly Label _feedStatus = new() { AutoSize = true, ForeColor = HubForm.Warning };
    private readonly Label _values = new() { AutoSize = true, ForeColor = HubForm.Muted };
    private readonly Label _result = new() { AutoSize = true, ForeColor = HubForm.Muted };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Top, Maximum = HoldMilliseconds };
    private readonly DarkButton _capture = Button("Capture relaxed cheeks");
    private readonly DarkButton _restart = Button("Start again");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 50 };
    private readonly List<(float Left, float Right)> _samples = [];
    private UdpClient? _receiver;
    private int _stepIndex;
    private long _lastSampleTick;
    private long _captureStarted;
    private long _lastCaptureSampleTick;
    private bool _capturing;
    private bool _captureInterrupted;
    private bool _wrongSourceSeen;
    private float _leftNeutral;
    private float _rightNeutral;
    private float _leftFull;

    internal CheekPuffCalibrationDialog(string source, string fontName)
    {
        if (source is not (CheekPuffCalibrationProfile.VirtualDesktopSource or CheekPuffCalibrationProfile.SteamLinkSource))
            throw new ArgumentException("Unknown cheek calibration source", nameof(source));
        _source = source;
        _expectedSource = source == CheekPuffCalibrationProfile.SteamLinkSource
            ? CheekPuffTelemetry.SourceSteamLink : CheekPuffTelemetry.SourceVirtualDesktop;
        string sourceName = _expectedSource == CheekPuffTelemetry.SourceSteamLink ? "Steam Link" : "Virtual Desktop";
        Text = $"Calibrate cheek puff — {sourceName}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(540, 440);
        Size = new Size(660, 500);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Font = new Font(fontName, 10);
        BackColor = HubForm.Background;
        ForeColor = Color.White;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(22),
            AutoScroll = true, BackColor = HubForm.Background,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var title = new Label
        {
            Text = "Your relaxed and full cheek puff", AutoSize = true,
            Font = new Font(fontName, 15, FontStyle.Bold), ForeColor = Color.White,
        };
        var intro = new Label
        {
            Text = $"Keep the headset on and {sourceName} connected. Start its Qpro module in VRCFaceTracking, then capture each pose for three seconds. Qpro camera tracking is not needed. Your profile is saved only after all three poses pass.",
            AutoSize = true, ForeColor = HubForm.Muted,
        };
        _step.Font = new Font(fontName, 12, FontStyle.Bold);
        foreach (var label in new[] { title, intro, _step, _instruction, _feedStatus, _values, _result })
        {
            label.Dock = DockStyle.Top;
            label.Margin = new Padding(0, 0, 0, 14);
            layout.Controls.Add(label);
        }
        layout.Controls.Add(_progress);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0, 14, 0, 0) };
        var close = Button("Close");
        close.DialogResult = DialogResult.Cancel;
        actions.Controls.AddRange([_capture, _restart, close]);
        layout.Controls.Add(actions);
        Controls.Add(layout);
        CancelButton = close;
        _capture.Click += (_, _) => StartCapture();
        _restart.Click += (_, _) => Restart();
        _timer.Tick += (_, _) => ReadSamples();
        Shown += (_, _) => StartReceiver();
        void FitText()
        {
            int width = Math.Max(200, layout.ClientSize.Width - layout.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
            foreach (var label in new[] { title, intro, _step, _instruction, _feedStatus, _values, _result })
                label.MaximumSize = new Size(width, 0);
        }
        layout.SizeChanged += (_, _) => FitText();
        FitText();
        FormClosed += (_, _) => { _timer.Stop(); _timer.Dispose(); _receiver?.Dispose(); };
        Restart();
    }

    private static DarkButton Button(string text) => new()
    {
        Text = text, AutoSize = true, BackColor = HubForm.Raised, ForeColor = Color.White,
        Padding = new Padding(12, 6, 12, 6), Margin = new Padding(0, 0, 8, 0),
    };

    private void StartReceiver()
    {
        try
        {
            _receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, CheekPuffTelemetry.Port));
            _timer.Start();
        }
        catch (SocketException error)
        {
            _feedStatus.Text = "Could not open the cheek calibration feed. Close any other Qpro calibration window and try again.";
            _result.Text = error.Message;
            _capture.Enabled = false;
        }
    }

    private void Restart()
    {
        _capturing = false;
        _stepIndex = 0;
        _samples.Clear();
        _progress.Value = 0;
        _leftNeutral = _rightNeutral = _leftFull = 0;
        _result.Text = "Hold each pose comfortably. Keep your jaw relaxed and avoid smiling during the capture.";
        ShowStep();
    }

    private void ShowStep()
    {
        (_step.Text, _instruction.Text, _capture.Text) = _stepIndex switch
        {
            0 => ("1 of 3 · Relax both cheeks", "Let both cheeks rest naturally. When you are steady, press Capture relaxed cheeks.", "Capture relaxed cheeks"),
            1 => ("2 of 3 · Puff only your left cheek", "Use your own left side. Keep your right cheek relaxed, then press Capture left cheek and hold the pose.", "Capture left cheek"),
            _ => ("3 of 3 · Puff only your right cheek", "Use your own right side. Keep your left cheek relaxed, then press Capture right cheek and hold the pose.", "Capture right cheek"),
        };
        _capture.Enabled = _receiver is not null && Environment.TickCount64 - _lastSampleTick <= MaximumSampleGap;
        _restart.Enabled = true;
    }

    private void StartCapture()
    {
        if (_receiver is null || Environment.TickCount64 - _lastSampleTick > MaximumSampleGap) return;
        _samples.Clear();
        _captureStarted = Environment.TickCount64;
        _lastCaptureSampleTick = _captureStarted;
        _captureInterrupted = false;
        _capturing = true;
        _capture.Enabled = false;
        _restart.Enabled = false;
        _result.Text = "Hold still for three seconds…";
        _progress.Value = 0;
    }

    private void ReadSamples()
    {
        if (_receiver is null) return;
        try
        {
            // Drain a bounded batch so a noisy sender cannot block the window.
            for (int count = 0; count < 64 && _receiver.Available > 0; count++)
            {
                IPEndPoint sender = new(IPAddress.Loopback, 0);
                byte[] packet = _receiver.Receive(ref sender);
                if (!IPAddress.IsLoopback(sender.Address) ||
                    !CheekPuffTelemetry.TryParse(packet, out byte source, out float left, out float right)) continue;
                if (source != _expectedSource)
                {
                    _wrongSourceSeen = true;
                    continue;
                }
                _lastSampleTick = Environment.TickCount64;
                _wrongSourceSeen = false;
                _values.Text = $"Live cheek strengths: left {left:0.000} · right {right:0.000}";
                if (_capturing)
                {
                    if (_lastSampleTick - _lastCaptureSampleTick > MaximumSampleGap)
                        _captureInterrupted = true;
                    _lastCaptureSampleTick = _lastSampleTick;
                    _samples.Add((left, right));
                }
            }
        }
        catch (SocketException error)
        {
            _receiver.Dispose();
            _receiver = null;
            _capturing = false;
            _capture.Enabled = false;
            _restart.Enabled = true;
            _feedStatus.Text = "The cheek calibration feed stopped. Close this window and try again.";
            _result.Text = error.Message;
            return;
        }

        long now = Environment.TickCount64;
        bool fresh = _lastSampleTick != 0 && now - _lastSampleTick <= MaximumSampleGap;
        _feedStatus.ForeColor = fresh ? HubForm.Good : HubForm.Warning;
        _feedStatus.Text = fresh ? "Live cheek feed is ready." : _wrongSourceSeen
            ? "The running Qpro module uses the other streaming app. Select its source in the Hub, or restart VRCFaceTracking with the selected module."
            : "Waiting for live cheek values. Keep the headset on and start the selected Qpro module in VRCFaceTracking.";
        if (!_capturing) { _capture.Enabled = fresh; return; }
        if (!fresh || _captureInterrupted)
        {
            RetryStep("The live feed paused during this pose. Reconnect the headset and capture this step again.");
            return;
        }
        _progress.Value = (int)Math.Clamp(now - _captureStarted, 0, HoldMilliseconds);
        if (now - _captureStarted >= HoldMilliseconds) FinishCapture();
    }

    private void FinishCapture()
    {
        _capturing = false;
        if (_samples.Count < MinimumSamples)
        {
            RetryStep("There were too few fresh samples. Keep the headset connected and capture this step again.");
            return;
        }
        float[] left = _samples.Select(sample => sample.Left).Order().ToArray();
        float[] right = _samples.Select(sample => sample.Right).Order().ToArray();
        float maximumSpread = _stepIndex == 0 ? 0.06f : 0.10f;
        bool stable = _stepIndex switch
        {
            0 => Spread(left) <= maximumSpread && Spread(right) <= maximumSpread,
            1 => Spread(left) <= maximumSpread,
            _ => Spread(right) <= maximumSpread,
        };
        if (!stable)
        {
            RetryStep("The cheek strength changed too much during the hold. Settle into the pose, then capture this step again.");
            return;
        }
        if (_stepIndex > 0)
        {
            float activeGain = _stepIndex == 1 ? Median(left) - _leftNeutral : Median(right) - _rightNeutral;
            float otherGain = _stepIndex == 1 ? Median(right) - _rightNeutral : Median(left) - _leftNeutral;
            if (activeGain < CheekPuffCalibrationProfile.MinimumRange)
            {
                RetryStep("This cheek did not differ enough from relaxed cheeks. Use a comfortable, stronger puff on the requested side, then capture this step again.");
                return;
            }
            if (otherGain >= activeGain * 0.75f)
            {
                RetryStep("Both cheeks puffed together. Relax the other cheek and puff only the requested side, then capture this step again.");
                return;
            }
        }
        if (_stepIndex == 0)
        {
            _leftNeutral = Median(left);
            _rightNeutral = Median(right);
        }
        else if (_stepIndex == 1) _leftFull = Median(left);
        else
        {
            CheekPuffCalibration calibration = new(_leftNeutral, _leftFull, _rightNeutral, Median(right));
            if (!CheekPuffCalibrationProfile.IsValid(calibration))
            {
                Restart();
                _result.Text = "The full cheek puff did not differ enough from relaxed cheeks. Start again and use a comfortable, stronger puff on each side. Your existing profile has been kept.";
                return;
            }
            try
            {
                CheekPuffCalibrationProfile.Save(_source, calibration);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                RetryStep("Could not save the calibration: " + error.Message + " Your existing profile has been kept.");
            }
            return;
        }
        _stepIndex++;
        _progress.Value = 0;
        _result.Text = "Pose captured. Relax briefly, then get ready for the next cheek.";
        ShowStep();
    }

    private void RetryStep(string message)
    {
        _capturing = false;
        _samples.Clear();
        _progress.Value = 0;
        _result.Text = message;
        ShowStep();
    }

    private static float Median(float[] sorted) => sorted.Length % 2 == 0
        ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2
        : sorted[sorted.Length / 2];

    private static float Spread(float[] sorted) => sorted[sorted.Length * 3 / 4] - sorted[sorted.Length / 4];
}
