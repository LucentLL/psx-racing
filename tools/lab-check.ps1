# THE LAB CHECK on a built exe (labswitches, 2026-11-23): every LAB row flipped, its picture compared, flipped
# back - on a stage at night in rain, and in Charlotte at night. One line per row in <out>\lab_*_lab.txt.
#
#   powershell -ExecutionPolicy Bypass -File tools\lab-check.ps1 -Exe C:\Users\mcgee\PSXRelease\Win64_PC65\PSXRacing.exe
#       [-Out C:\...\labcheck] [-Stage LakeLure] [-From 7970] [-Route trade] [-CityFrom 1700] [-View chase] [-Snaps]
#
# Exit 0: no row FAILED (NOT BACK / EXCEPTION / HALF STATE). NO CHANGE lines are reported, not failed: a row only
# shows where its feature is in view (the window rain in the cockpit, the tunnel lamps in a tunnel...).
# The run is a perf tour: it ignores the owner's saved LAB choices and writes none. It refuses to start while a
# PSXRacing.exe without -batchmode is running (the owner's game).
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$Out = "",
    [string]$Stage = "LakeLure",
    [string]$From = "7970",
    [string]$Route = "trade",
    [string]$CityFrom = "1700",
    [string]$View = "chase",
    [switch]$Snaps,
    [int]$MaxMinutes = 15
)
$ErrorActionPreference = "Stop"
if (-not (Test-Path $Exe)) { Write-Host "NO EXE $Exe"; exit 2 }
if ($Out -eq "") { $Out = Join-Path (Split-Path -Parent $Exe) "labcheck" }
New-Item -ItemType Directory -Force $Out | Out-Null
Get-ChildItem $Out -Filter "lab_*" -ErrorAction SilentlyContinue | Remove-Item -Force

$owner = Get-CimInstance Win32_Process -Filter "Name='PSXRacing.exe'" | Where-Object { $_.CommandLine -notmatch "-batchmode" }
if ($owner) { Write-Host "THE OWNER'S GAME IS RUNNING (pid $($owner.ProcessId)): not started"; exit 3 }

$snap = if ($Snaps) { "&labsnaps=1" } else { "" }
$runs = @(
    @{ tag = "lab_stage"; q = "stage=$Stage&from=$From&hour=night&weather=rain&view=$View&lab=1$snap&tag=lab_stage" },
    @{ tag = "lab_city";  q = "routes=$Route&from=$CityFrom&hour=night&view=$View&lab=1$snap&tag=lab_city" }
)
$fail = 0
foreach ($r in $runs) {
    $log = Join-Path $Out ($r.tag + "_player.log")
    $p = Start-Process -FilePath $Exe -PassThru -ArgumentList @("-batchmode", "-perftour", ('"' + $r.q + '"'), "-perfout", ('"' + $Out + '"'), "-logFile", ('"' + $log + '"'))
    if (-not $p.WaitForExit($MaxMinutes * 60 * 1000)) { Write-Host ("TIMEOUT " + $r.tag + " (left running: pid " + $p.Id + ")"); $fail++; continue }
    # (owner 2026-11-25: "please mute all testing. I constantly hear the game without ever seeing it") a hidden run must be
    # silent. The build says so in its log (MasterOut, mountain-nm 8a5737c0); an older exe is not to be run as a test.
    if (-not ((Test-Path $log) -and (Select-String -LiteralPath $log -Pattern "\[MasterOut\] test run: the sound is muted" -Quiet))) { Write-Host ("NOT SILENT " + $r.tag + ": this build did not mute its test run"); $fail++ }
    $txt = Get-ChildItem $Out -Filter ($r.tag + "_*_lab.txt") | Select-Object -First 1
    if (-not $txt) { Write-Host ("NO REPORT " + $r.tag + " - see " + $log); $fail++; continue }
    Write-Host ("=== " + $txt.Name)
    Get-Content $txt.FullName | Where-Object { $_ -match "^LAB CHECK|^SUMMARY|NOT BACK|EXCEPTION|HALF STATE|NO CHANGE|PASS|PENDING" } | ForEach-Object { $_ }
    if (Select-String -Path $txt.FullName -Pattern "NOT BACK|EXCEPTION|HALF STATE" -Quiet) { $fail++ }
}
if ($fail -gt 0) { Write-Host "LAB CHECK FAILED ($fail)"; exit 1 }
Write-Host "LAB CHECK OK"
exit 0
