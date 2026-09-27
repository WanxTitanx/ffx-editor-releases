# Install offline tools for Field Scout heavy ingest (texconv + folder layout).
param(
    [string]$ToolsRoot = (Join-Path $PSScriptRoot "tools")
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $ToolsRoot | Out-Null

$texconv = Join-Path $ToolsRoot "texconv.exe"
if (-not (Test-Path $texconv)) {
    Write-Host "Installing texconv via winget..."
    winget install --id Microsoft.DirectXTex.Texconv --accept-package-agreements --accept-source-agreements 2>$null
    $wingetPath = Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages\Microsoft.DirectXTex.Texconv_Microsoft.Winget.Source_8wekyb3d8bbwe\texconv.exe"
    if (Test-Path $wingetPath) {
        Copy-Item -LiteralPath $wingetPath -Destination $texconv -Force
    } else {
        Write-Host "winget path not found, trying GitHub release..."
        $url = "https://github.com/microsoft/DirectXTex/releases/download/oct2025/texconv.exe"
        Invoke-WebRequest -Uri $url -OutFile $texconv -UseBasicParsing
    }
    if (-not (Test-Path $texconv)) { throw "Failed to install texconv" }
    Write-Host "texconv installed: $texconv"
} else {
    Write-Host "texconv already present: $texconv"
}

Write-Host ""
Write-Host "Field Scout tools ready under: $ToolsRoot"
Write-Host "  texconv.exe  - DDS to PNG batch conversion"
