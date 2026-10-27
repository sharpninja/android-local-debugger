# Validation record

## Built artifacts

`bash scripts/build.sh` completed successfully with frozen, architecture-specific NuGet lockfiles. Both apps use .NET SDK 10.0.403, the .NET 10 Mono Android runtime, Avalonia 12.1.4, Android SDK platform 36, build-tools 36.0.0, and JDK 21.

- `artifacts/LocalDebug-android-arm64.apk`
- `artifacts/DebugTarget-android-arm64.apk`

The packages target Android API 36, require API 23 or later, and contain ARM64 native libraries. These are signed Debug builds for development use.

## Passed checks

- **2 headless Avalonia checks:** layout retains usable inspection space and the thread-selection UI renders at 360×420 and 720×380.
- **10 live Mono integration scenarios:** deferred attachment; actual process suspension; thread enumeration; repeated-attach protection; source breakpoint installation, deduplication, and invalid-location handling; exact breakpoint hit at line 13; argument/local values `input=21`, `multiplier=2`, `adjustment=7`, `result=49`; frame/thread selection; step over; step into `Multiply` and out to `ComputeNext`; breakpoint removal; continue/manual pause; detach with independently observed target progress; reconnect; target-exit detection; cancellation of a handshake to a non-debugger endpoint; and the shared eight-check phone runner against a separate, incrementing Mono fixture. These behaviors are grouped into ten scenarios by the host runner.
- **APK verification:** Android `apksigner` verifies both packages, including APK Signature Schemes v2 and v3. Their compiled manifests declare Internet socket permission, debuggability, the expected package IDs and SDK levels, and resizable activities.
- **Payload inspection:** both APK ZIPs pass CRC validation and contain the Mono debugger component. Only the demo target includes the enabled `127.0.0.1:56000` debugger-agent configuration. Both packaged native Mono hosts recognize the `debug.mono.runtime_args` setting.
- **Device-test preparation:** the Debug-only `dev.localdebug.debugger.DeviceTests` instrumentation compiles and is registered in the debugger APK. `Test-Fold4.ps1` passes PowerShell parsing; its wrong-host guard was exercised without accessing ADB. The requested physical run on payton-legion2 and the USB-connected Fold 4 is **NotRun**, because this cloud session has no desktop task-dispatch/terminal-execution tool or configured SSH route. The local handoff is provided in `DEVICE_TEST_HANDOFF.md`.
- **Reproducible commands:** both documented build/test scripts have been executed. Separate lockfiles prevent Android runtime-identifier restores from replacing the host-test dependency locks.
- **Dependency integrity:** the .NET SDK's official SHA-512 and JDK release SHA-256 matched; Android SDK archive checksums matched Google's HTTPS repository metadata. Android workload NuGet archives were separately signature-verified after the SDK installer skipped its own checks. Installed Android SDK pack files were compared with their signed packages.

`artifacts/package-checks.json` records the APK hashes and package attributes. `artifacts/SHA256SUMS` records the deliverable hashes.

## Scope and limitations

The live protocol fixture uses **.NET 8 Mono 8.0.31, Linux x64, protocol 2.66**. Both Android applications are compiled for **.NET 10**. These are distinct validation results.

No physical Android device or Android emulator was used. Android installation, real-device rendering/lifecycle behavior, and same-device .NET 10 attachment remain unverified. The included target and the README provide the device validation procedure.

NuGet returned online certificate-revocation lookup warnings under this environment's restricted networking. Signature verification and TLS verification were kept enabled; repository trust is configured from NuGet.org's signing metadata. Revocation freshness was not established. The compiler also reports legacy API/unused-variable warnings in the vendored debugger library. The app builds and checks completed without errors.

The debugger is a proof of concept. It requires a cooperating Mono Debug target and does not implement arbitrary PID attachment, CoreCLR/NativeAOT debugging, expression evaluation, value editing, async state-machine reconstruction, or a source editor. The agent is unauthenticated; keep it on loopback and in development builds.

The original `/workspace/private` checkout remains unchanged.
