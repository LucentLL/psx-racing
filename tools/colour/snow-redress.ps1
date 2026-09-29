# THE SNOW DRESS'S GROUNDS, WITHOUT A SCENE BUILD (the colour pass, C8).
#
#   $env:PSX_SANDBOX='C:\Users\mcgee\PSXRec'
#   powershell -ExecutionPolicy Bypass -File tools\colour\snow-redress.ps1
#
# The builder now gives a SNOW ground the snow turf (PSXRacingBuilder.Season.cs,
# RegisterSeasonalGround) where it used to tint the grass or dirt x(1.8,1.8,1.9).
# A sandbox whose scenes were built before that keeps the old materials until
# its next scene build; this rewrites them in place (same assets, same GUIDs:
# the scenes need nothing) and lists them in the log. The tracked ones then go
# back to source by hand (git ls-files Assets/PSXRacing/Materials/*_Snow.mat).
param([int]$MaxMinutes = 20)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. "$src\tools\unity-wait.ps1"
foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
$target = if ($env:PSX_BUILD_TARGET) { $env:PSX_BUILD_TARGET } else { "WebGL" }
Remove-Item "$proj\PSXRacing_snow_redress.txt" -ErrorAction SilentlyContinue
$log = "$proj\snowredress.log"
Invoke-UnityJob -Log $log -MaxMinutes $MaxMinutes -UnityArgs @(
    "-batchmode","-nographics","-projectPath",$proj,"-buildTarget",$target,
    "-executeMethod","PSXRacing.EditorTools.PSXRacingBuilder.RedressSnowGroundsFromCommandLine",
    "-logFile",$log,"-accept-apiupdate") | Out-Null
Select-String -Path $log -Pattern "error CS|RedressSnowGrounds" | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_snow_redress.txt") { Get-Content "$proj\PSXRacing_snow_redress.txt"; exit 0 }
"NO REPORT. Tail of the log:"
Get-Content $log -Tail 30
exit 1
