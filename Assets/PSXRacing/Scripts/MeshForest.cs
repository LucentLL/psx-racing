using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;

namespace PSXRacing
{
    /// <summary>
    /// Real mesh forest on the mountain stages (owner 2026-11-07: "overhauling
    /// the Mountain Tracks (tree models ... first) ... Focus on performance with
    /// LOD (low, mid high)"), from the owner's NatureManufacture packs.
    ///
    /// The stage builder still plants every tree where it always did, and the
    /// trunk table (TreeTrunks) still stops the car at each one. What changed:
    /// the broadleaf trees' crossed cards live in their own chunk meshes
    /// ("B" chunks), and this component holds the same trees - plus the extra
    /// canopy, the understory, the ground cover and the far slopes - as an
    /// instance table per chunk. Per chunk, per re-bucket:
    ///   - out of view or past <see cref="farRange"/>: the chunk's cards draw
    ///     (mirrors and reflection views looking back still see something);
    ///   - otherwise its cards are hidden and every plant in it is drawn,
    ///     GPU-instanced, at HIGH (LOD0) / MID (LOD1) / LOW (LOD2) by its own
    ///     distance, and past <see cref="meshRange"/> as the SAME tree's
    ///     impostor (LOD3, the pack's cross-card of that beech). Owner: "I'm
    ///     not sure I want different tree types used in distance. I'd rather
    ///     increase tree density (use lower LOD as necessary)".
    /// No GameObject per plant, no LODGroup.
    ///
    /// Leaves follow the season: each variant carries its own leaf and
    /// impostor material per dress (Winter, Spring, Summer, Fall, Snow).
    ///
    /// F8 flips between the mesh forest and the old cards everywhere (A/B).
    /// </summary>
    /// <summary>FOREST DETAIL (VISUALS): how far the mesh forest keeps its
    /// detail. Read when a stage loads.</summary>
    public static class ForestDetailPrefs
    {
        const string Key = "psx.forestdetail";
        static readonly string[] Names = { "LOW", "MEDIUM", "HIGH" };
        static readonly float[] Scales = { 0.65f, 1f, 1.5f };
        static int cached = -1;
        /// <summary>A perf tour's forest=0|1|2: this run only, never saved
        /// (-1: the player's own setting).</summary>
        public static int Override = -1;
        public static int Level
        {
            get { if (Override >= 0) return Mathf.Clamp(Override, 0, 2); if (cached < 0) cached = Mathf.Clamp(PlayerPrefs.GetInt(Key, 1), 0, 2); return cached; }
            set { cached = Mathf.Clamp(value, 0, 2); PlayerPrefs.SetInt(Key, cached); PlayerPrefs.Save(); }
        }
        public static float Scale => Scales[Level];
        public static string Label => Names[Level];
        public static void Cycle() => Level = (Level + 1) % 3;
    }

    // (after the chase camera's LateUpdate at 0 and the cockpit's at 1000:
    // the GPU path culls against the view as this frame will draw it)
    [DefaultExecutionOrder(1500)]
    public partial class MeshForest : MonoBehaviour
    {
        [System.Serializable]
        public class Variant
        {
            public Mesh[] lods = new Mesh[4];      // HIGH, MID, LOW, IMPOSTOR (may be null)
            public Material[] mats;                // per submesh (bark ... leaves)
            public bool[] leafSub;                 // per submesh: follows the dress
            public float height = 1f;              // model's own height (m)
            /// <summary>The LOD node's transform inside its FBX (unit scale,
            /// axis conversion), base at y = 0: applied before each plant's
            /// own placement.</summary>
            public Matrix4x4 pre = Matrix4x4.identity;
            /// <summary>The impostor node's transform, likewise.</summary>
            public Matrix4x4 impostorPre = Matrix4x4.identity;
            /// <summary>Leaves per dress (5); null entry = bare.</summary>
            public Material[] leafDress;
            /// <summary>Impostor material per dress (5).</summary>
            public Material[] impostorDress;
            /// <summary>Drawn no farther than this (m); 0 = the whole range.</summary>
            public float maxDist;
            /// <summary>&gt; 0: at its draw limit this plant THINS OUT - every
            /// instance leaves at its own distance between thin x the limit and
            /// the limit - instead of all of them on one ring (owner 2026-11-08:
            /// "after the blue line I drew ... there is no foliage").</summary>
            public float thin;
            /// <summary>Casts shadows at HIGH and MID (trees, not grass).</summary>
            public bool shadows = true;
        }

