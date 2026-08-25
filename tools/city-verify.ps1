# City verification battery against an ALREADY-BUILT sandbox: graph/bridge
# audit, edit-mode preview shots, and the LifeSim self-test. No mirror here on
# purpose — a mirror overwrites the freshly built scenes with the stale source
# ones (the trap that once reported six dead-flat circuits).
#
#   powershell -ExecutionPolicy Bypass -File tools\city-verify.ps1
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\unity-wait.ps1"
$proj = "C:\Users\mcgee\PSXBuild"

foreach ($job in @(
    @{ Name = "city audit";    Method = "PSXRacing.EditorTools.CityAudit.Run";       Out = "city_audit.txt" },
    @{ Name = "city preview";  Method = "PSXRacing.EditorTools.CityPreview.Run";     Out = $null },
    @{ Name = "self-test";     Method = "PSXRacing.EditorTools.LifeSimSelfTest.Run"; Out = "PSXRacing_selftest_log.txt" }
)) {
    Write-Host ("--- {0} ---" -f $job.Name) -ForegroundColor Cyan
    if ($job.Out) {
        $outFile = Join-Path $proj $job.Out
        if (Test-Path $outFile) { Remove-Item $outFile -Force }
    }
    $ok = Invoke-UnityJob -Log "$proj\cityjob.log" -MaxMinutes 20 -UnityArgs @(
        "-quit","-batchmode","-projectPath",$proj,
        "-executeMethod",$job.Method,
        "-logFile","$proj\cityjob.log","-accept-apiupdate")
    if (-not $ok) { Write-Host "job did not finish" -ForegroundColor Red }
    $cs = Select-String -Path "$proj\cityjob.log" -Pattern "error CS" | Select-Object -First 10
    if ($cs) { $cs | ForEach-Object { $_.Line } }
    if ($job.Out) {
        $outFile = Join-Path $proj $job.Out
        if (Test-Path $outFile) { Get-Content $outFile }
        else { Write-Host ("no {0} written - the method threw; log tail:" -f $job.Out) -ForegroundColor Red
               Get-Content "$proj\cityjob.log" -Tail 25 }
    }
}
Write-Host "--- preview shots ---"
Get-ChildItem "$proj\Screenshots\City" -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }
