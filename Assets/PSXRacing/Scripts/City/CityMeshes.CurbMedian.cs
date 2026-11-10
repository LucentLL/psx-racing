using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  CURBED MEDIANS (2026-11-05). The owner: "Curbs and sidewalks and
    //  median/islands that should have curbs are all pits in game, it seems"
    //  (West Trade Street in Uptown; Beatties Ford Road at the bank block by
    //  Johnson C. Smith, where the real median is a raised island planted
    //  with shrubs behind 6 in curbs).
    //
    //  THE CAUSE, MEASURED. A divided city street's carriageway gets no walk
    //  on its median side (WalkSide: the side faces its twin), so EmitSide
    //  laid that side the open-country verge: 2.5 cm down at the edge, the
    //  0.6 m shoulder falling, then the foreslope to the sunk lattice. The
    //  two carriageways' verges met in the middle as a TROUGH: West Trade
    //  (e5800 / e11389) a 2.5-5.5 m strip bottoming 16-35 cm under the lane
    //  edge, Beatties Ford (e13406 / e13408) 2.3-3.3 m bottoming 10-43 cm
    //  under it. The raised curbed median of the union work (RaisedCurbM)
    //  serves Barriered roads only, the kerbed refuge islands only the 17
    //  listed short pairs.
    //
    //  THE ISLAND. On such a side, where the ray out of the drawn edge meets
    //  the twin carriageway's own pavement 0.8-12 m away (no fan, no other
    //  road first, within half a metre of height), the side lays the walk's
    //  mountable curb (an inch under the edge, the 11 cm face over 10 cm,
    //  the rounded top to 15 cm at 25 cm: a wheel climbs it, the body box
    //  clears it) and the island's top from the curb's nose to the MEDIAN'S
    //  AXIS, half way to the twin's edge, at the mean of the two edges'
    //  heights + 15 cm: both halves meet at one height whichever is lower.
    //  Each half tucks a little on under the other's. The top is grass (the
    //  planted island; concrete on MedianConcreteWays). The island's height
    //  ramps in over KerbEndRampM wherever it starts or stops (a junction
    //  end, a span where the twin is not found, a squeezed strip, a nose
    //  graded flush), and a stop mid-run closes its end with a face down
    //  into the verge, so nothing is open under it.
    //
    //  SCOPE, CITY-WIDE (11-05 afternoon, the default): every divided
    //  surface street - a one-way named carriageway up to primary with its
    //  twin on the left, urban (walks) or suburban (no walks): a real
    //  divided city street's median is curbed either way. NOT: freeways,
    //  expressways and one-way trunks (Barriered: cable, Jersey, unions,
    //  GroundTwinRuns keep their own), ramps, decks, tunnels, the refuge
    //  pairs, and a side whose twin is itself not of this class (no half
    //  island against a deck's or a ramp's side). The island also ramps out
    //  at a degree-2 node where the carriageway goes on as such an edge.
    //  A WIDE median (twin's pavement 12-30 m off) is not curbed: it is
    //  grass at the two edges' grade (an inch down, as the verge's lip),
    //  not the verges' trough; the curb fades out over KerbEndRampM where a
    //  median widens past 12 m. Past 30 m the two carriageways are two
    //  roads: their verges as before.
    //  PSX_CITY_CURBMEDIAN=spots: the owner's two spots only (CurbMedianEdges
    //  on graph hash CurbMedianGraph, as 369b2a84 shipped); =0: the verge as
    //  before (the A/B).
    // ======================================================================
    public static partial class CityMeshes
    {
        static readonly string CurbMedianMode = System.Environment.GetEnvironmentVariable("PSX_CITY_CURBMEDIAN") ?? "";
        public static bool CurbMedianOn => CurbMedianMode != "0";
        /// <summary>City-wide (11-05 afternoon) unless =spots (the two spots only, as 369b2a84 shipped).</summary>
        static bool CurbMedianAll => CurbMedianMode != "spots";
        /// <summary>The export the spot list was checked on (map.graphHash).</summary>
        const uint CurbMedianGraph = 0xb2aa3453u;
        /// <summary>West Trade Street's divided Uptown stretch (both carriageways),
        /// and Beatties Ford Road from Five Points to n634 (the bank block by
        /// Johnson C. Smith: a raised island planted with shrubs, opening at the
        /// side street behind the bank).</summary>
        static readonly HashSet<int> CurbMedianEdges = new HashSet<int>
        {
            1011, 5798, 5799, 5800, 5801, 5802, 5803, 5804, 5805, 5951, 9637, 11389, 11390, 11392,
            14403, 14404, 14405, 14406, 14407,
            13406, 13407, 13408,
        };
        /// <summary>City-wide exceptions on this export (CurbMedianGraph): kept
        /// as live. Idlewild Road at the n6970 launch spot (e10544, e14670, 31
        /// and 32 m carriageway stubs at the Monroe Road link): the westbound
        /// inner lane's drawn line runs within 0.2 m of the median edge there,
        /// so a 70 km/h drive lifted a wheel on the new curb (0.03 s at
        /// 5273,-43) - the launch spot stays as fixed this morning.</summary>
        static readonly HashSet<int> CurbMedianSkipEdges = new HashSet<int> { 10544, 14670 };
        /// <summary>The island's width from the edge to the twin's edge.</summary>
        const float CurbMedianMinM = 0.8f, CurbMedianMaxM = 12f;
        /// <summary>City-wide: no island this near a SPLIT node (where a two-way
        /// road parts into the twin carriageways). The real nose starts back
        /// from the split behind a painted taper; the junction sweep's wheel
        /// tracks run over the first metres of the parting (334 of 378 new
        /// off-road steps lay within 10 m of a split node).</summary>
        const float CurbMedianSplitSetbackM = 12f;
        /// <summary>City-wide: a median wider than CurbMedianMaxM, up to this,
        /// is grass at the edges' grade (no curb) - not the two verges' trough.
        /// Wider than this the carriageways are two roads: their verges as before.</summary>
        const float CurbMedianWideMaxM = 30f;
        /// <summary>The two carriageways' edges at most this far apart in height.</summary>
        const float CurbMedianMaxDyM = 0.5f;
        /// <summary>Past the axis, under the other half: this far, this far down.</summary>
        const float CurbMedianTuckM = 0.3f, CurbMedianTuckDownM = 0.25f;
        /// <summary>A stopped island's end face reaches this far under its top.</summary>
        const float CurbMedianCapM = 0.6f;

        public static int CurbMedianSpans, CurbMedianFlatSpans; public static float CurbMedianM, CurbMedianFlatM;
        /// <summary>The last solve's islands per edge (metres), for the probes.</summary>
        public static readonly Dictionary<int, float> CurbMedianByEdge = new Dictionary<int, float>();
        /// <summary>...of which flat grass (a wide median, no curb).</summary>
        public static readonly Dictionary<int, float> CurbMedianFlatByEdge = new Dictionary<int, float>();

        /// <summary>For the census (CurbMedianCensus): why a one-way named
        /// carriageway gets no island by class - null when it is of the
        /// island's class and faces a twin.</summary>
        public static string CurbMedianExcluded(CityMap map, CityMap.Edge e)
        {
            if (e.link) return "link (ramp)";
            if (e.bridge) return "bridge (deck)";
            if (e.tunnel) return "tunnel";
            if (e.cls >= 5) return "motorway";
            if (Barriered(e) && DividerAt(e) != DividerKind.Island) return "trunk one-way (Barriered)";
            if (LineModel.RefugeOf(e.index) != null) return "refuge pair";
            if (CurbMedianSkipped(e)) return "launch spot kept as live (n6970)";
            if (!CurbMedianClass(e)) return "other";
            if (!CurbMedianTwin(map, e)) return "no twin on the left";
            if (!CurbMedianOn) return "switched off";
            if (!CurbMedianEdge(map, e)) return "not a listed spot";
            return null;
        }

        static float[] cmW = new float[128], cmY = new float[128], cmH = new float[128], cmC = new float[128], cmK = new float[128], cmT = new float[128];
        static int cmEdgeNow = -1;

        /// <summary>A carriageway of the class the island is laid on (no
        /// geometry test): a one-way named surface street up to primary, not
        /// a ramp, deck, tunnel or a Barriered road (freeways, expressways and
        /// one-way trunks keep their own medians: cable, Jersey, unions), not
        /// one of the refuge pairs (their own kerb).</summary>
        static bool CurbMedianClass(CityMap.Edge e) =>
            e.oneway && !e.link && !e.bridge && !e.tunnel && e.a != e.b && !string.IsNullOrEmpty(e.name)
            // (the median dress, 11-09) and a surface trunk, whose divider is an island (CityMeshes.Dividers TrunkIsland)
            && (e.cls <= 3 ? !Barriered(e) : DividerAt(e) == DividerKind.Island)
            && LineModel.RefugeOf(e.index) == null && !CurbMedianSkipped(e);

        static bool CurbMedianSkipped(CityMap.Edge e)
        {
            var m = CityMap.Get();
            return m != null && m.graphHash == CurbMedianGraph && CurbMedianSkipEdges.Contains(e.index);
        }

        static sbyte[] cmTwinCache;
        static CityMap cmTwinMap;

        static bool CurbMedianEdge(CityMap map, CityMap.Edge e)
        {
            if (!CurbMedianOn || !CurbMedianClass(e)) return false;
            if (!CurbMedianAll && (map.graphHash != CurbMedianGraph || !CurbMedianEdges.Contains(e.index))) return false;
            // its left side faces its twin (city-wide: with walks or without -
            // a suburban divided arterial's median is curbed as an urban one's)
            return CurbMedianTwin(map, e);
        }

        static bool CurbMedianTwin(CityMap map, CityMap.Edge e)
        {
            lock (CurbMedianByEdge)
            {
                if (cmTwinMap != map || cmTwinCache == null || cmTwinCache.Length != map.edges.Length) { cmTwinCache = new sbyte[map.edges.Length]; cmTwinMap = map; }
                ref sbyte c = ref cmTwinCache[e.index];
                if (c == 0) c = (sbyte)(HasTwin(map, e) ? 1 : -1);
                return c > 0;
            }
        }

        /// <summary>
        /// Per section of the edge being drawn (after PrepareWalks): the
        /// median-side island's half width to the axis (0 none), the axis
        /// height and the ramp factor. Side +1 only (the left of travel).
        /// </summary>
        static void PrepareCurbMedian(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, Vector2 min, Vector2 max)
        {
            int n = sections.Count;
            if (cmW.Length < n) { cmW = new float[n * 2]; cmY = new float[n * 2]; cmH = new float[n * 2]; cmC = new float[n * 2]; cmK = new float[n * 2]; cmT = new float[n * 2]; }
            System.Array.Clear(cmW, 0, n); System.Array.Clear(cmH, 0, n); System.Array.Clear(cmC, 0, n);
            MedianDressSize(n);
            System.Array.Clear(cmEv, 0, n);
            cmEdgeNow = e.index;
            if (!CurbMedianEdge(map, e)) return;
            const int side = +1;
            // (the median dress) read further past the tile: a stop within the pocket's length of a drawn section is known
            float near = MedianDressOn ? MedianEvalNearM : 30f;
            bool fastE = MedianDressOn && MedianFast(map, e);
            float sFrom = sections[0].s, sTo = sections[n - 1].s;
            if (CurbMedianAll)
            {
                if (SplitNode(map, e, e.a)) sFrom += CurbMedianSplitSetbackM;
                if (SplitNode(map, e, e.b)) sTo -= CurbMedianSplitSetbackM;
            }
            var o = tm.origin;
            bool any = false;
            for (int k = 0; k < n; k++)
            {
                var S = sections[k];
                var ed = S.Edge(side);
                float wx = ed.x + o.x, wz = ed.z + o.z;
                if (wx < min.x - near || wx > max.x + near || wz < min.y - near || wz > max.y + near) continue;
                cmEv[k] = true; cmTw[k] = -1;
                if (S.s < sFrom || S.s > sTo) continue;
                if (k > 0 && !WalkableSpan(k, side)) continue;
                if (k + 1 < n && !WalkableSpan(k + 1, side)) continue;
                var pw = new Vector2(wx, wz);
                var outw = S.Out(side);
                // a median nose graded flush (Five Points) stays as it is
                if (NoseFlushAt(map, trims, pw, outw, ed.y, out _) > 0f) continue;
                float reach = CurbMedianAll ? CurbMedianWideMaxM : CurbMedianMaxM;
                GatherNear(map, pw, pw, reach + 1f);
                ClearRun(map, trims, pw, outw, ed.y, reach, out float oy, out float edgeRun, out bool intoFan);
                int blk = clearRunBlockEdge;
                if (float.IsNaN(edgeRun) || intoFan || blk < 0 || edgeRun < CurbMedianMinM || edgeRun > reach) continue;
                if (Mathf.Abs(oy - ed.y) > CurbMedianMaxDyM) continue;
                // the pavement met is the twin: one-way, this name, running the
                // other way, and itself of the island's class (a deck, ramp or
                // refuge twin keeps its own side: no half island against it)
                var tw = map.edges[blk];
                if (tw.index == e.index || !tw.oneway || tw.name != e.name) continue;
                if (CurbMedianAll && !CurbMedianClass(tw)) continue;
                CityElevation.ProjectOn(tw, pw + outw * edgeRun, out float sTw);
                if (Vector2.Dot(tw.TangentAt(sTw).normalized, e.TangentAt(S.s).normalized) > -0.7f) continue;
                float dTw = float.MaxValue;
                if (CurbMedianAll)
                {
                    // BOTH HALVES DECIDE ALIKE (the island is one median, not
                    // two verges): the twin's own tests, read from here - its
                    // split setback, the ray back from its edge (a fan or other
                    // road on ITS side stops it), its ramp from its stopping
                    // ends. A half standing where the other is not left the
                    // tuck's 17-30 cm face bare at the axis (the junction
                    // sweep's steps at nodes where a link or side street meets
                    // one carriageway).
                    if ((sTw < CurbMedianSplitSetbackM && SplitNode(map, tw, tw.a)) || (tw.length - sTw < CurbMedianSplitSetbackM && SplitNode(map, tw, tw.b))) continue;
                    var q = pw + outw * edgeRun;
                    ClearRun(map, trims, q, -outw, oy, reach, out _, out float backRun, out bool backFan);
                    if (float.IsNaN(backRun) || backFan || clearRunBlockEdge != e.index || Mathf.Abs(backRun - edgeRun) > 1f) continue;
                    if (CurbMedianStops(map, tw, tw.a)) dTw = Mathf.Min(dTw, sTw);
                    if (CurbMedianStops(map, tw, tw.b)) dTw = Mathf.Min(dTw, tw.length - sTw);
                }
                cmT[k] = dTw;
                cmG[k] = edgeRun; cmTw[k] = tw.index; cmTwS[k] = sTw;
                cmFast[k] = fastE && MedianFast(map, tw);
                cmW[k] = 0.5f * edgeRun;
                // the mean of the two edges: the curbed island's top 15 cm over
                // it, a wide grass median's at the edges' own grade
                cmY[k] = 0.5f * (ed.y + oy);
                cmK[k] = cmC[k] = edgeRun <= CurbMedianMaxM ? 1f : 0f;
                any = true;
            }
            if (!any) return;
            // the ramp: in from every stop (a section without an island, a
            // junction end, a node where the carriageway goes on as one that
            // lays none - a deck, a refuge pair, a ramp)
            float s0 = sections[0].s, s1 = sections[n - 1].s;
            bool endA = CurbMedianStops(map, e, e.a), endB = CurbMedianStops(map, e, e.b);
            for (int k = 0; k < n; k++)
            {
                if (cmW[k] <= 0f) continue;
                float sk = sections[k].s, d = cmT[k], dc = float.MaxValue;
                if (endA) d = Mathf.Min(d, sk - s0);
                if (endB) d = Mathf.Min(d, s1 - sk);
                for (int j = 0; j < n; j++)
                {
                    if (cmW[j] <= 0f) d = Mathf.Min(d, Mathf.Abs(sections[j].s - sk));
                    else if (cmK[j] != cmK[k]) dc = Mathf.Min(dc, 0.5f * Mathf.Abs(sections[j].s - sk));
                }
                cmH[k] = LineModel.Smooth(d / KerbEndRampM);
                // the curb fades out where the median widens past CurbMedianMaxM
                // (and in where it narrows): the change half way between
                if (dc < float.MaxValue) cmC[k] = cmK[k] > 0.5f ? 0.5f + 0.5f * LineModel.Smooth(dc / KerbEndRampM) : 0.5f - 0.5f * LineModel.Smooth(dc / KerbEndRampM);
            }
            // the median dress: each section's top (concrete, a planted bed, grass)
            PrepareMedianDress(map, e, n);
        }

        /// <summary>A node where a two-way edge of this name parts into (or
        /// joins from) the one-way carriageways.</summary>
        static bool SplitNode(CityMap map, CityMap.Edge e, int node)
        {
            var ne = map.nodeEdges[node];
            if (ne == null) return false;
            foreach (int i in ne)
            {
                var o = map.edges[i];
                if (o.index != e.index && !o.oneway && o.name == e.name) return true;
            }
            return false;
        }

        /// <summary>Does the island stop at this end node: a junction, a dead
        /// end, or the carriageway going on as an edge that lays none.</summary>
        static bool CurbMedianStops(CityMap map, CityMap.Edge e, int node)
        {
            var ne = map.nodeEdges[node];
            if (!CurbMedianAll) return ne == null || ne.Count != 2;   // the spots: as shipped
            if (ne == null) return true;
            // city-wide: it goes on unless the carriageway does not - a side
            // street or link meeting it on the outer side leaves the median
            // whole (a real opening is a junction fan, which the ray finds)
            foreach (int i in ne)
            {
                if (i == e.index) continue;
                var o = map.edges[i];
                if (o.name != e.name || !o.oneway) continue;
                bool onward = node == e.b ? o.a == node : o.b == node;
                if (!onward) continue;
                // and runs on the same way (not the twin leaving a split)
                var te = node == e.b ? e.TangentAt(e.length) : e.TangentAt(0f);
                var to = node == e.b ? o.TangentAt(0f) : o.TangentAt(o.length);
                if (Vector2.Dot(te.normalized, to.normalized) < 0.5f) continue;
                if (CurbMedianEdge(map, o)) return false;
            }
            return true;
        }

        static readonly Vector3[] cmProfA = new Vector3[5], cmProfB = new Vector3[5];

        /// <summary>Span i's side has the island at both ends: its curb hides the inch under the edge (as FullWalkSpan).</summary>
        /// A wide median's flat grass (curb factor under a quarter) keeps the face under the edge.
        static bool FullCurbMedianSpan(CityMap.Edge e, int i, int side) =>
            side > 0 && cmEdgeNow == e.index && i >= 1 && i < sections.Count && cmW[i - 1] > 0f && cmW[i] > 0f
            && cmC[i - 1] >= 0.25f && cmC[i] >= 0.25f;

        /// <summary>The island half's cross-section: curb foot, face top, nose,
        /// the axis, the tuck under the other half. h 0 lays it flat an inch
        /// under the edge (a ramp's end). c is the curb factor: 1 the curbed
        /// island (axis at the edges' mean + CurbH), 0 a wide median's grass
        /// at grade (no curb, the axis at the mean an inch down, as the edge).</summary>
        static void CurbMedianProfile(Vector3 e, Vector2 outw, float w, float yMean, float h, float c, Vector3[] p)
        {
            var o = new Vector3(outw.x, 0f, outw.y);
            float yb = e.y - RoadsideRules.EdgeDropM;
            float hc = h * c;
            float yAxis = yMean + Mathf.Lerp(-RoadsideRules.EdgeDropM, CurbH, c);
            p[0] = new Vector3(e.x, yb, e.z);
            p[1] = e + o * CurbFaceOut; p[1].y = Mathf.Lerp(yb, e.y + CurbFaceH, hc);
            p[2] = e + o * CurbNoseOut; p[2].y = Mathf.Lerp(yb, e.y + CurbH, hc);
            p[3] = e + o * Mathf.Max(w, CurbNoseOut + 0.05f); p[3].y = Mathf.Lerp(yb, yAxis, h);
            p[4] = e + o * (Mathf.Max(w, CurbNoseOut + 0.05f) + CurbMedianTuckM); p[4].y = p[3].y - CurbMedianTuckDownM * h;
        }

        /// <summary>EmitSide's plain-verge span on a curbed median's side:
        /// the curb and the island's half. False: the verge as before.</summary>
        static bool CurbMedianSpan(CityMap map, TileMeshes tm, CityMap.Edge e, int i, int side, Vector3 eA, Vector3 eB, Vector2 outA, Vector2 outB, float sA, float sB)
        {
            if (side <= 0 || cmEdgeNow != e.index || i < 1 || i >= sections.Count) return false;
            float wA = cmW[i - 1], wB = cmW[i];
            if (wA <= 0f && wB <= 0f) return false;
            float hA = wA > 0f ? cmH[i - 1] : 0f, hB = wB > 0f ? cmH[i] : 0f;
            float yA = wA > 0f ? cmY[i - 1] : cmY[i], yB = wB > 0f ? cmY[i] : cmY[i - 1];
            float cA = wA > 0f ? cmC[i - 1] : cmC[i], cB = wB > 0f ? cmC[i] : cmC[i - 1];
            if (wA <= 0f) wA = wB; if (wB <= 0f) wB = wA;
            // the island stops at the end where it is not found: the ends of the
            // other quads match it, flat an inch under the edge there (h 0)
            CurbMedianProfile(eA, outA, wA, yA, hA, cA, cmProfA);
            CurbMedianProfile(eB, outB, wB, yB, hB, cB, cmProfB);
            float uA = sA / WalkTexM, uB = sB / WalkTexM;
            bool curbed = cA > 0f || cB > 0f;
            // the curb: its face (dark, CurbFaceDark) and the rounded top, the walk's texels
            if (curbed)
            {
                CurbFaceQuad(WalkBucket, cmProfA[0], cmProfA[1], cmProfB[1], cmProfB[0], uA, uB);
                WalkBucket.Up(cmProfA[1], cmProfA[2], cmProfB[2], cmProfB[1],
                              new Vector2(uA, walkBandV[1]), new Vector2(uA, walkBandV[2]), new Vector2(uB, walkBandV[2]), new Vector2(uB, walkBandV[1]));
            }
            // the top to the axis and the tuck past it: the median dress (11-09) - concrete
            // at a nose, beside a turn pocket and where it is narrow, a planted bed, or grass
            int top = MedianSpanTop(i);
            var tb = MedianTopBucket(top);
            var o = tm.origin;
            for (int q = curbed ? 2 : 0; q < 4; q++)
            {
                Vector3 a = cmProfA[q], b = cmProfA[q + 1], c = cmProfB[q + 1], d = cmProfB[q];
                tb.Up(a, b, c, d, MedianTopUV(top, a.x + o.x, a.z + o.z), MedianTopUV(top, b.x + o.x, b.z + o.z),
                      MedianTopUV(top, c.x + o.x, c.z + o.z), MedianTopUV(top, d.x + o.x, d.z + o.z));
                if (groundLog != null) { groundLog.Add((groundTag + " curb median", a + o, b + o, c + o)); groundLog.Add((groundTag + " curb median", a + o, c + o, d + o)); }
            }
            // a stop mid-run: the verge beside falls away under the flat end, so
            // its end is closed by a face down into it
            if (cmW[i - 1] <= 0f) CurbMedianCap(tb, o, cmProfA, -Flat2(eB - eA));
            if (cmW[i] <= 0f) CurbMedianCap(tb, o, cmProfB, Flat2(eB - eA));
            // a bed's plants, on the risen island only
            if (top == MedianTopBed && cmW[i - 1] > 0f && cmW[i] > 0f && hA > 0.99f && hB > 0.99f)
                MedianPlantSpan(e, i, eA, eB, outA, outB, sA, sB, cmProfA, cmProfB, wA, wB);
            CurbMedianSpans++;
            if (!curbed) CurbMedianFlatSpans++;
            float len = Vector2.Distance(new Vector2(eA.x, eA.z), new Vector2(eB.x, eB.z));
            if (top == MedianTopConcrete) { MedianConcreteSpans++; MedianConcreteM += len; }
            else if (top == MedianTopBed) { MedianBedSpans++; MedianBedM += len; }
            else { MedianGrassSpans++; MedianGrassM += len; }
            CurbMedianM += len;
            if (!curbed) CurbMedianFlatM += len;
            lock (CurbMedianByEdge)
            {
                CurbMedianByEdge.TryGetValue(e.index, out float m); CurbMedianByEdge[e.index] = m + len;
                if (!curbed) { CurbMedianFlatByEdge.TryGetValue(e.index, out m); CurbMedianFlatByEdge[e.index] = m + len; }
            }
            return true;
        }

        static Vector2 Flat2(Vector3 v) { var f = new Vector2(v.x, v.z); return f.sqrMagnitude > 1e-8f ? f.normalized : Vector2.up; }

        static void CurbMedianCap(Bucket bk, Vector3 o, Vector3[] p, Vector2 facing)
        {
            for (int q = 0; q < 4; q++)
            {
                var a = p[q]; var b = p[q + 1];
                if (Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z)) < 0.01f) continue;
                bk.WallSloped(a, b, a.y - CurbMedianCapM, a.y, b.y - CurbMedianCapM, b.y, facing,
                              (a.x + o.x) / 6f, (b.x + o.x) / 6f, (a.y - CurbMedianCapM) / 6f, a.y / 6f);
            }
        }

        // ------------------------------------------------------------------
        //  THE CURB READS (same pass). Every curb face was drawn from the walk's
        //  own slab texels (walkBandV's first band), so a 15 cm face read as the
        //  same pale concrete as the walk above it - at the driver's eye a pale
        //  line one or two pixels high, the walk looking flush with the road.
        //  The face now samples one column of the slab texture: its control
        //  joint (concrete_pt_4_walk x 170 of 256, about half the slab's
        //  brightness; the Enhanced copy's joint at 340 of 512 is the same U),
        //  run along the face - a darker face in shadow under the pale nose, as
        //  the real 6 in curbs read. UVs only: no vertex, collider or material
        //  changes. PSX_CITY_CURBFACE=0: the face in the slab's texels as before.
        // ------------------------------------------------------------------
        public static bool CurbFaceDarkOn = System.Environment.GetEnvironmentVariable("PSX_CITY_CURBFACE") != "0";
        /// <summary>The slab texture's darkest joint column, as U.</summary>
        const float CurbFaceU = 170.25f / 256f;

        /// <summary>A curb's face quad (foot a0/b0, face top a1/b1) in the
        /// walk bucket, U along metres / WalkTexM.</summary>
        static void CurbFaceQuad(Bucket bk, Vector3 a0, Vector3 a1, Vector3 b1, Vector3 b0, float uA, float uB)
        {
            if (CurbFaceDarkOn)
                bk.Up(a0, a1, b1, b0, new Vector2(CurbFaceU, uA), new Vector2(CurbFaceU, uA), new Vector2(CurbFaceU, uB), new Vector2(CurbFaceU, uB));
            else
                bk.Up(a0, a1, b1, b0, new Vector2(uA, walkBandV[0]), new Vector2(uA, walkBandV[1]), new Vector2(uB, walkBandV[1]), new Vector2(uB, walkBandV[0]));
        }
    }
}
