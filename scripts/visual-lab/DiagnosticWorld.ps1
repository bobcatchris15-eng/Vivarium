param(
 [ValidateSet('frozen','understory','colony','wet','traversal')][string]$Scenario='frozen',
 [ValidateSet('initial','mature')][string]$Preparation='initial',
 [string]$Preset='default', [string]$OutputPath, [int]$TimeoutSec=300
)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if(Test-Path (Join-Path $repo '.clanker/HALT')){throw 'HALT active'}
if(!$OutputPath){$OutputPath=Join-Path $repo ('build/visual-lab/world-diagnostics/'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+$Scenario)}
if(Test-Path -LiteralPath $OutputPath){throw 'Immutable diagnostic output already exists'}
. (Join-Path $repo 'scripts/common.ps1')
$exe=Get-GodotExe
$lease=$null; $prior=@{}
try{
 $lease=[IO.File]::Open((Join-Path $repo '.clanker/visual-lab-gpu.lease'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 if(Test-Path (Join-Path $repo '.clanker/HALT')){throw 'HALT active'}
 $busy=@(Get-Process|Where-Object{$_.ProcessName -match '^(Godot|Vivarium|blender)'})
 New-Item -ItemType Directory -Path $OutputPath|Out-Null
 $files=@(& git -C $repo ls-files 'game' 'src')+@(& git -C $repo ls-files --others --exclude-standard 'game' 'src')
 $records=foreach($file in ($files|Sort-Object -Unique)){"$file $((Get-FileHash -LiteralPath (Join-Path $repo $file) -Algorithm SHA256).Hash)"}
 $sha=[Security.Cryptography.SHA256]::Create()
 $tree=[Convert]::ToHexString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($records -join "`n")))).ToLowerInvariant()
 $gpu=Get-CimInstance Win32_VideoController|Where-Object Name -match NVIDIA|Select-Object -First 1
 $provenance=[ordered]@{run_id=(Split-Path $OutputPath -Leaf);source_revision=(& git -C $repo rev-parse HEAD).Trim();source_tree_sha256=$tree;source_dirty=$true;build_game_sha256=(Get-FileHash (Join-Path $repo 'game/.godot/mono/temp/bin/Debug/Vivarium.dll')).Hash.ToLowerInvariant();engine_version=(& $exe --version|Select-Object -First 1).Trim();executable_sha256=(Get-FileHash $exe).Hash.ToLowerInvariant();gpu=$gpu.Name;driver=$gpu.DriverVersion;cpu=(Get-CimInstance Win32_Processor|Select-Object -First 1).Name;ram_bytes=(Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory;os=(Get-CimInstance Win32_OperatingSystem).Caption;display_scaling=1.0;isolation_confirmed=$false;diagnostic=$true;competing_pids=@($busy.Id);started_utc=[DateTime]::UtcNow.ToString('o');preset=$Preset;startup_kind='cold';accessibility_mode='auto'}
 $provenancePath=Join-Path $OutputPath 'provenance.json'
 $provenance|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $provenancePath -Encoding utf8
 $names=@('VIVARIUM_LANDSCAPE_SCENARIO','VIVARIUM_LANDSCAPE_PREPARATION','VIVARIUM_PERF_SECONDS','VIVARIUM_RENDER_PROVENANCE','VIVARIUM_BENCHMARK_USER_DIR')
 foreach($name in $names){$prior[$name]=[Environment]::GetEnvironmentVariable($name)}
 $env:VIVARIUM_LANDSCAPE_SCENARIO=$Scenario
 $env:VIVARIUM_LANDSCAPE_PREPARATION=$Preparation
 $env:VIVARIUM_PERF_SECONDS='60'
 $env:VIVARIUM_RENDER_PROVENANCE=$provenancePath
 $env:VIVARIUM_BENCHMARK_USER_DIR=Join-Path $OutputPath 'user'
 $run=Invoke-Timed $exe @('--path',(Join-Path $repo 'game'),'--log-file',(Join-Path $OutputPath 'engine.log'),'--','--perf-test',$OutputPath,'--preset',$Preset) $TimeoutSec (Join-Path $OutputPath 'capture')
 @{exit_code=$run.ExitCode;diagnostic=$true;performance_acceptance=$false;source_tree_sha256=$tree;captured_utc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content (Join-Path $OutputPath 'launch.json') -Encoding utf8
 Write-Output "Diagnostic world evidence: $OutputPath"
 if($run.ExitCode -ne 0){throw "Own diagnostic run exited $($run.ExitCode); evidence retained"}
}finally{
 foreach($name in $prior.Keys){[Environment]::SetEnvironmentVariable($name,$prior[$name])}
 if($lease){$lease.Dispose()}
}
