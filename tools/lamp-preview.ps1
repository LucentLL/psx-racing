# Render every car shell with its head and tail lamps lit (front, rear, and
# side-on along each lamp line) and log where CarLampFinder seated them.
# Scripts+Editor only; renders, so NOT -nographics.
#
#   powershell -ExecutionPolicy Bypass -File tools\lamp-preview.ps1 [-Bake]
#
# -Bake re-bakes the shells first (CarModelBaker, which now measures the lamps
# into each prefab); without it the preview measures them in memory.
param([switch]$Bake)
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
if ($Bake) {
    Invoke-UnityJob -Log "$proj\lampbake.log" -MaxMinutes 20 -UnityArgs @(
        "-quit","-batchmode","-nographics","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.CarModelBaker.BakeMenu",
        "-logFile","$proj\lampbake.log","-accept-apiupdate") | Out-Null
    Select-String -Path "$proj\lampbake.log" -Pattern "error CS|Exception| head " | Select-Object -First 40 | ForEach-Object { $_.Line }
}
$out = "$proj\Screenshots\Lamps"
Remove-Item "$out\*.png" -ErrorAction SilentlyContinue
Invoke-UnityJob -Log "$proj\lampshot.log" -MaxMinutes 20 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CarLampPreview.Capture",
    "-logFile","$proj\lampshot.log","-accept-apiupdate") | Out-Null
Select-String -Path "$proj\lampshot.log" -Pattern "error CS|Exception| head | baked " | Select-Object -First 40 | ForEach-Object { $_.Line }
"{0} images in {1}" -f (Get-ChildItem "$out\*.png" -ErrorAction SilentlyContinue).Count, $out
