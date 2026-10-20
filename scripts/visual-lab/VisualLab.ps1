param(
 [Parameter(Mandatory)][ValidateSet('initialize','baseline','status','inventory','capture','blind','compare','decision','resume','halt','unhalt','authored','critiqued','reuse-before')][string]$Command,
 [string]$Experiment, [string]$ParentExperiment, [string]$Repo, [string]$Python='python',
 [ValidateSet('before','after')][string]$Side,
 [ValidateSet('accept','reject','revise')][string]$Decision,
 [string]$AuthorIdentity, [string]$Executable,
 [ValidateSet('reference','material-port-review','world-review','fern-asset-review')][string]$Mode='reference',
 [ValidateSet('frozen','live','understory','colony','traversal','sculpt','wet','density')][string]$Scenario='frozen',
 [int]$TimeoutSec=600, [switch]$ConfirmIsolation, [switch]$Diagnostic,
 [switch]$GeometryOnly, [string]$FloraModule,
 [ValidateSet('procedural','native')][string]$FernVariant='procedural', [string]$FernAsset
)
$ErrorActionPreference='Stop'
if (!$Repo) {
 $cursor=$PSScriptRoot
 while ($cursor -and !(Test-Path -LiteralPath (Join-Path $cursor 'scripts/verify.ps1'))) { $cursor=Split-Path $cursor -Parent }
 if (!$cursor) { throw 'Repository root not found; provide -Repo.' }
 $Repo=$cursor
}
$Repo=(Resolve-Path -LiteralPath $Repo).Path
$helper=Join-Path $PSScriptRoot 'visual_lab.py'
function Run-Lab([string]$Action) {
 $arguments=@($helper,$Action,'--repo',$Repo)
 if ($Experiment) { $arguments+=@('--experiment',$Experiment) }
 if ($ParentExperiment) { $arguments+=@('--parent-experiment',$ParentExperiment) }
 if ($Side) { $arguments+=@('--side',$Side) }
 if ($Decision) { $arguments+=@('--decision',$Decision) }
 if ($AuthorIdentity) { $arguments+=@('--author-identity',$AuthorIdentity) }
 $value=& $Python @arguments
 if ($LASTEXITCODE -ne 0) { throw ($value -join "`n") }
 return ($value -join "`n")
}
if ($Command -ne 'capture') { Run-Lab $Command; return }
if (!$ConfirmIsolation -and !$Diagnostic) { throw 'Capture requires -ConfirmIsolation or explicitly labelled -Diagnostic.' }
if (!$Side) { throw 'Capture requires -Side before or after.' }
$plan=Run-Lab 'capture-plan' | ConvertFrom-Json
$leaseDir=Join-Path $Repo '.clanker'
New-Item -ItemType Directory -Force -Path $leaseDir | Out-Null
# Check reparse attributes before opening the exclusive shared GPU lease.
foreach ($path in @($Repo,$leaseDir,(Join-Path $leaseDir 'visual-lab-gpu.lease'))) {
 if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Reparse path forbidden' }
}
$lease=$null; $prior=@{}
try {
 $lease=[IO.File]::Open((Join-Path $leaseDir 'visual-lab-gpu.lease'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 # Recheck HALT and paths after acquiring lease.
 $plan=Run-Lab 'capture-plan' | ConvertFrom-Json
 $busy=@(Get-Process | Where-Object { $_.ProcessName -match '^(Godot|Vivarium|blender)' })
 if ($busy.Count -and !$Diagnostic) { throw 'Competing rendering process present; no process terminated.' }
 . (Join-Path $Repo 'scripts/common.ps1')
 if (!$Executable) { $Executable=Get-GodotExe }
 # A source digest does not authenticate a stale C# assembly. Build the exact
 # source being captured, then fail if source/assets changed during compilation.
 & dotnet build (Join-Path $Repo 'game/Vivarium.csproj') --no-restore -v minimal
 if ($LASTEXITCODE -ne 0) { throw 'Game compilation failed before capture.' }
 $compiledPlan=Run-Lab 'capture-plan' | ConvertFrom-Json
 if ($compiledPlan.source_tree_sha256 -ne $plan.source_tree_sha256) { throw 'Source changed during compilation; reconcile before capture.' }
 New-Item -ItemType Directory -Path $plan.capture_dir -Force | Out-Null
 foreach ($name in @('VIVARIUM_LANDSCAPE_SCENARIO','VIVARIUM_BENCHMARK_USER_DIR')) { $prior[$name]=[Environment]::GetEnvironmentVariable($name) }
 $env:VIVARIUM_LANDSCAPE_SCENARIO=$Scenario
 $env:VIVARIUM_BENCHMARK_USER_DIR=Join-Path $plan.capture_dir 'user'
 $renderArgs=@('--path',(Join-Path $Repo 'game'),'--log-file',(Join-Path $plan.capture_dir 'engine.log'),'--',("--"+$Mode),$plan.capture_dir)
 if ($Mode -eq 'material-port-review') {
  $renderArgs+=@('--material-baseline',(Join-Path $Repo "build/visual-lab/experiments/$Experiment/baseline/source/game/Shaders"))
  if ($GeometryOnly) { $renderArgs+='--geometry-only' }
  if ($FloraModule) {
   $FloraModule=(Resolve-Path -LiteralPath $FloraModule).Path
   $assetRoot=[IO.Path]::GetFullPath((Join-Path $Repo 'game'))+[IO.Path]::DirectorySeparatorChar
   if (!$FloraModule.StartsWith($assetRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Review asset must be inside authenticated game source/assets.' }
   $renderArgs+=@('--flora-module',$FloraModule)
  }
 }
 if ($Mode -eq 'fern-asset-review') {
  if (!$FernAsset) { throw 'Fern review requires an authenticated game asset.' }
  $FernAsset=(Resolve-Path -LiteralPath $FernAsset).Path
  $assetRoot=[IO.Path]::GetFullPath((Join-Path $Repo 'game'))+[IO.Path]::DirectorySeparatorChar
  if (!$FernAsset.StartsWith($assetRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Fern asset must be inside authenticated game source/assets.' }
  $resource='res://'+$FernAsset.Substring($assetRoot.Length).Replace('\','/')
  $renderArgs+=@('--fern-asset',$resource,'--fern-variant',$FernVariant)
  if ($Side -eq 'after') {
   $reference=Join-Path $Repo "build/visual-lab/experiments/$Experiment/captures/before/fern_review.json"
   if (!(Test-Path -LiteralPath $reference)) { throw 'Matched fern cameras require the before receipt.' }
   $renderArgs+=@('--fern-camera-reference',$reference)
  }
 }
 $run=Invoke-Timed $Executable $renderArgs $TimeoutSec (Join-Path $plan.capture_dir 'capture')
 $images=@(Get-ChildItem -LiteralPath $plan.capture_dir -Filter '*.png' -Recurse)
 $actual=($run.ExitCode -eq 0 -and $images.Count -gt 0 -and ($run.Out+$run.Err) -notmatch 'SHADER ERROR|SCRIPT ERROR|Failed to load')
 @{exit_code=$run.ExitCode; actual_render=$actual; mode=$Mode; scenario=$Scenario; flora_module=$FloraModule; flora_module_sha256=$(if($FloraModule){(Get-FileHash -LiteralPath $FloraModule -Algorithm SHA256).Hash}else{$null}); diagnostic=[bool]$Diagnostic; isolation_confirmed=(!$Diagnostic -and $busy.Count -eq 0); competing_pids=@($busy.Id); source_tree_sha256=$plan.source_tree_sha256; baseline_manifest_sha256=$plan.baseline_manifest_sha256; executable=$Executable; captured_utc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $plan.capture_dir 'launch.json') -Encoding utf8
 if ($Mode -eq 'fern-asset-review') {
  @{variant=$FernVariant; asset=$FernAsset; asset_sha256=(Get-FileHash -LiteralPath $FernAsset -Algorithm SHA256).Hash; arguments=$renderArgs} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $plan.capture_dir 'fern-review-launch.json') -Encoding utf8
 }
 if (!$actual) { throw 'Capture failed; evidence retained.' }
 Run-Lab 'captured'
} finally {
 foreach ($name in $prior.Keys) { [Environment]::SetEnvironmentVariable($name,$prior[$name]) }
 if ($lease) { $lease.Dispose() }
}
