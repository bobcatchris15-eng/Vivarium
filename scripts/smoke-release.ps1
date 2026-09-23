<#
.SYNOPSIS
  Release smoke against the exported build (not the editor): boot test, UI smoke (new world, camera,
  simulation, every tool, save, quit), then reload + continued simulation. Also audits network use:
  the process must open no TCP connections, and the build output must not reference networking APIs.
  Default is headless; pass -Windowed to run with the real renderer (also produces screenshots).
#>
param([string]$Bundle = '', [switch]$Windowed, [int]$TimeoutSec = 600)
. (Join-Path $PSScriptRoot 'common.ps1')
if (-not $Bundle) { $Bundle = Join-Path $RepoRoot 'build\release\Vivarium' }
$exe = Join-Path $Bundle 'Vivarium.console.exe'
if (-not (Test-Path $exe)) { $exe = Join-Path $Bundle 'Vivarium.exe' }
if (-not (Test-Path $exe)) { throw "No exported build at $Bundle (run scripts/export.ps1)" }
$out = Join-Path $RepoRoot 'build\release-smoke'
Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out | Out-Null
$mode = if ($Windowed) { @() } else { @('--headless') }
$fail = @()

# 1. static offline audit: no networking types referenced by our assemblies
$dataDir = Get-ChildItem $Bundle -Directory | Where-Object Name -like 'data_*' | Select-Object -First 1
foreach ($dll in 'Vivarium.dll', 'Vivarium.Sim.dll') {
    $path = Get-ChildItem -Path $dataDir.FullName -Recurse -Filter $dll | Select-Object -First 1
    if (-not $path) { $fail += "missing $dll in bundle"; continue }
    $text = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($path.FullName))
    foreach ($api in 'HttpClient', 'WebClient', 'TcpClient', 'System.Net.Sockets', 'HttpRequest', 'StreamPeerTcp', 'WebSocket') {
        if ($text.Contains($api)) { $fail += "$dll references $api" }
    }
}

function Run-Watched([string[]]$UserArgs, [string]$Name) {
    $args = $mode + @('--') + $UserArgs
    $stdout = "$out\$Name.out.txt"; $stderr = "$out\$Name.err.txt"
    $p = Start-Process -FilePath $exe -ArgumentList $args -NoNewWindow -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $connections = 0
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while (-not $p.HasExited -and (Get-Date) -lt $deadline) {
        try { $connections += @(Get-NetTCPConnection -OwningProcess $p.Id -ErrorAction SilentlyContinue).Count } catch { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; return @{ Code = 124; Net = $connections } }
    $p.WaitForExit()
    return @{ Code = $p.ExitCode; Net = $connections }
}

$boot = Run-Watched @('--boot-test') 'boot'
if ($boot.Code -ne 0) { $fail += "boot test exit $($boot.Code)" }
$smoke = Run-Watched @('--smoke', $out) 'smoke'
if ($smoke.Code -ne 0) { $fail += "smoke exit $($smoke.Code)" }
$reload = if ($smoke.Code -eq 0) { Run-Watched @('--smoke-reload', $out) 'reload' } else { @{ Code = -1; Net = 0 } }
if ($reload.Code -ne 0) { $fail += "reload exit $($reload.Code)" }
$net = $boot.Net + $smoke.Net + $reload.Net
if ($net -gt 0) { $fail += "process opened $net TCP connection sample(s)" }

$report = [ordered]@{
    bundle = $Bundle; exe = $exe; windowed = [bool]$Windowed
    boot = $boot.Code; smoke = $smoke.Code; reload = $reload.Code; tcpConnectionsObserved = $net
    failures = $fail
}
$report | ConvertTo-Json | Set-Content "$out\release_smoke.json"
if ($fail.Count -gt 0) { Write-Host "Release smoke FAILED: $($fail -join '; ')" -ForegroundColor Red; exit 1 }
Write-Host "Release smoke passed (boot, UI smoke, reload; no network use). Reports in $out" -ForegroundColor Green
exit 0
