using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// Which bulb is behind the instrument cluster.
    ///
    /// Three real ones, not three arbitrary hues — the same set the HTML
    /// cluster this is modelled on offers, picked because each is instantly
    /// placeable as a particular kind of car at night:
    ///
    ///   Green  — JDM CRT-phosphor glow. 240Z, AE86, 80s CRX, late 900.
    ///   Amber  — Honda 90s warm bulb: EG/EJ Civic, DC Integra, NSX. A soft
    ///            incandescent gold rather than a saturated LED yellow.
    ///   Orange — BMW 90s amber-orange: E36, E34, E38, E39. Warmer toward red
    ///            than the Honda yellow, and the reason both are in the list.
    ///
    /// Persisted, because it is a preference rather than a setting: a player
    /// who picks green should not have to pick it again next race. Saved
    /// immediately for the same reason the camera view is — on Web a closed tab
    /// is not a clean quit.
    /// </summary>
    public enum ClusterBulb { Green = 0, Amber = 1, Orange = 2 }

    public static class ClusterBulbs
    {
        public static readonly string[] Names = { "GREEN", "AMBER", "ORANGE" };
        const string PrefKey = "psx.clusterBulb";
        const int Count = 3;

        static ClusterBulb current = (ClusterBulb)(-1);

        public static ClusterBulb Current
        {
            get
            {
                if ((int)current < 0)
                    current = (ClusterBulb)Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, 1), 0, Count - 1);
                return current;
            }
            set
            {
                var v = (ClusterBulb)(((int)value % Count + Count) % Count);
                if (v == current) return;
                current = v;
                PlayerPrefs.SetInt(PrefKey, (int)v);
                PlayerPrefs.Save();
                Changed++;
            }
        }

        /// <summary>Bumped on every change. A cluster compares it against the
        /// value it last drew with, which is cheaper and more reliable than an
        /// event a scene load could leave dangling.</summary>
        public static int Changed { get; private set; }

        public static void Cycle(int step = 1) => Current = (ClusterBulb)((int)Current + step);

        public static string Name => Names[(int)Current];

        /// <summary>
        /// Is the backlight actually ON?
        ///
        /// A cluster bulb is a thing you see AT NIGHT. In daylight the dial
        /// reads as printed white-on-black, because that is what a dial is — the
        /// bulb behind it is drowned out by the sun the same way your headlights
        /// are. Lighting it all day was a deliberate choice to keep the player's
        /// chosen colour visible and it was the wrong one: a green-lit dial at
        /// noon does not look like a preference, it looks like a bug.
        ///
        /// Uses the same signal the headlights and the lamp glows do, so the
        /// cluster comes on at exactly the hour the street lights do.
        /// </summary>
        public static bool Backlit => TimeOfDay.At(TimeOfDay.Current).lightsOn;

        /// <summary>Dirty token: the palette depends on the bulb AND on whether
        /// it is lit, and a dial baked at noon is wrong by dusk.</summary>
        public static int Revision => (int)Current * 4 + (Backlit ? 1 : 0) + Changed * 16;

        /// <summary>Dial face. Nearly black either way; at night it picks up a
        /// hint of whatever is behind it, the way dark plastic does.</summary>
        public static Color Face => !Backlit ? (Color)new Color32(0x0A, 0x0A, 0x0A, 0xFF)
            : Pick(new Color32(0x10, 0x15, 0x10, 0xFF),
                   new Color32(0x15, 0x13, 0x0D, 0xFF),
                   new Color32(0x18, 0x13, 0x10, 0xFF));

        /// <summary>Ticks and numerals. WHITE in daylight — a printed dial —
        /// and the bulb once it is dark.</summary>
        public static Color Lit => !Backlit ? (Color)new Color32(0xEA, 0xEA, 0xEA, 0xFF)
            : Pick(new Color32(0x5C, 0xFF, 0x6A, 0xFF),
                   new Color32(0xD9, 0xB8, 0x60, 0xFF),
                   new Color32(0xFF, 0x85, 0x33, 0xFF));

        /// <summary>Bezel and minor ticks — the same again, a step down.</summary>
        public static Color Dim => !Backlit ? (Color)new Color32(0x8A, 0x8A, 0x8A, 0xFF)
            : Pick(new Color32(0x2D, 0x80, 0x35, 0xFF),
                   new Color32(0x96, 0x7A, 0x35, 0xFF),
                   new Color32(0xA8, 0x52, 0x1C, 0xFF));

        /// <summary>The digital readouts, brighter than the dial they sit on so
        /// the number reads before the needle does.</summary>
        public static Color Text => !Backlit ? (Color)new Color32(0xFF, 0xFF, 0xFF, 0xFF)
            : Pick(new Color32(0xE0, 0xFF, 0xD8, 0xFF),
                   new Color32(0xF0, 0xE0, 0xB0, 0xFF),
                   new Color32(0xFF, 0xDD, 0xBC, 0xFF));

        /// <summary>Redline. NOT the bulb: it is the one mark on a cluster that
        /// means the same thing in every car ever built, and tinting it green
        /// to match the backlight would be the one place this palette is
        /// allowed to be pretty at the cost of being read.</summary>
        public static readonly Color Red = new Color32(0xD8, 0x22, 0x18, 0xFF);

        /// <summary>Needle. Red in all three, which is what the cluster this is
        /// modelled on settled on after trying to tint it per car: a needle the
        /// same colour as the dial behind it disappears at exactly the moment
        /// you need it, which on a tachometer is the top of the sweep.</summary>
        public static readonly Color Needle = new Color32(0xEE, 0x44, 0x44, 0xFF);

        static Color Pick(Color green, Color amber, Color orange) =>
            Current == ClusterBulb.Green ? green
            : Current == ClusterBulb.Amber ? amber : orange;
    }

    /// <summary>
    /// A working analog speedometer and tachometer, drawn on their own
    /// overlay canvas at DEVICE resolution.
    ///
    /// They used to be rasterised into the 240-line framebuffer with the rest
    /// of the picture, so that they would dither and crawl along with it rather
    /// than sit on top as crisp modern vector art. That reasoning is sound in a
    /// still and wrong on a phone: a tenth of 240 lines is 25 pixels of radius
    /// carrying eight-pixel numerals, and an eight-pixel numeral out of a
    /// dynamic font atlas is a grey smudge whatever you upscale it with. It was
    /// reported, accurately, as "too small and too blurry". The touch wheel and
    /// pedals were already at screen resolution on their own overlay, so the
    /// frame was never uniformly 240 lines; this makes the split deliberate.
    /// The cabin — what you read and what you hold — is at device resolution;
    /// the world, and the race data printed over it, stay at 240.
    ///
    /// What the game had was a speed in text and a horizontal bar for revs. A
    /// bar cannot be read at a glance the way a needle can — the whole point of
    /// a round dial is that you learn where the needle POINTS at the shift, and
    /// after a lap you stop reading numbers at all.
    ///
    /// Geometry is lifted from the HTML cluster this is modelled on, as
    /// fractions of the dial radius, so both instruments are the same
    /// instrument at two sizes: sweep 250 degrees from just below 8 o clock
    /// through 12 to just below 4, ticks in a band just inside the bezel,
    /// numerals inside those, a redline arc on the outer band of the tach, and
    /// a kite needle pivoting on a hub cap.
    ///
    /// Everything static — face, bezel, ticks, redline — is baked into ONE
    /// texture per dial. The alternative is forty rotated Image components per
    /// instrument, and this HUD is redrawn into a 240-line buffer every frame.
    /// Only the needle and the two digits move.
    /// </summary>
    public class GaugeCluster : MonoBehaviour
    {
        public CarController car;

        /// <summary>Dial radius in framebuffer pixels. The buffer is 240 lines
        /// tall whatever the display is, so this is a real size and not a
        /// fraction of anything: two 100-pixel dials side by side fill the
        /// bottom third of the frame and leave the corners for the touch wheel
        /// and pedals, which live on their own canvas at screen resolution.
        /// </summary>
        /// <summary>
        /// Dial radius as a FRACTION OF THE FRAME HEIGHT, not in pixels. The
        /// framebuffer is 240 lines at RETRO and 480 at SHARP, so a pixel size
        /// would be a cluster that halves the moment the player sharpens the
        /// picture.
        ///
        /// 0.105 makes each dial about a fifth of the frame tall. The first
        /// version was a third each, side by side across the middle of the
        /// bottom edge — a pair of dinner plates parked on the road you are
        /// trying to see.
        /// </summary>
        public float radiusFrac = 0.150f;
        /// <summary>
        /// Dial radius in COCKPIT view, where there is only one dial.
        ///
        /// Bigger than the pair, because it is a binnacle rather than a HUD:
        /// from the driver's seat the rev counter is a real instrument sitting
        /// on a real dashboard, and the space the speedometer used to take is
        /// now a digital readout a third of its size.
        /// </summary>
        public float cockpitRadiusFrac = 0.185f;
        /// <summary>
        /// Everything in the cockpit binnacle, scaled by one number.
        ///
        /// The binnacle in a cockpit is not a HUD in the corner: it sits on the
        /// dashboard, BEHIND the steering wheel, and it has to fit in the
        /// opening at the top of the rim rather than filling the bottom of the
        /// frame. Ported at HUD size it was a rev counter the size of a dinner
        /// plate with a wheel drawn across it.
        ///
        /// One factor over the whole group rather than five separate fractions,
        /// so the dial, the readout, the gear box and the gaps between them
        /// stay in proportion when it moves.
        /// </summary>
        public float cockpitGaugeScale = 0.62f;
        /// <summary>Clearance from the frame edges, also as a fraction of the
        /// frame height.</summary>
        public float marginFrac = 0.03f;
        /// <summary>Between a dial and the touch control beside it, in canvas
        /// units — the same 12 the pair used to leave either side of the
        /// centreline when they sat together in the middle.</summary>
        const float ControlClearance = 12f;
        /// <summary>The least a dial's edge may come to the frame's centre
        /// line with touch controls up — together, a 24-unit lane of road that
        /// no instrument is ever drawn across.</summary>
        const float CentreClearance = 12f;

        /// <summary>
        /// How much of the world a HUD dial's face hides, as the alpha of the
        /// face fill at the dial CENTRE: 0.38, so six tenths of the road
        /// behind it still shows through.
        ///
        /// Asked for by the owner on 2026-09-21, with Need for Speed (2015) as
        /// the reference: "The speedometers look a little too modern, but I
        /// like that they are transparent and have analog needles." So the
        /// 90s instrument stays exactly as it is — the numerals, the kite
        /// needles, the 250 degree sweep, the fuel and coolant gauges in the
        /// wedge — and only the FACE changes, from a black plate to smoked
        /// glass. Two opaque discs a fifth of the frame tall are two holes
        /// punched in the bottom of the picture; two smoked ones are
        /// instruments laid over it.
        ///
        /// Not lower. A face you can see straight through is no longer a face,
        /// and the white ticks need something to stand on when the corner
        /// behind them is a bright sky or a sodium-lit road. Nor does
        /// legibility rest on this number alone: the fill darkens toward the
        /// rim (<see cref="HudFaceRimAlpha"/>), every mark gets a dark halo of
        /// its own baked around it (Dial.BakeFace), the numerals get an
        /// outline, and the needle gets a dark rim. The self-test pins this
        /// number inside (0.2, 0.7).
        /// </summary>
        public const float HudFaceAlpha = 0.38f;
        /// <summary>
        /// The same for the COCKPIT binnacle: 1, solid, on purpose. That dial
        /// is not drawn over the world. It sits ON the cabin artwork, a real
        /// instrument in a real dashboard, and a see-through rev counter in a
        /// dash would show the plastic behind it, which no car has ever done.
        /// </summary>
        public const float CockpitFaceAlpha = 1f;
        /// <summary>
        /// The smoked face's alpha from <see cref="FaceRampR"/> out to the
        /// bezel. Smoked glass reads darker toward its rim, and the rim is
        /// exactly where the ticks, the redline and the fuel and coolant marks
        /// stand. So the face is darkest where it has something to carry, and
        /// clearest in the middle, where there is only the hub and the road.
        /// </summary>
        const float HudFaceRimAlpha = 0.62f;
        /// <summary>Where the smoked fill reaches <see cref="HudFaceRimAlpha"/>,
        /// as a fraction of the dial radius: just inside the tick band. It
        /// rises with the SQUARE of the radius, so the middle of the face stays
        /// close to <see cref="HudFaceAlpha"/> and most of the darkening
        /// happens where the marks are.</summary>
        const float FaceRampR = 0.90f;
        /// <summary>
        /// The HUD gear panel, smoked to match the dials beside it: a black
        /// face at this alpha, with an edge in the bulb's Dim colour at
        /// <see cref="GearEdgeAlpha"/>. The pale LCD it used to be was the
        /// one opaque slab left in the corner once the faces went clear, and a
        /// dark-digit LCD cannot simply be made see-through (dark digits over
        /// a dark road vanish), so on the HUD the panel becomes glass and its
        /// digit is drawn in the bulb's Lit colour. The cockpit's boxes sit on
        /// a dashboard and keep their LCDs.
        /// </summary>
        const float GearSmokeAlpha = 0.45f, GearEdgeAlpha = 0.8f;

        /// <summary>
        /// The dark outline on every piece of text drawn over the world: the
        /// numerals, the unit caption, the E/F and C/H letters and the HUD gear
        /// digit. Black at 0.75, 1.2 canvas units down and to the right, so a
        /// numeral keeps an edge over a bright sky as well as a dark road.
        ///
        /// HUD layout only. The cockpit's text sits on an opaque face or an LCD
        /// and has never needed an edge, and a Shadow alone (what RaceHUD
        /// uses) darkens only one side of each stroke. Once the face behind
        /// the text is smoked glass, the other sides need an edge as well.
        /// </summary>
        static void AddLegibilityOutline(Graphic g)
        {
            var o = g.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.75f);
            o.effectDistance = new Vector2(1.2f, -1.2f);
        }

        /// <summary>Set by RaceHandoffApplier from the car faults. A dead
        /// cluster parks both needles and blanks the digits — the player should
        /// see broken instruments, not missing ones.</summary>
        public bool hideGauges;
        /// <summary>A failing tacho wanders. Same source as the old bar.</summary>
        public bool rpmFlutter;

        Dial tach, speedo;
        Text gearText, speedText;
        /// <summary>
        /// How far up the BOTTOM-LEFT CORNER this cluster reaches, in its own
        /// canvas units — the top of the tach when the dials are in the
        /// corners, and zero in every other layout, because in those the corner
        /// belongs to the steering wheel or to nothing.
        ///
        /// Published for the same reason TouchControls.WheelInset is: the pizza
        /// cam has to sit clear of whatever is in that corner, and a fraction of
        /// the frame that clears a dial is a different fraction every time the
        /// dial is retuned. Both this and the wheel's box are on the same 1280
        /// by 720 reference, so the number means the same thing to a reader as
        /// it does here.
        /// </summary>
        public static float CornerTop { get; private set; }

        /// <summary>
        /// The same for the RIGHT-hand side, where the pizza cam lives now
        /// (2026-09-25, "opposite of the race map"): the top of the speedometer
        /// in the twin-dial layouts — in the corner on a PC, beside the pedals
        /// on a phone, and under the cam either way — and of the binnacle when
        /// the cockpit puts it on the right. Zero when nothing of this
        /// cluster's is over there.
        /// </summary>
        public static float RightCornerTop { get; private set; }

        /// <summary>Everything the cockpit layout adds, under one parent so a
        /// rebuild is one Destroy rather than a hunt for stragglers.</summary>
        GameObject cockpitRoot;
        /// <summary>The same, for what the twin-dial layout adds beside its
        /// dials — today just the gear panel. It needs a root of its own
        /// because <see cref="Dial.Destroy"/> only tears down dials, and a
        /// rebuild that leaves the panel behind stacks the next one on top of
        /// it.</summary>
        GameObject panelRoot;
        int builtBulb = -1;
        float builtRedline = -1f, builtSpeedMax = -1f;
        int builtHeight = -1, builtUnits = -1;
        bool builtTouch;
        bool builtCockpit;
        Vector2 builtWheelCentre = new Vector2(float.NaN, float.NaN);
        float builtWheelRadius = -1f;
        float flutter;

        static readonly string[] GearNames = { "R", "N", "1", "2", "3", "4", "5", "6" };

        /// <summary>
        /// What the two small gauges read.
        ///
        /// Found off the car rather than wired by the builder, and cached the
        /// first time they are asked for, because neither exists on an opponent
        /// and only the player ever has a cluster. A null answer means the
        /// needle sits on the empty end, which for a scene with no tank in it
        /// (a preview, the screenshot tool) is the honest reading.
        /// </summary>
        FuelTank tank; EngineTemp temp;
        bool subsFound;

        FuelTank Tank { get { FindSubs(); return tank; } }
        EngineTemp Temp { get { FindSubs(); return temp; } }

        void FindSubs()
        {
            if (subsFound || car == null) return;
            subsFound = true;
            tank = car.GetComponent<FuelTank>();
            temp = car.GetComponent<EngineTemp>();
        }

        /// <summary>
        /// Park the two small needles at fixed readings.
        ///
        /// For the preview tool, and it exists for one reason: a needle
        /// sweeping an arc has a left and a right, and HALF SCALE is the single
        /// position that cannot tell you whether it has them the right way
        /// round. A mirrored sub-gauge points straight down at 0.5 exactly like
        /// a correct one, and every still of a cluster at rest shows it there.
        /// </summary>
        public void PoseSubGauges(float coolant, float fuel)
        {
            if (tach != null) tach.SetSub(coolant);
            if (speedo != null) speedo.SetSub(fuel);
        }

        /// <summary>Where the COOLANT needle is pointing, 0-1, read off the
        /// transform. For TempPlayCheck, which has to be able to tell a dial
        /// that is reading the engine from one that merely exists.</summary>
        public float SubNeedleFraction => tach != null ? tach.SubFraction : -1f;

        /// <summary>
        /// Whether a dial on screen is carrying the FUEL gauge: the twin-dial
        /// speedometer, big enough to have got its sub-gauge. False in the
        /// cockpit (one dial, coolant only) and on a dial too small to carry
        /// one. RaceHUD reads this to drop its own fuel bar, which beside a
        /// fuel needle was the same reading twice (the owner, 2026-09-21).
        /// </summary>
        public bool CarriesFuel => speedo != null && speedo.HasSub;

        /// <summary>A HUD rev counter's face — smoked, redline from
        /// <paramref name="redlineFrac"/>, coolant scale with its red H mark — baked
        /// exactly as a race bakes it, for the self-test that scans it. The
        /// caller owns the texture.</summary>
        public static Texture2D BakeTachFaceForCheck(int radius, float redlineFrac) =>
            Dial.BakeFace(radius, 9000f, 1000f, redlineFrac, true, true, true).texture;

        /// <summary>Where the coolant reading <paramref name="frac"/> meets the
        /// ring, in degrees from straight down about the dial centre.</summary>
        public static float SubRingBearing(float frac) => Dial.SubRingBearing(frac);

        /// <summary>Where the main scale ENDS, in the same bearing: 55 degrees
        /// right of straight down with the sweep this copies.</summary>
        public static float SweepEndBearing =>
            90f - Mathf.Repeat(Dial.StartDeg + Dial.SweepDeg, 360f);

        /// <summary>
        /// Park the two big needles, in their own units — revs and whatever the
        /// player reads speed in.
        ///
        /// Same job as <see cref="PoseSubGauges"/> and the same reason: with no
        /// car in the scene both needles sit on their end stop, which is the
        /// one position that shows neither how far the sweep runs nor which way
        /// round it goes.
        /// </summary>
        public void PoseNeedles(float rpm, float speed)
        {
            if (tach != null) tach.SetValue(rpm);
            if (speedo != null) speedo.SetValue(speed);
        }

        void Start() => Build();

        /// <summary>
        /// Build or rebuild the cluster. Idempotent, and safe to call outside
        /// play mode — the screenshot tool does exactly that, because a HUD
        /// that only exists once a race has started is a HUD no reference shot
        /// ever contains.
        /// </summary>
        public void Build()
        {
            float redline = car != null ? car.revLimitRPM : 8000f;
            // What the car AS BUILT can reach, not the stock sheet figure: a
            // power build out-runs the sheet on purpose, and a dial scaled to
            // the sheet leaves its needle on the end stop — reading "280" at
            // any speed above it. See CarController.ReachableTopSpeedMps.
            float speedMax = SpeedScale(SpeedUnits.FromKmh(car != null
                ? Mathf.Max(car.BuildTopSpeedMps, car.ReachableTopSpeedMps) * 3.6f : 240f));
            int bulb = ClusterBulbs.Revision;
            // The canvas is ConstantPixelSize on the PSX camera, so its height
            // IS the framebuffer line count -- which the player can change.
            int frame = FrameHeight();
            bool touch = TouchControls.Instance != null && TouchControls.Instance.Visible;
            // From the driver's seat the binnacle is a different instrument
            // pack, not the same one moved: one big rev counter with the speed
            // and the gear as digital readouts beside it, which is the layout
            // of the cockpit this was modelled on and of most cars built since
            // about 1990.
            bool cockpit = ChaseCamera.Current == ChaseCamera.View.Cockpit;

            // The steering wheel the binnacle sits behind, as the cabin
            // reports it. Part of the dirty token because the cabin publishes
            // it during ITS Start, which may be after this one's — without it
            // the first cockpit of a session lays its dials out in the corner
            // and never moves them.
            Vector2 wheelC = CockpitView.WheelCentre;
            float wheelR = CockpitView.WheelRadius;

            // The unit is part of the token in its own right, not just via
            // speedMax: two scales can round to the same number and the CAP
            // under the needle would still be wrong.
            int units = SpeedUnits.Changed * 2 + (SpeedUnits.Mph ? 1 : 0);

            // The smoked HUD faces add NOTHING to this token, and that has to
            // stay true: their alpha, halos and outlines are constants, and
            // the only runtime input they read is ClusterBulbs.Backlit, which
            // is already in `bulb`. A tunable transparency (a user setting,
            // the weather, how bright the sky is) would need a term here, or
            // the dials would keep the face they were baked with.
            if (tach != null && bulb == builtBulb && frame == builtHeight && touch == builtTouch
                && cockpit == builtCockpit && units == builtUnits
                && wheelC == builtWheelCentre && Mathf.Approximately(wheelR, builtWheelRadius)
                && Mathf.Approximately(redline, builtRedline)
                && Mathf.Approximately(speedMax, builtSpeedMax)) return;

            builtBulb = bulb; builtRedline = redline; builtSpeedMax = speedMax;
            builtUnits = units;
            builtHeight = frame; builtTouch = touch; builtCockpit = cockpit;
            builtWheelCentre = wheelC; builtWheelRadius = wheelR;
            if (tach != null) { tach.Destroy(); tach = null; }
            if (speedo != null) { speedo.Destroy(); speedo = null; }
            if (cockpitRoot != null) { KillTree(cockpitRoot); cockpitRoot = null; }
            if (panelRoot != null) { KillTree(panelRoot); panelRoot = null; }
            gearText = null; speedText = null;
            // Cleared before the layout runs, not after: only one branch below
            // puts anything in that corner, and a stale number left over from
            // the last one is worse than none — it would push the pizza cam up
            // the screen to clear a dial that has moved.
            CornerTop = 0f;
            RightCornerTop = 0f;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            int radius = Mathf.Max(16, Mathf.RoundToInt(frame *
                (cockpit ? cockpitRadiusFrac * cockpitGaugeScale : radiusFrac)));
            int margin = Mathf.RoundToInt(frame * marginFrac);

            float tachMaxRPM = Mathf.Max(1000f, Mathf.Ceil(redline * 1.1f / 1000f) * 1000f);
            float redFrac = car != null ? car.redlineRPM / tachMaxRPM : 0.85f;

            if (cockpit)
            {
                BuildCockpit(font, frame, radius, margin, tachMaxRPM, redFrac, touch);
                HudOnTop.Apply(gameObject);
                return;
            }

            // WHERE they go depends on what else is on screen, and what else is
            // on screen is not on this canvas: the steering wheel and the pedals
            // are TouchControls, at screen resolution, where nothing measured in
            // framebuffer pixels can see them.
            //
            // With no touch controls both bottom corners are empty, and the
            // corners are where instruments belong -- as far from the vanishing
            // point as the frame allows. With them, the corners are the
            // controls', so each dial goes as far out as the controls let it:
            // hard against the one on its own side, which leaves the middle of
            // the bottom edge -- the road -- to the road.
            Vector2 tachAnchor, speedoAnchor, tachPos, speedoPos;
            if (touch)
            {
                // The band the touch panel leaves between the wheel and the
                // pedals, REPORTED by the panel rather than derived from a
                // fraction of the frame width. A fraction that clears both is a
                // different fraction every time the panel is retuned, and it
                // was wrong within one build of each of the last two changes.
                float left = TouchControls.WheelInset;
                float right = FrameWidth() - TouchControls.PedalsInset;
                float band = Mathf.Max(160f, right - left);
                // On a narrow screen it is the BAND that limits the dials, not
                // the frame height: two of them and their clearances have to
                // fit into it, and a dial that overlaps a control is worse than
                // a small one. At this cap the two meet in the middle.
                radius = Mathf.Min(radius,
                    Mathf.FloorToInt((band - ControlClearance * 2f) * 0.25f));
                // ...AND each dial stays on its own side of the centre line.
                // The band is not centred on the frame — the wheel's box is
                // wider than the pedal column — so sized off the band alone, a
                // 4:3 tablet's rev counter reached 24 px past the middle of the
                // screen, which is the one place this layout exists to keep
                // clear. Found by DriveHudPreview on its first run.
                float mid = FrameWidth() * 0.5f;
                float leftRoom = mid - CentreClearance - (left + ControlClearance);
                float rightRoom = (right - ControlClearance) - (mid + CentreClearance);
                radius = Mathf.Min(radius, Mathf.FloorToInt(Mathf.Min(leftRoom, rightRoom) * 0.5f));
                // A portrait window has no middle to keep clear; a dial it can
                // still read beats one sized to nothing.
                radius = Mathf.Max(16, radius);
                // REVS BESIDE THE WHEEL, SPEED BESIDE THE PEDALS. They used to
                // be centred as a pair in the band, which put two dials across
                // the one part of the picture a driver is looking at; the owner
                // asked for them "from center, to left and right, respectively
                // to clear the center of the screen" (2026-09-18). Revs on the
                // left keeps the side of the binnacle they are on in almost
                // every car, and the speed ends up beside the pedals that set it.
                tachAnchor = speedoAnchor = new Vector2(0f, 0f);
                tachPos = new Vector2(left + ControlClearance + radius, radius + margin);
                speedoPos = new Vector2(right - ControlClearance - radius, radius + margin);
            }
            else
            {
                tachAnchor = new Vector2(0f, 0f);
                speedoAnchor = new Vector2(1f, 0f);
                tachPos = new Vector2(radius + margin, radius + margin);
                speedoPos = new Vector2(-(radius + margin), radius + margin);
            }

            // Revs on the left, speed on the right: the same side of the binnacle
            // they sit on in almost every car with two round dials, and the tach
            // is the one you look at in a corner.
            //
            // The scale runs PAST the limiter, to the next whole thousand above
            // it plus ten percent. Ending exactly at the limiter is what the
            // catalog numbers invite — every car in it limits 500 rpm past its
            // redline — and it gives you a red band six percent of the sweep
            // wide with the needle jammed against the end stop every upshift.
            // A real tacho leaves the last segment empty so the red one has
            // room to mean something.
            // Coolant under the revs, fuel under the speed — the pairing on the
            // cluster this is modelled on, and on most twin-dial cars: the
            // gauge that says something about the ENGINE goes in the engine's
            // dial. Asked for in the constructor because the scale is baked
            // into the face; a dial too small to carry one says so afterwards.
            // H is a warning and F is not, so only the tach is told its high
            // end is one.
            //
            // TRANSLUCENT: these two are drawn over the world, so their faces
            // are smoked glass (see HudFaceAlpha). The cockpit binnacle below
            // passes false and keeps the solid face it has always had.
            float tachMax = tachMaxRPM;
            tach = new Dial(transform, font, "Tach", tachAnchor, tachPos, radius,
                            tachMax, 1000f, LabelStep(tachMax, 1000f, radius, 1f / 1000f), 1f / 1000f, "x1000",
                            redFrac, "C", "H", subHighIsDanger: true, translucent: true);
            float sTick = SpeedTick(speedMax);
            speedo = new Dial(transform, font, "Speedo", speedoAnchor, speedoPos, radius,
                              speedMax, sTick, LabelStep(speedMax, sTick, radius, 1f), 1f,
                              SpeedUnits.Label, -1f, "E", "F", translucent: true);
            // NOTHING IS PRINTED ON THE FACES. The speed and the gear used to
            // sit under their own needles, on the reasoning that a cluster with
            // a digital readout puts each number in the dial it belongs to.
            // Some do — but the face of the instrument this copies carries
            // nothing but its scale, and once the fuel and coolant gauges took
            // the bottom of the wedge the digits were a third thing crowding a
            // face that has room for two. A speedometer that also prints the
            // speed is also telling you its needle is not worth reading.
            //
            // The speed loses nothing by going: that is what the right-hand
            // dial is for. The gear does — a needle cannot show it — so it
            // moves OFF the face into its own small panel, the same one the
            // cockpit binnacle has always used, and only when nothing else on
            // screen is already showing it.
            if (!touch)
            {
                MakeGearPanel(font, radius, margin, tachAnchor, tachPos);
                CornerTop = tachPos.y + radius;
            }
            RightCornerTop = speedoPos.y + radius;

            // Everything above was created just now, so it is wearing the stock
            // depth-tested UI material and would vanish behind the bonnet in the
            // one view that most needs a rev counter.
            HudOnTop.Apply(gameObject);
        }

        /// <summary>
        /// The gear, in its own little LCD panel beside the rev counter.
        ///
        /// Deliberately NOT a fourth thing printed on a dial face: it is the
        /// cockpit binnacle's gear box, at the twin-dial layout's size, on the
        /// same bottom margin the dials sit on and hard against the tach's
        /// inboard edge so it reads as part of that instrument's group rather
        /// than as a stray label in the middle of the screen.
        ///
        /// Built only when the touch panel is hidden, because the shifter knob
        /// carries the gear when it is not — two of them on screen is how a HUD
        /// ends up with a readout that disagrees with itself.
        /// </summary>
        void MakeGearPanel(Font font, int radius, int margin, Vector2 anchor, Vector2 tachPos)
        {
            panelRoot = new GameObject("Panel", typeof(RectTransform));
            panelRoot.transform.SetParent(transform, false);
            var prt = (RectTransform)panelRoot.transform;
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;

            float w = radius * 0.52f, h = radius * 0.68f;
            float gap = radius * 0.14f;
            // Inboard of the tach and standing on the dials' own bottom margin,
            // which is not the dial's centre line — the box is a third of the
            // dial's height, so hanging it off that line would float it halfway
            // up the screen.
            float x = tachPos.x + radius + gap + w * 0.5f;
            float y = margin + h * 0.5f;

            // SMOKED, like the two faces it sits between (GearSmokeAlpha). The
            // blue AT/MT header stays solid: it is a printed label strip, and
            // it is what tells a glance that this box is the gearbox's.
            Color smoke = new Color(0f, 0f, 0f, GearSmokeAlpha);
            Color smokeEdge = ClusterBulbs.Dim;
            smokeEdge.a = GearEdgeAlpha;
            var box = Box(panelRoot.transform, "Gear", anchor, new Vector2(x, y),
                          new Vector2(w, h), smoke, smokeEdge, GearHead);
            var mode = Label(box, font, Mathf.Max(8, Mathf.RoundToInt(h * 0.22f)),
                             Color.white, new Vector2(0.5f, 1f),
                             new Vector2(0f, -h * GearHeadFrac * 0.5f));
            mode.text = car != null && car.manualMode ? "MT" : "AT";
            // The digit in the bulb's Lit colour, which is what the ticks
            // beside it are printed in. LcdInk is near-black, and near-black
            // on smoked glass over a night road is no digit at all.
            gearText = Label(box, font, Mathf.Max(12, Mathf.RoundToInt(h * 0.46f)),
                             ClusterBulbs.Lit, new Vector2(0.5f, 0f),
                             new Vector2(0f, h * (1f - GearHeadFrac) * 0.5f));
            AddLegibilityOutline(gearText);
            gearText.text = "1";
        }

        // ------------------------------------------------------------------
        //  Cockpit binnacle
        // ------------------------------------------------------------------
        /// <summary>
        /// The instruments as the driver sees them: one large rev counter with
        /// a digital speed readout to its left and the gear to its right.
        ///
        /// This is the layout of the cockpit this view is modelled on, and the
        /// reason it is a different layout rather than the same two dials moved
        /// is that a dashboard is not a HUD. Two matched dials in the bottom
        /// corners is a thing drawn OVER a picture of a car; a binnacle is a
        /// thing sitting ON one, and the moment the frame has a dashboard in it
        /// the corners are no longer where instruments live.
        ///
        /// The speedometer becoming a number is the same decision every car
        /// maker made for the same reason: a needle is read at a glance for
        /// RATE — how close to the shift, how close to the limit — and revs
        /// are the thing you steer by. Road speed is a number you check.
        /// </summary>
        void BuildCockpit(Font font, int frame, int radius, int margin,
                          float tachMax, float redFrac, bool touch)
        {
            cockpitRoot = new GameObject("Cockpit", typeof(RectTransform));
            cockpitRoot.transform.SetParent(transform, false);
            var crt = (RectTransform)cockpitRoot.transform;
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;

            float s = cockpitGaugeScale;
            float speedW = frame * 0.215f * s, speedH = frame * 0.105f * s;
            float gearW = frame * 0.115f * s, gearH = frame * 0.150f * s;
            float gap = frame * 0.022f * s;
            float capH = frame * 0.045f * s;

            Vector2 anchor;
            float speedCx, tachCx, gearCx;
            float groupCy = margin + radius;

            // Where the steering wheel is, if the cabin drew one. REPORTED, not
            // re-derived: which fraction of the frame the wheel occupies is a
            // property of the artwork and lives on CockpitView, and a second
            // copy of it here would be wrong the first time that art changed.
            float wheelR = CockpitView.WheelRadius;
            bool behindWheel = wheelR > 1f;

            if (behindWheel)
            {
                // In the opening at the TOP of the rim, which is where a driver
                // actually reads a binnacle from — you look through the wheel,
                // not over it. Centred on the column rather than on the frame,
                // because the wheel is not centred in the frame either.
                float total = speedW + gearW + gap * 2f + radius * 2f;
                float mid = CockpitView.WheelCentre.x;
                groupCy = CockpitView.WheelCentre.y + wheelR * 0.74f;
                // Never off the bottom of the screen, whatever the artwork does
                // with the wheel: an instrument you cannot see is worse than one
                // in the wrong place.
                groupCy = Mathf.Max(groupCy, margin + radius);

                anchor = new Vector2(0f, 0f);
                float x0 = mid - total * 0.5f;
                speedCx = x0 + speedW * 0.5f;
                tachCx = x0 + speedW + gap + radius;
                gearCx = x0 + speedW + gap * 2f + radius * 2f + gearW * 0.5f;
            }
            else if (touch)
            {
                // The band the touch panel leaves between the wheel and the
                // pedals, REPORTED by the panel rather than guessed at — see
                // the twin-dial layout above, which learned this the hard way.
                float left = TouchControls.WheelInset;
                float right = FrameWidth() - TouchControls.PedalsInset;
                float band = Mathf.Max(220f, right - left);
                radius = Mathf.Min(radius,
                    Mathf.FloorToInt((band - speedW - gearW - gap * 2f - 16f) * 0.5f));
                radius = Mathf.Max(14, radius);

                float total = speedW + gearW + gap * 2f + radius * 2f;
                float x0 = (left + right) * 0.5f - total * 0.5f;
                anchor = new Vector2(0f, 0f);
                speedCx = x0 + speedW * 0.5f;
                tachCx = x0 + speedW + gap + radius;
                gearCx = x0 + speedW + gap * 2f + radius * 2f + gearW * 0.5f;
            }
            else
            {
                // Right-hand corner of the dash, counting inward. Same visual
                // order — speed, revs, gear — just measured from the other side.
                anchor = new Vector2(1f, 0f);
                gearCx = -(margin + gearW * 0.5f);
                tachCx = -(margin + gearW + gap + radius);
                speedCx = -(margin + gearW + gap * 2f + radius * 2f + speedW * 0.5f);
            }
            // Behind the wheel the group sits on the column, clear of either
            // side; otherwise its top (the dial, or the gear box and the AT/MT
            // head over it) is what the pizza cam has to stand above.
            if (!behindWheel)
                RightCornerTop = groupCy + Mathf.Max(radius, gearH * 0.5f + capH);

            // Everything shares one CENTRE LINE. The dial is the tallest thing
            // in the group, so the two boxes hang off its middle rather than
            // off the bottom of the frame — which is the same thing only while
            // the group is sitting on the bottom of the frame, and it is not
            // once it moves up behind a steering wheel.
            // Coolant, in the bottom of the binnacle's one dial — the same
            // place the twin-dial layout puts it, and the same place the
            // cockpit this copies has it. There is no speedometer dial here to
            // hang a fuel gauge under, and fuel is already printed over the
            // world by the HUD's bar, so the driver's seat loses nothing.
            tach = new Dial(cockpitRoot.transform, font, "Tach", anchor,
                            new Vector2(tachCx, groupCy), radius,
                            tachMax, 1000f, LabelStep(tachMax, 1000f, radius, 1f / 1000f),
                            1f / 1000f, "x1000", redFrac, "C", "H",
                            subHighIsDanger: true, translucent: false);

            // Speed: a light LCD with dark digits, zero-padded to three, and
            // the unit under it. The padding is not decoration — a readout that
            // is sometimes two characters wide and sometimes three moves its
            // own digits about while you are trying to read them.
            speedText = Readout(font, "Speed", anchor,
                                new Vector2(speedCx, groupCy + capH * 0.5f),
                                new Vector2(speedW, speedH), Mathf.RoundToInt(speedH * 0.62f),
                                LcdInk, LcdFace, LcdEdge);
            var cap = Label(cockpitRoot.transform, font,
                            Mathf.Max(9, Mathf.RoundToInt(frame * 0.026f * s)),
                            ClusterBulbs.Lit, anchor,
                            new Vector2(speedCx, groupCy - speedH * 0.5f));
            cap.text = SpeedUnits.Label;

            // Gear, in its own box with the transmission type over it. AT or MT
            // from the car itself: the game has both, and which one you are
            // driving changes what the number under it means.
            var gearBox = Box(cockpitRoot.transform, "Gear", anchor,
                              new Vector2(gearCx, groupCy),
                              new Vector2(gearW, gearH), LcdFace, LcdEdge, GearHead);
            var mode = Label(gearBox, font, Mathf.Max(8, Mathf.RoundToInt(gearH * 0.20f)),
                             Color.white, new Vector2(0.5f, 1f),
                             new Vector2(0f, -gearH * GearHeadFrac * 0.5f));
            mode.text = car != null && car.manualMode ? "MT" : "AT";
            gearText = Label(gearBox, font, Mathf.Max(12, Mathf.RoundToInt(gearH * 0.46f)),
                             LcdInk, new Vector2(0.5f, 0f),
                             new Vector2(0f, gearH * (1f - GearHeadFrac) * 0.5f));
            gearText.text = "1";
        }

        /// <summary>Fraction of the gear box taken by its blue header.</summary>
        const float GearHeadFrac = 0.36f;

        static Color LcdInk => new Color32(0x0D, 0x11, 0x0D, 0xFF);
        static Color GearHead => new Color32(0x1E, 0x5F, 0xC8, 0xFF);

        /// <summary>
        /// The LCD's own face. A positive display: dark digits on a pale panel,
        /// which is what the cockpit this copies has and what almost every trip
        /// computer of the era had. At night it does not invert — a backlit LCD
        /// glows behind its digits and the digits stay black — so the face
        /// takes the bulb's colour and dims rather than turning into a lit
        /// number on a dark field.
        /// </summary>
        static Color LcdFace
        {
            get
            {
                var day = new Color32(0xCE, 0xD2, 0xC4, 0xFF);
                if (!ClusterBulbs.Backlit) return day;
                Color lit = ClusterBulbs.Lit;
                var night = Color.Lerp(day, lit, 0.4f) * 0.62f;
                // OPAQUE. Color * float scales ALPHA along with the colour, so
                // this used to return a face at 62% alpha: a night LCD that let
                // the dashboard art show through it, which nobody asked for and
                // no LCD does. The dimming was meant for the light, not for the
                // glass. Only the cockpit reads this now (the HUD gear panel is
                // smoked on purpose, see GearSmokeAlpha), and there the box
                // sits on the dash and must be solid.
                night.a = 1f;
                return night;
            }
        }

        static Color LcdEdge => ClusterBulbs.Backlit
            ? (Color)new Color32(0x2A, 0x2C, 0x2A, 0xFF)
            : (Color)new Color32(0x3A, 0x3D, 0x38, 0xFF);

        /// <summary>A framed box with an optional coloured header strip. The
        /// edge is the caller's: the cockpit passes <see cref="LcdEdge"/> (an
        /// LCD's bezel), and the smoked HUD panel passes the bulb's Dim colour,
        /// which is the colour of the dial bezels beside it.</summary>
        Transform Box(Transform parent, string name, Vector2 anchor, Vector2 pos,
                      Vector2 size, Color face, Color edge, Color header)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.sprite = BakeBox(Mathf.RoundToInt(size.x), Mathf.RoundToInt(size.y),
                                 face, edge, header, GearHeadFrac);
            img.raycastTarget = false;
            return go.transform;
        }

        /// <summary>A framed box with a number in it, returned as the number.</summary>
        Text Readout(Font font, string name, Vector2 anchor, Vector2 pos, Vector2 size,
                     int fontSize, Color ink, Color face, Color edge)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(cockpitRoot.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.sprite = BakeBox(Mathf.RoundToInt(size.x), Mathf.RoundToInt(size.y),
                                 face, edge, face, 0f);
            img.raycastTarget = false;

            var t = Label(go.transform, font, fontSize, ink, new Vector2(0.5f, 0.5f), Vector2.zero);
            t.text = "0";
            return t;
        }

        static Text Label(Transform parent, Font font, int size, Color colour,
                          Vector2 anchor, Vector2 pos)
        {
            var go = new GameObject("T");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.color = colour;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(90f, 26f);
            return t;
        }

        /// <summary>
        /// A rounded panel: face, one-pixel edge, and a header strip across the
        /// top when <paramref name="headFrac"/> is non-zero. Rasterised rather
        /// than sliced from a 9-patch because there are two of them per race
        /// and both are small — and because a sliced sprite would need an
        /// asset, which is one more thing to keep in step with the palette.
        /// </summary>
        static Sprite BakeBox(int w, int h, Color face, Color edge, Color head, float headFrac)
        {
            const int SS = 2;
            w = Mathf.Max(8, w) * SS; h = Mathf.Max(8, h) * SS;
            float r = Mathf.Min(w, h) * 0.14f;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[w * h];
            Color32 f = face, e = edge, hd = head;
            var clear = new Color32(0, 0, 0, 0);
            float band = h * (1f - headFrac);      // texture row 0 is the BOTTOM

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Max(r - (x + 0.5f), (x + 0.5f) - (w - r)));
                    float dy = Mathf.Max(0f, Mathf.Max(r - (y + 0.5f), (y + 0.5f) - (h - r)));
                    float sdf = Mathf.Sqrt(dx * dx + dy * dy) - r;
                    int i = y * w + x;
                    if (sdf > 0f) { px[i] = clear; continue; }
                    px[i] = sdf > -2f * SS ? e
                          : (headFrac > 0f && y >= band ? hd : f);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        /// <summary>
        /// Tear down a subtree, TEXTURES INCLUDED. Every panel sprite here is
        /// rasterised at build time and owned by nothing else, so dropping the
        /// GameObject alone leaks one texture per box — and this rebuilds every
        /// time the player switches into or out of the cockpit, which over a
        /// race is a lot of boxes. Same reasoning as <see cref="Dial.Destroy"/>.
        /// </summary>
        static void KillTree(GameObject root)
        {
            if (root == null) return;
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                var sp = img.sprite;
                if (sp == null) continue;
                var tex = sp.texture;
                img.sprite = null;
                Kill(sp);
                Kill(tex);
            }
            Kill(root);
        }

        /// <summary>
        /// Top of the speedometer scale, rounded UP to a whole number of tick
        /// steps past the car own top speed. Rounding to the speed itself would
        /// put the last numeral hard against the end stop, and pinning every car
        /// to one scale would give a 380 km/h supercar and a 130 km/h hatchback
        /// the same needle sweep for completely different speeds.
        ///
        /// Works in whatever unit the player reads, so a dial in MPH is a real
        /// MPH dial — ticks every 20, numerals on round numbers — rather than a
        /// km/h dial with converted labels. Its bounds are the same two speeds
        /// either way, just expressed in the current unit.
        /// </summary>
        static float SpeedScale(float top)
        {
            // A guard, not a limit. It was 440 km/h and every MPH dial ended at
            // 280, which a built car on the old (29% high) catalog cleared
            // with its needle on the end stop. The fastest thing in the game
            // now is ~395 km/h (a built Cizeta V16T, a GT-One on its own), and
            // 480 holds that with the 8% the scale adds and a numeral to spare.
            float want = Mathf.Clamp(top * 1.08f,
                                     SpeedUnits.FromKmh(140f), SpeedUnits.FromKmh(480f));
            float step = SpeedTick(want);
            return Mathf.Ceil(want / step) * step;
        }

        /// <summary>Tick spacing. Twenty is the step a road-car speedometer is
        /// marked in in both units; the coarser rungs are for the top of the
        /// catalog, where a 20 unit tick would ring the dial in hairs.</summary>
        static float SpeedTick(float max) => SpeedUnits.Mph
            ? (max <= 180f ? 20f : max <= 240f ? 30f : 40f)
            : (max <= 280f ? 20f : max <= 360f ? 40f : 50f);

        /// <summary>
        /// Double the tick step until the numerals fit round the dial.
        ///
        /// A tachometer to 9000 wants nine of them and a speedometer to 260
        /// wants thirteen, and at a sixth of a 240-line frame there is room for
        /// about five. Crowding them in is the ring of illegible smudges the
        /// first version had -- the TICKS still go every 1000 and every 20, so
        /// the dial keeps all of its resolution and only loses numbers nobody
        /// could read anyway.
        /// </summary>
        static float LabelStep(float max, float tickStep, int radius, float labelScale)
        {
            // Bounded by ANGLE and by how many characters a numeral has — not
            // by radius. The type scales with the dial, so a bigger dial fits
            // the same count LARGER rather than fitting more of them. The old
            // radius/7 read as "room" and handed a 108-unit speedometer fourteen
            // three-digit labels, which collided into a smear across the top of
            // the sweep at exactly the size that was supposed to make it
            // readable. The radius term stays as the lower bound because the
            // font size has a floor: a very small dial does get proportionally
            // larger type and so genuinely fits fewer.
            int digits = Mathf.Max(1, Mathf.CeilToInt(Mathf.Log10(max * labelScale + 1f)));
            int room = Mathf.Clamp(Mathf.RoundToInt(radius / 7f), 4, 14 - digits * 2);
            float step = tickStep;
            while (max / step > room) step *= 2f;
            return step;
        }

        /// <summary>Frame height in canvas units — the cluster's own
        /// screen-resolution canvas, not the 240-line framebuffer. Every size in
        /// here is a fraction of it.</summary>
        int FrameHeight()
        {
            var rt = transform as RectTransform;
            float h = rt != null ? rt.rect.height : 0f;
            // The canvas scaler's reference height, which is what this
            // rect resolves to once a layout pass has run. Only reachable
            // before the first one.
            if (h < 32f) h = 720f;
            return Mathf.RoundToInt(h);
        }

        /// <summary>Frame width in the same units. Falls back to 16:9 of the
        /// height, which is the aspect the game is played at most often.
        /// </summary>
        float FrameWidth()
        {
            var rt = transform as RectTransform;
            float w = rt != null ? rt.rect.width : 0f;
            return w < 32f ? FrameHeight() * 16f / 9f : w;
        }

        /// <summary>Whether the dials were switched off for a driver who got
        /// out. Nobody reads a rev counter from the pavement, and the cluster
        /// over the walk-around view was half of "while walking, I still see
        /// race car UI on screen".</summary>
        bool wasOnFoot;

        void Update()
        {
            bool foot = OnFoot.ForecourtMode.OnFoot;
            if (foot != wasOnFoot)
            {
                wasOnFoot = foot;
                // Everything this component draws — both dials, the gear panel,
                // the cockpit binnacle — is a direct child of its own transform,
                // so the toggle catches whatever layout happens to be built.
                foreach (Transform child in transform) child.gameObject.SetActive(!foot);
            }
            if (foot) return;

            // Cheap every frame — three float compares when nothing changed —
            // and the only way either a bulb picked from the pause menu or the
            // car spec RaceHandoffApplier lands during Start reaches a cluster
            // that has already drawn itself.
            Build();
            if (car == null) return;

            float rpm = car.currentRPM;
            if (rpmFlutter)
            {
                // Perlin rather than Random: a broken tacho drifts, it does not
                // buzz. Same treatment the camera shake gets.
                flutter += Time.deltaTime;
                rpm *= 0.55f + Mathf.PerlinNoise(flutter * 2.3f, 0f) * 0.9f;
            }
            float shown = SpeedUnits.FromKmh(Mathf.Abs(car.speedKmh));

            if (tach != null) tach.SetValue(hideGauges ? 0f : rpm);
            // No speedometer dial in the cockpit — the LCD below is the
            // speedometer there.
            if (speedo != null) speedo.SetValue(hideGauges ? 0f : shown);

            // The two small gauges. A dead cluster parks these as well: they
            // are on the same loom as the dials they sit in, so a fault that
            // takes the instruments takes all four needles, not two.
            if (tach != null)
            {
                tach.SetSub(hideGauges || Temp == null ? 0f : Temp.Gauge);
                tach.SetSubAlarm(!hideGauges && Temp != null && Temp.Overheating);
            }
            if (speedo != null)
                speedo.SetSub(hideGauges || Tank == null ? 0f : Tank.percent * 0.01f);

            int gear = Mathf.Clamp(car.currentGear + 1, 0, GearNames.Length - 1);
            string g = hideGauges ? "-" : GearNames[gear];
            if (gearText != null && gearText.text != g) gearText.text = g;

            // Only the cockpit binnacle prints a speed now; the twin dials have
            // a needle for it. Guarded rather than left to the change-gate
            // below, because ToString on an int allocates before anything can
            // compare it and this runs every frame of every race.
            if (speedText == null) return;

            int speed = Mathf.RoundToInt(shown);
            // Zero-padded, always: this readout is a fixed box on the binnacle
            // and a number in a fixed box has to be a fixed width, or its digits
            // shuffle sideways every time the speed crosses a hundred. The
            // unpadded form was for the one under the needle, which had the
            // needle beside it to be read against — and which is gone.
            string s = hideGauges ? "---" : Mathf.Min(speed, 999).ToString("000");
            if (speedText.text != s) speedText.text = s;
        }

        // ------------------------------------------------------------------
        //  One dial
        // ------------------------------------------------------------------
        class Dial
        {
            readonly GameObject root;
            readonly RectTransform needle;
            readonly float max;
            float lastDeg = float.NaN;
            /// <summary>A HUD dial drawn over the world (smoked face, haloed
            /// marks, outlined text, rimmed needles) rather than the cockpit's
            /// solid binnacle. Set first in the constructor, because Label reads
            /// it for every piece of text the dial makes.</summary>
            readonly bool translucent;

            /// <summary>The little gauge in the bottom of the face — fuel under
            /// the speedometer, coolant under the tachometer. Null on a dial too
            /// small to carry one.</summary>
            RectTransform subNeedle;
            float lastSubDeg = float.NaN;

            /// <summary>
            /// Sweep, in the SVG convention the source cluster uses: angles
            /// measured clockwise from east, starting at 145 (lower left),
            /// passing through 270 (straight up) and ending at 395 (lower
            /// right).
            ///
            /// 250 degrees, not the 270 this started with, and the difference
            /// is measured rather than chosen. On the cluster photographed for
            /// this the speedometer's 0 sits about 35 degrees below the
            /// horizontal and its 160 mirrors it, which is a shade over 250
            /// between them; a 270 sweep runs both ends another ten degrees
            /// down the face, where the first and last numeral tuck under the
            /// hub and stop being read.
            ///
            /// It also widens the empty wedge across the bottom from 90 degrees
            /// to 110, and that wedge is where the fuel and temperature gauges
            /// live.
            /// </summary>
            public const float StartDeg = 145f;
            public const float SweepDeg = 250f;
            // Everything below is a fraction of the dial radius, so the two
            // instruments stay the same instrument at different sizes.
            const float BezelIn = 0.965f;
            const float TickOut = 0.95f, TickIn = 0.84f, MinorIn = 0.885f;
            const float LabelR = 0.70f;
            const float RedIn = 0.905f, RedOut = 0.95f;
            const float NeedleLen = 0.90f, NeedleTail = 0.17f, NeedleHalf = 0.05f;
            const float HubR = 0.10f;

            // ---- smoked glass: the HUD twin dials only ---------------------
            //
            // The owner's NFS (2015) reference, 2026-09-21: keep the analog
            // needles, make the dials see-through. The cockpit binnacle does
            // not use any of this; see GaugeCluster.CockpitFaceAlpha.
            /// <summary>
            /// Inner edge of the bezel ring on a SMOKED face, thinner than the
            /// solid face's <see cref="BezelIn"/>. At 0.965 a grey ring 3.5% of
            /// the radius thick reads as a frame around a hole once the face
            /// inside it is see-through. At 0.975 it reads as the edge of a
            /// lens. It is drawn at <see cref="SmokedBezelAlpha"/> so it is not
            /// the one solid thing on a sheet of glass.
            /// </summary>
            const float SmokedBezelIn = 0.975f, SmokedBezelAlpha = 0.90f;
            /// <summary>
            /// A dark HAIRLINE outside the smoked bezel, from 0.99 of the radius
            /// to the edge, black at 0.55. A grey bezel over a pale sky has no
            /// edge at all, and this is the edge. On the solid face the black
            /// plate already gave it one.
            /// </summary>
            const float HairlineIn = 0.99f, HairlineAlpha = 0.55f;
            /// <summary>
            /// The LEGIBILITY HALO: how far from a mark the face under it is
            /// darkened, in canvas units (SS times that in the texture, the
            /// same way the tick half-width is given). 2.2 units on each side
            /// of a tick 2.2 units wide, so the tick stands in a dark stripe
            /// three times its own width.
            ///
            /// This is how every see-through gauge that can still be read does
            /// it. Contrast is only guaranteed if the mark carries its own
            /// dark surround. Glass that is merely tinted gives white ticks over
            /// a white sky, and no amount of general tint fixes that without
            /// turning the glass black again.
            /// </summary>
            const float HaloPx = 2.2f;
            /// <summary>The halo by day: black at 0.55, laid OVER the smoked
            /// fill (never in place of it, or the halo would be paler than the
            /// darkest part of the face it sits on).</summary>
            const float HaloDayAlpha = 0.55f;
            /// <summary>The halo at night, once the bulb is lit: the bulb's own
            /// colour at 0.22 over black at 0.35. An illuminated dial GLOWS
            /// around its marks, where the light leaks through the printing,
            /// and a black halo there would read as a smudge instead.</summary>
            const float HaloGlowAlpha = 0.22f, HaloGlowUnder = 0.35f;
            /// <summary>Per-pixel kinds for the halo pass: a face pixel may be
            /// darkened, a mark pixel is what it is darkened around, and
            /// everything else (outside the disc, the bezel, the hairline) is
            /// left alone.</summary>
            const byte KindFace = 1, KindMark = 2;
            /// <summary>
            /// The dark rim around a HUD needle, in canvas units (so SS texels
            /// in the texture), and its alpha. It is drawn OUTSIDE the white
            /// kite, so the Image's tint still turns the core red and leaves the
            /// rim black. A plain red needle vanishes over a sunset or under a
            /// sodium lamp, which is exactly where the night look puts it.
            /// </summary>
            const int NeedleRim = 1;
            const byte NeedleRimAlpha = 150;

            // ---- the sub-gauge in the bottom of the face -------------------
            //
            // The 250 degree sweep leaves a 110 degree wedge across the bottom
            // of every dial with nothing in it, and that wedge is where a real
            // cluster puts its fuel and temperature gauges — E and F under the
            // speedometer, C and H under the tachometer.
            //
            // THE MARKS STAND ON THE DIAL'S OWN CIRCUMFERENCE, AND EACH ONE AIMS
            // AT THE SUB-GAUGE'S PIN. Both halves of that are measured off the
            // photograph this copies, and it took four versions to hold them
            // at the same time.
            //
            // The first drew a little arc of its own around the sub-hub, sized
            // independently, so it floated in the wedge and read as a bracket.
            // The second put the marks out in the dial's tick band — right —
            // but struck them about the DIAL's centre, so every mark pointed
            // somewhere the needle does not turn. The third fixed the aim by
            // retreating to a private arc about the pin, a third of the radius
            // across, and that one shipped: a needle a quarter of the dial
            // long, five marks in fifty pixels, the letters hard against the
            // end marks, and the whole group huddled at the bottom of the face
            // touching the rim at one point. It was reported as "too small,
            // cramped, and not on the circumference", which is an inventory
            // rather than a complaint.
            //
            // What the photograph shows is neither arc. The outer end of every
            // fuel mark is ON the speedometer's ring — the same ring the 20
            // and the 40 stand on — and each mark LEANS, because it is drawn
            // along the line from the fuel needle's pin out through that point
            // of the ring. Position from the big circle, direction from the
            // small pin. The second version had the first half and the third
            // had the second; they are not alternatives.
            //
            // With the marks on the ring the needle can be as long as the pin
            // is far from it, so the tip runs along the inside of the ring the
            // way the big needle's does, and the group is as large as the
            // wedge allows instead of as small as an arc about the pin must be.
            // The price is that the pin is NOT equidistant from the ring: 0.41
            // of the radius straight down, 0.47 at the ends of the throw, so a
            // needle of one length falls a little short at E and at F. The
            // real one does exactly that, and its end marks are the long ones
            // for the same reason these are — they reach in to meet it.
            /// <summary>
            /// Depth of the sub-hub below the dial centre.
            ///
            /// 0.54, measured: on the photograph the fuel needle's pin is 139
            /// pixels under the hub of a speedometer 256 in radius, and the
            /// coolant pin is the same fraction under the tachometer's. It was
            /// 0.62 while the scale was an arc about the pin, because that arc
            /// had to be pushed down the face to touch the rim at all. With
            /// the marks on the rim in their own right the pin goes back where
            /// the instrument has it, and the needle gets the length that
            /// frees.
            /// </summary>
            const float SubHubY = 0.54f;
            /// <summary>
            /// Half the needle's throw, in degrees about the PIN — which is
            /// also the angle the end marks lean at, because every mark lies
            /// along the needle that points to it.
            ///
            /// Forty, measured the same way as the depth: the E and F marks
            /// stand 19 degrees of bearing either side of straight down as
            /// seen from the dial's centre, and from a pin 0.54 down, the ray
            /// to that point of the ring leaves at 40. The dead wedge is 55
            /// degrees of bearing a side, so the group keeps clear of the main
            /// sweep's first and last numeral with room to spare.
            /// </summary>
            const float SubHalfSweep = 40f;
            /// <summary>
            /// How many marks, ends included: E, the quarters, the half, F —
            /// what the fuel gauge in the photograph carries.
            ///
            /// Out on the ring they stand a seventh of the radius apart, sixteen
            /// units on a 108-unit dial. On the private arc the same five were
            /// packed into fifty pixels and only survived the mipmap because
            /// there were not seven of them.
            /// </summary>
            const int SubTickCount = 5;
            /// <summary>
            /// How far each mark runs in from the ring, along its own line to
            /// the pin, as a fraction of the dial radius. The end marks are the
            /// main sweep's numbered ticks over again and the quarters are its
            /// minor ones, so the bottom of the ring is the same ring; the half
            /// sits between the two. The photograph keeps that hierarchy, and
            /// it is what lets the scale be read without counting: the two
            /// long marks ARE empty and full.
            /// </summary>
            const float SubEndTick = TickOut - TickIn, SubMinorTick = TickOut - MinorIn,
                        SubMidTick = (SubEndTick + SubMinorTick) * 0.5f;
            /// <summary>The same hierarchy in weight, as multiples of the main
            /// sweep's tick half-width. The ends are drawn heavier as well as
            /// longer because they are crossed at an angle by a needle resting
            /// on them, and a hairline under a needle is no mark at all.
            /// </summary>
            const float SubEndWiden = 1.6f, SubMidWiden = 1.25f;
            /// <summary>
            /// The needle, as a fraction of the dial radius: as long as the pin
            /// is far from the tick ring, less a hair, so that pointing straight
            /// down its tip stops just inside the ring the way the big needle's
            /// does. 0.40 — the photograph's is 0.41 — where the arc about the
            /// pin could only ever give it 0.23.
            ///
            /// DERIVED, because the two numbers it comes from are the ones that
            /// get tuned, and a needle that pokes through the bezel after the
            /// pin moves is the kind of fault nobody looks for.
            /// </summary>
            const float SubNeedleLen = TickOut - SubHubY - 0.01f;
            /// <summary>
            /// The needle's tail and half-width, and the hub cap it turns on.
            /// Fractions of the DIAL radius, given directly, and that is
            /// deliberate.
            ///
            /// They used to be the main needle's own numbers through one
            /// scale factor, on the reasoning that the same needle smaller is
            /// the same needle. It is not: a needle a quarter the length came
            /// out a quarter as WIDE, a two-pixel stub on a four-pixel blob. A
            /// small gauge's needle is proportionally fatter than a big one's
            /// on every real cluster, and its boss is barely smaller at all —
            /// on the photograph the two caps are within a tenth of each
            /// other, which is what 0.09 against the main hub's 0.10 is. The
            /// tail is 1.4x the cap, so the counterweight shows past it instead
            /// of being swallowed by it.
            /// </summary>
            const float SubNeedleTail = 0.13f, SubNeedleHalf = 0.032f, SubHubR = 0.09f;
            /// <summary>
            /// Where the two letters sit, as a polar offset about the PIN —
            /// 0.31 of the dial radius out and 62 degrees round from straight
            /// down — and how big they are.
            ///
            /// That lands each one above the inboard end of its own long mark,
            /// beside the hub: where the photograph has them. The angle is set
            /// by the needle, not by eye. At its stop the needle lies along 40
            /// degrees; a bold capital 0.17 of the radius high has a corner
            /// 0.09 from its own centre; and 22 degrees of separation at this
            /// distance is 0.116, which is that corner, the blade's half-width
            /// there, and a finger of daylight. Any closer and a cold engine —
            /// every start there is — parks the needle across its own C.
            ///
            /// 0.17 against the numerals' 0.19. They were 0.125 and hugging
            /// the marks, and on the photograph E and F are very nearly the
            /// size of the 20 and the 40.
            /// </summary>
            const float SubLabelDeg = 62f, SubLabelR = 0.31f, SubLabelSize = 0.17f;
            /// <summary>
            /// Below this radius the sub-gauge is left off entirely.
            ///
            /// A dial of 40 units carries 6-unit letters and marks under six
            /// units apart, which is not a gauge, it is grit — and the touch
            /// layout clamps the radius to whatever band the wheel and pedals
            /// leave, so in a narrow window it really does get that small. The
            /// same discipline LabelStep applies to the numerals: drop what
            /// cannot be read rather than draw it anyway.
            /// </summary>
            const int SubMinRadius = 46;

            /// <summary>
            /// How far it is from the sub-gauge's pin to the dial's tick ring
            /// along a ray <paramref name="deg"/> from straight down, as a
            /// fraction of the dial radius.
            ///
            /// The one piece of geometry the group needs now that its marks
            /// stand on a circle the pin is not the centre of. A point t along
            /// the ray is at (t sin, -d - t cos) from the dial centre; set its
            /// length to the ring's radius and the positive root is this. 0.41
            /// straight down, 0.474 at the 40 degree stops.
            /// </summary>
            static float SubReach(float deg)
            {
                float a = deg * Mathf.Deg2Rad;
                float s = SubHubY * Mathf.Sin(a);
                return -SubHubY * Mathf.Cos(a) + Mathf.Sqrt(TickOut * TickOut - s * s);
            }

            /// <summary>
            /// Where the sub-gauge's reading <paramref name="frac"/> (0 the
            /// left letter, 1 the right) meets the ring, as a bearing about the
            /// DIAL centre: degrees from straight down, positive to the right.
            /// 18.6 at the end stops. For the self-test, which has to know
            /// where H is on the ring to prove nothing red runs on past it.
            /// </summary>
            internal static float SubRingBearing(float frac)
            {
                float deg = Mathf.Lerp(-SubHalfSweep, SubHalfSweep, Mathf.Clamp01(frac));
                float reach = SubReach(deg), a = deg * Mathf.Deg2Rad;
                return Mathf.Atan2(reach * Mathf.Sin(a), SubHubY + reach * Mathf.Cos(a)) * Mathf.Rad2Deg;
            }

            /// <summary>True when this dial actually got its sub-gauge — false
            /// on one too small to carry one, see <see cref="SubMinRadius"/>.
            /// Read by the constructor before the face is baked, because the
            /// sub-gauge's scale is part of that texture.</summary>
            public bool HasSub { get; private set; }

            public Dial(Transform parent, Font font, string name, Vector2 anchor, Vector2 centre,
                        int radius, float max, float tickStep, float labelStep, float labelScale,
                        string unit, float redlineFrac,
                        string subLow = null, string subHigh = null, bool subHighIsDanger = false,
                        bool translucent = false)
            {
                this.max = max;
                this.translucent = translucent;
                // Decided BEFORE the face is baked, because the sub-gauge's
                // scale is part of that texture rather than a sprite laid over
                // it. Two rasterisers that both had to agree about where the
                // marks went would only ever have agreed on the day they were
                // written.
                HasSub = subLow != null && subHigh != null && radius >= SubMinRadius;

                root = new GameObject(name, typeof(RectTransform));
                root.transform.SetParent(parent, false);
                var rt = (RectTransform)root.transform;
                rt.anchorMin = rt.anchorMax = anchor;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = centre;
                rt.sizeDelta = new Vector2(radius * 2, radius * 2);

                var faceGO = new GameObject("Face");
                faceGO.transform.SetParent(root.transform, false);
                var face = faceGO.AddComponent<Image>();
                face.sprite = BakeFace(radius, max, tickStep, redlineFrac, HasSub, subHighIsDanger,
                                       translucent);
                var frt = face.rectTransform;
                frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
                frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;

                // Numerals. Few enough to be Text components — six to nine per
                // dial — and text is the one thing not worth rasterising by hand.
                for (float v = 0f; v <= max + 0.01f; v += labelStep)
                {
                    float f = v / max;
                    Vector2 dir = Direction(StartDeg + SweepDeg * f);
                    var t = Label(font, Mathf.Max(8, Mathf.RoundToInt(radius * 0.19f)),
                                  ClusterBulbs.Lit, dir * (radius * LabelR));
                    t.text = Mathf.RoundToInt(v * labelScale).ToString();
                }

                var unitText = Label(font, Mathf.Max(7, Mathf.RoundToInt(radius * 0.15f)),
                                     ClusterBulbs.Dim, new Vector2(0f, radius * 0.33f));
                unitText.text = unit;

                // Whole pixels, and the SAME whole pixels the rasteriser used.
                // Drawing a 4-pixel needle into a 3.5-unit rect resamples it,
                // and a point-filtered resample of a shape one pixel wide at the
                // tip is a needle that flickers between two and none as it
                // sweeps.
                int len = Mathf.Max(2, Mathf.RoundToInt(radius * NeedleLen));
                int tail = Mathf.Max(1, Mathf.RoundToInt(radius * NeedleTail));
                int wide = Mathf.Max(3, Mathf.RoundToInt(radius * NeedleHalf * 2f));
                // The HUD needle's dark rim lies OUTSIDE the kite, so the
                // sprite, its rect and the pivot all grow by it on every side.
                // The blade itself stays exactly the size it always was. Zero
                // on the cockpit binnacle, whose needle is unchanged.
                int rim = translucent ? NeedleRim : 0;

                var needleGO = new GameObject("Needle");
                needleGO.transform.SetParent(root.transform, false);
                var img = needleGO.AddComponent<Image>();
                img.sprite = BakeNeedle(len, tail, wide, rim);
                img.color = ClusterBulbs.Needle;
                needle = img.rectTransform;
                needle.anchorMin = needle.anchorMax = new Vector2(0.5f, 0.5f);
                // Pivot ON THE HUB, which is where the tail meets the blade —
                // not at the middle of the sprite. Rotating a needle about its
                // own centre swings the tip round a circle instead of sweeping
                // the dial.
                needle.pivot = new Vector2(0.5f, (tail + rim) / (float)(len + tail + rim * 2));
                needle.anchoredPosition = Vector2.zero;
                needle.sizeDelta = new Vector2(wide + rim * 2, len + tail + rim * 2);

                var hubGO = new GameObject("Hub");
                hubGO.transform.SetParent(root.transform, false);
                var hub = hubGO.AddComponent<Image>();
                hub.sprite = BakeDisc(Mathf.RoundToInt(radius * HubR), ClusterBulbs.Dim,
                                      ClusterBulbs.Face);
                hub.rectTransform.anchorMin = hub.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                hub.rectTransform.anchoredPosition = Vector2.zero;
                hub.rectTransform.sizeDelta = new Vector2(radius * HubR * 2f, radius * HubR * 2f);

                if (HasSub) MakeSubGauge(font, radius, subLow, subHigh);

                SetValue(0f);
            }

            /// <summary>
            /// Fit the small gauge into the bottom of this face: two letters
            /// either side of a needle on its own pin, pointing at marks that
            /// stand on the big dial's ring.
            ///
            /// Baked the same way everything else static here is — the ticks go
            /// into the face sprite rather than becoming five rotated Images —
            /// so the only thing this adds to a frame is one more transform to
            /// rotate.
            ///
            /// Builds nothing when the dial is too small; see
            /// <see cref="SubMinRadius"/>. Silent on a small dial rather than
            /// cluttered.
            /// </summary>
            void MakeSubGauge(Font font, int radius, string lowLabel, string highLabel)
            {
                // The pin hangs BELOW the dial centre, and everything that MOVES
                // or is READ in this group is placed from it: the needle turns
                // on it, the letters flank it, and the marks baked into the face
                // above lie along rays out of it. Only where those marks END is
                // measured from the dial centre — on its ring.
                var centre = new Vector2(0f, -radius * SubHubY);

                // The letters sit beside the hub, each above the inboard end of
                // its own long mark and clear of the needle at its stop; see
                // SubLabelDeg for the arithmetic. A step under the numerals on
                // the main sweep, not the two thirds they were: on a gauge
                // whose marks are out on the ring the letters are the only
                // thing that says WHICH gauge it is.
                int letter = Mathf.Max(7, Mathf.RoundToInt(radius * SubLabelSize));
                foreach (var end in new[] { (-1f, lowLabel), (1f, highLabel) })
                {
                    Vector2 dir = SubDirection(end.Item1 * SubLabelDeg);
                    var t = Label(font, letter, ClusterBulbs.Lit,
                                  centre + dir * (radius * SubLabelR));
                    t.text = end.Item2;
                }

                // The needle keeps the main one's SHAPE — the kite, the taper,
                // the counterweight behind the pivot — but not its proportions,
                // and that is the fix rather than an inconsistency. Deriving
                // every dimension from one scale factor made a needle a quarter
                // the length also a quarter the width: a two-pixel stub on a
                // four-pixel bead. Length is what reaches the ring from the pin;
                // width, tail and cap are given directly, because a small
                // gauge's needle is proportionally fatter than a big one's on
                // every real cluster.
                int len = Mathf.Max(3, Mathf.RoundToInt(radius * SubNeedleLen));
                int tail = Mathf.Max(2, Mathf.RoundToInt(radius * SubNeedleTail));
                int wide = Mathf.Max(3, Mathf.RoundToInt(radius * SubNeedleHalf * 2f));
                // The same dark rim as the big needle on a HUD dial, and the
                // same growth of sprite, rect and pivot to make room for it.
                int rim = translucent ? NeedleRim : 0;

                var nGO = new GameObject("SubNeedle");
                nGO.transform.SetParent(root.transform, false);
                var img = nGO.AddComponent<Image>();
                img.sprite = BakeNeedle(len, tail, wide, rim);
                img.color = ClusterBulbs.Needle;
                // Remembered rather than re-read from ClusterBulbs when the
                // alarm clears: the bulb can be changed under a built cluster,
                // and putting back a colour the needle never had is how a
                // temperature alarm would repaint somebody's amber dials white.
                subNeedleInk = img.color;
                subNeedle = img.rectTransform;
                subNeedle.anchorMin = subNeedle.anchorMax = new Vector2(0.5f, 0.5f);
                subNeedle.pivot = new Vector2(0.5f, (tail + rim) / (float)(len + tail + rim * 2));
                subNeedle.anchoredPosition = centre;
                subNeedle.sizeDelta = new Vector2(wide + rim * 2, len + tail + rim * 2);

                var hubGO = new GameObject("SubHub");
                hubGO.transform.SetParent(root.transform, false);
                var hub = hubGO.AddComponent<Image>();
                // The same hub cap, nine tenths the size of the main one, and
                // built AFTER the needle for the same reason the big one is:
                // the cap is what the needle passes THROUGH. Drawn over the top
                // it swallows the counterweight tail and leaves a blade
                // emerging from a rim, which is the whole look. A solid dot
                // instead — which this had — is a needle stuck ONTO a bead.
                //
                // Nine tenths, up from three quarters, up from a third. The
                // pin of a temperature gauge is a visible boss on a real
                // cluster, near enough a match for the one above it; at a
                // third of the main hub this was four pixels across and read as
                // a smudge the needle happened to start at.
                float hubR = radius * SubHubR;
                hub.sprite = BakeDisc(Mathf.RoundToInt(hubR), ClusterBulbs.Dim, ClusterBulbs.Face);
                hub.rectTransform.anchorMin = hub.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                hub.rectTransform.anchoredPosition = centre;
                hub.rectTransform.sizeDelta = new Vector2(hubR * 2f, hubR * 2f);

                SetSub(0.5f);
            }

            /// <summary>Move the sub-needle. 0 is the left-hand letter (empty,
            /// cold), 1 the right-hand one (full, hot). The rotation IS the
            /// printed scale: every mark is baked along the ray this needle
            /// lies on when it points to it, so the two cannot disagree however
            /// far the ring they end on is from being centred on the pin.
            /// </summary>
            public void SetSub(float f)
            {
                if (subNeedle == null) return;
                float deg = Mathf.Lerp(-SubHalfSweep, SubHalfSweep, Mathf.Clamp01(f));
                if (Mathf.Abs(deg - lastSubDeg) < 0.2f) return;
                lastSubDeg = deg;
                // The sprite points along its own +Y, which a rotation of theta
                // sends to (-sin, cos); the sweep wants (sin deg, -cos deg),
                // measured from straight down and positive to the right. Those
                // two agree at theta = 180 + deg and at NO other sign
                // convention — 180 - deg also puts the needle straight down at
                // half scale and sweeps it the wrong way from there, which is
                // invisible in any still of a gauge sitting at rest.
                subNeedle.localRotation = Quaternion.Euler(0f, 0f, 180f + deg);
            }

            /// <summary>
            /// Turn the sub-needle red, or put it back.
            ///
            /// The face paints no band for it (only H's end mark is red), so
            /// this is the gauge's whole way of saying you are IN the damage:
            /// the needle is the thing the eye is already on, and the only
            /// moment this matters is a moment the driver is looking at the
            /// road.
            /// </summary>
            public void SetSubAlarm(bool on)
            {
                if (subNeedle == null || subAlarm == on) return;
                subAlarm = on;
                var img = subNeedle.GetComponent<Image>();
                if (img != null) img.color = on ? ClusterBulbs.Red : subNeedleInk;
            }

            /// <summary>
            /// Where the sub-needle is actually POINTING, 0-1.
            ///
            /// Read back off its transform rather than from the last value it
            /// was handed, because the play check that asks this is asking
            /// whether the needle MOVED — a cached copy of the argument would
            /// answer that question with the question. -1 on a dial with no
            /// sub-gauge.
            /// </summary>
            public float SubFraction
            {
                get
                {
                    if (subNeedle == null) return -1f;
                    float z = subNeedle.localRotation.eulerAngles.z;
                    if (z > 180f + SubHalfSweep + 1f) z -= 360f;
                    return Mathf.InverseLerp(-SubHalfSweep, SubHalfSweep, z - 180f);
                }
            }

            bool subAlarm;
            Color subNeedleInk = Color.white;

            /// <summary>Unit vector for a sub-gauge angle, measured from
            /// straight down and positive toward the right-hand letter.</summary>
            static Vector2 SubDirection(float deg)
            {
                float r = deg * Mathf.Deg2Rad;
                return new Vector2(Mathf.Sin(r), -Mathf.Cos(r));
            }

            // A dial used to be able to carry a digital number under its needle,
            // and both of the ones here did — the gear in the tach and the
            // speed in the speedo. Gone, with the two constants that placed it:
            // the face has its scale, its unit and its little gauge, and a
            // fourth thing on it is what made the wedge at the bottom look
            // full. The gear moved to a panel of its own beside the tach; the
            // speed did not move anywhere, because the needle is the readout.

            Text Label(Font font, int size, Color colour, Vector2 pos)
            {
                var go = new GameObject("T");
                go.transform.SetParent(root.transform, false);
                var t = go.AddComponent<Text>();
                t.font = font;
                t.fontSize = size;
                t.fontStyle = FontStyle.Bold;
                t.color = colour;
                t.alignment = TextAnchor.MiddleCenter;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                var rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = pos;
                rt.sizeDelta = new Vector2(60f, 20f);
                // Every piece of text a dial makes comes through here (the
                // numerals, the unit caption, the sub-gauge letters), so on a
                // smoked HUD face they all get their dark edge from this one
                // line. See AddLegibilityOutline.
                if (translucent) AddLegibilityOutline(t);
                return t;
            }

            public void SetValue(float v)
            {
                float f = Mathf.Clamp01(v / Mathf.Max(max, 1f));
                // The dial runs clockwise from lower left in a coordinate system
                // whose Y points DOWN; UI rotation is counter-clockwise about a
                // Y that points UP. Both flips together are this one line, and
                // getting it wrong gives a needle that sweeps the right arc
                // backwards — which looks almost right until you accelerate.
                //
                // The constant is 270 - StartDeg and it was written out as 135
                // while StartDeg was 135, which is the same number for two
                // unrelated reasons: the sprite points up, and the sweep starts
                // at lower left. Spelled out, it moves with the sweep.
                float deg = 270f - StartDeg - SweepDeg * f;
                if (Mathf.Abs(deg - lastDeg) < 0.1f) return;
                lastDeg = deg;
                needle.localRotation = Quaternion.Euler(0f, 0f, deg);
            }

            /// <summary>
            /// Tear the dial down, TEXTURES INCLUDED. Every sprite here is
            /// rasterised at build time and owned by nothing else, so dropping
            /// the GameObject alone leaks three textures per dial — and this
            /// rebuilds whenever the bulb changes or the player swaps car,
            /// which on a long session is a lot of dials.
            /// </summary>
            public void Destroy()
            {
                if (root == null) return;
                foreach (var img in root.GetComponentsInChildren<Image>(true))
                {
                    var sp = img.sprite;
                    if (sp == null) continue;
                    var tex = sp.texture;
                    img.sprite = null;
                    Kill(sp);
                    Kill(tex);
                }
                Kill(root);
            }

            static void Kill(Object o)
            {
                if (o == null) return;
                if (Application.isPlaying) Object.Destroy(o);
                else Object.DestroyImmediate(o);
            }

            /// <summary>Unit vector for a sweep angle, in UI space (Y up) from
            /// the source cluster SVG convention (Y down).</summary>
            static Vector2 Direction(float deg)
            {
                float r = deg * Mathf.Deg2Rad;
                return new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
            }

            // --------------------------------------------------------------
            //  Rasterisers
            // --------------------------------------------------------------
            /// <summary>
            /// Face, bezel, ticks and redline arc in one texture, drawn per
            /// PIXEL rather than as geometry.
            ///
            /// Drawn at SS times the size it occupies on the canvas, mipmapped
            /// and filtered, so the dial resolves cleanly at whatever scale
            /// factor the device's canvas ends up with.
            ///
            /// It used to be baked at exactly the layout size and point
            /// filtered, on the reasoning that then nothing resamples and a tick
            /// is either on or off. That was true while the layout size WAS a
            /// framebuffer pixel count. On a scaling canvas it is not: the same
            /// 166 units is 249 device pixels on one phone and 332 on another,
            /// so a texture baked at the layout size always resamples — the only
            /// choice is whether it does so raggedly or cleanly.
            ///
            /// <paramref name="translucent"/> bakes the HUD's SMOKED face
            /// (2026-09-21): the fill runs from HudFaceAlpha at the centre to
            /// HudFaceRimAlpha at the tick band, the bezel is thinner and
            /// slightly see-through with a dark hairline outside it, and every
            /// mark gets a legibility halo in a second pass (BakeHalos). Without
            /// it the bake is exactly the solid face it always was, texel for
            /// texel. The cockpit binnacle depends on that.
            /// </summary>
            internal static Sprite BakeFace(int radius, float max, float tickStep, float redlineFrac,
                                   bool subGauge, bool subHighIsDanger, bool translucent)
            {
                const int SS = 2;
                radius *= SS;
                int size = radius * 2;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
                {
                    filterMode = FilterMode.Trilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                var px = new Color32[size * size];
                Color32 face = ClusterBulbs.Face, lit = ClusterBulbs.Lit,
                        dim = ClusterBulbs.Dim, red = ClusterBulbs.Red;
                var clear = new Color32(0, 0, 0, 0);
                // The solid face's alpha, spelled as the constant the self-test
                // pins rather than trusted to the palette's 0xFF. It is the same
                // 255 either way, so the cockpit bake does not move.
                face.a = (byte)Mathf.RoundToInt(Mathf.Clamp01(CockpitFaceAlpha) * 255f);

                // Smoked-face parts. On a solid face the bezel is the full Dim
                // colour from BezelIn out and there is no hairline, exactly as
                // before. `kind` exists only for the halo pass, so a solid bake
                // allocates nothing extra.
                float bezelIn = translucent ? SmokedBezelIn : BezelIn;
                Color32 bezel = dim;
                if (translucent) bezel.a = (byte)Mathf.RoundToInt(SmokedBezelAlpha * 255f);
                var hairline = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(HairlineAlpha * 255f));
                byte[] kind = translucent ? new byte[size * size] : null;

                float tickCount = max / Mathf.Max(tickStep, 1f);
                float tickSpanDeg = SweepDeg / Mathf.Max(tickCount, 1f);
                // Half a tick mark, in degrees at the tick band. Constant WIDTH
                // matters more than constant angle: a mark specified in degrees
                // is two pixels wide on a small dial and five on a large one.
                float halfPx = 1.1f * SS;

                // The sub-gauge's marks: the angle each lies along, about the
                // PIN, and how far along that ray the dial's ring is. Worked
                // out once per mark here instead of once per pixel below — it
                // is a square root, and the wedge is a third of the face.
                float subStep = SubHalfSweep * 2f / (SubTickCount - 1);
                float subEndK = (SubTickCount - 1) * 0.5f;
                var subReach = new float[SubTickCount];
                for (int t = 0; t < SubTickCount; t++)
                    subReach[t] = SubReach((t - subEndK) * subStep);

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x + 0.5f - radius, dy = y + 0.5f - radius;
                        float r = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                        int i = y * size + x;
                        if (r > 1f) { px[i] = clear; continue; }

                        // Back to the source convention: clockwise from east
                        // with Y down.
                        float deg = Mathf.Repeat(Mathf.Atan2(-dy, dx) * Mathf.Rad2Deg, 360f);
                        float along = Mathf.Repeat(deg - StartDeg, 360f);
                        bool onSweep = along <= SweepDeg;

                        if (translucent && r >= HairlineIn) { px[i] = hairline; continue; }
                        if (r >= bezelIn) { px[i] = bezel; continue; }

                        // From here on every `continue` that paints a pixel is
                        // a MARK, and says so in `kind` for the halo pass: the
                        // redline, the ticks and the sub-gauge's marks. A mark
                        // that forgot would bake without a halo,
                        // which is invisible until it is over a bright sky.
                        if (onSweep && redlineFrac > 0f && r >= RedIn && r <= RedOut
                            && along / SweepDeg >= redlineFrac)
                        {
                            px[i] = red;
                            if (kind != null) kind[i] = KindMark;
                            continue;
                        }

                        if (onSweep && r <= TickOut)
                        {
                            // Distance to the nearest tick, as an arc length in
                            // pixels at this radius.
                            float k = Mathf.Round(along / tickSpanDeg);
                            float offDeg = Mathf.Abs(along - k * tickSpanDeg);
                            float offPx = offDeg * Mathf.Deg2Rad * r * radius;
                            bool major = offPx <= halfPx && r >= TickIn;
                            // Minor ticks halfway between the numbered ones.
                            // They cost nothing and they are most of what makes
                            // a dial read as an instrument instead of as a pie
                            // chart with a stick on it.
                            float offHalf = Mathf.Abs(offDeg - tickSpanDeg * 0.5f)
                                            * Mathf.Deg2Rad * r * radius;
                            bool minor = !major && offHalf <= halfPx * 0.75f && r >= MinorIn;
                            if (major || minor)
                            {
                                px[i] = major ? lit : dim;
                                if (kind != null) kind[i] = KindMark;
                                continue;
                            }
                        }

                        // The sub-gauge's scale, in the empty wedge the main
                        // sweep leaves across the bottom.
                        //
                        // TWO POLAR FRAMES AT ONCE, and which question goes to
                        // which frame is the whole design. WHERE a mark ends is
                        // asked of the dial: r <= TickOut, the same ring every
                        // tick above stands on, so the bottom of the face is
                        // one circle of marks rather than a big scale with a
                        // small one parked under it. WHICH WAY it runs is asked
                        // of the pin: a mark is the pixels within half a width
                        // of a ray out of the pin, for as far in from the ring
                        // along that ray as its length. Ask both of the dial
                        // and the marks point at a centre the needle does not
                        // turn on; ask both of the pin and they leave the ring.
                        //
                        // Still gated on being off the main sweep: the group
                        // reaches 19 degrees of bearing either side of straight
                        // down and the dead wedge is 55, so this can never fire
                        // on a pixel the sweep wants — but it costs nothing and
                        // it says so.
                        if (subGauge && !onSweep && r <= TickOut)
                        {
                            // Offset from the pin. MIND THE SIGN: dx/dy are
                            // texture-space and Y is UP there — the Y-down
                            // convention the angles above work in is something
                            // `deg` puts them into with its -dy, not something
                            // dy already is. The pin hangs below the centre, so
                            // it is at dy = -SubHubY, and getting that backwards
                            // silently puts the whole scale 180 degrees away and
                            // bakes no ticks at all rather than wrong ones.
                            float qx = dx, qy = dy + SubHubY * radius;
                            float qr = Mathf.Sqrt(qx * qx + qy * qy) / radius;
                            // Straight DOWN from the pin is zero, positive
                            // toward the right-hand letter — the same
                            // convention SubDirection and SetSub use, so the
                            // baked marks and the live needle cannot drift.
                            float qdeg = Mathf.Atan2(qx, -qy) * Mathf.Rad2Deg;
                            int t = Mathf.RoundToInt(qdeg / subStep + subEndK);
                            if (t >= 0 && t < SubTickCount)
                            {
                                // Long, heavy and lit at the ends; a step down
                                // at the half; short and dim at the quarters.
                                // The same hierarchy the main sweep keeps
                                // between its numbered ticks and its minor ones,
                                // and the reason the scale reads without being
                                // counted.
                                bool end = t == 0 || t == SubTickCount - 1;
                                bool mid = !end && t * 2 == SubTickCount - 1;
                                float run = end ? SubEndTick : mid ? SubMidTick : SubMinorTick;
                                float wide = halfPx * (end ? SubEndWiden : mid ? SubMidWiden : 1f);
                                float offPx = Mathf.Abs(qdeg - (t - subEndK) * subStep)
                                            * Mathf.Deg2Rad * qr * radius;
                                if (qr >= subReach[t] - run && offPx <= wide)
                                {
                                    // HOT is red on the instrument this copies
                                    // and FULL is not: one end of a temperature
                                    // gauge is a warning and neither end of a
                                    // fuel gauge is. Red for the reason the
                                    // redline is — it is not the bulb's to tint.
                                    //
                                    // AND THAT MARK IS THE ONLY RED ON THE
                                    // SCALE. There used to be a red band along
                                    // the ring from the damage line to H; the
                                    // owner, 2026-09-21: "there should not be
                                    // a circumferential red line in the temp
                                    // gauge. Just the red tick mark." Trouble
                                    // is said by the needle turning red
                                    // (SetSubAlarm) and the TEMP lamp, not by
                                    // paint. The self-test scans the ring.
                                    px[i] = subHighIsDanger && t == SubTickCount - 1 ? red
                                          : end || mid ? lit : dim;
                                    if (kind != null) kind[i] = KindMark;
                                    continue;
                                }
                            }
                        }

                        if (kind == null) { px[i] = face; continue; }

                        // SMOKED GLASS: the palette's face colour, with an
                        // alpha that climbs with the square of the radius from
                        // HudFaceAlpha in the middle to HudFaceRimAlpha at the
                        // tick band, and holds there out to the bezel.
                        float ramp = Mathf.Clamp01(r / FaceRampR);
                        var smoked = face;
                        smoked.a = (byte)Mathf.RoundToInt(255f *
                            Mathf.Lerp(HudFaceAlpha, HudFaceRimAlpha, ramp * ramp));
                        px[i] = smoked;
                        kind[i] = KindFace;
                    }
                }

                if (kind != null)
                {
                    // By day a black halo. At night, once the bulb is lit, a
                    // faint glow of the bulb's own colour over a lighter black.
                    // Composed once here, so the per-pixel pass lays down one
                    // colour.
                    Color halo = ClusterBulbs.Backlit
                        ? Over(WithAlpha(ClusterBulbs.Lit, HaloGlowAlpha),
                               new Color(0f, 0f, 0f, HaloGlowUnder))
                        : new Color(0f, 0f, 0f, HaloDayAlpha);
                    BakeHalos(px, kind, size, HaloPx * SS, halo);
                }

                tex.SetPixels32(px);
                tex.Apply();
                return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            }

            /// <summary>
            /// The legibility halo: darken every smoked-face pixel within
            /// <paramref name="reach"/> texels of a mark by laying
            /// <paramref name="halo"/> OVER it.
            ///
            /// Distance comes from a two-pass CHAMFER transform (1 along an
            /// axis, sqrt 2 on a diagonal): one sweep down the texture and one
            /// back up, each reading four neighbours already visited. That is
            /// O(pixels) whatever the reach. The obvious version, which tests a
            /// disc of neighbours around every pixel, is O(pixels x 60) at this
            /// reach, and on a 430-texel face that is the difference between a
            /// couple of milliseconds and a visible hitch. This bake runs on
            /// the main thread whenever the cluster rebuilds: when the view,
            /// the bulb or the hour changes. A chamfer distance is at most
            /// about 8% longer than the true one, which is a quarter of a texel
            /// at this reach and cannot be seen after the mipmap.
            ///
            /// Laid over the fill rather than written in its place: near the
            /// rim the smoked face is already 0.62 alpha, and a 0.55 halo
            /// "set" there would make the pixels round a tick PALER than the
            /// glass they sit on. The halo's edge fades over one texel centred
            /// on the reach, so it is anti-aliased before the mipmap ever sees
            /// it.
            /// </summary>
            static void BakeHalos(Color32[] px, byte[] kind, int size, float reach, Color halo)
            {
                int n = size * size;
                const float Far = 1e6f, Diag = 1.41421356f;
                var d = new float[n];
                for (int i = 0; i < n; i++) d[i] = kind[i] == KindMark ? 0f : Far;

                // Down the texture: left, and the three below.
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int i = y * size + x;
                        float v = d[i];
                        if (v == 0f) continue;
                        if (x > 0 && d[i - 1] + 1f < v) v = d[i - 1] + 1f;
                        if (y > 0)
                        {
                            int j = i - size;
                            if (d[j] + 1f < v) v = d[j] + 1f;
                            if (x > 0 && d[j - 1] + Diag < v) v = d[j - 1] + Diag;
                            if (x < size - 1 && d[j + 1] + Diag < v) v = d[j + 1] + Diag;
                        }
                        d[i] = v;
                    }
                }
                // And back up: right, and the three above.
                for (int y = size - 1; y >= 0; y--)
                {
                    for (int x = size - 1; x >= 0; x--)
                    {
                        int i = y * size + x;
                        float v = d[i];
                        if (v == 0f) continue;
                        if (x < size - 1 && d[i + 1] + 1f < v) v = d[i + 1] + 1f;
                        if (y < size - 1)
                        {
                            int j = i + size;
                            if (d[j] + 1f < v) v = d[j] + 1f;
                            if (x > 0 && d[j - 1] + Diag < v) v = d[j - 1] + Diag;
                            if (x < size - 1 && d[j + 1] + Diag < v) v = d[j + 1] + Diag;
                        }
                        d[i] = v;
                    }
                }

                for (int i = 0; i < n; i++)
                {
                    if (kind[i] != KindFace) continue;
                    float cover = Mathf.Clamp01(reach + 0.5f - d[i]);
                    if (cover <= 0f) continue;
                    px[i] = Over(WithAlpha(halo, halo.a * cover), px[i]);
                }
            }

            /// <summary>Porter-Duff OVER in straight (non-premultiplied)
            /// alpha, which is what these textures hold and what UI/Default
            /// blends them with.</summary>
            static Color Over(Color top, Color under)
            {
                float a = top.a + under.a * (1f - top.a);
                if (a < 1e-4f) return new Color(0f, 0f, 0f, 0f);
                float wt = top.a / a, wu = under.a * (1f - top.a) / a;
                return new Color(top.r * wt + under.r * wu,
                                 top.g * wt + under.g * wu,
                                 top.b * wt + under.b * wu, a);
            }

            static Color WithAlpha(Color c, float a) { c.a = a; return c; }

            /// <summary>The kite: a point at the tip, full width at the hub, a
            /// short counterweight tail behind it. Drawn white and tinted by the
            /// Image, so one texture serves whichever bulb is fitted.
            ///
            /// <paramref name="rim"/> (canvas units, 0 for none) adds a dark
            /// border OUTSIDE the white kite: every texel within that distance
            /// of the blade and not on it is black at NeedleRimAlpha. The Image
            /// tint multiplies it, and black times red is still black, so the
            /// HUD needle is a red blade with a dark edge. The texture grows by
            /// the rim on every side, so the blade keeps its size, and the
            /// caller grows the rect and moves the pivot to match.</summary>
            static Sprite BakeNeedle(int len, int tail, int w, int rim)
            {
                const int SS = 2;
                len *= SS; tail *= SS; w *= SS;
                int b = Mathf.Max(0, rim) * SS;
                int h = len + tail;
                int tw = w + b * 2, th = h + b * 2;
                var tex = new Texture2D(tw, th, TextureFormat.RGBA32, true)
                {
                    filterMode = FilterMode.Trilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                var px = new Color32[tw * th];
                var white = new Color32(255, 255, 255, 255);
                // A bare needle's clear texel is WHITE at zero alpha, so the
                // mipmap's edge is a fade of the blade and not a dark fringe.
                // A rimmed needle's outermost texels are dark on purpose, so
                // its clear is black at zero alpha. Otherwise the mipmap would
                // fade that rim toward white.
                var clear = b > 0 ? new Color32(0, 0, 0, 0) : new Color32(255, 255, 255, 0);
                for (int i = 0; i < px.Length; i++) px[i] = clear;
                var core = b > 0 ? new bool[tw * th] : null;
                float half = w * 0.5f;
                for (int y = 0; y < h; y++)
                {
                    // Widest at the hub, which is `tail` rows up from the bottom.
                    float hw = y < tail
                        ? half * (y + 0.5f) / Mathf.Max(tail, 1)
                        : half * (1f - (y - tail) / (float)Mathf.Max(len, 1));
                    hw = Mathf.Max(hw, 0.5f);
                    for (int x = 0; x < w; x++)
                    {
                        if (Mathf.Abs(x + 0.5f - half) > hw) continue;
                        int i = (y + b) * tw + (x + b);
                        px[i] = white;
                        if (core != null) core[i] = true;
                    }
                }

                if (b > 0)
                {
                    // The rim: a disc of radius b + 0.5 around every blade
                    // texel. Brute force is fine here. A needle is a few
                    // thousand texels, against a face's hundred and eighty
                    // thousand.
                    var rimInk = new Color32(0, 0, 0, NeedleRimAlpha);
                    float reach2 = (b + 0.5f) * (b + 0.5f);
                    for (int y = 0; y < th; y++)
                        for (int x = 0; x < tw; x++)
                        {
                            int i = y * tw + x;
                            if (core[i]) continue;
                            bool near = false;
                            for (int oy = -b; oy <= b && !near; oy++)
                            {
                                int yy = y + oy;
                                if (yy < 0 || yy >= th) continue;
                                for (int ox = -b; ox <= b; ox++)
                                {
                                    int xx = x + ox;
                                    if (xx < 0 || xx >= tw || ox * ox + oy * oy > reach2) continue;
                                    if (core[yy * tw + xx]) { near = true; break; }
                                }
                            }
                            if (near) px[i] = rimInk;
                        }
                }

                tex.SetPixels32(px);
                tex.Apply();
                return Sprite.Create(tex, new Rect(0, 0, tw, th), new Vector2(0.5f, 0.5f), 100f);
            }

            /// <summary>Hub cap: a filled disc with a rim.</summary>
            static Sprite BakeDisc(int radius, Color32 rim, Color32 fill)
            {
                const int SS = 2;
                int r = Mathf.Max(2, radius) * SS;
                int size = r * 2;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
                {
                    filterMode = FilterMode.Trilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                var px = new Color32[size * size];
                var clear = new Color32(0, 0, 0, 0);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x + 0.5f - r, dy = y + 0.5f - r;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                        px[y * size + x] = d > 1f ? clear : d > 0.62f ? rim : fill;
                    }
                tex.SetPixels32(px);
                tex.Apply();
                return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            }
        }
    }
}
