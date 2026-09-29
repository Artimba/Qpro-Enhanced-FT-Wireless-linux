namespace Qpro.GazeBridge;

internal readonly record struct NativeLipWeights(
    float UpperUpLeft, float UpperDeepenLeft,
    float UpperUpRight, float UpperDeepenRight,
    float SuckUpperLeft, float SuckUpperRight);

internal static class NativeLipMapping
{
    internal static NativeLipWeights FromFaceWeights(ReadOnlySpan<float> weights, NativeFaceSource source)
    {
        if (weights.Length < 70)
            throw new ArgumentException("Face weights must contain 70 expressions", nameof(weights));
        if (source is not (NativeFaceSource.VirtualDesktop or NativeFaceSource.SteamLink))
            throw new ArgumentOutOfRangeException(nameof(source));

        float upperUpLeft = MathF.Max(0.0f, weights[61] - weights[55]);
        float upperUpRight = MathF.Max(0.0f, weights[62] - weights[56]);
        // Steam Link's native module subtracts nose sneer a second time when
        // deriving MouthUpperDeepen; Virtual Desktop uses UpperUp for both.
        float upperDeepenLeft = source == NativeFaceSource.SteamLink
            ? MathF.Max(0.0f, upperUpLeft - weights[55]) : upperUpLeft;
        float upperDeepenRight = source == NativeFaceSource.SteamLink
            ? MathF.Max(0.0f, upperUpRight - weights[56]) : upperUpRight;
        // The native modules suppress upper lip suck using different inputs.
        float suckSuppressorLeft = source == NativeFaceSource.SteamLink ? weights[53] : weights[61];
        float suckSuppressorRight = source == NativeFaceSource.SteamLink ? weights[54] : weights[62];

        return new NativeLipWeights(
            upperUpLeft, upperDeepenLeft, upperUpRight, upperDeepenRight,
            MathF.Min(1.0f - MathF.Pow(suckSuppressorLeft, 1.0f / 6.0f), weights[45]),
            MathF.Min(1.0f - MathF.Pow(suckSuppressorRight, 1.0f / 6.0f), weights[47]));
    }
}
