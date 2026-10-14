<#
  Deterministic self-test for smoke_candidate_liveness.ps1.

  Windows inbox executables are copied as inert fixtures. One child survives the deadline, one exits
  early, and an independently started same-name process proves cleanup remains PID-scoped.
#>
$ErrorActionPreference = 'Stop'
$smoke = Join-Path $PSScriptRoot 'smoke_candidate_liveness.ps1'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('ffx-liveness-test-' + [Guid]::NewGuid().ToString('N'))
$package = Join-Path $tmp 'package'
$fails = [Collections.Generic.List[string]]::new()
$runs = 0
$sentinel = $null
New-Item -ItemType Directory -Path $package -Force | Out-Null

function Assert([bool]$Condition, [string]$Message) {
  $script:runs++
  if (-not $Condition) { $script:fails.Add($Message) }
}

function Wait-ForPath([string]$Path, [int]$Milliseconds) {
  $deadline = [datetime]::UtcNow.AddMilliseconds($Milliseconds)
  while ([datetime]::UtcNow -lt $deadline) {
    if (Test-Path -LiteralPath $Path -PathType Leaf) { return $true }
    Start-Sleep -Milliseconds 50
  }
  return (Test-Path -LiteralPath $Path -PathType Leaf)
}

function Get-PackageFingerprint {
  $rows = Get-ChildItem -LiteralPath $package -Recurse -File -Force | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($package, $_.FullName).Replace('\', '/')
    '{0}|{1}|{2}' -f $relative, $_.Length, (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
  }
  return [string]::Join("`n", @($rows | Sort-Object -CaseSensitive))
}

try {
  $fixtureExe = Join-Path $package 'liveness-helper.exe'
  Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\ping.exe') -Destination $fixtureExe
  $earlyExe = Join-Path $package 'early-helper.exe'
  Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\cmd.exe') -Destination $earlyExe
  [ordered]@{ schemaVersion = 2; product = 'fixture'; buildMode = 'Candidate' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'release-manifest.json') -Encoding utf8NoBOM

  $sentinel = Start-Process -FilePath $fixtureExe -ArgumentList @('-n', '8', '127.0.0.1') -PassThru -WindowStyle Hidden
  Assert (-not $sentinel.HasExited) 'same-name sentinel must be alive before the smoke'

  $surviveJson = Join-Path $tmp 'survive.json'
  $surviveOutput = (& pwsh -NoProfile -File $smoke -PackageRoot $package -ExecutablePath $fixtureExe `
    -OutputJson $surviveJson -Seconds 1 -Arguments '-n 5 127.0.0.1' 2>&1) | Out-String
  $surviveExit = $LASTEXITCODE
  Assert ($surviveExit -eq 0) ('surviving child must pass: ' + $surviveOutput.Trim())
  Assert (Test-Path -LiteralPath $surviveJson -PathType Leaf) 'surviving child must produce JSON evidence'
  if (-not (Test-Path -LiteralPath $surviveJson -PathType Leaf)) {
    throw ('surviving child did not produce JSON: ' + $surviveOutput.Trim())
  }
  $survive = Get-Content -LiteralPath $surviveJson -Raw | ConvertFrom-Json
  Assert ($survive.schemaVersion -eq 1) 'liveness JSON schema must be versioned'
  Assert ($survive.result -ceq 'PASS' -and $survive.livenessAtDeadline -eq $true) 'surviving child JSON must record PASS/livenessAtDeadline'
  Assert ($null -eq $survive.PSObject.Properties['aliveAtDeadline']) 'legacy aliveAtDeadline must not remain in the JSON contract'
  Assert ([long]$survive.spawnedPid -gt 0) 'liveness JSON must record the owned PID'
  Assert ((Test-Path -LiteralPath $survive.stdoutPath -PathType Leaf) -and (Test-Path -LiteralPath $survive.stderrPath -PathType Leaf)) 'stdout/stderr evidence files must exist'
  Assert ((Get-FileHash -LiteralPath $fixtureExe -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $survive.executableSha256) 'executable SHA must bind the evidence'
  Assert ((Get-FileHash -LiteralPath (Join-Path $package 'release-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $survive.manifestSha256) 'manifest SHA must bind the evidence'

  $sentinel.Refresh()
  Assert (-not $sentinel.HasExited) 'cleanup must not kill a preexisting same-name process'

  $earlyJson = Join-Path $tmp 'early.json'
  $earlyOutput = (& pwsh -NoProfile -File $smoke -PackageRoot $package -ExecutablePath $earlyExe `
    -OutputJson $earlyJson -Seconds 2 -Arguments '/d /c "exit 7"' 2>&1) | Out-String
  $earlyExit = $LASTEXITCODE
  Assert ($earlyExit -eq 1) ('early exit must fail: ' + $earlyOutput.Trim())
  $early = Get-Content -LiteralPath $earlyJson -Raw | ConvertFrom-Json
  Assert ($early.result -ceq 'FAIL' -and $early.livenessAtDeadline -eq $false) 'early exit JSON must record FAIL/not alive'
  Assert ($early.exitCode -eq 7 -and $early.exitBehavior -ceq 'exited-before-deadline') 'early exit behavior/code must be explicit'

  $packageBeforeEvidenceAlias = Get-PackageFingerprint
  $evidenceAlias = Join-Path $tmp 'evidence-alias-to-package'
  New-Item -ItemType Junction -Path $evidenceAlias -Target $package | Out-Null
  $aliasedJson = Join-Path $evidenceAlias 'aliased-evidence.json'
  $aliasSpawnMarker = Join-Path $tmp 'evidence-alias-spawned.txt'
  $aliasOutput = (& pwsh -NoProfile -File $smoke -PackageRoot $package -ExecutablePath $earlyExe `
    -OutputJson $aliasedJson -Seconds 1 -Arguments ('/d /c "echo spawned>{0}"' -f $aliasSpawnMarker) 2>&1) | Out-String
  Assert ($LASTEXITCODE -ne 0 -and $aliasOutput -match 'EVIDENCE_CANONICAL_OVERLAP_PACKAGE') `
    ('canonical evidence/log alias into PackageRoot must reject before spawn: ' + $aliasOutput.Trim())
  Assert (-not (Test-Path -LiteralPath $aliasSpawnMarker) -and (Get-PackageFingerprint) -ceq $packageBeforeEvidenceAlias) `
    'canonical evidence alias rejection must not spawn or alter PackageRoot bytes/entries'
  foreach ($aliasedOutput in @($aliasedJson, [IO.Path]::ChangeExtension($aliasedJson, 'stdout.log'), [IO.Path]::ChangeExtension($aliasedJson, 'stderr.log'))) {
    if (Test-Path -LiteralPath $aliasedOutput) { Remove-Item -LiteralPath $aliasedOutput -Force }
  }
  if (Test-Path -LiteralPath $aliasSpawnMarker) { Remove-Item -LiteralPath $aliasSpawnMarker -Force }
  Remove-Item -LiteralPath $evidenceAlias -Force

  $externalExecutableRoot = Join-Path $tmp 'external-executable'
  New-Item -ItemType Directory -Path $externalExecutableRoot | Out-Null
  Copy-Item -LiteralPath $earlyExe -Destination (Join-Path $externalExecutableRoot 'liveness-helper.exe')
  $linkedExecutableRoot = Join-Path $package 'linked-executable'
  New-Item -ItemType Junction -Path $linkedExecutableRoot -Target $externalExecutableRoot | Out-Null
  $reparseJson = Join-Path $tmp 'reparse.json'
  $spawnMarker = Join-Path $tmp 'reparse-spawned.txt'
  $reparseOutput = (& pwsh -NoProfile -File $smoke -PackageRoot $package `
    -ExecutablePath (Join-Path $linkedExecutableRoot 'liveness-helper.exe') -OutputJson $reparseJson `
    -Seconds 1 -Arguments ('/d /c "echo spawned>{0}"' -f $spawnMarker) 2>&1) | Out-String
  Assert ($LASTEXITCODE -ne 0 -and $reparseOutput -match 'EXECUTABLE_REPARSE_FORBIDDEN') `
    ('executable beneath a reparse ancestor must receive the pre-spawn reparse rejection: ' + $reparseOutput.Trim())
  Assert (-not (Test-Path -LiteralPath $reparseJson) -and -not (Test-Path -LiteralPath $spawnMarker)) `
    'intermediate reparse rejection must create no PID evidence and must not spawn the executable'

  $treeExe = Join-Path $package 'tree-helper.exe'
  Copy-Item -LiteralPath $earlyExe -Destination $treeExe
  $treeMarker = Join-Path $tmp 'owned-child-survived.txt'
  $treeScript = Join-Path $tmp 'spawn-child.cmd'
  @(
    '@echo off',
    ('start "" /b cmd.exe /d /c "ping -n 3 127.0.0.1 >nul & echo child-alive>{0}"' -f $treeMarker),
    ':owned_parent_loop',
    'goto owned_parent_loop'
  ) | Set-Content -LiteralPath $treeScript -Encoding ASCII
  $treeJson = Join-Path $tmp 'tree.json'
  $treeOutput = (& pwsh -NoProfile -File $smoke -PackageRoot $package -ExecutablePath $treeExe `
    -OutputJson $treeJson -Seconds 1 -Arguments ('/d /c {0}' -f $treeScript) 2>&1) | Out-String
  Assert ($LASTEXITCODE -eq 0) ('owned parent liveness smoke must pass: ' + $treeOutput.Trim())
  Assert (Wait-ForPath $treeMarker 5000) 'cleanup must not recursively terminate a child process not represented by the owned Process handle'

  $persistentChildPidPath = Join-Path $tmp 'persistent-child.pid'
  $persistentChildScript = Join-Path $tmp 'persistent-child.ps1'
  @"
[IO.File]::WriteAllText('$persistentChildPidPath', [string]`$PID)
while (`$true) { Start-Sleep -Seconds 1 }
"@ | Set-Content -LiteralPath $persistentChildScript -Encoding utf8NoBOM
  $persistentParentScript = Join-Path $tmp 'persistent-parent.cmd'
  @(
    '@echo off',
    ('start "" /b powershell.exe -NoProfile -ExecutionPolicy Bypass -File {0}' -f $persistentChildScript),
    ':persistent_parent_loop',
    'goto persistent_parent_loop'
  ) | Set-Content -LiteralPath $persistentParentScript -Encoding ASCII
  $persistentJson = Join-Path $tmp 'persistent-pipe.json'
  $persistentWrapper = Join-Path $tmp 'invoke-persistent-smoke.ps1'
  @"
& '$smoke' -PackageRoot '$package' -ExecutablePath '$treeExe' -OutputJson '$persistentJson' -Seconds 1 -Arguments '/d /c $persistentParentScript'
exit `$LASTEXITCODE
"@ | Set-Content -LiteralPath $persistentWrapper -Encoding utf8NoBOM
  $persistentOuterStdout = Join-Path $tmp 'persistent-outer.stdout.log'
  $persistentOuterStderr = Join-Path $tmp 'persistent-outer.stderr.log'
  $persistentWatch = [Diagnostics.Stopwatch]::StartNew()
  $persistentSmoke = Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList @('-NoProfile','-File',$persistentWrapper) `
    -RedirectStandardOutput $persistentOuterStdout -RedirectStandardError $persistentOuterStderr -PassThru -WindowStyle Hidden
  $persistentPidReady = Wait-ForPath $persistentChildPidPath 2500
  $remainingCeiling = [Math]::Max(1, 5000 - [int]$persistentWatch.ElapsedMilliseconds)
  $persistentCompletedInTime = $persistentSmoke.WaitForExit($remainingCeiling)
  $persistentWatch.Stop()
  $persistentChild = $null
  if ($persistentPidReady) {
    try { $persistentChild = [Diagnostics.Process]::GetProcessById([int](Get-Content -LiteralPath $persistentChildPidPath -Raw)) } catch { }
  }
  $persistentChildAliveBeforeCleanup = $null -ne $persistentChild -and -not $persistentChild.HasExited
  Assert ($persistentPidReady -and $persistentCompletedInTime -and $persistentWatch.ElapsedMilliseconds -lt 5000) `
    ('liveness must bound inherited stdout/stderr drain below 5s; elapsedMs={0}' -f $persistentWatch.ElapsedMilliseconds)
  Assert $persistentChildAliveBeforeCleanup 'persistent descendant inheriting pipes must remain untouched until fixture-owned cleanup'
  if ($persistentChildAliveBeforeCleanup) {
    $persistentChild.Kill($false)
    [void]$persistentChild.WaitForExit(5000)
  }
  if ($null -ne $persistentChild) { $persistentChild.Dispose() }
  if (-not $persistentCompletedInTime) {
    if (-not $persistentSmoke.WaitForExit(5000)) { $persistentSmoke.Kill($false); [void]$persistentSmoke.WaitForExit(5000) }
  }
  $persistentSmoke.Dispose()

  $writeBoundaryFingerprint = Get-PackageFingerprint
  $writeBoundaryParent = Join-Path $tmp 'liveness-write-boundary'
  New-Item -ItemType Directory -Path $writeBoundaryParent | Out-Null
  $writeBoundaryJson = Join-Path $writeBoundaryParent 'boundary.json'
  $writeBoundarySignal = Join-Path $tmp 'liveness-write-ready.signal'
  $writeBoundaryContinue = $writeBoundarySignal + '.continue'
  $writeBoundaryWrapper = Join-Path $tmp 'invoke-write-boundary-smoke.ps1'
  @"
& '$smoke' -PackageRoot '$package' -ExecutablePath '$fixtureExe' -OutputJson '$writeBoundaryJson' -Seconds 1 -Arguments '-n 5 127.0.0.1' -TestPauseBeforeEvidenceWriteSignal '$writeBoundarySignal'
exit `$LASTEXITCODE
"@ | Set-Content -LiteralPath $writeBoundaryWrapper -Encoding utf8NoBOM
  $writeBoundaryStdout = Join-Path $tmp 'write-boundary-outer.stdout.log'
  $writeBoundaryStderr = Join-Path $tmp 'write-boundary-outer.stderr.log'
  $oldLivenessHook = $env:FFX_RELEASE_LIVENESS_TEST_HOOK
  $env:FFX_RELEASE_LIVENESS_TEST_HOOK = 'evidence-write-pause-v1'
  try {
    $writeBoundaryProcess = Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList @('-NoProfile','-File',$writeBoundaryWrapper) `
      -RedirectStandardOutput $writeBoundaryStdout -RedirectStandardError $writeBoundaryStderr -PassThru -WindowStyle Hidden
    $writeBoundaryObserved = Wait-ForPath $writeBoundarySignal 5000
    Assert $writeBoundaryObserved 'liveness write-boundary hook must pause after process cleanup and before evidence writes'
    if ($writeBoundaryObserved) {
      Remove-Item -LiteralPath $writeBoundaryParent -Force
      New-Item -ItemType Junction -Path $writeBoundaryParent -Target $package | Out-Null
      Set-Content -LiteralPath $writeBoundaryContinue -Value 'continue' -NoNewline -Encoding ASCII
    }
    if (-not $writeBoundaryProcess.WaitForExit(10000)) { $writeBoundaryProcess.Kill($false); $writeBoundaryProcess.WaitForExit() }
    $writeBoundaryExit = $writeBoundaryProcess.ExitCode
    $writeBoundaryProcess.Dispose()
  } finally {
    $env:FFX_RELEASE_LIVENESS_TEST_HOOK = $oldLivenessHook
  }
  $writeBoundaryOutput = ((Get-Content -LiteralPath $writeBoundaryStdout -Raw -ErrorAction SilentlyContinue) + "`n" +
    (Get-Content -LiteralPath $writeBoundaryStderr -Raw -ErrorAction SilentlyContinue))
  Assert ($writeBoundaryExit -ne 0 -and $writeBoundaryOutput -match 'EVIDENCE_CANONICAL_OVERLAP_PACKAGE') `
    'liveness must re-resolve evidence/log overlap immediately before final writes'
  Assert ((Get-PackageFingerprint) -ceq $writeBoundaryFingerprint) `
    'liveness write-boundary swap must leave PackageRoot byte/entry unchanged'
  if (Test-Path -LiteralPath $writeBoundaryParent) { Remove-Item -LiteralPath $writeBoundaryParent -Force }

  $packageEvidence = Join-Path $package 'must-not-mutate-package.json'
  $packageOutput = (& pwsh -NoProfile -File $smoke -PackageRoot $package -ExecutablePath $fixtureExe `
    -OutputJson $packageEvidence -Seconds 1 -Arguments '/d /c "exit 0"' 2>&1) | Out-String
  Assert ($LASTEXITCODE -ne 0 -and -not (Test-Path -LiteralPath $packageEvidence)) ('OutputJson inside PackageRoot must be rejected: ' + $packageOutput.Trim())
} finally {
  if ($null -ne $sentinel) {
    try {
      $sentinel.Refresh()
      if (-not $sentinel.HasExited) { $sentinel.Kill($true); $sentinel.WaitForExit(5000) | Out-Null }
    } catch { }
    $sentinel.Dispose()
  }
  $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
  $resolvedTmp = [IO.Path]::GetFullPath($tmp)
  if ($resolvedTmp.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedTmp)) {
    Remove-Item -LiteralPath $resolvedTmp -Recurse -Force
  }
}

if ($fails.Count -gt 0) {
  $fails | ForEach-Object { Write-Output ('ASSERT-FAIL: ' + $_) }
  Write-Output ("LIVENESS_TEST_FAIL assertions={0} failures={1}" -f $runs, $fails.Count)
  exit 1
}
Write-Output ("LIVENESS_TEST_OK assertions={0}" -f $runs)
exit 0
