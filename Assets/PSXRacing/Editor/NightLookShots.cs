using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE NIGHT LOOK, PHOTOGRAPHED — the instrument for the 2026-09-21 pass
    /// after Need for Speed (2015). The owner's words: "I like how dark the
    /// night is, how much the skybox effects the color and mood of the world
    /// and cars, how street lights bathe the road. I like the particle effects
    /// on screen for rain and light." Every one of those is a property of a
    /// PICTURE — a floor luminance, a share of the frame under 0.10, a pool of
    /// sodium light on wet tarmac, a droplet glowing where a lamp is behind it
    /// — and none of them throws when it is wrong. So this takes the same
    /// frames every time, through the game's own framebuffer and PSX/Blit, and
    /// tools/night/night_stats.py puts numbers on them against the ones
    /// measured off the reference frames (DESIGN: floor 0.008-0.015, median
    /// 0.09-0.17, 27-54% of the frame under 0.10, saturation ~0.5; ours before
    /// the pass was a milky 0.108 floor, 0.226 median, 0% under 0.10, sat 0.13).
    ///
    /// What is shot (Screenshots\nl_&lt;venue&gt;_&lt;spot&gt;_&lt;hour&gt;[_rain][_lens].png):
    ///   CityCircuit  chase + three-quarter, at NIGHT, at DUSK (blue hour), at
    ///                night in RAIN, and that rain frame through the LENS.
    ///   Charlotte    chase at three streamed spots — an uptown arterial by the
    ///                277 loop's start line, the nearest motorway, the nearest
    ///                residential street with lamps — night, dusk, night rain,
    ///                and the arterial's rain frame through the lens as well.
    ///   BlueRidge    chase at night: a stage has no lamps at all, which is the
    ///                control — the mountain night is sky, headlights and dark.
    /// PSX_NIGHT_ONLY="circuit,city,stage" (any subset) runs part of it. A log
    /// of every frame, with how many street lamps were in the table when it was
    /// drawn, goes to Screenshots\nl_log.txt and the console ("[NightLook]").
    ///
    /// EVERYTHING HERE IS A PLAY-MODE THING DONE BY HAND, because it runs in
    /// edit mode and edit mode calls no Awake, Start or Update on anything
    /// that is not ExecuteAlways. Each of these was a way for the frame to be
    /// silently not the game's:
    ///   * NightGlow converts its lamp heads to halos and registers them with
    ///     the StreetLights table in Awake: NightGlow.PreviewAll does it here.
    ///   * The rain is a particle slab WeatherFx builds in Start and moves in
    ///     LateUpdate: WeatherFx.Preview builds it over the shot camera and
    ///     runs it forward, per frame, or a "rain" frame is a wet road under a
    ///     dry sky.
    ///   * The weather is the CALENDAR's: TimeOfDay.Apply reads it from
    ///     RaceHandoff.CalendarDay, so a rain frame borrows a day whose roll is
    ///     rain and the day is put back afterwards — the owner's editor keeps
    ///     its own.
    ///   * The lens is a URP pass on the base camera (SpeedBlurFeature), fed
    ///     by LensFx, which SpeedBlur fills from the live game every frame:
    ///     LensFx.PreviewSet points it at THE SHOT CAMERA for the lens frames.
    ///   * The city streams tiles round the car at runtime; an opened
    ///     Charlotte.unity is a car in a void until CityWorld.EnsureRing
    ///     builds them.
    ///   * Both lamp tables (street lamps, headlights) are chosen for ONE eye,
    ///     so the camera is stood where the shot is before either is filled.
    /// </summary>
    public static class NightLookShots
    {
        static string RootDir => Directory.GetParent(Application.dataPath).FullName;
        static string OutDir => Path.Combine(RootDir, "Screenshots");

        /// <summary>The lens frames: full rain on the glass, the night's dirt
        /// at 0.8, no speed (the car is parked, so nothing sheds), and a FIXED
        /// clock so every run photographs the same droplets — a before/after
        /// pair whose drops moved between them compares nothing.</summary>
        const float LensRainAmount = 1f, LensDirtAmount = 0.8f, LensClock = 3.7f;
        /// <summary>How long the rain slab is run forward before a frame: a
        /// streak lives 1.3 s, so by 2.5 s the column is full from the slab to
        /// the road rather than a sheet of drops hanging 13 m up.</summary>
        const float RainSettleSeconds = 2.5f;
        /// <summary>Lift over the solved road surface for a car stood on a
        /// city street — the circuits' grid lift, so a city frame and a
        /// circuit frame show the car sitting the same way.</summary>
        const float CarLift = 0.35f;
        /// <summary>How close a street lamp must be for a candidate spot to
        /// count as LIT, m: a 38 m pitch on an arterial, 60 m on a motorway's
        /// outside verge, so a lit stretch always has one inside this.</summary>
        const float LitProbeM = 90f;
        /// <summary>Candidate spots per kind probed for lamps before the
        /// nearest is taken lit or not; each probe builds one 256 m tile.</summary>
        const int MaxProbes = 8;
        /// <summary>Candidates closer together than this are one place, m.</summary>
        const float CandidateSpreadM = 250f;

        struct Variant
        {
            public int hour;
            public bool rain, lens;
            public Variant(int hour, bool rain, bool lens) { this.hour = hour; this.rain = rain; this.lens = lens; }
            public string Suffix =>
                TimeOfDay.At(hour).name.ToLowerInvariant() + (rain ? "_rain" : "") + (lens ? "_lens" : "");
        }

        static StringBuilder log;
        static int shots;
        /// <summary>Venues that threw or were skipped. A capture that lost a
        /// whole venue still writes the others' frames, and a runner that only
        /// counts frames would call it green; the closing verdict line counts
        /// this instead (tools/nightlook-shots.ps1 gates on it).</summary>
        static int failures;
        static int rainDay;
        /// <summary>The variant being shot is a rain one: every frame gets a
        /// settled rain slab over its lens (WeatherFx.Preview).</summary>
        static bool rainOn;
        static readonly List<Canvas> hiddenCanvases = new List<Canvas>();

        [MenuItem("PSX Racing/Night Look Shots")]
        public static void Capture()
        {
            Directory.CreateDirectory(OutDir);
            log = new StringBuilder();
            shots = 0;
            failures = 0;
            Line("NIGHT LOOK SHOTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

            var only = Only();
            int keepDay = RaceHandoff.CalendarDay;
            var keepView = ChaseCamera.Current;
            rainDay = FindRainDay();
            Line(rainDay > 0
                ? "rain frames borrow calendar day " + rainDay + " (its roll is rain)"
                : "no rain day in the first 400 of the calendar: rain frames skipped");
            try
            {
                // The chase view, whatever the editor was last left on: the
                // cluster is two translucent dials here and one binnacle in
                // the cockpit, and it is the translucent pair this pass is
                // about.
                ChaseCamera.PreviewView(ChaseCamera.View.Chase);
                if (only == null || only.Contains("circuit")) Venue("circuit", CaptureCircuit);
                if (only == null || only.Contains("city")) Venue("city", CaptureCity);
                if (only == null || only.Contains("stage")) Venue("stage", CaptureStage);
            }
            finally
            {
                EndVariant();
                RaceHandoff.CalendarDay = keepDay;
                ChaseCamera.PreviewView(keepView);
                Line(shots + " frames written to " + OutDir);
                // The verdict, last, and capitalised so the runner can match it
                // exactly: OK needs every venue shot and at least one frame.
                Line(failures == 0 && shots > 0
                    ? "NIGHT LOOK CAPTURE OK"
                    : "NIGHT LOOK CAPTURE FAILED (" + failures + " venue(s) threw or were skipped, " + shots + " frames)");
                File.WriteAllText(Path.Combine(OutDir, "nl_log.txt"), log.ToString());
                Debug.Log("[NightLook] " + shots + " frames written to " + OutDir + "\n" + log);
            }
        }

        // ------------------------------------------------------------------
        //  The venues
        // ------------------------------------------------------------------

        /// <summary>The city circuit from the two views the hour sweep uses
        /// (PSXScreenshotTool.CaptureHours), so these frames sit beside
        /// psx_hour_6_night and psx_hour_5_dusk as the before and after.</summary>
        static void CaptureCircuit()
        {
            var def = TrackCatalog.At(0);
            Line(def.id + ":");
            if (!PSXScreenshotTool.Open(def, out var cam, out var player))
            {
                Skipped("the scene did not open (run the scene build)");
                return;
            }
            var sun = FindSun();
            var t = player.transform;
            string venue = def.id.ToLowerInvariant();
            Vector3 eye = t.position - t.forward * 8f + Vector3.up * 2.6f;
            var chaseRot = Quaternion.LookRotation(t.position + Vector3.up * 0.8f + t.forward * 20f - eye);
            Vector3 grid = t.TransformPoint(new Vector3(3.6f, 1.6f, 6.0f));
            var gridRot = Quaternion.LookRotation(t.position + Vector3.up * 0.6f - grid);

            foreach (var v in Plan(dusk: true, rain: true, lens: true))
            {
                BeginVariant(v, cam, sun);
                Take(cam, venue, "chase", v, eye, chaseRot);
                Take(cam, venue, "34", v, grid, gridRot);
                EndVariant();
            }
        }

        /// <summary>
        /// Charlotte at night, which no tool photographed before this pass:
        /// the city had no street lamps at all, and it streams, so a shot of
        /// the opened scene is a car in a void (tools map). Three kinds of
        /// street, because the lamps are placed per road class and each class
        /// has its own reference — the arterial is NFS's lit downtown, the
        /// motorway is the sodium freeway frame, the residential street is
        /// the dark one between lamps.
        /// </summary>
        static void CaptureCity()
        {
            var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
            Line("Charlotte:");
            if (def.id != "Charlotte") { Skipped("no Charlotte venue in the catalog"); return; }
            if (!PSXScreenshotTool.Open(def, out var cam, out var player))
            {
                Skipped("the scene did not open (run the scene build)");
                return;
            }
            var world = Object.FindAnyObjectByType<CityWorld>();
            var map = CityMap.Get();
            if (world == null || map == null)
            {
                Skipped("" + (world == null ? "the scene has no CityWorld" : "charlotte_city.bytes is missing"));
                return;
            }
            var sun = FindSun();
            var car = player.GetComponent<CarController>();

            // The arterial is found from the 277 loop's START LINE, not from
            // the map's "uptown" point: that is the stretch a race actually
            // begins on, so it is the first city street most players see.
            var loop = TrackCatalog.At(TrackCatalog.IndexOf("UptownLoop"));
            string routeId = loop.id == "UptownLoop" && !string.IsNullOrEmpty(loop.cityRoute) ? loop.cityRoute : "uptown";
            Vector2 start = RouteStart(map, routeId, map.uptown);

            var spots = new List<(string name, CityMap.Edge e, float s)>();
            AddSpot(spots, "arterial", world, map, start, 4000f,
                    e => !e.link && !e.bridge && !e.tunnel && (e.cls == 2 || e.cls == 3));
            AddSpot(spots, "motorway", world, map, map.uptown, 12000f,
                    e => !e.link && !e.bridge && !e.tunnel && e.cls >= 5);
            AddSpot(spots, "residential", world, map, map.uptown, 8000f,
                    e => !e.link && !e.bridge && !e.tunnel && e.cls == 0);

            foreach (var sp in spots)
            {
                Stand(sp.e, sp.s, out Vector3 pos, out Quaternion rot);
                // Two tiles each way, the ring the game keeps round the car:
                // the chase view looks 110 m down the road and the fog closes
                // well inside 512 m, so nothing in frame is past the edge.
                world.EnsureRing(pos, 2);
                // TeleportTo rather than the transform: the player's body is
                // interpolated and a bare transform write is the pose the
                // memory warns about being painted back over.
                if (car != null) car.TeleportTo(pos, rot);
                else player.transform.SetPositionAndRotation(pos, rot);

                var t = player.transform;
                Vector3 eye = t.position - t.forward * 8f + Vector3.up * 2.6f;
                var chaseRot = Quaternion.LookRotation(t.position + Vector3.up * 0.8f + t.forward * 20f - eye);
                Line($"  {sp.name}: '{sp.e.name}' cls {sp.e.cls}{(sp.e.oneway ? " one-way" : "")} " +
                     $"{sp.e.speedKmh} km/h, {sp.e.lanes} lanes, at ({pos.x:0}, {pos.z:0}); " +
                     $"{StreetLights.CountWithin(pos, LitProbeM, StreetLights.Kind.Street)} lamps within {LitProbeM:0} m");
                foreach (var v in Plan(dusk: true, rain: true, lens: sp.name == "arterial"))
                {
                    BeginVariant(v, cam, sun);
                    Take(cam, "charlotte", sp.name, v, eye, chaseRot);
                    EndVariant();
                }
            }
        }

        /// <summary>A mountain stage at night: no lamps, so everything in
        /// the frame is the sky, the grade and the car's own headlights — the
        /// reference's neutral darks and blue-grey sky, not the city's sodium
        /// murk. If this frame goes orange the skyglow has leaked out of
        /// town.</summary>
        static void CaptureStage()
        {
            var def = TrackCatalog.At(TrackCatalog.IndexOf("BlueRidge"));
            Line("BlueRidge:");
            if (def.id != "BlueRidge") { Skipped("no BlueRidge venue in the catalog"); return; }
            if (!PSXScreenshotTool.Open(def, out var cam, out var player))
            {
                Skipped("the scene did not open (run the scene build)");
                return;
            }
            var sun = FindSun();
            var t = player.transform;
            Vector3 eye = t.position - t.forward * 8f + Vector3.up * 2.6f;
            var chaseRot = Quaternion.LookRotation(t.position + Vector3.up * 0.8f + t.forward * 20f - eye);
            var v = new Variant(TimeOfDay.Night, false, false);
            BeginVariant(v, cam, sun);
            Take(cam, def.id.ToLowerInvariant(), "chase", v, eye, chaseRot);
            EndVariant();
        }

        // ------------------------------------------------------------------
        //  One variant: an hour, maybe rain, maybe the lens
        // ------------------------------------------------------------------

        static List<Variant> Plan(bool dusk, bool rain, bool lens)
        {
            var list = new List<Variant> { new Variant(TimeOfDay.Night, false, false) };
            if (dusk) list.Add(new Variant(TimeOfDay.Dusk, false, false));
            if (rain && rainDay > 0)
            {
                list.Add(new Variant(TimeOfDay.Night, true, false));
                if (lens) list.Add(new Variant(TimeOfDay.Night, true, true));
            }
            return list;
        }

        /// <summary>
        /// The hour sweep's recipe (PSXScreenshotTool.CaptureHours), in the
        /// same order, plus the weather and the lens: the hour through
        /// TimeOfDay (which now also writes wetness, night, grade and mood to
        /// PSXGlobals), the globals pushed by hand, the lamps converted and
        /// lit, the cars' lamps built, the cluster rebuilt in the hour's bulb.
        /// </summary>
        static void BeginVariant(Variant v, Camera cam, Light sun)
        {
            EndVariant();
            // Day 0 is always clear; a borrowed rain day is rain. Set for
            // EVERY variant, clear ones included, so a frame never inherits
            // whatever day the editor happened to be on.
            RaceHandoff.CalendarDay = v.rain ? rainDay : 0;
            TimeOfDay.Apply(v.hour, sun);
            var globals = Object.FindAnyObjectByType<PSXGlobals>();
            if (globals != null) globals.Apply();
            var hour = TimeOfDay.At(v.hour);
            NightGlow.PreviewAll(hour.lightsOn);
            bool carsLit = hour.lightsOn || Seasons.LightsOn(Seasons.CurrentWeather);
            foreach (var lights in Object.FindObjectsByType<CarLights>(FindObjectsInactive.Exclude))
                lights.PreviewBuild(carsLit);
            foreach (var c in Object.FindObjectsByType<GaugeCluster>(FindObjectsInactive.Exclude))
                c.Build();
            foreach (var hud in Object.FindObjectsByType<RaceHUD>(FindObjectsInactive.Exclude))
                HudOnTop.Apply(hud.gameObject);

            rainOn = v.rain;
            if (v.lens)
            {
                // WITHOUT THE HUD AND THE CLUSTER. In the game neither is
                // under the lens: the HUD is split onto a camera stacked AFTER
                // the lens pass (SpeedBlur.SetSplit, forced on with LENS FX)
                // and the cluster is a device-resolution overlay that never
                // enters the framebuffer at all. Edit mode has no split, and
                // PSXScreenshotTool.Open pulls the cluster onto the shot
                // camera, so here both would be refracted through the drops —
                // a lap counter bent round a raindrop, which the game never
                // draws. A lens frame is for judging the lens.
                HideCanvasesOn(cam);
                LensFx.PreviewSet(cam, LensRainAmount, LensDirtAmount, 0f, LensClock);
            }
            Canvas.ForceUpdateCanvases();

            Line($"  [{v.Suffix}] weather {Seasons.CurrentWeather}  wet {Shader.GetGlobalFloat("_PSXWetness"):0.00}  " +
                 $"night {Shader.GetGlobalFloat("_PSXNight"):0.00}  gradeNight {Shader.GetGlobalFloat("_PSXGradeNight"):0.00}  " +
                 $"mood {Shader.GetGlobalVector("_PSXMood")}  urban {TimeOfDay.UrbanGlow():0.0}" +
                 (globals != null ? $"  fog {globals.fogNear:0}-{globals.fogFar:0} m" : "") +
                 (v.rain ? "  rain slab per frame" : "") +
                 (v.lens ? "  lens on (HUD hidden)" : ""));
        }

        /// <summary>Everything a variant turned on, turned off: safe to call
        /// when nothing is on, and called on every way out.</summary>
        static void EndVariant()
        {
            LensFx.PreviewSet(null, 0f, 0f, 0f, 0f);
            foreach (var c in hiddenCanvases) if (c != null) c.enabled = true;
            hiddenCanvases.Clear();
            if (rainOn) WeatherFx.PreviewClear();
            rainOn = false;
        }

        /// <summary>
        /// One frame: the camera stood at the shot first — the headlight table
        /// sorts its cars by distance from Camera.main and the rain slab rides
        /// above it — then both lamp tables filled for this eye, then the
        /// render through PSXScreenshotTool (game line count, PSX/Blit, film
        /// grade). StreetLights also fills its table from
        /// beginCameraRendering for the camera being drawn; the explicit push
        /// is belt and braces.
        /// </summary>
        static void Take(Camera cam, string venue, string spot, Variant v, Vector3 eye, Quaternion rot)
        {
            string file = "nl_" + venue + "_" + spot + "_" + v.Suffix;
            var keepPos = cam.transform.position;
            var keepRot = cam.transform.rotation;
            cam.transform.SetPositionAndRotation(eye, rot);
            // THE RAIN, over THIS lens. WeatherFx builds its slab in Start and
            // moves it in LateUpdate, neither of which edit mode calls, so
            // Preview builds the same particle system, parks it over the shot
            // camera and runs it forward to a full column. Per frame, after
            // the camera has moved: the particles are world-space and stay
            // where they fell, so the three-quarter view would otherwise be
            // taken under the chase view's rain.
            if (rainOn) WeatherFx.Preview(Weather.Rain, cam, RainSettleSeconds);
            CarLights.PushGlobals();
            StreetLights.Push(eye, rot * Vector3.forward);
            PSXScreenshotTool.ShotAs(cam, file, eye, rot);
            cam.transform.SetPositionAndRotation(keepPos, keepRot);
            shots++;

            string msg = $"{file}.png  street lamps in the table {StreetLights.PushedCount}/{StreetLights.MaxLamps} " +
                         $"({StreetLights.CountWithin(eye, 110f, StreetLights.Kind.Street)} registered within 110 m), " +
                         $"headlights {CarLights.PushedCount}";
            Line("    " + msg);
            Debug.Log("[NightLook] " + msg);
        }

        static void HideCanvasesOn(Camera cam)
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
            {
                if (c == null || !c.enabled || !c.isRootCanvas) continue;
                if (c.renderMode != RenderMode.ScreenSpaceCamera || c.worldCamera != cam) continue;
                c.enabled = false;
                hiddenCanvases.Add(c);
            }
        }

        // ------------------------------------------------------------------
        //  Finding places in the city
        // ------------------------------------------------------------------

        /// <summary>
        /// The nearest candidate of a kind that has street lamps round it,
        /// probing up to <see cref="MaxProbes"/> of them by building the one
        /// tile under each (the lamps are placed per tile, and NightGlow.Init
        /// registers them as the tile is built). The nearest is taken anyway
        /// if none is lit, and the log says so — an unlit street is a result,
        /// not a reason to skip the frame.
        /// </summary>
        static void AddSpot(List<(string name, CityMap.Edge e, float s)> spots, string name,
                            CityWorld world, CityMap map, Vector2 near, float maxDist,
                            System.Func<CityMap.Edge, bool> want)
        {
            var cands = Candidates(map, near, want, maxDist);
            if (cands.Count == 0) { Line("  no " + name + " street within " + maxDist + " m"); return; }
            int probed = 0;
            foreach (var c in cands)
            {
                Vector2 p = c.e.PointAt(c.s);
                var at = new Vector3(p.x, c.e.YAt(c.s), p.y);
                world.EnsureRing(at, 0);
                probed++;
                if (StreetLights.CountWithin(at, LitProbeM, StreetLights.Kind.Street) > 0)
                {
                    spots.Add((name, c.e, c.s));
                    if (probed > 1) Line($"  {name}: the {probed - 1} nearer candidate(s) had no lamps within {LitProbeM:0} m");
                    return;
                }
            }
            spots.Add((name, cands[0].e, cands[0].s));
            Line($"  {name}: none of the {probed} nearest candidates has a lamp within {LitProbeM:0} m -- shot unlit");
        }

        /// <summary>Points on the wanted edges, every 20 m, off structure and
        /// clear of the ends (a junction is not a street), nearest first and
        /// at least <see cref="CandidateSpreadM"/> apart so the probes look at
        /// different places.</summary>
        internal static List<(CityMap.Edge e, float s)> Candidates(CityMap map, Vector2 near,
                                                         System.Func<CityMap.Edge, bool> want, float maxDist)
        {
            var all = new List<(float d, CityMap.Edge e, float s)>();
            foreach (var e in map.edges)
            {
                if (e == null || e.pts == null || e.pts.Length < 2 || e.length < 90f || !want(e)) continue;
                if (Vector2.Distance(e.pts[0], near) > maxDist + e.length) continue;
                for (float s = 35f; s <= e.length - 35f; s += 20f)
                {
                    if (e.ElevatedAt(s)) continue;
                    float d = Vector2.Distance(e.PointAt(s), near);
                    if (d <= maxDist) all.Add((d, e, s));
                }
            }
            all.Sort((a, b) => a.d.CompareTo(b.d));
            var picked = new List<(CityMap.Edge e, float s)>();
            foreach (var c in all)
            {
                Vector2 p = c.e.PointAt(c.s);
                bool taken = false;
                foreach (var q in picked)
                    if (Vector2.Distance(q.e.PointAt(q.s), p) < CandidateSpreadM) { taken = true; break; }
                if (taken) continue;
                picked.Add((c.e, c.s));
                if (picked.Count >= MaxProbes) break;
            }
            return picked;
        }

        /// <summary>Where a car stands on an edge: in the right-hand lane of a
        /// two-way street (this is North Carolina), in the middle of a one-way
        /// carriageway, pitched with the road, lifted like a grid slot.</summary>
        internal static void Stand(CityMap.Edge e, float s, out Vector3 pos, out Quaternion rot)
        {
            Vector2 p = e.PointAt(s), t2 = e.TangentAt(s);
            float s0 = Mathf.Max(0f, s - 3f), s1 = Mathf.Min(e.length, s + 3f);
            float rise = s1 > s0 ? (e.YAt(s1) - e.YAt(s0)) / (s1 - s0) : 0f;
            Vector3 fwd = new Vector3(t2.x, rise, t2.y).normalized;
            Vector3 right = new Vector3(t2.y, 0f, -t2.x);
            float lateral = e.oneway ? 0f : e.width * 0.25f;
            pos = new Vector3(p.x, e.YAt(s) + CarLift, p.y) + right * lateral;
            rot = Quaternion.LookRotation(fwd, Vector3.up);
        }

        /// <summary>The start line of a city route, walked along its chain of
        /// edges; <paramref name="fallback"/> if the bake has no such route.</summary>
        internal static Vector2 RouteStart(CityMap map, string id, Vector2 fallback)
        {
            var r = map.RouteById(id);
            if (r == null || r.edges == null || r.dirs == null) return fallback;
            float acc = 0f;
            for (int k = 0; k < r.edges.Length && k < r.dirs.Length; k++)
            {
                var e = map.edges[r.edges[k]];
                if (acc + e.length >= r.startM)
                {
                    float into = Mathf.Clamp(r.startM - acc, 0f, e.length);
                    return e.PointAt(r.dirs[k] > 0 ? into : e.length - into);
                }
                acc += e.length;
            }
            return fallback;
        }

        // ------------------------------------------------------------------
        //  Plumbing
        // ------------------------------------------------------------------

        static int FindRainDay()
        {
            for (int d = 1; d <= 400; d++)
                if (Seasons.WeatherFor(d) == Weather.Rain) return d;
            return -1;
        }

        internal static Light FindSun()
        {
            var go = GameObject.Find("Sun");
            var sun = go != null ? go.GetComponent<Light>() : null;
            if (sun != null) return sun;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.type == LightType.Directional) return l;
            return null;
        }

        static HashSet<string> Only()
        {
            string only = System.Environment.GetEnvironmentVariable("PSX_NIGHT_ONLY");
            if (string.IsNullOrWhiteSpace(only)) return null;
            var set = new HashSet<string>();
            foreach (var s in only.Split(',', ';', ' '))
                if (!string.IsNullOrWhiteSpace(s)) set.Add(s.Trim().ToLowerInvariant());
            return set.Count > 0 ? set : null;
        }

        /// <summary>One venue in its own net: a throw is logged by name and
        /// the others still run, and whatever the venue left switched on is
        /// switched off.</summary>
        /// <summary>A venue that could not be shot at all: logged, and counted
        /// against the capture's verdict.</summary>
        static void Skipped(string why)
        {
            failures++;
            Line("  skipped: " + why);
        }

        static void Venue(string name, System.Action body)
        {
            try { body(); }
            catch (System.Exception e)
            {
                failures++;
                Line("  ERROR " + name + " threw " + e.GetType().Name + ": " + e.Message);
                Debug.LogException(e);
            }
            finally { EndVariant(); }
        }

        static void Line(string s) => log.AppendLine(s);
    }
}
