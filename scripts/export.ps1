<#
.SYNOPSIS
  Reproducible release export (no editor clicking).
  Output: build/release/Vivarium/ (Windows bundle), build/release/Vivarium-Linux/ (Linux bundle),
          build/Vivarium-<version>-win64.zip, and build/Vivarium-<version>-linux64.zip
  Prerequisites: .NET 8 SDK, Godot 4.7.1 mono (tools/godot or $env:VIVARIUM_GODOT), export templates 4.7.1.stable.mono.
#>
param(
    [string]$Target = 'All',
    [int]$TimeoutSec = 900
)
. (Join-Path $PSScriptRoot 'common.ps1')
Set-Location $RepoRoot
$version = Get-AppVersion
$godot = Get-GodotExe
$game = Join-Path $RepoRoot 'game'

$templates = Join-Path $env:APPDATA 'Godot\export_templates\4.7.1.stable.mono'
if (-not (Test-Path (Join-Path $templates 'windows_release_x86_64.exe'))) { throw "Export templates missing: $templates\windows_release_x86_64.exe" }
if (-not (Test-Path (Join-Path $templates 'linux_release.x86_64'))) { throw "Export templates missing: $templates\linux_release.x86_64" }

# stamp the single version source into Godot metadata
$proj = Join-Path $game 'project.godot'
(Get-Content $proj -Raw) -replace 'config/version="[^"]*"', "config/version=`"$version`"" | Set-Content $proj -NoNewline
$four = ($version.Split('.') + @('0','0','0','0'))[0..3] -join '.'
$presets = Join-Path $game 'export_presets.cfg'
(Get-Content $presets -Raw) -replace 'application/file_version="[^"]*"', "application/file_version=`"$four`"" -replace 'application/product_version="[^"]*"', "application/product_version=`"$four`"" | Set-Content $presets -NoNewline

Write-Host "Building C# (Release)..." -ForegroundColor Cyan
dotnet build (Join-Path $game 'Vivarium.csproj') -c ExportRelease -nologo -v q | Out-Host
if ($LASTEXITCODE -ne 0) { dotnet build (Join-Path $game 'Vivarium.csproj') -c Release -nologo -v q | Out-Host }

$exportWindows = $Target -in @('All', 'Windows', 'Win64')
$exportLinux = $Target -in @('All', 'Linux', 'Linux64')

if ($exportWindows) {
    $dest = Join-Path $RepoRoot 'build\release\Vivarium'
    $running = Get-Process -Name Vivarium -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$dest\*" }
    if ($running) { throw "Close the running Vivarium (pid $($running.Id -join ', ')) first: it locks files in $dest" }
    if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }   # fail loudly if a locked file would leak into the bundle
    New-Item -ItemType Directory -Force $dest | Out-Null

    Write-Host "Exporting Windows Desktop $version..." -ForegroundColor Cyan
    $log = Join-Path $RepoRoot 'build\export-win'
    $r = Invoke-Timed $godot @('--headless','--path',$game,'--export-release','Windows Desktop',(Join-Path $dest 'Vivarium.exe')) $TimeoutSec $log
    $dataDir = Get-ChildItem $dest -Directory -ErrorAction SilentlyContinue | Where-Object Name -like 'data_*'
    $exportErrors = ($r.Out + "`n" + $r.Err) -split "`n" | Where-Object { $_ -match '^ERROR:' }
    if ($r.ExitCode -ne 0 -or -not (Test-Path (Join-Path $dest 'Vivarium.exe')) -or -not $dataDir -or $exportErrors) {
        $exportErrors | Select-Object -First 5 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        Write-Host ($r.Out + "`n" + $r.Err)
        throw "Godot Windows export failed (exit $($r.ExitCode))"
    }

    # notices + readme
    Copy-Item (Join-Path $RepoRoot 'THIRD_PARTY_NOTICES.md') $dest
    $lic = Invoke-Timed $godot @('--headless','--path',$game,'--','--licenses',(Join-Path $dest 'ENGINE_LICENSES.txt')) 120 (Join-Path $RepoRoot 'build\licenses')
    if (-not (Test-Path (Join-Path $dest 'ENGINE_LICENSES.txt'))) { throw 'license inventory generation failed' }
@"
Vivarium $version — Digital Desktop Vivarium
Run Vivarium.exe. No installation or network connection is needed.
Your vivarium, settings and logs are stored in %APPDATA%\Vivarium\.
Press F1 in the application for controls.
"@ | Set-Content (Join-Path $dest 'README.txt')

    Get-ChildItem $dest -Filter '*~RF*.TMP' -Force | Remove-Item -Force
    $allowed = @('Vivarium.exe','Vivarium.pck','ENGINE_LICENSES.txt','THIRD_PARTY_NOTICES.md','README.txt')
    $stray = Get-ChildItem $dest -File | Where-Object { $allowed -notcontains $_.Name }
    if ($stray) { throw "Unexpected files in the Windows bundle: $($stray.Name -join ', ')" }

    $zip = Join-Path $RepoRoot "build\Vivarium-$version-win64.zip"
    Remove-Item $zip -ErrorAction SilentlyContinue
    Compress-Archive -Path "$dest\*" -DestinationPath $zip
    $size = (Get-ChildItem $dest -Recurse | Measure-Object Length -Sum).Sum / 1MB
    Write-Host ("Exported Windows: {0} ({1:N0} MB) and {2}" -f $dest, $size, $zip) -ForegroundColor Green
}

