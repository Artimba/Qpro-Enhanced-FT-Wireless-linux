using System.Text.Json.Nodes;

namespace QproFaceTracking.Hub;

internal enum TongueDatasetKind
{
    Quick,
    Focused,
    Full,
}

internal static class HubTongueDatasetKind
{
    internal static bool MatchesSessionType(TongueDatasetKind kind, string sessionType) => kind switch
    {
        TongueDatasetKind.Quick =>
            sessionType.Equals("lower-face-refinement-v1", StringComparison.OrdinalIgnoreCase) ||
            sessionType.Equals("tongue-stereo-corrections-v1", StringComparison.OrdinalIgnoreCase) ||
            sessionType.Equals("tongue-stereo-refinement-v2", StringComparison.OrdinalIgnoreCase),
        TongueDatasetKind.Focused => sessionType.Equals("tongue-stereo-arc-v3", StringComparison.OrdinalIgnoreCase),
        TongueDatasetKind.Full => sessionType.Equals("tongue-stereo-stills-v1", StringComparison.OrdinalIgnoreCase) ||
            sessionType.Equals("lower-face-stills-v1", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    internal static bool IsLegacyDiagonalOnly(JsonArray? prompts)
    {
        if (prompts is null || prompts.Count != 10) return false;
        return !prompts.OfType<JsonObject>().Any(card =>
            (card["name"]?.GetValue<string>()?.StartsWith("Mid diagonal ", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (card["context"]?.GetValue<string>()?.StartsWith("facial hair / ", StringComparison.OrdinalIgnoreCase) ?? false));
    }
}
