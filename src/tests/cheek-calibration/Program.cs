using Qpro.Shared;
using Qpro.GazeBridge;

int checks = 0;
List<string> failures = [];

try
{
    RunChecks();
}
catch (Exception error)
{
    // Report setup, helper and I/O failures without an unhandled CLR crash or
    // Windows Error Reporting dialog. The nonzero result remains a real fail.
    Console.Error.WriteLine($"FAIL: cheek calibration harness stopped after {checks} checks.");
    foreach (string failure in failures) Console.Error.WriteLine(failure);
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1;
}

void RunChecks()
{
// Explicit diagnostics exercise both failure paths without changing a product
// file or making normal test runs pass when an error occurs.
if (args.Contains("--verify-exception-handler"))
    throw new InvalidOperationException("Deliberately injected harness exception for failure-handler verification.");
if (args.Contains("--verify-assertion-handler"))
    Check(false, "Deliberately injected failed assertion for failure-handler verification.");

// A real opening frame from the existing Quest Pro replay has a right lead,
// yet the old feed emits equal strengths and loses that useful information.
float[] moderateFrame = new float[70];
moderateFrame[2] = .67526615f; moderateFrame[3] = .8001669f;
var erasedLead = CheekPuffMapping.FromFaceWeights(moderateFrame);
Check(MathF.Abs(erasedLead.Left - erasedLead.Right) < .00001f,
    "recorded moderate side lead is demonstrably lost in the old feed");
var nativeLead = CheekPuffMapping.RawFromFaceWeights(moderateFrame);
Check(nativeLead.Right - nativeLead.Left > .12f,
    "the new native calibration feed retains that recorded side lead");

// Synthetic paired holds: the inactive native channel rises almost as much
// as the active one. The former Balanced feed lost that moderate side lead.
var neutral = Hold(.05f, .04f);
var left = Hold(.65f, .55f);
var right = Hold(.49f, .69f);
var both = Hold(.92f, .94f);
Check(CheekPuffCalibrationCapture.TryAcceptNeutral(neutral, out _), "stable relaxed hold accepted");
Check(CheekPuffCalibrationCapture.TryAcceptSide(left, neutral, true, out _, out _, rawInputs: true),
    "moderate left lead accepted despite native cross-talk");
Check(CheekPuffCalibrationCapture.TryAcceptSide(right, neutral, false, out _, out _, rawInputs: true),
    "moderate right lead accepted despite native cross-talk");
Check(CheekPuffCalibrationCapture.TryCreateRaw(neutral, left, right, both, out var profile, out _),
    "four measured holds produce a usable fit");
Check(CheekPuffCalibrationProfile.IsValid(profile), "raw fit passes profile validity checks");
CheckNear(Map(.05f, .04f, profile), 0, 0, "relaxed cheeks remain neutral");
CheckNear(Map(.65f, .55f, profile), 1, 0, "left anchor separates coupled native channels");
CheckNear(Map(.49f, .69f, profile), 0, 1, "right anchor separates coupled native channels");
CheckNear(Map(.92f, .94f, profile), 1, 1, "measured both anchor reaches full on both sides");

// Partial single/both holds use the same measured axes; outputs must remain
// continuous and ordered rather than snapping every recognized pose to 1.
float previous = 0;
foreach (float strength in new[] { .10f, .25f, .5f, .75f, 1f })
{
    var gentleLeft = Map(.05f + .60f * strength, .04f + .51f * strength, profile);
    Check(gentleLeft.Left > previous && gentleLeft.Right < .001f,
        $"left strength {strength} is ordered and independent");
    previous = gentleLeft.Left;
    var gentleRight = Map(.05f + .44f * strength, .04f + .65f * strength, profile);
    Check(gentleRight.Right > 0 && gentleRight.Left < .001f,
        $"right strength {strength} is independent");
    var gentleBoth = Map(.05f + .87f * strength, .04f + .90f * strength, profile);
    Check(MathF.Abs(gentleBoth.Left - gentleBoth.Right) < .01f && gentleBoth.Left > 0,
        $"bilateral strength {strength} remains bilateral despite unequal native gains");
}

for (int i = 0; i < 100; i++)
{
    var idle = Map(.05f + MathF.Sin(i) * .001f, .04f + MathF.Cos(i * 1.7f) * .001f, profile);
    Check(idle.Left == 0 && idle.Right == 0, "captured neutral noise cannot become a puff");
}

// Low but stable ranges are usable. Noise on the same scale as the signal,
// movement of the inactive cheek, and wrong-side/both holds must be rejected.
var lowNeutral = Hold(.03f, .02f, .0001f);
var lowLeft = Hold(.06f, .03f, .0001f);
var lowRight = Hold(.04f, .05f, .0001f);
Check(CheekPuffCalibrationCapture.TryAcceptSide(lowLeft, lowNeutral, true, out _, out _, true),
    "stable low-range left pose accepted");
Check(CheekPuffCalibrationCapture.TryCreateRaw(lowNeutral, lowLeft, lowRight, Hold(.07f, .06f, .0001f),
    out var lowProfile, out _), "stable low-range profile accepted");
CheckNear(Map(.06f, .03f, lowProfile), 1, 0, "low-range left still reaches full without opposite activation");
Check(!CheekPuffCalibrationCapture.TryAcceptSide(Hold(.06f, .03f, .015f), lowNeutral, true, out _, out _, true),
    "noise larger than the puff range is rejected");
Check(!CheekPuffCalibrationCapture.TryAcceptSide(Hold(.06f, .03f, .0001f, .015f), lowNeutral, true, out _, out _, true),
    "unstable inactive cheek is rejected");
Check(!CheekPuffCalibrationCapture.TryAcceptSide(Hold(.04f, .07f), lowNeutral, true, out _, out _, true),
    "requested left pose rejects a right lead");
Check(CheekPuffCalibrationCapture.TryAcceptSide(Hold(.43f, .42f), lowNeutral, true, out _, out _, true),
    "raw channel orientation is deferred until the second labeled pose");
Check(!CheekPuffCalibrationCapture.TryCreateRaw(lowNeutral, Hold(.43f, .42f), Hold(.43f, .42f),
    Hold(.5f, .5f), out _, out _), "two identical common poses cannot masquerade as separate cheeks");
Check(!CheekPuffCalibrationCapture.TryCreateRaw(neutral, Hold(.65f, .64f), Hold(.64f, .65f), both,
    out _, out _), "near-identical single-cheek responses cannot produce a sensitive inverse");
Check(!CheekPuffCalibrationCapture.TryCreateRaw(neutral, left, right, left, out _, out _),
    "the final both-cheek hold cannot actually be a single cheek");
Check(!CheekPuffCalibrationCapture.TryCreateRaw(neutral, Hold(.85f, .74f, .03f),
    Hold(.75f, .84f, .03f), Hold(.95f, .94f), out _, out _),
    "noise is checked after cross-talk inversion rather than only against native gain");
Check(!CheekPuffCalibrationCapture.TrySummarize(Enumerable.Repeat((.1f, .1f), 10).ToArray(), out _, out _),
    "insufficient samples rejected");
Check(!CheekPuffCalibrationCapture.TrySummarize(Enumerable.Repeat((float.NaN, .1f), 64).ToArray(), out _, out _),
    "nonfinite samples rejected before fitting");

// The left native range is much smaller than the right. Raw baselines also
// differ, so classification must use the measured deltas, not absolute weights.
var asymmetricNeutral = Hold(.05f, .04f, .0001f);
Check(CheekPuffCalibrationCapture.TryCreateRaw(asymmetricNeutral,
    Hold(.09f, .055f, .0001f), Hold(.07f, .64f, .0001f), Hold(.12f, .66f, .0001f),
    out var asymmetric, out _), "large per-cheek range asymmetry fits");
CheckNear(Map(.09f, .055f, asymmetric), 1, 0, "asymmetric left range normalizes independently");
CheckNear(Map(.07f, .64f, asymmetric), 0, 1, "asymmetric right range normalizes independently");
CheckNear(Map(.12f, .66f, asymmetric), 1, 1, "asymmetric bilateral anchor is preserved");

CheekPuffCalibration weakBoth = new(.1f, .6f, .1f, .6f, .005f, .005f, new(.1f, .1f, .18f, .18f));
Check(CheekPuffCalibrationProfile.IsValid(weakBoth), "quiet weak bilateral anchor has a bounded noise margin");
CheckNear(Map(.105f, .105f, weakBoth), 0, 0, "bilateral gain cannot amplify captured neutral noise");
CheckNear(Map(.18f, .18f, weakBoth), 1, 1, "weak but measured bilateral anchor still reaches full");
Check(!CheekPuffCalibrationProfile.IsValid(weakBoth with { LeftDeadZone = .025f, RightDeadZone = .025f }),
    "bilateral gain rejects excessive transformed neutral noise");

// Channel amplitude is not cheek identity. These labeled responses have a
// larger right-channel rise even for a true left puff, but separate cleanly.
var gainAsymmetricNeutral = Hold(.05f, .04f, .0001f);
var gainAsymmetricLeft = Hold(.09f, .14f, .0001f);
var gainAsymmetricRight = Hold(.06f, .64f, .0001f);
Check(CheekPuffCalibrationCapture.TryAcceptSide(gainAsymmetricLeft, gainAsymmetricNeutral,
    true, out _, out _, true), "a useful left pose is not rejected because the right native channel has greater gain");
Check(CheekPuffCalibrationCapture.TryCreateRaw(gainAsymmetricNeutral, gainAsymmetricLeft,
    gainAsymmetricRight, Hold(.10f, .72f, .0001f), out var gainAsymmetric, out _),
    "unequal native gains still produce distinct labeled axes");
CheckNear(Map(.09f, .14f, gainAsymmetric), 1, 0, "gain-asymmetric left anchor reaches full only on the requested cheek");
CheckNear(Map(.06f, .64f, gainAsymmetric), 0, 1, "gain-asymmetric right anchor reaches full only on the requested cheek");
Check(!CheekPuffCalibrationCapture.TryCreateRaw(gainAsymmetricNeutral, gainAsymmetricRight,
    gainAsymmetricLeft, Hold(.1f, .72f, .0001f), out _, out _), "reversed labeled axes cannot replace a valid profile");

CheekPuffCalibration fragile = new(.05f, .075f, .05f, .075f, .00005f, .00005f,
    new(.073f, .073f, .09f, .09f));
Check(!CheekPuffCalibrationProfile.IsValid(fragile),
    "a quiet near-collinear low-range fit is rejected before slight later differential noise can become a large puff");
Check(CheekPuffCalibrationCapture.TryCreateRaw(Hold(.05f, .05f, .00001f),
    Hold(.45f, .40f, .00001f), Hold(.40f, .45f, .00001f), Hold(.75f, .75f, .00001f),
    out _, out _), "substantial but overlapping native ranges remain usable");
CheekPuffCalibration sensitivityBoundary = new(.05f, .07f, .05f, .07f, .0001f, .0001f,
    new(.05f, .05f, .07f, .07f));
Check(CheekPuffCalibrationProfile.IsValid(sensitivityBoundary),
    "uncoupled minimum range with the standard dead-zone floor remains usable");
Check(!CheekPuffCalibrationProfile.IsValid(sensitivityBoundary with { LeftDeadZone = .0035f, RightDeadZone = .0035f }),
    "the maximum sensitivity includes final normalization's additional gain");
Check(CheekPuffCalibrationProfile.IsValid(new(.05f, .07f, .05f, .07f, .0035f, .0035f)),
    "legacy profile validity retains its prior meaning");
var measured = profile.RawResponse!.Value;
Check(MathF.Abs(Map(profile.LeftFull, measured.RightOnLeft, profile).Left - 1) < .00001f,
    "the exact measured left anchor remains full after the tighter Jacobian noise guard");
Check(MathF.Abs(Map(measured.LeftOnRight, profile.RightFull, profile).Right - 1) < .00001f,
    "the exact measured right anchor remains full after the tighter Jacobian noise guard");
var measuredBoth = Map(measured.BothLeft, measured.BothRight, profile);
Check(MathF.Abs(measuredBoth.Left - 1) < .00001f && MathF.Abs(measuredBoth.Right - 1) < .00001f,
    "the exact measured bilateral anchor remains full after the tighter Jacobian noise guard");

// A comfortable full hold can vary slightly over three seconds. Its strong
// sustained section supplies the anchor, while uncertainty is retained from
// the entire capture, including chronological drift and the ending.
var unevenSamples = Enumerable.Range(0, 64).Select(i =>
{
    float strength = i < 24 ? 1 : i < 44 ? .9f : .95f;
    return (.05f + .6f * strength, .04f + .51f * strength);
}).ToArray();
Check(CheekPuffCalibrationCapture.TrySummarizeSteadyPuff(unevenSamples, neutral, 1, out var unevenLeft, out _),
    "a mildly uneven hold has a sustained usable full-strength section");
Check(MathF.Abs(unevenLeft.LeftMedian - .65f) < .001f,
    "the sustained full anchor is not reduced to the whole-hold fatigue median");
Check(CheekPuffCalibrationCapture.TryCreateRaw(neutral, unevenLeft, right, both, out _, out _),
    "correlated puff variation is assessed from actual paired samples rather than independent worst-case noise");
var outlierSamples = Enumerable.Repeat((.65f, .55f), 64).ToArray();
outlierSamples[11] = (.98f, .04f);
outlierSamples[63] = (.05f, .04f);
Check(CheekPuffCalibrationCapture.TrySummarizeSteadyPuff(outlierSamples, neutral, 1, out var outlierLeft, out _),
    "isolated spikes and one bad final sample cannot poison a sustained hold");
Check(MathF.Abs(outlierLeft.LeftMedian - .65f) < .001f && MathF.Abs(outlierLeft.RightMedian - .55f) < .001f,
    "bounded isolated outliers preserve the paired anchor");
var releasedSamples = Enumerable.Repeat((.65f, .55f), 64).ToArray();
for (int i = 59; i < 64; i++) releasedSamples[i] = (.05f, .04f);
Check(!CheekPuffCalibrationCapture.TrySummarizeSteadyPuff(releasedSamples, neutral, 1, out _, out string releaseProblem)
    && releaseProblem.Contains("released"), "the final few released frames cannot hide behind sorted percentiles");
var rampSamples = Enumerable.Range(0, 64).Select(i => (.20f + .04f * i / 63, .12f + .02f * i / 63)).ToArray();
Check(!CheekPuffCalibrationCapture.TrySummarizeSteadyPuff(rampSamples, neutral, 1, out _, out _),
    "a whole-hold .20-to-.24 ramp cannot pass by selecting its final plateau window");
var shakySamples = Enumerable.Range(0, 64).Select(i => i % 2 == 0 ? (.65f, .55f) : (.40f, .20f)).ToArray();
Check(!CheekPuffCalibrationCapture.TrySummarizeSteadyPuff(shakySamples, neutral, 1, out _, out _),
    "a shaky hold has no sustained usable section");
var twitchSamples = Enumerable.Range(0, 64).Select(i => i is >= 20 and < 30 ? (.65f, .55f) : (.05f, .04f)).ToArray();
Check(!CheekPuffCalibrationCapture.TrySummarizeSteadyPuff(twitchSamples, neutral, 1, out _, out _),
    "a brief puff cannot supply a full hold");
var neutralRamp = Enumerable.Range(0, 64).Select(i => (.06f + .04f * i / 63, .04f)).ToArray();
Check(CheekPuffCalibrationCapture.TrySummarize(neutralRamp, out var driftingNeutral, out _)
    && !CheekPuffCalibrationCapture.TryAcceptNeutral(driftingNeutral, out _),
    "relaxed chronological drift is checked independently of sorted spread");
var asymmetricNoise = Enumerable.Range(0, 64).Select(i => (.05f, i % 10 < 3 ? .035f : .04f)).ToArray();
Check(CheekPuffCalibrationCapture.TrySummarize(asymmetricNoise, out var downwardNeutral, out _)
    && CheekPuffCalibrationCapture.TryCreateRaw(downwardNeutral, left, right, both, out var downwardProfile, out _)
    && downwardProfile.RightDeadZone >= .004f,
    "neutral noise includes downward as well as upward excursions in the inactive native channel");

// The Quest Pro capture already present in the Steam OSC regression tests
// crosses a symmetric plateau for ~90 ms. Legacy calibration used to send
// both cheeks here. Keep the old baseline strengths but confirm pose changes.
var legacy = DeveloperCheekPuffBaseline.ForSource(CheekPuffCalibrationProfile.VirtualDesktopSource)!.Value;
var tracker = new CheekPuffTracker();
float[] weights = new float[70];
(long Ms, float Left, float Right)[] replay =
[
    (19698, .08293416f, .03803299f), (19791, .08523f, .04021459f),
    (19855, .13476846f, .065627575f), (19917, .24253877f, .1512201f),
    (20039, .39829665f, .28947696f), (20071, .5680012f, .43751585f),
    (20102, .8825973f, .7516156f), (20133, .9970358f, .994524f),
    (20164, .9999126f, .9998589f), (20195, .999997f, .99999577f),
    (20226, .89692426f, .9999999f), (20257, .73659927f, .84164476f),
    (20289, .7034784f, .78216815f), (20319, .6478207f, .7534606f),
    (20350, .63491404f, .7467608f)
];
foreach (var frame in replay)
{
    weights[2] = frame.Left; weights[3] = frame.Right;
    var result = tracker.Update(weights, CheekPuffMode.Calibrated, frame.Ms, legacy);
    Check(frame.Ms < 20289 ? result.Right == 0 && result.Left > 0 : result.Left == 0 && result.Right > 0,
        $"recorded side switch does not activate the opposite cheek at {frame.Ms}");
}
weights[2] = weights[3] = .7f;
tracker.Update(weights, CheekPuffMode.Calibrated, 20400, legacy);
var heldBoth = tracker.Update(weights, CheekPuffMode.Calibrated, 20630, legacy);
Check(heldBoth.Left > 0 && heldBoth.Right > 0, "sustained two-cheek pose follows a one-cheek pose");
CheckNear(tracker.Update(weights, CheekPuffMode.Off, 20640, profile), .7f, .7f,
    "Off restores native weights even after a calibrated switch");

// Newly fitted profiles retain continuous strength while their temporal
// output confirms a side switch and clears the old cheek's smoothing tail.
tracker.Reset();
weights[2] = .65f; weights[3] = .55f;
CheckNear(tracker.Update(weights, CheekPuffMode.Calibrated, 30000, profile), 1, 0, "raw profile starts on left");
weights[2] = .92f; weights[3] = .94f;
for (long ms = 30020; ms < 30120; ms += 20)
    Check(tracker.Update(weights, CheekPuffMode.Calibrated, ms, profile).Right == 0,
        "short symmetric crossover cannot puff the right cheek");
weights[2] = .49f; weights[3] = .69f;
tracker.Update(weights, CheekPuffMode.Calibrated, 30120, profile);
var switched = tracker.Update(weights, CheekPuffMode.Calibrated, 30180, profile);
Check(switched.Left == 0 && switched.Right > 0, "confirmed raw side switch has no old-cheek tail");
tracker.Reset();
weights[2] = .65f; weights[3] = .55f;
tracker.Update(weights, CheekPuffMode.Calibrated, 40000, profile);
weights[2] = .05f + .44f * .08f; weights[3] = .04f + .65f * .08f;
tracker.Update(weights, CheekPuffMode.Calibrated, 40010, profile);
var weakSwitch = tracker.Update(weights, CheekPuffMode.Calibrated, 40080, profile);
Check(weakSwitch.Left == 0 && weakSwitch.Right is > 0 and < .1f,
    "a held gentle opposite puff can switch sides without passing through full neutral");

// Points inside the four measured-anchor triangles are synthetic continuity
// cases, not claims about unmeasured wearer physiology.
foreach (var desired in new[] { (.7f, .3f), (.5f, .2f), (.3f, .7f), (.2f, .5f), (.2f, .07f) })
{
    var native = MixedNative(profile, desired.Item1, desired.Item2);
    weights[2] = native.Left; weights[3] = native.Right;
    tracker.Reset();
    var mixed = tracker.Update(weights, CheekPuffMode.Calibrated, 50000, profile);
    var direct = Map(native.Left, native.Right, profile);
    CheckNear(mixed, direct.Left, direct.Right, $"unequal simultaneous strengths {desired} preserve both corrected axes from neutral");
    Check(mixed.Left > .03f && mixed.Right > .03f, "the weaker active cheek is not removed by raw asymmetry");
}
tracker.Reset();
weights[2] = .65f; weights[3] = .55f;
tracker.Update(weights, CheekPuffMode.Calibrated, 60000, profile);
var unevenBothNative = MixedNative(profile, .7f, .3f);
weights[2] = unevenBothNative.Left; weights[3] = unevenBothNative.Right;
for (long ms = 60020; ms <= 60220; ms += 20)
    Check(tracker.Update(weights, CheekPuffMode.Calibrated, ms, profile).Right == 0,
        "uneven simultaneous input retains the transient side-crossover guard");
for (long ms = 60240; ms <= 60900; ms += 20)
    tracker.Update(weights, CheekPuffMode.Calibrated, ms, profile);
var sustainedUneven = tracker.Update(weights, CheekPuffMode.Calibrated, 60920, profile);
Check(sustainedUneven.Left > .6f && sustainedUneven.Right > .2f,
    "a steady weaker cheek joins an established individual pose after confirmation");
weights[2] = .65f; weights[3] = .55f;
tracker.Update(weights, CheekPuffMode.Calibrated, 60940, profile);
var singleAfterBoth = tracker.Update(weights, CheekPuffMode.Calibrated, 61020, profile);
Check(singleAfterBoth.Left > .6f && singleAfterBoth.Right == 0,
    "both-to-individual transition clears the released cheek after confirmation");
var gentleBothNative = MixedNative(profile, .7f, .08f);
weights[2] = gentleBothNative.Left; weights[3] = gentleBothNative.Right;
for (long ms = 61040; ms <= 61800; ms += 20)
    tracker.Update(weights, CheekPuffMode.Calibrated, ms, profile);
var sustainedGentleBoth = tracker.Update(weights, CheekPuffMode.Calibrated, 61820, profile);
Check(sustainedGentleBoth.Left > .6f && sustainedGentleBoth.Right is > .04f and < .1f,
    "a gentle steady second cheek is admitted without needing equal strength");

// Packet versions prevent the new raw fit from being fed already-separated
// old module values. Sources remain explicit for VD/Steam Link isolation.
foreach (byte source in new[] { CheekPuffTelemetry.SourceVirtualDesktop, CheekPuffTelemetry.SourceSteamLink })
{
    byte[] packet = CheekPuffTelemetry.CreateRaw(source, .65f, .55f);
    Check(CheekPuffTelemetry.TryParse(packet, out byte actualSource, out float actualLeft, out float actualRight, out bool raw)
        && raw && actualSource == source && actualLeft == .65f && actualRight == .55f, "raw feed preserves source and moderate lead");
    Check(CheekPuffTelemetry.TryParse(CheekPuffTelemetry.Create(source, .3f, 0), out _, out _, out _, out raw) && !raw,
        "legacy separated feed is explicitly identifiable");
    packet[3] = (byte)'9';
    Check(!CheekPuffTelemetry.TryParse(packet, out _, out _, out _, out _), "unsupported feed version rejected");
}

string profileDirectory = Path.Combine(Path.GetTempPath(), "qpro-cheek-fit-" + Guid.NewGuid().ToString("N"));
try
{
    CheekPuffCalibrationProfile.Save(CheekPuffCalibrationProfile.VirtualDesktopSource, profile, profileDirectory);
    CheekPuffCalibrationProfile.Save(CheekPuffCalibrationProfile.SteamLinkSource, lowProfile, profileDirectory);
    Check(CheekPuffCalibrationProfile.Load(CheekPuffCalibrationProfile.VirtualDesktopSource, profileDirectory) == profile,
        "raw correction, noise and both anchors round trip");
    Check(CheekPuffCalibrationProfile.Load(CheekPuffCalibrationProfile.SteamLinkSource, profileDirectory) == lowProfile,
        "Steam Link personal profile remains independent");
    File.WriteAllText(CheekPuffCalibrationProfile.ProfilePath(CheekPuffCalibrationProfile.VirtualDesktopSource, profileDirectory),
        "{\"version\":1,\"leftNeutral\":0,\"leftFull\":0.4,\"rightNeutral\":0,\"rightFull\":0.6}");
    Check(CheekPuffCalibrationProfile.Load(CheekPuffCalibrationProfile.VirtualDesktopSource, profileDirectory) == new CheekPuffCalibration(0, .4f, 0, .6f),
        "existing version 1 Balanced anchors retain their original meaning");
}
finally
{
    // The path is a newly created, absolute child of this machine's temp
    // directory; remove only files generated by this test, without recursion.
    if (Directory.Exists(profileDirectory))
    {
        foreach (string file in Directory.EnumerateFiles(profileDirectory)) File.Delete(file);
        Directory.Delete(profileDirectory);
    }
}
string harnessResult = $"{(failures.Count == 0 ? "PASS" : "FAIL")}: {checks} cheek calibration and replay checks (offline; wearer validation pending).";
if (failures.Count == 0) Console.WriteLine(harnessResult);
else Console.Error.WriteLine(harnessResult);
foreach (string failure in failures) Console.Error.WriteLine(failure);
if (failures.Count > 0) Environment.ExitCode = 1;
}

