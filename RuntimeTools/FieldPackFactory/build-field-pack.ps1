# FieldPack factory -- orchestrate scout ingest, map export, editor publish, overlay refresh,
# CHR subset staging, and field-pack-bundle.json manifest generation.
#
# Offline lab only; does NOT deploy Field Scout hooks to the game.
#
# Usage:
#   .\build-field-pack.ps1 -Areas map/maca/maca03
#   .\build-field-pack.ps1 -Areas map/maca/maca03,map/mcyt/mcyt02 -InstanceGroup -ChrSubset
#   .\build-field-pack.ps1 -SessionPath "D:\...\session-....jsonl" -ExportMaps -PublishToEditor
#   .\build-field-pack.ps1 -Areas map/maca/maca03 -PackOnly  (re-pack existing exports)

param(
    [string]$SessionPath = "",
    [string[]]$Areas = @(),
    [string]$WorkRoot = "work\field_scout",
    [string]$PackRoot = "work\field_pack",
    [string]$Ps3Root = "D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data",
    [string]$GameModules = "D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\modules",
    [string]$ViewerRoot = "RuntimeTools\FFXMapViewerWeb",
    [switch]$ExportMaps,
    [switch]$PublishToEditor,
    [switch]$MountWorkForMapViewer,
    [switch]$InstanceGroup,
    [switch]$ChrSubset,
    [switch]$SkipOverlayRefresh,
    [switch]$PackOnly,          # skip export, only re-pack existing exports into FieldPack
    [switch]$ValidateVisual     # run render gate + SSIM validation after packing
)

$ErrorActionPreference = "Stop"
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$scoutLab = Join-Path $repo "RuntimeTools\FieldScoutLab"
$mapLab = Join-Path $repo "RuntimeTools\PhyreMapExportLab"
$mapLabDll = Join-Path $mapLab "bin\Debug\net8.0\PhyreMapExportLab.dll"
$viewerRootFull = Join-Path $repo $ViewerRoot

function Resolve-Path([string]$p) {
    if ([System.IO.Path]::IsPathRooted($p)) { return $p }
    return Join-Path $repo $p
}

function Invoke-DotNetLab([string]$Project, [string[]]$Args) {
    Write-Host ">> dotnet run --project $Project $($Args -join ' ')"
    & dotnet run --project $Project -c Debug --no-launch-profile -- @Args
    if ($LASTEXITCODE -ne 0) { throw "Lab failed ($LASTEXITCODE): $Project" }
}

# -- Step 1: Ingest scout session ----------------------------------------------
if ((-not $PackOnly) -and ($SessionPath -or $PublishToEditor -or (-not $Areas.Count))) {
    $procArgs = @(
        "-OutputRoot", (Resolve-Path $WorkRoot),
        "-Ps3Root", $Ps3Root,
        "-GameModules", $GameModules
    )
    if ($SessionPath) { $procArgs += @("-SessionPath", $SessionPath) }
    if ($ExportMaps) { $procArgs += "-ExportMaps" }
    if ($PublishToEditor) { $procArgs += "-PublishToEditor" }
    & (Join-Path $scoutLab "process-scout-session.ps1") @procArgs
}

# -- Step 2: Export maps -------------------------------------------------------
$exportedAreas = @()
$exportRoot = Join-Path $repo "work\phyre_map_export_lab"

if ((-not $PackOnly) -and $Areas.Count -gt 0) {
    if (-not (Test-Path $mapLabDll)) {
        Write-Host "Building PhyreMapExportLab..."
        dotnet build $mapLab -c Debug -v q
        if ($LASTEXITCODE -ne 0) { throw "PhyreMapExportLab build failed" }
    }

    foreach ($area in $Areas) {
        $area = $area.Trim()
        if (-not $area) { continue }

        $areaFolder = $area -replace '^map/', '' -replace '^btlmap/', 'btlmap/'
        $areaOutput = Join-Path $exportRoot $areaFolder

        $exportArgs = @(
            "export",
            "--area", $area,
            "--ps3-root", $Ps3Root,
            "--output", $areaOutput,
            "--portable"
        )
        if ($InstanceGroup) { $exportArgs += "--instance-group" }
        Write-Host ">> Map export: $area -> $areaOutput"
        Invoke-DotNetLab $mapLab $exportArgs
        $exportedAreas += $area
    }
} elseif ($PackOnly -and $Areas.Count -gt 0) {
    foreach ($area in $Areas) {
        $area = $area.Trim()
        if (-not $area) { continue }
        $areaFolder = $area -replace '^map/', '' -replace '^btlmap/', 'btlmap/'
        $areaOutput = Join-Path $exportRoot $areaFolder
        if (Test-Path $areaOutput) {
            $exportedAreas += $area
        } else {
            Write-Warning "No existing export for $area at $areaOutput"
        }
    }
}

