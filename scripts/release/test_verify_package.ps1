<#
  FFX Mod Studio — harness red/green do verify_package (Bloco 1). exit 0 = verde.
  Rode: pwsh -NoProfile -File scripts/release/test_verify_package.ps1
#>
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '\..\..')).Path
$verify = Join-Path $PSScriptRoot 'verify_package.ps1'
$assemblyScan = Join-Path $PSScriptRoot 'scan_assembly_portability.ps1'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('ffx-vf-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tmp | Out-Null
$fails = New-Object System.Collections.Generic.List[string]
$runs = 0

function Assert([bool]$ok, [string]$m) { if (-not $ok) { $script:fails.Add($m) } }

function New-Package([string]$dir, [bool]$dirty, [string]$mode = 'Diagnostic', [switch]$withManifest) {
  New-Item -ItemType Directory -Force $dir | Out-Null
  foreach ($f in @('FFXProjectEditor.exe','FFXProjectEditor.dll','FFXProjectEditor.deps.json','FFXProjectEditor.runtimeconfig.json','System.Private.CoreLib.dll')) {
    Set-Content -LiteralPath (Join-Path $dir $f) -Value 'x' -NoNewline -Encoding ASCII
  }
  foreach ($lang in @('pt','es','fr','de','it','ja','ko','zh')) {
    New-Item -ItemType Directory -Force (Join-Path $dir $lang) | Out-Null
    Set-Content -LiteralPath (Join-Path $dir "$lang\FFXProjectEditor.resources.dll") -Value 's' -NoNewline -Encoding ASCII
  }
  if ($withManifest) { Write-Manifest $dir $dirty $mode }
}

function Write-Manifest([string]$dir, [bool]$dirty, [string]$mode = 'Diagnostic') {
  $files = @()
  $rootLen = (Resolve-Path $dir).Path.Length
  foreach ($f in Get-ChildItem -LiteralPath $dir -Recurse -File | Sort-Object FullName) {
    $rel = $f.FullName.Substring($rootLen).TrimStart('\', '/').Replace('\', '/')
    if ($rel -eq 'release-manifest.json') { continue }
    $origin = if ($rel -match '^[a-z]{2}/FFXProjectEditor\.resources\.dll$') { 'satellite' }
      elseif ($rel -match '^FFXProjectEditor\.(exe|dll|deps\.json|runtimeconfig\.json)$') { 'app' }
      elseif ($rel -match '^tools/') { 'tool' }
      else { 'runtime' }
    $files += [ordered]@{ path = $rel; bytes = $f.Length; sha256 = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLower(); origin = $origin }
  }
  $m = [ordered]@{ schemaVersion = 2; product = 'FFX Mod Studio'; target = 'win-x64'; selfContained = $true
    sourceCommit = '070a0733de6a387101c793584bfd7336344fbc6d'; sourceDirty = $dirty; buildSdk = '10.0.301'; buildMode = $mode; files = $files }
  $m | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $dir 'release-manifest.json') -Encoding UTF8
}

function Run([string]$dir, [string]$mode) {
  $script:runs++
  $out = & pwsh -NoProfile -File $verify -PackageRoot $dir -Mode $mode 2>&1 | Out-String
  return [pscustomobject]@{ Code = $LASTEXITCODE; Out = $out }
}

function Run-Json([string]$scriptPath, [string]$dir, [string]$mode) {
  $script:runs++
  $stderrPath = Join-Path $tmp ('stderr-' + [guid]::NewGuid().ToString('N') + '.txt')
  $stdout = (& pwsh -NoProfile -File $scriptPath -PackageRoot $dir -Mode $mode -Json 2> $stderrPath) | Out-String
  $code = $LASTEXITCODE
  $stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { '' }
  Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue
  $parsed = $null
  try { $parsed = $stdout | ConvertFrom-Json -NoEnumerate } catch { Assert $false ('JSON output invalido: ' + $stdout) }
  return [pscustomobject]@{ Code = $code; Stdout = $stdout; Stderr = $stderr; Json = $parsed }
}

function Assert-ExactSet($actual, [string[]]$expected, [string]$name) {
  $actualSorted = @($actual | ForEach-Object { [string]$_ } | Sort-Object -Unique)
  $expectedSorted = @($expected | Sort-Object -Unique)
  Assert (($actualSorted -join "`n") -ceq ($expectedSorted -join "`n")) ("$name exact-set esperado=[{0}] obtido=[{1}]" -f ($expectedSorted -join ', '), ($actualSorted -join ', '))
}

function Get-GeneratorStringArray($ast, [string]$variableName) {
  $assignments = @($ast.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
      $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
      $node.Left.VariablePath.UserPath -ceq $variableName
  }, $true))
  Assert ($assignments.Count -eq 1) ("generator deve declarar exatamente uma vez `${0}" -f $variableName)
  if ($assignments.Count -ne 1) { return @() }
  return @($assignments[0].Right.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.StringConstantExpressionAst]
  }, $true) | ForEach-Object Value)
}

function Get-DirectExpressionAst($containerAst, [string]$context) {
  $node = $containerAst
  if ($node -is [System.Management.Automation.Language.PipelineAst]) {
    if ($node.PipelineElements.Count -ne 1) { throw "$context must contain exactly one pipeline element" }
    $node = $node.PipelineElements[0]
  }
  if ($node -isnot [System.Management.Automation.Language.CommandExpressionAst]) {
    throw "$context must be a direct expression"
  }
  if ($node.Redirections.Count -ne 0) { throw "$context must not contain redirections" }
  return $node.Expression
}

function Get-DirectHashtableAst($containerAst, [string]$context) {
  $expression = Get-DirectExpressionAst $containerAst $context
  if ($expression -is [System.Management.Automation.Language.ConvertExpressionAst]) {
    $expression = $expression.Child
  }
  if ($expression -isnot [System.Management.Automation.Language.HashtableAst]) {
    throw "$context must be a direct hashtable"
  }
  return $expression
}

function Get-UniqueHashtableMemberValueAst($hashtableAst, [string]$memberName, [string]$context) {
  $members = @($hashtableAst.KeyValuePairs | Where-Object {
    $_.Item1 -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
      $_.Item1.Value -ceq $memberName
  })
  if ($members.Count -ne 1) { throw "$context must contain exactly one '$memberName' member" }
  return $members[0].Item2
}

function Get-PackagePolicyDenyHashtableAst($ast) {
  $assignments = @($ast.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
      $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
      $node.Left.VariablePath.UserPath -ceq 'packagePolicy'
  }, $true))
  if ($assignments.Count -ne 1) { throw 'generator must assign $packagePolicy exactly once' }

  $packagePolicy = Get-DirectHashtableAst $assignments[0].Right '$packagePolicy'
  $componentsValue = Get-UniqueHashtableMemberValueAst $packagePolicy 'components' '$packagePolicy'
  $components = Get-DirectHashtableAst $componentsValue '$packagePolicy.components'
  $coreValue = Get-UniqueHashtableMemberValueAst $components 'studio-core-win-x64' '$packagePolicy.components'
  $core = Get-DirectHashtableAst $coreValue '$packagePolicy.components.studio-core-win-x64'
  $denyValue = Get-UniqueHashtableMemberValueAst $core 'deny' '$packagePolicy.components.studio-core-win-x64'
  return (Get-DirectHashtableAst $denyValue '$packagePolicy.components.studio-core-win-x64.deny')
}

function Get-DirectArrayVariableName($valueAst, [string]$context) {
  $expression = Get-DirectExpressionAst $valueAst $context
  if ($expression -isnot [System.Management.Automation.Language.ArrayExpressionAst]) {
    throw "$context must use an explicit array expression"
  }
  $statements = @($expression.SubExpression.Statements)
  if ($statements.Count -ne 1 -or $expression.SubExpression.Traps.Count -ne 0) {
    throw "$context array must contain exactly one expression"
  }
  $innerExpression = Get-DirectExpressionAst $statements[0] "$context array"
  if ($innerExpression -isnot [System.Management.Automation.Language.VariableExpressionAst]) {
    throw "$context array must contain one variable reference"
  }
  return $innerExpression.VariablePath.UserPath
}

function Test-OfficialDenyPolicyBindings($ast) {
  try {
    $deny = Get-PackagePolicyDenyHashtableAst $ast
    $expectedBindings = [ordered]@{
      paths = 'officialDenyPaths'
      extensions = 'officialDenyExtensions'
      patterns = 'officialDenyPatterns'
    }
    foreach ($memberName in $expectedBindings.Keys) {
      $valueAst = Get-UniqueHashtableMemberValueAst $deny $memberName '$packagePolicy.components.studio-core-win-x64.deny'
      $actualVariable = Get-DirectArrayVariableName $valueAst ("deny.$memberName")
      if ($actualVariable -cne $expectedBindings[$memberName]) {
        throw ("deny.{0} must reference `${1}; got `${2}" -f $memberName, $expectedBindings[$memberName], $actualVariable)
      }
    }
    return [pscustomobject]@{ Passed = $true; Message = '' }
  } catch {
    return [pscustomobject]@{ Passed = $false; Message = $_.Exception.Message }
  }
}

function Replace-AstExtent([string]$source, $extent, [string]$replacement) {
  if ($extent.StartOffset -lt 0 -or $extent.EndOffset -gt $source.Length -or $extent.StartOffset -gt $extent.EndOffset) {
    throw 'AST extent is outside generator source bounds'
  }
  return $source.Substring(0, $extent.StartOffset) + $replacement + $source.Substring($extent.EndOffset)
}

function Parse-GeneratorMutation([string]$source, [string]$name) {
  $tokens = $null
  $parseErrors = $null
  $ast = [System.Management.Automation.Language.Parser]::ParseInput(
    $source,
    [ref]$tokens,
    [ref]$parseErrors)
  Assert (@($parseErrors).Count -eq 0) ("$name deve continuar sintaticamente valido")
  return $ast
}

