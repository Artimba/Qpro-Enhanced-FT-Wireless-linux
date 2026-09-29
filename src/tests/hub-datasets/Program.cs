using System.Text.Json.Nodes;
using QproFaceTracking.Hub;

var expected = new (string SessionType, TongueDatasetKind Kind)[]
{
    ("tongue-stereo-corrections-v1", TongueDatasetKind.Quick),
    ("tongue-stereo-refinement-v2", TongueDatasetKind.Quick),
    ("tongue-stereo-arc-v3", TongueDatasetKind.Focused),
    ("tongue-stereo-stills-v1", TongueDatasetKind.Full),
};

foreach (var (sessionType, expectedKind) in expected)
{
    foreach (var kind in Enum.GetValues<TongueDatasetKind>())
    {
        var matches = HubTongueDatasetKind.MatchesSessionType(kind, sessionType);
        if (matches != (kind == expectedKind))
            throw new Exception($"{sessionType} was assigned to {kind} instead of only {expectedKind}.");
        if (HubTongueDatasetKind.MatchesSessionType(kind, sessionType.ToUpperInvariant()) != matches)
            throw new Exception($"{sessionType} does not match case-insensitively for {kind}.");
    }
}

if (Enum.GetValues<TongueDatasetKind>().Any(kind => HubTongueDatasetKind.MatchesSessionType(kind, "unrecognized-session")))
    throw new Exception("An unknown session type entered a training queue.");

var originalArc = new JsonArray();
for (var index = 0; index < 10; index++)
    originalArc.Add(new JsonObject { ["name"] = $"Original arc {index}", ["context"] = "relaxed jaw" });
if (!HubTongueDatasetKind.IsLegacyDiagonalOnly(originalArc))
    throw new Exception("The original ten-card diagonal capture was not labeled legacy diagonal-only.");

var expandedArc = new JsonArray();
for (var index = 0; index < 10; index++)
    expandedArc.Add(new JsonObject { ["name"] = $"Original arc {index}", ["context"] = "relaxed jaw" });
expandedArc.Add(new JsonObject { ["name"] = "Mid diagonal upper-left", ["context"] = "relaxed jaw" });
expandedArc.Add(new JsonObject { ["name"] = "Facial hair, neutral, tongue hidden", ["context"] = "facial hair / neutral" });
if (HubTongueDatasetKind.IsLegacyDiagonalOnly(expandedArc))
    throw new Exception("Expanded diagonal and facial-hair capture was labeled legacy.");

Console.WriteLine("Hub dataset routing passed: quick, focused, and full sessions remain separate.");
