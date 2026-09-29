using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// WATCH MODE for the play-test harnesses (tools\unity-wait.ps1, "Watching
    /// the tests"). The *-play-check.ps1 tools used to run the editor with
    /// -batchmode -nographics in a hidden window, so the owner heard the race
    /// and saw nothing. By default they now open the sandbox in a normal,
    /// visible editor and set PSX_WATCH_ACTIVE=1 for it; this class then puts
    /// the race in front of him when play mode starts: the Game view set to
    /// 16:9 and maximised (Play Maximized) so the game fills the window, with
    /// the audio unmuted.
    ///
    /// Three more jobs, all because a visible editor sits on a desk where
    /// somebody is working:
    ///   * HANDS OFF. While the test plays, this PC's own keyboard, mouse,
    ///     pads and touch screen are switched off in the Input System, so a key
    ///     typed into what the owner thinks is another window - Escape opens
    ///     the pause menu and stops the clock; a click in the walk test grabs
    ///     the cursor; WASD steers a car the harness is driving - never reaches
    ///     the check. The harnesses' own VIRTUAL pads (InputSystem.AddDevice)
    ///     are not native and keep working. Batch mode never had a keyboard.
    ///   * NO FOCUS GRAB. Nothing here asks for the keyboard focus (no
    ///     EditorWindow.Focus): unity-wait.ps1 raises the window above the
    ///     others without activating it, so typing carries on where it was.
    ///   * A HEARTBEAT. "[PSX WATCH] alive: ..." every 30 s from the editor's
    ///     main loop, which is what unity-wait.ps1 believes when it asks "is
    ///     the editor still working, or is something holding it?" - Unity's
    ///     background threads (licensing, ADB scans) write to the log whatever
    ///     the main thread is doing, so a growing log alone proves nothing.
    ///     Its lines also tell the waiter the editor is NOT importing (no tick
    ///     runs inside an import), which is when it may be killed.
    ///   * A POLITE CLOSE. When unity-wait.ps1 has to end a watched run it
    ///     writes the stop file (PSX_WATCH_STOP_FILE, set for this editor
    ///     only); the first free tick of the main loop after that - never one
    ///     inside an import - exits with EditorApplication.Exit(3), which asks
    ///     no save-scene question. The waiter never kills an editor that is
    ///     importing (that corrupts the artifact database), so this is how an
    ///     editor it asked to close mid-import closes itself when the import
    ///     ends.
    ///
    /// It touches no seed, car, scene or check - only editor windows and the
    /// real input devices. What a window cannot help changing is written up in
    /// unity-wait.ps1: the frame is this 16:9 Game view instead of batch mode's
    /// 640x480, and the frame rate is a drawing editor's. It does nothing at
    /// all in batch mode or in an editor someone opened by hand (the variable
    /// is only ever set by the tools, for the one process they launch).
    /// </summary>
    [InitializeOnLoad]
    public static class PlayCheckWatch
    {
        /// <summary>A tool launched this editor to be watched.</summary>
        public static readonly bool Active =
            !Application.isBatchMode && Environment.GetEnvironmentVariable("PSX_WATCH_ACTIVE") == "1";

        /// <summary>Nobody is at this editor to answer a question: batch mode, or
        /// a watched run (the owner is watching, not driving). A harness that
        /// behaves differently "from the menu" must treat both the same, or a
        /// watched run asks a save-scene question and never quits.</summary>
        public static bool Unattended => Application.isBatchMode || Active;

        static bool announced;

        // Every line without a stack trace: they are lines for unity-wait.ps1
        // to find and for a person to read in the log's tail, and a four-line
        // trace under each would push the lines that matter out of it.
        static void Note(string s) => Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", s);
        static void Warn(string s) => Debug.LogFormat(LogType.Warning, LogOption.NoStacktrace, null, "{0}", s);

        static PlayCheckWatch()
        {
            if (!Active) return;
            EditorApplication.playModeStateChanged -= OnState;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.update -= Beat;
            EditorApplication.update += Beat;
            nextBeat = 0;   // the first beat right away: proof the domain came up
            // A domain that loads while already playing (a script reload in
            // play) gets no EnteredPlayMode: lock once the editor is settled.
            // Deferred, because the Input System initialises itself from its
            // own InitializeOnLoad and the order of those is not defined.
            if (EditorApplication.isPlaying) EditorApplication.delayCall += LockInput;
        }

        static void OnState(PlayModeStateChange st)
        {
            // Before the switch: pick 16:9 and Play Maximized, so the first
            // frame of play is already the right shape. After it: maximise
            // again (entering play re-docks the views), keep the frame fitted
            // to the view while it settles (see Refit), and take this PC's own
            // input away from the game until play ends.
            if (st == PlayModeStateChange.ExitingEditMode) Frame(false);
            else if (st == PlayModeStateChange.EnteredPlayMode)
            {
                LockInput();
                Frame(true);
                fitted = Vector2.zero;
                EditorApplication.update -= Refit;
                EditorApplication.update += Refit;
            }
            else if (st == PlayModeStateChange.ExitingPlayMode) UnlockInput();
        }

        // ---- heartbeat ---------------------------------------------------

        const double BeatSeconds = 30;
        static double nextBeat;

        /// <summary>One line every 30 s from EditorApplication.update, i.e. from
        /// the main thread's own loop. Stops when that loop stops (a modal
        /// dialog that holds it, a hang); does not stop for a background
        /// thread. No stack trace: it is a line for a script to find, and a
        /// four-line trace every 30 s would bury the log's tail.</summary>
        static void Beat()
        {
            double now = EditorApplication.timeSinceStartup;
            CheckStop(now);
            if (now < nextBeat) return;
            nextBeat = now + BeatSeconds;
            string state = EditorApplication.isPlaying ? (EditorApplication.isPaused ? "play mode, PAUSED" : "play mode")
                         : EditorApplication.isCompiling ? "edit mode, compiling" : "edit mode";
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null,
                "[PSX WATCH] alive: {0}, frame {1}, time scale {2:0.##}, {3:0} s since the editor started",
                state, Time.frameCount, Time.timeScale, now);
        }

        // ---- the polite close ------------------------------------------------

        /// <summary>The file unity-wait.ps1 writes to ask this editor to close
        /// (it deletes a stale one before the launch).</summary>
        static readonly string StopFile = Environment.GetEnvironmentVariable("PSX_WATCH_STOP_FILE");
        static double nextStopCheck;
        static bool stopping;

        /// <summary>Twice a second, from the main loop's own tick: if the waiter
        /// has asked, exit. Code 3, so a closed run is never read as a pass or
        /// a harness's own failure (0 / 1).</summary>
        static void CheckStop(double now)
        {
            if (stopping || string.IsNullOrEmpty(StopFile) || now < nextStopCheck) return;
            nextStopCheck = now + 0.5;
            string why;
            try
            {
                if (!File.Exists(StopFile)) return;
                why = File.ReadAllText(StopFile).Trim();
                File.Delete(StopFile);
            }
            catch (Exception) { return; }   // being written: the next check
            stopping = true;
            Note("[PSX WATCH] closing: unity-wait.ps1 asked (" + (why.Length > 0 ? why : "no reason given") +
                 ") - EditorApplication.Exit(3) from the main loop, between imports");
            EditorApplication.Exit(3);
        }

        // ---- hands off ---------------------------------------------------

        /// <summary>The real devices this class switched off, to switch back on
        /// when play ends (and nothing else: a device something else disabled
        /// stays as it was).</summary>
        static readonly List<InputDevice> locked = new List<InputDevice>();
        static bool listening;

        static void LockInput()
        {
            try
            {
                var names = new List<string>();
                foreach (var d in InputSystem.devices.ToArray())
                    if (LockOne(d)) names.Add(d.displayName ?? d.name);
                if (!listening)
                {
                    InputSystem.onDeviceChange += OnDeviceChange;
                    listening = true;
                }
                Note("[PSX WATCH] this PC's own input is OFF while the test plays (" +
                          (names.Count > 0 ? string.Join(", ", names) : "no devices") +
                          "): keys, clicks and pads cannot reach the check. The harness's virtual pads still work.");
            }
            catch (Exception e)
            {
                Warn("[PSX WATCH] could not switch this PC's input off: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>Switches one device off if it is a real one (reported by the
        /// platform, not added by script) and on. True if it did.</summary>
        static bool LockOne(InputDevice d)
        {
            if (d == null || !d.native || !d.enabled) return false;
            InputSystem.DisableDevice(d);
            if (!locked.Contains(d)) locked.Add(d);
            return true;
        }

        /// <summary>A pad plugged in mid-race, or a device something switched
        /// back on, is switched off again while the test plays.</summary>
        static void OnDeviceChange(InputDevice d, InputDeviceChange change)
        {
            if (!EditorApplication.isPlaying) return;
            if (change != InputDeviceChange.Added && change != InputDeviceChange.Reconnected &&
                change != InputDeviceChange.Enabled) return;
            try
            {
                if (LockOne(d)) Note("[PSX WATCH] switched off " + (d.displayName ?? d.name) + " (" + change + ") while the test plays");
            }
            catch (Exception e)
            {
                Warn("[PSX WATCH] could not switch " + d.name + " off: " + e.Message);
            }
        }

        static void UnlockInput()
        {
            if (listening)
            {
                InputSystem.onDeviceChange -= OnDeviceChange;
                listening = false;
            }
            foreach (var d in locked)
            {
                try { if (d != null && d.added && !d.enabled) InputSystem.EnableDevice(d); }
                catch (Exception) { /* a device that went away */ }
            }
            locked.Clear();
        }

        // ---- the Game view ---------------------------------------------------

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static EditorWindow view;
        static Vector2 fitted;
        static int refits;

        static void Frame(bool playing)
        {
            string aspect = "?", how = "?";
            try
            {
                EditorUtility.audioMasterMute = false;
                var gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                if (gvType == null) { Warn("[PSX WATCH] no UnityEditor.GameView type in this editor"); return; }
                // focus: false. The window is raised by unity-wait.ps1 WITHOUT
                // taking the keyboard; a Focus() here would ask Windows for it.
                var gv = EditorWindow.GetWindow(gvType, false, null, false);
                view = gv;
                aspect = SelectSixteenByNine(gv);
                how = SetPlayMaximized(gv);
                if (playing && !gv.maximized) gv.maximized = true;
                gv.Repaint();
                if (playing && !announced)
                {
                    announced = true;
                    // unity-wait.ps1 watches the log for this marker and brings
                    // the editor's main window to the front.
                    Note($"[PSX WATCH] playing in the Game view: {aspect}, {how}, maximised {gv.maximized}, " +
                              $"audio muted {EditorUtility.audioMasterMute}, {gv.position.width:0}x{gv.position.height:0}");
                }
            }
            catch (Exception e)
            {
                // Watching is a courtesy: never let it break the check.
                Warn("[PSX WATCH] could not frame the Game view: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>Keeps the whole 16:9 frame in the view. Maximising resizes
        /// the view a tick or two AFTER play starts, and the Game view keeps the
        /// zoom and pan it had for the old size - the first try came up at
        /// "Scale 1.5x", cropped at the bottom and the right. So on every
        /// editor tick of play, if the view's size differs from the one last
        /// fitted, snap the zoom to the scale that shows the whole frame (the
        /// Game view's own minScale; 1x when the frame is sized to the view).
        /// Runs only while playing; a size the owner picks by hand stands until
        /// the window itself is resized.</summary>
        static void Refit()
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= Refit; return; }
            try
            {
                if (view == null)
                {
                    var gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                    var all = gvType != null ? Resources.FindObjectsOfTypeAll(gvType) : null;
                    view = all != null && all.Length > 0 ? (EditorWindow)all[0] : null;
                    if (view == null) return;
                }
                var size = view.position.size;
                if (size == fitted) return;
                fitted = size;
                var t = view.GetType();
                t.GetMethod("UpdateZoomAreaAndParent", Any, null, Type.EmptyTypes, null)?.Invoke(view, null);
                var min = t.GetProperty("minScale", Any);
                var snap = t.GetMethod("SnapZoom", Any, null, new[] { typeof(float) }, null);
                if (min == null || snap == null) { EditorApplication.update -= Refit; return; }
                float scale = (float)min.GetValue(view);
                snap.Invoke(view, new object[] { scale });
                view.Repaint();
                if (refits++ < 4) Note($"[PSX WATCH] fitted the frame to the view: {size.x:0}x{size.y:0}, scale {scale:0.##}x");
            }
            catch (Exception e)
            {
                EditorApplication.update -= Refit;
                Warn("[PSX WATCH] could not fit the frame: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>Selects the Game view's 16:9 aspect entry (built in to the
        /// Standalone group, which WebGL uses). Reflection, because the Game
        /// view's size list is internal. It never ADDS a size: that list lives in
        /// the user's global editor preferences, shared with the owner's own
        /// editor.</summary>
        static string SelectSixteenByNine(EditorWindow gv)
        {
            var asm = typeof(EditorWindow).Assembly;
            var sizesType = asm.GetType("UnityEditor.GameViewSizes");
            var sizeType = asm.GetType("UnityEditor.GameViewSize");
            if (sizesType == null || sizeType == null) return "sizes unavailable";
            var single = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = single.GetProperty("instance", Any)?.GetValue(null);
            var group = sizes != null ? sizesType.GetProperty("currentGroup", Any)?.GetValue(sizes) : null;
            if (group == null) return "no size group";
            var groupType = group.GetType();
            int total = (int)groupType.GetMethod("GetTotalCount", Any).Invoke(group, null);
            var get = groupType.GetMethod("GetGameViewSize", Any);
            var pw = sizeType.GetProperty("width", Any);
            var ph = sizeType.GetProperty("height", Any);
            var pk = sizeType.GetProperty("sizeType", Any);
            var pn = sizeType.GetProperty("displayText", Any) ?? sizeType.GetProperty("baseText", Any);
            int aspectIndex = -1, anyIndex = -1;
            string name = "";
            for (int i = 0; i < total; i++)
            {
                var s = get.Invoke(group, new object[] { i });
                int w = (int)pw.GetValue(s), h = (int)ph.GetValue(s);
                if (w <= 0 || h <= 0 || w * 9 != h * 16) continue;
                bool isAspect = pk != null && pk.GetValue(s).ToString() == "AspectRatio";
                if (isAspect && aspectIndex < 0) { aspectIndex = i; name = pn != null ? (string)pn.GetValue(s) : "16:9"; }
                if (anyIndex < 0) anyIndex = i;
            }
            int pick = aspectIndex >= 0 ? aspectIndex : anyIndex;
            if (pick < 0) return "no 16:9 entry (left as it was)";
            if (pick != aspectIndex && pn != null) name = (string)pn.GetValue(get.Invoke(group, new object[] { pick }));

            // The dropdown's own callback first (it also resets the zoom for the
            // new size); the bare property if this editor has no such method.
            var cb = gv.GetType().GetMethod("SizeSelectionCallback", Any, null, new[] { typeof(int), typeof(object) }, null);
            if (cb != null) cb.Invoke(gv, new object[] { pick, null });
            else
            {
                var sel = gv.GetType().GetProperty("selectedSizeIndex", Any);
                if (sel == null || !sel.CanWrite) return "cannot select a size";
                sel.SetValue(gv, pick);
            }
            return "size '" + name + "'";
        }

        /// <summary>PlayModeView.enterPlayModeBehavior = PlayMaximized (Unity 6),
        /// or the older maximizeOnPlay flag.</summary>
        static string SetPlayMaximized(EditorWindow gv)
        {
            var p = gv.GetType().GetProperty("enterPlayModeBehavior", Any);
            if (p != null && p.CanWrite && p.PropertyType.IsEnum)
            {
                p.SetValue(gv, Enum.Parse(p.PropertyType, "PlayMaximized"));
                return "Play Maximized";
            }
            var m = gv.GetType().GetProperty("maximizeOnPlay", Any);
            if (m != null && m.CanWrite) { m.SetValue(gv, true); return "Maximize On Play"; }
            return "maximised by hand";
        }
    }
}