# -- Step 3: Publish to editor -------------------------------------------------
if ($PublishToEditor) {
    $pubArgs = @("-WorkRoot", (Resolve-Path $WorkRoot), "-CopyMapExports")
    if ($SkipOverlayRefresh) { $pubArgs += "-SkipOverlayRefresh" }
    & (Join-Path $scoutLab "publish-scout-to-editor.ps1") @pubArgs
}

# -- Step 4: Build FieldPack staging + manifest --------------------------------
if ($exportedAreas.Count -gt 0) {
    $packRoot = Resolve-Path $PackRoot
    $packManifestPath = Join-Path $packRoot "field-pack-bundle.json"

    $walkBundlePath = Join-Path $repo "FFXProjectEditor\Modules\AuroraFieldExplorer\WalkManifest\walk-bundle.json"
    $walkData = $null
    if (Test-Path $walkBundlePath) {
        try { $walkData = Get-Content -LiteralPath $walkBundlePath -Raw -Encoding UTF8 | ConvertFrom-Json } catch {}
    }

    $packEntries = [System.Collections.ArrayList]::new()

    foreach ($area in $exportedAreas) {
        $areaFolder = $area -replace '^map/', '' -replace '^btlmap/', 'btlmap/'
        $srcRoot = Join-Path $exportRoot $areaFolder
        if (-not (Test-Path $srcRoot)) {
            Write-Warning "Export source not found: $srcRoot -- skipping $area"
            continue
        }

        $dest = Join-Path $packRoot $area
        New-Item -ItemType Directory -Force -Path $dest | Out-Null

        $gltfFiles = @(Get-ChildItem -LiteralPath $srcRoot -Filter "*.gltf" -File)
        $jsonFiles = @(Get-ChildItem -LiteralPath $srcRoot -Filter "*.json" -File)
        $textureDir = Join-Path $srcRoot "textures"
        $hasTextures = Test-Path $textureDir

        foreach ($f in ($gltfFiles + $jsonFiles)) {
            Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $dest $f.Name) -Force
        }
        if ($hasTextures) {
            Copy-Item -LiteralPath $textureDir -Destination $dest -Recurse -Force
        }

        $bestGltf = $null
        foreach ($pattern in @("*textured-phyre-slots*vertex-color*", "*textured-phyre-slots*", "*textured*vertex-color*", "*textured*", "*debug*")) {
            $match = $gltfFiles | Where-Object { $_.Name -like $pattern } | Select-Object -First 1
            if ($match) { $bestGltf = $match.Name; break }
        }

        $walkField = $null
        if ($walkData) {
            $walkField = @($walkData.fields) | Where-Object { $_.area -eq $area } | Select-Object -First 1
        }

        $entry = @{
            area = $area
            assetId = $area -replace '/', '_'
            exportPresent = $gltfFiles.Count -gt 0
            bestGltf = $bestGltf
            gltfCount = $gltfFiles.Count
            jsonReportCount = $jsonFiles.Count
            hasTextures = $hasTextures
            stagedAt = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssK")
            walkPisado = ($null -ne $walkField)
            walkNpcCount = if ($walkField) { $walkField.npcSpawnCount } else { $null }
            walkChrCount = if ($walkField) { $walkField.chrSpawnCount } else { $null }
        }
        [void]$packEntries.Add($entry)

        Write-Host ("  staged $area -> $dest ($($gltfFiles.Count) glTF, $($jsonFiles.Count) JSON)")
    }

    # -- CHR subset staging ----------------------------------------------------
    $chrStagedCount = 0
    if ($ChrSubset -and $walkData) {
        $chrIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $packEntries) {
            if (-not $entry.walkPisado) { continue }
            $shardPath = Join-Path $repo "FFXProjectEditor\Modules\AuroraFieldExplorer\WalkManifest\fields\$($entry.assetId).json"
            if (-not (Test-Path $shardPath)) { continue }
            try {
                $shard = Get-Content -LiteralPath $shardPath -Raw -Encoding UTF8 | ConvertFrom-Json
                foreach ($spawn in @($shard.npcSpawns)) {
                    $name = if ($spawn.chrName) { [string]$spawn.chrName } else { "" }
                    if ($name -match '^[ncfmsw]\d{3,4}$') { [void]$chrIds.Add($name.ToLowerInvariant()) }
                }
            } catch { Write-Warning "  could not read shard: $shardPath" }
        }

        if ($chrIds.Count -gt 0) {
            Write-Host ">> Staging $($chrIds.Count) CHR models..."
            $chrArgs = @(
                "-ChrIds", ($chrIds -join ','),
                "-SourceWorkRoot", (Resolve-Path "work"),
                "-DestWorkRoot", (Join-Path $packRoot "chr")
            )
            & (Join-Path $PSScriptRoot "build-fieldpack-chr-subset.ps1") @chrArgs
            $chrStagedCount = $chrIds.Count
        }
    }

    # -- Write manifest --------------------------------------------------------
    $manifest = @{
        schemaVersion = 1
        generatedAtUtc = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssK")
        packRoot = $PackRoot
        totalAreas = $packEntries.Count
        chrStagedCount = $chrStagedCount
        instanceGroup = [bool]$InstanceGroup
        areas = $packEntries.ToArray()
        walkBundleRef = if (Test-Path $walkBundlePath) { $walkBundlePath } else { $null }
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 10
    New-Item -ItemType Directory -Force -Path (Split-Path $packManifestPath -Parent) | Out-Null
    $manifestJson | Out-File -LiteralPath $packManifestPath -Encoding UTF8
    Write-Host "  manifest: $packManifestPath"

    # -- Optional: zip ---------------------------------------------------------
    $zipPath = Join-Path $packRoot "field-pack.zip"
    if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Compress-Archive -Path (Join-Path $packRoot "*") -DestinationPath $zipPath -Force
    Write-Host "  zip: $zipPath ($((Get-Item $zipPath).Length / 1MB -as [int]) MB)"
}

