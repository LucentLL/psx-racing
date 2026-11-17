using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The CAMCORDER replay's camera work (see <see cref="Camcorder"/>).
    ///
    /// ROUND 2 (owner, 2026-11-16 21:50: "I don't like the follow cams. ... at
    /// least one camera ... should record at each checkpoint. Logically, it
    /// proves the cars started at the starting line, reached each checkpoint,
    /// then reached the finish line"): NOTHING TRAVELS WITH THE CARS. The
    /// cameras are bystanders at the race's points (<see cref="RacePlan"/>):
    /// the start line, every checkpoint, the finish line. The tape is the
    /// CLIPS they took, in order (<see cref="TapeClips"/>): each picks the car
    /// up about 150 m out, holds it through the point and lets it go 70 m
    /// past; then the tape cuts to the next point's camera (a burst of static,
    /// as between two recordings). With two bystanders at a point the FRONT
    /// one films the car coming and the BACK one takes over as it crosses.
    ///
    /// HANDHELD, as before: the aim follows the car on an under-damped spring
    /// with a human's top pan rate, a breathing sway and a hand tremor, a slow
    /// zoom toward framing the car, the focus lagging and hunting after a cut,
    /// the exposure a little off on every cut.
    /// </summary>
    public class CamcorderCam : MonoBehaviour
    {
        /// <summary>Top pan rate of a hand on a camcorder, deg/s.</summary>
        public const float MaxPanDegS = 240f;
        /// <summary>The static between two recordings, seconds; and the
        /// longest the tape waits for a streamed place to be there.</summary>
        public const float CutStaticS = 0.28f, CutWaitS = 3f;

        RaceReplay replay;
        Camera cam;
        float baseNear = 0.25f, baseFov = 60f;

        RacePlan plan;
        List<TapeClip> clips = new List<TapeClip>();
        int clipsFor = -1;
        int curClip = -1, curLens = -1;
        float lastT = -1f;
        float holdUntil = -1f, holdGiveUp = -1f;
        bool holding;

        // The hand: yaw/pitch and their rates, the zoom, the focus.
        float yaw, pitch, yawVel, pitchVel;
        bool seeded;
        float fov = 40f;
        float focusDist = 30f, focusVel;
        float glitch;
        float seed;
        /// <summary>The hand's own clock: runs with the tape (stops on a pause).</summary>
        float handClock;

        public RacePlan Plan => plan;
        public IReadOnlyList<TapeClip> Clips => clips;
        public int CurrentClip => curClip;
        public int CurrentLens => curLens;
        /// <summary>There are points to film from (else the replay keeps its trackside view).</summary>
        public bool Usable => plan != null && plan.course != null && plan.course.points.Count >= 2;

        public void Setup(RaceReplay rp, Camera c)
        {
            replay = rp; cam = c;
            if (cam != null) { baseNear = cam.nearClipPlane; baseFov = cam.fieldOfView; }
            var rm = RaceManager.Instance;
            plan = Camcorder.TapeCourse != null ? RacePlan.ForTape(Camcorder.TapeCourse, rm) : RacePlan.For(rm, rp);
            clipsFor = -1;
            lastT = -1f;
            holding = false;
            Retarget();
        }

        /// <summary>A seek or a new car: the next frame picks afresh.</summary>
        public void Retarget()
        {
            curLens = -1; seeded = false;
        }

        void OnEnable()
        {
            seeded = false;
            if (cam == null) cam = GetComponent<Camera>();
            Camcorder.Camera = cam;
            Camcorder.EyeReset = true;
            Camcorder.EyeKick = 1f;
            Camcorder.Defocus = 0f;
            curClip = -1; curLens = -1; lastT = -1f; holding = false;
            CamcorderMic.Attach(cam);
            CamcorderOSD.Show(true);
        }

        void OnDisable()
        {
            if (cam != null) { cam.nearClipPlane = baseNear; cam.fieldOfView = baseFov; }
            if (Camcorder.Camera == cam) Camcorder.Clear();
            if (replay != null) replay.Hold = false;
            holding = false;
            CamcorderMic.Detach();
            CamcorderOSD.Show(false);
        }

        /// <summary>The clips of the car being watched (rebuilt when the
        /// focus changes).</summary>
        void EnsureClips()
        {
            if (clipsFor == replay.FocusIndex) return;
            clips = TapeClips.Plan(plan.course, replay.SampleTimes, replay.TrackFrames(replay.FocusIndex));
            if (clips.Count == 0)
                clips.Add(new TapeClip
                {
                    kind = TapeClip.Kind.Start, point = Mathf.Max(0, plan.course.IndexOf(RacePoint.Kind.Start)),
                    lap = 1, tIn = 0f, tPass = 0f, tOut = replay.Duration,
                });
            clipsFor = replay.FocusIndex;
            curClip = -1;
        }

        // ------------------------------------------------------------------
        //  Shooting
        // ------------------------------------------------------------------
        void LateUpdate()
        {
            if (replay == null) replay = RaceReplay.Instance;
            var car = replay != null ? replay.Focus : null;
            if (car == null || cam == null || !Usable) return;
            if (seed == 0f) seed = Random.Range(1f, 100f);
            Camcorder.Camera = cam;
            EnsureClips();

            // ---- which clip the tape is in; between two, cut to the next ----
            float t = replay.ReplayTime;
            int ci = TapeClips.At(clips, t);
            bool ended = false;
            if (ci < 0 && !holding)
            {
                bool backwards = lastT >= 0f && t < lastT - 0.001f;
                float to = -1f;
                if (!backwards)
                {
                    for (int i = 0; i < clips.Count; i++) if (clips[i].tIn > t) { to = clips[i].tIn; break; }
                    // Past the last clip the tape is over: a deck runs out to
                    // its end and stops; a replay goes round again.
                    if (to < 0f) { if (replay.VcrControls) ended = true; else to = clips[0].tIn; }
                }
                else
                {
                    int prev = -1;
                    for (int i = clips.Count - 1; i >= 0; i--) if (clips[i].tOut < t) { prev = i; break; }
                    // A review runs back into the clip before; a skip lands on its start.
                    to = prev < 0 ? clips[0].tIn : replay.Rate < 0f ? clips[prev].tOut - 0.05f : clips[prev].tIn;
                }
                if (!ended)
                {
                    replay.Seek(to, hard: true);
                    t = replay.ReplayTime;
                    ci = TapeClips.At(clips, t);
                }
            }
            lastT = t;
            if (ci < 0) ci = curClip >= 0 ? curClip : clips.Count - 1;   // a tape's end: its last camera holds
            var clip = clips[ci];

            // ---- a new clip: a new recording, from that point's camera ----
            float now = Time.unscaledTime;
            if (ci != curClip)
            {
                // No static before a tape's first picture.
                bool first = curClip < 0 && ci == 0 && t <= clips[0].tIn + 0.1f;
                curClip = ci; curLens = -1;
                holdUntil = first ? now : now + CutStaticS;
                holdGiveUp = now + CutWaitS;
                holding = true;
            }
            int pi = Mathf.Clamp(clip.point, 0, plan.stations.Length - 1);
            var st = plan.stations[pi];
            if (holding)
            {
                // The place may still be streaming in (a city tile): the
                // bystanders are found once the ground is there to stand on.
                bool there = plan.Ensure(pi);
                if ((there && now >= holdUntil) || now >= holdGiveUp) holding = false;
            }
            replay.Hold = holding;
            Camcorder.Static = holding ? 1f : Mathf.MoveTowards(Camcorder.Static, 0f, Time.unscaledDeltaTime * 8f);
            Camcorder.ClipLabel = TapeClips.Label(clip, plan.course.Checkpoints, plan.course.laps);
            Camcorder.ClipSub = plan.course.laps > 1 && clip.kind == TapeClip.Kind.Checkpoint
                ? "LAP " + clip.lap + "/" + plan.course.laps : "";
            if (holding) return;

            // Nobody could stand at this point: its clip is not on the tape.
            if (st.lenses.Count == 0)
            {
                if (clips.Count > 1 && !replay.Paused && !ended)
                {
                    float to = -1f;
                    for (int i = 1; i <= clips.Count; i++)
                    {
                        int k = (ci + i) % clips.Count;
                        if (k <= ci && replay.VcrControls) break;
                        var s2 = plan.stations[Mathf.Clamp(clips[k].point, 0, plan.stations.Length - 1)];
                        if (!s2.judged || s2.lenses.Count > 0) { to = clips[k].tIn; break; }
                    }
                    if (to >= 0f) { replay.Seek(to, hard: true); lastT = replay.ReplayTime; }
                }
                return;
            }

            // FRONT until the car is at the point, BACK after it.
            int li = st.lenses.Count > 1 && t >= clip.tPass ? 1 : 0;
            if (li != curLens) { curLens = li; Cut(); }
            var lens = st.lenses[li];

            // The tape's clock, not the wall's: a paused tape holds the frame
            // still, a scanned one runs the hand as fast as the cars.
            float dt = Mathf.Min(Time.unscaledDeltaTime * Mathf.Abs(replay.Rate), 0.1f);
            if (replay.Paused) dt = 0f;
            handClock += dt;

            Vector3 target = car.transform.position + Vector3.up * 0.7f;
            float tc = handClock + seed;
            // Breathing sway of the whole body, a few centimetres.
            Vector3 eye = lens.pos + new Vector3(Mathf.Sin(tc * 0.9f), Mathf.Sin(tc * 1.3f + 1f) * 0.6f, Mathf.Cos(tc * 0.7f)) * 0.025f;
            float dist = Vector3.Distance(eye, target);
            // Framing the car loosely, tighter when it is far.
            float frameM = lens.crouch ? 7.5f : 9.5f;
            float wantFov = Mathf.Clamp(2f * Mathf.Atan(frameM / Mathf.Max(dist, 1f)) * Mathf.Rad2Deg, lens.far ? 5f : 9f, 50f);

            // ---- the aim: a hand on a spring, top speed a human's ----
            Vector3 to3 = target - eye;
            if (to3.sqrMagnitude < 1e-4f) return;
            float wantYaw = Mathf.Atan2(to3.x, to3.z) * Mathf.Rad2Deg;
            float wantPitch = -Mathf.Atan2(to3.y, new Vector2(to3.x, to3.z).magnitude) * Mathf.Rad2Deg;
            if (!seeded)
            {
                yaw = wantYaw; pitch = wantPitch; yawVel = pitchVel = 0f;
                fov = wantFov * Random.Range(0.9f, 1.3f);
                focusDist = to3.magnitude * Random.Range(0.6f, 1.6f);
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

            // ---- the zoom: a thumb on the rocker, a little behind ----
            const float zoomRate = 0.9f;   // log-fov per second
            float lf = Mathf.Log(fov), lw = Mathf.Log(wantFov);
            lf = Mathf.MoveTowards(lf, lw, zoomRate * dt * Mathf.Clamp01(Mathf.Abs(lw - lf) * 6f + 0.15f));
            fov = Mathf.Exp(lf);

            // ---- the hand: tremor and drift ----
            float tt = handClock;
            float n1 = Mathf.PerlinNoise(tt * 2.1f, seed) - 0.5f, n2 = Mathf.PerlinNoise(seed, tt * 2.6f) - 0.5f;
            float n3 = Mathf.PerlinNoise(tt * 7.5f, seed + 5f) - 0.5f, n4 = Mathf.PerlinNoise(seed + 9f, tt * 0.35f) - 0.5f;
            float shakeYaw = n1 * 0.9f + n3 * 0.25f + n4 * 1.2f;
            float shakePitch = n2 * 0.8f + n3 * 0.2f;
            float roll = n4 * 4f + n1 * 1.2f;   // camcorders are rarely level
            cam.transform.position = eye;
            cam.transform.rotation = Quaternion.Euler(pitch + shakePitch, yaw + shakeYaw, roll);
            cam.fieldOfView = fov;
            cam.nearClipPlane = baseNear;

            // ---- the focus: lagging, hunting after a cut ----
            float d = to3.magnitude;
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

            CamcorderMic.Wind = 0.35f;
            CamcorderMic.Scan = Camcorder.Transport == 2 ? Camcorder.ScanRate : 0f;
            glitch = Mathf.Max(0f, glitch - dt * 2.5f);
            Camcorder.Tracking = glitch;
        }

        /// <summary>A new recording starts: the hand, the zoom and the focus
        /// pick up where a person would, the exposure a little off.</summary>
        void Cut()
        {
            seeded = false;
            Camcorder.EyeReset = true;
            Camcorder.EyeKick = Random.Range(0.6f, 1.6f);
            glitch = Random.value < 0.6f ? Random.Range(0.25f, 0.6f) : 0f;
        }
    }
}
