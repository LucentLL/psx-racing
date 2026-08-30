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
            float speedMax = SpeedScale(
                SpeedUnits.FromKmh(car != null ? car.topSpeedMps * 3.6f : 240f));
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
            // point as the frame allows. With them, the corners are gone and the
            // only clear ground is the middle of the bottom edge, so the dials
            // go there and are pushed apart to leave the centreline itself clear
            // rather than sitting across it.
            Vector2 tachAnchor, speedoAnchor, tachPos, speedoPos;
            if (touch)
            {
                // Centred in the band the touch panel leaves between the wheel
                // and the pedals, and the panel REPORTS that band rather than
                // this deriving it from a fraction of the frame width. A
                // fraction that clears both is a different fraction every time
                // the panel is retuned, and it was wrong within one build of
                // each of the last two changes to it.
                float left = TouchControls.WheelInset;
                float right = FrameWidth() - TouchControls.PedalsInset;
                float band = Mathf.Max(160f, right - left);
                // On a narrow screen it is the BAND that limits the dials, not
                // the frame height: two of them and a gap have to fit into it,
                // and a dial that overlaps the wheel is worse than a small one.
                radius = Mathf.Min(radius, Mathf.FloorToInt((band - 24f) * 0.25f));
                float mid = (left + right) * 0.5f;
                tachAnchor = speedoAnchor = new Vector2(0f, 0f);
                tachPos = new Vector2(mid - radius - 12f, radius + margin);
                speedoPos = new Vector2(mid + radius + 12f, radius + margin);
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
            float tachMax = tachMaxRPM;
            tach = new Dial(transform, font, "Tach", tachAnchor, tachPos, radius,
                            tachMax, 1000f, LabelStep(tachMax, 1000f, radius, 1f / 1000f), 1f / 1000f, "x1000",
                            redFrac, "C", "H");
            float sTick = SpeedTick(speedMax);
            speedo = new Dial(transform, font, "Speedo", speedoAnchor, speedoPos, radius,
                              speedMax, sTick, LabelStep(speedMax, sTick, radius, 1f), 1f,
                              SpeedUnits.Label, -1f, "E", "F");
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

            var box = Box(panelRoot.transform, "Gear", anchor, new Vector2(x, y),
                          new Vector2(w, h), LcdFace, GearHead);
            var mode = Label(box, font, Mathf.Max(8, Mathf.RoundToInt(h * 0.22f)),
                             Color.white, new Vector2(0.5f, 1f),
                             new Vector2(0f, -h * GearHeadFrac * 0.5f));
            mode.text = car != null && car.manualMode ? "MT" : "AT";
            gearText = Label(box, font, Mathf.Max(12, Mathf.RoundToInt(h * 0.46f)),
                             LcdInk, new Vector2(0.5f, 0f),
                             new Vector2(0f, h * (1f - GearHeadFrac) * 0.5f));
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
                            1f / 1000f, "x1000", redFrac, "C", "H");

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
                              new Vector2(gearW, gearH), LcdFace, GearHead);
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
                return Color.Lerp(day, lit, 0.4f) * 0.62f;
            }
        }

        static Color LcdEdge => ClusterBulbs.Backlit
            ? (Color)new Color32(0x2A, 0x2C, 0x2A, 0xFF)
            : (Color)new Color32(0x3A, 0x3D, 0x38, 0xFF);

        /// <summary>A framed box with an optional coloured header strip.</summary>
        Transform Box(Transform parent, string name, Vector2 anchor, Vector2 pos,
                      Vector2 size, Color face, Color header)
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
                                 face, LcdEdge, header, GearHeadFrac);
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
            float want = Mathf.Clamp(top * 1.08f,
                                     SpeedUnits.FromKmh(140f), SpeedUnits.FromKmh(440f));
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

        void Update()
        {
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
                tach.SetSub(hideGauges || Temp == null ? 0f : Temp.Gauge);
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
            /// <summary>Bottom dead centre, measured ALONG the sweep from its
            /// start, which is where the sub-gauge is centred. Straight down is
            /// 90 in the convention above; the +360 keeps the result positive so
            /// the sub-gauge's band never has to wrap.</summary>
            const float BottomAlong = 90f - StartDeg + 360f;

            // Everything below is a fraction of the dial radius, so the two
            // instruments stay the same instrument at different sizes.
            const float BezelIn = 0.965f;
            const float TickOut = 0.95f, TickIn = 0.84f, MinorIn = 0.885f;
            const float LabelR = 0.70f;
            const float RedIn = 0.905f, RedOut = 0.95f;
            const float NeedleLen = 0.90f, NeedleTail = 0.17f, NeedleHalf = 0.05f;
            const float HubR = 0.10f;

            // ---- the sub-gauge in the bottom of the face -------------------
            //
            // The 250 degree sweep leaves a 110 degree wedge across the bottom
            // of every dial with nothing in it, and that wedge is where a real
            // cluster puts its fuel and temperature gauges — E and F under the
            // speedometer, C and H under the tachometer.
            //
            // THE SCALE IS ON THE DIAL'S OWN CIRCUMFERENCE. The first version
            // drew a little arc of its own around the sub-hub, floating in the
            // middle of the wedge, and it read as a bracket rather than as part
            // of the instrument. On the cluster in the photograph the marks sit
            // out on the rim in the same band as the main dial's ticks, and
            // only the hub and needle are down in the middle.
            //
            // Which leaves three numbers to fix, and they are not independent:
            // how deep the pin sits, how long the needle is, and how wide the
            // printed scale is. Pick the first two and the third follows, since
            // the tip has to land in the tick band across the whole throw —
            // see the notes on each below.
            /// <summary>
            /// Depth of the sub-hub below the dial centre.
            ///
            /// It sits LOW — most of the way down the face rather than halfway
            /// — and that is off the photographs: the fuel needle pivots about
            /// two thirds of the radius below the speedometer's own hub, which
            /// is what makes it read as a small instrument tucked into the
            /// bottom of a big one. At the 0.47 this used to be, the pin was in
            /// the same part of the face as the main hub and the two competed
            /// for the middle of the dial.
            /// </summary>
            const float SubHubY = 0.62f;
            /// <summary>
            /// The sub-needle's length, as a fraction of the dial radius. Hub
            /// depth plus needle length is where the tip sits at bottom dead
            /// centre, so this number is fixed by wanting the tip in the tick
            /// band once <see cref="SubHubY"/> is chosen.
            ///
            /// It used to BE the hub depth, and equal was not a style choice
            /// but a trick: with length exactly equal to depth the tip traces a
            /// circle through the dial centre, so a scale printed on the dial's
            /// own RIM is swept by a needle turning through twice its bearing
            /// and the tip rides that rim exactly. Very tidy — and it welds the
            /// pin to the middle of the face, so the moment the pin wants to be
            /// lower the identity has to go. Dropping it costs almost nothing:
            /// the tip is 0.95 out at the centre of the scale and 0.90 at its
            /// ends, so it still never leaves the tick band, it just crosses it
            /// on a shallow arc instead of following it.
            /// </summary>
            const float SubNeedleLen = 0.33f;
            /// <summary>
            /// Half the needle's ROTATION, which is not half the scale it
            /// sweeps — the pin is off-centre, so 53.5 degrees of needle moves
            /// the tip only 18 degrees round the rim. It is the t that solves
            /// tan(SubTickHalfSweep) = L sin t / (SubHubY + L cos t) for
            /// L = <see cref="SubNeedleLen"/>, and it changes if either length
            /// above does — widening the printed scale without re-solving it
            /// leaves the needle stopping short of its own end marks.
            /// </summary>
            const float SubNeedleHalfSweep = 53.5f;
            /// <summary>
            /// Half the printed scale, as a bearing from the dial centre.
            ///
            /// Eighteen degrees. The photographs measure about thirteen, and
            /// thirteen was tried — it is honest to the reference and slightly
            /// too tight to read at the size this dial is actually drawn, where
            /// the whole scale is a couple of dozen pixels wide. Five degrees
            /// either side buys the needle visible travel without the group
            /// growing back into the second-scale-arguing-with-the-first
            /// territory that the old twenty was in.
            /// </summary>
            const float SubTickHalfSweep = 18f;
            /// <summary>Radial band the sub ticks occupy — near enough the
            /// dial's own minor tick band, because that is where they are on
            /// the instrument this copies. They are not a separate little gauge
            /// drawn in the bottom of the face; they are marks on the same rim.
            ///
            /// The inner edge is 0.85 rather than MinorIn's 0.885 for a reason
            /// that is pure geometry: an off-centre pin swings its tip along an
            /// arc that dips INWARD toward the ends of the throw, and at full
            /// deflection the tip sits at 0.858. Leave the band where the main
            /// dial's minor marks are and the needle spends the ends of its
            /// travel just inside its own scale.</summary>
            const float SubTickOut = 0.95f, SubTickIn = 0.85f;
            /// <summary>The two marks at the ends of the scale run further in
            /// and are drawn heavier — they are the ones that mean EMPTY and
            /// FULL, COLD and HOT, and on a group this small the three between
            /// them are texture rather than information. Every real gauge
            /// weights its end marks; at five identical ticks the eye has to
            /// count to find the end of the scale.</summary>
            const float SubEndTickIn = 0.80f, SubEndTickWiden = 1.7f;
            /// <summary>Where the two letters sit: six degrees past the ends of
            /// the tick group, which is what keeps them clear now that those
            /// end marks reach in to 0.80 and the letters are at the same
            /// radius. Angular clearance is doing the work here, not radial —
            /// at 0.80 of the radius six degrees is about two thirds of a
            /// letter's width of daylight.</summary>
            const float SubLabelDeg = 24f, SubLabelR = 0.80f;
            /// <summary>
            /// Below this radius the sub-gauge is left off entirely.
            ///
            /// A dial of 40 units carries a 12-unit needle and 5-unit letters,
            /// which is not a gauge, it is grit — and the touch layout clamps
            /// the radius to whatever band the wheel and pedals leave, so on a
            /// narrow phone it really does get that small. The same discipline
            /// LabelStep applies to the numerals: drop what cannot be read
            /// rather than draw it anyway.
            /// </summary>
            const int SubMinRadius = 46;

            /// <summary>True when this dial actually got its sub-gauge — false
            /// on one too small to carry one, see <see cref="SubMinRadius"/>.
            /// Read by the constructor before the face is baked, because the
            /// sub-gauge's scale is part of that texture.</summary>
            public bool HasSub { get; private set; }

            public Dial(Transform parent, Font font, string name, Vector2 anchor, Vector2 centre,
                        int radius, float max, float tickStep, float labelStep, float labelScale,
                        string unit, float redlineFrac,
                        string subLow = null, string subHigh = null)
            {
                this.max = max;
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
                face.sprite = BakeFace(radius, max, tickStep, redlineFrac, HasSub);
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

                var needleGO = new GameObject("Needle");
                needleGO.transform.SetParent(root.transform, false);
                var img = needleGO.AddComponent<Image>();
                img.sprite = BakeNeedle(len, tail, wide);
                img.color = ClusterBulbs.Needle;
                needle = img.rectTransform;
                needle.anchorMin = needle.anchorMax = new Vector2(0.5f, 0.5f);
                // Pivot ON THE HUB, which is where the tail meets the blade —
                // not at the middle of the sprite. Rotating a needle about its
                // own centre swings the tip round a circle instead of sweeping
                // the dial.
                needle.pivot = new Vector2(0.5f, tail / (float)(len + tail));
                needle.anchoredPosition = Vector2.zero;
                needle.sizeDelta = new Vector2(wide, len + tail);

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
            /// Fit the small gauge into the bottom of this face: an arc of five
            /// ticks between two letters, with a stubby needle on its own hub.
            ///
            /// Baked the same way everything else static here is — the arc and
            /// its ticks go into one sprite rather than becoming a dozen rotated
            /// Images — so the only thing this adds to a frame is one more
            /// transform to rotate.
            ///
            /// Returns false and builds nothing when the dial is too small, and
            /// the caller uses that to decide whether to move its digital
            /// readout out of the way. Silent on a small dial rather than
            /// cluttered.
            /// </summary>
            void MakeSubGauge(Font font, int radius, string lowLabel, string highLabel)
            {
                // The hub hangs BELOW the dial centre; everything else about
                // this gauge is printed on the dial's own rim and was baked
                // into the face above.
                var centre = new Vector2(0f, -radius * SubHubY);

                // The letters are placed from the DIAL centre, not from the
                // sub-hub — they belong to the ring of marks out on the rim,
                // and they bracket the ends of that group.
                int letter = Mathf.Max(8, Mathf.RoundToInt(radius * 0.15f));
                foreach (var end in new[] { (-1f, lowLabel), (1f, highLabel) })
                {
                    Vector2 dir = SubDirection(end.Item1 * SubLabelDeg);
                    var t = Label(font, letter, ClusterBulbs.Lit, dir * (radius * SubLabelR));
                    t.text = end.Item2;
                }

                // THE SAME NEEDLE, SMALLER. Every dimension comes from the main
                // needle's own constants through one scale factor, so the kite
                // keeps its proportions — the taper, the counterweight tail
                // behind the pivot, and the width at the hub relative to the
                // hub cap. Writing this one its own numbers is what made it a
                // different needle that merely had the same parts: a stub with
                // almost no tail, thinner than the big one, on a solid blob
                // instead of a hub cap.
                //
                // The factor is the two needles' lengths, and everything else —
                // taper, tail, hub cap — follows from it. It used to read
                // SubHubY / NeedleLen, which was the same number only while the
                // sub-needle was as long as its hub was deep; now that the pin
                // has dropped and the blade has not, the length is its own
                // constant and this has to ask for it by name.
                float s = SubNeedleLen / NeedleLen;
                int len = Mathf.Max(3, Mathf.RoundToInt(radius * NeedleLen * s));
                int tail = Mathf.Max(1, Mathf.RoundToInt(radius * NeedleTail * s));
                int wide = Mathf.Max(3, Mathf.RoundToInt(radius * NeedleHalf * 2f * s));

                var nGO = new GameObject("SubNeedle");
                nGO.transform.SetParent(root.transform, false);
                var img = nGO.AddComponent<Image>();
                img.sprite = BakeNeedle(len, tail, wide);
                img.color = ClusterBulbs.Needle;
                subNeedle = img.rectTransform;
                subNeedle.anchorMin = subNeedle.anchorMax = new Vector2(0.5f, 0.5f);
                subNeedle.pivot = new Vector2(0.5f, tail / (float)(len + tail));
                subNeedle.anchoredPosition = centre;
                subNeedle.sizeDelta = new Vector2(wide, len + tail);

                var hubGO = new GameObject("SubHub");
                hubGO.transform.SetParent(root.transform, false);
                var hub = hubGO.AddComponent<Image>();
                // The same hub cap on the same scale, and built AFTER the
                // needle for the same reason the big one is: the cap is what
                // the needle passes THROUGH. Drawn over the top it swallows the
                // counterweight tail and leaves a blade emerging from a rim,
                // which is the whole look. A solid dot instead — which this had
                // — is a needle stuck ONTO a bead.
                float hubR = radius * HubR * s;
                hub.sprite = BakeDisc(Mathf.RoundToInt(hubR), ClusterBulbs.Dim, ClusterBulbs.Face);
                hub.rectTransform.anchorMin = hub.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                hub.rectTransform.anchoredPosition = centre;
                hub.rectTransform.sizeDelta = new Vector2(hubR * 2f, hubR * 2f);

                SetSub(0.5f);
            }

            /// <summary>Move the sub-needle. 0 is the left-hand letter (empty,
            /// cold), 1 the right-hand one (full, hot).</summary>
            public void SetSub(float f)
            {
                if (subNeedle == null) return;
                float deg = Mathf.Lerp(-SubNeedleHalfSweep, SubNeedleHalfSweep, Mathf.Clamp01(f));
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
            /// </summary>
            static Sprite BakeFace(int radius, float max, float tickStep, float redlineFrac,
                                   bool subGauge)
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

                float tickCount = max / Mathf.Max(tickStep, 1f);
                float tickSpanDeg = SweepDeg / Mathf.Max(tickCount, 1f);
                // Half a tick mark, in degrees at the tick band. Constant WIDTH
                // matters more than constant angle: a mark specified in degrees
                // is two pixels wide on a small dial and five on a large one.
                float halfPx = 1.1f * SS;

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

                        if (r >= BezelIn) { px[i] = dim; continue; }

                        if (onSweep && redlineFrac > 0f && r >= RedIn && r <= RedOut
                            && along / SweepDeg >= redlineFrac)
                        {
                            px[i] = red; continue;
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
                            if (major) { px[i] = lit; continue; }
                            if (minor) { px[i] = dim; continue; }
                        }

                        // The sub-gauge's scale, in the empty wedge the main
                        // sweep leaves across the bottom. Off the sweep `along`
                        // runs from SweepDeg to 360, and the band this wants is
                        // BottomAlong +/- 13 — well inside that, so no angle
                        // normalising is needed here.
                        if (subGauge && !onSweep && r >= SubEndTickIn && r <= SubTickOut)
                        {
                            float rel = along - BottomAlong;
                            if (Mathf.Abs(rel) <= SubTickHalfSweep + 0.5f)
                            {
                                float step = SubTickHalfSweep * 0.5f;   // five marks
                                float k = Mathf.Round(rel / step);
                                // The outermost pair, at +/- 2 steps: longer
                                // and heavier than the three between them.
                                bool end = Mathf.Abs(k) >= 1.5f;
                                float inner = end ? SubEndTickIn : SubTickIn;
                                float wide = end ? halfPx * SubEndTickWiden : halfPx;
                                float offPx = Mathf.Abs(rel - k * step)
                                            * Mathf.Deg2Rad * r * radius;
                                if (r >= inner && offPx <= wide) { px[i] = lit; continue; }
                            }
                        }

                        px[i] = face;
                    }
                }
                tex.SetPixels32(px);
                tex.Apply();
                return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            }

            /// <summary>The kite: a point at the tip, full width at the hub, a
            /// short counterweight tail behind it. Drawn white and tinted by the
            /// Image, so one texture serves whichever bulb is fitted.</summary>
            static Sprite BakeNeedle(int len, int tail, int w)
            {
                const int SS = 2;
                len *= SS; tail *= SS; w *= SS;
                int h = len + tail;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, true)
                {
                    filterMode = FilterMode.Trilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                var px = new Color32[w * h];
                var white = new Color32(255, 255, 255, 255);
                var clear = new Color32(255, 255, 255, 0);
                float half = w * 0.5f;
                for (int y = 0; y < h; y++)
                {
                    // Widest at the hub, which is `tail` rows up from the bottom.
                    float hw = y < tail
                        ? half * (y + 0.5f) / Mathf.Max(tail, 1)
                        : half * (1f - (y - tail) / (float)Mathf.Max(len, 1));
                    hw = Mathf.Max(hw, 0.5f);
                    for (int x = 0; x < w; x++)
                        px[y * w + x] = Mathf.Abs(x + 0.5f - half) <= hw ? white : clear;
                }
                tex.SetPixels32(px);
                tex.Apply();
                return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
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
