namespace QproFaceTracking.Hub;

internal enum HubCheekPuffMode { Off, Balanced, Strong }

internal static class HubCheekPuffPreference
{
    private static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QproFaceTracking", "config");
    private static string ModePath => Path.Combine(ConfigDirectory, "cheek-puff-mode.txt");
    private static string StylePath => Path.Combine(ConfigDirectory, "cheek-puff-last-style.txt");

    internal static HubCheekPuffMode LoadMode()
    {
        try
        {
            if (!File.Exists(ModePath)) return HubCheekPuffMode.Strong;
            return File.ReadAllText(ModePath).Trim().ToLowerInvariant() switch
            {
                "off" => HubCheekPuffMode.Off,
                "balanced" => HubCheekPuffMode.Balanced,
                _ => HubCheekPuffMode.Strong,
            };
        }
        catch (IOException) { return HubCheekPuffMode.Strong; }
        catch (UnauthorizedAccessException) { return HubCheekPuffMode.Strong; }
    }

    internal static bool LoadLastStrongStyle()
    {
        try
        {
            if (File.Exists(StylePath))
                return !File.ReadAllText(StylePath).Trim().Equals("balanced", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return LoadMode() != HubCheekPuffMode.Balanced;
    }

    internal static void Save(bool enabled, bool strong)
    {
        Directory.CreateDirectory(ConfigDirectory);
        File.WriteAllText(StylePath, strong ? "strong" : "balanced");
        string temporaryPath = Path.Combine(ConfigDirectory, $"cheek-puff-mode-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, enabled ? (strong ? "strong" : "balanced") : "off");
            File.Move(temporaryPath, ModePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
