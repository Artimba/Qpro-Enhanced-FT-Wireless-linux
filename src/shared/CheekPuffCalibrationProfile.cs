using System.Text.Json;

namespace Qpro.Shared;

internal readonly record struct CheekPuffCalibration(
    float LeftNeutral, float LeftFull, float RightNeutral, float RightFull,
    float LeftDeadZone = 0, float RightDeadZone = 0, CheekPuffRawResponse? RawResponse = null);

internal readonly record struct CheekPuffRawResponse(
    float LeftOnRight, float RightOnLeft, float BothLeft, float BothRight);

internal static class CheekPuffCalibrationProfile
{
    internal const string VirtualDesktopSource = "virtual-desktop";
    internal const string SteamLinkSource = "steam-link";
    internal const float MinimumRange = 0.02f;
    internal const float RawDeadZoneFloor = .03f;

    private static string DefaultConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QproFaceTracking", "config");

    internal static bool IsValid(CheekPuffCalibration calibration)
        => IsValidSide(calibration.LeftNeutral, calibration.LeftFull, calibration.LeftDeadZone) &&
           IsValidSide(calibration.RightNeutral, calibration.RightFull, calibration.RightDeadZone) &&
           (calibration.RawResponse is null || IsValidRawResponse(calibration));

    private static bool IsValidRawResponse(CheekPuffCalibration calibration)
    {
        CheekPuffRawResponse raw = calibration.RawResponse!.Value;
        if (!InRange(raw.LeftOnRight) || !InRange(raw.RightOnLeft) ||
            !InRange(raw.BothLeft) || !InRange(raw.BothRight)) return false;
        float leftRange = calibration.LeftFull - calibration.LeftNeutral;
        float rightRange = calibration.RightFull - calibration.RightNeutral;
        float leftCross = raw.LeftOnRight - calibration.LeftNeutral;
        float rightCross = raw.RightOnLeft - calibration.RightNeutral;
        float determinant = leftRange * rightRange - leftCross * rightCross;
        // Indistinguishable left/right poses make inversion amplify tiny
        // sensor changes. Reject that fit rather than saving a noisy profile.
        if (determinant < leftRange * rightRange * .12f) return false;
        float bothLeft = raw.BothLeft - calibration.LeftNeutral;
        float bothRight = raw.BothRight - calibration.RightNeutral;
        float bothLeftStrength = (rightRange * bothLeft - leftCross * bothRight) / determinant;
        float bothRightStrength = (leftRange * bothRight - rightCross * bothLeft) / determinant;
        if (bothLeftStrength is not (>= .15f and <= 3) || bothRightStrength is not (>= .15f and <= 3)) return false;
        var noise = RawNoiseMargins(calibration);
        var sensitivity = RawNoiseMargins(calibration with { LeftDeadZone = 1, RightDeadZone = 1 });
        // A quiet hold must not conceal a nearly singular fit. Its maximum
        // response to later raw jitter cannot exceed the gain of an uncoupled
        // cheek at the already required minimum usable range.
        float maximumSensitivity = 1 / (MinimumRange * (1 - RawDeadZoneFloor)) + .0001f;
        return noise.Left <= .20f && noise.Right <= .20f &&
            sensitivity.Left / (1 - MathF.Max(RawDeadZoneFloor, noise.Left)) <= maximumSensitivity &&
            sensitivity.Right / (1 - MathF.Max(RawDeadZoneFloor, noise.Right)) <= maximumSensitivity;
    }

    private static bool InRange(float value) => float.IsFinite(value) && value is >= 0 and <= 1;

