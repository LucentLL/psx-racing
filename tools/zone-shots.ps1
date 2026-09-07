# Build BOTH drivable-zone scenes and PHOTOGRAPH THEIR ZONE LINES the way the
# player meets them, then fail if a line does not change enough pixels.
#
#   powershell -ExecutionPolicy Bypass -File tools\zone-shots.ps1
#   powershell -ExecutionPolicy Bypass -File tools\zone-shots.ps1 -SkipMirror
#   powershell -ExecutionPolicy Bypass -File tools\zone-shots.ps1 -SkipBuild
#
# WHY THIS EXISTS. The first zone line was signed off from a photograph taken
# from the ARRIVAL side at 540 lines, and the owner could not see it from the
# chase camera at 240. ZoneLineShot now stands the scene's own camera where
# ChaseCamera stands it — inside the zone, heading out, 100/40/12 m before the
# line — at the game's own line counts, renders each frame with the line on and
# off, and counts the pixels that differ. This script is the loop that runs it;
# nb-check.ps1 is the model.
#
# It mirrors ALL of Assets, not just Scripts and Editor, because the curtain's
# shader lives under Shaders/ — shots-only.ps1 does not sync that folder and
# would photograph a magenta curtain (see the sky note for the same hole).
# Both scenes are built with -nographics; the shots are NOT: a null device
# reads back black, which is zero differing pixels and a FAIL for the wrong
# reason.
param([switch]$SkipMirror, [switch]$SkipBuild)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\unity-wait.ps1"

$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot

if (-not $SkipMirror -and -not $SkipBuild) {
    foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
}

if (-not $SkipBuild) {
    # The neighbourhood first: BuildNeighborhoodOnly runs EnsureFolders, and
    # the town builder assumes the folders exist.
    $jobs = @(
        @{ Scene = "Neighborhood"; Method = "PSXRacing.EditorTools.PSXRacingBuilder.BuildNeighborhoodOnly"; Log = "nbbuild.log" },
        @{ Scene = "Town";         Method = "PSXRacing.EditorTools.PSXRacingBuilder.BuildTownMenu";         Log = "townbuild.log" }
    )
    foreach ($job in $jobs) {
        $sceneFile = "$proj\Assets\PSXRacing\Scenes\$($job.Scene).unity"
        # The scene file is the only proof the builder finished. An
        # -executeMethod that throws still exits 0 in some Unity versions, and
        # the shots below would then photograph the PREVIOUS build.
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
        Select-String -Path "$proj\$($job.Log)" -Pattern "\[Neighborhood\]|\[Town\]|WARN" |
            ForEach-Object { "  " + ($_.Line -replace '^\[PSXBuild\] ', '') }
    }
}

# The PNGs are the only proof the shot tool ran: delete last run's first, or a
# tool that threw leaves yesterday's pictures to be read as today's.
$shots = "$proj\Screenshots\ZoneLines"
if (Test-Path $shots) { Remove-Item "$shots\*.png" -ErrorAction SilentlyContinue }
Invoke-UnityJob -Log "$proj\zoneshots.log" -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.ZoneLineShot.Run",
    "-logFile","$proj\zoneshots.log","-accept-apiupdate") | Out-Null

$cs = Select-String -Path "$proj\zoneshots.log" -Pattern "error CS" | Select-Object -First 20
if ($cs) { Write-Host "=== COMPILE ERRORS ==="; $cs | ForEach-Object { $_.Line }; exit 1 }

Write-Host "=== ZONE LINES ==="
Select-String -Path "$proj\zoneshots.log" -Pattern "\[ZoneLine\]" | ForEach-Object { "  " + $_.Line }

$pngs = Get-ChildItem "$shots\*.png" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notmatch "_diff\.png$" }
if (-not $pngs) {
    Write-Host "ZONE SHOTS WROTE NOTHING - the tool threw" -ForegroundColor Red
    Get-Content "$proj\zoneshots.log" -Tail 40
    exit 1
}
Write-Host "=== SHOTS ($shots) ==="
$pngs | ForEach-Object { "  {0}  {1:n0} bytes  {2:HH:mm:ss}" -f $_.Name, $_.Length, $_.LastWriteTime }

if (Select-String -Path "$proj\zoneshots.log" -Pattern "\[ZoneLine\] FAIL" -Quiet) {
    Write-Host "ZONE LINE FAIL - a line is below its pixel threshold; open the _diff.png masks" -ForegroundColor Red
    exit 1
}
exit 0
