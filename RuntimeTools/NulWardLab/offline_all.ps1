# Nul Ward - full offline prep. Stops before in-game RT2 (user turn).
param(
    [string]$GameRoot = "D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster",
    [switch]$SkipDeploy,
    [switch]$SkipDllInstall
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$kernelUs = Join-Path $GameRoot "data\mods\ffx_ps2\ffx\master\new_uspc\battle\kernel\command.bin"
$kernelJp = Join-Path $GameRoot "data\mods\ffx_ps2\ffx\master\jppc\battle\kernel\command.bin"
$exe = Join-Path $GameRoot "FFX.exe"
$hooksDll = Join-Path $GameRoot "modules\ffx-hooks.dll"
$verdictDir = Join-Path $repo "RuntimeTools\NulWardLab"
$workPack = Join-Path $repo "work\nul_ward_pack"

Write-Host "=== Nul Ward OFFLINE ALL (Jarvis-MAGIC) ===" -ForegroundColor Cyan
Write-Host "repo: $repo"

Push-Location $repo
try {
    Write-Host "`n[1/6] dotnet build Release..."
    dotnet build FFXProjectEditor/FFXProjectEditor.csproj -c Release --no-restore 2>&1 | Out-Null
    $useNoBuild = $false
    if ($LASTEXITCODE -ne 0) {
        $locked = Get-Process FFXProjectEditor -ErrorAction SilentlyContinue
        if ($locked) {
            Write-Host "  WARN: FFXProjectEditor running (PID $($locked.Id)) - using --no-build" -ForegroundColor Yellow
            $useNoBuild = $true
        } else {
            dotnet build FFXProjectEditor/FFXProjectEditor.csproj -c Release
            if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }
        }
    } else {
        Write-Host "  OK"
    }
    if (-not $useNoBuild) { Write-Host "  OK" }

    Write-Host "`n[2/6] build_hooks.ps1 -WithPolyHook -Release..."
    & (Join-Path $repo "RuntimeTools\FfxHooksDll\build_hooks.ps1") -WithPolyHook -Release
    if ($LASTEXITCODE -ne 0) { throw "hooks build failed" }
    Write-Host "  OK"

    Write-Host "`n[3/6] --nul-ward-static..."
    $staticArgs = @(
        "run", "-c", "Release", "--project", "FFXProjectEditor", "--",
        "--nul-ward-static",
        "--output", $workPack
    )
    if ($useNoBuild) { $staticArgs = @("run", "-c", "Release", "--no-build", "--project", "FFXProjectEditor", "--", "--nul-ward-static", "--output", $workPack) }
    if (Test-Path -LiteralPath $exe) { $staticArgs += @("--exe", $exe) }
    if (Test-Path -LiteralPath $kernelUs) { $staticArgs += @("--kernel", $kernelUs) }
    if (Test-Path -LiteralPath $hooksDll) { $staticArgs += @("--hooks-dll", $hooksDll) }
    dotnet @staticArgs
    if ($LASTEXITCODE -ne 0) { throw "nul-ward-static failed" }
    Write-Host "  OK -> $workPack\nul_ward_static_verdict.json"

    Write-Host "`n[4/6] --nul-ward-atel-diff..."
    $atelArgs = @("run", "-c", "Release", "--project", "FFXProjectEditor", "--", "--nul-ward-atel-diff", "--output", $workPack)
    if ($useNoBuild) { $atelArgs = @("run", "-c", "Release", "--no-build", "--project", "FFXProjectEditor", "--", "--nul-ward-atel-diff", "--output", $workPack) }
    if (Test-Path -LiteralPath $kernelUs) { $atelArgs += @("--kernel", $kernelUs) }
    dotnet @atelArgs
    if ($LASTEXITCODE -ne 0) { throw "nul-ward-atel-diff failed" }
    Write-Host "  OK -> $workPack\atel_diff_summary.json"

    $kernelAlreadyGrown = $false
    if (Test-Path -LiteralPath $kernelUs) {
        $staticJson = Join-Path $workPack "nul_ward_static_verdict.json"
        if (Test-Path $staticJson) {
            $sj = Get-Content $staticJson -Raw | ConvertFrom-Json
            $rowCheck = $sj.checks | Where-Object { $_.name -eq "entry_count" } | Select-Object -First 1
            if ($rowCheck -and $rowCheck.rows -ge 322) { $kernelAlreadyGrown = $true }
        }
    }

    if ($kernelAlreadyGrown) {
        Write-Host "`n[5/6] SKIP pack - kernel already has 322+ rows (Radiant/Umbral deployed)" -ForegroundColor Yellow
    } elseif (-not $SkipDeploy -and (Test-Path -LiteralPath $kernelUs)) {
        Write-Host "`n[5/6] --nul-ward-pack --deploy..."
        dotnet run -c Release --project FFXProjectEditor -- --nul-ward-pack --deploy `
            --kernel $kernelUs --kernel-jp $kernelJp --output $workPack
        if ($LASTEXITCODE -ne 0) { throw "nul-ward-pack deploy failed" }
        Write-Host "  OK (US+JP kernel deployed with backup)"
    } else {
        Write-Host "`n[5/6] --nul-ward-pack (stage only, no deploy)..."
        dotnet run -c Release --project FFXProjectEditor -- --nul-ward-pack --output $workPack
        if ($LASTEXITCODE -ne 0) { throw "nul-ward-pack stage failed" }
        Write-Host "  OK -> $workPack\command.bin"
    }

    if (-not $SkipDllInstall -and (Test-Path -LiteralPath (Join-Path $GameRoot "modules"))) {
        $ffx = Get-Process FFX -ErrorAction SilentlyContinue
        if ($ffx) {
            Write-Host "`n[6/6] SKIP DLL install - FFX running (PID $($ffx.Id)). Close game and run:" -ForegroundColor Yellow
            Write-Host "  RuntimeTools\FfxHooksDll\deploy\nul-ward-lab\install_to_modules.ps1 -EnableApply -EnableTeach -GameRoot `"$GameRoot`""
        } else {
            Write-Host "`n[6/6] install_to_modules.ps1 -EnableApply -EnableTeach..."
            & (Join-Path $repo "RuntimeTools\FfxHooksDll\deploy\nul-ward-lab\install_to_modules.ps1") `
                -EnableApply -EnableTeach -GameRoot $GameRoot
            Write-Host "  OK (dll + nul_ward_apply + teach flags)"
        }
    } else {
        Write-Host "`n[6/6] SKIP DLL install (no modules dir or -SkipDllInstall)"
    }

    Write-Host "`n[RT2 verdict template] --nul-ward-rt2-verdict --offline-ok..."
    $rt2Args = @("run", "-c", "Release", "--project", "FFXProjectEditor", "--", "--nul-ward-rt2-verdict", "--offline-ok", "--output", (Join-Path $verdictDir "nul_ward_rt2_verdict.json"))
    if ($useNoBuild) { $rt2Args = @("run", "-c", "Release", "--no-build", "--project", "FFXProjectEditor", "--", "--nul-ward-rt2-verdict", "--offline-ok", "--output", (Join-Path $verdictDir "nul_ward_rt2_verdict.json")) }
    dotnet @rt2Args
    # offline-ok may FAIL without cast lines - expected

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host " OFFLINE COMPLETE - YOUR TURN (in-game)" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "1. Close FFX; if DLL step skipped, run install_to_modules.ps1 -EnableApply -EnableTeach"
    Write-Host "2. Battle: cast 320/321, Holy/Dark hit, check TEMP\ffx-hooks.log"
    Write-Host "3. RuntimeTools\NulWardLab\run_rt2_gate.ps1"
    Write-Host "See RuntimeTools\NulWardLab\USER_TURN.md"
}
finally {
    Pop-Location
}
