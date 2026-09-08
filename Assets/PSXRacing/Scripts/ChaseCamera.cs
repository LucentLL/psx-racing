using UnityEngine;
using UnityEngine.InputSystem;

namespace PSXRacing
{
    /// <summary>
    /// The driving camera, in six views: two chase distances, three mounted on
    /// the car (roof, bonnet, front bumper) and one overhead.
    ///
    /// Every mounted view is positioned off the car's own BoxCollider rather
    /// than from constants. CarBody resizes that collider to whichever of the
    /// sixteen shells the player is driving, so a bumper cam pinned at a fixed
    /// 1.9 m sits inside the nose of a Land Rover and a metre in front of a
    /// supermini. Reading the box is also the only version that stays correct
    /// when the LifeSim hands over a different car mid-Start.
    ///
    /// Cycled with C, gamepad north, or the CAMERA row in the pause menu. On a
    /// phone the pause menu is the only one of those that exists, which is why
    /// that row is where the choice lives now: the permanent CAM button this
    /// used to read was two thumb-widths of screen spent on something a player
    /// touches once a race. The choice is remembered across races.

    /// </summary>
    public class ChaseCamera : MonoBehaviour
    {
        public Transform target;
        public CarController targetCar;

        /// <summary>Chase distance for a car the length of the reference FD.
        /// Scaled by the car actually being driven — see <see cref="LengthFit"/>.</summary>
        public float distance = 5.4f;
        public float height = 1.8f;
        public float lookHeight = 0.9f;
        /// <summary>Position follow rate, 1/s. This used to be
        /// <c>5 + 0.08 * speed</c>, and the speed term was hiding something:
        /// a first-order lag chasing a target moving at v sits v/lag behind
        /// it in the steady state, so the "5.4 m" chase distance was really
        /// 9.4 m at 100 km/h and 11.3 m at 200 — the car SHRANK in the frame
        /// as it sped up, which is the opposite of a speed cue. The speed term
        /// is gone and the along-forward lag is clamped instead (see
        /// <see cref="lagClampM"/>), so distance is now the number it says.</summary>
        public float positionLag = 5f;
        /// <summary>Yaw follow rate while gripping, 1/s. Was 7 — faster than
        /// the reference game manages while gripping (6) and with no drift
        /// branch at all, so the lens was at its most eager exactly when the
        /// yaw rate was highest.</summary>
        public float rotationLag = DefaultRotationLag;
        public const float DefaultRotationLag = 5.5f;
        /// <summary>And the rate while sideways. Read through
        /// <see cref="RotationLagDriftOf"/> rather than directly: belt and
        /// braces against a rate of zero, which would stop the camera turning
        /// altogether in a slide. (A field ABSENT from a scene's YAML keeps its
        /// C# initialiser — Unity only overwrites the keys it finds — so the
        /// real stale-value hazard is a key that IS written there, like
        /// <see cref="rotationLag"/>, which every baked scene carries at its
        /// old 7. That is what the builder's stamp and a rebake are for.)</summary>
        public float rotationLagDrift = DefaultRotationLagDrift;
        public const float DefaultRotationLagDrift = 3.5f;
        /// <summary>How much of the aim may be handed to the travel direction
        /// at full body slip, and the slip that counts as full. Racing Game 2's
        /// CAM_VEL_BLEND / CAM_SLIP_FULL.</summary>
        public float aimVelBlendMax = DefaultAimVelBlendMax;
        public const float DefaultAimVelBlendMax = 0.6f;
        public float aimSlipFullRad = DefaultAimSlipFullRad;
        public const float DefaultAimSlipFullRad = 0.35f;
        /// <summary>Hard ceiling on how far off the nose the aim may be pulled,
        /// degrees. Bounding the RATIO is not enough — see the block in Follow.</summary>
        public const float MaxAimOffsetDeg = 28f;
        /// <summary>Low-pass on the travel direction, 1/s, gripping and
        /// drifting. Unfiltered it carries every kerb.</summary>
        public float velFilterGrip = DefaultVelFilterGrip;
        public float velFilterDrift = DefaultVelFilterDrift;
        public const float DefaultVelFilterGrip = 10f;
        public const float DefaultVelFilterDrift = 14f;
        public float baseFOV = 58f;

