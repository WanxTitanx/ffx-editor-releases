<#
  FFX Mod Studio — portability/path/media/secret scanner (gate §6.7/§14.1/§16.1). Read-only.

  Exit codes (documented):
    0 = clean (nenhum match; URLs de origins oficiais são allowlisted e descartadas)
    1 = usage/IO error
    2 = matches presentes (PATH_CANDIDATE / SECRET_CANDIDATE / MEDIA_CANDIDATE /
        FORBIDDEN_CDN / UNCLASSIFIED)

  -Json emite UM único objeto JSON no stdout (seguro para ConvertFrom-Json); o modo humano
  (padrão) emite tabela + resumo. -Redact substitui USERPROFILE e o root por ~/<root>.
  URLs remotas são classificadas explicitamente: ffxmodstudio.com/.br = ALLOWED_ORIGIN
  (descartado); unpkg/jsdelivr = FORBIDDEN_CDN; demais = UNCLASSIFIED. z.noclip.website is
  deliberately excluded from FORBIDDEN_CDN — the host-side CDN bootstrap/fetch-through in
  NoclipDataBootstrap is a shipped feature; the bundled viewer JS stays CDN-free via
  noclip-runtime.manifest.json's forbiddenRuntimeOrigins gate.
#>
param(
  [Parameter(Mandatory = $true)][string]$Root,
  [switch]$Json,
  [switch]$Redact
)
$ErrorActionPreference = 'Stop'

# Carrega origins oficiais de release/official-origins.json (não hardcode — plano §4.4.4/§22).
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$oo = Get-Content (Join-Path $repoRoot 'release\official-origins.json') -Raw | ConvertFrom-Json
$official = @(([uri]$oo.canonicalSiteOrigin).Host, ([uri]$oo.brazilSiteOrigin).Host)

$patterns = @(
  @{ name = 'user-profile';   regex = 'C:[\\/]Users[\\/]';                                          cls = 'PATH_CANDIDATE' },
  @{ name = 'wande-user';     regex = 'Users[\\/]wande';                                            cls = 'PATH_CANDIDATE' },
  @{ name = 'drive-absolute'; regex = '(?i)\b[D-Z]:[\\/]';                                          cls = 'PATH_CANDIDATE' },
  @{ name = 'unc-device';     regex = '\\\\[?\\.]\\';                                               cls = 'PATH_CANDIDATE' },
  @{ name = 'unix-home';      regex = '/Users/|/home/';                                             cls = 'PATH_CANDIDATE' },
  @{ name = 'ffx-extracted';  regex = 'FFX Extracted';                                              cls = 'PATH_CANDIDATE' },
  @{ name = 'steamlibrary';   regex = 'SteamLibrary';                                               cls = 'PATH_CANDIDATE' },
  @{ name = 'repo-work';      regex = '(ffx-editor-main|\\work\\|/work/)';                          cls = 'PATH_CANDIDATE' },
  @{ name = 'file-uri';       regex = 'file:///';                                                   cls = 'PATH_CANDIDATE' },
  @{ name = 'env-file';       regex = '[\\/](\\.env)(\\W|$)';                                       cls = 'SECRET_CANDIDATE' },
  @{ name = 'secret-marker';  regex = '(?i)(api[_-]?key|private[_-]?key|bearer[_-]?token|client[_-]?secret|secret[_-]?key)'; cls = 'SECRET_CANDIDATE' },
  @{ name = 'game-media';     regex = '(?i)\\.(wav|fsb|fev|iso|vbf|pyre|dds|tm2|gltf|glb)(\\W|$)'; cls = 'MEDIA_CANDIDATE' },
  @{ name = 'pdb-file';       regex = '(?i)\\.pdb(\\W|$)';                                          cls = 'MEDIA_CANDIDATE' },
  @{ name = 'forbidden-cdn';  regex = '(?i)(unpkg\\.com|cdn\\.jsdelivr\\.net)'; cls = 'FORBIDDEN_CDN' }
)
$urlRegex = '(?i)https?://[^\s"''<>)]+'

