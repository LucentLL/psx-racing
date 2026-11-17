# citymap-bake.ps1 - draw the whole-city map under the race cards (the track preview) into a Unity
# project: Assets/StreamingAssets/CityMap/<level>/<tx>_<ty>.png and Resources/charlotte_citymap.txt.
#
# The owner, 2026-11-16: "It should be a picture of the entire city, the track drawn over it, then
# zoomed in to fit the track on screen for track preview before race." The map is a BUILD PRODUCT
# (gitignored, about 19 MB): run this in the build sandbox after its sync and before the player
# build. A build without it shows every city venue's own saved picture (as PSX_TRACKMAP_CITY=0).
# Levels 0-4 cover the whole city; levels 5-8 are drawn only round the venues: every route in
# Resources/charlotte_routes.json and the parking decks in Resources/TrackMapFrames.txt, so a new
# route gets its sharp tiles from the next run.
#
#   powershell -ExecutionPolicy Bypass -File tools\citymap-bake.ps1 -Project C:\Users\mcgee\PSXLab
#
# Needs Node, Python 3 with PIL and numpy, and the OSM layers in the main checkout's
# tools\city\cache (landuse, railways, buildings, lots). About 2.5 minutes with 8 jobs.
param(
    [string]$Project = (Split-Path -Parent $PSScriptRoot),
    [string]$CityCache = $(if ($env:PSX_CITY_CACHE) { $env:PSX_CITY_CACHE } else { "C:\Users\mcgee\PSX Racing\tools\city\cache" }),
    [string]$Python = "C:\Users\mcgee\AppData\Local\Programs\Python\Python313\python.exe",
    [string]$Venues = "",
    [int]$Jobs = 8
)
$ErrorActionPreference = "Stop"
$tools = Join-Path $PSScriptRoot "city"
if (-not (Test-Path (Join-Path $Project "Assets\PSXRacing\Resources\charlotte_city.bytes"))) {
    Write-Host "citymap-bake: no Assets\PSXRacing\Resources\charlotte_city.bytes under $Project"; exit 1
}
if (-not (Test-Path (Join-Path $CityCache "layers\landuse.json"))) {
    Write-Host "citymap-bake: no OSM layers in $CityCache (set -CityCache or PSX_CITY_CACHE)"; exit 1
}
$layers = Join-Path $env:TEMP ("citymap_layers_" + [guid]::NewGuid().ToString("N") + ".json")
try {
    $env:PSX_CITY_CACHE = $CityCache
    $prep = @((Join-Path $tools "citymap_prep.mjs"), "--out", $layers, "--project", $Project)
    if ($Venues) { $prep += @("--venues", $Venues) }
    & node @prep
    if ($LASTEXITCODE -ne 0) { Write-Host "citymap-bake: the layer prep failed"; exit 1 }
    & $Python -I (Join-Path $tools "citymap_tiles.py") $layers --project $Project --levels 0-8 --jobs $Jobs
    if ($LASTEXITCODE -ne 0) { Write-Host "citymap-bake: the tile draw failed"; exit 1 }
    $dir = Join-Path $Project "Assets\StreamingAssets\CityMap"
    $n = (Get-ChildItem $dir -Recurse -Filter *.png).Count
    $mb = [math]::Round(((Get-ChildItem $dir -Recurse -Filter *.png | Measure-Object Length -Sum).Sum) / 1MB, 1)
    Write-Host "citymap-bake: $n tiles, $mb MB in $dir"
}
finally {
    Remove-Item $layers -Force -ErrorAction SilentlyContinue
}
exit 0
