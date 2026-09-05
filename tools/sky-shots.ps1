# Re-photograph the built scenes after a SHADER or capture-tool change.
#
#   powershell -ExecutionPolicy Bypass -File tools\sky-shots.ps1
#
# The same trick as shots-only.ps1 -- sync code, never /MIR Assets, because a
# full mirror deletes the built scenes and the pass then photographs whatever
# was last committed. The difference is the folder list: shots-only.ps1 syncs
# Scripts and Editor, which is everything EXCEPT the shaders, so a sky or fog
# shader edit never reached the sandbox and the rerun photographed the old one.
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
}
# Resources carries the sky panoramas, and they are SOURCE art rather than
# build output -- but /MIR would delete the generated car shells that live
# beside them, so only the sky folder is copied and only additively.
robocopy "$src\Assets\PSXRacing\Resources\Sky" "$proj\Assets\PSXRacing\Resources\Sky" `
    /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null

# Runs WITHOUT -nographics: the capture renders through the pipeline and a
# headless editor has no device to render with. Retried for the same reason
# shots-only.ps1 retries -- a graphics batchmode editor here sometimes exits
# during script compilation.
for ($i = 1; $i -le 3; $i++) {
    Invoke-UnityJob -Log "$proj\shots.log" -UnityArgs @(
        "-quit","-batchmode","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.PSXScreenshotTool.Capture",
        "-logFile","$proj\shots.log","-accept-apiupdate") | Out-Null
    if (Select-String -Path "$proj\shots.log" -Pattern "Screenshots written to" -Quiet) { break }
    Write-Host "capture attempt $i did not finish, retrying..."
}
if (Select-String -Path "$proj\shots.log" -Pattern "Screenshots written to" -Quiet) {
    Write-Host "SKY SHOTS OK -> $proj\Screenshots"
} else {
    Write-Host "CAPTURE FAILED"; Get-Content "$proj\shots.log" -Tail 20
}
