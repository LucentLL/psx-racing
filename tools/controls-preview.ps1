# Render the assembled touch panel + instrument cluster to PNGs, at three
# control positions, without booting the game on a phone.
#
#   powershell -ExecutionPolicy Bypass -File tools\controls-preview.ps1
#
# Syncs CODE ONLY, like shots-only.ps1: this pass builds its own scene from
# scratch and never reads a circuit, so mirroring the whole project would cost a
# full reimport for nothing.
#
# Runs WITHOUT -nographics — the capture renders through the UI pipeline and a
# headless editor has no device to render with — and retries, because a
# graphics batchmode editor here sometimes exits during script compilation.
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
}

for ($i = 1; $i -le 3; $i++) {
    Invoke-UnityJob -Log "$proj\ctrlpreview.log" -UnityArgs @(
        "-quit","-batchmode","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.TouchControlsPreview.DumpPanel",
        "-logFile","$proj\ctrlpreview.log","-accept-apiupdate") | Out-Null

    $cs = Select-String -Path "$proj\ctrlpreview.log" -Pattern "error CS" | Select-Object -First 12
    if ($cs) {
        Write-Host "COMPILE ERRORS - retrying will not help:"
        $cs | ForEach-Object { $_.Line }
        exit 1
    }
    if (Select-String -Path "$proj\ctrlpreview.log" -Pattern "\[Preview\] wrote") {
        Select-String -Path "$proj\ctrlpreview.log" -Pattern "\[Preview\]" |
            ForEach-Object { $_.Line }
        exit 0
    }
    Write-Host "attempt ${i}: preview did not finish - retrying"
    Select-String -Path "$proj\ctrlpreview.log" -Pattern "Exception|NullReference" |
        Select-Object -First 4 | ForEach-Object { $_.Line }
}
Write-Host "PREVIEW FAILED - see $proj\ctrlpreview.log"
exit 1
