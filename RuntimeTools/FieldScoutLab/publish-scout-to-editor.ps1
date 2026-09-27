# Promote Field Scout lab output (work/) into the editor release bundle + MapViewer assets.
# work/ stays ephemeral/gitignored; this script is the ONLY bridge to shippable paths.
#
# Usage:
#   .\publish-scout-to-editor.ps1
#   .\publish-scout-to-editor.ps1 -WorkRoot work\field_scout -CopyMapExports

param(
    [string]$WorkRoot = "work\field_scout",
    [switch]$CopyMapExports,
    [switch]$SkipOverlayRefresh
)

$ErrorActionPreference = "Stop"
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not [System.IO.Path]::IsPathRooted($WorkRoot)) {
    $WorkRoot = Join-Path $repo $WorkRoot
}

$manifestRoot = Join-Path $repo "FFXProjectEditor\Modules\AuroraFieldExplorer\WalkManifest"
$fieldsDir = Join-Path $manifestRoot "fields"
$bundlePath = Join-Path $manifestRoot "walk-bundle.json"
$mapViewerMaps = Join-Path $repo "RuntimeTools\FFXMapViewerWeb\public\maps\map"

function Read-JsonFile([string]$Path) {
    if (-not (Test-Path $Path)) { return $null }
    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Get-FieldDetail($Report, $Catalog, [string]$AreaPath) {
    if ($Report -and $Report.mapFieldDetails) {
        $prop = $Report.mapFieldDetails.PSObject.Properties | Where-Object { $_.Name -eq $AreaPath } | Select-Object -First 1
        if ($prop) { return $prop.Value }
    }
    if ($Catalog) {
        return $Catalog | Where-Object { [string]$_.areaPath -eq $AreaPath } | Select-Object -First 1
    }
    return $null
}

$report = Read-JsonFile (Join-Path $WorkRoot "scout-report.json")
$catalog = Read-JsonFile (Join-Path $WorkRoot "walk-field-catalog.json")
$scenePlaced = Read-JsonFile (Join-Path $WorkRoot "scene-nodes-world.json")
$chrSpawns = Read-JsonFile (Join-Path $WorkRoot "chr-spawns.json")
$chestSpawns = Read-JsonFile (Join-Path $WorkRoot "chest-spawns.json")
$npcSpawnsJson = Read-JsonFile (Join-Path $WorkRoot "npc-spawns.json")
$triggerSpawnsJson = Read-JsonFile (Join-Path $WorkRoot "trigger-spawns.json")
$playerTrace = Read-JsonFile (Join-Path $WorkRoot "player-walk-trace.json")

if (-not $catalog -and -not $report) {
    throw "Nothing to publish. Run process-scout-session.ps1 first (output under $WorkRoot)."
}

New-Item -ItemType Directory -Force -Path $fieldsDir | Out-Null

$fieldKeys = @()
if ($report -and $report.mapFields) { $fieldKeys = @($report.mapFields) }
elseif ($catalog) {
    $fieldKeys = @($catalog | ForEach-Object { [string]$_.areaPath } | Sort-Object -Unique)
}

$sceneByField = @{}
if ($scenePlaced) {
    foreach ($node in @($scenePlaced)) {
        $area = [string]$node.area
        $field = [string]$node.field
        if (-not $area -or -not $field) { continue }
        $key = "map/$area/$field"
        if (-not $sceneByField.ContainsKey($key)) { $sceneByField[$key] = @() }
        $sceneByField[$key] += $node
    }
}

$ProximityMaxDistSq = 250000.0  # ~500 world units — skip mis-bucket when area/field missing

function Resolve-FieldKey {
    param(
        $obj,
        [string[]]$fieldKeys,
        $report,
        $catalog
    )
    $area = [string]$obj.area
    $field = [string]$obj.field
    if ($area -and $field) { return "map/$area/$field" }

    $bestKey = $null
    $bestDist = [double]::MaxValue
    $ox = [double]$obj.x
    $oy = [double]$obj.y
    $oz = [double]$obj.z
    if ($ox -eq 0 -and $oz -eq 0) { return $null }

    foreach ($fk in $fieldKeys) {
        $detail = Get-FieldDetail $report $catalog $fk
        if (-not $detail) { continue }
        $dx = $ox - [double]$detail.samplePx
        $dy = $oy - [double]$detail.samplePy
        $dz = $oz - [double]$detail.samplePz
        $d = ($dx * $dx) + ($dy * $dy) + ($dz * $dz)
        if ($d -lt $bestDist) { $bestDist = $d; $bestKey = $fk }
    }
    if ($bestKey -and $bestDist -le $ProximityMaxDistSq) { return $bestKey }
    return $null
}

$chestByField = @{}
if ($chestSpawns) {
    foreach ($chest in @($chestSpawns)) {
        $key = Resolve-FieldKey -obj $chest -fieldKeys $fieldKeys -report $report -catalog $catalog
        if (-not $key) { continue }
        if (-not $chestByField.ContainsKey($key)) { $chestByField[$key] = @() }
        $chestByField[$key] += $chest
    }
}

$chrByField = @{}
if ($chrSpawns) {
    foreach ($chr in @($chrSpawns)) {
        $key = Resolve-FieldKey -obj $chr -fieldKeys $fieldKeys -report $report -catalog $catalog
        if (-not $key) { continue }
        if (-not $chrByField.ContainsKey($key)) { $chrByField[$key] = @() }
        $chrByField[$key] += $chr
    }
}

$npcByField = @{}
if ($npcSpawnsJson) {
    foreach ($npc in @($npcSpawnsJson)) {
        $key = Resolve-FieldKey -obj $npc -fieldKeys $fieldKeys -report $report -catalog $catalog
        if (-not $key) { continue }
        if (-not $npcByField.ContainsKey($key)) { $npcByField[$key] = @() }
        $npcByField[$key] += $npc
    }
}

$triggerByField = @{}
if ($triggerSpawnsJson) {
    foreach ($trg in @($triggerSpawnsJson)) {
        $key = Resolve-FieldKey -obj $trg -fieldKeys $fieldKeys -report $report -catalog $catalog
        if (-not $key) { continue }
        if (-not $triggerByField.ContainsKey($key)) { $triggerByField[$key] = @() }
        $triggerByField[$key] += $trg
    }
}

$exportsCopied = 0
$fieldEntries = @()

foreach ($areaPath in ($fieldKeys | Sort-Object)) {
    if ($areaPath -notmatch '^map/([^/]+)/([^/]+)$') { continue }
    $area = $matches[1]
    $field = $matches[2]
    $shardName = "${area}_${field}.json"
    $shardPath = Join-Path $fieldsDir $shardName

    $detail = Get-FieldDetail $report $catalog $areaPath

    $shard = [ordered]@{
        schemaVersion = 1
        areaPath = $areaPath
        area = $area
        field = $field
        samplePx = $(if ($detail) { $detail.samplePx } else { $null })
        samplePy = $(if ($detail) { $detail.samplePy } else { $null })
        samplePz = $(if ($detail) { $detail.samplePz } else { $null })
        sceneNodesPlaced = @($(if ($sceneByField.ContainsKey($areaPath)) { $sceneByField[$areaPath] } else { @() }))
        chestSpawns = @($(if ($chestByField.ContainsKey($areaPath)) { $chestByField[$areaPath] } else { @() }))
        chrSpawns = @($(if ($chrByField.ContainsKey($areaPath)) { $chrByField[$areaPath] } else { @() }))
        npcSpawns = @($(if ($npcByField.ContainsKey($areaPath)) { $npcByField[$areaPath] } else { @() }))
        triggerSpawns = @($(if ($triggerByField.ContainsKey($areaPath)) { $triggerByField[$areaPath] } else { @() }))
        mapViewerRelative = "./public/maps/map/$area/$field"
    }

    $shard | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $shardPath -Encoding UTF8

    $hasExport = $false
    if ($CopyMapExports) {
        $src = Join-Path $WorkRoot "exports\map_${area}_${field}"
        $dest = Join-Path $mapViewerMaps "$area\$field"
        if (Test-Path $src) {
            New-Item -ItemType Directory -Force -Path $dest | Out-Null
            Copy-Item -Path (Join-Path $src "*") -Destination $dest -Recurse -Force
            $hasExport = $true
            ++$exportsCopied
        }
    } else {
        $hasExport = Test-Path (Join-Path $mapViewerMaps "$area\$field")
    }

    $fieldEntries += [ordered]@{
        areaPath = $areaPath
        shard = "fields/$shardName"
        sceneNodeCount = $(if ($sceneByField.ContainsKey($areaPath)) { @($sceneByField[$areaPath]).Count } else { 0 })
        chestCount = $(if ($chestByField.ContainsKey($areaPath)) { @($chestByField[$areaPath]).Count } else { 0 })
        chrSpawnCount = $(if ($chrByField.ContainsKey($areaPath)) { @($chrByField[$areaPath]).Count } else { 0 })
        npcSpawnCount = $(if ($npcByField.ContainsKey($areaPath)) { @($npcByField[$areaPath]).Count } else { 0 })
        triggerSpawnCount = $(if ($triggerByField.ContainsKey($areaPath)) { @($triggerByField[$areaPath]).Count } else { 0 })
        mapExportPresent = $hasExport
    }
}

$bundle = [ordered]@{
    schemaVersion = 1
    source = "field-scout"
    publishedAt = (Get-Date).ToString("o")
    sessionPath = $null
    workRoot = $null
    fieldCount = $fieldEntries.Count
    stats = [ordered]@{
        sceneNodesPlaced = $(if ($scenePlaced) { @($scenePlaced).Count } else { 0 })
        chestSpawns = $(if ($chestSpawns) { @($chestSpawns).Count } else { 0 })
        chrSpawns = $(if ($chrSpawns) { @($chrSpawns).Count } else { 0 })
        npcSpawns = $(if ($npcSpawnsJson) { @($npcSpawnsJson).Count } else { 0 })
        triggerSpawns = $(if ($triggerSpawnsJson) { @($triggerSpawnsJson).Count } else { 0 })
        playerTraceSamples = $(if ($playerTrace) { @($playerTrace).Count } else { 0 })
        exportsCopied = $exportsCopied
    }
    fields = $fieldEntries
}

$bundle | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $bundlePath -Encoding UTF8

Write-Host ""
Write-Host "Published Field Scout -> editor bundle"
Write-Host "  Bundle:  $bundlePath"
Write-Host "  Shards:  $fieldsDir  ($($fieldEntries.Count) fields)"
Write-Host "  MapViewer exports copied: $exportsCopied (gitignored public/maps/)"
Write-Host ""
Write-Host "Git safety:"
Write-Host "  work/field_scout/  = lab only (root .gitignore) — DO NOT commit"
Write-Host "  WalkManifest/fields/ = gitignored shards — local/release packaging only"
Write-Host "  walk-bundle.json = light summary (safe to commit when promoting a release)"
Write-Host ""

# Refresh MapViewer field-encounters.json overlays (NPCs/chests/zones) for mounted walked fields.
if (-not $SkipOverlayRefresh) {
Write-Host "Refreshing field-encounters overlays (WalkManifest -> MapViewer)..."
$csproj = Join-Path $repo "FFXProjectEditor\FFXProjectEditor.csproj"
if (Test-Path $csproj) {
    try {
        dotnet run --project $csproj -c Debug --no-launch-profile -- --field-explorer-refresh-walk 2>&1 | ForEach-Object { Write-Host $_ }
    } catch {
        Write-Host "  WARN: overlay refresh failed: $_"
        Write-Host "  Run manually: dotnet run --project FFXProjectEditor -- --field-explorer-refresh-walk"
    }
} else {
    Write-Host "  WARN: FFXProjectEditor.csproj not found - skip overlay refresh."
}
Write-Host ""
} else {
    Write-Host "Skip overlay refresh (-SkipOverlayRefresh) - use Field Explorer UI or --field-explorer-refresh-walk."
    Write-Host ""
}
