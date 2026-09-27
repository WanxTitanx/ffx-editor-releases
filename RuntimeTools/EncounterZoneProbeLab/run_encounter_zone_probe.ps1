# One-shot deploy/operator for the Encounter Zone RT2 probe.
# Spec: docs/ai/ENCOUNTER_ZONE_RT2_CAPTURE_PROBE_SPEC_2026-08-16.md
# Implemented by: lane HOOK (this script is the deploy/operator wrapper + offline collapse).
param(
    [string]$GameRoot = "D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster",
    [switch]$SkipBuild,
    [switch]$Restore,
    [switch]$Collapse,
    [string]$Field = "",
    [float]$GridSize = 8.0
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
# Hooks moved to the independent ffx-hooks repo (see RuntimeTools/HOOKS_MOVED.md).
# Legacy in-repo path kept as fallback; the real tree lives in ffx-hooks.
$hooksCandidates = @(
    (Join-Path $repo "RuntimeTools\FfxHooksDll"),
    (Join-Path $env:USERPROFILE "Documents\ffx-hooks\src\runtime\FfxHooksDll"),
    (Join-Path $repo "..\ffx-hooks\src\runtime\FfxHooksDll")
)
$hooks = $hooksCandidates | Where-Object { Test-Path (Join-Path $_ "build_hooks.ps1") } | Select-Object -First 1
if (-not $hooks) {
    $hooks = $hooksCandidates[0]
    Write-Warning "FfxHooksDll not found in known locations (checked: $($hooksCandidates -join '; ')). Hooks live in the ffx-hooks repo (RuntimeTools/HOOKS_MOVED.md)."
}
$dll = Join-Path $hooks "bin\Release\ffx-hooks.dll"
$modules = Join-Path $GameRoot "modules"
$config = Join-Path $modules "config"
$outRoot = Join-Path $repo "work\encounter_zone_probe"
$flag = "encounter_zone_probe.flag"
$noBattleFlag = "encounter_zone_probe.nobattle.flag"

Write-Host "=== Encounter Zone RT2 probe ==="

$rootFlags = Get-ChildItem -LiteralPath $modules -Filter "*.flag" -ErrorAction SilentlyContinue
if ($rootFlags) {
    Write-Warning "modules/*.flag present (Arena+/Aurora/etc.): $($rootFlags.Name -join ', ') - close FFX and clear if they break boot."
}

if ($Restore) {
    foreach ($f in @($flag, $noBattleFlag)) {
        $p = Join-Path $config $f
        if (Test-Path $p) { Remove-Item -LiteralPath $p -Force; Write-Host "removed $f" }
    }
    Write-Host "Restore done. Probe OFF (default deny)."
    exit 0
}

if ($Collapse) {
    # Journal dirs: repo work/ AND game-exe-dir work/ (the hook writes to the ffx.exe dir).
    $gameOutRoot = Join-Path $GameRoot "work\encounter_zone_probe"
    $searchDirs = @($gameOutRoot, $outRoot) | Where-Object { Test-Path $_ }
    $files = @()
    foreach ($d in $searchDirs) {
        $files += Get-ChildItem (Join-Path $d "*.jsonl") -ErrorAction SilentlyContinue
    }
    $files = $files | Where-Object { $_.Name -match "encounter_zone_capture" -or (-not $Field) -or $_.Name -match $Field }
    if (-not $files) {
        Write-Warning "no JSONL found (search: $($searchDirs -join '; ')); run FFX walk with encounter_zone_probe.flag first"
        exit 0
    }
    $rows = @()
    foreach ($f in $files) {
        foreach ($ln in Get-Content $f.FullName) {
            if ($ln -eq '' -or $ln -match '"type":"field_load"') { continue }
            try { $rows += ($ln | ConvertFrom-Json) } catch { }
        }
    }
    # Split by field (int id) so one capture across fields still collapses per field.
    $byField = $rows | Group-Object field
    foreach ($grp in $byField) {
        $fid = [string]$grp.Name
        if ($Field -and $fid -notmatch "\b$Field\b" -and $fid -ne $Field) { continue }
        $emit = @()
        foreach ($r in $grp.Group) {
            if ($null -eq $r.x -or $null -eq $r.z) { continue }
            $gx = [int][Math]::Floor([double]$r.x / $GridSize)
            $gz = [int][Math]::Floor([double]$r.z / $GridSize)
            $g = [int]$r.group
            $emit += [pscustomobject]@{ cellx = $gx; cellz = $gz; group = $g; x = [Math]::Round([double]$r.x,1); z = [Math]::Round([double]$r.z,1) }
        }
        if (-not $emit) { continue }
        $agg = $emit | Group-Object cellx,cellz,group | ForEach-Object {
            $pp = $_.Group
            [pscustomobject]@{
                field = $fid; cellx = $pp[0].cellx; cellz = $pp[0].cellz; group = $pp[0].group
                x = [Math]::Round(($pp | Measure-Object x -Average).Average,1)
                z = [Math]::Round(($pp | Measure-Object z -Average).Average,1)
                count = $_.Count
            }
        }
        $groups = @($agg | Select-Object -ExpandProperty group -Unique | Sort-Object)
        $csv = Join-Path $outRoot "field_$fid.zones.csv"
        $agg | Sort-Object cellz,cellx,group | Export-Csv -Path $csv -NoTypeInformation
        Write-Host "Collapse -> $csv"
        Write-Host "  field=$fid cells=$($agg.Count) unique groups: $($groups -join ', ')  (Gate C: >1 group => zones ALIVE; only 0/const => OFF)"
    }
    exit 0
}

if (-not (Test-Path $GameRoot)) { throw "Game not found: $GameRoot" }
if (Get-Process FFX -ErrorAction SilentlyContinue) { throw "Close FFX before deploy." }

if (-not $SkipBuild) {
    if (-not (Test-Path (Join-Path $hooks "build_hooks.ps1"))) { throw "build_hooks.ps1 not found at $hooks (hooks lane may live in ffx-hooks repo). Run from there or -SkipBuild after a manual release build." }
    & (Join-Path $hooks "build_hooks.ps1") -WithPolyHook -Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not (Test-Path $dll)) { throw "ffx-hooks.dll not found at $dll" }

New-Item -ItemType Directory -Force -Path $config | Out-Null
New-Item -ItemType Directory -Force -Path $outRoot | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $modules "ffx-hooks.dll") -Force
New-Item -ItemType File -Force -Path (Join-Path $config $flag) | Out-Null
# no-battle: tells the probe to force g_FFX_Encounter_ForceFieldOverride during the sweep.
New-Item -ItemType File -Force -Path (Join-Path $config $noBattleFlag) | Out-Null

