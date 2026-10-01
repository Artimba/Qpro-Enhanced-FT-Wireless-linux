using System.Collections.Concurrent;
using QproFaceTracking.Hub;

// The process tests use this executable as a harmless ADB stand-in.
// No real ADB server, device, or installed Qpro runtime is touched.
if (args.Length > 0)
{
    if (args[0] == "devices") Console.WriteLine("fixture-device\tdevice");
    if (args[0] == "hang") await Task.Delay(Timeout.Infinite);
    if (args[0] == "fail") { Console.Error.WriteLine("fixture command failure"); return 7; }
    return 0;
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var commands = new ConcurrentQueue<string>();
var probeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var probeFinished = false;
var session = new HubAdbSession(async (_, arguments, _, cancellation) =>
{
    var command = arguments.First();
    commands.Enqueue(command);
    if (command == "devices")
    {
        probeStarted.SetResult();
        try { await Task.Delay(Timeout.Infinite, cancellation); }
        finally { probeFinished = true; }
    }
    Check(probeFinished, "kill-server ran before the outstanding probe drained.");
    return (true, 0, "");
});
var pendingProbe = session.ProbeAsync("fixture-adb", ["devices"]);
await probeStarted.Task;
var queuedProbe = session.ProbeAsync("fixture-adb", ["connect", "fixture-address"]);
var stopped = await session.StopServerAsync("fixture-adb");
Check(stopped.Completed && stopped.ExitCode == 0, "Server shutdown did not finish.");
Check(!(await pendingProbe).Completed && !(await queuedProbe).Completed, "Closing left a probe active.");
Check(commands.SequenceEqual(new[] { "devices", "kill-server" }), "A queued probe restarted ADB while closing.");
Check(!(await session.ProbeAsync("fixture-adb", ["devices"])).Completed, "A later status tick restarted ADB.");

var idleCommands = new List<string>();
var idleSession = new HubAdbSession((_, arguments, _, _) =>
{
    idleCommands.Add(arguments.First());
    return Task.FromResult((true, 0, ""));
});
await idleSession.StopServerAsync("fixture-adb");
Check(idleCommands.SequenceEqual(new[] { "kill-server" }), "Idle shutdown must only send kill-server.");
idleSession.ResumeProbes();
Check((await idleSession.ProbeAsync("fixture-adb", ["devices"])).Completed,
    "Canceling a Hub close must allow status checks again.");

var failedSession = new HubAdbSession((_, _, _, _) => Task.FromResult((false, -1, "fixture timeout")));
Check(!(await failedSession.StopServerAsync("fixture-adb")).Completed, "A failed shutdown was reported as successful.");

var processSession = new HubAdbSession();
var fakeAdb = Path.Combine(AppContext.BaseDirectory, "HubAdbTests.exe");
var devices = await processSession.ProbeAsync(fakeAdb, ["devices"]);
Check(devices.Completed && devices.Output.Contains("fixture-device"), "Process output was not captured.");
var failure = await processSession.ProbeAsync(fakeAdb, ["fail"]);
Check(failure.Completed && failure.ExitCode == 7 && failure.Output.Contains("fixture command failure"),
    "Process exit code or stderr was lost.");
var timeout = await processSession.ProbeAsync(fakeAdb, ["hang"], 1);
Check(!timeout.Completed && timeout.Output == "ADB timed out.", "A hung command did not time out.");
Check((await processSession.StopServerAsync(fakeAdb)).Completed, "Process-backed shutdown failed.");

Console.WriteLine("ADB shutdown checks passed: idle close, canceled/queued probes, no restart, retry, output, failure and timeout.");
return 0;
