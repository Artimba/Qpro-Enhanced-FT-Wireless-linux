namespace Qpro.GazeBridge;

internal readonly record struct GazePoint(float X, float Y);

/// <summary>Keeps an eye's last open gaze while its detector is obscured by a blink.</summary>
internal sealed class BlinkGazeHold
{
    private const float ClosedThreshold = 0.15f;
    private const float OpenThreshold = 0.08f;
    private const long ReopenDelayMs = 80;

    private GazePoint? _lastOpen;
    private bool _closed;
    private long? _openSince;

    public void Reset()
    {
        _lastOpen = null;
        _closed = false;
        _openSince = null;
    }

    public GazePoint? Update(float blinkWeight, bool blinkAvailable,
        GazePoint? candidate, long nowMs)
    {
        if (candidate is { } point &&
            (!float.IsFinite(point.X) || !float.IsFinite(point.Y)))
            candidate = null;

        float blink = float.IsFinite(blinkWeight)
            ? Math.Clamp(blinkWeight, 0.0f, 1.0f)
            : 0.0f;
        if (blinkAvailable && blink >= ClosedThreshold)
        {
            _closed = true;
            _openSince = null;
        }

        if (_closed)
        {
            // Require a few open frames so detector rays corrupted at the end
            // of a blink cannot create a momentary convergence spike.
            if (blinkAvailable && blink > OpenThreshold)
                _openSince = null;
            else
                _openSince ??= nowMs;

            if (_openSince is null || nowMs - _openSince < ReopenDelayMs)
                return _lastOpen;
            _closed = false;
            _openSince = null;
        }

        if (candidate is { } openPoint)
            _lastOpen = openPoint;
        return _lastOpen;
    }
}
