using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE RIBBON FOLLOWS A CURVE, NOT ITS CHORDS.
    ///
    /// The owner, 2026-09-28: "Nor should any sharp angles of road or road
    /// lines." Every ribbon a car drives on was laid one ring per 4 m waypoint
    /// with straight quads between, so on a bend every painted line and every
    /// edge was a polygon: a V corner at each ring. Chimney Rock's hairpins turn
    /// up to 37.8 degrees in one 4 m station (wp 1052), where the chord misses
    /// the arc by 0.33 m on the centre line and 0.48 m on the outer edge of the
    /// verge strip - 13-20 times the 2.5 cm gate, and visible from the driver's
    /// seat as a double yellow bending in a sharp V. Every stage had it, milder
    /// (Gillespie 26.7 deg, NC 226A 20.5, Blowing Rock 16.7).
    ///
    /// So between two waypoints the road, the kerb strip and the inside of the
    /// shoulder are laid on SLICES of a smooth curve instead of one chord:
    ///
    ///  - The centre is a Catmull-Rom through the waypoints, in plan (height
    ///    stays on the chord, as it always was), in its Hermite form with
    ///    finite-difference tangents over chord-length knots: the tangent at
    ///    waypoint i is along pts[i+1] - pts[i-1], the direction RightAt has
    ///    always used, so a station ring is bit-for-bit the ring it was and
    ///    everything else laid on the stations (walls, banks, posts, the
    ///    lattice, the audits' station walks) meets the ribbon exactly where it
    ///    did; and each gap scales that tangent by its own length, so a short
    ///    piece beside long ones (a route's last 0.44 m, a loop's closing gap)
    ///    does not overshoot. On even 4 m chords it is the uniform
    ///    Catmull-Rom exactly.
    ///  - The across direction is the chord's own, normalised: nlerp of the two
    ///    stations' RightAt. Any smooth unit interpolation keeps an offset line
    ///    G1 at a station (its derivative there is along the tangent), and this
    ///    one keeps a slice's rung where the chord's rung was, so nothing slides
    ///    along the road (at most 6 cm, Chimney Rock's start).
    ///  - Slices per gap: enough that the chord of a slice misses the curve by
    ///    RibbonSliceTargetM at the verge strip's outer edge (the sagitta goes as
    ///    1/k^2), capped at RibbonMaxSlices. Road, kerb and shoulder share them.
    ///  - The shoulder (RoadEdge) is moved with the kerb at the strip's outer
    ///    edge and eases back to its chord (smoothstep) across the next
    ///    RibbonFadeMinM or RibbonFadeFactor times the move, never past the end
    ///    of its surface: the toe, the tuck and the skirt, the lattice that was
    ///    solved under them and the apex pads stay where the plan put them.
    ///  - Between two WALLED rows (StageRowWalled) the shoulder moves whole,
    ///    out to the wall's contact line and its tuck and skirt with it, and
    ///    the wall - rail, stone, posts and collider - stands on the same
    ///    slices (CurveWallRing), so a guardrail follows the road round a
    ///    hairpin instead of turning a corner at every post (2026-09-29
    ///    review, Chimney Rock 715 and 1049). A gap into a buried terminal,
    ///    a deck or a tunnel portal keeps its chord.
    ///
    /// Only render and collider geometry change. Waypoint indices, the TrackPath
    /// and everything keyed to a station are exactly what they were. LaneAudit
    /// measures the result (chord sagitta of every painted line and edge against
    /// a curve through its own vertices, gate V = 2.5 cm).
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        /// <summary>How far one slice's chord may miss the curve at the verge
        /// strip's outer edge. 1 cm, under the 2.5 cm gate with room.</summary>
        internal const float RibbonSliceTargetM = 0.01f;
        internal const int RibbonMaxSlices = 12;
        /// <summary>The shoulder's ease back to its chord: at least this far,
        /// or this many times the kerb edge's move, past the strip.</summary>
        const float RibbonFadeMinM = 0.75f, RibbonFadeFactor = 3f;

        /// <summary>One ring of the road-family ribbons, station or slice.</summary>
        internal struct RibbonRing
        {
            /// <summary>The waypoint this ring stands at, or the one its gap
            /// starts from.</summary>
            public int station;
            /// <summary>The gap the quad after this ring belongs to (station i
            /// to i+1, the loop's last closing onto 0); -1 on a strip's last
            /// ring.</summary>
            public int gap;
            /// <summary>0 at a station, j/k on a slice.</summary>
            public float t;
            /// <summary>On the waypoint plane (no lift): the curve in plan, the
            /// chord's height.</summary>
            public Vector3 centre;
            /// <summary>Horizontal unit across the road.</summary>
            public Vector3 right;
            /// <summary>Metres along the road (the textures' v).</summary>
            public float dist;
        }

        /// <summary>Slices per gap (1 = the chord), this build's.</summary>
        static int[] ribbonSub;
        /// <summary>Per side (0 left, 1 right) and gap: the largest move of
        /// the kerb strip's outer edge off its chord.</summary>
        static float[][] ribbonKerbDisp;
        /// <summary>Every ring the road, the kerb strips and the lane audit
        /// lay, in order.</summary>
        static List<RibbonRing> ribbonRings;

        internal static int RibbonSlices(int gap) =>
            ribbonSub != null && gap >= 0 && gap < ribbonSub.Length ? ribbonSub[gap] : 1;

        /// <summary>A control point: the waypoint, wrapped on a loop, mirrored
        /// past either end of a strip (so the end's tangent is its chord's,
        /// which is RightAt's there).</summary>
        static Vector3 RibbonCtl(List<Vector3> pts, int i)
        {
            int n = pts.Count;
            if (Loop) return pts[((i % n) + n) % n];
            if (i < 0) return 2f * pts[0] - pts[1];
            if (i >= n) return 2f * pts[n - 1] - pts[n - 2];
            return pts[i];
        }

        static void RibbonEnds(int n, int gap, out int a, out int b)
        {
            a = Loop ? ((gap % n) + n) % n : gap;
            b = Loop ? (a + 1) % n : gap + 1;
        }

        /// <summary>The curve's frame at t along gap: centre (plan on the
        /// uniform Catmull-Rom, height on the chord) and the unit right.
        /// Exactly the station's own at t = 0 and t = 1.</summary>
        static void RibbonFrame(List<Vector3> pts, int gap, float t, out Vector3 centre, out Vector3 right)
        {
            int n = pts.Count;
            RibbonEnds(n, gap, out int a, out int b);
            Vector3 A = pts[a], B = pts[b];
            if (t <= 0f) { centre = A; right = RightAt(pts, a); return; }
            if (t >= 1f) { centre = B; right = RightAt(pts, b); return; }
            Vector3 p0 = RibbonCtl(pts, gap - 1), p3 = RibbonCtl(pts, gap + 2);
            // Relative to A (world coordinates reach kilometres; 5 x 3 km in
            // float is a millimetre).
            float x0 = p0.x - A.x, z0 = p0.z - A.z, x2 = B.x - A.x, z2 = B.z - A.z, x3 = p3.x - A.x, z3 = p3.z - A.z;
            // Chord lengths in plan: the gap before, this one, the one after.
            float d0 = Mathf.Sqrt(x0 * x0 + z0 * z0), d1 = Mathf.Sqrt(x2 * x2 + z2 * z2);
            float d2 = Mathf.Sqrt((x3 - x2) * (x3 - x2) + (z3 - z2) * (z3 - z2));
            // Hermite tangents, scaled to this gap: M_A along p2 - p0, M_B
            // along p3 - p1, each times d1 over the two chords either side.
            float ka = d1 / Mathf.Max(d0 + d1, 1e-4f), kb = d1 / Mathf.Max(d1 + d2, 1e-4f);
            float max = (x2 - x0) * ka, maz = (z2 - z0) * ka;
            float mbx = x3 * kb, mbz = z3 * kb;
            float t2 = t * t, t3 = t2 * t;
            float h10 = t3 - 2f * t2 + t, h01 = -2f * t3 + 3f * t2, h11 = t3 - t2;
            float x = h10 * max + h01 * x2 + h11 * mbx;
            float z = h10 * maz + h01 * z2 + h11 * mbz;
            centre = new Vector3(A.x + x, Mathf.Lerp(A.y, B.y, t), A.z + z);
            right = Vector3.Lerp(RightAt(pts, a), RightAt(pts, b), t);
            right.y = 0f;
            right = right.normalized;
        }

        /// <summary>How far the curve moves a point of the chord geometry at t
        /// along gap and eLat across the road (signed, + right of the
        /// centre): plan only. Zero at both stations.</summary>
        static Vector3 RibbonDisp(List<Vector3> pts, int gap, float t, float eLat)
        {
            int n = pts.Count;
            RibbonEnds(n, gap, out int a, out int b);
            RibbonFrame(pts, gap, t, out Vector3 c, out Vector3 r);
            Vector3 d = (c - Vector3.Lerp(pts[a], pts[b], t))
                      + (r - Vector3.Lerp(RightAt(pts, a), RightAt(pts, b), t)) * eLat;
            d.y = 0f;
            return d;
        }

        /// <summary>
        /// Decide the slices of every gap for the venue being built, and lay
        /// the rings. Called by BuildRoad before anything is emitted; the kerbs
        /// and the shoulders read the same plan.
        /// </summary>
        static void PlanRibbonSmoothing(List<Vector3> pts)
        {
            int n = pts.Count;
            int gaps = n < 2 ? 0 : (Loop ? n : n - 1);
            ribbonSub = new int[gaps];
            ribbonKerbDisp = new[] { new float[gaps], new float[gaps] };
            float half = RoadWidth * 0.5f, reach = half + KerbWidth;
            float[] across = { -reach, -half, 0f, half, reach };
            int smoothed = 0, extra = 0, worstGap = -1, worstK = 1;
            float worst = 0f, worstMove = 0f;

            for (int g = 0; g < gaps; g++)
            {
                ribbonSub[g] = 1;
                if (n < 3) continue;

                RibbonEnds(n, g, out int a, out int b);
                Vector3 A = pts[a], B = pts[b], Ra = RightAt(pts, a), Rb = RightAt(pts, b);
                float dev = 0f;
                for (int q = 1; q <= 3; q++)
                {
                    float t = q * 0.25f;
                    RibbonFrame(pts, g, t, out Vector3 c, out Vector3 r);
                    foreach (float e in across)
                    {
                        Vector3 s = c + r * e, ea = A + Ra * e, eb = B + Rb * e;
                        float dx = eb.x - ea.x, dz = eb.z - ea.z, len = Mathf.Sqrt(dx * dx + dz * dz);
                        if (len < 1e-4f) continue;
                        dev = Mathf.Max(dev, Mathf.Abs((s.x - ea.x) * dz - (s.z - ea.z) * dx) / len);
                    }
                    for (int s2 = 0; s2 < 2; s2++)
                    {
                        float move = RibbonDisp(pts, g, t, (s2 == 0 ? -1f : 1f) * reach).magnitude;
                        ribbonKerbDisp[s2][g] = Mathf.Max(ribbonKerbDisp[s2][g], move);
                        worstMove = Mathf.Max(worstMove, move);
                    }
                }
                int k = dev > RibbonSliceTargetM
                    ? Mathf.Min(RibbonMaxSlices, Mathf.CeilToInt(Mathf.Sqrt(dev / RibbonSliceTargetM)))
                    : 1;
                ribbonSub[g] = k;
                if (k > 1) { smoothed++; extra += k - 1; }
                if (dev > worst) { worst = dev; worstGap = g; worstK = k; }
            }

            ribbonRings = new List<RibbonRing>(n + extra + 1);
            int last = n < 2 ? n - 1 : (Loop ? n : n - 1);
            float dist = 0f;
            for (int i = 0; i <= last; i++)
            {
                int idx = Loop ? i % n : i;
                ribbonRings.Add(new RibbonRing
                {
                    station = idx, gap = i < last ? i : -1, t = 0f,
                    centre = pts[idx], right = RightAt(pts, idx), dist = dist,
                });
                if (i < last)
                {
                    int k = RibbonSlices(i);
                    for (int j = 1; j < k; j++)
                    {
                        float t = j / (float)k;
                        RibbonFrame(pts, i, t, out Vector3 c, out Vector3 r);
                        ribbonRings.Add(new RibbonRing
                        {
                            station = idx, gap = i, t = t, centre = c, right = r, dist = dist + Spacing * t,
                        });
                    }
                }
                dist += Spacing;
            }
            Log($"Ribbon curve: {smoothed} of {gaps} gaps laid on a curve ({extra} slice rings, {ribbonRings.Count} rings in all); " +
                $"worst chord {worst:0.000} m off the curve at the strip's edge (wp {worstGap}, {worstK} slices, " +
                $"{worst / (worstK * worstK):0.0000} m left); kerb edge moved up to {worstMove:0.000} m.");
        }

        /// <summary>The kerb's lift at a ring: the station's, or the chord's
        /// lerp between two (a street curb's driveway ramp).</summary>
        static float RibbonKerbLift(in RibbonRing g, int n, float side)
        {
            if (g.t <= 0f) return KerbLiftAt(g.station, n, side);
            RibbonEnds(n, g.gap, out int a, out int b);
            return Mathf.Lerp(KerbLiftAt(a, n, side), KerbLiftAt(b, n, side), g.t);
        }

        /// <summary>ZipShoulder's own order of steps (true = advance row A),
        /// so a sliced zipper lays its bands along the same rungs.</summary>
        static List<bool> ZipSteps(float[] ea, float[] eb)
        {
            var steps = new List<bool>(ea.Length + eb.Length);
            int i = 0, j = 0, na = ea.Length, nb = eb.Length;
            float endK = Mathf.Max(ea[Mathf.Max(na - 3, 0)], eb[Mathf.Max(nb - 3, 0)]);
            float KeyA(int k) => k < na - 2 ? ea[k] : endK + (ea[k] - ea[Mathf.Max(na - 3, 0)]);
            float KeyB(int k) => k < nb - 2 ? eb[k] : endK + (eb[k] - eb[Mathf.Max(nb - 3, 0)]);
            while (i < na - 1 || j < nb - 1)
            {
                bool stepA = j >= nb - 1 || (i < na - 1 && KeyA(i + 1) <= KeyB(j + 1));
                steps.Add(stepA);
                if (stepA) i++; else j++;
            }
            return steps;
        }

        /// <summary>
        /// ZipShoulder between two station rows, laid on the gap's slices. The
        /// zipper's rungs (row A point to row B point, in ZipShoulder's order)
        /// are cut at t = j/k; the points on a rung whose ends reach into the
        /// fade are moved by the curve's displacement, eased to nothing at the
        /// fade's end. Rungs wholly past the fade are not cut (their triangles
        /// are ZipShoulder's own), and the one triangle between the last cut
        /// rung and the first uncut one is fanned from its far vertex, so the
        /// strip stays closed. New vertices go to <paramref name="extraV"/> and
        /// are referenced as -(index + 1) until the chunk appends them.
        /// </summary>
        static int SlicedShoulderZip(List<Vector3> pts, int gap, int k, float side, List<int> tris,
                                     List<Vector3> extraV, List<Vector2> extraUV, float uox, float uoz, float tile,
                                     int a0, Vector3[] pa, float[] ea, int b0, Vector3[] pb, float[] eb,
                                     bool rigid = false)
        {
            // RIGID: both rows are walled shoulders (StageRowWalled), and the
            // wall between them stands on the same slices of the curve
            // (CurveWallRing). The whole row moves with the curve - the flat
            // shoulder out to the wall's contact line, its tuck and its skirt -
            // so the collider's face and the shoulder's end meet all the way
            // round the bend, as they do at a station.
            var steps = ZipSteps(ea, eb);
            int R = steps.Count + 1;
            var ri = new int[R];
            var rj = new int[R];
            for (int r = 1; r < R; r++)
            {
                ri[r] = ri[r - 1] + (steps[r - 1] ? 1 : 0);
                rj[r] = rj[r - 1] + (steps[r - 1] ? 0 : 1);
            }

            float half = RoadWidth * 0.5f;
            float move = ribbonKerbDisp != null && gap >= 0 && gap < ribbonKerbDisp[0].Length
                ? ribbonKerbDisp[side < 0f ? 0 : 1][gap] : 0f;
            float fadeLen = Mathf.Max(RibbonFadeMinM, RibbonFadeFactor * move);
            float fs = KerbWidth;
            float SurfEnd(float[] e) => e[Mathf.Max(e.Length - 3, 0)];
            float feA = Mathf.Max(Mathf.Min(fs + fadeLen, SurfEnd(ea)), fs + 0.1f);
            float feB = Mathf.Max(Mathf.Min(fs + fadeLen, SurfEnd(eb)), fs + 0.1f);
            bool Cut(int r) => rigid || !(ea[ri[r]] >= feA && eb[rj[r]] >= feB);

            // V(r, j): the rung's point at j/k, as a vertex index (or code).
            var mids = new int[R][];
            int added = 0;
            for (int r = 0; r < R; r++)
            {
                if (!Cut(r)) break;
                mids[r] = new int[k + 1];
                mids[r][0] = a0 + ri[r];
                mids[r][k] = b0 + rj[r];
                for (int j = 1; j < k; j++)
                {
                    float t = j / (float)k;
                    Vector3 p = Vector3.Lerp(pa[ri[r]], pb[rj[r]], t);
                    float e = Mathf.Lerp(ea[ri[r]], eb[rj[r]], t);
                    float fe = Mathf.Lerp(feA, feB, t);
                    float w = rigid ? 1f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fs, fe, e));
                    if (w > 0f) p += RibbonDisp(pts, gap, t, side * (half + e)) * w;
                    extraV.Add(p);
                    extraUV.Add(new Vector2((p.x - uox) / tile, (p.z - uoz) / tile));
                    mids[r][j] = -extraV.Count;
                    added++;
                }
            }

            void Tri(int p, int q, int s)
            {
                if (p == q || q == s || p == s) return;
                if (side < 0f) { tris.Add(p); tris.Add(q); tris.Add(s); }
                else { tris.Add(p); tris.Add(s); tris.Add(q); }
            }

            for (int r = 0; r + 1 < R; r++)
            {
                int r2 = r + 1;
                if (mids[r] != null && mids[r2] != null)
                {
                    for (int j = 0; j < k; j++)
                    {
                        int rb = mids[r][j], r2b = mids[r2][j], rt = mids[r][j + 1], r2t = mids[r2][j + 1];
                        if (rb != r2b) Tri(rb, r2b, r2t);
                        if (rt != r2t) Tri(rb, r2t, rt);
                    }
                }
                else if (mids[r] != null)
                {
                    // The last cut rung to the first uncut one: ZipShoulder's
                    // triangle, fanned from the vertex it adds.
                    int apex = steps[r] ? a0 + ri[r2] : b0 + rj[r2];
                    for (int j = 0; j < k; j++) Tri(mids[r][j], apex, mids[r][j + 1]);
                }
                else
                {
                    int ai = a0 + ri[r], bj = b0 + rj[r];
                    if (steps[r]) Tri(ai, a0 + ri[r2], bj);
                    else Tri(ai, b0 + rj[r2], bj);
                }
            }
            return added;
        }
    }
}
