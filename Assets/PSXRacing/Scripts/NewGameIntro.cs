using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PSXRacing.LifeSim;

namespace PSXRacing
{
    /// <summary>
    /// THE NEW GAME'S OPENING (owner, 2026-11-16): "When starting a new game, the screen should be black (like
    /// you're in a long tunnel), after you select your car, you hear it drive up from behind, then through the
    /// camera, and the camera attaches and player takes over, and drives out of the tunnel to reveal the
    /// Appalachian mountain track level (like the screenshot reference) as the player is driving to Charlotte to
    /// start their career/life there."
    ///
    /// 1. NEW GAME's wizard runs over black with the tunnel's sound (LifeHomeScreen.Start: <see cref="TunnelAmbience"/>).
    /// 2. DRIVE THIS ONE hands the chosen car to <see cref="Launch"/> instead of reloading the home screen: the
    ///    Little Switzerland Tunnel on the Blue Ridge Parkway, driven toward Gillespie Gap (the reverse twin: the
    ///    bake runs west from the gap, so the way to NC 226, Marion, I-40 and Charlotte is the loop backwards), on
    ///    the career's own day, a daytime hour, a dry road, no traffic, no rivals. The drive is a TEST DRIVE to
    ///    the machinery - nothing is banked: no kilometres, fuel, wear, damage file, injury or tow.
    /// 3. RaceManager.Start hands the scene to <see cref="Attach"/>: the lens stands in the lane at a driver's eye
    ///    deep in the bore, facing the exit; the car rolls up from 125 m behind on its own engine (3D, doppler,
    ///    the bore's reverb - EngineAudio hears it from outside while <see cref="EarOutside"/>), its real
    ///    headlights on, the black lifting as the beams reach the walls ahead; it passes through the lens, the lens
    ///    eases onto it into the chase camera (<see cref="BlendS"/>), the controls pass with a short lane-keeping
    ///    hand that lets go the moment the player touches anything, and the HUD fades in.
    /// 4. The exit: the eye, held shut until the attach (ExposureAdapt.Scripted), has opened in the bore and closes
    ///    on the daylight (ExposureAdapt.ForceTunnel: the reverse twin's metres do not match the catalogue's
    ///    tunnel table); a caption, BLUE RIDGE PARKWAY and the date.
    /// 5. <see cref="DriveM"/> past the exit (or a time-out, a stop, a car that cannot go on, or SKIP held for a
    ///    second at any point: Esc, Start, or the corner button) it fades to black, CHARLOTTE, and the home screen
    ///    loads exactly as DRIVE THIS ONE loaded it before (the same save: nothing here writes one).
    /// PSX_INTRO=0: the flow as it was.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class NewGameIntro : MonoBehaviour
    {
        // =================== switches ===================

        /// <summary>The intro is on unless PSX_INTRO=0 (PSX_INTRO=1 forces it on whatever the default).</summary>
        public const bool DefaultOn = true;
        public static bool On
        {
            get
            {
                string e = System.Environment.GetEnvironmentVariable("PSX_INTRO");
                return e == "0" ? false : e == "1" ? true : DefaultOn;
            }
        }

        /// <summary>The venue: the Little Switzerland loop driven toward Gillespie Gap.</summary>
        public const string VenueId = "LittleSwitzerlandRev";

        /// <summary>The intro's hour (TimeOfDay index): daytime. The career's own clock is untouched.</summary>
        public static int Hour => EnvInt("PSX_INTRO_HOUR", TimeOfDay.Morning, 0, 6);
        /// <summary>The sky over the dry road (SkyKind): the photo's grey, lightly overcast.</summary>
        public static int Sky => EnvInt("PSX_INTRO_SKY", (int)SkyKind.Overcast, 0, 2);

        // =================== the numbers ===================

        public const float ApproachKmh = 58f;       // the car's speed up the bore
        public const float CarBackM = 125f;         // where it starts, behind the lens
        public const float CamIntoBore = 0.42f;     // the lens, this far through the bore
        public const float EyeH = 1.15f;            // a driver's eye over the road
        public const float IntroFov = 56f;          // the standing lens (vertical)
        public const float RevealFromM = 60f, RevealToM = 10f;   // the black lifts as the car comes from this far behind to this far
        public const float AttachAtM = 5.5f;        // the car's centre this far past the lens: the lens starts to follow
        public const float BlendS = 1.8f;           // the ease onto the chase camera
        public const float ControlAtW = 0.4f;       // the controls pass this far into the ease
        public const float AssistS = 1.8f;          // the lane-keeping hand lets go over this long
        public const float HudFadeS = 1.2f;
        public const float DriveM = 2400f;          // past the exit, then CHARLOTTE
        public const float DriveMaxS = 180f, StoppedMaxS = 20f, StrandedMaxS = 3f;
        public const float SkipHoldS = 1f;
        public const float OutroFadeS = 1.6f, CardInS = 0.6f, CardHoldS = 2.6f;
        public const float CaptionAfterExitS = 1.0f, CaptionS = 5.5f;

        // =================== state other code reads ===================

        /// <summary>Launched: the stage scene's RaceManager hands over.</summary>
        public static bool Pending { get; private set; }
        public static NewGameIntro Instance { get; private set; }
        /// <summary>EngineAudio.EarFor: the player's car is heard as a car out in the world.</summary>
        public static bool EarOutside { get; private set; }
        /// <summary>Tools: the autopilot keeps the wheel after the attach (the play check's driver).</summary>
        public static bool AutoDrive;

        public enum Phase { None, Approach, Attach, Drive, Outro }
        public Phase Now { get; private set; }
        /// <summary>The car's centre past the lens's plane, metres (negative behind).</summary>
        public float Along { get; private set; }
        /// <summary>The ease onto the chase camera, 0..1.</summary>
        public float Blend { get; private set; }
        public bool Control { get; private set; }
        public bool Exited { get; private set; }
        public float ExitedAt { get; private set; } = -1f;
        /// <summary>Metres driven past the exit (along the road).</summary>
        public float PastExitM { get; private set; }
        public float BlackAlpha => carrier != null ? carrier.BlackAlpha : 0f;
        public bool CardUp => carrier != null && carrier.CardAlpha > 0.99f;
        public string EndReason { get; private set; }
        /// <summary>The car, the lens and the bore, for the tools.</summary>
        public CarController Car => car;
        public Camera Lens => cam;
        public int BoreIn => iIn;
        public int BoreOut => iOut;
        public int CarIndex => carIdx;

        // =================== launch (the home screen) ===================

        /// <summary>
        /// DRIVE THIS ONE, with the intro on: the run is set up and the scene loads behind black. False (nothing
        /// changed) when the intro is off or cannot run here; the caller reloads the home screen as before.
        /// </summary>
        public static bool Launch(LifeState s)
        {
            if (!On || s == null || s.ActiveCar == null || !Edition.HasCareer) return false;
            if (!TrackCatalog.TryIndexOfShipped(VenueId, out int track)) return false;
            var def = TrackCatalog.At(track);
            int scene = TrackCatalog.SceneIndex(track);
            if (!TrackCatalog.SceneShipped(scene) || def.tunnels == null || def.tunnels.Length == 0) return false;

            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            // Banks nothing (see the class note): CrashStopGap, the damage ledger, the fates, a seizure and a
            // quit leg all stand down for a test drive, and the way home clears the handoff before the home
            // screen can read it.
            RaceHandoff.TestDrive = true;
            RaceHandoff.TestDriveKey = null;
            RaceHandoff.Solo = true;
            RaceHandoff.TrafficLevel = TrafficLevels.None;
            RaceHandoff.CarId = s.activeCar;
            RaceHandoff.CarSpecId = s.ActiveCar.specId;
            RaceHandoff.TrackIndex = track;
            RaceHandoff.CalendarDay = s.day;
            RaceHandoff.TimeOfDayIndex = Hour;
            RaceHandoff.WeatherOverride = (int)Weather.Clear;
            RaceHandoff.SkyOverride = Sky;
            RaceHandoff.RoadOverride = -1;
            // Live on frame one: no starter, no countdown (the intro places the car itself).
            RaceHandoff.RollingStartKmh = ApproachKmh;
            RaceHandoff.StartFuelPct = s.ActiveCar.fuel;
            LifeHomeScreen.FillCarRequestFor(s);
            Pending = true;
            Debug.Log("[NewGameIntro] launch: " + def.id + " on " + LifeRules.DateLabel(s.day) + ", hour " +
                      RaceHandoff.TimeOfDayIndex + ", car " + RaceHandoff.CarSpecId);
            Carrier.Get().LoadBehindBlack(scene);
            return true;
        }

        /// <summary>The wizard's tunnel: drips, a low draught, a faint echo, under the black (and on into the
        /// stage until the exit). Idempotent.</summary>
        public static void TunnelAmbience()
        {
            if (!On) return;
            Carrier.Get().StartAmbience();
        }

        /// <summary>Tools: stop the wizard's ambience (a check that leaves the wizard another way).</summary>
        public static void StopAmbience()
        {
            if (Carrier.Exists) Carrier.Get().Dispose();
        }

        // =================== the stage scene ===================

        RaceManager rm;
        TrackPath path;
        CarController car;
        PlayerCarInput input;
        Camera cam;
        Carrier carrier;
        int iIn, iOut, iCam, iEnd, carIdx;
        Vector3 camEye, camFwd;
        Quaternion camRot;
        float laneOff;
        float t0, attachT0 = -1f, controlT0 = -1f, driveT0 = -1f, outroT0 = -1f, cardT0 = -1f;
        float skipHeld, stoppedFor, strandedFor, sweepAt;
        bool assistOff, viewRestored, ambienceFading, haveLastProj;
        Matrix4x4 lastProj;
        ChaseCamera.View savedView;
        readonly List<CanvasGroup> hudGroups = new List<CanvasGroup>();
        readonly List<Canvas> hudHidden = new List<Canvas>();
        readonly List<GameObject> raceTexts = new List<GameObject>();
        Text centerText;
        bool centerMuted;
        float hudAlpha;

        /// <summary>RaceManager.Start, last thing: take the run over when <see cref="Launch"/> asked.</summary>
        public static void Attach(RaceManager race)
        {
            if (!Pending || race == null) return;
            Pending = false;
            var intro = race.gameObject.AddComponent<NewGameIntro>();
            intro.rm = race;
            if (!intro.Begin())
            {
                Debug.LogWarning("[NewGameIntro] this scene cannot stage the tunnel - on to the home screen");
                intro.EndReason = "could not stage";
                intro.Finish();
            }
        }

        bool Begin()
        {
            Instance = this;
            carrier = Carrier.Get();
            carrier.ShowBlack(1f);
            path = rm.path;
            car = rm.playerCar;
            var def = TrackCatalog.At(RaceHandoff.TrackIndex);
            TrackCatalog.EnsureStage(def);
            if (path == null || path.Count < 50 || car == null || def.stagePts == null || def.tunnels == null ||
                def.tunnels.Length == 0)
                return false;

            // The bore, from the bake's own points (forward metres): in at the far end of the table's span when
            // the venue is driven backwards, out at the near one.
            var pts = def.stagePts;
            Vector2 span = def.tunnels[0];
            int a = Mathf.Clamp(Mathf.RoundToInt(span.x / TrackCatalog.Spacing), 0, pts.Length - 1);
            int b = Mathf.Clamp(Mathf.RoundToInt(span.y / TrackCatalog.Spacing), 0, pts.Length - 1);
            Vector3 pIn = def.Reversed ? pts[b] : pts[a], pOut = def.Reversed ? pts[a] : pts[b];
            iIn = path.NearestIndex(pIn);
            iOut = path.NearestIndex(pOut);
            if (iOut - iIn < 10 || iOut - iIn > 200) { Debug.LogWarning($"[NewGameIntro] bore {iIn}..{iOut} makes no sense"); return false; }
            iCam = iIn + Mathf.RoundToInt((iOut - iIn) * CamIntoBore);
            int iCar = iCam - Mathf.RoundToInt(CarBackM / path.spacing);
            iEnd = iOut + Mathf.RoundToInt(DriveM / path.spacing);
            if (!path.HasEnds) iEnd = Mathf.Min(iEnd, path.Count - 12);
            laneOff = Mathf.Max(1.2f, def.roadWidth * 0.25f);

            // The lens: in the lane, a driver's eye over the road, looking down the bore at the exit.
            Vector3 eyeGround = LanePoint(iCam);
            eyeGround.y = RoadY(eyeGround, path.GetPoint(iCam).y);
            camEye = eyeGround + Vector3.up * EyeH;
            Vector3 look = LanePoint(iCam + 12);
            look.y = RoadY(look, path.GetPoint(iCam + 12).y) + EyeH * 0.9f;
            camFwd = (look - camEye); camFwd.y = 0f; camFwd.Normalize();
            camRot = Quaternion.LookRotation(look - camEye, Vector3.up);

            // The car: 125 m back in the same lane, rolling.
            Vector3 cp = LanePoint(iCar);
            cp.y = RoadY(cp, path.GetPoint(iCar).y) + 0.6f;
            Vector3 cf = path.GetTangent(iCar); cf.y = 0f;
            car.TeleportTo(cp, Quaternion.LookRotation(cf.normalized, Vector3.up));
            car.SetRolling(ApproachKmh / 3.6f);
            carIdx = iCar;
            var prog = rm.GetProgress(car);
            if (prog != null) prog.nearestIdx = iCar;
            input = car.GetComponent<PlayerCarInput>();
            if (input != null) input.enabled = false;
            car.throttleInput = 0.3f; car.brakeInput = 0f; car.steerInput = 0f;

            // Quiet: no replay, no pause (Esc and Start are the skip), no HUD until the attach.
            var replay = rm.GetComponent<RaceReplay>();
            if (replay != null) replay.enabled = false;
            foreach (var p in FindObjectsByType<PauseMenu>(FindObjectsInactive.Include)) p.enabled = false;
            var hud = FindAnyObjectByType<RaceHUD>();
            if (hud != null)
            {
                foreach (var t in new[] { hud.lapText, hud.timeText, hud.posText, hud.lastLapText })
                    if (t != null && t.gameObject.activeSelf) { t.gameObject.SetActive(false); raceTexts.Add(t.gameObject); }
                centerText = hud.centerText;
            }
            SweepCanvases();

            // The lens and its view: the chase rig keeps following (warm) under the standing lens.
            var chase = FindAnyObjectByType<ChaseCamera>();
            cam = chase != null ? chase.GetComponent<Camera>() : Camera.main;
            if (cam == null) return false;
            savedView = ChaseCamera.Current;
            ChaseCamera.PreviewView(savedView == ChaseCamera.View.Close ? ChaseCamera.View.Close : ChaseCamera.View.Chase);
            cam.transform.SetPositionAndRotation(camEye, camRot);

            // The car's own sound from outside, its lamps on, the eye held shut.
            EarOutside = true;
            CarLights.SetAll(true);
            ExposureAdapt.Scripted = 1f;
            ExposureAdapt.ForceTunnel = false;

            MenuKit.EnsureEventSystem();
            carrier.StartAmbience();
            carrier.BuildStageUI(LifeRules.DateOf(RaceHandoff.CalendarDay));
            t0 = Time.time;
            Now = Phase.Approach;
            Debug.Log($"[NewGameIntro] bore {iIn}..{iOut} ({(iOut - iIn) * path.spacing:0} m), lens at {iCam}, car from {iCar}, " +
                      $"end at {iEnd}; view {savedView}");
            return true;
        }

        Vector3 LanePoint(int i)
        {
            Vector3 p = path.GetPoint(i);
            Vector3 f = path.GetTangent(i); f.y = 0f;
            Vector3 right = Vector3.Cross(Vector3.up, f.normalized);
            return p + right * laneOff;
        }

        float RoadY(Vector3 at, float fallback)
        {
            var from = new Vector3(at.x, fallback + 3f, at.z);
            var hits = Physics.RaycastAll(from, Vector3.down, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            foreach (var h in hits)
            {
                if (car != null && h.collider.transform.IsChildOf(car.transform)) continue;
                if (h.point.y > best) best = h.point.y;
            }
            return best > float.MinValue ? best : fallback;
        }

        // =================== the frame ===================

        void Update()
        {
            if (Now == Phase.None || car == null) return;
            float now = Time.time, dt = Time.deltaTime;
            carIdx = path.NearestIndex(car.transform.position, carIdx, 30);
            Along = Vector3.Dot(car.transform.position - camEye, camFwd);

            StepSkip(dt);
            if (Now == Phase.Outro) { StepOutro(); return; }

            // the autopilot: until the controls pass, through the hand-over, and for the tools
            float autoSteer = 0f, autoThrottle = 0f, autoBrake = 0f;
            Autopilot(ref autoSteer, ref autoThrottle, ref autoBrake);
            if (!Control || AutoDrive)
            {
                car.steerInput = autoSteer; car.throttleInput = autoThrottle; car.brakeInput = autoBrake;
                car.handbrakeInput = false;
            }
            else if (!assistOff)
            {
                // The player's hands first: anything they touch ends the hand at once.
                if (Mathf.Abs(car.steerInput) > 0.08f || car.throttleInput > 0.05f || car.brakeInput > 0.05f) assistOff = true;
                else
                {
                    float k = 1f - Mathf.Clamp01((now - controlT0) / AssistS);
                    if (k <= 0f) assistOff = true;
                    car.steerInput = Mathf.Lerp(car.steerInput, autoSteer, k);
                    car.throttleInput = Mathf.Max(car.throttleInput, autoThrottle * k);
                }
            }

            switch (Now)
            {
                case Phase.Approach:
                {
                    float lift = Smooth01(Mathf.InverseLerp(-RevealFromM, -RevealToM, Along));
                    // never before the first second of black
                    if (now - t0 < 1f) lift = 0f;
                    carrier.ShowBlack(1f - lift);
                    if (Along >= AttachAtM)
                    {
                        Now = Phase.Attach;
                        attachT0 = now;
                        EarOutside = false;           // the chase rig's ear, eased over EngineAudio.BlendSeconds
                        ExposureAdapt.Scripted = -1f; // the eye is free: it opens in the bore
                        carrier.ShowBlack(0f);
                        Debug.Log($"[NewGameIntro] attach at {now - t0:0.0} s, {car.speedKmh:0} km/h");
                    }
                    break;
                }
                case Phase.Attach:
                {
                    Blend = Smooth01((now - attachT0) / BlendS);
                    if (!Control && Blend >= ControlAtW) GiveControl(now);
                    if (Blend >= 1f)
                    {
                        Now = Phase.Drive;
                        driveT0 = now;
                        RestoreView();
                    }
                    break;
                }
                case Phase.Drive:
                    StepDrive(now, dt);
                    break;
            }

            ExposureAdapt.ForceTunnel = carIdx >= iIn && carIdx <= iOut;
            if (attachT0 >= 0f)
            {
                float h = Mathf.Clamp01((now - attachT0 - BlendS * 0.45f) / HudFadeS);
                if (h != hudAlpha) { hudAlpha = h; ApplyHud(); }
            }
            if (Time.unscaledTime >= sweepAt && hudAlpha < 1f)
            {
                sweepAt = Time.unscaledTime + 0.25f;
                SweepCanvases();
            }
            StepCaption(now);
        }

        void GiveControl(float now)
        {
            Control = true;
            controlT0 = now;
            if (input != null) { input.enabled = true; input.inputEnabled = true; }
            Debug.Log($"[NewGameIntro] controls passed at {now - t0:0.0} s, {car.speedKmh:0} km/h");
        }

        void RestoreView()
        {
            if (viewRestored) return;
            viewRestored = true;
            if (savedView != ChaseCamera.Current) ChaseCamera.PreviewView(savedView);
        }

        void StepDrive(float now, float dt)
        {
            if (!Exited && carIdx > iOut)
            {
                Exited = true;
                ExitedAt = now;
                carrier.FadeAmbience(2.5f);
                Debug.Log($"[NewGameIntro] out of the bore at {now - t0:0.0} s, {car.speedKmh:0} km/h, eye {ExposureAdapt.Current:0.00}");
            }
            PastExitM = Mathf.Max(PastExitM, (carIdx - iOut) * path.spacing);
            float speed = Mathf.Abs(car.forwardSpeed);
            stoppedFor = speed < 0.6f ? stoppedFor + dt : 0f;
            strandedFor = CareerDrive.NeedsTow(car) ? strandedFor + dt : 0f;
            if (carIdx >= iEnd) BeginOutro("the road to Gillespie Gap (" + PastExitM.ToString("0") + " m past the exit)");
            else if (now - driveT0 > DriveMaxS) BeginOutro("time");
            else if (stoppedFor > StoppedMaxS) BeginOutro("stopped");
            else if (strandedFor > StrandedMaxS) BeginOutro("stranded");
        }

        void StepCaption(float now)
        {
            float a = 0f;
            if (Exited)
            {
                float t = now - ExitedAt - CaptionAfterExitS;
                a = t < 0f ? 0f : t < 0.8f ? t / 0.8f : t < CaptionS - 1f ? 1f : Mathf.Clamp01((CaptionS - t) / 1f);
            }
            carrier.ShowCaption(a);
            // Nothing over the caption while it is up: the notice line hangs in the same place.
            bool mute = a > 0f;
            if (centerText != null && mute != centerMuted) { centerMuted = mute; centerText.enabled = !mute; }
        }

        // =================== the lens ===================

        void LateUpdate()
        {
            if (cam == null || Now == Phase.None) return;
            bool standing = Now == Phase.Approach || (Now == Phase.Attach && Blend < 1f);
            if (!standing)
            {
                if (haveLastProj)
                {
                    // Hand the projection back. A rig that shifts its lens set its own matrix this frame (keep it);
                    // one that does not left ours, and goes back to its field of view.
                    haveLastProj = false;
                    if (cam.projectionMatrix == lastProj) cam.ResetProjectionMatrix();
                }
                return;
            }
            float w = Now == Phase.Attach ? Blend : 0f;
            // The chase rig has just placed the lens (ChaseCamera.LateUpdate runs first): ease from the standing
            // pose onto it.
            Vector3 p1 = cam.transform.position;
            Quaternion r1 = cam.transform.rotation;
            cam.transform.SetPositionAndRotation(Vector3.Lerp(camEye, p1, w), Quaternion.Slerp(camRot, r1, w));
            // The lens: what the rig set this frame (a shifted matrix, or its field of view), eased from ours.
            var cur = cam.projectionMatrix;
            bool rigSet = !haveLastProj || cur != lastProj;
            Matrix4x4 rigP = rigSet ? cur : Matrix4x4.Perspective(cam.fieldOfView, cam.aspect, cam.nearClipPlane, cam.farClipPlane);
            Matrix4x4 myP = Matrix4x4.Perspective(IntroFov, cam.aspect, cam.nearClipPlane, cam.farClipPlane);
            Matrix4x4 m = new Matrix4x4();
            for (int k = 0; k < 16; k++) m[k] = Mathf.Lerp(myP[k], rigP[k], w);
            cam.projectionMatrix = m;
            lastProj = m;
            haveLastProj = true;
        }

        // =================== the car, driven ===================

        void Autopilot(ref float steer, ref float throttle, ref float brake)
        {
            float v = Mathf.Max(0f, car.forwardSpeed);
            float look = Mathf.Clamp(v * 0.9f + 6f, 8f, 28f);
            int j = carIdx + Mathf.CeilToInt(look / path.spacing);
            Vector3 target = LanePoint(j);
            Vector3 local = car.transform.InverseTransformPoint(target);
            float ang = Mathf.Atan2(local.x, Mathf.Max(0.5f, local.z)) * Mathf.Rad2Deg;
            float lockDeg = Mathf.Max(4f, car.CurrentMaxSteerDeg);
            steer = Mathf.Clamp(ang / lockDeg, -1f, 1f);

            // speed: the approach speed in the bore; for the tools' drive the bend ahead sets it
            float want = ApproachKmh / 3.6f;
            if (AutoDrive && Control)
            {
                want = 70f / 3.6f;
                float turn = 0f;
                for (int k = 2; k <= 14; k += 2)
                {
                    Vector3 f0 = path.GetTangent(carIdx), f1 = path.GetTangent(carIdx + k * 3);
                    f0.y = 0f; f1.y = 0f;
                    turn = Mathf.Max(turn, Vector3.Angle(f0, f1) / (k * 3 * path.spacing));
                }
                if (turn > 0.001f)
                {
                    float r = Mathf.Rad2Deg / turn;   // metres per radian over the window
                    want = Mathf.Min(want, Mathf.Sqrt(0.32f * 9.81f * r));
                }
                want = Mathf.Max(want, 8f);
            }
            float err = want - v;
            throttle = Mathf.Clamp01(0.25f + err * 0.35f);
            brake = err < -1.5f ? Mathf.Clamp01(-err * 0.12f) : 0f;
            if (brake > 0f) throttle = 0f;
        }

        // =================== the HUD ===================

        /// <summary>Every root canvas of the drive (the HUD, the dials, the touch controls) held at the intro's
        /// HUD level; the pause menu's two hidden for the whole intro; the picture itself and ours left alone.</summary>
        void SweepCanvases()
        {
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
            {
                if (!c.isRootCanvas || c.gameObject.name == "DisplayCanvas") continue;
                if (carrier != null && c == carrier.Canvas) continue;
                if (c.gameObject.name == "PauseCanvas" || c.gameObject.name == "MenuCanvas")
                {
                    if (c.enabled) { c.enabled = false; hudHidden.Add(c); }
                    continue;
                }
                var g = c.GetComponent<CanvasGroup>();
                if (g == null)
                {
                    g = c.gameObject.AddComponent<CanvasGroup>();
                    hudGroups.Add(g);
                    g.alpha = hudAlpha;
                    g.interactable = g.blocksRaycasts = hudAlpha > 0.5f;
                }
                else if (!hudGroups.Contains(g) && c.enabled && hudAlpha < 1f)
                {
                    // a canvas with a group of its own: shown whole when the HUD is
                    c.enabled = false;
                    hudHidden.Add(c);
                }
            }
        }

        void ApplyHud()
        {
            foreach (var g in hudGroups)
            {
                if (g == null) continue;
                g.alpha = hudAlpha;
                g.interactable = g.blocksRaycasts = hudAlpha > 0.5f;
            }
            if (hudAlpha >= 1f)
            {
                foreach (var c in hudHidden)
                    if (c != null && c.gameObject.name != "PauseCanvas" && c.gameObject.name != "MenuCanvas") c.enabled = true;
            }
        }

        // =================== skip, and the way home ===================

        void StepSkip(float dt)
        {
            if (Now == Phase.Outro || Now == Phase.None) { carrier.ShowSkip(0f, false); return; }
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool held = (kb != null && kb.escapeKey.isPressed) || (pad != null && pad.startButton.isPressed) ||
                        carrier.SkipButtonHeld;
            skipHeld = held ? skipHeld + Time.unscaledDeltaTime : 0f;
            carrier.ShowSkip(Mathf.Clamp01(skipHeld / SkipHoldS), true);
            if (skipHeld >= SkipHoldS) BeginOutro("skipped");
        }

        /// <summary>Fade to black, CHARLOTTE, home. Also the tools' skip.</summary>
        public void BeginOutro(string why)
        {
            if (Now == Phase.Outro) return;
            Now = Phase.Outro;
            EndReason = why;
            outroT0 = Time.time;
            if (input != null) input.enabled = false;
            Debug.Log($"[NewGameIntro] the end ({why}) at {Time.time - t0:0.0} s, {PastExitM:0} m past the exit");
        }

        void StepOutro()
        {
            // the car rolls on under the fade, gently, rather than stopping dead
            car.steerInput = 0f; car.throttleInput = 0f; car.brakeInput = 0.15f; car.handbrakeInput = false;
            float t = Time.time - outroT0;
            carrier.ShowBlack(Mathf.Max(carrier.BlackAlpha, Smooth01(t / OutroFadeS)));
            carrier.FadeAmbience(OutroFadeS);
            if (t < OutroFadeS) return;
            if (cardT0 < 0f) { cardT0 = Time.time; AudioFader.FadeOut(); }
            float c = Time.time - cardT0;
            carrier.ShowCard(Mathf.Clamp01(c / CardInS));
            if (c >= CardInS + CardHoldS) Finish();
        }

        void Finish()
        {
            if (Now == Phase.None && EndReason == null) return;
            Now = Phase.None;
            Cleanup();
            RaceHandoff.ClearAll();
            // the card stays up over the load; the home screen's first frame takes it away
            if (carrier != null) carrier.DisposeOnNextScene();
            Debug.Log("[NewGameIntro] home (" + EndReason + ")");
            SceneManager.LoadScene(0);
        }

        void Cleanup()
        {
            EarOutside = false;
            ExposureAdapt.Scripted = -1f;
            ExposureAdapt.ForceTunnel = false;
            CarLights.SetAll(TimeOfDay.At(TimeOfDay.Current).lightsOn);
            if (centerText != null) centerText.enabled = true;
            foreach (var go in raceTexts) if (go != null) go.SetActive(true);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (Now != Phase.None) Cleanup();
        }

        static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        static int EnvInt(string key, int dflt, int min, int max)
        {
            string e = System.Environment.GetEnvironmentVariable(key);
            return int.TryParse(e, out int v) ? Mathf.Clamp(v, min, max) : dflt;
        }

        // =================== the carrier: across the scene loads ===================

        /// <summary>
        /// The black, the tunnel's sound, the skip prompt, the caption and the CHARLOTTE card: one object that
        /// lives from the wizard to the home screen (DontDestroyOnLoad), so the black never blinks and the drips
        /// do not stop over a scene load. Menus' own canvases stand at 10-200; this is over all of them.
        /// </summary>
        public sealed class Carrier : MonoBehaviour
        {
            static Carrier inst;
            public static bool Exists => inst != null;
            public static Carrier Get()
            {
                if (inst != null) return inst;
                var go = new GameObject("~NewGameIntro");
                DontDestroyOnLoad(go);
                inst = go.AddComponent<Carrier>();
                inst.BuildCanvas();
                return inst;
            }

            public Canvas Canvas { get; private set; }
            Image black;
            CanvasGroup caption, skip, card;
            Image skipFill;
            Text skipLabel, captionLine2, cardLine2;
            HoldButton skipButton;
            public float BlackAlpha { get; private set; }
            public float CardAlpha => card != null ? card.alpha : 0f;
            public bool SkipButtonHeld => skipButton != null && skipButton.Held;

            AudioSource draught, drips;
            AudioClip[] dripClips;
            float nextDrip, ambienceGain = 1f, ambienceFadeTo = 1f, ambienceFadeRate;
            bool disposeOnLoad;

            void BuildCanvas()
            {
                Canvas = MenuKit.Canvas(transform, "IntroCanvas", 30000);
                // the order is the drawing order: caption under the black, the skip prompt and the card over it
                var capRoot = MenuKit.Stretch(Canvas.transform, "Caption", Vector2.zero, Vector2.one, 0f, 0f, 0f, 0f);
                caption = capRoot.gameObject.AddComponent<CanvasGroup>();
                caption.alpha = 0f; caption.blocksRaycasts = false; caption.interactable = false;
                MenuKit.Label(capRoot, "BLUE RIDGE PARKWAY", MenuKit.Head, new Vector2(0.5f, 1f), new Vector2(0f, -30f),
                    TextAnchor.MiddleCenter, MenuKit.Accent, 900f, 44f, bold: true);
                captionLine2 = MenuKit.Label(capRoot, "", MenuKit.Small, new Vector2(0.5f, 1f), new Vector2(0f, -70f),
                    TextAnchor.MiddleCenter, MenuKit.Dim, 900f, 32f);

                black = MenuKit.Panel(Canvas.transform, "Black", Color.black).GetComponent<Image>();
                black.raycastTarget = false;
                SetBlack(0f);

                // SKIP, top left: the lap line's corner, which the intro leaves empty (the touch map hangs below it)
                var sk = MenuKit.Rect(Canvas.transform, "Skip", new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(20f, -14f), new Vector2(300f, 42f), new Color(0f, 0f, 0f, 0.45f));
                skip = sk.gameObject.AddComponent<CanvasGroup>();
                skip.alpha = 0f; skip.blocksRaycasts = false;
                skipButton = sk.gameObject.AddComponent<HoldButton>();
                skipLabel = MenuKit.Label(sk, "HOLD ESC TO SKIP", MenuKit.Tiny, new Vector2(0f, 0.5f), new Vector2(14f, 2f),
                    TextAnchor.MiddleLeft, MenuKit.Dim, 280f, 32f);
                var fill = MenuKit.Rect(sk, "Fill", new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(300f, 4f), MenuKit.Accent);
                skipFill = fill.GetComponent<Image>();
                skipFill.raycastTarget = false;
                fill.localScale = new Vector3(0f, 1f, 1f);

                var cr = MenuKit.Stretch(Canvas.transform, "Card", Vector2.zero, Vector2.one, 0f, 0f, 0f, 0f);
                card = cr.gameObject.AddComponent<CanvasGroup>();
                card.alpha = 0f; card.blocksRaycasts = false; card.interactable = false;
                MenuKit.Label(cr, "CHARLOTTE", 72, new Vector2(0.5f, 0.5f), new Vector2(0f, 26f),
                    TextAnchor.MiddleCenter, MenuKit.Accent, 900f, 96f, bold: true);
                cardLine2 = MenuKit.Label(cr, "", MenuKit.Small, new Vector2(0.5f, 0.5f), new Vector2(0f, -40f),
                    TextAnchor.MiddleCenter, MenuKit.Dim, 900f, 32f);
            }

            /// <summary>The drive's own UI: the date on the caption and the card, the skip prompt's words.</summary>
            public void BuildStageUI(System.DateTime date)
            {
                string d = date.ToString("dddd d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant();
                if (captionLine2 != null) captionLine2.text = "LITTLE SWITZERLAND  ·  " + d;
                if (cardLine2 != null) cardLine2.text = "NORTH CAROLINA  ·  " + d;
                if (skipLabel != null)
                    skipLabel.text = TouchControls.Showing ? "HOLD HERE TO SKIP"
                                   : Gamepad.current != null ? "HOLD START OR ESC TO SKIP" : "HOLD ESC TO SKIP";
            }

            void SetBlack(float a)
            {
                BlackAlpha = Mathf.Clamp01(a);
                if (black != null)
                {
                    black.color = new Color(0f, 0f, 0f, BlackAlpha);
                    black.enabled = BlackAlpha > 0.001f;
                    // up, it takes the clicks: nothing under it (the wizard's DRIVE THIS ONE) is pressed twice
                    black.raycastTarget = BlackAlpha > 0.5f;
                }
            }

            public void ShowBlack(float a) => SetBlack(a);
            public void ShowCaption(float a) { if (caption != null) caption.alpha = a; }
            public void ShowCard(float a) { if (card != null) card.alpha = a; }

            public void ShowSkip(float progress, bool visible)
            {
                if (skip == null) return;
                skip.alpha = visible ? 0.9f : 0f;
                skip.blocksRaycasts = visible;
                if (skipFill != null) skipFill.rectTransform.localScale = new Vector3(progress, 1f, 1f);
            }

            /// <summary>Black over the wizard at once; the stage loads a couple of frames later, so the black is
            /// what stands on screen through the load.</summary>
            public void LoadBehindBlack(int scene)
            {
                SetBlack(1f);
                StartCoroutine(LoadSoon(scene));
            }

            IEnumerator LoadSoon(int scene)
            {
                yield return null;
                yield return null;
                SceneGate.Load(scene);
            }

            /// <summary>The card stays up through the home screen's load; the first frame there takes it away.</summary>
            public void DisposeOnNextScene()
            {
                disposeOnLoad = true;
                SceneManager.sceneLoaded -= OnLoaded;
                SceneManager.sceneLoaded += OnLoaded;
            }

            void OnLoaded(Scene s, LoadSceneMode m)
            {
                SceneManager.sceneLoaded -= OnLoaded;
                if (disposeOnLoad) Dispose();
            }

            public void Dispose()
            {
                SceneManager.sceneLoaded -= OnLoaded;
                if (inst == this) inst = null;
                Destroy(gameObject);
            }

            // ---- the tunnel's sound ----

            public void StartAmbience()
            {
                if (draught != null) return;
                float fx = AudioPrefs.Effects;
                var wind = Resources.Load<AudioClip>("Sfx/wind_low");
                if (wind != null)
                {
                    var go = new GameObject("Draught");
                    go.transform.SetParent(transform, false);
                    draught = go.AddComponent<AudioSource>();
                    draught.clip = wind; draught.loop = true; draught.spatialBlend = 0f;
                    draught.pitch = 0.62f;
                    draught.bypassListenerEffects = true;
                    draught.volume = 0.30f * fx;
                    var lp = go.AddComponent<AudioLowPassFilter>();
                    lp.cutoffFrequency = 420f;
                    var rv = go.AddComponent<AudioReverbFilter>();
                    rv.reverbPreset = AudioReverbPreset.Cave;
                    draught.Play();
                }
                var dg = new GameObject("Drips");
                dg.transform.SetParent(transform, false);
                drips = dg.AddComponent<AudioSource>();
                drips.spatialBlend = 0f;
                drips.bypassListenerEffects = true;
                drips.playOnAwake = false;
                var echo = dg.AddComponent<AudioEchoFilter>();
                echo.delay = 240f; echo.decayRatio = 0.38f; echo.wetMix = 0.55f; echo.dryMix = 1f;
                var cave = dg.AddComponent<AudioReverbFilter>();
                cave.reverbPreset = AudioReverbPreset.Cave;
                dripClips = new[] { MakeDrip(1350f, 2300f, 1), MakeDrip(1700f, 2900f, 2), MakeDrip(1100f, 1900f, 3) };
                nextDrip = Time.unscaledTime + 0.6f;
                ambienceGain = ambienceFadeTo = 1f;
            }

            /// <summary>A water drop: a short rising sine "plink", fast decay, a hiss of the splash under it.</summary>
            static AudioClip MakeDrip(float f0, float f1, int seed)
            {
                const int rate = 44100;
                int n = (int)(rate * 0.16f);
                var data = new float[n];
                var rng = new System.Random(seed);
                double phase = 0;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)rate;
                    float f = Mathf.Lerp(f0, f1, Mathf.Clamp01(t / 0.018f));
                    phase += 2.0 * System.Math.PI * f / rate;
                    float env = Mathf.Exp(-t / 0.030f) * Mathf.Clamp01(t / 0.0015f);
                    float splash = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t / 0.006f) * 0.25f;
                    data[i] = ((float)System.Math.Sin(phase) * env + splash) * 0.8f;
                }
                var clip = AudioClip.Create("drip" + seed, n, 1, rate, false);
                clip.SetData(data, 0);
                return clip;
            }

            public void FadeAmbience(float seconds)
            {
                ambienceFadeTo = 0f;
                ambienceFadeRate = Mathf.Max(ambienceFadeRate, 1f / Mathf.Max(0.1f, seconds));
            }

            void Update()
            {
                if (draught == null && drips == null) return;
                if (ambienceFadeTo < ambienceGain)
                    ambienceGain = Mathf.MoveTowards(ambienceGain, ambienceFadeTo, ambienceFadeRate * Time.unscaledDeltaTime);
                float fx = AudioPrefs.Effects;
                if (draught != null) draught.volume = 0.30f * fx * ambienceGain;
                if (drips != null && dripClips != null && ambienceGain > 0.01f && Time.unscaledTime >= nextDrip)
                {
                    drips.pitch = Random.Range(0.85f, 1.2f);
                    drips.panStereo = Random.Range(-0.7f, 0.7f);
                    drips.PlayOneShot(dripClips[Random.Range(0, dripClips.Length)], Random.Range(0.10f, 0.26f) * fx * ambienceGain);
                    nextDrip = Time.unscaledTime + Random.Range(0.7f, 3.2f);
                }
            }
        }

        /// <summary>The corner button: held, for the touch and the mouse.</summary>
        public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
        {
            public bool Held { get; private set; }
            public void OnPointerDown(PointerEventData e) => Held = true;
            public void OnPointerUp(PointerEventData e) => Held = false;
            public void OnPointerExit(PointerEventData e) => Held = false;
            void OnDisable() => Held = false;
        }
    }
}
