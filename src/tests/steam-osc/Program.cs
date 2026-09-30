using System.Buffers.Binary;
using System.Text;
using Qpro.GazeBridge;
using Qpro.Shared;

foreach (string faceSource in new[] { CheekPuffCalibrationProfile.VirtualDesktopSource, CheekPuffCalibrationProfile.SteamLinkSource })
{
    CheekPuffCalibration? developerBaseline = DeveloperCheekPuffBaseline.ForSource(faceSource);
    Check(developerBaseline.HasValue && CheekPuffCalibrationProfile.IsValid(developerBaseline.Value),
        $"valid bundled cheek baseline for {faceSource}");
}
Check(DeveloperCheekPuffBaseline.ForSource("unsupported") is null,
    "unknown source has no bundled cheek baseline");

if (args.Length == 2 && args[0] == "--live-probe" && int.TryParse(args[1], out int seconds))
{
    using var live = new SteamOscSource();
    DateTime deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(seconds, 1, 10));
    while (DateTime.UtcNow < deadline)
    {
        live.Poll();
        Thread.Sleep(3);
    }
    long now = Environment.TickCount64;
    int freshExpressions = live.FreshExpressionCount(now);
    Console.WriteLine($"datagrams={live.ReceivedDatagrams} parsed={live.ParsedDatagrams} rejected={live.RejectedDatagrams} " +
        $"faceFresh={live.HasFreshFace(now)} eyeFresh={live.HasFreshEye(now)} gazeFresh={live.HasFreshGaze(now)} " +
        $"freshExpressions={freshExpressions}/70 labelSchemaReady={freshExpressions == 70 && live.HasFreshFace(now)} " +
        $"lowerFace={live.LowerFaceAvailable?.ToString() ?? "unknown"} upperFace={live.UpperFaceAvailable?.ToString() ?? "unknown"}");
    return;
}

if (args.Length == 2 && args[0] == "--live-diagnostics" && int.TryParse(args[1], out int diagnosticSeconds))
{
    using var live = new SteamOscSource();
    int[] watched = [12, 13, 24, 28, 29, 32, 33, 68];
    float[] minima = Enumerable.Repeat(float.PositiveInfinity, watched.Length).ToArray();
    float[] maxima = Enumerable.Repeat(float.NegativeInfinity, watched.Length).ToArray();
    bool[] everNonzero = new bool[70];
    int samples = 0;
    DateTime deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(diagnosticSeconds, 1, 15));
    while (DateTime.UtcNow < deadline)
    {
        live.Poll();
        long now = Environment.TickCount64;
        if (live.HasFreshFace(now))
        {
            samples++;
            ReadOnlySpan<float> weights = live.Expressions;
            for (int i = 0; i < weights.Length; i++)
                everNonzero[i] |= weights[i] > 0.01f;
            for (int i = 0; i < watched.Length; i++)
            {
                minima[i] = MathF.Min(minima[i], weights[watched[i]]);
                maxima[i] = MathF.Max(maxima[i], weights[watched[i]]);
            }
        }
        Thread.Sleep(3);
    }
    long endTick = Environment.TickCount64;
    Console.WriteLine($"datagrams={live.ReceivedDatagrams} parsed={live.ParsedDatagrams} rejected={live.RejectedDatagrams} " +
        $"faceFresh={live.HasFreshFace(endTick)} eyeFresh={live.HasFreshEye(endTick)} gazeFresh={live.HasFreshGaze(endTick)} " +
        $"freshExpressions={live.FreshExpressionCount(endTick)}/70 everNonzero={everNonzero.Count(x => x)}/70 " +
        $"samples={samples} lowerFace={live.LowerFaceAvailable?.ToString() ?? "unknown"} upperFace={live.UpperFaceAvailable?.ToString() ?? "unknown"}");
    for (int i = 0; i < watched.Length; i++)
        Console.WriteLine($"{SteamOscSource.ExpressionNames[watched[i]]}: min={minima[i]:F3} max={maxima[i]:F3}");
    return;
}

using var source = new SteamOscSource(port: 0);
Check(SteamOscSource.ExpressionNames.Count == 70, "70 expression names");
Check(SteamOscSource.ExpressionNames[12] == "EyesClosedL", "blink index");
Check(SteamOscSource.ExpressionNames[68] == "TongueOut", "tongue index");

byte[] frame = Bundle(
    Message("/sl/xrfb/facew/CheekPuffL", 0.75f),
    Message("/sl/xrfb/facew/CheekPuffR", 0.2f),
    Message("/sl/xrfb/facew/CheekSuckL", 0.34f),
    Message("/sl/xrfb/facew/CheekSuckR", 0.21f),
    Bundle(Message("/sl/xrfb/facew/FrontDorsalPalate", 0.35f)),
    Message("/sl/xrfb/facew/ToungeOut", 0.8f),
    Message("/sl/xrfb/facew/EyesClosedL", 0.4f),
    Message("/sl/xrfb/facew/LidTightenerL", 0.5f),
    Message("/sl/eyeTrackedGazePoint", 0.1f, 0.2f, -1.0f));
