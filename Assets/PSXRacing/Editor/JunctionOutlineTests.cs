using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// c7roads round 5, stage 0 f: PER-KIND TESTS of the junction outline solver (JunctionOutline, a pure function) on
    /// synthetic arms, seconds to run (Test Runner, EditMode, or -runTests -testPlatform EditMode). The checks do not
    /// trust the solver: a simple outline, no kink by rule 4 anywhere but at the cut rows' corners, every 0.25 m sample
    /// covered EXACTLY ONCE by the fill (0 a hole, 2 a layer) where the outline says road and never where it does not, the
    /// travelled way of every arm inside the junction all road. Heights are not in the 2D solver (no step check here).
    /// </summary>
    public class JunctionOutlineTests
    {
        const float Lane = 3.66f, Sh = 0.3f;
        static float Half(int lanes) => lanes * Lane * 0.5f + Sh;
        static Vector2 Dir(float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));

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
            // rule 4 everywhere but at the cut rows' corners (where the ribbon carries on)
            var cutCorners = new HashSet<Vector2>();
            for (int i = 0; i < r.arms.Count; i++)
            {
                int k = Mathf.Min(r.arms[i].L.Count - 1, Mathf.RoundToInt(r.cut[i] / JunctionOutline.Step));
                cutCorners.Add(r.arms[i].L[k]); cutCorners.Add(r.arms[i].R[k]);
            }
            for (int i = 0; i < o.Count; i++)
            {
                var b = o[i];
                bool atCut = false; foreach (var cc in cutCorners) if ((cc - b).sqrMagnitude < 1e-4f) { atCut = true; break; }
                if (atCut) continue;
                if (JunctionOutline.Kink(o[(i - 1 + o.Count) % o.Count], b, o[(i + 1) % o.Count])) return $"kink at {b}";
            }
            // covered exactly once: a jittered 0.25 m grid over the outline's box
            var min = o[0]; var max = o[0];
            foreach (var p in o) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            var rnd = new System.Random(7);
            int bad = 0; string first = null;
            for (float x = min.x; x <= max.x; x += sample)
                for (float y = min.y; y <= max.y; y += sample)
                {
                    var p = new Vector2(x + (float)rnd.NextDouble() * 0.01f + 0.003f, y + (float)rnd.NextDouble() * 0.01f + 0.004f);
                    bool road = JunctionOutline.Covers(r, p);
                    int n = JunctionOutline.CoverCount(r, p);
                    if ((road && n != 1) || (!road && n != 0)) { bad++; first ??= $"{p}: outline says {(road ? "road" : "not road")}, the fill covers it {n} times"; }
                }
            if (bad > 0) return $"coverage: {bad} samples wrong, first {first}";
            // the travelled way inside the junction is road
            for (int i = 0; i < r.arms.Count; i++)
            {
                var a = r.arms[i];
                int kc = Mathf.Min(a.L.Count - 1, Mathf.RoundToInt(r.cut[i] / JunctionOutline.Step));
                for (int k = 2; k < kc; k++)
                    for (float f = 0.1f; f <= 0.9f; f += 0.2f)
                    {
                        var p = Vector2.Lerp(a.R[k], a.L[k], f);
                        if (!JunctionOutline.Covers(r, p)) return $"arm {a.id}: its lane at {p} is not road";
                    }
            }
            return null;
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

        static void Assert0(JunctionOutline.Result r, string kind)
        {
            string why = Check(r);
            Debug.Log($"[JunctionOutlineTests] {kind}: {(why ?? "PASS")}; corners {string.Join(", ", r.corners.ConvertAll(c => c.shape + (c.r > 0 ? $" r{c.r:0.0}" : "")))}; " +
                      $"outline {r.outline.Count} vertices, fill {r.tris.Count / 3} triangles, slivers {r.slivers}, solve {r.solveMs:0.00} ms, fill {r.fillMs:0.00} ms");
            Assert.IsNull(why, kind + ": " + why);
        }

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
            Assert0(JunctionOutline.Solve(new List<JunctionOutline.Arm> { host0, host1, bay }), "bay at its host");
        }
        [Test] public void SlipLaneEnd()
        {
            var a0 = JunctionOutline.StraightArm(0, Vector2.zero, Dir(0), Half(2), Half(2), name: "Main");
            var a1 = JunctionOutline.StraightArm(1, Vector2.zero, Dir(180), Half(2), Half(2), name: "Main");
            var slip = JunctionOutline.CurvedArm(2, new Vector2(0f, -(Half(2) + Half(1) - 0.3f)), Dir(-20), -40f, Half(1), Half(1), oneway: true);
            Assert0(JunctionOutline.Solve(new List<JunctionOutline.Arm> { a0, a1, slip }), "slip lane end");
        }
        [Test] public void Fork()
        {
            var up = JunctionOutline.StraightArm(0, Vector2.zero, Dir(180), Half(3), Half(3), oneway: true, name: "Link");
            var l = JunctionOutline.StraightArm(1, new Vector2(0f, Half(2) - 0.4f), Dir(12), Half(2), Half(2), oneway: true, name: "Link");
            var r = JunctionOutline.StraightArm(2, new Vector2(0f, -(Half(1) + 0.4f)), Dir(-14), Half(1), Half(1), oneway: true, name: "Slip");
            Assert0(JunctionOutline.Solve(new List<JunctionOutline.Arm> { up, l, r }), "fork");
        }
        [Test] public void SplitOfOneRoad()
        {
            var u = JunctionOutline.StraightArm(0, Vector2.zero, Dir(180), Half(4), Half(4), name: "Road");
            var a = JunctionOutline.StraightArm(1, new Vector2(0f, Half(2) - 0.3f), Dir(12), Half(2), Half(2), oneway: true, name: "Road");
            var b = JunctionOutline.StraightArm(2, new Vector2(0f, -(Half(2) - 0.3f)), Dir(-12), Half(2), Half(2), oneway: true, name: "Road");
            Assert0(JunctionOutline.Solve(new List<JunctionOutline.Arm> { u, a, b }), "split of one road");
        }
        [Test] public void MedianOpening()
        {
            // two carriageways 6 m apart and the crossover between them: a cluster's arms that start apart
            var nb0 = JunctionOutline.StraightArm(0, new Vector2(0f, 6f + Half(2)), Dir(0), Half(2), Half(2), oneway: true, name: "Blvd");
            var nb1 = JunctionOutline.StraightArm(1, new Vector2(0f, 6f + Half(2)), Dir(180), Half(2), Half(2), oneway: true, name: "Blvd");
            var sb0 = JunctionOutline.StraightArm(2, new Vector2(0f, -(6f + Half(2))), Dir(0), Half(2), Half(2), oneway: true, name: "Blvd");
            var sb1 = JunctionOutline.StraightArm(3, new Vector2(0f, -(6f + Half(2))), Dir(180), Half(2), Half(2), oneway: true, name: "Blvd");
            Assert0(JunctionOutline.Solve(new List<JunctionOutline.Arm> { nb0, nb1, sb0, sb1 }), "median opening");
        }
        [Test] public void IslandHoleFill()
        {
            // a crossing with a teardrop island in one corner: the fill bridges the hole and covers everything once
            var arms = new List<JunctionOutline.Arm>();
            for (int i = 0; i < 4; i++) arms.Add(JunctionOutline.StraightArm(i, Vector2.zero, Dir(90 * i), Half(2), Half(2), radius: 15f, name: i % 2 == 0 ? "A" : "B"));
            var hole = new List<Vector2>();
            for (int k = 0; k < 24; k++) { float a = k * Mathf.PI * 2f / 24f; hole.Add(new Vector2(10.5f, 10.5f) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 1.2f); }
            var r = JunctionOutline.Solve(arms, new List<List<Vector2>> { hole });
            Assert0(r, "crossing with an island hole");
        }
        [Test] public void Sweep()
        {
            // a fixed-seed sweep over angle, lanes and radius (T and crossing); grade is not in the 2D solver
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
                if (why != null) { fails++; if (first.Length == 0) first = $"case {n} (side {side:0} deg, lanes {lm}/{ls}, r {rad:0.0}): {why}"; }
            }
            Debug.Log($"[JunctionOutlineTests] sweep: 60 cases, {fails} fail, {ms:0.0} ms in all; {first}");
            Assert.AreEqual(0, fails, first);
        }

        /// <summary>Batch entry (-executeMethod PSXRacing.EditorTools.JunctionOutlineTests.RunAll): every [Test] here, PASS / FAIL
        /// lines to Screenshots/c7/junction_outline_tests.txt (the same tests run in the Test Runner, EditMode).</summary>
        public static void RunAll()
        {
            var log = new System.Text.StringBuilder("JUNCTION OUTLINE TESTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            var logs = new List<string>();
            Application.LogCallback cb = (m, st, t) => { if (m.StartsWith("[JunctionOutlineTests]")) logs.Add(m); };
            Application.logMessageReceived += cb;
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
