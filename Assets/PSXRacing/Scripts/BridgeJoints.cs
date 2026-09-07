using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The expansion joints on a bridge deck — seen, heard and felt.
    ///
    /// Every span of a real trestle ends in a steel finger joint, and on the
    /// Langston and Atlantic Beach bridges they are the most obvious thing
    /// about driving over them: a regular metallic double-knock every twenty-odd
    /// metres, all the way across. The bands are drawn by the builder; this is
    /// the half you cannot see.
    ///
    /// WHY THIS IS NOT GEOMETRY. The obvious implementation is a raised strip on
    /// the deck and let the suspension find it. It cannot: the wheels are
    /// raycasts sampled once per physics tick, and at 200 km/h a wheel moves
    /// 0.93 m between ticks — so a 30 cm joint is missed far more often than it
    /// is hit, and whether you feel a bridge would depend on your speed in a way
    /// that has nothing to do with the bridge. Worse, the misses are not random
    /// to the player: they alias against speed, so a joint you felt on the way
    /// out is silent on the way back.
    ///
    /// So the joints live where they actually are — at known DISTANCES along the
    /// route — and a car crossing one is detected from its progress rather than
    /// from a collision. That is exact at any speed, costs one nearest-waypoint
    /// query per car per tick (which the AI is doing anyway), and gives the
    /// sound and the jolt a single shared trigger so they cannot drift apart.
    /// </summary>
    public class BridgeJoints : MonoBehaviour
    {
        /// <summary>Waypoint index of each joint, ascending. Baked by the
        /// builder from the same pier stations the deck is built on, so a
        /// joint is always over a pier rather than floating mid-span.</summary>
        public int[] jointIndex = new int[0];
        public TrackPath path;

        /// <summary>
        /// TURN THE PIERS ROUND WITH THE ROAD.
        ///
        /// These are waypoint INDICES baked at build time, which makes them the
        /// same kind of thing as TrackPath.finishIndex: the flip moves every
        /// point in the list and leaves the numbers pointing at whatever now
        /// happens to sit at that subscript. On a reversed venue that meant the
        /// viaduct went silent — a car on the real deck has an index that is
        /// not in the list — while the jolt and the clang fired somewhere else
        /// entirely, which on a mountain stage is kilometres away, on plain
        /// tarmac, potentially mid-corner.
        ///
        /// A METHOD RATHER THAN A FIELD WRITE, because Start bakes the array
        /// into the isJoint lookup and its order against RaceManager.Start —
        /// which is what calls the reversal — is undefined. Rewriting the array
        /// alone would work only when this component's Start had not run yet.
        /// The same shape, and for the same reason, as AIDriver.ReseedPath.
        /// </summary>
        public void ReverseIndices()
        {
            if (path == null || path.Count == 0 || jointIndex == null) return;
            int n = path.Count;
            for (int i = 0; i < jointIndex.Length; i++)
            {
                int j = jointIndex[i];
                // The mapping ReverseInPlace itself used: a straight flip on a
                // route with ends, and on a loop 0 stays put while the rest
                // walk backwards round it.
                jointIndex[i] = path.HasEnds ? n - 1 - j : (j == 0 ? 0 : n - j);
            }
            System.Array.Sort(jointIndex);   // the walk below assumes ascending

            // Rebuild the lookup if Start already built one; leave it null if it
            // has not, so Start still does the work in its own good time.
            if (isJoint != null)
            {
                isJoint = new bool[n];
                foreach (int i in jointIndex)
                    if (i >= 0 && i < n) isJoint[i] = true;
            }

            // AND RE-SEED WHERE EVERY CAR IS, which is why this has to run
            // AFTER the grid is restaged rather than before.
            //
            // lastIdx is the other half of the state: FixedUpdate walks from it
            // to the current index and fires every joint in between. A car that
            // was seeded on the forward grid is holding an index into a list
            // that no longer means the same thing, and NearestIndex takes it as
            // a HINT and only searches 25 waypoints either side of it — so it
            // would not merely be stale, it would latch onto whatever happened
            // to be near the wrong subscript and stay there. RE-SEEDED rather
            // than cleared: FixedUpdate skips a car with no entry and nothing
            // ever puts one back, so clearing would silence the bridge for the
            // rest of the race.
            for (int i = 0; i < cars.Count; i++)
            {
                var c = cars[i];
                if (c != null) lastIdx[c] = path.NearestIndex(c.transform.position);
            }
        }

        /// <summary>Upward velocity added per m/s of road speed, and the cap.
        /// A joint is a jolt, not a jump: 0.28 m/s at 60 m/s is a distinct
        /// knock through the seat that never unsettles the car mid-corner.
        /// </summary>
        public float kickPerMps = 0.0047f;
        public float maxKick = 0.30f;
        /// <summary>Pitch impulse, rad/s. The rear axle crosses a beat after the
        /// front, so the car noses up then settles — without this the jolt reads
        /// as the whole car being lifted, which feels like a lift not a joint.
        /// </summary>
        public float pitchPerMps = 0.0032f;
        public float maxPitch = 0.22f;
        /// <summary>Below this a car is parked on a bridge, not crossing a
        /// joint. Also stops a stationary car from being nudged forever by a
        /// nearest-index that jitters across a joint station.</summary>
        public float minSpeedKmh = 12f;

        [Range(0f, 1f)] public float volume = 0.55f;

        static AudioClip jointClip;
        const int SampleRate = 22050;

        readonly Dictionary<CarController, int> lastIdx = new Dictionary<CarController, int>();
        readonly List<CarController> cars = new List<CarController>();
        AudioSource src;
        bool[] isJoint;          // by waypoint index, for O(1) crossing tests

        void Start()
        {
            if (path == null) path = GetComponent<TrackPath>();
            if (path == null || path.Count == 0 || jointIndex.Length == 0) { enabled = false; return; }

            // A flat lookup rather than a scan of the joint list: a car can
            // cross several joints in one tick at speed (26 m spacing, 0.9 m a
            // tick, so not often — but a respawn or a stutter can jump it), and
            // the walk below has to check every index in between.
            isJoint = new bool[path.Count];
            foreach (int i in jointIndex)
                if (i >= 0 && i < isJoint.Length) isJoint[i] = true;

            cars.AddRange(FindObjectsByType<CarController>(FindObjectsSortMode.None));
            foreach (var c in cars)
                lastIdx[c] = path.NearestIndex(c.transform.position);

            EnsureClip();
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;       // the player's own car; the AI's joints
                                         // are not audible from inside a cabin
            src.clip = null;
        }

        void FixedUpdate()
        {
            for (int c = 0; c < cars.Count; c++)
            {
                var car = cars[c];
                if (car == null) continue;
                if (!lastIdx.TryGetValue(car, out int prev)) continue;

                int now = path.NearestIndex(car.transform.position, prev);
                if (now == prev) continue;
                lastIdx[car] = now;

                if (Mathf.Abs(car.speedKmh) < minSpeedKmh) continue;

                // Walk the indices actually traversed. Only FORWARD travel
                // counts — reversing over a joint is real but rare, and a car
                // rocking on the spot across one station would otherwise
                // machine-gun the clip.
                int crossed = 0;
                for (int i = prev + 1; i <= now && i < isJoint.Length; i++)
                    if (isJoint[i]) crossed++;
                // A respawn teleports the index; do not fire a burst for the
                // whole bridge because a car was put back on the line.
                if (crossed == 0 || now - prev > 12) continue;

                Hit(car, crossed);
            }
        }

        void Hit(CarController car, int count)
        {
            var body = car.GetComponent<Rigidbody>();
            float v = Mathf.Abs(car.speedKmh) / 3.6f;

            if (body != null)
            {
                float kick = Mathf.Min(v * kickPerMps, maxKick);
                float pitch = Mathf.Min(v * pitchPerMps, maxPitch);
                body.linearVelocity += Vector3.up * kick;
                // About the car's own right axis, so it noses up regardless of
                // which way the bridge is pointing.
                body.AddTorque(car.transform.right * pitch, ForceMode.VelocityChange);
            }

            // Only the player's car is heard. Sorting out whose joint this was
            // by distance would be the correct thing on a circuit full of cars;
            // on a bridge everyone crosses the same joints within a second of
            // each other and the result is a rattle, not a bridge.
            if (src == null || jointClip == null) return;
            if (!IsPlayer(car)) return;
            // Pitch rises a little with speed and the count of joints taken in
            // one tick, so a fast crossing sounds tighter than a slow one.
            src.pitch = Mathf.Clamp(0.86f + v * 0.006f + (count - 1) * 0.05f, 0.8f, 1.5f);
            src.PlayOneShot(jointClip, volume * Mathf.Clamp01(0.35f + v / 40f));
        }

        static bool IsPlayer(CarController car) =>
            car != null && car.GetComponent<PlayerCarInput>() != null;

        /// <summary>
        /// The knock, synthesised once. Same approach as CollisionAudio: a real
        /// AudioClip built with SetData, because WebGL never runs
        /// OnAudioFilterRead and an async-decoded asset is a silent loop.
        ///
        /// Two strikes 38 ms apart — front axle, then rear — each a short noise
        /// burst through a resonant tap, so it reads as steel rather than as a
        /// bump on tarmac.
        /// </summary>
        static void EnsureClip()
        {
            if (jointClip != null) return;
            int n = (int)(SampleRate * 0.20f);
            var buf = new float[n];
            var rng = new System.Random(9137);

            // Two resonant modes give the strike a pitch without it becoming a
            // note; 190 and 330 Hz are deliberately not harmonically related.
            AddStrike(buf, rng, 0, 190f, 330f, 1.00f);
            AddStrike(buf, rng, (int)(SampleRate * 0.038f), 190f, 330f, 0.72f);

            float peak = 0f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(buf[i]));
            if (peak > 0f) for (int i = 0; i < n; i++) buf[i] /= peak;

            jointClip = AudioClip.Create("psx_bridge_joint", n, 1, SampleRate, false);
            jointClip.SetData(buf, 0);
        }

        static void AddStrike(float[] buf, System.Random rng, int at,
                              float f1, float f2, float amp)
        {
            int len = Mathf.Min(buf.Length - at, (int)(SampleRate * 0.13f));
            if (len <= 0) return;
            // One-pole lowpassed noise for the thud body, plus two decaying
            // sines for the metal.
            float lp = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-t * 46f);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.34f;
                float ring = Mathf.Sin(2f * Mathf.PI * f1 * t) * Mathf.Exp(-t * 34f) * 0.5f
                           + Mathf.Sin(2f * Mathf.PI * f2 * t) * Mathf.Exp(-t * 58f) * 0.3f;
                buf[at + i] += (lp * 0.75f + ring) * env * amp;
            }
        }
    }
}
