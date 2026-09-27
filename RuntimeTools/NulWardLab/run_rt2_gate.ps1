param(
    [string]$GameRoot = "D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster"
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$logPath = Join-Path $env:TEMP "ffx-hooks.log"

Write-Host "=== Nul Ward RT2 gate ==="

Push-Location $repo
try {
    & (Join-Path $repo "RuntimeTools\FfxHooksDll\deploy\nul-ward-lab\preflight.ps1") -GameRoot $GameRoot

    dotnet run -c Release --project FFXProjectEditor -- --nul-ward-atel-diff `
        --kernel "$GameRoot\data\mods\ffx_ps2\ffx\master\new_uspc\battle\kernel\command.bin"

    if (Test-Path $logPath) {
        dotnet run -c Release --project FFXProjectEditor -- --nul-ward-rt2-verdict --log $logPath
        exit $LASTEXITCODE
    }

    Write-Host "WARN: no log at $logPath — run FFX battle RT2 then re-run this script"
    dotnet run -c Release --project FFXProjectEditor -- --nul-ward-rt2-verdict --log $logPath --offline-ok
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
