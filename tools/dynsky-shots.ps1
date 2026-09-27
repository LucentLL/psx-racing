# The DYNAMIC sky round the compass, five daylight hours (DynamicSkyShots):
# sunward / side / away / up. Syncs Scripts, Editor and Shaders like
# sky-shots.ps1 (a sky shader edit has to reach the sandbox).
#
#   powershell -ExecutionPolicy Bypass -File tools\dynsky-shots.ps1 [-Venue BlueRidge]
param([string]$Venue = "BlueRidge")
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\Screenshots\dsky_*" -ErrorAction SilentlyContinue
$env:PSX_SKY_VENUE = $Venue
for ($i = 1; $i -le 3; $i++) {
    Invoke-UnityJob -Log "$proj\dynsky.log" -UnityArgs @(
        "-quit","-batchmode","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.DynamicSkyShots.Capture",
        "-logFile","$proj\dynsky.log","-accept-apiupdate") | Out-Null
    if (Select-String -Path "$proj\dynsky.log" -Pattern "Screenshots written to" -Quiet) { break }
    Write-Host "capture attempt $i did not finish, retrying..."
}
if (Select-String -Path "$proj\dynsky.log" -Pattern "Screenshots written to" -Quiet) {
    Write-Host "DYNSKY SHOTS OK -> $proj\Screenshots\dsky_*"
} else {
    Write-Host "CAPTURE FAILED"
    Select-String -Path "$proj\dynsky.log" -Pattern "error|Shader error" | Select-Object -First 15
    Get-Content "$proj\dynsky.log" -Tail 15
}
