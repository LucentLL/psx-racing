using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// CHARLOTTE'S TREES (plan WP-08, release R3). The owner, after driving
    /// it: "its all so barren and flat". Charlotte is one of the most wooded
    /// big cities in the country, and the city had not one tree.
    ///
    /// WHERE AND HOW MANY come from the present-day canopy map
    /// (<see cref="CityCanopy"/>, USDA Forest Service NLCD Tree Canopy Cover
    /// 2024), at REAL density (the owner, Q2): each 16 m square of a tile
    /// plants canopy x area / <see cref="CrownM2"/> trees - a crown of about
    /// 95 m2, so a fully wooded 60 m cell holds about 38 and a typical
    /// Charlotte tile 150-300. Phones plant <see cref="PhoneDensity"/> of
    /// that (Q2's lower tier). A tree only stands where the tile's
    /// <see cref="RoadsideOccupancy"/> mask is empty: never on pavement, in a
    /// clear zone, a sight triangle or a reserved corner, in a building or
    /// water, or under a deck. What the squares cannot plant (they are road)
    /// is planted in a second pass on free ground within
    /// <see cref="BesideRoadM"/> of a carriageway: the canopy the map shows
    /// over a street is the crowns of the trees beside it.
    ///
    /// Deterministic from the graph and the tile alone: every choice is a
    /// hash of the square's global index, and a square belongs to exactly
    /// one tile, so the same trees stand on every build and none is planted
    /// twice across a seam. Placed at runtime from the live graph, so when
    /// the lines release moves a road's edges the trees move with them.
    ///
    /// WHAT from the context: willow-oak rows in the old city (inside 4.5 km
    /// of uptown), 45% pine in the suburbs past it, crape myrtles along the
    /// commercial arterials, sycamores by the creeks. They wear the stage
    /// forest's sixteen-cell billboard atlas (the owner's CC0 retro tree
    /// pack, already shipped: 0 MB), and the dress - winter, spring, summer,
    /// fall, snow - is the atlas the city kit hands out for the calendar day.
    ///
    /// DRAWN as two crossed cards a tree (8 vertices), one merged mesh a tile
    /// on the Foliage layer: one draw, and one sun-map caster. A crown may
    /// overhang a road (Myers Park's arches), but nothing a card paints below
    /// <see cref="OverhangClearM"/> over that road may reach over its
    /// pavement: measured per billboard off the atlases (the city kit's
    /// low-reach table), the cards turned to 45 degrees to the road, the tree
    /// grown, shrunk, given another billboard or stood back until it fits. A
    /// third of the billboards paint leaves to the ground.
    ///
    /// Along every grounded freeway, where the map has woods, two rows of
    /// hardwoods just past the clear zone: the tree walls.
    ///
    /// SOLID within <see cref="TrunkReachM"/> of a carriageway, and only a
    /// trunk of <see cref="SolidTrunkM"/> or more (the owner kept the plan's
    /// Q15 default: trunks of 30 cm and up are solid, small trees break away).
    /// A crape myrtle, a dead snag or a young understory tree is BREAKAWAY: no
    /// collider, the car goes through it. The solid ones are handed to the
    /// city's <see cref="TreeTrunks"/> table (CityWorld), which stands a
    /// capsule only round the cars, on objects named <see cref="TrunkName"/>.
    /// Past TrunkReachM no car can reach one.
    ///
    /// NOT IN RACE RUN-OFF: the mask keeps the city routes' run-off clear
    /// (<see cref="RaceRunOff"/>).
    /// </summary>
    public static class CityTrees
    {
        /// <summary>Tools switch the trees off for an A/B (the budget probe).</summary>
        public static bool Enabled = true;
        /// <summary>Tools set a density tier (1 desktop, <see cref="PhoneDensity"/>
        /// phone); null takes the device's.</summary>
        public static float? DensityOverride;
        public const float PhoneDensity = 0.6f;
        public static float Density => DensityOverride ?? (Application.isMobilePlatform ? PhoneDensity : 1f);

        /// <summary>The mean crown a tree stands for, m2 (the plan's 80-110).</summary>
        public const float CrownM2 = 95f;
        /// <summary>At most this many trees a tile (times the density).</summary>
        public const int MaxPerTile = 400;
        public const float SquareM = 16f;
        const int Squares = 16;   // TileSize / SquareM
        /// <summary>The second pass plants what road squares could not this
        /// close to a carriageway's edge.</summary>
        public const float BesideRoadM = 14f;
        /// <summary>Set while a freeway wall tree is being planted.</summary>
        static bool wallNow;
        /// <summary>Freeway tree walls: a tree every this many metres, the
        /// first row this far past the clear zone (plus up to the jitter), a
        /// second row this far behind it where the canopy is twice the least
        /// a wall needs.</summary>
        const float LineStepM = 7f, LineFirstM = 1.6f, LineRowM = 5.5f, LineJitterM = 2f, LineCanopyMin = 0.2f;
        /// <summary>No two trunks of a tile closer than this: about a crown's
        /// radius, so crowns overlap as little as the canopy lets them (random
        /// points piled half the crowns of a 30% canopy onto each other).</summary>
        public const float MinSpacingM = 6f;
        /// <summary>How far off a road's drawn edge a card's leaves below
        /// OverhangClearM stay when it is planted (the audit holds them to
        /// 0.4 m: this is that plus the difference between the piece a tree
        /// is fitted against and the exact line the audit reads).</summary>
        const float LeafMarginM = 0.65f;
        /// <summary>A trunk this close to a grounded carriageway's edge gets a
        /// collider.</summary>
        public const float TrunkReachM = 25f;
        /// <summary>Q15 (plan default, kept by the owner): a trunk this thick
        /// at chest height or more is solid; a smaller tree breaks away.</summary>
        public const float SolidTrunkM = 0.30f;
        public const string TrunkName = "TreeTrunk";
        /// <summary>Solid up to here (a car's roof and then some; TreeTrunks').</summary>
        public const float TrunkHeightM = 4f;
        /// <summary>The base sits this far into the ground, so a sloped
        /// lattice never shows daylight under a card.</summary>
        public const float SinkM = 0.3f;
        /// <summary>What a card paints below this height over a road must not
        /// reach over its pavement.</summary>
        public const float OverhangClearM = 4.2f;
        public const int FoliageLayer = 10;

        public enum Species : byte { Oak, Hardwood, Pine, Myrtle, Sycamore, Bare }
        static readonly byte[][] Cells =
        {
            new byte[] { 8, 9, 10, 11 },                 // willow oak: the atlas's oaks and greens
            new byte[] { 0, 1, 2, 4, 6, 8, 9, 10, 11 },  // mixed hardwood woods
            new byte[] { 12, 13, 14 },                   // pine (the atlas's conifers)
            new byte[] { 3, 5, 7 },                      // crape myrtle: the small reds
            new byte[] { 0, 4, 6 },                      // sycamore: the big golds
            new byte[] { 15 },                           // a dead or bare one
        };
        static readonly Vector2[] Heights =
        {
            // broadleaf cards are square and paint a crown 0.9 of their width:
            // 11.5-16 m is a crown of about 118 m2. The count is the plan's
            // (canopy x area / CrownM2, 95 m2); crowns planted at random overlap,
            // and at 95 m2 the canopy they drew within 25 m of the roads came
            // out 5-7 points under the map (tree audit). Charlotte's willow
            // oaks are 15-20 m trees.
            new Vector2(11.5f, 16f), new Vector2(11f, 16.5f), new Vector2(15f, 22f),
            new Vector2(5f, 7.5f), new Vector2(13f, 17.5f), new Vector2(9f, 12f),
        };
        static bool Conifer(Species s) => s == Species.Pine;
        static float CardWidth(Species s, float h) => h * (Conifer(s) ? 0.62f : 1f);

        /// <summary>
        /// The trunk's diameter at chest height, metres, from the tree's height
        /// (Q15 decides solid or breakaway on it). An open-grown street or yard
        /// hardwood thickens about 4 cm a metre of height (a 12 m willow oak
        /// is about 33 cm, a 16 m one 49 cm); a pine grown in a stand about
        /// 2.5 cm (15 m: 26 cm, 20 m: 38 cm). A crape myrtle is a clump of
        /// stems a hand or two thick, a young understory tree a sapling, and a
        /// dead snag's wood is rotten: all three break away, whatever their
        /// height.
        /// </summary>
        public static float TrunkDiameterM(Species s, float h, bool young)
        {
            if (young || s == Species.Myrtle) return 0.12f;
            if (s == Species.Bare) return 0f;
            return Mathf.Max(0.05f, Conifer(s) ? 0.025f * h - 0.12f : 0.04f * h - 0.15f);
        }

        /// <summary>Levels of the kit's low-reach table (twentieths, and 0).</summary>
        public const int ReachLevels = 21;
        /// <summary>Heights a tree too close to a road is tried at, as
        /// multiples of the one it drew: taller first (its foliage then starts
        /// higher), then shorter (a narrower card).</summary>
        static readonly float[] FitScales = { 1f, 1.15f, 1.3f, 0.85f, 0.7f };

        /// <summary>How far, as a fraction of the card's width, the tree a
        /// cell paints reaches out from its trunk below <paramref name="frac"/>
        /// of its height, in the widest of the five dresses (the city kit
        /// measured it off the atlases). Half the card without a kit.</summary>
        public static float LowReach(int cell, float frac)
        {
            if (frac <= 0f) return 0f;
            var kit = CityKit.Get();
            return LowReach(kit != null ? kit.treeLowReach : null, cell, frac);
        }

        /// <summary>As above off a table in hand (the planting reads the kit's
        /// once a tile, not once a road a billboard a size).</summary>
        static float LowReach(float[] t, int cell, float frac)
        {
            if (frac <= 0f) return 0f;
            if (t == null || t.Length < 16 * ReachLevels) return 0.5f;
            int L = Mathf.Clamp(Mathf.CeilToInt(frac * (ReachLevels - 1)), 0, ReachLevels - 1);
            return t[cell * ReachLevels + L];
        }
        static float[] reachTable;

        /// <summary>The crown a tree draws, as a radius in plan: 0.45 of a
        /// broadleaf card's width (the atlas paints its crown 0.9 of the cell
        /// at the widest), 0.275 of a conifer's (0.55). The audit measures the
        /// canopy the trees make with this.</summary>
        public static float CrownRadius(Tree t) => t.w * (Conifer(t.species) ? 0.275f : 0.45f);

        public struct Tree
        {
            public Vector3 foot;      // world, the base (sunk)
            public float h, w;        // card height and width
            public float r;           // trunk radius
            public float yawDeg;
            public byte cell;
            public Species species;
            /// <summary>Metres to the nearest grounded carriageway's edge.</summary>
            public float road;
            /// <summary>Trunk diameter at chest height (<see cref="TrunkDiameterM"/>).</summary>
            public float dbh;
            /// <summary>Stands a collider: within reach of a road, and a trunk
            /// of <see cref="SolidTrunkM"/> or more (Q15).</summary>
            public bool solid;
        }

        public const int RejectOverhang = 8, RejectSpacing = 9, RejectCount = 10;
        public static readonly string[] RejectNames =
        {
            "pavement", "clear zone", "sight triangle", "corner spot", "building", "water", "under a deck", "lot, lamp or sign",
            "card over the road too low", "too close to another tree",
        };

        public class TreeTile
        {
            public int tx, tz;
            public readonly List<Tree> trees = new List<Tree>();
            public Mesh mesh;
            public RoadsideOccupancy occ;
            /// <summary>The tile's signs (WP-23), placed on the mask before the
            /// trees; null when the signs are off or have no data.</summary>
            public CitySigns.SignTile signs;
            /// <summary>What the canopy asks this tile for (density applied,
            /// before the per-tile cap), and what was planted.</summary>
            public float wanted;
            public int solids, grown, shrunk, lined;
            /// <summary>Trees within reach of a road that break away (Q15).</summary>
            public int breakaway;
            /// <summary>The solid trunks as the city's TreeTrunks table wants
            /// them: base (x, y, z) and radius.</summary>
            public List<Vector4> Trunks()
            {
                var l = new List<Vector4>(solids);
                foreach (var t in trees) if (t.solid) l.Add(new Vector4(t.foot.x, t.foot.y, t.foot.z, t.r));
                return l;
            }
            public readonly int[] rejects = new int[RejectCount];
            /// <summary>Milliseconds: the whole plant, and the mask's share of it.</summary>
            public float ms, occMs;
        }

        // ------------------------------------------------------------------

        static readonly List<Vector3> vScratch = new List<Vector3>(4096), nScratch = new List<Vector3>(4096);
        static readonly List<Vector2> uvScratch = new List<Vector2>(4096);
        static readonly List<int> tScratch = new List<int>(6144);
        static readonly float[] squareCanopy = new float[Squares * Squares];
        static readonly int[] squareQuota = new int[Squares * Squares], squarePlaced = new int[Squares * Squares];
        static readonly bool[] taken = new bool[RoadsideOccupancy.Res * RoadsideOccupancy.Res];

        /// <summary>Plant one tile. <paramref name="tm"/> is the tile's own
        /// CityMeshes build (its fill houses and lamps; the ground lattice it
        /// cached is what the trees stand on).</summary>
        public static TreeTile Build(CityMap map, CityMeshes.Trims trims,
            Dictionary<long, List<CityBuildings.B>> buildings, CityMeshes.TileMeshes tm, int tx, int tz)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var tt = new TreeTile { tx = tx, tz = tz };
            var min = new Vector2(tx * CityMeshes.TileSize, tz * CityMeshes.TileSize);
            float density = Density;
            bool signs = CitySigns.Enabled && CitySignData.Loaded;
            bool trees = Enabled && CityCanopy.Loaded;

            // what the canopy asks for, square by square, and the tile's cap
            float wanted = 0f;
            if (trees)
                for (int z = 0; z < Squares; z++)
                    for (int x = 0; x < Squares; x++)
                    {
                        float c = CityCanopy.CellFraction(min.x + (x + 0.5f) * SquareM, min.y + (z + 0.5f) * SquareM);
                        squareCanopy[z * Squares + x] = c;
                        wanted += c * SquareM * SquareM / CrownM2 * density;
                    }
            tt.wanted = wanted;
            trees &= wanted >= 0.5f;
            if (!trees && !signs) { tt.ms = (float)clock.Elapsed.TotalMilliseconds; return tt; }
            float cap = MaxPerTile * density;
            float scale = wanted > cap ? cap / wanted : 1f;

            var kit = CityKit.Get();
            reachTable = kit != null ? kit.treeLowReach : null;
            var occ = RoadsideOccupancy.Build(map, trims, buildings, tm, tx, tz);
            tt.occ = occ;
            tt.occMs = (float)clock.Elapsed.TotalMilliseconds;
            // THE SIGNS FIRST (WP-23; the plan's priority: signs before trees):
            // they take their ground on the mask, and the trees keep off it
            if (signs) tt.signs = CitySigns.Build(map, trims, occ, tx, tz);
            if (!trees) { tt.ms = (float)clock.Elapsed.TotalMilliseconds; return tt; }
            System.Array.Clear(taken, 0, taken.Length);
            float distUp = Vector2.Distance(min + Vector2.one * (CityMeshes.TileSize * 0.5f), map.uptown);

            // every square's share
            for (int z = 0; z < Squares; z++)
                for (int x = 0; x < Squares; x++)
                {
                    int gx = tx * Squares + x, gz = tz * Squares + z;
                    float want = squareCanopy[z * Squares + x] * SquareM * SquareM / CrownM2 * density * scale;
                    int n = Mathf.FloorToInt(want);
                    if (Hash01(gx, gz, 1) < want - n) n++;
                    squareQuota[z * Squares + x] = n;
                    squarePlaced[z * Squares + x] = 0;
                }

            // THE FREEWAY TREE WALLS (plan: tree lines 15-25 m off freeways,
            // where the canopy map has woods): rows along every grounded
            // motorway carriageway just past its clear zone, a tree every
            // LineStepM, planted out of the share of the square each stands in
            // - so they decide where a wooded square's trees stand, never how
            // many.
            foreach (var pc in occ.pieces)
            {
                if (pc.deck) continue;
                var e = map.edges[pc.edge];
                if (e.link || e.cls < 5) continue;
                var d = pc.b - pc.a;
                float L = d.magnitude;
                if (L < 0.1f) continue;
                d /= L;
                var nrm = new Vector2(-d.y, d.x);
                for (float s = Mathf.Ceil(pc.sa / LineStepM) * LineStepM; s < pc.sb; s += LineStepM)
                {
                    int st = Mathf.RoundToInt(s / LineStepM);
                    var c = pc.a + d * (s - pc.sa);
                    for (int side = -1; side <= 1; side += 2)
                        for (int row = 0; row < 2; row++)
                        {
                            float off = pc.hw + pc.clear + LineFirstM + row * LineRowM + Hash01(pc.edge, st, 700 + side * 3 + row) * LineJitterM;
                            var p = c + nrm * (side * off) + d * ((Hash01(pc.edge, st, 710 + side * 3 + row) - 0.5f) * LineStepM * 0.6f);
                            if (p.x < min.x || p.y < min.y || p.x >= min.x + CityMeshes.TileSize || p.y >= min.y + CityMeshes.TileSize) continue;
                            int x = Mathf.FloorToInt((p.x - min.x) / SquareM), z = Mathf.FloorToInt((p.y - min.y) / SquareM);
                            int i = z * Squares + x;
                            float canopy = squareCanopy[i];
                            if (canopy < (row == 0 ? LineCanopyMin : LineCanopyMin * 2f) || squarePlaced[i] >= squareQuota[i]) continue;
                            wallNow = true;
                            bool ok = TryPlant(map, occ, tt, p, pc.edge, st, 800 + side * 3 + row, canopy, distUp, -1f);
                            wallNow = false;
                            if (ok) { squarePlaced[i]++; tt.lined++; }
                        }
                }
            }

            // PASS 1: every square plants the rest of its share on its free ground
            float pool = 0f;
            for (int z = 0; z < Squares; z++)
                for (int x = 0; x < Squares; x++)
                {
                    int gx = tx * Squares + x, gz = tz * Squares + z;
                    float canopy = squareCanopy[z * Squares + x];
                    int n = squareQuota[z * Squares + x] - squarePlaced[z * Squares + x];
                    if (n <= 0) continue;
                    int placed = 0;
                    var sq = min + new Vector2(x * SquareM, z * SquareM);
                    for (int k = 0; k < n * 6 + 6 && placed < n; k++)
                    {
                        var p = sq + new Vector2(Hash01(gx, gz, 10 + 2 * k), Hash01(gx, gz, 11 + 2 * k)) * SquareM;
                        if (TryPlant(map, occ, tt, p, gx, gz, k, canopy, distUp, -1f)) placed++;
                    }
                    pool += n - placed;
                }

            // PASS 2: what the road squares could not plant stands beside the
            // roads, square by square in a scrambled order (97 is prime to 256)
            for (int visit = 0; visit < Squares * Squares * 2 && pool >= 1f; visit++)
            {
                int i = (visit * 97 + 13) % (Squares * Squares), x = i % Squares, z = i / Squares;
                int gx = tx * Squares + x, gz = tz * Squares + z;
                float canopy = squareCanopy[i];
                var sq = min + new Vector2(x * SquareM, z * SquareM);
                int salt = visit < Squares * Squares ? 1000 : 2000;
                for (int k = 0; k < 3 && pool >= 1f; k++)
                {
                    var p = sq + new Vector2(Hash01(gx, gz, salt + 2 * k), Hash01(gx, gz, salt + 1 + 2 * k)) * SquareM;
                    if (TryPlant(map, occ, tt, p, gx, gz, salt + k, Mathf.Max(canopy, 0.3f), distUp, BesideRoadM)) pool -= 1f;
                }
            }

            BuildMesh(tt);
            tt.ms = (float)clock.Elapsed.TotalMilliseconds;
            return tt;
        }

        /// <summary>A candidate point: free on the mask, clear of the tile's
        /// other trunks, (for the second pass) within <paramref name="nearRoad"/>
        /// of a carriageway, and a card that fits. Counts why it was refused.</summary>
        static bool TryPlant(CityMap map, RoadsideOccupancy occ, TreeTile tt, Vector2 p, int gx, int gz, int k,
                             float canopy, float distUp, float nearRoad)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                byte b = occ.At(p);
                if (b != 0) { tt.rejects[FirstBit(b)]++; return false; }
                if (Taken(occ, p)) { tt.rejects[RejectSpacing]++; return false; }
                // (the second pass needs only "within nearRoad": it looks no farther)
                float road = occ.RoadEdgeDistance(p, nearRoad > 0f ? nearRoad : RoadsideOccupancy.NearReachM, out _, out int edge, out var roadDir);
                if (nearRoad > 0f && road > nearRoad) return false;
                if (Plant(map, occ, tt, p, gx, gz, k, canopy, distUp, road, edge, roadDir, out var push))
                {
                    Take(occ, p);
                    return true;
                }
                // its low leaves would hang over a road: once, stand it back
                // from that road by as much as they would (the canopy over a
                // street is the trees beside it, so keep the tree)
                if (push.sqrMagnitude < 1e-6f || push.sqrMagnitude > 16f) break;
                p += push;
            }
            tt.rejects[RejectOverhang]++;
            return false;
        }

        static readonly List<RoadsideOccupancy.NearRoad> nearRoads = new List<RoadsideOccupancy.NearRoad>(16);

        /// <summary>Choose, size and seat one tree at a free point; false when
        /// no size or billboard of its species keeps its low leaves off every
        /// road beside it, with <paramref name="push"/> the step back from the
        /// worst road that would.</summary>
        static bool Plant(CityMap map, RoadsideOccupancy occ, TreeTile tt, Vector2 p, int gx, int gz, int k,
                          float canopy, float distUp, float road, int edge, Vector2 roadDir, out Vector2 push)
        {
            push = Vector2.zero;
            var sp = SpeciesAt(map, occ, p, road, edge, distUp, Hash01(gx, gz, 100 + k), wallNow);
            var cells = Cells[(int)sp];
            byte cell = cells[Mathf.Min(cells.Length - 1, (int)(Hash01(gx, gz, 200 + k) * cells.Length))];
            var hr = Heights[(int)sp];
            float h = Mathf.Lerp(hr.x, hr.y, Hash01(gx, gz, 300 + k));
            // an understory under a thick canopy: young trees in the gaps a
            // driver's eye actually looks through
            bool young = canopy >= 0.6f && sp != Species.Myrtle && Hash01(gx, gz, 400 + k) < 0.22f;
            if (young) h *= 0.55f + 0.2f * Hash01(gx, gz, 500 + k);
            float ground = CityMeshes.LatticeAt(map, p.x, p.y) - SinkM;

            // A CARD OVER THE TARMAC ONLY HIGH UP. A card is a flat plane as
            // wide as the tree, and a third of the billboards paint leaves
            // down to the bumper. Against EVERY road it could reach, what it
            // paints below OverhangClearM over that road must stay 0.4 m off
            // the pavement: the cards are turned to 45 degrees to the nearest
            // road (each then reaches 0.71 of its half-span toward it), and
            // the tree grown, shrunk or given another billboard of its species
            // until it fits; failing that, stood back (TryPlant).
            float yaw = Hash01(gx, gz, 600 + k) * 360f;
            nearRoads.Clear();
            occ.NearRoads(p, CardWidth(sp, hr.y * 1.3f) * 0.5f + 1f, nearRoads);
            if (nearRoads.Count > 0)
            {
                yaw = Mathf.Atan2(roadDir.y, roadDir.x) * Mathf.Rad2Deg + 45f;
                bool fit = false;
                float h0 = h, worst = 0f;
                Vector2 worstAway = Vector2.zero;
                for (int c = 0; c < cells.Length && !fit; c++)
                {
                    byte tryCell = cells[(System.Array.IndexOf(cells, cell) + c) % cells.Length];
                    foreach (float f in FitScales)
                    {
                        float hh = Mathf.Clamp(h0 * f, hr.x * 0.6f, hr.y * 1.3f);
                        // the first try measures the worst road (the push back
                        // needs it); the rest only ask whether it fits
                        float over = Overhang(p, tryCell, hh, CardWidth(sp, hh), yaw, ground, out var away, !(c == 0 && f == 1f));
                        if (c == 0 && f == 1f) { worst = over; worstAway = away; }
                        if (over > 0f) continue;
                        h = hh; cell = tryCell; fit = true;
                        break;
                    }
                }
                if (!fit) { push = worstAway * (worst + 0.1f); return false; }
                if (h > h0 + 0.01f) tt.grown++; else if (h < h0 - 0.01f) tt.shrunk++;
            }
            float w = CardWidth(sp, h);
            var t = new Tree
            {
                foot = new Vector3(p.x, ground, p.y), h = h, w = w,
                r = Mathf.Clamp(0.025f * w, TreeTrunks.MinRadius, 0.5f),
                yawDeg = yaw, cell = cell, species = sp, road = road,
                dbh = TrunkDiameterM(sp, h, young),
            };
            // Q15: a trunk a car can reach is solid if it is 30 cm or more;
            // a smaller tree breaks away (no collider)
            t.solid = road <= TrunkReachM && t.dbh >= SolidTrunkM;
            if (t.solid) tt.solids++;
            else if (road <= TrunkReachM) tt.breakaway++;
            tt.trees.Add(t);
            return true;
        }

        /// <summary>The species a spot calls for (the plan's WP-08 list).</summary>
        static Species SpeciesAt(CityMap map, RoadsideOccupancy occ, Vector2 p, float road, int edge, float distUp, float roll, bool wall)
        {
            // a freeway's tree wall: the broad hardwoods mostly (a pine's crown
            // is a third of one, and a wall of them is a picket fence)
            if (wall) return roll < 0.02f ? Species.Bare : roll < 0.25f ? Species.Pine : Species.Hardwood;
            if (roll < 0.02f) return Species.Bare;
            if (occ.NearCreek(p, 40f) && roll < 0.55f) return Species.Sycamore;
            var e = edge >= 0 ? map.edges[edge] : null;
            bool strip = e != null && !e.link && e.cls >= 2 && e.cls <= 4 && road < 18f;
            if (distUp < 1000f) return roll < 0.7f ? Species.Oak : Species.Myrtle;          // uptown street trees
            if (strip && roll < 0.55f) return Species.Myrtle;                                 // commercial strip
            if (distUp < 4500f) return roll < 0.75f ? Species.Oak : roll < 0.88f ? Species.Hardwood : Species.Pine;   // the old city
            return roll < 0.45f ? Species.Pine : Species.Hardwood;                           // suburbs and woods
        }

        static void BuildMesh(TreeTile tt)
        {
            if (tt.trees.Count == 0) return;
            vScratch.Clear(); uvScratch.Clear(); tScratch.Clear(); nScratch.Clear();
            var origin = new Vector3(tt.tx * CityMeshes.TileSize, 0f, tt.tz * CityMeshes.TileSize);
            const float pad = 1.5f / 512f;
            foreach (var t in tt.trees)
            {
                int col = t.cell % 4, row = t.cell / 4;
                float u0 = col * 0.25f + pad, u1 = (col + 1) * 0.25f - pad;
                float v0 = row * 0.25f + pad, v1 = (row + 1) * 0.25f - pad;
                var b = t.foot - origin;
                for (int q = 0; q < 2; q++)
                {
                    float a = (t.yawDeg + q * 90f) * Mathf.Deg2Rad;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    var half = new Vector3(ca, 0f, sa) * (t.w * 0.5f);
                    int v = vScratch.Count;
                    vScratch.Add(b - half); vScratch.Add(b - half + Vector3.up * t.h);
                    vScratch.Add(b + half + Vector3.up * t.h); vScratch.Add(b + half);
                    // the card's face normal, as RecalculateNormals made it
                    // (up x half), handed over rather than worked out (WP-09)
                    var nrm = new Vector3(sa, 0f, -ca);
                    nScratch.Add(nrm); nScratch.Add(nrm); nScratch.Add(nrm); nScratch.Add(nrm);
                    uvScratch.Add(new Vector2(u0, v0)); uvScratch.Add(new Vector2(u0, v1));
                    uvScratch.Add(new Vector2(u1, v1)); uvScratch.Add(new Vector2(u1, v0));
                    tScratch.Add(v); tScratch.Add(v + 1); tScratch.Add(v + 2);
                    tScratch.Add(v); tScratch.Add(v + 2); tScratch.Add(v + 3);
                }
            }
            var m = new Mesh { name = "trees" };
            if (vScratch.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(vScratch);
            m.SetNormals(nScratch);
            m.SetUVs(0, uvScratch);
            m.SetTriangles(tScratch, 0, false);
            m.RecalculateBounds();
            tt.mesh = m;
        }

        /// <summary>The dress to plant in: the calendar's (what the scene's
        /// SeasonDress puts on the ground, whichever of the two wakes first);
        /// in an edit-mode tool, where no dress is applied and the ground
        /// wears its baked fall material, the one it has on.</summary>
        public static int DressNow() =>
            Application.isPlaying ? Seasons.CurrentDress
            : SeasonDress.AppliedDress >= 0 ? SeasonDress.AppliedDress : (int)Season.Fall;

        /// <summary>How far past the 0.4 m margin the worst road in
        /// <see cref="nearRoads"/> would have this tree's low leaves hang
        /// (0 or less: it fits), and the way away from that road. With
        /// <paramref name="anyOnly"/> it stops at the first road hung over
        /// (the answer is then only "does not fit").</summary>
        static float Overhang(Vector2 p, byte cell, float h, float w, float yaw, float ground, out Vector2 away, bool anyOnly = false)
        {
            float worst = float.MinValue;
            away = Vector2.zero;
            var u0 = new Vector2(Mathf.Cos(yaw * Mathf.Deg2Rad), Mathf.Sin(yaw * Mathf.Deg2Rad));
            var u1 = new Vector2(-u0.y, u0.x);
            foreach (var r in nearRoads)
            {
                // each card's low foliage is a line through the trunk, reach
                // either way; it must stay LeafMarginM off this road's pavement
                float reach = LowReach(reachTable, cell, (r.y + OverhangClearM - ground) / h) * w;
                // a road farther off than the leaves reach cannot be hung
                // over (and this bound is all a fit needs of it)
                if (reach + LeafMarginM < r.off)
                {
                    float bound = reach + LeafMarginM - r.off;
                    if (bound > worst) { worst = bound; away = r.away; }
                    continue;
                }
                float d = Mathf.Min(RoadsideOccupancy.SegSegDistance(p - u0 * reach, p + u0 * reach, r.a, r.b),
                                    RoadsideOccupancy.SegSegDistance(p - u1 * reach, p + u1 * reach, r.a, r.b));
                float over = r.hw + LeafMarginM - d;
                if (over > worst) { worst = over; away = r.away; }
                if (anyOnly && over > 0f) return over;
            }
            return worst;
        }

        /// <summary>The dress the city wears today: the kit's tree material
        /// for the calendar's season and weather; null without a kit (the
        /// trees then stand with their renderer off, not pink).</summary>
        public static Material MaterialFor(int dress)
        {
            var kit = CityKit.Get();
            if (kit == null || kit.trees == null || kit.trees.Length == 0) return null;
            dress = Mathf.Clamp(dress, 0, kit.trees.Length - 1);
            return kit.trees[dress] != null ? kit.trees[dress] : null;
        }

        // ---- the spacing grid (2 m cells, the mask's) ----
        static int CellIndex(RoadsideOccupancy occ, Vector2 p)
        {
            int cx = Mathf.FloorToInt((p.x - occ.min.x) / RoadsideOccupancy.CellM);
            int cz = Mathf.FloorToInt((p.y - occ.min.y) / RoadsideOccupancy.CellM);
            if (cx < 0 || cz < 0 || cx >= RoadsideOccupancy.Res || cz >= RoadsideOccupancy.Res) return -1;
            return cz * RoadsideOccupancy.Res + cx;
        }
        static bool Taken(RoadsideOccupancy occ, Vector2 p) { int i = CellIndex(occ, p); return i < 0 || taken[i]; }
        static void Take(RoadsideOccupancy occ, Vector2 p)
        {
            int r = Mathf.CeilToInt(MinSpacingM / RoadsideOccupancy.CellM);
            int cx = Mathf.FloorToInt((p.x - occ.min.x) / RoadsideOccupancy.CellM);
            int cz = Mathf.FloorToInt((p.y - occ.min.y) / RoadsideOccupancy.CellM);
            for (int z = cz - r; z <= cz + r; z++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || z < 0 || x >= RoadsideOccupancy.Res || z >= RoadsideOccupancy.Res) continue;
                    var c = new Vector2(occ.min.x + (x + 0.5f) * RoadsideOccupancy.CellM, occ.min.y + (z + 0.5f) * RoadsideOccupancy.CellM);
                    if ((c - p).sqrMagnitude < MinSpacingM * MinSpacingM) taken[z * RoadsideOccupancy.Res + x] = true;
                }
        }

        static int FirstBit(byte b)
        {
            for (int i = 0; i < 8; i++) if ((b & (1 << i)) != 0) return i;
            return 7;
        }

        public static float Hash01(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + salt * 2246822519) + 2654435761u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }
}
