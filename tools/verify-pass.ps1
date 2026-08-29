# Post-scene-build verification for this session's changes. Runs against the
# already-built sandbox -- no mirror, because a mirror overwrites the freshly
# built scenes with the stale source ones (see city-verify.ps1 for the full
# story on that trap).
#
#   powershell -ExecutionPolicy Bypass -File tools\verify-pass.ps1
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\unity-wait.ps1"
$proj = "C:\Users\mcgee\PSXBuild"

$jobs = @(
    @{ Name = "city audit";    Method = "PSXRacing.EditorTools.CityAudit.Run";       Out = "city_audit.txt" },
    @{ Name = "city preview";  Method = "PSXRacing.EditorTools.CityPreview.Run";     Out = $null },
    @{ Name = "engine hoist";  Method = "PSXRacing.EditorTools.HoistPreview.Run";    Out = $null },
    @{ Name = "self-test";     Method = "PSXRacing.EditorTools.LifeSimSelfTest.Run"; Out = "PSXRacing_selftest_log.txt" }
)

foreach ($job in $jobs) {
    Write-Host ("--- {0} ---" -f $job.Name) -ForegroundColor Cyan
    if ($job.Out) {
        $outFile = Join-Path $proj $job.Out
        if (Test-Path $outFile) { Remove-Item $outFile -Force }
    }
    $ok = Invoke-UnityJob -Log "$proj\verifyjob.log" -MaxMinutes 25 -UnityArgs @(
        "-quit","-batchmode","-projectPath",$proj,
        "-executeMethod",$job.Method,
        "-logFile","$proj\verifyjob.log","-accept-apiupdate")
    if (-not $ok) { Write-Host "job did not finish" -ForegroundColor Red }
    $cs = Select-String -Path "$proj\verifyjob.log" -Pattern "error CS" | Select-Object -First 10
    if ($cs) { $cs | ForEach-Object { $_.Line } }
    # The city and hoist previews write PNGs, not a text file -- their own Debug
    # lines are the only report they have, so surface them.
    $notes = Select-String -Path "$proj\verifyjob.log" -Pattern "\[Hoist\]|\[CityPreview\]|\[City\] buildings|prop prefab missing|City props baked" |
             Select-Object -First 12
    if ($notes) { $notes | ForEach-Object { $_.Line } }
    if ($job.Out) {
        $outFile = Join-Path $proj $job.Out
        if (Test-Path $outFile) { Get-Content $outFile }
        else {
            Write-Host ("no {0} written - the method threw; log tail:" -f $job.Out) -ForegroundColor Red
            Get-Content "$proj\verifyjob.log" -Tail 25
        }
    }
}

Write-Host "--- shots ---" -ForegroundColor Cyan
Get-ChildItem "$proj\Screenshots" -Recurse -Filter *.png -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 20 |
    ForEach-Object { "{0}  {1}" -f $_.LastWriteTime.ToString("HH:mm"), $_.FullName }