# The checked-in contract and its generator are one official absence policy.
# Parse literal generator arrays instead of running it against a dirty payload.
$officialPolicyPath = Join-Path $repo 'release\package-allowlist.json'
$officialPolicy = Get-Content -LiteralPath $officialPolicyPath -Raw | ConvertFrom-Json
$officialCore = $officialPolicy.components.'studio-core-win-x64'
$officialDenyPaths = [string[]]@($officialCore.deny.paths)
$officialDenyExtensions = [string[]]@($officialCore.deny.extensions)
$officialDenyPatterns = [string[]]@($officialCore.deny.patterns)
$generatorPath = Join-Path $PSScriptRoot 'generate_release_contracts.ps1'
$generatorTokens = $null
$generatorParseErrors = $null
$generatorAst = [System.Management.Automation.Language.Parser]::ParseFile(
  $generatorPath,
  [ref]$generatorTokens,
  [ref]$generatorParseErrors)
Assert (@($generatorParseErrors).Count -eq 0) 'generate_release_contracts.ps1 deve parsear sem erros'
$bindingResult = Test-OfficialDenyPolicyBindings $generatorAst
Assert $bindingResult.Passed ('gerador deve ligar deny members as listas oficiais: ' + $bindingResult.Message)
$generatorDenyPaths = @(Get-GeneratorStringArray $generatorAst 'officialDenyPaths')
$generatorDenyExtensions = @(Get-GeneratorStringArray $generatorAst 'officialDenyExtensions')
$generatorDenyPatterns = @(Get-GeneratorStringArray $generatorAst 'officialDenyPatterns')
Assert (($generatorDenyPaths -join "`n") -ceq ($officialDenyPaths -join "`n")) 'deny.paths do gerador e contrato versionado devem ser identicos e ordenados'
Assert (($generatorDenyExtensions -join "`n") -ceq ($officialDenyExtensions -join "`n")) 'deny.extensions do gerador e contrato versionado devem ser identicos e ordenados'
Assert (($generatorDenyPatterns -join "`n") -ceq ($officialDenyPatterns -join "`n")) 'deny.patterns do gerador e contrato versionado devem ser identicos e ordenados'
Assert ($officialDenyPaths.Count -eq @($officialDenyPaths | Sort-Object -Unique).Count) 'deny.paths oficial nao pode conter duplicatas'
Assert ($officialDenyExtensions.Count -eq @($officialDenyExtensions | Sort-Object -Unique).Count) 'deny.extensions oficial nao pode conter duplicatas'
Assert ($officialDenyPatterns.Count -eq @($officialDenyPatterns | Sort-Object -Unique).Count) 'deny.patterns oficial nao pode conter duplicatas'

# Adversarial AST mutations prove that list parity alone cannot hide a disconnected
# or cross-wired packagePolicy member. Mutations stay in memory and never regenerate contracts.
$generatorSource = Get-Content -LiteralPath $generatorPath -Raw
$denyAst = Get-PackagePolicyDenyHashtableAst $generatorAst
$pathsValueAst = Get-UniqueHashtableMemberValueAst $denyAst 'paths' '$packagePolicy.components.studio-core-win-x64.deny'
$emptyPathsSource = Replace-AstExtent $generatorSource $pathsValueAst.Extent '@()'
$emptyPathsAst = Parse-GeneratorMutation $emptyPathsSource 'mutacao paths=@()'
$emptyPathsBinding = Test-OfficialDenyPolicyBindings $emptyPathsAst
Assert (-not $emptyPathsBinding.Passed) 'mutacao paths=@() deve quebrar a validacao de binding AST'
Assert ($emptyPathsBinding.Message -ceq 'deny.paths array must contain exactly one expression') ('mutacao paths=@() falhou pelo motivo inesperado: ' + $emptyPathsBinding.Message)

$patternsValueAst = Get-UniqueHashtableMemberValueAst $denyAst 'patterns' '$packagePolicy.components.studio-core-win-x64.deny'
$crossWiredSource = Replace-AstExtent $generatorSource $patternsValueAst.Extent '@($officialDenyExtensions)'
$crossWiredAst = Parse-GeneratorMutation $crossWiredSource 'mutacao patterns cruzado com extensions'
$crossWiredBinding = Test-OfficialDenyPolicyBindings $crossWiredAst
Assert (-not $crossWiredBinding.Passed) 'mutacao de variavel cruzada deve quebrar a validacao de binding AST'
Assert ($crossWiredBinding.Message -ceq 'deny.patterns must reference $officialDenyPatterns; got $officialDenyExtensions') ('mutacao cruzada falhou pelo motivo inesperado: ' + $crossWiredBinding.Message)

# Manifest validation must not depend on the repository's live release policy.
$cases = @(
  @{ d='neg_sd'; dirty=$true; mode='Candidate'; exp=1; sub='SOURCE_DIRTY_FORBIDDEN_IN_Candidate'; name='Candidate dirty' }
)
foreach ($c in $cases) {
  $dir = Join-Path $tmp $c.d
  New-Package -dir $dir -dirty $c.dirty -mode $c.mode -withManifest
  if ($c.name -eq 'extra file') { Set-Content -LiteralPath (Join-Path $dir 'sneaky.extra') -Value 'e' -NoNewline -Encoding ASCII }
  $r = Run $dir $c.mode
  if ($env:FFX_VERIFY_TEST_TRACE -eq '1') { Write-Output ("TRACE [{0}] code={1}`n{2}" -f $c.name, $r.Code, $r.Out) }
  Assert ($r.Code -eq $c.exp) ("[{0}] exp {1} obt {2}; output={3}" -f $c.name, $c.exp, $r.Code, $r.Out.Trim())
  if ($c.sub) { Assert ($r.Out -match [regex]::Escape($c.sub)) ("[{0}] msg '{1}'; output={2}" -f $c.name, $c.sub, $r.Out.Trim()) }
  if ($r.Out -match 'MethodException|RuntimeException') { Assert $false ("[{0}] excecao nao tratada" -f $c.name) }
}

# negativos por mutação de manifest (bytes/abs/traversal/dup) + BLOCKED dedup
function Mutate([string]$d, [scriptblock]$sb, [string]$name) {
  $dir = Join-Path $tmp $d
  New-Package -dir $dir -dirty $true -withManifest
  $j = Get-Content (Join-Path $dir 'release-manifest.json') -Raw | ConvertFrom-Json
  & $sb $j
  $j | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $dir 'release-manifest.json') -Encoding UTF8
  return (Run $dir 'Diagnostic')
}

# These three contract failures replace the former assertions that deliberately
# expected the live repository policy to be incomplete. They remain hermetic as
# each failure is resolved before policy or payload validation starts.
$r = Mutate 'neg_product' { param($j) $j.product = 'Different Product' } 'product mismatch'
Assert ($r.Code -eq 1) 'product mismatch deve falhar'
Assert ($r.Out -match 'MANIFEST_PRODUCT_INVALID') 'product -> MANIFEST_PRODUCT_INVALID'

$r = Mutate 'neg_target' { param($j) $j.target = 'win-arm64' } 'target mismatch'
Assert ($r.Code -eq 1) 'target mismatch deve falhar'
Assert ($r.Out -match 'MANIFEST_TARGET_INVALID') 'target -> MANIFEST_TARGET_INVALID'

$r = Mutate 'neg_self_contained' { param($j) $j.selfContained = $false } 'self-contained false'
Assert ($r.Code -eq 1) 'self-contained false deve falhar'
Assert ($r.Out -match 'MANIFEST_SELF_CONTAINED_INVALID') 'self-contained -> MANIFEST_SELF_CONTAINED_INVALID'

$r = Mutate 'neg_bytes' { param($j) $j.files[0].bytes = 1.5 } 'bytes decimal'
Assert ($r.Code -eq 1) 'bytes 1.5 deve falhar'
Assert ($r.Out -match 'MANIFEST_BAD_BYTES') 'bytes -> MANIFEST_BAD_BYTES'

$r = Mutate 'neg_abs' { param($j) $j.files[0].path = 'C:/evil/path.dll' } 'path absoluto'
Assert ($r.Code -eq 1) 'path absoluto deve falhar'
Assert ($r.Out -match 'MANIFEST_PATH_ABS') 'abs -> MANIFEST_PATH_ABS'

$r = Mutate 'neg_slashroot' { param($j) $j.files[0].path = '/evil.dll' } 'slash root'
Assert ($r.Code -eq 1) 'slash root deve falhar'

$r = Mutate 'neg_trav' { param($j) $j.files[0].path = '../evil.dll' } 'traversal'
Assert ($r.Code -eq 1) 'traversal deve falhar'
Assert ($r.Out -match 'MANIFEST_PATH_TRAVERSAL') '.. -> MANIFEST_PATH_TRAVERSAL'

$r = Mutate 'neg_ads' { param($j) $j.files[0].path = 'a.dll:stream' } 'ADS'
Assert ($r.Code -eq 1) 'ADS deve falhar'

$r = Mutate 'neg_dos' { param($j) $j.files[0].path = 'CON.dll' } 'DOS reservado'
Assert ($r.Code -eq 1) 'CON.dll deve falhar'
Assert ($r.Out -match 'MANIFEST_PATH_DOS_RESERVED') 'CON -> DOS_RESERVED'

$r = Mutate 'neg_dup' { param($j) $j.files += $j.files[0] } 'path duplicado'
Assert ($r.Code -eq 1) 'dup deve falhar'
Assert ($r.Out -match 'MANIFEST_DUP_PATH') 'dup -> MANIFEST_DUP_PATH'

$r = Mutate 'neg_sha_syntax' { param($j) $j.files[0].sha256 = 'g' * 64 } 'SHA sintatica invalida'
Assert ($r.Code -eq 1) 'SHA sintatica deve falhar'
Assert ($r.Out -match 'MANIFEST_BAD_SHA') 'SHA com g -> MANIFEST_BAD_SHA'

$r = Mutate 'neg_hash' { param($j) $j.files[0].sha256 = '1' * 64 } 'hash valido divergente'
Assert ($r.Code -eq 1) 'hash sintaticamente valido e divergente deve falhar no payload'
Assert ($r.Out -match 'HASH_MISMATCH') 'hash divergente -> HASH_MISMATCH'

$r = Mutate 'neg_origin' { param($j) $j.files[0].origin = 'bogus' } 'origin fora enum'
Assert ($r.Code -eq 1) 'origin bogus deve falhar'
Assert ($r.Out -match 'MANIFEST_BAD_ORIGIN') 'origin -> BAD_ORIGIN'

