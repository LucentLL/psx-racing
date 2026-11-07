using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// A turntable that renders the car the player actually owns into a menu
    /// panel — body, wheels and the livery it races in, lit and dithered the
    /// same way the race scene is.
    ///
    /// The garage could name a car and price it but never show it, which after
    /// the vehicle pack landed is a strange thing for a garage to be: the player
    /// picks between 317 cars wearing sixteen bodies and had no way to see which
    /// one they bought short of starting a race.
    ///
    /// The rig lives 2 km under the floor rather than at the origin, and the
    /// camera that films it has a 60 m far plane. The menu scene is otherwise
    /// empty, so this is belt and braces — but it costs nothing and it means the
    /// viewer can never accidentally film something else the scene grows later.
    ///
    /// It is a component on the LifeHome object, NOT on the panel it draws into:
    /// LifeHomeScreen.Rebuild destroys and recreates the whole body on every
    /// button press, and a render texture reallocated per keypress is how a menu
    /// starts stuttering.
    /// </summary>
    public class CarViewer : MonoBehaviour
    {
        /// <summary>Resolution of the viewport. Deliberately tiny and point
        /// filtered: this is a PS1 garage, and a crisp 1024-wide render of a
        /// 700-triangle car next to a dithered 320x240 race would look like an
        /// asset from a different game.</summary>
        public int width = 320, height = 200;

        RenderTexture rt;
        Camera cam;
        Transform rig;      // yawed by the player
        Transform bodyRoot;
        MeshFilter bodyFilter;
        MeshRenderer bodyRenderer;
        readonly MeshFilter[] wheelFilters = new MeshFilter[4];
        readonly MeshRenderer[] wheelRenderers = new MeshRenderer[4];
        Light sun;
        PSXGlobals globals;
        Transform shadow;


        // ------------------------------------------------------------------
        //  Shared blob-shadow assets, generated rather than loaded: the baked
        //  BlobShadow texture lives in Generated/, which is not a Resources
        //  folder, and one 64x64 radial falloff is cheaper to make than to
        //  route through the asset pipeline.
        // ------------------------------------------------------------------
        static Mesh shadowQuad;
        static Mesh ShadowQuad
        {
            get
            {
                if (shadowQuad != null) return shadowQuad;
                shadowQuad = new Mesh { name = "ViewerShadowQuad" };
                shadowQuad.vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f),   new Vector3(0.5f, -0.5f, 0f),
                };
                shadowQuad.uv = new[]
                {
                    new Vector2(0f, 0f), new Vector2(0f, 1f),
                    new Vector2(1f, 1f), new Vector2(1f, 0f),
                };
                shadowQuad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                shadowQuad.RecalculateNormals();
                shadowQuad.RecalculateBounds();
                return shadowQuad;
            }
        }

        static Material shadowMat;
        static Material ShadowMat
        {
            get
            {
                if (shadowMat != null) return shadowMat;
                var shader = Shader.Find("PSX/Shadow");
                if (shader == null) return null;
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
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
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        a = a * a * (3f - 2f * a);                 // smoothstep
                        px[y * n + x] = new Color32(0, 0, 0, (byte)(a * 255f));
                    }
                tex.SetPixels32(px);
                tex.Apply();
                shadowMat = new Material(shader) { name = "ViewerShadow", mainTexture = tex };
                if (shadowMat.HasProperty("_Strength")) shadowMat.SetFloat("_Strength", 0.6f);
                return shadowMat;
            }
        }

        CarModelLibrary.SpecGeometry shownGeo;

        string shownKey;
        int shownSkin = -1;
        /// <summary>Front three-quarter to start: the angle every showroom
        /// photograph is taken from, because it shows length, width and face at
        /// once. (Zero is square-on from BEHIND — the camera sits on -Z and the
        /// car's nose points +Z.)</summary>
        float yaw = 215f;
        float spin = 20f;   // degrees/second of idle rotation
        float distance = 7.4f;
        float carLength = 4.1f, carWidth = 1.72f, carHeight = 1.3f;

        /// <summary>The turntable, or a pinned view for the inspection: the car
        /// from straight above with the nose right (the x-ray's frame), or
        /// side-on for its wheels and brakes.</summary>
        public enum ViewPose { Showroom, Plan, Side }
        ViewPose pose;
        float poseAspect = 1.6f, planHigh = 2f;

        /// <param name="aspect">The panel's width over height: the picture is
        /// stretched to fill it, so the camera draws for that shape.</param>
        /// <param name="planMetresHigh">Plan only: metres of ground the panel's
        /// height covers.</param>
        public void SetPose(ViewPose p, float aspect = 1.6f, float planMetresHigh = 2f)
        {
            if (p == ViewPose.Showroom && pose == ViewPose.Showroom) return;
            EnsureRig();
            pose = p;
            poseAspect = aspect;
            planHigh = planMetresHigh;
            if (p == ViewPose.Showroom)
            {
                cam.orthographic = false;
                cam.ResetAspect();
                yaw = 215f;
                spin = 20f;
                SetWheelsOff(false);
            }
            else
            {
                cam.aspect = aspect;
                spin = 0f;
                yaw = 90f;   // the nose points +X, screen right in both views
                rig.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
            PlaceCamera();
        }

        string nextWheelKey, shownWheelKey = "";
        bool shownOwnBrakes;

        /// <summary>Shells with rotors and calipers painted on their own
        /// wheelhouse walls: taking the wheels off already shows them.</summary>
        static readonly System.Collections.Generic.HashSet<string> PaintedBrakes =
            new System.Collections.Generic.HashSet<string>
            {
                "civic_eg", "crown_victoria", "fullsize_police", "fullsize_taxi", "fullsize_trooper",
                "skyline_r32", "gtr_89",
            };

        readonly MeshRenderer[] brakeRenderers = new MeshRenderer[4];
        readonly float[] brakeRadius = new float[4];
        bool wheelsOff;

        /// <summary>The wheels off, showing a disc and caliper at each hub.</summary>
        public void SetWheelsOff(bool off)
        {
            wheelsOff = off;
            for (int i = 0; i < 4; i++)
            {
                if (wheelRenderers[i] != null) wheelRenderers[i].enabled = !off;
                bool drawn = off && !shownOwnBrakes;
                if (drawn && brakeRenderers[i] == null && wheelFilters[i] != null) BuildBrake(i);
                if (brakeRenderers[i] == null) continue;
                brakeRenderers[i].enabled = drawn;
                if (drawn)
                {
                    var w = wheelFilters[i].transform;
                    var b = brakeRenderers[i].transform;
                    b.localPosition = w.localPosition;
                    b.localRotation = Quaternion.identity;
                    b.localScale = Vector3.one * brakeRadius[i] * 0.72f;
                }
            }
        }

        void BuildBrake(int i)
        {
            var go = new GameObject("Brake" + i);
            go.transform.SetParent(wheelFilters[i].transform.parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = BrakeMesh();
            brakeRenderers[i] = go.AddComponent<MeshRenderer>();
            brakeRenderers[i].sharedMaterial = BrakeMaterial();
        }

        static Mesh brakeMesh;
        Material brakeMaterial;

        /// <summary>Two texels: the disc's steel and the caliper's paint.</summary>
        Material BrakeMaterial()
        {
            if (brakeMaterial != null) return brakeMaterial;
            brakeMaterial = new Material(wheelRenderers[0].sharedMaterial);
            var tex = new Texture2D(2, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels32(new[] { new Color32(150, 152, 156, 255), new Color32(120, 30, 26, 255) });
            tex.Apply(false);
            brakeMaterial.mainTexture = tex;
            PSXTexDecode.Stamp(brakeMaterial);
            return brakeMaterial;
        }

        /// <summary>A unit-radius disc about X with a block for the caliper, made
        /// here because the car models have no brakes of their own.</summary>
        static Mesh BrakeMesh()
        {
            if (brakeMesh != null) return brakeMesh;
            const int Sides = 20;
            const float HalfW = 0.09f;
            var steel = new Vector2(0.25f, 0.5f);
            var paint = new Vector2(0.75f, 0.5f);
            var v = new System.Collections.Generic.List<Vector3>();
            var uv = new System.Collections.Generic.List<Vector2>();
            var tris = new System.Collections.Generic.List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 t)
            {
                // Both faces, each with its own vertices: shared ones would have
                // their normals cancel to zero, which normalizes to NaN in the shader.
                int n = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                for (int k = 0; k < 8; k++) uv.Add(t);
                tris.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3, n + 4, n + 6, n + 5, n + 4, n + 7, n + 6 });
            }
            for (int i = 0; i < Sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / Sides, a1 = (i + 1) * Mathf.PI * 2f / Sides;
                var p0 = new Vector3(0f, Mathf.Sin(a0), Mathf.Cos(a0));
                var p1 = new Vector3(0f, Mathf.Sin(a1), Mathf.Cos(a1));
                var x = new Vector3(HalfW, 0f, 0f);
                Quad(x, x + p0, x + p1, x, steel);
                Quad(-x, -x + p0, -x + p1, -x, steel);
                Quad(-x + p0, x + p0, x + p1, -x + p1, steel);
            }
            // The caliper: a block over the disc's trailing upper edge.
            Vector3 lo = new Vector3(-0.2f, 0.45f, -0.75f), hi = new Vector3(0.2f, 1.08f, -0.2f);
            Vector3 P(float xx, float yy, float zz) => new Vector3(xx, yy, zz);
            Quad(P(hi.x, lo.y, lo.z), P(hi.x, hi.y, lo.z), P(hi.x, hi.y, hi.z), P(hi.x, lo.y, hi.z), paint);
            Quad(P(lo.x, lo.y, lo.z), P(lo.x, hi.y, lo.z), P(lo.x, hi.y, hi.z), P(lo.x, lo.y, hi.z), paint);
            Quad(P(lo.x, hi.y, lo.z), P(hi.x, hi.y, lo.z), P(hi.x, hi.y, hi.z), P(lo.x, hi.y, hi.z), paint);
            Quad(P(lo.x, lo.y, lo.z), P(hi.x, lo.y, lo.z), P(hi.x, hi.y, lo.z), P(lo.x, hi.y, lo.z), paint);
            Quad(P(lo.x, lo.y, hi.z), P(hi.x, lo.y, hi.z), P(hi.x, hi.y, hi.z), P(lo.x, hi.y, hi.z), paint);
            brakeMesh = new Mesh { name = "ViewerBrake" };
            brakeMesh.SetVertices(v);
            brakeMesh.SetUVs(0, uv);
            brakeMesh.SetTriangles(tris, 0);
            brakeMesh.RecalculateNormals();
            brakeMesh.RecalculateBounds();
            return brakeMesh;
        }

        public Texture Texture => rt;
        /// <summary>The shell on display, for the caption under the panel.</summary>
        public CarModelDef Shown { get; private set; }

        /// <summary>
        /// Built on demand rather than in Awake, because the menu preview tool
        /// constructs this screen outside play mode — where AddComponent does
        /// NOT call Awake — and a rig that only exists at runtime is a rig the
        /// layout tool can never photograph.
        /// </summary>
        void EnsureRig()
        {
            if (rig == null) BuildRig();
        }

        void OnDestroy()
        {
            if (cam != null) cam.targetTexture = null;
            if (rt == null) return;
            rt.Release();
            if (Application.isPlaying) Destroy(rt); else DestroyImmediate(rt);
        }

        void BuildRig()
        {
            rt = new RenderTexture(width, height, 16, RenderTextureFormat.Default)
            {
                name = "CarViewerRT",
                filterMode = FilterMode.Point,
                antiAliasing = 1,
            };
            rt.Create();

            var root = new GameObject("CarViewerRig");
            root.transform.SetParent(transform, false);
            root.transform.position = new Vector3(0f, -2000f, 0f);

            rig = new GameObject("Turntable").transform;
            rig.SetParent(root.transform, false);

            bodyRoot = new GameObject("Body").transform;
            bodyRoot.SetParent(rig, false);
            bodyFilter = bodyRoot.gameObject.AddComponent<MeshFilter>();
            bodyRenderer = bodyRoot.gameObject.AddComponent<MeshRenderer>();

            for (int i = 0; i < 4; i++)
            {
                var w = new GameObject("Wheel" + i);
                w.transform.SetParent(rig, false);
                wheelFilters[i] = w.AddComponent<MeshFilter>();
                wheelRenderers[i] = w.AddComponent<MeshRenderer>();
            }

            // The same blob the cars race with. Without it the car reads as
            // floating in a void — there is no floor in here to catch a real
            // shadow, and a soft ellipse tight under the wheels is what a PS1
            // car-select screen used instead of one.
            var shadowGO = new GameObject("Shadow");
            shadowGO.transform.SetParent(rig, false);
            shadowGO.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            shadowGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shadowGO.AddComponent<MeshFilter>().sharedMesh = ShadowQuad;
            shadow = shadowGO.transform;
            shadowGO.AddComponent<MeshRenderer>().sharedMaterial = ShadowMat;

            var camGO = new GameObject("ViewerCamera");
            camGO.transform.SetParent(root.transform, false);
            cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            // Not black: a black car on black is a silhouette. This is the
            // menu's own panel colour, a shade darker.
            cam.backgroundColor = new Color(0.14f, 0.13f, 0.21f, 1f);
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 60f;
            cam.targetTexture = rt;
            cam.depth = -50f;
            // Driven by hand from LateUpdate rather than left to Unity's own
            // pass. A target-texture camera normally renders itself, but that
            // is one more thing that has to be true for the panel to show a car
            // instead of an uninitialised texture — and when it is not, the
            // failure is a black rectangle that looks exactly like a menu with
            // nothing in it. Drawing it explicitly makes the picture the same
            // in the editor preview, in play mode and in a WebGL build.
            cam.enabled = false;

            // PSX/Lit shades from global uniforms, not from scene lights, and
            // the menu scene has no PSXGlobals of its own — without this the
            // car renders with whatever the last race left in the shader
            // globals, which after a domain reload is black.
            var sunGO = new GameObject("ViewerSun");
            sunGO.transform.SetParent(root.transform, false);
            sunGO.transform.rotation = Quaternion.Euler(26f, 38f, 0f);
            sun = sunGO.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.94f, 0.86f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.None;
            globals = sunGO.AddComponent<PSXGlobals>();
            globals.sun = sun;
            globals.ambient = new Color(0.34f, 0.33f, 0.42f);
            // Fog pushed past the far plane: the showroom is not foggy, and the
            // globals are shared, so this is also what a menu leaves behind for
            // the next scene until the race applies its own hour.
            globals.fogColor = new Color(0.07f, 0.06f, 0.12f);
            globals.fogNear = 400f;
            globals.fogFar = 800f;

            root.SetActive(false);
        }

        /// <summary>Show or hide the whole rig. Off by default so a menu that is
        /// not looking at a car pays nothing for one.</summary>
        public void SetVisible(bool on)
        {
            if (rig != null) rig.parent.gameObject.SetActive(on);
        }

        public bool Visible => rig != null && rig.parent.gameObject.activeSelf;

        /// <summary>
        /// Draw one frame by hand. A camera with a target texture renders itself
        /// every frame at runtime, but nothing renders outside play mode — so
        /// without this the preview tool photographs an uninitialised texture,
        /// which is either black or last frame's garbage depending on the driver.
        /// </summary>
        public void RenderNow()
        {
            EnsureRig();
            if (cam == null || !Visible) return;
            // The turntable is spun from Update, which does not run outside play
            // mode either — so a preview would photograph the car at yaw zero,
            // which is square-on from behind rather than the three-quarter the
            // viewer actually opens at.
            rig.localRotation = Quaternion.Euler(0f, yaw, 0f);
            // Same story for PSXGlobals, which pushes the shader uniforms from
            // its own Update.
            if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);

            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req))
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
            else
                cam.Render();
        }

        /// <summary>Put a car on the turntable, in the livery it would turn up
        /// to a race in.</summary>
        public void Show(CarSpec spec)
        {
            if (spec == null) return;
            var def = CarModelLibrary.LoadFor(spec);
            if (def == null) return;
            // Paint's answer, which is CarBody's answer, so the car on the
            // turntable is the car on the grid rather than a different colour
            // of it.
            Show(def, Paint.FactorySkin(spec, def), CarModelLibrary.SpecGeometry.Of(spec));
        }

        /// <summary>
        /// The same, for a car the player OWNS — which is the only kind that
        /// can have been resprayed. Separate rather than a nullable argument
        /// because most callers are showing a car in a classified ad or a
        /// showroom, and those have no owner and no paint history.
        /// </summary>
        /// <param name="skinOverride">Force a particular livery, for the body
        /// shop's own preview: the player is choosing a colour and has to see
        /// the one they are hovering, not the one on the car.</param>
        public void ShowOwned(OwnedCar car, CarSpec spec, int skinOverride = -1)
        {
            var def = Paint.DefFor(spec);
            if (def == null) return;
            nextWheelKey = car != null ? car.wheelKey : "";
            Show(def, skinOverride >= 0 ? skinOverride : Paint.SkinFor(car, spec, def), CarModelLibrary.SpecGeometry.Of(spec));
        }

        /// <param name="widthMm">The real car's width (CarSpec.widthMm), 0 for
        /// the model's reference car: the turntable shows the car as wide as it
        /// races (CarModelLibrary.WidthScale).</param>
        public void Show(CarModelDef def, int skin, int widthMm = 0) =>
            Show(def, skin, CarModelLibrary.SpecGeometry.WidthOnly(widthMm));

        /// <summary>The turntable shows the car as it races: the same
        /// CarModelLibrary.Fit as CarBody (stretch, spec track, tyres, camber).</summary>
        public void Show(CarModelDef def, int skin, CarModelLibrary.SpecGeometry geo)
        {
            if (def == null) return;
            EnsureRig();
            string wheelKey = nextWheelKey ?? "";
            nextWheelKey = null;
            if (def.key == shownKey && skin == shownSkin && geo.Equals(shownGeo) && wheelKey == shownWheelKey) return;
            shownWheelKey = wheelKey;
            shownGeo = geo;
            var fit = CarModelLibrary.Fit(def, geo);
            float sx = fit.sx;
            shownKey = def.key;
            shownSkin = skin;
            Shown = def;

            var mat = def.SkinCount > 0
                ? def.skinMaterials[Mathf.Clamp(skin, 0, def.SkinCount - 1)] : null;
            var wheelMat = def.wheelMaterial != null ? def.wheelMaterial : mat;
            var donor = string.IsNullOrEmpty(wheelKey) || wheelKey == def.key ? null : CarModelLibrary.Load(wheelKey);
            if (donor != null && donor.wheelMesh == null) donor = null;
            var donorMat = CarModelLibrary.WheelMaterial(donor);
            var donorRatio = CarModelLibrary.WheelRatio(def, donor);
            if (donorMat != null) PSXTexDecode.Stamp(donorMat);
            shownOwnBrakes = PaintedBrakes.Contains(def.key);

            // The turntable spins about ITS origin, so the car is placed with
            // its own middle there — otherwise a long car swings, which is what
            // the axle-midpoint origin the rest of the game uses would do here.
            // The two offsets inside that are the same ones CarBody applies:
            // the body sits back from the axles by the model's own asymmetry,
            // and the wheels sit symmetrically about them.
            float centre = fit.P(def.colliderCenter).z;

            bodyFilter.sharedMesh = def.bodyMesh;
            bodyRenderer.sharedMaterial = mat;
            fit.PlaceBody(bodyRoot, def, new Vector3(0f, 0f, -centre));

            for (int i = 0; i < 4; i++)
            {
                fit.PlaceWheel(wheelFilters[i].transform, i, new Vector3(0f, 0f, -centre));
                brakeRadius[i] = fit.Radius(i);
                wheelFilters[i].sharedMesh = def.wheelMesh;
                wheelRenderers[i].sharedMaterial = wheelMat;
                CarPaint.DullWheels(wheelRenderers[i]);
                if (donor != null)
                {
                    wheelFilters[i].sharedMesh = donor.wheelMesh;
                    wheelRenderers[i].sharedMaterial = donorMat;
                    CarPaint.DullWheels(wheelRenderers[i]);
                    wheelFilters[i].transform.localScale =
                        Vector3.Scale(wheelFilters[i].transform.localScale, donorRatio);
                }
            }

            if (shadow != null)
                shadow.localScale = new Vector3(def.blobSize.x * sx * 1.5f, def.blobSize.y * fit.sz * 1.5f, 1f);

            // Frame the car it IS, not the car the framing was picked on. A
            // Daytona is a metre longer than an FD and a supermini a metre
            // shorter; one fixed distance either crops the first or strands the
            // second in the middle of an empty panel.
            //
            // Framed off the HORIZONTAL field of view, which is the one that
            // runs out first on a 16:10 panel: a 34-degree vertical lens on a
            // 1.6 aspect gives a 26-degree horizontal half-angle, so a car
            // whose three-quarter view spans about its own length needs
            // (L/2)/tan(26) metres, plus a quarter for margin. The first
            // version used a flat 1.85x and put a 4 m car in the middle of an
            // otherwise empty panel.
            carLength = Mathf.Max(def.colliderSize.z * fit.sz, 3f);
            carWidth = def.colliderSize.x * fit.sx;
            carHeight = def.bodyMesh.bounds.max.y + fit.bodyY;
            distance = carLength * 1.22f;
            PlaceCamera();
            if (wheelsOff) SetWheelsOff(true);
        }

        void PlaceCamera()
        {
            if (cam == null) return;
            if (pose == ViewPose.Plan)
            {
                cam.orthographic = true;
                cam.orthographicSize = planHigh * 0.5f;
                cam.transform.position = rig.position + Vector3.up * 20f;
                cam.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                return;
            }
            if (pose == ViewPose.Side)
            {
                // Level with the hubs, far enough that the whole car fills the
                // panel's width.
                cam.orthographic = false;
                var hubs = rig.position + Vector3.up * Mathf.Max(0.55f, carHeight * 0.5f);
                // Measured to the near side, which is what runs out of the frame.
                float tan = Mathf.Tan(17f * Mathf.Deg2Rad);
                float back = carWidth * 0.55f + Mathf.Max(carLength * 0.56f / (tan * Mathf.Max(poseAspect, 1f)), carHeight * 0.56f / tan);
                cam.transform.position = hubs + new Vector3(0f, 0.1f, -Mathf.Max(back, 2f));
                cam.transform.rotation = Quaternion.LookRotation(hubs - cam.transform.position, Vector3.up);
                return;
            }
            // Three-quarter view from slightly above eye level — the angle every
            // showroom photograph is taken from, because it shows length, width
            // and face at once.
            float h = carLength * 0.34f;
            var focus = rig.position + Vector3.up * (carLength * 0.16f);
            cam.transform.position = focus + new Vector3(0f, h, -distance);
            cam.transform.rotation = Quaternion.LookRotation(focus - cam.transform.position, Vector3.up);
        }

        void Update()
        {
            if (!Visible || rig == null) return;
            yaw += spin * Time.unscaledDeltaTime + dragYaw;
            dragYaw = 0f;
            if (pose != ViewPose.Showroom) yaw = 90f;   // pinned: drags and taps do nothing
            rig.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>After Update, so the frame drawn is the frame the turntable
        /// just moved to rather than the one before it.</summary>
        void LateUpdate()
        {
            if (Visible) RenderNow();
        }

        float dragYaw;

        /// <summary>
        /// One still of a car at the showroom three-quarter, copied out of the
        /// turntable's texture so several cars can sit side by side — the new
        /// game's car pick shows all three lanes at once, and one rig can only
        /// hold one car. The caller owns the texture (Destroy it with the panel).
        /// Leaves the rig hidden.
        /// </summary>
        public RenderTexture Snapshot(CarSpec spec)
        {
            EnsureRig();
            SetVisible(true);
            Show(spec);
            float keep = yaw;
            yaw = 215f;
            RenderNow();
            yaw = keep;
            var copy = new RenderTexture(width, height, 0, rt.format)
            {
                name = "CarSnapshot", filterMode = FilterMode.Point, antiAliasing = 1,
            };
            copy.Create();
            Graphics.Blit(rt, copy);
            SetVisible(false);
            return copy;
        }

        /// <summary>Nudge the turntable. Called by the drag handler on the panel
        /// and by the arrow keys; the idle spin keeps running underneath so a
        /// released car does not just stop dead.</summary>
        public void Nudge(float degrees) => dragYaw += degrees;

        /// <summary>
        /// Attach a viewport to a menu rect: the RawImage that shows the render
        /// texture, plus a drag handler that spins the car.
        /// </summary>
        public RawImage AttachTo(RectTransform panel)
        {
            EnsureRig();
            var go = new GameObject("CarView");
            go.transform.SetParent(panel, false);
            var raw = go.AddComponent<RawImage>();
            raw.texture = rt;
            var rrt = raw.rectTransform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;
            go.AddComponent<CarViewerDrag>().viewer = this;
            SetVisible(true);
            // Draw one frame immediately so the panel is never shown empty for
            // the frame between being built and LateUpdate coming round.
            RenderNow();
            return raw;
        }
    }

    /// <summary>
    /// Drag-to-spin on the viewport. Split out because a RawImage cannot
    /// receive drags without a component implementing the interface, and
    /// CarViewer itself lives on a different object.
    ///
    /// A vertical drag is handed BACK to the enclosing ScrollRect. Swallowing
    /// every gesture would make the viewer a dead zone the page cannot be
    /// scrolled through — and the viewer sits at the top of the garage, which
    /// is exactly where a thumb starts a flick.
    /// </summary>
    public class CarViewerDrag : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public CarViewer viewer;

        ScrollRect scroll;
        bool routeToScroll;

        void Awake() => scroll = GetComponentInParent<ScrollRect>();

        public void OnBeginDrag(PointerEventData e)
        {
            // Measured from the press point, not from this frame's delta: the
            // first delta of a drag is often a pixel or two of noise, and a
            // coin-flip on noise decides the whole gesture.
            Vector2 travel = e.position - e.pressPosition;
            routeToScroll = Mathf.Abs(travel.y) > Mathf.Abs(travel.x);
            if (routeToScroll && scroll != null) scroll.OnBeginDrag(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (routeToScroll) { if (scroll != null) scroll.OnDrag(e); return; }
            // Screen pixels, not canvas units: the gesture should feel the same
            // whether the canvas scaled up or down to fit the device.
            if (viewer != null) viewer.Nudge(-e.delta.x * 0.45f);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (routeToScroll && scroll != null) scroll.OnEndDrag(e);
            routeToScroll = false;
        }

        /// <summary>A tap with no drag flips the car end for end, which is the
        /// one thing a slow turntable makes you wait for.</summary>
        public void OnPointerClick(PointerEventData e)
        {
            if (viewer != null && !e.dragging) viewer.Nudge(180f);
        }
    }
}
