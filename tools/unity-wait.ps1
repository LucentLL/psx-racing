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
# editor itself, live. When play starts the editor is raised above your other
# windows, maximised, with the Game view at 16:9 and Play Maximized so the game
# fills the window; the editor closes by itself when the check is done, and
# the tool prints the same result lines it always has.
#
#   powershell -ExecutionPolicy Bypass -File tools\race-play-check.ps1 -Venue BlueRidge -Seconds 60
#   ... -NoWatch                 the old way: hidden, -batchmode -nographics
#   ... -MaxMinutes 90           a bigger budget (a cold sandbox imports for an hour)
#   $env:PSX_WATCH = '0'         every play test hidden, for the whole session
#
# YOUR KEYBOARD STAYS YOURS. The editor is put in front WITHOUT being activated
# (it is lifted above the other windows; nothing asks Windows for the focus),
# so whatever you were typing into keeps the keyboard. Unity itself grabs the
# focus at its splash and at play when Windows lets it (after a few idle
# minutes); when that happens with no key or click since, the waiter hands the
# keyboard back to the window that had it and lifts the editor over it again
# (a click into the editor is yours and is left alone). And while the test
# plays, this PC's own keyboard, mouse, pads and touch screen are switched off
# inside the game (Assets\PSXRacing\Editor\PlayCheckWatch.cs), so nothing you
# press - Escape, which would pause the race; a click, which grabs the cursor
# in the walk test; a pad button - reaches the check, even if you click into
# the editor. The harnesses' own virtual pads keep working. If the editor is
# still behind something 30 s after play starts, the tool says so ("click Unity
# on the taskbar") and flashes its taskbar button.
#
# What watching changes: no -batchmode / -nographics, a normal window, and
# PSX_WATCH_ACTIVE=1 for that one editor, which switches on PlayCheckWatch
# (Game view framing, audio unmuted, input off in play, a heartbeat). What it
# does not change: the checks, the seeds, the scenes, the report file and the
# lines the tool prints. Two things a window cannot keep the same:
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
# exactly fits: that is one game pixel per screen pixel, not a crop (the view's
# own numbers, 2026-09-28: a 2233x1256 frame at x 163.5 in a 2560x1256 view).
# A screen grab by a DPI-UNAWARE process (Windows PowerShell's CopyFromScreen)
# copies only the top-left two thirds of a 150% screen and looks cropped at the
# right and bottom; call SetProcessDPIAware first.
#
# Unattended safety, because a visible editor can ask questions batch mode never
# does. Flags: -ignoreCompilerErrors (no Safe Mode prompt), -skipUpgradeDialogs
# (no "project from another version" prompt), -accept-apiupdate (no API updater
# prompt); UNITY_DONOTSTARTBUGREPORTER=1 (a crash exits, no bug reporter).
# "Alive" means the editor's MAIN LOOP is working: PlayCheckWatch logs
# "[PSX WATCH] alive" every 30 s from EditorApplication.update, and any log
# line counts too EXCEPT the ones Unity's background threads write whatever
# the main thread is doing (licensing, ADB scans) - those used to keep a
# stuck editor looking busy. The waiter asks the editor to close, with the
# log's tail printed, when:
#   - the log shows compile errors (a visible editor would otherwise run the
#     LAST GOOD assemblies, i.e. a stale test), or -executeMethod failed;
#   - a Windows dialog with buttons stays up longer than PSX_WATCH_DIALOG_SECONDS
#     (default 90) while the editor is not alive - you can answer it yourself;
#   - the editor has not been alive for PSX_WATCH_STALL_MINUTES (default 10) -
#     except mid-import, where a quiet log is a long import step (the FMOD
#     bank build writes nothing for minutes): that is noted, not closed;
#   - the job outlives its budget. -MaxMinutes alone does NOT close a watched
#     editor (a cold sandbox imports for longer than a check's budget; it is
#     still watched, by every rule above): it is closed once play mode itself
#     has run -MaxMinutes without the check ending, or at PSX_WATCH_HARD_MINUTES
#     (default -MaxMinutes + 60) whatever it is doing;
#   - the tool itself is stopped (Ctrl+C).
#
# CLOSING, AND NEVER KILLING MID-IMPORT. Killing an editor while it imports
# corrupts the artifact database (the rule for every Unity job here: a batch
# job that times out is left running, never killed). So a watched editor is
# asked first - a stop file that PlayCheckWatch reads on the next free tick of
# the main loop (never inside an import) and answers with EditorApplication.
# Exit(3), plus a close of its window - and is killed 20 s later ONLY if its
# log has said for at least 10 s that it is not importing. An import or a
# compile begins with a line like "Initial Refresh Start", "Importing 'GUID:
# ..." or "Begin MonoManager ReloadAssembly", and ends at "Initial Refresh
# End", "Asset Pipeline Refresh (id=...): Total" or a line from PlayCheckWatch's
# own tick; an editor counts as importing from its launch until the first such
# end (the 10 s: back-to-back imports put an end line between them and start
# the next within a second). A visible editor also imports in the BACKGROUND
# while its main loop runs (the search indexer hands assets to the import
# workers: "[Worker1] Imported GUID: ..." lines, heartbeats and all), so any
# such line in the last 30 s counts as importing too. An editor that is
# importing is waited for instead: the window close it was sent waits in its
# queue and is honoured the moment the import ends (sandbox probe, 2026-09-28:
# a 150 s forced reimport, asked to close 10 s in, shut down cleanly within a
# second of its last import, both with the tool waiting and after a Ctrl+C);
# failing that the stop file closes it on its first free tick; and it is killed
# 20 s after the import ends only if neither has.
# The trade-off, in both directions:
#   - A rule that fires mid-import (compile errors, which a cold sandbox shows
#     in its first minutes and then imports for most of an hour; a dialog; the
#     hard budget) costs the rest of that import: the tool waits for it, up to
#     the hard budget, and prints what the editor is importing.
#   - An editor still importing at the hard budget, or when the tool is
#     stopped with Ctrl+C (which never waits for an import), is LEFT RUNNING
#     with its PID printed. It closes itself when the import ends; until then
#     the next watched run on the sandbox refuses to start beside it, and a
#     scene build waits for it (Wait-ProjectIdle).
#   - An editor HUNG inside an import (or before its first import has ended)
#     is never killed by the tool: it is left running at the hard budget, and
#     only you can close it. Everywhere else - playing, idle, holding a
#     dialog, hung after its import - it is killed when it will not close.
# The tool does not return while a watched editor runs, except in those two
# left-running cases, which it says in capitals ("LEFT RUNNING").
# And before it opens one: a watched job never starts beside another Unity
# already working on the same sandbox, and it clears the scene backups a
# killed editor leaves in Temp\__Backupscenes - with them there, the next
# visible editor stops on a "Recovering Scene Backups" Yes/No question before
# it compiles anything (seen 2026-09-28, the run after a watchdog kill).
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
# carries the same -projectPath, so it is found the same way. -Strict counts
# only what we can SEE is the sandbox's: the set a watched job raises, and the
# set it is allowed to close or kill, so an unreadable process (which could be
# the owner's own editor) is never touched.
function Get-UnityProcs([string]$ProjectPath, [switch]$Strict) {
    $procs = @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { return @() }
    # The leaf as a WHOLE path segment: "PSXBuild" must not match the Charlotte
    # branch's C:\Users\mcgee\PSXBuildWebGL output folder (2026-09-28: a camera
    # job sat waiting on that branch's WebGL build).
    $leaf = [regex]::Escape((Split-Path -Leaf $ProjectPath))
    $rx = "[\\/]$leaf(?=[\\/`"'\s]|$)"
    @($procs | Where-Object { ($null -eq $_.CommandLine -and -not $Strict) -or $_.CommandLine -match $rx })
}

