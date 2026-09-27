<#
  Deterministic self-test for classify_contract_drift.ps1.

  The classifier is intentionally narrow: only four versioned pin-drift prefixes may be reviewed,
  and malformed verifier output or infrastructure failures must never be downgraded.
#>
$ErrorActionPreference = 'Stop'
$classifier = Join-Path $PSScriptRoot 'classify_contract_drift.ps1'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('ffx-contract-drift-test-' + [Guid]::NewGuid().ToString('N'))
$fails = [Collections.Generic.List[string]]::new()
$runs = 0
New-Item -ItemType Directory -Path $tmp | Out-Null

function Assert([bool]$Condition, [string]$Message) {
  $script:runs++
  if (-not $Condition) { $script:fails.Add($Message) }
}

function Write-VerifierResult(
  [string]$Path,
  [string[]]$Errors,
  [string[]]$InfrastructureErrors,
  [int]$ExitCode = 1,
  [string]$Status = 'rejected') {
  [ordered]@{
    schemaVersion = 2
    status = $Status
    exitCode = $ExitCode
    phase = 'policy-contracts'
    mode = 'Candidate'
    errors = [string[]]$Errors
    infrastructureErrors = [string[]]$InfrastructureErrors
    warnings = [string[]]@()
  } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Path -Encoding utf8NoBOM
}

function Invoke-Classifier([string]$Path) {
  $output = (& pwsh -NoProfile -File $classifier -VerifierJson $Path 2>&1) | Out-String
  return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output.Trim() }
}

try {
  $empty = Join-Path $tmp 'empty.json'
  Write-VerifierResult $empty @() @()
  $result = Invoke-Classifier $empty
  Assert ($result.ExitCode -eq 1) 'empty validation-error list must remain blocked'
  Assert ($result.Output -match '^CONTRACT_DRIFT_BLOCKED\b') 'empty list must emit blocked marker'

  $allowedPrefixes = @(
    'ACCEPTED_RISK_PIN_MISMATCH:',
    'NOCLIP_PIN_MISMATCH:',
    'RUNTIME_PIN_MISMATCH:',
    'SCAN_ALLOWLIST_PIN_MISMATCH:'
  )
  foreach ($prefix in $allowedPrefixes) {
    $path = Join-Path $tmp (($prefix.TrimEnd(':')) + '.json')
    Write-VerifierResult $path @($prefix + 'fixture/path') @()
    $result = Invoke-Classifier $path
    Assert ($result.ExitCode -eq 0) "$prefix must be reviewable"
    Assert ($result.Output -ceq 'CONTRACT_DRIFT_REVIEWABLE') "$prefix must emit the exact reviewable marker"
  }

  $allAllowed = Join-Path $tmp 'all-allowed.json'
  Write-VerifierResult $allAllowed @($allowedPrefixes | ForEach-Object { $_ + 'fixture/path' }) @()
  $result = Invoke-Classifier $allAllowed
  Assert ($result.ExitCode -eq 0 -and $result.Output -ceq 'CONTRACT_DRIFT_REVIEWABLE') 'all four allowed classes may be reviewed together'

  $mixed = Join-Path $tmp 'mixed.json'
  Write-VerifierResult $mixed @('RUNTIME_PIN_MISMATCH:runtime/java', 'MISSING_REQUIRED_FILE:FFXProjectEditor.exe') @()
  $result = Invoke-Classifier $mixed
  Assert ($result.ExitCode -eq 1) 'allowed drift mixed with forbidden validation error must block'
  Assert ($result.Output -match 'MISSING_REQUIRED_FILE') 'blocked marker must identify the forbidden validation class'

  $infrastructure = Join-Path $tmp 'infrastructure.json'
  Write-VerifierResult $infrastructure @('NOCLIP_PIN_MISMATCH:viewers/noclip') @('POLICY_PACKAGE_ALLOWLIST_JSON_INVALID') 2 'infrastructure-error'
  $result = Invoke-Classifier $infrastructure
  Assert ($result.ExitCode -eq 1) 'infrastructure errors must never be reviewable'
  Assert ($result.Output -match 'INFRASTRUCTURE_ERROR') 'infrastructure block must have an explicit reason'

  $malformed = Join-Path $tmp 'malformed.json'
  Set-Content -LiteralPath $malformed -Value '{ invalid' -Encoding utf8NoBOM
  $result = Invoke-Classifier $malformed
  Assert ($result.ExitCode -eq 2) 'malformed verifier JSON must be an infrastructure/usage failure'
  Assert ($result.Output -match '^CONTRACT_DRIFT_INVALID\b') 'malformed JSON must emit invalid marker'
} finally {
  $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
  $resolvedTmp = [IO.Path]::GetFullPath($tmp)
  if ($resolvedTmp.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedTmp)) {
    Remove-Item -LiteralPath $resolvedTmp -Recurse -Force
  }
}

if ($fails.Count -gt 0) {
  $fails | ForEach-Object { Write-Output ('ASSERT-FAIL: ' + $_) }
  Write-Output ("CONTRACT_DRIFT_TEST_FAIL assertions={0} failures={1}" -f $runs, $fails.Count)
  exit 1
}
Write-Output ("CONTRACT_DRIFT_TEST_OK assertions={0}" -f $runs)
exit 0
