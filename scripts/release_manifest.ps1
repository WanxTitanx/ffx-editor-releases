# release_manifest.ps1 - P3-L4: gera o manifesto de release verificavel do FFX Project Editor.
#
# Contrato (docs/ai/P3_RUNTIME_CONTRACTS_2026-07-31.md sec. 4 / prompt P3 L418-425):
#   - versao real lida do FFXProjectEditor.csproj (os 4 campos - fonte de verdade);
#   - SHA-256 de cada artefato de build (exe/dll) + arquivos .sha256 ao lado;
#   - changelog verificavel: ultima entrada do CHANGELOG.md incluida no manifesto;
#   - commit git atual + aviso se o worktree estiver sujo;
#   - assinatura: DOCUMENTADA COMO PLANO (campo "signing": "not-signed" - nunca declarar feita).
#
# Uso:
#   powershell -File scripts/release_manifest.ps1 -BuildDir work/_build_editor_check -OutFile work/release-manifest.json
#   powershell -File scripts/release_manifest.ps1 -BuildDir "D:\FFX Mods\FFXProjectEditor-2.198.2.0"
#
# Read-only: nao altera artefatos, nao instala, nao assina. Falha com mensagem acionavel (exit 1).
# IMPORTANTE: arquivo em ASCII puro (sem acentos) - powershell.exe 5.1 le PS1 sem BOM como ANSI.

param(
    [Parameter(Mandatory = $true)][string]$BuildDir,
    [string]$OutFile = "work/release-manifest.json",
    [string]$CsprojPath = "FFXProjectEditor/FFXProjectEditor.csproj",
    [string]$ChangelogPath = "CHANGELOG.md"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $BuildDir)) {
    Write-Host "ERRO: BuildDir nao encontrado: $BuildDir" -ForegroundColor Red
    Write-Host "Acao: rode primeiro o build, ex.: dotnet build FFXProjectEditor/FFXProjectEditor.csproj -c Release -o <BuildDir>" -ForegroundColor Yellow
    exit 1
}

# ---- 1. Versao (fonte de verdade: csproj) -------------------------------------------------
if (-not (Test-Path $CsprojPath)) {
    Write-Host "ERRO: csproj nao encontrado: $CsprojPath" -ForegroundColor Red
    exit 1
}
$csproj = Get-Content $CsprojPath -Raw
$version = [regex]::Match($csproj, '<Version>([^<]+)</Version>').Groups[1].Value
if ([string]::IsNullOrWhiteSpace($version)) {
    Write-Host "ERRO: nao foi possivel ler <Version> de $CsprojPath" -ForegroundColor Red
    exit 1
}

# ---- 2. Artefatos + SHA-256 ----------------------------------------------------------------
$artifacts = Get-ChildItem -Path $BuildDir -File -Include *.exe, *.dll -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
    Sort-Object FullName

if ($artifacts.Count -eq 0) {
    Write-Host "ERRO: nenhum artefato (.exe/.dll) em $BuildDir" -ForegroundColor Red
    exit 1
}

$files = @()
$buildRoot = (Resolve-Path $BuildDir).Path

function Get-Sha256Hex([string]$path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $fs = [System.IO.File]::OpenRead($path)
        try {
            $hash = $sha.ComputeHash($fs)
            return ([System.BitConverter]::ToString($hash)).Replace('-', '').ToLowerInvariant()
        } finally { $fs.Dispose() }
    } finally { $sha.Dispose() }
}

foreach ($f in $artifacts) {
    $hash = Get-Sha256Hex $f.FullName
    $rel = $f.FullName.Substring($buildRoot.Length).TrimStart('\', '/')
    $files += [PSCustomObject]@{
        path   = $rel
        size   = $f.Length
        sha256 = $hash
        kind   = if ($f.Extension -eq '.exe') { 'executable' } else { 'library' }
    }
    # arquivo .sha256 ao lado (formato: <hash>  <nome>)
    Set-Content -Path "$($f.FullName).sha256" -Value "$hash  $($f.Name)" -Encoding Ascii
}

# ---- 3. Changelog verificavel: primeira entrada de versao do [Unreleased] -------------------
$changelogEntry = ""
if (Test-Path $ChangelogPath) {
    $changelogEntry = (Get-Content $ChangelogPath -Raw) -split "`n" |
        Where-Object { $_ -match '^\s*- \*\*`v' } | Select-Object -First 1
    $changelogEntry = $changelogEntry.Trim()
}

# ---- 4. Git state --------------------------------------------------------------------------
$gitCommit = ""
$gitDirty = $false
if (Get-Command git -ErrorAction SilentlyContinue) {
    $gitCommit = (git rev-parse --short HEAD 2>$null)
    $gitDirty = -not [string]::IsNullOrWhiteSpace((git status --porcelain 2>$null))
}

# ---- 5. Manifesto --------------------------------------------------------------------------
$manifest = [PSCustomObject]@{
    schemaVersion   = 1
    product         = "FFX Project Editor"
    version         = $version
    generatedAtUtc  = (Get-Date).ToUniversalTime().ToString("o")
    buildDir        = $buildRoot
    gitCommit       = $gitCommit
    gitDirty        = $gitDirty
    artifactCount   = $files.Count
    artifacts       = $files
    changelog       = $changelogEntry
    signing         = "not-signed"   # DOCUMENTADO COMO PLANO - nunca declarar feito sem executar
    notes           = "Assinatura: plano documentado em docs/ai/P3_RELEASE_2026-07-31.md. Verificacao: powershell Get-FileHash."
}

$outDir = Split-Path -Parent $OutFile
if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $OutFile -Encoding Utf8

Write-Host "Manifesto de release gerado: $OutFile" -ForegroundColor Green
Write-Host "  versao  : $version"
Write-Host "  artefatos: $($files.Count) (SHA-256 por arquivo + .sha256 ao lado)"
Write-Host "  git     : $gitCommit $(if ($gitDirty) { '(worktree SUJO - manifesto marca gitDirty=true)' } else { '(worktree limpo)' })"
if ($gitDirty) { Write-Host "  AVISO: worktree com mudancas nao commitadas - verifique antes de distribuir." -ForegroundColor Yellow }

