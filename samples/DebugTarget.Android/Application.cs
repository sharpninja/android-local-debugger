using Android.App;
using Android.Runtime;
using Android.Content.PM;
using Avalonia.Android;
using LocalDebug.Target;

[assembly: UsesPermission(Android.Manifest.Permission.Internet)]

namespace LocalDebug.Target.Android;

[Application]
public sealed class TargetApplication(nint handle, JniHandleOwnership ownership)
    : AvaloniaAndroidApplication<TargetApp>(handle, ownership);

[Activity(Name = "dev.localdebug.target.MainActivity", Label = "Local Debug Target",
    Theme = "@android:style/Theme.Material.NoActionBar", MainLauncher = true, Exported = true,
    ResizeableActivity = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity;