        [System.Serializable]
        public class Chunk
        {
            public Vector3 centre;                 // local to this transform
            public float radius;
            public Renderer[] cards;               // the chunk's "B" cards and crowns
            public int start, count;               // into the instance arrays
        }

        public Variant[] variants;
        public Chunk[] chunks;
        public Vector4[] inst;                     // local x, y, z, yaw (deg)
        public Vector2[] instScaleVar;             // x = scale, y = variant index
        /// <summary>Optional per plant (empty = all upright): pitch and roll
        /// in degrees, laying ground litter, branches, logs and stones on
        /// the slope.</summary>
        public Vector2[] instTilt;
        public Material[] leafByDress = new Material[5];

        // The MEDIUM distances (ForestDetailPrefs scales them): the 100/200/320
        // of the pop-in round dropped the owner under 20 FPS with leaves on.
        public float lod0To = 65f, lod1To = 140f, meshRange = 240f, farRange = 2200f;
        /// <summary>Hand-over bands (m) ending at lod0To, lod1To and meshRange:
        /// inside one, a plant draws at both levels, each dithered (PSX/Lit's
        /// PSX_LODFADE) so the two add up to one - no pop, no shape jump.
        /// Owner 2026-11-08: "I see plants, bushes, trees pop-in often. Trees
        /// change shape (especially in winter) as they pop-in."</summary>
        public float band0 = 12f, band1 = 16f, band2 = 20f;
        /// <summary>A plant with a draw limit dissolves over this share of it.</summary>
        public float limitBand = 0.2f;

        public static bool MeshTreesOn = true;
        /// <summary>A/B (perf tour ab=MeshForest.PopFix:0 = the forest as PC26
        /// drew it): the pop-in round of 2026-11-11.</summary>
        public static bool PopFix = true;
        const float BandShare = 0.22f;
        static MeshForest instance;
        bool inited;

        const int L = 4;
        Matrix4x4[] localM;
        Matrix4x4[] worldM;                        // localM under the root as of the last Bake
        Vector3[] worldP;
        Matrix4x4 bakedRoot;
        Matrix4x4[] impFix;                        // per variant: pre^-1 * impostorPre
        // Far chunks (wholly past meshRange) draw their impostors from lists
        // made once, per chunk per variant: no per-tree work at distance.
        Matrix4x4[][][] farLists;                  // [chunk][variant]
        readonly System.Collections.Generic.List<int> farVisible = new System.Collections.Generic.List<int>();
        // THE OTHER CAMERAS (mirrors, the car-reflection probe's faces): the
        // canopy near the car as impostors only - two cards a tree, one draw a
        // species. Owner 2026-11-09: "performance is very poor when leaves are
        // on trees ... especially in cockpit view with mirrors" - every one of
        // those cameras drew the WHOLE mesh forest again.
        Matrix4x4[][] mirrorM;
        int[] mirrorN;
        Camera mainCam;
        Matrix4x4[][] bucket;                      // [variant * L + lod]
        int[] bucketN;
        Vector3 lastCam = new Vector3(1e9f, 0f, 0f);
        Matrix4x4 lastRoot;
        int lastDress = -2;
        bool lastOn = true;
        readonly Plane[] planes = new Plane[6];
        Material[][] matsNow;
        Material[] impNow;
        float[] maxD2;
        Vector4[][] fadeOf;                        // [variant][lod]: _PSXLodFade
        bool[][] handOver;                         // [variant][lod]: dithers into lod + 1
        readonly System.Collections.Generic.Dictionary<(Material, Vector4, float), Material> faded =
            new System.Collections.Generic.Dictionary<(Material, Vector4, float), Material>();

