using System.Runtime.InteropServices;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.CI);

    [Parameter("Build configuration. The debugger agent and device checks require Debug.")]
    readonly string Configuration = "Debug";

    [Parameter("Android runtime identifier. Checked dependencies currently support android-arm64.")]
    readonly string RuntimeIdentifier = "android-arm64";

    [Parameter("Generated output directory below artifacts/.")]
    readonly string OutputDirectory = "artifacts/ci";

    [Parameter("Android SDK directory. Defaults to ANDROID_HOME or ANDROID_SDK_ROOT.")]
    readonly AbsolutePath AndroidSdkDirectory;

    [Parameter("JDK directory. Defaults to JAVA_HOME.")]
    readonly AbsolutePath JavaSdkDirectory;

    [Parameter("Python 3.11+ executable used for package verification and archives.")]
    readonly string Python = "python3";

    [Parameter("PowerShell executable for the optional physical-device checks.")]
    readonly string Powershell;

    [Parameter("ADB executable for the optional physical-device checks.")]
    readonly string AdbPath;

    AbsolutePath Artifacts => RootDirectory / OutputDirectory;
    AbsolutePath AndroidBuild => Artifacts / "android";
    AbsolutePath HostBuild => Artifacts / "host";
    AbsolutePath UiOutput => Artifacts / "tests" / "ui";
    AbsolutePath IntegrationOutput => Artifacts / "tests" / "integration";
    AbsolutePath FixtureOutput => Artifacts / "mono-debuggee";
    AbsolutePath AndroidSdk => AndroidSdkDirectory ?? (AbsolutePath)(
        Environment.GetEnvironmentVariable("ANDROID_HOME") ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"));
    AbsolutePath JavaSdk => JavaSdkDirectory ?? (AbsolutePath)Environment.GetEnvironmentVariable("JAVA_HOME");

    AbsolutePath[] AndroidProjects =>
    [
        RootDirectory / "src/LocalDebug.Android/LocalDebug.Android.csproj",
        RootDirectory / "samples/DebugTarget.Android/DebugTarget.Android.csproj"
    ];

    AbsolutePath UiProject => RootDirectory / "tests/LocalDebug.UiSmoke/LocalDebug.UiSmoke.csproj";
    AbsolutePath IntegrationProject => RootDirectory / "tests/LocalDebug.Integration/LocalDebug.Integration.csproj";
    AbsolutePath FixtureProject => RootDirectory / "tests/MonoDebuggee/MonoDebuggee.csproj";

    protected override void OnBuildInitialized()
    {
        Assert.True(Configuration == "Debug", "Use Debug: the cooperating debug agent and instrumentation are Debug-only.");
        Assert.True(RuntimeIdentifier == "android-arm64", "Only android-arm64 has checked lockfiles and package verification.");
        var relative = Path.GetRelativePath(RootDirectory / "artifacts", Artifacts);
        Assert.True(relative != "." && relative != ".." && !Path.IsPathRooted(relative) &&
                    !relative.StartsWith(".." + Path.DirectorySeparatorChar),
            "OutputDirectory must be a subdirectory of artifacts/, preserving the checked-in releases.");
    }

    Target Clean => _ => _
        .Description("Remove only generated CI output; preserve the checked-in deliverables.")
        .Before(Restore)
        .Executes(() =>
        {
            if (Directory.Exists(Artifacts)) Directory.Delete(Artifacts, recursive: true);
            Directory.CreateDirectory(Artifacts);
        });

    Target Restore => _ => _
        .Description("Restore Android and host projects using their frozen NuGet lockfiles.")
        .Requires(() => AndroidSdk != null && Directory.Exists(AndroidSdk))
        .Requires(() => JavaSdk != null && Directory.Exists(JavaSdk))
        .Executes(() =>
        {
            foreach (var project in AndroidProjects)
                DotNetRestore(s => s.SetProjectFile(project)
                    .SetRuntime(RuntimeIdentifier).EnableLockedMode()
                    .SetProperty("UseArtifactsOutput", true).SetProperty("ArtifactsPath", AndroidBuild)
                    .SetProperty("AndroidSdkDirectory", AndroidSdk).SetProperty("JavaSdkDirectory", JavaSdk));

            foreach (var project in new[] { UiProject, IntegrationProject, FixtureProject })
                DotNetRestore(s => s.SetProjectFile(project).EnableLockedMode()
                    .SetProperty("UseArtifactsOutput", true).SetProperty("ArtifactsPath", HostBuild));
        });

    Target Compile => _ => _
        .Description("Build both signed Android APKs and the host test executables without restoring.")
        .DependsOn(Restore)
        .Executes(() =>
        {
            foreach (var project in AndroidProjects)
            {
                var name = Path.GetFileNameWithoutExtension(project);
                var publish = Artifacts / "publish" / name;
                DotNetPublish(s => s.SetProject(project).SetConfiguration(Configuration)
                    .SetRuntime(RuntimeIdentifier).EnableNoRestore().SetOutput(publish)
                    .SetProperty("UseArtifactsOutput", true).SetProperty("ArtifactsPath", AndroidBuild)
                    .SetProperty("AndroidSdkDirectory", AndroidSdk).SetProperty("JavaSdkDirectory", JavaSdk));
                var debugger = name == "LocalDebug.Android";
                var package = debugger ? "dev.localdebug.debugger" : "dev.localdebug.target";
                var filename = debugger ? "LocalDebug" : "DebugTarget";
                File.Copy(publish / $"{package}-Signed.apk", Artifacts / $"{filename}-{RuntimeIdentifier}.apk", overwrite: true);
            }

            BuildHost(UiProject, UiOutput);
            BuildHost(IntegrationProject, IntegrationOutput);
            DotNetPublish(s => s.SetProject(FixtureProject).SetConfiguration(Configuration)
                .EnableNoRestore().SetOutput(FixtureOutput)
                .SetProperty("UseArtifactsOutput", true).SetProperty("ArtifactsPath", HostBuild));
        });

    Target Test => _ => _
        .Description("Run the headless UI checks and live Mono integration scenarios (Linux x64).")
        .DependsOn(Compile)
        .Requires(() => OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        .Executes(() =>
        {
            Run(DotNetPath, UiOutput / "LocalDebug.UiSmoke.dll");
            Run(DotNetPath, IntegrationOutput / "LocalDebug.Integration.dll", FixtureOutput / "MonoDebuggee");
        });

    Target Verify => _ => _
        .Description("Verify APK signatures, manifests, runtime payloads, and debug-agent settings.")
        .DependsOn(Compile)
        .Executes(() => Run(Python, RootDirectory / "scripts/verify-apks.py",
            "--artifacts-directory", Artifacts, "--android-sdk", AndroidSdk));

    Target Pack => _ => _
        .Description("Package tested APKs, source, device runner, notices, and SHA-256 checksums.")
        .DependsOn(Test, Verify)
        .Executes(() => Run(Python, RootDirectory / "scripts/package-source.py", "--artifacts-directory", Artifacts));

    Target CI => _ => _
        .Description("Run Clean, Restore, Compile, Test, Verify, and Pack on any Linux x64 CI agent.")
        .DependsOn(Clean, Pack);

    Target TestDevice => _ => _
        .Description("Run the separate physical Fold 4 checks locally on payton-legion2.")
        .DependsOn(Verify)
        .Executes(() =>
        {
            var args = new List<string> { "-NoProfile", "-File", RootDirectory / "scripts/Test-Fold4.ps1",
                "-ArtifactDirectory", Artifacts };
            if (!string.IsNullOrEmpty(AdbPath)) args.AddRange(["-AdbPath", AdbPath]);
            Run(Powershell ?? (OperatingSystem.IsWindows() ? "powershell" : "pwsh"), args.ToArray());
        });

    void BuildHost(AbsolutePath project, AbsolutePath output) =>
        DotNetBuild(s => s.SetProjectFile(project).SetConfiguration(Configuration)
            .EnableNoRestore().SetOutputDirectory(output)
            .SetProperty("UseArtifactsOutput", true).SetProperty("ArtifactsPath", HostBuild));

    void Run(string executable, params string[] args)
    {
        var arguments = string.Join(" ", args.Select(arg => arg.DoubleQuoteIfNeeded()));
        ProcessTasks.StartProcess(ToolPathResolver.GetPathExecutable(executable), arguments,
            workingDirectory: RootDirectory).AssertZeroExitCode();
    }
}
