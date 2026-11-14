using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE OWNER'S RULE (2026-11-14): "All screens and menus must be navigable
    /// on keyboard/mouse, controller, and mobile" and "buttons or text should
    /// never overlay other buttons or text". Pictures and a navigation log:
    ///   * the car page with a long fault list and no repair affordable (the
    ///     owner's case: nothing selectable under the buttons), a pad pressing
    ///     DOWN from the lowest control - every press logged - and the picture
    ///     where the presses left the page;
    ///   * OPTIONS > VISUALS at the five pause sizes (top, scrolled to the end,
    ///     and a pad walked down all thirteen rows), GAMEPLAY in an arcade
    ///     session (the DAMAGE row).
    /// Edit mode runs no EventSystem, so the presses go through ExecuteEvents
    /// with a local EventSystem, and MenuNavWatch's LateUpdate part (keep the
    /// cursor's control in view, give it its MenuNavScroll) is done after each
    /// press exactly as the watch does it.
    /// </summary>
    public static class NavScrollPreview
    {
        [MenuItem("PSX Racing/Preview Nav Scroll")]
        public static void Capture()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string outDir = Path.Combine(root, "Screenshots", "navscroll");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);
            fails = 0;
            try
            {
                LifeSimManager.DeleteSave();
                LifeSimManager.StartNewGame("VINCE", 25, LifeRules.DefaultJobIndex);
                LifeRules.SeedFallbackCar(LifeSimManager.State);
                var st = LifeSimManager.State;
                var car = st.ActiveCar ?? (st.cars.Count > 0 ? st.cars[0] : null);
                if (car == null) { Debug.LogError("[NavScroll] FAIL no car"); return; }
                string origin = null;
                int added = 0;
                foreach (var id in new[] { "panel_damage", "lamp_out", "radiator_leak", "exhaust_damage", "roof_glass",
                                           "alignment", "bumper_bar", "used_engine_worn", "used_fluids_low", "srs_deployed" })
                {
                    var f = FaultCatalog.MakeById(id, origin);
                    if (f == null) continue;
                    f.hidden = false;
                    f.diagnosed = true;
                    car.faults.Add(f);
                    added++;
                }
                // Nothing affordable: every repair button greyed, so the fault
                // list is words with nothing to select under the car's buttons.
                st.money = 0;
                LifeSimManager.Save();
                Debug.Log("[NavScroll] car " + car.displayName + " with " + added + " known faults, no money");
                LifeHomePreview.Shoot(outDir, "carmenu_nav", "carmenu", garageCar: car.id, probe: PadDownCarPage);

                DebugBenchPreview.ShootPause(outDir, "settings_visuals", SettingsTab.Visuals);
                DebugBenchPreview.ShootPause(outDir, "settings_visuals_end", SettingsTab.Visuals, scrollTo: 0f);
                DebugBenchPreview.ShootPause(outDir, "settings_visuals_pad", SettingsTab.Visuals, padWalk: true);
                DebugBenchPreview.ShootPause(outDir, "settings_gameplay_arcade", SettingsTab.Gameplay, arcade: true);
                DebugBenchPreview.ShootPause(outDir, "settings_system", SettingsTab.System);
            }
            finally
            {
                MenuNav.Trace = null;
                MenuKit.ScreenSizeOverride = Vector2.zero;
                ArcadeMode.Simulate(false);
                RaceHandoff.ClearAll();
            }
            Debug.Log("[NavScroll] DONE, " + fails + " FAIL(s)");
        }

        static int fails;

        static void Fail(string s) { fails++; Debug.LogError("[NavScroll] FAIL " + s); }

        static float Bottom(Component c)
        {
            var v = new Vector3[4];
            ((RectTransform)c.transform).GetWorldCorners(v);
            return v[0].y;
        }

        static string Pos(ScrollRect s) => Mathf.RoundToInt((1f - s.verticalNormalizedPosition) * 100f) + "%";

        /// <summary>Is the rect fully inside the scroll's viewport?</summary>
        static bool InView(ScrollRect s, RectTransform rt)
        {
            var vp = s.viewport != null ? s.viewport : (RectTransform)s.transform;
            var a = new Vector3[4]; var b = new Vector3[4];
            vp.GetWorldCorners(a); rt.GetWorldCorners(b);
            float tol = 0.5f * Mathf.Abs(vp.lossyScale.y);
            return b[0].y >= a[0].y - tol && b[1].y <= a[1].y + tol;
        }

        /// <summary>MenuNavWatch's LateUpdate, by hand: the new control in view,
        /// with its MenuNavScroll.</summary>
        static void Settle(GameObject now)
        {
            if (now == null) return;
            var sr = now.GetComponentInParent<ScrollRect>();
            if (sr == null) return;
            MenuNavWatch.EnsureVisible(sr, (RectTransform)now.transform);
            if (MenuNav.ScrollOf(now.transform) != null && now.GetComponent<MenuNavScroll>() == null)
                now.AddComponent<MenuNavScroll>();
            Canvas.ForceUpdateCanvases();
        }

        static GameObject Press(EventSystem es, MoveDirection dir, List<string> trace)
        {
            trace.Clear();
            var cur = es.currentSelectedGameObject;
            var data = new AxisEventData(es) { moveDir = dir,
                moveVector = dir == MoveDirection.Down ? Vector2.down : Vector2.up };
            ExecuteEvents.Execute(cur, data, ExecuteEvents.moveHandler);
            var now = es.currentSelectedGameObject;
            if (now != cur) Settle(now);
            Canvas.ForceUpdateCanvases();
            return now;
        }

        // ---------------- the car page ----------------
        static void PadDownCarPage(LifeHomeScreen screen, string where)
        {
            // The geometric graph, as the watch builds it one frame in.
            var w = screen.GetComponent<MenuNavWatch>();
            var apply = typeof(MenuNavWatch).GetMethod("ApplyPending", BindingFlags.NonPublic | BindingFlags.Instance);
            if (w != null && apply != null) apply.Invoke(w, null);
            var body = typeof(LifeHomeScreen).GetField("body", BindingFlags.NonPublic | BindingFlags.Instance)
                           ?.GetValue(screen) as RectTransform;
            var scroll = body != null ? body.GetComponentInParent<ScrollRect>() : null;
            var rows = MenuNav.Collect(body);
            if (scroll == null || rows.Count == 0) { Fail(where + " no scroll page or no controls"); return; }
            Selectable last = rows[0];
            foreach (var s in rows) if (Bottom(s) < Bottom(last)) last = s;
            Text faultsHead = null, lastFault = null;
            foreach (var t in body.GetComponentsInChildren<Text>(false))
            {
                if (t.text == "FAULTS") faultsHead = t;
                if (t.text.StartsWith("> ") && (lastFault == null || Bottom(t) < Bottom(lastFault))) lastFault = t;
            }

            var esGo = new GameObject("NavProbeES");
            var es = esGo.AddComponent<EventSystem>();
            var trace = new List<string>();
            MenuNav.Trace = s => trace.Add(s);
            try
            {
                es.SetSelectedGameObject(last.gameObject);
                Settle(last.gameObject);
                Debug.Log("[NavScroll] " + where + " " + rows.Count + " controls; cursor on the lowest, " + last.name +
                          "; page at " + Pos(scroll) + "; last fault row in view: " +
                          (lastFault != null && InView(scroll, lastFault.rectTransform)));
                bool left = false;
                for (int i = 1; i <= 16 && scroll.verticalNormalizedPosition > 0.001f; i++)
                {
                    var now = Press(es, MoveDirection.Down, trace);
                    Debug.Log("[NavScroll] " + where + " DOWN " + i + ": cursor " + (now != null ? now.name : "none") +
                              ", page at " + Pos(scroll) + (trace.Count > 0 ? "  [" + trace[0] + "]" : "  [moved]"));
                    if (now == null || !now.transform.IsChildOf(body)) { left = true; break; }
                }
                bool end = scroll.verticalNormalizedPosition <= 0.001f;
                bool shown = lastFault != null && InView(scroll, lastFault.rectTransform);
                var cur = es.currentSelectedGameObject != null ? es.currentSelectedGameObject.GetComponent<Selectable>() : null;
                var wrap = cur != null ? cur.FindSelectableOnDown() : null;
                Debug.Log("[NavScroll] " + where + " RESULT: page end " + (end ? "REACHED" : "NOT REACHED") +
                          ", FAULTS heading " + (faultsHead != null ? "present" : "absent") +
                          ", last fault row \"" + (lastFault != null ? lastFault.text : "-") + "\" " +
                          (shown ? "ON SCREEN" : "NOT on screen") + "; cursor " + (cur != null ? cur.name : "none") +
                          "; one more DOWN goes to " + (wrap != null ? wrap.name : "nothing") + " (the wrap, the page done)");
                if (left) Fail(where + " a DOWN left the page with page still below it");
                if (!end || !shown || lastFault == null) Fail(where + " the fault list was not reached by pad");

                // And back: UP walks the page back up to the top before it leaves.
                int ups = 0;
                for (; ups < 24; ups++)
                {
                    var before = es.currentSelectedGameObject;
                    var now = Press(es, MoveDirection.Up, trace);
                    if (now == null || !now.transform.IsChildOf(body)) break;
                }
                Debug.Log("[NavScroll] " + where + " UP x" + (ups + 1) + " back to the strip; page at " + Pos(scroll) +
                          " when the cursor left it");
                if (scroll.verticalNormalizedPosition < 0.999f) Fail(where + " UP left the page before its top");
                // The picture: the page where DOWN left it.
                es.SetSelectedGameObject(last.gameObject);
                Settle(last.gameObject);
                for (int i = 0; i < 16 && scroll.verticalNormalizedPosition > 0.001f; i++) Press(es, MoveDirection.Down, trace);
            }
            finally
            {
                MenuNav.Trace = null;
                Object.DestroyImmediate(esGo);
            }
            ReportControlOverlap(body.root, where);
        }

        // ---------------- the settings menu ----------------
        internal static void SettingsProbe(Transform host, string where, float scrollTo, bool padWalk)
        {
            ScrollRect scroll = null;
            foreach (var s in host.GetComponentsInChildren<ScrollRect>(false)) { scroll = s; break; }
            if (scroll == null) { Fail(where + " the settings rows have no scroll list"); return; }
            Canvas.ForceUpdateCanvases();
            if (!padWalk)
            {
                scroll.verticalNormalizedPosition = Mathf.Clamp01(scrollTo);
                Canvas.ForceUpdateCanvases();
                Debug.Log("[NavScroll] " + where + " rows " + scroll.content.rect.height.ToString("0") + " in a list of " +
                          scroll.viewport.rect.height.ToString("0") + ", left at " + Pos(scroll));
                ReportControlOverlap(host, where);
                return;
            }
            var rows = MenuNav.Collect(scroll.content);
            if (rows.Count == 0) { Fail(where + " no rows"); return; }
            var esGo = new GameObject("NavProbeES");
            var es = esGo.AddComponent<EventSystem>();
            var trace = new List<string>();
            try
            {
                es.SetSelectedGameObject(rows[0].gameObject);
                Settle(rows[0].gameObject);
                int outOfView = 0, steps = 0;
                GameObject now = rows[0].gameObject;
                while (steps < rows.Count + 2)
                {
                    steps++;
                    now = Press(es, MoveDirection.Down, trace);
                    if (now == null || !now.transform.IsChildOf(scroll.content)) break;
                    if (!InView(scroll, (RectTransform)now.transform)) outOfView++;
                }
                Debug.Log("[NavScroll] " + where + " pad DOWN x" + steps + " through " + rows.Count + " rows: " +
                          (outOfView == 0 ? "every row in view as the cursor reached it" : outOfView + " ROW(S) OUT OF VIEW") +
                          "; then " + (now != null ? now.name : "nothing") + "; list at " + Pos(scroll));
                if (outOfView > 0) Fail(where + " a selected row was out of view");
                // Leave the cursor on the last row for the picture.
                es.SetSelectedGameObject(rows[rows.Count - 1].gameObject);
                Settle(rows[rows.Count - 1].gameObject);
            }
            finally { Object.DestroyImmediate(esGo); }
            ReportControlOverlap(host, where);
        }

        // ---------------- buttons and text never over buttons ----------------
        /// <summary>
        /// Two controls whose visible rects overlap, or a line of text whose
        /// glyphs sit on a control it does not belong to. What a scroll list's
        /// mask hides is not visible and not counted.
        /// </summary>
        internal static int ReportControlOverlap(Transform root, string where)
        {
            var boxes = new List<(Rect r, Selectable s)>();
            foreach (var s in root.GetComponentsInChildren<Selectable>(false))
            {
                var r = Visible((RectTransform)s.transform, s.transform);
                if (r.width > 1f && r.height > 1f) boxes.Add((r, s));
            }
            int n = 0;
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    var a = boxes[i]; var b = boxes[j];
                    if (a.s.transform.IsChildOf(b.s.transform) || b.s.transform.IsChildOf(a.s.transform)) continue;
                    if (Overlap(a.r, b.r) < 3f) continue;
                    n++;
                    Debug.LogWarning("[NavScroll] OVERLAP " + where + "  BUTTON " + a.s.name + " over BUTTON " + b.s.name);
                }
            foreach (var t in root.GetComponentsInChildren<Text>(false))
            {
                if (string.IsNullOrEmpty(t.text) || t.color.a < 0.05f || t.GetComponentInParent<Selectable>() != null) continue;
                var gen = t.cachedTextGenerator;
                if (gen == null || gen.vertexCount == 0) continue;
                float upp = 1f / Mathf.Max(0.0001f, t.pixelsPerUnit);
                float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                foreach (var v in gen.verts)
                {
                    Vector3 wp = t.rectTransform.TransformPoint(v.position * upp);
                    x0 = Mathf.Min(x0, wp.x); x1 = Mathf.Max(x1, wp.x); y0 = Mathf.Min(y0, wp.y); y1 = Mathf.Max(y1, wp.y);
                }
                var g = Clip(Rect.MinMaxRect(x0, y0, x1, y1), t.transform);
                if (g.width <= 1f || g.height <= 1f) continue;
                foreach (var b in boxes)
                {
                    if (Overlap(g, b.r) < 3f) continue;
                    n++;
                    Debug.LogWarning("[NavScroll] OVERLAP " + where + "  TEXT \"" + Short(t.text) + "\" over BUTTON " + b.s.name);
                }
            }
            Debug.Log("[NavScroll] " + where + " control overlaps: " + n);
            return n;
        }

        static string Short(string s) { s = s.Replace("\n", " "); return s.Length > 40 ? s.Substring(0, 40) + "..." : s; }

        /// <summary>Smaller side of the intersection, in canvas units (0: none).</summary>
        static float Overlap(Rect a, Rect b)
        {
            float ox = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float oy = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            if (ox <= 0f || oy <= 0f) return 0f;
            var cv = Object.FindFirstObjectByType<Canvas>();
            float k = cv != null ? Mathf.Max(0.0001f, cv.transform.lossyScale.y) : 1f;
            return Mathf.Min(ox, oy) / k;
        }

        static Rect Visible(RectTransform rt, Transform from)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Clip(Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y), from);
        }

        /// <summary>The part a RectMask2D above it lets through.</summary>
        static Rect Clip(Rect r, Transform from)
        {
            var m = from.GetComponentInParent<RectMask2D>();
            if (m == null) return r;
            var c = new Vector3[4];
            m.rectTransform.GetWorldCorners(c);
            float x0 = Mathf.Max(r.xMin, c[0].x), y0 = Mathf.Max(r.yMin, c[0].y);
            float x1 = Mathf.Min(r.xMax, c[2].x), y1 = Mathf.Min(r.yMax, c[2].y);
            return x1 > x0 && y1 > y0 ? Rect.MinMaxRect(x0, y0, x1, y1) : Rect.zero;
        }
    }
}