# BLOCKED dedup: fileName + glob atingindo o mesmo arquivo devem gerar 1 falha
$dir = Join-Path $tmp 'neg_blocked'
New-Package -dir $dir -dirty $true -withManifest
New-Item -ItemType Directory -Force (Join-Path $dir 'x64') | Out-Null
Set-Content -LiteralPath (Join-Path $dir 'x64\keystone.dll') -Value 'k' -NoNewline -Encoding ASCII
$j = Get-Content (Join-Path $dir 'release-manifest.json') -Raw | ConvertFrom-Json
$j.files += [ordered]@{ path='x64/keystone.dll'; bytes=1; sha256=(Get-FileHash (Join-Path $dir 'x64\keystone.dll') -Algorithm SHA256).Hash.ToLower(); origin='tool' }
$j | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $dir 'release-manifest.json') -Encoding UTF8
$r = Run $dir 'Diagnostic'
# keystone agora é BUNDLED_OWNER_ACCEPTED_RISK (decisão do dono), não BLOCKED — sem assertion negativa.
$n = @($r.Out | Select-String -Pattern 'BLOCKED_COMPONENT_PRESENT: .*keystone\.dll').Count
# (a dedup por identidade normalizada é coberta pela lógica do verify_package)

$r = Mutate 'neg_bytes_str' { param($j) $j.files[0].bytes = 'abc' } 'bytes string'
Assert ($r.Code -eq 1) 'bytes string deve falhar'
Assert ($r.Out -notmatch 'InvalidArgument|InvalidOperation|MethodException|RuntimeException|verify_package\.ps1:') 'bytes string sem exception'
Assert ($r.Out -match 'MANIFEST_BAD_BYTES') 'bytes string -> MANIFEST_BAD_BYTES'

$r = Mutate 'neg_sha_null' { param($j) $j.files[0].sha256 = $null } 'sha null'
Assert ($r.Code -eq 1) 'sha null deve falhar'
Assert ($r.Out -notmatch 'InvalidArgument|InvalidOperation|MethodException|RuntimeException|verify_package\.ps1:') 'sha null sem exception'
Assert ($r.Out -match 'MANIFEST_BAD_SHA') 'sha null -> MANIFEST_BAD_SHA'

$r = Mutate 'neg_repeat' { param($j) $j.files[0].path = 'a//b.dll' } 'separador repetido'
Assert ($r.Code -eq 1) 'a//b deve falhar'
Assert ($r.Out -match 'MANIFEST_PATH_REPEATED_SEPARATOR') 'a//b -> MANIFEST_PATH_REPEATED_SEPARATOR'
Assert ($r.Out -notmatch 'EXTRA_FILE|FILE_MISSING') 'a//b isolado (gate Fase 1)'

# Documento inválido ou com shape errado deve parar na Fase 1 com conjunto exato.
$dir = Join-Path $tmp 'manifest_bad_json'
New-Package -dir $dir -dirty $true -withManifest
Set-Content -LiteralPath (Join-Path $dir 'release-manifest.json') -Value '{ invalid' -Encoding UTF8
$r = Run-Json $verify $dir 'Diagnostic'
Assert ($r.Code -eq 1 -and $r.Json.phase -eq 'manifest-contract') 'manifest JSON inválido: fase/exit'
Assert-ExactSet $r.Json.errors @('MANIFEST_JSON_INVALID') 'manifest JSON inválido errors'
Assert-ExactSet $r.Json.infrastructureErrors @() 'manifest JSON inválido infrastructureErrors'

$dir = Join-Path $tmp 'manifest_wrong_shape'
New-Package -dir $dir -dirty $true -withManifest
Set-Content -LiteralPath (Join-Path $dir 'release-manifest.json') -Value '[]' -Encoding UTF8
$r = Run-Json $verify $dir 'Diagnostic'
Assert ($r.Code -eq 1 -and $r.Json.phase -eq 'manifest-contract') 'manifest array: fase/exit'
Assert-ExactSet $r.Json.errors @('MANIFEST_TOP_LEVEL_NOT_OBJECT') 'manifest array errors'

# Scanner de assemblies precisa oferecer JSON puro e evidência estável para o verifier.
$asmDir = Join-Path $tmp 'assembly_json'
New-Item -ItemType Directory -Force $asmDir | Out-Null
Set-Content -LiteralPath (Join-Path $asmDir 'baked.exe') -Value 'D:\FFX Extracted\payload' -NoNewline -Encoding ASCII
Set-Content -LiteralPath (Join-Path $asmDir 'bounded.dll') -Value ('C:\' + ('a' * 400)) -NoNewline -Encoding ASCII
$asmStderrPath = Join-Path $tmp 'assembly-json-stderr.txt'
$asmRaw = (& pwsh -NoProfile -File $assemblyScan -Root $asmDir -Json 2> $asmStderrPath) | Out-String
$asmCode = $LASTEXITCODE
$asmStderr = Get-Content -LiteralPath $asmStderrPath -Raw -ErrorAction SilentlyContinue
$asmJson = $null
try { $asmJson = $asmRaw | ConvertFrom-Json } catch { Assert $false 'assembly scanner -Json deve emitir um objeto JSON puro' }
Assert ($asmCode -eq 2) 'assembly scanner com path baked deve sair 2'
Assert ([string]::IsNullOrEmpty($asmStderr)) 'assembly scanner -Json findings: stderr vazio'
Assert ($asmJson.schemaVersion -eq 1) 'assembly scanner JSON schemaVersion=1'
Assert (@($asmJson.matches).Count -ge 1) 'assembly scanner JSON deve conter findings'
$asmFinding = @($asmJson.matches | Where-Object { $_.file -eq 'baked.exe' -and $_.sha256 -match '^[0-9a-f]{64}$' })
Assert ($asmFinding.Count -ge 1) 'assembly scanner finding deve ter path relativo + SHA-256'
Assert (@($asmFinding | Where-Object { $_.encoding -in @('ascii-utf8','utf16le-even','utf16le-odd') }).Count -ge 1) 'assembly scanner finding deve classificar encoding'
Assert (@($asmFinding | Where-Object { $_.pattern -eq 'drive-absolute' -and $_.encoding -eq 'ascii-utf8' }).Count -eq 1) 'D:/ real deve continuar detectado'
Assert (@($asmFinding | Where-Object { $_.pattern -eq 'drive-absolute' -and $_.value -like 'D:\FFX Extracted*' }).Count -eq 1) 'finding de path deve trazer primeiro valor auditável'
$boundedFinding = @($asmJson.matches | Where-Object { $_.file -eq 'bounded.dll' -and $_.pattern -eq 'drive-absolute' -and $_.encoding -eq 'ascii-utf8' })
Assert ($boundedFinding.Count -eq 1 -and $boundedFinding[0].value.Length -eq 256) 'value deve ser bounded em 256 caracteres sem exceção'
Assert (@($asmJson.matches | Where-Object { $_.value -isnot [string] -or $_.value.Length -gt 256 }).Count -eq 0) 'todo finding deve ter value string sanitizada <=256'

$uriAsmDir = Join-Path $tmp 'assembly_uri_boundary'
New-Item -ItemType Directory -Force $uriAsmDir | Out-Null
Set-Content -LiteralPath (Join-Path $uriAsmDir 'uris.dll') -Value 'http://one.invalid https://two.invalid urn:x:/not-a-drive identifierD:/not-a-drive Z:/) g:/t S:/d+' -NoNewline -Encoding ASCII
$asmRaw = (& pwsh -NoProfile -File $assemblyScan -Root $uriAsmDir -Json 2> $asmStderrPath) | Out-String
$asmJson = $asmRaw | ConvertFrom-Json -NoEnumerate
Assert (@($asmJson.matches | Where-Object pattern -eq 'drive-absolute').Count -eq 0) 'http/https/urn/identifier não podem virar drive-absolute'
Assert (@($asmJson.matches | Where-Object pattern -eq 'remote-url').Count -eq 1) 'URLs reais continuam classificadas sem falso drive'

$utfAsmDir = Join-Path $tmp 'assembly_utf16_parity'
New-Item -ItemType Directory -Force $utfAsmDir | Out-Null
$utfBody = [Text.Encoding]::Unicode.GetBytes('C:\Users\fixture\payload')
[IO.File]::WriteAllBytes((Join-Path $utfAsmDir 'even.dll'), $utfBody)
$oddBody = New-Object byte[] ($utfBody.Length + 1)
$oddBody[0] = 0xff
[Array]::Copy($utfBody, 0, $oddBody, 1, $utfBody.Length)
[IO.File]::WriteAllBytes((Join-Path $utfAsmDir 'odd.dll'), $oddBody)
$asmRaw = (& pwsh -NoProfile -File $assemblyScan -Root $utfAsmDir -Json 2> $asmStderrPath) | Out-String
$asmJson = $asmRaw | ConvertFrom-Json -NoEnumerate
Assert (@($asmJson.matches | Where-Object { $_.file -eq 'even.dll' -and $_.pattern -eq 'drive-absolute' -and $_.encoding -eq 'utf16le-even' }).Count -eq 1) 'UTF-16LE even real deve ser detectado'
Assert (@($asmJson.matches | Where-Object { $_.file -eq 'even.dll' -and $_.pattern -eq 'drive-absolute' -and $_.encoding -eq 'utf16le-odd' }).Count -eq 0) 'UTF-16LE even não pode gerar lixo na parity odd'
Assert (@($asmJson.matches | Where-Object { $_.file -eq 'odd.dll' -and $_.pattern -eq 'drive-absolute' -and $_.encoding -eq 'utf16le-odd' }).Count -eq 1) 'UTF-16LE odd real deve ser detectado'
Assert (@($asmJson.matches | Where-Object { $_.file -eq 'odd.dll' -and $_.pattern -eq 'drive-absolute' -and $_.encoding -eq 'utf16le-even' }).Count -eq 0) 'UTF-16LE odd não pode gerar lixo na parity even'

$cleanAsmDir = Join-Path $tmp 'assembly_clean'
New-Item -ItemType Directory -Force $cleanAsmDir | Out-Null
Set-Content -LiteralPath (Join-Path $cleanAsmDir 'clean.dll') -Value 'fixture-clean' -NoNewline -Encoding ASCII
$asmRaw = (& pwsh -NoProfile -File $assemblyScan -Root $cleanAsmDir -Json 2> $asmStderrPath) | Out-String
$asmCode = $LASTEXITCODE
$asmJson = $asmRaw | ConvertFrom-Json -NoEnumerate
Assert ($asmCode -eq 0 -and $asmJson.status -eq 'clean' -and $asmJson.exitCode -eq 0) 'assembly scanner clean: schema/exit estáveis'
Assert (@($asmJson.matches).Count -eq 0 -and @($asmJson.errors).Count -eq 0) 'assembly scanner clean: arrays vazios'

$unsupportedRoot = Join-Path $tmp 'unsupported.txt'
Set-Content -LiteralPath $unsupportedRoot -Value 'fixture' -NoNewline -Encoding ASCII
$asmRaw = (& pwsh -NoProfile -File $assemblyScan -Root $unsupportedRoot -Json 2> $asmStderrPath) | Out-String
$asmCode = $LASTEXITCODE
$asmJson = $asmRaw | ConvertFrom-Json -NoEnumerate
Assert ($asmCode -eq 1 -and $asmJson.status -eq 'error' -and $asmJson.exitCode -eq 1) 'assembly scanner unsupported root: fail closed'
Assert-ExactSet $asmJson.errors @('ROOT_UNSUPPORTED_FILE:.txt') 'assembly scanner unsupported errors'
Assert ([string]::IsNullOrEmpty((Get-Content -LiteralPath $asmStderrPath -Raw -ErrorAction SilentlyContinue))) 'assembly scanner -Json error: stderr vazio'

# ---- integração hermética: policy válida + runtime self-contained real ----
$isolatedRepo = Join-Path $tmp 'isolated-policy-repo'
$isolatedScripts = Join-Path $isolatedRepo 'scripts\release'
$isolatedRelease = Join-Path $isolatedRepo 'release'
New-Item -ItemType Directory -Force $isolatedScripts, $isolatedRelease | Out-Null
foreach ($scriptName in @('verify_package.ps1','scan_portability.ps1','scan_assembly_portability.ps1')) {
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot $scriptName) -Destination (Join-Path $isolatedScripts $scriptName) -Force
}
$isolatedVerify = Join-Path $isolatedScripts 'verify_package.ps1'
$isolatedAssemblyScan = Join-Path $isolatedScripts 'scan_assembly_portability.ps1'

