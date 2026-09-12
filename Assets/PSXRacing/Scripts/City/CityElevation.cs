using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// Solves road heights and answers ground heights for the city.
    ///
    /// The circuits' rule carries over whole: the land is graded TO the road,
    /// never the other way round. Every edge starts life following the real
    /// ground (a 60 m SRTM grid, see <see cref="BaseY"/>), smoothed and
    /// grade-limited; then OpenStreetMap's FACTS turn into structure:
    ///
    ///   bridge=yes on an edge     -> the whole edge is a deck: it holds a
    ///                                straight line between its ends and never
    ///                                pins the land.
    ///   crossing (no shared node) -> the OVER edge takes a hump that clears
    ///                                the under road by <see cref="ClearanceM"/>,
    ///                                approach-graded, merging into a viaduct
    ///                                where humps overlap (I-77 through uptown).
    ///   ...unless the under road is a FREEWAY MAINLINE and the over road a
    ///   surface street, in which case the freeway is dug into a TRENCH under
    ///   it instead. Charlotte's inner freeways (the Belk, Brookshire) run in
    ///   cuts under streets that stay at grade; raising every cross street
    ///   onto an embankment would hump the whole uptown grid.
    ///   water span                -> the road HOLDS its line across the span
    ///                                while the ground carves a creek bed.
    ///
    /// Stations marked elevated get a deck and piers and NO ground pin; the
    /// ground query pins only to grounded stations, with the circuits' shelf /
    /// sink / blend shape — looked up through the tile-local spatial hash
    /// instead of a whole-track Gaussian, because an O(track) walk is a
    /// non-starter against 25,000 edges.
    /// </summary>
    public static class CityElevation
    {
        public const float StationStep = 10f;
        public const float ClearanceM = 5.0f;   // under-side of deck over road below
        public const float DeckThick = 0.55f;
        /// <summary>How far below the tarmac the land sits inside a road
        /// corridor: a real kerb height. The skirt CityMeshes cuts is deeper
        /// so the road's own side always reaches down to it.</summary>
        public const float CorridorSink = 0.18f;
        public const float CorridorBlend = 26f;
        /// <summary>Above the base ground by this much = on structure. Wider
        /// than it was on the noise terrain: a road profile smoothed over
        /// 25 m sits a little proud of a real dip, and a deck across every
        /// hollow in the county is not what that means.</summary>
        public const float ElevMarginM = 1.4f;

        const float ApproachGrade = 0.045f;
        /// <summary>A crossing closer than this to the end of the freeway
        /// edge is AT the junction, not near it; the hump rule keeps it.</summary>
        const float TrenchEndM = 20f;

        static float MaxGrade(CityMap.Edge e) =>
            e.cls >= 5 && !e.link ? 0.04f : e.link ? 0.08f : e.cls == 4 ? 0.05f : e.cls == 0 ? 0.08f : 0.065f;

        // ------------------------------------------------------------------
        //  Base terrain: the real one. A 60 m grid baked from SRTM by the
        //  exporter (min-filtered so uptown's roofs read as street level,
        //  then blurred), bilinear per query. Heights are metres above the
        //  grid's own datum, so the world's y = 0 is a little under the
        //  lowest point in the county rather than sea level.
        // ------------------------------------------------------------------
        static bool demTried;
        static int demNX, demNZ;
        static float demX0, demZ0, demCell, demBase;
        static ushort[] dem;

        static void EnsureDem()
        {
            if (demTried) return;
            demTried = true;
            var ta = Resources.Load<TextAsset>("charlotte_dem");
            if (ta == null) { Debug.LogWarning("[City] charlotte_dem.bytes missing — using value-noise terrain"); return; }
            using (var r = new BinaryReader(new MemoryStream(ta.bytes)))
            {
                if (r.ReadUInt32() != 0x4D454450 || r.ReadInt32() != 1) { Debug.LogError("[City] charlotte_dem.bytes: bad header"); return; }
                demNX = r.ReadInt32(); demNZ = r.ReadInt32();
                demX0 = r.ReadSingle(); demZ0 = r.ReadSingle(); demCell = r.ReadSingle(); demBase = r.ReadSingle();
                dem = new ushort[demNX * demNZ];
                for (int i = 0; i < dem.Length; i++) dem[i] = r.ReadUInt16();
            }
            Resources.UnloadAsset(ta);
        }

        /// <summary>The DEM's datum in metres above sea level: add it to a
        /// world y to get an altitude. 0 without a DEM.</summary>
        public static float DatumASL { get { EnsureDem(); return dem != null ? demBase : 0f; } }
        public static bool HasDem { get { EnsureDem(); return dem != null; } }

        public static float BaseY(float x, float z)
        {
            EnsureDem();
            if (dem == null) return NoiseY(x, z);
            x /= CityMap.LayoutScale; z /= CityMap.LayoutScale;
            float fx = (x - demX0) / demCell, fz = (z - demZ0) / demCell;
            int ix = Mathf.Clamp(Mathf.FloorToInt(fx), 0, demNX - 2);
            int iz = Mathf.Clamp(Mathf.FloorToInt(fz), 0, demNZ - 2);
            float tx = Mathf.Clamp01(fx - ix), tz = Mathf.Clamp01(fz - iz);
            float a = dem[iz * demNX + ix], b = dem[iz * demNX + ix + 1];
            float c = dem[(iz + 1) * demNX + ix], d = dem[(iz + 1) * demNX + ix + 1];
            return ((a * (1f - tx) + b * tx) * (1f - tz) + (c * (1f - tx) + d * tx) * tz) * 0.1f;
        }

        static float NoiseY(float x, float z)
        {
            return ValueNoise(x, z, 1701f) * 8.0f
                 + ValueNoise(x + 9173f, z - 4711f, 613f) * 4.2f
                 + ValueNoise(x - 3137f, z + 8291f, 211f) * 1.5f;
        }

        static float ValueNoise(float x, float z, float wavelength)
        {
            float fx = x / wavelength, fz = z / wavelength;
            int ix = Mathf.FloorToInt(fx), iz = Mathf.FloorToInt(fz);
            float tx = fx - ix, tz = fz - iz;
            tx = tx * tx * (3f - 2f * tx);
            tz = tz * tz * (3f - 2f * tz);
            float a = Hash01(ix, iz), b = Hash01(ix + 1, iz);
            float c = Hash01(ix, iz + 1), d = Hash01(ix + 1, iz + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz) * 2f - 1f;
        }

        static float Hash01(int x, int z)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + z * 668265263) + 1442695041u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        // ------------------------------------------------------------------
        //  The solve, at load.
        // ------------------------------------------------------------------
        /// <summary>Which crossings the solver actually enforced after the
        /// mutual-conflict prune. The audit reads this: judging a pruned
        /// crossing by the clearance rule it was excused from is a false FAIL.</summary>
        public static bool[] EnforcedCrossings { get; private set; }
        /// <summary>Crossings solved as a trench under the over road rather
        /// than a hump over the under road. For the audit and the preview.</summary>
        public static bool[] TrenchedCrossings { get; private set; }
        public static int TrenchCount { get; private set; }

        public static void Solve(CityMap map)
        {
            crossingOn = null;
            crossingTarget = null;
            map.nodeY = new float[map.nodes.Length];
            for (int i = 0; i < map.nodes.Length; i++)
                map.nodeY[i] = BaseY(map.nodes[i].x, map.nodes[i].y);

            // 1. per-edge profile from the terrain
            foreach (var e in map.edges)
            {
                int n = Mathf.Max(2, Mathf.CeilToInt(e.length / StationStep) + 1);
                e.stS = new float[n];
                e.stY = new float[n];
                e.stElev = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    float at = i == n - 1 ? e.length : i * e.length / (n - 1);
                    e.stS[i] = at;
                    var p = e.PointAt(at);
                    e.stY[i] = BaseY(p.x, p.y);
                }
                Smooth(e.stY, 2.5f);
                ClampGrade(e, MaxGrade(e));
                BlendEndsToNodes(map, e);
                // OSM's bridges are decks end to end, whatever the terrain
                // under them does.
                if (e.bridge) for (int i = 0; i < n; i++) e.stElev[i] = true;
            }

            // 2. trenches, before anything lifts anything: they only ever
            // LOWER a freeway, and every raise below reads the lowered height.
            SinkTrenches(map);

            // 3-7. structure and junction agreement, to a fixed point.
            //
            // Two forces both only push roads UP: a crossing lifts its OVER
            // edge clear of the road below, and a junction node takes the
            // highest incident end so ramps meet the mainline they climb to.
            // Each can invalidate the other, so neither order of two passes
            // settles it. Monotone + bounded means iterating CONVERGES; run to
            // quiescence and finish on a raise, so the clearance rule is the
            // one that holds exactly.
            for (int it = 0; it < 4; it++)
            {
                RaiseAllCrossings(map);
                HoldWaterSpans(map);
                HoldBridges(map);
                float moved = ReconcileNodes(map);
                if (moved < 0.05f) break;
            }

            // 8. a gentle pass over the interiors: reconciliation can leave a
            // cliff INSIDE a mid-length edge (its ends are fixed by nodes that
            // genuinely disagree). Ends stay put; interiors ease. BEFORE the
            // final raise, so nothing can lower a deck after its clearance is
            // guaranteed.
            foreach (var e in map.edges)
            {
                // NEVER STEEPER THAN THE EDGE'S OWN ENDS. The forward sweep
                // pins to the start and the backward sweep to the end, so
                // when the ends disagree by more than the clamp can span the
                // two sweeps meet in a cliff at the first station — 2.4 m in
                // 10 m on a 124 m piece of I-277 whose nodes sat 9.7 m apart.
                // An edge that has to climb 7.8% end to end climbs 7.8% all
                // the way, which is what the road honestly does.
                int last = e.stY.Length - 1;
                float ends = Mathf.Abs(e.stY[last] - e.stY[0]) / Mathf.Max(e.length, 1f);
                float g = Mathf.Max(MaxGrade(e) * 1.6f, ends * 1.05f);
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 1; i < e.stY.Length - 1; i++)
                    {
                        float ds = e.stS[i] - e.stS[i - 1];
                        e.stY[i] = Mathf.Clamp(e.stY[i], e.stY[i - 1] - g * ds, e.stY[i - 1] + g * ds);
                    }
                    for (int i = e.stY.Length - 2; i >= 1; i--)
                    {
                        float ds = e.stS[i + 1] - e.stS[i];
                        e.stY[i] = Mathf.Clamp(e.stY[i], e.stY[i + 1] - g * ds, e.stY[i + 1] + g * ds);
                    }
                }
            }

            // Holds before the final raise; nothing below this line may move a
            // road except the raise itself. Three fresh passes: within one, an
            // under-road that is itself OVER something later in the same
            // layer group can rise after being measured.
            HoldWaterSpans(map);
            HoldBridges(map);
            RaiseAllCrossings(map, fresh: true);
            RaiseAllCrossings(map, fresh: true);
            RaiseAllCrossings(map, fresh: true);

            // THE ENDS MUST MEET, AND THE APPROACH MUST HAVE ROOM. The fresh
            // raises lift some edge ends; the snap takes the highest at each
            // node; but the other edges' ends stayed where they were, and at
            // a degree-2 node that is a step the size of the lift (4.6 m in
            // 1 m on the 277 belt, in the first race path built through the
            // graph). Blending each edge's own ends up to its nodes was the
            // first answer and it fails on short edges: a 36 m street
            // approach to a bridge lifted 6.6 m is a 37% ramp however it is
            // blended within itself. A real approach embankment runs back
            // through the next junctions, and so does this: every node whose
            // incident ends fall short of it seeds an APPROACH CONE at 4.5%
            // through the graph (RaiseCone), which carries on through any
            // node it still stands above. Raises only; iterated with the
            // crossing raise until nothing moves, ending on the cones so the
            // ends are exact (the audit's clearance margin covers the few
            // centimetres a final cone can take from a deck's underside).
            for (int k = 0; k < 8; k++)
            {
                RaiseAllCrossings(map, fresh: true);
                SnapNodesToEnds(map);
                if (RaiseConesFromNodes(map) == 0) break;
            }
            SnapNodesToEnds(map);

            // 9. mark structure LAST, so lifted approaches near reconciled
            // nodes get decks and lose their ground pin too.
            foreach (var e in map.edges)
            {
                for (int i = 0; i < e.stS.Length; i++)
                {
                    if (e.stElev[i]) continue;
                    var p = e.PointAt(e.stS[i]);
                    if (e.stY[i] > BaseY(p.x, p.y) + ElevMarginM) e.stElev[i] = true;
                }
            }

            // 10. lakes get one flat surface each; the shore owns the level
            foreach (var w in map.waters)
            {
                if (!w.lake) { w.surfaceY = 0f; continue; }
                float min = float.MaxValue;
                foreach (var p in w.pts) min = Mathf.Min(min, BaseY(p.x, p.y));
                w.surfaceY = min - 0.6f;
            }
        }

        /// <summary>
        /// Dig a freeway under the surface streets that cross it.
        ///
        /// Only where the OVER road is a street (or an expressway) and the
        /// UNDER road a freeway mainline. Everywhere else — freeway over
        /// freeway, ramp over anything — the hump rule stands. The street
        /// keeps its ground level and its OSM bridge tag makes it a deck;
        /// when the tag is a bare layer=1 with no bridge=yes the stations over
        /// the cut are marked structure here, or the street would pin the
        /// ground up under itself and float over the trench.
        ///
        /// THE CUT RUNS THROUGH THE INTERCHANGES. The first cut only trenched
        /// a crossing 150 m clear of the freeway's next junction, so the
        /// trough could climb back out before a node whose height is the
        /// highest of its incident ends. Uptown's freeways have a ramp every
        /// two hundred metres; most crossings failed the test, the street
        /// was humped 6.6 m onto a 58 m bridge, and its 36 m approach edges
        /// could not climb that (26% steps on North Caldwell Street). Real
        /// Charlotte keeps the street at grade and the Belk in one continuous
        /// cut, with the ramps climbing out of it. So: the trough is carried
        /// through the freeway's own nodes into the next mainline edge, those
        /// nodes are PINNED to the cut (<see cref="pinnedNodeY"/> — the one
        /// place the max-of-ends rule yields), and every ramp meeting them
        /// blends DOWN to the cut, once, before the raise loop begins.
        /// Afterwards the cut is an ordinary set of low heights the monotone
        /// solver only ever raises where something real demands it.
        /// </summary>
        static float[] pinnedNodeY;

        static void SinkTrenches(CityMap map)
        {
            TrenchedCrossings = new bool[map.crossings.Length];
            TrenchCount = 0;
            pinnedNodeY = new float[map.nodes.Length];
            for (int i = 0; i < pinnedNodeY.Length; i++) pinnedNodeY[i] = float.NaN;

            // which edges carry water: not dug, the cut would drown
            var wet = new bool[map.edges.Length];
            foreach (var ws in map.wspans) wet[ws.edge] = true;

            var pending = new List<(int edge, float sAt, float target)>();
            for (int ci = 0; ci < map.crossings.Length; ci++)
            {
                var c = map.crossings[ci];
                if (!c.forced) continue;
                var over = map.edges[c.over];
                var under = map.edges[c.under];
                if (under.cls < 5 || under.link || under.tunnel || wet[c.under]) continue;
                if (over.cls >= 5 || over.link) continue;
                ProjectOn(under, c.at, out float sU);
                if (sU < TrenchEndM || under.length - sU < TrenchEndM) continue;
                ProjectOn(over, c.at, out float sO);
                float target = over.YAt(sO) - ClearanceM - DeckThick;
                pending.Add((c.under, sU, target));
                float half = under.width * 0.5f + 7f;
                for (int i = 0; i < over.stS.Length; i++)
                    if (Mathf.Abs(over.stS[i] - sO) <= half) over.stElev[i] = true;
                TrenchedCrossings[ci] = true;
                TrenchCount++;
            }

            // Sink, and carry the trough through the mainline's nodes into
            // its neighbours until it has climbed back to their own profiles.
            var queue = new Queue<(int edge, float sAt, float target)>(pending);
            int guard = 0;
            while (queue.Count > 0 && guard++ < 200000)
            {
                var (ei, sAt, target) = queue.Dequeue();
                var e = map.edges[ei];
                bool moved = false;
                for (int i = 0; i < e.stS.Length; i++)
                {
                    float want = target + Mathf.Abs(e.stS[i] - sAt) * ApproachGrade;
                    if (e.stY[i] > want + 0.01f) { e.stY[i] = want; moved = true; }
                }
                if (!moved) continue;
                // the ends: pin the node and continue into every other mainline
                // edge there (not ramps — they climb out at their own grade)
                foreach (var (node, endS) in new[] { (e.a, 0f), (e.b, e.length) })
                {
                    float wantEnd = target + Mathf.Abs(endS - sAt) * ApproachGrade;
                    float endY = e.a == node ? e.stY[0] : e.stY[e.stY.Length - 1];
                    if (endY > wantEnd + 0.01f) continue;      // the trough faded before this end
                    if (!float.IsNaN(pinnedNodeY[node]) && pinnedNodeY[node] <= endY + 0.01f) continue;
                    pinnedNodeY[node] = endY;
                    foreach (var oi in map.nodeEdges[node])
                    {
                        if (oi == ei) continue;
                        var o = map.edges[oi];
                        if (o.cls < 5 || o.link || o.tunnel || wet[oi]) continue;
                        float oAt = o.a == node ? 0f : o.length;
                        queue.Enqueue((oi, oAt, endY));
                    }
                }
            }

            // Every edge meeting a pinned node takes the cut's height at that
            // end, now, once — the only lowering in the solve. A RAMP is cut
            // down along its own cone (8%, a ramp's grade) so a short one is
            // steep rather than stepped; a surface street that shares a node
            // with the cut blends over its usual length.
            foreach (var e in map.edges)
            {
                bool pa = !float.IsNaN(pinnedNodeY[e.a]), pb = !float.IsNaN(pinnedNodeY[e.b]);
                if (!pa && !pb) continue;
                if (pa) map.nodeY[e.a] = pinnedNodeY[e.a];
                if (pb) map.nodeY[e.b] = pinnedNodeY[e.b];
                if (e.link)
                {
                    foreach (var (node, fromA) in new[] { (e.a, true), (e.b, false) })
                    {
                        if (float.IsNaN(pinnedNodeY[node])) continue;
                        float cut = pinnedNodeY[node];
                        for (int i = 0; i < e.stS.Length; i++)
                        {
                            float dist = fromA ? e.stS[i] : e.length - e.stS[i];
                            float want = cut + dist * 0.08f;
                            if (e.stY[i] > want) e.stY[i] = want;
                        }
                    }
                }
                else BlendEndsToNodes(map, e);
            }
        }

        /// <summary>A tagged bridge holds at least the straight line between
        /// its two ends: a deck does not sag into the valley it was built to
        /// cross. Raises only, like everything else here.</summary>
        static void HoldBridges(CityMap map)
        {
            foreach (var e in map.edges)
            {
                if (!e.bridge || e.stY.Length < 3) continue;
                float y0 = e.stY[0], y1 = e.stY[e.stY.Length - 1];
                for (int i = 1; i < e.stY.Length - 1; i++)
                {
                    float hold = Mathf.Lerp(y0, y1, e.stS[i] / e.length);
                    if (e.stY[i] < hold) e.stY[i] = hold;
                }
            }
        }

        /// <summary>Every grade separation lifts its OVER edge clear of the
        /// under road's CURRENT height. Lowest stack first, so an edge that is
        /// over one road and under a third reads the raised height when the
        /// higher deck solves. Idempotent and monotonic: safe to run again.</summary>
        static bool[] crossingOn;
        static float[] crossingTarget;

        static void RaiseAllCrossings(CityMap map, bool fresh = false)
        {
            crossingOn ??= PruneMutualCrossings(map);
            EnforcedCrossings = crossingOn;
            if (crossingTarget == null || crossingTarget.Length != map.crossings.Length)
            {
                crossingTarget = new float[map.crossings.Length];
                for (int i = 0; i < crossingTarget.Length; i++) crossingTarget[i] = float.NaN;
            }
            var order = new List<int>();
            for (int i = 0; i < map.crossings.Length; i++) if (crossingOn[i]) order.Add(i);
            order.Sort((p, q) => map.edges[map.crossings[p].over].layer
                .CompareTo(map.edges[map.crossings[q].over].layer));
            foreach (var ci in order)
            {
                var c = map.crossings[ci];
                var over = map.edges[c.over];
                var under = map.edges[c.under];
                ProjectOn(over, c.at, out float sOver);
                // The target LATCHES on first computation. A ramp that both
                // MEETS a street at a node and passes under it further along
                // otherwise feeds back: crossing raises street, node lifts
                // ramp tip, next pass reads the lifted ramp and raises the
                // street again — one such cluster once ratcheted 24 m into
                // the sky. Stacks still solve: within the first pass, lower
                // decks are raised before higher ones read them.
                if (fresh || float.IsNaN(crossingTarget[ci]))
                {
                    ProjectOn(under, c.at, out float sUnder);
                    float t = under.YAt(sUnder) + ClearanceM + DeckThick;
                    crossingTarget[ci] = fresh && !float.IsNaN(crossingTarget[ci])
                        ? Mathf.Max(crossingTarget[ci], t) : t;
                }
                RaiseHump(over, sOver, crossingTarget[ci]);
            }
        }

        /// <summary>
        /// Two crossings of the SAME two edges in OPPOSITE directions, close
        /// enough that their approach humps overlap, cannot both hold at these
        /// grades — each iteration raised one past the other and the pair
        /// ratcheted 20 m into the sky (braided interchange ramps are where
        /// this lives). Keep the one whose over-edge carries the higher stack
        /// (layer, then class, then length).
        /// </summary>
        static bool[] PruneMutualCrossings(CityMap map)
        {
            var on = new bool[map.crossings.Length];
            for (int i = 0; i < on.Length; i++) on[i] = true;
            var byPair = new Dictionary<long, List<int>>();
            for (int i = 0; i < map.crossings.Length; i++)
            {
                var c = map.crossings[i];
                long a = Mathf.Min(c.over, c.under), b = Mathf.Max(c.over, c.under);
                long key = (a << 20) | b;
                if (!byPair.TryGetValue(key, out var list)) byPair[key] = list = new List<int>(2);
                list.Add(i);
            }
            int pruned = 0;
            foreach (var list in byPair.Values)
            {
                if (list.Count < 2) continue;
                for (int m = 0; m < list.Count; m++)
                    for (int n = m + 1; n < list.Count; n++)
                    {
                        var cm = map.crossings[list[m]];
                        var cn = map.crossings[list[n]];
                        if (cm.over == cn.over) continue;             // same direction: a real double-cross
                        if (!on[list[m]] || !on[list[n]]) continue;
                        if (Vector2.Distance(cm.at, cn.at) > 500f) continue;
                        var em = map.edges[cm.over];
                        var en = map.edges[cn.over];
                        bool mWins = em.layer != en.layer ? em.layer > en.layer
                                   : em.cls != en.cls ? em.cls > en.cls
                                   : em.length >= en.length;
                        on[mWins ? list[n] : list[m]] = false;
                        pruned++;
                    }
            }
            if (pruned > 0) Debug.Log($"[City] pruned {pruned} mutually-conflicting grade separations");
            return on;
        }

        /// <summary>Water spans hold a straight line between their approach
        /// heights and are always structure.</summary>
        static void HoldWaterSpans(CityMap map)
        {
            foreach (var ws in map.wspans)
            {
                var e = map.edges[ws.edge];
                float s0 = Mathf.Clamp(ws.s0, 0f, e.length);
                float s1 = Mathf.Clamp(ws.s1, 0f, e.length);
                if (s1 - s0 < 2f) continue;
                float y0 = e.YAt(s0), y1 = e.YAt(s1);
                for (int i = 0; i < e.stS.Length; i++)
                {
                    if (e.stS[i] < s0 || e.stS[i] > s1) continue;
                    float t = (e.stS[i] - s0) / (s1 - s0);
                    float hold = Mathf.Lerp(y0, y1, t);
                    if (e.stY[i] < hold) e.stY[i] = hold;
                    e.stElev[i] = true;
                }
            }
        }

        /// <summary>
        /// Junctions meet exactly: a node takes the HIGHEST incident end (a
        /// raised mainline lifts its ramps' tips, never the reverse) and every
        /// edge re-blends. A stub too short to ramp levels its two nodes — but
        /// ONLY over small disagreements: a big disagreement across a stub
        /// stays a steep little ramp, which is what the geometry honestly is.
        /// Returns how far any node moved, for convergence.
        /// </summary>
        const float RigidStubM = 25f;
        const float RigidStubMaxDelta = 2.5f;

        static float ReconcileNodes(CityMap map)
        {
            float moved = 0f;
            for (int n = 0; n < map.nodes.Length; n++)
            {
                float best = float.MinValue;
                foreach (var ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    float endY = e.a == n ? e.stY[0] : e.stY[e.stY.Length - 1];
                    if (endY > best) best = endY;
                }
                if (best > float.MinValue)
                {
                    moved = Mathf.Max(moved, Mathf.Abs(best - map.nodeY[n]));
                    map.nodeY[n] = best;
                }
            }
            foreach (var e in map.edges)
            {
                if (e.a == e.b) continue;
                // A stub or a fragment with one end in a cut is a ramp out
                // of the cut, however short; levelling it would lift the cut.
                if (pinnedNodeY != null && (!float.IsNaN(pinnedNodeY[e.a]) || !float.IsNaN(pinnedNodeY[e.b]))) continue;
                if (e.length < RigidStubM)
                {
                    float d = Mathf.Abs(map.nodeY[e.a] - map.nodeY[e.b]);
                    if (d > 0.01f && d < RigidStubMaxDelta)
                    {
                        float m = Mathf.Max(map.nodeY[e.a], map.nodeY[e.b]);
                        map.nodeY[e.a] = m;
                        map.nodeY[e.b] = m;
                        moved = Mathf.Max(moved, d);
                    }
                }
                else if (e.length < 90f)
                {
                    // A short viaduct fragment whose nodes disagree by more
                    // than it can climb gets its LOW node raised to what the
                    // climb can reach. Raising only, so clearances hold; the
                    // lift cascades outward through later rounds.
                    float feasible = e.length * MaxGrade(e) * 1.6f;
                    float hi = Mathf.Max(map.nodeY[e.a], map.nodeY[e.b]);
                    float lo = Mathf.Min(map.nodeY[e.a], map.nodeY[e.b]);
                    if (hi - lo > feasible)
                    {
                        float lift = hi - feasible - lo;
                        if (map.nodeY[e.a] < map.nodeY[e.b]) map.nodeY[e.a] += lift;
                        else map.nodeY[e.b] += lift;
                        moved = Mathf.Max(moved, lift);
                    }
                }
            }
            foreach (var e in map.edges) BlendEndsToNodes(map, e);
            return moved;
        }

        /// <summary>
        /// Lift the network around a node with an approach cone: every edge
        /// out of the node rises to at least (y - distance x ApproachGrade),
        /// and where the cone still stands above the far node it carries on
        /// through it — the same 4.5% RaiseHump uses within one edge, taken
        /// across junctions. Raises only. A one-metre sliver between a raised
        /// bridge and a low junction is simply passed through.
        /// </summary>
        static readonly List<(int node, float y)> coneQueue = new List<(int, float)>(64);
        static bool RaiseCone(CityMap map, int node, float y)
        {
            coneQueue.Clear();
            coneQueue.Add((node, y));
            int head = 0;
            bool any = false;
            while (head < coneQueue.Count && head < 4000)
            {
                var (n, ny) = coneQueue[head++];
                if (map.nodeY[n] < ny) map.nodeY[n] = ny;
                foreach (var ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    bool fromA = e.a == n;
                    bool moved = false;
                    for (int i = 0; i < e.stS.Length; i++)
                    {
                        float dist = fromA ? e.stS[i] : e.length - e.stS[i];
                        float want = ny - dist * ApproachGrade;
                        if (e.stY[i] < want - 0.02f) { e.stY[i] = want; moved = true; }
                    }
                    if (!moved) continue;
                    any = true;
                    int far = fromA ? e.b : e.a;
                    float farWant = ny - e.length * ApproachGrade;
                    if (farWant > map.nodeY[far] + 0.02f) coneQueue.Add((far, farWant));
                }
            }
            return any;
        }

        /// <summary>
        /// Seed a cone from every node that stands ABOVE THE GROUND — every
        /// node a raise or a snap has lifted — and from any node an incident
        /// end falls short of. Not from every node: a road descending a real
        /// 6% hill away from a junction is not an embankment, and a cone from
        /// there would flatten every downhill in the county to 4.5%. Returns
        /// how many cones actually moved something, for the convergence loop.
        /// </summary>
        static int RaiseConesFromNodes(CityMap map)
        {
            int seeded = 0;
            for (int n = 0; n < map.nodes.Length; n++)
            {
                float y = map.nodeY[n];
                bool seed = y > BaseY(map.nodes[n].x, map.nodes[n].y) + 0.5f;
                if (!seed)
                    foreach (var ei in map.nodeEdges[n])
                    {
                        var e = map.edges[ei];
                        float endY = e.a == n ? e.stY[0] : e.stY[e.stY.Length - 1];
                        if (endY < y - 0.02f) { seed = true; break; }
                    }
                if (!seed) continue;
                if (RaiseCone(map, n, y)) seeded++;
            }
            return seeded;
        }

        /// <summary>After the final raise, patches need node heights that match
        /// the raised arm ends — max of ends, no re-blend, so the clearance
        /// the raise just guaranteed is not disturbed.</summary>
        static void SnapNodesToEnds(CityMap map)
        {
            for (int n = 0; n < map.nodes.Length; n++)
            {
                float best = float.MinValue;
                foreach (var ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    float endY = e.a == n ? e.stY[0] : e.stY[e.stY.Length - 1];
                    if (endY > best) best = endY;
                }
                if (best > float.MinValue) map.nodeY[n] = best;
            }
        }

        /// <summary>Gaussian along the stations, sigma in stations. The SRTM
        /// grid is 60 m and a road is sampled every 10, so the raw profile
        /// carries the grid's facets; 25 m of smoothing takes them out
        /// without flattening a real hill.</summary>
        static readonly List<float> smoothScratch = new List<float>(256);
        static void Smooth(float[] y, float sigma)
        {
            int n = y.Length;
            if (n < 4) return;
            int r = Mathf.CeilToInt(sigma * 2.5f);
            smoothScratch.Clear();
            for (int i = 0; i < n; i++)
            {
                float sum = 0f, wsum = 0f;
                for (int k = -r; k <= r; k++)
                {
                    int j = Mathf.Clamp(i + k, 0, n - 1);
                    float w = Mathf.Exp(-k * k / (2f * sigma * sigma));
                    sum += y[j] * w; wsum += w;
                }
                smoothScratch.Add(sum / wsum);
            }
            for (int i = 0; i < n; i++) y[i] = smoothScratch[i];
        }

        static void ClampGrade(CityMap.Edge e, float g)
        {
            for (int i = 1; i < e.stY.Length; i++)
            {
                float ds = e.stS[i] - e.stS[i - 1];
                e.stY[i] = Mathf.Clamp(e.stY[i], e.stY[i - 1] - g * ds, e.stY[i - 1] + g * ds);
            }
            for (int i = e.stY.Length - 2; i >= 0; i--)
            {
                float ds = e.stS[i + 1] - e.stS[i];
                e.stY[i] = Mathf.Clamp(e.stY[i], e.stY[i + 1] - g * ds, e.stY[i + 1] + g * ds);
            }
        }

        /// <summary>Returns how far either end moved, for the final
        /// convergence loop.</summary>
        static float BlendEndsToNodes(CityMap map, CityMap.Edge e)
        {
            float L = Mathf.Min(e.length * 0.5f, 90f);
            if (L < 1f)
            {
                // a stub too short to blend just takes its nodes' line
                float ya = map.nodeY[e.a], yb = map.nodeY[e.b];
                float was = Mathf.Max(Mathf.Abs(ya - e.stY[0]), Mathf.Abs(yb - e.stY[e.stY.Length - 1]));
                for (int i = 0; i < e.stY.Length; i++)
                    e.stY[i] = Mathf.Lerp(ya, yb, e.length > 0f ? e.stS[i] / e.length : 0f);
                return was;
            }
            float dA = map.nodeY[e.a] - e.stY[0];
            float dB = map.nodeY[e.b] - e.stY[e.stY.Length - 1];
            for (int i = 0; i < e.stY.Length; i++)
            {
                float fromA = e.stS[i], fromB = e.length - e.stS[i];
                if (fromA < L) e.stY[i] += dA * (1f - fromA / L);
                if (fromB < L) e.stY[i] += dB * (1f - fromB / L);
            }
            return Mathf.Max(Mathf.Abs(dA), Mathf.Abs(dB));
        }

        static void RaiseHump(CityMap.Edge e, float sAt, float targetY)
        {
            // No reach cap, deliberately. A capped hump under a four-level
            // stack ended in a fifteen-metre CLIFF at the cap — which the
            // grade-relax pass then "fixed" by hauling the whole deck down
            // through the road it was built to clear. The cone fades below
            // terrain on its own; distant stations are a comparison and a no-op.
            for (int i = 0; i < e.stS.Length; i++)
            {
                float want = targetY - Mathf.Abs(e.stS[i] - sAt) * ApproachGrade;
                if (e.stY[i] < want) e.stY[i] = want;
            }
        }

        public static void ProjectOn(CityMap.Edge e, Vector2 p, out float arcS)
        {
            float best = float.MaxValue; arcS = 0f;
            for (int i = 0; i + 1 < e.pts.Length; i++)
            {
                Vector2 a = e.pts[i], d = e.pts[i + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                Vector2 q = a + d * t;
                float dd = (p - q).sqrMagnitude;
                if (dd < best) { best = dd; arcS = e.s[i] + Mathf.Sqrt(L2) * t; }
            }
        }

        // ------------------------------------------------------------------
        //  Ground height, per query. Tile-local by construction.
        // ------------------------------------------------------------------
        static HashSet<int> segScratch;
        static HashSet<int> waterScratch;

        public const float MaxCorridorHalf = 24f;

        public static float GroundY(CityMap map, float x, float z)
        {
            float baseY = BaseY(x, z);

            // creeks carve, lakes sink
            waterScratch ??= new HashSet<int>();
            waterScratch.Clear();
            float reachW = 40f;
            map.WaterSegsInRect(new Vector2(x - reachW, z - reachW), new Vector2(x + reachW, z + reachW), waterScratch);
            var p2 = new Vector2(x, z);
            foreach (var packed in waterScratch)
            {
                int wi = packed >> 12, si = packed & 0xFFF;
                var w = map.waters[wi];
                if (w.lake)
                {
                    // inside: pinned under the surface; near shore: blended down
                    if (CityMap.PointInPoly(w.pts, p2))
                        baseY = Mathf.Min(baseY, w.surfaceY - 2.2f);
                    else
                    {
                        float dsh = DistToSeg(w.pts, si, p2);
                        if (dsh < 14f)
                            baseY = Mathf.Min(baseY, Mathf.Lerp(w.surfaceY + 0.4f, baseY, dsh / 14f));
                    }
                }
                else
                {
                    float reach = w.width * 0.5f + 16f;
                    float d = DistToSeg(w.pts, si, p2);
                    if (d < reach)
                    {
                        float t = 1f - d / reach;
                        t = t * t * (3f - 2f * t);
                        baseY -= 3.6f * t;
                    }
                }
            }

            // road corridors pin the land to the tarmac (grounded stations only)
            segScratch ??= new HashSet<int>();
            segScratch.Clear();
            float reachR = MaxCorridorHalf + CorridorBlend;
            map.EdgeSegsInRect(new Vector2(x - reachR, z - reachR), new Vector2(x + reachR, z + reachR), segScratch);

            float wSum = 0f, tSum = 0f, wMax = 0f, tMin = float.MaxValue;
            foreach (var packed in segScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p2 - a, d) / L2) : 0f;
                Vector2 q = a + d * t;
                float dist = Vector2.Distance(p2, q);
                float ch = Mathf.Min(e.CorridorHalf, MaxCorridorHalf);
                if (dist > ch + CorridorBlend) continue;
                float at = e.s[si] + Mathf.Sqrt(L2) * t;
                if (e.ElevatedAt(at)) continue;        // structure does not pin the land
                float target = e.YAt(at) - CorridorSink;
                float w = dist <= ch ? 1f
                    : 1f - (dist - ch) / CorridorBlend;
                w = w * w * (3f - 2f * w);
                wSum += w; tSum += target * w;
                if (w > wMax) wMax = w;
                if (dist <= ch && target < tMin) tMin = target;
            }
            if (wSum > 1e-4f)
            {
                float target = tSum / wSum;
                baseY = Mathf.Lerp(baseY, target, wMax);
                // ...and never above the LOWEST tarmac this point is actually
                // under. The blend above is a weighted MEAN, so where two roads
                // at different heights share a corridor it settles BETWEEN
                // them, which is above the lower road.
                if (tMin < float.MaxValue) baseY = Mathf.Min(baseY, tMin);
            }
            return baseY;
        }

        static float DistToSeg(Vector2[] pts, int si, Vector2 p)
        {
            if (si + 1 >= pts.Length) si = Mathf.Max(0, pts.Length - 2);
            Vector2 a = pts[si], d = pts[si + 1] - a;
            float L2 = d.sqrMagnitude;
            float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
            return Vector2.Distance(p, a + d * t);
        }

        /// <summary>Water surface height for a river point. The bed is carved
        /// 3.6 m; the surface sits 1.4 m up it, because the ground lattice is
        /// 8 m and a surface half a metre above the centreline's dip was
        /// under the grass everywhere but the one vertex that hit the
        /// centreline — creeks were invisible from every bridge. Lakes use
        /// their flat surfaceY instead.</summary>
        public static float RiverSurfaceY(float x, float z) => BaseY(x, z) - 3.6f + 1.4f;
    }
}
