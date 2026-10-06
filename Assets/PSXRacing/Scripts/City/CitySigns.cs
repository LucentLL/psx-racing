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
    ///     frontage every <see cref="FrontStepM"/> (<see cref="FrontShare"/> of
    ///     the stations) where OSM maps no shop - commercial or retail land, a
    ///     business near, or any arterial the game lines with stores - with a
    ///     store, not a house or an empty field, behind; never before a
    ///     building at the kerb (<see cref="SetbackMinM"/>), nor uptown inside
    ///     the loop (<see cref="UptownNoPoleM"/>: its signs are on the walls);
    ///     no two on one side of the road within
    ///     <see cref="PoleSignSpacingM"/> (the plan: every 30-60 m on commercial
    ///     frontage). A 2.4-3.2 m cabinet 6-10 m up, square to the road so
    ///     both directions read it, lit from inside at night. Breakaway (Q15):
    ///     no collider.
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
    /// Every face is FICTIONAL and made of the owner's pack pictures only
    /// (tools/city/signs_atlas.py; no lettering drawn in code).
    ///
    /// WHERE: every post, leg and face stands on free ground (no pavement,
    /// clear zone, sight triangle, corner spot, building, water, deck or race
    /// run-off), and takes its ground on the <see cref="RoadsideOccupancy"/>
    /// mask before the trees are planted (the plan's priority: signs before
    /// trees). A billboard and a business's cabinet also keep the trees out of
    /// their drivers' line of sight, as their owners would. Nothing overhangs a
    /// road but a gantry. A billboard's post and faces and a business's
    /// cabinet also stand clear of the utility poles, their crossarms,
    /// cobra-heads and wires (<see cref="CityPoles.SignClear"/>, the WP-15
    /// review), stepping back from the road past the pole line.
    ///
    /// ACROSS TILE SEAMS: a gantry spans up to 85 m and a billboard's line of
    /// sight runs 60 m, so both are decided from global data only - the
    /// STATIC mask (<see cref="RoadsideOccupancy.Static"/>: everything but a
    /// tile build's own fill houses and lamps) of whatever tile each part lands
    /// in - and every tile they reach finds the same ones and marks their
    /// ground on its own mask before its trees (<see cref="Pure"/>). The tile
    /// that owns one draws it; its owner also drops one whose post or face it
    /// finds on its own fill house or lamp (a neighbour then keeps a few cells
    /// clear for nothing, never the reverse). A business's cabinet stands
    /// wholly inside its own tile (<see cref="PoleInsetM"/>).
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
        /// <summary>The signs keep clear of the utility poles, their wires,
        /// crossarms and cobra-heads (<see cref="CityPoles.SignClear"/>; the
        /// WP-15 review). The sign audit turns it off once to show its wire
        /// check has teeth.</summary>
        public static bool KeepOffPoles = true;
        /// <summary>The sign audit's look at why business signs do not stand:
        /// (the edge, the nominal post, the outcome) for every candidate of a
        /// tile, when set.</summary>
        public static System.Action<int, Vector2, string> PoleTrace;
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
        /// <summary>Two billboards' nominal posts this close always compete
        /// (a board facing another road at a corner).</summary>
        public const float BoardMinM = 20f;
        /// <summary>Business pole signs: no two on one side of a road within
        /// PoleSignSpacingM; a business within PoiReachM of a collector or
        /// bigger; the frontage fill looked at every FrontStepM, FrontShare of
        /// the stations asking for one (the plan: every 30-60 m).</summary>
        public const float PoleSignSpacingM = 30f, PoiReachM = 70f, FrontStepM = 32f, FrontShare = 1f;
        /// <summary>Two cabinets on opposite sides of a road still keep this apart.</summary>
        public const float PoleSignMinM = 12f;
        /// <summary>No tree trunk this close to a business's sign.</summary>
        public const float PoleTreeM = 5f;
        /// <summary>A cabinet's post stands at least this far inside its own
        /// tile, so its ground and the trees it keeps off are all the tile's.</summary>
        public const float PoleInsetM = PoleTreeM + RoadsideOccupancy.CellPadM + 0.2f;
        /// <summary>How far toward each of its drivers a cabinet keeps tree
        /// trunks off its line of sight, and how wide.</summary>
        public const float PoleSightM = 45f, PoleSightR = 2.5f;
        /// <summary>Where a frontage fill looks for the store behind it: this
        /// deep from the post, this far either way along the road.</summary>
        public const float BehindDeepM = 90f, BehindAlongM = 35f;
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
        /// <summary>How far a gantry's ground can be from its exit's first
        /// station (its legs, and the stations stepped back up the road), and
        /// a billboard's from its nominal post (the post's setback, its faces
        /// and the 60 m of its drivers' line of sight): a tile marks every one
        /// that could reach it.</summary>
        public const float ReachGantryM = 200f, ReachBoardM = 125f;
        /// <summary>How far round a tile its candidates are gathered: a
        /// billboard's reach, the freeway spacing it is settled against, and
        /// a post's offset from its station.</summary>
        public const float Gather = 340f;
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
        /// <summary>The cabinets a business kind puts up (atlas pole cells,
        /// every one of pack pictures: 0 6twelve, 1 the 6twelve price board,
        /// 2 a burger, 3 the BURGERS tin, 4 a pizza, 5 the PIZZA banner, 6 EAT
        /// GOOD FOOD, 7 a motel's palm beach, 8 a bank's gold bar, 9 a
        /// pharmacy's prescription label, 10 a car wash's wheel and water, 11
        /// a car lot's plate and wheels, 12 tyres, 13 a strip mall's tenant
        /// pylon, 14 a grocer's produce, 15 a lounge's dancers).</summary>
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

        /// <summary>Ground a sign takes on the mask: a capsule (a disc when
        /// a == b). <see cref="local"/>: its owner tile's only (a cabinet's
        /// line of sight, clipped at the seam); every other mark is taken on
        /// every tile it reaches.</summary>
        public struct Mark { public Vector2 a, b; public float r; public byte bit; public bool local; }

        /// <summary>What faces, posts and lamps a sign has.</summary>
        public class Sink
        {
            public readonly List<Face> faces = new List<Face>();
            /// <summary>Solid posts, world: centre, size, yaw (degrees).</summary>
            public readonly List<(Vector3 centre, Vector3 size, float yawDeg)> posts = new List<(Vector3, Vector3, float)>();
            /// <summary>Floodlight heads, world (lamps at night).</summary>
            public readonly List<Vector3> lamps = new List<Vector3>();
        }

        public class SignTile : Sink
        {
            public int tx, tz;
            public readonly List<Sign> signs = new List<Sign>();
            /// <summary>The ground this tile's own signs take (their marks),
            /// for the audit.</summary>
            public readonly List<Mark> marks = new List<Mark>();
            public Mesh mesh;
            public int bulletins, posters, poleSigns, gantries, osm;
            /// <summary>Candidates that won their spacing but found no free ground.</summary>
            public int refusedBoards, refusedPoleSigns, refusedGantries;
            /// <summary>The business signs that did not stand, by why: lost the
            /// spacing, a frontage with no store behind, no drivers to turn to
            /// (a dead end or a T within the view), no free ground, a bend too
            /// sharp for one cabinet.</summary>
            public int poleLost, poleNoStore, poleAtKerb, poleNoDriver, poleNoGround, poleBend;
            /// <summary>Places a business's cabinet, or one of this tile's
            /// billboards and gantries, was stepped on from because a utility wire, pole or
            /// cobra-head was there (<see cref="CityPoles.SignClear"/>).</summary>
            public int poleWireSteps, boardWireSteps;
            /// <summary>Metres of store frontage in the tile (each side of a
            /// collector or bigger, a store behind it): where the plan wants a
            /// business sign every 30-60 m.</summary>
            public float storeFrontM;
            /// <summary>Gantries and billboards of this tile dropped on its own
            /// fill houses or lamps; and those of other tiles whose ground this
            /// tile marked.</summary>
            public int vetoed, foreign;
            public float ms;
        }

        /// <summary>
        /// A sign decided from GLOBAL data only (a gantry or a billboard: the
        /// kinds whose ground can cross a tile seam): its placement, its
        /// geometry as recorded boxes, its faces, posts, lamps and the ground
        /// it takes. Every tile it reaches computes the same one (cached per
        /// session), marks its ground, and its owner draws it.
        /// </summary>
        public class Pure : Sink
        {
            public Kind kind;
            public bool placed;
            public Sign sign;
            public readonly List<Mark> marks = new List<Mark>();
            /// <summary>Its posts and legs, and its faces' plan lines (the owner's
            /// look at its own fill houses and lamps).</summary>
            public readonly List<Vector2> feet = new List<Vector2>();
            public readonly List<(Vector2 a, Vector2 b)> spans = new List<(Vector2, Vector2)>();
            /// <summary>Places tried and left for a utility wire, pole or cobra-head.</summary>
            public int wireSteps;
            internal readonly List<BoxOp> ops = new List<BoxOp>();
            public Vector2 lo = Vector2.one * float.MaxValue, hi = Vector2.one * float.MinValue;
            internal void Take(Mark m)
            {
                marks.Add(m);
                lo = Vector2.Min(lo, Vector2.Min(m.a, m.b) - Vector2.one * m.r);
                hi = Vector2.Max(hi, Vector2.Max(m.a, m.b) + Vector2.one * m.r);
            }
        }

        internal struct BoxOp
        {
            public Vector3 c, x, y, z;
            public float hx, hy, hz;
            public RectInt front, sides, back;
            public bool hasBack;
        }

        // ------------------------------------------------------------------

        struct Cand
        {
            public Vector2 station, f, nominal, osmPos;
            public int edge, route, cell;
            public float s, rank, osmYaw;
            public bool freeway, osm, oneway, lit;
            public long key;
        }

        static readonly HashSet<int> segScratch = new HashSet<int>();
        static readonly List<int> edgeList = new List<int>(256);
        static readonly List<Cand> cands = new List<Cand>(64);
        static readonly List<int> poiScratch = new List<int>(64);
        static readonly List<Pure> pureNear = new List<Pure>(16);
        static readonly HashSet<Pure> pureSeen = new HashSet<Pure>();

        static CityMap mapNow;
        static CityMeshes.Trims trimsNow;
        static Dictionary<long, List<CityBuildings.B>> buildingsNow;
        static readonly Dictionary<long, Pure> pureCache = new Dictionary<long, Pure>();
        static CityMap pureMap;
        static CityMeshes.Trims pureTrims;
        static Dictionary<long, List<CityBuildings.B>> pureBuildings;
        /// <summary>While a pure sign is decided, its boxes are recorded here.</summary>
        static Pure rec;

        /// <summary>Forget every gantry and billboard decided so far (tools
        /// that change the data the signs are decided from).</summary>
        public static void ClearCache() { pureCache.Clear(); pureMap = null; }

        /// <summary>Place one tile's signs on its mask (which they then mark),
        /// and build their mesh. <paramref name="occ"/> is the tile's full mask
        /// (<see cref="RoadsideOccupancy.Build"/>) as it stands before any sign.</summary>
        public static SignTile Build(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings,
                                     CityMeshes.TileMeshes tm, RoadsideOccupancy occ, int tx, int tz)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var st = new SignTile { tx = tx, tz = tz };
            if (!CitySignData.Loaded || occ == null) return st;
            float ts = CityMeshes.TileSize;
            var min = new Vector2(tx * ts, tz * ts);
            var max = min + Vector2.one * ts;
            Setup(map, trims, buildings, tx, tz);
            Begin();

            // THE GANTRIES AND BILLBOARDS (official signs first, then
            // advertising): every one that could reach this tile, decided from
            // global data, so the tiles either side of a seam agree on them
            pureNear.Clear(); pureSeen.Clear();
            Gantries(map, trims, min, max, NoDeadline);
            Billboards(map, trims, min, max, st, NoDeadline);
            // the owner drops one that stands on its own fill house or lamp
            // (only it knows them), before anything is marked
            var keep = new List<Pure>(pureNear.Count);
            foreach (var pu in pureNear)
            {
                if (InTile(pu.sign.owner, min, max) && OnTileOnly(occ, pu, min, max)) { st.vetoed++; continue; }
                keep.Add(pu);
            }
            foreach (var pu in keep)
            {
                bool mine = InTile(pu.sign.owner, min, max);
                if (mine && !pu.placed && pu.kind == Kind.Gantry) st.refusedGantries++;
                if (!pu.placed || pu.hi.x < min.x || pu.hi.y < min.y || pu.lo.x > max.x || pu.lo.y > max.y) { if (!mine) continue; }
                foreach (var m in pu.marks) occ.MarkCapsule(m.a, m.b, m.r, m.bit);
                if (mine) { Emit(st, pu); st.boardWireSteps += pu.wireSteps; }
                else st.foreign++;
            }
            // then the businesses, on what is left
            PoleSigns(map, trims, buildings, tm, occ, st, min, max);
            st.mesh = End(min);
            st.ms = (float)clock.Elapsed.TotalMilliseconds;
            return st;
        }

        /// <summary>
        /// Decide, a slice at a time, every gantry and billboard that could
        /// reach a tile (and the static masks they are decided on), so the
        /// tile's tree frame finds them all decided: CityWorld spends a frame
        /// of its own on each slice before it plants. 0: nothing was left to
        /// decide; 1: this slice decided the last of them; 2: more to come.
        /// Every decision is the same whenever and wherever it is made.
        /// </summary>
        public static int Prepare(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings,
                                  int tx, int tz, float budgetMs)
        {
            if (!Enabled || !CitySignData.Loaded || map == null) return 0;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            long deadline = start + (long)(budgetMs * System.Diagnostics.Stopwatch.Frequency / 1000.0);
            float ts = CityMeshes.TileSize;
            var min = new Vector2(tx * ts, tz * ts);
            var max = min + Vector2.one * ts;
            Setup(map, trims, buildings, tx, tz);
            decided = 0;
            pureNear.Clear(); pureSeen.Clear();
            bool done = Gantries(map, trims, min, max, deadline) && Billboards(map, trims, min, max, null, deadline);
            pureNear.Clear(); pureSeen.Clear();
            return !done ? 2 : decided > 0 ? 1 : 0;
        }
        /// <summary>How many gantries and billboards the last slice decided.</summary>
        static int decided;
        const long NoDeadline = long.MaxValue;

        /// <summary>The data a tile's signs are decided from, and the roads
        /// round it (kept while the same tile asks again).</summary>
        static void Setup(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings, int tx, int tz)
        {
            mapNow = map; trimsNow = trims; buildingsNow = buildings;
            // the billboards keep clear of the poles (when both are on): a
            // tool that switches either decides them again
            bool withPoles = KeepOffPoles && CityPoles.Enabled;
            if (pureMap != map || pureTrims != trims || pureBuildings != buildings || pureWithPoles != withPoles)
            {
                pureCache.Clear();
                pureMap = map; pureTrims = trims; pureBuildings = buildings; pureWithPoles = withPoles;
                edgeTile = long.MinValue;
            }
            long key = ((long)tx << 24) ^ (tz & 0xFFFFFF);
            if (key == edgeTile) return;
            edgeTile = key;
            float ts = CityMeshes.TileSize;
            var min = new Vector2(tx * ts, tz * ts);
            var max = min + Vector2.one * ts;
            segScratch.Clear(); edgeList.Clear(); edgeSeen.Clear();
            map.EdgeSegsInRect(min - Vector2.one * Gather, max + Vector2.one * Gather, segScratch);
            foreach (int packed in segScratch) if (edgeSeen.Add(packed >> 12)) edgeList.Add(packed >> 12);
            edgeList.Sort();
        }
        static long edgeTile = long.MinValue;
        static bool pureWithPoles;
        static readonly HashSet<int> edgeSeen = new HashSet<int>();

        /// <summary>A sign's part (a capsule a..b of radius r in plan) clear
        /// of the utility poles, their wires, crossarms and cobra-heads
        /// (<see cref="CityPoles.SignClear"/>), when the signs keep off them.</summary>
        static bool OffPoles(Vector2 a, Vector2 b, float r) =>
            !KeepOffPoles || CityPoles.SignClear(mapNow, trimsNow, buildingsNow, a, b, r);

        /// <summary>A business cabinet's half depth (its two faces 0.44 m apart).</summary>
        const float CabinetHalfD = 0.22f;
        /// <summary>A gantry's plan half depth along the road: its legs (1.4 m), the truss (1.1 m).</summary>
        const float GantryHalfD = 0.75f;
        /// <summary>A billboard face's plan depth: the steel 0.35 m behind the
        /// picture, the catwalk and the floodlights' heads 1.62 m in front.</summary>
        const float BoardBackM = 0.35f, BoardFrontM = 1.62f;

        /// <summary>A billboard's post, the beam from its head to a face, and
        /// the face with its catwalk and floodlights, clear of the poles and
        /// their wires (the WP-15 review).</summary>
        static bool BoardOffPoles(Vector2 P, float postR, Vector2 a0, Vector2 a1, Vector3 n)
        {
            if (!KeepOffPoles) return true;
            if (!OffPoles(P, P, postR) || !OffPoles(P, (a0 + a1) * 0.5f, 0.25f)) return false;
            var off = new Vector2(n.x, n.z) * ((BoardFrontM - BoardBackM) * 0.5f);
            return OffPoles(a0 + off, a1 + off, (BoardFrontM + BoardBackM) * 0.5f);
        }

        // a slice always decides one at least, so a slice never ends where it began
        static bool Due(long deadline, long key) => deadline != NoDeadline && decided > 0 && !pureCache.ContainsKey(key) && System.Diagnostics.Stopwatch.GetTimestamp() > deadline;

        static bool InTile(Vector2 p, Vector2 min, Vector2 max) => p.x >= min.x && p.y >= min.y && p.x < max.x && p.y < max.y;
        static bool Inset(Vector2 p, Vector2 min, Vector2 max, float m) => p.x >= min.x + m && p.y >= min.y + m && p.x < max.x - m && p.y < max.y - m;
        static Vector2 RightOf(Vector2 t) => new Vector2(t.y, -t.x);   // x east, z north: facing +z the right hand is +x
        static Vector3 V3(Vector2 p, float y) => new Vector3(p.x, y, p.y);
        static Vector2 P2(Vector3 p) => new Vector2(p.x, p.z);

        /// <summary>The owner's own fill houses and lamp feet under a pure
        /// sign's posts, legs or faces (in the tile only: those are the ground
        /// the static mask it was decided on does not know).</summary>
        static bool OnTileOnly(RoadsideOccupancy occ, Pure pu, Vector2 min, Vector2 max)
        {
            if (!pu.placed) return false;
            foreach (var f in pu.feet) if (InTile(f, min, max) && occ.At(f) != 0) return true;
            foreach (var (a, b) in pu.spans)
            {
                float L = Vector2.Distance(a, b);
                int n = Mathf.Max(1, Mathf.CeilToInt(L / 1.5f));
                for (int i = 0; i <= n; i++)
                {
                    var q = Vector2.Lerp(a, b, i / (float)n);
                    if (InTile(q, min, max) && (occ.At(q) & BlockBits) != 0) return true;
                }
            }
            return false;
        }

        static void Emit(SignTile st, Pure pu)
        {
            if (!pu.placed) return;
            int first = st.faces.Count;
            st.faces.AddRange(pu.faces);
            st.posts.AddRange(pu.posts);
            st.lamps.AddRange(pu.lamps);
            st.marks.AddRange(pu.marks);
            var sg = pu.sign;
            sg.firstFace = first;
            st.signs.Add(sg);
            foreach (var op in pu.ops) BoxNow(op.c, op.x, op.y, op.z, op.hx, op.hy, op.hz, op.front, op.sides, op.hasBack ? op.back : (RectInt?)null);
            switch (sg.kind)
            {
                case Kind.Gantry: st.gantries++; break;
                case Kind.Bulletin: st.bulletins++; break;
                case Kind.Poster: st.posters++; break;
            }
            if (sg.osm) st.osm++;
        }

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

        /// <summary>The arc of the nearest point of an edge's line to p.</summary>
        static float ProjectS(CityMap.Edge e, Vector2 p)
        {
            float best = float.MaxValue, bs = 0f;
            for (int i = 0; i + 1 < e.pts.Length; i++)
            {
                Vector2 a = e.pts[i], d = e.pts[i + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                float dd = (p - (a + d * t)).sqrMagnitude;
                if (dd < best) { best = dd; bs = Mathf.Lerp(e.s[i], e.s[i + 1], t); }
            }
            return bs;
        }

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
            else if (remaining == 0f && ok)
            {
                // on the pavement there (roads pass L5: a panel over an aux
                // lane is read 150 m back, where that lane may not be yet)
                float sAt = Mathf.Clamp(e.length > 0f ? ProjectS(e, p) : 0f, 0f, e.length);
                LineModel.Extents(e, sAt, out float eM, out float eP);
                var tq = e.TangentAt(sAt);
                float k = Vector2.Dot(RightOf(toward), new Vector2(-tq.y, tq.x));   // lateral (+ left of a->b) per unit of lane
                if (Mathf.Abs(k) > 0.5f)
                {
                    float lat = Mathf.Clamp(lane * k, -(eM - 0.6f), eP - 0.6f);
                    lane = lat / k;
                }
            }
            return new Vector3(p.x, y + EyeM, p.y) + V3(RightOf(toward) * lane, 0f);
        }

        /// <summary>The static mask's bits at a point, in whatever tile it is.</summary>
        static byte StaticAt(Vector2 p) => RoadsideOccupancy.StaticAt(mapNow, trimsNow, buildingsNow, p);

        /// <summary>Is a plan point free for a pure sign: clear of every
        /// carriageway's pavement and clear zone and of every deck, and of what
        /// the static mask reserves there (<paramref name="bits"/>)?</summary>
        static bool PureFree(Vector2 p, byte bits) => (StaticAt(p) & bits) == 0 && RoadsClear(mapNow, trimsNow, p);

        /// <summary>Is a plan point free for a business's cabinet: in the tile,
        /// on the tile's own mask; and off every road's keep-out.</summary>
        static bool Free(RoadsideOccupancy occ, Vector2 p, Vector2 min, Vector2 max, byte bits)
        {
            if (!InTile(p, min, max) || (occ.At(p) & bits) != 0) return false;
            return RoadsClear(mapNow, trimsNow, p);
        }

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

        /// <summary>Every sample of a plan segment free for a pure sign.</summary>
        static bool PureSegFree(Vector2 a, Vector2 b, byte bits)
        {
            float L = Vector2.Distance(a, b);
            int n = Mathf.Max(1, Mathf.CeilToInt(L / 1.5f));
            for (int i = 0; i <= n; i++) if (!PureFree(Vector2.Lerp(a, b, i / (float)n), bits)) return false;
            return true;
        }

        /// <summary>Every sample of a plan segment free on the tile's mask (a cabinet's footprint).</summary>
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

        /// <summary>A motorway carriageway ending at an exit: its end node has
        /// a one-way ramp and the motorway going on out of it.</summary>
        static bool IsExit(CityMap map, CityMap.Edge e)
        {
            if (e.link || e.cls < 5 || !e.oneway || e.tunnel) return false;
            int n = e.b;
            bool link = false, main = false;
            foreach (int oi in map.nodeEdges[n])
            {
                var o = map.edges[oi];
                if (o.a != n || !o.oneway || o == e) continue;
                if (o.link) link = true; else if (o.cls >= 5) main = true;
            }
            return link && main;
        }

        static float GantryS0(CityMap.Edge e) => e.length > GantryBackM + 10f ? e.length - GantryBackM : e.length * 0.5f;

        static bool Gantries(CityMap map, CityMeshes.Trims trims, Vector2 min, Vector2 max, long deadline)
        {
            var lo = min - Vector2.one * ReachGantryM;
            var hi = max + Vector2.one * ReachGantryM;
            foreach (int ei in edgeList)
            {
                var e = map.edges[ei];
                if (!IsExit(map, e)) continue;
                var o = e.PointAt(GantryS0(e));
                if (o.x < lo.x || o.y < lo.y || o.x > hi.x || o.y > hi.y) continue;
                if (Due(deadline, GantryKey(ei))) return false;
                var pu = PureGantry(map, trims, ei);
                if (pureSeen.Add(pu)) pureNear.Add(pu);
            }
            return true;
        }

        static long GantryKey(int ei) => (1L << 56) | (uint)ei;

        /// <summary>An exit's gantry, decided once from global data.</summary>
        static Pure PureGantry(CityMap map, CityMeshes.Trims trims, int ei)
        {
            long key = GantryKey(ei);
            if (pureCache.TryGetValue(key, out var pu)) return pu;
            decided++;
            pu = new Pure { kind = Kind.Gantry };
            var e = map.edges[ei];
            float s0 = GantryS0(e);
            pu.sign.owner = e.PointAt(s0);
            var was = rec; rec = pu;
            try
            {
                // on a deck there is nothing to stand a leg on: step back up the road
                for (float s = s0; s > 5f && !pu.placed && s0 - s < 130f; s -= 40f)
                {
                    if (e.bridge || e.ElevatedAt(s)) continue;
                    pu.placed = Gantry(map, trims, pu, e, s, pu.sign.owner);
                }
            }
            finally { rec = was; }
            pureCache[key] = pu;
            return pu;
        }

        static bool FindLeg(Vector2 c, Vector2 dir, float from, out Vector2 leg)
        {
            for (float d = from + 1f; d <= LegReachM; d += 0.5f)
            {
                leg = c + dir * d;
                if (PureFree(leg, (byte)(BlockBits | RoadsideOccupancy.Sight | RoadsideOccupancy.Corner | RoadsideOccupancy.Other)))
                    return true;
            }
            leg = c;
            return false;
        }

        static bool Gantry(CityMap map, CityMeshes.Trims trims, Pure pu, CityMap.Edge e, float s, Vector2 owner)
        {
            RoadsideOccupancy.RoadEdgeAt(e, trims, s, out var c, out _, out float hwL, out float hwR);
            var t = e.TangentAt(s);
            var right = RightOf(t);
            if (!FindLeg(c, right, hwR, out var legR)) return false;
            // a sign bridge over the whole road; failing a place for its left
            // leg (no room in the median outside the clear zones, and the far
            // carriageway too wide to reach over), a cantilever from the right
            // leg out to the carriageway's left edge
            bool cantilever = !FindLeg(c, -right, hwL, out var legL) || Vector2.Distance(legL, legR) > MaxSpanM;
            var armEnd = cantilever ? c - right * (hwL - 0.3f) : legL;
            // clear of the utility poles and their wires: a leg found past a
            // frontage road stands its truss over that road's pole line (the
            // sign audit found three such, the WP-15 review). A left leg's
            // span that crosses one becomes a cantilever from the right leg;
            // a right leg's, the next station up the road
            if (!OffPoles(armEnd, legR, GantryHalfD))
            {
                pu.wireSteps++;
                if (cantilever) return false;
                cantilever = true;
                armEnd = c - right * (hwL - 0.3f);
                if (!OffPoles(armEnd, legR, GantryHalfD)) return false;
            }

            // the highest road under the span
            float roadTop = e.YAt(s);
            float span = Vector2.Distance(armEnd, legR);
            for (float d = 0f; d <= span; d += 2f)
            {
                roadTop = Mathf.Max(roadTop, RoadTopAt(map, trims, Vector2.Lerp(armEnd, legR, d / span), 0.5f));
                // and under the panels, hung 0.72 m toward the traffic (roads
                // pass L5: an aux lane's pavement can rise under them)
                roadTop = Mathf.Max(roadTop, RoadTopAt(map, trims, Vector2.Lerp(armEnd, legR, d / span) - t * 0.72f, 0.5f));
            }
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
            // the panels over the road's own lanes: a merge zone's aux lane
            // (roads pass L5) widens the pavement, not the destinations
            LineModel.ExtentsNoAux(e, s, out float pL, out float pR);
            float laneW = Mathf.Clamp((pL + pR - e.shl - e.shr) / total, 2.8f, 4.2f);
            var leftEdge = c - right * pL;
            float tallest = 0f;
            int lane = 0;
            var n3 = new Vector3(-t.x, 0f, -t.y);   // the panels face the traffic coming up to them
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
                pu.faces.Add(new Face
                {
                    centre = V3(pc, bottom + h * 0.5f) + n3 * 0.72f, normal = n3, w = w, h = h, kind = Kind.Gantry,
                    viewer = Driver(map, e, s, -ViewAheadM, mid - pL),
                });
            }
            // truss: a box beam behind the panels' upper half, leg to leg
            float truss = bottom + tallest - 0.7f;
            var mid3 = V3((armEnd + legR) * 0.5f, truss);
            var along = V3((legR - armEnd) / Mathf.Max(0.01f, span), 0f);
            Box(mid3, along, Vector3.up, new Vector3(t.x, 0f, t.y), span * 0.5f + 0.35f, 0.55f, 0.55f, MetalLight, MetalLight);
            // the panels, hung on its front
            for (int i = 0; i < pu.faces.Count; i++)
            {
                var f = pu.faces[i];
                bool exit = i == pu.faces.Count - 1 && pu.faces.Count >= 2;
                BoxFacing(f.centre - n3 * 0.08f, n3, f.w * 0.5f, f.h * 0.5f, 0.08f, exit ? GantryExit : GantryThrough, MetalLight);
            }
            // legs: from the ground to the top of the truss
            foreach (var leg in cantilever ? new[] { legR } : new[] { legL, legR })
            {
                float gy = CityMeshes.LatticeAt(map, leg.x, leg.y) - 0.3f;
                float top = truss + 0.55f;
                var lc = V3(leg, (gy + top) * 0.5f);
                Box(lc, along, Vector3.up, new Vector3(t.x, 0f, t.y), 0.35f, (top - gy) * 0.5f, 0.7f, MetalLight, MetalLight);
                pu.posts.Add((lc, new Vector3(0.7f, top - gy, 1.4f), Mathf.Atan2(t.x, t.y) * Mathf.Rad2Deg));   // 1.4 m along the road, as drawn
                pu.feet.Add(leg);
                pu.Take(new Mark { a = leg, b = leg, r = 2f + RoadsideOccupancy.CellPadM, bit = RoadsideOccupancy.Other });
            }
            // and no tree under the truss (a wide median's, past the clear zones)
            pu.Take(new Mark { a = armEnd, b = legR, r = 2f + RoadsideOccupancy.CellPadM, bit = RoadsideOccupancy.Other });
            pu.sign = new Sign
            {
                kind = Kind.Gantry, pos = c, ground = CityMeshes.LatticeAt(map, c.x, c.y), bottom = bottom, top = truss + 0.55f,
                route = -1, edge = e.index, legA = cantilever ? legR : legL, legB = legR, clearance = bottom - roadTop, firstFace = 0, faces = pu.faces.Count, owner = owner,
            };
            return true;
        }

        /// <summary>Does a disc or a capsule (the plan footprint of a
        /// billboard's post or face) touch the ground any gantry near it takes?</summary>
        static bool GantryClash(CityMap map, CityMeshes.Trims trims, Vector2 a, Vector2 b, float r)
        {
            gxScratch.Clear();
            float reach = ReachGantryM + 20f;
            var lo = Vector2.Min(a, b) - Vector2.one * reach;
            var hi = Vector2.Max(a, b) + Vector2.one * reach;
            map.EdgeSegsInRect(lo, hi, gxScratch);
            gxEdges.Clear();
            foreach (int packed in gxScratch) gxEdges.Add(packed >> 12);
            foreach (int ei in gxEdges)
            {
                var e = map.edges[ei];
                if (!IsExit(map, e)) continue;
                var pu = PureGantry(map, trims, ei);
                if (!pu.placed || pu.hi.x < lo.x + reach - r || pu.hi.y < lo.y + reach - r || pu.lo.x > hi.x - reach + r || pu.lo.y > hi.y - reach + r) continue;
                foreach (var m in pu.marks)
                    if (RoadsideOccupancy.SegSegDistance(a, b, m.a, m.b) < r + m.r) return true;
            }
            return false;
        }
        static readonly HashSet<int> gxScratch = new HashSet<int>();
        static readonly SortedSet<int> gxEdges = new SortedSet<int>();

        // ------------------------------------------------------------------
        //  Billboards
        // ------------------------------------------------------------------

        static bool Billboards(CityMap map, CityMeshes.Trims trims, Vector2 min, Vector2 max, SignTile st, long deadline)
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
                    rank = -1f + i * 1e-5f, lit = true, key = (3L << 56) | (uint)i,
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
                    float off = e.HalfMax + ZoneOffM;
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
                        station = q, f = f, nominal = q + f * (e.HalfMax + RoadsideOccupancy.ClearZoneOf(e) + setback),
                        edge = ei, s = s, route = ri, freeway = fw, oneway = e.oneway, rank = CityTrees.Hash01(ei, k, 41), lit = true,
                        cell = (int)(CityTrees.Hash01(ei, k, 43) * 1000f), key = (2L << 56) | ((long)ei << 20) | (uint)k,
                    });
                }
            }
            // spacing: a board loses to a better one on the same side within
            // the rule's distance; settled for every board that could reach
            // this tile (its competitors are all gathered)
            var rlo = min - Vector2.one * ReachBoardM;
            var rhi = max + Vector2.one * ReachBoardM;
            for (int i = 0; i < cands.Count; i++)
            {
                var c = cands[i];
                if (c.nominal.x < rlo.x || c.nominal.y < rlo.y || c.nominal.x > rhi.x || c.nominal.y > rhi.y) continue;
                bool lost = false;
                for (int j = 0; j < cands.Count && !lost; j++)
                {
                    if (j == i) continue;
                    var o = cands[j];
                    if (o.rank >= c.rank) continue;
                    float d2 = (o.nominal - c.nominal).sqrMagnitude;
                    // two boards this close always compete, whatever roads they face
                    if (d2 < BoardMinM * BoardMinM) { lost = true; break; }
                    if (Vector2.Dot(o.f, c.f) < 0.5f) continue;
                    float sp = c.freeway || o.freeway ? FreewaySpacingM : TownSpacingM;
                    if (d2 < sp * sp) lost = true;
                }
                if (lost) continue;
                if (Due(deadline, c.key)) return false;
                var pu = PureBillboard(map, trims, c);
                if (st != null && InTile(c.nominal, min, max) && !pu.placed) st.refusedBoards++;
                if (pureSeen.Add(pu)) pureNear.Add(pu);
            }
            return true;
        }

        static Pure PureBillboard(CityMap map, CityMeshes.Trims trims, Cand c)
        {
            if (pureCache.TryGetValue(c.key, out var pu)) return pu;
            decided++;
            pu = new Pure { kind = Kind.Bulletin };
            pu.sign.owner = c.nominal;
            var was = rec; rec = pu;
            try { pu.placed = Billboard(map, trims, pu, c); }
            finally { rec = was; }
            pureCache[c.key] = pu;
            return pu;
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

        static bool Billboard(CityMap map, CityMeshes.Trims trims, Pure pu, Cand c)
        {
            var e = map.edges[c.edge];
            // a freeway's 14 x 48 ft bulletin; elsewhere the median 12 x 36 ft,
            // or a third of the time a 12 x 24 ft poster
            int sizeRoll = (int)(CityTrees.Hash01(c.edge, (int)c.s, 49) * 3f);
            bool bulletin = c.freeway || sizeRoll > 0;
            float W = c.freeway ? BulletinW : bulletin ? MedianW : PosterW, H = c.freeway ? BulletinH : bulletin ? MedianH : PosterH;
            var t = e.TangentAt(c.s);
            var right = RightOf(t);
            float hwRoad = e.HalfMax + RoadsideOccupancy.ClearZoneOf(e);
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
            float postR = 1.5f + RoadsideOccupancy.CellPadM, faceR = 0.8f + RoadsideOccupancy.CellPadM;
            // coverage (2026-10-06): outside the core the real buildings stand by
            // the suburban roads too; a board they push off every setback looks
            // again 15 m up and down the road before it is refused (the first
            // round is the old one, so every board placed before stands as it was)
            bool covSite = !c.osm && !map.footprintBounds.Contains(c.station);
            var along = new Vector2(c.f.y, -c.f.x);
            for (int ti = 0; ti < tries.Length * (covSite ? 3 : 1); ti++)
            {
                float extra = tries[ti % tries.Length];
                float slide = ti < tries.Length ? 0f : ti < 2 * tries.Length ? 15f : -15f;
                var P = c.osm ? c.osmPos + c.f * extra : c.station + c.f * (hwRoad + extra) + along * slide;
                if (StaticAt(P) != 0 || !RoadsClear(map, trims, P)) continue;
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
                if (!PureSegFree(aA0, aA1, BlockBits)) continue;
                if (two && !PureSegFree(aB0, aB1, BlockBits)) continue;
                // clear of the gantries (official signs stand first)
                if (GantryClash(map, trims, P, P, postR) || GantryClash(map, trims, aA0, aA1, faceR) || (two && GantryClash(map, trims, aB0, aB1, faceR))) continue;
                // and of the utility poles and their wires (the WP-15 review:
                // the lower cables hang where the faces are): further back
                float postHalf = bulletin ? 0.45f : 0.3f;
                if (!BoardOffPoles(P, postHalf, aA0, aA1, nA) || (two && !BoardOffPoles(P, postHalf, aB0, aB1, nB))) { pu.wireSteps++; continue; }

                // heights (NCDOT's total height median is 13.4 m): a freeway
                // bulletin's face 9.5 m or more over the ground and 8 m over the
                // road; elsewhere 6.5 m and 5 m
                float g = CityMeshes.LatticeAt(map, P.x, P.y);
                float roadY = e.YAt(c.s);
                int hk = (int)(CityTrees.Hash01(c.edge, (int)c.s, 47) * 1000f);
                float bottom = c.freeway ? Mathf.Max(g + 9.5f, roadY + 8f) + (hk % 250) / 100f : Mathf.Max(g + 6.5f, roadY + 5f) + (hk % 250) / 100f;
                int cellA = bulletin ? (c.cell + (c.osm ? (int)(c.osmPos.x * 7f) & 0xFF : 0)) % Bulletins : (c.cell + (c.osm ? (int)(c.osmPos.x * 7f) & 0xFF : 0)) % Posters;
                int cellB = bulletin ? (cellA + 1 + (hk % (Bulletins - 1))) % Bulletins : (cellA + 1 + (hk % (Posters - 1))) % Posters;
                AddBoardFace(pu, aA0, aA1, nA, bottom, H, bulletin, cellA, viewA);
                if (two) AddBoardFace(pu, aB0, aB1, nB, bottom, H, bulletin, cellB, viewB);

                // the monopole, and a beam from its head to each face
                float pw = bulletin ? 0.9f : 0.6f;
                float gy = g - 0.3f;
                var pc = V3(P, (gy + bottom) * 0.5f);
                var fwd = new Vector3(t.x, 0f, t.y);
                Box(pc, new Vector3(right.x, 0f, right.y), Vector3.up, fwd, pw * 0.5f, (bottom - gy) * 0.5f, pw * 0.5f, MetalDark, MetalDark);
                pu.posts.Add((pc, new Vector3(pw, bottom - gy, pw), Mathf.Atan2(t.x, t.y) * Mathf.Rad2Deg));
                for (int i = 0; i < pu.faces.Count; i++)
                {
                    var fc = pu.faces[i].centre;
                    var to = new Vector3(fc.x - P.x, 0f, fc.z - P.y);
                    float L = to.magnitude;
                    if (L < 0.2f) continue;
                    Box(V3(P, bottom - 0.35f) + to * 0.5f, to / L, Vector3.up, Vector3.Cross(to / L, Vector3.up), L * 0.5f, 0.25f, 0.2f, MetalDark, MetalDark);
                }

                // the ground it takes: the post, the faces' footprints, and the
                // drivers' line of sight kept clear of trees
                pu.feet.Add(P);
                pu.spans.Add((aA0, aA1));
                pu.Take(new Mark { a = P, b = P, r = postR, bit = RoadsideOccupancy.Other });
                pu.Take(new Mark { a = aA0, b = aA1, r = faceR, bit = RoadsideOccupancy.Other });
                SightMark(pu, (aA0 + aA1) * 0.5f, viewA, W);
                if (two)
                {
                    pu.spans.Add((aB0, aB1));
                    pu.Take(new Mark { a = aB0, b = aB1, r = faceR, bit = RoadsideOccupancy.Other });
                    SightMark(pu, (aB0 + aB1) * 0.5f, viewB, W);
                }
                pu.sign = new Sign
                {
                    kind = bulletin ? Kind.Bulletin : Kind.Poster, pos = P, ground = g, bottom = bottom, top = bottom + H,
                    route = c.route, osm = c.osm, lit = c.lit, edge = c.edge, firstFace = 0, faces = pu.faces.Count, owner = c.nominal,
                };
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

        static void AddBoardFace(Sink sk, Vector2 a, Vector2 b, Vector3 n, float bottom, float H, bool bulletin, int cell, Vector3 viewer)
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
                sk.lamps.Add(head);
            }
            sk.faces.Add(new Face { centre = centre, normal = n, w = W, h = H, kind = bulletin ? Kind.Bulletin : Kind.Poster, viewer = viewer });
        }

        /// <summary>A billboard's owner keeps the trees out of its drivers'
        /// line of sight: the first 60 m of it, as wide as a third of the face.</summary>
        static void SightMark(Pure pu, Vector2 from, Vector3 viewer, float W)
        {
            var d = new Vector2(viewer.x, viewer.z) - from;
            float L = d.magnitude;
            if (L < 1f) return;
            pu.Take(new Mark { a = from, b = from + d / L * Mathf.Min(60f, L), r = W * 0.35f, bit = RoadsideOccupancy.Other });
        }

        // ------------------------------------------------------------------
        //  Business pole signs
        // ------------------------------------------------------------------

        struct PoleCand { public Vector2 station, f, nominal; public int edge, cellIndex; public float s, rank, w; public bool poi; }
        static readonly List<PoleCand> poles = new List<PoleCand>(128);
        /// <summary>The buildings round the tile: centre, and what it is (<see cref="Store"/>, a house, a tower).</summary>
        static readonly List<(Vector2 c, byte what, Vector2 u, float hu, float hv)> near = new List<(Vector2, byte, Vector2, float, float)>(512);
        const byte Store = 0, House = 1, Tower = 2, None = 3;
        /// <summary>A pole sign stands only before a store whose front wall is
        /// at least this far behind it (room for the cabinet); a building at the
        /// kerb wears its sign on its wall.</summary>
        public const float SetbackMinM = 3f;
        /// <summary>Uptown, inside the I-277 loop, the signs are on the walls:
        /// no business pole sign within this of Trade and Tryon.</summary>
        public const float UptownNoPoleM = 1300f;
        static readonly List<Mark> poleSight = new List<Mark>(64);

        static void PoleSigns(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings,
                              CityMeshes.TileMeshes tm, RoadsideOccupancy occ, SignTile st, Vector2 min, Vector2 max)
        {
            poles.Clear();
            // uptown's signs are on its walls
            var mid = (min + max) * 0.5f;
            if (Vector2.Distance(mid, map.uptown) < UptownNoPoleM - CityMeshes.TileSize) return;
            GatherBuildings(map, buildings, tm, min, max);
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
                var pnom = q + f * (e.HalfMax + RoadsideOccupancy.ClearZoneOf(e) + w * 0.5f + 0.8f);
                // a business at the kerb wears its sign on its wall
                if (NearestBehind(pnom, f, out float wall) != None && wall < SetbackMinM)
                {
                    if (InTile(pnom, min, max)) { st.poleAtKerb++; PoleTrace?.Invoke(ei, pnom, "poi at the kerb"); }
                    continue;
                }
                poles.Add(new PoleCand
                {
                    station = q, f = f, edge = ei, s = s, w = w, poi = true,
                    nominal = pnom,
                    cellIndex = cells[Mathf.Min(cells.Length - 1, (int)(CityTrees.Hash01(pi, 5, 73) * cells.Length))],
                    rank = CityTrees.Hash01(pi, 7, 91),
                });
            }
            // the commercial frontage where OSM maps no shop, and a store (not
            // a house, not an empty field) stands behind it
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
                        var f = side == 0 ? right : -right;
                        float w = 2.4f + 0.8f * CityTrees.Hash01(ei, 2 * k + side, 57);
                        var nom = q + f * (e.HalfMax + RoadsideOccupancy.ClearZoneOf(e) + w * 0.5f + 0.8f);
                        // commercial frontage: the landuse or a business says so,
                        // or the game stands a store on an arterial here; and a
                        // store, not a house or an empty field, is behind
                        bool landuse = (CitySignData.At(nom) & CitySignData.Frontage) != 0;
                        if (!landuse && e.cls < 2) continue;
                        // not in a divided road's median, looking across the other carriageway
                        if (RoadBehind(map, ei, nom, f)) continue;
                        byte what = NearestBehind(nom, f, out float wall);
                        bool store = what == Store && wall >= SetbackMinM;
                        if (InTile(nom, min, max))
                        {
                            if (store) st.storeFrontM += FrontStepM;
                            else if (what == Store) { st.poleAtKerb++; PoleTrace?.Invoke(ei, nom, "store at the kerb"); }
                            else if (landuse) { st.poleNoStore++; PoleTrace?.Invoke(ei, nom, "no store behind"); }
                        }
                        if (!store || CityTrees.Hash01(ei, 2 * k + side, 53) >= FrontShare) continue;
                        poles.Add(new PoleCand
                        {
                            station = q, f = f, edge = ei, s = s, w = w, nominal = nom,
                            cellIndex = FrontCells[(int)(CityTrees.Hash01(ei, 2 * k + side, 61) * FrontCells.Length) % FrontCells.Length],
                            rank = 1f + CityTrees.Hash01(ei, 2 * k + side, 59),
                        });
                    }
                }
            }
            poleSight.Clear();
            // best first: a sign loses to a better one on its own side of the
            // road within the spacing (the plan: one every 30-60 m of
            // frontage), across the road only if they would all but touch -
            // to one of the next tile's always, to one of this tile's only if
            // that one stood (the next tile judges its own the same way, so
            // no two stand closer across the seam either)
            order.Clear();
            for (int i = 0; i < poles.Count; i++) if (InTile(poles[i].nominal, min, max)) order.Add(i);
            order.Sort((x, y) => poles[x].rank.CompareTo(poles[y].rank));
            stood.Clear();
            foreach (int i in order)
            {
                var c = poles[i];
                if (Vector2.Distance(c.nominal, map.uptown) < UptownNoPoleM) continue;
                bool lost = false;
                for (int j = 0; j < poles.Count && !lost; j++)
                {
                    if (j == i) continue;
                    var o = poles[j];
                    if (o.rank >= c.rank || InTile(o.nominal, min, max)) continue;
                    lost = TooClose(o.nominal, o.f, c.nominal, c.f);
                }
                foreach (var (sn, sf) in stood) if (!lost) lost = TooClose(sn, sf, c.nominal, c.f);
                if (lost) { st.poleLost++; PoleTrace?.Invoke(c.edge, c.nominal, c.poi ? "poi lost the spacing" : "fill lost the spacing"); continue; }
                int why = PoleSign(map, occ, st, c, min, max);
                if (why == 0) stood.Add((st.signs[st.signs.Count - 1].pos, c.f));
                PoleTrace?.Invoke(c.edge, c.nominal, (c.poi ? "poi " : "fill ") + (why == 0 ? "placed" : why == 1 ? "no drivers" : why == 2 ? "no ground " + groundWhy : "bend"));
                switch (why)
                {
                    case 1: st.poleNoDriver++; break;
                    case 2: st.poleNoGround++; st.refusedPoleSigns++; break;
                    case 3: st.poleBend++; break;
                }
            }
            // the cabinets' lines of sight, once they all stand (so one's does
            // not push the next off its frontage): clipped to the tile
            foreach (var m in poleSight) { occ.MarkCapsule(m.a, m.b, m.r, m.bit); st.marks.Add(m); }
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
                float dist = Vector2.Distance(p, a + d * tq) - e.HalfMax;
                if (dist < bd || (Mathf.Approximately(dist, bd) && ei < best)) { bd = dist; best = ei; bestS = e.s[si] + Mathf.Sqrt(L2) * tq; }
            }
            return best >= 0;
        }

        /// <summary>Every building round the tile a frontage fill could look
        /// back at: the real footprints (a house by its OSM style, a glass
        /// tower, or a store), the lots (a house or trailer, a pack tower, or
        /// a store, a block, a restaurant) and the tile's fill houses.</summary>
        static void GatherBuildings(CityMap map, Dictionary<long, List<CityBuildings.B>> buildings, CityMeshes.TileMeshes tm, Vector2 min, Vector2 max)
        {
            near.Clear();
            // the fills looked at stand up to 70 m out of the tile
            var lo = min - Vector2.one * (BehindDeepM + 80f);
            var hi = max + Vector2.one * (BehindDeepM + 80f);
            int tx = Mathf.FloorToInt(min.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(min.y / CityMeshes.TileSize);
            for (int bz = tz - 1; bz <= tz + 1; bz++)
                for (int bx = tx - 1; bx <= tx + 1; bx++)
                {
                    var list = map.FootprintsInTile(bx, bz);
                    if (list != null)
                        foreach (int fi in list)
                        {
                            var f = map.footprints[fi];
                            if (f.centre.x < lo.x || f.centre.y < lo.y || f.centre.x > hi.x || f.centre.y > hi.y) continue;
                            near.Add((f.centre, f.style == 3 || f.gable ? House : f.style == 0 ? Tower : Store, f.u, f.hu, f.hv));
                        }
                    if (buildings != null && buildings.TryGetValue(((long)bx << 24) ^ (bz & 0xFFFFFF), out var lots))
                        foreach (var b in lots)
                        {
                            if (b.pos.x < lo.x || b.pos.y < lo.y || b.pos.x > hi.x || b.pos.y > hi.y) continue;
                            bool house = b.gable || (b.kind >= CityProps.House && b.kind <= CityProps.Trailer2);
                            // rgt = (cy, -sy) carries the width, fwd = (sy, cy) the depth (as the mask reads a lot)
                            near.Add((b.pos, house ? House : b.kind >= CityProps.Tower0 ? Tower : Store,
                                      new Vector2(Mathf.Cos(b.yaw), -Mathf.Sin(b.yaw)), b.w * 0.5f, b.d * 0.5f));
                        }
                }
            if (tm != null) foreach (var h in tm.houseBoxes) near.Add((h.c, House, h.u, h.hu, h.hv));
            // bucketed, for the frontage's many looks back
            nearLo = lo;
            for (int i = 0; i < nearGrid.Length; i++) nearGrid[i]?.Clear();
            for (int i = 0; i < near.Count; i++)
            {
                int gx = Mathf.Clamp(Mathf.FloorToInt((near[i].c.x - lo.x) / NearCell), 0, NearN - 1);
                int gz = Mathf.Clamp(Mathf.FloorToInt((near[i].c.y - lo.y) / NearCell), 0, NearN - 1);
                (nearGrid[gz * NearN + gx] ??= new List<int>(8)).Add(i);
            }
        }
        const float NearCell = 32f;
        const int NearN = 24;   // (256 + 2 x 170) / 32, rounded up
        static readonly List<int>[] nearGrid = new List<int>[NearN * NearN];
        static Vector2 nearLo;

        /// <summary>The nearest building behind a frontage post (within
        /// <see cref="BehindDeepM"/> of it, <see cref="BehindAlongM"/> either
        /// way along the road) is a store, not a house or an office tower
        /// (downtown signs are on the walls); false with none.</summary>
        /// <summary>Another carriageway within <see cref="BehindRoadM"/>
        /// behind a frontage post (a divided road's median side).</summary>
        static bool RoadBehind(CityMap map, int self, Vector2 P, Vector2 f)
        {
            var b = P + f * BehindRoadM;
            rbScratch.Clear();
            map.EdgeSegsInRect(Vector2.Min(P, b) - Vector2.one * 12f, Vector2.Max(P, b) + Vector2.one * 12f, rbScratch);
            foreach (int packed in rbScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                if (ei == self) continue;
                var e = map.edges[ei];
                if (e.tunnel) continue;
                if (RoadsideOccupancy.SegSegDistance(P, b, e.pts[si], e.pts[si + 1]) < e.HalfMax) return true;
            }
            return false;
        }
        const float BehindRoadM = 30f;
        static readonly HashSet<int> rbScratch = new HashSet<int>();

        /// <summary>The nearest building behind a post (its near wall within
        /// <see cref="BehindDeepM"/>, its middle within <see cref="BehindAlongM"/>
        /// either way along the road): what it is, and how far back its near
        /// wall stands (<see cref="None"/> and +infinity with none).</summary>
        static byte NearestBehind(Vector2 P, Vector2 f, out float wallDepth)
        {
            var along = new Vector2(-f.y, f.x);
            float best = float.MaxValue; byte what = None;
            float reach = BehindDeepM + BehindAlongM;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((P.x - reach - nearLo.x) / NearCell)), x1 = Mathf.Min(NearN - 1, Mathf.FloorToInt((P.x + reach - nearLo.x) / NearCell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((P.y - reach - nearLo.y) / NearCell)), z1 = Mathf.Min(NearN - 1, Mathf.FloorToInt((P.y + reach - nearLo.y) / NearCell));
            for (int gz = z0; gz <= z1; gz++)
                for (int gx = x0; gx <= x1; gx++)
                {
                    var cell = nearGrid[gz * NearN + gx];
                    if (cell == null) continue;
                    foreach (int ni in cell)
                    {
                        var (c, h, u, hu, hv) = near[ni];
                        var d = c - P;
                        var v = new Vector2(-u.y, u.x);
                        // the building's reach toward the road, off its middle
                        float ext = Mathf.Abs(Vector2.Dot(u, f)) * hu + Mathf.Abs(Vector2.Dot(v, f)) * hv;
                        float depth = Vector2.Dot(d, f) - ext, side = Mathf.Abs(Vector2.Dot(d, along));
                        if (depth + 2f * ext < -2f || depth > BehindDeepM || side > BehindAlongM) continue;
                        if (depth < best || (depth == best && h < what)) { best = depth; what = h; }
                    }
                }
            wallDepth = best;
            return what;
        }

        /// <summary>Stand one cabinet: 0, or why not (1 no drivers to turn
        /// to, 2 no free ground, 3 a bend too sharp).</summary>
        static int PoleSign(CityMap map, RoadsideOccupancy occ, SignTile st, PoleCand c, Vector2 min, Vector2 max)
        {
            var e = map.edges[c.edge];
            if (e.ElevatedAt(c.s)) return 2;
            float W = c.w, H = c.w;
            // its drivers: up the road (the front), and down it on a two-way road (the back)
            var viewA = Driver(map, e, c.s, -PoleViewM, AutoLane, out bool okA);
            bool okB = true;
            var viewB = e.oneway ? Vector3.zero : Driver(map, e, c.s, PoleViewM, AutoLane, out okB);
            if (!okA || !okB || !DriverOk(viewA, V3(c.nominal, 0f)) || (!e.oneway && !DriverOk(viewB, V3(c.nominal, 0f)))) return 1;
            groundWhy = "";
            var alongRoad = new Vector2(-c.f.y, c.f.x);
            foreach (var (extra, slide) in PoleTries)
            {
                var P = c.nominal + c.f * extra + alongRoad * slide;
                // wholly inside its own tile: its ground, and the trees it keeps off, are the tile's
                if (!Inset(P, min, max, PoleInsetM)) { groundWhy += "e"; continue; }
                if (occ.At(P) != 0) { groundWhy += occ.At(P).ToString() + ","; continue; }
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
                if (!SegFree(occ, a, b, min, max, BlockBits)) { groundWhy += "c,"; continue; }
                // clear of the utility poles, their crossarms, cobra-heads and
                // wires (the WP-15 review: a pole line runs along nearly every
                // road a business sign stands on, and its telecom cables hang
                // 6-9 m up in the band the cabinet stands in): further back
                if (!OffPoles(a, b, CabinetHalfD)) { groundWhy += "w,"; st.poleWireSteps++; continue; }
                float g = CityMeshes.LatticeAt(map, P.x, P.y);
                float bottom = g + 4.5f + 2f * CityTrees.Hash01(c.edge, (int)(c.s * 3f), 67);
                var centre = V3(P, bottom + H * 0.5f);
                // a bend too sharp for one cabinet to face both ways
                if (Vector3.Dot(n, (viewA - centre).normalized) < FaceDotPlace ||
                    (!e.oneway && Vector3.Dot(-n, (viewB - centre).normalized) < FaceDotPlace)) return 3;
                BoxFacing(centre, n, W * 0.5f, H * 0.5f, CabinetHalfD, PoleCell(c.cellIndex), BlackSide, PoleCell(c.cellIndex));
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
                var disc = new Mark { a = P, b = P, r = PoleTreeM + RoadsideOccupancy.CellPadM, bit = RoadsideOccupancy.Other };
                var cab = new Mark { a = a, b = b, r = 0.5f + RoadsideOccupancy.CellPadM, bit = RoadsideOccupancy.Other };
                occ.MarkCapsule(disc.a, disc.b, disc.r, disc.bit);
                occ.MarkCapsule(cab.a, cab.b, cab.r, cab.bit);
                st.marks.Add(disc); st.marks.Add(cab);
                // and the first PoleSightM of each driver's line of sight
                PoleSightMark(P, viewA);
                if (!e.oneway) PoleSightMark(P, viewB);
                st.signs.Add(new Sign
                {
                    kind = Kind.PoleSign, pos = P, ground = g, bottom = bottom, top = bottom + H, route = -1, osm = c.poi, lit = true,
                    edge = c.edge, firstFace = first, faces = st.faces.Count - first, owner = c.nominal,
                });
                st.poleSigns++;
                return 0;
            }
            return 2;
        }

        static string groundWhy = "";
        static readonly List<int> order = new List<int>(128);
        /// <summary>Where a cabinet may stand, round its nominal place: back
        /// from the road, then a few metres along it.</summary>
        static readonly (float back, float along)[] PoleTries =
        {
            (0f, 0f), (2f, 0f), (4f, 0f), (7f, 0f), (10f, 0f), (14f, 0f),
            (0f, 6f), (0f, -6f), (3f, 6f), (3f, -6f), (7f, 6f), (7f, -6f), (0f, 11f), (0f, -11f), (4f, 11f), (4f, -11f),
        };
        static readonly List<(Vector2 n, Vector2 f)> stood = new List<(Vector2, Vector2)>(64);

        static bool TooClose(Vector2 a, Vector2 fa, Vector2 b, Vector2 fb)
        {
            float d2 = (a - b).sqrMagnitude;
            bool sameSide = Vector2.Dot(fa, fb) > -0.5f;
            return d2 < (sameSide ? PoleSignSpacingM * PoleSignSpacingM : PoleSignMinM * PoleSignMinM);
        }

        static void PoleSightMark(Vector2 P, Vector3 viewer)
        {
            var d = new Vector2(viewer.x, viewer.z) - P;
            float L = d.magnitude;
            if (L < 1f) return;
            poleSight.Add(new Mark { a = P, b = P + d / L * Mathf.Min(PoleSightM, L), r = PoleSightR, bit = RoadsideOccupancy.Other, local = true });
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
        /// <paramref name="front"/>. While a pure sign is being decided the
        /// box is recorded for its owner to draw.</summary>
        static void Box(Vector3 c, Vector3 x, Vector3 y, Vector3 z, float hx, float hy, float hz, RectInt front, RectInt sides, RectInt? back = null)
        {
            if (rec != null)
            {
                rec.ops.Add(new BoxOp { c = c, x = x, y = y, z = z, hx = hx, hy = hy, hz = hz, front = front, sides = sides, back = back ?? default, hasBack = back.HasValue });
                return;
            }
            BoxNow(c, x, y, z, hx, hy, hz, front, sides, back);
        }

        static void BoxNow(Vector3 c, Vector3 x, Vector3 y, Vector3 z, float hx, float hy, float hz, RectInt front, RectInt sides, RectInt? back)
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
