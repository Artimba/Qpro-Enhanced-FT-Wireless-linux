using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Qpro.Shared;
using VRCFaceTracking;
using VRCFaceTracking.Core.Params.Expressions;

namespace Qpro.GazeBridge;

[SupportedOSPlatform("windows")]

public sealed class TrackingModule : ExtTrackingModule
{
    private const string MapName = "VirtualDesktop.BodyState";
    private const int StateBytes = 360;
    private const int ExpressionOffset = 4;
    private const int ExpressionCount = 70;
    private const int GazePort = 27275;
    private const int GazePacketBytes = 24;
    private const long GazeTimeoutMs = 250;
    private const int TonguePort = 27276;
    private const int TonguePacketBytes = 56;
    private const long TongueTimeoutMs = 300;
    private const int PupilPort = 27277;
    private const int PupilPacketBytes = 16;
    private const long PupilTimeoutMs = 350;
    private const int LabelPort = 27274;
    private const long SteamFaceTimeoutMs = 500;
    private static readonly string CheekPuffModePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QproFaceTracking", "config", "cheek-puff-mode.txt");
    private static readonly string CheekSuckModePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QproFaceTracking", "config", "cheek-suck-mode.txt");
    private static readonly (int Source, int[] Targets)[] ExpressionMap =
    [
        (8, [67]), (9, [66]),
        (10, [65]), (11, [64]), (24, [22]), (25, [24]),
        (26, [23]), (27, [25]), (30, [61]), (31, [60]),
        (34, [39]), (35, [37]),
        (36, [38]), (37, [36]), (38, [69]), (39, [68]),
        (40, [41, 43]), (41, [40, 42]), (42, [63]), (43, [62]),
        (44, [33]), (46, [32]), (48, [71]), (49, [70]),
        (50, [29]), (51, [51]), (52, [50]), (53, [53, 55]),
        (54, [52, 54]), (55, [49]), (56, [48])
    ];

    private readonly byte[] _first = new byte[StateBytes];
    private readonly byte[] _second = new byte[StateBytes];
    private MemoryMappedFile? _map;
    private MemoryMappedViewAccessor? _view;
    private UdpClient? _gazeSocket;
    private UdpClient? _tongueSocket;
    private UdpClient? _pupilSocket;
    private UdpClient? _steamLabelSocket;
    private SteamOscSource? _steamSource;
    private bool _useSteamLink;
    private long _nextSteamLabelTick;
    private long _nextSteamSchemaTick;
    private long _steamLabelSequence;
    private long _steamSourceChangeSequence;
    private long _lastSteamFaceTick;
    private long _nextSteamFaceWarningTick;
    private bool _steamFaceUnavailableReported;
    private bool _needsEye;
    private bool _needsExpression;
    private CheekPuffMode _cheekPuffMode = CheekPuffMode.Strong;
    private readonly CheekPuffTracker _cheekPuffTracker = new();
    private long _nextCheekPuffModeCheckTick;
    private CheekSuckMode _cheekSuckMode = CheekSuckMode.Strong;
    private readonly CheekSuckTracker _cheekSuckTracker = new();
    private long _nextCheekSuckModeCheckTick;
    private readonly SmirkTracker _smirkTracker = new();
    private EyebrowSettings _eyebrowSettings = EyebrowPreference.Default;
    private long _nextEyebrowSettingsCheckTick;
    private long _lastGazeTick;
    private float _leftGazeX;
    private float _leftGazeY;
    private float _rightGazeX;
    private float _rightGazeY;
    private byte _gazeFlags;
    private readonly BlinkGazeHold _leftBlinkGaze = new();
    private readonly BlinkGazeHold _rightBlinkGaze = new();
    private readonly float[] _tongueValues = new float[12];
    private bool _tongueEnabled;
    private bool _tongueDirty;
    private bool _customTongueWasApplied;
    private long _lastTongueTick;
    private long _lastPupilTick;
    private byte _pupilFlags;
    private float _leftPupilMm;
    private float _rightPupilMm;

    // One module owns both VRCFT slots for the selected headset streaming
    // source and overlays fresh Qpro gaze, tongue, and pupil packets.
    public override (bool SupportsEye, bool SupportsExpression) Supported => (true, true);

