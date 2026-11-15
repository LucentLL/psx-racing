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
    /// EVENTS (v29) pictures: the SANCTIONED and STREET lists, an event's page
    /// (signed up, and open), the event race's pre-race page from the house and
    /// from the line, MAIN's WEEK | EVENTS row, the week grid and the club
    /// list's standings - at the owner's 2000x1077 window, 16:9 and a 640x360
    /// phone. Every picture logs its control overlaps (NavScrollPreview's
    /// check), its clipped text and pad reach (LifeHomePreview's reports), and
    /// a pad walked DOWN from the top control to the page's end.
    ///   menu: PSX Racing/Preview Events -> Screenshots/events
    /// </summary>
    public static class EventsPreview
    {
        static readonly (string name, int w, int h)[] Sizes =
        {
            ("owner_2000x1077", 2000, 1077),
            ("landscape_16x9", 1280, 720),
            ("phone_640x360", 640, 360),
        };

        static int fails;
        static void Fail(string s) { fails++; Debug.LogError("[EventsPreview] FAIL " + s); }

        [MenuItem("PSX Racing/Preview Events")]
        public static void Capture()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string outDir = Path.Combine(root, "Screenshots", "events");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);
            fails = 0;
            try
            {
                LifeSimManager.DeleteSave();
                LifeSimManager.StartNewGame("VINCE", 25, LifeRules.DefaultJobIndex);
                var s = LifeSimManager.State;
                CarSpec spec = null;
                foreach (var c in CarCatalog.All)
                    if (!c.IsRaceCar && !c.IsRallyCar && c.origin == "jpn" && CarSpec.ToHp(c.hp) <= 170 &&
                        PSXRacing.OnFoot.CarShell.DefFor(c) != null) { spec = c; break; }
                if (spec == null) { Fail("no car"); return; }
                s.cars.Clear();
                var car = CarMarket.MakeOwnedCar(s, spec, 92, 3000f, spec.price);
                car.fuel = 95f;
                s.activeCar = car.id;
                s.money = 9000;
                s.racingRep = 12f;
                s.streetRep = 30f;
                s.blInvited = true;   // the blacklist's invitation toast is not this page's
                var jp = Clubs.ById("japanese");
                Clubs.Invite(s, jp, "preview");
                Clubs.Join(s, jp);
                s.evHeard.Add("tougenight");
                Events.SignUp(s, Events.ById("rookie"), out _);
                Events.SignUp(s, Events.ById("fridaynight"), out _);
                int[] pts = { 34, 52, 18, 9, 27, 0 };
                for (int i = 0; i < Clubs.All.Length && i < pts.Length; i++)
                    Events.ScoreOf(s, Clubs.All[i].id).points = pts[i];
                Events.ScoreOf(s, "japanese").mine = 16;
                Events.Post(s, 1, "US74 R1: BUCK (AMERICAN) WINS");
                Events.Post(s, 1, "Y2K RUN R1: NEO (FUTURE) WINS");
                Duty.lastNotice = null;
                LifeSimManager.Save();
                Debug.Log("[EventsPreview] career: " + car.displayName + ", signed up " + Events.SignedUpCount(s));

                Shoot(outDir, "events_sanctioned", "events", new (string, object)[] { ("evList", "sanctioned"), ("evView", "") });
                Shoot(outDir, "events_street", "events", new (string, object)[] { ("evList", "street"), ("evView", "") });
                Shoot(outDir, "event_page", "events", new (string, object)[] { ("evView", "rookie") });
                Shoot(outDir, "event_page_open", "events", new (string, object)[] { ("evView", "jpcup") });
                Shoot(outDir, "clubs_standings", "club", new (string, object)[] { ("clubView", "") });
                // The bottoms of the long pages: the races, and the standings.
                LifeHomePreview.Shoot(outDir, "event_page_end", "events", scrollTo: 0f, sizes: Sizes,
                    fields: new (string, object)[] { ("evView", "rookie") }, probe: Overlaps);
                LifeHomePreview.Shoot(outDir, "clubs_standings_end", "club", scrollTo: 0f, sizes: Sizes,
                    fields: new (string, object)[] { ("clubView", "") }, probe: Overlaps);
                LifeHomePreview.Shoot(outDir, "events_main", "main", mustFit: true, sizes: Sizes, probe: Overlaps);

                // RACE DAY: SAT 2 MORNING, the ROOKIE CUP's first race.
                s.day = 2; s.slotIndex = LifeRules.MorningSlot;
                LifeSimManager.Save();
                LifeHomePreview.Shoot(outDir, "events_main_raceday", "main", mustFit: true, sizes: Sizes, probe: Overlaps);
                LifeHomePreview.Shoot(outDir, "events_week", "main", calView: "Week", sizes: Sizes, probe: Overlaps);
                Shoot(outDir, "event_prerace_house", "prerace", null);
                Shoot(outDir, "event_prerace_line", "prerace", new (string, object)[] { ("raceFromLine", true) });
            }
            finally
            {
                MenuKit.ScreenSizeOverride = Vector2.zero;
                RaceHandoff.ClearAll();
                Duty.lastNotice = null;
            }
            Debug.Log("[EventsPreview] DONE, " + fails + " FAIL(s)");
        }

        static void Shoot(string outDir, string label, string tab, (string, object)[] fields) =>
            LifeHomePreview.Shoot(outDir, label, tab, sizes: Sizes, fields: fields, probe: WalkDown);

        /// <summary>The pad walk alone, traced, on the two list pages at the
        /// owner's size (a quick look at what DOWN does).</summary>
        public static void CaptureNav()
        {
            string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "events_nav");
            Directory.CreateDirectory(outDir);
            fails = 0;
            traceOn = true;
            try
            {
                LifeSimManager.DeleteSave();
                LifeSimManager.StartNewGame("VINCE", 25, LifeRules.DefaultJobIndex);
                var s = LifeSimManager.State;
                foreach (var c in CarCatalog.All)
                    if (!c.IsRaceCar && !c.IsRallyCar && c.origin == "jpn" && CarSpec.ToHp(c.hp) <= 170 &&
                        PSXRacing.OnFoot.CarShell.DefFor(c) != null)
                    {
                        s.cars.Clear();
                        var car = CarMarket.MakeOwnedCar(s, c, 92, 3000f, c.price);
                        s.activeCar = car.id;
                        break;
                    }
                s.streetRep = 30f;
                LifeSimManager.Save();
                var one = new[] { Sizes[0] };
                LifeHomePreview.Shoot(outDir, "nav_events", "events", sizes: one,
                    fields: new (string, object)[] { ("evList", "sanctioned"), ("evView", "") }, probe: WalkDown);
                LifeHomePreview.Shoot(outDir, "nav_clubs", "club", sizes: one,
                    fields: new (string, object)[] { ("clubView", "") }, probe: WalkDown);
            }
            finally { traceOn = false; MenuNav.Trace = null; MenuKit.ScreenSizeOverride = Vector2.zero; }
            Debug.Log("[EventsPreview] NAV DONE, " + fails + " FAIL(s)");
        }

        static bool traceOn;

        static RectTransform Body(LifeHomeScreen screen) =>
            typeof(LifeHomeScreen).GetField("body", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(screen) as RectTransform;

        static void Overlaps(LifeHomeScreen screen, string where)
        {
            var body = Body(screen);
            if (body == null) { Fail(where + " no body"); return; }
            if (NavScrollPreview.ReportControlOverlap(body.root, where) > 0) Fail(where + " a control or text over a control");
        }

        static float Bottom(Component c)
        {
            var v = new Vector3[4];
            ((RectTransform)c.transform).GetWorldCorners(v);
            return v[0].y;
        }

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

        static GameObject Press(EventSystem es, MoveDirection dir)
        {
            var cur = es.currentSelectedGameObject;
            var data = new AxisEventData(es) { moveDir = dir, moveVector = dir == MoveDirection.Down ? Vector2.down : Vector2.up };
            ExecuteEvents.Execute(cur, data, ExecuteEvents.moveHandler);
            var now = es.currentSelectedGameObject;
            if (now != cur) Settle(now);
            Canvas.ForceUpdateCanvases();
            return now;
        }

        /// <summary>A pad from the page's top control, DOWN to its end: every
        /// control reached, the page end reached, nothing overlapping. The
        /// picture is where the presses left the page.</summary>
        static void WalkDown(LifeHomeScreen screen, string where)
        {
            // World units as screen pixels, as the game's overlay canvas has
            // them: MenuNav's line tolerance has a 4-unit floor, and under the
            // preview's small orthographic camera four units span two rows.
            var camGo = GameObject.Find("PreviewCam");
            var cam = camGo != null ? camGo.GetComponent<Camera>() : null;
            if (cam != null)
            {
                cam.orthographicSize = cam.pixelHeight * 0.5f;
                cam.Render();   // a camera canvas takes its place from the camera when it renders
                Canvas.ForceUpdateCanvases();
            }
            var w = screen.GetComponent<MenuNavWatch>();
            var apply = typeof(MenuNavWatch).GetMethod("ApplyPending", BindingFlags.NonPublic | BindingFlags.Instance);
            if (w != null && apply != null) apply.Invoke(w, null);
            var body = Body(screen);
            var scroll = body != null ? body.GetComponentInParent<ScrollRect>() : null;
            var rows = body != null ? MenuNav.Collect(body) : new List<Selectable>();
            if (scroll == null || rows.Count == 0) { Fail(where + " no scroll page or no controls"); return; }
            Debug.Log("[EventsPreview] " + where + " world scale " + (body.lossyScale.y).ToString("0.0000") +
                      " (canvas units to world; the game's overlay canvas has screen pixels)");
            // The page's own controls, gridded by themselves: in edit mode the
            // strip's BACK has no resolved place yet, and the full-screen grid
            // sends every DOWN to it (a harness artefact - in play the strip
            // sits above the page). The walk checks the page's own order.
            MenuNav.Grid(rows);
            Selectable first = rows[0];
            foreach (var r in rows) if (Bottom(r) > Bottom(first)) first = r;
            var seen = new HashSet<Selectable> { first };
            var esGo = new GameObject("EventsProbeES");
            var es = esGo.AddComponent<EventSystem>();
            bool scrolls = scroll.content.rect.height > scroll.viewport.rect.height + 1f;
            int presses = 0;
            bool endSeen = false;
            var beside = new HashSet<Selectable>();
            if (traceOn) MenuNav.Trace = t => Debug.Log("[EventsPreview] trace " + where + ": " + t);
            try
            {
                es.SetSelectedGameObject(first.gameObject);
                Settle(first.gameObject);
                if (traceOn)
                    foreach (var r in rows)
                        Debug.Log("[EventsPreview] trace " + where + " control " + r.name + " bottom " + Bottom(r).ToString("0.0") +
                                  " down=" + (r.navigation.selectOnDown != null ? r.navigation.selectOnDown.name : "-") +
                                  " mode=" + r.navigation.mode + " interactable=" + r.interactable);
                GameObject cur = first.gameObject;
                for (; presses < 48; presses++)
                {
                    var now = Press(es, MoveDirection.Down);
                    if (traceOn) Debug.Log("[EventsPreview] trace " + where + " DOWN " + (presses + 1) + " -> " +
                                           (now != null ? now.name : "none") + " at " +
                                           (1f - scroll.verticalNormalizedPosition).ToString("0.00"));
                    if (now == null || !now.transform.IsChildOf(body)) break;
                    if (now == cur)
                    {
                        if (!scrolls || scroll.verticalNormalizedPosition <= 0.001f) endSeen = true;
                        continue;   // the page scrolled on under the cursor
                    }
                    var sel = now.GetComponent<Selectable>();
                    // DOWN went UP: the wrap, once the page has nothing left below.
                    if (sel != null && Bottom(sel) > Bottom(cur.GetComponent<Selectable>()) + 0.5f)
                    {
                        if (!scrolls || scroll.verticalNormalizedPosition <= 0.001f) endSeen = true;
                        break;
                    }
                    if (sel != null) seen.Add(sel);
                    cur = now;
                    if (!scrolls || scroll.verticalNormalizedPosition <= 0.001f) endSeen = true;
                }
                // A control BESIDE one the walk reached is LEFT/RIGHT of it.
                foreach (var r in rows)
                    if (!seen.Contains(r))
                        foreach (var t in new List<Selectable>(seen))
                            if (Mathf.Abs(Bottom(r) - Bottom(t)) < 0.5f) { beside.Add(r); break; }
            }
            finally { Object.DestroyImmediate(esGo); if (traceOn) MenuNav.Trace = null; }
            bool end = endSeen;
            Debug.Log("[EventsPreview] " + where + " pad DOWN x" + (presses + 1) + ": reached " + seen.Count + "/" + rows.Count +
                      " controls" + (beside.Count > 0 ? " (+" + beside.Count + " beside one, LEFT/RIGHT)" : "") +
                      ", page end " + (end ? "REACHED" : "NOT REACHED") + (scrolls ? "" : " (fits)"));
            if (seen.Count + beside.Count < rows.Count) Fail(where + " the pad does not reach every control going DOWN");
            if (!end) Fail(where + " the pad does not reach the page end");
            if (NavScrollPreview.ReportControlOverlap(body.root, where) > 0) Fail(where + " a control or text over a control");
        }
    }
}