Check(source.ParseDatagram(frame, 1000), "nested bundle parses");
CheckNear(source.Expressions[2], 0.75f, "left cheek");
CheckNear(source.Expressions[3], 0.2f, "right cheek");
CheckNear(source.Expressions[6], 0.34f, "left cheek suck channel");
CheckNear(source.Expressions[7], 0.21f, "right cheek suck channel");
float[] cheekWeights = new float[70];
cheekWeights[2] = 0.38f;
cheekWeights[3] = 0.27f;
CheekPuffWeights leftPuff = CheekPuffMapping.FromFaceWeights(cheekWeights);
Check(leftPuff.Left > 0.3f && leftPuff.Right < 0.01f, "one-sided left cheek puff separates");
CheekPuffWeights strongLeftPuff = CheekPuffMapping.FromFaceWeights(cheekWeights, strong: true);
CheckNear(strongLeftPuff.Left, 1.0f, "strong left cheek puff reaches full value");
CheckNear(strongLeftPuff.Right, 0.0f, "strong left cheek suppresses right");
cheekWeights[2] = 0.05f;
cheekWeights[3] = 0.10f;
CheekPuffWeights rightPuff = CheekPuffMapping.FromFaceWeights(cheekWeights);
Check(rightPuff.Right > 0.1f && rightPuff.Left < 0.01f, "one-sided right cheek puff separates");
CheekPuffWeights strongRightPuff = CheekPuffMapping.FromFaceWeights(cheekWeights, strong: true);
CheckNear(strongRightPuff.Right, 1.0f, "strong right cheek puff reaches full value");
CheckNear(strongRightPuff.Left, 0.0f, "strong right cheek suppresses left");
cheekWeights[2] = 1.0f;
cheekWeights[3] = 1.0f;
CheekPuffWeights bothPuff = CheekPuffMapping.FromFaceWeights(cheekWeights);
CheckNear(bothPuff.Left, 1.0f, "two-sided left cheek puff preserved");
CheckNear(bothPuff.Right, 1.0f, "two-sided right cheek puff preserved");
CheekPuffWeights strongBothPuff = CheekPuffMapping.FromFaceWeights(cheekWeights, strong: true);
CheckNear(strongBothPuff.Left, 1.0f, "strong two-sided left cheek puff preserved");
CheckNear(strongBothPuff.Right, 1.0f, "strong two-sided right cheek puff preserved");
cheekWeights[2] = 0.01f;
cheekWeights[3] = 0.0f;
CheekPuffWeights idleCheeks = CheekPuffMapping.FromFaceWeights(cheekWeights);
CheckNear(idleCheeks.Left, 0.0f, "idle left cheek stays neutral");
CheckNear(idleCheeks.Right, 0.0f, "idle right cheek stays neutral");

var cheekTracker = new CheekPuffTracker();
CheckCheeks(TrackCheeks(cheekTracker, cheekWeights, 0.38f, 0.27f, 0), 1, 0,
    "strong tracker starts on left");
for (long ms = 5; ms <= 120; ms += 5)
    CheckCheeks(TrackCheeks(cheekTracker, cheekWeights, 0.20f, 0.19f, ms), 1, 0,
        "120 ms crossover never puffs inactive cheek");
CheckCheeks(TrackCheeks(cheekTracker, cheekWeights, 0.70f, 0.78f, 125), 1, 0,
    "opposite lead starts confirmation without bouncing");
CheckCheeks(TrackCheeks(cheekTracker, cheekWeights, 0.70f, 0.78f, 155), 1, 0,
    "opposite lead does not switch early");
CheckCheeks(TrackCheeks(cheekTracker, cheekWeights, 0.70f, 0.78f, 185), 0, 1,
    "confirmed opposite side switches directly");
for (long ms = 190; ms <= 305; ms += 5)
    CheckCheeks(TrackCheeks(cheekTracker, cheekWeights, 0.19f, 0.20f, ms), 0, 1,
        "reverse crossover never puffs inactive cheek");
CheckCheeks(TrackCheeks(cheekTracker, cheekWeights, 0.78f, 0.70f, 310), 0, 1,
    "reverse lead starts confirmation");
CheckCheeks(TrackCheeks(cheekTracker, cheekWeights, 0.78f, 0.70f, 375), 1, 0,
    "reverse side switches directly");
CheckCheeks(TrackCheeks(cheekTracker, cheekWeights, 0.95f, 0.96f, 400), 1, 0,
    "both-cheek dwell starts from left");
CheckCheeks(TrackCheeks(cheekTracker, cheekWeights, 0.95f, 0.96f, 610), 1, 0,
    "both-cheek dwell holds one side before 220 ms");
CheckCheeks(TrackCheeks(cheekTracker, cheekWeights, 0.95f, 0.96f, 620), 1, 1,
    "sustained intentional both-cheek puff activates both");
CheckCheeks(TrackCheeks(cheekTracker, cheekWeights, 0, 0, 630), 0, 0,
    "neutral releases both cheeks");
CheckCheeks(TrackCheeks(cheekTracker, cheekWeights, 0.70f, 0.70f, 640), 0, 0,
    "both-cheek puff from neutral starts short dwell");
CheckCheeks(TrackCheeks(cheekTracker, cheekWeights, 0.70f, 0.70f, 680), 1, 1,
    "both-cheek puff from neutral activates after short dwell");

// Recorded Quest Pro side switch from artifacts/cheek-switch-raw.csv. The
// weights stay almost equal for 62 ms, then the new right lead is only 10-13%.
// The old stateless mapper briefly emitted both sides throughout this passage.
var recordedTracker = new CheekPuffTracker();
(long Ms, float Left, float Right)[] recordedSwitch =
[
    (19698, .08293416f, .03803299f),
    (19791, .08523f, .04021459f),
    (19855, .13476846f, .065627575f),
    (19917, .24253877f, .1512201f),
    (20039, .39829665f, .28947696f),
    (20071, .5680012f, .43751585f),
    (20102, .8825973f, .7516156f),
    (20133, .9970358f, .994524f),
    (20164, .9999126f, .9998589f),
    (20195, .999997f, .99999577f),
    (20226, .89692426f, .9999999f),
    (20257, .73659927f, .84164476f),
    (20289, .7034784f, .78216815f),
    (20319, .6478207f, .7534606f),
    (20350, .63491404f, .7467608f)
];
foreach ((long ms, float left, float right) in recordedSwitch)
    CheckCheeks(TrackCheeks(recordedTracker, cheekWeights, left, right, ms),
        ms < 20289 ? 1 : 0, ms < 20289 ? 0 : 1,
        $"recorded cheek switch at {ms} ms has no opposite bounce");

// The same capture opens with a moderate right lead, then switches to left
// through a short fully symmetric plateau. Initial side recognition must not
// wait for the larger 0.24 settled-pose lead.
var recordedOpeningTracker = new CheekPuffTracker();
(long Ms, float Left, float Right)[] recordedOpening =
[
    (3, .67526615f, .8001669f),
    (61, .45682833f, .56678075f),
    (93, .36806476f, .46219373f),
    (557, .46632242f, .5587441f),
    (588, .6959843f, .7993326f),
    (619, .9919739f, .99387544f),
    (650, .9999911f, .99999094f),
    (681, .99999964f, .9999996f),
    (711, 1f, 1f),
    (742, 1f, 1f),
    (772, .98450917f, .8892029f),
    (803, .9351505f, .7918478f),
    (834, .9351505f, .7918478f),
    (865, .75183487f, .5735842f),
    (926, .7321404f, .56508327f),
    (1498, .3201453f, .25328216f),
    (1560, .017690187f, .018135628f)
];
foreach ((long ms, float left, float right) in recordedOpening)
{
    float expectedRight = ms >= 61 && ms < 834 ? 1 : 0;
    float expectedLeft = ms >= 834 && ms < 1560 ? 1 : 0;
    CheckCheeks(TrackCheeks(recordedOpeningTracker, cheekWeights, left, right, ms),
        expectedLeft, expectedRight, $"recorded opening at {ms} ms");
}

