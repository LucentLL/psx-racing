using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  CURBS 3 (2026-11-15; the owner in the PC41 build: "I see some storm
    //  drains with curbs that extend into the road (they look decent), but
    //  other roads don't have curbs like that. Are these accurate?").
    //
    //  PART 1 - THE INLET (PSX_CITY_INLET2=0: the 11-11 inlet).
    //  What the standards draw (NCDOT Roadway Standard Drawings 840.02 /
    //  840.03, 846.01; Charlotte Land Development Standards Manual 10.17A/B,
    //  10.19, 10.29, 10.30 - read 2026-11-15):
    //    - the grate, 35-3/4 x 23-3/4 in (0.908 x 0.603 m), lies in the 2 ft
    //      gutter pan in front of the curb;
    //    - the cast hood is 3 ft 0-7/8 in long (0.937 m), its nose ON the
    //      curb's face line and its top LEVEL with the top of curb: it takes
    //      the place of 3 ft of curb, nothing stands forward of the curb
    //      line; under its 2-1/2 in lip the opening is a dark slot;
    //    - the standard basin has no slab and no round lid behind it (a lid
    //      is on the offset basin 20.34 and the open-throat types only);
    //    - on a valley-gutter (roll curb) street "no drainage structure is
    //      placed in the rolled curb": the valley gutter becomes standard
    //      2'-6" curb and gutter at every inlet through a transition 10 ft
    //      max / 3 ft 6 in min on each side (CLDSM 10.29); the gutter lip
    //      stays straight, the vertical face rises on the roll's back line
    //      and the back of curb moves 6 in toward the yard.
    //  What the 11-11 inlet drew: on a vertical curb a hood 1.1 m long, 2 cm
    //  proud and a centimetre over the curb, with a 1.3 m slab and a lid
    //  behind; on a rolled or part-rolled curb (DrawInlet's rolled = r > 0.5)
    //  the full-height hood and its 0.5 m deep top stood at the GUTTER LINE,
    //  0.45 m in front of the roll's crest, with blunt ends - a block in the
    //  road (his picture); and every "upstream of the corner" inlet of a
    //  rolled street stood 5.5 m before the ribbon's end, INSIDE the 8 m ease
    //  to the corner's vertical curb. The hood also ignored a driveway
    //  wing's lowered curb beside it (it stood up to 10 cm over the walk).
    //  Now:
    //    - the inlets of an edge are PLANNED before its walk is laid
    //      (Cs3EnsurePlan: the 11-11 stations, kept off aprons, tree grates
    //      and paint by map data alone, so every tile plans the same);
    //    - on a rolled street the walk's section eases, over InletTransM each
    //      side of the hood, from the roll to a pan with the vertical curb
    //      behind it: the face on the roll's crest line (RNoseOut), the
    //      proven chamfered collider behind the drawn plumb face, the back
    //      6 in further out (Cs3Profile; every section between is a lerp of
    //      the two shapes CityCurbDrive measured);
    //    - the hood is set in the face, flush (9 mm proud, 1 cm over the
    //      top), 0.937 m long, 0.152 m deep, to the local curb's height;
    //      the grate 0.908 x 0.603 m in the pan; no slab, no lid;
    //    - a rolled street's corner inlet stands clear of the corner's ease.
    //  Not drawn: the 2 in gutter depression at the grate (the pan is
    //  render-only on the road's collider).
    // ======================================================================
    public static partial class CityMeshes
    {
        public static bool Inlet2On = System.Environment.GetEnvironmentVariable("PSX_CITY_INLET2") != "0";
        /// <summary>PART 2 (curb and gutter where real Charlotte has it): PSX_CITY_CURBS3=0, the 11-11 rule.</summary>
        public static bool Curbs3On = System.Environment.GetEnvironmentVariable("PSX_CITY_CURBS3") != "0";

        /// <summary>Tools that flip Curbs3On / Inlet2On between builds: the per-edge caches the rule feeds.</summary>
        public static void Curbs3Reset()
        {
            walkMapFor = null; cs2MapFor = null; noEdgeMap = null; cs3ClassMap = null;
            cs3PlanEdge = -1; cs3Plan.Clear();
        }

        // ==================================================================
        //  PART 2 - CURB AND GUTTER WHERE REAL CHARLOTTE HAS IT
        //  (PSX_CITY_CURBS3=0: the 11-11 rule).
        //
        //  THE CAUSE. WalkEdge gave a curb only to a street near the Square
        //  (UrbanCoreM) or beside a non-house building: every subdivision
        //  street, and every arterial through houses, ended at grass. Real
        //  Charlotte has curb and gutter on nearly every city street (CLDSM:
        //  valley gutter or 2'-6" curb and gutter on local residential
        //  streets, 2'-6" on commercial streets and thoroughfares); open
        //  shoulders and ditches are the rural sections, freeways and ramps.
        //
        //  THE RULE, per edge, from the map (CurbClassOf):
        //    not a surface street (ramp, freeway, bridge, tunnel, alley): none;
        //    near the Square or beside a non-house building (the 11-11 walk
        //      streets): the VERTICAL curb, gutter pan and walk as before -
        //      and a house street near the Square (the pre-war grid: Fourth
        //      Ward, Dilworth, Elizabeth, Biddleville, Wesley Heights) is
        //      now VERTICAL too, its frontage lawn, not paved (PSX_CITY_PREWAR=0:
        //      rolled with a paved frontage, as 11-11);
        //    else BUILT-UP (a real footprint or a generated building within
        //      BuiltReachM of a third of a local street's length, half of a
        //      collector's or arterial's, or a gap between two built
        //      stretches):
        //        local (class 0): the ROLL curb alone - CLDSM 10.17B's 2'-0"
        //          valley gutter, CurbOnlyW wide, the lawn from its back; no
        //          walk (the city required sidewalks in new subdivisions only
        //          from 1998; most of 1950-1990 Charlotte has none);
        //        collector / arterial (class 1-3): the VERTICAL curb, pan and
        //          walk (the thoroughfare section);
        //    else (nothing built beside it: a rural section): none, as now.
        //  A corner between two roll-curb streets is a curb return (a strip
        //  round the ring), not a paved fan. A roll curb is not cut at a
        //  driveway (a car drives over it: PSX_CITY_DRIVEROLL=0 cuts it as a
        //  vertical curb is cut). A street's class eases over 8 m to its
        //  neighbour's at a plain joint of two streets too.
        // ==================================================================
        public static bool PrewarOn = System.Environment.GetEnvironmentVariable("PSX_CITY_PREWAR") != "0";
        public static bool DriveRollOn = System.Environment.GetEnvironmentVariable("PSX_CITY_DRIVEROLL") != "0";
        public const byte CurbNone = 0, CurbRollOnly = 1, CurbRollWalk = 2, CurbVertical = 3;
        /// <summary>A house street (its frontage is lawn): or-ed into the cached class.</summary>
        const byte CurbResidBit = 8;
        /// <summary>CLDSM 10.17B: the 2'-0" valley gutter.</summary>
        public const float CurbOnlyW = 0.61f;
        const float CurbOnlyMinM = 0.5f;
        /// <summary>Built-up: a building within this of the street's line, read every BuiltStepM along it.</summary>
        const float BuiltReachM = 45f, BuiltStepM = 30f;
        static sbyte[] cs3ClassCache, cs3RawCache; static CityMap cs3ClassMap;

        static void EnsureCs3Class(CityMap map)
        {
            if (cs3ClassMap == map && cs3ClassCache != null && cs3ClassCache.Length == map.edges.Length) return;
            cs3ClassMap = map;
            cs3ClassCache = new sbyte[map.edges.Length];
            cs3RawCache = new sbyte[map.edges.Length];
        }

        /// <summary>The street's curb class (CurbNone / CurbRollOnly / CurbRollWalk / CurbVertical).</summary>
        public static byte CurbClassOf(CityMap map, CityMap.Edge e) => (byte)(Cs3ClassBits(map, e) & 7);
        /// <summary>A house street: its frontage stays lawn.</summary>
        static bool Cs3Residential(CityMap map, CityMap.Edge e) => Curbs3On && (Cs3ClassBits(map, e) & CurbResidBit) != 0;
        /// <summary>The roll curb alone (no walk behind it).</summary>
        public static bool CurbOnly(CityMap map, CityMap.Edge e) => Curbs3On && CurbOnlyOn && (Cs3ClassBits(map, e) & 7) == CurbRollOnly;
        /// <summary>PSX_CITY_CURBONLY=0: a roll-curb street takes 11-11's rolled walk (the 2.4 m slab behind the roll) instead of the curb alone (the A/B for the curb drive).</summary>
        public static bool CurbOnlyOn = System.Environment.GetEnvironmentVariable("PSX_CITY_CURBONLY") != "0";
        static float Cs3WalkW(CityMap map, CityMap.Edge e) => CurbOnly(map, e) ? CurbOnlyW : WalkWOf(map, e);
        static float Cs3WalkMin(CityMap map, CityMap.Edge e) => CurbOnly(map, e) ? CurbOnlyMinM : WalkMinM;

        static int Cs3ClassBits(CityMap map, CityMap.Edge e)
        {
            if (!WalksOn) return CurbNone;
            EnsureCs3Class(map);
            ref sbyte c = ref cs3ClassCache[e.index];
            if (c != 0) return c - 1;
            int bits = Cs3ClassUncached(map, e, out bool sure);
            if (sure) c = (sbyte)(bits + 1);
            return bits;
        }

        static int Cs3ClassUncached(CityMap map, CityMap.Edge e, out bool sure)
        {
            sure = true;
            if (e.link || e.cls > 3 || e.tunnel || e.bridge || e.a == e.b) return CurbNone;
            if (e.width < (e.oneway ? 4f : 6f)) return CurbNone;
            bool core = false, shop = false;
            for (int k = 1; k <= 3; k++)
            {
                var p = e.PointAt(e.length * k * 0.25f);
                if (Vector2.Distance(p, map.uptown) < UrbanCoreM) core = true;
                if (map.AnyFootprintNear(p, UrbanFootM, nonHouseOnly: true)) shop = true;
            }
            if (core || shop)
            {
                // the 11-11 walk streets; its rolled class was a house street near the Square
                if (e.cls == 0 && !shop && !CityPoles.Uptown(map, e.PointAt(e.length * 0.5f)))
                    return Curbs3On && PrewarOn ? CurbVertical | CurbResidBit : CurbRollWalk;
                return CurbVertical;
            }
            if (!Curbs3On) return CurbNone;
            bool built = Cs3Built(map, e, out sure);
            if (!built) return CurbNone;
            return e.cls == 0 ? CurbRollOnly | CurbResidBit : CurbVertical;
        }

        /// <summary>Built-up: the share of the street's reads (every BuiltStepM) with a building within
        /// BuiltReachM is at least this - a local street a third (house rows with gaps), a collector
        /// or arterial a half (a road lined with buildings; a country road past a few houses keeps
        /// its shoulders) - or the street is a gap between two built stretches.</summary>
        const float BuiltShareLocal = 0.34f, BuiltShareMajor = 0.5f;
        static float Cs3BuiltNeed(CityMap.Edge e) => e.cls == 0 ? BuiltShareLocal : BuiltShareMajor;

        static bool Cs3Built(CityMap map, CityMap.Edge e, out bool sure)
        {
            if (Cs3BuiltShare(map, e, out sure) >= Cs3BuiltNeed(e)) { sure = true; return true; }
            bool a = false, b = false, allSure = sure;
            for (int end = 0; end < 2; end++)
                foreach (int ei in map.nodeEdges[end == 0 ? e.a : e.b])
                {
                    if (ei == e.index) continue;
                    var o = map.edges[ei];
                    if (o.link || o.cls > 3 || o.tunnel || o.bridge || o.a == o.b) continue;
                    bool r = Cs3BuiltShare(map, o, out bool s2) >= Cs3BuiltNeed(o);
                    allSure &= s2 || r;
                    if (r) { if (end == 0) a = true; else b = true; break; }
                }
            sure = allSure || (a && b);
            return a && b;
        }

        /// <summary>The share of the street's reads with a building (a real footprint or a generated
        /// one) within BuiltReachM. Not sure (not cached) while no tile build has bound the generated
        /// buildings and the share is short of all.</summary>
        static float Cs3BuiltShare(CityMap map, CityMap.Edge e, out bool sure)
        {
            sure = true;
            ref sbyte c = ref cs3RawCache[e.index];
            if (c != 0) return (c - 1) / 100f;
            int n = Mathf.Clamp(Mathf.CeilToInt(e.length / BuiltStepM), 1, 16), hit = 0;
            for (int k = 0; k < n; k++)
            {
                var p = e.PointAt(e.length * (k + 0.5f) / n);
                if (map.AnyFootprintNear(p, BuiltReachM) || CityHouses.ProcBuildingNear(map, p, BuiltReachM)) hit++;
            }
            float share = (float)hit / n;
            sure = hit == n || CityHouses.BoundTo(map);
            if (sure) c = (sbyte)(1 + Mathf.RoundToInt(share * 100f));
            return share;
        }

        /// <summary>Does a street's class ease to its node's here (Cs2Mix)? A junction of three
        /// or more, or (part 2) a plain joint of two streets of different classes.</summary>
        static bool Cs3EaseNode(CityMap map, int node)
        {
            int n = map.nodeEdges[node].Count;
            return n >= 3 || (Curbs3On && n == 2);
        }

        /// <summary>Lots (and anything else that opens a curb): an extra "is there an opening here"
        /// test the walk's curb-cut read asks beside ApronAt (world x, z). Null: none.</summary>
        public static System.Func<CityMap, float, float, bool> CurbOpeningAt;
        /// <summary>Set while a rolled street's span is read for cuts: none (a car drives over the roll).</summary>
        static bool cs3NoCutNow;

        /// <summary>Tools: driveway mouths the walk spans crossed, while RecordHardware is set:
        /// over a roll curb, through a cut (lip under 3 cm), or with a curb face across (a step over 3 cm).</summary>
        public static int DrivesRoll, DrivesCut, DrivesFace;
        public static readonly List<string> DrivesFaceAt = new List<string>();
        static void Cs3AuditDrives(CityMap map, CityMap.Edge e, int side, Vector3 o, Vector3 eA, Vector3 eB, Vector2 outA, float sA, float sB, float wA, float wB)
        {
            if (!RecordHardware || tileDrives.Count == 0 || wA <= 0f || wB <= 0f || sB - sA < 0.05f) return;
            foreach (var d in tileDrives)
            {
                if (d.edge != e.index || d.toLot || Vector2.Dot(d.nrm, outA) <= 0f) continue;
                // the mouth's own arc (a driveway threaded skew between buildings meets the street off d.s)
                CityElevation.ProjectOn(e, d.foot, out float ds);
                if (ds < sA || ds >= sB) continue;
                float t = (ds - sA) / (sB - sA);
                int j = 1;
                while (j < cutT.Count - 1 && cutT[j] < t) j++;
                float c = Mathf.Lerp(cutC[j - 1], cutC[j], Mathf.Clamp01((t - cutT[j - 1]) / Mathf.Max(cutT[j] - cutT[j - 1], 1e-4f)));
                float r = WalkRollAt(map, e, ds), k = Cs3K(side, ds);
                // the step a wheel meets: a rolled section has none (a ramp); a vertical one its face
                float step = r >= 0.5f && k < 0.5f ? 0f : Mathf.Lerp(VCurbH, RCurbH, r) * c;
                if (r >= 0.5f && k < 0.5f) DrivesRoll++;
                else if (step <= 0.03f) DrivesCut++;
                else { DrivesFace++; if (DrivesFaceAt.Count < 40) DrivesFaceAt.Add($"e{e.index} '{e.name}' s {ds:0.0} at ({d.foot.x:0.0},{d.foot.y:0.0}) step {step * 100f:0.0} cm r {r:0.00} k {k:0.00}"); }
            }
        }

        /// <summary>NCDOT 840.03: the hood 3 ft 0-7/8 in along the curb, on a 6 in curb top, its lip 2-1/2 in deep.</summary>
        public const float HoodLenM = 0.937f, HoodTopDeepM = 0.152f, HoodLipM = 0.064f;
        /// <summary>NCDOT 840.03: the grate 35-3/4 in along, 23-3/4 in across.</summary>
        public const float Grate2AlongM = 0.908f, Grate2AcrossM = 0.603f;
        /// <summary>CLDSM 10.29: valley gutter to standard curb at a catch basin, 10 ft max / 3 ft 6 in min each side.</summary>
        public const float InletTransM = 3.05f, InletTransMinM = 1.07f;
        /// <summary>The full vertical curb's half length at the inlet (the hood's half and a hand).</summary>
        const float InletFlatM = 0.60f;
        /// <summary>A vertical curb's inlet keeps this far along from an apron (its wing's curb back to 70%).</summary>
        const float InletApronClearM = 3.6f;
        static readonly float[] cs3Nudges = { 0f, 1.5f, -1.5f, 3f, -3f, 5f, -5f, 7f, -7f, 9f, -9f };
        static readonly float[] cs3Trans = { InletTransM, 2.0f, InletTransMinM };

        struct Cs3Inlet { public int side; public float s, trans; public byte why; }
        /// <summary>The inlets of the edge being laid, both sides (Cs3EnsurePlan).</summary>
        static readonly List<Cs3Inlet> cs3Plan = new List<Cs3Inlet>(16);
        static int cs3PlanEdge = -1;
        static Vector2 cs3Min, cs3Max;
        /// <summary>The driveways within 20 m of the tile being built (PrepareTileDrives): what the plan reads,
        /// so a tile plans its neighbour's inlets near the border as the neighbour does.</summary>
        static readonly List<CityHouses.Driveway> tileDrivesWide = new List<CityHouses.Driveway>(64);
        /// <summary>The cut shares of the walk spans recorded for the edge (Cs2RecordSpan), by span.</summary>
        static readonly List<float> cs3CutT = new List<float>(256), cs3CutC = new List<float>(256);

        /// <summary>PrepareWalks: a new edge (or tile): its plan is made on its first walk span.</summary>
        static void Cs3ResetPlan(Vector2 min, Vector2 max) { cs3PlanEdge = -1; cs3Plan.Clear(); cs3Min = min; cs3Max = max; }

        /// <summary>The drawn edge point and outward of a side at arc s of the edge being laid
        /// (its sections), and whether a curb stands there (a walk at both ends of the span).</summary>
        static bool Cs3EdgeAt(int side, float s, out Vector3 ed, out Vector2 outw, out bool curb)
        {
            ed = default; outw = default; curb = false;
            int i = SpanIndexAt(s);
            if (i < 1) return false;
            var A = sections[i - 1]; var B = sections[i];
            float t = (s - A.s) / Mathf.Max(B.s - A.s, 1e-3f);
            ed = Vector3.Lerp(A.Edge(side), B.Edge(side), t);
            outw = Vector2.Lerp(A.Out(side), B.Out(side), t).normalized;
            var arr = WalkArr(side);
            int si = side < 0 ? 0 : 1;
            curb = arr[i - 1] > 0f && arr[i] > 0f && s < walkStopHi[si] && s > walkStopLo[si];
            return true;
        }

        /// <summary>A ramp end of this side's walk (a section with none beside one with a walk,
        /// or a branch's square stop) within reach along of arc s?</summary>
        static bool Cs3RampEndWithin(int side, float s, float reach)
        {
            var arr = WalkArr(side);
            int n = sections.Count, si = side < 0 ? 0 : 1;
            for (int k = 0; k < n; k++)
            {
                if (arr[k] > 0f || Mathf.Abs(sections[k].s - s) >= reach) continue;
                if ((k > 0 && arr[k - 1] > 0f) || (k + 1 < n && arr[k + 1] > 0f)) return true;
            }
            return Mathf.Abs(walkStopHi[si] - s) < reach || Mathf.Abs(walkStopLo[si] - s) < reach;
        }

        /// <summary>An apron (a driveway's or a lot's) on this side within reach along the curb of arc s?</summary>
        static bool Cs3ApronWithin(CityMap map, Vector3 o, int side, float s, float reach)
        {
            int n = Mathf.CeilToInt(2f * reach / 0.6f);
            for (int k = 0; k <= n; k++)
            {
                float u = -reach + 2f * reach * k / n;
                if (!Cs3EdgeAt(side, s + u, out var ed, out var outw, out _)) continue;
                var pw = new Vector2(ed.x + o.x, ed.z + o.z) + outw * 0.6f;
                if (ApronAt(map, pw.x, pw.y) || (tileDrivesWide.Count > 0 && CityHouses.OnAny(tileDrivesWide, pw, 0.5f, true))) return true;
            }
            return false;
        }

        /// <summary>
        /// THE PLAN: where this edge's inlets stand, per side - the 11-11
        /// stations (sags, upstream of the low corner, on grade), each nudged
        /// along to the first place with a curb, clear of aprons, tree grates
        /// and paint; on a rolled street also with room for the transition on
        /// the plain roll (clear of a vertical corner's ease). Map data only.
        /// </summary>
        static void Cs3EnsurePlan(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e)
        {
            if (cs3PlanEdge == e.index) return;
            cs3PlanEdge = e.index; cs3Plan.Clear();
            if (!Curbs2On || !DrainsOn || !Inlet2On || sections.Count < 2 || walkEdgeNow != e.index) return;
            EnsureMarks(map, trims);
            InletStations(map, trims, e);
            if (cs2Stations.Count == 0) return;
            bool rolledEdge = RolledEdge(map, e);
            var o = tm.origin;
            const float near = 26f;
            foreach (var st in cs2Stations)
            {
                var sp = e.PointAt(st.s);
                if (sp.x < cs3Min.x - near || sp.x > cs3Max.x + near || sp.y < cs3Min.y - near || sp.y > cs3Max.y + near) continue;
                bool mine = sp.x >= cs3Min.x && sp.x < cs3Max.x && sp.y >= cs3Min.y && sp.y < cs3Max.y;
                for (int side = -1; side <= 1; side += 2)
                {
                    if (!WalkSide(map, e, side)) continue;
                    byte why = 0; bool placed = false;
                    foreach (float nudge in cs3Nudges)
                    {
                        float s = st.s + nudge;
                        if (!Cs3EdgeAt(side, s, out _, out _, out bool curb) || !curb) { why = 1; continue; }
                        // a walk's ramp end eases its curb in over the wing (CurbCutWingM): no hood in a low curb
                        if (Cs3RampEndWithin(side, s, InletApronClearM)) { why = 1; continue; }
                        float trans = 0f;
                        if (rolledEdge)
                        {
                            foreach (float T in cs3Trans)
                            {
                                float lo = s - InletFlatM - T, hi = s + InletFlatM + T;
                                if (Cs2Mix(map, e, lo) < 0.999f || Cs2Mix(map, e, hi) < 0.999f) continue;
                                if (!Cs3EdgeAt(side, lo, out _, out _, out bool c0) || !c0 || !Cs3EdgeAt(side, hi, out _, out _, out bool c1) || !c1) continue;
                                if (Cs3ApronWithin(map, o, side, s, Mathf.Max(InletFlatM + T, Cs3NoCut(map, e) ? 0f : InletApronClearM))) continue;   // (a cut's wing lowers the curb for 5 m)
                                trans = T; break;
                            }
                            if (trans <= 0f) { why = 3; continue; }
                        }
                        else if (Cs3ApronWithin(map, o, side, s, InletApronClearM)) { why = 3; continue; }
                        if (StreetTreeGap(map, trims, e, s) < 1.6f) { why = 4; continue; }
                        if (Cs2MarkClash(e, s, 0.5f * Grate2AlongM + 0.1f)) { why = 6; continue; }
                        cs3Plan.Add(new Cs3Inlet { side = side, s = s, trans = trans, why = st.why });
                        placed = true;
                        break;
                    }
                    if (!placed && mine)
                        switch (why)
                        {
                            case 1: Curbs2Stats.noSpan++; break;
                            case 3: Curbs2Stats.apron++; break;
                            case 4: Curbs2Stats.tree++; break;
                            case 6: Curbs2Stats.markRefused++; break;
                        }
                }
            }
        }

        /// <summary>Does this street's curb run on across a driveway (no cut, no wing)? Part 2's rolled streets.</summary>
        static bool Cs3NoCut(CityMap map, CityMap.Edge e) => Curbs3On && DriveRollOn && RolledEdge(map, e);

        /// <summary>The inlet share at arc s of a side: 1 at a rolled street's inlet (the
        /// vertical curb), easing to 0 (the roll) over the inlet's transition.</summary>
        static float Cs3K(int side, float s)
        {
            float k = 0f;
            foreach (var p in cs3Plan)
            {
                if (p.side != side || p.trans <= 0f) continue;
                float d = Mathf.Abs(s - p.s) - InletFlatM;
                if (d >= p.trans) continue;
                float kk = d <= 0f ? 1f : 1f - LineModel.Smooth(d / p.trans);
                if (kk > k) k = kk;
            }
            return k;
        }

        /// <summary>EmitWalkSpanCore: the span's reads (cutT / cutC) get the breaks an inlet's
        /// transition needs (the hood's ends, the transition's thirds and ends).</summary>
        static void Cs3SplitSpan(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, int side, float sA, float sB)
        {
            Cs3EnsurePlan(map, trims, tm, e);
            if (cs3Plan.Count == 0 || sB - sA < 0.05f) return;
            foreach (var p in cs3Plan)
            {
                if (p.side != side || p.trans <= 0f) continue;
                float reach = InletFlatM + p.trans;
                if (p.s + reach <= sA || p.s - reach >= sB) continue;
                for (int q = 0; q <= 3; q++)
                    for (int sg = -1; sg <= 1; sg += 2)
                    {
                        float t = (p.s + sg * (InletFlatM + p.trans * q / 3f) - sA) / (sB - sA);
                        if (t <= 0.002f || t >= 0.998f) continue;
                        int j = 1;
                        while (j < cutT.Count && cutT[j] < t) j++;
                        if (j >= cutT.Count || t - cutT[j - 1] < 0.002f || cutT[j] - t < 0.002f) continue;
                        float f = (t - cutT[j - 1]) / (cutT[j] - cutT[j - 1]);
                        float c = Mathf.Lerp(cutC[j - 1], cutC[j], f), ca = Mathf.Lerp(cutA[j - 1], cutA[j], f);
                        cutT.Insert(j, t); cutC.Insert(j, c); cutA.Insert(j, ca);
                    }
            }
        }

        /// <summary>
        /// The walk's section at inlet share k of a street of rolled share r:
        /// q is the foot at the drawn edge, p[0] the pan's end (the vertical
        /// face's foot, on the roll's crest line), p[1] / p[2] the collider's
        /// chamfer top and nose behind it, p[3] the back. k = 0: the plain
        /// section (q = p[0]).
        /// </summary>
        static void Cs3Profile(Vector3 e, Vector2 outw, float w, Vector3[] p, out Vector3 q, float curb, float r, float k)
        {
            Cs2Profile(e, outw, w, p, curb, r);
            q = p[0];
            if (k <= 0f || w <= 0f) return;
            var o = new Vector3(outw.x, 0f, outw.y);
            Cs2Shape(0f, out float vf, out float vy, out float vn, out float vh);
            float F = RNoseOut * r;
            Vector3 A = e + o * F; A.y = e.y - 0.006f;
            Vector3 B = e + o * (F + vf); B.y = e.y + Mathf.Max(vy * curb, -0.5f * RoadsideRules.EdgeDropM);
            Vector3 C = e + o * (F + vn); C.y = e.y + vh * curb;
            float wI = Mathf.Max(w, F + vn + 0.10f);
            Vector3 D = e + o * wI; D.y = C.y + WalkCrossFall * (wI - F - vn);
            p[0] = Vector3.Lerp(p[0], A, k); p[1] = Vector3.Lerp(p[1], B, k); p[2] = Vector3.Lerp(p[2], C, k); p[3] = Vector3.Lerp(p[3], D, k);
        }

        /// <summary>The collider section's height at lat metres out of the drawn edge.</summary>
        static float Cs3SurfY(Vector3 e, Vector3 o, Vector3 q, Vector3[] p, float lat)
        {
            Vector3 a = q; float xa = 0f;
            for (int j = 0; j < 4; j++)
            {
                float xb = Vector3.Dot(p[j] - e, o);
                if (lat <= xb || j == 3) return xb - xa < 1e-4f ? p[j].y : Mathf.Lerp(a.y, p[j].y, Mathf.Clamp01((lat - xa) / (xb - xa)));
                a = p[j]; xa = xb;
            }
            return p[3].y;
        }

        static readonly Vector3[] r3A = new Vector3[4], r3B = new Vector3[4];
        /// <summary>The drawn plumb face at an inlet-share section: from the collider at the
        /// face line up to the nose's height, the rounded nose, the top back to the nose.</summary>
        static bool Cs3FaceProfile(Vector3 e, Vector2 outw, Vector3 q, Vector3[] p, float r, Vector3[] f)
        {
            var o = new Vector3(outw.x, 0f, outw.y);
            float F = RNoseOut * r;
            float yTop = p[2].y + 0.003f;
            float ySurf = Cs3SurfY(e, o, q, p, F);
            float hgt = yTop - ySurf, ks = Mathf.Clamp01(hgt / VCurbH);
            float nose = Vector3.Dot(p[2] - e, o);
            f[0] = e + o * F; f[0].y = ySurf - 0.004f;
            f[1] = e + o * Mathf.Min(F + VFaceOut * ks, nose); f[1].y = yTop - (VCurbH - VFaceY) * ks;
            f[2] = e + o * Mathf.Min(F + VNoseOut * ks, nose); f[2].y = yTop;
            f[3] = p[2]; f[3].y = yTop;
            return hgt > 0.006f;
        }

        /// <summary>One piece of walk with an inlet share at either end: the pan, the walk's
        /// three bands (LayWalk) and the drawn face over the chamfer.</summary>
        static void Cs3LayPiece(Bucket bk, Vector3 eA, Vector2 oA, Vector3 qa, Vector3[] pa, Vector3 eB, Vector2 oB, Vector3 qb, Vector3[] pb,
                                float rA, float rB, float uA, float uB)
        {
            float da = Flat(pa[0] - qa), db = Flat(pb[0] - qb);
            if (da > 1e-3f || db > 1e-3f)
            {
                bk.Up(qa, pa[0], pb[0], qb, new Vector2(uA, -da / WalkTexM), new Vector2(uA, 0f), new Vector2(uB, 0f), new Vector2(uB, -db / WalkTexM));
                LogWalkQuad(" walk pan", qa, pa[0], pb[0], qb);
            }
            cs2FaceR = 1f;   // (no Cs2RenderFace: the face is drawn here, on the crest line)
            LayWalk(bk, pa, pb, Flat(pa[3] - pa[0]), Flat(pb[3] - pb[0]), uA, uB);
            bool fa = Cs3FaceProfile(eA, oA, qa, pa, rA, r3A), fb = Cs3FaceProfile(eB, oB, qb, pb, rB, r3B);
            if (!fa && !fb) return;
            var band = cs2Granite ? UvCollar : UvPan;
            Vector2 U(float u, float fv) => new Vector2(u, band.y + band.height * fv);
            float ua = uA * WalkTexM / 8f, ub = uB * WalkTexM / 8f;
            var outSum = new Vector3(oA.x + oB.x, 0f, oA.y + oB.y);
            structBucket.Face(r3A[0], r3A[1], r3B[1], r3B[0], -outSum, U(ua, 0f), U(ua, 0.25f), U(ub, 0.25f), U(ub, 0f));
            structBucket.Face(r3A[1], r3A[2], r3B[2], r3B[1], Vector3.up - outSum, U(ua, 0.25f), U(ua, 0.32f), U(ub, 0.32f), U(ub, 0.25f));
            structBucket.Up(r3A[2], r3A[3], r3B[3], r3B[2], U(ua, 0.32f), U(ua, 0.6f), U(ub, 0.6f), U(ub, 0.32f));
            float lm = Vector2.Distance(new Vector2(eA.x, eA.z), new Vector2(eB.x, eB.z));
            RenderFaceM += lm; Curbs3Stats.inletTransM += lm;
        }

        public static class Curbs3Stats
        {
            /// <summary>Walk metres laid inside an inlet's transition; inlets drawn in a transition; planned inlets not drawn for a low curb.</summary>
            public static float inletTransM;
            public static int inletsTrans, inletLowCurb;
            public static void Reset() { inletTransM = 0f; inletsTrans = inletLowCurb = 0; }
            public static string Line() => $"inlet2 {(Inlet2On ? "ON" : "OFF")}: {inletsTrans} inlets in a roll-to-vertical transition ({inletTransM:0} m of transition), {inletLowCurb} not drawn (a cut's wing)";
        }

        /// <summary>The curb's share (a driveway wing's) at t of a recorded span.</summary>
        static float Cs3SpanCurb(in Cs2Span sp, float t)
        {
            if (sp.cutN < 2) return 1f;
            for (int j = sp.cut0 + 1; j < sp.cut0 + sp.cutN; j++)
                if (t <= cs3CutT[j] || j == sp.cut0 + sp.cutN - 1)
                    return Mathf.Lerp(cs3CutC[j - 1], cs3CutC[j], Mathf.Clamp01((t - cs3CutT[j - 1]) / Mathf.Max(cs3CutT[j] - cs3CutT[j - 1], 1e-4f)));
            return 1f;
        }

        /// <summary>The planned inlets of the edge that stand in a walk span this tile laid.</summary>
        static void EmitInlets2(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e)
        {
            Cs3EnsurePlan(map, trims, tm, e);
            var o = tm.origin;
            foreach (var pl in cs3Plan)
            {
                int k = -1;
                for (int q = 0; q < cs2Spans.Count; q++)
                {
                    var sp = cs2Spans[q];
                    if (sp.side != pl.side) continue;
                    float lo = Mathf.Min(sp.sA, sp.sB), hi = Mathf.Max(sp.sA, sp.sB);
                    if (pl.s >= lo && pl.s < hi) { k = q; break; }
                }
                if (k < 0) continue;   // another tile's span
                var spn = cs2Spans[k];
                float t = (pl.s - spn.sA) / (spn.sB - spn.sA);
                float c = Cs3SpanCurb(spn, t);
                if (c < 0.6f) { Curbs3Stats.inletLowCurb++; continue; }
                int iSpan = SpanIndexAt(pl.s);
                if (iSpan < 1) continue;
                bool rolled = pl.trans > 0f;
                DrawInlet2(iSpan, spn, t, rolled, c);
                var ed = Vector3.Lerp(spn.eA, spn.eB, t);
                var outw = Vector2.Lerp(spn.outA, spn.outB, t).normalized;
                if (RecordHardware) InletsBuilt.Add((ed + o, outw, rolled));
                Curbs2Stats.inlets++;
                if (rolled) { Curbs2Stats.inletsRolled++; Curbs3Stats.inletsTrans++; }
                if (pl.why == WhySag) Curbs2Stats.inletSag++; else if (pl.why == WhyEnd) Curbs2Stats.inletEnd++; else Curbs2Stats.inletGrade++;
            }
        }

        /// <summary>
        /// One NCDOT 840.03 inlet at t of a walk span: the grate in the pan,
        /// the cast hood set flush in the vertical curb's face (on a rolled
        /// street the face stands on the roll's crest line, at the middle of
        /// the transition the walk laid), to the local curb's height (curb:
        /// a cut wing's share). Render-only (the structure mesh).
        /// </summary>
        static void DrawInlet2(int i, Cs2Span sp, float t, bool rolled, float curb)
        {
            var A = sections[i - 1]; var B = sections[i];
            var outw = Vector2.Lerp(sp.outA, sp.outB, t).normalized;
            var o3 = new Vector3(outw.x, 0f, outw.y);
            var d3 = sp.eB - sp.eA; float len = new Vector2(d3.x, d3.z).magnitude;
            var a3 = new Vector3(d3.x, 0f, d3.z) / Mathf.Max(len, 1e-3f);
            Vector3 Ed(float along) => Vector3.LerpUnclamped(sp.eA, sp.eB, t + along / Mathf.Max(len, 1e-3f));
            float F = rolled ? RNoseOut : 0f, H = VCurbH * curb;
            // ---- the grate in the pan, against the face ----
            float gl = Grate2AlongM * 0.5f, g0 = F - 0.012f, g1 = g0 - Grate2AcrossM;
            Vector3 P(float along, float lat, float lift)
            {
                var ea = Ed(along);
                var p = ea + o3 * lat;
                // on the road: its plane; past the drawn edge (a rolled street's pan): the pan, an inch under the edge at its lip
                p.y = lat <= 0f ? RoadYIn(A, B, p) + lift : ea.y + Mathf.Lerp(0f, -0.006f, lat / Mathf.Max(F, 1e-3f)) + lift;
                return p;
            }
            SQuad(P(-gl - 0.04f, g0 + 0.008f, GrateLiftM2), P(-gl - 0.04f, g1 - 0.04f, GrateLiftM2), P(gl + 0.04f, g1 - 0.04f, GrateLiftM2), P(gl + 0.04f, g0 + 0.008f, GrateLiftM2), UvIron);
            SQuadUv(P(-gl, g0, GrateLiftM2 + 0.002f), P(-gl, g1, GrateLiftM2 + 0.002f), P(gl, g1, GrateLiftM2 + 0.002f), P(gl, g0, GrateLiftM2 + 0.002f), UvSlot);
            const int bars = 11;   // (type E: straight bars across the flow, a spine along)
            for (int b = 0; b < bars; b++)
            {
                float c = -gl + (b + 0.5f) * (Grate2AlongM / bars), hw = 0.016f;
                SQuad(P(c - hw, g0, GrateBarLiftM), P(c - hw, g1, GrateBarLiftM), P(c + hw, g1, GrateBarLiftM), P(c + hw, g0, GrateBarLiftM), UvIronLid);
            }
            float mid = 0.5f * (g0 + g1);
            SQuad(P(-gl, mid - 0.02f, GrateBarLiftM + 0.001f), P(-gl, mid + 0.02f, GrateBarLiftM + 0.001f), P(gl, mid + 0.02f, GrateBarLiftM + 0.001f), P(gl, mid - 0.02f, GrateBarLiftM + 0.001f), UvIronLid);
            if (rolled)
            {
                // the frame's ends down to the pan (the pan's lip lies an inch under the road's edge)
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    float al = sg * (gl + 0.04f);
                    Vector3 t0 = P(al, 0f, GrateLiftM2), t1 = P(al, g0 + 0.008f, GrateLiftM2);
                    SFace(t0 - Vector3.up * 0.05f, t0, t1, t1 - Vector3.up * 0.03f, a3 * sg, UvIron);
                }
            }

            // ---- the hood: 0.937 m of the curb's face and top in cast iron, flush ----
            float hl = HoodLenM * 0.5f;
            float fr = F - 0.009f, th = F - 0.005f;        // the casting's front, the throat's dark back (in front of the drawn face)
            float yTop = H + 0.010f;                        // (the drawn curb's top stands 3 mm over the collider's)
            float yLip = Mathf.Max(0.025f, H - HoodLipM);   // the lip's underside: the throat's top
            Vector3 Hd(float along, float lat, float y)
            {
                var ea = Ed(along);
                var p = ea + o3 * lat;
                p.y = ea.y + y;
                return p;
            }
            var slot = new Rect(UvSlot.x, UvSlot.y, 0f, 0f);
            SFace(Hd(-hl + 0.015f, th, -0.03f), Hd(-hl + 0.015f, th, yLip), Hd(hl - 0.015f, th, yLip), Hd(hl - 0.015f, th, -0.03f), -o3, slot);
            structBucket.Down(Hd(-hl, fr, yLip), Hd(-hl, th, yLip), Hd(hl, th, yLip), Hd(hl, fr, yLip), UvSlot, UvSlot, UvSlot, UvSlot);
            SFace(Hd(-hl, fr, yLip), Hd(-hl, fr, yTop), Hd(hl, fr, yTop), Hd(hl, fr, yLip), -o3, UvIron);
            SQuad(Hd(-hl, fr, yTop), Hd(-hl, F + HoodTopDeepM, yTop), Hd(hl, F + HoodTopDeepM, yTop), Hd(hl, fr, yTop), UvIron);
            // its two ends: the casting's edge, a centimetre of iron in the curb's face
            SFace(Hd(-hl, fr, yLip), Hd(-hl, fr, yTop), Hd(-hl, F + 0.012f, yTop), Hd(-hl, F + 0.012f, yLip), -a3, UvIron);
            SFace(Hd(hl, fr, yLip), Hd(hl, fr, yTop), Hd(hl, F + 0.012f, yTop), Hd(hl, F + 0.012f, yLip), a3, UvIron);
        }
    }
}
