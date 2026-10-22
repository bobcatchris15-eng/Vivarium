<#
    Re-pins the default-preset warm-up digest in ReferenceWarmupTimingTests.

    The test is a canary for the aquatic advection fix, and its own comment says to re-pin it
    when content or the simulation legitimately moves the digest. This runs a throwaway probe to
    measure the current digest and writes it into the test, so the value is never transcribed by
    hand.
#>
param(
    [string]$TestFile = 'E:\Vivarium\tests\Vivarium.Sim.Tests\ReferenceWarmupTimingTests.cs',
    [string]$Trx = 'E:\Vivarium\tests\Vivarium.Sim.Tests\TestResults\pin.trx'
)

$ErrorActionPreference = 'Stop'
Remove-Item $Trx -ErrorAction SilentlyContinue
Push-Location 'E:\Vivarium'
dotnet test tests\Vivarium.Sim.Tests\Vivarium.Sim.Tests.csproj --filter 'FullyQualifiedName~TempDigestProbe' `
    --logger "trx;LogFileName=pin.trx" -v q --nologo --disable-build-servers | Out-Null
Pop-Location

$x = [xml](Get-Content $Trx -Raw)
$out = $x.TestRun.Results.UnitTestResult | ForEach-Object { $_.Output.StdOut } | Select-Object -First 1
if ($out -notmatch 'DIGEST ([0-9a-f]+)') { throw "probe did not print a digest" }
$digest = $Matches[1]
if ($digest.Length -ne 64) { throw "digest is $($digest.Length) chars, expected 64: $digest" }

$text = [System.IO.File]::ReadAllText($TestFile)
$pattern = 'Assert\.Equal\("[0-9a-f]{16,80}", WorldSerializer\.Digest\(w\)\);'
if (-not [regex]::IsMatch($text, $pattern)) { throw "the digest literal was not found in $TestFile" }
$new = [regex]::Replace($text, $pattern, "Assert.Equal(`"$digest`", WorldSerializer.Digest(w));")
if ($new -eq $text) { Write-Host "the digest was already $digest - nothing to change"; exit 0 }
[System.IO.File]::WriteAllText($TestFile, $new)
Write-Host "re-pinned warm-up digest: $digest"