    internal static (float Left, float Right) RawNoiseMargins(CheekPuffCalibration calibration)
    {
        CheekPuffRawResponse raw = calibration.RawResponse!.Value;
        float leftRange = calibration.LeftFull - calibration.LeftNeutral;
        float rightRange = calibration.RightFull - calibration.RightNeutral;
        float leftCross = raw.LeftOnRight - calibration.LeftNeutral;
        float rightCross = raw.RightOnLeft - calibration.RightNeutral;
        float determinant = leftRange * rightRange - leftCross * rightCross;
        float a = rightRange / determinant, b = -leftCross / determinant;
        float c = -rightCross / determinant, d = leftRange / determinant;
        float bothLeft = (rightRange * (raw.BothLeft - calibration.LeftNeutral) - leftCross * (raw.BothRight - calibration.RightNeutral)) / determinant;
        float bothRight = (leftRange * (raw.BothRight - calibration.RightNeutral) - rightCross * (raw.BothLeft - calibration.LeftNeutral)) / determinant;
        // The four anchors form two affine triangles. Bound noise using the
        // actual coefficients of both regions, including their cancellations,
        // and the inverse-only region when one axis is below neutral.
        float Weighted(float left, float right) => MathF.Abs(left) * calibration.LeftDeadZone +
            MathF.Abs(right) * calibration.RightDeadZone;
        float leftDominant = (1 - bothLeft) / bothRight;
        float rightDominant = (1 - bothRight) / bothLeft;
        float leftNoise = MathF.Max(Weighted(a, b), MathF.Max(
            Weighted(a + leftDominant * c, b + leftDominant * d), Weighted(a / bothLeft, b / bothLeft)));
        float rightNoise = MathF.Max(Weighted(c, d), MathF.Max(
            Weighted(c / bothRight, d / bothRight), Weighted(c + rightDominant * a, d + rightDominant * b)));
        return (leftNoise, rightNoise);
    }

    internal static (float Left, float Right) CorrectRawStrength(CheekPuffCalibration calibration, float left, float right)
    {
        CheekPuffRawResponse raw = calibration.RawResponse!.Value;
        float leftRange = calibration.LeftFull - calibration.LeftNeutral;
        float rightRange = calibration.RightFull - calibration.RightNeutral;
        float leftCross = raw.LeftOnRight - calibration.LeftNeutral;
        float rightCross = raw.RightOnLeft - calibration.RightNeutral;
        float determinant = leftRange * rightRange - leftCross * rightCross;
        (float Left, float Right) Unmix(float leftValue, float rightValue) =>
            ((rightRange * leftValue - leftCross * rightValue) / determinant,
             (leftRange * rightValue - rightCross * leftValue) / determinant);
        var strength = Unmix(left - calibration.LeftNeutral, right - calibration.RightNeutral);
        var both = Unmix(raw.BothLeft - calibration.LeftNeutral, raw.BothRight - calibration.RightNeutral);
        float shared = MathF.Max(0, MathF.Min(strength.Left / both.Left, strength.Right / both.Right));
        return (strength.Left + shared * (1 - both.Left), strength.Right + shared * (1 - both.Right));
    }