function Get-UnityPids([string]$ProjectPath, [switch]$Strict) {
    @(Get-UnityProcs $ProjectPath -Strict:$Strict | ForEach-Object { $_.ProcessId })
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

# A KILLED EDITION BUILD'S PARK, PUT BACK BEFORE ANYTHING TOUCHES THE SANDBOX.
#
# An edition WebGL build (build-and-publish -Edition MAIN|CITY) moves the other
# edition's Resources out to Assets\PSXRacing\_EditionParked for the length of
# one BuildPlayer call, listing them first in <sandbox>\PSXEditionParked.json
# (EditionParking in Assets\PSXRacing\Editor\EditionBuild.cs). A build that is
# killed leaves them there. The editor's own start-up restore is a delayCall,
# which a batch -executeMethod job runs its method before (2026-09-29: it
# never fired; the park was put back by hand) - and every tool here copies
# into the sandbox BEFORE it starts Unity. A full /MIR is harmless (it deletes
# the park folder and Resources come back from the source), but the additive
# /E /XO copies of Resources (verify -NoMirror, city-play-check, city-cycle,
# city-ground-probe, wall-scrape-play) put the source's charlotte_* files and
# CityProps metas back BESIDE the parked copies with the same GUIDs: Unity
# re-GUIDs one side, the restore finds every path occupied and leaves every
# item stuck, and the check that follows runs against half of Resources.
#
# So this runs FIRST: when a tool dot-sources this file (before its first
# robocopy - see the end of the file) and again before every Unity job. A
# manifest found with no Unity running on the sandbox is restored by a short
# batch job (EditionParking.RecoverIfNeeded, which every parking version of
# the code has); one found WITH a job running is that job's own live park,
# waited for, never touched. A park that cannot be put back is STUCK and the
# tool stops here, naming the items: a person has to look, because the park
# folder may hold the only copy of something.
function Assert-EditionParkClear([string]$ProjectPath) {
    if ([string]::IsNullOrEmpty($ProjectPath)) { return }
    $m = Join-Path $ProjectPath 'PSXEditionParked.json'
    if (-not (Test-Path -LiteralPath $m)) { return }
    if (@(Get-UnityPids $ProjectPath).Count) {
        Write-Host "EDITION PARK: $m exists and a Unity job is running on $ProjectPath - waiting for it (a running edition build restores its own park)" -ForegroundColor Yellow
        if (-not (Wait-ProjectIdle $ProjectPath 120)) {
            throw "EDITION PARK: a Unity job has held $ProjectPath for two hours with Resources parked - not touching the sandbox"
        }
        if (-not (Test-Path -LiteralPath $m)) { return }
    }
    $items = @(Get-Content -LiteralPath $m | ForEach-Object { $_.Trim() } | Where-Object { $_ -and $_ -notmatch '^(edition|pid) ' })
    if (-not (Test-SandboxPath $ProjectPath)) {
        throw "EDITION PARK: $m exists in $ProjectPath, which is not a sandbox - not starting a job there. Put $($items -join ', ') back from Assets\PSXRacing\_EditionParked by hand."
    }
    $parkDir = Join-Path $ProjectPath 'Assets\PSXRacing\_EditionParked'
    $held = @($items | Where-Object { Test-Path -LiteralPath (Join-Path $parkDir $_) })
    Write-Host ("EDITION PARK: an edition build on $ProjectPath did not finish (manifest: " + ($items -join ', ') +
                "; still in the park: " + $(if ($held.Count) { $held -join ', ' } else { "nothing" }) +
                "). Putting Resources back BEFORE anything is copied into the sandbox...") -ForegroundColor Yellow
    $log = Join-Path $ProjectPath 'parkrecover.log'
    Invoke-UnityJob -NoParkCheck -MaxMinutes 45 -Project $ProjectPath -Log $log -UnityArgs @(
        "-quit","-batchmode","-nographics","-projectPath",$ProjectPath,
        "-executeMethod","PSXRacing.EditorTools.EditionParking.RecoverIfNeeded",
        "-logFile",$log,"-accept-apiupdate") | Out-Null
    if (Test-Path -LiteralPath $m) {
        Select-String -LiteralPath $log -Pattern '\[EditionParking\]|error CS' -ErrorAction SilentlyContinue |
            Select-Object -Last 12 | ForEach-Object { Write-Host "  $($_.Line.Trim())" }
        $still = @($items | Where-Object { Test-Path -LiteralPath (Join-Path $parkDir $_) })
        $back = @($items | Where-Object { $still -notcontains $_ })
        $what = if ($still.Count) { "still parked: " + ($still -join ', ') } else { "the park folder is not empty" }
        if ($back.Count) { $what += " (put back: " + ($back -join ', ') + ")" }
        throw ("EDITION PARK STUCK on ${ProjectPath}: $what - see $log. " +
               "Nothing was copied and no job was started. Compare Assets\PSXRacing\_EditionParked with " +
               "Assets\PSXRacing\Resources in the sandbox and put each item back by hand - or, when the source " +
               "has everything, delete the park folder (with its .meta) and PSXEditionParked.json, then run " +
               "tools\verify.ps1 without -NoMirror (a full mirror and a scene build).")
    }
    Write-Host "EDITION PARK: restored - Resources are whole again (see $log)" -ForegroundColor Green
}

# EVERY UNITY JOB NAMES ITS BUILD TARGET (the colour pass, 2026-09-29).
#
# A texture is imported for the editor's ACTIVE build target, and the target a
# sandbox has is whatever its last job left it on. The release budget
# (ReleaseBudget.SixteenBitCandidate, fc551ef) gives ~926 textures a WebGL-only
# RGB565 override, and WebGL2 has no sRGB 565: on the WebGL target those
# texels are read AS LINEAR (asphalt up to x9 brighter). The sandboxes build
# with -buildTarget WebGL, and every shot tool launched with no -buildTarget,
# so after 2026-09-27 every editor frame silently carried the WebGL import -
# and nothing in a PNG said which import it was. So: every job launched
# through here passes -buildTarget. WebGL, the target that ships, is the
# default; $env:PSX_BUILD_TARGET (e.g. StandaloneWindows64, the decoded import
# the colour BASELINE is rendered with) or a -buildTarget already in the
# caller's arguments wins. The job's frames record it (ShotSidecar: a JSON
# beside every PNG with activeBuildTarget and a probe texture's format).
# Switching a sandbox's target reimports its textures once (minutes); the
# artifact cache makes switching back cheap.
function Get-PSXBuildTarget {
    if ($env:PSX_BUILD_TARGET) { return $env:PSX_BUILD_TARGET }
    return 'WebGL'
}

function Add-PSXBuildTarget([string[]]$UnityArgs, [string]$Target = "") {
    if (@($UnityArgs | Where-Object { $_ -ieq '-buildTarget' }).Count) { return ,$UnityArgs }
    if (-not $Target) { $Target = Get-PSXBuildTarget }
    return ,(@($UnityArgs) + @('-buildTarget', $Target))
}

# The commit the frames are rendered from, for their sidecars: the source
# checkout this tools\ folder belongs to (a sandbox has no .git), with
# "+dirty" when it has uncommitted changes to the code that renders.
function Get-PSXSourceCommit {
    try {
        $root = Split-Path -Parent $PSScriptRoot
        $h = (& git -C $root rev-parse --short HEAD 2>$null)
        if (-not $h) { return "" }
        $d = (& git -C $root status --porcelain -- Assets/PSXRacing/Scripts Assets/PSXRacing/Editor Assets/PSXRacing/Shaders 2>$null)
        if ($d) { return "$h+dirty" }
        return "$h"
    } catch { return "" }
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
    if ('PSXWatchWindows' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class PSXWatchWindows
{
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASHWINFO fi);

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)] struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO p);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);

    // Milliseconds since the last key press or mouse move anywhere on the desk.
    public static uint IdleMs()
    {
        var l = new LASTINPUTINFO(); l.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
        if (!GetLastInputInfo(ref l)) return 0;
        return unchecked((uint)Environment.TickCount - l.dwTime);
    }

    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10,
               SWP_NOOWNERZORDER = 0x200, SWP_ASYNCWINDOWPOS = 0x4000;
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_NOTOPMOST = new IntPtr(-2);

    public static string Text(IntPtr h) { var s = new StringBuilder(1024); GetWindowText(h, s, s.Capacity); return s.ToString(); }
    public static string Class(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, s.Capacity); return s.ToString(); }
    public static bool Owned(IntPtr h) { return GetWindow(h, 4) != IntPtr.Zero; }   // GW_OWNER
    public static long Area(IntPtr h) { RECT r; if (!GetWindowRect(h, out r)) return 0; return (long)(r.Right - r.Left) * (r.Bottom - r.Top); }
    public static int Pid(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return (int)p; }
    public static bool IsTopmost(IntPtr h) { return (GetWindowLong(h, -20) & 0x8) != 0; }   // GWL_EXSTYLE, WS_EX_TOPMOST

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

    // Above every other ordinary window WITHOUT activating it, so the keyboard
    // stays where it was. SetForegroundWindow is what Windows refuses a
    // background process (it flashes the taskbar instead), and what it grants
    // takes the keyboard with it; a z-order change is neither. TOPMOST then
    // NOTOPMOST lands the window at the top of the ordinary windows, under the
    // always-on-top ones. Asynchronous, so a busy editor cannot hang the
    // waiter: the result is checked on a later poll (InView).
    public static void Raise(IntPtr h)
    {
        const uint f = SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS;
        if (IsIconic(h)) ShowWindowAsync(h, 4);   // SW_SHOWNOACTIVATE
        SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, f);
        SetWindowPos(h, HWND_NOTOPMOST, 0, 0, 0, 0, f);
    }

    public static void NotTopmost(IntPtr h)
    {
        SetWindowPos(h, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
    }

    // A window that is neither maximised nor covering its monitor's work area
    // is sized to it - not maximised, because a maximise asks for the focus.
    public static bool Fill(IntPtr h)
    {
        if (IsZoomed(h)) return true;
        if (IsIconic(h)) return false;
        var mi = new MONITORINFO(); mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        if (!GetMonitorInfo(MonitorFromWindow(h, 2), ref mi)) return false;   // MONITOR_DEFAULTTONEAREST
        RECT w, a = mi.rcWork;
        if (GetWindowRect(h, out w) && w.Left <= a.Left && w.Top <= a.Top && w.Right >= a.Right && w.Bottom >= a.Bottom) return true;
        return SetWindowPos(h, IntPtr.Zero, a.Left, a.Top, a.Right - a.Left, a.Bottom - a.Top,
                            SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
    }

    // How many of five points spread over the window show one of these
    // processes' windows - what a click there would hit, i.e. what is on
    // screen. coverPid: the first other process found on top.
    public static int InView(IntPtr h, int[] pids, out int coverPid)
    {
        coverPid = 0;
        RECT r; if (!GetWindowRect(h, out r)) return 0;
        var set = new HashSet<int>(pids);
        double[] fx = { 0.5, 0.25, 0.75, 0.25, 0.75 }, fy = { 0.5, 0.3, 0.3, 0.75, 0.75 };
        int hits = 0;
        for (int i = 0; i < 5; i++)
        {
            POINT p; p.X = r.Left + (int)((r.Right - r.Left) * fx[i]); p.Y = r.Top + (int)((r.Bottom - r.Top) * fy[i]);
            IntPtr w = WindowFromPoint(p);
            if (w == IntPtr.Zero) continue;
            IntPtr root = GetAncestor(w, 2);   // GA_ROOT
            int pid = Pid(root != IntPtr.Zero ? root : w);
            if (set.Contains(pid)) hits++;
            else if (coverPid == 0) coverPid = pid;
        }
        return hits;
    }

    // Flash the taskbar button until the window is brought forward.
    public static void Flash(IntPtr h)
    {
        var fi = new FLASHWINFO();
        fi.cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO)); fi.hwnd = h;
        fi.dwFlags = 2 | 12;   // FLASHW_TRAY | FLASHW_TIMERNOFG
        FlashWindowEx(ref fi);
    }
}
'@
}

