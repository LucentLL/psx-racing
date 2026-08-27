# After a green sandbox scene build, pull the BLUE RIDGE stage build outputs
# and their .meta files back into the source project — same two reasons as
# city-sync-back.ps1: /MIR deletes sandbox-generated .meta files on the next
# mirror (full reimport every run), and the stage bake GENERATES assets (the
# copied CC0 tree billboards, the composed atlas, the mottle/shoulder
# textures, the stage materials) whose GUIDs should be owned by the source
# project rather than churned per build.
#
#   powershell -ExecutionPolicy Bypass -File tools\brp-sync-back.ps1
$ErrorActionPreference = "Stop"
$src  = Split-Path -Parent $PSScriptRoot
$proj = "C:\Users\mcgee\PSXBuild"

$paths = @(
    "Assets\PSXRacing\Art\BRP",
    "Assets\PSXRacing\Art\BRP.meta",
    "Assets\PSXRacing\Resources\brp_stage.json.meta",
    "Assets\PSXRacing\Scripts\StageCulling.cs.meta",
    "Assets\PSXRacing\Editor\PSXRacingBuilder.Stage.cs.meta"
)

$copied = 0
foreach ($rel in $paths) {
    $from = Join-Path $proj $rel
    $to   = Join-Path $src  $rel
    if (-not (Test-Path $from)) { Write-Host "missing in sandbox: $rel"; continue }
    if (Test-Path $from -PathType Container) {
        robocopy $from $to /E /NFL /NDL /NJH /NJS /NP | Out-Null
        $copied++
    } else {
        New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null
        Copy-Item $from $to -Force
        $copied++
    }
}

# Stage materials are generated into the shared Materials dir; pick them up
# by the scene prefix. Generated MESHES deliberately stay sandbox-only, the
# same as every circuit's: they are build output, and the source scenes are
# stale by design (see the build-workflow notes).
$matDirFrom = Join-Path $proj "Assets\PSXRacing\Materials"
$matDirTo   = Join-Path $src  "Assets\PSXRacing\Materials"
if (Test-Path $matDirFrom) {
    Get-ChildItem $matDirFrom -Filter "BlueRidge*" | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $matDirTo $_.Name) -Force
        $copied++
    }
}

Write-Host "brp sync-back: $copied paths copied."