var modeChangeTracker = new CheekPuffTracker();
TrackCheeks(modeChangeTracker, cheekWeights, 0.38f, 0.27f, 0);
CheekPuffWeights balancedAfterStrong = TrackCheeks(modeChangeTracker, cheekWeights,
    0.20f, 0.19f, 5, strong: false);
CheckNear(balancedAfterStrong.Left, CheekPuffMapping.FromFaceWeights(cheekWeights).Left,
    "balanced mode retains the original mapper");
CheckNear(balancedAfterStrong.Right, CheekPuffMapping.FromFaceWeights(cheekWeights).Right,
    "balanced mode retains the original mapper on right");
CheckCheeks(TrackCheeks(modeChangeTracker, cheekWeights, 0.70f, 0.70f, 10), 0, 0,
    "switching back to strong clears old side state");
CheckCheeks(TrackCheeks(modeChangeTracker, cheekWeights, 0.70f, 0.70f, 50), 1, 1,
    "strong mode restarts from neutral after a mode change");

var offModeTracker = new CheekPuffTracker();
cheekWeights[2] = 0.38f;
cheekWeights[3] = 0.27f;
CheckCheeks(offModeTracker.Update(cheekWeights, CheekPuffMode.Strong, 0), 1, 0,
    "strong mode selects a side before disabling individual cheek puff");
CheckCheeks(offModeTracker.Update(cheekWeights, CheekPuffMode.Off, 5), 0.38f, 0.27f,
    "off mode passes native left and right cheek weights unchanged");
cheekWeights[2] = 0.7f;
cheekWeights[3] = 0.7f;
CheckCheeks(offModeTracker.Update(cheekWeights, CheekPuffMode.Strong, 10), 0, 0,
    "re-enabling strong mode clears the previously selected side");
CheckCheeks(offModeTracker.Update(cheekWeights, CheekPuffMode.Strong, 50), 1, 1,
    "re-enabled strong mode recognizes a sustained both-cheek puff");
cheekWeights[2] = float.NaN;
cheekWeights[3] = 1.25f;
CheckCheeks(offModeTracker.Update(cheekWeights, CheekPuffMode.Off, 55), 0, 1,
    "off mode safely clamps invalid native cheek weights");
cheekWeights[2] = -0.2f;
cheekWeights[3] = float.PositiveInfinity;
CheckCheeks(offModeTracker.Update(cheekWeights, CheekPuffMode.Off, 60), 0, 0,
    "off mode rejects nonfinite and negative native cheek weights");

// The calibrated mode rescales the existing Balanced output continuously,
// using independent left and right neutral/full anchors for each source.
CheekPuffCalibration vdCalibration = new(.10f, .50f, .20f, .60f);
CheekPuffCalibration steamCalibration = new(.05f, .45f, .05f, .85f);
Check(CheekPuffCalibrationProfile.IsValid(vdCalibration), "valid calibration anchors");
var calibratedTracker = new CheekPuffTracker();
cheekWeights[2] = .30f;
cheekWeights[3] = .30f;
CheckCheeks(CheekPuffMapping.CalibratedFromFaceWeights(cheekWeights, vdCalibration),
    .50f, .25f, "calibration interpolates independently between anchors");
cheekWeights[2] = .10f;
cheekWeights[3] = .10f;
CheckCheeks(CheekPuffMapping.CalibratedFromFaceWeights(cheekWeights, vdCalibration),
    0, 0, "calibration clamps below neutral anchors");
cheekWeights[2] = .80f;
cheekWeights[3] = .80f;
CheckCheeks(CheekPuffMapping.CalibratedFromFaceWeights(cheekWeights, vdCalibration),
    1, 1, "calibration clamps above full anchors");
cheekWeights[2] = .38f;
cheekWeights[3] = .27f;
CheekPuffWeights separatedPuff = CheekPuffMapping.FromFaceWeights(cheekWeights);
CheckNear(separatedPuff.Right, 0, "calibration fixture separates one cheek");
CheckCheeks(CheekPuffMapping.CalibratedFromFaceWeights(cheekWeights, vdCalibration),
    Math.Clamp((separatedPuff.Left - .10f) / .40f, 0, 1), 0,
    "calibration preserves Balanced side separation");
CheckCheeks(calibratedTracker.Update(cheekWeights, CheekPuffMode.Calibrated, 85),
    separatedPuff.Left, separatedPuff.Right,
    "missing calibration falls back to Balanced");
CheekPuffCalibration reversedCalibration = new(.7f, .2f, .1f, .9f);
Check(!CheekPuffCalibrationProfile.IsValid(reversedCalibration), "reversed anchors are invalid");
CheckCheeks(calibratedTracker.Update(cheekWeights, CheekPuffMode.Calibrated, 90, reversedCalibration),
    separatedPuff.Left, separatedPuff.Right,
    "invalid calibration falls back to Balanced");
CheckCheeks(calibratedTracker.Update(cheekWeights, CheekPuffMode.Off, 95, vdCalibration),
    .38f, .27f, "calibration cannot change Off native passthrough");
CheckCheeks(calibratedTracker.Update(cheekWeights, CheekPuffMode.Strong, 100, vdCalibration),
    1, 0, "calibration cannot change Strong one-or-zero output");
cheekWeights[2] = float.NaN;
cheekWeights[3] = .20f;
CheckCheeks(calibratedTracker.Update(cheekWeights, CheekPuffMode.Calibrated, 105, vdCalibration),
    0, 1, "invalid source strength cannot poison the other calibrated cheek");
Check(!CheekPuffCalibrationProfile.IsValid(new(0, float.NaN, 0, 1)),
    "nonfinite calibration anchors are invalid");