CheekPuffPoseSummary Hold(float left, float right, float noise = .001f, float? rightNoise = null)
{
    var samples = Enumerable.Range(0, 64).Select(i =>
        (left + noise * MathF.Sin(i * 1.7f), right + (rightNoise ?? noise) * MathF.Sin(i * 2.3f))).ToArray();
    if (!CheekPuffCalibrationCapture.TrySummarize(samples, out var summary, out string problem))
        throw new Exception(problem);
    return summary;
}

CheekPuffWeights Map(float left, float right, CheekPuffCalibration profile)
{
    float[] values = new float[70]; values[2] = left; values[3] = right;
    return CheekPuffMapping.CalibratedFromFaceWeights(values, profile);
}

void Check(bool condition, string message)
{
    checks++;
    if (!condition) failures.Add(message);
}

void CheckNear(CheekPuffWeights value, float left, float right, string message)
    => Check(MathF.Abs(value.Left - left) < .02f && MathF.Abs(value.Right - right) < .02f,
        $"{message}: expected ({left}, {right}), got ({value.Left}, {value.Right})");

(float Left, float Right) MixedNative(CheekPuffCalibration fit, float left, float right)
{
    var raw = fit.RawResponse!.Value;
    float shared = MathF.Min(left, right);
    return (fit.LeftNeutral + (left - shared) * (fit.LeftFull - fit.LeftNeutral) +
        (right - shared) * (raw.LeftOnRight - fit.LeftNeutral) + shared * (raw.BothLeft - fit.LeftNeutral),
        fit.RightNeutral + (left - shared) * (raw.RightOnLeft - fit.RightNeutral) +
        (right - shared) * (fit.RightFull - fit.RightNeutral) + shared * (raw.BothRight - fit.RightNeutral));
}