        void Awake() => Init();

        void Init()
        {
            instance = this;
            inited = true;
            float q = ForestDetailPrefs.Scale;
            lod0To *= q; lod1To *= q; meshRange *= q;
            band0 *= q; band1 *= q; band2 *= q;
            if (PopFix)
            {
                // a hand-over band as a share of its distance, not a fixed few metres
                band0 = Mathf.Max(band0, lod0To * BandShare);
                band1 = Mathf.Max(band1, lod1To * BandShare);
                band2 = Mathf.Max(band2, meshRange * BandShare);
            }
            matsNow = new Material[variants.Length][];
            impNow = new Material[variants.Length];
            maxD2 = new float[variants.Length];
            impFix = new Matrix4x4[variants.Length];
            fadeOf = new Vector4[variants.Length][];
            handOver = new bool[variants.Length][];
            float far = Mathf.Max(farRange, meshRange);
            for (int v = 0; v < variants.Length; v++)
            {
                var vr = variants[v];
                float m = vr.maxDist > 0f ? Mathf.Min(vr.maxDist * ForestDetailPrefs.Scale, far) : far;
                if (vr.lods.Length < L || vr.lods[3] == null) m = Mathf.Min(m, meshRange);
                maxD2[v] = m * m;
                impFix[v] = vr.pre.inverse * vr.impostorPre;
                fadeOf[v] = new Vector4[L];
                handOver[v] = new bool[L];
                float[] edge = { lod0To, lod1To, meshRange, far };
                float[] band = { band0, band1, band2, 0f };
                for (int l = 0; l < L; l++)
                {
                    bool has = l < vr.lods.Length && vr.lods[l] != null;
                    if (!has) continue;
                    var f = Vector4.zero;
                    if (l > 0) { f.x = edge[l - 1] - band[l - 1]; f.y = edge[l - 1]; }
                    bool next = l + 1 < L && l + 1 < vr.lods.Length && vr.lods[l + 1] != null && edge[l] < m;
                    if (next) { f.z = edge[l] - band[l]; f.w = edge[l]; handOver[v][l] = true; }
                    else if (m <= edge[l]) { f.z = m * (1f - limitBand); f.w = m; }
                    fadeOf[v][l] = f;
                }
            }
            // The hidden cards stay the canopy's SHADOW (SunShadows draws its
            // casters itself and these meshes are no renderers).
            foreach (var c in chunks)
                if (c.cards != null)
                    foreach (var r in c.cards) if (r != null) SunShadows.ShadowStandIns.Add(r);
            // THE GPU PATH (MeshForest.Gpu.cs) where the machine has compute
            // shaders and the scene carries the cull; the old per-frame
            // re-bucket everywhere else, and its tables only when it runs.
            bool gpu = GpuDriven && InitGpu();
            if (!gpu) EnsureLegacy();
            Debug.Log($"[MeshForest] {inst.Length} plants in {chunks.Length} chunks, {variants.Length} variants; " +
                      $"LOD {lod0To}/{lod1To}/{meshRange} m, impostors to {farRange} m; F8 = A/B; " +
                      (gpu ? GpuDescribe() : "CPU lists (RenderMeshInstanced)") + ".");
        }

        /// <summary>A plant's matrix under the forest's root, as the builder placed it.</summary>
        Matrix4x4 LocalOf(int i)
        {
            var p = inst[i];
            float s = instScaleVar[i].x;
            var tl = instTilt != null && i < instTilt.Length ? instTilt[i] : Vector2.zero;
            return Matrix4x4.TRS(new Vector3(p.x, p.y, p.z),
                Quaternion.Euler(tl.x, 0f, tl.y) * Quaternion.Euler(0f, p.w, 0f), new Vector3(s, s, s))
                * variants[(int)instScaleVar[i].y].pre;
        }

