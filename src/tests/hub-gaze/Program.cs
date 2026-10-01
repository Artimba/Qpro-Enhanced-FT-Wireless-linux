using System.Text.Json;
using QproFaceTracking.Hub;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static HubGazeInspection Report(string selector = "false", bool recorded = false, string[]? modules = null, string[]? mounts = null)
{
    var line = HubGazeInspection.Prefix + JsonSerializer.Serialize(new
    {
        schema = 1, firmware = "fixture-build", experimentalSelection = selector,
        qproSessionRecorded = recorded, magiskGazeModules = modules ?? [], unverifiedMounts = mounts ?? []
    });
    return HubGazeInspection.ParseLine(line) ?? throw new Exception("A complete report could not be parsed.");
}

var normal = Report();
Check(!normal.NeedsAttention && normal.PopupText().Contains("press Prepare gaze"), "Normal selection lost its setup next step.");
Check(normal.PopupText().Contains("does not verify convergence"), "Normal selection was confused with validated independent gaze.");
var magisk = Report(modules: ["questpro_independent_gaze"], mounts: ["ADB:/odm/etc"]);
Check(magisk.PopupText().Contains("Magisk independent gaze") && magisk.PopupText().Contains("Leave Independent Eye Gaze unchecked"),
    "Magisk with selectorfalse was incorrectly sent to Prepare gaze.");
Check(!magisk.PopupText().Contains("press Prepare gaze"), "The popup encouraged a second active gaze method.");
var recorded = Report("true", true, mounts: ["ADB:/odm/etc/experimental/bolt.ptl"]);
Check(recorded.NeedsAttention && recorded.PopupText().Contains("Qpro gaze session recorded") &&
    recorded.PopupText().Contains("Stop tracking in the Hub that started"), "An owned session was mislabeled as a foreign or legacy method.");
var legacy = Report("true");
Check(legacy.PopupText().Contains("no Qpro recovery record") && legacy.PopupText().Contains("If an older Qpro session"),
    "Unrecorded experimental selection was attributed to Qpro without evidence.");
var foreign = Report(mounts: ["trackingservice:/odm/lib64"]);
Check(foreign.PopupText().Contains("method unverified") && !foreign.PopupText().Contains("press Prepare gaze"),
    "Unknown engine overlay was treated as normal gaze.");
Check(Report("unexpected").PopupText().Contains("Unknown headset gaze selection"), "Unknown selection was treated as normal.");
foreach (var result in new[] { normal, magisk, recorded, legacy, foreign })
{
    Check(result.PopupText().Contains("Recover Qpro gaze:") && result.PopupText().Contains("Reset legacy gaze:") &&
        result.PopupText().Contains("Neither button disables a Magisk module"), "Recovery explanations were missing.");
}
foreach (var invalid in new[] { "not a result", HubGazeInspection.Prefix + "{}", HubGazeInspection.Prefix + "{broken",
    HubGazeInspection.Prefix + "{\"schema\":2}", HubGazeInspection.Prefix + new string('x', 65537) })
    Check(HubGazeInspection.ParseLine(invalid) is null, "An incomplete/invalid report became a successful gaze result.");
Check(HubGazeInspection.FailedPopupText.Contains("Could not verify") && HubGazeInspection.FailedPopupText.Contains("Check Activity"),
    "A failed check could imply that normal gaze was verified.");
Console.WriteLine("PASS: gaze popup chooses safe next actions for Magisk, Qpro records, legacy selection and unknown overlays; incomplete results fail closed.");
