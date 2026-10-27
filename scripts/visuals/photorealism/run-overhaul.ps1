param(
    [Parameter(Mandatory)][string]$Name,
    [ValidateSet('world','reference','guardian','fauna','water','aquatic','isolation')][string]$Mode = 'world',
    [string]$Species = 'coalback_beetle',
    [string]$IsolatedSpecies,
    [int]$TimeoutSeconds = 900,
    [int]$LeaseWaitSeconds = 0,
    [string]$LoadSave,
    [string]$Scenes,
    [string]$SourceRoot,
    [switch]$ArtifactProbe,
    [switch]$SundewProbe,
    [switch]$SundewExtended,
    [switch]$IsolatedDof,
    [switch]$WorldDofProbe,
    [switch]$LiveRender,
    [switch]$LogFiniteProbe,
    [switch]$RebuildCheck,
    [switch]$LegacyCoverage,
    [Nullable[double]]$LightSeconds
)
$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$repo = if ($SourceRoot) { (Resolve-Path $SourceRoot).Path } else { $workspace }
if ($Name -notmatch '^[a-z0-9-]+$') { throw 'Use a unique lowercase run name.' }
if (Test-Path (Join-Path $workspace '.clanker/HALT')) { throw 'HALT present.' }
$run = Join-Path $workspace "build/photorealism/$Name"
if (Test-Path $run) { throw 'Run directory exists; preserve its evidence.' }
New-Item -ItemType Directory -Path $run | Out-Null
function SourceManifest {
    $rows = foreach ($folder in @('game/App','game/Camera','game/Render','game/Shaders','game/content','game/Textures','game/assets','src/Vivarium.Sim')) {
        Get-ChildItem (Join-Path $repo $folder) -File -Recurse |
            Where-Object { $_.Extension -in @('.cs','.gdshader','.gdshaderinc','.json','.res','.png','.jpg','.exr','.import','.glb','.tscn','.tres') -and $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } |
            Sort-Object FullName | ForEach-Object {
                [ordered]@{path=$_.FullName.Substring($repo.Length + 1);sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}
            }
    }
    $rows += [ordered]@{path='game/project.godot';sha256=(Get-FileHash (Join-Path $repo 'game/project.godot')).Hash}
    $rows | ConvertTo-Json -Depth 4 -Compress
}
$lease = $null
$leaseTimer = [Diagnostics.Stopwatch]::StartNew()
while ($null -eq $lease) {
    try { $lease = [IO.File]::Open((Join-Path $workspace '.clanker/visual-lab-gpu.lease'), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch [IO.IOException] {
        if ($leaseTimer.Elapsed.TotalSeconds -ge $LeaseWaitSeconds) { throw }
        Start-Sleep -Milliseconds 1000
    }
}
$priorUser = $env:VIVARIUM_BENCHMARK_USER_DIR
$p = $null
try {
    & dotnet build (Join-Path $repo 'Vivarium.sln') -c Debug --no-restore -v quiet *> (Join-Path $run 'sim-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Simulation build failed.' }
    & dotnet build (Join-Path $repo 'game/Vivarium.csproj') -c Debug --no-restore -v quiet *> (Join-Path $run 'game-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Client build failed.' }
    $before = SourceManifest
    $before | Set-Content (Join-Path $run 'source-before.json')
    $binaryManifest = Get-ChildItem (Join-Path $repo 'game/.godot/mono/temp/bin/Debug') -Filter 'Vivarium*.dll' |
        ForEach-Object { @{name=$_.Name;sha256=(Get-FileHash $_.FullName).Hash} }
    $binaryManifest | ConvertTo-Json | Set-Content (Join-Path $run 'binaries.json')
    $env:VIVARIUM_BENCHMARK_USER_DIR = Join-Path $run 'user'
    $captures = Join-Path $run 'captures'
    $launch = @('--path', (Join-Path $repo 'game'), '--log-file', (Join-Path $run 'engine.log'), '--')
    switch ($Mode) {
        'world' {
            $launch += @('--world-review', $captures)
            if (!$WorldDofProbe) { $launch += '--review-motion' }
        }
        'reference' { $launch += @('--reference', $captures) }
        'guardian' { $launch += @('--guardian-review', $captures, '--guardian-material-review', '--guardian-coat-detail', '--guardian-surface-detail') }
        'fauna' { $launch += @('--fauna-preview', $Species, '--output', $captures, '--live-motion') }
        'water' { $launch += @('--surface-fluid-review', $captures, '--scenario', 'D', '--motion') }
        'aquatic' { $launch += @('--overhaul-aquatic-review', $captures) }
        'isolation' { $launch += @('--species-isolation-review', $captures) }
    }
    if ($LoadSave) { $launch += @('--load', (Resolve-Path $LoadSave).Path) }
    if ($Scenes) { $launch += @('--review-scenes', $Scenes) }
    if ($ArtifactProbe) { $launch += '--review-artifact-probe' }
    if ($SundewProbe) { $launch += '--review-sundew-probe' }
    if ($SundewExtended) { $launch += @('--review-sundew-probe','--review-sundew-extended') }
    if ($IsolatedSpecies) { $launch += @('--isolation-species',$IsolatedSpecies) }
    if ($IsolatedDof) { $launch += '--isolation-dof' }
    if ($WorldDofProbe) { $launch += '--review-world-dof-probe' }
    if ($LiveRender) { $launch += '--review-live-render' }
    if ($LogFiniteProbe) { $launch += '--review-log-finite' }
    if ($RebuildCheck) { $launch += '--review-rebuild-check' }
    if ($LegacyCoverage) { $launch += '--review-coverage-legacy' }
    if ($null -ne $LightSeconds) { $launch += @('--review-light-seconds', $LightSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)) }
    $quoted = $launch | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    . (Join-Path $workspace 'scripts/common.ps1')
    $p = Start-Process -FilePath (Get-GodotExe) -ArgumentList $quoted -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run 'stdout.log') -RedirectStandardError (Join-Path $run 'stderr.log')
    $handle = $p.Handle
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (!$p.WaitForExit(500)) {
        if ($timer.Elapsed.TotalSeconds -gt $TimeoutSeconds) { Stop-Process -Id $p.Id -Force; throw 'Owned review timed out.' }
    }
    $p.WaitForExit()
    $after = SourceManifest
    $after | Set-Content (Join-Path $run 'source-after.json')
    $errors = @(Select-String -Path (Join-Path $run 'stderr.log') -Pattern 'SHADER ERROR:|SCRIPT ERROR:|ERROR:|Exception|Unable to open file')
    $stable = $before -ceq $after
    $summary = [ordered]@{mode=$Mode;source_root=$repo;revision=(& git -C $repo rev-parse HEAD).Trim();exit_code=$p.ExitCode;source_stable=$stable;engine_errors=$errors.Count;elapsed_seconds=$timer.Elapsed.TotalSeconds;png_count=@(Get-ChildItem $captures -Recurse -Filter '*.png' -ErrorAction SilentlyContinue).Count;gpu='Native device recorded in stdout.log';performance_acceptance=$false}
    $summary | ConvertTo-Json | Set-Content (Join-Path $run 'run.json')
    $captureComplete = $summary.png_count -gt 0 -and ($Mode -ne 'world' -or (Test-Path (Join-Path $captures 'world_review.json')))
    if ($p.ExitCode -ne 0 -or !$stable -or $errors.Count -gt 0 -or !$captureComplete) { throw "Review failed or incomplete: $($summary | ConvertTo-Json -Compress)" }
    $summary | ConvertTo-Json -Compress
}
finally {
    $env:VIVARIUM_BENCHMARK_USER_DIR = $priorUser
    $lease.Dispose()
}
