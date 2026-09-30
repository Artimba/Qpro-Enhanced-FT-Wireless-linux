using System.Text.Json;

namespace Qpro.Shared;

internal readonly record struct CheekPuffCalibration(
    float LeftNeutral, float LeftFull, float RightNeutral, float RightFull);

internal static class CheekPuffCalibrationProfile
{
    internal const string VirtualDesktopSource = "virtual-desktop";
    internal const string SteamLinkSource = "steam-link";
    internal const float MinimumRange = 0.02f;

    private static string DefaultConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QproFaceTracking", "config");

    internal static bool IsValid(CheekPuffCalibration calibration)
        => IsValidSide(calibration.LeftNeutral, calibration.LeftFull) &&
           IsValidSide(calibration.RightNeutral, calibration.RightFull);

    internal static CheekPuffCalibration? Load(string source, string? configDirectory = null)
    {
        string path = ProfilePath(source, configDirectory);
        try
        {
            if (!File.Exists(path)) return null;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out JsonElement version) ||
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out int versionNumber) || versionNumber != 1 ||
                !TryReadAnchor(root, "leftNeutral", out float leftNeutral) ||
                !TryReadAnchor(root, "leftFull", out float leftFull) ||
                !TryReadAnchor(root, "rightNeutral", out float rightNeutral) ||
                !TryReadAnchor(root, "rightFull", out float rightFull))
                return null;

            CheekPuffCalibration calibration = new(
                leftNeutral, leftFull, rightNeutral, rightFull);
            return IsValid(calibration) ? calibration : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    internal static void Save(string source, CheekPuffCalibration calibration,
        string? configDirectory = null)
    {
        string path = ProfilePath(source, configDirectory);
        if (!IsValid(calibration))
            throw new ArgumentOutOfRangeException(nameof(calibration),
                "Each cheek needs finite neutral and full anchors in 0..1, at least 0.02 apart.");

        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory,
            $"cheek-puff-calibration-{source}-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new
            {
                version = 1,
                leftNeutral = calibration.LeftNeutral,
                leftFull = calibration.LeftFull,
                rightNeutral = calibration.RightNeutral,
                rightFull = calibration.RightFull,
            }));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    internal static string ProfilePath(string source, string? configDirectory = null)
    {
        if (source is not (VirtualDesktopSource or SteamLinkSource))
            throw new ArgumentException("Unknown face tracking source", nameof(source));
        return Path.Combine(configDirectory ?? DefaultConfigDirectory,
            $"cheek-puff-calibration-{source}.json");
    }

    private static bool IsValidSide(float neutral, float full)
        => float.IsFinite(neutral) && float.IsFinite(full) &&
           neutral >= 0.0f && full <= 1.0f && full - neutral >= MinimumRange;

    private static bool TryReadAnchor(JsonElement root, string name, out float value)
    {
        value = 0.0f;
        return root.TryGetProperty(name, out JsonElement element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetSingle(out value) && float.IsFinite(value);
    }
}
