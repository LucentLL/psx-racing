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
    //  SCOPE (ship safely): PSX_CITY_CURBMEDIAN unset = the owner's spots
    //  only (CurbMedianEdges), and only on the city export
    //  it was checked on (graph hash CurbMedianGraph); =all: every divided
    //  city street with walks (the rule, for a later publish); =0: the verge
    //  as before (the A/B).
    // ======================================================================
    public static partial class CityMeshes
    {
        static readonly string CurbMedianMode = System.Environment.GetEnvironmentVariable("PSX_CITY_CURBMEDIAN") ?? "";
        public static bool CurbMedianOn => CurbMedianMode != "0";
        static bool CurbMedianAll => CurbMedianMode == "all";
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
        /// <summary>The island's width from the edge to the twin's edge.</summary>
        const float CurbMedianMinM = 0.8f, CurbMedianMaxM = 12f;
        /// <summary>The two carriageways' edges at most this far apart in height.</summary>
        const float CurbMedianMaxDyM = 0.5f;
        /// <summary>Past the axis, under the other half: this far, this far down.</summary>
        const float CurbMedianTuckM = 0.3f, CurbMedianTuckDownM = 0.25f;
        /// <summary>A stopped island's end face reaches this far under its top.</summary>
        const float CurbMedianCapM = 0.6f;

        public static int CurbMedianSpans; public static float CurbMedianM;
        /// <summary>The last solve's islands per edge (metres), for the probes.</summary>
        public static readonly Dictionary<int, float> CurbMedianByEdge = new Dictionary<int, float>();

        static float[] cmW = new float[128], cmY = new float[128], cmH = new float[128];
        static int cmEdgeNow = -1;

        static bool CurbMedianEdge(CityMap map, CityMap.Edge e)
        {
            if (!CurbMedianOn || !e.oneway || e.link || e.cls > 3 || e.bridge || e.tunnel || e.a == e.b || string.IsNullOrEmpty(e.name) || Barriered(e)) return false;
            if (!CurbMedianAll && (map.graphHash != CurbMedianGraph || !CurbMedianEdges.Contains(e.index))) return false;
            if (LineModel.RefugeOf(e.index) != null) return false;   // the refuge pairs keep their own kerb
            // a city street with walks whose left side faces its twin
            return WalkEdge(map, e) && !WalkSide(map, e, +1);
        }

        /// <summary>
        /// Per section of the edge being drawn (after PrepareWalks): the
        /// median-side island's half width to the axis (0 none), the axis
        /// height and the ramp factor. Side +1 only (the left of travel).
        /// </summary>
        static void PrepareCurbMedian(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, Vector2 min, Vector2 max)
        {
            int n = sections.Count;
            if (cmW.Length < n) { cmW = new float[n * 2]; cmY = new float[n * 2]; cmH = new float[n * 2]; }
            System.Array.Clear(cmW, 0, n); System.Array.Clear(cmH, 0, n);
            cmEdgeNow = e.index;
            if (!CurbMedianEdge(map, e)) return;
            const int side = +1;
            const float near = 30f;
            var o = tm.origin;
            bool any = false;
            for (int k = 0; k < n; k++)
            {
                var S = sections[k];
                var ed = S.Edge(side);
                float wx = ed.x + o.x, wz = ed.z + o.z;
                if (wx < min.x - near || wx > max.x + near || wz < min.y - near || wz > max.y + near) continue;
                if (k > 0 && !WalkableSpan(k, side)) continue;
                if (k + 1 < n && !WalkableSpan(k + 1, side)) continue;
                var pw = new Vector2(wx, wz);
                var outw = S.Out(side);
                // a median nose graded flush (Five Points) stays as it is
                if (NoseFlushAt(map, trims, pw, outw, ed.y, out _) > 0f) continue;
                GatherNear(map, pw, pw, CurbMedianMaxM + 1f);
                ClearRun(map, trims, pw, outw, ed.y, CurbMedianMaxM, out float oy, out float edgeRun, out bool intoFan);
                int blk = clearRunBlockEdge;
                if (float.IsNaN(edgeRun) || intoFan || blk < 0 || edgeRun < CurbMedianMinM || edgeRun > CurbMedianMaxM) continue;
                if (Mathf.Abs(oy - ed.y) > CurbMedianMaxDyM) continue;
                // the pavement met is the twin: one-way, this name, running the other way
                var tw = map.edges[blk];
                if (tw.index == e.index || !tw.oneway || tw.name != e.name) continue;
                CityElevation.ProjectOn(tw, pw + outw * edgeRun, out float sTw);
                if (Vector2.Dot(tw.TangentAt(sTw).normalized, e.TangentAt(S.s).normalized) > -0.7f) continue;
                cmW[k] = 0.5f * edgeRun;
                cmY[k] = 0.5f * (ed.y + oy) + CurbH;
                any = true;
            }
            if (!any) return;
            // the ramp: in from every stop (a section without an island, a junction end)
            float s0 = sections[0].s, s1 = sections[n - 1].s;
            bool endA = map.nodeEdges[e.a] == null || map.nodeEdges[e.a].Count != 2;
            bool endB = map.nodeEdges[e.b] == null || map.nodeEdges[e.b].Count != 2;
            for (int k = 0; k < n; k++)
            {
                if (cmW[k] <= 0f) continue;
                float sk = sections[k].s, d = float.MaxValue;
                if (endA) d = Mathf.Min(d, sk - s0);
                if (endB) d = Mathf.Min(d, s1 - sk);
                for (int j = 0; j < n; j++) if (cmW[j] <= 0f) d = Mathf.Min(d, Mathf.Abs(sections[j].s - sk));
                cmH[k] = LineModel.Smooth(d / KerbEndRampM);
            }
        }

        static readonly Vector3[] cmProfA = new Vector3[5], cmProfB = new Vector3[5];

        /// <summary>Span i's side has the island at both ends: its curb hides the inch under the edge (as FullWalkSpan).</summary>
        static bool FullCurbMedianSpan(CityMap.Edge e, int i, int side) =>
            side > 0 && cmEdgeNow == e.index && i >= 1 && i < sections.Count && cmW[i - 1] > 0f && cmW[i] > 0f;

        /// <summary>The island half's cross-section: curb foot, face top, nose,
        /// the axis, the tuck under the other half. h 0 lays it flat an inch
        /// under the edge (a ramp's end).</summary>
        static void CurbMedianProfile(Vector3 e, Vector2 outw, float w, float yAxis, float h, Vector3[] p)
        {
            var o = new Vector3(outw.x, 0f, outw.y);
            float yb = e.y - RoadsideRules.EdgeDropM;
            p[0] = new Vector3(e.x, yb, e.z);
            p[1] = e + o * CurbFaceOut; p[1].y = Mathf.Lerp(yb, e.y + CurbFaceH, h);
            p[2] = e + o * CurbNoseOut; p[2].y = Mathf.Lerp(yb, e.y + CurbH, h);
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
            if (wA <= 0f) wA = wB; if (wB <= 0f) wB = wA;
            // the island stops at the end where it is not found: the ends of the
            // other quads match it, flat an inch under the edge there (h 0)
            CurbMedianProfile(eA, outA, wA, yA, hA, cmProfA);
            CurbMedianProfile(eB, outB, wB, yB, hB, cmProfB);
            float uA = sA / WalkTexM, uB = sB / WalkTexM;
            // the curb: its face (dark, CurbFaceDark) and the rounded top, the walk's texels
            CurbFaceQuad(WalkBucket, cmProfA[0], cmProfA[1], cmProfB[1], cmProfB[0], uA, uB);
            WalkBucket.Up(cmProfA[1], cmProfA[2], cmProfB[2], cmProfB[1],
                          new Vector2(uA, walkBandV[1]), new Vector2(uA, walkBandV[2]), new Vector2(uB, walkBandV[2]), new Vector2(uB, walkBandV[1]));
            // the top to the axis and the tuck past it: grass (the planted island)
            bool conc = MedianConcreteWays.Contains(e.wayId);
            var o = tm.origin;
            for (int q = 2; q < 4; q++)
            {
                Vector3 a = cmProfA[q], b = cmProfA[q + 1], c = cmProfB[q + 1], d = cmProfB[q];
                GroundBucket(conc).Up(a, b, c, d, GroundUV(conc, a.x + o.x, a.z + o.z), GroundUV(conc, b.x + o.x, b.z + o.z),
                                      GroundUV(conc, c.x + o.x, c.z + o.z), GroundUV(conc, d.x + o.x, d.z + o.z));
            }
            // a stop mid-run: the verge beside falls away under the flat end, so
            // its end is closed by a face down into it
            if (cmW[i - 1] <= 0f) CurbMedianCap(conc, o, cmProfA, -Flat2(eB - eA));
            if (cmW[i] <= 0f) CurbMedianCap(conc, o, cmProfB, Flat2(eB - eA));
            CurbMedianSpans++;
            float len = Vector2.Distance(new Vector2(eA.x, eA.z), new Vector2(eB.x, eB.z));
            CurbMedianM += len;
            lock (CurbMedianByEdge) { CurbMedianByEdge.TryGetValue(e.index, out float m); CurbMedianByEdge[e.index] = m + len; }
            return true;
        }

        static Vector2 Flat2(Vector3 v) { var f = new Vector2(v.x, v.z); return f.sqrMagnitude > 1e-8f ? f.normalized : Vector2.up; }

        static void CurbMedianCap(bool conc, Vector3 o, Vector3[] p, Vector2 facing)
        {
            var bk = GroundBucket(conc);
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