Write-Host ""
Write-Host "Deployed."
Write-Host "  DLL : $modules\ffx-hooks.dll"
Write-Host "  Flag: $config\$flag (encounter zone sampler ON)"
Write-Host "  Flag: $config\$noBattleFlag (force-field override ON - travel without battles)"
Write-Host ""

if ($Collapse) {
    if (-not $Field) { throw "-Collapse requires -Field <token> (e.g. bika03)" }
    $glob = Join-Path $outRoot "*$Field*.jsonl"
    $files = Get-ChildItem $glob -ErrorAction SilentlyContinue
    if (-not $files) { Write-Warning (\"no JSONL for $Field under $outRoot\"); exit 0 }
    $rows = foreach ($f in $files) { Get-Content $f.FullName | ForEach-Object { if ($_ -match '\"type\":\"field_load\"') {} elseif ($_ -ne '') { $_ | ConvertFrom-Json } } }
    $emit = @()
    foreach ($r in $rows) {
        if (-not $r.x -or -not $r.z) { continue }
        $gx = [int][Math]::Floor([double]$r.x / $GridSize)
        $gz = [int][Math]::Floor([double]$r.z / $GridSize)
        $g = [int]$r.group
        $emit += [pscustomobject]@{ cellx = $gx; cellz = $gz; group = $g; x = [Math]::Round([double]$r.x,1); z = [Math]::Round([double]$r.z,1); n = 1 }
    }
    $agg = $emit | Group-Object cellx,cellz,group | ForEach-Object {
        $pp = $_.Group
        [pscustomobject]@{
            cellx = $pp[0].cellx; cellz = $pp[0].cellz; group = $pp[0].group
            x = [Math]::Round(($pp | Measure-Object x -Average).Average,1)
            z = [Math]::Round(($pp | Measure-Object z -Average).Average,1)
            count = $_.Count
        }
    }
    $groups = $agg | Select-Object -ExpandProperty group -Unique | Sort-Object
    $csv = Join-Path $outRoot "$Field.zones.csv"
    $agg | Sort-Object cellz,cellx | Export-Csv -Path $csv -NoTypeInformation
    Write-Host "Collapse -> $csv"
    Write-Host "  unique groups: $($groups -join ', ') (Gate C: >1 group => zones ALIVE; only 0/const => OFF)"
    exit 0
}

Write-Host "Play FFX - walk the field in zigzag (x4 speed + no battles forced)."
Write-Host "Probe logs: $outRoot\<field>.jsonl"
Write-Host ""
Write-Host "After the walk, close FFX and collapse into zone map:"
Write-Host "  .\RuntimeTools\EncounterZoneProbeLab\run_encounter_zone_probe.ps1 -Collapse -Field bika03"
Write-Host "  -> bika03.zones.csv feeds the Aurora/MapViewer overlay (one row per grid cell per group)."
Write-Host ""
Write-Host "REALITY CHECK (Gate C): unique groups in the CSV decide if the HD game uses encounter zones."
Write-Host "  - more than one group  => mechanism ALIVE -> build the map overlay."
Write-Host "  - only 0 / constant     => mechanism OFF (ffxmap.id absent) -> STOP; no image in decode."
exit 0