Check(!CheekPuffCalibrationProfile.IsValid(new(0, 1.1f, 0, 1)),
    "out-of-range calibration anchors are invalid");
Check(!CheekPuffCalibrationProfile.IsValid(new(.2f, .21f, 0, 1)),
    "nearly equal anchors cannot amplify source noise");

CheekPuffCalibration unitCalibration = new(0, 1, 0, 1);
var slowCalibratedTracker = new CheekPuffTracker();
var fastCalibratedTracker = new CheekPuffTracker();
cheekWeights[2] = cheekWeights[3] = .20f;
CheckCheeks(slowCalibratedTracker.Update(cheekWeights, CheekPuffMode.Calibrated, 1000, unitCalibration),
    .20f, .20f, "first calibrated sample begins at the current strength");
fastCalibratedTracker.Update(cheekWeights, CheekPuffMode.Calibrated, 1000, unitCalibration);
cheekWeights[2] = cheekWeights[3] = .80f;
CheekPuffWeights slowResponse = slowCalibratedTracker.Update(
    cheekWeights, CheekPuffMode.Calibrated, 1100, unitCalibration);
fastCalibratedTracker.Update(cheekWeights, CheekPuffMode.Calibrated, 1050, unitCalibration);
CheekPuffWeights fastResponse = fastCalibratedTracker.Update(
    cheekWeights, CheekPuffMode.Calibrated, 1100, unitCalibration);
float expectedRise = .20f + .60f * (1 - MathF.Exp(-1));
CheckCheeks(slowResponse, expectedRise, expectedRise,
    "calibrated strength approaches a new target over 100 milliseconds");
CheckCheeks(fastResponse, slowResponse.Left, slowResponse.Right,
    "calibrated smoothing is independent of sampling frequency");
cheekWeights[2] = cheekWeights[3] = 0;
CheekPuffWeights releasedResponse = slowCalibratedTracker.Update(
    cheekWeights, CheekPuffMode.Calibrated, 1200, unitCalibration);
CheckCheeks(releasedResponse, expectedRise * MathF.Exp(-1), expectedRise * MathF.Exp(-1),
    "calibrated cheek release is smooth");
CheckCheeks(slowCalibratedTracker.Update(cheekWeights, CheekPuffMode.Calibrated, 1700, unitCalibration),
    0, 0, "a source gap discards stale calibrated strength");
cheekWeights[2] = cheekWeights[3] = .20f;
CheckCheeks(slowCalibratedTracker.Update(cheekWeights, CheekPuffMode.Calibrated, 1600, unitCalibration),
    .20f, .20f, "a backwards timestamp resets calibrated smoothing");
cheekWeights[2] = cheekWeights[3] = .80f;
CheckCheeks(slowCalibratedTracker.Update(cheekWeights, CheekPuffMode.Off, 1710, unitCalibration),
    .80f, .80f, "switching off calibration immediately restores native strength");
cheekWeights[2] = cheekWeights[3] = .20f;
CheckCheeks(slowCalibratedTracker.Update(cheekWeights, CheekPuffMode.Calibrated, 1720, unitCalibration),
    .20f, .20f, "returning to calibration cannot reuse the prior mode's output");
CheckCheeks(slowCalibratedTracker.Update(cheekWeights, CheekPuffMode.Calibrated, 1730,
    new(0, .50f, 0, .50f)), .40f, .40f,
    "new calibration anchors discard the previous normalized strength");
CheckCheeks(slowCalibratedTracker.Update(cheekWeights, CheekPuffMode.Calibrated, 1740),
    .20f, .20f, "removing a calibration profile immediately restores Balanced fallback");

string profileTestDirectory = Path.Combine(Path.GetTempPath(),
    $"qpro-cheek-calibration-tests-{Guid.NewGuid():N}");
try
{
    Check(CheekPuffCalibrationProfile.Load(
        CheekPuffCalibrationProfile.VirtualDesktopSource, profileTestDirectory) is null,
        "missing personal profile is absent");
    CheekPuffCalibrationProfile.Save(
        CheekPuffCalibrationProfile.VirtualDesktopSource, vdCalibration, profileTestDirectory);
    CheekPuffCalibrationProfile.Save(
        CheekPuffCalibrationProfile.SteamLinkSource, steamCalibration, profileTestDirectory);
    Check(CheekPuffCalibrationProfile.Load(
        CheekPuffCalibrationProfile.VirtualDesktopSource, profileTestDirectory) == vdCalibration,
        "Virtual Desktop profile round trips independently");
    Check(CheekPuffCalibrationProfile.Load(
        CheekPuffCalibrationProfile.SteamLinkSource, profileTestDirectory) == steamCalibration,
        "Steam Link profile round trips independently");
    Check(!Directory.EnumerateFiles(profileTestDirectory, "*.tmp").Any(),
        "atomic profile writes leave no temporary files");
    bool rejectedInvalidSave = false;
    try
    {
        CheekPuffCalibrationProfile.Save(
            CheekPuffCalibrationProfile.VirtualDesktopSource,
            reversedCalibration, profileTestDirectory);
    }
    catch (ArgumentOutOfRangeException) { rejectedInvalidSave = true; }
    Check(rejectedInvalidSave, "invalid profile is rejected before replacing a valid profile");
    Check(CheekPuffCalibrationProfile.Load(
        CheekPuffCalibrationProfile.VirtualDesktopSource, profileTestDirectory) == vdCalibration,
        "invalid save leaves existing source profile intact");
    File.WriteAllText(CheekPuffCalibrationProfile.ProfilePath(
        CheekPuffCalibrationProfile.VirtualDesktopSource, profileTestDirectory),
        "{\"version\":1,\"leftNeutral\":0.5,\"leftFull\":0.4,\"rightNeutral\":0,\"rightFull\":1}");
    Check(CheekPuffCalibrationProfile.Load(
        CheekPuffCalibrationProfile.VirtualDesktopSource, profileTestDirectory) is null,
        "invalid stored anchors fall back safely");
    Check(CheekPuffCalibrationProfile.Load(
        CheekPuffCalibrationProfile.SteamLinkSource, profileTestDirectory) == steamCalibration,
        "corrupt Virtual Desktop profile does not affect Steam Link");
    File.WriteAllText(CheekPuffCalibrationProfile.ProfilePath(
        CheekPuffCalibrationProfile.VirtualDesktopSource, profileTestDirectory), "{broken");
    Check(CheekPuffCalibrationProfile.Load(
        CheekPuffCalibrationProfile.VirtualDesktopSource, profileTestDirectory) is null,
        "malformed profile falls back safely");
}
finally
{
    if (Directory.Exists(profileTestDirectory))
        Directory.Delete(profileTestDirectory, recursive: true);
}

