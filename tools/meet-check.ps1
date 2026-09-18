# Build the TOWN and the walk-in HOME, run the self-test, and photograph the
# car meet's lot -- without the forty minutes of circuits in front of them.
#
#   powershell -ExecutionPolicy Bypass -File tools\meet-check.ps1
#   powershell -ExecutionPolicy Bypass -File tools\meet-check.ps1 -SkipBuild
#
# For the loop where the sandbox has already been built once and the work is
# on the Eastside Lot (PSXRacingBuilder.Town.BuildTownMeetLot), the home lot's
# yard bays (GarageSceneBuilder.BuildBays), or the rules that fill them
# (CarMeets, CarWhere). Scripts and Editor are copied in with /E -- no /MIR, so
# the built circuits and the baked shells survive -- then only the two scenes
# this pass touches are rebuilt.
#
# It is NOT a substitute for verify.ps1: no terrain audit, no obstacle audit.
# Run this while shaping the lot; run verify before shipping.
#
# The probe stage must NOT pass -nographics: it renders into a RenderTexture
# and a null device reads back as a black PNG, which is indistinguishable from
# a scene that failed to build.
param([switch]$SkipBuild, [switch]$SkipSelfTest)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\unity-wait.ps1"

$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

if (-not $SkipBuild) {
    # The neighbourhood first: BuildNeighborhoodOnly runs EnsureFolders, and
    # the town builder assumes the folders exist (zone-shots.ps1's rule).
    $jobs = @(
        @{ Scene = "Neighborhood"; Method = "PSXRacing.EditorTools.PSXRacingBuilder.BuildNeighborhoodOnly"; Log = "nbbuild.log" },
        @{ Scene = "Town";         Method = "PSXRacing.EditorTools.PSXRacingBuilder.BuildTownMenu";         Log = "townbuild.log" },
        @{ Scene = "Garage";       Method = "PSXRacing.EditorTools.GarageSceneBuilder.Build";               Log = "garagebuild.log" }
    )
    foreach ($job in $jobs) {
        $sceneFile = "$proj\Assets\PSXRacing\Scenes\$($job.Scene).unity"
        # The scene file is the only proof the builder finished: an
        # -executeMethod that throws still exits 0, and everything below would
        # then be looking at the PREVIOUS build.
        Remove-Item $sceneFile -ErrorAction SilentlyContinue
        Invoke-UnityJob -Log "$proj\$($job.Log)" -UnityArgs @(
            "-quit","-batchmode","-nographics","-projectPath",$proj,
            "-executeMethod",$job.Method,
            "-logFile","$proj\$($job.Log)","-accept-apiupdate") | Out-Null

        $cs = Select-String -Path "$proj\$($job.Log)" -Pattern "error CS" | Select-Object -First 20
        if ($cs) { Write-Host "=== COMPILE ERRORS ==="; $cs | ForEach-Object { $_.Line }; exit 1 }
        if (-not (Test-Path $sceneFile)) {
            Write-Host "$($job.Scene.ToUpper()) DID NOT BUILD" -ForegroundColor Red
            Get-Content "$proj\$($job.Log)" -Tail 40
            exit 1
        }
        Select-String -Path "$proj\$($job.Log)" -Pattern "\[Neighborhood\]|\[Town\]|\[Home\]|WARN" |
            ForEach-Object { "  " + ($_.Line -replace '^\[PSXBuild\] ', '') }
    }
}

if (-not $SkipSelfTest) {
    # Delete the marker first. A tool that throws never writes its file, and a
    # stale one certifies the previous run as convincingly as a fresh one.
    Remove-Item "$proj\PSXRacing_selftest_log.txt" -ErrorAction SilentlyContinue
    Invoke-UnityJob -Log "$proj\selftest.log" -UnityArgs @(
        "-quit","-batchmode","-nographics","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.LifeSimSelfTest.Run",
        "-logFile","$proj\selftest.log","-accept-apiupdate") | Out-Null

    $cs = Select-String -Path "$proj\selftest.log" -Pattern "error CS" | Select-Object -First 20
    if ($cs) { Write-Host "=== COMPILE ERRORS ==="; $cs | ForEach-Object { $_.Line }; exit 1 }

    Write-Host "=== SELF-TEST ==="
    if (Test-Path "$proj\PSXRacing_selftest_log.txt") {
        Get-Content "$proj\PSXRacing_selftest_log.txt" | Where-Object { $_ -match "FAIL|SELF-TEST" }
    } else { Write-Host "  self-test wrote nothing - it threw"; Get-Content "$proj\selftest.log" -Tail 25 }
}

Remove-Item "$proj\PSXRacing_townprobe.txt" -ErrorAction SilentlyContinue
Remove-Item "$proj\Screenshots\Town\town_meet*.png" -ErrorAction SilentlyContinue
Invoke-UnityJob -Log "$proj\townprobe.log" -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.TownProbe.Run",
    "-logFile","$proj\townprobe.log","-accept-apiupdate") | Out-Null

Write-Host "=== THE CAR MEET ==="
if (Test-Path "$proj\PSXRacing_townprobe.txt") {
    $lines = Get-Content "$proj\PSXRacing_townprobe.txt"
    $on = $false
    foreach ($l in $lines) {
        if ($l -match "^--- the car meet") { $on = $true }
        elseif ($on -and $l -match "^(player car|===|---)") { $on = $false }
        if ($on) { Write-Host $l }
    }
} else { Write-Host "  probe wrote nothing - it threw"; Get-Content "$proj\townprobe.log" -Tail 30 }

# The home lot: the yard bays are only ever seen from outside, and the one
# shot that looks at them is in the garage capture.
Remove-Item "$proj\Screenshots\psx_garage_9_yard*.png" -ErrorAction SilentlyContinue
Invoke-UnityJob -Log "$proj\garageshots.log" -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.PSXScreenshotTool.CaptureGarageOnly",
    "-logFile","$proj\garageshots.log","-accept-apiupdate") | Out-Null

Write-Host "=== SHOTS ==="
Get-ChildItem "$proj\Screenshots\psx_garage_9_yard*.png","$proj\Screenshots\psx_garage_0_drive*.png" -ErrorAction SilentlyContinue |
    ForEach-Object { "  {0}  {1:n0} bytes  {2:HH:mm:ss}" -f $_.Name, $_.Length, $_.LastWriteTime }
Get-ChildItem "$proj\Screenshots\Town\town_meet*.png" -ErrorAction SilentlyContinue |
    ForEach-Object { "  {0}  {1:n0} bytes  {2:HH:mm:ss}" -f $_.Name, $_.Length, $_.LastWriteTime }
exit 0
