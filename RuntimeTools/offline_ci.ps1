<#
  offline_ci.ps1 — FFX editor OFFLINE regression gate.
  Builds + runs the byte-identity harnesses that need NO running game / probe:
    - ReverseHarness    (wd / rsd / ply / ma2 / omd / spheregrid / shop / w_name no-edit RT0)
    - AiScriptLab       (AI bytecode: RT0 + bounded-walk + known-opcode + edit byte-local + write-back + workers)
    - FormationSlotLab  (SPIRA FORGE v0.2: formation writer RT0 + slot-only diff + re-read on the btl corpus)
    - AbilityCommandLab (SPIRA FORGE: ability writer RT0 — certifies command/item byte-safe; flags MonMagic drift)
    - BiancaCatalogLab  (AURORA layer 1: btlmap catalog complete/deterministic + map->scene bridge 0-unexplained)
    - AuroraChamberLab  (AURORA layer 3: BIANCA+anchors+encounter GOLDEN + corpus + pipeline)
    - BattleArenaPositionLab (AURORA: position writer RT0 + position-only diff + re-read on the btl corpus)
    - BattleArenaGrowLab (AURORA: structural grow writer RT0 no-edit + add/remove round-trip + clean re-decode)
    - BattleCameraScanLab (AURORA: chunk0 ATEL RT0 byte-exact + camReq scan + shot/target byte-local edit round-trip)
    - FFXProjectEditor internal gates (--wave1-rt0 [12 families] + monster/encounter/encounter-rebuild/event/
      eventscript/keyitem/treasure/customization/autoability/ctbbase/mixtable/aiasm — no-edit byte-identity)
  Exit code 0 only if ALL gates PASS. Run before promoting any writer.

  Usage:  pwsh RuntimeTools\offline_ci.ps1
          pwsh RuntimeTools\offline_ci.ps1 -FfxRoot "D:\FFX Extracted\FFX\ffx_ps2\ffx" -MonRoot "...\jppc\battle\mon"
#>
[CmdletBinding()]
param(
  [string]$FfxRoot  = "D:\FFX Extracted\FFX\ffx_ps2\ffx",
  [string]$WaveDir  = "D:\FFX Extracted\FFX\ffx_ps2\ffx\proj\sound\wave",
  [string]$MonRoot  = "D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\mon",
  [string]$BtlRoot  = "D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\btl",
  [string]$MasterRoot = "D:\FFX Extracted\FFX\ffx_ps2\ffx\master",
  [string]$BtlmapRoot = "D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\btlmap"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot   # repo root (RuntimeTools is one level down)
$repo = Join-Path $root ""
Set-Location $root
$fail = 0
$lines = @()

function Section($name) { Write-Host "`n========== $name ==========" -ForegroundColor Cyan }
function Invoke-IsolatedBuild([string]$ProjectPath, [string]$OutputDir, [string]$Defines = '') {
  $defArg = @()
  if ($Defines) { $defArg = @("-p:FFXIncludeDevTools=true") }
  dotnet build $ProjectPath -c Release -nr:false -p:UseSharedCompilation=false -o $OutputDir -v q @defArg | Out-Null
  return $LASTEXITCODE
}

# ---- build ----
Section "build"
dotnet build RuntimeTools\ReverseHarness\ReverseHarness.csproj -c Release -v q | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "ReverseHarness build FAILED" -ForegroundColor Red; exit 2 }
dotnet build RuntimeTools\AiScriptLab\AiScriptLab.csproj -c Release -v q | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "AiScriptLab build FAILED" -ForegroundColor Red; exit 2 }
dotnet build RuntimeTools\FormationSlotLab\FormationSlotLab.csproj -c Release -v q | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "FormationSlotLab build FAILED" -ForegroundColor Red; exit 2 }
Invoke-IsolatedBuild "RuntimeTools\AbilityCommandLab\AbilityCommandLab.csproj" "work\_offline_ci_build\AbilityCommandLab" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "AbilityCommandLab build FAILED" -ForegroundColor Red; exit 2 }
dotnet build RuntimeTools\BiancaCatalogLab\BiancaCatalogLab.csproj -c Release -v q | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "BiancaCatalogLab build FAILED" -ForegroundColor Red; exit 2 }
dotnet build RuntimeTools\AuroraChamberLab\AuroraChamberLab.csproj -c Release -v q | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "AuroraChamberLab build FAILED" -ForegroundColor Red; exit 2 }
dotnet build RuntimeTools\BattleArenaPositionLab\BattleArenaPositionLab.csproj -c Release -v q | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "BattleArenaPositionLab build FAILED" -ForegroundColor Red; exit 2 }
dotnet build RuntimeTools\BattleArenaGrowLab\BattleArenaGrowLab.csproj -c Release -v q | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "BattleArenaGrowLab build FAILED" -ForegroundColor Red; exit 2 }
dotnet build RuntimeTools\BattleCameraScanLab\BattleCameraScanLab.csproj -c Release -v q | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "BattleCameraScanLab build FAILED" -ForegroundColor Red; exit 2 }
dotnet build RuntimeTools\BattleEncounterOpenerLab\BattleEncounterOpenerLab.csproj -c Release -v q | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "BattleEncounterOpenerLab build FAILED" -ForegroundColor Red; exit 2 }
dotnet build RuntimeTools\BattleCompanionActivationLab\BattleCompanionActivationLab.csproj -c Release -v q | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "BattleCompanionActivationLab build FAILED" -ForegroundColor Red; exit 2 }
Write-Host "build OK"

