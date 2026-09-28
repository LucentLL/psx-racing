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

        // WHERE the two chase views stand is no longer a serialized distance /
        // height / look-height triple. It is fitted per car and per screen from
        // code constants (see "The chase rig" below: ChaseRig, Fit, Shape), so
        // a retune takes effect with a code build — the old triple was baked
        // into 23 scenes, and a baked serialized value outvotes every later
        // change to its initialiser until the scenes are rebuilt. What stays
        // serialized here is the follow DYNAMICS, which the builder stamps
        // from Default* constants.

        /// <summary>Position follow rate, 1/s. This used to be
        /// <c>5 + 0.08 * speed</c>, and the speed term was hiding something:
        /// a first-order lag chasing a target moving at v sits v/lag behind
        /// it in the steady state, so the "5.4 m" chase distance was really
        /// 9.4 m at 100 km/h and 11.3 m at 200 — the car SHRANK in the frame
        /// as it sped up, which is the opposite of a speed cue. The speed term
        /// is gone and the along-forward lag is clamped instead (see
        /// <see cref="lagClampM"/>) — and, since 2026-09-28, LED OUT: the
        /// filter chases a point ahead of the wanted pose by the trail it is
        /// about to settle into, so in steady motion the lens sits exactly
        /// where the rig says. (Before that, the clamp itself was the steady
        /// state above 22 km/h and parked the lens 1.2 m further back than the
        /// framing every screenshot was judged on.)</summary>
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
        /// <summary>The MOUNTED views' lens (roof, hood, bumper, cockpit, top
        /// down are offsets from it — see <see cref="ViewFOV"/>). The two chase
        /// views have their own, per screen shape: <see cref="ChaseRig"/>.</summary>
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
        // The chase views' FOV pull is ChaseRig.speedFOV now (3.5 / 3 deg at
        // 200 km/h). It was 18, then 8 as a serialized field; eight plus the
        // 0.9 m pull-back and the lag clamp shrank the car by a third between
        // rest and 200 km/h, where MW05's measured frames shrink it by 5-9%.
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
        /// <summary>
        /// Bound on how far along its own forward axis the follow lag may sit
        /// off the pose it is chasing, metres. With the speed term gone from
        /// <see cref="positionLag"/> a launch would otherwise pull the car
        /// 11 m out of the frame; 1.2 m is enough to see it surge and not
        /// enough to lose it. The forward axis is the ROAD's (the heading
        /// turned up or down the grade, see <see cref="GradeDeg"/>), so the
        /// steady trail of a climb is led out with the rest. Lateral lag, and
        /// lag across the road's plane (a bump, a kerb), are left free — they
        /// are what makes the rig feel hung on rubber. The steady trail this
        /// clamp used to BE is led out in Follow.
        /// </summary>
        public float lagClampM = 1.2f;

        // ---- the road's grade (2026-09-28) ---------------------------------
        // The chase rig stood on a LEVEL frame: the lens a fixed height over
        // the car, aimed by the level heading. On a hill that is wrong twice.
        // (1) The follow lag's vertical half was never led out: a lens chasing
        // a car that climbs at v x grade trails it by v x grade / positionLag,
        // 0.44 m at 100 km/h on 8% and 0.9 m at 200. Measured on +7%: the FD's
        // CLOSE lens 0.20 m over its roof at 100 km/h (0.29 on the level) and
        // 0.20 m UNDER it at 200, with no road showing over the car at all.
        // (2) Leading that out alone is not enough: a level lens on a -7%
        // descent looks along the level while the road drops 4 deg under it,
        // so the grazing line over the roof met the road 70-110 m out in
        // CHASE and never in CLOSE (11-16 m on the level) — the lag had been
        // hiding this by holding the lens HIGH on descents. So the rig is
        // turned onto the grade: the lens stands behind and above the car in
        // the road's own frame, and on a steady grade shows exactly the level
        // road's picture, turned. The grade is read off the car's own travel
        // (its origin's motion, so it works kinematic — replays, the play
        // check — as well as driven) and low-passed, so a bump or a squat
        // does not tip the lens.
        /// <summary>Low-pass on the travel grade, 1/s, at full speed. Measured
        /// (tools\camframe-play-check.ps1: FD, Supra, euro hatch): a sag into
        /// +8% costs the lens at most 0.005 m of its roof margin at 100 km/h
        /// over 80 m (K 10) and 0.03-0.055 m at 200 km/h over 200 m (K 25), of
        /// 0.22-0.42 m; a steady grade costs nothing. At 3/s a 2 Hz body heave
        /// reaches the rig's angle at under a quarter.</summary>
        public const float GradeFollowRate = 3f;
        /// <summary>The grade the rig follows at most, degrees (17.6%).</summary>
        public const float GradeMaxDeg = 10f;
        /// <summary>The follow fades in between these speeds, m/s: below them
        /// the travel direction is too short a line to read a grade off, and
        /// the rig holds the last one (a car stopped on a hill stays on it).</summary>
        public const float GradeSpeedFrom = 2f, GradeSpeedFull = 8f;
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
        /// 500 m of city actually matters.
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
        /// <summary>The grade the chase rig stands on, degrees, + = the road
        /// rises ahead of the nose. See <see cref="GradeFollowRate"/>.</summary>
        public float GradeDeg => gradeDeg;
        float gradeDeg;
        Vector3 gradePrevPos;
        Transform gradePrevTarget;
        bool haveGradePrev;
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

        /// <summary>Whoever takes the lens next (the replay director, the
        /// walker's head on foot) sets a plain field of view, and a projection
        /// left shifted would silently ignore it.</summary>
        void OnDisable() => ApplyLensShift(0f);

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
            // On a replay the same keys cycle the DIRECTOR's cameras, which
            // include this rig's views; RaceReplay drives them by PreviewView.
            if (RaceReplay.Playing) return;

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
        /// <summary>The FD's collider length, which the top-down height was
        /// picked against.</summary>
        const float ReferenceLengthM = 4.1f;

        /// <summary>The transform the cached collider and body belong to. A
        /// replay re-points <see cref="target"/> at whichever car it is
        /// watching (RaceReplay.SetFocus / SetMode), and before this key the
        /// caches kept the PLAYER's box and shell for every rival — so a rival
        /// in the replay's chase, hood, bumper and cockpit views was framed
        /// and mounted with the player's dimensions.</summary>
        Transform cachedFor;

        void RefreshTargetCache()
        {
            if (cachedFor == target) return;
            cachedFor = target;
            targetBox = null;
            targetBody = null;
        }

        BoxCollider Box
        {
            get
            {
                RefreshTargetCache();
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
                RefreshTargetCache();
                if (targetBody == null && target != null) targetBody = target.GetComponent<CarBody>();
                return targetBody != null ? targetBody.Def : null;
            }
        }

        /// <summary>
        /// How much longer this car is than the FD, for the TOP-DOWN view's
        /// height. (The chase views no longer scale by length: they fit the
        /// car's rear silhouette to its width share instead — see
        /// <see cref="Fit"/>. Scaling the distance by length is what put a
        /// Charger, 11% wider than an FD, at a SMALLER share of the frame.)
        /// Read off the collider every frame: which body shell the player is
        /// driving is decided during Start by RaceHandoffApplier, one phase
        /// after this component's own. Clamped hard.
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
            float aspect = cam != null && cam.aspect > 0.1f ? cam.aspect : RefAspect;
            bool chaseView = Current == View.Chase || Current == View.Close;
            ChaseShape shape = default;

            UpdateAcceleration();
            // Every view, so a switch back to a chase view mid-climb lands on
            // the grade rather than easing onto it.
            UpdateGrade();

            switch (Current)
            {
                case View.Chase:
                case View.Close:
                    shape = Shape(Current, CurrentFit(Current, aspect), SpeedT(speed, speedFullMps), aspect);
                    Follow(shape, speed);
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
                cam.fieldOfView = chaseView ? shape.vfov : FOVFor(Current, baseFOV, speed, speedFullMps);
                cam.nearClipPlane = ViewNearClip(Current, baseNear);
                ApplyLensShift(chaseView ? shape.shift : 0f);
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

        /// <summary>
        /// The grade of the car's travel, low-passed. Read off the target's
        /// own motion frame to frame rather than the body's pitch — the body
        /// sits ~1.3 deg nose-up on its springs and squats and dives with the
        /// pedals, none of which is the road — or the rigidbody's velocity,
        /// which reads zero on a kinematic car (a replay). Held while the car
        /// is airborne (a jump's arc is not a road) and below walking pace;
        /// back to level across a teleport; kept on a change of target.
        /// </summary>
        void UpdateGrade()
        {
            float dt = Time.deltaTime;
            Vector3 p = target.position;
            if (!haveGradePrev || gradePrevTarget != target || dt <= 0f)
            {
                gradePrevPos = p; gradePrevTarget = target; haveGradePrev = true;
                return;
            }
            Vector3 d = p - gradePrevPos;
            gradePrevPos = p;
            float horiz = new Vector2(d.x, d.z).magnitude;
            // A respawn, a replay seek: the grade here is unknown until the
            // car moves, and level is the least wrong guess (a held descent
            // on a level grid would tip the whole picture until it did).
            if (horiz / dt > 150f || Mathf.Abs(d.y) / dt > 50f) { gradeDeg = 0f; return; }
            bool airborne = targetCar != null && targetCar.isActiveAndEnabled && !targetCar.anyWheelGrounded;
            float w = Smooth01((horiz / dt - GradeSpeedFrom) / (GradeSpeedFull - GradeSpeedFrom));
            if (airborne || w <= 0f) return;
            Vector3 fwd = target.forward; fwd.y = 0f;
            // Reversing up a hill is the road FALLING ahead of the nose.
            float sign = Vector3.Dot(new Vector3(d.x, 0f, d.z), fwd) >= 0f ? 1f : -1f;
            float raw = Mathf.Clamp(Mathf.Atan2(d.y * sign, horiz) * Mathf.Rad2Deg, -GradeMaxDeg, GradeMaxDeg);
            gradeDeg = Mathf.Lerp(gradeDeg, raw, 1f - Mathf.Exp(-GradeFollowRate * w * dt));
        }

        /// <summary>The level heading turned up the grade, and the road's up:
        /// the frame the chase rig stands in.</summary>
        public static void GradeFrame(Vector3 flatFwd, float gradeDeg, out Vector3 fwdG, out Vector3 upG)
        {
            float g = gradeDeg * Mathf.Deg2Rad, c = Mathf.Cos(g), s = Mathf.Sin(g);
            fwdG = flatFwd * c + Vector3.up * s;
            upG = Vector3.up * c - flatFwd * s;
        }

        void Follow(ChaseShape shape, float speed)
        {
            float dt = Time.deltaTime;
            // Flatten forward so the camera doesn't dive with body pitch, then
            // turn it onto the ROAD's grade (low-passed travel, not the body):
            // the rig stands in the road's frame. See GradeDeg.
            Vector3 fwd = target.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            GradeFrame(fwd, gradeDeg, out Vector3 fwdG, out Vector3 upG);

            // ---- the fitted pose, speed rig included (see Shape) -----------
            float dist = shape.back;
            float h = shape.height;

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

            Vector3 wanted = target.position - fwdG * dist + upG * h + right * swing;

            // ---- LEAD OUT THE STEADY TRAIL. A first-order lag chasing a
            // point that moves at v settles v / positionLag behind it, and the
            // clamp below pinned that at lagClampM above 6 m/s -- so in every
            // frame anybody drove, the lens was 1.2 m further back than the
            // framing said, and the FD shrank from a fifth of the frame at
            // rest to a seventh at 100 km/h. The filter now chases a point
            // that far AHEAD of the wanted pose, so its steady state IS the
            // wanted pose at any speed; what is left of the lag is the part
            // that means something -- the surge of a launch and the dip of a
            // stop -- on top of the accel coupling above. Ahead along the
            // ROAD: on a climb the car's travel is up the grade, and a lead
            // along the level heading left the rise to trail, v x grade /
            // positionLag under the fit (the CLOSE lens level with the roof).
            float fwdV = targetCar != null ? targetCar.forwardSpeed : 0f;
            float lead = Mathf.Clamp(fwdV / Mathf.Max(positionLag, 0.01f), -lagClampM, lagClampM);
            Vector3 chased = wanted + fwdG * lead;
            smoothPos = Vector3.Lerp(smoothPos, chased, 1f - Mathf.Exp(-positionLag * dt));

            // ---- the lag clamp: a launch never leaves the car behind the lens
            // (nor a braking stop in front of it). Only the part along the
            // road is bounded; sideways and across the road stay soft.
            float along = Vector3.Dot(smoothPos - chased, fwdG);
            float bounded = Mathf.Clamp(along, -lagClampM, lagClampM);
            if (bounded != along) smoothPos += fwdG * (bounded - along);
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

            // The look point is where the fitted PITCH comes from: LookAheadM
            // up the aim line, at the height that makes the steady-state lens
            // pitch exactly shape.pitch — to the ROAD, so both are in its
            // frame. A point near the car rather than a bare angle keeps the
            // car framed while the lateral and vertical lag are out.
            GradeFrame(aimFwd, gradeDeg, out Vector3 aimG, out _);
            Vector3 lookAt = target.position + upG * shape.lookY + aimG * LookAheadM;
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

        // ==================================================================
        //  THE CHASE RIG — NFS Underground / Most Wanted framing (2026-09-28)
        // ==================================================================
        //
        // "Adjust the camera angles to be more like NFS Underground and Most
        // Wanted while maintaining real car and road dimensions." The cars are
        // at their real widths now, so the camera does the work. What NFS
        // holds, measured off MW / UG / UG2 frames (research, 2026-09-28), y
        // from the BOTTOM of the frame at 16:9:
        //
        //            share (1.76 m car)  horizon     rear tyres   pitch    vFOV
        //   CHASE    25% +-1.5           0.53 +-.02  0.11 +-.02   2 +-1    58
        //   CLOSE    34% +-2             0.57 +-.02  0.07 +-.02   4.5 +-1  54
        //
        // with the roof a few percent of the frame under the horizon (the road
        // ahead shows over the car), and at 200 km/h the car 5-9% smaller —
        // never a third. On a phone wider than 16:9 the 16:9 HORIZONTAL field
        // is held and the lens pitches 3 deg further down, so the car keeps its
        // share and its tyres stay on the bottom edge.
        //
        // Nothing here is a baked scene value, so all of it ships with a code
        // build. Per car and per screen shape the rig SOLVES, from the shell's
        // own silhouette (ChaseSilhouettes):
        //   distance  so the car's rear silhouette fills its share x real
        //             width / 1.76 m. One distance for every car does not do
        //             that: a tapered FD reads a fifth narrower per metre than
        //             a Charger's square tail.
        //   height    so the rear tyres sit on their line with the lens at the
        //             target pitch. The ROOF then decides the rest: a lens that
        //             would leave the roof less than gapLo of the frame under
        //             the horizon (no road to drive by) rises, one that would
        //             leave it more than gapHi under (a Viper, sat in a bowl)
        //             comes down — the tyre line absorbing either within
        //             contactTol, then the pitch.
        //   pitch     held at the target (the horizon), moved only by the roof
        //             or by the tail's lowest edge leaving the frame. Past
        //             pitchTol the lens backs off instead — except on a tall
        //             vehicle, which may pitch down up to tallPitch further, as
        //             MW's van and pickup cameras do (ANGLE -6 to -10).
        //   lane      on a touch screen the dials sit either side of the road
        //             (GaugeCluster.TachCircle / SpeedoCircle); the picture is
        //             centred in the lane between them and the lens backs off
        //             until no part of the silhouette is under a dial.
        //   grade     all of the above is solved on a level road and stood in
        //             the ROAD's frame (GradeDeg: the car's travel, low-passed),
        //             so a climb or a descent shows the same picture, turned.
        // FrameProbe (Editor\CamFrameProbe.cs, tools\camframe-probe.ps1)
        // measures every shell in the library on its real meshes and checks
        // it against the bands above. Before this pass the chase view was a
        // fixed 5.4 m / 1.8 m / look at 0.9 m, pitched 7 deg down, and in
        // motion the follow lag and the speed rig parked the lens 2 m further
        // back: the FD was 20% of the frame at rest and 12-14% at speed.

        /// <summary>One chase view's framing, in the terms NFS is measured in.</summary>
        public struct ChaseRig
        {
            /// <summary>Vertical FOV at 16:9 (see <see cref="HoldWide"/>).</summary>
            public float vfov;
            /// <summary>Width share of the frame for a 1.76 m car (an FD),
            /// scaled by the car's real width.</summary>
            public float share;
            /// <summary>Target pitch, degrees down — the horizon — at 16:9 and
            /// at 19.5:9 and wider (MW's lens, 3 deg further down).</summary>
            public float pitch16, pitchWide;
            /// <summary>Rear-tyre contact line, fraction of the frame height
            /// from the BOTTOM, at 16:9 and at 19.5:9 and wider.</summary>
            public float contact16, contactWide;
            /// <summary>How far the contact line may move off its target to
            /// absorb the roof or the tail before the pitch does.</summary>
            public float contactTol;
            /// <summary>How far the pitch may move off its target before the
            /// lens backs off, and how much further a tall vehicle (roof at
            /// <see cref="TallRoofFull"/> and up) may pitch down.</summary>
            public float pitchTol, tallPitch;
            /// <summary>The roof's top line under the horizon, fraction of a
            /// 16:9 frame's height: at least gapLo (the road shows over the
            /// car), at most gapHi (a low car does not sit in a bowl).</summary>
            public float gapLo, gapHi;
            /// <summary>The tail's lowest edge never below this line.</summary>
            public float bottom;
            /// <summary>At 200 km/h: degrees of FOV pull (16:9 terms), metres
            /// back, metres down, and how far the contact line rises.</summary>
            public float speedFOV, speedPull, speedDrop, speedRise;
            /// <summary>Distance range, lens to the rearmost bodywork, metres.</summary>
            public float dMin, dMax;
        }

        /// <summary>CHASE: MW05's "far" camera. The tolerances sit a little
        /// inside the reference bands (horizon 0.51-0.55, tyres 0.09-0.13) so
        /// the measured frame lands inside them.</summary>
        public static readonly ChaseRig FarRig = new ChaseRig
        {
            vfov = 58f, share = 0.25f, pitch16 = 2f, pitchWide = 5f,
            contact16 = 0.11f, contactWide = 0.09f, contactTol = 0.017f,
            pitchTol = 0.9f, tallPitch = 8f, gapLo = 0.07f, gapHi = 0.13f, bottom = 0.015f,
            speedFOV = 3.5f, speedPull = 0.05f, speedDrop = 0.05f, speedRise = 0.02f,
            dMin = 1.6f, dMax = 5f,
        };

        /// <summary>
        /// CLOSE: MW05's close camera — nearer, a longer lens (54 against 58),
        /// pitched 4.5 deg: the car is 34% of the frame with its tyres 7% off
        /// the bottom edge and the horizon at 57%. The owner's history with
        /// this view is in the roof gap: at 0.62 of the old rig with a lens
        /// level with the roof it "filled half the screen with nothing of the
        /// road left to drive by", so the roof keeps 6% of the frame under the
        /// horizon, and the tail's bottom edge may only just leave the frame.
        /// </summary>
        public static readonly ChaseRig CloseRig = new ChaseRig
        {
            vfov = 54f, share = 0.34f, pitch16 = 4.5f, pitchWide = 7.5f,
            contact16 = 0.07f, contactWide = 0.06f, contactTol = 0.017f,
            pitchTol = 0.6f, tallPitch = 8f, gapLo = 0.06f, gapHi = 0.15f, bottom = -0.035f,
            speedFOV = 3f, speedPull = 0.03f, speedDrop = 0.05f, speedRise = 0.02f,
            dMin = 1.4f, dMax = 4.5f,
        };

        public static ChaseRig RigFor(View v) => v == View.Close ? CloseRig : FarRig;

        public const float RefAspect = 16f / 9f;
        /// <summary>The owner's phone, 2000x923.</summary>
        public const float PhoneAspect = 19.5f / 9f;
        /// <summary>The FD's real width, which the shares are quoted at.</summary>
        public const float RefWidthM = 1.76f;
        /// <summary>A roof line this high starts the tall-vehicle pitch
        /// allowance, and this high has all of it (the reference's "tall":
        /// over 1.45 m; MW's vans are ~2 m).</summary>
        public const float TallRoofFrom = 1.45f, TallRoofFull = 1.95f;
        /// <summary>How far up the aim line the look point sits, metres.</summary>
        public const float LookAheadM = 1.5f;
        /// <summary>Clearance kept between the silhouette and a dial, as a
        /// fraction of the frame width.</summary>
        public const float LaneMargin = 0.008f;
        /// <summary>The lane never takes the car below this fraction of its
        /// CHASE share: a 16:9 tablet's touch layout leaves only ~15% of the
        /// width between its dials, and a car that small is the old camera.</summary>
        public const float LaneFloor = 0.85f;
        /// <summary>The back-off step, metres.</summary>
        const float FitStep = 0.02f;
        /// <summary>CarModelBaker.ColliderFit: the collider is this much of
        /// the body mesh's bounds (x, y, z).</summary>
        const float BoxFitX = 0.915f, BoxFitY = 0.90f, BoxFitZ = 0.955f;

        /// <summary>What the rig needs to know about a car, car-local metres.</summary>
        public struct CarFrame
        {
            public string key;
            /// <summary>Rearmost bodywork and the rear tyres' contact (z), the
            /// roof (y), the real width.</summary>
            public float tailZ, rearAxleZ, roofY, widthM;
            /// <summary>The rear silhouette: REAL x (signed, + = right),
            /// height, metres forward of the tail.</summary>
            public Vector3[] sil;
            /// <summary>The roof line and the tail's lowest edge off the side
            /// profile: (height, metres forward of the tail).</summary>
            public Vector2[] roof, low;
        }

        /// <summary>The frame of a car standing in the scene, from its own
        /// collider, shell and chassis — what the game reads, so what the
        /// tools must read too.</summary>
        public static CarFrame FrameOf(GameObject car)
        {
            var box = car.GetComponent<BoxCollider>();
            var body = car.GetComponent<CarBody>();
            var cc = car.GetComponent<CarController>();
            return FrameOf(box != null ? box.center : new Vector3(0f, 0.72f, 0.05f),
                           box != null ? box.size : new Vector3(1.72f, 1.0f, 4.1f),
                           body != null ? body.Def : null,
                           cc != null ? cc.wheelbase : 2.425f,
                           body != null ? body.WidthScale : 1f,
                           body != null ? body.widthMm : 0);
        }

        public static CarFrame FrameOf(Vector3 c, Vector3 s, CarModelDef def, float wheelbase,
                                       float widthScale, int widthMm)
        {
            var f = new CarFrame { key = def != null ? def.key : null };
            f.rearAxleZ = -Mathf.Max(wheelbase, 1f) * 0.5f;
            f.roofY = def != null && def.roofY > 0.5f ? def.roofY : c.y + s.y * 0.5f / BoxFitY;
            float mm = widthMm;
            if (mm <= 0f && def != null)
            {
                var m = CarModelLibrary.Get(def.key);
                if (m != null) mm = m.widthMm;
            }
            f.widthM = mm > 0f ? mm / 1000f : s.x / BoxFitX;
            if (def != null && ChaseSilhouettes.TryGet(def.key, out var e) && e.Count > 0
                && e.RoofCount > 0 && e.LowCount > 0)
            {
                f.tailZ = e.tailZ;
                f.sil = new Vector3[e.Count];
                for (int i = 0; i < e.Count; i++)
                {
                    var q = e.Point(i);
                    f.sil[i] = new Vector3(q.x * widthScale, q.y, q.z);
                }
                f.roof = new Vector2[e.RoofCount];
                for (int i = 0; i < e.RoofCount; i++) f.roof[i] = e.Roof(i);
                f.low = new Vector2[e.LowCount];
                for (int i = 0; i < e.LowCount; i++) f.low[i] = e.Low(i);
            }
            else
            {
                // No row: a box-shaped car, full width low down at the tail and
                // the glasshouse narrower and further forward, a roof over the
                // middle of the box and a windscreen header ahead of it. The
                // box already carries the across-scale.
                f.tailZ = c.z - s.z * 0.5f / BoxFitZ;
                float half = s.x * 0.5f / BoxFitX, roof = f.roofY, len = s.z / BoxFitZ;
                float lowY = Mathf.Max(0.15f, c.y - s.y * 0.5f / BoxFitY);
                var right = new[]
                {
                    new Vector3(half * 0.95f, roof * 0.30f, 0.05f), new Vector3(half * 0.97f, roof * 0.50f, 0.15f),
                    new Vector3(half * 0.85f, roof * 0.75f, 0.40f), new Vector3(half * 0.65f, roof * 0.97f, 1.00f),
                };
                f.sil = new Vector3[right.Length * 2];
                for (int i = 0; i < right.Length; i++)
                {
                    f.sil[i * 2] = right[i];
                    f.sil[i * 2 + 1] = new Vector3(-right[i].x, right[i].y, right[i].z);
                }
                f.roof = new[] { new Vector2(roof, 0.9f), new Vector2(roof, len * 0.5f), new Vector2(roof * 0.68f, len - 0.3f) };
                f.low = new[] { new Vector2(lowY, 0.05f) };
            }
            return f;
        }

        /// <summary>The driving HUD's two round dials, as fractions of the
        /// screen: centre x, centre y (from the bottom-left), x radius, y
        /// radius. A zero radius is no dial.</summary>
        public struct HudDials { public Vector4 left, right; }

        /// <summary>What GaugeCluster laid out last, left one first.</summary>
        public static HudDials LiveDials()
        {
            Vector4 a = GaugeCluster.TachCircle, b = GaugeCluster.SpeedoCircle;
            return a.x <= b.x ? new HudDials { left = a, right = b } : new HudDials { left = b, right = a };
        }

        static bool HasDials(HudDials d) => d.left.z > 0f && d.right.z > 0f;

        /// <summary>
        /// How far to slide the picture sideways (fraction of the frame width,
        /// + = right) so its centre is the middle of the lane between the two
        /// dials. Zero on a PC, whose dials sit symmetrically in the corners;
        /// about +0.04 on a phone, where the touch wheel is wider than the
        /// pedal column and the dials sit hard against each. A lens shift, not
        /// a turn: the view still looks straight down the road.
        /// </summary>
        public static float LaneShift(HudDials d)
        {
            if (!HasDials(d)) return 0f;
            float inL = d.left.x + d.left.z, inR = d.right.x - d.right.z;
            if (inR <= inL) return 0f;
            return Mathf.Clamp((inL + inR) * 0.5f - 0.5f, -0.1f, 0.1f);
        }

        /// <summary>The per-car, per-screen part of the rig, solved when the
        /// car, the view, the screen or the HUD changes.</summary>
        public struct ChaseFit
        {
            /// <summary>Lens to the rearmost bodywork at rest, horizontal m.</summary>
            public float D;
            /// <summary>Lens height and pitch at rest.</summary>
            public float h0, pitch0;
            /// <summary>Lens shift, and the contact line the rest pose holds
            /// (the speed rig raises it from there).</summary>
            public float shift, contact0;
            public float tailZ, overhang;
            /// <summary>The car it was solved for (the speed rig re-reads the
            /// roof line).</summary>
            public CarFrame car;
        }

        /// <summary>The field of view that keeps the 16:9 HORIZONTAL field on
        /// a screen wider than 16:9 (and the vertical one on anything
        /// narrower, Hor+). Unity's fieldOfView is vertical, so on a 19.5:9
        /// phone a fixed 58 widens the horizontal field from 89 to 100 deg and
        /// shrinks the car by a fifth.</summary>
        public static float HoldWide(float vfov16, float aspect) =>
            aspect <= RefAspect ? vfov16
                : 2f * Mathf.Atan(Mathf.Tan(vfov16 * 0.5f * Mathf.Deg2Rad) * RefAspect / aspect) * Mathf.Rad2Deg;

        /// <summary>The target pitch (degrees down) of a chase view on a
        /// screen: pitch16 at 16:9 and narrower, pitchWide at 19.5:9 and wider.</summary>
        public static float TargetPitch(View v, float aspect)
        {
            var q = RigFor(v);
            return Mathf.Lerp(q.pitch16, q.pitchWide, WideT(aspect));
        }

        static float WideT(float aspect) => Mathf.Clamp01((aspect - RefAspect) / (PhoneAspect - RefAspect));

        /// <summary>A car-local point seen from a lens D behind the tail, h up,
        /// pitched down: its x in viewport widths from the picture's centre
        /// (signed), and its viewport y.</summary>
        static Vector2 ProjectSil(Vector3 p, float D, float h, float pitchDeg, float vfov, float aspect)
        {
            float a = pitchDeg * Mathf.Deg2Rad;
            float y = p.y - h, z = D + p.z;
            float zc = z * Mathf.Cos(a) - y * Mathf.Sin(a), yc = y * Mathf.Cos(a) + z * Mathf.Sin(a);
            if (zc < 0.05f) return new Vector2(p.x >= 0f ? 10f : -10f, 0f);
            float tn = Mathf.Tan(vfov * 0.5f * Mathf.Deg2Rad);
            return new Vector2(p.x / (zc * tn * aspect) * 0.5f, 0.5f + 0.5f * yc / (zc * tn));
        }

        /// <summary>The pitch that puts a point <paramref name="depth"/> ahead
        /// of the lens and <paramref name="py"/> up on viewport line
        /// <paramref name="y"/>.</summary>
        static float PitchToLine(float y, float h, float depth, float py, float vfov) =>
            (Mathf.Atan2(h - py, depth) - Mathf.Atan((1f - 2f * y) * Mathf.Tan(vfov * 0.5f * Mathf.Deg2Rad)))
            * Mathf.Rad2Deg;

        /// <summary>Degrees below the lens axis of viewport line y.</summary>
        static float LineAngle(float y, float tanHalf) => Mathf.Atan((1f - 2f * y) * tanHalf) * Mathf.Rad2Deg;

        /// <summary>The lens height at which the roof line's top sits
        /// <paramref name="gap"/> of a 16:9 frame under the horizon, with the
        /// lens pitched <paramref name="pitch"/> and D off the tail.</summary>
        static float RoofHeight(Vector2[] roof, float D, float pitch, float gap, float tan16)
        {
            float tp = Mathf.Tan(pitch * Mathf.Deg2Rad);
            float beta = pitch - Mathf.Atan(tp - 2f * tan16 * gap) * Mathf.Rad2Deg;
            float tb = Mathf.Tan(beta * Mathf.Deg2Rad), h = float.MinValue;
            foreach (var r in roof) h = Mathf.Max(h, r.x + (D + r.y) * tb);
            return h;
        }

        /// <summary>The least pitch that keeps the tail's lowest edge on or
        /// above the rig's bottom line.</summary>
        static float BottomPitch(ChaseRig q, Vector2[] low, float D, float h, float vfov)
        {
            float p = float.MinValue;
            foreach (var l in low) p = Mathf.Max(p, PitchToLine(q.bottom, h, D + l.y, l.x, vfov));
            return p;
        }

        /// <summary>The highest lens, at a pitch, that keeps the tail's
        /// lowest edge on or above the bottom line.</summary>
        static float BottomHeight(ChaseRig q, Vector2[] low, float D, float pitch, float tanHalf)
        {
            float a = Mathf.Tan((pitch + LineAngle(q.bottom, tanHalf)) * Mathf.Deg2Rad), h = float.MaxValue;
            foreach (var l in low) h = Mathf.Min(h, l.x + (D + l.y) * a);
            return h;
        }

        /// <summary>
        /// The lens height and pitch at a distance: the targets (pitch
        /// <paramref name="p0"/>, contact <paramref name="c0"/>) where the car
        /// allows, and otherwise the cascade — the roof gap and the tail's
        /// lowest edge move the lens, the contact line absorbs that within
        /// contactTol, then the pitch. Returns the contact line it ends on.
        /// </summary>
        static void SolvePose(ChaseRig q, CarFrame car, float overhang, float D, float vfov, float c0, float p0,
                              out float h, out float pitch, out float contact)
        {
            float dc = D + overhang;
            float t = Mathf.Tan(vfov * 0.5f * Mathf.Deg2Rad), t16 = Mathf.Tan(q.vfov * 0.5f * Mathf.Deg2Rad);
            float Hc(float c, float pp) => dc * Mathf.Tan((pp + LineAngle(c, t)) * Mathf.Deg2Rad);
            float p = p0;
            h = Hc(c0, p0);
            for (int it = 0; it < 6; it++)
            {
                float hLo = RoofHeight(car.roof, D, p, q.gapLo, t16);
                float hHi = RoofHeight(car.roof, D, p, q.gapHi, t16);
                p = p0;
                h = Hc(c0, p);
                if (h > hHi)
                {
                    // A low roof: the lens comes down, the tyres rising up
                    // their band, then the lens levels off a little.
                    h = Mathf.Max(hHi, Hc(c0 + q.contactTol, p));
                    if (h > hHi + 1e-4f)
                    {
                        p = Mathf.Max(p0 - q.pitchTol,
                                      Mathf.Atan2(hHi, dc) * Mathf.Rad2Deg - LineAngle(c0 + q.contactTol, t));
                        h = Mathf.Max(hHi, Hc(c0 + q.contactTol, p));
                    }
                }
                if (h < hLo)
                {
                    // A high roof: the lens goes up, the tyres dropping down
                    // their band, then the lens pitches down (a van).
                    h = hLo;
                    p = Mathf.Max(p, Mathf.Atan2(h, dc) * Mathf.Rad2Deg - LineAngle(c0 - q.contactTol, t));
                }
                float hB = BottomHeight(q, car.low, D, p, t);
                if (h > hB)
                {
                    // The tail's lowest edge leaves the frame: the lens comes
                    // down (never under the roof line), then pitches down.
                    h = Mathf.Max(hB, Mathf.Max(Hc(c0 + q.contactTol, p), hLo));
                    p = Mathf.Max(p, BottomPitch(q, car.low, D, h, vfov));
                }
            }
            pitch = p;
            contact = 0.5f - 0.5f * Mathf.Tan((Mathf.Atan2(h, dc) * Mathf.Rad2Deg - p) * Mathf.Deg2Rad) / t;
        }

        static float WidthAt(CarFrame car, float D, float h, float pitch, float vfov, float aspect)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var pt in car.sil)
            {
                float x = ProjectSil(pt, D, h, pitch, vfov, aspect).x;
                lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x);
            }
            return hi > lo ? hi - lo : 0f;
        }

        /// <summary>Whether a projected point (x offset from the picture's
        /// centre <paramref name="cx"/>) keeps its margin off a dial: the left
        /// dial for a point left of centre, the right one otherwise.</summary>
        static bool ClearOfDial(Vector4 c, bool left, float x, float y)
        {
            if (c.z <= 0f || c.w <= 0f) return true;
            float dy = (y - c.y) / c.w;
            if (Mathf.Abs(dy) >= 1f) return true;
            float half = c.z * Mathf.Sqrt(1f - dy * dy);
            return left ? x >= c.x + half + LaneMargin : x <= c.x - half - LaneMargin;
        }

        static bool LaneClear(CarFrame car, float D, float h, float pitch, float vfov, float aspect, float shift, HudDials d)
        {
            float cx = 0.5f + shift;
            foreach (var pt in car.sil)
            {
                var o = ProjectSil(pt, D, h, pitch, vfov, aspect);
                bool left = o.x < 0f;
                if (!ClearOfDial(left ? d.left : d.right, left, cx + o.x, o.y)) return false;
            }
            return true;
        }

        /// <summary>Solve a view's distance, height, pitch and lens shift for
        /// a car on a screen. Static and public: the framing probe and every
        /// screenshot tool frame through exactly this.</summary>
        public static ChaseFit Fit(View v, float aspect, CarFrame car, HudDials dials)
        {
            var q = RigFor(v);
            float k = WideT(aspect);
            float vfov0 = HoldWide(q.vfov, aspect);
            float c0 = Mathf.Lerp(q.contact16, q.contactWide, k);
            float p0 = Mathf.Lerp(q.pitch16, q.pitchWide, k);
            // Narrower than 16:9 (a 4:3 tablet) the lens is the 16:9 one
            // (Hor+), so the car keeps its size against the frame's HEIGHT
            // and takes a bigger share of the narrower width.
            float narrow = aspect < RefAspect ? RefAspect / Mathf.Max(aspect, 0.5f) : 1f;
            float share = q.share * car.widthM / RefWidthM * narrow;
            float floor = LaneFloor * FarRig.share * car.widthM / RefWidthM * narrow;
            if (car.sil == null || car.sil.Length == 0 || car.roof == null || car.roof.Length == 0
                || car.low == null || car.low.Length == 0)
                car = FrameOf(new Vector3(0f, 0.72f, 0.05f), new Vector3(1.72f, 1.0f, 4.1f), null,
                              Mathf.Max(-car.rearAxleZ * 2f, 1f), 1f, Mathf.RoundToInt(car.widthM * 1000f));
            float overhang = car.rearAxleZ - car.tailZ;
            float shift = LaneShift(dials);
            float roofTop = float.MinValue;
            foreach (var r in car.roof) roofTop = Mathf.Max(roofTop, r.x);
            float tall = Mathf.Clamp01((roofTop - TallRoofFrom) / (TallRoofFull - TallRoofFrom));
            float cap = p0 + q.pitchTol + (q.tallPitch - q.pitchTol) * tall;

            float h = 0f, p = 0f, c = 0f;
            float WidthOf(float d)
            {
                SolvePose(q, car, overhang, d, vfov0, c0, p0, out float wh, out float wp, out _);
                return WidthAt(car, d, wh, wp, vfov0, aspect);
            }
            // The share: a bisection, since the width falls as the lens backs off.
            float lo = q.dMin, hi = q.dMax, D;
            if (WidthOf(lo) <= share) D = lo;
            else if (WidthOf(hi) >= share) D = hi;
            else
            {
                for (int i = 0; i < 30; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (WidthOf(mid) > share) lo = mid; else hi = mid;
                }
                D = (lo + hi) * 0.5f;
            }
            // A pose the car cannot be framed in at this distance — pitched past
            // its cap, or the tyres pushed above their band by the tail — backs off.
            for (int i = 0; i < 200 && D < q.dMax; i++)
            {
                SolvePose(q, car, overhang, D, vfov0, c0, p0, out h, out p, out c);
                if (p <= cap + 1e-4f && c <= c0 + q.contactTol + 1e-4f) break;
                D += FitStep;
            }
            // And on a touch screen, off the dials.
            if (HasDials(dials))
                for (int i = 0; i < 200 && D < q.dMax && WidthOf(D) > floor; i++)
                {
                    SolvePose(q, car, overhang, D, vfov0, c0, p0, out h, out p, out c);
                    if (LaneClear(car, D, h, p, vfov0, aspect, shift, dials)) break;
                    D += FitStep;
                }

            SolvePose(q, car, overhang, D, vfov0, c0, p0, out h, out p, out c);
            return new ChaseFit
            {
                D = D, h0 = h, pitch0 = p, shift = shift, contact0 = c,
                tailZ = car.tailZ, overhang = overhang, car = car,
            };
        }

        /// <summary>A chase view's pose at a speed, from its fit.</summary>
        public struct ChaseShape
        {
            /// <summary>Lens behind the car's ORIGIN, horizontal metres.</summary>
            public float back;
            public float height, pitch, vfov, shift;
            /// <summary>Height of the look point <see cref="LookAheadM"/> up
            /// the aim line that gives <see cref="pitch"/> in the steady state.</summary>
            public float lookY;
        }

        /// <summary>The speed rig on top of the fit: a wider lens, a few
        /// centimetres back and down (never so far down the roof line closes
        /// on the horizon), and the tyre line rising a little as the lens
        /// levels — MW's squat, not the old third-of-the-car pull-back.</summary>
        public static ChaseShape Shape(View v, ChaseFit f, float speedT, float aspect)
        {
            var q = RigFor(v);
            float D = f.D + q.speedPull * speedT;
            float vfov = HoldWide(q.vfov + q.speedFOV * speedT, aspect);
            float t16 = Mathf.Tan(q.vfov * 0.5f * Mathf.Deg2Rad);
            float hDrop = f.h0 - q.speedDrop * speedT;
            float h = hDrop, pitch = f.pitch0;
            bool hasRoof = f.car.roof != null && f.car.roof.Length > 0, hasLow = f.car.low != null && f.car.low.Length > 0;
            for (int i = 0; i < 3; i++)
            {
                h = hasRoof ? Mathf.Max(hDrop, Mathf.Min(f.h0, RoofHeight(f.car.roof, D, pitch, q.gapLo, t16))) : hDrop;
                pitch = PitchToLine(f.contact0 + q.speedRise * speedT, h, D + f.overhang, 0f, vfov);
                if (hasLow) pitch = Mathf.Max(pitch, BottomPitch(q, f.car.low, D, h, vfov));
            }
            var s = new ChaseShape { back = D - f.tailZ, height = h, pitch = pitch, vfov = vfov, shift = f.shift };
            s.lookY = h - Mathf.Tan(pitch * Mathf.Deg2Rad) * (s.back + LookAheadM);
            return s;
        }

        /// <summary>
        /// Where a chase view holds the lens in the STEADY STATE — steady
        /// speed on a straight of constant grade <paramref name="gradeDeg"/>
        /// (+ = rising ahead), no slide: the follow lag led out, the accel
        /// term, swing and roll at zero. What Follow converges on, for the
        /// tools that cannot run it (Follow lerps by Time.deltaTime, which is
        /// zero outside play mode). On a grade it is the level pose turned
        /// about the car onto the grade. <paramref name="shift"/> is the lens
        /// shift to apply with <see cref="ShiftedProjection"/>.
        /// </summary>
        public static void SteadyPose(View v, float aspect, float speedMps, float fullMps, Transform car,
                                      CarFrame frame, HudDials dials,
                                      out Vector3 pos, out Quaternion rot, out float vfov, out float shift,
                                      float gradeDeg = 0f)
        {
            var s = Shape(v, Fit(v, aspect, frame, dials), SpeedT(speedMps, fullMps), aspect);
            PoseOf(s, car, out pos, out rot, gradeDeg);
            vfov = s.vfov;
            shift = s.shift;
        }

        /// <summary>A shape stood behind a car on a road of grade
        /// <paramref name="gradeDeg"/>: the lens and where it looks. The same
        /// frame as Follow's.</summary>
        public static void PoseOf(ChaseShape s, Transform car, out Vector3 pos, out Quaternion rot, float gradeDeg = 0f)
        {
            Vector3 fwd = car.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
            GradeFrame(fwd, gradeDeg, out Vector3 fwdG, out Vector3 upG);
            pos = car.position - fwdG * s.back + upG * s.height;
            Vector3 look = car.position + upG * s.lookY + fwdG * LookAheadM;
            rot = Quaternion.LookRotation(look - pos, Vector3.up);
        }

        /// <summary>A camera's projection with the picture slid right by
        /// <paramref name="shift"/> of its width (Unity's projection is
        /// OpenGL-style: m02 offsets x in clip space by -m02 per unit of w).</summary>
        public static Matrix4x4 ShiftedProjection(float vfov, float aspect, float near, float far, float shift)
        {
            var m = Matrix4x4.Perspective(vfov, aspect, near, far);
            m.m02 = -2f * shift;
            return m;
        }

        // ---- the live rig's cache ------------------------------------------
        ChaseFit liveFit;
        View liveFitView = (View)(-1);
        float liveFitAspect, liveFitSx, liveFitWheelbase;
        int liveFitWidthMm;
        string liveFitKey;
        Vector3 liveFitBoxC, liveFitBoxS;
        HudDials liveFitDials;
        Transform liveFitTarget;

        /// <summary>The fit for the car being followed, re-solved only when
        /// something it depends on changes: which car (a body swap during
        /// Start, a replay's retarget), the view, the screen, the HUD.</summary>
        ChaseFit CurrentFit(View v, float aspect)
        {
            var box = Box;
            var def = Shell;
            var body = targetBody;
            float sx = body != null ? body.WidthScale : 1f;
            int mm = body != null ? body.widthMm : 0;
            float wb = targetCar != null ? targetCar.wheelbase : 2.425f;
            Vector3 bc = box != null ? box.center : Vector3.zero, bs = box != null ? box.size : Vector3.zero;
            var dials = LiveDials();
            string key = def != null ? def.key : null;
            if (v == liveFitView && target == liveFitTarget && Mathf.Abs(aspect - liveFitAspect) < 1e-4f
                && key == liveFitKey && sx == liveFitSx && mm == liveFitWidthMm && wb == liveFitWheelbase
                && bc == liveFitBoxC && bs == liveFitBoxS
                && dials.left == liveFitDials.left && dials.right == liveFitDials.right)
                return liveFit;
            liveFitView = v; liveFitTarget = target; liveFitAspect = aspect; liveFitKey = key;
            liveFitSx = sx; liveFitWidthMm = mm; liveFitWheelbase = wb; liveFitBoxC = bc; liveFitBoxS = bs;
            liveFitDials = dials;
            var frame = box != null
                ? FrameOf(bc, bs, def, wb, sx, mm)
                : FrameOf(new Vector3(0f, 0.72f, 0.05f), new Vector3(1.72f, 1.0f, 4.1f), def, wb, sx, mm);
            liveFit = Fit(v, aspect, frame, dials);
            return liveFit;
        }

        bool lensShifted;

        /// <summary>Slide the picture, or put it back. Setting a projection
        /// matrix stops Unity rebuilding it from fieldOfView until
        /// ResetProjectionMatrix, so the matrix is rebuilt here from this
        /// frame's lens every frame it is in use, and reset the moment it is
        /// not (a mounted view, the replay director, the walker).</summary>
        void ApplyLensShift(float shift)
        {
            if (cam == null) return;
            if (Mathf.Abs(shift) < 1e-4f)
            {
                if (lensShifted) { cam.ResetProjectionMatrix(); lensShifted = false; }
                return;
            }
            cam.projectionMatrix = ShiftedProjection(cam.fieldOfView, cam.aspect, cam.nearClipPlane,
                                                     cam.farClipPlane, shift);
            lensShifted = true;
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
                // The chase pair have their own lenses, at 16:9 (see FOVFor
                // for a screen of another shape).
                case View.Chase: return FarRig.vfov;
                case View.Close: return CloseRig.vfov;
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

        /// <summary>Degrees of speed pull per view: the chase pair their
        /// rig's (in 16:9 terms), the mounted views <see cref="MountSpeedFOV"/>,
        /// and TOP DOWN none — from 20 m up a wider lens is a bowl, not
        /// speed, and that view already climbs with speed instead.</summary>
        public static float ViewSpeedFOV(View v)
        {
            switch (v)
            {
                case View.Chase: return FarRig.speedFOV;
                case View.Close: return CloseRig.speedFOV;
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

        /// <summary>The lens at a given speed on a screen of
        /// <paramref name="aspect"/>: the view's rest FOV plus its share of the
        /// pull, smoothstepped in over <paramref name="fullMps"/>, capped per
        /// view. Chase at 16:9: 58 at rest, 59.75 at 100 km/h, 61.5 at 200;
        /// on a 19.5:9 phone the same HORIZONTAL fields (48.9 vertical at
        /// rest). The mounted views keep a fixed vertical field on any screen.</summary>
        public static float FOVFor(View v, float baseFOV, float speedMps, float fullMps,
                                   float aspect = RefAspect)
        {
            float t = SpeedT(speedMps, fullMps);
            if (v == View.Chase || v == View.Close)
            {
                var q = RigFor(v);
                return HoldWide(q.vfov + q.speedFOV * t, aspect);
            }
            return Mathf.Min(ViewFOV(v, baseFOV) + ViewSpeedFOV(v) * t, ViewMaxFOV(v));
        }

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
