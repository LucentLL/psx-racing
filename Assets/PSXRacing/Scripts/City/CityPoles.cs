using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// CHARLOTTE'S UTILITY POLES AND WIRES (plan WP-15, release R6). The
    /// survey: "Utility poles and wires are the most visible thing the game
    /// lacks. Wood poles 12-14 m tall, 40-50 m apart, usually one side, 1-3 m
    /// behind the curb or edge. 2-4 wire levels: a primary crossarm on top,
    /// then secondary and telecom lower. They are on every arterial and
    /// two-lane in the sample except uptown, freeways and Myers Park.
    /// Cobra-head street lights usually hang on these poles."
    ///
    /// WHICH ROADS (<see cref="IsPoleEdge"/>): every street, collector,
    /// arterial and trunk street outside uptown (inside the freeway loop,
    /// measured off the map: <see cref="Uptown"/>) and Myers Park
    /// (<see cref="InMyersPark"/>) - no ramps, tunnels, roundabouts, freeways,
    /// or trunk expressways (posted 85 km/h and over). On those roads the
    /// cobra-head on every pole IS the street light: CityMeshes.PlaceLamps
    /// stands no lamp post there (pitch 38-55 m -> the poles' 35-55 m).
    /// Uptown's streets get black acorn posts instead (PlaceLamps, lamp kind
    /// <see cref="CityMeshes.LampAcorn"/>).
    ///
    /// POLE LINES: a road is followed through every node where it plainly
    /// carries on (the straightest arm, the same name at a junction, one-way
    /// with one-way the same way; <see cref="Chains"/>), so a line runs down a
    /// road on ONE side across its way splits and junctions: the outside of a
    /// one-way carriageway (a divided road gets a line along each outside),
    /// a side hashed from the line otherwise. Stations every
    /// <see cref="PitchM"/> +- <see cref="JitterM"/> along the line.
    ///
    /// WHERE A POLE STANDS: square off its station, past the pavement edge
    /// (<see cref="CityMap.Edge.PaveEdgeM"/>) and the road's clear zone
    /// (<see cref="RoadsideOccupancy.ClearZoneOf"/>) by 1-3.4 m, on the first
    /// spot the STATIC roadside mask (<see cref="RoadsideOccupancy.Static"/>)
    /// leaves free - no pavement, clear zone, sight triangle, corner spot,
    /// building, lot (a model's yard and driveway), water, ground under a
    /// deck or race run-off - and off every road's keep-out
    /// (<see cref="CitySigns.RoadsClear"/>), nudged a few metres along if its
    /// own spot is taken; else none. The tile build's own fill houses stand
    /// at least 24 m off every road and its lamp posts inside the clear
    /// zones, so no tile has anything of its own under a pole: every pole is
    /// decided from GLOBAL data alone, and every tile finds the same ones.
    ///
    /// WIRES: between consecutive poles of a line up to <see cref="MaxSpanM"/>
    /// apart, a primary pair on the crossarm and one to three lower levels
    /// (neutral, telecom), sagging 1.5% of the span, never under
    /// <see cref="WireClearM"/> over any road (checked every 2 m, and the
    /// span dropped if not) nor 4 m over the ground, never through a deck or
    /// a building. Drawn as ribbons PSX/Lit's PSX_FURNITURE variant stands
    /// square to the eye at least a pixel wide and fades out from 100 m, so a
    /// wire neither breaks into crawling dashes nor draws a hairline to the
    /// horizon.
    ///
    /// OWNERSHIP: a pole belongs to the tile its foot stands in, a span to
    /// its first pole's tile. DRAWN in one mesh a tile with the tile's street
    /// lamps (re-emitted from its lamp list) on the city kit's furniture atlas
    /// (<see cref="CityKit.furniture"/>, tools/city/furniture_atlas.py: pack
    /// wood and metal): the lamp posts' own mesh is destroyed when it
    /// stands, so a tile's poles, wires and lamps are the one draw its lamps
    /// were. SOLID: a box up each pole on a Solid-layer object named
    /// <see cref="PostName"/> (Q15: utility poles are solid); the cobra-heads
    /// light the road at night through the tile's NightGlow like any lamp.
    ///
    /// THE SIGNS KEEP CLEAR (the WP-15 review found the telecom cables, 6-9 m
    /// up and a few metres past the clear zone, running straight through
    /// business cabinets standing in the same band): a billboard's post and
    /// faces and a business's cabinet stand only where
    /// <see cref="SignClear"/> finds no wire, pole, crossarm or cobra-head
    /// within <see cref="SignAirM"/> in plan, stepping back from the road
    /// past the line as they already step back from everything else. Poles
    /// never look at signs, so they are decided first and the same either
    /// way; the sign audit measures every wire and pole part against every
    /// sign box in 3D.
    /// </summary>
    public static class CityPoles
    {
        /// <summary>Tools switch the poles off for an A/B (the lamps come back).</summary>
        public static bool Enabled = true;
        /// <summary>The Solid-layer object holding a tile's pole colliders:
        /// the signs' posts' name, the audits' and play checks' one prefix.</summary>
        public const string PostName = CitySigns.PostName;

        // ---- the line
        public const float PitchM = 45f, JitterM = 5f;
        /// <summary>How far past the clear zone a foot is tried, in order.</summary>
        static readonly float[] BackM = { 1.0f, 1.8f, 2.6f, 3.4f };
        /// <summary>Along the road from its station, in order.</summary>
        static readonly float[] Nudges = { 0f, 3f, -3f, 6f, -6f };
        public const float MaxSpanM = 70f, MinSpanM = 8f;
        /// <summary>The lowest wire over any road's pavement (plan: 5.5 m).</summary>
        public const float WireClearM = 5.5f;
        /// <summary>... and over the ground between the poles.</summary>
        public const float WireGroundM = 4.0f;
        public const float SagShare = 0.015f;
        /// <summary>A foot more than this above or below the road's surface is
        /// on some other bank than the road's.</summary>
        public const float MaxBankM = 3f;
        /// <summary>Poles sink this far into their ground (a sloped verge shows no daylight under them).</summary>
        public const float SinkM = 0.3f;
        public const float MinHeightM = 12f, MaxHeightM = 14f;
        /// <summary>A trunk road posted this fast or faster is an expressway: no poles.</summary>
        public const float ExpresswayKmh = 85f;

        // ---- the pole
        const float PoleW = 0.30f;
        public const float ColliderW = 0.34f;
        const float CrossArmL = 2.4f, CrossArmW = 0.10f, CrossArmH = 0.12f, CrossArmDown = 0.35f;
        const float PrimaryOff = 1.05f;
        /// <summary>Wire levels under the pole's top: the crossarm's pair, the
        /// neutral, the telecom cable, a second cable.</summary>
        const float NeutralDown = 1.9f, TelecomDown = 4.6f, Telecom2Down = 5.2f;
        const float PrimaryHalfW = 0.012f, NeutralHalfW = 0.015f, TelecomHalfW = 0.03f;
        const int WireSegments = 6;
        // ---- the cobra-head: its lens this high over the road, its arm as long as a davit's
        const float LensOverRoadM = 8.2f, LensUnderTopM = 3.0f, LensOverFootM = 6.0f;
        const float ArmMinM = 1.6f, ArmMaxM = 4.6f, ArmBackM = 0.4f;
        const float ArmW = 0.10f, HeadL = 0.75f, HeadH = 0.18f, HeadW = 0.40f;
        /// <summary>The ground a pole takes on the tile's mask (trees and signs keep off it).</summary>
        public const float FootR = 1.2f;
        /// <summary>The farthest a foot stands from its station's centreline
        /// point: the widest half width, a trunk's clear zone, the last offset
        /// and nudge, and a margin.</summary>
        const float StationReachM = 34f;

        // ---- what a sign keeps clear of (the WP-15 review: the telecom
        //      cables ran straight through business cabinets on the arterials)
        /// <summary>How far either side of the line between two poles' feet a
        /// span's wires reach in plan: the crossarm's primaries on their pins
        /// (each pole's own outward: a wire between two such points never
        /// strays further off the foot line than they do).</summary>
        public const float WireReachM = PrimaryOff + 0.08f;
        /// <summary>The air a sign's part keeps, in plan, from any wire, pole,
        /// crossarm or cobra-head, besides its own half size.</summary>
        public const float SignAirM = 0.5f;
        /// <summary>The largest half size a sign's part asks about (a
        /// billboard face with its catwalk and floodlights).</summary>
        public const float SignPartMaxR = 1.0f;
        /// <summary>How far round a sign's part the stations are looked at: a
        /// span near it has a pole within half a span of it, that pole's foot
        /// is within <see cref="StationReachM"/> of its station, and the keep
        /// itself.</summary>
        const float KeepReachM = MaxSpanM * 0.5f + StationReachM + WireReachM + SignAirM + CrossArmL * 0.5f;

        // ---- the atlas (tools/city/furniture_atlas.py): texel cells from the bottom left
        public const float AtlasPx = 256f;
        public static readonly Color32 CellWood = new Color32(0, 0, 127, 255), CellMetal = new Color32(128, 0, 127, 255),
                                       CellBlack = new Color32(0, 128, 127, 255), CellWire = new Color32(128, 128, 127, 255);

        public struct Pole
        {
            public int chain, k, edge;
            public float s;
            /// <summary>The foot (world; y = the ground less <see cref="SinkM"/>) and the top.</summary>
            public Vector3 foot;
            public float top;
            /// <summary>Away from the road (plan unit), along the line (the chain's way).</summary>
            public Vector2 outward, along;
            /// <summary>The cobra-head's lens (world): its light.</summary>
            public Vector3 lens;
            /// <summary>Wire levels this line carries (2-4).</summary>
            public int levels;
            public float roadY;
        }

        public sealed class PoleTile
        {
            public int tx, tz;
            /// <summary>The poles this tile owns (their feet are in it).</summary>
            public readonly List<Pole> poles = new List<Pole>();
            /// <summary>The spans this tile owns (their first pole is its).</summary>
            public readonly List<(Pole a, Pole b)> spans = new List<(Pole, Pole)>();
            /// <summary>Cobra-head lenses, world: lamps at night.</summary>
            public readonly List<Vector3> heads = new List<Vector3>();
            /// <summary>Poles, wires, heads and the tile's street lamps: one mesh, one material.</summary>
            public Mesh mesh;
            /// <summary>Stations whose foot would be in this tile, and those no pole stood at.</summary>
            public int stations, refused, lampsDrawn, wires;
            /// <summary>Refused stations by why (<see cref="WhyNames"/>'s order; a
            /// spot refused for several mask bits counts under each).</summary>
            public readonly int[] refusedWhy = new int[12];
            /// <summary>Spans not strung, by why: too long, a road under them too
            /// close, the ground too close, a deck or a building in the way.</summary>
            public int spanLong, spanRoad, spanGround, spanBlocked;
            public float ms;
        }

        // ==================================================================
        //  WHICH ROADS
        // ==================================================================

        /// <summary>The speed a road is judged by: its posted limit, else its class's.</summary>
        static float SpeedOf(CityMap.Edge e) => e.speedKmh > 0 ? e.speedKmh : e.cls >= 5 ? 105f : 56f;

        /// <summary>Does this road carry a pole line (its lamps are the poles' cobra-heads)?</summary>
        public static bool IsPoleEdge(CityMap map, CityMap.Edge e)
        {
            if (e.link || e.tunnel || e.roundabout || e.cls >= 5 || e.a == e.b) return false;
            if (e.cls == 4 && SpeedOf(e) >= ExpresswayKmh) return false;
            var mid = e.PointAt(e.length * 0.5f);
            return !Uptown(map, mid) && !InMyersPark(mid);
        }

        /// <summary>Is this an uptown street (inside the freeway loop): black
        /// acorn posts both sides, no wires.</summary>
        public static bool IsAcornEdge(CityMap map, CityMap.Edge e) =>
            !e.link && !e.tunnel && e.cls <= 3 && Uptown(map, e.PointAt(e.length * 0.5f));

        // ---- uptown: inside the freeway loop, measured off the map ------
        const int LoopSectors = 32;
        const float LoopLookM = 3000f, LoopInsetM = 40f;
        static CityMap loopMap;
        static float[] loopR;

        /// <summary>Inside the I-277 / I-77 loop round uptown: nearer
        /// <see cref="CityMap.uptown"/> than the nearest motorway in its
        /// bearing (32 sectors, the radius eased between sector middles), less
        /// <see cref="LoopInsetM"/>.</summary>
        public static bool Uptown(CityMap map, Vector2 p)
        {
            if (loopMap != map || loopR == null) BuildLoop(map);
            var d = p - map.uptown;
            float r = d.magnitude;
            if (r > LoopLookM) return false;
            float a = (Mathf.Atan2(d.y, d.x) / (2f * Mathf.PI) + 1f) % 1f * LoopSectors - 0.5f;
            int i0 = Mathf.FloorToInt(a);
            float t = a - i0;
            float R = Mathf.Lerp(loopR[(i0 + LoopSectors) % LoopSectors], loopR[(i0 + 1 + LoopSectors) % LoopSectors], t);
            return r < R - LoopInsetM;
        }

        static void BuildLoop(CityMap map)
        {
            loopMap = map;
            loopR = new float[LoopSectors];
            for (int i = 0; i < LoopSectors; i++) loopR[i] = 0f;
            var near = new float[LoopSectors];
            for (int i = 0; i < LoopSectors; i++) near[i] = float.MaxValue;
            foreach (var e in map.edges)
            {
                if (e.cls < 5 || e.link) continue;
                for (int i = 0; i + 1 < e.pts.Length; i++)
                {
                    // every 20 m of the line, so a long segment is seen in every sector it crosses
                    var a = e.pts[i]; var b = e.pts[i + 1];
                    int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 20f));
                    for (int k = 0; k <= n; k++)
                    {
                        var d = Vector2.Lerp(a, b, k / (float)n) - map.uptown;
                        float r = d.magnitude;
                        if (r > LoopLookM) continue;
                        int s = Mathf.Clamp(Mathf.FloorToInt((Mathf.Atan2(d.y, d.x) / (2f * Mathf.PI) + 1f) % 1f * LoopSectors), 0, LoopSectors - 1);
                        if (r < near[s]) near[s] = r;
                    }
                }
            }
            for (int i = 0; i < LoopSectors; i++) loopR[i] = near[i] < float.MaxValue ? near[i] : 0f;
        }

        // ---- Myers Park: a polygon round its streets (OSM, the street names'
        //      extent; lat/lon, exporter projection) --------------------------
        static readonly double[,] MyersParkLL =
        {
            { 35.2070, -80.8390 }, { 35.2075, -80.8230 }, { 35.1990, -80.8130 }, { 35.1830, -80.8140 },
            { 35.1770, -80.8300 }, { 35.1790, -80.8440 }, { 35.1930, -80.8450 },
        };
        static Vector2[] myersPark;

        public static bool InMyersPark(Vector2 p)
        {
            if (myersPark == null)
            {
                myersPark = new Vector2[MyersParkLL.GetLength(0)];
                for (int i = 0; i < myersPark.Length; i++) myersPark[i] = LL(MyersParkLL[i, 0], MyersParkLL[i, 1]);
            }
            return CityMap.PointInPoly(myersPark, p);
        }

        /// <summary>The exporter's projection (export_osm.mjs L144-191).</summary>
        static Vector2 LL(double lat, double lon)
        {
            const double Lat0 = 35.18456015184093, Lon0 = -80.81770185962013;
            double mLon = 111320.0 * System.Math.Cos(Lat0 * System.Math.PI / 180.0);
            return new Vector2((float)((lon - Lon0) * mLon), (float)((lat - Lat0) * 111132.0)) * CityMap.LayoutScale;
        }

        // ==================================================================
        //  THE LINES (per map)
        // ==================================================================

        sealed class Lines
        {
            public int[] chainOf;       // per edge, -1: no pole line
            public sbyte[] dirOf;       // +1 its points run the chain's way
            public float[] startOf;     // the chain's arc at the edge's chain-start end
            public int[][] edgesOf;     // per chain, in order
            public float[] lengthOf;    // per chain
            public sbyte[] sideOf;      // per chain: +1 right of the chain's way
            public byte[] levelsOf;     // per chain: 2-4 wire levels
        }
        static Lines lines;
        static CityMap linesMap;

        static Lines Chains(CityMap map)
        {
            if (linesMap == map && lines != null) return lines;
            linesMap = map;
            int ne = map.edges.Length;
            var elig = new bool[ne];
            for (int i = 0; i < ne; i++) elig[i] = IsPoleEdge(map, map.edges[i]);
            // each end's continuation, kept only where it is mutual
            var cont = new int[ne * 2];
            for (int i = 0; i < ne; i++)
            {
                cont[i * 2] = elig[i] ? BestNext(map, elig, i, map.edges[i].a) : -1;
                cont[i * 2 + 1] = elig[i] ? BestNext(map, elig, i, map.edges[i].b) : -1;
            }
            int Link(int ei, int node)
            {
                var e = map.edges[ei];
                int o = cont[ei * 2 + (node == e.a ? 0 : 1)];
                if (o < 0) return -1;
                var oe = map.edges[o];
                return cont[o * 2 + (node == oe.a ? 0 : 1)] == ei ? o : -1;
            }
            var L = new Lines
            {
                chainOf = new int[ne], dirOf = new sbyte[ne], startOf = new float[ne],
            };
            for (int i = 0; i < ne; i++) L.chainOf[i] = -1;
            var edgesOf = new List<int[]>();
            var lengthOf = new List<float>();
            var sideOf = new List<sbyte>();
            var levelsOf = new List<byte>();
            var run = new List<int>();
            var runDir = new List<sbyte>();
            for (int i = 0; i < ne; i++)
            {
                if (!elig[i] || L.chainOf[i] >= 0) continue;
                // back to the start: out through each edge's a end until the line
                // ends, or comes round to i (a loop starts at i, its lowest edge)
                int cur = i, entry = map.edges[i].b;   // walking backward: we leave through a
                int guard = 0;
                while (guard++ < ne)
                {
                    var ce = map.edges[cur];
                    int exitNode = entry == ce.a ? ce.b : ce.a;   // backward: exit through the far end
                    int prev = Link(cur, exitNode);
                    if (prev < 0 || prev == i) break;
                    cur = prev; entry = exitNode;
                }
                // cur is the start edge; the chain runs out of cur's end that is
                // NOT linked backward (its free end is its entry)
                var se = map.edges[cur];
                int startEntry;
                {
                    int lA = Link(cur, se.a), lB = Link(cur, se.b);
                    if (cur == i && lA >= 0 && lB >= 0) startEntry = se.a;          // a loop: enter i at a
                    else if (lA < 0) startEntry = se.a;
                    else startEntry = se.b;
                }
                run.Clear(); runDir.Clear();
                int c = cur, en = startEntry;
                guard = 0;
                while (c >= 0 && L.chainOf[c] < 0 && guard++ < ne)
                {
                    var ce = map.edges[c];
                    L.chainOf[c] = edgesOf.Count;
                    run.Add(c);
                    runDir.Add((sbyte)(en == ce.a ? 1 : -1));
                    int exitNode = en == ce.a ? ce.b : ce.a;
                    int nx = Link(c, exitNode);
                    en = exitNode;
                    c = nx;
                }
                int id = edgesOf.Count;
                float arc = 0f;
                bool oneway = map.edges[run[0]].oneway;
                for (int r = 0; r < run.Count; r++)
                {
                    L.dirOf[run[r]] = runDir[r];
                    L.startOf[run[r]] = arc;
                    arc += map.edges[run[r]].length;
                }
                edgesOf.Add(run.ToArray());
                lengthOf.Add(arc);
                // a one-way line on the right of its traffic (the outside of a
                // divided road); a two-way line on the side its hash picks
                sbyte side = oneway ? runDir[0] : (Hash01(id, 17, 3) < 0.5f ? (sbyte)-1 : (sbyte)1);
                sideOf.Add(side);
                levelsOf.Add((byte)(2 + Mathf.Min(2, Mathf.FloorToInt(Hash01(id, 23, 5) * 3f))));
            }
            L.edgesOf = edgesOf.ToArray();
            L.lengthOf = lengthOf.ToArray();
            L.sideOf = sideOf.ToArray();
            L.levelsOf = levelsOf.ToArray();
            lines = L;
            cache.Clear(); spanCache.Clear();
            return L;
        }

        /// <summary>The arm the road carries on into at a node: the
        /// straightest eligible one within 60 degrees at a two-arm node, within
        /// 30 and of the same name at a junction; a one-way only into a one-way
        /// going on the same way. -1: the line ends here.</summary>
        static int BestNext(CityMap map, bool[] elig, int ei, int node)
        {
            var e = map.edges[ei];
            // the direction the road ARRIVES at the node, walking toward it
            Vector2 inDir = node == e.b ? e.TangentAt(e.length) : -e.TangentAt(0f);
            int arms = 0;
            foreach (int oi in map.nodeEdges[node]) if (map.edges[oi].a != map.edges[oi].b) arms++;
            float cosMin = arms <= 2 ? 0.5f : 0.866f;
            int best = -1; float bestCos = cosMin;
            foreach (int oi in map.nodeEdges[node])
            {
                if (oi == ei || !elig[oi]) continue;
                var o = map.edges[oi];
                if (o.a == o.b) continue;
                if (o.oneway != e.oneway) continue;
                Vector2 outDir = node == o.a ? o.TangentAt(0f) : -o.TangentAt(o.length);
                if (e.oneway)
                {
                    // traffic arrives along e and leaves along o
                    bool eArrives = node == e.b, oLeaves = node == o.a;
                    if (eArrives != oLeaves) continue;
                }
                if (arms > 2 && (string.IsNullOrEmpty(e.name) || e.name != o.name)) continue;
                float c = Vector2.Dot(inDir, outDir);
                if (c > bestCos) { bestCos = c; best = oi; }
            }
            return best;
        }

        // ==================================================================
        //  THE POLES (decided once a session from global data)
        // ==================================================================

        /// <summary>A station's outcome; when no pole stood, why its own spot
        /// (no nudge, the nearest offset) was refused: the static mask's bits
        /// there, or <see cref="WhyRoad"/>, <see cref="WhyBank"/>,
        /// <see cref="WhyDeck"/>, <see cref="WhyZone"/>.</summary>
        struct Decision { public bool standing; public Pole pole; public int why; }
        public const int WhyRoad = 256, WhyBank = 512, WhyDeck = 1024, WhyZone = 2048;
        /// <summary>The refusal reasons' names, by bit (mask bits 0-7, then the four above).</summary>
        public static readonly string[] WhyNames =
        {
            "pavement", "clear zone", "sight triangle", "corner spot", "building", "water", "under a deck", "lot",
            "another road's keep-out", "a bank over 3 m", "a deck or bridge", "uptown or Myers Park",
        };
        static readonly Dictionary<long, Decision> cache = new Dictionary<long, Decision>();
        static readonly Dictionary<long, int> spanCache = new Dictionary<long, int>();
        static CityMap mapNow;
        static CityMeshes.Trims trimsNow;
        static Dictionary<long, List<CityBuildings.B>> buildingsNow;

        /// <summary>Forget every pole decided (tools that change what they are decided from).</summary>
        public static void ClearCache() { cache.Clear(); spanCache.Clear(); lines = null; linesMap = null; }

        static void Setup(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings)
        {
            if (mapNow != map || trimsNow != trims || buildingsNow != buildings)
            {
                cache.Clear(); spanCache.Clear();
                mapNow = map; trimsNow = trims; buildingsNow = buildings;
            }
            Chains(map);
        }

        static long Key(int chain, int k) => ((long)chain << 24) ^ (uint)(k & 0xFFFFFF);

        /// <summary>Where station k of a line is along it.</summary>
        static float StationArc(int chain, int k) => (k + 0.5f) * PitchM + (Hash01(chain, k, 11) - 0.5f) * 2f * JitterM;

        /// <summary>Station k of a line: the pole that stands for it, if any.</summary>
        static Decision Decide(int chain, int k)
        {
            long key = Key(chain, k);
            if (cache.TryGetValue(key, out var d)) return d;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            d = DecideNow(chain, k);
            DecideMs += Ms(t0); Decisions++;
            cache[key] = d;
            return d;
        }

        /// <summary>The profile the pole audit prints: stations decided and
        /// their milliseconds, spans judged and theirs (a station's static
        /// masks included where it built one).</summary>
        public static double DecideMs, SpanMs;
        public static int Decisions, Spans;
        static double Ms(long t0) => (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        static Decision DecideNow(int chain, int k)
        {
            var L = lines;
            var none = new Decision();
            float arc = StationArc(chain, k);
            if (k < 0 || arc < 0f || arc > L.lengthOf[chain]) return none;
            var run = L.edgesOf[chain];
            int lo = 0, hi = run.Length - 1;
            while (lo < hi) { int mid = (lo + hi + 1) >> 1; if (L.startOf[run[mid]] <= arc) lo = mid; else hi = mid - 1; }
            int ei = run[lo];
            var map = mapNow;
            var e = map.edges[ei];
            float local = arc - L.startOf[ei];
            float s = L.dirOf[ei] > 0 ? local : e.length - local;
            if (e.bridge) { none.why = WhyDeck; return none; }
            int side = L.sideOf[chain] * L.dirOf[ei];
            none.why = -1;
            float clear = RoadsideOccupancy.ClearZoneOf(e);
            foreach (float back in BackM)
                foreach (float dS in Nudges)
                {
                    float s2 = s + dS;
                    if (s2 < 0.5f || s2 > e.length - 0.5f || e.ElevatedAt(s2)) { if (none.why < 0) none.why = WhyDeck; continue; }
                    RoadsideOccupancy.RoadEdgeAt(e, trimsNow, s2, out var p, out _, out float hwL, out float hwR);
                    var t = e.TangentAt(s2);
                    var outward = new Vector2(t.y, -t.x) * side;   // right of the points is +1
                    float edgeOff = Mathf.Max(e.PaveEdgeM(s2, side), Mathf.Max(hwL, hwR));
                    var f = p + outward * (edgeOff + clear + back);
                    // none uptown or in Myers Park, wherever its road runs
                    if (Uptown(map, f) || InMyersPark(f)) { if (none.why < 0) none.why = WhyZone; continue; }
                    byte bits = RoadsideOccupancy.StaticAt(map, trimsNow, buildingsNow, f);
                    if (bits != 0) { if (none.why < 0) none.why = bits; continue; }
                    if (!CitySigns.RoadsClear(map, trimsNow, f)) { if (none.why < 0) none.why = WhyRoad; continue; }
                    float roadY = e.YAt(s2);
                    float ground = Mathf.Min(CityMeshes.LatticeAt(map, f.x, f.y), CityElevation.GroundY(map, f.x, f.y));
                    if (Mathf.Abs(ground - roadY) > MaxBankM) { if (none.why < 0) none.why = WhyBank; continue; }
                    float h = Mathf.Lerp(MinHeightM, MaxHeightM, Hash01(chain, k, 7));
                    float footY = ground - SinkM;
                    float top = footY + h;
                    // the cobra-head: over the road's edge, a street lamp's height over the road
                    float lensY = Mathf.Clamp(roadY + LensOverRoadM, footY + LensOverFootM, top - LensUnderTopM);
                    float arm = Mathf.Clamp(clear + back - ArmBackM, ArmMinM, ArmMaxM);
                    var lens = f - outward * arm;
                    var along = t * L.dirOf[ei];
                    return new Decision
                    {
                        standing = true,
                        pole = new Pole
                        {
                            chain = chain, k = k, edge = ei, s = s2,
                            foot = new Vector3(f.x, footY, f.y), top = top,
                            outward = outward, along = along,
                            lens = new Vector3(lens.x, lensY, lens.y),
                            levels = L.levelsOf[chain], roadY = roadY,
                        },
                    };
                }
            return none;
        }

        /// <summary>The next pole a span from station k strings to (k+1, or
        /// k+2 over a station that stood none), or -1; why not in
        /// <paramref name="why"/> (0 strung, 1 too long, 2 a road too close
        /// under it, 3 the ground too close, 4 a deck or a building).</summary>
        static int SpanFrom(int chain, int k, out int why)
        {
            long key = Key(chain, k);
            if (spanCache.TryGetValue(key, out int packed)) { why = packed >> 24; return (packed & 0xFFFFFF) == 0xFFFFFF ? -1 : packed & 0xFFFFFF; }
            why = 1;
            int next = -1;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var a = Decide(chain, k);
            if (a.standing)
                for (int k2 = k + 1; k2 <= k + 2; k2++)
                {
                    var b = Decide(chain, k2);
                    if (!b.standing) continue;
                    why = SpanOk(a.pole, b.pole);
                    if (why == 0) next = k2;
                    break;
                }
            spanCache[key] = (why << 24) | (next < 0 ? 0xFFFFFF : next);
            SpanMs += Ms(t0); Spans++;
            return next;
        }

        /// <summary>0 when a span may be strung between two poles, else why not.</summary>
        static int SpanOk(Pole a, Pole b)
        {
            var pa = new Vector2(a.foot.x, a.foot.z); var pb = new Vector2(b.foot.x, b.foot.z);
            float L = Vector2.Distance(pa, pb);
            if (L > MaxSpanM || L < MinSpanM) return 1;
            float lowA = LowestWire(a), lowB = LowestWire(b);
            float sag = SagShare * L;
            int n = Mathf.Max(2, Mathf.CeilToInt(L / 2f));
            for (int i = 1; i < n; i++)
            {
                float t = i / (float)n;
                var q = Vector2.Lerp(pa, pb, t);
                float wire = Mathf.Lerp(lowA, lowB, t) - 4f * sag * t * (1f - t);
                byte bits = RoadsideOccupancy.StaticAt(mapNow, trimsNow, buildingsNow, q);
                if ((bits & (RoadsideOccupancy.Deck | RoadsideOccupancy.Building)) != 0) return 4;
                if ((bits & RoadsideOccupancy.Pavement) != 0)
                {
                    float road = CitySigns.RoadTopAt(mapNow, trimsNow, q, 0.3f);
                    if (!float.IsNegativeInfinity(road) && wire < road + WireClearM) return 2;
                }
                if ((i & 3) == 2 && wire < CityMeshes.LatticeAt(mapNow, q.x, q.y) + WireGroundM) return 3;
            }
            return 0;
        }

        static float LowestWire(Pole p) => p.top - (p.levels >= 4 ? Telecom2Down : p.levels >= 2 ? TelecomDown : NeutralDown);

        // ==================================================================
        //  A TILE
        // ==================================================================

        static readonly HashSet<int> segScratch = new HashSet<int>();
        static readonly HashSet<int> edgeSeen = new HashSet<int>();
        static readonly List<int> edgeList = new List<int>(256);

        /// <summary>
        /// A tile's poles and wires, and its furniture mesh (its street lamps
        /// too). Marks every pole foot that reaches the tile on its mask
        /// <paramref name="occ"/>, before the signs and the trees take theirs.
        /// Deterministic from the map; the same poles whichever tile asks.
        /// </summary>
        public static PoleTile Build(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings,
                                     CityMeshes.TileMeshes tm, RoadsideOccupancy occ, int tx, int tz)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var pt = new PoleTile { tx = tx, tz = tz };
            if (map == null) return pt;
            float ts = CityMeshes.TileSize;
            var min = new Vector2(tx * ts, tz * ts);
            var max = min + Vector2.one * ts;
            if (Enabled)
            {
                Setup(map, trims, buildings);
                var L = lines;
                segScratch.Clear(); edgeSeen.Clear(); edgeList.Clear();
                float reach = StationReachM;
                map.EdgeSegsInRect(min - Vector2.one * reach, max + Vector2.one * reach, segScratch);
                foreach (int packed in segScratch) { int ei = packed >> 12; if (L.chainOf[ei] >= 0 && edgeSeen.Add(ei)) edgeList.Add(ei); }
                edgeList.Sort();
                foreach (int ei in edgeList)
                {
                    int chain = L.chainOf[ei];
                    var e = map.edges[ei];
                    float c0 = L.startOf[ei], c1 = c0 + e.length;
                    int k0 = Mathf.Max(0, Mathf.FloorToInt(c0 / PitchM) - 1), k1 = Mathf.CeilToInt(c1 / PitchM) + 1;
                    for (int k = k0; k <= k1; k++)
                    {
                        float arc = StationArc(chain, k);
                        if (arc < c0 || arc >= c1) continue;   // another edge's station
                        // only the stations whose pole could stand in or by this
                        // tile (a foot is at most StationReachM off its station):
                        // a long edge's far stations are other tiles' business
                        float sl = L.dirOf[ei] > 0 ? arc - c0 : c1 - arc;
                        var nominal = e.PointAt(sl);
                        if (nominal.x < min.x - StationReachM || nominal.y < min.y - StationReachM ||
                            nominal.x > max.x + StationReachM || nominal.y > max.y + StationReachM) continue;
                        var d = Decide(chain, k);
                        if (!d.standing)
                        {
                            // the station's nominal point decides which tile counts it
                            if (InTile(nominal, min, max))
                            {
                                pt.stations++; pt.refused++;
                                for (int b = 0; b < 12; b++) if (d.why > 0 && (d.why & (1 << b)) != 0) pt.refusedWhy[b]++;
                            }
                            continue;
                        }
                        var f = new Vector2(d.pole.foot.x, d.pole.foot.z);
                        if (f.x > min.x - FootR - 2f && f.y > min.y - FootR - 2f && f.x < max.x + FootR + 2f && f.y < max.y + FootR + 2f && occ != null)
                            occ.MarkDisc(f, FootR + RoadsideOccupancy.CellPadM, RoadsideOccupancy.Other);
                        if (!InTile(f, min, max)) continue;
                        pt.stations++;
                        pt.poles.Add(d.pole);
                        pt.heads.Add(d.pole.lens);
                        int next = SpanFrom(chain, k, out int why);
                        if (next >= 0) pt.spans.Add((d.pole, Decide(chain, next).pole));
                        else switch (why)
                            {
                                case 1: pt.spanLong++; break;
                                case 2: pt.spanRoad++; break;
                                case 3: pt.spanGround++; break;
                                case 4: pt.spanBlocked++; break;
                            }
                    }
                }
            }
            // THE MESH: the poles, their wires and heads, and the tile's lamps
            Begin();
            foreach (var p in pt.poles) EmitPole(p);
            foreach (var (a, b) in pt.spans) pt.wires += EmitSpan(a, b);
            if (tm != null)
                foreach (var l in tm.lamps) { EmitLamp(tm.origin + l.foot, tm.origin + l.head, l.kind); pt.lampsDrawn++; }
            pt.mesh = End(tm != null ? tm.origin : new Vector3(min.x, 0f, min.y));
            pt.ms = (float)clock.Elapsed.TotalMilliseconds;
            return pt;
        }

        static bool InTile(Vector2 p, Vector2 min, Vector2 max) => p.x >= min.x && p.y >= min.y && p.x < max.x && p.y < max.y;

        /// <summary>
        /// Decide, a slice at a time, every station and span a tile's
        /// <see cref="Build"/> and its business signs' <see cref="SignClear"/>
        /// will ask for (and the static masks they are decided on), so its
        /// tree frame finds them all decided: CityWorld
        /// spends a frame of its own on each slice before it plants, as it does
        /// for the signs (CitySigns.Prepare). 0: nothing was left to decide;
        /// 1: this slice decided the last of them; 2: more to come. A slice
        /// always decides one at least. Every decision is the same whenever
        /// and wherever it is made.
        /// </summary>
        public static int Prepare(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings,
                                  int tx, int tz, float budgetMs)
        {
            if (!Enabled || map == null) return 0;
            long deadline = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(budgetMs * System.Diagnostics.Stopwatch.Frequency / 1000.0);
            Setup(map, trims, buildings);
            float ts = CityMeshes.TileSize;
            var min = new Vector2(tx * ts, tz * ts);
            var max = min + Vector2.one * ts;
            // every station and span the tile's Build asks for, and every one
            // its business signs' keep-out asks for (SignClear: the stations
            // round a cabinet, which stands inside the tile)
            float reach = KeepReachM + SignPartMaxR;
            prepSt.Clear();
            StationsIn(map, min - Vector2.one * reach, max + Vector2.one * reach, prepSt);
            int did = 0;
            bool Due(bool fresh) => fresh && did > 0 && System.Diagnostics.Stopwatch.GetTimestamp() > deadline;
            foreach (var (chain, k) in prepSt)
            {
                bool fresh = !cache.ContainsKey(Key(chain, k));
                if (Due(fresh)) return 2;
                var d = Decide(chain, k);
                if (fresh) did++;
                if (!d.standing) continue;
                // the span from it, and those from up to two stations back that may end at it
                for (int kb = k; kb >= k - 2 && kb >= 0; kb--)
                {
                    bool spanFresh = !spanCache.ContainsKey(Key(chain, kb));
                    if (Due(spanFresh)) return 2;
                    SpanFrom(chain, kb, out _);
                    if (spanFresh) did++;
                }
            }
            return did > 0 ? 1 : 0;
        }
        static readonly List<(int chain, int k)> prepSt = new List<(int, int)>(512);

        /// <summary>Every station (line, k) whose nominal point on its road
        /// lies in the rectangle lo..hi, in a fixed order.</summary>
        static void StationsIn(CityMap map, Vector2 lo, Vector2 hi, List<(int chain, int k)> into)
        {
            var L = lines;
            stSegs.Clear(); stSeen.Clear(); stEdges.Clear();
            map.EdgeSegsInRect(lo, hi, stSegs);
            foreach (int packed in stSegs) { int ei = packed >> 12; if (L.chainOf[ei] >= 0 && stSeen.Add(ei)) stEdges.Add(ei); }
            stEdges.Sort();
            foreach (int ei in stEdges)
            {
                int chain = L.chainOf[ei];
                var e = map.edges[ei];
                float c0 = L.startOf[ei], c1 = c0 + e.length;
                int k0 = Mathf.Max(0, Mathf.FloorToInt(c0 / PitchM) - 1), k1 = Mathf.CeilToInt(c1 / PitchM) + 1;
                for (int k = k0; k <= k1; k++)
                {
                    float arc = StationArc(chain, k);
                    if (arc < c0 || arc >= c1) continue;
                    var nominal = e.PointAt(L.dirOf[ei] > 0 ? arc - c0 : c1 - arc);
                    if (nominal.x < lo.x || nominal.y < lo.y || nominal.x > hi.x || nominal.y > hi.y) continue;
                    into.Add((chain, k));
                }
            }
        }
        static readonly HashSet<int> stSegs = new HashSet<int>(), stSeen = new HashSet<int>();
        static readonly List<int> stEdges = new List<int>(256);

        // ==================================================================
        //  WHAT A SIGN KEEPS CLEAR OF (the WP-15 review)
        // ==================================================================

        /// <summary>
        /// Is a sign's part - a capsule a..b of radius <paramref name="r"/> in
        /// plan: a post, a business cabinet, a billboard's face with its
        /// catwalk - clear of the utility furniture, with <see cref="SignAirM"/>
        /// of air: every strung span's wires (<see cref="WireReachM"/> either
        /// side of the line between its poles' feet), every pole with its
        /// crossarm, and its cobra-head's arm and head? Plan only (the telecom
        /// cables hang 6-9 m up, where the cabinets and faces are). Decided
        /// from the global data the poles are, so a billboard (decided from
        /// global data too) is the same in every tile that asks; true when
        /// the poles are off.
        /// </summary>
        public static bool SignClear(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings,
                                     Vector2 a, Vector2 b, float r)
        {
            if (!Enabled || map == null) return true;
            Setup(map, trims, buildings);
            float reach = KeepReachM + r;
            keepSt.Clear();
            StationsIn(map, Vector2.Min(a, b) - Vector2.one * reach, Vector2.Max(a, b) + Vector2.one * reach, keepSt);
            foreach (var (chain, k) in keepSt)
            {
                var d = Decide(chain, k);
                if (!d.standing) continue;
                var p = d.pole;
                if (PartsClash(p, a, b, r)) return false;
                // its span, and the spans from up to two stations back that end at it
                int nx = SpanFrom(chain, k, out _);
                if (nx >= 0 && SpanClash(p, Decide(chain, nx).pole, a, b, r)) return false;
                for (int kb = k - 2; kb < k; kb++)
                    if (kb >= 0 && SpanFrom(chain, kb, out _) == k && SpanClash(Decide(chain, kb).pole, p, a, b, r)) return false;
            }
            return true;
        }
        static readonly List<(int chain, int k)> keepSt = new List<(int, int)>(128);

        static Vector2 Plan(Vector3 v) => new Vector2(v.x, v.z);

        /// <summary>A pole, its crossarm (across the line) and its cobra-head's
        /// arm and head (out over the road) within reach of a sign's part.</summary>
        static bool PartsClash(Pole p, Vector2 a, Vector2 b, float r)
        {
            var f = Plan(p.foot);
            float keep = r + SignAirM;
            if (RoadsideOccupancy.SegSegDistance(a, b, f - p.outward * (CrossArmL * 0.5f), f + p.outward * (CrossArmL * 0.5f)) < keep + PoleW * 0.5f) return true;
            var headEnd = Plan(p.lens) - p.outward * (HeadL * 0.5f);
            return RoadsideOccupancy.SegSegDistance(a, b, f, headEnd) < keep + HeadW * 0.5f;
        }

        static bool SpanClash(Pole pa, Pole pb, Vector2 a, Vector2 b, float r) =>
            RoadsideOccupancy.SegSegDistance(a, b, Plan(pa.foot), Plan(pb.foot)) < r + SignAirM + WireReachM;

        /// <summary>For the audits: every standing pole whose station's
        /// nominal point is within reach of the rectangle lo..hi, and every
        /// span they string (each once).</summary>
        public static void FurnitureNear(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings,
                                         Vector2 lo, Vector2 hi, List<Pole> poles, List<(Pole a, Pole b)> spans)
        {
            poles.Clear(); spans.Clear();
            if (!Enabled || map == null) return;
            Setup(map, trims, buildings);
            var near = new List<(int chain, int k)>();
            StationsIn(map, lo - Vector2.one * KeepReachM, hi + Vector2.one * KeepReachM, near);
            var seen = new HashSet<long>();
            foreach (var (chain, k) in near)
            {
                var d = Decide(chain, k);
                if (!d.standing) continue;
                poles.Add(d.pole);
                for (int kb = k; kb >= k - 2 && kb >= 0; kb--)
                {
                    int nx = SpanFrom(chain, kb, out _);
                    if (nx < 0 || (kb != k && nx != k) || !seen.Add(Key(chain, kb))) continue;
                    spans.Add((Decide(chain, kb).pole, Decide(chain, nx).pole));
                }
            }
        }

        /// <summary>A pole's solid parts as drawn, for the audits: capsules
        /// (from, to, radius) - the pole, its crossarm, its cobra-head's arm
        /// and head.</summary>
        public static void PartsOf(Pole p, List<(Vector3 a, Vector3 b, float r)> into)
        {
            var o = V3(p.outward);
            into.Add((p.foot, new Vector3(p.foot.x, p.top, p.foot.z), PoleW * 0.5f));
            float cy = p.top - CrossArmDown;
            var c = new Vector3(p.foot.x, cy, p.foot.z);
            into.Add((c - o * (CrossArmL * 0.5f), c + o * (CrossArmL * 0.5f), CrossArmH * 0.5f));
            float armY = p.lens.y + HeadH * 0.5f;
            into.Add((new Vector3(p.foot.x, armY, p.foot.z), new Vector3(p.lens.x, armY, p.lens.z) - o * (HeadL * 0.5f), HeadW * 0.5f));
        }

        /// <summary>The pole's collider: centre and size, world.</summary>
        public static (Vector3 centre, Vector3 size) ColliderOf(Pole p) =>
            (new Vector3(p.foot.x, (p.foot.y + p.top) * 0.5f, p.foot.z), new Vector3(ColliderW, p.top - p.foot.y, ColliderW));

        /// <summary>The kit's furniture material (null: the furniture does not draw).</summary>
        public static Material Material()
        {
            var kit = CityKit.Get();
            return kit != null ? kit.furniture : null;
        }

        // ==================================================================
        //  THE MESH (world space here; End moves it to the tile's origin)
        // ==================================================================

        static readonly List<Vector3> vs = new List<Vector3>(4096), ns = new List<Vector3>(4096);
        static readonly List<Vector2> uvs = new List<Vector2>(4096), wire = new List<Vector2>(4096);
        static readonly List<Color32> cols = new List<Color32>(4096);
        static readonly List<int> tris = new List<int>(8192);

        static void Begin() { vs.Clear(); ns.Clear(); uvs.Clear(); wire.Clear(); cols.Clear(); tris.Clear(); }

        static Mesh End(Vector3 origin)
        {
            if (vs.Count == 0) return null;
            for (int i = 0; i < vs.Count; i++) vs[i] -= origin;
            var m = new Mesh { name = "furniture" };
            if (vs.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(vs);
            m.SetNormals(ns);
            m.SetUVs(0, uvs);
            m.SetUVs(1, wire);
            m.SetColors(cols);
            m.SetTriangles(tris, 0, false);
            m.RecalculateBounds();
            // a wire's ribbon is stood up in the shader: a pixel wide far off
            var b = m.bounds; b.Expand(4f); m.bounds = b;
            return m;
        }

        /// <summary>One face of a box: its centre, outward normal, and the two
        /// half axes across it (<paramref name="u"/> reads to the viewer's
        /// right, <paramref name="v"/> up); UVs in metres, wrapped in the cell.</summary>
        static void Face(Vector3 c, Vector3 n, Vector3 u, Vector3 v, Color32 cell)
        {
            int i = vs.Count;
            vs.Add(c - u - v); vs.Add(c - u + v); vs.Add(c + u + v); vs.Add(c + u - v);
            for (int k = 0; k < 4; k++) { ns.Add(n); cols.Add(cell); wire.Add(Vector2.zero); }
            float lu = 2f * u.magnitude, lv = 2f * v.magnitude;
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0f, lv)); uvs.Add(new Vector2(lu, lv)); uvs.Add(new Vector2(lu, 0f));
            // clockwise from the side n points to (Unity's front face): a drawn
            // triangle (p0, p1, p2) faces Cross(p1 - p0, p2 - p0)
            if (Vector3.Dot(Vector3.Cross(vs[i + 1] - vs[i], vs[i + 2] - vs[i]), n) > 0f)
            { tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3); }
            else
            { tris.Add(i); tris.Add(i + 2); tris.Add(i + 1); tris.Add(i); tris.Add(i + 3); tris.Add(i + 2); }
        }

        /// <summary>A box from its centre and three half-axis vectors (ax
        /// horizontal, ay up, az horizontal), less the bottom when
        /// <paramref name="noBottom"/>.</summary>
        static void Box(Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, Color32 cell, bool noBottom)
        {
            Face(c + ax, ax.normalized, az, ay, cell);
            Face(c - ax, -ax.normalized, az, ay, cell);
            Face(c + az, az.normalized, ax, ay, cell);
            Face(c - az, -az.normalized, ax, ay, cell);
            Face(c + ay, Vector3.up, ax, az, cell);
            if (!noBottom) Face(c - ay, Vector3.down, ax, az, cell);
        }

        static Vector3 V3(Vector2 p) => new Vector3(p.x, 0f, p.y);

        static void EmitPole(Pole p)
        {
            var o = V3(p.outward); var al = V3(p.along);
            float hp = PoleW * 0.5f;
            float h = p.top - p.foot.y;
            // the pole
            Box(p.foot + Vector3.up * (h * 0.5f), al * hp, Vector3.up * (h * 0.5f), o * hp, CellWood, true);
            // the crossarm, across the line, and its pair of pins
            float cy = p.top - CrossArmDown;
            Box(new Vector3(p.foot.x, cy, p.foot.z), o * (CrossArmL * 0.5f), Vector3.up * (CrossArmH * 0.5f), al * (CrossArmW * 0.5f), CellWood, false);
            for (int s = -1; s <= 1; s += 2)
            {
                var pin = new Vector3(p.foot.x, cy + CrossArmH * 0.5f + 0.09f, p.foot.z) + o * (PrimaryOff * s);
                Box(pin, al * 0.04f, Vector3.up * 0.09f, o * 0.04f, CellMetal, true);
            }
            // the cobra-head: an arm from the pole out over the road's edge, the head at its end
            var lens = p.lens;
            float armY = lens.y + HeadH * 0.5f;
            var from = new Vector3(p.foot.x, armY, p.foot.z);
            var to = lens + Vector3.up * (HeadH * 0.5f) + o * (HeadL * 0.5f);
            to.y = armY;
            float half = Vector3.Distance(from, to) * 0.5f;
            if (half > 0.01f)
                Box((from + to) * 0.5f, -o * half, Vector3.up * (ArmW * 0.5f), al * (ArmW * 0.5f), CellMetal, false);
            Box(new Vector3(lens.x, lens.y + HeadH * 0.5f, lens.z), -o * (HeadL * 0.5f), Vector3.up * (HeadH * 0.5f), al * (HeadW * 0.5f), CellMetal, false);
        }

        /// <summary>A span's wires between two poles; how many.</summary>
        static int EmitSpan(Pole a, Pole b)
        {
            wireScratch.Clear();
            WiresOf(a, b, wireScratch);
            foreach (var w in wireScratch) Wire(w.from, w.to, w.sag, w.halfW);
            return wireScratch.Count;
        }
        static readonly List<(Vector3 from, Vector3 to, float sag, float halfW)> wireScratch = new List<(Vector3, Vector3, float, float)>(8);

        /// <summary>A span's wires as they are drawn (the audits read them
        /// too): each one's ends, its sag at the middle and its half width.</summary>
        public static void WiresOf(Pole a, Pole b, List<(Vector3 from, Vector3 to, float sag, float halfW)> into)
        {
            int levels = Mathf.Min(a.levels, b.levels);
            float sag = SagShare * Vector2.Distance(new Vector2(a.foot.x, a.foot.z), new Vector2(b.foot.x, b.foot.z));
            // the crossarm's pair (each pole's own outward: the pins stay on their sides)
            for (int s = -1; s <= 1; s += 2)
            {
                var pa = new Vector3(a.foot.x, a.top - CrossArmDown + CrossArmH * 0.5f + 0.16f, a.foot.z) + V3(a.outward) * (PrimaryOff * s);
                var pb = new Vector3(b.foot.x, b.top - CrossArmDown + CrossArmH * 0.5f + 0.16f, b.foot.z) + V3(b.outward) * (PrimaryOff * s);
                into.Add((pa, pb, sag, PrimaryHalfW));
            }
            // the lower levels hang on the road side of the pole
            float off = PoleW * 0.5f + 0.06f;
            if (levels >= 3) into.Add((At(a, NeutralDown, off), At(b, NeutralDown, off), sag, NeutralHalfW));
            into.Add((At(a, TelecomDown, off), At(b, TelecomDown, off), sag, TelecomHalfW));
            if (levels >= 4) into.Add((At(a, Telecom2Down, off), At(b, Telecom2Down, off), sag, TelecomHalfW));
        }

        /// <summary>A point a fraction t along a wire: on its chord, less the
        /// parabola of its sag (the drawn wire's vertices are these).</summary>
        public static Vector3 WirePoint(Vector3 from, Vector3 to, float sag, float t) =>
            Vector3.Lerp(from, to, t) + Vector3.down * (4f * sag * t * (1f - t));

        /// <summary>The segments a wire is drawn with.</summary>
        public const int WireSegmentsDrawn = WireSegments;

        static Vector3 At(Pole p, float down, float toRoad) =>
            new Vector3(p.foot.x, p.top - down, p.foot.z) - V3(p.outward) * toRoad;

        /// <summary>One wire from a to b sagging <paramref name="sag"/> at its
        /// middle: vertices ON the line, two a point (side -1, +1), the
        /// normal carrying the wire's direction (PSX/Lit's PSX_FURNITURE
        /// stands the ribbon up).</summary>
        static void Wire(Vector3 a, Vector3 b, float sag, float halfW)
        {
            int start = vs.Count;
            var dir = b - a;
            if (dir.sqrMagnitude < 1e-4f) return;
            dir.Normalize();
            for (int i = 0; i <= WireSegments; i++)
            {
                float t = i / (float)WireSegments;
                var q = WirePoint(a, b, sag, t);
                // the tangent of the sagging line here
                var tan = (b - a) + Vector3.down * (4f * sag * (1f - 2f * t));
                tan.Normalize();
                for (int s = -1; s <= 1; s += 2)
                {
                    vs.Add(q); ns.Add(tan); cols.Add(CellWire); uvs.Add(new Vector2(t, s > 0 ? 1f : 0f));
                    wire.Add(new Vector2(s, halfW));
                }
            }
            for (int i = 0; i < WireSegments; i++)
            {
                // A -1, B +1 here; D -1, C +1 at the next point. The shader
                // stands side +1 along cross(direction, to the eye), so (A, B, C)
                // faces Cross(B - A, C - A), toward the eye, from wherever it is
                int A = start + i * 2, B = A + 1, C = A + 3, D = A + 2;
                tris.Add(A); tris.Add(B); tris.Add(C);
                tris.Add(A); tris.Add(C); tris.Add(D);
            }
        }

        // ---- the street lamps, drawn on the atlas (CityMeshes.EmitLamp's shapes)
        const float LampPostW = 0.26f, LampArmW = 0.12f, LampHeadL = 0.75f, LampHeadH = 0.18f, LampHeadW = 0.40f, LampPostCapM = 0.2f;
        const float AcornPostW = 0.14f, AcornBaseW = 0.34f, AcornBaseH = 0.6f, AcornGlobeW = 0.36f, AcornGlobeH = 0.46f;

        /// <summary>One of the tile's street lamps, world foot and lens.</summary>
        static void EmitLamp(Vector3 foot, Vector3 head, byte kind)
        {
            if (kind == CityMeshes.LampAcorn)
            {
                // a black post, a fluted base, a globe (the lens) and its cap
                float topPost = head.y - AcornGlobeH * 0.5f;
                Box(new Vector3(foot.x, (foot.y + topPost) * 0.5f, foot.z), Vector3.right * (AcornPostW * 0.5f), Vector3.up * ((topPost - foot.y) * 0.5f), Vector3.forward * (AcornPostW * 0.5f), CellBlack, true);
                Box(new Vector3(foot.x, foot.y + AcornBaseH * 0.5f, foot.z), Vector3.right * (AcornBaseW * 0.5f), Vector3.up * (AcornBaseH * 0.5f), Vector3.forward * (AcornBaseW * 0.5f), CellBlack, true);
                Box(head, Vector3.right * (AcornGlobeW * 0.5f), Vector3.up * (AcornGlobeH * 0.5f), Vector3.forward * (AcornGlobeW * 0.5f), CellMetal, false);
                Box(head + Vector3.up * (AcornGlobeH * 0.5f + 0.06f), Vector3.right * 0.12f, Vector3.up * 0.06f, Vector3.forward * 0.12f, CellBlack, false);
                return;
            }
            var toRoad = new Vector3(head.x - foot.x, 0f, head.z - foot.z);
            if (toRoad.sqrMagnitude < 1e-6f) toRoad = Vector3.forward;
            toRoad.Normalize();
            var across = new Vector3(-toRoad.z, 0f, toRoad.x);
            float armY = head.y + LampHeadH * 0.5f;
            float top = armY + LampArmW * 0.5f + LampPostCapM;
            float hp = LampPostW * 0.5f;
            Box(new Vector3(foot.x, (foot.y + top) * 0.5f, foot.z), toRoad * hp, Vector3.up * ((top - foot.y) * 0.5f), across * hp, CellMetal, true);
            var box = new Vector3(head.x, armY, head.z);
            var from = new Vector3(foot.x, armY, foot.z);
            var to = box - toRoad * (LampHeadL * 0.5f);
            float half = Vector3.Distance(from, to) * 0.5f;
            if (half > 0.01f)
                Box((from + to) * 0.5f, toRoad * half, Vector3.up * (LampArmW * 0.5f), across * (LampArmW * 0.5f), CellMetal, false);
            Box(box, toRoad * (LampHeadL * 0.5f), Vector3.up * (LampHeadH * 0.5f), across * (LampHeadW * 0.5f), CellMetal, false);
        }

        static float Hash01(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + salt * 2246822519) + 1442695041u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        // ==================================================================
        //  For the audits and the shots
        // ==================================================================

        /// <summary>Every standing pole of the line a pole is on, from station
        /// k0 to k1 (the audit's pitch, and a shot's line of poles).</summary>
        public static List<Pole> LineOf(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings, int chain, int k0, int k1)
        {
            Setup(map, trims, buildings);
            var list = new List<Pole>();
            for (int k = Mathf.Max(0, k0); k <= k1; k++) { var d = Decide(chain, k); if (d.standing) list.Add(d.pole); }
            return list;
        }

        /// <summary>Work out the pole lines and the uptown loop now (CityWorld,
        /// as the map loads) rather than on the first tree frame.</summary>
        public static void Warm(CityMap map)
        {
            if (map == null || !Enabled) return;
            Chains(map);
        }

        /// <summary>The line an edge is on (-1 none), for the audit.</summary>
        public static int ChainOf(CityMap map, int edge) => Chains(map).chainOf[edge];

        /// <summary>The next pole a pole's span strings to, if it strings one.</summary>
        public static bool NextOf(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings, Pole p, out Pole next)
        {
            Setup(map, trims, buildings);
            next = default;
            int k2 = SpanFrom(p.chain, p.k, out _);
            if (k2 < 0) return false;
            next = Decide(p.chain, k2).pole;
            return true;
        }

        /// <summary>The lowest wire's height a fraction t along a span (the audit's clearance).</summary>
        public static float LowestWireAt(Pole a, Pole b, float t)
        {
            float L = Vector2.Distance(new Vector2(a.foot.x, a.foot.z), new Vector2(b.foot.x, b.foot.z));
            return Mathf.Lerp(LowestWire(a), LowestWire(b), t) - 4f * SagShare * L * t * (1f - t);
        }
    }
}