# -- Step 5: Mount work/ for MapViewer -----------------------------------------
if ($MountWorkForMapViewer) {
    & (Join-Path $PSScriptRoot "mount-work-for-mapviewer.ps1") -WorkRoot (Resolve-Path "work")
}

Write-Host ""
Write-Host "FieldPack factory pass complete."
Write-Host "  pack root:  $(Resolve-Path $PackRoot)"
Write-Host "  manifest:   $packManifestPath"
Write-Host "  areas:      $($exportedAreas.Count) exported / $($packEntries.Count) staged"
if ($chrStagedCount -gt 0) { Write-Host "  CHR models: $chrStagedCount" }

# -- Step 6: Validate visual (render gate + SSIM) -----------------------------
if ($ValidateVisual) {
    $nodeExe = "C:\Users\wande\AppData\Local\Programs\nodejs-portable\node-v24.15.0-win-x64\node.exe"
    $pythonExe = "C:\Users\wande\AppData\Local\Programs\Python\Python313\python.exe"
    $renderGate = Join-Path $PSScriptRoot "render-gate.mjs"
    $validator = Join-Path $PSScriptRoot "validate-visual.py"

    Write-Host ">> Render gate: capturing screenshots..."
    & $nodeExe $renderGate --all 2>&1 | Write-Host

    Write-Host ">> Validating SSIM..."
    & $pythonExe $validator --auto-golden 2>&1 | Write-Host

    Write-Host "  validation: $(Resolve-Path $PackRoot)\validation\score.json"
}