# The editor's main window among a job's windows: the biggest visible,
# unowned, non-dialog window whose title names Unity (or the project).
function Find-WatchMainWindow([int[]]$Pids, [string]$Leaf) {
    $best = [IntPtr]::Zero; $bestArea = 0
    foreach ($h in [PSXWatchWindows]::TopLevel($Pids)) {
        if ([PSXWatchWindows]::Owned($h)) { continue }
        if ([PSXWatchWindows]::Class($h) -eq '#32770') { continue }
        $t = [PSXWatchWindows]::Text($h)
        if (-not ($t -match 'Unity\s+\d' -or ($Leaf -and $t -like "*$Leaf*"))) { continue }
        $a = [PSXWatchWindows]::Area($h)
        if ($a -gt $bestArea) { $best = $h; $bestArea = $a }
    }
    return $best
}

# Question dialogs: a standard Windows dialog (#32770, which is what Unity's
# EditorDialog and message boxes are) that has buttons and is not a progress
# bar. Returns hashtables {Handle, Text}.
function Find-WatchDialogs([int[]]$Pids) {
    $found = @()
    foreach ($h in [PSXWatchWindows]::TopLevel($Pids)) {
        if ([PSXWatchWindows]::Class($h) -ne '#32770') { continue }
        if ([PSXWatchWindows]::HasChild($h, 'msctls_progress32')) { continue }
        $buttons = @([PSXWatchWindows]::Children($h, 'Button'))
        if ($buttons.Count -eq 0) { continue }
        $text = (@([PSXWatchWindows]::Message($h)) -join ' ').Trim()
        if ($text.Length -gt 300) { $text = $text.Substring(0, 300) + '...' }
        $found += @{ Handle = $h; Text = "'$([PSXWatchWindows]::Text($h))': $text [buttons: $($buttons -join ' / ')]" }
    }
    return $found
}

