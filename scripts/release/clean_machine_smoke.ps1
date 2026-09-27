<#
  Runs inside a fresh Windows Sandbox/VM. It verifies the immutable package, copies it to a
  writable user-owned folder, records prerequisite state, and proves that the editor stays alive.
  Manual UI/module checks remain explicit and are never inferred from process startup.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$PackageRoot,
  [Parameter(Mandatory = $true)][string]$EvidenceRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $EvidenceRoot | Out-Null
$errors = [Collections.Generic.List[string]]::new()
$warnings = [Collections.Generic.List[string]]::new()

function Has-Command([string]$Name) { return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue) }

$manifestPath = Join-Path $PackageRoot 'release-manifest.json'
try { $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -ErrorAction Stop }
catch { throw "Invalid release manifest: $($_.Exception.Message)" }

$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in @($manifest.files)) {
  $relative = ([string]$entry.path).Replace('/', [IO.Path]::DirectorySeparatorChar)
  $path = Join-Path $PackageRoot $relative
  [void]$seen.Add(([string]$entry.path).Replace('\','/'))
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $errors.Add("MANIFEST_FILE_MISSING:$($entry.path)"); continue }
  $item = Get-Item -LiteralPath $path
  if ($item.Length -ne [long]$entry.bytes) { $errors.Add("SIZE_MISMATCH:$($entry.path)"); continue }
  $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($hash -cne [string]$entry.sha256) { $errors.Add("HASH_MISMATCH:$($entry.path)") }
}
foreach ($file in Get-ChildItem -LiteralPath $PackageRoot -Recurse -File) {
  $relative = $file.FullName.Substring($PackageRoot.Length).TrimStart('\','/').Replace('\','/')
  if ($relative -ne 'release-manifest.json' -and -not $seen.Contains($relative)) { $errors.Add("EXTRA_FILE:$relative") }
}

$verifyOnlyExit = $null
$installer = Join-Path $PackageRoot 'prerequisites\install.ps1'
if (Test-Path -LiteralPath $installer -PathType Leaf) {
  & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer -VerifyOnly *> (Join-Path $EvidenceRoot 'prerequisites-verify.log')
  $verifyOnlyExit = $LASTEXITCODE
  if ($verifyOnlyExit -ne 0) { $errors.Add("PREREQUISITES_VERIFY_EXIT:$verifyOnlyExit") }
} else {
  $errors.Add('PREREQUISITES_INSTALLER_MISSING')
}

$localPackage = Join-Path $env:LOCALAPPDATA 'FFXModStudioCleanSmoke\package'
New-Item -ItemType Directory -Force -Path $localPackage | Out-Null
Get-ChildItem -LiteralPath $PackageRoot -Force | Copy-Item -Destination $localPackage -Recurse -Force
$exe = Join-Path $localPackage 'FFXProjectEditor.exe'
$startupState = 'NOT_RUN'
$exitCode = $null
if ($errors.Count -eq 0) {
  try {
    $process = Start-Process -FilePath $exe -WorkingDirectory $localPackage -PassThru
    Start-Sleep -Seconds 12
    $process.Refresh()
    if ($process.HasExited) {
      $startupState = 'EXITED_EARLY'
      $exitCode = $process.ExitCode
      $errors.Add("EDITOR_EXITED_EARLY:$exitCode")
    } else {
      $startupState = 'RUNNING_AFTER_12_SECONDS'
      $warnings.Add('MANUAL_UI_AND_MODULE_SMOKE_REQUIRED')
    }
  } catch {
    $startupState = 'START_FAILED'
    $errors.Add('EDITOR_START_FAILED:' + $_.Exception.GetType().Name)
  }
}

$result = [ordered]@{
  schemaVersion = 1
  generatedUtc = [DateTime]::UtcNow.ToString('o')
  os = [Environment]::OSVersion.VersionString
  is64BitOperatingSystem = [Environment]::Is64BitOperatingSystem
  user = [Environment]::UserName
  packageManifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
  sourceDirty = $manifest.sourceDirty
  externalCommandsInitiallyPresent = [ordered]@{
    dotnet = (Has-Command 'dotnet')
    java = (Has-Command 'java')
    node = (Has-Command 'node')
    python = (Has-Command 'python')
  }
  prerequisitesVerifyExit = $verifyOnlyExit
  startupState = $startupState
  editorExitCode = $exitCode
  errors = [string[]]$errors
  warnings = [string[]]$warnings
  manualStatus = 'NOT_RUN'
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $EvidenceRoot 'CLEAN_MACHINE_AUTOMATION.json') -Encoding UTF8

@'
FFX MOD STUDIO CLEAN-MACHINE MANUAL CHECKLIST

[ ] Confirm the editor window renders correctly and remains responsive.
[ ] Open an empty workspace and a temporary extracted master workspace.
[ ] Confirm missing game/NoClip/runtime-hook capabilities show actionable diagnostics, not crashes.
[ ] Run prerequisites\install.ps1 -Accept, approve UAC only for the two Microsoft installers, then restart the editor.
[ ] Open Map, Model, Magic and NoClip viewers; verify bundled UI loads offline and missing user game data is explained.
[ ] Configure user-owned NoClip data and verify /data is local-only; no CDN/network request or repair occurs.
[ ] Import one approved music track from a user-selected FSB; verify output is under LocalAppData and source remains unchanged.
[ ] Launch bundled FFXED with the private Java runtime.
[ ] Create/edit/save only temporary copies; verify backups and no write occurs under the application folder.
[ ] Close the editor and record PASS/FAIL plus screenshots/logs in this evidence folder.

Automation startup is not a clean-machine PASS until every applicable manual item is evidenced.
'@ | Set-Content -LiteralPath (Join-Path $EvidenceRoot 'MANUAL_CHECKLIST.txt') -Encoding UTF8

if ($errors.Count -gt 0) { exit 1 }
exit 0
