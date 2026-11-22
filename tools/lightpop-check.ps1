# THE LIGHT-POP CHECK (Editor\LightPopCheck.cs + Scripts\Dev\LightPopMeter.cs):
# night drives at speed counting every light that SWITCHES (a lamp's pool or
# glint, a headlight beam, the lamp field, a halo, a lens, a cone), by cause,
# per km, with the worst frame pairs. HIDDEN (batch mode WITH graphics: every
# frame is a render request). One process = one side: -Off is today's lights
# (PSX_LIGHT_NOPOP=0).
#
#   $env:PSX_SANDBOX = 'C:\Users\mcgee\PSXPerf'
#   powershell -ExecutionPolicy Bypass -File tools\lightpop-check.ps1 -Mode city [-Off] [-Routes tryon,uptown] [-Secs 40]
#   powershell -ExecutionPolicy Bypass -File tools\lightpop-check.ps1 -Mode race -Venue GillespieGap [-Off]
#   powershell -ExecutionPolicy Bypass -File tools\lightpop-check.ps1 -Mode build      # city scenes (PC content) + -Stages
#
# Takes the sandbox's lock atomically for the job only, mirrors Scripts / Editor
# / Shaders (from -Src, default this worktree), releases the lock even on failure.
param([string]$Mode = "city", [switch]$Off, [string]$Venue = "GillespieGap", [string]$Routes = "", [string]$Views = "",
      [int]$Secs = 40, [int]$Replay = 60, [string]$Out = "", [string]$Src = "", [string]$Stages = "GillespieGap",
      [int]$MaxMinutes = 60, [string]$Me = "lightpop")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXPerf" }
if (-not $Src) { $Src = Split-Path -Parent $PSScriptRoot }
. "$PSScriptRoot\unity-wait.ps1"
. "$PSScriptRoot\sandbox-lock.ps1"
if (-not $env:PSX_BUILD_TARGET) { $env:PSX_BUILD_TARGET = "StandaloneWindows64" }
$env:PSX_PROFILE = "PC"

$stamp = Enter-SandboxLock -Project $proj -Me $Me
try {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$Src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    if ($Mode -eq "build") {
        $env:PSX_PC_CONTENT = "1"
        Remove-Item "$proj\PSXRacing_city_build_log.txt" -ErrorAction SilentlyContinue
        Invoke-UnityJob -Log "$proj\lightpop_citybuild.log" -MaxMinutes $MaxMinutes -UnityArgs @(
            "-quit", "-batchmode", "-nographics", "-projectPath", $proj,
            "-executeMethod", "PSXRacing.EditorTools.PSXRacingBuilder.BuildCityScenesOnly",
            "-logFile", "$proj\lightpop_citybuild.log", "-accept-apiupdate") | Out-Null
        Select-String -Path "$proj\lightpop_citybuild.log" -Pattern "error CS" | Select-Object -First 10 | ForEach-Object { $_.Line }
        if (Test-Path "$proj\PSXRacing_city_build_log.txt") { Get-Content "$proj\PSXRacing_city_build_log.txt" -Tail 6 }
        if ($Stages) {
            Remove-Item "$proj\PSXRacing_stage_lab.txt" -ErrorAction SilentlyContinue
            $env:PSX_LAB_IDS = $Stages
            Invoke-UnityJob -Log "$proj\lightpop_stagelab.log" -MaxMinutes $MaxMinutes -UnityArgs @(
                "-quit", "-batchmode", "-nographics", "-projectPath", $proj,
                "-executeMethod", "PSXRacing.EditorTools.StageLab.Run",
                "-logFile", "$proj\lightpop_stagelab.log", "-accept-apiupdate") | Out-Null
            if (Test-Path "$proj\PSXRacing_stage_lab.txt") { Get-Content "$proj\PSXRacing_stage_lab.txt" | Select-Object -First 8 }
            else { "NO STAGE LAB REPORT"; Get-Content "$proj\lightpop_stagelab.log" -Tail 20 }
        }
        exit 0
    }
    $side = if ($Off) { "before" } else { "after" }
    if (-not $Out) { $Out = Join-Path $proj ("LightPop\" + $side + "_" + $Mode + $(if ($Mode -eq "race") { "_" + $Venue } else { "" })) }
    $env:PSX_LIGHT_NOPOP = if ($Off) { "0" } else { "1" }
    $env:PSX_LIGHTPOP_MODE = $Mode
    $env:PSX_LIGHTPOP_VENUE = $Venue
    $env:PSX_LIGHTPOP_SECS = "$Secs"
    $env:PSX_LIGHTPOP_REPLAY = "$Replay"
    $env:PSX_LIGHTPOP_OUT = $Out
    if ($Routes) { $env:PSX_LIGHTPOP_ROUTES = $Routes } else { Remove-Item Env:PSX_LIGHTPOP_ROUTES -ErrorAction SilentlyContinue }
    if ($Views) { $env:PSX_LIGHTPOP_VIEWS = $Views } else { Remove-Item Env:PSX_LIGHTPOP_VIEWS -ErrorAction SilentlyContinue }
    $log = "$proj\lightpop_$side`_$Mode.log"
    Remove-Item "$proj\PSXRacing_lightpop.txt" -ErrorAction SilentlyContinue
    # no -nographics: every frame is a render request; always hidden (the owner may be playing)
    Invoke-UnityJob -Watch:$false -Log $log -MaxMinutes $MaxMinutes -UnityArgs @(
        "-batchmode", "-projectPath", $proj,
        "-executeMethod", "PSXRacing.EditorTools.LightPopCheck.Run",
        "-logFile", $log, "-accept-apiupdate") | Out-Null
    Select-String -Path $log -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
    if (Test-Path "$proj\PSXRacing_lightpop.txt") { Get-Content "$proj\PSXRacing_lightpop.txt"; exit 0 }
    "NO REPORT - the run threw. Tail of the log:"
    Get-Content $log -Tail 40
    exit 1
}
finally {
    Exit-SandboxLock -Project $proj -Stamp $stamp
}
