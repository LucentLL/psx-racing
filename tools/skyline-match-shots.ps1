# THE SKYLINE PASS (2026-10-29): the owner's stadium-side aerial against the
# game - Editor/SkylineMatchShots.cs on an already-built sandbox scene, WITH a
# graphics device (it renders). Mirrors this tree's Scripts/Editor/Shaders into
# the sandbox (code only: never Resources/Art from a worktree - the GUID trap;
# copy a changed data file by path with -Files).
#
#   $env:PSX_SANDBOX='C:\Users\mcgee\PSXRec'
#   powershell -ExecutionPolicy Bypass -File tools\skyline-match-shots.ps1 -Tag before [-Shots "match:noon,tryon:noon"] [-AB] [-Ring 5]
#   ... -Sets "before=match:noon;after=match:noon,match:night;clear=match:noon"  (one world per set: before = this pass off)
#
# Writes Screenshots\skym\*.png + skym_*.txt in the sandbox. Takes the
# sandbox's AGENT_LOCK.txt (waits while another agent's is under 90 minutes).
param([string]$Tag = "", [string]$Shots = "", [string]$Sets = "", [switch]$AB, [int]$Ring = 0, [string[]]$Files = @(), [string]$Agent = "uptown-skyline-accuracy")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXRec" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

$lock = Join-Path $proj "AGENT_LOCK.txt"
while (Test-Path $lock) {
    $age = (Get-Date) - (Get-Item $lock).LastWriteTime
    $who = (Get-Content $lock -Raw)
    if ($who -like "$Agent*" -or $age.TotalMinutes -ge 90) { break }
    Write-Host "waiting for the sandbox lock: $who ($([int]$age.TotalMinutes) min old)"
    Start-Sleep -Seconds 60
}
Set-Content -Path $lock -Value ("$Agent " + (Get-Date).ToString("s")) -Encoding ascii
try {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /XF *.meta /XD .git /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    foreach ($f in $Files) {
        Copy-Item -LiteralPath (Join-Path $src $f) -Destination (Join-Path $proj $f) -Force
        Write-Host "copied $f"
    }
    $env:PSX_SKYM_TAG = $Tag
    $env:PSX_SKYM_SHOTS = $Shots
    $env:PSX_SKYM_SETS = $Sets
    $env:PSX_SKYM_AB = if ($AB) { "1" } else { "" }
    $env:PSX_SKYM_RING = if ($Ring -gt 0) { "$Ring" } else { "" }
    $log = "$proj\skymshots.log"
    if (Test-Path $log) { Remove-Item $log -Force }
    Invoke-UnityJob -Log $log -MaxMinutes 40 -UnityArgs @(
        "-quit","-batchmode","-force-glcore","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.SkylineMatchShots.Run",
        "-logFile",$log,"-accept-apiupdate") | Out-Null
    Select-String -Path $log -Pattern "error CS|Exception|\[SkylineMatch\]" -CaseSensitive |
        Select-Object -First 16 | ForEach-Object { $_.Line }
    Get-ChildItem "$proj\Screenshots\skym" -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }
}
finally {
    Remove-Item $lock -Force -ErrorAction SilentlyContinue
}
exit 0
