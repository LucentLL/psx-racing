# THE CONIFER ROUND (Editor\ConiferShots.cs): the Codex conifer delivery measured, one stage built with
# PSX_CONIFER_MODELS, and that stage in the game (beside an NM beech, from the road models | cards, the swaps,
# feet and road clearance, the cost). Hidden batch-mode jobs with graphics, muted; the sandbox's lock for the
# job only.
#
#   powershell -ExecutionPolicy Bypass -File tools\conifer-shots.ps1 -Step build [-Stage BeechGap] [-Models all]
#   powershell -ExecutionPolicy Bypass -File tools\conifer-shots.ps1 -Step play  [-Stage BeechGap] [-Out <folder>]
param([ValidateSet("build", "play")][string]$Step = "build", [string]$Stage = "BeechGap", [string]$Models = "all",
      [string]$Out = "", [int]$MaxMinutes = 60, [string]$Me = "conifers")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXRec" }
$src = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
. "$PSScriptRoot\sandbox-lock.ps1"
$env:PSX_BUILD_TARGET = "StandaloneWindows64"
$env:PSX_PROFILE = "PC"
$env:PSX_PC_CONTENT = "1"
$env:PSX_WATCH = "0"
if (-not $Out) { $Out = Join-Path $proj "Screenshots\conifers" }
$env:CS_OUT = $Out
$env:CS_STAGE = $Stage
New-Item -ItemType Directory -Force -Path $Out | Out-Null

# never while the owner plays: one blocking wait on his game (a PSXRacing.exe without -batchmode), 40 minutes at most
$his = Get-CimInstance Win32_Process -Filter "Name='PSXRacing.exe'" | Where-Object { $_.CommandLine -notmatch '-batchmode' }
if ($his) {
    Write-Host "the owner's game is running (pid $($his.ProcessId -join ',')) - waiting up to 40 min"
    Wait-Process -Id $his.ProcessId -Timeout 2400 -ErrorAction SilentlyContinue
    if (Get-CimInstance Win32_Process -Filter "Name='PSXRacing.exe'" | Where-Object { $_.CommandLine -notmatch '-batchmode' }) { Write-Host "still playing - not starting"; exit 2 }
}

$stamp = Enter-SandboxLock -Project $proj -Me $Me
try {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    & "$PSScriptRoot\mountain-conifers.ps1" -Dest $proj
    if ($Step -eq "build") {
        $env:PSX_CONIFER_MODELS = $Models
        $log = "$proj\conifers_build.log"
        Invoke-UnityJob -Watch:$false -Log $log -MaxMinutes $MaxMinutes -UnityArgs (Add-PSXBuildTarget @(
            "-quit", "-batchmode", "-projectPath", $proj,
            "-executeMethod", "PSXRacing.EditorTools.ConiferShots.BuildStage",
            "-logFile", $log, "-accept-apiupdate")) | Out-Null
        Select-String -Path $log -Pattern "error CS|Exception" | Select-Object -First 10 | ForEach-Object { $_.Line }
        if (Test-Path "$Out\build_report.txt") { Get-Content "$Out\build_report.txt" } else { "NO BUILD REPORT"; Get-Content $log -Tail 30 }
    }
    else {
        Remove-Item Env:PSX_CONIFER_MODELS -ErrorAction SilentlyContinue
        $log = "$proj\conifers_play.log"
        Invoke-UnityJob -Watch:$false -Log $log -MaxMinutes $MaxMinutes -UnityArgs (Add-PSXBuildTarget @(
            "-batchmode", "-projectPath", $proj,
            "-executeMethod", "PSXRacing.EditorTools.ConiferShots.Run",
            "-logFile", $log, "-accept-apiupdate")) | Out-Null
        Select-String -Path $log -Pattern "error CS|NullReference|Exception" | Select-Object -First 10 | ForEach-Object { $_.Line }
        if (Test-Path "$Out\report.txt") { Get-Content "$Out\report.txt" } else { "NO REPORT"; Get-Content $log -Tail 30 }
    }
}
finally {
    Exit-SandboxLock -Project $proj -Stamp $stamp
}
