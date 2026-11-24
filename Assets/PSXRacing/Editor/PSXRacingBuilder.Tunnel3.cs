using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LITTLE SWITZERLAND TUNNEL, ROUND 2 (tunnel2, owner 2026-11-23 on round 1: "this is not what they look
    /// like (inside or outside) - where did all the foliage go?", and on PC60: "I see a lot of gaps around the tunnel
    /// remodel. I also see stretched textures on the banks."). PSX_TUNNEL3=0 = round 1 (PSXRacingBuilder.Tunnel2.cs).
    ///
    /// Built to the REAL portal (Blue Ridge Parkway milepost 333.4, public photographs, reference only):
    ///   * a near-semicircular arch on short legs (round 1's bore: radius 4.3 m springing 1.6 m, 1.46 : 1);
    ///   * a ring of 21 LARGE rough voussoirs about 1 m deep, each its own width and depth (an irregular extrados),
    ///     carried down the legs as long-and-short quoins;
    ///   * a SMALL headwall: 2.5 m of wall past the ring each side, its flat top 1.05 m over the ring's crown, the
    ///     outermost 1.3 m a step lower; courses 0.30-0.50 m of big rough-faced (pillowed) blocks 0.7-1.7 m long, pale
    ///     grey, some tan-stained, thick moss on the top courses. No wing walls, no projecting coping;
    ///   * the natural hillside straight onto the wall: one surface laid over the bore and both mouths, on the
    ///     approach's own ground where that is (just under it), over the masonry's top behind it, spilling over the
    ///     wall's ends at the slope of the land and down to the verge in front of them; ground where it is a slope a
    ///     forest floor holds (under 46 degrees), rock where it is steeper (the dark ledges of the photographs);
    ///     mossy boulders on it; and the forest, undergrowth and scatter planted on it like on any slope of the stage
    ///     (it is handed to them as a fill: StageFillY, and kept clear of the stone: StageKeepOut);
    ///   * the maroon rounded sign with a white border and white serif letters on one wooden post, right of the road
    ///     before each mouth.
    /// No surface is stretched: the hill's ground is the ground's own world-planar mapping at the ground's tile on
    /// slopes under 46 degrees (at most 1.44 : 1), its rock the cut's rock at the cut's scale; the stones and the
    /// bore's rock take a third, top-down plane in the shader (_RockTop) so a crown or a block's top is not the side
    /// planes' streaks.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        static bool Tunnel3 => System.Environment.GetEnvironmentVariable("PSX_TUNNEL3") != "0";
        /// <summary>(tunnel2b, BUILD TIME) round 2b's flanks and closures: the slopes beside the wall laid back as
        /// leaf-litter ground (42 deg) with the wall's ends standing, the hill kept low behind an approach guard rail
        /// (no rail collider with a hill a few metres behind it), the hill's first row hard against the face and a
        /// floor under the verge at each mouth. PSX_TUNNEL3B=0 = round 2 (b1bd5967).</summary>
        public static bool Tunnel3B = System.Environment.GetEnvironmentVariable("PSX_TUNNEL3B") != "0";

        /// <summary>The voussoir ring's radial depth (mean), the wall past the ring each side, the top over the
        /// ring's crown, the end step.</summary>
        const float T3RingW = 1.0f, T3WallPastRing = 2.5f, T3TopOverRing = 1.05f, T3StepW = 1.3f, T3StepDrop = 0.85f;
        const float T3Bottom = -1.0f, T3CapH = 0.42f, T3Joint = 0.025f;
        /// <summary>The steepest the hill's ground may be before it is rock (degrees): 46 keeps the ground's world
        /// planar mapping under 1.44 : 1.</summary>
        const float T3RockSlopeDeg = 46f;

        static float T3WallX(float halfW) => halfW + T3RingW + T3WallPastRing;
        static float T3Top(float halfW) => T2CrownY(halfW) + T3RingW + T3TopOverRing;
        static float T3StepX(float halfW) => T3WallX(halfW) - T3StepW;
        static float T3StepTop(float halfW) => T3Top(halfW) - T3StepDrop;
        /// <summary>The masonry's top at |x| (over the portal's road height).</summary>
        static float T3MasonryTop(float ax, float halfW) => ax <= T3StepX(halfW) ? T3Top(halfW) : T3StepTop(halfW);

        // ---- the hill handed to the planting (FillGrid) and the stone kept clear (keep-out) ----
        sealed class T3Keep { public float ox, oz, cell; public int cols, rows; public bool[] no; }
        static readonly List<T3Keep> t3Keeps = new List<T3Keep>();
        /// <summary>The hills as drawn (the visible surface: the hill where it shows, the ground mesh where that is
        /// over it), read by StageLatticeY for everything planted after the tunnels.</summary>
        static readonly List<FillGrid> t3Grounds = new List<FillGrid>();
        static bool T3GroundAt(float x, float z, out float y)
        {
            foreach (var g in t3Grounds)
            {
                y = StageFillYAt(g, x, z);
                if (!float.IsNaN(y)) return true;
            }
            y = 0f;
            return false;
        }
        static readonly List<Vector4> t3Boulders = new List<Vector4>();   // x, y, z, radius
        static int t3Boulder, t3HillTris, t3RockTris;

        /// <summary>No plant on the portal's stone, its rock ledges, the strip in front of the wall, or a boulder.</summary>
        static bool T3KeepOut(float x, float z)
        {
            if (t3Keeps.Count == 0) return false;
            foreach (var k in t3Keeps)
            {
                int c = Mathf.FloorToInt((x - k.ox) / k.cell), r = Mathf.FloorToInt((z - k.oz) / k.cell);
                if (c >= 0 && r >= 0 && c < k.cols && r < k.rows && k.no[r * k.cols + c]) return true;
            }
            foreach (var b in t3Boulders)
                if ((x - b.x) * (x - b.x) + (z - b.z) * (z - b.z) < b.w * b.w * 1.2f) return true;
            return false;
        }

        /// <summary>Is (x, z) on the hill OVER a bore (its nearest road is in the tube, metres under it)? The
        /// planting's road margins (the lanes, the shoulder, the graded roadside, the low crowns) are for a road
        /// in the open; over the tube the hill is a hillside like any other.</summary>
        static bool T3OverBore(in RoadFoot f, float x, float z)
        {
            if (!Tunnel3 || t3Grounds.Count == 0 || tunnelIn == null) return false;
            int st = Mathf.Clamp(Mathf.RoundToInt(f.station), 0, tunnelIn.Length - 1);
            if (!tunnelIn[st] || !T3GroundAt(x, z, out float y)) return false;
            return y > f.roadY + T2CrownY(RoadWidth * 0.5f + TunnelWallOut) + 0.8f;
        }

        /// <summary>(tunnel2b, PSX_ROADSIDEUV2) a stage's ground / shoulder materials sample the world plane a surface
        /// faces (_GroundTri = 1 / the ground tile: the top plane is exactly their uv), the cuts' rock takes its top
        /// plane. Every material of this venue's prefix whose name starts Ground or RoadEdge, and its Bank.</summary>
        static void RoadsideMaterials()
        {
            if (track == null || !track.stage) return;
            float tri = RoadsideUV2 ? 1f / Mathf.Max(theme.groundTile, 0.01f) : 0f;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MatDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                string nm = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!nm.StartsWith(MeshPrefix)) continue;
                string rest = nm.Substring(MeshPrefix.Length);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) continue;
                if ((rest.StartsWith("Ground") && !rest.StartsWith("GroundFar")) || rest.StartsWith("RoadEdge"))
                {
                    if (m.HasProperty("_GroundTri")) { m.SetFloat("_GroundTri", tri); EditorUtility.SetDirty(m); }
                }
                else if (rest == "Bank" && m.HasProperty("_RockTop")) { m.SetFloat("_RockTop", RoadsideUV2 ? 1f : 0f); EditorUtility.SetDirty(m); }
            }
        }

        static void T3Reset() { t3Grounds.Clear(); t3Keeps.Clear(); t3Boulders.Clear(); t3Boulder = 0; t3HillTris = 0; t3RockTris = 0; }

        // ------------------------------------------------------------------
        //  A rough-faced stone
        // ------------------------------------------------------------------
        /// <summary>One stone with a PILLOWED rough face: the outline (four points on the face plane) bevelled back
        /// to the mortar, the face a fan from a centre pushed proud of its corners (each corner its own depth).
        /// <paramref name="col"/> its colour (tint, stain and moss in it); the top bevel mossier.</summary>
        static void T3Stone(List<Vector3> v, List<Vector2> uv, List<Color> col, List<int> t, System.Func<float, float, float, Vector3> P,
                            Vector2[] q, float proud, float bevel, Color c, float mossTop, Vector3 faceDir, System.Random rnd)
        {
            Vector2 mid = (q[0] + q[1] + q[2] + q[3]) * 0.25f;
            float size = Mathf.Min((q[2] - q[0]).magnitude, (q[3] - q[1]).magnitude);
            var fr = new Vector3[4]; var bk = new Vector3[4];
            for (int k = 0; k < 4; k++)
            {
                Vector2 d = mid - q[k];
                float len = d.magnitude;
                Vector2 qi = q[k] + (len > 1e-4f ? d / len * Mathf.Min(bevel, len * 0.4f) : Vector2.zero);
                float dp = proud * (0.55f + 0.5f * (float)rnd.NextDouble());
                fr[k] = P(qi.x, qi.y, dp);
                bk[k] = P(q[k].x, q[k].y, -0.03f);
            }
            // the pillow: the face's middle stands out by a share of the stone's size, a little off centre
            Vector2 mOff = mid + new Vector2((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * size * 0.25f;
            Vector3 centre = P(mOff.x, mOff.y, proud + Mathf.Clamp(size * 0.09f, 0.02f, 0.09f) * (0.6f + 0.6f * (float)rnd.NextDouble()));
            Color cTop = Color.Lerp(c, new Color(c.r * 0.62f, c.g * 0.80f, c.b * 0.44f, 1f), mossTop);
            Color cSide = c * 0.90f; cSide.a = 1f;
            void Tri(Vector3 a, Vector3 b, Vector3 d, Color ca, Color cb, Color cd, Vector3 want)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(d);
                col.Add(ca); col.Add(cb); col.Add(cd);
                foreach (var p in new[] { a, b, d }) uv.Add(new Vector2(p.x + p.z, p.y) / 1.9f);
                Vector3 n = Vector3.Cross(b - a, d - a);
                if (Vector3.Dot(n, want) >= 0f) { t.Add(i); t.Add(i + 1); t.Add(i + 2); } else { t.Add(i); t.Add(i + 2); t.Add(i + 1); }
            }
            Color cc = c * 1.04f; cc.a = 1f;
            for (int k = 0; k < 4; k++)
            {
                int k1 = (k + 1) % 4;
                Vector3 em = (fr[k] + fr[k1]) * 0.5f - centre;
                bool top = em.y > 0.01f && Mathf.Abs(em.y) > 0.5f * em.magnitude;
                Color ce = top ? Color.Lerp(c, cTop, 0.6f) : c;
                Tri(centre, fr[k], fr[k1], cc, ce, ce, faceDir);
            }
            for (int k = 0; k < 4; k++)
            {
                int k1 = (k + 1) % 4;
                Vector3 em = (fr[k] + fr[k1]) * 0.5f - (fr[0] + fr[1] + fr[2] + fr[3]) * 0.25f;
                bool top = em.y > 0.01f && Mathf.Abs(em.y) > 0.5f * em.magnitude;
                Color cs = top ? cTop : cSide;
                Vector3 want = em.normalized + faceDir * 0.3f;
                Tri(fr[k], fr[k1], bk[k1], cs, cs, cs, want);
                Tri(fr[k], bk[k1], bk[k], cs, cs, cs, want);
            }
            t2Stones++;
        }

        /// <summary>A stone's colour: pale grey, now and then tan-stained, darker now and then; moss (0..1) greens it.</summary>
        static Color T3StoneColour(System.Random rnd, float moss)
        {
            float g = 0.60f + 0.22f * (float)rnd.NextDouble();
            if (rnd.NextDouble() < 0.12) g *= 0.82f;
            var c = new Color(g, g, g * 0.985f, 1f);
            if (rnd.NextDouble() < 0.25) { float s = 0.4f + 0.6f * (float)rnd.NextDouble(); c = new Color(c.r * (1f + 0.07f * s), c.g * (1f - 0.01f * s), c.b * (1f - 0.13f * s), 1f); }
            if (moss > 0f) c = Color.Lerp(c, new Color(g * 0.58f, g * 0.74f, g * 0.38f, 1f), Mathf.Clamp01(moss));
            return c;
        }

        // ------------------------------------------------------------------
        //  The portal
        // ------------------------------------------------------------------
        static void BuildPortal3(List<Vector3> pts, int i, float dir, float halfW, Transform parent, int no, Material stoneMat, Material mortarMat)
        {
            Vector3 right = RightAt(pts, i);
            Vector3 fwd = Vector3.Cross(right, Vector3.up).normalized;
            Vector3 face = fwd * dir;
            Vector3 c = pts[i];
            Vector3 P(float x, float y, float d) => c + right * x + Vector3.up * y + face * d;
            var rnd = new System.Random(5281 + no * 97);
            float Rnd(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

            float R = T2Radius(halfW);
            float wallX = T3WallX(halfW), stepX = T3StepX(halfW), top = T3Top(halfW), stepTop = T3StepTop(halfW);
            float j = T3Joint;
            var sv = new List<Vector3>(); var suv = new List<Vector2>(); var sc = new List<Color>(); var st = new List<int>();

            // ---- the ring: 21 voussoirs of their own widths and depths ----
            const int VN = 21;
            var wgt = new float[VN]; float wsum = 0f;
            for (int q = 0; q < VN; q++) { wgt[q] = Rnd(0.78f, 1.22f); wsum += wgt[q]; }
            var a0s = new float[VN + 1];
            for (int q = 0; q < VN; q++) a0s[q + 1] = a0s[q] + Mathf.PI * wgt[q] / wsum;
            var re = new float[VN];
            for (int q = 0; q < VN; q++) re[q] = R + T3RingW + Rnd(-0.10f, 0.16f);
            Vector2 Pt(float a, float rad) => new Vector2(rad * Mathf.Cos(a), T2SpringY + rad * Mathf.Sin(a));
            for (int q = 0; q < VN; q++)
            {
                float a0 = a0s[q] + j / R, a1 = a0s[q + 1] - j / R;
                var outline = new[] { Pt(a0, R), Pt(a0, re[q]), Pt(a1, re[q]), Pt(a1, R) };
                float mossQ = Mathf.Clamp01((Mathf.Sin((a0 + a1) * 0.5f) - 0.6f) * 1.6f) * Rnd(0.3f, 0.9f);
                T3Stone(sv, suv, sc, st, P, outline, Rnd(0.06f, 0.11f), 0.04f, T3StoneColour(rnd, mossQ), 0.8f, face, rnd);
                // its soffit into the bore (seen looking up at the mouth), just inside the lining's line
                int b = sv.Count;
                Vector2 i0 = Pt(a0s[q], R - 0.015f), i1 = Pt(a0s[q + 1], R - 0.015f);
                sv.Add(P(i0.x, i0.y, 0.05f)); sv.Add(P(i1.x, i1.y, 0.05f)); sv.Add(P(i1.x, i1.y, -0.62f)); sv.Add(P(i0.x, i0.y, -0.62f));
                var cs = T3StoneColour(rnd, 0f) * 0.78f; cs.a = 1f;
                for (int k = 0; k < 4; k++) sc.Add(cs);
                foreach (var p in new[] { sv[b], sv[b + 1], sv[b + 2], sv[b + 3] }) suv.Add(new Vector2(p.x + p.z, p.y) / 1.9f);
                Vector2 mid = (i0 + i1) * 0.5f;
                QuadFacing(sv, st, b, b + 1, b + 2, b + 3, -(right * mid.x + Vector3.up * (mid.y - T2SpringY)));
            }
            // the ring's outer edge at height y on one side (the extrados of the stone that spans it)
            float ExtX(float y, float s)
            {
                if (y <= T2SpringY) return float.NaN;
                float best = 0f;
                for (int q = 0; q < VN; q++)
                {
                    float am = (a0s[q] + a0s[q + 1]) * 0.5f;
                    if ((Mathf.Cos(am) >= 0f) != (s > 0f)) continue;
                    float dy = y - T2SpringY;
                    if (dy > re[q]) continue;
                    // the stone's extrados x at this height, if its angular span covers it
                    float x = Mathf.Sqrt(re[q] * re[q] - dy * dy);
                    float ang = Mathf.Atan2(dy, s * x);
                    float lo = Mathf.Min(a0s[q], a0s[q + 1]) - 0.02f, hi = Mathf.Max(a0s[q], a0s[q + 1]) + 0.02f;
                    if (ang >= lo && ang <= hi) best = Mathf.Max(best, x);
                }
                return best;
            }

            // ---- course levels: the legs' courses, then the face's up to the step, then the middle's ----
            var levels = new List<float> { T3Bottom };
            foreach (float y in new[] { -0.35f, 0.28f, 0.94f }) levels.Add(y);
            levels.Add(T2SpringY);
            {
                float y = T2SpringY, stopA = stepTop - T3CapH;
                while (y < stopA - 0.05f)
                {
                    float h = Rnd(0.30f, 0.50f);
                    if (stopA - (y + h) < 0.26f) h = stopA - y;
                    y += h; levels.Add(y);
                }
                float stopB = top - T3CapH;
                while (y < stopB - 0.05f)
                {
                    float h = Rnd(0.30f, 0.50f);
                    if (stopB - (y + h) < 0.26f) h = stopB - y;
                    y += h; levels.Add(y);
                }
            }
            float stepCourseTop = stepTop - T3CapH;
            int nCourses = levels.Count - 1;
            for (int k = 0; k < nCourses; k++)
            {
                float y0 = levels[k] + j * 0.5f, y1 = levels[k + 1] - j * 0.5f;
                float xEnd = levels[k + 1] <= stepCourseTop + 0.01f ? wallX : stepX;
                float fromTop = top - T3CapH - levels[k + 1];
                float moss = fromTop < 0.05f ? Rnd(0.55f, 0.95f) : fromTop < 0.5f ? Rnd(0.35f, 0.75f) : fromTop < 1.0f ? Rnd(0.0f, 0.4f) : (levels[k] < 0.3f ? Rnd(0f, 0.25f) : 0f);
                if (levels[k + 1] > stepCourseTop - 0.6f && levels[k + 1] <= stepCourseTop + 0.01f) moss = Mathf.Max(moss, Rnd(0.2f, 0.6f));
                bool leg = levels[k + 1] <= T2SpringY + 0.01f;
                float e0 = ExtX(y0, 1f), e1 = ExtX(y1, 1f), e0L = ExtX(y0, -1f), e1L = ExtX(y1, -1f);
                bool overRing = !leg && e0 <= 0.01f && e1 <= 0.01f && e0L <= 0.01f && e1L <= 0.01f;
                if (overRing)
                {
                    // one course across the whole top, joints staggered from the course below
                    float x = -xEnd + Rnd(0f, 0.2f) * 0f;
                    while (x < xEnd - 0.05f)
                    {
                        float len = Rnd(0.7f, 1.7f);
                        if (x == -xEnd) len *= Rnd(0.4f, 1f);
                        if (xEnd - (x + len) < 0.4f) len = xEnd - x;
                        var o = new[] { new Vector2(x + j * 0.5f, y0), new Vector2(x + j * 0.5f, y1), new Vector2(x + len - j * 0.5f, y1), new Vector2(x + len - j * 0.5f, y0) };
                        T3Stone(sv, suv, sc, st, P, o, Rnd(0.04f, 0.12f), Rnd(0.03f, 0.05f), T3StoneColour(rnd, moss), 0.9f, face, rnd);
                        x += len;
                    }
                    continue;
                }
                foreach (float s in new[] { -1f, 1f })
                {
                    float xa, xb;
                    if (leg)
                    {
                        // the quoin: long and short in turn, the ring's own stone carried down
                        float qw = (k % 2 == 0) == (s > 0f) ? 1.20f : 0.82f;
                        var qo = new[] { new Vector2(s * R, y0), new Vector2(s * R, y1), new Vector2(s * (R + qw - j * 0.5f), y1), new Vector2(s * (R + qw - j * 0.5f), y0) };
                        T3Stone(sv, suv, sc, st, P, qo, Rnd(0.06f, 0.11f), 0.04f, T3StoneColour(rnd, levels[k] < 0f ? Rnd(0f, 0.3f) : 0f), 0.6f, face, rnd);
                        // its return into the bore
                        int b = sv.Count;
                        float xr = s * (R - 0.01f);
                        sv.Add(P(xr, levels[k], 0.05f)); sv.Add(P(xr, levels[k + 1], 0.05f)); sv.Add(P(xr, levels[k + 1], -0.62f)); sv.Add(P(xr, levels[k], -0.62f));
                        var cs = T3StoneColour(rnd, 0f) * 0.78f; cs.a = 1f;
                        for (int m = 0; m < 4; m++) sc.Add(cs);
                        foreach (var p in new[] { sv[b], sv[b + 1], sv[b + 2], sv[b + 3] }) suv.Add(new Vector2(p.x + p.z, p.y) / 1.9f);
                        QuadFacing(sv, st, b, b + 1, b + 2, b + 3, -right * s);
                        xa = R + qw + j * 0.5f; xb = xa;
                    }
                    else
                    {
                        float f0 = ExtX(y0, s), f1 = ExtX(y1, s);
                        xa = f0 + j; xb = f1 + j;
                    }
                    float x = Mathf.Max(xa, xb);
                    bool first = true;
                    while (x < xEnd - 0.05f)
                    {
                        float len = Rnd(0.7f, 1.7f) * (first ? 0.8f : 1f);
                        if (xEnd - (x + len) < 0.4f) len = xEnd - x;
                        float xl = x + j * 0.5f, xr = x + len - j * 0.5f;
                        Vector2[] o = first
                            ? new[] { new Vector2(s * xa, y0), new Vector2(s * xb, y1), new Vector2(s * xr, y1), new Vector2(s * xr, y0) }
                            : new[] { new Vector2(s * xl, y0), new Vector2(s * xl, y1), new Vector2(s * xr, y1), new Vector2(s * xr, y0) };
                        T3Stone(sv, suv, sc, st, P, o, Rnd(0.04f, 0.12f), Rnd(0.03f, 0.05f), T3StoneColour(rnd, moss), 0.9f, face, rnd);
                        x += len; first = false;
                    }
                }
            }
            // ---- the top course: long flat stones, mossy, the face's own plane (no projecting coping) ----
            void Cap(float x0, float x1, float yTop)
            {
                float x = x0;
                while (x < x1 - 0.05f)
                {
                    float len = Rnd(0.9f, 1.8f);
                    if (x1 - (x + len) < 0.45f) len = x1 - x;
                    var o = new[] { new Vector2(x + j * 0.5f, yTop - T3CapH + j * 0.5f), new Vector2(x + j * 0.5f, yTop), new Vector2(x + len - j * 0.5f, yTop), new Vector2(x + len - j * 0.5f, yTop - T3CapH + j * 0.5f) };
                    T3Stone(sv, suv, sc, st, P, o, Rnd(0.05f, 0.10f), 0.035f, T3StoneColour(rnd, Rnd(0.6f, 1f)), 1f, face, rnd);
                    x += len;
                }
            }
            Cap(-stepX, stepX, top);
            Cap(-wallX, -stepX, stepTop);
            Cap(stepX, wallX, stepTop);
            // ---- the wall's top back into the hill, the step risers and the ends (the hill buries most of them) ----
            void Slab(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Vector3 want, float moss)
            {
                int bi = sv.Count;
                sv.Add(a); sv.Add(b); sv.Add(cc); sv.Add(d);
                var cl = T3StoneColour(rnd, moss) * 0.92f; cl.a = 1f;
                for (int m = 0; m < 4; m++) sc.Add(cl);
                foreach (var p in new[] { a, b, cc, d }) suv.Add(new Vector2(p.x + p.z, p.y) / 1.9f);
                QuadFacing(sv, st, bi, bi + 1, bi + 2, bi + 3, want);
            }
            const float Back = -1.25f;
            Slab(P(-stepX, top, 0.08f), P(stepX, top, 0.08f), P(stepX, top, Back), P(-stepX, top, Back), Vector3.up, 0.9f);
            foreach (float s in new[] { -1f, 1f })
            {
                Slab(P(s * stepX, stepTop, 0.08f), P(s * wallX, stepTop, 0.08f), P(s * wallX, stepTop, Back), P(s * stepX, stepTop, Back), Vector3.up, 0.9f);
                Slab(P(s * stepX, stepTop, 0.08f), P(s * stepX, top, 0.08f), P(s * stepX, top, Back), P(s * stepX, stepTop, Back), right * s, 0.5f);
                Slab(P(s * wallX, T3Bottom, 0.08f), P(s * wallX, stepTop, 0.08f), P(s * wallX, stepTop, Back), P(s * wallX, T3Bottom, Back), right * s, 0.2f);
            }

            // ---- the mortar plane behind every joint ----
            var mv = new List<Vector3>(); var muv = new List<Vector2>(); var mc = new List<Color>(); var mt = new List<int>();
            void MQuad(Vector2 a, Vector2 b2, Vector2 c2, Vector2 d2)
            {
                int b = mv.Count;
                foreach (var q in new[] { a, b2, c2, d2 }) { mv.Add(P(q.x, q.y, -0.035f)); muv.Add(q / 1.9f); mc.Add(new Color(0.5f, 0.5f, 0.5f, 1f)); }
                QuadFacing(mv, mt, b, b + 1, b + 2, b + 3, face);
            }
            const int FanN = 36;
            float topM = top - 0.01f;
            for (int q = 0; q < FanN; q++)
            {
                float a0 = Mathf.PI * q / FanN, a1 = Mathf.PI * (q + 1) / FanN;
                Vector2 i0 = Pt(a0, R), i1 = Pt(a1, R);
                // over the arch, up to the top - or the step's top past the step
                float y0t = Mathf.Abs(i0.x) > stepX ? stepTop - 0.01f : topM, y1t = Mathf.Abs(i1.x) > stepX ? stepTop - 0.01f : topM;
                MQuad(i0, new Vector2(i0.x, y0t), new Vector2(i1.x, y1t), i1);
            }
            foreach (float s in new[] { -1f, 1f })
            {
                MQuad(new Vector2(s * R, T3Bottom), new Vector2(s * R, T2SpringY), new Vector2(s * stepX, T2SpringY), new Vector2(s * stepX, T3Bottom));
                MQuad(new Vector2(s * R, T2SpringY), new Vector2(s * R, topM), new Vector2(s * stepX, topM), new Vector2(s * stepX, T2SpringY));
                MQuad(new Vector2(s * stepX, T3Bottom), new Vector2(s * stepX, stepTop - 0.01f), new Vector2(s * wallX, stepTop - 0.01f), new Vector2(s * wallX, T3Bottom));
            }
            T2Mesh("TunnelPortalStone" + no, sv, suv, sc, st, stoneMat, parent, null);
            T2Mesh("TunnelPortalMortar" + no, mv, muv, mc, mt, mortarMat, parent, null);
        }

        /// <summary>The portal's solids as drawn: the headwall each side of the mouth from the tube's wall to the
        /// wall's end, as tall as its top ("WallPortal", four a tube - the self-test's count).</summary>
        static void PortalColliders3(List<Vector3> pts, int i, float dir, float halfW, Transform parent, PhysicsMaterial phys)
        {
            Vector3 right = RightAt(pts, i);
            Vector3 fwd = Vector3.Cross(right, Vector3.up).normalized;
            Vector3 into = -fwd * dir;
            portalSpots.Add(pts[i]);
            float wallX = T3WallX(halfW);
            foreach (float side in new[] { -1f, 1f })
            {
                float x0 = halfW, x1 = wallX, width = x1 - x0;
                float y0 = pts[i].y - 1.5f, y1 = pts[i].y + T3Top(halfW);
                Vector3 p = pts[i] + right * (side * (x0 + width * 0.5f)) + into * (StageWallCollThick * 0.5f);
                var seg = new GameObject("WallPortal");
                seg.transform.SetParent(parent, false);
                seg.transform.position = new Vector3(p.x, (y0 + y1) * 0.5f, p.z);
                seg.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                var box = seg.AddComponent<BoxCollider>();
                box.size = new Vector3(width, y1 - y0, StageWallCollThick);
                box.sharedMaterial = phys;
                seg.layer = SolidLayer;
                seg.isStatic = true;
            }
        }

        // ------------------------------------------------------------------
        //  The hillside
        // ------------------------------------------------------------------
        /// <summary>
        /// The hill over the bore and round both mouths, one surface in the tunnel's frame (x across, rings along
        /// the path, a ring every 1.5 m and at the portal planes, a column every metre and at the wall's edges):
        ///   behind a portal: never under the masonry's top (the wall's top slab runs 1.25 m back under it), never
        ///   under 1.2 m over the crown over the bore, and otherwise the land - the ground mesh where it is (12 cm
        ///   under it, hidden) or the stage lattice over the hole the tube cuts in it;
        ///   beside and in front of a portal: the land, or where the land is lower, the hill spilling over the
        ///   wall's ends (falling from the wall's top at 1 : 1.35 forward and 1 : 1 toward the road) and a verge at
        ///   the road edge's height falling away beyond 2.5 m - so nothing shows under the guard rail and no wall
        ///   end stands free;
        ///   never in the road (in front of a mouth the tarmac and 0.3 m are left out), never in front of the
        ///   wall's face.
        /// Ground below <see cref="T3RockSlopeDeg"/>, the cut's rock above it (two submeshes, one renderer).
        /// </summary>
        static void BuildHill3(List<Vector3> pts, int from, int stations, Transform parent, int no, float halfW, Material groundMat, Material rockMat, Material boulderMat)
        {
            int n = pts.Count;
            Physics.SyncTransforms();
            float total = (stations - 1) * Spacing;
            float roadHalf = RoadWidth * 0.5f;
            float wallX = T3WallX(halfW), stepX = T3StepX(halfW), stepTop = T3StepTop(halfW);
            float crownMin = T2CrownY(halfW) + 1.2f;
            float reach = halfW + 1f + NearCell * 2.5f;
            const float Dout = 32f;
            float tile = Mathf.Max(theme.groundTile, 0.01f);

            // THE GROUND AS DRAWN (not its collider: a near chunk's collider keeps the quads the tube's hole
            // drops from its drawn mesh, and a hill laid on the collider stood over the road edge with nothing
            // under it - the gap the probe found right of the entry): every drawn GroundN triangle near the tube
            var gTris = new List<Vector3>();
            {
                var zone = new Bounds(pts[WrapIdx(from, n)], Vector3.one);
                zone.Encapsulate(pts[WrapIdx(from + stations - 1, n)]);
                zone.Expand(new Vector3(2f * (reach + Dout) + 20f, 400f, 2f * (reach + Dout) + 20f));
                foreach (var mf in parent.root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf == null || !mf.name.Contains("GroundN") || mf.sharedMesh == null) continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || !mr.enabled) continue;
                    var gm = mf.sharedMesh; var gv = gm.vertices; var gt = gm.triangles; var xf = mf.transform.localToWorldMatrix;
                    for (int k = 0; k + 2 < gt.Length; k += 3)
                    {
                        Vector3 a = xf.MultiplyPoint3x4(gv[gt[k]]), b = xf.MultiplyPoint3x4(gv[gt[k + 1]]), c = xf.MultiplyPoint3x4(gv[gt[k + 2]]);
                        if (!zone.Contains(new Vector3(a.x, zone.center.y, a.z))) continue;
                        gTris.Add(a); gTris.Add(b); gTris.Add(c);
                    }
                }
            }
            float GroundAt(Vector3 p, out bool mesh)
            {
                mesh = false;
                float best = float.NegativeInfinity;
                for (int k = 0; k + 2 < gTris.Count; k += 3)
                {
                    Vector3 a = gTris[k], b = gTris[k + 1], c = gTris[k + 2];
                    float den = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(den) < 1e-6f) continue;
                    float l1 = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / den;
                    if (l1 < -1e-4f || l1 > 1.0001f) continue;
                    float l2 = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / den;
                    float l3 = 1f - l1 - l2;
                    if (l2 < -1e-4f || l3 < -1e-4f) continue;
                    float y = l1 * a.y + l2 * b.y + l3 * c.y;
                    if (y > best) { best = y; mesh = true; }
                }
                return mesh ? best : StageLatticeY(p.x, p.z);
            }
            Vector3 Frame(float dist, out Vector3 r, out Vector3 fwd)
            {
                float fs = Mathf.Clamp(dist / Spacing, 0f, stations - 1 - 1e-4f);
                int s = Mathf.Min(Mathf.FloorToInt(fs), stations - 2); float tt = fs - s;
                int i0 = WrapIdx(from + s, n), i1 = WrapIdx(from + s + 1, n);
                r = Vector3.Lerp(RightAt(pts, i0), RightAt(pts, i1), tt).normalized;
                fwd = Vector3.Cross(r, Vector3.up).normalized;
                Vector3 c = Vector3.Lerp(pts[i0], pts[i1], tt);
                // past a mouth: on along the end's own heading, at the road's own height there
                if (dist < 0f || dist > total)
                {
                    float beyond = dist < 0f ? dist : dist - total;
                    int ie = dist < 0f ? WrapIdx(from + Mathf.RoundToInt(dist / Spacing), n) : WrapIdx(from + stations - 1 + Mathf.RoundToInt(beyond / Spacing), n);
                    c += fwd * beyond;
                    c.y = pts[ie].y;
                }
                return c;
            }
            Vector3 cA = Frame(0f, out _, out _), cB = Frame(total, out _, out _);

            // rows: every 1.5 m, and the portal planes' own (in front, behind, the slab's back edge)
            var ds = new List<float>();
            for (float d = -Dout; d <= total + Dout + 1e-3f; d += 1.5f) ds.Add(d);
            float fr = Tunnel3B ? 0.02f : 0.16f;   // (2b) the hill's first row hard against the face
            float[] plane = { -fr, 0.05f, 1.25f, total + fr, total - 0.05f, total - 1.25f };
            // (2b) an approach guard rail on a side of a mouth: its collider stands 1.7 m tall over a 0.8 m rail, so
            // the hill behind it is kept low (under the rail) for 4.3 m, or the collider is "held off by air"
            var guard = new bool[2, 2];   // [mouth A/B, side -/+]
            if (Tunnel3B)
                for (int mo = 0; mo < 2; mo++)
                    for (int sd = 0; sd < 2; sd++)
                    {
                        Vector3 mc = Frame(mo == 0 ? 0f : total, out Vector3 mr, out Vector3 mf);
                        Vector3 outw = mo == 0 ? -mf : mf;
                        Vector3 q = mc + mr * ((sd == 0 ? -1f : 1f) * (roadHalf + 1.6f)) + outw * 3f;
                        foreach (var h in Physics.RaycastAll(q + Vector3.up * 30f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
                            if (h.collider != null && h.collider.name.StartsWith("WallColl")) guard[mo, sd] = true;
                    }
            const float SlopeB = 0.9f;
            ds.RemoveAll(d => System.Array.Exists(plane, p => Mathf.Abs(p - d) < 0.45f));
            ds.AddRange(plane);
            ds.Sort();
            // columns: every metre, and the road's, the wall's and the step's edges
            var xs = new List<float>();
            for (float x = -reach; x <= reach + 1e-3f; x += 1f) xs.Add(x);
            float[] edges = { roadHalf + 0.3f, stepX, wallX, wallX + 0.04f, halfW + 1.5f };
            xs.RemoveAll(x => System.Array.Exists(edges, e => Mathf.Abs(Mathf.Abs(x) - e) < 0.3f));
            foreach (float e in edges) { xs.Add(e); xs.Add(-e); }
            xs.Sort();
            int R_ = ds.Count, C = xs.Count;

            var v = new Vector3[R_ * C];
            var own = new bool[R_ * C];
            var vis = new float[R_ * C];
            var u = new float[R_];
            for (int ri = 0; ri < R_; ri++)
            {
                float dist = ds[ri];
                bool atA = dist < total * 0.5f;
                u[ri] = atA ? -dist : dist - total;              // outward from the nearer mouth (> 0 in front of it)
                Vector3 c = Frame(dist, out Vector3 r, out _);
                float y0 = atA ? cA.y : cB.y;                    // the portal's road height
                for (int ci = 0; ci < C; ci++)
                {
                    float x = xs[ci], ax = Mathf.Abs(x);
                    Vector3 p = c + r * x;
                    float g = GroundAt(p, out bool onMesh);
                    float land = onMesh ? g - 0.12f : g;
                    float need;
                    // (2b) the most the hill may stand here beside / in front of a mouth: from the verge up at 42 deg
                    // (from the rail's back, flat for 4.3 m, where an approach guard rail stands)
                    bool gside = guard[atA ? 0 : 1, x < 0f ? 0 : 1];
                    float capB = gside ? c.y + 0.45f + Mathf.Max(0f, ax - (roadHalf + 5.6f)) * SlopeB
                                       : c.y + Mathf.Max(0f, ax - (roadHalf + 0.6f)) * SlopeB;
                    if (u[ri] <= 0f)
                    {
                        // behind the plane: over the masonry, beside it the hill at the wall's end height falling away
                        need = ax <= wallX + 0.05f ? y0 + T3MasonryTop(ax, halfW) - 0.06f
                                                   : y0 + stepTop - 0.06f - (ax - wallX) * 0.25f;
                        // (2b) beside the wall's end the hillside comes from the verge at 42 deg, rising behind: the end
                        // of the wall stands out of it (the photographs), no rock cliff beside it
                        if (Tunnel3B && ax > wallX + 0.05f)
                        {
                            // falling away from the wall's end at 42 deg (beyond it the land is the hill: no shelf
                            // held up over a valley), and under the verge's slope
                            need -= (ax - wallX) * (SlopeB - 0.25f);
                            need = Mathf.Min(need, capB + Mathf.Max(0f, -u[ri]) * SlopeB);
                        }
                        if (ax < halfW + 1.5f) need = Mathf.Max(need, c.y + crownMin);
                    }
                    else
                    {
                        // in front: spilling over the wall's ends, a verge at the road edge
                        float dx = Mathf.Max(0f, wallX - ax), du = u[ri] * (Tunnel3B ? 1f : 1.35f);
                        float spill = y0 + (ax >= wallX ? stepTop - 0.06f - (ax - wallX) * 0.25f : stepTop - 0.06f) - Mathf.Sqrt(dx * dx + du * du) * (Tunnel3B ? 1f / SlopeB : 1f);
                        float road = c.y + (ax - (roadHalf + 1.0f)) * 2.2f;              // steep (rock) off the verge
                        float verge = c.y - 0.06f - Mathf.Max(0f, ax - (roadHalf + 2.5f)) * 1.0f;
                        need = u[ri] < 16f ? Mathf.Max(verge, Mathf.Min(spill, road)) : float.NegativeInfinity;
                    }
                    float y;
                    if (u[ri] <= 0f)
                    {
                        // behind and beside a mouth: the hillside laid back from the wall's top at about 41 degrees
                        // (a forest floor holds it), never over the land, never under the wall's top or the crown's cover
                        float depth = Mathf.Max(0f, -u[ri] - (ax <= wallX ? 1.25f : 0f));
                        float baseY = ax <= wallX ? y0 + T3MasonryTop(ax, halfW) - 0.06f : y0 + stepTop - 0.06f - (ax - wallX) * 0.25f;
                        if (Tunnel3B && ax > wallX + 0.05f) baseY = Mathf.Min(baseY, capB);
                        float undul = ((T2Value(new Vector3(p.x, 0f, p.z) / 5.5f) - 0.5f) * 1.2f + (T2Value(new Vector3(p.x, 3.7f, p.z) / 2.3f) - 0.5f) * 0.5f) * Mathf.Clamp01(depth / 2f);
                        float slope = baseY + depth * 0.88f + undul;
                        y = Mathf.Max(need, Mathf.Min(land, slope));
                    }
                    else
                    {
                        // in front: the land or the spill, but never over the road edge - from the verge the hill
                        // rises no faster than 2.2 : 1 (rock), so nothing hangs over the shoulder with air under it
                        float cap = Tunnel3B ? capB : c.y + Mathf.Max(0f, ax - (roadHalf + 0.6f)) * 2.2f;
                        y = Mathf.Max(c.y - 0.06f - Mathf.Max(0f, ax - (roadHalf + 2.5f)), Mathf.Min(Mathf.Max(land, need), cap));
                    }
                    v[ri * C + ci] = new Vector3(p.x, y, p.z);
                    own[ri * C + ci] = !onMesh || y > g + 0.02f;
                    vis[ri * C + ci] = onMesh ? Mathf.Max(g, y) : y;
                }
            }

            // the cells: not the road in front of a mouth, not across the wall's face, not deep under the ground
            bool Front(int ri) => u[ri] > 0f;
            var gT = new List<int>(); var rT = new List<int>();
            var cellOwn = new bool[(R_ - 1) * (C - 1)];
            float cosRock = Mathf.Cos(T3RockSlopeDeg * Mathf.Deg2Rad);
            var verts = new List<Vector3>(v);
            var uvs = new List<Vector2>(v.Length);
            foreach (var p in v) uvs.Add(new Vector2(p.x, p.z) / tile);
            var keepTris = new List<(Vector3 a, Vector3 b, Vector3 c, bool rock)>();
            var visTris = new List<(Vector3 a, Vector3 b, Vector3 c, bool rock)>();
            for (int ri = 0; ri + 1 < R_; ri++)
                for (int ci = 0; ci + 1 < C; ci++)
                {
                    float xa = xs[ci], xb = xs[ci + 1];
                    float axMax = Mathf.Max(Mathf.Abs(xa), Mathf.Abs(xb)), axMin = Mathf.Min(Mathf.Abs(xa), Mathf.Abs(xb));
                    bool f0 = Front(ri), f1 = Front(ri + 1);
                    if ((f0 || f1) && axMax <= roadHalf + 0.31f) continue;                      // the road
                    if (f0 != f1 && axMax <= wallX + 0.05f) continue;                          // across the wall's face
                    if (f0 != f1 && Mathf.Abs(u[ri]) > 3f && Mathf.Abs(u[ri + 1]) > 3f) continue;   // (the far ends of the two mouths' rows)
                    int a = ri * C + ci, b = a + 1, d = a + C, e = d + 1;
                    bool any = own[a] || own[b] || own[d] || own[e];
                    if (!any && false)
                    {
                        // a cell under the ground is kept only beside one that shows (the seam's overlap)
                        bool nb = false;
                        for (int dr = -1; dr <= 1 && !nb; dr++)
                            for (int dc = -1; dc <= 1 && !nb; dc++)
                            {
                                int r2 = ri + dr, c2 = ci + dc;
                                if (r2 < 0 || c2 < 0 || r2 + 1 >= R_ || c2 + 1 >= C) continue;
                                int q = r2 * C + c2;
                                nb = own[q] || own[q + 1] || own[q + C] || own[q + C + 1];
                            }
                        if (!nb) continue;
                    }
                    cellOwn[ri * (C - 1) + ci] = own[a] && own[b] && own[d] && own[e];
                    void Tri(int i0, int i1, int i2)
                    {
                        Vector3 nn = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]);
                        if (nn.y < 0f) { int tmp = i1; i1 = i2; i2 = tmp; nn = -nn; }
                        if (nn.sqrMagnitude < 1e-8f) return;
                        bool rock = nn.normalized.y < cosRock;
                        var list = rock ? rT : gT;
                        list.Add(i0); list.Add(i1); list.Add(i2);
                        // (2b) a near-vertical step in the surface (the plane's two rows beside a wall end, a fold) is
                        // drawn from both sides: seen from behind it showed the world through it
                        if (Tunnel3B && nn.normalized.y < 0.3f) { list.Add(i0); list.Add(i2); list.Add(i1); }
                        if (own[i0] || own[i1] || own[i2]) keepTris.Add((v[i0], v[i1], v[i2], rock));
                        visTris.Add((new Vector3(v[i0].x, vis[i0], v[i0].z), new Vector3(v[i1].x, vis[i1], v[i1].z), new Vector3(v[i2].x, vis[i2], v[i2].z), rock));
                    }
                    Tri(a, d, e); Tri(a, e, b);
                }
            // (2b) A FLOOR under the verge at each mouth, both sides, from the tarmac edge to past the wall's end,
            // 0.6 m behind the face to 1.5 m in front, 0.25 m under the road: whatever slit is left between the
            // verge, the face's foot and the bore's lip looks down onto ground, never through the world
            if (Tunnel3B)
                for (int mo = 0; mo < 2; mo++)
                    foreach (float sd in new[] { -1f, 1f })
                    {
                        float d0 = mo == 0 ? 0.6f : total - 0.6f, d1 = mo == 0 ? -1.5f : total + 1.5f;
                        Vector3 c0 = Frame(d0, out Vector3 r0, out _), c1 = Frame(d1, out Vector3 r1, out _);
                        float yF = (mo == 0 ? cA.y : cB.y) - 0.25f;
                        int b = verts.Count;
                        foreach (var (cc, rr) in new[] { (c0, r0), (c1, r1) })
                            foreach (float xx in new[] { roadHalf + 0.05f, wallX + 0.6f })
                            {
                                Vector3 pp = cc + rr * (sd * xx); pp.y = yF;
                                verts.Add(pp); uvs.Add(new Vector2(pp.x, pp.z) / tile);
                            }
                        foreach (var (i0, i1, i2) in new[] { (b, b + 1, b + 3), (b, b + 3, b + 2) })
                        {
                            Vector3 nn = Vector3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]);
                            if (nn.y >= 0f) { gT.Add(i0); gT.Add(i1); gT.Add(i2); } else { gT.Add(i0); gT.Add(i2); gT.Add(i1); }
                        }
                    }
            if (gT.Count + rT.Count == 0) return;
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(gT, 0); mesh.SetTriangles(rT, 1);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            SaveMesh(mesh, "TunnelHill" + no);
            var go = new GameObject("TunnelHill" + no);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { groundMat, rockMat };
            go.isStatic = true;
            t2Tris += (gT.Count + rT.Count) / 3; t3HillTris += gT.Count / 3; t3RockTris += rT.Count / 3;

            // ---- handed to the planting: the hill as a fill, the stone and the ledges kept clear ----
            var bmin = new Vector2(float.MaxValue, float.MaxValue); var bmax = new Vector2(float.MinValue, float.MinValue);
            foreach (var t in visTris)
                foreach (var p in new[] { t.a, t.b, t.c }) { bmin = Vector2.Min(bmin, new Vector2(p.x, p.z)); bmax = Vector2.Max(bmax, new Vector2(p.x, p.z)); }
            if (visTris.Count == 0) return;
            const float Cell = 1f;
            float ox = Mathf.Floor(bmin.x) - 1f, oz = Mathf.Floor(bmin.y) - 1f;
            int cols = Mathf.CeilToInt((bmax.x - ox) / Cell) + 2, rows = Mathf.CeilToInt((bmax.y - oz) / Cell) + 2;
            var fy = new float[(cols + 1) * (rows + 1)];
            var has = new bool[fy.Length];
            var rockAt = new bool[fy.Length];
            for (int k = 0; k < fy.Length; k++) fy[k] = float.NegativeInfinity;
            foreach (var t in visTris)
            {
                float x0 = Mathf.Min(t.a.x, Mathf.Min(t.b.x, t.c.x)), x1 = Mathf.Max(t.a.x, Mathf.Max(t.b.x, t.c.x));
                float z0 = Mathf.Min(t.a.z, Mathf.Min(t.b.z, t.c.z)), z1 = Mathf.Max(t.a.z, Mathf.Max(t.b.z, t.c.z));
                int c0 = Mathf.Max(0, Mathf.CeilToInt((x0 - ox) / Cell)), c1 = Mathf.Min(cols, Mathf.FloorToInt((x1 - ox) / Cell));
                int r0 = Mathf.Max(0, Mathf.CeilToInt((z0 - oz) / Cell)), r1 = Mathf.Min(rows, Mathf.FloorToInt((z1 - oz) / Cell));
                Vector2 A = new Vector2(t.a.x, t.a.z), B = new Vector2(t.b.x, t.b.z), Cc = new Vector2(t.c.x, t.c.z);
                float den = (B.y - Cc.y) * (A.x - Cc.x) + (Cc.x - B.x) * (A.y - Cc.y);
                if (Mathf.Abs(den) < 1e-6f) continue;
                for (int rr = r0; rr <= r1; rr++)
                    for (int cc = c0; cc <= c1; cc++)
                    {
                        Vector2 q = new Vector2(ox + cc * Cell, oz + rr * Cell);
                        float l1 = ((B.y - Cc.y) * (q.x - Cc.x) + (Cc.x - B.x) * (q.y - Cc.y)) / den;
                        float l2 = ((Cc.y - A.y) * (q.x - Cc.x) + (A.x - Cc.x) * (q.y - Cc.y)) / den;
                        float l3 = 1f - l1 - l2;
                        if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                        int k = rr * (cols + 1) + cc;
                        float y = l1 * t.a.y + l2 * t.b.y + l3 * t.c.y;
                        if (y > fy[k]) { fy[k] = y; has[k] = true; }
                        if (t.rock && (y > -1e5f)) rockAt[k] |= keepTris.Count > 0 && t.rock;
                    }
            }
            var fill = new FillGrid { ox = ox, oz = oz, cell = Cell, cols = cols, rows = rows, y = fy, drawn = new bool[cols * rows] };
            var keep = new T3Keep { ox = ox, oz = oz, cell = Cell, cols = cols, rows = rows, no = new bool[cols * rows] };
            for (int rr = 0; rr < rows; rr++)
                for (int cc = 0; cc < cols; cc++)
                {
                    int k = rr * (cols + 1) + cc;
                    bool all4 = has[k] && has[k + 1] && has[k + cols + 1] && has[k + cols + 2];
                    fill.drawn[rr * cols + cc] = all4;
                    if (!all4) { for (int m = 0; m < fy.Length && false; m++) { } }
                    bool rk = rockAt[k] || rockAt[k + 1] || rockAt[k + cols + 1] || rockAt[k + cols + 2];
                    keep.no[rr * cols + cc] = rk;
                }
            for (int k = 0; k < fy.Length; k++) if (!has[k]) fy[k] = -1e6f;
            // the stone itself and a strip in front of the wall's face, at each mouth
            foreach (var (pc, atA) in new[] { (cA, true), (cB, false) })
            {
                Vector3 c = Frame(atA ? 0f : total, out Vector3 r, out Vector3 fw);
                Vector3 outward = atA ? -fw : fw;
                for (int rr = 0; rr < rows; rr++)
                    for (int cc = 0; cc < cols; cc++)
                    {
                        Vector3 q = new Vector3(ox + (cc + 0.5f) * Cell, c.y, oz + (rr + 0.5f) * Cell) - c;
                        float x = Mathf.Abs(Vector3.Dot(q, r)), uu = Vector3.Dot(q, outward);
                        if (x <= wallX + 0.8f && uu > -1.3f && uu < 1.6f) keep.no[rr * cols + cc] = true;
                    }
            }
            t3Grounds.Add(fill);
            t3Keeps.Add(keep);

            // ---- mossy boulders and ledges on the hill round each mouth ----
            var rnd = new System.Random(733 + no * 17);
            var bv = new List<Vector3>(); var buv = new List<Vector2>(); var bc = new List<Color>(); var bt = new List<int>();
            foreach (bool atA in new[] { true, false })
            {
                Vector3 c = Frame(atA ? 0f : total, out Vector3 r, out Vector3 fw);
                Vector3 outward = atA ? -fw : fw;
                int placed = 0;
                for (int tries = 0; tries < 60 && placed < 9; tries++)
                {
                    float s = rnd.NextDouble() < 0.5 ? -1f : 1f;
                    bool above = tries % 3 == 0;
                    float x = above ? (float)(rnd.NextDouble() * 2 - 1) * (wallX - 1.5f) : s * (wallX + 0.8f + (float)rnd.NextDouble() * 7f);
                    float uu = above ? -2.0f - (float)rnd.NextDouble() * 5f : -4f + (float)rnd.NextDouble() * 7f;
                    Vector3 q = c + r * x + outward * uu;
                    if (Mathf.Abs(x) < roadHalf + 3.2f && uu > -1f) continue;
                    float y = StageFillYAt(fill, q.x, q.z);
                    if (float.IsNaN(y) || y < c.y + 1.4f) continue;
                    float size = 0.7f + (float)rnd.NextDouble() * 1.3f;
                    bool clash = false;
                    foreach (var o in t3Boulders) if ((new Vector2(o.x - q.x, o.z - q.z)).magnitude < o.w + size + 0.4f) clash = true;
                    if (clash) continue;
                    T3Boulder(bv, buv, bc, bt, new Vector3(q.x, y - size * 0.35f, q.z), size, r, outward, rnd);
                    t3Boulders.Add(new Vector4(q.x, y, q.z, size));
                    placed++; t3Boulder++;
                }
            }
            T2Mesh("TunnelBoulders" + no, bv, buv, bc, bt, boulderMat, parent, null);
        }

        static void TrimSolidsAtMouth(List<Vector3> pts, int i, float dir, float halfW, Transform parent)
        {
            Vector3 right = RightAt(pts, i);
            Vector3 fwd = Vector3.Cross(right, Vector3.up).normalized;
            Vector3 face = fwd * dir;                 // out of the mountain
            Vector3 c = pts[i];
            float wallX = T3WallX(halfW);
            int moved = 0;
            foreach (var mc in parent.root.GetComponentsInChildren<MeshCollider>(true))
            {
                if (mc == null || mc.sharedMesh == null || !mc.name.StartsWith("BankColl") || !mc.sharedMesh.isReadable) continue;
                var m = mc.sharedMesh;
                var vs = m.vertices;
                var xf = mc.transform.localToWorldMatrix; var inv = mc.transform.worldToLocalMatrix;
                bool any = false;
                for (int k = 0; k < vs.Length; k++)
                {
                    Vector3 w = xf.MultiplyPoint3x4(vs[k]);
                    Vector3 q = w - c;
                    float lat = Mathf.Abs(Vector3.Dot(q, right)), d = Vector3.Dot(q, face);
                    if (lat < halfW || lat > wallX + 6f || Mathf.Abs(q.y) > 20f) continue;
                    if (d >= 0f || d < -3f) continue;      // in front of the face already, or far into the hill
                    w += face * (-d);                      // back onto the face, where the drawn rock ends
                    vs[k] = inv.MultiplyPoint3x4(w); any = true; moved++;
                }
                if (!any) continue;
                m.vertices = vs; m.RecalculateBounds();
                mc.sharedMesh = null; mc.sharedMesh = m;
                EditorUtility.SetDirty(m);
            }
            if (moved > 0) Log($"Tunnel mouth: {moved} cut-solid vertices pulled back onto the portal's face.");
        }

        /// <summary>The fill's own height at (x, z), or NaN where it has none.</summary>
        static float StageFillYAt(FillGrid g, float x, float z)
        {
            float fx = (x - g.ox) / g.cell, fz = (z - g.oz) / g.cell;
            int c = Mathf.FloorToInt(fx), r = Mathf.FloorToInt(fz);
            if (c < 0 || r < 0 || c >= g.cols || r >= g.rows || !g.drawn[r * g.cols + c]) return float.NaN;
            float uu = fx - c, w = fz - r;
            int stride = g.cols + 1;
            float y00 = g.y[r * stride + c], y10 = g.y[r * stride + c + 1], y01 = g.y[(r + 1) * stride + c], y11 = g.y[(r + 1) * stride + c + 1];
            return Mathf.Lerp(Mathf.Lerp(y00, y10, uu), Mathf.Lerp(y01, y11, uu), w);
        }

        /// <summary>A boulder: a squashed, lumpy box (3 x 3 a face, every vertex pushed by a world noise), its own
        /// tint; the rock material (world projected) does its grain.</summary>
        static void T3Boulder(List<Vector3> v, List<Vector2> uv, List<Color> col, List<int> t, Vector3 at, float size, Vector3 r, Vector3 f, System.Random rnd)
        {
            Vector3 ex = r * size * (1.1f + 0.6f * (float)rnd.NextDouble()), ey = Vector3.up * size * (0.55f + 0.3f * (float)rnd.NextDouble()), ez = f * size * (0.9f + 0.5f * (float)rnd.NextDouble());
            float tint = 0.30f + 0.12f * (float)rnd.NextDouble();
            var cl = new Color(tint, tint * 1.02f, tint * 0.95f, 1f);
            const int N = 3;
            Vector3 Pt(Vector3 cube)
            {
                Vector3 s = cube.normalized * 0.75f + cube * 0.25f;   // rounded toward a ball
                Vector3 w = at + ex * s.x + ey * s.y + ez * s.z;
                float nz = T2Value(w / 0.9f) - 0.5f;
                return w + (w - at).normalized * nz * size * 0.35f;
            }
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var nrm in axes)
            {
                Vector3 a1 = Mathf.Abs(nrm.y) > 0.5f ? Vector3.right : Vector3.up, a2 = Vector3.Cross(nrm, a1);
                int b = v.Count;
                for (int yy = 0; yy <= N; yy++)
                    for (int xx = 0; xx <= N; xx++)
                    {
                        Vector3 cube = nrm + a1 * (xx * 2f / N - 1f) + a2 * (yy * 2f / N - 1f);
                        var p = Pt(cube);
                        v.Add(p); uv.Add(new Vector2(p.x + p.z, p.y) / 2f);
                        float moss = Mathf.Clamp01(cube.y);
                        col.Add(Color.Lerp(cl, new Color(tint * 0.75f, tint * 0.95f, tint * 0.5f, 1f), moss * 0.7f));
                    }
                for (int yy = 0; yy < N; yy++)
                    for (int xx = 0; xx < N; xx++)
                    {
                        int i0 = b + yy * (N + 1) + xx;
                        Vector3 cen = (v[i0] + v[i0 + N + 2]) * 0.5f;
                        QuadFacing(v, t, i0, i0 + 1, i0 + N + 2, i0 + N + 1, cen - at);
                    }
            }
        }

        // ------------------------------------------------------------------
        //  The sign
        // ------------------------------------------------------------------
        static string T3SignTexPath(string id) => Root + "/Art/Tunnel/tunnel_sign3_" + id.ToLowerInvariant() + ".png";

        /// <summary>The maroon rounded sign (white border, white serif name) on one wooden post, on the traffic's
        /// right <paramref name="before"/> m before the mouth, 2.5 m off the tarmac, standing on the land or the hill.</summary>
        static void BuildTunnelSign3(List<Vector3> pts, int portalIdx, int awayDir, Transform parent, int no, string texPath, PhysicsMaterial phys)
        {
            int n = pts.Count;
            if (!System.IO.File.Exists(texPath)) { Log("Tunnel sign: no texture " + texPath); return; }
            const float before = 6f;
            int i = WrapIdx(portalIdx + awayDir * Mathf.RoundToInt(before / Spacing), n);
            Vector3 c = pts[i];
            Vector3 right = RightAt(pts, i);
            Vector3 travel = Vector3.Cross(right, Vector3.up).normalized * (-awayDir);
            Vector3 trafficRight = Vector3.Cross(Vector3.up, travel).normalized;
            const float panelW = 1.5f, panelH = 0.9f, corner = 0.12f, under = 1.45f;
            Vector3 foot = c + trafficRight * (RoadWidth * 0.5f + 2.5f + 0.1f);
            foot.y = T3GroundAt(foot.x, foot.z, out float hy) ? hy : StageGroundHeightAt(foot.x, foot.z);
            Vector3 faceN = -travel;
            Vector3 across = Vector3.Cross(faceN, Vector3.up).normalized;
            var post = MakeMat(MeshPrefix + "TunnelSignPost3", GuardrailPostTexPath, affine: 0f, tint: new Color(0.62f, 0.52f, 0.42f));
            var panel = MakeMat(MeshPrefix + "TunnelSign3", texPath, affine: 0f);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            float postH = under + panelH - 0.08f;
            Block(v, uv, t, foot - across * 0.07f - faceN * 0.16f - Vector3.up * 0.4f, across * 0.14f, Vector3.up * (postH + 0.4f), faceN * 0.14f, foot, across, faceN);
            T2Mesh("TunnelSignPost" + no, v, uv, null, t, post, parent, null);
            // the panel: a rounded rectangle (corner arcs in the geometry), its face the texture, a board 3 cm thick
            var pv = new List<Vector3>(); var puv = new List<Vector2>(); var pt = new List<int>();
            Vector3 o = foot + Vector3.up * under;
            var outline = new List<Vector2>();
            for (int k = 0; k < 4; k++)
            {
                Vector2 cc = new Vector2(k == 0 || k == 3 ? panelW - corner : corner, k < 2 ? panelH - corner : corner);
                float a0 = k * Mathf.PI * 0.5f;
                for (int s = 0; s <= 4; s++) { float a = a0 + s * Mathf.PI / 8f; outline.Add(cc + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * corner); }
            }
            Vector3 W(Vector2 q, float d) => o + across * (q.x - panelW * 0.5f) + Vector3.up * q.y - faceN * d;
            int cf = pv.Count;
            pv.Add(W(new Vector2(panelW * 0.5f, panelH * 0.5f), 0f)); puv.Add(new Vector2(0.5f, 0.5f));
            foreach (var q in outline) { pv.Add(W(q, 0f)); puv.Add(new Vector2(q.x / panelW, q.y / panelH)); }
            int m = outline.Count;
            for (int k = 0; k < m; k++)
            {
                int a = cf + 1 + k, b = cf + 1 + (k + 1) % m;
                Vector3 nn = Vector3.Cross(pv[a] - pv[cf], pv[b] - pv[cf]);
                if (Vector3.Dot(nn, faceN) >= 0f) { pt.Add(cf); pt.Add(a); pt.Add(b); } else { pt.Add(cf); pt.Add(b); pt.Add(a); }
            }
            // back and rim in the board's colour (the texture's maroon margin)
            int cb = pv.Count;
            pv.Add(W(new Vector2(panelW * 0.5f, panelH * 0.5f), 0.03f)); puv.Add(new Vector2(0.005f, 0.5f));
            foreach (var q in outline) { pv.Add(W(q, 0.03f)); puv.Add(new Vector2(0.005f, 0.5f)); }
            for (int k = 0; k < m; k++)
            {
                int a = cb + 1 + k, b = cb + 1 + (k + 1) % m;
                Vector3 nn = Vector3.Cross(pv[a] - pv[cb], pv[b] - pv[cb]);
                if (Vector3.Dot(nn, -faceN) >= 0f) { pt.Add(cb); pt.Add(a); pt.Add(b); } else { pt.Add(cb); pt.Add(b); pt.Add(a); }
                int fa = cf + 1 + k, fb = cf + 1 + (k + 1) % m;
                int rb = pv.Count;
                pv.Add(pv[fa]); pv.Add(pv[fb]); pv.Add(pv[b]); pv.Add(pv[a]);
                for (int z = 0; z < 4; z++) puv.Add(new Vector2(0.005f, 0.5f));
                Vector2 mid = (outline[k] + outline[(k + 1) % m]) * 0.5f - new Vector2(panelW * 0.5f, panelH * 0.5f);
                QuadFacing(pv, pt, rb, rb + 1, rb + 2, rb + 3, across * mid.x + Vector3.up * mid.y);
            }
            T2Mesh("TunnelSignPanel" + no, pv, puv, null, pt, panel, parent, null);
            var colGo = new GameObject("SignPostColl");
            colGo.transform.SetParent(parent, false);
            colGo.transform.position = foot + Vector3.up * (postH * 0.5f) - faceN * 0.09f;
            colGo.transform.rotation = Quaternion.LookRotation(faceN, Vector3.up);
            var bx = colGo.AddComponent<BoxCollider>(); bx.size = new Vector3(0.16f, postH, 0.16f); bx.sharedMaterial = phys;
            colGo.layer = SolidLayer; colGo.isStatic = true;
        }

        /// <summary>Round 2's parts at one run of stations: the bore (round 1's, the rock rougher, the lined mouth
        /// short), the lamp row (round 1's, lit only with PSX_TUNNEL_LAMPS=1), the real portals, the hillside, the signs.</summary>
        static void BuildTunnel3Parts(List<Vector3> pts, int from, int stations, Transform parent, Material rockMat, PhysicsMaterial phys, int no, float halfW,
                                      Material lining)
        {
            int n = pts.Count;
            var stone = T2Mat("TunnelPortalStone3", StageRockTexPath, new Color(1.3f, 1.3f, 1.32f), true, new Vector4(1f / 6.2f, 1f / 18f, 0.6f, 1.6f));
            stone.SetFloat("_RockTop", 1f);
            EditorUtility.SetDirty(stone);
            var mortar = T2Mat("TunnelPortalMortar3", StageRockTexPath, new Color(0.17f, 0.17f, 0.16f), false, Vector4.zero, false);
            // the hill's rock (no vertex colour on the hill: no house tint) and the boulders' (their own tints)
            var hillRock = T2Mat("TunnelHillRock", StageRockTexPath, new Color(0.56f, 0.56f, 0.54f), true, new Vector4(1f / 7.5f, 1f / 23f, 0.9f, 0.5f), false);
            hillRock.SetFloat("_RockTop", 1f);
            EditorUtility.SetDirty(hillRock);
            var boulderRock = T2Mat("TunnelBoulderRock", StageRockTexPath, new Color(0.9f, 0.9f, 0.9f), true, new Vector4(1f / 7.5f, 1f / 23f, 0.9f, 0.7f), true);
            boulderRock.SetFloat("_RockTop", 1f);
            EditorUtility.SetDirty(boulderRock);
            BuildBore2(pts, from, stations, parent, rockMat, lining, no, halfW);
            BuildLampRow2(pts, from, stations, parent, no, halfW);
            int iA = WrapIdx(from, n), iB = WrapIdx(from + stations - 1, n);
            BuildPortal3(pts, iA, -1f, halfW, parent, no * 2, stone, mortar);
            BuildPortal3(pts, iB, 1f, halfW, parent, no * 2 + 1, stone, mortar);
            PortalColliders3(pts, iA, -1f, halfW, parent, phys);
            PortalColliders3(pts, iB, 1f, halfW, parent, phys);
            // (2b) THE CUTS' SOLIDS END WHERE THEIR ROCK IS DRAWN: a bank's collider ran 0.1 m past the portal's face
            // into the masonry, solid over nothing drawn (the self-test's ghost barriers at both mouths)
            if (Tunnel3B) { TrimSolidsAtMouth(pts, iA, -1f, halfW, parent); TrimSolidsAtMouth(pts, iB, 1f, halfW, parent); }
            var ground = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/" + MeshPrefix + "Ground.mat");
            // the hill's steep faces and its boulders in the road cuts' own rock (BuildStageBanks' material: the
            // neighbour's look and scale exactly)
            var bank = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/" + MeshPrefix + "Bank.mat");
            if (bank != null && bank.IsKeywordEnabled("PSX_ROCKFACE"))
            {
                // a copy of the cuts' rock with the top plane (the cuts' own material is left as it is)
                hillRock.CopyPropertiesFromMaterial(bank);
                hillRock.shaderKeywords = bank.shaderKeywords;
                hillRock.SetFloat("_RockTop", 1f);
                if (hillRock.HasProperty("_HouseTint")) hillRock.SetFloat("_HouseTint", 0f);
                // darker and less mossy than the cuts' faces: the dark wet ledges of the photographs
                hillRock.color = bank.color * 0.72f;
                var rp = bank.GetVector("_RockParams"); rp.w = Mathf.Min(rp.w, 0.45f); hillRock.SetVector("_RockParams", rp);
                EditorUtility.SetDirty(hillRock);
                boulderRock = hillRock;
            }
            if (ground != null) BuildHill3(pts, from, stations, parent, no, halfW, ground, hillRock, boulderRock);
            else Log("Tunnel hill: no ground material " + MeshPrefix + "Ground");
            string sign = T3SignTexPath(track.id);
            BuildTunnelSign3(pts, iA, -1, parent, no * 2, sign, phys);
            BuildTunnelSign3(pts, iB, 1, parent, no * 2 + 1, sign, phys);
            Log($"Tunnel {no} (TUNNEL3): hill {t3HillTris} ground + {t3RockTris} rock tris, {t3Boulder} boulders; the hill handed to the planting as a fill, the stone kept clear.");
        }
    }
}