# ---- ReverseHarness ----
Section "ReverseHarness (PS2 formats no-edit RT0)"
dotnet run --project RuntimeTools\ReverseHarness\ReverseHarness.csproj -c Release --no-build -- "$WaveDir" "$FfxRoot"
$rh = $LASTEXITCODE
if ($rh -ne 0) { $fail++ }
$lines += "ReverseHarness : " + $(if ($rh -eq 0) { "PASS" } else { "FAIL ($rh)" })

# ---- AiScriptLab ----
Section "AiScriptLab (AI bytecode RT0 + edit + write-back)"
dotnet run --project RuntimeTools\AiScriptLab\AiScriptLab.csproj -c Release --no-build -- "$MonRoot" --json "RuntimeTools\AiScriptLab\ai_rt0_verdict.json"
$ai = $LASTEXITCODE
if ($ai -ne 0) { $fail++ }
$lines += "AiScriptLab    : " + $(if ($ai -eq 0) { "PASS" } else { "FAIL ($ai)" })

# ---- AiScriptLab complex-family matrix / dedicated family lanes ----
Section "AiScriptLab (complex-family matrix + m125 + m142)"
dotnet run --project RuntimeTools\AiScriptLab\AiScriptLab.csproj -c Release --no-build -- "$MonRoot" --complex-family-matrix-rt0
$cfm = $LASTEXITCODE
if ($cfm -ne 0) { $fail++ }
$lines += "AiScriptLab --complex-family-matrix-rt0 : " + $(if ($cfm -eq 0) { "PASS" } else { "FAIL ($cfm)" })

dotnet run --project RuntimeTools\AiScriptLab\AiScriptLab.csproj -c Release --no-build -- "$MonRoot" --anima-family-rt0
$anm = $LASTEXITCODE
if ($anm -ne 0) { $fail++ }
$lines += "AiScriptLab --anima-family-rt0          : " + $(if ($anm -eq 0) { "PASS" } else { "FAIL ($anm)" })

dotnet run --project RuntimeTools\AiScriptLab\AiScriptLab.csproj -c Release --no-build -- "$MonRoot" --host-companion-family-rt0
$hcf = $LASTEXITCODE
if ($hcf -ne 0) { $fail++ }
$lines += "AiScriptLab --host-companion-family-rt0 : " + $(if ($hcf -eq 0) { "PASS" } else { "FAIL ($hcf)" })

