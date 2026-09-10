# Play every reverse twin headless and check the grid it lines up.
#
# A PLAY-MODE check on purpose: the reversal is arithmetic the edit-mode
# self-test already covers, but the grid STAGING writes car poses, and the bug
# it was written for (CarController.TeleportTo) only exists once physics runs.
# Scripts+Editor only, like quick-selftest.ps1 -- the built scenes and the baked
# shells survive, so this is a ~4 minute answer rather than a 40 minute one.
#
# Exit code 0 = every twin lines up; 1 = something in the report says FAIL.
$ErrorActionPreference = "Stop"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe"
$proj  = "C:\Users\mcgee\PSXBuild"
$src   = Split-Path -Parent $PSScriptRoot

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

# Delete the marker first: a tool that throws never writes its log, and a stale
# one certifies the previous run just as convincingly as a fresh one.
Remove-Item "$proj\PSXRacing_reverse_check.txt" -ErrorAction SilentlyContinue

# NO -quit: this one enters play mode and exits itself when the list is done.
$before = @(Get-Process Unity -ErrorAction SilentlyContinue | ForEach-Object Id)
Start-Process -FilePath $unity -WindowStyle Hidden -ArgumentList @(
    "-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.ReverseRaceCheck.Run",
    "-logFile","$proj\reversecheck.log","-accept-apiupdate") | Out-Null

# Wait for the child editor to APPEAR (the launcher exits immediately), then
# for it to stay gone.
$appeared = $false
$deadline = (Get-Date).AddMinutes(25)
$gone = 0
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
    $now = @(Get-Process Unity -ErrorAction SilentlyContinue | ForEach-Object Id)
    $new = @($now | Where-Object { $before -notcontains $_ })
    if ($new.Count -gt 0) { $appeared = $true; $gone = 0; continue }
    if ($appeared) { $gone++; if ($gone -ge 3) { break } }
}

Select-String -Path "$proj\reversecheck.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_reverse_check.txt") {
    $report = Get-Content "$proj\PSXRacing_reverse_check.txt"
    $report
    # Explicit, both ways: robocopy above exits 1-3 on a SUCCESSFUL copy, and a
    # script that just falls off the end inherits that as its own status.
    if ($report -match "FAIL") { exit 1 }
    exit 0
} else {
    "NO REVERSE-CHECK LOG - the run threw. Tail of reversecheck.log:"
    Get-Content "$proj\reversecheck.log" -Tail 40
    exit 1
}