        [Header("Speed rig (Black Box style: every value is a low/high pair over speed)")]
        /// <summary>
        /// Road speed at which the speed rig is fully wound in: 55.6 m/s is
        /// 200 km/h. The wind-in is a SMOOTHSTEP of speed/this rather than a
        /// straight ramp, so almost nothing happens below ~50 km/h (the town,
        /// the forecourt, parking) and the pull lands in the top half of the
        /// range where the speed is — MW05 keeps (low, high) pairs per camera
        /// and interpolates between them; this is the same thing with one
        /// curve shared by every pair.
        /// </summary>
        public float speedFullMps = DefaultSpeedFullMps;
        public const float DefaultSpeedFullMps = 55.6f;
        /// <summary>
        /// Degrees of FOV pull at full speed in the two CHASE views: 58 at
        /// rest, 62 at 100 km/h, 66 at 200. Modders call the MW05 behaviour
        /// "FOV pull" and it is the single cheapest speed cue there is: a
        /// projection change, free on a 240-line target where wide-angle
        /// aliasing is invisible. It went to 18 in the sense-of-speed pass and
        /// that was too far — see the note under the constant.
        /// </summary>
        public float speedFOV = DefaultChaseSpeedFOV;
        public const float DefaultChaseSpeedFOV = 8f;
        // Was 18, and eighteen degrees is a third again of the field of view:
        // 58 at rest, 67 at 100 km/h, 76 at 200. Black Box games do pull the
        // lens, but by a handful of degrees — at 76 the periphery streams past
        // fast enough to read as a boost effect rather than as speed, and it is
        // stacked on top of the pull-back, the drop and the look-ahead. Eight
        // gives 58 / 62 / 66: felt, not noticed.
        /// <summary>
        /// Mounted views get an INDEPENDENT 9 degrees, fixed by the near-plane
        /// geometry rather than derived from the chase pull — it used to be
        /// half of it, and is now slightly more than it. Their speed comes from
        /// the road surface streaming past a lens a foot off it, not from the
        /// lens, and <see cref="MountClearance"/> was sized against the hood FOV:
        /// the bonnet enters the frame at clearance / tan(halfFOV + pitch),
        /// and at 63 + 9 = 72 deg that is 0.18 / tan(38 deg) = 0.230 m — 1.28x
        /// the 0.18 m near plane, the same margin the clearance was designed
        /// to (1.3x at the old 71). At the full 18 it would be 0.196 m, 1.09x,
        /// and the near plane would start opening the bonnet on a bump.
        /// </summary>
        public const float MountSpeedFOV = 9f;
        /// <summary>Hard ceilings per mounted view, whatever the ramp says.
        /// Asserted by the self-test together with the bonnet-entry margin.</summary>
        public const float HoodMaxFOV = 80f;
        public const float BumperMaxFOV = 86f;
        /// <summary>Metres the chase camera backs off at full speed (scaled by
        /// the car's LengthFit), and metres it drops. Lower and further is
        /// the MW05 framing: more road in the top of the frame, the car
        /// lower in it. Starting values.</summary>
        public float speedPullBack = 0.9f;
        public float speedDrop = 0.25f;
        /// <summary>Extra metres of look-ahead at full speed, on top of the
        /// fixed 1.5. Looking further up the road drops the car in the frame
        /// and shows the corner arriving, which is what a fast car looks like
        /// from behind. Starting value.</summary>
        public float speedLookAhead = DefaultSpeedLookAhead;
        public const float DefaultSpeedLookAhead = 2.5f;   // was 4 — see DefaultChaseSpeedFOV
        /// <summary>
        /// Bound on how far along its own forward axis the follow lag may sit
        /// behind the wanted position, metres. With the speed term gone from
        /// <see cref="positionLag"/> a launch would otherwise pull the car
        /// 11 m out of the frame; 1.2 m is enough to see it surge and not
        /// enough to lose it. Lateral and vertical lag are left free — they
        /// are what makes the rig feel hung on rubber through a corner.
        /// </summary>
        public float lagClampM = 1.2f;
        /// <summary>
        /// Acceleration-coupled distance (research A2): metres of chase
        /// distance per m/s^2 of forward acceleration, so throttle stretches
        /// the rig and braking zooms in — MW05 hard-codes the brake zoom and
        /// carries LAG per camera for the launch stretch; one scalar does
        /// both. 0.12 puts a first-gear launch at ~0.6 g = 5.9 m/s^2 at 0.7 m
        /// and 0.9 g of braking at -1.06 m, inside the +/-25% limit below.
        /// </summary>
        public float accelDistPerMps2 = 0.05f;
        public float accelDistLimitFrac = 0.25f;
        /// <summary>Smoothing on the acceleration read, 1/s. Forward speed
        /// changes once a physics tick, so the per-frame difference is spiky;
        /// 6/s settles in ~0.5 s, which is the "returns within half a second"
        /// the design asks for.</summary>
        public float accelLag = 6f;
        /// <summary>
        /// Drift swing: metres the chase camera moves sideways per radian of
        /// body slip, toward the OUTSIDE of the slide, so the frame shows the
        /// car's flank and the nose pointing at the apex. Gated on the car's
        /// own Drifting flag and lagged, or a grip-limit corner would wag the
        /// camera on every entry. The slip is clamped at
        /// <see cref="DriftSwingClampRad"/> (34 deg) so a spin does not orbit
        /// the lens round the car. Starting value.
        /// </summary>
        public float driftSwing = 0.5f;
        public float driftSwingLag = 2.5f;
        public const float DriftSwingClampRad = 0.6f;

        // ---- roll bias, Sh2dow's NFS.CameraMod constants (research B6) ------
        // latG = |yawRate| * speed * LatGScale, smoothstepped from LatGStart to
        // LatGFull, MaxRollDeg at full, wound in at RollWindIn and out at
        // RollUnwind (both 1/s), slew-limited to RollSlewDegPerSec. In this
        // game's units v*yawRate IS lateral acceleration in m/s^2, so the 0.16
        // scale makes "1.0" about 0.64 g and "5.5" about 3.5 g: a grip-limit
        // corner (1.3 g, latG 2.0) sits UNDER LatGStart and rolls exactly zero,
        // and a committed drift (1 rad/s at 30 m/s, latG 4.8) rolls about 1.5
        // of the 1.8 deg available — the roll is a DRIFT cue and a gripping
        // corner does not tilt at all. Chase views only; the camera banks INTO
        // the turn, the UG2 lean.
        public const float LatGScale = 0.16f;
        public const float LatGStart = 2.6f;   // ordinary steering at 30 m/s makes ~1 g of this measure, so a 1.0 threshold wound the roll in and out on every correction
        public const float LatGFull = 5.5f;
        public const float MaxRollDeg = 1.8f;
        public const float RollWindIn = 3.5f;
        public const float RollUnwind = 11f;
        public const float RollSlewDegPerSec = 45f;

        [Header("Speed shake (MW05 RoadNoiseRecord: amplitude ramped between two speeds)")]
        /// <summary>
        /// Continuous road noise, an order of magnitude under the impact
        /// shake: 0.25 deg against 3.4. MW05 carries {Frequency, Amplitude,
        /// MinSpeed, MaxSpeed} per SURFACE for exactly this; here the surface
        /// is the car's own onRoad flag, and gravel shakes 2.5x tarmac. Ramps
        /// in from 38 m/s (137 km/h) to full at 63 (227), at 11 Hz through the
        /// same Perlin noise the impact shake uses. Held small on purpose — a
        /// phone is read at arm's length and a straight must not nauseate.
        /// None in TOP DOWN (nothing under the lens to shake it) and half in
        /// COCKPIT (the cabin overlay does not move, and the world jittering
        /// behind a still dashboard reads as a bug). Starting values.
        /// </summary>
        public float speedShakeStartMps = DefaultSpeedShakeStartMps;
        public float speedShakeSpanMps = DefaultSpeedShakeSpanMps;
        public float speedShakeDeg = DefaultSpeedShakeDeg;
        public float speedShakePos = 0.03f;
        public float speedShakeHz = 11f;
        public const float DefaultSpeedShakeStartMps = 47f;
        public const float DefaultSpeedShakeSpanMps = 25f;
        public const float DefaultSpeedShakeDeg = 0.10f;
        public const float OffroadShakeMul = 2.5f;
        public const float CockpitShakeMul = 0.5f;

