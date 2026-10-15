# The owner's universal cockpit, photographed: six frames on one built venue
# (PSXRacing.EditorTools.CockpitInteriorShots.Capture) - day/night, 16:9 and a
# 19.5:9 phone, ahead and at full look-over, with a settled three-box +
# two-bottle order mirrored onto the passenger seat. Copies Scripts, Editor,
# Shaders and Resources\Cockpit into an ALREADY-BUILT sandbox (no /MIR, no
# scene build), runs the capture, copies the frames to tools\shots\fdcockpit\
# and the importer's .meta files for Resources\Cockpit back into the source
# (fixed GUIDs). The [Cockpit] log lines carry the fit, the tri and draw-call
# counts, and the measured rest gap under the bottom box.
#
#   $env:PSX_SANDBOX = "C:\Users\mcgee\PSXCity"
#   powershell -ExecutionPolicy Bypass -File tools\cockpit\cockpit-shots.ps1 [-Venue GillespieGap]
#
# Re-convert the owner's model first if it or the converter changed:
#   blender.exe --background --python tools\cockpit\universal_cockpit.py -- <repo root>
param([string]$Venue = "")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. "$src\tools\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders", "Assets\PSXRacing\Resources\Cockpit")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
if (Test-Path "$src\Assets\PSXRacing\Resources\Cockpit.meta") {
    Copy-Item "$src\Assets\PSXRacing\Resources\Cockpit.meta" "$proj\Assets\PSXRacing\Resources\Cockpit.meta" -Force
}
# The generic cabin's files are gone from the source; robocopy /E never
# deletes, so take them out of the sandbox too (unreferenced Resources ship).
foreach ($f in @("generic_cockpit.obj", "generic_wheel.obj", "generic_hands.obj", "cockpit_sheet.png")) {
    Remove-Item "$proj\Assets\PSXRacing\Resources\Cockpit\$f", "$proj\Assets\PSXRacing\Resources\Cockpit\$f.meta" -ErrorAction SilentlyContinue
}
Remove-Item "$proj\Screenshots\fdcockpit_*.png" -ErrorAction SilentlyContinue

$env:PSX_SHOT_VENUE = $Venue
$log = "$proj\cockpitshots.log"
Remove-Item $log -ErrorAction SilentlyContinue
Invoke-UnityJob -Project $proj -Log $log -MaxMinutes 20 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CockpitInteriorShots.Capture",
    "-logFile",$log,"-accept-apiupdate") | Out-Null

Select-String -Path $log -Pattern "\[Cockpit\]|PSXShot\]|error CS|Exception" |
    Select-Object -First 40 | ForEach-Object { $_.Line }

$out = Join-Path $src "tools\shots\fdcockpit"
New-Item -ItemType Directory -Force $out | Out-Null
Get-ChildItem "$proj\Screenshots" -Filter "fdcockpit_*.png" | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $out $_.Name) -Force
    Write-Host ("shot " + (Join-Path $out $_.Name))
}
# The importer's metas for the new assets, so the GUIDs the scenes and the
# build see are the ones in git.
Get-ChildItem "$proj\Assets\PSXRacing\Resources\Cockpit" -Filter "*.meta" | ForEach-Object {
    Copy-Item $_.FullName (Join-Path "$src\Assets\PSXRacing\Resources\Cockpit" $_.Name) -Force
}
if (Test-Path "$proj\Assets\PSXRacing\Resources\Cockpit.meta") {
    Copy-Item "$proj\Assets\PSXRacing\Resources\Cockpit.meta" "$src\Assets\PSXRacing\Resources\Cockpit.meta" -Force
}
