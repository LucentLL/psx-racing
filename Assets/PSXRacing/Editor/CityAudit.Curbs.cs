using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CURBS AUDIT (curbs 3, 2026-11-15):
    ///   1. city-wide, from the rule alone: km of street by edge type (none /
    ///      roll curb alone / rolled walk / vertical curb and walk), before
    ///      (PSX_CITY_CURBS3=0) and after;
    ///   2. on a sample of tiles built both ways (PSX_CURBS_BOX "tx0,tz0,tx1,tz1"
    ///      inclusive, default the 8 x 8 tiles round West Trade St at Beatties
    ///      Ford Rd; plus one Uptown, one subdivision, one rural and one
    ///      interchange tile): inlets by type, the driveway mouths the walk
    ///      spans cross (over a roll, through a cut, or with a curb face
    ///      across), triangles, vertices and tile build milliseconds.
    ///   -executeMethod PSXRacing.EditorTools.CityAudit.CurbsOnly
    /// Output: curbs_audit.txt in the project root.
    /// </summary>
    public static partial class CityAudit
    {
        static void CurbsDestroy(CityMeshes.TileMeshes tm)
        {
            if (tm == null) return;
            foreach (var f in typeof(CityMeshes.TileMeshes).GetFields())
                if (f.FieldType == typeof(Mesh) && f.GetValue(tm) is Mesh m && m != null) Object.DestroyImmediate(m);
        }

        public static void CurbsOnly()
        {
            var sb = new StringBuilder();
            int code = 0;
            try
            {
                var map = CityMap.Get();
                if (map == null) throw new System.Exception("no city data");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var trims = CityMeshes.NodeTrims(map);
                var proc = CityBuildings.Precompute(map);
                CityHouses.Bind(map, trims, proc);
                sb.AppendLine($"CURBS AUDIT {System.DateTime.Now:yyyy-MM-dd HH:mm}; graph hash {map.graphHash:x8} (GHSH), {map.edges.Length} edges; trims + buildings {sw.ElapsedMilliseconds} ms");

                // ---- 1. the rule, city-wide ----
                foreach (bool after in new[] { false, true })
                {
                    CityMeshes.Curbs3On = after; CityMeshes.Inlet2On = after; CityMeshes.Curbs3Reset();
                    sw.Restart();
                    // [street class group: 0 local, 1 collector/arterial][type: 0 none, 1 roll alone, 2 rolled walk, 3 vertical]
                    var km = new double[2, 4]; double notStreet = 0, narrow = 0;
                    foreach (var e in map.edges)
                    {
                        if (e.link || e.cls > 3 || e.tunnel || e.bridge || e.a == e.b) { notStreet += e.length; continue; }
                        if (e.width < (e.oneway ? 4f : 6f)) { narrow += e.length; continue; }
                        int type = !CityMeshes.WalkEdge(map, e) ? 0 : CityMeshes.CurbOnly(map, e) ? 1 : CityMeshes.RolledEdge(map, e) ? 2 : 3;
                        km[e.cls == 0 ? 0 : 1, type] += e.length;
                    }
                    string Row(int g) => $"none {km[g, 0] / 1000:0.0} km, roll curb alone {km[g, 1] / 1000:0.0} km, rolled + walk {km[g, 2] / 1000:0.0} km, vertical + walk {km[g, 3] / 1000:0.0} km";
                    sb.AppendLine($"{(after ? "AFTER " : "BEFORE")} (curbs3 {(after ? "on" : "off")}), street centreline km by edge type ({sw.ElapsedMilliseconds} ms):");
                    sb.AppendLine($"  local streets (class 0):      {Row(0)}");
                    sb.AppendLine($"  collectors / arterials (1-3): {Row(1)}");
                    double all = 0, curbed = 0;
                    for (int g = 0; g < 2; g++) for (int t = 0; t < 4; t++) { all += km[g, t]; if (t > 0) curbed += km[g, t]; }
                    if (after)
                    {
                        // dead ends: a surface street's end at a node of one edge, with and without a turnaround bulb
                        int dead = 0, bulbs = 0, deadLocal = 0, bulbsLocal = 0;
                        for (int nd = 0; nd < map.nodes.Length; nd++)
                        {
                            if (map.nodeEdges[nd].Count != 1) continue;
                            var de = map.edges[map.nodeEdges[nd][0]];
                            if (de.link || de.cls > 3 || de.tunnel || de.bridge || de.a == de.b) continue;
                            bool bulb = trims.bulb != null && trims.bulb[nd];
                            dead++; if (bulb) bulbs++;
                            if (de.cls == 0) { deadLocal++; if (bulb) bulbsLocal++; }
                        }
                        sb.AppendLine($"  dead ends: {dead} street ends ({deadLocal} local), {bulbs} drawn with a turnaround bulb ({bulbsLocal} local), {dead - bulbs} without ({deadLocal - bulbsLocal} local)");
                    }
                    sb.AppendLine($"  curbed {curbed / 1000:0.0} of {all / 1000:0.0} km of surface street ({100.0 * curbed / System.Math.Max(all, 1):0.0}%); not streets (ramps, freeways, bridges, tunnels) {notStreet / 1000:0.0} km, alleys / drives under two lanes {narrow / 1000:0.0} km");
                }

                // ---- 2. the sample, built both ways ----
                var groups = new List<(string name, List<(int tx, int tz)> tiles)>();
                {
                    int x0 = -18, z0 = 21, x1 = -11, z1 = 28;
                    var bx = System.Environment.GetEnvironmentVariable("PSX_CURBS_BOX");
                    if (!string.IsNullOrEmpty(bx)) { var b = bx.Split(','); x0 = int.Parse(b[0]); z0 = int.Parse(b[1]); x1 = int.Parse(b[2]); z1 = int.Parse(b[3]); }
                    var box = new List<(int, int)>();
                    for (int tz = z0; tz <= z1; tz++) for (int tx = x0; tx <= x1; tx++) box.Add((tx, tz));
                    groups.Add(($"box {x0},{z0}..{x1},{z1} ({box.Count} tiles)", box));
                    (int, int) T(Vector2 p) => (Mathf.FloorToInt(p.x / CityMeshes.TileSize), Mathf.FloorToInt(p.y / CityMeshes.TileSize));
                    groups.Add(("Uptown (the Square)", new List<(int, int)> { T(map.uptown) }));
                    groups.Add(("subdivision (Hidden Valley)", new List<(int, int)> { T(CityRefSpots.LL(35.2655, -80.7790)) }));
                    groups.Add(("rural (north-west)", new List<(int, int)> { T(CityRefSpots.LL(35.3300, -80.9300)) }));
                    groups.Add(("freeway interchange (I-77 / I-85)", new List<(int, int)> { T(CityRefSpots.LL(35.2605, -80.8430)) }));
                }
                // warm: every tile once (the caches both builds share)
                CityMeshes.Curbs3On = false; CityMeshes.Inlet2On = false; CityMeshes.Curbs3Reset();
                foreach (var g in groups) foreach (var (tx, tz) in g.tiles) CurbsDestroy(CityMeshes.Build(map, trims, proc, tx, tz));
                var res = new Dictionary<string, (long tris, long verts, double ms)>();
                foreach (bool after in new[] { false, true })
                {
                    CityMeshes.Curbs3On = after; CityMeshes.Inlet2On = after; CityMeshes.Curbs3Reset();
                    CityMeshes.RecordHardware = true;
                    CityMeshes.InletsBuilt.Clear(); CityMeshes.LidsBuilt.Clear();
                    CityMeshes.Curbs2Stats.Reset(); CityMeshes.Curbs3Stats.Reset();
                    CityMeshes.DrivesRoll = CityMeshes.DrivesCut = CityMeshes.DrivesFace = 0; CityMeshes.DrivesFaceAt.Clear();
                    int dNone = 0, dRoll = 0, dVert = 0;
                    var dl = new List<CityHouses.Driveway>();
                    foreach (var g in groups)
                    {
                        long tris = 0, verts = 0; double ms = 0;
                        foreach (var (tx, tz) in g.tiles)
                        {
                            sw.Restart();
                            var tm = CityMeshes.Build(map, trims, proc, tx, tz);
                            ms += sw.Elapsed.TotalMilliseconds;
                            foreach (var f in typeof(CityMeshes.TileMeshes).GetFields())
                            {
                                if (f.FieldType != typeof(Mesh)) continue;
                                var m = f.GetValue(tm) as Mesh;
                                if (m == null) continue;
                                verts += m.vertexCount;
                                for (int s = 0; s < m.subMeshCount; s++) tris += (long)m.GetIndexCount(s) / 3;
                            }
                            CurbsDestroy(tm);
                            // the tile's driveways by their street's edge type
                            var min = new Vector2(tx, tz) * CityMeshes.TileSize; var max = min + Vector2.one * CityMeshes.TileSize;
                            CityHouses.DrivewaysNearBound(map, min, max, dl);
                            foreach (var d in dl)
                            {
                                if (d.toLot || d.foot.x < min.x || d.foot.x >= max.x || d.foot.y < min.y || d.foot.y >= max.y) continue;
                                var de = map.edges[d.edge];
                                if (!CityMeshes.WalkEdge(map, de)) dNone++; else if (CityMeshes.RolledEdge(map, de)) dRoll++; else dVert++;
                            }
                        }
                        res[(after ? "A:" : "B:") + g.name] = (tris, verts, ms);
                    }
                    CityMeshes.RecordHardware = false;
                    sb.AppendLine($"{(after ? "AFTER " : "BEFORE")} sample: {CityMeshes.Curbs2Stats.Line()}");
                    sb.AppendLine($"  {CityMeshes.Curbs3Stats.Line()}");
                    sb.AppendLine($"  driveways in the sample by their street: no curb {dNone}, roll curb {dRoll}, vertical curb {dVert}; mouths the walk spans crossed: over a roll {CityMeshes.DrivesRoll}, through a cut (lip under 3 cm) {CityMeshes.DrivesCut}, A CURB FACE ACROSS (step over 3 cm) {CityMeshes.DrivesFace}");
                    foreach (var s in CityMeshes.DrivesFaceAt) sb.AppendLine("    face: " + s);
                }
                sb.AppendLine("COST (before -> after):");
                foreach (var g in groups)
                {
                    var b = res["B:" + g.name]; var a = res["A:" + g.name];
                    sb.AppendLine($"  {g.name}: triangles {b.tris} -> {a.tris} ({100.0 * (a.tris - b.tris) / System.Math.Max(b.tris, 1):+0.0;-0.0}%), vertices {b.verts} -> {a.verts} ({100.0 * (a.verts - b.verts) / System.Math.Max(b.verts, 1):+0.0;-0.0}%), build {b.ms / g.tiles.Count:0} -> {a.ms / g.tiles.Count:0} ms a tile ({100.0 * (a.ms - b.ms) / System.Math.Max(b.ms, 1):+0.0;-0.0}%)");
                }
                CityMeshes.Curbs3On = true; CityMeshes.Inlet2On = true; CityMeshes.Curbs3Reset();
                sb.AppendLine("CURBS AUDIT OK");
            }
            catch (System.Exception ex) { sb.AppendLine("CURBS AUDIT FAILED: " + ex); Debug.LogException(ex); code = 1; }
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "curbs_audit.txt"), sb.ToString());
            Debug.Log("[CurbsAudit]\n" + sb);
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
