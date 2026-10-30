using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// REAL HEIGHTS round 10 (2026-10-29), the solve's side.
    ///
    /// 1'47" (the owner's UptownLoop video; I-277 past the Elizabeth Ave bridge, uptown ~1800 m;
    /// edge numbers of the regenerated data): the route's carriageway e2448 -> e2445 and its twin
    /// e2152 -> e2447 cross E 10th St on two bridges whose OSM abutments are SKEWED: e2448's ends at
    /// n3667 (e2445 s 0, carried on a few metres over E 10th St by the crossing's reach), e2447's at
    /// n3305, beside e2445 s ~7.5. ONE ABUTMENT LINE (plan L3, <see cref="UnionAbutments"/>) puts a
    /// union's two deck ends on one line square to the pair - the CHORD of the pair - and on this
    /// curving pair that line crossed e2445 at s 12.3, 4.9 m past the twin's own abutment. Those
    /// metres of e2445 became structure over nothing: no floor held the land beside it, the land
    /// under its footprint was dug to the soffit's air, and the median between it and the twin's
    /// approach fill fell into the E 10th St valley, -11 m (a creek channel rule did the same by
    /// creeks). The 8 m lattice carried the dip on past the end.
    ///
    /// ABUTFOOT: a deck end carried on to its twin's line stops where its road passes the twin's
    /// own deck end square to ITSELF (the twin end's perpendicular foot on the carried road) when
    /// the line would run more than <see cref="R10AbutTolM"/> past it: both decks end on the real
    /// skewed line. PSX_CITY_R10_ABUTFOOT=0: before. SPILL (below) holds a twin median's land at
    /// an abutment. The meshes' side (the deck V at such an end) is CityMeshes.Medians10.cs.
    /// </summary>
    public static partial class CityElevation
    {
        public static bool R10AbutFootOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R10_ABUTFOOT") != "0";
        /// <summary>How far past the twin's own deck end a carried end may be put before ABUTFOOT cuts it
        /// back there. A chord-square line within this of the twin's foot is left as it was (most such
        /// carries pass it by under 1 m - none a pit); 16 ends city-wide go further (1.5-4.9 m).</summary>
        public const float R10AbutTolM = 1.5f;

        /// <summary>
        /// (round 10, 1'47") SPILL: the land in a twin deck's median by its abutment. Nothing held the
        /// land in the gap between two union decks: beside a deck no floor pins it (structure does
        /// not pin the land), the soffit's air caps it, and the approaches' own fills fall at 1V:2H
        /// to 1V:4H into whatever is under the bridge - at 1'47" the E 10th St valley, 11 m down, so
        /// the median at the slab's end was a pit, and the 8 m lattice carried its slope past the end
        /// into the approaches' median (the owner's box; by a creek WP-25's channel rule did the
        /// same). A real abutment stands on a spill-through slope from its bridge seat. In the median
        /// of a union pair (beside the deck, never under its own pavement, within the union's cap
        /// reach) within <see cref="R10SpillReachM"/> of the deck's end, the land is held at least on
        /// that slope - the seat (the deck's height at its end less <see cref="DeckThick"/> and
        /// <see cref="R10SeatAirM"/>) falling 1V:2H under the deck - and the soffit's air cap tapers
        /// from <see cref="UnderDeckAir"/> down to R10SeatAirM over <see cref="R10SeatBandM"/> at the
        /// end (the land meets the seat; never the soffit's own plane). A road's cap still keeps it
        /// under every pavement. PSX_CITY_R10_SPILL=0: before.
        /// </summary>
        public static bool R10SpillOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R10_SPILL") != "0";
        /// <summary>How far under a deck from its end the spill-through slope is looked for.</summary>
        public const float R10SpillReachM = 24f;
        /// <summary>The air kept under the soffit at the abutment itself (no coplanar land and slab).</summary>
        public const float R10SeatAirM = 0.15f;
        /// <summary>Over how far from the deck's end the soffit's air cap grows back to UnderDeckAir.</summary>
        public const float R10SeatBandM = 4f;
        /// <summary>(round 10, SPILL) The soffit air a twin median point <paramref name="dEnd"/> from the
        /// deck's end is capped by.</summary>
        static float R10CapAir(float dEnd) => Mathf.Max(R10SeatAirM, UnderDeckAir * Mathf.Clamp01(dEnd / R10SeatBandM));
        /// <summary>How far past a twin deck's median-side pavement edge its abutment's spill holds the land.</summary>
        public const float R10SpillSideM = 8f;

        /// <summary>(round 10, SPILL) Is plan point p (its foot on segment si of deck e, <paramref name="past"/>
        /// m past the pavement) in a twin median: on the median side (the left of a one-way
        /// carriageway's travel) within <see cref="R10SpillSideM"/>, the deck one of a union pair or
        /// carried on from one through a two-arm node?</summary>
        static bool R10TwinMedianSide(CityMap map, CityMap.Edge e, int si, Vector2 p, float past, bool unionDeck)
        {
            // (CityMap.Edge.SideOf: -1 the left of the travel, the median side of a one-way carriageway)
            if (!e.oneway || past > R10SpillSideM || e.SideOf(si, p) >= 0) return false;
            if (unionDeck) return true;
            if (!TwinHoldsOn || map.deckUnionsOf == null) return false;
            for (int k = 0; k < 2; k++)
            {
                int node = k == 0 ? e.a : e.b;
                var list = map.nodeEdges[node];
                if (list.Count != 2) continue;
                int oi = list[0] == e.index ? list[1] : list[0];
                if (oi != e.index && map.deckUnionsOf[oi] != null && map.edges[oi].ElevatedAt(map.edges[oi].a == node ? 0f : map.edges[oi].length)) return true;
            }
            return false;
        }

        /// <summary>(round 10) The solve-side switches as one set (the look tool's A/B).</summary>
        public static bool[] Round10Flags() => new[] { R10AbutFootOn, R10SpillOn };
        public static void SetRound10Flags(bool[] f) { R10AbutFootOn = f[0]; R10SpillOn = f[1]; }

        /// <summary>(round 10, SPILL) The spill-through floor a deck's abutment holds a median point
        /// at: the point's true foot on deck <paramref name="e"/> is arc <paramref name="at"/>, the
        /// deck's structure ends within <see cref="R10SpillReachM"/> along it (inside the edge, or at
        /// a node where the road goes on grounded); MinValue when there is none.</summary>
        static float R10SpillFloor(CityMap map, CityMap.Edge e, float at, out float dEnd)
        {
            dEnd = float.MaxValue;
            float best = float.MaxValue, endS = 0f; CityMap.Edge endE = null;
            for (int dir = -1; dir <= 1; dir += 2)
            {
                for (float d = 0.5f; d <= R10SpillReachM + 1e-3f; d += 0.5f)
                {
                    float s = at + dir * d;
                    if (s < 0f || s > e.length)
                    {
                        // the deck runs to its node: an end there only if the road goes on grounded
                        int node = s < 0f ? e.a : e.b;
                        bool onStructure = false;
                        foreach (int oi in map.nodeEdges[node])
                        {
                            if (oi == e.index) continue;
                            var o = map.edges[oi];
                            if (o.a == o.b || o.stS == null) continue;
                            if (o.ElevatedAt(o.a == node ? 0f : o.length)) { onStructure = true; break; }
                        }
                        float dd = s < 0f ? at : e.length - at;
                        if (!onStructure && dd < best) { best = dd; endE = e; endS = s < 0f ? 0f : e.length; }
                        break;
                    }
                    if (!e.ElevatedAt(s))
                    {
                        if (d - 0.5f < best) { best = d - 0.5f; endE = e; endS = at + dir * (d - 0.5f); }
                        break;
                    }
                }
            }
            if (endE == null) return float.MinValue;
            dEnd = best;
            float seat = endE.YAt(endS) - DeckThick - R10SeatAirM;
            return seat - best * RoadsideRules.CityBankSlope;
        }

        /// <summary>(round 10) One abutment line: carries the twin's perpendicular foot cut short,
        /// how far at most, and each carry that ran past its twin's end (the probe's list).</summary>
        public static int R10AbutLimited, R10AbutPastFoot;
        public static float R10AbutLimitMaxM;
        public static readonly StringBuilder R10AbutLog = new StringBuilder();

        static void R10AbutReset()
        {
            R10AbutLimited = R10AbutPastFoot = 0; R10AbutLimitMaxM = 0f;
            R10AbutLog.Clear();
        }

        /// <summary>(round 10) Has the march along <paramref name="e"/> from arc <paramref name="from"/>
        /// to <paramref name="to"/> (direction <paramref name="dir"/>) passed the perpendicular through
        /// <paramref name="q"/> (the twin's deck end)? f: where between the two it is passed (0..1).</summary>
        static bool R10PassesFoot(CityMap.Edge e, float from, float to, int dir, Vector2 q, out float f)
        {
            f = 1f;
            float f0 = Vector2.Dot(e.PointAt(from) - q, e.TangentAt(from) * dir);
            float f1 = Vector2.Dot(e.PointAt(to) - q, e.TangentAt(to) * dir);
            if (f1 < 0f) return false;
            f = f0 >= 0f ? 0f : Mathf.Clamp01(-f0 / Mathf.Max(1e-5f, f1 - f0));
            return true;
        }

        /// <summary>(round 10) Has the deck end at arc <paramref name="s"/> of <paramref name="e"/>
        /// (walking <paramref name="dir"/>) already reached the twin end's perpendicular?</summary>
        static bool R10AtOrPastFoot(CityMap.Edge e, float s, int dir, Vector2 q) =>
            Vector2.Dot(e.PointAt(s) - q, e.TangentAt(s) * dir) >= -0.05f;

        // ------------------------------------------------------------------
        //  PSX_R10_TRACE="2435,2437": the structure flags of those edges after
        //  each structure step (the probe prints R10Trace)
        // ------------------------------------------------------------------
        public static readonly StringBuilder R10Trace = new StringBuilder();
        static int[] r10TraceEdges;
        static void R10TraceStep(CityMap map, string step)
        {
            if (r10TraceEdges == null)
            {
                var l = new List<int>();
                foreach (var s in (System.Environment.GetEnvironmentVariable("PSX_R10_TRACE") ?? "").Split(','))
                    if (int.TryParse(s.Trim(), out int v)) l.Add(v);
                r10TraceEdges = l.ToArray();
            }
            if (r10TraceEdges.Length == 0) return;
            if (step == "begin") { R10Trace.Clear(); return; }
            foreach (int ei in r10TraceEdges)
            {
                if (ei < 0 || ei >= map.edges.Length) continue;
                var e = map.edges[ei];
                if (e.stS == null) continue;
                var sb = new StringBuilder($"  {step,-10} e{ei} len {e.length:0.0}:");
                for (int i = 0; i < e.stS.Length; i++) sb.Append($" {e.stS[i]:0.0}{(e.stElev[i] ? "E" : "")}");
                R10Trace.AppendLine(sb.ToString());
            }
        }
    }
}
