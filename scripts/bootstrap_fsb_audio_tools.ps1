# Bootstrap vgmstream + fsbext + fsbankcl for Custom Battle SFX (Tier 2).
# Lane: Jarvis-MAGIC
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [switch]$SkipVgmStream,
    [switch]$SkipFsbExt,
    [switch]$InstallFsbankCl,
    [string]$FsbankClSource = ""
)

$ErrorActionPreference = "Stop"
$vgmDir = Join-Path $RepoRoot "tools\vgmstream"
$fsbDir = Join-Path $RepoRoot "tools\fsbext"
$bankClDir = Join-Path $RepoRoot "tools\fsbankcl"
New-Item -ItemType Directory -Force -Path $vgmDir, $fsbDir, $bankClDir | Out-Null

function Write-Status($msg) { Write-Host $msg }

function Find-FsbankClOnDisk {
    param([string[]]$ExtraRoots = @())
    $hits = @()
    $roots = @(
        "C:\Program Files (x86)\FMOD SoundSystem",
        "C:\Program Files\FMOD SoundSystem",
        "D:\FMOD",
        "C:\FMOD"
    ) + $ExtraRoots
    foreach ($r in $roots) {
        if (-not (Test-Path $r)) { continue }
        $hits += Get-ChildItem -Path $r -Filter "fsbankcl.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName
        $hits += Get-ChildItem -Path $r -Filter "fsbankexcl.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName
    }
    return $hits | Select-Object -First 1
}

function Install-FsbankClBundle {
    param([string]$SourceExe, [string]$DestDir)
    $base = if ($SourceExe -match 'fsbankexcl\.exe$') { "fsbankexcl.exe" } else { "fsbankcl.exe" }
    Copy-Item $SourceExe (Join-Path $DestDir $base) -Force
    if ($base -ne "fsbankcl.exe") {
        Copy-Item $SourceExe (Join-Path $DestDir "fsbankcl.exe") -Force
    }
    $srcDir = Split-Path $SourceExe -Parent
    Get-ChildItem -Path $srcDir -Filter "*.dll" -ErrorAction SilentlyContinue | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $DestDir $_.Name) -Force
    }
}

if (-not $SkipVgmStream) {
    $vgmCli = Join-Path $vgmDir "vgmstream-cli.exe"
    $needsVgm = -not (Test-Path $vgmCli)
    if (-not $needsVgm) {
        $dllCount = (Get-ChildItem -Path $vgmDir -Filter "*.dll" -ErrorAction SilentlyContinue).Count
        if ($dllCount -lt 1) { $needsVgm = $true }
    }
    if ($needsVgm) {
        Write-Status "Downloading vgmstream (latest GitHub release)..."
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/vgmstream/vgmstream/releases/latest" -Headers @{ "User-Agent" = "ffx-editor-bootstrap" }
        $asset = $release.assets | Where-Object { $_.name -match "vgmstream.*win.*\.zip$" -or $_.name -match "vgmstream.*win64.*\.zip$" } | Select-Object -First 1
        if (-not $asset) { throw "vgmstream Windows zip not found in latest release." }
        $zip = Join-Path $env:TEMP "vgmstream-bootstrap.zip"
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -Headers @{ "User-Agent" = "ffx-editor-bootstrap" }
        $extract = Join-Path $env:TEMP "vgmstream-extract"
        if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
        Expand-Archive -Path $zip -DestinationPath $extract -Force
        $found = Get-ChildItem -Path $extract -Recurse -Filter "vgmstream-cli.exe" | Select-Object -First 1
        if (-not $found) { throw "vgmstream-cli.exe not found inside zip." }
        $srcDir = $found.Directory.FullName
        Get-ChildItem -Path $srcDir -File | ForEach-Object { Copy-Item $_.FullName (Join-Path $vgmDir $_.Name) -Force }
        Write-Status "Installed vgmstream bundle: $vgmDir ($((Get-ChildItem $vgmDir -File).Count) files)"
    } else {
        Write-Status "vgmstream already present: $vgmCli"
    }
}

if (-not $SkipFsbExt) {
    $fsbCli = Join-Path $fsbDir "fsbext.exe"
    if (-not (Test-Path $fsbCli)) {
        Write-Status "Downloading fsbext from aluigi..."
        $zip = Join-Path $env:TEMP "fsbext.zip"
        Invoke-WebRequest -Uri "https://aluigi.altervista.org/papers/fsbext.zip" -OutFile $zip
        $extract = Join-Path $env:TEMP "fsbext-extract"
        if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
        Expand-Archive -Path $zip -DestinationPath $extract -Force
        $found = Get-ChildItem -Path $extract -Recurse -Filter "fsbext.exe" | Select-Object -First 1
        if (-not $found) { $found = Get-ChildItem -Path $extract -Recurse -Filter "fsbext" | Select-Object -First 1 }
        if (-not $found) { throw "fsbext executable not found inside zip." }
        Copy-Item $found.FullName $fsbCli -Force
        Write-Status "Installed: $fsbCli"
    } else {
        Write-Status "fsbext already present: $fsbCli"
    }
}

$bankCli = Join-Path $bankClDir "fsbankcl.exe"
if ($InstallFsbankCl -or -not (Test-Path $bankCli)) {
    if (Test-Path $bankCli) {
        Write-Status "fsbankcl already present: $bankCli"
    } elseif ($FsbankClSource -and (Test-Path $FsbankClSource)) {
        Install-FsbankClBundle -SourceExe $FsbankClSource -DestDir $bankClDir
        Write-Status "Installed fsbankcl from -FsbankClSource: $bankCli"
    } else {
        $sdkHit = Find-FsbankClOnDisk
        if ($sdkHit) {
            Install-FsbankClBundle -SourceExe $sdkHit -DestDir $bankClDir
            Write-Status "Installed fsbankcl from SDK: $bankCli (source: $sdkHit)"
        } else {
            Write-Status "fsbankcl not found on disk. Editor: Battle SFX -> Import fsbankcl, or install FMOD Programmer API."
        }
    }
}

$fsbankOk = Test-Path $bankCli
Write-Status "VERDICT: bootstrap complete. fsbankcl=$fsbankOk"