# How long the waiter keeps lifting the editor after play starts, until it is
# actually on screen.
$script:WatchRaiseSeconds = 30

# A log line that proves the editor is working: anything but a blank line and
# the lines Unity's BACKGROUND threads write whatever the main thread is doing
# (camframeplay.log, 2026-09-28: licensing checks and ADB scans every minute or
# so, through a whole run). The heartbeat is such a line, and so is every line
# of an import, a compile or the game. One test per chunk, not per line: a
# batch-mode-sized log is hundreds of MB.
$script:WatchLifeRx = New-Object Text.RegularExpressions.Regex(
    '(?m)^(?![ \t]*\r?$)(?![ \t]*(\[Licensing::|Android Extension - |Killing ADB server|Curl error))',
    [Text.RegularExpressions.RegexOptions]::Compiled)
$script:WatchErrRx  = New-Object Text.RegularExpressions.Regex('(?m)^[^\r\n]*error CS\d{4}[^\r\n]*')
$script:WatchExecRx = New-Object Text.RegularExpressions.Regex('(?m)^[^\r\n]*executeMethod (class|method) [^\r\n]*(could not be found|threw exception)[^\r\n]*')

# IS THE EDITOR IMPORTING? The one thing that decides whether a watched editor
# may be killed (never mid-import: that corrupts the artifact database). Read
# off the log as a state that the LAST marker sets: an import or a compile
# begins with one of the $WatchBusyMarks, and the editor is free again at one
# of the $WatchIdleMarks - the end of the initial refresh or of any later one,
# or a line from PlayCheckWatch, which writes only from the main loop's own
# tick (no tick runs inside an import). A long single step that writes nothing
# (the FMOD bank build) leaves the last marker a busy one, so it still counts
# as importing. An editor starts out importing: it opens the project and its
# first import before it says anything we can read.
# Seen in the sandbox logs (6000.5.5f1, batch and GUI alike):
#   Application.AssetDatabase Initial Refresh Start ... Initial Refresh End
#   Importing 'GUID: <guid> - Path: <path>' with importer (<Importer>)
#   [ScriptCompilation] Requested script compilation because: ...
#   Begin MonoManager ReloadAssembly
#   Asset Pipeline Refresh (id=<hex>): Total: 8.185 seconds - Initiated by ...
$script:WatchBusyMarks = @('Application.AssetDatabase Initial Refresh Start', "Importing 'GUID: ", 'Start importing ',
                          'DisplayProgressbar: ', '[ScriptCompilation] Requested', 'Begin MonoManager ReloadAssembly')
$script:WatchIdleMarks = @('Application.AssetDatabase Initial Refresh End', 'Asset Pipeline Refresh (id=',
                          '[PSX WATCH] alive', '[PSX WATCH] playing')

function Update-WatchImport([hashtable]$W, [string]$Text) {
    $busy = -1; $idle = -1
    foreach ($k in $script:WatchBusyMarks) { $i = $Text.LastIndexOf($k, [StringComparison]::Ordinal); if ($i -gt $busy) { $busy = $i } }
    foreach ($k in $script:WatchIdleMarks) { $i = $Text.LastIndexOf($k, [StringComparison]::Ordinal); if ($i -gt $idle) { $idle = $i } }
    if ($busy -gt $idle) {
        $a = $Text.LastIndexOf("`n", $busy) + 1
        $b = $Text.IndexOf("`n", $busy); if ($b -lt 0) { $b = $Text.Length }
        $line = $Text.Substring($a, $b - $a).Trim()
        if ($line.Length -gt 160) { $line = $line.Substring(0, 160) + '...' }
        if (-not $W.Importing) { $W.ImportSince = Get-Date }
        $W.Importing = $true; $W.ImportWhat = $line; $W.ImportNoted = $false; $W.IdleSince = $null
    } elseif ($idle -gt $busy) {
        if ($W.Importing -or -not $W.IdleSince) { $W.IdleSince = Get-Date }
        $W.Importing = $false
    }
    # BACKGROUND imports: a visible editor also imports WHILE its main loop
    # runs - the search indexer's "Initial Indexing" hands every asset to the
    # import workers, heartbeats and all (sandbox probe, 2026-09-28: 3776
    # "[Worker1] Imported GUID: ... ScriptedImporter/UnityEditor.
    # QuickSearchModule" lines after the project had opened). Batch mode never
    # does this. They write artifacts too, so a line of theirs in the last
    # $WatchBgSeconds counts as importing.
    $m = $script:WatchBgRx.Match($Text)
    if ($m.Success) {
        $W.BgAt = Get-Date
        $W.BgWhat = if ($m.Value.Length -gt 160) { $m.Value.Substring(0, 160) + '...' } else { $m.Value.Trim() }
    }
}

$script:WatchBgRx = New-Object Text.RegularExpressions.Regex('(?m)^\[Worker\d+\] [^\r\n]*',
    ([Text.RegularExpressions.RegexOptions]::RightToLeft -bor [Text.RegularExpressions.RegexOptions]::Compiled))
$script:WatchBgSeconds = 30

function Test-WatchBgImport([hashtable]$W) {
    return ($W.BgAt -and ((Get-Date) - $W.BgAt).TotalSeconds -lt $script:WatchBgSeconds)
}

# Importing now, in the foreground (the main loop's) or the background?
function Test-WatchImporting([hashtable]$W) {
    return ($W.Importing -or (Test-WatchBgImport $W))
}

# May the editor be killed? Only once the log has said "not importing" for
# 10 s: one ImportAsset after another (a harness's, or AssetDatabase work in a
# loop) puts an "Asset Pipeline Refresh ... Total" line between them and starts
# the next import well under a second later (seen in a sandbox probe,
# 2026-09-28), so a single idle reading is not enough. And no background import
# in the last $WatchBgSeconds.
function Test-WatchKillable([hashtable]$W) {
    return ((-not $W.Importing) -and $W.IdleSince -and ((Get-Date) - $W.IdleSince).TotalSeconds -ge 10 -and
            -not (Test-WatchBgImport $W))
}

# Whole new lines of the log (a partial last line waits for the next poll),
# with the signs of life and the import state taken from them.
function Update-WatchLog([hashtable]$W, [string]$Log) {
    $new = Read-WatchLog $W $Log
    if (-not $new) { return "" }
    $text = $W.Carry + $new
    $cut = $text.LastIndexOf("`n")
    if ($cut -lt 0) { $W.Carry = $text; $text = "" }
    else { $W.Carry = $text.Substring($cut + 1); $text = $text.Substring(0, $cut + 1) }
    if ($W.Carry.Length -gt 65536) { $W.Carry = "" }
    if ($text) {
        if ($script:WatchLifeRx.IsMatch($text)) { $W.LastLife = Get-Date }
        Update-WatchImport $W $text
    }
    return $text
}

# What the editor is doing, for a message.
function Get-WatchDoing([hashtable]$W) {
    if ($W.Importing) {
        $for = if ($W.ImportSince) { " for {0:0} min" -f ((Get-Date) - $W.ImportSince).TotalMinutes } else { "" }
        return "importing$for - last: $($W.ImportWhat)"
    }
    if (Test-WatchBgImport $W) { return "importing in the background (the editor's search index) - last: $($W.BgWhat)" }
    if ($W.Played) { return "playing" }
    return "not importing, not playing yet"
}

