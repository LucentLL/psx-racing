using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// ROAD SURVEY STAGE 4: THE FORK CHECK (owner 11-27, Wendover: "overlapping and conflicting road lines"). The island
    /// gate reads 0 at the Wendover split, so it cannot judge this class. Three counts, read from the tiles as the game
    /// builds them (CityMeshes.Build, live, cache off) around each spot, and from the line model:
    ///   (a) FOREIGN LINE: a painted line texel (a road triangle whose U lies in a line column of its profile, the top
    ///       sheet there) lying INSIDE a lane of any road: more than 0.45 m in from both of that lane's own lines
    ///       (LineModel.LinesAt, laid where the road's ribbon is drawn: past its trims, not on a branch's clipped
    ///       stretch, where the host owns the pavement). A road's own lines lie on its lane boundaries, so a line inside
    ///       a lane is another road's (or a line the model did not mean). Counted in 0.25 m cells; a PLACE is a 4 m
    ///       cell with 4 or more (1 m of line).
    ///   (b) TWO SURFACES: a 0.25 m cell covered by two driving-surface sheets (every road slot, decks included):
    ///       COPLANAR (under 1 cm apart: they fight), LAYERED (6 cm - 2.5 m: one stands on the other), MATERIAL (a
    ///       concrete deck sheet and an asphalt sheet within 0.5 m: a slab over or under asphalt). Sheets 1 - 6 cm
    ///       under the top are the builders' hidden seam floors (GoreSeamM): reported apart, not a fault. A place
    ///       counts when its cells exceed 0.25 m2 (4 cells).
    ///   (c) DECK END IN A ROAD: a bridge edge's end section (the drawn deck's start or end, from its left edge to its
    ///       right) of which any point lies inside the drawn ribbon of another non-bridge road that is not the deck's
    ///       own through continuation (line model extents, past that road's trims). City-wide, no tiles.
    ///   -executeMethod PSXRacing.EditorTools.CityForkCheck.Run
    ///   PSX_FC_SPOTS=file (the spot csv: id,x,z,heading,edges,nodes,kind,owner_date,owner_words,radius,must)
    ///   PSX_FC_TAG=name -> city_forkcheck_name.txt / .tsv in the project folder
    /// </summary>
    public static class CityForkCheck
    {
        const float Cell = 0.25f, PlaceM = 4f;
        sealed class Hits { public List<Vector2> a = new List<Vector2>(), cop = new List<Vector2>(), lay = new List<Vector2>(), mat = new List<Vector2>(), hid = new List<Vector2>(); public int tris; }

        public static void Run()
        {
            var inv = CultureInfo.InvariantCulture;
            string tag = System.Environment.GetEnvironmentVariable("PSX_FC_TAG") ?? "run";
            string spotsCsv = System.Environment.GetEnvironmentVariable("PSX_FC_SPOTS");
            string root = Directory.GetParent(Application.dataPath).FullName;
            var log = new StringBuilder($"FORK CHECK {System.DateTime.Now:yyyy-MM-dd HH:mm} tag {tag}; SHALLOWFORK {(CityMeshes.ShallowForkOn ? 1 : 0)}\n");
            var tsv = new StringBuilder("spot\tclass\tx\tz\tcells\n");
            int code = 0;
            try
            {
                var map = CityMap.Get();
                var trims = CityMeshes.NodeTrims(map);
                var buildings = CityBuildings.Precompute(map);
                // (c) city-wide
                var deckEnds = DeckEnds(map, trims);
                log.Append($"(c) DECK END IN A ROAD, city-wide: {deckEnds.Count} deck ends; SF2 deck ends kept from being carried onto a fork: {CityElevation.AbutmentForkSkips}\n");
                foreach (var d in deckEnds) log.Append(string.Format(inv, "  deck e{0} end at node {1} ({2:0.0}, {3:0.0}) inside e{4} '{5}'\n", d.deck, d.node, d.p.x, d.p.y, d.road, map.edges[d.road].name ?? ""));
                if (string.IsNullOrEmpty(spotsCsv)) throw new System.Exception("PSX_FC_SPOTS not set");
                var spots = CityIslandGate.ReadSpots(spotsCsv);
                var cache = new Dictionary<long, Hits>();
                double ms = 0; int built = 0; long tris = 0;
                int[] tot = new int[5]; int totC = 0;
                foreach (var sp in spots)
                {
                    var hits = new Hits();
                    int tx0 = Mathf.FloorToInt((sp.p.x - sp.r) / CityMeshes.TileSize), tx1 = Mathf.FloorToInt((sp.p.x + sp.r) / CityMeshes.TileSize);
                    int tz0 = Mathf.FloorToInt((sp.p.y - sp.r) / CityMeshes.TileSize), tz1 = Mathf.FloorToInt((sp.p.y + sp.r) / CityMeshes.TileSize);
                    var pl = new HashSet<long>[5]; for (int c = 0; c < 5; c++) pl[c] = new HashSet<long>();
                    var cellsIn = new int[5];
                    var placeCount = new Dictionary<(int, long), int>();
                    for (int tz = tz0; tz <= tz1; tz++)
                        for (int tx = tx0; tx <= tx1; tx++)
                        {
                            long k = ((long)tx << 32) ^ (uint)tz;
                            if (!cache.TryGetValue(k, out var th))
                            {
                                var sw = System.Diagnostics.Stopwatch.StartNew();
                                var tm = CityMeshes.Build(map, trims, buildings, tx, tz);
                                ms += sw.Elapsed.TotalMilliseconds; built++;
                                th = Analyse(map, trims, tm);
                                tris += th.tris;
                                cache[k] = th;
                            }
                            var lists = new[] { th.a, th.cop, th.lay, th.mat, th.hid };
                            for (int c = 0; c < 5; c++)
                                foreach (var q in lists[c])
                                {
                                    if ((q - sp.p).magnitude > sp.r) continue;
                                    cellsIn[c]++;
                                    long pk = ((long)Mathf.FloorToInt(q.x / PlaceM) << 32) ^ (uint)Mathf.FloorToInt(q.y / PlaceM);
                                    placeCount.TryGetValue((c, pk), out int n); placeCount[(c, pk)] = n + 1;
                                }
                        }
                    var places = new int[5];
                    foreach (var kv in placeCount)
                        if (kv.Value >= 4)
                        {
                            places[kv.Key.Item1]++;
                            long pk = kv.Key.Item2; float px = ((int)(pk >> 32) + 0.5f) * PlaceM, pz = ((int)(pk & 0xffffffff) + 0.5f) * PlaceM;
                            tsv.Append(string.Format(inv, "{0}\t{1}\t{2:0.0}\t{3:0.0}\t{4}\n", sp.id, new[] { "FOREIGN", "COPLANAR", "LAYERED", "MATERIAL", "hidden" }[kv.Key.Item1], px, pz, kv.Value));
                        }
                    int dk = deckEnds.Count(d => (d.p - sp.p).magnitude <= sp.r);
                    for (int c = 0; c < 5; c++) tot[c] += places[c];
                    totC += dk;
                    log.Append(string.Format(inv, "SPOT {0} [{1}]: FOREIGN {2} ({3:0.0} m) COPLANAR {4} LAYERED {5} MATERIAL {6} ({7:0.0} m2 in all) DECKEND {8} | hidden seam floors {9}\n",
                        sp.id, sp.kind, places[0], cellsIn[0] * Cell, places[1], places[2], places[3], (cellsIn[1] + cellsIn[2] + cellsIn[3]) * Cell * Cell, dk, places[4]));
                }
                log.Append(string.Format(inv, "TOTAL over {0} spots: FOREIGN {1} COPLANAR {2} LAYERED {3} MATERIAL {4} DECKEND {5} (places) | hidden {6}\n", spots.Count, tot[0], tot[1], tot[2], tot[3], totC, tot[4]));
                log.Append(string.Format(inv, "cost: {0} tiles, {1:0} triangles a tile, build {2:0} ms a tile\n", built, built > 0 ? (double)tris / built : 0, built > 0 ? ms / built : 0));
            }
            catch (System.Exception ex) { log.Append("FAILED: " + ex + "\n"); code = 1; }
            File.WriteAllText(Path.Combine(root, $"city_forkcheck_{tag}.txt"), log.ToString());
            File.WriteAllText(Path.Combine(root, $"city_forkcheck_{tag}.tsv"), tsv.ToString());
            Debug.Log(log.ToString());
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(code);
        }

        static Hits Analyse(CityMap map, CityMeshes.Trims trims, CityMeshes.TileMeshes tm)
        {
            var h = new Hits();
            var o = tm.origin;
            float m0 = -8f;
            int N = Mathf.CeilToInt((CityMeshes.TileSize + 16f) / Cell);
            var top = new float[N * N]; var sec = new float[N * N]; var cnt = new byte[N * N];
            var topC = new bool[N * N]; var secC = new bool[N * N]; var topP = new bool[N * N];
            for (int k = 0; k < top.Length; k++) { top[k] = float.NegativeInfinity; sec[k] = float.NegativeInfinity; }
            if (tm.roads != null && tm.roadSlots != null)
            {
                var v = tm.roads.vertices; var uv = tm.roads.uv;
                for (int sm = 0; sm < tm.roads.subMeshCount && sm < tm.roadSlots.Length; sm++)
                {
                    if (!CityMeshes.RoadSlot((int)tm.roadSlots[sm], out int prof, out int surf)) continue;
                    bool concrete = surf >= 2;
                    LineModel.Layout lay = prof != CityMeshes.JunctionProfile && prof >= 0 && prof < RoadProfiles.ProfileCount ? LineModel.LayoutOf(prof) : null;
                    var t = tm.roads.GetTriangles(sm);
                    h.tris += t.Length / 3;
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                        bool paint = false;
                        if (lay != null && uv != null && uv.Length == v.Length && lay.x0 != null)
                        {
                            float ua = uv[t[i]].x, ub = uv[t[i + 1]].x, uc = uv[t[i + 2]].x;
                            for (int j = 0; j < lay.x0.Length; j++)
                            {
                                float lo = lay.x0[j] / (float)lay.texW - 1e-4f, hi = (lay.x1[j] + 1) / (float)lay.texW + 1e-4f;
                                if (ua >= lo && ua <= hi && ub >= lo && ub <= hi && uc >= lo && uc <= hi) { paint = true; break; }
                            }
                        }
                        Raster(a, b, c, N, m0, (k, y) =>
                        {
                            if (cnt[k] < 255) cnt[k]++;
                            if (y > top[k]) { sec[k] = top[k]; secC[k] = topC[k]; top[k] = y; topC[k] = concrete; topP[k] = paint; }
                            else if (y > sec[k]) { sec[k] = y; secC[k] = concrete; }
                        });
                    }
                }
            }
            // the lane interiors of every drawn ribbon on the tile
            var lane = new bool[N * N]; var road = new bool[N * N];
            var segs = new HashSet<int>();
            var min = new Vector2(o.x, o.z); var max = min + Vector2.one * CityMeshes.TileSize;
            map.EdgeSegsInRect(min - Vector2.one * 10f, max + Vector2.one * 10f, segs);
            var edges = new HashSet<int>(); foreach (int pk in segs) edges.Add(pk >> 12);
            var lines = new List<LineModel.LineAt>();
            foreach (int ei in edges)
            {
                var e = map.edges[ei];
                if (e.a == e.b || e.bridge || e.tunnel) continue;
                float s0 = trims.atA[ei], s1 = e.length - trims.atB[ei];
                for (float s = s0; s <= s1; s += 0.5f)
                {
                    {
                        // the ROAD mask: (b) counts only on a map road's own drawn pavement, not on a lot or a yard
                        LineModel.Extents(e, s, out float eM, out float eP);
                        var pp = e.PointAt(s); var tt = e.TangentAt(s); var ll = new Vector2(-tt.y, tt.x);
                        for (float l = -eM; l <= eP; l += Cell * 0.8f)
                        {
                            var w = pp + ll * l;
                            int i = Mathf.FloorToInt((w.x - o.x - m0) / Cell), j = Mathf.FloorToInt((w.y - o.z - m0) / Cell);
                            if (i >= 0 && j >= 0 && i < N && j < N) road[j * N + i] = true;
                        }
                    }
                    if (CityMeshes.AuditView.ClipAt(e, s, out _)) continue;   // a branch's clipped stretch: the host owns it
                    LineModel.LinesAt(e, s, lines);
                    if (lines.Count < 2) continue;
                    var p = e.PointAt(s); var tg = e.TangentAt(s); var left = new Vector2(-tg.y, tg.x);
                    for (int q = 0; q + 1 < lines.Count; q++)
                    {
                        float l0 = lines[q].lat + 0.45f, l1 = lines[q + 1].lat - 0.45f;
                        if (lines[q + 1].lat - lines[q].lat < 2.4f || l1 <= l0) continue;
                        for (float l = l0; l <= l1; l += Cell * 0.8f)
                        {
                            var w = p + left * l;
                            int i = Mathf.FloorToInt((w.x - o.x - m0) / Cell), j = Mathf.FloorToInt((w.y - o.z - m0) / Cell);
                            if (i < 0 || j < 0 || i >= N || j >= N) continue;
                            lane[j * N + i] = true;
                        }
                    }
                }
            }
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int k = j * N + i;
                    var w = new Vector2(o.x + m0 + (i + 0.5f) * Cell, o.z + m0 + (j + 0.5f) * Cell);
                    if (w.x < o.x || w.y < o.z || w.x >= o.x + CityMeshes.TileSize || w.y >= o.z + CityMeshes.TileSize) continue;
                    if (cnt[k] == 0) continue;
                    if (topP[k] && lane[k]) h.a.Add(w);
                    if (road[k] && cnt[k] >= 2 && !float.IsNegativeInfinity(sec[k]))
                    {
                        float d = top[k] - sec[k];
                        if (d < 0.01f) h.cop.Add(w);
                        else if (d < 0.06f) h.hid.Add(w);
                        else if (d < 2.5f) h.lay.Add(w);
                        if (topC[k] != secC[k] && d < 0.5f) h.mat.Add(w);
                    }
                }
            return h;
        }

        struct DeckEnd { public int deck, node, road; public Vector2 p; }
        static List<DeckEnd> DeckEnds(CityMap map, CityMeshes.Trims trims)
        {
            var o = new List<DeckEnd>();
            var segs = new HashSet<int>();
            for (int di = 0; di < map.edges.Length; di++)
            {
                var d = map.edges[di];
                if (!d.bridge || d.a == d.b) continue;
                for (int end = 0; end < 2; end++)
                {
                    int n = end == 0 ? d.a : d.b;
                    bool deckOn = false;
                    foreach (int x in map.nodeEdges[n]) if (x != di && map.edges[x].bridge) deckOn = true;
                    if (deckOn) continue;   // the deck goes on: not its end
                    float s = end == 0 ? trims.atA[di] : d.length - trims.atB[di];
                    LineModel.Extents(d, s, out float eM, out float eP);
                    var p = d.PointAt(s); var tg = d.TangentAt(s); var left = new Vector2(-tg.y, tg.x);
                    int thA = trims.throughA[n], thB = trims.throughB[n];
                    segs.Clear(); map.EdgeSegsInRect(p - Vector2.one * 40f, p + Vector2.one * 40f, segs);
                    var cand = new HashSet<int>(); foreach (int pk in segs) cand.Add(pk >> 12);
                    bool hit = false;
                    foreach (int ri in cand)
                    {
                        if (ri == di || hit) continue;
                        var r = map.edges[ri];
                        if (r.bridge || r.tunnel || r.a == r.b) continue;
                        if ((ri == thA && di == thB) || (ri == thB && di == thA)) continue;   // its own continuation
                        // a deck's own approach (the continuing edge at the node, one road): not another road
                        if ((r.a == n || r.b == n) && map.nodeEdges[n].Count == 2) continue;
                        float r0 = trims.atA[ri] + 0.5f, r1 = r.length - trims.atB[ri] - 0.5f;
                        for (float u = -eM + 0.3f; u <= eP - 0.3f && !hit; u += 0.5f)
                        {
                            var q = p + left * u;
                            // nearest station on r
                            float best = float.MaxValue, bs = -1f;
                            for (float rs = r0; rs <= r1; rs += 1f) { float dd = (r.PointAt(rs) - q).sqrMagnitude; if (dd < best) { best = dd; bs = rs; } }
                            if (bs < 0f || best > 900f) continue;
                            var rp = r.PointAt(bs); var rt = r.TangentAt(bs); var rl = new Vector2(-rt.y, rt.x);
                            float lat = Vector2.Dot(q - rp, rl), lon = Vector2.Dot(q - rp, rt);
                            if (Mathf.Abs(lon) > 0.7f) continue;
                            LineModel.Extents(r, bs, out float rM, out float rP);
                            if (lat < rP - 0.3f && lat > -rM + 0.3f && Mathf.Abs(r.YAt(bs) - d.YAt(s)) < 1.5f) { hit = true; o.Add(new DeckEnd { deck = di, node = n, road = ri, p = q }); }
                        }
                    }
                }
            }
            return o;
        }

        static void Raster(Vector3 a, Vector3 b, Vector3 c, int N, float m0, System.Action<int, float> put)
        {
            float minx = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), maxx = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
            float minz = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), maxz = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
            int i0 = Mathf.Max(0, Mathf.CeilToInt((minx - m0) / Cell - 0.5f)), i1 = Mathf.Min(N - 1, Mathf.FloorToInt((maxx - m0) / Cell - 0.5f));
            int j0 = Mathf.Max(0, Mathf.CeilToInt((minz - m0) / Cell - 0.5f)), j1 = Mathf.Min(N - 1, Mathf.FloorToInt((maxz - m0) / Cell - 0.5f));
            if (i0 > i1 || j0 > j1) return;
            float den = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(den) < 1e-9f) return;
            for (int j = j0; j <= j1; j++)
            {
                float z = m0 + (j + 0.5f) * Cell;
                for (int i = i0; i <= i1; i++)
                {
                    float x = m0 + (i + 0.5f) * Cell;
                    float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / den;
                    float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / den;
                    float l3 = 1f - l1 - l2;
                    if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                    put(j * N + i, l1 * a.y + l2 * b.y + l3 * c.y);
                }
            }
        }
    }
}