dotnet run --project RuntimeTools\AiScriptLab\AiScriptLab.csproj -c Release --no-build -- "$MonRoot" --elemental-cluster-rt0
$ecl = $LASTEXITCODE
if ($ecl -ne 0) { $fail++ }
$lines += "AiScriptLab --elemental-cluster-rt0     : " + $(if ($ecl -eq 0) { "PASS" } else { "FAIL ($ecl)" })

dotnet run --project RuntimeTools\AiScriptLab\AiScriptLab.csproj -c Release --no-build -- "$MonRoot" --support-accumulator-rt0
$sac = $LASTEXITCODE
if ($sac -ne 0) { $fail++ }
$lines += "AiScriptLab --support-accumulator-rt0   : " + $(if ($sac -eq 0) { "PASS" } else { "FAIL ($sac)" })

dotnet run --project RuntimeTools\AiScriptLab\AiScriptLab.csproj -c Release --no-build -- "$MonRoot" --reactive-sensor-family-rt0
$rsf = $LASTEXITCODE
if ($rsf -ne 0) { $fail++ }
$lines += "AiScriptLab --reactive-sensor-family-rt0: " + $(if ($rsf -eq 0) { "PASS" } else { "FAIL ($rsf)" })

dotnet run --project RuntimeTools\AiScriptLab\AiScriptLab.csproj -c Release --no-build -- "$MonRoot" --sub-actor-family-rt0
$saf = $LASTEXITCODE
if ($saf -ne 0) { $fail++ }
$lines += "AiScriptLab --sub-actor-family-rt0      : " + $(if ($saf -eq 0) { "PASS" } else { "FAIL ($saf)" })

dotnet run --project RuntimeTools\AiScriptLab\AiScriptLab.csproj -c Release --no-build -- "$MonRoot" --round-scripted-boss-rt0
$rsb = $LASTEXITCODE
if ($rsb -ne 0) { $fail++ }
$lines += "AiScriptLab --round-scripted-boss-rt0   : " + $(if ($rsb -eq 0) { "PASS" } else { "FAIL ($rsb)" })

dotnet run --project RuntimeTools\AiScriptLab\AiScriptLab.csproj -c Release --no-build -- "$MonRoot" --tonberry-camera-routing-rt0
$tcr = $LASTEXITCODE
if ($tcr -ne 0) { $fail++ }
$lines += "AiScriptLab --tonberry-camera-routing-rt0: " + $(if ($tcr -eq 0) { "PASS" } else { "FAIL ($tcr)" })

dotnet run --project RuntimeTools\AiScriptLab\AiScriptLab.csproj -c Release --no-build -- "$MonRoot" --advanced-family-surface-popups-rt0
$afp = $LASTEXITCODE
if ($afp -ne 0) { $fail++ }
$lines += "AiScriptLab --advanced-family-surface-popups-rt0: " + $(if ($afp -eq 0) { "PASS" } else { "FAIL ($afp)" })

dotnet run --project RuntimeTools\AiScriptLab\AiScriptLab.csproj -c Release --no-build -- "$MonRoot" --advanced-route-bundle-rt0
$arb = $LASTEXITCODE
if ($arb -ne 0) { $fail++ }
$lines += "AiScriptLab --advanced-route-bundle-rt0 : " + $(if ($arb -eq 0) { "PASS" } else { "FAIL ($arb)" })
dotnet run --project RuntimeTools\AiScriptLab\AiScriptLab.csproj -c Release --no-build -- "$MonRoot" --phase-rotation-rt0
$pr = $LASTEXITCODE
if ($pr -ne 0) { $fail++ }
$lines += "AiScriptLab --phase-rotation-rt0   : " + $(if ($pr -eq 0) { "PASS" } else { "FAIL ($pr)" })