    public override (bool eyeSuccess, bool expressionSuccess) Initialize(
        bool eyeAvailable,
        bool expressionAvailable)
    {
        _needsEye = eyeAvailable;
        _needsExpression = expressionAvailable;
        _leftBlinkGaze.Reset();
        _rightBlinkGaze.Reset();
        _useSteamLink = ReadSteamLinkSelection();
        ModuleInformation = new ModuleMetadata
        {
            Name = _useSteamLink
                ? "Quest Pro + Steam Link face/tongue/pupil"
                : "Quest Pro gaze + Virtual Desktop face/tongue/pupil v0.7"
        };

        if (_useSteamLink)
        {
            try
            {
                _steamSource = new SteamOscSource();
                _steamLabelSocket = new UdpClient(AddressFamily.InterNetwork);
                _steamLabelSocket.Connect(IPAddress.Loopback, LabelPort);
            }
            catch (SocketException error)
            {
                Logger.LogError(error,
                    "Could not bind Steam Link OSC port 9015. Close any other Steam Link VRCFT module and restart VRCFaceTracking.");
                _steamSource?.Dispose();
                _steamSource = null;
                _steamLabelSocket?.Dispose();
                _steamLabelSocket = null;
                return (false, false);
            }
        }

        try
        {
            _gazeSocket = new UdpClient(new IPEndPoint(IPAddress.Loopback, GazePort));
            _gazeSocket.Client.Blocking = false;
        }
        catch (SocketException error)
        {
            Logger.LogError(error, "Could not bind the local Quest Pro gaze port {Port}", GazePort);
        }

        try
        {
            _tongueSocket = new UdpClient(new IPEndPoint(IPAddress.Loopback, TonguePort));
            _tongueSocket.Client.Blocking = false;
        }
        catch (SocketException error)
        {
            Logger.LogError(error, "Could not bind the local Quest Pro tongue port {Port}", TonguePort);
        }

        try
        {
            _pupilSocket = new UdpClient(new IPEndPoint(IPAddress.Loopback, PupilPort));
            _pupilSocket.Client.Blocking = false;
        }
        catch (SocketException error)
        {
            Logger.LogError(error, "Could not bind the local Quest Pro pupil port {Port}", PupilPort);
        }

        if (!_useSteamLink)
            TryOpenMap();
        Logger.LogInformation(
            "Quest Pro combined bridge initialized: source={Source}, eye={Eye}, face={Face}; Qpro gaze freshness={Timeout} ms",
            _useSteamLink ? "Steam Link OSC 9015" : "Virtual Desktop", _needsEye,
            _needsExpression, GazeTimeoutMs);
        return (_needsEye, _needsExpression);
    }

    public override void Update()
    {
        ReceiveGaze();
        ReceiveTongue();
        ReceivePupil();
        if (_useSteamLink)
        {
            UpdateSteamLink();
            Thread.Sleep(5);
            return;
        }
        if (!TryReadState())
        {
            Thread.Sleep(10);
            return;
        }

        Span<float> expressions = stackalloc float[ExpressionCount];
        for (int index = 0; index < ExpressionCount; ++index)
            expressions[index] = BitConverter.ToSingle(_second, ExpressionOffset + index * 4);

        if (_needsEye)
            UpdateEyes(expressions);
        if ((_needsEye || _needsExpression) && _second[1] != 0)
            UpdateBrowExpressions(expressions);
        if (_needsExpression && _second[1] != 0)
            UpdateMouth(expressions, _second[1], NativeFaceSource.VirtualDesktop);
        Thread.Sleep(5);
    }

    public override void Teardown()
    {
        _smirkTracker.Reset();
        _leftBlinkGaze.Reset();
        _rightBlinkGaze.Reset();
        _gazeSocket?.Dispose();
        _gazeSocket = null;
        _tongueSocket?.Dispose();
        _tongueSocket = null;
        _pupilSocket?.Dispose();
        _pupilSocket = null;
        _steamSource?.Dispose();
        _steamSource = null;
        _steamLabelSocket?.Dispose();
        _steamLabelSocket = null;
        _view?.Dispose();
        _view = null;
        _map?.Dispose();
        _map = null;
    }

    private void TryOpenMap()
    {
        if (_view is not null)
            return;
        try
        {
            _map = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
            _view = _map.CreateViewAccessor(0, StateBytes, MemoryMappedFileAccess.Read);
        }
        catch (FileNotFoundException)
        {
            _map?.Dispose();
            _map = null;
        }
    }

