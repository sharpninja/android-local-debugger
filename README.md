# Local .NET Debugger

An Android proof of concept for debugging a cooperating .NET application on the same unrooted device. The debugger and demo target use Avalonia 12.1.4 and .NET 10. The client connects only to IPv4 loopback; it does not need a desktop, ADB, a VPN, or an Internet connection during a debugging session.

This implements managed debugging through Mono's soft-debugger protocol. It attaches to an agent endpoint, not an arbitrary Android PID. The target must be rebuilt with its debug agent enabled.

## Install and try the demo

Download packaged builds from this repository's [GitHub releases](https://github.com/sharpninja/android-local-debugger/releases) or the tracked [artifacts directory](artifacts/). The ARM64 bundle includes both APKs and the local Fold 4 test runner. Source, dependency lockfiles, documentation, scripts, and checked deliverables are stored in Git.

The built ARM64 APKs are in `artifacts/`:

- `LocalDebug-android-arm64.apk`: debugger, package `dev.localdebug.debugger`.
- `DebugTarget-android-arm64.apk`: demo target, package `dev.localdebug.target`.

1. Copy both APKs to the phone, allow installation from your file manager when Android requests it, and install both.
2. Open **Local Debug Target**. Its counter should start without a debugger attached.
3. Open **Local .NET Debugger** beside it using Android split screen. Keeping both visible avoids background-process freezing. Set up split screen before attaching.
4. In **Breakpoints**, leave the port at `56000` and press **Attach**. Attachment pauses the target; the debugger remains responsive in its own process.
5. Keep type `LocalDebug.Demo.CounterMath` and method `ComputeNext`. Enter source line `13`, press **Set breakpoint**, then **Continue**.
6. When the breakpoint hits, open **Inspect**. The selected frame shows `input`, `multiplier`, `adjustment`, and `result`. The expected result is `input * 2 + 7`.
7. **Over**, **Into**, and **Out** step the selected thread. For an obvious step-into example, remove the line-13 breakpoint, set one at line `12`, continue to the next hit, then press **Into** to enter `Multiply`.
8. **Detach**, in the Breakpoints tab, clears the session and resumes the target.

The APKs require ARM64 Android 6/API 23 or later. Android split screen requires Android 7 or later and device support. While the target is paused, avoid interacting with its window; Android can show an application-not-responding prompt during prolonged managed pauses. Resume promptly if it does.

## Connect to your Avalonia app

Apply this to the Android executable project, rather than just its shared UI library. Use your normal Debug build with matching source and portable PDBs.

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <UseMonoRuntime>true</UseMonoRuntime>
  <PublishAot>false</PublishAot>
  <RunAOTCompilation>false</RunAOTCompilation>
  <PublishTrimmed>false</PublishTrimmed>
  <Optimize>false</Optimize>
  <DebugSymbols>true</DebugSymbols>
  <DebugType>portable</DebugType>
  <EmbedAssembliesIntoApk>true</EmbedAssembliesIntoApk>
</PropertyGroup>
<ItemGroup Condition="'$(Configuration)' == 'Debug'">
  <AndroidEnvironment Include="debugger.env" />
