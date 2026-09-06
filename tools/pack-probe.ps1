# Ask UNITY where a pack model's landmarks are, in Unity's own local coordinates.
#
#   powershell -ExecutionPolicy Bypass -File tools\pack-probe.ps1
#   powershell -ExecutionPolicy Bypass -File tools\pack-probe.ps1 -SkipMirror
#
# WHY THIS EXISTS. Every prop in the project is placed by arithmetic off a
# landmark measured in BLENDER and converted to Unity by hand, and that hand
# conversion is where the sign errors live -- it put the neighbourhood's
# driveways on the blank side of every house. This asks Unity instead. It builds
# no scene and takes no pictures, so it is the cheapest question in the project.
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
# still exits 0, and the caller would otherwise read the PREVIOUS run's file.
Remove-Item "$proj\PSXRacing_packprobe.txt" -ErrorAction SilentlyContinue
Invoke-UnityJob -Log "$proj\packprobe.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.PackProbe.Run",
    "-logFile","$proj\packprobe.log","-accept-apiupdate") | Out-Null

$cs = Select-String -Path "$proj\packprobe.log" -Pattern "error CS" | Select-Object -First 20
if ($cs) { Write-Host "=== COMPILE ERRORS ==="; $cs | ForEach-Object { $_.Line }; exit 1 }

if (-not (Test-Path "$proj\PSXRacing_packprobe.txt")) {
    Write-Host "PROBE DID NOT RUN" -ForegroundColor Red
    Get-Content "$proj\packprobe.log" -Tail 40
    exit 1
}
Get-Content "$proj\PSXRacing_packprobe.txt"
exit 0
