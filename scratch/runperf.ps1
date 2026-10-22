$ErrorActionPreference = 'Continue'
$env:VIVARIUM_PERF_SECONDS = '30'
$exe = 'E:\Vivarium\tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe'
$out = 'E:\Vivarium\build\perf-overhaul\probe1'
$args = @('--path','E:\Vivarium\game','--log-file',"$out\engine.log",'--','--perf-test',$out)
Write-Host "Launching: $exe $($args -join ' ')"
$proc = Start-Process -FilePath $exe -ArgumentList $args -PassThru -RedirectStandardOutput "$out\stdout.log" -RedirectStandardError "$out\stderr.log"
$timeoutMs = 120000
if (-not $proc.WaitForExit($timeoutMs)) { Stop-Process -Id $proc.Id -Force; Write-Host 'TIMED OUT' } else { $proc.WaitForExit(); Write-Host "EXIT: $($proc.ExitCode)" }