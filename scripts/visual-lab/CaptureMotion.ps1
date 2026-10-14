param([Parameter(Mandatory)][string]$Experiment,[Parameter(Mandatory)][ValidateSet('before','after')][string]$Side,[int]$TimeoutSec=180)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if($Experiment -notmatch '^[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}$'){throw 'Safe experiment ID required'}
$exp=Join-Path $repo "build/visual-lab/experiments/$Experiment"
if((Test-Path (Join-Path $repo '.clanker/HALT')) -or (Test-Path (Join-Path $exp 'HALT'))){throw 'HALT active'}
$out=Join-Path $exp "motion/$Side"
if(Test-Path -LiteralPath $out){throw 'Motion evidence already exists'}
. (Join-Path $repo 'scripts/common.ps1')
$lease=$null;$prior=@{}
try{
 $lease=[IO.File]::Open((Join-Path $repo '.clanker/visual-lab-gpu.lease'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 if((Test-Path (Join-Path $repo '.clanker/HALT')) -or (Test-Path (Join-Path $exp 'HALT'))){throw 'HALT active'}
 # Compute the identical eligible source digest used by still captures before launching.
 $helper=Join-Path $PSScriptRoot 'visual_lab.py'
 $code='import importlib.util,hashlib,sys;from pathlib import Path;s=importlib.util.spec_from_file_location("lab",sys.argv[1]);m=importlib.util.module_from_spec(s);s.loader.exec_module(m);r=Path(sys.argv[2]);print(hashlib.sha256("\n".join(p+" "+m.digest(f).upper() for p,f in m.source_paths(r) if Path(p).parts[0] in ("game","src")).encode()).hexdigest())'
 $source=(& python -c $code $helper $repo).Trim()
 if($LASTEXITCODE -ne 0){throw 'Source digest failed'}
 $still=Get-Content (Join-Path $exp "captures/$Side/launch.json") -Raw|ConvertFrom-Json
 if($source -ne $still.source_tree_sha256){throw 'Motion source differs from corresponding still capture; reconcile first'}
 $busy=@(Get-Process|Where-Object{$_.ProcessName -match '^(Godot|Vivarium|blender)'})
 New-Item -ItemType Directory -Path $out|Out-Null
 foreach($name in @('VIVARIUM_LANDSCAPE_SCENARIO','VIVARIUM_BENCHMARK_USER_DIR')){$prior[$name]=[Environment]::GetEnvironmentVariable($name)}
 $env:VIVARIUM_LANDSCAPE_SCENARIO='frozen';$env:VIVARIUM_BENCHMARK_USER_DIR=Join-Path $out 'user'
 $exe=Get-GodotExe
 $run=Invoke-Timed $exe @('--path',(Join-Path $repo 'game'),'--log-file',(Join-Path $out 'engine.log'),'--','--material-port-review',$out,'--material-baseline',(Join-Path $exp 'baseline/source/game/Shaders'),'--geometry-only','--motion-sequence') $TimeoutSec (Join-Path $out 'capture')
 $frames=@(Get-ChildItem (Join-Path $out 'motion') -Filter 'frame-*.png' -ErrorAction SilentlyContinue)
 @{exit_code=$run.ExitCode;frames=$frames.Count;source_tree_sha256=$source;diagnostic=$true;competing_pids=@($busy.Id);runtime_fps_measured=$false;captured_utc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content (Join-Path $out 'launch.json') -Encoding utf8
 if($run.ExitCode -ne 0 -or $frames.Count -ne 48){throw 'Motion capture incomplete; evidence retained'}
 & (Join-Path $PSScriptRoot 'Toolchain.ps1') -Action EncodeMotion -InputPath (Join-Path $out 'motion/frame-%03d.png') -OutputPath (Join-Path $out 'motion/motion.mp4')
 if($LASTEXITCODE -ne 0){throw 'Motion encoding failed'}
 Write-Output "Matched-source motion evidence: $out"
}finally{
 foreach($name in $prior.Keys){[Environment]::SetEnvironmentVariable($name,$prior[$name])}
 if($lease){$lease.Dispose()}
}
