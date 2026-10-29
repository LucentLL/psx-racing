using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE FLICKER FINDER (city-popin, 2026-10-28; owner: "textures flicker
    /// as they fight for layering", and 2026-10-01 "I regularly drive over
    /// texture pop in from overlap"). At spots along the race routes (the
    /// Tryon Sprint's first kilometre from South End toward NoDa - the owner's
    /// frames -, the Uptown Loop, I-277, Independence) and at three distances,
    /// every draw is rendered in a flat ID of its own (PSX/Lit's _PSXIdOn; a
    /// renderer on any other shader wears Hidden/PSX/FlickerId with its
    /// material's depth state), twice, with nothing moved but the camera's
    /// NEAR PLANE (x 1.01): the picture's x and y are the same to the bit, so
    /// every pixel whose ID changes is a depth tie - two surfaces fighting -
    /// and the two IDs say which. (A 2 cm nudge is rendered too, for the
    /// count the owner sees in motion; its edges move, so it is not ranked.)
    /// Run under -force-glcore (tools\city-flicker.ps1): the game is WebGL,
    /// OpenGL's 24-bit depth with no reversed Z, and D3D's float depth hides
    /// most of its ties. Chase near plane and cockpit near plane both.
    ///
    /// Writes Docs/CITY_FLICKER_TAG.md (ranked: surface A vs B, pixels, the
    /// spot, the distance) and, with PSX_FLICK_SHOTS=1, the pop-in frames: a
    /// street seen from 600 m and 300 m with the far buildings on and off.
    /// PSX_FLICK_PROXIES=0 runs the finder with the far buildings hidden.
    /// </summary>
    public static class CityFlickerFinder
    {
        const int W = 960, H = 540;
        static readonly float[] Backs = { 0f, 150f, 400f };
        const float EyeUp = 2.0f;

        struct Spot { public string name; public Vector2 at; public Vector2 dir; public float y; }

        sealed class Pair { public string a, b; public int px; public double distSum; public readonly Dictionary<string, int> spots = new Dictionary<string, int>(); }

        public static void Run()
        {
            var def = System.Array.Find(TrackCatalog.All, d => d.id == "Charlotte");
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out var player)) { Debug.LogError("[Flicker] Charlotte.unity did not open"); return; }
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[Flicker] no city data"); return; }
            PSXRacingBuilder.EnsureCityTextures();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include)) c.enabled = false;
            if (player != null) player.SetActive(false);
            string tag = System.Environment.GetEnvironmentVariable("PSX_FLICK_TAG");
            if (string.IsNullOrEmpty(tag)) tag = "run";
            // PSX_FLICK_PROXIES: 1 on, 0 off, both = each view measured both ways (two reports)
            string pmode = System.Environment.GetEnvironmentVariable("PSX_FLICK_PROXIES") ?? "1";
            bool proxiesOn = pmode != "0";
            var modes = pmode == "both" ? new[] { false, true } : new[] { proxiesOn };
            bool shots = System.Environment.GetEnvironmentVariable("PSX_FLICK_SHOTS") == "1";
            var only = new HashSet<string>((System.Environment.GetEnvironmentVariable("PSX_FLICK_SPOTS") ?? "").Split(','));
            only.Remove("");
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            int oldWeather = RaceHandoff.WeatherOverride;
            RaceHandoff.WeatherOverride = 0;
            int day = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals("day", System.StringComparison.OrdinalIgnoreCase));
            if (day < 0) day = System.Array.FindIndex(TimeOfDay.All, t => t.name.ToLowerInvariant().Contains("noon"));
            if (day < 0) day = 0;
            var go = new GameObject("~flickerWorld");
            var world = go.AddComponent<CityWorld>();
            string root = Directory.GetParent(Application.dataPath).FullName;
            string shotDir = Path.Combine(root, "Screenshots");
            Directory.CreateDirectory(shotDir);
            int M = modes.Length;
            var pairsM = new Dictionary<string, Pair>[M];
            var perSpotM = new StringBuilder[M];
            var totalM = new long[M]; var nudgeM = new long[M]; var pxM = new long[M];
            for (int mi = 0; mi < M; mi++) { pairsM[mi] = new Dictionary<string, Pair>(); perSpotM[mi] = new StringBuilder(); }
            float baseNear = cam.nearClipPlane;
            var log = new StringBuilder();
            try
            {
                TimeOfDay.Apply(day, sun);
                NightGlow.PreviewAll(false);
                if (globals != null) globals.Apply();
                CityProxies.Suppress = !proxiesOn;
                var spots = Spots(map);
                log.Append($"[Flicker] {tag}: {spots.Count} spots, near chase {baseNear:0.###} / cockpit {ChaseCamera.CockpitNearClip:0.###}, far {cam.farClipPlane:0}, proxies {(proxiesOn ? "on" : "off")}, device {SystemInfo.graphicsDeviceType}\n");
                foreach (var sp in spots)
                {
                    if (only.Count > 0 && !only.Contains(sp.name)) continue;
                    foreach (float back in Backs)
                    try
                    {
                        var at2 = sp.at - sp.dir * back;
                        var eye = new Vector3(at2.x, sp.y + EyeUp, at2.y);
                        var aim = new Vector3(sp.at.x + sp.dir.x * 60f, sp.y + EyeUp, sp.at.y + sp.dir.y * 60f);
                        var rot = Quaternion.LookRotation(aim - eye);
                        world.EnsureRing(eye, world.ring);
                        cam.transform.SetPositionAndRotation(eye, rot);
                        world.RefreshSkyline(cam);
                        foreach (var near in new[] { baseNear, ChaseCamera.CockpitNearClip })
                        {
                            string view = $"{sp.name}@{back:0}m/{(near == baseNear ? "chase" : "cockpit")}";
                            for (int mi = 0; mi < M; mi++)
                            {
                            CityProxies.Suppress = !modes[mi];
                            world.RefreshSkyline(cam);
                            var r = Measure(cam, world, near, eye, rot, view, pairsM[mi], out int nudge, out int drawn);
                            totalM[mi] += r; nudgeM[mi] += nudge; pxM[mi] += drawn;
                            perSpotM[mi].Append($"| {view} | {r} | {nudge} | {drawn} |\n");
                            }
                        }
                    }
                    catch (System.Exception ex) { Debug.LogException(ex); log.Append($"[Flicker] {sp.name}@{back:0}m THREW {ex.Message}\n"); }
                    world.DropAll();
                }
                // the pop-in frames: a Tryon street from 600 m and 300 m, far buildings on and off
                if (shots && spots.Count > 1)
                {
                    var sp = spots.Find(s => s.name == "tryon2");
                    if (sp.name == null) sp = spots[1];
                    foreach (float back in new[] { 600f, 300f })
                    {
                        var at2 = sp.at - sp.dir * back;
                        var eye = new Vector3(at2.x, sp.y + EyeUp, at2.y);
                        var aim = new Vector3(sp.at.x + sp.dir.x * 60f, sp.y + EyeUp + 8f, sp.at.y + sp.dir.y * 60f);
                        var rot = Quaternion.LookRotation(aim - eye);
                        world.EnsureRing(eye, world.ring);
                        foreach (bool on in new[] { true, false })
                        {
                            CityProxies.Suppress = !on;
                            cam.transform.SetPositionAndRotation(eye, rot);
                            world.RefreshSkyline(cam);
                            string name = $"popin_{sp.name}_{back:0}m_{(on ? "proxies" : "noproxies")}_{tag}";
                            PSXScreenshotTool.ShotAs(cam, name, eye, rot);
                            log.Append($"[Flicker] shot {name}.png ({world.Proxies?.Summary ?? "no proxies"})\n");
                        }
                        world.DropAll();
                    }
                    CityProxies.Suppress = !proxiesOn;
                }
                if (world.Proxies != null) log.Append("[Flicker] " + world.Proxies.Summary + "\n");
            }
            catch (System.Exception ex) { Debug.LogException(ex); log.Append("[Flicker] THREW " + ex.Message + "\n"); }
            finally
            {
                CityProxies.Suppress = false;
                RaceHandoff.WeatherOverride = oldWeather;
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
            }
            for (int mi = 0; mi < M; mi++)
            {
                string mtag = M > 1 ? tag + (modes[mi] ? "_on" : "_off") : tag;
                Report(mtag, modes[mi], pairsM[mi], perSpotM[mi], totalM[mi], nudgeM[mi], pxM[mi], root);
                log.Append($"[Flicker] {mtag}: total {totalM[mi]} tie px of {pxM[mi]} drawn, nudge {nudgeM[mi]}, {pairsM[mi].Count} pairs\n");
            }
            Debug.Log(log.ToString() + $"[Flicker] DONE {tag}");
        }

        static void Report(string tag, bool proxiesOn, Dictionary<string, Pair> pairs, StringBuilder perSpot, long total, long totalNudge, long totalPx, string root)
        {
            // the ranked list
            var list = new List<Pair>(pairs.Values);
            list.Sort((x, y) => y.px.CompareTo(x.px));
            var md = new StringBuilder();
            md.Append($"# City flicker finder - {tag} ({System.DateTime.Now:yyyy-MM-dd HH:mm})\n\n");
            md.Append($"Device {SystemInfo.graphicsDeviceType}; {W}x{H}; near-plane x1.01 flips (depth ties) per view; proxies {(proxiesOn ? "on" : "off")}.\n\n");
            md.Append($"**TOTAL depth-tie pixels: {total}** of {totalPx} drawn ({(totalPx > 0 ? 1e6 * total / totalPx : 0):0} ppm); 2 cm nudge changed {totalNudge} (edges included).\n\n");
            md.Append("## Ranked fights (surface A vs surface B)\n\n| # | A | B | px | mean dist m | worst spots |\n|---|---|---|---|---|---|\n");
            for (int k = 0; k < list.Count && k < 60; k++)
            {
                var p = list[k];
                var sl = new List<KeyValuePair<string, int>>(p.spots);
                sl.Sort((x, y) => y.Value.CompareTo(x.Value));
                var sb = new StringBuilder();
                for (int j = 0; j < sl.Count && j < 3; j++) sb.Append((j > 0 ? ", " : "") + sl[j].Key + " " + sl[j].Value);
                md.Append($"| {k + 1} | {p.a} | {p.b} | {p.px} | {(p.px > 0 ? p.distSum / p.px : 0):0} | {sb} |\n");
            }
            md.Append("\n## Per view\n\n| view | tie px | nudge px | drawn px |\n|---|---|---|---|\n").Append(perSpot);
            string docs = Path.Combine(root, "Docs");
            Directory.CreateDirectory(docs);
            File.WriteAllText(Path.Combine(docs, $"CITY_FLICKER_{tag}.md"), md.ToString());
        }

        // ------------------------------------------------------------------
        //  THE SPOTS
        // ------------------------------------------------------------------
        static List<Spot> Spots(CityMap map)
        {
            var spots = new List<Spot>();
            void FromRoute(string id, string prefix, float[] arcs, bool fromStart)
            {
                var rt = map.RouteById(id);
                if (rt == null) { Debug.LogWarning("[Flicker] no route " + id); return; }
                for (int k = 0; k < arcs.Length; k++)
                {
                    float s = (fromStart ? rt.startM : 0f) + arcs[k] * (fromStart ? 1f : rt.lengthM);
                    if (RoutePoint(map, rt, s, out var p, out var d, out float y))
                        spots.Add(new Spot { name = prefix + k, at = p, dir = d, y = y });
                }
            }
            // the Tryon Sprint's first ~25 s from its start in South End
            FromRoute("tryon", "tryon", new[] { 0f, 300f, 650f, 1000f }, true);
            FromRoute("uptown", "uptown", new[] { 0.05f, 0.30f, 0.55f, 0.80f }, false);
            FromRoute("independence", "indep", new[] { 300f, 1400f }, true);
            // I-277, either side of uptown
            foreach (int side in new[] { -1, 1 })
            {
                float best = float.MaxValue; int bei = -1; float bs = 0f;
                for (int ei = 0; ei < map.edges.Length; ei++)
                {
                    var e = map.edges[ei];
                    if (e.link || e.name != "I-277") continue;
                    for (float s = 0f; s <= e.length; s += 10f)
                    {
                        var q = e.PointAt(s);
                        if (Mathf.Sign(q.y - map.uptown.y) != side) continue;
                        float dd = Mathf.Abs(Vector2.Distance(q, map.uptown) - 900f);
                        if (dd < best) { best = dd; bei = ei; bs = s; }
                    }
                }
                if (bei < 0) continue;
                var be = map.edges[bei];
                var a = be.PointAt(Mathf.Max(bs - 5f, 0f)); var b = be.PointAt(Mathf.Min(bs + 5f, be.length));
                spots.Add(new Spot { name = side < 0 ? "i277s" : "i277n", at = be.PointAt(bs), dir = (b - a).normalized, y = be.YAt(bs) });
            }
            return spots;
        }

        /// <summary>The point <paramref name="s"/> metres along a route's chain
        /// (a loop wraps), its heading and its road height.</summary>
        static bool RoutePoint(CityMap map, CityMap.Route rt, float s, out Vector2 p, out Vector2 dir, out float y)
        {
            p = default; dir = Vector2.up; y = 0f;
            float total = 0f;
            foreach (int ei in rt.edges) total += map.edges[ei].length;
            if (total <= 1f) return false;
            if (rt.loop) s = Mathf.Repeat(s, total); else s = Mathf.Clamp(s, 0f, total - 1f);
            for (int k = 0; k < rt.edges.Length; k++)
            {
                var e = map.edges[rt.edges[k]];
                if (s > e.length) { s -= e.length; continue; }
                bool fwd = rt.dirs[k] >= 0;
                float at = fwd ? s : e.length - s;
                p = e.PointAt(at);
                var a = e.PointAt(Mathf.Clamp(at - 4f, 0f, e.length)); var b = e.PointAt(Mathf.Clamp(at + 4f, 0f, e.length));
                dir = ((b - a) * (fwd ? 1f : -1f)).normalized;
                y = e.YAt(at);
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        //  THE ID RENDERS
        // ------------------------------------------------------------------
        static readonly string[] Glows = { "PSX/Halo", "PSX/Glow", "PSX/Beam", "PSX/LampCone", "PSX/Sky", "PSX/Rain", "PSX/SunGlare", "PSX/Lens", "PSX/CarLens", "PSX/SpeedBlur", "PSX/ZoneLine", "Hidden/PSX/VolClouds", "PSX/Shadow" };
        static readonly int IdOnId = Shader.PropertyToID("_PSXIdOn"), IdColId = Shader.PropertyToID("_PSXIdCol");

        static int Measure(Camera cam, CityWorld world, float near, Vector3 eye, Quaternion rot, string view,
                           Dictionary<string, Pair> pairs, out int nudged, out int drawn)
        {
            var labels = new List<string> { "(sky)" };
            var swapped = new List<(Renderer r, Material[] mats)>();
            var off = new List<Renderer>();
            var touched = new List<(Renderer r, int n)>();
            var idShader = Shader.Find("Hidden/PSX/FlickerId");
            var made = new List<Material>();
            var mpb = new MaterialPropertyBlock();
            var rb = new MaterialPropertyBlock();
            var keepClear = cam.clearFlags; var keepBg = cam.backgroundColor; var keepNear = cam.nearClipPlane;
            bool keepHdr = cam.allowHDR, keepMsaa = cam.allowMSAA;
            var acd = cam.GetComponent<UniversalAdditionalCameraData>();
            bool keepPost = acd != null && acd.renderPostProcessing;
            var features = new List<(ScriptableRendererFeature f, bool on)>();
            foreach (var path in new[] { "Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset" })
            {
                var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if (data == null) continue;
                foreach (var f in data.rendererFeatures) if (f != null) { features.Add((f, f.isActive)); f.SetActive(false); }
            }
            nudged = 0; drawn = 0;
            int ties = 0;
            try
            {
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) { if (r.enabled && !r.forceRenderingOff) { r.forceRenderingOff = true; off.Add(r); } continue; }
                    if (!r.enabled || r.forceRenderingOff || !r.gameObject.activeInHierarchy) continue;
                    var mats = r.sharedMaterials;
                    bool anyGlow = false;
                    foreach (var m in mats) if (m != null && m.shader != null && System.Array.IndexOf(Glows, m.shader.name) >= 0) anyGlow = true;
                    if (anyGlow) { r.forceRenderingOff = true; off.Add(r); continue; }
                    r.GetPropertyBlock(rb);
                    float mode = rb.GetFloat(CityTileFade.ModeId);
                    Material[] repl = null;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        int id = labels.Count;
                        labels.Add(Label(r, m));
                        if (m != null && m.shader != null && m.shader.name != "PSX/Lit")
                        {
                            repl ??= (Material[])mats.Clone();
                            var im = new Material(idShader) { hideFlags = HideFlags.HideAndDontSave };
                            bool decal = m.shader.name == "PSX/Decal";
                            im.SetFloat("_IdOffF", decal ? -1f : 0f);
                            im.SetFloat("_IdOffU", decal ? -1f : 0f);
                            im.SetFloat("_IdZWrite", decal || m.renderQueue >= 2500 ? 0f : 1f);
                            im.SetFloat("_IdCull", m.HasProperty("_Cull") ? m.GetFloat("_Cull") : 2f);
                            im.renderQueue = m.renderQueue;
                            repl[i] = im;
                            made.Add(im);
                        }
                        mpb.Clear();
                        mpb.SetColor(IdColId, Encode(id));
                        if (mode > 0.5f) mpb.SetFloat(CityTileFade.ModeId, mode);
                        r.SetPropertyBlock(mpb, i);
                    }
                    touched.Add((r, mats.Length));
                    if (repl != null) { swapped.Add((r, mats)); r.sharedMaterials = repl; }
                }
                Shader.SetGlobalFloat(IdOnId, 1f);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.clear;
                cam.allowHDR = false; cam.allowMSAA = false;
                if (acd != null) acd.renderPostProcessing = false;
                var a = Render(cam, near, eye, rot, world);
                var b = Render(cam, near * 1.01f, eye, rot, world);
                var c = Render(cam, near, eye + rot * Vector3.right * 0.02f, rot, world);
                for (int k = 0; k < a.Length; k++)
                {
                    int ia = Decode(a[k]), ib = Decode(b[k]), ic = Decode(c[k]);
                    if (ia != 0) drawn++;
                    if (ia != ic) nudged++;
                    if (ia == ib) continue;
                    ties++;
                    int lo = Mathf.Min(ia, ib), hi = Mathf.Max(ia, ib);
                    string la = lo < labels.Count ? labels[lo] : "#" + lo, lb = hi < labels.Count ? labels[hi] : "#" + hi;
                    if (string.CompareOrdinal(la, lb) > 0) (la, lb) = (lb, la);
                    string key = la + " | " + lb;
                    if (!pairs.TryGetValue(key, out var p)) { p = new Pair { a = la, b = lb }; pairs[key] = p; }
                    p.px++;
                    p.distSum += Mathf.Max(a[k].a, b[k].a) / 255.0 * 2000.0;
                    p.spots[view] = p.spots.TryGetValue(view, out int n0) ? n0 + 1 : 1;
                }
            }
            finally
            {
                Shader.SetGlobalFloat(IdOnId, 0f);
                var none = new MaterialPropertyBlock();
                foreach (var (r, n) in touched) if (r != null) for (int i = 0; i < n; i++) { try { r.SetPropertyBlock(null, i); } catch (System.Exception) { r.SetPropertyBlock(none, i); } }
                foreach (var (r, mats) in swapped) if (r != null) r.sharedMaterials = mats;
                foreach (var r in off) if (r != null) r.forceRenderingOff = false;
                foreach (var m in made) Object.DestroyImmediate(m);
                foreach (var (f, on) in features) f.SetActive(on);
                cam.clearFlags = keepClear; cam.backgroundColor = keepBg; cam.nearClipPlane = keepNear;
                cam.allowHDR = keepHdr; cam.allowMSAA = keepMsaa;
                if (acd != null) acd.renderPostProcessing = keepPost;
            }
            return ties;
        }

        static Color32[] Render(Camera cam, float near, Vector3 eye, Quaternion rot, CityWorld world)
        {
            cam.nearClipPlane = near;
            cam.transform.SetPositionAndRotation(eye, rot);
            world.RefreshSkyline(cam);
            StreetLights.Push(cam.transform.position, cam.transform.forward);
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point, antiAliasing = 1 };
            rt.Create();
            RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt });
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            rt.Release(); Object.DestroyImmediate(rt);
            return px;
        }

        static Color Encode(int id) => new Color((id & 255) / 255f, ((id >> 8) & 255) / 255f, ((id >> 16) & 255) / 255f, 1f);
        static int Decode(Color32 c) => c.r | (c.g << 8) | (c.b << 16);

        /// <summary>A draw's class, the same in every tile: the object's own
        /// name (a tile's child: Roads, Buildings, Kerbs...; a prop: its model)
        /// and the material's.</summary>
        static string Label(Renderer r, Material m)
        {
            var t = r.transform;
            string n = t.name;
            if (n.StartsWith("Tile_")) n = "TileRoot";
            else if (t.parent != null && !t.parent.name.StartsWith("Tile_") && t.parent.parent != null && t.parent.parent.name.StartsWith("Tile_")) n = t.parent.name + "/" + n;
            n = System.Text.RegularExpressions.Regex.Replace(n, @"\(Clone\)|\s*\(\d+\)", "");
            string mn = m != null ? m.name.Replace(" (Instance)", "") : "null";
            return n + " [" + mn + "]";
        }
    }
}
