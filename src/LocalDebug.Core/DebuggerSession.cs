using System.Net;
using System.Net.Sockets;
using Mono.Debugger.Soft;
using SoftFrame = Mono.Debugger.Soft.StackFrame;

namespace LocalDebug.Core;

/// <summary>A loopback-only client for an app's cooperative Mono debug agent.</summary>
public sealed class DebuggerSession : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, (BreakpointEventRequest Request, DebugBreakpoint Info)> _breakpoints = [];
    private VirtualMachine? _vm;
    private Socket? _socket;
    private CancellationTokenSource? _eventsCancellation;
    private StepEventRequest? _step;
    private ThreadMirror[] _threads = [];
    private SoftFrame[] _frames = [];
    private long? _selectedThread;
    private int _selectedFrame = -1;
    private bool _manualSuspension;
    private int _eventSuspensions;
    private volatile SessionState _state = SessionState.Disconnected;
    private SessionSnapshot _snapshot = SessionSnapshot.Empty;

    public SessionSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public event Action<SessionSnapshot>? Changed;
    public event Action<string>? Logged;

    public async Task AttachAsync(int port, CancellationToken cancellationToken = default)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Socket? socket = null;
        VirtualMachine? connected = null;
        bool startedAttach = false;
        try
        {
            if (_vm is not null) throw new InvalidOperationException("Detach the current session first.");
            startedAttach = true;
            _state = SessionState.Connecting;
            Publish($"Connecting to 127.0.0.1:{port}");
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            { SendTimeout = 10000, ReceiveTimeout = 10000 };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var registration = timeout.Token.Register(socket.Dispose);
            await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), timeout.Token).ConfigureAwait(false);
            connected = await Task.Run(() => LocalTcpConnector.Connect(socket), timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            connected.SetSocketTimeouts(10000, 0, 1000);
            await Task.Run(() =>
            {
                connected.EnableEvents([EventType.UserBreak], SuspendPolicy.All);
                connected.Suspend();
            }, cancellationToken).ConfigureAwait(false);
            _vm = connected;
            _socket = socket;
            _eventsCancellation = new CancellationTokenSource();
            _manualSuspension = true;
            _eventSuspensions = 0;
            _state = SessionState.Paused;
            Log($"Attached: {connected.Version.VMVersion}; protocol {connected.Version.MajorVersion}.{connected.Version.MinorVersion}");
            await Task.Run(() => RefreshPaused("Attached and paused"), cancellationToken).ConfigureAwait(false);
            var vm = connected;
            var token = _eventsCancellation.Token;
            _ = Task.Run(() => PumpEventsAsync(vm, token), CancellationToken.None);
        }
        catch
        {
            if (!startedAttach) throw;
            // A failed attach must not leave a cooperating target suspended.
            try { connected?.Detach(); } catch { connected?.Disconnect(); }
            socket?.Dispose();
            _vm = null;
            _socket = null;
            _state = SessionState.Disconnected;
            ClearInspection();
            Publish("Attach failed. Check that the target is running with its Mono agent enabled.");
            throw;
        }
        finally { _gate.Release(); }
    }

    public Task PauseAsync() => ExecuteAsync(() =>
    {
        var vm = RequireVm();
        if (_state != SessionState.Running) throw new InvalidOperationException("The target is not running.");
        vm.Suspend();
        _manualSuspension = true;
        _state = SessionState.Paused;
        RefreshPaused("Paused");
    });

    public Task ContinueAsync() => ExecuteAsync(() =>
    {
        var vm = RequirePaused();
        DisableStep();
        ResumeTarget(vm);
        _state = SessionState.Running;
        ClearInspection();
        Publish("Running");
    });

    public Task StepAsync(StepKind kind) => ExecuteAsync(() =>
    {
        var vm = RequirePaused();
        var thread = _threads.FirstOrDefault(t => t.Id == _selectedThread)
            ?? throw new InvalidOperationException("Select a thread first.");
        DisableStep();
        _step = vm.CreateStepRequest(thread);
        _step.Depth = kind switch { StepKind.Into => StepDepth.Into, StepKind.Out => StepDepth.Out, _ => StepDepth.Over };
        _step.Size = StepSize.Line;
        _step.Filter = StepFilter.StaticCtor | StepFilter.DebuggerHidden | StepFilter.DebuggerStepThrough;
        _step.Enable();
        ResumeTarget(vm);
        _state = SessionState.Running;
        ClearInspection();
        Publish($"Stepping {kind.ToString().ToLowerInvariant()}");
    });

    public Task<DebugBreakpoint> SetBreakpointAsync(string typeName, string methodName, int? line = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        if (line is < 1) throw new ArgumentOutOfRangeException(nameof(line));
        return ExecuteAsync(() =>
        {
            var vm = RequirePaused();
            var types = vm.GetTypes(typeName.Trim(), false);
            if (types.Count == 0) throw new InvalidOperationException($"Type '{typeName}' is not loaded. Continue the app until that type is used, pause, and retry.");
            var methods = types.SelectMany(t => t.GetMethods()).Where(m => m.Name == methodName.Trim()).ToArray();
            if (methods.Length == 0) throw new InvalidOperationException($"Method '{methodName}' was not found on '{typeName}'.");
            var locations = methods.SelectMany(m => m.Locations)
                .Where(l => l.LineNumber is > 0 and < 0xFEEFEE && !string.IsNullOrEmpty(l.SourceFile)).ToArray();
            var candidates = line.HasValue ? locations.Where(l => l.LineNumber == line.Value).ToArray() : locations;
            if (candidates.Length == 0) throw new InvalidOperationException(line.HasValue
                ? $"No executable sequence point at line {line}. Available lines: {string.Join(", ", locations.Select(l => l.LineNumber).Distinct().Order())}"
                : "No source locations were found. Include portable PDBs in a Debug build.");
            var methodIds = candidates.Select(l => l.Method.GetId()).Distinct().ToArray();
            if (methodIds.Length > 1) throw new InvalidOperationException("The method has multiple overloads. Specify a source line to select one.");
            var location = candidates.OrderBy(l => l.ILOffset).First();
            var existing = _breakpoints.Values.FirstOrDefault(b => b.Info.Method == location.Method.FullName && b.Info.Line == location.LineNumber && b.Info.File == location.SourceFile);
            if (existing.Info is not null) return existing.Info;
            var request = vm.CreateBreakpointRequest(location);
            request.Enable();
            var info = new DebugBreakpoint(Guid.NewGuid(), location.Method.FullName, location.SourceFile, location.LineNumber);
            _breakpoints.Add(info.Id, (request, info));
            Log($"Breakpoint set: {info.Method} at {Path.GetFileName(info.File)}:{info.Line}");
            Publish(Snapshot.Status);
            return info;
        });
    }

    public Task RemoveBreakpointAsync(Guid id) => ExecuteAsync(() =>
    {
        RequirePaused();
        if (_breakpoints.Remove(id, out var breakpoint)) breakpoint.Request.Disable();
        Publish(Snapshot.Status);
    });

    public Task SelectThreadAsync(long id) => ExecuteAsync(() =>
    {
        RequirePaused();
        if (!_threads.Any(t => t.Id == id)) throw new ArgumentException("The thread no longer exists.", nameof(id));
        _selectedThread = id;
        _selectedFrame = 0;
        ReadFrames();
        Publish(Snapshot.Status);
    });

    public Task SelectFrameAsync(int index) => ExecuteAsync(() =>
    {
        RequirePaused();
        if (index < 0 || index >= _frames.Length) throw new ArgumentOutOfRangeException(nameof(index));
        _selectedFrame = index;
        Publish(Snapshot.Status);
    });

    public Task DetachAsync() => ExecuteAsync(() =>
    {
        _eventsCancellation?.Cancel();
        var vm = _vm;
        try { vm?.Detach(); }
        finally
        {
            try { vm?.Disconnect(); } catch { }
            _socket?.Dispose();
            _vm = null;
            _socket = null;
            _step = null;
            _breakpoints.Clear();
            ClearInspection();
            _state = SessionState.Disconnected;
            Publish("Detached; target continues running");
            Log("Detached");
        }
    });

    private async Task PumpEventsAsync(VirtualMachine vm, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var set = vm.GetNextEventSet(250);
                if (set is null) continue;
                await _gate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (!ReferenceEquals(_vm, vm)) return;
                    if (set.Events.Any(e => e.EventType is EventType.VMDeath or EventType.VMDisconnect))
                    {
                        LostConnection("Target exited or disconnected");
                        return;
                    }
                    var stop = set.Events.FirstOrDefault(e => e is BreakpointEvent or StepEvent || e.EventType == EventType.UserBreak);
                    if (stop is not null)
                    {
                        if (set.SuspendPolicy != SuspendPolicy.None) _eventSuspensions++;
                        DisableStep();
                        _state = SessionState.Paused;
                        _selectedThread = stop.Thread?.Id;
                        _selectedFrame = 0;
                        Log($"Stopped: {stop.EventType}");
                        RefreshPaused($"Stopped: {stop.EventType}");
                    }
                    // VMStart/keepalive notifications do not add a suspension in
                    // deferred-attach mode. Do not resume the user's manual pause.
                }
                finally { _gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_vm, vm)) LostConnection($"Connection lost: {error.Message}");
            }
            finally { _gate.Release(); }
        }
    }

    private void LostConnection(string message)
    {
        _eventsCancellation?.Cancel();
        try { _vm?.Disconnect(); } catch { }
        _socket?.Dispose();
        _socket = null;
        _vm = null;
        _step = null;
        _breakpoints.Clear();
        _state = SessionState.Disconnected;
        ClearInspection();
        Publish(message);
        Log(message);
    }

    private void RefreshPaused(string status)
    {
        _threads = RequireVm().GetThreads().ToArray();
        if (!_threads.Any(t => t.Id == _selectedThread))
            _selectedThread = _threads.FirstOrDefault(t => SafeName(t).Contains("Demo", StringComparison.OrdinalIgnoreCase))?.Id
                ?? _threads.FirstOrDefault()?.Id;
        ReadFrames();
        Publish(status);
    }

    private void ReadFrames()
    {
        var thread = _threads.FirstOrDefault(t => t.Id == _selectedThread);
        _frames = thread?.GetFrames() ?? [];
        _selectedFrame = _frames.Length == 0 ? -1 : Math.Clamp(_selectedFrame, 0, _frames.Length - 1);
    }

    private IReadOnlyList<DebugVariable> ReadVariables()
    {
        if (_selectedFrame < 0 || _selectedFrame >= _frames.Length) return [];
        var frame = _frames[_selectedFrame];
        var result = new List<DebugVariable>();
        try
        {
            foreach (var parameter in frame.Method.GetParameters())
                result.Add(ReadVariable(parameter.Name, () => parameter.ParameterType.FullName, () => frame.GetValue(parameter)));
            foreach (var variable in frame.GetVisibleVariables().Where(v => !v.IsArg))
                result.Add(ReadVariable(variable.Name, () => variable.Type.FullName, () => frame.GetValue(variable)));
        }
        catch (AbsentInformationException) { result.Add(new("Symbols", "", "Local symbols are not available for this frame.")); }
        return result;
    }

    private static DebugVariable ReadVariable(string name, Func<string> type, Func<Value> value)
    {
        try { return new(name, type(), ValueFormatter.Format(value())); }
        catch (Exception error) { return new(name, "", $"<unavailable: {error.Message}>"); }
    }

    private void Publish(string status)
    {
        var paused = _state == SessionState.Paused;
        var snapshot = new SessionSnapshot(_state, status,
            paused ? _threads.Select(t => new DebugThread(t.Id, SafeName(t))).ToArray() : [],
            paused ? _selectedThread : null,
            paused ? _frames.Select((f, i) => new DebugFrame(i, f.Method.FullName, f.FileName, f.LineNumber)).ToArray() : [],
            paused ? _selectedFrame : -1,
            paused ? ReadVariables() : [],
            _breakpoints.Values.Select(b => b.Info).ToArray());
        Volatile.Write(ref _snapshot, snapshot);
        Changed?.Invoke(snapshot);
    }

    private static string SafeName(ThreadMirror thread)
    {
        try { return string.IsNullOrWhiteSpace(thread.Name) ? "Managed thread" : thread.Name; }
        catch { return "Managed thread"; }
    }

    private void ClearInspection()
    {
        _threads = [];
        _frames = [];
        _selectedThread = null;
        _selectedFrame = -1;
    }

    private void ResumeTarget(VirtualMachine vm)
    {
        int count = (_manualSuspension ? 1 : 0) + _eventSuspensions;
        for (int i = 0; i < Math.Max(count, 1); i++) vm.Resume();
        _manualSuspension = false;
        _eventSuspensions = 0;
    }

    private void DisableStep()
    {
        _step?.Disable();
        _step = null;
    }

    private VirtualMachine RequireVm() => _vm ?? throw new InvalidOperationException("Attach to a target first.");
    private VirtualMachine RequirePaused() => _state == SessionState.Paused ? RequireVm()
        : throw new InvalidOperationException("Pause the target first.");
    private void Log(string message) => Logged?.Invoke($"{DateTimeOffset.Now:HH:mm:ss} {message}");

    private async Task ExecuteAsync(Action action)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await Task.Run(action).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task<T> ExecuteAsync<T>(Func<T> action)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return await Task.Run(action).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync() => await DetachAsync().ConfigureAwait(false);
}