if ($exportLinux) {
    $destLinux = Join-Path $RepoRoot 'build\release\Vivarium-Linux'
    if (Test-Path $destLinux) { Remove-Item -Recurse -Force $destLinux }
    New-Item -ItemType Directory -Force $destLinux | Out-Null

    Write-Host "Exporting Linux $version..." -ForegroundColor Cyan
    $logLinux = Join-Path $RepoRoot 'build\export-linux'
    $rLinux = Invoke-Timed $godot @('--headless','--path',$game,'--export-release','Linux',(Join-Path $destLinux 'Vivarium.x86_64')) $TimeoutSec $logLinux
    $dataDirLinux = Get-ChildItem $destLinux -Directory -ErrorAction SilentlyContinue | Where-Object Name -like 'data_*'
    $exportErrorsLinux = ($rLinux.Out + "`n" + $rLinux.Err) -split "`n" | Where-Object { $_ -match '^ERROR:' }
    if ($rLinux.ExitCode -ne 0 -or -not (Test-Path (Join-Path $destLinux 'Vivarium.x86_64')) -or -not $dataDirLinux -or $exportErrorsLinux) {
        $exportErrorsLinux | Select-Object -First 5 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        Write-Host ($rLinux.Out + "`n" + $rLinux.Err)
        throw "Godot Linux export failed (exit $($rLinux.ExitCode))"
    }

    Copy-Item (Join-Path $RepoRoot 'THIRD_PARTY_NOTICES.md') $destLinux
    $licFile = Join-Path (Join-Path $RepoRoot 'build\release\Vivarium') 'ENGINE_LICENSES.txt'
    if (Test-Path $licFile) {
        Copy-Item $licFile $destLinux
    } else {
        $null = Invoke-Timed $godot @('--headless','--path',$game,'--','--licenses',(Join-Path $destLinux 'ENGINE_LICENSES.txt')) 120 (Join-Path $RepoRoot 'build\licenses-linux')
    }
@"
Vivarium $version — Digital Desktop Vivarium (Linux x86_64)
Run Vivarium.x86_64 (e.g. chmod +x Vivarium.x86_64 && ./Vivarium.x86_64).
No installation or network connection is needed.
Your vivarium, settings and logs are stored in ~/.local/share/godot/app_userdata/Vivarium/ or ~/.config/Vivarium/.
Press F1 in the application for controls.
"@ | Set-Content (Join-Path $destLinux 'README.txt')

    $allowedLinux = @('Vivarium.x86_64','Vivarium.pck','ENGINE_LICENSES.txt','THIRD_PARTY_NOTICES.md','README.txt')
    $strayLinux = Get-ChildItem $destLinux -File | Where-Object { $allowedLinux -notcontains $_.Name }
    if ($strayLinux) { throw "Unexpected files in the Linux bundle: $($strayLinux.Name -join ', ')" }

    $zipLinux = Join-Path $RepoRoot "build\Vivarium-$version-linux64.zip"
    Remove-Item $zipLinux -ErrorAction SilentlyContinue
    Compress-Archive -Path "$destLinux\*" -DestinationPath $zipLinux
    $sizeLinux = (Get-ChildItem $destLinux -Recurse | Measure-Object Length -Sum).Sum / 1MB
    Write-Host ("Exported Linux: {0} ({1:N0} MB) and {2}" -f $destLinux, $sizeLinux, $zipLinux) -ForegroundColor Green
}

exit 0
