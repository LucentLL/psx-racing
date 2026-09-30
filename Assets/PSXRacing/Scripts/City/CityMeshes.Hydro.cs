using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        // ------------------------------------------------------------------
        //  CULVERTS (WP-25). Where a ravine goes under a road on its
        //  embankment (CityCulverts finds where, and each end's wall), the
        //  end is a concrete HEADWALL across the channel at the toe of the
        //  fill, the pipe's mouth in it, WING WALLS back along both sides
        //  and grass BACKFILL between them up to the wall's coping: the
        //  embankment the road already stands on ends in a wall instead of
        //  running on into the channel. Nothing here touches the road, its
        //  verge or the ground function: every piece stands past the clear
        //  zone (CityCulverts.ClearOfRoads), the lattice is left as it is,
        //  and the backfill lies over it until the rising fill swallows it.
        //
        //  Draws: the wall and wings are the barriers mesh (concrete, the
        //  Solid layer: a car that leaves the road down the fill meets
        //  them); the backfill is ground (it collides as ground); the pipe's
        //  inside is the lamp posts' dark pack metal (a corrugated pipe, and
        //  the dark a mouth needs), render-only. No new material, so a tile
        //  that already has barriers and lamps draws nothing more.
        // ------------------------------------------------------------------
        public struct CulvertEnd
        {
            public Vector2 at, outward;
            public float groundY, wallW, wallH, pipeD, backfillM, pastPave;
            public int edge;
            /// <summary>The rising fill met the backfill (false: it fell
            /// back to the ground behind a free-standing wall).</summary>
            public bool backfillMet;
        }

        /// <summary>The backfill is flat at the coping until the fill behind
        /// rises through it; if that has not happened this far back, it is a
        /// berm instead, falling at 1V:2H (<see cref="BackfillFall"/>) from
        /// 1 m back until it meets the ground (a wall on a gentle run-out
        /// stood at the end of a 10 m concrete trough), and never runs past
        /// <see cref="BackfillMaxM"/>.</summary>
        const float BackfillFlatM = 6f, BackfillFall = 0.5f, BackfillMaxM = 14f, BackfillStepM = 1f;
        /// <summary>The wall's coping stands this far over the backfill; the
        /// wing walls are this thick.</summary>
        const float CopingM = 0.12f, WingThickM = 0.3f;
        /// <summary>How far the pipe runs back into the fill before its dark
        /// end cap.</summary>
        const float PipeDepthM = 1.4f;
        static readonly List<float> backfillScratch = new List<float>(32);

        /// <summary>WP-25 off: no culvert ends and no creek banks (the A/B
        /// instruments set it: CityHydroShots' "before", CityBudgetProbe's
        /// draws without them). Never set by the game.</summary>
        public static bool HydroOff;

        static void BuildCulverts(CityMap map, Trims trims, TileMeshes tm, Vector2 min, Vector2 max)
        {
            if (HydroOff) return;
            foreach (var c in CityCulverts.ForTile(map, trims, Mathf.FloorToInt(min.x / TileSize + 0.5f), Mathf.FloorToInt(min.y / TileSize + 0.5f)))
            {
                if (c.skip != null) continue;
                if (c.hasLo && InRect(c.lo.at, min, max)) EmitCulvertEnd(map, tm, c.lo);
                if (c.hasHi && InRect(c.hi.at, min, max)) EmitCulvertEnd(map, tm, c.hi);
            }
        }

        static bool InRect(Vector2 p, Vector2 min, Vector2 max) => p.x >= min.x && p.x < max.x && p.y >= min.y && p.y < max.y;

        static void EmitCulvertEnd(CityMap map, TileMeshes tm, CityCulverts.End end)
        {
            Vector2 o = end.outward, r = new Vector2(o.y, -o.x);
            float W = end.wallW, H = end.wallH, D = end.pipeD, T = CityCulverts.WallThickM;
            float hw = W * 0.5f;
            Vector2 at = end.at;
            var org = tm.origin;
            Vector3 P(Vector2 plan, float y) => new Vector3(plan.x - org.x, y, plan.y - org.z);
            float G(Vector2 p) => LatticeY(map, p.x, p.y);

            // the pipe's invert on the drawn ground at the face; the wall's
            // foot under the lowest ground beneath any corner of it
            float inv = G(at) + 0.03f;
            float foot = inv;
            foreach (var q in new[] { at + r * hw, at - r * hw, at + r * hw - o * T, at - r * hw - o * T })
                foot = Mathf.Min(foot, G(q));
            foot -= 0.5f;
            float top = inv + H;
            float fill = top - CopingM;

            // ---- the backfill's profile, back from the wall's back face
            backfillScratch.Clear();
            float wingIn = hw - WingThickM;
            bool met = false, fell = false;
            for (float x = 0f; x <= BackfillMaxM + 1e-3f; x += BackfillStepM)
            {
                float h = x <= BackfillFlatM ? fill : fill - (x - 1f) * BackfillFall;
                if (x > BackfillFlatM && backfillScratch.Count > 0)
                {
                    // past the flat reach without meeting the fill: re-profile
                    // as a fall from 1 m back (the ground behind is low)
                    fell = true;
                    backfillScratch.Clear();
                    for (float x2 = 0f; x2 <= BackfillMaxM + 1e-3f; x2 += BackfillStepM)
                    {
                        float h2 = x2 <= 1f ? fill : fill - (x2 - 1f) * BackfillFall;
                        backfillScratch.Add(h2);
                        if (x2 > 0f && LowestAcross(map, at - o * (T + x2), r, wingIn) >= h2) { met = true; break; }
                    }
                    break;
                }
                backfillScratch.Add(h);
                if (x > 0f && LowestAcross(map, at - o * (T + x), r, wingIn) >= h) { met = true; break; }
            }
            int n = backfillScratch.Count;
            if (!met) backfillScratch[n - 1] = Mathf.Min(backfillScratch[n - 1], LowestAcross(map, at - o * (T + (n - 1) * BackfillStepM), r, wingIn) + 0.02f);
            float depth = (n - 1) * BackfillStepM;

            var con = barrierBucket;
            Vector3 o3 = new Vector3(o.x, 0f, o.y), r3 = new Vector3(r.x, 0f, r.y);
            const float UvM = 3f;

            // ---- the headwall's front face, round the pipe's mouth
            float cy = inv + D * 0.5f, R = D * 0.5f;
            var oct = new Vector3[8];
            var octUv = new Vector2[8];
            for (int k = 0; k < 8; k++)
            {
                float a = (22.5f + 45f * k) * Mathf.Deg2Rad;
                float lx = Mathf.Cos(a) * R, ly = cy + Mathf.Sin(a) * R;
                oct[k] = P(at + r * lx, ly);
                octUv[k] = new Vector2(lx / UvM, ly / UvM);
            }
            Vector3 TR = P(at + r * hw, top), TL = P(at - r * hw, top), BL = P(at - r * hw, foot), BR = P(at + r * hw, foot);
            Vector2 uTR = new Vector2(hw / UvM, top / UvM), uTL = new Vector2(-hw / UvM, top / UvM),
                    uBL = new Vector2(-hw / UvM, foot / UvM), uBR = new Vector2(hw / UvM, foot / UvM);
            TriFacing(con, oct[0], oct[1], TR, o3, octUv[0], octUv[1], uTR);
            con.Face(oct[1], oct[2], TL, TR, o3, octUv[1], octUv[2], uTL, uTR);
            TriFacing(con, oct[2], oct[3], TL, o3, octUv[2], octUv[3], uTL);
            con.Face(oct[3], oct[4], BL, TL, o3, octUv[3], octUv[4], uBL, uTL);
            TriFacing(con, oct[4], oct[5], BL, o3, octUv[4], octUv[5], uBL);
            con.Face(oct[5], oct[6], BR, BL, o3, octUv[5], octUv[6], uBR, uBL);
            TriFacing(con, oct[6], oct[7], BR, o3, octUv[6], octUv[7], uBR);
            con.Face(oct[7], oct[0], TR, BR, o3, octUv[7], octUv[0], uTR, uBR);

            // ---- the rest of the wall: back, top, ends
            Vector2 back = at - o * T;
            Vector3 bTR = P(back + r * hw, top), bTL = P(back - r * hw, top), bBL = P(back - r * hw, foot), bBR = P(back + r * hw, foot);
            con.Face(bTL, bTR, bBR, bBL, -o3, uTL, uTR, uBR, uBL);
            con.Face(TL, TR, bTR, bTL, Vector3.up, new Vector2(-hw / UvM, 0f), new Vector2(hw / UvM, 0f), new Vector2(hw / UvM, T / UvM), new Vector2(-hw / UvM, T / UvM));
            con.Face(TR, bTR, bBR, BR, r3, new Vector2(0f, top / UvM), new Vector2(T / UvM, top / UvM), new Vector2(T / UvM, foot / UvM), new Vector2(0f, foot / UvM));
            con.Face(TL, bTL, bBL, BL, -r3, new Vector2(0f, top / UvM), new Vector2(T / UvM, top / UvM), new Vector2(T / UvM, foot / UvM), new Vector2(0f, foot / UvM));

            // ---- the pipe: its inside back into the fill, and a dark end
            var pipe = lampBucket;
            Vector3 axis = P(at, cy);
            var octBack = new Vector3[8];
            for (int k = 0; k < 8; k++) octBack[k] = oct[k] - o3 * PipeDepthM;
            Vector3 axisBack = axis - o3 * PipeDepthM;
            for (int k = 0; k < 8; k++)
            {
                int k1 = (k + 1) & 7;
                Vector3 mid = (oct[k] + oct[k1]) * 0.5f;
                Vector3 inward = axis - mid;
                pipe.Face(oct[k], oct[k1], octBack[k1], octBack[k], inward,
                          new Vector2(k / 8f, 0f), new Vector2((k + 1) / 8f, 0f), new Vector2((k + 1) / 8f, PipeDepthM / 2f), new Vector2(k / 8f, PipeDepthM / 2f));
                TriFacing(pipe, octBack[k], octBack[k1], axisBack, o3, new Vector2(0f, 0f), new Vector2(0.25f, 0f), new Vector2(0.12f, 0.25f));
            }

            // ---- wing walls and the backfill between them
            var gb = GroundBucket(false);
            for (int i = 0; i + 1 < n; i++)
            {
                float x0 = i * BackfillStepM, x1 = (i + 1) * BackfillStepM;
                float h0 = backfillScratch[i], h1 = backfillScratch[i + 1];
                Vector2 c0 = back - o * x0, c1 = back - o * x1;
                // the backfill, flat across between the wings' inner faces
                Vector2 l0 = c0 - r * wingIn, l1 = c1 - r * wingIn, q0 = c0 + r * wingIn, q1 = c1 + r * wingIn;
                gb.Up(P(l0, h0), P(l1, h1), P(q1, h1), P(q0, h0),
                      GroundUV(false, l0.x, l0.y), GroundUV(false, l1.x, l1.y), GroundUV(false, q1.x, q1.y), GroundUV(false, q0.x, q0.y));
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 outN = r * side;
                    Vector2 in0 = c0 + outN * wingIn, in1 = c1 + outN * wingIn;
                    Vector2 ou0 = c0 + outN * hw, ou1 = c1 + outN * hw;
                    float f0 = Mathf.Min(G(ou0), h0) - 0.5f, f1 = Mathf.Min(G(ou1), h1) - 0.5f;
                    float t0 = h0 + CopingM, t1 = h1 + CopingM;
                    // outer face, inner face (its strip over the backfill), top
                    con.WallSloped(new Vector3(ou0.x - org.x, 0f, ou0.y - org.z), new Vector3(ou1.x - org.x, 0f, ou1.y - org.z),
                                   f0, t0, f1, t1, outN, x0 / UvM, x1 / UvM, f0 / UvM, t0 / UvM);
                    con.WallSloped(new Vector3(in0.x - org.x, 0f, in0.y - org.z), new Vector3(in1.x - org.x, 0f, in1.y - org.z),
                                   h0 - 0.3f, t0, h1 - 0.3f, t1, -outN, x0 / UvM, x1 / UvM, (h0 - 0.3f) / UvM, t0 / UvM);
                    con.Up(P(in0, t0), P(in1, t1), P(ou1, t1), P(ou0, t0),
                           new Vector2(x0 / UvM, 0f), new Vector2(x1 / UvM, 0f), new Vector2(x1 / UvM, WingThickM / UvM), new Vector2(x0 / UvM, WingThickM / UvM));
                }
            }
            // each wing's far end
            if (n >= 1)
            {
                float hEnd = backfillScratch[n - 1];
                Vector2 cEnd = back - o * depth;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 outN = r * side;
                    Vector2 inE = cEnd + outN * wingIn, ouE = cEnd + outN * hw;
                    float fE = Mathf.Min(G(ouE), G(inE)) - 0.5f, tE = hEnd + CopingM;
                    con.Face(P(inE, tE), P(ouE, tE), P(ouE, fE), P(inE, fE), -o3,
                             new Vector2(0f, tE / UvM), new Vector2(WingThickM / UvM, tE / UvM), new Vector2(WingThickM / UvM, fE / UvM), new Vector2(0f, fE / UvM));
                }
            }

            tm.culvertEnds.Add(new CulvertEnd
            {
                at = at, outward = o, groundY = end.groundY, wallW = W, wallH = H, pipeD = D,
                backfillM = depth, pastPave = end.pastPave, edge = end.edge, backfillMet = met && !fell,
            });
        }

        /// <summary>The lowest drawn ground across the backfill at a station
        /// (its middle and both wings' inner faces).</summary>
        static float LowestAcross(CityMap map, Vector2 c, Vector2 r, float half) =>
            Mathf.Min(LatticeY(map, c.x, c.y), Mathf.Min(LatticeY(map, c.x + r.x * half, c.y + r.y * half), LatticeY(map, c.x - r.x * half, c.y - r.y * half)));

        // ------------------------------------------------------------------
        //  CREEK BANKS (WP-25). R1's creeks are a water sheet in a carved
        //  channel, and the channel's banks were the same grass as a lawn:
        //  from the road the water read as a silver ribbon laid in a field.
        //  A band of the pack's red Piedmont clay now runs along both banks,
        //  from under the water's edge (the lattice meets the water between
        //  its 8 m vertices, so the edge wanders; the band starts a metre
        //  under the floor's edge so it always does) up the bank, draped on
        //  the lattice a few centimetres proud. Render-only, its own draw
        //  (the kit's bank material) on a tile with a creek; never on or
        //  beside a grounded road's pavement.
        // ------------------------------------------------------------------
        static readonly Bucket bankBucket = new Bucket();
        /// <summary>The band's columns, metres past the creek's flat floor.</summary>
        static readonly float[] BankCols = { -1f, 1.75f, 4.5f };
        const float BankLiftM = 0.06f, BankTexM = 12f;
        /// <summary>No bank quad within this of a grounded road's pavement.</summary>
        const float BankRoadClearM = 3f;
        static readonly HashSet<int> bankRoadScratch = new HashSet<int>();

        static void EmitCreekBanks(CityMap map, TileMeshes tm, Vector2 p0, Vector2 p1, Vector2 right0, Vector2 right1, float flat)
        {
            if (HydroOff) return;
            var o = tm.origin;
            for (int side = -1; side <= 1; side += 2)
                for (int c = 0; c + 1 < BankCols.Length; c++)
                {
                    float d0 = (flat + BankCols[c]) * side, d1 = (flat + BankCols[c + 1]) * side;
                    Vector2 a0 = p0 + right0 * d0, a1 = p1 + right1 * d0, b0 = p0 + right0 * d1, b1 = p1 + right1 * d1;
                    Vector2 mid = (a0 + a1 + b0 + b1) * 0.25f;
                    float half = 0.5f * Mathf.Max(Vector2.Distance(a0, b1), Vector2.Distance(a1, b0));
                    if (NearGroundedPavement(map, mid, BankRoadClearM + half)) continue;
                    Drape(map, bankBucket, o, a0, a1, b1, b0, BankLiftM, BankTexM);
                }
        }

        static readonly List<Vector2> drapeA = new List<Vector2>(16), drapeB = new List<Vector2>(16);
        static readonly Vector2[] drapeTri = new Vector2[3];

        /// <summary>A convex plan quad laid ON the lattice: cut against every
        /// triangle of the 8 m cells it covers (BuildGround's diagonal, (0,0)
        /// to (1,1)) and each piece set on that triangle's own plane, lifted
        /// <paramref name="lift"/>. A quad whose corners were merely set on
        /// the lattice bridged its folds, and the grass came through it in
        /// patches. World-planar UVs, <paramref name="texM"/> a repeat.</summary>
        static void Drape(CityMap map, Bucket bk, Vector3 origin, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float lift, float texM)
        {
            float minX = Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x)), maxX = Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x));
            float minZ = Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y)), maxZ = Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y));
            int ix0 = Mathf.FloorToInt(minX / LatticeCell), ix1 = Mathf.FloorToInt(maxX / LatticeCell);
            int iz0 = Mathf.FloorToInt(minZ / LatticeCell), iz1 = Mathf.FloorToInt(maxZ / LatticeCell);
            for (int iz = iz0; iz <= iz1; iz++)
                for (int ix = ix0; ix <= ix1; ix++)
                {
                    float x0 = ix * LatticeCell, z0 = iz * LatticeCell, x1 = x0 + LatticeCell, z1 = z0 + LatticeCell;
                    for (int half = 0; half < 2; half++)
                    {
                        // tx >= tz: (0,0) (1,0) (1,1); else (0,0) (1,1) (0,1) - both anticlockwise
                        drapeTri[0] = new Vector2(x0, z0);
                        drapeTri[1] = half == 0 ? new Vector2(x1, z0) : new Vector2(x1, z1);
                        drapeTri[2] = half == 0 ? new Vector2(x1, z1) : new Vector2(x0, z1);
                        drapeA.Clear(); drapeA.Add(a); drapeA.Add(b); drapeA.Add(c); drapeA.Add(d);
                        for (int k = 0; k < 3 && drapeA.Count >= 3; k++)
                        {
                            Vector2 p = drapeTri[k], q = drapeTri[(k + 1) % 3];
                            drapeB.Clear();
                            for (int i = 0; i < drapeA.Count; i++)
                            {
                                Vector2 cur = drapeA[i], prev = drapeA[(i + drapeA.Count - 1) % drapeA.Count];
                                float sc = Cross2(q - p, cur - p), sp = Cross2(q - p, prev - p);
                                if (sc >= 0f)
                                {
                                    if (sp < 0f) drapeB.Add(Vector2.Lerp(prev, cur, sp / (sp - sc)));
                                    drapeB.Add(cur);
                                }
                                else if (sp >= 0f) drapeB.Add(Vector2.Lerp(prev, cur, sp / (sp - sc)));
                            }
                            drapeA.Clear(); drapeA.AddRange(drapeB);
                        }
                        if (drapeA.Count < 3) continue;
                        Vector3 V(Vector2 pt) => new Vector3(pt.x - origin.x, LatticeY(map, pt.x, pt.y) + lift, pt.y - origin.z);
                        Vector2 U(Vector2 pt) => new Vector2(pt.x / texM, pt.y / texM);
                        var v0 = V(drapeA[0]); var u0 = U(drapeA[0]);
                        for (int i = 1; i + 1 < drapeA.Count; i++)
                        {
                            if (Mathf.Abs(Cross2(drapeA[i] - drapeA[0], drapeA[i + 1] - drapeA[0])) < 1e-4f) continue;
                            TriFacing(bk, v0, V(drapeA[i]), V(drapeA[i + 1]), Vector3.up, u0, U(drapeA[i]), U(drapeA[i + 1]));
                        }
                    }
                }
        }


        /// <summary>The creek sheet's side direction at vertex v of its line:
        /// the mitre of the two segments meeting there (their unit rights
        /// averaged and lengthened by 1/cos of half the turn, at most 2x, so
        /// an offset along it lands on both segments' offset lines); an end
        /// or a degenerate neighbour takes the segment's own.</summary>
        static Vector2 CreekMitre(CityMap.Water w, int v, Vector2 fallback)
        {
            Vector2 prev = v > 0 ? SegRight(w, v - 1) : Vector2.zero;
            Vector2 next = v + 1 < w.pts.Length ? SegRight(w, v) : Vector2.zero;
            if (prev == Vector2.zero) return next == Vector2.zero ? fallback : next;
            if (next == Vector2.zero) return prev;
            var m = prev + next;
            if (m.sqrMagnitude < 1e-6f) return next;
            m.Normalize();
            return m / Mathf.Max(0.5f, Vector2.Dot(m, next));
        }

        static Vector2 SegRight(CityMap.Water w, int i)
        {
            var d = w.pts[i + 1] - w.pts[i];
            float L = d.magnitude;
            return L < 0.01f ? Vector2.zero : new Vector2(d.y, -d.x) / L;
        }

        /// <summary>Is a grounded stretch of any road's pavement within
        /// <paramref name="pad"/> of p?</summary>
        static bool NearGroundedPavement(CityMap map, Vector2 p, float pad)
        {
            bankRoadScratch.Clear();
            float r = pad + 16f;
            map.EdgeSegsInRect(p - Vector2.one * r, p + Vector2.one * r, bankRoadScratch);
            foreach (int packed in bankRoadScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (si + 1 >= e.pts.Length) continue;
                Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                if (Vector2.Distance(p, a + d * t) > e.width * 0.5f + pad) continue;
                if (e.ElevatedAt(e.s[si] + Mathf.Sqrt(L2) * t)) continue;
                return true;
            }
            return false;
        }

        /// <summary>A triangle facing the side <paramref name="facing"/>
        /// points to (Tri(a, b, c) draws (a, c, b), normal Cross(c - a, b - a)).</summary>
        static void TriFacing(Bucket bk, Vector3 a, Vector3 b, Vector3 c, Vector3 facing, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            if (Vector3.Dot(Vector3.Cross(c - a, b - a), facing) >= 0f) bk.Tri(a, b, c, ua, ub, uc);
            else bk.Tri(a, c, b, ua, uc, ub);
        }
    }
}