    internal static CheekPuffCalibration? Load(string source, string? configDirectory = null)
    {
        string path = ProfilePath(source, configDirectory);
        try
        {
            if (!File.Exists(path)) return null;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out JsonElement version) ||
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out int versionNumber) || versionNumber is not (1 or 2 or 3) ||
                !TryReadAnchor(root, "leftNeutral", out float leftNeutral) ||
                !TryReadAnchor(root, "leftFull", out float leftFull) ||
                !TryReadAnchor(root, "rightNeutral", out float rightNeutral) ||
                !TryReadAnchor(root, "rightFull", out float rightFull))
                return null;

            float leftDeadZone = 0, rightDeadZone = 0;
            if (versionNumber >= 2 &&
                (!TryReadAnchor(root, "leftDeadZone", out leftDeadZone) ||
                 !TryReadAnchor(root, "rightDeadZone", out rightDeadZone)))
                return null;
            CheekPuffRawResponse? rawResponse = null;
            if (versionNumber == 3)
            {
                if (!root.TryGetProperty("rawResponse", out JsonElement raw) || raw.ValueKind != JsonValueKind.Object ||
                    !TryReadAnchor(raw, "leftOnRight", out float leftOnRight) ||
                    !TryReadAnchor(raw, "rightOnLeft", out float rightOnLeft) ||
                    !TryReadAnchor(raw, "bothLeft", out float bothLeft) ||
                    !TryReadAnchor(raw, "bothRight", out float bothRight)) return null;
                rawResponse = new(leftOnRight, rightOnLeft, bothLeft, bothRight);
            }
            CheekPuffCalibration calibration = new(
                leftNeutral, leftFull, rightNeutral, rightFull, leftDeadZone, rightDeadZone, rawResponse);
            return IsValid(calibration) ? calibration : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    internal static void Save(string source, CheekPuffCalibration calibration,
        string? configDirectory = null)
    {
        string path = ProfilePath(source, configDirectory);
        if (!IsValid(calibration))
            throw new ArgumentOutOfRangeException(nameof(calibration),
                "Each cheek needs finite neutral and full anchors in 0..1, at least 0.02 apart, and a dead zone no larger than a quarter of its range.");

        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory,
            $"cheek-puff-calibration-{source}-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new
            {
                version = calibration.RawResponse.HasValue ? 3 : 2,
                leftNeutral = calibration.LeftNeutral,
                leftFull = calibration.LeftFull,
                rightNeutral = calibration.RightNeutral,
                rightFull = calibration.RightFull,
                leftDeadZone = calibration.LeftDeadZone,
                rightDeadZone = calibration.RightDeadZone,
                rawResponse = calibration.RawResponse is { } raw ? new
                {
                    leftOnRight = raw.LeftOnRight, rightOnLeft = raw.RightOnLeft,
                    bothLeft = raw.BothLeft, bothRight = raw.BothRight,
                } : null,
            }));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    internal static string ProfilePath(string source, string? configDirectory = null)
    {
        if (source is not (VirtualDesktopSource or SteamLinkSource))
            throw new ArgumentException("Unknown face tracking source", nameof(source));
        return Path.Combine(configDirectory ?? DefaultConfigDirectory,
            $"cheek-puff-calibration-{source}.json");
    }

    private static bool IsValidSide(float neutral, float full, float deadZone)
        => float.IsFinite(neutral) && float.IsFinite(full) && float.IsFinite(deadZone) &&
           neutral >= 0.0f && full <= 1.0f && full - neutral >= MinimumRange &&
           deadZone >= 0 && deadZone <= (full - neutral) * .25f;

    private static bool TryReadAnchor(JsonElement root, string name, out float value)
    {
        value = 0.0f;
        return root.TryGetProperty(name, out JsonElement element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetSingle(out value) && float.IsFinite(value);
    }
}

internal readonly record struct CheekPuffPoseSummary(
    float LeftMedian, float RightMedian, float LeftSpread, float RightSpread,
    float LeftUpperNoise, float RightUpperNoise, float LeftDrift = 0, float RightDrift = 0)
{
    internal IReadOnlyList<(float Left, float Right)>? PairedSamples { get; init; }
}

// Shared with the offline replay tests so the dialog's acceptance rules are
// checked with the same samples that would be captured from the live feed.
internal static class CheekPuffCalibrationCapture
{
    internal const int MinimumSamples = 30;
    private const int PlateauSamples = 20;

    internal static bool TrySummarize(IReadOnlyList<(float Left, float Right)> samples,
        out CheekPuffPoseSummary summary, out string problem)
    {
        summary = default;
        problem = "";
        if (samples.Count < MinimumSamples)
        {
            problem = "There were too few fresh samples. Keep the headset connected and capture this step again.";
            return false;
        }
        if (samples.Any(sample => !float.IsFinite(sample.Left) || !float.IsFinite(sample.Right) ||
            sample.Left is < 0 or > 1 || sample.Right is < 0 or > 1))
        {
            problem = "The cheek feed contained invalid values. Capture this step again.";
            return false;
        }
        summary = Summarize(samples);
        return true;
    }