        /// <summary>The CPU path's tables (a matrix a plant, twice): made the
        /// first time that path draws.</summary>
        void EnsureLegacy()
        {
            if (localM != null) return;
            localM = new Matrix4x4[inst.Length];
            for (int i = 0; i < inst.Length; i++) localM[i] = LocalOf(i);
            int nb = variants.Length * L;
            bucket = new Matrix4x4[nb][];
            bucketN = new int[nb];
            for (int i = 0; i < nb; i++) bucket[i] = new Matrix4x4[256];
            mirrorM = new Matrix4x4[variants.Length][];
            mirrorN = new int[variants.Length];
            for (int v = 0; v < variants.Length; v++) mirrorM[v] = new Matrix4x4[256];
            Bake(transform.localToWorldMatrix);
        }

        /// <summary>World matrices and the far chunks' impostor lists under
        /// this root (again only if the root ever moves).</summary>
        void Bake(Matrix4x4 root)
        {
            bakedRoot = root;
            worldM = new Matrix4x4[localM.Length];
            worldP = new Vector3[localM.Length];
            for (int i = 0; i < localM.Length; i++)
            {
                worldM[i] = root * localM[i];
                worldP[i] = new Vector3(worldM[i].m03, worldM[i].m13, worldM[i].m23);
            }
            farLists = new Matrix4x4[chunks.Length][][];
            var tmp = new System.Collections.Generic.List<Matrix4x4>[variants.Length];
            for (int v = 0; v < variants.Length; v++) tmp[v] = new System.Collections.Generic.List<Matrix4x4>();
            for (int ci = 0; ci < chunks.Length; ci++)
            {
                var c = chunks[ci];
                foreach (var t in tmp) t.Clear();
                for (int i = c.start, e = c.start + c.count; i < e; i++)
                {
                    int vi = (int)instScaleVar[i].y;
                    var vr = variants[vi];
                    if (vr.lods.Length < L || vr.lods[3] == null || vr.maxDist > 0f) continue;
                    tmp[vi].Add(worldM[i] * impFix[vi]);
                }
                farLists[ci] = new Matrix4x4[variants.Length][];
                for (int v = 0; v < variants.Length; v++)
                    farLists[ci][v] = tmp[v].Count > 0 ? tmp[v].ToArray() : null;
            }
        }

        /// <summary>Edit-mode shots (ForestShots): Update never runs there, so
        /// bucket and queue the plants for this one camera now, before its
        /// Render.</summary>
        public void DrawNow(Camera cam, bool viewFrustum = false)
        {
            if (!inited) Init();
            int dress = SeasonDress.AppliedDress;
            DressMaterials(dress);
            lastDress = dress;
            // (a shot's camera takes its aspect from the texture it is about
            // to be drawn into: the chunk pass's wide cone for the plants too,
            // unless the caller has set the camera's aspect and asks for the
            // player's own cull - viewFrustum)
            if (UseGpu) { GpuDressChanged(); GpuFrame(cam, !viewFrustum); return; }
            EnsureLegacy();
            cardsKnown = false;
            Rebucket(cam, transform.localToWorldMatrix, cam.transform.position);
            Draw(cam);
        }

        /// <summary>Edit-mode shots: the old cards everywhere.</summary>
        public void CardsNow()
        {
            foreach (var c in chunks) SetCards(c, true);
            cardsKnown = false;
        }

        void OnEnable() => RenderPipelineManager.beginCameraRendering += OtherCamera;
        void OnDisable() => RenderPipelineManager.beginCameraRendering -= OtherCamera;

        void OtherCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (!MeshTreesOn || cam == null || mainCam == null) return;
            if (cam == mainCam) { if (StatsOn && UseGpu) StatsView(cam); return; }
            if (cam.cameraType != CameraType.Game || (cam.cullingMask & (1 << gameObject.layer)) == 0) return;
            long pc = PerfClock.On ? PerfClock.Now() : 0;
            if (UseGpu) GpuOtherCamera(cam);
            else OtherCameraLegacy(cam);
            if (PerfClock.On) PerfClock.Add(PerfClock.Slot.ForestOther, pc);
        }

