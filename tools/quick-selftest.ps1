# Scripts+Editor-only self-test: ~3 minutes instead of ~40.
#
# Copies just the two source trees into an ALREADY-BUILT sandbox (no /MIR, so
# the built scenes and the baked shells survive) and runs the self-test. Use
# this while iterating on assertions; anything that changed the BUILDER still
# needs tools\menu-check.ps1 or a full scene build before the result means
# anything about a scene.
$ErrorActionPreference = "Stop"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe"
$proj  = "C:\Users\mcgee\PSXBuild"
$src   = Split-Path -Parent $PSScriptRoot

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

# Delete the marker first: a tool that throws never writes its log, and a stale
# one certifies the previous run just as convincingly as a fresh one.
Remove-Item "$proj\PSXRacing_selftest_log.txt" -ErrorAction SilentlyContinue

$before = @(Get-Process Unity -ErrorAction SilentlyContinue | ForEach-Object Id)
Start-Process -FilePath $unity -WindowStyle Hidden -ArgumentList @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.LifeSimSelfTest.Run",
    "-logFile","$proj\selftest.log","-accept-apiupdate") | Out-Null

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
    if ($appeared) { $gone++; if ($gone -ge 6) { break } }
}

Select-String -Path "$proj\selftest.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_selftest_log.txt") { Get-Content "$proj\PSXRacing_selftest_log.txt" }
else { "NO SELF-TEST LOG - the run threw. Tail of selftest.log:"; Get-Content "$proj\selftest.log" -Tail 40 }
