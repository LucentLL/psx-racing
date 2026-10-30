using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// CHARLOTTE'S FREEWAY GUIDE SIGNS (the highway signs pass, 2026-10-29).
    /// The owner, from I-77 at the Brookshire Freeway: two photos "of what
    /// bridges, highway signs, and guard rails should look like" - galvanised
    /// two-post box-truss sign bridges, green panels with white borders and
    /// white words, the EXIT 11 B tab with its yellow LEFT plate, the diagonal
    /// arrow, the yellow EXIT 40 MPH plate at the ramp, the little gore sign.
    ///
    /// WHAT STANDS, per exit (a motorway_link diverge), as the MUTCD of the
    /// 1990s placed it:
    ///   * the EXIT DIRECTION sign over the road at the exit
    ///     (<see cref="GantryBackM"/> before the diverge, stepping back off a
    ///     deck): the exit's panel over its lanes with the up-diagonal arrow
    ///     on the exit's side, its number tab on top (a yellow LEFT plate in it
    ///     for a left exit), and, where the structure spans the road, the
    ///     PULL-THROUGH panel over the lanes going on - route, cardinal,
    ///     control city - with a down arrow over each of them;
    ///   * the ADVANCE guide signs a mile and half a mile before it
    ///     (<see cref="MileM"/>), the same legend with "1 MILE" / "1/2 MILE"
    ///     under it, where the freeway runs that far and no other overhead
    ///     sign stands within <see cref="SiteSpacingM"/> on that carriageway;
    ///     a cantilever from the exit's side, a sign bridge when that side
    ///     has no room;
    ///   * the GORE sign (EXIT, the number, the arrow) on two breakaway posts
    ///     in the gore where the ramp has drawn clear of the freeway;
    ///   * the EXIT SPEED ADVISORY (W13-2, black on yellow) on a breakaway
    ///     post on the ramp's right, past the gore: the map's
    ///     maxspeed:advisory, else the ramp's tightest curve within 300 m
    ///     (AASHTO, e + f = 0.22), posted when 45 mph or less.
    /// Every legend comes from the map (<see cref="CitySignData.Exit"/>):
    /// route designations are WORDS ("NC 16 NORTH") - never a shield, as the
    /// Interstate shield, the US shield and the NC diamond are trademarks - and
    /// nothing else is written: no brand, no logo, no express lane or toll
    /// sign (1999 had none).
    ///
    /// SET, NOT PAINTED: each face is a MOSAIC of quads on the guide atlas
    /// (Art/City/Signs/CityGuide.png, tools/city/guide_atlas.py: Overpass,
    /// the open Highway Gothic, OFL 1.1) - a letter's cell, the green between
    /// letters, the border's corners and edges - laid edge to edge with none
    /// overlapping, so no layer of a face can z-fight another and any legend
    /// the map holds can be set; a panel is sized to its legend, as a real one
    /// is, so nothing clips. A green backing a hand inside the panel hides any
    /// hairline a T-junction of the mosaic could open. The structures wear
    /// the pack's galvanised steel and concrete from the same atlas, so a
    /// tile's guide signs are one draw (<see cref="SignTile.guideMesh"/>).
    ///
    /// WHERE: the overhead legs as the gantries always stood (off every
    /// carriageway's pavement and clear zone, the panels
    /// <see cref="GantryClearM"/> over the highest road under them); the
    /// small signs on breakaway posts (no collider: a car knocks them down,
    /// as the MUTCD's crashworthy supports are made to be), off every
    /// pavement and deck, never in a building, lake or race run-off.
    /// Decided from global data like the gantries (<see cref="Pure"/>):
    /// every tile that a sign reaches finds the same one.
    /// </summary>
    public static partial class CitySigns
    {
        /// <summary>MUTCD's advance guide signs: a mile and half a mile before the exit.</summary>
        public const float MileM = 1609.344f;
        /// <summary>No two overhead sign structures on one carriageway nearer than this.</summary>
        public const float SiteSpacingM = 150f;
        /// <summary>Letter heights, metres: 16 in. destinations, 13 in. cardinals, the tab's 18 in. number.</summary>
        const float CapDest = 0.406f, CapCard = 0.33f, CapRoute = 0.406f, CapTabNum = 0.457f, CapTabWord = 0.30f;
        /// <summary>A big panel's border ring (the atlas's 48 px corner at 160 px/m).</summary>
        const float RingBig = 0.30f;
        /// <summary>The longest cantilever arm (leg to its free end).</summary>
        const float MaxArmM = 22f;
        /// <summary>A sign's bottom over the ground at a gore or a ramp (urban 7 ft).</summary>
        const float SmallSignBottomM = 2.13f;

        public enum GuideKind : byte { ExitGantry, Advance, Gore, Advisory, Curve, Chevron }

        internal struct Site
        {
            public GuideKind kind;
            /// <summary>The motorway edge ending at the exit's diverge (-1: a curve warning).</summary>
            public int exitEdge;
            /// <summary>An advance sign's station (its edge and arc), and its legend's miles.</summary>
            public int edge;
            public float s, miles;
            public Vector2 nominal;
            // ---- signs round 2 ----
            /// <summary>An advance sign tried on the ground first (a right
            /// exit, no lane drop, three lanes or fewer); <see cref="groundOnly"/>:
            /// and never overhead (an overhead one would stand within
            /// <see cref="SiteSpacingM"/> of another structure).</summary>
            public bool ground, groundOnly;
            /// <summary>An advance sign hung on another exit's structure: that
            /// site's index + 1 (0: its own structure).</summary>
            public int host;
            /// <summary>The advance panels of other exits this structure carries (site indices), or null.</summary>
            public List<int> extras;
            /// <summary>A curve warning or chevron: the traffic's way along the
            /// edge (+1 a to b), the side it stands on (+1 right of travel), the
            /// W1 symbol, the curve's hand (true: to the left), the advisory
            /// (mph), the diamond's side and the plaque's (metres).</summary>
            public sbyte dir, side;
            public byte sym;
            public bool leftHand;
            public int mph;
            public float size, plaque;
        }

        static readonly List<Site> sites = new List<Site>();
        static readonly Dictionary<long, List<int>> siteCells = new Dictionary<long, List<int>>();
        static CityMap siteMap;
        const float SiteCell = 256f;
        static long CellKey(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        /// <summary>Every exit's sign sites, once per graph: the exit gantry, the
        /// gore sign and the advisory at the exit, then the advance signs (half a
        /// mile first, then the mile) where no overhead sign stands near.</summary>
        static void EnsureSites(CityMap map)
        {
            if (siteMap == map) return;
            siteMap = map;
            sites.Clear(); siteCells.Clear(); exitInfos.Clear();
            droppedMile = droppedHalf = droppedMinor = 0;
            var overhead = new List<(Vector2 p, Vector2 t, int site)>();
            var exits = new List<int>();
            // ONE exit a diverge node: where two carriageways run into the node
            // (a ramp merging where another leaves, two freeways joining) each
            // is an "exit" edge, and two sign bridges stood in one another,
            // their legends z-fighting. The carriageway the freeway goes on
            // from most straightly carries the signs.
            var byNode = new Dictionary<int, int>();
            for (int ei = 0; ei < map.edges.Length; ei++)
            {
                var e = map.edges[ei];
                if (!IsExit(map, e)) continue;
                if (byNode.TryGetValue(e.b, out int had) && Straightness(map, map.edges[had]) >= Straightness(map, e)) continue;
                byNode[e.b] = ei;
            }
            for (int ei = 0; ei < map.edges.Length; ei++)
            {
                var e = map.edges[ei];
                if (!IsExit(map, e) || byNode[e.b] != ei) continue;
                exits.Add(ei);
                float s0 = GantryS0(e);
                sites.Add(new Site { kind = GuideKind.ExitGantry, exitEdge = ei, edge = ei, s = s0, nominal = e.PointAt(s0) });
                overhead.Add((e.PointAt(s0), e.TangentAt(s0), sites.Count - 1));
                var node = map.nodes[e.b];
                sites.Add(new Site { kind = GuideKind.Gore, exitEdge = ei, edge = ei, s = e.length, nominal = node });
                sites.Add(new Site { kind = GuideKind.Advisory, exitEdge = ei, edge = ei, s = e.length, nominal = node - e.TangentAt(e.length) * 0.5f });
            }
            foreach (float milesNominal in new[] { 0.5f, 1f })
                foreach (int ei in exits)
                {
                    var e = map.edges[ei];
                    float miles = milesNominal, back = miles * MileM;
                    bool ground = false;
                    if (S2Density)
                    {
                        // MUTCD 2E.33: the advance signs at 1/2 and 1 mile, never
                        // over the previous exit's own signing - closer than a
                        // mile, the 1 MILE sign is left out (real urban freeways);
                        // closer than half a mile, ONE advance sign half way, its
                        // distance to the nearest quarter mile; under 300 m none
                        float gap = PrevExitGap(map, e);
                        if (milesNominal > 0.75f && gap < MileM + 150f) { droppedMile++; continue; }
                        // ...and "at minor interchanges, only one Advance Guide sign" (2E.33): an
                        // exit to a street (no route on its sign) keeps its 1/2 MILE sign only
                        if (milesNominal > 0.75f && MinorInterchange(map, ei)) { droppedMinor++; continue; }
                        if (milesNominal < 0.75f && gap < 0.5f * MileM + 150f)
                        {
                            if (gap < 300f) { droppedHalf++; continue; }
                            back = gap * 0.5f;
                            miles = Mathf.Max(0.25f, Mathf.Round(back / MileM * 4f) / 4f);
                        }
                        // ground-mounted where real practice puts it (MUTCD 2A.17's
                        // overhead factors: a left exit, a lane drop, more than three lanes)
                        var exi = ExitOf(map, ei);
                        ground = !exi.left && exi.dropped == 0;
                    }
                    if (!BackAlong(map, e, e.length, back, out var ae, out float aS)) continue;
                    // (the lanes where the sign stands: an auxiliary lane begun nearer the exit is not there yet)
                    if (ground && MinLanesBack(map, ae, 400f) > 3) ground = false;
                    if (ground)
                    {
                        // a ground sign takes no overhead spacing; overhead only
                        // as a fallback where no structure stands within the spacing
                        var p = ae.PointAt(aS); var t = ae.TangentAt(aS);
                        bool near = false;
                        foreach (var (q, tq, _) in overhead)
                            if ((q - p).sqrMagnitude < SiteSpacingM * SiteSpacingM && Vector2.Dot(t, tq) > 0.5f) { near = true; break; }
                        sites.Add(new Site { kind = GuideKind.Advance, exitEdge = ei, edge = ae.index, s = aS, miles = miles, nominal = p, ground = true, groundOnly = near });
                        continue;
                    }
                    foreach (float off in new[] { 0f, -40f, 40f, -80f, 80f, -120f })
                    {
                        var se = ae; float ss = aS;
                        if (!Slide(map, ref se, ref ss, off)) continue;
                        var p = se.PointAt(ss); var t = se.TangentAt(ss);
                        int clash = -1;
                        foreach (var (q, tq, si) in overhead)
                            if ((q - p).sqrMagnitude < SiteSpacingM * SiteSpacingM && Vector2.Dot(t, tq) > 0.5f)
                            {
                                // (round 2) prefer another exit's sign bridge to share
                                if (clash < 0 || (S2Density && sites[si].kind == GuideKind.ExitGantry && sites[si].exitEdge != ei && sites[clash].kind != GuideKind.ExitGantry)) clash = si;
                            }
                        if (clash >= 0)
                        {
                            // (round 2) at its own station within the spacing of another
                            // exit's sign bridge: its panel hangs on that structure
                            // (one structure for two exits' signs, as closely spaced
                            // urban interchanges are signed) instead of a structure of its own
                            if (S2Density && off == 0f && sites[clash].kind == GuideKind.ExitGantry && sites[clash].exitEdge != ei
                                && (sites[clash].extras == null || sites[clash].extras.Count == 0))
                            {
                                var host = sites[clash];
                                host.extras = new List<int> { sites.Count };
                                sites[clash] = host;
                                sites.Add(new Site { kind = GuideKind.Advance, exitEdge = ei, edge = se.index, s = ss, miles = miles, nominal = p, host = clash + 1 });
                                break;
                            }
                            continue;
                        }
                        sites.Add(new Site { kind = GuideKind.Advance, exitEdge = ei, edge = se.index, s = ss, miles = miles, nominal = p });
                        overhead.Add((p, t, sites.Count - 1));
                        break;
                    }
                }
            // (round 2) the curve warnings and chevrons (CitySigns.Signs2)
            if (S2Curves) CurveSites(map);
            for (int i = 0; i < sites.Count; i++)
            {
                var p = sites[i].nominal;
                long k = CellKey(Mathf.FloorToInt(p.x / SiteCell), Mathf.FloorToInt(p.y / SiteCell));
                if (!siteCells.TryGetValue(k, out var l)) siteCells[k] = l = new List<int>();
                l.Add(i);
            }
        }

        /// <summary>How straight the freeway goes on out of a carriageway's end
        /// (the best dot of its tangent with a motorway edge leaving the node).</summary>
        static float Straightness(CityMap map, CityMap.Edge e)
        {
            float best = -2f;
            var t = e.TangentAt(e.length);
            foreach (int oi in map.nodeEdges[e.b])
            {
                var o = map.edges[oi];
                if (o == e || o.a != e.b || !IsMainline(o)) continue;
                best = Mathf.Max(best, Vector2.Dot(t, o.TangentAt(0f)));
            }
            return best;
        }

        /// <summary>The sites whose nominal place is in a rectangle.</summary>
        static void SitesIn(Vector2 lo, Vector2 hi, List<int> into)
        {
            into.Clear();
            int x0 = Mathf.FloorToInt(lo.x / SiteCell), x1 = Mathf.FloorToInt(hi.x / SiteCell);
            int z0 = Mathf.FloorToInt(lo.y / SiteCell), z1 = Mathf.FloorToInt(hi.y / SiteCell);
            for (int cz = z0; cz <= z1; cz++)
                for (int cx = x0; cx <= x1; cx++)
                    if (siteCells.TryGetValue(CellKey(cx, cz), out var l))
                        foreach (int i in l)
                        {
                            var p = sites[i].nominal;
                            if (p.x >= lo.x && p.y >= lo.y && p.x <= hi.x && p.y <= hi.y) into.Add(i);
                        }
            into.Sort();
        }
        static readonly List<int> siteScratch = new List<int>(64);

        static bool IsMainline(CityMap.Edge o) => !o.link && o.cls >= 5 && o.oneway && !o.tunnel && o.a != o.b;

        /// <summary>Back up a carriageway from arc s of e by dist, through each
        /// node onto the straightest motorway carriageway running into it.</summary>
        static bool BackAlong(CityMap map, CityMap.Edge e, float s, float dist, out CityMap.Edge at, out float sAt)
        {
            for (int hop = 0; hop < 80; hop++)
            {
                if (s >= dist) { at = e; sAt = s - dist; return true; }
                dist -= s;
                CityMap.Edge prev = null; float best = 0.5f;
                var t0 = e.TangentAt(0f);
                foreach (int oi in map.nodeEdges[e.a])
                {
                    var o = map.edges[oi];
                    if (o == e || o.b != e.a || !IsMainline(o)) continue;
                    float d = Vector2.Dot(o.TangentAt(o.length), t0);
                    if (d > best) { best = d; prev = o; }
                }
                if (prev == null) break;
                e = prev; s = e.length;
            }
            at = null; sAt = 0f;
            return false;
        }

        /// <summary>Move a station along its carriageway (+ with the traffic).</summary>
        static bool Slide(CityMap map, ref CityMap.Edge e, ref float s, float d)
        {
            if (d <= 0f) return BackAlong(map, e, s, -d, out e, out s);
            for (int hop = 0; hop < 40; hop++)
            {
                if (s + d <= e.length) { s += d; return true; }
                d -= e.length - s;
                CityMap.Edge next = null; float best = 0.5f;
                var t1 = e.TangentAt(e.length);
                foreach (int oi in map.nodeEdges[e.b])
                {
                    var o = map.edges[oi];
                    if (o == e || o.a != e.b || !IsMainline(o)) continue;
                    float dd = Vector2.Dot(o.TangentAt(0f), t1);
                    if (dd > best) { best = dd; next = o; }
                }
                if (next == null) return false;
                e = next; s = 0f;
            }
            return false;
        }

        // ------------------------------------------------------------------
        //  An exit: its legends, its side, its lanes
        // ------------------------------------------------------------------

        internal sealed class ExitInfo
        {
            public CityMap.Edge main, link;
            public bool left, hasData;
            public CitySignData.Exit data;
            /// <summary>Lanes before the diverge (destination groups summed) and the exit's of them.</summary>
            public int lanes, exitLanes;
            public int advisoryMph;
            /// <summary>(signs round 2) The through lanes DROPPED at the exit -
            /// carried onto the ramp and not on along the freeway (the graph's
            /// lanes before and after the diverge, OSM turn:lanes): the EXIT
            /// ONLY panel's arrows (MUTCD 2E.24). 0: an ordinary exit.</summary>
            public int dropped;
        }
        static readonly Dictionary<int, ExitInfo> exitInfos = new Dictionary<int, ExitInfo>();

        static ExitInfo ExitOf(CityMap map, int ei)
        {
            if (exitInfos.TryGetValue(ei, out var x)) return x;
            var e = map.edges[ei];
            x = new ExitInfo();
            int n = e.b;
            foreach (int oi in map.nodeEdges[n])
            {
                var o = map.edges[oi];
                if (o.a != n || !o.oneway || o == e) continue;
                if (o.link) { if (x.link == null || o.index < x.link.index) x.link = o; }
                else if (o.cls >= 5 && (x.main == null || o.index < x.main.index)) x.main = o;
            }
            x.hasData = CitySignData.ExitAt(map.nodes[n], out x.data);
            // the side: where the ramp heads off the freeway going on, 30 m out
            var node = map.nodes[n];
            Vector2 tl = x.link != null ? (x.link.PointAt(Mathf.Min(30f, x.link.length)) - node).normalized : Vector2.zero;
            Vector2 tm = x.main != null ? (x.main.PointAt(Mathf.Min(30f, x.main.length)) - node).normalized : e.TangentAt(e.length);
            float cross = tm.x * tl.y - tm.y * tl.x;
            x.left = Mathf.Abs(cross) > 0.03f ? cross > 0f : x.hasData && x.data.LeftByLanes;
            var groups = CitySignData.GantryGroups(e.wayId);
            int total = 0;
            if (groups != null) foreach (var g in groups) total += g;
            if (groups != null && groups.Length >= 2 && total >= 2)
            {
                x.lanes = total;
                x.exitLanes = x.left ? groups[0] : groups[groups.Length - 1];
            }
            else { x.lanes = Mathf.Max(2, e.lanes); x.exitLanes = 1; }
            x.exitLanes = Mathf.Clamp(x.exitLanes, 1, x.lanes - 1);
            x.advisoryMph = x.hasData && x.data.advisory > 0 ? x.data.advisory : CurveMph(map, x.link);
            if (S2ExitOnly) x.dropped = DroppedLanes(map, e, x);
            exitInfos[ei] = x;
            return x;
        }

        /// <summary>The speed a ramp's tightest curve within 300 m of the
        /// diverge asks for, mph: THE advisory the traffic drives and the census
        /// measures (<see cref="CityCurves.ExitMph"/>, road-logic-bends: one
        /// source of truth - AASHTO's V^2 = 15 R (e + f), R in feet,
        /// e + f = 0.22, rounded down to 5); no sign for 50 or more, and none
        /// under 15 (the W13-2 plaques NCDOT stocks).</summary>
        static int CurveMph(CityMap map, CityMap.Edge link)
        {
            int v = CityCurves.ExitMph(map, link);
            return v <= 0 || v >= 50 ? 0 : Mathf.Max(15, v);
        }

        // ------------------------------------------------------------------
        //  Deciding a site (global data only, cached like the gantries were)
        // ------------------------------------------------------------------

        static bool Guides(CityMap map, CityMeshes.Trims trims, Vector2 min, Vector2 max, long deadline)
        {
            EnsureSites(map);
            SitesIn(min - Vector2.one * ReachGantryM, max + Vector2.one * ReachGantryM, siteScratch);
            foreach (int si in siteScratch)
            {
                if (Due(deadline, GuideKey(si))) return false;
                var pu = PureGuide(map, trims, si);
                if (pureSeen.Add(pu)) pureNear.Add(pu);
            }
            return true;
        }

        static long GuideKey(int si) => (2L << 56) | (uint)si;

        /// <summary>Why the last site found no room (the census's tally).</summary>
        static string why = "";
        static readonly Dictionary<int, string> whyNot = new Dictionary<int, string>();

        static Pure PureGuide(CityMap map, CityMeshes.Trims trims, int si)
        {
            long key = GuideKey(si);
            if (pureCache.TryGetValue(key, out var pu)) return pu;
            decided++;
            why = "";
            var site = sites[si];
            bool small = site.kind == GuideKind.Gore || site.kind == GuideKind.Advisory || site.kind == GuideKind.Curve || site.kind == GuideKind.Chevron;
            pu = new Pure { kind = small ? Kind.Guide : Kind.Gantry, breakaway = small };
            pu.sign.owner = site.nominal;
            var was = rec; rec = pu;
            var wasLight = nightLight; nightLight = null;
            try
            {
                if (site.kind == GuideKind.Curve || site.kind == GuideKind.Chevron)
                {
                    // (round 2) MUTCD W1 + W13-1P, W1-8 (CitySigns.Signs2)
                    pu.placed = WarnSign(map, trims, pu, site);
                    if (!pu.placed) { pu.Reset(); whyNot[si] = why; }
                    pureCache[key] = pu;
                    return pu;
                }
                var ex = ExitOf(map, site.exitEdge);
                var e = map.edges[site.exitEdge];
                switch (site.kind)
                {
                    case GuideKind.ExitGantry:
                        // (round 2) the exit direction sign POST-MOUNTED past the clear zone
                        // where no MUTCD 2A.17 overhead factor holds (a right exit, no lane
                        // drop, three lanes or fewer: NCDOT's simple suburban interchanges)
                        if (S2Density && !ex.left && ex.dropped == 0 && MinLanesBack(map, e, 400f) <= 3)
                            foreach (float back in new[] { 0f, 20f, 40f, 60f })
                            {
                                float sg = site.s - back;
                                if (sg < 5f || e.bridge || e.ElevatedAt(sg)) continue;
                                pu.kind = Kind.Guide; pu.breakaway = true;
                                pu.placed = GroundGuide(map, trims, pu, e, sg, ex, site);
                                if (pu.placed) break;
                                pu.Reset(); pu.kind = Kind.Gantry; pu.breakaway = false;
                            }
                        // on a deck there is nothing to stand a leg on: step back up the road
                        for (float s = site.s; s > 5f && !pu.placed && site.s - s < 130f; s -= 40f)
                        {
                            if (e.bridge || e.ElevatedAt(s)) continue;
                            pu.placed = Overhead(map, trims, pu, e, s, ex, site);
                            if (!pu.placed) pu.Reset();
                        }
                        // (round 2) no ground for legs: on the overpass that crosses
                        // the freeway before the exit, on its fascia (MUTCD 2E.25)
                        if (!pu.placed && S2Bridge)
                        {
                            pu.Reset();
                            string whyLegs = why;
                            pu.placed = BridgeMount(map, trims, pu, e, site.s, ex, site, (e.length - 25f) - site.s, (e.length - 300f) - site.s);
                            // ...else a sign bridge bolted to its own deck's edges (an elevated freeway's)
                            for (float s = site.s; !pu.placed && s > 5f && site.s - s < 130f; s -= 20f)
                            {
                                if (!(e.bridge || e.ElevatedAt(s))) continue;
                                pu.Reset(); pu.kind = Kind.Gantry;
                                pu.placed = Overhead(map, trims, pu, e, s, ex, site, true);
                            }
                            if (!pu.placed) why = (whyLegs.Length > 0 ? whyLegs : "no ground for legs") + "; " + why;
                        }
                        break;
                    case GuideKind.Advance:
                        if (site.host > 0)
                        {
                            // (round 2) its panel hangs on another exit's structure
                            var hp = PureGuide(map, trims, site.host - 1);
                            why = hp.placed && hp.extraDrawn ? "shared" : "shared, the host did not stand";
                            break;
                        }
                        // the mile sign: on two posts past the clear zone where the
                        // ground is free (NCDOT's suburban habit); the half mile, a
                        // mile sign with no room beside the road, and every sign for a
                        // LEFT exit (a sign on the right for a left exit misleads): overhead
                        // (round 2: the ground first for every sign marked so, the
                        // overhead fallback only where no structure stands near)
                        int pass0 = S2Density ? (site.ground ? 0 : 1) : (site.miles > 0.75f && !ex.left ? 0 : 1);
                        int passEnd = S2Density && site.groundOnly ? 1 : 2;
                        for (int pass = pass0; pass < passEnd && !pu.placed; pass++)
                            foreach (float off in new[] { 0f, -30f, 30f, -60f, 60f })
                            {
                                var se = map.edges[site.edge]; float ss = site.s;
                                if (!Slide(map, ref se, ref ss, off) || se.bridge || se.ElevatedAt(ss)) continue;
                                if (pass == 0) { pu.kind = Kind.Guide; pu.breakaway = true; pu.placed = GroundGuide(map, trims, pu, se, ss, ex, site); }
                                else { pu.kind = Kind.Gantry; pu.breakaway = false; pu.placed = Overhead(map, trims, pu, se, ss, ex, site); }
                                if (pu.placed) break;
                                pu.Reset();
                            }
                        // (round 2) an overhead advance sign on a deck: on an overpass near its station
                        if (!pu.placed && S2Bridge && passEnd == 2)
                        {
                            pu.Reset(); pu.kind = Kind.Gantry; pu.breakaway = false;
                            var se = map.edges[site.edge];
                            if (se.bridge || se.ElevatedAt(site.s)) pu.placed = BridgeMount(map, trims, pu, se, site.s, ex, site, 120f, -140f);
                        }
                        break;
                    case GuideKind.Gore:
                        pu.placed = S2Gores ? GoreSign2(map, trims, pu, e, ex) : GoreSign(map, trims, pu, e, ex);
                        break;
                    case GuideKind.Advisory:
                        pu.placed = ex.advisoryMph > 0 && AdvisorySign(map, trims, pu, e, ex);
                        break;
                }
                if (!pu.placed) { pu.Reset(); whyNot[si] = site.kind == GuideKind.Advisory && ex.advisoryMph <= 0 ? "no advisory (over 45 mph)" : why; }
            }
            finally { rec = was; nightLight = wasLight; }
            pureCache[key] = pu;
            return pu;
        }

        // ------------------------------------------------------------------
        //  The overhead structures
        // ------------------------------------------------------------------

        /// <summary>A sign bridge or a cantilever at arc s of a carriageway,
        /// with the exit's panels (and at the exit, the pull-through) hung on
        /// its box truss. Records nothing unless it stands.</summary>
        static bool Overhead(CityMap map, CityMeshes.Trims trims, Pure pu, CityMap.Edge e, float s, ExitInfo ex, Site site, bool deck = false)
        {
            if (!ex.hasData && ex.link == null) { why = "no legend"; return false; }
            var p0 = e.PointAt(s);
            var t = e.TangentAt(s);
            var right = RightOf(t);
            var left = -right;
            LineModel.ExtentsNoAux(e, s, out float eM, out float eP);   // eM to the right of travel, eP to the left
            LineModel.Extents(e, s, out float pavR, out float pavL);
            // legs: off every pavement and clear zone, each side
            Vector2 legR, legL; bool hasR, hasL;
            if (deck)
            {
                // (round 2) ON STRUCTURE: the legs bolted to the deck's edges, outside
                // its rails (the rail's 0.3 m past the drawn edge, the post's half and a
                // hand), wherever no other pavement or deck is there
                legR = p0 + right * (pavR + CityMeshes.RailOverhangM + 0.5f);
                legL = p0 + left * (pavL + CityMeshes.RailOverhangM + 0.5f);
                hasR = PavementMargin(map, trims, legR) >= 0.25f - RoadsideOccupancy.DeckMarginM && RoadTopAt(map, trims, legR, 0.1f) < e.YAt(s) - 0.5f;
                hasL = PavementMargin(map, trims, legL) >= 0.25f - RoadsideOccupancy.DeckMarginM && RoadTopAt(map, trims, legL, 0.1f) < e.YAt(s) - 0.5f;
            }
            else
            {
                hasR = FindLeg(p0, right, pavR, out legR);
                hasL = FindLeg(p0, left, pavL, out legL);
            }
            bool advance = site.kind == GuideKind.Advance;
            bool exitSide = ex.left ? hasL : hasR, farSide = ex.left ? hasR : hasL;
            bool spanOk = hasL && hasR && Vector2.Distance(legL, legR) <= MaxSpanM;
            // an advance sign: a cantilever from the exit's side (a sign bridge
            // when that side has no room); the exit: a sign bridge, so the
            // pull-through hangs beside the exit's panel, else a cantilever
            bool span, armFromRight;
            if (advance && exitSide) { span = false; armFromRight = !ex.left; }
            else if (spanOk) { span = true; armFromRight = false; }
            else if (exitSide) { span = false; armFromRight = !ex.left; }
            else if (farSide) { span = false; armFromRight = ex.left; }
            else { why = "no legs"; return false; }
            Vector2 legA, legB;   // A: the left end of the truss, B: the right
            bool cantilever = !span;
            if (span) { legA = legL; legB = legR; }
            else if (armFromRight) { legB = legR; legA = p0 + left * (eP - 0.3f); }
            else { legA = legL; legB = p0 + right * (eM - 0.3f); }

            // the lanes under it (left to right), the exit's at its side
            int total = Mathf.Max(2, ex.lanes);
            int exitN = Mathf.Clamp(ex.exitLanes, 1, total - 1);
            float laneW = Mathf.Clamp((eM + eP - e.shl - e.shr) / total, 2.8f, 4.2f);
            var leftEdge = p0 + left * eP;
            float LaneX(int i) => e.shl + (i + 0.5f) * laneW;   // from the left edge, metres rightward
            float exitMid = 0f, thruMid = 0f;
            for (int i = 0; i < exitN; i++) exitMid += LaneX(ex.left ? i : total - 1 - i);
            exitMid /= exitN;
            var thruLanes = new List<float>();
            for (int i = 0; i < total - exitN; i++) thruLanes.Add(LaneX(ex.left ? exitN + i : i));
            foreach (var x in thruLanes) thruMid += x;
            thruMid /= Mathf.Max(1, thruLanes.Count);

            // the legends
            var d = ex.data;
            // (round 2) a LANE DROP: the EXIT ONLY panel (MUTCD 2E.24, the
            // 1988 manual's black on yellow), on the dropped lanes this station
            // has; on an advance sign a down arrow over each of them
            int dropHere = 0; List<float> dropXs = null;
            if (S2ExitOnly && ex.dropped > 0)
            {
                dropHere = Mathf.Clamp(ex.dropped - Mathf.Max(0, LanesOn(map.edges[site.exitEdge]) - LanesOn(e)), 0, Mathf.Min(ex.dropped, total - 1));
                if (advance && dropHere > 0)
                {
                    dropXs = new List<float>();
                    for (int i = 0; i < dropHere; i++) dropXs.Add(LaneX(ex.left ? i : total - 1 - i) - exitMid);
                }
            }
            var exitPanel = ExitLegend(ex, advance ? site.miles : 0f, !advance, dropHere, dropXs);
            var tab = string.IsNullOrEmpty(d.number) && !ex.left ? null : TabLegend(ex);
            // (round 2) another exit's advance panel shares this structure: it
            // hangs over the through lanes, where the pull-through would
            ExitInfo exX = null; GBlock extraPanel = null, extraTab = null;
            if (S2Density && site.extras != null && site.extras.Count > 0)
            {
                var xsite = sites[site.extras[0]];
                exX = ExitOf(map, xsite.exitEdge);
                if (exX.hasData)
                {
                    extraPanel = ExitLegend(exX, xsite.miles, false, S2ExitOnly ? Mathf.Min(exX.dropped, 2) : 0, null);
                    extraTab = string.IsNullOrEmpty(exX.data.number) && !exX.left ? null : TabLegend(exX);
                }
            }
            GBlock thruPanel = null;
            if (!advance && span && ex.hasData && !string.IsNullOrEmpty(d.thru) && extraPanel == null)
                thruPanel = ThruLegend(ex, thruLanes, thruMid);
            var panels = new List<(GBlock b, float x)>();   // x: centre, metres rightward from the left edge
            float ePos = exitMid;
            panels.Add((exitPanel, ePos));
            var second = thruPanel ?? extraPanel;
            if (second != null)
            {
                float tx = thruMid, gap = 0.6f;
                if (ex.left) tx = Mathf.Max(tx, ePos + exitPanel.w * 0.5f + gap + second.w * 0.5f);
                else tx = Mathf.Min(tx, ePos - exitPanel.w * 0.5f - gap - second.w * 0.5f);
                if (thruPanel != null) ((GThruPanel)thruPanel).shift = tx - thruMid;
                panels.Add((second, tx));
            }
            // within the truss: its ends (in the same left-edge frame); a leg keeps 0.8 m off a panel
            float aX = Vector2.Dot(legA - leftEdge, right), bX = Vector2.Dot(legB - leftEdge, right);
            if (span)
            {
                for (int i = 0; i < panels.Count; i++)
                {
                    var (b, x) = panels[i];
                    float lo = aX + 0.8f + b.w * 0.5f, hi = bX - 0.8f - b.w * 0.5f;
                    if (hi < lo)
                    {
                        // (round 2) the shared panel that does not fit goes; the exit's own stays
                        if (i > 0 && extraPanel != null) { panels.RemoveAt(i); extraPanel = null; break; }
                        why = "span shorter than the panels"; return false;
                    }
                    panels[i] = (b, Mathf.Clamp(x, lo, hi));
                }
            }
            else
            {
                // a cantilever reaches just past the panels it carries, no further
                float far = armFromRight ? float.MaxValue : float.MinValue;
                for (int i = 0; i < panels.Count; i++)
                {
                    var (b, x) = panels[i];
                    x = armFromRight ? Mathf.Min(x, bX - 0.8f - b.w * 0.5f) : Mathf.Max(x, aX + 0.8f + b.w * 0.5f);
                    panels[i] = (b, x);
                    far = armFromRight ? Mathf.Min(far, x - b.w * 0.5f - 0.3f) : Mathf.Max(far, x + b.w * 0.5f + 0.3f);
                }
                if (armFromRight) { aX = far; legA = leftEdge + right * far; }
                else { bX = far; legB = leftEdge + right * far; }
                if (Vector2.Distance(legA, legB) > MaxArmM)
                {
                    if (extraPanel == null) { why = "arm over 22 m"; return false; }
                    // (round 2) without the shared panel the arm may reach
                    pu.Reset();
                    var keep = site; keep.extras = null;
                    bool ok = Overhead(map, trims, pu, e, s, ex, keep);
                    pu.extraDrawn = false;
                    return ok;
                }
            }
            // clear of the utility poles and their wires
            if (!OffPoles(legA, legB, GantryHalfD)) { pu.wireSteps++; why = "a utility wire"; return false; }
            if (panels.Count > 1)
            {
                var (eb, ex0) = panels[0]; var (tb, tx0) = panels[1];
                if (Mathf.Abs(tx0 - ex0) < (eb.w + tb.w) * 0.5f + 0.3f) { panels.RemoveAt(1); thruPanel = null; extraPanel = null; }
                else if (thruPanel != null) ((GThruPanel)thruPanel).shift = tx0 - thruMid;
            }
            if (exitPanel is GPlate ep && ep.dropRow != null) ep.dropRow.shift = panels[0].x - exitMid;
            pu.extraDrawn = extraPanel != null;

            // the highest road under the span and the panels
            var A3 = legA; var B3 = legB;
            float spanL = Vector2.Distance(A3, B3);
            float roadTop = e.YAt(s);
            for (float q = 0f; q <= spanL; q += 2f)
            {
                var pq = Vector2.Lerp(A3, B3, q / Mathf.Max(0.01f, spanL));
                roadTop = Mathf.Max(roadTop, RoadTopAt(map, trims, pq, 0.5f));
                roadTop = Mathf.Max(roadTop, RoadTopAt(map, trims, pq - t * 0.72f, 0.5f));
            }
            float maxH = 0f;
            foreach (var (b, _) in panels) maxH = Mathf.Max(maxH, b.h);
            float yT = roadTop + GantryClearM + maxH * 0.5f;   // the truss's centre line: every panel centred on it
            const float TrussH = 1.1f, TrussD = 0.9f;
            float trussTop = yT + TrussH * 0.5f;
            var n3 = new Vector3(-t.x, 0f, -t.y);
            var r3 = new Vector3(right.x, 0f, right.y);
            // (round 2) THE 1990s SIGN LIGHTING: a catwalk under the panels and
            // a mercury-vapour luminaire on its front edge every ~2.8 m of panel,
            // aimed up at the face (MUTCD 2E.06: overhead signs illuminated)
            float lowest = yT - maxH * 0.5f;
            float walkY = lowest - 0.30f;

            // ---- record: the panels (and the tab), each a face
            float tallest = 0f;
            float spanLo = float.MaxValue, spanHi = float.MinValue;
            for (int i = 0; i < panels.Count; i++)
            {
                var (b, x) = panels[i];
                var pc = leftEdge + right * x;
                var centre = V3(pc, yT) + n3 * 0.72f;
                System.Func<Vector3, float> lit = null;
                if (S2Night)
                {
                    var lampXs = LampSpots(b.w);
                    var bl = centre - r3 * (b.w * 0.5f) - Vector3.up * (b.h * 0.5f);
                    lit = PanelLight(bl, r3, b.w, b.h, lampXs, false);
                    foreach (float lx in lampXs) Luminaire(pu, V3(leftEdge + right * (x - b.w * 0.5f + lx), walkY) + n3 * 0.72f, r3, n3, false);
                    spanLo = Mathf.Min(spanLo, x - b.w * 0.5f); spanHi = Mathf.Max(spanHi, x + b.w * 0.5f);
                }
                HangPanel(b, centre, r3, n3, lit);
                tallest = Mathf.Max(tallest, yT + b.h * 0.5f);
                pu.faces.Add(new Face { centre = centre, normal = n3, w = b.w, h = b.h, kind = deck ? Kind.Bridge : Kind.Gantry,
                                        viewer = Driver(map, e, s, -ViewAheadM, x - eP) });
                var tb = i == 0 ? tab : (b == extraPanel ? extraTab : null);
                bool tabLeft = i == 0 ? ex.left : (exX != null && exX.left);
                if (tb != null)
                {
                    // the number tab on top, flush with the panel's exit side
                    float tx = tabLeft ? x - b.w * 0.5f + tb.w * 0.5f : x + b.w * 0.5f - tb.w * 0.5f;
                    var tcentre = V3(leftEdge + right * tx, yT + b.h * 0.5f + tb.h * 0.5f) + n3 * 0.72f;
                    HangPanel(tb, tcentre, r3, n3, lit);
                    tallest = Mathf.Max(tallest, yT + b.h * 0.5f + tb.h);
                    pu.faces.Add(new Face { centre = tcentre, normal = n3, w = tb.w, h = tb.h, kind = deck ? Kind.Bridge : Kind.Gantry,
                                            viewer = Driver(map, e, s, -ViewAheadM, tx - eP) });
                }
            }
            if (S2Night && spanLo < spanHi)
                Catwalk(V3(leftEdge + right * (spanLo - 0.3f), walkY) + n3 * 0.72f, V3(leftEdge + right * (spanHi + 0.3f), walkY) + n3 * 0.72f, r3, n3);
            // ---- the box truss, leg to leg (or leg to its arm's end)
            var a3 = V3(legA, yT); var b3 = V3(legB, yT);
            BoxTruss(a3, b3, n3, TrussD, TrussH);
            // ---- the end frames: two round galvanised posts each, laced, on concrete footings
            var feet = span ? new[] { legA, legB } : new[] { armFromRight ? legB : legA };
            foreach (var leg in feet)
            {
                float gy = deck ? e.YAt(s) - 0.6f : CityMeshes.LatticeAt(map, leg.x, leg.y);
                EndFrame(V3(leg, gy), trussTop + 0.25f, n3, r3, TrussD, deck);
                float top = trussTop + 0.25f;
                if (S2Legs || deck)
                {
                    // (round 2) what is drawn: each round post (a box on its flats,
                    // 8-sided, 0.17 m) and its concrete footing (0.36 m, 0.55 m up),
                    // so the air between the two posts of a leg is air
                    LegPosts(pu, leg, gy, top, n3, TrussD, !deck);
                }
                else
                {
                    var lc = V3(leg, (gy - 0.3f + top) * 0.5f);
                    pu.posts.Add((lc, new Vector3(0.7f, top - gy + 0.3f, 1.4f), Mathf.Atan2(t.x, t.y) * Mathf.Rad2Deg));
                }
                if (deck) continue;   // on a deck: no ground of its own
                pu.feet.Add(leg);
                pu.Take(new Mark { a = leg, b = leg, r = 2f + RoadsideOccupancy.CellPadM, bit = RoadsideOccupancy.Other });
            }
            // and no tree under the truss
            if (!deck) pu.Take(new Mark { a = legA, b = legB, r = 2f + RoadsideOccupancy.CellPadM, bit = RoadsideOccupancy.Other });
            var c = (legA + legB) * 0.5f;
            if (deck) pu.kind = Kind.Bridge;
            pu.sign = new Sign
            {
                kind = deck ? Kind.Bridge : Kind.Gantry, pos = c, ground = CityMeshes.LatticeAt(map, c.x, c.y), bottom = yT - maxH * 0.5f, top = yT + 0.55f,   // the audit's truss box is centred 0.55 under top
                route = -1, edge = e.index, legA = span ? legA : (armFromRight ? legB : legA), legB = span ? legB : (armFromRight ? legB : legA),
                clearance = yT - maxH * 0.5f - roadTop, firstFace = 0, faces = pu.faces.Count, owner = pu.sign.owner,
            };
            return true;
        }

        /// <summary>A ground-mounted advance guide sign: the exit's panel (and
        /// its tab) on two galvanised posts on the right, past the
        /// carriageway's clear zone, turned to the drivers 150 m back, its
        /// bottom 2.1 m over the ground. Records nothing unless it stands.</summary>
        static bool GroundGuide(CityMap map, CityMeshes.Trims trims, Pure pu, CityMap.Edge e, float s, ExitInfo ex, Site site)
        {
            if (!ex.hasData) { why = "no legend"; return false; }
            var p0 = e.PointAt(s);
            var t = e.TangentAt(s);
            var right = RightOf(t);
            LineModel.Extents(e, s, out float pavR, out _);
            var panel = ExitLegend(ex, site.miles, site.kind == GuideKind.ExitGantry);
            var tab = TabLegend(ex);
            float off = pavR + RoadsideOccupancy.ClearZoneOf(e) + 1.0f + panel.w * 0.5f;
            var g = p0 + right * off;
            var viewer = Driver(map, e, s, -ViewAheadM, 0f);
            var vd = new Vector2(viewer.x - g.x, viewer.z - g.y);
            if (vd.sqrMagnitude < 1f) { why = "no driver"; return false; }
            vd.Normalize();
            var across = RightOf(-vd);
            var a = g - across * (panel.w * 0.5f); var b = g + across * (panel.w * 0.5f);
            var post0 = g - across * (panel.w * 0.3f); var post1 = g + across * (panel.w * 0.3f);
            const byte bits = (byte)(BlockBits | RoadsideOccupancy.Sight | RoadsideOccupancy.Corner | RoadsideOccupancy.Other);
            if (!PureFree(post0, bits) || !PureFree(post1, bits) || !PureSegFree(a, b, BlockBits)) { why = "no room beside the road"; return false; }
            if (RaceRunOff.Inside(map, trims, post0) || RaceRunOff.Inside(map, trims, post1)) { why = "race run-off"; return false; }
            if (!OffPoles(a, b, 0.5f)) { pu.wireSteps++; why = "a utility wire"; return false; }
            SmallSign(map, trims, pu, panel, g, vd, new[] { post0, post1 }, viewer, e, 0.10f, e.YAt(s));
            if (tab != null)
            {
                // the number tab on top, flush with the panel's exit side (its own plate)
                var f0 = pu.faces[0];
                var r3 = Vector3.Cross(f0.normal, Vector3.up).normalized;
                float side = ex.left ? -1f : 1f;
                var tc = f0.centre + r3 * (side * (panel.w - tab.w) * 0.5f) + Vector3.up * ((panel.h + tab.h) * 0.5f);
                HangSmall(tab, tc, r3, f0.normal);
                pu.faces.Add(new Face { centre = tc, normal = f0.normal, w = tab.w, h = tab.h, kind = Kind.Guide, viewer = viewer });
                var sg = pu.sign; sg.faces = pu.faces.Count; sg.top += tab.h; pu.sign = sg;
            }
            return true;
        }

        /// <summary>A panel: its aluminium box (back and edges), the green
        /// backing a hand inside its face, the legend's mosaic on its face, and
        /// the vertical stiffeners that hold it to the truss.</summary>
        static void HangPanel(GBlock b, Vector3 centre, Vector3 r3, Vector3 n3, System.Func<Vector3, float> lit = null)
        {
            const float depth = 0.10f;
            var up = Vector3.up;
            // the box: aluminium all round but the face
            GBox(centre - n3 * (depth * 0.5f), r3, up, n3, b.w * 0.5f, b.h * 0.5f, depth * 0.5f, CityGuideFont.Alum, false);
            // (round 2) at night the face takes its luminaires' light (UV2)
            var keepLight = nightLight; nightLight = lit;
            // the backing (only a crack in the mosaic shows it), at the panel's
            // back plane: 3 cm behind the legend z-fought it from 150 m on a
            // 5 cm near plane; 9.5 cm holds to 450 m on the cockpit's 12 cm
            var bo = centre - n3 * (depth - 0.005f) - r3 * (b.w * 0.5f) + up * (b.h * 0.5f);
            if (!DbgNoBacking) GQuadAt(bo, bo + r3 * b.w, bo + r3 * b.w - up * b.h, bo - up * b.h, n3, Solid(b.yellow));
            // the legend
            var face = new GFace { o = centre + n3 * 0.001f - r3 * (b.w * 0.5f) + up * (b.h * 0.5f), r = r3, d = -up, n = n3 };
            legendLead = S2Glyph;
            b.Draw(face, 0f, 0f, b.w, b.h);
            legendLead = false;
            nightLight = keepLight;
            // stiffeners behind: one every 1.6 m, from the panel's back to the truss
            int ns = Mathf.Max(2, Mathf.CeilToInt(b.w / 1.6f));
            for (int i = 0; i < ns; i++)
            {
                float x = -b.w * 0.5f + (i + 0.5f) * b.w / ns;
                GBox(centre - n3 * (depth + 0.14f) + r3 * x, r3, up, n3, 0.05f, b.h * 0.5f - 0.05f, 0.14f, CityGuideFont.Alum, true);
            }
        }

        /// <summary>A galvanised box truss from a to b (plan through its centre
        /// line), depth along n, height up: four chords, a vertical frame each
        /// bay, the front, back and bottom faces laced with diagonals.</summary>
        static void BoxTruss(Vector3 a, Vector3 b, Vector3 n, float D, float Hh)
        {
            var along = b - a; float L = along.magnitude;
            if (L < 0.5f) return;
            along /= L;
            var up = Vector3.up;
            const float chord = 0.07f, lace = 0.035f;
            var cell = CityGuideFont.Galv;
            float hd = D * 0.5f, hh = Hh * 0.5f;
            var mid = (a + b) * 0.5f;
            foreach (float sy in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    GBox(mid + up * (sy * hh) + n * (sz * hd), along, up, n, L * 0.5f + 0.2f, chord, chord, cell, true);
            int bays = Mathf.Max(1, Mathf.RoundToInt(L / 1.6f));
            float bay = L / bays;
            for (int i = 0; i <= bays; i++)
            {
                var c = a + along * (i * bay);
                // the bay's frame: the front and back verticals, the bottom cross member
                // (the top's hides behind the panels and above every eye)
                foreach (float sz in new[] { -1f, 1f }) Lace(c + n * (sz * hd) - up * hh, c + n * (sz * hd) + up * hh, lace, n, cell);
                Lace(c - up * hh - n * hd, c - up * hh + n * hd, lace, up, cell);
                if (i == bays) break;
                var c1 = c + along * bay;
                float flip = (i & 1) == 0 ? 1f : -1f;
                // the front and back faces' diagonals, zig-zag, and the bottom face's
                foreach (float sz in new[] { -1f, 1f })
                    Lace(c + n * (sz * hd) + up * (-hh * flip), c1 + n * (sz * hd) + up * (hh * flip), lace, n, cell);
                Lace(c + up * (-hh) + n * (-hd * flip), c1 + up * (-hh) + n * (hd * flip), lace, up, cell);
            }
        }

        /// <summary>A sign bridge's end frame: two round posts (front and back
        /// of the truss) on concrete footings, laced like a ladder.</summary>
        static void EndFrame(Vector3 ground, float top, Vector3 n, Vector3 r, float D, bool deck = false)
        {
            float hd = D * 0.5f;
            var cell = CityGuideFont.Galv;
            foreach (float sz in new[] { -1f, 1f })
            {
                var g = ground + n * (sz * hd);
                if (deck)
                {
                    // (round 2) bolted to the deck's side: a steel base plate, the post from under the slab
                    GBox(g + Vector3.up * 0.3f, n, Vector3.up, r, 0.24f, 0.3f, 0.24f, cell, true);
                    Prism(g, new Vector3(g.x, top, g.z), 0.17f, 8, cell, true);
                    continue;
                }
                Prism(g + Vector3.down * 0.4f, g + Vector3.up * 0.55f, 0.36f, 8, CityGuideFont.Concrete, true);
                Prism(g + Vector3.up * 0.5f, new Vector3(g.x, top, g.z), 0.17f, 8, cell, true);
            }
            float h0 = ground.y + 1.4f;
            int rungs = Mathf.Max(1, Mathf.FloorToInt((top - 0.6f - h0) / 1.5f));
            float step = (top - 0.6f - h0) / rungs;
            for (int i = 0; i <= rungs; i++)
            {
                var c = new Vector3(ground.x, h0 + i * step, ground.z);
                Bar(c - n * hd, c + n * hd, 0.04f, cell);
                if (i < rungs) Bar(c + n * (-hd * ((i & 1) == 0 ? 1f : -1f)), c + Vector3.up * step + n * (hd * ((i & 1) == 0 ? 1f : -1f)), 0.03f, cell);
            }
        }

        // ------------------------------------------------------------------
        //  The small signs: the gore sign and the exit-speed advisory
        // ------------------------------------------------------------------

        /// <summary>A plan point a breakaway post may stand on: off every
        /// pavement (half a metre) and deck, out of buildings, water, lots,
        /// lamps and race run-off.</summary>
        static bool BreakawayFree(Vector2 p) =>
            (StaticAt(p) & (RoadsideOccupancy.Building | RoadsideOccupancy.Water | RoadsideOccupancy.Other | S2BreakawayKeep)) == 0 &&
            PavementMargin(mapNow, trimsNow, p) >= 0.5f && !RaceRunOff.Inside(mapNow, trimsNow, p) && OffPoles(p, p, 0.5f);

        /// <summary>How far a plan point is outside every road's pavement (and a
        /// deck's margin): negative on it. The breakaway signs' rule, and the
        /// sign audit's for them.</summary>
        public static float PavementMargin(CityMap map, CityMeshes.Trims trims, Vector2 p)
        {
            float worst = float.MaxValue;
            clearScratch.Clear();
            map.EdgeSegsInRect(p - Vector2.one * 30f, p + Vector2.one * 30f, clearScratch);
            foreach (int packed in clearScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (e.tunnel) continue;
                Vector2 a = e.pts[si], dd = e.pts[si + 1] - a;
                float L2 = dd.sqrMagnitude;
                float tq = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, dd) / L2) : 0f;
                float at = e.s[si] + Mathf.Sqrt(L2) * tq;
                RoadsideOccupancy.RoadEdgeAt(e, trims, at, out _, out _, out float hwL, out float hwR);
                float off = Vector2.Distance(p, a + dd * tq) - Mathf.Max(hwL, hwR);
                if (e.bridge || e.ElevatedAt(at)) off -= RoadsideOccupancy.DeckMarginM;
                worst = Mathf.Min(worst, off);
            }
            return worst;
        }

        /// <summary>The gore sign: EXIT, the number, the arrow, on two
        /// breakaway posts where the ramp has drawn clear of the freeway.</summary>
        static bool GoreSign(CityMap map, CityMeshes.Trims trims, Pure pu, CityMap.Edge e, ExitInfo ex)
        {
            why = "no gore within 170 m";
            if (ex.link == null || ex.main == null) { why = "no ramp"; return false; }
            var L = ex.link; var M = ex.main;
            var legend = GoreLegend(ex);
            float need = legend.w + 1.4f;
            for (float t = 15f; t <= Mathf.Min(170f, L.length - 5f); t += 5f)
            {
                var pl = L.PointAt(t);
                float sm = ProjectS(M, pl);
                var pm = M.PointAt(sm);
                LineModel.Extents(M, sm, out float mR, out float mL);
                LineModel.Extents(L, t, out float lR, out float lL);
                float dist = Vector2.Distance(pl, pm);
                float gap = dist - (ex.left ? mL : mR) - (ex.left ? lR : lL);
                if (gap < need) continue;
                var toMain = (pm - pl).normalized;
                var g = pl + toMain * ((ex.left ? lR : lL) + gap * 0.5f);
                var viewer = Driver(map, e, e.length, -ViewAheadM, 0f);
                var vd = new Vector2(viewer.x - g.x, viewer.z - g.y);
                if (vd.sqrMagnitude < 1f) continue;
                vd.Normalize();
                var across = RightOf(-vd);   // the face's width runs square to the look
                var p0 = g - across * (legend.w * 0.5f); var p1 = g + across * (legend.w * 0.5f);
                var post0 = g - across * (legend.w * 0.3f); var post1 = g + across * (legend.w * 0.3f);
                if (!BreakawayFree(post0) || !BreakawayFree(post1) || PavementMargin(map, trims, p0) < 0.3f || PavementMargin(map, trims, p1) < 0.3f) { why = "the gore's ground taken"; continue; }
                if (!OffPoles(p0, p1, 0.4f)) { pu.wireSteps++; continue; }
                SmallSign(map, trims, pu, legend, g, vd, new[] { post0, post1 }, viewer, e, 0.038f, Mathf.Max(M.YAt(sm), L.YAt(t)));
                return true;
            }
            return false;
        }

        /// <summary>The exit-speed advisory: on the ramp's right, past the gore.</summary>
        static bool AdvisorySign(CityMap map, CityMeshes.Trims trims, Pure pu, CityMap.Edge e, ExitInfo ex)
        {
            why = "no free ground on the ramp's right";
            if (ex.link == null) { why = "no ramp"; return false; }
            var L = ex.link;
            var legend = AdvisoryLegend(ex.advisoryMph);
            for (float t = 55f; t <= Mathf.Min(175f, L.length - 5f); t += 8f)
            {
                var pl = L.PointAt(t);
                var tt = L.TangentAt(t);
                LineModel.Extents(L, t, out float lR, out _);
                var g = pl + RightOf(tt) * (lR + 1.6f);
                var viewer = Driver(map, L, t, -60f, 0f);
                var vd = new Vector2(viewer.x - g.x, viewer.z - g.y);
                if (vd.sqrMagnitude < 1f) continue;
                vd.Normalize();
                var across = RightOf(-vd);
                var p0 = g - across * (legend.w * 0.5f); var p1 = g + across * (legend.w * 0.5f);
                if (!BreakawayFree(g) || PavementMargin(map, trims, p0) < 0.3f || PavementMargin(map, trims, p1) < 0.3f) continue;
                if (!OffPoles(p0, p1, 0.4f)) { pu.wireSteps++; continue; }
                SmallSign(map, trims, pu, legend, g, vd, new[] { g }, viewer, L, 0.038f, L.YAt(t));
                return true;
            }
            return false;
        }

        /// <summary>A small sign on breakaway posts: its face turned to its
        /// driver, its bottom <see cref="SmallSignBottomM"/> over the ground and
        /// the road beside it, square galvanised posts into the ground.</summary>
        static void SmallSign(CityMap map, CityMeshes.Trims trims, Pure pu, GBlock legend, Vector2 g, Vector2 look, Vector2[] posts, Vector3 viewer, CityMap.Edge e, float postHalf = 0.038f, float roadY = float.NegativeInfinity)
        {
            float gy = CityMeshes.LatticeAt(map, g.x, g.y);
            float road = Mathf.Max(RoadTopAt(map, trims, g, 4f), roadY);
            // the bottom 2.1 m over the ground AND over the road it serves (a
            // road on fill: the panel stands on longer posts, not down the bank)
            float baseY = float.IsNegativeInfinity(road) ? gy : Mathf.Max(gy, road);
            float bottom = baseY + SmallSignBottomM;
            var n3 = new Vector3(look.x, 0f, look.y);
            var r3 = Vector3.Cross(n3, Vector3.up).normalized;   // the viewer's right as they look at it
            var centre = new Vector3(g.x, bottom + legend.h * 0.5f, g.y);
            HangSmall(legend, centre, r3, n3);
            foreach (var p in posts)
            {
                float py = CityMeshes.LatticeAt(map, p.x, p.y);
                var foot = new Vector3(p.x, Mathf.Min(py, baseY) - 0.4f, p.y) - n3 * (0.06f + postHalf);
                var head = new Vector3(p.x, bottom + legend.h - 0.05f, p.y) - n3 * (0.06f + postHalf);
                GBox((foot + head) * 0.5f, r3, Vector3.up, n3, postHalf, (head.y - foot.y) * 0.5f, postHalf, CityGuideFont.Galv, true);
                pu.feet.Add(p);
                pu.Take(new Mark { a = p, b = p, r = 0.6f + RoadsideOccupancy.CellPadM, bit = RoadsideOccupancy.Other });
            }
            var a = g - new Vector2(r3.x, r3.z) * (legend.w * 0.5f); var b = g + new Vector2(r3.x, r3.z) * (legend.w * 0.5f);
            pu.spans.Add((a, b));
            pu.Take(new Mark { a = a, b = b, r = 0.4f + RoadsideOccupancy.CellPadM, bit = RoadsideOccupancy.Other });
            pu.faces.Add(new Face { centre = centre, normal = n3, w = legend.w, h = legend.h, kind = Kind.Guide, viewer = viewer });
            pu.sign = new Sign
            {
                kind = Kind.Guide, pos = g, ground = gy, bottom = bottom, top = bottom + legend.h, route = -1, edge = e.index,
                legA = posts[0], legB = posts[posts.Length - 1], firstFace = 0, faces = 1, owner = pu.sign.owner,
            };
        }

        static void HangSmall(GBlock b, Vector3 centre, Vector3 r3, Vector3 n3)
        {
            const float depth = 0.05f;
            var up = Vector3.up;
            GBox(centre - n3 * (depth * 0.5f), r3, up, n3, b.w * 0.5f, b.h * 0.5f, depth * 0.5f, CityGuideFont.Alum, false);
            var bo = centre - n3 * (depth - 0.004f) - r3 * (b.w * 0.5f) + up * (b.h * 0.5f);
            if (!DbgNoBacking) GQuadAt(bo, bo + r3 * b.w, bo + r3 * b.w - up * b.h, bo - up * b.h, n3, Solid(b.yellow));
            var face = new GFace { o = centre + n3 * 0.001f - r3 * (b.w * 0.5f) + up * (b.h * 0.5f), r = r3, d = -up, n = n3 };
            legendLead = S2Glyph;
            b.Draw(face, 0f, 0f, b.w, b.h);
            legendLead = false;
        }

        // ------------------------------------------------------------------
        //  The legends
        // ------------------------------------------------------------------

        /// <summary>A route line: "NC 16 NORTH" - the designation in 16 in.
        /// letters, its cardinal word in 13 in., on one baseline.</summary>
        static GBlock RouteLine(string route)
        {
            if (string.IsNullOrEmpty(route)) return null;
            int sp = route.LastIndexOf(' ');
            string last = sp > 0 ? route.Substring(sp + 1) : "";
            if (last == "NORTH" || last == "SOUTH" || last == "EAST" || last == "WEST")
                return new GRow(new GBlock[] { new GText(route.Substring(0, sp), CapRoute, false), new GText(last, CapCard, false) }, CapCard * 0.55f);
            return new GText(route, CapRoute, false);
        }

        /// <summary>An exit's panel: the route, up to two destinations, the
        /// cardinal word; the up-diagonal arrow on the exit's side (at the
        /// exit), or "1 MILE" / "1/2 MILE" under it (an advance sign).</summary>
        static GBlock ExitLegend(ExitInfo ex, float miles, bool arrow, int exitOnly = 0, List<float> onlyXs = null)
        {
            var d = ex.data;
            var lines = new List<GBlock>();
            var r = ex.hasData ? RouteLine(d.route) : null;
            if (r != null) lines.Add(r);
            if (ex.hasData && !string.IsNullOrEmpty(d.dest1)) lines.Add(new GText(d.dest1, CapDest, false));
            if (ex.hasData && !string.IsNullOrEmpty(d.dest2)) lines.Add(new GText(d.dest2, CapDest, false));
            if (ex.hasData && !string.IsNullOrEmpty(d.cardinal)) lines.Add(new GText(d.cardinal, CapCard, false));
            if (lines.Count == 0) lines.Add(new GText("EXIT", CapDest, false));
            GBlock body = new GCol(lines, CapDest * 0.28f);
            if (arrow)
            {
                var a = new GPic(CityGuideFont.ArrowDiag, 0.78f, 0.78f, ex.left, false);
                body = ex.left ? new GRow(new GBlock[] { a, body }, 0.35f, false) : new GRow(new GBlock[] { body, a }, 0.35f, false);
            }
            else if (miles > 0f)
            {
                // (round 2) a quarter, a half, three quarters or a mile (MUTCD 2E.33:
                // a sign under half a mile out gives the nearest quarter)
                string num = miles < 0.375f ? "¼" : miles < 0.625f ? "½" : miles < 0.875f ? "¾" : "1";
                var dist = new GRow(new GBlock[] { new GText(num, CapRoute, false), new GText("MILE", CapTabWord, false) }, CapTabWord * 0.5f);
                body = new GCol(new List<GBlock> { body, dist }, CapDest * 0.45f);
            }
            GExitOnly only = null;
            if (exitOnly > 0)
            {
                // (round 2) the lane drop's EXIT ONLY panel across the sign's foot
                only = new GExitOnly(onlyXs, body.w);
                body = new GCol(new List<GBlock> { body, only }, CapDest * 0.35f);
            }
            var plate = new GPlate(body, false, RingBig, 0.22f, 0.10f);
            plate.dropRow = only;
            return plate;
        }

        /// <summary>The exit number tab ("EXIT 11 B"), with the yellow LEFT
        /// plate in it for a left exit.</summary>
        static GBlock TabLegend(ExitInfo ex)
        {
            var d = ex.data;
            var rows = new List<GBlock>();
            if (ex.left) rows.Add(new GPlate(new GText("LEFT", CapTabWord, true), true, 0.06f, 0.06f, 0.0f));
            string num = ex.hasData ? d.number : "";
            if (!string.IsNullOrEmpty(num))
                rows.Add(new GRow(new GBlock[] { new GText(num.IndexOf('-') >= 0 ? "EXITS" : "EXIT", CapTabWord, false), new GText(num, CapTabNum, false) }, CapTabWord * 0.6f));
            else if (rows.Count == 0) return null;
            return new GPlate(new GCol(rows, 0.08f), false, 0.16f, 0.14f, 0.02f);
        }

        /// <summary>The pull-through: the route going on, its control city,
        /// a down arrow over each lane going on.</summary>
        static GBlock ThruLegend(ExitInfo ex, List<float> lanes, float mid)
        {
            var d = ex.data;
            var lines = new List<GBlock>();
            var r = RouteLine(d.thru);
            if (r != null) lines.Add(r);
            if (!string.IsNullOrEmpty(d.thruCity)) lines.Add(new GText(d.thruCity, CapDest, false));
            var body = new GCol(lines, CapDest * 0.28f);
            var xs = new List<float>();
            foreach (var x in lanes) xs.Add(x - mid);
            return new GThruPanel(body, xs);
        }

        static GBlock GoreLegend(ExitInfo ex)
        {
            string num = ex.hasData ? ex.data.number : "";
            var a = new GPic(CityGuideFont.ArrowDiag, 0.34f, 0.34f, ex.left, false);
            GBlock row;
            if (!string.IsNullOrEmpty(num))
            {
                var n = new GText(num, 0.30f, false);
                row = ex.left ? new GRow(new GBlock[] { a, n }, 0.10f, false) : new GRow(new GBlock[] { n, a }, 0.10f, false);
                return new GPlate(new GCol(new List<GBlock> { new GText("EXIT", 0.20f, false), row }, 0.04f), false, 0.08f, 0.08f, 0.03f);
            }
            var w = new GText("EXIT", 0.28f, false);
            row = ex.left ? new GRow(new GBlock[] { a, w }, 0.10f, false) : new GRow(new GBlock[] { w, a }, 0.10f, false);
            return new GPlate(row, false, 0.08f, 0.08f, 0.04f);
        }

        /// <summary>W13-2 (the 1988 MUTCD): EXIT / 40 / MPH, black on yellow, 36 x 48 in.</summary>
        static GBlock AdvisoryLegend(int mph)
        {
            var col = new GCol(new List<GBlock> { new GText("EXIT", 0.15f, true), new GText(mph.ToString(), 0.36f, true), new GText("MPH", 0.15f, true) }, 0.04f);
            var p = new GPlate(col, true, 0.05f, 0.10f, 0.06f);
            p.MinSize(0.91f, 1.22f);
            return p;
        }

        // ------------------------------------------------------------------
        //  The mosaic: blocks that tile their rectangle exactly
        // ------------------------------------------------------------------

        /// <summary>A face's frame: its top-left corner, unit right and down,
        /// its normal. Every corner of every quad is <see cref="P"/> of panel
        /// coordinates, so two quads that share an edge share its corners
        /// exactly.</summary>
        internal sealed class GFace
        {
            public Vector3 o, r, d, n;
            public Vector3 P(float x, float y) => o + r * x + d * y;
            public void Quad(float x0, float y0, float x1, float y1, Vector2 uTL, Vector2 uTR, Vector2 uBR, Vector2 uBL)
            {
                if (x1 - x0 < 1e-5f || y1 - y0 < 1e-5f) return;
                GQuadUV(P(x0, y0), P(x1, y0), P(x1, y1), P(x0, y1), n, uTL, uTR, uBR, uBL);
            }
            public void Fill(float x0, float y0, float x1, float y1, bool yellow)
            {
                var u = SolidUV(yellow);
                Quad(x0, y0, x1, y1, u, u, u, u);
            }
            public void Cell(float x0, float y0, float x1, float y1, RectInt c, bool flipU, bool flipV)
            {
                UvRect(c, out float u0, out float v0, out float u1, out float v1);
                if (flipU) { float k = u0; u0 = u1; u1 = k; }
                if (flipV) { float k = v0; v0 = v1; v1 = k; }
                Quad(x0, y0, x1, y1, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
            }
        }

        /// <summary>A cell's UV corners (top-left origin pixels), half a texel in.</summary>
        static void UvRect(RectInt c, out float u0, out float vTop, out float u1, out float vBot)
        {
            u0 = (c.x + 0.5f) / CityGuideFont.AtlasW; u1 = (c.x + c.width - 0.5f) / CityGuideFont.AtlasW;
            vTop = 1f - (c.y + 0.5f) / CityGuideFont.AtlasH; vBot = 1f - (c.y + c.height - 0.5f) / CityGuideFont.AtlasH;
        }
        static Vector2 SolidUV(bool yellow)
        {
            var c = yellow ? CityGuideFont.SolidYellow : CityGuideFont.SolidGreen;
            return new Vector2((c.x + c.width * 0.5f) / CityGuideFont.AtlasW, 1f - (c.y + c.height * 0.5f) / CityGuideFont.AtlasH);
        }
        static RectInt Solid(bool yellow) => yellow ? CityGuideFont.SolidYellow : CityGuideFont.SolidGreen;

        internal abstract class GBlock
        {
            public float w, h;
            /// <summary>The baseline's depth from the top, or NaN (not text).</summary>
            public float asc = float.NaN;
            public bool yellow;
            /// <summary>Draw into (x, y, W, H) - at least w x h - filling what
            /// the content does not cover with the background.</summary>
            public virtual void Draw(GFace f, float x, float y, float W, float H)
            {
                float cx = x + (W - w) * 0.5f, cy = y + (H - h) * 0.5f;
                float x1 = cx + w, y1 = cy + h, X = x + W, Y = y + H;
                f.Fill(x, y, X, cy, yellow);          // above
                f.Fill(x, y1, X, Y, yellow);          // below
                f.Fill(x, cy, cx, y1, yellow);        // left
                f.Fill(x1, cy, X, y1, yellow);        // right
                Content(f, cx, cy, x1, y1);
            }
            protected abstract void Content(GFace f, float x0, float y0, float x1, float y1);
        }

        /// <summary>A run of letters in one size: each its cell, set edge to edge.</summary>
        internal sealed class GText : GBlock
        {
            readonly string text; readonly float cap;
            public GText(string s, float capM, bool onYellow)
            {
                text = s ?? ""; cap = capM; yellow = onYellow;
                float px = 0f;
                foreach (char ch in text) px += AdvPx(ch);
                w = px / CityGuideFont.CapPx * cap;
                h = CityGuideFont.RowPx / (float)CityGuideFont.CapPx * cap;
                asc = CityGuideFont.AscPx / (float)CityGuideFont.CapPx * cap;
            }
            int AdvPx(char ch)
            {
                int i = Index(ch);
                return i < 0 ? CityGuideFont.SpacePx : CityGuideFont.Adv[i];
            }
            int Index(char ch)
            {
                if (ch == ' ') return -1;
                if (yellow)
                {
                    char u = char.ToUpperInvariant(ch);
                    return CityGuideFont.Yellow.IndexOf(u) >= 0 ? CityGuideFont.Green.IndexOf(u) : -1;
                }
                return CityGuideFont.Green.IndexOf(ch);
            }
            protected override void Content(GFace f, float x0, float y0, float x1, float y1)
            {
                float k = cap / CityGuideFont.CapPx, x = x0;
                for (int c = 0; c < text.Length; c++)
                {
                    char ch = text[c];
                    int i = Index(ch);
                    float nx = c == text.Length - 1 ? x1 : x + AdvPx(ch) * k;
                    if (i < 0) f.Fill(x, y0, nx, y1, yellow);
                    else
                    {
                        int at;
                        short gx, gy;
                        if (yellow) { at = CityGuideFont.Yellow.IndexOf(char.ToUpperInvariant(ch)); gx = CityGuideFont.YellowAt[at * 2]; gy = CityGuideFont.YellowAt[at * 2 + 1]; }
                        else { gx = CityGuideFont.GreenAt[i * 2]; gy = CityGuideFont.GreenAt[i * 2 + 1]; }
                        f.Cell(x, y0, nx, y1, new RectInt(gx, gy, CityGuideFont.Adv[i], CityGuideFont.RowPx), false, false);
                    }
                    x = nx;
                }
            }
        }

        /// <summary>A picture cell (an arrow), flipped as asked.</summary>
        internal sealed class GPic : GBlock
        {
            readonly RectInt cell; readonly bool fu, fv;
            public GPic(RectInt c, float wm, float hm, bool flipU, bool flipV) { cell = c; w = wm; h = hm; fu = flipU; fv = flipV; }
            protected override void Content(GFace f, float x0, float y0, float x1, float y1) => f.Cell(x0, y0, x1, y1, cell, fu, fv);
        }

        /// <summary>Blocks side by side: on one baseline when all are text,
        /// else centred; the gaps filled.</summary>
        internal sealed class GRow : GBlock
        {
            readonly GBlock[] kids; readonly float gap; readonly bool baseline;
            float maxAsc;
            public GRow(GBlock[] k, float g, bool onBaseline = true)
            {
                kids = k; gap = g;
                yellow = true; foreach (var b in k) yellow &= b.yellow;
                baseline = onBaseline;
                foreach (var b in k) if (float.IsNaN(b.asc)) baseline = false;
                float desc = 0f; maxAsc = 0f;
                w = 0f;
                for (int i = 0; i < k.Length; i++)
                {
                    w += k[i].w + (i > 0 ? gap : 0f);
                    if (baseline) { maxAsc = Mathf.Max(maxAsc, k[i].asc); desc = Mathf.Max(desc, k[i].h - k[i].asc); }
                    else h = Mathf.Max(h, k[i].h);
                }
                if (baseline) { h = maxAsc + desc; asc = maxAsc; }
            }
            protected override void Content(GFace f, float x0, float y0, float x1, float y1)
            {
                float x = x0;
                for (int i = 0; i < kids.Length; i++)
                {
                    var b = kids[i];
                    if (i > 0) { float g1 = x + gap; f.Fill(x, y0, g1, y1, yellow); x = g1; }
                    float nx = i == kids.Length - 1 ? x1 : x + b.w;
                    if (baseline)
                    {
                        float top = y0 + (maxAsc - b.asc), bot = top + b.h;
                        f.Fill(x, y0, nx, top, yellow);
                        f.Fill(x, bot, nx, y1, yellow);
                        b.Draw(f, x, top, nx - x, b.h);
                    }
                    else b.Draw(f, x, y0, nx - x, y1 - y0);
                    x = nx;
                }
            }
        }

        /// <summary>Blocks stacked, each centred across the column; the gaps filled.</summary>
        internal sealed class GCol : GBlock
        {
            readonly List<GBlock> kids; readonly float gap;
            public GCol(List<GBlock> k, float g)
            {
                kids = k; gap = g;
                yellow = true; foreach (var b in k) yellow &= b.yellow;
                for (int i = 0; i < k.Count; i++) { w = Mathf.Max(w, k[i].w); h += k[i].h + (i > 0 ? gap : 0f); }
            }
            protected override void Content(GFace f, float x0, float y0, float x1, float y1)
            {
                float y = y0;
                for (int i = 0; i < kids.Count; i++)
                {
                    if (i > 0) { float g1 = y + gap; f.Fill(x0, y, x1, g1, yellow); y = g1; }
                    float ny = i == kids.Count - 1 ? y1 : y + kids[i].h;
                    kids[i].Draw(f, x0, y, x1 - x0, ny - y);
                    y = ny;
                }
            }
        }

        /// <summary>A plate: a border ring (rounded corners), padding, its
        /// content centred; at least a given size.</summary>
        internal sealed class GPlate : GBlock
        {
            readonly GBlock kid; readonly float ring, px, py;
            /// <summary>(signs round 2) Its EXIT ONLY row, whose arrows follow
            /// the plate's place over the lanes (null: none).</summary>
            public GExitOnly dropRow;
            public GPlate(GBlock k, bool onYellow, float ringM, float padX, float padY)
            {
                kid = k; yellow = onYellow; ring = ringM; px = padX; py = padY;
                w = k.w + 2f * (ring + px); h = k.h + 2f * (ring + py);
            }
            public void MinSize(float mw, float mh) { w = Mathf.Max(w, mw); h = Mathf.Max(h, mh); }
            protected override void Content(GFace f, float x0, float y0, float x1, float y1)
            {
                var corner = yellow ? CityGuideFont.RingYCorner : CityGuideFont.RingGCorner;
                var edge = yellow ? CityGuideFont.RingYEdge : CityGuideFont.RingGEdge;
                float ia = x0 + ring, ib = x1 - ring, ja = y0 + ring, jb = y1 - ring;
                f.Cell(x0, y0, ia, ja, corner, false, false);
                f.Cell(ib, y0, x1, ja, corner, true, false);
                f.Cell(x0, jb, ia, y1, corner, false, true);
                f.Cell(ib, jb, x1, y1, corner, true, true);
                UvRect(edge, out float uo, out float vt, out float ui, out float vb);
                float vm = (vt + vb) * 0.5f;
                Vector2 O = new Vector2(uo, vm), I = new Vector2(ui, vm);
                f.Quad(x0, ja, ia, jb, O, I, I, O);     // left: outer at its left
                f.Quad(ib, ja, x1, jb, I, O, O, I);     // right
                f.Quad(ia, y0, ib, ja, O, O, I, I);     // top: outer at its top
                f.Quad(ia, jb, ib, y1, I, I, O, O);     // bottom
                kid.Draw(f, ia, ja, ib - ia, jb - ja);
            }
        }

        /// <summary>The pull-through's panel: its legend over a row of down
        /// arrows, one over each lane going on (x: metres from the lanes'
        /// middle; <see cref="shift"/>: how far the panel stands off it).</summary>
        internal sealed class GThruPanel : GBlock
        {
            readonly GBlock body; readonly List<float> xs;
            public float shift;
            const float AW = 0.42f, AH = 0.72f, Ring = RingBig;
            readonly GPlate plate;
            public GThruPanel(GBlock b, List<float> laneXs)
            {
                body = b; xs = laneXs;
                float span = 0f;
                foreach (var x in xs) span = Mathf.Max(span, Mathf.Abs(x) * 2f + AW + 0.4f);
                var inner = new GArrows(this, Mathf.Max(b.w, Mathf.Min(span, 12f)), AH);
                plate = new GPlate(new GCol(new List<GBlock> { b, inner }, CapDest * 0.35f), false, Ring, 0.22f, 0.10f);
                w = plate.w; h = plate.h;
            }
            public override void Draw(GFace f, float x, float y, float W, float H) => plate.Draw(f, x, y, W, H);
            protected override void Content(GFace f, float x0, float y0, float x1, float y1) { }

            /// <summary>The arrow row: each arrow over its lane, the rest green.</summary>
            sealed class GArrows : GBlock
            {
                readonly GThruPanel p;
                public GArrows(GThruPanel owner, float wm, float hm) { p = owner; w = wm; h = hm; }
                public override void Draw(GFace f, float x, float y, float W, float H)
                {
                    float mid = x + W * 0.5f, cur = x, top = y, bot = y + H;
                    var ax = new List<float>();
                    foreach (var lx in p.xs)
                    {
                        float c = mid + lx - p.shift;
                        if (c - AW * 0.5f >= x && c + AW * 0.5f <= x + W) ax.Add(c);
                    }
                    ax.Sort();
                    foreach (var c in ax)
                    {
                        float a0 = c - AW * 0.5f, a1 = c + AW * 0.5f;
                        if (a0 < cur) continue;
                        f.Fill(cur, top, a0, bot, false);
                        f.Cell(a0, top, a1, bot, CityGuideFont.ArrowDown, false, false);
                        cur = a1;
                    }
                    f.Fill(cur, top, x + W, bot, false);
                }
                protected override void Content(GFace f, float x0, float y0, float x1, float y1) { }
            }
        }

        // ------------------------------------------------------------------
        //  The guide mesh (its own atlas and material: one more draw a tile
        //  that has freeway signs)
        // ------------------------------------------------------------------

        /// <summary>A quad of the guide mesh; ga..gd its corners' night light
        /// (signs round 2: the share of a lit panel's luminaires, UV2.x).</summary>
        internal struct GQuad { public Vector3 a, b, c, d, n; public Vector2 ua, ub, uc, ud; public float ga, gb, gc, gd; public bool lead; }

        static readonly List<Vector3> gvs = new List<Vector3>(4096), gns = new List<Vector3>(4096);
        static readonly List<Vector2> guvs = new List<Vector2>(4096);
        /// <summary>(signs round 2) the night light per vertex (UV2.x), and whether any is lit.</summary>
        static readonly List<Vector2> guv2s = new List<Vector2>(4096);
        static bool gLit, gLead;
        /// <summary>(signs round 2, the glyph fix) While a face's legend mosaic is
        /// laid: its quads LEAD in depth (UV2.y = 1, PSX/Lit's _NightVtx path pulls
        /// such a vertex a few depth steps toward the eye along its own ray - it
        /// stays on its pixel). The green backing behind a mosaic (4.6 cm on a
        /// small sign, 9.5 cm on a panel) won the depth test over the legend in
        /// bands once the depth buffer's step at the sign's distance passed that
        /// gap: I-85 exit 36's gore sign read "3 S" from 56 m.</summary>
        static bool legendLead;
        static readonly List<int> gtris = new List<int>(8192);

        static void GQuadUV(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            var q = new GQuad { a = a, b = b, c = c, d = d, n = n, ua = ua, ub = ub, uc = uc, ud = ud, lead = legendLead };
            if (nightLight != null) { q.ga = nightLight(a); q.gb = nightLight(b); q.gc = nightLight(c); q.gd = nightLight(d); }
            if (rec != null) { rec.gquads.Add(q); return; }
            GQuadNow(q);
        }

        /// <summary>a, b, c, d clockwise as seen from the side n points to.</summary>
        static void GQuadNow(GQuad q)
        {
            int v = gvs.Count;
            gvs.Add(q.a); gvs.Add(q.b); gvs.Add(q.c); gvs.Add(q.d);
            gns.Add(q.n); gns.Add(q.n); gns.Add(q.n); gns.Add(q.n);
            guvs.Add(q.ua); guvs.Add(q.ub); guvs.Add(q.uc); guvs.Add(q.ud);
            float ld = q.lead ? 1f : 0f;
            guv2s.Add(new Vector2(q.ga, ld)); guv2s.Add(new Vector2(q.gb, ld)); guv2s.Add(new Vector2(q.gc, ld)); guv2s.Add(new Vector2(q.gd, ld));
            if (q.ga + q.gb + q.gc + q.gd > 0f) gLit = true;
            if (q.lead) gLead = true;
            gtris.Add(v); gtris.Add(v + 1); gtris.Add(v + 2);
            gtris.Add(v); gtris.Add(v + 2); gtris.Add(v + 3);
        }

        /// <summary>A quad wearing a whole cell, its corners clockwise from the top left as seen from n.</summary>
        static void GQuadAt(Vector3 tl, Vector3 tr, Vector3 br, Vector3 bl, Vector3 n, RectInt cell)
        {
            UvRect(cell, out float u0, out float v0, out float u1, out float v1);
            GQuadUV(tl, tr, br, bl, n, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
        }

        /// <summary>A box on orthonormal axes, every face wearing one cell
        /// (the +z face too unless <paramref name="front"/> is false: a panel's
        /// face is its legend).</summary>
        static void GBox(Vector3 c, Vector3 x, Vector3 y, Vector3 z, float hx, float hy, float hz, RectInt cell, bool front)
        {
            x.Normalize(); y.Normalize(); z.Normalize();
            void Face(Vector3 n, Vector3 up, float hn, float hu, float hr)
            {
                var right = Vector3.Cross(n, up).normalized;
                var fc = c + n * hn;
                GQuadAt(fc - right * hr + up * hu, fc + right * hr + up * hu, fc + right * hr - up * hu, fc - right * hr - up * hu, n, cell);
            }
            if (front) Face(z, y, hz, hy, hx);
            Face(-z, y, hz, hy, hx);
            Face(x, y, hx, hy, hz);
            Face(-x, y, hx, hy, hz);
            Face(y, z, hy, hz, hx);
            Face(-y, z, hy, hz, hx);
        }

        /// <summary>A thin square bar from p to q: its four long faces (no
        /// end caps: nobody sees a 7 cm square end).</summary>
        static void Bar(Vector3 p, Vector3 q, float half, RectInt cell)
        {
            var d = q - p; float L = d.magnitude;
            if (L < 0.02f) return;
            d /= L;
            var side = Mathf.Abs(Vector3.Dot(d, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
            var x = Vector3.Cross(side, d).normalized;
            var y = Vector3.Cross(d, x).normalized;
            var c = (p + q) * 0.5f;
            UvRect(cell, out float u0, out float v0, out float u1, out float v1);
            Vector2 tl = new Vector2(u0, v0), tr = new Vector2(u1, v0), br = new Vector2(u1, v1), bl = new Vector2(u0, v1);
            float hl = L * 0.5f;
            foreach (var n in new[] { x, -x, y, -y })
            {
                // the face's "up" runs along the bar; its right is n x up
                var right = Vector3.Cross(n, d);
                var fc = c + n * half;
                GQuadUV(fc - right * half + d * hl, fc + right * half + d * hl, fc + right * half - d * hl, fc - right * half - d * hl, n, tl, tr, br, bl);
            }
        }

        /// <summary>A lacing bar from p to q lying in a truss face whose normal
        /// is f: its two faces out of and into that face (its edges are seen
        /// only at a graze).</summary>
        static void Lace(Vector3 p, Vector3 q, float half, Vector3 f, RectInt cell)
        {
            var d = q - p; float L = d.magnitude;
            if (L < 0.02f) return;
            d /= L;
            f = (f - d * Vector3.Dot(f, d)).normalized;
            var c = (p + q) * 0.5f;
            UvRect(cell, out float u0, out float v0, out float u1, out float v1);
            Vector2 tl = new Vector2(u0, v0), tr = new Vector2(u1, v0), br = new Vector2(u1, v1), bl = new Vector2(u0, v1);
            float hl = L * 0.5f;
            foreach (var n in new[] { f, -f })
            {
                var right = Vector3.Cross(n, d);
                var fc = c + n * half;
                GQuadUV(fc - right * half + d * hl, fc + right * half + d * hl, fc + right * half - d * hl, fc - right * half - d * hl, n, tl, tr, br, bl);
            }
        }

        /// <summary>A round post: an n-sided prism from a to b (vertical), its top capped.</summary>
        static void Prism(Vector3 a, Vector3 b, float r, int sides, RectInt cell, bool cap)
        {
            UvRect(cell, out float u0, out float v0, out float u1, out float v1);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                var n = (d0 + d1).normalized;
                float uA = Mathf.Lerp(u0, u1, i / (float)sides), uB = Mathf.Lerp(u0, u1, (i + 1) / (float)sides);
                // clockwise from outside (seen from out there, a1 is to the right of a0)
                GQuadUV(b + d0 * r, b + d1 * r, a + d1 * r, a + d0 * r, n,
                        new Vector2(uA, v0), new Vector2(uB, v0), new Vector2(uB, v1), new Vector2(uA, v1));
            }
            if (cap)
            {
                var uc = new Vector2((u0 + u1) * 0.5f, (v0 + v1) * 0.5f);
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                    var p0 = b + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r;
                    var p1 = b + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r;
                    GQuadUV(b, p1, p0, b, Vector3.up, uc, uc, uc, uc);
                }
            }
        }

        static Mesh EndGuide(Vector2 min)
        {
            if (gvs.Count == 0) return null;
            var origin = new Vector3(min.x, 0f, min.y);
            for (int i = 0; i < gvs.Count; i++) gvs[i] -= origin;
            var m = new Mesh { name = "guide signs" };
            if (gvs.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(gvs);
            m.SetNormals(gns);
            m.SetUVs(0, guvs);
            // (signs round 2) the lit panels' night light and the legends' lead,
            // read by PSX/Lit's _NightVtx path: ALWAYS written, zeros where
            // neither - a guide mesh with no UV2 got another channel fed to the
            // shader there (Unity's stand-in for a missing channel): with every
            // switch off round 1's panels glowed in blocks at night and their
            // legends led at random; in round 2 a tile of chevrons alone did
            m.SetUVs(2, guv2s);
            m.SetTriangles(gtris, 0, false);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>The kit's guide-sign material (the guide atlas); null without one.</summary>
        public static Material GuideMaterial()
        {
            var kit = CityKit.Get();
            return kit != null ? kit.guide : null;
        }

        /// <summary>The census the highway-signs check reads: every site, how
        /// it was decided (global data, the same on every tile).</summary>
        public static void GuideCensus(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings,
                                       System.Action<string, bool, Vector2, string, int, int, Vector3, string> each)
        {
            Setup(map, trims, buildings, 0, 0);
            EnsureSites(map);
            for (int i = 0; i < sites.Count; i++)
            {
                var st = sites[i];
                var cx = Mathf.FloorToInt(st.nominal.x / CityMeshes.TileSize); var cz = Mathf.FloorToInt(st.nominal.y / CityMeshes.TileSize);
                Setup(map, trims, buildings, cx, cz);
                var pu = PureGuide(map, trims, i);
                if (st.exitEdge < 0)
                {
                    // (round 2) a curve warning or a chevron
                    whyNot.TryGetValue(i, out string wc);
                    string sym = st.kind == GuideKind.Chevron ? "W1-8" : st.sym == SymTurn ? "W1-1" : st.sym == SymCurve ? "W1-2" : st.sym == SymRevTurn ? "W1-3" : "W1-4";
                    each(st.kind == GuideKind.Curve ? (st.side > 0 ? "Curve warning (right)" : "Curve warning (left, supplementary)") : "Chevron",
                         pu.placed, st.nominal, $"{sym} {(st.leftHand ? "L" : "R")} {st.mph} MPH on e{st.edge} '{map.edges[st.edge].name}'", pu.gquads.Count, pu.faces.Count,
                         pu.faces.Count > 0 ? pu.faces[0].centre : Vector3.zero, wc ?? "");
                    continue;
                }
                var ex = ExitOf(map, st.exitEdge);
                string what = st.kind == GuideKind.Advance ? (st.miles < 0.375f ? "advance 1/4 mile" : st.miles < 0.625f ? "advance 1/2 mile" : st.miles < 0.875f ? "advance 3/4 mile" : "advance 1 mile") : st.kind.ToString();
                if (st.kind == GuideKind.Advance && pu.placed) what += pu.kind == Kind.Guide ? " (ground)" : pu.kind == Kind.Bridge ? (pu.sign.legA == pu.sign.legB ? " (on an overpass)" : " (on its own deck)") : " (overhead)";
                if (st.kind == GuideKind.Advance && st.host > 0) what += " (shared)";
                if (st.kind == GuideKind.ExitGantry && pu.placed && pu.kind == Kind.Bridge) what += pu.sign.legA == pu.sign.legB ? " (on an overpass)" : " (on its own deck)";
                if (st.kind == GuideKind.ExitGantry && pu.placed && pu.kind == Kind.Guide) what += " (ground)";
                string legend = ex.hasData ? $"{(ex.left ? "LEFT " : "")}EXIT {ex.data.number} | {ex.data.route} | {ex.data.dest1} | {ex.data.dest2} | {ex.data.cardinal} || {ex.data.thru} {ex.data.thruCity} | adv {ex.advisoryMph}{(ex.dropped > 0 ? $" | EXIT ONLY x{ex.dropped}" : "")}" : "(no legend data)";
                whyNot.TryGetValue(i, out string wn);
                each(what, pu.placed, st.nominal, legend, pu.gquads.Count, pu.faces.Count, pu.faces.Count > 0 ? pu.faces[0].centre : Vector3.zero, wn ?? "");
            }
        }
    }
}