        void OtherCameraLegacy(Camera cam)
        {
            if (mirrorM == null) return;
            for (int v = 0; v < variants.Length; v++)
            {
                int n = mirrorN[v];
                var mat = impNow != null ? impNow[v] : null;
                if (n == 0 || mat == null) continue;
                var mesh = variants[v].lods[3];
                var rp = new RenderParams(mat)
                {
                    shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true,
                    layer = gameObject.layer, camera = cam,
                };
                for (int s = 0; s < mesh.subMeshCount; s++)
                    for (int at = 0; at < n; at += 1023)
                        Graphics.RenderMeshInstanced(rp, mesh, s, mirrorM[v], Mathf.Min(1023, n - at), at);
            }
        }

        void OnDestroy()
        {
            ReleaseGpu();
            if (chunks != null)
                foreach (var c in chunks)
                    if (c.cards != null)
                        foreach (var r in c.cards) if (r != null) SunShadows.ShadowStandIns.Remove(r);
            if (instance == this) instance = null;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (instance == this && kb != null && kb.f8Key.wasPressedThisFrame)
            {
                MeshTreesOn = !MeshTreesOn;
                Debug.Log("[MeshForest] " + (MeshTreesOn ? "MESH forest" : "CARD trees (old)"));
            }

            if (UseGpu) return;     // that path draws from LateUpdate, once the camera has moved
            var cam = FrameStart();
            if (cam == null) return;
            EnsureLegacy();
            if (cardsKnown) { cardsKnown = false; lastOn = false; }   // the GPU path was drawing: bucket afresh

            var root = transform.localToWorldMatrix;
            Vector3 camPos = cam.transform.position;
            bool moved = (camPos - lastCam).sqrMagnitude > 1f || root != lastRoot || !lastOn;
            // The view turns faster than the car moves: re-cull every 2nd
            // frame even standing still.
            long pc = PerfClock.On ? PerfClock.Now() : 0;
            if (moved || (Time.frameCount & 1) == 0) Rebucket(cam, root, camPos);
            if (PerfClock.On) PerfClock.Add(PerfClock.Slot.ForestCull, pc);
            if (moved) { lastCam = camPos; lastRoot = root; }
            lastOn = true;
            mainCam = cam;
            pc = PerfClock.On ? PerfClock.Now() : 0;
            Draw(cam);
            if (PerfClock.On) PerfClock.Add(PerfClock.Slot.ForestDraw, pc);
        }

        void LateUpdate()
        {
            if (!UseGpu) return;
            var cam = FrameStart();
            if (cam == null) return;
            GpuFrame(cam);
            lastOn = true;
            mainCam = cam;
        }

        /// <summary>The frame's camera and dress; null when nothing is to be
        /// drawn (no camera, or the A/B switch shows the old cards).</summary>
        Camera FrameStart()
        {
            var cam = Camera.main;
            if (cam == null || variants == null || variants.Length == 0) return null;
            if (!MeshTreesOn)
            {
                if (lastOn) { foreach (var c in chunks) SetCards(c, true); cardsKnown = false; }
                lastOn = false;
                return null;
            }
            int dress = SeasonDress.AppliedDress;
            if (dress != lastDress) { DressMaterials(dress); lastDress = dress; GpuDressChanged(); }
            return cam;
        }

        static void SetCards(Chunk c, bool on)
        {
            if (c.cards == null) return;
            foreach (var r in c.cards) if (r != null && r.enabled != on) r.enabled = on;
        }

        void DressMaterials(int dress)
        {
            int di = dress >= 0 && dress < 5 ? dress : 2;
            for (int v = 0; v < variants.Length; v++)
            {
                var vr = variants[v];
                var own = vr.leafDress;
                Material leaf = own != null && own.Length == 5 ? own[di]
                              : di < leafByDress.Length ? leafByDress[di] : null;
                var m = new Material[vr.mats.Length];
                for (int s = 0; s < m.Length; s++)
                    m[s] = vr.leafSub[s] ? leaf : vr.mats[s];
                matsNow[v] = m;
                impNow[v] = vr.impostorDress != null && vr.impostorDress.Length == 5 ? vr.impostorDress[di] : null;
            }
        }

