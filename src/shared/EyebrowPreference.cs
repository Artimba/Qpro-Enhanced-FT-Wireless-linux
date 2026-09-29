using System.Text.Json;

namespace Qpro.Shared;

internal readonly record struct EyebrowSettings(bool Enabled, float Sensitivity);

internal static class EyebrowPreference
{
    internal const float MinimumSensitivity = 0.5f;
    internal const float MaximumSensitivity = 3.0f;
    internal static readonly EyebrowSettings Default = new(false, 1.0f);

    private static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QproFaceTracking", "config");
    private static string SettingsPath => Path.Combine(ConfigDirectory, "eyebrow-settings.json");

    internal static EyebrowSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return Default;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("enabled", out JsonElement enabled) ||
                enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !root.TryGetProperty("sensitivity", out JsonElement sensitivity) ||
                sensitivity.ValueKind != JsonValueKind.Number ||
                !sensitivity.TryGetSingle(out float gain) ||
                !float.IsFinite(gain) ||
                gain < MinimumSensitivity || gain > MaximumSensitivity)
                return Default;
            return new EyebrowSettings(enabled.GetBoolean(), gain);
        }
        catch (IOException) { return Default; }
        catch (UnauthorizedAccessException) { return Default; }
        catch (JsonException) { return Default; }
    }

    internal static void Save(EyebrowSettings settings)
    {
        if (!float.IsFinite(settings.Sensitivity) ||
            settings.Sensitivity < MinimumSensitivity ||
            settings.Sensitivity > MaximumSensitivity)
            throw new ArgumentOutOfRangeException(nameof(settings));

        Directory.CreateDirectory(ConfigDirectory);
        string temporaryPath = Path.Combine(ConfigDirectory, $"eyebrow-settings-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new
            {
                enabled = settings.Enabled,
                sensitivity = settings.Sensitivity,
            }));
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
