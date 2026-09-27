# Mount work/ into FFXMapViewerWeb so CHR glTF paths (/work/npc_anim/...) resolve over HTTP.
# Uses directory junction (no admin required on Win10+).
#
# Usage:
#   .\mount-work-for-mapviewer.ps1
#   .\mount-work-for-mapviewer.ps1 -WorkRoot "D:\ffx-editor-main\work"

param(
    [string]$WorkRoot = "work"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not [System.IO.Path]::IsPathRooted($WorkRoot)) {
    $WorkRoot = Join-Path $repo $WorkRoot
}

$viewerRoot = Join-Path $repo "RuntimeTools\FFXMapViewerWeb"
$linkPath = Join-Path $viewerRoot "work"

if (-not (Test-Path $WorkRoot)) {
    Write-Host "Creating work root: $WorkRoot"
    New-Item -ItemType Directory -Force -Path $WorkRoot | Out-Null
}

if (Test-Path $linkPath) {
    $item = Get-Item -LiteralPath $linkPath -Force
    $isLink = $item.Attributes -band [IO.FileAttributes]::ReparsePoint
    if ($isLink) {
        Write-Host "work/ junction already exists: $linkPath"
        exit 0
    }
    Write-Host "work/ exists as regular directory -- removing"
    Remove-Item -LiteralPath $linkPath -Recurse -Force
}

# Use directory junction (no admin required)
cmd /c mklink /J "$linkPath" "$WorkRoot" 2>&1 | Out-Null
if (Test-Path $linkPath) {
    Write-Host "Mounted MapViewer /work -> $WorkRoot (junction)"
    Write-Host "CHR glTF URLs like /work/npc_anim/models/n001/n001_static.gltf should now resolve."
} else {
    Write-Host "Junction failed -- falling back to copy"
    New-Item -ItemType Directory -Force -Path $linkPath | Out-Null
    Copy-Item -LiteralPath "$WorkRoot\*" -Destination $linkPath -Recurse -Force
    Write-Host "Copied work/ -> $linkPath (fallback)"
}