        Vector3 bucketCam;   // the eye the lists were last made for

        void Rebucket(Camera cam, Matrix4x4 root, Vector3 camPos)
        {
            if (root != bakedRoot) Bake(root);
            bucketCam = camPos;
            // A WIDER cone than the camera's: a chunk swaps its cards for meshes
            // before it reaches the screen's edge, not on it (owner 2026-11-08:
            // "a lot of foliage and tree pop-in even at extremely low speeds").
            var wide = Matrix4x4.Perspective(Mathf.Min(cam.fieldOfView * 1.5f + 10f, 170f), cam.aspect * 1.3f,
                                             cam.nearClipPlane, cam.farClipPlane);
            GeometryUtility.CalculateFrustumPlanes(wide * cam.worldToCameraMatrix, planes);
            for (int i = 0; i < bucketN.Length; i++) bucketN[i] = 0;
            for (int v = 0; v < mirrorN.Length; v++) mirrorN[v] = 0;
            if (lastPut == null || lastPut.Length != variants.Length) lastPut = new int[variants.Length];
            for (int v = 0; v < lastPut.Length; v++) lastPut[v] = -1;
            farVisible.Clear();
            float far = Mathf.Max(farRange, meshRange);
            float r2 = meshRange * meshRange, l0 = lod0To * lod0To, l1 = lod1To * lod1To;
            float scale = root.lossyScale.x;
            for (int ci = 0; ci < chunks.Length; ci++)
            {
                var c = chunks[ci];
                Vector3 wc = root.MultiplyPoint3x4(c.centre);
                float rad = c.radius * scale;
                float dist = Vector3.Distance(wc, camPos);
                bool draw = dist - rad < far &&
                            GeometryUtility.TestPlanesAABB(planes, new Bounds(wc, Vector3.one * rad * 2f));
                SetCards(c, !draw);
                if (!draw) continue;
                if (dist - rad >= meshRange) { farVisible.Add(ci); continue; }
                for (int i = c.start, e = c.start + c.count; i < e; i++)
                {
                    int vi = (int)instScaleVar[i].y;
                    float d2 = (worldP[i] - camPos).sqrMagnitude;
                    if (d2 > maxD2[vi]) continue;
                    int lod = d2 < l0 ? 0 : d2 < l1 ? 1 : d2 < r2 ? 2 : 3;
                    Put(vi, lod, i);
                    // Inside a hand-over band - and Margin either side of it, so
                    // a bucket a metre or two stale never leaves a tree with
                    // neither level drawn: the neighbouring level too. The
                    // shader's fade (by the tree's own distance) decides what
                    // of each shows; out of its band it draws nothing.
                    float d = Mathf.Sqrt(d2);
                    if (lod < 3 && handOver[vi][lod] && d > fadeOf[vi][lod].z - Margin) Put(vi, lod + 1, i);
                    if (lod > 0 && handOver[vi][lod - 1] && d < fadeOf[vi][lod - 1].w + Margin) Put(vi, lod - 1, i);
                }
            }
        }

        const float Margin = 4f;
        int[] lastPut;   // per species: the instance last put in the other cameras' list (a hand-over puts one twice)

        void Put(int vi, int lod, int i)
        {
            int b = vi * L + lod;
            if (bucketN[b] == bucket[b].Length)
                System.Array.Resize(ref bucket[b], bucket[b].Length * 2);
            var m = lod == 3 ? worldM[i] * impFix[vi] : worldM[i];
            bucket[b][bucketN[b]++] = m;
            // the other cameras: a canopy tree once, as its impostor
            var vr = variants[vi];
            if (vr.lods.Length > 3 && vr.lods[3] != null && vr.maxDist <= 0f && lastPut[vi] != i)
            {
                if (mirrorN[vi] == mirrorM[vi].Length) System.Array.Resize(ref mirrorM[vi], mirrorM[vi].Length * 2);
                mirrorM[vi][mirrorN[vi]++] = lod == 3 ? m : m * impFix[vi];
                lastPut[vi] = i;
            }
        }

