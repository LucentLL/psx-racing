using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing
{
    /// <summary>
    /// THE OWNER'S COCKPIT, v08 (2026-10-16). One interior shared by every car
    /// in COCKPIT view, now in both drive sides: "RHD when real car is RHD.
    /// Same for LHD." It replaces the v07 cabin of 2026-10-14 (his first
    /// universal cockpit: OBJs, five needles on a printed atlas).
    ///
    /// THE MODEL is <see cref="CockpitModel"/>: his LHD file converted by
    /// tools/cockpit/cockpit_v08.py into one quantized geometry file, PBR trim
    /// on PSX/Cabin (his normal and roughness maps, lit by our sun, sky,
    /// lamps and night). An RHD car (<see cref="DriveSide"/>) shows the SAME
    /// parts under a -1 x scale - the owner's RHD file is the exact mirror of
    /// his LHD one - with the gauges trading roles so the order stays temp,
    /// tach, speedo, fuel from the driver's left, and their canvases flipped
    /// back to read left-to-right. The cargo is never mirrored: it rides an
    /// unmirrored frame on whichever seat is the passenger's.
    ///
    /// THE FIT, per car, as v07's: his Driver_Eye sits on the game's eye
    /// (ChaseCamera.CockpitEye, on the driver's side of THIS car) and the
    /// model is scaled UNIFORMLY about it until the front of the dash lands
    /// on the shell's cowl (about x0.36). A uniform scale about the eye
    /// changes no angle: every car gets the owner's picture exactly. The lens
    /// is his camera's: 53.7 degrees vertical, 5.83 degrees down.
    ///
    /// THE GAUGES are drawn in game on his four blank faces
    /// (<see cref="CockpitGauges"/>); the 2D cluster stands down entirely in
    /// this view (<see cref="GaugesShowing"/>) - the gear is in the tach's LCD.
    ///
    /// THE GLASS carries the rain (PSX/CabinGlass): while it shows, the lens
    /// drops are off (<see cref="GlassShowing"/>) - "rain should stop at the
    /// windows, not on camera". The rain particles already fade out inside
    /// 1.2 m of the camera (PSX/Rain), which is outside this cabin, so none
    /// fall inside it.
    ///
    /// THE MIRROR is live: a small camera looking back renders into the
    /// mirror face's own texture, every other frame, only while this shows.
    ///
    /// THE WINDSCREEN reflects the cabin, faintly (<see cref="CockpitReflection"/>:
    /// a planar, interior-only reflection; desktop with REFLECTIONS on).
    ///
    /// THE DRIVER'S ARMS (<see cref="CockpitArms"/>, 2026-10-16) hold the
    /// wheel, go hand over hand through its 1.25 turns and reach for the
    /// shifter and the handbrake; they ride the model, so RHD mirrors them.
    ///
    /// SHARP UNDER THE SPEED BLUR (every renderer carries
    /// SpeedBlur.CarRenderingLayer), ONLY THE DRIVER'S CAMERA SEES IT, and
    /// the driver's camera does not see the car's own shell (its faces would
    /// land inside this scaled cabin) - as v07.
    /// </summary>
    // After ChaseCamera has mounted the eye and CarBodyLean posed the body,
    // and before the canvases are laid out (the HUD is screen-space-camera
    // on the driver's camera): see THE CABIN RIDES THE BODY.
    [DefaultExecutionOrder(1000)]
    public class CockpitInterior : MonoBehaviour
    {
        /// <summary>The converter's layout (CockpitModel.L), or null.</summary>
        public static CockpitModel.Layout L => CockpitModel.L;

        /// <summary>Eye to the dash's front edge, authored metres.</summary>
        public static float ZRef => L != null ? L.dashFrontZ - L.eye.z : 1.31f;

        /// <summary>How far under his eye the line over the dash is by the
        /// time it reaches the dash's front edge, authored metres: the line
        /// from Driver_Eye over the dash where the windscreen meets it
        /// (<see cref="CockpitModel.Layout.screenBase"/>, 8.9 degrees down),
        /// carried on to the front. ChaseCamera stands the eye this far above
        /// the shell's cowl so that line meets the bonnet (2026-10-16).</summary>
        public static float SightDrop
        {
            get
            {
                if (L == null || L.screenBase.z < L.eye.z + 0.2f) return 0.205f;
                float slope = (L.eye.y - L.screenBase.y) / (L.screenBase.z - L.eye.z);
                return slope * (L.dashFrontZ - L.eye.z);
            }
        }

        /// <summary>His eye's distance off his cabin's centreline, authored metres.</summary>
        public static float EyeOffCentre => L != null ? Mathf.Abs(L.eye.x) : 0.405f;

        /// <summary>The fit's range. Life-size is 1 (ChaseCamera.CabinScale);
        /// a shell whose cowl is near its middle fits it smaller.</summary>
        public const float MinScale = 0.15f, MaxScale = 1.25f;
        /// <summary>Wheel degrees at full lock, each way: 1.25 turns (2026-10-16,
        /// the owner's clip turns 1-1.5 turns hand over hand; it was 135, a
        /// wheel that never needed the hands to leave it). Full lock is the
        /// car's widest road-wheel angle (CockpitView passes the drift lock),
        /// so a 45-degree drift lock is a 10:1 rack and 9 degrees at speed is
        /// a quarter turn. Visual only: the physics steering is untouched.</summary>
        public const float LockDeg = 450f;
        /// <summary>Margin of the near plane under the nearest visible surface.</summary>
        public const float NearMargin = 1.5f;
        /// <summary>The mirror's render: pixels, field, reach, and every Nth frame.</summary>
        public const int MirrorW = 256, MirrorH = 64, MirrorEvery = 2;
        public const float MirrorVFov = 13f;
        /// <summary>How long the glass takes to dry once the rain stops or
        /// the car is under cover, seconds.</summary>
        public const float DrySeconds = 25f;
        /// <summary>The cut stands this far outside the cabin's side glass.</summary>
        public const float CabinCutMargin = 0.01f;

        public struct Fit
        {
            public Vector3 eye;
            public float scale;
            public bool measured;
            public bool rhd;
            /// <summary>The windscreen's base (car frame z): the dash's front
            /// lands here and the shell is cut here (<see cref="CabinClip"/>).</summary>
            public float cutZ;
            /// <summary>The cabin's own room, car frame (set by Apply, 0 before):
            /// its side glass's half-width and its rearmost z. Only the shell
            /// inside it is cut; the door mirrors and the panels beside the
            /// cabin show through its glass (2026-10-19).</summary>
            public float cabinHalfW, cabinTailZ;
            /// <summary>The cabin's across-scale against its length scale
            /// (<see cref="WidthFitFor"/>; 1 = his cabin's own shape).</summary>
            public float widthFit;
            /// <summary>The cabin's height scale (0: as its length, the old
            /// uniform fit) - <see cref="Fit3"/>.</summary>
            public float scaleY;
            public float ScaleY => scaleY > 0f ? scaleY : scale;
            /// <summary>Fit3 placed it on a measured windscreen.</summary>
            public bool fit3;
        }

        /// <summary>The fit onto the car's own windscreen and mirrors
        /// (<see cref="CockpitFit"/>): pure, for the runtime, the check and
        /// the self-test. `f` is <see cref="FitFor"/>'s.</summary>
        public static Fit Fit3(Fit f, CarModelDef def, float ls, float lift, float ws)
        {
            f.scaleY = 0f;
            f.fit3 = false;
            float sAcross = WidthFitFor(def, ws, 1f);        // absolute, his cabin = 1
            bool row = CockpitFit.On && f.measured && def != null && L != null &&
                       CockpitFit.TryGet(def.key, out var r) && CockpitFit.Pane(out var pb, out var pt);
            if (!row)
            {
                f.widthFit = WidthFitFor(def, ws, f.scale);
                f.eye.x *= f.widthFit;
                return f;
            }
            CockpitFit.TryGet(def.key, out r);
            CockpitFit.Pane(out pb, out pt);
            float cowlZ = def.cowlZ * ls, cowlY = def.cowlY + lift;
            float hz = r.headerZ * ls, hy = r.headerY + lift - CockpitFit.Inset;
            // against the catalogue's middle (CockpitFit.CalibZ/Y, written by the probe)
            float sz = (cowlZ - hz) / Mathf.Max(0.05f, pb.z - pt.z) / CockpitFit.CalibZ;
            float sy = (hy - cowlY) / Mathf.Max(0.05f, pt.y - pb.y) / CockpitFit.CalibY;
            sz = Mathf.Clamp(sz, CockpitFit.MinScale, CockpitFit.MaxScaleZ);
            sy = Mathf.Clamp(sy, CockpitFit.MinScaleY, CockpitFit.MaxScaleY);
            float an = Mathf.Clamp(sy / sz, 1f / CockpitFit.MaxAniso, CockpitFit.MaxAniso);
            float g = Mathf.Sqrt(sy * sz);
            sy = g * Mathf.Sqrt(an);
            sz = g / Mathf.Sqrt(an);
            // the seat stays behind the car's own door mirrors (a door mirror
            // stands ahead of the driver: CockpitFit.MirrorAhead at least)
            if (DoorMirrors.TryGet(def.key, out var dm))
            {
                var yawQ = Quaternion.Euler(0f, def.bodyYaw, 0f);
                float mz = float.MaxValue;
                foreach (var gl in dm.glass) mz = Mathf.Min(mz, ((yawQ * gl.centre).z + def.bodyZOffset) * ls);
                if (mz < float.MaxValue)
                {
                    float szMin = (cowlZ - (mz - CockpitFit.MirrorAhead)) / Mathf.Max(0.05f, pb.z - L.eye.z);
                    sz = Mathf.Clamp(Mathf.Max(sz, szMin), CockpitFit.MinScale, CockpitFit.MaxScale);
                }
            }
            // across: the car's mirrors and its own side glass, never wider
            if (WidthFitOn && r.sideX > 0f)
                sAcross = Mathf.Min(sAcross, (r.sideX * Mathf.Abs(ws) - CockpitFit.Inset) / Mathf.Max(0.01f, AuthoredGlassX));
            sAcross = Mathf.Clamp(sAcross, MinWidthFit, 1f);
            var e = L.eye;
            f.scale = sz;
            f.scaleY = sy;
            f.widthFit = sAcross / sz;
            f.eye = new Vector3((f.rhd ? 1f : -1f) * Mathf.Abs(e.x) * sAcross,
                                cowlY - sy * (pb.y - e.y),
                                cowlZ - sz * (pb.z - e.z));
            f.cutZ = cowlZ;
            f.fit3 = true;
            return f;
        }

        /// <summary>
        /// THE CABIN FITS INSIDE THE CAR, ACROSS TOO (owner, 2026-11-11: "all
        /// cockpits should fit inside each car model, with mirrors properly
        /// mounted just outside the cockpit window"). The fit scaled his
        /// cabin by its LENGTH only (dash front on the cowl), so every car got
        /// his 1.43 m between the side glasses; on a narrow car (the CTRs,
        /// the E30) the shell's own door mirrors stand INSIDE that - from the
        /// seat they were behind the cabin's door and A-pillar, and the
        /// mirror's glass sat in the cabin. Now the cabin is narrowed (never
        /// widened) until its side glass stands <see cref="MirrorClear"/>
        /// inboard of the inner edge of the car's own mirrors (their housing,
        /// stalk and sail mount, DoorMirrors' measured box): the mirrors are
        /// outside its windows, as on the car. The wheel keeps its round
        /// shape; the driver's eye moves in with his seat. A shell with no
        /// measured mirror keeps 1. PSX_COCKPIT_WIDTHFIT=0: as before.
        /// </summary>
        public const float MirrorClear = 0.01f, MinWidthFit = 0.8f;

        static float authoredGlassX = -1f;
        /// <summary>His side glass's half-width, authored metres.</summary>
        public static float AuthoredGlassX
        {
            get
            {
                if (authoredGlassX > 0f) return authoredGlassX;
                var parts = CockpitModel.Parts;
                if (parts == null || L == null || L.glass == null) return 0.713f;
                float x = 0f;
                foreach (var g in L.glass)
                    if (g.key.StartsWith("door") && parts.TryGetValue(g.part, out var gp) && gp.mesh != null)
                        x = Mathf.Max(x, Mathf.Max(Mathf.Abs(gp.mesh.bounds.min.x), Mathf.Abs(gp.mesh.bounds.max.x)));
                if (x > 0f) authoredGlassX = x;
                return x > 0f ? x : 0.713f;
            }
        }

        public static bool WidthFitOn => System.Environment.GetEnvironmentVariable("PSX_COCKPIT_WIDTHFIT") != "0";

        /// <summary>The innermost point of the car's own door mirrors off its
        /// centreline, car metres (the body's across-scale applied), or 0
        /// when the shell has no measured mirror.</summary>
        public static float MirrorInnerX(CarModelDef def, float widthScale)
        {
            if (def == null || !DoorMirrors.TryGet(def.key, out var e)) return 0f;
            float inner = float.MaxValue;
            foreach (var g in e.glass)
                inner = Mathf.Min(inner, Mathf.Min(Mathf.Abs(g.min.x), Mathf.Abs(g.max.x)) * Mathf.Abs(widthScale));
            return inner < float.MaxValue ? inner : 0f;
        }

        /// <summary>The cabin's across-scale for this car at length-scale
        /// `scale` (pure, for the check and the self-test).</summary>
        public static float WidthFitFor(CarModelDef def, float widthScale, float scale)
        {
            if (!WidthFitOn) return 1f;
            float inner = MirrorInnerX(def, widthScale);
            if (inner <= 0f) return 1f;
            float sx = (inner - MirrorClear) / Mathf.Max(0.01f, AuthoredGlassX * scale);
            return Mathf.Clamp(sx, MinWidthFit, 1f);
        }

        /// <summary>
        /// THE STEERING WHEEL STAYS ROUND (2026-11-11 afternoon: on the narrowed
        /// CTRs the wheel, scaled with the cabin, was 0.84 as wide as tall).
        /// The cabin is fitted per axis (across, height, length), and a tilted
        /// wheel under that scale is an ellipse whatever its own scale is. So
        /// the wheel hangs from its own root beside the model, scaled
        /// UNIFORMLY by the cabin's smaller in-plane scale (across or height:
        /// never larger than the cabin it sits in), at the point where the
        /// model puts its pivot (mirrored with it on an RHD car). The arms
        /// still plan in the model's frame: `warp` maps the wheel's own frame
        /// into it (<see cref="CockpitArms.WheelWarp"/>), so the grips land on
        /// the drawn rim. PSX_COCKPIT_ROUNDWHEEL=0: the wheel scales with the
        /// cabin as before (warp 1).
        /// </summary>
        public static bool RoundWheelOn => System.Environment.GetEnvironmentVariable("PSX_COCKPIT_ROUNDWHEEL") != "0";

        /// <summary>The wheel root's place and scale under the cabin's root
        /// (the eye) and the model-frame warp of the wheel, for a fit. Pure,
        /// for the fit gate.</summary>
        public static void WheelFrame(Fit f, out Vector3 rootPos, out Vector3 rootScale, out Vector3 warp)
        {
            var lay = L;
            float sx = f.scale * (f.widthFit > 0f ? f.widthFit : 1f), sy = f.ScaleY, sz = f.scale;
            var ms = new Vector3(f.rhd ? -sx : sx, sy, sz);
            Vector3 eye = lay != null ? lay.eye : Vector3.zero;
            Vector3 piv = lay != null && lay.wheel != null ? lay.wheel.pivot : Vector3.zero;
            rootPos = Vector3.Scale(ms, piv - eye);
            if (!RoundWheelOn)
            {
                rootScale = ms;
                warp = Vector3.one;
                return;
            }
            float s = Mathf.Min(sx, sy);
            rootScale = new Vector3(f.rhd ? -s : s, s, s);
            warp = new Vector3(s / Mathf.Max(0.01f, sx), s / Mathf.Max(0.01f, sy), s / Mathf.Max(0.01f, sz));
        }

        /// <summary>The eye moved with its seat by the fit (<see cref="Fit3"/>),
        /// car metres, added to the chase camera's mount (shot tools add it
        /// too).</summary>
        public Vector3 EyeShift { get; private set; }
        public float EyeShiftX => EyeShift.x;

        /// <summary>The fit for a body: the eye where ChaseCamera puts it
        /// (on the car's driving side) and the scale that lands the dash's
        /// front on the cowl - 1, life-size, wherever the body has room.
        /// Static and pure, for the self-test.</summary>
        public static Fit FitFor(Vector3 boxCenter, Vector3 boxSize, CarModelDef def,
                                 float lengthScale = 1f, float lift = 0f, bool rhd = false)
        {
            Vector3 eye = ChaseCamera.MountOffset(ChaseCamera.View.Cockpit, boxCenter, boxSize, def, lengthScale, lift, rhd);
            Vector2 cowl = ChaseCamera.CowlLine(boxCenter, boxSize, def, lengthScale, lift, out bool measured);
            float reach = Mathf.Max(0.2f, cowl.x - eye.z);
            return new Fit
            {
                eye = eye,
                measured = measured,
                rhd = rhd,
                cutZ = cowl.x,
                scale = Mathf.Clamp(reach / Mathf.Max(0.1f, ZRef), MinScale, MaxScale),
            };
        }

        /// <summary>The nearest in-view point of wheel and hands at a fit scale.</summary>
        public static float NearestM(float scale) => (L != null ? L.nearForward : 0.21f) * scale;

        /// <summary>The near plane the cockpit wants at a fit scale, looking
        /// ahead.</summary>
        public static float NearClipFor(float scale) => NearClipFor(scale, 0f);

        /// <summary>The near plane for a head turned `towardPassenger` of the
        /// way to full look-over (negative: toward the driver's door, whose
        /// card is the nearest thing in the cabin). It eases from the forward
        /// value toward the nearer of it and that side's own, so the depth
        /// buffer keeps the forward plane down the road while the driver
        /// looks ahead.</summary>
        public static float NearClipFor(float scale, float towardPassenger)
        {
            float fwd = L != null ? L.nearForward : 0.21f;
            float side = L == null ? 0.14f : towardPassenger >= 0f ? L.nearPassenger : L.nearDoor;
            if (side <= 0f && L != null) side = L.nearLook;
            float t = ChaseCamera.Smooth01(Mathf.Clamp01(Mathf.Abs(towardPassenger) * 1.6f));
            float nearest = Mathf.Lerp(fwd, Mathf.Min(fwd, side), t);
            return Mathf.Min(ChaseCamera.CockpitNearClip, nearest * scale / NearMargin);
        }

        public static float ActiveNearClip { get; private set; }
        /// <summary>The drawn gauges are on screen: the 2D cluster stands down.</summary>
        public static bool GaugesShowing { get; private set; }
        /// <summary>The cabin with the cargo on its seat is on screen.</summary>
        public static bool CargoInView { get; private set; }
        /// <summary>The cabin's glass is on screen: the rain is drawn on it,
        /// not on the lens.</summary>
        public static bool GlassShowing { get; private set; }

        // ------------------------------------------------------------------
        //  The instance
        // ------------------------------------------------------------------

        CarController car;
        Camera driver;
        Transform model, wheelRoot, wheelPivot, wheel, cargoRoot;
        Renderer[] rends;
        Renderer[] gaugeRends = new Renderer[4];
        string[] gaugeKeys = new string[4];
        Material[] glassMats;
        Material mirrorMat;
        CockpitReflection refl;
        CockpitModel.Part screenPart;
        BoxCollider box;
        CarBody body;
        GaugeCluster cluster; float nextClusterFind;
        FuelTank tank;
        EngineTemp temp;
        CockpitCargoMirror mirror;
        CockpitGauges gauges;
        Renderer[] shell;
        bool[] shellWas;
        bool shellHidden;
        bool fitted, shown;
        CarModelDef fitDef;
        Vector3 fitC, fitS;
        float fitLs, fitLift;
        bool fitRhd;
        float lastDeg = float.NaN, wheelDeg = float.NaN, wheelTarget;
        CockpitArms arms;
        Camera mirrorCam;
        RenderTexture mirrorRT;
        DoorMirrorRig doors;
        static float rainWet, runClock, blowClock;

        // RAIN 2, THE WIPERS (2026-11-16; the owner's driving clips: "the
        // wipers sweep arcs clear and the drops build up again"). No cockpit
        // had one. The blades are drawn by the windscreen's own shader; this
        // is their clock: a stroke (up and back) of WiperStroke seconds, one
        // every period, the period set by how hard it rains - intermittent in
        // light rain, continuous in steady rain, fast in a downpour. They run
        // while rain lands on the glass and the car is being driven, finish
        // the stroke they are on, and park WiperParkAfter seconds after the
        // car stops - so a parked car's screen beads over, as his parked clip
        // shows. Rain2.Wipers off (or PSX_RAIN2=0): no blades, no clock.
        public const float WiperParkDeg = 4f, WiperSweepDeg = 98f;
        public const float WiperParkAfter = 5f;
        /// <summary>Seconds into the stroke, the stroke's length, the cycle before it.</summary>
        static float wipeTc = 1000f, wipeStroke = 1.3f, wipePrev = 1000f, wipePeriod = 3.6f, wipeStill, wipeLastTick = -1f;
        static readonly int WipeId = Shader.PropertyToID("_CabinWipe"), Wipe2Id = Shader.PropertyToID("_CabinWipe2"),
                            Rain2GlassId = Shader.PropertyToID("_CabinRain2");
        /// <summary>For the checks: strokes started since the scene loaded.</summary>
        public static int WiperStrokes { get; private set; }

        /// <summary>A stroke's seconds and the cycle's, by how hard it rains
        /// (Rain2.RainNow: 0.55 light .. 1 a downpour).</summary>
        public static void WiperTiming(float rain, out float stroke, out float period)
        {
            if (rain > 0.86f) { stroke = 0.9f; period = 0.9f; }
            else if (rain > 0.68f) { stroke = 1.3f; period = 1.4f; }
            else { stroke = 1.3f; period = 3.6f; }
        }

        /// <summary>Seconds for a wiped pane to bead up again: the harder it
        /// rains and the faster the car, the sooner.</summary>
        public static float WiperFill(float rain, float wind) =>
            Mathf.Lerp(3.2f, 1.0f, Mathf.InverseLerp(0.55f, 1f, rain)) * (1f - 0.3f * wind);

        void TickWipers(float dt, float wind)
        {
            bool glass = Rain2.On && Rain2.Glass;
            Shader.SetGlobalVector(Rain2GlassId, new Vector4(glass && Rain2.CurrentTier == Rain2.Tier.High ? 1f : 0f,
                                                             glass && Rain2.CurrentTier != Rain2.Tier.Mobile ? 1f : 0f, 0f, 0f));
            if (!Rain2.On || !Rain2.Wipers) { Shader.SetGlobalVector(WipeId, Vector4.zero); return; }
            float rain = Rain2.RainNow;
            float v = car != null ? Mathf.Abs(car.forwardSpeed) : 0f;
            // The cabin ticks only while the cockpit shows, so the car's
            // standing time comes from Rain2Driver, which counts it in every
            // view (a car that stood a minute in the chase view has parked its
            // wipers when the driver looks out; the 11-17 play check caught
            // strokes starting there). A stroke under way when the view
            // changed has finished meanwhile.
            float now = Time.time, gap = wipeLastTick >= 0f ? now - wipeLastTick - dt : 0f;
            wipeLastTick = now;
            if (gap > 0.25f) wipeTc += gap;
            wipeStill = v > 1.5f ? 0f : wipeStill + dt;
            if (car != null && Rain2.StoodCar == car) wipeStill = Rain2.StoodFor;
            bool want = rain > 0.01f && WeatherFx.LensRain > 0.3f && wipeStill < WiperParkAfter;
            wipeTc += dt;
            if (want && wipeTc >= wipePeriod)
            {
                wipePrev = Mathf.Min(wipeTc, 1000f);
                wipeTc = 0f;
                WiperTiming(rain, out wipeStroke, out wipePeriod);
                WiperStrokes++;
            }
            if (wipeTc > 2000f) wipeTc = 1000f;
            Shader.SetGlobalVector(WipeId, new Vector4(wipeTc, wipeStroke, wipePrev, 1f));
            Shader.SetGlobalVector(Wipe2Id, new Vector4(WiperFill(Mathf.Max(rain, 0.55f), wind), 0f, 0f, 0f));
        }

        /// <summary>For the shot tool: the wipers <paramref name="into"/>
        /// seconds into a stroke in rain <paramref name="rain"/> (0.55..1) at
        /// <paramref name="speedMps"/>, as if they had been running; into
        /// below zero = parked, the screen beaded over. fitted false = none.</summary>
        public static void PreviewWipers(bool fitted, float rain, float speedMps, float into)
        {
            bool glass = Rain2.On && Rain2.Glass;
            Shader.SetGlobalVector(Rain2GlassId, new Vector4(glass && Rain2.CurrentTier == Rain2.Tier.High ? 1f : 0f,
                                                             glass && Rain2.CurrentTier != Rain2.Tier.Mobile ? 1f : 0f, 0f, 0f));
            if (!fitted || !Rain2.On || !Rain2.Wipers) { Shader.SetGlobalVector(WipeId, Vector4.zero); return; }
            float wind = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(5f, 28f, speedMps));
            WiperTiming(rain, out float stroke, out float period);
            Shader.SetGlobalVector(WipeId, into < 0f ? new Vector4(1000f, stroke, 1000f, 1f) : new Vector4(into, stroke, period, 1f));
            Shader.SetGlobalVector(Wipe2Id, new Vector4(WiperFill(Mathf.Max(rain, 0.55f), wind), 0f, 0f, 0f));
        }
        static bool warned;

        public Fit Current { get; private set; }

        // THE CABIN RIDES THE BODY (owner, 2026-10-24: "car body moves
        // separately from the cockpit, leaving gaps between door and hood").
        // The shell leans and rides its tyres (CarBodyLean, the bump stops)
        // off the car's frame, while this cabin and the driver's eye sat on
        // the car's frame itself: the bonnet, the door tops and the door
        // mirrors slid against the dash and the window frames, and the road
        // showed between them. The cabin (and the mirror cameras on it) and
        // the cut take the body's motion from its rest seat, so the car is
        // one piece. The EYE does not (owner, same day, on the first cut that
        // moved it with the body: "the body/cockpit don't move relative to
        // car's motion. Instead the UI ... move around the screen while the
        // cockpit and body are stiff and unmoving making the game feel flat
        // and 2D"): a head rides the car's frame and only follows the body a
        // little, late (EyeFollow, EyeLag), so the cabin and the bonnet roll,
        // pitch and bob round the view. All of it in LateUpdate, after the
        // chase camera and the lean (DefaultExecutionOrder) and before the
        // canvases: that first cut moved the eye at render time, after the
        // HUD had been laid out on the camera, and the HUD slid with it.
        public const float EyeFollow = 0.25f;   // share of the body's lean and rise the head takes...
        public const float EyeLag = 0.15f;      // ...this late (s)
        Quaternion rideRot = Quaternion.identity;
        Vector3 ridePos;
        Quaternion eyeRot = Quaternion.identity;
        Vector3 eyePos;
        bool eyeInit;
        Vector3 eyeBasePos, eyeSetPos;
        Quaternion eyeBaseRot, eyeSetRot;
        bool eyeSet;
        public Transform Model => model;
        /// <summary>The drawn steering wheel (for the grip gate).</summary>
        public Transform WheelTransform => wheel;
        public CockpitCargoMirror Mirror => mirror;
        public CockpitGauges Gauges => gauges;
        /// <summary>The driver's arms, or null (no arms data: the static hands).</summary>
        public CockpitArms Arms => arms;
        public bool RHD => Current.rhd;
        /// <summary>The live door mirrors, or null (a shell with no row).</summary>
        public DoorMirrorRig Doors => doors;
        public bool Shown => shown;
        /// <summary>The windscreen's reflection of the cabin, or null.</summary>
        public CockpitReflection Reflection => refl;

        public static CockpitInterior Ensure(CarController car, Camera driver)
        {
            if (car == null) return null;
            var have = car.GetComponentInChildren<CockpitInterior>(true);
            if (have != null) { have.driver = driver; return have; }
            // The model comes in an on-demand bundle: the first ask starts the
            // download, and until it lands the view is the seat camera alone.
            if (!CockpitModel.Ready) { CockpitModel.Request(); if (!CockpitModel.Ready) return null; }
            if (L == null || CockpitModel.Parts == null || CockpitModel.Mat("vinyl") == null)
            {
                if (!warned) Debug.LogWarning("[Cockpit] the v08 model, its layout or PSX/Cabin is missing - no 3D cabin.");
                warned = true;
                return null;
            }
            var go = new GameObject("CockpitInterior");
            go.SetActive(false);
            go.transform.SetParent(car.transform, false);
            go.layer = car.gameObject.layer;
            var ci = go.AddComponent<CockpitInterior>();
            ci.car = car;
            ci.driver = driver;
            ci.Build();
            if (!Application.isPlaying)
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    t.gameObject.hideFlags = HideFlags.DontSave;
            return ci;
        }

        void Build()
        {
            var parts = CockpitModel.Parts;
            var lay = L;
            model = new GameObject("Model").transform;
            model.SetParent(transform, false);
            Place(parts, "cabin", model);
            var mr = Place(parts, "mirror", model);
            var gaugeShader = Shader.Find("PSX/CabinGauge");
            if (mr != null && gaugeShader != null)
            {
                mirrorMat = new Material(gaugeShader) { name = "Cockpit_Mirror", hideFlags = HideFlags.HideAndDontSave };
                mirrorMat.SetFloat("_Mode", 1f);
                mr.sharedMaterial = mirrorMat;
            }
            for (int i = 0; i < 4; i++)
            {
                gaugeKeys[i] = lay.gauges[i].key;
                gaugeRends[i] = Place(parts, lay.gauges[i].part, model);
            }
            var glassShader = Shader.Find("PSX/CabinGlass");
            glassMats = new Material[lay.glass.Length];
            var panes = new System.Collections.Generic.List<Renderer>();
            Renderer screen = null;
            for (int i = 0; i < lay.glass.Length; i++)
            {
                var g = lay.glass[i];
                var r = Place(parts, g.part, model);
                if (r != null) panes.Add(r);
                if (g.key == "windshield" && r != null) { screen = r; parts.TryGetValue(g.part, out screenPart); }
                if (r == null || glassShader == null) continue;
                glassMats[i] = GlassMaterial(glassShader, g);
                r.sharedMaterial = glassMats[i];
            }
            BuildReflection(screen, panes);

            // The wheel stands on its own uniformly scaled root beside the
            // model (WheelFrame), so the cabin's per-axis fit cannot make it oval.
            wheelRoot = new GameObject("WheelRoot").transform;
            wheelRoot.SetParent(transform, false);
            wheelPivot = new GameObject("WheelPivot").transform;
            wheelPivot.SetParent(wheelRoot, false);
            wheelPivot.localPosition = Vector3.zero;
            wheelPivot.localRotation = lay.wheel.rot;
            wheel = new GameObject("SteeringWheel").transform;
            wheel.SetParent(wheelPivot, false);
            Place(parts, "wheel", wheel);
            // The driver's arms (CockpitArms): they hold the rim, go hand over
            // hand and reach for the shifter. Without their data, the old
            // static hands posed in the wheel's own frame ride it instead.
            arms = CockpitArms.Create(model, lay, gameObject.layer);
            if (arms == null) Place(parts, "hands", wheel);

            // The cargo's frame: never mirrored (its motion is the car's).
            cargoRoot = new GameObject("CargoMirror").transform;
            cargoRoot.SetParent(transform, false);
            CollectRenderers();
        }

        /// <summary>The windscreen's reflection of the cabin
        /// (<see cref="CockpitReflection"/>): a second renderer on the
        /// screen's mesh, under PSX/CabinGlass's rain.</summary>
        void BuildReflection(Renderer screen, System.Collections.Generic.List<Renderer> panes)
        {
            var sh = Shader.Find("PSX/CabinGlassReflect");
            var mf = screen != null ? screen.GetComponent<MeshFilter>() : null;
            if (sh == null || mf == null || mf.sharedMesh == null || screenPart == null) return;
            var go = new GameObject(screen.name + "_reflect");
            go.layer = gameObject.layer;
            go.transform.SetParent(screen.transform.parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var r = go.AddComponent<MeshRenderer>();
            var m = new Material(sh) { name = "Cockpit_Glass_Reflect", hideFlags = HideFlags.HideAndDontSave };
            m.SetFloat("_Gain", CockpitReflection.Gain);
            m.SetFloat("_Peak", CockpitReflection.Peak);
            m.SetFloat("_Lift", CockpitReflection.Lift);
            r.sharedMaterial = m;
            panes.Add(r);
            refl = gameObject.AddComponent<CockpitReflection>();
            refl.Init(this, r, m, panes);
        }

        /// <summary>A part as a renderer under `parent`, its submeshes on the
        /// shared materials (a per-instance material replaces them after).</summary>
        Renderer Place(System.Collections.Generic.Dictionary<string, CockpitModel.Part> parts, string name, Transform parent)
        {
            if (parts == null || !parts.TryGetValue(name, out var p)) { Debug.LogWarning("[Cockpit] missing part " + name); return null; }
            var go = new GameObject(name);
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = p.mesh;
            var r = go.AddComponent<MeshRenderer>();
            var m = new Material[p.mats.Length];
            for (int i = 0; i < m.Length; i++) m[i] = CockpitModel.Mat(p.mats[i]);
            r.sharedMaterials = m;
            return r;
        }

        static Material GlassMaterial(Shader sh, CockpitModel.PaneDef g)
        {
            var m = new Material(sh) { name = "Cockpit_Glass_" + g.key, hideFlags = HideFlags.HideAndDontSave };
            float mu = Mathf.Max(0.01f, g.metresU), mv = Mathf.Max(0.01f, g.metresV);
            Vector2 Q(Vector2 uvDir) { var d = new Vector2(uvDir.x * mu, uvDir.y * mv); return d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.down; }
            Vector2 up = Q(g.up), rear = Q(g.rear), right = Q(g.right);
            bool screen = g.key == "windshield", side = g.key.StartsWith("door");
            // Wind: up the screen (and outward), back along the side glass;
            // the rear window is in the roof's lee - its drops just run down.
            Vector2 wind = screen ? up : side ? rear : -up;
            float centre = Vector2.Dot(new Vector2(0.5f * mu, 0.5f * mv), right);
            m.SetVector("_Metres", new Vector4(mu, mv, 0f, 0f));
            m.SetVector("_Down", -up);
            m.SetVector("_Wind", new Vector4(wind.x, wind.y, screen ? 1.6f : 0f, centre));
            m.SetVector("_Out", right);
            m.SetFloat("_PaneRain", screen ? 1f : side ? 0.55f : 0.45f);
            if (screen)
            {
                // RAIN 2, THE WIPERS (PSX/CabinGlass draws them; nothing shows
                // until CockpitInterior's clock says they are fitted). Two
                // blades in tandem, in the pane's own metres: pivots just
                // under its lower edge at 10 % and 52 % of its width, a reach
                // that clears the top edge when a blade stands upright, parked
                // lying along the base and sweeping WiperSweepDeg from there.
                float reach = Mathf.Min(0.46f * mu, 1.02f * mv);
                m.SetVector("_Wiper", new Vector4(0.10f * mu, 0.52f * mu, -0.04f * mv, reach));
                m.SetVector("_WiperArc", new Vector4(WiperParkDeg * Mathf.Deg2Rad, WiperSweepDeg * Mathf.Deg2Rad, 0.20f, 0.011f * Mathf.Max(mu, 0.5f)));
            }
            return m;
        }

        /// <summary>Every renderer this cabin draws, cargo copies included.</summary>
        internal void CollectRenderers()
        {
            rends = GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.renderingLayerMask |= SpeedBlur.CarRenderingLayer;
                SunShadows.Exclude(r.gameObject);
            }
            if (refl != null) refl.Collect(rends);
        }

        /// <summary>Re-measure when the body or the car's drive side changed.</summary>
        public void Refit()
        {
            if (car == null) return;
            if (box == null) box = car.GetComponent<BoxCollider>();
            if (body == null) body = car.GetComponent<CarBody>();
            Vector3 c = box != null ? box.center : new Vector3(0f, 0.72f, 0.05f);
            Vector3 s = box != null ? box.size : new Vector3(1.72f, 1.0f, 4.1f);
            var def = body != null ? body.Def : null;
            float ls = body != null ? body.LengthScale : 1f;
            float lift = body != null ? body.BodyLift : 0f;
            bool rhd = DriveSide.IsRHD(car);
            if (fitted && def == fitDef && c == fitC && s == fitS && ls == fitLs && lift == fitLift && rhd == fitRhd)
            {
                if (gauges != null) gauges.Refresh(car);
                return;
            }
            fitted = true; fitDef = def; fitC = c; fitS = s; fitLs = ls; fitLift = lift; fitRhd = rhd;
            Apply(FitFor(c, s, def, ls, lift, rhd));
        }

        void Apply(Fit f)
        {
            var lay = L;
            // The cabin onto the car's own windscreen and mirrors (Fit3): the
            // eye moves with its seat; the chase camera's mount is moved by
            // the difference (EyeShift).
            Vector3 eye0 = f.eye;
            f = Fit3(f, fitDef, fitLs, fitLift, body != null ? body.WidthScale : 1f);
            EyeShift = f.eye - eye0;
            Current = f;
            transform.localPosition = f.eye;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            // Driver_Eye on the game's eye, everything scaled about it; an RHD
            // car sees the mirror image.
            float sxAcross = f.scale * f.widthFit;
            model.localScale = new Vector3(f.rhd ? -sxAcross : sxAcross, f.ScaleY, f.scale);
            model.localPosition = -Vector3.Scale(model.localScale, lay.eye);
            // The wheel ROUND on every car (WheelFrame), the arms gripping it
            // where it is drawn (CockpitArms.WheelWarp).
            WheelFrame(f, out var wPos, out var wScale, out var warp);
            if (wheelRoot != null)
            {
                wheelRoot.localPosition = wPos;
                wheelRoot.localRotation = Quaternion.identity;
                wheelRoot.localScale = wScale;
            }
            if (wheelPivot != null) wheelPivot.localScale = Vector3.one;
            if (arms != null) arms.WheelWarp = warp;
            PlaceMirrorCamera();
            model.localRotation = Quaternion.identity;
            var eyeV = new Vector3(f.rhd ? -lay.eye.x : lay.eye.x, lay.eye.y, lay.eye.z);
            cargoRoot.localScale = Vector3.one * f.scale;
            cargoRoot.localPosition = -new Vector3(eyeV.x * f.widthFit * f.scale, eyeV.y * f.ScaleY, eyeV.z * f.scale);
            if (mirror != null) mirror.Clear();
            mirror = new CockpitCargoMirror(cargoRoot, f.rhd ? lay.MirroredCargo() : lay);
            CockpitLook.PassengerSign = f.rhd ? -1f : 1f;

            // The gauges: roles by where each face is, as the driver sees it.
            if (gauges != null) gauges.Destroy();
            gauges = new CockpitGauges();
            for (int i = 0; i < 4; i++)
                gauges.Add(f.rhd ? Swap(gaugeKeys[i]) : gaugeKeys[i], gaugeRends[i], f.rhd);
            gauges.Refresh(car);
            if (mirrorMat != null && lay.mirror != null)
                mirrorMat.SetFloat("_FlipU", lay.mirror.uRight * (f.rhd ? -1 : 1) > 0 ? 1f : 0f);
            lastDeg = float.NaN;
            wheelDeg = float.NaN;
            ActiveNearClip = NearClipFor(f.scale);
            Current = CabinRoom(f);
            // The car's own door mirrors, live (DoorMirrors): built before
            // the renderers are collected, so the cabin's visibility rules
            // (the driver alone) cover their glass too.
            if (doors != null) doors.Destroy();
            // The car's widest point: the box is a 0.915 fit of the shell (CabinClip).
            float halfW = box != null ? box.size.x / 0.915f * 0.5f : 0.9f;
            doors = DoorMirrorRig.Build(transform, car != null ? car.transform : null, body, fitDef, gameObject.layer, halfW);
            if (doors != null) doors.SetActive(shown);
            CollectRenderers();
            CollectShell();
            if (refl != null) refl.Fit(model, screenPart);
        }

        /// <summary>The cabin's own room in the car's frame (CabinClip): out
        /// to its side glass (the door panes' outermost point, at its sill),
        /// back to its rearmost part. The shell outside it - the door
        /// mirrors, the door tops, a muscle car's haunches, the deck behind
        /// the rear glass - stays, seen through the glass.</summary>
        Fit CabinRoom(Fit f)
        {
            var parts = CockpitModel.Parts;
            var lay = L;
            if (parts == null || lay == null || lay.glass == null) return f;
            float glassX = 0f;
            foreach (var g in lay.glass)
                if (g.key.StartsWith("door") && parts.TryGetValue(g.part, out var gp) && gp.mesh != null)
                    glassX = Mathf.Max(glassX, Mathf.Max(Mathf.Abs(gp.mesh.bounds.min.x), Mathf.Abs(gp.mesh.bounds.max.x)));
            if (glassX <= 0f || !parts.TryGetValue("cabin", out var cp) || cp.mesh == null) return f;
            f.cabinHalfW = glassX * f.scale * (f.widthFit > 0f ? f.widthFit : 1f) + CabinCutMargin;
            f.cabinTailZ = f.eye.z + f.scale * (cp.mesh.bounds.min.z - lay.eye.z);
            return f;
        }

        /// <summary>The mirrored cluster's roles: the LHD file's tach face sits
        /// where the RHD driver's speedo is, its temp where the fuel is.</summary>
        public static string Swap(string key) =>
            key == "tach" ? "speed" : key == "speed" ? "tach" : key == "temp" ? "fuel" : key == "fuel" ? "temp" : key;

        /// <summary>The car's own renderers, sorted for a camera inside it: the
        /// body and its lamp covers CUT at the windscreen (marked for
        /// <see cref="CabinClip"/>), anything else under the car hidden
        /// outright as before. Its shadow stays.</summary>
        void CollectShell()
        {
            if (shellHidden) HideShell(false);
            EnsureInside();
            var list = new System.Collections.Generic.List<Renderer>();
            if (car != null)
                foreach (var r in car.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.transform.IsChildOf(transform)) continue;
                    if (r == insideR) { CabinClip.Mark(r); continue; }
                    var m = r.sharedMaterial;
                    if (m != null && m.shader != null && m.shader.name == "PSX/Shadow") continue;
                    if (CabinClip.Cuts(r)) { CabinClip.Mark(r); continue; }
                    list.Add(r);
                }
            shell = list.ToArray();
            shellWas = new bool[shell.Length];
        }

        /// <summary>
        /// NO VIEW OUT THROUGH THE CAR'S OWN NOSE (owner, 2026-11-11: "I can
        /// see the road ahead under my hood in cockpit mode ... I should [not]
        /// be able to see through the front bumper area"). The shell is one
        /// skin drawn Cull Back: cut at the windscreen's base, any sightline
        /// that passes under the bonnet's back edge (a low eye, a nose-up
        /// body, a cowl panel lower than the bonnet) went into the hollow nose
        /// and out through it - every inside face skipped. A second renderer
        /// of the same body mesh, on a copy of its material with its FRONT
        /// faces culled and drawn near-black (PSX/CarPaint _PSXInside): the
        /// underside of the bonnet, the inner wings, the bumper's inside and
        /// the bay floor - drawn only for the driver's camera while the cut is
        /// open, and only ahead of the cut (the cabin's own glass and the
        /// doors beside it are never darkened). The bonnet's top is in front
        /// of it from the seat, so the view over it is as it was.
        /// PSX_COCKPIT_INSIDE=0: no inside (as before).
        /// </summary>
        MeshRenderer insideR;
        MeshFilter insideF;
        Material insideMat;
        Material insideSrc;
        static readonly int CullId = Shader.PropertyToID("_PSXCull"), InsideId = Shader.PropertyToID("_PSXInside");
        public static bool InsideOn = System.Environment.GetEnvironmentVariable("PSX_COCKPIT_INSIDE") != "0";

        void EnsureInside()
        {
            if (!InsideOn || body == null || body.bodyFilter == null || body.bodyRenderer == null) return;
            var src = body.bodyRenderer.sharedMaterial;
            if (src == null || !src.HasProperty(InsideId)) return;
            if (insideR == null)
            {
                var go = new GameObject("CabinInside");
                go.layer = body.bodyFilter.gameObject.layer;
                if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
                go.transform.SetParent(body.bodyFilter.transform, false);
                insideF = go.AddComponent<MeshFilter>();
                insideR = go.AddComponent<MeshRenderer>();
                insideR.shadowCastingMode = ShadowCastingMode.Off;
                insideR.receiveShadows = false;
                insideR.lightProbeUsage = LightProbeUsage.Off;
                insideR.reflectionProbeUsage = ReflectionProbeUsage.Off;
                insideR.forceRenderingOff = true;
                SunShadows.Exclude(go);
            }
            if (insideMat == null || insideSrc != src)
            {
                if (insideMat != null) DestroyImmediate(insideMat);
                insideMat = new Material(src) { name = "CabinInside", hideFlags = HideFlags.HideAndDontSave };
                insideMat.SetFloat(CullId, 1f);
                insideMat.SetFloat(InsideId, 1f);
                insideSrc = src;
            }
            insideR.sharedMaterial = insideMat;
            insideR.renderingLayerMask = body.bodyRenderer.renderingLayerMask;
            insideF.sharedMesh = body.bodyFilter.sharedMesh;
            CabinClip.Mark(insideR);
        }

        /// <summary>The shell as a camera inside the car sees it (and back):
        /// cut at the windscreen, the rest of what hangs off the car hidden.
        /// Public for the shot tool, which renders in edit mode with no camera
        /// events.</summary>
        public void HideShell(bool hide) => HideShell(hide, false);

        /// <summary><paramref name="whole"/>: the whole body behind the
        /// windscreen cut, not only the cabin's room (the rear-view mirror).</summary>
        public void HideShell(bool hide, bool whole)
        {
            if (shell == null || hide == shellHidden) return;
            if (insideR != null)
            {
                // The body's mesh as it is now (the lamps-up and lens bodies swap it).
                if (hide && body != null && body.bodyFilter != null && insideF.sharedMesh != body.bodyFilter.sharedMesh)
                    insideF.sharedMesh = body.bodyFilter.sharedMesh;
                insideR.forceRenderingOff = !(hide && !whole && InsideOn);
            }
            for (int i = 0; i < shell.Length; i++)
            {
                if (shell[i] == null) continue;
                if (hide) { shellWas[i] = shell[i].forceRenderingOff; shell[i].forceRenderingOff = true; }
                else shell[i].forceRenderingOff = shellWas[i];
            }
            if (hide && car != null)
            {
                if (box == null) box = car.GetComponent<BoxCollider>();
                Vector3 c = box != null ? box.center : new Vector3(0f, 0.72f, 0.05f);
                Vector3 s = box != null ? box.size : new Vector3(1.72f, 1.0f, 4.1f);
                // The cabin's room only (2026-10-19), each door mirror kept
                // whole where a narrow car's reaches inside it; the whole body
                // box when the cabin was not measured.
                var w2l = RideW2L;
                if (!whole && Current.cabinHalfW > 0f)
                {
                    CabinClip.Open(w2l, Current.cutZ, Current.cabinHalfW, c.y + s.y * 0.5f + 0.8f, Current.cabinTailZ);
                    if (doors != null) doors.Keep();
                }
                else CabinClip.Open(w2l, Current.cutZ, c, s);
            }
            else CabinClip.Close();
            shellHidden = hide;
        }

        /// <summary>
        /// Draw the built cabin once, off screen, while it is still hidden: the
        /// first frame a material is drawn is the frame its shaders and
        /// pipeline states are compiled, and that was the half-second freeze
        /// on the first switch to the cockpit. Four views from the cabin's
        /// middle into a small throwaway target, then hidden again.
        /// </summary>
        public void Prewarm()
        {
            bool was = gameObject.activeSelf;
            if (!was) gameObject.SetActive(true);
            var rs = GetComponentsInChildren<Renderer>(false);
            if (rs.Length == 0) { if (!was) gameObject.SetActive(false); return; }
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            var go = new GameObject("CockpitPrewarm");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 120f;
            cam.aspect = 1f;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 20f;
            var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            var rt = RenderTexture.GetTemporary(128, 128, 16, RenderTextureFormat.ARGB32);
            Vector3 fwd = car != null ? car.transform.forward : Vector3.forward, up = car != null ? car.transform.up : Vector3.up;
            go.transform.position = b.center;
            // the cabin draws only for the driver's camera (OnBeginCamera): this one stands in
            var drv = driver;
            driver = cam;
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    go.transform.rotation = Quaternion.AngleAxis(90f * i, up) * Quaternion.LookRotation(fwd, up);
                    var req = new RenderPipeline.StandardRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                    else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
                }
            }
            finally { driver = drv; }
            RenderTexture.ReleaseTemporary(rt);
            Destroy(go);
            if (!was) gameObject.SetActive(false);
        }

        /// <summary>Cabin on or off; the wheel and hands separately - a phone
        /// has its own wheel under the player's thumb.</summary>
        public void Show(bool on, bool wheelOn)
        {
            if (gameObject.activeSelf != on) gameObject.SetActive(on);
            if (wheelPivot != null && wheelPivot.gameObject.activeSelf != wheelOn)
                wheelPivot.gameObject.SetActive(wheelOn);
            // No wheel (a phone's is under the thumb): no hands to hold it.
            if (arms != null) arms.Show(wheelOn);
            // Every renderer under the car as the view opens is sorted (cut or
            // hidden): one added since the fit (a lens cover, a part fitted at
            // home) would otherwise stand inside the cabin, uncut.
            if (on && !shown && fitted) CollectShell();
            shown = on;
            GaugesShowing = on && gauges != null;
            GlassShowing = on && glassMats != null && glassMats.Length > 0;
            CargoInView = on;
            if (on)
            {
                ActiveNearClip = NearClipFor(Current.scale > 0f ? Current.scale : 0.357f);
                CockpitLook.PassengerSign = Current.rhd ? -1f : 1f;
            }
            if (mirrorCam != null && mirrorCam.gameObject.activeSelf != on) mirrorCam.gameObject.SetActive(on);
            if (doors != null) doors.SetActive(on);
        }

        /// <summary>Turn the wheel with the front ROAD wheels: full lock is
        /// <see cref="LockDeg"/> each way. Positive = anticlockwise as the
        /// driver sees it (the mirrored RHD wheel turns the other way in its
        /// own frame).</summary>
        public void SetSteer(float roadWheelDeg, float fullLockDeg)
        {
            if (wheel == null) return;
            float deg = -roadWheelDeg / Mathf.Max(5f, fullLockDeg) * LockDeg;
            deg = Mathf.Clamp(deg, -LockDeg * 1.1f, LockDeg * 1.1f);
            if (Current.rhd) deg = -deg;
            wheelTarget = deg;
            // First frame (or a new fit / drive side): straight there, hands at rest.
            if (float.IsNaN(wheelDeg))
            {
                wheelDeg = deg;
                ApplyWheel();
                if (arms != null) arms.Plan.Reset(deg);
            }
        }

        /// <summary>The visual wheel follows the rack at most
        /// <see cref="CockpitArms.WheelMaxRate"/> (the hands must keep up);
        /// a still (dt 0) goes straight to it.</summary>
        void TickWheel(float dt)
        {
            if (float.IsNaN(wheelDeg)) return;
            wheelDeg = dt > 0f ? Mathf.MoveTowards(wheelDeg, wheelTarget, CockpitArms.WheelMaxRate * dt) : wheelTarget;
            ApplyWheel();
            if (arms != null) arms.Tick(dt, wheelDeg, car);
        }

        void ApplyWheel()
        {
            if (wheel == null || (!float.IsNaN(lastDeg) && Mathf.Abs(wheelDeg - lastDeg) < 0.1f)) return;
            lastDeg = wheelDeg;
            wheel.localRotation = Quaternion.Euler(0f, 0f, wheelDeg);
        }

        /// <summary>The visual wheel angle now (model frame, degrees).</summary>
        public float WheelDeg => float.IsNaN(wheelDeg) ? 0f : wheelDeg;

        /// <summary>The per-frame parts, for the profiler (tools\perf-probe.ps1).</summary>
        static readonly Unity.Profiling.ProfilerMarker RideMarker = new Unity.Profiling.ProfilerMarker("CockpitInterior.Ride"),
            WheelMarker = new Unity.Profiling.ProfilerMarker("CockpitInterior.WheelAndArms"),
            GaugesMarker = new Unity.Profiling.ProfilerMarker("CockpitInterior.Gauges"),
            MirrorMarker = new Unity.Profiling.ProfilerMarker("CockpitInterior.Mirrors");

        void LateUpdate()
        {
            if (!shown) { eyeInit = false; return; }
            using (RideMarker.Auto())
            {
                RideCabin();
                RideEye(Time.deltaTime, driver != null ? driver : Camera.main);
            }
            Tick(Time.deltaTime, PizzaCargo.Instance);
        }

        /// <summary>The per-frame work: gauges, rain, mirror, the cargo.
        /// Public for the shot tool, which has no frames to wait for.</summary>
        public void Tick(float dt, PizzaCargo cargo)
        {
            if (car != null)
            {
                // Looked for once a second, not every frame: when the HUD's
                // cluster is not there to find, the search walked the scene's
                // objects every frame (Charlotte holds thousands).
                if (cluster == null && Time.unscaledTime >= nextClusterFind)
                {
                    nextClusterFind = Time.unscaledTime + 1f;
                    cluster = FindFirstObjectByType<GaugeCluster>();
                }
                if (tank == null) tank = car.GetComponent<FuelTank>();
                if (temp == null) temp = car.GetComponent<EngineTemp>();
            }
            using (WheelMarker.Auto()) TickWheel(dt);
            if (gauges != null) using (GaugesMarker.Auto()) gauges.Tick(dt, car, cluster, tank, temp, ClusterBulbs.Backlit);
            ActiveNearClip = NearClipFor(Current.scale > 0f ? Current.scale : 0.357f,
                                         CockpitLook.Yaw * CockpitLook.PassengerSign / CockpitLook.MaxYaw);
            TickRain(dt);
            using (MirrorMarker.Auto()) TickMirror();
            if (cargo != null && cargo.Car != null && cargo.Car != car) cargo = null;
            if (mirror != null && mirror.Sync(cargo)) CollectRenderers();
        }

        /// <summary>The rain on the glass: how wet (eased in with the lens's
        /// rain, drying over <see cref="DrySeconds"/>), how much wind, and the
        /// two clocks the drops run on.</summary>
        void TickRain(float dt)
        {
            float target = WeatherFx.LensRain;
            rainWet = target >= rainWet ? target : Mathf.Max(target, rainWet - dt / DrySeconds);
            float speed = car != null ? Mathf.Abs(car.forwardSpeed) : 0f;
            float wind = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(5f, 28f, speed));
            runClock += dt * 0.27f;
            blowClock += dt * (0.4f + 2.6f * wind);
            if (runClock > 1000f) runClock -= 1000f;
            if (blowClock > 1000f) blowClock -= 1000f;
            Shader.SetGlobalFloat("_CabinRain", rainWet);
            Shader.SetGlobalFloat("_CabinWind", wind);
            Shader.SetGlobalFloat("_CabinRun", runClock);
            Shader.SetGlobalFloat("_CabinBlow", blowClock);
            TickWipers(dt, wind);
        }

        /// <summary>For the shot tool: the rain as if it had been raining
        /// for a while at this speed.</summary>
        public static void PreviewRain(float rain, float speedMps, float clock)
        {
            rainWet = rain;
            float wind = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(5f, 28f, speedMps));
            Shader.SetGlobalFloat("_CabinRain", rain);
            Shader.SetGlobalFloat("_CabinWind", wind);
            Shader.SetGlobalFloat("_CabinRun", clock * 0.27f);
            Shader.SetGlobalFloat("_CabinBlow", clock * (0.4f + 2.6f * wind));
        }

        void TickMirror()
        {
            if (!Application.isPlaying) return;
            // The door mirrors draw on the frames the rear-view does not, and
            // share those with the reflection probe (ExtraViews).
            if (doors != null) doors.Tick(driver != null ? driver : Camera.main);
            ExtraViews.Report(true, doors != null && doors.SeenAny);
            if (mirrorMat == null) return;
            if (mirrorCam == null) EnsureMirrorCamera();
            // the player's rate (ViewRatePrefs), or AUTO: every MirrorEvery-th frame
            if (mirrorCam != null)
                mirrorCam.enabled = ViewRatePrefs.Auto(ViewRatePrefs.View.RearMirror)
                    ? Time.frameCount % MirrorEvery == 0
                    : ViewRatePrefs.Due(ViewRatePrefs.View.RearMirror);
        }

        /// <summary>For the shot tool (edit mode, no frames): the mirror
        /// rendered once from `src`'s world.</summary>
        public void PreviewMirror(Camera src) => PreviewMirror(src, true);

        /// <summary><paramref name="whole"/> false: the cut as it was before
        /// 2026-10-24 (the cabin's room only: the deck and the wing showed).</summary>
        public void PreviewMirror(Camera src, bool whole)
        {
            if (driver == null) driver = src;
            if (mirrorCam == null) EnsureMirrorCamera();
            if (mirrorCam == null) return;
            mirrorCam.gameObject.SetActive(true);
            mirrorCam.enabled = false;
            // Edit mode has no camera events: hide what play mode hides from it.
            SetVisible(false);
            if (shellHidden) HideShell(false);
            HideShell(true, whole);
            mirrorCam.Render();
            HideShell(false);
            SetVisible(true);
        }

        /// <summary>For the shot tool (edit mode, no frames): the door
        /// mirrors rendered once from `src`'s world, the shell whole for
        /// them (they look at it from outside).</summary>
        public void PreviewDoorMirrors(Camera src)
        {
            if (doors == null) return;
            if (driver == null) driver = src;
            SetVisible(false);
            if (shellHidden) HideShell(false);
            CabinClip.Close();
            doors.Preview(src);
            SetVisible(true);
        }

        /// <summary>For the shot tool (edit mode, no frames): the windscreen's
        /// reflection drawn now, by <see cref="CockpitReflection.Wanted"/>.</summary>
        public void PreviewReflection()
        {
            if (refl != null) refl.Tick(true);
        }

        /// <summary>The rear-view's camera at the mirror as THIS fit places it
        /// (2026-11-11: it was placed once, when made, and kept the first
        /// car's place through every body swap - in a shorter car it stood in
        /// the shell behind the cabin and showed the car's own insides).</summary>
        void PlaceMirrorCamera()
        {
            if (mirrorCam == null || L == null || L.mirror == null || model == null) return;
            var mc = L.mirror.centre;
            mirrorCam.transform.localPosition = Vector3.Scale(model.localScale, mc) + model.localPosition;
            mirrorCam.transform.localRotation = Quaternion.Euler(2f, 180f, 0f);
        }

        /// <summary>A small camera at the mirror looking back, into the
        /// mirror face's texture. Play mode only (a preview tool's scene must
        /// not grow cameras). It renders the world as the driver's camera does
        /// (this cabin hides itself from every other camera).</summary>
        void EnsureMirrorCamera()
        {
            var src = driver != null ? driver : Camera.main;
            if (src == null || L == null || L.mirror == null || mirrorMat == null) return;
            // 24-bit depth (owner, 2026-10-16: the mirror "flickers in and out of
            // constantly overlapping layers"): at 16 bits a 0.3 m near plane
            // resolves half a metre at 100 m, and the road and the ground under
            // it traded places in black wedges every frame.
            mirrorRT = new RenderTexture(MirrorW, MirrorH, 24) { name = "CockpitMirror", filterMode = FilterMode.Bilinear, antiAliasing = 1 };
            var go = new GameObject("CockpitMirrorCamera");
            if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(transform, false);
            mirrorCam = go.AddComponent<Camera>();
            mirrorCam.CopyFrom(src);
            // AFTER CopyFrom, which also copies the source camera's transform:
            // at the mirror, on the centreline, looking straight back.
            PlaceMirrorCamera();
            go.tag = "Untagged";
            mirrorCam.targetTexture = mirrorRT;
            mirrorCam.aspect = MirrorW / (float)MirrorH;
            mirrorCam.fieldOfView = MirrorVFov;
            mirrorCam.nearClipPlane = 0.3f;
            // The DRIVER's far plane (2026-10-16; it was 160 m). The mirror
            // shows the world the windscreen shows: its edge fade, its far
            // skyline squeezed into the same band and culled by the same
            // bounds. At 160 m the world ended in a hard cut that things
            // popped in and out of, and the skyline - fitted to the driver's
            // 400-469 m band - was pulled in to 130-150 m by the mirror's own
            // plane, over the road, coming and going with the culling: the
            // black wedges in the owner's video.
            mirrorCam.farClipPlane = src.farClipPlane;
            // and the driver's per-layer distances (a stage's horizon: StageCulling)
            if (StageCulling.LayerDistances != null) mirrorCam.layerCullDistances = StageCulling.LayerDistances;
            mirrorCam.depth = src.depth - 1f;
            mirrorCam.useOcclusionCulling = false;
            mirrorCam.allowHDR = false;
            mirrorCam.allowMSAA = false;
            var data = go.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (data == null) data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;
            mirrorMat.SetTexture("_FaceTex", mirrorRT);
            go.SetActive(shown);
        }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            HideShell(false);
            SetVisible(true);
            shown = false;
            GaugesShowing = false;
            GlassShowing = false;
            CargoInView = false;
            CockpitLook.PassengerSign = 1f;
        }

        void OnDestroy()
        {
            if (mirror != null) mirror.Clear();
            if (gauges != null) gauges.Destroy();
            if (arms != null) arms.Destroy();
            if (doors != null) { doors.Destroy(); doors = null; }
            if (glassMats != null) foreach (var m in glassMats) if (m != null) DestroyImmediate(m);
            if (mirrorMat != null) DestroyImmediate(mirrorMat);
            if (mirrorRT != null) { mirrorRT.Release(); DestroyImmediate(mirrorRT); }
            if (insideR != null) DestroyImmediate(insideR.gameObject);
            if (insideMat != null) DestroyImmediate(insideMat);
            ActiveNearClip = 0f;
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            bool isDriver = driver != null ? cam == driver : StreetLights.IsDriver(cam);
            SetVisible(isDriver);
            // The door mirrors' glass sits on the body as it leans now.
            if (isDriver && doors != null) doors.Pose();
            // The mirror looks back from inside the shell too: whatever the
            // shell models inside (seats, a parcel shelf) would fill it - and
            // (owner, 2026-10-24: the rear-view "shows floating disconnecting
            // spoilers and trunks (should not show any body, just rear view)")
            // so would the deck and the wing behind the cabin, so for the
            // mirror the cut is the whole body behind the windscreen. The
            // door mirrors look at the car from outside: the whole shell.
            bool rear = mirrorCam != null && cam == mirrorCam;
            if (isDriver || rear) HideShell(true, rear);
        }

        /// <summary>The body's motion from its rest seat in the car's frame:
        /// a car-frame point at rest is now at rideRot * p + ridePos.</summary>
        void Ride()
        {
            rideRot = Quaternion.identity;
            ridePos = Vector3.zero;
            if (car == null) return;
            if (body == null) body = car.GetComponent<CarBody>();
            var br = body != null ? body.bodyRoot : null;
            if (br == null || body.Def == null || br.parent != car.transform) return;
            rideRot = br.localRotation * Quaternion.Inverse(Quaternion.Euler(0f, body.Def.bodyYaw, 0f));
            ridePos = br.localPosition - rideRot * body.SeatPosition;
        }

        /// <summary>A pose mounted on the car's frame, moved with the body.</summary>
        void RideOnto(ref Vector3 pos, ref Quaternion rot, Quaternion q, Vector3 t)
        {
            var cr = car.transform;
            Vector3 lp = cr.InverseTransformPoint(pos);
            Quaternion lq = Quaternion.Inverse(cr.rotation) * rot;
            pos = cr.TransformPoint(q * lp + t);
            rot = cr.rotation * (q * lq);
        }

        /// <summary>For the shot tool (edit mode, no frames): the cabin onto
        /// the body as it stands now and the eye mounted at pos/rot moved
        /// with it - or, <paramref name="ride"/> false, both left on the
        /// car's frame as they were before 2026-10-24.</summary>
        public void PreviewRide(ref Vector3 pos, ref Quaternion rot, bool ride = true)
        {
            if (car == null) return;
            if (ride) Ride();
            else { rideRot = Quaternion.identity; ridePos = Vector3.zero; }
            transform.localPosition = rideRot * Current.eye + ridePos;
            transform.localRotation = rideRot;
            pos += car.transform.TransformVector(EyeShift);
            RideOnto(ref pos, ref rot, rideRot, ridePos);
        }

        /// <summary>For the shot tool: one frame of play's order - the cabin
        /// onto the body, the head stepped <paramref name="dt"/> - with the
        /// eye mounted at pos/rot moved by the head (a fresh run when
        /// <paramref name="reset"/>).</summary>
        public void PreviewStep(float dt, ref Vector3 pos, ref Quaternion rot, bool reset)
        {
            if (car == null) return;
            if (reset) eyeInit = false;
            RideCabin();
            StepEye(dt);
            pos += car.transform.TransformVector(EyeShift);
            RideOnto(ref pos, ref rot, eyeRot, eyePos);
        }

        /// <summary>The rear-view mirror's picture (the shot tool reads it).</summary>
        public RenderTexture MirrorTexture => mirrorRT;

        /// <summary>World to the car's rest frame riding the body: the cut.</summary>
        Matrix4x4 RideW2L => (car.transform.localToWorldMatrix * Matrix4x4.TRS(ridePos, rideRot, Vector3.one)).inverse;

        /// <summary>The cabin onto the body as it stands now.</summary>
        void RideCabin()
        {
            Ride();
            transform.localPosition = rideRot * Current.eye + ridePos;
            transform.localRotation = rideRot;
        }

        /// <summary>The head: EyeFollow of the body's motion, EyeLag late.</summary>
        void StepEye(float dt)
        {
            var wantRot = Quaternion.Slerp(Quaternion.identity, rideRot, EyeFollow);
            var wantPos = ridePos * EyeFollow;
            float k = eyeInit && dt > 0f ? 1f - Mathf.Exp(-dt / EyeLag) : 1f;
            eyeRot = Quaternion.Slerp(eyeRot, wantRot, k);
            eyePos = Vector3.Lerp(eyePos, wantPos, k);
            eyeInit = true;
        }

        /// <summary>The driver's eye onto the head's motion, once a frame. The
        /// chase camera mounts it on the car's frame every LateUpdate (before
        /// this); a frame it did not (nothing moved it since this did) starts
        /// again from the mount this frame started from, so the motion never
        /// piles up.</summary>
        void RideEye(float dt, Camera cam)
        {
            StepEye(dt);
            if (cam == null || car == null) return;
            var ct = cam.transform;
            var cr = car.transform;
            Vector3 bp = ct.position;
            Quaternion bq = ct.rotation;
            if (eyeSet && bp == eyeSetPos && bq == eyeSetRot) { bp = eyeBasePos; bq = eyeBaseRot; }
            eyeBasePos = bp;
            eyeBaseRot = bq;
            // the seat narrowed with the cabin (WidthFitFor)
            bp += cr.TransformVector(EyeShift);
            RideOnto(ref bp, ref bq, eyeRot, eyePos);
            ct.SetPositionAndRotation(bp, bq);
            eyeSetPos = ct.position;
            eyeSetRot = ct.rotation;
            eyeSet = true;
        }

        void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (shellHidden) HideShell(false);
        }

        void SetVisible(bool on)
        {
            if (rends == null) return;
            for (int i = 0; i < rends.Length; i++)
                if (rends[i] != null) rends[i].forceRenderingOff = !on;
        }
    }
}