byte[] cheekPacket = CheekPuffTelemetry.Create(
    CheekPuffTelemetry.SourceVirtualDesktop, .25f, .75f);
Check(CheekPuffTelemetry.TryParse(cheekPacket, out byte cheekSource,
    out float cheekLeft, out float cheekRight), "cheek telemetry packet round trips");
Check(cheekSource == CheekPuffTelemetry.SourceVirtualDesktop,
    "cheek telemetry preserves source identity");
CheckNear(cheekLeft, .25f, "cheek telemetry preserves left strength");
CheckNear(cheekRight, .75f, "cheek telemetry preserves right strength");
cheekPacket[4] = 99;
Check(!CheekPuffTelemetry.TryParse(cheekPacket, out _, out _, out _),
    "cheek telemetry rejects unknown source");
cheekPacket = CheekPuffTelemetry.Create(CheekPuffTelemetry.SourceSteamLink, .2f, .8f);
BinaryPrimitives.WriteInt32LittleEndian(cheekPacket.AsSpan(8),
    BitConverter.SingleToInt32Bits(float.NaN));
Check(!CheekPuffTelemetry.TryParse(cheekPacket, out _, out _, out _),
    "cheek telemetry rejects nonfinite strength");
cheekPacket = CheekPuffTelemetry.Create(CheekPuffTelemetry.SourceSteamLink, .2f, .8f);
cheekPacket[5] = 1;
Check(!CheekPuffTelemetry.TryParse(cheekPacket, out _, out _, out _),
    "cheek telemetry rejects unsupported reserved flags");
Check(!CheekPuffTelemetry.TryParse(cheekPacket.AsSpan(0, 15), out _, out _, out _),
    "cheek telemetry rejects incomplete packets");

// Suck has its own XR_FB channels. A negative puff weight does not mean suck.
var suckTracker = new CheekSuckTracker();
float[] suckWeights = new float[70];
suckWeights[2] = -0.8f;
CheckSuck(suckTracker.Update(suckWeights, CheekSuckMode.Strong, 0), 0, 0,
    "negative cheek puff does not create cheek suck");
suckWeights[6] = 0.38f;
suckWeights[7] = 0.27f;
CheckSuck(suckTracker.Update(suckWeights, CheekSuckMode.Strong, 5), 1, 0,
    "left cheek suck suppresses the right");
CheckSuck(suckTracker.Update(suckWeights, CheekSuckMode.Off, 10), 0.38f, 0.27f,
    "off mode passes native cheek suck weights through");
CheckSuck(suckTracker.Update(suckWeights, CheekSuckMode.Balanced, 15),
    CheekSuckMapping.FromFaceWeights(suckWeights).Left,
    CheekSuckMapping.FromFaceWeights(suckWeights).Right,
    "balanced cheek suck matches side separation");
suckWeights[6] = 0.27f;
suckWeights[7] = 0.38f;
CheckSuck(suckTracker.Update(suckWeights, CheekSuckMode.Strong, 20), 0, 1,
    "right cheek suck suppresses the left");
suckWeights[6] = 0.8f;
suckWeights[7] = 0.8f;
CheckSuck(suckTracker.Update(suckWeights, CheekSuckMode.Strong, 25), 0, 1,
    "bilateral suck does not flash on during a side change");
CheckSuck(suckTracker.Update(suckWeights, CheekSuckMode.Strong, 245), 1, 1,
    "sustained bilateral cheek suck activates both sides");
suckWeights[6] = 0f;
suckWeights[7] = 0f;
CheckSuck(suckTracker.Update(suckWeights, CheekSuckMode.Strong, 250), 0, 0,
    "neutral releases both cheek suck weights");
suckWeights[6] = float.NaN;
suckWeights[7] = -0.2f;
CheckSuck(suckTracker.Update(suckWeights, CheekSuckMode.Off, 255), 0, 0,
    "invalid and negative cheek suck values stay neutral");

float[] browWeights = new float[70];
browWeights[0] = 0.20f; browWeights[1] = 0.60f;
browWeights[22] = 0.30f; browWeights[23] = 0.10f;
browWeights[57] = 0.80f; browWeights[58] = 0.40f;
browWeights[12] = 0.70f; // Eye closure must remain outside the brow mapper.
EyebrowWeights nativeBrow = EyebrowMapping.FromFaceWeights(browWeights, false, 3.0f);
CheckNear(nativeBrow.LowerLeft, 0.20f, "brow boost off passes native left lowerer");
CheckNear(nativeBrow.LowerRight, 0.60f, "brow boost off passes native right lowerer");
CheckNear(nativeBrow.InnerLeft, 0.30f, "brow boost off passes native left inner raise");
CheckNear(nativeBrow.InnerRight, 0.10f, "brow boost off passes native right inner raise");
CheckNear(nativeBrow.OuterLeft, 0.80f, "brow boost off passes native left outer raise");
CheckNear(nativeBrow.OuterRight, 0.40f, "brow boost off passes native right outer raise");
EyebrowWeights boostedBrow = EyebrowMapping.FromFaceWeights(browWeights, true, 1.5f);
CheckNear(boostedBrow.LowerLeft, 0.30f, "brow boost scales left lowerer");
CheckNear(boostedBrow.LowerRight, 0.90f, "brow boost scales right lowerer independently");
CheckNear(boostedBrow.InnerLeft, 0.45f, "brow boost scales left inner raise");
CheckNear(boostedBrow.InnerRight, 0.15f, "brow boost scales right inner raise independently");
CheckNear(boostedBrow.OuterLeft, 1.00f, "brow boost clips left outer raise");
CheckNear(boostedBrow.OuterRight, 0.60f, "brow boost scales right outer raise independently");
CheckNear(browWeights[12], 0.70f, "brow boost leaves source blink unchanged");
CheckNear(EyebrowMapping.Apply(float.NaN, true, 2.0f), 0.0f,
    "brow boost rejects invalid native weight");

