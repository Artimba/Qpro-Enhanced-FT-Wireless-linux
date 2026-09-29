using Qpro.GazeBridge;

internal static class BlinkGazeContractTests
{
    public static void Run()
    {
        var left = new BlinkGazeHold();
        var right = new BlinkGazeHold();
        Equal(left.Update(0, true, new GazePoint(.12f, .02f), 0), .12f, .02f,
            "open left gaze");
        Equal(right.Update(0, true, new GazePoint(-.12f, .02f), 0), -.12f, .02f,
            "open right gaze preserves true convergence");

        // A closed eye's ray can remain marked valid while pointing sharply
        // inward. Hold its last open ray, but keep the other eye independent.
        Equal(left.Update(.85f, true, new GazePoint(-.7f, -.3f), 20), .12f, .02f,
            "blink does not change left gaze");
        Equal(right.Update(0, true, new GazePoint(-.18f, .03f), 20), -.18f, .03f,
            "open right eye still tracks");
        Equal(left.Update(.11f, true, new GazePoint(-.7f, -.3f), 60), .12f, .02f,
            "partly closed lid keeps hold");
        Equal(left.Update(.04f, true, new GazePoint(-.7f, -.3f), 80), .12f, .02f,
            "first open frame is held");
        Equal(left.Update(.04f, true, new GazePoint(.2f, .05f), 140), .12f, .02f,
            "reopen dwell rejects transient ray");
        Equal(left.Update(.04f, true, new GazePoint(.2f, .05f), 160), .2f, .05f,
            "fresh open gaze resumes after dwell");

        // A second blink during the dwell restarts it.
        Equal(left.Update(.5f, true, new GazePoint(-.9f, 0), 180), .2f, .05f,
            "second blink holds last open gaze");
        Equal(left.Update(.01f, true, new GazePoint(-.9f, 0), 190), .2f, .05f,
            "second reopen starts dwell");
        Equal(left.Update(.2f, true, new GazePoint(-.9f, 0), 250), .2f, .05f,
            "closure interrupts reopen dwell");
        Equal(left.Update(.01f, true, new GazePoint(.3f, .01f), 260), .2f, .05f,
            "reopened dwell restarts");
        Equal(left.Update(.01f, true, new GazePoint(.3f, .01f), 340), .3f, .01f,
            "open gaze resumes after interrupted dwell");

        // A missing face stream must not strand gaze in the held state.
        Equal(left.Update(.9f, true, new GazePoint(-.9f, 0), 350), .3f, .01f,
            "closed eye holds before face dropout");
        Equal(left.Update(0, false, new GazePoint(.1f, .04f), 360), .3f, .01f,
            "face dropout starts release");
        Equal(left.Update(0, false, new GazePoint(.1f, .04f), 440), .1f, .04f,
            "face dropout does not hold gaze indefinitely");
        Equal(left.Update(0, true, new GazePoint(float.NaN, 0), 450), .1f, .04f,
            "invalid gaze cannot enter the output");
        left.Reset();
        if (left.Update(.9f, true, new GazePoint(-.9f, 0), 460) is not null)
            throw new Exception("first closed frame must not initialize gaze");
    }

    private static void Equal(GazePoint? actual, float x, float y, string label)
    {
        if (actual is not { } point ||
            MathF.Abs(point.X - x) > .00001f ||
            MathF.Abs(point.Y - y) > .00001f)
            throw new Exception($"{label}: got {actual}, wanted ({x}, {y})");
    }
}
