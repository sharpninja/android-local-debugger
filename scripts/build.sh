#!/usr/bin/env bash
set -euo pipefail
project_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
cd "$project_root"

if ! command -v dotnet >/dev/null && [ -x /workspace/.cloud-setup/dotnet/dotnet ]; then
  source scripts/env.sh
fi
rid=${1:-android-arm64}
case "$rid" in android-arm64|android-x64) ;; *) echo 'Use android-arm64 or android-x64.' >&2; exit 2 ;; esac
android_args=()
if [ -n "${ANDROID_HOME:-}" ]; then android_args+=("-p:AndroidSdkDirectory=$ANDROID_HOME"); fi
if [ -n "${JAVA_HOME:-}" ]; then android_args+=("-p:JavaSdkDirectory=$JAVA_HOME"); fi

mkdir -p artifacts
for project in src/LocalDebug.Android/LocalDebug.Android.csproj samples/DebugTarget.Android/DebugTarget.Android.csproj; do
  dotnet restore "$project" -r "$rid" --locked-mode
  dotnet publish "$project" -c Debug -r "$rid" --no-restore "${android_args[@]}"
done
cp "src/LocalDebug.Android/bin/Debug/net10.0-android36.0/$rid/publish/dev.localdebug.debugger-Signed.apk" "artifacts/LocalDebug-$rid.apk"
cp "samples/DebugTarget.Android/bin/Debug/net10.0-android36.0/$rid/publish/dev.localdebug.target-Signed.apk" "artifacts/DebugTarget-$rid.apk"
echo "Signed APKs: $project_root/artifacts/LocalDebug-$rid.apk and DebugTarget-$rid.apk"
