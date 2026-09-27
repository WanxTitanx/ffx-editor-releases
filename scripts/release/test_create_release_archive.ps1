<#
  Deterministic self-test for create_release_archive.ps1.

  Two equivalent Candidate trees with different mtimes must produce byte-identical ZIPs. The
  remaining cases exercise verifier, source/mode, containment, existing-output, PDB/deny and
  reparse fail-closed boundaries without using a product payload.
#>
$ErrorActionPreference = 'Stop'
$archiveSource = Join-Path $PSScriptRoot 'create_release_archive.ps1'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('ffx-archive-test-' + [Guid]::NewGuid().ToString('N'))
$fixtureRepo = Join-Path $tmp 'repo'
$fixtureScripts = Join-Path $fixtureRepo 'scripts\release'
$releaseRoot = Join-Path $fixtureRepo 'work\release-readiness'
$fails = [Collections.Generic.List[string]]::new()
$runs = 0
New-Item -ItemType Directory -Path $fixtureScripts -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $fixtureRepo 'release') -Force | Out-Null
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

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

function Get-TreeFingerprint([string]$Root) {
  $rows = Get-ChildItem -LiteralPath $Root -Recurse -File -Force | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    '{0}|{1}|{2}' -f $relative, $_.Length, $hash
  }
  return [string]::Join("`n", @($rows | Sort-Object -CaseSensitive))
}

function Get-VerifyLogLength {
  if (Test-Path -LiteralPath $verifyLog -PathType Leaf) { return (Get-Item -LiteralPath $verifyLog).Length }
  return 0
}

function Write-Manifest([string]$Root, [string]$Commit, [string]$Mode = 'Candidate') {
  [ordered]@{
    schemaVersion = 2
    product = 'FFX Mod Studio fixture'
    target = 'win-x64'
    selfContained = $true
    sourceCommit = $Commit
    sourceDirty = $false
    buildSdk = '8.0.100'
    buildMode = $Mode
    generatedUtc = '2026-08-26T00:00:00.0000000Z'
    files = @()
  } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Root 'release-manifest.json') -Encoding utf8NoBOM
}

function New-Package([string]$Root, [string]$Commit, [datetime]$Timestamp) {
  New-Item -ItemType Directory -Path (Join-Path $Root 'unicode\beta') -Force | Out-Null
  Set-Content -LiteralPath (Join-Path $Root 'zeta.txt') -Value 'zeta' -NoNewline -Encoding utf8NoBOM
  Set-Content -LiteralPath (Join-Path $Root 'Alpha.txt') -Value 'alpha' -NoNewline -Encoding utf8NoBOM
  Set-Content -LiteralPath (Join-Path $Root 'unicode\Ångström.txt') -Value 'angstrom' -NoNewline -Encoding utf8NoBOM
  Set-Content -LiteralPath (Join-Path $Root 'unicode\beta\same.bin') -Value 'same' -NoNewline -Encoding utf8NoBOM
  Write-Manifest $Root $Commit
  Get-ChildItem -LiteralPath $Root -Recurse -Force | ForEach-Object { $_.LastWriteTimeUtc = $Timestamp }
}

function Invoke-Archive([string]$Package, [string]$Zip, [string]$Commit) {
  $script = Join-Path $fixtureScripts 'create_release_archive.ps1'
  $output = (& pwsh -NoProfile -File $script -PackageRoot $Package -OutputZip $Zip -Mode Candidate -SourceCommit $Commit 2>&1) | Out-String
  return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output.Trim() }
}