# ---- AtelLangLab (ATEL high-level language: compile dry-run on the m001 fixture) ----
Section "AtelLangLab (ATEL lang compile dry-run)"
dotnet run --project RuntimeTools\AtelLangLab\AtelLangLab.csproj -c Release -- --src RuntimeTools\AtelLangLab\sample.atel --monster FFXProjectEditor.Tests\Fixtures\Monster\m001.bin --dry-run | Out-Null
$all = $LASTEXITCODE
if ($all -ne 0) { $fail++ }
$lines += "AtelLangLab          : " + $(if ($all -eq 0) { "PASS" } else { "FAIL ($all)" })



# ---- FormationSlotLab (SPIRA FORGE v0.2) ----
Section "FormationSlotLab (formation writer RT0 + slot-only + re-read)"
dotnet run --project RuntimeTools\FormationSlotLab\FormationSlotLab.csproj -c Release --no-build -- "$BtlRoot" --json "RuntimeTools\FormationSlotLab\formation_slot_verdict.json"
$fs = $LASTEXITCODE
if ($fs -ne 0) { $fail++ }
$lines += "FormationSlotLab: " + $(if ($fs -eq 0) { "PASS" } else { "FAIL ($fs)" })

# ---- AbilityCommandLab (SPIRA FORGE) ----
Section "AbilityCommandLab (ability writer RT0: certifies command/item; flags MonMagic drift)"
$AbilityCommandLabDll = "work\_offline_ci_build\AbilityCommandLab\AbilityCommandLab.dll"
dotnet $AbilityCommandLabDll "$MasterRoot" --json "RuntimeTools\AbilityCommandLab\ability_writer_verdict.json"
$ac = $LASTEXITCODE
if ($ac -ne 0) { $fail++ }
$lines += "AbilityCommandLab: " + $(if ($ac -eq 0) { "PASS" } else { "FAIL ($ac)" })

# ---- BiancaCatalogLab (AURORA layer 1) ----
Section "BiancaCatalogLab (btlmap catalog complete/deterministic + map->scene bridge 0-unexplained)"
dotnet run --project RuntimeTools\BiancaCatalogLab\BiancaCatalogLab.csproj -c Release --no-build -- "$BtlmapRoot" --json "RuntimeTools\BiancaCatalogLab\bianca_catalog_verdict.json"
$bc = $LASTEXITCODE
if ($bc -ne 0) { $fail++ }
$lines += "BiancaCatalogLab : " + $(if ($bc -eq 0) { "PASS" } else { "FAIL ($bc)" })

# ---- AuroraChamberLab (AURORA layer 3) ----
Section "AuroraChamberLab (BIANCA + anchors + encounter GOLDEN + corpus + pipeline)"
dotnet run --project RuntimeTools\AuroraChamberLab\AuroraChamberLab.csproj -c Release --no-build -- "$BtlmapRoot" --btl "$BtlRoot" --json "RuntimeTools\AuroraChamberLab\aurora_chamber_verdict.json"
$au = $LASTEXITCODE
if ($au -ne 0) { $fail++ }
$lines += "AuroraChamberLab : " + $(if ($au -eq 0) { "PASS" } else { "FAIL ($au)" })

# ---- BattleArenaPositionLab (AURORA position writer) ----
Section "BattleArenaPositionLab (position writer RT0 + position-only diff + re-read)"
dotnet run --project RuntimeTools\BattleArenaPositionLab\BattleArenaPositionLab.csproj -c Release --no-build -- "$BtlRoot" --json "RuntimeTools\BattleArenaPositionLab\arena_position_verdict.json"
$ap = $LASTEXITCODE
if ($ap -ne 0) { $fail++ }
$lines += "BattleArenaPositionLab: " + $(if ($ap -eq 0) { "PASS" } else { "FAIL ($ap)" })