function Get-AssemblyExceptions([string]$packageRoot) {
  $stderrPath = Join-Path $tmp ('asm-stderr-' + [guid]::NewGuid().ToString('N') + '.txt')
  $raw = (& pwsh -NoProfile -File $isolatedAssemblyScan -Root $packageRoot -Json 2> $stderrPath) | Out-String
  $code = $LASTEXITCODE
  $stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { '' }
  Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue
  Assert ([string]::IsNullOrEmpty($stderr)) 'assembly fixture scanner: stderr vazio'
  Assert ($code -in @(0,2)) ('assembly fixture scanner: exit 0/2, obtido ' + $code)
  $json = $raw | ConvertFrom-Json -NoEnumerate
  return @($json.matches | ForEach-Object {
    [ordered]@{ scanner = 'assembly'; path = $_.file; sha256 = $_.sha256; pattern = $_.pattern; class = $_.class; encoding = $_.encoding; maxCount = $_.count }
  })
}

function Get-TopLevelFiles([string]$packageRoot) {
  $names = @(Get-ChildItem -LiteralPath $packageRoot -File | Select-Object -ExpandProperty Name)
  if ('release-manifest.json' -notin $names) { $names += 'release-manifest.json' }
  return @($names | Sort-Object -Unique)
}

function Write-IsolatedContracts(
  [string]$packageRoot,
  [object[]]$toolEntries = @(),
  [string[]]$allowedTopLevel = @(),
  [string[]]$denyPaths = @(),
  [string[]]$denyExtensions = @('.pdb'),
  [string[]]$denyPatterns = @(),
  [object[]]$assemblyExceptions = @(),
  [object[]]$scanExceptions = @()
) {
  if ($allowedTopLevel.Count -eq 0) { $allowedTopLevel = @(Get-TopLevelFiles $packageRoot) }
  $roots = @(Get-ChildItem -LiteralPath $packageRoot -Directory | ForEach-Object { $_.Name + '/' } | Sort-Object -Unique)
  $policy = [ordered]@{
    schemaVersion = 1
    components = [ordered]@{
      'studio-core-win-x64' = [ordered]@{
        target = 'win-x64'; selfContained = $true; hardLimitBytes = 524288000; warnLimitBytes = 419430400
        requiredFiles = @('FFXProjectEditor.exe','FFXProjectEditor.dll','FFXProjectEditor.deps.json','FFXProjectEditor.runtimeconfig.json','release-manifest.json')
        requiredSatellites = @('pt','es','fr','de','it','ja','ko','zh')
        allowedRoots = $roots
        allowedTopLevel = @($allowedTopLevel)
        scanAllowlist = @($assemblyExceptions + $scanExceptions)
        deny = [ordered]@{ paths = @($denyPaths); extensions = @($denyExtensions); patterns = @($denyPatterns) }
      }
    }
  }
  $tools = [ordered]@{ schemaVersion = 1; entries = @($toolEntries); denyByDefault = @(); pendingFullClosure = $false }
  $official = [ordered]@{
    schemaVersion = 1; canonicalSiteOrigin = 'https://ffxmodstudio.com'; brazilSiteOrigin = 'https://ffxmodstudio.com.br'
    studioReleaseApiOrigin = 'https://ffxmodstudio.com'; studioReleasePathTemplate = '/api/releases/studio/{channel}'
  }
  $noclip = [ordered]@{ schemaVersion = 1; state = 'BLOCKED_RELEASE' }
  $runtimeVersion = '8.0.0'
  try {
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $packageRoot 'FFXProjectEditor.runtimeconfig.json') -Raw | ConvertFrom-Json
    $runtimeVersion = [string](@($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App' | Select-Object -First 1).version)
    if ($runtimeVersion -notmatch '^8[.]\d+[.]\d+$') { $runtimeVersion = '8.0.0' }
  } catch { $runtimeVersion = '8.0.0' }
  $dotnetPins = @(
    @{ path='coreclr.dll'; architecture='x64'; peMachine='x64' },
    @{ path='hostfxr.dll'; architecture='x64'; peMachine='x64' },
    @{ path='hostpolicy.dll'; architecture='x64'; peMachine='x64' },
    @{ path='System.Private.CoreLib.dll'; architecture='managed-any'; peMachine='managed' }
  ) | ForEach-Object {
    $pinPath = Join-Path $packageRoot $_.path
    [ordered]@{ path=$_.path; architecture=$_.architecture; peMachine=$_.peMachine; sha256=if(Test-Path -LiteralPath $pinPath){(Get-FileHash -LiteralPath $pinPath -Algorithm SHA256).Hash.ToLowerInvariant()}else{'0' * 64} }
  }
  $runtimePolicy = [ordered]@{
    schemaVersion=1; target=[ordered]@{rid='win-x64';architecture='x64'}; strictModes=@('Candidate','Release')
    components=[ordered]@{dotnetSelfContained=[ordered]@{
      required=$true; delivery='self-contained-payload'; architecture='x64'; versionPin=$runtimeVersion
      runtimeConfigPath='FFXProjectEditor.runtimeconfig.json'; depsPath='FFXProjectEditor.deps.json'
      forbiddenDependencies=@('Tmds.DBus.Protocol','Avalonia.X11','Avalonia.FreeDesktop'); payloadFiles=@($dotnetPins)
    }}
  }
  $policy | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $isolatedRelease 'package-allowlist.json') -Encoding UTF8
  $tools | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $isolatedRelease 'tool-dependencies.json') -Encoding UTF8
  $official | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $isolatedRelease 'official-origins.json') -Encoding UTF8
  $noclip | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $isolatedRelease 'noclip-runtime.manifest.json') -Encoding UTF8
  $runtimePolicy | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $isolatedRelease 'windows-runtime-prerequisites.json') -Encoding UTF8
}

$tempDrive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($tmp))
Assert ($tempDrive.AvailableFreeSpace -gt 1GB) 'fixture publish: exige >1 GiB livre no TEMP drive'
$miniSource = Join-Path $tmp 'mini-source'
$validPackage = Join-Path $tmp 'valid-package'
New-Item -ItemType Directory -Force $miniSource | Out-Null
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>FFXProjectEditor</AssemblyName>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <UseAppHost>true</UseAppHost>
    <DebugSymbols>false</DebugSymbols>
    <DebugType>none</DebugType>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $miniSource 'Mini.csproj') -Encoding UTF8
'using System; Console.WriteLine("fixture");' | Set-Content -LiteralPath (Join-Path $miniSource 'Program.cs') -Encoding UTF8
& dotnet publish (Join-Path $miniSource 'Mini.csproj') -c Release -r win-x64 --self-contained true --nologo -p:RestoreIgnoreFailedSources=true -o $validPackage | Out-Null
Assert ($LASTEXITCODE -eq 0) 'fixture publish self-contained deve passar'
Get-ChildItem -LiteralPath $validPackage -Filter *.pdb -File -ErrorAction SilentlyContinue | Remove-Item -Force
foreach ($lang in @('pt','es','fr','de','it','ja','ko','zh')) {
  $langDir = Join-Path $validPackage $lang
  New-Item -ItemType Directory -Force $langDir | Out-Null
  Copy-Item -LiteralPath (Join-Path $validPackage 'FFXProjectEditor.dll') -Destination (Join-Path $langDir 'FFXProjectEditor.resources.dll') -Force
}
Write-Manifest $validPackage $true 'Diagnostic'
$baselineTopLevel = @(Get-TopLevelFiles $validPackage)
$baselineAssemblyExceptions = @(Get-AssemblyExceptions $validPackage)
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions

$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 0) ('valid self-contained: esperado 0, obtido ' + $r.Code + ' ' + $r.Stdout)
Assert ([string]::IsNullOrEmpty($r.Stderr)) 'valid self-contained: stderr vazio em -Json'
Assert ($r.Json.status -eq 'verified' -and $r.Json.phase -eq 'complete') 'valid self-contained: status/phase'
Assert-ExactSet $r.Json.errors @() 'valid self-contained errors'
Assert-ExactSet $r.Json.infrastructureErrors @() 'valid self-contained infrastructureErrors'

