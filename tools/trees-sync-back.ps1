# Bring back what a sandbox scene build made for the trees pass:
#
#   the five forest atlases - the builder recomposes them (every billboard slid
#   until its painted trunk stands under the crossing of its two cards) and
#   rewrites the PNGs IN PLACE in the sandbox. The source must carry the same
#   bytes, or the next mirror puts the old, off-centre atlases back and the
#   build after that rewrites them again;
#
#   the .meta files the sandbox minted for scripts this pass added - a script
#   without its .meta gets a new GUID on every mirror.
#
#   powershell -ExecutionPolicy Bypass -File tools\trees-sync-back.ps1
$ErrorActionPreference = "Stop"
$src  = Split-Path -Parent $PSScriptRoot
$proj = "C:\Users\mcgee\PSXBuild"

$copied = 0
$gen = "Assets\PSXRacing\Art\BRP\Gen"
Get-ChildItem (Join-Path $proj $gen) -Filter "TreeAtlas*" | ForEach-Object {
    Copy-Item $_.FullName (Join-Path (Join-Path $src $gen) $_.Name) -Force
    $copied++
}

foreach ($rel in @(
    "Assets\PSXRacing\Editor\TreePlayCheck.cs.meta",
    "Assets\PSXRacing\Editor\PaintGlowCheck.cs.meta",
    "Assets\PSXRacing\Editor\ForestShots.cs.meta"
)) {
    $from = Join-Path $proj $rel
    $to   = Join-Path $src  $rel
    if (-not (Test-Path $from)) { Write-Host "missing in sandbox: $rel"; continue }
    if (-not (Test-Path $to)) { Copy-Item $from $to; $copied++ }
}

Write-Host "trees sync-back: $copied files copied."
