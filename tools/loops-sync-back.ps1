# After a green sandbox scene build, pull the PARKWAY LOOP stage build outputs
# (Blowing Rock, Little Switzerland) and the 2026-09-11 pass's new source
# files' .meta files back into the source project -- same reasons as
# brp-sync-back.ps1: /MIR deletes sandbox-generated .meta files on the next
# mirror (full reimport every run), and GUIDs of generated assets should be
# owned by the source project rather than churned per build.
#
# The loops SHARE the Parkway's generated art (Theme.artShareDir -> Art/BRP),
# so there is no Art/BlowingRock/Gen to carry: only the DEM grids, the stage
# json metas and the stage materials.
#
#   powershell -ExecutionPolicy Bypass -File tools\loops-sync-back.ps1
$ErrorActionPreference = "Stop"
$src  = Split-Path -Parent $PSScriptRoot
$proj = "C:\Users\mcgee\PSXBuild"

$paths = @(
    "Assets\PSXRacing\Art\BlowingRock",
    "Assets\PSXRacing\Art\BlowingRock.meta",
    "Assets\PSXRacing\Art\Switzerland",
    "Assets\PSXRacing\Art\Switzerland.meta",
    "Assets\PSXRacing\Resources\brock_stage.json.meta",
    "Assets\PSXRacing\Resources\swiss_stage.json.meta",
    "Assets\PSXRacing\Scripts\Replay.meta",
    "Assets\PSXRacing\Scripts\Replay\RaceReplay.cs.meta",
    "Assets\PSXRacing\Scripts\Replay\ReplayCamera.cs.meta",
    "Assets\PSXRacing\Scripts\CarPaint.cs.meta",
    "Assets\PSXRacing\Shaders\PSXCarPaint.shader.meta",
    "Assets\PSXRacing\Editor\ReplayCheck.cs.meta"
)

$copied = 0
foreach ($rel in $paths) {
    $from = Join-Path $proj $rel
    $to   = Join-Path $src  $rel
    if (-not (Test-Path $from)) { Write-Host "missing in sandbox: $rel"; continue }
    if (Test-Path $from -PathType Container) {
        # Only the .meta files for a directory: the DEM grids and stage json
        # are the SOURCE's (a re-bake there must not be clobbered by the
        # sandbox's older copy), and the metas are all the sandbox owns.
        robocopy $from $to *.meta /E /NFL /NDL /NJH /NJS /NP | Out-Null
        $copied++
    } else {
        New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null
        Copy-Item $from $to -Force
        $copied++
    }
}

# Stage materials are generated into the shared Materials dir; pick them up
# by the scene prefix. Generated MESHES stay sandbox-only, as for every
# circuit. The car-model materials are tracked too and were rebaked onto
# PSX/CarPaint this pass, so they come back as well: they reference the
# shader by the GUID in PSXCarPaint.shader.meta, which the list above owns.
$matDirFrom = Join-Path $proj "Assets\PSXRacing\Materials"
$matDirTo   = Join-Path $src  "Assets\PSXRacing\Materials"
if (Test-Path $matDirFrom) {
    foreach ($prefix in @("BlowingRock*", "LittleSwitzerland*")) {
        Get-ChildItem $matDirFrom -Filter $prefix | ForEach-Object {
            Copy-Item $_.FullName (Join-Path $matDirTo $_.Name) -Force
            $copied++
        }
    }
    $carFrom = Join-Path $matDirFrom "CarModels"
    $carTo   = Join-Path $matDirTo   "CarModels"
    if ((Test-Path $carFrom) -and (Test-Path $carTo)) {
        robocopy $carFrom $carTo /E /NFL /NDL /NJH /NJS /NP | Out-Null
        $copied++
    }
}

Write-Host "loops sync-back: $copied paths copied."
