param([Parameter(Mandatory)][string]$Name, [ValidateSet('leaf','exposure','light','family','backlight','physical','wet','looks','refine','substrate','loam','world','habitat','habitat-final','pilot','overview')][string]$Mode = 'leaf', [int]$TimeoutSeconds = 120, [string]$CameraReference, [switch]$ReviewMotion)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
if ($Name -notmatch '^[a-z0-9-]+$') { throw 'Use a lowercase run name.' }
$run = Join-Path $repo "build/photorealism/$Name"
if (Test-Path -LiteralPath $run) { throw 'Run already exists.' }
if (Test-Path (Join-Path $repo '.clanker/HALT')) { throw 'HALT present.' }
$lease = [IO.File]::Open((Join-Path $repo '.clanker/visual-lab-gpu.lease'), 'OpenOrCreate', 'ReadWrite', 'None')
$priorUser = $env:VIVARIUM_BENCHMARK_USER_DIR
function Write-PhotoSource([string]$Target) {
    $entries = foreach ($folder in @('game/App','game/Render','game/Shaders','game/content','src/Vivarium.Sim','scripts/visuals/photorealism')) {
        Get-ChildItem -LiteralPath (Join-Path $repo $folder) -File -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -in @('.cs','.gdshader','.gdshaderinc','.json','.py','.ps1') } |
            ForEach-Object { [ordered]@{ path=$_.FullName.Substring($repo.Length + 1); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } }
    }
    $entries += @(Get-ChildItem -LiteralPath (Join-Path $repo 'game/Textures') -File |
        Where-Object { $_.Name -like 'ScatteredLeaves007_4K-PNG_*' -or $_.Name -like 'Ground048_1K-JPG_*' } |
        ForEach-Object { [ordered]@{ path=$_.FullName.Substring($repo.Length + 1); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
    $entries += [ordered]@{ path='game/project.godot'; sha256=(Get-FileHash -LiteralPath (Join-Path $repo 'game/project.godot') -Algorithm SHA256).Hash }
    @($entries | Sort-Object path) | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $Target
}
try {
    New-Item -ItemType Directory -Path $run | Out-Null
    Write-PhotoSource (Join-Path $run 'source-before.json')
    & dotnet build (Join-Path $repo 'game/Vivarium.csproj') --no-restore --nologo -v quiet *> (Join-Path $run 'build.log')
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $run/build.log" }
    . (Join-Path $repo 'scripts/common.ps1')
    $godot = Get-GodotExe
    $binaries = @(Get-ChildItem (Join-Path $repo 'game/.godot/mono/temp/bin/Debug') -Filter 'Vivarium*.dll' | ForEach-Object { @{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
    $revision = (& git -C $repo rev-parse HEAD).Trim()
    $toolHashes = @(Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object { $_.Extension -in @('.py','.ps1','.json') } | ForEach-Object { @{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
    $env:VIVARIUM_BENCHMARK_USER_DIR = Join-Path $run 'user'
    $launchProject = Join-Path $repo 'game'
    $startupProjectHash = $null
    if ($Mode -eq 'physical') {
        # Physical-unit rendering state is initialized before scene scripts run.
        # Use a private configuration; runtime asset folders remain read-only inputs.
        $launchProject = Join-Path $run 'startup-project'
        New-Item -ItemType Directory -Path $launchProject | Out-Null
        foreach ($item in Get-ChildItem -LiteralPath (Join-Path $repo 'game') -Force) {
            $destination = Join-Path $launchProject $item.Name
            if ($item.PSIsContainer) { New-Item -ItemType Junction -Path $destination -Target $item.FullName | Out-Null }
            else { Copy-Item -LiteralPath $item.FullName -Destination $destination }
        }
        $privateConfig = Join-Path $launchProject 'project.godot'
        $configText = Get-Content -LiteralPath $privateConfig -Raw
        if ($configText -match '(?m)^lights_and_shadows/use_physical_light_units=') { throw 'Physical startup setting already authored; reconcile explicitly.' }
        $configText = $configText.Replace('[rendering]', "[rendering]`nlights_and_shadows/use_physical_light_units=true")
        [IO.File]::WriteAllText($privateConfig, $configText, (New-Object Text.UTF8Encoding($false)))
        $startupProjectHash = (Get-FileHash -LiteralPath $privateConfig).Hash
    }
    $reviewFlag = switch ($Mode) { 'pilot' { '--pilot-overhaul-review' } 'overview' { '--world-review' } 'habitat-final' { '--world-review' } 'habitat' { '--world-review' } 'world' { '--world-review' } 'loam' { '--loam-ground-review' } 'physical' { '--photographic-physical-review' } { $_ -in @('family','backlight','wet','looks','refine','substrate') } { '--photographic-family-review' } 'light' { '--photorealism-light-review' } default { '--photorealism-review' } }
    $argsPhoto = @('--path', $launchProject, '--log-file', (Join-Path $run 'engine.log'), '--', $reviewFlag, (Join-Path $run 'captures'))
    if ($Mode -eq 'overview') {
        if (!$CameraReference) { throw 'Overview requires an explicit camera reference so the full tall tree is framed.' }
        $referencePath = (Resolve-Path -LiteralPath $CameraReference).Path
        Copy-Item -LiteralPath $referencePath -Destination (Join-Path $run 'cameras.json')
        $argsPhoto += @('--review-camera-reference', (Join-Path $run 'cameras.json'), '--review-light-seconds', '60')
        if ($ReviewMotion) { $argsPhoto += '--review-motion' }
    }
    if ($Mode -eq 'habitat-final') { $argsPhoto += @('--forest-review', '--habitat-light-production-check') }
    if ($Mode -eq 'habitat') { $argsPhoto += @('--forest-review', '--habitat-light-sweep') }
    if ($Mode -eq 'world') { $argsPhoto += @('--forest-review', '--review-scenes', 'loam_floor_close,loam_floor_medium,log_contact,water_margin', '--review-motion', '--loam-live') }
    if ($Mode -eq 'exposure') { $argsPhoto += '--exposure-sweep' }
    if ($Mode -in @('family','backlight','wet','looks','refine','substrate')) { $argsPhoto += @('--family-profiles', (Join-Path $PSScriptRoot 'family-profiles.json')) }
    if ($Mode -eq 'backlight') { $argsPhoto += '--backlight-sweep' }
    if ($Mode -eq 'wet') { $argsPhoto += '--wet-layer-sweep' }
    if ($Mode -eq 'looks') { $argsPhoto += '--look-sweep' }
    if ($Mode -eq 'refine') { $argsPhoto += '--look-refine' }
    if ($Mode -eq 'substrate') { $argsPhoto += '--substrate-refine' }
    $quoted = $argsPhoto | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $p = Start-Process -FilePath $godot -ArgumentList $quoted -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run 'stdout.log') -RedirectStandardError (Join-Path $run 'stderr.log')
    $processHandle = $p.Handle # retain the native handle so Windows PowerShell preserves ExitCode
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (!$p.WaitForExit(500)) {
        if ($timer.Elapsed.TotalSeconds -gt $TimeoutSeconds) { Stop-Process -Id $p.Id -Force; throw 'Owned capture timed out.' }
    }
    $p.WaitForExit()
    $startupStable = !$startupProjectHash -or ((Get-FileHash -LiteralPath (Join-Path $launchProject 'project.godot')).Hash -eq $startupProjectHash)
    Write-PhotoSource (Join-Path $run 'source-after.json')
    $stable = (Get-FileHash (Join-Path $run 'source-before.json')).Hash -eq (Get-FileHash (Join-Path $run 'source-after.json')).Hash
    $stdout = [string](Get-Content (Join-Path $run 'stdout.log') -Raw)
    $stderr = [string](Get-Content (Join-Path $run 'stderr.log') -Raw)
    $successMarker = switch ($Mode) { 'pilot' { 'PILOT_OVERHAUL_REVIEW_OK' } 'overview' { 'VIVARIUM_WORLDREVIEW_OK' } 'habitat-final' { 'VIVARIUM_WORLDREVIEW_OK' } 'habitat' { 'VIVARIUM_WORLDREVIEW_OK' } 'world' { 'VIVARIUM_WORLDREVIEW_OK' } 'loam' { 'LOAM_GROUND_REVIEW_OK' } 'looks' { 'PHOTO_LOOK_SWEEP_OK' } 'refine' { 'PHOTO_LOOK_SWEEP_OK' } 'substrate' { 'PHOTO_LOOK_SWEEP_OK' } 'wet' { 'PHOTO_WET_LAYER_REVIEW_OK' } 'physical' { 'PHOTO_PHYSICAL_REVIEW_OK' } 'backlight' { 'PHOTO_BACKLIGHT_REVIEW_OK' } 'family' { 'PHOTO_FAMILY_REVIEW_OK' } 'exposure' { 'PHOTO_EXPOSURE_REVIEW_OK' } 'light' { 'PHOTO_REALISM_LIGHT_REVIEW_OK' } default { 'PHOTO_REALISM_REVIEW_OK' } }
    $ok = $p.ExitCode -eq 0 -and $stdout.Contains($successMarker) -and $stderr -notmatch 'SHADER ERROR|SCRIPT ERROR|Parse Error|ERROR:' -and $stable -and $startupStable
    @{mode=$Mode;revision=$revision;tools=$toolHashes;launch_project=$launchProject;startup_project_sha256=$startupProjectHash;exit_code=$p.ExitCode;source_stable=$stable;native_success=$ok;elapsed_seconds=$timer.Elapsed.TotalSeconds;godot_sha256=(Get-FileHash $godot).Hash;binaries=$binaries;limitation='Shared machine with open editors; diagnostic capture, not isolated performance acceptance.'} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'launch.json')
    if (!$ok) { throw "Native capture did not pass: $run" }
    Write-Output $run
} finally { $env:VIVARIUM_BENCHMARK_USER_DIR=$priorUser; $lease.Dispose() }
