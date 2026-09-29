namespace Qpro.GazeBridge;

internal readonly record struct EyebrowWeights(
    float LowerLeft, float LowerRight,
    float InnerLeft, float InnerRight,
    float OuterLeft, float OuterRight);

internal static class EyebrowMapping
{
    internal static EyebrowWeights FromFaceWeights(ReadOnlySpan<float> values, bool enabled, float sensitivity)
    {
        if (values.Length < 59)
            throw new ArgumentException("Face weights must contain both brow sides", nameof(values));
        return new EyebrowWeights(
            Apply(values[0], enabled, sensitivity), Apply(values[1], enabled, sensitivity),
            Apply(values[22], enabled, sensitivity), Apply(values[23], enabled, sensitivity),
            Apply(values[57], enabled, sensitivity), Apply(values[58], enabled, sensitivity));
    }

    internal static float Apply(float nativeWeight, bool enabled, float sensitivity)
    {
        if (!enabled) return nativeWeight;
        if (!float.IsFinite(nativeWeight)) return 0.0f;
        if (!float.IsFinite(sensitivity)) sensitivity = 1.0f;
        return Math.Clamp(nativeWeight * sensitivity, 0.0f, 1.0f);
    }
}
