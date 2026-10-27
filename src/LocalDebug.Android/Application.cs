using Android.App;
using Android.Runtime;
using Android.Content.PM;
using Avalonia.Android;
using LocalDebug.App;

[assembly: UsesPermission(Android.Manifest.Permission.Internet)]

namespace LocalDebug.Android;

[Application]
public sealed class DebuggerApplication(nint handle, JniHandleOwnership ownership)
    : AvaloniaAndroidApplication<DebuggerApp>(handle, ownership);

[Activity(Name = "dev.localdebug.debugger.MainActivity", Label = "Local .NET Debugger",
    Theme = "@android:style/Theme.Material.NoActionBar", MainLauncher = true, Exported = true,
    ResizeableActivity = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity;