# Task 5 public-payload absence fixtures. Each case first allowlists the fixture's
# location, then proves that the official deny contract rejects it in the
# payload-inventory phase. No fixture supplies a private policy override.
function Assert-DeniedPayloadFixture(
  [Parameter(Mandatory=$true)][string]$relativePath,
  [Parameter(Mandatory=$true)][string]$content,
  [Parameter(Mandatory=$true)][string[]]$expectedErrors,
  [Parameter(Mandatory=$true)][string]$name
) {
  $normalizedRelative = $relativePath.Replace('\', '/')
  $segments = @($normalizedRelative -split '/')
  $fixturePath = Join-Path $validPackage ($normalizedRelative.Replace('/', '\'))
  $ownedFixtureRoot = if ($segments.Count -eq 1) { $fixturePath } else { Join-Path $validPackage $segments[0] }
  Assert (-not (Test-Path -LiteralPath $ownedFixtureRoot)) ("$name fixture root must be attempt-owned: $ownedFixtureRoot")
  if (Test-Path -LiteralPath $ownedFixtureRoot) { return }

  try {
    New-Item -ItemType Directory -Force (Split-Path -Parent $fixturePath) | Out-Null
    Set-Content -LiteralPath $fixturePath -Value $content -NoNewline -Encoding ASCII
    Write-Manifest $validPackage $true 'Diagnostic'
    $fixtureTopLevel = @($baselineTopLevel)
    if ($segments.Count -eq 1) { $fixtureTopLevel += $segments[0] }
    Write-IsolatedContracts `
      -packageRoot $validPackage `
      -allowedTopLevel $fixtureTopLevel `
      -denyPaths $officialDenyPaths `
      -denyExtensions $officialDenyExtensions `
      -denyPatterns $officialDenyPatterns `
      -assemblyExceptions $baselineAssemblyExceptions
    $fixtureResult = Run-Json $isolatedVerify $validPackage 'Diagnostic'
    Assert ($fixtureResult.Code -eq 1 -and $fixtureResult.Json.phase -eq 'payload-inventory') ("$name must fail in payload-inventory")
    Assert-ExactSet $fixtureResult.Json.errors $expectedErrors ($name + ' errors')
  } finally {
    Remove-Item -LiteralPath $ownedFixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
    Write-Manifest $validPackage $true 'Diagnostic'
    Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions
  }
}

Assert-DeniedPayloadFixture `
  -relativePath 'assets/noclip/NoclipDataRepair.cs' `
  -content 'fixture' `
  -expectedErrors @('DENY_PATTERN_PATH:assets/noclip/NoclipDataRepair.cs:NoclipDataRepair','DENY_PATTERN_CONTENT:release-manifest.json:NoclipDataRepair') `
  -name 'NoClip repair source'
Assert-DeniedPayloadFixture `
  -relativePath 'assets/noclip/repair_noclip_data.py' `
  -content 'fixture' `
  -expectedErrors @('DENY_PATTERN_PATH:assets/noclip/repair_noclip_data.py:repair_noclip_data','DENY_PATTERN_CONTENT:release-manifest.json:repair_noclip_data') `
  -name 'NoClip repair script'
Assert-DeniedPayloadFixture `
  -relativePath 'assets/noclip/network-repair.exe' `
  -content 'https://cdn.jsdelivr.net/noclip-repair' `
  -expectedErrors @('DENY_PATTERN_CONTENT:assets/noclip/network-repair.exe:cdn.jsdelivr.net') `
  -name 'NoClip network CDN repair executable'
Assert-DeniedPayloadFixture `
  -relativePath 'ExternalLibs/NoclipViewer/dist/index.html' `
  -content 'fixture' `
  -expectedErrors @('DENY_PATH:ExternalLibs/NoclipViewer/dist/index.html') `
  -name 'duplicate generic NoClip dist'
Assert-DeniedPayloadFixture `
  -relativePath 'FFX.exe' `
  -content 'fixture' `
  -expectedErrors @('DENY_PATH:FFX.exe') `
  -name 'copyrighted game executable'
Assert (Test-Path -LiteralPath (Join-Path $validPackage 'FFXProjectEditor.exe') -PathType Leaf) 'game executable guard must preserve the product apphost fixture'
Assert-DeniedPayloadFixture `
  -relativePath 'assets/Saves/slot001.ffx' `
  -content 'fixture' `
  -expectedErrors @('DENY_PATH:assets/Saves/slot001.ffx') `
  -name 'user save payload'
Assert-DeniedPayloadFixture `
  -relativePath 'assets/Dumps/process.dmp' `
  -content 'fixture' `
  -expectedErrors @('DENY_PATH:assets/Dumps/process.dmp') `
  -name 'runtime dump payload'
Assert-DeniedPayloadFixture `
  -relativePath 'SpiraForge/paused/field-hub.bin' `
  -content 'fixture' `
  -expectedErrors @('DENY_PATH:SpiraForge/paused/field-hub.bin') `
  -name 'paused SpiraForge payload'

$runtimePolicyPath = Join-Path $isolatedRelease 'windows-runtime-prerequisites.json'
$runtimePolicyJson = Get-Content -LiteralPath $runtimePolicyPath -Raw | ConvertFrom-Json
($runtimePolicyJson.components.dotnetSelfContained.payloadFiles | Where-Object path -eq 'coreclr.dll').sha256 = '0' * 64
$runtimePolicyJson | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $runtimePolicyPath -Encoding UTF8
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 1 -and $r.Json.phase -eq 'runtime-prerequisite-pins') 'runtime SHA pin: fase/exit'
Assert-ExactSet $r.Json.errors @('RUNTIME_PIN_MISMATCH:dotnetSelfContained:coreclr.dll') 'runtime SHA pin errors'
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions

# Owner-risk sem tuple path/hash/bytes/arch/target é erro de contrato claro,
# mesmo quando nenhum payload correspondente está presente.
$unpinnedEntry = [ordered]@{
  id = 'unpinned-owner-risk'; classification = 'BUNDLED_OWNER_ACCEPTED_RISK'; executionModel = 'in-process'
  payloadFileNames = @('unpinned.dll'); payloadGlobs = @('tools/unpinned.dll')
}
Write-IsolatedContracts -packageRoot $validPackage -toolEntries @($unpinnedEntry) -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 2 -and $r.Json.phase -eq 'policy-contracts') 'accepted-risk sem pin: exit 2 policy'
Assert-ExactSet $r.Json.infrastructureErrors @('POLICY_ACCEPTED_RISK_APPROVED_PAYLOADS_MISSING:unpinned-owner-risk') 'accepted-risk sem pin errors'

# origin=tool fora de tools/ exige matcher explícito; matcher ausente não pode
# causar exceção nem conceder origem por inferência.
$originDir = Join-Path $validPackage 'assets'
New-Item -ItemType Directory -Force $originDir | Out-Null
$originPath = Join-Path $originDir 'tool-origin.dll'
Copy-Item -LiteralPath (Join-Path $validPackage 'hostfxr.dll') -Destination $originPath
Write-Manifest $validPackage $true 'Diagnostic'
$originManifest = Get-Content -LiteralPath (Join-Path $validPackage 'release-manifest.json') -Raw | ConvertFrom-Json
($originManifest.files | Where-Object path -eq 'assets/tool-origin.dll').origin = 'tool'
$originManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $validPackage 'release-manifest.json') -Encoding UTF8
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 1 -and $r.Json.phase -eq 'payload-inventory') 'origin tool sem matcher: fase/exit'
Assert-ExactSet $r.Json.errors @('ORIGIN_PATH_MISMATCH:assets/tool-origin.dll:tool') 'origin tool sem matcher errors'
Remove-Item -LiteralPath $originDir -Recurse -Force
Write-Manifest $validPackage $true 'Diagnostic'

# allowedRoots também é uma allowlist; presença no manifest não autoriza uma raiz nova.
$rogueDir = Join-Path $validPackage 'rogue'
New-Item -ItemType Directory -Force $rogueDir | Out-Null
Set-Content -LiteralPath (Join-Path $rogueDir 'payload.txt') -Value 'fixture' -Encoding UTF8
Write-Manifest $validPackage $true 'Diagnostic'
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions
$policyPath = Join-Path $isolatedRelease 'package-allowlist.json'
$policyJson = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
$policyJson.components.'studio-core-win-x64'.allowedRoots = @($policyJson.components.'studio-core-win-x64'.allowedRoots | Where-Object { $_ -cne 'rogue/' })
$policyJson | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $policyPath -Encoding UTF8
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert-ExactSet $r.Json.errors @('UNALLOWLISTED_ROOT:rogue/payload.txt') 'unlisted root errors'
Remove-Item -LiteralPath $rogueDir -Recurse -Force
Write-Manifest $validPackage $true 'Diagnostic'

# Allowlist top-level: estar no manifest não concede permissão.
$unlisted = Join-Path $validPackage 'unlisted.exe'
Copy-Item -LiteralPath (Join-Path $validPackage 'hostfxr.dll') -Destination $unlisted
Write-Manifest $validPackage $true 'Diagnostic'
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 1 -and $r.Json.phase -eq 'payload-inventory') 'unlisted top-level: exit 1 na fase inventory'
Assert-ExactSet $r.Json.errors @('UNALLOWLISTED_TOP_LEVEL:unlisted.exe') 'unlisted top-level errors'
Remove-Item -LiteralPath $unlisted -Force
Write-Manifest $validPackage $true 'Diagnostic'

# Hash mismatch deve ser isolado na fase inventory.
$manifestFile = Join-Path $validPackage 'release-manifest.json'
$manifestJson = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
$manifestJson.files[0].sha256 = '1' * 64
$manifestJson | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestFile -Encoding UTF8
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 1 -and $r.Json.phase -eq 'payload-inventory') 'hash mismatch: exit 1 na fase inventory'
Assert (@($r.Json.errors).Count -eq 1 -and $r.Json.errors[0] -like 'HASH_MISMATCH:*') 'hash mismatch: conjunto sem cascata'
Write-Manifest $validPackage $true 'Diagnostic'

