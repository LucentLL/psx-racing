# After a green sandbox scene build, pull the PIZZA CARGO bake and the
# materials it wears back into the source project.
#
# Why this exists (2026-09-12, "pizzas are pink when carrying and driving"):
# the cargo prefabs in Resources/PizzaCargo were committed on Aug 30 pointing at
# scenery_Pizza* materials whose .mat files were never committed - only their
# .meta files were. Every full /MIR of the sandbox therefore orphaned those
# metas, Unity deleted them, and the next scene build minted fresh GUIDs for the
# regenerated materials and re-baked the prefabs against them. Then any additive
# copy of Resources put the source's Aug 30 prefabs back over the bake, pointing
# at GUIDs that no longer existed, and the player drew every box with the error
# shader. Committing the bake AND its materials makes those GUIDs stable: a
# mirror now carries each .mat together with its .meta, and
# PSXRacingBuilder.PSXMaterialFor reuses a material file that already exists.
#
#   powershell -ExecutionPolicy Bypass -File tools\cargo-sync-back.ps1
$ErrorActionPreference = "Stop"
$src  = Split-Path -Parent $PSScriptRoot
$proj = "C:\Users\mcgee\PSXBuild"

$cargoRel = "Assets\PSXRacing\Resources\PizzaCargo"
$from = Join-Path $proj $cargoRel
$to   = Join-Path $src  $cargoRel
if (-not (Test-Path $from)) { Write-Host "no cargo bake in the sandbox - run the scene build first" -ForegroundColor Red; exit 1 }

# The bake, whole: every prefab and every .meta. (The bottles' meshes and sheet
# are ART now - Art/LifeSim/Groceries, committed like any pack - so the only
# bottle files in here are the four prefabs.)
robocopy $from $to /E /NFL /NDL /NJH /NJS /NP | Out-Null
$copied = (Get-ChildItem $from -File).Count

# The materials those prefabs reference, found by GUID in the sandbox.
$guidToPath = @{}
Get-ChildItem (Join-Path $proj "Assets\PSXRacing") -Recurse -Filter *.meta | ForEach-Object {
    $line = Select-String -Path $_.FullName -Pattern '^guid: ([0-9a-f]{32})' | Select-Object -First 1
    if ($line) { $guidToPath[$line.Matches[0].Groups[1].Value] = $_.FullName.Substring(0, $_.FullName.Length - 5) }
}
$wanted = @{}
Get-ChildItem $from -Filter *.prefab | ForEach-Object {
    Select-String -Path $_.FullName -Pattern 'guid: ([0-9a-f]{32})' -AllMatches | ForEach-Object {
        foreach ($m in $_.Matches) { $wanted[$m.Groups[1].Value] = $true }
    }
}
$mats = 0; $missing = @()
foreach ($g in $wanted.Keys) {
    if (-not $guidToPath.ContainsKey($g)) { continue }        # a package or built-in asset
    $asset = $guidToPath[$g]
    if ($asset -notlike "*\Assets\PSXRacing\Materials\*") { continue }
    $rel = $asset.Substring($proj.Length + 1)
    $dest = Join-Path $src $rel
    Copy-Item $asset $dest -Force
    Copy-Item ($asset + ".meta") ($dest + ".meta") -Force
    $mats++
}
# Anything the prefabs point at must now exist in the source too.
$srcGuids = @{}
Get-ChildItem (Join-Path $src "Assets") -Recurse -Filter *.meta | ForEach-Object {
    $line = Select-String -Path $_.FullName -Pattern '^guid: ([0-9a-f]{32})' | Select-Object -First 1
    if ($line -and (Test-Path ($_.FullName.Substring(0, $_.FullName.Length - 5)))) { $srcGuids[$line.Matches[0].Groups[1].Value] = $true }
}
foreach ($g in $wanted.Keys) {
    if ($guidToPath.ContainsKey($g) -and -not $srcGuids.ContainsKey($g)) { $missing += ($g + " -> " + $guidToPath[$g]) }
}
Write-Host "cargo sync-back: $copied bake files, $mats materials copied."
if ($missing.Count -gt 0) {
    Write-Host "STILL MISSING IN SOURCE:" -ForegroundColor Red
    $missing | ForEach-Object { "  $_" }
    exit 1
}
Write-Host "every GUID the cargo prefabs reference now exists in the source project."