# New text in a log Unity still has open (shared read), from the last offset;
# at most 8 MB a poll (the rest comes on the next).
function Read-WatchLog([hashtable]$W, [string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return "" }
    try {
        $fs = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]'ReadWrite, Delete')
        try {
            $len = $fs.Length
            if ($len -lt $W.Offset) { $W.Offset = 0 }
            if ($len -eq $W.Offset) { return "" }
            [void]$fs.Seek($W.Offset, [IO.SeekOrigin]::Begin)
            $buf = New-Object byte[] ([int][Math]::Min($len - $W.Offset, 8MB))
            $n = $fs.Read($buf, 0, $buf.Length)
            $W.Offset += $n
            return [Text.Encoding]::UTF8.GetString($buf, 0, $n)
        } finally { $fs.Dispose() }
    } catch { return "" }
}

function Get-WatchLogTail([string]$Path, [int]$Lines = 30) {
    $w = @{ Offset = 0 }
    try {
        if (Test-Path $Path) { $w.Offset = [Math]::Max(0, (Get-Item -LiteralPath $Path).Length - 65536) }
    } catch {}
    $text = Read-WatchLog $w $Path
    if (-not $text) { return @("(no log at $Path)") }
    # Blank lines, compiler WARNINGS and a run of heartbeats drown the lines
    # that matter: keep the last heartbeat (it says what the editor was doing).
    # So does the "Imported 'GUID: ..." half of each import's pair of lines (the
    # "Importing 'GUID: ... - Path: ..." half says which asset).
    $all = @($text -split "`r?`n" | Where-Object { $_.Trim() -and $_ -notmatch ': warning (CS|UAC)\d+' -and -not $_.StartsWith("Imported 'GUID: ") })
    $lastBeat = -1
    for ($i = 0; $i -lt $all.Count; $i++) { if ($all[$i].Contains('[PSX WATCH] alive')) { $lastBeat = $i } }
    $keep = for ($i = 0; $i -lt $all.Count; $i++) { if (-not $all[$i].Contains('[PSX WATCH] alive') -or $i -eq $lastBeat) { $all[$i] } }
    return @($keep | Select-Object -Last $Lines)
}

# One poll of a watched job: the log (markers, compile errors, a failed
# -executeMethod, signs of life) and the windows (the editor's, and any
# dialog). Returns $null to keep waiting, or the reason to close the editor.
function Step-Watch([hashtable]$W, [int[]]$Pids, [string]$Log, [int]$StallMinutes, [int]$DialogSeconds) {
    $now = Get-Date
    $text = Update-WatchLog $W $Log
    if ($text) {
        if ($text.Contains('error CS')) {
            foreach ($m in $script:WatchErrRx.Matches($text)) {
                if ($W.Errors.Count -lt 15) { $W.Errors.Add($m.Value.Trim()) }
            }
        }
        $m = $script:WatchExecRx.Match($text)
        if ($m.Success) { return "-executeMethod failed: $($m.Value.Trim())" }
        if (-not $W.BeatSeen -and $text.Contains('[PSX WATCH] alive')) {
            $W.BeatSeen = $true
            Write-Host "WATCH: the editor's heartbeat is in the log (every 30 s from its main loop)"
        }
        if (-not $W.InputOff) {
            $m = [regex]::Match($text, "\[PSX WATCH\] (this PC's own input is OFF[^\r\n]*)")
            if ($m.Success) { $W.InputOff = $true; Write-Host "WATCH: $($m.Groups[1].Value.Trim())" }
        }
        if (-not $W.Played) {
            $m = [regex]::Match($text, '\[PSX WATCH\] playing[^\r\n]*')
            if ($m.Success) {
                $W.Played = $true; $W.PlayedAt = $now
                $W.RaiseUntil = $now.AddSeconds($script:WatchRaiseSeconds)
                Write-Host "WATCH: $($m.Value.Trim())"
            }
        }
    }
    if ($W.Errors.Count -gt 0) {
        return "compile errors (a visible editor would run the LAST GOOD code):`n  " + ($W.Errors -join "`n  ")
    }

    try {
        if ($W.Window -eq [IntPtr]::Zero -or -not [PSXWatchWindows]::IsWindowVisible($W.Window)) {
            $h = Find-WatchMainWindow $Pids $W.Leaf
            if ($h -ne [IntPtr]::Zero) {
                $first = -not $W.SawWindow
                $W.Window = $h; $W.SawWindow = $true
                if ($first) {
                    Write-Host ("WATCH: the editor window is up: '{0}' ({1}, handle 0x{2:X}, maximised {3})" -f
                        [PSXWatchWindows]::Text($h), [PSXWatchWindows]::Class($h), $h.ToInt64(), [PSXWatchWindows]::IsZoomed($h))
                    [void][PSXWatchWindows]::Fill($h)
                    [PSXWatchWindows]::Raise($h); $W.RaisedAt = $now
                }
            }
        }

        # Unity takes the keyboard for itself - at its splash, its main window
        # and play - whenever Windows lets it, which is when nobody has
        # touched the keyboard or mouse for a while (seen 2026-09-28 in 4 of 6
        # watched runs). Then the owner comes back, types into what he thinks
        # is his own window, and the keys land in the editor. So when the
        # keyboard moves to the editor and there has been NO input since it
        # was last seen elsewhere (Unity took it; a click into the editor
        # would be input), hand it back to that window and lift the editor
        # over it again without activating it. A click into the editor is
        # left alone. At most 5 times a run.
        $fg = [PSXWatchWindows]::GetForegroundWindow()
        if ($Pids -contains [PSXWatchWindows]::Pid($fg)) {
            if (-not $W.FgEditor) {
                $W.FgEditor = $true
                $lastInput = $now.AddMilliseconds(-[double][PSXWatchWindows]::IdleMs())
                $back = $W.FgBefore
                if ($lastInput -lt $W.FgSeenAt -and $W.KbReturns -lt 5 -and $back -ne [IntPtr]::Zero -and
                    [PSXWatchWindows]::IsWindowVisible($back) -and -not ($Pids -contains [PSXWatchWindows]::Pid($back))) {
                    $W.KbReturns++
                    $ok = [PSXWatchWindows]::SetForegroundWindow($back)
                    if ($W.Window -ne [IntPtr]::Zero) { [PSXWatchWindows]::Raise($W.Window); $W.RaisedAt = $now }
                    $who = "the window you were using"
                    try { $who = "$who ($((Get-Process -Id ([PSXWatchWindows]::Pid($back)) -ErrorAction Stop).ProcessName))" } catch { }
                    Write-Host ("WATCH: Unity took the keyboard for itself - {0} {1}" -f
                        $(if ($ok) { "handed it back to" } else { "could not hand it back to" }), $who)
                } elseif (-not $W.KbNoted) {
                    $W.KbNoted = $true
                    $why = if ($lastInput -ge $W.FgSeenAt) { "you used the keyboard or mouse just then, so it is yours" }
                           elseif ($W.KbReturns -ge 5) { "it has been handed back 5 times already" }
                           elseif ($back -eq [IntPtr]::Zero) { "no window had it before" }
                           elseif (-not [PSXWatchWindows]::IsWindowVisible($back)) { "the window that had it is gone or hidden (pid $([PSXWatchWindows]::Pid($back)), class $([PSXWatchWindows]::Class($back)))" }
                           else { "the window that had it was Unity's own" }
                    Write-Host "WATCH: Unity took the keyboard and has it for now - $why"
                }
            }
        } else {
            # Only a real window is somewhere to hand the keyboard back to: not
            # the empty or dying handle Windows reports mid-switch, nor the
            # hidden window of some batch job.
            $W.FgEditor = $false
            if ($fg -ne [IntPtr]::Zero -and [PSXWatchWindows]::IsWindowVisible($fg) -and [PSXWatchWindows]::Pid($fg) -ne 0) {
                $W.FgBefore = $fg
            }
            $W.FgSeenAt = $now
        }

        if ($W.Window -ne [IntPtr]::Zero) {
            # The raise passes through always-on-top for an instant; never
            # leave it there (it would sit over everything until it closed).
            if ($W.RaisedAt -and ($now - $W.RaisedAt).TotalSeconds -gt 3 -and [PSXWatchWindows]::IsTopmost($W.Window)) {
                [PSXWatchWindows]::NotTopmost($W.Window)
            }
            # Play has started: lift the editor until it is really on screen
            # (checked, not assumed), for up to WatchRaiseSeconds.
            if ($W.RaiseUntil) {
                $cover = 0
                $hits = [PSXWatchWindows]::InView($W.Window, $Pids, [ref]$cover)
                if ($hits -ge 4) {
                    $kbPid = [PSXWatchWindows]::Pid([PSXWatchWindows]::GetForegroundWindow())
                    $kb = if ($Pids -contains $kbPid) { "the editor (its game ignores this PC's input while the test plays)" } else { "the window you were using, not the editor" }
                    Write-Host ("WATCH: the editor is in front ({0} of 5 points on screen, maximised {1}, lifted again {2}x since play began); the keyboard is with {3}" -f
                        $hits, [PSXWatchWindows]::IsZoomed($W.Window), $W.Raises, $kb)
                    $W.RaiseUntil = $null
                } elseif ($now -lt $W.RaiseUntil) {
                    [void][PSXWatchWindows]::Fill($W.Window)
                    [PSXWatchWindows]::Raise($W.Window); $W.RaisedAt = $now; $W.Raises++
                } else {
                    $W.RaiseUntil = $null
                    $who = "another program"
                    if ($cover) { try { $who = (Get-Process -Id $cover -ErrorAction Stop).ProcessName } catch { } }
                    Write-Host "WATCH: the editor is BEHIND other windows ($hits of 5 points on screen; $who is over it) - click Unity on the taskbar to watch"
                    [PSXWatchWindows]::Flash($W.Window)
                }
            }
        }

        foreach ($d in (Find-WatchDialogs $Pids)) {
            $key = $d.Handle.ToInt64()
            if (-not $W.Dialogs.ContainsKey($key)) {
                $W.Dialogs[$key] = $now
                Write-Host "WATCH: a dialog is up - $($d.Text)"
            } elseif ((($now - $W.Dialogs[$key]).TotalSeconds -gt $DialogSeconds) -and
                      (($now - $W.LastLife).TotalSeconds -gt $DialogSeconds)) {
                return "a dialog has held the editor for over $DialogSeconds s (no heartbeat, no work in the log): $($d.Text)"
            }
        }
    } catch { }

    if (($now - $W.LastLife).TotalMinutes -gt $StallMinutes) {
        # Mid-import a quiet log is a long import step (the FMOD bank build
        # writes nothing for minutes), and an editor there may not be killed
        # anyway: it is left to finish, under the hard budget.
        if ($W.Importing) {
            if (-not $W.ImportNoted) {
                $W.ImportNoted = $true
                Write-Host ("WATCH: nothing in the log for {0} minutes, mid-import ({1}) - a long import step; not closed mid-import (the hard budget, {2:HH:mm}, still stands)" -f
                    $StallMinutes, $W.ImportWhat, $W.HardAt)
            }
            return $null
        }
        return "the editor has shown no sign of life for $StallMinutes minutes (no heartbeat from its main loop, no work in the log)"
    }
    return $null
}

