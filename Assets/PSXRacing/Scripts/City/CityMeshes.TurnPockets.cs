using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  LEFT-TURN POCKETS (2026-11-10). The owner's road-level photos of NC-16
    //  Brookshire Blvd by N Crigler St, and the median pass's 37 real views:
    //  on a divided Charlotte arterial the median necks down to a narrow
    //  concrete island beside a LEFT-TURN LANE before every opening. The game
    //  drew none: OSM maps a divided road as two one-way lines and (mostly)
    //  tags the through lanes only, so the pavement the turn lane stands on
    //  was drawn as median - a bed or grass strip as wide as the real island
    //  PLUS the turn lane beside it (Providence Rd 5-7 m, South Blvd south
    //  7 m, Brookshire by N Hoskins 4.5-7 m; the real islands are 1-1.5 m).
    //
    //  THE RULE. A one-way carriageway of a divided surface street
    //  (secondary to trunk, its median an island: CurbMedian's class) that
    //  arrives at a JUNCTION (a fan or a junction cluster) where a road it
    //  can turn into leaves on its median side (on a secondary street only
    //  where that road is secondary or bigger), and whose leftmost lane is
    //  not already a turn lane (OSM turn:lanes, a TAPR bay), gets a pocket:
    //  the pavement widened on the median side by one lane (PocketLaneM, or
    //  what the median leaves over a PocketIslandMinM island, never under
    //  PocketMinLaneM) from the junction back over the storage length, then
    //  a taper (a smoothstep, as every other taper of the line model). The
    //  storage and taper are the class's (55 + 30 m primary and trunk, 40 +
    //  25 m secondary), fitted to the run back to the last junction or
    //  opening (half of it where the median cannot hold two pockets side by
    //  side: the other carriageway's pocket for the opening behind takes
    //  the other half), none under PocketMinStorageM.
    //
    //  HOW. An AUX EASE of the line model (as a merge zone's aux lane: the
    //  pavement widens, the edge line on that side rides it, the lanes'
    //  frame - traffic, the AI, the race line - is unchanged), from the
    //  node back. The island beside it narrows by itself (CurbMedian reads
    //  the drawn edge), its top concrete where it is narrow and at the nose
    //  (the median dress). The paint: the yellow left edge line rides the
    //  pocket; a solid white lane line (MkAuxLS) between the pocket and the
    //  through lanes over the storage; a left arrow in the pocket.
    //  PSX_CITY_TURNPOCKETS=0: as before.
    // ======================================================================
    public static partial class CityMeshes
    {
        public static readonly bool TurnPocketsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_TURNPOCKETS") != "0";
        /// <summary>The tag a pocket's aux ease carries (no merge zone's).</summary>
        public const long PocketTag = long.MinValue + 11;
        /// <summary>The pocket's lane (11 ft), the least it is drawn at (10 ft), and the island left beside it.</summary>
        public const float PocketLaneM = 3.35f, PocketMinLaneM = 3.0f, PocketIslandMinM = 0.85f;
        /// <summary>No pocket with less storage than this behind the stop.</summary>
        public const float PocketMinStorageM = 12f, PocketMinTaperM = 15f;
        /// <summary>The piece of a carriageway between a divided cross street's two carriageways is at most this long.</summary>
        const float PocketCrossingM = 45f;
        /// <summary>Junctions kept as they were on this export (CurbMedianGraph): the city-wide
        /// launch audit found a new spot on the junction's fan with the pocket's wider mouth.
        /// n11655, North Graham St into University City Blvd at (2214,12389): 0.38 m.</summary>
        static readonly HashSet<int> PocketSkipNodes = new HashSet<int> { 11655 };
        /// <summary>Clear of the junction or opening the run starts at.</summary>
        const float PocketStartClearM = 6f;

        public sealed class TurnPocket
        {
            public int node, edge, twin; public float w, storage, taper, trim, gapMin, run; public Vector2 at;
            /// <summary>The edges it lies on, the node's first: (edge, the chain distance from the node to its b end).</summary>
            public readonly List<(int edge, float d0)> chain = new List<(int, float)>(2);
            /// <summary>The pocket's reach back from the node.</summary>
            public float Reach => trim + storage + taper;
        }
        public static readonly List<TurnPocket> TurnPockets = new List<TurnPocket>();
        /// <summary>Approaches that open on the median side and get none, with why.</summary>
        public static readonly List<string> TurnPocketLeftAt = new List<string>();
        public static readonly Dictionary<string, int> TurnPocketLeftWhy = new Dictionary<string, int>();
        static readonly Dictionary<int, TurnPocket> pocketOfEdge = new Dictionary<int, TurnPocket>();

        /// <summary>The pocket this edge carries (null: none).</summary>
        public static TurnPocket PocketOf(int edge) => pocketOfEdge.TryGetValue(edge, out var p) ? p : null;

        /// <summary>The pocket's widening of this edge's pavement at s (0: none).</summary>
        public static float PocketWidthAt(CityMap.Edge e, float s)
        {
            if (pocketOfEdge.Count == 0 || !pocketOfEdge.ContainsKey(e.index)) return 0f;
            var ez = e.lmEase;
            if (ez == null) return 0f;
            float w = 0f;
            for (int i = 0; i < ez.Length; i++)
                if (ez[i].aux && ez[i].tag == PocketTag) w = Mathf.Max(w, LineModel.AuxAt(e, ez[i], s));
            return w;
        }

        /// <summary>A carriageway a pocket is laid on (no geometry test).</summary>
        /// (the map is passed: this runs inside the map's own parse, where CityMap.Get() would start it again)
        static bool PocketClass(CityMap map, CityMap.Edge e) =>
            e.oneway && !e.link && !e.bridge && !e.tunnel && e.a != e.b && !string.IsNullOrEmpty(e.name)
            && e.cls >= 2 && e.cls <= 4 && (e.cls <= 3 || TrunkIsland(e))
            && LineModel.RefugeOf(e.index) == null && !(map.graphHash == CurbMedianGraph && CurbMedianSkipEdges.Contains(e.index));

        static readonly HashSet<int> pkSegs = new HashSet<int>();

        /// <summary>The median at s: from this carriageway's drawn left edge
        /// to its twin's (NaN: no twin of the class within 45 m).</summary>
        static float PocketGap(CityMap map, CityMap.Edge e, float s, out int twin)
        {
            twin = -1;
            s = Mathf.Clamp(s, 0f, e.length);
            var p = e.PointAt(s); var tg = e.TangentAt(s).normalized;
            var left = new Vector2(-tg.y, tg.x);
            pkSegs.Clear();
            map.EdgeSegsInRect(p - Vector2.one * 45f, p + Vector2.one * 45f, pkSegs);
            float best = float.MaxValue; int bi = -1;
            foreach (int packed in pkSegs)
            {
                var o = map.edges[packed >> 12];
                if (o.index == e.index || !o.oneway || o.name != e.name) continue;
                int seg = packed & 0xFFF;
                if (seg + 1 >= o.pts.Length) continue;
                Vector2 a = o.pts[seg], b = o.pts[seg + 1], ab = b - a;
                float l2 = ab.sqrMagnitude;
                if (l2 < 1e-4f) continue;
                float u = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
                var q = a + ab * u;
                float lat = Vector2.Dot(q - p, left);
                if (lat < 1f || lat > 45f || Mathf.Abs(Vector2.Dot(q - p, tg)) > 3f) continue;
                if (Vector2.Dot(ab / Mathf.Sqrt(l2), tg) > -0.7f) continue;
                if (lat < best) { best = lat; bi = o.index; }
            }
            if (bi < 0) return float.NaN;
            var tw = map.edges[bi];
            if (!PocketClass(map, tw)) return float.NaN;
            CityElevation.ProjectOn(tw, p + left * best, out float sT);
            LineModel.Extents(e, s, out _, out float eP);
            LineModel.Extents(tw, sT, out _, out float tP);
            twin = bi;
            return best - eP - tP;
        }

        /// <summary>A road the carriageway can turn into leaves this node on
        /// its median side: the greatest such road's class (-1: none).</summary>
        static int PocketTurnAt(CityMap map, CityMap.Edge e, int node)
        {
            var ne = map.nodeEdges[node];
            if (ne == null) return -1;
            int best = -1;
            var te = e.TangentAt(e.length).normalized;
            var left = new Vector2(-te.y, te.x);
            foreach (int i in ne)
            {
                if (i == e.index) continue;
                var o = map.edges[i];
                if (o.a == o.b || o.length < 0.5f) continue;
                if (o.oneway && o.a != node) continue;          // it arrives: nothing to turn into
                var d = o.a == node ? o.TangentAt(Mathf.Min(1f, o.length)) : -o.TangentAt(Mathf.Max(0f, o.length - 1f));
                if (Vector2.Dot(d.normalized, left) > 0.5f) best = Mathf.Max(best, o.cls);
            }
            return best;
        }

        static void PocketLeft(CityMap map, CityMap.Edge e, string why)
        {
            TurnPocketLeftWhy.TryGetValue(why, out int c); TurnPocketLeftWhy[why] = c + 1;
            if (TurnPocketLeftAt.Count < 3000)
                TurnPocketLeftAt.Add($"n{e.b} ({map.nodes[e.b].x:0},{map.nodes[e.b].y:0}) e{e.index} '{e.name}': {why}");
        }

        static void BuildTurnPockets(CityMap map, Trims t)
        {
            TurnPockets.Clear(); TurnPocketLeftAt.Clear(); TurnPocketLeftWhy.Clear(); pocketOfEdge.Clear();
            if (!TurnPocketsOn || !CurbMedianOn || !CurbMedianAll) return;
            // NOT IN THE ELEVATION SOLVE'S TRIMS (CityElevation.PrepareSeats): the solve seats
            // branches against their hosts' extents, and a pocket there moved 14 seat stations
            // and with them a launch spot (the city-wide launch audit, 91 -> 92). The heights
            // are solved on the roads as they were; the pockets are laid on the solved state.
            if (DividerSeatPass) return;
            var add = new Dictionary<int, List<LineModel.Ease>>();
            var chain = new List<CityMap.Edge>(8);
            foreach (var e in map.edges)
            {
                if (!PocketClass(map, e)) continue;
                int n = e.b;
                bool junction = t.patch[n] || (t.clusterOf != null && t.clusterOf[n] >= 0);
                int turnCls = PocketTurnAt(map, e, n);
                // a divided road crossing a divided road: the turn is made at the far node of the
                // crossing, off the short piece between the cross street's two carriageways
                if (turnCls < 0)
                {
                    var on = MedianOnward(map, e, n);
                    if (on != null && on.length <= PocketCrossingM && PocketClass(map, on)) turnCls = PocketTurnAt(map, on, on.b);
                }
                if (turnCls < 0) continue;
                // a secondary street's opening to a minor street has no turn lane (Queens Rd, The Plaza: a break in the trees)
                if (e.cls == 2 && turnCls < 2) { PocketLeft(map, e, "a secondary street's opening to a minor street"); continue; }
                if (!junction) { PocketLeft(map, e, "the opening is a link leaving a through road (no junction)"); continue; }
                if (SplitNode(map, e, n)) continue;
                if (t.Internal(e.index)) continue;
                if (map.graphHash == CurbMedianGraph && PocketSkipNodes.Contains(n)) { PocketLeft(map, e, "kept as it was (a launch spot on its fan)"); continue; }
                TurnOnly(e, out int fwd, out _, out _, out _, out _);
                if ((fwd & 1) != 0) { PocketLeft(map, e, "its left lane is a turn lane already"); continue; }
                float trim = t.atB[e.index];
                // the run back: through mitred joints of the same carriageway, to the last junction or opening
                chain.Clear();
                float run = 0f; var cur = e;
                for (int guard = 0; guard < 12; guard++)
                {
                    chain.Add(cur); run += cur.length;
                    int m = cur.a;
                    if (!t.mitre[m]) { run -= t.atA[cur.index]; break; }
                    int oi = t.throughA[m] == cur.index ? t.throughB[m] : t.throughB[m] == cur.index ? t.throughA[m] : -1;
                    if (oi < 0 || oi == cur.index) break;
                    var o = map.edges[oi];
                    if (!PocketClass(map, o) || o.b != m || o.name != cur.name) break;
                    if (MedianNodeOpens(map, o, m)) break;
                    if (run > 220f) break;
                    cur = o;
                }
                run -= trim + PocketStartClearM;
                float ls0 = e.cls >= 3 ? 55f : 40f, lt0 = e.cls >= 3 ? 30f : 25f;
                // the median over the storage
                float gapMin = float.MaxValue; int twin = -1; bool noTwin = false;
                float probeTo = Mathf.Min(ls0, Mathf.Max(PocketMinStorageM, run - PocketMinTaperM));
                for (float d = trim + 2f; d <= trim + probeTo + 0.01f; d += 6f)
                {
                    if (!PocketChainAt(chain, d, out var ce, out float cs)) break;
                    float g = PocketGap(map, ce, cs, out int tw);
                    if (float.IsNaN(g)) { noTwin = true; break; }
                    if (g < gapMin) { gapMin = g; twin = tw; }
                }
                if (noTwin || twin < 0) { PocketLeft(map, e, "no twin carriageway beside the storage"); continue; }
                float w = Mathf.Min(PocketLaneM, gapMin - PocketIslandMinM);
                if (w < PocketMinLaneM) { PocketLeft(map, e, "the median is too narrow"); continue; }
                // two pockets back to back share the run where the median cannot hold both side by side
                float room = gapMin >= 2f * w + PocketIslandMinM ? run : 0.5f * run - 1f;
                float total = Mathf.Min(ls0 + lt0, room);
                if (total < PocketMinStorageM + PocketMinTaperM) { PocketLeft(map, e, "the run back is too short"); continue; }
                float lt = Mathf.Min(lt0, Mathf.Max(PocketMinTaperM, 0.4f * total));
                float ls = total - lt;
                // the taper stays inside the median too
                bool fits = true;
                for (int q = 1; q <= 3 && fits; q++)
                {
                    float f = q * 0.25f;
                    if (!PocketChainAt(chain, trim + ls + lt * f, out var ce, out float cs)) { fits = false; break; }
                    float g = PocketGap(map, ce, cs, out _);
                    if (float.IsNaN(g) || g - PocketIslandMinM < w * (1f - LineModel.Smooth(f))) fits = false;
                }
                if (!fits) { PocketLeft(map, e, "the median narrows along the taper"); continue; }
                var pk = new TurnPocket { node = n, edge = e.index, twin = twin, w = w, storage = ls, taper = lt, trim = trim, gapMin = gapMin, run = run, at = map.nodes[n] };
                float d0 = 0f;
                foreach (var ce in chain)
                {
                    if (d0 >= pk.Reach) break;
                    if (!add.TryGetValue(ce.index, out var list)) add[ce.index] = list = new List<LineModel.Ease>(1);
                    list.Add(new LineModel.Ease
                    {
                        aux = true, auxArm = -1, side = 1, fromA = false, d0 = d0, tag = PocketTag,
                        auxD = 0f, auxWN = w, auxW = w, auxLs = 0f, auxXF = -(trim + ls), auxLt = lt, auxZone = -1 - TurnPockets.Count,
                    });
                    pk.chain.Add((ce.index, d0));
                    pocketOfEdge[ce.index] = pk;
                    d0 += ce.length;
                }
                TurnPockets.Add(pk);
            }
            foreach (var kv in add)
            {
                var e = map.edges[kv.Key];
                var all = new List<LineModel.Ease>(e.lmEase ?? System.Array.Empty<LineModel.Ease>());
                all.AddRange(kv.Value);
                e.lmEase = all.ToArray();
            }
            if (TurnPockets.Count > 0) Debug.Log("[City] " + TurnPocketLine());
        }

        /// <summary>The chain's edge and arc at d back from the node.</summary>
        static bool PocketChainAt(List<CityMap.Edge> chain, float d, out CityMap.Edge e, out float s)
        {
            float d0 = 0f;
            foreach (var c in chain)
            {
                if (d <= d0 + c.length) { e = c; s = c.length - (d - d0); return true; }
                d0 += c.length;
            }
            e = null; s = 0f; return false;
        }

        public static string TurnPocketLine()
        {
            float m = 0f, wMin = float.MaxValue;
            foreach (var p in TurnPockets) { m += p.storage + p.taper; wMin = Mathf.Min(wMin, p.w); }
            var sb = new System.Text.StringBuilder();
            sb.Append($"left-turn pockets {(TurnPocketsOn ? "on" : "OFF")}: {TurnPockets.Count} ({m:0} m of lane and taper, narrowest {(TurnPockets.Count > 0 ? wMin : 0f):0.00} m; {MarkStats.pocketZoneLines} merge-zone lanes to a median link marked as turn lanes, {MarkStats.pocketArrows} arrows); left as they were:");
            foreach (var kv in TurnPocketLeftWhy) sb.Append($" {kv.Value} {kv.Key};");
            return sb.ToString();
        }

        // ---- the paint -------------------------------------------------------

        /// <summary>After TurnMarks: each pocket's solid white lane line over
        /// its storage and its arrows.</summary>
        static void PocketMarks(CityMap map, Trims t)
        {
            foreach (var pk in TurnPockets)
            {
                foreach (var (ei, d0) in pk.chain)
                {
                    var e = map.edges[ei];
                    // the storage: chain distance trim .. trim + storage
                    float s0 = e.length - (pk.trim + pk.storage - d0), s1 = e.length - (pk.trim - d0);
                    s0 = Mathf.Max(0f, s0); s1 = Mathf.Min(e.length, s1);
                    if (s1 - s0 < 0.3f) continue;
                    AddMark(new PaintMark { style = MkAuxLS, edge = ei, s0 = s0, s1 = s1, side = 1, zone = -1, what = "turn pocket" });
                    MarkStats.pocketLines++; MarkStats.pocketM += s1 - s0;
                }
                // the arrows: one near the stop, one where the storage begins (a long pocket)
                var a = map.edges[pk.edge];
                if (PocketArrow(t, a, a.length - pk.trim - 7f, pk.w)) MarkStats.pocketArrows++;
                if (pk.storage > 40f && PocketChainAt2(map, pk, pk.trim + pk.storage - 6f, out var e2, out float s2) && PocketArrow(t, e2, s2, pk.w)) MarkStats.pocketArrows++;
            }
        }

        static bool PocketChainAt2(CityMap map, TurnPocket pk, float d, out CityMap.Edge e, out float s)
        {
            foreach (var (ei, d0) in pk.chain)
            {
                var c = map.edges[ei];
                if (d <= d0 + c.length) { e = c; s = c.length - (d - d0); return true; }
            }
            e = null; s = 0f; return false;
        }

        /// <summary>A left arrow in the pocket, its head at sRef (travel a to b), where the pocket runs at full width inside the drawn ribbon.</summary>
        static bool PocketArrow(Trims t, CityMap.Edge e, float sRef, float w)
        {
            float sLo = sRef - ArrowH, sHi = sRef;
            if (sLo < t.TrimAt(e, e.a) + 1f || sHi > e.length - t.TrimAt(e, e.b) - 1f) return false;
            if (LineModel.AuxWidth(e, sLo, 1) < w - 0.01f || LineModel.AuxWidth(e, sHi, 1) < w - 0.01f) return false;
            if (LineModel.Relayed(e, sLo) || LineModel.Relayed(e, sHi)) return false;
            float la = AuxThroughLat(e, 0.5f * (sLo + sHi), 1);
            if (float.IsNaN(la)) return false;
            float laneLat = la + 0.5f * w;
            glyphLocal.Clear();
            Arrow(glyphLocal, -1);
            var pieces = new List<Vector2[]>(glyphLocal.Count);
            var bps = new List<float>(16);
            foreach (var p in glyphLocal)
            {
                var q = new Vector2[p.Length];
                for (int i = 0; i < p.Length; i++)
                {
                    float s = sRef - (ArrowH - p[i].y);
                    q[i] = new Vector2(laneLat - p[i].x, s);
                    bps.Add(s);
                }
                pieces.Add(q);
            }
            bps.Sort();
            var u = new List<float>(bps.Count);
            foreach (float b in bps) if (u.Count == 0 || b - u[u.Count - 1] > 1e-3f) u.Add(b);
            var tg = e.TangentAt(0.5f * (sLo + sHi));
            AddMark(new PaintMark { style = MkGlyph, edge = e.index, s0 = sLo, s1 = sHi, pieces = pieces, bps = u.ToArray(), side = -1, what = "turn pocket arrow",
                                    hdg = Mathf.Repeat(Mathf.Atan2(tg.x, tg.y) * Mathf.Rad2Deg, 360f) });
            return true;
        }
    }
}
