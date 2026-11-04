using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE COURSES FROM ABOVE (owner, 2026-11-04: "Selecting/Previewing track
    /// should display the actual top-down view of the map", Need for Speed's
    /// map as the reference). Every venue's square (RouteGeo.Frame) is
    /// photographed straight down out of the game's own world at noon, with no
    /// fog: a stage's scene as it was built, Charlotte stood up tile by tile
    /// (CityWorld, as play stands it) under a chunked camera. One JPG a
    /// picture key into StreamingAssets/TrackMaps - files beside the player,
    /// fetched by a card when it opens (RouteMap), so the web player's data
    /// file does not grow - and each picture's square into
    /// Resources/TrackMapFrames.txt, so a reverse twin or a sprint drawn in
    /// its loop's picture is drawn in register with it.
    ///
    /// Run it in a sandbox whose scenes and city data are the shipped ones
    /// (the publish sandbox), again after a stage or a route changes. Menu:
    /// PSX Racing/Photograph Track Maps; batch (it RENDERS, so not
    /// -nographics): -executeMethod PSXRacing.EditorTools.TrackMapShots.Run.
    /// PSX_TRACKMAPS=key,key shoots only those; the report is
    /// PSXRacing_trackmaps.txt at the project root.
    /// </summary>
    public static class TrackMapShots
    {
        public const int Size = 1024;
        const string OutDir = "Assets/StreamingAssets/" + RouteMap.PhotoFolder;
        const string FramesPath = "Assets/PSXRacing/Resources/" + RouteMap.FramesResource + ".txt";
        /// <summary>Rendered at this many times the picture's pixels and
        /// averaged down: a 3 m pixel would otherwise stair-step every road,
        /// and a stage's crowns and the sea's sparkle are finer than one.
        /// The city's chunks are each a small square already.</summary>
        const int CitySupersample = 2, StageSupersample = 4;
        /// <summary>The most metres one city chunk's camera covers (its
        /// tiles stand up as a ring round it, kept until the route is done).</summary>
        const float CityChunkM = 1200f;
        /// <summary>A city square wider than this is not photographed (it
        /// keeps its street picture): tens of thousands of tiles.</summary>
        const float CityMaxSpanM = 9000f;
        const int JpgQuality = 84;
        /// <summary>The camera's height. High enough that every pixel looks
        /// straight down: at 5 km the shaders' view rays fanned out twenty
        /// degrees across a stage's square, and the sea threw the noon sun
        /// back at the camera as a white blaze (Emerald Isle).</summary>
        const float EyeY = 60000f;
        static readonly Color Ground = new Color(0.050f, 0.056f, 0.072f);

        class Job
        {
            public string key, scene, venues;
            public bool city;
            public Vector2 lo, hi;
            public Vector2 Centre => (lo + hi) * 0.5f;
            public float Span => Mathf.Max(hi.x - lo.x, hi.y - lo.y);
        }

        [MenuItem("PSX Racing/Photograph Track Maps")]
        public static void Run()
        {
            var report = new StringBuilder();
            var was = Edition.Simulated;
            Edition.Simulate(EditionKind.All);
            List<Job> jobs;
            try { jobs = Jobs(report); }
            finally { Edition.Simulate(was); }
            var only = Env("PSX_TRACKMAPS");
            if (only != null) jobs.RemoveAll(j => System.Array.IndexOf(only.Split(','), j.key) < 0);
            // PSX_TRACKMAPS_KIND=city|stage: one half (the stages want a
            // sandbox whose scenes carry the PC pass's crowns - the forest
            // seen from above; the city wants the shipped city data)
            var kind = Env("PSX_TRACKMAPS_KIND");
            if (kind == "city") jobs.RemoveAll(j => !j.city);
            else if (kind == "stage") jobs.RemoveAll(j => j.city);

            Directory.CreateDirectory(OutDir);
            var frames = ReadFrames();
            int shot = 0;
            var city = jobs.FindAll(j => j.city);
            if (city.Count > 0) shot += ShootCity(city, frames, report);
            foreach (var j in jobs)
                if (!j.city && ShootStage(j, frames, report)) shot++;
            WriteFrames(frames);
            AssetDatabase.Refresh();
            report.Insert(0, $"[TrackMapShots] {shot} of {jobs.Count} pictures into {OutDir}\n");
            File.WriteAllText(Path.Combine(ProjectDir, "PSXRacing_trackmaps.txt"), report.ToString());
            Debug.Log(report.ToString());
        }

        static string ProjectDir => Path.GetDirectoryName(Application.dataPath);

        static string Env(string name)
        {
            string v = System.Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(v) ? null : v.Trim();
        }

        /// <summary>One job a picture key: the union of the squares of every
        /// venue drawn in it.</summary>
        static List<Job> Jobs(StringBuilder report)
        {
            var byKey = new Dictionary<string, Job>();
            var list = new List<Job>();
            for (int i = 0; i < TrackCatalog.Count; i++)
            {
                var def = TrackCatalog.At(i);
                string key = RouteGeo.PhotoKey(def);
                if (key == null) continue;
                var pts = TrackCatalog.Sample(def, TrackCatalog.Spacing);
                if (pts.Count < 4) { report.AppendLine($"  {def.id}: no centreline in this build - skipped"); continue; }
                RouteGeo.Frame(def, pts, out var c, out float span);
                var lo = c - Vector2.one * (span * 0.5f);
                var hi = c + Vector2.one * (span * 0.5f);
                if (byKey.TryGetValue(key, out var j))
                {
                    j.lo = Vector2.Min(j.lo, lo); j.hi = Vector2.Max(j.hi, hi);
                    j.venues += " " + def.id;
                    continue;
                }
                j = new Job { key = key, scene = TrackCatalog.SceneIdOf(i), city = def.IsCityRace, lo = lo, hi = hi, venues = def.id };
                byKey[key] = j;
                list.Add(j);
            }
            // a union of squares is a rectangle: square it about its centre
            foreach (var j in list)
            {
                var c = j.Centre; float s = j.Span;
                j.lo = c - Vector2.one * (s * 0.5f); j.hi = c + Vector2.one * (s * 0.5f);
            }
            return list;
        }

        // ---- light ------------------------------------------------------

        /// <summary>Noon as the game lights it, then what a map does not
        /// want taken off: the fog, the far haze, the PS1 vertex snap, a wet
        /// road's sheen.</summary>
        static void Light(PSXGlobals globals, Light sun)
        {
            TimeOfDay.Apply(TimeOfDay.Noon, sun);
            globals.Apply();
            Shader.SetGlobalFloat("_PSXFogNear", 1e6f);
            Shader.SetGlobalFloat("_PSXFogFar", 2e6f);
            Shader.SetGlobalVector("_PSXDistHaze", Vector4.zero);
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalFloat("_PSXWetness", 0f);
        }

        // ---- the camera ---------------------------------------------------

        /// <summary>One square straight down, north up, into
        /// <paramref name="into"/> at (x0, y0), px a side.</summary>
        static void Render(Texture2D into, int x0, int y0, int px, Vector2 centre, float half, int ss)
        {
            int big = px * ss;
            var camGO = new GameObject("~trackMapCam");
            var cam = camGO.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(new Vector3(centre.x, EyeY, centre.y),
                                                 Quaternion.LookRotation(Vector3.down, Vector3.forward));
            cam.orthographic = true;
            cam.orthographicSize = half;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Ground;
            cam.nearClipPlane = 100f;
            cam.farClipPlane = EyeY + 5000f;
            var rt = new RenderTexture(big, big, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tmp = new Texture2D(big, big, TextureFormat.RGB24, false);
            tmp.ReadPixels(new Rect(0, 0, big, big), 0, 0);
            tmp.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            var src = tmp.GetPixels32();
            var dst = new Color32[px * px];
            int n = ss * ss;
            for (int y = 0; y < px; y++)
                for (int x = 0; x < px; x++)
                {
                    int r = 0, g = 0, b = 0;
                    for (int sy = 0; sy < ss; sy++)
                        for (int sx = 0; sx < ss; sx++)
                        {
                            var c = src[(y * ss + sy) * big + x * ss + sx];
                            r += c.r; g += c.g; b += c.b;
                        }
                    dst[y * px + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
                }
            into.SetPixels32(x0, y0, px, px, dst);
            Object.DestroyImmediate(tmp);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGO);
        }

        static void Save(Job j, Texture2D tex, Dictionary<string, Vector3> frames, StringBuilder report, string note)
        {
            // Where the square runs past the built world (a stage's terrain
            // ends, the city's last tiles) the camera saw only its clear
            // colour. A flat colour there read as a slab across the corner
            // (Beech Gap, 9% of its square), so the picture beside the edge
            // is mirrored across it - down each column, then along each row
            // for what the columns could not reach - and only what neither
            // reaches takes the picture's mean, a shade darker.
            var px = tex.GetPixels32();
            Color32 bg = Ground;
            bool Clear(Color32 c) => Mathf.Abs(c.r - bg.r) <= 2 && Mathf.Abs(c.g - bg.g) <= 2 && Mathf.Abs(c.b - bg.b) <= 2;
            var hole = new bool[px.Length];
            long r = 0, g = 0, b = 0, n = 0;
            for (int i = 0; i < px.Length; i++)
            {
                hole[i] = Clear(px[i]);
                if (hole[i]) continue;
                r += px[i].r; g += px[i].g; b += px[i].b; n++;
            }
            int filled = (int)(px.Length - n);
            if (filled > 0 && n > 0)
            {
                Mirror(px, hole, Size, columns: true);
                Mirror(px, hole, Size, columns: false);
                var mean = new Color32((byte)(r * 85 / (n * 100)), (byte)(g * 85 / (n * 100)), (byte)(b * 85 / (n * 100)), 255);
                for (int i = 0; i < px.Length; i++) if (hole[i]) px[i] = mean;
                tex.SetPixels32(px);
            }
            if (filled > 0) note += $", {filled * 100 / px.Length}% past the built world filled";
            tex.Apply();
            var bytes = tex.EncodeToJPG(JpgQuality);
            File.WriteAllBytes(Path.Combine(OutDir, j.key + ".jpg"), bytes);
            frames[j.key] = new Vector3(j.Centre.x, j.Centre.y, j.Span);
            report.AppendLine($"  {j.key}: {j.Span:0} m square at ({j.Centre.x:0}, {j.Centre.y:0}), {bytes.Length / 1024} KB, {note}  [{j.venues}]");
        }

        /// <summary>Each run of holes along a column (or a row) that touches
        /// seen picture takes that picture mirrored across its edge, as far
        /// as the seen side reaches; filled pixels stop being holes.</summary>
        static void Mirror(Color32[] px, bool[] hole, int size, bool columns)
        {
            int At(int line, int k) => columns ? k * size + line : line * size + k;
            for (int line = 0; line < size; line++)
            {
                int k = 0;
                while (k < size)
                {
                    if (!hole[At(line, k)]) { k++; continue; }
                    int a = k;
                    while (k < size && hole[At(line, k)]) k++;
                    int bEnd = k;   // holes [a, bEnd)
                    for (int h = a; h < bEnd; h++)
                    {
                        int s = -1;
                        if (a > 0) { int m = 2 * a - 1 - h; if (m >= 0 && !hole[At(line, m)]) s = m; }
                        if (s < 0 && bEnd < size) { int m = 2 * bEnd - 1 - h; if (m < size && !hole[At(line, m)]) s = m; }
                        if (s < 0) continue;
                        px[At(line, h)] = px[At(line, s)];
                    }
                    for (int h = a; h < bEnd; h++)
                    {
                        // only what found a source stops being a hole
                        int m1 = a > 0 ? 2 * a - 1 - h : -1, m2 = bEnd < size ? 2 * bEnd - 1 - h : size;
                        bool got = (m1 >= 0 && !hole[At(line, m1)]) || (m2 < size && !hole[At(line, m2)]);
                        if (got) hole[At(line, h)] = false;
                    }
                }
            }
        }

        // ---- Charlotte -------------------------------------------------------

        static int ShootCity(List<Job> jobs, Dictionary<string, Vector3> frames, StringBuilder report)
        {
            PSXRacingBuilder.EnsureCityTextures();
            if (CityMap.Get() == null) { report.AppendLine("  no city data: the city's races keep their street pictures"); return 0; }
            // a scratch scene: nothing of the open one gets into the city's pictures
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sunGo = new GameObject("~trackMapSun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            var globals = sunGo.AddComponent<PSXGlobals>();
            globals.sun = sun;
            var go = new GameObject("~trackMapCity");
            var world = go.AddComponent<CityWorld>();
            int shot = 0;
            try
            {
                foreach (var j in jobs)
                {
                    if (j.Span > CityMaxSpanM) { report.AppendLine($"  {j.key}: {j.Span:0} m square - too wide to stand up, keeps its street picture"); continue; }
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    // chunks a side: a power of two, so each has whole pixels
                    int g = 1;
                    while (j.Span / g > CityChunkM && g < 16) g *= 2;
                    float sub = j.Span / g;
                    int px = Size / g;
                    int ring = Mathf.CeilToInt(sub * 0.5f / CityMeshes.TileSize) + 1;
                    var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
                    for (int iz = 0; iz < g; iz++)
                        for (int ix = 0; ix < g; ix++)
                        {
                            var c = new Vector2(j.lo.x + (ix + 0.5f) * sub, j.lo.y + (iz + 0.5f) * sub);
                            world.EnsureRing(new Vector3(c.x, 0f, c.y), ring);
                            Light(globals, sun);
                            Render(tex, ix * px, iz * px, px, c, sub * 0.5f, CitySupersample);
                        }
                    world.DropAll();
                    Save(j, tex, frames, report, $"{g}x{g} chunks, {clock.Elapsed.TotalSeconds:0} s");
                    Object.DestroyImmediate(tex);
                    shot++;
                }
            }
            finally
            {
                world.DropAll();
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(sunGo);
            }
            return shot;
        }

        // ---- the stages ------------------------------------------------------

        static bool ShootStage(Job j, Dictionary<string, Vector3> frames, StringBuilder report)
        {
            string path = "Assets/PSXRacing/Scenes/" + j.scene + ".unity";
            if (!File.Exists(Path.Combine(ProjectDir, path))) { report.AppendLine($"  {j.key}: no scene {path} - skipped"); return false; }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var globals = Object.FindAnyObjectByType<PSXGlobals>();
            var sun = globals != null ? globals.sun : null;
            GameObject made = null;
            if (globals == null || sun == null)
            {
                made = new GameObject("~trackMapSun");
                sun = made.AddComponent<Light>();
                sun.type = LightType.Directional;
                globals = made.AddComponent<PSXGlobals>();
                globals.sun = sun;
            }
            // every tree at its nearest model: a map camera five kilometres up
            // would otherwise pick the last LOD, or none
            var lods = Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var l in lods) l.ForceLOD(0);
            // the sky's own meshes and the weather's particles stay out of a
            // picture taken from above them
            var hidden = new List<Renderer>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                string n = r.gameObject.name;
                if (!r.enabled) continue;
                if (r is ParticleSystemRenderer || n.IndexOf("Sky", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Cloud", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Dome", System.StringComparison.OrdinalIgnoreCase) >= 0)
                { r.enabled = false; hidden.Add(r); }
            }
            try
            {
                Light(globals, sun);
                var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
                Render(tex, 0, 0, Size, j.Centre, j.Span * 0.5f, StageSupersample);
                Save(j, tex, frames, report, $"{lods.Length} LOD groups held at 0, {hidden.Count} sky/weather renderers hidden, {clock.Elapsed.TotalSeconds:0} s");
                Object.DestroyImmediate(tex);
            }
            finally
            {
                foreach (var r in hidden) if (r != null) r.enabled = true;
                foreach (var l in lods) if (l != null) l.ForceLOD(-1);
                if (made != null) Object.DestroyImmediate(made);
            }
            return true;
        }

        // ---- the frames --------------------------------------------------------

        static Dictionary<string, Vector3> ReadFrames()
        {
            var d = new Dictionary<string, Vector3>();
            string p = Path.Combine(ProjectDir, FramesPath);
            if (!File.Exists(p)) return d;
            foreach (var line in File.ReadAllLines(p))
            {
                var f = line.Trim().Split(' ');
                if (f.Length < 4) continue;
                d[f[0]] = new Vector3(float.Parse(f[1], CultureInfo.InvariantCulture),
                                      float.Parse(f[2], CultureInfo.InvariantCulture),
                                      float.Parse(f[3], CultureInfo.InvariantCulture));
            }
            return d;
        }

        static void WriteFrames(Dictionary<string, Vector3> frames)
        {
            var keys = new List<string>(frames.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (var k in keys)
            {
                var f = frames[k];
                sb.Append(k).Append(' ')
                  .Append(f.x.ToString("0.0", CultureInfo.InvariantCulture)).Append(' ')
                  .Append(f.y.ToString("0.0", CultureInfo.InvariantCulture)).Append(' ')
                  .Append(f.z.ToString("0.0", CultureInfo.InvariantCulture)).Append('\n');
            }
            File.WriteAllText(Path.Combine(ProjectDir, FramesPath), sb.ToString());
        }
    }
}