# Close a watched editor - and NEVER KILL IT MID-IMPORT (killing an editor
# while it imports corrupts the artifact database; see "Unattended safety" at
# the top). Only processes this job started (not in $Before) whose command
# line we can read and which name the sandbox; never the owner's editor.
#
#   1. Ask, two polite ways at once: the stop file, which PlayCheckWatch reads
#      on the next free tick of the editor's main loop - so never inside an
#      import - and answers with EditorApplication.Exit(3) (no save-scene
#      question); and a close of the main window, for an editor whose
#      PlayCheckWatch did not compile. A clean exit leaves no lock behind.
#   2. Still there 20 s later: killed ONLY if the log says it is not importing
#      (Test-WatchKillable: no import for 10 s, no background import for 30).
#      An editor that is importing is left to finish:
#      the moment the import ends it closes (the queued window close, or the
#      stop file); if not, it is asked again once the log has been quiet of
#      imports for 10 s, and killed 20 s after that if it still has not gone.
#   3. -WaitUntil is how long to wait for an import to end (the hard budget);
#      -Quick (the tool itself was stopped) does not wait for one at all. An
#      editor still importing then is LEFT RUNNING, with its PID printed: it
#      closes itself when the import ends, and until then the next watched job
#      refuses to start beside it (Wait-ProjectIdle waits for it too).
# Then the lot is waited for - the editor's import worker outlives it by a few
# seconds - so the next job does not start beside a leftover. The stop file is
# left where it is (only an editor this tool launched watched ever reads it,
# and the next watched launch deletes it first), so an editor that appears
# only after a Ctrl+C still finds it.
function Stop-WatchedEditor([string]$Project, $Before, [hashtable]$W, [string]$Log, [datetime]$WaitUntil, [string]$Why = "", [switch]$Quick) {
    $job = { @(Get-UnityPids $Project -Strict | Where-Object { $Before -notcontains $_ }) }
    $ask = {
        try { [IO.File]::WriteAllText($W.StopFile, "$Why`r`n") } catch { }
        foreach ($id in (& $job)) {
            try {
                $p = Get-Process -Id $id -ErrorAction Stop
                if ($p.MainWindowHandle -ne [IntPtr]::Zero) { [void]$p.CloseMainWindow() }
            } catch { }
        }
    }
    [void](Update-WatchLog $W $Log)
    & $ask
    Write-Host ("WATCH: asked the editor to close (a stop file its main loop reads between imports, and its window's close) - it is {0}" -f (Get-WatchDoing $W))
    $grace = (Get-Date).AddSeconds(20)
    $wasImporting = Test-WatchImporting $W
    $said = $false
    while ($true) {
        Start-Sleep -Seconds 2
        [void](Update-WatchLog $W $Log)
        if (-not (& $job).Count) {
            Write-Host "WATCH: the editor closed"
            if (-not @(Get-UnityPids $Project).Count) { Clear-WatchSceneBackups $Project }
            return
        }
        $now = Get-Date
        # An import it was in has ended (and stayed ended for 10 s): the stop
        # file should have closed it on its first free tick. Ask again - the
        # window's close may have been eaten by the import's progress bar -
        # and give it another 20 s before the kill.
        if (Test-WatchImporting $W) { $wasImporting = $true }
        elseif ($wasImporting -and (Test-WatchKillable $W)) {
            $wasImporting = $false
            Write-Host "WATCH: the import is over - asking the editor again to close (it is killed in 20 s if it has not)"
            & $ask
            $grace = $now.AddSeconds(20)
            continue
        }
        if ($now -lt $grace) { continue }
        if (Test-WatchKillable $W) { break }
        if (-not (Test-WatchImporting $W)) { continue }   # an import only just ended: 10 s of quiet first
        if (-not $said) {
            $said = $true
            Write-Host ("WATCH: NOT killing it: it is {0}, and killing an editor mid-import corrupts the artifact database. It closes itself when the import ends." -f (Get-WatchDoing $W))
            if (-not $Quick -and $now -lt $WaitUntil) { Write-Host ("WATCH: waiting for that (until {0:HH:mm}, the hard budget)" -f $WaitUntil) }
        }
        if ($Quick -or $now -ge $WaitUntil) {
            Write-Host ("WATCH: LEFT RUNNING, still importing: PID {0} on {1}. It closes itself when the import ends; until then a watched run on this sandbox will not start beside it." -f ((& $job) -join ', '), $Project)
            return
        }
    }
    foreach ($id in (& $job)) {
        Write-Host "WATCH: the editor did not close and is not importing - killing PID $id"
        Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
    }
    $until = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $until -and (& $job).Count) { Start-Sleep -Seconds 2 }
    $left = & $job
    if ($left.Count) { Write-Host "WATCH: still running after the kill: PID $($left -join ', ')" }
    else {
        Write-Host "WATCH: the editor is gone"
        if (-not @(Get-UnityPids $Project).Count) { Clear-WatchSceneBackups $Project }
    }
}

