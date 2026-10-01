using System.Text.Json;
using System.Text.RegularExpressions;

namespace QproFaceTracking.Hub;

// Keep setup status aligned with runtime-python.ps1: verified shared installs
// survive ZIP upgrades, while older release-local environments remain usable.
internal static class HubRocmRuntime
{
    internal static IReadOnlyList<string> Candidates(string releaseRoot, bool legacy, string? localAppData = null)
    {
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var storage = Path.Combine(localAppData, "QproFaceTracking", "r");
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return;
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (seen.Add(fullPath)) candidates.Add(fullPath);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { }
        }

        var index = Path.Combine(storage, "rocm-runtimes.json");
        try
        {
            if (File.Exists(index) && new FileInfo(index).Length <= 16384)
            {
                using var record = JsonDocument.Parse(File.ReadAllText(index));
                if (record.RootElement.TryGetProperty("schema", out var schema) && schema.GetInt32() == 1 &&
                    record.RootElement.TryGetProperty(legacy ? "legacyEnvironment" : "latestEnvironment", out var entry))
                    Add(entry.GetString());
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException) { }

        try
        {
            var namePattern = legacy ? @"^721-gfx\d{4}(-[0-9a-f]{8})?$" : @"^10-gfx\d{4}(-[0-9a-f]{8})?$";
            if (Directory.Exists(storage))
                foreach (var directory in new DirectoryInfo(storage).EnumerateDirectories()
                    .Where(directory => Regex.IsMatch(directory.Name, namePattern, RegexOptions.IgnoreCase))
                    .OrderByDescending(directory => directory.LastWriteTimeUtc).Take(32))
                    Add(directory.FullName);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        Add(Path.Combine(releaseRoot, legacy ? ".venv-rocm" : ".venv-rocm-experimental"));
        return candidates;
    }

    internal static bool IsReady(string environment, bool legacy, string? gfxTarget)
    {
        if (gfxTarget is null || !File.Exists(Path.Combine(environment, "Scripts", "python.exe"))) return false;
        var marker = Path.Combine(environment, "qpro-rocm-ready.json");
        try
        {
            if (!File.Exists(marker) || new FileInfo(marker).Length > 16384) return false;
            using var ready = JsonDocument.Parse(File.ReadAllText(marker));
            return ready.RootElement.TryGetProperty("schema", out var schema) && schema.GetInt32() == 1 &&
                ready.RootElement.TryGetProperty("supportTier", out var tier) &&
                tier.GetString() == (legacy ? "amd-windows-7.2.1" : "experimental-rocm-10") &&
                ready.RootElement.TryGetProperty("rocmVersion", out var version) &&
                version.GetString()?.StartsWith(legacy ? "7.2.1" : "10.0", StringComparison.Ordinal) == true &&
                // Older 7.2.1 markers did not record gfxTarget. The caller
                // limits that fallback to listed legacy cards; its launch
                // probe still validates the actual discrete GPU. New markers
                // and all 10.0 installs must match their recorded target.
                (!ready.RootElement.TryGetProperty("gfxTarget", out var target) || target.ValueKind == JsonValueKind.Null
                    ? legacy
                    : string.Equals(target.GetString(), gfxTarget, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }
}
