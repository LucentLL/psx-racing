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
    ///   beam     (not in the default) the night spots dark and lit at every
    ///            low-beam intensity in PSX_BEAM_SWEEP (C5).
    ///   fx       (not in the default) falling snow and tyre smoke, each with
    ///            its twin without them (C7).
    ///   look     (not in the default) the owner's two open choices, G1 and
    ///            cool darks, off and on (C11/C12).
    ///   tune     (not in the default; review 2026-09-29) the harsh sun's
    ///            fill (PSX_DAYFILL_SWEEP, TimeOfDay.HarshSunFill) at S1, the
    ///            circuit and the snow spots, and the snow's brightness
    ///            (PSX_SNOWTINT_SWEEP, runtime copies of the snow grounds).
    /// The protocol set also carries (review, 2026-09-29) the PLAYER'S OWN
    /// CAMERA: S1 by noon and night and S3 by night through the game's CHASE
    /// and CLOSE rigs at rest (ChaseCamera.SteadyPose, _rigchase/_rigclose)
    /// with LENS FX as it ships (on); and downtown Charlotte (CD) at night,
    /// its window walls with and without the lens.
    /// The protocol set also carries B1, the drag strip: the low beam's flat,
    /// straight measuring road, dark / lit / braking.
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
            public bool oncoming;      // one of the field's cars 24 m ahead in the other lane, facing the eye, lamps lit
            public float beam;         // the beam set's sweep: a CarLights.BeamIntensity to push instead (0 = the shipped one)
            public bool smoke;         // the player's rear tyres smoking (a second and a half of a slide, stood still)
            public bool noFlakes;      // the weather's dress and light with no falling snow/rain drawn (the particles' twin)
            public bool sunLift;       // the owner's C11 choice ON for this frame (G1: the grade's lift fade in clear sun)
            public bool coolNight;     // the owner's C12 choice ON for this frame (cool night darks)
            public string rig = "";    // "chase" / "close": the game's chase rig at rest instead of the protocol eye
            public float dayFill;      // the tune set: a TimeOfDay.HarshSunFill for this frame (0 = the constant)
            public float snowTint;     // the tune set: the snow grounds' colour x this (0 = as dressed)
            public bool dynSky;        // the pause menu's SKY switch on DYNAMIC for this frame (the computed sky)
            public string diag = "";   // the night set's light census: "noamb", "nosun", "nofog", "nowet" (any mix) switch that light off for this frame
            public string extra = "";  // appended to the tag (the look switches' A/B twins: _cool, _sunlift ...)
            public string Tag()
            {
                string s = TimeOfDay.At(hour).name.ToLowerInvariant() + "_" + weather.ToString().ToLowerInvariant() +
                           "_" + season.ToString().ToLowerInvariant();
                s += lights == Lights.On ? "_lit" : lights == Lights.Off ? "_dark" : "";
                if (lens) s += "_lens";
                if (brake) s += "_brake";
                if (flat) s += "_flat";
                if (keepField) s += "_field";
                if (oncoming) s += "_onc";
                if (beam > 0f) s += "_bi" + Mathf.RoundToInt(beam * 100f).ToString("000");
                if (smoke) s += "_smoke";
                if (noFlakes) s += "_noflk";
                if (sunLift) s += "_sunlift";
                if (coolNight) s += "_cool";
                if (!string.IsNullOrEmpty(rig)) s += "_rig" + rig;
                if (dayFill > 0f) s += "_df" + Mathf.RoundToInt(dayFill * 100f).ToString("000");
                if (snowTint > 0f) s += "_st" + Mathf.RoundToInt(snowTint * 100f).ToString("000");
                if (!string.IsNullOrEmpty(diag)) s += "_x" + diag.Replace(",", "");
                if (dynSky) s += "_dyn";
                return s + extra;
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
            // PSX_CONE: the beam-in-the-air strength for the whole run (the
            // C5 sweep); unset = the shipped rule.
            string cone = System.Environment.GetEnvironmentVariable("PSX_CONE");
            CarLights.ConeStrengthOverride = float.TryParse(cone, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float coneV) ? coneV : -1f;
            if (CarLights.ConeStrengthOverride >= 0f) Line("  PSX_CONE=" + CarLights.ConeStrengthOverride);
            try
            {
                ChaseCamera.PreviewView(ChaseCamera.View.Chase);
                if (sets.Contains("pred")) Guard("pred", Prediction);
                if (sets.Contains("protocol") || sets.Contains("ab")) Guard("protocol", () => ProtocolSet(sets.Contains("protocol"), sets.Contains("ab")));
                if (sets.Contains("sweep")) Guard("sweep", Sweep);
                if (sets.Contains("beam")) Guard("beam", BeamSet);
                if (sets.Contains("fx")) Guard("fx", FxSet);
                if (sets.Contains("look")) Guard("look", LookSet);
                if (sets.Contains("tune")) Guard("tune", TuneSet);
                if (sets.Contains("explore")) Guard("explore", Explore);
                if (sets.Contains("interior")) Guard("interior", Interiors);
                if (sets.Contains("night")) Guard("night", NightSet);
                if (sets.Contains("census")) Guard("census", CensusSet);
            }
            finally
            {
                EndFrame();
                CarLights.ConeStrengthOverride = -1f;
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
                    // The colour pass: the hours the exposure leaves at 1 must
                    // barely move (the old light shoulder is gone at all of them).
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Afternoon, season = Season.Winter });
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, lights = Lights.On, oncoming = true, lens = true });
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
                    Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Afternoon });
                    Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Morning });
                    // Oncoming lamps through the lens: the glare keyed on sources.
                    Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On, oncoming = true, lens = true });
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
                // B1, the low beam's own road (C5): night without and with
                // the lamps (the pool against the unlit road, box by box),
                // braking (the tail lamps against the unlit road behind).
                var b1 = ColourSpots.Find("B1");
                if (OpenAt(b1, out cam, out player, out pos, out rot))
                {
                    Frame(cam, player, b1, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.Off });
                    Frame(cam, player, b1, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On });
                    Frame(cam, player, b1, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On, brake = true });
                }
                RigFrames();
                // Downtown Charlotte at night (review): the low beam on fresh
                // asphalt under window walls, and the lens over those walls.
                var cd = ColourSpots.Find("CD");
                if (OpenAt(cd, out cam, out player, out pos, out rot))
                {
                    Frame(cam, player, cd, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.Off });
                    Frame(cam, player, cd, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On });
                    Frame(cam, player, cd, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On, lens = true });
                    Frame(cam, player, cd, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On, lens = true, rig = "close", hud = true });
                }
            }
        }

        /// <summary>
        /// THE DARK NIGHT (set "night", 2026-09-29, the owner after driving
        /// the colour build: "These lights seem pretty dull, and night is
        /// hardly dark at all, even without street lights" - with real night
        /// drives and NFS Heat beside his town frame). Every night spot the
        /// retune is judged on, lamps off and on: his TOWN in snow through the
        /// HOOD cam he drove it with and the chase rig; Samuel Street; the
        /// unlit Blue Ridge stage; Sunset City GP's street lamps (and its tail
        /// lamps, braking, in the chase rig); the drag strip's flat beam road;
        /// downtown Charlotte's window walls. Read with colour_stats.py night.
        /// </summary>
        static void CensusSet()
        {
            var tw = ColourSpots.Find("TW");
            if (!OpenAt(tw, out var cam, out var player, out var pos, out var rot)) return;
            foreach (var d in new[] { "", "noamb", "noamb,nosun", "noamb,nosun,nofog", "noamb,nosun,nofog,nowet" })
                Frame(cam, player, tw, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, weather = Weather.Snow, lights = Lights.Off, rig = "hood", noFlakes = true, diag = d, gradeOff = d == "" });
        }

        static void NightSet()
        {
            var tw = ColourSpots.Find("TW");
            if (OpenAt(tw, out var cam, out var player, out var pos, out var rot))
            {
                Frame(cam, player, tw, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, weather = Weather.Snow, lights = Lights.On, rig = "hood", hud = true });
                Frame(cam, player, tw, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, weather = Weather.Snow, lights = Lights.Off, rig = "hood" });
                Frame(cam, player, tw, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, weather = Weather.Snow, lights = Lights.On, rig = "chase", hud = true });
                Frame(cam, player, tw, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, weather = Weather.Snow, lights = Lights.On, noFlakes = true });
                Frame(cam, player, tw, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Fall, lights = Lights.On, rig = "hood" });
                // The same night under the computed sky (the pause menu's SKY
                // switch; a preference, so a player who once chose DYNAMIC
                // drives every night under it).
                Frame(cam, player, tw, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, weather = Weather.Snow, lights = Lights.On, rig = "hood", dynSky = true });
            }
            var s1 = ColourSpots.Find("S1");
            if (OpenAt(s1, out cam, out player, out pos, out rot))
            {
                Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, lights = Lights.On });
                Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, lights = Lights.Off });
                Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, lights = Lights.On, rig = "close", lens = true, hud = true });
            }
            var s3 = ColourSpots.Find("S3");
            if (OpenAt(s3, out cam, out player, out pos, out rot))
            {
                Frame(cam, player, s3, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On });
                Frame(cam, player, s3, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.Off });
                Frame(cam, player, s3, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On, rig = "hood" });
                Frame(cam, player, s3, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On, rig = "chase", lens = true });
            }
            var cc = ColourSpots.Find("CC");
            if (OpenAt(cc, out cam, out player, out pos, out rot))
            {
                Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On });
                Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.Off });
                Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On, brake = true, rig = "chase", lens = true, hud = true });
            }
            var b1 = ColourSpots.Find("B1");
            if (OpenAt(b1, out cam, out player, out pos, out rot))
            {
                Frame(cam, player, b1, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.Off });
                Frame(cam, player, b1, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On });
            }
            var cd = ColourSpots.Find("CD");
            if (OpenAt(cd, out cam, out player, out pos, out rot))
            {
                Frame(cam, player, cd, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On });
                Frame(cam, player, cd, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On, lens = true, rig = "close", hud = true });
            }
        }

        /// <summary>
        /// THE PLAYER'S OWN CAMERA (review, 2026-09-29). Every other protocol
        /// frame looks from the look tools' fixed eye (8 m back, the car about
        /// 12% of the frame's width); the game's CHASE rig holds the car at
        /// about 25% and CLOSE - the view in the owner's frames 4 and 5 and
        /// both NFS references - at about 34%, where the near field of the low
        /// beam, the tail lamps' glow and the dirt round our own lamps fill
        /// the lower half of the screen. S1 by noon and by night, S3 by night,
        /// both rigs at rest, LENS FX on (as it ships), with the HUD; and the
        /// night twins without the lens and without the lamps, so the beam and
        /// the dirt can be read apart.
        /// </summary>
        static void RigFrames()
        {
            var s1 = ColourSpots.Find("S1");
            if (OpenAt(s1, out var cam, out var player, out var pos, out var rot))
            {
                // THE HARSH SUN'S OWN NUMBERS (review): the protocol view at noon
                // and afternoon again with NO shadow maps (_flat) - the car's
                // shadow is every road pixel near the car that the twin shows
                // lit (colour_stats.py shade).
                Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter, flat = true });
                Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Afternoon, season = Season.Winter, flat = true });
                foreach (var rig in new[] { "chase", "close" })
                {
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter, rig = rig, hud = true });
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, lights = Lights.On, lens = true, rig = rig, hud = true });
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, lights = Lights.On, rig = rig });
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, lights = Lights.Off, rig = rig });
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter, weather = Weather.Snow, rig = rig });
                }
            }
            var cc = ColourSpots.Find("CC");
            if (OpenAt(cc, out cam, out player, out pos, out rot))
            {
                Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Noon, flat = true });
                Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Afternoon, flat = true });
            }
            var s3 = ColourSpots.Find("S3");
            if (OpenAt(s3, out cam, out player, out pos, out rot))
                foreach (var rig in new[] { "chase", "close" })
                {
                    Frame(cam, player, s3, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On, lens = true, rig = rig, hud = true });
                    Frame(cam, player, s3, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On, rig = rig });
                    Frame(cam, player, s3, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.Off, rig = rig });
                }
        }

        /// <summary>
        /// THE REVIEW'S TUNING SWEEPS (set "tune", 2026-09-29): the harsh sun's
        /// fill (PSX_DAYFILL_SWEEP, comma list of TimeOfDay.HarshSunFill
        /// values; default 1,0.6,0.5,0.4,0.3) at S1 noon clear and snowy, S1
        /// afternoon, the circuit at noon and morning, and Blowing Rock's snowy
        /// noon; then the snow's own brightness (PSX_SNOWTINT_SWEEP, default
        /// 1,0.9,0.8: the snow grounds' colour times that, on runtime copies)
        /// at both snowy noons. Everything else as it ships.
        /// </summary>
        static void TuneSet()
        {
            var fills = Floats("PSX_DAYFILL_SWEEP", "1,0.6,0.5,0.4,0.3");
            var tints = Floats("PSX_SNOWTINT_SWEEP", "1,0.9,0.8");
            var s1 = ColourSpots.Find("S1");
            if (OpenAt(s1, out var cam, out var player, out var pos, out var rot))
            {
                foreach (float f in fills)
                {
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter, dayFill = f });
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter, dayFill = f, flat = true });
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter, weather = Weather.Snow, dayFill = f });
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Afternoon, season = Season.Winter, dayFill = f });
                }
                foreach (float t in tints)
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter, weather = Weather.Snow, snowTint = t });
            }
            var cc = ColourSpots.Find("CC");
            if (OpenAt(cc, out cam, out player, out pos, out rot))
                foreach (float f in fills)
                {
                    Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Noon, dayFill = f });
                    Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Noon, dayFill = f, flat = true });
                    Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Morning, dayFill = f });
                }
            var s2 = ColourSpots.Find("S2");
            if (OpenAt(s2, out cam, out player, out pos, out rot))
            {
                foreach (float f in fills)
                    Frame(cam, player, s2, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter, weather = Weather.Snow, dayFill = f });
                foreach (float t in tints)
                    Frame(cam, player, s2, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter, weather = Weather.Snow, snowTint = t });
            }
        }

        static List<float> Floats(string env, string dflt)
        {
            var list = new List<float>();
            string v = System.Environment.GetEnvironmentVariable(env);
            foreach (var p in (string.IsNullOrWhiteSpace(v) ? dflt : v).Split(','))
                if (float.TryParse(p, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) && f > 0f)
                    list.Add(f);
            return list;
        }

        /// <summary>The tune set's snow brightness: every renderer wearing a
        /// SNOW ground (Turf_Snow) gets a runtime copy of its material with
        /// the colour times <paramref name="k"/> - never the asset - put back
        /// by <see cref="UntintSnow"/> when the frame ends.</summary>
        static void TintSnow(float k)
        {
            foreach (var r in SceneRenderers())
            {
                var mats = r.sharedMaterials;
                bool any = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.mainTexture == null || m.mainTexture.name != "Turf_Snow" || !m.HasProperty("_Color")) continue;
                    var copy = new Material(m) { name = m.name + " (snow x" + k + ")", hideFlags = HideFlags.DontSave };
                    var c = m.color;
                    copy.color = new Color(c.r * k, c.g * k, c.b * k, c.a);
                    mats[i] = copy;
                    any = true;
                }
                if (!any) continue;
                tinted.Add(new KeyValuePair<Renderer, Material[]>(r, r.sharedMaterials));
                r.sharedMaterials = mats;
            }
        }

        static readonly List<KeyValuePair<Renderer, Material[]>> tinted = new List<KeyValuePair<Renderer, Material[]>>();

        static void UntintSnow()
        {
            foreach (var kv in tinted) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
            tinted.Clear();
        }

        /// <summary>
        /// THE OWNER'S TWO OPEN CHOICES, A/B (the colour pass; set "look"):
        /// the same frames with each choice off (what ships) and on -
        /// C11 "G1", the grade's lift fading under a clear sun (_sunlift), at
        /// S1 noon, CityCircuit noon and dusk (where it must change nothing)
        /// and Blowing Rock's noon snow; C12, cool night darks (_cool), at S1
        /// and CityCircuit by night. With the HUD, so he sees the game.
        /// </summary>
        static void LookSet()
        {
            var s1 = ColourSpots.Find("S1");
            if (OpenAt(s1, out var cam, out var player, out var pos, out var rot))
            {
                foreach (bool on in new[] { false, true })
                {
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter, hud = true, sunLift = on });
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, lights = Lights.On, hud = true, coolNight = on });
                    // "Especially with snow" (review): G1 at the owner's own
                    // spot on a snowy noon, in his CLOSE view.
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter, weather = Weather.Snow, hud = true, sunLift = on });
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter, weather = Weather.Snow, hud = true, sunLift = on, rig = "close" });
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter, hud = true, sunLift = on, rig = "close" });
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, lights = Lights.On, lens = true, hud = true, coolNight = on, rig = "close" });
                }
            }
            var cc = ColourSpots.Find("CC");
            if (OpenAt(cc, out cam, out player, out pos, out rot))
            {
                foreach (bool on in new[] { false, true })
                {
                    Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Noon, hud = true, sunLift = on });
                    Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Dusk, hud = true, sunLift = on });
                    Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On, hud = true, coolNight = on });
                }
            }
            var s2 = ColourSpots.Find("S2");
            if (OpenAt(s2, out cam, out player, out pos, out rot))
                foreach (bool on in new[] { false, true })
                    Frame(cam, player, s2, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter, weather = Weather.Snow, hud = true, sunLift = on });
        }

        /// <summary>
        /// LIT PARTICLES (the colour pass, C7; set "fx"): each frame with its
        /// twin without the particles - the falling snow (_noflk twin) and the
        /// player's tyre smoke (_smoke frame against the plain one) - at night
        /// in the dark, in the beams and in the tail lamps, and by day. The
        /// pixels the particles cover are read against the same pixels
        /// without them (colour_stats.py fx): at night outside every light a
        /// flake or a puff may be at most 1.5x the ground behind it.
        /// </summary>
        static void FxSet()
        {
            var s1 = ColourSpots.Find("S1");
            if (OpenAt(s1, out var cam, out var player, out var pos, out var rot))
            {
                foreach (bool lit in new[] { true, false })
                {
                    var l = lit ? Lights.On : Lights.Off;
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, weather = Weather.Snow, lights = l });
                    Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, weather = Weather.Snow, lights = l, noFlakes = true });
                }
                Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, lights = Lights.On });
                Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, lights = Lights.On, smoke = true });
                Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, lights = Lights.On, brake = true });
                Frame(cam, player, s1, pos, rot, new Variant { hour = TimeOfDay.Night, season = Season.Winter, lights = Lights.On, brake = true, smoke = true });
            }
            var s3 = ColourSpots.Find("S3");
            if (OpenAt(s3, out cam, out player, out pos, out rot))
            {
                Frame(cam, player, s3, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On });
                Frame(cam, player, s3, pos, rot, new Variant { hour = TimeOfDay.Night, lights = Lights.On, smoke = true });
                // Snow where no street lamp is: the flakes against the dark
                // with nothing lighting them, and in the beams.
                foreach (bool lit in new[] { false, true })
                {
                    var l = lit ? Lights.On : Lights.Off;
                    Frame(cam, player, s3, pos, rot, new Variant { hour = TimeOfDay.Night, weather = Weather.Snow, lights = l });
                    Frame(cam, player, s3, pos, rot, new Variant { hour = TimeOfDay.Night, weather = Weather.Snow, lights = l, noFlakes = true });
                }
            }
            var s2 = ColourSpots.Find("S2");
            if (OpenAt(s2, out cam, out player, out pos, out rot))
            {
                Frame(cam, player, s2, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter, weather = Weather.Snow });
                Frame(cam, player, s2, pos, rot, new Variant { hour = TimeOfDay.Noon, season = Season.Winter, weather = Weather.Snow, noFlakes = true });
            }
            var cc = ColourSpots.Find("CC");
            if (OpenAt(cc, out cam, out player, out pos, out rot))
            {
                Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Noon });
                Frame(cam, player, cc, pos, rot, new Variant { hour = TimeOfDay.Noon, smoke = true });
            }
        }

        /// <summary>
        /// THE LOW BEAM'S SWEEP (the colour pass, C5; set "beam"): the night
        /// spots without their lamps and with them at every intensity in
        /// PSX_BEAM_SWEEP (comma list of CarLights.BeamIntensity values;
        /// empty = the shipped one), so the intensity is chosen off frames -
        /// the pool against the unlit road, box by box - in one editor session.
        /// </summary>
        static void BeamSet()
        {
            var sweep = new List<float>();
            string env = System.Environment.GetEnvironmentVariable("PSX_BEAM_SWEEP");
            if (!string.IsNullOrWhiteSpace(env))
                foreach (var p in env.Split(','))
                    if (float.TryParse(p, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) && f > 0f)
                        sweep.Add(f);
            if (sweep.Count == 0) sweep.Add(0f);
            foreach (var id in new[] { "B1", "S3", "CC", "S1", "CD" })
            {
                var spot = ColourSpots.Find(id);
                if (!OpenAt(spot, out var cam, out var player, out var pos, out var rot)) continue;
                var season = id == "S1" ? Season.Winter : Season.Fall;
                Frame(cam, player, spot, pos, rot, new Variant { hour = TimeOfDay.Night, season = season, lights = Lights.Off });
                foreach (float b in sweep)
                    Frame(cam, player, spot, pos, rot, new Variant { hour = TimeOfDay.Night, season = season, lights = Lights.On, beam = b });
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
            TrackCatalog.TrackDef def;
            if (ColourSpots.IsSceneOnly(spot)) def = new TrackCatalog.TrackDef { id = spot.venue, name = spot.venue };
            else
            {
                int idx = TrackCatalog.IndexOf(spot.venue);
                def = TrackCatalog.At(idx);
                if (def.id != spot.venue) { Problem(spot.id + ": no venue " + spot.venue); return false; }
            }
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

        static float keepNear;

        static void Frame(Camera cam, GameObject player, ColourSpots.Spot spot, Vector3 pos, Quaternion rot, Variant v,
                          bool dressAsBaked = false, bool only = false)
        {
            // only: the A/B set alone asks for just the grade-off twin of a
            // frame the protocol set would also have written grade-on.
            // The owner's two open choices (LookChoices) for this frame only.
            string keepG1 = System.Environment.GetEnvironmentVariable("PSX_G1");
            string keepCool = System.Environment.GetEnvironmentVariable("PSX_COOLDARKS");
            string keepFill = System.Environment.GetEnvironmentVariable("PSX_DAYFILL");
            if (v.sunLift) System.Environment.SetEnvironmentVariable("PSX_G1", "1");
            if (v.coolNight) System.Environment.SetEnvironmentVariable("PSX_COOLDARKS", "1");
            if (v.dayFill > 0f) System.Environment.SetEnvironmentVariable("PSX_DAYFILL", v.dayFill.ToString(System.Globalization.CultureInfo.InvariantCulture));
            bool keepDyn = SkyModePrefs.Dynamic;
            if (v.dynSky != keepDyn) SkyModePrefs.Dynamic = v.dynSky;
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
                if (v.oncoming) PlaceOncoming(pos, rot);
                var world = Object.FindAnyObjectByType<CityWorld>();
                if (world != null) { world.EnsureRing(pos, 2); if (!dressAsBaked) Dress(); }
                if (v.snowTint > 0f) TintSnow(v.snowTint);

                TimeOfDay.Apply(v.hour, sun);
                if (globals != null) globals.Apply();
                // THE LIGHT CENSUS (the night set): one light at a time taken
                // out of the frame, to find what lights an unlit night.
                if (!string.IsNullOrEmpty(v.diag) && globals != null)
                {
                    if (v.diag.Contains("noamb")) { globals.ambient = new Color(0f, 0f, 0f, 1f); globals.skyAmbient = new Color(0f, 0f, 0f, 1f); }
                    if (v.diag.Contains("nosun") && sun != null) sun.intensity = 0f;
                    if (v.diag.Contains("nofog")) globals.fogColor = new Color(0f, 0f, 0f, 1f);
                    if (v.diag.Contains("nowet")) globals.wetness = 0f;
                    globals.Apply();
                }
                var hour = TimeOfDay.At(v.hour);
                NightGlow.PreviewAll(hour.lightsOn);
                bool lit = v.lights == Lights.On || (v.lights == Lights.Auto && (hour.lightsOn || Seasons.LightsOn(Seasons.CurrentWeather)));
                CarLights.BeamIntensityOverride = v.beam;
                foreach (var l in Object.FindObjectsByType<CarLights>(FindObjectsInactive.Exclude)) l.PreviewBuild(lit, v.brake);
                foreach (var c in Object.FindObjectsByType<GaugeCluster>(FindObjectsInactive.Exclude)) c.Build();
                foreach (var h in Object.FindObjectsByType<RaceHUD>(FindObjectsInactive.Exclude))
                {
                    HudOnTop.Apply(h.gameObject);
                    PreviewHudText(h, spot);
                }
                Canvas.ForceUpdateCanvases();

                // THE EYE, and the lens and the falling snow over it.
                Vector3 eye; Quaternion look;
                float rigShift = 0f;
                cam.ResetProjectionMatrix();
                if (string.IsNullOrEmpty(v.rig))
                {
                    ColourSpots.Eye(pos, rot, out eye, out look);
                    cam.fieldOfView = ColourSpots.Fov;
                }
                else if (v.rig == "hood")
                {
                    // The game's HOOD CAM (the owner's frame 11): the mount
                    // ChaseCamera measures off this car's shell, its pitch and
                    // its lens at rest.
                    var box = player.GetComponent<BoxCollider>();
                    var body = player.GetComponent<CarBody>();
                    Vector3 c = box != null ? box.center : new Vector3(0f, 0.72f, 0.05f);
                    Vector3 s = box != null ? box.size : new Vector3(1.72f, 1.0f, 4.1f);
                    Vector3 off = ChaseCamera.MountOffset(ChaseCamera.View.Hood, c, s, body != null ? body.Def : null);
                    eye = player.transform.TransformPoint(off);
                    look = player.transform.rotation * Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Hood), 0f, 0f);
                    cam.fieldOfView = ChaseCamera.FOVFor(ChaseCamera.View.Hood, ColourSpots.Fov, 0f, ChaseCamera.DefaultSpeedFullMps);
                    keepNear = cam.nearClipPlane;
                    cam.nearClipPlane = ChaseCamera.ViewNearClip(ChaseCamera.View.Hood, keepNear);
                }
                else
                {
                    // The game's own rig at rest for this car (ChaseCamera.Fit),
                    // at the protocol's 16:9: what the player sees standing here.
                    var view = v.rig == "close" ? ChaseCamera.View.Close : ChaseCamera.View.Chase;
                    ChaseCamera.SteadyPose(view, 16f / 9f, 0f, ChaseCamera.DefaultSpeedFullMps, player.transform,
                                           ChaseCamera.FrameOf(player), default, out eye, out look, out float rigFov, out rigShift);
                    cam.fieldOfView = rigFov;
                }
                cam.transform.SetPositionAndRotation(eye, look);
                float night = Shader.GetGlobalFloat("_PSXNight");
                // THE EYE'S ADAPTATION (C10), settled for this pose: a frame
                // is a moment the player has been in a while (a tunnel two
                // seconds in, not its first frame). PSX_ADAPT=0 holds it at 1.
                if (globals != null)
                {
                    int vi = TrackCatalog.IndexOf(spot.venue);
                    var vdef = vi >= 0 ? TrackCatalog.At(vi) : null;
                    globals.adapt = ExposureAdapt.SteadyFor(eye, player.transform, sun != null ? -sun.transform.forward : Vector3.up,
                                                            globals.night, Object.FindAnyObjectByType<TrackPath>(), vdef, globals.dayFill);
                    globals.Apply();
                }
                float dirt = v.lens ? LensFx.DirtFor(night, 0f) : 0f;
                if (v.lens) LensFx.PreviewSet(cam, 0f, dirt, 0f, 3.7f);
                if ((v.weather == Weather.Snow || v.weather == Weather.Rain) && !v.noFlakes) WeatherFx.Preview(v.weather, cam, 2.5f);
                if (v.smoke) SimulateSmoke(player);
                // PSX_LITFX=0: the particles as they were before C7 (unlit) -
                // the A/B's before-picture, on runtime copies, never the asset.
                if (System.Environment.GetEnvironmentVariable("PSX_LITFX") == "0") UnlightParticles();
                if (v.weather == Weather.Snow || v.smoke) Line("    particles: " + ParticleLitState());
                CarLights.PushGlobals();
                StreetLights.Push(eye, look * Vector3.forward);

                float keepShadow = globals != null ? globals.sunShadow : 0f, keepSky = globals != null ? globals.skyShade : 0f;
                if (v.flat) { if (globals != null) { globals.sunShadow = 0f; globals.skyShade = 0f; globals.Apply(); } }
                else SunShadows.RenderFor(cam);

                // THE BOXES, projected from where they are on the road.
                float aspect = 16f / 9f;
                cam.aspect = aspect;
                if (Mathf.Abs(rigShift) > 1e-5f)
                    cam.projectionMatrix = ChaseCamera.ShiftedProjection(cam.fieldOfView, aspect, cam.nearClipPlane, cam.farClipPlane, rigShift);
                // The sun's direction, by day: the boxes then say which are in
                // the sun, and the car's shadow gets its own pair of boxes.
                Vector3 toSun = sun != null && Shader.GetGlobalFloat("_PSXNight") < 0.5f && sun.transform.forward.y < -0.1f
                    ? -sun.transform.forward : Vector3.zero;
                var regions = ColourSpots.Project(cam, pos, rot, player.transform, aspect, toSun);
                // And the LAMP HEADS in view (the colour pass: the owner's
                // yellow bulbs must stay yellow through the tone curve and
                // the emitter-keyed halation) - the street lamps the table
                // was just filled with for this eye, each a small box round
                // its projected head.
                AddLampHeads(cam, regions);
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
                        ["beamIntensity"] = CarLights.BeamIntensityNow,
                        ["coneStrength"] = CarLights.ConeStrengthOverride >= 0f ? CarLights.ConeStrengthOverride
                            : CarLights.BeamConeStrength * CarLights.BeamIntensityNow / CarLights.BeamIntensity,
                        ["tailLamp"] = v.brake ? CarLights.TailLampBrake : CarLights.TailLampDim,
                        ["extra"] = v.extra,
                        ["sunLift"] = LookChoices.SunLift, ["gradeSun"] = globals != null ? globals.gradeSun : 0f,
                        ["coolNight"] = LookChoices.CoolNight,
                        ["adapt"] = globals != null ? globals.adapt : 1f, ["openness"] = ExposureAdapt.Openness,
                        ["tunnel"] = ExposureAdapt.Tunnel,
                        ["rig"] = v.rig ?? "",
                        ["dayFill"] = TimeOfDay.HarshSunFillNow,
                        ["snowTint"] = v.snowTint,
                        ["exposure"] = globals != null ? globals.exposure : 1f,
                    },
                    ["pose"] = new Dictionary<string, object> { ["pos"] = pos, ["rot"] = new List<object> { rot.x, rot.y, rot.z, rot.w } },
                    ["eye"] = new Dictionary<string, object> { ["pos"] = eye, ["rot"] = new List<object> { look.x, look.y, look.z, look.w }, ["fov"] = cam.fieldOfView, ["shift"] = rigShift },
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
                     $"lens {dirt:0.00} adapt {(globals != null ? globals.adapt : 1f):0.00} (open {ExposureAdapt.Openness:0.00}{(ExposureAdapt.Tunnel ? ", tunnel" : "")})" +
                     $"  regions visible {vis}/{regions.Count}, on ground {onGround}");
            }
            catch (System.Exception e)
            {
                Problem(spot.id + " " + v.Tag() + " threw " + e.GetType().Name + ": " + e.Message);
                Debug.LogException(e);
            }
            finally
            {
                EndFrame();
                System.Environment.SetEnvironmentVariable("PSX_G1", keepG1);
                System.Environment.SetEnvironmentVariable("PSX_COOLDARKS", keepCool);
                System.Environment.SetEnvironmentVariable("PSX_DAYFILL", keepFill);
                if (keepNear > 0f) { cam.nearClipPlane = keepNear; keepNear = 0f; }
                if (SkyModePrefs.Dynamic != keepDyn) SkyModePrefs.Dynamic = keepDyn;
                cam.ResetProjectionMatrix();
            }
        }

        /// <summary>The HUD's labels as a drive shows them (C9: the text's
        /// legibility is read on the _hud frames, and in edit mode nothing
        /// ever writes them): in Charlotte the street name in the lap slot -
        /// the owner's own frames had SAMUEL STREET there - elsewhere a lap
        /// count, the clock, the position and the best lap.</summary>
        static void PreviewHudText(RaceHUD h, ColourSpots.Spot spot)
        {
            bool city = spot != null && spot.venue == "Charlotte";
            if (h.lapText != null) h.lapText.text = city ? "SAMUEL STREET" : "LAP 1/3";
            if (h.timeText != null) h.timeText.text = "1'23\"456";
            if (h.posText != null) h.posText.text = city ? "FREE ROAM" : "POS 1/4";
            if (h.lastLapText != null) h.lastLapText.text = city ? "MAP DATA (C) OPENSTREETMAP CONTRIBUTORS" : "BEST 1'22\"901";
        }

        /// <summary>A region per street lamp in the pushed table
        /// (_PSXLampPos / _PSXLampColor, kind 0 = street) whose head is in
        /// front of the eye and in frame: "lamp_&lt;slot&gt;", kind "lamp",
        /// a box about ten framebuffer pixels across round the head, seen
        /// or not by a ray from the eye (a lamp post's own head box does not
        /// count as in the way: the ray stops half a metre short).</summary>
        static void AddLampHeads(Camera cam, List<ColourSpots.RegionHit> regions)
        {
            int n = Mathf.RoundToInt(Shader.GetGlobalFloat("_PSXLampCount"));
            if (n <= 0) return;
            var pos = Shader.GetGlobalVectorArray("_PSXLampPos");
            var col = Shader.GetGlobalVectorArray("_PSXLampColor");
            if (pos == null || col == null) return;
            Vector3 eye = cam.transform.position;
            for (int i = 0; i < n && i < pos.Length && i < col.Length; i++)
            {
                if (col[i].w > 0.5f) continue;                       // a point lamp (a tail lamp), not a head
                if (col[i].x + col[i].y + col[i].z < 1e-4f) continue; // off
                Vector3 w = pos[i];
                Vector3 v = cam.WorldToViewportPoint(w);
                if (v.z <= 1f || v.x < 0.02f || v.x > 0.98f || v.y < 0.02f || v.y > 0.98f) continue;
                const float hw = 0.006f, hh = 0.011f;
                var h = new ColourSpots.RegionHit
                {
                    name = "lamp_" + i, kind = "lamp", world = w, hit = "projected",
                    x0 = Mathf.Clamp01(v.x - hw), x1 = Mathf.Clamp01(v.x + hw),
                    y0 = Mathf.Clamp01(1f - v.y - hh), y1 = Mathf.Clamp01(1f - v.y + hh),
                    inFrame = true, onGround = false, clean = true, visible = true, occluder = "",
                };
                float d = Vector3.Distance(eye, w);
                foreach (var hh2 in Physics.RaycastAll(eye, (w - eye) / Mathf.Max(d, 1e-3f), Mathf.Max(0f, d - 0.5f)))
                {
                    if (hh2.collider == null || hh2.collider.isTrigger) continue;
                    h.visible = false; h.occluder = hh2.collider.name; break;
                }
                regions.Add(h);
            }
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
            // The emitter mask's picture, when PSX_SHOT_ALPHA=1 asked for it.
            string asrc = Path.Combine(RootDir, "Screenshots", name + "_alpha.png"), adst = Path.Combine(OutDir, name + "_alpha.png");
            if (File.Exists(asrc)) { if (File.Exists(adst)) File.Delete(adst); File.Move(asrc, adst); }
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

        /// <summary>THE ONCOMING CAR (the colour pass: glare keyed on light
        /// sources). The first parked field car, back on the road 24 m ahead
        /// of the player and 3.4 m to the left, turned to face the eye, so
        /// its lenses are in view and its beams light the road toward us: the
        /// halation and the lens dirt must glow round the LENSES and never
        /// round the pool its beams throw.</summary>
        static void PlaceOncoming(Vector3 pos, Quaternion rot)
        {
            // The same car every run (the find order is not): the first by
            // name, so an A/B pair of runs lights the same road.
            GameObject other = null;
            foreach (var go in parked)
            {
                if (go == null) continue;
                if (other == null) { other = go; continue; }
                int c = string.CompareOrdinal(go.name, other.name);
                Vector3 a = go.transform.position, b = other.transform.position;
                if (c < 0 || (c == 0 && (a.x < b.x || (a.x == b.x && a.z < b.z)))) other = go;
            }
            if (other == null) { Line("  (no field car to bring back as the oncoming car)"); return; }
            other.SetActive(true);
            parked.Remove(other);
            Vector3 fwd = rot * Vector3.forward;
            Vector3 flat = new Vector3(fwd.x, 0f, fwd.z).normalized;
            Vector3 left = -Vector3.Cross(Vector3.up, flat).normalized;
            Vector3 at = pos + flat * 24f + left * 3.4f + Vector3.up * (fwd.y * 24f);
            // Onto the road under it, as the player stands on his.
            if (Physics.Raycast(at + Vector3.up * 6f, Vector3.down, out var hit, 14f)) at.y = hit.point.y + ColourSpots.CarLift;
            var face = Quaternion.LookRotation(-flat, Vector3.up);
            var cc = other.GetComponent<CarController>();
            if (cc != null) cc.TeleportTo(at, face); else other.transform.SetPositionAndRotation(at, face);
            Line($"  oncoming: {other.name} at {at} facing the eye");
            // Parked again when the frame is done (SetField puts it back).
            parked.Add(other);
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

        // ------------------------------------------------------------------
        //  Lit particles (the colour pass, C7): the tyre smoke, stood still
        // ------------------------------------------------------------------

        static readonly List<TireSmoke> smoked = new List<TireSmoke>();

        /// <summary>A second and a half of the player's rear tyres sliding
        /// hard with the car stood on its spot: TireSmoke's own Emit /
        /// Integrate / BuildMesh on a clock of this tool's (TireFxPreview's
        /// trick - the component reads CarController.wheelContacts, four plain
        /// structs), so the smoke is the game's smoke in the game's material,
        /// round the car the frame is taken of.</summary>
        static void SimulateSmoke(GameObject player)
        {
            var car = player.GetComponent<CarController>();
            var smoke = player.GetComponentInChildren<TireSmoke>(true);
            if (car == null || smoke == null || smoke.material == null) { Line("  (no TireSmoke on the player's car: no smoke)"); return; }
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var holder = typeof(TireSmoke).GetField("holder", flags);
            if (holder == null || holder.GetValue(smoke) == null) typeof(TireSmoke).GetMethod("Awake", flags)?.Invoke(smoke, null);
            smoked.Add(smoke);
            Random.InitState(4711);
            var t = car.transform;
            for (int i = 0; i < 90; i++)
            {
                for (int w = 0; w < 4; w++)
                {
                    bool front = w < 2;
                    Vector3 local = new Vector3(w % 2 == 0 ? -0.73f : 0.73f, 0f, front ? 1.21f : -1.21f);
                    Vector3 top = t.TransformPoint(local) + Vector3.up * 1.5f;
                    Vector3 at = top - Vector3.up * (1.5f + ColourSpots.CarLift);
                    foreach (var h in Physics.RaycastAll(top, Vector3.down, 4f))
                        if (h.collider != null && !h.collider.isTrigger && !h.collider.transform.IsChildOf(t)) { at = h.point; break; }
                    car.wheelContacts[w].grounded = true;
                    car.wheelContacts[w].point = at;
                    car.wheelContacts[w].normal = Vector3.up;
                    car.wheelContacts[w].forward = t.forward;
                    car.wheelContacts[w].slide = front ? 0f : 9f;
                    car.wheelContacts[w].load = 3100f;
                    car.wheelContacts[w].onRoad = true;
                }
                smoke.Tick(1f / 60f);
            }
        }

        /// <summary>PSX_LITFX=0: every smoke puff and falling flake drawn with
        /// _Lit 0 on a runtime copy of its material (the smoke's is an asset,
        /// and an editor tool must never write one).</summary>
        static void UnlightParticles()
        {
            foreach (var r in SceneRenderers())
            {
                var m = r != null ? r.sharedMaterial : null;
                if (m == null || m.shader == null || m.shader.name != "PSX/Decal" || !m.HasProperty("_Lit") || m.GetFloat("_Lit") < 0.5f) continue;
                var copy = new Material(m) { name = m.name + " (unlit A/B)", hideFlags = HideFlags.DontSave };
                copy.SetFloat("_Lit", 0f);
                r.sharedMaterial = copy;
            }
        }

        /// <summary>Every renderer in the open scene, the hidden ones
        /// included: the weather preview is DontSave, which
        /// FindObjectsByType does not return.</summary>
        static IEnumerable<Renderer> SceneRenderers()
        {
            foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
                if (r != null && r.gameObject.scene.IsValid() && r.gameObject.activeInHierarchy) yield return r;
        }

        /// <summary>For the log: every PSX/Decal renderer in the scene (the
        /// flakes, the smoke) and its _Lit.</summary>
        static string ParticleLitState()
        {
            var sb = new StringBuilder();
            foreach (var r in SceneRenderers())
            {
                var m = r != null ? r.sharedMaterial : null;
                if (m == null || m.shader == null || m.shader.name != "PSX/Decal") continue;
                sb.Append(r.name).Append(" _Lit ").Append(m.HasProperty("_Lit") ? m.GetFloat("_Lit").ToString("0") : "-").Append("; ");
            }
            return sb.Length > 0 ? sb.ToString() : "none found";
        }

        static void ClearSmoke()
        {
            var holder = typeof(TireSmoke).GetField("holder", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var s in smoked)
            {
                if (s == null || holder == null) continue;
                if (holder.GetValue(s) is GameObject go && go != null) Object.DestroyImmediate(go);
                holder.SetValue(s, null);
            }
            smoked.Clear();
        }

        static void EndFrame()
        {
            ClearSmoke();
            UntintSnow();
            CarLights.BeamIntensityOverride = 0f;
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
