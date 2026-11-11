using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// NO PLANT THROUGH A SOLID (owner 2026-11-10, on the Linn Cove Viaduct in
    /// the PC26 build: "trees/foliage/etc should not protrude through bridge,
    /// walls, rails, or cars (especially in cockpit mode)").
    ///
    /// Every plant the stage forest places is tested as a VOLUME - its height
    /// and, in eight height bands, its reach from the axis (PlantShape, off the
    /// variant's own LOD0 at its scale; a billboard's card width for a card
    /// tree) - in the road's own cross-section, at every station its crown
    /// spans, against
    ///   - the CLEARANCE ENVELOPE: lanes and shoulders (tarmac edge + 1.1 m)
    ///     from the road surface to 4.5 m, so nothing can pass through a car or
    ///     into its cabin; canopy may overhang the road above it;
    ///   - a BRIDGE DECK, its parapets and the air under it: nothing over the
    ///     deck's width higher than its soffit less UnderDeckClearM, and the
    ///     PIERS as columns;
    ///   - WALLS AND RAILS: the stone, its posts and the beam, from its foot to
    ///     the collider's top;
    ///   - a faced ROCK CUT: nothing rooted in the rock may come out through
    ///     the face below the crest (leaves running INTO rock from in front of
    ///     it are hidden, and allowed).
    /// A plant that meets one is first MOVED out from the road (up to 3 m, on
    /// ground its own rules allow), then SHORTENED (to under half its scale),
    /// then SWAPPED for a smaller kind (a beech for a young beech or a bush, a
    /// bush for ground cover), and DROPPED only when nothing fits
    /// (psx-racing-flicker-not-remove: fix, do not delete). Under a deck the
    /// forest still stands, a metre or more below the soffit.
    ///
    /// The scene audit (PlantSolidAudit, StageLab) checks the result vertex by
    /// vertex against the built colliders; this is the rule that makes it 0.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        const float PlantEnvelopeH = 4.5f;
        const float PlantEnvelopeShoulder = KerbWidth + 0.2f;
        /// <summary>Air left between a plant under a deck and its soffit (m).</summary>
        const float UnderDeckClearM = 1.0f;
        /// <summary>Slack round every solid (m).</summary>
        const float PlantSlackM = 0.15f;
        /// <summary>The farthest solid tested, past the tarmac edge (m): a deck's
        /// edge, a wall's back, a faced cut's crest.</summary>
        const float PlantReachM = 9f;
        /// <summary>A wall's stone and beam, tarmac-relative: down to its deepest
        /// foot, up to its collider's top (StageWallCollH) and a little.</summary>
        const float PlantWallBelowM = 6.3f, PlantWallTopM = 1.85f;

        internal enum PlantClass { Canopy, ExtraCanopy, SmallBeech, Bush, Cover, Debris, Ledge, CardTree, Count }
        static readonly string[] PlantClassNames = { "canopy", "extra canopy", "small beech", "bush", "cover", "debris", "ledge", "card tree" };
        internal enum PlantFit { Ok, Moved, Shortened, Swapped, Dropped }
        /// <summary>[class, 0 near the road and tested, 1+fit outcome]</summary>
        static readonly int[,] plantFit = new int[(int)PlantClass.Count, 6];
        static readonly string[] SolidNames = { "", "envelope", "deck", "wall/rail", "rock cut", "pier" };
        static readonly int[] plantFitWhat = new int[6];

        /// <summary>The piers BuildPiers stood: centre x, z, bottom y, top y.</summary>
        static readonly List<Vector4> pierSpots = new List<Vector4>();

        static void ResetPlantFit()
        {
            System.Array.Clear(plantFit, 0, plantFit.Length);
            System.Array.Clear(plantFitWhat, 0, plantFitWhat.Length);
        }

        static string PlantFitLine()
        {
            var sb = new System.Text.StringBuilder("Plants through solids, the planting rule: ");
            for (int c = 0; c < (int)PlantClass.Count; c++)
            {
                if (plantFit[c, 0] == 0) continue;
                sb.Append($"{PlantClassNames[c]} {plantFit[c, 0]} near the road: {plantFit[c, 1]} clear, {plantFit[c, 2]} moved, " +
                          $"{plantFit[c, 3]} shortened, {plantFit[c, 4]} swapped, {plantFit[c, 5]} dropped; ");
            }
            sb.Append("first met:");
            for (int k = 1; k < plantFitWhat.Length; k++) sb.Append(" " + SolidNames[k] + " " + plantFitWhat[k]);
            return sb.ToString();
        }

        /// <summary>Could a plant of this reach meet anything solid here?</summary>
        static bool PlantNearRoad(float x, float z, float reach) =>
            StageCorridor(x, z, RoadWidth * 0.5f + PlantReachM + reach, out RoadFoot _);

        /// <summary>What the plant, base at (x, y, z) and scale s, first meets:
        /// 0 nothing, 1 the envelope, 2 a deck, 3 a wall or rail, 4 rock, 5 a
        /// pier.</summary>
        static int PlantBlocked(float x, float y, float z, float s, PlantShape sh)
        {
            float R = sh.rMax * s;
            float roadHalf = RoadWidth * 0.5f;
            if (!StageCorridor(x, z, roadHalf + PlantReachM + R, out RoadFoot f)) return 0;
            int k = BlockedFrame(f, y, s, sh);
            if (k != 0) return k;
            // a switchback's other leg, a grade separation
            if (OtherRoadNear(x, z, roadHalf + PlantReachM + R, Mathf.RoundToInt(f.station), out RoadFoot g))
            {
                k = BlockedFrame(g, y, s, sh);
                if (k != 0) return k;
            }
            float pr = PierHalf * 1.415f + PlantSlackM;
            foreach (var p in pierSpots)
            {
                float dx = p.x - x, dz = p.y - z, dd = Mathf.Sqrt(dx * dx + dz * dz);
                if (dd > pr + R) continue;
                for (int b = 0; b < PlantShape.Bands; b++)
                {
                    float y0 = y + (b * sh.height / PlantShape.Bands) * s, y1 = y + ((b + 1) * sh.height / PlantShape.Bands) * s;
                    if (dd < pr + sh.r[b] * s && y1 > p.z && y0 < p.w) return 5;
                }
            }
            // AND THE PLANT ITSELF against what is built: its own vertices at its
            // yaw and tilt, against the deck, pier, wall and rock colliders and
            // the envelope - the audit's own test (PlantSolidAudit), so what
            // passes here passes there
            if (curPts != null)
            {
                var M = Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.Euler(curTilt.x, 0f, curTilt.y) * Quaternion.Euler(0f, curYaw, 0f),
                                      new Vector3(s, s, s));
                string w = PlantSolidAudit.Blocked(M, curPts, curHeight, plantEnvFloor > 0.1f);
                if (w != null)
                    return w == "envelope" ? 1 : w.StartsWith("BridgeDeck") ? 2 : w == "Pier" ? 5
                         : w.StartsWith("Bank") || w.StartsWith("WallTunnel") ? 4 : 3;
            }
            return 0;
        }

        /// <summary>The plant being fitted, as the audit takes it: its points
        /// (base at the origin, scale 1), height, yaw and tilt.</summary>
        static Vector3[] curPts;
        static float curHeight, curYaw;
        static Vector2 curTilt;

        const int PlantPoints = 320;

        static void SetCurrent(MeshForest.Variant v, float yaw, Vector2 tilt)
        {
            curPts = PlantShape.Points(v, PlantPoints);
            var sh = PlantShape.Of(v);
            curHeight = sh != null ? sh.height : 1f;
            curYaw = yaw; curTilt = tilt;
        }

        /// <summary>A billboard tree's points, as the audit samples a card: the
        /// inner 70% of each plane's width, from 15% of its height up.</summary>
        static void SetCurrentCard(float h, float width, float yaw)
        {
            var pts = new List<Vector3>();
            for (int q = 0; q < 2; q++)
            {
                var rot = Quaternion.Euler(0f, yaw + q * 90f, 0f);
                Vector3 a = rot * new Vector3(-width * 0.5f, 0f, 0f), b = rot * new Vector3(width * 0.5f, 0f, 0f);
                for (int gy = 0; gy <= 4; gy++)
                    for (int gx = 0; gx <= 6; gx++)
                        pts.Add(Vector3.Lerp(a, b, Mathf.Lerp(0.15f, 0.85f, gx / 6f)) + Vector3.up * (Mathf.Lerp(0.15f, 1f, gy / 4f) * h));
            }
            curPts = pts.ToArray(); curHeight = h; curYaw = 0f; curTilt = Vector2.zero;
        }

        /// <summary>The plant against the road's cross-section at every station
        /// its crown spans, in the frame of <paramref name="f"/>.</summary>
        static int BlockedFrame(in RoadFoot f, float y, float s, PlantShape sh)
        {
            int n = stageWp.Count;
            float roadHalf = RoadWidth * 0.5f;
            float u = f.side * f.d;                         // signed lateral offset of the plant's axis
            float R = sh.rMax * s;
            int st0 = Mathf.RoundToInt(f.station);
            float frac = f.station - st0;
            int kk = Mathf.CeilToInt((R + 0.5f) / Spacing);
            float envHalf = roadHalf + PlantEnvelopeShoulder + PlantSlackM;
            float bandH = sh.height * s / PlantShape.Bands;
            for (int j = -kk; j <= kk; j++)
            {
                int raw = st0 + j;
                if (!Loop && (raw < 0 || raw >= n)) continue;
                int st = WrapIdx(raw, n);
                float along = (j - frac) * Spacing;
                float wpY = stageWp[st].y, tarmac = wpY + RoadLift;
                bool deck = DeckCoversStation(st);
                float deckHalf = DeckHalfWidth + PlantSlackM + 0.1f;
                float soffit = wpY + DeckTopLift - DeckThick - UnderDeckClearM;
                for (int b = 0; b < PlantShape.Bands; b++)
                {
                    float rb = sh.r[b] * s + PlantSlackM;
                    float w2 = rb * rb - along * along;
                    if (w2 <= 0f) continue;
                    float w = Mathf.Sqrt(w2);
                    float y0 = y + b * bandH, y1 = y0 + bandH;
                    float uLo = u - w, uHi = u + w;
                    // THE ENVELOPE: lanes and shoulders, road surface to 4.5 m
                    if (uHi > -envHalf && uLo < envHalf && y1 > tarmac + plantEnvFloor && y0 < tarmac + PlantEnvelopeH) return 1;
                    // THE DECK: its width, from a metre under the soffit up
                    if (deck && uHi > -deckHalf && uLo < deckHalf && y1 > soffit && y0 < tarmac + PlantEnvelopeH) return 2;
                    if (rsKind == null) continue;
                    for (int sIx = 0; sIx < 2; sIx++)
                    {
                        float sign = sIx == 0 ? -1f : 1f;
                        var kind = rsKind[sIx][st];
                        // the plant's reach on this side, past this side's tarmac edge
                        float eLo = (sign > 0f ? uLo : -uHi) - roadHalf, eHi = (sign > 0f ? uHi : -uLo) - roadHalf;
                        if (kind == Roadside.Walled || kind == Roadside.Deck)
                        {
                            float we = rsWallE != null ? rsWallE[sIx][st] : WallFaceE;
                            if (we <= 0f) we = WallFaceE;
                            // the drawn stone, its posts and beam, and the collider behind them
                            float e0 = we - PlantSlackM, e1 = we + Mathf.Max(StageWallCollThick, StageWallFaceIn + StageWallDrawThick + StagePostGap + StagePostW) + PlantSlackM;
                            if (eHi > e0 && eLo < e1 && y1 > tarmac - PlantWallBelowM && y0 < tarmac + PlantWallTopM) return 3;
                        }
                        else if (kind == Roadside.Cut && rsCutFaced != null && rsCutFaced[sIx][st]
                                 && !(rsCutGraded != null && rsCutGraded[sIx][st]) && rsFaceH[sIx][st] > 0.5f)
                        {
                            // only a plant ROOTED in the rock (behind the face, under
                            // its crest) can come out through it
                            float faceH = rsFaceH[sIx][st];
                            float eB = sign * u - roadHalf, yB = y - wpY;
                            // (a plant on the rock top is rooted in it too: its sunk
                            // foot must not show below the crest either)
                            bool inRock = eB > CutFaceE(Mathf.Clamp(yB, 0f, faceH)) + 0.05f;
                            if (!inRock) continue;
                            float b0 = y0 - wpY;
                            if (b0 < faceH - 0.05f && eLo < CutFaceE(Mathf.Clamp(b0, 0f, faceH)) + 0.02f) return 4;
                        }
                    }
                }
            }
            return 0;
        }

        /// <summary>A faced cut's face at <paramref name="yy"/> over the waypoint
        /// plane, in metres past the tarmac edge: the vertical plinth, then the
        /// batter.</summary>
        static float CutFaceE(float yy) => CutToeE + Mathf.Max(0f, yy - BankPlinth) * BankBatter;

        /// <summary>A smaller kind to try in place of a plant, and its scales.</summary>
        struct PlantSwap { public int[] pool; public float s0, s1; }

        /// <summary>
        /// Plants a MeshForest plant clear of every solid: as asked, else moved,
        /// shortened or swapped; false = dropped. <paramref name="baseAt"/> gives
        /// the base height a plant of scale s may stand at (x, z), or NaN where
        /// its own rules do not let it stand.
        /// </summary>
        static PlantFit FitPlant(PlantClass cls, MeshTreeKit kit, ref float x, ref float z, ref float y, ref float s, ref int vi,
                                 System.Func<float, float, float, float> baseAt, float minScaleFrac, PlantSwap[] swaps,
                                 float yaw, Vector2 tilt = default,
                                 bool canMove = true, float[] steps = null, bool tilted = false, float envFloor = 0.02f)
        {
            plantEnvFloor = envFloor;
            SetCurrent(kit.variants[vi], yaw, tilt);
            try { return FitPlantCore(cls, kit, ref x, ref z, ref y, ref s, ref vi, baseAt, minScaleFrac, swaps, canMove, steps, tilted); }
            finally { plantEnvFloor = 0.02f; curPts = null; }
        }

        /// <summary>How far over the tarmac the envelope starts for the plant
        /// being fitted: 2 cm, or 0.3 m for flat litter (a leaf carpet on the
        /// shoulder is what a car drives over).</summary>
        static float plantEnvFloor = 0.02f;

        static PlantFit FitPlantCore(PlantClass cls, MeshTreeKit kit, ref float x, ref float z, ref float y, ref float s, ref int vi,
                                     System.Func<float, float, float, float> baseAt, float minScaleFrac, PlantSwap[] swaps,
                                     bool canMove, float[] steps, bool tilted)
        {
            var sh = PlantShape.Of(kit.variants[vi]);
            if (sh != null && tilted) sh = sh.Tumbled();
            if (sh == null || !PlantNearRoad(x, z, sh.rMax * s)) return PlantFit.Ok;
            int c = (int)cls;
            plantFit[c, 0]++;
            int first = PlantBlocked(x, y, z, s, sh);
            if (first == 0) { plantFit[c, 1]++; return PlantFit.Ok; }
            plantFitWhat[first]++;
            // MOVE: out from the road
            if (canMove && StageCorridor(x, z, RoadWidth * 0.5f + PlantReachM + sh.rMax * s, out RoadFoot f))
            {
                Vector2 dir = OutFromRoad(f, x, z);
                foreach (float t in steps ?? MoveSteps)
                {
                    float nx = x + dir.x * t, nz = z + dir.y * t;
                    float ny = baseAt(nx, nz, s);
                    if (float.IsNaN(ny) || PlantBlocked(nx, ny, nz, s, sh) != 0) continue;
                    x = nx; z = nz; y = ny;
                    plantFit[c, 2]++; return PlantFit.Moved;
                }
            }
            // SHORTEN: the same plant, smaller
            for (float k = 0.85f; k >= minScaleFrac - 1e-4f; k -= 0.1f)
            {
                float ns = s * k, ny = baseAt(x, z, ns);
                if (float.IsNaN(ny) || PlantBlocked(x, ny, z, ns, sh) != 0) continue;
                s = ns; y = ny;
                plantFit[c, 3]++; return PlantFit.Shortened;
            }
            // SWAP: a smaller kind, its largest scale that fits
            if (swaps != null)
                foreach (var sw in swaps)
                {
                    if (sw.pool == null || sw.pool.Length == 0) continue;
                    int start = Mathf.Abs(Mathf.FloorToInt(x * 13.7f + z * 7.3f)) % sw.pool.Length;
                    for (int q = 0; q < sw.pool.Length; q++)
                    {
                        int nv = sw.pool[(start + q) % sw.pool.Length];
                        var nsh = PlantShape.Of(kit.variants[nv]);
                        if (nsh == null) continue;
                        if (tilted) nsh = nsh.Tumbled();
                        SetCurrent(kit.variants[nv], curYaw, curTilt);
                        for (int step = 0; step < 3; step++)
                        {
                            float ns = Mathf.Lerp(sw.s1, sw.s0, step / 2f), ny = baseAt(x, z, ns);
                            if (float.IsNaN(ny) || PlantBlocked(x, ny, z, ns, nsh) != 0) continue;
                            s = ns; y = ny; vi = nv;
                            plantFit[c, 4]++; return PlantFit.Swapped;
                        }
                    }
                }
            plantFit[c, 5]++;
            return PlantFit.Dropped;
        }

        static readonly float[] MoveSteps = { 1f, 2f, 3f };

        /// <summary>A billboard tree clear of every solid: moved, else shortened
        /// (to 45% of its height), else false (dropped). The card is a cross of
        /// planes <paramref name="halfW"/> to either side, foliage from
        /// <paramref name="foliageFrac"/> of its height up.</summary>
        static PlantFit FitCard(ref float x, ref float z, ref float ground, ref float h, float widthPerH, float foliageFrac,
                                float yaw, System.Func<float, float, float> groundAt)
        {
            try { return FitCardCore(ref x, ref z, ref ground, ref h, widthPerH, foliageFrac, yaw, groundAt); }
            finally { curPts = null; }
        }

        static PlantFit FitCardCore(ref float x, ref float z, ref float ground, ref float h, float widthPerH, float foliageFrac,
                                    float yaw, System.Func<float, float, float> groundAt)
        {
            float halfW = h * widthPerH * 0.5f;
            SetCurrentCard(h, h * widthPerH, yaw);
            var sh = PlantShape.Card(h, halfW, h * foliageFrac);
            if (!PlantNearRoad(x, z, halfW)) return PlantFit.Ok;
            int c = (int)PlantClass.CardTree;
            plantFit[c, 0]++;
            int first = PlantBlocked(x, ground - 0.25f, z, 1f, sh);
            if (first == 0) { plantFit[c, 1]++; return PlantFit.Ok; }
            plantFitWhat[first]++;
            if (StageCorridor(x, z, RoadWidth * 0.5f + PlantReachM + halfW, out RoadFoot f))
            {
                Vector2 dir = OutFromRoad(f, x, z);
                foreach (float t in MoveSteps)
                {
                    float nx = x + dir.x * t, nz = z + dir.y * t, ng = groundAt(nx, nz);
                    if (float.IsNaN(ng) || PlantBlocked(nx, ng - 0.25f, nz, 1f, sh) != 0) continue;
                    x = nx; z = nz; ground = ng;
                    plantFit[c, 2]++; return PlantFit.Moved;
                }
            }
            for (float k = 0.85f; k >= 0.45f - 1e-4f; k -= 0.1f)
            {
                float nh = h * k;
                var nsh = PlantShape.Card(nh, nh * widthPerH * 0.5f, nh * foliageFrac);
                SetCurrentCard(nh, nh * widthPerH, yaw);
                if (PlantBlocked(x, ground - 0.25f, z, 1f, nsh) != 0) continue;
                h = nh;
                plantFit[c, 3]++; return PlantFit.Shortened;
            }
            plantFit[c, 5]++;
            return PlantFit.Dropped;
        }

        /// <summary>Horizontal unit vector from the road's centreline out
        /// through (x, z).</summary>
        static Vector2 OutFromRoad(in RoadFoot f, float x, float z)
        {
            int n = stageWp.Count;
            int a = Mathf.Clamp(f.s0, 0, n - 1), b = Mathf.Clamp(f.s1, 0, n - 1);
            Vector3 c = Vector3.Lerp(stageWp[a], stageWp[b], f.t);
            var d = new Vector2(x - c.x, z - c.z);
            if (d.sqrMagnitude > 1e-4f) return d.normalized;
            var r = rsRight != null ? rsRight[WrapIdx(Mathf.RoundToInt(f.station), n)] : Vector3.right;
            return new Vector2(r.x, r.z) * f.side;
        }

        // ---- where each kind may stand: its own planting rules, for a moved plant ----

        static bool TooSteepForTrees(float x, float z)
        {
            float s1 = StageDemY(x + 8f, z) - StageDemY(x - 8f, z);
            float s2 = StageDemY(x, z + 8f) - StageDemY(x, z - 8f);
            return s1 * s1 + s2 * s2 > 19f * 19f;
        }

        /// <summary>A canopy tree's base at (x, z), scale s (BuildStageForest's rules).</summary>
        static float CanopyBaseAt(float x, float z, float s)
        {
            if (!StageCorridor(x, z, ForestBand + 10f, out RoadFoot f) || f.d > ForestBand || TooSteepForTrees(x, z)) return float.NaN;
            float br = BridgeAt(f.station);
            if (br <= 0.4f && (f.d < RoadWidth * 0.5f + 2.6f || (f.d < StageWallOffset + 1.2f && br > 0.35f)
                               || (OnGradedRoadside(f) && !OnRockTop(f)))) return float.NaN;
            return StageStandY(x, z, f) - 0.25f - SlopeSink(x, z, 0.8f * s);
        }

        /// <summary>A second canopy beech's base (ExtraCanopySite's rules).</summary>
        static float ExtraCanopyBaseAt(float x, float z, float s)
        {
            if (!StageCorridor(x, z, ForestBand + 10f, out RoadFoot f)) return float.NaN;
            if (f.d < RoadWidth * 0.5f + 4.5f || f.d > 90f) return float.NaN;
            if (BridgeAt(f.station) > 0.35f || (OnGradedRoadside(f) && !OnRockTop(f))) return float.NaN;
            return StageStandY(x, z, f) - 0.25f - SlopeSink(x, z, 0.8f * s);
        }

        /// <summary>A billboard tree's ground (the same rules, card trees).</summary>
        static float CardGroundAt(float x, float z, bool deckOver)
        {
            if (!StageCorridor(x, z, ForestBand + 10f, out RoadFoot f) || f.d > ForestBand || TooSteepForTrees(x, z)) return float.NaN;
            if (!deckOver && (f.d < RoadWidth * 0.5f + 2.6f || (f.d < StageWallOffset + 1.2f && BridgeAt(f.station) > 0.35f)
                              || (OnGradedRoadside(f) && !OnRockTop(f)))) return float.NaN;
            return StageStandY(x, z, f);
        }

        /// <summary>An understory plant's base (AddUnderstory's rules).</summary>
        static float UnderBaseAt(float x, float z, float s)
        {
            if (!StageCorridor(x, z, ForestBand + 10f, out RoadFoot f)) return float.NaN;
            if (f.d < RoadWidth * 0.5f + UnderClearM || f.d > ForestBand) return float.NaN;
            if (BridgeAt(f.station) > 0.35f || (OnGradedRoadside(f) && !OnRockTop(f))) return float.NaN;
            return StageStandY(x, z, f) - 0.1f - SlopeSink(x, z, 0.35f);
        }

        /// <summary>A ground-cover plant's base (AddGroundCover's rules).</summary>
        static float CoverBaseAt(float x, float z, float s)
        {
            if (!StageCorridor(x, z, CoverBand + 4f, out RoadFoot f)) return float.NaN;
            if (f.d < RoadWidth * 0.5f + 0.9f || f.d > CoverBand) return float.NaN;
            if (BridgeAt(f.station) > 0.35f) return float.NaN;
            float lat = StageLatticeY(x, z), top = StageBankTopY(f);
            if (OnGradedRoadside(f) && top > lat + 0.3f && !OnRockTop(f)) return float.NaN;
            return Mathf.Max(Mathf.Max(lat, top), StageFillY(x, z)) - 0.03f;
        }

        static PlantSwap[] CanopySwaps(MeshTreeKit kit) => new[]
        {
            new PlantSwap { pool = kit.smallBeech, s0 = 0.8f, s1 = 1.4f },
            new PlantSwap { pool = kit.bushes, s0 = 1.4f, s1 = 2.3f },
        };

        /// <summary>Where the stage's own ground is at (x, z): the lattice, a
        /// rock top, a fill tail - whichever is highest.</summary>
        static float StageStandY(float x, float z, in RoadFoot f) =>
            Mathf.Max(Mathf.Max(StageLatticeY(x, z), StageBankTopY(f)), StageFillY(x, z));
    }
}
