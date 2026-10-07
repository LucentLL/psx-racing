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
        /// <summary>A deck's stall line, wider than the lots' (see the stall lines).</summary>
        const float DeckLineW = 0.15f;
        /// <summary>The bucket the deck being built lays its floors' tops in
        /// (the slab, see EmitDeck) - the concrete when there is none.</summary>
        static Bucket deckSlab;
        static readonly bool DeckSlabOff = System.Environment.GetEnvironmentVariable("PSX_DECK_SLAB") == "0";
        /// <summary>A floor top's UV: the lots' world mapping (12 m) on the
        /// slab, the pack concrete's 4 m otherwise.</summary>
        static Vector2 FUV(CityDecks.Deck d, float x, float z, Bucket con)
        {
            if (deckSlab == con) return DUV(d, x, z);
            var p = d.W(x, z); return new Vector2(p.x / 12f, p.y / 12f);
        }
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
            // the stall paint's slot is found by the lots' first use: a deck
            // on the first tile built (the deck photos spawn there) came
            // before it and drew no stall lines at all
            EnsureLots(map);
            var paint = stallSlot >= 0 ? buckets[(int)SlotOf(stallSlot, Surface.AsphaltNew)] : null;
            // THE SLAB (leftovers-b, 2026-10-05): the floors the cars drive on
            // are a sealed deck, dark grey, in the lots' own pavement (the fans'
            // slot, world UVs at the lots' 12 m): the pack concrete read near
            // white by day and the stall lines on it hardly at all. Only a slot
            // the tile has already drawn into, so no draw call of its own; a
            // tile without one keeps the concrete. OFF until gated: PSX_DECK_SLAB=1 lays it.
            deckSlab = con;
            if (!DeckSlabOff)
                foreach (var sf in new[] { Surface.AsphaltOld, Surface.AsphaltNew })
                {
                    var sb = buckets[(int)SlotOf(JunctionProfile, sf)];
                    if (sb.Count > 0) { deckSlab = sb; break; }
                }
            // BRICK (owner, 2026-10-06, the UNC Charlotte decks): the street
            // faces in red brick, a cast-stone band at every floor. The brick
            // rides the facade atlas's bucket (its brick column, the bucket the
            // solid deck drew its walls in): no draw call of its own.
            deckBrick = d.Brick ? buckets[(int)Slot.FacadeGlass] : null;
            var keepTint = Bucket.Tint;
            if (deckBrick != null) Bucket.Tint = new Color32(TintByte(1.18f), TintByte(0.92f), TintByte(0.84f), (byte)(LookBrick * 32));
            // the clearance bar's yellow: the lines' own yellow in the stall paint's slot
            deckPaint = deckBrick != null ? paint : null;
            if (deckPaint != null)
            {
                var layY = LineModel.LayoutOf(stallSlot);
                deckYellow = new Vector2(0.5f * (stallU0 + stallU1), 0.2f);
                for (int q = 0; q < layY.m.Length; q++)
                    if (layY.kind[q] == LineModel.KYellow) { deckYellow = new Vector2(layY.m[q] / layY.W, 0.2f); break; }
            }
            int t0 = con.t.Count + (paint != null ? paint.t.Count : 0) + lampBucket.t.Count + (deckSlab != con ? deckSlab.t.Count : 0);

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
                // a brick deck's punched screen up to the floor above (none over the roof)
                float yAbove = CityDecks.TurnLo(k + 1);
                System.Func<Vector2, float> up = k < top ? (_ => yAbove) : (System.Func<Vector2, float>)null;
                DeckWall(con, d, tm, new Vector2(-hu, -hv), new Vector2(x0, -hv), new Vector2(0f, -1f), yf, skirt, low, d.entrySide == -1 ? oa : float.MaxValue, ob, up, k == top);
                DeckWall(con, d, tm, new Vector2(-hu, hv), new Vector2(x0, hv), new Vector2(0f, 1f), yf, skirt, low, d.entrySide == 1 ? oa : float.MaxValue, ob, up, k == top);
                DeckWall(con, d, tm, new Vector2(-hu, -hv), new Vector2(-hu, hv), new Vector2(-1f, 0f), yf, skirt, low, d.entrySide == 0 ? oa : float.MaxValue, ob, up, k == top);
            }
            for (int k = 0; k < top; k++)
            {
                int kk = k; float yh = CityDecks.TurnHi(k); bool skirt = k == 0;
                bool roof = k == top - 1; float yh1 = CityDecks.TurnHi(k + 1);
                System.Func<Vector2, float> upH = roof ? null : (System.Func<Vector2, float>)(_ => yh1);
                System.Func<Vector2, float> upA = roof ? null : (System.Func<Vector2, float>)(p => CityDecks.BayA(d, kk + 1, p.x));
                System.Func<Vector2, float> upB = roof ? null : (System.Func<Vector2, float>)(p => CityDecks.BayB(d, kk + 1, p.x));
                DeckWall(con, d, tm, new Vector2(x1, -hv), new Vector2(hu, -hv), new Vector2(0f, -1f), _ => yh, skirt, low, float.MaxValue, 0f, upH, roof);
                DeckWall(con, d, tm, new Vector2(x1, hv), new Vector2(hu, hv), new Vector2(0f, 1f), _ => yh, skirt, low, float.MaxValue, 0f, upH, roof);
                DeckWall(con, d, tm, new Vector2(hu, -hv), new Vector2(hu, hv), new Vector2(1f, 0f), _ => yh, skirt, low, float.MaxValue, 0f, upH, roof);
                DeckWall(con, d, tm, new Vector2(x0, -hv), new Vector2(x1, -hv), new Vector2(0f, -1f), p => CityDecks.BayA(d, kk, p.x), skirt, low, float.MaxValue, 0f, upA, roof);
                DeckWall(con, d, tm, new Vector2(x0, hv), new Vector2(x1, hv), new Vector2(0f, 1f), p => CityDecks.BayB(d, kk, p.x), skirt, low, float.MaxValue, 0f, upB, roof);
            }
            // the roof's turning bay ends in a drop over bay A's last run: a parapet
            DeckWall(con, d, tm, new Vector2(x0 + W, -hv + W), new Vector2(x0 + W, -S), new Vector2(1f, 0f), _ => CityDecks.TurnLo(top), false, low, float.MaxValue, 0f, null, false);

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

            if (deckBrick != null) DeckTower(con, d, tm, top, low);

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
                        // never in the street opening (the columns flank it):
                        // one standing floor to slab there pinned the AI on
                        // 500204485's way out
                        if (k == 0 && d.entrySide == s && Mathf.Abs(x - d.entryAt) < half + CityDecks.ColumnM) continue;
                        // floor to slab, standing in front of the parapet: from
                        // inside a column at the back of every third stall
                        float c0 = f0 - 0.03f, c1 = f1 - CityDecks.SlabM;
                        if (c1 - c0 < 0.3f) continue;
                        float zo = s * (hv - W), zi = s * (hv - W - CityDecks.ColumnM), xa = x - CityDecks.ColumnM * 0.5f, xb = x + CityDecks.ColumnM * 0.5f;
                        DeckBoxSides(con, d, tm, xa, xb, Mathf.Min(zo, zi), Mathf.Max(zo, zi), c0, c1, true);
                        if (deckBrick != null) continue;    // brick piers: no precast joint
                        // the precast spandrel's joint on the street face, at
                        // the column it hangs from (dark, the lamp mesh)
                        // 12 cm: the 6 cm reveal fell under a pixel at
                        // street range and the band read as one stripe
                        float jb = k == 0 ? low : f0 - CityDecks.SlabM, jt = f0 + P;
                        var jo = s * hv + s * 0.02f;
                        Dark(tm, DP(d, x - 0.06f, jb, jo, tm), DP(d, x - 0.06f, jt, jo, tm), DP(d, x + 0.06f, jt, jo, tm), DP(d, x + 0.06f, jb, jo, tm),
                                        new Vector3(DirW(d, 0f, s).x, 0f, DirW(d, 0f, s).y));
                    }
                }
            // ...the ends' pilasters on the same pitch, parapet top to slab IN
            // the wall's own thickness (the turning bays drive along these
            // walls: nothing stands proud inside), 5 cm proud outside, with
            // the spandrel's joint under each: the end face read as bare
            // grey and dark stripes
            foreach (var (xe, xw, lo) in new[] { (-hu, -hu + W, true), (hu, hu - W, false) })
            {
                float sg = lo ? -1f : 1f, xo = xe + sg * 0.05f;
                for (float z = -hv + 0.8f; z <= hv - 0.8f; z += CityDecks.ColumnPitchM)
                    for (int k = 0; ; k++)
                    {
                        float f0, f1;
                        if (lo) { if (k >= top) break; f0 = k == 0 ? CityDecks.GroundBayLocal(d, xw, z) : CityDecks.TurnLo(k); f1 = CityDecks.TurnLo(k + 1); }
                        else { if (k >= top - 1) break; f0 = CityDecks.TurnHi(k); f1 = CityDecks.TurnHi(k + 1); }
                        if (lo && k == 0 && d.entrySide == 0 && Mathf.Abs(z - d.entryAt) < half + CityDecks.ColumnM) continue;
                        float c0 = f0 + P, c1 = f1 - CityDecks.SlabM;
                        if (c1 - c0 < 0.3f) continue;
                        DeckBoxSides(con, d, tm, Mathf.Min(xo, xw), Mathf.Max(xo, xw), z - CityDecks.ColumnM * 0.5f, z + CityDecks.ColumnM * 0.5f, c0, c1, true);
                        if (deckBrick != null) continue;
                        float jb = k == 0 ? low : f0 - CityDecks.SlabM, jx = xe + sg * 0.02f;
                        var nn = new Vector3(DirW(d, sg, 0f).x, 0f, DirW(d, sg, 0f).y);
                        Dark(tm, DP(d, jx, jb, z - 0.06f, tm), DP(d, jx, c0, z - 0.06f, tm), DP(d, jx, c0, z + 0.06f, tm), DP(d, jx, jb, z + 0.06f, tm), nn);
                    }
            }
            // ...and the column line's twin on the spine, both faces, under
            // every covered run: the grid reads down the aisle
            for (int s = -1; s <= 1; s += 2)
                for (float x = -hu + 0.8f; x <= hu - 0.8f; x += CityDecks.ColumnPitchM)
                {
                    if (x < x0 + 0.5f || x > x1 - 0.5f) continue;
                    for (int k = 0; k < top - 1; k++)
                    {
                        float f0 = s < 0 ? CityDecks.BayA(d, k, x) : CityDecks.BayB(d, k, x);
                        float zs = s * S, zc = s * (S + CityDecks.ColumnM * 0.8f);
                        DeckBoxSides(con, d, tm, x - CityDecks.ColumnM * 0.5f, x + CityDecks.ColumnM * 0.5f, Mathf.Min(zs, zc), Mathf.Max(zs, zc),
                                     f0 - 0.05f, f0 + H - CityDecks.SlabM);
                    }
                }

            // ---- stall lines: 90 degree stalls down both sides of each bay's aisle ----
            // One texel of the white column at every corner (no UV slope, so
            // mip 0 and no asphalt bled in: the 0..0.45 run along the line
            // sampled a coarser mip at the grazing aisle view and read grey),
            // and 15 cm, not the lots' 10: in the shade under a slab the
            // thinner line fell under a pixel a few stalls down the aisle.
            // YELLOW, the tw2 profile's centre-line column: the deck's pack
            // concrete is pale (near white on the sunlit roof), and white
            // paint on it measured no contrast - on asphalt the lots' white
            // reads, here only a hue does.
            // On the slab (leftovers-b) the lines are the lots' WHITE, as a
            // real deck paints them on its sealed floor: the column's middle,
            // its full width (both texels) clear of the asphalt either side.
            if (paint != null)
            {
                var uw = new Vector2(0.5f * (stallU0 + stallU1), 0.2f);
                var lay = LineModel.LayoutOf(stallSlot);
                if (deckSlab == con)
                    for (int q = 0; q < lay.m.Length; q++)
                        if (lay.kind[q] == LineModel.KYellow) { uw = new Vector2(lay.m[q] / lay.W, 0.2f); break; }
                for (int k = 0; k < top; k++)
                    for (int s = -1; s <= 1; s += 2)
                        for (float x = StallStart(-hu + 0.8f, x0 + 1.5f); x <= x1 - 1.5f; x += StallW)
                        {
                            float y = (s < 0 ? CityDecks.BayA(d, k, x) : CityDecks.BayB(d, k, x)) + 0.02f;
                            foreach (float zIn in new[] { s * (hv - W), s * (S + StallD) })
                            {
                                float za = zIn, zb = zIn - s * StallD;
                                float xa = x - DeckLineW * 0.5f, xb = x + DeckLineW * 0.5f;
                                paint.Up(DP(d, xa, y, za, tm), DP(d, xb, y, za, tm), DP(d, xb, y, zb, tm), DP(d, xa, y, zb, tm), uw, uw, uw, uw);
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

            // ---- light: strip fixtures hung from every covered ceiling, one
            // between each pair of columns (a column bay, ~8.2 m) over both
            // stall rows and the aisle; poles on the roof. A fixture is a
            // shallow concrete housing with its lens the stall paint's white
            // (no new draw): a pale panel on the grey ceiling by day. The
            // aisle row's lenses are lamps too (at most 24 a deck, the
            // field's budget), lit at night - halo and the lamp field ----
            {
                const float FixL = 1.25f, FixW = 0.3f, FixDrop = 0.12f;
                var lw = paint != null ? new Vector2(0.5f * (stallU0 + stallU1), 0.2f) : new Vector2(0.5f, 0.5f);
                var lens = paint ?? con;
                float pitch = CityDecks.ColumnPitchM, rowOut = hv - W - StallD * 0.5f, rowIn = S + StallD * 0.5f;
                float aisle = 0.5f * ((hv - W - StallD) + (S + StallD));
                var aisleLamps = new List<Lamp>();
                for (int k = 0; k < top - 1; k++)
                    for (int s = -1; s <= 1; s += 2)
                        for (float x = -hu + 0.8f + pitch * 0.5f; x <= x1 - 1.5f; x += pitch)
                        {
                            if (x < x0 + 1.5f) continue;
                            float f = s < 0 ? CityDecks.BayA(d, k, x) : CityDecks.BayB(d, k, x);
                            float ceil = f + H - CityDecks.SlabM, yb = ceil - FixDrop;
                            foreach (float zr in new[] { rowOut, aisle, rowIn })
                            {
                                float z = s * zr, xa = x - FixL * 0.5f, xb = x + FixL * 0.5f, za = z - FixW * 0.5f, zb = z + FixW * 0.5f;
                                lens.Down(DP(d, xa, yb, za, tm), DP(d, xb, yb, za, tm), DP(d, xb, yb, zb, tm), DP(d, xa, yb, zb, tm), lw, lw, lw, lw);
                                // the housing: a dark metal frame round the
                                // lens (a white lens alone on pale concrete in
                                // the shade measured +20 of 255) and its sides
                                // up to the slab
                                const float Rim = 0.09f; float yr = yb + 0.01f;
                                var mm = new Vector2(0.5f, 0.5f);
                                DarkDown(tm, DP(d, xa - Rim, yr, za - Rim, tm), DP(d, xb + Rim, yr, za - Rim, tm),
                                         DP(d, xb + Rim, yr, zb + Rim, tm), DP(d, xa - Rim, yr, zb + Rim, tm), mm, mm, mm, mm);
                                DarkBox(d, tm, xa - Rim, xb + Rim, za - Rim, zb + Rim, yr, ceil + 0.02f);
                                if (zr == aisle)
                                    aisleLamps.Add(new Lamp { foot = DP(d, x, f, z, tm), head = DP(d, x, yb - 0.05f, z, tm), height = yb - f, kind = LampDeck, breakaway = true });
                            }
                        }
                // 24 a deck: the lamp field is flat (x/z), so every floor's
                // lights pile onto the same texels - 36 at gain 0.6 burnt the
                // night interior white
                int stride = Mathf.Max(1, Mathf.CeilToInt(aisleLamps.Count / 24f));
                for (int i = 0; i < aisleLamps.Count; i += stride) tm.lamps.Add(aisleLamps[i]);
                // roof poles along both long parapets (bay A's and bay B's last runs)
                const float PoleH = 7f;
                for (int s = -1; s <= 1; s += 2)
                    for (float x = x0 + 4f; x <= x1 - 4f + 0.01f; x += Mathf.Max(12f, (x1 - x0 - 8f) / 2f))
                    {
                        float f = s < 0 ? CityDecks.BayA(d, top - 1, x) : CityDecks.BayB(d, top - 1, x);
                        float z = s * (hv - W - 0.35f);
                        DarkBox(d, tm, x - 0.12f, x + 0.12f, z - 0.12f, z + 0.12f, f, f + PoleH);
                        float zh = z - s * 0.8f;
                        DarkDown(tm, DP(d, x - 0.35f, f + PoleH - 0.1f, zh - 0.5f, tm), DP(d, x + 0.35f, f + PoleH - 0.1f, zh - 0.5f, tm),
                                        DP(d, x + 0.35f, f + PoleH - 0.1f, zh + 0.5f, tm), DP(d, x - 0.35f, f + PoleH - 0.1f, zh + 0.5f, tm),
                                        new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                        tm.lamps.Add(new Lamp { foot = DP(d, x, f, z, tm), head = DP(d, x, f + PoleH - 0.15f, zh, tm), height = PoleH, kind = LampDeck, breakaway = true });
                    }
            }

            d.trisDeck = (con.t.Count + (paint != null ? paint.t.Count : 0) + lampBucket.t.Count + (deckSlab != con ? deckSlab.t.Count : 0) - t0) / 3;
            if (!d.built) { d.built = true; DecksBuilt++; }
            CityDecks.AnyBuilt = true;
            Bucket.Tint = keepTint; deckBrick = null; deckPaint = null;
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
                    deckSlab.Up(DP(d, a, CityDecks.GroundBayLocal(d, a, c), c, tm), DP(d, b, CityDecks.GroundBayLocal(d, b, c), c, tm),
                           DP(d, b, CityDecks.GroundBayLocal(d, b, e), e, tm), DP(d, a, CityDecks.GroundBayLocal(d, a, e), e, tm),
                           FUV(d, a, c, con), FUV(d, b, c, con), FUV(d, b, e, con), FUV(d, a, e, con));
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
                deckSlab.Up(DP(d, a, ya, za, tm), DP(d, b, yb, za, tm), DP(d, b, yb, zb, tm), DP(d, a, ya, zb, tm),
                       FUV(d, a, za, con), FUV(d, b, za, con), FUV(d, b, zb, con), FUV(d, a, zb, con));
                if (soffit)
                {
                    // the ceiling is the slab's underside: the same pack
                    // concrete as its top (the owner, 2026-10-05: a deck's
                    // ceilings are concrete). Facing down it takes no sun,
                    // so it reads a shade under the floor - a grey ceiling
                    // in the open bands from the street, not the black void
                    // the dark cell made, which swallowed the columns. Also
                    // in the collider-only bucket: the chase camera's cover
                    // ray looks up for it
                    var p0 = DP(d, a, ya - sl, za, tm); var p1 = DP(d, b, yb - sl, za, tm);
                    var p2 = DP(d, b, yb - sl, zb, tm); var p3 = DP(d, a, ya - sl, zb, tm);
                    var m = new Vector2(0.5f, 0.5f);
                    con.Down(p0, p1, p2, p3, DUV(d, a, za), DUV(d, b, za), DUV(d, b, zb), DUV(d, a, zb));
                    guardBucket.Down(p0, p1, p2, p3, m, m, m, m);
                }
            }
        }

        /// <summary>A parapet along the outer line from A to B (local), the
        /// wall <see cref="CityDecks.WallM"/> thick inside it, outward
        /// <paramref name="outL"/>: inner face floor to parapet top, outer face
        /// slab bottom (or the skirt's low) to the top, the cap. Pieces whose
        /// middle falls in [oa, ob] (along the run's own axis) are the opening:
        /// only the skirt below the sill.</summary>
        static void DeckWall(Bucket con, CityDecks.Deck d, TileMeshes tm, Vector2 A, Vector2 B, Vector2 outL,
                             System.Func<Vector2, float> yf, bool skirt, float low, float oa, float ob,
                             System.Func<Vector2, float> yUp = null, bool roof = false)
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
                bool opening = mid > oa && mid < ob;
                if (deckBrick != null && yUp != null) BrickScreen(con, d, tm, A, a, b, inV, oW, alongX, yf, yUp, i == 0, i == n - 1, oa, ob, opening);
                if (deckBrick != null && roof && !opening) Cornice(con, d, tm, a, b, inV, oW, outL, ya + P, yb + P);
                if (opening)
                {
                    con.WallSloped(DP(d, a.x, 0f, a.y, tm), DP(d, b.x, 0f, b.y, tm), d.y0 + (skirt ? low : ya + bot), d.y0 + ya, d.y0 + (skirt ? low : yb + bot), d.y0 + yb,
                                   oW, ua, ub, 0f, 0.3f);
                    continue;
                }
                float ba = skirt ? low : ya + bot, bb = skirt ? low : yb + bot;
                if (deckBrick != null)
                {
                    // cast stone from the slab's soffit to StoneBandM over the
                    // floor, brick over it to the coping. A SKIRT (the ground
                    // floor's runs, down to the lowest land) is cast stone from
                    // its foot to the band's top, one plinth: the land meets the
                    // wall 6-35 cm under the ground floor (Solve seats floor 0 at
                    // least 6 cm over every land sample), right where a brick
                    // skirt under the band put its brick/stone joint, and the
                    // land's facets crossing that joint drew the sawtooth of
                    // brick and stone triangles along the base (2026-10-06).
                    float sa = ya + StoneBandM, sb = yb + StoneBandM, ta = ya - CityDecks.SlabM, tb = yb - CityDecks.SlabM;
                    if (skirt) { ta = Mathf.Min(ta, ba); tb = Mathf.Min(tb, bb); }
                    // ...and that skin stands SkinProudM proud of the footprint
                    // line: a lot's or verge's edge face laid along the same line
                    // (the sheets stop at a building's footprint) shared its
                    // plane, and the two fought in triangles, the grey sheet face
                    // winning most of the brick band
                    Vector2 sk = outL * SkinProudM, ao = a + sk, bo = b + sk;
                    con.WallSloped(DP(d, ao.x, 0f, ao.y, tm), DP(d, bo.x, 0f, bo.y, tm), d.y0 + ta, d.y0 + sa, d.y0 + tb, d.y0 + sb,
                                   oW, ua, ub, ta * 0.25f, sa * 0.25f);
                    BrickBand(d, tm, ao, bo, oW, sa, ya + P, sb, yb + P);
                }
                else
                con.WallSloped(DP(d, a.x, 0f, a.y, tm), DP(d, b.x, 0f, b.y, tm), d.y0 + ba, d.y0 + ya + P, d.y0 + bb, d.y0 + yb + P,
                               oW, ua, ub, ba * 0.25f, (ya + P) * 0.25f);
                Vector2 ai = a + inV, bi = b + inV;
                con.WallSloped(DP(d, ai.x, 0f, ai.y, tm), DP(d, bi.x, 0f, bi.y, tm), d.y0 + ya, d.y0 + ya + P, d.y0 + yb, d.y0 + yb + P,
                               -oW, ua, ub, ya * 0.25f, (ya + P) * 0.25f);
                con.Up(DP(d, a.x, ya + P, a.y, tm), DP(d, b.x, yb + P, b.y, tm), DP(d, bi.x, yb + P, bi.y, tm), DP(d, ai.x, ya + P, ai.y, tm),
                       DUV(d, a.x, a.y), DUV(d, b.x, b.y), DUV(d, bi.x, bi.y), DUV(d, ai.x, ai.y));
            }
        }

        const float PierW = 0.6f, PierPitchM = CityDecks.ColumnPitchM / 4f, HeaderM = 0.18f, ClearM = 2.49f;

        /// <summary>A brick deck's open band between a parapet and the slab
        /// above, as the owner's photos show it: PUNCHED openings, a brick pier
        /// every stall (three per column bay, on the column grid's phase, one at
        /// each run's ends) the wall's depth deep, and a brick header under the
        /// floor above's cast-stone band. Over the street opening only the header
        /// (its soffit 2.55 m up, over the 8 ft 2 in bar), the yellow-striped
        /// clearance bar under it and a sign plate on the header's face.</summary>
        static void BrickScreen(Bucket con, CityDecks.Deck d, TileMeshes tm, Vector2 A, Vector2 a, Vector2 b, Vector2 inV, Vector2 oW,
                                bool alongX, System.Func<Vector2, float> yf, System.Func<Vector2, float> yUp, bool first, bool last, float oa, float ob, bool opening)
        {
            float P = CityDecks.ParapetM;
            float ta = yUp(a) - CityDecks.SlabM, tb = yUp(b) - CityDecks.SlabM, ha = ta - HeaderM, hb = tb - HeaderM;
            Vector2 ai = a + inV, bi = b + inV;
            BrickBand(d, tm, a, b, oW, ha, ta, hb, tb);
            con.WallSloped(DP(d, ai.x, 0f, ai.y, tm), DP(d, bi.x, 0f, bi.y, tm), d.y0 + ha, d.y0 + ta, d.y0 + hb, d.y0 + tb, -oW, 0f, 0.3f, 0f, 0.05f);
            con.Face(DP(d, a.x, ha, a.y, tm), DP(d, b.x, hb, b.y, tm), DP(d, bi.x, hb, bi.y, tm), DP(d, ai.x, ha, ai.y, tm), Vector3.down,
                     DUV(d, a.x, a.y), DUV(d, b.x, b.y), DUV(d, bi.x, bi.y), DUV(d, ai.x, ai.y));
            float ca = alongX ? a.x : a.y, cb = alongX ? b.x : b.y, lo = Mathf.Min(ca, cb), hi = Mathf.Max(ca, cb);
            var n3 = new Vector3(oW.x, 0f, oW.y);
            if (opening)
            {
                // the clearance bar: 15 cm, half the wall's depth in from the
                // face, 0.4 m stripes of the lines' yellow and black
                var o = inV * 0.5f;
                float yb0 = Mathf.Max(yf(a), yf(b)) + ClearM;
                int ns = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(a, b) / 0.4f));
                for (int j = 0; j < ns; j++)
                {
                    var p0 = Vector2.Lerp(a, b, j / (float)ns) + o; var p1 = Vector2.Lerp(a, b, (j + 1) / (float)ns) + o;
                    var q0 = DP(d, p0.x, yb0, p0.y, tm); var q1 = DP(d, p1.x, yb0, p1.y, tm);
                    var r0 = DP(d, p0.x, yb0 + 0.15f, p0.y, tm); var r1 = DP(d, p1.x, yb0 + 0.15f, p1.y, tm);
                    if ((j & 1) == 0 && deckPaint != null)
                    {
                        deckPaint.Face(q0, q1, r1, r0, n3, deckYellow, deckYellow, deckYellow, deckYellow);
                        deckPaint.Face(q1, q0, r0, r1, -n3, deckYellow, deckYellow, deckYellow, deckYellow);
                    }
                    else { Dark(tm, q0, q1, r1, r0, n3); Dark(tm, q1, q0, r0, r1, -n3); }
                }
                // the sign beam's plate on the header's face (no text)
                var so = -inV.normalized * 0.03f;
                Dark(tm, DP(d, a.x + so.x, ha + 0.02f, a.y + so.y, tm), DP(d, b.x + so.x, hb + 0.02f, b.y + so.y, tm),
                         DP(d, b.x + so.x, tb + 0.42f, b.y + so.y, tm), DP(d, a.x + so.x, ta + 0.42f, a.y + so.y, tm), n3);
                return;
            }
            float ph = (alongX ? -d.hu : -d.hv) + 0.8f;
            for (float c = ph + Mathf.Ceil((lo - ph) / PierPitchM) * PierPitchM; c < hi; c += PierPitchM)
                ScreenPier(d, tm, A, inV, oW, alongX, yf, yUp, c, oa, ob);
            if (first) ScreenPier(d, tm, A, inV, oW, alongX, yf, yUp, ca < cb ? ca + PierW * 0.5f : ca - PierW * 0.5f, oa, ob);
            if (last) ScreenPier(d, tm, A, inV, oW, alongX, yf, yUp, cb > ca ? cb - PierW * 0.5f : cb + PierW * 0.5f, oa, ob);
        }

        /// <summary>One brick pier of <see cref="BrickScreen"/> at <paramref name="c"/>
        /// along its run: parapet top to the header, the wall's depth deep.</summary>
        static void ScreenPier(CityDecks.Deck d, TileMeshes tm, Vector2 A, Vector2 inV, Vector2 oW, bool alongX,
                               System.Func<Vector2, float> yf, System.Func<Vector2, float> yUp, float c, float oa, float ob)
        {
            if (c > oa - PierW && c < ob + PierW) return;
            Vector2 Pt(float cc) => alongX ? new Vector2(cc, A.y) : new Vector2(A.x, cc);
            var pm = Pt(c);
            float y0 = yf(pm) + CityDecks.ParapetM, y1 = yUp(pm) - CityDecks.SlabM - HeaderM;
            if (y1 - y0 < 0.3f) return;
            Vector2 p0 = Pt(c - PierW * 0.5f), p1 = Pt(c + PierW * 0.5f), i0 = p0 + inV, i1 = p1 + inV;
            float Y0 = d.y0 + y0, Y1 = d.y0 + y1, w0 = y0 / BrickUM, w1 = y1 / BrickUM;
            var ax = alongX ? DirW(d, 1f, 0f) : DirW(d, 0f, 1f);
            deckBrick.Wall(DP(d, p0.x, 0f, p0.y, tm), DP(d, p1.x, 0f, p1.y, tm), Y0, Y1, oW, 0.30f, 0.38f, w0, w1);
            deckBrick.Wall(DP(d, i0.x, 0f, i0.y, tm), DP(d, i1.x, 0f, i1.y, tm), Y0, Y1, -oW, 0.30f, 0.38f, w0, w1);
            deckBrick.Wall(DP(d, p0.x, 0f, p0.y, tm), DP(d, i0.x, 0f, i0.y, tm), Y0, Y1, -ax, 0.30f, 0.33f, w0, w1);
            deckBrick.Wall(DP(d, p1.x, 0f, p1.y, tm), DP(d, i1.x, 0f, i1.y, tm), Y0, Y1, ax, 0.30f, 0.33f, w0, w1);
        }

        /// <summary>The roof parapet's cast-stone cornice: a coping 10 cm proud,
        /// 0.36 m down the face, its soffit and its top.</summary>
        static void Cornice(Bucket con, CityDecks.Deck d, TileMeshes tm, Vector2 a, Vector2 b, Vector2 inV, Vector2 oW, Vector2 outL, float ya, float yb)
        {
            var ao = a + outL * 0.1f; var bo = b + outL * 0.1f; var ai = a + inV; var bi = b + inV;
            float ua = (a.x + a.y) * 0.25f, ub = ua + Vector2.Distance(a, b) * 0.25f;
            con.WallSloped(DP(d, ao.x, 0f, ao.y, tm), DP(d, bo.x, 0f, bo.y, tm), d.y0 + ya - 0.3f, d.y0 + ya + 0.06f, d.y0 + yb - 0.3f, d.y0 + yb + 0.06f, oW, ua, ub, 0f, 0.09f);
            con.Face(DP(d, ao.x, ya - 0.3f, ao.y, tm), DP(d, bo.x, yb - 0.3f, bo.y, tm), DP(d, b.x, yb - 0.3f, b.y, tm), DP(d, a.x, ya - 0.3f, a.y, tm), Vector3.down,
                     DUV(d, ao.x, ao.y), DUV(d, bo.x, bo.y), DUV(d, b.x, b.y), DUV(d, a.x, a.y));
            con.Up(DP(d, ao.x, ya + 0.06f, ao.y, tm), DP(d, bo.x, yb + 0.06f, bo.y, tm), DP(d, bi.x, yb + 0.06f, bi.y, tm), DP(d, ai.x, ya + 0.06f, ai.y, tm),
                   DUV(d, ao.x, ao.y), DUV(d, bo.x, bo.y), DUV(d, bi.x, bi.y), DUV(d, ai.x, ai.y));
        }

        /// <summary>The brick decks' stair tower (the owner's photos: a tower on
        /// the street face beside the way in, a glazed opening at every floor,
        /// cast-stone bands, an arched top over the roof): 5.6 m wide, 3.2 m out
        /// from the face, brick strips of the atlas's plain pier, solid like the
        /// walls. It stands on the ENTRY face (the -x end or the +z side), the
        /// one the street sees: on the +z side always (2026-10-06) it stood on
        /// the Epic Ln deck's back, where no street photo shows it. Laid out in
        /// the face's own frame, a along it and o out from the deck's centre.</summary>
        static void DeckTower(Bucket con, CityDecks.Deck d, TileMeshes tm, int top, float low)
        {
            const float TW = 5.6f, TD = 3.2f;
            bool endFace = d.entrySide == 0;
            float faceD = endFace ? d.hu : d.hv, ext = endFace ? d.hv : d.hu;
            // the face frame: +z side a = x, o = z; -x end a = z, o = -x (a turn, not a mirror)
            Vector3 TP(float a, float y, float o) => endFace ? DP(d, -o, y, a, tm) : DP(d, a, y, o, tm);
            Vector2 TN(float la, float lo) => endFace ? DirW(d, -lo, la) : DirW(d, la, lo);
            Vector2 TUV(float a, float o) => endFace ? DUV(d, -o, a) : DUV(d, a, o);
            // central when the opening leaves room, else beside the opening,
            // on its roomier side (and clear of its driveway)
            float clear = CityDecks.OpeningM * 0.5f + TW * 0.5f + 1.5f, xc = 0f;
            if (Mathf.Abs(d.entryAt) < clear)
            {
                float sg = d.entryAt <= 0f ? 1f : -1f;
                xc = d.entryAt + sg * clear;
                if (Mathf.Abs(xc) + TW * 0.5f > ext - 1f) xc = d.entryAt - sg * clear;
            }
            if (Mathf.Abs(xc) + TW * 0.5f > ext - 1f) return;
            float zf = faceD + TD, zb = faceD, xa = xc - TW * 0.5f, xb = xc + TW * 0.5f;
            float yTop = CityDecks.TurnLo(top) + CityDecks.ParapetM + 2.2f, r = TW * 0.5f;
            var nF = TN(0f, 1f); var nL = TN(-1f, 0f); var nR = TN(1f, 0f);
            float w0 = low / BrickUM, w1 = yTop / BrickUM;
            void Strip(Vector2 p, Vector2 q, Vector2 n)
            {
                int m = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(p, q) / 1f));
                for (int j = 0; j < m; j++)
                {
                    var s0 = Vector2.Lerp(p, q, j / (float)m); var s1 = Vector2.Lerp(p, q, (j + 1) / (float)m);
                    deckBrick.Wall(TP(s0.x, 0f, s0.y), TP(s1.x, 0f, s1.y), d.y0 + low, d.y0 + yTop, n, 0.30f, 0.38f, w0, w1);
                }
            }
            Strip(new Vector2(xa, zf), new Vector2(xb, zf), nF);
            Strip(new Vector2(xa, zb), new Vector2(xa, zf), nL);
            Strip(new Vector2(xb, zb), new Vector2(xb, zf), nR);
            var n3 = new Vector3(nF.x, 0f, nF.y);
            for (int k = 0; k <= top; k++)
            {
                float y = CityDecks.TurnLo(k) + (k == 0 ? 0.9f : 1.1f);
                // the glazed opening (dark, a mullion's gap down the middle)
                float zo = zf + 0.03f;
                Dark(tm, TP(xc - 0.85f, y, zo), TP(xc - 0.06f, y, zo), TP(xc - 0.06f, y + 1.7f, zo), TP(xc - 0.85f, y + 1.7f, zo), n3);
                Dark(tm, TP(xc + 0.06f, y, zo), TP(xc + 0.85f, y, zo), TP(xc + 0.85f, y + 1.7f, zo), TP(xc + 0.06f, y + 1.7f, zo), n3);
                // a cast-stone band at the floor line, 4 cm proud
                if (k == 0) continue;
                float yb = CityDecks.TurnLo(k) - CityDecks.SlabM, ys = CityDecks.TurnLo(k) + StoneBandM;
                con.Wall(TP(xa - 0.04f, 0f, zf + 0.04f), TP(xb + 0.04f, 0f, zf + 0.04f), d.y0 + yb, d.y0 + ys, nF, 0f, TW * 0.25f, 0f, 0.12f);
                con.Up(TP(xa - 0.04f, ys, zf + 0.04f), TP(xb + 0.04f, ys, zf + 0.04f), TP(xb, ys, zf), TP(xa, ys, zf),
                       TUV(xa, zf), TUV(xb, zf), TUV(xb, zf + 0.04f), TUV(xa, zf + 0.04f));
            }
            // the arched top: a half round over the front and back, a stone barrel between
            const int AS = 8;
            var cF = TP(xc, yTop, zf); var cB = TP(xc, yTop, zb);
            var nB = -n3;
            for (int j = 0; j < AS; j++)
            {
                float t0 = Mathf.PI * j / AS, t1 = Mathf.PI * (j + 1) / AS;
                float x0 = xc + r * Mathf.Cos(t0), y0 = yTop + r * Mathf.Sin(t0), x1 = xc + r * Mathf.Cos(t1), y1 = yTop + r * Mathf.Sin(t1);
                Vector2 U(float x, float yy) => new Vector2(0.30f + 0.08f * (x - xa) / TW, yy / BrickUM);
                var f0 = TP(x0, y0, zf); var f1 = TP(x1, y1, zf);
                deckBrick.Face(cF, f0, f1, f1, n3, U(xc, yTop), U(x0, y0), U(x1, y1), U(x1, y1));
                var g0 = TP(x0, y0, zb); var g1 = TP(x1, y1, zb);
                deckBrick.Face(cB, g0, g1, g1, nB, U(xc, yTop), U(x0, y0), U(x1, y1), U(x1, y1));
                var mid = new Vector3(Mathf.Cos(0.5f * (t0 + t1)), Mathf.Sin(0.5f * (t0 + t1)), 0f);
                var nOut = TN(mid.x, 0f); var nUp = new Vector3(nOut.x, mid.y, nOut.y);
                var f0e = TP(x0, y0 + 0.08f * Mathf.Sin(t0), zf + 0.06f); var f1e = TP(x1, y1 + 0.08f * Mathf.Sin(t1), zf + 0.06f);
                con.Face(TP(x0, y0, zb), TP(x1, y1, zb), f1e, f0e, nUp, TUV(x0, zb), TUV(x1, zb), TUV(x1, zf), TUV(x0, zf));
            }
        }

        /// <summary>The first stall line at or past <paramref name="from"/> on
        /// the column grid's phase (<see cref="CityDecks.ColumnPitchM"/> is
        /// three stalls): every column stands on a stall line.</summary>
        static float StallStart(float phase, float from) => phase + Mathf.Ceil((from - phase) / StallW) * StallW;

        /// <summary>A dark quad (a-b along the bottom, d-c along the top) facing
        /// <paramref name="n"/>: into the Lamps mesh AND <see cref="TileMeshes.deckDark"/>,
        /// because a tile's furniture (CityPoles) destroys the Lamps mesh and
        /// draws the lamps itself - the deck's fixtures and roof poles were
        /// never seen, only a street post under each.</summary>
        static void Dark(TileMeshes tm, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        {
            var m = new Vector2(0.5f, 0.5f);
            lampBucket.Face(a, b, c, d, n, m, m, m, m);
            tm.deckDark.Add(a); tm.deckDark.Add(b); tm.deckDark.Add(c); tm.deckDark.Add(d); tm.deckDark.Add(n);
        }

        static void DarkDown(TileMeshes tm, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            => Dark(tm, a, b, c, d, Vector3.down);

        static void DarkBox(CityDecks.Deck d, TileMeshes tm, float xa, float xb, float za, float zb, float y0, float y1)
        {
            Vector3 N(float lx, float lz) { var w = DirW(d, lx, lz); return new Vector3(w.x, 0f, w.y); }
            Dark(tm, DP(d, xa, y0, za, tm), DP(d, xb, y0, za, tm), DP(d, xb, y1, za, tm), DP(d, xa, y1, za, tm), N(0f, -1f));
            Dark(tm, DP(d, xa, y0, zb, tm), DP(d, xb, y0, zb, tm), DP(d, xb, y1, zb, tm), DP(d, xa, y1, zb, tm), N(0f, 1f));
            Dark(tm, DP(d, xa, y0, za, tm), DP(d, xa, y0, zb, tm), DP(d, xa, y1, zb, tm), DP(d, xa, y1, za, tm), N(-1f, 0f));
            Dark(tm, DP(d, xb, y0, za, tm), DP(d, xb, y0, zb, tm), DP(d, xb, y1, zb, tm), DP(d, xb, y1, za, tm), N(1f, 0f));
        }

        /// <summary>The four sides of an upright box (a column, a pole), local x/z extents.</summary>
        /// <summary>A brick deck's band of brick on the street face (the facade
        /// atlas's brick column, its plain spandrel row between two window rows:
        /// atlas rows 178-197 of 256, so V 0.232-0.302; U at the look's 12.4 m).</summary>
        static void BrickBand(CityDecks.Deck d, TileMeshes tm, Vector2 a, Vector2 b, Vector2 oW, float ya0, float ya1, float yb0, float yb1)
        {
            float ua = (Mathf.Abs(b.x - a.x) > Mathf.Abs(b.y - a.y) ? a.x : a.y) / BrickUM, ub = ua + Vector2.Distance(a, b) / BrickUM;
            deckBrick.WallSloped(DP(d, a.x, 0f, a.y, tm), DP(d, b.x, 0f, b.y, tm), d.y0 + ya0, d.y0 + ya1, d.y0 + yb0, d.y0 + yb1,
                                 oW, ua, ub, BrickV0, BrickV1);
        }
        const float StoneBandM = 0.12f, BrickUM = 12.4f, BrickV0 = 0.232f, BrickV1 = 0.302f, SkinProudM = 0.04f;
        /// <summary>The facade atlas's bucket while a BRICK deck is emitted, else null.</summary>
        static Bucket deckBrick, deckPaint;
        static Vector2 deckYellow;

        /// <summary>Four sides of a column; <paramref name="perimeter"/>: on a brick
        /// deck it is a brick pier (the atlas brick column's plain pier between
        /// two window columns, U 0.30-0.38, V true to scale).</summary>
        static void DeckBoxSides(Bucket bk, CityDecks.Deck d, TileMeshes tm, float xa, float xb, float za, float zb, float y0, float y1, bool perimeter = false)
        {
            if (perimeter && deckBrick != null)
            {
                float B0 = d.y0 + y0, B1 = d.y0 + y1, w0 = y0 / BrickUM, w1 = y1 / BrickUM;
                deckBrick.Wall(DP(d, xa, 0f, za, tm), DP(d, xb, 0f, za, tm), B0, B1, DirW(d, 0f, -1f), 0.30f, 0.38f, w0, w1);
                deckBrick.Wall(DP(d, xa, 0f, zb, tm), DP(d, xb, 0f, zb, tm), B0, B1, DirW(d, 0f, 1f), 0.30f, 0.38f, w0, w1);
                deckBrick.Wall(DP(d, xa, 0f, za, tm), DP(d, xa, 0f, zb, tm), B0, B1, DirW(d, -1f, 0f), 0.30f, 0.38f, w0, w1);
                deckBrick.Wall(DP(d, xb, 0f, za, tm), DP(d, xb, 0f, zb, tm), B0, B1, DirW(d, 1f, 0f), 0.30f, 0.38f, w0, w1);
                return;
            }
            float Y0 = d.y0 + y0, Y1 = d.y0 + y1, v0 = y0 * 0.25f, v1 = y1 * 0.25f;
            bk.Wall(DP(d, xa, 0f, za, tm), DP(d, xb, 0f, za, tm), Y0, Y1, DirW(d, 0f, -1f), 0f, 0.15f, v0, v1);
            bk.Wall(DP(d, xa, 0f, zb, tm), DP(d, xb, 0f, zb, tm), Y0, Y1, DirW(d, 0f, 1f), 0f, 0.15f, v0, v1);
            bk.Wall(DP(d, xa, 0f, za, tm), DP(d, xa, 0f, zb, tm), Y0, Y1, DirW(d, -1f, 0f), 0f, 0.15f, v0, v1);
            bk.Wall(DP(d, xb, 0f, za, tm), DP(d, xb, 0f, zb, tm), Y0, Y1, DirW(d, 1f, 0f), 0f, 0.15f, v0, v1);
        }
    }
}
