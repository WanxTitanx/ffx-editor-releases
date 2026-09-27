<#
  Classifies Candidate verifier output without weakening package validation.

  Only the four versioned exact-pin drift classes are reviewable. Infrastructure failures,
  empty error lists, unknown validation classes, malformed JSON, and non-Candidate verifier
  results remain fail-closed.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$VerifierJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$allowedPrefixes = [string[]]@(
  'ACCEPTED_RISK_PIN_MISMATCH:',
  'NOCLIP_PIN_MISMATCH:',
  'RUNTIME_PIN_MISMATCH:',
  'SCAN_ALLOWLIST_PIN_MISMATCH:'
)

function Write-Invalid([string]$Reason) {
  Write-Output ('CONTRACT_DRIFT_INVALID reason=' + $Reason)
  exit 2
}

function Write-Blocked([string]$Reason, [string]$Detail = '') {
  $suffix = if ([string]::IsNullOrWhiteSpace($Detail)) { '' } else { ' detail=' + $Detail }
  Write-Output ('CONTRACT_DRIFT_BLOCKED reason=' + $Reason + $suffix)
  exit 1
}

if (-not (Test-Path -LiteralPath $VerifierJson -PathType Leaf)) { Write-Invalid 'VERIFIER_JSON_MISSING' }
try {
  $result = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $VerifierJson).Path) |
    ConvertFrom-Json -NoEnumerate -ErrorAction Stop
} catch {
  Write-Invalid 'VERIFIER_JSON_MALFORMED'
}

if ($null -eq $result -or $result -isnot [pscustomobject]) { Write-Invalid 'VERIFIER_TOP_LEVEL_NOT_OBJECT' }
foreach ($property in @('schemaVersion', 'status', 'exitCode', 'mode', 'errors', 'infrastructureErrors')) {
  if ($null -eq $result.PSObject.Properties[$property]) { Write-Invalid ('VERIFIER_PROPERTY_MISSING_' + $property.ToUpperInvariant()) }
}
if ($result.schemaVersion -ne 2) { Write-Invalid 'VERIFIER_SCHEMA_UNSUPPORTED' }
if ($result.errors -is [string] -or $result.errors -isnot [Collections.IList]) { Write-Invalid 'VERIFIER_ERRORS_NOT_ARRAY' }
if ($result.infrastructureErrors -is [string] -or $result.infrastructureErrors -isnot [Collections.IList]) { Write-Invalid 'VERIFIER_INFRASTRUCTURE_ERRORS_NOT_ARRAY' }
if ([string]$result.mode -cne 'Candidate') { Write-Blocked 'NOT_CANDIDATE_RESULT' ([string]$result.mode) }

$infrastructureErrors = [string[]]@($result.infrastructureErrors | ForEach-Object { [string]$_ })
if ($infrastructureErrors.Count -gt 0) {
  Write-Blocked 'INFRASTRUCTURE_ERROR' ([string]::Join('|', $infrastructureErrors))
}
if ([int]$result.exitCode -ne 1 -or [string]$result.status -cne 'rejected') {
  Write-Blocked 'NOT_REJECTED_VALIDATION_RESULT' ('status={0},exitCode={1}' -f $result.status, $result.exitCode)
}

$errors = [string[]]@($result.errors | ForEach-Object { [string]$_ })
if ($errors.Count -eq 0) { Write-Blocked 'NO_VALIDATION_ERRORS' }
foreach ($errorMessage in $errors) {
  $allowed = $false
  foreach ($prefix in $allowedPrefixes) {
    if ($errorMessage.StartsWith($prefix, [StringComparison]::Ordinal)) { $allowed = $true; break }
  }
  if (-not $allowed) { Write-Blocked 'FORBIDDEN_VALIDATION_ERROR' $errorMessage }
}

Write-Output 'CONTRACT_DRIFT_REVIEWABLE'
exit 0
