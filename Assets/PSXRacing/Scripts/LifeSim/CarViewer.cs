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

        string shownKey;
        int shownSkin = -1;
        /// <summary>Front three-quarter to start: the angle every showroom
        /// photograph is taken from, because it shows length, width and face at
        /// once. (Zero is square-on from BEHIND — the camera sits on -Z and the
        /// car's nose points +Z.)</summary>
        float yaw = 215f;
        float spin = 20f;   // degrees/second of idle rotation
        float distance = 7.4f;
        float carLength = 4.1f;

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
            Show(def, Paint.FactorySkin(spec, def));
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
            Show(def, skinOverride >= 0 ? skinOverride : Paint.SkinFor(car, spec, def));
        }

        public void Show(CarModelDef def, int skin)
        {
            if (def == null) return;
            EnsureRig();
            if (def.key == shownKey && skin == shownSkin) return;
            shownKey = def.key;
            shownSkin = skin;
            Shown = def;

            var mat = def.SkinCount > 0
                ? def.skinMaterials[Mathf.Clamp(skin, 0, def.SkinCount - 1)] : null;
            var wheelMat = def.wheelMaterial != null ? def.wheelMaterial : mat;

            // The turntable spins about ITS origin, so the car is placed with
            // its own middle there — otherwise a long car swings, which is what
            // the axle-midpoint origin the rest of the game uses would do here.
            // The two offsets inside that are the same ones CarBody applies:
            // the body sits back from the axles by the model's own asymmetry,
            // and the wheels sit symmetrically about them.
            float centre = def.colliderCenter.z;

            bodyFilter.sharedMesh = def.bodyMesh;
            bodyRenderer.sharedMaterial = mat;
            bodyRoot.localPosition = new Vector3(0f, def.bodyYOffset, def.bodyZOffset - centre);
            bodyRoot.localRotation = Quaternion.Euler(0f, def.bodyYaw, 0f);

            for (int i = 0; i < 4; i++)
            {
                bool left = i % 2 == 0;
                var t = wheelFilters[i].transform;
                t.localPosition = new Vector3(
                    (left ? -0.5f : 0.5f) * def.trackWidth,
                    def.wheelRadius,
                    (i < 2 ? 0.5f : -0.5f) * def.wheelbase - centre);
                t.localRotation = Quaternion.Euler(0f, left ? 180f : 0f, 0f);
                t.localScale = Vector3.one * def.wheelMeshScale;
                wheelFilters[i].sharedMesh = def.wheelMesh;
                wheelRenderers[i].sharedMaterial = wheelMat;
            }

            if (shadow != null)
                shadow.localScale = new Vector3(def.blobSize.x * 1.5f, def.blobSize.y * 1.5f, 1f);

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
            carLength = Mathf.Max(def.colliderSize.z, 3f);
            distance = carLength * 1.22f;
            PlaceCamera();
        }

        void PlaceCamera()
        {
            if (cam == null) return;
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
            rig.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>After Update, so the frame drawn is the frame the turntable
        /// just moved to rather than the one before it.</summary>
        void LateUpdate()
        {
            if (Visible) RenderNow();
        }

        float dragYaw;

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
