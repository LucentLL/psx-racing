using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PSXRacing
{
    /// <summary>
    /// The SPEED BLUR option, remembered across sessions.
    ///
    /// Same shape as <see cref="LookPrefs"/> and <see cref="PSXQuality"/>: a
    /// lazily-read PlayerPrefs int with an eager Save, because on WebGL a
    /// preference that is not flushed is a preference lost to the next tab
    /// close. Ships ON — the owner asked for it by name — and it is a switch
    /// because turning it off also takes the HUD back off its own camera
    /// (see <see cref="SpeedBlur"/>), so a player whose device dislikes any
    /// part of this has one row that puts the old picture back.
    ///
    /// Since 2026-09-21 that row shares the HUD camera with LENS FX
    /// (<see cref="LensFx"/>): the rain drops and the dirt bokeh are drawn
    /// into the same framebuffer after the blur, and would refract the lap
    /// counter just as the blur would smear it. So the HUD comes back onto
    /// the world camera only when BOTH are off — SPEED BLUR: OFF alone now
    /// stops the smear and leaves the split to the lens.
    /// </summary>
    public static class SpeedBlurPrefs
    {
        const string PrefKey = "psx.speedBlur";

        static int cached = -1;

        public static bool Enabled
        {
            get
            {
                if (cached < 0) cached = PlayerPrefs.GetInt(PrefKey, 1);
                return cached != 0;
            }
            set
            {
                int v = value ? 1 : 0;
                if (cached == v) return;
                cached = v;
                PlayerPrefs.SetInt(PrefKey, v);
                PlayerPrefs.Save();
            }
        }

        public static void Toggle() => Enabled = !Enabled;

        public static string Label => Enabled ? "ON" : "OFF";
    }

    /// <summary>
    /// THE LENS (2026-09-21): rain drops on the glass while it rains, and
    /// faint dirt on it that throws bokeh round every bright light at night.
    /// The owner, on what to take from Need for Speed (2015): "I like the
    /// particle effects on screen for rain and light."
    ///
    /// This is the game half, like <see cref="SpeedBlur"/>'s published look:
    /// how much rain is on the glass, how dirty it is, how hard the air is
    /// pushing the drops, and the clock they live by, for ONE camera.
    /// <see cref="SpeedBlurFeature"/>'s lens pass draws it through PSX/Lens
    /// (Shaders/PSXLens.shader) on that camera and no other — the mirror, the
    /// pizza camera and the scene view never get drops.
    ///
    /// WHY A PASS AND NOT PSX/Blit. The Blit was the first plan and the
    /// wrong one: by the time it runs, the race HUD is already inside the
    /// picture it reads (the ScreenSpaceCamera canvas draws in the world
    /// camera's transparents, or the stacked HUD camera writes the same
    /// target), so a drop there would refract the lap counter and the dirt
    /// would bloom every HUD glyph. The pass runs after the speed blur and
    /// before the stacked HUD camera, and <see cref="SpeedBlur"/> hands the
    /// HUD to that camera whenever LENS FX is on, exactly as it does for the
    /// blur. It runs at framebuffer resolution (240-480 lines) — pixel-art
    /// drops that PSX/Blit dithers with the rest, at a fifth of the cost of
    /// doing it per device pixel.
    ///
    /// WHO WRITES IT. <see cref="SpeedBlur"/>.Update, every frame of play
    /// mode, for its own camera: off on foot and from the top-down view (the
    /// blur's own view rule), off with LENS FX: OFF, off when the HUD could
    /// not be moved out of the way. Edit-mode tools, where no Update runs,
    /// call <see cref="PreviewSet"/> with the camera they are about to render
    /// and clear it with a null camera afterwards. A SpeedBlur that is
    /// disabled or destroyed clears it if it is the one that set it, so a
    /// scene change never leaves drops published for a dead camera.
    ///
    /// Fields, not properties, by contract: the self-test and the shot tools
    /// read them directly.
    /// </summary>
    public static class LensFx
    {
        public const string ShaderName = "PSX/Lens";

        /// <summary>Below this a term is not drawn at all (the shader's
        /// LENS_MIN), and with both terms below it the pass is not even
        /// enqueued: a dry day costs nothing.</summary>
        public const float MinDrawn = 0.001f;
        /// <summary>The road speed at which the air over the glass is at its
        /// strongest (<see cref="Flow"/> 1). Drops slide outward at up to 0.6
        /// of a cell over their life at that speed.</summary>
        public const float FlowKmh = 160f;
        /// <summary>At full flow a drop lives out its life this much faster
        /// (x (1 + FlowLife x flow)): the air tears drops off the glass. Put
        /// into the CLOCK, not the shader — see <see cref="Time"/>.</summary>
        public const float FlowLife = 2.5f;
        /// <summary>Dirt = saturate(DirtFromNight x dark + DirtFromRain x
        /// rain), dark = the darker half of the night curve (see
        /// <see cref="DirtFor"/>). Night is when there are lamps to catch;
        /// rain puts grime and spray on the glass whatever the hour.</summary>
        public const float DirtFromNight = 0.7f, DirtFromRain = 0.5f;
        /// <summary>The PSXGlobals.night value below which a DRY lens shows no
        /// dirt at all: TimeOfDay.NightFor gives sunset 0.3 and dawn 0.5, so
        /// both are clean; dusk (0.75) gets half, full night all of it.</summary>
        public const float DirtNightStart = 0.5f;

        /// <summary>The one camera the lens is drawn on; null = none.</summary>
        public static Camera Camera;
        /// <summary>Rain on the glass 0..1; dirt 0..1; flow 0..1 (road speed /
        /// <see cref="FlowKmh"/>).</summary>
        public static float Rain, Dirt, Flow;
        /// <summary>
        /// THE DROP CLOCK, in seconds — Time.time's rate at a standstill,
        /// faster with <see cref="Flow"/>. Integrated by SpeedBlur rather than
        /// multiplied in the shader, and that is the whole reason it exists:
        /// frac(time x rate x (1 + 2.5 flow)) jumps every drop to a random
        /// point of its life whenever the speedometer moves, because time is
        /// large — ten minutes in, a change of flow of one part in a thousand
        /// is half a life. A clock that runs faster never jumps.
        /// </summary>
        public static float Time;
        /// <summary>A camera is named and there is something to draw.</summary>
        public static bool Active;

        /// <summary>For the preview tools, where no Update runs: publish the
        /// lens for <paramref name="cam"/> by hand (<paramref name="time"/> is
        /// the drop clock). A null camera clears it — call that after the
        /// shot, or every later edit-mode render of that camera keeps the
        /// drops.</summary>
        public static void PreviewSet(Camera cam, float rain, float dirt, float flow, float time) =>
            Publish(cam, rain, dirt, flow, time);

        /// <summary>
        /// The dirt for an hour's darkness (PSXGlobals.night, 0..1) and the
        /// rain on the glass.
        ///
        /// Only the DARK half of the night curve counts: dark =
        /// saturate((night - <see cref="DirtNightStart"/>) / (1 - start)),
        /// so sunset (0.3) and dawn (0.5) get none, dusk (0.75) half and full
        /// night all of it. Dirt only reads as dirt where the frame is black
        /// and a few lamps punch through it — that is the NFS image, a grime
        /// disc blooming round each sodium head. Fed the raw night value it
        /// ran at 0.21 at SUNSET (the default hour) and 0.35 at dawn, and
        /// there the frame is anything but black: the lens printed its disc
        /// pattern over the bright horizon, the fog band and every white car
        /// in the shot. Rain is untouched: a wet lens is grimy at any hour.
        /// </summary>
        public static float DirtFor(float night, float rain)
        {
            float dark = Mathf.Clamp01((night - DirtNightStart) / (1f - DirtNightStart));
            return Mathf.Clamp01(DirtFromNight * dark + DirtFromRain * rain);
        }

        internal static void Publish(Camera cam, float rain, float dirt, float flow, float time)
        {
            bool has = cam != null;
            Camera = has ? cam : null;
            Rain = has ? Mathf.Clamp01(rain) : 0f;
            Dirt = has ? Mathf.Clamp01(dirt) : 0f;
            Flow = has ? Mathf.Clamp01(flow) : 0f;
            Time = time;
            Active = has && (Rain > MinDrawn || Dirt > MinDrawn);
        }
    }

    /// <summary>
    /// The sense of speed, done the way Need for Speed Carbon did it: the
    /// picture smears radially, harder the faster the car goes, and the part
    /// of it that stays sharp CLOSES IN — tunnel vision — until at 140 mph
    /// only the road the car is heading down, and the car itself, are clear.
    ///
    /// This replaced the radial speed STREAKS (2026-09-19). The owner's call:
    /// "I am not a fan of the speed lines. If anything, I would prefer a blur
    /// that increases with speed like NFS Carbon." Lines are drawn ON the
    /// picture; a blur is something that happens TO it, and it is the second
    /// that reads as speed rather than as an overlay. And the same day, once
    /// a first version existed to look at: "Blur should scale with speed,
    /// giving a more restricted tunnel vision of clearness. 30mph+ initiates
    /// very subtle blur, 60mph+ noticeable blur, 100mph+ substantial blur,
    /// 140mph+ extreme blur" — which is <see cref="Stages"/>, row for row.
    ///
    /// Two halves. This component is the game's: it turns road speed into a
    /// smear length, a tunnel and a place to point it, and publishes them
    /// (<see cref="ActiveCamera"/>, <see cref="ActiveStrength"/> and the
    /// rest). <see cref="SpeedBlurFeature"/> is the pipeline's: a URP pass
    /// that smears the camera's colour by that much, after the transparents.
    /// It runs inside the low-res framebuffer, so the smear is dithered and
    /// quantised by PSX/Blit with everything else.
    ///
    /// THE PLAYER'S CAR STAYS SHARP. It sits below where it is heading, so a
    /// tunnel that closes, closes over it. The car's renderers are tagged
    /// with <see cref="CarRenderingLayer"/> and the pass draws them into a
    /// mask it will not smear — the road beside the car streaks right up to
    /// its edge, the way the reference does it.
    ///
    /// THE HUD IS THE HARD PART. The lap counter, position, fuel bar and map
    /// are rasterised into that same framebuffer by this same camera — they
    /// are drawn in its transparent pass, in the corners, which is exactly
    /// where a radial blur is strongest. So while the blur is on, the HUD
    /// canvas is handed to a second camera STACKED on this one
    /// (<see cref="SetSplit"/>): URP draws a stacked overlay camera after
    /// every pass of its base, the blur included, into the same framebuffer.
    /// Same pixels, same dither, never smeared. The cluster, the cabin and
    /// the touch controls are screen-resolution overlays drawn after the
    /// framebuffer is shown and were never in question.
    ///
    /// The split is made at RUNTIME, from the two references the builder
    /// wires, rather than baked: every edit-mode preview tool renders the
    /// scene as saved, and none of them should have to know about a second
    /// camera to photograph a HUD.
    ///
    /// THE LENS RIDES THE SAME SPLIT (2026-09-21). Rain drops and dirt bokeh
    /// (<see cref="LensFx"/>) are a second pass on this camera, after the
    /// blur and before the HUD camera, with the same reason to keep the HUD
    /// out of it; so the split is made while SPEED BLUR or LENS FX is on, and
    /// this component also publishes the lens every frame — it already knows
    /// the camera, the car and the view rule the lens needs.
    /// </summary>
    public class SpeedBlur : MonoBehaviour
    {
        public CarController car;
        /// <summary>The 240-line HUD canvas on this camera. Wired by the
        /// builder. Null is allowed and means there is no HUD to protect.</summary>
        public Canvas hudCanvas;
        /// <summary>No blur while this is set, whatever the car is doing. The
        /// replay's switch: the blur is the DRIVER's speed and a trackside
        /// lens is standing still. A flag rather than disabling the component,
        /// because LateUpdate still has a HUD camera to keep pointed at the
        /// framebuffer — a phone rotated mid-replay rebuilds it.</summary>
        [System.NonSerialized] public bool suspended;

        /// <summary>One row of the tuning table: at this road speed, how long
        /// the smear is at the frame corner (a fraction of the distance from
        /// the focus), and the clear tunnel — nothing smears inside
        /// <c>inner</c>, the smear is full length from <c>outer</c> out
        /// (radii, 1 = the distance from frame centre to frame corner).</summary>
        public readonly struct Stage
        {
            public readonly float mph, strength, inner, outer;
            public Stage(float mph, float strength, float inner, float outer)
            {
                this.mph = mph; this.strength = strength; this.inner = inner; this.outer = outer;
            }
        }

        /// <summary>
        /// THE OWNER'S FOUR SPEEDS, and what each one looks like. Straight
        /// lines between the rows, nothing under the first, the last row held
        /// above it. MPH because that is how it was asked for and what the
        /// speedometer reads.
        ///
        ///  30  very subtle: it BEGINS here. Only the outer third of the frame
        ///      is in it at all, and at 40 the longest streak on a 480-line
        ///      picture is a dozen pixels in the far corner.
        ///  60  noticeable: the verge and the near kerb are moving; the clear
        ///      part is still most of the picture.
        /// 100  substantial: a roadside wall is a band of colour a seventh of
        ///      the frame long (the reference frame, Carbon at 109). The clear
        ///      tunnel is the road ahead and the car.
        /// 140  extreme: the smear is a third of the way to the focus and the
        ///      tunnel is a tenth of the frame. Everything that is not where
        ///      the car is going is streaks.
        /// </summary>
        public static readonly Stage[] Stages =
        {
            new Stage( 30f, 0.00f, 0.62f, 1.00f),
            new Stage( 60f, 0.06f, 0.46f, 0.95f),
            new Stage(100f, 0.18f, 0.28f, 0.80f),
            new Stage(140f, 0.32f, 0.10f, 0.62f),
        };

        public const float MpsPerMph = 0.44704f;

        /// <summary>The longest the smear ever gets: the last row's.</summary>
        public static float MaxStrength => Stages[Stages.Length - 1].strength;

        /// <summary>Below this the streak at the corner is about a pixel.
        /// The pass is not enqueued at all, so a car park costs nothing.</summary>
        public const float MinDrawnStrength = 0.004f;
        /// <summary>How fast the look follows the speedometer, 1/s. What is
        /// faded is the SPEED the table is read at, so a view change, a
        /// respawn or the option being switched walks the smear and the
        /// tunnel back through the same rows together rather than snapping a
        /// full-frame effect in one frame.</summary>
        const float FadeRate = 6f;
        /// <summary>How fast the tunnel swings to a new heading, 1/s, and how
        /// far from the middle of the frame it may go. It follows where the
        /// car is GOING — in a slide that is not where it is pointing — but a
        /// tunnel pinned to the edge of the picture is not a tunnel.</summary>
        const float FocusRate = 5f;
        const float FocusMinX = 0.30f, FocusMaxX = 0.70f, FocusMinY = 0.40f, FocusMaxY = 0.72f;
        static readonly Vector2 FrameCentre = new Vector2(0.5f, 0.5f);

        public const string ShaderName = "PSX/SpeedBlur";
        /// <summary>Unity's built-in UI layer. The HUD canvas moves onto it
        /// for the split: the world camera stops drawing it, the HUD camera
        /// draws nothing else.</summary>
        public const int HudLayer = 5;
        /// <summary>The RENDERING layer (not the GameObject layer — nothing
        /// about physics, culling or the wheels' raycasts moves) that marks
        /// the player's car for the pass's do-not-smear mask.</summary>
        public const uint CarRenderingLayer = 1u << 20;

        /// <summary>The camera the pipeline pass may run on, and the look it
        /// is to draw. Written here every frame, read by
        /// <see cref="SpeedBlurFeature"/> for every camera the project renders.</summary>
        public static Camera ActiveCamera { get; private set; }
        public static float ActiveStrength { get; private set; }
        public static float ActiveInner { get; private set; } = 1f;
        public static float ActiveOuter { get; private set; } = 1f;
        /// <summary>Where the tunnel points, in viewport units.</summary>
        public static Vector2 ActiveFocus { get; private set; } = new Vector2(0.5f, 0.5f);

        /// <summary>The stacked camera the HUD is on while split, else null.</summary>
        public Camera HudCamera => split ? hudCam : null;
        public bool IsSplit => split;

        Camera cam;
        Camera hudCam;
        bool split, splitRefused;
        bool worldDrewHudLayer;
        int canvasLayer;
        int layeredCount = -1;
        float shownMps;
        Vector2 focus = new Vector2(0.5f, 0.5f);
        float nextCarMark;
        readonly System.Collections.Generic.List<Renderer> carRenderers =
            new System.Collections.Generic.List<Renderer>();

        /// <summary>How fast the lens's flow follows the speedometer, 1/s.
        /// Slower than the blur's fade: a respawn drops the speed to nothing
        /// in one frame, and drops that were streaming outward should settle,
        /// not stop dead.</summary>
        const float LensFlowRate = 3f;
        /// <summary>The scene's PSXGlobals, for how night-time the hour is
        /// (its <c>night</c> field, written by TimeOfDay.Apply). Looked for
        /// with the car marking, twice a second until found, never per frame.
        /// The SCENE's value and not TimeOfDay.Current: the static carries
        /// over from the last race, the scene's field is what this scene was
        /// lit with.</summary>
        PSXGlobals sceneGlobals;
        float lensFlow;
        /// <summary>The drop clock (<see cref="LensFx.Time"/>). A double so a
        /// long session keeps adding frames to it at full precision; it is
        /// handed to the shader as a float.</summary>
        double lensClock;

        /// <summary>
        /// The look for a road speed in m/s, read off <see cref="Stages"/>.
        /// Static and public so the self-test can pin the table: a blur that
        /// showed at 20 mph would be reported as "the car park is out of
        /// focus", not as a curve bug.
        /// </summary>
        public static Stage LookFor(float speedMps)
        {
            float mph = Mathf.Abs(speedMps) / MpsPerMph;
            var rows = Stages;
            if (mph <= rows[0].mph) return new Stage(mph, 0f, rows[0].inner, rows[0].outer);
            for (int i = 1; i < rows.Length; i++)
            {
                if (mph > rows[i].mph) continue;
                Stage a = rows[i - 1], b = rows[i];
                float t = (mph - a.mph) / (b.mph - a.mph);
                return new Stage(mph, Mathf.Lerp(a.strength, b.strength, t),
                                 Mathf.Lerp(a.inner, b.inner, t), Mathf.Lerp(a.outer, b.outer, t));
            }
            var last = rows[rows.Length - 1];
            return new Stage(mph, last.strength, last.inner, last.outer);
        }

        /// <summary>The smear length alone, for a road speed in m/s.</summary>
        public static float StrengthFor(float speedMps) => LookFor(speedMps).strength;

        /// <summary>Whether the world may smear right now. Not on foot, and
        /// not from directly above, where nothing radiates from the middle of
        /// the frame. The cockpit keeps it: the cabin is a screen-resolution
        /// overlay and stays sharp, and what smears is the world going past
        /// the side glass. The pause menu is deliberately NOT here — a paused
        /// frame is a photograph of that instant and keeps its blur, which is
        /// also what lets the option be judged from the menu that toggles it.
        /// </summary>
        public static bool Allowed =>
            SpeedBlurPrefs.Enabled && ViewAllowed;

        /// <summary>Whether the lens may be drawn right now: LENS FX on, and
        /// the blur's own view rule. Not on foot — the walker has no glass in
        /// front of them — and not from directly above, which is a map, not a
        /// lens. The cockpit keeps it: there the drops are on the windscreen,
        /// seen through the cabin sheet's glass, and hidden by its dash and
        /// pillars like real ones. The replay keeps it too (a camera out in
        /// the rain is still wet); only the flow stops, see UpdateLens.</summary>
        public static bool LensAllowed =>
            LensFxPrefs.Enabled && ViewAllowed;

        /// <summary>The part of the rule the blur and the lens share.</summary>
        static bool ViewAllowed =>
            !OnFoot.ForecourtMode.OnFoot &&
            ChaseCamera.Current != ChaseCamera.View.TopDown;

        void OnEnable()
        {
            cam = GetComponent<Camera>();
            PrepareCamera();
        }

        /// <summary>
        /// Keep the picture THE SAME PICTURE once this camera stops drawing
        /// straight into its framebuffer.
        ///
        /// Left alone, URP renders an offscreen camera directly into its
        /// target: no intermediate buffer, and so none of the pipeline asset's
        /// HDR or render scale. A camera STACK — and a pass that reads the
        /// colour it writes — both force the intermediate, and with it
        /// everything that had been quietly ignored. On the Mobile asset that
        /// was a 0.8 render scale: the first build of this drew the world at
        /// four fifths size and stretched it back, a visibly softer game
        /// whenever SPEED BLUR was on, blur or no blur
        /// (SpeedBlurPlayCheck compares the two pictures and found it). The
        /// asset's scale is 1 now; this is the other half — an LDR buffer in
        /// the framebuffer's own format, not an HDR one that blends the lamp
        /// glows differently and rounds its way back to 8 bits.
        /// </summary>
        void PrepareCamera()
        {
            if (cam != null) cam.allowHDR = false;
        }

        void OnDisable()
        {
            // The HUD stays where it is (the split is harmless with no blur);
            // the pass stops.
            shownMps = 0f;
            if (ActiveCamera == cam) Publish(null, LookFor(0f), FrameCentre);
            // The lens too: a static naming a camera this component no longer
            // drives would keep drops published for it after the scene moved on.
            lensFlow = 0f;
            if (LensFx.Camera == cam) LensFx.Publish(null, 0f, 0f, 0f, 0f);
        }

        void Update()
        {
            if (cam == null) return;
            // The HUD goes onto its own camera while EITHER pass may draw over
            // the world: the blur would smear it, the lens would refract it.
            bool want = SpeedBlurPrefs.Enabled || LensFxPrefs.Enabled;
            if (want != split && !(want && splitRefused)) SetSplit(want);

            float v = car != null ? Mathf.Abs(car.forwardSpeed) : 0f;
            // With a HUD that could not be moved out of the way there is no
            // blur at all: smeared lap counters are worse than no effect.
            bool safe = hudCanvas == null || split;
            float target = Allowed && safe && !suspended ? v : 0f;
            // Unscaled: the option is toggled from a menu that stops time.
            float dt = Time.unscaledDeltaTime;
            shownMps = Mathf.Lerp(shownMps, target, 1f - Mathf.Exp(-FadeRate * dt));
            if (target <= 0f && shownMps < 0.5f) shownMps = 0f;
            focus = Vector2.Lerp(focus, Heading(), 1f - Mathf.Exp(-FocusRate * dt));

            if (Time.unscaledTime >= nextCarMark)
            {
                MarkCar();
                if (sceneGlobals == null) sceneGlobals = FindAnyObjectByType<PSXGlobals>();
                nextCarMark = Time.unscaledTime + 0.5f;
            }

            Publish(cam, LookFor(shownMps), focus);
            UpdateLens(safe, dt);
        }

        /// <summary>
        /// Publish the lens for this camera (<see cref="LensFx"/>).
        ///
        /// Rain is WeatherFx's (it eases in over a few seconds after the rain
        /// starts, and is 0 in snow — snow does not bead on glass). Dirt is the
        /// scene's darkness plus the rain. Flow is road speed over
        /// <see cref="LensFx.FlowKmh"/>, eased, and ZERO while the replay has
        /// the blur <see cref="suspended"/>: the same reasoning as the blur —
        /// the replay's lens is usually a trackside one, standing still, and
        /// the drops on it should drip, not stream. The drops themselves keep
        /// running in the replay; it is still raining.
        ///
        /// The clock runs on SCALED time, so the pause menu freezes the drops
        /// where they are: a paused frame is a photograph of that instant, as
        /// the blur's is. The switch itself is not faded — LENS FX is toggled
        /// from that paused menu and the answer should be visible at once.
        /// </summary>
        void UpdateLens(bool safe, float dt)
        {
            // Same safety as the blur: a HUD that could not be moved out of
            // the way gets no lens rather than a refracted lap counter.
            bool on = LensAllowed && safe;
            float rain = on ? Mathf.Clamp01(WeatherFx.LensRain) : 0f;
            float night = on && sceneGlobals != null ? Mathf.Clamp01(sceneGlobals.night) : 0f;
            float flowTarget = on && !suspended && car != null
                ? Mathf.Clamp01(Mathf.Abs(car.forwardSpeed) * 3.6f / LensFx.FlowKmh)
                : 0f;
            lensFlow = Mathf.Lerp(lensFlow, flowTarget, 1f - Mathf.Exp(-LensFlowRate * dt));
            if (flowTarget <= 0f && lensFlow < 0.002f) lensFlow = 0f;
            lensClock += Time.deltaTime * (1.0 + LensFx.FlowLife * lensFlow);
            LensFx.Publish(on ? cam : null, rain, on ? LensFx.DirtFor(night, rain) : 0f,
                           lensFlow, (float)lensClock);
        }

        /// <summary>
        /// Where the car is GOING, as this lens sees it: the point the whole
        /// picture is streaming away from, and so the one place it is still.
        /// Off the velocity, not the nose — sideways in a slide the world
        /// streams from where the car is sliding to — and from the nose when
        /// there is no velocity worth the name. Behind the lens (reversing,
        /// spun round) it is the middle of the frame.
        /// </summary>
        public Vector2 Heading()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (car == null || cam == null) return FrameCentre;
            Vector3 vel = car.Body != null ? car.Body.linearVelocity : Vector3.zero;
            Vector3 dir = vel.sqrMagnitude > 36f ? vel.normalized : car.transform.forward;
            Vector3 p = cam.WorldToViewportPoint(cam.transform.position + dir * 300f);
            if (p.z <= 0f) return FrameCentre;
            return new Vector2(Mathf.Clamp(p.x, FocusMinX, FocusMaxX), Mathf.Clamp(p.y, FocusMinY, FocusMaxY));
        }

        /// <summary>
        /// Tag the player's car for the pass's mask. Twice a second rather
        /// than once: the shell is swapped at runtime (the handoff dresses the
        /// reference car as the one the player owns, the debug bench re-dresses
        /// it mid-drive) and a body that arrives later has to be found. Into a
        /// kept list, so looking costs no garbage.
        /// </summary>
        public void MarkCar()
        {
            if (car == null) return;
            car.GetComponentsInChildren(true, carRenderers);
            foreach (var r in carRenderers)
                if (r != null && (r.renderingLayerMask & CarRenderingLayer) == 0)
                    r.renderingLayerMask |= CarRenderingLayer;
        }

        static void Publish(Camera camera, Stage look, Vector2 where)
        {
            ActiveCamera = camera;
            ActiveStrength = camera != null ? look.strength : 0f;
            ActiveInner = look.inner;
            ActiveOuter = look.outer;
            ActiveFocus = where;
        }

        void LateUpdate()
        {
            // The HUD canvas is ConstantPixelSize and takes its size from ITS
            // camera's target, so the HUD camera has to be looking at the
            // same framebuffer — which PSXCameraOutput rebuilds on every
            // resize, rotation and PICTURE change. URP itself ignores an
            // overlay camera's target; this is for the canvas.
            if (!split || hudCam == null) return;
            if (hudCam.targetTexture != cam.targetTexture)
                hudCam.targetTexture = cam.targetTexture;
            // The HUD grows at runtime — the track map, a rival's dot, the
            // city minimap, the replay captions — and everything new is born
            // on the Default layer, which is the WORLD camera's: it would be
            // drawn under the blur it is here to escape. hierarchyCount is one
            // integer for the whole canvas, so the check is free and the
            // re-layer runs only on the frame something was added.
            int count = hudCanvas.transform.hierarchyCount;
            if (count != layeredCount)
            {
                SetLayer(hudCanvas.transform, HudLayer);
                layeredCount = count;
            }
        }

        /// <summary>
        /// Hand the HUD canvas to a camera stacked on this one, or take it
        /// back. Everything is checked before anything is changed, so a
        /// refusal leaves the scene exactly as the builder made it.
        /// </summary>
        public bool SetSplit(bool on)
        {
            if (cam == null) cam = GetComponent<Camera>();
            PrepareCamera();
            if (on == split) return true;
            if (cam == null || hudCanvas == null) { splitRefused = on; return false; }

            // Null when this renderer cannot stack cameras (URP says why), and
            // the getter dereferences the active pipeline, which a camera
            // asked before URP is up does not have. Either way the answer is
            // "leave the HUD alone": a cosmetic does not get to throw.
            System.Collections.Generic.List<Camera> stack = null;
            try
            {
                var data = cam.GetUniversalAdditionalCameraData();
                stack = data != null ? data.cameraStack : null;
            }
            catch (System.NullReferenceException) { }
            if (stack == null) { splitRefused = on; return false; }

            if (on)
            {
                if (hudCam == null) hudCam = MakeHudCamera();
                hudCam.fieldOfView = cam.fieldOfView;
                hudCam.targetTexture = cam.targetTexture;
                hudCam.enabled = true;
                if (!stack.Contains(hudCam)) stack.Add(hudCam);

                canvasLayer = hudCanvas.gameObject.layer;
                SetLayer(hudCanvas.transform, HudLayer);
                layeredCount = hudCanvas.transform.hierarchyCount;
                worldDrewHudLayer = (cam.cullingMask & (1 << HudLayer)) != 0;
                cam.cullingMask &= ~(1 << HudLayer);
                hudCanvas.worldCamera = hudCam;
            }
            else
            {
                hudCanvas.worldCamera = cam;
                SetLayer(hudCanvas.transform, canvasLayer);
                // One bit back, not a saved mask: anything else that changed
                // this camera's layers in between keeps its change.
                if (worldDrewHudLayer) cam.cullingMask |= 1 << HudLayer;
                if (hudCam != null)
                {
                    stack.Remove(hudCam);
                    hudCam.enabled = false;
                    hudCam.targetTexture = null;
                }
            }
            split = on;
            splitRefused = false;
            return true;
        }

        Camera MakeHudCamera()
        {
            var go = new GameObject("HUDCamera");
            // A preview tool makes one of these in an OPEN SCENE; it must
            // never be saved into it.
            if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(cam.transform, false);
            var c = go.AddComponent<Camera>();
            c.clearFlags = CameraClearFlags.Depth;
            c.cullingMask = 1 << HudLayer;
            c.nearClipPlane = 0.1f;
            // The canvas plane is a metre out.
            c.farClipPlane = 10f;
            c.depth = cam.depth + 1f;
            c.allowHDR = cam.allowHDR;
            c.allowMSAA = false;
            c.useOcclusionCulling = false;
            var data = c.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Overlay;
            data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            return c;
        }

        static void SetLayer(Transform root, int layer)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
        }

        void OnDestroy()
        {
            if (hudCam != null)
            {
                if (Application.isPlaying) Destroy(hudCam.gameObject);
                else DestroyImmediate(hudCam.gameObject);
            }
        }

        /// <summary>For the preview tools, where no Update runs: publish the
        /// look for a road speed and a camera by hand, the tunnel pointed at
        /// <paramref name="where"/> (the frame centre if none is given). A null
        /// camera clears it.</summary>
        public static void Preview(Camera camera, float speedMps, Vector2? where = null) =>
            Publish(camera, LookFor(camera != null ? speedMps : 0f), where ?? FrameCentre);
    }
}