# Owner accepted risk: matcher + tuple exata passa; mutação com manifest atualizado falha no pin.
$toolDir = Join-Path $validPackage 'tools'
New-Item -ItemType Directory -Force $toolDir | Out-Null
$approvedPath = Join-Path $toolDir 'approved.dll'
Copy-Item -LiteralPath (Join-Path $validPackage 'hostfxr.dll') -Destination $approvedPath
$approvedItem = Get-Item -LiteralPath $approvedPath
$approvedHash = (Get-FileHash -LiteralPath $approvedPath -Algorithm SHA256).Hash.ToLowerInvariant()
$approvedEntry = [ordered]@{
  id = 'approved-owner-risk'; classification = 'BUNDLED_OWNER_ACCEPTED_RISK'; executionModel = 'in-process'
  payloadFileNames = @('approved.dll'); payloadGlobs = @('tools/approved.dll')
  approvedPayloads = @([ordered]@{ path = 'tools/approved.dll'; bytes = $approvedItem.Length; sha256 = $approvedHash; kind = 'native'; arch = 'x64'; target = 'win-x64' })
}
Write-Manifest $validPackage $true 'Diagnostic'
$approvedExceptions = @($baselineAssemblyExceptions + @(Get-AssemblyExceptions $toolDir | ForEach-Object { $_.path = 'tools/' + $_.path; $_ }))
Write-IsolatedContracts -packageRoot $validPackage -toolEntries @($approvedEntry) -allowedTopLevel $baselineTopLevel -assemblyExceptions $approvedExceptions
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 0) ('accepted-risk exato deve passar: ' + $r.Stdout)
[IO.File]::AppendAllText($approvedPath, 'x', [Text.Encoding]::ASCII)
Write-Manifest $validPackage $true 'Diagnostic'
Write-IsolatedContracts -packageRoot $validPackage -toolEntries @($approvedEntry) -allowedTopLevel $baselineTopLevel -assemblyExceptions $approvedExceptions
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 1 -and $r.Json.phase -eq 'payload-inventory') 'accepted-risk mutado: exit 1 inventory'
Assert-ExactSet $r.Json.errors @('ACCEPTED_RISK_PIN_MISMATCH:approved-owner-risk:tools/approved.dll') 'accepted-risk mismatch errors'
Remove-Item -LiteralPath $approvedPath -Force
Remove-Item -LiteralPath $toolDir -Force
Write-Manifest $validPackage $true 'Diagnostic'

# approvedPayloads distingue dados, managed AnyCPU/I386 e native x64. Dados não
# passam por PE; managed exige metadata sem exigir AMD64; native exige AMD64.
$managedSource = Join-Path $tmp 'managed-anycpu-source'
$managedOut = Join-Path $tmp 'managed-anycpu-out'
New-Item -ItemType Directory -Force $managedSource | Out-Null
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><AssemblyName>ManagedAnyCpuFixture</AssemblyName><PlatformTarget>AnyCPU</PlatformTarget></PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $managedSource 'ManagedAnyCpu.csproj') -Encoding UTF8
'public sealed class ManagedAnyCpuFixture { }' | Set-Content -LiteralPath (Join-Path $managedSource 'Fixture.cs') -Encoding UTF8
& dotnet build (Join-Path $managedSource 'ManagedAnyCpu.csproj') -c Release --nologo -o $managedOut | Out-Null
Assert ($LASTEXITCODE -eq 0) 'fixture managed AnyCPU deve compilar'
$managedFixtureSource = Join-Path $managedOut 'ManagedAnyCpuFixture.dll'
Add-Type -AssemblyName System.Reflection.Metadata -ErrorAction Stop
$managedStream = [IO.File]::OpenRead($managedFixtureSource)
try {
  $managedReader = [Reflection.PortableExecutable.PEReader]::new($managedStream)
  Assert ($managedReader.HasMetadata -and $managedReader.PEHeaders.CoffHeader.Machine.ToString() -eq 'I386') 'fixture managed precisa provar metadata + I386/AnyCPU'
} finally { $managedStream.Dispose() }

$kindDir = Join-Path $validPackage 'tools\schema'
New-Item -ItemType Directory -Force $kindDir | Out-Null
$dataPath = Join-Path $kindDir 'README.md'
$managedPath = Join-Path $kindDir 'managed.dll'
$nativePath = Join-Path $kindDir 'native.dll'
$nativeX86Path = Join-Path $kindDir 'native-x86.dll'
Set-Content -LiteralPath $dataPath -Value 'Docs: https://one.docs.invalid https://two.docs.invalid' -NoNewline -Encoding UTF8
Copy-Item -LiteralPath $managedFixtureSource -Destination $managedPath
Copy-Item -LiteralPath (Join-Path $validPackage 'hostfxr.dll') -Destination $nativePath
Copy-Item -LiteralPath (Join-Path $repo 'ExternalLibs\MemorySharp64\x86\keystone.dll') -Destination $nativeX86Path
$kindPins = @(
  [ordered]@{ path='tools/schema/README.md'; bytes=(Get-Item $dataPath).Length; sha256=(Get-FileHash $dataPath -Algorithm SHA256).Hash.ToLowerInvariant(); kind='data'; arch='data'; target='win-x64' },
  [ordered]@{ path='tools/schema/managed.dll'; bytes=(Get-Item $managedPath).Length; sha256=(Get-FileHash $managedPath -Algorithm SHA256).Hash.ToLowerInvariant(); kind='managed'; arch='managed-any'; target='win-x64' },
  [ordered]@{ path='tools/schema/native.dll'; bytes=(Get-Item $nativePath).Length; sha256=(Get-FileHash $nativePath -Algorithm SHA256).Hash.ToLowerInvariant(); kind='native'; arch='x64'; target='win-x64' },
  [ordered]@{ path='tools/schema/native-x86.dll'; bytes=(Get-Item $nativeX86Path).Length; sha256=(Get-FileHash $nativeX86Path -Algorithm SHA256).Hash.ToLowerInvariant(); kind='native'; arch='x86'; target='win-x64' }
)
$kindEntry = [ordered]@{
  id='tool-kind-matrix'; classification='BUNDLED_OWNER_ACCEPTED_RISK'; executionModel='child-process'
  payloadFileNames=@('README.md','managed.dll','native.dll','native-x86.dll'); payloadGlobs=@('tools/schema/*'); approvedPayloads=@($kindPins)
}
Write-Manifest $validPackage $true 'Diagnostic'
$kindExceptions = @($baselineAssemblyExceptions + @(Get-AssemblyExceptions $kindDir | ForEach-Object { $_.path = 'tools/schema/' + $_.path; $_ }))
$toolDocException = [ordered]@{ scanner='text'; path='tools/schema/README.md'; sha256=(Get-FileHash $dataPath -Algorithm SHA256).Hash.ToLowerInvariant(); pattern='URL'; class='UNCLASSIFIED'; maxCount=2 }
Write-IsolatedContracts -packageRoot $validPackage -toolEntries @($kindEntry) -allowedTopLevel $baselineTopLevel -assemblyExceptions $kindExceptions -scanExceptions @($toolDocException)
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 0) ('kind data/managed/native válido deve passar: ' + $r.Stdout)

$x86Entry = $kindEntry | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$x86Entry.executionModel = 'in-process'
Write-IsolatedContracts -packageRoot $validPackage -toolEntries @($x86Entry) -allowedTopLevel $baselineTopLevel -assemblyExceptions $kindExceptions -scanExceptions @($toolDocException)
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 2 -and $r.Json.phase -eq 'policy-contracts') 'in-process+x86 deve falhar no schema'
Assert-ExactSet $r.Json.infrastructureErrors @('POLICY_APPROVED_PAYLOAD_X86_EXECUTION_MODEL_INVALID:tool-kind-matrix:tools/schema/native-x86.dll') 'in-process+x86 errors'
Remove-Item -LiteralPath $toolDir -Recurse -Force
Write-Manifest $validPackage $true 'Diagnostic'

# BLOCKED com matcher explícito e deny path/pattern precisam produzir conjuntos exatos.
New-Item -ItemType Directory -Force $toolDir | Out-Null
$blockedPath = Join-Path $toolDir 'vbfextract.exe'
Copy-Item -LiteralPath (Join-Path $validPackage 'hostfxr.dll') -Destination $blockedPath
$blockedEntry = [ordered]@{ id = 'vbfextract'; classification = 'BLOCKED'; payloadFileNames = @('vbfextract.exe'); payloadGlobs = @('tools/vbfextract.exe') }
Write-Manifest $validPackage $true 'Diagnostic'
Write-IsolatedContracts -packageRoot $validPackage -toolEntries @($blockedEntry) -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 1 -and $r.Json.phase -eq 'payload-inventory') 'blocked tool: exit 1 inventory'
Assert-ExactSet $r.Json.errors @('BLOCKED_COMPONENT_PRESENT:tools/vbfextract.exe:vbfextract') 'blocked tool errors'
Remove-Item -LiteralPath $blockedPath -Force
Remove-Item -LiteralPath $toolDir -Force

$broadBlockedEntry = [ordered]@{ id = 'broad-block'; classification = 'BLOCKED'; payloadGlobs = @('*') }
Write-Manifest $validPackage $true 'Diagnostic'
Write-IsolatedContracts -packageRoot $validPackage -toolEntries @($broadBlockedEntry) -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 2 -and $r.Json.phase -eq 'policy-contracts') 'broad matcher: exit 2 policy'
Assert-ExactSet $r.Json.infrastructureErrors @('POLICY_TOOL_GLOB_NOT_EXPLICIT:broad-block:*') 'broad matcher errors'

New-Item -ItemType Directory -Force $toolDir | Out-Null
$externalPath = Join-Path $toolDir 'external.exe'
Copy-Item -LiteralPath (Join-Path $validPackage 'hostfxr.dll') -Destination $externalPath
$externalEntry = [ordered]@{ id = 'external-launcher'; classification = 'LAUNCHER_COMPONENT'; payloadFileNames = @('external.exe'); payloadGlobs = @('tools/external.exe') }
Write-Manifest $validPackage $true 'Diagnostic'
Write-IsolatedContracts -packageRoot $validPackage -toolEntries @($externalEntry) -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert-ExactSet $r.Json.errors @('NON_BUNDLED_COMPONENT_PRESENT:tools/external.exe:external-launcher:LAUNCHER_COMPONENT') 'non-bundled tool errors'
Remove-Item -LiteralPath $externalPath -Force
Remove-Item -LiteralPath $toolDir -Force

