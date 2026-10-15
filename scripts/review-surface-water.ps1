[CmdletBinding()]
param(
    [ValidateSet('A','B','C','D','E','F','H')][string]$Scenario = 'A',
    [ValidateSet('Static','Motion','Video','Benchmark')][string]$Mode = 'Static',
    [string]$OutputDirectory,
    [string]$Preset,
    [string]$SavePath,
    [double]$DurationSeconds = 0,
    [ValidateSet('oblique','macro','ground','pond')][string]$View = 'oblique',
    [int]$TimeoutSeconds = 900,
    [switch]$SkipBuild,
    [switch]$DisableMobileDrawing
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo "build/surface-water-replacement/review-$Scenario-$Mode-$(Get-Date -Format yyyyMMdd-HHmmss)" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path $OutputDirectory) { throw "Use a new evidence directory: $OutputDirectory" }
if (-not $SkipBuild) {
    & dotnet build (Join-Path $repo 'game/Vivarium.csproj') -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Game build failed' }
}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$exe = Join-Path $repo 'tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe'
$arguments = @('--path', ('"' + (Join-Path $repo 'game') + '"'), '--', '--surface-fluid-review', ('"' + $OutputDirectory + '"'), '--scenario', $Scenario)
$arguments += @('--fluid-view', $View)
if ($Preset) { $arguments += @('--fluid-preset', $Preset) }
if ($SavePath) { $arguments += @('--save', ('"' + [IO.Path]::GetFullPath($SavePath) + '"')) }
if ($DurationSeconds -gt 0) { $arguments += @('--fluid-duration', $DurationSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)) }
if ($Mode -eq 'Motion') { $arguments += '--motion' }
if ($Mode -eq 'Benchmark') { $arguments += '--benchmark' }
if ($Mode -eq 'Video') { $arguments += '--fluid-video' }
if ($DisableMobileDrawing) { $arguments += '--fluid-drawing-off' }
$stdout = Join-Path $OutputDirectory 'stdout.log'
$stderr = Join-Path $OutputDirectory 'stderr.log'
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
try {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not $process.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Review timed out' }
    }
    if ($process.ExitCode -ne 0) { throw "Review exited $($process.ExitCode)" }
    $log = (Get-Content $stdout -Raw) + (Get-Content $stderr -Raw)
    if ($log -match '(?m)SHADER ERROR|^ERROR:|SURFACE_FLUID_REVIEW_FAILED' -or $log -notmatch 'SURFACE_FLUID_REVIEW_OK') { throw 'Review log contains a failure or lacks completion marker' }
    foreach ($file in @('physical.json','46-reloaded.png','review.vivsave')) {
        if (-not (Test-Path (Join-Path $OutputDirectory $file))) { throw "Missing evidence: $file" }
    }
    $receipt = Get-Content (Join-Path $OutputDirectory 'physical.json') -Raw | ConvertFrom-Json
    foreach ($record in $receipt.records) { if ([Math]::Abs($record.error) -gt 2e-9) { throw 'Conservation failed' } }
    if ($Mode -ne 'Static') {
        $perf = Get-Content (Join-Path $OutputDirectory 'performance.json') -Raw | ConvertFrom-Json
        if ($perf.fluidSeconds -lt $perf.wallSeconds - .5 -or $perf.backlog -gt .5) { throw 'Fluid did not keep pace with wall time' }
        if ($Mode -eq 'Benchmark' -and ($perf.p99 -gt 33.333 -or $perf.fps -lt 30)) { throw 'Frame-time target failed' }
    }
    if ($Mode -eq 'Video') {
        & python (Join-Path $PSScriptRoot 'check-fluid-video.py') $OutputDirectory
        if ($LASTEXITCODE -ne 0) { throw 'Native motion validation failed' }
    }
    if ($Mode -eq 'Motion') {
        if (@(Get-ChildItem $OutputDirectory -Filter 'motion-*.jpg').Count -lt 40) { throw 'Insufficient motion frames' }
        $ffmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue
        if ($ffmpeg) {
            & $ffmpeg.Source -hide_banner -loglevel error -framerate 8 -i (Join-Path $OutputDirectory 'motion-%04d.jpg') -c:v libx264 -pix_fmt yuv420p (Join-Path $OutputDirectory 'motion-8hz.mp4')
            if ($LASTEXITCODE -ne 0) { throw 'Motion encoding failed' }
        }
    }
    Write-Host "NUMERICAL AND RUNTIME CHECKS PASSED: $OutputDirectory"
    Write-Host 'Appearance and motion still require visual inspection.'
    if ($Mode -eq 'Video') { Write-Host 'Native video is 30 FPS and was validated against the owned viewport.' }
    elseif ($Mode -eq 'Motion') { Write-Host 'Viewport image samples are 8 Hz; use Video mode for native 30 FPS motion.' }
    else { Write-Host 'Benchmark mode excludes screenshot and video recording overhead.' }
}
finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
}