# ---- BattleArenaGrowLab (AURORA structural grow: add/remove monster) ----
Section "BattleArenaGrowLab (structural grow RT0 no-edit + add/remove round-trip + clean re-decode)"
dotnet run --project RuntimeTools\BattleArenaGrowLab\BattleArenaGrowLab.csproj -c Release --no-build -- "$BtlRoot" --json "RuntimeTools\BattleArenaGrowLab\arena_grow_verdict.json"
$ag = $LASTEXITCODE
if ($ag -ne 0) { $fail++ }
$lines += "BattleArenaGrowLab: " + $(if ($ag -eq 0) { "PASS" } else { "FAIL ($ag)" })

# ---- BattleCameraScanLab (AURORA battle camera: chunk0 ATEL RT0 + camReq read + shot/target byte-local edit) ----
Section "BattleCameraScanLab (chunk0 ATEL RT0 + camReq scan + shot/target edit round-trip)"
dotnet run --project RuntimeTools\BattleCameraScanLab\BattleCameraScanLab.csproj -c Release --no-build -- "$BtlRoot" --json "RuntimeTools\BattleCameraScanLab\camera_scan_verdict.json"
$cs = $LASTEXITCODE
if ($cs -ne 0) { $fail++ }
$lines += "BattleCameraScanLab: " + $(if ($cs -eq 0) { "PASS" } else { "FAIL ($cs)" })

# ---- BattleEncounterOpenerLab (encounter opener / CTB seed: golden Seymour + corpus clean) ----
Section "BattleEncounterOpenerLab (encounter opener / CTB seed - golden Seymour + corpus scan)"
dotnet run --project RuntimeTools\BattleEncounterOpenerLab\BattleEncounterOpenerLab.csproj -c Release --no-build -- "$BtlRoot" --json "RuntimeTools\BattleEncounterOpenerLab\opener_verdict.json"
$eo = $LASTEXITCODE
if ($eo -ne 0) { $fail++ }
$lines += "BattleEncounterOpenerLab: " + $(if ($eo -eq 0) { "PASS" } else { "FAIL ($eo)" })

# ---- BattleCompanionActivationLab (m213 hidden companion activation / summon-handoff) ----
Section "BattleCompanionActivationLab (m213 hidden companion activation / summon-handoff)"
dotnet run --project RuntimeTools\BattleCompanionActivationLab\BattleCompanionActivationLab.csproj -c Release --no-build -- "$BtlRoot" --mon-root "$MonRoot" --json "RuntimeTools\BattleCompanionActivationLab\companion_activation_verdict.json"
$ca = $LASTEXITCODE
if ($ca -ne 0) { $fail++ }
$lines += "BattleCompanionActivationLab: " + $(if ($ca -eq 0) { "PASS" } else { "FAIL ($ca)" })

