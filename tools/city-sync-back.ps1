# After a green sandbox scene build, pull the CITY build outputs and their
# .meta files back into the source project.
#
# Two reasons, both learned the hard way (see the build-workflow notes):
#   - /MIR deletes sandbox-generated .meta files on the next mirror, which
#     forces a full reimport every single run;
#   - the city bake GENERATES assets (drawn road textures, the facade copies,
#     the shopfront atlas, the menu thumbnail, the City materials). If they
#     live only in the sandbox they are rebuilt every run and their GUIDs
#     churn; the source project should own them like any other art.
#
#   powershell -ExecutionPolicy Bypass -File tools\city-sync-back.ps1
$ErrorActionPreference = "Stop"
$src  = Split-Path -Parent $PSScriptRoot
$proj = "C:\Users\mcgee\PSXBuild"

$paths = @(
    "Assets\PSXRacing\Art\City",
    "Assets\PSXRacing\Resources\charlotte_thumb.png",
    "Assets\PSXRacing\Resources\charlotte_thumb.png.meta",
    "Assets\PSXRacing\Resources\charlotte_city.json.meta",
    "Assets\PSXRacing\Art\City.meta",
    "Assets\PSXRacing\Scripts\City.meta",
    "Assets\PSXRacing\Scripts\City\CityMap.cs.meta",
    "Assets\PSXRacing\Scripts\City\CityElevation.cs.meta",
    "Assets\PSXRacing\Scripts\City\CityMeshes.cs.meta",
    "Assets\PSXRacing\Scripts\City\CityBuildings.cs.meta",
    "Assets\PSXRacing\Scripts\City\CityWorld.cs.meta",
    "Assets\PSXRacing\Scripts\City\CityMode.cs.meta",
    "Assets\PSXRacing\Scripts\DriveSession.cs.meta",
    "Assets\PSXRacing\Editor\PSXRacingBuilder.City.cs.meta",
    "Assets\PSXRacing\Editor\CityAudit.cs.meta",
    "Assets\PSXRacing\Editor\CityPreview.cs.meta"
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

# City materials are generated into the shared Materials dir; pick them up by name.
$matDirFrom = Join-Path $proj "Assets\PSXRacing\Materials"
$matDirTo   = Join-Path $src  "Assets\PSXRacing\Materials"
Get-ChildItem $matDirFrom -Filter "City*" | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $matDirTo $_.Name) -Force
    $copied++
}

Write-Host "city sync-back: $copied paths copied."
