# The driver's arms (CockpitArms), tested and photographed in ONE Unity job
# (PSXRacing.EditorTools.CockpitArmsShots.Run) on an ALREADY-BUILT sandbox:
# the arms' self-test lines, eight frames (rest LHD/RHD, hand over hand x3,
# shift, handbrake, look-over x2 - steering is a scripted sweep, the shift and
# handbrake go through CarController), and the cockpit.bundle rebuilt for
# WebGL with its size before/after. Copies Scripts, Editor, Shaders,
# Resources\Cockpit and CockpitSrc in (robocopy /E, never /MIR), the frames to
# tools\shots\cockpitarms\, and the importer's .meta files + the bundle's
# version hash back into the source.
#
#   powershell -ExecutionPolicy Bypass -File tools\cockpit\cockpit-arms.ps1 [-Venue GillespieGap]
#
# Re-convert the arms first if the converter changed:
#   blender.exe --background --python tools\cockpit\cockpit_arms.py -- <repo root> [preview.png]
param([string]$Venue = "")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. "$src\tools\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders", "Assets\PSXRacing\Resources\Cockpit", "Assets\PSXRacing\CockpitSrc")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\Screenshots\cockpitarms_*.png", "$proj\Screenshots\cockpitcontact_*.png" -ErrorAction SilentlyContinue
$report = "$proj\PSXRacing_cockpitarms.txt"
Remove-Item $report -ErrorAction SilentlyContinue

$env:PSX_SHOT_VENUE = $Venue
$log = "$proj\cockpitarms.log"
Remove-Item $log -ErrorAction SilentlyContinue
Invoke-UnityJob -Project $proj -Log $log -MaxMinutes 25 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CockpitArmsShots.Run",
    "-logFile",$log,"-accept-apiupdate") | Out-Null

if (Test-Path $report) { Get-Content $report }
else {
    Write-Host "NO REPORT - the job died; errors:"
    Select-String -Path $log -Pattern "error CS|Exception" | Select-Object -First 20 | ForEach-Object { $_.Line }
}

$out = Join-Path $src "tools\shots\cockpitarms"
New-Item -ItemType Directory -Force $out | Out-Null
Get-ChildItem "$proj\Screenshots" -Filter "cockpitarms_*.png" -ErrorAction SilentlyContinue | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $out $_.Name) -Force
    Write-Host ("shot " + (Join-Path $out $_.Name))
}
# The before/after contact close-ups (CockpitArms.Contact off, on).
$cout = Join-Path $src "tools\shots\cockpitcontact"
New-Item -ItemType Directory -Force $cout | Out-Null
Get-ChildItem "$proj\Screenshots" -Filter "cockpitcontact_*.png" -ErrorAction SilentlyContinue | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $cout $_.Name) -Force
}
# The importer's metas for the new files (fixed GUIDs in git) and the bundle's
# content hash (the URL's ?v=).
foreach ($f in @("Assets\PSXRacing\CockpitSrc\arms_geo.bytes.meta", "Assets\PSXRacing\Scripts\CockpitArms.cs.meta",
                 "Assets\PSXRacing\Editor\CockpitArmsShots.cs.meta", "Assets\PSXRacing\Resources\Cockpit\v08_bundle.txt")) {
    if (Test-Path "$proj\$f") { Copy-Item "$proj\$f" "$src\$f" -Force }
}
