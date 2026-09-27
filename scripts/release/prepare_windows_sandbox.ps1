<#
  Creates an offline Windows Sandbox configuration for FFX Mod Studio clean-machine smoke.
  This script never enables Windows features, starts a VM, installs prerequisites, or mutates the
  package unless -Open is explicitly supplied by a human operator.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$PackageRoot,
  [string]$EvidenceRoot,
  [switch]$Open
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$package = (Resolve-Path -LiteralPath $PackageRoot).Path
if (-not (Test-Path -LiteralPath (Join-Path $package 'FFXProjectEditor.exe') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $package 'release-manifest.json') -PathType Leaf)) {
  throw 'PackageRoot must contain FFXProjectEditor.exe and release-manifest.json.'
}

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
  $EvidenceRoot = Join-Path (Split-Path $package -Parent) ('clean-machine-evidence-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$evidence = [IO.Path]::GetFullPath($EvidenceRoot)
New-Item -ItemType Directory -Force -Path $evidence | Out-Null

$harness = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$smoke = Join-Path $harness 'clean_machine_smoke.ps1'
if (-not (Test-Path -LiteralPath $smoke -PathType Leaf)) { throw "Smoke harness missing: $smoke" }

function Xml([string]$Value) { return [Security.SecurityElement]::Escape($Value) }

$configuration = @"
<Configuration>
  <VGpu>Disable</VGpu>
  <Networking>Disable</Networking>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <PrinterRedirection>Disable</PrinterRedirection>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$(Xml $package)</HostFolder>
      <SandboxFolder>C:\FFXPackage</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$(Xml $harness)</HostFolder>
      <SandboxFolder>C:\FFXHarness</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$(Xml $evidence)</HostFolder>
      <SandboxFolder>C:\FFXEvidence</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\FFXHarness\clean_machine_smoke.ps1 -PackageRoot C:\FFXPackage -EvidenceRoot C:\FFXEvidence</Command>
  </LogonCommand>
</Configuration>
"@

$wsbPath = Join-Path $evidence 'FFX-Mod-Studio-Win11-Offline.wsb'
[IO.File]::WriteAllText($wsbPath, $configuration, [Text.UTF8Encoding]::new($false))

$manifestHash = (Get-FileHash -LiteralPath (Join-Path $package 'release-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()
$prep = [ordered]@{
  schemaVersion = 1
  preparedUtc = [DateTime]::UtcNow.ToString('o')
  packageRoot = $package
  manifestSha256 = $manifestHash
  evidenceRoot = $evidence
  sandboxConfig = $wsbPath
  networking = 'disabled'
  packageMapping = 'read-only'
  executionStatus = 'NOT_RUN_EXTERNAL'
}
$prep | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'PREPARATION.json') -Encoding UTF8

Write-Output "SANDBOX_CONFIG=$wsbPath"
Write-Output "EVIDENCE_ROOT=$evidence"
Write-Output "MANIFEST_SHA256=$manifestHash"

if ($Open) {
  $sandboxExe = Join-Path $env:SystemRoot 'System32\WindowsSandbox.exe'
  if (-not (Test-Path -LiteralPath $sandboxExe -PathType Leaf)) {
    throw 'Windows Sandbox is not available. Enable it manually (may require elevation/reboot) or use a clean Win11 VM.'
  }
  Start-Process -FilePath $sandboxExe -ArgumentList @($wsbPath)
}
