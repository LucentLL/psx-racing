using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// WHAT STANDS IN A JUNCTION (Five Points, 2026-11-03). The owner was
    /// launched by "a curb [that] juts up from the ground" inside Beatties Ford
    /// x W Trade. This stands the tiles round a world point up as the game
    /// does (CityMeshes.Build + CityWorld.Attach, the 5x5 round it) and maps
    /// the first surface a wheel's down ray meets: per cell its collider, its
    /// submesh's slot, its height against the highest ROAD surface under it,
    /// and, for land, the strip that laid it (CityMeshes.groundLog). PSX_JPROBE
    /// "x:z:half:step[;...]". Writes city_junction_probe.txt (+ .csv).
    /// </summary>
    public static class CityJunctionProbe
    {
        [MenuItem("PSX Racing/Probe Charlotte Junction Surface")]
        public static void Run()
        {
            var sb = new StringBuilder();
            var csv = new StringBuilder("x,z,top,slot,topY,roadY,dy,tag\n");
            var map = CityMap.Get();
            var inv = CultureInfo.InvariantCulture;
            var trims = CityMeshes.ComputeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            foreach (var item in (System.Environment.GetEnvironmentVariable("PSX_JPROBE") ?? "").Split(';'))
            {
                var f = item.Trim().Split(':');
                if (f.Length < 4) continue;
                Probe(map, trims, buildings, float.Parse(f[0], inv), float.Parse(f[1], inv), float.Parse(f[2], inv), float.Parse(f[3], inv), sb, csv);
            }
            // PSX_JPROBE_PAVE="x0:z0:x1:z1:step[;...]": every drawn pavement over
            // each point along the line (ribbon outlines and fans, with heights)
            foreach (var item in (System.Environment.GetEnvironmentVariable("PSX_JPROBE_PAVE") ?? "").Split(';'))
            {
                var f = item.Trim().Split(':');
                if (f.Length < 5) continue;
                Vector2 a = new Vector2(float.Parse(f[0], inv), float.Parse(f[1], inv)), b = new Vector2(float.Parse(f[2], inv), float.Parse(f[3], inv));
                float st = float.Parse(f[4], inv);
                int cnt = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / st));
                sb.AppendLine($"=== PAVEMENT along ({a.x:0.00},{a.y:0.00}) -> ({b.x:0.00},{b.y:0.00})");
                for (int i = 0; i <= cnt; i++)
                {
                    var q = Vector2.Lerp(a, b, i / (float)cnt);
                    sb.AppendLine($"  ({q.x:0.00},{q.y:0.00}){CityMeshes.DebugPavementAt(map, trims, q)}");
                }
            }
            // PSX_JPROBE_EDGES="e1,e2,...": each edge's line model every 2 m (the
            // drawn ribbon's place against OSM's line: shift, extents, lanes)
            foreach (var t in (System.Environment.GetEnvironmentVariable("PSX_JPROBE_EDGES") ?? "").Split(','))
            {
                if (!int.TryParse(t.Trim(), out int ei) || ei < 0 || ei >= map.edges.Length) continue;
                var e = map.edges[ei];
                sb.AppendLine($"=== EDGE e{ei} '{e.name}' n{e.a}->n{e.b} len {e.length:0.0} w {e.width:0.00} lanes {e.lanes} oneway {e.oneway} link {e.link} turnLane {e.turnLane} trims {trims.atA[ei]:0.0}|{trims.atB[ei]:0.0} patch {trims.patch[e.a]}|{trims.patch[e.b]} branch {trims.branchA[ei]}|{trims.branchB[ei]}");
                for (float s = 0f; s <= e.length + 0.01f; s += 2f)
                {
                    float sc = Mathf.Min(s, e.length);
                    var p = e.PointAt(sc); var l = LineModel.SectionLeft(e, sc);
                    LineModel.Extents(e, sc, out float em, out float ep);
                    var lp = LineModel.LanePoint(e, sc);
                    sb.AppendLine($"  s {sc,5:0.0} osm ({p.x:0.00},{p.y:0.00}) left ({l.x:0.00},{l.y:0.00}) shift {LineModel.ShiftAt(e, sc):0.00} ext -{em:0.00}/+{ep:0.00} lane ({lp.x:0.00},{lp.y:0.00}) centre {LineModel.LaneCentre(e, sc):0.00} y {e.YAt(sc):0.000} station {e.StationYAt(sc):0.000}");
                }
                { var st = new StringBuilder(" stations:"); for (int k = 0; k < e.stS.Length; k++) st.Append($" {e.stS[k]:0.0}:{e.stY[k]:0.000}"); sb.AppendLine(st.ToString()); }
                sb.AppendLine(" drawn outline:");
                sb.Append(CityMeshes.DebugOutline(map, trims, ei));
                CityMeshes.EnsureMarks(map, trims);
                var ml = CityMeshes.MarksOf(ei);
                sb.AppendLine($" jcl {CityMeshes.IsJcl(map, ei)}; marks {(ml == null ? 0 : ml.Count)}");
                if (ml != null) foreach (var m in ml) sb.AppendLine($"   mark {CityMeshes.MarkNames[m.style]} s {m.s0:0.00}..{m.s1:0.00} k {m.k} '{m.what}'{(m.yellow ? " yellow" : "")}");
            }
            // PSX_JPROBE_NODES="n1,n2,...": each node as the tile builder draws it
            foreach (var t in (System.Environment.GetEnvironmentVariable("PSX_JPROBE_NODES") ?? "").Split(','))
                if (int.TryParse(t.Trim().TrimStart('n'), out int nd) && nd >= 0 && nd < map.nodes.Length)
                {
                    sb.AppendLine($"=== NODE n{nd} at ({map.nodes[nd].x:0.00},{map.nodes[nd].y:0.00}) " + CityMeshes.DescribeNode(map, trims, nd));
                    sb.AppendLine("  " + CityMeshes.DebugThrough(map, trims, nd));
                }
            CityMeshes.EnsureJcl(map);
            sb.AppendLine($"JUNCTION-COMPLEX LINKS ({(CityMeshes.JclOn ? "ON" : "OFF")}): {CityMeshes.JclCount}, {CityMeshes.JclMetres:0} m");
            foreach (var l in CityMeshes.JclAt) sb.AppendLine("  jcl: " + l);
            CurbPastLines(sb);
            sb.AppendLine($"MITRES DRAWN AS FANS ({(CityMeshes.MitreFanOn ? "ON" : "OFF")}): {CityMeshes.MitreFanCount}");
            foreach (var l in CityMeshes.MitreFanAt) sb.AppendLine("  mitrefan: " + l);
            sb.AppendLine($"  branch pairs drawn as fan arms there ({(CityMeshes.MitreFanUnclipOn ? "ON" : "OFF")}): {CityMeshes.MitreFanUnclipped}");
            {
                var js = CityMeshes.JStats;
                var tl = new List<string>(); var cl = new List<CityMeshes.CurbRec>();
                CityMeshes.ThruNoseLog = tl; CityMeshes.CurbLog = cl;
                try { for (int fn = 0; fn < map.nodes.Length; fn++) if (trims.patch[fn] && CityMeshes.FanKey(trims, fn) == fn) CityMeshes.DescribeFan(map, trims, fn); }
                finally { CityMeshes.ThruNoseLog = null; CityMeshes.CurbLog = null; }
                int past = 0; foreach (var l in tl) if (l.EndsWith("PAST A NODE")) past++;
                sb.AppendLine($"ONE-WAY THROUGH CORNERS NOT A MEDIAN NOSE ({(CityMeshes.ThruNoNoseOn ? "ON" : "OFF")}): {tl.Count} fan inside corners city-wide ({past} with the edge lines meeting past a node); trims pass {js.thruNoses} corners, {js.thruNoseGrew} grown by the old nose");
                foreach (var l in tl) sb.AppendLine("  thrunose: " + l);
                foreach (var l in js.thruNoseAt) sb.AppendLine("  thrunose-trim: " + l);
            }
            CityMeshes.EnsureMarks(map, trims);
            { var ms = CityMeshes.MarkStats; sb.AppendLine($"MOUTHS: {ms.mouths} marked ({ms.mouthsDotted} dotted, {ms.mouthsDottedYellow} of them yellow; the rest stop); shallow joins outside the dotted scope left as before: {ms.mouthsDottedOut}; skipped {ms.mouthsSkipped}"); }
            sb.AppendLine($"FAN DRAPE ({(CityMeshes.FanDrapeOn ? "ON" : "OFF")}): {CityMeshes.FanDrapeN} ribbon vertices put on a fan's height, largest move {CityMeshes.FanDrapeMaxMoved:0.000} m");
            sb.AppendLine($"BRANCH APRONS ({(CityMeshes.BranchApronOn ? "ON" : "OFF")}): {CityMeshes.BranchApronN}, widest {CityMeshes.BranchApronMaxM:0.00} m");
            foreach (var l in CityMeshes.BranchApronAt) sb.AppendLine("  " + l);
            sb.AppendLine($"ISLAND NOSES ({(CityMeshes.IslandNoseOn ? "ON" : "OFF")}, R {CityMeshes.IslandNoseR:0.0} m): {CityMeshes.IslandNoseAt.Count}");
            foreach (var l in CityMeshes.IslandNoseAt) sb.AppendLine("  nose: " + l);
            sb.AppendLine($"SLIP CURVE WIDENING ({(CityMeshes.SlipWidenOn ? "ON" : "OFF")}, K {CityMeshes.SlipWidenK:0.0} m2): {CityMeshes.SlipWidenAt.Count}");
            foreach (var l in CityMeshes.SlipWidenAt) sb.AppendLine("  widen: " + l);
            sb.AppendLine($"BRANCH OVERHANGS LEFT: {CityMeshes.BranchApronLeft}");
            foreach (var l in CityMeshes.BranchApronLeftAt) sb.AppendLine("  left: " + l);
            sb.AppendLine("WALKS (the tiles this probe built): " + CityMeshes.WalkStats.Line());
            foreach (var kv in CityMeshes.WalkStats.medianSkipAt) sb.AppendLine("  median skip: " + kv.Value);
            string root = System.IO.Directory.GetParent(Application.dataPath).FullName;
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "city_junction_probe.txt"), sb.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "city_junction_probe.csv"), csv.ToString());
            Debug.Log("[CityJunctionProbe] wrote city_junction_probe.txt");
        }

        static void Probe(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings,
                          float x, float z, float half, float step, StringBuilder sb, StringBuilder csv)
        {
            int tx = Mathf.FloorToInt(x / CityMeshes.TileSize), tz = Mathf.FloorToInt(z / CityMeshes.TileSize);
            var root = new GameObject("~jprobe");
            var built = new List<Mesh>();
            var slotsOf = new Dictionary<Collider, CityMeshes.Slot[]>();
            var meshOf = new Dictionary<Collider, Mesh>();
            CityMeshes.groundLog = new List<(string, Vector3, Vector3, Vector3)>();
            try
            {
                for (int pass = 0; pass < 2; pass++)
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            bool centre = dx == 0 && dz == 0;
                            if (centre != (pass == 1)) continue;
                            var tm = CityMeshes.Build(map, trims, buildings, tx + dx, tz + dz);
                            var go = new GameObject($"tile_{tx + dx}_{tz + dz}");
                            go.transform.SetParent(root.transform, false);
                            go.transform.position = tm.origin;
                            built.AddRange(CityWorld.Attach(go, tm, null));
                            foreach (var c in go.GetComponentsInChildren<MeshCollider>())
                            {
                                meshOf[c] = c.sharedMesh;
                                if (c.name == "Ground") slotsOf[c] = tm.groundSlots;
                                else if (c.name == "Roads") slotsOf[c] = tm.roadSlots;
                            }
                        }
                Physics.SyncTransforms();
                int n = Mathf.Clamp(Mathf.RoundToInt(half / step), 1, 200);
                bool tagAll = System.Environment.GetEnvironmentVariable("PSX_JPROBE_TAGALL") == "1";
                sb.AppendLine($"=== JUNCTION round ({x:0.00},{z:0.00}), {step:0.00} m cells, x {x - n * step:0.00}..{x + n * step:0.00}, z north up");
                sb.AppendLine("  '=' road on top; ground on top: 'o' within 3 cm of the road under it, '1' 3-10 cm over, '2' 10-25 cm, '3' over 25 cm, 'l' lower; 'g' ground with no road under; 'B' barrier, 'X' other solid");
                var worst = new List<(float dy, float qx, float qz, string what)>();
                for (int j = n; j >= -n; j--)
                {
                    var row = new StringBuilder($"  {z + j * step,9:0.00} ");
                    for (int i = -n; i <= n; i++)
                    {
                        float qx = x + i * step, qz = z + j * step;
                        RaycastHit top = default, road = default; bool ft = false, fr = false;
                        foreach (var h in Physics.RaycastAll(new Vector3(qx, 400f, qz), Vector3.down, 800f, ~0, QueryTriggerInteraction.Ignore))
                        {
                            if (!ft || h.point.y > top.point.y) { top = h; ft = true; }
                            if (h.collider.name == "Roads" && (!fr || h.point.y > road.point.y)) { road = h; fr = true; }
                        }
                        char c = ' ';
                        string slot = "", tag = "";
                        float dy = 0f;
                        if (ft)
                        {
                            string nm = top.collider.name;
                            slot = SlotAt(top, slotsOf, meshOf);
                            if (nm == "Roads") c = '=';
                            else if (nm == "Barriers" || nm == "Guardrail") c = 'B';
                            else if (nm == "Ground")
                            {
                                if (!fr) { c = 'g'; if (tagAll) tag = TagAt(top.point); }
                                else
                                {
                                    dy = top.point.y - road.point.y;
                                    c = dy < -0.03f ? 'l' : dy <= 0.03f ? 'o' : dy <= 0.10f ? '1' : dy <= 0.25f ? '2' : '3';
                                    if (dy > 0.03f && dy < 3f)
                                    {
                                        tag = TagAt(top.point);
                                        worst.Add((dy, qx, qz, $"{slot} {tag}"));
                                    }
                                    else if (tagAll && dy > 0f) tag = TagAt(top.point);
                                }
                            }
                            else c = 'X';
                            csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:0.00},{1:0.00},{2},{3},{4:0.000},{5},{6:0.000},\"{7}\"",
                                qx, qz, nm, slot, top.point.y, fr ? road.point.y.ToString("0.000", CultureInfo.InvariantCulture) : "", dy, tag));
                        }
                        row.Append(c);
                    }
                    sb.AppendLine(row.ToString());
                }
                worst.Sort((a, b) => b.dy.CompareTo(a.dy));
                sb.AppendLine($"  ground over a road by more than 3 cm: {worst.Count} cells; fan chords facing pavement so far (PSX_CITY_CHORD_PAVED {(CityMeshes.ChordPavedOn ? "on" : "off")}): {CityMeshes.chordPavedSkipped} left bare, {CityMeshes.chordPavedCut} cut back to their bare part");
                var seenTag = new Dictionary<string, int>();
                foreach (var w in worst) { seenTag.TryGetValue(w.what, out int k); seenTag[w.what] = k + 1; }
                foreach (var kv in seenTag) sb.AppendLine($"    {kv.Value,5} cells  {kv.Key}");
                foreach (var l in CityMeshes.ChordPavedAt) sb.AppendLine("    chord " + l);
                CityMeshes.ChordPavedAt.Clear();
                for (int k = 0; k < Mathf.Min(40, worst.Count); k++)
                    sb.AppendLine($"    {worst[k].dy:+0.000} at ({worst[k].qx:0.00},{worst[k].qz:0.00}) {worst[k].what}");
                // the junction nodes in the box
                for (int ni = 0; ni < map.nodes.Length; ni++)
                {
                    var p = map.nodes[ni];
                    if (Mathf.Abs(p.x - x) > half || Mathf.Abs(p.y - z) > half) continue;
                    sb.AppendLine($"  node {ni} ({p.x:0.0},{p.y:0.0}) y {map.nodeY[ni]:0.000}");
                }
                sb.AppendLine();
            }
            finally
            {
                CityMeshes.groundLog = null;
                foreach (var m in built) if (m != null) Object.DestroyImmediate(m);
                Object.DestroyImmediate(root);
            }
        }

        // ------------------------------------------------------------------
        //  THE JUNCTION WHEEL-TRACK SWEEP (Five Points, 2026-11-03). The drive
        //  audit's boxed runs drive PSX_DRIVE_EDGES only, and the launch audit
        //  reads each movement's CENTRE line (a 1 m profile, judged as a
        //  ballistic arc): a kerbed hump standing beside the line, where a
        //  car's wheels go, was in neither. Here every movement through every
        //  junction node (3+ arms; in-arm -> out-arm as it may be driven, no
        //  U-turn) is a smooth path from 6 m before the in-arm's trim to 6 m
        //  past the out-arm's, in its lane (a one-way's middle, a two-way's
        //  right half), and both WHEEL TRACKS (0.8 m either side) are read
        //  every 0.1 m on the built colliders (every tile once, its ring
        //  standing). A STEP is a change of over 3 cm between two readings a
        //  tenth of a metre apart (a 12% grade moves 1.2 cm): a kerb, a raised
        //  fill, a hole, a seam. Writes city_junction_steps.txt / .csv.
        //  PSX_JSWEEP_BOX=x0,z0,x1,z1 limits it.
        // ------------------------------------------------------------------
        const float SweepStepM = 0.1f, SweepTrackM = 0.8f, SweepRunM = 6f, SweepGateM = 0.03f;

        struct Move { public int node, ein, eout; public Vector2[] p; }

        /// <summary>The curb returns past the node the tiles built so far moved
        /// (CityMeshes.CurbPastOn): count and list.</summary>
        static void CurbPastLines(StringBuilder sb)
        {
            var l = new List<string>();
            lock (CityMeshes.CurbPastAt) l.AddRange(CityMeshes.CurbPastAt.Values);
            l.Sort(System.StringComparer.Ordinal);
            sb.AppendLine($"CURB RETURNS PAST THE NODE ({(CityMeshes.CurbPastOn ? "ON" : "OFF")}): {l.Count} tangent points moved over 5 mm (tiles built here)");
            foreach (var t in l) sb.AppendLine("  curbpast: " + t);
            sb.AppendLine($"BESIDE LINKS SEATED ({(CityMeshes.BesideSeatOn ? "ON" : "OFF")}): {CityMeshes.BesideSeatAt.Count}");
            foreach (var t in CityMeshes.BesideSeatAt) sb.AppendLine("  besideseat: " + t);
        }

        [MenuItem("PSX Racing/Sweep Charlotte Junction Wheel Tracks")]
        public static void Sweep()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var map = CityMap.Get();
            var trims = CityMeshes.ComputeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            var inv = CultureInfo.InvariantCulture;
            Rect box = Rect.MinMaxRect(-1e9f, -1e9f, 1e9f, 1e9f);
            var bx = System.Environment.GetEnvironmentVariable("PSX_JSWEEP_BOX");
            if (!string.IsNullOrEmpty(bx)) { var f = bx.Split(','); box = Rect.MinMaxRect(float.Parse(f[0], inv), float.Parse(f[1], inv), float.Parse(f[2], inv), float.Parse(f[3], inv)); }
            // the movements, by the tile their node stands in
            var byTile = new Dictionary<long, List<Move>>();
            int nMoves = 0, nNodes = 0;
            int tx0 = int.MaxValue, tx1 = int.MinValue, tz0 = int.MaxValue, tz1 = int.MinValue;
            for (int n = 0; n < map.nodes.Length; n++)
            {
                var arms = map.nodeEdges[n];
                if (arms == null || arms.Count < 3 || !box.Contains(map.nodes[n])) continue;
                bool any = false;
                foreach (int ia in arms)
                    foreach (int oa in arms)
                    {
                        if (ia == oa) continue;
                        var ei = map.edges[ia]; var eo = map.edges[oa];
                        if (ei.a == ei.b || eo.a == eo.b || ei.tunnel || eo.tunnel) continue;
                        if (ei.oneway && ei.b != n) continue;   // must be driven INTO n
                        if (eo.oneway && eo.a != n) continue;   // and OUT of it
                        if (ei.ElevatedAt(ei.a == n ? 0f : ei.length) || eo.ElevatedAt(eo.a == n ? 0f : eo.length)) continue;
                        var p = MovePath(map, trims, n, ei, eo);
                        if (p == null) continue;
                        int tx = Mathf.FloorToInt(map.nodes[n].x / CityMeshes.TileSize), tz = Mathf.FloorToInt(map.nodes[n].y / CityMeshes.TileSize);
                        long key = ((long)tx << 32) ^ (uint)tz;
                        if (!byTile.TryGetValue(key, out var l)) byTile[key] = l = new List<Move>();
                        l.Add(new Move { node = n, ein = ia, eout = oa, p = p });
                        tx0 = Mathf.Min(tx0, tx); tx1 = Mathf.Max(tx1, tx); tz0 = Mathf.Min(tz0, tz); tz1 = Mathf.Max(tz1, tz);
                        nMoves++; any = true;
                    }
                if (any) nNodes++;
            }
            var sb = new StringBuilder();
            var csv = new StringBuilder("node,ein,eout,x,z,step,track,hi,hiSlot,lo,loSlot,lat\n");
            sb.AppendLine($"JUNCTION WHEEL-TRACK SWEEP: {nMoves} movements through {nNodes} junction nodes, {byTile.Count} tiles; median walks {(CityMeshes.MedianNoWalkOn ? "kept out (fix ON)" : "laid (PSX_CITY_MEDIAN_NOWALK=0)")}");
            var root = new GameObject("~jsweep");
            var live = new Dictionary<long, (GameObject go, Mesh[] meshes, Dictionary<Collider, CityMeshes.Slot[]> slots)>();
            var flagged = new List<(float step, string line, int node)>();
            var flaggedRor = new List<string>();
            int movesBad = 0;
            var badNodes = new HashSet<int>();
            var slotsOf = new Dictionary<Collider, CityMeshes.Slot[]>();
            var meshOf = new Dictionary<Collider, Mesh>();
            try
            {
                // row by row: tile (x, z) is read once rows z-1..z+1 stand
                for (int z = tz0 - 1; z <= tz1 + 1; z++)
                {
                    for (int x = tx0 - 1; x <= tx1 + 1; x++)
                    {
                        long key = ((long)x << 32) ^ (uint)(z);
                        bool need = false;
                        for (int dz = -1; dz <= 1 && !need; dz++) for (int dx = -1; dx <= 1 && !need; dx++) need = byTile.ContainsKey(((long)(x + dx) << 32) ^ (uint)(z + dz));
                        if (!need) continue;
                        var tm = CityMeshes.Build(map, trims, buildings, x, z);
                        var go = new GameObject($"tile_{x}_{z}");
                        go.transform.SetParent(root.transform, false);
                        go.transform.position = tm.origin;
                        var ms = CityWorld.Attach(go, tm, null);
                        foreach (var c in go.GetComponentsInChildren<MeshCollider>())
                        {
                            meshOf[c] = c.sharedMesh;
                            if (c.name == "Ground") slotsOf[c] = tm.groundSlots;
                            else if (c.name == "Roads") slotsOf[c] = tm.roadSlots;
                        }
                        live[key] = (go, ms, null);
                    }
                    Physics.SyncTransforms();
                    // the row below the one just built has its ring now
                    int rz = z - 1;
                    for (int x = tx0; x <= tx1; x++)
                    {
                        if (!byTile.TryGetValue(((long)x << 32) ^ (uint)rz, out var moves)) continue;
                        foreach (var mv in moves)
                        {
                            float worst = 0f; string worstLine = null;
                            float worstRor = 0f; string worstRorLine = null;
                            for (int tr = -1; tr <= 1; tr += 2)
                            {
                                float lastY = float.NaN; string lastC = "", lastS = "";
                                for (int k = 0; k < mv.p.Length; k++)
                                {
                                    var c = mv.p[k];
                                    var d = k + 1 < mv.p.Length ? mv.p[k + 1] - c : c - mv.p[k - 1];
                                    d.Normalize();
                                    var q = c + new Vector2(d.y, -d.x) * (tr * SweepTrackM);
                                    if (!Physics.Raycast(new Vector3(q.x, 600f, q.y), Vector3.down, out var h, 1200f, ~(1 << 2), QueryTriggerInteraction.Ignore)) { lastY = float.NaN; continue; }
                                    string cn = h.collider.name, sl = SlotAt(h, slotsOf, meshOf);
                                    if (!float.IsNaN(lastY))
                                    {
                                        float st = Mathf.Abs(h.point.y - lastY);
                                        // (night 11-06) road over road, its own worst: a larger step onto land must not hide it
                                        if (cn == "Roads" && lastC == "Roads" && st > worstRor)
                                        {
                                            worstRor = st;
                                            bool upR = h.point.y > lastY;
                                            worstRorLine = string.Format(inv, "{0:0.00},{1:0.00},{2:0.000},{3},{4},{5},{6},{7}",
                                                q.x, q.y, st, tr < 0 ? "left" : "right", upR ? cn : lastC, upR ? sl : lastS, upR ? lastC : cn, upR ? lastS : sl);
                                        }
                                        if (st > worst)
                                        {
                                            worst = st;
                                            bool up = h.point.y > lastY;
                                            worstLine = string.Format(inv, "{0:0.00},{1:0.00},{2:0.000},{3},{4},{5},{6},{7}",
                                                q.x, q.y, st, tr < 0 ? "left" : "right", up ? cn : lastC, up ? sl : lastS, up ? lastC : cn, up ? lastS : sl);
                                        }
                                    }
                                    lastY = h.point.y; lastC = cn; lastS = sl;
                                }
                            }
                            if (worstRor > SweepGateM && worstRorLine != null) flaggedRor.Add($"{mv.node},{mv.ein},{mv.eout},{worstRorLine}");
                            if (worst > SweepGateM && worstLine != null)
                            {
                                movesBad++; badNodes.Add(mv.node);
                                flagged.Add((worst, $"{mv.node},{mv.ein},{mv.eout},{worstLine}", mv.node));
                            }
                        }
                    }
                    // drop the row that no reading needs any more
                    int dropZ = z - 2;
                    for (int x = tx0 - 1; x <= tx1 + 1; x++)
                    {
                        long key = ((long)x << 32) ^ (uint)dropZ;
                        if (!live.TryGetValue(key, out var t)) continue;
                        foreach (var c in t.go.GetComponentsInChildren<MeshCollider>()) { slotsOf.Remove(c); meshOf.Remove(c); }
                        foreach (var m in t.meshes) if (m != null) Object.DestroyImmediate(m);
                        Object.DestroyImmediate(t.go);
                        live.Remove(key);
                    }
                }
            }
            finally
            {
                foreach (var t in live.Values) { foreach (var m in t.meshes) if (m != null) Object.DestroyImmediate(m); if (t.go != null) Object.DestroyImmediate(t.go); }
                Object.DestroyImmediate(root);
            }
            flagged.Sort((a, b) => b.step.CompareTo(a.step));
            // by what the high side is
            var byKind = new Dictionary<string, int>();
            foreach (var f in flagged)
            {
                var c = f.line.Split(',');
                string k = $"{c[7]}/{c[8]} over {c[9]}/{c[10]}";
                byKind.TryGetValue(k, out int v); byKind[k] = v + 1;
            }
            sb.AppendLine($"movements with a wheel-track step over {SweepGateM * 100f:0} cm: {movesBad} of {nMoves}, at {badNodes.Count} nodes; over 10 cm: {flagged.FindAll(f => f.step > 0.10f).Count}; over 15 cm: {flagged.FindAll(f => f.step > 0.15f).Count}");
            sb.AppendLine("by the high side over the low (collider/slot):");
            var kinds = new List<KeyValuePair<string, int>>(byKind); kinds.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (var kv in kinds) sb.AppendLine($"  {kv.Value,6}  {kv.Key}");
            sb.AppendLine("worst 80 (node, in edge, out edge, x, z, step m, track, high side, low side):");
            for (int i = 0; i < Mathf.Min(80, flagged.Count); i++) sb.AppendLine("  " + flagged[i].line + "  " + CityAudit.LatLon(float.Parse(flagged[i].line.Split(',')[3], inv), float.Parse(flagged[i].line.Split(',')[4], inv)));
            foreach (var f in flagged) csv.AppendLine(f.line);
            CurbPastLines(sb);
            // (wedlane 11-06) the lane drops the line model finds city-wide, and those held to the node
            sb.AppendLine($"BRANCH DROPS ({(LineModel.BranchDropOn ? (LineModel.BranchDropAll ? "ALL" : "SCOPED") : "OFF")}): {LineModel.BranchDropFound} found city-wide, {LineModel.BranchDropHeld} held to the node");
            foreach (var t in LineModel.BranchDropLog) sb.AppendLine("  branchdrop: " + t);
            sb.AppendLine($"{clock.Elapsed.TotalSeconds:0} s");
            string rootDir = System.IO.Directory.GetParent(Application.dataPath).FullName;
            System.IO.File.WriteAllText(System.IO.Path.Combine(rootDir, "city_junction_steps.txt"), sb.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(rootDir, "city_junction_steps.csv"), csv.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(rootDir, "city_junction_steps_ror.csv"), "node,ein,eout,x,z,step,track,hi,hiSlot,lo,loSlot\n" + string.Join("\n", flaggedRor) + "\n");
            Debug.Log("[JunctionSweep]\n" + sb);
        }

        /// <summary>A movement's smooth path through node n, every SweepStepM:
        /// SweepRunM before the in-arm's trim to SweepRunM past the out-arm's,
        /// in the lane; null for a U-turn or an arm too short.</summary>
        static Vector2[] MovePath(CityMap map, CityMeshes.Trims trims, int n, CityMap.Edge ei, CityMap.Edge eo)
        {
            float ti = trims.TrimAt(ei, n), to = trims.TrimAt(eo, n);
            float si = ei.b == n ? ei.length - ti - SweepRunM : ti + SweepRunM;
            float so = eo.a == n ? to + SweepRunM : eo.length - to - SweepRunM;
            if (si < 0f || si > ei.length || so < 0f || so > eo.length) return null;
            var tIn = ei.TangentAt(si) * (ei.b == n ? 1f : -1f);    // travel
            var tOut = eo.TangentAt(so) * (eo.a == n ? 1f : -1f);
            if (Vector2.Dot(tIn, tOut) < -0.85f) return null;        // a U-turn
            var p0 = ei.PointAt(si) + new Vector2(tIn.y, -tIn.x) * (ei.oneway ? 0f : ei.width * 0.25f);
            var p3 = eo.PointAt(so) + new Vector2(tOut.y, -tOut.x) * (eo.oneway ? 0f : eo.width * 0.25f);
            float d = Vector2.Distance(p0, p3) * 0.4f;
            var p1 = p0 + tIn * d; var p2 = p3 - tOut * d;
            // arc length ~ chord * 1.3 at most
            int steps = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(p0, p3) * 1.4f / SweepStepM), 4, 4000);
            var raw = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps, u = 1f - t;
                raw[i] = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
            }
            // even SweepStepM spacing
            var even = new List<Vector2> { raw[0] };
            float carry = 0f;
            for (int i = 1; i < raw.Length; i++)
            {
                var a = raw[i - 1]; var b = raw[i]; float L = Vector2.Distance(a, b);
                float at = SweepStepM - carry;
                while (at <= L) { even.Add(Vector2.Lerp(a, b, at / L)); at += SweepStepM; }
                carry = L - (at - SweepStepM);
            }
            return even.ToArray();
        }

        static string SlotAt(RaycastHit h, Dictionary<Collider, CityMeshes.Slot[]> slotsOf, Dictionary<Collider, Mesh> meshOf)
        {
            if (!slotsOf.TryGetValue(h.collider, out var slots) || !meshOf.TryGetValue(h.collider, out var mesh) || mesh == null) return "";
            int ti = h.triangleIndex * 3;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var d = mesh.GetSubMesh(s);
                if (ti >= d.indexStart && ti < d.indexStart + d.indexCount) return s < slots.Length ? slots[s].ToString() : "sub" + s;
            }
            return "?";
        }

        static string TagAt(Vector3 p)
        {
            if (CityMeshes.groundLog == null) return "";
            string best = "the lattice"; float bestDy = 0.03f;
            foreach (var (tag, a, b, c) in CityMeshes.groundLog)
            {
                if (!InTri(new Vector2(p.x, p.z), new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z), out float u, out float v)) continue;
                float y = a.y + (b.y - a.y) * u + (c.y - a.y) * v;
                float d = Mathf.Abs(y - p.y);
                if (d < bestDy) { bestDy = d; best = tag; }
            }
            return best;
        }

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float u, out float v)
        {
            Vector2 v0 = b - a, v1 = c - a, v2 = p - a;
            float den = v0.x * v1.y - v1.x * v0.y;
            u = v = 0f;
            if (Mathf.Abs(den) < 1e-9f) return false;
            u = (v2.x * v1.y - v1.x * v2.y) / den;
            v = (v0.x * v2.y - v2.x * v0.y) / den;
            return u >= -1e-4f && v >= -1e-4f && u + v <= 1f + 1e-4f;
        }
    }
}
