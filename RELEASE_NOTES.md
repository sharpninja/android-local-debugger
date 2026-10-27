# v0.1.0-alpha.1

Initial Android proof of concept for debugging a cooperating .NET 10 Mono app on the same unrooted phone. The debugger and demo target use Avalonia 12.1.4 and communicate over IPv4 loopback.

Project license: GNU Affero General Public License, version 3. Included third-party components retain their own licenses; see `LICENSE` and `THIRD_PARTY_NOTICES.txt`.

Includes attachment and detachment, pause and continue, source breakpoints, stepping, thread and stack inspection, and argument/local values. The target must be rebuilt with its Mono debugger agent enabled; see the README.

Download `local-debugger-arm64.zip` for both signed development APKs, usage instructions, package hashes, third-party notices, and the `Test-Fold4.ps1` local device-test runner. Individual APKs and a source bundle are also provided. `SHA256SUMS` contains the deliverable checksums.

Validation completed: both ARM64 Android builds and APK signatures, two headless UI checks, and ten live Linux Mono integration scenarios, including the shared eight-check device-test runner. **Physical Android testing and .NET 10 on-device attachment remain unverified.** The requested payton-legion2 / USB Samsung Fold 4 test is pending; see `DEVICE_TEST_HANDOFF.md` and `CHECKS.md`.

These are Debug builds for development. Keep the unauthenticated target agent on loopback. This proof of concept does not support arbitrary PID attachment, CoreCLR/NativeAOT, expression evaluation, or value editing.