$textExts = @('.cs', '.csproj', '.vbproj', '.fsproj', '.axaml', '.xaml', '.json', '.jsonc', '.csv', '.xml',
  '.props', '.targets', '.pubxml', '.sln', '.config', '.manifest', '.js', '.mjs', '.ts', '.tsx',
  '.html', '.css', '.ps1', '.psm1', '.py', '.md', '.txt', '.rst', '.yml', '.yaml', '.bat',
  '.cmd', '.sh', '.resx', '.gitignore', '.editorconfig')

$rootItem = Get-Item -LiteralPath $Root
$rootFull = $rootItem.FullName

function Classify-Url([string]$u) {
  try { $uri = [uri]$u } catch { return 'UNCLASSIFIED' }
  # loopback é permitido APENAS por regra separada e explícita (viewers internos).
  if ($uri.IsLoopback) { return 'ALLOWED_LOCAL_LOOPBACK' }
  # domínios públicos: só https, host exato, porta padrão, sem userinfo.
  if ($uri.Scheme -ne 'https') { return 'UNCLASSIFIED' }
  if (-not [string]::IsNullOrEmpty($uri.UserInfo)) { return 'UNCLASSIFIED' }
  if (-not $uri.IsDefaultPort) { return 'UNCLASSIFIED' }
  if ($uri.Host -eq $official[0]) { return 'ALLOWED_FEED_ORIGIN' }        # trust root do feed
  if ($uri.Host -eq $official[1]) { return 'ALLOWED_BR_ENTRY' }           # entrada BR (não é trust root)
  if ($uri.Host -in @('unpkg.com', 'cdn.jsdelivr.net')) { return 'FORBIDDEN_CDN' }
  return 'UNCLASSIFIED'
}

function Scan-File([string]$full) {
  if ((Get-Item -LiteralPath $full).PSIsContainer) { return @() }
  try { $t = Get-Content -LiteralPath $full -Raw -ErrorAction Stop } catch { return @() }
  $rel = if ($full.StartsWith($rootFull)) { $full.Substring($rootFull.Length).TrimStart('\', '/') } else { $full }
  $out = @()
  foreach ($m in [regex]::Matches($t, $urlRegex)) {
    $u = $m.Value
    $c = Classify-Url $u
    if ($c -notlike 'ALLOWED_*') {
      $out += [pscustomobject]@{ file = $rel; pattern = 'URL'; class = $c; count = 1; sample = $u.Substring(0, [Math]::Min(80, $u.Length)) }
    }
  }
  foreach ($p in $patterns) {
    if ($p.name -eq 'forbidden-cdn') { continue }  # já coberto pela rota de URLs + abaixo
    $ms = [regex]::Matches($t, $p.regex)
    if ($ms.Count -gt 0) {
      $s = $ms[0].Value
      if ($Redact) { $s = $s -replace [regex]::Escape($env:USERPROFILE), '~'; $s = $s -replace [regex]::Escape($rootFull), '<root>' }
      $out += [pscustomobject]@{ file = $rel; pattern = $p.name; class = $p.cls; count = $ms.Count; sample = $s }
    }
  }
  return $out
}

$files = if ($rootItem.PSIsContainer) {
  Get-ChildItem -LiteralPath $Root -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $textExts -contains $_.Extension.ToLower() -and $_.FullName -notmatch '\\(obj|bin|\.git|node_modules|third_party)\\' }
} else { @($rootItem) }

$matches = @(foreach ($f in $files) { Scan-File $f.FullName })
$unclassified = @($matches | Where-Object { $_.class -eq 'UNCLASSIFIED' }).Count

$result = [ordered]@{
  schemaVersion = 1
  root = $rootFull
  filesScanned = $files.Count
  matchCount = $matches.Count
  unclassifiedCount = $unclassified
  matches = $matches
}

if ($Json) {
  $result | ConvertTo-Json -Depth 4
} else {
  if ($matches.Count -eq 0) { Write-Output 'CLEAN' }
  else { $matches | Sort-Object file, pattern | Format-Table file, pattern, class, count, sample -AutoSize }
  Write-Output ('filesScanned={0} matchCount={1} unclassified={2}' -f $files.Count, $matches.Count, $unclassified)
}

if ($matches.Count -gt 0) { exit 2 } else { exit 0 }