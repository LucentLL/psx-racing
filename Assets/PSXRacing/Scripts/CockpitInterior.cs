using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing
{
    /// <summary>
    /// THE GENERIC 3D COCKPIT (2026-10-13). One simple low-poly interior,
    /// shared by every car, for COCKPIT view when no cabin.png has been
    /// supplied (which is always, so far): dash, binnacle hood, A-pillars,
    /// header, door tops, both mirrors, a centre console, and a steering wheel
    /// with the owner's HandsPack hands on it at nine and three. The owner:
    /// "make a generic model in blender for now. simplistic, don't spend too
    /// much time on it." The model is tools/cockpit/generic_cockpit.py's
    /// output, in Resources/Cockpit, loaded at runtime so no scene needs a
    /// rebake.
    ///
    /// WHY IT HAS TO BE OURS. The car shells are solid single-sided bodies with
    /// painted windows: from the driver's seat every face is back-facing and
    /// culled, so the view used to be a hood cam pulled back with nothing round
    /// it. This puts a cabin there - hard-mounted, so it is locked to the
    /// screen and rolls and pitches with the body, the way a real one does.
    ///
    /// THE FIT, per car. The model is authored as a realistic cabin round an
    /// eye at its origin, windscreen base <see cref="ZRef"/> ahead. The eye the
    /// game uses (ChaseCamera.CockpitEye) is only 0.46 m behind the shell's
    /// measured cowl, so the whole cabin is scaled UNIFORMLY about the eye until
    /// its windscreen base lands on that cowl. A uniform scale about the eye
    /// changes no angle - every car gets the same picture - and nothing of the
    /// dash reaches past the windscreen base, which is what keeps the shell's
    /// own bonnet from poking up through it: the bonnet starts where the dash
    /// stops, and the part of it above the dash line is in the windscreen, as
    /// it should be. The cabin (not the wheel) is then stretched sideways by the
    /// body's width, so a wide car's doors and centreline sit further out.
    ///
    /// LIT BY THE WORLD: PSX/Lit, like the scenery, so it darkens with the
    /// hour's ambient, goes black at night (the 2D gauges stay lit), takes the
    /// sky map under a bridge and the street lamps as they pass. The shell
    /// round it shadows the sun, so by day the cabin is in its own shade -
    /// which is what a cabin is. Excluded from the sun map as a caster (it is
    /// inside the shell; it would only cost draw calls).
    ///
    /// SHARP UNDER THE SPEED BLUR: every renderer carries
    /// <see cref="SpeedBlur.CarRenderingLayer"/>, so the blur's mask leaves it
    /// alone the way it leaves the car.
    ///
    /// ONLY THE DRIVER'S CAMERA SEES IT: the reflection probe on the roof
    /// (CarReflections), the mirror and the pizza cam are other cameras, and a
    /// dashboard in the paint's reflection is a bug, so it is switched off for
    /// every camera but the driver's (beginCameraRendering).
    ///
    /// The 2D instruments stay: CockpitView publishes the 3D wheel's projected
    /// centre and radius (<see cref="ProjectWheel"/>) and GaugeCluster lays its
    /// cockpit binnacle in the top opening of the rim, over the dark dial
    /// recess the model has there.
    /// </summary>
    public class CockpitInterior : MonoBehaviour
    {
        // ---- SHARED with tools/cockpit/generic_cockpit.py ----------------
        /// <summary>Authored eye-to-windscreen-base distance, metres.</summary>
        public const float ZRef = 0.78f;
        /// <summary>The wheel hub in the authored eye frame.</summary>
        public static readonly Vector3 Hub = new Vector3(0f, -0.263f, 0.465f);
        /// <summary>Column tilt: the hub frame is Euler(TiltDeg, 0, 0) of the eye's.</summary>
        public const float TiltDeg = 24f;
        /// <summary>Rim radius to the middle of the tube, metres (authored).</summary>
        public const float RimRadius = 0.18f;
        /// <summary>The nearest point of wheel and hands INSIDE the view, as
        /// camera depth in authored metres (the script's geometry, measured
        /// through the cockpit lens and pitch). The forearms come closer but
        /// leave the frame through its bottom edge first.</summary>
        public const float NearestAuthoredM = 0.40f;
        // -------------------------------------------------------------------

        /// <summary>Fit clamps. The cowl distance the eye is set from is 0.46 m
        /// on every shell today (scale 0.59); the clamps only catch a shell
        /// whose measurements are nonsense.</summary>
        public const float MinScale = 0.5f, MaxScale = 1.3f;
        /// <summary>Collider width the cabin was authored round (the FD's).</summary>
        public const float RefWidth = 1.72f;
        public const float MinStretch = 0.9f, MaxStretch = 1.2f;
        /// <summary>Wheel degrees at full lock, each way.</summary>
        public const float LockDeg = 135f;
        /// <summary>GaugeCluster puts its cockpit group 0.74 of the published
        /// radius above the published centre; the dial recess is 0.45 of the
        /// rim's radius above the hub. The difference, published as a drop.</summary>
        public const float ClusterDrop = 0.74f - 0.45f;

        const string CabinPath = "Cockpit/generic_cockpit";
        const string WheelPath = "Cockpit/generic_wheel";
        const string HandsPath = "Cockpit/generic_hands";
        const string SheetPath = "Cockpit/cockpit_sheet";
        const string SkinPath = "Cockpit/hands_skin";

        public struct Fit
        {
            public Vector3 eye;
            /// <summary>Uniform scale about the eye.</summary>
            public float scale;
            /// <summary>Extra sideways scale of the cabin (not the wheel).</summary>
            public float stretch;
            /// <summary>Whether the shell's cowl was usable - ChaseCamera's own
            /// test for the eye. If not, the eye stands on the body box and so
            /// does this (the fit assumes the eye's 0.46 m setback).</summary>
            public bool measured;
        }

        /// <summary>
        /// The fit for a body: the eye where ChaseCamera puts it, the scale
        /// that lands the windscreen base on the measured cowl, the stretch by
        /// width. Static and pure, so the self-test can walk every shell.
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
                scale = Mathf.Clamp(reach / ZRef, MinScale, MaxScale),
                stretch = Mathf.Clamp(boxSize.x / RefWidth, MinStretch, MaxStretch),
            };
        }

        /// <summary>The nearest in-view point of wheel and hands at a fit
        /// scale, metres of camera depth. The near plane has to stay under it.</summary>
        public static float NearestM(float scale) => NearestAuthoredM * scale;

        /// <summary>
        /// Where the hub lands on screen and how big the rim is, through the
        /// cockpit lens: hub in viewport units (0..1 from the bottom left),
        /// radius in viewport HEIGHTS. Independent of the fit - a uniform scale
        /// about the eye moves nothing on screen - so it is a function of the
        /// lens alone and does not twitch with the camera shake.
        /// </summary>
        public static bool ProjectWheel(float vfovDeg, float aspect, float pitchDeg,
                                        out Vector2 hub, out float radius)
        {
            float p = pitchDeg * Mathf.Deg2Rad;
            float yc = Hub.y * Mathf.Cos(p) + Hub.z * Mathf.Sin(p);
            float zc = -Hub.y * Mathf.Sin(p) + Hub.z * Mathf.Cos(p);
            float t = 2f * Mathf.Tan(vfovDeg * 0.5f * Mathf.Deg2Rad);
            hub = new Vector2(0.5f + Hub.x / (zc * t * Mathf.Max(0.1f, aspect)), 0.5f + yc / (zc * t));
            radius = RimRadius / (zc * t);
            return zc > 0.01f;
        }

        // ------------------------------------------------------------------
        //  The instance
        // ------------------------------------------------------------------

        static Material sheetMat, skinMat;

        CarController car;
        Camera driver;
        Transform cabin, wheelPivot, wheel;
        Renderer[] rends;
        BoxCollider box;
        CarBody body;
        bool fitted;
        CarModelDef fitDef;
        Vector3 fitC, fitS;
        float fitLs, fitLift;
        float lastDeg = float.NaN;

        public Fit Current { get; private set; }

        /// <summary>
        /// The interior on this car, built the first time it is asked for.
        /// Null (with a warning) if the model is missing from Resources - the
        /// view then falls back to what it was: a camera in the seat.
        /// </summary>
        public static CockpitInterior Ensure(CarController car, Camera driver)
        {
            if (car == null) return null;
            var have = car.GetComponentInChildren<CockpitInterior>(true);
            if (have != null) { have.driver = driver; return have; }
            var cabinPrefab = PSXTexDecode.LoadPrefab(CabinPath);
            var wheelPrefab = PSXTexDecode.LoadPrefab(WheelPath);
            var handsPrefab = PSXTexDecode.LoadPrefab(HandsPath);
            if (cabinPrefab == null || wheelPrefab == null)
            {
                Debug.LogWarning("[Cockpit] Resources/" + CabinPath + " or " + WheelPath + " missing - no 3D cabin.");
                return null;
            }
            var go = new GameObject("CockpitInterior");
            go.SetActive(false);
            go.transform.SetParent(car.transform, false);
            var ci = go.AddComponent<CockpitInterior>();
            ci.car = car;
            ci.driver = driver;
            ci.Build(cabinPrefab, wheelPrefab, handsPrefab);
            // A preview tool builds this in edit mode, into a scene someone
            // might save: none of it belongs in the file.
            if (!Application.isPlaying)
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    t.gameObject.hideFlags = HideFlags.DontSave;
            return ci;
        }

        void Build(GameObject cabinPrefab, GameObject wheelPrefab, GameObject handsPrefab)
        {
            if (sheetMat == null) sheetMat = MakeMat("CockpitSheet", Resources.Load<Texture2D>(SheetPath));
            if (skinMat == null) skinMat = MakeMat("CockpitHands", Resources.Load<Texture2D>(SkinPath));

            cabin = Instantiate(cabinPrefab, transform, false).transform;
            cabin.name = "Cabin";
            wheelPivot = new GameObject("WheelPivot").transform;
            wheelPivot.SetParent(transform, false);
            wheel = Instantiate(wheelPrefab, wheelPivot, false).transform;
            wheel.name = "SteeringWheel";
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (sheetMat != null) r.sharedMaterial = sheetMat;
            // The hands are authored in the wheel's own frame and ride it.
            if (handsPrefab != null)
            {
                var hands = Instantiate(handsPrefab, wheel, false);
                hands.name = "Hands";
                foreach (var r in hands.GetComponentsInChildren<Renderer>(true))
                    if (skinMat != null) r.sharedMaterial = skinMat;
            }

            rends = GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.renderingLayerMask |= SpeedBlur.CarRenderingLayer;
                SunShadows.Exclude(r.gameObject);
            }
        }

        static Material MakeMat(string name, Texture2D tex)
        {
            var sh = Shader.Find("PSX/Lit");
            if (sh == null) return null;
            var m = new Material(sh) { name = name, hideFlags = HideFlags.HideAndDontSave };
            if (tex != null) m.mainTexture = tex;
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_Affine")) m.SetFloat("_Affine", 0f);
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
            cabin.localPosition = Vector3.zero;
            cabin.localRotation = Quaternion.identity;
            cabin.localScale = new Vector3(f.scale * f.stretch, f.scale, f.scale);
            // The wheel is not stretched: a wheel is round in every car.
            wheelPivot.localPosition = Hub * f.scale;
            wheelPivot.localRotation = Quaternion.Euler(TiltDeg, 0f, 0f);
            wheelPivot.localScale = Vector3.one * f.scale;
        }

        /// <summary>Cabin on or off; the wheel and hands separately - a phone
        /// has its own wheel under the player's thumb, and the binnacle takes
        /// the space between the controls there.</summary>
        public void Show(bool on, bool wheelOn)
        {
            if (gameObject.activeSelf != on) gameObject.SetActive(on);
            if (wheelPivot != null && wheelPivot.gameObject.activeSelf != wheelOn)
                wheelPivot.gameObject.SetActive(wheelOn);
        }

        /// <summary>
        /// Turn the wheel with the front ROAD wheels, so it lags and
        /// self-centres as the car does: full lock is <see cref="LockDeg"/> each
        /// way. Same sign as the 2D wheel it replaces.
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

        void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            SetVisible(true);
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            SetVisible(driver != null ? cam == driver : StreetLights.IsDriver(cam));
        }

        void SetVisible(bool on)
        {
            if (rends == null) return;
            for (int i = 0; i < rends.Length; i++)
                if (rends[i] != null) rends[i].forceRenderingOff = !on;
        }
    }
}
