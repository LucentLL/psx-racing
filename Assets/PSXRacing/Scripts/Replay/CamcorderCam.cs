using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The CAMCORDER replay's camera work (see <see cref="Camcorder"/>): the
    /// owner's reference is 1990s street-racing tapes, shot by people standing
    /// at the roadside with a camcorder in their hands.
    ///
    /// OPERATORS, planted from the route like the trackside director's lenses
    /// (<see cref="ReplayCamera.Plan"/>), but people: an eye at 1.6 m on the
    /// ground they stand on (a crouch at the kerb now and then), on the
    /// OUTSIDE of the corner ahead, off the road - a spot whose ground is road
    /// is moved out until it is not, and dropped if it never is; a spot up a
    /// building or down a gorge is dropped too. Where a bridge crosses over
    /// the route the operator stands at its railing looking down the road.
    /// Never in a lane.
    ///
    /// HANDHELD: the aim follows the car on an under-damped spring with a
    /// human's top pan rate, so a close pass is a WHIP-PAN that overshoots and
    /// settles; a breathing sway and a hand tremor on top; a ZOOM servo that
    /// moves slowly toward framing the car; the FOCUS lagging the car's
    /// distance and HUNTING after a cut (published as a soft blur); the
    /// exposure kicked a little off on every cut so the eye settles again.
    ///
    /// Now and then a BUDDY'S CAR: a camcorder in the passenger seat of a car
    /// running the focus car's own line half a second behind it (so it never
    /// drives anywhere the race did not), bouncing with the road.
    /// </summary>
    public class CamcorderCam : MonoBehaviour
    {
        public enum Kind { Roadside = 0, Kerb = 1, Overpass = 2 }

        public struct Spot
        {
            public int idx;
            public Vector3 pos;
            public Kind kind;
        }

        /// <summary>Metres of road per operator (jittered).</summary>
        public const float SpotEveryM = 130f;
        public const float EyeM = 1.6f, CrouchM = 1.05f;
        /// <summary>Clear of the road edge: a kerb operator and one further back.</summary>
        public const float KerbStandoffM = 2.2f, BackStandoffM = 3.8f;   // inside the cleared verge, in front of the tree line
        /// <summary>Every this many shots, the buddy's car.</summary>
        public const int ChaseEvery = 3;
        public const float ChaseSeconds = 7f;
        /// <summary>Top pan rate of a hand on a camcorder, deg/s.</summary>
        public const float MaxPanDegS = 240f;

        TrackPath path;
        Camera cam;
        float roadWidth = 12f;
        System.Func<int, bool> avoid;
        float baseNear = 0.25f, baseFov = 60f;
        readonly List<Spot> spots = new List<Spot>();

        int cur = -1;
        int carIdx = -1;
        int shots;
        bool chasing;
        float chaseUntil;
        float shotStart;
        // The hand: yaw/pitch and their rates, the zoom, the focus.
        float yaw, pitch, yawVel, pitchVel;
        bool seeded;
        float fov = 40f;
        float focusDist = 30f, focusVel;
        float glitch;
        float seed;
        /// <summary>The hand's own clock: runs with the tape (stops on a pause).</summary>
        float handClock;

        public IReadOnlyList<Spot> Spots => spots;
        public bool Chasing => chasing;
        public int Current => cur;

        public void Setup(TrackPath p, Camera c, float roadWidthM, System.Func<int, bool> avoidAt = null)
        {
            path = p; cam = c; roadWidth = roadWidthM; avoid = avoidAt;
            if (cam != null) { baseNear = cam.nearClipPlane; baseFov = cam.fieldOfView; }
            spots.Clear();
            if (path != null && path.Count > 2)
                spots.AddRange(Plan(path, roadWidth, GroundAt, Blocked, avoidAt));
            Retarget();
        }

        /// <summary>A seek or a new car: the next frame picks afresh.</summary>
        public void Retarget()
        {
            cur = -1; carIdx = -1; seeded = false; chasing = false;
        }

        void OnEnable()
        {
            seeded = false;
            if (cam == null) cam = GetComponent<Camera>();
            Camcorder.Camera = cam;
            Camcorder.EyeReset = true;
            Camcorder.EyeKick = 1f;
            Camcorder.Defocus = 0f;
            CamcorderMic.Attach(cam);
            CamcorderOSD.Show(true);
        }

        void OnDisable()
        {
            if (cam != null) { cam.nearClipPlane = baseNear; cam.fieldOfView = baseFov; }
            if (Camcorder.Camera == cam) Camcorder.Clear();
            CamcorderMic.Detach();
            CamcorderOSD.Show(false);
        }

        // ------------------------------------------------------------------
        //  Planning (pure over its delegates, like ReplayCamera.Plan)
        // ------------------------------------------------------------------
        public static List<Spot> Plan(TrackPath path, float roadWidth,
                                      System.Func<float, float, Vector2> groundAt,
                                      System.Func<Vector3, Vector3, bool> blocked,
                                      System.Func<int, bool> avoid = null)
        {
            var list = new List<Spot>();
            int n = path.Count;
            if (n < 4) return list;
            int every = Mathf.Max(8, Mathf.RoundToInt(SpotEveryM / Mathf.Max(0.5f, path.spacing)));
            int count = Mathf.Max(1, n / every);
            bool loop = !path.HasEnds;
            float half = roadWidth * 0.5f;
            int lastSide = 1;
            for (int s = 0; s < count; s++)
            {
                int idx = Mathf.Clamp(s * every + Hash(s * 7 + 3) % Mathf.Max(1, every / 2), 0, n - 1);
                if (!loop && (idx < 6 || idx >= n - 6)) continue;
                if (avoid != null && avoid(idx)) continue;
                if (TryPlace(path, idx, s, half, groundAt, blocked, ref lastSide, out var spot)) list.Add(spot);
            }
            return list;
        }

        /// <summary>One operator by station <paramref name=idx/>: at a bridge's
        /// railing over it, or on the outside of the corner ahead (the other
        /// shoulder, then further back, when that will not do). False when no
        /// spot here passes: never road, nor road further out, nor a roof or a
        /// gorge, and the road in sight.</summary>
        public static bool TryPlace(TrackPath path, int idx, int s, float half,
                                    System.Func<float, float, Vector2> groundAt,
                                    System.Func<Vector3, Vector3, bool> blocked, ref int lastSide, out Spot spot)
        {
            spot = default;
            Vector3 road = path.GetPoint(idx);
            Vector3 tan = path.GetTangent(idx);
            tan.y = 0f;
            if (tan.sqrMagnitude < 1e-6f) return false;
            tan.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, tan).normalized;
            Vector3 ahead = path.GetPoint(path.Wrap(idx + 12));

            // A bridge over the route here: stand at its railing.
            if (TryOverpass(road, tan, groundAt, blocked, ahead, out var op))
            {
                spot = new Spot { idx = idx, pos = op, kind = Kind.Overpass };
                return true;
            }

            float turn = 0f;
            for (int k = 1; k <= 12; k++)
            {
                int a = path.Wrap(idx + k - 1), b = path.Wrap(idx + k);
                turn += Vector3.SignedAngle(path.GetTangent(a), path.GetTangent(b), Vector3.up);
            }
            int side = Mathf.Abs(turn) < 6f ? -lastSide : (turn < 0f ? 1 : -1);
            lastSide = side;
            bool kerb = Hash(s * 13 + 1) % 3 == 0;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                int sd = attempt < 3 ? side : -side;
                float off = half + (kerb ? KerbStandoffM : BackStandoffM) + (attempt % 3) * 3f;
                Vector3 p = road + right * sd * off;
                Vector2 g = groundAt(p.x, p.z);   // x: height, y: 1 = road surface
                if (float.IsNaN(g.x)) continue;
                if (g.y > 0.5f) continue;                       // a lane: never
                if (g.x > road.y + 12f || g.x < road.y - 2.5f) continue;   // a roof, a gorge
                // Road further out too: a median between two carriageways,
                // or an island in a junction - nobody stands there (run 1,
                // Uptown Loop: an operator crouched in the I-277 median).
                Vector3 o1 = p + right * sd * 2.5f, o2 = p + right * sd * 6f;
                if (groundAt(o1.x, o1.z).y > 0.5f || groundAt(o2.x, o2.z).y > 0.5f) continue;
                bool crouch = kerb && Hash(s * 31 + attempt) % 2 == 0;
                p.y = g.x + (crouch ? CrouchM : EyeM);
                // Aimed at a car's roof: over a Jersey barrier.
                if (blocked(p, road + Vector3.up * 1.1f) || blocked(p, ahead + Vector3.up * 1.1f)) continue;
                spot = new Spot { idx = idx, pos = p, kind = kerb ? Kind.Kerb : Kind.Roadside };
                return true;
            }
            return false;
        }

        /// <summary>A deck 4-14 m over the route: the railing on the side the
        /// cars come from, an eye over it.</summary>
        static bool TryOverpass(Vector3 road, Vector3 tan, System.Func<float, float, Vector2> groundAt,
                                System.Func<Vector3, Vector3, bool> blocked, Vector3 ahead, out Vector3 pos)
        {
            pos = default;
            // Looked for from ABOVE: a deck's collider faces up, and a ray from
            // the road below passes through its back face (run 1 found none).
            Vector3 from = road + Vector3.up * 18f;
            if (!Physics.Raycast(from, Vector3.down, out var hit, 16.5f, SolidMask, QueryTriggerInteraction.Ignore)) return false;
            if (hit.point.y - road.y < 4f) return false;
            float deckY = hit.point.y;
            // Walk back down the route (toward the oncoming car) to the deck's edge.
            float edge = -1f;
            for (float s = 1f; s <= 16f; s += 1f)
            {
                Vector3 q = road - tan * s;
                if (!Physics.Raycast(new Vector3(q.x, deckY + 3f, q.z), Vector3.down, out var h2, 5f, SolidMask, QueryTriggerInteraction.Ignore)
                    || Mathf.Abs(h2.point.y - deckY) > 1.5f)
                { edge = s - 1f; break; }
            }
            if (edge < 2f) return false;
            Vector3 stand = road - tan * (edge - 0.7f);
            Vector2 g = groundAt(stand.x, stand.z);
            if (float.IsNaN(g.x) || Mathf.Abs(g.x - deckY) > 2f) return false;
            pos = new Vector3(stand.x, g.x + EyeM, stand.z);
            // Looking down the road from the railing.
            Vector3 down = road - tan * 40f + Vector3.up * 0.8f;
            return !blocked(pos, down);
        }

        static int SolidMask => ~((1 << 2) | (1 << 4) | (1 << LayerMask.NameToLayer("Foliage")));

        static int Hash(int s)
        {
            unchecked
            {
                uint x = (uint)s * 2654435761u;
                x ^= x >> 13; x *= 0x5bd1e995u; x ^= x >> 15;
                return (int)(x & 0x7fffffff);
            }
        }

        /// <summary>Ground under a world XZ (x) and whether it is road (y = 1).
        /// NaN when nothing is there.</summary>
        static Vector2 GroundAt(float x, float z)
        {
            if (Physics.Raycast(new Vector3(x, 1500f, z), Vector3.down, out var hit, 3000f,
                                SolidMask, QueryTriggerInteraction.Ignore))
                return new Vector2(hit.point.y, hit.collider.gameObject.layer == 8 ? 1f : 0f);
            return new Vector2(float.NaN, 0f);
        }

        static bool Blocked(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.5f) return false;
            if (Physics.Raycast(from, d / len, len - 2.5f, SolidMask, QueryTriggerInteraction.Ignore)) return true;
            // A tree right in front of the lens (run 2: a trunk filled a shot).
            // Only near: the woods further off are what the road runs through.
            int foliage = LayerMask.NameToLayer("Foliage");
            return foliage >= 0 && Physics.Raycast(from, d / len, Mathf.Min(7f, len - 2.5f), 1 << foliage,
                                                   QueryTriggerInteraction.Ignore);
        }

        // ------------------------------------------------------------------
        //  Shooting
        // ------------------------------------------------------------------
        void LateUpdate()
        {
            var replay = RaceReplay.Instance;
            var car = replay != null ? replay.Focus : null;
            if (car == null || path == null || cam == null) return;
            // The tape's clock, not the wall's: a paused tape holds the frame
            // still, a scanned one runs the hand as fast as the cars.
            float dt = Mathf.Min(Time.unscaledDeltaTime * Mathf.Abs(replay.Rate), 0.1f);
            if (replay.Paused) dt = 0f;
            handClock += dt;
            if (seed == 0f) seed = Random.Range(1f, 100f);

            carIdx = path.NearestIndex(car.transform.position, carIdx);
            PickShot(replay, car);
            Camcorder.Camera = cam;

            Vector3 target = car.transform.position + Vector3.up * 0.7f;
            Vector3 eye;
            float wantFov;
            float tremor;
            if (chasing)
            {
                // The buddy's car: the focus car's own line, a beat behind.
                if (!BuddyEye(replay, out eye, out _))
                    eye = car.transform.position - car.transform.forward * 15f + Vector3.up * 1.22f;
                float rough = Mathf.PerlinNoise(handClock * 9f, seed) - 0.5f;
                eye += Vector3.up * rough * 0.03f;
                wantFov = 38f;
                tremor = 1.6f;
            }
            else
            {
                var sp = spots[cur];
                float t = handClock + seed;
                // Breathing sway of the whole body, a few centimetres.
                eye = sp.pos + new Vector3(Mathf.Sin(t * 0.9f), Mathf.Sin(t * 1.3f + 1f) * 0.6f, Mathf.Cos(t * 0.7f)) * 0.025f;
                float dist = Vector3.Distance(eye, target);
                // Framing the car loosely, tighter when it is far.
                float frameM = sp.kind == Kind.Kerb ? 7.5f : 9.5f;
                wantFov = Mathf.Clamp(2f * Mathf.Atan(frameM / Mathf.Max(dist, 1f)) * Mathf.Rad2Deg, 9f, 50f);
                tremor = 1f;
            }

            // ---- the aim: a hand on a spring, top speed a human's ----
            Vector3 to = target - eye;
            if (to.sqrMagnitude < 1e-4f) return;
            float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float wantPitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            if (!seeded)
            {
                yaw = wantYaw; pitch = wantPitch; yawVel = pitchVel = 0f;
                fov = wantFov * Random.Range(0.85f, 1.25f);
                focusDist = to.magnitude * Random.Range(0.55f, 1.8f);
                focusVel = 0f;
                seeded = true;
            }
            const float omega = 7.5f, zeta = 0.62f;
            float dy = Mathf.DeltaAngle(yaw, wantYaw);
            float acc = omega * omega * dy - 2f * zeta * omega * yawVel;
            yawVel = Mathf.Clamp(yawVel + acc * dt, -MaxPanDegS, MaxPanDegS);
            yaw += yawVel * dt;
            float dp = Mathf.DeltaAngle(pitch, wantPitch);
            pitchVel = Mathf.Clamp(pitchVel + (omega * omega * dp - 2f * zeta * omega * pitchVel) * dt, -120f, 120f);
            pitch += pitchVel * dt;

            // ---- the zoom servo: slow, a little behind ----
            float zoomRate = 0.55f;   // log-fov per second
            float lf = Mathf.Log(fov), lw = Mathf.Log(wantFov);
            lf = Mathf.MoveTowards(lf, lw, zoomRate * dt * Mathf.Clamp01(Mathf.Abs(lw - lf) * 6f + 0.15f));
            fov = Mathf.Exp(lf);

            // ---- the hand: tremor and drift, bigger on a long lens ----
            float tt = handClock;
            float n1 = Mathf.PerlinNoise(tt * 2.1f, seed) - 0.5f, n2 = Mathf.PerlinNoise(seed, tt * 2.6f) - 0.5f;
            float n3 = Mathf.PerlinNoise(tt * 7.5f, seed + 5f) - 0.5f, n4 = Mathf.PerlinNoise(seed + 9f, tt * 0.35f) - 0.5f;
            float shakeYaw = (n1 * 0.9f + n3 * 0.25f + n4 * 1.2f) * tremor;
            float shakePitch = (n2 * 0.8f + n3 * 0.2f) * tremor;
            float roll = (n4 * 4f + n1 * 1.2f) * tremor;   // camcorders are rarely level
            cam.transform.position = eye;
            cam.transform.rotation = Quaternion.Euler(pitch + shakePitch, yaw + shakeYaw, roll);
            cam.fieldOfView = fov;
            cam.nearClipPlane = chasing ? 0.3f : baseNear;

            // ---- the focus: lagging, hunting after a cut ----
            float d = to.magnitude;
            const float fOmega = 6f, fZeta = 0.35f;   // under-damped: it hunts
            float fAcc = fOmega * fOmega * (d - focusDist) - 2f * fZeta * fOmega * focusVel;
            focusVel += fAcc * dt;
            focusDist = Mathf.Max(1f, focusDist + focusVel * dt);
            float coc = Mathf.Abs(1f / focusDist - 1f / Mathf.Max(d, 1f)) * 10f /
                        Mathf.Max(0.05f, Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
            Camcorder.Defocus = Mathf.Clamp01(coc * 1.4f);

            // ---- the smear of a fast pan at 1/60 s, in screen widths ----
            float hfov = 2f * Mathf.Atan(Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * 4f / 3f) * Mathf.Rad2Deg;
            float smear = (yawVel / Mathf.Max(hfov, 1f)) * (1f / 60f);
            Camcorder.PanSmear = Mathf.Clamp(smear, -0.03f, 0.03f) * (4f / 3f) / Mathf.Max(cam.aspect, 0.5f);

            // ---- a glitch at the start of a recording, now and then ----
            Camcorder.Interior = chasing;
            CamcorderMic.Wind = chasing ? 0.8f : cur >= 0 && cur < spots.Count && spots[cur].kind == Kind.Overpass ? 0.55f : 0.35f;
            CamcorderMic.Scan = Camcorder.Transport == 2 ? Camcorder.ScanRate : 0f;
            glitch = Mathf.Max(0f, glitch - dt * 2.5f);
            if (dt > 0f && Random.value < dt * 0.02f) glitch = Mathf.Max(glitch, Random.Range(0.3f, 0.7f));
            Camcorder.Tracking = glitch;
        }

        void PickShot(RaceReplay replay, CarController car)
        {
            int n = path.Count;
            bool needNew = cur < 0 && !chasing;
            if (chasing)
            {
                // The buddy's car stops filming when a car comes up at the lens.
                if (handClock < chaseUntil && BuddySafe(replay)) return;
                chasing = false;
                needNew = true;
            }
            else if (cur >= 0)
            {
                var sp = spots[cur];
                int passed = Behind(carIdx, sp.idx, n);
                float dist = Vector3.Distance(sp.pos, car.transform.position);
                bool gone = (passed > 0 && passed * path.spacing > 55f) || (passed > 0 && dist > 140f);
                if (!gone) return;
                needNew = true;
            }
            if (!needNew) return;

            shots++;
            int best = NearestAhead(n, out int bestAhead);
            bool farAhead = best < 0 || bestAhead * path.spacing > 300f;
            if (farAhead)
            {
                // Nobody planned near: somebody turned up to watch, 60-180 m on.
                int added = AddSpotAhead(n);
                if (added >= 0) { best = added; farAhead = false; }
            }
            bool wantChase = farAhead || shots % ChaseEvery == 0;
            if (wantChase && BuddySafe(replay))
            {
                chasing = true;
                chaseUntil = handClock + ChaseSeconds;
                cur = Mathf.Max(0, best);
            }
            else if (best >= 0) { chasing = false; cur = best; }
            else
            {
                // Nobody ahead and no safe ride: the nearest operator of all.
                cur = NearestAny(car.transform.position);
                if (cur < 0) { chasing = true; chaseUntil = handClock + 1f; }
            }
            Cut();
        }

        int NearestAhead(int n, out int bestAhead)
        {
            int best = -1;
            bestAhead = int.MaxValue;
            for (int i = 0; i < spots.Count; i++)
            {
                int ahead = Ahead(spots[i].idx, carIdx, n);
                if (ahead >= 3 && ahead < bestAhead) { bestAhead = ahead; best = i; }
            }
            return best;
        }

        int NearestAny(Vector3 at)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < spots.Count; i++)
            {
                float d = (spots[i].pos - at).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>An operator where the plan left nobody (a city freeway, a
        /// grid): tried 60-180 m ahead of the car by the planner's own rules.
        /// Kept for the rest of the tape. -1 when none will do.</summary>
        int AddSpotAhead(int n)
        {
            float half = roadWidth * 0.5f;
            foreach (int k in new[] { 15, 22, 30, 38, 45 })
            {
                int idx = carIdx + k;
                if (path.HasEnds) { if (idx >= n - 6) break; }
                else idx = path.Wrap(idx);
                if (avoid != null && avoid(idx)) continue;
                if (!TryPlace(path, idx, shots * 97 + k, half, GroundAt, Blocked, ref addSide, out var sp)) continue;
                spots.Add(sp);
                return spots.Count - 1;
            }
            return -1;
        }
        int addSide = 1;

        /// <summary>Where the buddy's lens is: the passenger seat of a car on
        /// the focus car's own line, a beat behind it.</summary>
        bool BuddyEye(RaceReplay replay, out Vector3 eye, out Quaternion rot)
        {
            float spd = Mathf.Max(5f, Mathf.Abs(replay.FocusFrame.speedKmh) / 3.6f);
            float lag = Mathf.Clamp(15f / spd, 0.35f, 1.3f);
            eye = default; rot = Quaternion.identity;
            if (replay.ReplayTime - lag < 0f) return false;
            if (!replay.PoseAt(replay.FocusIndex, replay.ReplayTime - lag, out var bp, out rot)) return false;
            eye = bp + rot * new Vector3(0.38f, 1.22f, 0f);
            return true;
        }

        /// <summary>The ride is filmable: the field is moving (no ride on the
        /// grid - run 2 put the lens inside a car there) and no car of the
        /// field is within a car's length of the lens.</summary>
        bool BuddySafe(RaceReplay replay)
        {
            if (replay.ReplayTime < 4f || Mathf.Abs(replay.FocusFrame.speedKmh) < 35f) return false;
            if (!BuddyEye(replay, out var eye, out _)) return false;
            for (int i = 0; i < replay.TrackCount; i++)
            {
                var c = replay.TrackCar(i);
                if (c != null && (c.transform.position - eye).sqrMagnitude < 5.5f * 5.5f) return false;
            }
            return true;
        }

        /// <summary>A new recording starts: the hand, the zoom and the focus
        /// pick up where a person would, the exposure a little off.</summary>
        void Cut()
        {
            seeded = false;
            shotStart = Time.time;
            Camcorder.EyeReset = true;
            Camcorder.EyeKick = Random.Range(0.55f, 1.7f);
            glitch = Random.value < 0.6f ? Random.Range(0.25f, 0.6f) : 0f;
        }

        int Ahead(int lensIdx, int carAt, int n)
        {
            int d = lensIdx - carAt;
            if (!path.HasEnds) d = ((d % n) + n) % n;
            return d;
        }

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
