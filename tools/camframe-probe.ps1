# Measure the chase camera's FRAMING (PSX Racing/Camera Framing Probe,
# Editor\CamFrameProbe.cs) on an already-built sandbox: EVERY body shell in the
# library on a straight of a built venue, CHASE and CLOSE, at 0 / 100 / 200
# km/h, at 16:9 and at a 19.5:9 phone with the touch HUD, projected
# analytically and checked against the NFS U / MW reference bands (their own
# +-2 at 16:9, their phone rule on a phone; the replaced targets are named in
# the report's header). PNGs are drawn for eight of the shells. Scripts and
# Editor are copied over first (no mirror, no scene build), so a camera change
# is a few minutes. Graphics ON: the frames render through the pipeline.
#
#   powershell -ExecutionPolicy Bypass -File tools\camframe-probe.ps1
#   ... -Rig legacy          the rig every scene carried on 2026-09-28
#   ... -Venue DragQuarter   another built venue
#   ... -NoPng               numbers only
#   ... -Cars a,b            only these shells (keys, comma-separated)
#   ... -AllScreens          also 18:9 and 20:9 phones and a 16:9 touch tablet
#                            (reported, not part of the verdict)
#   ... -Emit                write the chase rig's per-shell silhouette table
#                            from the meshes to Screenshots\CamFrame\
#                            ChaseSilhouettes.Table.cs (copy it over
#                            Assets\PSXRacing\Scripts\ChaseSilhouettes.Table.cs
#                            after a shell is re-baked or added)
#
# Output: <sandbox>\Screenshots\CamFrame\camframe_<rig>.txt / .csv, one PNG
# per case (cf_<rig>_<view>_<screen>_<car>_<kmh>.png) and a contact sheet per
# view and screen (cf_sheet_<rig>_<view>_<screen>.png). Last line of the log:
# "[CamFrame] ALL IN TARGET" or "[CamFrame] FAIL n".
param(
    [string]$Rig = "game",
    [string]$Venue = "GillespieGap",
    [switch]$NoPng,
    [switch]$AllScreens,
    [switch]$Emit,
    [string]$Cars = ""
)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
$out = "$proj\Screenshots\CamFrame"
if (Test-Path $out) {
    Remove-Item "$out\cf_${Rig}_*.png" -ErrorAction SilentlyContinue
    Remove-Item "$out\cf_sheet_${Rig}_*.png" -ErrorAction SilentlyContinue
    Remove-Item "$out\camframe_$Rig.*" -ErrorAction SilentlyContinue
}

$env:PSX_CAMFRAME_RIG = $Rig
$env:PSX_CAMFRAME_VENUE = $Venue
$env:PSX_CAMFRAME_PNG = if ($NoPng) { "0" } else { "1" }
$env:PSX_CAMFRAME_SCREENS = if ($AllScreens) { "all" } else { "" }
$env:PSX_CAMFRAME_EMIT = if ($Emit) { "1" } else { "" }
$env:PSX_CAMFRAME_CARS = $Cars
$log = "$proj\camframe_$Rig.log"
Invoke-UnityJob -Log $log -MaxMinutes 20 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CamFrameProbe.Run",
    "-logFile",$log,"-accept-apiupdate") | Out-Null

if (-not (Test-Path $log)) { Write-Host "NO LOG - the job never ran"; exit 2 }
Select-String -Path $log -Pattern "error CS|Exception" | Select-Object -First 8 | ForEach-Object { $_.Line }
Select-String -Path $log -Pattern "\[CamFrame\] (venue|car |scene|silhouette|ALL IN TARGET|FAIL|done)" -CaseSensitive |
    ForEach-Object { $_.Line }
if (Test-Path "$out\camframe_$Rig.txt") { Get-Content "$out\camframe_$Rig.txt" }
else { Write-Host "NO REPORT - see $log"; exit 2 }
