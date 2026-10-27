#!/usr/bin/env bash
set -euo pipefail
project_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
cd "$project_root"
if ! command -v dotnet >/dev/null && [ -x /workspace/.cloud-setup/dotnet/dotnet ]; then
  source scripts/env.sh
fi
dotnet restore tests/LocalDebug.UiSmoke/LocalDebug.UiSmoke.csproj --locked-mode
dotnet run --project tests/LocalDebug.UiSmoke/LocalDebug.UiSmoke.csproj --no-restore
dotnet restore tests/MonoDebuggee/MonoDebuggee.csproj --locked-mode
dotnet publish tests/MonoDebuggee/MonoDebuggee.csproj -c Debug --no-restore -o artifacts/mono-debuggee
dotnet restore tests/LocalDebug.Integration/LocalDebug.Integration.csproj --locked-mode
dotnet run --project tests/LocalDebug.Integration/LocalDebug.Integration.csproj --no-restore -- artifacts/mono-debuggee/MonoDebuggee
