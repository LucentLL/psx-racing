using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// c7roads round 5: PER-KIND TESTS of the junction outline solver (JunctionOutline, a pure function) on synthetic arms,
    /// seconds to run (Test Runner, EditMode, or -executeMethod ...RunAll). The checks do not trust the solver: a simple
    /// outline, no kink by rule 4 anywhere but at the end rows' corners, every 0.25 m sample covered EXACTLY ONCE by the
    /// fill (0 in a hole, 2 a layer) where the outline says road and never where it does not, the travelled way of every arm
    /// inside the junction all road; the fill's own tests with holes; the height function's tests.
    /// </summary>
    public class JunctionOutlineTests
    {
        const float Lane = 3.66f, Sh = 0.3f;
        static float Half(int lanes) => lanes * Lane * 0.5f + Sh;
        static Vector2 Dir(float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
        /// <summary>Every result the tests made, by name (for a dump).</summary>
        public static readonly List<KeyValuePair<string, JunctionOutline.Result>> Seen = new List<KeyValuePair<string, JunctionOutline.Result>>();

        /// <summary>The checks, returning a reason or null.</summary>
        public static string Check(JunctionOutline.Result r, float sample = 0.25f)
        {
            if (r.refused != null) return "refused: " + r.refused;
            var o = r.outline;
            if (o.Count < 3) return "no outline";
            // a simple outline
            for (int i = 0; i < o.Count; i++)
                for (int j = i + 2; j < o.Count; j++)
                {
                    if (i == 0 && j == o.Count - 1) continue;
                    if (SegX(o[i], o[(i + 1) % o.Count], o[j], o[(j + 1) % o.Count])) return $"outline crosses itself at {o[i]} / {o[j]}";
                }
            // rule 4 everywhere but at the end rows' corners (where the ribbon carries on)
            var rowPts = new List<Vector2>();
            if (r.rowR != null) for (int i = 0; i < r.rowR.Length; i++) { rowPts.Add(r.rowR[i]); rowPts.Add(r.rowL[i]); }
            for (int i = 0; i < o.Count; i++)
            {
                var b = o[i];
                bool atRow = false; foreach (var cc in rowPts) if ((cc - b).sqrMagnitude < 1e-4f) { atRow = true; break; }
                if (atRow) continue;
                if (JunctionOutline.Kink(o[(i - 1 + o.Count) % o.Count], b, o[(i + 1) % o.Count]))
                    return r.outlineSrc.Count == o.Count
                        ? $"kink at {b}: [{JunctionOutline.Piece(r.outlineSrc, (i - 1 + o.Count) % o.Count)}] meets [{JunctionOutline.Piece(r.outlineSrc, i)}] at {Vector2.Angle(b - o[(i - 1 + o.Count) % o.Count], o[(i + 1) % o.Count] - b):0.0} deg"
                        : $"kink at {b}";
            }
            string cov = Coverage(r, sample);
            if (cov != null) return cov;
            // the travelled way inside the junction is road
            for (int i = 0; i < r.arms.Count; i++)
            {
                var a = r.arms[i];
                int kc = Mathf.FloorToInt(Mathf.Min(r.sR[i], r.sL[i]) / JunctionOutline.Step);
                for (int k = 2; k < kc; k++)
                    for (float f = 0.1f; f <= 0.9f; f += 0.2f)
                    {
                        var p = Vector2.Lerp(a.R[k], a.L[k], f);
                        bool island = false; foreach (var h in r.holes) if (JunctionOutline.InPoly(h, p)) island = true;
                        if (island) continue;   // the caller's island stands there
                        if (BeyondFarKerb(r, p)) continue;   // a wide arm's ribbon poking past the far kerb of the road it meets
                        // a lane opening over a taper (a bay, a slip): its outer half develops along the taper
                        var lc = r.corners[i]; var rc = r.corners[(i - 1 + r.arms.Count) % r.arms.Count];
                        if (lc.shape == "through-taper" && f > 0.5f && k * JunctionOutline.Step < lc.sA) continue;
                        if (rc.shape == "through-taper" && f < 0.5f && k * JunctionOutline.Step < rc.sB) continue;
                        if (!JunctionOutline.Covers(r, p)) return $"arm {a.id}: its lane at {p} is not road";
                    }
            }
            return null;
        }

        /// <summary>Is p on the land side of a straight THROUGH kerb (the far kerb of the road an arm meets)? A side road
        /// wider than the road it meets has its ribbon, drawn to the node, past that kerb: not travelled way.</summary>
        static bool BeyondFarKerb(JunctionOutline.Result r, Vector2 p)
        {
            int n = r.arms.Count;
            for (int i = 0; i < n; i++)
            {
                var c = r.corners[i]; if (c.shape != "through") continue;
                var A = r.arms[i]; var B = r.arms[(i + 1) % n];
                // the outline runs in along A.L then out along B.R: land on its right
                var d = (B.R[Mathf.Min(4, B.R.Count - 1)] - A.L[Mathf.Min(4, A.L.Count - 1)]).normalized;
                var q = p - c.tA;
                if (d.x * q.y - d.y * q.x < -1e-3f && Mathf.Abs(Vector2.Dot(q, d)) < 40f) return true;
            }
            return false;
        }

        /// <summary>Covered exactly once: a jittered grid over the outline's box; the fill against the outline and holes.</summary>
        public static string Coverage(JunctionOutline.Result r, float sample)
        {
            var o = r.outline;
            var min = o[0]; var max = o[0];
            foreach (var p in o) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            var rnd = new System.Random(7);
            int bad = 0, n2 = 0; string first = null;
            for (float x = min.x; x <= max.x; x += sample)
                for (float y = min.y; y <= max.y; y += sample)
                {
                    var p = new Vector2(x + (float)rnd.NextDouble() * 0.01f + 0.003f, y + (float)rnd.NextDouble() * 0.01f + 0.004f);
                    bool road = JunctionOutline.Covers(r, p);
                    int n = JunctionOutline.CoverCount(r, p);
                    if (n == 0 && road && JunctionOutline.OnFillEdge(r, p)) continue;   // on a shared edge (zero measure): not a gap
                    if (n > 1) n2++;
                    if ((road && n != 1) || (!road && n != 0)) { bad++; first ??= $"{p}: outline says {(road ? "road" : "not road")}, the fill covers it {n} times"; }
                }
            return bad > 0 ? $"coverage: {bad} samples wrong ({n2} covered twice or more), first {first}" : null;
        }

        static bool SegX(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            var r = b - a; var s = d - c;
            float den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) < 1e-9f) return false;
            var q = c - a;
            float t = (q.x * s.y - q.y * s.x) / den, u = (q.x * r.y - q.y * r.x) / den;
            return t > 1e-4f && t < 1f - 1e-4f && u > 1e-4f && u < 1f - 1e-4f;
        }

        static string Shapes(JunctionOutline.Result r) => string.Join(", ", r.corners.ConvertAll(c => c.shape + (c.r > 0 ? $" r{c.r:0.0}" : "")));
        static void Note(JunctionOutline.Result r, string kind, string why)
        {
            Seen.Add(new KeyValuePair<string, JunctionOutline.Result>(kind, r));
            Debug.Log($"[JunctionOutlineTests] {kind}: {(why ?? "PASS")}; corners {Shapes(r)}; outline {r.outline.Count} vertices, fill {r.tris.Count / 3} triangles " +
                      $"({r.steiner} interior points, {r.flips} flips, {r.slivers} slivers under {JunctionOutline.SliverDeg} deg), solve {r.solveMs:0.00} ms, fill {r.fillMs:0.00} ms");
        }
        static void Assert0(JunctionOutline.Result r, string kind)
        {
            string why = Check(r);
            Note(r, kind, why);
            Assert.IsNull(why, kind + ": " + why);
        }

        // ---------------- the kinds ----------------
        [Test] public void TCorner()
        {
            var arms = new List<JunctionOutline.Arm> {
                JunctionOutline.StraightArm(0, Vector2.zero, Dir(0), Half(2), Half(2), name: "Main"),
                JunctionOutline.StraightArm(1, Vector2.zero, Dir(180), Half(2), Half(2), name: "Main"),
                JunctionOutline.StraightArm(2, Vector2.zero, Dir(90), Half(1), Half(1), radius: 9f, name: "Side") };
            Assert0(JunctionOutline.Solve(arms), "T corner");
        }
        [Test] public void Crossing()
        {
            var arms = new List<JunctionOutline.Arm>();
            for (int i = 0; i < 4; i++) arms.Add(JunctionOutline.StraightArm(i, Vector2.zero, Dir(90 * i), Half(2), Half(2), name: i % 2 == 0 ? "A" : "B"));
            Assert0(JunctionOutline.Solve(arms), "crossing");
        }
        [Test] public void SkewCrossing()
        {
            var arms = new List<JunctionOutline.Arm> {
                JunctionOutline.StraightArm(0, Vector2.zero, Dir(0), Half(2), Half(2), name: "A"), JunctionOutline.StraightArm(1, Vector2.zero, Dir(180), Half(2), Half(2), name: "A"),
                JunctionOutline.StraightArm(2, Vector2.zero, Dir(55), Half(1), Half(1), name: "B"), JunctionOutline.StraightArm(3, Vector2.zero, Dir(235), Half(1), Half(1), name: "B") };
            Assert0(JunctionOutline.Solve(arms), "skew crossing");
        }
        [Test] public void CurvedArmT()
        {
            var arms = new List<JunctionOutline.Arm> {
                JunctionOutline.CurvedArm(0, Vector2.zero, Dir(0), 150f, Half(2), Half(2), name: "Main"),
                JunctionOutline.CurvedArm(1, Vector2.zero, Dir(180), -150f, Half(2), Half(2), name: "Main"),
                JunctionOutline.CurvedArm(2, Vector2.zero, Dir(90), 60f, Half(1), Half(1), radius: 9f, name: "Side") };
            Assert0(JunctionOutline.Solve(arms), "T with curved arms");
        }
        [Test] public void BayAtHost()
        {
            // a one-way host going on; the bay leaves on its left at 10 degrees, its right border starting on the host's left border
            var host0 = JunctionOutline.StraightArm(0, Vector2.zero, Dir(0), Half(2), Half(2), oneway: true, name: "Host");
            var host1 = JunctionOutline.StraightArm(1, Vector2.zero, Dir(180), Half(2), Half(2), oneway: true, name: "Host");
            var bay = JunctionOutline.StraightArm(2, new Vector2(0f, Half(2) + Half(1) - 0.2f), Dir(10), Half(1), Half(1), oneway: true, name: "");
            var r = JunctionOutline.Solve(new List<JunctionOutline.Arm> { host0, host1, bay });
            Assert0(r, "bay at its host");
            NoseRowsOnOneLine(r, "bay at its host");
        }
        static JunctionOutline.Result SlipCase()
        {
            var a0 = JunctionOutline.StraightArm(0, Vector2.zero, Dir(0), Half(2), Half(2), name: "Main");
            var a1 = JunctionOutline.StraightArm(1, Vector2.zero, Dir(180), Half(2), Half(2), name: "Main");
            var slip = JunctionOutline.CurvedArm(2, new Vector2(0f, -(Half(2) + Half(1) - 0.3f)), Dir(-20), -40f, Half(1), Half(1), oneway: true);
            return JunctionOutline.Solve(new List<JunctionOutline.Arm> { a0, a1, slip });
        }
        [Test] public void SlipLaneEnd()
        {
            var r = SlipCase();
            Assert0(r, "slip lane end");
            NoseRowsOnOneLine(r, "slip lane end");
        }
        /// <summary>REGRESSION (round 5 item 2): the slip end as it passed at 5ad47f15, before the sort of side-by-side arms
        /// changed (d18d0dcf). Pinned: the arm ORDER the sort gave then (slip, Main east, Main west, cyclic), the outline passing
        /// every check, and every lane point inside the junction (up to each arm's new row) the old outline had as road still road (the share of all its road within 12 m
        /// kept is reported: the old r9 fillet paved the gore to 27 m out, the nose ends it at 2.4 m). The old corner kinds (arc r9 /
        /// through / connector) are recorded, not pinned: item 2 makes the slip's gore a nose and its outer edge a taper,
        /// because the old connector ran 1 m back behind the node (x = -1.00) to reach the slip.</summary>
        [Test] public void SlipLaneEndRegression()
        {
            var r = SlipCase();
            var order = string.Join(",", r.arms.ConvertAll(a => a.id));
            string why = null;
            if (order != "2,0,1" && order != "0,1,2" && order != "1,2,0") why = $"arm order {order}, was 2,0,1 (cyclic)";
            why ??= Check(r);
            if (why == null)
            {
                // the old outline's road within 12 m of the node: at least 95 % of it still road (sampled every 0.25 m)
                int inOld = 0, still = 0;
                for (float x = -12f; x <= 12f; x += 0.25f)
                    for (float y = -12f; y <= 12f; y += 0.25f)
                    {
                        var q = new Vector2(x + 0.013f, y + 0.007f);
                        if (q.magnitude > 12f || !JunctionOutline.InPoly(OldSlipOutline, q)) continue;
                        inOld++; if (JunctionOutline.Covers(r, q)) still++;
                    }
                regressionKept = still / (float)Mathf.Max(1, inOld);
                // pinned: every lane point the old outline had as road (each arm, 0.5 m to 12 m out) is road now
                for (int ai = 0; ai < r.arms.Count; ai++)
                {
                    var a = r.arms[ai];   // up to its new row: past it, the arm's own ribbon is road
                    int kmax = Mathf.Min(24, Mathf.FloorToInt(Mathf.Min(r.sR[ai], r.sL[ai]) / JunctionOutline.Step));
                    for (int k = 1; k <= kmax && k < a.L.Count && why == null; k++)
                        for (float f = 0.1f; f <= 0.9f; f += 0.2f)
                        {
                            var q = Vector2.Lerp(a.R[k], a.L[k], f);
                            if (JunctionOutline.InPoly(OldSlipOutline, q) && !JunctionOutline.Covers(r, q)) { why = $"arm {a.id}: its lane at {q} was road at 5ad47f15 and is not now"; break; }
                        }
                }
            }
            Note(r, $"slip lane end (regression vs 5ad47f15: order {order}, {regressionKept * 100f:0.0} % of the old road kept)", why);
            Assert.IsNull(why, "slip lane end regression: " + why);
        }
        static float regressionKept;
        static readonly List<Vector2> OldSlipOutline = Parse(
            "16.457,-20.672 19.857,-18.105 19.411,-17.391 19.031,-16.639 18.724,-15.856 18.49,-15.047 18.334,-14.22 18.255,-13.382 18.255,-12.54 18.333,-11.702 18.49,-10.875 18.723,-10.067 19.03,-9.283 19.41,-8.532 19.858,-7.819 20.37,-7.151 20.943,-6.535 21.571,-5.974 22.248,-5.474 22.97,-5.04 23.728,-4.676 24.518,-4.383 25.331,-4.166 26.161,-4.026 27,-3.963 27,3.96 0,3.96 0,-3.96 " +
            "0.109,-3.97 0.193,-3.998 0.253,-4.043 0.292,-4.105 0.311,-4.182 0.312,-4.272 0.296,-4.376 0.265,-4.491 0.22,-4.616 0.164,-4.751 0.097,-4.895 0.023,-5.045 -0.059,-5.201 -0.146,-5.363 -0.237,-5.527 -0.329,-5.695 -0.422,-5.864 -0.514,-6.033 -0.602,-6.201 -0.686,-6.367 -0.763,-6.53 -0.833,-6.689 -0.892,-6.842 -0.941,-6.989 -0.977,-7.128 -0.998,-7.259 -1.003,-7.379 -0.99,-7.488 -0.958,-7.585 -0.905,-7.669 -0.829,-7.738 -0.729,-7.792 " +
            "-0.286,-7.959 2.323,-9.08 4.841,-10.393 7.254,-11.891 9.547,-13.565 11.708,-15.407 13.726,-17.405 15.588,-19.549 16.172,-20.294");
        static List<Vector2> Parse(string s)
        {
            var l = new List<Vector2>();
            foreach (var t in s.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries)) { var xy = t.Split(','); l.Add(new Vector2(float.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture))); }
            return l;
        }
        [Test] public void Fork()
        {
            var up = JunctionOutline.StraightArm(0, Vector2.zero, Dir(180), Half(3), Half(3), oneway: true, name: "Link");
            var l = JunctionOutline.StraightArm(1, new Vector2(0f, Half(2) - 0.4f), Dir(12), Half(2), Half(2), oneway: true, name: "Link");
            var r = JunctionOutline.StraightArm(2, new Vector2(0f, -(Half(1) + 0.4f)), Dir(-14), Half(1), Half(1), oneway: true, name: "Slip");
            var res = JunctionOutline.Solve(new List<JunctionOutline.Arm> { up, l, r });
            Assert0(res, "fork");
            NoseRowsOnOneLine(res, "fork");
        }
        [Test] public void SplitOfOneRoad()
        {
            var u = JunctionOutline.StraightArm(0, Vector2.zero, Dir(180), Half(4), Half(4), name: "Road");
            var a = JunctionOutline.StraightArm(1, new Vector2(0f, Half(2) - 0.3f), Dir(12), Half(2), Half(2), oneway: true, name: "Road");
            var b = JunctionOutline.StraightArm(2, new Vector2(0f, -(Half(2) - 0.3f)), Dir(-12), Half(2), Half(2), oneway: true, name: "Road");
            var r = JunctionOutline.Solve(new List<JunctionOutline.Arm> { u, a, b });
            Assert0(r, "split of one road");
            NoseRowsOnOneLine(r, "split of one road");
        }
        /// <summary>A nose's two arms: both end rows on the one cut line, the tangent points shared exactly.</summary>
        static void NoseRowsOnOneLine(JunctionOutline.Result r, string kind)
        {
            int n = r.arms.Count;
            for (int i = 0; i < n; i++)
            {
                var c = r.corners[i]; if (c.shape != "nose") continue;
                int j = (i + 1) % n;
                Assert.IsTrue(r.rowL[i].Equals(c.tA) && r.rowR[j].Equals(c.tB), $"{kind}: the nose's tangent points are not the rows' points");
                var nrm = new Vector2(-c.lineD.y, c.lineD.x);
                foreach (var p in new[] { r.rowR[i], r.rowL[i], r.rowR[j], r.rowL[j] })
                    Assert.IsTrue(Mathf.Abs(Vector2.Dot(p - c.lineP, nrm)) < 1e-3f, $"{kind}: row point {p} is {Vector2.Dot(p - c.lineP, nrm):0.000} m off the cut line");
                Assert.IsTrue(c.pts[0].Equals(c.tA) && c.pts[c.pts.Count - 1].Equals(c.tB), $"{kind}: the nose arc does not start and end on its tangent points");
            }
        }
        static List<JunctionOutline.Arm> Boulevard(float m, out JunctionOutline.Link link, float crossHalf)
        {
            var arms = new List<JunctionOutline.Arm> {
                JunctionOutline.StraightArm(0, new Vector2(0f, m + Half(2)), Dir(0), Half(2), Half(2), oneway: true, name: "Blvd"),
                JunctionOutline.StraightArm(1, new Vector2(0f, m + Half(2)), Dir(180), Half(2), Half(2), oneway: true, name: "Blvd"),
                JunctionOutline.StraightArm(2, new Vector2(0f, -(m + Half(2))), Dir(0), Half(2), Half(2), oneway: true, name: "Blvd"),
                JunctionOutline.StraightArm(3, new Vector2(0f, -(m + Half(2))), Dir(180), Half(2), Half(2), oneway: true, name: "Blvd") };
            link = JunctionOutline.StraightLink(9, new Vector2(0f, -(m + Half(2))), new Vector2(0f, m + Half(2)), crossHalf);
            return arms;
        }
        [Test] public void MedianOpening()
        {
            // two one-way carriageways with a 12 m median and the crossover between them (a link inside the cluster)
            var arms = Boulevard(6f, out var link, Half(2));
            var r = JunctionOutline.Solve(arms, null, new List<JunctionOutline.Link> { link });
            Assert0(r, "median opening");
            Assert.IsTrue(r.corners.FindAll(c => c.shape == "connector" && c.note.StartsWith("via link")).Count == 2, "median opening: both noses via the crossover");
        }
        [Test] public void MedianOpeningWide()
        {
            // a 30 m median: each nose is two kerb returns joined by the crossover's straight border
            var arms = Boulevard(15f, out var link, Half(1));
            var r = JunctionOutline.Solve(arms, null, new List<JunctionOutline.Link> { link });
            Assert0(r, "median opening, 30 m median");
        }

        [Test] public void MedianOpeningUTurnBays()
        {
            // spot 80's shape: a 24 m boulevard (carriageways' centres), a U-turn lane from each carriageway crossing the median
            // into the other and running on beside it as its inner lane: the U-turn lanes merge into their hosts (bays are lanes
            // of their host), their crossings become the cluster's links, the noses round off against the crossings
            float m = 12f, hA = Half(3), hU = Half(1) - Sh;
            var arms = new List<JunctionOutline.Arm> {
                JunctionOutline.StraightArm(0, new Vector2(0f, m), Dir(0), hA, hA, oneway: true, name: "Blvd"),
                JunctionOutline.StraightArm(1, new Vector2(0f, m), Dir(180), hA, hA, oneway: true, name: "Blvd"),
                JunctionOutline.StraightArm(2, new Vector2(0f, -m), Dir(0), hA, hA, oneway: true, name: "Blvd"),
                JunctionOutline.StraightArm(3, new Vector2(0f, -m), Dir(180), hA, hA, oneway: true, name: "Blvd"),
                JunctionOutline.ArmAlong(4, JunctionOutline.CurveThenStraight(new Vector2(0f, -m), new Vector2(1f, 1.2f), new Vector2(22f, m - hA - hU), Dir(0), 70f), hU, hU, oneway: true),
                JunctionOutline.ArmAlong(5, JunctionOutline.CurveThenStraight(new Vector2(0f, m), new Vector2(-1f, -1.2f), new Vector2(-22f, -m + hA + hU), Dir(180), 70f), hU, hU, oneway: true) };
            var links = new List<JunctionOutline.Link>();
            var merges = JunctionOutline.MergeBays(arms, links);
            Assert.IsTrue(merges.Count == 2 && links.Count == 2, "U-turn bays: " + string.Join("; ", merges));
            var r = JunctionOutline.Solve(arms, null, links);
            Assert0(r, "median opening with U-turn bays (spot 80's shape)");
            Assert.IsTrue(r.corners.FindAll(c => c.shape == "connector" && c.note.StartsWith("via link")).Count == 2, "U-turn bays: both noses via the crossings: " + Shapes(r));
        }

        // ---------------- REAL junctions (fixtures written by CityOutlineProto.RunCity) ----------------
        /// <summary>A fixture: the plain solver inputs of one real cluster (arms, links), about its first node.</summary>
        public static bool LoadFixture(string path, out string kind, out List<JunctionOutline.Arm> arms, out List<JunctionOutline.Link> links) => LoadFixture(path, out kind, out arms, out links, out _);
        public static bool LoadFixture(string path, out string kind, out List<JunctionOutline.Arm> arms, out List<JunctionOutline.Link> links, out List<List<Vector2>> holes)
        {
            kind = ""; arms = new List<JunctionOutline.Arm>(); links = new List<JunctionOutline.Link>(); holes = new List<List<Vector2>>();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            JunctionOutline.Arm arm = null; JunctionOutline.Link link = null;
            List<Vector2> Pts(string l)
            {
                var o = new List<Vector2>();
                foreach (var t in l.Substring(2).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries)) { var xy = t.Split(','); o.Add(new Vector2(float.Parse(xy[0], inv), float.Parse(xy[1], inv))); }
                return o;
            }
            foreach (var line in System.IO.File.ReadAllLines(path))
            {
                if (line.StartsWith("kind ")) kind = line.Substring(5);
                else if (line.StartsWith("arm "))
                {
                    var t = line.Split(new[] { ' ' }, 5);
                    arm = new JunctionOutline.Arm { id = int.Parse(t[1]), oneway = t[2] == "1", radius = float.Parse(t[3], inv), name = t.Length > 4 ? t[4] : "", Y = new List<float>() };
                    arms.Add(arm); link = null;
                }
                else if (line.StartsWith("hole ")) holes.Add(Pts("x " + line.Substring(5)));
                else if (line.StartsWith("link ")) { link = new JunctionOutline.Link { id = int.Parse(line.Substring(5)) }; links.Add(link); arm = null; }
                else if (line.StartsWith("L ")) { if (arm != null) arm.L = Pts(line); else if (link != null) link.L = Pts(line); }
                else if (line.StartsWith("R ")) { if (arm != null) arm.R = Pts(line); else if (link != null) link.R = Pts(line); }
                else if (line.StartsWith("C ") && arm != null) arm.C = Pts(line);
                else if (line.StartsWith("Y ") && arm != null) foreach (var t in line.Substring(2).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries)) arm.Y.Add(float.Parse(t, inv));
            }
            return arms.Count > 0;
        }
        public static string FixtureDir => System.IO.Path.Combine(Application.dataPath, "PSXRacing", "Editor", "JunctionFixtures");
        /// <summary>Every real fixture through MergeBays and the solver, each checked like the synthetic kinds. These are the
        /// tests that matter now: the real junctions at his spots and one of each other kind from the gallery.</summary>
        [Test] public void RealJunctions()
        {
            if (!System.IO.Directory.Exists(FixtureDir)) Assert.Fail("no fixtures at " + FixtureDir);
            int fail = 0, n = 0; var first = "";
            foreach (var f in System.IO.Directory.GetFiles(FixtureDir, "*.txt"))
            {
                n++;
                if (!LoadFixture(f, out string kind, out var arms, out var links, out var holes)) { fail++; continue; }
                var merges = JunctionOutline.MergeBays(arms, links);
                var r = JunctionOutline.Solve(arms, holes, links);
                string why = Check(r, 0.5f);
                string name = System.IO.Path.GetFileNameWithoutExtension(f);
                Note(r, $"real {name} ({kind.Split('|')[0].Trim()}; {arms.Count} arms, {links.Count} links{(holes.Count > 0 ? ", " + holes.Count + " islands" : "")}{(merges.Count > 0 ? ", " + merges.Count + " bays merged" : "")})", why);
                if (why != null) { fail++; if (first.Length == 0) first = name + ": " + why; }
            }
            Assert.AreEqual(0, fail, $"{fail} of {n} real junctions fail; first {first}");
        }

        [Test] public void PaintedGoreToTheStopLine()
        {
            // spot 81's shape (g05): a 3-lane link comes south to a T with a boulevard 35 m on; its right lane leaves as a slip
            // on a large curve, separated by PAINT (the two branches drawn overlapping), the kerbed island only where the
            // slip meets the boulevard (it merges 15 degrees into the boulevard's westbound side). One junction: the split node and the T, the straight branch a link between them.
            // Expect: no nose at the split (painted), the island is the kerb return between the slip and the boulevard.
            float hApp = Half(3), hB = Half(2), hS = Half(1) - Sh;
            var approach = JunctionOutline.StraightArm(0, Vector2.zero, Dir(90), hApp, hApp, oneway: true, name: "Link");
            var slip = JunctionOutline.ArmAlong(1, JunctionOutline.CurveThenStraight(new Vector2(-2.2f, 0f), Dir(-95), new Vector2(-24f, -22f), Dir(-165), 50f), hS, hS, radius: 15f, oneway: true, name: "Slip");
            var east = JunctionOutline.StraightArm(2, new Vector2(0f, -35f), Dir(0), Half(4), Half(4), name: "Blvd");
            var west = JunctionOutline.StraightArm(3, new Vector2(0f, -35f), Dir(180), Half(4), Half(4), name: "Blvd");
            var link = JunctionOutline.StraightLink(9, new Vector2(1.8f, 0f), new Vector2(1.8f, -35f), hB);
            var r = JunctionOutline.Solve(new List<JunctionOutline.Arm> { approach, slip, east, west }, null, new List<JunctionOutline.Link> { link });
            Assert0(r, "painted gore to the stop line (spot 81's shape)");
            // no raised nose at the split (within 20 m of it); the slip's merge into the boulevard may have its island
            Assert.IsTrue(!r.corners.Exists(c => c.shape == "nose" && c.tA.magnitude < 20f), "81: a nose at the split where the photo has paint: " + Shapes(r));
        }

        // ---------------- the fill with holes ----------------
        static List<Vector2> Circle(Vector2 c, float r, int n = 24) { var l = new List<Vector2>(); for (int k = 0; k < n; k++) { float a = k * Mathf.PI * 2f / n; l.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r); } return l; }
        static List<Vector2> Rect(float x0, float y0, float x1, float y1) => new List<Vector2> { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };
        static void AssertFill(JunctionOutline.Result r, string kind)
        {
            string why = r.refused != null ? "refused: " + r.refused : Coverage(r, 0.25f);
            Note(r, kind, why);
            Assert.IsNull(why, kind + ": " + why);
        }
        [Test] public void FillOneHole() => AssertFill(JunctionOutline.FillOnly(Rect(0, 0, 20, 20), new List<List<Vector2>> { Circle(new Vector2(9, 10), 2.5f) }), "fill: one hole");
        [Test] public void FillTwoHolesSideBySide() => AssertFill(JunctionOutline.FillOnly(Rect(0, 0, 30, 16), new List<List<Vector2>> { Circle(new Vector2(9, 8), 2.5f), Circle(new Vector2(17, 8), 2.5f) }), "fill: two holes side by side");
        [Test] public void FillHoleFacingReflex()
        {
            // the ray from the hole's rightmost vertex passes just over a spike of the ring; the edge it hits ends low, behind
            // the spike: the spike's reflex tip, inside M-I-P, is the vertex bridged to
            var ring = new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0), new Vector2(16, 9.95f), new Vector2(18, 0), new Vector2(40, 0), new Vector2(30, 30), new Vector2(0, 30) };
            var r = JunctionOutline.FillOnly(ring, new List<List<Vector2>> { Circle(new Vector2(8, 10), 1f) });
            AssertFill(r, "fill: a hole facing a reflex vertex");
            // the spike's tip stands 5 cm under the ray, so the endpoint of the edge the ray hits (the ring is cut every
            // 1.25 m: just below the ray) is hidden behind it: the bridge goes to the tip, which is in the ring twice
            int tip = 0;
            for (int i = 0; i < r.ringCount; i++) if ((r.fillVerts[i] - new Vector2(16, 9.95f)).sqrMagnitude < 1e-8f) tip++;
            Assert.IsTrue(tip == 2, $"fill: the bridge did not go to the reflex tip (tip x{tip})");
        }
        [Test] public void FillHoleTouchingNothing()
        {
            // a crossing with an island in the middle of the junction, clear of every border
            var arms = new List<JunctionOutline.Arm>();
            for (int i = 0; i < 4; i++) arms.Add(JunctionOutline.StraightArm(i, Vector2.zero, Dir(90 * i), Half(2), Half(2), radius: 15f, name: i % 2 == 0 ? "A" : "B"));
            Assert0(JunctionOutline.Solve(arms, new List<List<Vector2>> { Circle(Vector2.zero, 2f) }), "fill: a crossing with a hole touching nothing");
        }
        [Test] public void IslandHoleFill()
        {
            // a crossing with islands in two corners' road (inside the kerb returns) and one in the middle
            var arms = new List<JunctionOutline.Arm>();
            for (int i = 0; i < 4; i++) arms.Add(JunctionOutline.StraightArm(i, Vector2.zero, Dir(90 * i), Half(2), Half(2), radius: 15f, name: i % 2 == 0 ? "A" : "B"));
            var holes = new List<List<Vector2>> { Circle(new Vector2(6.5f, 6.5f), 1.2f), Circle(new Vector2(-6.5f, -6.5f), 1.2f), Circle(Vector2.zero, 1.5f) };
            Assert0(JunctionOutline.Solve(arms, holes), "crossing with three island holes");
        }

        // ---------------- height ----------------
        /// <summary>The height checks: each row takes its arm's profile (1 mm); each interior point within 1 cm of the plane
        /// of its neighbours; along every straight line between two rows' middles the mesh within 1 cm of the surface and the
        /// surface's grade changing by under GradeStep per 0.5 m (the mesh's own facet-to-facet change is reported).</summary>
        public const float GradeStep = 0.01f;   // a ceiling: 1 % in 0.5 m (the smoothness test is the halving)
        static string HeightCheck(JunctionOutline.Result r, out string stats)
        {
            stats = "";
            if (r.refused != null) return "refused: " + r.refused;
            for (int i = 0; i < r.arms.Count; i++)
            {
                var a = r.arms[i];
                float hr = r.HeightAt(r.rowR[i]), hl = r.HeightAt(r.rowL[i]);
                if (Mathf.Abs(hr - a.YAt(r.sR[i])) > 1e-3f || Mathf.Abs(hl - a.YAt(r.sL[i])) > 1e-3f) return $"arm {a.id}: its row stands {hr - a.YAt(r.sR[i]):0.000} / {hl - a.YAt(r.sL[i]):0.000} m off its profile";
            }
            // one-ring planes of the interior points
            var nb = new Dictionary<int, HashSet<int>>();
            for (int t = 0; t + 2 < r.tris.Count; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int u = r.tris[t + e], w = r.tris[t + (e + 1) % 3];
                    if (!nb.TryGetValue(u, out var su)) nb[u] = su = new HashSet<int>(); su.Add(w);
                    if (!nb.TryGetValue(w, out var sw)) nb[w] = sw = new HashSet<int>(); sw.Add(u);
                }
            float worst = 0f; int worstV = -1;
            for (int v = r.ringCount; v < r.fillVerts.Count; v++)
            {
                if (!nb.TryGetValue(v, out var s) || s.Count < 3) continue;
                double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, sn = 0, sxh = 0, syh = 0, sh = 0;
                var c = r.fillVerts[v];
                foreach (int w in s) { var p = r.fillVerts[w] - c; float h = r.fillY[w]; sxx += p.x * p.x; sxy += p.x * p.y; syy += p.y * p.y; sx += p.x; sy += p.y; sn += 1; sxh += p.x * h; syh += p.y * h; sh += h; }
                // least squares h = a x + b y + c0 (relative to the vertex): c0 by Cramer's rule
                double[,] m = { { sxx, sxy, sx }, { sxy, syy, sy }, { sx, sy, sn } };
                double det = Det3(m); if (System.Math.Abs(det) < 1e-9) continue;
                var m3 = (double[,])m.Clone(); m3[0, 2] = sxh; m3[1, 2] = syh; m3[2, 2] = sh;
                double c0 = Det3(m3) / det;
                float dev = Mathf.Abs(r.fillY[v] - (float)c0);
                if (dev > worst) { worst = dev; worstV = v; }
            }
            float worstMesh = 0f, worstMeshStep = 0f;
            float step50 = GradeSteps(r, 0.5f, ref worstMesh, ref worstMeshStep), mDummy = 0f, msDummy = 0f, step25 = GradeSteps(r, 0.25f, ref mDummy, ref msDummy);
            stats = $"plane {worst * 1000f:0.0} mm, mesh vs surface {worstMesh * 1000f:0.0} mm, surface grade change {step50 * 100f:0.000} % per 0.5 m / {step25 * 100f:0.000} % per 0.25 m " +
                    $"(mesh facets {worstMeshStep * 100f:0.000} % per 0.5 m), interior spacing {r.steinerM:0.00} m";
            if (worstMesh > 0.01f) return $"the mesh stands {worstMesh * 100f:0.00} cm off the surface";
            if (worst > 0.01f) return $"interior point {r.fillVerts[worstV]} stands {worst * 100f:0.00} cm off its neighbours' plane";
            // SMOOTH = no jump in grade: halving the step must cut the change to 3/4 or less (a smooth surface halves it,
            // 0.50 to 0.70 measured; a kink keeps its size, 1.0)
            if (step25 > 0.75f * step50 + 2e-4f) return $"the grade jumps: {step50 * 100f:0.000} % per 0.5 m but {step25 * 100f:0.000} % per 0.25 m";
            if (step50 > GradeStep) return $"the grade changes {step50 * 100f:0.000} % in 0.5 m";
            return null;
        }
        /// <summary>Along every straight line between two rows' middles, sampled every h: the largest change of the surface's
        /// grade from one step to the next; the mesh's distance from the surface and its facet-to-facet change.</summary>
        static float GradeSteps(JunctionOutline.Result r, float hstep, ref float worstMesh, ref float worstMeshStep)
        {
            float worstStep = 0f;
            int na = r.arms.Count;
            for (int i = 0; i < na; i++)
                for (int j = i + 1; j < na; j++)
                {
                    var p0 = 0.5f * (r.rowR[i] + r.rowL[i]); var p1 = 0.5f * (r.rowR[j] + r.rowL[j]);
                    float len = (p1 - p0).magnitude; int steps = Mathf.Max(2, Mathf.FloorToInt(len / hstep));
                    float prevG = float.NaN, prevH = float.NaN, prevGM = float.NaN, prevM = float.NaN;
                    for (int k = 1; k < steps; k++)
                    {
                        var p = Vector2.Lerp(p0, p1, k / (float)steps);
                        if (!JunctionOutline.Covers(r, p)) { prevG = prevH = prevGM = prevM = float.NaN; continue; }
                        float h = r.HeightAt(p), hm = JunctionOutline.MeshHeight(r, p);
                        if (float.IsNaN(hm)) { prevG = prevH = prevGM = prevM = float.NaN; continue; }
                        worstMesh = Mathf.Max(worstMesh, Mathf.Abs(hm - h));
                        if (!float.IsNaN(prevH)) { float g = (h - prevH) / (len / steps); if (!float.IsNaN(prevG)) worstStep = Mathf.Max(worstStep, Mathf.Abs(g - prevG)); prevG = g; }
                        if (!float.IsNaN(prevM)) { float g = (hm - prevM) / (len / steps); if (!float.IsNaN(prevGM)) worstMeshStep = Mathf.Max(worstMeshStep, Mathf.Abs(g - prevGM)); prevGM = g; }
                        prevH = h; prevM = hm;
                    }
                }
            return worstStep;
        }
        static double Det3(double[,] m) =>
            m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
        static void AssertHeight(JunctionOutline.Result r, string kind)
        {
            string stats = "";
            string why = Check(r) ?? HeightCheck(r, out stats);
            if (stats.Length == 0) HeightCheck(r, out stats);
            Note(r, kind + " [" + stats + "]", why);
            Assert.IsNull(why, kind + ": " + why);
        }
        [Test] public void HeightTOnAGrade()
        {
            // the main road rising 4 % eastward, the side road falling 3 % away
            var arms = new List<JunctionOutline.Arm> {
                JunctionOutline.StraightArm(0, Vector2.zero, Dir(0), Half(2), Half(2), name: "Main", grade: 0.04f),
                JunctionOutline.StraightArm(1, Vector2.zero, Dir(180), Half(2), Half(2), name: "Main", grade: -0.04f),
                JunctionOutline.StraightArm(2, Vector2.zero, Dir(90), Half(1), Half(1), radius: 9f, name: "Side", grade: -0.03f) };
            AssertHeight(JunctionOutline.Solve(arms), "height: T on a 4 % grade");
        }
        [Test] public void HeightCrossingOnACrest()
        {
            // every arm falling 5 % away from the node (a crossing on a crest), one road's arms skewed
            var arms = new List<JunctionOutline.Arm> {
                JunctionOutline.StraightArm(0, Vector2.zero, Dir(0), Half(2), Half(2), name: "A", grade: -0.05f), JunctionOutline.StraightArm(1, Vector2.zero, Dir(180), Half(2), Half(2), name: "A", grade: -0.05f),
                JunctionOutline.StraightArm(2, Vector2.zero, Dir(70), Half(1), Half(1), name: "B", grade: -0.05f), JunctionOutline.StraightArm(3, Vector2.zero, Dir(250), Half(1), Half(1), name: "B", grade: -0.05f) };
            AssertHeight(JunctionOutline.Solve(arms), "height: crossing on a crest");
        }
        [Test] public void HeightSplitOnAGrade()
        {
            var u = JunctionOutline.StraightArm(0, Vector2.zero, Dir(180), Half(4), Half(4), name: "Road", grade: -0.05f);
            var a = JunctionOutline.StraightArm(1, new Vector2(0f, Half(2) - 0.3f), Dir(12), Half(2), Half(2), oneway: true, name: "Road", grade: 0.05f);
            var b = JunctionOutline.StraightArm(2, new Vector2(0f, -(Half(2) - 0.3f)), Dir(-12), Half(2), Half(2), oneway: true, name: "Road", grade: 0.02f);
            AssertHeight(JunctionOutline.Solve(new List<JunctionOutline.Arm> { u, a, b }), "height: split on a 5 % grade, one branch easing to 2 %");
        }
        [Test] public void HeightRiseRefused()
        {
            // the side road's row stands 3 m above the main's: over the 2.5 m limit, refused with its reason
            var arms = new List<JunctionOutline.Arm> {
                JunctionOutline.StraightArm(0, Vector2.zero, Dir(0), Half(2), Half(2), name: "Main"),
                JunctionOutline.StraightArm(1, Vector2.zero, Dir(180), Half(2), Half(2), name: "Main"),
                JunctionOutline.StraightArm(2, Vector2.zero, Dir(90), Half(1), Half(1), name: "Side", y0: 3f) };
            var r = JunctionOutline.Solve(arms);
            bool ok = r.refused != null && (r.refused.Contains("off one plane") || r.refused.Contains("slope"));
            Note(r, "height: a side road standing 3 m proud, refused (" + r.refused + ")", ok ? null : "not refused");
            Assert.IsTrue(ok, "a side road 3 m proud was not refused: " + r.refused);
        }

        [Test] public void HeightSteadySlopeAllowed()
        {
            // a T on a steady 8 % slope rises over 2.5 m across its rows but is one plane: solved (the old limit refused it)
            var arms = new List<JunctionOutline.Arm> {
                JunctionOutline.StraightArm(0, Vector2.zero, Dir(0), Half(2), Half(2), name: "Main", grade: 0.08f),
                JunctionOutline.StraightArm(1, Vector2.zero, Dir(180), Half(2), Half(2), name: "Main", grade: -0.08f),
                JunctionOutline.StraightArm(2, Vector2.zero, Dir(90), Half(1), Half(1), radius: 15f, name: "Side") };
            AssertHeight(JunctionOutline.Solve(arms), "height: T on a steady 8 % slope (rows rise over 2.5 m, no warp)");
        }

        [Test] public void Sweep()
        {
            // a fixed-seed sweep over angle, lanes and radius (T and crossing)
            var rnd = new System.Random(20261126);
            int fails = 0; var first = "";
            double ms = 0;
            for (int n = 0; n < 60; n++)
            {
                int lm = 1 + rnd.Next(4), ls = 1 + rnd.Next(3);
                float side = 40f + (float)rnd.NextDouble() * 100f, rad = 6f + (float)rnd.NextDouble() * 12f;
                var arms = new List<JunctionOutline.Arm> {
                    JunctionOutline.StraightArm(0, Vector2.zero, Dir(0), Half(lm), Half(lm), name: "M"),
                    JunctionOutline.StraightArm(1, Vector2.zero, Dir(180), Half(lm), Half(lm), name: "M"),
                    JunctionOutline.StraightArm(2, Vector2.zero, Dir(side), Half(ls), Half(ls), radius: rad, name: "S") };
                if (rnd.Next(2) == 0) arms.Add(JunctionOutline.StraightArm(3, Vector2.zero, Dir(side + 180f), Half(ls), Half(ls), radius: rad, name: "S"));
                var r = JunctionOutline.Solve(arms);
                ms += r.solveMs + r.fillMs;
                string why = Check(r, 0.5f);
                if (why != null) { fails++; Seen.Add(new KeyValuePair<string, JunctionOutline.Result>($"sweep {n}", r)); if (first.Length == 0) first = $"case {n} (side {side:0} deg, lanes {lm}/{ls}, r {rad:0.0}): {why}"; }
            }
            Debug.Log($"[JunctionOutlineTests] sweep: 60 cases, {fails} fail, {ms:0.0} ms in all; {first}");
            Assert.AreEqual(0, fails, first);
        }

        // ---------------- road-surface stage 1: the measures ----------------
        [Test] public void MeasuredNoses()
        {
            // a 12 m median's opening with both ends MEASURED: the east end a flat front at x = 8 with 2 m shoulders
            // (concrete), the west end at x = -10 with a radius over half the width (a full round, 6 m; grass)
            var arms = Boulevard(6f, out var link, Half(2));
            var meas = new JunctionOutline.Measures { provider = "hand" };
            meas.noses.Add(new JunctionOutline.Nose { a = 2, b = 0, capA = new Vector2(8f, -3f), capB = new Vector2(8f, 3f), r = 2f, kind = 1 });
            meas.noses.Add(new JunctionOutline.Nose { a = 1, b = 3, capA = new Vector2(-10f, 3f), capB = new Vector2(-10f, -3f), r = 20f, kind = 0 });
            var r = JunctionOutline.Solve(arms, null, new List<JunctionOutline.Link> { link }, meas);
            Assert0(r, "measured median ends (a flat front with 2 m shoulders, a full round)");
            var e = r.corners.Find(c => c.a == 2 && c.b == 0); var w = r.corners.Find(c => c.a == 1 && c.b == 3);
            Assert.IsTrue(e != null && e.note == "measured nose" && e.kind == 1 && Mathf.Abs(e.r - 2f) < 0.01f, $"east nose: {e?.shape} {e?.note} r {e?.r}");
            Assert.IsTrue(w != null && w.note == "measured nose" && w.kind == 0 && Mathf.Abs(w.r - 6f) < 0.15f, $"west nose: a full round is half the median (6 m), got r {w?.r}");
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (var p in e.pts) minX = Mathf.Min(minX, p.x);
            foreach (var p in w.pts) maxX = Mathf.Max(maxX, p.x);
            Assert.IsTrue(Mathf.Abs(minX - 8f) < 0.02f, $"east nose: its front stands at x {minX:0.000}, measured 8");
            Assert.IsTrue(Mathf.Abs(maxX + 10f) < 0.05f, $"west nose: its tip stands at x {maxX:0.000}, measured -10");
            Assert.AreEqual("hand", r.provider, "the result names its provider");
        }

        [Test] public void MeasuredIslandAndRows()
        {
            // a concrete island in the opening and a channel strip standing in arm 0's pavement from x = 10 to x = 24:
            // both are holes with their kinds; arm 0's row lies beyond the strip (an island may not cross a row)
            float m = 6f;
            var arms = Boulevard(m, out var link, Half(2));
            var meas = new JunctionOutline.Measures { provider = "hand" };
            var a = new JunctionOutline.Island { kind = 1, kerb = 0.15f };
            a.pts.AddRange(new[] { new Vector2(-2f, -1.5f), new Vector2(2f, -1.5f), new Vector2(2f, 1.5f), new Vector2(-2f, 1.5f) }); a.r.AddRange(new[] { 1.2f, 1.2f, 1.2f, 1.2f });
            float yc = m + Half(2);
            var b = new JunctionOutline.Island { kind = 0, kerb = 0.2f };
            b.pts.AddRange(new[] { new Vector2(10f, yc - 0.6f), new Vector2(24f, yc - 0.6f), new Vector2(24f, yc + 0.6f), new Vector2(10f, yc + 0.6f) }); b.r.AddRange(new[] { 0.55f, 0.55f, 0.55f, 0.55f });
            meas.islands.Add(a); meas.islands.Add(b);
            var r = JunctionOutline.Solve(arms, null, new List<JunctionOutline.Link> { link }, meas);
            Assert0(r, "measured islands (one in the opening, a channel strip along an arm)");
            Assert.AreEqual(2, r.holes.Count, "both measured islands are holes");
            Assert.IsTrue(r.holeKind[0] == 1 && r.holeKind[1] == 0 && Mathf.Abs(r.holeKerb[1] - 0.2f) < 1e-4f, "the holes carry their kinds and kerb heights");
            int i0 = r.arms.FindIndex(x => x.id == 0);
            Assert.IsTrue(r.sL[i0] >= 24f + JunctionOutline.IslandRowM - 0.01f && r.sR[i0] >= 24f + JunctionOutline.IslandRowM - 0.01f, $"arm 0's row stands at {r.sL[i0]:0.0} m: not beyond the strip's end (24 m + {JunctionOutline.IslandRowM})");
            foreach (var h in r.holes) for (int k = 0; k < h.Count; k++) Assert.IsTrue(!JunctionOutline.Kink(h[(k - 1 + h.Count) % h.Count], h[k], h[(k + 1) % h.Count]), "an island's outline has a kink");
            Assert.AreEqual(r.holes.Count, r.ringHoles.Count, "the fill keeps each hole's own boundary vertices");
            // the lane check: arm 0's rule ribbon has the strip standing in it
            JunctionOutline.LaneCheck(r, r.arms[i0].L, r.arms[i0].R, r.sL[i0] - 1f, out float run, out float worst, out _);
            Assert.IsTrue(run >= 13f && worst < 0f, $"the lane check misses a strip in the lanes: {run:0.0} m, worst {worst:0.00}");
        }

        [Test] public void RoundedPolygonKeepsRule4()
        {
            // an L-shaped strip (one reflex vertex, radius 8) with small corner radii: every joint keeps the 1 cm / 6 degree
            // rule, the ring is anticlockwise and stays within 1 cm of its own polygon's hull corners' fillets
            var pts = new List<Vector2> { new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 12), new Vector2(17, 12), new Vector2(17, 3), new Vector2(0, 3) };
            var rr = new List<float> { 0.5f, 0.5f, 1.4f, 1.4f, 8f, 0.5f };
            var ring = JunctionOutline.RoundedPolygon(pts, rr);
            float area = 0f; for (int i = 0; i < ring.Count; i++) { var p = ring[i]; var q = ring[(i + 1) % ring.Count]; area += p.x * q.y - q.x * p.y; }
            Assert.IsTrue(area > 0f, "a rounded polygon is anticlockwise");
            for (int i = 0; i < ring.Count; i++) Assert.IsTrue(!JunctionOutline.Kink(ring[(i - 1 + ring.Count) % ring.Count], ring[i], ring[(i + 1) % ring.Count]), $"kink at {ring[i]}");
            Assert.IsNull(JunctionOutline.SelfCross(ring), "a rounded polygon crosses itself");
            // the reflex vertex (17, 3) gains land: the arc stands outside the L's notch corner; two radii sharing the 3 m edge are scaled to fit
            Assert.IsTrue(JunctionOutline.InPoly(ring, new Vector2(16.8f, 3.2f)), "the reflex vertex's fillet is missing");
            Assert.IsTrue(0.5f * area > 20f * 3f + 3f * 9f - 3f && 0.5f * area < 20f * 3f + 3f * 9f + 16f, $"area {0.5f * area:0.0} m2");
        }

        [Test] public void MeasuredBordersHeldAndBlended()
        {
            // a straight arm 3.66 / 3.66 m wide; its edge measured LEFT 6 m at 5 m and 8 m at 20 m (blend 10 m): the border
            // is held at 6 m from the first station in to the junction, runs through 8 m at 20 m, is the rule's again
            // from 30 m; no joint breaks rule 4; the arm's measEnd is 30 m. The same edge read from its b end swaps sides.
            var em = new JunctionMeasures.EdgeM { edge = 1, blend = 10f };
            em.st.Add(new JunctionMeasures.Station { s = 5f, left = 6f }); em.st.Add(new JunctionMeasures.Station { s = 20f, left = 8f });
            var arm = JunctionOutline.StraightArm(1, Vector2.zero, Dir(0), 3.66f, 3.66f);
            JunctionMeasures.ApplyToArm(arm, true, 80f, em);
            float Off(JunctionOutline.Arm a, bool left, float d) { int k = Mathf.RoundToInt(d / JunctionOutline.Step); return ((left ? a.L[k] : a.R[k]) - a.C[k]).magnitude; }
            Assert.IsTrue(Mathf.Abs(Off(arm, true, 0f) - 6f) < 1e-3f && Mathf.Abs(Off(arm, true, 5f) - 6f) < 1e-3f, $"held to the junction: {Off(arm, true, 0f):0.000}");
            Assert.IsTrue(Mathf.Abs(Off(arm, true, 20f) - 8f) < 1e-3f, $"at the last station: {Off(arm, true, 20f):0.000}");
            Assert.IsTrue(Mathf.Abs(Off(arm, true, 30f) - 3.66f) < 1e-3f && Mathf.Abs(Off(arm, true, 60f) - 3.66f) < 1e-3f, $"the rule's width beyond the blend: {Off(arm, true, 30f):0.000}");
            Assert.IsTrue(Mathf.Abs(Off(arm, false, 10f) - 3.66f) < 1e-3f, "the unmeasured side keeps the rule");
            Assert.IsTrue(arm.measL && !arm.measR && Mathf.Abs(arm.measEnd - 30f) < 1e-3f, $"measEnd {arm.measEnd}");
            for (int k = 1; k + 1 < arm.L.Count; k++) Assert.IsTrue(!JunctionOutline.Kink(arm.L[k - 1], arm.L[k], arm.L[k + 1]), $"a kink in the measured border at {k * JunctionOutline.Step} m");
            var back = JunctionOutline.StraightArm(1, Vector2.zero, Dir(0), 3.66f, 3.66f);
            JunctionMeasures.ApplyToArm(back, false, 80f, em);   // leaves from the edge's b end: station 80 - d, the edge's left is its right
            Assert.IsTrue(Mathf.Abs(Off(back, false, 60f) - 8f) < 1e-3f && Mathf.Abs(Off(back, false, 75f) - 6f) < 1e-3f, $"from the b end: {Off(back, false, 60f):0.000} / {Off(back, false, 75f):0.000}");
            Assert.IsTrue(Mathf.Abs(Off(back, false, 0f) - 8f) < 1e-3f && back.measR && !back.measL, "from the b end: the station nearest the junction (8 m) is held to it, on the arm's right");
        }

        [Test] public void MeasuresParseAndSolver()
        {
            string text = "# hand\nprovider hand\nedge 7 blend 9\ns 4.5 - 3.25\ns 1.5 6 -\njunction j1 nodes 3 4\nnose 5 6 1 2 3 4 r 2.5 kind concrete\nreturn 8 9 r 18\nisland kind grass kerb 0.2 pts 0,0,1 4,0,1 4,3\ndrop 11\njoin 3 12\n";
            var set = JunctionMeasures.Parse(text);
            Assert.AreEqual("hand", set.provider, "provider");
            Assert.IsTrue(set.edges.Count == 1 && set.edges[7].blend == 9f && set.edges[7].st.Count == 2 && set.edges[7].st[0].s == 1.5f && float.IsNaN(set.edges[7].st[0].right) && set.edges[7].st[1].right == 3.25f, "edge stations (sorted, '-' = no measure)");
            var j = set.For(new HashSet<int> { 4, 99 });
            Assert.IsTrue(j != null && j.name == "j1" && j.noses.Count == 1 && j.returns.Count == 1 && j.islands.Count == 1 && j.drop[0] == 11 && j.join[0] == (3, 12), "junction items");
            Assert.IsNull(set.For(new HashSet<int> { 98 }), "a cluster the measures do not name keeps the rules");
            var m = JunctionMeasures.ToSolver(set, j, new Vector2(1f, 1f));
            Assert.IsTrue(m.noses[0].capA == new Vector2(0f, 1f) && m.noses[0].kind == 1 && m.returns[0].r == 18f && m.islands[0].kind == 0 && m.islands[0].pts[1] == new Vector2(3f, -1f) && m.islands[0].r[2] == 0.5f, "about the cluster's origin");
        }

        [Test] public void MeasuredReturnRadius()
        {
            // a T whose two kerb returns are measured 15 m and 4 m (the class says 9 m)
            var arms = new List<JunctionOutline.Arm> {
                JunctionOutline.StraightArm(0, Vector2.zero, Dir(0), Half(2), Half(2), name: "Main"), JunctionOutline.StraightArm(1, Vector2.zero, Dir(180), Half(2), Half(2), name: "Main"),
                JunctionOutline.StraightArm(2, Vector2.zero, Dir(90), Half(1), Half(1), name: "Side") };
            var meas = new JunctionOutline.Measures { provider = "hand" };
            meas.returns.Add(new JunctionOutline.Return { a = 0, b = 2, r = 15f }); meas.returns.Add(new JunctionOutline.Return { a = 2, b = 1, r = 4f });
            var r = JunctionOutline.Solve(arms, null, null, meas);
            Assert0(r, "measured kerb returns (15 m and 4 m)");
            var c0 = r.corners.Find(c => c.a == 0 && c.b == 2); var c1 = r.corners.Find(c => c.a == 2 && c.b == 1);
            Assert.IsTrue(c0 != null && c0.shape == "arc" && Mathf.Abs(c0.r - 15f) < 1e-3f && c1 != null && c1.shape == "arc" && Mathf.Abs(c1.r - 4f) < 1e-3f, $"returns {c0?.shape} r{c0?.r} / {c1?.shape} r{c1?.r}");
        }

        [Test] public void MeasuresParseRound2()
        {
            // round 2: edge kinds, a lane cap, lane picks, an arrow, a stop line, corners paved up to a line
            string text = "provider hand\nedge 7 blend 9 hold b lanes 2 left paint 1.5 right shoulder 0.3\ns 0 6 4\nline 1 solid\np 0 -0.5\np 10 -0.7\nline 3 broken paint\np 2 5\np 8 6\narrow 12.5 4.4 left\nstop 25.6\nedge 8 right line\ns 0 - 3\njunction j1 nodes 3 4\npaved 7 8 open at 6\npaved 8 7 pts 0,0 1,2 3,4\n";
            var set = JunctionMeasures.Parse(text);
            var e = set.edges[7];
            Assert.IsTrue(e.holdB && !e.holdA && e.lanes == 2 && e.kindL == 2 && e.shoulderL == 1.5f && e.kindR == 1 && e.shoulderR == 0.3f, "edge kinds and the lane cap");
            Assert.IsTrue(e.lines.Count == 2 && e.lines[0].index == 1 && e.lines[0].kind == JunctionMeasures.LkSolid && !e.lines[0].paintOnly && e.lines[1].paintOnly && e.lines[1].kind == JunctionMeasures.LkBroken && e.lines[1].st.Count == 2, "lane picks");
            Assert.IsTrue(e.arrows.Count == 1 && e.arrows[0].x == 12.5f && e.arrows[0].z < 0f && e.stop == 25.6f, "an arrow and a stop line");
            Assert.IsTrue(set.edges[8].kindR == 3 && float.IsNaN(set.edges[8].stop), "a side that is a lane line");
            var j = set.junctions[0];
            Assert.IsTrue(j.paved.Count == 2 && j.paved[0].open && j.paved[0].at == 6f && !j.paved[1].open && j.paved[1].pts.Count == 3, "paved corners");
            Assert.IsTrue(e.lines[0].At(5f, out float off, out float w) && Mathf.Abs(off + 0.6f) < 1e-3f && w == 1f, $"a pick between two stations: {off}");
            Assert.IsTrue(!e.lines[0].At(20f, out _, out _), "no pick beyond its last station (a 3 m fade)");
            var m = JunctionMeasures.ToSolver(set, j, new Vector2(1f, 1f));
            Assert.IsTrue(m.paved.Count == 2 && m.paved[0].open && m.paved[1].pts[2] == new Vector2(2f, 3f), "paved corners about the cluster's origin");
        }

        [Test] public void PavedLineCorner()
        {
            // one road going on; on one side the pavement runs out to a measured line (the mouth of a street the map lacks:
            // a bulge 1.5 m deep, tangent to both kerbs), the other side is the rule's
            var arms = new List<JunctionOutline.Arm> {
                JunctionOutline.StraightArm(0, Vector2.zero, Dir(0), Half(2), Half(2), name: "Main"), JunctionOutline.StraightArm(1, Vector2.zero, Dir(180), Half(2), Half(2), name: "Main") };
            float h = Half(2);
            var pv = new JunctionOutline.Paved { a = 0, b = 1 };
            for (float x = 12f; x >= -12.001f; x -= 2f) pv.pts.Add(new Vector2(x, h + (Mathf.Abs(x) < 8f ? 0.75f * (1f + Mathf.Cos(Mathf.PI * x / 8f)) : 0f)));
            var meas = new JunctionOutline.Measures { provider = "hand" };
            meas.paved.Add(pv);
            var r = JunctionOutline.Solve(arms, null, null, meas);
            Assert0(r, "a corner paved up to a measured line");
            var c = r.corners.Find(q => q.a == 0 && q.b == 1);
            Assert.IsTrue(c != null && c.shape == "arc" && c.note == "paved to a measured line" && c.through, $"corner {c?.shape} ({c?.note}) through {c?.through}");
            Assert.IsTrue(JunctionOutline.Clearance(r, new Vector2(0f, h + 1.3f)) > 0.1f && JunctionOutline.Clearance(r, new Vector2(0f, h + 1.7f)) < -0.1f, "the pavement ends on the measured line");
            // the two rows stand beyond the line's ends
            for (int i = 0; i < r.arms.Count; i++) Assert.IsTrue(r.sR[i] >= 12f - 1e-3f && r.sL[i] >= 12f - 1e-3f, $"row of arm {r.arms[i].id} at {r.sR[i]} / {r.sL[i]}");
        }

        /// <summary>Batch entry (-executeMethod PSXRacing.EditorTools.JunctionOutlineTests.RunAll): every [Test] here, PASS / FAIL
        /// lines to Screenshots/c7/junction_outline_tests.txt (the same tests run in the Test Runner, EditMode).</summary>
        public static void RunAll()
        {
            var log = new System.Text.StringBuilder("JUNCTION OUTLINE TESTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            var logs = new List<string>();
            Application.LogCallback cb = (m, st, t) => { if (m.StartsWith("[JunctionOutlineTests]")) logs.Add(m); };
            Application.logMessageReceived += cb;
            Seen.Clear();
            int fail = 0, n = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var m in typeof(JunctionOutlineTests).GetMethods())
            {
                if (m.GetCustomAttributes(typeof(TestAttribute), false).Length == 0) continue;
                n++;
                try { m.Invoke(new JunctionOutlineTests(), null); log.Append("PASS " + m.Name + "\n"); }
                catch (System.Exception ex) { fail++; var e = ex.InnerException ?? ex; log.Append("FAIL " + m.Name + ": " + e.Message.Split('\n')[0] + "\n"); }
            }
            Application.logMessageReceived -= cb;
            log.Append($"{n} tests, {fail} failed, {sw.Elapsed.TotalSeconds:0.0} s" + "\n");
            foreach (var l in logs) log.Append(l + "\n");
            string dir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "Screenshots", "c7");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "junction_outline_tests.txt"), log.ToString());
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
