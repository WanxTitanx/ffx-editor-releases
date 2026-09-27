# Copy a minimal CHR glTF subset from modelviewer work/ into field pack staging for MapViewer.
#
# Usage:
#   .\build-fieldpack-chr-subset.ps1 -ChrIds n001,f042
#   .\build-fieldpack-chr-subset.ps1 -FromEncountersJson "RuntimeTools\FFXMapViewerWeb\public\maps\map\mcyt\mcyt02\field-encounters.json"

param(
    [string[]]$ChrIds = @(),
    [string]$FromEncountersJson = "",
    [string]$SourceWorkRoot = "work",
    [string]$DestWorkRoot = "work\field_pack_chr"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

function Resolve-RepoPath([string]$p) {
    if ([System.IO.Path]::IsPathRooted($p)) { return $p }
    return Join-Path $repo $p
}

function Chr-Folder([string]$id) {
    switch ($id.Substring(0, 1)) {
        "n" { return "npc_anim" }
        "c" { return "pc_anim" }
        "f" { return "obj_anim" }
        "m" { return "phyre_chr_anim" }
        "s" { return "summon_anim" }
        "w" { return "wep_anim" }
        default { return $null }
    }
}

$ids = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($id in $ChrIds) {
    if ($id) { [void]$ids.Add($id.Trim().ToLowerInvariant()) }
}

if ($FromEncountersJson) {
    $encPath = Resolve-RepoPath $FromEncountersJson
    if (-not (Test-Path $encPath)) { throw "Encounters JSON not found: $encPath" }
    $enc = Get-Content -LiteralPath $encPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($e in @($enc.entities)) {
        $name = [string]($e.chrName ?? $e.name ?? "")
        if ($name -match '^[ncfmsw]\d{3,4}$') { [void]$ids.Add($name.ToLowerInvariant()) }
    }
}

if ($ids.Count -eq 0) {
    throw "No CHR ids. Pass -ChrIds or -FromEncountersJson."
}

$srcRoot = Resolve-RepoPath $SourceWorkRoot
$destRoot = Resolve-RepoPath $DestWorkRoot
New-Item -ItemType Directory -Force -Path $destRoot | Out-Null

$copied = 0
foreach ($id in ($ids | Sort-Object)) {
    $folder = Chr-Folder $id
    if (-not $folder) {
        Write-Host "  skip unknown prefix: $id"
        continue
    }
    $rel = Join-Path $folder "models\$id"
    $src = Join-Path $srcRoot $rel
    $dest = Join-Path $destRoot $rel
    if (-not (Test-Path $src)) {
        Write-Host "  missing: $src"
        continue
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
    Copy-Item -LiteralPath $src -Destination $dest -Recurse -Force
    $copied++
    Write-Host "  copied $id -> $dest"
}

Write-Host ""
Write-Host "CHR subset staging: $copied models under $destRoot"
Write-Host "Run mount-work-for-mapviewer.ps1 -WorkRoot $DestWorkRoot if serving from field pack only."
