using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// THE CITY EDITION'S FRONT END — the Charlotte test page's whole menu.
    ///
    /// The owner, 2026-09-28: "Charlotte map and Charlotte tracks should be in
    /// their own version until being united." That version (see
    /// <see cref="Edition"/>) is the city and its races and nothing else — no
    /// house, no calendar, no money, no town, no mountains — so it cannot use
    /// MAIN's front end, which is a career from its first line. This is the
    /// small one in its place, in the same GT2 amber-on-charcoal chrome
    /// (MenuKit): FREE ROAM, the Charlotte races, a car picker over the whole
    /// catalog, the hour and the weather, and the same OPTIONS rows MAIN has
    /// (LifeHomeScreen.OptionSpecs — one list, two pages).
    ///
    /// NOTHING HERE TOUCHES A CAREER. LifeSimManager.State creates one on
    /// first touch, and the city has none: a race or a drive hands the scene a
    /// factory-fresh loaner of the picked car, the result comes back HERE and
    /// is shown and dropped (LifeRules.ApplyRaceResult never runs), and the
    /// only things remembered are the picks, in PlayerPrefs — which the test
    /// page keeps in its own save database (/idbfs-city).
    ///
    /// Built by LifeHomeScreen.Start when the edition has no career, in the
    /// same LifeHome scene, so the CITY build ships no scene of its own for
    /// it. <see cref="Open"/> is the entry point (not Start) so the menu
    /// preview can build a page with no play mode, as it does MAIN's.
    /// </summary>
    public class CityFrontEnd : MonoBehaviour
    {
        /// <summary>A page to open on instead of FREE ROAM — the menu
        /// preview's way in, read once and cleared (LifeHomeScreen.PendingTab's
        /// contract).</summary>
        public static string PendingPage;
        /// <summary>For the preview's make page: which make to list.</summary>
        public static string PendingMake;

        // The picks, per viewer. PlayerPrefs, and on the test page that is its
        // own IndexedDB (see tools\build-and-publish.ps1 -PagesDir).
        const string PrefCar = "psx.city.car";
        const string PrefHour = "psx.city.hour";
        const string PrefWeather = "psx.city.weather";

        /// <summary>What the data on this page is, and whose. ODbL requires the
        /// OpenStreetMap line wherever the map is shown; the race HUD carries
        /// it in the drive, this carries it at the door.</summary>
        public const string Credits =
            "Map data © OpenStreetMap contributors (ODbL). Elevation: NASA SRTM.";

        Canvas canvas;
        RectTransform bodyViewport;
        RectTransform body;
        string page = "drive";
        string carMake;
        Text statusText;
        float toastExpires;

        // The last result, shown on the result page and then dropped.
        string resultHead, resultSub;
        readonly List<string> resultLines = new List<string>();
        int resultVenue = -1;
        bool resultRoam;

        // ---- layout: the same column rules as LifeHomeScreen ----
        static float HeaderH => MenuKit.DesignHeight * 0.133f;
        static float BodyH => MenuKit.DesignHeight - HeaderH;
        static float ColL => -MenuKit.HalfWidth * 0.95f;
        static float ColR => MenuKit.HalfWidth * 0.95f;
        static float ColW => ColR - ColL;
        const float Gutter = 22f;
        static float HalfW => Mathf.Min(470f, (ColW - Gutter) * 0.5f);
        static float LeftX => -(HalfW * 2f + Gutter) * 0.5f;
        static float RightX => LeftX + HalfW + Gutter;

        // =================== entry ===================
        /// <summary>Build the front end. Banks nothing: a result waiting in
        /// the handoff is read into the result page and cleared.</summary>
        public void Open()
        {
            MenuKit.EnsureEventSystem();
            canvas = MenuKit.Canvas(transform, "CityCanvas", 10);
            MenuKit.Panel(canvas.transform, "Backdrop", MenuKit.Bg);
            MenuKit.GridBackdrop(canvas.transform);
            MenuKit.Scanlines(canvas.transform);

            if (RaceHandoff.ResultReady) { TakeResult(); page = "result"; }
            // Whatever came back — a finish, a quit, a free-roam exit — the
            // request is spent. The CalendarDay the scenes read stays 0: the
            // city has no calendar, so it is the fall the scenes were baked as.
            RaceHandoff.ClearAll();
            if (!string.IsNullOrEmpty(PendingPage)) { page = PendingPage; PendingPage = null; }
            if (!string.IsNullOrEmpty(PendingMake)) { carMake = PendingMake; PendingMake = null; }

            BuildChrome();
            Rebuild();
        }

        void BuildChrome()
        {
            var header = MenuKit.Stretch(canvas.transform, "Header",
                new Vector2(0f, 1f), new Vector2(1f, 1f), 0f, 0f, -HeaderH, 0f,
                new Color(0.03f, 0.03f, 0.07f, 1f));
            MenuKit.Label(header, "CHARLOTTE", MenuKit.Head, new Vector2(0f, 0.5f),
                new Vector2(30f, 18f), TextAnchor.MiddleLeft, MenuKit.Accent, 360f, bold: true);
            MenuKit.Label(header, "CHARLOTTE TEST  ·  THE CITY AND ITS RACES", MenuKit.Small,
                new Vector2(0f, 0.5f), new Vector2(30f, -22f), TextAnchor.MiddleLeft, MenuKit.Dim, 560f);
            MenuKit.Label(header, "CITY EDITION", MenuKit.Small, new Vector2(1f, 0.5f),
                new Vector2(-30f, 18f), TextAnchor.MiddleRight, MenuKit.Dim, 240f, bold: true);
            // The hard rule under the chrome, as the tab strip draws one.
            MenuKit.Stretch(header, "Rule", new Vector2(0f, 0f), new Vector2(1f, 0f),
                0f, 0f, 0f, 2f, MenuKit.Line);
        }

        void Rebuild()
        {
            if (bodyViewport != null) Destroy(bodyViewport.gameObject);
            bodyViewport = MenuKit.Stretch(canvas.transform, "Body",
                new Vector2(0f, 0f), new Vector2(1f, 1f), 0f, 0f, 0f, -HeaderH);
            body = MenuKit.ScrollBody(bodyViewport);

            switch (page)
            {
                case "car": BuildCarPage(); break;
                case "carmake": BuildMakePage(); break;
                case "options": BuildOptions(); break;
                case "result": BuildResult(); break;
                default: page = "drive"; BuildDrive(); break;
            }

            MenuKit.FitScrollContent(body, BodyH);
            WireNavigation();
        }

        void Go(string to)
        {
            page = to;
            Rebuild();
        }

        // =================== the door: FREE ROAM, the races, the car ===================
        void BuildDrive()
        {
            float lx = LeftX, rx = RightX, w = HalfW;

            // ---- left: where ----
            float y = -14f;
            Section(ref y, lx, w, "FREE ROAM");
            bool roamHere = TrackCatalog.TryIndexOfShipped("Charlotte", out int roam);
            Named(MenuKit.Button(body, "FREE ROAM — CHARLOTTE", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(lx, w), y), new Vector2(w, 56f),
                roamHere ? (UnityEngine.Events.UnityAction)(() => StartFreeRoam()) : null, 22), "roam");
            y -= 62f;
            MenuKit.Para(body, "The whole city at 1:1, uptown out to the 485 belt. Nothing is scored.",
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(lx, y), out float ph,
                TextAnchor.UpperLeft, MenuKit.Dim, w);
            y -= ph + 14f;

            Section(ref y, lx, w, "CITY RACES  ·  THREE RIVALS");
            int shown = 0;
            for (int i = 0; i < TrackCatalog.Count; i++)
            {
                var t = TrackCatalog.At(i);
                if (!t.IsCityRace || !TrackCatalog.Offered(i)) continue;
                RaceRow(ref y, lx, w, i, t);
                shown++;
            }
            if (shown == 0)
            {
                MenuKit.Para(body, "No city races in this build.", MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(lx, y), out float nh, TextAnchor.UpperLeft, MenuKit.Dim, w);
                y -= nh;
            }

            // ---- right: in what, and when ----
            float ry = -14f;
            Section(ref ry, rx, w, "YOUR CAR");
            var spec = Car();
            var carBtn = Named(MenuKit.Button(body, " ", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(rx, w), ry), new Vector2(w, 56f),
                () => { carMake = null; Go("car"); }, 20), "car");
            var carName = carBtn.GetComponentInChildren<Text>();
            carName.text = spec != null ? spec.name.ToUpperInvariant() : "PICK A CAR";
            carName.fontSize = 22;
            MenuKit.FitOneLine(carName, w - 24f);
            ry -= 62f;
            var stats = MenuKit.Label(body, spec != null ? CarLine(spec) : "no catalog",
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(rx, ry), TextAnchor.MiddleLeft,
                MenuKit.Dim, w, height: 26f);
            MenuKit.FitOneLine(stats, w);
            ry -= 36f;

            Section(ref ry, rx, w, "WHEN");
            Named(MenuKit.Button(body, "HOUR:  " + TimeOfDay.Label(Hour), new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(rx, w), ry), new Vector2(w, 48f),
                () => { SetHour((Hour + 1) % TimeOfDay.Count); Rebuild(); }, 20), "hour");
            ry -= 54f;
            Named(MenuKit.Button(body, "WEATHER:  " + WeatherName(WeatherPick), new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(rx, w), ry), new Vector2(w, 48f),
                () => { SetWeather((WeatherPick + 1) % 4); Rebuild(); }, 20), "weather");
            ry -= 54f;
            Named(MenuKit.Button(body, "OPTIONS", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(rx, w), ry), new Vector2(w, 48f),
                () => Go("options"), 20), "options");
            ry -= 60f;
            MenuKit.Para(body, Credits, MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(rx, ry),
                out float ch, TextAnchor.UpperLeft, MenuKit.Dim, w);
        }

        /// <summary>One race: its name on the left of the button and how far
        /// it is on the right, so four of them fit a phone with no scroll.</summary>
        void RaceRow(ref float y, float x, float w, int index, TrackCatalog.TrackDef t)
        {
            int captured = index;
            var b = Named(MenuKit.Button(body, " ", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(x, w), y), new Vector2(w, 50f),
                () => StartRace(captured), 20), "race_" + t.id);
            var caption = b.GetComponentInChildren<Text>();
            caption.text = "";
            string km = (t.RaceMeters / 1000f).ToString("0.0") + " KM" +
                        (t.laps > 1 ? "  ·  " + t.laps + " LAPS" : "");
            var num = MenuKit.Label(b.transform, km, 20, new Vector2(1f, 0.5f), new Vector2(-12f, 0f),
                TextAnchor.MiddleRight, MenuKit.Dim, 200f, height: 30f);
            // The numbers take what they need; the name gets the rest, cut
            // at a word if it has to be (no text may clip).
            float numW = Mathf.Ceil(num.preferredWidth) + 4f;
            num.rectTransform.sizeDelta = new Vector2(numW, 30f);
            float nameW = w - numW - 40f;
            var name = MenuKit.Label(b.transform, t.name, 20, new Vector2(0f, 0.5f), new Vector2(12f, 0f),
                TextAnchor.MiddleLeft, Color.white, nameW, height: 30f, bold: true);
            MenuKit.FitOneLine(name, nameW);
            y -= 56f;
        }

        void Section(ref float y, float x, float w, string text)
        {
            var l = MenuKit.Label(body, text, MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(x, y),
                TextAnchor.MiddleLeft, MenuKit.Accent, w, height: 26f, bold: true);
            MenuKit.FitOneLine(l, w);
            y -= 32f;
        }

        static string CarLine(CarSpec c) =>
            c.hp + " HP  ·  " + c.kg.ToString("N0") + " KG  ·  " + c.drv +
            (c.modelYear > 0 ? "  ·  " + c.modelYear : "");

        // =================== the car picker ===================
        /// <summary>The catalog as an index of makes — 317 rows is a list
        /// nobody scrolls with a thumbstick; four dozen makes is a page. The
        /// same index the debug bench's CAR page uses (DebugCarOps.Makes).</summary>
        void BuildCarPage()
        {
            float y = -14f;
            PageHeader(ref y, "PICK A CAR", () => Go("drive"));
            var spec = Car();
            var now = MenuKit.Label(body, "DRIVING  " + (spec != null ? spec.name.ToUpperInvariant() : "—"),
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Dim, ColW, height: 26f);
            MenuKit.FitOneLine(now, ColW);
            y -= 34f;

            var makes = DebugCarOps.Makes();
            int cols = Mathf.Clamp(Mathf.FloorToInt((ColW + 10f) / 250f), 2, 6);
            const float Gap = 10f, CellH = 46f;
            float cellW = (ColW - Gap * (cols - 1)) / cols;
            string mine = spec != null ? DebugCarOps.MakeOf(spec) : null;
            for (int i = 0; i < makes.Count; i++)
            {
                string make = makes[i].Key;
                int col = i % cols;
                if (i > 0 && col == 0) y -= CellH + Gap;
                var b = Named(MenuKit.Button(body, make + "  (" + makes[i].Value + ")", new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(ColL + col * (cellW + Gap), cellW), y),
                    new Vector2(cellW, CellH), () => { carMake = make; Go("carmake"); }, 20), "make_" + make);
                MenuKit.FitOneLine(b.GetComponentInChildren<Text>(), cellW - 16f);
                if (make == mine) MenuKit.MarkTab(b, true);
            }
            y -= CellH + 20f;
        }

        /// <summary>One make's cars. Tap one and it is the car.</summary>
        void BuildMakePage()
        {
            if (string.IsNullOrEmpty(carMake)) { page = "car"; BuildCarPage(); return; }
            float y = -14f;
            var models = DebugCarOps.ModelsOf(carMake);
            PageHeader(ref y, carMake + "  ·  " + models.Count + (models.Count == 1 ? " CAR" : " CARS"),
                () => Go("car"));
            string mine = PlayerPrefs.GetString(PrefCar, "");
            float rowW = Mathf.Min(ColW, 900f);
            float x = -rowW * 0.5f;
            foreach (var c in models)
            {
                var captured = c;
                var b = Named(MenuKit.Button(body, " ", new Vector2(0.5f, 1f),
                    new Vector2(0f, y), new Vector2(rowW, 48f),
                    () => { SetCar(captured.id); Go("drive"); Toast(captured.name.ToUpperInvariant()); },
                    20), "model_" + c.id);
                b.GetComponentInChildren<Text>().text = "";
                string nums = c.hp + " HP  ·  " + c.drv;
                var num = MenuKit.Label(b.transform, nums, 20, new Vector2(1f, 0.5f), new Vector2(-12f, 0f),
                    TextAnchor.MiddleRight, MenuKit.Dim, 200f, height: 30f);
                float numW = Mathf.Ceil(num.preferredWidth) + 4f;
                num.rectTransform.sizeDelta = new Vector2(numW, 30f);
                float nameW = rowW - numW - 40f;
                var name = MenuKit.Label(b.transform, c.name.ToUpperInvariant(), 20, new Vector2(0f, 0.5f),
                    new Vector2(12f, 0f), TextAnchor.MiddleLeft, Color.white, nameW,
                    height: 30f, bold: true);
                MenuKit.FitOneLine(name, nameW);
                if (c.id == mine) MenuKit.MarkTab(b, true);
                y -= 54f;
            }
        }

        // =================== options ===================
        void BuildOptions()
        {
            float y = -14f;
            PageHeader(ref y, "OPTIONS", () => Go("drive"));
            float w = Mathf.Min(ColW, 460f);
            foreach (var o in LifeHomeScreen.OptionSpecs())
            {
                var apply = o.apply;
                Named(MenuKit.Button(body, o.name + ":  " + o.value(), new Vector2(0.5f, 1f),
                    new Vector2(0f, y), new Vector2(w, 48f), () => { apply(); Rebuild(); }, 18),
                    "opt_" + o.name);
                y -= 46f;
                var l = MenuKit.Label(body, o.blurb, 17, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                    TextAnchor.MiddleLeft, MenuKit.Dim, ColW, height: 24f);
                MenuKit.FitOneLine(l, ColW);
                y -= 34f;
            }
            MenuKit.Para(body, "The pause menu inside a drive carries most of these, plus the camera and RESET CAR.",
                17, new Vector2(0.5f, 1f), new Vector2(ColL, y - 8f), out float h,
                TextAnchor.UpperLeft, MenuKit.Dim, ColW);
        }

        // =================== the result ===================
        /// <summary>
        /// Read what the scene handed back — a finish, or a free-roam exit —
        /// into lines for the result page. Nothing is banked: there is no
        /// career to bank it into. A race QUIT from the pause menu carries no
        /// result (PauseMenu clears it) and lands on the door instead.
        /// </summary>
        void TakeResult()
        {
            resultLines.Clear();
            resultRoam = RaceHandoff.FreeRoam;
            resultVenue = RaceHandoff.TrackIndex;
            if (resultRoam)
            {
                resultHead = "FREE ROAM";
                resultSub = "CHARLOTTE";
                resultLines.Add((RaceHandoff.MetersDriven / 1000f).ToString("0.0") + " KM DRIVEN  ·  " +
                                Clock(RaceHandoff.RaceTimeSeconds));
                if (RaceHandoff.DriftSeconds > 0.5f)
                    resultLines.Add(RaceHandoff.DriftSeconds.ToString("0.0") + " S SIDEWAYS");
            }
            else
            {
                var t = resultVenue >= 0 && resultVenue < TrackCatalog.Count ? TrackCatalog.At(resultVenue) : null;
                resultSub = t != null ? t.name : "THE RACE";
                int pos = RaceHandoff.FinishPos, field = RaceHandoff.FieldSize;
                resultHead = pos > 0 ? "P" + pos + (field > 0 ? " OF " + field : "") : "FINISHED";
                resultLines.Add("TIME  " + Clock(RaceHandoff.RaceTimeSeconds));
                if (RaceHandoff.BestLapSeconds > 0f && t != null && t.loop && t.laps > 1)
                    resultLines.Add("BEST LAP  " + Clock(RaceHandoff.BestLapSeconds));
            }
            if (RaceHandoff.HardHits > 0)
                resultLines.Add(RaceHandoff.HardHits + (RaceHandoff.HardHits == 1 ? " HARD HIT" : " HARD HITS"));
        }

        void BuildResult()
        {
            if (string.IsNullOrEmpty(resultHead)) { page = "drive"; BuildDrive(); return; }
            float y = -14f;
            PageHeader(ref y, "RESULT", () => Go("drive"));
            float w = Mathf.Min(ColW, 640f);
            float x = -w * 0.5f;
            var sub = MenuKit.Label(body, resultSub, MenuKit.Small, new Vector2(0.5f, 1f), new Vector2(x, y),
                TextAnchor.MiddleLeft, MenuKit.Dim, w, height: 30f, bold: true);
            MenuKit.FitOneLine(sub, w);
            y -= 36f;
            MenuKit.Label(body, resultHead, MenuKit.Title, new Vector2(0.5f, 1f), new Vector2(x, y),
                TextAnchor.MiddleLeft, MenuKit.Accent, w, height: 52f, bold: true);
            y -= 60f;
            foreach (var line in resultLines)
            {
                var l = MenuKit.Label(body, line, MenuKit.Body, new Vector2(0.5f, 1f), new Vector2(x, y),
                    TextAnchor.MiddleLeft, Color.white, w, height: 34f);
                MenuKit.FitOneLine(l, w);
                y -= 38f;
            }
            y -= 12f;
            int venue = resultVenue;
            bool again = resultRoam ? TrackCatalog.TryIndexOfShipped("Charlotte", out _)
                                    : TrackCatalog.Offered(venue);
            Named(MenuKit.Button(body, resultRoam ? "DRIVE AGAIN" : "RACE AGAIN", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(x, 300f), y), new Vector2(300f, 52f),
                again ? (UnityEngine.Events.UnityAction)(() =>
                {
                    if (resultRoam) StartFreeRoam(); else StartRace(venue);
                }) : null, 22), "again");
            Named(MenuKit.Button(body, "BACK TO CHARLOTTE", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(x + 320f, Mathf.Max(200f, w - 320f)), y),
                new Vector2(Mathf.Max(200f, w - 320f), 52f), () => Go("drive"), 20), "back");
        }

        static string Clock(float seconds)
        {
            if (seconds <= 0f) return "0:00.00";
            int m = Mathf.FloorToInt(seconds / 60f);
            float s = seconds - m * 60f;
            return m + ":" + s.ToString("00.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        // =================== launching ===================
        void StartFreeRoam()
        {
            if (!FillFreeRoam(CarId(), Hour, WeatherPick, out int scene))
            {
                Toast("CHARLOTTE IS NOT IN THIS BUILD");
                return;
            }
            if (!TrackCatalog.TryLoadScene(scene, "Charlotte")) Toast("CHARLOTTE IS NOT IN THIS BUILD");
        }

        void StartRace(int venue)
        {
            if (!FillRace(venue, CarId(), Hour, WeatherPick, out int scene))
            {
                Toast("THAT RACE IS NOT IN THIS BUILD");
                return;
            }
            if (!TrackCatalog.TryLoadScene(scene, TrackCatalog.At(venue).id))
                Toast("THAT RACE IS NOT IN THIS BUILD");
        }

        /// <summary>
        /// The request for a drive round the city, written into the handoff,
        /// with nothing loaded. PUBLIC because the city play check launches
        /// through it under the CITY edition, so the test drives the front
        /// end's own request rather than a copy of it.
        /// </summary>
        public static bool FillFreeRoam(string specId, int hour, int weather, out int scene)
        {
            scene = -1;
            if (!TrackCatalog.TryIndexOfShipped("Charlotte", out int roam)) return false;
            scene = TrackCatalog.SceneIndex(roam);
            if (!TrackCatalog.SceneShipped(scene)) return false;
            FillCar(specId, hour, weather);
            RaceHandoff.FreeRoam = true;
            RaceHandoff.TrackIndex = roam;
            return true;
        }

        /// <summary>The request for one of the city races, with a field
        /// drawn around the picked car. Nothing loaded; see
        /// <see cref="FillFreeRoam"/>.</summary>
        public static bool FillRace(int venue, string specId, int hour, int weather, out int scene)
        {
            scene = -1;
            if (!TrackCatalog.Offered(venue)) return false;
            scene = TrackCatalog.SceneIndex(venue);
            if (!TrackCatalog.SceneShipped(scene)) return false;
            FillCar(specId, hour, weather);
            RaceHandoff.TrackIndex = venue;
            var spec = CarCatalog.Get(specId);
            // The career's draw, around this car at its sticker price and a
            // middle street tier. False leaves the scene's own field standing:
            // a worse race, never a broken one.
            LifeRules.FillOpponentFieldFor(spec, spec != null ? spec.price : 15000, 1);
            return true;
        }

        /// <summary>A LOANER: the catalog car, factory fresh, full tank, no
        /// parts and no faults — which is exactly what ClearAll leaves every
        /// car field at. FromLifeSim so the scene applies the car and a finish
        /// comes back to this scene; nothing on the far side asks for a career
        /// (Edition.HasCareer guards the fuel truck, the bench and the engine).</summary>
        static void FillCar(string specId, int hour, int weather)
        {
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.CarId = "";
            RaceHandoff.CarSpecId = specId ?? "";
            RaceHandoff.StartFuelPct = 100f;
            RaceHandoff.TimeOfDayIndex = Mathf.Clamp(hour, 0, TimeOfDay.Count - 1);
            RaceHandoff.WeatherOverride = Mathf.Clamp(weather, 0, 3);
            RaceHandoff.CalendarDay = 0;
        }

        // =================== the picks ===================
        /// <summary>The picked car's catalog id, or the default one.</summary>
        public static string CarId()
        {
            string id = PlayerPrefs.GetString(PrefCar, "");
            if (!string.IsNullOrEmpty(id) && CarCatalog.Get(id) != null) return id;
            return DefaultCarId();
        }

        static CarSpec Car() => CarCatalog.Get(CarId());

        static void SetCar(string id)
        {
            PlayerPrefs.SetString(PrefCar, id ?? "");
            PlayerPrefs.Save();
        }

        /// <summary>A street car for a street city: the first Silvia, else the
        /// first 240SX, else the first car in the catalog.</summary>
        public static string DefaultCarId()
        {
            if (!CarCatalog.Ready) return "";
            foreach (var want in new[] { "SILVIA", "240SX", "SKYLINE" })
                foreach (var c in CarCatalog.All)
                    if (c.name != null && c.name.ToUpperInvariant().Contains(want)) return c.id;
            return CarCatalog.All[0].id;
        }

        static int Hour => Mathf.Clamp(PlayerPrefs.GetInt(PrefHour, TimeOfDay.Sunset), 0, TimeOfDay.Count - 1);
        static void SetHour(int h) { PlayerPrefs.SetInt(PrefHour, h); PlayerPrefs.Save(); }

        static int WeatherPick => Mathf.Clamp(PlayerPrefs.GetInt(PrefWeather, (int)Weather.Clear), 0, 3);
        static void SetWeather(int w) { PlayerPrefs.SetInt(PrefWeather, w); PlayerPrefs.Save(); }

        static string WeatherName(int w) => ((Weather)Mathf.Clamp(w, 0, 3)).ToString().ToUpperInvariant();

        // =================== chrome bits ===================
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
            // No tab strip here: the page is the whole menu. Creation order
            // first (live on the frame it appears), the geometric graph a
            // frame later, as every MenuKit page does (see MenuNav).
            var rows = MenuNav.Collect(body);
            MenuNav.Column(rows);
            var none = new List<Selectable>();
            Selectable first = rows.Count > 0 ? rows[0] : null;
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
            toastExpires = Time.unscaledTime + 4f;
        }

        void Update()
        {
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
            // B / Escape walks back towards the door, as MAIN's does.
            if (page == "carmake") Go("car");
            else if (page != "drive") Go("drive");
        }
    }
}