CheckNear(source.Expressions[65], 0.35f, "Steam tongue alias");
CheckNear(source.Expressions[68], 0.8f, "misspelled TongueOut alias");
CheckNear(source.LeftOpenness, 0.4f, "blink-adjusted openness");
Check(source.HasFreshFace(1499) && !source.HasFreshFace(1501), "face freshness");
Check(source.HasFreshEye(1499) && !source.HasFreshEye(1501), "eye freshness");
Check(!source.HasAvailableLowerFace(1000) && !source.HasAvailableUpperFace(1000),
    "face weight packets alone do not imply face tracking capability");
Check(source.TryGetGaze(1000, out float lx, out float ly, out float rx, out float ry), "shared gaze");
CheckNear(lx, MathF.Atan2(0.1f, 1.0f), "shared gaze x");
CheckNear(ly, MathF.Atan2(0.2f, 1.0f), "shared gaze y");
CheckNear(lx, rx, "shared left/right x");
CheckNear(ly, ry, "shared left/right y");

byte[] splitFace = Bundle(Message("/sl/xrfb/facew/JawDrop", 0.5f),
    Message("/sl/xrfb/facew/CheekPuffL", 0.3f));
byte[] splitEye = Bundle(Message("/sl/xrfb/facew/EyesClosedR", 0.2f),
    Message("/sl/xrfb/facew/LidTightenerR", 0.4f));
Check(source.ParseDatagram(splitFace, 1001), "first live-style bundle parses");
Check(source.ParseDatagram(splitEye, 1002), "second live-style bundle parses");
CheckNear(source.Expressions[24], 0.5f, "split face weight retained");
CheckNear(source.Expressions[13], 0.2f, "split eye weight retained");
CheckNear(source.Expressions[2], 0.3f, "split cheek weight retained");
CheckNear(source.RightOpenness, 0.72f, "split right eye openness");

Check(source.ParseDatagram(Bundle(Message("/sl/xrfb/facew/TongueOut", 0.6f),
    Message("/sl/xrfb/facew/ToungeOut", 0.05f)), 1003), "duplicate tongue bundle parses");
CheckNear(source.Expressions[68], 0.6f, "canonical TongueOut wins over misspelling");

// The ordinary Steam Link tongue path must use TongueOut, not the adjacent
// TongueTipInterdental weight used by Virtual Desktop's other tongue layout.
Check(source.ParseDatagram(Bundle(
    Message("/sl/xrfb/facew/TongueTipInterdental", 0.05f),
    Message("/sl/xrfb/facew/TongueTipAlveolar", 0.25f),
    Message("/sl/xrfb/facew/TongueOut", 0.85f),
    Message("/sl/xrfb/facew/TongueRetreat", 0.15f)), 1004),
    "native Steam tongue bundle parses");
NativeTongueWeights? nativeTongue = NativeTongueMapping.Resolve(
    source.Expressions, NativeFaceSource.SteamLink, 3, customFresh: false, customEnabled: false);
Check(nativeTongue.HasValue, "Steam native tongue selected");
NativeTongueWeights steamNative = nativeTongue.GetValueOrDefault();
CheckNear(steamNative.Out, 0.85f, "native Steam TongueOut mapping");
Check(steamNative.CurlUp is null,
    "Steam TongueTipAlveolar does not imply VRCFT TongueCurlUp");
Check(steamNative.BendDown is null,
    "Steam TongueRetreat does not imply VRCFT TongueBendDown");
NativeTongueWeights? vdTongue = NativeTongueMapping.Resolve(
    source.Expressions, NativeFaceSource.VirtualDesktop, 3, customFresh: false, customEnabled: false);
Check(vdTongue.HasValue, "Virtual Desktop native tongue selected");
NativeTongueWeights vdNative = vdTongue.GetValueOrDefault();
CheckNear(vdNative.Out, 0.05f, "Virtual Desktop full-face tongue layout preserved");
CheckNear(vdNative.Left!.Value, 0.25f,
    "Virtual Desktop full-face directional layout preserved");
NativeTongueWeights? vdBasicTongue = NativeTongueMapping.Resolve(
    source.Expressions, NativeFaceSource.VirtualDesktop, 1, customFresh: false, customEnabled: false);
Check(vdBasicTongue.HasValue, "Virtual Desktop basic native tongue selected");
NativeTongueWeights vdBasicNative = vdBasicTongue.GetValueOrDefault();
CheckNear(vdBasicNative.Out, 0.85f, "Virtual Desktop basic tongue out preserved");
CheckNear(vdBasicNative.CurlUp!.Value, 0.25f,
    "Virtual Desktop basic TongueTipAlveolar fallback preserved");
Check(NativeTongueMapping.Resolve(source.Expressions, NativeFaceSource.SteamLink, 3,
    customFresh: true, customEnabled: true) is null, "fresh custom tongue overrides Steam native tongue");
Check(NativeTongueMapping.Resolve(source.Expressions, NativeFaceSource.VirtualDesktop, 3,
    customFresh: true, customEnabled: true) is null, "fresh custom tongue overrides Virtual Desktop native tongue");

// Reuse one output buffer as the native source changes. Optional channels from
// the previous layout must return to zero, including camera-only channels.
float[] tongueSlots = Enumerable.Repeat(0.7f, NativeTongueMapping.SlotCount).ToArray();
NativeTongueMapping.WriteSlots(vdNative, tongueSlots);
CheckNear(tongueSlots[3], 0.25f, "Virtual Desktop directional tongue written");
CheckNear(tongueSlots[5], 0.0f, "native tongue clears camera-only roll");
CheckNear(tongueSlots[7], 0.0f, "full-face layout clears prior curl");
NativeTongueMapping.WriteSlots(vdBasicNative, tongueSlots);
CheckNear(tongueSlots[3], 0.0f, "basic layout clears previous left direction");
CheckNear(tongueSlots[7], 0.25f, "basic layout writes curl");
NativeTongueMapping.WriteSlots(steamNative, tongueSlots);
CheckNear(tongueSlots[0], 0.85f, "Steam layout writes tongue out");
for (int index = 1; index < tongueSlots.Length; ++index)
    CheckNear(tongueSlots[index], 0.0f, $"Steam layout clears tongue slot {index}");

