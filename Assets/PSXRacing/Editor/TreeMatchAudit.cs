using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// A TREE LOOKS THE SAME AT EVERY DISTANCE (owner 2026-11-16: "trees full of
    /// leaves (or different color trees in Fall) pop-in as you approach and then
    /// the tree is leafless"). THE AUDIT: every species of a tree kit (Charlotte's
    /// CityTreeKit, a stage's MeshForest), in every dress (winter, spring,
    /// summer, fall, snow), drawn ALONE through the game's own forest path (a
    /// MeshForest pool, the GPU cull, the faded materials, PSX/Lit) at each
    /// hand-over distance, once as the level this side of the hand-over and once
    /// as the level the other side - the same eye, the same light, the same
    /// plant; MeshForest.LodEye picks the level. Measured off the two pictures:
    ///   LEAF   the tree's drawn area (m2): leaf-on against leaf-off
    ///   SIZE   its height and width
    ///   HUE / SAT / VALUE   its mean colour
    /// A pair FAILS on any (the limits are below). Several plants of each
    /// species at different places: PSX/Lit gives every tree its own shade and,
    /// in fall, its own colour from its place, and both levels must agree on it.
    ///
    ///   -executeMethod PSXRacing.EditorTools.TreeMatchAudit.RunKits
    ///   PSX_TM_TAG=name  PSX_TM_KITS=city,BlueRidge,...  PSX_TM_DRESSES=0,1,2,3,4  PSX_TM_INST=3
    /// Output: Screenshots/treematch/&lt;tag&gt;/tm_&lt;kit&gt;.csv (every pair), tm_&lt;kit&gt;_&lt;dress&gt;_fail.png + .txt
    /// (the failing pairs side by side), tm_summary.txt (the table, the classes).
    /// </summary>
    public static partial class TreeMatchAudit
    {
        // ---- the limits ----
        /// <summary>LEAF: the smaller drawn area over the larger, at least this.</summary>
        public const float AreaRatioMin = 0.65f;
        /// <summary>SIZE: height and width within this share.</summary>
        public const float SizeTol = 0.10f;
        /// <summary>HUE: degrees, where both sides have a colour (saturation over SatFloor).</summary>
        public const float HueTolDeg = 12f, SatFloor = 0.08f, SatTol = 0.15f;
        /// <summary>VALUE: the mean luma within this share.</summary>
        public const float LumaTol = 0.18f;

        const int Cell = 320;
        static readonly Color32 Bg = new Color32(255, 0, 255, 255);
        static readonly string[] PairNames = { "LOD0-LOD1", "LOD1-LOD2", "LOD2-FAR" };

        struct Stat
        {
            public int n, x0, x1, y0, y1;
            public float r, g, b, hue, sat, val, luma, hM, wM, areaM2;
            public bool Any => n > 12;
        }

        sealed class Tile { public Color32[] px; public int size; public Stat st; public float mpp; }

        sealed class PairRow
        {
            public string kit, name, group, dress, pair; public int variant, inst;
            public Stat a, b; public string why; public Tile ta, tb;
            public bool Fail => why.Length > 0;
        }

        static string Env(string k, string d) { var v = System.Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }
        static string F(float v, string f = "0.00") => v.ToString(f, CultureInfo.InvariantCulture);

        public static void RunKits()
        {
            string tag = Env("PSX_TM_TAG", "run");
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "treematch", tag);
            Directory.CreateDirectory(root);
            var summary = new StringBuilder($"TREE MATCH AUDIT ({tag}) {System.DateTime.Now:yyyy-MM-dd HH:mm}; PSX_TREE_MATCH={Env("PSX_TREE_MATCH", "(default)")}\n" +
                $"limits: LEAF area ratio >= {F(AreaRatioMin)}, SIZE (the span of 96 % of the drawn pixels each way) within {F(SizeTol * 100f, "0")} % and 3 px, HUE within {F(HueTolDeg, "0")} deg, SAT within {F(SatTol)}, VALUE (luma) within {F(LumaTol * 100f, "0")} %\n");
            int code = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int keepDay = RaceHandoff.CalendarDay, keepWeather = RaceHandoff.WeatherOverride, keepDetail = ForestDetailPrefs.Override;
            try
            {
                var dresses = new List<int>();
                foreach (var s in Env("PSX_TM_DRESSES", "0,1,2,3,4").Split(',')) if (int.TryParse(s.Trim(), out int d) && d >= 0 && d < Seasons.DressCount) dresses.Add(d);
                int inst = Mathf.Clamp(int.Parse(Env("PSX_TM_INST", "3"), CultureInfo.InvariantCulture), 1, 8);
                ForestDetailPrefs.Override = Mathf.Clamp(int.Parse(Env("PSX_TM_DETAIL", "1"), CultureInfo.InvariantCulture), 0, 2);
                var totals = new List<string>();
                foreach (var kitId in Env("PSX_TM_KITS", "city").Split(','))
                {
                    string id = kitId.Trim();
                    if (id.Length == 0) continue;
                    var rows = AuditKit(id, dresses, inst, root, summary);
                    if (rows == null) { code = 1; continue; }
                    totals.Add(Tally(id, rows, dresses, summary));
                    WriteCsv(Path.Combine(root, "tm_" + id + ".csv"), rows);
                    foreach (int d in dresses) WriteFailSheet(root, id, d, rows);
                }
                summary.Append("\nTOTALS (failing species-pairs / audited species-pairs, by kit):\n");
                foreach (var t in totals) summary.Append("  " + t + "\n");
                summary.Append($"\n{clock.Elapsed.TotalSeconds:0} s\nTREE MATCH AUDIT DONE\n");
            }
            catch (System.Exception ex) { summary.Append("TREE MATCH AUDIT FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                MeshForest.LodEyeOn = false;
                ForestDetailPrefs.Override = keepDetail;
                Seasons.Forced = null; Seasons.Invalidate();
                RaceHandoff.WeatherOverride = keepWeather; RaceHandoff.CalendarDay = keepDay;
                File.WriteAllText(Path.Combine(root, "tm_summary.txt"), summary.ToString());
                Debug.Log("[TreeMatchAudit]\n" + summary);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>The scene whose light the kit is drawn under, its camera, and the kit itself.</summary>
        static bool OpenKit(string id, out Camera cam, out MeshForest.Variant[] variants, out string[] names, out string[] groups,
                            out float lod0, out float lod1, out float mesh, out float far, out ComputeShader cull, out float bandShare, StringBuilder log)
        {
            cam = null; variants = null; names = null; groups = null; lod0 = lod1 = mesh = far = 0f; cull = null; bandShare = -1f;
            bool city = id == "city";
            string scene = city ? "Charlotte" : id;
            int ti = TrackCatalog.IndexOf(scene);
            if (ti < 0) { log.Append($"{id}: no such venue\n"); return false; }
            var def = TrackCatalog.At(ti);
            if (!PSXScreenshotTool.Open(def, out cam, out _)) { log.Append($"{id}: scene did not open\n"); return false; }
            if (city)
            {
                CityTreeKit.Reset();
                var kit = CityTreeKit.Get();
                if (kit == null) { log.Append("city: no CityTreeKit in Resources (PC content pass?)\n"); return false; }
                variants = kit.variants; names = kit.names;
                groups = new string[variants.Length];
                for (int v = 0; v < variants.Length; v++)
                    groups[v] = kit.Group3(v) != 0 ? "g3:" + (kit.Group3(v) == CityTreeKit.G3Street ? "Street" : kit.Group3(v) == CityTreeKit.G3Avenue ? "Avenue" : kit.Group3(v) == CityTreeKit.G3Pine ? "Pine" : "Myrtle")
                              : kit.roles[v].ToString();
                lod0 = kit.lod0To; lod1 = kit.lod1To; mesh = kit.meshRange; far = kit.farRange; cull = kit.cull;
                bandShare = CityMeshTrees.LodFade ? CityMeshTrees.LodFadeShare : -1f;
            }
            else
            {
                var f = Object.FindFirstObjectByType<MeshForest>();
                if (f == null) { log.Append($"{id}: the scene has no MeshForest (a PC scene pass?)\n"); return false; }
                variants = f.variants;
                names = new string[variants.Length]; groups = new string[variants.Length];
                for (int v = 0; v < variants.Length; v++)
                {
                    names[v] = variants[v].lods != null && variants[v].lods.Length > 0 && variants[v].lods[0] != null ? variants[v].lods[0].name : "variant" + v;
                    groups[v] = StageGroup(names[v], variants[v]);
                }
                lod0 = f.lod0To; lod1 = f.lod1To; mesh = f.meshRange; far = f.farRange; cull = f.cullShader;
                bandShare = f.bandShare;
            }
            if (cull == null) cull = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PSXRacing/Shaders/PSXForestCull.compute");
            // the light: a clear noon, the same for every kit
            var sun = NightLookShots.FindSun();
            RaceHandoff.CalendarDay = 0;
            RaceHandoff.WeatherOverride = -1;
            Seasons.Forced = null; Seasons.Invalidate();
            var w = Seasons.Now;
            w.fog = false; w.precip = PrecipKind.None; w.precipRate = 0f; w.sky = SkyKind.Clear; w.scenery = SceneryKind.Dry;
            Seasons.Forced = w;
            if (sun != null) TimeOfDay.Apply(TimeOfDay.Noon, sun);
            var globals = Object.FindAnyObjectByType<PSXGlobals>();
            if (globals != null) globals.Apply();
            NightGlow.PreviewAll(false);
            CarLights.PushGlobals();
            return true;
        }

        static string StageGroup(string name, MeshForest.Variant v)
        {
            string n = name.ToLowerInvariant();
            bool imp = v.lods != null && v.lods.Length > 3 && v.lods[3] != null;
            if (n.Contains("poplar")) return "poplar";
            if (n.Contains("beech_tree_00")) return "small beech";
            if (n.Contains("beech_tree")) return imp ? "canopy beech" : "beech (no far level)";
            if (n.Contains("cherry")) return "cherry bush";
            if (n.Contains("maple")) return "maple bush";
            if (n.Contains("grass") || n.Contains("fern") || n.Contains("plant") || n.Contains("shaggy")) return "ground cover";
            return "other";
        }

        static bool Distinct(MeshForest.Variant v, int a, int b) =>
            v.lods != null && v.lods.Length > b && v.lods[a] != null && v.lods[b] != null && v.lods[a] != v.lods[b];

        static List<PairRow> AuditKit(string id, List<int> dresses, int inst, string root, StringBuilder log)
        {
            if (!OpenKit(id, out var cam, out var variants, out var names, out var groups, out float lod0, out float lod1, out float mesh, out float far,
                         out var cull, out float bandShare, log)) return null;
            // THE TREES: every species that stands to the far range, or should (no draw limit of its own). The
            // understory, ground cover and litter have one (maxDist) and thin out under the canopy plant by plant
            // as they near it - small details that fade in, the same material at every level: not audited.
            var pick = new List<int>();
            int thinned = 0;
            for (int v = 0; v < variants.Length; v++)
            {
                var vr = variants[v];
                if (vr == null || vr.lods == null || vr.lods.Length < 3 || vr.lods[0] == null) continue;
                bool hasFar = vr.lods.Length > 3 && vr.lods[3] != null;
                if (!hasFar && (vr.maxDist > 0f || vr.height < 2.5f)) { thinned++; continue; }
                pick.Add(v);
            }
            log.Append($"\n=== {id}: {variants.Length} species; {pick.Count} trees audited; {thinned} plants with a draw limit of their own (understory, ground cover, litter, rock, fence) not audited\n");
            int per = pick.Count * inst;
            if (per == 0) { log.Append($"{id}: no species with two levels\n"); return new List<PairRow>(); }
            int dressNow = (int)Season.Summer;
            var forest = MeshForest.CreatePool(null, variants, 1, per, lod0, lod1, mesh, far, () => dressNow, cull, bandShare);
            if (forest == null) { log.Append($"{id}: no GPU forest on this machine\n"); return null; }
            // (the levels as the forest draws them: with the tree match on, its far level is the match card)
            variants = forest.variants;
            var rows = new List<PairRow>();
            float gameFov = cam.fieldOfView, keepFov = gameFov, keepAspect = cam.aspect, keepFar = cam.farClipPlane;
            var keepFlags = cam.clearFlags; var keepBg = cam.backgroundColor; int keepMask = cam.cullingMask;
            float keepStr = SunShadows.Strength, keepSky = SunShadows.SkyStrength;
            var sStr = typeof(SunShadows).GetProperty("Strength"); var sSky = typeof(SunShadows).GetProperty("SkyStrength");
            try
            {
                // a row of plants far above the scene: nothing else of the foliage layer in the view
                const float Spacing = 250f;
                var basePos = new Vector3(-per * Spacing * 0.5f, 3000f, 0f);
                var plants = new List<MeshForest.PoolPlant>(per);
                for (int i = 0; i < pick.Count; i++)
                    for (int k = 0; k < inst; k++)
                        plants.Add(new MeshForest.PoolPlant
                        {
                            pos = basePos + new Vector3((i * inst + k) * Spacing + k * 3.37f, k * 1.91f, k * 7.13f),
                            yaw = k * 45f, scale = 1f, variant = pick[i],
                        });
                var centre = basePos + new Vector3(per * Spacing * 0.5f, 0f, 0f);
                int slot = forest.PoolFill(centre, per * Spacing * 0.5f + 600f, null, plants, out int kept);
                if (slot < 0 || kept != per) { log.Append($"{id}: the pool took {kept} of {per} plants\n"); return null; }
                log.Append($"  {inst} plants of each at different places, each seen from its own bearing (0 / 120 / 240 degrees: sun behind, across, ahead); FOREST DETAIL {ForestDetailPrefs.Label}: levels {F(forest.lod0To, "0")} / {F(forest.lod1To, "0")} / {F(forest.meshRange, "0")} m, " +
                           $"hand-over bands {F(forest.band0, "0")} / {F(forest.band1, "0")} / {F(forest.band2, "0")} m; game lens {F(gameFov, "0")} deg, pictures at the pixel scale of a {GameLines}-line frame; tree match cards on {forest.MatchCount} species\n");
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Bg; cam.cullingMask = 1 << forest.gameObject.layer;
                cam.aspect = 1f; cam.farClipPlane = Mathf.Max(keepFar, 3000f);
                sStr?.SetValue(null, 0f); sSky?.SetValue(null, 0f);
                // the level a pair's side is drawn at: an eye distance inside that level, outside every band
                float e0 = Mathf.Max(2f, (forest.lod0To - forest.band0) * 0.5f);
                float e1 = (forest.lod0To + (forest.lod1To - forest.band1)) * 0.5f;
                float e2 = (forest.lod1To + (forest.meshRange - forest.band2)) * 0.5f;
                float e3 = forest.meshRange + 8f;
                float[] eyeOf = { e0, e1, e2, e3 };
                float[] at = { forest.lod0To, forest.lod1To, forest.meshRange };
                PSXTexDecode.StampAll();
                // PSX_TM_TUNE=1: the match cards' light and gain as this process's PSX_TM_* say (in memory only:
                // the kit on disk keeps what its build wrote) - a sweep without a kit build
                if (Env("PSX_TM_TUNE", "0") == "1")
                {
                    int tuned = 0;
                    foreach (var tv in variants)
                    {
                        if (tv == null || tv.matchDress == null) continue;
                        foreach (var m in tv.matchDress)
                        {
                            if (m == null) continue;
                            bool leafy = m.GetVector("_LeafVivid").z > 0.5f;
                            float g = leafy ? PSXRacingBuilder.TMLeafGain : PSXRacingBuilder.TMBareGain;
                            m.color = new Color(g, g, g);
                            var cc = m.GetVector("_PSXCrownCard");
                            m.SetVector("_PSXCrownCard", leafy ? new Vector4(PSXRacingBuilder.TMLeafN, PSXRacingBuilder.TMLeafUp, cc.z, 0f)
                                                               : new Vector4(PSXRacingBuilder.TMBareN, PSXRacingBuilder.TMBareUp, cc.z, 0f));
                            tuned++;
                        }
                    }
                    log.Append($"  TUNED in memory: leaf gain {F(PSXRacingBuilder.TMLeafGain)}, normal {F(PSXRacingBuilder.TMLeafN)} + up {F(PSXRacingBuilder.TMLeafUp)}; bare gain {F(PSXRacingBuilder.TMBareGain)}, normal {F(PSXRacingBuilder.TMBareN)} + up {F(PSXRacingBuilder.TMBareUp)} ({tuned} material slots)\n");
                }
                string pairsWanted = Env("PSX_TM_PAIRS", "0,1,2");
                foreach (int d in dresses)
                {
                    dressNow = d;
                    string dn = Seasons.DressNames[d];
                    for (int i = 0; i < pick.Count; i++)
                    {
                        int v = pick[i];
                        var vr = variants[v];
                        float h = Mathf.Max(0.5f, vr.height);
                        float span = Mathf.Max(h, 2f * RadiusOf(vr)) * 1.3f;
                        for (int k = 0; k < inst; k++)
                        {
                            var p = plants[i * inst + k].pos;
                            float az = k * 120f;
                            for (int pr = 0; pr < 3; pr++)
                            {
                                if (pairsWanted.IndexOf((char)('0' + pr)) < 0) continue;
                                bool noFar = pr == 2 && (vr.lods.Length < 4 || vr.lods[3] == null);
                                if (!noFar && !Distinct(vr, pr, pr + 1)) continue;
                                float D = at[pr];
                                var ta = Shoot(cam, forest, p, h, span, D, eyeOf[pr], gameFov, az);
                                var tb = Shoot(cam, forest, p, h, span, D, eyeOf[pr + 1], gameFov, az);
                                var row = new PairRow { kit = id, name = names[v], group = groups[v], dress = dn, pair = PairNames[pr], variant = v, inst = k, a = ta.st, b = tb.st, ta = ta, tb = tb };
                                row.why = noFar ? "NOFAR" : Verdict(ta.st, tb.st, ta.mpp);
                                if (!row.Fail && k > 0) { row.ta = null; row.tb = null; }   // (pictures kept for the first plant and for failures)
                                rows.Add(row);
                            }
                        }
                    }
                }
            }
            finally
            {
                MeshForest.LodEyeOn = false;
                sStr?.SetValue(null, keepStr); sSky?.SetValue(null, keepSky);
                cam.clearFlags = keepFlags; cam.backgroundColor = keepBg; cam.cullingMask = keepMask;
                cam.fieldOfView = keepFov; cam.aspect = keepAspect; cam.farClipPlane = keepFar;
                Object.DestroyImmediate(forest.gameObject);
            }
            return rows;
        }

        /// <summary>The lines of the frame whose pixel scale the pictures are drawn at (the owner's display: 3832 x 2054).</summary>
        const int GameLines = 2054;

        static float RadiusOf(MeshForest.Variant v)
        {
            float r = 0f;
            for (int l = 0; l < 3 && l < v.lods.Length; l++)
            {
                var m = v.lods[l];
                if (m == null) continue;
                var b = m.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var q = v.pre.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                    r = Mathf.Max(r, Mathf.Sqrt(q.x * q.x + q.z * q.z));
                }
            }
            return r;
        }

        /// <summary>One plant alone, seen from <paramref name="D"/> m at the game's pixel scale,
        /// its level chosen as from <paramref name="lodEye"/> m.</summary>
        static Tile Shoot(Camera cam, MeshForest forest, Vector3 p, float h, float span, float D, float lodEye, float gameFov, float azimuth = 0f)
        {
            var turn = Quaternion.Euler(0f, azimuth, 0f);
            float need = span / (2f * D * Mathf.Tan(gameFov * 0.5f * Mathf.Deg2Rad)) * GameLines;
            int size = Mathf.Clamp(Mathf.CeilToInt(need / 4f) * 4, 32, Cell);
            // (a tree taller than the cell at the game's scale is drawn a little smaller: the lens widened to hold it)
            float spanLens = need > Cell ? span : span * size / need;
            cam.fieldOfView = 2f * Mathf.Atan(spanLens * 0.5f / D) * Mathf.Rad2Deg;
            var eye = p + turn * new Vector3(0f, h * 0.5f, -D);
            var rot = Quaternion.LookRotation(turn * Vector3.forward);
            cam.transform.SetPositionAndRotation(eye, rot);
            MeshForest.LodEyeOn = true;
            MeshForest.LodEye = p + turn * new Vector3(0f, 0f, -lodEye);
            MeshForest.MeshTreesOn = true;
            forest.DrawNow(cam);
            var rt = RenderTexture.GetTemporary(size, size, 32, RenderTextureFormat.ARGB32);
            var request = new RenderPipeline.StandardRequest { destination = rt };
            var tile = new Tile { size = size };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                tile.px = tex.GetPixels32();
                Object.DestroyImmediate(tex);
            }
            else tile.px = new Color32[size * size];
            RenderTexture.ReleaseTemporary(rt);
            MeshForest.LodEyeOn = false;
            tile.mpp = 2f * D * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / size;
            // (the eye looks level at half the tree's height: its foot is that far under the picture's middle; 0.3 m of grace)
            tile.st = Measure(tile.px, size, tile.mpp, Mathf.FloorToInt(size * 0.5f - (h * 0.5f + 0.3f) / tile.mpp));
            return tile;
        }

        /// <summary>The plant off its picture. Rows under <paramref name="footRow"/> are left out (what a
        /// model carries under its own foot - roots, a drooping limb - is in the ground in the game). The
        /// outline is where 96 % of its drawn pixels lie each way (2nd to 98th percentile): one twig's
        /// pixel out at the edge is not the tree's size.</summary>
        static Stat Measure(Color32[] px, int size, float mpp, int footRow = 0)
        {
            var s = new Stat { x0 = size, y0 = size, x1 = -1, y1 = -1 };
            double r = 0, g = 0, b = 0;
            var colN = new int[size]; var rowN = new int[size];
            for (int y = Mathf.Max(0, footRow); y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var c = px[y * size + x];
                    if (Mathf.Abs(c.r - Bg.r) <= 10 && c.g <= 10 && Mathf.Abs(c.b - Bg.b) <= 10) continue;
                    s.n++;
                    r += c.r; g += c.g; b += c.b;
                    colN[x]++; rowN[y]++;
                }
            if (s.n == 0) return s;
            int lo = Mathf.Max(1, Mathf.RoundToInt(s.n * 0.02f)), hi = s.n - lo + 1;
            void Span(int[] hist, out int a0, out int a1)
            {
                a0 = 0; a1 = hist.Length - 1;
                int acc = 0; bool got0 = false;
                for (int i = 0; i < hist.Length; i++)
                {
                    acc += hist[i];
                    if (!got0 && acc >= lo) { a0 = i; got0 = true; }
                    if (acc >= hi) { a1 = i; break; }
                }
            }
            Span(colN, out s.x0, out s.x1);
            Span(rowN, out s.y0, out s.y1);
            s.r = (float)(r / s.n / 255.0); s.g = (float)(g / s.n / 255.0); s.b = (float)(b / s.n / 255.0);
            Color.RGBToHSV(new Color(s.r, s.g, s.b), out s.hue, out s.sat, out s.val);
            s.hue *= 360f;
            s.luma = 0.2126f * s.r + 0.7152f * s.g + 0.0722f * s.b;
            s.hM = (s.y1 - s.y0 + 1) * mpp; s.wM = (s.x1 - s.x0 + 1) * mpp;
            s.areaM2 = s.n * mpp * mpp;
            return s;
        }

        static string Verdict(Stat a, Stat b, float mpp)
        {
            if (!a.Any && !b.Any) return "";
            if (!a.Any || !b.Any) return "GONE";
            var why = new List<string>();
            float ar = Mathf.Min(a.areaM2, b.areaM2) / Mathf.Max(a.areaM2, b.areaM2);
            if (ar < AreaRatioMin) why.Add("LEAF");
            // (the outline: past the share AND past three pixels - one twig's pixel at the edge is not a size)
            bool Off(float x, float y) => Mathf.Abs(x - y) / Mathf.Max(x, y) > SizeTol && Mathf.Abs(x - y) > 3f * mpp;
            if (Off(a.hM, b.hM) || Off(a.wM, b.wM)) why.Add("SIZE");
            float smin = Mathf.Min(a.sat, b.sat), smax = Mathf.Max(a.sat, b.sat);
            if (smin > SatFloor && Mathf.Abs(Mathf.DeltaAngle(a.hue, b.hue)) > HueTolDeg) why.Add("HUE");
            if (smax - smin > SatTol) why.Add("SAT");
            if (Mathf.Abs(a.luma - b.luma) / Mathf.Max(0.02f, Mathf.Max(a.luma, b.luma)) > LumaTol) why.Add("VALUE");
            return string.Join("+", why);
        }

        static void WriteCsv(string path, List<PairRow> rows)
        {
            var sb = new StringBuilder("kit,variant,name,group,dress,pair,inst,verdict,areaA,areaB,hA,hB,wA,wB,hueA,hueB,satA,satB,valA,valB,lumaA,lumaB,rgbA,rgbB\n");
            foreach (var r in rows)
                sb.Append($"{r.kit},{r.variant},{r.name},{r.group},{r.dress},{r.pair},{r.inst},{(r.Fail ? r.why : "ok")},{F(r.a.areaM2, "0.0")},{F(r.b.areaM2, "0.0")},{F(r.a.hM, "0.0")},{F(r.b.hM, "0.0")},{F(r.a.wM, "0.0")},{F(r.b.wM, "0.0")}," +
                          $"{F(r.a.hue, "0")},{F(r.b.hue, "0")},{F(r.a.sat)},{F(r.b.sat)},{F(r.a.val)},{F(r.b.val)},{F(r.a.luma, "0.000")},{F(r.b.luma, "0.000")}," +
                          $"{(int)(r.a.r * 255)}/{(int)(r.a.g * 255)}/{(int)(r.a.b * 255)},{(int)(r.b.r * 255)}/{(int)(r.b.g * 255)}/{(int)(r.b.b * 255)}\n");
            File.WriteAllText(path, sb.ToString());
        }

        /// <summary>The table for one kit: per dress and hand-over, the species that fail (a species fails
        /// when any of its plants does) by reason; then the classes (group, dress, hand-over, reasons).</summary>
        static string Tally(string id, List<PairRow> rows, List<int> dresses, StringBuilder log)
        {
            // species-pair -> the union of its plants' reasons
            var sp = new Dictionary<string, (string group, string name, string dress, string pair, HashSet<string> why, PairRow worst)>();
            foreach (var r in rows)
            {
                string key = r.variant + "|" + r.dress + "|" + r.pair;
                if (!sp.TryGetValue(key, out var e)) e = (r.group, r.name, r.dress, r.pair, new HashSet<string>(), r);
                if (r.Fail) { foreach (var w in r.why.Split('+')) e.why.Add(w); if (!e.worst.Fail) e.worst = r; }
                sp[key] = e;
            }
            int total = sp.Count, failed = 0;
            log.Append($"  {"dress",-7} {"hand-over",-10} {"species",8} {"fail",5}   by reason\n");
            foreach (int d in dresses)
                foreach (var pn in PairNames)
                {
                    int n = 0, f = 0; var by = new SortedDictionary<string, int>();
                    foreach (var e in sp.Values)
                    {
                        if (e.dress != Seasons.DressNames[d] || e.pair != pn) continue;
                        n++;
                        if (e.why.Count == 0) continue;
                        f++;
                        foreach (var w in e.why) { by.TryGetValue(w, out int c); by[w] = c + 1; }
                    }
                    if (n == 0) continue;
                    failed += f;
                    var bs = new StringBuilder();
                    foreach (var kv in by) bs.Append($"{kv.Key} {kv.Value}  ");
                    log.Append($"  {Seasons.DressNames[d],-7} {pn,-10} {n,8} {f,5}   {bs}\n");
                }
            // the classes
            var cls = new SortedDictionary<string, (int n, string eg, PairRow worst)>();
            var clsAll = new Dictionary<string, int>();
            foreach (var e in sp.Values)
            {
                string all = e.group + " | " + e.dress + " | " + e.pair;
                clsAll.TryGetValue(all, out int ca); clsAll[all] = ca + 1;
                if (e.why.Count == 0) continue;
                var ws = new List<string>(e.why); ws.Sort();
                string key = all + " | " + string.Join("+", ws);
                cls.TryGetValue(key, out var c);
                cls[key] = (c.n + 1, c.eg ?? e.name, c.worst ?? e.worst);
            }
            log.Append($"  MISMATCH CLASSES ({cls.Count}):\n");
            foreach (var kv in cls)
            {
                string all = kv.Key.Substring(0, kv.Key.LastIndexOf(" | "));
                var w = kv.Value.worst;
                log.Append($"    {kv.Key}: {kv.Value.n} of {clsAll[all]} species (e.g. {kv.Value.eg}: area {F(w.a.areaM2, "0")} v {F(w.b.areaM2, "0")} m2, " +
                           $"h {F(w.a.hM, "0.0")} v {F(w.b.hM, "0.0")} m, w {F(w.a.wM, "0.0")} v {F(w.b.wM, "0.0")} m, hue {F(w.a.hue, "0")} v {F(w.b.hue, "0")}, sat {F(w.a.sat)} v {F(w.b.sat)}, luma {F(w.a.luma)} v {F(w.b.luma)})\n");
            }
            return $"{id}: {failed} / {total}";
        }

        /// <summary>The failing pairs of one dress, side by side (near level | far level), one species a row
        /// (its first failing plant), and the index beside the picture.</summary>
        static void WriteFailSheet(string root, string id, int dress, List<PairRow> rows)
        {
            string dn = Seasons.DressNames[dress];
            var seen = new HashSet<string>();
            var list = new List<PairRow>();
            foreach (var r in rows)
            {
                if (r.dress != dn || !r.Fail || r.ta == null || r.tb == null) continue;
                if (!seen.Add(r.variant + "|" + r.pair)) continue;
                list.Add(r);
            }
            bool all = Env("PSX_TM_PICS", "fail") == "all";
            if (all)
                foreach (var r in rows)
                    if (r.dress == dn && !r.Fail && r.inst == 0 && r.ta != null && r.tb != null && seen.Add(r.variant + "|" + r.pair)) list.Add(r);
            string txt = Path.Combine(root, $"tm_{id}_{dn}_fail.txt");
            foreach (var old in Directory.GetFiles(root, $"tm_{id}_{dn}_fail*.png")) File.Delete(old);
            if (File.Exists(txt)) File.Delete(txt);
            if (list.Count == 0) return;
            // the far hand-over first (the widest on the screen), then the mesh levels; pages of PageRows
            list.Sort((x, y) => x.pair == y.pair ? x.variant.CompareTo(y.variant) : string.CompareOrdinal(y.pair, x.pair));
            const int PageRows = 40, MaxPages = 6;
            if (list.Count > PageRows * MaxPages) list.RemoveRange(PageRows * MaxPages, list.Count - PageRows * MaxPages);
            var idx = new StringBuilder("page,row,variant,name,group,pair,verdict,inst\n");
            for (int page = 0; page * PageRows < list.Count; page++)
            {
                int n = Mathf.Min(PageRows, list.Count - page * PageRows);
                int W = Cell * 2, H = Cell * n;
                var img = new Color32[W * H];
                for (int i = 0; i < img.Length; i++) img[i] = new Color32(40, 40, 40, 255);
                for (int k = 0; k < n; k++)
                {
                    var r = list[page * PageRows + k];
                    int rowY = (n - 1 - k) * Cell;   // (row 0 at the top of the picture)
                    Paste(img, W, r.ta, 0, rowY);
                    Paste(img, W, r.tb, Cell, rowY);
                    idx.Append($"{page},{k},{r.variant},{r.name},{r.group},{r.pair},{(r.Fail ? r.why : "ok")},{r.inst}\n");
                }
                var t = new Texture2D(W, H, TextureFormat.RGBA32, false);
                t.SetPixels32(img); t.Apply();
                File.WriteAllBytes(Path.Combine(root, $"tm_{id}_{dn}_fail_{page}.png"), t.EncodeToPNG());
                Object.DestroyImmediate(t);
            }
            File.WriteAllText(txt, idx.ToString());
        }

        static void Paste(Color32[] img, int W, Tile t, int ox, int oy)
        {
            int off = (Cell - t.size) / 2;
            for (int y = 0; y < t.size; y++)
                for (int x = 0; x < t.size; x++)
                {
                    var c = t.px[y * t.size + x];
                    // the key colour drawn as a quiet grey-blue
                    if (Mathf.Abs(c.r - Bg.r) <= 10 && c.g <= 10 && Mathf.Abs(c.b - Bg.b) <= 10) c = new Color32(150, 170, 190, 255);
                    c.a = 255;
                    img[(oy + off + y) * W + ox + off + x] = c;
                }
        }
    }
}
