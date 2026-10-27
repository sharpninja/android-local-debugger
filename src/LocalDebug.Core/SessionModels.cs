namespace LocalDebug.Core;

public enum SessionState { Disconnected, Connecting, Running, Paused, Faulted }
public enum StepKind { Into, Over, Out }

public sealed record DebugThread(long Id, string Name)
{
    public override string ToString() => $"{Name} · thread {Id}";
}

public sealed record DebugFrame(int Index, string Method, string? File, int Line)
{
    public override string ToString() => $"#{Index} {Method}\n{Path.GetFileName(File)}:{Line}";
}

public sealed record DebugVariable(string Name, string Type, string Value)
{
    public override string ToString() => $"{Name} = {Value}\n{Type}";
}

public sealed record DebugBreakpoint(Guid Id, string Method, string? File, int Line)
{
    public override string ToString() => $"{Method}\n{Path.GetFileName(File)}:{Line}";
}

public sealed record SessionSnapshot(
    SessionState State,
    string Status,
    IReadOnlyList<DebugThread> Threads,
    long? SelectedThread,
    IReadOnlyList<DebugFrame> Frames,
    int SelectedFrame,
    IReadOnlyList<DebugVariable> Variables,
    IReadOnlyList<DebugBreakpoint> Breakpoints)
{
    public static SessionSnapshot Empty { get; } = new(
        SessionState.Disconnected, "Ready to attach", [], null, [], -1, [], []);
}
