<#
    Adds starter-flora entries to a preset without reformatting the file.

    The presets are hand-formatted and several of them have been through a lot of editing, so
    round-tripping them through a JSON serialiser would churn the whole file. This inserts the new
    entries textually, matching the indentation already in use.

    Usage:  powershell -File scratch\preset-append.ps1 -Path game\content\presets\default.json -SpeciesList "hollowcrown:1,glasslily:2"
#>
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$SpeciesList
)

$ErrorActionPreference = 'Stop'
$Species = $SpeciesList -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }
$text = [System.IO.File]::ReadAllText($Path)

$idx = $text.IndexOf('"starterFlora"')
if ($idx -lt 0) { throw "no starterFlora block in $Path" }
$open = $text.IndexOf('[', $idx)
$close = $text.IndexOf(']', $open)
if ($open -lt 0 -or $close -lt 0) { throw "malformed starterFlora in $Path" }
$block = $text.Substring($open, $close - $open + 1)

# The indentation of an existing entry, so the new ones match, and the indent of the closing bracket.
$indent = '      '
$closeIndent = '  '
foreach ($line in $block -split "`r`n") {
    if ($line -match '^(\s+)"species"') { $indent = $Matches[1]; break }
}
foreach ($line in ($block -split "`r`n")) {
    if ($line -match '^(\s*)\]$') { $closeIndent = $Matches[1]; break }
}

$entries = @()
foreach ($spec in $Species) {
    $parts = $spec -split ':'
    $id = $parts[0].Trim()
    $count = if ($parts.Count -gt 1) { [int]$parts[1] } else { 2 }
    if ($block -match ('"species"\s*:\s*"' + [regex]::Escape($id) + '"')) {
        Write-Host "  $id already present in $Path, skipped"
        continue
    }
    $entries += "`r`n$indent{`r`n$indent`"species`": `"$id`",`r`n$indent`"count`": $count`r`n$indent}"
}

if ($entries.Count -eq 0) { Write-Host "nothing to add to $Path"; exit 0 }

# The last hand-written entry gains the comma that separates it from the first new one, and every
# new entry is separated from the next by a comma too.
$body = $block.Substring(0, $block.Length - 1).TrimEnd() + ','
for ($i = 0; $i -lt $entries.Count; $i++) {
    $body += $entries[$i]
    if ($i -lt $entries.Count - 1) { $body += ',' }
}
$body += "`r`n$closeIndent]"

$text = $text.Substring(0, $open) + $body + $text.Substring($close + 1)
[System.IO.File]::WriteAllText($Path, $text)
Write-Host "added $($entries.Count) species to $Path"
