using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        // ------------------------------------------------------------------
        //  CULVERTS (WP-25). Where a ravine goes under a road on its
        //  embankment (CityCulverts finds where, each end, and its form),
        //  the end is one of two things:
        //    * a concrete HEADWALL across the channel, the pipe's mouth in
        //      it, WING WALLS back along both sides and grass BACKFILL
        //      between them level with the coping until the rising fill
        //      swallows it - only where the fill does rise to it within
        //      CityCulverts.BackfillFlatM and the channel runs on out in
        //      front (review, 2026-09-30: 47 of 55 walls stood free on the
        //      lawn, a concrete-walled berm behind them sloping down to the
        //      road); a concrete apron at its foot;
        //    * elsewhere the PIPE PROJECTING from the toe of the fill, its
        //      bore a quarter silted under the drawn ground, its barrel
        //      running back into the fill.
        //  Both drain into a short clay DITCH down the ravine, draped on the
        //  lattice like the creeks' banks. Nothing here touches the road, its
        //  verge or the ground function: every piece stands past the clear
        //  zone (CityCulverts.ClearOfRoads) and the lattice is left as it is.
        //
        //  Draws: the wall, wings and pipe barrel are the barriers mesh
        //  (concrete, the Solid layer: a car that leaves the road down the
        //  fill meets them); the backfill is ground (it collides as ground);
        //  the apron is the kerbs' render-only concrete; the pipe's inside is
        //  the lamp posts' dark pack metal (a corrugated pipe, and the dark a
        //  mouth needs), render-only; the ditch is the banks' clay. No new
        //  material.
        // ------------------------------------------------------------------
        public struct CulvertEnd
        {
            public Vector2 at, outward, downstream;
            public float groundY, wallW, wallH, pipeD, backfillM, pastPave;
            public int edge;
            /// <summary>A headwall (false: a projecting pipe).</summary>
            public bool headwall;
            /// <summary>A headwall's rising fill met its backfill, across
            /// the width between the wing walls, where the solve said.</summary>
            public bool backfillMet;
        }

        /// <summary>How far the pipe runs back into the fill before its dark
        /// end cap.</summary>
        const float PipeDepthM = 1.4f;
        /// <summary>A headwall's concrete apron: this long in front of its
        /// face.</summary>
        const float ApronM = 1.5f;
        const float DitchLiftM = 0.05f, ApronLiftM = 0.04f;
        /// <summary>A headwall's berm: its sides fall at the steepest graded
        /// bank (RoadsideRules.CityBankSlope), sampled every BermStepM out to
        /// at most BermSideMaxM, and end BermTuckM under the lattice; its
        /// concrete wings run WingM back.</summary>
        const float BermSlope = RoadsideRules.CityBankSlope, BermStepM = 0.25f, BermSideMaxM = 8f, BermTuckM = 0.05f, WingM = 1.5f;

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
            if (end.headwall) EmitHeadwall(map, tm, end);
            else EmitPipeEnd(map, tm, end);
            EmitDitch(map, tm, end);
        }

        static void EmitHeadwall(CityMap map, TileMeshes tm, CityCulverts.End end)
        {
            Vector2 o = end.outward, r = new Vector2(o.y, -o.x);
            float W = end.wallW, H = end.wallH, D = end.pipeD, T = CityCulverts.WallThickM;
            float hw = W * 0.5f;
            Vector2 at = end.at;
            var org = tm.origin;
            Vector3 P(Vector2 plan, float y) => new Vector3(plan.x - org.x, y, plan.y - org.z);
            float G(Vector2 p) => LatticeY(map, p.x, p.y);
            const float CopingM = CityCulverts.CopingM, WingThickM = CityCulverts.WingThickM, BackfillStepM = CityCulverts.BackfillStepM;

            // the pipe's invert on the drawn ground at the face; the wall's
            // foot under the lowest ground beneath any corner of it
            float inv = G(at) + CityCulverts.InvertLiftM;
            float foot = inv;
            foreach (var q in new[] { at + r * hw, at - r * hw, at + r * hw - o * T, at - r * hw - o * T })
                foot = Mathf.Min(foot, G(q));
            foot -= 0.5f;
            float top = inv + H;
            float fill = top - CopingM;

            // ---- the backfill: level with the coping back to where the
            // solve found the fill rising through it (CityCulverts
            // .HeadwallFits), which is where the wing walls end
            float wingIn = hw - WingThickM;
            int n = Mathf.Max(2, Mathf.RoundToInt(end.backfillM / BackfillStepM) + 1);
            float depth = (n - 1) * BackfillStepM;
            bool met = CityCulverts.LowestAcross(map, at - o * (T + depth), r, wingIn) >= fill - 1e-3f;

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

            // ---- THE EMBANKMENT BEHIND IT (review 2026-09-30: wing walls
            // 6 m long with the lawn low beside them read as a concrete box):
            // a grass berm level with the backfill from the wall's back face
            // to where the fill rises through it, its sides falling at the
            // steepest graded bank (1V:2H) until they pass under the
            // lattice; short concrete wings at the wall, their coping a
            // lip over the berm.
            var gb = GroundBucket(false);
            Vector2 GUv(Vector2 p) => GroundUV(false, p.x, p.y);
            // the berm's side run at a station: out from its top edge until
            // the 1V:2H face is a hand under the lattice
            float SideRun(Vector2 edge, Vector2 outN)
            {
                for (float s = BermStepM; s <= BermSideMaxM + 1e-3f; s += BermStepM)
                    if (fill - s * BermSlope <= G(edge + outN * s) - BermTuckM) return s;
                return BermSideMaxM;
            }
            for (int i = 0; i + 1 < n; i++)
            {
                float x0 = i * BackfillStepM, x1 = (i + 1) * BackfillStepM;
                Vector2 c0 = back - o * x0, c1 = back - o * x1;
                // the berm's level top, the wall's full width
                Vector2 l0 = c0 - r * hw, l1 = c1 - r * hw, q0 = c0 + r * hw, q1 = c1 + r * hw;
                gb.Up(P(l0, fill), P(l1, fill), P(q1, fill), P(q0, fill), GUv(l0), GUv(l1), GUv(q1), GUv(q0));
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 outN = r * side;
                    Vector2 e0 = c0 + outN * hw, e1 = c1 + outN * hw;
                    float s0 = SideRun(e0, outN), s1 = SideRun(e1, outN);
                    Vector2 f0 = e0 + outN * s0, f1 = e1 + outN * s1;
                    gb.Up(P(e0, fill), P(e1, fill), P(f1, fill - s1 * BermSlope), P(f0, fill - s0 * BermSlope), GUv(e0), GUv(e1), GUv(f1), GUv(f0));
                }
            }
            // beside the wall its sides wrap round its ends as a quarter cone
            // at the same 1V:2H, down past the wall's face to the lattice
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 outN = r * side;
                Vector2 e = back + outN * hw, dm = (outN + o).normalized;
                float sF = SideRun(e, outN), sM = SideRun(e, dm), sK = SideRun(e, o);
                Vector2 f = e + outN * sF, m = e + dm * sM, k = e + o * sK;
                Vector3 pe = P(e, fill), pf = P(f, fill - sF * BermSlope), pm = P(m, fill - sM * BermSlope), pk = P(k, fill - sK * BermSlope);
                TriFacing(gb, pe, pf, pm, Vector3.up, GUv(e), GUv(f), GUv(m));
                TriFacing(gb, pe, pm, pk, Vector3.up, GUv(e), GUv(m), GUv(k));
            }
            // its far end, where the fill has risen through it: closed down
            // under the lattice (the ground hides most of it)
            {
                Vector2 c = back - o * depth;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 outN = r * side;
                    Vector2 e = c + outN * hw;
                    float s = SideRun(e, outN);
                    Vector2 f = e + outN * s;
                    float yF = fill - s * BermSlope;
                    float bottom = Mathf.Min(Mathf.Min(G(e), G(f)), yF) - BermTuckM;
                    gb.Face(P(e, fill), P(f, yF), P(f, bottom), P(e, bottom), -o3,
                            GUv(e), GUv(f), GUv(f + o * (yF - bottom)), GUv(e + o * (fill - bottom)));
                }
                Vector2 a = c - r * hw, b = c + r * hw;
                float bot = Mathf.Min(fill, Mathf.Min(G(a), G(b))) - BermTuckM;
                gb.Face(P(a, fill), P(b, fill), P(b, bot), P(a, bot), -o3,
                        GUv(a), GUv(b), GUv(b + o * (fill - bot)), GUv(a + o * (fill - bot)));
            }
            // the wings: WingM back along both sides, a coping over the berm
            {
                float wl = Mathf.Min(WingM, depth), t0 = fill + CopingM;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 outN = r * side;
                    Vector2 in0 = back + outN * wingIn, in1 = back - o * wl + outN * wingIn;
                    Vector2 ou0 = back + outN * hw, ou1 = back - o * wl + outN * hw;
                    float lip = fill - 0.2f;
                    con.WallSloped(new Vector3(ou0.x - org.x, 0f, ou0.y - org.z), new Vector3(ou1.x - org.x, 0f, ou1.y - org.z),
                                   lip, t0, lip, t0, outN, 0f, wl / UvM, lip / UvM, t0 / UvM);
                    con.WallSloped(new Vector3(in0.x - org.x, 0f, in0.y - org.z), new Vector3(in1.x - org.x, 0f, in1.y - org.z),
                                   lip, t0, lip, t0, -outN, 0f, wl / UvM, lip / UvM, t0 / UvM);
                    con.Up(P(in0, t0), P(in1, t0), P(ou1, t0), P(ou0, t0),
                           new Vector2(0f, 0f), new Vector2(wl / UvM, 0f), new Vector2(wl / UvM, WingThickM / UvM), new Vector2(0f, WingThickM / UvM));
                    con.Face(P(in1, t0), P(ou1, t0), P(ou1, lip), P(in1, lip), -o3,
                             new Vector2(0f, t0 / UvM), new Vector2(WingThickM / UvM, t0 / UvM), new Vector2(WingThickM / UvM, lip / UvM), new Vector2(0f, lip / UvM));
                }
            }

            // ---- the apron: a concrete slab at the wall's foot, where the
            // pipe spills (render-only: the kerbs' concrete, a hand proud)
            Drape(map, kerbBucket, org, at + r * hw, at + r * hw + o * ApronM, at - r * hw + o * ApronM, at - r * hw, ApronLiftM, UvM);

            tm.culvertEnds.Add(new CulvertEnd
            {
                at = at, outward = o, downstream = end.downstream, groundY = end.groundY, wallW = W, wallH = H, pipeD = D,
                backfillM = depth, pastPave = end.pastPave, edge = end.edge, headwall = true, backfillMet = met,
            });
        }

        /// <summary>
        /// A PIPE PROJECTING FROM THE TOE OF THE FILL, where a headwall would
        /// stand free (CityCulverts.HeadwallFits): a concrete barrel, eight
        /// sided, its bore a quarter under the drawn ground at the mouth
        /// (silted), running back <see cref="CityCulverts.PipeBarrelM"/>
        /// into the embankment, rising at half the ground's own rise there but
        /// never with its back end out of the ground (on a flat toe it dips
        /// in), so the fill swallows it; the mouth's ring and the dark inside
        /// as a headwall's. Solid, like the wall: it stands past the clear
        /// zone.
        /// </summary>
        static void EmitPipeEnd(CityMap map, TileMeshes tm, CityCulverts.End end)
        {
            Vector2 o = end.outward, r = new Vector2(o.y, -o.x);
            float D = end.pipeD, R = D * 0.5f, Ro = R + CityCulverts.PipeWallM, L = CityCulverts.PipeBarrelM;
            Vector2 at = end.at;
            var org = tm.origin;
            Vector3 P(Vector2 plan, float y) => new Vector3(plan.x - org.x, y, plan.y - org.z);
            float G(Vector2 p) => LatticeY(map, p.x, p.y);
            const float UvM = 3f;

            float g = G(at);
            float cy0 = g - D * CityCulverts.PipeBuryFrac + R;
            // back into the fill at half the ground's rise - and never with
            // its back end out of the ground: where the land behind does not
            // rise (review 2026-09-30: a 4 m tube lying on the lawn) the
            // barrel dips into it instead, a pipe coming up out of the toe
            float gBack = G(at - o * L);
            float rise = Mathf.Clamp((gBack - g) * 0.5f, 0f, L * 0.25f);
            float cy1 = Mathf.Min(cy0 + rise, gBack - Ro - 0.05f);
            Vector3 o3 = new Vector3(o.x, 0f, o.y), up = Vector3.up;
            // the barrel's axis leans up into the fill: its sections are
            // square to the axis, not plumb
            Vector3 axis0 = P(at, cy0), axis1 = P(at - o * L, cy1);
            Vector3 fwd = (axis0 - axis1).normalized;
            Vector3 r3 = new Vector3(r.x, 0f, r.y), u3 = Vector3.Cross(fwd, r3).normalized;
            if (u3.y < 0f) u3 = -u3;
            var outer0 = new Vector3[8]; var outer1 = new Vector3[8]; var inner0 = new Vector3[8]; var innerB = new Vector3[8];
            for (int k = 0; k < 8; k++)
            {
                float a = (22.5f + 45f * k) * Mathf.Deg2Rad;
                Vector3 dir = r3 * Mathf.Cos(a) + u3 * Mathf.Sin(a);
                outer0[k] = axis0 + dir * Ro;
                outer1[k] = axis1 + dir * Ro;
                inner0[k] = axis0 + dir * R;
                innerB[k] = axis0 - fwd * PipeDepthM + dir * R;
            }
            var con = barrierBucket;
            var dark = lampBucket;
            Vector3 axisB = axis0 - fwd * PipeDepthM;
            for (int k = 0; k < 8; k++)
            {
                int k1 = (k + 1) & 7;
                // the barrel's outside, facing away from its axis
                Vector3 mid0 = (outer0[k] + outer0[k1]) * 0.5f;
                float u0 = k * Ro * 0.78f / UvM, u1 = (k + 1) * Ro * 0.78f / UvM;
                con.Face(outer0[k], outer0[k1], outer1[k1], outer1[k], mid0 - axis0,
                         new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, L / UvM), new Vector2(u0, L / UvM));
                // the mouth's ring
                con.Face(outer0[k], outer0[k1], inner0[k1], inner0[k], fwd,
                         new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, (Ro - R) / UvM), new Vector2(u0, (Ro - R) / UvM));
                // the back end, buried in the fill (or closed where it is not)
                TriFacing(con, outer1[k], outer1[k1], axis1, -fwd, new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2((u0 + u1) * 0.5f, Ro / UvM));
                // the dark bore and its end
                Vector3 midI = (inner0[k] + inner0[k1]) * 0.5f;
                dark.Face(inner0[k], inner0[k1], innerB[k1], innerB[k], axis0 - midI,
                          new Vector2(k / 8f, 0f), new Vector2((k + 1) / 8f, 0f), new Vector2((k + 1) / 8f, PipeDepthM / 2f), new Vector2(k / 8f, PipeDepthM / 2f));
                TriFacing(dark, innerB[k], innerB[k1], axisB, fwd, new Vector2(0f, 0f), new Vector2(0.25f, 0f), new Vector2(0.12f, 0.25f));
            }

            tm.culvertEnds.Add(new CulvertEnd
            {
                at = at, outward = o, downstream = end.downstream, groundY = end.groundY, wallW = 2f * Ro, wallH = cy0 + Ro - g, pipeD = D,
                backfillM = L, pastPave = end.pastPave, edge = end.edge, headwall = false, backfillMet = false,
            });
        }

        /// <summary>
        /// THE DITCH every culvert end drains into: a strip of the banks'
        /// pack clay down the ravine from the end's face, the pipe plus
        /// <see cref="CityCulverts.DitchOverPipeM"/> wide, narrowing over its
        /// last third, <see cref="CityCulverts.DitchM"/> long, draped on the
        /// lattice (review, 2026-09-30: nothing in front of a pipe said a
        /// stream ran out of it). Never on or beside a grounded road's
        /// pavement.
        /// </summary>
        static void EmitDitch(CityMap map, TileMeshes tm, CityCulverts.End end)
        {
            Vector2 d = end.downstream, r = new Vector2(d.y, -d.x);
            float w = (end.pipeD + CityCulverts.DitchOverPipeM) * 0.5f, L = CityCulverts.DitchM;
            // from the front of a headwall's apron, or from under a pipe's
            // mouth
            Vector2 a = end.headwall ? end.at + end.outward * ApronM : end.at - end.outward * 0.1f;
            Vector2 b = end.at + d * (L * 0.66f), c = end.at + d * L;
            float wc = w * 0.25f;
            if (!NearGroundedPavement(map, (a + b) * 0.5f, BankRoadClearM + L * 0.33f + w))
                Drape(map, bankBucket, tm.origin, a + r * w, b + r * w, b - r * w, a - r * w, DitchLiftM, BankTexM);
            if (!NearGroundedPavement(map, (b + c) * 0.5f, BankRoadClearM + L * 0.17f + w))
                Drape(map, bankBucket, tm.origin, b + r * w, c + r * wc, c - r * wc, b - r * w, DitchLiftM, BankTexM);
        }

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

        static void EmitCreekBanks(CityMap map, TileMeshes tm, Vector2 p0, Vector2 p1, Vector2 right0, Vector2 right1, float flat, float surfaceY)
        {
            if (HydroOff) return;
            // ONLY WHERE THE WATER SHOWS (the hydro audit's own test, at the
            // piece's middle): where the lattice buries the sheet - a road's
            // fill over the creek, the 8 m cells over a narrow one - two clay
            // strips 5.5 m wide with lawn between them read as a dirt track,
            // beside I-77 and on the last 30-40 m to State St and Archdale Dr
            var mid = (p0 + p1) * 0.5f;
            if (!WaterShows(map, mid, surfaceY)) return;
            var o = tm.origin;
            for (int side = -1; side <= 1; side += 2)
                for (int c = 0; c + 1 < BankCols.Length; c++)
                {
                    float d0 = (flat + BankCols[c]) * side, d1 = (flat + BankCols[c + 1]) * side;
                    Vector2 a0 = p0 + right0 * d0, a1 = p1 + right1 * d0, b0 = p0 + right0 * d1, b1 = p1 + right1 * d1;
                    Vector2 qm = (a0 + a1 + b0 + b1) * 0.25f;
                    float half = 0.5f * Mathf.Max(Vector2.Distance(a0, b1), Vector2.Distance(a1, b0));
                    if (NearGroundedPavement(map, qm, BankRoadClearM + half)) continue;
                    Drape(map, bankBucket, o, a0, a1, b1, b0, BankLiftM, BankTexM);
                }
        }

        /// <summary>Does a creek's water show at plan point
        /// <paramref name="p"/> - its surface over the drawn ground? The
        /// hydro audit's test, and the banks'.</summary>
        public static bool WaterShows(CityMap map, Vector2 p, float surfaceY) => LatticeY(map, p.x, p.y) < surfaceY - 0.02f;

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
