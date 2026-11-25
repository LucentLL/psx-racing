using System.Collections.Generic;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// THE RULER READS THE ROAD THE CAR IS ON, NOT A LINE BESIDE IT (cityai 11-25; the owner's CltC7 race: his RX-7
    /// read 80 -> 1,231 m to gate 2 on University City Blvd, then 1,232 -> 78 m, when the graph locate handed him to
    /// the channelised right turn's line (edge 13224) 3-4 m right of the lane he was in, and his place went from 2nd to
    /// 5th; the self-test's lane walk found the same at 9 of 10 city tracks: a slip lane, a turn bay, a parallel
    /// carriageway, a ramp's line beside the lane). The locate picks ONE line; a car on a wide road is on the pavement
    /// of every line drawn under it. So: every edge whose pavement holds the car (its half width + <see cref="PaveM"/>,
    /// its height within 4 m) is read too, and the lowest reading wins when it beats the locate's by <see cref="JumpM"/>
    /// or more. A car that really turned off is on that road's pavement only, and reads the real way back. Used by
    /// <see cref="CheckpointRace"/> for every car read by the graph locate (the player, a rival far off its road) and
    /// by the self-test's lane walk. PSX_CP_RULER_HOLD=0: as before.
    /// </summary>
    public struct RulerHold
    {
        public bool on; public CityRouter.Loc loc; public int dir; public float trustR;
        public static RulerHold Fresh => new RulerHold { trustR = -1f };
        public const float JumpM = 25f, PaveM = 0.5f;
        static readonly HashSet<int> segs = new HashSet<int>();
        static readonly HashSet<int> seen = new HashSet<int>();

        public static float Step(CityRouter R, CityMap map, ref RulerHold h, CityRouter.Field F, Vector3 pos, Vector3 vel, Vector3 nose,
                                 CityRouter.Loc cur, int curDir, float r)
        {
            if (float.IsNaN(r) || !cur.valid || cur.offGraph) { h.on = false; return r; }
            var p = new Vector2(pos.x, pos.z);
            segs.Clear(); seen.Clear();
            map.EdgeSegsNear(p - new Vector2(20f, 20f), p + new Vector2(20f, 20f), segs);
            float best = r; CityRouter.Loc bestLoc = cur; int bestDir = curDir;
            foreach (int packed in segs)
            {
                int ei = packed >> 12;
                if (ei == cur.edge || !seen.Add(ei)) continue;
                var e = map.edges[ei];
                if (!R.Project(ei, pos, 0f, 0f, out float s, out float d)) continue;
                if (d > e.HalfMax + PaveM || Mathf.Abs(pos.y - e.YAt(s)) > 4f) continue;
                var l = cur; l.edge = ei; l.s = s; l.dist = d; l.at = pos; l.valid = true; l.offGraph = false;
                l.dir = R.DirOf(ei, s, vel, nose);
                int dd = l.dir;
                float v = R.Remaining(F, pos, ref l, ref dd);
                if (v < best - JumpM) { best = v; bestLoc = l; bestDir = dd; }
            }
            h.on = best < r;
            if (h.on) { h.loc = bestLoc; h.dir = bestDir; }
            h.trustR = best;
            return best;
        }
    }
}
