# CI with NUKE

NUKE owns the dependency graph in `_build/Build.cs`. Any CI agent can invoke the same checked-in entry points used locally. No GitHub Actions runner, hosted CI account, repository API, or publishing credential is required.

## Agent prerequisites

- Linux x64 for the full CI run and its Linux Mono test fixture.
- The .NET SDK pinned in `global.json`, with the `android` workload installed.
- JDK 21, selected through `JAVA_HOME` or `--java-sdk-directory`.
- Android platform 36, build-tools 36.0.0, and platform-tools, selected through `ANDROID_HOME`, `ANDROID_SDK_ROOT`, or `--android-sdk-directory`.
- Python 3.11 or later (`python3` by default; override with `--python`).

Provision those tools in the agent image or before invoking NUKE. The supported workload command is `dotnet workload install android --skip-manifest-update`. On Linux, `python3 scripts/install-android-sdk.py` installs the Android components into `ANDROID_HOME` from Google's repository with archive checksums. Set that variable to the agent's SDK directory before using the helper.

Package restore requires NuGet.org access unless dependencies are cached. NUKE and its dependencies use `_build/packages.lock.json`; application and test restores use their existing checked lockfiles. Signature and TLS verification stay enabled. No global NUKE tool is required.

## Entry points and targets

```bash
./build.sh --target CI
```

`CI` is also the default when no target is supplied. The process returns nonzero if a required command or check fails. The CI system can capture stdout/stderr and collect the files below without interpreting build steps itself.

| Target | Dependencies and work |
| --- | --- |
| `Clean` | Removes only the generated output directory. Runs before Restore when selected. |
| `Restore` | Restores both Android apps and all host-test projects in locked mode. |
| `Compile` | Restore → signed Debug APKs, host-test executables, and the Mono fixture. |
| `Test` | Compile → two headless UI checks and ten live Mono integration scenarios. Linux x64 required. |
| `Verify` | Compile → APK signatures, SDK levels, ARM64 payloads, instrumentation, and agent settings. |
| `Pack` | Test + Verify → source ZIP, ARM64 bundle, and SHA-256 checksums. |
| `CI` | Clean → Restore → Compile → Test + Verify → Pack. |
| `TestDevice` | Verify → explicit physical Fold 4 checks on payton-legion2. Excluded from CI. |

For example, `./build.sh --target Verify` restores, compiles, and verifies the APKs without running host tests or packaging. `./build.sh --help` lists the targets and parameters; `./build.sh --target CI --plan` displays the graph without executing it.

PowerShell agents use `./build.ps1` with the same arguments. Windows agents can compile and verify with the Android toolchain installed; use `--python python` if that is the Python executable name. The full `CI`, `Test`, and `Pack` targets require Linux x64 because the live fixture is a Linux Mono executable.

All Android builds are Debug/ARM64: the cooperating agent and instrumentation are Debug-only, and only ARM64 has checked runtime lockfiles and payload verification. Unsupported configuration/runtime selections fail explicitly.

## Outputs and isolation

The default generated directory is `artifacts/ci/`. Select another subdirectory of `artifacts/` with `--output-directory artifacts/my-agent`. The build rejects the artifacts root and paths outside it, so Clean cannot remove the checked-in release files.

Android and host restores use separate .NET SDK artifact roots within that directory. This keeps shared projects' `project.assets.json` files from being overwritten by a restore for the other runtime. Compile uses `--no-restore`; Test executes the already built test assemblies.

Collect these six files after CI succeeds:

```text
artifacts/ci/LocalDebug-android-arm64.apk
artifacts/ci/DebugTarget-android-arm64.apk
artifacts/ci/package-checks.json
artifacts/ci/local-debugger-source.zip
artifacts/ci/local-debugger-arm64.zip
artifacts/ci/SHA256SUMS
```

The remaining subdirectories contain intermediate builds and the host fixture. They are not deliverables. The source archive excludes generated output, NUKE temporary/local parameters, signing keys, and device-result logs. Published files directly under `artifacts/` remain the existing release snapshots; CI does not overwrite them or publish externally.

## Physical-device checks

On payton-legion2 with the authorized USB Fold 4 attached:

```powershell
./build.ps1 --target TestDevice --python python
```

Use `--adb-path` if ADB is not discoverable and `--powershell` if the PowerShell executable needs an explicit path. The existing script checks the host and phone identities, installs the generated APK pair, runs eight checks inside the debugger's Android process, and saves results beside the APKs. It does not forward the debugger port. See `DEVICE_TEST_HANDOFF.md` for the exact checks and access requirements.

Hosted or generic CI success does not establish that a phone test ran. Physical Fold 4 validation remains pending until TestDevice or the handoff runner executes on that hardware.
