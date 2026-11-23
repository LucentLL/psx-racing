using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  CURBS 4 ("hoods 2", 2026-11-15 afternoon).
    //
    //  1. THE STANDARD SECTION (PSX_CITY_STRIP=0: the walk from the curb).
    //     The owner at Beatties Ford Rd: "that is a LOT of curb on the side of
    //     the road". Drawn there, lane edge outward: pan 0.60 m, a curb top
    //     0.40 m deep (to the collider's nose), the walk slab from the curb to
    //     2.4 m, a paved frontage. The standard (NCDOT 846.01 / CLDSM 10.17A
    //     2'-6" curb and gutter; Charlotte's street sections U-02A and the
    //     Streets Manual's avenue / boulevard): pan 2 ft, curb top 6 in, an 8 ft
    //     planting strip, a 5-6 ft walk; a hardscape amenity zone to the curb
    //     only on a main street.
    //     Now, by rule per walk street (StripOf):
    //       - Uptown inside the loop, a dressed street, a storefront block (a
    //         non-house wall within StoreFrontM of the pavement along half the
    //         street): the walk from the curb, as before;
    //       - a local street (the pre-war house streets): curb top 0.152 m,
    //         planting strip 1.83 m, walk 1.52 m;
    //       - a collector / arterial: curb top 0.152 m, strip 1.5 m (the street
    //         views of 11-15: 1-2 m of grass; first drawn 2.25 m = 8 ft from
    //         the gutter line less the curb, kept inside the 4 m the generated
    //         frontage leaves), walk 1.52 m.
    //     The strip gives way first where room is short (StripMinM, then the
    //     walk from the curb); it is concrete across a driveway's or lot's
    //     apron and for CornerLandM before a junction (the ramps' landing).
    //     A strip street's frontage is lawn (WalkFrontage).
    //     THE COLLIDER IS UNCHANGED: 11-11's chamfer (the only one that drove),
    //     now GRASS behind the drawn curb - the drawn plumb face and a 6 in top
    //     (render-only, with a back face down to the grass) stand over its
    //     first 15 cm; from the curb's back on, collider and picture are one.
    //     The planting strip (for the street trees): from 0.152 m to 0.152 m +
    //     strip behind the face of curb (the drawn pavement edge), its middle
    //     1.07 m (local) / 1.28 m (collector, arterial) from that edge.
    //  2. THE WALK KEEPS OFF BUILDINGS (PSX_CITY_WALKCLEAR=0: as before): a
    //     walk section is no wider than the clear ground out to the first
    //     building's box; and a driveway's curb cut is also read 0.35 m behind
    //     the curb (the 8 driveways left with a curb face across them were
    //     0.5-0.9 m long - a town house at the pavement's edge - and the cut
    //     was read only at the walk's middle, 1.2 m out).
    //  3. THE ROLL CURB IS THE VALLEY GUTTER (PSX_CITY_VALLEY=0: 11-11's
    //     roll): CLDSM 10.17B, 2 ft wide - the flow line 3/4 in under the lip
    //     6-1/2 in from it, a 25 in concave arc into an 18 in convex one, the
    //     back 3 in over the lip. Five points: lip, flow line (0.165, -0.019),
    //     (0.33, 0.003), (0.50, 0.0625), back (0.61, 0.076); 11-11's was 6 cm at
    //     0.18 m and 10 cm at 0.45 m.
    //  4. CUL-DE-SACS (PSX_CITY_STDBULBS=0 / PSX_CITY_BULBCURB=0): a local
    //     street that simply ends in the map extract (charlotte_deadends.txt:
    //     no other way carries on from its end, not at the extract's edge) and
    //     is a court by name or has a building beyond its end, with room, gets
    //     the CLDSM 11.16 bulb: 31 ft (9.45 m) to the gutter lip, on the
    //     street's centreline, through the turning-circle code (BulbTrims).
    //     A bulb's rim takes its street's curb and gutter (Cs4BulbCurb); the
    //     walk and strip stop at the throat, as the detail draws them.
    // ======================================================================
    public static partial class CityMeshes
    {
        public static bool StripOn = System.Environment.GetEnvironmentVariable("PSX_CITY_STRIP") != "0";
        public static bool ValleyOn = System.Environment.GetEnvironmentVariable("PSX_CITY_VALLEY") != "0";
        public static bool BulbCurbOn = System.Environment.GetEnvironmentVariable("PSX_CITY_BULBCURB") != "0";
        public static bool StdBulbsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_STDBULBS") != "0";
        public static bool WalkClearOn = System.Environment.GetEnvironmentVariable("PSX_CITY_WALKCLEAR") != "0";
        /// <summary>(roadsmerge 11-23) the two-lane collector / arterial shoulder section with no curb (hoods 2, 0ba58918): OFF until
        /// the line gate passes with it - with it on, its newly painted edge lines put existing taper and kink faults on the
        /// gate as new places (SHIFT +2, SJOG +1, KINK +4, WIDTH +2 city-wide). PSX_CITY_SHOULDER2=1: on.</summary>
        public static bool ShoulderSectionOn = System.Environment.GetEnvironmentVariable("PSX_CITY_SHOULDER2") == "1";

        /// <summary>(the lanes round's curb close-ups, scratchpad lanes/EDGES.md version 2, 13:45: N Graham St at 1400 and
        /// McClintock Rd, looked at from above) On the OLD city house streets the asphalt runs to the curb: there is no
        /// gutter pan to see. So the pre-war house streets (local, near the Square, no shops: the class's resident
        /// bit on a vertical curb) are laid without the render-only pan; every other curbed street keeps its 2 ft pan
        /// (certain on the rebuilt state-road sections). Render-only: no collider changes. PSX_CITY_OLDNOPAN=0: the pan there too.</summary>
        public static bool OldNoPanOn = System.Environment.GetEnvironmentVariable("PSX_CITY_OLDNOPAN") != "0";
        static bool Cs4NoPan(CityMap map, CityMap.Edge e)
        {
            if (!OldNoPanOn || !StripOn || !Curbs3On || e == null || e.cls != 0) return false;
            int bits = Cs3ClassBits(map, e);
            return (bits & 7) == CurbVertical && (bits & CurbResidBit) != 0;
        }
        /// <summary>Set while a corner (or a bulb's rim) is laid whose streets all go without a pan.</summary>
        static bool cs4CornerNoPan;

        /// <summary>The curb's top (6 in), the standard walk (5 ft), the planting strips.</summary>
        public const float CurbTopM = 0.152f, WalkStdM = 1.52f, StripLocalM = 1.83f, StripMajorM = 1.5f;   // (the street views: grass 1-2 m on avenues and divided arterials, 1.5-2 m on local streets)
        const float StripMinM = 0.5f, StoreFrontM = 8f, CornerLandM = 3f;
        /// <summary>CLDSM 11.16: the standard bulb, 31 ft to the gutter lip.</summary>
        public const float StdBulbR = 9.45f;

        public static class Cs4Stats
        {
            public static float stripM, bulbCurbM, apronM;
            public static int bulbCurbs;
            /// <summary>Local dead ends without a tagged turning circle, and what each is (Cs4Turns).</summary>
            public static int deadLocal, std, carriesOn, graphCut, notInExtract, plainStub, noRoom;
            public static void Reset() { stripM = bulbCurbM = apronM = 0f; bulbCurbs = 0; }
            public static string Line() => $"curbs4: planting strip {stripM:0} m of walk side, apron panels {apronM:0} m; bulb rims curbed {bulbCurbs} ({bulbCurbM:0} m)";
            public static string BulbLine() =>
                $"standard bulbs {(StdBulbsOn ? "ON" : "OFF")}: {deadLocal} local dead ends with no tagged turning circle: {std} take the CLDSM 11.16 bulb (before the street-too-short test); the rest: another way carries on {carriesOn}, the graph was cut there {graphCut}, not in the extract or at its edge {notInExtract}, a plain stub (no court name, nothing built beyond) {plainStub}, no room {noRoom}";
        }

        // ---- 1. the section rule ---------------------------------------------
        static CityMap cs4StripMap; static float[] cs4StripCache;

        /// <summary>The planting strip this walk street asks for (0: the walk runs from the curb).</summary>
        public static float StripOf(CityMap map, CityMap.Edge e)
        {
            if (!StripOn || !Curbs3On || !WalksOn) return 0f;
            if (cs4StripMap != map || cs4StripCache == null || cs4StripCache.Length != map.edges.Length)
            {
                cs4StripMap = map; cs4StripCache = new float[map.edges.Length];
                for (int i = 0; i < cs4StripCache.Length; i++) cs4StripCache[i] = -1f;
            }
            ref float c = ref cs4StripCache[e.index];
            if (c >= 0f) return c;
            int cc = Cs3ClassBits(map, e) & 7;
            if (cc != CurbVertical)
            {
                if (cc != CurbNone || CityHouses.BoundTo(map)) c = 0f;   // (a class not known before the buildings are bound is not kept)
                return 0f;
            }
            float s;
            if (CityPoles.Uptown(map, e.PointAt(e.length * 0.5f)) || (StreetDressOn && StreetDressOf(map, e) != DressNone)) s = 0f;
            else
            {
                int n = Mathf.Clamp(Mathf.CeilToInt(e.length / BuiltStepM), 1, 16), hit = 0;
                for (int k = 0; k < n; k++)
                    if (map.AnyFootprintNear(e.PointAt(e.length * (k + 0.5f) / n), e.width * 0.5f + StoreFrontM, nonHouseOnly: true)) hit++;
                s = hit * 2 >= n ? 0f : e.cls == 0 ? StripLocalM : StripMajorM;
            }
            c = s;
            return s;
        }

        /// <summary>The strip at a section whose walk is w wide: what is left after the curb's top and the walk, none under StripMinM.</summary>
        static float Cs4StripAt(float w, float strip)
        {
            float s = Mathf.Min(strip, w - CurbTopM - WalkStdM);
            return s >= StripMinM ? s : 0f;
        }

        /// <summary>Set by EmitWalkSpanCore: the strip of the street being laid.</summary>
        static float cs4StripNow;

        static bool Cs4NearJunction(CityMap map, CityMap.Edge e, float s)
        {
            if (sections.Count < 2) return false;
            if (map.nodeEdges[e.a].Count >= 3 && s - sections[0].s < CornerLandM) return true;
            if (map.nodeEdges[e.b].Count >= 3 && sections[sections.Count - 1].s - s < CornerLandM) return true;
            return false;
        }

        /// <summary>A corner's walk on a strip street: the plain walk from the curb (WalkWOf), as the corners were laid before
        /// the strip - the ramps' landing. (The whole section at a corner lost 86 of the sample's 1090 corner walks: P on pavement, no room.)</summary>
        static float Cs4CornerW(CityMap map, CityMap.Edge e) => StripOf(map, e) > 0f ? WalkWOf(map, e) : Cs3WalkW(map, e);
        /// <summary>The width a street's walk asks for at arc s of the edge being laid: the standard section, but the corner's own
        /// (Cs4CornerW) within CornerLandM of a junction end, so the street's end section meets its corner as before.</summary>
        static float Cs4WalkWAt(CityMap map, CityMap.Edge e, float s) => StripOf(map, e) > 0f && Cs4NearJunction(map, e, s) ? WalkWOf(map, e) : Cs3WalkW(map, e);

        static void Cs4InsertBreak(float t)
        {
            if (t <= 0.002f || t >= 0.998f) return;
            int j = 1;
            while (j < cutT.Count && cutT[j] < t) j++;
            if (j >= cutT.Count || t - cutT[j - 1] < 0.002f || cutT[j] - t < 0.002f) return;
            float f = (t - cutT[j - 1]) / (cutT[j] - cutT[j - 1]);
            float c = Mathf.Lerp(cutC[j - 1], cutC[j], f), ca = Mathf.Lerp(cutA[j - 1], cutA[j], f);
            cutT.Insert(j, t); cutC.Insert(j, c); cutA.Insert(j, ca);
        }

        /// <summary>EmitWalkSpanCore: the breaks the strip needs in this span (where the corner's landing ends).</summary>
        static void Cs4SplitSpan(CityMap map, CityMap.Edge e, float sA, float sB)
        {
            if (cs4StripNow <= 0f || sB - sA < 0.05f || sections.Count < 2) return;
            if (map.nodeEdges[e.a].Count >= 3) Cs4InsertBreak((sections[0].s + CornerLandM - sA) / (sB - sA));
            if (map.nodeEdges[e.b].Count >= 3) Cs4InsertBreak((sections[sections.Count - 1].s - CornerLandM - sA) / (sB - sA));
        }

        static readonly Vector3[] r4A = new Vector3[6], r4B = new Vector3[6];
        /// <summary>The drawn curb of a strip section over the collider's section p (foot, chamfer top, nose, back):
        /// foot, face top, nose, the top's back 6 in out, the back face's foot on the grass, and the outward.</summary>
        static bool Cs4CurbProfile(Vector3[] p, Vector3[] r)
        {
            var d = p[2] - p[0]; d.y = 0f;
            float run = d.magnitude;
            if (run < CurbTopM + 0.02f) return false;
            var o = d / run;
            float k = Mathf.Clamp01((p[2].y - p[0].y) / (VCurbH + RoadsideRules.EdgeDropM));
            if (k < 0.05f) return false;
            float yTop = p[2].y + 0.003f;
            r[0] = p[0];
            r[1] = p[0] + o * VFaceOut; r[1].y = p[0].y + (VFaceY + RoadsideRules.EdgeDropM) * k;
            r[2] = p[0] + o * VNoseOut; r[2].y = yTop;
            r[3] = p[0] + o * CurbTopM; r[3].y = yTop;
            float f1 = Flat(p[1] - p[0]);
            float yc = CurbTopM <= f1 ? Mathf.Lerp(p[0].y, p[1].y, CurbTopM / Mathf.Max(f1, 1e-3f)) : Mathf.Lerp(p[1].y, p[2].y, (CurbTopM - f1) / Mathf.Max(run - f1, 1e-3f));
            r[4] = r[3]; r[4].y = Mathf.Min(yc - 0.01f, yTop);
            r[5] = o;
            return true;
        }

        /// <summary>A strip street's curb cut comes up over the drop curb's transition (3 ft), not the walks' 5 m wing:
        /// its walk stays level across the cut (Cs4LayStrip), so nothing dips under a wheel riding the walk.</summary>
        public const float Cs4CutTaperM = 0.91f;
        static readonly Vector3[] cs4FullA = new Vector3[4], cs4FullB = new Vector3[4];

        /// <summary>
        /// One piece of a strip street's walk between two sections: the
        /// collider's chamfer and the strip in the ground's grass, the walk
        /// slab behind, the drawn curb (plumb face, 6 in top, back face).
        /// AT A CURB CUT (the share c0 / c1 of the curb's height) only the curb
        /// drops: the walk stays level, the strip is the apron's ramp from the
        /// lip up to the walk - and where both ends are at the lip (the apron
        /// itself) curb, ramp and walk are ONE panel of the driveway's own
        /// plain concrete (the ground's pavement), the walk's line carried
        /// across it. False (nothing laid) where the piece takes the paved
        /// section: the corner's landing, no room for a strip.
        /// </summary>
        static bool Cs4LayStrip(TileMeshes tm, Vector3 pA, Vector2 oA, float w0, float c0, Vector3 pB, Vector2 oB, float w1, float c1,
                                bool landing, float uA, float uB, out Vector3 backA, out Vector3 backB)
        {
            backA = backB = default;
            if (landing || w0 <= 0f || w1 <= 0f) return false;
            float s0 = Cs4StripAt(w0, cs4StripNow), s1 = Cs4StripAt(w1, cs4StripNow);
            if (s0 <= 0f && s1 <= 0f) return false;
            WalkProfile(pA, oA, w0, walkProfA, c0, 0f);   // the curb at the cut's share
            WalkProfile(pB, oB, w1, walkProfB, c1, 0f);
            WalkProfile(pA, oA, w0, cs4FullA, 1f, 0f);    // the walk's own plane: level across a cut
            WalkProfile(pB, oB, w1, cs4FullB, 1f, 0f);
            Vector3 S(Vector3[] p, float w, float s)
            {
                float nose = Flat(p[2] - p[0]);
                float x = s > 0f ? CurbTopM + s : nose;
                return Vector3.Lerp(p[2], p[3], Mathf.Clamp01((x - nose) / Mathf.Max(w - nose, 1e-3f)));
            }
            Vector3 SA = S(cs4FullA, w0, s0), SB = S(cs4FullB, w1, s1);
            bool apron = c0 <= CurbCutShare + 0.02f && c1 <= CurbCutShare + 0.02f;
            var o = tm.origin;
            var gb = GroundBucket(apron);
            void G(Vector3 a, Vector3 b, Vector3 c, Vector3 d2, string what)
            {
                gb.Up(a, b, c, d2, GroundUV(apron, a.x + o.x, a.z + o.z), GroundUV(apron, b.x + o.x, b.z + o.z), GroundUV(apron, c.x + o.x, c.z + o.z), GroundUV(apron, d2.x + o.x, d2.z + o.z));
                LogWalkQuad(what, a, b, c, d2);
            }
            G(walkProfA[0], walkProfA[1], walkProfB[1], walkProfB[0], " strip chamfer0");
            G(walkProfA[1], walkProfA[2], walkProfB[2], walkProfB[1], " strip chamfer1");
            G(walkProfA[2], SA, SB, walkProfB[2], apron ? " apron ramp" : " strip grass");
            if (apron) G(SA, cs4FullA[3], cs4FullB[3], SB, " apron walk");
            else
            {
                float vA = Flat(cs4FullA[3] - SA) / WalkTexM, vB = Flat(cs4FullB[3] - SB) / WalkTexM;
                WalkBucket.Up(SA, cs4FullA[3], cs4FullB[3], SB, new Vector2(uA, 0f), new Vector2(uA, vA), new Vector2(uB, vB), new Vector2(uB, 0f));
                LogWalkQuad(" strip walk", SA, cs4FullA[3], cs4FullB[3], SB);
            }
            // the drawn curb
            if (Cs4CurbProfile(walkProfA, r4A) && Cs4CurbProfile(walkProfB, r4B))
            {
                var band = UvPan;
                Vector2 U(float u, float fv) => new Vector2(u, band.y + band.height * fv);
                float ua = uA * WalkTexM / 8f, ub = uB * WalkTexM / 8f;
                var outSum = r4A[5] + r4B[5];
                structBucket.Face(r4A[0], r4A[1], r4B[1], r4B[0], -outSum, U(ua, 0f), U(ua, 0.25f), U(ub, 0.25f), U(ub, 0f));
                structBucket.Face(r4A[1], r4A[2], r4B[2], r4B[1], Vector3.up - outSum, U(ua, 0.25f), U(ua, 0.32f), U(ub, 0.32f), U(ub, 0.25f));
                structBucket.Up(r4A[2], r4A[3], r4B[3], r4B[2], U(ua, 0.32f), U(ua, 0.6f), U(ub, 0.6f), U(ub, 0.32f));
                structBucket.Face(r4A[3], r4A[4], r4B[4], r4B[3], outSum, U(ua, 0.6f), U(ua, 0.75f), U(ub, 0.75f), U(ub, 0.6f));
                RenderFaceM += Vector2.Distance(new Vector2(pA.x, pA.z), new Vector2(pB.x, pB.z));
            }
            backA = cs4FullA[3] + Vector3.up * RoadsideRules.EdgeDropM;
            backB = cs4FullB[3] + Vector3.up * RoadsideRules.EdgeDropM;
            float lm = Vector2.Distance(new Vector2(pA.x, pA.z), new Vector2(pB.x, pB.z));
            if (apron) Cs4Stats.apronM += lm; else Cs4Stats.stripM += lm;
            return true;
        }

        /// <summary>A walk-from-the-curb street's apron piece (both ends at the cut's lip): the walk's three bands in the
        /// driveway's own plain concrete (the ground's pavement), not the jointed slab.</summary>
        static void Cs4LayApron(TileMeshes tm, Vector3[] pa, Vector3[] pb)
        {
            var o = tm.origin;
            var gb = GroundBucket(true);
            for (int q = 0; q < 3; q++)
            {
                Vector3 a = pa[q], b = pa[q + 1], c = pb[q + 1], d2 = pb[q];
                gb.Up(a, b, c, d2, GroundUV(true, a.x + o.x, a.z + o.z), GroundUV(true, b.x + o.x, b.z + o.z), GroundUV(true, c.x + o.x, c.z + o.z), GroundUV(true, d2.x + o.x, d2.z + o.z));
                LogWalkQuad(" apron band" + q, a, b, c, d2);
            }
            Cs4Stats.apronM += Vector2.Distance(new Vector2(pa[0].x, pa[0].z), new Vector2(pb[0].x, pb[0].z));
        }

        // ---- 3. the valley gutter ----------------------------------------------
        /// <summary>Set while a roll-curb-alone street, its corner or its bulb is laid with the valley gutter's section.</summary>
        static bool cs4Valley;
        /// <summary>Where the vertical face stands at an inlet of a rolled street: on the roll's back line.</summary>
        static float Cs3FaceLine(float r) => (cs4Valley ? CurbOnlyW : RNoseOut) * r;

        /// <summary>
        /// The valley gutter (CLDSM 10.17B) in five points at rolled share r:
        /// q the lip's foot under the road's edge, p[0] the flow line, p[1] and
        /// p[2] on the concave and the convex arc, p[3] the back. r = 0: the
        /// vertical section (its pan of no width: p[0] = q). A ramp end keeps
        /// the shape inside its width.
        /// </summary>
        static void Cs4ValleyBase(Vector3 e, Vector2 outw, float w, Vector3[] p, out Vector3 q, float curb, float r)
        {
            Cs2Profile(e, outw, w, p, curb, 0f);
            q = p[0];
            if (w <= 0f || r <= 0f) return;
            var o = new Vector3(outw.x, 0f, outw.y);
            float sc = Mathf.Min(1f, w / CurbOnlyW);
            Vector3 V(float x, float y) { var v = e + o * (x * sc); v.y = e.y + y * curb; return v; }
            Vector3 v0 = V(0.165f, -0.019f), v1 = V(0.33f, 0.003f), v2 = V(0.50f, 0.0625f), v3 = V(0.61f, 0.076f);
            if (w > CurbOnlyW) { v3 = e + o * w; v3.y = e.y + 0.076f * curb + WalkCrossFall * (w - CurbOnlyW); }
            p[0] = Vector3.Lerp(p[0], v0, r); p[1] = Vector3.Lerp(p[1], v1, r); p[2] = Vector3.Lerp(p[2], v2, r); p[3] = Vector3.Lerp(p[3], v3, r);
        }

        // ---- 4. cul-de-sacs -----------------------------------------------------
        static Dictionary<int, char> cs4Ends; static bool cs4EndsRead;
        /// <summary>charlotte_deadends.txt: the graph's dead-end nodes against the map extract.</summary>
        static Dictionary<int, char> Cs4DeadEnds()
        {
            if (cs4EndsRead) return cs4Ends;
            cs4EndsRead = true;
            try
            {
                var ta = Resources.Load<TextAsset>("charlotte_deadends");
                if (ta == null) return null;
                var d = new Dictionary<int, char>();
                foreach (var line in ta.text.Split('\n'))
                {
                    if (line.Length < 3 || line[0] == '#') continue;
                    int sp = line.IndexOf(' ');
                    if (sp > 0 && int.TryParse(line.Substring(0, sp), out int node) && sp + 1 < line.Length) d[node] = line[sp + 1];
                }
                cs4Ends = d;
            }
            catch (System.Exception ex) { Debug.LogWarning("[Curbs4] charlotte_deadends not read: " + ex.Message); cs4Ends = null; }
            return cs4Ends;
        }

        static CityMap cs4TurnsMap; static CityMap.Turn[] cs4Turns;
        static readonly HashSet<int> cs4Segs = new HashSet<int>();
        static readonly string[] cs4CourtNames = { " Court", " Place", " Cove", " Circle", " Close" };

        /// <summary>
        /// BulbTrims' turning circles: the map's tagged ones, and (part 4) the
        /// standard bulb at every local dead end the rule gives one. Map data
        /// only (no buildings bound yet when the trims are made).
        /// </summary>
        public static CityMap.Turn[] Cs4Turns(CityMap map)
        {
            if (!StdBulbsOn || !Curbs3On) return map.turns;
            if (cs4TurnsMap == map && cs4Turns != null) return cs4Turns;
            var list = new List<CityMap.Turn>();
            var tagged = new HashSet<int>();
            if (map.turns != null) foreach (var t in map.turns) { list.Add(t); tagged.Add(t.node); }
            var ends = Cs4DeadEnds();
            Cs4Stats.deadLocal = Cs4Stats.std = Cs4Stats.carriesOn = Cs4Stats.graphCut = Cs4Stats.notInExtract = Cs4Stats.plainStub = Cs4Stats.noRoom = 0;
            if (ends != null)
                for (int nd = 0; nd < map.nodes.Length; nd++)
                {
                    if (map.nodeEdges[nd].Count != 1 || tagged.Contains(nd)) continue;
                    var e = map.edges[map.nodeEdges[nd][0]];
                    if (e.link || e.cls != 0 || e.tunnel || e.bridge || e.a == e.b) continue;
                    Cs4Stats.deadLocal++;
                    if (!ends.TryGetValue(nd, out char st)) st = 'X';
                    if (st == 'C') { Cs4Stats.carriesOn++; continue; }
                    if (st == 'T') { Cs4Stats.graphCut++; continue; }
                    if (st != 'E') { Cs4Stats.notInExtract++; continue; }
                    var c = map.nodes[nd];
                    bool endB = e.b == nd;
                    var tan = e.TangentAt(endB ? e.length : 0f) * (endB ? 1f : -1f);
                    // a court by name, or something built beyond the end: a turnaround; else a plain stub
                    bool court = false;
                    string nm = e.name ?? "";
                    foreach (var suffix in cs4CourtNames) if (nm.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase)) { court = true; break; }
                    if (!court && !map.AnyFootprintNear(c + tan * (StdBulbR + 9f), 11f)) { Cs4Stats.plainStub++; continue; }
                    // room: no building on the bulb, no other road within 3 m of its rim
                    bool room = !map.AnyFootprintNear(c, StdBulbR + 1.5f);
                    if (room)
                    {
                        float reach = StdBulbR + 3f;
                        cs4Segs.Clear();
                        map.EdgeSegsInRect(c - Vector2.one * (reach + 12f), c + Vector2.one * (reach + 12f), cs4Segs);
                        foreach (int packed in cs4Segs)
                        {
                            int oi = packed >> 12, si = packed & 0xFFF;
                            if (oi == e.index) continue;
                            var oe = map.edges[oi];
                            if (si + 1 >= oe.pts.Length) continue;
                            Vector2 a = oe.pts[si], ab = oe.pts[si + 1] - a;
                            float l2 = ab.sqrMagnitude;
                            float tq = l2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(c - a, ab) / l2) : 0f;
                            if (Vector2.Distance(c, a + ab * tq) < reach + oe.width * 0.5f) { room = false; break; }
                        }
                    }
                    if (!room) { Cs4Stats.noRoom++; continue; }
                    list.Add(new CityMap.Turn { node = nd, kind = 0, r = StdBulbR });
                    Cs4Stats.std++;
                }
            cs4TurnsMap = map; cs4Turns = list.ToArray();
            return cs4Turns;
        }

        /// <summary>Does this dead end's bulb carry the street's curb round it (so the street's walk runs to its end)?</summary>
        static bool Cs4BulbCurbAt(CityMap map, Trims trims, CityMap.Edge e, int node) =>
            BulbCurbOn && Curbs3On && trims.bulb != null && trims.bulb[node] && WalkEdge(map, e);

        /// <summary>
        /// The fan's perimeter (CityMeshes.cs): a bulb's rim takes its street's
        /// curb and gutter - the roll curb of a roll-curb street, else the
        /// vertical curb and pan alone (CLDSM 11.16: the walk and the strip stop
        /// at the throat) - from the late mouth corner round to the early one,
        /// the lawn from its back. The rim's chords go into walkGroupOf (their
        /// plain kerb and verge are not laid).
        /// </summary>
        static void Cs4BulbCurb(CityMap map, Trims trims, TileMeshes tm, List<FanCorner> ring, int n)
        {
            if (!BulbCurbOn || !Curbs3On || ring.Count < 5 || trims.bulb == null || !trims.bulb[n]) return;
            int ei = ring[0].edge;
            if (ei < 0 || ring[1].edge != ei) return;
            var e = map.edges[ei];
            if (!WalkSide(map, e, ring[0].side) || !WalkSide(map, e, ring[1].side)) return;
            bool rolled = RolledEdge(map, e), curbOnly = CurbOnly(map, e);
            float r = rolled ? 1f : 0f;
            bool valley = ValleyOn && curbOnly;
            float w = curbOnly ? CurbOnlyW : rolled ? RNoseOut / 0.8f + 0.05f : CNoseOut / 0.8f + 0.02f;
            int nR = ring.Count;
            var o = tm.origin;
            Vector2 OutAt(int j)
            {
                if (j == 0 || j == 1) return ArmOut(map, trims, ring[j], o);
                var a = ring[j - 1].pos; var b = ring[(j + 1) % nR].pos;
                var d = new Vector2(b.x - a.x, b.z - a.z);
                return new Vector2(d.y, -d.x).normalized;   // the perimeter runs anticlockwise: outward is its right
            }
            var verge = new StripShape { shoulder = VergeShoulderM, maxRun = VergeMaxRunM, noFins = true };
            bool wasGranite = cs2Granite, wasPaver = walkPaverNow;
            cs2Granite = false; walkPaverNow = false;
            cs4CornerNoPan = Cs4NoPan(map, e);
            if (groundLog != null) groundTag = $"bulb curb node {n} e{ei}";
            float u = 0f;
            for (int j = 1; j < nR; j++)
            {
                int jn = (j + 1) % nR;
                Vector3 a = ring[j].pos, b = ring[jn].pos;
                float len = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                walkGroupOf.Add(j);
                if (len < 0.02f) continue;
                Vector2 oa = OutAt(j), ob = OutAt(jn);
                if (valley)
                {
                    cs4Valley = true;
                    Cs3Profile(a, oa, w, walkProfA, out var qa, 1f, r, 0f);
                    Cs3Profile(b, ob, w, walkProfB, out var qb, 1f, r, 0f);
                    Cs3LayPiece(WalkBucket, a, oa, qa, walkProfA, b, ob, qb, walkProfB, r, r, u / WalkTexM, (u + len) / WalkTexM);
                    cs4Valley = false;
                }
                else
                {
                    WalkProfile(a, oa, w, walkProfA, 1f, r);
                    WalkProfile(b, ob, w, walkProfB, 1f, r);
                    cs2FaceR = r;
                    LayWalk(WalkBucket, walkProfA, walkProfB, w, w, u / WalkTexM, (u + len) / WalkTexM);
                    if (r < 0.5f) Cs2CornerPan(a, b, oa, ob, u / WalkTexM, (u + len) / WalkTexM, r);
                }
                Vector3 backA = walkProfA[3] + Vector3.up * RoadsideRules.EdgeDropM, backB = walkProfB[3] + Vector3.up * RoadsideRules.EdgeDropM;
                EmitStrip(map, trims, tm, backA, backB, oa, ob, verge);
                u += len;
                Cs4Stats.bulbCurbM += len;
            }
            cs2Granite = wasGranite; walkPaverNow = wasPaver; cs4CornerNoPan = false;
            Cs4Stats.bulbCurbs++;
        }
    }
}
