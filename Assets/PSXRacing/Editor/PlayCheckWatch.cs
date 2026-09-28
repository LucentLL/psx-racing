using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// WATCH MODE for the play-test harnesses (tools\unity-wait.ps1, "Watching
    /// the tests"). The *-play-check.ps1 tools used to run the editor with
    /// -batchmode -nographics in a hidden window, so the owner heard the race
    /// and saw nothing. By default they now open the sandbox in a normal,
    /// visible editor and set PSX_WATCH_ACTIVE=1 for it; this class then puts
    /// the race in front of him when play mode starts: the Game view focused,
    /// set to 16:9 and maximised (Play Maximized) so the game fills the window,
    /// with the audio unmuted.
    ///
    /// It touches no seed, car, scene or check - only editor windows. What a
    /// window cannot help changing is written up in unity-wait.ps1: the frame
    /// is this 16:9 Game view instead of batch mode's 640x480, and the frame
    /// rate is a drawing editor's. It does nothing at all in batch mode or in
    /// an editor someone opened by hand (the variable is only ever set by the
    /// tools, for the one process they launch).
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

        static PlayCheckWatch()
        {
            if (!Active) return;
            EditorApplication.playModeStateChanged -= OnState;
            EditorApplication.playModeStateChanged += OnState;
        }

        static void OnState(PlayModeStateChange st)
        {
            // Before the switch: pick 16:9 and Play Maximized, so the first
            // frame of play is already the right shape. After it: maximise and
            // focus again (entering play re-docks the views), then keep the
            // frame fitted to the view while it settles (see Refit).
            if (st == PlayModeStateChange.ExitingEditMode) Frame(false);
            else if (st == PlayModeStateChange.EnteredPlayMode)
            {
                Frame(true);
                fitted = Vector2.zero;
                EditorApplication.update -= Refit;
                EditorApplication.update += Refit;
            }
        }

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
                if (gvType == null) { Debug.LogWarning("[PSX WATCH] no UnityEditor.GameView type in this editor"); return; }
                var gv = EditorWindow.GetWindow(gvType, false, null, true);
                view = gv;
                aspect = SelectSixteenByNine(gv);
                how = SetPlayMaximized(gv);
                if (playing && !gv.maximized) gv.maximized = true;
                gv.Focus();
                gv.Repaint();
                if (playing && !announced)
                {
                    announced = true;
                    // unity-wait.ps1 watches the log for this marker and brings
                    // the editor's main window to the front, maximised.
                    Debug.Log($"[PSX WATCH] playing in the Game view: {aspect}, {how}, maximised {gv.maximized}, " +
                              $"audio muted {EditorUtility.audioMasterMute}, {gv.position.width:0}x{gv.position.height:0}");
                }
            }
            catch (Exception e)
            {
                // Watching is a courtesy: never let it break the check.
                Debug.LogWarning("[PSX WATCH] could not frame the Game view: " + e.GetType().Name + ": " + e.Message);
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
                if (refits++ < 4) Debug.Log($"[PSX WATCH] fitted the frame to the view: {size.x:0}x{size.y:0}, scale {scale:0.##}x");
            }
            catch (Exception e)
            {
                EditorApplication.update -= Refit;
                Debug.LogWarning("[PSX WATCH] could not fit the frame: " + e.GetType().Name + ": " + e.Message);
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
