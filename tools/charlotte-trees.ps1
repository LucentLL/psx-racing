# The owner's Charlotte tree models into a
# checkout or sandbox. The repo is PUBLIC and the delivery is the owner's own
# art (about 100 MB), so it is gitignored (Assets/CharlotteTrees/) like the
# store packs, and copied here from his art folder WITH its .meta files: every
# model, texture and material keeps the GUID the delivery gave it, so a kit
# built from it survives a re-copy. The delivery itself is never written to.
#
# A missing delivery is a warning, not an error: the city tree kit builder
# (PSXRacingBuilder.CityTreeKit4) then builds round 3's kit exactly.
#
#   powershell -ExecutionPolicy Bypass -File tools\charlotte-trees.ps1 [-Dest <project root>] [-Src <art folder>\Charlotte_Trees\Unity\CharlotteTrees]
#
# Robocopy /MIR keeps the copy in step with the delivery (a model the owner
# takes out goes), /XO leaves a file the editor re-imported in the copy (a
# texture's importer .meta, newer than the delivery's) as it is, and the kit
# builder's own folder (Gen: the distance-card sheets it renders) is never
# purged.
param(
    [string]$Dest = (Split-Path -Parent $PSScriptRoot),
    [string]$Src
)

$ErrorActionPreference = "Stop"
if (-not $PSBoundParameters.ContainsKey('Src')) {
    $art = Join-Path $env:USERPROFILE "OneDrive\Documents\Game Development\PSX Assets\PSX Racing\Charlotte_Trees"
    $Src = Join-Path $art 'Pass2\Unity\CharlotteTrees'
    if (-not (Test-Path -LiteralPath (Join-Path $Src 'Models'))) {
        $Src = Join-Path $art 'Unity\CharlotteTrees'
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $Src "Models"))) {
    Write-Warning "Charlotte trees: no delivery at $Src (Models missing) - the kit builds round 3's trees"
    exit 0
}
$assets = Join-Path $Dest "Assets"
if (-not (Test-Path -LiteralPath $assets)) { throw "not a Unity project: $Dest" }
$dst = Join-Path $assets "CharlotteTrees"

# the folder's own .meta (its GUID) beside it
$folderMeta = Join-Path (Split-Path -Parent $Src) "CharlotteTrees.meta"
if (Test-Path -LiteralPath $folderMeta) { Copy-Item -LiteralPath $folderMeta -Destination (Join-Path $assets "CharlotteTrees.meta") -Force }

$null = robocopy $Src $dst /MIR /XO /XD Gen /R:2 /W:2 /NFL /NDL /NJH /NJS /NP
$rc = $LASTEXITCODE
if ($rc -ge 8) { throw "robocopy failed ($rc) copying $Src -> $dst" }

$fbx = @(Get-ChildItem -LiteralPath (Join-Path $dst "Models") -Filter *.FBX -File).Count
$tex = @(Get-ChildItem -LiteralPath (Join-Path $dst "Textures") -Filter *.png -File).Count
$meta = @(Get-ChildItem -LiteralPath $dst -Recurse -Filter *.meta -File).Count
"Charlotte trees: $fbx models, $tex textures, $meta .meta files in $dst (robocopy $rc)"