        [Header("Impact shake")]
        /// <summary>Peak angular kick in degrees at full trauma.</summary>
        public float shakeAngleDeg = 3.4f;
        public float shakePosition = 0.22f;
        public float shakeFrequency = 22f;
        public float traumaDecay = 1.9f;

        /// <summary>The camera CollisionResponder shakes. Set by whichever chase
        /// camera is following the player, so the responder does not have to
        /// search the scene on every impact.</summary>
        public static ChaseCamera Active;

        /// <summary>
        /// Cockpit sits between BUMPER and TOP DOWN rather than next to the
        /// other two chase views, and that is deliberate on both sides. The
        /// cycle runs outside-in — behind the car, closer, on the roof, on the
        /// bonnet, on the bumper, in the driver's seat — and TOP DOWN has to
        /// stay LAST because it is the only conditional view and
        /// <see cref="CycleLength"/> drops it by shortening the cycle.
        /// Appending here also leaves every existing index alone, so a saved
        /// preference from before the cockpit existed still means what it meant.
        /// </summary>
        public enum View
        {
            Chase = 0, Close = 1, Roof = 2, Hood = 3, Bumper = 4, Cockpit = 5, TopDown = 6
        }

        public static readonly string[] ViewNames =
            { "CHASE", "CLOSE CHASE", "ROOF CAM", "HOOD CAM", "BUMPER CAM", "COCKPIT", "TOP DOWN" };

        /// <summary>Short forms, for the touch button — a 120-unit button cannot
        /// hold "BUMPER CAM" and the word CAM is already printed above it.</summary>
        public static readonly string[] ShortNames =
            { "CHASE", "CLOSE", "ROOF", "HOOD", "BUMPER", "COCKPIT", "TOP DOWN" };

        const int ViewCount = 7;
        const string PrefKey = "psx.cameraView";

        /// <summary>
        /// How far a mounted camera stands off the panel it looks over.
        ///
        /// Not a taste number: it is set by the NEAR PLANE. The bonnet enters
        /// the frame at clearance / tan(halfFOV + pitch), which at this game's
        /// widest hood FOV is about 1.3x the clearance — so anything under
        /// <see cref="MountNearClip"/> * 1.3 puts the panel closer to the lens
        /// than the near plane and the camera slices straight through its own
        /// bodywork. That is what "inside the engine bay, showing transparency"
        /// looks like: the near plane opens a hole in the bonnet and the far
        /// side of the shell is backface-culled, so you see the road through
        /// the car.
        /// </summary>
        public const float MountClearance = 0.18f;

        /// <summary>
        /// Near plane while mounted on the car, tightened from the scene
        /// camera's own. A lens a hand's width off the bonnet has bodywork
        /// well inside a 25 cm near plane; the chase views never do, and they
        /// keep the looser plane because that is where depth precision over
        /// 360 m of city actually matters.
        /// </summary>
        public const float MountNearClip = 0.18f;

        /// <summary>
        /// Top-down is a DRAG-STRIP view. On a circuit it is a novelty that
        /// makes the car impossible to place against a corner you cannot see
        /// the entry of; on a strip, where the only question is which car is
        /// ahead, it is the clearest view in the game. So it sits at the end of
        /// the cycle and the cycle only reaches it on a strip.
        /// </summary>
        public static bool TopDownAllowed =>
            RaceManager.Instance != null && RaceManager.Instance.path != null &&
            RaceManager.Instance.path.drag;

        static int CycleLength => TopDownAllowed ? ViewCount : ViewCount - 1;

        /// <summary>The view in use, and when it last changed — the HUD flashes
        /// the name for a moment after a switch, which is the only way a player
        /// discovers there are six of them.</summary>
        public static View Current { get; private set; }
        public static float ChangedAt { get; private set; } = -99f;

        Vector3 smoothPos;
        /// <summary>
        /// The follow rotation BEFORE roll and shake. Kept separately because
        /// both of those are composed onto the transform after the follow
        /// slerp, and a slerp that starts from a rotation which already
        /// carries last frame's roll re-applies it on top: a steady 2 deg of
        /// roll through a 7/s slerp at 60 fps converges on 2 / 0.11 = 18 deg.
        /// The impact shake had the same leak and got away with it only
        /// because noise averages to zero.
        /// </summary>
        Quaternion followRot = Quaternion.identity;
        Camera cam;
        float trauma;
        float shakeSeed;
        float swing;
        float roll;
        float accelSmoothed;
        float prevForward;
        bool haveForward;
        /// <summary>The near plane the scene was built with, so a mounted view
        /// can tighten it and every other view can put it back.</summary>
        float baseNear = 0.25f;

