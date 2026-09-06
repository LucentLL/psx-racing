# Put a car on a 15% slope, let go, and measure how far it gets.
#
#   powershell -ExecutionPolicy Bypass -File tools\park-sim.ps1
#   powershell -ExecutionPolicy Bypass -File tools\park-sim.ps1 -SkipMirror
#
# WHY THIS EXISTS. "When I park my car it tends to roll away." Nothing else in
# this project can see that: a screenshot of a parked car and a screenshot of a
# car about to be somewhere else are the same picture, and the self-test's whole
# CarController block is algebra that never steps the solver. This builds no
# scene and takes no pictures -- it drives the physics and prints four numbers,
# one of which is a CONTROL that must fail.
param([switch]$SkipMirror)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\unity-wait.ps1"

$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot

if (-not $SkipMirror) {
    foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
}

# The report is the only proof the method ran: an -executeMethod that throws
# still exits 0 in some Unity versions, and the caller would read the last run's.
Remove-Item "$proj\PSXRacing_parksim.txt" -ErrorAction SilentlyContinue
Invoke-UnityJob -Log "$proj\parksim.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.ParkedCarSim.Shoot",
    "-logFile","$proj\parksim.log","-accept-apiupdate") | Out-Null

$cs = Select-String -Path "$proj\parksim.log" -Pattern "error CS" | Select-Object -First 20
if ($cs) { Write-Host "=== COMPILE ERRORS ==="; $cs | ForEach-Object { $_.Line }; exit 1 }

if (-not (Test-Path "$proj\PSXRacing_parksim.txt")) {
    Write-Host "PARK SIM DID NOT RUN" -ForegroundColor Red
    Get-Content "$proj\parksim.log" -Tail 40
    exit 1
}
Get-Content "$proj\PSXRacing_parksim.txt"
exit 0
