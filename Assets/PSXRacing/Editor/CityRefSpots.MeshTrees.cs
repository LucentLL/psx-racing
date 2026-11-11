using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CHARLOTTE'S MESH TREES, PROVED (ctrees, 2026-11-11): before (the card
    /// trees, F8's A/B) and after (CityMeshTrees through the mountain forest's
    /// GPU path) from the same build, at the owner's N Graham St spot, an Uptown
    /// street of grate trees, Dilworth, a south subdivision, a freeway verge and
    /// a planted median - from the driver's eye and from 150 m back (the
    /// distance hand-over) - noon, plus dusk and night for two, fall and winter
    /// for one. Then the CITY FOLIAGE AUDIT over every tile the shots built:
    /// each mesh tree's LOD0 vertices (not the fit's bands) against the
    /// pavements' clearance envelope, the building footprints and roofs, the
    /// poles, wires and signal arms, and every collider standing there - the
    /// gate is 0. And the cost: trees and triangles a ring, the frame with and
    /// without, buffer memory.
    ///
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunMeshTrees
    ///   PSX_CT_ONLY=place,place  PSX_CT_NOSHOTS=1 (audit + cost only)
    /// Output: Screenshots/ctrees/&lt;place&gt;_&lt;pose&gt;_&lt;hour&gt;_&lt;dress&gt;_&lt;before|after&gt;.png, ctrees_log.txt
    /// </summary>
    public static partial class CityRefSpots
    {
        struct CtPlace { public string id, road, what; public double lat, lon; public float hdg; public bool hours, seasons; }

        static readonly CtPlace[] CtPlaces =
        {
            new CtPlace { id = "graham", lat = 35.23456, lon = -80.84152, road = "Graham", hdg = 229f, hours = true, what = "N Graham St SW past W 10th (the owner's spot, the buildings agent's graham_b)" },
            new CtPlace { id = "uptown", lat = 35.22745, lon = -80.84275, road = "Tryon", hdg = 50f, hours = true, what = "N Tryon St, Trade to 5th: grate street trees" },
            new CtPlace { id = "dilworth", lat = 35.20225, lon = -80.84758, road = "Dilworth", hdg = 192f, seasons = true, what = "Dilworth Rd E: old-city willow oaks, yards" },
            new CtPlace { id = "south_sub", lat = 35.09500, lon = -80.80200, road = "", hdg = float.NaN, what = "a south Charlotte subdivision street (nearest road)" },
            new CtPlace { id = "fwy_verge", lat = 35.16100, lon = -80.87900, road = "I-77", hdg = float.NaN, what = "I-77 south of Uptown: freeway verge, tree walls" },
            new CtPlace { id = "median", lat = 35.13864, lon = -80.78400, road = "Providence", hdg = 328f, what = "Providence Rd: planted median" },
        };

        public static void RunMeshTrees()
        {
            var log = new StringBuilder("CITY MESH TREES " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "ctrees");
            Directory.CreateDirectory(dir);
            int code = 0, shots = 0;
            try
            {
                System.Environment.SetEnvironmentVariable("PSX_PC_CONTENT", "1");
                var kitAsset = PSXRacingBuilder.EnsureCityTreeKit(System.Environment.GetEnvironmentVariable("PSX_CT_REKIT") == "1");
                if (kitAsset == null) throw new System.Exception("no city tree kit (store files?)");
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                if (output != null) output.height = TexShotHeight;
                TexturePrefs.Enhanced = true;
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                var sun = NightLookShots.FindSun();
                var car = player.GetComponent<CarController>();
                var dressing = Object.FindAnyObjectByType<SeasonDress>();
                CityMeshTrees.ResetCounts();
                var mt = CityMeshTrees.EnsureFor(world);
                if (mt == null) throw new System.Exception("CityMeshTrees did not start (kit / GPU)");
                var kit = mt.Kit;
                log.Append($"kit: {kit.variants.Length} species; LOD {mt.Forest.lod0To:0}/{mt.Forest.lod1To:0}/{mt.Forest.meshRange:0} m, impostors to {mt.Forest.farRange:0} m (FOREST DETAIL {ForestDetailPrefs.Label})\n");
                for (int v = 0; v < kit.variants.Length; v++)
                    log.Append($"  {v} {kit.names[v]} [{kit.roles[v]}] {kit.variants[v].height:0.0} m crown from {kit.crownBase[v]:0.00}\n");

                string only = System.Environment.GetEnvironmentVariable("PSX_CT_ONLY");
                bool noShots = System.Environment.GetEnvironmentVariable("PSX_CT_NOSHOTS") == "1";
                var audit = new AuditSum();
                foreach (var pl in CtPlaces)
                {
                    if (!string.IsNullOrEmpty(only) && System.Array.IndexOf(only.Split(','), pl.id) < 0) continue;
                    if (!Snap(map, LL(pl.lat, pl.lon), pl.road, out var e, out float s, out bool named)) { log.Append($"{pl.id}: no road near\n"); continue; }
                    var t2 = e.TangentAt(s);
                    var fh = new Vector3(t2.x, 0f, t2.y).normalized;
                    if (!float.IsNaN(pl.hdg))
                    {
                        float hr = pl.hdg * Mathf.Deg2Rad;
                        if (Vector3.Dot(fh, new Vector3(Mathf.Sin(hr), 0f, Mathf.Cos(hr))) < 0f) fh = -fh;
                    }
                    Vector2 p = e.PointAt(s);
                    float lat = e.oneway ? 0f : e.width * 0.25f;
                    var pos = new Vector3(p.x, e.YAt(s) + 0.35f, p.y) + Vector3.Cross(Vector3.up, fh) * lat;
                    var rot = Quaternion.LookRotation(fh, Vector3.up);
                    int f0 = CityMeshTrees.Fitted, pl0 = CityMeshTrees.Placed, dr0 = CityMeshTrees.Dropped;
                    double ms0 = CityMeshTrees.FitMs;
                    world.EnsureRing(pos, 2);
                    mt.Flush();
                    if (car != null) car.TeleportTo(pos, rot); else player.transform.SetPositionAndRotation(pos, rot);
                    log.Append($"{pl.id}\t'{e.name}' cls {e.cls}{(named ? "" : " (NOT the named road)")}\tcar ({pos.x:0.0}, {pos.y:0.00}, {pos.z:0.0}) fwd ({fh.x:0.00}, {fh.z:0.00})\t{pl.what}\n");
                    log.Append($"  ring: {CityMeshTrees.Fitted - f0} tree frames fitted, {CityMeshTrees.Placed - pl0} mesh trees, {CityMeshTrees.Dropped - dr0} not fitted, fit {(CityMeshTrees.FitMs - ms0):0} ms; live tiles {mt.LiveTiles}, plants {mt.LivePlants()}\n");

                    // THE AUDIT of every fitted tile in this ring (each tile once)
                    AuditRing(mt, map, world, audit, log);

                    if (!noShots)
                    {
                        Vector3 eye = pos + fh * 3f + Vector3.up * 0.8f;
                        var poses = new List<(string, Vector3, Quaternion)>
                        {
                            ("eye", eye, Quaternion.LookRotation(fh * 40f + Vector3.up * 1.5f)),
                            ("far150", pos - fh * 150f + Vector3.up * 1.6f, Quaternion.LookRotation(fh * 150f + Vector3.up * 3f)),
                        };
                        var hours = pl.hours ? new[] { "noon", "dusk", "night" } : new[] { "noon" };
                        foreach (var h in hours)
                            shots += CtShootSet(cam, sun, dressing, mt, pl.id, poses, h, (int)Season.Summer, log);
                        if (pl.seasons)
                        {
                            shots += CtShootSet(cam, sun, dressing, mt, pl.id, poses.GetRange(0, 1), "noon", (int)Season.Fall, log);
                            shots += CtShootSet(cam, sun, dressing, mt, pl.id, poses.GetRange(0, 1), "noon", (int)Season.Winter, log);
                        }
                        // THE COST at the driver's eye: cards v meshes, the frame and the triangles
                        Cost(cam, mt, pl.id, eye, poses[0].Item3, log);
                    }
                    world.DropAll();
                    mt.Flush();
                }
                log.Append($"\nFIT TOTALS: {CityMeshTrees.Placed} mesh trees placed, {CityMeshTrees.Dropped} not fitted ({CityMeshTrees.DroppedSolid} of them solid: their trunks left the table), moved {CityMeshTrees.Moved}, grown {CityMeshTrees.Grown}, shrunk {CityMeshTrees.Shrunk}, swapped {CityMeshTrees.Swapped}; {CityMeshTrees.Fitted} frames in {CityMeshTrees.FitMs:0} ms ({CityMeshTrees.FitMs / Mathf.Max(1, CityMeshTrees.Fitted):0.0} ms a tile)\n");
                log.Append("  candidates refused by:");
                for (int i = 0; i < CityMeshTrees.RejectBy.Length; i++) log.Append($" {CityMeshTrees.RejectNames[i]} {CityMeshTrees.RejectBy[i]};");
                log.Append("\n  colliders that refused:");
                foreach (var kv in CityMeshTrees.RejectColliders) log.Append($" {kv.Key} {kv.Value};");
                foreach (var dl in CityMeshTrees.DiagLines) log.Append("\n    " + dl);
                log.Append("\n  placed by role:");
                for (int i = 0; i < CityMeshTrees.PlacedByRole.Length; i++) if (CityMeshTrees.PlacedByRole[i] > 0) log.Append($" {(CityTreeKit.Role)i} {CityMeshTrees.PlacedByRole[i]};");
                log.Append($"\nCITY FOLIAGE AUDIT: {audit.tiles} tiles, {audit.trees} mesh trees, {audit.points} crown points tested -> trees through solids {audit.bad} (must be 0): road envelope {audit.road}, building {audit.bld}, pole/wire {audit.pole}, signal {audit.sig}, collider {audit.col}\n");
                foreach (var w in audit.worst) log.Append("  " + w + "\n");
                log.Append($"{shots} frames to {dir}\nCITY MESH TREES OK\n");
            }
            catch (System.Exception ex) { log.Append("CITY MESH TREES FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                File.WriteAllText(Path.Combine(dir, "ctrees_log.txt"), log.ToString());
                Debug.Log("[CityMeshTrees]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static int CtShootSet(Camera cam, Light sun, SeasonDress dressing, CityMeshTrees mt, string place,
                              List<(string name, Vector3 eye, Quaternion r)> poses, string h, int dress, StringBuilder log)
        {
            int n = 0;
            int hour = HourOf(h);
            RaceHandoff.CalendarDay = 0;
            TimeOfDay.Apply(hour, sun);
            var globals = Object.FindAnyObjectByType<PSXGlobals>();
            if (globals != null) globals.Apply();
            var hd = TimeOfDay.At(hour);
            NightGlow.PreviewAll(hd.lightsOn);
            foreach (var lights in Object.FindObjectsByType<CarLights>(FindObjectsInactive.Exclude)) lights.PreviewBuild(hd.lightsOn);
            if (dressing != null) dressing.Apply(dress);
            string dn = dress == (int)Season.Winter ? "winter" : dress == (int)Season.Fall ? "fall" : dress == (int)Season.Spring ? "spring" : dress == Seasons.DressSnow ? "snow" : "summer";
            foreach (var p in poses)
            {
                foreach (var mode in new[] { "before", "after" })
                {
                    CarLights.PushGlobals();
                    cam.transform.SetPositionAndRotation(p.eye, p.r);
                    SunShadows.RenderFor(cam);
                    if (mode == "before") { MeshForest.MeshTreesOn = false; mt.Forest.CardsNow(); }
                    else { MeshForest.MeshTreesOn = true; mt.Forest.DrawNow(cam); }
                    PSXScreenshotTool.ShotAs(cam, $"ctrees/{place}_{p.name}_{h}_{dn}_{mode}", p.eye, p.r);
                    n++;
                }
            }
            MeshForest.MeshTreesOn = true;
            log.Append($"  {place} {h} {dn}: {n} frames\n");
            return n;
        }

        /// <summary>Frame time and triangles at one eye, cards v mesh trees
        /// (editor, PC profile; the render waited for with a read-back).</summary>
        static void Cost(Camera cam, CityMeshTrees mt, string place, Vector3 eye, Quaternion r, StringBuilder log)
        {
            cam.transform.SetPositionAndRotation(eye, r);
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            var px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            double Time(bool mesh, int frames)
            {
                var sw = new System.Diagnostics.Stopwatch();
                for (int i = 0; i < frames + 2; i++)
                {
                    if (i == 2) sw.Start();
                    if (mesh) { MeshForest.MeshTreesOn = true; mt.Forest.DrawNow(cam); } else { MeshForest.MeshTreesOn = false; mt.Forest.CardsNow(); }
                    cam.Render();
                    RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); px.Apply(); RenderTexture.active = null;
                }
                sw.Stop();
                return sw.Elapsed.TotalMilliseconds / frames;
            }
            double cards = Time(false, 12), mesh = Time(true, 12);
            MeshForest.StatsOn = true; MeshForest.StatsReset();
            for (int i = 0; i < 12; i++) { mt.Forest.DrawNow(cam); cam.Render(); }
            UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
            int sf = Mathf.Max(1, MeshForest.StatFrames);
            log.Append($"  COST {place}: frame (1920x1080, editor, render + read-back) cards {cards:0.00} ms, mesh trees {mesh:0.00} ms; mesh trees drawn {MeshForest.SumPlants / sf:0} plants, {MeshForest.SumTris / sf / 1e6:0.00} M tris, {MeshForest.SumDraws / sf:0} draws; {MeshForest.StatsBreakdown()}\n");
            MeshForest.StatsOn = false;
            MeshForest.MeshTreesOn = true;
            cam.targetTexture = old;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(px);
        }

        // ------------------------------------------------------------------
        //  THE CITY FOLIAGE AUDIT
        // ------------------------------------------------------------------
        class AuditSum
        {
            public int tiles, trees, points, bad, road, bld, pole, sig, col;
            public readonly HashSet<long> seen = new HashSet<long>();
            public readonly List<string> worst = new List<string>();
        }

        static readonly Dictionary<Mesh, Vector3[]> samples = new Dictionary<Mesh, Vector3[]>();
        static readonly Collider[] auditHits = new Collider[64];

        static Vector3[] SampleOf(Mesh m)
        {
            if (samples.TryGetValue(m, out var s)) return s;
            var l = new List<Vector3>();
            for (int sub = 0; sub < m.subMeshCount; sub++)
            {
                if (!TreeKit.Read(m, sub, out var verts, out var tris)) continue;
                int step = Mathf.Max(1, tris.Length / 900);
                for (int i = 0; i < tris.Length; i += step) l.Add(verts[tris[i]]);
            }
            s = l.ToArray();
            samples[m] = s;
            return s;
        }

        static void AuditRing(CityMeshTrees mt, CityMap map, CityWorld world, AuditSum sum, StringBuilder log)
        {
            Physics.SyncTransforms();
            var kit = mt.Kit;
            var segSet = new HashSet<int>();
            int mask = (1 << CityWorld.SolidLayer) | 1;
            int ringBad = 0, ringTrees = 0;
            foreach (long key in new List<long>(mt.FittedKeys))
            {
                if (!sum.seen.Add(key)) continue;
                var plants = mt.PlantsOf(key);
                var frame = mt.FrameOf(key);
                if (plants == null) continue;
                sum.tiles++;
                foreach (var pp in plants)
                {
                    sum.trees++; ringTrees++;
                    var v = kit.variants[pp.variant];
                    var M = Matrix4x4.TRS(pp.pos, Quaternion.Euler(0f, pp.yaw, 0f), Vector3.one * pp.scale) * v.pre;
                    var pts = SampleOf(v.lods[0]);
                    var p2 = new Vector2(pp.pos.x, pp.pos.z);
                    float reach = 0f;
                    for (int b = 0; b < CityTreeKit.Bands; b++) reach = Mathf.Max(reach, kit.CrownR(pp.variant, b) * pp.scale);
                    // the roads round it
                    segSet.Clear();
                    map.EdgeSegsInRect(p2 - Vector2.one * (reach + 25f), p2 + Vector2.one * (reach + 25f), segSet);
                    var rs = new List<(Vector2 a, Vector2 b, float hw, CityMap.Edge e, float s0, float len)>();
                    foreach (int packed in segSet)
                    {
                        int ei = packed >> 12, si = packed & 0xFFF;
                        var e = map.edges[ei];
                        if (e.tunnel) continue;
                        rs.Add((e.pts[si], e.pts[si + 1], e.width * 0.5f, e, e.s[si], Vector2.Distance(e.pts[si], e.pts[si + 1])));
                    }
                    // the buildings round it
                    var bl = new List<CityBuildings.B>();
                    int tx = Mathf.FloorToInt(pp.pos.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(pp.pos.z / CityMeshes.TileSize);
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (world.Buildings != null && world.Buildings.TryGetValue(((long)(tx + dx) << 24) ^ ((tz + dz) & 0xFFFFFF), out var list))
                                foreach (var b in list) if ((b.pos - p2).sqrMagnitude < (reach + Mathf.Max(b.w, b.d) + 2f) * (reach + Mathf.Max(b.w, b.d) + 2f)) bl.Add(b);
                    // colliders (broad phase once a tree)
                    int nc = Physics.OverlapCapsuleNonAlloc(pp.pos, pp.pos + Vector3.up * (v.height * pp.scale + 1f), reach + 0.5f, auditHits, mask, QueryTriggerInteraction.Ignore);
                    bool anyCol = false;
                    for (int i = 0; i < nc; i++) { var c = auditHits[i]; if (c != null && Eligible(c)) { anyCol = true; break; } }
                    int hitRoad = 0, hitBld = 0, hitPole = 0, hitSig = 0, hitCol = 0;
                    foreach (var lp in pts)
                    {
                        var w = M.MultiplyPoint3x4(lp);
                        sum.points++;
                        var w2 = new Vector2(w.x, w.z);
                        if (w.y < pp.pos.y + 0.4f) continue;   // (roots and the trunk's foot in the ground)
                        foreach (var r in rs)
                        {
                            var d = r.b - r.a; float L2 = d.sqrMagnitude;
                            float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(w2 - r.a, d) / L2) : 0f;
                            if (Vector2.Distance(w2, r.a + d * t) >= r.hw) continue;
                            float ry = r.e.YAt(r.s0 + r.len * t);
                            if (w.y < ry + 4.5f && w.y > ry - 1.0f) { hitRoad++; break; }
                        }
                        foreach (var b in bl)
                        {
                            float cy = Mathf.Cos(b.yaw), sy = Mathf.Sin(b.yaw);
                            var q = w2 - b.pos;
                            if (Mathf.Abs(q.x * cy - q.y * sy) >= b.w * 0.5f || Mathf.Abs(q.x * sy + q.y * cy) >= b.d * 0.5f) continue;
                            float roof = b.gable ? 0.45f * Mathf.Min(b.w, b.d) : 0f;
                            if (w.y < pp.pos.y + b.h + roof) { hitBld++; break; }
                        }
                        if (frame != null && frame.poles != null)
                        {
                            foreach (var pl in frame.poles.poles)
                                if (Vector2.Distance(w2, new Vector2(pl.foot.x, pl.foot.z)) < 0.2f && w.y < pl.top) { hitPole++; break; }
                            foreach (var (a, b) in frame.poles.spans)
                            {
                                Vector2 a2 = new Vector2(a.foot.x, a.foot.z), b2 = new Vector2(b.foot.x, b.foot.z), d = b2 - a2;
                                float L2 = d.sqrMagnitude; float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(w2 - a2, d) / L2) : 0f;
                                if (Vector2.Distance(w2, a2 + d * t) > 0.15f) continue;
                                float top = Mathf.Lerp(a.top, b.top, t);
                                if (w.y < top + 0.1f && w.y > top - 0.4f - 1.2f * Mathf.Max(1, a.levels - 1) - 1.2f) { hitPole++; break; }
                            }
                        }
                        if (frame != null && frame.signals != null)
                            foreach (var (sf, top) in frame.signals.poles)
                                if (Vector2.Distance(w2, new Vector2(sf.x, sf.z)) < 0.25f && w.y < top) { hitSig++; break; }
                        if (anyCol)
                        {
                            int n2 = Physics.OverlapSphereNonAlloc(w, 0.04f, auditHits, mask, QueryTriggerInteraction.Ignore);
                            for (int i = 0; i < n2; i++) if (auditHits[i] != null && Eligible(auditHits[i])) { hitCol++; break; }
                        }
                    }
                    if (hitRoad + hitBld + hitPole + hitSig + hitCol > 0)
                    {
                        sum.bad++; ringBad++;
                        if (hitRoad > 0) sum.road++; if (hitBld > 0) sum.bld++; if (hitPole > 0) sum.pole++; if (hitSig > 0) sum.sig++; if (hitCol > 0) sum.col++;
                        if (sum.worst.Count < 40)
                            sum.worst.Add($"{kit.names[pp.variant]} x{pp.scale:0.00} at ({pp.pos.x:0.0}, {pp.pos.y:0.0}, {pp.pos.z:0.0}): points in road {hitRoad}, building {hitBld}, pole/wire {hitPole}, signal {hitSig}, collider {hitCol}");
                    }
                }
            }
            log.Append($"  audit: {ringTrees} mesh trees in this ring's new tiles, {ringBad} through a solid\n");
        }

        static bool Eligible(Collider c)
        {
            if (c.name == CityTrees.TrunkName) return false;
            string n = c.gameObject.name;
            if (n == "Ground" || n == "Kerbs" || n == "Roads" || n == "Banks" || n == "Water") return false;
            if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) return false;
            return true;
        }
    }
}
