# Local test handoff: payton-legion2 and Samsung Fold 4

The user has authorized testing this debugger on `payton-legion2`, with a Samsung Fold 4 connected to that PC by USB. The original task is running in a cloud workspace. It has no exposed desktop task-dispatch/terminal-execution capability, so the physical test has not run yet.

## Prompt for a local Codex task

> Run this task locally on payton-legion2, using its USB-connected Samsung Fold 4. Use the downloaded Local .NET Debugger ARM64 bundle and its Test-Fold4.ps1 script. Verify the bundle's APK hashes, select the authorized physical SM-F936* device, install the two development APKs, run their Android instrumentation tests, and inspect the resulting host-report.json and device-checks.json. The instrumentation runs the debugger engine in the debugger APK's own .NET 10 process and attaches over the phone's loopback interface to the separate demo process. ADB is only for deployment and orchestration; do not forward or reverse the debugger port. Verify all eight device checks ran, the runtime versions, separate process IDs, target survival, and both UI launches. Preserve the logs and report any failing or unrun checks accurately. Use the existing bundle folder; no Git worktree is needed. Do not substitute Linux Mono or headless tests for physical-phone results. Do not uninstall/clear unrelated applications or change phone security settings. If USB debugging authorization is missing, report the phone's authorization prompt as the blocker.

## Files

Download and extract `local-debugger-arm64.zip` from the original cloud task. The refreshed bundle includes:

- `LocalDebug-android-arm64.apk`, including a Debug-only instrumentation entry point.
- `DebugTarget-android-arm64.apk`.
- `package-checks.json` with the expected SHA-256 hashes.
- `Test-Fold4.ps1`.
- Usage, validation, and third-party notices.

The cloud path `/workspace/android-local-debugger` is not a filesystem path on the PC. The bundle must be downloaded to that PC before running the local task.

## Run on the PC

From the extracted bundle folder, using its existing ADB installation:

```powershell
.\Test-Fold4.ps1
```

If ADB needs an explicit path:

```powershell
.\Test-Fold4.ps1 -AdbPath 'C:\path\to\platform-tools\adb.exe'
```

The script checks the PC hostname, selects only a Samsung Fold 4 model `SM-F936*` on a non-network ADB transport, checks the APK hashes, installs with `adb install -r`, starts the target, and invokes:

```text
adb -s SERIAL shell am instrument -w -r -e port 56000 dev.localdebug.debugger/dev.localdebug.debugger.DeviceTests
```

Only the two `dev.localdebug.*` packages are restarted. Results go to `device-results/<UTC run id>/` beside the APKs. Success requires eight passed checks, no failed/unrun checks, a .NET 10 client and target runtime, separate debugger/target processes, unchanged target PID across the test, and successful launches of both activities. Activity launch is a smoke test; it does not establish full touch interaction or folded/unfolded rendering correctness.

If the local task runs in WSL, use the Windows ADB installation that can see the USB phone and run the PowerShell script from a Windows-accessible folder. Native Linux with an existing ADB connection can run the same instrumentation command directly and save its `localdebug.result` JSON; it must apply the same identity and result checks.

The phone should already have USB debugging enabled and this PC authorized. The script does not root the phone, bypass its lock screen, or alter USB authorization.

## Current evidence

The updated APKs compile, their signatures and instrumentation registration have been checked, the PowerShell script parses, and its wrong-host guard was exercised. The shared eight-check runner passes against an actual Linux Mono fixture. None of those observations are a physical Fold 4 test result.
