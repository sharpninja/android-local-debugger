using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using LocalDebug.Core;

namespace LocalDebug.App;

public sealed class DebuggerApp : Application
{
    private readonly DebuggerSession _session = new();

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IActivityApplicationLifetime activity)
            activity.MainViewFactory = () => new DebuggerView(_session);
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            single.MainView = new DebuggerView(_session);
        base.OnFrameworkInitializationCompleted();
    }
}
