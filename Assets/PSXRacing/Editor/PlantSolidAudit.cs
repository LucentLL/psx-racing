using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// PLANTS THROUGH SOLIDS (owner 2026-11-10, on the Linn Cove Viaduct in the
    /// PC26 build: "trees/foliage/etc should not protrude through bridge, walls,
    /// rails, or cars (especially in cockpit mode)"). On a BUILT stage scene,
    /// independent of the builder's own planting rule: every MeshForest plant
    /// near the road (and every visible billboard tree) taken as its actual
    /// mesh, vertex by vertex, at its own scale, yaw and tilt, against
    ///
    ///   envelope  the lanes and shoulders (tarmac edge + 1.1 m) from the road
    ///             surface up to 4.5 m: anything there goes through a car or
    ///             into its cabin. Canopy higher up may overhang the road.
    ///   solid     the colliders of what is built solid: bridge decks and piers,
    ///             walls and rails (WallColl), tunnel walls and portals, and the
    ///             rock cuts (BankColl, BankTop's face). A segment from the
    ///             plant's own axis out to each vertex (and up the axis) that
    ///             crosses a deck, pier, wall or rail passes through it; one that
    ///             comes OUT of rock (the plant rooted inside a face) does too.
    ///             Leaves running into rock from in front of it are hidden, not
    ///             through, and are not counted.
    ///
    /// One line per class; the total must be 0. The worst offenders are listed
    /// with their position and station so they can be looked at.
    /// </summary>
    public static class PlantSolidAudit
    {
        const float EnvelopeH = 4.5f;
        /// <summary>The envelope's width past the tarmac edge: the 0.9 m verge
        /// strip and the 0.2 m paved shoulder (PSXRacingBuilder.StageVerge's
        /// shoulder), where a car on its wheels can be.</summary>
        const float EnvelopeShoulder = PSXRacingBuilder.KerbWidth + 0.2f;
        const float RoadLift = PSXRacingBuilder.RoadLift;
        const int AuditLayer = 31;
        const int PointsPerPlant = 320;

        internal enum Cat { None, Thin, Rock }

        static Cat CategoryOf(Collider c)
        {
            string n = c.gameObject.name;
            if (n.StartsWith("BridgeDeck") || n == "Pier" || n.StartsWith("WallColl") || n.StartsWith("WallPortal")) return Cat.Thin;
            // a boulder (PSXRacingBuilder.PlaceBoulders): a plant may not cross it either way
            if (n == "Boulder" && c is MeshCollider) return Cat.Thin;
            if (n.StartsWith("BankColl") || n.StartsWith("WallTunnel")) return Cat.Rock;
            // a rock top carries two colliders: its top (ground a plant stands
            // in) and its FACE (BankTopColl); only the face is the cut
            if (n.StartsWith("BankTop") && c is MeshCollider mc && mc.sharedMesh != null && mc.sharedMesh.name.StartsWith("BankTopColl")) return Cat.Rock;
            return Cat.None;
        }

        public sealed class Result
        {
            public readonly Dictionary<string, int> plants = new Dictionary<string, int>();
            public readonly Dictionary<string, int> hits = new Dictionary<string, int>();
            public readonly Dictionary<string, int> hitWhat = new Dictionary<string, int>();
            public readonly Dictionary<string, int> byClassWhat = new Dictionary<string, int>();
            public readonly List<(float depth, string cls, string what, Vector3 at, int station)> worst = new List<(float, string, string, Vector3, int)>();
            public int checkedNear, total;
            /// <summary>Stations (waypoint index) where foliage came closest to
            /// the envelope (m from it; 0 = in it), best first.</summary>
            public readonly List<(int station, float gap)> closest = new List<(int, float)>();
        }

        static string ClassOf(MeshForest.Variant v)
        {
            string n = v.lods[0] != null ? v.lods[0].name.ToLowerInvariant() : "";
            // the boulders and the cuts' talus (PSXRacingBuilder.LoadBoulders):
            // rock itself, like the cliffs - placed clear of walls by the builder
            if (v.mats != null && v.mats.Length > 0 && v.mats[0] != null && v.mats[0].name.ToLowerInvariant().Contains("boulder")) return "cliff";
            if (n.Contains("beech_tree_00")) return "small beech";
            if (n.Contains("beech_tree_") || n.Contains("poplar")) return "canopy";
            if (n.Contains("black_cherry") || n.Contains("maple_bush")) return "bush";
            if (n.Contains("grass") || n.Contains("fern") || n.Contains("plant_") || n.Contains("shaggy")) return "cover";
            if (n.Contains("detail_") || n.Contains("forest_branch")) return "litter";
            if (n.Contains("stones") || n.Contains("dead_log") || n.Contains("stump")) return "debris";
            if (n.Contains("cliff")) return "cliff";
            if (n.Contains("fence")) return "fence";
            return "other";
        }

        // ---- the road (the scene's TrackPath, or the builder's waypoints) ----
        static Vector3[] wp; static Vector3[] wpRight;
        static Dictionary<long, List<int>> wpHash;
        const float WpCell = 16f;
        static float roadHalf;
        static bool wpLoop;

        static void IndexRoad(IList<Vector3> pts, float roadWidth)
        {
            int n = pts.Count;
            wpBankL = wpBankR = null;
            wp = new Vector3[n]; wpRight = new Vector3[n];
            wpHash = new Dictionary<long, List<int>>();
            wpLoop = n > 2 && new Vector2(pts[0].x - pts[n - 1].x, pts[0].z - pts[n - 1].z).magnitude < TrackCatalog.Spacing * 2f;
            for (int i = 0; i < n; i++) wp[i] = pts[i];
            for (int i = 0; i < n; i++)
            {
                int a = i > 0 ? i - 1 : (wpLoop ? n - 1 : 0), b = i < n - 1 ? i + 1 : (wpLoop ? 0 : n - 1);
                var t = wp[b] - wp[a]; t.y = 0f;
                wpRight[i] = Vector3.Cross(Vector3.up, t.sqrMagnitude > 1e-8f ? t.normalized : Vector3.forward);
                long k = Key(wp[i].x, wp[i].z);
                if (!wpHash.TryGetValue(k, out var l)) wpHash[k] = l = new List<int>();
                l.Add(i);
            }
            roadHalf = roadWidth * 0.5f;
        }

        /// <summary>The scene path's cross-section (TrackPath.bankL/bankR,
        /// PSXRacingBuilder.Bank), or null on a flat road: the envelope stands
        /// on the banked surface, not on the centre line's height.</summary>
        static float[] wpBankL, wpBankR;

        static void IndexRoad(TrackPath tp)
        {
            var pts = new List<Vector3>(tp.Count);
            for (int i = 0; i < tp.Count; i++) pts.Add(tp.GetPoint(i));
            IndexRoad(pts, tp.roadWidth);
            wpBankL = tp.HasBank ? tp.bankL : null;
            wpBankR = tp.HasBank ? tp.bankR : null;
        }

        static long Key(float x, float z) => ((long)Mathf.FloorToInt(x / WpCell) << 32) ^ (uint)Mathf.FloorToInt(z / WpCell);

        /// <summary>Nearest waypoint in plan, preferring (within 6 m of the
        /// nearest) the one whose road is nearest in height, so a plant under
        /// a grade separation is judged by the road it is under.</summary>
        static int Nearest(Vector3 p, float reach, out float planD)
        {
            int cx = Mathf.FloorToInt(p.x / WpCell), cz = Mathf.FloorToInt(p.z / WpCell);
            int cells = Mathf.CeilToInt(reach / WpCell);
            int best = -1; float bd = reach * reach;
            for (int oz = -cells; oz <= cells; oz++)
                for (int ox = -cells; ox <= cells; ox++)
                {
                    if (!wpHash.TryGetValue(((long)(cx + ox) << 32) ^ (uint)(cz + oz), out var l)) continue;
                    foreach (int i in l)
                    {
                        float dx = wp[i].x - p.x, dz = wp[i].z - p.z, d2 = dx * dx + dz * dz;
                        if (d2 < bd) { bd = d2; best = i; }
                    }
                }
            planD = best >= 0 ? Mathf.Sqrt(bd) : float.MaxValue;
            return best;
        }

        /// <summary>Lateral offset of a point from the centreline at the
        /// nearest station (refined onto the segment either side).</summary>
        static bool RoadFrame(Vector3 p, float reach, out float lateral, out float roadTop, out int station)
        {
            lateral = float.MaxValue; roadTop = 0f;
            station = Nearest(p, reach, out _);
            if (station < 0) return false;
            int i = station, n = wp.Length;
            if (!wpLoop && (i == 0 || i == n - 1))
            {
                // past the end of a stage with ends: no road there to clear
                Vector3 fwd = i == 0 ? wp[1] - wp[0] : wp[n - 1] - wp[n - 2]; fwd.y = 0f;
                float along = Vector3.Dot(new Vector3(p.x - wp[i].x, 0f, p.z - wp[i].z), fwd.normalized);
                if ((i == 0 && along < -0.5f) || (i == n - 1 && along > 0.5f)) return false;
            }
            Vector3 best = wp[i]; float t0 = 0f;
            // project onto the segment toward whichever neighbour the point is beside
            for (int s = -1; s <= 1; s += 2)
            {
                int j = i + s; if (j < 0 || j >= n) continue;
                Vector3 a = wp[i], b = wp[j];
                Vector2 ab = new Vector2(b.x - a.x, b.z - a.z), ap = new Vector2(p.x - a.x, p.z - a.z);
                float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(ap, ab) / ab.sqrMagnitude) : 0f;
                if (t > t0) { t0 = t; best = Vector3.Lerp(a, b, t); }
            }
            float latS = Vector3.Dot(new Vector3(p.x - best.x, 0f, p.z - best.z), wpRight[i]);
            lateral = Mathf.Abs(latS);
            roadTop = best.y + RoadLift;
            if (wpBankL != null && wpBankL.Length == n)
                roadTop += (latS >= 0f ? wpBankR[i] : wpBankL[i]) * Mathf.Min(lateral, roadHalf);
            return true;
        }

        public static string AuditForLab(TrackCatalog.TrackDef def, string scenePath)
        {
            if (EditorSceneManager.GetActiveScene().path != scenePath)
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var r = Audit();
            return Describe(def.id, r);
        }

        public static string Describe(string id, Result r)
        {
            var sb = new StringBuilder();
            sb.Append("plants through solids (" + id + "): " + r.total + " (must be 0)");
            sb.Append("  [" + r.checkedNear + " plants within reach of the road checked]");
            sb.AppendLine();
            foreach (var kv in r.plants)
            {
                r.hits.TryGetValue(kv.Key, out int h);
                sb.AppendLine($"  {kv.Key,-12} {h,6} of {kv.Value,7}");
            }
            if (r.hitWhat.Count > 0)
            {
                sb.Append("  through:");
                foreach (var kv in r.hitWhat) sb.Append(" " + kv.Key + " " + kv.Value);
                sb.AppendLine();
            }
            if (r.byClassWhat.Count > 0)
            {
                sb.Append("  by class and solid:");
                var keys = new List<string>(r.byClassWhat.Keys); keys.Sort();
                foreach (var k in keys) sb.Append(" " + k + " " + r.byClassWhat[k] + ";");
                sb.AppendLine();
            }
            r.worst.Sort((a, b) => b.depth.CompareTo(a.depth));
            // the worst few of each solid
            var shownBy = new Dictionary<string, int>();
            foreach (var w in r.worst)
            {
                shownBy.TryGetValue(w.what, out int sh);
                if (sh >= 4) continue;
                shownBy[w.what] = sh + 1;
                sb.AppendLine($"  worst: {w.cls} through {w.what} by {w.depth:0.00} m at ({w.at.x:0.0}, {w.at.y:0.0}, {w.at.z:0.0}), station {w.station} ({w.station * TrackCatalog.Spacing:0} m)");
            }
            if (r.closest.Count > 0)
            {
                sb.Append("  foliage nearest the envelope (station m: gap):");
                foreach (var c in r.closest) sb.Append($" {c.station * TrackCatalog.Spacing:0}: {c.gap:0.00}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static Result Audit()
        {
            var r = new Result();
            var tp = Object.FindFirstObjectByType<TrackPath>();
            if (tp == null || tp.Count < 2) return r;
            IndexRoad(tp);
            PlantShape.ClearCache();
            BeginSolids();
            // nearest-to-envelope foliage per 40 m of road (for picking cockpit runs)
            var gapBy = new Dictionary<int, float>();
            try
            {
                var forest = Object.FindFirstObjectByType<MeshForest>();
                if (forest != null && forest.inst != null)
                {
                    var root = forest.transform.localToWorldMatrix;
                    for (int i = 0; i < forest.inst.Length; i++)
                    {
                        var v = forest.variants[(int)forest.instScaleVar[i].y];
                        string cls = ClassOf(v);
                        if (cls == "cliff") continue;   // rock itself, set into the face by design
                        r.plants[cls] = r.plants.TryGetValue(cls, out int pc) ? pc + 1 : 1;
                        var p = forest.inst[i];
                        float s = forest.instScaleVar[i].x;
                        var shape = PlantShape.Of(v);
                        if (shape == null) continue;
                        var basePos = root.MultiplyPoint3x4(new Vector3(p.x, p.y, p.z));
                        int near = Nearest(basePos, roadHalf + 12f + shape.rMax * s, out float pd);
                        // a tunnel's portal wall reaches 18 m from the centreline: out to 20 m, solids only
                        if (near < 0) near = Nearest(basePos, 20f + shape.rMax * s, out pd);
                        if (near < 0) continue;
                        r.checkedNear++;
                        var tl = forest.instTilt != null && i < forest.instTilt.Length ? forest.instTilt[i] : Vector2.zero;
                        var M = root * Matrix4x4.TRS(new Vector3(p.x, p.y, p.z),
                                    Quaternion.Euler(tl.x, 0f, tl.y) * Quaternion.Euler(0f, p.w, 0f), new Vector3(s, s, s));
                        TestPlant(r, cls, M, PlantShape.Points(v, PointsPerPlant), shape.height, gapBy,
                                  pd - shape.rMax * s < roadHalf + EnvelopeShoulder + 3f);
                    }
                }
                // the visible billboard trees (conifers on the PC build, all of them on the web's)
                foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                {
                    string n = mf.gameObject.name;
                    if (!(n.StartsWith("Forest_") || n.StartsWith("Fill_") || n.StartsWith("FarTrees_"))) continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || !mr.enabled || mf.sharedMesh == null) continue;
                    var vs = mf.sharedMesh.vertices;
                    var xf = mf.transform.localToWorldMatrix;
                    for (int t = 0; t + 7 < vs.Length; t += 8)
                    {
                        // a tree: two crossed quads, corners bottom-left, top-left, top-right, bottom-right
                        Vector3 b0 = xf.MultiplyPoint3x4(vs[t]), b3 = xf.MultiplyPoint3x4(vs[t + 3]);
                        Vector3 baseW = (b0 + b3) * 0.5f;
                        float h = xf.MultiplyPoint3x4(vs[t + 1]).y - b0.y;
                        float halfW = (b3 - b0).magnitude * 0.5f;
                        r.plants["card tree"] = r.plants.TryGetValue("card tree", out int cc) ? cc + 1 : 1;
                        if (Nearest(baseW, roadHalf + 12f + halfW, out float cpd) < 0) continue;
                        r.checkedNear++;
                        // the painted part: the inner 70% of each card's width, from 15% of its height
                        var pts = new List<Vector3>();
                        for (int q = 0; q < 2; q++)
                        {
                            Vector3 a = xf.MultiplyPoint3x4(vs[t + q * 4]) - baseW, b = xf.MultiplyPoint3x4(vs[t + q * 4 + 3]) - baseW;
                            for (int gy = 0; gy <= 4; gy++)
                                for (int gx = 0; gx <= 6; gx++)
                                {
                                    float u = Mathf.Lerp(0.15f, 0.85f, gx / 6f), y = Mathf.Lerp(0.15f, 1f, gy / 4f) * h;
                                    pts.Add(Vector3.Lerp(a, b, u) + Vector3.up * y);
                                }
                        }
                        TestPlant(r, "card tree", Matrix4x4.Translate(baseW), pts.ToArray(), h, gapBy,
                                  cpd - halfW < roadHalf + EnvelopeShoulder + 3f);
                    }
                }
            }
            finally { EndSolids(); }
            foreach (var kv in r.hits) r.total += kv.Value;
            var gl = new List<(int, float)>();
            // (keyed by 40 m of road: back to a station)
            foreach (var kv in gapBy) gl.Add((kv.Key * Mathf.RoundToInt(40f / TrackCatalog.Spacing), kv.Value));
            gl.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            // the closest few, each at least 250 m from the others
            foreach (var g in gl)
            {
                bool apart = true;
                foreach (var c in r.closest) if (Mathf.Abs(c.station - g.Item1) * TrackCatalog.Spacing < 250f) apart = false;
                if (apart) r.closest.Add(g);
                if (r.closest.Count >= 4) break;
            }
            return r;
        }

        static readonly RaycastHit[] hitBuf = new RaycastHit[16];

        /// <summary>One plant: its local points (base at the origin, metres at
        /// scale 1, axis up) through <paramref name="M"/>.</summary>
        static void TestPlant(Result r, string cls, Matrix4x4 M, Vector3[] pts, float height,
                              Dictionary<int, float> gapBy, bool envCheck)
        {
            string what = ProbePlant(M, pts, height, cls == "litter", envCheck, gapBy, out float depth, out Vector3 at, out int atSt);
            if (what != null)
            {
                r.hits[cls] = r.hits.TryGetValue(cls, out int hc) ? hc + 1 : 1;
                string key = what.StartsWith("BridgeDeck") ? "deck" : what == "Pier" ? "pier" : what.StartsWith("Wall") ? "wall/rail"
                           : what.StartsWith("Bank") ? "rock cut" : what;
                r.hitWhat[key] = r.hitWhat.TryGetValue(key, out int wc) ? wc + 1 : 1;
                string ck = cls + "/" + key;
                r.byClassWhat[ck] = r.byClassWhat.TryGetValue(ck, out int cw) ? cw + 1 : 1;
                if (atSt < 0) RoadFrame(at, 60f, out _, out _, out atSt);
                r.worst.Add((depth, cls, key, at, atSt));
                // keep the deepest of each solid
                if (r.worst.Count > 4000)
                {
                    r.worst.Sort((a, b) => b.depth.CompareTo(a.depth));
                    var keep = new List<(float, string, string, Vector3, int)>();
                    var per = new Dictionary<string, int>();
                    foreach (var w in r.worst)
                    {
                        per.TryGetValue(w.what, out int pk);
                        if (pk >= 40) continue;
                        per[w.what] = pk + 1; keep.Add(w);
                    }
                    r.worst.Clear(); r.worst.AddRange(keep);
                }
            }
        }

        // ---- the solids, shared with the builder's planting rule (PSXRacingBuilder.PlantClear) ----
        static Dictionary<Collider, Cat> cats;
        static List<(GameObject go, int layer)> movedLayers;
        static bool keepBackfaces;
        const int SolidMask = 1 << AuditLayer;

        /// <summary>The solids onto a layer of their own and back-face hits on,
        /// until <see cref="EndSolids"/> (always called; the layers go back).</summary>
        static void BeginSolids()
        {
            movedLayers = new List<(GameObject, int)>();
            cats = new Dictionary<Collider, Cat>();
            foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                var cat = CategoryOf(c);
                if (cat == Cat.None || c.isTrigger) continue;
                cats[c] = cat;
                if (c.gameObject.layer != AuditLayer) { movedLayers.Add((c.gameObject, c.gameObject.layer)); c.gameObject.layer = AuditLayer; }
            }
            Physics.SyncTransforms();
            keepBackfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
        }

        static void EndSolids()
        {
            Physics.queriesHitBackfaces = keepBackfaces;
            if (movedLayers != null) foreach (var (go, layer) in movedLayers) if (go != null) go.layer = layer;
            movedLayers = null; cats = null;
        }

        /// <summary>For the stage builder: the road and the solids it has
        /// built so far, for <see cref="Blocked"/>.</summary>
        internal static void BeginForBuilder(IList<Vector3> road, float roadWidth)
        {
            IndexRoad(road, roadWidth);
            BeginSolids();
        }

        internal static void EndForBuilder() => EndSolids();

        /// <summary>Does the plant (local points through M, as the audit takes
        /// it) go through anything? The solid's name, "envelope", or null.</summary>
        internal static string Blocked(Matrix4x4 M, Vector3[] pts, float height, bool litter)
        {
            if (cats == null) return null;
            return ProbePlant(M, pts, height, litter, true, null, out _, out _, out _);
        }

        static string ProbePlant(Matrix4x4 M, Vector3[] pts, float height, bool litter, bool envCheck, Dictionary<int, float> gapBy,
                            out float oDepth, out Vector3 oAt, out int oSt)
        {
            var cats = PlantSolidAudit.cats;
            const int mask = SolidMask;
            float depth = 0f; string what = null; Vector3 at = default; int atSt = -1;
            // ENVELOPE: any point over the lanes or shoulder, between the road and 4.5 m above it
            if (envCheck)
            foreach (var lp in pts)
            {
                var w = M.MultiplyPoint3x4(lp);
                if (!RoadFrame(w, roadHalf + EnvelopeShoulder + 4f, out float lat, out float top, out int st)) continue;
                float over = lat - (roadHalf + EnvelopeShoulder);
                float dy = w.y - top;
                // flat litter (a leaf carpet a car drives over) from 0.3 m up, the rest from 2 cm
                if (dy > (litter ? 0.3f : 0.02f) && dy < EnvelopeH)
                {
                    int bucket = Mathf.FloorToInt(st * TrackCatalog.Spacing / 40f);
                    float g = Mathf.Max(0f, over);
                    if (gapBy != null && (!gapBy.TryGetValue(bucket, out float og) || g < og)) gapBy[bucket] = g;
                    if (over < 0f && -over > depth) { depth = -over; what = "envelope"; at = w; atSt = st; }
                }
            }
            // SOLIDS: only where the plant's box meets one
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            foreach (var lp in pts) { var w = M.MultiplyPoint3x4(lp); lo = Vector3.Min(lo, w); hi = Vector3.Max(hi, w); }
            var axis0 = M.MultiplyPoint3x4(Vector3.zero);
            lo = Vector3.Min(lo, axis0); hi = Vector3.Max(hi, axis0);
            if (Physics.CheckBox((lo + hi) * 0.5f, (hi - lo) * 0.5f + Vector3.one * 0.05f, Quaternion.identity, mask, QueryTriggerInteraction.Ignore))
            {
                // the axis itself, from just over the base to the top
                Seg(M.MultiplyPoint3x4(new Vector3(0f, Mathf.Min(0.3f, height * 0.1f), 0f)), M.MultiplyPoint3x4(new Vector3(0f, height, 0f)));
                // rooted INSIDE a deck, pier or wall
                for (float f = 0.15f; f < 1f; f += 0.35f)
                {
                    var ap = M.MultiplyPoint3x4(new Vector3(0f, height * f, 0f));
                    foreach (var c in Physics.OverlapSphere(ap, 0.05f, mask, QueryTriggerInteraction.Ignore))
                        if (cats.TryGetValue(c, out var cat) && cat == Cat.Thin)
                            Note(0.5f, c.gameObject.name, ap);
                }
                foreach (var lp in pts)
                    Seg(M.MultiplyPoint3x4(new Vector3(0f, lp.y, 0f)), M.MultiplyPoint3x4(lp));
            }
            oDepth = depth; oAt = at; oSt = atSt;
            return what;

            void Note(float d, string name, Vector3 p)
            {
                if (what != null && d <= depth) return;
                depth = Mathf.Max(depth, d); what = name; at = p; atSt = -1;
            }

            void Seg(Vector3 a, Vector3 b)
            {
                var dir = b - a;
                float len = dir.magnitude;
                if (len < 0.02f) return;
                dir /= len;
                int n = Physics.RaycastNonAlloc(a, dir, hitBuf, len, mask, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                {
                    var h = hitBuf[k];
                    if (!cats.TryGetValue(h.collider, out var cat)) continue;
                    bool exits = Vector3.Dot(h.normal, dir) > 0f;
                    if (cat == Cat.Rock)
                    {
                        // out of the rock (rooted inside it), and only through a FACE:
                        // a rock top's level surface is the ground the plant stands in
                        if (!exits || Mathf.Abs(h.normal.y) > 0.6f) continue;
                    }
                    // how much of the plant lies past the solid (a 5 cm graze is not through it)
                    if (len - h.distance < 0.05f) continue;
                    Note(len - h.distance, h.collider.gameObject.name, h.point);
                }
            }
        }

        // ------------------------------------------------------------------
        //  Lab entry: the audit on the scenes as they stand, and the pictures
        // ------------------------------------------------------------------

        /// <summary>
        /// For each venue in PSX_FOL_IDS: the audit on its built scene (no
        /// build), then (PSX_FOL_SHOTS) HazeProbe's pictures at fixed poses -
        /// "BlueRidge=4608:chase|4608:under|auto:cockpit;SwissNC226A=auto:cockpit"
        /// ("auto" = the stations where foliage came nearest the envelope, or
        /// through it). Writes PSXRacing_plant_audit.txt and
        /// Screenshots/fol_(PSX_FOL_TAG)_(venue)_(m)_(view).png.
        /// </summary>
        public static void Probe()
        {
            string tag = System.Environment.GetEnvironmentVariable("PSX_FOL_TAG") ?? "before";
            string ids = System.Environment.GetEnvironmentVariable("PSX_FOL_IDS") ?? "BlueRidge";
            string shots = System.Environment.GetEnvironmentVariable("PSX_FOL_SHOTS") ?? "";
            var sb = new StringBuilder();
            string shotDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
            Directory.CreateDirectory(shotDir);
            foreach (var f in Directory.GetFiles(shotDir, "fol_" + tag + "_*")) File.Delete(f);
            try
            {
                foreach (var raw in ids.Split(','))
                {
                    string id = raw.Trim();
                    if (id.Length == 0) continue;
                    string path = "Assets/PSXRacing/Scenes/" + id + ".unity";
                    if (!File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath), path))) { sb.AppendLine("no scene " + path); continue; }
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    var t0 = System.DateTime.Now;
                    var res = Audit();
                    sb.Append(Describe(id, res));
                    sb.AppendLine($"  ({(System.DateTime.Now - t0).TotalSeconds:0} s)");
                    // the pictures
                    string spec = null;
                    foreach (var part in shots.Split(';'))
                        if (part.StartsWith(id + "=")) spec = part.Substring(id.Length + 1);
                    if (string.IsNullOrEmpty(spec)) continue;
                    var pairs = new List<string>();
                    foreach (var item in spec.Split('|'))
                    {
                        var kv = item.Split(':');
                        if (kv.Length != 2) continue;
                        if (kv[0] == "auto")
                        {
                            // the worst offenders' stations first, then the nearest foliage
                            var used = new List<int>();
                            res.worst.Sort((a, b) => b.depth.CompareTo(a.depth));
                            foreach (var w in res.worst)
                            {
                                if (w.station < 0 || used.Count >= 2 || w.what != "envelope") continue;
                                bool apart = true;
                                foreach (int u in used) if (Mathf.Abs(u - w.station) * TrackCatalog.Spacing < 250f) apart = false;
                                if (apart) used.Add(w.station);
                            }
                            foreach (var c in res.closest)
                            {
                                if (used.Count >= 3) break;
                                bool apart = true;
                                foreach (int u in used) if (Mathf.Abs(u - c.station) * TrackCatalog.Spacing < 250f) apart = false;
                                if (apart) used.Add(c.station);
                            }
                            // stand 10 m short of the spot, so it is in front of the camera
                            foreach (int u in used) pairs.Add(Mathf.Max(0f, u * TrackCatalog.Spacing - 10f).ToString("0", CultureInfo.InvariantCulture) + ":" + kv[1]);
                        }
                        else pairs.Add(item);
                    }
                    sb.AppendLine("  shots: " + string.Join(",", pairs));
                    System.Environment.SetEnvironmentVariable("PSX_HAZE_VENUE", id);
                    System.Environment.SetEnvironmentVariable("PSX_HAZE_SHOTS", string.Join(",", pairs));
                    System.Environment.SetEnvironmentVariable("PSX_HAZE_HOURS", "noon");
                    System.Environment.SetEnvironmentVariable("PSX_HAZE_VARIANTS", "asis");
                    HazeProbe.Capture();
                    foreach (var f in Directory.GetFiles(shotDir, "hz_noon_*_asis.png"))
                    {
                        string nm = Path.GetFileName(f).Replace("hz_noon_", "fol_" + tag + "_" + id + "_").Replace("_asis", "");
                        string dst = Path.Combine(shotDir, nm);
                        if (File.Exists(dst)) File.Delete(dst);
                        File.Move(f, dst);
                    }
                }
            }
            catch (System.Exception e) { sb.AppendLine("THREW " + e); }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_plant_audit_" + tag + ".txt"), sb.ToString());
            Debug.Log("[PlantSolidAudit]\n" + sb);
        }
    }
}
