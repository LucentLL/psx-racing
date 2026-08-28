# Re-run just the screenshot pass against the scenes already in the sandbox.
#
#   powershell -ExecutionPolicy Bypass -File tools\shots-only.ps1
#
# DOES NOT MIRROR. Mirroring would overwrite the built scenes with the stale
# ones in source and photograph those instead -- which is exactly how a set of
# bridge shots came back with no ground in them. Use tools\verify.ps1 when the
# scenes need rebuilding first.
#
# Runs WITHOUT -nographics: the capture renders through the pipeline and a
# headless editor has no device to render with. That is also why it retries --
# a graphics batchmode editor here sometimes exits during script compilation.
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

# Sync the CODE but not the scenes. Without this a fix to the capture tool never
# reaches the sandbox and the rerun photographs the old one; with a full /MIR the
# scenes go stale and it photographs the wrong circuits. The two script folders
# are the only thing this pass can legitimately need updated.
foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
}

for ($i = 1; $i -le 3; $i++) {
    Invoke-UnityJob -Log "$proj\shots.log" -UnityArgs @(
        "-quit","-batchmode","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.PSXScreenshotTool.Capture",
        "-logFile","$proj\shots.log","-accept-apiupdate") | Out-Null
    if (Select-String -Path "$proj\shots.log" -Pattern "PSXShot") {
        Select-String -Path "$proj\shots.log" -Pattern "PSXShot" | ForEach-Object { $_.Line }
        exit 0
    }
    Write-Host "attempt ${i}: capture did not finish - retrying"
    Select-String -Path "$proj\shots.log" -Pattern "error CS|Exception" |
        Select-Object -First 4 | ForEach-Object { $_.Line }
}
Write-Host "CAPTURE FAILED - see $proj\shots.log"
exit 1
