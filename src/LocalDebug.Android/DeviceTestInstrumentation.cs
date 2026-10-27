#if DEBUG
using Android.App;
using Android.OS;
using Android.Util;
using LocalDebug.DeviceChecks;
using System.Runtime.InteropServices;
using System.Text.Json;
using Environment = System.Environment;

namespace LocalDebug.Android;

// The test runs in the debugger APK's process. The separate demo activity can
// stay foregrounded; no ADB forwarding carries debugger traffic to the PC.
[Instrumentation(Name = "dev.localdebug.debugger.DeviceTests", TargetPackage = "dev.localdebug.debugger", FunctionalTest = true)]
public sealed class DeviceTestInstrumentation : Instrumentation
{
    private int _port = 56000;

    public override void OnCreate(Bundle? arguments)
    {
        base.OnCreate(arguments);
        if (int.TryParse(arguments?.GetString("port"), out int port)) _port = port;
        Start();
    }

    public override void OnStart()
    {
        base.OnStart();
        AttachCheckReport report;
        try
        {
            report = LocalAttachChecks.RunAsync(_port, message => Log.Info("LocalDebug.DeviceTests", message))
                .GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            report = new AttachCheckReport { Error = error.ToString(), CompletedUtc = DateTimeOffset.UtcNow };
        }
        report.Metadata["Manufacturer"] = Build.Manufacturer ?? "unknown";
        report.Metadata["Model"] = Build.Model ?? "unknown";
        report.Metadata["AndroidRelease"] = Build.VERSION.Release ?? "unknown";
        report.Metadata["AndroidApi"] = ((int)Build.VERSION.SdkInt).ToString();
        report.Metadata["ClientFramework"] = RuntimeInformation.FrameworkDescription;
        report.Metadata["ClientEnvironmentVersion"] = Environment.Version.ToString();
        report.Metadata["ClientArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString();
        report.Metadata["ClientProcessId"] = Environment.ProcessId.ToString();
        var json = JsonSerializer.Serialize(report);
        var result = new Bundle();
        result.PutString("localdebug.result", json);
        try
        {
            var directory = TargetContext?.FilesDir?.AbsolutePath;
            if (directory is not null) File.WriteAllText(Path.Combine(directory, "device-test-result.json"), json);
        }
        catch (Exception error) { Log.Warn("LocalDebug.DeviceTests", "Could not retain report: " + error.Message); }
        Finish(report.Passed ? Result.Ok : Result.Canceled, result);
    }
}
#endif
