using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The replay's DIRECTOR: a set of fixed trackside lenses planted along
    /// the route, each one panning to hold the car it is watching and
    /// zooming to keep it in frame, cutting to the next lens up the road as
    /// the car passes. The reference is Gran Turismo's replay, whose whole
    /// look is a long lens on a tripod: the car comes at you small, fills
    /// the frame, sweeps past, and the cut lands you a few hundred metres up
    /// the road waiting for it again.
    ///
    /// Planted, not authored. A circuit's lenses are derived from its own
    /// waypoints — one every so many metres, standing off the OUTSIDE of
    /// whatever corner comes next so the car sweeps across the frame rather
    /// than straight at the lens, at a height taken from the ground it is
    /// standing on, and moved to the other shoulder or lifted when something
    /// solid stands between it and the road. Every few is a LOW lens at
    /// wheel height by the kerb, which is the other shot those replays are
    /// made of.
    ///
    /// Lives on the same camera object as ChaseCamera and takes over only
    /// while enabled; RaceReplay switches the two.
    /// </summary>
    public class ReplayCamera : MonoBehaviour
    {
        public struct Station
        {
            /// <summary>Waypoint the lens stands beside.</summary>
            public int idx;
            public Vector3 pos;
            /// <summary>A wheel-height lens by the road edge, wide.</summary>
            public bool low;
        }

        /// <summary>Metres of road per lens. Long enough that a car at race
        /// pace holds each shot for three or four seconds.</summary>
        public const float StationEveryM = 150f;
        /// <summary>Stand-off from the road EDGE for a high lens and a low one.</summary>
        public const float HighStandoffM = 7f, LowStandoffM = 2.6f;
        public const float HighEyeM = 3.6f, LowEyeM = 0.9f;
        /// <summary>Stations the car passes beyond a lens before the cut.</summary>
        public const int PassStations = 6;
        /// <summary>The zoom: the lens keeps a target of about this many
        /// metres across the frame at the car, between the two FOV stops.</summary>
        public const float FrameAtCarM = 9f;
        public const float MinFOV = 8f, MaxFOV = 56f;
        /// <summary>Pan follow rate, 1/s. A tripod pans, it does not snap.</summary>
        public const float PanLag = 9f;

        TrackPath path;
        Camera cam;
        float roadWidth = 12f;
        readonly List<Station> stations = new List<Station>();
        int cur = -1;
        int carIdx = -1;
        Quaternion aim = Quaternion.identity;
        bool aimSeeded;
        float baseNear = 0.25f;

        public IReadOnlyList<Station> Stations => stations;
        public int Current => cur;

        /// <param name="avoid">Stations no lens may stand at — inside a
        /// tunnel, where a lens beside the road is inside the mountain.</param>
        public void Setup(TrackPath p, Camera c, float roadWidthM, System.Func<int, bool> avoid = null)
        {
            path = p; cam = c; roadWidth = roadWidthM;
            if (cam != null) baseNear = cam.nearClipPlane;
            stations.Clear();
            if (path != null && path.Count > 2)
                stations.AddRange(Plan(path, roadWidth, GroundAt, Blocked, avoid));
            cur = -1; carIdx = -1; aimSeeded = false;
            enabled = true;
        }

        /// <summary>Drop the current lens so the next frame re-picks for a
        /// car somewhere else on the track (a seek, or a change of focus).</summary>
        public void Retarget() { cur = -1; carIdx = -1; aimSeeded = false; }

        void OnDisable()
        {
            if (cam != null) cam.nearClipPlane = baseNear;
        }

        // ------------------------------------------------------------------
        //  Planning
        // ------------------------------------------------------------------
        /// <summary>
        /// Plant the lenses. Pure over its arguments so the self-test can
        /// plan a circuit without a scene: <paramref name="groundAt"/> gives
        /// a height for a world XZ (or NaN for "nothing there"), and
        /// <paramref name="blocked"/> says whether anything solid stands
        /// between a lens and a point on the road.
        /// </summary>
        public static List<Station> Plan(TrackPath path, float roadWidth,
                                         System.Func<float, float, float> groundAt,
                                         System.Func<Vector3, Vector3, bool> blocked,
                                         System.Func<int, bool> avoid = null)
        {
            var list = new List<Station>();
            int n = path.Count;
            if (n < 2) return list;
            int every = Mathf.Max(8, Mathf.RoundToInt(StationEveryM / Mathf.Max(0.5f, path.spacing)));
            int count = Mathf.Max(1, n / every);
            bool loop = !path.HasEnds;
            float half = roadWidth * 0.5f;
            int lastSide = 1;

            for (int s = 0; s < count; s++)
            {
                // A little jitter along the route, deterministic in the index,
                // so the cuts do not land on a metronome.
                int idx = Mathf.Clamp(s * every + (Hash(s) % Mathf.Max(1, every / 3)), 0, n - 1);
                if (!loop && idx >= n - 4) break;
                if (avoid != null && avoid(idx)) continue;
                bool low = s % 4 == 3;

                // The outside of the corner ahead: sum the turn over the next
                // stretch and stand on the far side of it.
                float turn = 0f;
                for (int k = 1; k <= 12; k++)
                {
                    int a = path.Wrap(idx + k - 1), b = path.Wrap(idx + k);
                    Vector3 ta = path.GetTangent(a), tb = path.GetTangent(b);
                    turn += Vector3.SignedAngle(ta, tb, Vector3.up);
                }
                // Left turn (negative signed angle about +Y) → outside is the right.
                int side = Mathf.Abs(turn) < 6f ? -lastSide : (turn < 0f ? 1 : -1);
                lastSide = side;

                Vector3 road = path.GetPoint(idx);
                Vector3 right = Vector3.Cross(Vector3.up, path.GetTangent(idx)).normalized;
                Vector3 ahead = path.GetPoint(path.Wrap(idx + 10));

                Station st = default;
                bool placed = false;
                // Try the chosen shoulder, then the other, each at the lens
                // height and then lifted, until the road is in clear view.
                for (int attempt = 0; attempt < 4 && !placed; attempt++)
                {
                    int sd = attempt < 2 ? side : -side;
                    float lift = attempt % 2 == 0 ? 0f : 5f;
                    Vector3 p = Place(road, right * sd, half, low, groundAt, lift);
                    if (!blocked(p, road + Vector3.up * 0.8f) && !blocked(p, ahead + Vector3.up * 0.8f))
                    { st = new Station { idx = idx, pos = p, low = low }; placed = true; }
                }
                if (!placed)
                    st = new Station { idx = idx, pos = Place(road, right * side, half, false, groundAt, 9f), low = false };
                list.Add(st);
            }
            return list;
        }

        static Vector3 Place(Vector3 road, Vector3 outward, float half, bool low,
                             System.Func<float, float, float> groundAt, float lift)
        {
            float off = half + (low ? LowStandoffM : HighStandoffM);
            Vector3 p = road + outward * off;
            float g = groundAt(p.x, p.z);
            // Nothing under the lens (the far side of a viaduct): stand on the
            // road's own level rather than in a gorge.
            float floor = float.IsNaN(g) ? road.y : Mathf.Max(g, road.y - 2.5f);
            p.y = floor + (low ? LowEyeM : HighEyeM) + lift;
            return p;
        }

        static int Hash(int s)
        {
            unchecked
            {
                uint x = (uint)s * 2654435761u;
                x ^= x >> 13; x *= 0x5bd1e995u; x ^= x >> 15;
                return (int)(x & 0x7fffffff);
            }
        }

        /// <summary>Ground under a world XZ off the physics scene: the road,
        /// the ground and anything solid, never foliage. NaN when the ray
        /// finds nothing at all.</summary>
        static float GroundAt(float x, float z)
        {
            int mask = ~((1 << 2) | (1 << 4) | (1 << LayerMask.NameToLayer("Foliage")));
            if (Physics.Raycast(new Vector3(x, 1500f, z), Vector3.down, out var hit, 3000f,
                                mask, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return float.NaN;
        }

        static bool Blocked(Vector3 from, Vector3 to)
        {
            int mask = ~((1 << 2) | (1 << 4) | (1 << LayerMask.NameToLayer("Foliage")));
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.5f) return false;
            // Short of the target by a car's width, so the road surface itself
            // is not the thing reported as blocking.
            return Physics.Raycast(from, d / len, len - 2.5f, mask, QueryTriggerInteraction.Ignore);
        }

        // ------------------------------------------------------------------
        //  Directing
        // ------------------------------------------------------------------
        void LateUpdate()
        {
            var replay = RaceReplay.Instance;
            var car = replay != null ? replay.Focus : null;
            if (car == null || path == null || cam == null || stations.Count == 0) return;

            carIdx = path.NearestIndex(car.transform.position, carIdx);
            PickStation();
            var st = stations[cur];

            cam.transform.position = st.pos;
            cam.nearClipPlane = baseNear;

            Vector3 target = car.transform.position + Vector3.up * 0.7f;
            Vector3 to = target - st.pos;
            if (to.sqrMagnitude < 1e-4f) return;
            Quaternion want = Quaternion.LookRotation(to, Vector3.up);
            if (!aimSeeded) { aim = want; aimSeeded = true; }
            else aim = Quaternion.Slerp(aim, want, 1f - Mathf.Exp(-PanLag * Time.deltaTime));
            cam.transform.rotation = aim;

            // The zoom: a long lens that opens up as the car arrives. A low
            // lens is a wide one and stays wide.
            float dist = to.magnitude;
            float fov = 2f * Mathf.Atan(FrameAtCarM / Mathf.Max(dist, 1f)) * Mathf.Rad2Deg;
            if (st.low) fov = Mathf.Max(fov, 46f);
            cam.fieldOfView = Mathf.Clamp(fov, MinFOV, MaxFOV);
        }

        /// <summary>Hold the lens until the car has gone PassStations past
        /// it, then cut to the next lens up the road; on the first frame (or
        /// after a seek) take the first lens ahead of the car.</summary>
        void PickStation()
        {
            int n = path.Count;
            if (cur >= 0)
            {
                int passed = Behind(carIdx, stations[cur].idx, n);
                if (passed < PassStations) return;
                // Passed it — but only cut FORWARD to a lens still ahead of the
                // car; a car that has jumped a long way (a seek) re-picks.
                int next = (cur + 1) % stations.Count;
                if (Ahead(stations[next].idx, carIdx, n) < n / 2) { cur = next; aimSeeded = false; return; }
            }
            int best = -1, bestAhead = int.MaxValue;
            for (int i = 0; i < stations.Count; i++)
            {
                int ahead = Ahead(stations[i].idx, carIdx, n);
                // A lens just behind the car still sees it leave; prefer one
                // ahead, but the nearest of either when nothing is ahead.
                if (ahead >= 0 && ahead < bestAhead) { bestAhead = ahead; best = i; }
            }
            if (best < 0) best = 0;
            cur = best;
            aimSeeded = false;
        }

        /// <summary>Stations from the car forward to a lens, wrapping on a loop.</summary>
        int Ahead(int lensIdx, int carAt, int n)
        {
            int d = lensIdx - carAt;
            if (!path.HasEnds) d = ((d % n) + n) % n;
            return d;
        }

        /// <summary>Stations the car is past a lens by (negative if not yet).</summary>
        int Behind(int carAt, int lensIdx, int n)
        {
            int d = carAt - lensIdx;
            if (!path.HasEnds)
            {
                d = ((d % n) + n) % n;
                if (d > n / 2) d -= n;
            }
            return d;
        }
    }
}
