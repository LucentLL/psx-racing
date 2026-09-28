# Run one Unity job in the sandbox and DO NOT RETURN UNTIL IT IS DONE.
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
#
# WATCHING THE TESTS
# ------------------
# The play tests - every tool that enters play mode: race-play-check,
# city-play-check, traffic-play-check and the other *-play-check.ps1, plus
# sprint-check, reverse-check, replay-check and wall-scrape-play - open the
# sandbox in a normal, VISIBLE Unity editor by default, and the test plays in
# its Game view in front of you, with sound. Nothing is recorded; it is the
# editor itself, live. The editor comes to the front and maximises, the Game
# view is set to 16:9 and maximised (Play Maximized) so the game fills the
# window, and the editor closes by itself when the check is done. The tool
# then prints the same result lines it always has.
#
#   powershell -ExecutionPolicy Bypass -File tools\race-play-check.ps1 -Venue BlueRidge -Seconds 60
#   ... -NoWatch                 the old way: hidden, -batchmode -nographics
#   $env:PSX_WATCH = '0'         every play test hidden, for the whole session
#
# What watching changes: no -batchmode / -nographics, a normal window, and
# PSX_WATCH_ACTIVE=1 for that one editor, which switches on
# Assets\PSXRacing\Editor\PlayCheckWatch.cs (Game view framing, audio unmuted).
# What it does not change: the checks, the seeds, the scenes, the report file
# and the lines the tool prints. Two things a window cannot keep the same:
#   - FRAME RATE. A hidden -nographics editor runs frames as fast as the CPU
#     goes; a visible one draws them. Most of the game steps in Update, so the
#     race's story (distances, recoveries, damage) varies from run to run - but
#     it varies just as much between two HIDDEN runs with the same seed (race
#     check, BlueRidge, seed 0, 2026-09-28: 1052-1124 m with a pile-up in one
#     batch run, 1232-1400 m in the next). The ok/FAIL verdicts matched.
#   - FRAME SHAPE. A hidden editor renders 640x480 (4:3); the watched Game view
#     is 16:9. A check that measures the picture (camframe-play-check notes
#     "frame 640x480 (aspect 1.333)") reports 16:9 numbers when watched. Use
#     -NoWatch for numbers comparable with older hidden runs.
# On a 150% Windows display the Game view's "Scale" reads 1.5x when the frame
# exactly fits: that is one game pixel per screen pixel, not a crop.
#
# Unattended safety, because a visible editor can ask questions batch mode never
# does. Flags: -ignoreCompilerErrors (no Safe Mode prompt), -skipUpgradeDialogs
# (no "project from another version" prompt), -accept-apiupdate (no API updater
# prompt); UNITY_DONOTSTARTBUGREPORTER=1 (a crash exits, no bug reporter). And
# the waiter watches the log and the windows, and closes the editor (politely,
# then by force) with the log's tail printed when:
#   - the log shows compile errors (a visible editor would otherwise run the
#     LAST GOOD assemblies, i.e. a stale test), or -executeMethod failed;
#   - a Windows dialog with buttons stays up longer than PSX_WATCH_DIALOG_SECONDS
#     (default 90) while the log stands still - you can answer it yourself;
#   - the log has not moved for PSX_WATCH_STALL_MINUTES (default 10);
#   - the job's -MaxMinutes runs out and the log has stopped (an editor still
#     writing - a cold sandbox importing - is left to finish and quit itself).
# Watching refuses anything that is not a sandbox (the owner's project, or any
# folder with .git or tools\unity-wait.ps1 in it) and runs that job hidden
# instead. Build, bake, audit, verify, publish and screenshot jobs never watch:
# only a caller that passes -Watch gets a window.
$ErrorActionPreference = "Stop"

