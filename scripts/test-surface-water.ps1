[CmdletBinding()]
param([switch]$ExcludeKnownBaselineFailures)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$out = Join-Path $repo "build/surface-water-replacement/tests-$(Get-Date -Format yyyyMMdd-HHmmss)"
if (Test-Path $out) { throw "Evidence directory exists: $out" }
New-Item -ItemType Directory -Path $out | Out-Null
$filter = '(Suite=Hydrology|Suite=Persistence|Suite=Tools|Suite=Camera|Suite=Integration|FullyQualifiedName~SurfaceFluidReplacement|FullyQualifiedName~SurfaceErosion|FullyQualifiedName~NightFlora)&Speed!=Slow'
if ($ExcludeKnownBaselineFailures) {
    $known = @('StarterPopulationsAreCompleteAndValid','DefaultPresetExposesEveryHabitat','EveryToolChangesOnlyItsIntendedState')
    Write-Host "Explicitly excluding independently reproduced baseline failures: $($known -join ', ')" -ForegroundColor Yellow
    foreach ($name in $known) { $filter += "&FullyQualifiedName!~$name" }
}
Push-Location $repo
try {
    & dotnet test tests/Vivarium.Sim.Tests --artifacts-path (Join-Path $out 'artifacts') --filter $filter --logger 'trx;LogFileName=fluid.trx' --results-directory $out 2>&1 | Tee-Object -FilePath (Join-Path $out 'test.log')
    $code = $LASTEXITCODE
    $receipt = Join-Path $out 'fluid.trx'
    if (-not (Test-Path $receipt)) { throw 'No test receipt was produced' }
    [xml]$trx = Get-Content $receipt -Raw
    $counts = $trx.TestRun.ResultSummary.Counters
    if ($code -ne 0 -or [int]$counts.failed -ne 0 -or [int]$counts.passed -eq 0) { throw "Tests failed; see $out" }
    Write-Host "$($counts.passed) tests passed; evidence: $out"
    if ($ExcludeKnownBaselineFailures) { Write-Host 'This is a targeted regression pass with three baseline failures excluded, not a clean full-suite result.' }
}
finally { Pop-Location }
