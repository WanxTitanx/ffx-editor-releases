<#
  FFX Mod Studio — scan_assembly_portability.ps1.

  Scans PE payloads without treating them as text files. Findings are reported with a
  package-relative path, SHA-256, encoding lane and classification so verify_package can
  consume the result without scraping Format-Table output.

  Exit: 0 = clean · 1 = usage/IO/contract error · 2 = findings.
  With -Json, stdout contains exactly one JSON object and stderr stays empty.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$Root,
  [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$patterns = @(
  [pscustomobject]@{ name = 'user-profile';      regex = 'C:(?:\\+|/+)(?:Users)(?:\\+|/+)';                         class = 'PATH_CANDIDATE'; needles = @('C:') },
  # A drive marker must start at a real token boundary and have a plausible path tail.
  # This deliberately rejects the trailing "p:/" and "s:/" inside http:// / https://,
  # URI-like "urn:x:/..." text and drive-looking identifier suffixes.
  [pscustomobject]@{ name = 'drive-absolute';    regex = '(?<![A-Za-z0-9_:/.-])[A-Za-z]:(?:\\+|/+)(?=[\p{L}\p{N}_.$~-][\p{L}\p{N}_.$~(){}\[\]\\/-]{2,})'; class = 'PATH_CANDIDATE'; needles = @(':') },
  [pscustomobject]@{ name = 'unc-device';        regex = '(?:\\\\|%5c%5c)[?.](?:\\|/|%5c)';                      class = 'PATH_CANDIDATE'; needles = @('\\','%5c') },
  [pscustomobject]@{ name = 'unix-home';         regex = '(?:/Users/|/home/)';                                        class = 'PATH_CANDIDATE'; needles = @('/Users/','/home/') },
  [pscustomobject]@{ name = 'file-uri';          regex = 'file:///';                                                  class = 'PATH_CANDIDATE'; needles = @('file:///') },
  [pscustomobject]@{ name = 'ffx-extracted';     regex = 'FFX[ ]+Extracted';                                          class = 'DEV_REFERENCE'; needles = @('FFX') },
  [pscustomobject]@{ name = 'steamlibrary';      regex = 'SteamLibrary';                                              class = 'DEV_REFERENCE'; needles = @('SteamLibrary') },
  [pscustomobject]@{ name = 'repo-root';         regex = 'Documents(?:\\+|/+)ffx-editor-main';                      class = 'DEV_REFERENCE'; needles = @('Documents') },
  [pscustomobject]@{ name = 'repo-name';         regex = 'ffx-editor-main';                                           class = 'DEV_REFERENCE'; needles = @('ffx-editor-main') },
  [pscustomobject]@{ name = 'runtime-tools';     regex = 'RuntimeTools';                                              class = 'DEV_REFERENCE'; needles = @('RuntimeTools') },
  [pscustomobject]@{ name = 'work-path';         regex = '(?:\\+|/+)work(?:\\+|/+)';                              class = 'DEV_REFERENCE'; needles = @('\work\','/work/') },
  [pscustomobject]@{ name = 'git-data-root';     regex = 'Program[ ]+Files(?:\\+|/+)Git(?:\\+|/+)data';           class = 'DEV_REFERENCE'; needles = @('Program Files') },
  [pscustomobject]@{ name = 'derived-game-data'; regex = '(?:data(?:\\+|/+)FinalFantasyX|FFXModelAssets)';          class = 'DERIVED_GAME_DATA'; needles = @('FinalFantasyX','FFXModelAssets') },
  # z.noclip.website is deliberately NOT forbidden in packaged assemblies: NoclipDataBootstrap's
  # CDN fetch-through is a shipped product feature (the bundled viewer JS is still CDN-free —
  # noclip-runtime.manifest.json's forbiddenRuntimeOrigins gate stays). unpkg/jsdelivr remain denied.
  [pscustomobject]@{ name = 'forbidden-cdn';     regex = '(?:unpkg[.]com|cdn[.]jsdelivr[.]net)'; class = 'FORBIDDEN_CDN'; needles = @('unpkg.com','jsdelivr.net') },
  [pscustomobject]@{ name = 'remote-url';        regex = 'https?://[A-Za-z0-9][A-Za-z0-9._~:/?#@!$&''()*+,;=%-]*';    class = 'REMOTE_URL'; needles = @('http://','https://') }
)
$regexOptions = [Text.RegularExpressions.RegexOptions]::IgnoreCase -bor
  [Text.RegularExpressions.RegexOptions]::CultureInvariant
foreach ($pattern in $patterns) {
  $pattern | Add-Member -NotePropertyName matcher -NotePropertyValue ([regex]::new($pattern.regex, $regexOptions))
}

# Extract printable strings in one native loop. PowerShell MatchCollection iteration over a
# 500+ MiB self-contained payload is prohibitively slow and allocates one object per random
# printable run. Runs shorter than four characters cannot satisfy any detector in this script.
if (-not ('AssemblyPortabilityStringExtractor' -as [type])) {
  Add-Type -TypeDefinition @'
using System;
using System.Text;

public static class AssemblyPortabilityStringExtractor
{
    private static bool Printable(byte value) => value >= 0x20 && value <= 0x7e;

    public static string FromBytes(byte[] bytes)
    {
        var output = new StringBuilder(Math.Min(bytes.Length / 8, 4 * 1024 * 1024));
        int runStart = -1;
        for (int index = 0; index <= bytes.Length; index++)
        {
            bool printable = index < bytes.Length && Printable(bytes[index]);
            if (printable && runStart < 0) runStart = index;
            if (!printable && runStart >= 0)
            {
                int length = index - runStart;
                if (length >= 4)
                {
                    output.Append(Encoding.Latin1.GetString(bytes, runStart, length));
                    output.Append('\0');
                }
                runStart = -1;
            }
        }
        return output.ToString();
    }

    public static string FromUtf16Le(byte[] bytes, int offset)
    {
        var output = new StringBuilder(Math.Min(bytes.Length / 16, 4 * 1024 * 1024));
        int runStart = -1;
        int runLength = 0;
        for (int index = offset; index + 1 <= bytes.Length; index += 2)
        {
            bool printable = index + 1 < bytes.Length && bytes[index + 1] == 0 && Printable(bytes[index]);
            if (printable)
            {
                if (runStart < 0) runStart = index;
                runLength++;
            }
            else if (runStart >= 0)
            {
                if (runLength >= 4)
                {
                    for (int cursor = runStart; cursor < runStart + (runLength * 2); cursor += 2)
                        output.Append((char)bytes[cursor]);
                    output.Append('\0');
                }
                runStart = -1;
                runLength = 0;
            }
        }
        if (runStart >= 0 && runLength >= 4)
        {
            for (int cursor = runStart; cursor < runStart + (runLength * 2); cursor += 2)
                output.Append((char)bytes[cursor]);
            output.Append('\0');
        }
        return output.ToString();
    }
}
'@
}

function Get-RelativePath([string]$rootFull, [bool]$rootIsDirectory, [IO.FileInfo]$file) {
  if (-not $rootIsDirectory) { return $file.Name }
  return $file.FullName.Substring($rootFull.Length).TrimStart('\', '/').Replace('\', '/')
}

function Decode-Utf16Lane([byte[]]$bytes, [int]$offset) {
  # Only retain printable ASCII code units from the requested UTF-16LE parity.
  # Decoding the entire PE at both parities creates cross-parity gibberish in which
  # unrelated bytes can accidentally spell path/URL tokens. NUL separators keep
  # independent runs from joining while preserving real UTF-16LE paths and URLs.
  return [AssemblyPortabilityStringExtractor]::FromUtf16Le($bytes, $offset)
}

function Add-LaneFindings(
  [Collections.Generic.List[object]]$target,
  [string]$relativePath,
  [string]$sha256,
  [string]$encoding,
  [string]$text
) {
  foreach ($pattern in $patterns) {
    $couldMatch = $false
    foreach ($needle in $pattern.needles) {
      if ($text.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $couldMatch = $true; break }
    }
    if (-not $couldMatch) { continue }
    $hits = $pattern.matcher.Matches($text)
    if ($hits.Count -eq 0) { continue }

    if ($pattern.class -in @('REMOTE_URL', 'FORBIDDEN_CDN')) {
      $value = $hits[0].Value.Substring(0, [Math]::Min(256, $hits[0].Value.Length))
    } else {
      # Include the printable tail from the same extracted string run so a drive marker
      # such as "C:\" remains actionable. Never cross the NUL separator between runs.
      $sampleLength = [Math]::Min(256, $text.Length - $hits[0].Index)
      $value = $text.Substring($hits[0].Index, $sampleLength)
      $separator = $value.IndexOf([char]0)
      if ($separator -ge 0) { $value = $value.Substring(0, $separator) }
      $value = -join @($value.ToCharArray() | ForEach-Object { if ([int]$_ -ge 0x20 -and [int]$_ -le 0x7e) { $_ } else { '?' } })
      if ($value.Length -gt 256) { $value = $value.Substring(0, 256) }
    }
    $target.Add([pscustomobject][ordered]@{
      file = $relativePath
      sha256 = $sha256
      pattern = $pattern.name
      class = $pattern.class
      encoding = $encoding
      count = $hits.Count
      value = $value
    })
  }
}

$rootFull = $null
$files = @()
$findings = New-Object Collections.Generic.List[object]
$scanErrors = New-Object Collections.Generic.List[string]

try {
  $rootItem = Get-Item -LiteralPath $Root -ErrorAction Stop
  $rootFull = $rootItem.FullName
  if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    $scanErrors.Add('ROOT_REPARSE_POINT_FORBIDDEN')
  } elseif ($rootItem.PSIsContainer) {
    $files = @(Get-ChildItem -LiteralPath $rootFull -Recurse -File -ErrorAction Stop |
      Where-Object { $_.Extension -iin @('.dll', '.exe') } |
      Sort-Object FullName)
  } elseif ($rootItem.Extension -iin @('.dll', '.exe')) {
    $files = @($rootItem)
  } else {
    $scanErrors.Add(('ROOT_UNSUPPORTED_FILE:{0}' -f $rootItem.Extension.ToLowerInvariant()))
  }

  foreach ($file in $files) {
    $relative = Get-RelativePath $rootFull $rootItem.PSIsContainer $file
    try {
      if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        $scanErrors.Add(('REPARSE_POINT_FORBIDDEN:{0}' -f $relative))
        continue
      }
      $bytes = [IO.File]::ReadAllBytes($file.FullName)
      $sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()

      # Latin-1 is a byte-preserving lane for ASCII/UTF-8 path markers. UTF-16LE is
      # decoded twice because PE metadata/resources can begin on either byte parity.
      Add-LaneFindings $findings $relative $sha256 'ascii-utf8' ([AssemblyPortabilityStringExtractor]::FromBytes($bytes))
      Add-LaneFindings $findings $relative $sha256 'utf16le-even' (Decode-Utf16Lane $bytes 0)
      Add-LaneFindings $findings $relative $sha256 'utf16le-odd' (Decode-Utf16Lane $bytes 1)
    } catch {
      $scanErrors.Add(('READ_FAILED:{0}:{1}' -f $relative, $_.Exception.GetType().Name))
    }
  }
} catch {
  $scanErrors.Add(('ROOT_IO_ERROR:{0}' -f $_.Exception.GetType().Name))
}

$sortedFindings = @($findings | Sort-Object file, pattern, encoding, sha256, value)
$sortedErrors = @($scanErrors | Sort-Object -Unique)
$exitCode = if ($sortedErrors.Count -gt 0) { 1 } elseif ($sortedFindings.Count -gt 0) { 2 } else { 0 }
$status = if ($exitCode -eq 0) { 'clean' } elseif ($exitCode -eq 2) { 'findings' } else { 'error' }
$result = [ordered]@{
  schemaVersion = 1
  status = $status
  exitCode = $exitCode
  root = $rootFull
  filesScanned = @($files).Count
  matchCount = $sortedFindings.Count
  matches = [object[]]$sortedFindings
  errors = [string[]]$sortedErrors
}

if ($Json) {
  [Console]::Out.WriteLine(($result | ConvertTo-Json -Depth 6 -Compress))
} else {
  if ($sortedFindings.Count -gt 0) {
    [Console]::Out.WriteLine(($sortedFindings | Format-Table file, pattern, class, encoding, count -AutoSize | Out-String).TrimEnd())
  } elseif ($exitCode -eq 0) {
    [Console]::Out.WriteLine('CLEAN')
  }
  foreach ($scanError in $sortedErrors) { [Console]::Error.WriteLine($scanError) }
  [Console]::Out.WriteLine(('ASSEMBLY_MATCHES={0}' -f $sortedFindings.Count))
}

exit $exitCode
