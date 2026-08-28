# After a green sandbox scene build, pull the BOGUE BANKS stage build outputs
# and their .meta files back into the source project — same two reasons as
# brp-sync-back.ps1: /MIR deletes sandbox-generated .meta files on the next
# mirror (full reimport every run), and the stage bake GENERATES assets (the
# sand/scrub/sea/shoulder textures and the stage materials) whose GUIDs should
# be owned by the source project rather than churned per build.
#
# The DEM, mask and stage JSON are NOT in that category — fetch_bogue.mjs
# writes them straight into the source project — but their .meta files are
# still minted in the sandbox on first import, so they come back too.
#
#   powershell -ExecutionPolicy Bypass -File tools\bogue-sync-back.ps1
$ErrorActionPreference = "Stop"
$src  = Split-Path -Parent $PSScriptRoot
$proj = "C:\Users\mcgee\PSXBuild"

$paths = @(
    "Assets\PSXRacing\Art\Bogue",
    "Assets\PSXRacing\Art\Bogue.meta",
    "Assets\PSXRacing\Resources\bogue_emerald.json.meta",
    "Assets\PSXRacing\Resources\bogue_langston.json.meta",
    "Assets\PSXRacing\Resources\bogue_atlantic.json.meta"
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

# Stage materials land in the shared Materials dir under each scene's prefix.
# Three venues, three prefixes — and the generated MESHES deliberately stay
# sandbox-only, the same as every circuit's.
$matDirFrom = Join-Path $proj "Assets\PSXRacing\Materials"
$matDirTo   = Join-Path $src  "Assets\PSXRacing\Materials"
if (Test-Path $matDirFrom) {
    foreach ($prefix in @("EmeraldIsle*", "LangstonBridge*", "AtlanticBeachBridge*")) {
        Get-ChildItem $matDirFrom -Filter $prefix | ForEach-Object {
            Copy-Item $_.FullName (Join-Path $matDirTo $_.Name) -Force
            $copied++
        }
    }
}

Write-Host "bogue sync-back: $copied paths copied."
