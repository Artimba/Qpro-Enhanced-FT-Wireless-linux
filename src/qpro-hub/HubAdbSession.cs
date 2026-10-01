using System.Diagnostics;

namespace QproFaceTracking.Hub;

internal sealed class HubAdbSession
{
    internal delegate Task<(bool Completed, int ExitCode, string Output)> CommandRunner(
        string adb, IEnumerable<string> arguments, int timeoutSeconds, CancellationToken cancellation);

    private readonly SemaphoreSlim _probeGate = new(1, 1);
    private readonly CommandRunner _run;
    private CancellationTokenSource _probeCancellation = new();

    internal HubAdbSession(CommandRunner? runner = null) => _run = runner ?? RunCommandAsync;

    internal async Task<(bool Completed, int ExitCode, string Output)> ProbeAsync(
        string adb, IEnumerable<string> arguments, int timeoutSeconds = 4)
    {
        var cancellation = _probeCancellation.Token;
        try { await _probeGate.WaitAsync(cancellation); }
        catch (OperationCanceledException) { return (false, -1, "ADB status checks stopped for Hub shutdown."); }
        try
        {
            cancellation.ThrowIfCancellationRequested();
            return await _run(adb, arguments, timeoutSeconds, cancellation);
        }
        catch (OperationCanceledException) { return (false, -1, "ADB status checks stopped for Hub shutdown."); }
        finally { _probeGate.Release(); }
    }

    internal async Task SuspendProbesAsync()
    {
        // A late devices/get-state/connect probe would restart ADB after kill-server.
        // Cancel and drain probes before shutdown, while restoration scripts can still use ADB.
        _probeCancellation.Cancel();
        await _probeGate.WaitAsync();
        _probeGate.Release();
    }

    internal void ResumeProbes()
    {
        if (!_probeCancellation.IsCancellationRequested) return;
        _probeCancellation.Dispose();
        _probeCancellation = new CancellationTokenSource();
    }

    internal async Task<(bool Completed, int ExitCode, string Output)> StopServerAsync(string adb)
    {
        await SuspendProbesAsync();
        // Use the configured ADB command and endpoint; never kill unrelated adb.exe processes by name.
        return await _run(adb, ["kill-server"], 4, CancellationToken.None);
    }

    private static async Task<(bool Completed, int ExitCode, string Output)> RunCommandAsync(
        string adb, IEnumerable<string> arguments, int timeoutSeconds, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var info = new ProcessStartInfo(adb)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (!process.Start()) return (false, -1, "ADB could not start.");
            var standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var standardError = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = string.Join("\n", new[] { await standardOutput, await standardError }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            return (true, process.ExitCode, output);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* Process already ended. */ }
            return (false, -1, cancellation.IsCancellationRequested
                ? "ADB status checks stopped for Hub shutdown." : "ADB timed out.");
        }
        catch (Exception error) { return (false, -1, error.Message); }
    }
}
