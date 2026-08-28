# Run one Unity batchmode job in the sandbox and DO NOT RETURN UNTIL IT IS DONE.
#
# Dot-source it:  . "$PSScriptRoot\unity-wait.ps1"
# then:           Invoke-UnityJob -UnityArgs @(...) -Log "$proj\build.log"
#
# Two traps this exists for, both of which have silently produced a green run
# over a job that never happened:
#
#   1. Unity.exe on Windows is a LAUNCHER. It spawns the real editor and returns
#      in seconds, so waiting on the call itself reads stale logs. The fix
#      everything here already used was "sleep 5, then poll for new Unity PIDs"
#      -- which is a race. Under a full asset reimport the child can take longer
#      than five seconds to appear, the poll sees nothing new, and the caller
#      marches on and starts a SECOND job against the same sandbox. Two Unity
#      instances fight over the artifact database and the scene build writes
#      nothing at all. That is what turned an elevation pass into six dead-flat
#      circuits, twice.
#
#   2. A -executeMethod that throws never writes its own output file, so the
#      caller reads the PREVIOUS run's.
#
# So: wait for the child to APPEAR before starting to wait for it to leave, and
# hand back whether the log says the job finished.
$ErrorActionPreference = "Stop"

function Invoke-UnityJob {
    param(
        [Parameter(Mandatory = $true)][string[]]$UnityArgs,
        [Parameter(Mandatory = $true)][string]$Log,
        [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe",
        [int]$MaxMinutes = 40
    )

    if (Test-Path $Log) { Remove-Item $Log -Force }

    $before = @(Get-Process Unity -ErrorAction SilentlyContinue | ForEach-Object Id)
    Start-Process -FilePath $Unity -ArgumentList $UnityArgs -WindowStyle Hidden | Out-Null

    # Phase one: wait for a Unity process that was not there before. Up to two
    # minutes, because a cold sandbox spends that long opening the project
    # before it is visible as a child at all.
    $appeared = $false
    $spawnDeadline = (Get-Date).AddMinutes(2)
    while ((Get-Date) -lt $spawnDeadline) {
        Start-Sleep -Seconds 2
        $now = @(Get-Process Unity -ErrorAction SilentlyContinue | ForEach-Object Id)
        if (@($now | Where-Object { $before -notcontains $_ })) { $appeared = $true; break }
    }
    if (-not $appeared) {
        Write-Host "UNITY NEVER STARTED - check the argument list (a path with a space must be ONE quoted string)"
        return $false
    }

    # Phase two: wait for every new PID to go away, and STAY away.
    #
    # One empty poll is not enough. The launcher is itself a Unity process, so
    # the sequence is: launcher appears, launcher exits, gap, real editor
    # appears. A single empty poll landing in that gap declares the job finished
    # a fraction of a second before it starts. Requiring the gap to hold for
    # half a minute is longer than the handoff has ever taken and costs half a
    # minute on a job that runs for five.
    $quiet = 0
    $deadline = (Get-Date).AddMinutes($MaxMinutes)
    while ((Get-Date) -lt $deadline) {
        $now = @(Get-Process Unity -ErrorAction SilentlyContinue | ForEach-Object Id)
        if (@($now | Where-Object { $before -notcontains $_ })) { $quiet = 0 }
        else {
            $quiet++
            if ($quiet -ge 6) { return $true }
        }
        Start-Sleep -Seconds 5
    }
    Write-Host "UNITY TIMED OUT after $MaxMinutes minutes - see $Log"
    return $false
}

# Build every circuit, RETRYING until the builder says it finished.
#
# A cold sandbox has to import ~4400 assets, 560 of which are engine-audio .ogg
# files, and the editor reliably dies somewhere inside the FMOD bank build for
# those -- no error in the log, just a process that stops writing and exits. It
# is not fatal, because everything imported before the crash is now in the
# artifact database: the next run picks up where it stopped and gets further.
# Three or four passes gets through a full reimport.
#
# The BUILD OK marker is the only thing that counts, and the file is deleted
# first, because a stale one from an earlier run reads exactly like a fresh one
# and has already let an audit run against six dead-flat circuits.
function Invoke-SceneBuild {
    param(
        [Parameter(Mandatory = $true)][string]$Proj,
        [int]$Attempts = 5
    )
    $marker = "$Proj\PSXRacing_build_log.txt"
    if (Test-Path $marker) { Remove-Item $marker -Force }

    for ($i = 1; $i -le $Attempts; $i++) {
        Invoke-UnityJob -Log "$Proj\scenebuild.log" -UnityArgs @(
            "-quit","-batchmode","-nographics","-projectPath",$Proj,
            "-executeMethod","PSXRacing.EditorTools.PSXRacingBuilder.Build",
            "-logFile","$Proj\scenebuild.log","-accept-apiupdate") | Out-Null

        $cs = Select-String -Path "$Proj\scenebuild.log" -Pattern "error CS" |
              Select-Object -First 20
        if ($cs) {
            Write-Host "COMPILE ERRORS - retrying will not help:"
            $cs | ForEach-Object { $_.Line }
            return $false
        }
        if ((Test-Path $marker) -and (Select-String -Path $marker -Pattern "BUILD OK")) {
            if ($i -gt 1) { Write-Host "scene build finished on attempt $i" }
            return $true
        }
        $imported = (Select-String -Path "$Proj\scenebuild.log" -Pattern "with importer" | Measure-Object).Count
        Write-Host "attempt ${i}: builder did not finish (imported $imported assets this pass) - retrying"
    }
    return $false
}
