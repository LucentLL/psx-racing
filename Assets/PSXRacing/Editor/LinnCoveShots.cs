using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LINNCOVE ROUND's pictures (owner 2026-11-16: the barren far
    /// mountains, and "the iconic rock face" in the track and the title shot).
    /// Real editor frames of the built scenes, PC profile, through the game's
    /// own dither:
    ///
    ///   title   the title postcard (TitleScreen.PlaceCamera's rule, views[0],
    ///           or PSX_LC_EYE / PSX_LC_LOOK), fall golden hour, fall fog,
    ///           summer, winter
    ///   linn    Linn Cove from the road: on the deck before the curve, and
    ///           from below where the boulders lie under the deck
    ///   road    a stage's road view and its vista at noon and dusk
    ///           (PSX_LC_ROAD = "SwissNC226A:250,MtMitchell:6800,ChimneyRock:3800")
    ///   perf    the title pose rendered 24 times, far canopy on and off
    ///
    /// The switches (PSX_FARCANOPY, PSX_LINNCOVE_ROCKS, PSX_AERIAL) are read as
    /// the player reads them; PSX_LC_AERIAL=1 adds an "aerial" frame (the blue
    /// distance v2) beside each title and vista frame. Writes
    /// Screenshots/lc_&lt;tag&gt;_*.png and lc_&lt;tag&gt;.txt (PSX_LC_TAG).
    /// </summary>
    public static class LinnCoveShots
    {
        static string Env(string k, string d) { var v = System.Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }
        static string OutDir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
        static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();
        static string tag;

        [System.Serializable] class StageJson { public float spacing; public float[] bridges; }

        public static void Capture()
        {
            Directory.CreateDirectory(OutDir);
            tag = Env("PSX_LC_TAG", "x");
            foreach (var f in Directory.GetFiles(OutDir, "lc_" + tag + "_*")) File.Delete(f);
            log.Clear();
            log.AppendLine($"tag {tag}: PSX_FARCANOPY={Env("PSX_FARCANOPY", "-")} PSX_LINNCOVE_ROCKS={Env("PSX_LINNCOVE_ROCKS", "-")} PSX_AERIAL={Env("PSX_AERIAL", "-")} PC {PlatformProfile.PC}");
            var sets = new HashSet<string>(Env("PSX_LC_SETS", "title,linn,road,perf").Split(','));
            try
            {
                if (sets.Contains("title") || sets.Contains("linn") || sets.Contains("perf")) BlueRidge(sets);
                if (sets.Contains("road"))
                    foreach (var spec in Env("PSX_LC_ROAD", "SwissNC226A:250,MtMitchell:6800,ChimneyRock:3800").Split(','))
                    {
                        var kv = spec.Split(':');
                        if (kv.Length == 2) Road(kv[0].Trim(), float.Parse(kv[1], CultureInfo.InvariantCulture));
                    }
            }
            catch (System.Exception e) { log.AppendLine("THREW " + e); }
            finally
            {
                LookChoices.AerialForce = null; LookChoices.FarCanopyForce = null;
                Seasons.Forced = null; Seasons.Invalidate();
                TimeOfDay.FarEdgeM = 0f; TimeOfDay.HorizonM = 0f;
            }
            File.WriteAllText(Path.Combine(OutDir, "lc_" + tag + ".txt"), log.ToString());
            Debug.Log("[LinnCoveShots] " + log);
        }

        // ------------------------------------------------------------ scene
        class Scene
        {
            public Camera cam; public TrackPath path; public Light sun; public PSXGlobals globals;
            public SeasonDress dressing; public MeshForest forest; public StageCulling sc;
        }

        static Scene Open(string id)
        {
            TrackCatalog.TrackDef def = null;
            for (int i = 0; i < TrackCatalog.Count; i++) if (TrackCatalog.At(i).id == id) def = TrackCatalog.At(i);
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out _)) { log.AppendLine("no scene " + id); return null; }
            var s = new Scene
            {
                cam = cam, path = Object.FindAnyObjectByType<TrackPath>(), sun = GameObject.Find("Sun")?.GetComponent<Light>(),
                globals = Object.FindAnyObjectByType<PSXGlobals>(), dressing = Object.FindAnyObjectByType<SeasonDress>(),
                forest = Object.FindAnyObjectByType<MeshForest>(), sc = cam.GetComponent<StageCulling>(),
            };
            if (s.path == null || s.globals == null || s.sun == null) { log.AppendLine("scene parts missing in " + id); return null; }
            if (s.sc != null) s.sc.Configure(cam);
            int rocks = LandmarkRocks.ApplyAll();
            Physics.SyncTransforms();
            int horizon = LayerMask.NameToLayer(StageCulling.HorizonLayerName), crowns = 0, fringe = 0;
            var seen = new HashSet<Material>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                if (r.gameObject.layer != horizon) continue;
                if (r.gameObject.name.StartsWith("Fringe")) fringe++;
                foreach (var m in r.sharedMaterials)
                    if (m != null && seen.Add(m) && m.HasProperty("_HorizonCrowns") && m.GetFloat("_HorizonCrowns") > 0.5f) crowns++;
            }
            log.AppendLine($"== {id}: far plane {cam.farClipPlane:0} m, horizon {TimeOfDay.HorizonM:0} m, far-canopy materials {crowns}, fringe meshes {fringe}, " +
                           $"landmark roots {rocks} (rocks {(LookChoices.LinnCoveRocks ? "on" : "off")}), far canopy {(LookChoices.FarCanopy ? "on" : "off")}");
            return s;
        }

        static int DayFor(int dress)
        {
            if (dress == (int)Season.Fall || dress == Seasons.DressSnow) return 0;
            int first = -1, last = -1;
            for (int d = 1; d < 400; d++)
            {
                if ((int)Seasons.Of(d) == dress) { if (first < 0) first = d; last = d; }
                else if (first >= 0) break;
            }
            return first < 0 ? 0 : (first + last) / 2;
        }

        static void Condition(Scene s, int dress, string hourName, bool fog, Vector3 eye)
        {
            int day = DayFor(dress);
            RaceHandoff.CalendarDay = day;
            if (s.dressing != null)
            {
                typeof(SeasonDress).GetMethod("Register", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(s.dressing, null);
                s.dressing.Apply(dress);
            }
            RaceHandoff.WeatherOverride = -1;
            Seasons.Forced = null; Seasons.Invalidate();
            var w = Seasons.Now;
            w.fog = fog; w.precip = PrecipKind.None; w.precipRate = 0f;
            w.sky = fog ? SkyKind.Overcast : SkyKind.Clear;
            Seasons.Forced = w;
            int hour = 0;
            for (int h = 0; h < TimeOfDay.Count; h++) if (TimeOfDay.At(h).name.ToLower() == hourName.ToLower()) hour = h;
            if (s.sc != null) s.sc.Configure(s.cam);
            TimeOfDay.Apply(hour, s.sun);
            s.globals.Apply();
            DynamicSky.PrimeClouds(eye);
        }

        static void Grab(Scene s, string name, Vector3 eye, Quaternion rot, int h)
        {
            s.cam.transform.SetPositionAndRotation(eye, rot);
            if (s.sc != null) s.sc.Configure(s.cam);
            SunShadows.RenderFor(s.cam);
            if (s.forest != null) s.forest.DrawNow(s.cam);
            int w = Mathf.RoundToInt(h * 16f / 9f) & ~1;
            var rt = new RenderTexture(w, h, 32, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            PSXTexDecode.StampAll();
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (!RenderPipeline.SupportsRenderRequest(s.cam, req)) { log.AppendLine("render request unsupported"); return; }
            RenderPipeline.SubmitRenderRequest(s.cam, req);
            var shown = PSXScreenshotTool.Dithered(rt);
            var prev = RenderTexture.active;
            RenderTexture.active = shown;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
            File.WriteAllBytes(Path.Combine(OutDir, "lc_" + tag + "_" + name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release(); Object.DestroyImmediate(rt);
            log.AppendLine($"  {name}: eye ({eye.x:0.0},{eye.y:0.0},{eye.z:0.0}) yaw {rot.eulerAngles.y:0.0} pitch {rot.eulerAngles.x:0.0} fov {s.cam.fieldOfView:0} {w}x{h}");
        }

        /// <summary>The frame, and with PSX_LC_AERIAL=1 its blue-distance twin.</summary>
        static void GrabAB(Scene s, string name, Vector3 eye, Quaternion rot, int h, int dress, string hour, bool fog, bool ab)
        {
            LookChoices.AerialForce = null;
            Condition(s, dress, hour, fog, eye);
            Grab(s, name, eye, rot, h);
            if (!ab || Env("PSX_LC_AERIAL", "0") != "1") return;
            LookChoices.AerialForce = true;
            Condition(s, dress, hour, fog, eye);
            Grab(s, name + "_aerial", eye, rot, h);
            LookChoices.AerialForce = null;
        }

        // ------------------------------------------------------------ Blue Ridge
        static void BlueRidge(HashSet<string> sets)
        {
            var s = Open("BlueRidge");
            if (s == null) return;
            var path = s.path;
            var js = Resources.Load<TextAsset>("brp_stage");
            var sj = js != null ? JsonUtility.FromJson<StageJson>(js.text) : null;
            float spacing = sj != null && sj.spacing > 0f ? sj.spacing : TrackCatalog.Spacing;
            Vector2 best = new Vector2(4488f, 4880f);
            if (sj != null && sj.bridges != null)
                for (int k = 0; k + 1 < sj.bridges.Length; k += 2)
                    if (sj.bridges[k + 1] - sj.bridges[k] > best.y - best.x || k == 0) best = new Vector2(sj.bridges[k], sj.bridges[k + 1]);
            int i0 = Mathf.Clamp(Mathf.RoundToInt(best.x / spacing), 1, path.Count - 2);
            int i1 = Mathf.Clamp(Mathf.RoundToInt(best.y / spacing), 1, path.Count - 2);
            var (eye, look) = Postcard(path, i0, i1);
            string eyeArg = Env("PSX_LC_EYE", ""), lookArg = Env("PSX_LC_LOOK", "");
            if (eyeArg.Length > 0 && lookArg.Length > 0) { eye = V3(eyeArg); look = V3(lookArg); log.AppendLine("  postcard from PSX_LC_EYE/LOOK"); }
            var rot = Quaternion.LookRotation(look - eye, Vector3.up);
            float fovWas = s.cam.fieldOfView;
            s.cam.fieldOfView = 42f;   // TitleScreen.Fov
            log.AppendLine($"  postcard (views[0] by TitleScreen.PlaceCamera's rule): eye {eye} look {look}, viaduct wp {i0}-{i1}");
            if (sets.Contains("title"))
            {
                GrabAB(s, "title_fall_sunset", eye, rot, 1440, (int)Season.Fall, "SUNSET", false, true);
                GrabAB(s, "title_fall_fog", eye, rot, 1440, (int)Season.Fall, "MORNING", true, false);
                GrabAB(s, "title_summer_afternoon", eye, rot, 1440, (int)Season.Summer, "AFTERNOON", false, true);
                GrabAB(s, "title_winter_noon", eye, rot, 1440, (int)Season.Winter, "NOON", false, true);
            }
            if (sets.Contains("perf")) Perf(s, eye, rot);
            s.cam.fieldOfView = fovWas;
            if (sets.Contains("linn"))
            {
                // on the deck before the curve: the driver's eye in the right-hand lane
                int a = Mathf.Clamp(i0 + 14, 2, path.Count - 12);
                Vector3 p = path.GetPoint(a), f = path.GetTangent(a); f.y = 0f; f.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, f);
                float lane = path.roadWidth * 0.25f - 0.37f;
                var deckEye = p + right * lane + Vector3.up * (PSXRacingBuilder.RoadLift + 1.15f);
                int ahead = Mathf.Clamp(a + 9, 0, path.Count - 1);
                var deckRot = Quaternion.LookRotation(path.GetPoint(ahead) + right * lane + Vector3.up * (PSXRacingBuilder.RoadLift + 0.9f) - deckEye);
                GrabAB(s, "linn_deck", deckEye, deckRot, 1080, (int)Season.Fall, "AFTERNOON", false, false);
                // the chase view a little further on, looking over the rail at the field
                int b2 = Mathf.Clamp(i0 + 40, 2, path.Count - 12);
                Vector3 pb = path.GetPoint(b2), fb = path.GetTangent(b2); fb.y = 0f; fb.Normalize();
                var chaseEye = pb - fb * 8f + Vector3.up * 2.6f;
                GrabAB(s, "linn_chase", chaseEye, Quaternion.LookRotation(pb + Vector3.up * 0.8f + fb * 20f - chaseEye), 1080, (int)Season.Fall, "AFTERNOON", false, false);
                // from below: the downhill side, under the deck's line, looking
                // up the slope at the deck and the blocks under it (photo b)
                int c = Mathf.Clamp((i0 + i1) / 2 + 2, 2, path.Count - 12);
                Vector3 pc = path.GetPoint(c), fc = path.GetTangent(c); fc.y = 0f; fc.Normalize();
                Vector3 rc = Vector3.Cross(Vector3.up, fc);
                float upR = GroundY(pc + rc * 50f, pc.y), upL = GroundY(pc - rc * 50f, pc.y);
                Vector3 down = upR < upL ? rc : -rc;
                var low = pc + down * 70f;
                low.y = GroundY(low, pc.y - 20f) + 1.7f;
                var lowRot = Quaternion.LookRotation(pc - Vector3.up * 6f - low);
                GrabAB(s, "linn_below", low, lowRot, 1080, (int)Season.Fall, "AFTERNOON", false, false);
                // and the field from the valley side, wide
                var wide = pc + down * 150f - fc * 60f; wide.y = GroundY(wide, pc.y) + 30f;
                GrabAB(s, "linn_wide", wide, Quaternion.LookRotation(pc - Vector3.up * 10f + (-down) * 40f - wide), 1080, (int)Season.Fall, "AFTERNOON", false, false);
            }
        }

        static Vector3 V3(string s)
        {
            var p = s.Trim('(', ')', ' ').Split(',');
            return new Vector3(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture));
        }

        static float GroundY(Vector3 at, float fallback) =>
            Physics.Raycast(at + Vector3.up * 400f, Vector3.down, out var hit, 900f) ? hit.point.y : fallback;

        /// <summary>TitleScreen.PlaceCamera's rule, as it stands (e26dc1f4): views[0].</summary>
        static (Vector3, Vector3) Postcard(TrackPath path, int i0, int i1)
        {
            int n = i1 - i0;
            var deck = new Vector3[5];
            float GroundAt(Vector3 p)
            {
                var top = new Vector3(p.x, p.y + 600f, p.z);
                return Physics.Raycast(top, Vector3.down, out var hit, 2000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.point.y : p.y - 1000f;
            }
            foreach (int end in new[] { 0, 1 })
            {
                int at = end == 0 ? i0 : i1;
                float dirSign = end == 0 ? 1f : -1f;
                var a = path.GetPoint(at);
                var along = path.GetTangent(at).normalized * dirSign;
                var right = Vector3.Cross(Vector3.up, along).normalized;
                float upR = GroundAt(a + right * 50f), upL = GroundAt(a - right * 50f);
                float uphill = upR >= upL ? 1f : -1f;
                for (int k = 0; k < 5; k++)
                {
                    float f = 0.15f + 0.175f * k;
                    int wi = end == 0 ? i0 + Mathf.RoundToInt(n * f) : i1 - Mathf.RoundToInt(n * f);
                    deck[k] = path.GetPoint(wi) + Vector3.up * 1.5f;
                }
                int li = end == 0 ? i0 + Mathf.RoundToInt(n * 0.6f) : i1 - Mathf.RoundToInt(n * 0.6f);
                var look = path.GetPoint(li) + Vector3.up * 1f;
                foreach (float side in new[] { uphill, -uphill })
                    foreach (float dist in new[] { 30f, 45f, 60f, 80f })
                        foreach (float back in new[] { 15f, 35f })
                            foreach (float up in new[] { 25f, 35f, 50f })
                            {
                                var eye = a - along * back + right * side * dist + Vector3.up * up;
                                if (eye.y - GroundAt(eye) < 22f) continue;
                                int seen = 0;
                                foreach (var p in deck)
                                    if (!Physics.Linecast(eye, p, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) seen++;
                                if (seen >= 4) return (eye, look);
                            }
            }
            var a0 = path.GetPoint(i0);
            var t = path.GetTangent(i0).normalized;
            var r0 = Vector3.Cross(Vector3.up, t).normalized;
            return (a0 - t * 60f + r0 * 110f + Vector3.up * 35f, path.GetPoint((i0 + i1) / 2) + Vector3.up * 2f);
        }

        // ------------------------------------------------------------ a stage's road
        static void Road(string id, float m)
        {
            var s = Open(id);
            if (s == null) return;
            var path = s.path;
            int at = Mathf.Clamp(Mathf.RoundToInt(m / TrackCatalog.Spacing), 2, path.Count - 12);
            Vector3 p = path.GetPoint(at), fwd = path.GetTangent(at); fwd.y = 0f; fwd.Normalize();
            var chaseEye = p - fwd * 8f + Vector3.up * 2.6f;
            var chaseRot = Quaternion.LookRotation(p + Vector3.up * 0.8f + fwd * 20f - chaseEye);
            // the vista: 25 m up, level, turned 60 degrees toward the open side
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float yr = GroundY(p + right * 60f, p.y), yl = GroundY(p - right * 60f, p.y);
            float open = yr <= yl ? 1f : -1f;
            var vistaEye = p + Vector3.up * 25f;
            var vistaRot = Quaternion.LookRotation(Quaternion.Euler(0f, open * 60f, 0f) * fwd);
            float fovWas = s.cam.fieldOfView;
            foreach (var hour in new[] { "NOON", "DUSK" })
            {
                GrabAB(s, $"road_{id}_{m:0}_{hour.ToLower()}_chase", chaseEye, chaseRot, 1080, (int)Season.Fall, hour, false, false);
                GrabAB(s, $"road_{id}_{m:0}_{hour.ToLower()}_vista", vistaEye, vistaRot, 1080, (int)Season.Fall, hour, false, true);
            }
            s.cam.fieldOfView = fovWas;
        }

        // ------------------------------------------------------------ cost
        /// <summary>The title pose drawn 24 times a side at 1920 x 1080, the far
        /// canopy forced on and off in turn (twice, ABAB), each draw waited on
        /// with a one-pixel read. Editor timing: the difference is the number.</summary>
        static void Perf(Scene s, Vector3 eye, Quaternion rot)
        {
            Condition(s, (int)Season.Fall, "SUNSET", false, eye);
            s.cam.transform.SetPositionAndRotation(eye, rot);
            var rt = new RenderTexture(1920, 1080, 32, RenderTextureFormat.ARGB32);
            rt.Create();
            var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            double Time(bool on)
            {
                LookChoices.FarCanopyForce = on;
                if (s.sc != null) s.sc.Configure(s.cam);
                var req = new RenderPipeline.StandardRequest { destination = rt };
                for (int k = 0; k < 4; k++) RenderPipeline.SubmitRenderRequest(s.cam, req);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                const int N = 24;
                for (int k = 0; k < N; k++)
                {
                    RenderPipeline.SubmitRenderRequest(s.cam, req);
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); one.Apply();
                    RenderTexture.active = prev;
                }
                return sw.Elapsed.TotalMilliseconds / N;
            }
            double on1 = Time(true), off1 = Time(false), on2 = Time(true), off2 = Time(false);
            LookChoices.FarCanopyForce = null;
            if (s.sc != null) s.sc.Configure(s.cam);
            log.AppendLine($"  PERF title pose 1920x1080, ms a frame (editor, render + 1 px read): canopy ON {on1:0.00} / {on2:0.00}, OFF {off1:0.00} / {off2:0.00}; " +
                           $"difference {(on1 + on2 - off1 - off2) / 2:0.00} ms");
            Object.DestroyImmediate(one);
            rt.Release(); Object.DestroyImmediate(rt);
        }
    }
}
