param([switch]$Check)
. "$PSScriptRoot/common.ps1"
$diagnosticRoot = Join-Path $RepoRoot 'build/diagnostics'
New-Item -ItemType Directory -Force -Path $diagnosticRoot | Out-Null
dotnet build (Join-Path $RepoRoot 'game/Vivarium.csproj') --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Game build failed' }
$launchTag = Get-Date -Format 'yyyyMMdd-HHmmss'
$launchDir = Join-Path $diagnosticRoot "launch-$launchTag"
New-Item -ItemType Directory -Force -Path $launchDir | Out-Null
git -C $RepoRoot rev-parse HEAD | Set-Content (Join-Path $launchDir 'git-head.txt')
git -C $RepoRoot status --short | Set-Content (Join-Path $launchDir 'git-status.txt')
$godotArgs = @('--path', (Join-Path $RepoRoot 'game'), '--', '--diagnostics', $launchDir)
if ($Check) { $godotArgs += '--diagnostics-check' }
$quotedArgs = $godotArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
$diagnosticProcess = Start-Process -FilePath (Get-GodotExe) -WindowStyle Hidden -PassThru -ArgumentList $quotedArgs -RedirectStandardOutput (Join-Path $launchDir 'console.log') -RedirectStandardError (Join-Path $launchDir 'errors.log')
$launchDir | Set-Content (Join-Path $diagnosticRoot 'latest-run.txt')
Write-Host "Vivarium diagnostics launched (PID $($diagnosticProcess.Id))."
Write-Host "Results: $launchDir"
Write-Host 'Normal camera and simulation controls remain available. Ctrl+Shift+D stops/starts capture; Tab hides the HUD.'