float[] lipWeights = new float[70];
lipWeights[45] = 0.9f;  // LipSuckLt
lipWeights[47] = 0.85f; // LipSuckRt
lipWeights[53] = 0.1f;  // MouthLeft
lipWeights[54] = 0.25f; // MouthRight
lipWeights[55] = 0.3f;  // NoseWrinklerL
lipWeights[56] = 0.2f;  // NoseWrinklerR
lipWeights[61] = 0.8f;  // UpperLipRaiserL
lipWeights[62] = 0.7f;  // UpperLipRaiserR
NativeLipWeights vdLips = NativeLipMapping.FromFaceWeights(lipWeights, NativeFaceSource.VirtualDesktop);
NativeLipWeights steamLips = NativeLipMapping.FromFaceWeights(lipWeights, NativeFaceSource.SteamLink);
CheckNear(vdLips.UpperUpLeft, 0.5f, "VD left upper lip nose correction");
CheckNear(vdLips.UpperDeepenLeft, 0.5f, "VD left upper lip deepen correction");
CheckNear(vdLips.UpperUpRight, 0.5f, "VD right upper lip nose correction");
CheckNear(vdLips.UpperDeepenRight, 0.5f, "VD right upper lip deepen correction");
CheckNear(steamLips.UpperUpLeft, 0.5f, "Steam left upper lip nose correction");
CheckNear(steamLips.UpperDeepenLeft, 0.2f, "Steam left upper lip deepen correction");
CheckNear(steamLips.UpperUpRight, 0.5f, "Steam right upper lip nose correction");
CheckNear(steamLips.UpperDeepenRight, 0.3f, "Steam right upper lip deepen correction");
CheckNear(vdLips.SuckUpperLeft, 1.0f - MathF.Pow(0.8f, 1.0f / 6.0f),
    "VD left lip suck uses upper lip raiser");
CheckNear(vdLips.SuckUpperRight, 1.0f - MathF.Pow(0.7f, 1.0f / 6.0f),
    "VD right lip suck uses upper lip raiser");
CheckNear(steamLips.SuckUpperLeft, 1.0f - MathF.Pow(0.1f, 1.0f / 6.0f),
    "Steam left lip suck uses mouth left");
CheckNear(steamLips.SuckUpperRight, 1.0f - MathF.Pow(0.25f, 1.0f / 6.0f),
    "Steam right lip suck uses mouth right");

float[] smirkWeights = new float[70];
Check(SmirkMapping.FromFaceWeights(smirkWeights) == new SmirkWeights(0, 0),
    "neutral mouth remains neutral");
smirkWeights[32] = 0.102f; smirkWeights[33] = 0.068f;
smirkWeights[10] = 0.065f; smirkWeights[11] = 0.031f;
SmirkWeights leftSmirk = SmirkMapping.FromFaceWeights(smirkWeights);
Check(leftSmirk.Left > 0.25f && leftSmirk.Right == 0.0f,
    "measured left smirk becomes clear and stays unilateral");
smirkWeights[10] = 0; smirkWeights[11] = 0;
CheckNear(SmirkMapping.FromFaceWeights(smirkWeights).Right, 0,
    "clear one-sided puller works without a dimple signal");
smirkWeights[32] = 0.072f; smirkWeights[33] = 0.124f;
smirkWeights[10] = 0.015f; smirkWeights[11] = 0.061f;
SmirkWeights rightSmirk = SmirkMapping.FromFaceWeights(smirkWeights);
Check(rightSmirk.Right > 0.35f && rightSmirk.Left == 0.0f,
    "measured right smirk becomes clear and stays unilateral");
smirkWeights[32] = 0.188f; smirkWeights[33] = 0.199f;
smirkWeights[10] = 0.056f; smirkWeights[11] = 0.064f;
Check(SmirkMapping.FromFaceWeights(smirkWeights) == new SmirkWeights(0.188f, 0.199f),
    "measured gentle two-sided smile stays native");
smirkWeights[32] = 0.736f; smirkWeights[33] = 0.769f;
smirkWeights[10] = 0.251f; smirkWeights[11] = 0.251f;
Check(SmirkMapping.FromFaceWeights(smirkWeights) == new SmirkWeights(0.736f, 0.769f),
    "measured full smile stays native");
smirkWeights[32] = float.NaN; smirkWeights[33] = float.PositiveInfinity;
Check(SmirkMapping.FromFaceWeights(smirkWeights) == new SmirkWeights(0, 0),
    "invalid mouth weights cannot leak into output");

using (var smirkSteam = new SteamOscSource(port: 0))
{
    Check(smirkSteam.ParseDatagram(Bundle(
        Message("/sl/xrfb/facew/LipCornerPullerL", 0.102f),
        Message("/sl/xrfb/facew/LipCornerPullerR", 0.068f),
        Message("/sl/xrfb/facew/DimplerL", 0.065f),
        Message("/sl/xrfb/facew/DimplerR", 0.031f)), 1000),
        "Steam Link unilateral smile parses");
    SmirkWeights steamSmirk = SmirkMapping.FromFaceWeights(smirkSteam.Expressions);
    CheckNear(steamSmirk.Left, leftSmirk.Left, "Steam and Virtual Desktop left smirk agree");
    CheckNear(steamSmirk.Right, leftSmirk.Right, "Steam and Virtual Desktop relaxed side agree");
}

var smirkTracker = new SmirkTracker();
Array.Clear(smirkWeights);
Check(smirkTracker.Update(smirkWeights, 0) == new SmirkWeights(0, 0),
    "smirk tracker starts neutral");
smirkWeights[32] = 0.102f; smirkWeights[33] = 0.068f;
smirkWeights[10] = 0.065f; smirkWeights[11] = 0.031f;
SmirkWeights smirkRise = smirkTracker.Update(smirkWeights, 20);
Check(smirkRise.Left > 0 && smirkRise.Left < leftSmirk.Left,
    "one-sided smile rises gradually");
CheckNear(smirkRise.Right, 0, "inactive smirk side is exactly zero on entry");
SmirkWeights smirkHeld = smirkTracker.Update(smirkWeights, 100);
Check(smirkHeld.Left > smirkRise.Left && smirkHeld.Right == 0.0f,
    "held smirk settles on the active side");
