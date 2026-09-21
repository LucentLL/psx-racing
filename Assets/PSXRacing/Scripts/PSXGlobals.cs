using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Drives the global shader uniforms for the PSX/Lit shader:
    /// sun direction/color, ambient, fog range, and the vertex-snap toggle -
    /// and, since the night pass, wetness / night / grade-night / mood, plus
    /// the hook that pushes the street-lamp table per camera.
    /// </summary>
    [ExecuteAlways]
    public class PSXGlobals : MonoBehaviour
    {
        public Light sun;
        public Color ambient = new Color(0.42f, 0.40f, 0.50f);
        public Color fogColor = new Color(0.87f, 0.56f, 0.44f);
        public float fogNear = 60f;
        public float fogFar = 240f;
        /// <summary>
        /// Per-SCENE multiplier on the hour presets' fog band, baked by the
        /// scene builder. The circuits live inside 500 m; a mountain stage is
        /// about the ridge two valleys over, so its scene bakes ~3x that again
        /// and TimeOfDay.Apply multiplies the preset through this. The preset
        /// table itself stays one table — a second table of seven hours per
        /// venue would drift apart the first time one of them was tuned.
        /// </summary>
        public float fogScale = 1f;
        /// <summary>
        /// THE SHAPE OF THE BAND, not its length.
        ///
        /// The fog was a straight ramp from <see cref="fogNear"/> to
        /// <see cref="fogFar"/>, which puts HALF the fog colour over anything
        /// standing in the middle of it — 250 m on a circuit — and the whole of
        /// it over everything past the end. That is the "objects in the
        /// distance are white" picture: a hill at 300 m is not hazy, it is
        /// erased and repainted in sky colour.
        ///
        /// Raising the exponent bends the ramp so the band starts slowly and
        /// only closes near the end: at 2.2, halfway through is 22% fog rather
        /// than 50%, and three quarters of the way is 53% rather than 75%. What
        /// it does NOT change is either end — the fog is still nothing at
        /// fogNear and still total at fogFar, so the far plane stays hidden
        /// behind a full-strength wall and no geometry pops through it. It is
        /// also free: one pow() per vertex, no extra draw distance, which
        /// matters because this game is played on a phone.
        ///
        /// Written every race by TimeOfDay.Apply from the one constant there,
        /// so a scene baked before this existed cannot disagree with a scene
        /// baked after it. An unset shader global reads 0 and the shaders floor
        /// it at 1, which is the old straight ramp.
        /// </summary>
        public float fogCurve = TimeOfDay.FogCurve;
        /// <summary>
        /// THE AMBIENT FROM ABOVE. PSX/Lit used to light every unlit face the
        /// same colour whichever way it pointed, so a roof and a floor sat
        /// under the same grey, and the sky above it, once it became a real
        /// photograph, had a colour the world beneath it never picked up.
        /// Faces that look UP now take this instead of <see cref="ambient"/>,
        /// blended by the normal's upness, so a bonnet under a noon sky is a
        /// touch blue and one under a sunset a touch orange. TimeOfDay writes
        /// it from the hour's own sky stops (see TimeOfDay.SkyAmbientFor); a
        /// scene that never applies an hour keeps it equal to the ambient,
        /// which is exactly the old picture. Left alpha-zero here so Apply
        /// can tell "never set" from "set to black".
        /// </summary>
        public Color skyAmbient = new Color(0f, 0f, 0f, 0f);

        /// <summary>
        /// PS1 vertex jitter: quantise every vertex to the framebuffer grid.
        ///
        /// DEFAULTS TO OFF, on the owner's instruction: "many textures are
        /// interfering when moving. this should never happen in buildings or
        /// when driving." The snap moves a vertex in SCREEN space and leaves
        /// its depth alone, so the depth rasterised across a polygon no longer
        /// describes where that polygon actually is — and two surfaces sitting
        /// on each other (a table top and its trim, a road and its painted
        /// line, a wall and its poster) disagree about which is in front, per
        /// pixel, differently every frame. That is the flicker. The error is
        /// ANGULAR, so it grows with distance without bound, and it is worst on
        /// exactly the surfaces you look at most: floors and roads at a grazing
        /// angle, where half a pixel sideways is a long way forward.
        ///
        /// The same call the affine warping got, for the same reason and from
        /// the same person — see the _Affine note in PSXLit.shader. Both are
        /// only ever right on small triangles, and almost nothing in this game
        /// is made of small triangles.
        ///
        /// Worth writing down: EVERY preview tool in this project
        /// (CityPreview, TownPreview, PizzeriaPreview, TireFxPreview,
        /// HoistPreview) has always set _PSXSnap to 0 before shooting. So every
        /// screenshot this look was signed off from was rendered WITHOUT the
        /// snap, while the game shipped WITH it — the tools and the game were
        /// never showing the same picture. They agree now.
        ///
        /// The flag stays so the jitter can be turned back on deliberately (one
        /// bool and a scene rebuild), but nothing sets it today.
        /// </summary>
        public bool vertexSnap;

        // ------------------------------------------------------------------
        //  THE NIGHT LOOK (2026-09-21, the Need for Speed 2015 pass). Four
        //  more globals, all written by TimeOfDay.Apply from the hour and the
        //  weather, and all ZERO by default (a scene saved before they
        //  existed deserialises them as zero too) - so a scene that never
        //  applies an hour pushes zeros, and every shader that reads them
        //  draws exactly what it drew before they existed. Never Shader.SetGlobal these anywhere else: this
        //  component rewrites every global it owns every frame, so a value
        //  written around it lasts one frame. Set the FIELD.
        // ------------------------------------------------------------------

        /// <summary>
        /// HOW WET THE WORLD IS, 0..1 (_PSXWetness). Written by
        /// TimeOfDay.Apply from TimeOfDay.WetnessFor(hour, weather): rain 1,
        /// fog and snow partway, and clear nights DAMP - the owner's reference
        /// is a city that is always wet after dark. PSX/Lit multiplies it by
        /// each material's own _Wet mask (a road is wet, a wall is not), and
        /// PSX/Halo grows the lamp halos with it (mist).
        /// </summary>
        public float wetness;
        /// <summary>
        /// HOW NIGHT-TIME IT IS, 0..1 (_PSXNight): night 1, dusk 0.75, dawn
        /// 0.5, sunset 0.3, day 0. Written by TimeOfDay.Apply from
        /// TimeOfDay.NightFor(hour). Lights the city's windows (PSX/Lit) and
        /// dirties the lens (LensFx).
        /// </summary>
        public float night;
        /// <summary>
        /// How far the FILM GRADE takes its night form, 0..1 (_PSXGradeNight):
        /// the matte lift fading out, a heavier vignette, cooler saturation.
        /// Written by TimeOfDay.Apply from TimeOfDay.GradeNightFor(hour);
        /// read by PSX/Blit only. 0 is the daytime grade the owner signed off,
        /// bit for bit.
        /// </summary>
        public float gradeNight;
        /// <summary>
        /// THE HOUR'S MOOD in the shadows (_PSXMood): rgb = the hue the grade
        /// split-tones the darks toward (any luminance - the grade takes the
        /// hue only), a = how much, 0..1. Written by TimeOfDay.Apply from
        /// TimeOfDay.MoodFor(hour, urban): sodium murk in a city at night,
        /// blue on a mountain, violet at dawn. Pushed with SetGlobalVector,
        /// NOT SetGlobalColor: it is a hue to be used as it is, and the sRGB
        /// conversion SetGlobalColor applies would bend it. Alpha zero (the
        /// default) is no split-tone at all.
        /// </summary>
        public Color mood = new Color(0f, 0f, 0f, 0f);

        void OnEnable()
        {
            // The street-lamp table is pushed per camera, from the render
            // pipeline's own callback (StreetLights says why). This is the
            // one component every drivable scene has, and it runs in edit
            // mode too - so the scene view and the tools' render requests get
            // lamps chosen for their own eye with nobody asking.
            StreetLights.EnsureHook();
            Apply();
        }
        void Update() => Apply();

        public void Apply()
        {
            Vector3 dir = sun != null ? -sun.transform.forward : new Vector3(0.3f, 0.8f, 0.2f).normalized;
            Color lightCol = sun != null ? sun.color * sun.intensity : Color.white;
            Shader.SetGlobalVector("_PSXLightDir", dir);
            Shader.SetGlobalColor("_PSXLightColor", lightCol);
            Shader.SetGlobalColor("_PSXAmbient", ambient);
            Shader.SetGlobalColor("_PSXSkyAmbient", skyAmbient.a > 0f ? skyAmbient : ambient);
            Shader.SetGlobalColor("_PSXFogColor", fogColor);
            Shader.SetGlobalFloat("_PSXFogNear", fogNear);
            Shader.SetGlobalFloat("_PSXFogFar", fogFar);
            Shader.SetGlobalFloat("_PSXFogCurve", fogCurve);
            Shader.SetGlobalFloat("_PSXSnap", vertexSnap ? 1f : 0f);
            Shader.SetGlobalFloat("_PSXWetness", Mathf.Clamp01(wetness));
            Shader.SetGlobalFloat("_PSXNight", Mathf.Clamp01(night));
            Shader.SetGlobalFloat("_PSXGradeNight", Mathf.Clamp01(gradeNight));
            Shader.SetGlobalVector("_PSXMood", new Vector4(mood.r, mood.g, mood.b, Mathf.Clamp01(mood.a)));
            // NOT StreetLights.Push() here: this runs in Update, before the
            // cars and the chase camera move in LateUpdate, and a table chosen
            // here would light the road for where the camera was last frame.
            // The push is per camera, at render time (see OnEnable).
        }
    }
}
