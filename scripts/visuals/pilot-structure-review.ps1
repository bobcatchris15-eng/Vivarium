param([Parameter(Mandatory)][string]$Name)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if ($Name -notmatch '^[a-z0-9-]+$') { throw 'Use a unique lowercase run name.' }
if (Test-Path (Join-Path $repo '.clanker/HALT')) { throw 'HALT present.' }
$run = Join-Path $repo "build/pilot-structure/$Name"
if (Test-Path $run) { throw 'Preserve existing evidence.' }
New-Item -ItemType Directory $run | Out-Null
$lease = [IO.File]::Open((Join-Path $repo '.clanker/visual-lab-gpu.lease'), 'OpenOrCreate', 'ReadWrite', 'None')
$priorUser = $env:VIVARIUM_BENCHMARK_USER_DIR
$priorDof = $env:VIVARIUM_DISABLE_DOF
function Manifest {
    $paths = @('src/Vivarium.Sim/Geometry/PilotTreeMeshes.cs','src/Vivarium.Sim/World/PilotTree.cs','game/Render/PilotTreeRenderer.cs','game/App/PilotTreeOverhaulReview.cs')
    $paths += Get-ChildItem (Join-Path $repo 'game/Shaders') -File | ForEach-Object { $_.FullName }
    $paths += Get-ChildItem (Join-Path $repo 'game/.godot/mono/temp/bin/Debug') -Filter 'Vivarium*.dll' | ForEach-Object { $_.FullName }
    @($paths | ForEach-Object { $p = if ([IO.Path]::IsPathRooted($_)) { $_ } else { Join-Path $repo $_ }; @{path=$p;hash=(Get-FileHash $p).Hash} }) | ConvertTo-Json -Compress
}
try {
    $env:VIVARIUM_DISABLE_DOF = "1"
    $before = Manifest
    $before | Set-Content (Join-Path $run 'source-before.json')
    $env:VIVARIUM_BENCHMARK_USER_DIR = Join-Path $run 'user'
    . (Join-Path $repo 'scripts/common.ps1')
    $args = @('--path',(Join-Path $repo 'game'),'--log-file',(Join-Path $run 'engine.log'),'--','--pilot-overhaul-review',(Join-Path $run 'captures'),'--pilot-structure-only')
    $quoted = $args | ForEach-Object { if ($_ -match "\s") { '"' + $_ + '"' } else { $_ } }
    $p = Start-Process (Get-GodotExe) -ArgumentList $quoted -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run 'stdout.log') -RedirectStandardError (Join-Path $run 'stderr.log')
    $null = $p.Handle
    if (!$p.WaitForExit(600000)) { Stop-Process -Id $p.Id; throw 'Owned review timed out.' }
    $p.WaitForExit()
    $after = Manifest
    $after | Set-Content (Join-Path $run 'source-after.json')
    $errors = @(Select-String -Path (Join-Path $run 'stderr.log'),(Join-Path $run 'stdout.log') -Pattern 'SHADER ERROR:|SCRIPT ERROR:|ERROR:|Exception|VK_ERROR')
    $result = @{exit_code=$p.ExitCode;source_stable=($before -ceq $after);errors=$errors.Count;complete=(Test-Path (Join-Path $run 'captures/metadata.json'));performance_acceptance=$false}
    $result | ConvertTo-Json | Set-Content (Join-Path $run 'run.json')
    if ($p.ExitCode -ne 0 -or !$result.source_stable -or $errors.Count -gt 0 -or !$result.complete) { throw ($result | ConvertTo-Json -Compress) }
    $result | ConvertTo-Json -Compress
} finally { $env:VIVARIUM_BENCHMARK_USER_DIR = $priorUser; $env:VIVARIUM_DISABLE_DOF = $priorDof; $lease.Dispose() }
