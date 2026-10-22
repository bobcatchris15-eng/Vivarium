# Isolated build/test sandbox for the vine work.
#
# Another session is actively editing this tree and running its own test runs, which hold locks on the shared
# build output. This script copies the live sources into scratch\vinebox and builds and tests there, so the
# vine work can be verified without fighting for the shared bin/obj directories. The live tree is never modified.
#
#   .\scratch\vinebox.ps1            # sync + build + run the vine tests
#   .\scratch\vinebox.ps1 -Test All  # sync + build + run every test

param(
    [string]$Test = 'Vine'
)

$ErrorActionPreference = 'Stop'
$root = 'E:\Vivarium'
$box = Join-Path $root 'scratch\vinebox'

if (Test-Path $box) { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $box | Out-Null

function Copy-Tree($from, $to) {
    & robocopy $from $to /E /NFL /NDL /NJH /NJS /NP /XD bin obj .godot | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $from" }
}

Copy-Tree (Join-Path $root 'src') (Join-Path $box 'src')
Copy-Tree (Join-Path $root 'tests') (Join-Path $box 'tests')
Copy-Tree (Join-Path $root 'game\content') (Join-Path $box 'game\content')
Copy-Item (Join-Path $root 'Vivarium.sln') $box -Force
Write-Output "vinebox synced"

Push-Location $box
& dotnet build tests/Vivarium.Sim.Tests/Vivarium.Sim.Tests.csproj -c Debug --nologo -v q
$buildOk = ($LASTEXITCODE -eq 0)
Pop-Location
if (-not $buildOk) { throw 'vinebox build failed' }

$filter = switch ($Test) {
    'Vine' { 'FullyQualifiedName~ClimberGrowthTests|FullyQualifiedName~ClimberFoliage|FullyQualifiedName~ClimbersGenerateTrailing|FullyQualifiedName~ClimbersNeed|FullyQualifiedName~ClimberEstablishes|FullyQualifiedName~ClimberSearches' }
    'Flora' { 'FullyQualifiedName~FloraTests' }
    'All' { 'Speed!=Slow' }
    default { $Test }
}

Push-Location $box
& dotnet test tests/Vivarium.Sim.Tests -c Debug --no-build --filter $filter --nologo
$code = $LASTEXITCODE
Pop-Location
exit $code