</ItemGroup>
```

Create `debugger.env` next to that project:

```text
debug.mono.debug=1
debug.mono.runtime_args=--debugger-agent=transport=dt_socket,address=127.0.0.1:56000,server=y,suspend=n,embedding=1
```

Ensure the Android manifest declares the normal socket permission:

```xml
<uses-permission android:name="android.permission.INTERNET" />
```

Rebuild and install the app, open it, then attach from the debugger. Change the breakpoint type and method to a fully qualified type and method in your app. The optional line must be an executable source line within that method. Types need to have loaded before a breakpoint can be set. For overloaded methods, specify a line that selects one overload.

Stop the demo target first: only one target can listen on port 56000. Alternatively, change the port in your environment file and in the debugger. If an earlier desktop debugging session left global `debug.mono.*` properties set, restarting the phone clears those transient properties.

`server=y,suspend=n` allows the app to launch before the debugger connects. Enable this agent only in development builds. Loopback prevents remote network access but does not authenticate other applications on the same device.

## Implemented scope

- Attach, pause, continue, detach, and reconnect.
- Method or exact source-line breakpoints, deduplication, and removal.
- Source-level step over, into, and out.
- Managed thread selection, call stacks, arguments, and visible locals.
- Primitive values, strings, bounded array previews, and struct summaries.
- Connection loss and bounded/cancelable initial handshake handling.

Object references show their type. Property evaluation, editing values, conditional breakpoints, a source editor, async state-machine reconstruction, native debugging, CoreCLR, and NativeAOT targets are outside this proof of concept. Logs are kept in memory in the debugger process.

## Build

Prerequisites: the .NET SDK pinned in `global.json`, the `android` workload, JDK 21, Android platform 36, build-tools 36.0.0, platform-tools, and Python 3.11 or later. Full host testing requires Linux x64.

```bash
dotnet workload install android --skip-manifest-update
./build.sh --target CI
```

NUKE 10.1.0 coordinates the build through `_build/Build.cs`. The bootstrap restores NUKE using its checked lockfile; no global NUKE installation is needed. `CI` runs Clean, Restore, Compile, Test, Verify, and Pack. It produces both signed Debug APKs, package checks, source and APK bundles, and checksums under `artifacts/ci/`. Existing published files directly under `artifacts/` are preserved. Debug signing keys remain local and are not included in the source archive.

Individual targets are available with `./build.sh --target Restore`, `Compile`, `Test`, `Verify`, or `Pack`. PowerShell uses `./build.ps1` with the same arguments. `TestDevice` is an explicit local hardware target. See [CI.md](CI.md) for the target graph, agent prerequisites, parameters, and output collection. The build is independent of any hosted CI service.

In this managed cloud workspace:

```bash
cd /workspace/android-local-debugger
source scripts/env.sh
./build.sh --target CI
```

`scripts/install-android-sdk.py` is a Linux setup helper. It downloads SDK components from Google's official HTTPS repository, verifies the checksums from Google's repository metadata, and records SDK license acceptance. It preserves already installed components. It avoids the Xamarin SDK installer's certificate-pinning conflict with the cloud HTTPS proxy. TLS verification remains enabled.

NuGet signature validation is required in `NuGet.config`, using NuGet.org's published repository certificates. To refresh those certificates through NuGet's supported mechanism:

```bash
dotnet nuget trust sync nuget.org --configfile NuGet.config
```

## Validation

For the requested `payton-legion2` + USB Samsung Fold 4 test, see [the local test handoff](DEVICE_TEST_HANDOFF.md). The refreshed ARM64 bundle includes `Test-Fold4.ps1`; run it locally on that PC. It checks the hardware identity, installs the APK pair, runs eight checks inside the debugger APK on the phone, verifies .NET 10 runtime/process evidence, and saves structured results. This cloud task cannot dispatch a local desktop task with its currently exposed tools; the physical run is still pending.

```bash
./build.sh --target Test
```

The Test target is for Linux x64. It checks the Avalonia layout and navigation headlessly at 360×420 and 720×380, then runs ten integration scenarios against actual, separately running .NET 8 Mono processes. One scenario runs the same eight checks used by the phone instrumentation, using a second fixture with an increasing counter. This older Linux runtime is a protocol test fixture; both Android apps target .NET 10. The older `scripts/build.sh` and `scripts/test.sh` entry points delegate to NUKE.

The integration suite verifies that the target runs before attachment, really pauses, hits a source breakpoint, exposes four expected argument/local values, steps over/into/out, changes stack frames, removes breakpoints, resumes on detach, reconnects, detects process exit, and cancels a non-debugger handshake.

Both ARM64 Android builds and their APK signatures have been checked. **No physical Android device was attached to this cloud machine, so Android installation, UI interaction, and .NET 10 on-device attachment remain unverified.** See `CHECKS.md` for the recorded results and limits.

## Source layout

- `src/LocalDebug.Core`: session lifecycle, debugger operations, snapshots, and value formatting.
- `src/LocalDebug.App`: shared Avalonia debugger UI.
- `src/LocalDebug.Android`: Android debugger host.
- `samples/DebugTarget.*` and `samples/Shared`: cooperating Avalonia target and debug-friendly calculation.
- `third_party/Mono.Debugger.Soft`: pinned debugger library source, license, provenance, and compatibility changes.
- `tests/`: headless layout checks and live Mono integration harness.
- `tests/LocalDebug.DeviceChecks` and the Debug-only Android instrumentation: reusable local-attach checks and their phone entry point.

## License

This project is licensed under the GNU Affero General Public License, version 3; see [LICENSE](LICENSE). Vendored code and dependencies retain their own licenses, recorded in [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt) and the vendored source.

The existing `/workspace/private` repository was not changed.
