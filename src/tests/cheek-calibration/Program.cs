using System.Text.Json;
using Qpro.GazeBridge;
using Qpro.Shared;

// Exercise the restored three-pose profile and processed-value feed. Raw
// anchors from the withdrawn four-pose method must never become v1 anchors.
string directory = Path.Combine(Path.GetTempPath(), $"qpro-cheek-profile-tests-{Guid.NewGuid():N}");
int checks = 0;
int exitCode = 0;
try
{
    Directory.CreateDirectory(directory);
    const string vd = CheekPuffCalibrationProfile.VirtualDesktopSource;
    const string steam = CheekPuffCalibrationProfile.SteamLinkSource;
    CheekPuffCalibration vdProfile = new(.1f, .5f, .05f, .55f);
    CheekPuffCalibration steamProfile = new(0, .7f, 0, .9f);
    CheekPuffCalibrationProfile.Save(vd, vdProfile, directory);
    CheekPuffCalibrationProfile.Save(steam, steamProfile, directory);
    Check(CheekPuffCalibrationProfile.Load(vd, directory) == vdProfile,
        "Virtual Desktop version 1 profile round trips");
    Check(CheekPuffCalibrationProfile.Load(steam, directory) == steamProfile,
        "Steam Link retains its separate version 1 profile");
    using (JsonDocument saved = JsonDocument.Parse(File.ReadAllText(CheekPuffCalibrationProfile.ProfilePath(vd, directory))))
        Check(saved.RootElement.GetProperty("version").GetInt32() == 1,
            "new three-pose profiles use version 1");

    string vdPath = CheekPuffCalibrationProfile.ProfilePath(vd, directory);
    foreach (int version in new[] { 2, 3 })
    {
        // These anchors deliberately also pass v1 range validation: only the
        // version check can stop a raw-domain profile being misinterpreted.
        byte[] unsupported = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version, leftNeutral = .1f, leftFull = .5f, rightNeutral = .05f, rightFull = .55f,
            leftDeadZone = .01f, rightDeadZone = .01f,
            rawResponse = new { leftOnRight = .2f, rightOnLeft = .2f, bothLeft = .8f, bothRight = .8f }
        });
        File.WriteAllBytes(vdPath, unsupported);
        Check(CheekPuffCalibrationProfile.Load(vd, directory) is null,
            $"unsupported version {version} is ignored");
        Check(File.ReadAllBytes(vdPath).SequenceEqual(unsupported),
            $"loading leaves version {version} file unchanged");
        Check(CheekPuffCalibrationProfile.Load(steam, directory) == steamProfile,
            $"unsupported version {version} cannot alter the other source");
        CheekPuffCalibration? active = CheekPuffCalibrationProfile.Load(vd, directory)
            ?? DeveloperCheekPuffBaseline.ForSource(vd);
        Check(active == DeveloperCheekPuffBaseline.ForSource(vd)
            && active.HasValue && CheekPuffCalibrationProfile.IsValid(active.Value),
            $"unsupported version {version} permits the existing developer fallback");
    }

    foreach (byte source in new[] { CheekPuffTelemetry.SourceVirtualDesktop, CheekPuffTelemetry.SourceSteamLink })
    {
        byte[] packet = CheekPuffTelemetry.Create(source, .25f, .75f);
        Check(packet[3] == (byte)'1', "calibration publisher uses the processed QCP1 format");
        Check(CheekPuffTelemetry.TryParse(packet, out byte parsedSource, out float left, out float right)
            && parsedSource == source && Near(left, .25f) && Near(right, .75f),
            "processed feed preserves source and both strengths");
        packet[3] = (byte)'2';
        Check(!CheekPuffTelemetry.TryParse(packet, out _, out _, out _),
            "raw QCP2 packets cannot enter three-pose calibration");
    }

    float[] weights = new float[70];
    weights[2] = weights[3] = .3f;
    CheekPuffWeights mapped = CheekPuffMapping.CalibratedFromFaceWeights(weights, vdProfile);
    Check(Near(mapped.Left, .5f) && Near(mapped.Right, .5f),
        "version 1 anchors retain intermediate strengths");
    Check(CheekPuffMapping.CalibratedFromFaceWeights(weights, null) == CheekPuffMapping.FromFaceWeights(weights),
        "missing profile uses Balanced values");
    weights[2] = weights[3] = 0;
    mapped = CheekPuffMapping.CalibratedFromFaceWeights(weights, vdProfile);
    Check(Near(mapped.Left, 0) && Near(mapped.Right, 0), "values below relaxed anchors release both cheeks");
    foreach (string source in new[] { vd, steam })
        Check(DeveloperCheekPuffBaseline.ForSource(source) is { } baseline
            && CheekPuffCalibrationProfile.IsValid(baseline), "bundled source baseline remains usable");
    Console.WriteLine($"PASS: {checks} three-pose cheek rollback checks.");
}
catch (Exception error)
{
    Console.Error.WriteLine($"FAIL after {checks} checks: {error.Message}");
    exitCode = 1;
}
finally
{
    try
    {
        // Confirm the target remains a child of the test's temporary root.
        string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        if (!Path.GetFullPath(directory).StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The test cleanup path is outside its temporary root.");
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"Test cleanup failed: {error.Message}");
        exitCode = 1;
    }
}
return exitCode;

void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    checks++;
}
bool Near(float actual, float expected) => MathF.Abs(actual - expected) <= .00001f;