        /// <summary>This material with a level's fade band (a copy, made once).</summary>
        Material Faded(Material m, Vector4 f, float thin = 0f)
        {
            if (m == null) return null;
            if (f == Vector4.zero) return m;
            if (faded.TryGetValue((m, f, thin), out var c) && c != null) return c;
            c = new Material(m) { name = m.name + "_fade", enableInstancing = true };
            c.SetVector("_PSXLodFade", f);
            c.SetVector("_PSXLodThin", new Vector4(thin, 0f, 0f, 0f));
            c.EnableKeyword("PSX_LODFADE");
            faded[(m, f, thin)] = c;
            return c;
        }

        /// <summary>The thinning for a level: only where that level's fade-out
        /// is the plant's draw limit, not a hand-over to the next level.</summary>
        float ThinFor(int v, int lod) =>
            !handOver[v][lod] && fadeOf[v][lod].w > 0f ? variants[v].thin : 0f;

        void Draw(Camera only = null)
        {
            for (int v = 0; v < variants.Length; v++)
            {
                var vr = variants[v];
                var mats = matsNow[v];
                for (int lod = 0; lod < L; lod++)
                {
                    int b = v * L + lod, n = bucketN[b];
                    if (n == 0 || lod >= vr.lods.Length) continue;
                    var mesh = vr.lods[lod];
                    if (mesh == null) continue;
                    bool imp = lod == 3;
                    int subs = imp ? mesh.subMeshCount : Mathf.Min(mesh.subMeshCount, mats.Length);
                    for (int s = 0; s < subs; s++)
                    {
                        var mat = Faded(imp ? impNow[v] : mats[s], fadeOf[v][lod], ThinFor(v, lod));
                        if (mat == null) continue;   // bare: no leaves
                        var rp = new RenderParams(mat)
                        {
                            shadowCastingMode = ShadowCastingMode.Off,   // the sun's shadow: SunShadows' card stand-ins
                            receiveShadows = true,
                            layer = gameObject.layer,
                            camera = only,
                        };
                        for (int at = 0; at < n; at += 1023)
                            Graphics.RenderMeshInstanced(rp, mesh, s, bucket[b], Mathf.Min(1023, n - at), at);
                        if (StatsOn) { long st = (long)(mesh.GetIndexCount(s) / 3) * n; statTris += st; StatAdd(v, lod, st); statDraws += (n + 1022) / 1023; if (s == 0) statPlants += n; }
                    }
                }
            }
            // The far chunks: each one's impostor lists, as baked.
            foreach (int ci in farVisible)
            {
                var lists = farLists[ci];
                for (int v = 0; v < variants.Length; v++)
                {
                    var arr = lists[v];
                    var mat = Faded(impNow[v], fadeOf[v][3]);
                    if (arr == null || mat == null) continue;
                    var mesh = variants[v].lods[3];
                    var rp = new RenderParams(mat)
                    {
                        shadowCastingMode = ShadowCastingMode.Off,
                        receiveShadows = true,
                        layer = gameObject.layer,
                        camera = only,
                    };
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        for (int at = 0; at < arr.Length; at += 1023)
                            Graphics.RenderMeshInstanced(rp, mesh, s, arr, Mathf.Min(1023, arr.Length - at), at);
                        if (StatsOn) { long st = (long)(mesh.GetIndexCount(s) / 3) * arr.Length; statTris += st; StatAdd(v, 3, st); statDraws += (arr.Length + 1022) / 1023; if (s == 0) statPlants += arr.Length; }
                    }
                }
            }
            if (StatsOn) StatsFrame();
        }
    }
}
