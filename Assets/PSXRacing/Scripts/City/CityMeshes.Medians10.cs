using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// REAL HEIGHTS round 10 (2026-10-29), the meshes' side. The solve's side (the 1'47" abutment
    /// line, the spill-through slope) is CityElevation.R10.cs. Each rule behind its own
    /// PSX_CITY_R10_* switch (=0: before).
    ///
    ///   DECKV  (1'47", I-277 past the Elizabeth Ave bridge) a deck run's end that nothing carries on
    ///          but its OWN partner run's padded claim across (Continued's twin test - round 9's open
    ///          item 2, fixed there only for ground ends that split) was drawn with no end at all:
    ///          the union Jersey stopped square and hollow at the slab's end, 7 m from the route's own
    ///          rail, which started square 1 m on - the owner's "empty box" between them. Such an end
    ///          is CLOSED now: the deck V (its legs from the Jersey to each carriageway's edge at the
    ///          slab's end, EmitUnionLegs), and the carriageway rail its leg meets keeps its full
    ///          height there (no turned-down start beside the leg's foot: a notch).
    ///          PSX_CITY_R10_DECKV=0: before.
    /// </summary>
    public static partial class CityMeshes
    {
        public static bool R10DeckVOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R10_DECKV") != "0";

        /// <summary>(round 10) The meshes' round-10 switches as one set (the look tool's A/B).</summary>
        public static bool[] Round10Flags() => new[] { R10DeckVOn };
        public static void SetRound10Flags(bool[] f)
        {
            if (f != null && f.Length > 0) R10DeckVOn = f[0];
        }

        static int r10DeckV;
        /// <summary>(round 10, DECKV) The ends closed (run, 1: s0, 2: s1), for the rails their legs meet.</summary>
        static readonly Dictionary<UnionRun, int> r10DeckVEnds = new Dictionary<UnionRun, int>();
        /// <summary>(round 10) Where DECKV closed an end (the probe's list).</summary>
        public static readonly System.Text.StringBuilder R10DeckVLog = new System.Text.StringBuilder();

        /// <summary>(round 10) What the round's mesh rules did (the union report and the probe).</summary>
        public static string Round10Report() => $"round 10: {r10DeckV} deck ends continued only by their own partner closed (the deck V){(R10DeckVOn ? "" : " [DECKV off]")}";

        /// <summary>
        /// (round 10, DECKV) After the open flags (and round 9's splits): an owner Barrier run's end on
        /// a deck that is "continued" only by its own partner run is closed - nothing else carries the
        /// median on past it, so its open end was a hollow, square Jersey end facing the traffic.
        /// </summary>
        static int CloseDeckEndsByOwnPartner(CityMap map, Trims t)
        {
            r10DeckVEnds.Clear();
            R10DeckVLog.Clear();
            if (!R10DeckVOn) return 0;
            if (!R9SplitOn) SplitVs.Clear();   // (MarkSplitEnds clears it when it runs)
            int n = 0;
            var E = map.edges;
            for (int ei = 0; ei < E.Length; ei++)
            {
                var list = t.unions[ei];
                if (list == null) continue;
                var X = E[ei];
                foreach (var r in list)
                {
                    if (!r.owner || r.median != DeckPairs.Median.Barrier || r.across == null || r.s1 - r.s0 < 1f) continue;
                    for (int end = 0; end < 2; end++)
                    {
                        if (end == 0 ? r.open0 : r.open1) continue;
                        if (ContinuedButByOwnPartner(map, t, r, end)) continue;
                        float s = Mathf.Clamp(end == 0 ? r.s0 : r.s1, 0f, X.length);
                        int dir = end == 0 ? -1 : 1;
                        // a DECK end (a ground end that parts is round 9's SPLIT)
                        if (!X.bridge && !X.ElevatedAt(Mathf.Clamp(s - dir * 0.5f, 0f, X.length))) continue;
                        if (end == 0) r.open0 = true; else r.open1 = true;
                        r10DeckVEnds.TryGetValue(r, out int bits);
                        r10DeckVEnds[r] = bits | (end == 0 ? 1 : 2);
                        n++;
                        // the V for the drive audit's MEDIAN rays, as a split's (its designed end): the
                        // mouth on the end's cross-line, the apex on the centre line where its legs start
                        // (EmitUnionMedian: max(TaperLenM, 1.5 x the gap) back)
                        var pX = X.PointAt(s);
                        FootAny(map, r.across, pX, out var Y, out float y);
                        if (Y != null)
                        {
                            var a = DrawnEdgeAt(map, t, X, s, r.side); var b = FacingEdgeAt(map, t, Y, y, pX);
                            float w = new Vector2(b.x - a.x, b.z - a.z).magnitude;
                            float leg = Mathf.Max(TaperLenM, 1.5f * w);
                            float sIn = Mathf.Clamp(s - dir * leg, 0f, X.length);
                            var aIn = DrawnEdgeAt(map, t, X, sIn, r.side); var pIn = X.PointAt(sIn);
                            FootAny(map, r.across, pIn, out var Yi, out float yi);
                            var bIn = Yi != null ? FacingEdgeAt(map, t, Yi, yi, pIn) : b;
                            SplitVs.Add((new Vector2(0.5f * (a.x + b.x), 0.5f * (a.z + b.z)), new Vector2(0.5f * (aIn.x + bIn.x), 0.5f * (aIn.z + bIn.z)), 0.5f * w));
                        }
                        if (R10DeckVLog.Length < 8000) R10DeckVLog.Append($" e{X.index} s{s:0.0} side {r.side} (nb e{r.nb});");
                    }
                }
            }
            return n;
        }

        /// <summary>(round 10, DECKV) Is an end of this run (owner; or the owner a partner run stands
        /// down for) a deck end DECKV closed, within <paramref name="reach"/> of arc sb on edge E?
        /// Then the carriageway's own rail starting there meets the V's leg: kept full height.</summary>
        static bool R10DeckVEndNear(CityMap map, UnionRun run, CityMap.Edge E, float sb, float reach)
        {
            if (!R10DeckVOn || run == null || r10DeckVEnds.Count == 0) return false;
            var M = run.owner ? run : run.mirror;
            if (M == null || !M.owner || M.edge < 0 || M.edge >= map.edges.Length || !r10DeckVEnds.TryGetValue(M, out int bits)) return false;
            var X = map.edges[M.edge];
            for (int end = 0; end < 2; end++)
            {
                if ((bits & (end == 0 ? 1 : 2)) == 0) continue;
                float sx = Mathf.Clamp(end == 0 ? M.s0 : M.s1, 0f, X.length);
                if (run == M) { if (Mathf.Abs(sb - sx) <= reach) return true; continue; }
                // the partner's side: the foot of the owner's end on this edge, or this run's own end beside it
                CityElevation.ProjectOn(E, X.PointAt(sx), out float f);
                if (Mathf.Abs(sb - f) <= reach + 1.5f) return true;
                float raw = Mathf.Abs(f - run.s0) <= Mathf.Abs(f - run.s1) ? run.s0 : run.s1;
                if (Mathf.Abs(sb - raw) <= reach && Mathf.Abs(raw - f) <= 3f) return true;
            }
            return false;
        }
    }
}
