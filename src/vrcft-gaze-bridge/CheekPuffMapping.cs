namespace Qpro.GazeBridge;

internal readonly record struct CheekPuffWeights(float Left, float Right);

internal enum CheekPuffMode { Off, Balanced, Strong }

internal static class CheekPuffMapping
{
    internal static CheekPuffWeights RawFromFaceWeights(ReadOnlySpan<float> weights)
    {
        if (weights.Length < 4)
            throw new ArgumentException("Face weights must contain both cheek puff expressions", nameof(weights));

        return new CheekPuffWeights(
            float.IsFinite(weights[2]) ? Math.Clamp(weights[2], 0.0f, 1.0f) : 0.0f,
            float.IsFinite(weights[3]) ? Math.Clamp(weights[3], 0.0f, 1.0f) : 0.0f);
    }

    internal static CheekPuffWeights FromFaceWeights(ReadOnlySpan<float> weights, bool strong = false)
    {
        if (weights.Length < 4)
            throw new ArgumentException("Face weights must contain both cheek puff expressions", nameof(weights));

        float left = Math.Clamp(weights[2], 0.0f, 1.0f);
        float right = Math.Clamp(weights[3], 0.0f, 1.0f);
        float peak = MathF.Max(left, right);
        if (peak < 0.02f)
            return new CheekPuffWeights(0.0f, 0.0f);

        // Quest Pro can raise both raw cheek weights during a one-sided puff.
        // A genuine two-sided puff is nearly symmetric, while a one-sided puff
        // leaves a small but repeatable lead on the active side. Preserve the
        // shared weight only when the two source weights agree closely.
        float asymmetry = MathF.Abs(left - right) / peak;
        if (strong && peak >= 0.08f)
        {
            // Require a meaningful side lead so a slightly uneven two-cheek
            // puff does not snap to one side. In a clear one-sided pose the
            // selected cheek gets the full value requested by the user.
            if (asymmetry >= 0.24f && MathF.Abs(left - right) >= 0.025f)
                return left > right
                    ? new CheekPuffWeights(1.0f, 0.0f)
                    : new CheekPuffWeights(0.0f, 1.0f);
            if (asymmetry <= 0.12f && MathF.Min(left, right) >= 0.08f)
                return new CheekPuffWeights(1.0f, 1.0f);
        }
        float unilateralBlend = SmoothStep(0.12f, 0.28f, asymmetry);
        float bilateral = MathF.Min(left, right) * (1.0f - unilateralBlend);
        float leftOnly = MathF.Max(0.0f, (left - right - 0.015f) * 4.0f);
        float rightOnly = MathF.Max(0.0f, (right - left - 0.015f) * 4.0f);
        CheekPuffWeights balanced = new(
            Math.Clamp(MathF.Max(bilateral, leftOnly), 0.0f, 1.0f),
            Math.Clamp(MathF.Max(bilateral, rightOnly), 0.0f, 1.0f));
        if (!strong) return balanced;
        // Keep uncertain transitions continuous instead of flipping between
        // left, right, and both when the source weights are nearly tied.
        return new CheekPuffWeights(
            Math.Clamp((balanced.Left - 0.03f) * 7.0f, 0.0f, 1.0f),
            Math.Clamp((balanced.Right - 0.03f) * 7.0f, 0.0f, 1.0f));
    }

    private static float SmoothStep(float low, float high, float value)
    {
        float t = Math.Clamp((value - low) / (high - low), 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }
}

// Strong mode needs the previous pose: raw left/right weights often become
// nearly equal for a moment while a one-sided puff changes sides.
internal sealed class CheekPuffTracker
{
    private const float NeutralPeak = 0.02f;
    private const float FullStrengthPeak = 0.08f;
    private const long SideConfirmationMs = 60;
    private const long BothFromNeutralMs = 40;
    private const long BothFromSideMs = 220;

    private enum Pose { Neutral, Left, Right, Both }

    private Pose _pose;
    private Pose _candidate;
    private long _candidateSinceMs;

    internal CheekPuffWeights Update(ReadOnlySpan<float> weights, bool strong, long nowMs)
        => Update(weights, strong ? CheekPuffMode.Strong : CheekPuffMode.Balanced, nowMs);

