namespace QproFaceTracking.Hub;

internal enum HubCheekPuffMode { Off, Balanced, Strong, Calibrated }

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
                "calibrated" => HubCheekPuffMode.Calibrated,
                _ => HubCheekPuffMode.Strong,
            };
        }
        catch (IOException) { return HubCheekPuffMode.Strong; }
        catch (UnauthorizedAccessException) { return HubCheekPuffMode.Strong; }
    }

    internal static HubCheekPuffMode LoadLastStyle()
    {
        try
        {
            if (File.Exists(StylePath))
                return ParseStyle(File.ReadAllText(StylePath));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        HubCheekPuffMode current = LoadMode();
        return current == HubCheekPuffMode.Off ? HubCheekPuffMode.Strong : current;
    }

    internal static void Save(bool enabled, HubCheekPuffMode style)
    {
        string styleText = style switch
        {
            HubCheekPuffMode.Calibrated => "calibrated",
            HubCheekPuffMode.Balanced => "balanced",
            _ => "strong",
        };
        Directory.CreateDirectory(ConfigDirectory);
        WriteAtomically(StylePath, styleText);
        WriteAtomically(ModePath, enabled ? styleText : "off");
    }

    private static HubCheekPuffMode ParseStyle(string value) => value.Trim().ToLowerInvariant() switch
    {
        "calibrated" => HubCheekPuffMode.Calibrated,
        "balanced" => HubCheekPuffMode.Balanced,
        _ => HubCheekPuffMode.Strong,
    };

    private static void WriteAtomically(string path, string value)
    {
        string temporaryPath = Path.Combine(ConfigDirectory, $"cheek-puff-mode-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, value);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
