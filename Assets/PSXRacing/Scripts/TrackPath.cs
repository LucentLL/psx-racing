using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Baked centerline of the circuit: evenly spaced waypoints with tangents.
    /// Waypoint 0 is the start/finish line. Used by AI, lap tracking, and respawn.
    /// </summary>
    public class TrackPath : MonoBehaviour
    {
        public Vector3[] waypoints;
        public float[] curvatures;      // 1/turn-radius at each waypoint
        public float spacing = 4f;
        public float roadWidth = 12f;

        /// <summary>A straight strip: the waypoint list has ENDS. Every index
        /// walk clamps instead of wrapping, or an AI that reaches the shutdown
        /// area turns round and drives back up the strip at the field.</summary>
        public bool drag;
        /// <summary>A point-to-point STAGE: has ends exactly like a strip, but
        /// is a real winding road — so everything that keys off "the list has
        /// ends" asks <see cref="HasEnds"/>, and everything that is really
        /// about DRAG RACING (the top-down view, trap-speed talk, staging
        /// abreast) keeps asking <see cref="drag"/>.</summary>
        public bool pointToPoint;
        /// <summary>Waypoint the traps sit at. -1 on a circuit, where the race
        /// is decided by lap count instead.</summary>
        public int finishIndex = -1;
        /// <summary>What the HUD calls the distance, e.g. "1/4 MILE". Baked
        /// rather than derived from the metres, because 402.336 m rounds to
        /// several things and none of them is what a drag racer says.</summary>
        public string dragLabel = "";

        /// <summary>The waypoint list has ends: index walks clamp, the race is
        /// decided at a distance, and there is no lap.</summary>
        public bool HasEnds => drag || pointToPoint;

        /// <summary>Which way round this path is being driven. Set once, at
        /// load, by whoever turned it round; read by anything that has to
        /// PRINT the direction rather than follow it.</summary>
        public bool reversed;

        /// <summary>
        /// TURN THE CIRCUIT ROUND.
        ///
        /// A reverse venue has no scene of its own — it races in its forward
        /// twin's, because the road, the barriers, the kerbs, the scenery and
        /// the elevation are the same physical objects standing in the same
        /// places, and the only thing that differs is the order you meet them
        /// in. That order is this list.
        ///
        /// WAYPOINT 0 DOES NOT MOVE. On a loop the list is reversed and then
        /// rotated so the old first point is still the first point, which keeps
        /// the start/finish line, the grid, the fuel-stop opening and the lap
        /// counter exactly where they were baked — a start line is a painted
        /// band across a road and does not care which way you cross it. On a
        /// route with ENDS the whole list simply flips, because the far end of
        /// a mountain stage IS the new start, and the climb becomes a descent.
        ///
        /// The curvature array rides along unchanged in VALUE because
        /// BuildWaypoints measures it with Vector3.Angle, which is unsigned: a
        /// corner is as tight from either side. Only its order moves.
        /// </summary>
        public void ReverseInPlace()
        {
            if (waypoints == null || waypoints.Length < 2 || reversed) return;
            int n = waypoints.Length;
            var wp = new Vector3[n];
            var cv = curvatures != null && curvatures.Length == n ? new float[n] : null;

            for (int i = 0; i < n; i++)
            {
                // Loop: 0 stays put and the rest walk backwards round it.
                // Ends:  a straight flip, first point to last.
                int src = HasEnds ? (n - 1 - i) : (i == 0 ? 0 : n - i);
                wp[i] = waypoints[src];
                if (cv != null) cv[i] = curvatures[src];
            }
            waypoints = wp;
            if (cv != null) curvatures = cv;
            reversed = true;
        }

        public int Count => waypoints != null ? waypoints.Length : 0;
        public float TotalLength => Count * spacing;

        public Vector3 GetPoint(int i) => waypoints[Wrap(i)];

        /// <summary>Index normaliser: modulo on a circuit, clamp on a route
        /// with ends.</summary>
        public int Wrap(int i) => HasEnds
            ? Mathf.Clamp(i, 0, Count - 1)
            : ((i % Count) + Count) % Count;

        public Vector3 GetTangent(int i)
        {
            // Step BACKWARD off the end of a strip rather than forward into the
            // clamp. On a drag strip Wrap clamps, so at the last waypoint the
            // two samples were the SAME point and this handed back Vector3.zero
            // -- a heading of nowhere, from a method every caller assumes is
            // unit length. It reaches GetRotation (LookRotation of a zero
            // vector), the AI's lateral basis, respawn, and the obstacle audit,
            // where a zero right-vector measured every barrier at the end of
            // both strips as lying across the centreline: six phantom
            // "invisible walls" in a report whose whole job is to find real ones.
            int a = Wrap(i), b = Wrap(i + 1);
            if (a == b) { b = a; a = Wrap(i - 1); }
            Vector3 d = waypoints[b] - waypoints[a];
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
        }

        public Quaternion GetRotation(int i) => Quaternion.LookRotation(GetTangent(i), Vector3.up);

        /// <summary>Find nearest waypoint index, searching a window around a hint for speed.</summary>
        public int NearestIndex(Vector3 pos, int hint = -1, int window = 25)
        {
            int best = 0;
            float bestDist = float.MaxValue;
            if (hint >= 0)
            {
                for (int o = -window; o <= window; o++)
                {
                    int i = Wrap(hint + o);
                    float d = (waypoints[i] - pos).sqrMagnitude;
                    if (d < bestDist) { bestDist = d; best = i; }
                }
            }
            else
            {
                for (int i = 0; i < Count; i++)
                {
                    float d = (waypoints[i] - pos).sqrMagnitude;
                    if (d < bestDist) { bestDist = d; best = i; }
                }
            }
            return best;
        }

        /// <summary>Max curvature over the next few waypoints (for AI corner speed).</summary>
        public float MaxCurvatureAhead(int from, int count)
        {
            float max = 0f;
            for (int o = 0; o < count; o++)
            {
                float c = curvatures[Wrap(from + o)];
                if (c > max) max = c;
            }
            return max;
        }
    }
}
