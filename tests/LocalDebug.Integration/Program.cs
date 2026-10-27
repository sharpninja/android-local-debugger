using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using LocalDebug.Core;
using LocalDebug.DeviceChecks;

if (args.Length != 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: LocalDebug.Integration /path/to/published/MonoDebuggee");
    return 2;
}

int passed = 0;
var output = new List<string>();
int ticks = 0;
int port;
using (var reservation = new TcpListener(IPAddress.Loopback, 0))
{
    reservation.Start();
    port = ((IPEndPoint)reservation.LocalEndpoint).Port;
}
var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var start = new ProcessStartInfo(Path.GetFullPath(args[0]))
{
    RedirectStandardOutput = true, RedirectStandardError = true,
    UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(args[0]))!
};
start.Environment["MONO_ENV_OPTIONS"] = $"--debug --debugger-agent=transport=dt_socket,address=127.0.0.1:{port},server=y,suspend=n";
using var target = new Process { StartInfo = start, EnableRaisingEvents = true };
target.OutputDataReceived += (_, e) =>
{
    if (e.Data is null) return;
    lock (output) { output.Add(e.Data); if (output.Count > 50) output.RemoveAt(0); }
    if (e.Data.StartsWith("READY:")) ready.TrySetResult();
    if (e.Data.StartsWith("TICK:")) Interlocked.Increment(ref ticks);
};
target.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.Add(e.Data); };
target.Exited += (_, _) => ready.TrySetException(new Exception($"Debuggee exited with {target.ExitCode}"));
await using var session = new DebuggerSession();
session.Logged += Console.WriteLine;
session.Changed += snapshot => Console.WriteLine($"STATE {snapshot.State}: {snapshot.Status}");

