namespace Qpro.GazeBridge;

internal enum NativeFaceSource { VirtualDesktop, SteamLink }

internal readonly record struct NativeTongueWeights(
    float Out, float? Up = null, float? Down = null, float? Left = null,
    float? Right = null, float? BendDown = null, float? CurlUp = null);

internal static class NativeTongueMapping
{
    internal static NativeTongueWeights? Resolve(ReadOnlySpan<float> weights,
        NativeFaceSource source, byte faceFlags, bool customFresh, bool customEnabled)
    {
        // The camera model owns the tongue only while its enabled packet is fresh.
        if (customFresh && customEnabled)
            return null;
        if (weights.Length < 70)
            throw new ArgumentException("Face weights must contain 70 expressions", nameof(weights));

        return source switch
        {
            // Steam Link sends XR_FB_face_tracking2 weights: 64 is tongue tip
            // touching the alveolar ridge and 69 is tongue retreat. Neither
            // means VRCFT's CurlUp or BendDown. Only TongueOut maps directly;
            // the Qpro camera model supplies detailed tongue poses when active.
            NativeFaceSource.SteamLink => new NativeTongueWeights(Out: weights[68]),
            NativeFaceSource.VirtualDesktop when (faceFlags & 2) != 0 =>
                new NativeTongueWeights(Out: weights[63], Left: weights[64],
                    Right: weights[65], Up: weights[66], Down: weights[67]),
            NativeFaceSource.VirtualDesktop =>
                new NativeTongueWeights(Out: weights[68], CurlUp: weights[64]),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
    }
}