        void Start()
        {
            cam = GetComponent<Camera>();
            if (cam != null) baseNear = cam.nearClipPlane;
            if (target != null) smoothPos = target.position;
            followRot = transform.rotation;
            Active = this;
            shakeSeed = Random.value * 100f;
            // Remembered across races: a player who drives in bumper cam should
            // not have to re-pick it every time they leave the apartment.
            Current = (View)Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, 0), 0, ViewCount - 1);
            if (Current == View.TopDown && !TopDownAllowed) Current = View.Chase;
            // Flash the view name on the grid. Six cameras are worth nothing to
            // a player who never learns there is more than one, and the only
            // moment they are certainly looking at the screen and not at the
            // road is before the lights go out.
            ChangedAt = Time.unscaledTime;
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
        }

        /// <summary>Add impact energy, 0..1. Accumulates so a multi-panel crash
        /// shakes harder than a single tap, then clamps so it cannot run away.</summary>
        public void AddTrauma(float amount)
        {
            trauma = Mathf.Clamp01(trauma + amount);
        }

        public static void SetView(View v)
        {
            v = (View)(((int)v % ViewCount + ViewCount) % ViewCount);
            // A saved top-down from a previous drag race must not follow the
            // player onto a circuit.
            if (v == View.TopDown && !TopDownAllowed) v = View.Chase;
            if (v == Current) return;
            Current = v;
            ChangedAt = Time.unscaledTime;
            PlayerPrefs.SetInt(PrefKey, (int)v);
            // Flushed rather than left to the auto-save. On Web, PlayerPrefs
            // live in IndexedDB and a closed tab is not a clean quit, so the
            // one thing this game remembers between sessions would be the one
            // thing a player loses by closing it the way people close tabs.
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Set the current view WITHOUT saving it, for the screenshot tool.
        ///
        /// That tool photographs all seven views in turn, and two things now
        /// read <see cref="Current"/> rather than being told which view they are
        /// in — the cabin overlay and the instrument binnacle. Driving them
        /// through <see cref="SetView"/> would work and would also leave the
        /// editor's saved camera preference wherever the sweep happened to
        /// stop, which is a side effect a reference-shot pass has no business
        /// having.
        /// </summary>
        public static void PreviewView(View v)
        {
            Current = (View)(((int)v % ViewCount + ViewCount) % ViewCount);
            ChangedAt = Time.unscaledTime;
        }

        public static void CycleView(int step = 1)
        {
            int n = CycleLength;
            int cur = Mathf.Min((int)Current, n - 1);
            SetView((View)(((cur + step) % n + n) % n));
        }

        void Update()
        {
            // The pause menu owns the pad while it is open; cycling the camera
            // from under it would fight the menu's own north/east bindings.
            if (PauseMenu.IsOpen) return;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.cKey.wasPressedThisFrame) CycleView(1);
                // Direct select, the way a PS1 game let you hold a view: 1-6.
                for (int i = 0; i < ViewCount; i++)
                    if (kb[FirstDigit + i].wasPressedThisFrame) SetView((View)i);
            }

            // Y/Triangle, and nothing else on the pad may claim it — see
            // PlayerCarInput, where the car reset used to share this button and
            // every view change came with a free teleport back to the racing
            // line. The reset lives on X/Square and Back/Share now.
            var pad = Gamepad.current;
            if (pad != null && pad.buttonNorth.wasPressedThisFrame) CycleView(1);
        }

        const Key FirstDigit = Key.Digit1;

        BoxCollider targetBox;
        /// <summary>The FD's collider length, which every camera offset here was
        /// picked against.</summary>
        const float ReferenceLengthM = 4.1f;

        BoxCollider Box
        {
            get
            {
                if (targetBox == null && target != null) targetBox = target.GetComponent<BoxCollider>();
                return targetBox;
            }
        }

        CarBody targetBody;
        /// <summary>The shell being driven, for the measurements the collider
        /// cannot carry. Looked up lazily and not cached as the DEF, because
        /// which body the player is in is decided during Start by
        /// RaceHandoffApplier — one phase after this component's own.</summary>
        CarModelDef Shell
        {
            get
            {
                if (targetBody == null && target != null) targetBody = target.GetComponent<CarBody>();
                return targetBody != null ? targetBody.Def : null;
            }
        }

        /// <summary>
        /// How much longer this car is than the FD the camera was framed on.
        /// Read off the collider every frame rather than cached at Start: which
        /// body shell the player is driving is decided during Start by
        /// RaceHandoffApplier, one phase after this component's own, and a
        /// 5.2 m Daytona framed for a 4.1 m FD puts its own rear wing across a
        /// third of the screen.
        ///
        /// Clamped hard. This is framing, not a camera mode — a supermini
        /// should not feel like a different game.
        /// </summary>
        float LengthFit()
        {
            var b = Box;
            if (b == null) return 1f;
            return Mathf.Clamp(b.size.z / ReferenceLengthM, 0.9f, 1.3f);
        }

        void LateUpdate()
        {
            if (target == null) return;
            float speed = targetCar != null ? Mathf.Abs(targetCar.forwardSpeed) : 0f;
            float fit = LengthFit();
            float fov = ViewFOV(Current, baseFOV);

            UpdateAcceleration();

            switch (Current)
            {
                case View.Chase:
                case View.Close:
                    ChaseParams(Current, out float dm, out float hm, out float lm);
                    Follow(distance * fit * dm, height * hm, lookHeight * lm, speed, fit);
                    break;
                case View.Roof:
                case View.Hood:
                case View.Bumper:
                case View.Cockpit:
                    Mount(MountOffset(Current, BoxCenter, BoxSize, Shell), MountPitch(Current));
                    break;
                case View.TopDown:
                    TopDown(speed, fit);
                    break;
            }

            if (cam != null)
            {
                cam.fieldOfView = FOVFor(Current, baseFOV, speed, speedFOV, speedFullMps);
                cam.nearClipPlane = ViewNearClip(Current, baseNear);
            }

            // Composed onto the follow result, never fed back into it.
            ApplyRoll(speed);
            ApplyShake(speed);
        }

        /// <summary>Smoothstep wind-in of the speed rig, 0 at rest to 1 at
        /// <paramref name="fullMps"/>. Static so the self-test can pin the
        /// curve: it must be zero at rest and monotonic.</summary>
        public static float SpeedT(float speedMps, float fullMps) =>
            Smooth01(Mathf.Abs(speedMps) / Mathf.Max(fullMps, 1f));

        /// <summary>A real smoothstep: 0 below <paramref name="a"/>, 1 above
        /// <paramref name="b"/>, 3t^2 - 2t^3 between. (Mathf.SmoothStep is an
        /// interpolator between two VALUES, which is not this.)</summary>
        public static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Forward acceleration, smoothed. Differenced off the car's forward
        /// speed once a frame, so it reads zero on the frames between physics
        /// ticks and double on the frames after; the first-order lag averages
        /// that to the true value and gives the rig its half-second return.
        /// </summary>
        void UpdateAcceleration()
        {
            float dt = Time.deltaTime;
            float fwdV = targetCar != null ? targetCar.forwardSpeed : 0f;
            float raw = haveForward && dt > 0f ? (fwdV - prevForward) / dt : 0f;
            prevForward = fwdV;
            haveForward = true;
            accelSmoothed = Mathf.Lerp(accelSmoothed, raw, 1f - Mathf.Exp(-accelLag * dt));
        }

        void Follow(float dist, float h, float look, float speed, float fit)
        {
            float dt = Time.deltaTime;
            // Flatten forward so the camera doesn't dive with body pitch
            Vector3 fwd = target.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            // ---- the speed rig: back, down, and further up the road --------
            float t = SpeedT(speed, speedFullMps);
            dist += speedPullBack * t * fit;
            h -= speedDrop * t;

            // ---- acceleration-coupled distance: throttle stretches, braking
            // zooms in, bounded so a wall strike cannot throw the lens.
            float limit = accelDistLimitFrac * dist;
            dist += Mathf.Clamp(accelSmoothed * accelDistPerMps2, -limit, limit);

            // ---- drift swing, to the outside of the slide ----------------
            // chassisSlipAngle is positive when the nose points RIGHT of the
            // velocity — tail out to the left, a right-hand drift — and the
            // outside of that corner is the left, so the sign is inverted.
            float slip = targetCar != null
                ? Mathf.Clamp(targetCar.chassisSlipAngle, -DriftSwingClampRad, DriftSwingClampRad)
                : 0f;
            float wantSwing = targetCar != null && targetCar.Drifting ? -slip * driftSwing : 0f;
            swing = Mathf.Lerp(swing, wantSwing, 1f - Mathf.Exp(-driftSwingLag * dt));

            Vector3 wanted = target.position - fwd * dist + Vector3.up * h + right * swing;
            smoothPos = Vector3.Lerp(smoothPos, wanted, 1f - Mathf.Exp(-positionLag * dt));

            // ---- the lag clamp: a launch never leaves the car behind the lens
            // (nor a braking stop in front of it). Only the along-forward part
            // is bounded; sideways and vertical lag stay soft.
            float along = Vector3.Dot(smoothPos - wanted, fwd);
            float bounded = Mathf.Clamp(along, -lagClampM, lagClampM);
            if (bounded != along) smoothPos += fwd * (bounded - along);
            transform.position = smoothPos;

            // ---- WHERE THE LENS POINTS: the heading, blended toward the
            // direction the car is actually TRAVELLING once it is sideways.
            //
            // The aim was locked 100% to the chassis heading, so in a slide the
            // whole world rotated under the player at the car's full yaw rate —
            // the "too aggressive" half of the camera. Racing Game 2 carries
            // this layer (cameraOrientation.ts: heading blended toward a
            // filtered velocity angle by slip, and a SLOWER lerp while
            // drifting, "snapping the camera instantly to the target would make
            // drift look frantic"); it was never ported. Applied to the AIM
            // only, not to where the rig sits — moving the seat as well rotates
            // it the other way round the car and the two cancel.
            float aimSlipT = 0f;
            aimFwd = fwd;
            if (targetCar != null && targetCar.Body != null)
            {
                Vector3 v = targetCar.Body.linearVelocity; v.y = 0f;
                // REVERSING IS NOT A SLIDE, and this is the same trap the drift
                // swing eleven lines up already guards ("so a spin does not
                // orbit the lens round the car"). chassisSlipAngle is the angle
                // between travel and nose, so a car backing out of a space at
                // 3 m/s reads pi: the ratio pins at 1, the blend swings the aim
                // 0.6 of the way toward a heading 180 degrees away, LerpAngle
                // takes whichever arc is shorter and flips sides on any wobble,
                // AND the follow rate drops to its drift setting at the moment
                // the player has least idea where they are. Below, a car whose
                // travel is not broadly forward simply aims down its own nose.
                bool goingForward = Vector3.Dot(v, fwd) > 0f;
                if (v.sqrMagnitude > 4f && goingForward)
                {
                    aimSlipT = Mathf.Clamp01(Mathf.Abs(targetCar.chassisSlipAngle) /
                                             Mathf.Max(aimSlipFullRad, 0.01f));
                    float rawVelYaw = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                    if (!haveVelYaw) { velYawDeg = rawVelYaw; haveVelYaw = true; }
                    else
                    {
                        float filt = Mathf.Lerp(velFilterGrip, velFilterDrift, aimSlipT);
                        velYawDeg = Mathf.LerpAngle(velYawDeg, rawVelYaw,
                                                    1f - Mathf.Exp(-filt * dt));
                    }
                    // And the OFFSET is bounded, not just the ratio. Clamping
                    // the ratio alone leaves it at 1 for anything past
                    // aimSlipFullRad, so a big angle still gets the full blend
                    // of a big angle; bounding the applied degrees is what
                    // keeps the lens behind the car.
                    float headYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
                    float delta = Mathf.Clamp(Mathf.DeltaAngle(headYaw, velYawDeg),
                                              -MaxAimOffsetDeg, MaxAimOffsetDeg);
                    aimFwd = Quaternion.Euler(0f, headYaw + aimVelBlendMax * aimSlipT * delta, 0f)
                             * Vector3.forward;
                }
                else haveVelYaw = false;
            }

            Vector3 lookAt = target.position + Vector3.up * look + aimFwd * (1.5f + speedLookAhead * t);
            Quaternion wantedRot = Quaternion.LookRotation(lookAt - smoothPos, Vector3.up);
            // Slower while sideways, for the reason the reference gives. Reads
            // the same gated aimSlipT, so a spin or a reverse no longer slows
            // the follow to its drift rate.
            float rotRate = Mathf.Lerp(rotationLag, RotationLagDriftOf(this), aimSlipT);
            followRot = Quaternion.Slerp(followRot, wantedRot, 1f - Mathf.Exp(-rotRate * dt));
            transform.rotation = followRot;
        }

        Vector3 aimFwd = Vector3.forward;
        float velYawDeg;
        bool haveVelYaw;

        /// <summary>Drop the travel-direction filter. Called whenever the rig
        /// stops being the thing that aims the camera — a mounted view, a
        /// respawn — so returning to a chase view mid-slide re-seeds on the
        /// heading the car has NOW rather than resuming on one last filtered
        /// several seconds ago.</summary>
        public void ForgetAim() { haveVelYaw = false; aimFwd = Vector3.forward; }

        /// <summary>
        /// <see cref="rotationLagDrift"/>, with a floor.
        ///
        /// A yaw lerp rate of zero is a camera that stops turning altogether
        /// the moment the car goes sideways — a worse bug than the one this
        /// block is fixing, and one that would only ever show up in a drift.
        /// Anything implausibly small means somebody zeroed it by accident.
        /// </summary>
        static float RotationLagDriftOf(ChaseCamera c) =>
            c.rotationLagDrift > 0.5f ? c.rotationLagDrift : DefaultRotationLagDrift;

        /// <summary>Roll bias for a Sh2dow-style latG (see the constants):
        /// 0 below <see cref="LatGStart"/>, <see cref="MaxRollDeg"/> at
        /// <see cref="LatGFull"/>, smoothstepped between. Unsigned.</summary>
        public static float RollDegFor(float latG) =>
            MaxRollDeg * Smooth01((latG - LatGStart) / (LatGFull - LatGStart));

        /// <summary>
        /// Bank the chase camera into the turn. Composed onto the transform
        /// after the follow slerp, never stored back into it — see
        /// <see cref="followRot"/>. A right turn is a negative yaw rate about
        /// +Y, and a negative roll about the camera's forward is clockwise
        /// seen from behind: the lens leans the way the car is going.
        /// </summary>
        void ApplyRoll(float speed)
        {
            float dt = Time.deltaTime;
            bool chase = Current == View.Chase || Current == View.Close;
            float yawRate = chase && targetCar != null && targetCar.Body != null
                ? Vector3.Dot(targetCar.Body.angularVelocity, Vector3.up)
                : 0f;
            float latG = Mathf.Abs(yawRate) * speed * LatGScale;
            float want = chase ? Mathf.Sign(yawRate) * RollDegFor(latG) : 0f;

            float rate = Mathf.Abs(want) > Mathf.Abs(roll) ? RollWindIn : RollUnwind;
            float next = Mathf.Lerp(roll, want, 1f - Mathf.Exp(-rate * dt));
            roll = Mathf.MoveTowards(roll, next, RollSlewDegPerSec * dt);
            if (Mathf.Abs(roll) > 1e-4f)
                transform.rotation *= Quaternion.Euler(0f, 0f, roll);
        }

        /// <summary>
        /// Hard-mount on the car. No lag at all: a mounted camera that lerps is
        /// a camera bolted to the roof with rubber, and the whole reason to pick
        /// one of these views is that the car's rotation IS the picture.
        /// </summary>
        void Mount(Vector3 localOffset, float pitchDeg)
        {
            transform.position = target.TransformPoint(localOffset);
            transform.rotation = target.rotation * Quaternion.Euler(pitchDeg, 0f, 0f);
            smoothPos = transform.position;
            // So a switch back to a chase view slerps from where the lens IS,
            // not from wherever the chase rig last left it a lap ago. The aim
            // filter goes with it, for exactly the same reason.
            followRot = transform.rotation;
            ForgetAim();
        }

        // Every offset below is derived from the body box: centre and size are
        // in car-local metres, with the car's origin on the ground between the
        // wheels and +Z out of the nose.
        Vector3 BoxCenter => Box != null ? Box.center : new Vector3(0f, 0.72f, 0.05f);
        Vector3 BoxSize => Box != null ? Box.size : new Vector3(1.72f, 1.0f, 4.1f);

        /// <summary>
        /// Where a mounted view sits in car-local metres, from the body box's
        /// centre and size. Static and public so the screenshot tool can frame
        /// the REAL offsets rather than a copy of them that drifts — every way
        /// these can be wrong (a lens inside the windscreen, a bumper cam
        /// clipping through its own nose) is visual and silent.
        /// </summary>
        public static Vector3 MountOffset(View v, Vector3 c, Vector3 s, CarModelDef def = null)
        {
            switch (v)
            {
                case View.Roof:
                    // Just above the roofline and a little back of centre, so
                    // the bonnet is in frame and the roof itself is not. The
                    // MEASURED roof where there is one: the collider box is a
                    // shrunk fit, so deriving a roofline from it puts the lens
                    // inside the cabin on anything tall.
                    if (def != null && def.roofY > 0.5f)
                        return new Vector3(0f, def.roofY + MountClearance, c.z - s.z * 0.06f);
                    return new Vector3(0f, c.y + s.y * 0.5f + 0.22f, c.z - s.z * 0.06f);
                case View.Hood:
                    // Just in front of the windscreen and clear of the bonnet,
                    // both MEASURED. A fixed fraction of the car's length gave a
                    // Superbird and a 1970s Civic the same bonnet, which is not
                    // close to true — one has nearly two metres of it and the
                    // other barely half that. CarModelBaker reads the cowl off
                    // the body mesh's top SURFACE; the fraction below is only
                    // the fallback for a car with no baked shell.
                    //
                    // The clearance was 0.10 and that was not enough on any
                    // shell in the pack. It only looked like enough because the
                    // old vertex-binned scan under-reported the cowl by about
                    // that much, so the two errors cancelled to "a camera three
                    // millimetres above the glass" — which is to say, inside it
                    // for the whole of the near plane.
                    if (def != null && def.cowlY > 0.4f && def.noseZ > def.cowlZ
                                    && def.cowlZ > c.z - s.z * 0.5f)
                        return new Vector3(0f, def.cowlY + MountClearance, def.cowlZ + 0.05f);
                    return new Vector3(0f, c.y + s.y * 0.30f, c.z + s.z * 0.32f);
                case View.Cockpit:
                    return CockpitEye(c, s, def);
                default:
                    // Ahead of the nose, not inside it — and the nose is the
                    // MESH's, not the box's. The collider is a 0.955 fit, so its
                    // front face sits a good 10 cm inside the bodywork and a
                    // bumper cam set just ahead of the box lands on the number
                    // plate rather than in front of it.
                    float nose = def != null && def.noseZ > c.z
                        ? def.noseZ
                        : c.z + s.z * 0.5f + 0.11f;
                    return new Vector3(0f, Mathf.Max(0.36f, c.y - s.y * 0.34f), nose + 0.06f);
            }
        }

        /// <summary>
        /// Where the driver's eye goes, in car-local metres.
        ///
        /// Behind the base of the windscreen and above the top of the bonnet —
        /// both MEASURED, for the same reason the hood camera measures them: a
        /// fraction of the car's length puts the driver of a Superbird in the
        /// boot and the driver of a 70s Civic on the bumper. The cabin is
        /// between the cowl and the roof, and this sits the eye two thirds of
        /// the way up that gap, which is where a seat and a head put it.
        ///
        /// Held clear of BOTH by a margin. Level with the roof is a camera
        /// looking through its own headlining; level with the cowl is one
        /// looking along the bonnet from underneath, which is what a bumper cam
        /// is for. On a shell with no measurements at all, the body box gives
        /// the same answer to within a few centimetres.
        ///
        /// Off to the LEFT, because the cars in this game are American and
        /// their drivers sit on the left. The offset is a fraction of the body
        /// WIDTH rather than a constant: a quarter of the car's width from the
        /// centreline is a driver's seat in a supermini and in a Charger alike.
        /// </summary>
        static Vector3 CockpitEye(Vector3 c, Vector3 s, CarModelDef def)
        {
            float floor = c.y - s.y * 0.5f;
            bool measured = def != null && def.cowlY > 0.4f && def.roofY > def.cowlY + 0.25f
                            && def.cowlZ > floor;
            float y = measured
                ? Mathf.Lerp(def.cowlY, def.roofY, 0.62f)
                : c.y + s.y * 0.34f;
            // Never through the headlining, and never below the scuttle.
            if (measured)
                y = Mathf.Clamp(y, def.cowlY + 0.12f, def.roofY - EyeHeadroom);

            // Behind the windscreen base. Clamped INTO the box: on a cab-forward
            // shell the cowl is already near the middle of the car, and half a
            // metre further back would seat the driver over the rear axle.
            float z = (measured ? def.cowlZ : c.z + s.z * 0.16f) - EyeSetback;
            z = Mathf.Max(z, c.z - s.z * 0.35f);

            return new Vector3(-s.x * 0.22f, y, z);
        }

        /// <summary>Clearance between the driver's eye and the roof, metres.</summary>
        const float EyeHeadroom = 0.22f;
        /// <summary>How far behind the base of the windscreen the eye sits.</summary>
        const float EyeSetback = 0.46f;

        /// <summary>
        /// Distance, height and look-height multipliers for the two chase
        /// views, against the default rig. Closer AND lower for the near one:
        /// the point of a close chase is that the car fills more of the frame
        /// and the road comes at you faster, and the FOV opens up to match.
        ///
        /// The camera sits at the car's ORIGIN plus the distance and the tail is
        /// already 2 m behind that origin, so the multiplier is a much bigger
        /// lever than it looks: 0.62 of 5.4 m is 3.35 m back, which is 1.35 m
        /// off the rear bumper.
        ///
        /// This has been round the loop twice. 0.62 was tried, reported as
        /// filling half the screen with nothing of the road left to drive by,
        /// and backed off to 0.75 — but the SAME pass dropped the height to
        /// 0.82, which put the lens level with the roofline, and that is what
        /// was actually blocking the view forward. With the height fixed at
        /// 1.15 (2.07 m, comfortably above the roof) the lens looks down over
        /// the car rather than at the back of it, and 0.75 simply reads as the
        /// normal chase view moved a little. Back to 0.62, height held, because
        /// close is the whole point of this view.
        /// </summary>
        public static void ChaseParams(View v, out float dist, out float height, out float look)
        {
            bool close = v == View.Close;
            dist = close ? 0.62f : 1f;
            height = close ? 1.15f : 1f;
            look = close ? 0.85f : 1f;
        }

        /// <summary>
        /// Near plane per view. Public and static for the same reason
        /// <see cref="MountOffset"/> is: the screenshot tool has to frame these
        /// views exactly as the game does, and a camera that clips its own
        /// bonnet in the build but not in the reference shots is a bug that
        /// gets shipped.
        /// </summary>
        public static float ViewNearClip(View v, float baseNear) =>
            v == View.Roof || v == View.Hood || v == View.Bumper || v == View.Cockpit
                ? Mathf.Min(baseNear, MountNearClip)
                : baseNear;

        /// <summary>Downward tilt per mounted view. Small: these are cameras
        /// bolted to a car, not a director's crane. The cockpit gets the most
        /// of the four, because a driver looks at the road rather than at the
        /// horizon and because the dash takes the bottom of the frame.</summary>
        public static float MountPitch(View v) =>
            v == View.Roof ? 3f : v == View.Cockpit ? 3.5f : v == View.Hood ? 2f : 1f;

        /// <summary>
        /// Field of view per view. The bumper cam is the widest of the six by
        /// ten degrees: a lens a foot off the deck reads as SLOW at 58, because
        /// the speed of that view comes from the edges of the frame moving and
        /// not from the middle. Top-down goes the other way — a wide lens from
        /// 20 m up turns the car into a dot in a bowl.
        /// </summary>
        public static float ViewFOV(View v, float baseFOV)
        {
            switch (v)
            {
                case View.Close: return baseFOV + 4f;
                case View.Hood: return baseFOV + 5f;
                case View.Bumper: return baseFOV + 10f;
                // The cabin overlay eats the bottom third of the frame and both
                // sides, so the cockpit needs a wider lens than the roof cam
                // just to be left with as much ROAD. Not as wide as the bumper:
                // this is a view you steer by, and a fisheye makes a corner
                // arrive at a different rate than the car is travelling.
                case View.Cockpit: return baseFOV + 6f;
                case View.TopDown: return baseFOV - 6f;
                default: return baseFOV;
            }
        }

        /// <summary>Degrees of speed pull per view: the chase pair get the
        /// full amount, the mounted views half (see <see cref="MountSpeedFOV"/>),
        /// and TOP DOWN none — from 20 m up a wider lens is a bowl, not
        /// speed, and that view already climbs with speed instead.</summary>
        public static float ViewSpeedFOV(View v, float chaseSpeedFOV)
        {
            switch (v)
            {
                case View.Chase:
                case View.Close: return chaseSpeedFOV;
                case View.TopDown: return 0f;
                default: return MountSpeedFOV;
            }
        }

        /// <summary>Ceiling per view. The two the near plane cares about are
        /// named; the rest simply cannot be reached by the ramp.</summary>
        public static float ViewMaxFOV(View v)
        {
            switch (v)
            {
                case View.Hood: return HoodMaxFOV;
                case View.Bumper: return BumperMaxFOV;
                default: return 90f;
            }
        }

        /// <summary>The lens at a given speed: the view's rest FOV plus its
        /// share of the pull, smoothstepped in over <paramref name="fullMps"/>,
        /// capped per view. Chase: 58 at rest, 62 at 100 km/h, 66 at 200.</summary>
        public static float FOVFor(View v, float baseFOV, float speedMps,
                                   float chaseSpeedFOV, float fullMps) =>
            Mathf.Min(ViewFOV(v, baseFOV) + ViewSpeedFOV(v, chaseSpeedFOV) * SpeedT(speedMps, fullMps),
                      ViewMaxFOV(v));

        /// <summary>
        /// How far ahead of a mounted lens the panel it looks over crosses the
        /// bottom of the frame, metres: clearance / tan(halfFOV + pitch). The
        /// number the near plane has to stay under — see
        /// <see cref="MountClearance"/> — and the self-test checks it at the
        /// hood camera's WIDEST lens, because that is the frame a bump at
        /// 200 km/h would open a hole in.
        /// </summary>
        public static float PanelEntryM(float fovDeg, float pitchDeg) =>
            MountClearance / Mathf.Tan((fovDeg * 0.5f + pitchDeg) * Mathf.Deg2Rad);

        /// <summary>
        /// Overhead, rotating with the car, climbing with speed. A fixed height
        /// either buries a fast car in the bottom of the frame or makes a slow
        /// one look parked; tying it to speed keeps roughly the same amount of
        /// road ahead in shot whatever the car is doing.
        /// </summary>
        void TopDown(float speed, float fit)
        {
            Vector3 fwd = target.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;

            float h = (14f + Mathf.Clamp01(speed / 60f) * 10f) * fit;
            Vector3 wanted = target.position + Vector3.up * h + fwd * (h * 0.16f);
            smoothPos = Vector3.Lerp(smoothPos, wanted, 1f - Mathf.Exp(-9f * Time.deltaTime));
            transform.position = smoothPos;

            Quaternion wantedRot = Quaternion.LookRotation(Vector3.down, fwd);
            followRot = Quaternion.Slerp(followRot, wantedRot, 1f - Mathf.Exp(-8f * Time.deltaTime));
            transform.rotation = followRot;
        }

        /// <summary>Continuous road-noise amplitude in degrees for a road speed
        /// and surface, with the DEFAULT constants — zero below the start
        /// speed, <see cref="DefaultSpeedShakeDeg"/> (x2.5 off the tarmac) at
        /// the top of the ramp. Static so the self-test can pin that it is
        /// silent at town speed and an order of magnitude under an impact.</summary>
        public static float SpeedShakeDeg(float speedMps, bool onRoad) =>
            DefaultSpeedShakeDeg * (onRoad ? 1f : OffroadShakeMul) *
            Mathf.Clamp01((Mathf.Abs(speedMps) - DefaultSpeedShakeStartMps) / DefaultSpeedShakeSpanMps);

        /// <summary>
        /// Two shakes on one transform. Trauma-squared IMPACT shake: the
        /// response is deliberately non-linear so small scrapes stay subtle
        /// while a real hit is violent. And the continuous SPEED shake, ramped
        /// between two speeds and scaled by surface, at its own lower
        /// frequency. Both driven by Perlin noise rather than Random so the
        /// motion is continuous instead of jittering, and both composed onto
        /// the transform after the follow logic — the follow slerps from its
        /// own stored rotation, so nothing here feeds back into the lag.
        /// </summary>
        void ApplyShake(float speed)
        {
            float st = Mathf.Clamp01((speed - speedShakeStartMps) / Mathf.Max(speedShakeSpanMps, 0.01f));
            bool onRoad = targetCar == null || targetCar.onRoad;
            float surf = onRoad ? 1f : OffroadShakeMul;
            float viewMul = Current == View.TopDown ? 0f
                          : Current == View.Cockpit ? CockpitShakeMul : 1f;
            float speedAng = speedShakeDeg * st * surf * viewMul;
            float speedPos = speedShakePos * st * surf * viewMul;

            bool impact = trauma > 0.0001f;
            if (!impact && speedAng <= 0f) return;

            float ang = 0f, pos = 0f;
            float nx = 0f, ny = 0f, nz = 0f;
            if (impact)
            {
                float s = trauma * trauma;
                float t = Time.time * shakeFrequency + shakeSeed;
                nx += (Mathf.PerlinNoise(t, 0f) * 2f - 1f) * shakeAngleDeg * s;
                ny += (Mathf.PerlinNoise(0f, t) * 2f - 1f) * shakeAngleDeg * s;
                nz += (Mathf.PerlinNoise(t, t) * 2f - 1f) * shakeAngleDeg * s * 1.6f;
                ang = shakeAngleDeg * s;
                pos = shakePosition * s;
            }
            if (speedAng > 0f)
            {
                // A different noise lane (offset seed) at the road-noise
                // frequency, so the two shakes do not phase-lock.
                float t = Time.time * speedShakeHz + shakeSeed + 37f;
                nx += (Mathf.PerlinNoise(t, 0f) * 2f - 1f) * speedAng;
                ny += (Mathf.PerlinNoise(0f, t) * 2f - 1f) * speedAng;
                nz += (Mathf.PerlinNoise(t, t) * 2f - 1f) * speedAng * 0.6f;
                pos += speedPos;
            }

            transform.rotation *= Quaternion.Euler(nx, ny, nz);
            // Positional jitter follows the dominant angular lane, scaled to
            // the summed positional amplitude.
            float denom = Mathf.Max(ang + speedAng, 1e-4f);
            transform.position += transform.right * (nx / denom * pos)
                                + transform.up * (ny / denom * pos);

            trauma = Mathf.Max(0f, trauma - traumaDecay * Time.deltaTime);
        }
    }
}
