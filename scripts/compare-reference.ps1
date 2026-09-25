<#
.SYNOPSIS
  Before/after comparison of two reference captures (game --reference DIR).
.DESCRIPTION
  Writes <Out>/index.html: per-scene side-by-side images plus a metric delta table (fps, p95/worst frame,
  draw calls, primitives). Scenes are matched by name; scenes present in only one run are listed.
.EXAMPLE
  ./scripts/compare-reference.ps1 -Before build/reference/baseline-v0.1.2 -After build/reference/latest
#>
param(
    [Parameter(Mandatory)] [string]$Before,
    [Parameter(Mandatory)] [string]$After,
    [string]$Out = "build/reference/compare",
    [string]$Filter = "*"
)
$ErrorActionPreference = "Stop"
$Before = (Resolve-Path $Before).Path
$After = (Resolve-Path $After).Path
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path

function Load($dir) {
    $r = Get-Content (Join-Path $dir "reference_report.json") -Raw | ConvertFrom-Json
    $map = [ordered]@{}
    foreach ($s in $r.facts.scenes) { $map[$s.name] = $s }
    return @{ Report = $r; Scenes = $map }
}
$b = Load $Before; $a = Load $After

function Uri($p) { ([System.Uri]$p).AbsoluteUri }
function Delta($x, $y, [bool]$higherBetter) {
    if ($null -eq $x -or $null -eq $y -or $x -eq 0) { return "" }
    $pct = ($y - $x) / $x * 100
    $good = if ($higherBetter) { $pct -ge 0 } else { $pct -le 0 }
    $cls = if ([math]::Abs($pct) -lt 5) { "flat" } elseif ($good) { "good" } else { "bad" }
    return "<span class='$cls'>{0:+0;-0}%</span>" -f $pct
}

$names = @($b.Scenes.Keys) + @($a.Scenes.Keys) | Select-Object -Unique | Where-Object { $_ -like $Filter }
$rows = New-Object System.Text.StringBuilder
$cards = New-Object System.Text.StringBuilder
$console = @()
foreach ($n in $names) {
    $x = $b.Scenes[$n]; $y = $a.Scenes[$n]
    if ($x -and $y) {
        [void]$rows.Append("<tr><td><a href='#$n'>$n</a></td><td>$($x.fps_mean) → $($y.fps_mean) $(Delta $x.fps_mean $y.fps_mean $true)</td><td>$($x.frame_ms_p95) → $($y.frame_ms_p95) $(Delta $x.frame_ms_p95 $y.frame_ms_p95 $false)</td><td>$($x.frame_ms_worst) → $($y.frame_ms_worst)</td><td>$($x.draw_calls) → $($y.draw_calls) $(Delta $x.draw_calls $y.draw_calls $false)</td><td>$($x.primitives) → $($y.primitives) $(Delta $x.primitives $y.primitives $false)</td></tr>")
        $console += [pscustomobject]@{ Scene = $n; FpsBefore = $x.fps_mean; FpsAfter = $y.fps_mean; P95Before = $x.frame_ms_p95; P95After = $y.frame_ms_p95; PrimsBefore = $x.primitives; PrimsAfter = $y.primitives }
    } else {
        $which = if ($x) { "before only" } else { "after only" }
        [void]$rows.Append("<tr><td>$n</td><td colspan='5'><i>$which</i></td></tr>")
    }
    $bi = Join-Path $Before "$n.png"; $ai = Join-Path $After "$n.png"
    $bimg = if (Test-Path $bi) { "<img src='$(Uri $bi)' loading='lazy'>" } else { "<div class='none'>missing</div>" }
    $aimg = if (Test-Path $ai) { "<img src='$(Uri $ai)' loading='lazy'>" } else { "<div class='none'>missing</div>" }
    [void]$cards.Append("<section id='$n'><h2>$n</h2><div class='pair'><figure>$bimg<figcaption>before</figcaption></figure><figure>$aimg<figcaption>after</figcaption></figure></div></section>")
}

$html = @"
<!doctype html><html><head><meta charset='utf-8'><title>Reference compare</title><style>
body{font:14px system-ui;background:#16181b;color:#ddd;margin:16px}a{color:#9cf}
table{border-collapse:collapse;margin-bottom:24px}td,th{padding:3px 10px;border-bottom:1px solid #333;text-align:left;white-space:nowrap}
.good{color:#6d6}.bad{color:#f66}.flat{color:#888}
.pair{display:grid;grid-template-columns:1fr 1fr;gap:8px}img{width:100%;display:block}figure{margin:0}figcaption{color:#888}
.none{padding:40px;background:#222;color:#666;text-align:center}h2{font-size:16px;margin:24px 0 6px}
</style></head><body>
<h1>Reference compare</h1>
<p>Before: $Before ($($b.Report.version), $($b.Report.facts.renderer))<br>After: $After ($($a.Report.version), $($a.Report.facts.renderer))</p>
<table><tr><th>scene</th><th>fps</th><th>p95 ms</th><th>worst ms</th><th>draws</th><th>primitives</th></tr>$rows</table>
$cards
</body></html>
"@
Set-Content -Path (Join-Path $Out "index.html") -Value $html -Encoding UTF8
$console | Format-Table -AutoSize | Out-String | Write-Host
Write-Host "Wrote $(Join-Path $Out 'index.html')"
