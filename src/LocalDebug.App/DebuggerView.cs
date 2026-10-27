using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LocalDebug.Core;

namespace LocalDebug.App;

public sealed class DebuggerView : UserControl
{
    private readonly DebuggerSession _session;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _port = new() { Text = "56000", PlaceholderText = "Local port" };
    private readonly TextBox _type = new() { Text = "LocalDebug.Demo.CounterMath", PlaceholderText = "Namespace.TypeName" };
    private readonly TextBox _method = new() { Text = "ComputeNext", PlaceholderText = "Method name" };
    private readonly TextBox _line = new() { PlaceholderText = "Source line (optional)" };
    private readonly Button _attach = new() { Content = "Attach" };
    private readonly Button _detach = new() { Content = "Detach" };
    private readonly Button _pause = new() { Content = "Pause" };
    private readonly Button _continue = new() { Content = "Continue" };
    private readonly Button _stepOver = new() { Content = "Over" };
    private readonly Button _stepInto = new() { Content = "Into" };
    private readonly Button _stepOut = new() { Content = "Out" };
    private readonly Button _addBreakpoint = new() { Content = "Set breakpoint" };
    private readonly Button _removeBreakpoint = new() { Content = "Remove selected" };
    private readonly ListBox _breakpoints = new() { MinHeight = 90, MaxHeight = 200 };
    private readonly ComboBox _threads = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ListBox _frames = new() { Height = 220 };
    private readonly ListBox _variables = new() { Height = 280 };
    private readonly ObservableCollection<string> _messages = [];
    private bool _busy;
    private bool _updating;

