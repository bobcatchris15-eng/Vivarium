# Shared helpers for verify.ps1 / export.ps1 / smoke-release.ps1
$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot

function Get-GodotExe {
    if ($env:VIVARIUM_GODOT -and (Test-Path $env:VIVARIUM_GODOT)) { return $env:VIVARIUM_GODOT }
    $local = Join-Path $RepoRoot 'tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe'
    if (Test-Path $local) { return $local }
    throw "Godot 4.7.1 mono not found. Unpack it to tools\godot\ or set `$env:VIVARIUM_GODOT (see docs/architecture/adr-001-stack.md)."
}

function Get-AppVersion {
    $src = Get-Content (Join-Path $RepoRoot 'src\Vivarium.Sim\Core\AppVersion.cs') -Raw
    if ($src -notmatch 'Application\s*=\s*"([^"]+)"') { throw 'AppVersion.Application not found' }
    return $Matches[1]
}

# Runs an executable with a hard timeout; returns @{ ExitCode; Out; Err }. Never hangs the caller.
function Invoke-Timed([string]$Exe, [string[]]$Arguments, [int]$TimeoutSec, [string]$LogPrefix) {
    $out = "$LogPrefix.out.txt"; $err = "$LogPrefix.err.txt"
    New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null
    $quoted = $Arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $p = Start-Process -FilePath $Exe -ArgumentList $quoted -NoNewWindow -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
    if (-not $p.WaitForExit($TimeoutSec * 1000)) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        return @{ ExitCode = 124; Out = (Get-Content $out -Raw -ErrorAction SilentlyContinue); Err = "TIMEOUT after $TimeoutSec s" }
    }
    $p.WaitForExit()
    return @{ ExitCode = $p.ExitCode; Out = (Get-Content $out -Raw -ErrorAction SilentlyContinue); Err = (Get-Content $err -Raw -ErrorAction SilentlyContinue) }
}
