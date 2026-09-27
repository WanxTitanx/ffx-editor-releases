<#
  FFX Mod Studio offline prerequisite installer.

  This script is intentionally user-initiated and fail-closed. It verifies the exact SHA-256 and
  Microsoft Authenticode publisher recorded in the packaged runtime contract before launching
  either installer. It never downloads content and never changes the editor/game files.
#>
[CmdletBinding()]
param([switch]$Accept, [switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$packageRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $root)).Path
$contractPath = Join-Path $root 'windows-runtime-prerequisites.json'
if (-not (Test-Path -LiteralPath $contractPath -PathType Leaf)) {
  throw 'Runtime prerequisite contract is missing.'
}
$contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json

if (-not $Accept -and -not $VerifyOnly) {
  $answer = Read-Host 'Install the bundled Microsoft WebView2 and Visual C++ 2013 x64 prerequisites? Type INSTALL to continue'
  if ($answer -cne 'INSTALL') { Write-Output 'Cancelled. No installer was started.'; exit 1 }
}

function Resolve-CheckedInstaller($Spec, [string]$Label) {
  $relative = ([string]$Spec.path).Replace('/', [IO.Path]::DirectorySeparatorChar)
  $candidate = [IO.Path]::GetFullPath((Join-Path $packageRoot $relative))
  $prefix = $packageRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
  if (-not $candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "$Label path escapes the prerequisites directory."
  }
  if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "$Label installer is missing." }
  $actualHash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -cne ([string]$Spec.sha256).ToLowerInvariant()) { throw "$Label SHA-256 mismatch." }
  $signature = Get-AuthenticodeSignature -LiteralPath $candidate
  if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
      [string]$signature.SignerCertificate.Subject -notmatch 'Microsoft') {
    throw "$Label does not have the required valid Microsoft signature."
  }
  return $candidate
}

function Invoke-Installer([string]$Path, [string[]]$Arguments, [string]$Label) {
  $process = Start-Process -FilePath $Path -ArgumentList $Arguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru
  if ($process.ExitCode -notin @(0, 1638, 3010)) { throw "$Label failed with exit code $($process.ExitCode)." }
  Write-Output "$Label completed (exit $($process.ExitCode))."
}

$webSpec = $contract.components.webView2Evergreen.offlineInstaller
$vcSpec = $contract.components.vcpp2013X64.installer
$webInstaller = Resolve-CheckedInstaller $webSpec 'WebView2 Evergreen x64'
$vcInstaller = Resolve-CheckedInstaller $vcSpec 'Visual C++ 2013 x64'

if ($VerifyOnly) {
  Write-Output 'PREREQUISITES_VERIFIED: hashes and Microsoft signatures are valid.'
  exit 0
}

Invoke-Installer $webInstaller @('/silent', '/install') 'WebView2 Evergreen x64'
Invoke-Installer $vcInstaller @('/install', '/quiet', '/norestart') 'Visual C++ 2013 x64'
Write-Output 'FFX Mod Studio Windows prerequisites are installed. A reboot is recommended only if an installer requested it.'
