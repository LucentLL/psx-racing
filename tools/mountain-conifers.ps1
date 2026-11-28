# The owner's mountain conifer models (Codex, 2026-11-27) into a checkout or
# sandbox. The delivery is the owner's own art (about 240 MB) and the repo is
# PUBLIC, so it is gitignored (Assets/MountainConifers/) like the store packs,
# and copied here from his art folder WITH its .meta files: every model,
# texture and material keeps the GUID the delivery gave it. The delivery
# itself is never written to.
#
# A missing delivery is a warning, not an error: the stage builder then draws
# every conifer as crossed cards, as before (PSX_CONIFER_MODELS has nothing
# to load).
#
#   powershell -ExecutionPolicy Bypass -File tools\mountain-conifers.ps1 [-Dest <project root>] [-Src <art folder>\Mountain_Conifers\Unity\MountainConifers]
#
# Robocopy /MIR keeps the copy in step with the delivery; /XO leaves a file
# the editor re-imported in the copy (a texture importer .meta the builder
# set to keep its cut-out coverage, newer than the delivery's) as it is.
param(
    [string]$Dest = (Split-Path -Parent $PSScriptRoot),
    [string]$Src = (Join-Path $env:USERPROFILE "OneDrive\Documents\Game Development\PSX Assets\PSX Racing\Mountain_Conifers\Unity\MountainConifers")
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath (Join-Path $Src "Models"))) {
    Write-Warning "Mountain conifers: no delivery at $Src (Models missing) - conifers stay crossed cards"
    exit 0
}
$assets = Join-Path $Dest "Assets"
if (-not (Test-Path -LiteralPath $assets)) { throw "not a Unity project: $Dest" }
$dst = Join-Path $assets "MountainConifers"

$null = robocopy $Src $dst /MIR /XO /R:2 /W:2 /NFL /NDL /NJH /NJS /NP
$rc = $LASTEXITCODE
if ($rc -ge 8) { throw "robocopy failed ($rc) copying $Src -> $dst" }

$fbx = @(Get-ChildItem -LiteralPath (Join-Path $dst "Models") -Filter *.fbx -File).Count
$tex = @(Get-ChildItem -LiteralPath (Join-Path $dst "Textures") -Filter *.png -File).Count
$meta = @(Get-ChildItem -LiteralPath $dst -Recurse -Filter *.meta -File).Count
"Mountain conifers: $fbx models, $tex textures, $meta .meta files in $dst (robocopy $rc)"