# ONLY THE PIDS WORKING ON *THIS* PROJECT.
#
# Every waiter here used to ask "which Unity PIDs were not running when I
# started?", and treat all of them as the job. That is wrong the moment a
# second Unity exists for any reason, and the obvious reason is the owner
# opening their own editor while a build runs. 2026-09-09: a WebGL build
# finished at 23:58, the editor was opened at 00:05, and the publish sat
# waiting on it -- a complete, correct, verified build undeployed for the whole
# of its forty-minute deadline, looking from the outside exactly like a hang.
#
# The command line is the discriminator and it is exact: the sandbox job runs
# -projectPath C:\Users\mcgee\PSXBuild and the owner's editor does not. A
# process whose CommandLine we cannot read is counted IN, because the failure
# that matters is calling a job done while it is still running -- two jobs on
# one sandbox is the trap unity-wait.ps1 exists for. A watched (GUI) editor
# carries the same -projectPath, so it is found the same way.
function Get-UnityProcs([string]$ProjectPath) {
    $procs = @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { return @() }
    # The leaf as a WHOLE path segment: "PSXBuild" must not match the Charlotte
    # branch's C:\Users\mcgee\PSXBuildWebGL output folder (2026-09-28: a camera
    # job sat waiting on that branch's WebGL build).
    $leaf = [regex]::Escape((Split-Path -Leaf $ProjectPath))
    $rx = "[\\/]$leaf(?=[\\/`"'\s]|$)"
    @($procs | Where-Object { $null -eq $_.CommandLine -or $_.CommandLine -match $rx })
}

function Get-UnityPids([string]$ProjectPath) {
    @(Get-UnityProcs $ProjectPath | ForEach-Object { $_.ProcessId })
}

# Watch the play tests unless told not to: -NoWatch on the tool, or
# $env:PSX_WATCH = '0'.
function Test-PSXWatch([switch]$NoWatch) {
    return ((-not $NoWatch) -and ($env:PSX_WATCH -ne '0'))
}

# A watched editor opens the project in a window, so it must never be the
# owner's own project or a source checkout: only a sandbox (a robocopy of
# Assets/Packages/ProjectSettings, which has neither .git nor tools\).
function Test-SandboxPath([string]$Path) {
    if ([string]::IsNullOrEmpty($Path)) { return $false }
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if ($full -ieq 'C:\Users\mcgee\PSX Racing') { return $false }
    if (Test-Path -LiteralPath (Join-Path $full '.git')) { return $false }
    if (Test-Path -LiteralPath (Join-Path $full 'tools\unity-wait.ps1')) { return $false }
    return $true
}

# The job's arguments for a visible editor: the same list without -batchmode
# and -nographics, plus the flags that keep a GUI editor from asking questions.
function ConvertTo-WatchArgs([string[]]$UnityArgs) {
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($a in $UnityArgs) {
        if ($a -ieq '-batchmode' -or $a -ieq '-nographics') { continue }
        $out.Add($a)
    }
    foreach ($f in @('-ignoreCompilerErrors', '-skipUpgradeDialogs', '-accept-apiupdate')) {
        if (-not @($out | Where-Object { $_ -ieq $f }).Count) { $out.Add($f) }
    }
    return ,$out.ToArray()
}

