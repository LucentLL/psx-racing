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
        public const float MirrorVFov = 13f, MirrorFar = 160f;
        /// <summary>How long the glass takes to dry once the rain stops or
        /// the car is under cover, seconds.</summary>
        public const float DrySeconds = 25f;

        public struct Fit
        {
            public Vector3 eye;
            public float scale;
            public bool measured;
            public bool rhd;
            /// <summary>The windscreen's base (car frame z): the dash's front
            /// lands here and the shell is cut here (<see cref="CabinClip"/>).</summary>
            public float cutZ;
        }

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
        Transform model, wheelPivot, wheel, cargoRoot;
        Renderer[] rends;
        Renderer[] gaugeRends = new Renderer[4];
        string[] gaugeKeys = new string[4];
        Material[] glassMats;
        Material mirrorMat;
        CockpitReflection refl;
        CockpitModel.Part screenPart;
        BoxCollider box;
        CarBody body;
        GaugeCluster cluster;
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
        static float rainWet, runClock, blowClock;
        static bool warned;

        public Fit Current { get; private set; }
        public Transform Model => model;
        public CockpitCargoMirror Mirror => mirror;
        public CockpitGauges Gauges => gauges;
        /// <summary>The driver's arms, or null (no arms data: the static hands).</summary>
        public CockpitArms Arms => arms;
        public bool RHD => Current.rhd;
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

            wheelPivot = new GameObject("WheelPivot").transform;
            wheelPivot.SetParent(model, false);
            wheelPivot.localPosition = lay.wheel.pivot;
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
            Current = f;
            var lay = L;
            transform.localPosition = f.eye;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            // Driver_Eye on the game's eye, everything scaled about it; an RHD
            // car sees the mirror image.
            model.localScale = new Vector3(f.rhd ? -f.scale : f.scale, f.scale, f.scale);
            model.localPosition = -Vector3.Scale(model.localScale, lay.eye);
            model.localRotation = Quaternion.identity;
            var eyeV = new Vector3(f.rhd ? -lay.eye.x : lay.eye.x, lay.eye.y, lay.eye.z);
            cargoRoot.localScale = Vector3.one * f.scale;
            cargoRoot.localPosition = -eyeV * f.scale;
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
            CollectRenderers();
            CollectShell();
            if (refl != null) refl.Fit(model, screenPart);
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
            var list = new System.Collections.Generic.List<Renderer>();
            if (car != null)
                foreach (var r in car.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.transform.IsChildOf(transform)) continue;
                    var m = r.sharedMaterial;
                    if (m != null && m.shader != null && m.shader.name == "PSX/Shadow") continue;
                    if (CabinClip.Cuts(r)) { CabinClip.Mark(r); continue; }
                    list.Add(r);
                }
            shell = list.ToArray();
            shellWas = new bool[shell.Length];
        }

        /// <summary>The shell as a camera inside the car sees it (and back):
        /// cut at the windscreen, the rest of what hangs off the car hidden.
        /// Public for the shot tool, which renders in edit mode with no camera
        /// events.</summary>
        public void HideShell(bool hide)
        {
            if (shell == null || hide == shellHidden) return;
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
                CabinClip.Open(car.transform, Current.cutZ, c, s);
            }
            else CabinClip.Close();
            shellHidden = hide;
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

        void LateUpdate()
        {
            if (!shown) return;
            Tick(Time.deltaTime, PizzaCargo.Instance);
        }

        /// <summary>The per-frame work: gauges, rain, mirror, the cargo.
        /// Public for the shot tool, which has no frames to wait for.</summary>
        public void Tick(float dt, PizzaCargo cargo)
        {
            if (car != null)
            {
                if (cluster == null) cluster = FindFirstObjectByType<GaugeCluster>();
                if (tank == null) tank = car.GetComponent<FuelTank>();
                if (temp == null) temp = car.GetComponent<EngineTemp>();
            }
            TickWheel(dt);
            if (gauges != null) gauges.Tick(dt, car, cluster, tank, temp, ClusterBulbs.Backlit);
            ActiveNearClip = NearClipFor(Current.scale > 0f ? Current.scale : 0.357f,
                                         CockpitLook.Yaw * CockpitLook.PassengerSign / CockpitLook.MaxYaw);
            TickRain(dt);
            TickMirror();
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
            if (mirrorMat == null || !Application.isPlaying) return;
            if (mirrorCam == null) EnsureMirrorCamera();
            if (mirrorCam != null) mirrorCam.enabled = Time.frameCount % MirrorEvery == 0;
        }

        /// <summary>For the shot tool (edit mode, no frames): the mirror
        /// rendered once from `src`'s world.</summary>
        public void PreviewMirror(Camera src)
        {
            if (driver == null) driver = src;
            if (mirrorCam == null) EnsureMirrorCamera();
            if (mirrorCam == null) return;
            mirrorCam.gameObject.SetActive(true);
            mirrorCam.enabled = false;
            // Edit mode has no camera events: hide what play mode hides from it.
            SetVisible(false);
            HideShell(true);
            mirrorCam.Render();
            SetVisible(true);
        }

        /// <summary>For the shot tool (edit mode, no frames): the windscreen's
        /// reflection drawn now, by <see cref="CockpitReflection.Wanted"/>.</summary>
        public void PreviewReflection()
        {
            if (refl != null) refl.Tick(true);
        }

        /// <summary>A small camera at the mirror looking back, into the
        /// mirror face's texture. Play mode only (a preview tool's scene must
        /// not grow cameras). It renders the world as the driver's camera does
        /// (this cabin hides itself from every other camera).</summary>
        void EnsureMirrorCamera()
        {
            var src = driver != null ? driver : Camera.main;
            if (src == null || L == null || L.mirror == null || mirrorMat == null) return;
            mirrorRT = new RenderTexture(MirrorW, MirrorH, 16) { name = "CockpitMirror", filterMode = FilterMode.Bilinear, antiAliasing = 1 };
            var go = new GameObject("CockpitMirrorCamera");
            if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(transform, false);
            mirrorCam = go.AddComponent<Camera>();
            mirrorCam.CopyFrom(src);
            // AFTER CopyFrom, which also copies the source camera's transform:
            // at the mirror, on the centreline, looking straight back.
            var mc = L.mirror.centre;
            go.transform.localPosition = Vector3.Scale(model.localScale, mc) + model.localPosition;
            go.transform.localRotation = Quaternion.Euler(2f, 180f, 0f);
            go.tag = "Untagged";
            mirrorCam.targetTexture = mirrorRT;
            mirrorCam.aspect = MirrorW / (float)MirrorH;
            mirrorCam.fieldOfView = MirrorVFov;
            mirrorCam.nearClipPlane = 0.3f;
            mirrorCam.farClipPlane = Mathf.Min(src.farClipPlane, MirrorFar);
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
            if (glassMats != null) foreach (var m in glassMats) if (m != null) DestroyImmediate(m);
            if (mirrorMat != null) DestroyImmediate(mirrorMat);
            if (mirrorRT != null) { mirrorRT.Release(); DestroyImmediate(mirrorRT); }
            ActiveNearClip = 0f;
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            bool isDriver = driver != null ? cam == driver : StreetLights.IsDriver(cam);
            SetVisible(isDriver);
            // The mirror looks back from inside the shell too: whatever the
            // shell models inside (seats, a parcel shelf) would fill it.
            if (isDriver || (mirrorCam != null && cam == mirrorCam)) HideShell(true);
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