    private static CheekPuffPoseSummary Summarize(IReadOnlyList<(float Left, float Right)> samples)
    {
        float[] left = samples.Select(sample => sample.Left).Order().ToArray();
        float[] right = samples.Select(sample => sample.Right).Order().ToArray();
        float leftMedian = Percentile(left, .5f), rightMedian = Percentile(right, .5f);
        int segment = Math.Max(3, samples.Count / 3);
        float leftDrift = MathF.Abs(Percentile(samples.Take(segment).Select(sample => sample.Left).Order().ToArray(), .5f) -
            Percentile(samples.TakeLast(segment).Select(sample => sample.Left).Order().ToArray(), .5f));
        float rightDrift = MathF.Abs(Percentile(samples.Take(segment).Select(sample => sample.Right).Order().ToArray(), .5f) -
            Percentile(samples.TakeLast(segment).Select(sample => sample.Right).Order().ToArray(), .5f));
        return new(leftMedian, rightMedian,
            Percentile(left, .9f) - Percentile(left, .1f),
            Percentile(right, .9f) - Percentile(right, .1f),
            Percentile(left, .9f) - leftMedian, Percentile(right, .9f) - rightMedian, leftDrift, rightDrift)
            { PairedSamples = samples.ToArray() };
    }

    internal static bool TrySummarizeSteadyPuff(IReadOnlyList<(float Left, float Right)> samples,
        CheekPuffPoseSummary neutral, int poseIndex, out CheekPuffPoseSummary summary, out string problem,
        float leftFull = 1, float rightFull = 1)
    {
        if (poseIndex is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(poseIndex));
        if (!TrySummarize(samples, out summary, out problem)) return false;
        var wholeHold = summary;
        // The native feed is capped at 20 Hz. Use the strongest steady one
        // second, rather than treating fatigue over a three-second hold as
        // the full-strength anchor. Medians ignore isolated spikes; a short
        // twitch cannot supply twenty consecutive usable samples.
        float bestScore = float.NegativeInfinity;
        CheekPuffPoseSummary best = default;
        for (int start = 0; start <= samples.Count - PlateauSamples; start++)
        {
            var candidate = Summarize(samples.Skip(start).Take(PlateauSamples).ToArray());
            bool stable = poseIndex == 3 ? IsSteadyBoth(candidate, neutral, leftFull, rightFull)
                : TryAcceptSide(candidate, neutral, poseIndex == 1, out _, out _, rawInputs: true);
            if (!stable) continue;
            float score = poseIndex switch
            {
                1 => candidate.LeftMedian - neutral.LeftMedian,
                2 => candidate.RightMedian - neutral.RightMedian,
                _ => MathF.Min((candidate.LeftMedian - neutral.LeftMedian) / (leftFull - neutral.LeftMedian),
                    (candidate.RightMedian - neutral.RightMedian) / (rightFull - neutral.RightMedian)),
            };
            if (score > bestScore) { bestScore = score; best = candidate; }
        }
        if (!float.IsFinite(bestScore))
        {
            problem = "There was no steady puff for long enough. Settle into a comfortable full puff, then hold it until the progress bar finishes and capture this step again.";
            return false;
        }
        var tail = Summarize(samples.TakeLast(3).ToArray());
        bool leftReleased = tail.LeftMedian - neutral.LeftMedian < (best.LeftMedian - neutral.LeftMedian) * .35f;
        bool rightReleased = tail.RightMedian - neutral.RightMedian < (best.RightMedian - neutral.RightMedian) * .35f;
        if (poseIndex == 1 ? leftReleased : poseIndex == 2 ? rightReleased : leftReleased || rightReleased)
        {
            problem = "The puff released before the capture finished. Keep holding until the progress bar finishes, then relax and capture this step again.";
            return false;
        }
        // Keep uncertainty from the entire hold. Selecting a stable anchor
        // must not hide shaking, chronological drift, or a longer bad section.
        summary = best with
        {
            LeftSpread = MathF.Max(best.LeftSpread, wholeHold.LeftSpread),
            RightSpread = MathF.Max(best.RightSpread, wholeHold.RightSpread),
            LeftDrift = MathF.Max(best.LeftDrift, wholeHold.LeftDrift),
            RightDrift = MathF.Max(best.RightDrift, wholeHold.RightDrift),
            PairedSamples = wholeHold.PairedSamples,
        };
        bool wholeSteady = poseIndex == 3 ? IsSteadyBoth(summary, neutral, leftFull, rightFull)
            : TryAcceptSide(summary, neutral, poseIndex == 1, out _, out _, rawInputs: true);
        if (!wholeSteady)
        {
            problem = "The puff drifted or moved too much across the hold. Settle into a comfortable full puff and keep it steady until the progress bar finishes, then capture this step again.";
            return false;
        }
        return true;
    }