try {
  $verifyLog = Join-Path $tmp 'archive-verifier-invocations.log'
  $env:FFX_TEST_ARCHIVE_VERIFY_LOG = $verifyLog
  Copy-Item -LiteralPath $archiveSource -Destination (Join-Path $fixtureScripts 'create_release_archive.ps1')
  @'
param([Parameter(Mandatory=$true)][string]$PackageRoot,[ValidateSet('Diagnostic','Candidate','Release')][string]$Mode,[switch]$Json)
if (-not [string]::IsNullOrWhiteSpace($env:FFX_TEST_ARCHIVE_VERIFY_LOG)) {
  [IO.File]::AppendAllText($env:FFX_TEST_ARCHIVE_VERIFY_LOG, ($PackageRoot + "`n"))
}
if (Test-Path -LiteralPath (Join-Path $PackageRoot 'reject.verify')) {
  [ordered]@{schemaVersion=2;status='rejected';exitCode=1;phase='fixture';mode=$Mode;errors=@('FIXTURE_REJECTED');infrastructureErrors=@();warnings=@()} | ConvertTo-Json -Compress
  exit 1
}
[ordered]@{schemaVersion=2;status='verified';exitCode=0;phase='complete';mode=$Mode;errors=@();infrastructureErrors=@();warnings=@()} | ConvertTo-Json -Compress
exit 0
'@ | Set-Content -LiteralPath (Join-Path $fixtureScripts 'verify_package.ps1') -Encoding utf8NoBOM
  [ordered]@{
    schemaVersion = 1
    components = [ordered]@{
      'studio-core-win-x64' = [ordered]@{
        deny = [ordered]@{ paths = @('forbidden/'); extensions = @('.pdb'); patterns = @() }
      }
    }
  } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $fixtureRepo 'release\package-allowlist.json') -Encoding utf8NoBOM
  Set-Content -LiteralPath (Join-Path $fixtureRepo '.gitignore') -Value "work/`n" -Encoding utf8NoBOM
  & git -C $fixtureRepo init -q
  & git -C $fixtureRepo config user.email 'release-fixture@invalid.local'
  & git -C $fixtureRepo config user.name 'FFX Release Fixture'
  & git -C $fixtureRepo config core.autocrlf false
  & git -C $fixtureRepo add -- .gitignore release scripts
  & git -C $fixtureRepo commit -q -m 'fixture'
  $commit = (& git -C $fixtureRepo rev-parse HEAD).Trim()
  Assert ($LASTEXITCODE -eq 0 -and $commit -match '^[0-9a-f]{40}$') 'fixture git commit must exist'

  $packageA = Join-Path $releaseRoot 'run-a\package'
  $packageB = Join-Path $releaseRoot 'run-b\package'
  New-Package $packageA $commit ([datetime]'2020-01-02T03:04:05Z')
  New-Package $packageB $commit ([datetime]'2026-08-26T11:12:13Z')
  $zipA = Join-Path $releaseRoot 'run-a\candidate-a.zip'
  $zipB = Join-Path $releaseRoot 'run-b\candidate-b.zip'
  $resultA = Invoke-Archive $packageA $zipA $commit
  $resultB = Invoke-Archive $packageB $zipB $commit
  Assert ($resultA.ExitCode -eq 0) ('first deterministic archive must pass: ' + $resultA.Output)
  Assert ($resultB.ExitCode -eq 0) ('second deterministic archive must pass: ' + $resultB.Output)
  Assert ((Get-FileHash -LiteralPath $zipA -Algorithm SHA256).Hash -ceq (Get-FileHash -LiteralPath $zipB -Algorithm SHA256).Hash) 'equivalent trees with different mtimes must yield byte-identical ZIPs'
  Assert ((Test-Path -LiteralPath ($zipA + '.sha256')) -and (Test-Path -LiteralPath ($zipB + '.sha256'))) 'each ZIP must have a SHA-256 sidecar'

  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $opened = [IO.Compression.ZipFile]::OpenRead($zipA)
  try {
    $actualNames = [string[]]@($opened.Entries | ForEach-Object FullName)
    $expectedNames = [string[]]@($actualNames)
    [Array]::Sort($expectedNames, [StringComparer]::Ordinal)
    Assert (([string]::Join("`n", $actualNames)) -ceq ([string]::Join("`n", $expectedNames))) 'ZIP entries must use stable ordinal Unicode ordering'
    Assert (@($opened.Entries | Where-Object { $_.FullName.Contains('\') }).Count -eq 0) 'ZIP entry separators must be normalized to slash'
    # ZIP stores a DOS wall-clock timestamp and has no timezone field. The stable byte-level
    # representation of 1980-01-01T00:00:00Z therefore reopens with the host's historical offset.
    $badTimestamps = @($opened.Entries | Where-Object { $_.LastWriteTime.DateTime -ne [datetime]'1980-01-01T00:00:00' })
    Assert ($badTimestamps.Count -eq 0) ('ZIP timestamps must be normalized to the 1980-01-01 DOS epoch; actual=' +
      [string]::Join(',', @($badTimestamps | ForEach-Object { $_.LastWriteTime.ToString('o') })))
  } finally { $opened.Dispose() }

  $outsideZip = Join-Path $tmp 'outside.zip'
  $result = Invoke-Archive $packageA $outsideZip $commit
  Assert ($result.ExitCode -ne 0 -and -not (Test-Path -LiteralPath $outsideZip)) 'archive output outside work/release-readiness must be rejected before write'

  $existingZip = Join-Path $releaseRoot 'existing.zip'
  Set-Content -LiteralPath $existingZip -Value 'existing' -NoNewline -Encoding ASCII
  $existingHash = (Get-FileHash -LiteralPath $existingZip -Algorithm SHA256).Hash
  $result = Invoke-Archive $packageA $existingZip $commit
  Assert ($result.ExitCode -ne 0 -and (Get-FileHash -LiteralPath $existingZip -Algorithm SHA256).Hash -ceq $existingHash) 'existing archive must be rejected without overwrite'

  $policyPath = Join-Path $fixtureRepo 'release\package-allowlist.json'
  $policyBytes = [IO.File]::ReadAllBytes($policyPath)
  Add-Content -LiteralPath $policyPath -Value ' ' -Encoding ASCII
  $dirtyPolicyZip = Join-Path $releaseRoot 'dirty-policy.zip'
  $result = Invoke-Archive $packageA $dirtyPolicyZip $commit
  Assert ($result.ExitCode -ne 0 -and $result.Output -match 'SOURCE_REPO_DIRTY' -and -not (Test-Path -LiteralPath $dirtyPolicyZip)) `
    'dirty tracked allowlist must block archive creation before policy/verifier reads'
  [IO.File]::WriteAllBytes($policyPath, $policyBytes)

  $verifierPath = Join-Path $fixtureScripts 'verify_package.ps1'
  $verifierBytes = [IO.File]::ReadAllBytes($verifierPath)
  Add-Content -LiteralPath $verifierPath -Value '# dirty verifier fixture' -Encoding ASCII
  $dirtyVerifierZip = Join-Path $releaseRoot 'dirty-verifier.zip'
  $result = Invoke-Archive $packageA $dirtyVerifierZip $commit
  Assert ($result.ExitCode -ne 0 -and $result.Output -match 'SOURCE_REPO_DIRTY' -and -not (Test-Path -LiteralPath $dirtyVerifierZip)) `
    'dirty tracked verifier must block archive creation'
  [IO.File]::WriteAllBytes($verifierPath, $verifierBytes)

  $untrackedSource = Join-Path $fixtureRepo 'untracked-source.txt'
  Set-Content -LiteralPath $untrackedSource -Value 'untracked' -NoNewline -Encoding ASCII
  $untrackedZip = Join-Path $releaseRoot 'untracked-source.zip'
  $result = Invoke-Archive $packageA $untrackedZip $commit
  Assert ($result.ExitCode -ne 0 -and $result.Output -match 'SOURCE_REPO_DIRTY' -and -not (Test-Path -LiteralPath $untrackedZip)) `
    'untracked producing-repo source must block archive creation'
  Remove-Item -LiteralPath $untrackedSource -Force

  $rejectPackage = Join-Path $releaseRoot 'reject\package'
  New-Package $rejectPackage $commit ([datetime]'2026-01-01T00:00:00Z')
  Set-Content -LiteralPath (Join-Path $rejectPackage 'reject.verify') -Value 'reject' -NoNewline -Encoding ASCII
  $result = Invoke-Archive $rejectPackage (Join-Path $releaseRoot 'reject\candidate.zip') $commit
  Assert ($result.ExitCode -ne 0 -and $result.Output -match 'PACKAGE_VERIFIER_REJECTED') 'package verifier rejection must block archive creation'

  $sourceZip = Join-Path $releaseRoot 'source-mismatch.zip'
  $result = Invoke-Archive $packageA $sourceZip ('0' * 40)
  Assert ($result.ExitCode -ne 0 -and -not (Test-Path -LiteralPath $sourceZip)) 'source commit mismatch must block archive creation'

  $headMismatchCommit = '1' * 40
  $headMismatchPackage = Join-Path $releaseRoot 'head-mismatch\package'
  New-Package $headMismatchPackage $headMismatchCommit ([datetime]'2026-01-01T00:00:00Z')
  $headMismatchZip = Join-Path $releaseRoot 'head-mismatch\candidate.zip'
  $result = Invoke-Archive $headMismatchPackage $headMismatchZip $headMismatchCommit
  Assert ($result.ExitCode -ne 0 -and $result.Output -match 'SOURCE_COMMIT_HEAD_MISMATCH' -and
    -not (Test-Path -LiteralPath $headMismatchZip)) 'argument and manifest must both bind to the producing worktree HEAD'

  $modePackage = Join-Path $releaseRoot 'mode\package'
  New-Package $modePackage $commit ([datetime]'2026-01-01T00:00:00Z')
  Write-Manifest $modePackage $commit 'Diagnostic'
  $result = Invoke-Archive $modePackage (Join-Path $releaseRoot 'mode\candidate.zip') $commit
  Assert ($result.ExitCode -ne 0 -and $result.Output -match 'MANIFEST_MODE_MISMATCH') 'non-Candidate package manifest must block archive creation'

  $pdbPackage = Join-Path $releaseRoot 'pdb\package'
  New-Package $pdbPackage $commit ([datetime]'2026-01-01T00:00:00Z')
  Set-Content -LiteralPath (Join-Path $pdbPackage 'symbols.pdb') -Value 'pdb' -NoNewline -Encoding ASCII
  $result = Invoke-Archive $pdbPackage (Join-Path $releaseRoot 'pdb\candidate.zip') $commit
  Assert ($result.ExitCode -ne 0 -and $result.Output -match 'DENYLIST') 'PDB/denylist payload must block archive creation'

  $reparsePackage = Join-Path $releaseRoot 'reparse\package'
  New-Package $reparsePackage $commit ([datetime]'2026-01-01T00:00:00Z')
  $outsideTarget = Join-Path $tmp 'outside-target'
  New-Item -ItemType Directory -Path $outsideTarget | Out-Null
  Set-Content -LiteralPath (Join-Path $outsideTarget 'escaped.txt') -Value 'escape' -NoNewline -Encoding ASCII
  New-Item -ItemType Junction -Path (Join-Path $reparsePackage 'linked-out') -Target $outsideTarget | Out-Null
  $result = Invoke-Archive $reparsePackage (Join-Path $releaseRoot 'reparse\candidate.zip') $commit
  Assert ($result.ExitCode -ne 0 -and $result.Output -match 'REPARSE') 'reparse payload must block archive creation'

  $outputOutside = Join-Path $tmp 'archive-output-outside'
  New-Item -ItemType Directory -Path $outputOutside | Out-Null
  $outputLink = Join-Path $releaseRoot 'output-link'
  New-Item -ItemType Junction -Path $outputLink -Target $outputOutside | Out-Null
  $escapedOutput = Join-Path $outputLink 'escaped.zip'
  $result = Invoke-Archive $packageA $escapedOutput $commit
  Assert ($result.ExitCode -ne 0 -and -not (Test-Path -LiteralPath (Join-Path $outputOutside 'escaped.zip'))) 'output reparse ancestor escaping release root must be rejected before write'

  $outsidePackageRoot = Join-Path $tmp 'archive-package-outside'
  $outsidePackage = Join-Path $outsidePackageRoot 'package'
  New-Package $outsidePackage $commit ([datetime]'2026-01-01T00:00:00Z')
  $packageLink = Join-Path $releaseRoot 'package-link'
  New-Item -ItemType Junction -Path $packageLink -Target $outsidePackageRoot | Out-Null
  $result = Invoke-Archive (Join-Path $packageLink 'package') (Join-Path $releaseRoot 'package-link-test.zip') $commit
  Assert ($result.ExitCode -ne 0 -and $result.Output -match 'REPARSE') 'PackageRoot beneath an escaping reparse ancestor must be rejected'

  $packageBeforeAlias = Get-TreeFingerprint $packageA
  $verifyBeforeAlias = Get-VerifyLogLength
  $zipAlias = Join-Path $releaseRoot 'zip-alias-to-package'
  New-Item -ItemType Junction -Path $zipAlias -Target $packageA | Out-Null
  $aliasedZip = Join-Path $zipAlias 'must-not-create.zip'
  $result = Invoke-Archive $packageA $aliasedZip $commit
  Assert ($result.ExitCode -ne 0 -and $result.Output -match 'OUTPUT_CANONICAL_OVERLAP_PACKAGE' -and
    (Get-VerifyLogLength) -eq $verifyBeforeAlias -and -not (Test-Path -LiteralPath $aliasedZip)) `
    'canonical ZIP/sidecar alias into PackageRoot must reject before verifier and CreateNew'
  Assert ((Get-TreeFingerprint $packageA) -ceq $packageBeforeAlias) 'archive canonical alias rejection must leave PackageRoot byte/entry unchanged'
  Remove-Item -LiteralPath $zipAlias -Force

  $mutationPackage = Join-Path $releaseRoot 'mutation\package'
  New-Package $mutationPackage $commit ([datetime]'2026-01-01T00:00:00Z')
  $mutationZip = Join-Path $releaseRoot 'mutation\candidate.zip'
  $snapshotSignal = Join-Path $tmp 'archive-snapshot-ready.signal'
  $continueSignal = $snapshotSignal + '.continue'
  $mutationStdout = Join-Path $tmp 'mutation-stdout.log'
  $mutationStderr = Join-Path $tmp 'mutation-stderr.log'
  $oldTestHook = $env:FFX_RELEASE_ARCHIVE_TEST_HOOK
  $env:FFX_RELEASE_ARCHIVE_TEST_HOOK = 'snapshot-pause-v1'
  try {
    $mutationProcess = Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList @(
      '-NoProfile', '-File', (Join-Path $fixtureScripts 'create_release_archive.ps1'),
      '-PackageRoot', $mutationPackage, '-OutputZip', $mutationZip, '-Mode', 'Candidate',
      '-SourceCommit', $commit, '-TestPauseAfterSnapshotSignal', $snapshotSignal
    ) -RedirectStandardOutput $mutationStdout -RedirectStandardError $mutationStderr -PassThru -WindowStyle Hidden
    $snapshotObserved = Wait-ForPath $snapshotSignal 3000
    Assert $snapshotObserved 'archive mutation hook must pause only after the verifier-approved snapshot exists'
    if ($snapshotObserved) {
      Set-Content -LiteralPath (Join-Path $mutationPackage 'zeta.txt') -Value 'mutated-after-approval' -NoNewline -Encoding utf8NoBOM
      Set-Content -LiteralPath $continueSignal -Value 'continue' -NoNewline -Encoding ASCII
    }
    if (-not $mutationProcess.WaitForExit(10000)) { $mutationProcess.Kill(); $mutationProcess.WaitForExit() }
    $mutationExit = $mutationProcess.ExitCode
    $mutationProcess.Dispose()
  } finally {
    $env:FFX_RELEASE_ARCHIVE_TEST_HOOK = $oldTestHook
  }
  $mutationOutput = ((Get-Content -LiteralPath $mutationStdout -Raw -ErrorAction SilentlyContinue) + "`n" +
    (Get-Content -LiteralPath $mutationStderr -Raw -ErrorAction SilentlyContinue))
  Assert ($mutationExit -ne 0 -and $mutationOutput -match 'PACKAGE_.*MUTAT' -and
    -not (Test-Path -LiteralPath $mutationZip) -and -not (Test-Path -LiteralPath ($mutationZip + '.sha256'))) `
    'mutation after verifier approval must fail, remove the owned ZIP, and omit the sidecar'

  $dirtyDuringPackage = Join-Path $releaseRoot 'dirty-during\package'
  New-Package $dirtyDuringPackage $commit ([datetime]'2026-01-01T00:00:00Z')
  $dirtyDuringZip = Join-Path $releaseRoot 'dirty-during\candidate.zip'
  $dirtySignal = Join-Path $tmp 'archive-dirty-source-ready.signal'
  $dirtyContinue = $dirtySignal + '.continue'
  $dirtyStdout = Join-Path $tmp 'dirty-source-stdout.log'
  $dirtyStderr = Join-Path $tmp 'dirty-source-stderr.log'
  $env:FFX_RELEASE_ARCHIVE_TEST_HOOK = 'snapshot-pause-v1'
  try {
    $dirtyProcess = Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList @(
      '-NoProfile', '-File', (Join-Path $fixtureScripts 'create_release_archive.ps1'),
      '-PackageRoot', $dirtyDuringPackage, '-OutputZip', $dirtyDuringZip, '-Mode', 'Candidate',
      '-SourceCommit', $commit, '-TestPauseAfterSnapshotSignal', $dirtySignal
    ) -RedirectStandardOutput $dirtyStdout -RedirectStandardError $dirtyStderr -PassThru -WindowStyle Hidden
    $dirtySignalObserved = Wait-ForPath $dirtySignal 3000
    Assert $dirtySignalObserved 'mid-run dirty-source hook must pause after initial clean-source validation'
    $midRunUntracked = Join-Path $fixtureRepo 'mid-run-untracked.txt'
    if ($dirtySignalObserved) {
      Set-Content -LiteralPath $midRunUntracked -Value 'dirty during archive' -NoNewline -Encoding ASCII
      Set-Content -LiteralPath $dirtyContinue -Value 'continue' -NoNewline -Encoding ASCII
    }
    if (-not $dirtyProcess.WaitForExit(10000)) { $dirtyProcess.Kill(); $dirtyProcess.WaitForExit() }
    $dirtyExit = $dirtyProcess.ExitCode
    $dirtyProcess.Dispose()
  } finally {
    $env:FFX_RELEASE_ARCHIVE_TEST_HOOK = $oldTestHook
  }
  $dirtyOutput = ((Get-Content -LiteralPath $dirtyStdout -Raw -ErrorAction SilentlyContinue) + "`n" +
    (Get-Content -LiteralPath $dirtyStderr -Raw -ErrorAction SilentlyContinue))
  Assert ($dirtyExit -ne 0 -and $dirtyOutput -match 'SOURCE_REPO_DIRTY' -and
    -not (Test-Path -LiteralPath $dirtyDuringZip) -and -not (Test-Path -LiteralPath ($dirtyDuringZip + '.sha256'))) `
    'source becoming dirty mid-run must remove the owned ZIP/sidecar and block ARCHIVE_OK'
  if (Test-Path -LiteralPath $midRunUntracked) { Remove-Item -LiteralPath $midRunUntracked -Force }

  $parentSwapPackage = Join-Path $releaseRoot 'parent-swap-package\package'
  New-Package $parentSwapPackage $commit ([datetime]'2026-01-01T00:00:00Z')
  $parentSwapFingerprint = Get-TreeFingerprint $parentSwapPackage
  $parentSwapDirectory = Join-Path $releaseRoot 'parent-swap-output'
  $parentSwapZip = Join-Path $parentSwapDirectory 'candidate.zip'
  $parentSwapSignal = Join-Path $tmp 'archive-parent-ready.signal'
  $parentSwapContinue = $parentSwapSignal + '.continue'
  $parentSwapStdout = Join-Path $tmp 'parent-swap-stdout.log'
  $parentSwapStderr = Join-Path $tmp 'parent-swap-stderr.log'
  $oldParentHook = $env:FFX_RELEASE_ARCHIVE_PARENT_TEST_HOOK
  $env:FFX_RELEASE_ARCHIVE_PARENT_TEST_HOOK = 'output-parent-pause-v1'
  try {
    $parentSwapProcess = Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList @(
      '-NoProfile', '-File', (Join-Path $fixtureScripts 'create_release_archive.ps1'),
      '-PackageRoot', $parentSwapPackage, '-OutputZip', $parentSwapZip, '-Mode', 'Candidate',
      '-SourceCommit', $commit, '-TestPauseAfterOutputParentSignal', $parentSwapSignal
    ) -RedirectStandardOutput $parentSwapStdout -RedirectStandardError $parentSwapStderr -PassThru -WindowStyle Hidden
    $parentSwapObserved = Wait-ForPath $parentSwapSignal 3000
    Assert $parentSwapObserved 'archive write-boundary hook must pause after creating the empty output parent'
    if ($parentSwapObserved) {
      Remove-Item -LiteralPath $parentSwapDirectory -Force
      New-Item -ItemType Junction -Path $parentSwapDirectory -Target $parentSwapPackage | Out-Null
      Set-Content -LiteralPath $parentSwapContinue -Value 'continue' -NoNewline -Encoding ASCII
    }
    if (-not $parentSwapProcess.WaitForExit(10000)) { $parentSwapProcess.Kill(); $parentSwapProcess.WaitForExit() }
    $parentSwapExit = $parentSwapProcess.ExitCode
    $parentSwapProcess.Dispose()
  } finally {
    $env:FFX_RELEASE_ARCHIVE_PARENT_TEST_HOOK = $oldParentHook
  }
  $parentSwapOutput = ((Get-Content -LiteralPath $parentSwapStdout -Raw -ErrorAction SilentlyContinue) + "`n" +
    (Get-Content -LiteralPath $parentSwapStderr -Raw -ErrorAction SilentlyContinue))
  Assert ($parentSwapExit -ne 0 -and $parentSwapOutput -match 'OUTPUT_CANONICAL_OVERLAP_PACKAGE' -and
    -not (Test-Path -LiteralPath $parentSwapZip)) `
    'archive must re-resolve package/ZIP/sidecar overlap after output-parent creation and before CreateNew'
  Assert ((Get-TreeFingerprint $parentSwapPackage) -ceq $parentSwapFingerprint) `
    'archive output-parent boundary swap must leave PackageRoot byte/entry unchanged'
  if (Test-Path -LiteralPath $parentSwapDirectory) { Remove-Item -LiteralPath $parentSwapDirectory -Force }

  $rootEscapeTarget = Join-Path $tmp 'archive-root-junction-target'
  Remove-Item -LiteralPath $releaseRoot -Recurse -Force
  New-Item -ItemType Directory -Path $rootEscapeTarget -Force | Out-Null
  New-Item -ItemType Junction -Path $releaseRoot -Target $rootEscapeTarget | Out-Null
  $rootEscapePackage = Join-Path $releaseRoot 'package'
  New-Package $rootEscapePackage $commit ([datetime]'2026-01-01T00:00:00Z')
  $rootEscapeZip = Join-Path $releaseRoot 'candidate.zip'
  $result = Invoke-Archive $rootEscapePackage $rootEscapeZip $commit
  Assert ($result.ExitCode -ne 0 -and $result.Output -match 'ALLOWED_ROOT_.*ESCAPE' -and
    -not (Test-Path -LiteralPath (Join-Path $rootEscapeTarget 'candidate.zip'))) `
    'archive must reject when the allowed release root itself is a junction outside the producing worktree'
} finally {
  $env:FFX_TEST_ARCHIVE_VERIFY_LOG = $null
  $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
  $resolvedTmp = [IO.Path]::GetFullPath($tmp)
  if ($resolvedTmp.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedTmp)) {
    Remove-Item -LiteralPath $resolvedTmp -Recurse -Force
  }
}

if ($fails.Count -gt 0) {
  $fails | ForEach-Object { Write-Output ('ASSERT-FAIL: ' + $_) }
  Write-Output ("ARCHIVE_TEST_FAIL assertions={0} failures={1}" -f $runs, $fails.Count)
  exit 1
}
Write-Output ("ARCHIVE_TEST_OK assertions={0}" -f $runs)
exit 0