$audioDir = Join-Path $validPackage 'assets\Audio'
New-Item -ItemType Directory -Force $audioDir | Out-Null
Set-Content -LiteralPath (Join-Path $audioDir 'track.txt') -Value 'fixture' -Encoding UTF8
Write-Manifest $validPackage $true 'Diagnostic'
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -denyPaths @('assets/Audio/') -assemblyExceptions $baselineAssemblyExceptions
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert-ExactSet $r.Json.errors @('DENY_PATH:assets/Audio/track.txt') 'deny path errors'
Remove-Item -LiteralPath (Join-Path $validPackage 'assets') -Recurse -Force

$assetsDir = Join-Path $validPackage 'assets'
New-Item -ItemType Directory -Force $assetsDir | Out-Null
Set-Content -LiteralPath (Join-Path $assetsDir 'symbols.pdb') -Value 'fixture' -Encoding UTF8
Write-Manifest $validPackage $true 'Diagnostic'
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert-ExactSet $r.Json.errors @('FORBIDDEN_EXT:assets/symbols.pdb') 'deny extension errors'
Remove-Item -LiteralPath $assetsDir -Recurse -Force

$assetsDir = Join-Path $validPackage 'assets'
New-Item -ItemType Directory -Force $assetsDir | Out-Null
Set-Content -LiteralPath (Join-Path $assetsDir 'config.txt') -Value 'ForbiddenToken' -Encoding UTF8
Write-Manifest $validPackage $true 'Diagnostic'
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -denyPatterns @('ForbiddenToken') -assemblyExceptions $baselineAssemblyExceptions
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert-ExactSet $r.Json.errors @('DENY_PATTERN_CONTENT:assets/config.txt:ForbiddenToken') 'deny pattern errors'
Remove-Item -LiteralPath $assetsDir -Recurse -Force

# Vendor é sempre escaneado; uma exceção só existe por path+SHA+pattern/class+maxCount.
$vendorDir = Join-Path $validPackage 'viewers\map\vendor\three'
New-Item -ItemType Directory -Force $vendorDir | Out-Null
$vendorPath = Join-Path $vendorDir 'NOTICE.js'
Set-Content -LiteralPath $vendorPath -Value '// upstream docs: https://example.invalid/docs' -NoNewline -Encoding UTF8
$legalDir = Join-Path $validPackage 'licenses'
New-Item -ItemType Directory -Force $legalDir | Out-Null
$legalPath = Join-Path $legalDir 'THIRD_PARTY_NOTICES.txt'
Set-Content -LiteralPath $legalPath -Value 'License: https://licenses.example.invalid/project' -NoNewline -Encoding UTF8
Write-Manifest $validPackage $true 'Diagnostic'
$vendorHash = (Get-FileHash -LiteralPath $vendorPath -Algorithm SHA256).Hash.ToLowerInvariant()
$vendorException = [ordered]@{ scanner='text'; path='viewers/map/vendor/three/NOTICE.js'; sha256=$vendorHash; pattern='URL'; class='UNCLASSIFIED'; maxCount=1 }
$legalException = [ordered]@{ scanner='text'; path='licenses/THIRD_PARTY_NOTICES.txt'; sha256=(Get-FileHash $legalPath -Algorithm SHA256).Hash.ToLowerInvariant(); pattern='URL'; class='UNCLASSIFIED'; maxCount=1 }
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions -scanExceptions @($vendorException,$legalException)
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 0) ('vendor + legal URL pinados devem passar: ' + $r.Stdout)

Set-Content -LiteralPath $vendorPath -Value '// https://one.invalid/a https://two.invalid/b' -NoNewline -Encoding UTF8
Write-Manifest $validPackage $true 'Diagnostic'
$vendorHash = (Get-FileHash -LiteralPath $vendorPath -Algorithm SHA256).Hash.ToLowerInvariant()
$vendorException = [ordered]@{ scanner='text'; path='viewers/map/vendor/three/NOTICE.js'; sha256=$vendorHash; pattern='URL'; class='UNCLASSIFIED'; maxCount=1 }
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions -scanExceptions @($vendorException,$legalException)
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert-ExactSet $r.Json.errors @('TEXT_ALLOWLIST_MAX_COUNT_EXCEEDED:viewers/map/vendor/three/NOTICE.js:UNCLASSIFIED:URL:2:1') 'vendor maxCount errors'

$staleVendorException = [ordered]@{ scanner='text'; path='viewers/map/vendor/three/NOTICE.js'; sha256=('0' * 64); pattern='URL'; class='UNCLASSIFIED'; maxCount=2 }
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions -scanExceptions @($staleVendorException,$legalException)
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Json.phase -eq 'portability-scans') 'vendor stale hash deve parar antes de invocar scanners'
Assert-ExactSet $r.Json.errors @('SCAN_ALLOWLIST_PIN_MISMATCH:viewers/map/vendor/three/NOTICE.js') 'vendor stale hash errors'

Remove-Item -LiteralPath (Join-Path $validPackage 'viewers') -Recurse -Force
Set-Content -LiteralPath $legalPath -Value 'Forbidden CDN: https://unpkg.com/evil.js' -NoNewline -Encoding UTF8
Write-Manifest $validPackage $true 'Diagnostic'
$forbiddenLegalException = [ordered]@{ scanner='text'; path='licenses/THIRD_PARTY_NOTICES.txt'; sha256=(Get-FileHash $legalPath -Algorithm SHA256).Hash.ToLowerInvariant(); pattern='URL'; class='FORBIDDEN_CDN'; maxCount=1 }
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions -scanExceptions @($forbiddenLegalException)
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 2 -and $r.Json.phase -eq 'policy-contracts') 'legal forbidden CDN não pode ser excepcionada'
Assert-ExactSet $r.Json.infrastructureErrors @('POLICY_TEXT_EXCEPTION_FORBIDDEN_CDN:licenses/THIRD_PARTY_NOTICES.txt') 'legal forbidden CDN errors'
Remove-Item -LiteralPath $legalDir -Recurse -Force

$firstPartyDir = Join-Path $validPackage 'viewers\map'
New-Item -ItemType Directory -Force $firstPartyDir | Out-Null
Set-Content -LiteralPath (Join-Path $firstPartyDir 'app-url.js') -Value 'const endpoint = "https://unknown.invalid/live";' -NoNewline -Encoding UTF8
Write-Manifest $validPackage $true 'Diagnostic'
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert-ExactSet $r.Json.errors @('TEXT_PORTABILITY:viewers/map/app-url.js:UNCLASSIFIED:URL') 'first-party URL continua fail-closed'
$firstPartyException = [ordered]@{ scanner='text'; path='viewers/map/app-url.js'; sha256=(Get-FileHash (Join-Path $firstPartyDir 'app-url.js') -Algorithm SHA256).Hash.ToLowerInvariant(); pattern='URL'; class='UNCLASSIFIED'; maxCount=1 }
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions -scanExceptions @($firstPartyException)
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 2 -and $r.Json.phase -eq 'policy-contracts') 'first-party exception deve falhar na policy'
Assert-ExactSet $r.Json.infrastructureErrors @('POLICY_TEXT_EXCEPTION_NON_VENDOR_OR_LEGAL:viewers/map/app-url.js') 'first-party exception errors'
Remove-Item -LiteralPath (Join-Path $validPackage 'viewers') -Recurse -Force
Write-Manifest $validPackage $true 'Diagnostic'

# NoClip schema v2: os 16 arquivos do bundle são exatos; mutação e extra falham.
$noclipRoot = Join-Path $validPackage 'viewers\noclip'
New-Item -ItemType Directory -Force $noclipRoot | Out-Null
foreach ($index in 1..16) { Set-Content -LiteralPath (Join-Path $noclipRoot ('bundle-{0:d2}.bin' -f $index)) -Value ('noclip-' + $index) -NoNewline -Encoding ASCII }
Write-Manifest $validPackage $true 'Diagnostic'
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions
$noclipFiles = @(Get-ChildItem -LiteralPath $noclipRoot -File | Sort-Object Name | ForEach-Object {
  [ordered]@{ path=$_.Name; bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$noclipV2 = [ordered]@{
  schemaVersion=2; state='BUNDLED_RUNTIME_VERIFIED_LOCAL_DATA_REQUIRED'
  studioBuild=[ordered]@{
    outputRoot='ExternalLibs/NoclipViewer/dist-ffxstudio'; forbiddenRuntimeOrigins=@('z.noclip.website','unpkg.com','cdn.jsdelivr.net')
    forbiddenRuntimeOriginMatches=0; files=@($noclipFiles)
  }
  dataCapability=[ordered]@{packageContainsGameData=$false;route='/data/';repair=$false;networkRepair=$false;junctionRepair=$false}
}
$noclipContractRaw = $noclipV2 | ConvertTo-Json -Depth 10
Set-Content -LiteralPath (Join-Path $isolatedRelease 'noclip-runtime.manifest.json') -Value $noclipContractRaw -Encoding UTF8
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 0) ('NoClip v2 pinado deve passar: ' + $r.Stdout)

$noclipV2.dataCapability.repair = $true
Set-Content -LiteralPath (Join-Path $isolatedRelease 'noclip-runtime.manifest.json') -Value ($noclipV2 | ConvertTo-Json -Depth 10) -Encoding UTF8
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 2 -and $r.Json.phase -eq 'policy-contracts') 'NoClip repair capability deve falhar na policy'
Assert-ExactSet $r.Json.infrastructureErrors @('POLICY_NOCLIP_DATA_CAPABILITY_INVALID') 'NoClip repair capability errors'
$noclipV2.dataCapability.repair = $false
Set-Content -LiteralPath (Join-Path $isolatedRelease 'noclip-runtime.manifest.json') -Value $noclipContractRaw -Encoding UTF8

Set-Content -LiteralPath (Join-Path $noclipRoot 'bundle-01.bin') -Value 'mutated' -NoNewline -Encoding ASCII
Write-Manifest $validPackage $true 'Diagnostic'
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions
Set-Content -LiteralPath (Join-Path $isolatedRelease 'noclip-runtime.manifest.json') -Value $noclipContractRaw -Encoding UTF8
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert-ExactSet $r.Json.errors @('NOCLIP_PIN_MISMATCH:viewers/noclip/bundle-01.bin') 'NoClip pin mismatch errors'
Set-Content -LiteralPath (Join-Path $noclipRoot 'bundle-01.bin') -Value 'noclip-1' -NoNewline -Encoding ASCII

Set-Content -LiteralPath (Join-Path $noclipRoot 'extra.bin') -Value 'extra' -NoNewline -Encoding ASCII
Write-Manifest $validPackage $true 'Diagnostic'
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions
Set-Content -LiteralPath (Join-Path $isolatedRelease 'noclip-runtime.manifest.json') -Value $noclipContractRaw -Encoding UTF8
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert-ExactSet $r.Json.errors @('NOCLIP_UNPINNED_FILE:viewers/noclip/extra.bin') 'NoClip unpinned extra errors'
Remove-Item -LiteralPath (Join-Path $validPackage 'viewers') -Recurse -Force
Write-Manifest $validPackage $true 'Diagnostic'

# Scanner assembly integrado: PE válido com overlay baked deve chegar apenas à fase scanners.
$bakedPath = Join-Path $validPackage 'baked.exe'
Copy-Item -LiteralPath (Join-Path $validPackage 'hostfxr.dll') -Destination $bakedPath
[IO.File]::AppendAllText($bakedPath, 'D:\FFX Extracted\payload', [Text.Encoding]::ASCII)
Write-Manifest $validPackage $true 'Diagnostic'
$topWithBaked = @($baselineTopLevel + 'baked.exe')
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $topWithBaked -assemblyExceptions $baselineAssemblyExceptions
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 1 -and $r.Json.phase -eq 'portability-scans') ('assembly integrated phase: ' + $r.Stdout)
Assert (@($r.Json.errors).Count -ge 2 -and @($r.Json.errors | Where-Object { $_ -notlike 'ASSEMBLY_PORTABILITY:baked.exe:*' }).Count -eq 0) 'assembly integrated: somente findings do baked.exe'
Remove-Item -LiteralPath $bakedPath -Force
Write-Manifest $validPackage $true 'Diagnostic'