    internal static bool TryAcceptNeutral(CheekPuffPoseSummary neutral, out string problem)
    {
        problem = "";
        if (MathF.Max(neutral.LeftSpread, neutral.RightSpread) <= .04f &&
            MathF.Max(neutral.LeftDrift, neutral.RightDrift) <= .02f) return true;
        problem = "Relaxed cheeks moved too much during the hold. Settle your jaw and capture relaxed cheeks again.";
        return false;
    }

    internal static bool TryAcceptSide(CheekPuffPoseSummary pose, CheekPuffPoseSummary neutral,
        bool left, out float full, out string problem, bool rawInputs = false)
    {
        full = left ? pose.LeftMedian : pose.RightMedian;
        float gain = full - (left ? neutral.LeftMedian : neutral.RightMedian);
        float neutralSpread = rawInputs ? (left ? neutral.LeftSpread : neutral.RightSpread)
            : MathF.Max(neutral.LeftSpread, neutral.RightSpread);
        problem = "";
        if (gain < CheekPuffCalibrationProfile.MinimumRange)
        {
            problem = "This cheek did not differ enough from relaxed cheeks. Use a comfortable, stronger puff on the requested side, then capture this step again.";
            return false;
        }
        if (neutralSpread > gain * .25f)
        {
            problem = "Relaxed cheek noise is too large compared with this puff. Start again with steady relaxed cheeks, or try a comfortable, stronger puff.";
            return false;
        }
        // The old absolute 0.10 limit could accept five times the entire
        // range of a 0.02 puff. Check both cheeks relative to the actual gain.
        float leftGain = MathF.Abs(pose.LeftMedian - neutral.LeftMedian);
        float rightGain = MathF.Abs(pose.RightMedian - neutral.RightMedian);
        float allowedLeft = MathF.Max(.006f, (rawInputs ? leftGain : gain) * .25f);
        float allowedRight = MathF.Max(.006f, (rawInputs ? rightGain : gain) * .25f);
        if (pose.LeftSpread > allowedLeft || pose.RightSpread > allowedRight ||
            pose.LeftDrift > MathF.Max(.004f, leftGain * .10f) || pose.RightDrift > MathF.Max(.004f, rightGain * .10f))
        {
            problem = "One cheek changed too much during the hold. Settle into the pose and keep the other cheek relaxed, then capture this step again.";
            return false;
        }
        float otherGain = left ? pose.RightMedian - neutral.RightMedian : pose.LeftMedian - neutral.LeftMedian;
        // Native channel gains differ by wearer. A left puff can legitimately
        // cause a larger right-channel delta. Its orientation is checked only
        // after both labeled poses are available, using the paired fit.
        if (!rawInputs && otherGain >= gain * .35f)
        {
            problem = "There was no clear lead on the requested cheek. Use your own left/right side and keep the other cheek relaxed, then capture this step again.";
            return false;
        }
        return true;
    }

    internal static CheekPuffCalibration Create(CheekPuffPoseSummary neutral, float leftFull, float rightFull)
        => new(neutral.LeftMedian, leftFull, neutral.RightMedian, rightFull,
            MathF.Max(neutral.LeftUpperNoise, (leftFull - neutral.LeftMedian) * .03f),
            MathF.Max(neutral.RightUpperNoise, (rightFull - neutral.RightMedian) * .03f));

