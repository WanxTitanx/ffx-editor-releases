<#
  FFX Mod Studio — preflight_release.ps1 (§12.2/§12.3). Read-only.
  Verifica git/dotnet/Get-Volume (LASTEXITCODE + try/catch). -Json emite UM objeto JSON puro
  (nada além no stdout); o modo texto emite Format-List + PREFLIGHT_OK/NOT_READY.
  Authenticode: AUTHENTICODE_DEFERRED_BY_OWNER_DECISION (não bloqueia Candidate/Release).
  Release permanece fail-closed por RELEASE_NOT_YET_VALIDATED_VERSION_GATES.
  COMMAND_ERRORS tem prioridade sobre GIT_DIRTY/version-gates.
  Exit: 0 = ready · 1 = não ready. -RepoRoot permite fixture limpa em temp repo (default: repo canônico).
#>
param(
  [ValidateSet('Diagnostic', 'Candidate', 'Release')][string]$Mode = 'Diagnostic',
  [switch]$Json,
  [string]$RepoRoot
)
$ErrorActionPreference = 'Stop'
$errors = New-Object System.Collections.Generic.List[string]

if ($RepoRoot) {
  $repo = (Resolve-Path -LiteralPath $RepoRoot -ErrorAction SilentlyContinue | Select-Object -First 1).Path
  if (-not $repo) { $repo = $RepoRoot }
} else {
  $repo = (Resolve-Path (Join-Path $PSScriptRoot '\..\..')).Path
}
if (-not (Test-Path (Join-Path $repo '.git'))) { $errors.Add("RepoRoot não é um repositório Git: $repo") }

$branch = ''; $head = ''; $dirty = -1
try { $branch = (git -C $repo branch --show-current 2>$null); if ($LASTEXITCODE -ne 0) { $errors.Add("git branch exit=$LASTEXITCODE") } } catch { $errors.Add('git branch threw') }
try { $head = (git -C $repo rev-parse HEAD 2>$null); if ($LASTEXITCODE -ne 0) { $errors.Add("git rev-parse exit=$LASTEXITCODE") } } catch { $errors.Add('git rev-parse threw') }
try { if (Test-Path (Join-Path $repo '.git')) { $dirty = @(git -C $repo status --porcelain).Count; if ($LASTEXITCODE -ne 0) { $errors.Add("git status exit=$LASTEXITCODE") } } } catch { $errors.Add('git status threw') }
try { $null = dotnet --list-sdks 2>$null; if ($LASTEXITCODE -ne 0) { $errors.Add("dotnet --list-sdks exit=$LASTEXITCODE") } } catch { $errors.Add('dotnet --list-sdks threw') }
$volCount = 0
try { $volCount = @(Get-Volume | Where-Object DriveLetter | Where-Object { $_.SizeRemaining -gt 0 }).Count } catch { $errors.Add("Get-Volume threw: $($_.Exception.Message)") }

$signingState = 'NOT_SIGNED_DEFERRED_BY_OWNER_DECISION'

$reason = $null
if ($errors.Count -gt 0) { $reason = 'COMMAND_ERRORS' }
elseif ($dirty -gt 0 -and $Mode -ne 'Diagnostic') { $reason = 'GIT_DIRTY' }
elseif ($Mode -eq 'Release') { $reason = 'RELEASE_NOT_YET_VALIDATED_VERSION_GATES' }

$r = [ordered]@{
  mode = $Mode; repo = $repo; branch = $branch; head = $head
  dirtyFiles = $dirty; sourceDirty = ($dirty -gt 0); disksReported = $volCount
  signingState = $signingState
  ready = ($null -eq $reason)
  reason = $reason
  errors = @($errors)
}

if ($Json) {
  $r | ConvertTo-Json -Depth 3
} else {
  $r | Format-List
  if ($null -eq $reason) { Write-Output "PREFLIGHT_OK mode=$Mode" } else { Write-Output "PREFLIGHT_NOT_READY mode=$Mode reason=$reason" }
}

if ($null -eq $reason) { exit 0 }
exit 1