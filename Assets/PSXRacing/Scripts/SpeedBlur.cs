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

        /// <summary>The row's three words, in pref order.</summary>
        public static readonly string[] Names = { "OFF", "ZOOM", "MOTION" };

        static int cached = -1;

        /// <summary>
        /// THE MODE (2026-10-13). The pref was a bool stored as an int — 0 off,
        /// 1 on — and on meant the radial ZOOM, so those two numbers keep
        /// their meaning and MOTION is the new 2. A saved 1 is still ZOOM, a
        /// fresh install still gets ZOOM, and anything this build does not
        /// know is read as ZOOM rather than as nothing.
        /// </summary>
        public static SpeedBlurMode Mode
        {
            get
            {
                if (Forced.HasValue) return Forced.Value;
                if (cached < 0)
                {
                    cached = PlayerPrefs.GetInt(PrefKey, (int)SpeedBlurMode.Zoom);
                    if (cached < 0 || cached >= Names.Length) cached = (int)SpeedBlurMode.Zoom;
                }
                return (SpeedBlurMode)cached;
            }
            set
            {
                int v = Mathf.Clamp((int)value, 0, Names.Length - 1);
                if (cached == v) return;
                cached = v;
                PlayerPrefs.SetInt(PrefKey, v);
                PlayerPrefs.Save();
            }
        }

        /// <summary>For the check tools: a mode used for this session without
        /// being SAVED. The sandbox editor's PlayerPrefs are the owner's own,
        /// and a check that dies half way must not leave his game on MOTION.
        /// Null = the saved mode.</summary>
        public static SpeedBlurMode? Forced;

        /// <summary>Any blur at all. Setting it true keeps a mode that is
        /// already on (ZOOM or MOTION) and turns OFF into ZOOM.</summary>
        public static bool Enabled
        {
            get => Mode != SpeedBlurMode.Off;
            set
            {
                if (value == Enabled) return;
                Mode = value ? SpeedBlurMode.Zoom : SpeedBlurMode.Off;
            }
        }

        /// <summary>OFF -> ZOOM -> MOTION -> OFF.</summary>
        public static void Cycle(int step = 1)
        {
            int n = Names.Length;
            Mode = (SpeedBlurMode)((((int)Mode + step) % n + n) % n);
        }

        public static void Toggle() => Cycle(1);

        public static string Label => Names[(int)Mode];
    }

    /// <summary>The SPEED BLUR row. The numbers are the stored pref.</summary>
    public enum SpeedBlurMode
    {
        Off = 0,
        /// <summary>The radial NFS-Carbon smear into a tunnel (2026-09-19).</summary>
        Zoom = 1,
        /// <summary>Per-pixel camera motion blur (2026-10-13, after Touge
        /// Attack): every pixel smears along its own motion on the screen.</summary>
        Motion = 2,
    }

    /// <summary>
    /// MOTION BLUR, THE CAMERA HALF (2026-10-13). The owner approved it after
    /// studying Touge Attack, whose sense of speed players rave about and
    /// which does not zoom at all: each pixel smears along its OWN motion on
    /// the screen. Measured off its 2560x1440 frames at 90-140 km/h: trees
    /// and rock 2-5 m from the road streak 5-10% of the frame width at the
    /// edges, the sky and the far ridges stay sharp on a straight (moving
    /// forward does not move a point at infinity), a corner smears the whole
    /// periphery sideways (the lens yaws), and the player's car and the
    /// cabin stay crisp.
    ///
    /// HOW. The pass rebuilds each pixel's position from the depth buffer
    /// and asks where the PREVIOUS frame's lens saw it; the difference is the
    /// pixel's motion. This class keeps that previous lens per camera and
    /// hands the pass two matrices, both from THIS frame's view space:
    /// <c>reproj</c> to last frame's clip space (the real motion), and
    /// <c>rot</c> the same with both lenses' positions taken out — what the
    /// sky gets, because the sky is at infinity and only turning moves it.
    /// The matrices are relative (last view times this view's inverse) and
    /// built here in C#, so the world's kilometre-sized coordinates cancel on
    /// the CPU instead of eating the GPU's float precision.
    ///
    /// THE SHUTTER. A frame's motion over (1/60 s) / dt is the smear a 1/60 s
    /// exposure would show: the same picture at 30 fps and at 144. The
    /// SpeedBlur shutter fraction (the owner's 30/60/100/140 ladder, see
    /// <see cref="SpeedBlur.MotionStages"/>) scales it, and the pass clamps it
    /// to <see cref="MaxSmear"/> of the frame width.
    ///
    /// CUTS DO NOT SMEAR. A frame whose lens jumped (a teleport, a respawn, a
    /// view switch, a replay seek, a scene load, a frame the pass did not
    /// see) has no previous lens worth the name: that frame is drawn sharp
    /// and becomes the next frame's previous. Explicitly through
    /// <see cref="Cut"/> (SpeedBlur watches the car's TeleportCount and the
    /// view), and by measurement: further than <see cref="CutMinMetres"/> +
    /// <see cref="CutSpeedMps"/> x dt, or turned more than
    /// <see cref="CutDeg"/>, in one frame.
    /// </summary>
    public static class MotionBlurFx
    {
        /// <summary>The longest smear, as a fraction of the frame WIDTH: the
        /// top of what the reference showed at the edges (5-10%).</summary>
        public const float MaxSmear = 0.09f;
        /// <summary>The exposure the shutter fraction is a fraction of.</summary>
        public const float ShutterSeconds = 1f / 60f;
        /// <summary>A lens that moves further than CutMinMetres +
        /// CutSpeedMps x dt in one frame was cut, not driven (200 mph is
        /// 89 m/s).</summary>
        public const float CutMinMetres = 4f, CutSpeedMps = 90f;
        /// <summary>Or turns further than this in one frame.</summary>
        public const float CutDeg = 25f;
        /// <summary>Taps along the smear: the desktop's, and a phone's (or a
        /// 240-line picture's, where the smear is half as many pixels).</summary>
        public const int Taps = 16, PhoneTaps = 12;

        class History
        {
            public Matrix4x4 view, proj;
            public int frame = int.MinValue;
            public bool valid;
            // The last motion handed out: re-used for a second render of the
            // same frame, and while the lens is held still with the clock
            // stopped (a paused frame is a photograph and keeps its blur).
            public bool held;
            public Matrix4x4 reproj, rot;
            public float perFrame;
        }

        static readonly System.Collections.Generic.Dictionary<Camera, History> histories =
            new System.Collections.Generic.Dictionary<Camera, History>();

        // The preview tools' previous lens (edit mode has no frames).
        static Camera previewCam;
        static Matrix4x4 previewView, previewProj;
        static float previewDt;

        /// <summary>For the check tools: the pass writes each pixel's smear
        /// (and its depth) instead of smearing. Never set in the game.</summary>
        public static bool DebugVelocity;

        /// <summary>Forget <paramref name="cam"/>'s previous lens: its next
        /// frame is drawn sharp and starts a new history.</summary>
        public static void Cut(Camera cam)
        {
            if (cam != null && histories.TryGetValue(cam, out var h)) { h.valid = false; h.held = false; }
        }

        /// <summary>
        /// For the preview tools, where no frames run: render
        /// <paramref name="cam"/> as if its previous frame, <paramref name="dt"/>
        /// seconds ago, had been seen through <paramref name="prevView"/> /
        /// <paramref name="prevProj"/> (worldToCameraMatrix and the plain
        /// projectionMatrix). A null camera clears it.
        /// </summary>
        public static void PreviewSet(Camera cam, Matrix4x4 prevView, Matrix4x4 prevProj, float dt)
        {
            previewCam = cam;
            previewView = prevView;
            previewProj = prevProj;
            previewDt = dt;
        }

        /// <summary>A camera's view matrix at a pose, as Unity builds
        /// worldToCameraMatrix (camera space looks down -Z).</summary>
        public static Matrix4x4 ViewAt(Vector3 pos, Quaternion rot) =>
            Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(pos, rot, Vector3.one).inverse;

        /// <summary>
        /// This frame's motion for <paramref name="cam"/>, called as it
        /// RENDERS (the lens is final then). <paramref name="perFrame"/> is the
        /// exposure per unit of frame motion, (1/60 s) / dt. False = draw this
        /// frame sharp: there is no previous lens, or it was a cut. Called on
        /// every frame MOTION is chosen, whether or not the pass will run, so
        /// the first frame past 30 mph already has a history.
        /// </summary>
        public static bool Step(Camera cam, out Matrix4x4 reproj, out Matrix4x4 rot, out float perFrame)
        {
            reproj = rot = Matrix4x4.identity;
            perFrame = 0f;
            if (cam == null) return false;
            Matrix4x4 view = cam.worldToCameraMatrix;
            Matrix4x4 proj = cam.nonJitteredProjectionMatrix;

            if (previewCam != null && cam == previewCam)
                return Build(previewView, previewProj, view, proj, previewDt, out reproj, out rot, out perFrame);

            if (histories.Count > 8) Prune();
            if (!histories.TryGetValue(cam, out var h)) histories[cam] = h = new History();

            int frame = Time.frameCount;
            if (h.valid && h.frame == frame)
            {
                // The same frame rendered again: the same answer.
                reproj = h.reproj; rot = h.rot; perFrame = h.perFrame;
                return h.held;
            }

            bool ok = false;
            if (h.valid && h.frame == frame - 1)
            {
                if (Time.deltaTime <= 0f && h.held && Same(h.view, view) && Same(h.proj, proj))
                {
                    // The clock is stopped and the lens with it (the pause
                    // menu): keep the last motion, as the zoom keeps its smear.
                    reproj = h.reproj; rot = h.rot; perFrame = h.perFrame;
                    ok = true;
                }
                else
                {
                    float dt = Time.deltaTime > 0f ? Time.deltaTime : Time.unscaledDeltaTime;
                    ok = Build(h.view, h.proj, view, proj, dt, out reproj, out rot, out perFrame);
                }
            }

            h.view = view;
            h.proj = proj;
            h.frame = frame;
            h.valid = true;
            h.held = ok;
            h.reproj = reproj; h.rot = rot; h.perFrame = perFrame;
            LastMoved = ok;
            LastPerFrame = perFrame;
            LastFrame = frame;
            if (ok) SmearedFrames++; else CutFrames++;
            return ok;
        }

        /// <summary>Frames the history handed a motion, and frames it drew
        /// sharp as cuts, since the session began (the race check's note).</summary>
        public static int SmearedFrames, CutFrames;

        /// <summary>The last frame's answer from the frame-to-frame history
        /// (not the preview path), for the play check: whether it smeared,
        /// its exposure per unit of motion, and Time.frameCount.</summary>
        public static bool LastMoved;
        public static float LastPerFrame;
        public static int LastFrame;

        /// <summary>The two matrices from a previous lens to this one, or
        /// false for a cut.</summary>
        static bool Build(Matrix4x4 prevView, Matrix4x4 prevProj, Matrix4x4 view, Matrix4x4 proj, float dt,
                          out Matrix4x4 reproj, out Matrix4x4 rot, out float perFrame)
        {
            reproj = rot = Matrix4x4.identity;
            perFrame = 0f;
            dt = Mathf.Max(dt, 1e-4f);

            // Where each lens stood and which way it looked. Row 2 of a view
            // matrix is the lens's BACK, row 1 its up.
            Matrix4x4 camToWorld = view.inverse;
            Vector3 pos = camToWorld.GetColumn(3);
            Vector3 prevPos = prevView.inverse.GetColumn(3);
            if ((pos - prevPos).magnitude > CutMinMetres + CutSpeedMps * dt) return false;
            Vector3 back = view.GetRow(2), prevBack = prevView.GetRow(2);
            Vector3 up = view.GetRow(1), prevUp = prevView.GetRow(1);
            if (Vector3.Angle(back, prevBack) > CutDeg || Vector3.Angle(up, prevUp) > CutDeg) return false;

            // This view space -> last frame's clip space, relative, so the
            // world coordinates cancel here in C#.
            reproj = prevProj * (prevView * camToWorld);
            // The same with both lenses' positions taken out: turning only.
            Matrix4x4 r = view, pr = prevView;
            r.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
            pr.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
            rot = prevProj * (pr * r.transpose);
            perFrame = ShutterSeconds / dt;
            return true;
        }

        static bool Same(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++)
                if (Mathf.Abs(a[i] - b[i]) > 1e-6f) return false;
            return true;
        }

        /// <summary>Destroyed cameras (an old scene's) out of the table.</summary>
        static void Prune()
        {
            var dead = new System.Collections.Generic.List<Camera>();
            foreach (var kv in histories) if (kv.Key == null) dead.Add(kv.Key);
            foreach (var c in dead) histories.Remove(c);
        }
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
    /// THE SUN IN THE LENS (2026-10-13, the GT look plan, item 2). Gran
    /// Turismo 3's own list of effects (Sony, 2000) names "blinding sun glare
    /// hitting the windshield", and its box blurb says the eyes "adjust"
    /// when a car rounds a turn and the sun comes out from behind a building.
    /// Here the sun was a disc in the sky and a small halation ring
    /// (PSX/Blit), however hard the player drove into it.
    ///
    /// WHAT IT DRAWS (Shaders/PSXSunGlare.shader, a third pass of
    /// <see cref="SpeedBlurFeature"/>, after the lens and before the HUD): a
    /// hot white bloom on the sun, a wide glow round it, and a veil over the
    /// whole picture that is strongest with the sun in the middle of it —
    /// screen-blended, so it washes the frame toward the sun's colour
    /// instead of clipping it.
    ///
    /// HOW MUCH OF THE SUN IS SEEN is read off the frame itself: the sky
    /// writes its sun disc into the EMITTER MASK (the frame's alpha,
    /// PSXTone.cginc) and every opaque surface writes 0 there, so a dozen taps
    /// of alpha round the sun say how much of the disc a tower, a hill, a
    /// tree's leaves or another car has hidden. No rays: the trees' leaves
    /// have no colliders, and glare that ignored them would blaze through
    /// every avenue. One pixel of GPU work, no read-back (WebGL has none).
    ///
    /// THE EYE. That pixel also keeps an adapted level that follows the
    /// seen sun — quickly into the light, slowly back out of it — and the
    /// glare FLASHES by how far the sun is ahead of it: come out of a tunnel
    /// or round a building into a low sun and the frame blazes, then settles
    /// over a second. Kept on the GPU frame to frame, packed 16 bits a value
    /// into an RGBA8 pixel (no float targets needed on WebGL).
    ///
    /// WHEN. The hour's own sun (TimeOfDay.ShadowFor: a clear day 1, dawn and
    /// sunset 0.85, rain 0.15, fog 0.2, snow 0.45, nothing from dusk), on the
    /// LENS FX switch and under the same rules as the lens (off on foot, off
    /// from the top-down view, never over a HUD that could not be moved).
    /// Every interior applies no hour and gets none.
    ///
    /// Fields, not properties, by contract with the shot tools, like
    /// <see cref="LensFx"/>.
    /// </summary>
    public static class SunGlareFx
    {
        public const string ShaderName = "PSX/SunGlare";

        /// <summary>Below this sun strength nothing is drawn at all.</summary>
        public const float MinDrawn = 0.01f;
        /// <summary>The half-width of the patch of sky the meter reads, in
        /// degrees: inside the disc's full-strength core (1.5, PSX/Sky).</summary>
        public const float MeterDeg = 1.2f;
        /// <summary>Time constants, seconds: how fast the seen sun is
        /// followed (a little, so leaves flicker rather than strobe), how fast
        /// the eye closes down in the light, and how slowly it opens again.</summary>
        public const float SeenTau = 0.06f, CloseTau = 0.7f, OpenTau = 2.5f;

        /// <summary>The one camera the glare is drawn on; null = none.</summary>
        public static Camera Camera;
        /// <summary>The hour's sun, 0..1.</summary>
        public static float Strength;
        /// <summary>xy = where the sun is on the picture (viewport, which IS
        /// the pass's uv); z = 1 when it is in front of the lens; w = the
        /// meter's radius as a fraction of the picture's height.</summary>
        public static Vector4 Sun;
        /// <summary>The glare's colour: the sun's own, half way to warm white.</summary>
        public static Color Tint = Color.white;
        /// <summary>Seconds since the last frame (the eye's clock); 0 = held.</summary>
        public static float Dt;
        /// <summary>This frame starts the eye adapted to what it sees (no
        /// flash): the first frame after the glare was off, and every shot a
        /// tool takes unless it asks otherwise.</summary>
        public static bool Reset;
        /// <summary>A camera is named and the hour has a sun.</summary>
        public static bool Active;

        static bool wasActive;

        /// <summary>For the preview tools: publish the glare for
        /// <paramref name="cam"/> at <paramref name="strength"/>, the frame
        /// <paramref name="dt"/> seconds after the last one. A null camera
        /// clears it — call that after the shot.</summary>
        public static void PreviewSet(Camera cam, float strength, float dt, bool reset)
        {
            Publish(cam, strength, dt);
            if (cam != null) Reset = reset;
        }

        internal static void Publish(Camera cam, float strength, float dt)
        {
            bool has = cam != null && strength > MinDrawn;
            Camera = has ? cam : null;
            Strength = has ? Mathf.Clamp01(strength) : 0f;
            Dt = Mathf.Max(0f, dt);
            Reset = has && !wasActive;
            Active = has;
            wasActive = has;
        }

        /// <summary>
        /// Where the sun is on <paramref name="cam"/>'s picture, and its
        /// colour: called by <see cref="SpeedBlurFeature"/> as the camera
        /// RENDERS, not from Update. The chase camera moves in LateUpdate, and
        /// a sun placed from the pose before it would sit a frame behind the
        /// disc - in a fast turn, more than the meter's own radius, and the
        /// glare would flicker every time the car turned. False = no sun.
        /// </summary>
        public static bool Aim(Camera cam)
        {
            // The sun the player SEES - the sky's emitter disc, which for
            // the photographs is not the light's height (TimeOfDay.SunSeen).
            Vector3 dir = TimeOfDay.SunSeen;
            if (cam == null || dir.sqrMagnitude < 1e-6f) return false;
            dir.Normalize();
            var t = cam.transform;
            Vector3 vp = cam.WorldToViewportPoint(t.position + dir * 100f);
            float front = Vector3.Dot(t.forward, dir) > 0.05f && vp.z > 0f ? 1f : 0f;
            float radius = MeterDeg / Mathf.Max(cam.fieldOfView, 1f);
            Sun = new Vector4(vp.x, vp.y, front, radius);

            Color c = Shader.GetGlobalColor("_PSXLightColor");
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            c = m > 1e-4f ? new Color(c.r / m, c.g / m, c.b / m) : Color.white;
            Tint = Color.Lerp(c, new Color(1f, 0.96f, 0.88f), 0.5f);
            return true;
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
    ///
    /// SPEED BLUR: MOTION (2026-10-13) is the second look on the same row:
    /// per-pixel camera motion blur (<see cref="MotionBlurFx"/>) instead of
    /// the zoom, on the same faded speed, the same HUD split, the same car
    /// mask and the same view rule. ZOOM stays the default and is untouched.
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

        /// <summary>
        /// MOTION's half of the owner's ladder: (mph, shutter), the fraction
        /// of a 1/60 s exposure the camera blur gets at that speed. Straight
        /// lines between, nothing under 30 (on foot, parking, the menus), the
        /// last row held. The words are the same four as <see cref="Stages"/>;
        /// what they come to here, on a 480-line 16:9 picture, for a tree
        /// 4 m off the lens at the edge of the frame:
        ///
        ///  30  very subtle: it BEGINS. At 40 mph that tree streaks ~5 px.
        ///  60  noticeable: ~2.5% of the width (~20 px); the sky is sharp.
        /// 100  substantial: ~7% of the width. The verge is streaks.
        /// 140  extreme: past <see cref="MotionBlurFx.MaxSmear"/>, so the
        ///      whole roadside is at the clamp, and the near road with it.
        ///
        /// The reference (Touge Attack, 115 km/h = 71 mph) put 2-5 m roadside
        /// at 5-10% of the width: between the second and third rows here.
        /// </summary>
        public static readonly (float mph, float shutter)[] MotionStages =
        {
            ( 30f, 0.00f),
            ( 60f, 0.50f),
            (100f, 0.90f),
            (140f, 1.30f),
        };

        /// <summary>Below this the pass is not enqueued in MOTION.</summary>
        public const float MinDrawnShutter = 0.02f;

        /// <summary>MOTION's shutter fraction at a road speed in MPH.</summary>
        public static float ShutterForMph(float mph)
        {
            mph = Mathf.Abs(mph);
            var rows = MotionStages;
            if (mph <= rows[0].mph) return 0f;
            for (int i = 1; i < rows.Length; i++)
            {
                if (mph > rows[i].mph) continue;
                var a = rows[i - 1]; var b = rows[i];
                return Mathf.Lerp(a.shutter, b.shutter, (mph - a.mph) / (b.mph - a.mph));
            }
            return rows[rows.Length - 1].shutter;
        }

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
        /// <summary>Which blur the pass draws: ZOOM reads the strength, the
        /// tunnel and the focus above; MOTION reads <see cref="ActiveShutter"/>
        /// and the lens's own motion (<see cref="MotionBlurFx"/>).</summary>
        public static SpeedBlurMode ActiveMode { get; private set; } = SpeedBlurMode.Zoom;
        /// <summary>MOTION's shutter fraction for the faded speed.</summary>
        public static float ActiveShutter { get; private set; }

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
        // What MOTION's history was last checked against: a change in any of
        // them is a camera cut (MotionBlurFx.Cut).
        int seenTeleports = -1;
        ChaseCamera.View seenView;
        bool seenSuspended;
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
            // A scene load, or the component back on: whatever this lens saw
            // before is not the frame before this one.
            MotionBlurFx.Cut(cam);
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
            if (SunGlareFx.Camera == cam) SunGlareFx.Publish(null, 0f, 0f);
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

            WatchCuts();
            Publish(cam, LookFor(shownMps), focus);
            UpdateLens(safe, dt);
        }

        /// <summary>
        /// The camera cuts MOTION must not smear across, as far as this
        /// component can see them: the car put somewhere (TeleportTo — a
        /// respawn, the grid, the replay putting the player back), the view
        /// switched, the replay taking the lens or giving it back. The pass
        /// also refuses any frame whose lens jumped further than driving can
        /// move it, so a cut nobody announced is still a sharp frame.
        /// </summary>
        void WatchCuts()
        {
            int teleports = car != null ? car.TeleportCount : 0;
            var view = ChaseCamera.Current;
            if (teleports != seenTeleports || view != seenView || suspended != seenSuspended)
                MotionBlurFx.Cut(cam);
            seenTeleports = teleports;
            seenView = view;
            seenSuspended = suspended;
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
            // In the cockpit the rain is on the cabin's GLASS, not the lens
            // (owner, 2026-10-16: "rain should stop at the windows, not on
            // camera when in cockpit view"): PSX/CabinGlass draws it there.
            bool glass = CockpitInterior.GlassShowing && ChaseCamera.Current == ChaseCamera.View.Cockpit;
            float rain = on && !glass ? Mathf.Clamp01(WeatherFx.LensRain) : 0f;
            float night = on && sceneGlobals != null ? Mathf.Clamp01(sceneGlobals.night) : 0f;
            float flowTarget = on && !suspended && car != null
                ? Mathf.Clamp01(Mathf.Abs(car.forwardSpeed) * 3.6f / LensFx.FlowKmh)
                : 0f;
            lensFlow = Mathf.Lerp(lensFlow, flowTarget, 1f - Mathf.Exp(-LensFlowRate * dt));
            if (flowTarget <= 0f && lensFlow < 0.002f) lensFlow = 0f;
            lensClock += Time.deltaTime * (1.0 + LensFx.FlowLife * lensFlow);
            LensFx.Publish(on ? cam : null, rain, on ? LensFx.DirtFor(night, rain) : 0f,
                           lensFlow, (float)lensClock);
            // The sun in the lens, under the lens's own rules: the hour's sun
            // (0 from dusk and in every interior), on SCALED time so a paused
            // frame holds the eye where it was.
            float sunStrength = on && sceneGlobals != null ? sceneGlobals.sunShadow : 0f;
            SunGlareFx.Publish(on ? cam : null, sunStrength, Time.deltaTime);
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

        static void Publish(Camera camera, Stage look, Vector2 where) =>
            Publish(camera, look, where, SpeedBlurPrefs.Mode);

        /// <summary>The look for the pass. The faded speed (look.mph) drives
        /// both modes, so a switch from the pause menu, a respawn or a view
        /// change walks either one back through the owner's rows.</summary>
        static void Publish(Camera camera, Stage look, Vector2 where, SpeedBlurMode mode)
        {
            ActiveCamera = camera;
            ActiveStrength = camera != null ? look.strength : 0f;
            ActiveInner = look.inner;
            ActiveOuter = look.outer;
            ActiveFocus = where;
            ActiveMode = mode == SpeedBlurMode.Motion ? SpeedBlurMode.Motion : SpeedBlurMode.Zoom;
            ActiveShutter = camera != null ? ShutterForMph(look.mph) : 0f;
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
        /// camera clears it. ZOOM unless told otherwise, whatever the saved
        /// pref: a tool's pictures do not depend on the owner's menu. MOTION
        /// also needs the previous lens, from MotionBlurFx.PreviewSet.</summary>
        public static void Preview(Camera camera, float speedMps, Vector2? where = null,
                                   SpeedBlurMode mode = SpeedBlurMode.Zoom) =>
            Publish(camera, LookFor(camera != null ? speedMps : 0f), where ?? FrameCentre, mode);
    }
}
