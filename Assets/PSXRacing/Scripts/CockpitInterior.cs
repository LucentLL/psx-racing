using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing
{
    /// <summary>
    /// THE UNIVERSAL COCKPIT (owner's model, 2026-10-14). One interior shared by
    /// every car in COCKPIT view: "Just as a proof of concept, I made a rough
    /// cockpit to be used universally." It replaces the generic Blender cabin
    /// of 2026-10-13 (tools/cockpit/generic_cockpit.py, kept as history).
    ///
    /// THE MODEL is tools/cockpit/universal_cockpit.py's conversion of the
    /// owner's packed .blend into Resources/Cockpit (loaded at runtime, so no
    /// scene needs a rebake): the static cabin as ONE world-lit mesh, the five
    /// dial faces and the clock as one glowing mesh, the wheel and each needle
    /// in its own pivot frame, the HandsPack hands re-posed onto his rim at
    /// nine and three, and universal_layout.json with his markers and what the
    /// converter measured (see <see cref="Layout"/>).
    ///
    /// THE FIT, per car - the same idea as the generic cabin's. The game's eye
    /// (ChaseCamera.CockpitEye) sits only 0.46 m behind the shell's measured
    /// cowl; this cabin's dash runs 1.29 m ahead of his Driver_Eye. So the
    /// model is put with Driver_Eye ON the game's eye and scaled UNIFORMLY
    /// about it until the front of the dash lands on the cowl (about x0.36).
    /// A uniform scale about the eye changes no angle: every car gets the
    /// owner's picture exactly, and nothing of the dash reaches past the
    /// windscreen base, so the shell's bonnet can never poke up through it.
    /// What the scale DOES change is how near the wheel and hands are, so the
    /// near plane follows the fit (<see cref="ActiveNearClip"/>).
    ///
    /// LIT BY THE WORLD (PSX/Lit), so the cabin is in its own shade by day and
    /// dark at night - except the dial faces and the needles, which are
    /// backlit: half self-lit by day so they read in the shade, fully at night
    /// (<see cref="ClusterBulbs.Backlit"/>, the HUD's own switch).
    ///
    /// THE NEEDLES are the car's: tach and speedo on the printed scales of the
    /// owner's art (0-9 x1000 r/min, 0-180 MPH), fuel E-F, coolant C-H off the
    /// engine temperature model, oil 0-120 psi off the revs (the game has no
    /// oil model; idle pressure rising to the limiter). A dead cluster parks
    /// them and a fluttering tach flutters, as the 2D instruments do. With
    /// these on the dash, GaugeCluster draws only a small gear box in the
    /// corner in this view (<see cref="GaugesShowing"/>).
    ///
    /// THE CARGO rides on his passenger seat: <see cref="CockpitCargoMirror"/>
    /// copies the island's boxes, lids, pizzas and bottles into this cabin
    /// every frame, and the Pizza Cam stands down (<see cref="CargoInView"/>).
    ///
    /// SHARP UNDER THE SPEED BLUR (every renderer carries
    /// SpeedBlur.CarRenderingLayer) and ONLY THE DRIVER'S CAMERA SEES IT (the
    /// roof probe, the mirror and the pizza cam are other cameras).
    ///
    /// AND THE DRIVER'S CAMERA DOES NOT SEE THE SHELL. The cabin is scaled
    /// about the eye to a third of its size, so the car's own body is all
    /// round it, and wherever a shell has a face turned to the eye from inside
    /// (an A-pillar, a modelled seat) it lands in FRONT of the cabin - at full
    /// look-over a grey band of bodywork across the passenger seat. So for the
    /// driver's camera alone, while this cabin shows, the shell is switched
    /// off and restored as that camera finishes; every other camera (the
    /// reflection probe, the mirror, the shadows) sees the car as before. The
    /// blob shadow on the road stays. Looking ahead nothing is lost: the dash
    /// line is above the bonnet on every shell (the fit puts the dash's front
    /// on the cowl).
    /// </summary>
    public class CockpitInterior : MonoBehaviour
    {
        // ------------------------------------------------------------------
        //  The layout, written by tools/cockpit/universal_cockpit.py
        // ------------------------------------------------------------------

        [System.Serializable]
        public class NeedleDef
        {
            public string key;
            public Vector3 pivot;
            public Quaternion rot;
            public float minValue, maxValue;
            /// <summary>The needle's angle at minValue, degrees clockwise from
            /// up as the driver sees it, and the signed sweep to maxValue -
            /// the art's printed marks, measured through the dial's UVs.</summary>
            public float minDeg, sweepDeg;
        }

        [System.Serializable]
        public class WheelDef
        {
            public Vector3 pivot;
            public Quaternion rot;
            public float tiltDeg, rimRadius, rimZ, tube;
        }

        [System.Serializable]
        public class StackDef
        {
            /// <summary>The bottom of the pizza stack at rest: on the owner's
            /// marker, at the height of the plane a 0.41 m box rests on (the
            /// cushion's highest points under it - its side bolsters).</summary>
            public Vector3 centre, normal;
            public float squabZ;
        }

        [System.Serializable]
        public class RoomDef
        {
            /// <summary>Passenger footwell floor, the firewall's face, the door
            /// card's and the console's faces: what a mirrored box stays
            /// inside.</summary>
            public float floorY, firewallZ, doorX, consoleX;
        }

        /// <summary>Everything in the MODEL frame: the owner's root (floor,
        /// cabin centreline), Unity axes, authored metres.</summary>
        [System.Serializable]
        public class Layout
        {
            public Vector3 eye, seatSurface, cargoSpawn, footwell, sodaSpawn;
            /// <summary>The dash's front edge, under the windscreen.</summary>
            public float dashFrontZ;
            public NeedleDef[] needles;
            public WheelDef wheel;
            public StackDef stack;
            public RoomDef room;
            /// <summary>The nearest visible surface from the eye, authored
            /// metres of camera depth, looking ahead (wheel and hands in frame)
            /// and at full look-over.</summary>
            public float nearForward, nearLook;
        }

        const string Dir = "Cockpit/";
        const string LayoutPath = Dir + "universal_layout";
        const string CabinPath = Dir + "universal_cabin";
        const string DialsPath = Dir + "universal_dials";
        const string WheelPath = Dir + "universal_wheel";
        const string HandsPath = Dir + "universal_hands";
        const string NeedlePath = Dir + "universal_needle_";
        const string AtlasPath = Dir + "universal_atlas";
        const string SkinPath = Dir + "hands_skin";

        static Layout layout;
        static bool layoutTried;

        /// <summary>The converter's layout, or null if it is missing.</summary>
        public static Layout L
        {
            get
            {
                if (layout == null && !layoutTried)
                {
                    layoutTried = true;
                    var ta = Resources.Load<TextAsset>(LayoutPath);
                    if (ta != null) layout = JsonUtility.FromJson<Layout>(ta.text);
                    if (layout == null || layout.needles == null || layout.wheel == null || layout.stack == null)
                    {
                        Debug.LogWarning("[Cockpit] Resources/" + LayoutPath + " missing or incomplete");
                        layout = null;
                    }
                }
                return layout;
            }
        }

        /// <summary>Eye to the dash's front edge, authored metres - what the
        /// fit lands on the shell's cowl.</summary>
        public static float ZRef => L != null ? L.dashFrontZ - L.eye.z : 1.29f;

        /// <summary>Fit clamps. The cowl distance the eye is set from is 0.46 m
        /// on almost every shell (x0.357); the clamps only catch a shell whose
        /// measurements are nonsense.</summary>
        public const float MinScale = 0.15f, MaxScale = 1.0f;
        /// <summary>Wheel degrees at full lock, each way.</summary>
        public const float LockDeg = 135f;
        /// <summary>Margin of the near plane under the nearest visible surface
        /// (the impact shake moves the eye).</summary>
        public const float NearMargin = 1.5f;
        /// <summary>Dial glow by day, as PSX/Lit's _Emission (night is 1).</summary>
        public const float DayDialGlow = 0.5f;
        /// <summary>Oil pressure, psi: at idle, and at the rev limiter.</summary>
        public const float OilIdlePsi = 25f, OilPeakPsi = 72f;

        public struct Fit
        {
            public Vector3 eye;
            /// <summary>Uniform scale about the eye.</summary>
            public float scale;
            /// <summary>Whether the shell's cowl was usable - ChaseCamera's own
            /// test for the eye. If not, the eye stands on the body box and so
            /// does this (the fit assumes the eye's 0.46 m setback).</summary>
            public bool measured;
        }

        /// <summary>
        /// The fit for a body: the eye where ChaseCamera puts it and the scale
        /// that lands the dash's front on the measured cowl. Static and pure,
        /// so the self-test can walk every shell.
        /// </summary>
        public static Fit FitFor(Vector3 boxCenter, Vector3 boxSize, CarModelDef def,
                                 float lengthScale = 1f, float lift = 0f)
        {
            Vector3 eye = ChaseCamera.MountOffset(ChaseCamera.View.Cockpit, boxCenter, boxSize, def, lengthScale, lift);
            // The same test ChaseCamera.CockpitEye applies before it trusts the
            // cowl (the full-size sedans' roof signs read as a cowl at 1.44 m).
            float cowlZ = def != null ? def.cowlZ * lengthScale : 0f;
            float cowlY = def != null ? def.cowlY + lift : 0f;
            float roofY = def != null ? def.roofY + lift : 0f;
            bool measured = def != null && cowlY > 0.4f && roofY > cowlY + 0.25f
                            && cowlZ > boxCenter.y - boxSize.y * 0.5f;
            float reach = measured ? Mathf.Max(0.2f, cowlZ - eye.z) : ChaseCamera.EyeSetback;
            return new Fit
            {
                eye = eye,
                measured = measured,
                scale = Mathf.Clamp(reach / Mathf.Max(0.1f, ZRef), MinScale, MaxScale),
            };
        }

        /// <summary>The nearest in-view point of wheel and hands at a fit
        /// scale, metres of camera depth.</summary>
        public static float NearestM(float scale) => (L != null ? L.nearForward : 0.28f) * scale;

        /// <summary>The near plane the cockpit wants at a fit scale: under the
        /// nearest visible surface ahead or at full look-over, by
        /// <see cref="NearMargin"/>.</summary>
        public static float NearClipFor(float scale)
        {
            float nearest = L != null ? Mathf.Min(L.nearForward, L.nearLook) : 0.28f;
            return Mathf.Min(ChaseCamera.CockpitNearClip, nearest * scale / NearMargin);
        }

        /// <summary>The near plane of the interior that is showing, or 0 with
        /// none (ChaseCamera.ViewNearClip reads it).</summary>
        public static float ActiveNearClip { get; private set; }
        /// <summary>The 3D needles are on screen: the 2D binnacle stands down.</summary>
        public static bool GaugesShowing { get; private set; }
        /// <summary>The cabin with the cargo on its seat is on screen: the
        /// Pizza Cam stands down (panel hidden, camera off).</summary>
        public static bool CargoInView { get; private set; }

        // ------------------------------------------------------------------
        //  The instance
        // ------------------------------------------------------------------

        static Material cabinMat, dialMat, skinMat;
        static float dialGlow = -1f;

        CarController car;
        Camera driver;
        Transform model, wheelPivot, wheel, mirrorRoot;
        Transform[] needles;
        float[] needleDeg;
        Renderer[] rends;
        BoxCollider box;
        CarBody body;
        GaugeCluster cluster;
        FuelTank tank;
        EngineTemp temp;
        CockpitCargoMirror mirror;
        Renderer[] shell;
        bool[] shellWas;
        bool shellHidden;
        bool fitted, shown;
        CarModelDef fitDef;
        Vector3 fitC, fitS;
        float fitLs, fitLift;
        float lastDeg = float.NaN;
        float flutter;

        public Fit Current { get; private set; }
        public Transform Model => model;
        public CockpitCargoMirror Mirror => mirror;
        /// <summary>The needle pivots' children, in Layout.needles order.</summary>
        public Transform[] Needles => needles;

        /// <summary>
        /// The interior on this car, built the first time it is asked for.
        /// Null (with a warning) if the model is missing from Resources - the
        /// view then falls back to what it was: a camera in the seat with the
        /// 2D binnacle.
        /// </summary>
        public static CockpitInterior Ensure(CarController car, Camera driver)
        {
            if (car == null) return null;
            var have = car.GetComponentInChildren<CockpitInterior>(true);
            if (have != null) { have.driver = driver; return have; }
            var cabinPrefab = PSXTexDecode.LoadPrefab(CabinPath);
            var wheelPrefab = PSXTexDecode.LoadPrefab(WheelPath);
            if (cabinPrefab == null || wheelPrefab == null || L == null)
            {
                Debug.LogWarning("[Cockpit] Resources/" + CabinPath + ", " + WheelPath + " or the layout missing - no 3D cabin.");
                return null;
            }
            var go = new GameObject("CockpitInterior");
            go.SetActive(false);
            go.transform.SetParent(car.transform, false);
            go.layer = car.gameObject.layer;
            var ci = go.AddComponent<CockpitInterior>();
            ci.car = car;
            ci.driver = driver;
            ci.Build(cabinPrefab, wheelPrefab);
            // A preview tool builds this in edit mode, into a scene someone
            // might save: none of it belongs in the file.
            if (!Application.isPlaying)
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    t.gameObject.hideFlags = HideFlags.DontSave;
            return ci;
        }

        void Build(GameObject cabinPrefab, GameObject wheelPrefab)
        {
            var atlas = Resources.Load<Texture2D>(AtlasPath);
            if (cabinMat == null) cabinMat = MakeMat("CockpitCabin", atlas, 0f);
            if (dialMat == null) { dialMat = MakeMat("CockpitDials", atlas, DayDialGlow); dialGlow = -1f; }
            if (skinMat == null) skinMat = MakeMat("CockpitHands", Resources.Load<Texture2D>(SkinPath), 0f);
            var lay = L;

            model = new GameObject("Model").transform;
            model.SetParent(transform, false);
            Place(cabinPrefab, model, "Cabin", cabinMat);
            Place(PSXTexDecode.LoadPrefab(DialsPath), model, "Dials", dialMat);

            int n = lay.needles.Length;
            needles = new Transform[n];
            needleDeg = new float[n];
            for (int i = 0; i < n; i++)
            {
                var d = lay.needles[i];
                var pivot = new GameObject("Needle_" + d.key).transform;
                pivot.SetParent(model, false);
                pivot.localPosition = d.pivot;
                pivot.localRotation = d.rot;
                needles[i] = Place(PSXTexDecode.LoadPrefab(NeedlePath + d.key), pivot, "Pointer", dialMat);
                needleDeg[i] = float.NaN;
            }

            wheelPivot = new GameObject("WheelPivot").transform;
            wheelPivot.SetParent(model, false);
            wheelPivot.localPosition = lay.wheel.pivot;
            wheelPivot.localRotation = lay.wheel.rot;
            wheel = Place(wheelPrefab, wheelPivot, "SteeringWheel", cabinMat);
            // The hands are posed in the wheel's own frame and ride it.
            Place(PSXTexDecode.LoadPrefab(HandsPath), wheel, "Hands", skinMat);

            mirrorRoot = new GameObject("CargoMirror").transform;
            mirrorRoot.SetParent(model, false);
            mirror = new CockpitCargoMirror(mirrorRoot, lay);
            CollectRenderers();
        }

        Transform Place(GameObject prefab, Transform parent, string name, Material mat)
        {
            if (prefab == null) { Debug.LogWarning("[Cockpit] missing part " + name); return null; }
            var t = Instantiate(prefab, parent, false).transform;
            t.name = name;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                if (mat != null) r.sharedMaterial = mat;
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = gameObject.layer;
            return t;
        }

        /// <summary>Every renderer this cabin draws, cargo copies included,
        /// set up the cabin's way. Again whenever the mirror adds copies.</summary>
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
        }

        static Material MakeMat(string name, Texture2D tex, float emission)
        {
            var sh = Shader.Find("PSX/Lit");
            if (sh == null) return null;
            var m = new Material(sh) { name = name, hideFlags = HideFlags.HideAndDontSave };
            if (tex != null) m.mainTexture = tex;
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_Affine")) m.SetFloat("_Affine", 0f);
            if (m.HasProperty("_Emission")) m.SetFloat("_Emission", emission);
            PSXTexDecode.Stamp(m);
            return m;
        }

        /// <summary>Re-measure only when the body changed (a shell swapped
        /// mid-drive by the bench or the handoff).</summary>
        public void Refit()
        {
            if (car == null) return;
            if (box == null) box = car.GetComponent<BoxCollider>();
            if (body == null) body = car.GetComponent<CarBody>();
            // ChaseCamera's own fallbacks for a car with no box.
            Vector3 c = box != null ? box.center : new Vector3(0f, 0.72f, 0.05f);
            Vector3 s = box != null ? box.size : new Vector3(1.72f, 1.0f, 4.1f);
            var def = body != null ? body.Def : null;
            float ls = body != null ? body.LengthScale : 1f;
            float lift = body != null ? body.BodyLift : 0f;
            if (fitted && def == fitDef && c == fitC && s == fitS && ls == fitLs && lift == fitLift) return;
            fitted = true; fitDef = def; fitC = c; fitS = s; fitLs = ls; fitLift = lift;
            Apply(FitFor(c, s, def, ls, lift));
        }

        void Apply(Fit f)
        {
            Current = f;
            transform.localPosition = f.eye;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            // Driver_Eye on the game's eye, everything scaled about it.
            model.localScale = Vector3.one * f.scale;
            model.localPosition = -L.eye * f.scale;
            model.localRotation = Quaternion.identity;
            ActiveNearClip = NearClipFor(f.scale);
            CollectShell();
        }

        /// <summary>The car's own renderers, less this cabin and the blob
        /// shadow on the road. Again on every refit (a shell swap).</summary>
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
                    list.Add(r);
                }
            shell = list.ToArray();
            shellWas = new bool[shell.Length];
        }

        /// <summary>Shell off for the driver's camera (and back). Public for
        /// the shot tool, which renders in edit mode with no camera events.</summary>
        public void HideShell(bool hide)
        {
            if (shell == null || hide == shellHidden) return;
            for (int i = 0; i < shell.Length; i++)
            {
                if (shell[i] == null) continue;
                if (hide) { shellWas[i] = shell[i].forceRenderingOff; shell[i].forceRenderingOff = true; }
                else shell[i].forceRenderingOff = shellWas[i];
            }
            shellHidden = hide;
        }

        /// <summary>Cabin on or off; the wheel and hands separately - a phone
        /// has its own wheel under the player's thumb.</summary>
        public void Show(bool on, bool wheelOn)
        {
            if (gameObject.activeSelf != on) gameObject.SetActive(on);
            if (wheelPivot != null && wheelPivot.gameObject.activeSelf != wheelOn)
                wheelPivot.gameObject.SetActive(wheelOn);
            shown = on;
            GaugesShowing = on && needles != null && needles.Length > 0;
            CargoInView = on;
            if (on) ActiveNearClip = NearClipFor(Current.scale > 0f ? Current.scale : 0.357f);
        }

        /// <summary>
        /// Turn the wheel with the front ROAD wheels, so it lags and
        /// self-centres as the car does: full lock is <see cref="LockDeg"/> each
        /// way. Positive = anticlockwise as the driver sees it.
        /// </summary>
        public void SetSteer(float roadWheelDeg, float fullLockDeg)
        {
            if (wheel == null) return;
            float deg = -roadWheelDeg / Mathf.Max(5f, fullLockDeg) * LockDeg;
            deg = Mathf.Clamp(deg, -LockDeg * 1.4f, LockDeg * 1.4f);
            if (!float.IsNaN(lastDeg) && Mathf.Abs(deg - lastDeg) < 0.1f) return;
            lastDeg = deg;
            wheel.localRotation = Quaternion.Euler(0f, 0f, deg);
        }

        void LateUpdate()
        {
            if (!shown) return;
            Tick(Time.deltaTime, PizzaCargo.Instance);
        }

        /// <summary>The per-frame work: needles, dial glow, the cargo. Public
        /// for the shot tool, which has no frames to wait for.</summary>
        public void Tick(float dt, PizzaCargo cargo)
        {
            UpdateGauges(dt);
            // Only this car's load (a cargo built without a car is a harness's).
            if (cargo != null && cargo.Car != null && cargo.Car != car) cargo = null;
            if (mirror != null && mirror.Sync(cargo)) CollectRenderers();
        }

        void UpdateGauges(float dt)
        {
            float glow = ClusterBulbs.Backlit ? 1f : DayDialGlow;
            if (dialMat != null && glow != dialGlow) { dialGlow = glow; dialMat.SetFloat("_Emission", glow); }
            if (car == null || needles == null) return;
            if (cluster == null) cluster = FindFirstObjectByType<GaugeCluster>();
            if (tank == null) tank = car.GetComponent<FuelTank>();
            if (temp == null) temp = car.GetComponent<EngineTemp>();
            bool dead = cluster != null && cluster.hideGauges;
            float rpm = car.currentRPM;
            if (cluster != null && cluster.rpmFlutter)
            {
                // A broken tacho drifts, the same Perlin the 2D one uses.
                flutter += dt;
                rpm *= 0.55f + Mathf.PerlinNoise(flutter * 2.3f, 0f) * 0.9f;
            }
            var lay = L;
            for (int i = 0; i < needles.Length; i++)
            {
                if (needles[i] == null) continue;
                var d = lay.needles[i];
                float v, tau;
                switch (d.key)
                {
                    case "tach": v = rpm; tau = 0.05f; break;
                    case "speedo": v = Mathf.Abs(car.speedKmh) / 1.609344f; tau = 0.08f; break;
                    case "fuel": v = tank != null ? tank.percent * 0.01f : 1f; tau = 0.8f; break;
                    case "coolant": v = temp != null ? temp.Gauge : 0.5f; tau = 0.8f; break;
                    case "oil":
                        v = car.currentRPM > 150f
                            ? Mathf.Lerp(OilIdlePsi, OilPeakPsi, Mathf.Clamp01(car.currentRPM / Mathf.Max(1000f, car.revLimitRPM)))
                            : 0f;
                        tau = 0.15f; break;
                    default: v = d.minValue; tau = 0.1f; break;
                }
                if (dead) v = d.minValue;
                float want = d.minDeg + d.sweepDeg * Mathf.Clamp01(Mathf.InverseLerp(d.minValue, d.maxValue, v));
                float deg = float.IsNaN(needleDeg[i]) || dt <= 0f
                    ? want
                    : Mathf.Lerp(needleDeg[i], want, 1f - Mathf.Exp(-dt / tau));
                if (!float.IsNaN(needleDeg[i]) && Mathf.Abs(deg - needleDeg[i]) < 0.05f) continue;
                needleDeg[i] = deg;
                // Clockwise as the driver sees it is a NEGATIVE turn about z.
                needles[i].localRotation = Quaternion.Euler(0f, 0f, -deg);
            }
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
            CargoInView = false;
        }

        void OnDestroy()
        {
            if (mirror != null) mirror.Clear();
            ActiveNearClip = 0f;
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            bool isDriver = driver != null ? cam == driver : StreetLights.IsDriver(cam);
            SetVisible(isDriver);
            if (isDriver) HideShell(true);
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
