using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace QproFaceTracking.Hub;

internal static class HubModelPackage
{
    internal static int NextVersion(string models)
    {
        int maximum = 0;
        if (Directory.Exists(models))
            foreach (string path in Directory.EnumerateFiles(models, "qpro-stereo-tongue-v*"))
            {
                Match match = Regex.Match(Path.GetFileName(path), @"^qpro-stereo-tongue-v(?<v>\d+)(?:[.-]|$)");
                if (match.Success && int.TryParse(match.Groups["v"].Value, out int version))
                    maximum = Math.Max(maximum, version);
            }
        return checked(maximum + 1);
    }

    internal static JsonObject Import(string package, string models, int version,
        Func<JsonObject, JsonObject> metadata)
    {
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        Directory.CreateDirectory(models);
        string stem = $"qpro-stereo-tongue-v{version}";
        if (Directory.EnumerateFiles(models, stem + "*").Any(path =>
                Path.GetFileName(path).StartsWith(stem + "-", StringComparison.Ordinal) ||
                Path.GetFileName(path).StartsWith(stem + ".", StringComparison.Ordinal)))
            throw new IOException("This model version already has files. Existing models were kept; retry the import.");
        string staging = Path.Combine(models, ".qpro-import-" + Guid.NewGuid().ToString("N"));
        var created = new List<string>();
        Directory.CreateDirectory(staging);
        try
        {
            using var archive = ZipFile.OpenRead(package);
            ZipArchiveEntry? gate = archive.GetEntry("gate.pt"), direction = archive.GetEntry("direction.pt");
            if (gate is null || direction is null || gate.Length <= 0 || direction.Length <= 0 ||
                gate.Length > 268_435_456 || direction.Length > 268_435_456)
                throw new InvalidDataException("This package does not contain a valid, reasonably sized paired model.");
            ZipArchiveEntry? entry = archive.GetEntry("manifest.json");
            if (entry is null || entry.Length > 262_144)
                throw new InvalidDataException("The model package manifest is missing or too large.");
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            JsonObject manifest = JsonNode.Parse(reader.ReadToEnd())?.AsObject()
                ?? throw new InvalidDataException("The model package manifest is invalid.");
            if (manifest["format"]?.GetValue<string>() != "qpro-tongue-model-package-v1")
                throw new InvalidDataException("This model package format is not supported.");
            gate.ExtractToFile(Path.Combine(staging, "gate.pt"), false);
            direction.ExtractToFile(Path.Combine(staging, "direction.pt"), false);
            File.WriteAllText(Path.Combine(staging, "metadata.json"),
                metadata(manifest).ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            foreach ((string source, string ending) in new[] { ("gate.pt", "-gate.pt"),
                         ("direction.pt", "-direction.pt"), ("metadata.json", ".metadata.json") })
            {
                string destination = Path.Combine(models, stem + ending);
                File.Move(Path.Combine(staging, source), destination, false);
                created.Add(destination);
            }
            return manifest;
        }
        catch
        {
            // Only files created by this attempt belong to its rollback. A
            // pre-existing or concurrently created model must never be deleted.
            foreach (string path in created) File.Delete(path);
            throw;
        }
        finally
        {
            string fullStaging = Path.GetFullPath(staging);
            string allowedModels = Path.GetFullPath(models).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullStaging.StartsWith(allowedModels, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe model import staging path.");
            Directory.Delete(fullStaging, true);
        }
    }
}
