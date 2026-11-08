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
    public class MeshForest : MonoBehaviour
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
        public Material[] leafByDress = new Material[5];

        public float lod0To = 45f, lod1To = 100f, meshRange = 190f, farRange = 2200f;

        public static bool MeshTreesOn = true;
        static MeshForest instance;

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

        void Awake() => Init();

        void Init()
        {
            instance = this;
            localM = new Matrix4x4[inst.Length];
            for (int i = 0; i < inst.Length; i++)
            {
                var p = inst[i];
                float s = instScaleVar[i].x;
                localM[i] = Matrix4x4.TRS(new Vector3(p.x, p.y, p.z),
                    Quaternion.Euler(0f, p.w, 0f), new Vector3(s, s, s))
                    * variants[(int)instScaleVar[i].y].pre;
            }
            int nb = variants.Length * L;
            bucket = new Matrix4x4[nb][];
            bucketN = new int[nb];
            for (int i = 0; i < nb; i++) bucket[i] = new Matrix4x4[256];
            matsNow = new Material[variants.Length][];
            impNow = new Material[variants.Length];
            maxD2 = new float[variants.Length];
            impFix = new Matrix4x4[variants.Length];
            float far = Mathf.Max(farRange, meshRange);
            for (int v = 0; v < variants.Length; v++)
            {
                var vr = variants[v];
                float m = vr.maxDist > 0f ? Mathf.Min(vr.maxDist, far) : far;
                if (vr.lods.Length < L || vr.lods[3] == null) m = Mathf.Min(m, meshRange);
                maxD2[v] = m * m;
                impFix[v] = vr.pre.inverse * vr.impostorPre;
            }
            Bake(transform.localToWorldMatrix);
            Debug.Log($"[MeshForest] {inst.Length} plants in {chunks.Length} chunks, {variants.Length} variants; " +
                      $"LOD {lod0To}/{lod1To}/{meshRange} m, impostors to {farRange} m; F8 = A/B.");
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
        public void DrawNow(Camera cam)
        {
            if (localM == null) Init();
            int dress = SeasonDress.AppliedDress;
            DressMaterials(dress);
            lastDress = dress;
            Rebucket(cam, transform.localToWorldMatrix, cam.transform.position);
            Draw(cam);
        }

        /// <summary>Edit-mode shots: the old cards everywhere.</summary>
        public void CardsNow()
        {
            foreach (var c in chunks) SetCards(c, true);
        }

        void OnDestroy()
        {
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

            var cam = Camera.main;
            if (cam == null || variants == null || variants.Length == 0) return;

            if (!MeshTreesOn)
            {
                if (lastOn) foreach (var c in chunks) SetCards(c, true);
                lastOn = false;
                return;
            }

            int dress = SeasonDress.AppliedDress;
            if (dress != lastDress) { DressMaterials(dress); lastDress = dress; }

            var root = transform.localToWorldMatrix;
            Vector3 camPos = cam.transform.position;
            bool moved = (camPos - lastCam).sqrMagnitude > 9f || root != lastRoot || !lastOn;
            // The view turns faster than the car moves: re-cull every 4th
            // frame even standing still.
            if (moved || (Time.frameCount & 3) == 0) Rebucket(cam, root, camPos);
            if (moved) { lastCam = camPos; lastRoot = root; }
            lastOn = true;
            Draw();
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

        void Rebucket(Camera cam, Matrix4x4 root, Vector3 camPos)
        {
            if (root != bakedRoot) Bake(root);
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            for (int i = 0; i < bucketN.Length; i++) bucketN[i] = 0;
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
                    int b = vi * L + lod;
                    if (bucketN[b] == bucket[b].Length)
                        System.Array.Resize(ref bucket[b], bucket[b].Length * 2);
                    bucket[b][bucketN[b]++] = lod == 3 ? worldM[i] * impFix[vi] : worldM[i];
                }
            }
        }

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
                        var mat = imp ? impNow[v] : mats[s];
                        if (mat == null) continue;   // bare: no leaves
                        var rp = new RenderParams(mat)
                        {
                            shadowCastingMode = vr.shadows && lod < 2 ? ShadowCastingMode.On : ShadowCastingMode.Off,
                            receiveShadows = true,
                            layer = gameObject.layer,
                            camera = only,
                        };
                        for (int at = 0; at < n; at += 1023)
                            Graphics.RenderMeshInstanced(rp, mesh, s, bucket[b], Mathf.Min(1023, n - at), at);
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
                    var mat = impNow[v];
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
                        for (int at = 0; at < arr.Length; at += 1023)
                            Graphics.RenderMeshInstanced(rp, mesh, s, arr, Mathf.Min(1023, arr.Length - at), at);
                }
            }
        }
    }
}
