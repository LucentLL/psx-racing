# THE BAKED CITY (Scripts/City/CityTileCache.cs, Editor/CityTileBake.cs): bake every
# Charlotte tile into <sandbox>\CityTiles when the bake is stale (city data, generator
# sources, format or generator switches changed), then optionally run the gate
# (Editor/HitchProbe.Bake.cs: every route tile stood up built and baked, compared whole).
#
#   tools\city-tiles.ps1 [-Force] [-Gate] [-MaxTiles 70] [-Me name]
#     PSX_SANDBOX picks the sandbox (default PSXCity); PSX_BUILD_TARGET its import
#     (a PC sandbox: StandaloneWindows64, so the job does not switch it to WebGL).
# The Win64 build (PSXBuildWin64) re-bakes by itself when stale and ships the bake in
# PSXRacing_Data\StreamingAssets\CityTiles; the player uses it on the PC profile
# (PSXRacing.exe -psx-tilecache-off: built live, for an A/B; -psx-tilecache-verify:
# every baked tile also built by the player and compared). Nothing baked is committed.
param([switch]$Force, [switch]$Gate, [int]$MaxTiles = 70, [string]$Me = "city-tiles", [int]$MaxMinutes = 120)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXCity" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
$lock = Join-Path $proj "AGENT_LOCK.txt"
while (Test-Path $lock) {
    $txt = (Get-Content $lock -Raw).Trim()
    $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
    if ($txt.StartsWith($Me) -or $age -gt 90) { break }
    "sandbox locked by '$txt' ($([int]$age) min) - waiting"
    Start-Sleep -Seconds 120
}
"$Me $((Get-Date).ToString('s'))" | Set-Content -Path $lock -Encoding ascii
try {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    Remove-Item "$proj\PSXRacing_citytiles.txt" -ErrorAction SilentlyContinue
    $extra = @(); if ($Force) { $extra = @("-psxTileForce") }
    Invoke-UnityJob -Log "$proj\citytiles.log" -MaxMinutes $MaxMinutes -UnityArgs (@(
        "-batchmode","-nographics","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.CityTileBake.BakeFromCommandLine",
        "-logFile","$proj\citytiles.log","-accept-apiupdate") + $extra) | Out-Null
    Select-String -Path "$proj\citytiles.log" -Pattern "error CS" | Select-Object -First 10 | ForEach-Object { $_.Line }
    if (Test-Path "$proj\PSXRacing_citytiles.txt") { Get-Content "$proj\PSXRacing_citytiles.txt" }
    else { "NO REPORT - the bake threw. Tail of citytiles.log:"; Get-Content "$proj\citytiles.log" -Tail 30; exit 1 }
    if ($Gate) {
        $env:PSX_HITCH_TAG = "tiles"; $env:PSX_HITCH_MODE = "bake"; $env:PSX_HITCH_MAXTILES = "$MaxTiles"
        $env:PSX_HITCH_ROUTES = "uptown,tryon,i77"
        Remove-Item "$proj\PSXRacing_hitch_tiles.txt" -ErrorAction SilentlyContinue
        Invoke-UnityJob -Log "$proj\citytiles_gate.log" -MaxMinutes $MaxMinutes -UnityArgs @(
            "-batchmode","-projectPath",$proj,"-executeMethod","PSXRacing.EditorTools.HitchProbe.Run",
            "-logFile","$proj\citytiles_gate.log","-accept-apiupdate") | Out-Null
        if (Test-Path "$proj\PSXRacing_hitch_tiles.txt") { Get-Content "$proj\PSXRacing_hitch_tiles.txt" }
        else { "NO GATE REPORT. Tail of citytiles_gate.log:"; Get-Content "$proj\citytiles_gate.log" -Tail 30; exit 1 }
    }
}
finally { Remove-Item $lock -ErrorAction SilentlyContinue }
