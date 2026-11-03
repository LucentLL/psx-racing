using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// THE ARCADE'S FRONT END (owner, 2026-11-03, with GT4's Arcade Mode as the
    /// reference: "Race which allows player to race against similar spec
    /// vehicles controlled by AI, Time Trial, or Cruise (which is Free Roam).
    /// All cars and colors should be available. Modifications should be
    /// minimal."). Its own pages in the GT2 amber-on-charcoal chrome (MenuKit),
    /// in the order the reference walks them:
    ///   MODE      RACE / TIME TRIAL / CRUISE, the car, the settings, the title;
    ///   COURSES   every course this build offers, by kind, with its length
    ///             (and the best time, in a time trial);
    ///   CARD      one course from above - its kind, length and climb, NORMAL
    ///             or REVERSE where it has a twin, the car, the options, START;
    ///   CARS      the whole catalog, by era and by drive - no makes: nothing
    ///             new carries a brand (the owner's rule, 2026-10-04);
    ///   CAR       the car on the turntable, its sheet, and every colour its
    ///             shell carries;
    ///   OPTIONS   gearbox, aids, tyres, and rivals (a race) or traffic (a
    ///             cruise), the hour and the weather - then START;
    ///   RESULT    what came back, a time trial's new best, and AGAIN.
    ///
    /// NOTHING HERE TOUCHES A CAREER (ArcadeMode's notes): every drive is a
    /// loaner of the picked car, factory fresh with a full tank; the result
    /// comes back here and is dropped. The only memory is the picks, the
    /// options and the time-trial bests, in PlayerPrefs.
    ///
    /// Built by LifeHomeScreen.Start while an arcade session is on.
    /// <see cref="Open"/> is the entry point, as CityFrontEnd's is, so a tool
    /// can build a page outside play mode.
    /// </summary>
    public class ArcadeFrontEnd : MonoBehaviour
    {
        /// <summary>A page to open on instead of MODE - a tool's way in (the
        /// menu preview, the door tour), read once and cleared: a page name,
        /// then optionally the mode - "courses:trial", "options:cruise" - or
        /// "course:&lt;id&gt;[:race|trial]" for one course's card.</summary>
        public static string PendingPage;

        // ---- the flow, which outlives the scene loads in between ----
        static ArcadeMode.Kind kind = ArcadeMode.Kind.Race;
        static int course = -1;          // catalog index of the course's forward entry
        static bool reverse;             // drive its twin (Name II)
        static string carBack = "mode";  // where the car pages go back to
        static string optBack = "mode";  // where OPTIONS goes back to
        static int eraPick = -1, drivePick = -1;   // the car list's filters, -1 = ALL
        static string cardCar;           // the car on the CAR page (catalog id)

        // ---- the picks, per viewer ----
        const string PrefCar = "psx.arcade.car", PrefSkin = "psx.arcade.skin.",
                     PrefHour = "psx.arcade.hour", PrefWeather = "psx.arcade.weather",
                     PrefTraffic = "psx.arcade.traffic";

        Canvas canvas;
        RectTransform bodyViewport;
        RectTransform body;
        string page = "mode";
        Text statusText;
        float toastExpires;
        CarViewer viewer;
        CarViewer Viewer => viewer != null ? viewer : viewer = gameObject.AddComponent<CarViewer>();

        // The last result, shown on the result page and then dropped.
        string resultHead, resultSub;
        readonly List<string> resultLines = new List<string>();
        bool resultRoam, resultBest;

        // ---- layout: the same column rules as CityFrontEnd / LifeHomeScreen ----
        static float HeaderH => MenuKit.DesignHeight * 0.133f;
        static float BodyH => MenuKit.DesignHeight - HeaderH;
        static float ColL => -MenuKit.HalfWidth * 0.95f;
        static float ColR => MenuKit.HalfWidth * 0.95f;
        static float ColW => ColR - ColL;
        const float Gutter = 22f;
        static float HalfW => Mathf.Min(470f, (ColW - Gutter) * 0.5f);
        static float LeftX => -(HalfW * 2f + Gutter) * 0.5f;
        static float RightX => LeftX + HalfW + Gutter;

        /// <summary>The gold of a button that starts a drive, as MAIN's START wears it.</summary>
        static readonly Color GoBg = new Color(0.62f, 0.48f, 0.12f, 1f);

        // =================== entry ===================
        public void Open()
        {
            MenuKit.EnsureEventSystem();
            canvas = MenuKit.Canvas(transform, "ArcadeCanvas", 10);
            screenWatch = new MenuKit.ScreenWatch();
            MenuKit.Panel(canvas.transform, "Backdrop", MenuKit.Bg);
            MenuKit.GridBackdrop(canvas.transform);
            MenuKit.Scanlines(canvas.transform);

            if (RaceHandoff.ResultReady) { TakeResult(); page = "result"; }
            // Whatever came back - a finish, a quit, a cruise's exit - the
            // request is spent, and nothing of it is banked anywhere.
            RaceHandoff.ClearAll();
            if (!string.IsNullOrEmpty(PendingPage))
            {
                var parts = PendingPage.Split(':');
                PendingPage = null;
                page = parts[0];
                string mode = parts.Length > 1 ? parts[1] : null;
                if (page == "course")
                {
                    course = parts.Length > 1 && TrackCatalog.TryIndexOf(parts[1], out int c) ? c : -1;
                    reverse = false;
                    // A twin named outright is its road with REVERSE picked.
                    if (course >= 0 && TrackCatalog.At(course).Reversed &&
                        TrackCatalog.TryIndexOf(TrackCatalog.At(course).reverseOf, out int fwd))
                    { course = fwd; reverse = true; }
                    mode = parts.Length > 2 ? parts[2] : null;
                    page = "card";
                }
                if (mode == "race") kind = ArcadeMode.Kind.Race;
                else if (mode == "trial") kind = ArcadeMode.Kind.TimeTrial;
                else if (mode == "cruise") kind = ArcadeMode.Kind.Cruise;
            }
            BuildChrome();
            Rebuild();
        }

        void BuildChrome()
        {
            var header = MenuKit.Stretch(canvas.transform, "Header",
                new Vector2(0f, 1f), new Vector2(1f, 1f), 0f, 0f, -HeaderH, 0f,
                new Color(0.03f, 0.03f, 0.07f, 1f));
            MenuKit.Label(header, "ARCADE", MenuKit.Head, new Vector2(0f, 0.5f),
                new Vector2(30f, 18f), TextAnchor.MiddleLeft, MenuKit.Accent, 360f, bold: true);
            var sub = MenuKit.Label(header, "ANY CAR  ·  ANY COURSE  ·  NOTHING BANKED", MenuKit.Small,
                new Vector2(0f, 0.5f), new Vector2(30f, -22f), TextAnchor.MiddleLeft, MenuKit.Dim, 560f);
            MenuKit.FitOneLine(sub, Mathf.Min(560f, MenuKit.HalfWidth * 1.3f));
            MenuKit.Stretch(header, "Rule", new Vector2(0f, 0f), new Vector2(1f, 0f),
                0f, 0f, 0f, 2f, MenuKit.Line);
        }

        static string KindName(ArcadeMode.Kind k) =>
            k == ArcadeMode.Kind.Race ? "RACE" : k == ArcadeMode.Kind.TimeTrial ? "TIME TRIAL" : "CRUISE";

        void Rebuild()
        {
            if (bodyViewport != null) Destroy(bodyViewport.gameObject);
            // The turntable shows only on the CAR page.
            if (viewer != null && page != "carcard") viewer.SetVisible(false);
            bodyViewport = MenuKit.Stretch(canvas.transform, "Body",
                new Vector2(0f, 0f), new Vector2(1f, 1f), 0f, 0f, 0f, -HeaderH);
            body = MenuKit.ScrollBody(bodyViewport);

            switch (page)
            {
                case "courses": BuildCourses(); break;
                case "card": BuildCard(); break;
                case "cars": BuildCars(); break;
                case "carcard": BuildCarCard(); break;
                case "options": BuildOptions(); break;
                case "result": BuildResult(); break;
                default: page = "mode"; BuildMode(); break;
            }

            MenuKit.FitScrollContent(body, BodyH);
            WireNavigation();
        }

        void Go(string to)
        {
            page = to;
            Rebuild();
        }

        // =================== MODE ===================
        void BuildMode()
        {
            float lx = LeftX, rx = RightX, w = HalfW;

            float y = -14f;
            Section(ref y, lx, w, "PICK A MODE");
            ModeButton(ref y, lx, w, "RACE", "Up to seven rivals in cars like yours.", "mode_race",
                () => { kind = ArcadeMode.Kind.Race; course = -1; Go("courses"); });
            ModeButton(ref y, lx, w, "TIME TRIAL", "Alone against the clock - your best on every course.", "mode_trial",
                () => { kind = ArcadeMode.Kind.TimeTrial; course = -1; Go("courses"); });
            bool city = TrackCatalog.TryIndexOfShipped("Charlotte", out _);
            ModeButton(ref y, lx, w, "CRUISE", city ? "Charlotte at 1:1, uptown out to the 485 belt. Nothing scored."
                                                    : "The city is not in this build.", "mode_cruise",
                city ? (UnityEngine.Events.UnityAction)(() => { kind = ArcadeMode.Kind.Cruise; optBack = "mode"; Go("options"); }) : null);

            float ry = -14f;
            Section(ref ry, rx, w, "YOUR CAR");
            var spec = Car();
            var carBtn = Named(MenuKit.Button(body, " ", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(rx, w), ry), new Vector2(w, 56f),
                () => { carBack = "mode"; Go("cars"); }, 20), "car");
            var carName = carBtn.GetComponentInChildren<Text>();
            carName.text = spec != null ? spec.name.ToUpperInvariant() : "PICK A CAR";
            carName.fontSize = 22;
            MenuKit.FitOneLine(carName, w - 24f);
            ry -= 62f;
            var stats = MenuKit.Label(body, spec != null ? CarLine(spec) : "no catalog",
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(rx, ry), TextAnchor.MiddleLeft,
                MenuKit.Dim, w, height: 26f);
            MenuKit.FitOneLine(stats, w);
            ry -= 30f;
            var paint = MenuKit.Label(body, "COLOUR  " + SkinLabel(spec), MenuKit.Tiny, new Vector2(0.5f, 1f),
                new Vector2(rx, ry), TextAnchor.MiddleLeft, MenuKit.Dim, w, height: 26f);
            MenuKit.FitOneLine(paint, w);
            ry -= 40f;

            Section(ref ry, rx, w, "SETTINGS");
            Named(MenuKit.Button(body, "CONTROLS, PICTURE, SOUND", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(rx, w), ry), new Vector2(w, 48f),
                () => OpenSettings((SettingsTab)0), 20), "settings");
            ry -= 54f;
            Named(MenuKit.Button(body, "<  BACK TO TITLE", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(rx, w), ry), new Vector2(w, 48f),
                ArcadeMode.ExitToTitle, 20), "title");
        }

        void ModeButton(ref float y, float x, float w, string label, string line, string name,
                        UnityEngine.Events.UnityAction go)
        {
            Named(MenuKit.Button(body, label, new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(x, w), y),
                new Vector2(w, 60f), go, MenuKit.Head, go != null ? GoBg : (Color?)null), name);
            y -= 66f;
            MenuKit.Para(body, line, MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(x, y), out float h,
                TextAnchor.UpperLeft, MenuKit.Dim, w);
            y -= h + 16f;
        }

        // =================== COURSES ===================
        /// <summary>The kinds the list is grouped by, in the order a player
        /// reaches for them. A course belongs to the first that takes it.</summary>
        static readonly (string head, System.Func<TrackCatalog.TrackDef, bool> takes)[] Kinds =
        {
            ("MOUNTAIN ROADS", t => t.stage && !t.IsDragEvent && !t.IsSprintVariant),
            ("MOUNTAIN SPRINTS", t => t.stage && t.IsSprintVariant),
            ("THE COAST", t => t.IsDragEvent),
            ("CITY COURSES", t => t.IsCityRace && !t.IsDeckRun),
            ("PARKING DECKS", t => t.IsDeckRun),
        };

        /// <summary>A course on the list: offered here, not the free-roam
        /// city, and its FORWARD entry - a reverse twin is the card's REVERSE.</summary>
        static bool Listed(int i)
        {
            if (!TrackCatalog.Offered(i)) return false;
            var t = TrackCatalog.At(i);
            return !t.IsRoam && !t.Reversed;
        }

        void BuildCourses()
        {
            float y = -14f;
            PageHeader(ref y, KindName(kind) + "  ·  PICK A COURSE", () => Go("mode"));
            float rowW = Mathf.Min(ColW, 900f);
            float x = -rowW * 0.5f;
            int shown = 0;
            foreach (var k in Kinds)
            {
                bool head = false;
                for (int i = 0; i < TrackCatalog.Count; i++)
                {
                    if (!Listed(i)) continue;
                    var t = TrackCatalog.At(i);
                    bool earlier = false;
                    foreach (var e in Kinds) { if (e.head == k.head) break; if (e.takes(t)) { earlier = true; break; } }
                    if (earlier || !k.takes(t)) continue;
                    if (!head) { if (shown > 0) y -= 6f; Section(ref y, x, rowW, k.head); head = true; }
                    CourseRow(ref y, x, rowW, i, t);
                    shown++;
                }
            }
            if (shown == 0)
                MenuKit.Para(body, "No courses in this build.", MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(x, y), out _, TextAnchor.UpperLeft, MenuKit.Dim, rowW);
        }

        /// <summary>One course: its name on the left, its length (and in a time
        /// trial the best) on the right. Tap: its card.</summary>
        void CourseRow(ref float y, float x, float w, int index, TrackCatalog.TrackDef t)
        {
            int captured = index;
            var b = Named(MenuKit.Button(body, " ", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(x, w), y), new Vector2(w, 50f),
                () => { course = captured; reverse = false; Go("card"); }, 20), "course_" + t.id);
            b.GetComponentInChildren<Text>().text = "";
            string right = Km(t);
            if (kind == ArcadeMode.Kind.TimeTrial)
            {
                float best = ArcadeMode.Best(t.id);
                right += "  ·  " + (best > 0f ? "BEST " + ArcadeMode.Clock(best) : "NO TIME");
            }
            var num = MenuKit.Label(b.transform, right, 20, new Vector2(1f, 0.5f), new Vector2(-12f, 0f),
                TextAnchor.MiddleRight, MenuKit.Dim, 260f, height: 30f);
            float numW = Mathf.Ceil(num.preferredWidth) + 4f;
            num.rectTransform.sizeDelta = new Vector2(numW, 30f);
            float nameW = w - numW - 40f;
            var name = MenuKit.Label(b.transform, t.name, 20, new Vector2(0f, 0.5f), new Vector2(12f, 0f),
                TextAnchor.MiddleLeft, Color.white, nameW, height: 30f, bold: true);
            MenuKit.FitOneLine(name, nameW);
            if (index == course) MenuKit.MarkTab(b, true);
            y -= 56f;
        }

        static string Km(TrackCatalog.TrackDef t) =>
            (t.RaceMeters / 1000f).ToString("0.0") + " KM" + (t.loop && t.laps > 1 ? "  ·  " + t.laps + " LAPS" : "");

        // =================== CARD ===================
        /// <summary>The course the drive will be: the forward entry, or its
        /// twin when REVERSE is picked and there is one.</summary>
        static int Venue()
        {
            if (course < 0 || course >= TrackCatalog.Count) return -1;
            if (!reverse) return course;
            int twin = Blacklist.TwinOf(course);
            return twin >= 0 ? twin : course;
        }

        void BuildCard()
        {
            int venue = Venue();
            if (venue < 0 || !TrackCatalog.Offered(venue)) { page = "courses"; BuildCourses(); return; }
            var t = TrackCatalog.At(venue);
            float y = -14f;
            PageHeader(ref y, t.name, () => Go("courses"));
            float lx = LeftX, rx = RightX, w = HalfW;

            // ---- left: from above ----
            const float LegendH = 30f;
            float mapSize = Mathf.Clamp(BodyH + y - LegendH - MenuKit.ScrollPad - 4f, 220f, w);
            float mx = lx + (w - mapSize) * 0.5f;
            RouteMap.Build(body, t, new Vector2(0.5f, 1f), new Vector2(mx, y), mapSize);
            var legend = MenuKit.Label(body, RouteMap.LegendFor(t), MenuKit.Tiny, new Vector2(0.5f, 1f),
                new Vector2(lx + w * 0.5f, y - mapSize - 2f), TextAnchor.MiddleCenter, MenuKit.Dim, w, height: 26f);
            MenuKit.FitOneLine(legend, w);

            // ---- right: the card ----
            float ry = y;
            Section(ref ry, rx, w, RouteGeo.KindOf(t) + "  ·  " + KindName(kind));
            float climb = ClimbM(venue);
            string facts = Km(t) + (climb >= 0f ? "  ·  CLIMB " + Mathf.RoundToInt(climb) + " M" : "") +
                           "  ·  " + (t.loop ? (t.laps == 1 ? "1 LAP" : t.laps + " LAPS") : "POINT TO POINT");
            var f = MenuKit.Label(body, facts, MenuKit.Small, new Vector2(0.5f, 1f), new Vector2(rx, ry),
                TextAnchor.MiddleLeft, Color.white, w, height: 28f, bold: true);
            MenuKit.FitOneLine(f, w);
            ry -= 34f;
            if (kind == ArcadeMode.Kind.TimeTrial)
            {
                float best = ArcadeMode.Best(t.id);
                string car = ArcadeMode.BestCar(t.id);
                var bl = MenuKit.Label(body, best > 0f ? "BEST  " + ArcadeMode.Clock(best) + (car.Length > 0 ? "  ·  " + car.ToUpperInvariant() : "")
                                                       : "NO TIME YET", MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(rx, ry), TextAnchor.MiddleLeft, MenuKit.Accent, w, height: 26f, bold: true);
                MenuKit.FitOneLine(bl, w);
                ry -= 32f;
            }
            else
            {
                var rl = MenuKit.Label(body, ArcadeMode.RivalsAt(t) + (ArcadeMode.RivalsAt(t) == 1 ? " RIVAL" : " RIVALS") +
                                             " IN CARS LIKE YOURS", MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(rx, ry), TextAnchor.MiddleLeft, MenuKit.Dim, w, height: 26f, bold: true);
                MenuKit.FitOneLine(rl, w);
                ry -= 32f;
            }

            // NORMAL / REVERSE, where the road has a twin (Name / Name II).
            if (Blacklist.TwinOf(course) >= 0)
            {
                float half = (w - 8f) * 0.5f;
                var n = Named(MenuKit.Button(body, "NORMAL", new Vector2(0.5f, 1f), new Vector2(rx + half * 0.5f, ry),
                    new Vector2(half, 48f), () => { reverse = false; Rebuild(); }, 20), "dir_normal");
                var r = Named(MenuKit.Button(body, "REVERSE", new Vector2(0.5f, 1f), new Vector2(rx + half * 1.5f + 8f, ry),
                    new Vector2(half, 48f), () => { reverse = true; Rebuild(); }, 20), "dir_reverse");
                MenuKit.MarkTab(reverse ? r : n, true);
                ry -= 54f;
            }

            Named(MenuKit.Button(body, kind == ArcadeMode.Kind.TimeTrial ? "START TIME TRIAL  >>" : "START RACE  >>",
                new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(rx, w), ry), new Vector2(w, 56f),
                Launch, 22, GoBg), "start");
            ry -= 62f;
            var spec = Car();
            var carBtn = Named(MenuKit.Button(body, "CAR:  " + (spec != null ? spec.name.ToUpperInvariant() : "PICK A CAR"),
                new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(rx, w), ry), new Vector2(w, 48f),
                () => { carBack = "card"; Go("cars"); }, 20), "car");
            MenuKit.FitOneLine(carBtn.GetComponentInChildren<Text>(), w - 20f);
            ry -= 54f;
            Named(MenuKit.Button(body, "DRIVING OPTIONS", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(rx, w), ry), new Vector2(w, 48f),
                () => { optBack = "card"; Go("options"); }, 20), "options");
            ry -= 58f;
            if (!string.IsNullOrEmpty(t.blurb))
            {
                MenuKit.Para(body, t.blurb, MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(rx, ry),
                    out float bh, TextAnchor.UpperLeft, MenuKit.Dim, w);
                ry -= bh + 8f;
            }
            // The map is OpenStreetMap's where a city course is shown on it.
            if (t.IsCityRace)
                MenuKit.Para(body, CreditsPanel.Line(EditionKind.City), MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(rx, ry), out _, TextAnchor.UpperLeft, MenuKit.Dim, w);
        }

        static readonly Dictionary<int, float> climbs = new Dictionary<int, float>();

        /// <summary>
        /// How far the raced road rises from its lowest point to its highest,
        /// metres - the card's "elevation change". A mountain course's bake
        /// carries its heights; the raced part is cut out of it (a stage's
        /// start line to its finish, a sprint's own two marks on its loop, a
        /// loop the whole way round), and a twin races the same road the other
        /// way. A city course's menu line is flat (TrackCatalog.Sample), so it
        /// has none to show: -1. Read once per course per session.
        /// </summary>
        static float ClimbM(int index)
        {
            if (climbs.TryGetValue(index, out float v)) return v;
            v = -1f;
            var t = TrackCatalog.At(index);
            try
            {
                if (t.stage)
                {
                    var pts = TrackCatalog.Sample(t, TrackCatalog.Spacing);
                    if (pts != null && pts.Count > 2)
                    {
                        float sp = TrackCatalog.Spacing, end = (pts.Count - 1) * sp;
                        float a = 0f, b = end;
                        if (t.IsSprintVariant) { a = Mathf.Min(t.sprintStartM, t.sprintFinishM); b = Mathf.Max(t.sprintStartM, t.sprintFinishM); }
                        else if (!t.loop && t.dragMeters > t.stageStartLineM) { a = t.stageStartLineM; b = t.dragMeters; }
                        int i0 = Mathf.Clamp(Mathf.FloorToInt(a / sp), 0, pts.Count - 1);
                        int i1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Min(b, end) / sp), i0, pts.Count - 1);
                        float lo = float.MaxValue, hi = float.MinValue;
                        for (int i = i0; i <= i1; i++) { lo = Mathf.Min(lo, pts[i].y); hi = Mathf.Max(hi, pts[i].y); }
                        if (hi >= lo) v = hi - lo;
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[Arcade] the climb of " + t.id + ": " + e.Message); }
            climbs[index] = v;
            return v;
        }

        // =================== CARS ===================
        /// <summary>The eras the list can be cut to, by model year.</summary>
        static readonly (string label, int from, int to)[] Eras =
        {
            ("60s & EARLIER", 0, 1969), ("70s", 1970, 1979), ("80s", 1980, 1989),
            ("90s", 1990, 1999), ("2000s", 2000, 9999),
        };
        static readonly string[] Drives = { "FR", "FF", "MR", "4WD", "RR" };

        static bool Passes(CarSpec c, int era, int drive)
        {
            if (era >= 0 && (c.modelYear < Eras[era].from || c.modelYear > Eras[era].to)) return false;
            if (drive >= 0 && c.drv != Drives[drive]) return false;
            return true;
        }

        void BuildCars()
        {
            float y = -14f;
            PageHeader(ref y, "PICK A CAR", () => Go(carBack));
            var spec = Car();
            var now = MenuKit.Label(body, "DRIVING  " + (spec != null ? spec.name.ToUpperInvariant() : "-"),
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Dim, ColW, height: 26f);
            MenuKit.FitOneLine(now, ColW);
            y -= 34f;
            if (!CarCatalog.Ready) return;

            // Two strips of filters: the era, then the drive. ALL first in each.
            var eraLabels = new List<string> { "ALL ERAS" };
            foreach (var e in Eras) eraLabels.Add(e.label);
            FilterStrip(ref y, eraLabels, eraPick + 1, i => { eraPick = i - 1; Rebuild(); }, "era_");
            var driveLabels = new List<string> { "ALL DRIVES" };
            driveLabels.AddRange(Drives);
            FilterStrip(ref y, driveLabels, drivePick + 1, i => { drivePick = i - 1; Rebuild(); }, "drive_");

            var list = new List<CarSpec>();
            foreach (var c in CarCatalog.All) if (c != null && Passes(c, eraPick, drivePick)) list.Add(c);
            list.Sort((a, b) => a.hp != b.hp ? a.hp.CompareTo(b.hp) : string.CompareOrdinal(a.name, b.name));
            Section(ref y, ColL, ColW, list.Count + (list.Count == 1 ? " CAR" : " CARS") + "  ·  BY POWER");

            float rowW = Mathf.Min(ColW, 900f);
            float x = -rowW * 0.5f;
            string mine = CarId();
            foreach (var c in list)
            {
                var captured = c;
                var b = Named(MenuKit.Button(body, " ", new Vector2(0.5f, 1f),
                    new Vector2(0f, y), new Vector2(rowW, 48f),
                    () => { cardCar = captured.id; Go("carcard"); }, 20), "model_" + c.id);
                b.GetComponentInChildren<Text>().text = "";
                string nums = CarSpec.ToHp(c.hp) + " HP  ·  " + c.drv + (c.modelYear > 0 ? "  ·  " + c.modelYear : "");
                var num = MenuKit.Label(b.transform, nums, 20, new Vector2(1f, 0.5f), new Vector2(-12f, 0f),
                    TextAnchor.MiddleRight, MenuKit.Dim, 260f, height: 30f);
                float numW = Mathf.Ceil(num.preferredWidth) + 4f;
                num.rectTransform.sizeDelta = new Vector2(numW, 30f);
                float nameW = rowW - numW - 40f;
                var name = MenuKit.Label(b.transform, c.name.ToUpperInvariant(), 20, new Vector2(0f, 0.5f),
                    new Vector2(12f, 0f), TextAnchor.MiddleLeft, Color.white, nameW, height: 30f, bold: true);
                MenuKit.FitOneLine(name, nameW);
                if (c.id == mine) MenuKit.MarkTab(b, true);
                y -= 54f;
            }
        }

        /// <summary>A row of equal cells, as many to a line as fit; the picked
        /// one marked.</summary>
        void FilterStrip(ref float y, List<string> labels, int picked, System.Action<int> pick, string prefix)
        {
            int cols = Mathf.Clamp(Mathf.FloorToInt((ColW + 10f) / 170f), 2, labels.Count);
            const float Gap = 8f, CellH = 42f;
            float cellW = (ColW - Gap * (cols - 1)) / cols;
            for (int i = 0; i < labels.Count; i++)
            {
                int col = i % cols;
                if (i > 0 && col == 0) y -= CellH + Gap;
                int captured = i;
                var b = Named(MenuKit.Button(body, labels[i], new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(ColL + col * (cellW + Gap), cellW), y),
                    new Vector2(cellW, CellH), () => pick(captured), 18), prefix + i);
                MenuKit.FitOneLine(b.GetComponentInChildren<Text>(), cellW - 14f);
                if (i == picked) MenuKit.MarkTab(b, true);
            }
            y -= CellH + 14f;
        }

        // =================== CAR ===================
        void BuildCarCard()
        {
            var spec = CarCatalog.Get(cardCar) ?? Car();
            if (spec == null) { page = "cars"; BuildCars(); return; }
            float y = -14f;
            PageHeader(ref y, spec.name.ToUpperInvariant(), () => Go("cars"));
            float lx = LeftX, rx = RightX, w = HalfW;

            var def = Paint.DefFor(spec);
            int skin = SkinOf(spec, def);

            // ---- left: the car on the turntable, in the colour picked ----
            float viewH = Mathf.Min(w / 1.6f, BodyH + y - MenuKit.ScrollPad - 40f);
            if (def != null)
            {
                Viewer.Show(def, skin, CarModelLibrary.SpecGeometry.Of(spec));
                var panel = MenuKit.Rect(body, "CarViewPanel", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(lx + w * 0.5f, y), new Vector2(w, viewH), new Color(0f, 0f, 0f, 0.6f));
                Viewer.AttachTo(panel);
                Viewer.SetVisible(true);
                Hairline(panel);
                var hint = MenuKit.Label(body, "DRAG TO TURN, TAP TO SPIN", MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(lx + w * 0.5f, y - viewH - 4f), TextAnchor.MiddleCenter, MenuKit.Dim, w, height: 24f);
                MenuKit.FitOneLine(hint, w);
            }

            // ---- right: the sheet, the colours, DRIVE IT ----
            float ry = y;
            Section(ref ry, rx, w, "THE CAR");
            Row(ref ry, rx, w, "YEAR", spec.modelYear > 0 ? spec.modelYear.ToString() : "-");
            Row(ref ry, rx, w, "POWER", CarSpec.ToHp(spec.hp) + " HP" + (spec.redline > 0 ? "  ·  " + spec.redline.ToString("N0") + " RPM" : ""));
            Row(ref ry, rx, w, "WEIGHT", spec.kg.ToString("N0") + " KG");
            Row(ref ry, rx, w, "DRIVE", spec.drv + "  ·  " + DriveSide.Label(spec.id) + (spec.gears > 0 ? "  ·  " + spec.gears + " SPEED" : ""));
            if (spec.topSpeedMps > 1f) Row(ref ry, rx, w, "TOP SPEED", Mathf.RoundToInt(spec.topSpeedMps * 3.6f) + " KM/H");
            ry -= 6f;

            int n = def != null ? def.SkinCount : 0;
            Section(ref ry, rx, w, n <= 1 ? "COLOUR  ·  THE ONE IT COMES IN" : "COLOUR  ·  " + n + " TO PICK FROM");
            if (n > 0)
            {
                int cols = Mathf.Clamp(Mathf.FloorToInt((w + 8f) / 64f), 3, 8);
                const float Gap = 8f;
                float chip = Mathf.Min(56f, (w - Gap * (cols - 1)) / cols);
                for (int i = 0; i < n; i++)
                {
                    int col = i % cols;
                    if (i > 0 && col == 0) ry -= chip + Gap;
                    int captured = i;
                    var b = Named(MenuKit.Button(body, " ", new Vector2(0.5f, 1f),
                        new Vector2(MenuKit.ColLeft(rx + col * (chip + Gap), chip), ry), new Vector2(chip, chip),
                        () => { SetSkin(spec, def, captured); Rebuild(); }, 16, Paint.ColorOf(def, i)), "paint_" + i);
                    if (i == skin) MenuKit.MarkTab(b, true);
                }
                ry -= chip + 8f;
                var cl = MenuKit.Label(body, ColourName(def, skin), MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(rx, ry), TextAnchor.MiddleLeft, Color.white, w, height: 26f, bold: true);
                MenuKit.FitOneLine(cl, w);
                ry -= 34f;
            }

            Named(MenuKit.Button(body, spec.id == CarId() ? "DRIVING THIS CAR  ·  BACK" : "DRIVE THIS CAR  >>",
                new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(rx, w), ry), new Vector2(w, 56f),
                () => { SetCar(spec.id); Toast(spec.name.ToUpperInvariant()); Go(carBack); }, 22, GoBg), "use");
        }

        void Row(ref float y, float x, float w, string head, string value)
        {
            MenuKit.Label(body, head, MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(x, y), TextAnchor.MiddleLeft,
                MenuKit.Dim, 150f, height: 28f, bold: true);
            var v = MenuKit.Label(body, value, MenuKit.Small, new Vector2(0.5f, 1f), new Vector2(x + 150f, y),
                TextAnchor.MiddleLeft, Color.white, w - 150f, height: 28f);
            MenuKit.FitOneLine(v, w - 150f);
            y -= 32f;
        }

        /// <summary>A gold hairline round the turntable's window, as the
        /// garage draws one (a dark panel on a dark page reads as nothing).</summary>
        static void Hairline(RectTransform panel)
        {
            foreach (var edge in new[]
            {
                new[] { 0f, 0f, 1f, 0f }, new[] { 0f, 1f, 1f, 1f },
                new[] { 0f, 0f, 0f, 1f }, new[] { 1f, 0f, 1f, 1f },
            })
            {
                var rt = MenuKit.Stretch(panel, "Edge", new Vector2(edge[0], edge[1]), new Vector2(edge[2], edge[3]),
                    0f, 0f, 0f, 0f, MenuKit.Line);
                if (edge[1] == edge[3]) { rt.offsetMin = new Vector2(0f, edge[1] > 0.5f ? -2f : 0f); rt.offsetMax = new Vector2(0f, edge[1] > 0.5f ? 0f : 2f); }
                if (edge[0] == edge[2]) { rt.offsetMin = new Vector2(edge[0] > 0.5f ? -2f : 0f, 0f); rt.offsetMax = new Vector2(edge[0] > 0.5f ? 0f : 2f, 0f); }
                var img = rt.GetComponent<Image>();
                if (img != null) img.raycastTarget = false;
            }
        }

        // =================== OPTIONS ===================
        void BuildOptions()
        {
            float y = -14f;
            PageHeader(ref y, KindName(kind) + "  ·  DRIVING OPTIONS", () => Go(optBack));
            float w = Mathf.Min(ColW, 640f);
            float x = -w * 0.5f;

            if (kind == ArcadeMode.Kind.Cruise)
            {
                var spec = Car();
                var carBtn = Named(MenuKit.Button(body, "CAR:  " + (spec != null ? spec.name.ToUpperInvariant() : "PICK A CAR"),
                    new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(x, w), y), new Vector2(w, 48f),
                    () => { carBack = "options"; Go("cars"); }, 20), "car");
                MenuKit.FitOneLine(carBtn.GetComponentInChildren<Text>(), w - 20f);
                y -= 58f;
            }
            Option(ref y, x, w, "GEARBOX:  " + (ArcadeMode.Manual ? "MANUAL" : "AUTOMATIC"), "opt_gearbox",
                () => ArcadeMode.Manual = !ArcadeMode.Manual);
            Option(ref y, x, w, "DRIVING AIDS:  " + ArcadeMode.AidNames[ArcadeMode.Aids], "opt_aids",
                () => ArcadeMode.Aids = (ArcadeMode.Aids + 1) % ArcadeMode.AidNames.Length);
            Option(ref y, x, w, "TYRES:  " + ArcadeMode.TyreNames[ArcadeMode.Tyres], "opt_tyres",
                () => ArcadeMode.Tyres = (ArcadeMode.Tyres + 1) % ArcadeMode.TyreNames.Length);
            if (kind == ArcadeMode.Kind.Race)
            {
                var t = Venue() >= 0 ? TrackCatalog.At(Venue()) : null;
                int most = t != null ? RaceField.MaxFor(t) - 1 : RaceField.MaxCars - 1;
                Option(ref y, x, w, "RIVALS:  " + Mathf.Min(ArcadeMode.Rivals, most) +
                                    (ArcadeMode.Rivals > most ? "  (THIS COURSE RACES " + most + ")" : ""), "opt_rivals",
                    () => ArcadeMode.Rivals = ArcadeMode.Rivals % (RaceField.MaxCars - 1) + 1);
            }
            else if (kind == ArcadeMode.Kind.Cruise)
                Option(ref y, x, w, "TRAFFIC:  " + TrafficLevels.Name(Traffic), "opt_traffic",
                    () => SetTraffic((Traffic + 1) % TrafficLevels.Count));
            Option(ref y, x, w, "HOUR:  " + TimeOfDay.Label(Hour), "opt_hour",
                () => SetHour((Hour + 1) % TimeOfDay.Count));
            Option(ref y, x, w, "WEATHER:  " + WeatherName(WeatherPick), "opt_weather",
                () => SetWeather((WeatherPick + 1) % 4));
            y -= 6f;
            Named(MenuKit.Button(body, kind == ArcadeMode.Kind.Cruise ? "START CRUISE  >>"
                                     : kind == ArcadeMode.Kind.TimeTrial ? "START TIME TRIAL  >>" : "START RACE  >>",
                new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(x, w), y), new Vector2(w, 56f),
                Launch, 22, GoBg), "start");
            y -= 66f;
            MenuKit.Para(body, "Aids: how much the car helps you catch a slide - there is no ABS. " +
                "Tyres: grip over stock, up to a fifth more. Pressing a shift key takes the automatic to manual; M hands it back.",
                MenuKit.MinLabelSize, new Vector2(0.5f, 1f), new Vector2(x, y), out _, TextAnchor.UpperLeft, MenuKit.Dim, w);
        }

        void Option(ref float y, float x, float w, string label, string name, System.Action change)
        {
            var b = Named(MenuKit.Button(body, label, new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(x, w), y),
                new Vector2(w, 48f), () => { change(); Rebuild(); }, 20), name);
            MenuKit.FitOneLine(b.GetComponentInChildren<Text>(), w - 20f);
            y -= 54f;
        }

        // =================== RESULT ===================
        /// <summary>Read what the scene handed back into lines for the result
        /// page, and a time trial's finish against the course's best. Nothing
        /// else is kept. A drive QUIT from the pause menu carries no result and
        /// lands on MODE instead.</summary>
        void TakeResult()
        {
            resultLines.Clear();
            resultBest = false;
            resultRoam = RaceHandoff.FreeRoam;
            int venue = RaceHandoff.TrackIndex;
            if (resultRoam)
            {
                resultHead = "CRUISE";
                resultSub = "CHARLOTTE";
                resultLines.Add((RaceHandoff.MetersDriven / 1000f).ToString("0.0") + " KM DRIVEN  ·  " +
                                ArcadeMode.Clock(RaceHandoff.RaceTimeSeconds));
                if (RaceHandoff.DriftSeconds > 0.5f)
                    resultLines.Add(RaceHandoff.DriftSeconds.ToString("0.0") + " S SIDEWAYS");
            }
            else
            {
                var t = venue >= 0 && venue < TrackCatalog.Count ? TrackCatalog.At(venue) : null;
                resultSub = t != null ? t.name : "THE COURSE";
                bool trial = RaceHandoff.Solo;
                float time = RaceHandoff.RaceTimeSeconds;
                if (trial)
                {
                    resultHead = time > 0f ? ArcadeMode.Clock(time) : "NO TIME";
                    var spec = CarCatalog.Get(RaceHandoff.CarSpecId);
                    if (t != null && time > 0f && RaceHandoff.FinishPos > 0)
                    {
                        resultBest = ArcadeMode.OfferBest(t.id, time, spec != null ? spec.name : "");
                        float best = ArcadeMode.Best(t.id);
                        resultLines.Add(resultBest ? "A NEW BEST ON THIS COURSE" : "BEST  " + ArcadeMode.Clock(best));
                    }
                }
                else
                {
                    int pos = RaceHandoff.FinishPos, field = RaceHandoff.FieldSize;
                    resultHead = pos > 0 ? "P" + pos + (field > 0 ? " OF " + field : "") : "FINISHED";
                    resultLines.Add("TIME  " + ArcadeMode.Clock(time));
                }
                if (RaceHandoff.BestLapSeconds > 0f && t != null && t.loop && t.laps > 1)
                    resultLines.Add("BEST LAP  " + ArcadeMode.Clock(RaceHandoff.BestLapSeconds));
            }
            if (RaceHandoff.HardHits > 0)
                resultLines.Add(RaceHandoff.HardHits + (RaceHandoff.HardHits == 1 ? " HARD HIT" : " HARD HITS"));
        }

        void BuildResult()
        {
            if (string.IsNullOrEmpty(resultHead)) { page = "mode"; BuildMode(); return; }
            float y = -14f;
            PageHeader(ref y, "RESULT", () => Go("mode"));
            float w = Mathf.Min(ColW, 640f);
            float x = -w * 0.5f;
            var sub = MenuKit.Label(body, resultSub, MenuKit.Small, new Vector2(0.5f, 1f), new Vector2(x, y),
                TextAnchor.MiddleLeft, MenuKit.Dim, w, height: 30f, bold: true);
            MenuKit.FitOneLine(sub, w);
            y -= 36f;
            MenuKit.Label(body, resultHead, MenuKit.Title, new Vector2(0.5f, 1f), new Vector2(x, y),
                TextAnchor.MiddleLeft, resultBest ? MenuKit.Active : MenuKit.Accent, w, height: 52f, bold: true);
            y -= 60f;
            foreach (var line in resultLines)
            {
                var l = MenuKit.Label(body, line, MenuKit.Body, new Vector2(0.5f, 1f), new Vector2(x, y),
                    TextAnchor.MiddleLeft, Color.white, w, height: 34f);
                MenuKit.FitOneLine(l, w);
                y -= 38f;
            }
            y -= 12f;
            float third = (w - 16f) / 3f;
            Named(MenuKit.Button(body, "AGAIN", new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(x, third), y),
                new Vector2(third, 52f), Launch, 20, GoBg), "again");
            Named(MenuKit.Button(body, resultRoam ? "OPTIONS" : "COURSES", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(x + third + 8f, third), y), new Vector2(third, 52f),
                () => { if (resultRoam) { optBack = "mode"; Go("options"); } else Go("courses"); }, 20), "courses");
            Named(MenuKit.Button(body, "ARCADE MENU", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(x + (third + 8f) * 2f, third), y), new Vector2(third, 52f),
                () => Go("mode"), 20), "menu");
        }

        // =================== launching ===================
        /// <summary>START (and AGAIN): the request for the mode, the course and
        /// the car, written into the handoff, then the scene.</summary>
        void Launch()
        {
            var spec = Car();
            if (spec == null) { Toast("PICK A CAR"); return; }
            if (kind == ArcadeMode.Kind.Cruise)
            {
                if (!TrackCatalog.TryIndexOfShipped("Charlotte", out int roam)) { Toast("THE CITY IS NOT IN THIS BUILD"); return; }
                int rs = TrackCatalog.SceneIndex(roam);
                if (!TrackCatalog.SceneShipped(rs)) { Toast("THE CITY IS NOT IN THIS BUILD"); return; }
                FillCar(spec);
                RaceHandoff.FreeRoam = true;
                RaceHandoff.TrackIndex = roam;
                RaceHandoff.TrafficLevel = Traffic;
                // Charlotte's data is its own download: SceneGate waits for it.
                SceneGate.Load(rs);
                return;
            }
            int venue = Venue();
            if (venue < 0 || !TrackCatalog.Offered(venue)) { Toast("PICK A COURSE"); Go("courses"); return; }
            int scene = TrackCatalog.SceneIndex(venue);
            if (!TrackCatalog.SceneShipped(scene)) { Toast("THAT COURSE IS NOT IN THIS BUILD"); return; }
            FillCar(spec);
            RaceHandoff.TrackIndex = venue;
            // An arcade course is the road and the rivals: no traffic on it.
            RaceHandoff.TrafficLevel = TrafficLevels.None;
            if (kind == ArcadeMode.Kind.TimeTrial) RaceHandoff.Solo = true;
            // A field the catalog cannot fill leaves the scene's own grid: a
            // worse race, never a broken one.
            else ArcadeMode.FillField(spec, ArcadeMode.RivalsAt(TrackCatalog.At(venue)));
            SceneGate.Load(scene);
        }

        /// <summary>A LOANER: the catalog car in the colour picked, factory
        /// fresh, a full tank, the arcade's tyre stage and no other part -
        /// which ClearAll leaves every other car field at. FromLifeSim so the
        /// scene applies the car and the field and a finish comes back to
        /// scene 0; the session keeps the career out of it (ArcadeMode).</summary>
        static void FillCar(CarSpec spec)
        {
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.CarId = "";
            RaceHandoff.CarSpecId = spec.id;
            var def = Paint.DefFor(spec);
            int skin = SkinOf(spec, def);
            RaceHandoff.CarPaintSkin = def != null && def.skinNames != null && skin >= 0 && skin < def.skinNames.Length
                ? def.skinNames[skin] : null;
            RaceHandoff.StartFuelPct = 100f;
            RaceHandoff.TimeOfDayIndex = Hour;
            RaceHandoff.WeatherOverride = WeatherPick;
            // The fall the scenes were baked as, as the CITY front end races.
            RaceHandoff.CalendarDay = 0;
            RaceHandoff.UpTires = ArcadeMode.TyreStage;
            EngineVoiceLibrary.PrefetchSpec(spec.id);
        }

        // =================== the picks ===================
        static string CarId()
        {
            string id = PlayerPrefs.GetString(PrefCar, "");
            if (!string.IsNullOrEmpty(id) && CarCatalog.Get(id) != null) return id;
            return CityFrontEnd.DefaultCarId();
        }

        static CarSpec Car() => CarCatalog.Get(CarId());

        static void SetCar(string id)
        {
            PlayerPrefs.SetString(PrefCar, id ?? "");
            PlayerPrefs.Save();
        }

        /// <summary>The colour picked for this car, else the one it left the
        /// factory in (Paint.FactorySkin).</summary>
        static int SkinOf(CarSpec spec, CarModelDef def)
        {
            if (spec == null || def == null || def.SkinCount == 0) return 0;
            int i = Paint.IndexOf(def, PlayerPrefs.GetString(PrefSkin + spec.id, ""));
            return i >= 0 ? i : Paint.FactorySkin(spec, def);
        }

        static void SetSkin(CarSpec spec, CarModelDef def, int skin)
        {
            if (spec == null || def == null || def.skinNames == null || skin < 0 || skin >= def.skinNames.Length) return;
            PlayerPrefs.SetString(PrefSkin + spec.id, def.skinNames[skin]);
            PlayerPrefs.Save();
        }

        static string SkinLabel(CarSpec spec)
        {
            var def = spec != null ? Paint.DefFor(spec) : null;
            return def != null ? ColourName(def, SkinOf(spec, def)) : "-";
        }

        static readonly string[] ColourWords =
        {
            "red", "blue", "white", "black", "silver", "grey", "gray", "yellow", "green", "orange", "purple",
            "pink", "gold", "brown", "beige", "maroon", "navy", "teal", "cyan", "violet", "bronze", "copper",
            "champagne", "cream", "wine", "burgundy", "lime", "midnight", "pearl", "metallic", "dark", "light",
        };

        /// <summary>
        /// A livery's name for a player: the colour words in its baked name
        /// ("midnight_purple" - MIDNIGHT PURPLE), never the file stem a shell
        /// made from one sheet carries ("s13_512x256" read as a debug dump on
        /// the first preview). A sheet with no colour in its name is FACTORY
        /// COLOUR when it is the car's only one, else its place in the list:
        /// the swatch is the mean of the whole sheet - glass and tyres too -
        /// so it is no guide to a name.
        /// </summary>
        static string ColourName(CarModelDef def, int skin)
        {
            int n = def != null ? def.SkinCount : 0;
            string raw = def != null && def.skinNames != null && skin >= 0 && skin < def.skinNames.Length
                ? def.skinNames[skin] : null;
            if (!string.IsNullOrEmpty(raw))
            {
                var words = new List<string>();
                foreach (var w in raw.ToLowerInvariant().Split('_', '-', ' ', '.'))
                    if (System.Array.IndexOf(ColourWords, w) >= 0) words.Add(w == "gray" ? "grey" : w);
                if (words.Count > 0) return string.Join(" ", words).ToUpperInvariant();
            }
            return n <= 1 ? "FACTORY COLOUR" : "COLOUR " + (Mathf.Clamp(skin, 0, n - 1) + 1) + " OF " + n;
        }

        static int Hour => Mathf.Clamp(PlayerPrefs.GetInt(PrefHour, 3), 0, TimeOfDay.Count - 1);
        static void SetHour(int h) { PlayerPrefs.SetInt(PrefHour, h); PlayerPrefs.Save(); }

        static int WeatherPick => Mathf.Clamp(PlayerPrefs.GetInt(PrefWeather, (int)Weather.Clear), 0, 3);
        static void SetWeather(int w) { PlayerPrefs.SetInt(PrefWeather, w); PlayerPrefs.Save(); }

        static string WeatherName(int w) => ((Weather)Mathf.Clamp(w, 0, 3)).ToString().ToUpperInvariant();

        static int Traffic => Mathf.Clamp(PlayerPrefs.GetInt(PrefTraffic, TrafficLevels.Medium), 0, TrafficLevels.Count - 1);
        static void SetTraffic(int l) { PlayerPrefs.SetInt(PrefTraffic, l); PlayerPrefs.Save(); }

        static string CarLine(CarSpec c) =>
            CarSpec.ToHp(c.hp) + " HP  ·  " + c.kg.ToString("N0") + " KG  ·  " + c.drv +
            (c.modelYear > 0 ? "  ·  " + c.modelYear : "");

        // =================== chrome bits ===================
        void Section(ref float y, float x, float w, string text)
        {
            var l = MenuKit.Label(body, text, MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(x, y),
                TextAnchor.MiddleLeft, MenuKit.Accent, w, height: 26f, bold: true);
            MenuKit.FitOneLine(l, w);
            y -= 32f;
        }

        void PageHeader(ref float y, string title, UnityEngine.Events.UnityAction back)
        {
            var t = MenuKit.Label(body, title, MenuKit.Head, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, MenuKit.Accent, ColW - 170f, height: 40f, bold: true);
            MenuKit.FitOneLine(t, ColW - 170f);
            Named(MenuKit.Button(body, "<  BACK", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColRight(ColR, 140f), y - 1f), new Vector2(140f, 38f), back, 16), "back_hdr");
            y -= 52f;
        }

        static Button Named(Button b, string name)
        {
            if (b != null) b.gameObject.name = "Btn_" + name;
            return b;
        }

        void WireNavigation()
        {
            var rows = MenuNav.Collect(body);
            MenuNav.Column(rows);
            var none = new List<Selectable>();
            Selectable first = rows.Count > 0 ? rows[0] : null;
            // Land on what the page is for, not on its BACK.
            foreach (var s in rows)
                if (s != null && (s.name == "Btn_start" || s.name == "Btn_again" || s.name == "Btn_mode_race")) { first = s; break; }
            MenuNav.Select(first);
            var watch = MenuNav.Watch(gameObject, first);
            MenuNav.Defer(watch, none, rows, null);
        }

        void Toast(string msg)
        {
            if (statusText != null) Destroy(statusText.transform.parent.gameObject);
            var box = MenuKit.Rect(canvas.transform, "Toast",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 24f), new Vector2(780f, 52f), new Color(0f, 0f, 0f, 0.78f));
            statusText = MenuKit.Label(box, msg, 18, new Vector2(0.5f, 0.5f),
                Vector2.zero, TextAnchor.MiddleCenter, MenuKit.Accent, 760f, bold: true);
            statusText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            MenuKit.FitOneLine(statusText, 750f);
            toastExpires = Time.unscaledTime + 3f;
        }

        // The settings menu, its own page over this one (as CityFrontEnd's).
        SettingsPanel settings;

        void OpenSettings(SettingsTab at)
        {
            if (settings == null)
            {
                settings = gameObject.AddComponent<SettingsPanel>();
                settings.onClosed = () =>
                {
                    if (this == null || body == null) return;
                    var b = body.Find("Btn_settings");
                    if (b != null) MenuNav.Select(b.GetComponent<Selectable>());
                };
            }
            settings.Open(at);
        }

        // =================== the screen changing size ===================
        MenuKit.ScreenWatch screenWatch;

        internal bool TickScreenWatch()
        {
            if (screenWatch == null || canvas == null || !screenWatch.Settled()) return false;
            MenuKit.Rescale(canvas);
            KillNow(canvas.transform.Find("Header"));
            KillNow(bodyViewport);
            bodyViewport = null;
            BuildChrome();
            Rebuild();
            canvas.transform.Find("Toast")?.SetAsLastSibling();
            return true;
        }

        static void KillNow(Component c)
        {
            if (c == null) return;
            if (Application.isPlaying) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
            else DestroyImmediate(c.gameObject);
        }

        void Update()
        {
            TickScreenWatch();
            if (settings != null && (settings.IsOpen || settings.ClosedFrame == Time.frameCount)) return;
            if (statusText != null && Time.unscaledTime >= toastExpires)
            {
                Destroy(statusText.transform.parent.gameObject);
                statusText = null;
            }
            var pad = UnityEngine.InputSystem.Gamepad.current;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            bool cancel = (pad != null && pad.buttonEast.wasPressedThisFrame) ||
                          (kb != null && kb.escapeKey.wasPressedThisFrame);
            if (!cancel) return;
            // B / Escape walks one page back; MODE stays (the title is a button).
            if (page == "carcard") Go("cars");
            else if (page == "cars") Go(carBack);
            else if (page == "options") Go(optBack);
            else if (page == "card") Go("courses");
            else if (page != "mode") Go("mode");
        }
    }
}
