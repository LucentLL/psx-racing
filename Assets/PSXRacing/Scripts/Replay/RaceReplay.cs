using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PSXRacing
{
    /// <summary>
    /// THE REPLAY. Records every car on the grid from the countdown to the
    /// flag, and plays the race back through trackside cameras the way Gran
    /// Turismo did — the car sweeping past a fixed lens that pans and zooms
    /// to hold it, cut to the next lens as it goes by.
    ///
    /// RECORDING is a sample of each car's pose and the handful of visual
    /// states nothing could re-derive from a pose — steer angle, wheel roll,
    /// revs, gear, brake, the body lean — thirty times a second. That is a
    /// few megabytes for a long race and nothing for a short one, and it is
    /// taken in FixedUpdate off the same physics step the pose came from.
    /// Nothing about the recording touches the race.
    ///
    /// PLAYBACK puts every car's rigidbody in kinematic mode and moves it
    /// with MovePosition/MoveRotation on the physics step, which keeps the
    /// interpolation the cars already run (see CarController.TeleportTo for
    /// why a bare transform write is not an option on these bodies) and
    /// gives the audio sources a velocity to doppler with. The car's own
    /// controller, driver, watchdog, tank and thermostat are switched off
    /// for the duration; the components that READ the car — engine audio,
    /// tyre audio, wind, lights, smoke — keep running off fields this class
    /// writes from the recording, so the replay sounds like the race did.
    /// Everything is put back exactly when the replay ends, including the
    /// poses the cars were in when it began.
    ///
    /// A DELIVERY'S LOAD IS RECORDED TOO — the seat, every box, bottle,
    /// pizza and lid, on the same clock as the cars — and played back into
    /// the Pizza Cam. See <see cref="CargoTrack"/>.
    ///
    /// Created at runtime by RaceManager rather than baked into the scenes:
    /// a replay is a property of a race, not of a circuit, and adding it
    /// here is one line that reaches every venue without a rebake.
    /// </summary>
    public class RaceReplay : MonoBehaviour
    {
        public static RaceReplay Instance { get; private set; }
        /// <summary>A replay is on screen.</summary>
        public static bool Playing => Instance != null && Instance.playing;
        /// <summary>Frame on which a replay was last ended, so whatever
        /// button ended it is not ALSO read by the results screen in the
        /// same frame — RaceManager and this component both poll the same
        /// press and their Update order is undefined.</summary>
        public static int EndedFrame { get; private set; } = -1;

        /// <summary>Samples per second. Half the physics rate: a pose every
        /// other step is indistinguishable at 60 fps once interpolated, and
        /// half the memory.</summary>
        public const float SampleHz = 30f;
        /// <summary>Longest race recorded. Past this the recorder stops and
        /// the replay covers the first quarter hour, which is longer than
        /// any race in the catalog takes at a sane pace.</summary>
        public const float MaxSeconds = 15f * 60f;
        /// <summary>Seconds kept rolling after the player finishes, so the
        /// replay ends on the car crossing the line rather than on the
        /// frame it did.</summary>
        public const float TailSeconds = 4f;
        /// <summary>A recording has to be LONGER than this to be offered;
        /// a countdown-only stub is not a replay.</summary>
        public const float MinSeconds = 2f;
        public const float SkipSeconds = 10f;

        // ------------------------------------------------------------------
        //  What is recorded
        // ------------------------------------------------------------------
        public struct Frame
        {
            public Vector3 pos;
            public Quaternion rot;
            /// <summary>The body shell's lean, as CarBodyLean left it.</summary>
            public Quaternion lean;
            public float steerDeg;
            public float rpm, speedKmh, forwardSpeed, throttle, brake;
            /// <summary>Wheel roll, degrees, in CarController's own sign.</summary>
            public float roll0, roll1, roll2, roll3;
            /// <summary>The larger of the two axle slip angles, radians —
            /// what the tyre audio and the smoke key off.</summary>
            public float slip;
            /// <summary>Peak contact slide across the four wheels, m/s.</summary>
            public float slide;
            public sbyte gear;
            public byte lap, place;
            public byte flags;
        }
        public const byte FlagHandbrake = 1, FlagDrifting = 2, FlagGrounded = 4, FlagOnRoad = 8;

        class CarTrack
        {
            public CarController car;
            public CarBody body;
            public Transform bodyRoot;
            public List<Frame> frames = new List<Frame>(4096);
            // Restored on End.
            public Vector3 savedPos; public Quaternion savedRot;
            public bool savedKinematic;
            public RigidbodyInterpolation savedInterp;
            public Behaviour[] paused;
            public string name;
        }

        readonly List<CarTrack> tracks = new List<CarTrack>();

        /// <summary>
        /// THE LOAD ON THE PASSENGER SEAT, per sample.
        ///
        /// Reported as "the replay of a delivery does not track where items
        /// were in the seat at the time, but where they were at the end".
        /// Playback switched PizzaCargo off and recorded nothing of it, so the
        /// Pizza Cam spent the whole replay looking at the load as it lay at
        /// the flag. Now every part of the island (PizzaCargo.ReplayParts) is
        /// sampled on the cars' step, in world space, and written back on the
        /// replay clock with every body on the island kinematic — the same
        /// shape as the cars, with transforms rather than MovePosition because
        /// half the parts (a shut box's pizza and lid) are not bodies at all.
        ///
        /// Its own FIRST sample index, because a load is not guaranteed to
        /// exist on the recorder's first step; before it, the first frame
        /// holds. The condition rides along so the Pizza Cam's caption goes
        /// amber at the moment in the replay it went amber in the race.
        /// </summary>
        class CargoTrack
        {
            public PizzaCargo cargo;
            public Transform[] parts;
            /// <summary>Index into <c>times</c> of this track's first sample.</summary>
            public int first;
            /// <summary>parts.Length entries per sample, parts in order.</summary>
            public readonly List<Vector3> pos = new List<Vector3>(8192);
            public readonly List<Quaternion> rot = new List<Quaternion>(8192);
            public readonly List<float> condition = new List<float>(1024);
            public int Samples => condition.Count;
            // Restored on End.
            public Vector3[] savedPos; public Quaternion[] savedRot;
            public Rigidbody[] bodies;
            public bool[] savedKinematic;
            public RigidbodyInterpolation[] savedInterp;
            public CollisionDetectionMode[] savedCcd;
            public Vector3[] savedVel, savedSpin;
        }
        CargoTrack cargoTrack;
        /// <summary>Race clock per sample, seconds since the recording
        /// began. Frames are evenly spaced so this is index / SampleHz, but
        /// stored anyway: a dropped fixed step must not silently stretch the
        /// replay.</summary>
        readonly List<float> times = new List<float>(4096);

        bool recording;
        bool playing;
        bool paused;
        float recordT0 = -1f;
        int stepCounter;
        float tailLeft = -1f;

        // ------------------------------------------------------------------
        //  Playback state
        // ------------------------------------------------------------------
        /// <summary>Replay time, seconds from the first sample.</summary>
        public float Time01 => Duration > 0f ? Mathf.Clamp01(replayTime / Duration) : 0f;
        public float ReplayTime => replayTime;
        public float Duration => times.Count > 1 ? times[times.Count - 1] : 0f;
        public bool Paused => paused;
        /// <summary>The car the cameras follow and the HUD reads.</summary>
        public CarController Focus => focus < tracks.Count ? tracks[focus].car : null;
        public string FocusName => focus < tracks.Count ? tracks[focus].name : "";
        public int FocusIndex => focus;
        public int CarCount => tracks.Count;
        /// <summary>Frame data of the focus car at the replay time, for the HUD.</summary>
        public Frame FocusFrame => focus < tracks.Count ? Sample(tracks[focus], replayTime) : default;

        float replayTime;
        int focus;
        ReplayCamera replayCam;
        ChaseCamera chase;
        Camera cam;
        GameObject cluster;
        SpeedBlur speedBlur;
        bool clusterWasActive;
        ChaseCamera.View savedView;

        /// <summary>Camera choices on the replay: the trackside director,
        /// then the car's own views. Cycled with the camera control.</summary>
        public enum CamMode { Trackside = 0, Chase = 1, Hood = 2, Bumper = 3, Cockpit = 4 }
        public static readonly string[] CamNames = { "TRACKSIDE", "CHASE", "HOOD CAM", "BUMPER CAM", "COCKPIT" };
        public CamMode Mode { get; private set; }
        /// <summary>When the camera mode last changed, for the HUD flash.</summary>
        public float ModeChangedAt { get; private set; } = -99f;

        /// <summary>A recording long enough to watch exists and no replay is running.</summary>
        public bool Available => !playing && times.Count > 1 && Duration > MinSeconds;

        void Awake() => Instance = this;
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (playing) End();
        }

        /// <summary>Start recording the field. Called by RaceManager once the
        /// grid is settled; safe to call once only.</summary>
        public void BeginRecording(List<CarController> cars)
        {
            if (recording || cars == null) return;
            tracks.Clear(); times.Clear();
            foreach (var c in cars)
            {
                if (c == null) continue;
                var body = c.GetComponent<CarBody>();
                tracks.Add(new CarTrack
                {
                    car = c, body = body,
                    bodyRoot = body != null ? body.bodyRoot : null,
                    name = NameOf(c),
                });
            }
            recording = tracks.Count > 0;
            recordT0 = -1f;
            stepCounter = 0;
            tailLeft = -1f;
            cargoTrack = null;
        }

        /// <summary>The car's catalog name, or the built-in car's.</summary>
        static string NameOf(CarController c)
        {
            var spec = c.activeSpec;
            if (spec != null && !string.IsNullOrEmpty(spec.name)) return spec.name.ToUpperInvariant();
            var body = c.GetComponent<CarBody>();
            var def = body != null ? body.Def : null;
            if (def != null && !string.IsNullOrEmpty(def.displayName)) return def.displayName.ToUpperInvariant();
            return "RX-7";
        }

        void FixedUpdate()
        {
            if (playing) { StepPlayback(); return; }
            if (!recording) return;

            // Every other physics step. Counted, not timed: a step is a step.
            stepCounter++;
            if (stepCounter % Mathf.Max(1, Mathf.RoundToInt(1f / (SampleHz * Time.fixedDeltaTime))) != 0) return;

            float now = Time.fixedTime;
            if (recordT0 < 0f) recordT0 = now;
            float t = now - recordT0;
            if (t > MaxSeconds) { recording = false; return; }

            var rm = RaceManager.Instance;
            // Roll a few seconds past the flag, then stop: the replay should
            // end on the line, and the field parking behind it is nobody's
            // highlight.
            if (rm != null && rm.State == RaceManager.RaceState.Finished)
            {
                if (tailLeft < 0f) tailLeft = TailSeconds;
                tailLeft -= 1f / SampleHz;
                if (tailLeft <= 0f) { recording = false; return; }
            }

            times.Add(t);
            foreach (var tr in tracks)
                tr.frames.Add(Capture(tr, rm));
            CaptureCargo();
        }

        /// <summary>One sample of the load, taken on the same step as the
        /// cars'. Attaches to the load the first time one exists; a load that
        /// has since been destroyed simply stops adding samples and the replay
        /// holds its last one.</summary>
        void CaptureCargo()
        {
            var cargo = PizzaCargo.Instance;
            if (cargoTrack == null)
            {
                if (cargo == null || cargo.BoxCount == 0) return;
                cargoTrack = new CargoTrack
                {
                    cargo = cargo,
                    parts = cargo.ReplayParts().ToArray(),
                    first = times.Count - 1,
                };
            }
            var ct = cargoTrack;
            if (ct.cargo == null || ct.cargo != cargo) return;
            foreach (var p in ct.parts)
            {
                ct.pos.Add(p != null ? p.position : Vector3.zero);
                ct.rot.Add(p != null ? p.rotation : Quaternion.identity);
            }
            ct.condition.Add(cargo.Condition);
        }

        static Frame Capture(CarTrack tr, RaceManager rm)
        {
            var c = tr.car;
            var f = new Frame
            {
                pos = c.transform.position,
                rot = c.transform.rotation,
                lean = tr.bodyRoot != null ? tr.bodyRoot.localRotation : Quaternion.identity,
                rpm = c.currentRPM,
                speedKmh = c.speedKmh,
                forwardSpeed = c.forwardSpeed,
                throttle = c.throttleInput,
                brake = c.brakeInput,
                gear = (sbyte)Mathf.Clamp(c.currentGear, -1, 8),
                slip = Mathf.Max(Mathf.Abs(c.rearSlipAngle), Mathf.Abs(c.frontSlipAngle)),
            };
            // The front hubs carry the steer angle as a yaw; either one
            // reads the same number (toe aside) so take the left.
            if (c.wheelHubs != null && c.wheelHubs.Length > 0 && c.wheelHubs[0] != null)
                f.steerDeg = Mathf.DeltaAngle(0f, c.wheelHubs[0].localEulerAngles.y);
            if (c.wheelMeshes != null && c.wheelMeshes.Length >= 4)
            {
                f.roll0 = RollOf(c.wheelMeshes[0]); f.roll1 = RollOf(c.wheelMeshes[1]);
                f.roll2 = RollOf(c.wheelMeshes[2]); f.roll3 = RollOf(c.wheelMeshes[3]);
            }
            float slide = 0f; bool grounded = false, onRoad = false;
            if (c.wheelContacts != null)
                for (int i = 0; i < c.wheelContacts.Length; i++)
                {
                    var wc = c.wheelContacts[i];
                    if (wc.grounded) { grounded = true; onRoad |= wc.onRoad; }
                    if (wc.slide > slide) slide = wc.slide;
                }
            f.slide = slide;
            if (c.handbrakeInput) f.flags |= FlagHandbrake;
            if (c.Drifting) f.flags |= FlagDrifting;
            if (grounded) f.flags |= FlagGrounded;
            if (onRoad) f.flags |= FlagOnRoad;
            if (rm != null)
            {
                var p = rm.GetProgress(c);
                f.lap = (byte)Mathf.Clamp(p != null ? Mathf.Min(p.lap, rm.totalLaps) : 1, 0, 255);
                f.place = (byte)Mathf.Clamp(rm.GetPosition(c), 0, 255);
            }
            return f;
        }

        static float RollOf(Transform mesh) =>
            mesh != null ? Mathf.DeltaAngle(0f, mesh.localEulerAngles.x) : 0f;

        // ------------------------------------------------------------------
        //  Playback
        // ------------------------------------------------------------------
        /// <summary>Start the replay from the grid. No-op without a recording.</summary>
        public void Begin()
        {
            if (playing || !Available) return;
            recording = false;
            playing = true;
            paused = false;
            replayTime = 0f;
            focus = 0;
            var rm = RaceManager.Instance;
            if (rm != null && rm.playerCar != null)
                for (int i = 0; i < tracks.Count; i++)
                    if (tracks[i].car == rm.playerCar) { focus = i; break; }

            foreach (var tr in tracks)
            {
                var c = tr.car;
                if (c == null) continue;
                tr.savedPos = c.transform.position; tr.savedRot = c.transform.rotation;
                // Everything that DRIVES or JUDGES the car goes to sleep; what
                // merely reads it stays awake and reads the recording.
                tr.paused = PauseList(c);
                foreach (var b in tr.paused) if (b != null) b.enabled = false;
                if (c.Body != null)
                {
                    tr.savedKinematic = c.Body.isKinematic;
                    tr.savedInterp = c.Body.interpolation;
                    c.Body.linearVelocity = Vector3.zero;
                    c.Body.angularVelocity = Vector3.zero;
                    c.Body.isKinematic = true;
                    c.Body.interpolation = RigidbodyInterpolation.Interpolate;
                }
                c.throttleInput = 0f; c.brakeInput = 0f; c.handbrakeInput = false;
            }
            BeginCargo();

            // The lens. The chase rig stands down; the director takes the
            // same camera and the same AudioListener.
            cam = Camera.main;
            if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
            if (cam != null)
            {
                chase = cam.GetComponent<ChaseCamera>();
                replayCam = cam.GetComponent<ReplayCamera>();
                if (replayCam == null) replayCam = cam.gameObject.AddComponent<ReplayCamera>();
                var venue = RaceHUD.VenueDef();
                var path = rm != null ? rm.path : Object.FindFirstObjectByType<TrackPath>();
                float spacing = path != null ? path.spacing : 4f;
                replayCam.Setup(path, cam, path != null ? path.roadWidth : 12f,
                                idx => TrackCatalog.InTunnel(venue, idx * spacing));
            }
            savedView = ChaseCamera.Current;
            SetMode(CamMode.Trackside, flash: false);

            // The cabin is the driver's; a spectator gets the race data only.
            var hud = Object.FindFirstObjectByType<RaceHUD>();
            if (hud != null && hud.cluster != null)
            {
                cluster = hud.cluster.gameObject;
                clusterWasActive = cluster.activeSelf;
                cluster.SetActive(false);
            }
            // The blur is the DRIVER's speed; a trackside lens is standing still.
            speedBlur = Object.FindFirstObjectByType<SpeedBlur>();
            if (speedBlur != null) speedBlur.suspended = true;
            TouchControls.Instance?.SetReplayMode(true);

            Seek(0f, hard: true);
        }

        static Behaviour[] PauseList(CarController c)
        {
            var list = new List<Behaviour>();
            void Add<T>() where T : Behaviour { var b = c.GetComponent<T>(); if (b != null && b.enabled) list.Add(b); }
            Add<CarController>(); Add<AIDriver>(); Add<PlayerCarInput>(); Add<StuckRecovery>();
            Add<CollisionResponder>(); Add<FuelTank>(); Add<EngineTemp>(); Add<CarBodyLean>();
            return list.ToArray();
        }

        /// <summary>Stop the replay and put the race back the way it was.</summary>
        public void End()
        {
            if (!playing) return;
            playing = false;
            EndedFrame = Time.frameCount;
            foreach (var tr in tracks)
            {
                var c = tr.car;
                if (c == null) continue;
                if (c.Body != null)
                {
                    c.Body.isKinematic = tr.savedKinematic;
                    c.Body.interpolation = tr.savedInterp;
                }
                // Through the teleport: the body is interpolated, and a bare
                // transform write is painted over on the next step.
                c.TeleportTo(tr.savedPos, tr.savedRot);
                if (tr.paused != null) foreach (var b in tr.paused) if (b != null) b.enabled = true;
                c.throttleInput = 0f; c.brakeInput = 0f; c.handbrakeInput = false;
            }
            EndCargo();

            if (replayCam != null) replayCam.enabled = false;
            if (chase != null)
            {
                chase.enabled = true;
                var rm = RaceManager.Instance;
                if (rm != null && rm.playerCar != null) { chase.target = rm.playerCar.transform; chase.targetCar = rm.playerCar; }
                ChaseCamera.PreviewView(savedView);
            }
            if (cluster != null) cluster.SetActive(clusterWasActive);
            if (speedBlur != null) speedBlur.suspended = false;
            TouchControls.Instance?.SetReplayMode(false);
        }

        void Update()
        {
            var rm = RaceManager.Instance;
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            var touch = TouchControls.Instance;

            if (!playing)
            {
                // Offered on the results screen only.
                if (rm == null || rm.State != RaceManager.RaceState.Finished || !Available) return;
                if (PauseMenu.IsOpen) return;
                bool want = (kb != null && kb.vKey.wasPressedThisFrame) ||
                            (pad != null && pad.buttonWest.wasPressedThisFrame) ||
                            (touch != null && touch.ActionPressed);
                if (want) Begin();
                return;
            }

            // ---- replay controls ----
            bool exit = (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.rKey.wasPressedThisFrame)) ||
                        (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)) ||
                        (touch != null && touch.ContinuePressed);
            if (exit) { End(); return; }

            bool playPause = (kb != null && kb.spaceKey.wasPressedThisFrame) ||
                             (pad != null && pad.buttonSouth.wasPressedThisFrame) ||
                             (touch != null && touch.ReplayPressed(TouchControls.ReplayKey.PlayPause));
            if (playPause) paused = !paused;

            bool back = (kb != null && kb.leftArrowKey.wasPressedThisFrame) ||
                        (pad != null && (pad.dpad.left.wasPressedThisFrame || pad.leftShoulder.wasPressedThisFrame)) ||
                        (touch != null && touch.ReplayPressed(TouchControls.ReplayKey.Back));
            bool fwd = (kb != null && kb.rightArrowKey.wasPressedThisFrame) ||
                       (pad != null && (pad.dpad.right.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame)) ||
                       (touch != null && touch.ReplayPressed(TouchControls.ReplayKey.Forward));
            if (back) Seek(replayTime - SkipSeconds, hard: true);
            if (fwd) Seek(replayTime + SkipSeconds, hard: true);

            bool nextCar = (kb != null && kb.upArrowKey.wasPressedThisFrame) ||
                           (pad != null && pad.dpad.up.wasPressedThisFrame) ||
                           (touch != null && touch.ReplayPressed(TouchControls.ReplayKey.Car));
            bool prevCar = (kb != null && kb.downArrowKey.wasPressedThisFrame) ||
                           (pad != null && pad.dpad.down.wasPressedThisFrame);
            if (nextCar) SetFocus(focus + 1);
            if (prevCar) SetFocus(focus - 1);

            bool camKey = (kb != null && kb.cKey.wasPressedThisFrame) ||
                          (pad != null && pad.buttonNorth.wasPressedThisFrame) ||
                          (touch != null && touch.ReplayPressed(TouchControls.ReplayKey.Camera));
            if (camKey) SetMode((CamMode)(((int)Mode + 1) % CamNames.Length), flash: true);

            // The readers run in Update too, in no defined order against this
            // one, so the fields are written here as well as on the step.
            ApplyVisuals();
        }

        void SetFocus(int i)
        {
            if (tracks.Count == 0) return;
            focus = ((i % tracks.Count) + tracks.Count) % tracks.Count;
            if (replayCam != null) replayCam.Retarget();
            if (chase != null && Mode != CamMode.Trackside)
            {
                chase.target = Focus.transform; chase.targetCar = Focus;
            }
        }

        void SetMode(CamMode m, bool flash)
        {
            Mode = m;
            if (flash) ModeChangedAt = Time.unscaledTime;
            bool trackside = m == CamMode.Trackside;
            if (replayCam != null) replayCam.enabled = trackside;
            if (chase != null)
            {
                chase.enabled = !trackside;
                if (!trackside && Focus != null)
                {
                    chase.target = Focus.transform; chase.targetCar = Focus;
                    // The chase rig is told its view without the preference
                    // being saved: a replay camera is not a driving choice.
                    ChaseCamera.PreviewView(
                        m == CamMode.Chase ? ChaseCamera.View.Chase :
                        m == CamMode.Hood ? ChaseCamera.View.Hood :
                        m == CamMode.Bumper ? ChaseCamera.View.Bumper : ChaseCamera.View.Cockpit);
                }
                else if (trackside) ChaseCamera.PreviewView(ChaseCamera.View.Chase);
            }
        }

        /// <summary>Jump the clock. A hard seek drops the interpolation
        /// history so the cars do not smear across the track.</summary>
        public void Seek(float t, bool hard)
        {
            replayTime = Mathf.Clamp(t, 0f, Mathf.Max(0f, Duration));
            foreach (var tr in tracks)
            {
                if (tr.car == null || tr.frames.Count == 0) continue;
                var f = Sample(tr, replayTime);
                if (hard) tr.car.TeleportTo(f.pos, f.rot);
                else if (tr.car.Body != null) { tr.car.Body.MovePosition(f.pos); tr.car.Body.MoveRotation(f.rot); }
            }
            if (replayCam != null) replayCam.Retarget();
            ApplyVisuals();
            ApplyCargo(replayTime);
        }

        void StepPlayback()
        {
            if (!paused)
            {
                replayTime += Time.fixedDeltaTime;
                // Round again from the grid, the way the reference loops.
                if (replayTime > Duration) { Seek(0f, hard: true); return; }
            }
            foreach (var tr in tracks)
            {
                if (tr.car == null || tr.car.Body == null || tr.frames.Count == 0) continue;
                var f = Sample(tr, replayTime);
                tr.car.Body.MovePosition(f.pos);
                tr.car.Body.MoveRotation(f.rot);
            }
            ApplyVisuals();
        }

        /// <summary>Write the recorded state into the things that read the
        /// car: hubs, wheel meshes, body lean, and the controller fields the
        /// audio, lights and smoke look at.</summary>
        void ApplyVisuals()
        {
            foreach (var tr in tracks)
            {
                var c = tr.car;
                if (c == null || tr.frames.Count == 0) continue;
                var f = Sample(tr, replayTime);
                c.currentRPM = f.rpm;
                c.currentGear = f.gear;
                c.speedKmh = f.speedKmh;
                c.forwardSpeed = f.forwardSpeed;
                c.throttleInput = f.throttle;
                c.brakeInput = f.brake;
                c.handbrakeInput = (f.flags & FlagHandbrake) != 0;
                c.rearSlipAngle = f.slip;
                c.frontSlipAngle = 0f;
                c.anyWheelGrounded = (f.flags & FlagGrounded) != 0;
                c.onRoad = (f.flags & FlagOnRoad) != 0;
                if (tr.bodyRoot != null) tr.bodyRoot.localRotation = f.lean;
                if (c.wheelHubs != null)
                    for (int i = 0; i < Mathf.Min(2, c.wheelHubs.Length); i++)
                        if (c.wheelHubs[i] != null)
                            c.wheelHubs[i].localRotation = Quaternion.Euler(0f, f.steerDeg, 0f);
                if (c.wheelMeshes != null && c.wheelMeshes.Length >= 4)
                {
                    SetRoll(c.wheelMeshes[0], f.roll0); SetRoll(c.wheelMeshes[1], f.roll1);
                    SetRoll(c.wheelMeshes[2], f.roll2); SetRoll(c.wheelMeshes[3], f.roll3);
                }
                // The contacts feed the smoke and the marks: a sliding rear
                // axle at the recorded slide, seated at the wheel.
                // WheelContact is a struct: read, edit, WRITE BACK.
                if (c.wheelContacts != null && c.wheelHubs != null)
                    for (int i = 0; i < Mathf.Min(4, c.wheelContacts.Length, c.wheelHubs.Length); i++)
                    {
                        var wc = c.wheelContacts[i];
                        wc.grounded = (f.flags & FlagGrounded) != 0;
                        wc.onRoad = (f.flags & FlagOnRoad) != 0;
                        wc.slide = i >= 2 ? f.slide : f.slide * 0.3f;
                        wc.load = c.massKg * 9.81f * 0.25f;
                        wc.normal = Vector3.up;
                        wc.forward = c.transform.forward;
                        if (c.wheelHubs[i] != null)
                            wc.point = c.wheelHubs[i].position - Vector3.up * c.wheelRadius;
                        c.wheelContacts[i] = wc;
                    }
            }
        }

        static void SetRoll(Transform mesh, float deg)
        {
            if (mesh != null) mesh.localRotation = Quaternion.Euler(deg, 0f, 0f);
        }

        // ------------------------------------------------------------------
        //  The load
        // ------------------------------------------------------------------
        /// <summary>
        /// Freeze the island for playback. The component that drives it goes
        /// to sleep, as it always did; what is new is that every body on the
        /// island goes KINEMATIC, because a dynamic box the replay writes a
        /// pose into is a box gravity and the solver immediately take back.
        /// Interpolation off so a transform write is what gets drawn, and
        /// speculative contacts first because PhysX refuses swept CCD on a
        /// kinematic body and says so on every step.
        /// </summary>
        void BeginCargo()
        {
            var live = PizzaCargo.Instance;
            if (live != null) live.enabled = false;
            var ct = cargoTrack;
            if (ct == null || ct.cargo == null || ct.Samples == 0) return;

            int n = ct.parts.Length;
            ct.savedPos = new Vector3[n];
            ct.savedRot = new Quaternion[n];
            for (int j = 0; j < n; j++)
                if (ct.parts[j] != null)
                { ct.savedPos[j] = ct.parts[j].position; ct.savedRot[j] = ct.parts[j].rotation; }

            // Asked for NOW, not at attach: opening a box adds the bodies of
            // its pizza and lid, and those have to stand still too.
            ct.bodies = ct.cargo.GetComponentsInChildren<Rigidbody>(true);
            int m = ct.bodies.Length;
            ct.savedKinematic = new bool[m];
            ct.savedInterp = new RigidbodyInterpolation[m];
            ct.savedCcd = new CollisionDetectionMode[m];
            ct.savedVel = new Vector3[m];
            ct.savedSpin = new Vector3[m];
            for (int i = 0; i < m; i++)
            {
                var b = ct.bodies[i];
                if (b == null) continue;
                ct.savedKinematic[i] = b.isKinematic;
                ct.savedInterp[i] = b.interpolation;
                ct.savedCcd[i] = b.collisionDetectionMode;
                if (!b.isKinematic) { ct.savedVel[i] = b.linearVelocity; ct.savedSpin[i] = b.angularVelocity; }
                b.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                b.isKinematic = true;
                b.interpolation = RigidbodyInterpolation.None;
            }
        }

        /// <summary>Put the load back exactly as the replay found it — poses,
        /// body flags and the velocities it had — and wake its driver with
        /// its motion memory wiped, because the car it rides in has just been
        /// teleported back from wherever the replay left it.</summary>
        void EndCargo()
        {
            var ct = cargoTrack;
            if (ct != null && ct.cargo != null && ct.bodies != null && ct.savedPos != null)
            {
                // Poses FIRST, while every body is still kinematic and nothing
                // can argue with the write.
                for (int j = 0; j < ct.parts.Length; j++)
                    if (ct.parts[j] != null)
                        ct.parts[j].SetPositionAndRotation(ct.savedPos[j], ct.savedRot[j]);
                for (int i = 0; i < ct.bodies.Length; i++)
                {
                    var b = ct.bodies[i];
                    if (b == null) continue;
                    b.position = b.transform.position;
                    b.rotation = b.transform.rotation;
                    b.isKinematic = ct.savedKinematic[i];
                    b.collisionDetectionMode = ct.savedCcd[i];
                    b.interpolation = ct.savedInterp[i];
                    if (!b.isKinematic) { b.linearVelocity = ct.savedVel[i]; b.angularVelocity = ct.savedSpin[i]; }
                }
                ct.bodies = null;
                ct.savedPos = null;
            }
            var live = PizzaCargo.Instance;
            if (live != null)
            {
                live.ForgetMotion();
                live.enabled = true;
            }
        }

        /// <summary>The clock the cars are DRAWN at. They are moved on the
        /// physics step and interpolated, which shows the pose one step behind
        /// the step's own and slides toward it over the frames between — so
        /// the load, which is written directly, samples the same moment or it
        /// leads the car by a step.</summary>
        float DrawnReplayTime()
        {
            if (paused) return replayTime;
            float dt = Time.fixedDeltaTime;
            float into = Mathf.Clamp(Time.time - Time.fixedTime, 0f, dt);
            return Mathf.Clamp(replayTime - dt + into, 0f, Mathf.Max(0f, Duration));
        }

        void LateUpdate()
        {
            if (playing) ApplyCargo(DrawnReplayTime());
        }

        /// <summary>Write the load's recorded pose at replay time
        /// <paramref name="t"/>, parents before children.</summary>
        void ApplyCargo(float t)
        {
            var ct = cargoTrack;
            if (ct == null || ct.cargo == null || ct.bodies == null) return;
            if (!CargoSampleAt(ct, t, out int k0, out int k1, out float u)) return;
            int n = ct.parts.Length;
            for (int j = 0; j < n; j++)
            {
                var p = ct.parts[j];
                if (p == null) continue;
                int a = k0 * n + j, b = k1 * n + j;
                p.SetPositionAndRotation(Vector3.Lerp(ct.pos[a], ct.pos[b], u),
                                         Quaternion.Slerp(ct.rot[a], ct.rot[b], u));
            }
        }

        /// <summary>The two cargo samples either side of replay time t, as
        /// indices into the track's own lists, and how far between them.</summary>
        bool CargoSampleAt(CargoTrack ct, float t, out int k0, out int k1, out float u)
        {
            k0 = k1 = 0; u = 0f;
            int samples = ct.Samples;
            if (samples == 0 || ct.parts.Length == 0 || times.Count == 0) return false;
            int i;
            if (times.Count == 1 || t <= times[0]) { i = 0; u = 0f; }
            else if (t >= times[times.Count - 1]) { i = times.Count - 1; u = 0f; }
            else
            {
                i = IndexAt(t);
                float t0 = times[i], t1 = times[i + 1];
                u = t1 > t0 ? Mathf.Clamp01((t - t0) / (t1 - t0)) : 0f;
            }
            k0 = Mathf.Clamp(i - ct.first, 0, samples - 1);
            k1 = Mathf.Clamp(i + 1 - ct.first, 0, samples - 1);
            if (k0 == k1) u = 0f;
            return true;
        }

        /// <summary>The load's condition at the replay clock, when a replay
        /// with a recorded load is on screen — what the Pizza Cam's caption
        /// reads instead of the load's condition at the flag.</summary>
        public bool TryCargoCondition(out float condition)
        {
            condition = 1f;
            var ct = cargoTrack;
            if (!playing || ct == null || !CargoSampleAt(ct, replayTime, out int k0, out int k1, out float u))
                return false;
            condition = Mathf.Lerp(ct.condition[k0], ct.condition[k1], u);
            return true;
        }

        /// <summary>The car's state at a replay time, interpolated between
        /// the two samples either side of it.</summary>
        Frame Sample(CarTrack tr, float t)
        {
            int n = tr.frames.Count;
            if (n == 0) return default;
            if (n == 1 || t <= times[0]) return tr.frames[0];
            if (t >= times[n - 1]) return tr.frames[n - 1];
            int i = IndexAt(t);
            float t0 = times[i], t1 = times[i + 1];
            float u = t1 > t0 ? Mathf.Clamp01((t - t0) / (t1 - t0)) : 0f;
            return Lerp(tr.frames[i], tr.frames[i + 1], u);
        }

        /// <summary>Largest sample index whose time is not past t. Evenly
        /// spaced samples make the guess exact almost always; the walk
        /// covers a dropped step either side.</summary>
        int IndexAt(float t)
        {
            int n = times.Count;
            int i = Mathf.Clamp(Mathf.FloorToInt(t * SampleHz), 0, n - 2);
            while (i > 0 && times[i] > t) i--;
            while (i < n - 2 && times[i + 1] <= t) i++;
            return i;
        }

        public static Frame Lerp(Frame a, Frame b, float u)
        {
            return new Frame
            {
                pos = Vector3.Lerp(a.pos, b.pos, u),
                rot = Quaternion.Slerp(a.rot, b.rot, u),
                lean = Quaternion.Slerp(a.lean, b.lean, u),
                steerDeg = Mathf.Lerp(a.steerDeg, b.steerDeg, u),
                rpm = Mathf.Lerp(a.rpm, b.rpm, u),
                speedKmh = Mathf.Lerp(a.speedKmh, b.speedKmh, u),
                forwardSpeed = Mathf.Lerp(a.forwardSpeed, b.forwardSpeed, u),
                throttle = Mathf.Lerp(a.throttle, b.throttle, u),
                brake = Mathf.Lerp(a.brake, b.brake, u),
                roll0 = Mathf.LerpAngle(a.roll0, b.roll0, u), roll1 = Mathf.LerpAngle(a.roll1, b.roll1, u),
                roll2 = Mathf.LerpAngle(a.roll2, b.roll2, u), roll3 = Mathf.LerpAngle(a.roll3, b.roll3, u),
                slip = Mathf.Lerp(a.slip, b.slip, u),
                slide = Mathf.Lerp(a.slide, b.slide, u),
                gear = u < 0.5f ? a.gear : b.gear,
                lap = u < 0.5f ? a.lap : b.lap,
                place = u < 0.5f ? a.place : b.place,
                flags = u < 0.5f ? a.flags : b.flags,
            };
        }

        // ------------------------------------------------------------------
        //  For the self-test: the pure parts, without a scene.
        // ------------------------------------------------------------------
        /// <summary>Feed synthetic frames straight in. Test only.</summary>
        public void InjectForTest(CarController car, List<float> t, List<Frame> frames)
        {
            tracks.Clear(); times.Clear();
            times.AddRange(t);
            tracks.Add(new CarTrack { car = car, frames = frames, name = "TEST" });
            recording = false;
            cargoTrack = null;
        }
        public Frame SampleForTest(float t) => tracks.Count > 0 ? Sample(tracks[0], t) : default;
        public int FrameCount => times.Count;
        /// <summary>For the play-mode check: the Time.fixedTime of the first
        /// sample, so a trace taken on the same steps can be lined up with
        /// the replay clock; and the clock of sample k.</summary>
        public float RecordStartFixedTime => recordT0;
        public float SampleClock(int k) => times[Mathf.Clamp(k, 0, times.Count - 1)];
        /// <summary>How many samples of a load were recorded (0 = none), and
        /// the recorder sample index the first of them was taken on.</summary>
        public int CargoSamples => cargoTrack != null ? cargoTrack.Samples : 0;
        public int CargoFirstSample => cargoTrack != null ? cargoTrack.first : -1;
    }
}
