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
    /// Cycled with C, gamepad north, or the CAM button on the touch pad — that
    /// button existed and was wired to nothing, so on a phone there was no way
    /// to change view at all. The choice is remembered across races.
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
        public float positionLag = 5f;
        public float rotationLag = 7f;
        public float baseFOV = 58f;
        public float speedFOV = 8f;

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
        Camera cam;
        float trauma;
        float shakeSeed;
        /// <summary>The near plane the scene was built with, so a mounted view
        /// can tighten it and every other view can put it back.</summary>
        float baseNear = 0.25f;

        void Start()
        {
            cam = GetComponent<Camera>();
            if (cam != null) baseNear = cam.nearClipPlane;
            if (target != null) smoothPos = target.position;
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

            // Rising edge, not the flag itself. TouchButton holds
            // PressedThisFrame for TWO frames deliberately, so that a consumer
            // whose Update runs before the event module still sees the press —
            // read raw, one tap would step the camera on twice.
            var touch = TouchControls.Instance;
            bool camDown = touch != null && touch.CameraPressed;
            if (camDown && !camWasDown) CycleView(1);
            camWasDown = camDown;
        }

        bool camWasDown;

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

            switch (Current)
            {
                case View.Chase:
                case View.Close:
                    ChaseParams(Current, out float dm, out float hm, out float lm);
                    Follow(distance * fit * dm, height * hm, lookHeight * lm, speed);
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
                cam.fieldOfView = fov + speedFOV * Mathf.Clamp01(speed / 60f);
                cam.nearClipPlane = ViewNearClip(Current, baseNear);
            }

            ApplyShake();
        }

        void Follow(float dist, float h, float look, float speed)
        {
            // Flatten forward so the camera doesn't dive with body pitch
            Vector3 fwd = target.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;

            Vector3 wanted = target.position - fwd * dist + Vector3.up * h;
            float lag = positionLag + speed * 0.08f;
            smoothPos = Vector3.Lerp(smoothPos, wanted, 1f - Mathf.Exp(-lag * Time.deltaTime));
            transform.position = smoothPos;

            Vector3 lookAt = target.position + Vector3.up * look + fwd * 1.5f;
            Quaternion wantedRot = Quaternion.LookRotation(lookAt - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, wantedRot,
                1f - Mathf.Exp(-rotationLag * Time.deltaTime));
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
        /// 0.75 rather than the 0.62 this started at. The camera sits at the
        /// car's ORIGIN plus the distance, and the tail is already 2 m behind
        /// that origin — at 0.62 the lens ended up 1.3 m off the rear bumper,
        /// with the car filling half the screen and nothing of the road ahead
        /// left to drive by.
        ///
        /// The height went the other way. Dropping it to 0.82 (1.48 m) put the
        /// lens level with the roofline of the car it was following, so the
        /// close view could not see PAST the car — reported as "too low to the
        /// ground making it hard to see in front". Above the roof at 1.15 the
        /// car still fills the frame and the road ahead comes back.
        /// </summary>
        public static void ChaseParams(View v, out float dist, out float height, out float look)
        {
            bool close = v == View.Close;
            dist = close ? 0.75f : 1f;
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
            transform.rotation = Quaternion.Slerp(transform.rotation, wantedRot,
                1f - Mathf.Exp(-8f * Time.deltaTime));
        }

        /// <summary>
        /// Trauma-squared shake: the response is deliberately non-linear so small
        /// scrapes stay subtle while a real hit is violent. Driven by Perlin noise
        /// rather than Random so the motion is continuous instead of jittering,
        /// and applied AFTER the follow logic so it never feeds back into the lag.
        /// </summary>
        void ApplyShake()
        {
            if (trauma <= 0.0001f) return;
            float s = trauma * trauma;
            float t = Time.time * shakeFrequency + shakeSeed;

            float nx = Mathf.PerlinNoise(t, 0f) * 2f - 1f;
            float ny = Mathf.PerlinNoise(0f, t) * 2f - 1f;
            float nz = Mathf.PerlinNoise(t, t) * 2f - 1f;

            transform.rotation *= Quaternion.Euler(nx * shakeAngleDeg * s,
                                                   ny * shakeAngleDeg * s,
                                                   nz * shakeAngleDeg * s * 1.6f);
            transform.position += transform.right * (nx * shakePosition * s)
                                + transform.up * (ny * shakePosition * s);

            trauma = Mathf.Max(0f, trauma - traumaDecay * Time.deltaTime);
        }
    }
}
