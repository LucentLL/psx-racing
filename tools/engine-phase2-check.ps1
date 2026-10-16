# Engine audio phase 2 in a sandbox (default PSXCity): sync the code, the
# default family (Resources/Engines) and the bundled ones (EngineSrc), then
#   1. build the WebGL engine bundles into StreamingAssets/engines (sizes,
#      manifest) - PSXRacing_engine_bundles_webgl.txt;
#   2. build the same bundles for the editor's platform into Temp (a WebGL
#      bundle's AAC audio does not play in the Windows editor) and delete one
#      of them (the unfetchable-family case);
#   3. run EnginePhase2PlayCheck in play mode over those bundles.
# Exit 0 = every check passed.
#
#   powershell -ExecutionPolicy Bypass -File tools/engine-phase2-check.ps1 [-SkipBundles] [-NoWatch]
#
param([switch]$NoWatch, [switch]$SkipBundles, [switch]$SkipWebGL, [int]$MaxMinutes = 60)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXCity" }
$env:PSX_SANDBOX = $proj
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Resources\Engines",
                 "Assets\PSXRacing\EngineSrc", "Assets\PSXRacing\Resources\Crackle")) {
    if (Test-Path "$src\$d") { robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null }
}
foreach ($f in @("EngineSrc.meta", "Resources\res2_rig.json", "Resources\EngineBundles.txt", "Resources\EngineBundles.txt.meta", "Resources\Crackle.meta")) {
    if (Test-Path "$src\Assets\PSXRacing\$f") { Copy-Item "$src\Assets\PSXRacing\$f" "$proj\Assets\PSXRacing\$f" -Force }
}

# NOT under Temp: Unity clears Temp when the next editor opens.
$edOut = "$proj\EngineBundlesEditor"
if (-not $SkipBundles -and -not $SkipWebGL) {
    Remove-Item "$proj\PSXRacing_engine_bundles.txt" -ErrorAction SilentlyContinue
    $env:PSX_ENGINE_TARGET = ""
    $env:PSX_ENGINE_OUT = ""
    Invoke-UnityJob -Log "$proj\enginebundles.log" -MaxMinutes $MaxMinutes -UnityArgs @(
        "-quit","-batchmode","-nographics","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.EngineBundleBuilder.BuildFromCommandLine",
        "-logFile","$proj\enginebundles.log","-accept-apiupdate") | Out-Null
    if (-not (Test-Path "$proj\PSXRacing_engine_bundles.txt")) {
        "NO WEBGL BUNDLE REPORT. Tail of enginebundles.log:"; Get-Content "$proj\enginebundles.log" -Tail 40; exit 1
    }
    Copy-Item "$proj\PSXRacing_engine_bundles.txt" "$proj\PSXRacing_engine_bundles_webgl.txt" -Force
    Get-Content "$proj\PSXRacing_engine_bundles_webgl.txt"
}
if (-not $SkipBundles) {
    Remove-Item "$proj\PSXRacing_engine_bundles.txt" -ErrorAction SilentlyContinue
    $env:PSX_ENGINE_TARGET = "editor"
    $env:PSX_ENGINE_OUT = $edOut
    Invoke-UnityJob -Log "$proj\enginebundles_ed.log" -MaxMinutes $MaxMinutes -UnityArgs @(
        "-quit","-batchmode","-nographics","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.EngineBundleBuilder.BuildFromCommandLine",
        "-logFile","$proj\enginebundles_ed.log","-accept-apiupdate") | Out-Null
    $env:PSX_ENGINE_TARGET = ""
    $env:PSX_ENGINE_OUT = ""
    if (-not (Test-Path "$proj\PSXRacing_engine_bundles.txt")) {
        "NO EDITOR BUNDLE REPORT. Tail of enginebundles_ed.log:"; Get-Content "$proj\enginebundles_ed.log" -Tail 40; exit 1
    }
    Get-Content "$proj\PSXRacing_engine_bundles.txt" -TotalCount 1
}
Remove-Item "$edOut\v12_british.bundle" -ErrorAction SilentlyContinue

Remove-Item "$proj\PSXRacing_engine_phase2_check.txt" -ErrorAction SilentlyContinue
$env:PSX_ENGINE_URL = "file:///" + ($edOut -replace '\\', '/')
Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\enginephase2.log" -MaxMinutes $MaxMinutes -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.EnginePhase2PlayCheck.Run",
    "-logFile","$proj\enginephase2.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\enginephase2.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_engine_phase2_check.txt") {
    Get-Content "$proj\PSXRacing_engine_phase2_check.txt"
    if (Select-String -Path "$proj\PSXRacing_engine_phase2_check.txt" -Pattern "FAIL" -CaseSensitive -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of enginephase2.log:"
Get-Content "$proj\enginephase2.log" -Tail 40
exit 1