try
{
    target.Start();
    target.BeginOutputReadLine();
    target.BeginErrorReadLine();
    await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
    await WaitFor(() => Volatile.Read(ref ticks) >= 2);
    Pass("Target runs before a debugger connects");

    await session.AttachAsync(port);
    Require(session.Snapshot.State == SessionState.Paused && session.Snapshot.Threads.Count > 0,
        "Attach must pause the target and enumerate real threads");
    await Task.Delay(150);
    int pausedTicks = Volatile.Read(ref ticks);
    await Task.Delay(250);
    Require(Volatile.Read(ref ticks) == pausedTicks, "The target must actually stop while paused");
    await ExpectFailure(() => session.AttachAsync(port));
    Require(session.Snapshot.State == SessionState.Paused, "Repeated attach must preserve the existing session");
    Pass("Attach, pause, thread enumeration, and repeated-attach protection");

    var breakpoint = await session.SetBreakpointAsync("LocalDebug.Demo.CounterMath", "ComputeNext", 13);
    var duplicate = await session.SetBreakpointAsync("LocalDebug.Demo.CounterMath", "ComputeNext", 13);
    Require(duplicate.Id == breakpoint.Id && session.Snapshot.Breakpoints.Count == 1,
        "Setting the same breakpoint twice must not duplicate requests");
    await ExpectFailure(() => session.SetBreakpointAsync("LocalDebug.Demo.CounterMath", "ComputeNext", 9999));
    Require(session.Snapshot.State == SessionState.Paused, "Invalid source locations must leave the target paused");
    await ContinueUntilStop("Breakpoint");
    var hit = session.Snapshot;
    Require(hit.Frames.Count > 0 && hit.Frames[0].Method.Contains("ComputeNext"), "Breakpoint must stop in the requested method");
    Require(hit.Frames[0].Line == 13, "Breakpoint must stop at the requested source line");
    foreach (var pair in new[] { ("input", "21"), ("multiplier", "2"), ("adjustment", "7"), ("result", "49") })
        Require(hit.Variables.Any(v => v.Name == pair.Item1 && v.Value == pair.Item2), $"Expected real local {pair.Item1}={pair.Item2}");
    Pass("Source breakpoint, deduplication, validation, call stack, and four real values");

    if (hit.Frames.Count > 1)
    {
        await session.SelectFrameAsync(1);
        Require(session.Snapshot.SelectedFrame == 1, "Frame selection must change inspection context");
        await session.SelectFrameAsync(0);
    }
    await session.SelectThreadAsync(hit.SelectedThread!.Value);
    var stepStopped = WaitForStop("Step");
    await session.StepAsync(StepKind.Over);
    await stepStopped;
    Require(session.Snapshot.Frames.Count > 0, "Step must produce an inspectable frame");
    Pass("Frame/thread selection and source-level step over");

    await session.RemoveBreakpointAsync(breakpoint.Id);
    var callBreakpoint = await session.SetBreakpointAsync("LocalDebug.Demo.CounterMath", "ComputeNext", 12);
    await ContinueUntilStop("Breakpoint");
    var entered = WaitForStop("Step");
    await session.StepAsync(StepKind.Into);
    await entered;
    Require(session.Snapshot.Frames[0].Method.Contains("Multiply"), "Step into must enter the helper method");
    Require(session.Snapshot.Variables.Any(v => v.Name == "value" && v.Value == "21"), "Helper argument must be inspectable");
    var returned = WaitForStop("Step");
    await session.StepAsync(StepKind.Out);
    await returned;
    Require(session.Snapshot.Frames[0].Method.Contains("ComputeNext"), "Step out must return to the calling method");
    Pass("Step into a real call, inspect its argument, and step out to its caller");

    await session.RemoveBreakpointAsync(callBreakpoint.Id);
    Require(session.Snapshot.Breakpoints.Count == 0, "Removing a breakpoint must update the session");
    await session.ContinueAsync();
    await WaitFor(() => Volatile.Read(ref ticks) > pausedTicks);
    await session.PauseAsync();
    Require(session.Snapshot.State == SessionState.Paused, "Manual pause must stop a running target");
    Pass("Remove breakpoint, continue, and manual pause");

    int beforeDetach = Volatile.Read(ref ticks);
    await session.DetachAsync();
    await WaitFor(() => Volatile.Read(ref ticks) > beforeDetach);
    Require(!target.HasExited && session.Snapshot.State == SessionState.Disconnected,
        "Detach must resume the target without terminating it");
    Pass("Detach resumes the target and keeps it alive");

    var deviceChecks = await RunSharedDeviceChecks();
    Require(deviceChecks.Passed, "The shared on-device runner failed: " + deviceChecks.Error);
    Require(deviceChecks.Checks.Count == 8 && deviceChecks.Checks.All(check => check.Outcome == "Passed"), "The shared runner did not complete all eight checks.");
    Pass("Eight shared device checks against the live Mono target");

    await session.AttachAsync(port);
    await session.ContinueAsync();
    target.Kill(entireProcessTree: true);
    await target.WaitForExitAsync();
    await WaitFor(() => session.Snapshot.State == SessionState.Disconnected);
    Pass("Reattachment and target-exit detection");

    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int wrongPort = ((IPEndPoint)listener.LocalEndpoint).Port;
    var accepted = listener.AcceptTcpClientAsync();
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
    var failedAttach = session.AttachAsync(wrongPort, cancellation.Token);
    using var wrongServer = await accepted;
    await ExpectFailure(() => failedAttach);
    Require(session.Snapshot.State == SessionState.Disconnected, "Canceled handshake must release the session");
    Pass("Cancellation of a non-debugger endpoint handshake");

    Console.WriteLine($"PASS: {passed} integration scenarios against a live Linux Mono runtime.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    lock (output) foreach (var line in output.TakeLast(30)) Console.Error.WriteLine("TARGET " + line);
    return 1;
}
finally
{
    if (!target.HasExited) { target.Kill(entireProcessTree: true); await target.WaitForExitAsync(); }
}

void Pass(string name) { passed++; Console.WriteLine("PASS " + name); }
static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task ExpectFailure(Func<Task> operation)
{
    try { await operation(); }
    catch { return; }
    throw new Exception("Expected the operation to fail");
}
static async Task WaitFor(Func<bool> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
    while (!condition()) await Task.Delay(25, timeout.Token);
}
Task WaitForStop(string reason)
{
    var previous = session.Snapshot;
    return WaitFor(() => !ReferenceEquals(previous, session.Snapshot)
        && session.Snapshot.State == SessionState.Paused
        && session.Snapshot.Status == $"Stopped: {reason}");
}
async Task ContinueUntilStop(string reason)
{
    var stopped = WaitForStop(reason);
    await session.ContinueAsync();
    await stopped;
}

async Task<AttachCheckReport> RunSharedDeviceChecks()
{
    using var reservation = new TcpListener(IPAddress.Loopback, 0);
    reservation.Start();
    int checkPort = ((IPEndPoint)reservation.LocalEndpoint).Port;
    reservation.Stop();
    var checkStart = new ProcessStartInfo(Path.GetFullPath(args[0]), "--incrementing")
    {
        RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(args[0]))!
    };
    checkStart.Environment["MONO_ENV_OPTIONS"] = $"--debug --debugger-agent=transport=dt_socket,address=127.0.0.1:{checkPort},server=y,suspend=n";
    using var fixture = Process.Start(checkStart) ?? throw new Exception("Could not start the incrementing fixture.");
    try
    {
        var errorOutput = fixture.StandardError.ReadToEndAsync();
        var first = await fixture.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Require(first?.StartsWith("READY:") == true, "Incrementing fixture did not become ready.");
        var remainingOutput = fixture.StandardOutput.ReadToEndAsync();
        return await LocalAttachChecks.RunAsync(checkPort, Console.WriteLine);
    }
    finally
    {
        if (!fixture.HasExited) fixture.Kill(entireProcessTree: true);
        await fixture.WaitForExitAsync();
    }
}
