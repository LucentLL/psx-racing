using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// PARKING DECKS (2026-10-05, part 1): a listed deck (CityDecks) drawn in
    /// place of its solid building, never both. Everything it is made of rides
    /// buckets the tile already has: the floors, ramps, parapets, spine, columns
    /// and driveway are <see cref="Slot.Concrete"/> (the structural concrete of
    /// the bridge decks, in the roads mesh, so the floors are what the wheels
    /// read and the walls are solid); the stall lines are the lots' stall paint
    /// (the tw2 profile's white column); the roof poles and ceiling fixtures are
    /// the lamp mesh, and their light joins the tile's lamps (breakaway: lit,
    /// no post collider) with <see cref="LampDeck"/>. At most one new draw on a
    /// deck tile (the concrete, where the tile has no bridge).
    /// </summary>
    public static partial class CityMeshes
    {
        /// <summary>A deck's ceiling fixture or roof pole (CityWorld.LampGain).</summary>
        public const byte LampDeck = 4;
        public static int DecksBuilt, DecksRefused;

        static Vector3 DP(CityDecks.Deck d, float x, float y, float z, TileMeshes tm) => L(d.W(x, z), d.y0 + y, tm);
        static Vector2 DUV(CityDecks.Deck d, float x, float z) { var p = d.W(x, z); return new Vector2(p.x * 0.25f, p.y * 0.25f); }
        static Vector2 DirW(CityDecks.Deck d, float lx, float lz) => d.U * lx + d.V * lz;

        /// <summary>One deck, or false (the caller draws the solid building).</summary>
        static bool EmitDeck(CityMap map, Trims trims, TileMeshes tm, int fi)
        {
            var d = CityDecks.ForFootprint(fi);
            if (d == null || !CityDecks.Solve(map, d)) return false;
            // the rectangle must not reach the drawn pavement (no deck in a lane)
            fitPoly.Clear();
            fitPoly.Add(d.W(-d.hu, -d.hv)); fitPoly.Add(d.W(d.hu, -d.hv)); fitPoly.Add(d.W(d.hu, d.hv)); fitPoly.Add(d.W(-d.hu, d.hv));
            if (FitFootprint(map, trims, fitPoly, d.y0 - 1f, d.y0 + d.levels * CityDecks.FloorM) != 0)
            {
                if (!d.built) { d.why = "its rectangle meets the drawn pavement"; DecksRefused++; }
                return false;
            }
            var con = buckets[(int)Slot.Concrete];
            var paint = stallSlot >= 0 ? buckets[(int)SlotOf(stallSlot, Surface.AsphaltNew)] : null;
            int t0 = con.t.Count + (paint != null ? paint.t.Count : 0) + lampBucket.t.Count;

            int top = d.levels - 1;
            float hv = d.hv, hu = d.hu, x0 = d.x0, x1 = d.x1, W = CityDecks.WallM, S = CityDecks.SpineM * 0.5f;
            float H = CityDecks.FloorM, P = CityDecks.ParapetM, low = d.yLow - d.y0;

            // ---- floors (top and soffit) ----
            for (int k = 0; k <= top; k++)
            {
                float y = CityDecks.TurnLo(k);
                // the ground turning bay runs to the outer face (the opening's sill)
                if (k == 0) DeckGroundBay(con, d, tm);
                else DeckFloor(con, d, tm, -hu + W, x0, -hv + W, hv - W, _ => y, true);
            }
            for (int k = 0; k < top; k++)
            {
                int kk = k; float yh = CityDecks.TurnHi(k);
                DeckFloor(con, d, tm, x1, hu - W, -hv + W, hv - W, _ => yh, k > 0);
                DeckFloor(con, d, tm, x0, x1, -hv + W, -S, x => CityDecks.BayA(d, kk, x), k > 0);
                DeckFloor(con, d, tm, x0, x1, S, hv - W, x => CityDecks.BayB(d, kk, x), k > 0);
            }

            // ---- perimeter parapets: a band of precast at every floor's edge,
            // the open band above it, columns between (the k = 0 runs skirt down
            // to the lowest ground: the ramps below the first turn are on fill)
            var en = d.EntryN; var ep = d.EntryP; float half = CityDecks.OpeningM * 0.5f;
            for (int k = 0; k <= top; k++)
            {
                float y = CityDecks.TurnLo(k);
                bool skirt = k == 0;
                System.Func<Vector2, float> yf = k == 0 ? (System.Func<Vector2, float>)(p => CityDecks.GroundBayLocal(d, p.x, p.y)) : (_ => y);
                float oa = float.MaxValue, ob = float.MinValue;
                if (k == 0) { oa = d.entryAt - half; ob = d.entryAt + half; }
                DeckWall(con, d, tm, new Vector2(-hu, -hv), new Vector2(x0, -hv), new Vector2(0f, -1f), yf, skirt, low, d.entrySide == -1 ? oa : float.MaxValue, ob);
                DeckWall(con, d, tm, new Vector2(-hu, hv), new Vector2(x0, hv), new Vector2(0f, 1f), yf, skirt, low, d.entrySide == 1 ? oa : float.MaxValue, ob);
                DeckWall(con, d, tm, new Vector2(-hu, -hv), new Vector2(-hu, hv), new Vector2(-1f, 0f), yf, skirt, low, d.entrySide == 0 ? oa : float.MaxValue, ob);
            }
            for (int k = 0; k < top; k++)
            {
                int kk = k; float yh = CityDecks.TurnHi(k); bool skirt = k == 0;
                DeckWall(con, d, tm, new Vector2(x1, -hv), new Vector2(hu, -hv), new Vector2(0f, -1f), _ => yh, skirt, low, float.MaxValue, 0f);
                DeckWall(con, d, tm, new Vector2(x1, hv), new Vector2(hu, hv), new Vector2(0f, 1f), _ => yh, skirt, low, float.MaxValue, 0f);
                DeckWall(con, d, tm, new Vector2(hu, -hv), new Vector2(hu, hv), new Vector2(1f, 0f), _ => yh, skirt, low, float.MaxValue, 0f);
                DeckWall(con, d, tm, new Vector2(x0, -hv), new Vector2(x1, -hv), new Vector2(0f, -1f), p => CityDecks.BayA(d, kk, p.x), skirt, low, float.MaxValue, 0f);
                DeckWall(con, d, tm, new Vector2(x0, hv), new Vector2(x1, hv), new Vector2(0f, 1f), p => CityDecks.BayB(d, kk, p.x), skirt, low, float.MaxValue, 0f);
            }
            // the roof's turning bay ends in a drop over bay A's last run: a parapet
            DeckWall(con, d, tm, new Vector2(x0 + W, -hv + W), new Vector2(x0 + W, -S), new Vector2(1f, 0f), _ => CityDecks.TurnLo(top), false, low, float.MaxValue, 0f);

            // ---- the spine between the bays, ground to a parapet over the top runs ----
            {
                int n = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / 2.5f));
                float Top(float x) => Mathf.Max(CityDecks.BayA(d, top - 1, x), CityDecks.BayB(d, top - 1, x)) + P;
                for (int i = 0; i < n; i++)
                {
                    float a = Mathf.Lerp(x0, x1, i / (float)n), b = Mathf.Lerp(x0, x1, (i + 1) / (float)n);
                    float ta = Top(a), tb = Top(b);
                    for (int s = -1; s <= 1; s += 2)
                        con.WallSloped(DP(d, a, 0f, s * S, tm), DP(d, b, 0f, s * S, tm), d.y0 + low, d.y0 + ta, d.y0 + low, d.y0 + tb,
                                       DirW(d, 0f, s), a * 0.25f, b * 0.25f, low * 0.25f, ta * 0.25f);
                    con.Up(DP(d, a, ta, -S, tm), DP(d, b, tb, -S, tm), DP(d, b, tb, S, tm), DP(d, a, ta, S, tm),
                           DUV(d, a, -S), DUV(d, b, -S), DUV(d, b, S), DUV(d, a, S));
                }
                foreach (var (x, sgn) in new[] { (x0, -1f), (x1, 1f) })
                {
                    float tt = Top(x);
                    con.Face(DP(d, x, low, -S, tm), DP(d, x, tt, -S, tm), DP(d, x, tt, S, tm), DP(d, x, low, S, tm),
                             new Vector3(DirW(d, sgn, 0f).x, 0f, DirW(d, sgn, 0f).y),
                             new Vector2(0f, low * 0.25f), new Vector2(0f, tt * 0.25f), new Vector2(0.1f, tt * 0.25f), new Vector2(0.1f, low * 0.25f));
                }
                // the fill under bay B's first run faces the ground turning bay
                con.Wall(DP(d, x0, 0f, S, tm), DP(d, x0, 0f, hv - W, tm), d.y0 + low, d.y0 + H, DirW(d, -1f, 0f), S * 0.25f, (hv - W) * 0.25f, low * 0.25f, H * 0.25f);
            }

            // ---- columns at the perimeter, between a parapet and the slab above ----
            for (int s = -1; s <= 1; s += 2)
                for (float x = -hu + 0.8f; x <= hu - 0.8f; x += CityDecks.ColumnPitchM)
                {
                    int kk = 0;
                    for (int k = 0; ; k++)
                    {
                        float f0, f1;
                        if (x < x0) { if (k >= top) break; f0 = k == 0 ? CityDecks.GroundBayLocal(d, x, s * (hv - W)) : CityDecks.TurnLo(k); f1 = CityDecks.TurnLo(k + 1); }
                        else if (x > x1) { if (k >= top - 1) break; f0 = CityDecks.TurnHi(k); f1 = CityDecks.TurnHi(k + 1); }
                        else { if (k >= top - 1) break; f0 = s < 0 ? CityDecks.BayA(d, k, x) : CityDecks.BayB(d, k, x); f1 = f0 + H; }
                        kk++;
                        float c0 = f0 + P, c1 = f1 - CityDecks.SlabM;
                        if (c1 - c0 < 0.3f) continue;
                        float zo = s * (hv - W), zi = s * (hv - W - CityDecks.ColumnM), xa = x - CityDecks.ColumnM * 0.5f, xb = x + CityDecks.ColumnM * 0.5f;
                        DeckBoxSides(con, d, tm, xa, xb, Mathf.Min(zo, zi), Mathf.Max(zo, zi), c0, c1);
                    }
                }

            // ---- stall lines: 90 degree stalls down both sides of each bay's aisle ----
            if (paint != null)
            {
                float u = 0.5f * (stallU0 + stallU1);
                for (int k = 0; k < top; k++)
                    for (int s = -1; s <= 1; s += 2)
                        for (float x = x0 + 1.5f; x <= x1 - 1.5f; x += StallW)
                        {
                            float y = (s < 0 ? CityDecks.BayA(d, k, x) : CityDecks.BayB(d, k, x)) + 0.02f;
                            foreach (float zIn in new[] { s * (hv - W), s * (S + StallD) })
                            {
                                float za = zIn, zb = zIn - s * StallD;
                                float xa = x - StallLineW * 0.5f, xb = x + StallLineW * 0.5f;
                                paint.Up(DP(d, xa, y, za, tm), DP(d, xb, y, za, tm), DP(d, xb, y, zb, tm), DP(d, xa, y, zb, tm),
                                         new Vector2(u, 0f), new Vector2(u, 0f), new Vector2(u, 0.45f), new Vector2(u, 0.45f));
                            }
                        }
            }

            // ---- the driveway: a concrete apron out of the opening to the street's edge ----
            {
                var nW = DirW(d, en.x, en.y);
                var side = new Vector2(-en.y, en.x);
                float a0 = -W, a1 = d.driveLen + 0.3f;
                int n = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) / 1.5f));
                for (int i = 0; i < n; i++)
                {
                    float aa = Mathf.Lerp(a0, a1, i / (float)n), ab = Mathf.Lerp(a0, a1, (i + 1) / (float)n);
                    float ya = ApronY(map, d, aa), yb = ApronY(map, d, ab);
                    Vector2 pa = ep + en * aa, pb = ep + en * ab;
                    Vector2 la = pa + side * (half + 0.5f), ra = pa - side * (half + 0.5f), lb = pb + side * (half + 0.5f), rb = pb - side * (half + 0.5f);
                    con.Up(DP(d, la.x, ya, la.y, tm), DP(d, lb.x, yb, lb.y, tm), DP(d, rb.x, yb, rb.y, tm), DP(d, ra.x, ya, ra.y, tm),
                           DUV(d, la.x, la.y), DUV(d, lb.x, lb.y), DUV(d, rb.x, rb.y), DUV(d, ra.x, ra.y));
                    foreach (var (p0, p1, o) in new[] { (la, lb, side), (ra, rb, -side) })
                        con.WallSloped(DP(d, p0.x, 0f, p0.y, tm), DP(d, p1.x, 0f, p1.y, tm), d.y0 + ya - 0.45f, d.y0 + ya, d.y0 + yb - 0.45f, d.y0 + yb,
                                       DirW(d, o.x, o.y), aa * 0.25f, ab * 0.25f, 0f, 0.1f);
                }
                _ = nW;
            }

            // ---- light: fixtures under every covered aisle, poles on the roof ----
            {
                int bays = top * 2;
                float span = x1 - x0 - 6f;
                int per = Mathf.Clamp(Mathf.FloorToInt(36f / Mathf.Max(1, bays)), 1, Mathf.Max(1, Mathf.FloorToInt(span / 8f)));
                for (int k = 0; k < top - 1; k++)
                    for (int s = -1; s <= 1; s += 2)
                        for (int i = 0; i < per; i++)
                        {
                            float x = x0 + 3f + span * (i + 0.5f) / per, z = s * d.hv * 0.5f;
                            float f = s < 0 ? CityDecks.BayA(d, k, x) : CityDecks.BayB(d, k, x);
                            float ceil = f + H - CityDecks.SlabM - 0.04f;
                            lampBucket.Down(DP(d, x - 0.6f, ceil, z - 0.15f, tm), DP(d, x + 0.6f, ceil, z - 0.15f, tm),
                                            DP(d, x + 0.6f, ceil, z + 0.15f, tm), DP(d, x - 0.6f, ceil, z + 0.15f, tm),
                                            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                            tm.lamps.Add(new Lamp { foot = DP(d, x, f, z, tm), head = DP(d, x, ceil - 0.05f, z, tm), height = ceil - f, kind = LampDeck, breakaway = true });
                        }
                // roof poles along both long parapets (bay A's and bay B's last runs)
                const float PoleH = 7f;
                for (int s = -1; s <= 1; s += 2)
                    for (float x = x0 + 4f; x <= x1 - 4f + 0.01f; x += Mathf.Max(12f, (x1 - x0 - 8f) / 2f))
                    {
                        float f = s < 0 ? CityDecks.BayA(d, top - 1, x) : CityDecks.BayB(d, top - 1, x);
                        float z = s * (hv - W - 0.35f);
                        DeckBoxSides(lampBucket, d, tm, x - 0.12f, x + 0.12f, z - 0.12f, z + 0.12f, f, f + PoleH);
                        float zh = z - s * 0.8f;
                        lampBucket.Down(DP(d, x - 0.35f, f + PoleH - 0.1f, zh - 0.5f, tm), DP(d, x + 0.35f, f + PoleH - 0.1f, zh - 0.5f, tm),
                                        DP(d, x + 0.35f, f + PoleH - 0.1f, zh + 0.5f, tm), DP(d, x - 0.35f, f + PoleH - 0.1f, zh + 0.5f, tm),
                                        new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                        tm.lamps.Add(new Lamp { foot = DP(d, x, f, z, tm), head = DP(d, x, f + PoleH - 0.15f, zh, tm), height = PoleH, kind = LampDeck, breakaway = true });
                    }
            }

            d.trisDeck = (con.t.Count + (paint != null ? paint.t.Count : 0) + lampBucket.t.Count - t0) / 3;
            if (!d.built) { d.built = true; DecksBuilt++; }
            CityDecks.AnyBuilt = true;
            return true;
        }

        /// <summary>The apron's local height <paramref name="a"/> metres out of
        /// the opening: the driveway's smoothstep, never under the verge.</summary>
        static float ApronY(CityMap map, CityDecks.Deck d, float a)
        {
            var p = d.W(d.EntryP.x + d.EntryN.x * a, d.EntryP.y + d.EntryN.y * a);
            float y = CityDecks.DriveAt(d, a);
            if (a > 0f) y = Mathf.Max(y, CityElevation.GroundY(map, p.x, p.y) + 0.03f);
            return y - d.y0;
        }

        /// <summary>The ground turning bay: the entry ramp's top end, rising
        /// from the opening's sill to the ground floor over <see cref="CityDecks.Deck.lin"/>.</summary>
        static void DeckGroundBay(Bucket con, CityDecks.Deck d, TileMeshes tm)
        {
            float xa = -d.hu, xb = d.x0, za = -d.hv, zb = d.hv;
            int nx = Mathf.CeilToInt((xb - xa) / 2f), nz = Mathf.CeilToInt((zb - za) / 2f);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    float a = Mathf.Lerp(xa, xb, i / (float)nx), b = Mathf.Lerp(xa, xb, (i + 1) / (float)nx);
                    float c = Mathf.Lerp(za, zb, j / (float)nz), e = Mathf.Lerp(za, zb, (j + 1) / (float)nz);
                    con.Up(DP(d, a, CityDecks.GroundBayLocal(d, a, c), c, tm), DP(d, b, CityDecks.GroundBayLocal(d, b, c), c, tm),
                           DP(d, b, CityDecks.GroundBayLocal(d, b, e), e, tm), DP(d, a, CityDecks.GroundBayLocal(d, a, e), e, tm),
                           DUV(d, a, c), DUV(d, b, c), DUV(d, b, e), DUV(d, a, e));
                }
        }

        /// <summary>A floor piece from xa to xb, za to zb at height yf(x): its
        /// top, and its soffit a slab below where a level looks up at it.</summary>
        static void DeckFloor(Bucket con, CityDecks.Deck d, TileMeshes tm, float xa, float xb, float za, float zb,
                              System.Func<float, float> yf, bool soffit)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(xb - xa) / 2.5f));
            float sl = CityDecks.SlabM;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Lerp(xa, xb, i / (float)n), b = Mathf.Lerp(xa, xb, (i + 1) / (float)n);
                float ya = yf(a), yb = yf(b);
                con.Up(DP(d, a, ya, za, tm), DP(d, b, yb, za, tm), DP(d, b, yb, zb, tm), DP(d, a, ya, zb, tm),
                       DUV(d, a, za), DUV(d, b, za), DUV(d, b, zb), DUV(d, a, zb));
                if (soffit)
                    con.Down(DP(d, a, ya - sl, za, tm), DP(d, b, yb - sl, za, tm), DP(d, b, yb - sl, zb, tm), DP(d, a, ya - sl, zb, tm),
                             DUV(d, a, za), DUV(d, b, za), DUV(d, b, zb), DUV(d, a, zb));
            }
        }

        /// <summary>A parapet along the outer line from A to B (local), the
        /// wall <see cref="CityDecks.WallM"/> thick inside it, outward
        /// <paramref name="outL"/>: inner face floor to parapet top, outer face
        /// slab bottom (or the skirt's low) to the top, the cap. Pieces whose
        /// middle falls in [oa, ob] (along the run's own axis) are the opening:
        /// only the skirt below the sill.</summary>
        static void DeckWall(Bucket con, CityDecks.Deck d, TileMeshes tm, Vector2 A, Vector2 B, Vector2 outL,
                             System.Func<Vector2, float> yf, bool skirt, float low, float oa, float ob)
        {
            float len = Vector2.Distance(A, B);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / 2.5f));
            var inV = -outL * CityDecks.WallM;
            bool alongX = Mathf.Abs(B.x - A.x) > Mathf.Abs(B.y - A.y);
            var oW = DirW(d, outL.x, outL.y);
            float P = CityDecks.ParapetM;
            for (int i = 0; i < n; i++)
            {
                var a = Vector2.Lerp(A, B, i / (float)n); var b = Vector2.Lerp(A, B, (i + 1) / (float)n);
                float ya = yf(a), yb = yf(b);
                float bot = skirt ? low : -CityDecks.SlabM;
                float mid = alongX ? 0.5f * (a.x + b.x) : 0.5f * (a.y + b.y);
                float ua = (alongX ? a.x : a.y) * 0.25f, ub = (alongX ? b.x : b.y) * 0.25f;
                if (mid > oa && mid < ob)
                {
                    con.WallSloped(DP(d, a.x, 0f, a.y, tm), DP(d, b.x, 0f, b.y, tm), d.y0 + (skirt ? low : ya + bot), d.y0 + ya, d.y0 + (skirt ? low : yb + bot), d.y0 + yb,
                                   oW, ua, ub, 0f, 0.3f);
                    continue;
                }
                float ba = skirt ? low : ya + bot, bb = skirt ? low : yb + bot;
                con.WallSloped(DP(d, a.x, 0f, a.y, tm), DP(d, b.x, 0f, b.y, tm), d.y0 + ba, d.y0 + ya + P, d.y0 + bb, d.y0 + yb + P,
                               oW, ua, ub, ba * 0.25f, (ya + P) * 0.25f);
                Vector2 ai = a + inV, bi = b + inV;
                con.WallSloped(DP(d, ai.x, 0f, ai.y, tm), DP(d, bi.x, 0f, bi.y, tm), d.y0 + ya, d.y0 + ya + P, d.y0 + yb, d.y0 + yb + P,
                               -oW, ua, ub, ya * 0.25f, (ya + P) * 0.25f);
                con.Up(DP(d, a.x, ya + P, a.y, tm), DP(d, b.x, yb + P, b.y, tm), DP(d, bi.x, yb + P, bi.y, tm), DP(d, ai.x, ya + P, ai.y, tm),
                       DUV(d, a.x, a.y), DUV(d, b.x, b.y), DUV(d, bi.x, bi.y), DUV(d, ai.x, ai.y));
            }
        }

        /// <summary>The four sides of an upright box (a column, a pole), local x/z extents.</summary>
        static void DeckBoxSides(Bucket bk, CityDecks.Deck d, TileMeshes tm, float xa, float xb, float za, float zb, float y0, float y1)
        {
            float Y0 = d.y0 + y0, Y1 = d.y0 + y1, v0 = y0 * 0.25f, v1 = y1 * 0.25f;
            bk.Wall(DP(d, xa, 0f, za, tm), DP(d, xb, 0f, za, tm), Y0, Y1, DirW(d, 0f, -1f), 0f, 0.15f, v0, v1);
            bk.Wall(DP(d, xa, 0f, zb, tm), DP(d, xb, 0f, zb, tm), Y0, Y1, DirW(d, 0f, 1f), 0f, 0.15f, v0, v1);
            bk.Wall(DP(d, xa, 0f, za, tm), DP(d, xa, 0f, zb, tm), Y0, Y1, DirW(d, -1f, 0f), 0f, 0.15f, v0, v1);
            bk.Wall(DP(d, xb, 0f, za, tm), DP(d, xb, 0f, zb, tm), Y0, Y1, DirW(d, 1f, 0f), 0f, 0.15f, v0, v1);
        }
    }
}
