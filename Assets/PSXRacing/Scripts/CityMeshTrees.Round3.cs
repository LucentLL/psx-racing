using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// CHARLOTTE'S MESH TREES, ROUND 3 (trees2, 2026-11-15). Owner, on West
    /// Trade Street: "Trees don't seem accurate for Charlotte and are often
    /// too tall" - the walks lined with tall thin columns standing over
    /// three-storey blocks.
    ///
    /// WHY THEY WERE TALL AND THIN (rounds 1-2, kept behind PSX_CT_ROUND3=0):
    ///   * a street tree was a poplar or beech NARROWED to 0.42-0.62 of its
    ///     width on a long stem, then tried at scales up to 1.6 until its
    ///     crown cleared the lane: the clearance came from growing the tree
    ///     (measured: Uptown's grate trees 13 m tall, 4.3 m wide);
    ///   * every other tree stood at 1.3-1.35 times its card's height whatever
    ///     stood round it (a yard, a strip, a wood: 14-22 m alike), and one
    ///     that met a wall took those narrow street forms before a smaller tree;
    ///   * crape myrtles were a bush scaled to 1.7, pines a poplar in leaf to
    ///     the ground.
    ///
    /// ROUND 3: each tree's SITE decides (what stands round its foot: the
    /// nearest road and its class, the nearest house and other building -
    /// the map's real footprints as well as the procedural ones - the canopy
    /// map, the distance to Uptown), then its planted species:
    ///   uptown grate   a pruned street tree (CityTreeKit.G3Street), round or
    ///                  broad, 9-12.5 m on Tryon and Trade, 7.5-10.5 m on the
    ///                  side streets, as wide as the walk allows
    ///   median row     crape myrtles (4-6 m) where the island holds one, else
    ///                  small round street trees (7.5-9.5 m)
    ///   old-city street  the willow oak (G3Avenue): 17-24 m, broad, limbed up
    ///                  to a third, its canopy over the street
    ///   other roadside / yard / lot   Charlotte's own mix by size class (the
    ///                  city's street tree inventory: 42% large, 17% medium,
    ///                  41% small species): small ornamentals 4.5-7.5 m,
    ///                  medium round trees 8-13 m, large shade trees 15-22 m
    ///   woodland       the tallest: 18-27 m hardwoods, 19-27 m pines, the
    ///                  columnar poplar 1 in 40
    /// The lane's clearance comes from the FORM (a crown that starts at the
    /// clearance at the height the site wants); a tree is never scaled past
    /// its site's range. Where nothing of the wanted size fits, the next
    /// smaller class, then a bush, then nothing - as before. A tree drawn
    /// under <see cref="BreakawayUnderM"/> breaks away (its stem is a hand
    /// thick: Q15), so no thick trunk stands in the collider table without one drawn.
    ///
    /// THE FIT'S COST: what stands round the foot is laid out once as a
    /// PROFILE (the free radius about the trunk in quarter-metre slabs of
    /// height: roads' envelopes, boxes, poles, wires, real footprints), so a
    /// size is asked of it in a few dozen comparisons; only the size chosen
    /// goes to the exact test and to the physics (the tile-wide colliders),
    /// and what the physics refuses is written back into the profile - a tree
    /// against a wall is settled in two or three physics tests where round 2
    /// spent its 160.
    /// </summary>
    public partial class CityMeshTrees
    {
        /// <summary>PSX_CT_ROUND3=0: round 2's trees (the picture as it was).</summary>
        public static bool Round3 = System.Environment.GetEnvironmentVariable("PSX_CT_ROUND3") != "0";
        /// <summary>PSX_CT_PINEFORM=1: pines drawn as the kit's limbed-up pine forms (the crown in the top two
        /// fifths of a long bare trunk). OFF by default: their pruned trunks carry sawn limb stubs that read as
        /// damage from the road (2026-11-15 sheet, run 8); pines stay round 2's stand-in (a dark Meadow poplar)
        /// at the site's height until the form is clean.</summary>
        public static bool PineForms = System.Environment.GetEnvironmentVariable("PSX_CT_PINEFORM") == "1";
        public const float BreakawayUnderM = 8.5f;
        public static int Softened, DataTests;

        public enum Ctx : byte { Grate, Median, OldStreet, Roadside, Yard, Lot, Woodland, Open }
        public static readonly string[] CtxNames = { "uptown grate", "median row", "old-city street", "other roadside", "yard", "lot / strip", "woodland", "open ground" };

        public struct Site
        {
            public Ctx ctx;
            public float distUp, homeD, bldD, roadD, canopy;
            public int cls;
            public bool main, old, young;
        }

        // what Gather saw round the foot (the site)
        CityMap.Edge gEdge;
        float gEdgeD, gHomeD, gBldD;
        // BigHit's last refusal (the band it tested, the nearest ray's hit)
        float bigLo, bigHi, bigRr, bigD;

        bool round3Ready;
        public bool Round3Ready => round3Ready;
        int[] g3Street, g3Avenue, g3Pine, g3Myrtle, gSmall, gMedium, gLarge, gForest, gOak, gColumnar, gSnag, gShrub, gPineOld;
        float[] maxR;

        /// <summary>Tools: each fitted tile's sites, one a plant (CtxOf).</summary>
        public static bool RecordSites;
        readonly Dictionary<long, List<byte>> ctxOf = new Dictionary<long, List<byte>>();
        public List<byte> CtxOf(long key) => ctxOf.TryGetValue(key, out var l) ? l : null;
        Site lastSite;

        void Setup3()
        {
            int n = kit.variants.Length;
            maxR = new float[n];
            for (int v = 0; v < n; v++)
            {
                float m = 0f;
                for (int b = 0; b < CityTreeKit.Bands; b++) m = Mathf.Max(m, kit.CrownR(v, b));
                maxR[v] = Mathf.Max(0.1f, m);
            }
            int[] G3(byte g) { var l = new List<int>(); for (int v = 0; v < n; v++) if (kit.Group3(v) == g) l.Add(v); return l.ToArray(); }
            int[] Named(params string[] names)
            {
                var l = new List<int>();
                if (kit.names != null) foreach (var nm in names) { int i = System.Array.IndexOf(kit.names, nm); if (i >= 0) l.Add(i); }
                return l.ToArray();
            }
            g3Street = G3(CityTreeKit.G3Street); g3Avenue = G3(CityTreeKit.G3Avenue); g3Pine = G3(CityTreeKit.G3Pine); g3Myrtle = G3(CityTreeKit.G3Myrtle);
            gSmall = byRole[(int)CityTreeKit.Role.Small]; gOak = byRole[(int)CityTreeKit.Role.Oak]; gColumnar = byRole[(int)CityTreeKit.Role.Columnar];
            gSnag = byRole[(int)CityTreeKit.Role.Snag]; gShrub = byRole[(int)CityTreeKit.Role.Shrub]; gPineOld = byRole[(int)CityTreeKit.Role.PineStandIn];
            gMedium = Named("beech_tree_01", "beech_tree_02", "02_Poplar", "01_Poplar");
            gLarge = Named("03_Poplar_skan", "04_Poplar_skan", "05_Poplar_skan", "07_Poplar_skan", "beech_tree_03");
            gForest = Named("beech_tree_06", "beech_tree_07", "beech_tree_08", "beech_tree_03", "05_Poplar_skan", "07_Poplar_skan");
            round3Ready = g3Street.Length > 0 && g3Avenue.Length > 0 && gMedium.Length > 0 && gLarge.Length > 0 && gSmall.Length > 0;
            // (the canopy map read now - while the city loads - not at the first tree fitted: a baked city has not asked for it yet)
            if (round3Ready && !CityCanopy.Loaded) Debug.Log("[CityMeshTrees] no canopy map: sites take 0.4");
            if (Round3 && !round3Ready) Debug.Log("[CityMeshTrees] the kit has no round-3 forms (PSXRacingBuilder.BuildCityTreeKit): round 2's trees");
            Setup4();
        }

        // ==================================================================
        //  THE MAP'S REAL BUILDINGS (round 2 met them only as the tile's collider)
        // ==================================================================
        struct Fp { public CityMap.Footprint f; public float reach; public bool house; }
        readonly List<Fp> tileFp = new List<Fp>(128);
        int fpTx = int.MinValue, fpTz = int.MinValue;
        struct FpNear { public float dist, y0, y1; public bool house; }
        readonly List<FpNear> fpNear = new List<FpNear>(8);
        Vector2 fpAway;

        void TileFootprints(int tx, int tz)
        {
            if (tx == fpTx && tz == fpTz) return;
            fpTx = tx; fpTz = tz;
            tileFp.Clear();
            var map = world.Map;
            if (map.footprints == null) return;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var l = map.FootprintsInTile(tx + dx, tz + dz);
                    if (l == null) continue;
                    foreach (int fi in l)
                    {
                        var f = map.footprints[fi];
                        if (f == null || f.replaced || f.pts == null || f.pts.Length < 3) continue;
                        float r = 0f;
                        foreach (var q in f.pts) r = Mathf.Max(r, (q - f.centre).magnitude);
                        tileFp.Add(new Fp { f = f, reach = r, house = f.style == 3 || f.gable });
                    }
                }
        }

        /// <summary>The footprints within reach of a foot: how far each
        /// stands (0 inside it) and its height; the site's nearest house and
        /// other building with them.</summary>
        void GatherFoot(Vector3 foot)
        {
            fpNear.Clear();
            fpAway = Vector2.zero;
            float awayD = float.MaxValue;
            var p = new Vector2(foot.x, foot.z);
            foreach (var fp in tileFp)
            {
                float lim = GatherReach + fp.reach;
                if ((fp.f.centre - p).sqrMagnitude > lim * lim) continue;
                var pts = fp.f.pts;
                float best = float.MaxValue; bool inside = false;
                var nearest = p;
                for (int a = pts.Length - 1, b = 0; b < pts.Length; a = b++)
                {
                    Vector2 pa = pts[a], d = pts[b] - pa;
                    float L2 = d.sqrMagnitude;
                    float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - pa, d) / L2) : 0f;
                    float dd = (pa + d * t - p).sqrMagnitude;
                    if (dd < best) { best = dd; nearest = pa + d * t; }
                    if ((pa.y > p.y) != (pts[b].y > p.y) && p.x < (pts[b].x - pa.x) * (p.y - pa.y) / (pts[b].y - pa.y) + pa.x) inside = !inside;
                }
                float dist = inside ? 0f : Mathf.Sqrt(best);
                if (dist > GatherReach) continue;
                // (the way a tree against this wall would step: straight off it)
                if (!inside && dist < awayD && dist > 0.05f) { awayD = dist; fpAway = (p - nearest) / dist; }
                if (fp.house) { if (dist < gHomeD) gHomeD = dist; } else if (dist < gBldD) gBldD = dist;
                float roof = fp.f.roofH > 0.1f ? fp.f.roofH : fp.f.gable ? 3f : 0f;
                fpNear.Add(new FpNear
                {
                    dist = Mathf.Max(0f, dist - (fp.house ? 1.0f : 0.5f)),
                    y0 = fp.f.minH > 1f ? foot.y + fp.f.minH - 1f : foot.y - 3f,
                    y1 = foot.y + fp.f.h + roof + 0.8f, house = fp.house,
                });
            }
        }

        /// <summary>The site of a tree, from what the last Gather and GatherFoot at its foot saw.</summary>
        Site SiteNow(in CityTrees.Tree t)
        {
            var p = new Vector2(t.foot.x, t.foot.z);
            var s = new Site
            {
                distUp = Vector2.Distance(p, world.Map.uptown), homeD = gHomeD, bldD = gBldD,
                roadD = gEdge != null ? Mathf.Max(0f, gEdgeD) : 99f, cls = -1,
                canopy = CityCanopy.Loaded ? CityCanopy.Fraction(p.x, p.y) : 0.4f,
                young = t.dbh <= 0.125f && t.species != CityTrees.Species.Myrtle && t.species != CityTrees.Species.Bare && t.stem <= 0f,
            };
            s.old = s.distUp < 4500f;
            if (gEdge != null)
            {
                s.cls = gEdge.link ? 9 : gEdge.cls;
                s.main = gEdge.name != null && (gEdge.name.EndsWith("Tryon Street") || gEdge.name.EndsWith("Trade Street"));
            }
            if (t.street) { s.ctx = s.distUp < 3000f ? Ctx.Grate : Ctx.Median; return s; }
            bool big = !s.young && (t.species == CityTrees.Species.Oak || t.species == CityTrees.Species.Hardwood || t.species == CityTrees.Species.Sycamore);
            if (s.roadD <= 7.5f && s.cls >= 0 && s.cls < 5) s.ctx = s.old && s.cls <= 1 && big ? Ctx.OldStreet : Ctx.Roadside;
            else if (s.homeD <= 16f) s.ctx = Ctx.Yard;
            else if (s.bldD <= 16f) s.ctx = Ctx.Lot;
            else if (s.canopy >= 0.5f) s.ctx = Ctx.Woodland;
            else s.ctx = Ctx.Open;
            return s;
        }

        /// <summary>The site of a tree round 2 has just fitted (its Gather is still in hand).</summary>
        Site SiteLate(CityTrees.TreeTile tt, in CityTrees.Tree t)
        {
            TileFootprints(tt.tx, tt.tz);
            GatherFoot(t.foot);
            return SiteNow(t);
        }

        /// <summary>Tools: the site of a tree of a fitted tile.</summary>
        public Site SiteOf(CityTrees.TreeTile tt, in CityTrees.Tree t)
        {
            TileBuildings(tt.tx, tt.tz);
            Gather(world.Map, tt, t.foot, GatherReach);
            return SiteLate(tt, t);
        }

        // ==================================================================
        //  THE PROFILE: the free radius about the trunk, by height
        // ==================================================================
        const float GatherReach = 16f, MinScale3 = 0.40f, MaxScale3 = 1.65f;
        const float SlabM = 0.25f;
        const int Slabs = 200;
        readonly float[] freeR = new float[Slabs];
        readonly byte[] freeWhy = new byte[Slabs];
        float profY;

        void Put(float y0, float y1, float dist, int why)
        {
            int k0 = Mathf.Max(0, Mathf.FloorToInt((y0 - profY) / SlabM)), k1 = Mathf.Min(Slabs - 1, Mathf.FloorToInt((y1 - profY) / SlabM));
            for (int k = k0; k <= k1; k++) if (dist < freeR[k]) { freeR[k] = dist; freeWhy[k] = (byte)why; }
        }

        /// <summary>The profile at a foot, from the last Gather / GatherFoot
        /// there: exactly Conflict's data rules (roads, boxes, poles and
        /// wires), a slab at a time - a slab's worth conservative.</summary>
        void BuildProfile(Vector3 foot)
        {
            profY = foot.y;
            for (int k = 0; k < Slabs; k++) { freeR[k] = 99f; freeWhy[k] = 255; }
            var p = new Vector2(foot.x, foot.z);
            foreach (var r in roads)
            {
                float dd = SegDist(p, r.a, r.b);
                float clear = fitStreet ? GratePave(r.hw - 0.3f, dd) + 0.1f : r.hw + RoadMargin;
                Put(r.y - DeckUnderM, r.y + RoadClearM, dd - clear, 0);
            }
            foreach (var bx in boxes)
            {
                var q = p - bx.c;
                float du = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(q, bx.u)) - bx.hu), dv = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(q, bx.v)) - bx.hv);
                Put(bx.y0, bx.y1, Mathf.Sqrt(du * du + dv * dv), bx.why);
            }
            foreach (var sg in segs)
            {
                float t = SegT(p, sg.a, sg.b);
                Put(Mathf.Lerp(sg.y0a, sg.y0b, t), Mathf.Lerp(sg.y1a, sg.y1b, t), SegDist(p, sg.a, sg.b) - sg.r - Margin, sg.why);
            }
            foreach (var f in fpNear) Put(f.y0, f.y1, f.dist, 1);
        }

        int profWhy;

        /// <summary>Does a tree of this size stand clear of everything in the profile?</summary>
        bool Fits(int v, float s)
        {
            DataTests++;
            float hgt = kit.variants[v].height * s;
            for (int b = 0; b < CityTreeKit.Bands; b++)
            {
                float R = kit.CrownR(v, b) * s;
                if (R < 0.02f) continue;
                int k0 = Mathf.Max(0, Mathf.FloorToInt(hgt * b / CityTreeKit.Bands / SlabM)), k1 = Mathf.Min(Slabs - 1, Mathf.FloorToInt(hgt * (b + 1) / CityTreeKit.Bands / SlabM));
                for (int k = k0; k <= k1; k++)
                    if (freeR[k] < R) { profWhy = freeWhy[k]; lastBand = b; return false; }
            }
            return true;
        }

        // ==================================================================
        //  WHAT A TREE MAY BE
        // ==================================================================
        static readonly float[] LowFactors = { 1f, 0.94f, 0.88f, 0.82f, 0.76f, 0.7f, 0.64f, 0.58f, 0.52f };
        static readonly float[] HighFactors = { 1f, 1.06f, 0.94f, 1.12f, 0.88f, 1.18f, 0.82f, 1.24f, 0.76f, 0.7f };

        /// <summary>One thing to try: trees of a group at a height (and the
        /// range it may take). high: a limbed-up form - sizes either way, the
        /// broadest crown that fits near the height wins (broad: the radius
        /// wanted, as a share of the height).</summary>
        struct Stage { public int[] group; public float hT, hMin, hMax, broad; public bool high; }
        readonly List<Stage> plan = new List<Stage>(8);

        void St(int[] g, float hT, float hMin, float hMax, bool high = false, float broad = 0.4f)
        {
            if (g == null || g.Length == 0) return;
            plan.Add(new Stage { group = g, hT = Mathf.Clamp(hT, hMin, hMax), hMin = hMin, hMax = hMax, high = high, broad = broad });
        }

        /// <summary>What a tree of this site and planted species may be, in the order tried: ROUND 4's
        /// Charlotte species first (PSX_CT_CHARLOTTE, the owner's models in the kit), then round 3's
        /// stages as they were - so whatever round 3 stood up still stands where no Charlotte tree fits.</summary>
        void Plan3(in CityTrees.Tree t, in Site s)
        {
            plan.Clear();
            int c4 = 0;
            if (Charlotte && charlotteReady) { Plan4(t, s); c4 = plan.Count; }
            Plan3Stages(t, s);
            charlotteStages = c4;
        }

        /// <summary>Round 3's stages for a tree (added to the plan as they were).</summary>
        void Plan3Stages(in CityTrees.Tree t, in Site s)
        {
            float r1 = H(t.foot, 31), r2 = H(t.foot, 37), r3 = H(t.foot, 41);
            float L(float a, float b) => Mathf.Lerp(a, b, r2);
            var sp = t.species;
            if (t.street)
            {
                if (s.ctx == Ctx.Grate)
                {
                    // Tryon and Trade: the willow oaks of the 1980s streetscape, 15 years on; the side streets' younger trees
                    if (s.main) St(g3Street, L(9.2f, 12.4f), 7.6f, 13f, true, 0.40f);
                    else St(g3Street, L(7.8f, 10.4f), 7.4f, 11.2f, true, 0.38f);
                }
                else
                {
                    // a median row: crape myrtles where the island holds one clear of the lanes, else small round trees
                    if (r1 < 0.5f) St(g3Myrtle, L(4.2f, 6.2f), 3.4f, 6.6f);
                    St(g3Street, L(7.6f, 9.6f), 7.3f, 10.6f, true, 0.38f);
                    if (r1 >= 0.5f) St(g3Myrtle, L(4.2f, 6.0f), 3.2f, 6.6f);
                }
                St(gSmall, 4.5f, 2.5f, 5.5f);
                St(gShrub, 2.4f, 1.0f, 3.2f);
                return;
            }
            if (sp == CityTrees.Species.Bare) { St(gSnag, Mathf.Clamp(t.h * 1.15f, 8f, 18f), 5f, 19f); return; }
            if (sp == CityTrees.Species.Myrtle)
            {
                St(g3Myrtle, L(4.0f, 6.4f), 3.0f, 7f);
                St(gShrub, 3.0f, 1.2f, 4.2f);
                return;
            }
            if (s.young)
            {
                St(gSmall, Mathf.Clamp(t.h * 0.85f, 3.5f, 8f), 2.6f, 8.5f);
                St(gShrub, 2.6f, 1.2f, 3.6f);
                return;
            }
            bool wild = s.ctx == Ctx.Woodland || s.ctx == Ctx.Open;
            if (sp == CityTrees.Species.Pine)
            {
                // loblolly and shortleaf: the tallest thing on a lot, the crown high
                St(PineForms && g3Pine.Length > 0 ? g3Pine : gPineOld, wild ? L(19f, 27f) : L(16f, 24f), 12f, 28f);
                St(gMedium, L(8f, 12f), 5f, 13f);
                St(gSmall, L(4.5f, 7f), 2.6f, 8f);
                St(gShrub, 2.6f, 1.2f, 3.6f);
                return;
            }
            // oak, mixed hardwood, sycamore: by the site's size mix (0 small, 1 medium, 2 large, 3 forest)
            int size = SizeOf(s, sp, r1);
            bool roadside = s.ctx == Ctx.Roadside || s.ctx == Ctx.OldStreet;
            if (size == 3)
            {
                if (r3 < 0.025f) St(gColumnar, L(18f, 24f), 12f, 26f);
                St(gForest, L(18f, 27f), 11f, 29f);
                St(g3Avenue, L(17f, 24f), 14.5f, 26f, true, 0.44f);
                St(gMedium, L(9f, 13f), 5f, 14f);
            }
            else if (size == 2)
            {
                if (roadside)
                {
                    // beside a street: the canopy over it, the trunk clear to the lane's height
                    bool oldOak = s.ctx == Ctx.OldStreet;
                    St(g3Avenue, oldOak ? L(17f, 24.5f) : L(15f, 21f), 14.5f, oldOak ? 26f : 23f, true, 0.46f);
                    St(g3Street, L(10.5f, 14f), 9.5f, 15f, true, 0.42f);
                }
                else
                {
                    if (r3 < (s.ctx == Ctx.Open ? 0.12f : 0.06f)) St(gOak, L(15f, 20f), 14.5f, 22f);   // (the hero oak: the heaviest model, a park or a big yard)
                    St(gLarge, L(15f, 22f), 11f, 23f);
                    // against a house: a big tree limbed up, its crown over the roof
                    St(g3Avenue, L(16f, 23f), 14.5f, 25f, true, 0.44f);
                }
                St(gMedium, L(9f, 13f), 5f, 14f);
            }
            else if (size == 1)
            {
                if (roadside && s.roadD < 4.5f) St(g3Street, L(8f, 11.5f), 7.4f, 12.5f, true, 0.42f);
                St(gMedium, L(8f, 13f), 5f, 14f);
                if (roadside) St(g3Street, L(8f, 11.5f), 7.4f, 12.5f, true, 0.42f);
            }
            St(gSmall, L(4.5f, 7.5f), 2.6f, 8.5f);
            St(gShrub, 2.6f, 1.2f, 3.8f);
        }

        /// <summary>An oak / hardwood / sycamore's size class by its site (0 small, 1 medium, 2 large,
        /// 3 forest): the city's street tree inventory's mix (round 3).</summary>
        static int SizeOf(in Site s, CityTrees.Species sp, float r1)
        {
            int size;
            switch (s.ctx)
            {
                case Ctx.OldStreet: size = r1 < 0.12f ? 1 : 2; break;
                case Ctx.Roadside:
                    size = s.cls >= 2 ? (r1 < 0.25f ? 0 : r1 < 0.78f ? 1 : 2)          // a commercial strip: pears and small trees
                         : s.old ? (r1 < 0.2f ? 0 : r1 < 0.5f ? 1 : 2)
                         : (r1 < 0.28f ? 0 : r1 < 0.68f ? 1 : 2);                       // a subdivision street
                    break;
                case Ctx.Yard:
                    size = s.old ? (sp == CityTrees.Species.Oak ? (r1 < 0.22f ? 0 : r1 < 0.42f ? 1 : 2) : (r1 < 0.30f ? 0 : r1 < 0.62f ? 1 : 2))
                         : (r1 < 0.35f ? 0 : r1 < 0.72f ? 1 : 2);
                    break;
                case Ctx.Lot: size = r1 < 0.28f ? 0 : r1 < 0.80f ? 1 : 2; break;
                case Ctx.Woodland: size = 3; break;
                default: size = r1 < 0.14f ? 0 : r1 < 0.40f ? 1 : 2; break;
            }
            if (sp == CityTrees.Species.Sycamore && size < 2) size = 2;   // (a creek bottom's trees are big)
            return size;
        }

        // ==================================================================
        //  ROUND 4: THE OWNER'S CHARLOTTE TREES, BY SITE (trees4, 2026-11-15)
        // ==================================================================
        /// <summary>PSX_CT_CHARLOTTE=0: round 3's trees exactly (the owner's models stay in the kit,
        /// not planted). Needs round 3 (PSX_CT_ROUND3) and a kit built with the delivery.</summary>
        public static bool Charlotte = System.Environment.GetEnvironmentVariable("PSX_CT_CHARLOTTE") != "0";
        bool charlotteReady;
        public bool CharlotteReady => charlotteReady;
        /// <summary>Tools: how many of the last plan's stages were Charlotte species (a plant from a later stage is an old form).</summary>
        int charlotteStages;
        public static int PlacedOldForm, PlacedCharlotte;
        int[][] one;
        int cLobM, cLobY, cLobE, cWoAve, cWoYoung, cWoOpen, cPear, cDog, cCherry, cPlum, cMag, cHolly, cCedar,
            cWater, cWhite, cRedOak, cRedMaple, cSugar, cSilver, cGum, cTulip, cHickory, cSyc, cBirch;
        int[] cMyr;
        /// <summary>The willow oaks pruned for the street (the kit's CT4P_ forms: crown base raised to the lane's
        /// clearance): young round / small-high, young narrow (a walk under a wall), avenue limbed to a third, broad.</summary>
        int[] pYoung, pNarrow, pAve, pBroad;
        /// <summary>Plan4: the tree stands by a street or a lot's lanes (its willow oaks pruned first).</summary>
        bool side4;

        /// <summary>A site's species of one size class: the form, its share, the heights it is planted at.</summary>
        struct Pick4 { public int v; public float w, lo, hi; }
        Pick4[] tRoad0, tRoad1, tRoad2, tLot0, tLot1, tLot2, tYard0, tYard1, tYard2, tWood, tOpen0, tOpen1, tOpen2, tOld1, tOld2;

        void Setup4()
        {
            int n = kit.variants.Length;
            one = new int[n][];
            for (int v = 0; v < n; v++) one[v] = new[] { v };
            int have = 0;
            int F(string stem)
            {
                int i = kit.names != null ? System.Array.IndexOf(kit.names, "CT4_" + stem) : -1;
                if (i < 0 || kit.Group3(i) != CityTreeKit.G4Charlotte) return -1;
                have++;
                return i;
            }
            cLobM = F("LoblollyPine_Mature"); cLobY = F("LoblollyPine_Young"); cLobE = F("LoblollyPine_WoodlandEdge");
            cWoAve = F("WillowOak_Avenue"); cWoYoung = F("WillowOak_YoungStreet"); cWoOpen = F("WillowOak_OpenGrown");
            cPear = F("CalleryPear"); cDog = F("FloweringDogwood"); cCherry = F("FloweringCherry"); cPlum = F("PurpleleafPlum");
            cMag = F("SouthernMagnolia"); cHolly = F("AmericanHolly"); cCedar = F("EasternRedcedar");
            cWater = F("WaterOak"); cWhite = F("WhiteOak"); cRedOak = F("NorthernRedOak");
            cRedMaple = F("RedMaple"); cSugar = F("SugarMaple"); cSilver = F("SilverMaple");
            cGum = F("Sweetgum"); cTulip = F("TulipPoplar"); cHickory = F("Hickory"); cSyc = F("AmericanSycamore"); cBirch = F("RiverBirch");
            cMyr = new[] { F("CrapeMyrtle_Vase_pink"), F("CrapeMyrtle_Vase_red"), F("CrapeMyrtle_Vase_white"), F("CrapeMyrtle_Vase_purple") };
            for (int k = 1; k < cMyr.Length; k++) if (cMyr[k] < 0) cMyr[k] = cMyr[0];
            int[] PF(params string[] tags)
            {
                var l = new List<int>();
                foreach (var tg in tags)
                {
                    int i = kit.names != null ? System.Array.IndexOf(kit.names, "CT4P_" + tg) : -1;
                    if (i >= 0 && kit.Group3(i) == CityTreeKit.G4Charlotte) { l.Add(i); have++; }
                }
                return l.ToArray();
            }
            pYoung = PF("WillowOak_YoungStreet_lr", "WillowOak_YoungStreet_ls"); pNarrow = PF("WillowOak_YoungStreet_ln");
            pAve = PF("WillowOak_Avenue_av"); pBroad = PF("WillowOak_Avenue_lb");
            charlotteReady = round3Ready && cWoAve >= 0 && cWoYoung >= 0 && cLobM >= 0 && cMyr[0] >= 0;
            if (have > 0)
                Debug.Log($"[CityMeshTrees] Charlotte's own trees: {have} forms in the kit, " +
                          (!charlotteReady ? "NOT planted (a key form or round 3 missing)" : Charlotte ? "planted by site" : "not planted (PSX_CT_CHARLOTTE=0: round 3's trees)"));
            if (!charlotteReady) return;
            Pick4 P(int v, float w, float lo, float hi) => new Pick4 { v = v, w = w, lo = lo, hi = hi };
            int M = cMyr[0];   // (the colour is the tree's own hash, chosen when it is staged)
            // THE SHARES (the city's street tree inventory - willow oak 16.7%, crape myrtle 14.1%, pears 8.7%,
            // red maple 6.7%, dogwood 6.5%, sugar maple 3.5%, plum 2.9%, silver maple 2.4%, red cedar 2.4%,
            // sweetgum 2.2% - moved to 2026: crape myrtle now the commonest planted, fewer callery pears) by site
            tRoad0 = new[] { P(M, 55, 4.2f, 6.4f), P(cDog, 15, 4.5f, 6.8f), P(cCherry, 15, 5.5f, 8f), P(cPlum, 15, 5f, 7f) };
            tRoad1 = new[] { P(cPear, 30, 8.5f, 12f), P(cRedMaple, 30, 9f, 13.5f), P(cWoYoung, 25, 8f, 11.5f), P(cHolly, 15, 7.5f, 11f) };
            tRoad2 = new[] { P(cWoAve, 40, 15f, 21f), P(cRedMaple, 15, 13f, 16.5f), P(cWater, 15, 15f, 20f), P(cGum, 10, 15f, 21f), P(cRedOak, 10, 16f, 22f), P(cSilver, 10, 14f, 19f) };
            tLot0 = new[] { P(M, 70, 4.2f, 6.4f), P(cPlum, 10, 5f, 7f), P(cCherry, 10, 5.5f, 8f), P(cDog, 10, 4.5f, 6.8f) };
            tLot1 = new[] { P(cPear, 35, 8.5f, 12f), P(cRedMaple, 35, 9f, 13.5f), P(cHolly, 20, 7.5f, 11f), P(cWoYoung, 10, 8f, 11.5f) };
            tLot2 = new[] { P(cWoAve, 35, 15f, 21f), P(cRedMaple, 20, 13f, 16.5f), P(cWater, 20, 15f, 20f), P(cGum, 15, 15f, 21f), P(cWhite, 10, 16f, 21f) };
            tYard0 = new[] { P(cDog, 30, 4.5f, 6.8f), P(M, 30, 4.2f, 6.4f), P(cCherry, 20, 5.5f, 8f), P(cPlum, 20, 5f, 7f) };
            tYard1 = new[] { P(cPear, 22, 8.5f, 12f), P(cRedMaple, 25, 9f, 13.5f), P(cHolly, 18, 7.5f, 11f), P(cCedar, 15, 8f, 12.5f), P(cMag, 10, 9f, 13f), P(cSugar, 10, 10f, 14f) };
            tYard2 = new[] { P(cSugar, 18, 14f, 19f), P(cRedMaple, 14, 13f, 16.5f), P(cMag, 14, 12f, 16.5f), P(cWater, 14, 15f, 20f), P(cWhite, 10, 16f, 21f),
                             P(cRedOak, 10, 16f, 22f), P(cWoAve, 8, 15f, 21f), P(cSilver, 6, 14f, 19f), P(cTulip, 6, 18f, 24f) };
            tWood = new[] { P(cTulip, 22, 20f, 28f), P(cWhite, 18, 18f, 23f), P(cRedOak, 15, 18f, 24f), P(cHickory, 15, 18f, 25f), P(cGum, 15, 17f, 23f), P(cRedMaple, 15, 14f, 18f) };
            tOpen0 = new[] { P(cCedar, 60, 6f, 10f), P(cDog, 20, 4.5f, 6.8f), P(cRedMaple, 20, 7f, 10f) };
            tOpen1 = new[] { P(cCedar, 45, 9f, 13.5f), P(cGum, 30, 10f, 14f), P(cRedMaple, 25, 9f, 13.5f) };
            tOpen2 = new[] { P(cGum, 45, 15f, 22f), P(cRedMaple, 15, 13f, 16.5f), P(cWhite, 10, 16f, 21f), P(cTulip, 10, 18f, 24f), P(cCedar, 10, 11f, 14f), P(cRedOak, 10, 16f, 22f) };
            tOld1 = new[] { P(cWoYoung, 60, 9f, 12f), P(cRedMaple, 40, 10f, 14f) };
            tOld2 = new[] { P(cWoAve, 55, 17f, 24.5f), P(cWater, 17, 16f, 21f), P(cRedOak, 14, 17f, 23f), P(cWhite, 14, 16f, 22f) };
        }

        int Myr(float r) => cMyr[r < 0.40f ? 0 : r < 0.65f ? 1 : r < 0.85f ? 2 : 3];   // pink most, then red, white, purple

        /// <summary>A Charlotte form at a height range (none in the kit: nothing). The street willow oaks are
        /// limbed-up forms (the broadest crown that clears the lane near the height); the rest stand as grown,
        /// a quarter smaller at most where that is all that fits.</summary>
        void Cs(int v, float hT, float hMin, float hMax, bool high = false, float broad = 0.4f)
        {
            if (v < 0 || v >= one.Length) return;
            St(one[v], hT, hMin, hMax, high, broad);
        }

        void Sp(int v, float lo, float hi, float r)
        {
            float hT = Mathf.Lerp(lo, hi, r);
            if (v == cWoAve || v == cWoYoung)
            {
                // beside a street or a lot's lanes: pruned as the city prunes them first (crown base over the lane)
                if (side4) { if (v == cWoAve) { St(pAve, hT, lo * 0.85f, hi * 1.05f, true, 0.46f); St(pBroad, hT * 0.85f, lo * 0.75f, hi, true, 0.42f); } else St(pYoung, hT, lo * 0.85f, hi * 1.1f, true, 0.40f); }
                Cs(v, hT, lo * 0.85f, hi * 1.05f, true, v == cWoAve ? 0.44f : 0.38f);
            }
            else Cs(v, hT, lo * 0.75f, hi * 1.05f);
        }

        /// <summary>One of a table's species by its share (r 0..1), staged at its heights.</summary>
        void Take(Pick4[] tab, float r, float rh, int myr)
        {
            if (tab == null) return;
            float tot = 0f;
            foreach (var p in tab) if (p.v >= 0) tot += p.w;
            if (tot <= 0f) return;
            float x = r * tot;
            foreach (var p in tab)
            {
                if (p.v < 0) continue;
                x -= p.w;
                if (x < 0f) { Sp(p.v == cMyr[0] ? myr : p.v, p.lo, p.hi, rh); return; }
            }
            var last = tab[tab.Length - 1];
            Sp(last.v == cMyr[0] ? myr : last.v, last.lo, last.hi, rh);
        }

        /// <summary>ROUND 4's stages for a tree: the species its site plants, then smaller Charlotte species.</summary>
        void Plan4(in CityTrees.Tree t, in Site s)
        {
            var sp = t.species;
            if (sp == CityTrees.Species.Bare) return;   // (the dead snags stay round 3's)
            float r1 = H(t.foot, 31), r2 = H(t.foot, 37), r3 = H(t.foot, 41), r4 = H(t.foot, 53), r5 = H(t.foot, 59), r6 = H(t.foot, 61);
            float L(float a, float b) => Mathf.Lerp(a, b, r2);
            int myr = Myr(r5);
            side4 = s.ctx == Ctx.OldStreet || s.ctx == Ctx.Roadside || s.ctx == Ctx.Lot;
            bool wild = s.ctx == Ctx.Woodland || s.ctx == Ctx.Open;
            if (t.street)
            {
                // the street's willow oaks pruned as the city prunes them (crown base over the lane), the delivered forms after
                if (s.ctx == Ctx.Grate)
                {
                    // Uptown's grates: willow oaks - Tryon's and Trade's (planted in the 1980s) now 15-18 m where the walk
                    // allows one that broad, the side streets' younger; narrow ones under a wall
                    if (s.main) { St(pAve, L(15f, 18f), 13f, 18.5f, true, 0.42f); St(pBroad, L(12f, 15f), 10.5f, 16f, true, 0.40f); St(pYoung, L(9.5f, 12f), 7.6f, 12.5f, true, 0.40f); }
                    else if (r4 < 0.3f) { St(pBroad, L(11.5f, 14.5f), 10f, 15.5f, true, 0.40f); St(pYoung, L(8f, 10.5f), 7.2f, 11.2f, true, 0.38f); }
                    else { St(pYoung, L(7.8f, 10.4f), 7.2f, 11.2f, true, 0.38f); St(pBroad, L(11f, 13.5f), 10f, 14.5f, true, 0.40f); }
                    St(pNarrow, L(8f, 10.5f), 7f, 11.2f, true, 0.34f);
                    Cs(cWoYoung, L(7.8f, 10.4f), 7.2f, 11.2f, true, 0.38f);
                }
                else
                {
                    // a median row: crape myrtles and young willow oaks
                    if (r4 < 0.6f) { Cs(myr, L(4.4f, 6.2f), 3.4f, 6.6f); St(pYoung, L(7.4f, 9.4f), 7f, 10.2f, true, 0.38f); }
                    else { St(pYoung, L(7.4f, 9.4f), 7f, 10.2f, true, 0.38f); Cs(myr, L(4.4f, 6.2f), 3.4f, 6.6f); }
                    St(pNarrow, L(7.2f, 9f), 6.8f, 9.8f, true, 0.34f);
                    Cs(cWoYoung, L(7.4f, 9.4f), 7f, 10.2f, true, 0.38f);
                }
                return;
            }
            // (crape myrtle is planted: the bake's strip myrtle on a road through woods or open ground takes the wild mix)
            if (sp == CityTrees.Species.Myrtle && !wild)
            {
                // the baked crape myrtle (Uptown's 30%, a commercial strip's 55%): crape myrtle; a strip's pear now and then
                if ((s.ctx == Ctx.Lot || s.ctx == Ctx.Roadside) && r4 < 0.2f) Sp(cPear, 8.5f, 11.5f, r2);
                Sp(myr, 4.2f, 6.4f, r2);
                return;
            }
            if (s.young)
            {
                // under the canopy: flowering dogwood, American holly, young red maple; a young loblolly
                if (sp == CityTrees.Species.Pine) Sp(cLobY, 6.5f, 10f, r2);
                else if (r4 < 0.45f) Sp(cDog, 4.5f, 6.8f, r2);
                else if (r4 < 0.70f) Sp(cHolly, 5.5f, 8.5f, r2);
                else Sp(cRedMaple, 6.5f, 8.5f, r2);
                Sp(cDog, 4.2f, 6f, r2);
                return;
            }
            if (sp == CityTrees.Species.Pine)
            {
                // loblolly: the woods' tall high-crowned edge trees, a lot's or yard's mature pine; redcedar now and then
                if (wild) { if (r4 < 0.5f) Sp(cLobE, 21f, 27f, r2); else Sp(cLobM, 18f, 24f, r2); }
                else if (r4 < 0.16f) Sp(cCedar, 9f, 13.5f, r2);
                else if (r4 < 0.28f) Sp(cLobY, 8f, 11f, r2);
                else Sp(cLobM, 16f, 23f, r2);
                Sp(cLobY, 7.5f, 10.5f, r2);
                Sp(cCedar, 7.5f, 11f, r2);
                return;
            }
            if (sp == CityTrees.Species.Sycamore)
            {
                // within 40 m of a creek (CityTrees.SpeciesAt): American sycamore, river birch, silver maple on the low ground
                if (r4 < 0.50f) Sp(cSyc, 17f, 23f, r2); else if (r4 < 0.85f) Sp(cBirch, 12f, 16.5f, r2); else Sp(cSilver, 15f, 19.5f, r2);
                Sp(cBirch, 10f, 14f, r2);
                Sp(cRedMaple, 9f, 13f, r2);
                return;
            }
            int size = SizeOf(s, sp, r1);
            Pick4[] t0, t1, t2;
            switch (s.ctx)
            {
                case Ctx.OldStreet: t0 = tRoad0; t1 = tOld1; t2 = tOld2; break;
                case Ctx.Roadside: t0 = tRoad0; t1 = tRoad1; t2 = tRoad2; break;
                case Ctx.Lot: t0 = tLot0; t1 = tLot1; t2 = tLot2; break;
                case Ctx.Yard: t0 = tYard0; t1 = tYard1; t2 = tYard2; break;
                case Ctx.Woodland: t0 = tOpen0; t1 = tOpen1; t2 = tWood; break;
                default: t0 = tOpen0; t1 = tOpen1; t2 = tOpen2; break;
            }
            if (size == 3)
            {
                Take(tWood, r4, r2, myr);
                Take(tWood, r6, r2, myr);
                Sp(cRedMaple, 10f, 14f, r2);
            }
            else if (size == 2)
            {
                // the hero: the open-grown willow oak, a park or a big yard only (the heaviest model)
                if ((s.ctx == Ctx.Yard && r3 < (s.old ? 0.10f : 0.06f)) || (s.ctx == Ctx.Open && r3 < 0.12f)) Sp(cWoOpen, 14f, 19f, r2);
                Take(t2, r4, r2, myr);
                // (an old-city street's water, red or white oak that will not clear the lane: a willow oak limbed up)
                if (s.ctx == Ctx.OldStreet) St(pAve, L(16f, 22f), 14.5f, 24f, true, 0.44f);
                Take(t1, r6, r2, myr);
            }
            else if (size == 1) Take(t1, r4, r2, myr);
            Take(t0, size == 0 ? r4 : r6, r2, myr);
            // the last Charlotte tree: a crape myrtle kept small (yards and lots) or a young redcedar (the wild)
            if (wild) Sp(cCedar, 4.5f, 7f, r2); else Cs(myr, L(2.6f, 3.8f), 1.8f, 4.2f);
        }

        void Refuse(int why, in CityTrees.Tree t, Vector3 foot)
        {
            if (why < 0 || why >= RejectBy.Length) return;
            RejectBy[why]++; lastWhy = why;
            if (why == 1) { lastAway = AwayFromBox(new Vector2(foot.x, foot.z)); if (lastAway.sqrMagnitude < 0.5f) lastAway = fpAway; }
            if (why == 0 && t.street) RoadBand[Mathf.Clamp(lastBand, 0, RoadBand.Length - 1)]++;
            if (why == 4 && lastHit != null) { RejectColliders.TryGetValue(lastHit, out int rc); RejectColliders[lastHit] = rc + 1; }
        }

        /// <summary>The best tree of a stage the profile lets stand.</summary>
        bool Choose(in Stage st, in CityTrees.Tree t, out int bv, out float bs)
        {
            bv = -1; bs = 0f;
            var g = st.group;
            int start = Mathf.Min(g.Length - 1, (int)(H(t.foot, 43) * g.Length));
            float bestScore = float.MinValue, bestF = 0f;
            for (int k = 0; k < g.Length; k++)
            {
                int v = g[(start + k) % g.Length];
                float h0 = Mathf.Max(0.5f, kit.variants[v].height);
                float sLo = Mathf.Max(st.hMin / h0, MinScale3), sHi = Mathf.Min(st.hMax / h0, MaxScale3);
                if (sLo > sHi) continue;
                float s0 = Mathf.Clamp(st.hT / h0, sLo, sHi);
                if (!st.high)
                {
                    // an open-grown tree: of the group's models (from the hash's first), the one that stands nearest its size
                    foreach (float f in LowFactors)
                    {
                        float s = s0 * f;
                        if (s < sLo - 1e-3f || f <= bestF + 0.03f) break;
                        if (maxR[v] * s > GatherReach - 0.4f) continue;   // (no crown past what was gathered round the foot)
                        if (!Fits(v, s)) continue;
                        bestF = f; bv = v; bs = s;
                        break;
                    }
                    if (bestF >= 0.999f) return true;
                    continue;
                }
                // a limbed-up form: the height the site wants, lower for a slender crown (a pole is no street tree);
                // of every model's sizes that stand, the broadest near that height
                float slender = h0 / (2f * maxR[v]);
                // (the narrow ones are for a grate under a wall only: anywhere else a smaller tree stands, not a pole)
                if (!fitStreet && slender > 2.3f) continue;
                float want = st.hT * Mathf.Clamp(1.9f / slender, 0.72f, 1f);
                foreach (float f in HighFactors)
                {
                    float s = s0 * f;
                    if (s < sLo - 1e-3f || s > sHi + 1e-3f) continue;
                    if (maxR[v] * s > GatherReach - 0.4f) continue;
                    if (!Fits(v, s)) continue;
                    float score = -1.2f * Mathf.Abs(h0 * s - want) / st.hT + 0.9f * Mathf.Min(1f, maxR[v] * s / (st.broad * st.hT))
                                  - 0.3f * Mathf.Max(0f, slender - 1.8f) + 0.12f * H(t.foot, 47 + v);
                    if (score > bestScore) { bestScore = score; bv = v; bs = s; }
                }
            }
            return bv >= 0;
        }

        bool TryStage(in Stage st, in CityTrees.Tree t, Vector3 foot, float yaw, out MeshForest.PoolPlant plant)
        {
            plant = default;
            // (each refusal by the exact test or the physics is written into the profile: then choose again)
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (!Choose(st, t, out int v, out float s))
                {
                    if (attempt == 0 && profWhy < RejectBy.Length) Refuse(profWhy, t, foot);
                    return false;
                }
                int why = Conflict(v, s, foot, 1);
                if (why >= 0)
                {
                    // (the profile is a slab conservative, never the other way: belt and braces)
                    Refuse(why, t, foot);
                    float hb = kit.variants[v].height * s / CityTreeKit.Bands;
                    Put(foot.y + hb * lastBand, foot.y + hb * (lastBand + 1), kit.CrownR(v, lastBand) * s - 0.02f, why);
                    continue;
                }
                if (bigSolids.Count > 0)
                {
                    if (++tests > MaxTests) { CappedTrees++; return false; }
                    why = Conflict(v, s, foot, 2);
                    if (why >= 0)
                    {
                        Refuse(why, t, foot);
                        // what the physics met: nothing may reach that far in that band again
                        float d = (bigD < 90f ? Mathf.Min(bigD, bigRr) : bigRr) - Margin - 0.03f;
                        Put(bigLo, bigHi, Mathf.Max(0f, d), 4);
                        continue;
                    }
                }
                plant = new MeshForest.PoolPlant { pos = foot, yaw = yaw, scale = s, variant = v };
                return true;
            }
            return false;
        }

        bool TryTree3(CityMap map, CityTrees.TreeTile tt, in CityTrees.Tree t, out MeshForest.PoolPlant plant)
        {
            plant = default;
            lastAway = Vector2.zero;
            lastWhy = -1; lastHit = null;
            fitStreet = t.street;
            float yaw = H(t.foot, 13) * 360f;
            TileFootprints(tt.tx, tt.tz);
            Gather(map, tt, t.foot, GatherReach);
            GatherFoot(t.foot);
            awayKnown = false;
            lastSite = SiteNow(t);
            Plan3(t, lastSite);
            var away = Vector2.zero;
            int moves = t.street ? 1 : 7;
            int firstWhy = -1; string firstHit = null;
            for (int mv = 0; mv < moves; mv++)
            {
                var foot = t.foot;
                if (mv > 0)
                {
                    if (away.sqrMagnitude < 0.5f) break;
                    // (round 2's moves: straight away from the wall, then 45 degrees either side; 2 m, then 3.5 m)
                    float ang = mv == 1 || mv == 4 ? 0f : mv == 2 || mv == 5 ? 45f : -45f;
                    var dir2 = (Vector2)(Quaternion.Euler(0f, 0f, ang) * (Vector3)away.normalized);
                    var q = new Vector2(t.foot.x, t.foot.z) + dir2 * (mv < 4 ? 2f : 3.5f);
                    if (!GroundAt(q, t.foot.y, out float gy)) continue;
                    foot = new Vector3(q.x, gy - CityTrees.SinkM, q.y);
                    Gather(map, tt, foot, GatherReach);
                    GatherFoot(foot);
                    awayKnown = false;
                }
                BuildProfile(foot);
                for (int i = 0; i < plan.Count; i++)
                {
                    // ROUND 3'S STAGES AS THEY WERE: what the Charlotte stages' refusals wrote into the profile (each
                    // with its own reason) and the physics tests they spent are not round 3's - its stages get the
                    // profile and the budget they had, so no tree round 3 stood up falls (first after run: 48 v 47)
                    if (i == charlotteStages && i > 0) { BuildProfile(foot); tests = 0; }
                    if (!TryStage(plan[i], t, foot, yaw, out plant)) continue;
                    if (mv > 0) Moved++;
                    if (i > 0) Swapped++;
                    if (charlotteStages > 0) { if (i < charlotteStages) PlacedCharlotte++; else PlacedOldForm++; }
                    return true;
                }
                if (mv == 0) { away = lastAway; firstWhy = lastWhy; firstHit = lastHit; }
            }
            if (firstWhy >= 0) { lastWhy = firstWhy; lastHit = firstHit; }
            return false;
        }
    }
}
