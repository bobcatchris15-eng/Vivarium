<#
    Isolated compile/measure harness for adding a flora species to the tree.

    Several sessions author species generators in the same tree at once, so a plain
    `dotnet build` fights over bin/ and obj/ and reports failures that belong to another
    session's half-written file. This copies the sim sources and the CLI into a private
    directory, replaces every OTHER new-flora species file with a generated stub, and builds
    there. Nothing in the repository is written to.

    Usage:
      powershell -File scratch\isolated-sim-build.ps1 -Name Silverweed
      powershell -File scratch\isolated-sim-build.ps1 -Name Silverweed -Measure silverweed
#>
param(
    [Parameter(Mandatory = $true)][string]$Name,
    [string]$Measure = ''
)

$ErrorActionPreference = 'Stop'
$root = 'E:\Vivarium'
$tmp = Join-Path $env:TEMP "opencode\sim-$Name"

# The species of the 2026-10-21 new-flora set. A build for one of them stubs the others out.
$set = @('Glasslily', 'Silverweed', 'Coalspike', 'Marrowbloom', 'Blackneedle',
         'Starbulb', 'Bonecup', 'Embercup', 'Hollowcrown', 'Tankbloom')

if ($tmp) { if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp } }
New-Item -ItemType Directory -Force -Path (Join-Path $tmp 'src') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $tmp 'game') | Out-Null

Copy-Item -Recurse -Force (Join-Path $root 'src\Vivarium.Sim') (Join-Path $tmp 'src\Vivarium.Sim')
Copy-Item -Recurse -Force (Join-Path $root 'src\Vivarium.Cli') (Join-Path $tmp 'src\Vivarium.Cli')
Copy-Item -Recurse -Force (Join-Path $root 'game\content') (Join-Path $tmp 'game\content')
Set-Content -Path (Join-Path $tmp 'Vivarium.sln') -Value 'isolated build sandbox'

foreach ($stale in @('bin', 'obj')) {
    Remove-Item -Recurse -Force (Join-Path $tmp "src\Vivarium.Sim\$stale") -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force (Join-Path $tmp "src\Vivarium.Cli\$stale") -ErrorAction SilentlyContinue
}

# Every other species in the set is replaced by a stub that satisfies the dispatch line, so the
# build reflects the file the caller is responsible for and nothing else.
foreach ($other in $set) {
    if ($other -eq $Name) { continue }
    $path = Join-Path $tmp "src\Vivarium.Sim\Geometry\OrganismMeshes.$other.cs"
    # The tree's dispatch passes an extra juvenile flag to the veteran tree and nothing else.
    $extra = if ($other -eq 'Hollowcrown') { ', bool juvenile' } else { '' }
    $stub = @"
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>Isolated-build stub for $other.</summary>
public static partial class OrganismMeshes
{
    private static void $other(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height$extra) { }
}
"@
    Set-Content -Path $path -Value $stub
}

dotnet build (Join-Path $tmp 'src\Vivarium.Cli\Vivarium.Cli.csproj') -v q --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($Measure -ne '') {
    Push-Location $tmp
    dotnet run --no-build --project (Join-Path $tmp 'src\Vivarium.Cli\Vivarium.Cli.csproj') -- flora-mesh --species $Measure --seed 42
    $code = $LASTEXITCODE
    Pop-Location
    exit $code
}
exit 0
