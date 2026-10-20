param(
    [Parameter(Mandatory)][string]$Name,
    [Parameter(Mandatory)][string]$SourceRepo,
    [string]$Species = 'armored_forest,quilled_arboreal,crested_canopy,woodland_omnivore',
    [switch]$PositiveControl,
    [int]$TimeoutSeconds = 360
)
$ErrorActionPreference = 'Stop'
$ownerRepo = Split-Path (Split-Path $PSScriptRoot)
$oldLocation = Get-Location
$repo = (Resolve-Path -LiteralPath $SourceRepo).Path
if ($Name -notmatch '^[a-z0-9-]+$') { throw 'Use a simple lowercase capture name.' }
$output = Join-Path $ownerRepo "build/forest-reference-20261015/$Name"
if (Test-Path -LiteralPath $output) { throw 'Capture already exists; choose a new name.' }
if (Test-Path (Join-Path $ownerRepo '.clanker/HALT')) { throw 'HALT present.' }
$lease = [IO.File]::Open((Join-Path $ownerRepo '.clanker/visual-lab-gpu.lease'), 'OpenOrCreate', 'ReadWrite', 'None')
$oldUser = $env:VIVARIUM_BENCHMARK_USER_DIR
try {
    Set-Location -LiteralPath $repo
    New-Item -ItemType Directory -Path $output | Out-Null
    $busy = @(Get-Process | Where-Object { $_.ProcessName -match '^(Godot|Vivarium|blender)' })
    & dotnet build game/Vivarium.csproj --no-restore --no-incremental --nologo -v quiet *> (Join-Path $output 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Build failed; inspect build.log.' }
    & python scripts/visuals/forest_source_manifest.py (Join-Path $output 'source-before.json')
    if ($LASTEXITCODE -ne 0) { throw 'Source manifest failed.' }
    $binaries = @(Get-ChildItem -LiteralPath (Join-Path $repo 'game/.godot/mono/temp/bin/Debug') -Filter 'Vivarium*.dll' | ForEach-Object {
        [ordered]@{ name=$_.Name; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    . (Join-Path $ownerRepo 'scripts/common.ps1')
    $env:VIVARIUM_BENCHMARK_USER_DIR = Join-Path $output 'user'
    $arguments = @('--path', (Join-Path $repo 'game'), '--log-file', (Join-Path $output 'engine.log'), '--',
        '--guardian-review', $output, '--guardian-material-review', '--guardian-studio-only',
        '--guardian-species', $Species)
    if ($PositiveControl) { $arguments += '--guardian-material-positive-control' }
    $quoted = $arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $process = Start-Process -FilePath (Get-GodotExe) -ArgumentList $quoted -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'stdout.log') -RedirectStandardError (Join-Path $output 'stderr.log')
    if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw 'Owned capture timed out; evidence retained.'
    }
    $process.WaitForExit()
    & python scripts/visuals/forest_source_manifest.py (Join-Path $output 'source-after.json')
    if ($LASTEXITCODE -ne 0) { throw 'Source manifest failed after capture.' }
    $sourceBefore = Get-Content (Join-Path $output 'source-before.json') -Raw | ConvertFrom-Json
    $sourceAfter = Get-Content (Join-Path $output 'source-after.json') -Raw | ConvertFrom-Json
    $stable = $sourceBefore.tree_sha256 -eq $sourceAfter.tree_sha256
    $errors = [string](Get-Content (Join-Path $output 'stderr.log') -Raw)
    $stdout = [string](Get-Content (Join-Path $output 'stdout.log') -Raw)
    $ok = $process.ExitCode -eq 0 -and $stable -and $stdout -match 'GUARDIAN_REVIEW_OK' -and $errors -notmatch 'SHADER ERROR|SCRIPT ERROR|Failed to load'
    $images = @(Get-ChildItem -LiteralPath $output -Filter '*.png' -File -Recurse | ForEach-Object {
        $rel = $_.FullName.Substring($output.Length).TrimStart('\','/')
        [ordered]@{ path=$rel; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    [ordered]@{ success=$ok; exit_code=$process.ExitCode; source_stable=$stable; source_tree_sha256=$sourceBefore.tree_sha256; source_repo=$repo;
        forced_recompile=$true; binaries=$binaries; arguments=$arguments; competing_pids=@($busy.Id); performance_evaluated=$false; images=$images;
        lighting='production canopy daylight phase60 with fixed exposure, then explicit grazing-light probe';
        limitations='studio plane; animation seeks and initial CPU poses do not establish fully matched motion or populated-scene acceptance';
        captured_utc=[DateTime]::UtcNow.ToString('o') } | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $output 'launch.json') -Encoding utf8
    if (!$ok) { throw 'Capture failed validation; evidence retained.' }
    Write-Output $output
} finally {
    $env:VIVARIUM_BENCHMARK_USER_DIR = $oldUser
    $lease.Dispose()
    Set-Location -LiteralPath $oldLocation.Path
}
