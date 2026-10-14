param(
    [string]$Preset = 'shoreline',
    [UInt64]$Seed = 804584441,
    [string]$OutputDirectory = '',
    [switch]$SkipBuild
)
. "$PSScriptRoot/common.ps1"
if (!$OutputDirectory) { $OutputDirectory = Join-Path $RepoRoot ('build/soaks/' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
if (!$SkipBuild) {
    dotnet build (Join-Path $RepoRoot 'game/Vivarium.csproj') -v:q
    if ($LASTEXITCODE) { throw 'Build failed; soak was not launched.' }
}
$arguments = @('--path', (Join-Path $RepoRoot 'game'), '--resolution', '2548x968', '--', '--soak', $OutputDirectory, '--preset', $Preset, '--seed', "$Seed", '--loading-preview', (Join-Path $OutputDirectory 'preview'))
$quoted = $arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
$process = Start-Process -FilePath (Get-GodotExe) -ArgumentList $quoted -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $OutputDirectory 'stdout.log') -RedirectStandardError (Join-Path $OutputDirectory 'stderr.log')
[pscustomobject]@{ LauncherProcessId = $process.Id; OutputDirectory = $OutputDirectory; Preset = $Preset; Seed = "$Seed" } | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'process.json') -Encoding utf8
Write-Output "Seeded soak launched: $OutputDirectory"
Write-Output 'The island runs at 1x indefinitely; Ctrl+Shift+D stops recording, closing the game ends the run.'
Write-Output 'Its initial save, five-minute autosaves, diagnostics and preview captures are in that directory.'
