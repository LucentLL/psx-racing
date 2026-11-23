# THE LIGHT-LOOK ROUND'S COST (copied from lightpop-cost.ps1; 2026-11-23)
# Variants: look_off = PSX_LIGHT_LOOK off in-process; aa = a null A/A (sets the switch to what it is: the noise).
# THE LIGHT-POP ROUND'S COST (the original header): the perf tour's in-process A/B (Scripts\Dev\PerfTour.Probe.cs)
# in the EDITOR (Editor\PerfTourEditor.cs) at night, the main view offscreen at 3832x2054, the
# car driven by the race AI, PSX_LIGHT_NOPOP switched off and on in alternating blocks inside
# one run. Hidden (batch with graphics). The same A/B in a player:
#   PSXRacing.exe -perftour "routes=uptown&probe=drive&offscreen=3832x2054&hour=Night&weather=Clear&view=chase&pv=look_off=set:LightLook.On:0;aa=set:LightLook.On:1&pcfg=8,6,24" -perfout <dir>
#
#   $env:PSX_SANDBOX = 'C:\Users\mcgee\PSXPerf'
#   powershell -ExecutionPolicy Bypass -File tools\lightpop-cost.ps1 -Routes uptown -Tag lp_uptown
param([string]$Routes = "uptown", [string]$Tag = "lp", [string]$View = "chase", [string]$Probe = "park",
      [string]$Pcfg = "20,8,30", [string]$Out = "", [int]$MaxMinutes = 40, [string]$Me = "lightlook", [string]$Pv = "look_off=set:LightLook.On:0;aa=set:LightLook.On:1")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXPerf" }
$Src = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
. "$PSScriptRoot\sandbox-lock.ps1"
if (-not $env:PSX_BUILD_TARGET) { $env:PSX_BUILD_TARGET = "StandaloneWindows64" }
$env:PSX_PROFILE = "PC"
if (-not $Out) { $Out = Join-Path $proj "LightLook\cost_$Tag" }
$stamp = Enter-SandboxLock -Project $proj -Me $Me
try {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$Src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
    $env:PSX_LIGHT_NOPOP = "1"
    $env:PSX_PERFTOUR = "routes=$Routes&probe=$Probe&offscreen=3832x2054&hour=Night&weather=Clear&view=$View&pv=$Pv&pcfg=$Pcfg&tag=$Tag"
    $env:PSX_PERFOUT = $Out
    $log = "$proj\lightlook_cost_$Tag.log"
    Invoke-UnityJob -Watch:$false -Log $log -MaxMinutes $MaxMinutes -UnityArgs @(
        "-batchmode", "-projectPath", $proj,
        "-executeMethod", "PSXRacing.EditorTools.PerfTourEditor.Run",
        "-logFile", $log, "-accept-apiupdate") | Out-Null
    Select-String -Path $log -Pattern "error CS|\[PerfTour\] (FAIL|DONE)" | Select-Object -First 10 | ForEach-Object { $_.Line }
    Get-ChildItem $Out -Filter "*probe.txt" -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName }
}
finally {
    Exit-SandboxLock -Project $proj -Stamp $stamp
}