    internal CheekPuffWeights Update(ReadOnlySpan<float> weights, CheekPuffMode mode, long nowMs)
    {
        if (mode == CheekPuffMode.Off)
        {
            Reset();
            return CheekPuffMapping.RawFromFaceWeights(weights);
        }
        if (mode == CheekPuffMode.Balanced)
        {
            Reset();
            return CheekPuffMapping.FromFaceWeights(weights);
        }
        if (weights.Length < 4)
            throw new ArgumentException("Face weights must contain both cheek puff expressions", nameof(weights));

        float left = float.IsFinite(weights[2]) ? Math.Clamp(weights[2], 0.0f, 1.0f) : 0.0f;
        float right = float.IsFinite(weights[3]) ? Math.Clamp(weights[3], 0.0f, 1.0f) : 0.0f;
        float peak = MathF.Max(left, right);
        if (peak < NeutralPeak)
        {
            Reset();
            return new CheekPuffWeights(0.0f, 0.0f);
        }

        Pose observed = Observe(left, right, peak);
        if (_pose == Pose.Neutral)
        {
            if (observed is Pose.Left or Pose.Right)
            {
                float difference = MathF.Abs(left - right);
                bool clearSide = difference / peak >= 0.24f && difference >= 0.025f;
                if (clearSide || Confirm(observed, nowMs, BothFromNeutralMs))
                    Select(observed);
            }
            else if (observed == Pose.Both && Confirm(Pose.Both, nowMs, BothFromNeutralMs))
                Select(Pose.Both);
            else if (observed != Pose.Both)
                ClearCandidate();
        }
        else if (_pose == Pose.Both)
        {
            if (observed is Pose.Left or Pose.Right)
            {
                if (Confirm(observed, nowMs, SideConfirmationMs))
                    Select(observed);
            }
            else
                ClearCandidate();
        }
        else
        {
            Pose opposite = _pose == Pose.Left ? Pose.Right : Pose.Left;
            if (observed == opposite)
            {
                if (Confirm(opposite, nowMs, SideConfirmationMs))
                    Select(opposite);
            }
            else if (observed == Pose.Both)
            {
                if (Confirm(Pose.Both, nowMs, BothFromSideMs))
                    Select(Pose.Both);
            }
            else
                ClearCandidate();
        }

        // Let the active cheek release smoothly at very low source weights,
        // while the other cheek remains exactly zero throughout a side switch.
        float strength = Math.Clamp((peak - NeutralPeak) / (FullStrengthPeak - NeutralPeak), 0.0f, 1.0f);
        return _pose switch
        {
            Pose.Left => new CheekPuffWeights(strength, 0.0f),
            Pose.Right => new CheekPuffWeights(0.0f, strength),
            Pose.Both => new CheekPuffWeights(strength, strength),
            _ => new CheekPuffWeights(0.0f, 0.0f)
        };
    }

    internal void Reset()
    {
        _pose = Pose.Neutral;
        ClearCandidate();
    }

    private Pose Observe(float left, float right, float peak)
    {
        if (peak < FullStrengthPeak) return Pose.Neutral;
        float difference = MathF.Abs(left - right);
        float asymmetry = difference / peak;
        // Once one side is selected, a steady lead by the other side is
        // enough to switch. The Quest Pro's two raw weights can both stay high
        // during a real side change, so the initial 0.24 threshold is too
        // strict here. Confirmation time filters out brief bilateral noise.
        if (_pose == Pose.Left && right - left >= 0.06f && asymmetry >= 0.09f)
            return Pose.Right;
        if (_pose == Pose.Right && left - right >= 0.06f && asymmetry >= 0.09f)
            return Pose.Left;
        if (asymmetry >= 0.24f && difference >= 0.025f)
            return left > right ? Pose.Left : Pose.Right;
        // The first recorded one-cheek pose can have a smaller lead than a
        // settled pose. Confirm it briefly before activating either side.
        if (_pose == Pose.Neutral && asymmetry >= 0.14f && difference >= 0.05f)
            return left > right ? Pose.Left : Pose.Right;
        if (asymmetry <= 0.12f && MathF.Min(left, right) >= FullStrengthPeak)
            return Pose.Both;
        return Pose.Neutral;
    }

    private bool Confirm(Pose candidate, long nowMs, long durationMs)
    {
        if (_candidate != candidate || nowMs < _candidateSinceMs)
        {
            _candidate = candidate;
            _candidateSinceMs = nowMs;
            return false;
        }
        return nowMs - _candidateSinceMs >= durationMs;
    }

    private void Select(Pose pose)
    {
        _pose = pose;
        ClearCandidate();
    }

    private void ClearCandidate()
    {
        _candidate = Pose.Neutral;
        _candidateSinceMs = 0;
    }
}
