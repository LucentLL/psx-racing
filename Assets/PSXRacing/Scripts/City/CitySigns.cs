using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// CHARLOTTE'S SIGNS (plan WP-23, release R9): billboards, business pole
    /// signs and exit gantries. The owner, after driving it: "its all so
    /// barren and flat ... hills, ditches, trees, billboards".
    ///
    /// REAL DENSITY (the owner, Q2), from what <see cref="CitySignData"/> holds
    /// (tools/city/signs.mjs):
    ///   * BILLBOARDS: the OSM ones where they are, and along every interstate
    ///     and US/NC route as many more as NCDOT's permits say that route
    ///     carries per km (a statistic, never a position: plan Q8), placed at
    ///     random on the stretches where North Carolina lets one stand -
    ///     commercial or industrial land within 660 ft of the right of way
    ///     (19A NCAC 02E .0203) - no two on the same side of a freeway within
    ///     500 ft (152.4 m), or of another route within 100 ft, none within
    ///     <see cref="InterchangeM"/> of a ramp's gore (not to hide an official
    ///     sign). On a freeway a 14 x 48 ft bulletin on a monopole, a V (its
    ///     point toward the road) when there is traffic the other way across
    ///     the median; on the other routes a 12 x 24 ft poster, back to back.
    ///     Each face looks at the drivers <see cref="ViewAheadM"/> up the road.
    ///     Lit: two floodlights under a bulletin's face, one under a poster's,
    ///     each a lamp of the game's own (NightGlow's halo and StreetLights'
    ///     pool), and the face glows after dark through the atlas's night mask.
    ///   * BUSINESS POLE SIGNS: one at the frontage of every OSM business a
    ///     collector or bigger passes within 70 m of, and along the commercial
    ///     frontage every <see cref="FrontStepM"/> (7 in 10 of them) where OSM
    ///     maps no shop - no two within <see cref="PoleSignSpacingM"/>. A
    ///     2.4-3.2 m cabinet 6-10 m up, square to the road so both directions
    ///     read it, lit from inside at night. Breakaway (Q15): no collider.
    ///   * EXIT GANTRIES: a sign bridge over the motorway
    ///     <see cref="GantryBackM"/> before every exit (a motorway_link
    ///     diverge), a panel over each destination group of its lanes (OSM
    ///     destination:lanes), the exit's panel over the exit lanes; the panels
    ///     are the plan's code-drawn green (Q7), the truss pack metal. Its legs
    ///     stand clear of every carriageway's clear zone, across both
    ///     carriageways when the median has no room; the panels clear the
    ///     highest road under them by <see cref="GantryClearM"/>. Unlit, as
    ///     NCDOT's retroreflective panels are today: the headlights light them.
    ///
    /// Every face is FICTIONAL, from the owner's packs (tools/city/signs_atlas.py).
    ///
    /// WHERE: every post, leg and face stands on free ground of the tile's
    /// <see cref="RoadsideOccupancy"/> mask (no pavement, clear zone, sight
    /// triangle, corner spot, building, water, deck or race run-off), and marks
    /// what it takes before the trees are planted (the plan's priority: signs
    /// before trees). A billboard also clears the trees out of its drivers' line
    /// of sight, as its owner would. Nothing overhangs a road but a gantry.
    ///
    /// DETERMINISTIC from the graph alone: every choice is a hash of an edge's
    /// station or a business's index; a sign belongs to the tile its nominal
    /// post stands in, and the spacing rules are settled over everything
    /// within <see cref="Gather"/> of the tile, so the same signs stand on
    /// every build and none twice across a seam.
    ///
    /// DRAWN in one mesh a tile with the one atlas material (the city kit's
    /// <see cref="CityKit.signs"/>): one draw, no sun-map caster. SOLID: the
    /// billboards' monopoles and the gantries' legs (box colliders on one
    /// Solid-layer object a tile named <see cref="PostName"/>).
    /// </summary>
    public static class CitySigns
    {
        /// <summary>Tools switch the signs off for an A/B (the budget probe).</summary>
        public static bool Enabled = true;
        /// <summary>The Solid-layer object holding a tile's sign posts.</summary>
        public const string PostName = "CityPost";

        /// <summary>Face sizes. NCDOT's Mecklenburg permits: median 12 x 36 ft,
        /// p90 14 x 48 (the interstate bulletin), p10 about 10 x 25; total
        /// height median 44 ft (13.4 m).</summary>
        public const float BulletinW = 14.63f, BulletinH = 4.27f;   // 48 x 14 ft, on a freeway
        public const float MedianW = 10.97f, MedianH = 3.66f;       // 36 x 12 ft, elsewhere
        public const float PosterW = 7.32f, PosterH = 3.66f;        // 24 x 12 ft, a third of those elsewhere
        /// <summary>.0203(2)(b)(i): 500 ft between structures on the same side of a freeway.</summary>
        public const float FreewaySpacingM = 152.4f;
        /// <summary>.0203(2)(c)(ii): 100 ft inside a town (the belt is nearly all Charlotte and its towns).</summary>
        public const float TownSpacingM = 30.5f;
        /// <summary>Billboard stations along a route, and how far off the
        /// centreline the zoning is read (signs.mjs reads the same).</summary>
        public const float StationM = 25f, ZoneOffM = 25f;
        /// <summary>No billboard this close to a node where a ramp meets a motorway.</summary>
        public const float InterchangeM = 80f;
        public const float PoleSignSpacingM = 30f, PoiReachM = 70f, FrontStepM = 40f, FrontShare = 0.7f;
        /// <summary>No tree trunk this close to a business's sign.</summary>
        public const float PoleTreeM = 5f;
        /// <summary>A gantry's panels clear the highest road under them by
        /// this (the plan's 5.5 m, and NCDOT's 17.5 ft for sign structures).</summary>
        public const float GantryClearM = 5.8f;
        /// <summary>A gantry stands this far before the exit's diverge node.</summary>
        public const float GantryBackM = 30f;
        /// <summary>A face looks at the drivers this far up the road, at eye
        /// height (a business's cabinet, read closer, <see cref="PoleViewM"/>).</summary>
        public const float ViewAheadM = 150f, PoleViewM = 80f, EyeM = 1.2f;
        /// <summary>A face must look at its driver at least this squarely
        /// (the sign audit's 0.9, and a margin).</summary>
        const float FaceDotPlace = 0.92f;
        /// <summary>How far round a tile its candidates are gathered (the
        /// freeway spacing plus a post's setback).</summary>
        public const float Gather = 230f;
        /// <summary>Legs are looked for at most this far off the carriageway's
        /// centreline; a span longer than <see cref="MaxSpanM"/> is not built
        /// (the exit then gets a cantilever from its right-hand leg).</summary>
        const float LegReachM = 65f, MaxSpanM = 85f;
        public const byte BlockBits = RoadsideOccupancy.Pavement | RoadsideOccupancy.Clear | RoadsideOccupancy.Deck |
                                      RoadsideOccupancy.Building | RoadsideOccupancy.Water;

        // ---- the atlas (tools/city/signs_atlas.py: pixel rectangles of 512 from the top left)
        const float AtlasPx = 512f;
        static RectInt BulletinCell(int i) => new RectInt(256 * (i % 2), 76 * (i / 2), 256, 76);
        static RectInt PosterCell(int i) => new RectInt(128 * i, 228, 128, 64);
        static RectInt PoleCell(int i) => new RectInt(64 * (i % 8), 292 + 64 * (i / 8), 64, 64);
        static readonly RectInt GantryThrough = new RectInt(0, 420, 128, 64), GantryExit = new RectInt(128, 420, 128, 64);
        static readonly RectInt MetalDark = new RectInt(256, 420, 128, 92), MetalLight = new RectInt(384, 420, 128, 92);
        static readonly RectInt LensCell = new RectInt(0, 484, 16, 16), BlackSide = new RectInt(16, 484, 16, 16);
        public const int Bulletins = 6, Posters = 4;
        /// <summary>The cabinets a business kind puts up (atlas pole cells:
        /// 0 6twelve, 1 GAS, 2 STACK BURGER, 3 BURGERS, 4 SLICE HOUSE, 5 PIZZA,
        /// 6 EAT GOOD FOOD, 7 MOTEL, 8 BANK, 9 DRUGS, 10 CAR WASH, 11 AUTO
        /// SALES, 12 TIRES, 13 PLAZA, 14 FOOD MART, 15 LOUNGE).</summary>
        static readonly byte[][] KindCells =
        {
            new byte[] { 0, 1 }, new byte[] { 2, 3 }, new byte[] { 4, 5 }, new byte[] { 6 }, new byte[] { 7 }, new byte[] { 8 },
            new byte[] { 9 }, new byte[] { 10 }, new byte[] { 11 }, new byte[] { 12 }, new byte[] { 13 }, new byte[] { 14 }, new byte[] { 15 },
        };
        /// <summary>The strip malls' pylons where OSM maps no shop.</summary>
        static readonly byte[] FrontCells = { 13, 13, 13, 14, 12, 11, 9, 8, 13, 6 };

        public enum Kind : byte { Bulletin, Poster, PoleSign, Gantry }

        /// <summary>A face that shows to traffic: its centre, outward normal,
        /// size, and the driver it was turned to (on a carriageway, at eye
        /// height, <see cref="ViewAheadM"/> up the road from it).</summary>
        public struct Face { public Vector3 centre, normal, viewer; public float w, h; public Kind kind; }

        public struct Sign
        {
            public Kind kind;
            /// <summary>The post (a gantry: the middle of its span over the carriageway).</summary>
            public Vector2 pos;
            public float ground, bottom, top;
            /// <summary>The route it counts toward (billboards), or -1.</summary>
            public int route;
            public bool osm, lit;
            public int edge;
            /// <summary>A gantry's legs, and how far its panels clear the highest road under them.</summary>
            public Vector2 legA, legB;
            public float clearance;
            public int firstFace, faces;
            /// <summary>The point that decided which tile it belongs to (a
            /// post's nominal place; a gantry's first choice of station).</summary>
            public Vector2 owner;
        }

        public class SignTile
        {
            public int tx, tz;
            public readonly List<Sign> signs = new List<Sign>();
            public readonly List<Face> faces = new List<Face>();
            /// <summary>Solid posts, world: centre, size, yaw (degrees).</summary>
            public readonly List<(Vector3 centre, Vector3 size, float yawDeg)> posts = new List<(Vector3, Vector3, float)>();
            /// <summary>Floodlight heads, world (lamps at night).</summary>
            public readonly List<Vector3> lamps = new List<Vector3>();
            public Mesh mesh;
            public int bulletins, posters, poleSigns, gantries, osm;
            /// <summary>Candidates that won their spacing but found no free ground.</summary>
            public int refusedBoards, refusedPoleSigns, refusedGantries;
            public float ms;
        }

        // ------------------------------------------------------------------

        struct Cand
        {
            public Vector2 station, f, nominal, osmPos;
            public int edge, route, cell;
            public float s, rank, osmYaw;
            public bool freeway, osm, oneway, lit;
        }

        static readonly HashSet<int> segScratch = new HashSet<int>();
        static readonly List<int> edgeList = new List<int>(256);
        static readonly List<Cand> cands = new List<Cand>(64);
        static readonly List<int> poiScratch = new List<int>(64);

        /// <summary>Place one tile's signs on its mask (which they then mark),
        /// and build their mesh.</summary>
        public static SignTile Build(CityMap map, CityMeshes.Trims trims, RoadsideOccupancy occ, int tx, int tz)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var st = new SignTile { tx = tx, tz = tz };
            if (!CitySignData.Loaded || occ == null) return st;
            float ts = CityMeshes.TileSize;
            var min = new Vector2(tx * ts, tz * ts);
            var max = min + Vector2.one * ts;
            segScratch.Clear(); edgeList.Clear();
            map.EdgeSegsInRect(min - Vector2.one * Gather, max + Vector2.one * Gather, segScratch);
            var seen = new HashSet<int>();
            foreach (int packed in segScratch) if (seen.Add(packed >> 12)) edgeList.Add(packed >> 12);
            edgeList.Sort();
            mapNow = map; trimsNow = trims;
            Begin();
            // official signs first, then advertising, then the businesses
            Gantries(map, trims, occ, st, min, max);
            Billboards(map, trims, occ, st, min, max);
            PoleSigns(map, trims, occ, st, min, max);
            st.mesh = End(min);
            st.ms = (float)clock.Elapsed.TotalMilliseconds;
            return st;
        }

        static bool InTile(Vector2 p, Vector2 min, Vector2 max) => p.x >= min.x && p.y >= min.y && p.x < max.x && p.y < max.y;
        static Vector2 RightOf(Vector2 t) => new Vector2(t.y, -t.x);   // x east, z north: facing +z the right hand is +x
        static Vector3 V3(Vector2 p, float y) => new Vector3(p.x, y, p.y);
        static Vector2 P2(Vector3 p) => new Vector2(p.x, p.z);

        /// <summary>
        /// A driver <paramref name="dist"/> metres up (negative) or down the
        /// road from arc s of an edge, at eye height, on the lane that comes
        /// toward the sign: the walk carries on across junctions onto the
        /// straightest carriageway going on (a one-way road's the same way; a
        /// street's never onto a ramp), so a driver round a bend or past a
        /// junction is still on a road. <paramref name="lane"/> is how far to
        /// the right of the traffic that comes toward the sign it drives;
        /// <see cref="AutoLane"/> puts it in the middle of a one-way
        /// carriageway and in the lane toward the sign on a two-way road,
        /// whichever the walk ends on. A one-way carriageway is only ever
        /// walked against its traffic (a road that splits into a pair is
        /// followed on the side that comes toward the sign). False when the road does not run that
        /// far toward the sign: a dead end, or a junction where nothing goes
        /// on within 60 degrees of straight (a sign there has no drivers
        /// coming at it from far enough to turn it to).
        /// </summary>
        static Vector3 Driver(CityMap map, CityMap.Edge e, float s, float dist, float lane) => Driver(map, e, s, dist, lane, out _);

        public const float AutoLane = float.NaN;

        static Vector3 Driver(CityMap map, CityMap.Edge e, float s, float dist, float lane, out bool ok)
        {
            ok = true;
            float remaining = dist;
            Vector2 p = e.PointAt(s), walk = e.TangentAt(s) * Mathf.Sign(dist);
            float y = e.YAt(s);
            bool link = e.link;
            for (int hop = 0; hop < 12; hop++)
            {
                float target = s + remaining;
                if (target >= 0f && target <= e.length)
                {
                    p = e.PointAt(target); y = e.YAt(target);
                    walk = e.TangentAt(target) * (remaining >= 0f ? 1f : -1f);
                    remaining = 0f;
                    break;
                }
                bool back = target < 0f;
                int node = back ? e.a : e.b;
                float left = back ? -target : target - e.length;
                var here = back ? -e.TangentAt(0f) : e.TangentAt(e.length);
                p = map.nodes[node]; y = e.YAt(back ? 0f : e.length); walk = here;
                // the straightest road going on
                CityMap.Edge next = null; float bestDot = 0.5f;
                foreach (int oi in map.nodeEdges[node])
                {
                    var o = map.edges[oi];
                    if (o == e || o.tunnel || o.a == o.b || (o.link && !link)) continue;
                    bool fromA = o.a == node;
                    // the drivers come toward the sign: a one-way carriageway only against its travel
                    if (o.oneway && fromA) continue;
                    var dir = fromA ? o.TangentAt(0f) : -o.TangentAt(o.length);
                    float d = Vector2.Dot(dir, here);
                    if (d > bestDot) { bestDot = d; next = o; }
                }
                // a dead end, or a T: the driver is at the junction
                if (next == null) { remaining = 0f; ok = false; break; }
                e = next;
                if (e.a == node) { s = 0f; remaining = left; }
                else { s = e.length; remaining = -left; }
            }
            // the traffic comes toward the sign: against the walk
            var toward = -walk;
            if (float.IsNaN(lane)) lane = e.oneway ? 0f : e.width * 0.25f;
            return new Vector3(p.x, y + EyeM, p.y) + V3(RightOf(toward) * lane, 0f);
        }

        /// <summary>Is a plan point clear of every carriageway's pavement and
        /// clear zone and of every deck (and, inside the tile, of what the
        /// mask reserves besides)? <paramref name="bits"/> is what the mask
        /// may not have there.</summary>
        static bool Free(RoadsideOccupancy occ, Vector2 p, Vector2 min, Vector2 max, byte bits)
        {
            if (InTile(p, min, max) && (occ.At(p) & bits) != 0) return false;
            return RoadsClear(mapNow, trimsNow, p);
        }
        static CityMap mapNow;
        static CityMeshes.Trims trimsNow;

        /// <summary>A plan point off every grounded carriageway's pavement and
        /// clear zone (<see cref="RoadsideOccupancy.ClearZoneOf"/>), and at
        /// least <see cref="RoadsideOccupancy.DeckMarginM"/> off every deck;
        /// the edges read through <see cref="RoadsideOccupancy.RoadEdgeAt"/>
        /// (the one accessor the lines release replaces). Measured off the
        /// graph, so a gantry's leg 60 m out is judged as surely as a post in
        /// the tile; the sign audit measures with it too.</summary>
        public static bool RoadsClear(CityMap map, CityMeshes.Trims trims, Vector2 p) => WorstRoad(map, trims, p, out _, out _) >= 0f;

        /// <summary>How far a point is outside the nearest road's keep-out
        /// (its clear zone, or a deck's margin): negative inside it, with that
        /// road and what the keep-out is.</summary>
        public static float WorstRoad(CityMap map, CityMeshes.Trims trims, Vector2 p, out int edge, out string what)
        {
            float worst = float.MaxValue;
            edge = -1; what = "";
            clearScratch.Clear();
            map.EdgeSegsInRect(p - Vector2.one * 34f, p + Vector2.one * 34f, clearScratch);
            foreach (int packed in clearScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (e.tunnel) continue;
                Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float tq = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                float at = e.s[si] + Mathf.Sqrt(L2) * tq;
                RoadsideOccupancy.RoadEdgeAt(e, trims, at, out _, out _, out float hwL, out float hwR);
                float off = Vector2.Distance(p, a + d * tq) - Mathf.Max(hwL, hwR);
                bool deck = e.bridge || e.ElevatedAt(at);
                float keep = deck ? RoadsideOccupancy.DeckMarginM : RoadsideOccupancy.ClearZoneOf(e);
                if (off - keep < worst) { worst = off - keep; edge = ei; what = off < 0f ? "on pavement" : deck ? "under a deck" : "in a clear zone"; }
            }
            return worst;
        }
        static readonly HashSet<int> clearScratch = new HashSet<int>();

        /// <summary>The highest road whose pavement a plan point is over (within
        /// <paramref name="slack"/>), or -infinity.</summary>
        public static float RoadTopAt(CityMap map, CityMeshes.Trims trims, Vector2 p, float slack)
        {
            float top = float.NegativeInfinity;
            clearScratch.Clear();
            map.EdgeSegsInRect(p - Vector2.one * 25f, p + Vector2.one * 25f, clearScratch);
            foreach (int packed in clearScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (e.tunnel) continue;
                Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float tq = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                float at = e.s[si] + Mathf.Sqrt(L2) * tq;
                RoadsideOccupancy.RoadEdgeAt(e, trims, at, out _, out _, out float hwL, out float hwR);
                if (Vector2.Distance(p, a + d * tq) - Mathf.Max(hwL, hwR) <= slack) top = Mathf.Max(top, e.YAt(at));
            }
            return top;
        }

        /// <summary>
        /// Is a face's driver on a road coming toward it? 0: on no pavement;
        /// 1: only on one-way carriageways running away from the face; 2: on a
        /// two-way road, or a carriageway running toward it. The sign audit
        /// asks the same of every face.
        /// </summary>
        public static int DriverOn(CityMap map, CityMeshes.Trims trims, Vector2 vw, Vector2 face, out string what)
        {
            what = "";
            clearScratch.Clear();
            map.EdgeSegsInRect(vw - Vector2.one * 25f, vw + Vector2.one * 25f, clearScratch);
            int best = 0;
            foreach (int packed in clearScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (e.tunnel) continue;
                Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float tq = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(vw - a, d) / L2) : 0f;
                float at = e.s[si] + Mathf.Sqrt(L2) * tq;
                RoadsideOccupancy.RoadEdgeAt(e, trims, at, out _, out _, out float hwL, out float hwR);
                if (Vector2.Distance(vw, a + d * tq) - Mathf.Max(hwL, hwR) > 0.5f) continue;
                if (!e.oneway || Vector2.Dot(e.TangentAt(at), face - vw) >= 0f) return 2;
                best = 1; what = $"e{ei} '{e.name}'";
            }
            return best;
        }

        static bool DriverOk(Vector3 viewer, Vector3 face) => DriverOn(mapNow, trimsNow, new Vector2(viewer.x, viewer.z), new Vector2(face.x, face.z), out _) == 2;

        /// <summary>Every sample of a plan segment free (a face's or a cabinet's footprint).</summary>
        static bool SegFree(RoadsideOccupancy occ, Vector2 a, Vector2 b, Vector2 min, Vector2 max, byte bits)
        {
            float L = Vector2.Distance(a, b);
            int n = Mathf.Max(1, Mathf.CeilToInt(L / 1.5f));
            for (int i = 0; i <= n; i++) if (!Free(occ, Vector2.Lerp(a, b, i / (float)n), min, max, bits)) return false;
            return true;
        }

        /// <summary>A node where a ramp meets a motorway within r of a point?</summary>
        public static bool InterchangeNear(CityMap map, Vector2 p, float r)
        {
            ixScratch.Clear();
            map.EdgeSegsInRect(p - Vector2.one * r, p + Vector2.one * r, ixScratch);
            foreach (int packed in ixScratch)
            {
                var e = map.edges[packed >> 12];
                foreach (int n in new[] { e.a, e.b })
                {
                    if ((map.nodes[n] - p).sqrMagnitude >= r * r) continue;
                    bool link = false, mw = false;
                    foreach (int oi in map.nodeEdges[n])
                    {
                        var o = map.edges[oi];
                        if (o.link) link = true; else if (o.cls >= 5) mw = true;
                    }
                    if (link && mw) return true;
                }
            }
            return false;
        }
        static readonly HashSet<int> ixScratch = new HashSet<int>();

        // ------------------------------------------------------------------
        //  Exit gantries
        // ------------------------------------------------------------------

        static void Gantries(CityMap map, CityMeshes.Trims trims, RoadsideOccupancy occ, SignTile st, Vector2 min, Vector2 max)
        {
            foreach (int ei in edgeList)
            {
                var e = map.edges[ei];
                if (e.link || e.cls < 5 || !e.oneway || e.tunnel) continue;
                int n = e.b;
                bool link = false, main = false;
                foreach (int oi in map.nodeEdges[n])
                {
                    var o = map.edges[oi];
                    if (o.a != n || !o.oneway || o == e) continue;
                    if (o.link) link = true; else if (o.cls >= 5) main = true;
                }
                if (!link || !main) continue;
                float s0 = e.length > GantryBackM + 10f ? e.length - GantryBackM : e.length * 0.5f;
                if (!InTile(e.PointAt(s0), min, max)) continue;
                bool placed = false;
                // on a deck there is nothing to stand a leg on: step back up the road
                for (float s = s0; s > 5f && !placed && s0 - s < 130f; s -= 40f)
                {
                    if (e.bridge || e.ElevatedAt(s)) continue;
                    placed = Gantry(map, trims, occ, st, e, s, e.PointAt(s0), min, max);
                }
                if (!placed) st.refusedGantries++;
            }
        }

        static bool FindLeg(RoadsideOccupancy occ, Vector2 c, Vector2 dir, float from, Vector2 min, Vector2 max, out Vector2 leg)
        {
            for (float d = from + 1f; d <= LegReachM; d += 0.5f)
            {
                leg = c + dir * d;
                // a leg, and the 1.4 m of its foot along the road
                if (Free(occ, leg, min, max, (byte)(BlockBits | RoadsideOccupancy.Sight | RoadsideOccupancy.Corner | RoadsideOccupancy.Other)))
                    return true;
            }
            leg = c;
            return false;
        }

        static bool Gantry(CityMap map, CityMeshes.Trims trims, RoadsideOccupancy occ, SignTile st, CityMap.Edge e, float s, Vector2 owner, Vector2 min, Vector2 max)
        {
            RoadsideOccupancy.RoadEdgeAt(e, trims, s, out var c, out _, out float hwL, out float hwR);
            var t = e.TangentAt(s);
            var right = RightOf(t);
            if (!FindLeg(occ, c, right, hwR, min, max, out var legR)) return false;
            // a sign bridge over the whole road; failing a place for its left
            // leg (no room in the median outside the clear zones, and the far
            // carriageway too wide to reach over), a cantilever from the right
            // leg out to the carriageway's left edge
            bool cantilever = !FindLeg(occ, c, -right, hwL, min, max, out var legL) || Vector2.Distance(legL, legR) > MaxSpanM;
            var armEnd = cantilever ? c - right * (hwL - 0.3f) : legL;

            // the highest road under the span
            float roadTop = e.YAt(s);
            float span = Vector2.Distance(armEnd, legR);
            for (float d = 0f; d <= span; d += 2f)
                roadTop = Mathf.Max(roadTop, RoadTopAt(map, trims, Vector2.Lerp(armEnd, legR, d / span), 0.5f));
            float bottom = roadTop + GantryClearM;

            // panels: one per destination group, over its lanes (left to right)
            var groups = CitySignData.GantryGroups(e.wayId);
            int total = 0;
            if (groups != null) foreach (var g in groups) total += g;
            if (groups == null || total < 1)
            {
                int nl = Mathf.Max(2, e.lanes);
                groups = new[] { (byte)(nl - 1), (byte)1 };
                total = nl;
            }
            float laneW = Mathf.Clamp((hwL + hwR - e.shl - e.shr) / total, 2.8f, 4.2f);
            var leftEdge = c - right * hwL;
            float tallest = 0f;
            int first = st.faces.Count;
            int lane = 0;
            var n3 = new Vector3(-t.x, 0f, -t.y);   // the panels face the traffic coming up to them
            float truss = 0f;
            for (int gi = 0; gi < groups.Length; gi++)
            {
                int g = groups[gi];
                bool exit = groups.Length >= 2 && gi == groups.Length - 1;
                float h = exit ? 3.0f : 3.4f;
                tallest = Mathf.Max(tallest, h);
                float mid = e.shl + (lane + g * 0.5f) * laneW;
                lane += g;
                float w = Mathf.Clamp(Mathf.Min(g * laneW - 0.5f, 2.2f + 2.6f * g), 2.4f, 12f);
                var pc = leftEdge + right * mid;
                st.faces.Add(new Face
                {
                    centre = V3(pc, bottom + h * 0.5f) + n3 * 0.72f, normal = n3, w = w, h = h, kind = Kind.Gantry,
                    viewer = Driver(map, e, s, -ViewAheadM, mid - hwL),
                });
            }
            // truss: a box beam behind the panels' upper half, leg to leg
            truss = bottom + tallest - 0.7f;
            var mid3 = V3((armEnd + legR) * 0.5f, truss);
            var along = V3((legR - armEnd) / Mathf.Max(0.01f, span), 0f);
            Box(mid3, along, Vector3.up, new Vector3(t.x, 0f, t.y), span * 0.5f + 0.35f, 0.55f, 0.55f, MetalLight, MetalLight);
            // the panels, hung on its front
            for (int i = first; i < st.faces.Count; i++)
            {
                var f = st.faces[i];
                bool exit = i == st.faces.Count - 1 && st.faces.Count - first >= 2;
                BoxFacing(f.centre - n3 * 0.08f, n3, f.w * 0.5f, f.h * 0.5f, 0.08f, exit ? GantryExit : GantryThrough, MetalLight);
            }
            // legs: from the ground to the top of the truss
            foreach (var leg in cantilever ? new[] { legR } : new[] { legL, legR })
            {
                float gy = CityMeshes.LatticeAt(map, leg.x, leg.y) - 0.3f;
                float top = truss + 0.55f;
                var lc = V3(leg, (gy + top) * 0.5f);
                Box(lc, along, Vector3.up, new Vector3(t.x, 0f, t.y), 0.35f, (top - gy) * 0.5f, 0.7f, MetalLight, MetalLight);
                st.posts.Add((lc, new Vector3(0.7f, top - gy, 1.4f), Mathf.Atan2(t.x, t.y) * Mathf.Rad2Deg));   // 1.4 m along the road, as drawn
                occ.MarkDisc(leg, 2f + RoadsideOccupancy.CellPadM, RoadsideOccupancy.Other);
            }
            st.signs.Add(new Sign
            {
                kind = Kind.Gantry, pos = c, ground = CityMeshes.LatticeAt(map, c.x, c.y), bottom = bottom, top = truss + 0.55f,
                route = -1, edge = e.index, legA = cantilever ? legR : legL, legB = legR, clearance = bottom - roadTop, firstFace = first, faces = st.faces.Count - first, owner = owner,
            });
            st.gantries++;
            return true;
        }

        // ------------------------------------------------------------------
        //  Billboards
        // ------------------------------------------------------------------

        static void Billboards(CityMap map, CityMeshes.Trims trims, RoadsideOccupancy occ, SignTile st, Vector2 min, Vector2 max)
        {
            cands.Clear();
            var lo = min - Vector2.one * Gather;
            var hi = max + Vector2.one * Gather;
            // the OSM boards, turned to the nearest route or major road
            var boards = CitySignData.Boards;
            for (int i = 0; i < boards.Length; i++)
            {
                var b = boards[i];
                if (b.pos.x < lo.x || b.pos.y < lo.y || b.pos.x > hi.x || b.pos.y > hi.y) continue;
                if (!NearestMajor(map, b.pos, 250f, out int ei, out float s)) continue;
                var e = map.edges[ei];
                var q = e.PointAt(s);
                var f = b.pos - q;
                if (f.sqrMagnitude < 1e-4f) f = RightOf(e.TangentAt(s));
                f.Normalize();
                cands.Add(new Cand
                {
                    station = q, f = f, nominal = b.pos, osmPos = b.pos, osmYaw = b.yawDeg, edge = ei, s = s,
                    route = CitySignData.RouteOf(e.wayId), freeway = e.cls >= 5, osm = true, oneway = e.oneway,
                    rank = -1f + i * 1e-5f, lit = true,
                });
            }
            // the routes' own, on their zoned stretches
            foreach (int ei in edgeList)
            {
                var e = map.edges[ei];
                if (e.link || e.tunnel) continue;
                int ri = CitySignData.RouteOf(e.wayId);
                if (ri < 0) continue;
                var route = CitySignData.Routes[ri];
                if (route.rate <= 0f) continue;
                bool fw = route.cls == 1;
                float p = route.rate * StationM / 1000f;
                for (int k = 0; (k + 0.5f) * StationM < e.length; k++)
                {
                    if (CityTrees.Hash01(ei, k, 37) >= p) continue;
                    float s = (k + 0.5f) * StationM;
                    var q = e.PointAt(s);
                    if (q.x < lo.x || q.y < lo.y || q.x > hi.x || q.y > hi.y) continue;
                    if (fw && InterchangeNear(map, q, InterchangeM)) continue;
                    var right = RightOf(e.TangentAt(s));
                    float off = e.width * 0.5f + ZoneOffM;
                    int side = e.oneway ? 1 : (CityTrees.Hash01(ei, k, 31) < 0.5f ? 1 : -1);
                    if ((CitySignData.At(q + right * (side * off)) & CitySignData.Zoned) == 0)
                    {
                        if (e.oneway) continue;
                        side = -side;
                        if ((CitySignData.At(q + right * (side * off)) & CitySignData.Zoned) == 0) continue;
                    }
                    var f = right * side;
                    float setback = fw ? 10f : 5f;
                    cands.Add(new Cand
                    {
                        station = q, f = f, nominal = q + f * (e.width * 0.5f + RoadsideOccupancy.ClearZoneOf(e) + setback),
                        edge = ei, s = s, route = ri, freeway = fw, oneway = e.oneway, rank = CityTrees.Hash01(ei, k, 41), lit = true,
                        cell = (int)(CityTrees.Hash01(ei, k, 43) * 1000f),
                    });
                }
            }
            // spacing: a board loses to a better one on the same side within the rule's distance
            for (int i = 0; i < cands.Count; i++)
            {
                var c = cands[i];
                if (!InTile(c.nominal, min, max)) continue;
                bool lost = false;
                for (int j = 0; j < cands.Count && !lost; j++)
                {
                    if (j == i) continue;
                    var o = cands[j];
                    if (o.rank >= c.rank || Vector2.Dot(o.f, c.f) < 0.5f) continue;
                    float sp = c.freeway || o.freeway ? FreewaySpacingM : TownSpacingM;
                    if ((o.nominal - c.nominal).sqrMagnitude < sp * sp) lost = true;
                }
                if (lost) continue;
                if (!Billboard(map, trims, occ, st, c, min, max)) st.refusedBoards++;
            }
        }

        /// <summary>The nearest collector-or-bigger carriageway (not a ramp)
        /// within r: the road an OSM board was put up to face.</summary>
        static bool NearestMajor(CityMap map, Vector2 p, float r, out int best, out float bestS)
        {
            best = -1; bestS = 0f;
            float bd = r;
            ixScratch.Clear();
            map.EdgeSegsInRect(p - Vector2.one * r, p + Vector2.one * r, ixScratch);
            foreach (int packed in ixScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (e.link || e.tunnel || e.cls < 1) continue;
                Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float tq = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                float dist = Vector2.Distance(p, a + d * tq);
                if (dist < bd || (Mathf.Approximately(dist, bd) && ei < best)) { bd = dist; best = ei; bestS = e.s[si] + Mathf.Sqrt(L2) * tq; }
            }
            return best >= 0;
        }

        /// <summary>Where the drivers coming the other way across the median
        /// are: the nearest carriageway running against this one, left of it,
        /// near the point <see cref="ViewAheadM"/> down the road.</summary>
        static bool OppositeViewer(CityMap map, CityMap.Edge e, float s, out Vector3 viewer)
        {
            viewer = default;
            var t = e.TangentAt(s);
            var here = e.PointAt(s);
            var d = here + t * ViewAheadM;
            var right = RightOf(t);
            float bd = 70f;
            bool found = false;
            ixScratch.Clear();
            map.EdgeSegsInRect(d - Vector2.one * 70f, d + Vector2.one * 70f, ixScratch);
            foreach (int packed in ixScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var o = map.edges[ei];
                if (o.link || o.tunnel || !o.oneway || o.cls < 4 || o.index == e.index) continue;
                Vector2 a = o.pts[si], dd = o.pts[si + 1] - a;
                float L2 = dd.sqrMagnitude;
                if (L2 < 1e-6f || Vector2.Dot(dd, t) > -0.7f * Mathf.Sqrt(L2)) continue;
                float tq = Mathf.Clamp01(Vector2.Dot(d - a, dd) / L2);
                var q = a + dd * tq;
                if (Vector2.Dot(q - d, right) > 0f) continue;   // the far carriageway is on the left
                if (Vector2.Dot(q - here, t) < 60f) continue;    // and well down the road, coming back
                float dist = Vector2.Distance(d, q);
                if (dist < bd) { bd = dist; found = true; viewer = V3(q, o.YAt(o.s[si] + Mathf.Sqrt(L2) * tq) + EyeM); }
            }
            return found;
        }

        static bool Billboard(CityMap map, CityMeshes.Trims trims, RoadsideOccupancy occ, SignTile st, Cand c, Vector2 min, Vector2 max)
        {
            var e = map.edges[c.edge];
            // a freeway's 14 x 48 ft bulletin; elsewhere the median 12 x 36 ft,
            // or a third of the time a 12 x 24 ft poster
            int sizeRoll = (int)(CityTrees.Hash01(c.edge, (int)c.s, 49) * 3f);
            bool bulletin = c.freeway || sizeRoll > 0;
            float W = c.freeway ? BulletinW : bulletin ? MedianW : PosterW, H = c.freeway ? BulletinH : bulletin ? MedianH : PosterH;
            var t = e.TangentAt(c.s);
            var right = RightOf(t);
            float hwRoad = e.width * 0.5f + RoadsideOccupancy.ClearZoneOf(e);
            float[] tries = c.osm ? new[] { 0f, 3f, 6f, 9f, 12f, 15f } : c.freeway ? new[] { 10f, 14f, 19f, 25f, 32f, 40f } : new[] { 6f, 9f, 12f, 16f, 21f };
            // the drivers each face is turned to
            var viewA = Driver(map, e, c.s, -ViewAheadM, AutoLane, out bool okA);
            Vector3 viewB;
            bool two;
            if (e.oneway) two = OppositeViewer(map, e, c.s, out viewB);
            else viewB = Driver(map, e, c.s, ViewAheadM, AutoLane, out two);
            okA &= DriverOk(viewA, V3(c.station, 0f));
            if (two) two = DriverOk(viewB, V3(c.station, 0f));
            if (!okA)
            {
                // no drivers coming up this way: the board faces the other way only
                if (!two) return false;
                viewA = viewB; two = false;
            }
            foreach (float extra in tries)
            {
                var P = c.osm ? c.osmPos + c.f * extra : c.station + c.f * (hwRoad + extra);
                if (!InTile(P, min, max)) continue;
                if (occ.At(P) != 0) continue;
                // the faces in plan: a V with its point toward the road; back to
                // back beside a two-way road where one plane faces both ways well
                // enough; or one flat face
                var nA = Flat(V3(P, 0f), viewA);
                var nB = two ? Flat(V3(P, 0f), viewB) : Vector3.zero;
                Vector2 aA0, aA1, aB0 = default, aB1 = default;
                var bis = nA - nB;
                bool backToBack = two && !e.oneway && bis.sqrMagnitude > 1e-4f &&
                                  Vector3.Dot(bis.normalized, nA) >= FaceDotPlace && Vector3.Dot(-bis.normalized, nB) >= FaceDotPlace;
                if (backToBack)
                {
                    nA = bis.normalized; nB = -nA;
                    var u = new Vector2(-nA.z, nA.x);
                    aA0 = P - u * (W * 0.5f) + new Vector2(nA.x, nA.z) * 0.2f; aA1 = aA0 + u * W;
                    aB0 = P + u * (W * 0.5f) + new Vector2(nB.x, nB.z) * 0.2f; aB1 = aB0 - u * W;
                }
                else
                {
                    FaceLine(P, c.f, nA, W, two, out aA0, out aA1);
                    if (two) FaceLine(P, c.f, nB, W, true, out aB0, out aB1);
                }
                if (!SegFree(occ, aA0, aA1, min, max, BlockBits)) continue;
                if (two && !SegFree(occ, aB0, aB1, min, max, BlockBits)) continue;

                // heights (NCDOT's total height median is 13.4 m): a freeway
                // bulletin's face 9.5 m or more over the ground and 8 m over the
                // road; elsewhere 6.5 m and 5 m
                float g = CityMeshes.LatticeAt(map, P.x, P.y);
                float roadY = e.YAt(c.s);
                int hk = (int)(CityTrees.Hash01(c.edge, (int)c.s, 47) * 1000f);
                float bottom = c.freeway ? Mathf.Max(g + 9.5f, roadY + 8f) + (hk % 250) / 100f : Mathf.Max(g + 6.5f, roadY + 5f) + (hk % 250) / 100f;
                int first = st.faces.Count;
                int cellA = bulletin ? (c.cell + (c.osm ? (int)(c.osmPos.x * 7f) & 0xFF : 0)) % Bulletins : (c.cell + (c.osm ? (int)(c.osmPos.x * 7f) & 0xFF : 0)) % Posters;
                int cellB = bulletin ? (cellA + 1 + (hk % (Bulletins - 1))) % Bulletins : (cellA + 1 + (hk % (Posters - 1))) % Posters;
                AddBoardFace(st, aA0, aA1, nA, bottom, H, bulletin, cellA, viewA);
                if (two) AddBoardFace(st, aB0, aB1, nB, bottom, H, bulletin, cellB, viewB);

                // the monopole, and a beam from its head to each face
                float pw = bulletin ? 0.9f : 0.6f;
                float gy = g - 0.3f;
                var pc = V3(P, (gy + bottom) * 0.5f);
                var fwd = new Vector3(t.x, 0f, t.y);
                Box(pc, new Vector3(right.x, 0f, right.y), Vector3.up, fwd, pw * 0.5f, (bottom - gy) * 0.5f, pw * 0.5f, MetalDark, MetalDark);
                st.posts.Add((pc, new Vector3(pw, bottom - gy, pw), Mathf.Atan2(t.x, t.y) * Mathf.Rad2Deg));
                for (int i = first; i < st.faces.Count; i++)
                {
                    var fc = st.faces[i].centre;
                    var to = new Vector3(fc.x - P.x, 0f, fc.z - P.y);
                    float L = to.magnitude;
                    if (L < 0.2f) continue;
                    Box(V3(P, bottom - 0.35f) + to * 0.5f, to / L, Vector3.up, Vector3.Cross(to / L, Vector3.up), L * 0.5f, 0.25f, 0.2f, MetalDark, MetalDark);
                }

                // occupancy: the post, the faces' footprints, and the drivers'
                // line of sight kept clear of trees
                occ.MarkDisc(P, 1.5f + RoadsideOccupancy.CellPadM, RoadsideOccupancy.Other);
                occ.MarkCapsule(aA0, aA1, 0.8f + RoadsideOccupancy.CellPadM, RoadsideOccupancy.Other);
                MarkSight(occ, (aA0 + aA1) * 0.5f, viewA, W);
                if (two)
                {
                    occ.MarkCapsule(aB0, aB1, 0.8f + RoadsideOccupancy.CellPadM, RoadsideOccupancy.Other);
                    MarkSight(occ, (aB0 + aB1) * 0.5f, viewB, W);
                }
                st.signs.Add(new Sign
                {
                    kind = bulletin ? Kind.Bulletin : Kind.Poster, pos = P, ground = g, bottom = bottom, top = bottom + H,
                    route = c.route, osm = c.osm, lit = c.lit, edge = c.edge, firstFace = first, faces = st.faces.Count - first, owner = c.nominal,
                });
                if (bulletin) st.bulletins++; else st.posters++;
                if (c.osm) st.osm++;
                return true;
            }
            return false;
        }

        /// <summary>The horizontal unit normal from a face at p to a viewer.</summary>
        static Vector3 Flat(Vector3 p, Vector3 viewer)
        {
            var d = new Vector3(viewer.x - p.x, 0f, viewer.z - p.z);
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
        }

        /// <summary>A face's plan line for a board at P whose drivers' side is
        /// -f: in a V (<paramref name="vee"/>) it runs from the point, which is
        /// nearest the road, away from the road; flat, it is centred on P.</summary>
        static int FaceLine(Vector2 P, Vector2 f, Vector3 n, float W, bool vee, out Vector2 a, out Vector2 b)
        {
            var n2 = new Vector2(n.x, n.z);
            var u = new Vector2(-n2.y, n2.x);
            if (Vector2.Dot(u, f) < 0f) u = -u;       // away from the road
            if (vee)
            {
                var apex = P - f * (W * 0.5f * Mathf.Abs(Vector2.Dot(u, f))) + n2 * 0.25f;
                a = apex; b = apex + u * W;
            }
            else { a = P - u * (W * 0.5f); b = P + u * (W * 0.5f); }
            return 0;
        }

        static void AddBoardFace(SignTile st, Vector2 a, Vector2 b, Vector3 n, float bottom, float H, bool bulletin, int cell, Vector3 viewer)
        {
            var mid = (a + b) * 0.5f;
            float W = Vector2.Distance(a, b);
            var centre = V3(mid, bottom + H * 0.5f);
            // the face panel, 0.35 m deep: the picture in front, steel behind
            BoxFacing(centre - n * 0.175f, n, W * 0.5f, H * 0.5f, 0.175f, bulletin ? BulletinCell(cell) : PosterCell(cell), MetalDark);
            // the catwalk under it, and its floodlights on arms out front
            var ax = Vector3.Cross(n, Vector3.up).normalized;
            Box(V3(mid, bottom - 0.25f) + n * 0.3f, ax, Vector3.up, n, W * 0.5f, 0.08f, 0.55f, MetalDark, MetalDark);
            int lamps = bulletin ? 2 : 1;
            for (int k = 0; k < lamps; k++)
            {
                float along = lamps == 1 ? 0f : (k == 0 ? -0.25f : 0.25f) * W;
                var head = V3(mid, bottom - 0.35f) + ax * along + n * 1.4f;
                Box(V3(mid, bottom - 0.3f) + ax * along + n * 0.8f, n, Vector3.up, -ax, 0.6f, 0.05f, 0.05f, MetalDark, MetalDark);
                BoxFacing(head, (Vector3.up * 0.8f - n * 0.6f).normalized, 0.22f, 0.14f, 0.12f, LensCell, MetalDark);
                st.lamps.Add(head);
            }
            st.faces.Add(new Face { centre = centre, normal = n, w = W, h = H, kind = bulletin ? Kind.Bulletin : Kind.Poster, viewer = viewer });
        }

        /// <summary>A billboard's owner keeps the trees out of its drivers'
        /// line of sight: the first 60 m of it, as wide as a third of the face.</summary>
        static void MarkSight(RoadsideOccupancy occ, Vector2 from, Vector3 viewer, float W)
        {
            var d = new Vector2(viewer.x, viewer.z) - from;
            float L = d.magnitude;
            if (L < 1f) return;
            occ.MarkCapsule(from, from + d / L * Mathf.Min(60f, L), W * 0.35f, RoadsideOccupancy.Other);
        }

        // ------------------------------------------------------------------
        //  Business pole signs
        // ------------------------------------------------------------------

        struct PoleCand { public Vector2 station, f, nominal; public int edge, cellIndex; public float s, rank, w; public bool poi; }
        static readonly List<PoleCand> poles = new List<PoleCand>(128);

        static void PoleSigns(CityMap map, CityMeshes.Trims trims, RoadsideOccupancy occ, SignTile st, Vector2 min, Vector2 max)
        {
            poles.Clear();
            // every business near enough to the tile that its sign could be in it
            poiScratch.Clear();
            CitySignData.PoisIn(min - Vector2.one * 110f, max + Vector2.one * 110f, poiScratch);
            foreach (int pi in poiScratch)
            {
                var poi = CitySignData.Pois[pi];
                if (!NearestArterial(map, poi.pos, PoiReachM, out int ei, out float s)) continue;
                var e = map.edges[ei];
                var q = e.PointAt(s);
                var right = RightOf(e.TangentAt(s));
                var f = Vector2.Dot(poi.pos - q, right) >= 0f ? right : -right;
                var cells = KindCells[Mathf.Clamp(poi.kind, 0, KindCells.Length - 1)];
                float w = 2.4f + 0.8f * CityTrees.Hash01(pi, 3, 71);
                poles.Add(new PoleCand
                {
                    station = q, f = f, edge = ei, s = s, w = w, poi = true,
                    nominal = q + f * (e.width * 0.5f + RoadsideOccupancy.ClearZoneOf(e) + w * 0.5f + 0.8f),
                    cellIndex = cells[Mathf.Min(cells.Length - 1, (int)(CityTrees.Hash01(pi, 5, 73) * cells.Length))],
                    rank = CityTrees.Hash01(pi, 7, 91),
                });
            }
            // the commercial frontage where OSM maps no shop
            var lo = min - Vector2.one * 70f;
            var hi = max + Vector2.one * 70f;
            foreach (int ei in edgeList)
            {
                var e = map.edges[ei];
                if (e.link || e.tunnel || e.cls < 1 || e.cls > 4) continue;
                for (int k = 0; (k + 0.5f) * FrontStepM < e.length; k++)
                {
                    float s = (k + 0.5f) * FrontStepM;
                    var q = e.PointAt(s);
                    if (q.x < lo.x || q.y < lo.y || q.x > hi.x || q.y > hi.y) continue;
                    if (e.bridge || e.ElevatedAt(s)) continue;
                    var right = RightOf(e.TangentAt(s));
                    for (int side = 0; side < 2; side++)
                    {
                        if (CityTrees.Hash01(ei, 2 * k + side, 53) >= FrontShare) continue;
                        var f = side == 0 ? right : -right;
                        float w = 2.4f + 0.8f * CityTrees.Hash01(ei, 2 * k + side, 57);
                        var nom = q + f * (e.width * 0.5f + RoadsideOccupancy.ClearZoneOf(e) + w * 0.5f + 0.8f);
                        if ((CitySignData.At(nom) & CitySignData.Frontage) == 0) continue;
                        poles.Add(new PoleCand
                        {
                            station = q, f = f, edge = ei, s = s, w = w, nominal = nom,
                            cellIndex = FrontCells[(int)(CityTrees.Hash01(ei, 2 * k + side, 61) * FrontCells.Length) % FrontCells.Length],
                            rank = 1f + CityTrees.Hash01(ei, 2 * k + side, 59),
                        });
                    }
                }
            }
            for (int i = 0; i < poles.Count; i++)
            {
                var c = poles[i];
                if (!InTile(c.nominal, min, max)) continue;
                bool lost = false;
                for (int j = 0; j < poles.Count && !lost; j++)
                {
                    if (j == i) continue;
                    var o = poles[j];
                    if (o.rank < c.rank && (o.nominal - c.nominal).sqrMagnitude < PoleSignSpacingM * PoleSignSpacingM) lost = true;
                }
                if (lost) continue;
                if (!PoleSign(map, occ, st, c, i, min, max)) st.refusedPoleSigns++;
            }
        }

        /// <summary>The nearest collector, arterial or trunk carriageway on
        /// the ground (a business's frontage) within r of its edge.</summary>
        static bool NearestArterial(CityMap map, Vector2 p, float r, out int best, out float bestS)
        {
            best = -1; bestS = 0f;
            float bd = r;
            ixScratch.Clear();
            map.EdgeSegsInRect(p - Vector2.one * (r + 20f), p + Vector2.one * (r + 20f), ixScratch);
            foreach (int packed in ixScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (e.link || e.tunnel || e.bridge || e.cls < 1 || e.cls > 4) continue;
                Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float tq = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                float dist = Vector2.Distance(p, a + d * tq) - e.width * 0.5f;
                if (dist < bd || (Mathf.Approximately(dist, bd) && ei < best)) { bd = dist; best = ei; bestS = e.s[si] + Mathf.Sqrt(L2) * tq; }
            }
            return best >= 0;
        }

        static bool PoleSign(CityMap map, RoadsideOccupancy occ, SignTile st, PoleCand c, int salt, Vector2 min, Vector2 max)
        {
            var e = map.edges[c.edge];
            if (e.ElevatedAt(c.s)) return false;
            float W = c.w, H = c.w;
            // its drivers: up the road (the front), and down it on a two-way road (the back)
            var viewA = Driver(map, e, c.s, -PoleViewM, AutoLane, out bool okA);
            bool okB = true;
            var viewB = e.oneway ? Vector3.zero : Driver(map, e, c.s, PoleViewM, AutoLane, out okB);
            if (!okA || !okB || !DriverOk(viewA, V3(c.nominal, 0f)) || (!e.oneway && !DriverOk(viewB, V3(c.nominal, 0f)))) return false;
            foreach (float extra in new[] { 0f, 2f, 4f })
            {
                var P = c.nominal + c.f * extra;
                if (!InTile(P, min, max) || occ.At(P) != 0) continue;
                // square to the road on a straight: turned so the front looks
                // at one driver and the back at the other (on a bend, halfway)
                var n = Flat(V3(P, 0f), viewA);
                if (!e.oneway)
                {
                    var nb = Flat(V3(P, 0f), viewB);
                    var bis = n - nb;
                    n = bis.sqrMagnitude > 1e-4f ? bis.normalized : n;
                }
                var u = new Vector2(-n.z, n.x);
                if (Vector2.Dot(u, c.f) < 0f) u = -u;
                var a = P - u * (W * 0.5f);
                var b = P + u * (W * 0.5f);
                if (!SegFree(occ, a, b, min, max, BlockBits)) continue;
                float g = CityMeshes.LatticeAt(map, P.x, P.y);
                float bottom = g + 4.5f + 2f * CityTrees.Hash01(c.edge, (int)(c.s * 3f), 67);
                var centre = V3(P, bottom + H * 0.5f);
                // a bend too sharp for one cabinet to face both ways
                if (Vector3.Dot(n, (viewA - centre).normalized) < FaceDotPlace ||
                    (!e.oneway && Vector3.Dot(-n, (viewB - centre).normalized) < FaceDotPlace)) return false;
                BoxFacing(centre, n, W * 0.5f, H * 0.5f, 0.22f, PoleCell(c.cellIndex), BlackSide, PoleCell(c.cellIndex));
                // one post, or two under a wide cabinet
                int posts = W >= 2.8f ? 2 : 1;
                float gy = g - 0.3f;
                for (int k = 0; k < posts; k++)
                {
                    var pp = posts == 1 ? P : P + u * ((k == 0 ? -1f : 1f) * W / 3f);
                    Box(V3(pp, (gy + bottom) * 0.5f), new Vector3(u.x, 0f, u.y), Vector3.up, n, 0.13f, (bottom - gy) * 0.5f, 0.13f, MetalDark, MetalDark);
                }
                int first = st.faces.Count;
                st.faces.Add(new Face { centre = centre, normal = n, w = W, h = H, kind = Kind.PoleSign, viewer = viewA });
                if (!e.oneway) st.faces.Add(new Face { centre = centre, normal = -n, w = W, h = H, kind = Kind.PoleSign, viewer = viewB });
                // its post and cabinet, and no tree trunk within PoleTreeM of
                // it: the business keeps its sign in view
                occ.MarkDisc(P, PoleTreeM + RoadsideOccupancy.CellPadM, RoadsideOccupancy.Other);
                occ.MarkCapsule(a, b, 0.5f + RoadsideOccupancy.CellPadM, RoadsideOccupancy.Other);
                st.signs.Add(new Sign
                {
                    kind = Kind.PoleSign, pos = P, ground = g, bottom = bottom, top = bottom + H, route = -1, osm = c.poi, lit = true,
                    edge = c.edge, firstFace = first, faces = st.faces.Count - first, owner = c.nominal,
                });
                st.poleSigns++;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        //  The mesh
        // ------------------------------------------------------------------

        static readonly List<Vector3> vs = new List<Vector3>(4096), ns = new List<Vector3>(4096);
        static readonly List<Vector2> uvs = new List<Vector2>(4096);
        static readonly List<int> tris = new List<int>(8192);

        static void Begin() { vs.Clear(); ns.Clear(); uvs.Clear(); tris.Clear(); }

        static Mesh End(Vector2 min)
        {
            if (vs.Count == 0) return null;
            var origin = new Vector3(min.x, 0f, min.y);
            for (int i = 0; i < vs.Count; i++) vs[i] -= origin;
            var m = new Mesh { name = "signs" };
            if (vs.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(vs);
            m.SetNormals(ns);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0, false);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>One quad seen from the side <paramref name="n"/> points to:
        /// the texture's u runs to the viewer's right, v up.</summary>
        static void Quad(Vector3 c, Vector3 n, Vector3 right, Vector3 up, float hr, float hu, RectInt cell)
        {
            int v = vs.Count;
            vs.Add(c - right * hr - up * hu); vs.Add(c - right * hr + up * hu);
            vs.Add(c + right * hr + up * hu); vs.Add(c + right * hr - up * hu);
            for (int k = 0; k < 4; k++) ns.Add(n);
            // half a texel in, so a point-filtered edge never takes the neighbour's colour
            float u0 = (cell.x + 0.5f) / AtlasPx, u1 = (cell.x + cell.width - 0.5f) / AtlasPx;
            float v1 = 1f - (cell.y + 0.5f) / AtlasPx, v0 = 1f - (cell.y + cell.height - 0.5f) / AtlasPx;
            uvs.Add(new Vector2(u0, v0)); uvs.Add(new Vector2(u0, v1)); uvs.Add(new Vector2(u1, v1)); uvs.Add(new Vector2(u1, v0));
            tris.Add(v); tris.Add(v + 1); tris.Add(v + 2);
            tris.Add(v); tris.Add(v + 2); tris.Add(v + 3);
        }

        /// <summary>A box on orthonormal axes (x, y, z) with half sizes: every
        /// face wears <paramref name="sides"/> but the +z face, which wears
        /// <paramref name="front"/>.</summary>
        static void Box(Vector3 c, Vector3 x, Vector3 y, Vector3 z, float hx, float hy, float hz, RectInt front, RectInt sides, RectInt? back = null)
        {
            x.Normalize(); y.Normalize(); z.Normalize();
            FaceOf(c, z, y, x, y, z, hx, hy, hz, front);
            FaceOf(c, -z, y, x, y, z, hx, hy, hz, back ?? sides);
            FaceOf(c, x, y, x, y, z, hx, hy, hz, sides);
            FaceOf(c, -x, y, x, y, z, hx, hy, hz, sides);
            FaceOf(c, y, z, x, y, z, hx, hy, hz, sides);
            FaceOf(c, -y, z, x, y, z, hx, hy, hz, sides);
        }

        static void FaceOf(Vector3 c, Vector3 n, Vector3 up, Vector3 x, Vector3 y, Vector3 z, float hx, float hy, float hz, RectInt cell)
        {
            var right = Vector3.Cross(n, up).normalized;
            float Half(Vector3 v) => Mathf.Abs(Vector3.Dot(v, x)) * hx + Mathf.Abs(Vector3.Dot(v, y)) * hy + Mathf.Abs(Vector3.Dot(v, z)) * hz;
            Quad(c + n * Half(n), n, right, up, Half(right), Half(up), cell);
        }

        /// <summary>A panel box whose front (the picture) looks along
        /// <paramref name="n"/>, upright; the back wears <paramref name="back"/>
        /// (the steel, or the same picture for a double-faced cabinet).</summary>
        static void BoxFacing(Vector3 c, Vector3 n, float halfW, float halfH, float halfD, RectInt front, RectInt sides, RectInt? back = null)
        {
            var up = Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up;
            var x = Vector3.Cross(n, up).normalized;
            var y = Vector3.Cross(x, n).normalized;
            Box(c, x, y, n, halfW, halfH, halfD, front, sides, back);
        }

        /// <summary>The kit's sign material (the atlas); null without a kit
        /// (the signs then stand with their renderer off, not pink).</summary>
        public static Material Material()
        {
            var kit = CityKit.Get();
            return kit != null ? kit.signs : null;
        }
    }
}