    internal static bool TryCreateRaw(CheekPuffPoseSummary neutral, CheekPuffPoseSummary left,
        CheekPuffPoseSummary right, CheekPuffPoseSummary both, out CheekPuffCalibration calibration, out string problem)
    {
        calibration = new(neutral.LeftMedian, left.LeftMedian, neutral.RightMedian, right.RightMedian,
            MathF.Max(neutral.LeftUpperNoise, neutral.LeftSpread - neutral.LeftUpperNoise),
            MathF.Max(neutral.RightUpperNoise, neutral.RightSpread - neutral.RightUpperNoise),
            new(right.LeftMedian, left.RightMedian, both.LeftMedian, both.RightMedian));
        problem = "";
        if (!IsSteadyBoth(both, neutral, left.LeftMedian, right.RightMedian))
        {
            problem = "Both-cheek strength changed too much during the hold. Settle into a comfortable puff on both sides and capture it again.";
            return false;
        }
        if (CheekPuffCalibrationProfile.IsValid(calibration))
        {
            foreach (var pose in new[] { left, right, both })
            {
                (float Left, float Right) spread;
                if (pose.PairedSamples is { Count: >= MinimumSamples } samples)
                {
                    // Common strength variation moves both native channels
                    // together. Project actual pairs so that it isn't mistaken
                    // for independent sensor noise amplified by the inverse.
                    var fitted = new (float Left, float Right)[samples.Count];
                    for (int sampleIndex = 0; sampleIndex < samples.Count; sampleIndex++)
                        fitted[sampleIndex] = CheekPuffCalibrationProfile.CorrectRawStrength(calibration,
                            samples[sampleIndex].Left, samples[sampleIndex].Right);
                    float[] fittedLeft = fitted.Select(value => value.Left).Order().ToArray();
                    float[] fittedRight = fitted.Select(value => value.Right).Order().ToArray();
                    spread = (Percentile(fittedLeft, .9f) - Percentile(fittedLeft, .1f),
                        Percentile(fittedRight, .9f) - Percentile(fittedRight, .1f));
                }
                else spread = CheekPuffCalibrationProfile.RawNoiseMargins(calibration with
                    { LeftDeadZone = pose.LeftSpread, RightDeadZone = pose.RightSpread });
                if (MathF.Max(spread.Left, spread.Right) > .35f)
                {
                    problem = "The single-cheek holds overlap too much for their measured noise. Start again and keep each pose steady; use a comfortable, clearer puff on each side. Your existing profile has been kept.";
                    return false;
                }
            }
            return true;
        }
        problem = "The captured poses could not be separated reliably. Start again, keep each single-cheek puff steady, then puff both cheeks together for the final pose. Your existing profile has been kept.";
        return false;
    }

    private static bool IsSteadyBoth(CheekPuffPoseSummary both, CheekPuffPoseSummary neutral, float leftFull, float rightFull)
    {
        float leftRange = leftFull - neutral.LeftMedian, rightRange = rightFull - neutral.RightMedian;
        return leftRange >= CheekPuffCalibrationProfile.MinimumRange && rightRange >= CheekPuffCalibrationProfile.MinimumRange &&
            both.LeftSpread <= MathF.Max(.006f, leftRange * .25f) &&
            both.RightSpread <= MathF.Max(.006f, rightRange * .25f) &&
            both.LeftDrift <= MathF.Max(.004f, leftRange * .10f) &&
            both.RightDrift <= MathF.Max(.004f, rightRange * .10f);
    }

    private static float Percentile(float[] sorted, float fraction)
    {
        float index = (sorted.Length - 1) * fraction;
        int lower = (int)index;
        return sorted[lower] + (sorted[Math.Min(lower + 1, sorted.Length - 1)] - sorted[lower]) * (index - lower);
    }
}
