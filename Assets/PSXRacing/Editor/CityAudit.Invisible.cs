using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// INVISIBLE COLLIDERS (2026-10-04, the owner's Uptown Loop at 1:18: "I
    /// still hit an invisible wall right here on 277" - the car stopped on
    /// clear-looking pavement). A collider exists only where its visible
    /// geometry is, and never in a lane. The tiles are stood up through
    /// CityWorld itself (EnsureTile + PlantTrees: the game's own colliders,
    /// props, poles, signals, signs and the trunk table), and two questions
    /// are asked:
    ///
    ///   LANES  along every lane of the race routes (route direction, 1 m)
    ///          and of every road in the box (both ways on a two-way road,
    ///          2 m), rays at 0.3 / 0.6 / 1.0 m over the lane's own surface,
    ///          1.05 m along the travel, with back faces ON (PhysX collides a
    ///          triangle mesh from both sides; the PSX shaders cull the back),
    ///          plus an overlap at each ray's start (a ray never sees the box
    ///          it starts in) and the trunk table (a capsule stands only near
    ///          a car). A hit with no drawn triangle facing the car within
    ///          <see cref="InvisTolM"/> is INVISIBLE: a FAIL. A drawn face
    ///          standing in a lane is reported (the drive audit owns those).
    ///   SOLIDS every Solid-layer collider of those tiles that is not its own
    ///          drawn mesh (collider-only meshes, boxes) is sampled - each
    ///          triangle's centre, each box's side-face centres - and needs a
    ///          drawn triangle within <see cref="InvisTolM"/>.
    ///
    /// tools\city-cycle.ps1 -DriveOnly runs it after the drive audit (in
    /// city_drive.txt); the menu item / PSX_INVIS_ONLY runs it alone into
    /// city_invisible.txt. PSX_DRIVE_INVIS=0 skips it in the drive audit;
    /// PSX_INVIS_BOX=0 skips the box; PSX_DRIVE_ROUTES picks the routes.
    /// </summary>
    public static partial class CityAudit
    {
        const float InvisTolM = 0.05f;
        static readonly float[] InvisHeights = { 0.3f, 0.6f, 1.0f, 1.4f };
        const float InvisRayM = 1.05f;
        /// <summary>The sweep's car: half extents (1.7 m wide, 1.1 m of body
        /// over a 0.2 m floor, 4 m long).</summary>
        static readonly Vector3 CarHalf = new Vector3(0.85f, 0.55f, 2.0f);
        static readonly List<float> laneLats = new List<float>(16);
        /// <summary>How far inside the drawn edge a rail's traffic face may
        /// stand (the drive audit's WALL probe keeps the same).</summary>
        const float RailClearM = 0.6f;

        /// <summary>The lane's own surface under a plan point: the highest
        /// Roads hit within 0.6 m of the solve's height.</summary>
        static bool LaneSurface(Vector3 w, float y, RaycastHit[] buf, out float sy)
        {
            int nd = Physics.RaycastNonAlloc(new Vector3(w.x, y + 2.5f, w.z), Vector3.down, buf, 5f, ~0, QueryTriggerInteraction.Ignore);
            sy = float.NegativeInfinity;
            for (int i = 0; i < nd; i++)
                if (buf[i].collider.name == "Roads" && Mathf.Abs(buf[i].point.y - y) < 0.6f && buf[i].point.y > sy) sy = buf[i].point.y;
            return !float.IsNegativeInfinity(sy);
        }

        /// <summary>A drawn mesh's triangles in world space, bucketed by 2 m
        /// plan cells, for "is there a drawn face here".</summary>
        sealed class TriGrid
        {
            const float Cell = 2f;
            readonly Vector3[] v; readonly int[] t; readonly Vector3[] n;
            readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
            static long K(int x, int z) => ((long)x << 32) | (uint)z;
            public TriGrid(Mesh m, Matrix4x4 l2w)
            {
                var lv = m.vertices;
                v = new Vector3[lv.Length];
                for (int i = 0; i < lv.Length; i++) v[i] = l2w.MultiplyPoint3x4(lv[i]);
                t = m.triangles;
                n = new Vector3[t.Length / 3];
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                    n[i / 3] = Vector3.Cross(b - a, c - a).normalized;
                    int x0 = Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - 0.1f) / Cell);
                    int x1 = Mathf.FloorToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) + 0.1f) / Cell);
                    int z0 = Mathf.FloorToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - 0.1f) / Cell);
                    int z1 = Mathf.FloorToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) + 0.1f) / Cell);
                    if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > 4096) continue;
                    for (int x = x0; x <= x1; x++)
                        for (int z = z0; z <= z1; z++)
                        {
                            long k = K(x, z);
                            if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<int>(8);
                            l.Add(i / 3);
                        }
                }
            }
            /// <summary>A triangle crossed by the segment p..p+dir*maxD whose
            /// front faces along dir (p stands behind it).</summary>
            public bool Behind(Vector3 p, Vector3 dir, float maxD)
            {
                var seen = new HashSet<int>();
                for (float d = 0f; d <= maxD + Cell; d += Cell * 0.5f)
                {
                    var c = p + dir * Mathf.Min(d, maxD);
                    if (!cells.TryGetValue(K(Mathf.FloorToInt(c.x / Cell), Mathf.FloorToInt(c.z / Cell)), out var l)) continue;
                    foreach (int ti in l)
                    {
                        if (!seen.Add(ti) || Vector3.Dot(n[ti], dir) <= 0.05f) continue;
                        Vector3 a = v[t[ti * 3]], b = v[t[ti * 3 + 1]], cc = v[t[ti * 3 + 2]];
                        Vector3 e1 = b - a, e2 = cc - a, pv = Vector3.Cross(dir, e2);
                        float det = Vector3.Dot(e1, pv);
                        if (Mathf.Abs(det) < 1e-8f) continue;
                        float inv = 1f / det; var tv = p - a;
                        float u = Vector3.Dot(tv, pv) * inv; if (u < 0f || u > 1f) continue;
                        var qv = Vector3.Cross(tv, e1);
                        float w = Vector3.Dot(dir, qv) * inv; if (w < 0f || u + w > 1f) continue;
                        float tt = Vector3.Dot(e2, qv) * inv;
                        if (tt >= 0f && tt <= maxD) return true;
                    }
                }
                return false;
            }

            /// <summary>A triangle within <paramref name="tol"/> of p; with a
            /// non-zero <paramref name="ray"/>, one whose FRONT faces back
            /// along it (what a driver looking along the ray sees).</summary>
            public bool Near(Vector3 p, float tol, Vector3 ray)
            {
                if (!cells.TryGetValue(K(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell)), out var l)) return false;
                foreach (int ti in l)
                {
                    if (ray != Vector3.zero && Vector3.Dot(n[ti], ray) > -0.05f) continue;
                    var q = ClosestOnTri(p, v[t[ti * 3]], v[t[ti * 3 + 1]], v[t[ti * 3 + 2]]);
                    if ((q - p).sqrMagnitude <= tol * tol) return true;
                }
                return false;
            }
        }

        /// <summary>Closest point on triangle abc to p (Ericson 5.1.5).</summary>
        static Vector3 ClosestOnTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float den = 1f / (va + vb + vc);
            return a + ab * (vb * den) + ac * (vc * den);
        }

        [MenuItem("PSX Racing/Audit City Invisible Colliders")]
        public static void RunInvisible()
        {
            outLog = new StringBuilder();
            failures = 0;
            var map = CityMap.Get();
            if (map == null) { Fail("charlotte_city.bytes missing from Resources"); FinishTo("city_invisible.txt"); return; }
            var trims = CityMeshes.NodeTrims(map);
            routeOfEdge = RouteEdges(map);
            InvisibleColliders(map, trims);
            FinishTo("city_invisible.txt");
        }

        static void InvisibleColliders(CityMap map, CityMeshes.Trims trims)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            string want = System.Environment.GetEnvironmentVariable("PSX_DRIVE_ROUTES");
            var ids = new HashSet<string>();
            if (!string.IsNullOrWhiteSpace(want)) foreach (var s in want.Split(',', ';', ' ')) if (s.Trim().Length > 0) ids.Add(s.Trim());
            bool withBox = System.Environment.GetEnvironmentVariable("PSX_INVIS_BOX") != "0";
            var scope = ScopeFor("DRIVE");

            // the sweep: (edge, direction, step, label, route offset); each
            // station is bucketed by the tile it stands on
            var jobs = new List<(int ei, int dir, float step, string label, float acc)>();
            var routeDone = new HashSet<long>();
            foreach (var r in map.routes)
            {
                if (ids.Count > 0 && !ids.Contains(r.id)) continue;
                float acc = 0f;
                for (int k = 0; k < r.edges.Length; k++)
                {
                    var e = map.edges[r.edges[k]];
                    int d = r.dirs != null && k < r.dirs.Length && r.dirs[k] < 0 ? -1 : 1;
                    jobs.Add((e.index, d, 1f, r.id, acc));
                    routeDone.Add(((long)e.index << 2) | (d > 0 ? 1L : 2L));
                    acc += e.length;
                }
            }
            int boxEdges = 0;
            if (withBox)
                foreach (var e in map.edges)
                {
                    if (!scope.Takes(e)) continue;
                    boxEdges++;
                    for (int d = 1; d >= -1; d -= 2)
                    {
                        if (d < 0 && e.oneway) continue;
                        if (routeDone.Contains(((long)e.index << 2) | (d > 0 ? 1L : 2L))) continue;
                        jobs.Add((e.index, d, 2f, "box", -1f));
                    }
                }
            var byTile = new Dictionary<long, List<(int job, float s)>>();
            var tileOrder = new List<long>();
            for (int j = 0; j < jobs.Count; j++)
            {
                var e = map.edges[jobs[j].ei];
                float step = jobs[j].step;
                for (float s = 0.5f; s < e.length; s += step)
                {
                    var p = e.PointAt(s);
                    if (jobs[j].label == "box" && !scope.Contains(p)) continue;
                    long key = TileKey(Mathf.FloorToInt(p.x / CityMeshes.TileSize), Mathf.FloorToInt(p.y / CityMeshes.TileSize));
                    if (!byTile.TryGetValue(key, out var l)) { byTile[key] = l = new List<(int, float)>(); tileOrder.Add(key); }
                    l.Add((j, s));
                }
            }
            Line($"INVISIBLE COLLIDERS: {jobs.Count} lane sweeps ({(ids.Count > 0 ? string.Join(",", ids) : "all")} routes; {(withBox ? $"{boxEdges} edges of {scope.Describe()}" : "no box")}) on {tileOrder.Count} tiles; rays at {string.Join("/", InvisHeights)} m over the lane, back faces on; a drawn face within {InvisTolM * 100f:0} cm");

            // the game's own world, stood up a 3x3 window at a time
            PSXRacingBuilder.EnsureCityTextures();
            var go = new GameObject("~invisAudit");
            var world = go.AddComponent<CityWorld>();
            world.ring = 0;
            world.enabled = false;
            var dropTile = typeof(CityWorld).GetMethod("DropTile", BindingFlags.Instance | BindingFlags.NonPublic);
            long WKey(int tx, int tz) => ((long)tx << 24) ^ (tz & 0xFFFFFF);
            var liveTiles = new HashSet<long>();          // audit keys
            var grids = new Dictionary<MeshFilter, TriGrid>();   // drawn mesh -> grid
            var drawn = new Dictionary<long, List<MeshFilter>>();
            int unreadable = 0;
            var railWas = CityMeshes.railLog;
            var rails = new List<CityMeshes.RailRecord>();
            CityMeshes.railLog = rails;
            // whose rail a barrier hit is: the nearest recorded piece within 1.5 m
            string RailOf(Vector3 at)
            {
                float bd = 1.5f; int bi = -1;
                for (int i = 0; i < rails.Count; i++)
                {
                    Vector3 a = rails[i].a, b = rails[i].b;
                    var ab = new Vector2(b.x - a.x, b.z - a.z); float l2 = ab.sqrMagnitude;
                    var ap = new Vector2(at.x - a.x, at.z - a.z);
                    float tt = l2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(ap, ab) / l2) : 0f;
                    float d = (ap - ab * tt).magnitude;
                    if (d < bd) { bd = d; bi = i; }
                }
                if (bi < 0) return "";
                var r = rails[bi];
                return $"; rail of e{r.edge} side {(r.side < 0 ? "L" : "R")}{(r.node >= 0 ? $" node {r.node}" : "")} s {r.s0:0.0}..{r.s1:0.0} ({bd:0.00} m)";
            }
            bool backWas = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;

            var tiles2 = new List<MeshFilter>();
            bool DrawnNear(Vector3 p, Vector3 ray, float tol)
            {
                int ptx = Mathf.FloorToInt(p.x / CityMeshes.TileSize), ptz = Mathf.FloorToInt(p.z / CityMeshes.TileSize);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (!drawn.TryGetValue(TileKey(ptx + dx, ptz + dz), out var mfs)) continue;
                        foreach (var mf in mfs)
                        {
                            if (mf == null) continue;
                            var mr = mf.GetComponent<Renderer>();
                            var b = mr.bounds; b.Expand(tol * 2f + 0.02f);
                            if (!b.Contains(p)) continue;
                            if (!grids.TryGetValue(mf, out var g))
                            {
                                var m = mf.sharedMesh;
                                if (m == null || !m.isReadable) { unreadable++; grids[mf] = null; continue; }
                                grids[mf] = g = new TriGrid(m, mf.transform.localToWorldMatrix);
                            }
                            if (g != null && g.Near(p, tol, ray)) return true;
                        }
                    }
                return false;
            }

            bool DrawnBehind(Vector3 p, Vector3 dir, float maxD)
            {
                int ptx = Mathf.FloorToInt(p.x / CityMeshes.TileSize), ptz = Mathf.FloorToInt(p.z / CityMeshes.TileSize);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (!drawn.TryGetValue(TileKey(ptx + dx, ptz + dz), out var mfs)) continue;
                        foreach (var mf in mfs)
                        {
                            if (mf == null) continue;
                            var b = mf.GetComponent<Renderer>().bounds; b.Expand(2f * maxD + 0.1f);
                            if (!b.Contains(p)) continue;
                            if (!grids.TryGetValue(mf, out var g))
                            {
                                var m = mf.sharedMesh;
                                if (m == null || !m.isReadable) { unreadable++; grids[mf] = null; continue; }
                                grids[mf] = g = new TriGrid(m, mf.transform.localToWorldMatrix);
                            }
                            if (g != null && g.Behind(p, dir, maxD)) return true;
                        }
                    }
                return false;
            }

            string PathOf(Component c)
            {
                var t = c.transform; var sb = new StringBuilder(t.name);
                while (t.parent != null && t.parent.name != "~invisAudit") { t = t.parent; sb.Insert(0, t.name + "/"); }
                return sb.ToString();
            }
            bool SelfDrawn(Collider c)
            {
                if (!(c is MeshCollider mc)) return false;
                var mf = c.GetComponent<MeshFilter>();
                return mf != null && mf.sharedMesh == mc.sharedMesh && c.GetComponent<MeshRenderer>() != null;
            }

            int stations = 0, sweeps = 0, rays = 0, invis = 0, visIn = 0, trunksIn = 0, solidsChecked = 0, solidsBare = 0;
            // WALLS IN LANES (2026-10-04, the owner's "stray medians on 277"): a
            // drawn face on a race route met between the route edge's DESIGNED
            // edge lines (the line model's, unsqueezed, unclipped) - a wall in
            // a lane the driver sees painted, not a parapet's foot on the shoulder
            int wallsInLanes = 0; var wallNotes = new List<string>(); var wallSeen = new HashSet<string>();
            var wallLines = new List<LineModel.LineAt>(16);
            void WallInLane(string jl, CityMap.Edge we, float ws, Vector2 wp, Vector2 wr, Vector3 whp, string wkd, string wnote)
            {
                if (jl == "box" || wkd.StartsWith("Ground") || wkd.StartsWith("Roads")) return;
                LineModel.LinesAt(we, ws, wallLines);
                if (wallLines.Count < 2) return;
                float lo = wallLines[0].lat, hi = wallLines[wallLines.Count - 1].lat;
                float lh = Vector2.Dot(new Vector2(whp.x, whp.z) - wp, wr);
                // a rail's traffic face stands RailW inside the drawn edge by
                // design (a curbless bridge's edge line at that edge): only a
                // face further in than that is in a lane
                if (lh <= lo + CityMeshes.RailW || lh >= hi - CityMeshes.RailW) return;
                wallsInLanes++;
                if (wallSeen.Add($"{jl} e{we.index} {Mathf.FloorToInt(ws / 5f)}"))
                    wallNotes.Add($"WALL IN LANE {wnote} | at lateral {lh:+0.00;-0.00} between the edge lines {lo:+0.00;-0.00}..{hi:+0.00;-0.00}");
            }
            var invisNotes = new List<string>(); var visNotes = new List<string>(); var bareNotes = new List<string>();
            // one note per (where, what) run, the first station's, with the run's count
            var noteRuns = new Dictionary<string, (List<string> list, int idx, int n)>();
            void AddNote(List<string> list, string key, string text)
            {
                if (noteRuns.TryGetValue(key, out var r)) { noteRuns[key] = (r.list, r.idx, r.n + 1); return; }
                if (list.Count >= 80) return;
                noteRuns[key] = (list, list.Count, 1);
                list.Add(text);
            }
            var invisByKind = new Dictionary<string, int>(); var visByKind = new Dictionary<string, int>(); var bareByKind = new Dictionary<string, int>();
            var seenHit = new HashSet<long>();
            var hitBuf = new RaycastHit[16];
            var ovBuf = new Collider[16];
            var trunkBuf = new List<Vector4>();
            void Bump(Dictionary<string, int> d, string k) => d[k] = d.TryGetValue(k, out var n) ? n + 1 : 1;
            string KindOf(Collider c)
            {
                string nm = c.name;
                var p = c.transform.parent;
                if (p != null && !p.name.StartsWith("Tile_")) nm = p.name + "/" + nm;
                return $"{nm} ({c.GetType().Name})";
            }

            try
            {
                foreach (long key in tileOrder)
                {
                    int ctx = (int)(key >> 32), ctz = (int)(uint)key;
                    // drop what left the window, stand up what entered it
                    foreach (var k in new List<long>(liveTiles))
                    {
                        int lx = (int)(k >> 32), lz = (int)(uint)k;
                        if (Mathf.Abs(lx - ctx) <= 1 && Mathf.Abs(lz - ctz) <= 1) continue;
                        if (drawn.TryGetValue(k, out var mfs)) { foreach (var mf in mfs) if (mf != null) grids.Remove(mf); drawn.Remove(k); }
                        dropTile.Invoke(world, new object[] { WKey(lx, lz) });
                        liveTiles.Remove(k);
                    }
                    var fresh = new List<long>();
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            long k = TileKey(ctx + dx, ctz + dz);
                            if (liveTiles.Contains(k)) continue;
                            world.EnsureTile(ctx + dx, ctz + dz);
                            world.PlantTrees(WKey(ctx + dx, ctz + dz));
                            liveTiles.Add(k);
                            fresh.Add(k);
                            var root = world.transform.Find($"Tile_{ctx + dx}_{ctz + dz}");
                            var list = new List<MeshFilter>();
                            if (root != null)
                                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                                {
                                    var mr = mf.GetComponent<MeshRenderer>();
                                    if (mr != null && mr.enabled && mr.gameObject.activeInHierarchy) list.Add(mf);
                                }
                            drawn[k] = list;
                        }
                    Physics.SyncTransforms();

                    // SOLIDS: the new tiles' Solid-layer colliders that are not their own drawn mesh
                    foreach (long k in fresh)
                    {
                        int fx = (int)(k >> 32), fz = (int)(uint)k;
                        if (!byTile.ContainsKey(k)) continue;   // a window tile only: its own turn checks it
                        var root = world.transform.Find($"Tile_{fx}_{fz}");
                        if (root == null) continue;
                        foreach (var c in root.GetComponentsInChildren<Collider>(true))
                        {
                            if (c.gameObject.layer != CityWorld.SolidLayer || c.isTrigger || SelfDrawn(c)) continue;
                            var pts = new List<Vector3>(); var outs = new List<Vector3>();
                            if (c is MeshCollider mc && mc.sharedMesh != null && mc.sharedMesh.isReadable)
                            {
                                var vv = mc.sharedMesh.vertices; var tt = mc.sharedMesh.triangles; var l2w = c.transform.localToWorldMatrix;
                                for (int i = 0; i + 2 < tt.Length; i += 3)
                                    pts.Add(l2w.MultiplyPoint3x4((vv[tt[i]] + vv[tt[i + 1]] + vv[tt[i + 2]]) / 3f));
                            }
                            else if (c is BoxCollider bc)
                            {
                                var tr = bc.transform;
                                var h = bc.size * 0.5f;
                                foreach (var f in new[] { new Vector3(h.x, 0f, 0f), new Vector3(-h.x, 0f, 0f), new Vector3(0f, 0f, h.z), new Vector3(0f, 0f, -h.z) })
                                {
                                    pts.Add(tr.TransformPoint(bc.center + f));
                                    outs.Add(tr.TransformDirection(f).normalized);
                                }
                            }
                            else continue;
                            int bare = 0; Vector3 firstBare = default;
                            for (int qi = 0; qi < pts.Count; qi++)
                            {
                                var q = pts[qi];
                                solidsChecked++;
                                if (DrawnNear(q, Vector3.zero, InvisTolM)) continue;
                                // a box face hidden inside what is drawn (a tower's
                                // box shaved inside its walls) is no invisible wall
                                if (qi < outs.Count && DrawnBehind(q, outs[qi], 0.65f)) continue;
                                if (bare++ == 0) firstBare = q;
                            }
                            if (bare == 0) continue;
                            solidsBare += bare;
                            string kd = KindOf(c);
                            Bump(bareByKind, kd);
                            if (bareNotes.Count < 40) bareNotes.Add($"BARE {PathOf(c)} [{kd}]: {bare} of {pts.Count} samples with no drawn face within {InvisTolM * 100f:0} cm, first at ({firstBare.x:0.0},{firstBare.y:0.00},{firstBare.z:0.0})");
                        }
                    }

                    // LANES
                    if (!byTile.TryGetValue(key, out var stns)) continue;
                    foreach (var (j, s) in stns)
                    {
                        var job = jobs[j];
                        var e = map.edges[job.ei];
                        var p = e.PointAt(s);
                        var tan = e.TangentAt(s);
                        var right = new Vector2(-tan.y, tan.x);
                        float y = e.YAt(s);
                        CityMeshes.LaneExtents(map, trims, e, s, out float hwL, out float hwR);
                        if (hwL + hwR < 2f) continue;
                        var dir3 = new Vector3(tan.x, 0f, tan.y) * job.dir;
                        stations++;
                        // THE CAR'S OWN SHAPE: a car-sized box (its floor 0.2 m
                        // over the lane, pitched with the road) on every lane
                        // centre, swept 1.05 m on - what a ray between two
                        // samples, or under a low edge, never meets
                        {
                            float sA = Mathf.Clamp(s - 2f, 0f, e.length), sB = Mathf.Clamp(s + 2f, 0f, e.length);
                            float slope = (e.YAt(sB) - e.YAt(sA)) / Mathf.Max(0.5f, sB - sA) * job.dir;
                            var fwd3 = new Vector3(dir3.x, slope, dir3.z).normalized;
                            var rot = Quaternion.LookRotation(fwd3, Vector3.up);
                            laneLats.Clear();
                            // the box kept inside the rail line: a deck's rail stands
                            // up to RailClearM inside the drawn edge
                            float l0 = -hwL + RailClearM + CarHalf.x, l1 = hwR - RailClearM - CarHalf.x;
                            if (l1 < l0) laneLats.Add((hwR - hwL) * 0.5f);
                            else for (float lc = l0; lc <= l1 + 1e-3f; lc += Mathf.Max(0.5f, Mathf.Min(1.0f, l1 - l0))) laneLats.Add(lc);
                            foreach (float lat in laneLats)
                            {
                                var w = new Vector3(p.x + right.x * lat, 0f, p.y + right.y * lat);
                                if (!LaneSurface(w, y, hitBuf, out float sy)) continue;
                                var c = new Vector3(w.x, sy + 0.2f + CarHalf.y, w.z);
                                sweeps++;
                                Collider hc = null; Vector3 hp = default, hn = Vector3.zero; bool inside = false, insideSeen = false;
                                int no = Physics.OverlapBoxNonAlloc(c, CarHalf, ovBuf, rot, ~0, QueryTriggerInteraction.Ignore);
                                for (int i = 0; i < no && hc == null; i++)
                                {
                                    var oc = ovBuf[i];
                                    if (oc.name == "Roads" || oc.name == "Ground" || oc.name == "Water") continue;   // the surface itself (the drive audit's)
                                    hc = oc; inside = true;
                                    hp = oc is MeshCollider mco && !mco.convex ? c : oc.ClosestPoint(c);
                                    // what the car's space sees of it: rays from a lattice of
                                    // points in the box, six ways; a FRONT face met (one
                                    // turned toward the ray) with a drawn face there is seen.
                                    // Only back faces met: a hollow end - nothing faces the car
                                    var rr = rot * Vector3.right; var ff = rot * Vector3.forward; var uu = rot * Vector3.up;
                                    insideSeen = false;
                                    foreach (float ox in new[] { -0.8f, 0f, 0.8f })
                                        foreach (float oy in new[] { -0.4f, 0.4f })
                                            foreach (float oz in new[] { -1.9f, 0f, 1.9f })
                                                foreach (var dv in new[] { rr, -rr, ff, -ff, -uu, uu })
                                                {
                                                    var org3 = c + rr * ox + uu * oy + ff * oz;
                                                    if (!oc.Raycast(new Ray(org3, dv), out var oh, 4.2f)) continue;
                                                    var tn = oh.normal;
                                                    if (oc is MeshCollider om && om.sharedMesh != null && om.sharedMesh.isReadable && oh.triangleIndex >= 0)
                                                    {
                                                        var tv = om.sharedMesh.triangles; var vv = om.sharedMesh.vertices; int ti = oh.triangleIndex * 3;
                                                        if (ti + 2 < tv.Length)
                                                        {
                                                            var tr = oc.transform;
                                                            Vector3 ta = tr.TransformPoint(vv[tv[ti]]), tb = tr.TransformPoint(vv[tv[ti + 1]]), tc = tr.TransformPoint(vv[tv[ti + 2]]);
                                                            tn = Vector3.Cross(tb - ta, tc - ta).normalized;
                                                        }
                                                    }
                                                    hp = oh.point; hn = -dv;
                                                    if (Vector3.Dot(tn, dv) < -0.05f && DrawnNear(oh.point, dv, InvisTolM)) { insideSeen = true; goto found; }
                                                }
                                    found:;
                                }
                                if (hc == null)
                                {
                                    int nh = Physics.BoxCastNonAlloc(c, CarHalf, fwd3, hitBuf, rot, InvisRayM, ~0, QueryTriggerInteraction.Ignore);
                                    float best = float.MaxValue;
                                    for (int i = 0; i < nh; i++)
                                    {
                                        var h = hitBuf[i];
                                        if (h.distance <= 0f) continue;
                                        if ((h.collider.name == "Roads" || h.collider.name == "Ground" || h.collider.name == "Water") && h.normal.y > 0.7f) continue;
                                        if (h.distance < best) { best = h.distance; hc = h.collider; hp = h.point; hn = h.normal; }
                                    }
                                }
                                if (hc == null) continue;
                                long cellB = ((long)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(hc) << 32) ^ (uint)(Mathf.FloorToInt(hp.x) * 73856093 ^ Mathf.FloorToInt(hp.z) * 19349663) ^ 0x3333;
                                if (!seenHit.Add(cellB)) continue;
                                // seen = a drawn face turned TOWARD the car: the face the sweep
                                // met (back faces on), or the face a ray from the box's middle met
                                bool visB = inside ? insideSeen : DrawnNear(hp, fwd3, InvisTolM);
                                string kdB = KindOf(hc);
                                string noteB = $"SWEEP {job.label}{(job.acc >= 0f ? $" at {job.acc + (job.dir > 0 ? s : e.length - s):0} m" : "")} e{e.index} '{e.name}'{(e.link ? " L" : "")}{(e.bridge ? " B" : "")} s={s:0}/{e.length:0} dir {job.dir:+0;-0} lane centre {lat:+0.0;-0.0} (hw {hwL:0.0}/{hwR:0.0}): {PathOf(hc)} [{kdB}]{(inside ? " INSIDE the car box" : "")} at ({hp.x:0.00},{hp.y:0.00},{hp.z:0.00}), {hp.y - sy:+0.00;-0.00} over the lane, normal ({hn.x:0.00},{hn.y:0.00},{hn.z:0.00}){(SelfDrawn(hc) ? " drawn mesh" : " collider-only")}{Owner2(map, trims, hp, e.index)}{(hc.name == "Barriers" ? RailOf(hp) : "")}";
                                string keyB = $"{job.label} e{e.index} {job.dir} sweep {kdB} {visB}";
                                if (!visB) { invis++; Bump(invisByKind, "sweep " + kdB); AddNote(invisNotes, keyB, "INVISIBLE " + noteB); }
                                else { visIn++; Bump(visByKind, "sweep " + kdB); AddNote(visNotes, keyB, "DRAWN " + noteB); WallInLane(job.label, e, s, p, right, hp, kdB, noteB); }
                            }
                        }
                        for (float lat = -hwL + 0.5f; lat <= hwR - 0.5f + 1e-3f; lat += 0.75f)
                        {
                            var w = new Vector3(p.x + right.x * lat, 0f, p.y + right.y * lat);
                            // the lane's own surface: the highest Roads hit within 0.6 m of the solve
                            int nd = Physics.RaycastNonAlloc(new Vector3(w.x, y + 2.5f, w.z), Vector3.down, hitBuf, 5f, ~0, QueryTriggerInteraction.Ignore);
                            float sy = float.NegativeInfinity;
                            for (int i = 0; i < nd; i++)
                                if (hitBuf[i].collider.name == "Roads" && Mathf.Abs(hitBuf[i].point.y - y) < 0.6f && hitBuf[i].point.y > sy) sy = hitBuf[i].point.y;
                            if (float.IsNegativeInfinity(sy)) continue;
                            // the trunk table: a capsule in the lane
                            if (world.Trunks != null)
                            {
                                trunkBuf.Clear();
                                world.Trunks.TableTrunksNear(new Vector3(w.x, sy, w.z), 1.5f, trunkBuf);
                                foreach (var tk in trunkBuf)
                                {
                                    float dd = new Vector2(tk.x - w.x, tk.z - w.z).magnitude;
                                    if (dd > tk.w + 0.3f || tk.y > sy + 1.0f || tk.y + world.Trunks.trunkHeight < sy + 0.2f) continue;
                                    long hk = ((long)Mathf.RoundToInt(tk.x * 10f) << 32) ^ (uint)Mathf.RoundToInt(tk.z * 10f);
                                    if (!seenHit.Add(hk)) continue;
                                    trunksIn++;
                                    AddNote(invisNotes, $"trunk {tk.x:0} {tk.z:0}", $"TRUNK {job.label}{(job.acc >= 0f ? $" at {job.acc + (job.dir > 0 ? s : e.length - s):0} m" : "")} e{e.index} '{e.name}'{(e.link ? " L" : "")}{(e.bridge ? " B" : "")} s={s:0}/{e.length:0} lat {lat:+0.0;-0.0}: trunk base ({tk.x:0.0},{tk.y:0.00},{tk.z:0.0}) r {tk.w:0.00}, lane surface {sy:0.00}");
                                }
                            }
                            // a ceiling under a car's roof: anything over the lane within 1.6 m
                            {
                                var o = new Vector3(w.x, sy + 0.15f, w.z);
                                rays++;
                                if (Physics.Raycast(o, Vector3.up, out var hu, 1.45f, ~0, QueryTriggerInteraction.Ignore))
                                {
                                    long cellU = ((long)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(hu.collider) << 32) ^ (uint)(Mathf.FloorToInt(hu.point.x) * 73856093 ^ Mathf.FloorToInt(hu.point.z) * 19349663) ^ 0x5555;
                                    if (seenHit.Add(cellU))
                                    {
                                        bool visU = DrawnNear(hu.point, Vector3.up, InvisTolM);
                                        string kdU = KindOf(hu.collider);
                                        string noteU = $"CEILING {job.label}{(job.acc >= 0f ? $" at {job.acc + (job.dir > 0 ? s : e.length - s):0} m" : "")} e{e.index} '{e.name}'{(e.link ? " L" : "")}{(e.bridge ? " B" : "")} s={s:0}/{e.length:0} lat {lat:+0.0;-0.0}: {PathOf(hu.collider)} [{kdU}] {hu.point.y - sy:0.00} m over the lane at ({hu.point.x:0.00},{hu.point.y:0.00},{hu.point.z:0.00}){Owner2(map, trims, hu.point, e.index)}{(hu.collider.name == "Barriers" ? RailOf(hu.point) : "")}";
                                        string keyU = $"{job.label} e{e.index} ceiling {kdU} {visU}";
                                        if (!visU) { invis++; Bump(invisByKind, "ceiling " + kdU); AddNote(invisNotes, keyU, "INVISIBLE " + noteU); }
                                        else { visIn++; Bump(visByKind, "ceiling " + kdU); AddNote(visNotes, keyU, "DRAWN " + noteU); }
                                    }
                                }
                            }
                            foreach (float hh in InvisHeights)
                            {
                                var o = new Vector3(w.x, sy + hh, w.z);
                                rays++;
                                Collider hc = null; Vector3 hp = default; Vector3 hn = Vector3.zero; bool inside = false;
                                int nh = Physics.RaycastNonAlloc(o, dir3, hitBuf, InvisRayM, ~0, QueryTriggerInteraction.Ignore);
                                float best = float.MaxValue;
                                for (int i = 0; i < nh; i++)
                                    if (hitBuf[i].distance < best)
                                    {
                                        best = hitBuf[i].distance; hc = hitBuf[i].collider; hp = hitBuf[i].point;
                                        hn = Vector3.zero;
                                        if (hc is MeshCollider hm && hm.sharedMesh != null && hm.sharedMesh.isReadable && hitBuf[i].triangleIndex >= 0)
                                        {
                                            var tv = hm.sharedMesh.triangles; var vv = hm.sharedMesh.vertices; int ti = hitBuf[i].triangleIndex * 3;
                                            if (ti + 2 < tv.Length)
                                            {
                                                var tr = hc.transform;
                                                Vector3 a = tr.TransformPoint(vv[tv[ti]]), b = tr.TransformPoint(vv[tv[ti + 1]]), c = tr.TransformPoint(vv[tv[ti + 2]]);
                                                hn = Vector3.Cross(b - a, c - a).normalized;
                                            }
                                        }
                                        else hn = hitBuf[i].normal;
                                    }
                                if (hc == null)
                                {
                                    int no = Physics.OverlapSphereNonAlloc(o, 0.05f, ovBuf, ~0, QueryTriggerInteraction.Ignore);
                                    for (int i = 0; i < no && hc == null; i++)
                                        if (!(ovBuf[i] is MeshCollider)) { hc = ovBuf[i]; hp = o; inside = true; }
                                }
                                if (hc == null) continue;
                                // a ray up a grade or a crest meets the lane's own road: not a wall
                                if (!inside && hc.name == "Roads" && hn.y > 0.85f) continue;
                                long cell = ((long)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(hc) << 32) ^ (uint)(Mathf.FloorToInt(hp.x) * 73856093 ^ Mathf.FloorToInt(hp.z) * 19349663);
                                bool fresh2 = seenHit.Add(cell);
                                bool front = !inside && hn != Vector3.zero && Vector3.Dot(hn, dir3) < -0.05f;
                                bool visible = !inside && ((SelfDrawn(hc) && front) || DrawnNear(hp, dir3, InvisTolM));
                                string kd = KindOf(hc);
                                string at = $"{job.label}{(job.acc >= 0f ? $" at {job.acc + (job.dir > 0 ? s : e.length - s):0} m" : "")} e{e.index} '{e.name}'{(e.link ? " L" : "")}{(e.bridge ? " B" : "")} s={s:0}/{e.length:0} dir {job.dir:+0;-0} lat {lat:+0.0;-0.0} (hw {hwL:0.0}/{hwR:0.0}) +{hh:0.0} m";
                                string what = $"{PathOf(hc)} [{kd}]{(inside ? " (ray starts inside it)" : "")} at ({hp.x:0.00},{hp.y:0.00},{hp.z:0.00}) {(hp - o).magnitude:0.00} m on, normal ({hn.x:0.00},{hn.y:0.00},{hn.z:0.00}){(front ? " front" : inside ? "" : " BACK")}{(SelfDrawn(hc) ? " drawn mesh" : " collider-only")}, lane surface {sy:0.00} (solve {y:0.00}){Owner2(map, trims, hp, e.index)}";
                                if (!visible)
                                {
                                    if (!fresh2) continue;
                                    invis++; Bump(invisByKind, kd);
                                    AddNote(invisNotes, $"{job.label} e{e.index} ray {kd} False", $"INVISIBLE {at}: {what}");
                                }
                                else
                                {
                                    if (!fresh2) continue;
                                    visIn++; Bump(visByKind, kd);
                                    AddNote(visNotes, $"{job.label} e{e.index} ray {kd} True", $"DRAWN {at}: {what}");
                                    WallInLane(job.label, e, s, p, right, hp, kd, $"{at}: {what}");
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                Physics.queriesHitBackfaces = backWas;
                CityMeshes.railLog = railWas;
                Object.DestroyImmediate(go);
            }
            string Kinds(Dictionary<string, int> d) { var sb = new StringBuilder(); foreach (var kv in d) sb.Append($" {kv.Key} {kv.Value};"); return sb.Length > 0 ? sb.ToString() : " none"; }
            Line($"  LANES: {stations} stations, {sweeps} car-box sweeps, {rays} rays; hits with no drawn face toward the car (INVISIBLE) {invis}:{Kinds(invisByKind)}; tree trunks in a lane {trunksIn}; drawn faces standing in a lane (reported) {visIn}:{Kinds(visByKind)}");
            Line($"  SOLIDS: {solidsChecked} samples of collider-only Solid-layer colliders, {solidsBare} with no drawn face within {InvisTolM * 100f:0} cm:{Kinds(bareByKind)}; meshes not readable (skipped) {unreadable}; {clock.Elapsed.TotalSeconds:0} s");
            Check(invis + trunksIn == 0, "no collider stands in a lane without a drawn face (INVISIBLE COLLIDERS: lanes)", invis + trunksIn);
            Line($"  WALLS IN LANES (race routes; a drawn barrier, rail or solid met more than a rail's 0.3 m inset inside the route edge's designed edge lines): {wallsInLanes} hits at {wallNotes.Count} places");
            foreach (var wn in wallNotes) Line("    " + wn);
            Check(wallsInLanes == 0, "no wall stands in a race route's lanes, between its designed edge lines (WALLS IN LANES)", wallsInLanes);
            Check(solidsBare == 0, "every Solid-layer collider has a drawn face within 5 cm (INVISIBLE COLLIDERS: solids)", solidsBare);
            var countOf = new Dictionary<string, int>();
            foreach (var kv in noteRuns) countOf[kv.Value.list[kv.Value.idx]] = kv.Value.n;
            string Counted(string t) => countOf.TryGetValue(t, out int n) && n > 1 ? $"{t} [x{n} on this edge]" : t;
            foreach (var s in invisNotes) Line("    " + Counted(s));
            foreach (var s in bareNotes) Line("    " + s);
            foreach (var s in visNotes) Line("    " + Counted(s));
        }

        /// <summary>The nearest other edge to a point, for a note.</summary>
        static string Owner2(CityMap map, CityMeshes.Trims trims, Vector3 at, int notEdge)
        {
            var p2 = new Vector2(at.x, at.z);
            var near = new HashSet<int>();
            map.EdgeSegsInRect(p2 - Vector2.one * 25f, p2 + Vector2.one * 25f, near);
            float bd = float.MaxValue; int bi = -1; float bs = 0f;
            foreach (var packed in near)
            {
                int oi = packed >> 12, si = packed & 0xFFF;
                if (oi == notEdge) continue;
                var o = map.edges[oi];
                Vector2 q0 = o.pts[si], dq = o.pts[si + 1] - q0;
                float L2 = dq.sqrMagnitude;
                float tt = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p2 - q0, dq) / L2) : 0f;
                float dd = Vector2.Distance(p2, q0 + dq * tt);
                if (dd < bd) { bd = dd; bi = oi; bs = o.s[si] + Mathf.Sqrt(L2) * tt; }
            }
            if (bi < 0) return "";
            var oe = map.edges[bi];
            return $"; nearest other e{bi} '{oe.name}'{(oe.link ? " L" : "")}{(oe.bridge ? " B" : "")} at {bd:0.0} m, its y {oe.YAt(bs):0.00}";
        }
    }
}