smirkWeights[32] = 0.072f; smirkWeights[33] = 0.124f;
smirkWeights[10] = 0.015f; smirkWeights[11] = 0.061f;
SmirkWeights pendingSwitch = smirkTracker.Update(smirkWeights, 120);
CheckNear(pendingSwitch.Right, 0, "brief opposite lead cannot bounce the inactive side");
SmirkWeights smirkSwitch = smirkTracker.Update(smirkWeights, 190);
CheckNear(smirkSwitch.Left, 0, "confirmed side switch clears old side exactly");
Check(smirkSwitch.Right > 0, "confirmed side switch raises new side");
Array.Clear(smirkWeights);
SmirkWeights pendingRelease = smirkTracker.Update(smirkWeights, 210);
CheckNear(pendingRelease.Left, 0, "pending release cannot wake old side");
SmirkWeights smirkRelease = smirkTracker.Update(smirkWeights, 280);
Check(smirkRelease.Right < pendingRelease.Right,
    "confirmed release moves toward neutral");

Check(source.ParseDatagram(Message("/avatar/parameters/LeftEyeX", 0.25f), 1010), "per-eye x left");
Check(source.ParseDatagram(Message("/avatar/parameters/LeftEyeY", -0.3f), 1010), "per-eye y left");
Check(source.ParseDatagram(Message("/avatar/parameters/RightEyeX", -0.2f), 1010), "per-eye x right");
Check(source.ParseDatagram(Message("/avatar/parameters/RightEyeY", 0.15f), 1010), "per-eye y right");
Check(source.TryGetRawPerEyeGaze(1010, out lx, out ly, out rx, out ry), "raw per-eye gaze diagnostic");
CheckNear(lx, 0.25f, "per-eye left x");
CheckNear(ly, -0.3f, "per-eye left y");
CheckNear(rx, -0.2f, "per-eye right x");
CheckNear(ry, 0.15f, "per-eye right y");
Check(source.TryGetGaze(1010, out lx, out ly, out rx, out ry), "usable gaze remains shared");
CheckNear(lx, rx, "raw per-eye gaze does not override shared angles");
CheckNear(ly, ry, "raw per-eye gaze does not override shared angles y");

Check(source.ParseDatagram(Bundle(
    Message("/sl/xrfb/facec/LowerFace", 0),
    Message("/sl/xrfb/facec/UpperFace", 0)), 1040), "false face capabilities parse");
Check(source.HasFreshLowerFaceCapability(1040) && source.HasFreshUpperFaceCapability(1040),
    "false face capabilities remain distinguishable from missing messages");
Check(!source.HasAvailableLowerFace(1040) && !source.HasAvailableUpperFace(1040),
    "false face capabilities do not mark tracking available");
Check(source.ParseDatagram(Bundle(
    Message("/sl/xrfb/facec/LowerFace", 1),
    Message("/sl/xrfb/facec/UpperFace", 1)), 1050), "true face capabilities parse");
Check(source.HasAvailableLowerFace(1549) && source.HasAvailableUpperFace(1549),
    "true face capabilities stay available within freshness window");
Check(!source.HasFreshLowerFaceCapability(1551) && !source.HasFreshUpperFaceCapability(1551) &&
    !source.HasAvailableLowerFace(1551) && !source.HasAvailableUpperFace(1551),
    "stale true face capabilities no longer mark tracking available");

byte[] invalidSize = (byte[])frame.Clone();
BinaryPrimitives.WriteInt32BigEndian(invalidSize.AsSpan(16, 4), int.MaxValue);
Check(!source.ParseDatagram(invalidSize, 1020), "bundle element length checked");
Check(!source.ParseDatagram(new byte[16 * 1024 + 1], 1020), "oversize datagram rejected");
byte[] nan = Message("/sl/xrfb/facew/CheekPuffL", float.NaN);
Check(!source.ParseDatagram(nan, 1020), "non-finite weights rejected");
CheckNear(source.Expressions[2], 0.3f, "rejected data leaves value unchanged");
byte[] lateCorruption = Bundle(Message("/sl/xrfb/facew/CheekPuffL", 0.01f), nan);
Check(!source.ParseDatagram(lateCorruption, 1030), "later corrupt bundle item rejected");
CheckNear(source.Expressions[2], 0.3f, "whole corrupt bundle leaves values unchanged");
Console.WriteLine("Steam OSC parser tests passed");

static byte[] Message(string address, params float[] values)
{
    using var output = new MemoryStream();
    WriteString(output, address);
    WriteString(output, "," + new string('f', values.Length));
    Span<byte> bits = stackalloc byte[4];
    foreach (float value in values)
    {
        BinaryPrimitives.WriteInt32BigEndian(bits, BitConverter.SingleToInt32Bits(value));
        output.Write(bits);
    }
    return output.ToArray();
}

static byte[] Bundle(params byte[][] elements)
{
    using var output = new MemoryStream();
    output.Write("#bundle\0"u8);
    output.Write(new byte[8]);
    Span<byte> length = stackalloc byte[4];
    foreach (byte[] element in elements)
    {
        BinaryPrimitives.WriteInt32BigEndian(length, element.Length);
        output.Write(length);
        output.Write(element);
    }
    return output.ToArray();
}

static void WriteString(Stream stream, string value)
{
    byte[] chars = Encoding.ASCII.GetBytes(value);
    stream.Write(chars);
    stream.WriteByte(0);
    for (int padded = chars.Length + 1; padded % 4 != 0; padded++)
        stream.WriteByte(0);
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void CheckNear(float actual, float expected, string message)
{
    if (MathF.Abs(actual - expected) > 0.00001f)
        throw new Exception($"{message}: actual={actual}, expected={expected}");
}

static CheekPuffWeights TrackCheeks(CheekPuffTracker tracker, float[] weights,
    float left, float right, long nowMs, bool strong = true)
{
    weights[2] = left;
    weights[3] = right;
    return tracker.Update(weights, strong, nowMs);
}

static void CheckCheeks(CheekPuffWeights actual, float left, float right, string message)
{
    CheckNear(actual.Left, left, message + " left");
    CheckNear(actual.Right, right, message + " right");
}

static void CheckSuck(CheekSuckWeights actual, float left, float right, string message)
{
    CheckNear(actual.Left, left, message + " left");
    CheckNear(actual.Right, right, message + " right");
}
