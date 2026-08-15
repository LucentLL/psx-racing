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

        public int Count => waypoints != null ? waypoints.Length : 0;
        public float TotalLength => Count * spacing;

        public Vector3 GetPoint(int i) => waypoints[((i % Count) + Count) % Count];

        public Vector3 GetTangent(int i)
        {
            Vector3 a = GetPoint(i);
            Vector3 b = GetPoint(i + 1);
            return (b - a).normalized;
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
                    int i = ((hint + o) % Count + Count) % Count;
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
                float c = curvatures[((from + o) % Count + Count) % Count];
                if (c > max) max = c;
            }
            return max;
        }
    }
}
