using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE COLOUR PROTOCOL, PHOTOGRAPHED (the colour pass, 2026-09-29; see
    /// the plan's "Measurement protocol" and tools\colour\colour-shots.ps1).
    ///
    /// The owner: "the game is very unpleasant to look at ... headlights so
    /// bright at night that they completely wash out anything in front of the
    /// car ... noon (especially with snow) is blinding and washed out." Every
    /// claim about that is a number read off a frame, so this takes the SAME
    /// frames every time - the protocol spots of <see cref="ColourSpots"/>,
    /// each hour/weather/dress/lights/lens asked for, with the film grade on
    /// and (for the A/B matrix) off - and writes each with a sidecar
    /// (<see cref="ShotSidecar"/>) that says which texture import it was drawn
    /// with, where the car and eye were, and where the measuring boxes landed
    /// (projected from the world). tools\colour\colour_stats.py reads them.
    ///
    /// Sets (PSX_COLOUR_SETS, comma list; default all):
    ///   pred     CityCircuit noon from the psx_hour_* view with the grid as
    ///            baked and no shadow map: the dated pair's view, for the
    ///            Step-0 prediction (asphalt 50 decoded against 117 raw).
    ///   protocol S1 (Samuel Street deck) noon, noon snow, night with and
    ///            without headlights, night through the lens; the road under
    ///            it; S2 Blowing Rock noon snow; S3 Blue Ridge night with and
    ///            without headlights; CityCircuit noon, night (both), dusk;
    ///            the Little Switzerland tunnel at noon from outside and in.
    ///   ab       the A/B matrix's grade-OFF twins of S1 noon, S1 night lit,
    ///            CityCircuit noon, CityCircuit night, S2 snow noon.
    ///   sweep    every venue with a scene at noon and night (headlights on),
    ///            from its own grid pose: the road-colour gate's baseline.
    ///   interior the garage sweep and the pizzeria (cs_I_*): the INTERIORS
    ///            target's frames (the LifeSim textures).
    ///   explore  (not in the default) poses along the Samuel Street edge,
    ///            to find the owner's view.
    /// Every protocol frame is written twice: _world (HUD hidden, what the
    /// numbers are read on) and, for the HUD rows, _hud.
    ///
    /// Frames: Screenshots\colour_&lt;activeBuildTarget&gt;\cs_*.png (+ .json),
    /// log cs_log.txt, verdict line "COLOUR SHOTS OK".
    /// </summary>
    public static class ColourShots
    {
        static string RootDir => Directory.GetParent(Application.dataPath).FullName;
        static string OutDir;

        static StringBuilder log;
        static int shots, failures;
        static readonly List<Canvas> hidden = new List<Canvas>();

        public enum Lights { Auto, On, Off }

        public class Variant
        {
            public int hour;
            public Weather weather = Weather.Clear;
            public Season season = Season.Fall;
            public Lights lights = Lights.Auto;
            public bool lens;
            public bool brake;         // the brake lamps held on (the tail-lamp targets: dim and braking)
            public bool flat;          // no sun shadow map (the psx_hour sweep's frames had none)
            public bool keepField;     // leave the scene's other cars where they are
            public bool hud;           // also write the _hud frame
            public bool gradeOff;      // also write the grade-off twin (A/B matrix)
            public string Tag()
            {
                string s = TimeOfDay.At(hour).name.ToLowerInvariant() + "_" + weather.ToString().ToLowerInvariant() +
                           "_" + season.ToString().ToLowerInvariant();
                s += lights == Lights.On ? "_lit" : lights == Lights.Off ? "_dark" : "";
                if (lens) s += "_lens";
                if (brake) s += "_brake";
                if (flat) s += "_flat";
                if (keepField) s += "_field";
                return s;
            }
        }

        [MenuItem("PSX Racing/Colour Shots")]
        public static void Capture()
        {
            string target = EditorUserBuildSettings.activeBuildTarget.ToString();
            OutDir = System.Environment.GetEnvironmentVariable("PSX_COLOUR_OUT");
            if (string.IsNullOrWhiteSpace(OutDir)) OutDir = Path.Combine(RootDir, "Screenshots", "colour_" + target);
            Directory.CreateDirectory(OutDir);
            var sets = Sets();
            // A full run starts clean; a partial one (a set or two) overwrites
            // its own frames by name and leaves the others.
            if (sets.Contains("pred") && sets.Contains("protocol") && sets.Contains("ab") && sets.Contains("sweep"))
                foreach (var f in Directory.GetFiles(OutDir, "cs_*")) File.Delete(f);
            log = new StringBuilder();
            shots = 0; failures = 0;
            Line("COLOUR SHOTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "  target " + target +
                 "  probe " + ProbeLine() + "  sets " + string.Join(",", sets));
            int keepDay = RaceHandoff.CalendarDay, keepWeather = RaceHandoff.WeatherOverride;
            string keepGrade = System.Environment.GetEnvironmentVariable("PSX_GRADE");
            var keepView = ChaseCamera.Current;
            try
            {
                ChaseCamera.PreviewView(ChaseCamera.View.Chase);
                if (sets.Contains("pred")) Guard("pred", Prediction);
                if (sets.Contains("protocol") || sets.Contains("ab")) Guard("protocol", () => ProtocolSet(sets.Contains("protocol"), sets.Contains("ab")));
                if (sets.Contains("sweep")) Guard("sweep", Sweep);
                if (sets.Contains("explore")) Guard("explore", Explore);
                if (sets.Contains("interior")) Guard("interior", Interiors);
            }
            finally
            {
                EndFrame();
                RaceHandoff.CalendarDay = keepDay;
                RaceHandoff.WeatherOverride = keepWeather;
                System.Environment.SetEnvironmentVariable("PSX_GRADE", keepGrade);
                ChaseCamera.PreviewView(keepView);
                Line(shots + " frames written to " + OutDir);
                Line(failures == 0 && shots > 0
                    ? "COLOUR SHOTS OK"
                    : "COLOUR SHOTS FAILED (" + failures + " problem(s), " + shots + " frames)");
                File.WriteAllText(Path.Combine(OutDir, "cs_log.txt"), log.ToString());
                Debug.Log("[ColourShots] " + shots + " frames written to " + OutDir + "\n" + log);
            }
        }

        static string ProbeLine()
        {
            var p = ShotSidecar.Probe();
            p.TryGetValue("graphicsFormat", out var gf);
            p.TryGetValue("isDataSRGB", out var srgb);
            p.TryGetValue("webglOverride", out var ov);
            return $"{ShotSidecar.ProbeTexture} {gf} isDataSRGB={srgb} webglOverride={ov}";
        }

        // ------------------------------------------------------------------
        //  The sets
        // ------------------------------------------------------------------

        /// <summary>The dated pair's view (psx_hour_2_noon: CaptureHours on
        /// CityCircuit, the grid as baked, no shadow map, the baked dress)
        /// so the Step-0 prediction is read on the very pixels it was made
        /// from.</summary>
        static void Prediction()
        {
            var spot = ColourSpots.Find("CC");
            if (!OpenAt(spot, out var cam, out var player, out var pos, out var rot)) return;
            var v = new Variant { hour = TimeOfDay.Noon, flat = true, keepField = true, hud = true, gradeOff = true };
            Frame(cam, player, spot, pos, rot, v, dressAsBaked: true);
        }

        static void ProtocolSet(bool protocol, bool ab)
        {
            // S1: the owner's Samuel Street deck, in his dress (his frames'
            // grass is the WINTER grass), and the road under it.
            var s1 = ColourSpots.Find("S1");
            if (OpenAt(s1, out var cam, out var player, out var pos, out var rot))
            {
                Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter, hud = true, gradeOff = ab }, only: !protocol);
                Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, lights = Lights.On, hud = true, gradeOff = ab }, only: !protocol);
                if (protocol)
                {
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, lights = Lights.On, lens = true });
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, lights = Lights.Off });
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, lights = Lights.On, brake = true });
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter, weather = Weather.Snow });
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Dusk, season = Season.Winter });
                    var s1d = ColourSpots.Find("S1d");
                    if (ColourSpots.Pose(s1d, player.transform, out var dpos, out var drot, out var dinfo))
                    {
                        Line("  S1d: " + dinfo);
                        Frame(cam, player, s1d, dpos, drot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter });
                        Frame(cam, player, s1d, dpos, drot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, lights = Lights.On });
                    }
                    else Problem("S1d: " + dinfo);
                    var s1u = ColourSpots.Find("S1u");
                    if (ColourSpots.Pose(s1u, player.transform, out var upos, out var urot, out var uinfo))
                    {
                        Line("  S1u: " + uinfo);
                        Frame(cam, player, s1u, upos, urot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter });
                        Frame(cam, player, s1u, upos, urot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, lights = Lights.On });
                    }
                    else Problem("S1u: " + uinfo);
                }
            }

            var s2 = ColourSpots.Find("S2");
            if (OpenAt(s2, out cam, out player, out pos, out rot))
            {
                Frame(cam, player, s2, pos, rot, new Variant { hour = TimeOfDay.Noon, weather = Weather.Snow, season = Season.Winter, gradeOff = ab }, only: !protocol);
                if (protocol) Frame(cam, player, s2, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter });
            }

            if (protocol)
            {
                var s3 = ColourSpots.Find("S3");
                if (OpenAt(s3, out cam, out player, out pos, out rot))
                {
                    Frame(cam, player, s3, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On });
                    Frame(cam, player, s3, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.Off });
                }
            }

            var cc = ColourSpots.Find("CC");
            if (OpenAt(cc, out cam, out player, out pos, out rot))
            {
                Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Noon, hud = true, gradeOff = ab }, only: !protocol);
                Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On, gradeOff = ab }, only: !protocol);
                if (protocol)
                {
                    Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.Off });
                    Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On, brake = true });
                    Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Dusk });
                }
            }

            if (protocol)
            {
                foreach (var id in new[] { "T1", "T2" })
                {
                    var t = ColourSpots.Find(id);
                    if (OpenAt(t, out cam, out player, out pos, out rot))
                        Frame(cam, player, t, pos, rot, new Variant { hour = TimeOfDay.Noon });
                }
            }
        }

        /// <summary>Not a protocol set: poses along the Samuel Street deck's
        /// edge both ways (PSX_COLOUR_EXPLORE="s1,s2,..." metres, default a
        /// spread), at noon, to find the one the owner's frames were taken
        /// from.</summary>
        static void Explore()
        {
            var baseSpot = ColourSpots.Find("S1");
            string env = System.Environment.GetEnvironmentVariable("PSX_COLOUR_EXPLORE");
            var along = new List<float>();
            foreach (var p in (string.IsNullOrWhiteSpace(env) ? "40,70,100,125,145,170,200,230" : env).Split(','))
                if (float.TryParse(p, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)) along.Add(f);
            bool opened = false; Camera cam = null; GameObject player = null;
            foreach (float s in along)
                foreach (int dir in new[] { 1, -1 })
                {
                    var spot = new ColourSpots.Spot { id = $"X{s:0}{(dir > 0 ? "f" : "r")}", venue = baseSpot.venue, how = "cityDeck",
                                                      street = baseSpot.street, alongM = s, dir = dir, note = "explore" };
                    if (!opened) { if (!OpenAt(spot, out cam, out player, out _, out _)) return; opened = true; }
                    if (!ColourSpots.Pose(spot, player.transform, out var pos, out var rot, out var info)) { Problem(spot.id + ": " + info); continue; }
                    Line($"{spot.id}: {info}");
                    Frame(cam, player, spot, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter, hud = true });
                }
        }

        /// <summary>The interiors (sunModel 0: lit by their own lamps, and
        /// textured almost entirely from Art/LifeSim, 491 of the 16-bit set):
        /// the garage sweep (PSXScreenshotTool.CaptureGarageOnly) and the
        /// pizzeria (PizzeriaPreview.Run), their frames copied here as
        /// cs_I_* with their sidecars, so the INTERIORS target (WebGL =
        /// baseline +-2) has the same pair of targets to read.</summary>
        static void Interiors()
        {
            string shots = Path.Combine(RootDir, "Screenshots");
            foreach (var f in Directory.GetFiles(shots, "psx_garage_*.png")) { File.Delete(f); if (File.Exists(f + ".json")) File.Delete(f + ".json"); }
            PSXScreenshotTool.CaptureGarageOnly();
            foreach (var f in Directory.GetFiles(shots, "psx_garage_*.png")) CopyIn(f, "cs_I_" + Path.GetFileNameWithoutExtension(f).Substring(4));
            string pz = Path.Combine(shots, "Pizzeria");
            if (Directory.Exists(pz)) foreach (var f in Directory.GetFiles(pz, "*.png")) { File.Delete(f); if (File.Exists(f + ".json")) File.Delete(f + ".json"); }
            PizzeriaPreview.Run();
            if (Directory.Exists(pz)) foreach (var f in Directory.GetFiles(pz, "*.png")) CopyIn(f, "cs_I_pizzeria_" + Path.GetFileNameWithoutExtension(f));
            Line($"interiors: {Directory.GetFiles(OutDir, "cs_I_*.png").Length} frames");
        }

        static void CopyIn(string src, string stem)
        {
            string dst = Path.Combine(OutDir, stem + ".png");
            File.Copy(src, dst, true);
            if (File.Exists(src + ".json")) File.Copy(src + ".json", dst + ".json", true);
            shots++;
        }

        /// <summary>Every venue with a scene, noon and night, from its grid
        /// pose: the road-colour gate's frames.</summary>
        static void Sweep()
        {
            foreach (var def in TrackCatalog.Scened)
            {
                if (def == null) continue;
                var spot = new ColourSpots.Spot { id = "V_" + def.id, venue = def.id, how = "grid", note = "venue sweep: the grid pose" };
                try
                {
                    if (!OpenAt(spot, out var cam, out var player, out var pos, out var rot)) continue;
                    Frame(cam, player, spot, pos, rot, new Variant { hour = TimeOfDay.Noon });
                    Frame(cam, player, spot, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On });
                }
                catch (System.Exception e)
                {
                    Problem(def.id + " threw " + e.GetType().Name + ": " + e.Message);
                    Debug.LogException(e);
                }
                finally { EndFrame(); }
            }
        }

        // ------------------------------------------------------------------
        //  Opening a venue and standing the car
        // ------------------------------------------------------------------

        static bool OpenAt(ColourSpots.Spot spot, out Camera cam, out GameObject player, out Vector3 pos, out Quaternion rot)
        {
            cam = null; player = null; pos = default; rot = Quaternion.identity;
            if (spot == null) { Problem("no such spot"); return false; }
            int idx = TrackCatalog.IndexOf(spot.venue);
            var def = TrackCatalog.At(idx);
            if (def.id != spot.venue) { Problem(spot.id + ": no venue " + spot.venue); return false; }
            EndFrame();
            if (!PSXScreenshotTool.Open(def, out cam, out player)) { Problem(spot.id + ": " + spot.venue + " did not open (run the scene build)"); return false; }
            if (!ColourSpots.Pose(spot, player.transform, out pos, out rot, out string info))
            {
                Problem(spot.id + ": " + info);
                return false;
            }
            Line($"{spot.id} {spot.venue}: {info}  ({spot.note})");
            return true;
        }

        // ------------------------------------------------------------------
        //  One frame
        // ------------------------------------------------------------------

        static void Frame(Camera cam, GameObject player, ColourSpots.Spot spot, Vector3 pos, Quaternion rot, Variant v,
                          bool dressAsBaked = false, bool only = false)
        {
            // only: the A/B set alone asks for just the grade-off twin of a
            // frame the protocol set would also have written grade-on.
            try
            {
                var sun = NightLookShots.FindSun();
                var globals = Object.FindAnyObjectByType<PSXGlobals>();

                // THE DAY, THE WEATHER, THE DRESS - before the city builds
                // its tiles (they ask SeasonDress.Substitute as they go).
                RaceHandoff.CalendarDay = dressAsBaked ? 0 : ColourSpots.DayIn(v.season);
                RaceHandoff.WeatherOverride = dressAsBaked ? -1 : (int)v.weather;
                if (!dressAsBaked) Dress();

                // The field: out of the way unless asked for (the player's
                // harness has none either), then the car on its spot.
                SetField(player, v.keepField);
                var car = player.GetComponent<CarController>();
                if (car != null) car.TeleportTo(pos, rot); else player.transform.SetPositionAndRotation(pos, rot);
                var world = Object.FindAnyObjectByType<CityWorld>();
                if (world != null) { world.EnsureRing(pos, 2); if (!dressAsBaked) Dress(); }

                TimeOfDay.Apply(v.hour, sun);
                if (globals != null) globals.Apply();
                var hour = TimeOfDay.At(v.hour);
                NightGlow.PreviewAll(hour.lightsOn);
                bool lit = v.lights == Lights.On || (v.lights == Lights.Auto && (hour.lightsOn || Seasons.LightsOn(Seasons.CurrentWeather)));
                foreach (var l in Object.FindObjectsByType<CarLights>(FindObjectsInactive.Exclude)) l.PreviewBuild(lit, v.brake);
                foreach (var c in Object.FindObjectsByType<GaugeCluster>(FindObjectsInactive.Exclude)) c.Build();
                foreach (var h in Object.FindObjectsByType<RaceHUD>(FindObjectsInactive.Exclude)) HudOnTop.Apply(h.gameObject);
                Canvas.ForceUpdateCanvases();

                // THE EYE, and the lens and the falling snow over it.
                ColourSpots.Eye(pos, rot, out Vector3 eye, out Quaternion look);
                cam.transform.SetPositionAndRotation(eye, look);
                cam.ResetProjectionMatrix();
                cam.fieldOfView = ColourSpots.Fov;
                float night = Shader.GetGlobalFloat("_PSXNight");
                float dirt = v.lens ? LensFx.DirtFor(night, 0f) : 0f;
                if (v.lens) LensFx.PreviewSet(cam, 0f, dirt, 0f, 3.7f);
                if (v.weather == Weather.Snow || v.weather == Weather.Rain) WeatherFx.Preview(v.weather, cam, 2.5f);
                CarLights.PushGlobals();
                StreetLights.Push(eye, look * Vector3.forward);

                float keepShadow = globals != null ? globals.sunShadow : 0f, keepSky = globals != null ? globals.skyShade : 0f;
                if (v.flat) { if (globals != null) { globals.sunShadow = 0f; globals.skyShade = 0f; globals.Apply(); } }
                else SunShadows.RenderFor(cam);

                // THE BOXES, projected from where they are on the road.
                float aspect = 16f / 9f;
                cam.aspect = aspect;
                var regions = ColourSpots.Project(cam, pos, rot, player.transform, aspect);
                cam.ResetAspect();

                string stem = "cs_" + spot.id + "_" + v.Tag();
                var meta = new Dictionary<string, object>
                {
                    ["protocol"] = "colour-2026-09-29",
                    ["spot"] = spot.id, ["venue"] = spot.venue, ["spotNote"] = spot.note ?? "",
                    ["variant"] = new Dictionary<string, object>
                    {
                        ["hour"] = hour.name, ["weather"] = v.weather.ToString(), ["season"] = dressAsBaked ? "BAKED" : v.season.ToString(),
                        ["lights"] = v.lights.ToString(), ["lit"] = lit, ["brake"] = v.brake, ["lensDirt"] = dirt, ["shadowMap"] = !v.flat,
                        ["field"] = v.keepField,
                    },
                    ["pose"] = new Dictionary<string, object> { ["pos"] = pos, ["rot"] = new List<object> { rot.x, rot.y, rot.z, rot.w } },
                    ["eye"] = new Dictionary<string, object> { ["pos"] = eye, ["rot"] = new List<object> { look.x, look.y, look.z, look.w }, ["fov"] = ColourSpots.Fov },
                    ["regions"] = ColourSpots.ToSidecar(regions),
                };

                var grades = new List<bool>();
                if (!only) grades.Add(true);
                if (v.gradeOff) grades.Add(false);
                foreach (bool graded in grades)
                {
                    System.Environment.SetEnvironmentVariable("PSX_GRADE", graded ? "1" : "0");
                    string g = graded ? "_g1" : "_g0";
                    HideCanvasesOn(cam);
                    Shoot(cam, stem + g + "_world", eye, look, meta, "world");
                    ShowCanvases();
                    if (v.hud) Shoot(cam, stem + g + "_hud", eye, look, meta, "hud");
                }

                if (v.flat && globals != null) { globals.sunShadow = keepShadow; globals.skyShade = keepSky; globals.Apply(); }
                int vis = 0, onGround = 0;
                foreach (var r in regions) { if (r.visible && r.inFrame) vis++; if (r.onGround) onGround++; }
                Line($"  {stem}: weather {Seasons.CurrentWeather} dress {DressName()} lit {lit} lamps {StreetLights.PushedCount} " +
                     $"heads {CarLights.PushedCount} wet {Shader.GetGlobalFloat("_PSXWetness"):0.00} night {night:0.00} " +
                     $"lens {dirt:0.00}  regions visible {vis}/{regions.Count}, on ground {onGround}");
            }
            catch (System.Exception e)
            {
                Problem(spot.id + " " + v.Tag() + " threw " + e.GetType().Name + ": " + e.Message);
                Debug.LogException(e);
            }
            finally { EndFrame(); }
        }

        static void Shoot(Camera cam, string name, Vector3 eye, Quaternion look, Dictionary<string, object> meta, string layer)
        {
            foreach (var kv in meta) ShotSidecar.Pending[kv.Key] = kv.Value;
            ShotSidecar.Pending["layer"] = layer;
            ShotSidecar.Pending["frameName"] = name;
            // ShotAs writes Screenshots\<name>.png; this tool's frames go to
            // their own folder per target, so the file is moved after.
            PSXScreenshotTool.ShotAs(cam, name, eye, look);
            string src = Path.Combine(RootDir, "Screenshots", name + ".png");
            string dst = Path.Combine(OutDir, name + ".png");
            if (File.Exists(dst)) File.Delete(dst);
            if (File.Exists(dst + ".json")) File.Delete(dst + ".json");
            if (File.Exists(src)) File.Move(src, dst);
            if (File.Exists(src + ".json")) File.Move(src + ".json", dst + ".json");
            shots++;
        }

        /// <summary>Put the dress for the day and weather on the scene's
        /// renderers. SeasonDress registers its table in Awake, which edit
        /// mode never calls - the private Register is run by hand.</summary>
        static void Dress()
        {
            var sd = Object.FindAnyObjectByType<SeasonDress>();
            if (sd == null) return;
            var reg = typeof(SeasonDress).GetMethod("Register", BindingFlags.Instance | BindingFlags.NonPublic);
            reg?.Invoke(sd, null);
            sd.Apply(Seasons.CurrentDress);
        }

        static string DressName()
        {
            int d = SeasonDress.AppliedDress;
            return d >= 0 && d < Seasons.DressNames.Length ? Seasons.DressNames[d] : "BAKED";
        }

        static readonly List<GameObject> parked = new List<GameObject>();

        static void SetField(GameObject player, bool keep)
        {
            foreach (var go in parked) if (go != null) go.SetActive(true);
            parked.Clear();
            if (keep) return;
            foreach (var c in Object.FindObjectsByType<CarController>(FindObjectsInactive.Exclude))
            {
                if (c == null || c.gameObject == player) continue;
                c.gameObject.SetActive(false);
                parked.Add(c.gameObject);
            }
        }

        static void HideCanvasesOn(Camera cam)
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
            {
                if (c == null || !c.enabled || !c.isRootCanvas) continue;
                if (c.renderMode != RenderMode.ScreenSpaceCamera || c.worldCamera != cam) continue;
                c.enabled = false;
                hidden.Add(c);
            }
        }

        static void ShowCanvases()
        {
            foreach (var c in hidden) if (c != null) c.enabled = true;
            hidden.Clear();
        }

        static void EndFrame()
        {
            ShowCanvases();
            LensFx.PreviewSet(null, 0f, 0f, 0f, 0f);
            WeatherFx.PreviewClear();
        }

        // ------------------------------------------------------------------
        //  Plumbing
        // ------------------------------------------------------------------

        static HashSet<string> Sets()
        {
            string s = System.Environment.GetEnvironmentVariable("PSX_COLOUR_SETS");
            var set = new HashSet<string>();
            if (string.IsNullOrWhiteSpace(s)) s = "pred,protocol,ab,sweep,interior";
            foreach (var p in s.Split(',', ';', ' '))
                if (!string.IsNullOrWhiteSpace(p)) set.Add(p.Trim().ToLowerInvariant());
            return set;
        }

        static void Guard(string name, System.Action body)
        {
            try { body(); }
            catch (System.Exception e)
            {
                Problem(name + " threw " + e.GetType().Name + ": " + e.Message);
                Debug.LogException(e);
            }
            finally { EndFrame(); }
        }

        static void Problem(string s) { failures++; Line("  PROBLEM " + s); }
        static void Line(string s) => log.AppendLine(s);
    }
}