    private bool TryReadState()
    {
        TryOpenMap();
        if (_view is null)
            return false;
        try
        {
            _view.ReadArray(0, _first, 0, StateBytes);
            Thread.MemoryBarrier();
            _view.ReadArray(0, _second, 0, StateBytes);
            return _first.AsSpan().SequenceEqual(_second);
        }
        catch (ObjectDisposedException)
        {
            _view = null;
            _map = null;
            return false;
        }
    }

    private static bool ReadSteamLinkSelection()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QproFaceTracking", "config", "tracking-source.txt");
        try
        {
            return File.Exists(path) && File.ReadAllText(path).Trim().Equals(
                "steam-link", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private void UpdateSteamLink()
    {
        if (_steamSource is null)
            return;
        _steamSource.Poll();
        long now = Environment.TickCount64;
        bool faceFresh = _steamSource.HasFreshFace(now, SteamFaceTimeoutMs);
        ReadOnlySpan<float> expressions = _steamSource.Expressions;
        bool upperFaceAvailable = faceFresh && _steamSource.HasAvailableUpperFace(now, SteamFaceTimeoutMs);
        bool lowerFaceAvailable = faceFresh && _steamSource.HasAvailableLowerFace(now, SteamFaceTimeoutMs);
        if (faceFresh)
            ReportSteamFaceAvailability(now, expressions, upperFaceAvailable, lowerFaceAvailable);
        Span<float> neutral = stackalloc float[ExpressionCount];
        neutral.Clear();
        ReadOnlySpan<float> upperValues = upperFaceAvailable ? expressions : neutral;
        ReadOnlySpan<float> lowerValues = lowerFaceAvailable ? expressions : neutral;
        if (_needsEye)
            UpdateSteamEyes(upperValues, upperFaceAvailable, now);
        if (_needsEye || _needsExpression)
            UpdateBrowExpressions(upperValues);
        if (_needsExpression)
            UpdateMouth(lowerValues, 3, NativeFaceSource.SteamLink);
        if (faceFresh)
            PublishSteamLabels(now, expressions);
    }

    private void ReportSteamFaceAvailability(long now, ReadOnlySpan<float> expressions,
        bool upperFaceAvailable, bool lowerFaceAvailable)
    {
        if (_steamSource is null)
            return;
        bool bothCapabilitiesFresh = _steamSource.HasFreshUpperFaceCapability(now, SteamFaceTimeoutMs) &&
            _steamSource.HasFreshLowerFaceCapability(now, SteamFaceTimeoutMs);
        bool allWeightsZero = true;
        foreach (float weight in expressions)
        {
            if (weight == 0.0f) continue;
            allWeightsZero = false;
            break;
        }
        if (bothCapabilitiesFresh && !upperFaceAvailable && !lowerFaceAvailable &&
            allWeightsZero && _steamSource.HasFreshGaze(now, SteamFaceTimeoutMs))
        {
            if (now < _nextSteamFaceWarningTick) return;
            Logger.LogWarning(
                "Steam Link OSC gaze is live, but upper/lower face capability is OFF and all face weights are zero. " +
                "Enable eye and face tracking on the Quest Pro, then in SteamVR > Steam Link enable OSC and " +
                "'Share face tracking data to other apps on this PC via OSC' with output port 9015 (ALT). " +
                "Reconnect Steam Link if those settings were already enabled.");
            _steamFaceUnavailableReported = true;
            _nextSteamFaceWarningTick = now + 30_000;
        }
        else if (_steamFaceUnavailableReported && upperFaceAvailable && lowerFaceAvailable)
        {
            Logger.LogInformation("Steam Link upper and lower face capability is available again.");
            _steamFaceUnavailableReported = false;
            _nextSteamFaceWarningTick = 0;
        }
    }

    private void UpdateSteamEyes(ReadOnlySpan<float> values,
        bool upperFaceAvailable, long now)
    {
        bool customFresh = _lastGazeTick != 0 && now - _lastGazeTick <= GazeTimeoutMs;
        float leftX = 0, leftY = 0, rightX = 0, rightY = 0;
        bool steamGazeFresh = _steamSource is not null &&
            _steamSource.HasFreshGaze(now, SteamFaceTimeoutMs) &&
            _steamSource.TryGetGaze(now, out leftX, out leftY,
                out rightX, out rightY);

        GazePoint? left = customFresh && (_gazeFlags & 1) != 0
            ? new GazePoint(_leftGazeX, _leftGazeY)
            : steamGazeFresh ? new GazePoint(leftX, leftY) : null;
        GazePoint? right = customFresh && (_gazeFlags & 2) != 0
            ? new GazePoint(_rightGazeX, _rightGazeY)
            : steamGazeFresh ? new GazePoint(rightX, rightY) : null;
        ApplyBlinkGaze(values, upperFaceAvailable, left, right, now);

        UnifiedTracking.Data.Eye.Left.Openness = 1.0f - Math.Clamp(
            values[12] + values[12] * values[28], 0.0f, 1.0f);
        UnifiedTracking.Data.Eye.Right.Openness = 1.0f - Math.Clamp(
            values[13] + values[13] * values[29], 0.0f, 1.0f);
        bool pupilFresh = _lastPupilTick != 0 && now - _lastPupilTick <= PupilTimeoutMs;
        UnifiedTracking.Data.Eye.Left.PupilDiameter_MM =
            pupilFresh && (_pupilFlags & 1) != 0 ? _leftPupilMm : 5.0f;
        UnifiedTracking.Data.Eye.Right.PupilDiameter_MM =
            pupilFresh && (_pupilFlags & 2) != 0 ? _rightPupilMm : 5.0f;
        UnifiedTracking.Data.Eye._minDilation = 2.0f;
        UnifiedTracking.Data.Eye._maxDilation = 8.0f;
        UpdateEyeExpressions(values);
    }

    private void PublishSteamLabels(long now, ReadOnlySpan<float> expressions)
    {
        if (_steamLabelSocket is null || _steamSource is null)
            return;
        long qpc = Stopwatch.GetTimestamp();
        try
        {
            if (qpc >= _nextSteamSchemaTick)
            {
                SendSteamLabel(new
                {
                    V = 1, Type = "schema", Names = SteamOscSource.ExpressionNames
                });
                _nextSteamSchemaTick = qpc + Stopwatch.Frequency * 2;
            }
            if (qpc < _nextSteamLabelTick)
                return;
            _nextSteamLabelTick = qpc + Stopwatch.Frequency / 60;
            if (_steamSource.LastFaceTick != _lastSteamFaceTick)
            {
                _lastSteamFaceTick = _steamSource.LastFaceTick;
                ++_steamSourceChangeSequence;
            }
            bool gazeValid = _steamSource.HasFreshGaze(now, SteamFaceTimeoutMs);
            SendSteamLabel(new
            {
                V = 1,
                Type = "sample",
                Sequence = ++_steamLabelSequence,
                Qpc = qpc,
                QpcFrequency = Stopwatch.Frequency,
                UtcUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                SourceChangeSequence = _steamSourceChangeSequence,
                SourceUnchangedMs = (double)(now - _steamSource.LastFaceTick),
                Values = expressions.ToArray(),
                // Bit 1 is Virtual Desktop's alternate tongue layout, which
                // Steam Link's named XR_FB weights do not use.
                FaceFlags = _steamSource.HasAvailableLowerFace(now, SteamFaceTimeoutMs) ? 1 : 0,
                IsEyeFollowingBlendshapesValid = _steamSource.HasAvailableUpperFace(now, SteamFaceTimeoutMs),
                LeftEyeIsValid = gazeValid,
                RightEyeIsValid = gazeValid,
                LeftEyeOrientation = new float[] { 0, 0, 0, 1 },
                RightEyeOrientation = new float[] { 0, 0, 0, 1 },
                LeftEyePosition = new float[] { 0, 0, 0 },
                RightEyePosition = new float[] { 0, 0, 0 },
                LeftEyeConfidence = gazeValid ? 1.0f : 0.0f,
                RightEyeConfidence = gazeValid ? 1.0f : 0.0f
            });
        }
        catch (SocketException)
        {
            // The label receiver is optional for ordinary live tracking.
        }
    }

    private void SendSteamLabel<T>(T message)
    {
        byte[] packet = JsonSerializer.SerializeToUtf8Bytes(message,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        _steamLabelSocket!.Send(packet, packet.Length);
    }

    private void ReceiveGaze()
    {
        if (_gazeSocket is null)
            return;
        try
        {
            while (_gazeSocket.Available >= GazePacketBytes)
            {
                IPEndPoint sender = new(IPAddress.Loopback, 0);
                byte[] packet = _gazeSocket.Receive(ref sender);
                if (packet.Length != GazePacketBytes ||
                    packet[0] != (byte)'Q' || packet[1] != (byte)'P' ||
                    packet[2] != (byte)'G' || packet[3] != (byte)'E' ||
                    packet[4] != 1)
                    continue;
                _gazeFlags = packet[5];
                _leftGazeX = ReadFloat(packet, 8);
                _leftGazeY = ReadFloat(packet, 12);
                _rightGazeX = ReadFloat(packet, 16);
                _rightGazeY = ReadFloat(packet, 20);
                _lastGazeTick = Environment.TickCount64;
            }
        }
        catch (SocketException error) when (
            error.SocketErrorCode is SocketError.WouldBlock or SocketError.IOPending)
        {
        }
    }

    private void ReceiveTongue()
    {
        if (_tongueSocket is null)
            return;
        try
        {
            while (_tongueSocket.Available >= TonguePacketBytes)
            {
                IPEndPoint sender = new(IPAddress.Loopback, 0);
                byte[] packet = _tongueSocket.Receive(ref sender);
                if (packet.Length != TonguePacketBytes ||
                    packet[0] != (byte)'Q' || packet[1] != (byte)'P' ||
                    packet[2] != (byte)'T' || packet[3] != (byte)'O' ||
                    packet[4] != 1)
                    continue;
                _tongueEnabled = (packet[5] & 1) != 0;
                for (int index = 0; index < _tongueValues.Length; ++index)
                    _tongueValues[index] = Math.Clamp(ReadFloat(packet, 8 + index * 4), 0.0f, 1.0f);
                _tongueDirty = true;
                _lastTongueTick = Environment.TickCount64;
            }
        }
        catch (SocketException error) when (
            error.SocketErrorCode is SocketError.WouldBlock or SocketError.IOPending)
        {
        }
    }

    private void ReceivePupil()
    {
        if (_pupilSocket is null)
            return;
        try
        {
            while (_pupilSocket.Available >= PupilPacketBytes)
            {
                IPEndPoint sender = new(IPAddress.Loopback, 0);
                byte[] packet = _pupilSocket.Receive(ref sender);
                if (packet.Length != PupilPacketBytes ||
                    packet[0] != (byte)'Q' || packet[1] != (byte)'P' ||
                    packet[2] != (byte)'D' || packet[3] != (byte)'I' ||
                    packet[4] != 1)
                    continue;
                _pupilFlags = (byte)(packet[5] & 3);
                float left = ReadFloat(packet, 8);
                float right = ReadFloat(packet, 12);
                if (!float.IsFinite(left) || left < 2.0f || left > 9.0f)
                    _pupilFlags &= 0b10;
                if (!float.IsFinite(right) || right < 2.0f || right > 9.0f)
                    _pupilFlags &= 0b01;
                _leftPupilMm = left;
                _rightPupilMm = right;
                _lastPupilTick = Environment.TickCount64;
            }
        }
        catch (SocketException error) when (
            error.SocketErrorCode is SocketError.WouldBlock or SocketError.IOPending)
        {
        }
    }

    private void UpdateEyes(ReadOnlySpan<float> values)
    {
        bool leftValid = _second[292] != 0;
        bool rightValid = _second[293] != 0;
        long now = Environment.TickCount64;
        bool customFresh = _lastGazeTick != 0 &&
            now - _lastGazeTick <= GazeTimeoutMs;

        GazePoint? left = customFresh && (_gazeFlags & 1) != 0
            ? new GazePoint(_leftGazeX, _leftGazeY)
            : leftValid ? GazeFromQuaternion(_second, 296) : null;
        GazePoint? right = customFresh && (_gazeFlags & 2) != 0
            ? new GazePoint(_rightGazeX, _rightGazeY)
            : rightValid ? GazeFromQuaternion(_second, 324) : null;
        ApplyBlinkGaze(values, _second[1] != 0, left, right, now);

        // Blink drives closure. A smile can raise the cheeks and tighten the
        // lids without actually closing either eye.
        UnifiedTracking.Data.Eye.Left.Openness = 1.0f - Math.Clamp(
            values[12] + values[12] * values[28], 0.0f, 1.0f);
        UnifiedTracking.Data.Eye.Right.Openness = 1.0f - Math.Clamp(
            values[13] + values[13] * values[29], 0.0f, 1.0f);
        bool pupilFresh = _lastPupilTick != 0 &&
            Environment.TickCount64 - _lastPupilTick <= PupilTimeoutMs;
        UnifiedTracking.Data.Eye.Left.PupilDiameter_MM =
            pupilFresh && (_pupilFlags & 1) != 0 ? _leftPupilMm : 5.0f;
        UnifiedTracking.Data.Eye.Right.PupilDiameter_MM =
            pupilFresh && (_pupilFlags & 2) != 0 ? _rightPupilMm : 5.0f;
        // VRCFT normalizes combined dilation using these limits. The relative
        // camera estimate eases from 2 to 8 with neutral at 5, so use the
        // same range to make VRChat's 0..1 animation respond visibly.
        UnifiedTracking.Data.Eye._minDilation = 2.0f;
        UnifiedTracking.Data.Eye._maxDilation = 8.0f;

        if (_second[1] != 0)
            UpdateEyeExpressions(values);
    }

    private void ApplyBlinkGaze(ReadOnlySpan<float> values, bool blinkAvailable,
        GazePoint? left, GazePoint? right, long now)
    {
        GazePoint? heldLeft = _leftBlinkGaze.Update(values[12], blinkAvailable, left, now);
        GazePoint? heldRight = _rightBlinkGaze.Update(values[13], blinkAvailable, right, now);
        if (heldLeft is { } leftPoint)
        {
            UnifiedTracking.Data.Eye.Left.Gaze.x = leftPoint.X;
            UnifiedTracking.Data.Eye.Left.Gaze.y = leftPoint.Y;
        }
        if (heldRight is { } rightPoint)
        {
            UnifiedTracking.Data.Eye.Right.Gaze.x = rightPoint.X;
            UnifiedTracking.Data.Eye.Right.Gaze.y = rightPoint.Y;
        }
    }

    private static GazePoint GazeFromQuaternion(byte[] bytes, int offset)
    {
        (float x, float y) = QuaternionToCartesian(bytes, offset);
        return new GazePoint(x, y);
    }

    private static void UpdateEyeExpressions(ReadOnlySpan<float> values)
    {
        Set(1, values[28]); Set(0, values[29]);
        Set(3, values[59]); Set(2, values[60]);
    }

    private void UpdateBrowExpressions(ReadOnlySpan<float> values)
    {
        // Brow blendshapes are present even when VRCFT owns only the face
        // expression slot. Preserve Virtual Desktop's separate left/right
        // inner, outer, and lowering values in either module configuration.
        RefreshEyebrowSettings();
        EyebrowWeights brow = EyebrowMapping.FromFaceWeights(
            values, _eyebrowSettings.Enabled, _eyebrowSettings.Sensitivity);
        Set((int)UnifiedExpressions.BrowPinchLeft, brow.LowerLeft);
        Set((int)UnifiedExpressions.BrowLowererLeft, brow.LowerLeft);
        Set((int)UnifiedExpressions.BrowPinchRight, brow.LowerRight);
        Set((int)UnifiedExpressions.BrowLowererRight, brow.LowerRight);
        Set((int)UnifiedExpressions.BrowInnerUpLeft, brow.InnerLeft);
        Set((int)UnifiedExpressions.BrowInnerUpRight, brow.InnerRight);
        Set((int)UnifiedExpressions.BrowOuterUpLeft, brow.OuterLeft);
        Set((int)UnifiedExpressions.BrowOuterUpRight, brow.OuterRight);
    }

    private void UpdateMouth(ReadOnlySpan<float> values, byte faceFlags, NativeFaceSource source)
    {
        // Both sources use the same XR_FB indices for direct face shapes.
        long frameTickMs = Environment.TickCount64;
        foreach ((int expressionIndex, int[] targets) in ExpressionMap)
            foreach (int target in targets)
                Set(target, values[expressionIndex]);

        SmirkWeights smile = _smirkTracker.Update(values, frameTickMs);
        Set((int)UnifiedExpressions.MouthCornerPullLeft, smile.Left);
        Set((int)UnifiedExpressions.MouthCornerSlantLeft, smile.Left);
        Set((int)UnifiedExpressions.MouthCornerPullRight, smile.Right);
        Set((int)UnifiedExpressions.MouthCornerSlantRight, smile.Right);

        // Both sources use the same left/right XR_FB cheek indices. The Hub
        // selects native passthrough, balanced separation, or strong separation.
        RefreshCheekPuffMode();
        CheekPuffWeights cheeks = _cheekPuffTracker.Update(values, _cheekPuffMode, frameTickMs);
        Set((int)UnifiedExpressions.CheekPuffLeft, cheeks.Left);
        Set((int)UnifiedExpressions.CheekPuffRight, cheeks.Right);
        Set((int)UnifiedExpressions.CheekSquintLeft, values[4]);
        Set((int)UnifiedExpressions.CheekSquintRight, values[5]);
        RefreshCheekSuckMode();
        CheekSuckWeights suck = _cheekSuckTracker.Update(values, _cheekSuckMode, frameTickMs);
        Set((int)UnifiedExpressions.CheekSuckLeft, suck.Left);
        Set((int)UnifiedExpressions.CheekSuckRight, suck.Right);

        NativeLipWeights lips = NativeLipMapping.FromFaceWeights(values, source);
        Set((int)UnifiedExpressions.MouthUpperUpLeft, lips.UpperUpLeft);
        Set((int)UnifiedExpressions.MouthUpperDeepenLeft, lips.UpperDeepenLeft);
        Set((int)UnifiedExpressions.MouthUpperUpRight, lips.UpperUpRight);
        Set((int)UnifiedExpressions.MouthUpperDeepenRight, lips.UpperDeepenRight);
        Set((int)UnifiedExpressions.LipSuckUpperLeft, lips.SuckUpperLeft);
        Set((int)UnifiedExpressions.LipSuckUpperRight, lips.SuckUpperRight);

        bool customFresh = _lastTongueTick != 0 &&
            Environment.TickCount64 - _lastTongueTick <= TongueTimeoutMs;
        NativeTongueWeights? nativeTongue = NativeTongueMapping.Resolve(
            values, source, faceFlags, customFresh, _tongueEnabled);
        if (nativeTongue is null)
        {
            // Do not rewrite unchanged tongue shapes at the module's ~200 Hz
            // face cadence. A new camera packet is the only thing that should
            // dirty these slots; this avoids flooding VRCFT/VRChat while the
            // rest of the face remains on its normal update path.
            if (_tongueDirty)
            {
                ApplyTongueOverride();
                _tongueDirty = false;
                _customTongueWasApplied = true;
            }
        }
        else
        {
            if (_customTongueWasApplied)
            {
                ClearDetailedTongueOverride();
                _customTongueWasApplied = false;
            }
            NativeTongueWeights tongue = nativeTongue.Value;
            Set((int)UnifiedExpressions.TongueOut, tongue.Out);
            if (tongue.Up is float up) Set((int)UnifiedExpressions.TongueUp, up);
            if (tongue.Down is float down) Set((int)UnifiedExpressions.TongueDown, down);
            if (tongue.Left is float left) Set((int)UnifiedExpressions.TongueLeft, left);
            if (tongue.Right is float right) Set((int)UnifiedExpressions.TongueRight, right);
            if (tongue.BendDown is float bendDown) Set((int)UnifiedExpressions.TongueBendDown, bendDown);
            if (tongue.CurlUp is float curlUp) Set((int)UnifiedExpressions.TongueCurlUp, curlUp);
        }
    }

    private void RefreshCheekPuffMode()
    {
        long now = Environment.TickCount64;
        if (now < _nextCheekPuffModeCheckTick) return;
        _nextCheekPuffModeCheckTick = now + 250;
        CheekPuffMode selected;
        try
        {
            string saved = File.Exists(CheekPuffModePath)
                ? File.ReadAllText(CheekPuffModePath).Trim()
                : "strong";
            selected = saved.ToLowerInvariant() switch
            {
                "off" => CheekPuffMode.Off,
                "balanced" => CheekPuffMode.Balanced,
                _ => CheekPuffMode.Strong
            };
        }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        if (_cheekPuffMode == selected) return;
        _cheekPuffMode = selected;
        _cheekPuffTracker.Reset();
        Logger.LogInformation("Cheek puff style: {Style}", selected switch
        {
            CheekPuffMode.Off => "native passthrough",
            CheekPuffMode.Balanced => "balanced",
            _ => "strong individual"
        });
    }

    private void RefreshCheekSuckMode()
    {
        long now = Environment.TickCount64;
        if (now < _nextCheekSuckModeCheckTick) return;
        _nextCheekSuckModeCheckTick = now + 250;
        CheekSuckMode selected;
        try
        {
            string saved = File.Exists(CheekSuckModePath)
                ? File.ReadAllText(CheekSuckModePath).Trim()
                : "strong";
            selected = saved.ToLowerInvariant() switch
            {
                "off" => CheekSuckMode.Off,
                "balanced" => CheekSuckMode.Balanced,
                _ => CheekSuckMode.Strong
            };
        }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        if (_cheekSuckMode == selected) return;
        _cheekSuckMode = selected;
        _cheekSuckTracker.Reset();
        Logger.LogInformation("Cheek suck style: {Style}", selected switch
        {
            CheekSuckMode.Off => "native passthrough",
            CheekSuckMode.Balanced => "balanced",
            _ => "strong individual"
        });
    }

    private void RefreshEyebrowSettings()
    {
        long now = Environment.TickCount64;
        if (now < _nextEyebrowSettingsCheckTick) return;
        _nextEyebrowSettingsCheckTick = now + 250;
        EyebrowSettings selected = EyebrowPreference.Load();
        if (_eyebrowSettings == selected) return;
        _eyebrowSettings = selected;
        Logger.LogInformation("Eyebrow adjustment: {Enabled}; sensitivity: {Sensitivity:F2}x",
            selected.Enabled, selected.Sensitivity);
    }

    private void ApplyTongueOverride()
    {
        Set((int)UnifiedExpressions.TongueOut, _tongueValues[0]);
        Set((int)UnifiedExpressions.TongueUp, _tongueValues[1]);
        Set((int)UnifiedExpressions.TongueDown, _tongueValues[2]);
        Set((int)UnifiedExpressions.TongueLeft, _tongueValues[3]);
        Set((int)UnifiedExpressions.TongueRight, _tongueValues[4]);
        Set((int)UnifiedExpressions.TongueRoll, _tongueValues[5]);
        Set((int)UnifiedExpressions.TongueBendDown, _tongueValues[6]);
        Set((int)UnifiedExpressions.TongueCurlUp, _tongueValues[7]);
        Set((int)UnifiedExpressions.TongueSquish, _tongueValues[8]);
        Set((int)UnifiedExpressions.TongueFlat, _tongueValues[9]);
        Set((int)UnifiedExpressions.TongueTwistLeft, _tongueValues[10]);
        Set((int)UnifiedExpressions.TongueTwistRight, _tongueValues[11]);
    }

    private static void ClearDetailedTongueOverride()
    {
        Set((int)UnifiedExpressions.TongueOut, 0.0f);
        Set((int)UnifiedExpressions.TongueUp, 0.0f);
        Set((int)UnifiedExpressions.TongueDown, 0.0f);
        Set((int)UnifiedExpressions.TongueLeft, 0.0f);
        Set((int)UnifiedExpressions.TongueRight, 0.0f);
        Set((int)UnifiedExpressions.TongueRoll, 0.0f);
        Set((int)UnifiedExpressions.TongueBendDown, 0.0f);
        Set((int)UnifiedExpressions.TongueCurlUp, 0.0f);
        Set((int)UnifiedExpressions.TongueSquish, 0.0f);
        Set((int)UnifiedExpressions.TongueFlat, 0.0f);
        Set((int)UnifiedExpressions.TongueTwistLeft, 0.0f);
        Set((int)UnifiedExpressions.TongueTwistRight, 0.0f);
    }

    private static void Set(int expression, float value) =>
        UnifiedTracking.Data.Shapes[expression].Weight = value;

    private static float ReadFloat(byte[] bytes, int offset) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4)));

    private static (float x, float y) QuaternionToCartesian(byte[] bytes, int offset)
    {
        float x = BitConverter.ToSingle(bytes, offset);
        float y = BitConverter.ToSingle(bytes, offset + 4);
        float z = BitConverter.ToSingle(bytes, offset + 8);
        float w = BitConverter.ToSingle(bytes, offset + 12);
        float length = MathF.Sqrt(x * x + y * y + z * z + w * w);
        if (length <= 1e-6f)
            return (0.0f, 0.0f);
        x /= length; y /= length; z /= length; w /= length;
        float first = MathF.Asin(Math.Clamp(2.0f * (x * z - w * y), -1.0f, 1.0f));
        float second = MathF.Atan2(
            2.0f * (y * z + w * x),
            w * w - x * x - y * y + z * z);
        return (first, second);
    }
}
