using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  CURBS 2 + STORM DRAINS + MANHOLES (2026-11-11; the owner on North
    //  Graham Street: "Curbs are sloped and not 90 deg like in real cities.
    //  neighborhoods have gentle sloping curbs to park on. i also still
    //  don't see storm drains or manholes in roads").
    //
    //  THE CAUSE. Every walk street's curb was one profile: the foot an inch
    //  under the drawn edge, an 11 cm face over 10 cm (about 48 deg), the
    //  rounded top to 15 cm 25 cm out (CurbFaceOut/CurbNoseOut) - and along a
    //  street ROLLED further (launch 7, 11-05: the face over 22 cm, the nose
    //  40 cm out, WalkRollFaceOut/WalkRollNoseOut), with the face in the slab
    //  texture's darkest joint column (the curbs pass, 11-05). From the
    //  cockpit that is a dark 45-degree ramp a third of a metre wide - what he
    //  arrowed. Nothing anywhere drew a gutter pan, an inlet or a lid.
    //
    //  THE RULE, by edge, from what the map knows:
    //    VERTICAL  (Charlotte's 2'-6" curb and gutter; Uptown's granite):
    //              every walk street (WalkEdge) that is not a plain residential
    //              one - 15 cm, the face 1.2 cm off plumb over its height, a
    //              4.5 cm rounded nose, light concrete; a 0.6 m concrete gutter
    //              pan on the road's outer 0.6 m (render-only, CurbGutterM).
    //    ROLLED    (the 2'-0" valley gutter / roll curb a car parks across):
    //              a walk street of class 0 (local) outside the Uptown loop
    //              with no non-house building within UrbanFootM of it (it has
    //              a walk only for being near the Square: Fourth Ward,
    //              Dilworth, Elizabeth...) - 10 cm over 45 cm, a low valley
    //              then the roll (max 33%).
    //    A CORNER  takes VERTICAL unless every walk street at its node is
    //              rolled; a street's walk eases from its own class to its
    //              corners' over the WalkBackEndEaseM (8 m) before a junction
    //              (the same ease the backing uses), so no step either way.
    //  Medians, refuge islands and lot curbs keep their own profiles.
    //
    //  STORM INLETS (Charlotte's combination inlet: a cast grate in the
    //  gutter, a black cast hood forming the curb face, a concrete top slab
    //  with a round lid behind): on every walk side, at each sag of the
    //  edge's real profile, every InletPitchM on grade, and just upstream of
    //  the low end's corner (3.5 m before the ribbon's end); none on a
    //  driveway or lot apron, a walk's ramp end, a street tree's grate or
    //  beside a lamp post; nudged up to 3 m to find room. Rolled streets take
    //  the low hood and the grate in the valley.
    //  MANHOLES (24 in lids, 1990s plain cast iron, no words or logos) in
    //  the lanes of every city street (not a freeway, ramp, deck or tunnel):
    //  every ~95-125 m by a hash of the edge, in a lane's wheel path or
    //  centre, never within 25 m of a junction mouth (arrows, stop bars,
    //  crosswalks), never within 0.5 m of a painted line; water valve lids
    //  between them near the curb lane's edge; a lid in the walk now and then.
    //
    //  EVERYTHING HERE BUT THE CURB IS RENDER-ONLY: drawn on the structure
    //  atlas into the tile's render-only structure mesh (Slot.Structure,
    //  tm.structure), a centimetre or two proud of the road or walk it lies
    //  on. The wheels roll on the road and walk colliders, which no detail
    //  changes. No new slot, mesh or baked field.
    //
    //  PSX_CITY_CURBS2=0: the curbs as they were, no gutter, inlets or lids
    //  (the A/B). PSX_CITY_DRAINS=0 / PSX_CITY_LIDS=0: just the inlets / lids
    //  off. PSX_CITY_CURBROLL=0: every walk street vertical (no rolled class).
    // ======================================================================
    public static partial class CityMeshes
    {
        public static bool Curbs2On = System.Environment.GetEnvironmentVariable("PSX_CITY_CURBS2") != "0";
        public static bool DrainsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_DRAINS") != "0";
        public static bool LidsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_LIDS") != "0";
        public static bool CurbRollOn = System.Environment.GetEnvironmentVariable("PSX_CITY_CURBROLL") != "0";

        // ---- the two profiles (x out of the drawn edge, y over it) ----------
        /// <summary>The vertical curb: face top 1.2 cm out at 12.5 cm, the nose 4.5 cm out at 15 cm.</summary>
        public const float VFaceOut = 0.012f, VFaceY = 0.125f, VNoseOut = 0.045f, VCurbH = 0.15f;
        /// <summary>The rolled curb: the valley's top 22 cm out at 2.5 cm, the roll's top 45 cm out at 10 cm.</summary>
        public const float RFaceOut = 0.22f, RFaceY = 0.025f, RNoseOut = 0.45f, RCurbH = 0.10f;
        /// <summary>The concrete gutter pan's width on the road (Charlotte's 2'-6" curb and gutter: 24 in).</summary>
        public const float CurbGutterM = 0.6f;
        /// <summary>How far the render-only details stand proud of what they lie on.</summary>
        const float GutterLiftM = 0.010f, GrateLiftM2 = 0.015f, GrateBarLiftM = 0.021f, LidLiftM = 0.012f, CollarLiftM = 0.008f, WalkLidLiftM = 0.007f;

        /// <summary>Set while a walk span or corner of the curbs pass is laid
        /// (its face in the slab's own texels, not the dark joint column).</summary>
        static bool cs2Walk;

        /// <summary>The curb shape at rolled share r (0 vertical, 1 rolled).</summary>
        // ---- THE COLLIDER'S CHAMFER (measured, Editor/CityCurbDrive, 11-11) ----
        // The wheels are rays straight down from the hub: over a plumb 15 cm
        // face a ray steps the whole height in one tick, where a real 0.3 m
        // tyre climbs it over ~26 cm of its tread. Driven at the drawn curb
        // (collider = mesh) the RX-7 met it as a blow: square on at 10 km/h a
        // vertical kick of 11.8 g, a wheel off for 0.52 s and two jounce-stop
        // strikes (2.7 m/s), against 2.3 g / 0.07 s / 1.3 m/s on the old
        // 45-degree curb; at 30 deg 10 km/h seven strikes up to 9.4 m/s (old:
        // 0.5 g, one at 1.4); at 45 deg 10 km/h 5.9 g and 0.42 s (old 0.8 g,
        // 0 s). A face top 6 cm out still struck (9.9 g square on); 12 cm out
        // drove as the old curb did (2.8 g / 0 s square on; 1.0 g at 45 deg;
        // 0.5 g at 30 deg; 1.4 g at 15 deg 30 km/h). So the COLLIDER keeps a
        // 12 cm chamfer - about the path a tyre's contact takes up a 15 cm
        // step - and the curb is DRAWN plumb in front of it, render-only (the
        // structure mesh: Cs2RenderFace): the chamfer lies wholly behind the
        // drawn face and under its nose. A wheel sits up to 12 cm into the
        // drawn curb's top edge for the instant it climbs.
        // PSX_CITY_CURBCHAMFER=0: the collider plumb as drawn.
        public static bool CurbChamferOn = System.Environment.GetEnvironmentVariable("PSX_CITY_CURBCHAMFER") != "0";
        /// <summary>The vertical curb's collider: the face top this far out, the nose at CNoseOut.</summary>
        public const float CFaceOut = 0.12f, CNoseOut = 0.15f;

        static void Cs2Shape(float r, out float faceOut, out float faceY, out float noseOut, out float h)
        {
            float vf = CurbChamferOn ? CFaceOut : VFaceOut, vn = CurbChamferOn ? CNoseOut : VNoseOut;
            if (Cs2TestFaceOut >= 0f) { vf = Cs2TestFaceOut; vn = Mathf.Max(VNoseOut, Cs2TestFaceOut + 0.03f); }
            faceOut = Mathf.Lerp(vf, RFaceOut, r);
            faceY = Mathf.Lerp(VFaceY, RFaceY, r);
            noseOut = Mathf.Lerp(vn, RNoseOut, r);
            h = Mathf.Lerp(VCurbH, RCurbH, r);
        }

        /// <summary>The rolled share of the walk being laid (LayWalk's caller sets it):
        /// under 0.5 the vertical curb is drawn over the collider's chamfer.</summary>
        static float cs2FaceR = 1f;

        /// <summary>
        /// THE DRAWN CURB over the chamfered collider, between two walk
        /// cross-sections pa / pb (the collider's: foot, chamfer top, nose,
        /// back): the plumb face (1.2 cm batter) to 2.5 cm under the top, the
        /// rounded nose to 4.5 cm out, the top on to the collider's nose -
        /// render-only, light concrete (the structure atlas's girder band, as
        /// the gutter pan), a hair proud. Scaled with the curb's height there
        /// (a driveway's lip, a cut's wing).
        /// </summary>
        static void Cs2RenderFace(Vector3[] pa, Vector3[] pb, float uA, float uB)
        {
            if (!CurbChamferOn || Cs2TestFaceOut >= 0f || cs2FaceR >= 0.5f) return;
            if (!RenderProfile(pa, rfA) || !RenderProfile(pb, rfB)) return;
            Vector2 U(float u, float f) => new Vector2(u, UvPan.y + UvPan.height * f);
            float ua = uA * WalkTexM / 8f, ub = uB * WalkTexM / 8f;
            var outA = rfA[4]; var outB = rfB[4];
            structBucket.Face(rfA[0], rfA[1], rfB[1], rfB[0], -(outA + outB), U(ua, 0f), U(ua, 0.25f), U(ub, 0.25f), U(ub, 0f));
            structBucket.Face(rfA[1], rfA[2], rfB[2], rfB[1], Vector3.up - (outA + outB), U(ua, 0.25f), U(ua, 0.32f), U(ub, 0.32f), U(ub, 0.25f));
            structBucket.Up(rfA[2], rfA[3], rfB[3], rfB[2], U(ua, 0.32f), U(ua, 0.6f), U(ub, 0.6f), U(ub, 0.32f));
            RenderFaceM += Vector2.Distance(new Vector2(pa[0].x, pa[0].z), new Vector2(pb[0].x, pb[0].z));
        }
        static readonly Vector3[] rfA = new Vector3[5], rfB = new Vector3[5];
        /// <summary>Tools: drawn-curb metres laid over a chamfered collider.</summary>
        public static float RenderFaceM;

        static bool RenderProfile(Vector3[] p, Vector3[] r)
        {
            var d = p[2] - p[0]; d.y = 0f;
            float run = d.magnitude;
            if (run < VNoseOut + 0.02f) return false;
            var o = d / run;
            float k = Mathf.Clamp01((p[2].y - p[0].y) / (VCurbH + RoadsideRules.EdgeDropM));
            if (k < 0.05f) return false;
            const float lift = 0.003f;
            r[0] = p[0];
            r[1] = p[0] + o * VFaceOut; r[1].y = p[0].y + (VFaceY + RoadsideRules.EdgeDropM) * k;
            r[2] = p[0] + o * VNoseOut; r[2].y = p[2].y + lift;
            r[3] = p[2]; r[3].y += lift;
            r[4] = o;
            return true;
        }

        /// <summary>The curb drive's lever (Editor/CityCurbDrive): the vertical
        /// curb's face top this far out instead of VFaceOut (a chamfered face,
        /// to measure what a wheel ray makes of it). -1: the drawn curb.</summary>
        public static float Cs2TestFaceOut = -1f;

        /// <summary>Tools: where the builds put inlets (world point on the curb line,
        /// outward plan unit) and lids (world centre, radius), while RecordHardware is set.</summary>
        public static bool RecordHardware;
        public static readonly List<(Vector3 at, Vector2 outw, bool rolled)> InletsBuilt = new List<(Vector3, Vector2, bool)>();
        public static readonly List<(Vector3 at, float r)> LidsBuilt = new List<(Vector3, float)>();

        /// <summary>For the curb drive: is plan point (x, z) on a driveway's or lot's apron?</summary>
        public static bool ApronAtForTools(CityMap map, float x, float z) => ApronAt(map, x, z);

        /// <summary>WalkProfile under the curbs pass: r is the rolled share.</summary>
        static void Cs2Profile(Vector3 e, Vector2 outw, float w, Vector3[] p, float curb, float r)
        {
            float yFoot = e.y - RoadsideRules.EdgeDropM;
            if (w <= 0f) { p[0] = p[1] = p[2] = p[3] = new Vector3(e.x, yFoot, e.z); return; }
            var o = new Vector3(outw.x, 0f, outw.y);
            Cs2Shape(r, out float faceOut, out float faceY, out float noseOut, out float h);
            // a walk narrower than the shape (a ramp end) keeps it inside its width
            faceOut = Mathf.Min(faceOut, 0.4f * w); noseOut = Mathf.Min(noseOut, 0.8f * w);
            p[0] = new Vector3(e.x, yFoot, e.z);
            p[1] = e + o * faceOut; p[1].y = e.y + Mathf.Max(faceY * curb, -0.5f * RoadsideRules.EdgeDropM);
            p[2] = e + o * noseOut; p[2].y = e.y + h * curb;
            p[3] = e + o * w; p[3].y = e.y + h * curb + WalkCrossFall * (w - noseOut);
        }

        /// <summary>WalkBack under the curbs pass (the walk's back an inch up).</summary>
        static Vector3 Cs2Back(Vector3 e, Vector2 outw, float w, float curb, float r)
        {
            if (w <= 0f) return e;
            Cs2Shape(r, out _, out _, out float noseOut, out float h);
            noseOut = Mathf.Min(noseOut, 0.8f * w);
            var b = e + new Vector3(outw.x, 0f, outw.y) * w;
            b.y = e.y + h * curb + WalkCrossFall * (w - noseOut) + RoadsideRules.EdgeDropM;
            return b;
        }

        // ---- the class ------------------------------------------------------
        static CityMap cs2MapFor;
        static sbyte[] cs2EdgeCache, cs2NodeCache;

        static void EnsureCs2Cache(CityMap map)
        {
            if (cs2MapFor == map && cs2EdgeCache != null && cs2EdgeCache.Length == map.edges.Length) return;
            cs2MapFor = map;
            cs2EdgeCache = new sbyte[map.edges.Length];
            cs2NodeCache = new sbyte[map.nodeEdges.Length];
        }

        /// <summary>A rolled-curb (residential) walk street? See the rule above.</summary>
        public static bool RolledEdge(CityMap map, CityMap.Edge e)
        {
            if (!Curbs2On || !CurbRollOn) return false;
            EnsureCs2Cache(map);
            ref sbyte c = ref cs2EdgeCache[e.index];
            if (c == 0)
            {
                bool rolled = e.cls == 0 && WalkEdge(map, e) && !CityPoles.Uptown(map, e.PointAt(e.length * 0.5f));
                if (rolled)
                    for (int k = 1; k <= 3 && rolled; k++)
                        if (map.AnyFootprintNear(e.PointAt(e.length * k * 0.25f), UrbanFootM, nonHouseOnly: true)) rolled = false;
                c = (sbyte)(rolled ? 1 : -1);
            }
            return c > 0;
        }

        /// <summary>A corner's class at a node: rolled only where every walk street there is.</summary>
        static bool RolledNode(CityMap map, int node)
        {
            EnsureCs2Cache(map);
            ref sbyte c = ref cs2NodeCache[node];
            if (c == 0)
            {
                bool any = false, all = true;
                foreach (int ei in map.nodeEdges[node])
                {
                    var o = map.edges[ei];
                    if (!WalkEdge(map, o)) continue;
                    any = true;
                    if (!RolledEdge(map, o)) { all = false; break; }
                }
                c = (sbyte)(any && all ? 1 : -1);
            }
            return c > 0;
        }

        /// <summary>The rolled share at arc s of the edge being laid (its
        /// <see cref="sections"/>): its own class, easing to its corners'
        /// over the WalkBackEndEaseM before a junction.</summary>
        static float Cs2Mix(CityMap map, CityMap.Edge e, float s)
        {
            float own = RolledEdge(map, e) ? 1f : 0f;
            if (sections.Count < 2) return own;
            float r = own;
            if (map.nodeEdges[e.a].Count >= 3)
            {
                float k = LineModel.Smooth(Mathf.Clamp01((s - sections[0].s) / WalkBackEndEaseM));
                r = Mathf.Lerp(RolledNode(map, e.a) ? 1f : 0f, r, k);
            }
            if (map.nodeEdges[e.b].Count >= 3)
            {
                float k = LineModel.Smooth(Mathf.Clamp01((sections[sections.Count - 1].s - s) / WalkBackEndEaseM));
                r = Mathf.Lerp(RolledNode(map, e.b) ? 1f : 0f, r, k);
            }
            return r;
        }

        /// <summary>What a walk span's roll argument is: the curbs pass's rolled
        /// share, else launch 7's ease (or 0, the steep curb, with the wings off).</summary>
        static float WalkRollAt(CityMap map, CityMap.Edge e, float s) =>
            Curbs2On ? Cs2Mix(map, e, s) : CurbWingsOn ? WalkBackEndEase(map, e, s) : 0f;

        // ---- the counts -----------------------------------------------------
        public static class Curbs2Stats
        {
            public static float verticalM, rolledM, gutterM;
            public static int inlets, inletsRolled, inletSag, inletEnd, inletGrade, manholes, valves, walkLids;
            /// <summary>Inlet stations refused: no walk span here (another tile's or no walk), narrow walk, apron, tree, lamp, ramp.</summary>
            public static int noSpan, narrow, apron, tree, lamp, lidNoRoom;
            public static void Reset()
            {
                verticalM = rolledM = gutterM = 0f;
                inlets = inletsRolled = inletSag = inletEnd = inletGrade = manholes = valves = walkLids = 0;
                noSpan = narrow = apron = tree = lamp = lidNoRoom = 0;
            }
            public static string Line() =>
                $"curbs2 {(Curbs2On ? "ON" : "OFF")}: vertical curb {verticalM:0} m, rolled {rolledM:0} m, gutter pan {gutterM:0} m; inlets {inlets} ({inletsRolled} rolled; at sags {inletSag}, upstream of corners {inletEnd}, on grade {inletGrade}); refused: not here {noSpan}, narrow/ramp {narrow}, apron {apron}, tree {tree}, lamp {lamp}; manholes {manholes}, valve lids {valves}, walk lids {walkLids} ({lidNoRoom} lane stations with no room)";
        }

        // ---- the walk spans the edge being laid drew (EmitWalkSpanCore) -------
        struct Cs2Span
        {
            public int side;
            public Vector3 eA, eB; public Vector2 outA, outB;
            public float sA, sB, wA, wB;
            public bool cut;
        }
        static readonly List<Cs2Span> cs2Spans = new List<Cs2Span>(64);
        static int cs2SpansEdge = -1;

        static void Cs2RecordSpan(CityMap.Edge e, int side, Vector3 eA, Vector3 eB, Vector2 outA, Vector2 outB, float sA, float sB, float wA, float wB, bool cut)
        {
            if (cs2SpansEdge != e.index) { cs2Spans.Clear(); cs2SpansEdge = e.index; }
            cs2Spans.Add(new Cs2Span { side = side, eA = eA, eB = eB, outA = outA, outB = outB, sA = sA, sB = sB, wA = wA, wB = wB, cut = cut });
        }

        // ---- the structure atlas's regions these details sample --------------
        // (v, Unity: 0 = the sheet's bottom; CityMeshes.Bridges' bands)
        static readonly Rect UvPan = new Rect(0.00f, 0.900f, 1f, 0.075f);       // girder band: light concrete
        static readonly Rect UvSlab = new Rect(0.10f, 0.520f, 0.30f, 0.200f);   // column band: light concrete
        static readonly Rect UvCollar = new Rect(0.30f, 0.270f, 0.20f, 0.090f); // under band: weathered grey
        static readonly Rect UvIron = new Rect(0.79f, 0.020f, 0.16f, 0.080f);   // luminaire housing: black iron
        static readonly Rect UvIronLid = new Rect(0.28f, 0.020f, 0.18f, 0.080f);// bearing pad: dark grey iron
        static readonly Rect UvRust = new Rect(0.20f, 0.770f, 0.06f, 0.080f);   // weathering steel: rusty iron
        static readonly Vector2 UvSlot = new Vector2(444.5f / 512f, 0.81f);      // the steel band's darkest joint: the dark under a grate

        static Vector2 UvIn(Rect r, float fu, float fv) => new Vector2(r.x + r.width * Mathf.Clamp01(fu), r.y + r.height * Mathf.Clamp01(fv));

        static void SQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Rect r)
        {
            structBucket.Up(a, b, c, d, UvIn(r, 0f, 0f), UvIn(r, 0f, 1f), UvIn(r, 1f, 1f), UvIn(r, 1f, 0f));
        }
        static void SFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, Rect r)
        {
            structBucket.Face(a, b, c, d, facing, UvIn(r, 0f, 0f), UvIn(r, 0f, 1f), UvIn(r, 1f, 1f), UvIn(r, 1f, 0f));
        }
        static void SQuadUv(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uv)
        {
            structBucket.Up(a, b, c, d, uv, uv, uv, uv);
        }

        // ---- the road's plane under a point of a span ------------------------
        /// <summary>The drawn road's height at tile-local plan point p within
        /// the span between sections A and B (bilinear over its four corners).</summary>
        static float RoadYIn(in Section A, in Section B, Vector3 p)
        {
            Vector3 ca = 0.5f * (A.L + A.R), cb = 0.5f * (B.L + B.R);
            var ax = new Vector2(cb.x - ca.x, cb.z - ca.z);
            float l2 = ax.sqrMagnitude;
            float t = l2 > 1e-6f ? Mathf.Clamp01(((p.x - ca.x) * ax.x + (p.z - ca.z) * ax.y) / l2) : 0f;
            Vector3 L = Vector3.Lerp(A.L, B.L, t), R = Vector3.Lerp(A.R, B.R, t);
            var lr = new Vector2(R.x - L.x, R.z - L.z);
            float w2 = lr.sqrMagnitude;
            float u = w2 > 1e-6f ? Mathf.Clamp01(((p.x - L.x) * lr.x + (p.z - L.z) * lr.y) / w2) : 0.5f;
            return Mathf.Lerp(L.y, R.y, u);
        }

        // ---- the gutter pan ---------------------------------------------------
        /// <summary>The 0.6 m concrete pan on the road's outer edge under a walk
        /// sub-span (edge points pA, pB, outward oA, oB; span i's sections),
        /// render-only, GutterLiftM proud. vertical: the share of it to lay
        /// (rolled curbs have their valley in the curb itself).</summary>
        static void Cs2Gutter(int i, Vector3 pA, Vector3 pB, Vector2 oA, Vector2 oB, float uA, float uB, float rA, float rB)
        {
            if (rA > 0.5f && rB > 0.5f) return;
            if (i < 1 || i >= sections.Count) return;
            var A = sections[i - 1]; var B = sections[i];
            Vector3 iA = pA - new Vector3(oA.x, 0f, oA.y) * CurbGutterM, iB = pB - new Vector3(oB.x, 0f, oB.y) * CurbGutterM;
            iA.y = RoadYIn(A, B, iA) + GutterLiftM; iB.y = RoadYIn(A, B, iB) + GutterLiftM;
            Vector3 fA = pA, fB = pB; fA.y += GutterLiftM; fB.y += GutterLiftM;
            // U along (8 m a repeat, the atlas's own), V across the band
            structBucket.Up(fA, iA, iB, fB,
                new Vector2(uA, UvPan.y), new Vector2(uA, UvPan.yMax), new Vector2(uB, UvPan.yMax), new Vector2(uB, UvPan.y));
            Curbs2Stats.gutterM += Vector2.Distance(new Vector2(pA.x, pA.z), new Vector2(pB.x, pB.z));
        }

        // ---- the hardware pass (CityMeshes.cs calls it per edge, after its spans and lamps) ----
        static readonly List<(float s, byte why)> cs2Stations = new List<(float, byte)>(32);
        const byte WhySag = 1, WhyEnd = 2, WhyGrade = 3;
        /// <summary>On-grade inlet spacing: 90-120 m by the edge's hash.</summary>
        const float InletPitchMinM = 90f, InletPitchMaxM = 120f;
        /// <summary>The low end's inlet stands this far before the ribbon's end (the corner's curb return).</summary>
        const float InletEndM = 3.5f;
        /// <summary>A sag: the profile's low point at least this far under both neighbours 10 m away.</summary>
        const float SagDipM = 0.03f;
        const float InletAlongM = 1.0f;

        static uint Cs2Hash(int a, int b)
        {
            unchecked
            {
                uint h = 2166136261u;
                h ^= (uint)a; h *= 16777619u; h ^= (uint)b; h *= 16777619u;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                return h;
            }
        }
        static float Cs2Rand(int a, int b) => (Cs2Hash(a, b) & 0xFFFF) / 65535f;

        static void EmitStreetHardware(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, Vector2 min, Vector2 max)
        {
            if (!Curbs2On) { cs2Spans.Clear(); cs2SpansEdge = -1; return; }
            if (DrainsOn && cs2SpansEdge == e.index && cs2Spans.Count > 0) EmitInlets(map, trims, tm, e);
            if (LidsOn) EmitLids(map, trims, tm, e, min, max);
            cs2Spans.Clear(); cs2SpansEdge = -1;
        }

        /// <summary>The inlet stations of an edge (global: every tile finds the same).</summary>
        static void InletStations(CityMap map, Trims trims, CityMap.Edge e)
        {
            cs2Stations.Clear();
            float s0 = trims.atA[e.index] + InletEndM, s1 = e.length - trims.atB[e.index] - InletEndM;
            if (s1 - s0 < 2f) return;
            // sags of the drawn profile (the real heights)
            for (float s = s0 + 10f; s <= s1 - 10f; s += 5f)
            {
                float y = e.YAt(s);
                if (e.YAt(s - 10f) - y > SagDipM && e.YAt(s + 10f) - y > SagDipM && y <= e.YAt(s - 5f) && y <= e.YAt(s + 5f))
                { cs2Stations.Add((s, WhySag)); s += 20f; }
            }
            // upstream of the low end's corner (both on the level)
            float ya = e.YAt(s0), yb = e.YAt(s1);
            bool endA = map.nodeEdges[e.a].Count >= 3, endB = map.nodeEdges[e.b].Count >= 3;
            if (endA && ya <= yb + 0.15f) cs2Stations.Add((s0, WhyEnd));
            if (endB && yb <= ya + 0.15f && s1 - s0 > 15f) cs2Stations.Add((s1, WhyEnd));
            // on grade between, from the high end down
            float pitch = Mathf.Lerp(InletPitchMinM, InletPitchMaxM, Cs2Rand(e.index, 7));
            int n = Mathf.FloorToInt((s1 - s0) / pitch);
            for (int k = 1; k <= n; k++)
            {
                float s = ya > yb ? s0 + k * pitch : s1 - k * pitch;
                bool near = false;
                foreach (var st in cs2Stations) if (Mathf.Abs(st.s - s) < 30f) { near = true; break; }
                if (!near && s > s0 + 15f && s < s1 - 15f) cs2Stations.Add((s, WhyGrade));
            }
        }

        static void EmitInlets(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e)
        {
            InletStations(map, trims, e);
            if (cs2Stations.Count == 0) return;
            var o = tm.origin;
            foreach (var st in cs2Stations)
                for (int side = -1; side <= 1; side += 2)
                {
                    bool placed = false; byte why = 0;
                    foreach (float nudge in new[] { 0f, 1.5f, -1.5f, 3f, -3f })
                    {
                        float s = st.s + nudge;
                        int k = -1;
                        for (int q = 0; q < cs2Spans.Count; q++)
                        {
                            var sp = cs2Spans[q];
                            if (sp.side != side) continue;
                            float lo = Mathf.Min(sp.sA, sp.sB), hi = Mathf.Max(sp.sA, sp.sB);
                            if (s >= lo && s < hi) { k = q; break; }
                        }
                        if (k < 0) { why = 1; continue; }
                        var spn = cs2Spans[k];
                        float t = (s - spn.sA) / (spn.sB - spn.sA);
                        float w = Mathf.Lerp(spn.wA, spn.wB, t);
                        if (spn.wA < 1.6f || spn.wB < 1.6f || Mathf.Abs(spn.sB - spn.sA) < InletAlongM + 0.2f) { why = 2; continue; }
                        // the whole inlet inside the span
                        float half = 0.5f * (InletAlongM + 0.3f) / Mathf.Abs(spn.sB - spn.sA);
                        if (t - half < 0f || t + half > 1f) { why = 2; continue; }
                        var ed = Vector3.Lerp(spn.eA, spn.eB, t);
                        var outw = Vector2.Lerp(spn.outA, spn.outB, t).normalized;
                        var along = new Vector2(-outw.y, outw.x);
                        var edW = new Vector2(ed.x + o.x, ed.z + o.z);
                        bool ap = false;
                        for (int q = -1; q <= 1 && !ap; q++)
                        {
                            var c = edW + along * (q * 1.2f) + outw * 0.6f;
                            ap = ApronAt(map, c.x, c.y) || OnDriveway(c, 0.5f);
                        }
                        if (ap) { why = 3; continue; }
                        if (StreetTreeGap(map, trims, e, s) < 2.2f) { why = 4; continue; }
                        bool lampNear = false;
                        foreach (var lp in tm.lamps)
                            if (Vector2.Distance(new Vector2(lp.foot.x + o.x, lp.foot.z + o.z), edW + outw * 0.5f) < 1.6f) { lampNear = true; break; }
                        if (lampNear) { why = 5; continue; }
                        float r = Cs2Mix(map, e, s);
                        int iSpan = SpanIndexAt(s);
                        if (iSpan < 1) { why = 1; continue; }
                        DrawInlet(iSpan, spn, t, w, r);
                        if (RecordHardware) InletsBuilt.Add((ed + o, outw, r > 0.5f));
                        Curbs2Stats.inlets++;
                        if (r > 0.5f) Curbs2Stats.inletsRolled++;
                        if (st.why == WhySag) Curbs2Stats.inletSag++; else if (st.why == WhyEnd) Curbs2Stats.inletEnd++; else Curbs2Stats.inletGrade++;
                        placed = true;
                        break;
                    }
                    if (!placed)
                        switch (why)
                        {
                            case 1: Curbs2Stats.noSpan++; break;
                            case 2: Curbs2Stats.narrow++; break;
                            case 3: Curbs2Stats.apron++; break;
                            case 4: Curbs2Stats.tree++; break;
                            case 5: Curbs2Stats.lamp++; break;
                        }
                }
        }

        /// <summary>The span (sections index i, i - 1) whose arc holds s, or -1.</summary>
        static int SpanIndexAt(float s)
        {
            for (int i = 1; i < sections.Count; i++)
                if (s >= sections[i - 1].s && s <= sections[i].s) return i;
            return -1;
        }

        /// <summary>
        /// One combination inlet at drawn edge point ed (tile-local), outward
        /// outw, walk w wide, rolled share r: the grate in the gutter, the
        /// hood over its throat in the curb face, the top slab and its lid in
        /// the walk behind. All render-only (the structure mesh).
        /// </summary>
        static void DrawInlet(int i, Cs2Span sp, float t, float w, float r)
        {
            var A = sections[i - 1]; var B = sections[i];
            var outw = Vector2.Lerp(sp.outA, sp.outB, t).normalized;
            var o3 = new Vector3(outw.x, 0f, outw.y);
            var d3 = sp.eB - sp.eA; float len = new Vector2(d3.x, d3.z).magnitude;
            var a3 = new Vector3(d3.x, 0f, d3.z) / Mathf.Max(len, 1e-3f);
            // the drawn edge (with its height, so a grade is followed) along the curb from the station
            Vector3 Ed(float along) => Vector3.LerpUnclamped(sp.eA, sp.eB, t + along / Mathf.Max(len, 1e-3f));
            bool rolled = r > 0.5f;
            Cs2Shape(r, out _, out _, out float noseOut, out float h);
            // ---- the grate: 0.9 m along, 0.55 m across, in the gutter --------
            const float gAlong = 0.9f, gIn0 = 0.04f, gIn1 = 0.59f;
            float gl = gAlong * 0.5f;
            Vector3 P(float along, float lat, float lift)
            {
                var p = Ed(along) + o3 * lat;
                p.y = RoadYIn(A, B, p) + lift;
                return p;
            }
            // frame (black iron) and the dark under it
            SQuad(P(-gl - 0.04f, -gIn0 + 0.02f, GrateLiftM2), P(-gl - 0.04f, -gIn1 - 0.04f, GrateLiftM2), P(gl + 0.04f, -gIn1 - 0.04f, GrateLiftM2), P(gl + 0.04f, -gIn0 + 0.02f, GrateLiftM2), UvIron);
            SQuadUv(P(-gl, -gIn0, GrateLiftM2 + 0.002f), P(-gl, -gIn1, GrateLiftM2 + 0.002f), P(gl, -gIn1, GrateLiftM2 + 0.002f), P(gl, -gIn0, GrateLiftM2 + 0.002f), UvSlot);
            // bars across the flow (bicycle-safe), rusty iron, 11 of them, and a centre spine along
            const int bars = 11;
            for (int b = 0; b < bars; b++)
            {
                float c = -gl + (b + 0.5f) * (gAlong / bars), hw = 0.022f;
                SQuad(P(c - hw, -gIn0, GrateBarLiftM), P(c - hw, -gIn1, GrateBarLiftM), P(c + hw, -gIn1, GrateBarLiftM), P(c + hw, -gIn0, GrateBarLiftM), UvRust);
            }
            float mid = -0.5f * (gIn0 + gIn1);
            SQuad(P(-gl, mid - 0.02f, GrateBarLiftM + 0.001f), P(-gl, mid + 0.02f, GrateBarLiftM + 0.001f), P(gl, mid + 0.02f, GrateBarLiftM + 0.001f), P(gl, mid - 0.02f, GrateBarLiftM + 0.001f), UvRust);

            // ---- the hood over the throat, in the curb face ------------------
            // The black cast hood stands 2 cm proud of the face from the throat's
            // top to a centimetre over the curb's top; under it the throat: its
            // underside and the curb face behind it, dark (the opening a car
            // sees into: the hood's 2 cm and the shadowed slot under it).
            float hl = 0.55f;                         // half its length along
            float proud = rolled ? 0.03f : 0.02f;
            float yThroat = rolled ? 0.045f : 0.065f; // the opening's height over the gutter
            float yTop = h + 0.01f;
            float back = rolled ? noseOut + 0.03f : noseOut + 0.06f;
            Vector3 Hd(float along, float lat, float y)
            {
                var ea = Ed(along);
                var p = ea + o3 * lat;
                p.y = ea.y + y;
                return p;
            }
            const float th = -0.003f;   // the throat's dark back: just in front of the curb's face
            var slot = new Rect(UvSlot.x, UvSlot.y, 0f, 0f);
            SFace(Hd(-hl, th, -0.02f), Hd(-hl, th, yThroat), Hd(hl, th, yThroat), Hd(hl, th, -0.02f), -o3, slot);
            structBucket.Down(Hd(-hl, -proud, yThroat), Hd(-hl, th, yThroat), Hd(hl, th, yThroat), Hd(hl, -proud, yThroat), UvSlot, UvSlot, UvSlot, UvSlot);
            // the hood's front and top
            SFace(Hd(-hl, -proud, yThroat), Hd(-hl, -proud, yTop), Hd(hl, -proud, yTop), Hd(hl, -proud, yThroat), -o3, UvIron);
            SQuad(Hd(-hl, -proud, yTop), Hd(-hl, back, yTop), Hd(hl, back, yTop), Hd(hl, -proud, yTop), UvIron);
            // its two ends (on a rolled curb, down to the roll behind)
            float yEnd = rolled ? h * 0.9f : yThroat;
            float latEnd = rolled ? back : th;
            SFace(Hd(-hl, -proud, yThroat), Hd(-hl, -proud, yTop), Hd(-hl, latEnd, yTop), Hd(-hl, latEnd, yEnd), -a3, UvIron);
            SFace(Hd(hl, -proud, yThroat), Hd(hl, -proud, yTop), Hd(hl, latEnd, yTop), Hd(hl, latEnd, yEnd), a3, UvIron);

            // ---- the top slab behind, and its round lid ----------------------
            float d0 = back + 0.02f, d1 = Mathf.Min(back + 1.0f, w - 0.3f);
            if (d1 - d0 > 0.6f)
            {
                float sl = 0.65f;
                Vector3 W(float along, float d, float lift)
                {
                    var p = SlabAt(Ed(along), outw, w, r, d);
                    p.y += lift;
                    return p;
                }
                SQuad(W(-sl, d0, WalkLidLiftM), W(-sl, d1, WalkLidLiftM), W(sl, d1, WalkLidLiftM), W(sl, d0, WalkLidLiftM), UvSlab);
                float lidR = Mathf.Min(0.30f, 0.45f * (d1 - d0));
                float dm = 0.5f * (d0 + d1);
                var c = W(0f, dm, WalkLidLiftM + 0.003f);
                var gx = W(0.3f, dm, WalkLidLiftM + 0.003f) - c; var gz = W(0f, dm + 0.3f, WalkLidLiftM + 0.003f) - c;
                Lid(c, lidR, 0f, true, PlaneGrad(gx, gz));
            }
        }

        /// <summary>The plane's height gradient (dy/dx, dy/dz, world axes) from two
        /// non-parallel steps on it.</summary>
        static Vector2 PlaneGrad(Vector3 u, Vector3 v)
        {
            float det = u.x * v.z - u.z * v.x;
            if (Mathf.Abs(det) < 1e-6f) return Vector2.zero;
            return new Vector2((u.y * v.z - v.y * u.z) / det, (u.x * v.y - v.x * u.y) / det);
        }

        /// <summary>A 24 in cast-iron lid at tile-local centre c (its top at c.y),
        /// radius r, on the plane of height gradient grad (dy/dx, dy/dz);
        /// collar: a concrete ring round it (0 none). Waffle: a grid of raised
        /// bars inside a rim - plain 1990s cast iron, no words.</summary>
        static void Lid(Vector3 c, float r, float collar, bool waffle, Vector2 grad)
        {
            const int n = 16;
            Vector3 On(Vector3 p, float dy) { p.y = c.y + dy + grad.x * (p.x - c.x) + grad.y * (p.z - c.z); return p; }
            if (collar > 0f)
            {
                for (int k = 0; k < n; k++)
                {
                    float a0 = k * Mathf.PI * 2f / n, a1 = (k + 1) * Mathf.PI * 2f / n;
                    float dy = -(LidLiftM - CollarLiftM);
                    Vector3 i0 = On(c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r, dy), i1 = On(c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r, dy);
                    Vector3 o0 = On(c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * (r + collar), dy), o1 = On(c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * (r + collar), dy);
                    SQuad(i0, o0, o1, i1, UvCollar);
                }
            }
            // the disc (dark iron)
            var uc = UvIn(UvIronLid, 0.5f, 0.5f);
            for (int k = 0; k < n; k++)
            {
                float a0 = k * Mathf.PI * 2f / n, a1 = (k + 1) * Mathf.PI * 2f / n;
                Vector3 p0 = On(c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r, 0f), p1 = On(c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r, 0f);
                var u0 = UvIn(UvIronLid, 0.5f + 0.5f * Mathf.Cos(a0), 0.5f + 0.5f * Mathf.Sin(a0));
                var u1 = UvIn(UvIronLid, 0.5f + 0.5f * Mathf.Cos(a1), 0.5f + 0.5f * Mathf.Sin(a1));
                // Tri faces up when wound clockwise in map view (Quad's (a,c,b) rule)
                structBucket.Tri(c, p1, p0, uc, u1, u0);
                if (structBucket.LastNormal().y < 0f)
                {
                    int t = structBucket.t.Count;
                    (structBucket.t[t - 1], structBucket.t[t - 2]) = (structBucket.t[t - 2], structBucket.t[t - 1]);
                }
            }
            if (!waffle) return;
            // the rim and a waffle of raised bars (rusty iron), 2 mm proud
            float rim = 0.035f;
            for (int k = 0; k < n; k++)
            {
                float a0 = k * Mathf.PI * 2f / n, a1 = (k + 1) * Mathf.PI * 2f / n;
                Vector3 i0 = On(c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * (r - rim), 0.002f), i1 = On(c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * (r - rim), 0.002f);
                Vector3 o0 = On(c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r, 0.002f), o1 = On(c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r, 0.002f);
                SQuad(i0, o0, o1, i1, UvRust);
            }
            float inner = r - rim, bw = 0.012f;
            for (int g = -2; g <= 2; g++)
            {
                float x = g * inner * 0.36f;
                float hz = Mathf.Sqrt(Mathf.Max(0f, inner * inner - x * x)) * 0.97f;
                if (hz < 0.03f) continue;
                SQuad(On(c + new Vector3(x - bw, 0f, -hz), 0.002f), On(c + new Vector3(x - bw, 0f, hz), 0.002f), On(c + new Vector3(x + bw, 0f, hz), 0.002f), On(c + new Vector3(x + bw, 0f, -hz), 0.002f), UvRust);
                SQuad(On(c + new Vector3(-hz, 0f, x - bw), 0.0025f), On(c + new Vector3(hz, 0f, x - bw), 0.0025f), On(c + new Vector3(hz, 0f, x + bw), 0.0025f), On(c + new Vector3(-hz, 0f, x + bw), 0.0025f), UvRust);
            }
        }

        // ---- manholes and valve lids in the lanes ---------------------------
        const float LidPitchMinM = 95f, LidPitchMaxM = 125f, LidJunctionClearM = 25f, ManholeR = 0.31f, ValveR = 0.11f, LidLineClearM = 0.5f;
        static readonly List<float> cs2Lines = new List<float>(16);
        static readonly List<byte> cs2Kinds = new List<byte>(16);

        static void EmitLids(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, Vector2 min, Vector2 max)
        {
            if (e.link || e.cls > 3 || e.bridge || e.tunnel || e.a == e.b) return;
            if (sections.Count < 2) return;
            float s0 = trims.atA[e.index] + LidJunctionClearM, s1 = e.length - trims.atB[e.index] - LidJunctionClearM;
            if (s1 - s0 < 5f) return;
            var pr = RoadProfiles.All[Mathf.Clamp(e.profile, 0, RoadProfiles.All.Length - 1)];
            RoadProfiles.PaintLines(pr, cs2Lines, cs2Kinds);
            float pitch = Mathf.Lerp(LidPitchMinM, LidPitchMaxM, Cs2Rand(e.index, 11));
            float phase = Cs2Rand(e.index, 13) * pitch;
            var o = tm.origin;
            int k = 0;
            for (float s = s0 + phase * 0.5f; s <= s1; s += pitch, k++)
            {
                // the manhole, then a valve lid 30-45 m on
                for (int kind = 0; kind < 2; kind++)
                {
                    float sk = kind == 0 ? s : s + Mathf.Lerp(30f, 45f, Cs2Rand(e.index, 100 + k));
                    if (sk > s1) continue;
                    int i = SpanIndexAt(sk);
                    if (i < 1) continue;
                    var A = sections[i - 1]; var B = sections[i];
                    var f = spanFlags[i];
                    if (f.skip || f.elev || e.ElevatedAt(sk)) continue;
                    float t = (sk - A.s) / Mathf.Max(B.s - A.s, 1e-3f);
                    Vector3 L = Vector3.Lerp(A.L, B.L, t), R = Vector3.Lerp(A.R, B.R, t);
                    float wd = Vector2.Distance(new Vector2(L.x, L.z), new Vector2(R.x, R.z));
                    if (wd < 3f) continue;
                    float scale = wd / pr.Width;
                    // where across, metres from R (the left of travel, the paint's m)
                    float m;
                    uint hsh = Cs2Hash(e.index, 1000 + k * 2 + kind);
                    if (kind == 0)
                    {
                        // a lane by the hash, its centre or a wheel path
                        int lanes = Mathf.Max(1, pr.lanes - (pr.turnLane ? 1 : 0));
                        int lane = (int)(hsh % (uint)lanes);
                        // skip a TWLTL's centre lane
                        float laneStart = pr.shl + lane * RoadProfiles.LaneM;
                        if (pr.turnLane && !pr.oneway)
                        {
                            int nb = (pr.lanes - 1) / 2;
                            if (lane >= nb) laneStart += RoadProfiles.LaneM;
                        }
                        float off = ((hsh >> 8) % 3u) switch { 0u => 0f, 1u => -0.85f, _ => 0.85f };
                        m = laneStart + 0.5f * RoadProfiles.LaneM + off;
                    }
                    else
                    {
                        // a valve near the curb lane's edge, either side
                        m = ((hsh >> 4) & 1u) == 0u ? pr.shl + 1.3f : pr.Width - pr.shr - 1.3f;
                    }
                    float rad = kind == 0 ? ManholeR : ValveR;
                    // never on (or within LidLineClearM of) a painted line, or in the gutter pan
                    bool clash = m * scale - rad < CurbGutterM + 0.15f || wd - m * scale - rad < CurbGutterM + 0.15f;
                    foreach (float ln in cs2Lines) if (Mathf.Abs(ln - m) * scale < rad + LidLineClearM) { clash = true; break; }
                    if (clash) { Curbs2Stats.lidNoRoom++; continue; }
                    var c = Vector3.Lerp(R, L, m * scale / wd);
                    var cw = new Vector2(c.x + o.x, c.z + o.z);
                    if (cw.x < min.x || cw.x >= max.x || cw.y < min.y || cw.y >= max.y) continue;   // the tile that holds it draws it
                    c.y = RoadYIn(A, B, c) + LidLiftM;
                    var cx = c + new Vector3(0.3f, 0f, 0f); cx.y = RoadYIn(A, B, cx) + LidLiftM;
                    var cz = c + new Vector3(0f, 0f, 0.3f); cz.y = RoadYIn(A, B, cz) + LidLiftM;
                    var grad = PlaneGrad(cx - c, cz - c);
                    if (kind == 0) { Lid(c, ManholeR, 0.16f, true, grad); Curbs2Stats.manholes++; if (RecordHardware) LidsBuilt.Add((c + o, ManholeR)); }
                    else { Lid(c, ValveR, 0.05f, false, grad); Curbs2Stats.valves++; }
                }
            }
            // a lid in the walk now and then (a telephone or water vault in the slab)
            if (cs2SpansEdge == e.index)
                foreach (var sp in cs2Spans)
                {
                    float len = Mathf.Abs(sp.sB - sp.sA);
                    if (len < 2f || sp.cut || sp.wA < 2f || sp.wB < 2f) continue;
                    float smid = 0.5f * (sp.sA + sp.sB);
                    if (Cs2Rand(e.index * 7 + (sp.side + 1), Mathf.FloorToInt(smid / 10f)) > 0.06f) continue;
                    if (StreetTreeGap(map, trims, e, smid) < 2.5f) continue;
                    var ed = Vector3.Lerp(sp.eA, sp.eB, 0.5f);
                    var ow = Vector2.Lerp(sp.outA, sp.outB, 0.5f).normalized;
                    float w = 0.5f * (sp.wA + sp.wB);
                    float rr = Cs2Mix(map, e, smid), dd = Mathf.Min(1.4f, w - 0.5f);
                    var c = SlabAt(ed, ow, w, rr, dd);
                    var cw = new Vector2(c.x + o.x, c.z + o.z);
                    if (ApronAt(map, cw.x, cw.y)) continue;
                    var al = (sp.eB - sp.eA); float ln = new Vector2(al.x, al.z).magnitude;
                    var c2 = SlabAt(Vector3.Lerp(sp.eA, sp.eB, 0.5f + 0.3f / Mathf.Max(ln, 0.3f)), ow, w, rr, dd);
                    var c3 = SlabAt(ed, ow, w, rr, dd + 0.3f);
                    c.y += WalkLidLiftM;
                    Lid(c, 0.30f, 0f, true, PlaneGrad(c2 - (c - Vector3.up * WalkLidLiftM), c3 - (c - Vector3.up * WalkLidLiftM)));
                    Curbs2Stats.walkLids++;
                }
        }
    }
}
