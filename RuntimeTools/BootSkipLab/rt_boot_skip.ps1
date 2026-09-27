# RT-BS-01..03 runner - static checks always; live probe/hooks when FFX+probe open.
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$results = [ordered]@{}
$mmfOk = $false

function Test-Mmf {
    $ctl = Join-Path $repo "RuntimeTools\FfxDinput8Probe\ctl\bin\ffxprobectl.exe"
    if (-not (Test-Path $ctl)) {
        & (Join-Path $repo "RuntimeTools\FfxDinput8Probe\build.ps1") | Out-Null
    }
    try {
        $null = [System.IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting("Local\FFXProbeBlock_v1")
        return $true
    } catch {
        return $false
    }
}

# RT-BS-03: BootSkipHook compiles with PolyHook build
Write-Host "RT-BS-03: build ffx-hooks with BootSkipHook.cpp..."
if (-not $SkipBuild) {
    & (Join-Path $repo "RuntimeTools\FfxHooksDll\build_hooks.ps1") -WithPolyHook -Release
}
$dll = Join-Path $repo "RuntimeTools\FfxHooksDll\bin\Release\ffx-hooks.dll"
$results["RT-BS-03_build"] = (Test-Path $dll)
Write-Host "  -> $(if ($results['RT-BS-03_build']) { 'PASS' } else { 'FAIL' })"

$mmfOk = Test-Mmf
$results["probe_mmf_present"] = $mmfOk

if ($mmfOk) {
    Write-Host "RT-BS-01: short boot trace (15s)..."
    & (Join-Path $PSScriptRoot "boot_trace.ps1") -DurationSec 15 -IntervalMs 300
    $results["RT-BS-01_trace"] = ($LASTEXITCODE -eq 0)

    Write-Host "RT-BS-02: auto_title pulse (10s)..."
    & (Join-Path $PSScriptRoot "auto_title.ps1") -DurationSec 10 -ConfirmIntervalMs 1000
    $results["RT-BS-02_setinput"] = ($LASTEXITCODE -eq 0)
} else {
    Write-Warning "FFX+probe not detected - RT-BS-01/02 skipped (run game with ffx-probe, then re-run)."
    $results["RT-BS-01_trace"] = "SKIP"
    $results["RT-BS-02_setinput"] = "SKIP"
}

$outJson = Join-Path $PSScriptRoot "rt_boot_skip_results.json"
$results | ConvertTo-Json | Set-Content $outJson -Encoding UTF8
Write-Host "Results -> $outJson"
$fail = @($results["RT-BS-03_build"], $results["RT-BS-01_trace"], $results["RT-BS-02_setinput"] |
    Where-Object { $_ -eq $false }).Count
exit $(if ($fail -gt 0) { 1 } else { 0 })
