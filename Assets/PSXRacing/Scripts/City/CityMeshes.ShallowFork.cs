using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  ROAD SURVEY STAGE 3: SHALLOW FORKS AND MERGES (owner 11-27, the Wendover race before checkpoint 7: "overlapping
    //  and conflicting road lines").
    //
    //  At a node where one road comes in and two one-way roads leave it (or meet it) at a shallow angle, the trims'
    //  through pair was the two MOST OPPOSITE arms, whatever their size: at n3583 East Independence Expressway (4 lanes)
    //  went through into Albemarle Road (2 lanes, 1 degree straighter) and the 4-lane Independence Boulevard became the
    //  BRANCH clipped onto the 2-lane road. A branch's ribbon is cut back against its host only where the host's
    //  pavement is, so the bigger road drew its lanes, lines and deck start across the smaller one for 115 m.
    //
    //  SF1 (this file): the through partner of the incoming arm is chosen among the arms that continue it (dot under
    //  ThroughCos) and lie within SfMaxDeg of the most opposite one; the one with the most lanes wins, then the widest,
    //  then the most opposite (the old choice). Links never win over a road (the pass-0 rule stands). Every other arm
    //  shallow to it is then CLIPPED onto it by the existing branch machinery (ClipAt / EmitBranch / BuildGores: one
    //  owner over the shared pavement, the branch ribbon cut at the host's edge, the host's side standing down where the
    //  branch has a surface beside it, the painted gore once the branch has left it). No edge, lane count, ease or
    //  control trim changes: traffic and rivals follow the same lane model; only which ribbon owns the shared pavement.
    //  PSX_CITY_SHALLOWFORK=1 turns it on (LAB row "SHALLOW FORKS ONE ROAD").
    // ======================================================================
    public static partial class CityMeshes
    {
        public static bool ShallowForkOn = System.Environment.GetEnvironmentVariable("PSX_CITY_SHALLOWFORK") == "1";
        public const float SfMaxDeg = 12f;
        /// <summary>Since the last trims: nodes whose through partner SF1 changed (and the first 40 of them).</summary>
        public static int ShallowForkSwaps; public static readonly List<int> ShallowForkNodes = new List<int>();

        /// <summary>SF1: given the through pair (tA, tB) by opposition, pick the continuing arm by size.</summary>
        static void ShallowForkThrough(List<(CityMap.Edge e, Vector2 dir, float hw)> arms, int n, ref int tA, ref int tB)
        {
            if (!ShallowForkOn || tA < 0 || tB < 0 || arms.Count < 3) return;
            float cosMax = Mathf.Cos(SfMaxDeg * Mathf.Deg2Rad);
            // try each end of the pair as the incoming arm: the other end and its shallow rivals compete
            for (int pass = 0; pass < 2; pass++)
            {
                int inc = pass == 0 ? tA : tB, cur = pass == 0 ? tB : tA;
                int best = cur;
                for (int k = 0; k < arms.Count; k++)
                {
                    if (k == inc || k == cur) continue;
                    var ek = arms[k].e; var eb = arms[best].e;
                    if (ek.link != arms[cur].e.link) continue;                                  // a link never takes a road's place
                    if (Vector2.Dot(arms[k].dir, arms[inc].dir) >= ThroughCos) continue;          // k does not continue the incoming arm
                    if (Vector2.Dot(arms[k].dir, arms[cur].dir) < cosMax) continue;               // not a shallow rival of the through arm
                    if (!ek.oneway || !arms[cur].e.oneway) continue;                               // two one-way roads parting
                    bool bigger = ek.lanes > eb.lanes || (ek.lanes == eb.lanes && arms[k].hw > arms[best].hw + 0.5f);
                    if (bigger) best = k;
                }
                if (best != cur)
                {
                    if (pass == 0) tB = best; else tA = best;
                    ShallowForkSwaps++; if (ShallowForkNodes.Count < 40) ShallowForkNodes.Add(n);
                    return;
                }
            }
        }
    }
}
