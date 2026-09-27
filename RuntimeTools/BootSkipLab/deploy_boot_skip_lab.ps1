# Lab deploy: ffx-hooks with BootSkipHook compiled (NOT wired until dllmain WIRE-ME uncommented).
# Does NOT touch production flags unless -ArmObserve is passed.
#
#   .\deploy_boot_skip_lab.ps1 [-GameRoot path] [-ArmObserve] [-SkipBuild]
param(
    [string]$GameRoot = "D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster",
    [switch]$ArmObserve,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$hooks = Join-Path $repo "RuntimeTools\FfxHooksDll"

if (Get-Process FFX -ErrorAction SilentlyContinue) { throw "Close FFX before deploy." }
if (-not (Test-Path $GameRoot)) { throw "GameRoot not found: $GameRoot" }

if (-not $SkipBuild) {
    & (Join-Path $hooks "build_hooks.ps1") -WithPolyHook -Release -Deploy -LabDeploy -GameRoot $GameRoot
}

$modules = Join-Path $GameRoot "modules"
$config = Join-Path $modules "config"
New-Item -ItemType Directory -Force -Path $config | Out-Null

$dll = Join-Path $hooks "bin\Release\ffx-hooks.dll"
if (-not (Test-Path $dll)) { throw "Missing $dll — build failed?" }

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$dest = Join-Path $modules "ffx-hooks.dll"
if (Test-Path $dest) {
    Copy-Item $dest (Join-Path $modules "ffx-hooks.dll.backup-bootskip-$stamp") -Force
}
Copy-Item $dll $dest -Force
Write-Host "Deployed $dll -> $dest"

if ($ArmObserve) {
    New-Item -ItemType File -Force -Path (Join-Path $config "fast_boot_skip.flag") | Out-Null
    Write-Warning "fast_boot_skip.flag created — hook still INACTIVE until dllmain WIRE-ME block is uncommented."
    Write-Warning "Set FFXHOOKS_FAST_BOOT_OBSERVE_ONLY=1 before launch when wired."
} else {
    Write-Host "No flags armed. Uncomment WIRE-ME in dllmain.cpp + pass -ArmObserve to enable observe mode."
}

Write-Host @"

Lab deploy complete. Isolation notes:
  - BootSkipHook.cpp is linked but InstallBootSkipHook is NOT called (WIRE-ME in dllmain.cpp).
  - Do not combine with native_menu.flag / field_scout* during titl00 boot.
  - Prefer probe-only boot_trace.ps1 / auto_title.ps1 until RT-BS-03 passes after wiring.

"@
