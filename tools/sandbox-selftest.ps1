# Run the self-test against whatever is ALREADY in the sandbox. No copy, no
# mirror — for isolating one hand-patched variable without disturbing source.
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
Remove-Item "$proj\PSXRacing_selftest_log.txt" -ErrorAction SilentlyContinue
$before = @(Get-Process Unity -ErrorAction SilentlyContinue | ForEach-Object Id)
Start-Process -FilePath "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe" -WindowStyle Hidden -ArgumentList @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.LifeSimSelfTest.Run",
    "-logFile","$proj\selftest.log","-accept-apiupdate") | Out-Null
$app = $false; $gone = 0; $dl = (Get-Date).AddMinutes(20)
while ((Get-Date) -lt $dl) {
    Start-Sleep -Seconds 5
    $now = @(Get-Process Unity -ErrorAction SilentlyContinue | ForEach-Object Id)
    $new = @($now | Where-Object { $before -notcontains $_ })
    if ($new.Count -gt 0) { $app = $true; $gone = 0; continue }
    if ($app) { $gone++; if ($gone -ge 6) { break } }
}
if (Test-Path "$proj\PSXRacing_selftest_log.txt") {
    Select-String -Path "$proj\PSXRacing_selftest_log.txt" -Pattern "FAIL|SELF-TEST|PASSENGER|across the seat" | ForEach-Object { $_.Line }
} else { "NO LOG"; Get-Content "$proj\selftest.log" -Tail 30 }
Select-String -Path "$proj\selftest.log" -Pattern "\[PizzaSim\] tumble to 180|\[PizzaSim\] side " | ForEach-Object { $_.Line }
