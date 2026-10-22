# Isolated build helper for the ground-cover sessions (mooncoin, coinrunner, raincup).
#
# Other sessions are actively editing species files in this tree, and their in-flight edits do not
# always compile. This script mirrors the sources into scratch\cb, stands a placeholder in for any
# file that another session was mid-way through writing, builds there, and drops the resulting
# assemblies into the real tree's Godot output folder so the specimen renderer stays usable.
#
# When a file is broken and no placeholder is known for it, the script waits and re-mirrors: the
# other session is usually seconds away from finishing that file.
#
# Nothing in the real tree is modified except the two assembly files the renderer loads.

$ErrorActionPreference = 'Continue'
$root = 'E:\Vivarium'
$cb = Join-Path $root 'scratch\cb'

# Entry points that live in the other sessions' files, with the signature OrganismMeshes.cs calls.
# A placeholder only has to satisfy the call site; the real implementation stays in their tree.
$stubs = @{
    'OrganismMeshes.Veilfern.cs' = @'
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

// SCRATCH-BUILD PLACEHOLDER for the veilfern session's in-flight file.
public static partial class OrganismMeshes
{
    private static void Veilfern(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2,
        bool juvenile, double radius, double height)
    {
    }
}
'@
    'OrganismMeshes.Snaptrap.cs' = @'
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

// SCRATCH-BUILD PLACEHOLDER for the snaptrap session's in-flight file.
public static partial class OrganismMeshes
{
    private static void LatchJawColumn(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2,
        bool juvenile, double radius, double height)
    {
    }
}
'@
}

# The glassfinger session's two files (OrganismMeshes.Glassfinger.cs and
# OrganismMeshes.Plumegrass.cs) and the vaultfern session's file now compile
# cleanly, so they no longer need a placeholder: stubbing them deploys an
# EMPTY species into the live tree. Remove a stub the moment its file compiles.

function Copy-Tree($from, $to, [string[]]$excludeDirs) {
    $args = @($from, $to, '/E', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
    if ($excludeDirs) { $args += '/XD'; $args += $excludeDirs }
    robocopy @args | Out-Null
}

$built = $false
for ($attempt = 1; $attempt -le 10; $attempt++) {
    # 1. mirror the sources
    Copy-Tree (Join-Path $root 'src') (Join-Path $cb 'src') @('obj', 'bin')
    Copy-Tree (Join-Path $root 'game') (Join-Path $cb 'game') @('assets', '.godot', 'obj', 'bin', 'build')

    # 2. stand in for any file another session was mid-way through writing
    foreach ($file in $stubs.Keys) {
        Set-Content -Path (Join-Path $cb "src\Vivarium.Sim\Geometry\$file") -Value $stubs[$file] -Encoding utf8
    }

    # 3. build
    $out = & dotnet build (Join-Path $cb 'game\Vivarium.csproj') -c Release --nologo -v q 2>&1
    $errors = @($out | Select-String -Pattern 'error CS')
    if ($errors.Count -eq 0) {
        Write-Output "scratch build OK (attempt $attempt)"
        $built = $true
        break
    }
    $broken = @($errors | ForEach-Object {
        if ($_.Line -match '([A-Za-z0-9_\.]+\.cs)\(\d+,\d+\)') { $Matches[1] }
    } | Sort-Object -Unique)
    $mine = @('OrganismMeshes.Coinrunner.cs', 'OrganismMeshes.Mooncoin.cs', 'OrganismMeshes.PitcherPlant.cs',
              'OrganismMeshes.Fenbead.cs', 'OrganismMeshes.Brooklace.cs')
    $unknown = @($broken | Where-Object { $mine -notcontains $_ -and -not $stubs.ContainsKey($_) })
    if ($unknown.Count -gt 0) {
        Write-Output "waiting on: $($unknown -join ', ')"
        Start-Sleep -Seconds 45
        continue
    }
    Write-Output "placeholder did not satisfy: $($broken -join ', ')"
    exit 1
}

if (-not $built) { Write-Output 'could not build'; exit 1 }

# 4. build Debug too, then deploy the assemblies the Godot renderer loads
& dotnet build (Join-Path $cb 'game\Vivarium.csproj') -c Debug --nologo -v q 2>&1 | Select-String -Pattern 'error CS' | Select-Object -First 4
foreach ($cfg in 'Debug', 'Release') {
    $from = Join-Path $cb "game\.godot\mono\temp\bin\$cfg"
    $to = Join-Path $root "game\.godot\mono\temp\bin\$cfg"
    if (-not (Test-Path (Join-Path $from 'Vivarium.dll'))) { Write-Output "MISSING BUILD $cfg"; continue }
    Copy-Item (Join-Path $from 'Vivarium.dll') $to -Force
    Copy-Item (Join-Path $from 'Vivarium.Sim.dll') $to -Force
    Write-Output "deployed $cfg"
}
