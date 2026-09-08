# One-off: run the self-test against a CLEAN checkout, to tell a failure this
# pass introduced from one it inherited. Point -Src at a git worktree.
param([string]$Src = "C:\Users\mcgee\psxhead")
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$Src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
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
    Select-String -Path "$proj\PSXRacing_selftest_log.txt" -Pattern "FAIL|SELF-TEST" | ForEach-Object { $_.Line }
} else { "NO LOG"; Get-Content "$proj\selftest.log" -Tail 30 }
Select-String -Path "$proj\selftest.log" -Pattern "\[PizzaSim\] tumble" | ForEach-Object { $_.Line }
