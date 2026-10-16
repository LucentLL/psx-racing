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
# -Reflect: the windscreen's reflection of the cabin instead
# (CockpitInteriorShots.CaptureReflection, default venue BlueRidge): five
# wsr_*.png frames into tools\shots\windshieldreflect\ and [Reflect] lines.
#
# Re-convert the owner's model first if it or the converter changed:
#   blender.exe --background --python tools\cockpit\cockpit_v08.py -- <repo root>, then py tools\cockpit\cockpit_v08_tex.py
param([string]$Venue = "", [switch]$Reflect)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. "$src\tools\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders", "Assets\PSXRacing\Resources\Cockpit", "Assets\PSXRacing\CockpitSrc")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
if (Test-Path "$src\Assets\PSXRacing\Resources\Cockpit.meta") {
    Copy-Item "$src\Assets\PSXRacing\Resources\Cockpit.meta" "$proj\Assets\PSXRacing\Resources\Cockpit.meta" -Force
}
# The generic cabin's files are gone from the source; robocopy /E never
# deletes, so take them out of the sandbox too (unreferenced Resources ship).
foreach ($f in @("generic_cockpit.obj", "generic_wheel.obj", "generic_hands.obj", "cockpit_sheet.png", "hands_skin.png")) {
    Remove-Item "$proj\Assets\PSXRacing\Resources\Cockpit\$f", "$proj\Assets\PSXRacing\Resources\Cockpit\$f.meta" -ErrorAction SilentlyContinue
}
Get-ChildItem "$proj\Assets\PSXRacing\Resources\Cockpit" -Filter "universal_*" -ErrorAction SilentlyContinue | Remove-Item -Force
$prefix = if ($Reflect) { "wsr_" } else { "cockpitv08_" }
$method = if ($Reflect) { "CaptureReflection" } else { "Capture" }
# The reflection's shader must be Always Included like the other cabin ones.
if ($Reflect) { Copy-Item "$src\ProjectSettings\GraphicsSettings.asset" "$proj\ProjectSettings\GraphicsSettings.asset" -Force }
Remove-Item "$proj\Screenshots\$prefix*.png" -ErrorAction SilentlyContinue

$env:PSX_SHOT_VENUE = $Venue
$log = "$proj\cockpitshots.log"
Remove-Item $log -ErrorAction SilentlyContinue
Invoke-UnityJob -Project $proj -Log $log -MaxMinutes 20 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CockpitInteriorShots.$method",
    "-logFile",$log,"-accept-apiupdate") | Out-Null

Select-String -Path $log -Pattern "\[Cockpit\]|\[Reflect\]|PSXShot\]|error CS|Exception" |
    Select-Object -First 40 | ForEach-Object { $_.Line }

$out = Join-Path $src $(if ($Reflect) { "tools\shots\windshieldreflect" } else { "tools\shots\cockpitv08" })
New-Item -ItemType Directory -Force $out | Out-Null
Get-ChildItem "$proj\Screenshots" -Filter "$prefix*.png" | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $out $_.Name) -Force
    Write-Host ("shot " + (Join-Path $out $_.Name))
}
# The importer's metas for the new assets, so the GUIDs the scenes and the
# build see are the ones in git.
foreach ($d in @("Assets\PSXRacing\Resources\Cockpit", "Assets\PSXRacing\CockpitSrc")) {
    Get-ChildItem "$proj\$d" -Filter "*.meta" | ForEach-Object {
        Copy-Item $_.FullName (Join-Path "$src\$d" $_.Name) -Force
    }
}
if (Test-Path "$proj\Assets\PSXRacing\Resources\Cockpit.meta") {
    Copy-Item "$proj\Assets\PSXRacing\Resources\Cockpit.meta" "$src\Assets\PSXRacing\Resources\Cockpit.meta" -Force
}