function Initialize-PSXWatchWin {
    if ('PSXWatchWin' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class PSXWatchWin
{
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern void SwitchToThisWindow(IntPtr h, bool alt);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    struct RECT { public int Left, Top, Right, Bottom; }

    public static string Text(IntPtr h) { var s = new StringBuilder(1024); GetWindowText(h, s, s.Capacity); return s.ToString(); }
    public static string Class(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, s.Capacity); return s.ToString(); }
    public static bool Owned(IntPtr h) { return GetWindow(h, 4) != IntPtr.Zero; }   // GW_OWNER
    public static long Area(IntPtr h) { RECT r; if (!GetWindowRect(h, out r)) return 0; return (long)(r.Right - r.Left) * (r.Bottom - r.Top); }

    // Visible top-level windows that belong to these processes.
    public static IntPtr[] TopLevel(int[] pids)
    {
        var set = new HashSet<int>(pids);
        var list = new List<IntPtr>();
        EnumWindows(delegate (IntPtr h, IntPtr l) {
            uint p; GetWindowThreadProcessId(h, out p);
            if (set.Contains((int)p) && IsWindowVisible(h)) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }

    // Text of the visible child controls of one class ("Button", "Static").
    public static string[] Children(IntPtr parent, string cls)
    {
        var list = new List<string>();
        EnumChildWindows(parent, delegate (IntPtr h, IntPtr l) {
            if (IsWindowVisible(h) && Class(h) == cls) { var t = Text(h); if (t.Length > 0) list.Add(t.Replace("\r", " ").Replace("\n", " ")); }
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }

    // Text of every visible child control that is not a button (the message).
    public static string[] Message(IntPtr parent)
    {
        var list = new List<string>();
        EnumChildWindows(parent, delegate (IntPtr h, IntPtr l) {
            if (IsWindowVisible(h) && Class(h) != "Button") { var t = Text(h); if (t.Length > 0) list.Add(t.Replace("\r", " ").Replace("\n", " ")); }
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }

    public static bool HasChild(IntPtr parent, string cls)
    {
        bool found = false;
        EnumChildWindows(parent, delegate (IntPtr h, IntPtr l) { if (Class(h) == cls) { found = true; return false; } return true; }, IntPtr.Zero);
        return found;
    }
}
'@
}

# The editor's main window among a job's windows: the biggest visible,
# unowned, non-dialog window whose title names Unity (or the project).
function Find-WatchMainWindow([int[]]$Pids, [string]$Leaf) {
    $best = [IntPtr]::Zero; $bestArea = 0
    foreach ($h in [PSXWatchWin]::TopLevel($Pids)) {
        if ([PSXWatchWin]::Owned($h)) { continue }
        if ([PSXWatchWin]::Class($h) -eq '#32770') { continue }
        $t = [PSXWatchWin]::Text($h)
        if (-not ($t -match 'Unity\s+\d' -or ($Leaf -and $t -like "*$Leaf*"))) { continue }
        $a = [PSXWatchWin]::Area($h)
        if ($a -gt $bestArea) { $best = $h; $bestArea = $a }
    }
    return $best
}

# Question dialogs: a standard Windows dialog (#32770, which is what Unity's
# EditorDialog and message boxes are) that has buttons and is not a progress
# bar. Returns hashtables {Handle, Text}.
function Find-WatchDialogs([int[]]$Pids) {
    $found = @()
    foreach ($h in [PSXWatchWin]::TopLevel($Pids)) {
        if ([PSXWatchWin]::Class($h) -ne '#32770') { continue }
        if ([PSXWatchWin]::HasChild($h, 'msctls_progress32')) { continue }
        $buttons = @([PSXWatchWin]::Children($h, 'Button'))
        if ($buttons.Count -eq 0) { continue }
        $text = (@([PSXWatchWin]::Message($h)) -join ' ').Trim()
        if ($text.Length -gt 300) { $text = $text.Substring(0, 300) + '...' }
        $found += @{ Handle = $h; Text = "'$([PSXWatchWin]::Text($h))': $text [buttons: $($buttons -join ' / ')]" }
    }
    return $found
}

# Maximise the editor and put it in front of whatever has the focus.
function Show-WatchWindow([IntPtr]$Handle) {
    if ($Handle -eq [IntPtr]::Zero) { return }
    try {
        if (-not [PSXWatchWin]::IsZoomed($Handle)) { [void][PSXWatchWin]::ShowWindow($Handle, 3) }   # SW_MAXIMIZE
        [void][PSXWatchWin]::SetForegroundWindow($Handle)
        if ([PSXWatchWin]::GetForegroundWindow() -ne $Handle) { [PSXWatchWin]::SwitchToThisWindow($Handle, $true) }
        Start-Sleep -Milliseconds 500   # activation is asynchronous
        # Windows may refuse to hand the focus over while you are typing in
        # another window (it flashes the taskbar button instead); the editor
        # is maximised and visible either way.
        $front = [PSXWatchWin]::GetForegroundWindow() -eq $Handle
        Write-Host ("WATCH: editor window maximised {0}, in front {1}" -f [PSXWatchWin]::IsZoomed($Handle), $front)
    } catch { Write-Host "WATCH: could not bring the editor forward: $($_.Exception.Message)" }
}

# New text in a log Unity still has open (shared read), from the last offset.
function Read-WatchLog([hashtable]$W, [string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return "" }
    try {
        $fs = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]'ReadWrite, Delete')
        try {
            $len = $fs.Length
            if ($len -lt $W.Offset) { $W.Offset = 0 }
            if ($len -eq $W.Offset) { return "" }
            [void]$fs.Seek($W.Offset, [IO.SeekOrigin]::Begin)
            $buf = New-Object byte[] ([int]($len - $W.Offset))
            $n = $fs.Read($buf, 0, $buf.Length)
            $W.Offset += $n
            return [Text.Encoding]::UTF8.GetString($buf, 0, $n)
        } finally { $fs.Dispose() }
    } catch { return "" }
}

function Get-WatchLogTail([string]$Path, [int]$Lines = 30) {
    $w = @{ Offset = 0 }
    try {
        if (Test-Path -LiteralPath $Path) { $w.Offset = [Math]::Max(0, (Get-Item -LiteralPath $Path).Length - 65536) }
    } catch {}
    $text = Read-WatchLog $w $Path
    if (-not $text) { return @("(no log at $Path)") }
    # Blank lines and compiler WARNINGS drown the lines that matter.
    return @($text -split "`r?`n" | Where-Object { $_.Trim() -and $_ -notmatch ': warning (CS|UAC)\d+' } | Select-Object -Last $Lines)
}

# One poll of a watched job: the log (markers, compile errors, a failed
# -executeMethod, a stall) and the windows (the editor's, and any dialog).
# Returns $null to keep waiting, or the reason to close the editor.
function Step-Watch([hashtable]$W, [int[]]$Pids, [string]$Log, [int]$StallMinutes, [int]$DialogSeconds) {
    $now = Get-Date
    $new = Read-WatchLog $W $Log
    if ($new) {
        $W.LastGrowth = $now
        $text = $W.Carry + $new
        $lines = @($text -split "`n")
        $W.Carry = $lines[$lines.Count - 1]
        for ($i = 0; $i -lt $lines.Count - 1; $i++) {
            $line = $lines[$i].TrimEnd("`r")
            if ($line -match 'error CS\d{4}') {
                if ($W.Errors.Count -lt 15) { $W.Errors.Add($line) }
                continue
            }
            if ($line -match "executeMethod (class|method) .*(could not be found|threw exception)") {
                return "-executeMethod failed: $line"
            }
            if (-not $W.Played -and $line.Contains('[PSX WATCH] playing')) {
                $W.Played = $true
                Write-Host "WATCH: $($line.Trim())"
                if ($W.Window -ne [IntPtr]::Zero) { Show-WatchWindow $W.Window }
            }
        }
    }
    if ($W.Errors.Count -gt 0) {
        return "compile errors (a visible editor would run the LAST GOOD code):`n  " + ($W.Errors -join "`n  ")
    }

    try {
        if ($W.Window -eq [IntPtr]::Zero -or -not [PSXWatchWin]::IsWindowVisible($W.Window)) {
            $h = Find-WatchMainWindow $Pids $W.Leaf
            if ($h -ne [IntPtr]::Zero) {
                $first = -not $W.SawWindow
                $W.Window = $h; $W.SawWindow = $true
                if ($first) {
                    Write-Host ("WATCH: the editor window is up: '{0}' ({1}, handle 0x{2:X})" -f [PSXWatchWin]::Text($h), [PSXWatchWin]::Class($h), $h.ToInt64())
                    Show-WatchWindow $h
                }
            }
        }
        foreach ($d in (Find-WatchDialogs $Pids)) {
            $key = $d.Handle.ToInt64()
            if (-not $W.Dialogs.ContainsKey($key)) {
                $W.Dialogs[$key] = $now
                Write-Host "WATCH: a dialog is up - $($d.Text)"
            } elseif ((($now - $W.Dialogs[$key]).TotalSeconds -gt $DialogSeconds) -and
                      (($now - $W.LastGrowth).TotalSeconds -gt $DialogSeconds)) {
                return "a dialog has blocked the editor for over $DialogSeconds s: $($d.Text)"
            }
        }
    } catch { }

    if (($now - $W.LastGrowth).TotalMinutes -gt $StallMinutes) {
        return "the log has not moved for $StallMinutes minutes (a stalled editor)"
    }
    return $null
}

# Close a watched editor: ask its window first (a clean exit leaves no lock
# behind), then force whatever of the job is still there after 20 s, and wait
# for the lot - the editor's import worker outlives it by a few seconds - so
# the next job does not start beside a leftover. Only processes this job
# started (not in $Before), matched by the sandbox's own path; never the
# owner's editor.
function Stop-WatchedEditor([string]$Project, $Before) {
    $job = { @(Get-UnityPids $Project | Where-Object { $Before -notcontains $_ }) }
    foreach ($id in (& $job)) {
        try {
            $p = Get-Process -Id $id -ErrorAction Stop
            if ($p.MainWindowHandle -ne [IntPtr]::Zero) { [void]$p.CloseMainWindow() }
        } catch { }
    }
    $until = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $until) {
        if (-not (& $job).Count) { Write-Host "WATCH: the editor closed"; return }
        Start-Sleep -Seconds 2
    }
    foreach ($id in (& $job)) {
        Write-Host "WATCH: the editor did not close - killing PID $id"
        Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
    }
    $until = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $until -and (& $job).Count) { Start-Sleep -Seconds 2 }
    $left = & $job
    if ($left.Count) { Write-Host "WATCH: still running after the kill: PID $($left -join ', ')" }
}

# Wait until no Unity is working on this project. A job that TIMED OUT is still
# running (it is never killed: killing an editor mid-import corrupts the
# artifact database), so a retry must wait for it rather than start a second
# editor on the same sandbox or trip over its locked log.
function Wait-ProjectIdle([string]$ProjectPath, [int]$MaxMinutes = 60) {
    $deadline = (Get-Date).AddMinutes($MaxMinutes)
    $said = $false
    while ((Get-Date) -lt $deadline) {
        if (@(Get-UnityPids $ProjectPath).Count -eq 0) { return $true }
        if (-not $said) { Write-Host "waiting for the Unity job still running on $ProjectPath to finish..."; $said = $true }
        Start-Sleep -Seconds 10
    }
    return $false
}

function Invoke-UnityJob {
    param(
        [Parameter(Mandatory = $true)][string[]]$UnityArgs,
        [Parameter(Mandatory = $true)][string]$Log,
        [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe",
        [int]$MaxMinutes = 40,
        [string]$Project = $(if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }),
        # A play test: open a VISIBLE editor instead of a hidden batch one
        # (see "Watching the tests" at the top). Callers pass
        # -Watch:(Test-PSXWatch -NoWatch:$NoWatch).
        [switch]$Watch,
        [int]$StallMinutes = $(if ($env:PSX_WATCH_STALL_MINUTES) { [int]$env:PSX_WATCH_STALL_MINUTES } else { 10 }),
        [int]$DialogSeconds = $(if ($env:PSX_WATCH_DIALOG_SECONDS) { [int]$env:PSX_WATCH_DIALOG_SECONDS } else { 90 })
    )

    if (Test-Path $Log) { Remove-Item $Log -Force }
    $script:UnityJobExitCode = $null

    if ($Watch) {
        $target = $Project
        for ($i = 0; $i -lt $UnityArgs.Count - 1; $i++) {
            if ($UnityArgs[$i] -ieq '-projectPath') { $target = $UnityArgs[$i + 1] }
        }
        if (-not (Test-SandboxPath $target)) {
            Write-Host "WATCH REFUSED: $target is not a sandbox (the owner's project or a source checkout) - running it hidden in batch mode"
            $Watch = $false
        }
    }

    $before = @(Get-UnityPids $Project)
    if ($Watch) {
        Initialize-PSXWatchWin
        $UnityArgs = ConvertTo-WatchArgs $UnityArgs
        $savedActive = $env:PSX_WATCH_ACTIVE
        $savedReporter = $env:UNITY_DONOTSTARTBUGREPORTER
        # Set for the one process launched here (the environment is copied at
        # launch), then put back so nothing else inherits it.
        $env:PSX_WATCH_ACTIVE = '1'
        $env:UNITY_DONOTSTARTBUGREPORTER = '1'
        try { Start-Process -FilePath $Unity -ArgumentList $UnityArgs -WindowStyle Maximized | Out-Null }
        finally { $env:PSX_WATCH_ACTIVE = $savedActive; $env:UNITY_DONOTSTARTBUGREPORTER = $savedReporter }
        Write-Host "WATCH: opening $target in a visible Unity editor (-NoWatch or PSX_WATCH=0 runs it hidden)"
    } else {
        Start-Process -FilePath $Unity -ArgumentList $UnityArgs -WindowStyle Hidden | Out-Null
    }

    # Phase one: wait for a Unity process that was not there before. Up to two
    # minutes, because a cold sandbox spends that long opening the project
    # before it is visible as a child at all.
    $appeared = $false
    $spawnDeadline = (Get-Date).AddMinutes(2)
    while ((Get-Date) -lt $spawnDeadline) {
        Start-Sleep -Seconds 2
        $now = @(Get-UnityPids $Project)
        if (@($now | Where-Object { $before -notcontains $_ })) { $appeared = $true; break }
    }
    if (-not $appeared) {
        Write-Host "UNITY NEVER STARTED - check the argument list (a path with a space must be ONE quoted string)"
        return $false
    }

    # Hold a handle on every process of the job as it appears, so its exit
    # code can still be read after it is gone (EditorApplication.Exit(n) in a
    # harness lands here). The editor is the one that is not an import worker.
    $held = @{}
    $w = @{ Offset = 0; Carry = ""; LastGrowth = Get-Date; Window = [IntPtr]::Zero; SawWindow = $false;
            Played = $false; Dialogs = @{}; Errors = (New-Object System.Collections.Generic.List[string]);
            Leaf = (Split-Path -Leaf $Project) }

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
        $procs = @(Get-UnityProcs $Project | Where-Object { $before -notcontains $_.ProcessId })
        foreach ($pr in $procs) {
            if ($held.ContainsKey($pr.ProcessId)) { continue }
            try {
                $p = Get-Process -Id $pr.ProcessId -ErrorAction Stop
                $null = $p.Handle
                $held[$pr.ProcessId] = @{ Proc = $p; Worker = ($pr.CommandLine -like '*AssetImportWorker*' -or $pr.CommandLine -like '*-adb2*') }
            } catch { }
        }
        if ($procs.Count) {
            $quiet = 0
            if ($Watch) {
                $pids = @($procs | ForEach-Object { [int]$_.ProcessId })
                $why = Step-Watch $w $pids $Log $StallMinutes $DialogSeconds
                if ($why) {
                    Write-Host "WATCH: closing the editor - $why"
                    Write-Host "Tail of ${Log}:"
                    Get-WatchLogTail $Log 30 | ForEach-Object { Write-Host "  $_" }
                    Stop-WatchedEditor $Project $before
                    return $false
                }
            }
        } else {
            $quiet++
            if ($quiet -ge 6) {
                # The editor's code: the last non-worker to exit (the launcher
                # exits first, with its own).
                $lastExit = [DateTime]::MinValue
                foreach ($k in $held.Keys) {
                    $e = $held[$k]
                    if ($e.Worker) { continue }
                    try {
                        if ($e.Proc.HasExited -and $e.Proc.ExitTime -ge $lastExit) {
                            $lastExit = $e.Proc.ExitTime
                            $script:UnityJobExitCode = $e.Proc.ExitCode
                        }
                    } catch { }
                }
                if ($Watch) {
                    if (-not $w.SawWindow) { Write-Host "WATCH: the editor never showed a window" }
                    Write-Host "WATCH: the editor closed by itself (exit code $script:UnityJobExitCode)"
                }
                return $true
            }
        }
        Start-Sleep -Seconds 5
    }
    if ($Watch) {
        # Still writing its log = still working (a cold sandbox can import for
        # longer than the check's budget): leave it, it quits itself at the end,
        # just as a batch job past its deadline does. A silent one is stuck.
        if (((Get-Date) - $w.LastGrowth).TotalMinutes -lt 2) {
            Write-Host "UNITY TIMED OUT after $MaxMinutes minutes, but the watched editor is still writing its log - left running; it closes itself when the check ends. See $Log"
            return $false
        }
        Write-Host "WATCH: closing the editor - it ran past $MaxMinutes minutes and its log has stopped"
        Write-Host "Tail of ${Log}:"
        Get-WatchLogTail $Log 30 | ForEach-Object { Write-Host "  $_" }
        Stop-WatchedEditor $Project $before
        return $false
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
        if (-not (Wait-ProjectIdle $Proj)) {
            Write-Host "a Unity job has held $Proj for an hour - not starting a second one"
            return $false
        }
        # 75 minutes: a pass that also re-imports ~1700 assets overran 40 and was
        # still building (2026-09-28).
        Invoke-UnityJob -MaxMinutes 75 -Log "$Proj\scenebuild.log" -UnityArgs @(
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
