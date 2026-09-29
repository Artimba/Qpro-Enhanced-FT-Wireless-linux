namespace QproFaceTracking.Hub;

internal enum HubCheekSuckMode { Off, Balanced, Strong }

internal static class HubCheekSuckPreference
{
    private static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QproFaceTracking", "config");
    private static string ModePath => Path.Combine(ConfigDirectory, "cheek-suck-mode.txt");
    private static string StylePath => Path.Combine(ConfigDirectory, "cheek-suck-last-style.txt");

    internal static HubCheekSuckMode LoadMode()
    {
        try
        {
            if (!File.Exists(ModePath)) return HubCheekSuckMode.Strong;
            return File.ReadAllText(ModePath).Trim().ToLowerInvariant() switch
            {
                "off" => HubCheekSuckMode.Off,
                "balanced" => HubCheekSuckMode.Balanced,
                _ => HubCheekSuckMode.Strong,
            };
        }
        catch (IOException) { return HubCheekSuckMode.Strong; }
        catch (UnauthorizedAccessException) { return HubCheekSuckMode.Strong; }
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
        return LoadMode() != HubCheekSuckMode.Balanced;
    }

    internal static void Save(bool enabled, bool strong)
    {
        Directory.CreateDirectory(ConfigDirectory);
        File.WriteAllText(StylePath, strong ? "strong" : "balanced");
        string temporaryPath = Path.Combine(ConfigDirectory, $"cheek-suck-mode-{Guid.NewGuid():N}.tmp");
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
