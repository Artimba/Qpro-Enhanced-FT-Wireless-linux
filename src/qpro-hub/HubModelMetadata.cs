using System.Text.Json.Nodes;

namespace QproFaceTracking.Hub;

// Export only portable model details. Private recording paths and prepared
// image caches belong to the local dataset, not to a transferable model.
internal static class HubModelMetadata
{
    internal static JsonObject Create(int version, string displayName, string? datasetName,
        string origin, JsonObject? existing, JsonObject? classification = null)
    {
        var payload = new JsonObject
        {
            ["format"] = "qpro-tongue-model-metadata-v1", ["displayName"] = displayName,
            ["version"] = version, ["origin"] = origin,
            ["datasetSession"] = datasetName is null ? existing?["datasetSession"]?.DeepClone() : datasetName,
            ["createdUtc"] = existing?["createdUtc"]?.DeepClone() ?? JsonValue.Create(DateTimeOffset.UtcNow.ToString("O"))
        };
        CopyPortableDetails(classification ?? existing, payload);
        return payload;
    }

    internal static bool IsMustachio(JsonObject? metadata) =>
        Text(metadata, "modelKind") == "mustachio-experimental" ||
        Text(metadata, "parentModelKind") == "mustachio-experimental";

    internal static bool HasCameraCheeks(JsonObject? metadata) =>
        metadata?["hasCameraCheeks"] is JsonValue value &&
        value.TryGetValue<bool>(out bool enabled) && enabled;

    internal static bool IsExperimental(JsonObject? metadata) =>
        IsMustachio(metadata) || HasCameraCheeks(metadata) ||
        Text(metadata, "modelKind") == "camera-cheeks-experimental" ||
        (metadata?["isExperimental"] is JsonValue value &&
         value.TryGetValue<bool>(out bool experimental) && experimental);

    internal static void CopyPortableDetails(JsonObject? source, JsonObject destination)
    {
        if (source is null) return;
        foreach (string key in new[] { "modelKind", "parentModelKind", "parentDisplayName", "trackingSource",
                     "tongueParentSha256", "trainingOrigin", "trainedUtc" })
            if (Text(source, key) is string text && !string.IsNullOrWhiteSpace(text))
                destination[key] = text;
        if (IsExperimental(source)) destination["isExperimental"] = true;
        if (HasCameraCheeks(source)) destination["hasCameraCheeks"] = true;
        foreach (string key in new[] { "parentVersion", "sourceVersion" })
            if (source[key] is JsonValue number && number.TryGetValue<int>(out int version) && version > 0)
                destination[key] = version;
        if (Text(source, "tongueParent") is string parent)
            destination["tongueParent"] = parent.Replace('\\', '/').Split('/')[^1];
        if (destination["trainingOrigin"] is null && Text(source, "origin") is string origin)
            destination["trainingOrigin"] = origin;
        if (destination["trainedUtc"] is null && Text(source, "createdUtc") is string created)
            destination["trainedUtc"] = created;
        if (HasCameraCheeks(source) && source["cheekTraining"] is JsonObject training)
        {
            var portableTraining = new JsonObject();
            foreach (string key in new[] { "targetSource", "frozenTongueParent", "trainSamples", "validationSamples",
                         "checkpointEpoch", "validation", "featureNormalization", "loss", "validationGrade",
                         "independentWearerValidation", "developerPromotionApproved", "trainingWearers",
                         "independentCaptureValidation", "liveValidationComplete", "freshLivePoseCheck" })
                if (training[key] is JsonNode detail) portableTraining[key] = detail.DeepClone();
            destination["cheekTraining"] = portableTraining;
        }
    }

    private static string? Text(JsonObject? metadata, string key) =>
        metadata?[key] is JsonValue value && value.TryGetValue<string>(out string? text) ? text : null;
}
