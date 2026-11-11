using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CANOPY'S ONE LIGHTING RULE, MEASURED (owner 2026-11-11, the title's
    /// drone over Linn Cove: "poplars seem very repetitive, clumped, and not
    /// following the same lighting as the other trees").
    ///
    /// For each venue in PSX_PP_VENUES, each day in PSX_PP_DAYS (290 fall, 200
    /// summer) and each hour in PSX_PP_HOURS:
    ///  1. every canopy species of the mesh forest ALONE (its own layer, no
    ///     scene), at its mid level (LOD1, leaves only) and as its impostor,
    ///     from three sides of the sun - sun to the side (sunward and shaded
    ///     half of the silhouette), sun behind the eye (front-lit) and the eye
    ///     looking into it (back-lit) - mean pixel luma over five placings
    ///     (the per-tree shade rides on the instance's position), written to
    ///     PSXRacing_poplar_probe_(tag).txt;
    ///  2. the scene from fixed poses (Screenshots/pp_(tag)_(venue)_(day)_(hour)_(pose).png):
    ///     BlueRidge - the title's drone shots at mid-move (Establish, Pan,
    ///     Descend: TitleScreen.Drone's own eyes); any venue - "high" (150 m out
    ///     over the valley side, 90 m up) and road-level chase and cockpit at
    ///     the stations in PSX_PP_STATIONS ("BlueRidge=4600,7200"), or where
    ///     the most poplars stand within 60 m of the road; with the forest's
    ///     triangles in view (each species and level, from MeshForest's lists).
    /// </summary>
    public static class PoplarProbe
    {
        static string Env(string k, string d) { var v = System.Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }

        public static void Run()
        {
            string tag = Env("PSX_PP_TAG", "before");
            string root = Path.GetDirectoryName(Application.dataPath);
            string shotDir = Path.Combine(root, "Screenshots");
            Directory.CreateDirectory(shotDir);
            foreach (var f in Directory.GetFiles(shotDir, "pp_" + tag + "_*")) File.Delete(f);
            var log = new StringBuilder();
            log.AppendLine("PoplarProbe " + tag + " " + System.DateTime.Now.ToString("s"));
            try
            {
                foreach (var raw in Env("PSX_PP_VENUES", "BlueRidge").Split(','))
                    Venue(raw.Trim(), tag, log);
            }
            catch (System.Exception e) { log.AppendLine("THREW " + e); }
            finally
            {
                Seasons.Forced = null; Seasons.Invalidate();
                RaceHandoff.CalendarDay = 0;
            }
            File.WriteAllText(Path.Combine(root, "PSXRacing_poplar_probe_" + tag + ".txt"), log.ToString());
            Debug.Log("[PoplarProbe]\n" + log);
        }

        static void Venue(string id, string tag, StringBuilder log)
        {
            if (id.Length == 0) return;
            TrackCatalog.TrackDef def = null;
            for (int i = 0; i < TrackCatalog.Count; i++) if (TrackCatalog.At(i).id == id) def = TrackCatalog.At(i);
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out _)) { log.AppendLine("no scene " + id); return; }
            var path = Object.FindFirstObjectByType<TrackPath>();
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var dressing = Object.FindFirstObjectByType<SeasonDress>();
            var forest = Object.FindFirstObjectByType<MeshForest>();
            var output = Object.FindFirstObjectByType<PSXCameraOutput>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (path == null || sun == null || forest == null) { log.AppendLine(id + ": scene parts missing (forest " + (forest != null) + ")"); return; }
            if (output != null) output.height = 720;
            var sc = cam.GetComponent<StageCulling>();
            if (sc != null) sc.Configure(cam);

            // the species
            var canopy = new List<int>();
            int poplar = -1;
            for (int v = 0; v < forest.variants.Length; v++)
            {
                var vr = forest.variants[v];
                if (vr.lods.Length < 4 || vr.lods[3] == null || vr.maxDist > 0f) continue;
                canopy.Add(v);
                if (Name(vr).ToLower().Contains("poplar")) poplar = v;
            }
            // census: how many of each, and how many poplars stand next to another
            var count = new int[forest.variants.Length];
            foreach (var sv in forest.instScaleVar) count[(int)sv.y]++;
            int canopyN = 0; foreach (int v in canopy) canopyN += count[v];
            log.AppendLine($"=== {id}: {forest.inst.Length} plants, canopy {canopyN} in {canopy.Count} species");
            foreach (int v in canopy)
                log.AppendLine($"  {Name(forest.variants[v]),-28} {count[v],7}  {100f * count[v] / Mathf.Max(1, canopyN),5:0.0}%  tris LOD0/1/2/imp {Tris(forest.variants[v].lods[0])}/{Tris(forest.variants[v].lods[1])}/{Tris(forest.variants[v].lods[2])}/{Tris(forest.variants[v].lods[3])}");
            log.AppendLine("  " + Neighbours(forest, canopy, poplar));

            var stations = new List<float>();
            foreach (var part in Env("PSX_PP_STATIONS", "").Split(';'))
                if (part.StartsWith(id + "="))
                    foreach (var s in part.Substring(id.Length + 1).Split(','))
                        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float m)) stations.Add(m);
            if (stations.Count == 0 && poplar >= 0) stations = PoplarStations(forest, path, poplar);
            log.AppendLine("  road stations: " + string.Join(",", stations.ConvertAll(s => s.ToString("0", CultureInfo.InvariantCulture))));

            foreach (var dayS in Env("PSX_PP_DAYS", "290,200").Split(','))
            {
                int day = int.Parse(dayS.Trim(), CultureInfo.InvariantCulture);
                RaceHandoff.CalendarDay = day;
                Seasons.Forced = null; Seasons.Invalidate();
                var w = Seasons.Now;
                w.fog = false; w.precip = PrecipKind.None; w.precipRate = 0f; w.sky = SkyKind.Clear;
                Seasons.Forced = w;
                int dress = (int)Seasons.Of(day);
                if (dressing != null)
                {
                    typeof(SeasonDress).GetMethod("Register", System.Reflection.BindingFlags.NonPublic |
                                                  System.Reflection.BindingFlags.Instance)?.Invoke(dressing, null);
                    dressing.Apply(dress);
                }
                foreach (var hourS in Env("PSX_PP_HOURS", "SUNSET,NOON").Split(','))
                {
                    int hour = -1;
                    for (int h = 0; h < TimeOfDay.Count; h++) if (TimeOfDay.At(h).name == hourS.Trim().ToUpper()) hour = h;
                    if (hour < 0) continue;
                    TimeOfDay.Apply(hour, sun);
                    if (globals != null) globals.Apply();
                    string key = $"{id}_{day}_{hourS.Trim().ToLower()}";
                    log.AppendLine($"--- {key} (dress {Seasons.DressNames[dress]})");
                    Luma(forest, canopy, path, cam, dress, log);
                    Scenes(id, def, forest, path, cam, sun, hour, stations, tag, key, log);
                }
            }
        }

        static string Name(MeshForest.Variant vr) => vr.lods[0] != null ? vr.lods[0].name : "?";

        static long Tris(Mesh m)
        {
            if (m == null) return 0;
            long n = 0;
            for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s) / 3;
            return n;
        }

        /// <summary>Poplars whose nearest canopy neighbour (within 9 m) is also a
        /// poplar, and the largest group of poplars each within 9 m of the next.</summary>
        static string Neighbours(MeshForest f, List<int> canopy, int poplar)
        {
            if (poplar < 0) return "no poplar";
            var isCanopy = new HashSet<int>(canopy);
            var cells = new Dictionary<Vector2Int, List<int>>();
            for (int i = 0; i < f.inst.Length; i++)
            {
                if (!isCanopy.Contains((int)f.instScaleVar[i].y)) continue;
                var k = new Vector2Int(Mathf.FloorToInt(f.inst[i].x / 9f), Mathf.FloorToInt(f.inst[i].z / 9f));
                if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<int>();
                l.Add(i);
            }
            int pop = 0, nearPop = 0, pairs = 0;
            var adj = new Dictionary<int, List<int>>();
            foreach (var kv in cells)
                foreach (int i in kv.Value)
                {
                    if ((int)f.instScaleVar[i].y != poplar) continue;
                    pop++;
                    float best = 1e9f; int bestJ = -1;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                            if (cells.TryGetValue(kv.Key + new Vector2Int(dx, dz), out var l))
                                foreach (int j in l)
                                {
                                    if (j == i) continue;
                                    float d = new Vector2(f.inst[i].x - f.inst[j].x, f.inst[i].z - f.inst[j].z).magnitude;
                                    if (d < 9f && (int)f.instScaleVar[j].y == poplar)
                                    {
                                        pairs++;
                                        if (!adj.TryGetValue(i, out var a)) adj[i] = a = new List<int>();
                                        a.Add(j);
                                    }
                                    if (d < best) { best = d; bestJ = j; }
                                }
                    if (bestJ >= 0 && best < 9f && (int)f.instScaleVar[bestJ].y == poplar) nearPop++;
                }
            // largest connected group
            int biggest = pop > 0 ? 1 : 0;
            var seen = new HashSet<int>();
            foreach (var s in adj.Keys)
            {
                if (seen.Contains(s)) continue;
                int n = 0; var stack = new Stack<int>(); stack.Push(s); seen.Add(s);
                while (stack.Count > 0) { int x = stack.Pop(); n++; if (adj.TryGetValue(x, out var a)) foreach (int y in a) if (seen.Add(y)) stack.Push(y); }
                biggest = Mathf.Max(biggest, n);
            }
            return $"poplars {pop}: nearest canopy neighbour a poplar {100f * nearPop / Mathf.Max(1, pop):0.0}%, poplar pairs within 9 m {pairs / 2}, largest group {biggest}";
        }

        /// <summary>Up to two stations (m), 600 m apart, with the most poplars within 60 m of the road there.</summary>
        static List<float> PoplarStations(MeshForest f, TrackPath path, int poplar)
        {
            var per = new int[path.Count];
            for (int i = 0; i < f.inst.Length; i++)
            {
                if ((int)f.instScaleVar[i].y != poplar) continue;
                var w = f.transform.TransformPoint(new Vector3(f.inst[i].x, f.inst[i].y, f.inst[i].z));
                int n = path.NearestIndex(w);
                if ((path.GetPoint(n) - w).magnitude < 35f) per[n]++;
            }
            var score = new int[path.Count];
            int win = Mathf.Max(1, Mathf.RoundToInt(60f / path.spacing));
            for (int i = 0; i < path.Count; i++)
                for (int j = Mathf.Max(0, i - win); j < Mathf.Min(path.Count, i + win); j++) score[i] += per[j];
            var res = new List<float>();
            for (int k = 0; k < 2; k++)
            {
                int best = -1;
                for (int i = 20; i < path.Count - 20; i++)
                {
                    bool apart = true;
                    foreach (float r in res) if (Mathf.Abs(r - i * path.spacing) < 600f) apart = false;
                    if (apart && (best < 0 || score[i] > score[best])) best = i;
                }
                if (best >= 0) res.Add(Mathf.Max(0f, best * path.spacing - 15f));
            }
            return res;
        }

        // ------------------------------------------------------------------
        //  1. Each species alone
        // ------------------------------------------------------------------

        static int FreeLayer()
        {
            for (int l = 31; l >= 8; l--) if (string.IsNullOrEmpty(LayerMask.LayerToName(l))) return l;
            return 31;
        }

        static Material Unfaded(Material m)
        {
            if (m == null) return null;
            var c = new Material(m) { enableInstancing = true };
            c.SetVector("_PSXLodFade", Vector4.zero);
            c.SetVector("_PSXLodThin", Vector4.zero);
            c.EnableKeyword("PSX_LODFADE");
            return c;
        }

        static void Luma(MeshForest forest, List<int> canopy, TrackPath path, Camera cam, int dress, StringBuilder log)
        {
            int layer = FreeLayer();
            int keepMask = cam.cullingMask; var keepFlags = cam.clearFlags; var keepBg = cam.backgroundColor; float keepFov = cam.fieldOfView;
            float keepFar = cam.farClipPlane;
            Vector4 keepEye = Shader.GetGlobalVector("_PSXLodEye");
            Shader.SetGlobalVector("_PSXLodEye", Vector4.zero);
            cam.cullingMask = 1 << layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView = 40f;
            Vector3 l = Shader.GetGlobalVector("_PSXLightDir");
            Vector3 s = new Vector3(l.x, 0f, l.z).normalized;
            if (s.sqrMagnitude < 0.5f) s = Vector3.forward;
            log.AppendLine($"  sun dir ({l.x:0.00},{l.y:0.00},{l.z:0.00}) elevation {Mathf.Asin(Mathf.Clamp(l.normalized.y, -1f, 1f)) * Mathf.Rad2Deg:0.0} deg; mean luma 0-255 over the tree's pixels, 5 placings");
            log.AppendLine("  species                       level    sunward  shaded  ratio  front-lit back-lit   sd(front)");
            Vector3 basePos = path.GetPoint(path.Count / 2) + Vector3.up * 500f;
            int di = Mathf.Clamp(dress, 0, 4);
            const int W = 360, H = 360;
            foreach (int v in canopy)
            {
                var vr = forest.variants[v];
                var leafM = vr.leafDress != null && vr.leafDress.Length == 5 ? vr.leafDress[di] : forest.leafByDress[di];
                var impM = vr.impostorDress != null && vr.impostorDress.Length == 5 ? vr.impostorDress[di] : null;
                for (int level = 0; level < 2; level++)
                {
                    var mesh = level == 0 ? vr.lods[1] : vr.lods[3];
                    var mat = Unfaded(level == 0 ? leafM : impM);
                    if (mesh == null || mat == null) { log.AppendLine($"  {Name(vr),-28} {(level == 0 ? "LOD1" : "imp"),-6} (none in this dress)"); continue; }
                    int sub = 0;
                    if (level == 0) for (int q = 0; q < vr.leafSub.Length; q++) if (vr.leafSub[q]) sub = q;
                    var pre = level == 0 ? vr.pre : vr.impostorPre;
                    double sunw = 0, shade = 0, front = 0, back = 0, sdf = 0; int nOk = 0;
                    for (int k = 0; k < 5; k++)
                    {
                        Vector3 p = basePos + new Vector3(k * 37.3f, 0f, k * 11.9f);
                        var M = Matrix4x4.TRS(p, Quaternion.Euler(0f, k * 71f, 0f), Vector3.one) * pre;
                        float hgt = vr.height, dist = Mathf.Max(30f, hgt * 1.9f);
                        Vector3 aim = p + Vector3.up * hgt * 0.5f;
                        // side: the eye square to the sun's azimuth
                        Vector3 side = Vector3.Cross(Vector3.up, s);
                        var a = Shoot(cam, mesh, sub, mat, M, layer, aim - side * dist, aim, W, H);
                        var b = Shoot(cam, mesh, sub, mat, M, layer, aim - s * dist, aim, W, H);       // eye away from the sun, looking into it: back-lit
                        var c = Shoot(cam, mesh, sub, mat, M, layer, aim + s * dist, aim, W, H);       // eye on the sun's side: front-lit
                        // the screen's right is Cross(up, forward)
                        var sideStats = Halves(a, W, H, sunOnRight: Vector3.Dot(Vector3.Cross(Vector3.up, side), s) > 0f);
                        var fs = Stats(c); var bs = Stats(b);
                        if (sideStats.n == 0 || fs.n == 0) continue;
                        sunw += sideStats.sun; shade += sideStats.shade; front += fs.mean; back += bs.mean; sdf += fs.sd; nOk++;
                    }
                    if (nOk == 0) { log.AppendLine($"  {Name(vr),-28} {(level == 0 ? "LOD1" : "imp"),-6} (nothing drawn)"); continue; }
                    sunw /= nOk; shade /= nOk; front /= nOk; back /= nOk; sdf /= nOk;
                    log.AppendLine($"  {Name(vr),-28} {(level == 0 ? "LOD1" : "imp"),-6} {sunw,8:0.0} {shade,7:0.0} {sunw / System.Math.Max(1.0, shade),6:0.00} {front,9:0.0} {back,8:0.0} {sdf,9:0.0}");
                    Object.DestroyImmediate(mat);
                }
            }
            cam.cullingMask = keepMask; cam.clearFlags = keepFlags; cam.backgroundColor = keepBg; cam.fieldOfView = keepFov; cam.farClipPlane = keepFar;
            Shader.SetGlobalVector("_PSXLodEye", keepEye);
        }

        static Color32[] Shoot(Camera cam, Mesh mesh, int sub, Material mat, Matrix4x4 M, int layer, Vector3 eye, Vector3 aim, int W, int H)
        {
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
            var rp = new RenderParams(mat) { camera = cam, layer = layer, worldBounds = new Bounds(aim, Vector3.one * 200f),
                                              shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            Graphics.RenderMeshInstanced(rp, mesh, sub, new[] { M }, 1, 0);
            var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
            var req = new RenderPipeline.StandardRequest { destination = rt };
            RenderPipeline.SubmitRenderRequest(cam, req);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return px;
        }

        static float Y(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        static bool On(Color32 c) => c.r + c.g + c.b > 3;

        static (double mean, double sd, int n) Stats(Color32[] px)
        {
            double s = 0, s2 = 0; int n = 0;
            foreach (var c in px) if (On(c)) { float y = Y(c); s += y; s2 += y * y; n++; }
            if (n == 0) return (0, 0, 0);
            double m = s / n;
            return (m, System.Math.Sqrt(System.Math.Max(0, s2 / n - m * m)), n);
        }

        static (double sun, double shade, int n) Halves(Color32[] px, int W, int H, bool sunOnRight)
        {
            // the silhouette's own middle, column-wise
            double sx = 0; int n = 0;
            for (int i = 0; i < px.Length; i++) if (On(px[i])) { sx += i % W; n++; }
            if (n == 0) return (0, 0, 0);
            double mid = sx / n;
            double r = 0, l = 0; int nr = 0, nl = 0;
            for (int i = 0; i < px.Length; i++)
            {
                if (!On(px[i])) continue;
                if (i % W > mid) { r += Y(px[i]); nr++; } else { l += Y(px[i]); nl++; }
            }
            double mr = nr > 0 ? r / nr : 0, ml = nl > 0 ? l / nl : 0;
            return sunOnRight ? (mr, ml, n) : (ml, mr, n);
        }

        // ------------------------------------------------------------------
        //  2. The scene
        // ------------------------------------------------------------------

        static float GroundAt(Vector3 p) =>
            Physics.Raycast(new Vector3(p.x, p.y + 2000f, p.z), Vector3.down, out var hit, 5000f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : p.y - 50f;

        static void Scenes(string id, TrackCatalog.TrackDef def, MeshForest forest, TrackPath path, Camera cam, Light sun, int hour,
                           List<float> stations, string tag, string key, StringBuilder log)
        {
            var poses = new List<(string name, Vector3 eye, Vector3 look, float fov)>();
            float sp = path.spacing;
            Vector3 P(int i) => path.GetPoint(Mathf.Clamp(i, 1, path.Count - 2));
            // the title's drone (TitleScreen.Drone), mid-move: Ease(0.5) = 0.5
            if (def.bridges != null && def.bridges.Length > 0)
            {
                Vector2 best = def.bridges[0];
                foreach (var b in def.bridges) if (b.y - b.x > best.y - best.x) best = b;
                int i0 = Mathf.Clamp(Mathf.RoundToInt(best.x / sp), 1, path.Count - 2), i1 = Mathf.Clamp(Mathf.RoundToInt(best.y / sp), 1, path.Count - 2);
                int im = (i0 + i1) / 2, n = Mathf.Max(1, i1 - i0);
                Vector3 mid = path.GetPoint(im), along = path.GetTangent(im).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, along).normalized;
                Vector3 valley = GroundAt(mid + right * 80f) <= GroundAt(mid - right * 80f) ? right : -right;
                Vector3 L(Vector3 a, Vector3 b) => Vector3.Lerp(a, b, 0.5f);
                poses.Add(("establish", L(mid + valley * 640f - along * 160f + Vector3.up * 240f, mid + valley * 470f + along * 40f + Vector3.up * 185f),
                           L(mid + Vector3.up * 30f, mid), 50f));
                poses.Add(("pan", L(P(i0 - 30) + valley * 170f + Vector3.up * 95f, P(i1 + 30) + valley * 170f + Vector3.up * 95f),
                           L(P(i0 + n / 6), P(i1 - n / 6)), 50f));
                poses.Add(("descend", L(P(i0 + n / 4) + valley * 110f + Vector3.up * 150f, P(i0 + n / 2) + valley * 40f + Vector3.up * 42f),
                           L(P(i0 + n / 2), P(i0 + (3 * n) / 4) + Vector3.up * 2f), 50f));
            }
            else
            {
                int at = path.Count / 2;
                Vector3 p = path.GetPoint(at), fwd = path.GetTangent(at).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
                Vector3 valley = GroundAt(p + right * 80f) <= GroundAt(p - right * 80f) ? right : -right;
                poses.Add(("high", p + valley * 150f + Vector3.up * 90f - fwd * 60f, p + fwd * 60f, 50f));
            }
            foreach (float m in stations)
            {
                int at = Mathf.Clamp(Mathf.RoundToInt(m / sp), 2, path.Count - 12);
                Vector3 p = path.GetPoint(at), fwd = path.GetTangent(at); fwd.y = 0f; fwd.Normalize();
                string ms = m.ToString("0", CultureInfo.InvariantCulture);
                poses.Add(("chase" + ms, p - fwd * 8f + Vector3.up * 2.6f, p + Vector3.up * 0.8f + fwd * 20f, 0f));
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                float lane = path.roadWidth * 0.25f - 0.37f;
                int ahead = Mathf.Clamp(at + 8, 0, path.Count - 1);
                Vector3 pa = path.GetPoint(ahead), fa = path.GetTangent(ahead); fa.y = 0f; fa.Normalize();
                poses.Add(("cockpit" + ms, p + right * lane + Vector3.up * (PSXRacingBuilder.RoadLift + 1.15f),
                           pa + Vector3.Cross(Vector3.up, fa) * lane + Vector3.up * (PSXRacingBuilder.RoadLift + 0.9f), 0f));
            }
            float keepFov = cam.fieldOfView;
            foreach (var (name, eye0, look, fov) in poses)
            {
                Vector3 eye = eye0;
                if (fov > 0f) eye.y = Mathf.Max(eye.y, GroundAt(eye) + 22f);   // the drone's canopy clearance
                cam.fieldOfView = fov > 0f ? fov : keepFov;
                var rot = Quaternion.LookRotation(look - eye);
                TimeOfDay.Apply(hour, sun);
                DynamicSky.PrimeClouds(eye);
                cam.transform.SetPositionAndRotation(eye, rot);
                SunShadows.RenderFor(cam);
                forest.DrawNow(cam);
                string file = $"pp_{tag}_{key}_{name}";
                PSXScreenshotTool.ShotAs(cam, file, eye, rot);
                log.AppendLine($"  {file}: eye ({eye.x:0},{eye.y:0},{eye.z:0}) yaw {rot.eulerAngles.y:0} pitch {rot.eulerAngles.x:0.0} fov {cam.fieldOfView:0}; forest {ForestTris(forest)}");
            }
            cam.fieldOfView = keepFov;
        }

        /// <summary>The forest's triangles in the last DrawNow's lists, total and the poplar's.</summary>
        static string ForestTris(MeshForest f)
        {
            long total = 0, pop = 0; var top = new List<(long t, string n)>();
            foreach (var line in f.DebugCounts().Split('\n'))
            {
                var kv = line.Split(':');
                if (kv.Length != 2) continue;
                int v = int.Parse(kv[0], CultureInfo.InvariantCulture);
                var c = kv[1].Split(',');
                long t = 0;
                for (int l = 0; l < 4 && l < c.Length; l++) t += long.Parse(c[l], CultureInfo.InvariantCulture) * Tris(f.variants[v].lods[l]);
                total += t;
                if (Name(f.variants[v]).ToLower().Contains("poplar")) pop += t;
                top.Add((t, Name(f.variants[v])));
            }
            top.Sort((a, b) => b.t.CompareTo(a.t));
            var sb = new StringBuilder($"{total / 1e6:0.00} M tris (poplar {pop / 1e6:0.00} M); top:");
            for (int i = 0; i < Mathf.Min(4, top.Count); i++) sb.Append($" {top[i].n} {top[i].t / 1e6:0.00}");
            return sb.ToString();
        }
    }
}