# An editor killed in play mode (by the rules above, or a crash) leaves the
# open scenes' backups in Temp\__Backupscenes, and the next VISIBLE editor to
# open the sandbox stops on a "Recovering Scene Backups" Yes/No question before
# it compiles anything (2026-09-28, the run after a watchdog kill). Batch mode
# never asks. They are a dead test's backups in a sandbox, so they go - only
# when no Unity is working on the sandbox.
function Clear-WatchSceneBackups([string]$Project) {
    $bk = Join-Path $Project 'Temp\__Backupscenes'
    if (-not (Test-Path -LiteralPath $bk)) { return }
    try {
        Remove-Item -LiteralPath $bk -Recurse -Force -ErrorAction Stop
        Write-Host "WATCH: cleared the scene backups a killed editor left in $bk (they stop a visible editor on a 'Recovering Scene Backups' question)"
    } catch { Write-Host "WATCH: could not clear ${bk}: $($_.Exception.Message)" }
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
        [int]$DialogSeconds = $(if ($env:PSX_WATCH_DIALOG_SECONDS) { [int]$env:PSX_WATCH_DIALOG_SECONDS } else { 90 }),
        # The most a watched editor may run, whatever it is doing. 0 = the
        # default, -MaxMinutes + 60.
        [int]$HardMinutes = $(if ($env:PSX_WATCH_HARD_MINUTES) { [int]$env:PSX_WATCH_HARD_MINUTES } else { 0 }),
        # The park recovery's own job (Assert-EditionParkClear). Nothing else passes it.
        [switch]$NoParkCheck
    )

    $script:UnityJobExitCode = $null
    # The job's own -projectPath IS the project to watch. -Project defaults to
    # $env:PSX_SANDBOX or PSXBuild, so a PSXCity job launched without either
    # (2026-10-05, the decks run) looked for its editor among PSXBuild's - where
    # another agent's Unity was already running - said UNITY NEVER STARTED,
    # and the caller started the next job on top of the live one.
    for ($i = 0; $i -lt $UnityArgs.Count - 1; $i++) {
        if ($UnityArgs[$i] -ieq '-projectPath') { $Project = $UnityArgs[$i + 1] }
    }
    if (-not $NoParkCheck) {
        # A killed edition build's park goes back before any job runs on the
        # sandbox (see Assert-EditionParkClear). Throws when it is stuck.
        $parkTarget = $Project
        for ($i = 0; $i -lt $UnityArgs.Count - 1; $i++) {
            if ($UnityArgs[$i] -ieq '-projectPath') { $parkTarget = $UnityArgs[$i + 1] }
        }
        Assert-EditionParkClear $parkTarget
    }
    if ($HardMinutes -le 0) { $HardMinutes = $MaxMinutes + 60 }
    if ($HardMinutes -lt $MaxMinutes) { $HardMinutes = $MaxMinutes }

    # The build target, always named (see Get-PSXBuildTarget), and printed so
    # a tool's own output says which import its frames were drawn with.
    $UnityArgs = Add-PSXBuildTarget $UnityArgs
    for ($i = 0; $i -lt $UnityArgs.Count - 1; $i++) {
        if ($UnityArgs[$i] -ieq '-buildTarget') { Write-Host "UNITY TARGET: $($UnityArgs[$i + 1])"; break }
    }
    # For the frames' sidecars (Editor\ShotSidecar.cs): the commit of the
    # source the job was copied from. Inherited by the one process launched.
    $env:PSX_COMMIT = Get-PSXSourceCommit

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
    if ($Watch) {
        # One Unity job at a time per sandbox: never a second, visible one
        # beside a job that is still working there.
        $busy = @(Get-UnityPids $Project -Strict)
        if ($busy.Count) {
            Write-Host "WATCH: a Unity job is already working on $Project (PID $($busy -join ', ')) - not opening a second editor on the same sandbox"
            return $false
        }
    }

    # A batch job never starts beside a live one on the same project (Unity
    # refuses the second, which then never shows up as a new process).
    if (-not $Watch -and -not (Wait-ProjectIdle $Project $MaxMinutes)) {
        Write-Host "a Unity job is still running on $Project after $MaxMinutes min - not starting another"
        return $false
    }
    if (Test-Path $Log) { Remove-Item $Log -Force }
    $before = @(Get-UnityPids $Project)
    $fgAtLaunch = [IntPtr]::Zero; $launchedAt = Get-Date
    $stopFile = $null
    if ($Watch) {
        if (-not $before.Count) { Clear-WatchSceneBackups $Project }
        Initialize-PSXWatchWin
        # A stale stop file would close the new editor at its first tick.
        $stopFile = Join-Path $target 'PSXWatch_stop.txt'
        Remove-Item -LiteralPath $stopFile -Force -ErrorAction SilentlyContinue
        # Where the keyboard is now: where it goes back to if Unity takes it.
        $fgAtLaunch = [PSXWatchWindows]::GetForegroundWindow(); $launchedAt = Get-Date
        if (-not [PSXWatchWindows]::IsWindowVisible($fgAtLaunch)) { $fgAtLaunch = [IntPtr]::Zero }
        $UnityArgs = ConvertTo-WatchArgs $UnityArgs
    }

    # Hold a handle on every process of the job as it appears, so its exit
    # code can still be read after it is gone (EditorApplication.Exit(n) in a
    # harness lands here). The editor is the one that is not an import worker.
    # $w is a watched job's state (see Step-Watch). An editor starts out
    # IMPORTING: it opens the project before it writes anything we can read.
    $held = @{}
    $start = Get-Date
    $soft = $start.AddMinutes($MaxMinutes)
    $hard = $start.AddMinutes($HardMinutes)
    $w = @{ Offset = 0; Carry = ""; LastLife = $start; Window = [IntPtr]::Zero; SawWindow = $false;
            Played = $false; PlayedAt = $null; RaiseUntil = $null; RaisedAt = $null; Raises = 0;
            BeatSeen = $false; InputOff = $false; PastBudget = $false;
            FgBefore = $fgAtLaunch; FgSeenAt = $launchedAt; FgEditor = $false; KbReturns = 0;
            Dialogs = @{}; Errors = (New-Object System.Collections.Generic.List[string]);
            Importing = $true; ImportWhat = "opening the project"; ImportSince = $start; ImportNoted = $false; IdleSince = $null; BgAt = $null; BgWhat = "";
            HardAt = $hard; StopFile = $stopFile;
            Leaf = (Split-Path -Leaf $Project) }

    # A batch job is given up on at -MaxMinutes (and left running, as ever). A
    # watched one is not: it is watched until it quits, trips a rule, or runs
    # out of its hard budget - and asked to close then (Stop-WatchedEditor,
    # which never kills it mid-import). The finally does the same, without
    # waiting for an import, if the tool itself is stopped (Ctrl+C) at any
    # point after the launch.
    $settled = -not $Watch
    try {
        if ($Watch) {
            $savedActive = $env:PSX_WATCH_ACTIVE
            $savedReporter = $env:UNITY_DONOTSTARTBUGREPORTER
            $savedStop = $env:PSX_WATCH_STOP_FILE
            # Set for the one process launched here (the environment is copied
            # at launch), then put back so nothing else inherits it.
            $env:PSX_WATCH_ACTIVE = '1'
            $env:UNITY_DONOTSTARTBUGREPORTER = '1'
            $env:PSX_WATCH_STOP_FILE = $stopFile
            try { Start-Process -FilePath $Unity -ArgumentList $UnityArgs -WindowStyle Maximized | Out-Null }
            finally {
                $env:PSX_WATCH_ACTIVE = $savedActive; $env:UNITY_DONOTSTARTBUGREPORTER = $savedReporter
                $env:PSX_WATCH_STOP_FILE = $savedStop
            }
            Write-Host "WATCH: opening $target in a visible Unity editor (-NoWatch or PSX_WATCH=0 runs it hidden)"
        } else {
            Start-Process -FilePath $Unity -ArgumentList $UnityArgs -WindowStyle Hidden | Out-Null
        }

        # Phase one: wait for a Unity process that was not there before. Up to
        # two minutes, because a cold sandbox spends that long opening the
        # project before it is visible as a child at all.
        $appeared = $false
        $spawnDeadline = (Get-Date).AddMinutes(2)
        while ((Get-Date) -lt $spawnDeadline) {
            Start-Sleep -Seconds 2
            $now = @(Get-UnityPids $Project)
            if (@($now | Where-Object { $before -notcontains $_ })) { $appeared = $true; break }
        }
        if (-not $appeared) {
            $settled = $true
            Write-Host "UNITY NEVER STARTED - check the argument list (a path with a space must be ONE quoted string)"
            return $false
        }
        $start = Get-Date
        $soft = $start.AddMinutes($MaxMinutes)
        $hard = $start.AddMinutes($HardMinutes)
        $w.LastLife = $start; $w.ImportSince = $start; $w.HardAt = $hard

        # Phase two: wait for every new PID to go away, and STAY away.
        #
        # One empty sighting is not enough. The launcher is itself a Unity
        # process, so the sequence is: launcher appears, launcher exits, gap,
        # real editor appears. A single empty poll landing in that gap declares
        # the job finished a fraction of a second before it starts. Requiring
        # the gap to hold for half a minute is longer than the handoff has ever
        # taken and costs half a minute on a job that runs for five.
        $quietSince = $null
        while ($true) {
            $now = Get-Date
            if (-not $Watch -and $now -ge $soft) { break }
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
                $quietSince = $null
                if ($Watch) {
                    $pids = @($procs | Where-Object { $_.CommandLine } | ForEach-Object { [int]$_.ProcessId })
                    $why = Step-Watch $w $pids $Log $StallMinutes $DialogSeconds
                    if (-not $why) {
                        # Once play has started the import is over: the play
                        # itself gets the whole job's budget, and no more.
                        $killAt = $hard
                        if ($w.PlayedAt) {
                            $playEnd = $w.PlayedAt.AddMinutes($MaxMinutes)
                            if ($playEnd -lt $soft) { $playEnd = $soft }
                            if ($playEnd -lt $killAt) { $killAt = $playEnd }
                        }
                        if (-not $w.PastBudget -and $now -ge $soft) {
                            $w.PastBudget = $true
                            Write-Host ("WATCH: the job has used its {0}-minute budget and the editor is still at work ({1}) - still watching it; it is asked to close at {2:HH:mm} if it has not quit by then" -f
                                $MaxMinutes, (Get-WatchDoing $w), $killAt)
                        }
                        if ($now -ge $killAt) {
                            $why = if ($killAt -lt $hard) { "play mode has run $MaxMinutes minutes (the whole job's budget) without the check ending" }
                                   else { "it has run $HardMinutes minutes (PSX_WATCH_HARD_MINUTES; default -MaxMinutes + 60) without quitting" }
                        }
                    }
                    if ($why) {
                        Write-Host "WATCH: closing the editor - $why"
                        Write-Host "Tail of ${Log}:"
                        Get-WatchLogTail $Log 30 | ForEach-Object { Write-Host "  $_" }
                        Stop-WatchedEditor $Project $before $w $Log $hard ($why -split "`n")[0]
                        $settled = $true
                        return $false
                    }
                }
            } else {
                if (-not $quietSince) { $quietSince = $now }
                if (($now - $quietSince).TotalSeconds -ge 30) {
                    # The editor's code: the last non-worker to exit (the
                    # launcher exits first, with its own).
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
                    $settled = $true
                    if ($Watch) {
                        if (-not $w.SawWindow) { Write-Host "WATCH: the editor never showed a window" }
                        $late = if ($w.PastBudget) { ", {0:0} minutes past the job's budget" -f ($now - $soft).TotalMinutes } else { "" }
                        Write-Host "WATCH: the editor closed by itself (exit code $script:UnityJobExitCode$late)"
                    }
                    return $true
                }
            }
            Start-Sleep -Seconds $(if ($Watch) { 2 } else { 5 })
        }
    } finally {
        if (-not $settled) {
            Write-Host "WATCH: the tool was stopped - asking the watched editor to close"
            Stop-WatchedEditor $Project $before $w $Log (Get-Date) "the tool was stopped" -Quick
        }
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

# DOT-SOURCED: check the sandbox this tool is about to copy into, now - before
# its first robocopy. Every sandbox tool sets $proj (or PSX_SANDBOX) first; the
# fallback is the same default the tools use.
$psxParkSandbox = if ((Test-Path variable:proj) -and $proj) { "$proj" }
                  elseif ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
Assert-EditionParkClear $psxParkSandbox
