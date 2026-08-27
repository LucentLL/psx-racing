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
