using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace PSXRacing
{
    /// <summary>
    /// DAMAGE ROUND 3: THE COCKPIT THROUGH A BROKEN PANE (owner, 2026-11-17:
    /// "I'm not a fan of the low-poly blocky interior when windows are broken.
    /// If shown, the cockpit model should be used."). Round 2 drew the cabin
    /// behind a shattered side or rear pane as ray-cast grey boxes in the paint
    /// shader. Now a holed car shows the OWNER'S COCKPIT (CockpitModel, the
    /// one the cockpit view draws) fitted to this shell exactly as the cockpit
    /// view fits it (CockpitInterior.FitFor + Fit3: dash on the cowl, header
    /// under the roof, side glass inside the shell's) - seats, dash, door
    /// cards, headliner, the wheel, the cluster faces and the mirror.
    ///
    /// HOW IT STAYS INSIDE THE HOLE (no seat through a door or the roof
    /// whatever the shell): two companion renderers of the holed body, on
    /// copies of its PSX/CarPaint material, drawn after every shell - the
    /// first marks the hole's own pixels in the stencil (the paint's hole
    /// test, so the edge is the jagged one round 2 drew; ColorMask 0), the
    /// second pushes the depth there 3 m back along each sightline - and the
    /// cabin's materials (PSX/Cabin copies) pass only on that mark. Behind the
    /// cabin, and wherever it leaves a gap, the paint's own DARK cabin shows
    /// (round 2's hole without its boxes: the lining, the floor, the far
    /// glass). Lit as a room under its roof and fogged as the paint round it.
    ///
    /// PERFORMANCE (owner 11-16: performance first): nothing for a car without
    /// a shattered pane. Built LAZILY - the first time a camera that can draw
    /// it comes within <see cref="ReachM"/> of a holed car - one instance per
    /// car from a POOL (released 3 s after the last camera leaves), at most
    /// <see cref="MaxCabins"/> (the nearest win). Within <see cref="FullM"/>
    /// the full mesh; past it a REDUCED copy (CockpitLod: the same mesh
    /// vertex-clustered at 3 cm, built once on a worker thread). Past the reach
    /// the hole shows the dark cabin: no instance, no draw. No shadows, no
    /// arms (from outside they end at a shoulder with no body), no glass.
    /// A camera whose target has no stencil (the car reflection's faces, the
    /// old 16-bit mirrors) never draws it: it sees the dark cabin.
    ///
    /// PSX_DMG_CABIN=0: round 2's look (the boxes), nothing built.
    /// </summary>
    [DefaultExecutionOrder(1001)]
    public sealed class DamageCabin : MonoBehaviour
    {
        public static bool On = System.Environment.GetEnvironmentVariable("PSX_DMG_CABIN") != "0"
                                && System.Environment.GetEnvironmentVariable("PSX_DMG_GLASS2") != "0";

        /// <summary>A camera this near (m) to a holed car draws its cockpit; past it, the dark cabin.</summary>
        public static float ReachM => PlatformProfile.PC ? 40f : 20f;
        /// <summary>Nearer than this (m) the full mesh, else the reduced one.</summary>
        public static float FullM => PlatformProfile.PC ? 8f : 5f;
        /// <summary>Cabins built at once (the nearest holed cars).</summary>
        public static int MaxCabins => PlatformProfile.PC ? 8 : 4;
        /// <summary>A pane this broken is a hole (DamageGlass: 1 shattered, 0.5 crazed).</summary>
        public const float HoleState = 0.75f;
        public const int StencilBit = 64;
        public const int QueueMark = 2001, QueueDepth = 2002, QueueCabin = 2003;
        /// <summary>Released this long (s) after the last camera within reach.</summary>
        public const float KeepSeconds = 3f;

        // ------------------------------------------------------------------
        //  What this car is (set by ForCar / ForView)
        // ------------------------------------------------------------------
        Renderer body;
        MeshFilter bodyFilter;
        CarController car;          // a driven car: the cabin rides its leaning body
        CarBody carBody;
        CarModelDef def;
        Vector3 boxC, boxS, slide;
        float ls = 1f, lift, ws = 1f;
        bool rhd;
        bool holed;
        Vector4 panes;

        Instance inst;
        float lastWanted = -1e9f, nearest = float.MaxValue;
        int seenVersion = -1;
        Material markSrc;
        bool fitted;
        CockpitInterior.Fit fit;
        CockpitInterior cockpit;

        static readonly List<DamageCabin> all = new List<DamageCabin>();
        static readonly Stack<Instance> pool = new Stack<Instance>();
        static readonly Dictionary<string, Material> holeMats = new Dictionary<string, Material>();
        static bool hooked;
        static int stepFrame = -1;
        static MaterialPropertyBlock mpb;

        /// <summary>TOOLS: every cabin hidden from every camera (the dark cabin
        /// shows) - the cost probe's A/B in one process.</summary>
        public static bool Suspend;

        /// <summary>Built now / ever (the self-test and the cost note).</summary>
        public static int Live { get; private set; }
        public static int Built { get; private set; }

        public bool Holed => holed;
        public bool HasCabin => inst != null;
        public CockpitInterior.Fit CabinFit => fit;
        public Transform CabinRoot => inst != null ? inst.root.transform : null;
        /// <summary>The hole's two companions and the full cabin (the self-test).</summary>
        public Renderer MarkRenderer => inst != null ? inst.mark : null;
        public Renderer DepthRenderer => inst != null ? inst.depth : null;
        public Renderer CabinRenderer => inst != null ? inst.cabinFull : null;
        public Renderer CabinLowRenderer => inst != null ? inst.cabinLow : null;

        static readonly Unity.Profiling.ProfilerMarker StepMarker = new Unity.Profiling.ProfilerMarker("DamageCabin.Step"),
            CamMarker = new Unity.Profiling.ProfilerMarker("DamageCabin.Camera");

        // ------------------------------------------------------------------
        //  The two ways in
        // ------------------------------------------------------------------

        static bool Holes(Vector4 p) => p.y >= HoleState || p.z >= HoleState || p.w >= HoleState;

        /// <summary>The driven car (DamageLook.Push): its rows' look now.</summary>
        public static void ForCar(CarController c, CarBody b, DamageLook.Look look)
        {
            if (c == null) return;
            var dc = c.GetComponent<DamageCabin>();
            if (!On || !Holes(look.panes) || b == null || b.bodyRenderer == null)
            {
                if (dc != null) dc.Clear();
                return;
            }
            if (dc == null) dc = c.gameObject.AddComponent<DamageCabin>();
            var box = c.GetComponent<BoxCollider>();
            dc.Set(b.bodyRenderer, b.Def,
                   box != null ? box.center : new Vector3(0f, 0.72f, 0.05f), box != null ? box.size : new Vector3(1.72f, 1f, 4.1f),
                   Vector3.zero, b.LengthScale, b.BodyLift, b.WidthScale, DriveSide.IsRHD(c), look.panes);
            dc.car = c;
            dc.carBody = b;
            if (!Application.isPlaying) Step(true);   // a picture tool: no frames to wait for
        }

        /// <summary>A display model (DamageView.Dress): its frame, its body
        /// renderer, the body box in the frame and the car's look.</summary>
        public static void ForView(Transform frame, Renderer bodyR, Vector3 boxCentre, Vector3 boxSize, CarModelDef d,
                                   float stretchZ, DamageLook.Look look, string specId)
        {
            if (frame == null) return;
            var dc = frame.GetComponent<DamageCabin>();
            if (!On || !Holes(look.panes) || bodyR == null || d == null)
            {
                if (dc != null) dc.Clear();
                return;
            }
            if (dc == null) dc = frame.gameObject.AddComponent<DamageCabin>();
            // The rig's own frame (axle midpoint on the ground): the display
            // slid it along so the car's middle is on the frame's origin.
            float sx = d.colliderSize.x > 0.01f ? boxSize.x / d.colliderSize.x : 1f;
            float sz = Mathf.Max(0.5f, stretchZ);
            float dy = boxCentre.y - d.colliderCenter.y;
            var rigC = new Vector3(d.colliderCenter.x * sx, d.colliderCenter.y + dy, d.colliderCenter.z * sz);
            dc.Set(bodyR, d, rigC, boxSize, boxCentre - rigC, sz, dy, sx, DriveSide.IsRHD(specId), look.panes);
            dc.car = null;
            dc.carBody = null;
            if (!Application.isPlaying) Step(true);   // a picture tool: no frames to wait for
        }

        void Set(Renderer bodyR, CarModelDef d, Vector3 c, Vector3 s, Vector3 sl, float lengthScale, float bodyLift,
                 float widthScale, bool isRhd, Vector4 p)
        {
            bool refit = !fitted || d != def || c != boxC || s != boxS || sl != slide || lengthScale != ls
                         || bodyLift != lift || widthScale != ws || isRhd != rhd || bodyR != body;
            body = bodyR;
            bodyFilter = bodyR != null ? bodyR.GetComponent<MeshFilter>() : null;
            def = d; boxC = c; boxS = s; slide = sl; ls = lengthScale; lift = bodyLift; ws = widthScale; rhd = isRhd;
            panes = p;
            holed = true;
            seenVersion = -1;
            if (refit) { fitted = false; if (inst != null) Place(); }
            if (!all.Contains(this)) all.Add(this);
            Hook();
            CockpitModel.Request();
        }

        /// <summary>No hole any more (a repair, a mint car on a reused model).</summary>
        public void Clear()
        {
            holed = false;
            Release();
            all.Remove(this);
        }

        // Hidden with its frame (a hidden menu model): the cabin is its child and
        // hides with it; never re-parented while the frame is being switched off.
        void OnEnable() { if (holed && !all.Contains(this)) { all.Add(this); Hook(); } }

        void OnDestroy()
        {
            all.Remove(this);
            if (inst == null) return;
            Live--;
            // going with the frame: not back to the pool
            DestroyNow(inst.mark != null ? inst.mark.gameObject : null);
            DestroyNow(inst.depth != null ? inst.depth.gameObject : null);
            DestroyNow(inst.root);
            DestroyNow(inst.markMat);
            DestroyNow(inst.depthMat);
            inst = null;
        }

        static void DestroyNow(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        // ------------------------------------------------------------------
        //  Each frame: who gets a cabin
        // ------------------------------------------------------------------

        void Update() => Step(false);

        /// <summary>Once a frame for all holed cars: the nearest within reach
        /// of a camera get a cabin (built if needed), the rest give theirs back.
        /// <paramref name="now"/>: a picture tool in edit mode - build at once,
        /// the reduced mesh too, ignoring the clock.</summary>
        public static void Step(bool now)
        {
            if (!now && stepFrame == Time.frameCount) return;
            stepFrame = Time.frameCount;
            using (StepMarker.Auto())
            {
                float t = Time.realtimeSinceStartup;
                if (!CockpitModel.Ready) { if (all.Count > 0) CockpitModel.Request(); return; }
                if (now) CockpitLod.BuildNow(); else CockpitLod.Start();
                // the nearest first
                all.RemoveAll(d => d == null);
                all.Sort((a, b) => a.nearest.CompareTo(b.nearest));
                int n = 0;
                foreach (var d in all)
                {
                    bool want = d.holed && (now || (d.enabled && d.gameObject.activeInHierarchy && t - d.lastWanted < KeepSeconds)) && !d.CockpitShown();
                    if (want && n < MaxCabins) { n++; d.Acquire(); }
                    else d.Release();
                    d.nearest = float.MaxValue;
                }
                foreach (var d in all) if (d.inst != null) d.Tick();
            }
        }

        /// <summary>This car's own cockpit view is up: the cabin is the real one
        /// (the shell is cut), so the hole's copy stands down. A display model
        /// has none (looked for once); in edit mode (the picture tools build
        /// the cockpit on the player when they need it) looked for every time.</summary>
        bool CockpitShown()
        {
            if (cockpit == null && (car != null || !Application.isPlaying || !cockpitLooked))
            {
                cockpitLooked = true;
                cockpit = (car != null ? car.transform : transform).GetComponentInChildren<CockpitInterior>(true);
            }
            return cockpit != null && cockpit.Shown;
        }

        bool cockpitLooked;

        void LateUpdate()
        {
            // the driven car's body leans and rides its tyres: the cabin rides with it
            if (inst != null && carBody != null) Ride();
        }

        // ------------------------------------------------------------------
        //  Per camera: who draws it
        // ------------------------------------------------------------------

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        /// <summary>The camera's target can hold the hole's mark (a stencil).</summary>
        public static bool HasStencil(Camera cam)
        {
            var rt = cam != null ? cam.targetTexture : null;
            if (rt == null) return true;
            return GraphicsFormatUtility.IsStencilFormat(rt.depthStencilFormat);
        }

        static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam) => ForCamera(cam);

        /// <summary>Show each built cabin to this camera or not (public for the
        /// picture tools, whose edit-mode renders also come through here).</summary>
        public static void ForCamera(Camera cam)
        {
            if (all.Count == 0 || cam == null) return;
            using (CamMarker.Auto())
            {
                bool game = cam.cameraType == CameraType.Game || cam.cameraType == CameraType.SceneView;
                bool stencil = HasStencil(cam);
                Vector3 eye = cam.transform.position;
                float reach = ReachM, full = FullM;
                foreach (var d in all)
                {
                    if (d == null || !d.holed) continue;
                    Vector3 at = d.body != null ? d.body.bounds.center : d.transform.position;
                    float dist = Vector3.Distance(eye, at);
                    if (game && stencil && dist < reach)
                    {
                        d.lastWanted = Time.realtimeSinceStartup;
                        if (dist < d.nearest) d.nearest = dist;
                    }
                    bool cut = d.cockpit != null && d.cockpit.Shown;
                    if (d.inst != null) d.inst.Show(!Suspend && !cut && game && stencil && dist < reach + 2f, dist < full || !CockpitLod.Ready);
                }
            }
        }

        // ------------------------------------------------------------------
        //  The instance
        // ------------------------------------------------------------------

        sealed class Instance
        {
            public GameObject root;
            public Transform model, wheelRoot;
            public MeshRenderer cabinFull, cabinLow, wheelFull, wheelLow, extras;
            public MeshRenderer mark, depth;
            public MeshFilter markF, depthF, cabinLowF, wheelLowF, extrasF;
            public Material markMat, depthMat;
            public uint layers;
            public int layer = -1;
            bool shown = true, fullOn = true;
            bool any;

            public void Show(bool on, bool full)
            {
                if (any && on == shown && full == fullOn) return;
                any = true; shown = on; fullOn = full;
                bool lowReady = cabinLowF != null && cabinLowF.sharedMesh != null;
                Set(mark, on); Set(depth, on);
                Set(cabinFull, on && (full || !lowReady)); Set(cabinLow, on && !full && lowReady);
                bool wLow = wheelLowF != null && wheelLowF.sharedMesh != null;
                Set(wheelFull, on && (full || !wLow)); Set(wheelLow, on && !full && wLow);
                Set(extras, on && extrasF != null && extrasF.sharedMesh != null);
            }

            public void Reset() => any = false;

            static void Set(Renderer r, bool on) { if (r != null && r.forceRenderingOff == on) r.forceRenderingOff = !on; }
        }

        void Acquire()
        {
            if (inst != null) { Refresh(); return; }
            if (body == null || bodyFilter == null) return;
            var L = CockpitModel.L;
            var parts = CockpitModel.Parts;
            if (L == null || parts == null) return;
            inst = pool.Count > 0 ? pool.Pop() : Make(parts, L);
            if (inst == null) return;
            Live++;
            inst.root.transform.SetParent(transform, false);
            inst.root.SetActive(true);
            inst.mark.transform.SetParent(body.transform, false);
            inst.depth.transform.SetParent(body.transform, false);
            inst.mark.gameObject.SetActive(true);
            inst.depth.gameObject.SetActive(true);
            markSrc = null;
            seenVersion = -1;
            fitted = false;
            Place();
            Refresh();
            inst.Reset();
            inst.Show(false, true);   // the next camera decides
        }

        void Release()
        {
            if (inst == null) return;
            Live--;
            if (inst.root != null)
            {
                inst.root.SetActive(false);
                inst.root.transform.SetParent(null, false);
                if (Application.isPlaying) DontDestroyOnLoad(inst.root);
            }
            foreach (var r in new Renderer[] { inst.mark, inst.depth })
                if (r != null)
                {
                    r.gameObject.SetActive(false);
                    r.transform.SetParent(inst.root != null ? inst.root.transform : null, false);
                }
            if (inst.root != null) pool.Push(inst);
            inst.layer = -1;
            inst = null;
        }

        static Material HoleMat(string key)
        {
            if (holeMats.TryGetValue(key, out var m) && m != null) return m;
            var src = CockpitModel.Mat(key) ?? CockpitModel.Mat("abs");
            if (src == null) return null;
            m = new Material(src) { name = "HoleCabin_" + key, hideFlags = HideFlags.HideAndDontSave };
            m.SetFloat("_PSXStRef", StencilBit);
            m.SetFloat("_PSXStRead", StencilBit);
            m.SetFloat("_PSXStComp", (float)CompareFunction.Equal);
            m.SetFloat("_PSXCabOut", 1f);
            m.renderQueue = QueueCabin;
            holeMats[key] = m;
            return m;
        }

        static MeshRenderer Part(Transform parent, string name, Mesh mesh, string[] mats, out MeshFilter mf)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            if (mats != null)
            {
                var ms = new Material[mats.Length];
                for (int i = 0; i < ms.Length; i++) ms[i] = HoleMat(mats[i]);
                r.sharedMaterials = ms;
            }
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
            return r;
        }

        static Instance Make(Dictionary<string, CockpitModel.Part> parts, CockpitModel.Layout L)
        {
            if (!parts.TryGetValue("cabin", out var cab) || cab.mesh == null) return null;
            var i = new Instance();
            i.root = new GameObject("DamageCabin");
            if (!Application.isPlaying) i.root.hideFlags = HideFlags.DontSave;
            i.model = new GameObject("Model").transform;
            i.model.SetParent(i.root.transform, false);
            i.cabinFull = Part(i.model, "cabin", cab.mesh, cab.mats, out _);
            i.cabinLow = Part(i.model, "cabin_low", null, cab.mats, out i.cabinLowF);
            i.extras = Part(i.model, "faces", null, new[] { "abs" }, out i.extrasF);
            i.wheelRoot = new GameObject("WheelRoot").transform;
            i.wheelRoot.SetParent(i.root.transform, false);
            var piv = new GameObject("WheelPivot").transform;
            piv.SetParent(i.wheelRoot, false);
            piv.localRotation = L.wheel != null ? L.wheel.rot : Quaternion.identity;
            if (parts.TryGetValue("wheel", out var wp) && wp.mesh != null)
            {
                i.wheelFull = Part(piv, "wheel", wp.mesh, wp.mats, out _);
                i.wheelLow = Part(piv, "wheel_low", null, wp.mats, out i.wheelLowF);
            }
            var mk = new GameObject("HoleMark");
            i.markF = mk.AddComponent<MeshFilter>();
            i.mark = mk.AddComponent<MeshRenderer>();
            var dp = new GameObject("HoleDepth");
            i.depthF = dp.AddComponent<MeshFilter>();
            i.depth = dp.AddComponent<MeshRenderer>();
            foreach (var r in new[] { i.mark, i.depth })
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.allowOcclusionWhenDynamic = false;
                if (!Application.isPlaying) r.gameObject.hideFlags = HideFlags.DontSave;
                r.transform.SetParent(i.root.transform, false);
            }
            foreach (var t in i.root.GetComponentsInChildren<Transform>(true))
                if (!Application.isPlaying) t.gameObject.hideFlags = HideFlags.DontSave;
            SunShadows.Exclude(i.root);
            Built++;
            return i;
        }

        /// <summary>The body's material, its mesh, its block, its layers onto
        /// the two companions; the reduced meshes once they exist.</summary>
        void Refresh()
        {
            if (inst == null || body == null) return;
            var src = body.sharedMaterial;
            if (src != null && src != markSrc)
            {
                if (inst.markMat != null) DestroyImmediate(inst.markMat);
                if (inst.depthMat != null) DestroyImmediate(inst.depthMat);
                inst.markMat = new Material(src) { name = "HoleMark", hideFlags = HideFlags.HideAndDontSave };
                inst.markMat.SetFloat("_PSXHoleMode", 1f);
                inst.markMat.SetFloat("_PSXStRef", StencilBit);
                inst.markMat.SetFloat("_PSXStWrite", StencilBit);
                inst.markMat.SetFloat("_PSXStComp", (float)CompareFunction.Always);
                inst.markMat.SetFloat("_PSXStPass", (float)StencilOp.Replace);
                inst.markMat.SetFloat("_PSXZWrite", 0f);
                inst.markMat.SetFloat("_PSXZTest", (float)CompareFunction.LessEqual);
                inst.markMat.SetFloat("_PSXColorMask", 0f);
                inst.markMat.SetFloat("_PSXCull", 2f);
                inst.markMat.SetFloat("_PSXInside", 0f);
                inst.markMat.renderQueue = QueueMark;
                inst.depthMat = new Material(inst.markMat) { name = "HoleDepth", hideFlags = HideFlags.HideAndDontSave };
                inst.depthMat.SetFloat("_PSXHoleMode", 2f);
                inst.depthMat.SetFloat("_PSXStRead", StencilBit);
                inst.depthMat.SetFloat("_PSXStWrite", 0f);
                inst.depthMat.SetFloat("_PSXStComp", (float)CompareFunction.Equal);
                inst.depthMat.SetFloat("_PSXStPass", (float)StencilOp.Keep);
                inst.depthMat.SetFloat("_PSXZWrite", 1f);
                inst.depthMat.SetFloat("_PSXZTest", (float)CompareFunction.Always);
                inst.depthMat.renderQueue = QueueDepth;
                markSrc = src;
                int n = Mathf.Max(1, body.sharedMaterials.Length);
                var mm = new Material[n];
                var dm = new Material[n];
                for (int k = 0; k < n; k++) { mm[k] = inst.markMat; dm[k] = inst.depthMat; }
                inst.mark.sharedMaterials = mm;
                inst.depth.sharedMaterials = dm;
            }
            var mesh = bodyFilter != null ? bodyFilter.sharedMesh : null;
            if (inst.markF.sharedMesh != mesh) { inst.markF.sharedMesh = mesh; inst.depthF.sharedMesh = mesh; }
            if (seenVersion != DamageLook.BlockVersion)
            {
                seenVersion = DamageLook.BlockVersion;
                if (mpb == null) mpb = new MaterialPropertyBlock();
                body.GetPropertyBlock(mpb);
                inst.mark.SetPropertyBlock(mpb);
                inst.depth.SetPropertyBlock(mpb);
            }
            uint layers = body.renderingLayerMask;
            int layer = body.gameObject.layer;
            if (inst.layers != layers || inst.layer != layer)
            {
                // the body's camera layer and rendering layers (SpeedBlur's car mask)
                inst.layers = layers;
                inst.layer = layer;
                foreach (var r in inst.root.GetComponentsInChildren<Renderer>(true)) { r.renderingLayerMask = layers; r.gameObject.layer = layer; }
                foreach (var r in new Renderer[] { inst.mark, inst.depth }) { r.renderingLayerMask = layers; r.gameObject.layer = layer; }
            }
            if (CockpitLod.Ready)
            {
                if (inst.cabinLowF.sharedMesh == null) { inst.cabinLowF.sharedMesh = CockpitLod.Cabin; inst.Reset(); }
                if (inst.wheelLowF != null && inst.wheelLowF.sharedMesh == null) { inst.wheelLowF.sharedMesh = CockpitLod.Wheel; inst.Reset(); }
                if (inst.extrasF.sharedMesh == null) { inst.extrasF.sharedMesh = CockpitLod.Faces; inst.Reset(); }
            }
        }

        void Tick()
        {
            if (inst == null) return;
            if (body == null) { Release(); return; }
            Refresh();
            if (!fitted) Place();
        }

        /// <summary>The cockpit view's fit (CockpitInterior.FitFor + Fit3) and
        /// its transforms (Apply), at the car's rest.</summary>
        void Place()
        {
            if (inst == null) return;
            var L = CockpitModel.L;
            if (L == null) return;
            var f = CockpitInterior.FitFor(boxC, boxS, def, ls, lift, rhd);
            f = CockpitInterior.Fit3(f, def, ls, lift, ws);
            fit = f;
            fitted = true;
            float sxAcross = f.scale * f.widthFit;
            var ms = new Vector3(f.rhd ? -sxAcross : sxAcross, f.ScaleY, f.scale);
            inst.model.localScale = ms;
            inst.model.localPosition = -Vector3.Scale(ms, L.eye);
            inst.model.localRotation = Quaternion.identity;
            CockpitInterior.WheelFrame(f, out var wPos, out var wScale, out _);
            inst.wheelRoot.localPosition = wPos;
            inst.wheelRoot.localRotation = Quaternion.identity;
            inst.wheelRoot.localScale = wScale;
            restEye = f.eye + slide;
            var rt = inst.root.transform;
            rt.localPosition = restEye;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
            if (carBody != null) Ride();
        }

        Vector3 restEye;

        /// <summary>The body's motion from its rest seat, as the cockpit takes
        /// it (CockpitInterior.Ride): the cabin stays in its shell.</summary>
        void Ride()
        {
            var br = carBody != null ? carBody.bodyRoot : null;
            if (br == null || carBody.Def == null || br.parent != transform || inst == null) return;
            var rideRot = br.localRotation * Quaternion.Inverse(Quaternion.Euler(0f, carBody.Def.bodyYaw, 0f));
            var ridePos = br.localPosition - rideRot * carBody.SeatPosition;
            var rt = inst.root.transform;
            rt.localPosition = rideRot * restEye + ridePos;
            rt.localRotation = rideRot;
        }

        /// <summary>Every holed car tracked now (the picture tools and the self-test).</summary>
        public static IReadOnlyList<DamageCabin> All => all;
    }

    /// <summary>
    /// THE REDUCED COCKPIT for the broken-glass cabin past
    /// <see cref="DamageCabin.FullM"/>: the owner's cabin and wheel
    /// vertex-clustered at <see cref="Cell"/> (each material on its own; the
    /// faces of one cell that look the same way merged, so a seat's front and
    /// back never fuse), and the four cluster faces with the mirror in one
    /// draw. Read from the model's own geometry file on a worker thread the
    /// first time a holed car is near; the full mesh stands in until then.
    /// Measured on v08 (offline): the cabin 55,169 -> about 26,000 triangles,
    /// the wheel 7,402 -> about 1,100. No new art: the same surfaces, fewer
    /// corners.
    /// </summary>
    public static class CockpitLod
    {
        public const float Cell = 0.03f;
        public static readonly string[] FaceParts = { "gauge_temp", "gauge_tach", "gauge_speed", "gauge_fuel", "mirror" };

        public static Mesh Cabin { get; private set; }
        public static Mesh Wheel { get; private set; }
        public static Mesh Faces { get; private set; }
        public static bool Ready => Cabin != null;
        public static int CabinTris { get; private set; }
        public static int WheelTris { get; private set; }
        public static float BuildMs { get; private set; }

        sealed class Raw
        {
            public Vector3[] pos, nrm;
            public Vector2[] uv;
            public Color32[] col;
            public int[][] subs;
        }

        sealed class Result { public Raw cabin, wheel, faces; public float ms; }

        static System.Threading.Tasks.Task<Result> task;
        static bool failed;

        /// <summary>Start the build (a worker thread), or take its result once done.</summary>
        public static void Start()
        {
            if (Ready || failed) return;
            if (task == null)
            {
                var bytes = CockpitModel.GeoBytes();
                if (bytes == null) return;
                task = System.Threading.Tasks.Task.Run(() => Build(bytes));
                return;
            }
            if (!task.IsCompleted) return;
            if (task.IsFaulted || task.Result == null)
            {
                failed = true;
                Debug.LogWarning("[DamageCabin] the reduced cockpit failed: " + (task.Exception != null ? task.Exception.GetBaseException().Message : "no result") + " - the full mesh everywhere");
                return;
            }
            Finish(task.Result);
        }

        /// <summary>Build it now, on this thread (the picture tools, edit mode).</summary>
        public static void BuildNow()
        {
            if (Ready || failed) return;
            var bytes = CockpitModel.GeoBytes();
            if (bytes == null) return;
            try { Finish(Build(bytes)); }
            catch (System.Exception e) { failed = true; Debug.LogWarning("[DamageCabin] the reduced cockpit failed: " + e.Message); }
        }

        static Result Build(byte[] bytes)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var names = new HashSet<string> { "cabin", "wheel" };
            foreach (var f in FaceParts) names.Add(f);
            var parts = CockpitModel.ReadRaw(bytes, names);
            var res = new Result();
            if (parts.TryGetValue("cabin", out var c)) res.cabin = Cluster(c, Cell);
            if (parts.TryGetValue("wheel", out var w)) res.wheel = Cluster(w, Cell);
            // the faces: every part in one submesh, as they are
            var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color32>();
            var idx = new List<int>();
            foreach (var f in FaceParts)
            {
                if (!parts.TryGetValue(f, out var p)) continue;
                int b = pos.Count;
                pos.AddRange(p.pos); nrm.AddRange(p.nrm); uv.AddRange(p.uv); col.AddRange(p.col);
                foreach (var s in p.subs) foreach (var i in s) idx.Add(b + i);
            }
            if (pos.Count > 0)
                res.faces = new Raw { pos = pos.ToArray(), nrm = nrm.ToArray(), uv = uv.ToArray(), col = col.ToArray(), subs = new[] { idx.ToArray() } };
            res.ms = (float)sw.Elapsed.TotalMilliseconds;
            return res;
        }

        /// <summary>Vertex clustering per material: one vertex per cell of
        /// <paramref name="cell"/> metres and facing (the six axis directions),
        /// at the members' mean; triangles that collapse or repeat dropped.</summary>
        static Raw Cluster(CockpitModel.RawPart p, float cell)
        {
            var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color32>();
            var cnt = new List<int>();
            var subs = new int[p.subs.Length][];
            for (int s = 0; s < p.subs.Length; s++)
            {
                var map = new Dictionary<long, int>();
                var remap = new Dictionary<int, int>();
                var src = p.subs[s];
                foreach (int v in src)
                {
                    if (remap.ContainsKey(v)) continue;
                    Vector3 q = p.pos[v], n = p.nrm[v];
                    float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                    int face = ax >= ay && ax >= az ? (n.x > 0f ? 0 : 1) : ay >= az ? (n.y > 0f ? 2 : 3) : (n.z > 0f ? 4 : 5);
                    long kx = Mathf.FloorToInt(q.x / cell) + 4096, ky = Mathf.FloorToInt(q.y / cell) + 4096, kz = Mathf.FloorToInt(q.z / cell) + 4096;
                    long key = ((kx * 8192 + ky) * 8192 + kz) * 8 + face;
                    if (!map.TryGetValue(key, out int id))
                    {
                        id = pos.Count;
                        map[key] = id;
                        pos.Add(Vector3.zero); nrm.Add(Vector3.zero); uv.Add(p.uv[v]); col.Add(p.col[v]); cnt.Add(0);
                    }
                    pos[id] += q; nrm[id] += n; cnt[id]++;
                    remap[v] = id;
                }
                var tris = new List<int>(src.Length / 2);
                var seen = new HashSet<long>();
                for (int t = 0; t + 2 < src.Length; t += 3)
                {
                    int a = remap[src[t]], b = remap[src[t + 1]], c = remap[src[t + 2]];
                    if (a == b || b == c || a == c) continue;
                    int lo = Mathf.Min(a, Mathf.Min(b, c)), hi = Mathf.Max(a, Mathf.Max(b, c)), mid = a + b + c - lo - hi;
                    long k = ((long)lo * 1000003L + mid) * 1000003L + hi;
                    if (!seen.Add(k)) continue;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                }
                subs[s] = tris.ToArray();
            }
            for (int i = 0; i < pos.Count; i++)
            {
                pos[i] /= Mathf.Max(1, cnt[i]);
                nrm[i] = nrm[i].sqrMagnitude > 1e-12f ? nrm[i].normalized : Vector3.up;
            }
            return new Raw { pos = pos.ToArray(), nrm = nrm.ToArray(), uv = uv.ToArray(), col = col.ToArray(), subs = subs };
        }

        static Mesh Make(string name, Raw r, out int tris)
        {
            tris = 0;
            if (r == null || r.pos.Length == 0) return null;
            var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            if (r.pos.Length > 65535) m.indexFormat = IndexFormat.UInt32;
            m.vertices = r.pos;
            m.normals = r.nrm;
            m.uv = r.uv;
            m.colors32 = r.col;
            m.subMeshCount = r.subs.Length;
            for (int s = 0; s < r.subs.Length; s++) { m.SetTriangles(r.subs[s], s, false); tris += r.subs[s].Length / 3; }
            m.RecalculateBounds();
            m.RecalculateTangents();
            m.UploadMeshData(!Application.isEditor);
            return m;
        }

        static void Finish(Result r)
        {
            if (r == null) { failed = true; return; }
            Wheel = Make("CockpitLod_wheel", r.wheel, out int wt);
            Faces = Make("CockpitLod_faces", r.faces, out _);
            Cabin = Make("CockpitLod_cabin", r.cabin, out int ct);
            CabinTris = ct;
            WheelTris = wt;
            BuildMs = r.ms;
            Debug.Log($"[DamageCabin] reduced cockpit: cabin {ct} tris, wheel {wt}, built in {r.ms:F0} ms (worker)");
        }
    }
}
