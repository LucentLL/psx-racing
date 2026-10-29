using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// THE BRIDGES PASS (2026-10-29; the owner's two photos of I-77 at the
    /// Brookshire Freeway, "what bridges, highway signs, and guard rails should
    /// look like", and 2026-10-24's "many bridges (concrete) look very basic").
    /// What a Charlotte freeway structure is made of below its deck, drawn on
    /// the STRUCTURE ATLAS (tools/city/structure_atlas.py, Slot.Structure) into
    /// one RENDER-ONLY mesh a tile (<see cref="TileMeshes.structure"/>): no
    /// collider is cooked for any of it but the pier columns' capsules, so a
    /// tile's build and its collider stay what they were.
    ///
    ///   GIRDERS   under every span on structure, below the slab
    ///             (<see cref="CityElevation.DeckThick"/>): a concrete BOX
    ///             girder under a ramp or any deck up to 13 m wide (the photos'
    ///             flyovers), its webs battered in from under the deck's
    ///             overhang; wider decks stand on a row of girders 3 m apart,
    ///             precast concrete or weathering-steel plate (by structure, a
    ///             hash: the photo's lower bridge is steel). Their depth is the
    ///             structure's: 1.2 m, deeper on a deck high over everything
    ///             (to 1.7 m), shallower wherever the clearance under the deck
    ///             would leave less than <see cref="GirderCarClearM"/> over a
    ///             road below - the elevations are the solve's and never move.
    ///   PIERS     every <see cref="PierEvery"/> as before, nudged off the roads
    ///             below as before (<see cref="PierBlocked"/>, now on the real
    ///             column), as a HAMMERHEAD - one round column, a cap tapering
    ///             to its tips under the outer girders, a bearing under each
    ///             girder line - or, under a deck wider than
    ///             <see cref="HammerheadMaxM"/>, a MULTI-COLUMN BENT (two to four
    ///             columns and a cap beam). A cap is never laid over a road
    ///             passing under it (<see cref="CapBlocked"/>): the pier moves,
    ///             or stands its column with a cap no wider than the column.
    ///             Columns stand from the drawn ground (the lattice or the land,
    ///             whichever is lower, 0.6 m into it) and are solid: a capsule
    ///             the size of the drawn twelve-sided column.
    ///   ABUTMENTS where a deck meets its fill: a backwall from the slab down
    ///             into the ground across the deck, closing the air under a
    ///             deck end that used to show the land cut away beneath it.
    /// PSX_CITY_BRIDGES=0 draws the structures as they were (the A/B).
    /// </summary>
    public static partial class CityMeshes
    {
        /// <summary>PSX_CITY_BRIDGES=0: the structures as they were (the A/B).</summary>
        public static bool BridgeDetailOn = System.Environment.GetEnvironmentVariable("PSX_CITY_BRIDGES") != "0";

        /// <summary>The tile's render-only structure mesh (null where it has none).</summary>
        public static Mesh StructureMeshOf(TileMeshes tm) => tm != null ? tm.structure : null;

        /// <summary>The structure atlas's render-only bucket (Slot.Structure).</summary>
        static readonly Bucket structBucket = new Bucket();

        // ---- the atlas bands (v, Unity: 0 = the sheet's bottom row) ---------
        const float BandGirder0 = 0.879f, BandGirder1 = 0.996f;
        const float BandSteel0 = 0.754f, BandSteel1 = 0.871f;
        const float BandColumn0 = 0.504f, BandColumn1 = 0.746f;
        const float BandCap0 = 0.379f, BandCap1 = 0.496f;
        const float BandUnder0 = 0.254f, BandUnder1 = 0.371f;
        const float BandWall0 = 0.129f, BandWall1 = 0.246f;
        const float BandMisc0 = 0.006f, BandMisc1 = 0.119f;
        /// <summary>The misc row's cells, u ranges: the OM-3 object marker,
        /// a bearing pad, galvanized steel, a luminaire housing.</summary>
        const float CellOM0 = 0.004f, CellOM1 = 0.246f, CellBear0 = 0.254f, CellBear1 = 0.496f,
                    CellGalv0 = 0.504f, CellGalv1 = 0.746f, CellHouse0 = 0.754f, CellHouse1 = 0.996f;
        /// <summary>Metres of girder, cap or wall one U repeat (512 px) covers.</summary>
        const float StructRepeatM = 8f;

        // ---- the girders ---------------------------------------------------
        const float GirderStdM = 1.2f, GirderDeepM = 1.7f, GirderMinM = 0.45f;
        /// <summary>What a girder's underside leaves over a road below: a car,
        /// the chase camera over it and a hand (the deck's own clearance is
        /// the solve's <see cref="CityElevation.ClearanceM"/> to the slab).</summary>
        const float GirderCarClearM = 3.8f;
        /// <summary>A box girder's web, metres in from the deck's edge at the
        /// slab, and its batter (in per metre of depth).</summary>
        const float BoxInsetM = 1.15f, BoxBatter = 0.28f;
        /// <summary>A row of girders: centre to centre, the outer ones' centre
        /// in from the edge, and a bottom flange's width (concrete / steel).</summary>
        const float GirderPitchM = 3.0f, GirderEdgeM = 1.05f, GirderFlangeM = 0.5f, SteelFlangeM = 0.42f;
        /// <summary>Box girders under every link and every deck this wide or narrower.</summary>
        const float BoxMaxWidthM = 13.5f;

        public const byte GirderBox = 0, GirderConcrete = 1, GirderSteel = 2;
        struct GirderPlan { public byte kind; public float depth, minClear; }
        static readonly Dictionary<int, GirderPlan> girderPlans = new Dictionary<int, GirderPlan>();
        static CityMap girderMap;
        static readonly HashSet<int> gpSegs = new HashSet<int>();

        /// <summary>The edge's girders: kind and depth, decided once from the
        /// map alone (every tile draws the same structure).</summary>
        static GirderPlan PlanOf(CityMap map, CityMap.Edge e)
        {
            if (girderMap != map) { girderPlans.Clear(); girderMap = map; }
            if (girderPlans.TryGetValue(e.index, out var gp)) return gp;
            // by structure: twin carriageways of one road at one place get the same kind
            var mid = e.PointAt(e.length * 0.5f);
            uint h = 2166136261u;
            unchecked
            {
                if (!string.IsNullOrEmpty(e.name)) foreach (char ch in e.name) { h ^= ch; h *= 16777619u; }
                h ^= (uint)Mathf.FloorToInt(mid.x / 600f); h *= 16777619u;
                h ^= (uint)Mathf.FloorToInt(mid.y / 600f); h *= 16777619u;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            }
            gp.kind = e.link || e.width <= BoxMaxWidthM ? GirderBox : (h % 100u) < 40u ? GirderSteel : GirderConcrete;
            // the least air under the deck over anything below it
            float minClear = float.MaxValue;
            bool water = false;
            if (map.wspans != null) foreach (var ws in map.wspans) if (ws.edge == e.index) { water = true; break; }
            for (int k = 0; k < e.stS.Length; k++)
            {
                float s = e.stS[k];
                if (!e.ElevatedAt(s)) continue;
                var p = e.PointAt(s);
                float slab = e.YAt(s) - CityElevation.DeckThick;
                float reach = e.width * 0.5f + 20f;
                gpSegs.Clear();
                map.EdgeSegsInRect(p - Vector2.one * reach, p + Vector2.one * reach, gpSegs);
                foreach (int packed in gpSegs)
                {
                    int oi = packed >> 12, si = packed & 0xFFF;
                    if (oi == e.index) continue;
                    var o = map.edges[oi];
                    if (si + 1 >= o.pts.Length) continue;
                    Vector2 a = o.pts[si], d = o.pts[si + 1] - a;
                    float L2 = d.sqrMagnitude;
                    float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                    if (Vector2.Distance(p, a + d * t) > o.HalfMax + e.HalfMax + 0.5f) continue;
                    float oy = o.YAt(o.s[si] + Mathf.Sqrt(L2) * t);
                    if (oy > slab - 1.0f) continue;   // beside it or over it, not under
                    minClear = Mathf.Min(minClear, slab - oy);
                }
            }
            gp.minClear = minClear;
            if (water) gp.depth = 0.75f;   // a creek's short spans, and the water under them
            else if (minClear == float.MaxValue) gp.depth = GirderDeepM;
            else gp.depth = Mathf.Clamp(minClear - GirderCarClearM, GirderMinM,
                                        Mathf.Lerp(GirderStdM, GirderDeepM, Mathf.InverseLerp(6.5f, 12f, minClear)));
            girderPlans[e.index] = gp;
            return gp;
        }

        /// <summary>A point across a section, <paramref name="lat"/> metres from
        /// its L edge toward R, at the surface (tile-local).</summary>
        static Vector3 Across(Vector3 L, Vector3 R, float lat)
        {
            var d = new Vector2(R.x - L.x, R.z - L.z);
            float w = d.magnitude;
            float t = w > 1e-4f ? lat / w : 0f;
            return new Vector3(L.x + d.x * t, Mathf.LerpUnclamped(L.y, R.y, t), L.z + d.y * t);
        }

        static float PlanWidth(Vector3 L, Vector3 R) => new Vector2(R.x - L.x, R.z - L.z).magnitude;

        /// <summary>The lateral girder lines of a deck <paramref name="w"/>
        /// wide: a box's two web feet, or each girder's centre.</summary>
        static void GirderLines(byte kind, float w, float depth, List<float> lats)
        {
            lats.Clear();
            if (kind == GirderBox)
            {
                float inset = Mathf.Min(BoxInsetM, 0.22f * w) + BoxBatter * depth;
                if (w - 2f * inset < 0.8f) inset = Mathf.Max(0.2f, 0.5f * (w - 0.8f));
                lats.Add(inset); lats.Add(w - inset);
                return;
            }
            float edge = Mathf.Min(GirderEdgeM, 0.2f * w);
            int n = Mathf.Max(2, Mathf.RoundToInt((w - 2f * edge) / GirderPitchM) + 1);
            for (int k = 0; k < n; k++) lats.Add(Mathf.Lerp(edge, w - edge, k / (float)(n - 1)));
        }
        static readonly List<float> girderLatsA = new List<float>(8), girderLatsB = new List<float>(8);

        /// <summary>One span's girders (render-only), between sections A and B
        /// of a span on structure; <paramref name="i"/> is B's index, so the
        /// run's first and last spans close their ends.</summary>
        static void EmitGirders(CityMap map, CityMap.Edge e, int i)
        {
            var A = sections[i - 1]; var B = sections[i];
            var gp = PlanOf(map, e);
            float D = gp.depth, dk = CityElevation.DeckThick;
            float wA = PlanWidth(A.L, A.R), wB = PlanWidth(B.L, B.R);
            if (wA < 2f || wB < 2f) return;
            float uA = (e.vOff + e.vDir * A.s) / StructRepeatM, uB = (e.vOff + e.vDir * B.s) / StructRepeatM;
            int n = sections.Count;
            bool openA = i == 1 || !spanFlags[i - 1].elev || spanFlags[i - 1].skip;
            bool openB = i == n - 1 || !spanFlags[i + 1].elev || spanFlags[i + 1].skip;
            var bk = structBucket;
            var tanA = new Vector3(A.right.y, 0f, -A.right.x);   // along the travel... either way: the faces pick their side
            var along = new Vector3(B.L.x - A.L.x, 0f, B.L.z - A.L.z);
            if (Vector3.Dot(along, tanA) < 0f) tanA = -tanA;
            var rA = new Vector3(A.right.x, 0f, A.right.y); var rB = new Vector3(B.right.x, 0f, B.right.y);
            if (gp.kind == GirderBox)
            {
                // the webs: from under the overhang at the slab, battered in to the bottom flange
                float topIn = Mathf.Min(BoxInsetM, 0.22f * Mathf.Min(wA, wB));
                GirderLines(GirderBox, wA, D, girderLatsA); GirderLines(GirderBox, wB, D, girderLatsB);
                var tLA = Across(A.L, A.R, topIn) + Vector3.down * dk; var tRA = Across(A.L, A.R, wA - topIn) + Vector3.down * dk;
                var tLB = Across(B.L, B.R, topIn) + Vector3.down * dk; var tRB = Across(B.L, B.R, wB - topIn) + Vector3.down * dk;
                var bLA = Across(A.L, A.R, girderLatsA[0]) + Vector3.down * (dk + D); var bRA = Across(A.L, A.R, girderLatsA[1]) + Vector3.down * (dk + D);
                var bLB = Across(B.L, B.R, girderLatsB[0]) + Vector3.down * (dk + D); var bRB = Across(B.L, B.R, girderLatsB[1]) + Vector3.down * (dk + D);
                bk.Face(bLA, tLA, tLB, bLB, -(rA + rB), new Vector2(uA, BandGirder0), new Vector2(uA, BandGirder1), new Vector2(uB, BandGirder1), new Vector2(uB, BandGirder0));
                bk.Face(bRA, tRA, tRB, bRB, rA + rB, new Vector2(uA, BandGirder0), new Vector2(uA, BandGirder1), new Vector2(uB, BandGirder1), new Vector2(uB, BandGirder0));
                bk.Down(bLA, bLB, bRB, bRA, new Vector2(uA, BandUnder0), new Vector2(uB, BandUnder0), new Vector2(uB, BandUnder1), new Vector2(uA, BandUnder1));
                if (openA) EndFace(bk, bLA, tLA, tRA, bRA, -tanA, wA);
                if (openB) EndFace(bk, bLB, tLB, tRB, bRB, tanA, wB);
                return;
            }
            bool steel = gp.kind == GirderSteel;
            float half = 0.5f * (steel ? SteelFlangeM : GirderFlangeM);
            float b0 = steel ? BandSteel0 : BandGirder0, b1 = steel ? BandSteel1 : BandGirder1;
            GirderLines(gp.kind, wA, D, girderLatsA); GirderLines(gp.kind, wB, D, girderLatsB);
            int m = Mathf.Min(girderLatsA.Count, girderLatsB.Count);
            for (int g = 0; g < m; g++)
            {
                // a girder keeps its line: the same fraction of each section's width
                float fa = girderLatsA[g], fb = girderLatsA.Count == girderLatsB.Count ? girderLatsB[g] : girderLatsA[g] * wB / wA;
                var lA = Across(A.L, A.R, fa - half) + Vector3.down * dk; var hA = Across(A.L, A.R, fa + half) + Vector3.down * dk;
                var lB = Across(B.L, B.R, fb - half) + Vector3.down * dk; var hB = Across(B.L, B.R, fb + half) + Vector3.down * dk;
                var dn = Vector3.down * D;
                bk.Face(lA + dn, lA, lB, lB + dn, -(rA + rB), new Vector2(uA, b0), new Vector2(uA, b1), new Vector2(uB, b1), new Vector2(uB, b0));
                bk.Face(hA + dn, hA, hB, hB + dn, rA + rB, new Vector2(uA, b0), new Vector2(uA, b1), new Vector2(uB, b1), new Vector2(uB, b0));
                bk.Down(lA + dn, lB + dn, hB + dn, hA + dn, new Vector2(uA, BandUnder0), new Vector2(uB, BandUnder0), new Vector2(uB, BandUnder1), new Vector2(uA, BandUnder1));
                if (openA) EndFace(bk, lA + dn, lA, hA, hA + dn, -tanA, 2f * half);
                if (openB) EndFace(bk, lB + dn, lB, hB, hB + dn, tanA, 2f * half);
            }
        }

        static void EndFace(Bucket bk, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, float w)
        {
            float u = w / StructRepeatM;
            bk.Face(a, b, c, d, facing, new Vector2(0f, BandCap0), new Vector2(0f, BandCap1), new Vector2(u, BandCap1), new Vector2(u, BandCap0));
        }

        // ---- the piers -------------------------------------------------------
        /// <summary>Wider than this, a deck stands on a multi-column bent.</summary>
        const float HammerheadMaxM = 17f;
        const float BearingH = 0.22f, BearingAcrossM = 0.6f, BearingAlongM = 0.45f;
        /// <summary>A hammerhead cap's thickness along the deck, and the cap
        /// past its outermost bearing.</summary>
        const float CapAlongM = 1.7f, CapPastBearingM = 0.55f;
        /// <summary>What a cap's underside keeps over a road below (the cap
        /// may not reach over one at all within this).</summary>
        const float CapRoadClearM = 4.6f;
        const int ColumnSides = 12;

        static readonly List<float> pierLats = new List<float>(8);
        static readonly List<float> colLats = new List<float>(4);

        /// <summary>The section through arc <paramref name="s"/> of the
        /// current edge's drawn sections, interpolated (false outside them).</summary>
        static bool SectionAt(float s, out Vector3 L, out Vector3 R, out Vector2 right)
        {
            L = R = default; right = default;
            int n = sections.Count;
            if (n < 2 || s < sections[0].s || s > sections[n - 1].s) return false;
            for (int k = 1; k < n; k++)
            {
                var A = sections[k - 1]; var B = sections[k];
                if (s > B.s && k < n - 1) continue;
                float t = B.s - A.s > 1e-4f ? Mathf.Clamp01((s - A.s) / (B.s - A.s)) : 0f;
                L = Vector3.Lerp(A.L, B.L, t); R = Vector3.Lerp(A.R, B.R, t);
                right = Vector2.Lerp(A.right, B.right, t).normalized;
                return !A.collapsed && !B.collapsed;
            }
            return false;
        }

        /// <summary>
        /// The bridges pass's pier at arc <paramref name="sAt"/> of a deck (see
        /// the class summary): nudged along the deck as the old one was, never
        /// under a grounded span or inside a union's run (its owner's bent).
        /// </summary>
        static void EmitBridgePier(CityMap map, Trims trims, CityMap.Edge e, TileMeshes tm, float sAt)
        {
            var gp = PlanOf(map, e);
            float dk = CityElevation.DeckThick;
            bool haveSlim = false; float slimS = 0f;
            for (int pass = 0; pass < 2; pass++)
                foreach (var dS in PierNudges)
                {
                    float s = pass == 0 ? sAt + dS : slimS;
                    if (pass == 1 && (!haveSlim || dS != 0f)) continue;
                    if (s < 2f || s > e.length - 2f) continue;
                    if (InUnionDeckRun(trims, e.index, s) || !e.ElevatedAt(s)) continue;
                    if (!SectionAt(s, out var L, out var R, out var right)) continue;
                    float w = PlanWidth(L, R);
                    if (w < 2.5f) continue;
                    var tan = e.TangentAt(s);
                    var o = tm.origin;
                    Vector2 World(Vector3 v) => new Vector2(v.x + o.x, v.z + o.z);
                    float Seat(float lat) => Across(L, R, lat).y - dk - gp.depth - BearingH;
                    GirderLines(gp.kind, w, gp.depth, pierLats);
                    float lat0 = Mathf.Max(0.25f, pierLats[0] - CapPastBearingM), lat1 = Mathf.Min(w - 0.25f, pierLats[pierLats.Count - 1] + CapPastBearingM);
                    bool bent = w > HammerheadMaxM;
                    float capW = lat1 - lat0;
                    float rootD = bent ? 1.15f : Mathf.Clamp(0.9f + 0.1f * capW, 1.3f, 2.2f);
                    float tipD = bent ? rootD : 0.55f * rootD;
                    float radius = bent ? Mathf.Clamp(0.45f + 0.012f * w, 0.55f, 0.7f) : Mathf.Clamp(0.45f + 0.03f * w, 0.6f, 1.0f);
                    colLats.Clear();
                    if (bent)
                    {
                        int nc = Mathf.Clamp(Mathf.RoundToInt(capW / 6.5f), 2, 4);
                        for (int k = 0; k < nc; k++) colLats.Add(Mathf.Lerp(lat0 + 0.9f, lat1 - 0.9f, k / (float)(nc - 1)));
                    }
                    else colLats.Add(0.5f * (lat0 + lat1));
                    bool slim = pass == 1;
                    if (slim)
                    {
                        // one column under the deck's middle with its own cap: no
                        // cantilever over the road beside it
                        float midLat = 0.5f * (lat0 + lat1), best = colLats[0];
                        foreach (float cl in colLats) if (Mathf.Abs(cl - midLat) < Mathf.Abs(best - midLat)) best = cl;
                        colLats.Clear(); colLats.Add(best);
                        radius = Mathf.Max(radius, 0.6f);
                        lat0 = best - radius - 0.35f; lat1 = best + radius + 0.35f;
                        rootD = tipD = 1.0f; capW = lat1 - lat0; bent = false;
                    }
                    // every column clear of the roads below, standing on enough height
                    bool ok = true;
                    float minSeat = Mathf.Min(Seat(lat0), Seat(lat1));
                    foreach (float cl in colLats)
                    {
                        var cp = World(Across(L, R, cl));
                        float top = Mathf.Lerp(Seat(lat0), Seat(lat1), Mathf.InverseLerp(lat0, lat1, cl)) - rootD;
                        float gy = Mathf.Min(CityElevation.GroundY(map, cp.x, cp.y), LatticeY(map, cp.x, cp.y));
                        if (top - gy < (bent ? 0.6f : 1.0f)) { ok = false; break; }
                        if (PierBlocked(map, e, cp, tan, radius, e.YAt(s) - dk)) { ok = false; break; }
                    }
                    if (!ok) continue;
                    var capMid = World(Across(L, R, 0.5f * (lat0 + lat1)));
                    if (!slim && CapBlocked(map, e, capMid, right, tan, 0.5f * capW, 0.5f * CapAlongM, minSeat - rootD))
                    {
                        if (!haveSlim) { haveSlim = true; slimS = s; }
                        continue;
                    }
                    // ---- stand it ----
                    var bk = structBucket;
                    var r3 = new Vector3(right.x, 0f, right.y);
                    var f3 = new Vector3(tan.x, 0f, tan.y);
                    var ha = f3 * (0.5f * CapAlongM);
                    Vector3 P0 = Across(L, R, lat0), P1 = Across(L, R, lat1);
                    float s0 = Seat(lat0), s1 = Seat(lat1);
                    EmitCap(bk, new Vector3(P0.x, s0, P0.z), new Vector3(P1.x, s1, P1.z), ha, r3, f3, rootD, tipD,
                            bent || slim ? 0f : radius + 0.3f, Mathf.InverseLerp(lat0, lat1, colLats[0]));
                    // a bearing under each girder line the cap reaches
                    foreach (float bl in pierLats)
                    {
                        if (bl < lat0 + 0.2f || bl > lat1 - 0.2f) continue;
                        var bp = Across(L, R, bl);
                        float y1 = bp.y - dk - gp.depth;
                        EmitBlock(bk, new Vector3(bp.x, y1 - BearingH, bp.z), new Vector3(bp.x, y1, bp.z), r3 * (0.5f * BearingAcrossM), f3 * (0.5f * BearingAlongM),
                                  CellBear0, CellBear1, BandMisc0, BandMisc1);
                    }
                    foreach (float cl in colLats)
                    {
                        var cpl = Across(L, R, cl);
                        var cp = World(cpl);
                        float top = Mathf.Lerp(s0, s1, Mathf.InverseLerp(lat0, lat1, cl)) - rootD;
                        float gy = Mathf.Min(CityElevation.GroundY(map, cp.x, cp.y), LatticeY(map, cp.x, cp.y));
                        EmitRoundColumn(tm, new Vector3(cpl.x, gy - 0.6f, cpl.z), top + 0.05f, radius);
                        if (pierLog != null) RecordPier(e, sAt, s, cp, gy - 0.6f, top, radius, tan);   // AuditView's record (audits only)
                    }
                    return;
                }
        }

        /// <summary>
        /// Would a pier cap - the plan rectangle centred on <paramref name="c"/>,
        /// <paramref name="halfAcross"/> along <paramref name="across"/> and
        /// <paramref name="halfAlong"/> along the deck - stand over a road
        /// passing under it with less than <see cref="CapRoadClearM"/> between
        /// the road and the cap's underside at <paramref name="capBottomY"/>?
        /// Roads beside the deck at its own level are its neighbours, not under it.
        /// </summary>
        static bool CapBlocked(CityMap map, CityMap.Edge deck, Vector2 c, Vector2 across, Vector2 tan, float halfAcross, float halfAlong, float capBottomY)
        {
            pierScratch.Clear();
            float reach = halfAcross + 30f;
            map.EdgeSegsInRect(c - Vector2.one * reach, c + Vector2.one * reach, pierScratch);
            float deckY = deck.YAt(deck.length * 0.5f);
            foreach (var packed in pierScratch)
            {
                int oi = packed >> 12, si = packed & 0xFFF;
                if (oi == deck.index) continue;
                var o = map.edges[oi];
                if (si + 1 >= o.pts.Length) continue;
                Vector2 a = o.pts[si], d = o.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(c - a, d) / L2) : 0f;
                float at = o.s[si] + Mathf.Sqrt(L2) * t;
                float oy = o.YAt(at);
                if (oy < capBottomY - CapRoadClearM) continue;            // far enough below
                if (oy > capBottomY + 1.5f) continue;                      // beside the deck, at its level
                float dist = SegRectDistance(a - c, o.pts[si + 1] - c, across, tan, halfAcross, halfAlong);
                LineModel.Extents(o, at, out float exM, out float exP);
                if (dist <= Mathf.Max(o.HalfMax, Mathf.Max(exM, exP)) + 0.5f) return true;
            }
            return false;
        }

        /// <summary>A pier cap from tip <paramref name="t0"/> to tip
        /// <paramref name="t1"/> (each its TOP centre, tile-local),
        /// <paramref name="ha"/> half its thickness along the deck: its top,
        /// sides, tips and underside - a hammerhead's tapering from
        /// <paramref name="rootD"/> deep over the column (<paramref name="rootHalf"/>
        /// either side of the root at <paramref name="rootT"/> of the way
        /// across) to <paramref name="tipD"/> at the tips; a bent's beam
        /// (rootHalf 0) one depth throughout.</summary>
        static void EmitCap(Bucket bk, Vector3 t0, Vector3 t1, Vector3 ha, Vector3 r3, Vector3 f3, float rootD, float tipD, float rootHalf, float rootT)
        {
            float w = Vector3.Distance(new Vector3(t0.x, 0f, t0.z), new Vector3(t1.x, 0f, t1.z));
            if (w < 0.3f) return;
            // the profile along the cap: (fraction across, depth)
            float rA = rootHalf > 0f ? Mathf.Clamp01(rootT - rootHalf / w) : 0f;
            float rB = rootHalf > 0f ? Mathf.Clamp01(rootT + rootHalf / w) : 1f;
            var xs = rootHalf > 0f ? new[] { 0f, rA, rB, 1f } : new[] { 0f, 1f };
            var ds = rootHalf > 0f ? new[] { tipD, rootD, rootD, tipD } : new[] { rootD, rootD };
            Vector3 Top(float x) => Vector3.Lerp(t0, t1, x);
            float U(float x) => x * w / StructRepeatM;
            for (int k = 0; k + 1 < xs.Length; k++)
            {
                float x0 = xs[k], x1 = xs[k + 1];
                if (x1 - x0 < 1e-4f) continue;
                Vector3 a0 = Top(x0), a1 = Top(x1);
                var d0 = Vector3.down * ds[k]; var d1 = Vector3.down * ds[k + 1];
                // the two long faces (deck-ward and back), the underside
                bk.Face(a0 - ha + d0, a0 - ha, a1 - ha, a1 - ha + d1, -f3, new Vector2(U(x0), BandCap0), new Vector2(U(x0), BandCap1), new Vector2(U(x1), BandCap1), new Vector2(U(x1), BandCap0));
                bk.Face(a0 + ha + d0, a0 + ha, a1 + ha, a1 + ha + d1, f3, new Vector2(U(x0), BandCap0), new Vector2(U(x0), BandCap1), new Vector2(U(x1), BandCap1), new Vector2(U(x1), BandCap0));
                bk.Face(a0 - ha + d0, a1 - ha + d1, a1 + ha + d1, a0 + ha + d0, Vector3.down + (ds[k] != ds[k + 1] ? (k == 0 ? -r3 : r3) * 0.5f : Vector3.zero),
                        new Vector2(U(x0), BandUnder0), new Vector2(U(x1), BandUnder0), new Vector2(U(x1), BandUnder1), new Vector2(U(x0), BandUnder1));
                // the top, seen between the girders
                bk.Up(a0 - ha, a1 - ha, a1 + ha, a0 + ha, new Vector2(U(x0), BandCap0), new Vector2(U(x1), BandCap0), new Vector2(U(x1), BandCap1), new Vector2(U(x0), BandCap1));
            }
            // the tips
            float ua = 2f * ha.magnitude / StructRepeatM;
            bk.Face(t0 - ha + Vector3.down * ds[0], t0 - ha, t0 + ha, t0 + ha + Vector3.down * ds[0], -r3,
                    new Vector2(0f, BandCap0), new Vector2(0f, BandCap1), new Vector2(ua, BandCap1), new Vector2(ua, BandCap0));
            float dl = ds[ds.Length - 1];
            bk.Face(t1 - ha + Vector3.down * dl, t1 - ha, t1 + ha, t1 + ha + Vector3.down * dl, r3,
                    new Vector2(0f, BandCap0), new Vector2(0f, BandCap1), new Vector2(ua, BandCap1), new Vector2(ua, BandCap0));
        }

        /// <summary>A small box from <paramref name="bottom"/> to
        /// <paramref name="top"/> (centres), its four sides (a bearing pad, a
        /// marker post): half extents along <paramref name="hx"/> and
        /// <paramref name="hz"/>, one atlas cell.</summary>
        static void EmitBlock(Bucket bk, Vector3 bottom, Vector3 top, Vector3 hx, Vector3 hz, float u0, float u1, float v0, float v1)
        {
            var c = new[] { hx + hz, hx - hz, -hx - hz, -hx + hz };
            for (int k = 0; k < 4; k++)
            {
                var a = c[k]; var b = c[(k + 1) % 4];
                var n = (a + b); if (n.sqrMagnitude < 1e-8f) n = Vector3.Cross(b - a, Vector3.up);
                bk.Face(bottom + a, top + a, top + b, bottom + b, n, new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0));
            }
        }

        /// <summary>A round pier column (twelve sides, render-only) from its
        /// buried foot to <paramref name="topY"/>, and its solid: a capsule the
        /// size of the drawn column (CityWorld.Attach, <see cref="SolidBox.round"/>).</summary>
        static void EmitRoundColumn(TileMeshes tm, Vector3 foot, float topY, float r)
        {
            float h = topY - foot.y;
            if (h < 0.3f) return;
            var bk = structBucket;
            float circ = 2f * Mathf.PI * r;
            for (int k = 0; k < ColumnSides; k++)
            {
                float a0 = k * 2f * Mathf.PI / ColumnSides, a1 = (k + 1) * 2f * Mathf.PI / ColumnSides;
                var o0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r; var o1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r;
                float u0 = circ * k / ColumnSides / StructRepeatM, u1 = circ * (k + 1) / ColumnSides / StructRepeatM;
                var top = new Vector3(foot.x, topY, foot.z);
                bk.Face(foot + o0, top + o0, top + o1, foot + o1, o0 + o1,
                        new Vector2(u0, BandColumn0), new Vector2(u0, BandColumn1), new Vector2(u1, BandColumn1), new Vector2(u1, BandColumn0));
            }
            // the solid: the twelve-gon's mean radius (within 1.5 cm of every face)
            tm.solids.Add(new SolidBox
            {
                center = new Vector3(foot.x, foot.y + 0.5f * h, foot.z),
                size = new Vector3(2f * r, h, 2f * r),
                yawDeg = 0f,
                round = true,
                radius = r * 0.983f,
            });
        }

        // ---- the abutments ----------------------------------------------------
        static readonly List<float> abutEnds = new List<float>(4);

        /// <summary>The backwall at each place this edge's deck meets its fill
        /// (inside the edge, and at a node where it is on the ground and the
        /// road it carries on into is on structure), owned by the tile the
        /// point is in.</summary>
        static void EmitAbutments(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, Vector2 min, Vector2 max)
        {
            if (e.stS.Length < 2) return;
            abutEnds.Clear();
            InternalStructureEnds(e, abutEnds);
            for (int end = 0; end < 2; end++)
            {
                int node = end == 0 ? e.a : e.b;
                float sEnd = end == 0 ? 0f : e.length;
                if (e.ElevatedAt(sEnd)) continue;
                var o = ThroughPartner(map, e, node);
                if (o != null && o.stS.Length >= 2 && o.ElevatedAt(o.a == node ? 0f : o.length)) abutEnds.Add(end == 0 ? 0.01f : e.length - 0.01f);
            }
            float dk = CityElevation.DeckThick;
            foreach (float s in abutEnds)
            {
                var p = e.PointAt(s);
                if (p.x < min.x || p.x >= max.x || p.y < min.y || p.y >= max.y) continue;
                // which way the bridge is: on this edge, or past its end
                int dir;
                if (s <= 0.02f) dir = -1;
                else if (s >= e.length - 0.02f) dir = 1;
                else dir = e.ElevatedAt(s + 2f) ? 1 : e.ElevatedAt(s - 2f) ? -1 : 0;
                if (dir == 0) continue;
                if (!SectionAt(Mathf.Clamp(s, sections[0].s, sections[sections.Count - 1].s), out var L, out var R, out var right)) continue;
                var r3 = new Vector3(right.x, 0f, right.y);
                var wl = L - r3 * 0.35f; var wr = R + r3 * 0.35f;
                var o = tm.origin;
                float g = float.MaxValue;
                for (int k = 0; k <= 4; k++)
                {
                    var q = Vector3.Lerp(wl, wr, k / 4f);
                    g = Mathf.Min(g, Mathf.Min(LatticeY(map, q.x + o.x, q.z + o.z), CityElevation.GroundY(map, q.x + o.x, q.z + o.z)));
                }
                float topL = wl.y - dk, topR = wr.y - dk;
                if (Mathf.Min(topL, topR) - g < 0.5f || Mathf.Max(topL, topR) - g > 9f) continue;   // on its fill, or a deck ending in the air
                var tan = e.TangentAt(Mathf.Clamp(s, 0f, e.length));
                var face = new Vector2(tan.x, tan.y) * dir;
                float u = PlanWidth(wl, wr) / StructRepeatM;
                structBucket.WallSloped(wl, wr, g - 0.8f, topL, g - 0.8f, topR, face, 0f, u, BandWall0, BandWall1);
            }
        }
            // ---- W-BEAM RUNS (the bridges pass, item 2) ----------------------------
        //
        // A warranted DROP on the ground (RoadsideRules: a fill the verge cannot
        // grade to a recoverable slope) was guarded by the deck's concrete
        // parapet carried along the road. Real roads guard a fill with W-beam on
        // posts (the owner's photo: along the ramp's outside, posts with
        // blockouts, the end turned down with a yellow and black marker). Now a
        // grounded rail span that is not an approach (the parapet's own 20 m
        // past a structure end), not on a retaining face, not in a squeeze strip
        // and not beside a deck's or a retaining face's rail is a W-BEAM RUN:
        //   the beam on posts, its collider the drawn beam (as the lead-ins');
        //   its ends a W-beam's: on open roadside the terminal (flared and
        //   turned down, CityMeshes.EmitWBeam), else turned down inside the run;
        //   where it meets a parapet run on the ground the parapet's end slopes
        //   down over TaperLenM with the beam bolted along it (the lead-in's
        //   transition), so no square concrete end faces anyone.
        // Every decision is the span's own flags and its neighbours', all inside
        // the flag window, so every tile draws the same runs.
        // PSX_CITY_WBEAM_RUNS=0 keeps the concrete (the A/B).
        static readonly bool wbRunsOff = System.Environment.GetEnvironmentVariable("PSX_CITY_WBEAM_RUNS") == "0";
        public static bool WBeamRunsOn => BridgeDetailOn && !wbRunsOff;
        static readonly List<bool> wbRunL = new List<bool>(64), wbRunR = new List<bool>(64);
        static readonly List<bool> wbConcL = new List<bool>(64), wbConcR = new List<bool>(64);
        /// <summary>Per section: metres to a W-beam run's turned-down end (NaN: none near).</summary>
        static readonly List<float> wbDownL = new List<float>(64), wbDownR = new List<float>(64);
        static List<bool> WbRun(int side) => side < 0 ? wbRunL : wbRunR;
        static List<float> WbDown(int side) => side < 0 ? wbDownL : wbDownR;
        static bool WBeamRunAt(int side, int i) { var r = WbRun(side); return WBeamRunsOn && i >= 1 && i < r.Count && r[i]; }

        static void ResetWBeamRuns(int n)
        {
            wbRunL.Clear(); wbRunR.Clear(); wbConcL.Clear(); wbConcR.Clear(); wbDownL.Clear(); wbDownR.Clear();
            for (int k = 0; k < n; k++) { wbRunL.Add(false); wbRunR.Add(false); wbConcL.Add(false); wbConcR.Add(false); wbDownL.Add(float.NaN); wbDownR.Add(float.NaN); }
        }

        static void WBeamConcrete(int side, int span)
        {
            var c = side < 0 ? wbConcL : wbConcR;
            if (span >= 0 && span < c.Count) c[span] = true;
        }

        static bool WBeamEligible(int i, int side)
        {
            int n = sections.Count;
            if (i < 1 || i >= n) return false;
            var f = spanFlags[i]; var sf = f[side];
            if (!sf.rail || f.elev || f.approach || sf.retain || f.wedge || f.skip || sf.gap || sf.union || !f.decided) return false;
            if ((side < 0 ? wbConcL : wbConcR)[i]) return false;
            var A = sections[i - 1]; var B = sections[i];
            if (A.Strip(side) >= 0f || B.Strip(side) >= 0f || A.collapsed || B.collapsed || A.elev || B.elev) return false;
            // never beside a rail on a retaining face or a deck: those keep their concrete to the end
            for (int j = i - 1; j <= i + 1; j += 2)
                if (j >= 1 && j < n && spanFlags[j][side].rail && (spanFlags[j].elev || spanFlags[j][side].retain)) return false;
            return true;
        }

        static void MarkWBeamRuns(bool again)
        {
            if (!WBeamRunsOn) return;
            int n = sections.Count;
            for (int side = -1; side <= 1; side += 2)
            {
                var run = WbRun(side);
                for (int i = 1; i < n; i++) run[i] = WBeamEligible(i, side);
            }
        }

        /// <summary>A W-beam run's end at section <paramref name="k"/> (the open
        /// roadside toward <paramref name="dir"/>): its terminal laid on that
        /// roadside (<paramref name="leadIn"/>, the lead-in's own lists, from
        /// u = 0 at the run's end), or its last WBeamDownM turned down. False
        /// when the rail there is not a W-beam run (a parapet's end: the caller's).</summary>
        static bool PlanWBeamRunEnd(int side, int k, int dir, bool leadIn, TileMeshes count)
        {
            if (!WBeamRunsOn) return false;
            int railSpan = dir > 0 ? k : k + 1;
            var run = WbRun(side);
            if (railSpan < 1 || railSpan >= run.Count || !run[railSpan]) return false;
            int n = sections.Count;
            if (leadIn)
            {
                var wb = WbList(side, dir);
                for (int q = k; q >= 0 && q < n; q += dir)
                {
                    float u = Mathf.Abs(sections[q].s - sections[k].s);
                    wb[q] = u;
                    if (u >= WBeamLenM) break;
                }
                if (count != null) count.railEndsWBeam++;
            }
            else
            {
                var dn = WbDown(side);
                for (int j = k; j >= 0 && j < n; j -= dir)
                {
                    float d = Mathf.Abs(sections[j].s - sections[k].s);
                    dn[j] = float.IsNaN(dn[j]) ? d : Mathf.Min(dn[j], d);
                    if (d >= WBeamDownM) break;
                    int spb = dir > 0 ? j : j + 1;
                    if (spb < 1 || spb >= n || !run[spb]) break;
                }
                if (count != null) count.railEndsInside++;
            }
            return true;
        }

        /// <summary>Where a W-beam run and a parapet run meet on the ground: the
        /// parapet's end sloped down over TaperLenM, the beam on along it.</summary>
        static void WBeamHandOvers(int w0, int w1)
        {
            if (!WBeamRunsOn) return;
            int n = sections.Count;
            for (int side = -1; side <= 1; side += 2)
            {
                var run = WbRun(side);
                var taper = side < 0 ? taperL : taperR;
                for (int k = Mathf.Max(1, w0); k <= Mathf.Min(n - 2, w1); k++)
                {
                    var fl = spanFlags[k]; var fr = spanFlags[k + 1];
                    if (!fl.decided || !fr.decided || !fl[side].rail || !fr[side].rail || run[k] == run[k + 1]) continue;
                    int conc = run[k] ? k + 1 : k;
                    if (spanFlags[conc].elev || spanFlags[conc][side].retain) continue;
                    int dir = run[k + 1] ? 1 : -1;   // from the parapet toward the beam
                    var wb = WbList(side, dir);
                    // the parapet's sloped end is closed (a curb-high face, as at any sloped end)
                    var fc = spanFlags[conc]; var sc = fc[side];
                    if (conc == k) sc.capEnd = true; else sc.capStart = true;
                    fc[side] = sc; spanFlags[conc] = fc;
                    for (int j = k; j >= 0 && j < n; j -= dir)
                    {
                        float d = Mathf.Abs(sections[j].s - sections[k].s);
                        taper[j] = Mathf.Min(taper[j], Mathf.Clamp01(d / TaperLenM));
                        wb[j] = TaperLenM - d;
                        if (d >= TaperLenM) break;
                        int spb = dir > 0 ? j : j + 1;
                        if (spb < 1 || spb >= n || !spanFlags[spb][side].rail || run[spb]) break;
                    }
                }
            }
        }

        /// <summary>One span of a W-beam run: the beam on the rail's line (its
        /// flare and ease included) WBeamInsetM behind where the parapet's face
        /// would be, in pieces of WBeamStepM with its turned-down ends; posts
        /// every WBeamPostM of the road's chain distance; its collider the
        /// drawn beam (as a lead-in's); an object marker before a turned-down end.</summary>
        static void EmitWBeamRun(TileMeshes tm, CityMap.Edge e, int i, int side, float outAm, float outBm)
        {
            var A = sections[i - 1]; var B = sections[i];
            int n = sections.Count;
            var run = WbRun(side); var dn = WbDown(side);
            Vector3 eA = A.Edge(side), eB = B.Edge(side);
            Vector2 oA = A.Out(side), oB = B.Out(side);
            float len = Vector2.Distance(new Vector2(eA.x, eA.z), new Vector2(eB.x, eB.z));
            if (len < 0.05f) return;
            float dA = dn[i - 1], dB = dn[i];
            bool openA = (i == 1 || !run[i - 1]) && OpenEnd(side, i - 1);
            bool openB = (i == n - 1 || !run[i + 1]) && OpenEnd(side, i);
            void At(float t, out Vector3 front, out Vector2 outw, out float top)
            {
                var ed = Vector3.Lerp(eA, eB, t);
                outw = Vector2.Lerp(oA, oB, t).normalized;
                float off = Mathf.Lerp(outAm, outBm, t) - (RailW + WBeamInsetM);
                front = ed + new Vector3(outw.x, 0f, outw.y) * off;
                float d;
                if (!float.IsNaN(dA) && !float.IsNaN(dB)) d = Mathf.Lerp(dA, dB, t);
                else if (!float.IsNaN(dA)) d = dA + t * len;
                else if (!float.IsNaN(dB)) d = dB + (1f - t) * len;
                else d = float.MaxValue;
                top = ed.y + (d >= WBeamDownM ? WBeamTopM : Mathf.Lerp(-0.15f, WBeamTopM, Mathf.Clamp01(d / WBeamDownM)));
                // a turned-down end eases out over the verge, so its tip goes into
                // the ground and never into the shoulder's pavement
                if (d < WBeamDownM + 4f) front += new Vector3(outw.x, 0f, outw.y) * Mathf.Lerp(0.65f, 0f, Mathf.Clamp01(d / (WBeamDownM + 4f)));
            }
            int pieces = Mathf.Max(1, Mathf.CeilToInt(len / WBeamStepM));
            for (int q = 0; q < pieces; q++)
            {
                float t0 = (float)q / pieces, t1 = (float)(q + 1) / pieces;
                At(t0, out var fa, out var na, out float ta);
                At(t1, out var fb, out var nb, out float tb);
                tm.wbeams.Add(new WBeamPiece
                {
                    a = fa, b = fb, outA = na, outB = nb,
                    botA = ta - WBeamDepthM, topA = ta, botB = tb - WBeamDepthM, topB = tb,
                    endA = q == 0 && openA, endB = q == pieces - 1 && openB,
                });
                WBeamColliderPiece(fa, fb, na, nb, ta, tb);
            }
            // posts at the road's chain distance, so a run reads the same across spans and tiles
            float mA = e.vOff + e.vDir * A.s, mB = e.vOff + e.vDir * B.s;
            float lo = Mathf.Min(mA, mB), hi = Mathf.Max(mA, mB);
            for (int m = Mathf.FloorToInt(lo / WBeamPostM) + 1; m * WBeamPostM <= hi + 1e-4f; m++)
            {
                float t = Mathf.InverseLerp(mA, mB, m * WBeamPostM);
                At(t, out var fp, out var np, out float tp);
                if (tp - Mathf.Lerp(eA.y, eB.y, t) < 0.35f) continue;   // turned down here
                tm.wbeamPosts.Add(new WBeamPost { at = fp, outward = np, top = tp - 0.03f });
            }
            // the marker before a turned-down end (the run ends at that section)
            for (int end = 0; end < 2; end++)
            {
                float d = end == 0 ? dA : dB;
                if (float.IsNaN(d) || d > 0.01f) continue;
                float tEnd = end == 0 ? 0f : 1f;
                float tMk = Mathf.Clamp01(end == 0 ? (WBeamDownM + 0.4f) / len : 1f - (WBeamDownM + 0.4f) / len);
                At(tMk, out var fm, out var nm, out _);
                At(tEnd, out var fe, out _, out _);
                EmitObjectMarker(fm, nm, new Vector3(fe.x - fm.x, 0f, fe.z - fm.z), Mathf.Lerp(eA.y, eB.y, tMk));
            }
        }

        /// <summary>Is the open roadside beyond section <paramref name="k"/>
        /// (on this side) bare - no lead-in or hand-over beam carrying on?</summary>
        static bool OpenEnd(int side, int k) =>
            float.IsNaN(WbList(side, 1)[k]) && float.IsNaN(WbList(side, -1)[k]);

        /// <summary>The collider of one W-beam piece: the drawn beam itself
        /// (CityPoles.EmitWBeam's), its face, back, top, underside and ends -
        /// the lead-ins' rule (INVISIBLE COLLIDERS, 2026-10-04).</summary>
        static void WBeamColliderPiece(Vector3 fa, Vector3 fb, Vector2 na, Vector2 nb, float ta, float tb)
        {
            var bk = guardBucket;
            var na3 = new Vector3(na.x, 0f, na.y); var nb3 = new Vector3(nb.x, 0f, nb.y);
            Vector3 fcA = fa + na3 * WBeamFaceM, fcB = fb + nb3 * WBeamFaceM;
            Vector3 baA = fa + na3 * WBeamBackM, baB = fb + nb3 * WBeamBackM;
            float lowA = ta - WBeamDepthM, lowB = tb - WBeamDepthM;
            bk.WallSloped(fcA, fcB, lowA, ta, lowB, tb, -(na + nb), 0f, 1f, 0f, 1f);
            bk.WallSloped(baA, baB, lowA, ta, lowB, tb, na + nb, 0f, 1f, 0f, 1f);
            bk.Up(new Vector3(fcA.x, ta, fcA.z), new Vector3(fcB.x, tb, fcB.z), new Vector3(baB.x, tb, baB.z), new Vector3(baA.x, ta, baA.z),
                  Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            bk.Down(new Vector3(fcA.x, lowA, fcA.z), new Vector3(fcB.x, lowB, fcB.z), new Vector3(baB.x, lowB, baB.z), new Vector3(baA.x, lowA, baA.z),
                    Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            var along = new Vector3(fb.x - fa.x, 0f, fb.z - fa.z);
            bk.Face(new Vector3(fa.x, lowA, fa.z), new Vector3(fa.x, ta, fa.z), new Vector3(baA.x, ta, baA.z), new Vector3(baA.x, lowA, baA.z),
                    -along, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            bk.Face(new Vector3(fb.x, lowB, fb.z), new Vector3(fb.x, tb, fb.z), new Vector3(baB.x, tb, baB.z), new Vector3(baB.x, lowB, baB.z),
                    along, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        }

        /// <summary>
        /// A 1999 guardrail end's OBJECT MARKER (MUTCD OM-3, 12 x 36 in, yellow
        /// and black stripes): a plate on a thin post just behind the beam at
        /// <paramref name="beamFront"/> (tile-local, road-edge height), facing
        /// both ways along the road, before the turned-down end it marks.
        /// Render-only - a breakaway plate, not a wall.
        /// </summary>
        static void EmitObjectMarker(Vector3 beamFront, Vector2 outward, Vector3 towardEnd, float roadY)
        {
            if (towardEnd.sqrMagnitude < 1e-6f) return;
            towardEnd.Normalize();
            var o = new Vector3(outward.x, 0f, outward.y);
            var c = new Vector3(beamFront.x, roadY, beamFront.z) + o * 0.30f;
            float bottom = roadY + 0.55f, top = roadY + 1.45f;
            var bk = structBucket;
            // the plate: both faces, the stripes on each
            var hw = o * 0.155f;
            var lo = new Vector3(c.x, bottom, c.z); var hi = new Vector3(c.x, top, c.z);
            bk.Face(lo - hw, hi - hw, hi + hw, lo + hw, -towardEnd, new Vector2(CellOM0, BandMisc0), new Vector2(CellOM0, BandMisc1), new Vector2(CellOM1, BandMisc1), new Vector2(CellOM1, BandMisc0));
            var lo2 = lo + towardEnd * 0.01f; var hi2 = hi + towardEnd * 0.01f;
            bk.Face(lo2 - hw, hi2 - hw, hi2 + hw, lo2 + hw, towardEnd, new Vector2(CellOM0, BandMisc0), new Vector2(CellOM0, BandMisc1), new Vector2(CellOM1, BandMisc1), new Vector2(CellOM1, BandMisc0));
            // its post, galvanized, into the verge
            EmitBlock(bk, new Vector3(c.x, roadY - 0.5f, c.z) + towardEnd * 0.045f, new Vector3(c.x, bottom, c.z) + towardEnd * 0.045f,
                      o * 0.035f, towardEnd * 0.035f, CellGalv0, CellGalv1, BandMisc0, BandMisc1);
        }
            // ---- HIGH MASTS (the bridges pass, item 4) -----------------------------
        //
        // The tall pole in the owner's first photo: a 1990s interchange is lit
        // by HIGH MASTS - a galvanized pole 30-37 m tall with a ring of
        // high-pressure sodium floods at its top - standing in the infields the
        // ramps sweep round, well out of every clear zone. Placed from the map
        // alone, once (<see cref="HighMasts"/>): inside every freeway or trunk
        // link that turns 100 degrees or more (a loop's middle, a big curve's
        // inside), where nothing is paved within <see cref="MastRoadClearM"/>,
        // no building or water stands and no other mast within
        // <see cref="MastSpacingM"/>. A tile stands the masts whose foot it
        // holds: the pole and ring on the structure atlas (render-only), a
        // capsule round its foot (solid, a car meets a pole), and its heads to
        // the tile's night glow (<see cref="TileMeshes.mastHeads"/>).
        // PSX_CITY_MASTS=0: none.
        static readonly bool mastsOff = System.Environment.GetEnvironmentVariable("PSX_CITY_MASTS") == "0";
        public static bool HighMastsOn => BridgeDetailOn && !mastsOff;
        const float MastRoadClearM = 14f, MastSpacingM = 170f, MastMinTurnDeg = 100f;
        const float MastBaseR = 0.42f, MastTopR = 0.17f, MastRingR = 1.35f;
        const int MastHeadCount = 6;
        public struct HighMast { public Vector2 foot; public float height; public int edge; }
        static List<HighMast> highMasts;
        static CityMap highMastMap;

        /// <summary>Every high mast in the city (computed once per map).</summary>
        public static List<HighMast> HighMasts(CityMap map)
        {
            if (highMasts != null && highMastMap == map) return highMasts;
            var list = new List<HighMast>();
            var near = new HashSet<int>();
            foreach (var e in map.edges)
            {
                if (!e.link || e.cls < 4 || e.length < 110f || e.pts.Length < 3) continue;
                float turn = 0f;
                for (int k = 1; k + 1 < e.pts.Length; k++)
                {
                    Vector2 d0 = e.pts[k] - e.pts[k - 1], d1 = e.pts[k + 1] - e.pts[k];
                    if (d0.sqrMagnitude < 1e-4f || d1.sqrMagnitude < 1e-4f) continue;
                    turn += Vector2.SignedAngle(d0, d1);
                }
                if (Mathf.Abs(turn) < MastMinTurnDeg) continue;
                // the arc's middle, pulled in toward its chord: inside the curve
                Vector2 c = Vector2.zero;
                foreach (var p in e.pts) c += p;
                c /= e.pts.Length;
                var mid = e.PointAt(e.length * 0.5f);
                var foot = Vector2.Lerp(mid, c, Mathf.Abs(turn) >= 220f ? 1f : 0.7f);
                if (!MastClear(map, foot, near)) continue;
                bool spaced = true;
                foreach (var m in list) if (Vector2.Distance(m.foot, foot) < MastSpacingM) { spaced = false; break; }
                if (!spaced) continue;
                // 100 ft on a ramp's loop, 120 ft where a freeway's own carriageway is near
                list.Add(new HighMast { foot = foot, height = e.cls >= 5 ? 36.5f : 30.5f, edge = e.index });
            }
            highMasts = list; highMastMap = map;
            return list;
        }

        static bool MastClear(CityMap map, Vector2 p, HashSet<int> scratch)
        {
            scratch.Clear();
            float reach = MastRoadClearM + 25f;
            map.EdgeSegsInRect(p - Vector2.one * reach, p + Vector2.one * reach, scratch);
            foreach (int packed in scratch)
            {
                int oi = packed >> 12, si = packed & 0xFFF;
                var o = map.edges[oi];
                if (si + 1 >= o.pts.Length) continue;
                Vector2 a = o.pts[si], d = o.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                if (Vector2.Distance(p, a + d * t) < o.HalfMax + MastRoadClearM) return false;
            }
            if (map.AnyFootprintNear(p, 8f)) return false;
            if (map.waters != null)
                foreach (var w in map.waters)
                {
                    if (w.pts == null || w.pts.Length < 2) continue;
                    float pad = w.width * 0.5f + 6f;
                    if (p.x < w.bbMin.x - pad || p.x > w.bbMax.x + pad || p.y < w.bbMin.y - pad || p.y > w.bbMax.y + pad) continue;
                    if (w.lake && w.pts.Length >= 3 && CityMap.PointInPoly(w.pts, p)) return false;
                    for (int k = 0; k + 1 < w.pts.Length; k++)
                    {
                        Vector2 a = w.pts[k], d = w.pts[k + 1] - a;
                        float L2 = d.sqrMagnitude;
                        float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                        if (Vector2.Distance(p, a + d * t) < w.width * 0.5f + 6f) return false;
                    }
                }
            return true;
        }

        /// <summary>The masts whose foot is in this tile: pole, ring, heads.</summary>
        static void BuildHighMasts(CityMap map, TileMeshes tm, Vector2 min, Vector2 max)
        {
            if (!HighMastsOn) return;
            foreach (var m in HighMasts(map))
            {
                var p = m.foot;
                if (p.x < min.x || p.x >= max.x || p.y < min.y || p.y >= max.y) continue;
                float gy = Mathf.Min(LatticeY(map, p.x, p.y), CityElevation.GroundY(map, p.x, p.y));
                var o = tm.origin;
                var foot = new Vector3(p.x - o.x, gy - 0.4f, p.y - o.z);
                float top = gy + m.height;
                tm.mastFeet.Add(p);
                var bk = structBucket;
                // the pole: twelve sides, tapering, galvanized
                for (int k = 0; k < ColumnSides; k++)
                {
                    float a0 = k * 2f * Mathf.PI / ColumnSides, a1 = (k + 1) * 2f * Mathf.PI / ColumnSides;
                    var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    var t3 = new Vector3(foot.x, top, foot.z);
                    bk.Face(foot + d0 * MastBaseR, t3 + d0 * MastTopR, t3 + d1 * MastTopR, foot + d1 * MastBaseR, d0 + d1,
                            new Vector2(CellGalv0, BandMisc0), new Vector2(CellGalv0, BandMisc1), new Vector2(CellGalv1, BandMisc1), new Vector2(CellGalv1, BandMisc0));
                }
                // the base plate's anchor collar, the ring frame and its floods
                EmitBlock(bk, foot, foot + Vector3.up * 0.75f, Vector3.right * 0.55f, Vector3.forward * 0.55f, CellHouse0, CellHouse1, BandMisc0, BandMisc1);
                var ringY = top - 0.9f;
                for (int k = 0; k < 8; k++)
                {
                    float a0 = k * Mathf.PI / 4f, a1 = (k + 1) * Mathf.PI / 4f;
                    var q0 = new Vector3(foot.x + Mathf.Cos(a0) * MastRingR, ringY, foot.z + Mathf.Sin(a0) * MastRingR);
                    var q1 = new Vector3(foot.x + Mathf.Cos(a1) * MastRingR, ringY, foot.z + Mathf.Sin(a1) * MastRingR);
                    var along = q1 - q0; var mid = 0.5f * (q0 + q1);
                    var outw = (mid - new Vector3(foot.x, ringY, foot.z)).normalized;
                    EmitBlock(bk, mid + Vector3.down * 0.08f, mid + Vector3.up * 0.08f, along * 0.5f, outw * 0.05f, CellGalv0, CellGalv1, BandMisc0, BandMisc1);
                }
                for (int k = 0; k < MastHeadCount; k++)
                {
                    float a = (k + 0.5f) * 2f * Mathf.PI / MastHeadCount;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var hc = new Vector3(foot.x, ringY - 0.25f, foot.z) + dir * (MastRingR + 0.3f);
                    var side = new Vector3(-dir.z, 0f, dir.x);
                    EmitBlock(bk, hc + Vector3.down * 0.22f, hc + Vector3.up * 0.18f, dir * 0.38f, side * 0.26f, CellHouse0, CellHouse1, BandMisc0, BandMisc1);
                    // the flood's lens, tilted down and out: the light's head
                    tm.mastHeads.Add(hc + Vector3.down * 0.25f + dir * 0.1f);
                }
                // the head cap
                EmitBlock(bk, new Vector3(foot.x, top - 0.1f, foot.z), new Vector3(foot.x, top + 0.45f, foot.z), Vector3.right * 0.32f, Vector3.forward * 0.32f,
                          CellHouse0, CellHouse1, BandMisc0, BandMisc1);
                // solid where a car can reach it: its foot, as drawn (the taper is
                // 9 mm a metre, so a 4 m capsule stays within 2 cm of the pole)
                float cr = MastBaseR - (MastBaseR - MastTopR) * (2.2f / m.height);
                tm.solids.Add(new SolidBox
                {
                    center = foot + Vector3.up * 2.2f, size = new Vector3(2f * cr, 4.4f, 2f * cr),
                    round = true, radius = cr * 0.983f,
                });
            }
        }
            /// <summary>
        /// THE PIERS' CLEAR ZONE (the bridges pass, DOT's first choice: move
        /// the hazard rather than shield it). How far a pier column keeps,
        /// in plan, from the paved edge of road <paramref name="o"/> passing
        /// under its deck at arc <paramref name="at"/> (<paramref name="p"/> the
        /// column, world plan): a freeway's or expressway's recovery area and
        /// a ramp's or arterial's clear zone (RoadsideRules.CityBenchM: 8 m,
        /// 4.5 m) - except on a divided road's MEDIAN side, where its Jersey
        /// barrier already stands between the lanes and the column - and
        /// <see cref="PierClearM"/> beside a street.
        /// </summary>
        static float PierClearFor(CityMap.Edge o, float at, Vector2 p)
        {
            bool fast = o.link || o.cls >= 4 || o.speedKmh >= 80;
            if (!fast) return PierClearM;
            if (o.oneway && !o.link && Barriered(o))
            {
                // R (the left of travel) is a carriageway's median side
                var t = o.TangentAt(at);
                var q = o.PointAt(at);
                float leftOf = t.x * (p.y - q.y) - t.y * (p.x - q.x);
                if (leftOf > 0f) return PierClearM;
            }
            return Mathf.Max(PierClearM, RoadsideRules.CityBenchM(o.cls, o.link));
        }
            // ---- the W-beams drawn on the structure atlas ---------------------------
        /// <summary>
        /// The bridges pass draws every W-beam - the parapets' lead-ins and the
        /// runs - with the tile's structure (render-only, one draw with the
        /// girders): GALVANIZED rail and steel posts with their blockouts, as
        /// in the owner's photo, where the furniture atlas painted them dark
        /// metal and brown wood. The shapes are CityPoles.EmitWBeam's (the
        /// colliders are cut to them, INVISIBLE COLLIDERS), so nothing moves;
        /// CityPoles.WBeamMesh stands down while this is on.
        /// </summary>
        public static bool WBeamsOnAtlas => BridgeDetailOn;
        static readonly float[] WbProfH = { 0f, 0.25f, 0.5f, 0.75f, 1f };
        static readonly float[] WbProfO = { 0.03f, 0f, 0.05f, 0f, 0.03f };

        static void DrawWBeams(TileMeshes tm)
        {
            if (!WBeamsOnAtlas) return;
            foreach (var w in tm.wbeams) DrawWBeamPiece(w);
            foreach (var p in tm.wbeamPosts) DrawWBeamPost(p);
        }

        static void DrawWBeamPiece(WBeamPiece w)
        {
            var bk = structBucket;
            Vector3 a = new Vector3(w.a.x, 0f, w.a.z), b = new Vector3(w.b.x, 0f, w.b.z);
            var oa = new Vector3(w.outA.x, 0f, w.outA.y); var ob = new Vector3(w.outB.x, 0f, w.outB.y);
            float ha = w.topA - w.botA, hb = w.topB - w.botB;
            float len = Vector3.Distance(a, b);
            float u0 = 0f, u1 = len / StructRepeatM * 2f;   // the galvanized cell repeats every 4 m
            Vector3 P(Vector3 p, Vector3 o, float bot, float h, int k, float extra) => p + o * (WbProfO[k] + extra) + Vector3.up * (bot + h * WbProfH[k]);
            for (int k = 0; k + 1 < WbProfH.Length; k++)
            {
                var p0 = P(a, oa, w.botA, ha, k, 0f); var p1 = P(b, ob, w.botB, hb, k, 0f);
                var p2 = P(b, ob, w.botB, hb, k + 1, 0f); var p3 = P(a, oa, w.botA, ha, k + 1, 0f);
                var n = Vector3.Cross(p1 - p0, p3 - p0);
                if (Vector3.Dot(n, oa + ob) > 0f) n = -n;   // the face looks at the traffic
                float va = Mathf.Lerp(BandMisc0, BandMisc1, WbProfH[k]), vb = Mathf.Lerp(BandMisc0, BandMisc1, WbProfH[k + 1]);
                bk.Face(p0, p3, p2, p1, n, new Vector2(CellGalv0 + u0 * 0f, va), new Vector2(CellGalv0, vb), new Vector2(CellGalv1, vb), new Vector2(CellGalv1, va));
            }
            Vector3 ba0 = a + oa * WBeamBackM + Vector3.up * w.botA, bb0 = b + ob * WBeamBackM + Vector3.up * w.botB;
            Vector3 ba1 = a + oa * WBeamBackM + Vector3.up * w.topA, bb1 = b + ob * WBeamBackM + Vector3.up * w.topB;
            var gv0 = new Vector2(CellGalv0, BandMisc0); var gv1 = new Vector2(CellGalv0, BandMisc1); var gv2 = new Vector2(CellGalv1, BandMisc1); var gv3 = new Vector2(CellGalv1, BandMisc0);
            bk.Face(ba0, ba1, bb1, bb0, oa + ob, gv0, gv1, gv2, gv3);
            bk.Face(P(a, oa, w.botA, ha, 4, 0f), ba1, bb1, P(b, ob, w.botB, hb, 4, 0f), Vector3.up, gv0, gv1, gv2, gv3);
            bk.Face(P(a, oa, w.botA, ha, 0, 0f), ba0, bb0, P(b, ob, w.botB, hb, 0, 0f), Vector3.down, gv0, gv1, gv2, gv3);
            var along = (b - a).normalized;
            if (w.endA) bk.Face(P(a, oa, w.botA, ha, 0, -0.03f), ba0, ba1, P(a, oa, w.botA, ha, 4, -0.03f), -along, gv0, gv1, gv2, gv3);
            if (w.endB) bk.Face(P(b, ob, w.botB, hb, 0, -0.03f), bb0, bb1, P(b, ob, w.botB, hb, 4, -0.03f), along, gv0, gv1, gv2, gv3);
        }

        /// <summary>A W-beam's steel post (W6x9, galvanized) and its blockout,
        /// behind the beam, a metre into the ground (CityPoles' sizes).</summary>
        static void DrawWBeamPost(WBeamPost p)
        {
            var o = new Vector3(p.outward.x, 0f, p.outward.y).normalized;
            var al = new Vector3(-o.z, 0f, o.x);
            float top = p.top, foot = p.at.y - 1.0f;
            var c = new Vector3(p.at.x, 0f, p.at.z) + o * 0.33f;
            EmitBlock(structBucket, new Vector3(c.x, foot, c.z), new Vector3(c.x, top, c.z), al * 0.075f, o * 0.075f, CellGalv0, CellGalv1, BandMisc0, BandMisc1);
            float mid = top - 0.5f * WBeamDepthM;
            var bc = new Vector3(p.at.x, 0f, p.at.z) + o * 0.155f;
            EmitBlock(structBucket, new Vector3(bc.x, mid - 0.18f, bc.z), new Vector3(bc.x, mid + 0.18f, bc.z), al * 0.07f, o * 0.075f, CellGalv0, CellGalv1, BandMisc0, BandMisc1);
        }
    }
}
