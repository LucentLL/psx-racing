using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// The SPEED LINES option, remembered across sessions.
    ///
    /// Same shape as <see cref="LookPrefs"/> and <see cref="PSXQuality"/>: a
    /// lazily-read PlayerPrefs int with an eager Save, because on WebGL a
    /// preference that is not flushed is a preference lost to the next tab
    /// close. Default ON, and a toggle rather than a slider — the overlay is a
    /// stylistic call (Ridge Racer and GT never drew these; WipEout and
    /// Rollcage did), so the one thing a player needs is a way to say no.
    /// </summary>
    public static class SpeedLinesPrefs
    {
        const string PrefKey = "psx.speedLines";

        static int cached = -1;

        public static bool Enabled
        {
            get
            {
                // SHIPS OFF (2026-09-08). It shipped ON and reached the owner
                // as a black screen with white bars: the streak sheet was
                // opaque, so any frame that drew it on a plain UI material
                // painted the whole picture. The sheet has an alpha channel
                // now, but a cosmetic flourish does not get to be the thing
                // that can make the game unplayable — it is a switch in
                // OPTIONS, and the player turns it on.
                if (cached < 0) cached = PlayerPrefs.GetInt(PrefKey, 0);
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
    /// Radial speed streaks over the world, the Underground "warp" cue done
    /// the PS1 way: a polar-mapped 256 px dash texture on a full-frame
    /// RawImage that lives on HUDCanvas, so it is rasterised INSIDE the
    /// low-res framebuffer and goes through the same Bayer dither and colour
    /// quantise as the road. Drawn as a modern post effect it would look like
    /// one; drawn at 240 lines it looks like the game.
    ///
    /// Deliberately faint. The in-world cues — the FOV pull, the verge posts,
    /// the wind — carry the sense of speed; this is the garnish, and it caps at
    /// <see cref="MaxIntensity"/> so it never covers the road a player is
    /// steering by. It starts above town speeds (<see cref="StartMps"/>) so a
    /// parking lot never streaks, and it is off in the two views where it
    /// would be wrong: COCKPIT (the cabin overlay is the periphery there) and
    /// TOP DOWN (nothing radiates from a car seen from above).
    ///
    /// The material is driven, not the texture: <c>_Scroll</c> advances with
    /// distance travelled so the dashes stream OUTWARD at road speed, which is
    /// what makes them read as motion rather than as a frame.
    /// </summary>
    public class SpeedLines : MonoBehaviour
    {
        public CarController car;
        /// <summary>The full-frame RawImage on HUDCanvas this drives. Wired by
        /// the builder; SpeedLines owns its material from Awake onward.</summary>
        public RawImage image;
        /// <summary>The PSX camera, for the frame's aspect — the polar mapping
        /// has to be corrected or the streaks radiate from an ellipse.</summary>
        public Camera cam;

        /// <summary>Speed the streaks begin at: 30 m/s = 108 km/h. Below this
        /// a street is a street, and a car pulling out of a driveway must not
        /// look like it is entering hyperspace.</summary>
        public const float StartMps = 30f;
        /// <summary>Speed span over which they reach full strength: 30 -> 58 m/s
        /// (108 -> 209 km/h), which puts "full" at the top of what the median
        /// catalog car (vmax 284 km/h) does on a circuit straight.</summary>
        public const float SpanMps = 28f;
        /// <summary>Hard ceiling on the overlay's alpha. 0.35 is where a streak
        /// reads as a streak on a 240-line frame without the periphery going
        /// white; the owner's call is that this stays subtle.</summary>
        public const float MaxIntensity = 0.35f;
        /// <summary>Texture repeats scrolled per metre travelled. At 40 m/s
        /// that is 2.4 dash-lengths a second — fast enough to stream, slow
        /// enough that point filtering does not strobe it. A starting value.</summary>
        public const float ScrollPerMetre = 0.06f;
        /// <summary>Fade rate on/off, 1/s. A view change or the pause menu
        /// should not snap a full-frame overlay in one frame.</summary>
        const float FadeRate = 6f;

        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int ScrollId = Shader.PropertyToID("_Scroll");
        static readonly int AspectId = Shader.PropertyToID("_Aspect");

        Material mat;
        float scroll;
        float shown;

        /// <summary>
        /// Overlay alpha for a road speed in m/s: zero below
        /// <see cref="StartMps"/>, smoothstepped to <see cref="MaxIntensity"/>
        /// over <see cref="SpanMps"/>. Static and public so the self-test can
        /// pin the curve — a streak overlay that showed at 20 m/s would be
        /// reported as "the screen is smeared in town", not as a curve bug.
        /// </summary>
        public static float IntensityFor(float speedMps)
        {
            float k = Mathf.Clamp01((Mathf.Abs(speedMps) - StartMps) / SpanMps);
            return Mathf.SmoothStep(0f, 1f, k) * MaxIntensity;
        }

        /// <summary>Whether the overlay may draw at all right now. Everything
        /// here is a reason for it NOT to: the option, the menu, on foot, and
        /// the two views it does not belong in.</summary>
        public static bool Allowed =>
            SpeedLinesPrefs.Enabled &&
            !PauseMenu.IsOpen &&
            !OnFoot.ForecourtMode.OnFoot &&
            ChaseCamera.Current != ChaseCamera.View.Cockpit &&
            ChaseCamera.Current != ChaseCamera.View.TopDown;

        void Awake()
        {
            if (image == null) image = GetComponent<RawImage>();
            if (image == null || image.material == null) return;
            // Our own instance. The builder assigns a SAVED material asset so
            // the scene shows the overlay wired; writing _Scroll into that
            // asset sixty times a second in the editor would dirty it on
            // every play, and in a build would share one scroll across every
            // instance there could ever be.
            mat = new Material(image.material) { hideFlags = HideFlags.DontSave };
            image.material = mat;
            image.enabled = false;
        }

        void Update()
        {
            if (image == null || mat == null) return;
            float dt = Time.deltaTime;
            float v = car != null ? Mathf.Abs(car.forwardSpeed) : 0f;
            float want = Allowed ? IntensityFor(v) : 0f;
            shown = Mathf.Lerp(shown, want, 1f - Mathf.Exp(-FadeRate * dt));
            if (want <= 0f && shown < 0.002f) shown = 0f;

            // Not drawn at all when there is nothing to draw. A full-frame
            // alpha-blended quad in the framebuffer is fill rate on a phone,
            // and for most of a race — every corner, all of town — it is empty.
            bool draw = shown > 0.001f;
            if (image.enabled != draw) image.enabled = draw;
            if (!draw) return;

            // HudOnTop.Apply puts every Graphic under the HUD canvas on its
            // shared ZTest-Always material whenever the cluster rebuilds. This
            // shader already ignores depth, so it takes its own material back
            // rather than losing the overlay to a UI/Default quad of the
            // streak texture.
            if (image.material != mat) image.material = mat;

            scroll += v * dt * ScrollPerMetre;
            // Keep the scroll small: a float that has counted a whole race's
            // distance has lost the sub-texel precision the streaks need.
            if (scroll > 1024f) scroll -= 1024f;

            mat.SetFloat(IntensityId, shown);
            mat.SetFloat(ScrollId, scroll);
            mat.SetFloat(AspectId, cam != null ? cam.aspect : 16f / 9f);
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }
    }
}
