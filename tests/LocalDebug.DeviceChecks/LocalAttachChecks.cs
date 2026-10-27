using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using LocalDebug.Core;

namespace LocalDebug.DeviceChecks;

public sealed record AttachCheck(string Name, string Outcome, long ElapsedMilliseconds = 0, string? Error = null);

public sealed class AttachCheckReport
{
    public int SchemaVersion { get; } = 1;
    public int ExpectedChecks { get; } = 8;
    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedUtc { get; set; }
    public bool Passed => Error is null && Checks.Count == ExpectedChecks && Checks.All(c => c.Outcome == "Passed");
    public string? Error { get; set; }
    public string? CleanupError { get; set; }
    public Dictionary<string, string> Metadata { get; } = [];
    public List<AttachCheck> Checks { get; } = [];
    public List<string> Logs { get; } = [];
}

/// <summary>Runs in the debugger's process; all debug traffic stays on loopback.</summary>
public static class LocalAttachChecks
{
    public static async Task<AttachCheckReport> RunAsync(int port, Action<string>? log = null)
    {
        var report = new AttachCheckReport();
        string[] names = ["Attach and enumerate threads", "Source breakpoint and real locals",
            "Thread and stack-frame selection", "Step over", "Step into and out of a call",
            "Remove breakpoint, continue, and pause", "Detach, reattach, and observe target progress",
            "Cancel a handshake to a non-debugger endpoint"];
        report.Checks.AddRange(names.Select(n => new AttachCheck(n, "NotRun")));
        var messages = new ConcurrentQueue<string>();
        var session = new DebuggerSession();
        session.Logged += message =>
        {
            messages.Enqueue(message);
            while (messages.Count > 150) messages.TryDequeue(out _);
            log?.Invoke(message);
        };
        DebugBreakpoint? breakpoint = null;
        long selectedThread = 0;
        try
        {
            await Check(0, async () =>
            {
                await session.AttachAsync(port).ConfigureAwait(false);
                Require(session.Snapshot.State == SessionState.Paused, "Attachment did not pause the target.");
                Require(session.Snapshot.Threads.Count > 0, "No managed threads were returned.");
                report.Metadata["LoopbackEndpoint"] = $"127.0.0.1:{port}";
            });
            await Check(1, async () =>
            {
                breakpoint = await session.SetBreakpointAsync("LocalDebug.Demo.CounterMath", "ComputeNext", 13).ConfigureAwait(false);
                await ContinueToBreakpoint().ConfigureAwait(false);
                var snapshot = session.Snapshot;
                Require(snapshot.Frames.Count > 0 && snapshot.Frames[0].Method.Contains("ComputeNext"), "Wrong breakpoint frame.");
                Require(snapshot.Frames[0].Line == 13, "Wrong breakpoint source line.");
                int input = IntLocal("input");
                Require(IntLocal("multiplier") == 2, "Incorrect multiplier.");
                Require(IntLocal("adjustment") == 7, "Incorrect adjustment.");
                Require(IntLocal("result") == input * 2 + 7, "The computed result does not match its real input.");
                report.Metadata["ObservedInput"] = input.ToString(CultureInfo.InvariantCulture);
                report.Metadata["ObservedResult"] = IntLocal("result").ToString(CultureInfo.InvariantCulture);
                selectedThread = snapshot.SelectedThread ?? throw new Exception("No stopped thread selected.");
            });
            await Check(2, async () =>
            {
                Require(session.Snapshot.Frames.Count > 1, "The demo did not expose a calling frame.");
                await session.SelectFrameAsync(1).ConfigureAwait(false);
                Require(session.Snapshot.SelectedFrame == 1, "Stack-frame selection failed.");
                await session.SelectFrameAsync(0).ConfigureAwait(false);
                await session.SelectThreadAsync(selectedThread).ConfigureAwait(false);
                Require(session.Snapshot.SelectedThread == selectedThread, "Thread selection failed.");
            });
            await Check(3, async () =>
            {
                await Step(StepKind.Over).ConfigureAwait(false);
                Require(session.Snapshot.Frames.Count > 0, "Step over produced no inspectable frame.");
            });
            await Check(4, async () =>
            {
                await session.RemoveBreakpointAsync(breakpoint!.Id).ConfigureAwait(false);
                breakpoint = await session.SetBreakpointAsync("LocalDebug.Demo.CounterMath", "ComputeNext", 12).ConfigureAwait(false);
                await ContinueToBreakpoint().ConfigureAwait(false);
                int input = IntLocal("input");
                await Step(StepKind.Into).ConfigureAwait(false);
                Require(session.Snapshot.Frames[0].Method.Contains("Multiply"), "Step into did not enter Multiply.");
                Require(IntLocal("value") == input, "The helper argument differs from the caller's input.");
                await Step(StepKind.Out).ConfigureAwait(false);
                Require(session.Snapshot.Frames[0].Method.Contains("ComputeNext"), "Step out did not return to the caller.");
            });
            await Check(5, async () =>
            {
                await session.RemoveBreakpointAsync(breakpoint!.Id).ConfigureAwait(false);
                Require(session.Snapshot.Breakpoints.Count == 0, "A removed breakpoint remains installed.");
                await session.ContinueAsync().ConfigureAwait(false);
                await Task.Delay(150).ConfigureAwait(false);
                await session.PauseAsync().ConfigureAwait(false);
                Require(session.Snapshot.State == SessionState.Paused, "Manual pause failed.");
            });
            await Check(6, async () =>
            {
                breakpoint = await session.SetBreakpointAsync("LocalDebug.Demo.CounterMath", "ComputeNext", 13).ConfigureAwait(false);
                await ContinueToBreakpoint().ConfigureAwait(false);
                int before = IntLocal("input");
                await session.RemoveBreakpointAsync(breakpoint.Id).ConfigureAwait(false);
                await session.DetachAsync().ConfigureAwait(false);
                await Task.Delay(1200).ConfigureAwait(false);
                await session.AttachAsync(port).ConfigureAwait(false);
                breakpoint = await session.SetBreakpointAsync("LocalDebug.Demo.CounterMath", "ComputeNext", 13).ConfigureAwait(false);
                await ContinueToBreakpoint().ConfigureAwait(false);
                int after = IntLocal("input");
                Require(after > before, "The target did not make progress after detachment.");
                report.Metadata["InputBeforeDetach"] = before.ToString(CultureInfo.InvariantCulture);
                report.Metadata["InputAfterReattach"] = after.ToString(CultureInfo.InvariantCulture);
                await session.RemoveBreakpointAsync(breakpoint.Id).ConfigureAwait(false);
                await session.DetachAsync().ConfigureAwait(false);
            });
            await Check(7, async () =>
            {
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var accept = listener.AcceptTcpClientAsync();
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                var attach = session.AttachAsync(((IPEndPoint)listener.LocalEndpoint).Port, cancellation.Token);
                using var server = await accept.ConfigureAwait(false);
                bool failed = false;
                try { await attach.ConfigureAwait(false); }
                catch { failed = true; }
                Require(failed && session.Snapshot.State == SessionState.Disconnected, "Canceled handshake did not release the client.");
            });
        }
        catch (Exception error) { report.Error = error.ToString(); }
        finally
        {
            try { await session.DetachAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
            catch (Exception error) { report.CleanupError = error.Message; report.Error ??= "Session cleanup failed: " + error.Message; }
            report.Logs.AddRange(messages);
            report.CompletedUtc = DateTimeOffset.UtcNow;
        }
        return report;

        async Task Check(int index, Func<Task> operation)
        {
            var watch = Stopwatch.StartNew();
            report.Checks[index] = new(names[index], "Running");
            try
            {
                await operation().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                report.Checks[index] = new(names[index], "Passed", watch.ElapsedMilliseconds);
                log?.Invoke("PASS: " + names[index]);
            }
            catch (Exception error)
            {
                report.Checks[index] = new(names[index], "Failed", watch.ElapsedMilliseconds, error.Message);
                throw;
            }
        }
        int IntLocal(string name)
        {
            var value = session.Snapshot.Variables.FirstOrDefault(v => v.Name == name)?.Value;
            return int.TryParse(value, CultureInfo.InvariantCulture, out int number) ? number
                : throw new Exception($"Local '{name}' was not an inspectable integer: {value ?? "missing"}");
        }
        async Task ContinueToBreakpoint()
        {
            var previous = session.Snapshot;
            await session.ContinueAsync().ConfigureAwait(false);
            await Stop(previous, "Breakpoint").ConfigureAwait(false);
        }
        async Task Step(StepKind kind)
        {
            var previous = session.Snapshot;
            await session.StepAsync(kind).ConfigureAwait(false);
            await Stop(previous, "Step").ConfigureAwait(false);
        }
        async Task Stop(SessionSnapshot previous, string reason)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (ReferenceEquals(previous, session.Snapshot) || session.Snapshot.State != SessionState.Paused
                   || session.Snapshot.Status != "Stopped: " + reason)
            {
                if (session.Snapshot.State == SessionState.Disconnected) throw new Exception(session.Snapshot.Status);
                await Task.Delay(25, timeout.Token).ConfigureAwait(false);
            }
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
