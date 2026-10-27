using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LocalDebug.App;
using LocalDebug.Core;

AppBuilder.Configure<DebuggerApp>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
    .SetupWithoutStarting();

foreach (var viewport in new[] { (Width: 360, Height: 420), (Width: 720, Height: 380) })
{
    var session = new DebuggerSession();
    var view = new DebuggerView(session);
    var window = new Window { Width = viewport.Width, Height = viewport.Height, Content = view };
    window.Show();
    Dispatcher.UIThread.RunJobs();
    var tabs = view.GetVisualDescendants().OfType<TabControl>().Single();
    if (tabs.Bounds.Height < 150)
        throw new Exception($"Inspection tabs have insufficient room at {viewport}: {tabs.Bounds}");
    tabs.SelectedIndex = 1;
    Dispatcher.UIThread.RunJobs();
    if (!tabs.GetVisualDescendants().OfType<ComboBox>().Any())
        throw new Exception("The thread selector did not render in the inspection tab.");
    window.Close();
    session.DisposeAsync().AsTask().GetAwaiter().GetResult();
    Console.WriteLine($"PASS: Avalonia layout and inspection navigation at {viewport.Width}×{viewport.Height}.");
}
