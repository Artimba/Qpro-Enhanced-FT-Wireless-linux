using System.Text.Json;
using QproFaceTracking.Hub;

// Isolated paths exercise upgrades and failed installs without starting Python
// or reading the user's runtime, graphics driver, or Windows registry.
static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var root = Path.Combine(Path.GetTempPath(), "qpro-rocm-routing-" + Guid.NewGuid().ToString("N"));
var release = Path.Combine(root, "extracted release");
var local = Path.Combine(root, "local");
var storage = Path.Combine(local, "QproFaceTracking", "r");
var latest = Path.Combine(storage, "10-gfx1100");
var legacy = Path.Combine(release, ".venv-rocm");
var custom = Path.Combine(root, "custom");
var index = Path.Combine(storage, "rocm-runtimes.json");
void WriteReady(string environment, bool old, string target = "gfx1100")
{
    Directory.CreateDirectory(Path.Combine(environment, "Scripts"));
    File.WriteAllText(Path.Combine(environment, "Scripts", "python.exe"), "fixture");
    File.WriteAllText(Path.Combine(environment, "qpro-rocm-ready.json"), JsonSerializer.Serialize(new
    {
        schema = 1, gfxTarget = target,
        supportTier = old ? "amd-windows-7.2.1" : "experimental-rocm-10",
        rocmVersion = old ? "7.2.1" : "10.0.0"
    }));
}
try
{
    WriteReady(latest, false);
    WriteReady(legacy, true);
    WriteReady(custom, false);
    Directory.CreateDirectory(Path.Combine(storage, "unrelated"));
    File.WriteAllText(index, JsonSerializer.Serialize(new { schema = 1, latestEnvironment = custom }));
    var candidates = HubRocmRuntime.Candidates(release, false, local);
    Check(candidates[0] == custom && candidates.Contains(latest), "A verified custom short install was not preferred.");
    Check(candidates[^1] == Path.Combine(release, ".venv-rocm-experimental"), "An older extracted environment was lost.");
    Check(!candidates.Any(path => path.EndsWith("unrelated")), "Unrelated folders entered runtime discovery.");
    Check(HubRocmRuntime.IsReady(custom, false, "gfx1100"), "Verified latest runtime was not ready.");
    Check(!HubRocmRuntime.IsReady(custom, false, "gfx1031"), "A runtime for another GPU was accepted.");
    Check(!HubRocmRuntime.IsReady(custom, true, "gfx1100"), "Latest runtime was mislabeled as a legacy fallback.");
    Check(HubRocmRuntime.Candidates(release, true, local).Contains(legacy) &&
        HubRocmRuntime.IsReady(legacy, true, "gfx1100"), "Old release-local compatibility fallback was lost.");
    Check(!HubRocmRuntime.IsReady(legacy, true, "gfx1201"), "Legacy readiness ignored the detected GPU target.");
    File.WriteAllText(Path.Combine(legacy, "qpro-rocm-ready.json"), JsonSerializer.Serialize(new
    {
        schema = 1, supportTier = "amd-windows-7.2.1", rocmVersion = "7.2.1", gfxTarget = (string?)null
    }));
    Check(HubRocmRuntime.IsReady(legacy, true, "gfx1100"), "Old verified legacy marker without a GPU target was lost.");
    File.WriteAllText(Path.Combine(custom, "qpro-rocm-ready.json"), JsonSerializer.Serialize(new
    {
        schema = 1, supportTier = "experimental-rocm-10", rocmVersion = "10.0.0", gfxTarget = (string?)null
    }));
    Check(!HubRocmRuntime.IsReady(custom, false, "gfx1100"), "Latest runtime accepted a missing GPU target.");

    File.WriteAllText(index, "incomplete index");
    Check(HubRocmRuntime.Candidates(release, false, local).Contains(latest), "Damaged index hid a usable compact runtime.");
    File.WriteAllText(index, JsonSerializer.Serialize(new { schema = 1, latestEnvironment = "relative" }));
    Check(!HubRocmRuntime.Candidates(release, false, local).Contains("relative"), "Relative index path was accepted.");
    File.Delete(Path.Combine(latest, "qpro-rocm-ready.json"));
    Check(!HubRocmRuntime.IsReady(latest, false, "gfx1100"), "Partial install was marked ready.");
    File.WriteAllText(Path.Combine(latest, "qpro-rocm-ready.json"), "incomplete marker");
    Check(!HubRocmRuntime.IsReady(latest, false, "gfx1100"), "Invalid marker was marked ready.");
    WriteReady(latest, false);
    File.Delete(Path.Combine(latest, "Scripts", "python.exe"));
    Check(!HubRocmRuntime.IsReady(latest, false, "gfx1100"), "Missing interpreter was marked ready.");
    Console.WriteLine("PASS: Hub ROCm routing preserves short installs and legacy fallback; rejects wrong GPU, incomplete installs and damaged metadata.");
}
finally
{
    // Only this test's freshly created absolute directory is removed.
    Directory.Delete(root, recursive: true);
}
