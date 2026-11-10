using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  THE UPTOWN STREET DRESS (2026-11-09; the owner, on a road-level photo
    //  of East 3rd Street beside 237 South Tryon: "I've asked many times
    //  before to reference road level photos to make the city accurate
    //  enough to be recognizable").
    //
    //  WHAT THE PHOTOS SHOW between the curb and the building face inside
    //  the I-277 loop (17 block faces looked at at road level; the frames
    //  are Editor/CityRefSpots.Uptown.cs's spots): Tryon and Trade Streets are the
    //  DRESSED streets - brick pavers from a grey granite curb to the
    //  buildings, a tree every 9-11 m in a pit or an iron grate a metre
    //  behind the curb, the lamp posts between the trees; the side streets
    //  (College, Church, the numbered streets, Brooklyn Village) are plainer:
    //  a concrete walk, a tree in a grate every 11-13 m where the walk is
    //  whole, a single post lamp. The game drew every one of them as a bare
    //  concrete slab with posts.
    //
    //  THE RULE, keyed on what the generator knows:
    //    which street  StreetDressOf: a walk street (WalkEdge) whose middle
    //                  lies inside the freeway loop (CityPoles.Uptown) is a
    //                  SIDE street; named Tryon Street or Trade Street it is
    //                  a MAIN street.
    //    the surface   a main street's slab behind the curb is brick pavers
    //                  (Slot.WalkPaver; the curb's face and top stay the grey
    //                  slab: the granite curb), its corners too; a side
    //                  street keeps the jointed concrete.
    //    the trees     StreetTreePlan: evenly between the corners of each
    //                  block, a station every TreePitchMainM / TreePitchSideM,
    //                  none within TreeCornerClearM of a junction's drawn
    //                  mouth (the crosswalk landing, the signal poles and the
    //                  sight triangle). DressSpan stands one a metre behind
    //                  the drawn edge on a walk span that is whole there (no
    //                  curb cut or wing: a driveway, a lot or deck entrance;
    //                  at least TreeMinWalkM wide), clear of every building
    //                  wall, driveway and other road's pavement or fan, and
    //                  lays its grate (a 3 cm dark slab in the walk's own
    //                  mesh and collider). CityTrees plants the tree
    //                  (PlantStreet): a willow oak, pruned up, its crown
    //                  fitted clear of the lane like every other city tree,
    //                  its trunk solid by the city's own rule (Q15) - and
    //                  breakaway in a race route's run-off, as the lamp posts
    //                  there are.
    //    the lamps     keep their rule (the acorn post, both sides), moved to
    //                  the middle of the gap between two trees.
    //
    //  THE SKYBRIDGES (Skybridges, a table of places): the glazed walkways
    //  over the streets, each a box between two buildings at second-floor
    //  height, from OpenStreetMap's own outline of the bridge. One whose end
    //  does not stand in a building the game draws is left out, never
    //  floated (SkybridgeCheck says which and why).
    //
    //  PSX_CITY_STREETDRESS=0: the walks as they were (the A/B);
    //  PSX_CITY_SKYBRIDGES=0: no bridges.
    // ======================================================================
    public static partial class CityMeshes
    {
        /// <summary>PSX_CITY_STREETDRESS=0: no pavers, grates or street trees
        /// (the before). Not readonly: the reference shots photograph both.</summary>
        public static bool StreetDressOn = System.Environment.GetEnvironmentVariable("PSX_CITY_STREETDRESS") != "0";
        /// <summary>PSX_CITY_SKYBRIDGES=0: no skybridges.</summary>
        public static bool SkybridgesOn = System.Environment.GetEnvironmentVariable("PSX_CITY_SKYBRIDGES") != "0";

        public const int DressNone = 0, DressSide = 1, DressMain = 2;
        /// <summary>A tree every this many metres (the photos: 9-11 m on Tryon and Trade, 11-13 m on the side streets).</summary>
        public const float TreePitchMainM = 10f, TreePitchSideM = 12f;
        /// <summary>No tree this close to a junction's drawn mouth.</summary>
        public const float TreeCornerClearM = 9f;
        /// <summary>The trunk stands this far behind the drawn edge (the curb's foot).</summary>
        public const float TreeOutM = 1.0f;
        /// <summary>A walk narrower than this there takes no tree.</summary>
        const float TreeMinWalkM = 2.0f;
        /// <summary>The trunk also stands this far off its road's MODEL on its own side (the map line
        /// and the line model's half width on that side, the widest within a mask piece's 8 m either
        /// way: a lane tapering in beside the station), so the trunk's cards clear the city's 0.65 m
        /// leaf margin there as well as off the drawn edge; a walk too narrow for it takes no tree.</summary>
        const float TreeLaneClearM = 0.95f;

        /// <summary>How far plan point p stands off an edge's road model at arc s, on p's own side
        /// of the map line (the line model's extent on that side - not the wider of the two, which
        /// is what the roadside mask keeps free-ground trees off).</summary>
        public static float OffRoadModel(CityMap.Edge e, Trims trims, float s, Vector2 p)
        {
            RoadsideOccupancy.RoadEdgeAt(e, trims, Mathf.Clamp(s, 0f, e.length), out var c, out var right, out float hl, out float hr);
            float lat = Vector2.Dot(p - c, right);
            return Mathf.Abs(lat) - (lat >= 0f ? hr : hl);
        }
        /// <summary>The grate: this long along the street, from just behind the curb's top this far across, this proud of the slab.</summary>
        const float GrateAlongM = 1.5f, GrateAcrossM = 1.1f, GrateInM = 0.06f, GrateLiftM = 0.03f;
        /// <summary>One repeat of the paver texture, metres (eight basket-weave units: a brick 22 x 11 cm).</summary>
        public const float PaverTexM = 1.8f;
        static readonly float PaverUV = WalkTexM / PaverTexM;
        static Bucket PaverBucket => buckets[(int)Slot.WalkPaver];
        /// <summary>A main street's walk is this wide behind the curb (the photos: Tryon's and
        /// Trade's brick runs 5-8 m from the curb to the buildings; the side streets keep WalkW).</summary>
        public const float WalkMainW = 4.2f;
        /// <summary>The walk's width an edge asks for: <see cref="WalkW"/>, or a dressed main street's.</summary>
        static float WalkWOf(CityMap map, CityMap.Edge e) => StreetDressOn && StreetDressOf(map, e) == DressMain ? WalkMainW : WalkW;
        /// <summary>Set while a main street's walk or corner is laid (EmitWalkSpan, EmitCornerWalk).</summary>
        static bool walkPaverNow;

        /// <summary>A street tree a tile's walk stood a grate for (tile-local,
        /// on the slab), for CityTrees to plant.</summary>
        public struct StreetTree
        {
            public Vector3 foot;
            public int edge;
            public sbyte side;
            public byte dress;
            public short station;
        }

        /// <summary>What the last builds dressed (the reference shots print it).</summary>
        public static class DressStats
        {
            public static int stations, grates, noWalk, lane, building, drive, pavement, otherTile, movedOut, bridge, fan;
            public static void Reset() { stations = grates = noWalk = lane = building = drive = pavement = otherTile = movedOut = bridge = fan = 0; }
            public static string Line() =>
                $"street dress: {grates} grates on {stations} stations in walk spans ({movedOut} of them stood further back for a wider road model; none for: no whole walk {noWalk}, no room off the lane {lane}, under or beside a skybridge or a road deck {bridge}, on a junction's fan disc {fan}, building {building}, driveway or apron {drive}, another road {pavement}; {otherTile} another tile's)";
        }

        static CityMap dressMapFor;
        static sbyte[] dressCache;

        /// <summary>0 not an Uptown walk street, <see cref="DressSide"/> a side
        /// street inside the loop, <see cref="DressMain"/> Tryon or Trade
        /// Street inside it.</summary>
        public static int StreetDressOf(CityMap map, CityMap.Edge e)
        {
            if (dressMapFor != map || dressCache == null || dressCache.Length != map.edges.Length)
            {
                dressMapFor = map;
                dressCache = new sbyte[map.edges.Length];
            }
            ref sbyte c = ref dressCache[e.index];
            if (c == 0)
            {
                int k = DressNone;
                if (WalkEdge(map, e) && CityPoles.Uptown(map, e.PointAt(e.length * 0.5f)))
                    k = e.name != null && (e.name.EndsWith("Tryon Street") || e.name.EndsWith("Trade Street")) ? DressMain : DressSide;
                c = (sbyte)(k + 1);
            }
            return c - 1;
        }

        /// <summary>An edge's street-tree stations: s = lo + (k + 0.5) * step
        /// for k in [0, n) - evenly between the junction mouths at its ends
        /// (a node the road only carries on through takes no clearance).
        /// False: not a dressed street, or no room.</summary>
        public static bool StreetTreePlan(CityMap map, Trims trims, CityMap.Edge e, out float lo, out float step, out int n)
        {
            lo = step = 0f; n = 0;
            int kind = StreetDressOn ? StreetDressOf(map, e) : DressNone;
            if (kind == DressNone) return false;
            float a = trims.patch[e.a] ? trims.TrimAt(e, e.a) + TreeCornerClearM : 0f;
            float b = e.length - (trims.patch[e.b] ? trims.TrimAt(e, e.b) + TreeCornerClearM : 0f);
            float run = b - a;
            if (run < 5f) return false;
            float pitch = kind == DressMain ? TreePitchMainM : SidePitchOf(e);   // (the corrections: a side street's own pitch)
            n = Mathf.Max(1, Mathf.RoundToInt(run / pitch));
            step = run / n; lo = a;
            return true;
        }

        /// <summary>How far arc <paramref name="s"/> of an edge is from its
        /// nearest street-tree station (a big number where it has none).</summary>
        public static float StreetTreeGap(CityMap map, Trims trims, CityMap.Edge e, float s)
        {
            if (!StreetTreePlan(map, trims, e, out float lo, out float step, out int n)) return 1e6f;
            int k = Mathf.Clamp(Mathf.FloorToInt((s - lo) / step), 0, n - 1);
            return Mathf.Abs(s - (lo + (k + 0.5f) * step));
        }

        /// <summary>The lamp rule's station on a dressed street: the middle
        /// of the nearest gap between two trees (a block's lamps stand
        /// between its trees, as the photos have them).</summary>
        static float LampBetweenTrees(CityMap map, Trims trims, CityMap.Edge e, float s)
        {
            if (!StreetTreePlan(map, trims, e, out float lo, out float step, out int n) || n < 2) return s;
            int j = Mathf.Clamp(Mathf.RoundToInt((s - lo) / step), 1, n - 1);
            return lo + j * step;
        }

        /// <summary>How far plan point p (beside arc s of its own street) stands off that street's
        /// model, the least over the map line's pieces within 14 m of s - each measured as the tree
        /// audit measures it (the nearest point of the piece, the model there, p's own side) and at
        /// its two ends, so a lane tapering in or a bend of the line beside the tree is seen.</summary>
        static float OffOwnRoad(CityMap.Edge e, Trims trims, float s, Vector2 p)
        {
            float least = float.MaxValue;
            for (int si = 0; si + 1 < e.pts.Length; si++)
            {
                if (e.s[si + 1] < s - 14f || e.s[si] > s + 14f) continue;
                Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                float l2 = d.sqrMagnitude;
                float tq = l2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / l2) : 0f;
                float len = Mathf.Sqrt(l2);
                least = Mathf.Min(least, OffRoadModel(e, trims, e.s[si] + len * tq, p));
                least = Mathf.Min(least, OffRoadModel(e, trims, Mathf.Max(e.s[si], s - 14f), p));
                least = Mathf.Min(least, OffRoadModel(e, trims, Mathf.Min(e.s[si + 1], s + 14f), p));
            }
            for (int q = -2; q <= 2; q++) least = Mathf.Min(least, OffRoadModel(e, trims, s + q * 4f, p));
            return least;
        }

        static readonly Vector3[] dressProf = new Vector3[4];

        /// <summary>A point on the walk's slab (tile-local): at the drawn edge
        /// point <paramref name="ed"/>, <paramref name="d"/> out of it.</summary>
        static Vector3 SlabAt(Vector3 ed, Vector2 outw, float w, float roll, float d)
        {
            WalkProfile(ed, outw, w, dressProf, 1f, roll);
            float nose = Vector2.Distance(new Vector2(dressProf[2].x, dressProf[2].z), new Vector2(ed.x, ed.z));
            return Vector3.Lerp(dressProf[2], dressProf[3], Mathf.Clamp01((d - nose) / Mathf.Max(w - nose, 1e-3f)));
        }

        /// <summary>
        /// The street trees of one whole walk span (EmitWalkSpan, no curb cut
        /// in it): each station of the edge inside [sA, sB) that this tile
        /// owns gets its grate and a <see cref="StreetTree"/>.
        /// </summary>
        static void DressSpan(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, int side,
                              Vector3 eA, Vector3 eB, Vector2 outA, Vector2 outB, float sA, float sB, float wA, float wB)
        {
            if (sB - sA < 0.05f || !StreetTreePlan(map, trims, e, out float lo, out float step, out int n)) return;
            int dress = StreetDressOf(map, e);
            int k0 = Mathf.Max(0, Mathf.CeilToInt((sA - lo) / step - 0.5f)), k1 = Mathf.Min(n - 1, Mathf.CeilToInt((sB - lo) / step - 0.5f) - 1);
            var o = tm.origin;
            float len = Vector2.Distance(new Vector2(eA.x, eA.z), new Vector2(eB.x, eB.z));
            for (int k = k0; k <= k1; k++)
            {
                float s = lo + (k + 0.5f) * step;
                if (s < sA || s >= sB) continue;
                float t = (s - sA) / (sB - sA);
                var ed = Vector3.Lerp(eA, eB, t);
                var outw = Vector2.Lerp(outA, outB, t).normalized;
                var edW = new Vector2(ed.x + o.x, ed.z + o.z);
                // how far out: TreeOutM, and further where the road's model is wider than its drawn edge here
                // (a section's way out is not always square to the map line: step out until it is clear)
                float dTree = TreeOutM;
                var footW = edW + outw * dTree;
                for (int it = 0; it < 5; it++)
                {
                    float own = OffOwnRoad(e, trims, s, footW);
                    // ...and the next piece of the street past a node this near (its model may be wider)
                    for (int end = 0; end < 2; end++)
                    {
                        int nd = end == 0 ? e.a : e.b;
                        if ((end == 0 ? s : e.length - s) > 14f) continue;
                        foreach (int oi in map.nodeEdges[nd])
                            if (oi != e.index && !map.edges[oi].tunnel)
                                own = Mathf.Min(own, OffOwnRoad(map.edges[oi], trims, map.edges[oi].a == nd ? 0f : map.edges[oi].length, footW));
                    }
                    if (own >= TreeLaneClearM) break;
                    dTree += (TreeLaneClearM - own) * 1.05f + 0.01f;
                    footW = edW + outw * dTree;
                }
                // the tile that holds the trunk owns the tree
                if (footW.x < o.x || footW.x >= o.x + TileSize || footW.y < o.z || footW.y >= o.z + TileSize) { DressStats.otherTile++; continue; }
                DressStats.stations++;
                float w = Mathf.Lerp(wA, wB, t);
                if (wA < TreeMinWalkM || wB < TreeMinWalkM || len < 0.5f) { DressStats.noWalk++; continue; }
                if (dTree > Mathf.Min(wA, wB) - 0.7f) { DressStats.lane++; continue; }
                // no crown up through a skybridge's floor or a road deck, no trunk on a junction's fan
                if (UnderSkybridge(map, footW, TreeBridgeClearM) || NearDeck(map, trims, footW, TreeBridgeClearM)) { DressStats.bridge++; continue; }
                if (InFanDisc(map, trims, footW, 0.3f)) { DressStats.fan++; continue; }
                if (dTree > TreeOutM + 0.01f) DressStats.movedOut++;
                // clear of every wall, of a driveway or a lot's apron a car turns over, and of any
                // other road's pavement or junction fan (the trunk and the grate's two ends)
                if (!map.FootprintClear(footW, 0.9f) || LampInLot(footW)) { DressStats.building++; continue; }
                var along = new Vector2(-outw.y, outw.x);
                bool drive = OnDriveway(footW, 1.2f);
                for (int q = -1; q <= 1 && !drive; q++) { var c = footW + along * (q * 1.4f); drive = ApronAt(map, c.x, c.y); }
                if (drive) { DressStats.drive++; continue; }
                bool paved = false;
                for (int q = -1; q <= 1 && !paved; q++) paved = OnPavement(map, trims, footW + along * (q * 1.0f), ed.y + CurbH, e.index, e.index);
                if (paved) { DressStats.pavement++; continue; }

                // THE GRATE: a dark slab 3 cm proud of the walk, in the walk's own mesh
                float half = Mathf.Min(GrateAlongM * 0.5f, 0.45f * len);
                float t0 = Mathf.Clamp01(t - half / len), t1 = Mathf.Clamp01(t + half / len);
                float r0 = CurbWingsOn ? WalkBackEndEase(map, e, Mathf.Lerp(sA, sB, t0)) : 0f, r1 = CurbWingsOn ? WalkBackEndEase(map, e, Mathf.Lerp(sA, sB, t1)) : 0f;
                Vector3 e0 = Vector3.Lerp(eA, eB, t0), e1 = Vector3.Lerp(eA, eB, t1);
                Vector2 o0 = Vector2.Lerp(outA, outB, t0).normalized, o1 = Vector2.Lerp(outA, outB, t1).normalized;
                float w0 = Mathf.Lerp(wA, wB, t0), w1 = Mathf.Lerp(wA, wB, t1);
                float dIn = Mathf.Max(WalkRollNoseOut + GrateInM, dTree - GrateAcrossM * 0.5f), dOut = Mathf.Min(dIn + GrateAcrossM, Mathf.Min(w0, w1) - 0.3f);
                Vector3 a = SlabAt(e0, o0, w0, r0, dIn), b = SlabAt(e0, o0, w0, r0, dOut), c1 = SlabAt(e1, o1, w1, r1, dOut), d1 = SlabAt(e1, o1, w1, r1, dIn);
                var up = Vector3.up * GrateLiftM;
                var bk = WalkBucket;
                var uv = new Vector2(CurbFaceU, s / WalkTexM);
                bk.Up(a + up, b + up, c1 + up, d1 + up, uv, uv, uv, uv);
                var o03 = new Vector3(o0.x, 0f, o0.y); var al3 = new Vector3(along.x, 0f, along.y);
                bk.Face(a, a + up, d1 + up, d1, -o03, uv, uv, uv, uv);       // toward the curb
                bk.Face(b, b + up, c1 + up, c1, o03, uv, uv, uv, uv);        // toward the buildings
                bk.Face(a, a + up, b + up, b, e0 - e1, uv, uv, uv, uv);      // the two ends
                bk.Face(d1, d1 + up, c1 + up, c1, e1 - e0, uv, uv, uv, uv);
                var foot = SlabAt(ed, outw, w, CurbWingsOn ? WalkBackEndEase(map, e, s) : 0f, dTree);
                foot.y += GrateLiftM;
                tm.streetTrees.Add(new StreetTree { foot = foot, edge = e.index, side = (sbyte)side, dress = (byte)dress, station = (short)k });
                DressStats.grates++;
            }
        }

        // ------------------------------------------------------------------
        //  THE SKYBRIDGES. A table of places, each row OpenStreetMap's own
        //  outline of the walkway (man_made=bridge, way id in the row): its
        //  centre and long axis in the game's plan (x east, z north, metres),
        //  its length between the two facades and its width. Clear is the
        //  soffit's height over the street under its middle, tall the box's
        //  own height (the photos: a storey of glass between two bands, the
        //  soffit at second-floor height). since is the year it was built
        //  where known (0: before the 1990s): PSX_CITY_SKYBRIDGE_ERA=1999
        //  keeps only the walkways a 1999 driver saw (default: all - the
        //  city's buildings are the present day's, and a bridge joins two
        //  of them). No names: a row is its street.
        // ------------------------------------------------------------------
        public struct Skybridge
        {
            public string id, street; public uint way;
            public Vector2 c, u; public float len, wide, clear, tall; public int since;
            public Color32 glass;
            public Skybridge(string id, uint way, string street, float cx, float cz, float ux, float uz, float len, float wide, float clear, float tall, int since, Color32 glass)
            { this.id = id; this.way = way; this.street = street; c = new Vector2(cx, cz); u = new Vector2(ux, uz).normalized; this.len = len; this.wide = wide; this.clear = clear; this.tall = tall; this.since = since; this.glass = glass; }
        }

        static readonly Color32 SkyDark = new Color32(70, 84, 92, 255), SkyGrey = new Color32(120, 132, 138, 255), SkyBronze = new Color32(96, 84, 72, 255);

        public static readonly Skybridge[] Skybridges =
        {
            new Skybridge("e3rd_tryon_college", 1190021133u, "East 3rd Street between South Tryon and South College", -2454.01f, 4507.05f, 0.7431f, 0.6692f, 24.43f, 4.11f, 5.6f, 4.4f, 0, SkyDark),
            new Skybridge("e4th_tryon_college", 1188214517u, "East 4th Street between South Tryon and South College", -2333.38f, 4571.31f, 0.7653f, 0.6437f, 17.00f, 4.26f, 5.6f, 4.0f, 0, SkyGrey),
            new Skybridge("scollege_3rd_4th", 1188479467u, "South College Street between East 3rd and East 4th", -2365.19f, 4495.99f, 0.6431f, -0.7658f, 28.76f, 4.64f, 6.0f, 4.2f, 0, SkyDark),
            new Skybridge("scollege_mlk_3rd", 1190021136u, "South College Street between East MLK and East 3rd", -2464.57f, 4418.72f, -0.4295f, 0.9031f, 37.40f, 5.78f, 5.8f, 4.2f, 0, SkyGrey),
            new Skybridge("etrade_tryon_college", 1188214515u, "East Trade Street between Tryon and College", -2253.85f, 4678.75f, 0.9003f, 0.4352f, 33.00f, 3.44f, 5.8f, 4.2f, 1992, SkyBronze),
            new Skybridge("w4th_tryon_church", 1188214505u, "West 4th Street between South Tryon and South Church", -2465.47f, 4705.74f, 0.9974f, -0.0717f, 33.12f, 3.61f, 6.0f, 4.0f, 0, SkyDark),
            new Skybridge("emlk_tryon_college", 1190021132u, "East MLK Boulevard between South Tryon and South College", -2557.47f, 4431.24f, 0.7931f, 0.6091f, 15.98f, 3.61f, 5.6f, 4.4f, 2000, SkyGrey),
            new Skybridge("e5th_tryon_college", 1188214514u, "East 5th Street between North Tryon and North College", -2133.46f, 4754.26f, 0.9590f, 0.2833f, 20.39f, 8.03f, 5.8f, 4.4f, 2002, SkyBronze),
            new Skybridge("e5th_college_brevard", 1186586212u, "East 5th Street east of North College", -2037.40f, 4649.78f, 0.7922f, 0.6102f, 24.19f, 4.21f, 5.6f, 4.0f, 0, SkyGrey),
            new Skybridge("nchurch_6th", 1187727093u, "North Church Street by West 6th", -2155.50f, 5057.16f, 0.6286f, -0.7777f, 21.44f, 4.89f, 5.6f, 4.0f, 0, SkyGrey),
            new Skybridge("sbrevard_stonewall", 1190021180u, "South Brevard Street south of East MLK", -2432.52f, 4106.27f, 0.6432f, -0.7657f, 25.98f, 12.15f, 6.0f, 4.6f, 2010, SkyGrey),
        };

        /// <summary>PSX_CITY_SKYBRIDGE_ERA=year: only the walkways built by then (0 or unset: all).</summary>
        static readonly int SkybridgeEra = int.TryParse(System.Environment.GetEnvironmentVariable("PSX_CITY_SKYBRIDGE_ERA"), out int y) ? y : 0;
        /// <summary>A bridge runs this far into each building past the mapped facade (a footprint drawn cut back from a road still meets it).</summary>
        const float SkybridgeIntoM = 2.5f;

        /// <summary>The non-hidden footprint (an outline or a part) standing
        /// over plan point p from <paramref name="y0"/> to <paramref name="y1"/>
        /// above its ground, or -1.</summary>
        static int FootprintOver(CityMap map, Vector2 p, float y0, float y1)
        {
            int tx = Mathf.FloorToInt(p.x / TileSize), tz = Mathf.FloorToInt(p.y / TileSize);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var list = map.FootprintsInTile(tx + dx, tz + dz);
                    if (list == null) continue;
                    foreach (int fi in list)
                    {
                        var f = map.footprints[fi];
                        if (f.hidden || f.replaced || f.propKind != 0) continue;
                        if (f.minH > y0 + 0.75f || f.h < y1 - 0.75f) continue;
                        if (SkyInPoly(f.pts, p)) return fi;
                    }
                }
            return -1;
        }

        static bool SkyInPoly(Vector2[] ring, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                Vector2 a = ring[i], b = ring[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        /// <summary>Can this bridge be built: both its ends inside a building
        /// the game draws at its height? <paramref name="why"/> says what is
        /// missing (the hand-back's list).</summary>
        public static bool SkybridgeCheck(CityMap map, in Skybridge b, out string why)
        {
            why = "";
            if (!SkybridgesOn) { why = "skybridges off"; return false; }
            if (SkybridgeEra > 0 && b.since > SkybridgeEra) { why = $"built {b.since}, after the era switch {SkybridgeEra}"; return false; }
            for (int s = -1; s <= 1; s += 2)
            {
                var end = b.c + b.u * (s * (b.len * 0.5f + SkybridgeIntoM - 0.5f));
                int fi = FootprintOver(map, end, b.clear, b.clear + b.tall);
                if (fi < 0)
                {
                    // say what IS there
                    int any = FootprintOver(map, end, 1e6f, -1e6f);
                    why = any < 0 ? $"no drawn building at its {(s < 0 ? "first" : "second")} end ({end.x:0},{end.y:0})"
                                  : $"the building at its {(s < 0 ? "first" : "second")} end ({end.x:0},{end.y:0}) stands {map.footprints[any].minH:0.#}-{map.footprints[any].h:0.#} m, the bridge {b.clear:0.#}-{b.clear + b.tall:0.#} m";
                    return false;
                }
            }
            return true;
        }

        /// <summary>One line a bridge: built, or left out and why.</summary>
        public static string SkybridgeReport(CityMap map)
        {
            var sb = new System.Text.StringBuilder();
            int built = 0;
            foreach (var b in Skybridges)
            {
                bool ok = SkybridgeCheck(map, b, out string why);
                if (ok) built++;
                sb.Append($"  {b.id} (way {b.way}, {b.street}{(b.since > 0 ? ", " + b.since : "")}): {(ok ? "BUILT" : "LEFT OUT - " + why)}\n");
            }
            return $"skybridges: {built} of {Skybridges.Length} built\n" + sb;
        }

        /// <summary>A street tree stands this far clear of a skybridge's side (its crown is 3-4 m in radius).</summary>
        const float TreeBridgeClearM = 4.5f;
        static CityMap skyOkFor;
        static bool[] skyOk;

        /// <summary>Is plan point p under a skybridge the game builds, or within <paramref name="pad"/> of its side?</summary>
        static bool UnderSkybridge(CityMap map, Vector2 p, float pad)
        {
            if (!SkybridgesOn) return false;
            if (skyOkFor != map || skyOk == null)
            {
                skyOk = new bool[Skybridges.Length];
                for (int i = 0; i < Skybridges.Length; i++) skyOk[i] = SkybridgeCheck(map, Skybridges[i], out _);
                skyOkFor = map;
            }
            for (int i = 0; i < Skybridges.Length; i++)
            {
                if (!skyOk[i]) continue;
                var b = Skybridges[i];
                var h = b.u * (b.len * 0.5f + SkybridgeIntoM);
                if (RoadsideOccupancy.DistToSeg(p, b.c - h, b.c + h) < b.wide * 0.5f + pad) return true;
            }
            return false;
        }

        static readonly HashSet<int> dressSegs = new HashSet<int>();

        /// <summary>Is plan point p under a road deck (a bridge, an elevated stretch) or within
        /// <paramref name="pad"/> of its side, as the tree audit measures a deck (the map line and
        /// the wider half width)?</summary>
        static bool NearDeck(CityMap map, Trims trims, Vector2 p, float pad)
        {
            dressSegs.Clear();
            map.EdgeSegsInRect(p - Vector2.one * 30f, p + Vector2.one * 30f, dressSegs);
            foreach (int packed in dressSegs)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var o = map.edges[ei];
                if (o.tunnel) continue;
                Vector2 a = o.pts[si], d = o.pts[si + 1] - a;
                float l2 = d.sqrMagnitude;
                float tq = l2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / l2) : 0f;
                float at = o.s[si] + Mathf.Sqrt(l2) * tq;
                if (!o.bridge && !o.ElevatedAt(at)) continue;
                RoadsideOccupancy.RoadEdgeAt(o, trims, at, out _, out _, out float hl, out float hr);
                if (Vector2.Distance(p, a + d * tq) - Mathf.Max(hl, hr) < RoadsideOccupancy.DeckMarginM + pad) return true;
            }
            return false;
        }

        /// <summary>Is plan point p inside a junction's fan disc (the tree audit's measure: the
        /// node, out to the farthest drawn corner of any arm's mouth), or within <paramref name="pad"/> of it?</summary>
        static bool InFanDisc(CityMap map, Trims trims, Vector2 p, float pad)
        {
            dressSegs.Clear();
            map.EdgeSegsInRect(p - Vector2.one * 40f, p + Vector2.one * 40f, dressSegs);
            foreach (int packed in dressSegs)
            {
                var o = map.edges[packed >> 12];
                for (int end = 0; end < 2; end++)
                {
                    int nd = end == 0 ? o.a : o.b;
                    if (!trims.patch[nd]) continue;
                    float reach = 0f;
                    foreach (int oi in map.nodeEdges[nd])
                    {
                        var arm = map.edges[oi];
                        float tr = trims.TrimAt(arm, nd), hw = arm.width * 0.5f;
                        reach = Mathf.Max(reach, Mathf.Sqrt(tr * tr + hw * hw));
                    }
                    if (Vector2.Distance(map.nodes[nd], p) < reach + pad) return true;
                }
            }
            return false;
        }

        static readonly List<Vector2> skyPoly = new List<Vector2>(4), skyBand = new List<Vector2>(4);

        static void SkyRect(List<Vector2> poly, Vector2 c, Vector2 u, float hu, float hv)
        {
            var v = new Vector2(-u.y, u.x);
            poly.Clear();
            poly.Add(c + u * hu + v * hv); poly.Add(c - u * hu + v * hv); poly.Add(c - u * hu - v * hv); poly.Add(c + u * hu - v * hv);
        }

        /// <summary>The skybridges whose middle stands on this tile, into the
        /// tile's building mesh: a storey of curtain wall between a floor band
        /// and a roof band (the facade atlas's metal), a flat roof and a soffit.</summary>
        static void EmitSkybridges(CityMap map, TileMeshes tm, int tx, int tz)
        {
            if (!SkybridgesOn || skylineBuild) return;
            foreach (var b in Skybridges)
            {
                if (Mathf.FloorToInt(b.c.x / TileSize) != tx || Mathf.FloorToInt(b.c.y / TileSize) != tz) continue;
                if (!SkybridgeCheck(map, b, out _)) continue;
                // level, over the highest ground under it
                float g = CityElevation.GroundY(map, b.c.x, b.c.y);
                for (int s = -1; s <= 1; s += 2)
                {
                    var q = b.c + b.u * (s * b.len * 0.5f);
                    g = Mathf.Max(g, CityElevation.GroundY(map, q.x, q.y));
                }
                float y0 = g + b.clear, y1 = y0 + b.tall;
                // the corrections (CityMeshes.StreetFurniture.cs): a walkway that is its own - sloped, banded, glass-roofed
                if (EmitSkybridgeLook(tm, b, y0, y1)) continue;
                float hu = b.len * 0.5f + SkybridgeIntoM, hv = b.wide * 0.5f;
                const float band = 0.75f, proud = 0.12f;
                SkyRect(skyPoly, b.c, b.u, hu, hv);
                SkyRect(skyBand, b.c, b.u, hu, hv + proud);
                PickFacade(b.c, 0, 1, 1, b.glass, y0 + band, b.tall - 2f * band);
                Walls(tm, skyPoly, y0 + band, y1 - band);
                FacadeMetal(true);
                Walls(tm, skyBand, y0, y0 + band);
                Walls(tm, skyBand, y1 - band, y1);
                FacadeMetal(false);
                EndFacade();
                EarcutInto(buckets[(int)Slot.RoofFlat], skyBand, y1, tm.origin, RoofFlatM);
                EmitSoffit(tm, skyBand, y0);
            }
        }
    }
}
