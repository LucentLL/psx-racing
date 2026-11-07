using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;

namespace PSXRacing
{
    /// <summary>
    /// Real mesh broadleaf trees on the mountain stages (owner 2026-11-07:
    /// "overhauling the Mountain Tracks (tree models ... first) ... Focus on
    /// performance with LOD (low, mid high)"), from the owner's NatureManufacture
    /// beech pack.
    ///
    /// The stage builder still plants every tree where it always did, and the
    /// trunk table (TreeTrunks) still stops the car at each one. What changed:
    /// the broadleaf trees' crossed cards live in their own chunk meshes
    /// (Forest_B_*), and this component holds the same trees as an instance
    /// table per chunk. Per chunk, per frame:
    ///   - farther than <see cref="MeshRange"/> from the camera, or outside its
    ///     view: the card chunk draws, nothing else (mirrors and reflection
    ///     views looking back still see the cards);
    ///   - otherwise the cards are hidden and every tree is drawn as a mesh,
    ///     GPU-instanced, at HIGH (LOD0) / MID (LOD1) / LOW (LOD2) by its own
    ///     distance. No GameObject per tree, no LODGroup: ~20k trees a stage.
    /// Buckets are rebuilt only when the camera has moved a few metres.
    ///
    /// Leaves follow the season: green in spring and summer, the pack's autumn
    /// atlas in fall, none in winter and snow (bare beech).
    ///
    /// F8 flips between mesh trees and the old cards everywhere, for the A/B.
    /// </summary>
    public class MeshForest : MonoBehaviour
    {
        [System.Serializable]
        public class Variant
        {
            public Mesh[] lods = new Mesh[3];      // HIGH, MID, LOW
            public Material[] mats;                // per submesh (bark ... leaves)
            public bool[] leafSub;                 // per submesh: the leaf atlas
            public float height = 1f;              // model's own height (m)
            /// <summary>The LOD node's transform inside its FBX (unit scale,
            /// axis conversion): applied before each tree's own placement.</summary>
            public Matrix4x4 pre = Matrix4x4.identity;
            /// <summary>This variant's leaves per dress (5), overriding
            /// <see cref="leafByDress"/>: the fall tints, the bushes' own leaf.</summary>
            public Material[] leafDress;
            /// <summary>Drawn no farther than this (m); 0 = meshRange. The
            /// understory stops at ~110 m.</summary>
            public float maxDist;
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
        /// <summary>Leaf material per season dress (Winter, Spring, Summer,
        /// Fall, Snow); null = bare.</summary>
        public Material[] leafByDress = new Material[5];

        public float lod0To = 45f, lod1To = 100f, meshRange = 190f;

        public static bool MeshTreesOn = true;
        public static float MeshRange => instance != null ? instance.meshRange : 0f;
        static MeshForest instance;

        Matrix4x4[] localM;
        // buckets[variant * 3 + lod] -> matrices
        Matrix4x4[][] bucket;
        int[] bucketN;
        Vector3 lastCam = new Vector3(1e9f, 0f, 0f);
        Matrix4x4 lastRoot;
        int lastDress = -2;
        bool lastOn = true;
        readonly Plane[] planes = new Plane[6];
        Material[][] matsNow;
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
            int nb = variants.Length * 3;
            bucket = new Matrix4x4[nb][];
            bucketN = new int[nb];
            for (int i = 0; i < nb; i++) bucket[i] = new Matrix4x4[256];
            matsNow = new Material[variants.Length][];
            maxD2 = new float[variants.Length];
            for (int v = 0; v < variants.Length; v++)
            {
                float m = variants[v].maxDist > 0f ? Mathf.Min(variants[v].maxDist, meshRange) : meshRange;
                maxD2[v] = m * m;
            }
            Debug.Log($"[MeshForest] {inst.Length} mesh trees in {chunks.Length} chunks, " +
                      $"{variants.Length} variants; LOD {lod0To}/{lod1To}/{meshRange} m; F8 = A/B.");
        }

        /// <summary>Edit-mode shots (ForestShots): Update never runs there, so
        /// bucket and queue the trees for this one camera now, before its
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
            if (kb != null && kb.f8Key.wasPressedThisFrame)
            {
                MeshTreesOn = !MeshTreesOn;
                Debug.Log("[MeshForest] " + (MeshTreesOn ? "MESH trees" : "CARD trees (old)"));
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
                var own = variants[v].leafDress;
                Material leaf = own != null && own.Length == 5 ? own[di]
                              : di < leafByDress.Length ? leafByDress[di] : null;
                var src = variants[v].mats;
                var m = new Material[src.Length];
                for (int s = 0; s < src.Length; s++)
                    m[s] = variants[v].leafSub[s] ? leaf : src[s];
                matsNow[v] = m;
            }
        }

        void Rebucket(Camera cam, Matrix4x4 root, Vector3 camPos)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            for (int i = 0; i < bucketN.Length; i++) bucketN[i] = 0;
            float r2 = meshRange * meshRange, l0 = lod0To * lod0To, l1 = lod1To * lod1To;
            float scale = root.lossyScale.x;
            for (int ci = 0; ci < chunks.Length; ci++)
            {
                var c = chunks[ci];
                Vector3 wc = root.MultiplyPoint3x4(c.centre);
                float rad = c.radius * scale;
                float dc = Vector3.Distance(wc, camPos) - rad;
                bool mesh = dc < meshRange &&
                            GeometryUtility.TestPlanesAABB(planes, new Bounds(wc, Vector3.one * rad * 2f));
                SetCards(c, !mesh);
                if (!mesh) continue;
                for (int i = c.start, e = c.start + c.count; i < e; i++)
                {
                    var m = root * localM[i];
                    Vector3 p = new Vector3(m.m03, m.m13, m.m23);
                    float d2 = (p - camPos).sqrMagnitude;
                    int vi = (int)instScaleVar[i].y;
                    if (d2 > r2 || d2 > maxD2[vi]) continue;
                    int lod = d2 < l0 ? 0 : d2 < l1 ? 1 : 2;
                    int b = vi * 3 + lod;
                    if (bucketN[b] == bucket[b].Length)
                        System.Array.Resize(ref bucket[b], bucket[b].Length * 2);
                    bucket[b][bucketN[b]++] = m;
                }
            }
        }

        void Draw(Camera only = null)
        {
            for (int v = 0; v < variants.Length; v++)
            {
                var vr = variants[v];
                var mats = matsNow[v];
                for (int lod = 0; lod < 3; lod++)
                {
                    int b = v * 3 + lod, n = bucketN[b];
                    var mesh = vr.lods[lod];
                    if (n == 0 || mesh == null) continue;
                    int subs = Mathf.Min(mesh.subMeshCount, mats.Length);
                    for (int s = 0; s < subs; s++)
                    {
                        if (mats[s] == null) continue;   // bare: no leaves
                        var rp = new RenderParams(mats[s])
                        {
                            shadowCastingMode = lod < 2 ? ShadowCastingMode.On : ShadowCastingMode.Off,
                            receiveShadows = true,
                            layer = gameObject.layer,
                            camera = only,
                        };
                        for (int at = 0; at < n; at += 1023)
                            Graphics.RenderMeshInstanced(rp, mesh, s, bucket[b], Mathf.Min(1023, n - at), at);
                    }
                }
            }
        }
    }
}