    public DebuggerView(DebuggerSession session)
    {
        _session = session;
        _attach.Click += async (_, _) => await Run(async () =>
        {
            if (!int.TryParse(_port.Text, out var port)) throw new ArgumentException("Enter a local TCP port between 1 and 65535.");
            await _session.AttachAsync(port);
        });
        _detach.Click += async (_, _) => await Run(_session.DetachAsync);
        _pause.Click += async (_, _) => await Run(_session.PauseAsync);
        _continue.Click += async (_, _) => await Run(_session.ContinueAsync);
        _stepOver.Click += async (_, _) => await Run(() => _session.StepAsync(StepKind.Over));
        _stepInto.Click += async (_, _) => await Run(() => _session.StepAsync(StepKind.Into));
        _stepOut.Click += async (_, _) => await Run(() => _session.StepAsync(StepKind.Out));
        _addBreakpoint.Click += async (_, _) => await Run(async () =>
        {
            int? line = null;
            if (!string.IsNullOrWhiteSpace(_line.Text))
            {
                if (!int.TryParse(_line.Text, out var number) || number < 1)
                    throw new ArgumentException("The source line must be a positive integer.");
                line = number;
            }
            await _session.SetBreakpointAsync(_type.Text ?? "", _method.Text ?? "", line);
        });
        _removeBreakpoint.Click += async (_, _) => await Run(async () =>
        {
            if (_breakpoints.SelectedItem is DebugBreakpoint breakpoint)
                await _session.RemoveBreakpointAsync(breakpoint.Id);
        });
        _breakpoints.SelectionChanged += (_, _) => UpdateButtons();
        _threads.SelectionChanged += async (_, _) =>
        {
            if (!_updating && _threads.SelectedItem is DebugThread thread && thread.Id != _session.Snapshot.SelectedThread)
                await Run(() => _session.SelectThreadAsync(thread.Id));
        };
        _frames.SelectionChanged += async (_, _) =>
        {
            if (!_updating && _frames.SelectedItem is DebugFrame frame && frame.Index != _session.Snapshot.SelectedFrame)
                await Run(() => _session.SelectFrameAsync(frame.Index));
        };

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Margin = new Thickness(16),
            Children =
            {
                new StackPanel
                {
                    Spacing = 4,
                    Margin = new Thickness(0, 0, 0, 8),
                    Children = { new TextBlock { Text = "Local .NET Debugger", FontSize = 20, FontWeight = FontWeight.SemiBold } }
                },
                AtRow(Card(new StackPanel
                {
                    Spacing = 10,
                    Children = { _status, Row(_pause, _continue, _stepOver, _stepInto, _stepOut) }
                }), 1),
                AtRow(new TabControl
                {
                    Items =
                    {
                        new TabItem { Header = "Breakpoints", Content = Scroll(new StackPanel
                        {
                            Spacing = 12,
                            Children =
                            {
                                Heading("Connect on this device"), Row(_port, _attach, _detach),
                                Hint("The target must already be running with a Mono debug agent listening on this local port."),
                                Heading("Add a managed breakpoint"), _type, _method, _line, _addBreakpoint,
                                Hint("Attach pauses the app. Set a breakpoint, then Continue. Leave the line empty to stop at the method's first executable line."),
                                _breakpoints, _removeBreakpoint,
                                Heading("Try the included target"),
                                Hint("Install and open Local Debug Target. Put both apps in Android split screen, attach to port 56000, and set the default ComputeNext breakpoint. Step over to see multiplier, adjustment, and result change.")
                            }
                        }) },
                        new TabItem { Header = "Inspect", Content = Scroll(new StackPanel
                        {
                            Spacing = 10,
                            Children = { Heading("Thread"), _threads, Heading("Call stack"), _frames,
                                Heading("Arguments and locals"), _variables,
                                Hint("Select a frame to inspect its values. Object references show their type; property getters and arbitrary expression evaluation are not executed.") }
                        }) },
                        new TabItem { Header = "Log", Content = new ListBox { ItemsSource = _messages } }
                    }
                }, 2)
            }
        };
        AttachedToVisualTree += (_, _) =>
        {
            _session.Changed += OnChanged;
            _session.Logged += OnLogged;
            Update(_session.Snapshot);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _session.Changed -= OnChanged;
            _session.Logged -= OnLogged;
        };
        Update(_session.Snapshot);
    }

    private async Task Run(Func<Task> operation)
    {
        if (_busy) return;
        _busy = true;
        UpdateButtons();
        try { await operation(); }
        catch (Exception error)
        {
            _status.Text = error.Message;
            AddLog($"Error: {error.Message}");
        }
        finally { _busy = false; UpdateButtons(); }
    }

    private void OnChanged(SessionSnapshot snapshot) => Dispatcher.UIThread.Post(() => Update(snapshot));
    private void OnLogged(string message) => Dispatcher.UIThread.Post(() => AddLog(message));

    private void Update(SessionSnapshot snapshot)
    {
        _updating = true;
        try
        {
            _status.Text = $"{snapshot.State} · {snapshot.Status}";
            _threads.ItemsSource = snapshot.Threads;
            _threads.SelectedItem = snapshot.Threads.FirstOrDefault(t => t.Id == snapshot.SelectedThread);
            _frames.ItemsSource = snapshot.Frames;
            _frames.SelectedIndex = snapshot.SelectedFrame;
            _variables.ItemsSource = snapshot.Variables;
            var selected = (_breakpoints.SelectedItem as DebugBreakpoint)?.Id;
            _breakpoints.ItemsSource = snapshot.Breakpoints;
            _breakpoints.SelectedItem = snapshot.Breakpoints.FirstOrDefault(b => b.Id == selected);
            UpdateButtons();
        }
        finally { _updating = false; }
    }

    private void UpdateButtons()
    {
        var state = _session.Snapshot.State;
        var paused = state == SessionState.Paused;
        var connected = state is SessionState.Paused or SessionState.Running;
        _attach.IsEnabled = !_busy && !connected && state != SessionState.Connecting;
        _port.IsEnabled = _attach.IsEnabled;
        _detach.IsEnabled = !_busy && connected;
        _pause.IsEnabled = !_busy && state == SessionState.Running;
        _continue.IsEnabled = !_busy && paused;
        _stepOver.IsEnabled = _stepInto.IsEnabled = _stepOut.IsEnabled = !_busy && paused && _session.Snapshot.Frames.Count > 0;
        _addBreakpoint.IsEnabled = !_busy && paused;
        _removeBreakpoint.IsEnabled = !_busy && paused && _breakpoints.SelectedItem is not null;
        _threads.IsEnabled = _frames.IsEnabled = !_busy && paused;
    }

    private void AddLog(string message)
    {
        _messages.Insert(0, message);
        while (_messages.Count > 150) _messages.RemoveAt(_messages.Count - 1);
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 18, FontWeight = FontWeight.SemiBold };
    private static TextBlock Hint(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
    private static ScrollViewer Scroll(Control content) => new() { Content = content, Margin = new Thickness(0, 12, 0, 0) };
    private static Border Card(Control child) => new()
    {
            Child = child, Padding = new Thickness(8), CornerRadius = new CornerRadius(12),
        BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.Parse("#394457")), Margin = new Thickness(0, 0, 0, 8)
    };
    private static WrapPanel Row(params Control[] controls)
    {
        var row = new WrapPanel();
        foreach (var control in controls)
        {
            control.Margin = new Thickness(0, 0, 8, 8);
            row.Children.Add(control);
        }
        return row;
    }
    private static T AtRow<T>(T control, int row) where T : Control { Grid.SetRow(control, row); return control; }
}
