using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  THE GROUND BEHIND A WALK (2026-11-05, the curbs pass, city-wide). The
    //  owner: "Curbs and sidewalks ... are all pits in game". Measured at
    //  West Trade Street and Beatties Ford Road, the land behind the walk
    //  stood 0.10-0.50 m under the LANE edge, 0.3-0.7 m under the walk's
    //  back: every walk read as a ledge over a ditch.
    //
    //  THE CAUSE. The walk (curb + 2.4 m slab, its back 0.19 m over the lane
    //  edge) was laid ON the roadside the land had been graded for a street
    //  without one. CityElevation.Ground holds the 8 m lattice 0.10 m under
    //  the tarmac (CorridorSink) across the cut band (8.5 m past the
    //  pavement) and never above that cap within the lattice's reach of the
    //  pavement (11.3 m), so a grass triangle can never stand in a lane. The
    //  walk's verge (EmitWalkSpan) then fell from the walk's back as a rural
    //  verge does - the 0.6 m shoulder at 4%, then 1V:4H - down to that sunk
    //  lattice: 0.3 m down within about a metre and a half. Where the land
    //  stands at or above the road (most of Uptown, Beatties Ford) it came
    //  back up at the cut band's back slope 3-7 m further out: a TRENCH 3-6 m
    //  wide between the walk and the lots. Where the land is lower the fill
    //  falls on past the bench (2-4.5 m from the pavement, under the walk
    //  itself), deeper still.
    //
    //  THE FIX (the strip, not the lattice: the lattice's cap is what keeps
    //  grass out of the lanes, and raising it under 8 m triangles reaching
    //  back under the pavement is exactly what it forbids). Behind a walk
    //  the verge is the BACKING: it leaves the walk's back level with it
    //  (no inch down, no falling shoulder) and
    //   - where the land comes back up to the walk within WalkBackFillM, the
    //     backing runs on level until it meets it and tucks under it there:
    //     the trench is filled and the walk meets the lot at its own height;
    //   - where it does not but the land stays near the walk (within
    //     WalkBackNearM: land at the road's level, which the corridor holds
    //     10 cm under the tarmac), it runs level for WalkBackLevelM - the
    //     frontage - and its bank down to the land (1V:4H, the fill's
    //     slope, a few decimetres) is behind that;
    //   - where the land really is lower (a fill), it stays level until the
    //     land falls past WalkBackNearM under it (WalkBackMinM at least),
    //     then grades down at 1V:4H as the verge did - a bank, not a pit;
    //   - a parking lot (cut flush into the lattice) on the way is never
    //     buried: the backing's bank comes down to the lot's edge; nor does
    //     the level run up to another road's pavement (a junction's fan, a
    //     side street): it ends where its bank still comes down before it,
    //     not in a face at the pavement's pad.
    //  Nothing else moves: the lattice, the buildings (seated on the lattice,
    //  CityBuildings.SeatY), the props, the lots and every road surface are
    //  as they were; the backing only lies over lattice that was lower than
    //  the walk, which it hides (a tree or pole foot standing in that dip
    //  is now a few decimetres deeper in the ground, never in the air). The
    //  same four profile points as the verge it replaces. Where a walk ramps
    //  out the backing eases back to the plain verge with the walk's width,
    //  so it meets the next span's verge at the ramp's end.
    //
    //  AT A JUNCTION the corner keeps its verges as they were: the corner
    //  walk's strips (behind the curb return, from the property corner P)
    //  are short and their rays turn through the corner, so a backing's
    //  level run of several metres folded them (their steep triangles are
    //  left out as fins) and opened the lattice between them and the arms'
    //  strips 0.5-0.8 m down - the junction sweep found it on 78 right turns.
    //  The arm's backing eases in from the plain verge over WalkBackEndEaseM
    //  from a junction end (a node of three or more edges), so it meets the
    //  corner's strips as the verge always did.
    //  PSX_CITY_WALKBACK=0: the verge as before (the A/B).
    // ======================================================================
    public static partial class CityMeshes
    {
        /// <summary>PSX_CITY_WALKBACK=0: the walk's verge falls away as before.
        /// Not readonly: the census builds both.</summary>
        public static bool WalkBackOn = System.Environment.GetEnvironmentVariable("PSX_CITY_WALKBACK") != "0";
        /// <summary>How far past the walk's back the backing runs level to meet
        /// land that comes back up to it: the cut band (8.5 m past the
        /// pavement) less the walk, plus the back slope's climb and one
        /// lattice cell's sag.</summary>
        public const float WalkBackFillM = 10f;
        /// <summary>Where the land stays lower, the level backing before the
        /// bank down to it.</summary>
        public const float WalkBackMinM = 1.0f;
        /// <summary>Where the land stays lower but near the walk (within
        /// <see cref="WalkBackNearM"/> of it: the road's corridor held 10 cm
        /// under the tarmac, a lattice cell's sag - land at the road's level),
        /// the backing runs level this far, the frontage a lot or a building
        /// stands on, and its bank down to the land is behind that.</summary>
        public const float WalkBackLevelM = 5.0f;
        public const float WalkBackNearM = 0.5f;
        /// <summary>From a junction end of an edge (its trimmed end at a node
        /// of three or more edges) the backing eases in from the plain verge
        /// over this run.</summary>
        public const float WalkBackEndEaseM = 8f;
        /// <summary>How finely the backing reads the land and the lots out of
        /// the walk (the verge's own crossing search refines it).</summary>
        const float WalkBackStepM = 0.5f;

        /// <summary>The backing's share at metre <paramref name="s"/> of the
        /// edge being laid (its <see cref="sections"/>): 0 at a junction end,
        /// 1 from WalkBackEndEaseM on.</summary>
        static float WalkBackEndEase(CityMap map, CityMap.Edge e, float s)
        {
            if (sections.Count < 2) return 1f;
            float k = 1f;
            if (map.nodeEdges[e.a].Count >= 3) k = Mathf.Min(k, Mathf.Clamp01((s - sections[0].s) / WalkBackEndEaseM));
            if (map.nodeEdges[e.b].Count >= 3) k = Mathf.Min(k, Mathf.Clamp01((sections[sections.Count - 1].s - s) / WalkBackEndEaseM));
            return k * k * (3f - 2f * k);
        }

        /// <summary>The census (CityWalkBackCount): off in the game.</summary>
        public static bool WalkBackCensus;
        /// <summary>The edge whose walk the strip being laid backs.</summary>
        static int walkBackEdge = -1;
        /// <summary>The backing's share at the cross-section being solved
        /// (SolveStripAt: 0 the verge, 1 the backing, between on a ramp).</summary>
        static float walkBackK;

        public static class WalkBackStats
        {
            /// <summary>Backing cross-sections laid with the switch on (every
            /// solve, refinements included): trench filled, kept off a lot,
            /// graded down to lower land, land at/over the walk.</summary>
            public static int sections, filled, lotKept, lower, flush, padded;
            /// <summary>The census, one count per cross-section place (0.5 m):
            /// sections; trench = the land dips more than <see cref="PitM"/>
            /// under the walk's back and comes back up within the census reach,
            /// of which the backing fills <see cref="trenchFilled"/> (back up
            /// within WalkBackFillM, no lot first); fall = it dips and stays
            /// down (graded down to it).</summary>
            public static int cSections, trench, trenchFilled, trenchFar, trenchLot, trenchEnd, fall, cornerSections, cornerTrench;
            public static float maxDip, maxTrenchDip;
            /// <summary>Per edge (census): sections, trench sections, trench
            /// sections the backing leaves, fall sections, the deepest trench
            /// under the walk's back and under the lane edge, the deepest fall.</summary>
            public static readonly Dictionary<int, (int n, int trench, int left, int fall, float dip, float laneDip, float fallDip, Vector2 at)> byEdge =
                new Dictionary<int, (int, int, int, int, float, float, float, Vector2)>();
            public static readonly HashSet<long> seen = new HashSet<long>();
            public static void Reset()
            {
                sections = filled = lotKept = lower = flush = padded = 0;
                cSections = trench = trenchFilled = trenchFar = trenchLot = trenchEnd = fall = cornerSections = cornerTrench = 0;
                maxDip = maxTrenchDip = 0f; byEdge.Clear(); seen.Clear();
            }
            public static string Line() =>
                $"walk backing ({(WalkBackOn ? "ON" : "OFF")}): solves {sections}: {filled} trenches filled, {lotKept} kept off a lot, {lower} graded down to lower land ({padded} levels shortened before another road's pavement), {flush} land at/over the walk";
            public static string CensusLine() =>
                $"census: {cSections} backing sections (0.5 m places); TRENCH (dips > {PitM:0.00} m under the walk's back, comes back up within {CensusReachM:0} m) {trench}: filled {trenchFilled}, left {trenchFar} (back up past {WalkBackFillM:0} m) + {trenchLot} (a lot first) + {trenchEnd} (within {WalkBackEndEaseM:0} m of a junction end, eased); deepest trench {maxTrenchDip:0.00} m; FALL (dips, stays down: graded) {fall}; deepest dip {maxDip:0.00} m; corners {cornerSections} sections, {cornerTrench} trench";
        }
        /// <summary>A dip under the walk's back the census calls a pit.</summary>
        public const float PitM = 0.10f;
        /// <summary>The walk's back over the lane edge (full curb, full width).</summary>
        static readonly float WalkBackOverLaneM = CurbH + WalkCrossFall * (WalkW - CurbNoseOut);
        /// <summary>How far the census looks for the land coming back up.</summary>
        const float CensusReachM = 16f;

        /// <summary>
        /// The backing's level run from the walk's back (<paramref name="s0"/>,
        /// height <paramref name="y0"/>, world plan) along <paramref name="outw"/>:
        /// where the lattice first comes back up to the walk within
        /// <paramref name="reach"/>, or NaN (the land stays lower, or a lot
        /// lies on the way: <paramref name="lot"/> its distance, else NaN).
        /// </summary>
        /// <param name="level">Where it returns NaN: how far the backing runs
        /// level before its bank down to the land - to where the land first
        /// lies more than <see cref="WalkBackNearM"/> under the walk (at least
        /// <see cref="WalkBackMinM"/>), at most <see cref="WalkBackLevelM"/>,
        /// and short of a lot by the bank's own run, so the bank's toe meets
        /// the lot's edge rather than lying on it.</param>
        static float WalkBackFill(CityMap map, Vector2 s0, Vector2 outw, float y0, float reach, out float lot, out float level)
        {
            lot = float.NaN;
            float deep = float.NaN;
            bool lots = WalkBackRayLots(map, s0, s0 + outw * reach);
            for (float e = 0f; e <= reach + 1e-3f; e += WalkBackStepM)
            {
                var q = s0 + outw * e;
                float l = LatticeY(map, q.x, q.y);
                if (l >= y0) { level = e; return e; }
                if (lots && WalkBackLotAt(map, q))
                {
                    lot = e;
                    float lv = float.IsNaN(deep) ? WalkBackLevelM : Mathf.Max(WalkBackMinM, deep);
                    level = Mathf.Clamp(e - (y0 - l) / RoadsideRules.CityFillSlope, 0f, Mathf.Min(lv, e));
                    return float.NaN;
                }
                if (float.IsNaN(deep) && l < y0 - WalkBackNearM) deep = e;
            }
            level = Mathf.Min(reach, float.IsNaN(deep) ? WalkBackLevelM : Mathf.Clamp(deep, WalkBackMinM, WalkBackLevelM));
            return float.NaN;
        }

        /// <summary>Is the plan point on a parking lot (its ring) or a lot
        /// entrance's apron?</summary>
        /// (Of the lots and aprons <see cref="WalkBackRayLots"/> gathered.)
        static bool WalkBackLotAt(CityMap map, Vector2 q)
        {
            if (rayDrives && DriveApronAt(q.x, q.y)) return true;
            foreach (var Q in rayAprons)
            {
                bool inside = true; float s0 = 0f;
                for (int k = 0; k < 4 && inside; k++)
                {
                    var a = Q[k]; var b = Q[(k + 1) % 4];
                    float s = (b.x - a.x) * (q.y - a.y) - (b.y - a.y) * (q.x - a.x);
                    if (k == 0) s0 = s; else if (s * s0 < 0f) inside = false;
                }
                if (inside) return true;
            }
            foreach (int li in rayLots)
            {
                var b = map.lots[li].box;
                if (q.x < b.x || q.x > b.z || q.y < b.y || q.y > b.w) continue;
                if (InRing(map.lots[li].ring, q)) return true;
            }
            return false;
        }

        static readonly List<int> rayLots = new List<int>(8);
        static readonly List<Vector2[]> rayAprons = new List<Vector2[]>(8);
        static bool rayDrives;

        /// <summary>The lots and lot-entrance aprons whose boxes meet the ray
        /// from <paramref name="a"/> to <paramref name="b"/> (and whether the
        /// tile has driveways): false where nothing could stop the backing.</summary>
        static bool WalkBackRayLots(CityMap map, Vector2 a, Vector2 b)
        {
            rayLots.Clear(); rayAprons.Clear();
            rayDrives = tileDrives.Count > 0;
            float x0 = Mathf.Min(a.x, b.x), x1 = Mathf.Max(a.x, b.x), z0 = Mathf.Min(a.y, b.y), z1 = Mathf.Max(a.y, b.y);
            int tx0 = Mathf.FloorToInt(x0 / TileSize), tx1 = Mathf.FloorToInt(x1 / TileSize);
            int tz0 = Mathf.FloorToInt(z0 / TileSize), tz1 = Mathf.FloorToInt(z1 / TileSize);
            for (int tz = tz0; tz <= tz1; tz++)
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    var ll = LotsInTile(map, tx, tz);
                    if (ll != null)
                        foreach (int li in ll)
                        {
                            var L = map.lots[li]; var bx = L.box;
                            if (L.ring == null || bx.z < x0 || bx.x > x1 || bx.w < z0 || bx.y > z1 || rayLots.Contains(li)) continue;
                            rayLots.Add(li);
                        }
                    if (LotsOn && apronsByTile != null && lotMapFor == map && apronsByTile.TryGetValue(LotTileKey(tx, tz), out var al))
                        foreach (int i in al)
                        {
                            var Q = apronQuads[i];
                            float qx0 = Mathf.Min(Mathf.Min(Q[0].x, Q[1].x), Mathf.Min(Q[2].x, Q[3].x)), qx1 = Mathf.Max(Mathf.Max(Q[0].x, Q[1].x), Mathf.Max(Q[2].x, Q[3].x));
                            float qz0 = Mathf.Min(Mathf.Min(Q[0].y, Q[1].y), Mathf.Min(Q[2].y, Q[3].y)), qz1 = Mathf.Max(Mathf.Max(Q[0].y, Q[1].y), Mathf.Max(Q[2].y, Q[3].y));
                            if (qx1 < x0 || qx0 > x1 || qz1 < z0 || qz0 > z1 || rayAprons.Contains(Q)) continue;
                            rayAprons.Add(Q);
                        }
                }
            return rayDrives || rayLots.Count > 0 || rayAprons.Count > 0;
        }

        /// <summary>The census of one backing cross-section (whatever the
        /// switch): how far the land behind the walk dips and whether it
        /// comes back up.</summary>
        static void WalkBackCensusAt(CityMap map, Vector2 s0, Vector2 outw, float y0, float run, float k)
        {
            long key = ((long)Mathf.RoundToInt(s0.x * 2f) << 32) ^ (uint)Mathf.RoundToInt(s0.y * 2f);
            if (!WalkBackStats.seen.Add(key)) return;
            float reach = Mathf.Min(run, CensusReachM), lo = float.MaxValue;
            float back = float.NaN, lotAt = float.NaN;
            bool lots = WalkBackRayLots(map, s0, s0 + outw * reach);
            for (float e = 0f; e <= reach + 1e-3f; e += VergeSearchStepM)
            {
                var q = s0 + outw * e;
                float l = LatticeY(map, q.x, q.y);
                if (l >= y0) { back = e; break; }
                if (l < lo) lo = l;
                if (lots && float.IsNaN(lotAt) && WalkBackLotAt(map, q)) lotAt = e;
            }
            float dip = lo < float.MaxValue ? y0 - lo : 0f;
            bool corner = walkBackEdge < 0;
            WalkBackStats.cSections++;
            if (corner) WalkBackStats.cornerSections++;
            bool isTrench = dip > PitM && !float.IsNaN(back), isFall = dip > PitM && float.IsNaN(back);
            bool left = false;
            if (isTrench)
            {
                if (corner) WalkBackStats.cornerTrench++;
                WalkBackStats.trench++;
                if (k < 1f) { WalkBackStats.trenchEnd++; left = true; }
                else if (!float.IsNaN(lotAt)) { WalkBackStats.trenchLot++; left = true; }
                else if (back > WalkBackFillM) { WalkBackStats.trenchFar++; left = true; }
                else WalkBackStats.trenchFilled++;
                WalkBackStats.maxTrenchDip = Mathf.Max(WalkBackStats.maxTrenchDip, dip);
            }
            if (isFall) WalkBackStats.fall++;
            if (dip > WalkBackStats.maxDip) WalkBackStats.maxDip = dip;
            if (corner) return;
            WalkBackStats.byEdge.TryGetValue(walkBackEdge, out var r);
            r.n++;
            if (isTrench) { r.trench++; if (left) r.left++; if (dip > r.dip) { r.dip = dip; r.at = s0; } r.laneDip = Mathf.Max(r.laneDip, dip - WalkBackOverLaneM); }
            if (isFall) { r.fall++; r.fallDip = Mathf.Max(r.fallDip, dip); }
            WalkBackStats.byEdge[walkBackEdge] = r;
        }
    }
}
