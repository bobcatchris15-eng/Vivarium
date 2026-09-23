<#
.SYNOPSIS
  Reproducible Windows release export (no editor clicking).
  Output: build/release/Vivarium/ (portable bundle) and build/Vivarium-<version>-win64.zip
  Prerequisites: .NET 8 SDK, Godot 4.7.1 mono (tools/godot or $env:VIVARIUM_GODOT), export templates 4.7.1.stable.mono.
#>
param([int]$TimeoutSec = 900)
. (Join-Path $PSScriptRoot 'common.ps1')
Set-Location $RepoRoot
$version = Get-AppVersion
$godot = Get-GodotExe
$game = Join-Path $RepoRoot 'game'

$templates = Join-Path $env:APPDATA 'Godot\export_templates\4.7.1.stable.mono'
if (-not (Test-Path (Join-Path $templates 'windows_release_x86_64.exe'))) { throw "Export templates missing: $templates" }

# stamp the single version source into Godot metadata
$proj = Join-Path $game 'project.godot'
(Get-Content $proj -Raw) -replace 'config/version="[^"]*"', "config/version=`"$version`"" | Set-Content $proj -NoNewline
$four = ($version.Split('.') + @('0','0','0','0'))[0..3] -join '.'
$presets = Join-Path $game 'export_presets.cfg'
(Get-Content $presets -Raw) -replace 'application/file_version="[^"]*"', "application/file_version=`"$four`"" -replace 'application/product_version="[^"]*"', "application/product_version=`"$four`"" | Set-Content $presets -NoNewline

$dest = Join-Path $RepoRoot 'build\release\Vivarium'
Remove-Item -Recurse -Force $dest -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dest | Out-Null

Write-Host "Building C# (Release)..." -ForegroundColor Cyan
dotnet build (Join-Path $game 'Vivarium.csproj') -c ExportRelease -nologo -v q | Out-Host
if ($LASTEXITCODE -ne 0) { dotnet build (Join-Path $game 'Vivarium.csproj') -c Release -nologo -v q | Out-Host }

Write-Host "Exporting Windows Desktop $version..." -ForegroundColor Cyan
$log = Join-Path $RepoRoot 'build\export'
$r = Invoke-Timed $godot @('--headless','--path',$game,'--export-release','Windows Desktop',(Join-Path $dest 'Vivarium.exe')) $TimeoutSec $log
$dataDir = Get-ChildItem $dest -Directory -ErrorAction SilentlyContinue | Where-Object Name -like 'data_*'
$exportErrors = ($r.Out + "`n" + $r.Err) -split "`n" | Where-Object { $_ -match '^ERROR:' }
if ($r.ExitCode -ne 0 -or -not (Test-Path (Join-Path $dest 'Vivarium.exe')) -or -not $dataDir -or $exportErrors) {
    $exportErrors | Select-Object -First 5 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host ($r.Out + "`n" + $r.Err)
    throw "Godot export failed (exit $($r.ExitCode))"
}

# notices + readme (engine inventory generated from the engine itself)
Copy-Item (Join-Path $RepoRoot 'THIRD_PARTY_NOTICES.md') $dest
$lic = Invoke-Timed $godot @('--headless','--path',$game,'--','--licenses',(Join-Path $dest 'ENGINE_LICENSES.txt')) 120 (Join-Path $RepoRoot 'build\licenses')
if (-not (Test-Path (Join-Path $dest 'ENGINE_LICENSES.txt'))) { throw 'license inventory generation failed' }
@"
Vivarium $version — Digital Desktop Vivarium
Run Vivarium.exe. No installation or network connection is needed.
Your vivarium, settings and logs are stored in %APPDATA%\Vivarium\.
Press F1 in the application for controls.
"@ | Set-Content (Join-Path $dest 'README.txt')

$zip = Join-Path $RepoRoot "build\Vivarium-$version-win64.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path "$dest\*" -DestinationPath $zip
$size = (Get-ChildItem $dest -Recurse | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Exported {0} ({1:N0} MB) and {2}" -f $dest, $size, $zip) -ForegroundColor Green
exit 0
