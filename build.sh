#!/usr/bin/env bash
set -euo pipefail
export NUKE_TELEMETRY_OPTOUT=1
project_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
cd "$project_root"
if ! command -v dotnet >/dev/null && [ -x /workspace/.cloud-setup/dotnet/dotnet ]; then
  source scripts/env.sh
fi
dotnet restore _build/_build.csproj --locked-mode
exec dotnet run --project _build/_build.csproj --no-restore -- "$@"
