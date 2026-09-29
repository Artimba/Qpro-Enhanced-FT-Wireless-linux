namespace Qpro.GazeBridge;

internal readonly record struct CheekSuckWeights(float Left, float Right);

internal enum CheekSuckMode { Off, Balanced, Strong }

internal static class CheekSuckMapping
{
    // XR_FB exposes cheek suck as two independent, nonnegative expressions.
    // Virtual Desktop and Steam Link both put them at indices 6 and 7.
    internal static CheekSuckWeights RawFromFaceWeights(ReadOnlySpan<float> weights)
    {
        Validate(weights);
        return new CheekSuckWeights(Clean(weights[6]), Clean(weights[7]));
    }

    internal static CheekSuckWeights FromFaceWeights(ReadOnlySpan<float> weights, bool strong = false)
    {
        Validate(weights);
        Span<float> cheekPair = stackalloc float[4];
        cheekPair[2] = Clean(weights[6]);
        cheekPair[3] = Clean(weights[7]);
        CheekPuffWeights separated = CheekPuffMapping.FromFaceWeights(cheekPair, strong);
        return new CheekSuckWeights(separated.Left, separated.Right);
    }

    internal static float Clean(float value) => float.IsFinite(value) ? Math.Clamp(value, 0.0f, 1.0f) : 0.0f;

    internal static void Validate(ReadOnlySpan<float> weights)
    {
        if (weights.Length < 8)
            throw new ArgumentException("Face weights must contain both cheek suck expressions", nameof(weights));
    }
}

// Use the same side confirmation and bilateral dwell as individual cheek puff.
// Each tracker owns its own state, so suck and puff cannot change each other's
// active side. Only the dedicated cheek suck channels feed this tracker.
internal sealed class CheekSuckTracker
{
    private readonly CheekPuffTracker _sideTracker = new();

    internal CheekSuckWeights Update(ReadOnlySpan<float> weights, CheekSuckMode mode, long nowMs)
    {
        CheekSuckMapping.Validate(weights);
        Span<float> cheekPair = stackalloc float[4];
        cheekPair[2] = CheekSuckMapping.Clean(weights[6]);
        cheekPair[3] = CheekSuckMapping.Clean(weights[7]);
        CheekPuffMode sideMode = mode switch
        {
            CheekSuckMode.Off => CheekPuffMode.Off,
            CheekSuckMode.Balanced => CheekPuffMode.Balanced,
            CheekSuckMode.Strong => CheekPuffMode.Strong,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown cheek suck mode")
        };
        CheekPuffWeights separated = _sideTracker.Update(cheekPair, sideMode, nowMs);
        return new CheekSuckWeights(separated.Left, separated.Right);
    }

    internal void Reset() => _sideTracker.Reset();
}