# runtimeconfig/deps/host são evidência, não apenas nomes de arquivo.
$runtimeConfigPath = Join-Path $validPackage 'FFXProjectEditor.runtimeconfig.json'
$runtimeConfigRaw = [IO.File]::ReadAllText($runtimeConfigPath)
Set-Content -LiteralPath $runtimeConfigPath -Value '[]' -Encoding UTF8
Write-Manifest $validPackage $true 'Diagnostic'
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 1 -and $r.Json.phase -eq 'runtime-contract') 'runtimeconfig array: fase/exit'
Assert-ExactSet $r.Json.errors @('RUNTIMECONFIG_TOP_LEVEL_NOT_OBJECT') 'runtimeconfig array errors'
[IO.File]::WriteAllText($runtimeConfigPath, $runtimeConfigRaw, [Text.UTF8Encoding]::new($false))
Write-Manifest $validPackage $true 'Diagnostic'

$depsPath = Join-Path $validPackage 'FFXProjectEditor.deps.json'
$depsRaw = [IO.File]::ReadAllText($depsPath)
$depsJson = $depsRaw | ConvertFrom-Json
$depsTargetName = [string]$depsJson.runtimeTarget.name
$depsTargetGraph = $depsJson.targets.PSObject.Properties[$depsTargetName].Value
$runtimePackProperty = @($depsTargetGraph.PSObject.Properties | Where-Object { $_.Name -like 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/*' } | Select-Object -First 1)
if ($runtimePackProperty.Count -eq 1) {
  $depsTargetGraph.PSObject.Properties.Remove($runtimePackProperty[0].Name)
  $depsJson | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $depsPath -Encoding UTF8
  Write-Manifest $validPackage $true 'Diagnostic'
  Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions
  $r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
  Assert-ExactSet $r.Json.errors @('DEPS_RUNTIME_PACK_MISSING') 'deps runtime pack errors'
} else { Assert $false 'fixture self-contained deve declarar runtimepack win-x64 no deps' }
[IO.File]::WriteAllText($depsPath, $depsRaw, [Text.UTF8Encoding]::new($false))
Write-Manifest $validPackage $true 'Diagnostic'

$appHostPath = Join-Path $validPackage 'FFXProjectEditor.exe'
$appHostBytes = [IO.File]::ReadAllBytes($appHostPath)
$mutatedHost = [byte[]]$appHostBytes.Clone()
$needle = [Text.Encoding]::ASCII.GetBytes('FFXProjectEditor.dll')
$hostMarkers = 0
for ($i = 0; $i -le $mutatedHost.Length - $needle.Length; $i++) {
  $same = $true
  for ($j = 0; $j -lt $needle.Length; $j++) { if ($mutatedHost[$i + $j] -ne $needle[$j]) { $same = $false; break } }
  if ($same) { $mutatedHost[$i] = [byte][char]'X'; $hostMarkers++ }
}
Assert ($hostMarkers -gt 0) 'fixture apphost deve conter entrypoint FFXProjectEditor.dll'
[IO.File]::WriteAllBytes($appHostPath, $mutatedHost)
Write-Manifest $validPackage $true 'Diagnostic'
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert-ExactSet $r.Json.errors @('APPHOST_ENTRYPOINT_MISMATCH') 'apphost entrypoint errors'
[IO.File]::WriteAllBytes($appHostPath, $appHostBytes)
Write-Manifest $validPackage $true 'Diagnostic'

# O verifier valida o schema/exit do scanner filho, sem tratar JSON qualquer como clean.
$isolatedTextScan = Join-Path $isolatedScripts 'scan_portability.ps1'
@'
param([Parameter(Mandatory=$true)][string]$Root,[switch]$Json)
$rootFull=(Resolve-Path -LiteralPath $Root).Path
[ordered]@{schemaVersion=99;root=$rootFull;filesScanned=0;matchCount=0;unclassifiedCount=0;matches=[object[]]@()} | ConvertTo-Json -Compress
exit 0
'@ | Set-Content -LiteralPath $isolatedTextScan -Encoding UTF8
Write-IsolatedContracts -packageRoot $validPackage -allowedTopLevel $baselineTopLevel -assemblyExceptions $baselineAssemblyExceptions
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 2 -and $r.Json.phase -eq 'portability-scans') 'text scanner schema: fase/exit'
Assert-ExactSet $r.Json.infrastructureErrors @('TEXT_SCANNER_SCHEMA_INVALID') 'text scanner schema errors'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'scan_portability.ps1') -Destination $isolatedTextScan -Force

@'
param([Parameter(Mandatory=$true)][string]$Root,[switch]$Json)
$rootFull=(Resolve-Path -LiteralPath $Root).Path
[ordered]@{schemaVersion=99;status='clean';exitCode=0;root=$rootFull;filesScanned=0;matchCount=0;matches=[object[]]@();errors=[string[]]@()} | ConvertTo-Json -Compress
exit 0
'@ | Set-Content -LiteralPath $isolatedAssemblyScan -Encoding UTF8
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 2 -and $r.Json.phase -eq 'portability-scans') 'assembly scanner schema: fase/exit'
Assert-ExactSet $r.Json.infrastructureErrors @('ASSEMBLY_SCANNER_SCHEMA_INVALID') 'assembly scanner schema errors'
Copy-Item -LiteralPath $assemblyScan -Destination $isolatedAssemblyScan -Force

# Policy JSON inválida é infraestrutura, JSON stdout continua parseável e stderr vazio.
Set-Content -LiteralPath (Join-Path $isolatedRelease 'package-allowlist.json') -Value '{ invalid' -Encoding UTF8
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 2 -and $r.Json.phase -eq 'policy-contracts') 'policy JSON invalida: exit 2 policy-contracts'
Assert-ExactSet $r.Json.infrastructureErrors @('POLICY_PACKAGE_ALLOWLIST_JSON_INVALID') 'policy JSON errors'
Assert ([string]::IsNullOrEmpty($r.Stderr)) 'policy JSON invalida: stderr vazio em -Json'

Set-Content -LiteralPath (Join-Path $isolatedRelease 'package-allowlist.json') -Value '[]' -Encoding UTF8
$r = Run-Json $isolatedVerify $validPackage 'Diagnostic'
Assert ($r.Code -eq 2 -and $r.Json.phase -eq 'policy-contracts') 'policy array: exit 2 policy-contracts'
Assert-ExactSet $r.Json.infrastructureErrors @('POLICY_PACKAGE_ALLOWLIST_TOP_LEVEL_NOT_OBJECT') 'policy array errors'

# Fake self-contained nunca volta a ser positive fixture.
$fakePackage = Join-Path $tmp 'fake-runtime'
New-Package -dir $fakePackage -dirty $true -withManifest
foreach ($runtimeFile in @('coreclr.dll','hostfxr.dll','hostpolicy.dll')) {
  Set-Content -LiteralPath (Join-Path $fakePackage $runtimeFile) -Value 'x' -NoNewline -Encoding ASCII
}
Write-Manifest $fakePackage $true 'Diagnostic'
$fakeTop = @(Get-TopLevelFiles $fakePackage)
$fakeExceptions = @(Get-AssemblyExceptions $fakePackage)
Write-IsolatedContracts -packageRoot $fakePackage -allowedTopLevel $fakeTop -assemblyExceptions $fakeExceptions
$r = Run-Json $isolatedVerify $fakePackage 'Diagnostic'
Assert ($r.Code -eq 1 -and $r.Json.phase -eq 'runtime-contract') 'fake runtime: rejeitado na fase runtime'
Assert (@($r.Json.errors | Where-Object { $_ -like 'INVALID_PE:*' -or $_ -like 'RUNTIMECONFIG_*' -or $_ -like 'DEPS_*' }).Count -ge 1) 'fake runtime: erro forte de PE/runtime JSON'

Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue

if ($fails.Count -gt 0) { $fails | ForEach-Object { Write-Output ('FAIL: ' + $_) }; Write-Output ("VERIFY_HARNESS_FAIL n=$($fails.Count)"); exit 1 }
Write-Output "VERIFY_HARNESS_OK ($runs runs)"
exit 0
