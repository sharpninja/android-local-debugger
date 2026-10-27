$ErrorActionPreference = 'Stop'
$env:NUKE_TELEMETRY_OPTOUT = '1'
Push-Location $PSScriptRoot
try {
    & dotnet restore _build/_build.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & dotnet run --project _build/_build.csproj --no-restore -- @args
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
