<#
.SYNOPSIS
  Vivarium verification runner. Exit code 0 = every selected suite passed.
.EXAMPLE
  ./scripts/verify.ps1                         # all fast suites + Godot boot/smoke
  ./scripts/verify.ps1 -Suite World,Hydrology  # targeted
  ./scripts/verify.ps1 -Suite All -IncludeSlow # everything incl. multi-week soaks and render tour
Sim suites: Bootstrap World Camera Time Fields Hydrology Flora Fauna Genetics Ecology Tools Persistence Perf Integration
Godot suites: Boot (headless) Smoke (headless UI smoke + reload) Render (windowed screenshot tour) Package (export + release smoke)
#>
param(
    [string[]]$Suite = @('All'),
    [switch]$IncludeSlow,
    [int]$GodotTimeoutSec = 600
)
. (Join-Path $PSScriptRoot 'common.ps1')
Set-Location $RepoRoot

$simSuites = 'Bootstrap','World','Camera','Time','Fields','Hydrology','Flora','Fauna','Genetics','Ecology','Tools','Persistence','Perf','Integration'
$godotSuites = 'Boot','Smoke','Render','Package'
$all = $Suite -contains 'All'
$selectedSim = if ($all) { $simSuites } else { $Suite | Where-Object { $simSuites -contains $_ } }
$selectedGodot = if ($all) { @('Boot','Smoke') + $(if ($IncludeSlow) { 'Render','Package' } else { @() }) } else { $Suite | Where-Object { $godotSuites -contains $_ } }
$unknown = $Suite | Where-Object { $_ -ne 'All' -and $simSuites -notcontains $_ -and $godotSuites -notcontains $_ }
if ($unknown) { Write-Error "Unknown suite(s): $($unknown -join ', ')"; exit 2 }

$results = [ordered]@{}
$out = Join-Path $RepoRoot 'build\verify'
New-Item -ItemType Directory -Force $out | Out-Null

function Record($name, [bool]$ok, $detail) { $results[$name] = @{ ok = $ok; detail = $detail }; Write-Host ("{0,-12} {1}  {2}" -f $name, $(if ($ok) { 'PASS' } else { 'FAIL' }), $detail) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' }) }

# ---------------------------------------------------------------- build
Write-Host "Building..." -ForegroundColor Cyan
dotnet build Vivarium.sln -c Debug -nologo -v q | Out-Host
if ($LASTEXITCODE -ne 0) { Record 'Build' $false 'dotnet build failed'; exit 1 }
if ($selectedGodot.Count -gt 0) {
    dotnet build game/Vivarium.csproj -c Debug -nologo -v q | Out-Host
    if ($LASTEXITCODE -ne 0) { Record 'BuildGame' $false 'game build failed'; exit 1 }
}

# ---------------------------------------------------------------- sim suites (xUnit)
foreach ($s in $selectedSim) {
    $filter = "Suite=$s"
    if (-not $IncludeSlow) { $filter += "&Speed!=Slow" }
    $trx = "$s.trx"
    dotnet test tests/Vivarium.Sim.Tests --no-build --filter $filter --logger "trx;LogFileName=$trx" --results-directory $out -nologo 2>&1 | Tee-Object -FilePath "$out\$s.log" | Select-String -Pattern 'Passed!|Failed!|No test matches|error' | ForEach-Object { $_.Line.Trim() } | Out-Null
    $summary = Select-String -Path "$out\$s.log" -Pattern '(Passed|Failed)!\s+-\s+Failed:\s+(\d+), Passed:\s+(\d+)' | Select-Object -Last 1
    if ($summary) {
        $failed = [int]$summary.Matches[0].Groups[2].Value; $passed = [int]$summary.Matches[0].Groups[3].Value
        Record $s ($failed -eq 0 -and $LASTEXITCODE -eq 0) "$passed passed, $failed failed"
    } else { Record $s $false "no test results (see build\verify\$s.log)" }
}

# ---------------------------------------------------------------- godot suites
if ($selectedGodot.Count -gt 0) { $godot = Get-GodotExe }
$game = Join-Path $RepoRoot 'game'
foreach ($s in $selectedGodot) {
    switch ($s) {
        'Boot' {
            $r = Invoke-Timed $godot @('--headless','--path',$game,'--','--boot-test') $GodotTimeoutSec "$out\boot"
            Record 'Boot' ($r.ExitCode -eq 0 -and $r.Out -match 'VIVARIUM_BOOT_OK') ($r.Out -split "`n" | Where-Object { $_ -match 'VIVARIUM_BOOT ' } | Select-Object -First 1)
        }
        'Smoke' {
            $d = Join-Path $out 'smoke'; Remove-Item -Recurse -Force $d -ErrorAction SilentlyContinue; New-Item -ItemType Directory $d | Out-Null
            $r1 = Invoke-Timed $godot @('--headless','--path',$game,'--','--smoke',$d) $GodotTimeoutSec "$d\smoke"
            $r2 = if ($r1.ExitCode -eq 0) { Invoke-Timed $godot @('--headless','--path',$game,'--','--smoke-reload',$d) $GodotTimeoutSec "$d\reload" } else { @{ ExitCode = -1; Err = 'skipped' } }
            Record 'Smoke' ($r1.ExitCode -eq 0 -and $r2.ExitCode -eq 0) "smoke exit $($r1.ExitCode), reload exit $($r2.ExitCode) (reports in build\verify\smoke)"
        }
        'Render' {
            $d = Join-Path $out 'render'; Remove-Item -Recurse -Force $d -ErrorAction SilentlyContinue; New-Item -ItemType Directory $d | Out-Null
            $r = Invoke-Timed $godot @('--path',$game,'--','--render-test',$d) $GodotTimeoutSec "$d\render"
            $shaderErrors = ($r.Err + $r.Out) -split "`n" | Where-Object { $_ -match 'SHADER ERROR|SCRIPT ERROR|Failed to load' }
            Record 'Render' ($r.ExitCode -eq 0 -and -not $shaderErrors) "exit $($r.ExitCode), $(@($shaderErrors).Count) shader/script errors, screenshots in build\verify\render"
        }
        'Package' {
            & (Join-Path $PSScriptRoot 'export.ps1') | Out-Host
            if ($LASTEXITCODE -ne 0) { Record 'Package' $false 'export failed'; continue }
            & (Join-Path $PSScriptRoot 'smoke-release.ps1') | Out-Host
            Record 'Package' ($LASTEXITCODE -eq 0) 'export + release smoke'
        }
    }
}

$failedSuites = @($results.Keys | Where-Object { -not $results[$_].ok })
$results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $out 'summary.json')
if ($failedSuites.Count -gt 0) { Write-Host "FAILED suites: $($failedSuites -join ', ')" -ForegroundColor Red; exit 1 }
Write-Host "All $($results.Count) suite(s) passed." -ForegroundColor Green
exit 0
