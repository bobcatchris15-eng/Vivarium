param(
    [string]$Executable,
    [string]$SourceRoot,
    [string]$WorldPath,
    [ValidateSet('frozen','live','understory','colony','traversal','sculpt','wet','density','stationary','repeat_traversal')] [string]$Scenario = 'frozen',
    [string]$Preset = 'default',
    [ValidateSet('initial','mature')] [string]$Preparation = 'mature',
    [ValidateRange(60,3600)] [int]$Seconds = 60,
    [ValidateRange(0,2)] [int]$Quality = 1,
    [string]$OutputRoot = 'build/living-landscape',
    [ValidateSet('cold','warm')] [string]$StartupKind = 'cold',
    [string]$Python = 'python',
    [switch]$ConfirmIsolation,
    [switch]$HitchTraceOnly,
    [switch]$DisableAccessibility,
    [string]$EngineVersion,
    [string]$SourceRevision,
    [string]$SourceTreeSha256,
    [double]$DisplayScaling = 1.0
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sourceRepo = if ($SourceRoot) { (Resolve-Path -LiteralPath $SourceRoot).Path } else { $repo }
if ($WorldPath) { $WorldPath = (Resolve-Path -LiteralPath $WorldPath).Path }
$gpuLease = [IO.File]::Open((Join-Path $repo '.clanker/visual-lab-gpu.lease'), 'OpenOrCreate', 'ReadWrite', 'None')
try {
if (-not $ConfirmIsolation) { throw 'ConfirmIsolation is required: close other GPU-intensive apps, Godot editors/previews, and competing benchmarks; no application is terminated automatically.' }
$busy = @(Get-Process | Where-Object { $_.ProcessName -match '^(Godot|Vivarium|blender)' })
if ($busy.Count) { throw "Competing rendering processes: $($busy.ProcessName -join ', ')" }
if (-not $Executable) { . (Join-Path $repo 'scripts/common.ps1'); $Executable = Get-GodotExe }
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$sourceLaunch = [IO.Path]::GetFileName($Executable) -match '^Godot'
if ($sourceLaunch) {
    & dotnet build (Join-Path $sourceRepo 'game/Vivarium.csproj') -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Source build failed; no capture started.' }
}
if ($sourceLaunch -and -not $EngineVersion) { $EngineVersion = (& $Executable --version | Select-Object -First 1).Trim() }
if (-not $EngineVersion) { throw 'Portable release requires exact EngineVersion (cannot infer it from application version).' }
if (-not $SourceRevision) { $SourceRevision = (& git -C $repo rev-parse HEAD).Trim() }
if (-not $SourceTreeSha256) {
    $files = @(& git -C $repo ls-files 'game' 'src') + @(& git -C $repo ls-files --others --exclude-standard 'game' 'src')
    $records = foreach ($file in ($files | Sort-Object -Unique)) { "$file $((Get-FileHash -LiteralPath (Join-Path $sourceRepo $file) -Algorithm SHA256).Hash)" }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($records -join "`n")); $sha = [Security.Cryptography.SHA256]::Create()
    $SourceTreeSha256 = [Convert]::ToHexString($sha.ComputeHash($bytes)).ToLowerInvariant()
}
$runId = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N')
if (-not [IO.Path]::IsPathRooted($OutputRoot)) { $OutputRoot = Join-Path $repo $OutputRoot }
$run = Join-Path $OutputRoot $runId
if (Test-Path -LiteralPath $run) { throw 'Run directory collision' }
New-Item -ItemType Directory -Path $run | Out-Null
$gpu = Get-CimInstance Win32_VideoController | Where-Object Name -match 'NVIDIA' | Select-Object -First 1
if (-not $gpu) { $gpu = Get-CimInstance Win32_VideoController | Select-Object -First 1 }
$provenance = [ordered]@{
    run_id=$runId; source_revision=$SourceRevision; source_tree_sha256=$SourceTreeSha256
    build_game_sha256=$(if ($sourceLaunch) { (Get-FileHash -LiteralPath (Join-Path $sourceRepo 'game/.godot/mono/temp/bin/Debug/Vivarium.dll') -Algorithm SHA256).Hash.ToLowerInvariant() } else { 'portable-runtime-attested' })
    source_root=$sourceRepo; loaded_world_sha256=$(if ($WorldPath) { (Get-FileHash -LiteralPath $WorldPath -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null })
    accessibility_mode=$(if ($DisableAccessibility) { "disabled" } else { "auto" })
    source_dirty=([bool](& git -C $repo status --porcelain)); engine_version=$EngineVersion
    executable_sha256=(Get-FileHash -LiteralPath $Executable -Algorithm SHA256).Hash.ToLowerInvariant()
    gpu=$gpu.Name; driver=$gpu.DriverVersion; cpu=(Get-CimInstance Win32_Processor | Select-Object -First 1).Name
    ram_bytes=(Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory; os=(Get-CimInstance Win32_OperatingSystem).Caption
    display_scaling=$DisplayScaling; isolation_confirmed=$true; started_utc=[DateTime]::UtcNow.ToString('o'); preset=$Preset; startup_kind=$StartupKind; hitch_trace_only=[bool]$HitchTraceOnly
}
$provenancePath = Join-Path $run 'provenance.json'
$provenance | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $provenancePath -Encoding UTF8
$names = 'VIVARIUM_LANDSCAPE_SCENARIO','VIVARIUM_PERF_SECONDS','VIVARIUM_RENDER_PROVENANCE','VIVARIUM_BENCHMARK_USER_DIR','VIVARIUM_LANDSCAPE_PREPARATION','VIVARIUM_HITCH_TRACE_ONLY','VIVARIUM_BENCHMARK_QUALITY'
$prior = @{}; foreach ($name in $names) { $prior[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    $env:VIVARIUM_LANDSCAPE_SCENARIO=$Scenario; $env:VIVARIUM_LANDSCAPE_PREPARATION=$Preparation; $env:VIVARIUM_PERF_SECONDS="$Seconds"; $env:VIVARIUM_RENDER_PROVENANCE=$provenancePath; $env:VIVARIUM_BENCHMARK_USER_DIR=(Join-Path $run 'user')
    $env:VIVARIUM_HITCH_TRACE_ONLY = if ($HitchTraceOnly) { '1' } else { $null }; $env:VIVARIUM_BENCHMARK_QUALITY = "$Quality"
    $arguments = @()
    if ($DisableAccessibility) { $arguments += @("--accessibility", "disabled") }
    if ($sourceLaunch) { $arguments += @('--path', (Join-Path $sourceRepo 'game')) }
    # Override user storage before Main initializes; never read or alter actual settings/saves/logs.
    $arguments += @('--log-file', (Join-Path $run 'engine.log'), '--', '--perf-test', $run, '--preset', $Preset)
    if ($WorldPath) { $arguments += @('--load', $WorldPath) }
    $quoted = $arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + $_.Replace('"','\"') + '"' } else { $_ } }
    $launch = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $Executable -ArgumentList $quoted -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $run 'stdout.log') -RedirectStandardError (Join-Path $run 'stderr.log')
    $timeout = ($Seconds + 600) * 1000
    while (-not $process.WaitForExit(1000)) {
        $captureProgressPath = Join-Path $run 'capture-progress.json'
        $stalledView = $null
        if (Test-Path -LiteralPath $captureProgressPath) {
            try {
                $captureProgress = Get-Content -LiteralPath $captureProgressPath -Raw | ConvertFrom-Json
                if ($captureProgress.status -eq 'started' -and ([DateTime]::UtcNow - ([DateTime]$captureProgress.started_utc).ToUniversalTime()).TotalSeconds -gt 45) { $stalledView = $captureProgress.scene }
            } catch { } # Writer may be replacing the small progress record.
        }
        if ($launch.ElapsedMilliseconds -gt $timeout -or $stalledView) {
            & taskkill /PID $process.Id /T /F | Out-Null
            if ($stalledView) { throw "Own benchmark capture stalled in view '$stalledView'; timing and per-view progress retained." }
            throw 'Own benchmark process timed out; logs retained.'
        }
    }
    $process.WaitForExit()
    @{exit_code=$process.ExitCode; wall_seconds=$launch.Elapsed.TotalSeconds; arguments=$arguments} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'launch.json') -Encoding UTF8
    if ($HitchTraceOnly) {
        $receipt = Get-Content -LiteralPath (Join-Path $run 'hitch_receipt.json') -Raw | ConvertFrom-Json
        if (-not $receipt.game_module_matches -or -not $receipt.sim_module_matches -or $receipt.duration_seconds -lt $Seconds) {
            throw 'Focused hitch trace receipt failed identity/duration checks.'
        }
        $validationExit = 0
        Write-Host 'Focused hitch trace complete; full landscape performance/capture gate not evaluated.'
    } else {
        & $Python (Join-Path $PSScriptRoot 'validate-report.py') (Join-Path $run 'render_report.json') --result (Join-Path $run 'validation.json')
        $validationExit = $LASTEXITCODE
    }
    Write-Host "Immutable run evidence: $run"
    if ($process.ExitCode -ne 0 -or $validationExit -ne 0) { exit 1 }
} finally { foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $prior[$name]) } }
} finally { $gpuLease.Dispose() }
