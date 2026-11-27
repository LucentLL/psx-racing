using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// Road-surface stage 1: THE MEASURES FEED THE LINE MODEL. A measured pavement edge does not only move a junction's
    /// kerb: the ribbon, its lanes, its paint and the traffic's lane centre must follow, or the lanes stand off the
    /// measured pavement (the lane check; the island gate's HOLES / RAISED). So an edge's measure becomes two kinds of
    /// PROFILE EASES of the line model (the refuge islands' mechanism, LineModel.Ease.prof, sampled every FeedStepM):
    ///   a SHIFT  - where BOTH sides are measured, the whole cross-section (pavement, lanes, lines, lane centre) moves
    ///              sideways by ((left - right)measured - (left - right)rule) / 2: the map's line is rarely where the
    ///              rule assumes it lies in the cross-section (half a lane to a lane off at the owner's spots);
    ///   a WIDEN  - per side, what is left to the measured edge (pavement only: the lanes never see it; negative trims).
    /// LineModel.Extents then RETURNS THE MEASURE along the measured stations, held to an end named 'hold' (the end at a
    /// measured junction) and blended back to the rule over the measure's 'blend' beyond its other stations.
    /// Called at the end of CityMeshes.ComputeTrimsCore's ease builders (after the merge zones, the slip widening and the
    /// turn pockets: the rule's extents are final), each time the trims are computed. PSX_CITY_ROADSURFACE only.
    /// </summary>
    public static partial class JunctionMeasures
    {
        public const float FeedStepM = 1f;
        /// <summary>The edges whose measure the line model carries (the outline solver reads LineModel.Extents for them
        /// and does not apply the measure a second time).</summary>
        public static readonly HashSet<int> Fed = new HashSet<int>();
        public static string FeedLog = "";

        public static void FeedLineModel(CityMap map)
        {
            Fed.Clear(); FeedLog = "";
            var set = Current;
            if (set == null || map == null) return;
            var log = new System.Text.StringBuilder();
            foreach (var em in set.edges.Values)
            {
                if (em.edge < 0 || em.edge >= map.edges.Length || em.st.Count == 0) continue;
                var e = map.edges[em.edge];
                if (e.a == e.b || e.length < 2f) continue;
                int n = Mathf.CeilToInt(e.length / FeedStepM) + 1;
                var sh = new float[n]; var wp = new float[n]; var wm = new float[n];
                bool anyL = SideAt(em, true, 0f, out _, out _), anyR = SideAt(em, false, 0f, out _, out _);
                float maxS = 0f, maxW = 0f;
                for (int k = 0; k < n; k++)
                {
                    float s = Mathf.Min(k * FeedStepM, e.length);
                    LineModel.Extents(e, s, out float eM, out float eP);
                    float mL = eP, mR = eM;
                    if (anyL && SideAt(em, true, s, out float oL, out float wL, e.length)) mL = Mathf.Lerp(eP, oL, wL);
                    if (anyR && SideAt(em, false, s, out float oR, out float wR, e.length)) mR = Mathf.Lerp(eM, oR, wR);
                    float S = anyL && anyR ? 0.5f * ((mL - mR) - (eP - eM)) : 0f;
                    sh[k] = S; wp[k] = mL - (eP + S); wm[k] = mR - (eM - S);
                    maxS = Mathf.Max(maxS, Mathf.Abs(S)); maxW = Mathf.Max(maxW, Mathf.Max(Mathf.Abs(wp[k]), Mathf.Abs(wm[k])));
                }
                long tag = -(7000000L + em.edge);
                var add = new List<LineModel.Ease>(3);
                if (maxS > 0.01f) add.Add(new LineModel.Ease { shift = true, fromA = true, dw = 1f, len = e.length + 1f, d0 = 0f, prof = sh, profD0 = 0f, profDs = FeedStepM, tag = tag });
                if (maxW > 0.01f)
                {
                    add.Add(new LineModel.Ease { widen = true, side = 1, fromA = true, dw = 1f, len = e.length + 1f, d0 = 0f, prof = wp, profD0 = 0f, profDs = FeedStepM, tag = tag });
                    add.Add(new LineModel.Ease { widen = true, side = -1, fromA = true, dw = 1f, len = e.length + 1f, d0 = 0f, prof = wm, profD0 = 0f, profDs = FeedStepM, tag = tag });
                }
                if (add.Count == 0) continue;
                var ez = e.lmEase; var all = new List<LineModel.Ease>((ez?.Length ?? 0) + add.Count);
                if (ez != null) all.AddRange(ez);
                all.AddRange(add);
                e.lmEase = all.ToArray();
                Fed.Add(em.edge);
                log.Append($"e{em.edge} shift up to {maxS:0.00} m, widen up to {maxW:0.00} m; ");
            }
            FeedLog = log.ToString();
            if (Fed.Count > 0) Debug.Log($"[JunctionMeasures] the line model carries {Fed.Count} measured edges ({set.provider}): {FeedLog}");
        }
    }
}
