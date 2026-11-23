using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Headlights, tail lights and brake lights.
    ///
    /// Three things, drawn three ways:
    ///   * THE LENSES are additive sprites on the bodywork (PSX/Glow), the
    ///     way a PS1 game drew a lamp. Head lenses run after dark; tail lenses
    ///     run after dark AND whenever the car is braking, day or night,
    ///     because the brake light of the car in front is the one lighting
    ///     cue that tells you what it is about to do.
    ///   * THE BEAM ON THE WORLD is real: this component pushes every lit
    ///     lamp into a global table (position, axis, spread, colour) and
    ///     PSXHeadlights.cginc lights every PSX surface from it per pixel â€” a
    ///     halogen low beam with a flat top, thirty degrees wide, seventy-odd
    ///     metres long, on the road, the kerb, the wall ahead and the car
    ///     ahead. It replaced a single additive disc laid on the tarmac.
    ///   * THE BEAM IN THE AIR is a cone mesh from each lens (PSX/Beam), the
    ///     lit volume a night drive is mostly made of.
    ///   * THE TAIL LAMPS ON THE WORLD (2026-09-21, the NFS night pass): ONE
    ///     red point light per car in <see cref="StreetLights"/>' table, at
    ///     the midpoint of the two tail lenses a hand behind the bumper - the
    ///     red wash on the wet road behind a car, on its rain spray, and on
    ///     the bonnet of the car following it. One per car, not two: the two
    ///     lenses are half a metre apart and their pools are one pool three
    ///     metres out, and the table has only four slots for tail lamps.
    ///   * THE LIT TAIL LAMPS, AUTHORED (2026-10-15, the owner's method): a
    ///     model with a lamp control map (Resources/CarLights/
    ///     &lt;modelKey&gt;_lightmask_rg.png, R running / G brake) has its lamp
    ///     texels lit in PSX/CarPaint through a property block, and no tail
    ///     sprites. See the LAMP CONTROL MAP block below.
    ///
    /// THE LAMPS ARE MEASURED OFF THE SHELL, NOT THE COLLIDER. They used to
    /// hang three centimetres outside the car's BoxCollider â€” and the baker
    /// fits every pack car's collider to 95.5% of its mesh length, so on a
    /// 4.5 m car the lens sat seven centimetres INSIDE the bumper, where the
    /// opaque body hid it. That is the whole of "brake lights seem to be
    /// missing": every pack car had its head and tail lamps buried since the
    /// day the pack was wired in, and only the built-in RX-7, whose box is
    /// verbatim, ever showed them. The shell's own mesh bounds, transformed
    /// through the body's yaw and offsets into the car's frame, are where the
    /// nose and the tail actually are.
    ///
    /// Everything hangs under the BODY root when there is one, so the lamps
    /// dive with the nose under braking and roll with it in a corner â€” and
    /// so the beam does too, which is the one thing about a real headlight
    /// that the follow-the-steering pool got wrong.
    ///
    /// Every lamp is HALOGEN. The game is set in 1999; a white LED beam is
    /// twenty years out of period, and the warm slightly-yellow light is the
    /// single cheapest thing that dates the night scenes correctly.
    /// </summary>
    public class CarLights : MonoBehaviour
    {
        public CarController car;
        public BoxCollider box;
        /// <summary>For a car with no CarBody (traffic, CarShell): the shell
        /// it wears, and how far its shell is slid along Z from the def's frame
        /// (CarShell centres the body on the collider: -colliderCenter.z).</summary>
        public CarModelDef shellDef;
        public float shellZ;
        /// <summary>A car with no CarController (city traffic, CityTraffic)
        /// says here when its driver is on the brakes.</summary>
        [System.NonSerialized] public bool externalBrake;

        static readonly List<CarLights> all = new List<CarLights>();
        /// <summary>Every enabled CarLights (read only): the traffic's mist in the rain finds its cars here (Rain2Traffic).</summary>
        internal static List<CarLights> AllLive => all;
        static bool lightsOn;
        static bool lightsDecided;

        /// <summary>Turn every car's running lights on or off. Called by
        /// <see cref="TimeOfDay.Apply"/>; the hour owns this, not the car.
        /// </summary>
        public static void SetAll(bool on)
        {
            lightsOn = on;
            lightsDecided = true;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (all[i] == null) { all.RemoveAt(i); continue; }
                all[i].Refresh();
            }
            PushGlobals();
        }

        // ------------------------------------------------------------------
        //  The halogen numbers. Colour is shared by the lens, the beam on the
        //  world and the beam in the air, or they read as three lights.
        // ------------------------------------------------------------------
        /// <summary>Tungsten-halogen: warm white, a shade of yellow, nothing
        /// like an LED's blue-white.</summary>
        public static readonly Color Halogen = new Color(1.00f, 0.86f, 0.62f);
        /// <summary>Metres of useful beam. A halogen low beam lights the
        /// road to about this; the night fog closes at 190.</summary>
        public const float BeamRange = 75f;
        /// <summary>
        /// Multiplier on the LINEAR halogen colour as it reaches the world:
        /// what one lamp puts on a road square to it, on the beam's flat
        /// plateau (PSXHeadlights.cginc: full from 10 m to 55 m).
        ///
        /// THE COLOUR PASS (C5, 2026-09-29). It was 2.0, on the colour as
        /// authored (sRGB numbers pushed as linear light: 0.87 of luminance
        /// per unit instead of 0.75, and 5,000 K instead of the bulb's
        /// 4,000 K), falling as t*sqrt(t) over the range: 3.2 of light on the
        /// road at 5 m, 0.14 at 60 m. With the night presets cut fivefold on
        /// 09-21 around it, that was the owner's "headlights so bright at
        /// night that they completely wash out anything in front of the car":
        /// a white blob to the car's roof that was gone by 40 m. Now the
        /// colour goes through .linear, as the street lamps' always did, and
        /// the beam is a flat plateau pinned by MEASUREMENT against the NFS
        /// (2015) night the owner asked for.
        ///
        /// 0.42 (review, 2026-09-29), not the first cut's 0.22. That one was
        /// pinned on ONE of the plan's numbers - the headlit road 2.3-3.6x the
        /// unlit road on the moonlit Blue Ridge stage - and missed the rest in
        /// both directions: a plateau of 0.33 of light where the plan asks
        /// 0.55-0.70, old asphalt in the beam at 36 (38-60), and on the owner's
        /// fresh #1e1e22 asphalt under street lamps (the drag strip, the
        /// circuit, downtown) a beam a reviewer could not tell from lamps-off
        /// (x1.24-1.49 over the lamp-lit road; the plan asks 1.5-3). Swept
        /// 0.22-0.50 on the protocol's night spots (colour-shots -Sets beam,
        /// PSX_BEAM_SWEEP, the cone held at 0.03): at 0.42 the plateau is 0.64
        /// of light (the plan pinned "about 0.42 for 0.62"), Blue Ridge's old
        /// asphalt 48-50 at 14-22 m, the Samuel Street concrete 134 at 14 m
        /// (120-165), fresh asphalt 24-25 (20-32), the lamp-lit circuit
        /// x1.8-3.0 and the strip x1.4-1.6 over its street lamps; 0.50 put the
        /// plateau over 0.70. Still flat - the beam's own light is the plateau
        /// from 10 m to 55 m (PSXHeadlights.cginc) - and nowhere near the old
        /// blob: the deck at 14 m is 134, where the owner's frame had 235.
        ///
        /// 0.80 (the dark-night retune, 2026-09-29). The owner, after driving
        /// it: "These lights seem pretty dull, and night is hardly dark at
        /// all", with real night drives and NFS Heat beside it. Those frames
        /// put the pool on old asphalt at Ycode 55-80 over an unlit road of
        /// 2-11 - four to five stops, the brightest thing on the road - where
        /// ours was 1.5-2.5x an unlit road the moon lit to 13-29 (colour_stats
        /// beam on the night set). The night itself came down about three
        /// stops (TimeOfDay NIGHT) and the beam came up to 0.80: old asphalt
        /// at 14-22 m about 60, the owner's fresh #1e1e22 about 35-40 (his
        /// colour, lit, not lightened), the Samuel Street concrete under
        /// the tone curve's knee - and it now tails off past 30 m
        /// (PSXHeadlights.cginc BEAM_FADE_M) instead of holding flat to 55.
        /// AT NIGHT: by day (headlights on in rain, fog and snow) the beam
        /// stays the reviewed <see cref="BeamIntensityDay"/>, blended by
        /// <see cref="BeamNight"/>, so no daylight road gets lighter for it -
        /// a low beam in daylight is barely there.
        ///
        /// 0.90 and the albedo floor (round two, 2026-09-29). The review of
        /// round one measured the pool on the owner's FRESH asphalt barely
        /// moved (drag strip 27 -> 30, Sunset City GP 28 -> 32, downtown
        /// 32-38; the references 55-80, NFS Heat's road 48), and on the
        /// circuit a street lamp's pool on the road (53) outshone the beam:
        /// only the surroundings had got darker. The road is the reason, not
        /// the beam - #1e1e22 is linear 0.013, a quarter of a real fresh
        /// road - and the beam could not be raised to meet it without
        /// whiting out Samuel Street's concrete. So at night the beam reads a
        /// dark upward texel at a real road's reflectance
        /// (PSXHeadlights.cginc BEAM_ALBEDO_FLOOR, keyed by the same
        /// <see cref="BeamNight"/>), and the plateau comes up to 0.90.
        /// </summary>
        public const float BeamIntensity = 0.90f;
        /// <summary>The beam at a daylight hour (NightFor 0): the colour
        /// review's 0.42, unchanged.</summary>
        public const float BeamIntensityDay = 0.42f;
        /// <summary>
        /// HOW MUCH OF THE NIGHT BEAM an hour gets, from its
        /// <see cref="TimeOfDay.NightFor"/> (0..1): 0 through SUNSET (0.3) and
        /// DAWN (0.5), which run headlights with the sun still up (1.05 and
        /// 0.72) and must not light a road any brighter than the reviewed day
        /// beam did; half at DUSK (0.75, the sun down, a blue twilight); all
        /// of it at NIGHT. Round one blended by NightFor itself, which put a
        /// sun-up Sunset at 0.53 and Dawn at 0.61 (the review, 2026-09-29).
        /// The shader's albedo floor takes the same number (_PSXHeadNight).
        /// </summary>
        public static float BeamNight(float night) => SmoothStep(0.5f, 1f, Mathf.Clamp01(night));
        /// <summary>The beam for an hour's night (0 day .. 1 night).</summary>
        public static float BeamIntensityFor(float night) =>
            Mathf.Lerp(BeamIntensityDay, BeamIntensity, BeamNight(night));
        /// <summary>
        /// What each lamp's GLINTS take instead (the streak of an oncoming
        /// car's lamps down a wet road, the lamps of the car behind in your
        /// paint): a glint is the LENS seen in a mirror, a light source, and
        /// the low beam's retune made the road it lights darker, not the
        /// lens. Pushed as the table's w, a multiplier on the beam's colour
        /// (PSXHeadlights.cginc), so the glints keep the brightness the night
        /// pass tuned them to (the old 2.0, now on the linear colour: 85% of
        /// the old luminance, at the bulb's own 4,000 K) while the diffuse
        /// beam comes down.
        /// </summary>
        public const float GlintIntensity = 2.0f;
        /// <summary>For the look tools only (ColourShots' beam sweep): a
        /// BeamIntensity to push instead of the constant, or 0 for the
        /// constant. Nothing in the game writes it.</summary>
        public static float BeamIntensityOverride;
        /// <summary>The intensity pushed this frame.</summary>
        public static float BeamIntensityNow => BeamIntensityOverride > 0f ? BeamIntensityOverride
            : BeamIntensityFor(Shader.GetGlobalFloat("_PSXNight"));
        /// <summary>
        /// THE BEAM IN THE AIR (PSX/Beam's _Strength) at <see cref="BeamIntensity"/>,
        /// and in proportion to it: the light a cone of air scatters back is
        /// a share of the light going through it. It was 0.45 beside the old
        /// 2.0 beam, and with the beam retuned (C5) the same cone was most of
        /// what lit the road in front of the car (measured 2026-09-29 on the
        /// drag strip: the road 14 m out barely moved across a fourfold beam
        /// sweep - the cone was drawn over it). The cone is brightest at the
        /// lens, where the beam came down fifteenfold (3.2 of light at 5 m to
        /// 0.2), so it comes down by as much: 0.03. Measured on the strip, the
        /// pool's own light over 22-58 m is flat to 1.20 without the cone,
        /// 1.53 with it at 0.03, 1.93 at 0.10 - the volume the owner asked
        /// for is kept, faint, near the lamps.
        /// </summary>
        public const float BeamConeStrength = 0.03f;
        /// <summary>For the look tools only: a cone strength to use instead,
        /// or a negative number for the rule above.</summary>
        public static float ConeStrengthOverride = -1f;
        static float ConeStrengthNow => ConeStrengthOverride >= 0f ? ConeStrengthOverride
            : BeamConeStrength * BeamIntensityNow / BeamIntensity;
        /// <summary>Half-spread of the beam, outer edge and full-strength
        /// core, in degrees.</summary>
        public const float BeamOuterDeg = 34f, BeamInnerDeg = 11f;
        /// <summary>The axis is aimed this far below the body's level (a low
        /// beam is), and this far outward on each side (two lobes that merge
        /// a few metres out).</summary>
        public const float BeamDipDeg = 1.5f, BeamToeDeg = 2f;
        /// <summary>The cone in the air: length and its end radii.</summary>
        public const float ConeLength = 13f;
        static readonly Vector2 ConeStart = new Vector2(0.10f, 0.07f);
        static readonly Vector2 ConeEnd = new Vector2(2.4f, 1.3f);

        /// <summary>
        /// The tail lamps' light on the world: a short red pool. sRGB, pushed
        /// linear by StreetLights - redder than the lens tint on purpose, since
        /// what reaches the road through a red lens is the red.
        /// </summary>
        static readonly Color TailLampColour = new Color(1.00f, 0.10f, 0.05f);
        /// <summary>Metres of red pool, and its strength with the running
        /// lights on and with the brakes on.
        ///
        /// FIVE-WATT BULBS (the colour pass, C6, 2026-09-29). They were 0.45
        /// and 1.2 - a third to a half of a street lamp's pool, where a 5 W
        /// tail bulb behind red glass gives about a tenth of one - and on the
        /// owner's night frame the road behind his car was washed pink
        /// (display .35,.27,.24). Now the running light is about a tenth of a
        /// street lamp and the brake three times that: a red wash you notice
        /// on a wet road behind a braking car, not a red floor.
        ///
        /// 0.10 and 0.30 since the dark-night retune (2026-09-29): with the
        /// moon and the ambient taken to about a sixth, the same red on the owner's
        /// Samuel Street concrete stood alone on a black road and read as a
        /// red floor again (the road 2 m behind +34 in red, the plan's cap
        /// +25). The GLOW of the lens went up instead (TailLensDim) - NFS
        /// Heat's running lamps halo, their road is only tinged.</summary>
        public const float TailLampRadius = 4.5f, TailLampDim = 0.10f, TailLampBrake = 0.30f;
        /// <summary>How far behind the lenses the light sits, so the car's
        /// own tail panel is not what it lights most.</summary>
        public const float TailLampBack = 0.25f;
        /// <summary>How far a measured lens stands off the skin it is seated
        /// on: enough not to flicker into it, not enough to see a gap.</summary>
        public const float LensProud = 0.012f;

        /// <summary>
        /// THE LAMPS ON THEIR OWN CAR (owner, 2026-10-19: "Running Lights and
        /// brake lights should illuminate the rear of the car. Currently the
        /// car stays completely black at night, despite the light"). The
        /// table's tail lamp never lights its own car (PSXLamps POINT_NEAR_M:
        /// one point between the lenses would light the boot from inside), so
        /// each LENS lights the panels round it instead, from just behind it,
        /// in PSX/CarPaint (_PSXOwnLampA/B, _PSXOwnLampCol): a short red pool
        /// on the bumper, the tail panel and the boot's edge, brighter on the
        /// brakes. With the running lights only, like the road's pool.
        /// </summary>
        public const float OwnSpillBack = 0.10f, OwnSpillRadius = 0.95f, OwnSpillDim = 0.70f, OwnSpillBrake = 2.2f;
        static readonly int OwnLampAId = Shader.PropertyToID("_PSXOwnLampA");
        static readonly int OwnLampBId = Shader.PropertyToID("_PSXOwnLampB");
        static readonly int OwnLampColId = Shader.PropertyToID("_PSXOwnLampCol");
        static MaterialPropertyBlock spillBlock;
        /// <summary>The tools' before-picture: no lens lights its own car.</summary>
        public static bool OwnSpillOff;
        Renderer spillRenderer;
        bool spillOn;
        // THE FINISH (PaintGloss): this car's shine on its paint, asked again
        // each second (a wash, a new shell) and written only when it changed.
        float glossAt = -1f, glossSet = -1f;
        Renderer glossRenderer;
        // THE RAIN AND SNOW ON THE BODY (PSXCarDrops): the air over this car
        // in its paint renderers' own frames, and how fast it is going -
        // from how far it moved, so a replay's kinematic car and the
        // traffic's servo cars blow their drops back like a driven one.
        // Written only when it changes (WindStep, WindTurnDeg), only in rain
        // or snow.
        public const float WindStep = 0.04f, WindTurnDeg = 6f, WindFullMps = 18f, WindNoneMps = 1.5f;
        /// <summary>The streaks' clock rate (cycles a second) parked and at
        /// full wind: they race back faster the faster the car goes.</summary>
        public const float BlowRateLo = 0.15f, BlowRateHi = 1.1f;
        float blowClk, blowT0, blowRate;
        readonly System.Collections.Generic.List<Renderer> paintRends = new System.Collections.Generic.List<Renderer>();
        Vector3 windLastPos, windVel, windAirSet;
        float windSet = -1f;
        bool windHavePos;
        static MaterialPropertyBlock windBlock;

        MeshRenderer[] headLens = new MeshRenderer[2];
        MeshRenderer[] tailLens = new MeshRenderer[2];
        MeshRenderer[] beams = new MeshRenderer[2];
        Transform lampRoot;
        CarBody body;
        Mesh fittedMesh;
        Vector3 fitCenter, fitSize;
        bool braking;
        // The StreetLights handle of this car's tail lamp, -1 until the first
        // update registers it. NonSerialized so a domain reload resets it to
        // -1 (the field initialiser) along with the registry it indexes -
        // never a handle into a table that no longer holds it.
        [System.NonSerialized] int tailLamp = -1;
        Vector3 tailLampPos;

        // ------------------------------------------------------------------
        //  THE LAMP CONTROL MAP (owner, 2026-10-15: "please add the R32
        //  running and brake light textures"). His method: the lit lamp is
        //  AUTHORED ART - one linear map per model, in the body atlas's UV
        //  layout, R = running intensity, G = brake intensity - and
        //  PSX/CarPaint lights exactly those texels (the LAMP_* block there).
        //  A model opts in by dropping a file at
        //  Resources/CarLights/<modelKey>_lightmask.png (RGBA, 2026-10-16: his
        //  v03 map adds B = headlamp projector core, A = clear-lens scatter)
        //  or, the first convention, <modelKey>_lightmask_rg.png (R/G only:
        //  its B/A are never read); nothing is baked into a livery, so every
        //  livery of the model shares it. A car that wears a map shows its
        //  lamps on the bodywork itself and its additive tail-lens sprites
        //  stay off (they covered the lit texels) - and its HEAD-lens sprites
        //  too when the map carries the headlamps; its red light on the road
        //  behind it and its beams stay. A car without one is untouched: no
        //  property block is ever written on its body.
        //  A shell's LENS VARIANT (CarBody.SetLensCovers, the debug bench's
        //  B) gets the same inputs on its two clear covers, and _LensLayer 1
        //  on the body, so the light splits between them (PSXLampMap.cginc).
        // ------------------------------------------------------------------
        public const string LampMapDir = "CarLights";
        /// <summary>RGBA: R running, G brake, B projector core, A lens scatter.</summary>
        public const string LampMapSuffix = "_lightmask";
        /// <summary>The first convention, R running / G brake only.</summary>
        public const string LegacyLampMapSuffix = "_lightmask_rg";
        /// <summary>An incandescent bulb: up in about 40 ms, down in about
        /// 90 (time constants of the brake lamp's ease).</summary>
        public const float BrakeLampOnTau = 0.04f, BrakeLampOffTau = 0.09f;
        /// <summary>TOOLS ONLY: 0..1 holds every car's brake lamps (and its
        /// brake light on the road) at that, for a picture; -1 = the car's own
        /// brake.</summary>
        public static float BrakeOverride = -1f;
        static readonly int LightMaskId = Shader.PropertyToID("_LightMask");
        static readonly int RunningLightsId = Shader.PropertyToID("_RunningLights");
        static readonly int BrakeId = Shader.PropertyToID("_Brake");
        static readonly int HeadLightsId = Shader.PropertyToID("_HeadLights");
        static readonly int LensLayerId = Shader.PropertyToID("_LensLayer");
        static readonly Dictionary<string, Texture2D> lampMaps = new Dictionary<string, Texture2D>();
        static readonly HashSet<string> headMaps = new HashSet<string>();
        static MaterialPropertyBlock lampBlock;
        Texture2D lampMap;
        bool lampHead;
        Renderer lampRenderer;
        /// <summary>A lens variant's two clear covers (CarBody.LensRenderer),
        /// lit with the body while B is on the car.</summary>
        readonly Renderer[] lensCovers = new Renderer[2];
        float brakeLit, pushedRun = -1f, pushedBrake = -1f;

        /// <summary>The model's lamp control map, or null (cached, misses too):
        /// the RGBA file first, then the R/G one.</summary>
        public static Texture2D LampMapFor(string modelKey)
        {
            if (string.IsNullOrEmpty(modelKey)) return null;
            if (!lampMaps.TryGetValue(modelKey, out var tex))
            {
                tex = Resources.Load<Texture2D>(LampMapDir + "/" + modelKey + LampMapSuffix);
                if (tex != null) headMaps.Add(modelKey);
                else tex = Resources.Load<Texture2D>(LampMapDir + "/" + modelKey + LegacyLampMapSuffix);
                // THE HD CAR TIER: the same map at the HD sheet's size (CarHD; PC only)
                var hd = tex != null ? CarHD.LampMap(modelKey) : null;
                if (hd != null) tex = hd;
                lampMaps[modelKey] = tex;
            }
            return tex;
        }

        /// <summary>TOOLS: forget the cached maps (the HD car tier switched).</summary>
        public static void ForgetLampMaps() { lampMaps.Clear(); headMaps.Clear(); }

        /// <summary>The model's map carries the headlamps (B/A: an RGBA
        /// <see cref="LampMapSuffix"/> file, not a legacy R/G one).</summary>
        public static bool LampMapHasHead(string modelKey) =>
            LampMapFor(modelKey) != null && headMaps.Contains(modelKey);

        /// <summary>True once this car's body wears an authored lamp map.</summary>
        public bool HasLampMap => lampMap != null && lampRenderer != null;
        /// <summary>...and that map draws the headlamps too.</summary>
        public bool HasHeadMap => HasLampMap && lampHead;
        /// <summary>The lens variant's covers are lit with the body (B).</summary>
        public bool LensCoversLit => HasLampMap && lensCovers[0] != null;

        /// <summary>The tail-lens sprites: today's rule, and never on a car
        /// whose lamps are drawn on its body.</summary>
        /// <summary>
        /// This car's running lamps, now: the hour's switch (SetAll), and
        /// nothing at all on a car nobody is in. The hour owns the lamps of a
        /// car with a driver; a driver who gets out turns the engine off and
        /// takes the key, and a parked car burning its headlamps on an empty
        /// seat was the owner's 2026-11-11 report. See
        /// <see cref="CarController.Unattended"/>.
        /// </summary>
        public bool LitNow => lightsOn && !Dark;

        /// <summary>R1a part 2a (DamageLook): the lamps a hit broke - bit 0 / 1 the
        /// left / right headlamp, 2 / 3 the left / right tail lamp. A broken lamp
        /// gives no light, no beam and no lens sprite; on a lamp-map body its lens
        /// goes dark in PSX/CarPaint (_PSXLampOut). 0 on every car never hit.</summary>
        [System.NonSerialized] public int lampOut;
        bool HeadOut(int i) => (lampOut & (1 << i)) != 0;
        bool TailOut(int i) => (lampOut & (4 << i)) != 0;
        public void SetLampOut(int mask)
        {
            if (mask == lampOut) return;
            lampOut = mask;
            Refresh();
            ApplyBrake();
        }

        /// <summary>Nobody in it (and the left-car rules on).</summary>
        bool Dark => CarController.LeftCarRules && car != null && car.Unattended;
        bool lastDark;

        bool TailSpriteOn => (!HasLampMap || LightLook.CarPointsAtStart) && (LitNow || braking);
        /// <summary>The head-lens sprites: with the lights, and never on a car
        /// whose headlamps are drawn on its body.</summary>
        bool HeadSpriteOn => LitNow && (!HasHeadMap || LightLook.CarPointsAtStart);
        /// <summary>(lightlook) a lamp-map body's lens sprites are its far point only (PSX/Glow _LensLook 2).</summary>
        Material HeadLensMat => HasHeadMap ? HeadFarMat : HeadMat;

        /// <summary>Find the map for the shell this car wears now and the
        /// renderer that draws its body; put the lamps out on a renderer that
        /// no longer wears a map (the debug bench re-dresses at runtime).</summary>
        void ResolveLampMap()
        {
            var def = Def;
            var map = def != null ? LampMapFor(def.key) : null;
            var f = map != null ? ShellFilter(def) : null;
            var r = f != null ? f.GetComponent<Renderer>() : null;
            if (lampRenderer != null && (lampRenderer != r || map == null))
                WriteLampBlock(lampRenderer, Texture2D.blackTexture, 0f, 0f, 0f, 0f);
            lampMap = r != null ? map : null;
            lampRenderer = lampMap != null ? r : null;
            lampHead = lampMap != null && LampMapHasHead(def.key);
            // B: the covers CarBody shows now (none on A, or without a map).
            for (int i = 0; i < 2; i++)
            {
                var c = lampMap != null && body != null ? body.LensRenderer(i) : null;
                if (lensCovers[i] != null && lensCovers[i] != c)
                    WriteLampBlock(lensCovers[i], Texture2D.blackTexture, 0f, 0f, 0f, 0f);
                lensCovers[i] = c;
            }
            pushedRun = pushedBrake = -1f;
            for (int i = 0; i < 2; i++)
            {
                if (tailLens[i] != null) { tailLens[i].sharedMaterial = TailLensMat; tailLens[i].enabled = TailSpriteOn && !TailOut(i); }
                if (headLens[i] != null) { headLens[i].sharedMaterial = HeadLensMat; headLens[i].enabled = HeadSpriteOn && !HeadOut(i); }
            }
        }

        /// <summary>The lamp inputs onto the body and, on B, both covers.</summary>
        void PushLamps(float run, float brake)
        {
            float head = lampHead ? run : 0f;
            float layer = lensCovers[0] != null ? 1f : 0f;
            WriteLampBlock(lampRenderer, lampMap, run, brake, head, layer);
            for (int i = 0; i < 2; i++)
                if (lensCovers[i] != null) WriteLampBlock(lensCovers[i], lampMap, run, brake, head, 0f);
            pushedRun = run;
            pushedBrake = brake;
        }

        /// <summary>Ease the brake lamp toward the brake switch and hand both
        /// inputs to the body's renderer, only when they changed.</summary>
        void UpdateLampMap(float dt)
        {
            if (!HasLampMap) return;
            float target = braking ? 1f : 0f;
            if (dt <= 0f) brakeLit = target;
            else
            {
                float tau = target > brakeLit ? BrakeLampOnTau : BrakeLampOffTau;
                brakeLit += (target - brakeLit) * (1f - Mathf.Exp(-dt / tau));
                if (Mathf.Abs(target - brakeLit) < 0.003f) brakeLit = target;
            }
            float run = LitNow ? 1f : 0f;
            if (run == pushedRun && Mathf.Abs(brakeLit - pushedBrake) < 0.002f) return;
            PushLamps(run, brakeLit);
        }

        /// <summary>Set the lamp inputs on a renderer's property block,
        /// keeping whatever else another writer put there.</summary>
        static void WriteLampBlock(Renderer r, Texture tex, float run, float brake, float head, float layer)
        {
            if (r == null) return;
            if (lampBlock == null) lampBlock = new MaterialPropertyBlock();
            r.GetPropertyBlock(lampBlock);
            lampBlock.SetTexture(LightMaskId, tex);
            lampBlock.SetFloat(RunningLightsId, run);
            lampBlock.SetFloat(BrakeId, brake);
            lampBlock.SetFloat(HeadLightsId, head);
            lampBlock.SetFloat(LensLayerId, layer);
            r.SetPropertyBlock(lampBlock);
        }

        void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
            bornAt = Time.unscaledTime;
            Refresh();
        }

        void OnDisable()
        {
            all.Remove(this);
            // The last car out clears the table. A global nothing writes is
            // a stale one, not an empty one.
            if (all.Count == 0) PushGlobals();
            // A disabled car lights nothing; the entry stays, so re-enabling
            // does not register a second one.
            if (tailLamp >= 0) StreetLights.Set(tailLamp, tailLampPos, 0f, false);
            // ...and its lamps on the bodywork go out with it.
            if (HasLampMap) PushLamps(0f, 0f);
            pushedRun = pushedBrake = -1f;
        }

        void OnDestroy()
        {
            if (tailLamp >= 0) StreetLights.Remove(tailLamp);
            tailLamp = -1;
        }

        /// <summary>
        /// Move / dim / switch this car's tail lamp in the StreetLights table,
        /// registering it the first time. Lit while the car's RUNNING LIGHTS
        /// are (the hour or the weather), brighter under braking. Not on a
        /// brake light alone: in daylight a brake lamp is a red lens and puts
        /// no visible light on the road against the sun, and a red pool under
        /// every braking car at noon would change the daytime picture the
        /// owner signed off.
        /// </summary>
        /// <summary>The lenses' light on this car's own body (see
        /// <see cref="OwnSpillBack"/>): their positions and the light onto the
        /// shell's property block every frame while lit, cleared once when not.</summary>
        void UpdateOwnSpill()
        {
            var def = Def;
            var f = def != null ? ShellFilter(def) : null;
            var r = f != null ? f.GetComponent<Renderer>() : null;
            if (spillRenderer != null && spillRenderer != r && spillOn) WriteSpill(spillRenderer, false);
            spillRenderer = r;
            if (r == null) return;
            bool lit = !OwnSpillOff && LitNow && enabled && gameObject.activeInHierarchy && tailLens[0] != null && tailLens[1] != null;
            if (!lit && !spillOn) return;
            WriteSpill(r, lit);
        }

        void UpdateGloss()
        {
            if (Time.time < glossAt && spillRenderer == glossRenderer) return;
            glossAt = Time.time + 1f;
            float g = PaintGloss.For(gameObject);
            if (Mathf.Abs(g - glossSet) < 0.005f && spillRenderer == glossRenderer) return;
            PaintGloss.WriteAll(transform, g);
            glossSet = g;
            if (glossRenderer != spillRenderer || paintRends.Count == 0)
            {
                PaintGloss.Collect(transform, paintRends);
                windSet = -1f;
            }
            glossRenderer = spillRenderer;
        }

        void UpdateWind(float dt)
        {
            Vector3 pos = transform.position;
            if (windHavePos && dt > 0f)
            {
                Vector3 v = (pos - windLastPos) / dt;
                // a teleport (respawn, replay cut) is not wind
                if (v.sqrMagnitude < 90f * 90f) windVel = Vector3.Lerp(windVel, v, 1f - Mathf.Exp(-dt / 0.25f));
            }
            windLastPos = pos;
            windHavePos = true;
            if (WeatherFx.CarRain <= 0f && WeatherFx.CarSnow <= 0f) return;
            float speed = windVel.magnitude;
            float wind = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(WindNoneMps, WindFullMps, speed));
            Vector3 air = speed > 0.5f ? -windVel / speed : -transform.forward;
            Vector3 airCar = transform.InverseTransformDirection(air);
            if (windSet >= 0f && Mathf.Abs(wind - windSet) < WindStep && Vector3.Angle(airCar, windAirSet) < WindTurnDeg) return;
            windSet = wind;
            windAirSet = airCar;
            // re-base the streaks' clock where it is now, then run it at this
            // speed's rate: continuous, so the pattern never jumps
            float now = Time.timeSinceLevelLoad;
            blowClk += blowRate * (now - blowT0);
            blowT0 = now;
            blowRate = Mathf.Lerp(BlowRateLo, BlowRateHi, wind);
            if (windBlock == null) windBlock = new MaterialPropertyBlock();
            for (int i = 0; i < paintRends.Count; i++)
            {
                var r = paintRends[i];
                if (r == null) continue;
                Vector3 a = r.transform.InverseTransformDirection(air);
                r.GetPropertyBlock(windBlock);
                windBlock.SetVector(PaintGloss.CarWindId, new Vector4(a.x, a.y, a.z, wind));
                windBlock.SetVector(PaintGloss.CarRunId, new Vector4(blowClk, blowT0, blowRate, 0f));
                r.SetPropertyBlock(windBlock);
            }
        }

        void WriteSpill(Renderer r, bool lit)
        {
            if (spillBlock == null) spillBlock = new MaterialPropertyBlock();
            r.GetPropertyBlock(spillBlock);
            if (lit)
            {
                var a = tailLens[0].transform;
                var b = tailLens[1].transform;
                Vector3 pa = a.position + a.forward * OwnSpillBack, pb = b.position + b.forward * OwnSpillBack;
                spillBlock.SetVector(OwnLampAId, new Vector4(pa.x, pa.y, pa.z, OwnSpillRadius));
                spillBlock.SetVector(OwnLampBId, new Vector4(pb.x, pb.y, pb.z, OwnSpillRadius));
                float brake = HasLampMap ? brakeLit : (braking ? 1f : 0f);
                Color c = TailLampColour.linear * Mathf.Lerp(OwnSpillDim, OwnSpillBrake, brake);
                spillBlock.SetVector(OwnLampColId, new Vector4(c.r, c.g, c.b, 1f));
            }
            else spillBlock.SetVector(OwnLampColId, Vector4.zero);
            r.SetPropertyBlock(spillBlock);
            spillOn = lit;
        }

        void UpdateTailLamp()
        {
            if (tailLens[0] == null || tailLens[1] == null) return;
            var a = tailLens[0].transform;
            var b = tailLens[1].transform;
            // The lenses face out of the back of the car, so their forward IS
            // "behind", with the body's dive and roll in it.
            tailLampPos = (a.position + b.position) * 0.5f + a.forward * TailLampBack;
            float intensity = braking ? TailLampBrake : TailLampDim;
            bool lit = LitNow && enabled && gameObject.activeInHierarchy;
            if (tailLamp < 0)
                tailLamp = StreetLights.Add(this, tailLampPos, TailLampRadius, TailLampColour,
                                            intensity, StreetLights.Kind.Point);
            StreetLights.Set(tailLamp, tailLampPos, intensity, lit);
        }

        void Start()
        {
            if (car == null) car = GetComponent<CarController>();
            if (box == null) box = GetComponent<BoxCollider>();
            Build();
            // The hour is usually applied before this car exists (the applier
            // runs in RaceManager's Start); pick up whatever it decided. Only
            // when nothing has: SetAll folds the weather in and the preset
            // alone does not.
            if (!lightsDecided) lightsOn = TimeOfDay.At(TimeOfDay.Current).lightsOn;
            Refresh();
        }

        /// <summary>
        /// Re-fit if the shell changed. CarBody can re-fit a car after this
        /// component has already built itself â€” the LifeSim hands over a grid
        /// during Start, and component Start order is undefined â€” so the fit
        /// is re-checked rather than trusted once.
        /// </summary>
        void LateUpdate()
        {
            if (ShellChanged()) Fit();
            // The driver got out or back in: the lamps switch with the key
            // (an empty car shows no brake lamp either - the lever the game
            // holds it on is not a pedal anybody is pressing).
            bool dark = Dark;
            if (dark != lastDark) { lastDark = dark; Refresh(); }

            bool nowBraking = BrakeOverride >= 0f ? BrakeOverride > 0.15f
                            : !dark && (externalBrake || car != null && (car.brakeInput > 0.15f || car.handbrakeInput));
            if (nowBraking != braking)
            {
                braking = nowBraking;
                ApplyBrake();
            }
            // The lamps drawn on the body (a car with a lamp control map):
            // the same brake switch, eased like a bulb.
            UpdateLampMap(Time.deltaTime);

            // Every frame: the car moved (StreetLights pushes at render time,
            // after every LateUpdate, so this is where it will be drawn).
            UpdateTailLamp();
            UpdateOwnSpill();
            UpdateGloss();
            UpdateWind(Time.deltaTime);

            // Once per frame for the whole field, by whichever car runs first.
            if (pushedFrame != Time.frameCount)
            {
                pushedFrame = Time.frameCount;
                PushGlobals();
                // the rain's water film on every car's paint (THE FINISH)
                Shader.SetGlobalFloat(PaintGloss.CarWetId, Seasons.CarWetNow);
                // and the drops and flakes on them (PSXCarDrops)
                Shader.SetGlobalVector(PaintGloss.CarDropsId, new Vector4(WeatherFx.CarRain, WeatherFx.CarSnow, Rain2.On && Rain2.CarGlass ? 1f : 0f, 0f));
            }
        }

        bool ShellChanged()
        {
            if (body != null && body.bodyFilter != null)
                return body.bodyFilter.sharedMesh != fittedMesh;
            return box != null && (box.center != fitCenter || box.size != fitSize);
        }

        void ApplyBrake()
        {
            var mat = TailLensMat;
            for (int i = 0; i < 2; i++)
            {
                if (tailLens[i] == null) continue;
                tailLens[i].sharedMaterial = mat;
                tailLens[i].enabled = TailSpriteOn && !TailOut(i);
            }
        }

        /// <summary>
        /// Build the lamps outside play mode and force them on or off â€” and,
        /// optionally, force the brakes on so a picture can show the brake
        /// lights, which Update would otherwise only ever show for the frames
        /// a pedal is down.
        ///
        /// Only the screenshot tool calls this. Whether a lamp ends up buried
        /// in the bodywork of one of the shells is a purely visual failure
        /// that throws nothing, and the cheapest way to catch it is a picture.
        /// </summary>
        public void PreviewBuild(bool lit, bool brake = false)
        {
            if (car == null) car = GetComponent<CarController>();
            if (box == null) box = GetComponent<BoxCollider>();
            // JOIN THE TABLE, which OnEnable does in play and nothing does
            // here: this is not ExecuteAlways, so outside play mode Unity
            // never calls OnEnable on it, the car was never in 'all', and
            // PushGlobals filled the headlight table from nobody. Every
            // edit-mode night shot - the night-look frames ("headlights 0" on
            // every one of them) and the psx_hour sweep before them - showed
            // the lenses and the cones in the air but never a BEAM ON THE
            // ROAD, the one part of a headlight the owner sees most. Play
            // mode keeps OnEnable/OnDisable as the only doors; a destroyed
            // preview car is pruned as null by the next SetAll/PushGlobals.
            if (!Application.isPlaying && !all.Contains(this)) all.Add(this);
            Build();
            lightsOn = lit;
            lightsDecided = true;
            braking = brake;
            ApplyBrake();
            Refresh();
            // The lamps on the body, at once (no ease in a still picture).
            pushedRun = pushedBrake = -1f;
            UpdateLampMap(0f);
            PushGlobals();
            // And the tail lamp's light, which LateUpdate would otherwise
            // place - and LateUpdate never runs outside play mode.
            UpdateTailLamp();
            UpdateOwnSpill();
        }

        void Refresh()
        {
            for (int i = 0; i < 2; i++)
            {
                if (headLens[i] != null) { headLens[i].sharedMaterial = HeadLensMat; headLens[i].enabled = HeadSpriteOn && !HeadOut(i); }
                if (beams[i] != null) beams[i].enabled = LitNow && !HeadOut(i);
                if (tailLens[i] != null) tailLens[i].enabled = TailSpriteOn && !TailOut(i);
            }
            PopUps();
            // The running lamps on the body switch with the hour too. (The
            // brake keeps its ease: no dt here, so it is pushed as it stands.)
            if (HasLampMap && pushedRun != (LitNow ? 1f : 0f))
                PushLamps(LitNow ? 1f : 0f, brakeLit);
            // SetAll lands here for every car: the tail lamp switches with
            // the hour now, not a frame later.
            UpdateTailLamp();
        }

        void Build()
        {
            if (headLens[0] != null) return;
            if (body == null) body = GetComponent<CarBody>();
            lampRoot = body != null && body.bodyRoot != null ? body.bodyRoot : transform;
            for (int i = 0; i < 2; i++)
            {
                headLens[i] = MakeQuad("Headlight" + i, lampRoot, HeadMat);
                tailLens[i] = MakeQuad("Taillight" + i, lampRoot, TailDimMat);
                beams[i] = MakeMesh("Beam" + i, lampRoot, ConeMesh, BeamMat);
            }
            Fit();
        }

        /// <summary>
        /// The shell's extents in the CAR's frame: the body mesh's bounds
        /// pushed through the body's yaw and offsets (CarBody.Apply sets
        /// exactly those on the body root, scale one). A 180 yaw swaps nose
        /// and tail, which is why the eight corners go through the matrix
        /// rather than the centre and size.
        /// </summary>
        CarModelDef Def => body != null && body.Def != null ? body.Def : shellDef;

        bool MeasureShell(out Bounds b)
        {
            b = default;
            var def = Def;
            if (def == null || def.bodyMesh == null) return false;
            var m = Matrix4x4.TRS(new Vector3(0f, def.bodyYOffset, def.bodyZOffset),
                                  Quaternion.Euler(0f, def.bodyYaw, 0f), Vector3.one);
            var mb = def.bodyMesh.bounds;
            b = new Bounds(m.MultiplyPoint3x4(mb.center), Vector3.zero);
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3((c & 1) == 0 ? mb.min.x : mb.max.x,
                                         (c & 2) == 0 ? mb.min.y : mb.max.y,
                                         (c & 4) == 0 ? mb.min.z : mb.max.z);
                b.Encapsulate(m.MultiplyPoint3x4(corner));
            }
            return true;
        }

        /// <summary>
        /// POP-UP LAMPS (owner, 2026-10-03: "two models for NSX. One with
        /// headlights down and one with headlights flipped-up"). A shell with a
        /// <see cref="CarModelDef.lampsUpMesh"/> wears it while the running
        /// lights are on and its plain body while they are off: the mesh on
        /// the body's own MeshFilter is swapped, at once (PSX: no animation),
        /// so it is the same one draw either way. Any car - the player, a
        /// rival (CarBody) or traffic/CarShell (shellDef) - goes through here
        /// from Refresh, i.e. on SetAll (the hour and the weather) and on
        /// enable, and from Fit, after CarBody re-fits a shell.
        /// </summary>
        void PopUps()
        {
            var def = Def;
            if (def == null || def.lampsUpMesh == null) return;
            var f = ShellFilter(def);
            if (f == null) return;
            var want = def.BodyMeshFor(LitNow);
            // Only ever trade one of this shell's two bodies for the other.
            if (f.sharedMesh == want || (f.sharedMesh != def.bodyMesh && f.sharedMesh != def.lampsUpMesh)) return;
            f.sharedMesh = want;
            // ...and its clear covers with it (CarCovers: the raised lamps' own)
            CarCovers.Dress(f, def);
            // The swap is not a new shell: ShellChanged must not re-fit on it.
            if (body != null) fittedMesh = want;
        }

        MeshFilter shellFilter;

        /// <summary>The MeshFilter that draws this car's body: CarBody's, or
        /// (traffic, CarShell) the child wearing either of the def's bodies.</summary>
        MeshFilter ShellFilter(CarModelDef def)
        {
            if (body == null) body = GetComponent<CarBody>();
            if (body != null) return body.bodyFilter;
            if (shellFilter != null && (shellFilter.sharedMesh == def.bodyMesh || shellFilter.sharedMesh == def.lampsUpMesh))
                return shellFilter;
            shellFilter = null;
            foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh == def.bodyMesh || mf.sharedMesh == def.lampsUpMesh) { shellFilter = mf; break; }
            return shellFilter;
        }

        void Fit()
        {
            Bounds b;
            if (!MeasureShell(out b))
            {
                // No shell to measure: the collider is all there is.
                b = box != null ? new Bounds(box.center, box.size)
                                : new Bounds(new Vector3(0f, 0.72f, 0.05f), new Vector3(1.72f, 1.0f, 4.1f));
            }
            fittedMesh = body != null && body.bodyFilter != null ? body.bodyFilter.sharedMesh : null;
            if (box != null) { fitCenter = box.center; fitSize = box.size; }
            // A new shell may bring (or drop) a lamp control map.
            ResolveLampMap();

            // MEASURED (owner, 2026-09-26: lamps "off of the car, in the car,
            // in front of or behind"): the baker found each shell's lamp glass
            // on its sheet and seated it on the skin. The lens sits a shade
            // proud of that surface, facing out of it but never further than
            // halfway off the car's axis - a raked lamp is still aimed ahead.
            var def = Def;
            if (def != null && def.LampsMeasured)
            {
                // Pop-ups: the head lenses and beams are only lit with the
                // pods raised (PopUps), so they live on the RAISED glass.
                bool pop = def.PopUpLampsMeasured;
                Vector3 headPos = pop ? def.headLampUp : def.headLamp;
                Vector3 headNrm = pop ? def.headLampUpNormal : def.headLampNormal;
                Vector2 headSize = pop ? def.headLampUpSize : def.headLampSize;
                for (int i = 0; i < 2; i++)
                {
                    float side = i == 0 ? -1f : 1f;
                    Vector3 M(Vector3 v) => new Vector3(v.x * side, v.y, v.z);
                    Vector3 hn = M(Vector3.Slerp(Vector3.forward, headNrm.normalized, 0.5f));
                    Vector3 tn = M(Vector3.Slerp(Vector3.back, def.tailLampNormal.normalized, 0.5f));
                    Vector3 hp = M(headPos), tp = M(def.tailLamp);
                    Place(headLens[i].transform, hp + hn * LensProud,
                          Quaternion.LookRotation(hn, Vector3.up),
                          new Vector3(headSize.x, headSize.y, 1f));
                    Place(tailLens[i].transform, tp + tn * LensProud,
                          Quaternion.LookRotation(tn, Vector3.up),
                          new Vector3(def.tailLampSize.x, def.tailLampSize.y, 1f));
                    Place(beams[i].transform, hp - Vector3.forward * 0.05f,
                          Quaternion.Euler(BeamDipDeg, side * BeamToeDeg, 0f), Vector3.one);
                }
                PopUps();
                return;
            }

            float halfW = b.extents.x;
            float noseZ = b.max.z, tailZ = b.min.z;
            float minY = b.min.y, h = b.size.y;
            float headY = minY + h * 0.40f;
            float tailY = minY + h * 0.52f;
            float lensW = Mathf.Clamp(b.size.x * 0.26f, 0.22f, 0.55f);

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Place(headLens[i].transform,
                      new Vector3(side * halfW * 0.62f, headY, noseZ + 0.03f),
                      Quaternion.LookRotation(Vector3.forward, Vector3.up),
                      new Vector3(lensW, lensW * 0.62f, 1f));
                Place(tailLens[i].transform,
                      new Vector3(side * halfW * 0.66f, tailY, tailZ - 0.03f),
                      Quaternion.LookRotation(Vector3.back, Vector3.up),
                      new Vector3(lensW * 0.9f, lensW * 0.5f, 1f));
                // The beam starts a hand inside the lens so the cone's base is
                // hidden in the bodywork, dips a degree and a half and toes
                // out by two: two lobes that merge into one a few metres on.
                Place(beams[i].transform,
                      new Vector3(side * halfW * 0.62f, headY, noseZ - 0.05f),
                      Quaternion.Euler(BeamDipDeg, side * BeamToeDeg, 0f),
                      Vector3.one);
            }
            PopUps();
        }

        /// <summary>Put a lamp at a pose given in the CAR's frame, whichever
        /// transform it actually hangs from.</summary>
        void Place(Transform t, Vector3 carPos, Quaternion carRot, Vector3 scale)
        {
            if (lampRoot == transform)
            {
                t.localPosition = carPos + new Vector3(0f, 0f, body == null ? shellZ : 0f);
                t.localRotation = carRot;
            }
            else
            {
                var def = Def;
                var bodyRot = Quaternion.Euler(0f, def != null ? def.bodyYaw : 0f, 0f);
                var bodyPos = def != null ? new Vector3(0f, def.bodyYOffset, def.bodyZOffset) : Vector3.zero;
                t.localPosition = Quaternion.Inverse(bodyRot) * (carPos - bodyPos);
                t.localRotation = Quaternion.Inverse(bodyRot) * carRot;
            }
            t.localScale = scale;
        }

        // ------------------------------------------------------------------
        //  The global table PSXHeadlights.cginc reads.
        // ------------------------------------------------------------------
        /// <summary>Slots in the shader table (PSXHeadlights.cginc PSX_MAX_HEADLIGHTS).
        /// 16 since the lightpop round; today's table (PSX_LIGHT_NOPOP=0) and
        /// phones fill <see cref="LegacyLights"/> of them.</summary>
        public const int MaxLights = 16;
        public const int LegacyLights = 8;
        static readonly Vector4[] gPos = new Vector4[MaxLights];
        static readonly Vector4[] gFwd = new Vector4[MaxLights];
        static readonly Vector4[] gRight = new Vector4[MaxLights];
        static readonly Vector4[] gColor = new Vector4[MaxLights];
        static readonly List<CarLights> sorted = new List<CarLights>();
        static int pushedFrame = -1;
        static readonly int CountId = Shader.PropertyToID("_PSXHeadCount");
        static readonly int PosId = Shader.PropertyToID("_PSXHeadPos");
        static readonly int FwdId = Shader.PropertyToID("_PSXHeadFwd");
        static readonly int RightId = Shader.PropertyToID("_PSXHeadRight");
        static readonly int ColorId = Shader.PropertyToID("_PSXHeadColor");
        static readonly int HeadNightId = Shader.PropertyToID("_PSXHeadNight");

        /// <summary>How many lamps the table holds right now. For tests.</summary>
        public static int PushedCount { get; private set; }

        // ---- NO LIGHT POPS (the lightpop round, 2026-11-22) ----
        // The table was the four cars nearest the camera, cut HARD at the
        // fourth: in traffic the fifth car's beams on the road switched on and
        // off as the order changed, with no fade at all. Now each lit car gets
        // a score - the distance from the eye to the stretch of road its beams
        // light (HeadAimM ahead of it), times StreetLights' view factor - the
        // four lowest take the slots, and each is weighted by how far inside
        // the fifth's score (the cut) it is, over HeadRankBandM, and by its own
        // distance (whole to HeadFullM, none at HeadEndM): a pure function of
        // where the cars and the eye are, so it moves only as they move, and a
        // replay cut is right on its first frame. PSX_LIGHT_NOPOP=0: as before.
        public const float HeadAimM = 12f, HeadAimRadiusM = 25f;
        public const float HeadRankBandM = 45f, HeadFullM = 150f, HeadEndM = 210f;
        /// <summary>No cut lies further out than this score: twice the end, so a car
        /// with no rival for its slot (a race of four seen from a replay's bystander)
        /// fades by its own distance alone, never by the angle its beams point at.</summary>
        public const float HeadScoreCap = HeadEndM * 2f;
        /// <summary>A car's beams on the world: its lamps are scored by the aim
        /// point's distance times 1 + this times how far off the line of sight
        /// (less than the street lamps': a car turning across the view must not
        /// sweep the ranking).</summary>
        public const float HeadViewPenalty = 1f;
        /// <summary>A car just switched on (a spawn, a respawn) scores this much
        /// further, easing to nothing over <see cref="HeadBirthS"/>: it slides
        /// into the ranking instead of moving the cut in a step.</summary>
        public const float HeadBirthM = 120f, HeadBirthS = 1.5f;
        const int CarsCap = MaxLights / 2;
        /// <summary>Cars whose beams light the world: eight on PC, four on a phone.</summary>
        static int CarsMax => PlatformProfile.PC ? Mathf.Clamp(HeadCarsPC, 1, CarsCap) : LegacyLights / 2;
        /// <summary>Cars whose beams light the world on PC (the perf probe's
        /// set:CarLights.HeadCarsPC:4 A/B).</summary>
        public static int HeadCarsPC = 4;
        float bornAt = -100f;
        static readonly int[] scoredIdx = new int[CarsCap + 1];
        static readonly float[] scoredScore = new float[CarsCap + 1];
        static readonly float[] scoredFade = new float[CarsCap + 1];
        static int scoredCount;
        static float carCut;

        static float HeadFade(CarLights l, Vector3 eye)
        {
            var tr = l.transform;
            float dist = (tr.position + tr.forward * HeadAimM - eye).magnitude;
            return Mathf.Clamp01((HeadEndM - dist) / (HeadEndM - HeadFullM));
        }

        /// <summary>The four lowest scores of <see cref="sorted"/> into
        /// scoredIdx (best first) and the cut (the fifth, never past HeadEndM).</summary>
        static void ScoreCars(Vector3 eye, Vector3 fwd)
        {
            scoredCount = 0;
            int carsMax = CarsMax;
            int cap = carsMax + 1;
            float now = Time.unscaledTime;
            for (int i = 0; i < sorted.Count; i++)
            {
                var tr = sorted[i].transform;
                Vector3 aim = tr.position + tr.forward * HeadAimM;
                Vector3 d = aim - eye;
                float dist = d.magnitude;
                if (dist >= HeadEndM) continue;
                float vf = StreetLights.ViewFactor(d, dist, HeadAimRadiusM, fwd);
                float s = dist * (1f + (vf - 1f) * HeadViewPenalty / StreetLights.ViewPenalty);
                float born = Application.isPlaying ? Mathf.Clamp01((now - sorted[i].bornAt) / HeadBirthS) : 1f;
                s += HeadBirthM * (1f - born);
                if (s >= HeadScoreCap) continue;
                if (scoredCount == cap && s >= scoredScore[cap - 1]) continue;
                int k = scoredCount < cap ? scoredCount++ : cap - 1;
                float fade = Mathf.Clamp01((HeadEndM - dist) / (HeadEndM - HeadFullM));
                while (k > 0 && scoredScore[k - 1] > s)
                {
                    scoredScore[k] = scoredScore[k - 1]; scoredIdx[k] = scoredIdx[k - 1]; scoredFade[k] = scoredFade[k - 1];
                    k--;
                }
                scoredScore[k] = s; scoredIdx[k] = i; scoredFade[k] = fade;
            }
            carCut = scoredCount > carsMax ? Mathf.Min(scoredScore[carsMax], HeadScoreCap) : HeadScoreCap;
            if (scoredCount > carsMax) scoredCount = carsMax;
        }

        // ---- the light-pop meter's hooks (Dev/LightPopMeter.cs) ----
        internal static bool MeterOn, MeterHold;
        /// <summary>Per car (instance id): the rank part of its weight as last pushed.</summary>
        internal static readonly Dictionary<int, float> MeterRank = new Dictionary<int, float>();
        /// <summary>The frame-before's weights, while the meter re-pushes it.</summary>
        internal static Dictionary<int, float> MeterPrev;
        /// <summary>Re-push the table at the frame-before's weights (cars where they are now).</summary>
        internal static void MeterPush(Dictionary<int, float> prev)
        {
            MeterHold = true; MeterPrev = prev;
            try { PushGlobals(); }
            finally { MeterHold = false; MeterPrev = null; }
        }
        /// <summary>Every live CarLights (the meter's lens and beam renderers).</summary>
        internal static List<CarLights> MeterAll => all;
        internal Renderer MeterLens(int i, bool head) => head ? (headLens[i] as Renderer) : (tailLens[i] as Renderer);
        internal Renderer MeterBeam(int i) => beams[i];

        /// <summary>
        /// Fill the table from every lit car, nearest the camera first, two
        /// lamps per car, and push it. Eight slots is four cars, which is a
        /// grid; the fifth car back is a pair of lens sprites and no beam,
        /// which nobody has ever seen from the driver's seat.
        /// </summary>
        public static void PushGlobals()
        {
            int n = 0;
            if (lightsOn)
            {
                sorted.Clear();
                // "Is this car switched on?" asked two ways. In play,
                // isActiveAndEnabled, as it always was. Outside play mode that
                // is false for every car PreviewBuild enlisted - it reports
                // whether Unity has ENABLED the component, and Unity never
                // enables a non-ExecuteAlways one in edit mode - so the tools
                // ask the serialized switch and the hierarchy instead.
                bool playing = Application.isPlaying;
                for (int i = all.Count - 1; i >= 0; i--)
                {
                    var l = all[i];
                    if (l == null) { all.RemoveAt(i); continue; }
                    bool live = playing ? l.isActiveAndEnabled
                                        : l.enabled && l.gameObject.activeInHierarchy;
                    if (live && l.LitNow && l.beams[0] != null) sorted.Add(l);
                }
                var cam = Camera.main;
                bool noPop = StreetLights.NoPop && cam != null;
                if (noPop) ScoreCars(cam.transform.position, cam.transform.forward);
                else if (cam != null)
                {
                    Vector3 eye = cam.transform.position;
                    sorted.Sort((a, b) => (a.transform.position - eye).sqrMagnitude
                                   .CompareTo((b.transform.position - eye).sqrMagnitude));
                }
                float cosOuter = Mathf.Cos(BeamOuterDeg * Mathf.Deg2Rad);
                float cosInner = Mathf.Cos(BeamInnerDeg * Mathf.Deg2Rad);
                // LINEAR light (the colour pass, C5): the halogen is authored
                // sRGB like every colour in the project, and a global vector
                // is pushed exactly as written - StreetLights has always
                // linearised its bulbs; the beams never were.
                float bi = BeamIntensityNow;
                var col = Halogen.linear * bi;
                float glint = GlintIntensity / Mathf.Max(bi, 1e-4f);
                if (MeterOn && !MeterHold) MeterRank.Clear();
                bool meterB = MeterHold && MeterPrev != null;
                int carsTaken = noPop && !meterB ? scoredCount : sorted.Count;
                Vector3 eyeNow = cam != null ? cam.transform.position : Vector3.zero;
                for (int c = 0; c < carsTaken; c++)
                {
                    var l = noPop && !meterB ? sorted[scoredIdx[c]] : sorted[c];
                    // NO LIGHT POPS: the car's rank weight times its own distance fade
                    float rank = noPop && !meterB ? Mathf.Clamp01((carCut - scoredScore[c]) / HeadRankBandM) : (n < LegacyLights ? 1f : 0f);
                    float w = noPop && !meterB ? Mathf.Min(rank, scoredFade[c]) : rank;
                    if (meterB)
                    {
                        // the meter's frame-before: the weights it had, faded for today's distance
                        rank = MeterPrev.TryGetValue(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(l), out float pr) ? pr : 0f;
                        w = noPop ? Mathf.Min(rank, HeadFade(l, eyeNow)) : rank;
                    }
                    else if (MeterOn) MeterRank[System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(l)] = rank;
                    if (w <= 0f) continue;
                    var cw = col * w;
                    for (int i = 0; i < 2 && n < (noPop ? MaxLights : LegacyLights); i++)
                    {
                        if (l.HeadOut(i)) continue;   // R1a part 2a: a lamp a hit broke gives no light
                        var t = l.beams[i].transform;
                        Vector3 p = t.position, f = t.forward, r = t.right;
                        gPos[n] = new Vector4(p.x, p.y, p.z, BeamRange);
                        gFwd[n] = new Vector4(f.x, f.y, f.z, cosOuter);
                        gRight[n] = new Vector4(r.x, r.y, r.z, cosInner);
                        gColor[n] = new Vector4(cw.r, cw.g, cw.b, glint);
                        n++;
                    }
                }
            }
            for (int i = n; i < MaxLights; i++)
                gPos[i] = gFwd[i] = gRight[i] = gColor[i] = Vector4.zero;
            PushedCount = n;
            // The cone in the air follows the beam (BeamConeStrength). One
            // shared material for every car, so one write.
            if (beamMat != null) beamMat.SetFloat("_Strength", ConeStrengthNow);
            Shader.SetGlobalFloat(CountId, n);
            // The beam's albedo floor (PSXHeadlights.cginc): the hour's
            // BeamNight, 0 through sunset and dawn.
            Shader.SetGlobalFloat(HeadNightId, BeamNight(Shader.GetGlobalFloat("_PSXNight")));
            Shader.SetGlobalVectorArray(PosId, gPos);
            Shader.SetGlobalVectorArray(FwdId, gFwd);
            Shader.SetGlobalVectorArray(RightId, gRight);
            Shader.SetGlobalVectorArray(ColorId, gColor);
        }

        /// <summary>
        /// The diffuse light the pushed table puts on a surface at
        /// <paramref name="wpos"/> facing <paramref name="n"/>: linear RGB,
        /// the same arithmetic as PSXHeadlightsCore (Shaders/PSXHeadlights.cginc),
        /// constant for constant. For the look tools (ColourShots writes it
        /// into each measuring box's sidecar entry, so the beam's shape is
        /// read apart from the road's texture, the street lamps and the fog);
        /// the game never calls it.
        /// </summary>
        public static Vector3 BeamLightAt(Vector3 wpos, Vector3 n)
        {
            const float Near = 0.55f, NearFrom = 3f, NearTo = 10f, FadeM = 45f;   // PSXHeadlights.cginc BEAM_FADE_M (45 since the dark-night retune)
            const float CutLo = 0.016f, CutHi = 0.032f, FaceLo = -0.05f, FaceHi = 0.02f;
            const float Spill = 0.12f, SpillCos = 0.342f, SpillFrom = 10f, SpillTo = 25f, SpillCutLo = 0.05f, SpillCutHi = 0.25f;
            Vector3 sum = Vector3.zero;
            for (int j = 0; j < PushedCount && j < MaxLights; j++)
            {
                Vector3 d = wpos - (Vector3)gPos[j];
                float dist = d.magnitude;
                Vector3 F = gFwd[j], R = gRight[j];
                Vector3 U = Vector3.Cross(F, R);
                // HLSL's cross is the right-handed formula; Unity's
                // Vector3.Cross is the same formula, so U is the same vector.
                float fz = Vector3.Dot(d, F), fx = Vector3.Dot(d, R), fy = Vector3.Dot(d, U);
                float hz = fz / Mathf.Sqrt(fx * fx + fz * fz + 1e-4f);
                float spread = SmoothStep(gFwd[j].w, gRight[j].w, hz);
                float slope = fy / Mathf.Max(fz, 0.25f);
                float cut = 1f - SmoothStep(CutLo, CutHi, slope);
                float front = Mathf.Clamp01(fz * 1.5f);
                float range = Mathf.Max(gPos[j].w, 1f);
                float att = Mathf.Lerp(Near, 1f, SmoothStep(NearFrom, NearTo, dist)) * (1f - SmoothStep(range - FadeM, range, dist));
                float spill = Spill * SmoothStep(SpillCos, gFwd[j].w, hz) * (1f - SmoothStep(SpillCutLo, SpillCutHi, slope))
                            * (1f - SmoothStep(SpillFrom, SpillTo, dist));
                float nl = Vector3.Dot(n, -d) / Mathf.Max(dist, 0.05f);
                float facing = SmoothStep(FaceLo, FaceHi, nl);
                float k = (spread * cut * att + spill) * front * facing;
                sum += new Vector3(gColor[j].x, gColor[j].y, gColor[j].z) * k;
            }
            return sum;
        }

        /// <summary>HLSL's smoothstep (Mathf.SmoothStep is not it: that one
        /// interpolates between its first two arguments).</summary>
        static float SmoothStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static MeshRenderer MakeQuad(string name, Transform parent, Material mat)
            => MakeMesh(name, parent, QuadMesh, mat);

        static MeshRenderer MakeMesh(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        // ------------------------------------------------------------------
        //  Shared assets. Built once per run and reused by every car: two
        //  opponents' headlights should not be two materials.
        // ------------------------------------------------------------------
        static Mesh quadMesh;
        static Mesh QuadMesh
        {
            get
            {
                if (quadMesh != null) return quadMesh;
                quadMesh = new Mesh { name = "GlowQuad" };
                quadMesh.vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f),   new Vector3(0.5f, -0.5f, 0f),
                };
                quadMesh.uv = new[]
                {
                    new Vector2(0f, 0f), new Vector2(0f, 1f),
                    new Vector2(1f, 1f), new Vector2(1f, 0f),
                };
                quadMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                quadMesh.RecalculateNormals();
                quadMesh.RecalculateBounds();
                return quadMesh;
            }
        }

        static Mesh coneMesh;
        /// <summary>
        /// The beam in the air: an elliptical cone along +Z from the lens,
        /// wider than it is tall like the beam it stands for. Normals are
        /// analytic (the cross of the around and along tangents) so the seam
        /// vertex carries the same normal as its twin and PSX/Beam's
        /// silhouette term shows no line down the cone.
        /// </summary>
        static Mesh ConeMesh
        {
            get
            {
                if (coneMesh != null) return coneMesh;
                const int seg = 16;
                var verts = new Vector3[(seg + 1) * 2];
                var norms = new Vector3[(seg + 1) * 2];
                var uvs = new Vector2[(seg + 1) * 2];
                for (int s = 0; s <= seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    for (int r = 0; r < 2; r++)
                    {
                        Vector2 rad = r == 0 ? ConeStart : ConeEnd;
                        int k = s * 2 + r;
                        verts[k] = new Vector3(rad.x * ca, rad.y * sa, r * ConeLength);
                        var around = new Vector3(-rad.x * sa, rad.y * ca, 0f);
                        var along = new Vector3((ConeEnd.x - ConeStart.x) * ca,
                                                (ConeEnd.y - ConeStart.y) * sa, ConeLength);
                        norms[k] = Vector3.Cross(around, along).normalized;
                        uvs[k] = new Vector2(s / (float)seg, r);
                    }
                }
                var tris = new int[seg * 6];
                for (int s = 0; s < seg; s++)
                {
                    int a0 = s * 2, a1 = s * 2 + 1, b0 = (s + 1) * 2, b1 = (s + 1) * 2 + 1;
                    tris[s * 6 + 0] = a0; tris[s * 6 + 1] = a1; tris[s * 6 + 2] = b0;
                    tris[s * 6 + 3] = a1; tris[s * 6 + 4] = b1; tris[s * 6 + 5] = b0;
                }
                coneMesh = new Mesh { name = "BeamCone" };
                coneMesh.vertices = verts;
                coneMesh.normals = norms;
                coneMesh.uv = uvs;
                coneMesh.triangles = tris;
                coneMesh.RecalculateBounds();
                return coneMesh;
            }
        }

        static Texture2D glowTex;
        /// <summary>Radial falloff, generated rather than imported: one 48x48
        /// blob is every lamp in the game and an asset for it would be one more
        /// thing to keep in sync with the shader.</summary>
        static Texture2D GlowTex
        {
            get
            {
                if (glowTex != null) return glowTex;
                const int n = 48;
                glowTex = new Texture2D(n, n, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x - (n - 1) * 0.5f) / (n * 0.5f);
                        float dy = (y - (n - 1) * 0.5f) / (n * 0.5f);
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a;                       // tighter core, softer edge
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                glowTex.SetPixels32(px);
                glowTex.Apply();
                return glowTex;
            }
        }

        static Material MakeMat(string shaderName, string name, Color tint, float strength, bool masked)
        {
            var shader = Shader.Find(shaderName);
            // A missing shader would otherwise draw magenta rectangles all over
            // the cars, which reads as damage rather than as a build problem.
            if (shader == null) { Debug.LogWarning("CarLights: " + shaderName + " shader missing"); return null; }
            var m = new Material(shader) { name = name };
            if (masked) m.mainTexture = GlowTex;
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
            if (m.HasProperty("_Strength")) m.SetFloat("_Strength", strength);
            return m;
        }

        static Material headMat, tailDim, tailBright, beamMat;
        /// <summary>The running tail lens's glow (PSX/Glow _Strength). 0.9
        /// until the dark-night retune (2026-09-29): NFS Heat's running
        /// lamps (the owner's frame 15) glow a red halo well past the lens -
        /// cores Ycode 180-195 - where ours read as two small red dots.
        /// The lens only: its light on the road stays the 5 W TailLampDim.</summary>
        public const float TailLensDim = 1.35f;
        /// <summary>The lens, in halogen, a touch hotter than the beam so it
        /// reads as the source.</summary>
        static Material HeadMat => headMat != null ? headMat
            : headMat = Lens(MakeMat("PSX/Glow", "Headlight", Halogen, 1.6f, true), 1f, LensHeadHaloM, LensHeadHaloAmt);
        static Material TailDimMat => tailDim != null ? tailDim
            : tailDim = Lens(MakeMat("PSX/Glow", "Taillight", new Color(1.00f, 0.16f, 0.10f), TailLensDim, true), 1f, LensTailHaloM, LensTailHaloAmt);
        static Material TailBrightMat => tailBright != null ? tailBright
            : tailBright = Lens(MakeMat("PSX/Glow", "Brakelight", new Color(1.00f, 0.12f, 0.06f), 2.6f, true), 1f, LensTailHaloM, LensTailHaloAmt);

        // ---- the lightlook round (2026-11-23): a far car's lamps a steady point (PSX/Glow _LensLook) ----
        /// <summary>The halos round a lens with the look on (PSX/Glow): half-size
        /// in metres (it grows with the approach as the world does) and amount.</summary>
        public static float LensHeadHaloM = 0.75f, LensHeadHaloAmt = 0.30f, LensTailHaloM = 0.40f, LensTailHaloAmt = 0.22f;
        static Material headFar, tailDimFar, tailBrightFar;
        static Material Lens(Material m, float mode, float haloM, float haloAmt)
        {
            if (m == null) return null;
            m.SetFloat("_LensLook", mode); m.SetFloat("_LensHaloM", haloM); m.SetFloat("_LensHaloAmt", haloAmt);
            return m;
        }
        /// <summary>A lamp-map body's lens sprites: the far point only (mode 2).</summary>
        static Material HeadFarMat => headFar != null ? headFar
            : headFar = Lens(MakeMat("PSX/Glow", "HeadlightFar", Halogen, 1.6f, true), 2f, 0f, 0f);
        static Material TailDimFarMat => tailDimFar != null ? tailDimFar
            : tailDimFar = Lens(MakeMat("PSX/Glow", "TaillightFar", new Color(1.00f, 0.16f, 0.10f), TailLensDim, true), 2f, 0f, 0f);
        static Material TailBrightFarMat => tailBrightFar != null ? tailBrightFar
            : tailBrightFar = Lens(MakeMat("PSX/Glow", "BrakelightFar", new Color(1.00f, 0.12f, 0.06f), 2.6f, true), 2f, 0f, 0f);
        Material TailLensMat => HasLampMap ? (braking ? TailBrightFarMat : TailDimFarMat) : (braking ? TailBrightMat : TailDimMat);
        /// <summary>The beam in the air. Same halogen; PSX/Beam fades it out
        /// by daylight on its own.</summary>
        static Material BeamMat => beamMat != null ? beamMat
            : beamMat = MakeMat("PSX/Beam", "HeadlightBeam", Halogen, ConeStrengthNow, false);
    }
}
