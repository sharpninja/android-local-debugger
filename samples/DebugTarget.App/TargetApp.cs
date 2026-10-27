using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using LocalDebug.Demo;

namespace LocalDebug.Target;

public sealed class TargetApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IActivityApplicationLifetime activity)
            activity.MainViewFactory = () => new TargetView();
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            single.MainView = new TargetView();
        base.OnFrameworkInitializationCompleted();
    }
}

public sealed class TargetView : UserControl
{
    private CancellationTokenSource? _cancellation;
    private readonly TextBlock _counter = new() { Text = "Starting…", FontSize = 30, TextWrapping = TextWrapping.Wrap };

    public TargetView()
    {
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 20,
                Children =
                {
                    new TextBlock { Text = "Local Debug Target", FontSize = 26, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = "Avalonia 12 · .NET 10 · Mono", Opacity = 0.75 },
                    _counter,
                    new TextBlock
                    {
                        Text = "Open Local .NET Debugger in split screen with this app. Attach to port 56000. The target starts normally and accepts the debugger later.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = "Breakpoint type: LocalDebug.Demo.CounterMath\nMethod: ComputeNext\nLine: 13 (after the calculation)",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = "The Demo counter thread calls ComputeNext once a second. Pausing stops this managed process; the debugger runs in a separate process.",
                        TextWrapping = TextWrapping.Wrap, Opacity = 0.75
                    }
                }
            }
        };
        AttachedToVisualTree += (_, _) => StartWorker();
        DetachedFromVisualTree += (_, _) => _cancellation?.Cancel();
    }

    private void StartWorker()
    {
        _cancellation?.Cancel();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        new Thread(() =>
        {
            int input = 1;
            while (!token.IsCancellationRequested)
            {
                int result = CounterMath.ComputeNext(input);
                int current = input;
                Dispatcher.UIThread.Post(() => _counter.Text = $"Input {current}\nResult {result}");
                input++;
                if (token.WaitHandle.WaitOne(1000)) break;
            }
        }) { IsBackground = true, Name = "Demo counter" }.Start();
    }
}
