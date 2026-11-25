using UnityEngine;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// THE RULER HOLDS ACROSS A LANE'S WIDTH (cityai 11-25; the owner's CltC7 race: his RX-7 read 80 -> 1,231 m to
    /// gate 2 on University City Blvd, then 1,232 -> 78 m, when the graph locate handed him to the channelised right
    /// turn's line (edge 13224) beside the lane he was in, and his place went from 2nd to 5th). A locate that raises
    /// the remaining by <see cref="JumpM"/> or more on another road is not believed while the car is still on the
    /// pavement of the road it was read on (+ <see cref="OffM"/>): the ruler reads THAT road (and at its end node,
    /// whichever road from the node reads lowest with the car on its pavement), so it falls metre for metre as the car
    /// drives on. Once the car is clearly on the other road, that road's reading stands: a car that really turned off
    /// has the real way back. Used by <see cref="CheckpointRace"/> for every car read by the graph locate (the player,
    /// a rival far off its road) and by the self-test's lane walk. PSX_CP_RULER_HOLD=0: as before.
    /// </summary>
    public struct RulerHold
    {
        public CityRouter.Loc trust; public int trustDir; public float trustR;
        public bool on; public CityRouter.Loc loc; public int dir;
        public static RulerHold Fresh => new RulerHold { trustR = -1f };
        public const float JumpM = 60f, OffM = 6f;

        public static float Step(CityRouter R, CityMap map, ref RulerHold h, CityRouter.Field F, Vector3 pos, Vector3 vel, Vector3 nose,
                                 CityRouter.Loc cur, int curDir, float r)
        {
            if (float.IsInfinity(r) || float.IsNaN(r) || !cur.valid || cur.offGraph) return r;
            if (!h.on)
            {
                if (h.trustR >= 0f && h.trust.valid && cur.edge != h.trust.edge && r > h.trustR + JumpM)
                { h.on = true; h.loc = h.trust; h.dir = h.trustDir; }
                else { h.trust = cur; h.trustDir = curDir; h.trustR = r; return r; }
            }
            float best = float.PositiveInfinity; CityRouter.Loc bestLoc = h.loc; int bestDir = h.dir;
            var hold = h;
            void Try(int ei, float s0, float win, int dirHint)
            {
                var e = map.edges[ei];
                if (!R.Project(ei, pos, s0, win, out float hs, out float hd)) return;
                if (hd > e.HalfMax + OffM || Mathf.Abs(pos.y - e.YAt(hs)) > 6f) return;
                var l = hold.loc; l.edge = ei; l.s = hs; l.dist = hd; l.at = pos; l.valid = true; l.offGraph = false;
                l.dir = dirHint != 0 ? dirHint : R.DirOf(ei, hs, vel, nose);
                int d = ei == hold.loc.edge ? hold.dir : l.dir;
                float v = R.Remaining(F, pos, ref l, ref d);
                if (v < best) { best = v; bestLoc = l; bestDir = d; }
            }
            var he = map.edges[h.loc.edge];
            Try(h.loc.edge, h.loc.s, 40f, h.loc.dir);
            if (float.IsInfinity(best) || bestLoc.s <= 0.5f || bestLoc.s >= he.length - 0.5f)
            {
                int node = h.loc.dir >= 0 ? he.b : he.a;
                if (node >= 0 && node < map.nodeEdges.Length && map.nodeEdges[node] != null)
                    foreach (int ei in map.nodeEdges[node])
                        if (ei != h.loc.edge) Try(ei, map.edges[ei].a == node ? 0f : map.edges[ei].length, 60f, 0);
            }
            if (!float.IsInfinity(best) && r > best + JumpM * 0.5f)
            {
                h.loc = bestLoc; h.dir = bestDir;
                return best;
            }
            h.on = false;
            h.trust = cur; h.trustDir = curDir; h.trustR = r;
            return r;
        }
    }
}
