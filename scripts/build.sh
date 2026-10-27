#!/usr/bin/env bash
set -euo pipefail
project_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
rid=${1:-android-arm64}
exec bash "$project_root/build.sh" --target Compile --runtime-identifier "$rid"
