using Unity.Collections;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE MESH FOREST, STREAMED (Charlotte's mesh trees, 2026-11-11; owner:
    /// "Why are you still using old tree cards when I've specifically asked to
    /// use Mountain/Meadow dynamic trees"). A mountain stage's forest is made
    /// once with its scene; a city's trees arrive and leave with its 256 m
    /// tiles. The same component, the same GPU cull and draws, with the
    /// instance buffers cut into fixed SLOTS - one a live tile - that a tile
    /// fills when its trees stand up and gives back when it is dropped:
    ///   * a slot is a chunk (its sphere, its cards: the tile's old card mesh,
    ///     drawn where the chunk is not - mirrors' far side, past the far
    ///     range - and still the sun shadow's caster, as on the stages);
    ///   * its plants' matrices, impostor matrices and species are written into
    ///     the slot's range of the buffers (one SetData each);
    ///   * an empty slot's plants point at the never-drawn chunk.
    /// Every list can hold the whole pool (any slot may hold any species).
    /// The CPU path does not exist here: no compute, no mesh trees (the tiles
    /// keep their cards).
    /// </summary>
    public partial class MeshForest
    {
        /// <summary>One plant of a slot: foot (world), yaw (deg), scale, species.</summary>
        public struct PoolPlant
        {
            public Vector3 pos;
            public float yaw, scale;
            public int variant;
        }

        int poolPer;
        bool[] poolUsed;
        static readonly Vector3 PoolNowhere = new Vector3(1e7f, -1e4f, 1e7f);

        /// <summary>A pooled forest of <paramref name="slots"/> x <paramref name="per"/>
        /// plants under <paramref name="parent"/> (identity root). Null when the
        /// machine cannot run the GPU path (the tiles then keep their cards).</summary>
        public static MeshForest CreatePool(Transform parent, Variant[] variants, int slots, int per,
                                            float lod0, float lod1, float mesh, float far, System.Func<int> dress, ComputeShader cull)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsInstancing || variants == null || variants.Length == 0) return null;
            var go = new GameObject("CityMeshForest") { hideFlags = HideFlags.DontSave };
            go.SetActive(false);
            if (parent != null) go.transform.SetParent(parent, false);
            go.layer = 10;   // (CityTrees.FoliageLayer: what the mirrors and probes cull by)
            var f = go.AddComponent<MeshForest>();
            f.pooled = true;
            f.cullShader = cull;
            f.dressSource = dress;
            f.variants = variants;
            f.lod0To = lod0; f.lod1To = lod1; f.meshRange = mesh; f.farRange = far;
            int n = slots * per;
            f.poolPer = per;
            f.poolUsed = new bool[slots];
            f.inst = new Vector4[n];
            f.instScaleVar = new Vector2[n];
            for (int i = 0; i < n; i++) { f.inst[i] = new Vector4(PoolNowhere.x, PoolNowhere.y, PoolNowhere.z, 0f); f.instScaleVar[i] = new Vector2(1f, 0f); }
            f.chunks = new Chunk[slots];
            for (int s = 0; s < slots; s++) f.chunks[s] = new Chunk { centre = PoolNowhere, radius = 0f, start = s * per, count = per };
            go.SetActive(true);    // Awake: Init and the GPU buffers
            if (!f.inited) f.Init();   // (edit mode: no Awake)
            if (!f.gpuReady) { Debug.LogWarning("[MeshForest] streamed forest: no GPU path on this machine - the city keeps its card trees."); Object.Destroy(go); return null; }
            return f;
        }

        public bool PoolReady => pooled && gpuReady;
        public int PoolSlots => chunks != null ? chunks.Length : 0;
        public int PoolPerSlot => poolPer;
        public int PoolUsed { get { int u = 0; if (poolUsed != null) foreach (var b in poolUsed) if (b) u++; return u; } }

        /// <summary>The chunks' spheres (the root is the identity: the city's
        /// world frame).</summary>
        void PoolBake(Matrix4x4 root)
        {
            float scale = root.lossyScale.x;
            for (int ci = 0; ci < chunks.Length; ci++)
            {
                chunkWorld[ci] = root.MultiplyPoint3x4(chunks[ci].centre);
                chunkRad[ci] = chunks[ci].radius * scale;
            }
        }

        /// <summary>Put a tile's plants in a free slot: -1 when the pool is full
        /// (the caller grows it) or not ready. Plants past the slot's size are
        /// left out (counted in the return of <paramref name="kept"/>).</summary>
        public int PoolFill(Vector3 centre, float radius, Renderer[] cards, System.Collections.Generic.List<PoolPlant> plants, out int kept)
        {
            kept = 0;
            if (!PoolReady) return -1;
            int slot = -1;
            for (int s = 0; s < poolUsed.Length; s++) if (!poolUsed[s]) { slot = s; break; }
            if (slot < 0) return -1;
            int n = inst.Length, at = slot * poolPer;
            kept = Mathf.Min(plants.Count, poolPer);
            var rows = new NativeArray<Vector4>(poolPer * 3, Allocator.Temp, NativeArrayOptions.ClearMemory);
            var irows = new NativeArray<Vector4>(poolPer * 3, Allocator.Temp, NativeArrayOptions.ClearMemory);
            var meta = new NativeArray<MetaInfo>(poolPer, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            ulong lo = 0, hi = 0;
            for (int k = 0; k < poolPer; k++)
            {
                int i = at + k;
                if (k >= kept)
                {
                    inst[i] = new Vector4(PoolNowhere.x, PoolNowhere.y, PoolNowhere.z, 0f);
                    instScaleVar[i] = new Vector2(1f, 0f);
                    meta[k] = new MetaInfo { packed = (uint)chunks.Length << 10, imp = (uint)(n + i) };
                    continue;
                }
                var p = plants[k];
                int v = Mathf.Clamp(p.variant, 0, variants.Length - 1);
                inst[i] = new Vector4(p.pos.x, p.pos.y, p.pos.z, p.yaw);
                instScaleVar[i] = new Vector2(p.scale, v);
                var m = LocalOf(i);   // (the root is the identity)
                rows[k * 3] = new Vector4(m.m00, m.m01, m.m02, m.m03);
                rows[k * 3 + 1] = new Vector4(m.m10, m.m11, m.m12, m.m13);
                rows[k * 3 + 2] = new Vector4(m.m20, m.m21, m.m22, m.m23);
                var mi = m * impFix[v];
                irows[k * 3] = new Vector4(mi.m00, mi.m01, mi.m02, mi.m03);
                irows[k * 3 + 1] = new Vector4(mi.m10, mi.m11, mi.m12, mi.m13);
                irows[k * 3 + 2] = new Vector4(mi.m20, mi.m21, mi.m22, mi.m23);
                meta[k] = new MetaInfo { packed = (uint)v | ((uint)slot << 10), imp = (uint)(n + i) };
                if (v < 64) lo |= 1UL << v; else if (v < 128) hi |= 1UL << (v - 64);
            }
            matBuf.SetData(rows, 0, at * 3, poolPer * 3);
            matBuf.SetData(irows, 0, (n + at) * 3, poolPer * 3);
            metaBuf.SetData(meta, 0, at, poolPer);
            rows.Dispose(); irows.Dispose(); meta.Dispose();
            chunkMaskLo[slot] = lo; chunkMaskHi[slot] = hi;
            var c = chunks[slot];
            c.centre = centre; c.radius = radius; c.cards = cards;
            chunkWorld[slot] = gpuRoot.MultiplyPoint3x4(centre);
            chunkRad[slot] = radius * gpuRoot.lossyScale.x;
            if (cards != null) foreach (var r in cards) if (r != null) SunShadows.ShadowStandIns.Add(r);
            poolUsed[slot] = true;
            cardsKnown = false;
            return slot;
        }

        /// <summary>Give a slot back: its plants point at no chunk, its cards
        /// are the tile's again (if it still stands).</summary>
        public void PoolRelease(int slot)
        {
            if (!PoolReady || slot < 0 || slot >= poolUsed.Length || !poolUsed[slot]) return;
            int at = slot * poolPer, n = inst.Length;
            var meta = new NativeArray<MetaInfo>(poolPer, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int k = 0; k < poolPer; k++) meta[k] = new MetaInfo { packed = (uint)chunks.Length << 10, imp = (uint)(n + at + k) };
            metaBuf.SetData(meta, 0, at, poolPer);
            meta.Dispose();
            var c = chunks[slot];
            if (c.cards != null)
                foreach (var r in c.cards)
                    if (r != null) { SunShadows.ShadowStandIns.Remove(r); r.enabled = true; }
            c.cards = null; c.centre = PoolNowhere; c.radius = 0f;
            chunkWorld[slot] = PoolNowhere; chunkRad[slot] = 0f;
            chunkMaskLo[slot] = 0; chunkMaskHi[slot] = 0;
            poolUsed[slot] = false;
            cardsKnown = false;
        }

        /// <summary>The slot's cards were destroyed with their tile.</summary>
        public bool PoolCardsGone(int slot)
        {
            if (slot < 0 || slot >= chunks.Length) return true;
            var cs = chunks[slot].cards;
            if (cs == null || cs.Length == 0) return false;
            foreach (var r in cs) if (r != null) return false;
            return true;
        }

        /// <summary>Plants in the used slots.</summary>
        public int PoolPlants()
        {
            int t = 0;
            if (poolUsed == null) return 0;
            for (int s = 0; s < poolUsed.Length; s++)
            {
                if (!poolUsed[s]) continue;
                for (int k = 0, at = s * poolPer; k < poolPer; k++)
                    if (inst[at + k].y > PoolNowhere.y + 1f) t++;
            }
            return t;
        }
    }
}