# ---- FFXProjectEditor internal RT0 gates (writer-completeness: 12 Wave-1 families + monster/encounter/event/AI/kernel) ----
Section "FFXProjectEditor internal gates (--wave1-rt0 + 12 more, no-edit byte-identity)"
Invoke-IsolatedBuild "FFXProjectEditor\FFXProjectEditor.csproj" "work\_offline_ci_build\FFXProjectEditor" -Defines "FFX_INCLUDE_DEVTOOLS" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "FFXProjectEditor build FAILED" -ForegroundColor Red; exit 2 }
$EditorDll = "work\_offline_ci_build\FFXProjectEditor\FFXProjectEditor.dll"
# The headless RT0 gates live under #if FFX_INCLUDE_DEVTOOLS (built above). Gates that need a corpus
# root accept it as args[1]; pass the lab's -FfxRoot-derived paths so the gates exercise the same
# corpus as the labs instead of owner-environment defaults.
$MonRoot = Join-Path $FfxRoot "jppc\battle\mon"
$KernelRoot = Join-Path $FfxRoot "jppc\battle\kernel"
$NewUspcRoot = Join-Path $FfxRoot "new_uspc\battle\kernel"
# Gate -> corpus path map (each headless gate takes its own path as args[1]; see FFXProjectEditor/Program.cs).
$K = Join-Path $FfxRoot "jppc\battle\kernel"
$gatePaths = @{
  "wave1"               = @()
  "monster"             = @((Join-Path $FfxRoot "jppc\battle\mon"))
  "monster-capture-bit" = @((Join-Path $FfxRoot "jppc\battle\mon"))
  "encounter"           = @((Join-Path $K "btl.bin"))
  "encounter-rebuild"   = @((Join-Path $K "btl.bin"))
  "event"               = @((Join-Path $FfxRoot "jppc\event\obj"))
  "eventscript"         = @((Join-Path $FfxRoot "jppc\event\obj"))
  "keyitem"             = @((Join-Path $K "important.bin"))
  "treasure"            = @((Join-Path $K "takara.bin"))
  "customization"       = @((Join-Path $K "kaizou.bin"), (Join-Path $K "sum_grow.bin"))
  "autoability"         = @((Join-Path $K "a_ability.bin"), (Join-Path $K "arms_rate.bin"))
  "ctbbase"             = @((Join-Path $K "ctb_base.bin"))
  "mixtable"            = @((Join-Path $K "prepare.bin"))
  "aiasm"               = @((Join-Path $FfxRoot "jppc\battle\mon"))
  "spheregrid-layout"   = @((Join-Path $FfxRoot "jppc\menu\abmap"))
  "spheregrid-build"    = @((Join-Path $FfxRoot "jppc\menu\abmap"))
  "spheregrid-edit"     = @((Join-Path $FfxRoot "jppc\menu\abmap"))
  "jptext"              = @((Join-Path $FfxRoot "jppc\event\obj"))
  "player"              = @((Join-Path $K "ply_save.bin"), (Join-Path $K "ply_rom.bin"))
}
$gateArgs = @("wave1","monster","monster-capture-bit","encounter","encounter-rebuild","event","eventscript",
  "keyitem","treasure","customization","autoability","ctbbase","mixtable","aiasm",
  "spheregrid-layout","spheregrid-build","spheregrid-edit","jptext","player")
foreach ($g in $gateArgs) {
  dotnet $EditorDll "--$g-rt0" @($gatePaths[$g]) | Out-Null
  $rc = $LASTEXITCODE
  if ($rc -ne 0) { $fail++ }
  $lines += ("editor --{0,-18}-rt0 : " -f $g) + $(if ($rc -eq 0) { "PASS" } else { "FAIL ($rc)" })
}
# --btltext-jp-decode: JP battle-text DECODE gate. Proves jppc/battle/kernel/btl_txt.bin decodes via
# JpDecoder to font-bank kanji <K:n>, 0 <MISS>, with a lossless decode->encode round-trip.
dotnet $EditorDll "--btltext-jp-decode" (Join-Path $K "btl_txt.bin") | Out-Null
$bj = $LASTEXITCODE
if ($bj -ne 0) { $fail++ }
$lines += "editor --btltext-jp-decode  : " + $(if ($bj -eq 0) { "PASS" } else { "FAIL ($bj)" })

# --kernelcmd-roundtrip: the GUI open/save path (ReadList -> wrapper Wrap/Unwrap -> WriteList) byte-identical.
dotnet $EditorDll "--kernelcmd-roundtrip" (Join-Path $FfxRoot "new_uspc\battle\kernel") | Out-Null
$kr = $LASTEXITCODE
if ($kr -ne 0) { $fail++ }
$lines += "editor --kernelcmd-roundtrip: " + $(if ($kr -eq 0) { "PASS" } else { "FAIL ($kr)" })

# --textstr-rt0 needs an explicit help_txt.bin path (no default) — left out of the auto-run; pass the path manually to verify it.

# ---- verdict ----
Section "OFFLINE CI VERDICT"
$lines | ForEach-Object { Write-Host "  $_" }
if ($fail -eq 0) {
  Write-Host "`nOFFLINE CI PASS - all no-edit/no-game regression gates held." -ForegroundColor Green
  exit 0
} else {
  Write-Host "`nOFFLINE CI FAIL - $fail gate(s) broken." -ForegroundColor Red
  exit 1
}
