# Field Scout — ingest walk manifest JSONL and prepare export queue.
# Usage:
#   .\process-scout-session.ps1
#   .\process-scout-session.ps1 -SessionPath "D:\...\modules\field-scout\session-....jsonl"
#   .\process-scout-session.ps1 -ExportMaps -Ps3Root "D:\FFX Extracted\...\ps3data"

param(
    [string]$SessionPath = "",
    [string]$GameModules = "D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\modules",
    [string]$OutputRoot = "work\field_scout",
    [string]$Ps3Root = "D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data",
    [switch]$ExportMaps,
    [switch]$ExtractTextures,
    [switch]$ConvertTextures,
    [switch]$PublishToEditor
)

$ErrorActionPreference = "Stop"

function Find-LatestSession {
    param([string]$Root)
    $dir = Join-Path $Root "field-scout"
    if (-not (Test-Path $dir)) { return $null }
    Get-ChildItem -Path $dir -Filter "session-*.jsonl" |
        Where-Object { $_.Name -notmatch '-trace\.jsonl$' } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

if (-not $SessionPath) {
    $latest = Find-LatestSession -Root $GameModules
    if (-not $latest) { throw "No session JSONL under $GameModules\field-scout" }
    $SessionPath = $latest.FullName
}

if (-not (Test-Path $SessionPath)) { throw "Session not found: $SessionPath" }

Write-Host "Ingest: $SessionPath"
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$assets = @{}
$fields = @{}
$geometry = @{}
$sceneNodes = @{}
$sceneNodesPlaced = @{}
$chrSpawns = @{}
$chestSpawns = @{}
$npcSpawns = @{}
$triggerSpawns = @{}
$ultraStubs = [System.Collections.Generic.List[object]]::new()
$maxEvents = [System.Collections.Generic.List[object]]::new()
$ultraMeta = $null
$playerTrace = [System.Collections.Generic.List[object]]::new()
$encounters = [System.Collections.Generic.List[object]]::new()
$polyMetas = @{}
$fieldLoads = [System.Collections.Generic.List[object]]::new()
$categories = @{}
$lines = Get-Content -LiteralPath $SessionPath -Encoding UTF8

$tracePath = $SessionPath -replace '\.jsonl$','-trace.jsonl'
if (Test-Path $tracePath) {
    Write-Host "Ingest trace: $tracePath"
    $traceLines = Get-Content -LiteralPath $tracePath -Encoding UTF8
    $lines = $lines + $traceLines
}

function Test-ChestLikeName([string]$Name) {
    if (-not $Name) { return $false }
    $n = $Name.ToLowerInvariant()
    foreach ($pat in @('bauro', 'chest', 'takara', 'treasure', 'tbox', 'h_treasure', 'obj_treasure')) {
        if ($n.Contains($pat)) { return $true }
    }
    return $false
}

function Register-Field {
    param($obj, [string]$Source)
    $field = [string]$obj.field
    $area = [string]$obj.area
    if (-not $field -or -not $area) { return }
    $key = "map/$area/$field"
    if (-not $fields.ContainsKey($key)) {
        $fields[$key] = @{
            area = $area
            field = $field
            samplePx = $obj.px
            samplePy = $obj.py
            samplePz = $obj.pz
            sceneId = $obj.sceneId
            source = $Source
        }
    }
}

foreach ($line in $lines) {
    if (-not $line.Trim()) { continue }
    try { $obj = $line | ConvertFrom-Json } catch { continue }

    if ($obj.kind -eq "session_start") {
        $ultraMeta = $obj
        continue
    }
    if ($obj.kind -eq "session_end" -or $obj.kind -eq "trace_end") { continue }

    if ($obj.kind -eq "ultra_stub" -or $obj.kind -eq "ultra_scene_env_sample" -or $obj.kind -eq "ultra_pipeline_hint") {
        $ultraStubs.Add($obj) | Out-Null
        Register-Field -obj $obj -Source $obj.kind
        continue
    }

    if ($obj.kind -eq "ultra_encounter_sample" -or $obj.kind -eq "ultra_zone_trace") {
        $encounters.Add($obj) | Out-Null
        Register-Field -obj $obj -Source $obj.kind
        continue
    }

    if ($obj.kind -eq "max_takara" -or $obj.kind -eq "max_warp" -or $obj.kind -eq "max_zone_slot") {
        $maxEvents.Add($obj) | Out-Null
        Register-Field -obj $obj -Source $obj.kind
        continue
    }

    if ($obj.kind -eq "npc_spawn") {
        $key = "$($obj.chrId)|$($obj.slot)|$($obj.x)|$($obj.y)|$($obj.z)"
        if (-not $npcSpawns.ContainsKey($key)) { $npcSpawns[$key] = $obj }
        Register-Field -obj $obj -Source "npc_spawn"
        continue
    }

    if ($obj.kind -eq "trigger_spawn") {
        $key = "$($obj.name)|$($obj.x)|$($obj.y)|$($obj.z)"
        if (-not $triggerSpawns.ContainsKey($key)) { $triggerSpawns[$key] = $obj }
        Register-Field -obj $obj -Source "trigger_spawn"
        continue
    }

    if ($obj.kind -eq "player_trace") {
        $playerTrace.Add($obj) | Out-Null
        continue
    }

    if ($obj.kind -eq "encounter" -or $obj.kind -eq "zone_resolve") {
        $encounters.Add($obj) | Out-Null
        continue
    }

    if ($obj.kind -eq "poly_meta") {
        $key = [string]$obj.polyMeta
        if ($key -and -not $polyMetas.ContainsKey($key)) { $polyMetas[$key] = $obj }
        continue
    }

    if ($obj.kind -eq "field_load") {
        $fieldLoads.Add($obj) | Out-Null
        $path = [string]$obj.path
        if ($path -match '/map/([^/]+)/([^/]+)') {
            Register-Field -obj $obj -Source "field_load"
        }
        continue
    }

    if ($obj.kind -eq "geometry" -or $obj.kind -eq "geometry_inferred") {
        $path = [string]$obj.path
        if ($path -and -not $geometry.ContainsKey($path)) {
            $geometry[$path] = $obj
            $cat = [string]$obj.cat
            if ($cat) { $categories[$cat] = 1 + [int]($categories[$cat]) }
        }
        Register-Field -obj $obj -Source $obj.kind
        continue
    }

    if ($obj.kind -eq "chest_spawn") {
        $name = [string]$obj.name
        if (-not $name) { $name = [string]$obj.path }
        $key = "$name|$($obj.x)|$($obj.y)|$($obj.z)"
        if ($name -and -not $chestSpawns.ContainsKey($key)) {
            $chestSpawns[$key] = $obj
        }
        Register-Field -obj $obj -Source "chest_spawn"
        continue
    }

    if ($obj.kind -eq "scene_node_placed") {
        $name = [string]$obj.path
        $key = "$name|$($obj.wx)|$($obj.wy)|$($obj.wz)"
        if ($name -and -not $sceneNodesPlaced.ContainsKey($key)) {
            $sceneNodesPlaced[$key] = $obj
        }
        if ($name -and (Test-ChestLikeName $name) -and $obj.hasWorld -ne $false) {
            $cKey = "$name|$($obj.wx)|$($obj.wy)|$($obj.wz)"
            if (-not $chestSpawns.ContainsKey($cKey)) {
                $chestSpawns[$cKey] = [ordered]@{
                    kind = "chest_spawn"
                    name = $name
                    source = "scene_node_placed_inferred"
                    area = [string]$obj.area
                    field = [string]$obj.field
                    x = $obj.wx
                    y = $obj.wy
                    z = $obj.wz
                    px = $obj.px
                    py = $obj.py
                    pz = $obj.pz
                    sceneId = $obj.sceneId
                    mapToken = $obj.mapToken
                }
            }
        }
        Register-Field -obj $obj -Source "scene_node_placed"
        continue
    }

    if ($obj.kind -eq "chr_spawn") {
        $key = "$($obj.chrId)|$($obj.slot)|$($obj.x)|$($obj.y)|$($obj.z)"
        if (-not $chrSpawns.ContainsKey($key)) { $chrSpawns[$key] = $obj }
        Register-Field -obj $obj -Source "chr_spawn"
        $chrName = [string]$obj.chrName
        if ((Test-ChestLikeName $chrName) -and -not $chestSpawns.ContainsKey("chr|$key")) {
            $chestSpawns["chr|$key"] = [ordered]@{
                kind = "chest_spawn"
                name = $chrName
                source = "chr_spawn_inferred"
                chrId = $obj.chrId
                x = $obj.x
                y = $obj.y
                z = $obj.z
                px = $obj.px
                py = $obj.py
                pz = $obj.pz
                sceneId = $obj.sceneId
                mapToken = $obj.mapToken
            }
        }
        continue
    }

    if ($obj.kind -eq "scene_node") {
        $name = [string]$obj.path
        if ($name -and -not $sceneNodes.ContainsKey($name)) {
            $sceneNodes[$name] = $obj
        }
        Register-Field -obj $obj -Source "scene_node"
        continue
    }

    if ($obj.kind -ne "asset") { continue }

    $path = [string]$obj.path
    if (-not $path) { continue }
    if ($assets.ContainsKey($path)) { continue }

    $assets[$path] = $obj
    $cat = [string]$obj.cat
    if ($cat) { $categories[$cat] = 1 + [int]($categories[$cat]) }
    Register-Field -obj $obj -Source "asset"
}

$catalogPath = Join-Path $OutputRoot "walk-field-catalog.json"
$catalog = @($fields.Keys | Sort-Object | ForEach-Object {
    $f = $fields[$_]
    [ordered]@{
        areaPath = $_
        area = $f.area
        field = $f.field
        sceneId = $f.sceneId
        samplePx = $f.samplePx
        samplePy = $f.samplePy
        samplePz = $f.samplePz
        source = $f.source
    }
})

$report = [ordered]@{
    sessionPath = $SessionPath
    tracePath = $(if (Test-Path $tracePath) { $tracePath } else { $null })
    ingestedAt = (Get-Date).ToString("o")
    uniqueAssets = $assets.Count
    uniqueGeometry = $geometry.Count
    uniqueSceneNodes = $sceneNodes.Count
    sceneNodesPlaced = $sceneNodesPlaced.Count
    chestSpawns = $chestSpawns.Count
    npcSpawns = $npcSpawns.Count
    triggerSpawns = $triggerSpawns.Count
    ultraStubEvents = $ultraStubs.Count
    maxEvents = $maxEvents.Count
    ultraSession = $ultraMeta
    chrSpawns = $chrSpawns.Count
    playerTraceSamples = $playerTrace.Count
    encounterEvents = $encounters.Count
    uniquePolyMeta = $polyMetas.Count
    fieldLoadEvents = $fieldLoads.Count
    categories = $categories
    mapFields = @($fields.Keys | Sort-Object)
    mapFieldDetails = $fields
    geometryPaths = @($geometry.Keys | Sort-Object)
    sceneNodeNames = @($sceneNodes.Keys | Sort-Object)
    assetPaths = @($assets.Keys | Sort-Object)
}

$reportPath = Join-Path $OutputRoot "scout-report.json"
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding UTF8
$catalog | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $catalogPath -Encoding UTF8

$queuePath = Join-Path $OutputRoot "export-queue.txt"
$fields.Keys | Sort-Object | Set-Content -LiteralPath $queuePath -Encoding UTF8

Write-Host "Report: $reportPath"
Write-Host "Walk catalog: $catalogPath"
Write-Host "Unique assets (textures/etc): $($assets.Count)"
Write-Host "Unique geometry paths: $($geometry.Count)"
Write-Host "Unique scene nodes: $($sceneNodes.Count)"
Write-Host "Scene nodes with world XYZ: $($sceneNodesPlaced.Count)"
Write-Host "Chest spawns tracked: $($chestSpawns.Count)"
Write-Host "NPC spawns tracked: $($npcSpawns.Count)"
Write-Host "Trigger spawns tracked: $($triggerSpawns.Count)"
Write-Host "Ultra stub/sample events: $($ultraStubs.Count)"
Write-Host "CHR spawns tracked: $($chrSpawns.Count)"
Write-Host "Player trace samples: $($playerTrace.Count)"
Write-Host "Encounter events: $($encounters.Count)"
Write-Host "Unique polyMeta: $($polyMetas.Count)"
Write-Host "Field load events: $($fieldLoads.Count)"
Write-Host "Map fields discovered: $($fields.Count)"
Write-Host "Export queue: $queuePath"

if ($ExportMaps -and $fields.Count -gt 0) {
    $lab = Join-Path $PSScriptRoot "..\PhyreMapExportLab"
    $labProj = Join-Path $lab "PhyreMapExportLab.csproj"
    if (-not (Test-Path $labProj)) { throw "PhyreMapExportLab not found: $labProj" }

    foreach ($areaPath in ($fields.Keys | Sort-Object)) {
        Write-Host "Export map: $areaPath"
        dotnet run --project $labProj -c Release -- export `
            --ps3-root $Ps3Root `
            --area $areaPath `
            --output (Join-Path $OutputRoot "exports\$($areaPath -replace '/','_')")
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Export failed for $areaPath (exit $LASTEXITCODE)"
        }
    }
}

if ($sceneNodesPlaced.Count -gt 0) {
    $placedOut = Join-Path $OutputRoot "scene-nodes-world.json"
    $sceneNodesPlaced.Values | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $placedOut -Encoding UTF8
    Write-Host "Scene node world placements: $placedOut"
}

if ($chestSpawns.Count -gt 0) {
    $chestOut = Join-Path $OutputRoot "chest-spawns.json"
    $chestSpawns.Values | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $chestOut -Encoding UTF8
    Write-Host "Chest spawn catalog: $chestOut"
}

if ($npcSpawns.Count -gt 0) {
    $npcOut = Join-Path $OutputRoot "npc-spawns.json"
    $npcSpawns.Values | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $npcOut -Encoding UTF8
    Write-Host "NPC spawn catalog: $npcOut"
}

if ($triggerSpawns.Count -gt 0) {
    $trgOut = Join-Path $OutputRoot "trigger-spawns.json"
    $triggerSpawns.Values | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $trgOut -Encoding UTF8
    Write-Host "Trigger spawn catalog: $trgOut"
}

if ($maxEvents.Count -gt 0) {
    $maxOut = Join-Path $OutputRoot "max-events.json"
    $maxEvents.ToArray() | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $maxOut -Encoding UTF8
    Write-Host "MAX MODE events: $maxOut"
}

if ($ultraStubs.Count -gt 0) {
    $ultraOut = Join-Path $OutputRoot "ultra-samples.json"
    $ultraStubs.ToArray() | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ultraOut -Encoding UTF8
    Write-Host "Ultra samples/stubs: $ultraOut"
}

if ($chrSpawns.Count -gt 0) {
    $chrOut = Join-Path $OutputRoot "chr-spawns.json"
    $chrSpawns.Values | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $chrOut -Encoding UTF8
    Write-Host "CHR spawn catalog: $chrOut"
}

if ($playerTrace.Count -gt 0) {
    $traceOut = Join-Path $OutputRoot "player-walk-trace.json"
    $playerTrace.ToArray() | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $traceOut -Encoding UTF8
    Write-Host "Player walk trace: $traceOut"
}

if ($ExtractTextures -or $ConvertTextures) {
    $extractArgs = @{
        ReportPath = $reportPath
        Ps3Root = $Ps3Root
        OutputRoot = (Join-Path $OutputRoot "textures")
    }
    if ($ConvertTextures) { $extractArgs.ConvertPng = $true }
    & (Join-Path $PSScriptRoot "extract-scout-textures.ps1") @extractArgs
}

if ($PublishToEditor) {
    $pubArgs = @{
        WorkRoot = $OutputRoot
    }
    if ($ExportMaps) { $pubArgs.CopyMapExports = $true }
    & (Join-Path $PSScriptRoot "publish-scout-to-editor.ps1") @pubArgs
}

Write-Host "Done."
