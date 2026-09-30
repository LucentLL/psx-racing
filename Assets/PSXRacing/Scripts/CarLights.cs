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

        static readonly List<CarLights> all = new List<CarLights>();
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

        void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
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
        void UpdateTailLamp()
        {
            if (tailLens[0] == null || tailLens[1] == null) return;
            var a = tailLens[0].transform;
            var b = tailLens[1].transform;
            // The lenses face out of the back of the car, so their forward IS
            // "behind", with the body's dive and roll in it.
            tailLampPos = (a.position + b.position) * 0.5f + a.forward * TailLampBack;
            float intensity = braking ? TailLampBrake : TailLampDim;
            bool lit = lightsOn && enabled && gameObject.activeInHierarchy;
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

            bool nowBraking = car != null && (car.brakeInput > 0.15f || car.handbrakeInput);
            if (nowBraking != braking)
            {
                braking = nowBraking;
                ApplyBrake();
            }

            // Every frame: the car moved (StreetLights pushes at render time,
            // after every LateUpdate, so this is where it will be drawn).
            UpdateTailLamp();

            // Once per frame for the whole field, by whichever car runs first.
            if (pushedFrame != Time.frameCount)
            {
                pushedFrame = Time.frameCount;
                PushGlobals();
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
            var mat = braking ? TailBrightMat : TailDimMat;
            for (int i = 0; i < 2; i++)
            {
                if (tailLens[i] == null) continue;
                tailLens[i].sharedMaterial = mat;
                tailLens[i].enabled = lightsOn || braking;
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
            PushGlobals();
            // And the tail lamp's light, which LateUpdate would otherwise
            // place - and LateUpdate never runs outside play mode.
            UpdateTailLamp();
        }

        void Refresh()
        {
            for (int i = 0; i < 2; i++)
            {
                if (headLens[i] != null) headLens[i].enabled = lightsOn;
                if (beams[i] != null) beams[i].enabled = lightsOn;
                if (tailLens[i] != null) tailLens[i].enabled = lightsOn || braking;
            }
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

            // MEASURED (owner, 2026-09-26: lamps "off of the car, in the car,
            // in front of or behind"): the baker found each shell's lamp glass
            // on its sheet and seated it on the skin. The lens sits a shade
            // proud of that surface, facing out of it but never further than
            // halfway off the car's axis - a raked lamp is still aimed ahead.
            var def = Def;
            if (def != null && def.LampsMeasured)
            {
                for (int i = 0; i < 2; i++)
                {
                    float side = i == 0 ? -1f : 1f;
                    Vector3 M(Vector3 v) => new Vector3(v.x * side, v.y, v.z);
                    Vector3 hn = M(Vector3.Slerp(Vector3.forward, def.headLampNormal.normalized, 0.5f));
                    Vector3 tn = M(Vector3.Slerp(Vector3.back, def.tailLampNormal.normalized, 0.5f));
                    Vector3 hp = M(def.headLamp), tp = M(def.tailLamp);
                    Place(headLens[i].transform, hp + hn * LensProud,
                          Quaternion.LookRotation(hn, Vector3.up),
                          new Vector3(def.headLampSize.x, def.headLampSize.y, 1f));
                    Place(tailLens[i].transform, tp + tn * LensProud,
                          Quaternion.LookRotation(tn, Vector3.up),
                          new Vector3(def.tailLampSize.x, def.tailLampSize.y, 1f));
                    Place(beams[i].transform, hp - Vector3.forward * 0.05f,
                          Quaternion.Euler(BeamDipDeg, side * BeamToeDeg, 0f), Vector3.one);
                }
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
        public const int MaxLights = 8;
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
                    if (live && l.beams[0] != null) sorted.Add(l);
                }
                var cam = Camera.main;
                if (cam != null)
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
                foreach (var l in sorted)
                {
                    for (int i = 0; i < 2 && n < MaxLights; i++)
                    {
                        var t = l.beams[i].transform;
                        Vector3 p = t.position, f = t.forward, r = t.right;
                        gPos[n] = new Vector4(p.x, p.y, p.z, BeamRange);
                        gFwd[n] = new Vector4(f.x, f.y, f.z, cosOuter);
                        gRight[n] = new Vector4(r.x, r.y, r.z, cosInner);
                        gColor[n] = new Vector4(col.r, col.g, col.b, glint);
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
            : headMat = MakeMat("PSX/Glow", "Headlight", Halogen, 1.6f, true);
        static Material TailDimMat => tailDim != null ? tailDim
            : tailDim = MakeMat("PSX/Glow", "Taillight", new Color(1.00f, 0.16f, 0.10f), TailLensDim, true);
        static Material TailBrightMat => tailBright != null ? tailBright
            : tailBright = MakeMat("PSX/Glow", "Brakelight", new Color(1.00f, 0.12f, 0.06f), 2.6f, true);
        /// <summary>The beam in the air. Same halogen; PSX/Beam fades it out
        /// by daylight on its own.</summary>
        static Material BeamMat => beamMat != null ? beamMat
            : beamMat = MakeMat("PSX/Beam", "HeadlightBeam", Halogen, ConeStrengthNow, false);
    }
}
