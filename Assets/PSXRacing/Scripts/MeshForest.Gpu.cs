using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing
{
    /// <summary>
    /// THE MESH FOREST, GPU-DRIVEN (2026-11-09; owner: "60FPS minimum, 144FPS
    /// target ... Minimal pop-in (preferably none)"). Measured in the Windows
    /// player on NC 226A's lower half, winter dusk, with no view drawn at all:
    /// 10.5 ms of main thread a frame with the mesh forest, 0.5 ms without -
    /// the re-bucket of every plant of every drawn chunk on the main thread
    /// (3.9 ms a pass, every second frame), about a thousand
    /// Graphics.RenderMeshInstanced calls a frame each copying its matrices
    /// (3 ms), and every other camera's cull walking those calls again (the
    /// paint's reflection face: 4.5 ms).
    ///
    /// The same forest - the same plants in the same places at the same
    /// levels, handed over by the same dithered bands - from buffers made once
    /// when the stage loads:
    ///   * every plant's matrix (and every canopy tree's impostor matrix) in
    ///     one GPU buffer, computed exactly as the old path computed them;
    ///   * a compute shader (Shaders/PSXForestCull.compute) that runs the old
    ///     re-bucket's rule for every plant, every frame, against the view as
    ///     this frame draws it - and culls each plant against the frustum
    ///     itself (the old path drew every plant of every chunk a cone half
    ///     as wide again as the view touched);
    ///   * one Graphics.RenderMeshIndirect per species, level and submesh (a
    ///     few hundred at most, no matrices copied), the instance counts
    ///     written by the cull on the GPU;
    ///   * the mirrors and the reflection faces: one draw a canopy species of
    ///     the list the same cull filled for them (the canopy of the drawn
    ///     near chunks as impostors, as before).
    /// What stays on the main thread is the CHUNK pass (a few hundred spheres:
    /// which chunks show their cards and which their plants), three dispatches
    /// and the draw calls.
    ///
    /// The old path stays whole behind <see cref="GpuDriven"/> (a perf tour's
    /// <c>ab=MeshForest.GpuDriven:0</c>, a machine with no compute shaders, a
    /// scene built before the cull existed and not passed through the Windows
    /// build).
    /// </summary>
    public partial class MeshForest
    {
        /// <summary>The cull (Shaders/PSXForestCull.compute): set by the stage
        /// builder's PC scene pass, and by the Windows build for a scene built
        /// before it (Editor/ForestGpuBuild.cs). Null = the CPU path.</summary>
        public ComputeShader cullShader;

        /// <summary>A/B: false = the old per-frame re-bucket and
        /// RenderMeshInstanced.</summary>
        public static bool GpuDriven = true;

        /// <summary>The instance frustum is this much wider than the view's
        /// (tan of the half angles): slack for nothing in particular - the cull
        /// runs after every camera script of the frame.</summary>
        const float FrustumSlack = 1.06f;

        [StructLayout(LayoutKind.Sequential)]
        struct VarInfo
        {
            public Vector4 a, b;
            public int b0, b1, b2, b3;
            public int mirror, flags, pad0, pad1;
            public Vector4 sphere, impSphere;
        }
        [StructLayout(LayoutKind.Sequential)] struct MetaInfo { public uint packed, imp; }
        [StructLayout(LayoutKind.Sequential)] struct BucketInfo { public uint start, cap; }

        struct Cmd
        {
            public Mesh mesh;
            public int sub, variant, lod;      // lod 4: the other cameras' list
            public int bucket;
            public uint indexCount;
        }

        bool gpuReady, gpuTried;
        bool UseGpu => GpuDriven && inited && (gpuReady || (!gpuTried && InitGpu()));

        GraphicsBuffer matBuf, metaBuf, varBuf, chunkBuf, bucketBuf, cmdBucketBuf, countBuf, visBuf, argsBuf;
        Cmd[] cmds;
        int mainCmds;                           // cmds[0..mainCmds) the main view's, the rest the other cameras'
        RenderParams[] cmdRp;
        bool[] cmdOn;                           // has a material in this dress
        MaterialPropertyBlock[] cmdProps;
        int bucketCount, impCount;
        long gpuBytes;
        Matrix4x4 gpuRoot;
        Vector3[] chunkWorld;
        float[] chunkRad;
        uint[] chunkFlag;
        ulong[] chunkMaskLo, chunkMaskHi;       // the species each chunk holds
        bool cardsKnown;                        // chunkFlag says what every chunk's cards show
        readonly ulong[] reachLo = new ulong[L], reachHi = new ulong[L];
        bool reachAll;
        readonly Plane[] instPlanes = new Plane[6];
        readonly Vector4[] planeVec = new Vector4[5];
        readonly Bounds[] lodBounds = new Bounds[L + 1];
        int kClear, kCull, kArgs;
        static readonly int IdCam = Shader.PropertyToID("_ForestCam"), IdPlanes = Shader.PropertyToID("_ForestPlanes"),
                            IdLod = Shader.PropertyToID("_ForestLod"), IdN = Shader.PropertyToID("_ForestN"),
                            IdBuckets = Shader.PropertyToID("_ForestBuckets"), IdCmds = Shader.PropertyToID("_ForestCmds"),
                            IdMat = Shader.PropertyToID("_ForestMat"), IdMeta = Shader.PropertyToID("_ForestMeta"),
                            IdVar = Shader.PropertyToID("_ForestVar"), IdChunk = Shader.PropertyToID("_ForestChunk"),
                            IdBucket = Shader.PropertyToID("_ForestBucket"), IdCmdBucket = Shader.PropertyToID("_ForestCmdBucket"),
                            IdCount = Shader.PropertyToID("_ForestCount"), IdVis = Shader.PropertyToID("_ForestVis"),
                            IdArgs = Shader.PropertyToID("_ForestArgs"), IdBase = Shader.PropertyToID("_ForestBase");

        // ---- what was drawn, for the perf tour (its own count: Unity's
        // triangle counter does not see an indirect draw's instances) ----
        public static bool StatsOn;
        public static double SumTris, SumPlants, SumDraws;
        public static long MaxTris, UnreachedTris;
        public static int StatFrames;
        long statTris, statPlants;
        int statDraws;
        public static void StatsReset() { SumTris = SumPlants = SumDraws = 0; MaxTris = UnreachedTris = 0; StatFrames = 0; }
        void StatsFrame()
        {
            SumTris += statTris; SumPlants += statPlants; SumDraws += statDraws; StatFrames++;
            if (statTris > MaxTris) MaxTris = statTris;
            statTris = statPlants = 0; statDraws = 0;
        }

        string GpuDescribe() =>
            $"GPU lists ({bucketCount} lists, {mainCmds} + {cmds.Length - mainCmds} draw commands, {impCount} impostor matrices, {gpuBytes / 1048576f:0} MB of buffers)";

        /// <summary>The buffers, once. False (and the CPU path) where the
        /// machine or the scene cannot run the cull.</summary>
        bool InitGpu()
        {
            gpuTried = true;
#if UNITY_EDITOR
            if (cullShader == null)
                cullShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PSXRacing/Shaders/PSXForestCull.compute");
#endif
            if (cullShader == null || !SystemInfo.supportsComputeShaders || !SystemInfo.supportsInstancing ||
                variants == null || variants.Length == 0 || variants.Length > 1023 || chunks == null || inst == null || inst.Length == 0)
                return false;
            int n = inst.Length, nv = variants.Length;

            // each plant's chunk; the species a chunk holds; how many of a species the stage holds
            var chunkOf = new int[n];
            for (int i = 0; i < n; i++) chunkOf[i] = chunks.Length;     // (one flag past the last chunk: never drawn)
            var countV = new int[nv];
            chunkMaskLo = new ulong[chunks.Length]; chunkMaskHi = new ulong[chunks.Length];
            reachAll = nv > 128;
            for (int ci = 0; ci < chunks.Length; ci++)
            {
                var c = chunks[ci];
                for (int i = Mathf.Max(0, c.start), e = Mathf.Min(n, c.start + c.count); i < e; i++)
                {
                    chunkOf[i] = ci;
                    int v = (int)instScaleVar[i].y;
                    if (v < 64) chunkMaskLo[ci] |= 1UL << v; else if (v < 128) chunkMaskHi[ci] |= 1UL << (v - 64);
                }
            }
            bool HasLod(int v, int l) => l < variants[v].lods.Length && variants[v].lods[l] != null;
            bool Canopy(int v) => HasLod(v, 3) && variants[v].maxDist <= 0f;
            var impOf = new uint[n];
            impCount = 0;
            for (int i = 0; i < n; i++)
            {
                int v = (int)instScaleVar[i].y;
                countV[v]++;
                impOf[i] = HasLod(v, 3) ? (uint)(n + impCount++) : 0u;
            }

            // the lists: one a species and level that has a mesh, one more a canopy species for the other cameras
            var infos = new VarInfo[nv];
            var buckets = new System.Collections.Generic.List<BucketInfo>();
            var cmdList = new System.Collections.Generic.List<Cmd>();
            var mirrorCmds = new System.Collections.Generic.List<Cmd>();
            uint visN = 0;
            for (int v = 0; v < nv; v++)
            {
                var vr = variants[v];
                var bk = new int[L];
                for (int l = 0; l < L; l++)
                {
                    bk[l] = -1;
                    if (countV[v] == 0 || !HasLod(v, l)) continue;
                    bk[l] = buckets.Count;
                    buckets.Add(new BucketInfo { start = visN, cap = (uint)countV[v] });
                    visN += (uint)countV[v];
                    var mesh = vr.lods[l];
                    int subs = l == 3 ? mesh.subMeshCount : Mathf.Min(mesh.subMeshCount, vr.mats.Length);
                    for (int s = 0; s < subs; s++)
                        cmdList.Add(new Cmd { mesh = mesh, sub = s, variant = v, lod = l, bucket = bk[l], indexCount = mesh.GetIndexCount(s) });
                }
                int mb = -1;
                if (countV[v] > 0 && Canopy(v))
                {
                    mb = buckets.Count;
                    buckets.Add(new BucketInfo { start = visN, cap = (uint)countV[v] });
                    visN += (uint)countV[v];
                    var mesh = vr.lods[3];
                    for (int s = 0; s < mesh.subMeshCount; s++)
                        mirrorCmds.Add(new Cmd { mesh = mesh, sub = s, variant = v, lod = 4, bucket = mb, indexCount = mesh.GetIndexCount(s) });
                }
                int flags = (handOver[v][0] ? 1 : 0) | (handOver[v][1] ? 2 : 0) | (handOver[v][2] ? 4 : 0) | (Canopy(v) ? 8 : 0);
                // the meshes' sphere (object space, round LOD 0's centre) ...
                Vector4 sph = Vector4.zero, isph = Vector4.zero;
                if (HasLod(v, 0))
                {
                    Vector3 c0 = vr.lods[0].bounds.center;
                    float r = 0f;
                    for (int l = 0; l < 3; l++)
                        if (HasLod(v, l)) r = Mathf.Max(r, Vector3.Distance(vr.lods[l].bounds.center, c0) + vr.lods[l].bounds.extents.magnitude);
                    sph = new Vector4(c0.x, c0.y, c0.z, r * 1.02f + 0.05f);
                }
                // ... and the impostor's, in the same space, as large as PSX/Lit's _PSXFarGrow ever draws it
                if (HasLod(v, 3))
                {
                    float g = 1f;
                    if (vr.impostorDress != null)
                        foreach (var im in vr.impostorDress)
                            if (im != null && im.HasProperty("_PSXFarGrow")) g = Mathf.Max(g, im.GetVector("_PSXFarGrow").z);
                    var ib = vr.lods[3].bounds;
                    Vector3 ic = ib.center * ((1f + g) * 0.5f);
                    float ir = ib.extents.magnitude * g + ib.center.magnitude * (g - 1f) * 0.5f;
                    var fix = impFix[v];
                    float fs = Mathf.Max(fix.GetColumn(0).magnitude, Mathf.Max(fix.GetColumn(1).magnitude, fix.GetColumn(2).magnitude));
                    Vector3 oc = fix.MultiplyPoint3x4(ic);
                    isph = new Vector4(oc.x, oc.y, oc.z, ir * fs * 1.02f + 0.05f);
                }
                infos[v] = new VarInfo
                {
                    a = new Vector4(maxD2[v], fadeOf[v][0].z, fadeOf[v][1].z, fadeOf[v][2].z),
                    b = new Vector4(fadeOf[v][0].w, fadeOf[v][1].w, fadeOf[v][2].w, 0f),
                    b0 = bk[0], b1 = bk[1], b2 = bk[2], b3 = bk[3],
                    mirror = mb, flags = flags,
                    sphere = sph, impSphere = isph,
                };
            }
            if (buckets.Count == 0 || visN == 0) return false;
            mainCmds = cmdList.Count;
            cmdList.AddRange(mirrorCmds);
            cmds = cmdList.ToArray();
            bucketCount = buckets.Count;

            try
            {
                const GraphicsBuffer.Target S = GraphicsBuffer.Target.Structured;
                matBuf = new GraphicsBuffer(S, (n + impCount) * 3, 16);
                metaBuf = new GraphicsBuffer(S, n, 8);
                varBuf = new GraphicsBuffer(S, nv, Marshal.SizeOf<VarInfo>());
                chunkBuf = new GraphicsBuffer(S, chunks.Length + 1, 4);
                bucketBuf = new GraphicsBuffer(S, bucketCount, 8);
                cmdBucketBuf = new GraphicsBuffer(S, cmds.Length, 4);
                countBuf = new GraphicsBuffer(S, bucketCount, 4);
                visBuf = new GraphicsBuffer(S, (int)visN, 4);
                argsBuf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | S, cmds.Length, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                gpuBytes = (long)(n + impCount) * 48 + (long)n * 8 + (long)visN * 4;

                var meta = new NativeArray<MetaInfo>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < n; i++)
                    meta[i] = new MetaInfo { packed = (uint)(int)instScaleVar[i].y | ((uint)chunkOf[i] << 10), imp = impOf[i] };
                metaBuf.SetData(meta);
                meta.Dispose();
                varBuf.SetData(infos);
                bucketBuf.SetData(buckets);
                var cb = new uint[cmds.Length];
                var args = new GraphicsBuffer.IndirectDrawIndexedArgs[cmds.Length];
                for (int c = 0; c < cmds.Length; c++)
                {
                    cb[c] = (uint)cmds[c].bucket;
                    args[c] = new GraphicsBuffer.IndirectDrawIndexedArgs
                    {
                        indexCountPerInstance = cmds[c].indexCount,
                        instanceCount = 0,
                        startIndex = cmds[c].mesh.GetIndexStart(cmds[c].sub),
                        baseVertexIndex = cmds[c].mesh.GetBaseVertex(cmds[c].sub),
                        startInstance = 0,
                    };
                }
                cmdBucketBuf.SetData(cb);
                argsBuf.SetData(args);
                countBuf.SetData(new uint[bucketCount]);
                chunkFlag = new uint[chunks.Length + 1];
                chunkBuf.SetData(chunkFlag);
                chunkWorld = new Vector3[chunks.Length];
                chunkRad = new float[chunks.Length];
                BakeGpu(transform.localToWorldMatrix);

                kClear = cullShader.FindKernel("Clear"); kCull = cullShader.FindKernel("Cull"); kArgs = cullShader.FindKernel("Args");
                BindCull();

                cmdProps = new MaterialPropertyBlock[cmds.Length];
                cmdRp = new RenderParams[cmds.Length];
                cmdOn = new bool[cmds.Length];
                for (int c = 0; c < cmds.Length; c++)
                {
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetBuffer(IdMat, matBuf);
                    mpb.SetBuffer(IdVis, visBuf);
                    mpb.SetFloat(IdBase, buckets[cmds[c].bucket].start);
                    cmdProps[c] = mpb;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MeshForest] GPU path not available (" + e.Message + ") - CPU lists.");
                ReleaseGpu();
                return false;
            }
            gpuReady = true;
            cardsKnown = false;
            if (lastDress != -2) GpuDressChanged();
            return true;
        }

        void BindCull()
        {
            cullShader.SetBuffer(kClear, IdCount, countBuf);
            cullShader.SetBuffer(kCull, IdMat, matBuf);
            cullShader.SetBuffer(kCull, IdMeta, metaBuf);
            cullShader.SetBuffer(kCull, IdVar, varBuf);
            cullShader.SetBuffer(kCull, IdChunk, chunkBuf);
            cullShader.SetBuffer(kCull, IdBucket, bucketBuf);
            cullShader.SetBuffer(kCull, IdCount, countBuf);
            cullShader.SetBuffer(kCull, IdVis, visBuf);
            cullShader.SetBuffer(kArgs, IdCmdBucket, cmdBucketBuf);
            cullShader.SetBuffer(kArgs, IdBucket, bucketBuf);
            cullShader.SetBuffer(kArgs, IdCount, countBuf);
            cullShader.SetBuffer(kArgs, IdArgs, argsBuf);
            cullShader.SetInt(IdN, inst.Length);
            cullShader.SetInt(IdBuckets, bucketCount);
            cullShader.SetInt(IdCmds, cmds.Length);
            cullShader.SetVector(IdLod, new Vector4(lod0To * lod0To, lod1To * lod1To, meshRange * meshRange, Margin));
        }

        /// <summary>Every plant's matrix under this root, exactly as the CPU
        /// path makes it (root x local, and x the impostor's fix for a canopy
        /// tree's LOD 3), into the matrix buffer; the chunks' spheres.</summary>
        void BakeGpu(Matrix4x4 root)
        {
            gpuRoot = root;
            int n = inst.Length;
            var rows = new NativeArray<Vector4>((n + impCount) * 3, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            int at = n * 3;
            for (int i = 0; i < n; i++)
            {
                var m = root * LocalOf(i);
                rows[i * 3] = new Vector4(m.m00, m.m01, m.m02, m.m03);
                rows[i * 3 + 1] = new Vector4(m.m10, m.m11, m.m12, m.m13);
                rows[i * 3 + 2] = new Vector4(m.m20, m.m21, m.m22, m.m23);
                int v = (int)instScaleVar[i].y;
                var vr = variants[v];
                if (vr.lods.Length > 3 && vr.lods[3] != null)
                {
                    var mi = m * impFix[v];
                    rows[at++] = new Vector4(mi.m00, mi.m01, mi.m02, mi.m03);
                    rows[at++] = new Vector4(mi.m10, mi.m11, mi.m12, mi.m13);
                    rows[at++] = new Vector4(mi.m20, mi.m21, mi.m22, mi.m23);
                }
            }
            matBuf.SetData(rows);
            rows.Dispose();
            float scale = root.lossyScale.x;
            for (int ci = 0; ci < chunks.Length; ci++)
            {
                chunkWorld[ci] = root.MultiplyPoint3x4(chunks[ci].centre);
                chunkRad[ci] = chunks[ci].radius * scale;
            }
        }

        void ReleaseGpu()
        {
            gpuReady = false;
            matBuf?.Release(); metaBuf?.Release(); varBuf?.Release(); chunkBuf?.Release(); bucketBuf?.Release();
            cmdBucketBuf?.Release(); countBuf?.Release(); visBuf?.Release(); argsBuf?.Release();
            matBuf = metaBuf = varBuf = chunkBuf = bucketBuf = cmdBucketBuf = countBuf = visBuf = argsBuf = null;
        }

        /// <summary>Each draw command's material for the dress now applied
        /// (the same copies, with the same fade bands, the CPU path draws with).</summary>
        void GpuDressChanged()
        {
            if (!gpuReady) return;
            for (int c = 0; c < cmds.Length; c++)
            {
                var cm = cmds[c];
                Material mat;
                if (cm.lod == 4) mat = impNow[cm.variant];
                else mat = Faded(cm.lod == 3 ? impNow[cm.variant] : matsNow[cm.variant][cm.sub], fadeOf[cm.variant][cm.lod], ThinFor(cm.variant, cm.lod));
                cmdOn[c] = mat != null;
                if (mat == null) continue;
                cmdRp[c] = new RenderParams(mat)
                {
                    shadowCastingMode = ShadowCastingMode.Off,   // the sun's shadow: SunShadows' card stand-ins
                    receiveShadows = true,
                    layer = gameObject.layer,
                    matProps = cmdProps[c],
                };
            }
        }

        /// <summary>One frame of the GPU path for the main view (or an
        /// edit-mode shot's camera): the chunk pass, the cull, the draws.</summary>
        void GpuFrame(Camera cam, bool wideInstances = false)
        {
            long pc = PerfClock.On ? PerfClock.Now() : 0;
            var root = transform.localToWorldMatrix;
            if (root != gpuRoot) BakeGpu(root);
            Vector3 camPos = cam.transform.position;
            // THE CHUNKS, by the old path's own rule: a WIDER cone than the
            // camera's (a chunk swaps its cards for plants before it reaches
            // the screen's edge, not on it), cards where it is not drawn.
            var wide = Matrix4x4.Perspective(Mathf.Min(cam.fieldOfView * 1.5f + 10f, 170f), cam.aspect * 1.3f,
                                             cam.nearClipPlane, cam.farClipPlane);
            var w2c = cam.worldToCameraMatrix;
            GeometryUtility.CalculateFrustumPlanes(wide * w2c, planes);
            float far = Mathf.Max(farRange, meshRange);
            for (int l = 0; l < L; l++) { reachLo[l] = 0; reachHi[l] = 0; }
            float e0 = lod0To + Margin, e1 = lod1To + Margin, e2 = meshRange + Margin;
            float s1 = lod0To - band0 - Margin, s2 = lod1To - band1 - Margin, s3 = meshRange - band2 - Margin;
            bool dirty = !cardsKnown;
            for (int ci = 0; ci < chunkWorld.Length; ci++)
            {
                Vector3 wc = chunkWorld[ci];
                float rad = chunkRad[ci];
                float dx = wc.x - camPos.x, dy = wc.y - camPos.y, dz = wc.z - camPos.z;
                float dist = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                bool draw = dist - rad < far;
                if (draw)
                    for (int k = 0; k < 6; k++)
                    {
                        // (GeometryUtility.TestPlanesAABB of the cube round the chunk's sphere, as the old path asked)
                        var pn = planes[k].normal;
                        if (pn.x * wc.x + pn.y * wc.y + pn.z * wc.z + planes[k].distance < -rad * (Mathf.Abs(pn.x) + Mathf.Abs(pn.y) + Mathf.Abs(pn.z))) { draw = false; break; }
                    }
                uint f = !draw ? 0u : dist - rad >= meshRange ? 2u : 1u;
                if (!cardsKnown || f != chunkFlag[ci])
                {
                    if (!cardsKnown || (f == 0) != (chunkFlag[ci] == 0)) SetCards(chunks[ci], f == 0);
                    chunkFlag[ci] = f;
                    dirty = true;
                }
                if (f == 0) continue;
                ulong lo = chunkMaskLo[ci], hi = chunkMaskHi[ci];
                if (f == 2) { reachLo[3] |= lo; reachHi[3] |= hi; continue; }
                float dmin = dist - rad, dmax = dist + rad;
                if (dmin < e0) { reachLo[0] |= lo; reachHi[0] |= hi; }
                if (dmin < e1 && dmax > s1) { reachLo[1] |= lo; reachHi[1] |= hi; }
                if (dmin < e2 && dmax > s2) { reachLo[2] |= lo; reachHi[2] |= hi; }
                if (dmax > s3) { reachLo[3] |= lo; reachHi[3] |= hi; }
            }
            cardsKnown = true;
            if (dirty) chunkBuf.SetData(chunkFlag);

            // THE PLANTS, on the GPU, against the view as it stands now.
            if (wideInstances) for (int k = 0; k < 6; k++) instPlanes[k] = planes[k];
            else
            {
                var pj = cam.projectionMatrix;
                pj.m00 /= FrustumSlack; pj.m11 /= FrustumSlack;
                GeometryUtility.CalculateFrustumPlanes(pj * w2c, instPlanes);
            }
            for (int k = 0; k < 5; k++)
            {
                var pn = instPlanes[k].normal;
                planeVec[k] = new Vector4(pn.x, pn.y, pn.z, instPlanes[k].distance);
            }
            cullShader.SetVector(IdCam, camPos);
            cullShader.SetVectorArray(IdPlanes, planeVec);
            cullShader.Dispatch(kClear, (bucketCount + 63) / 64, 1, 1);
            cullShader.Dispatch(kCull, (inst.Length + 63) / 64, 1, 1);
            cullShader.Dispatch(kArgs, (cmds.Length + 63) / 64, 1, 1);
            if (PerfClock.On) { PerfClock.Add(PerfClock.Slot.ForestCull, pc); pc = PerfClock.Now(); }

            // THE DRAWS: one a species, level and submesh that a drawn chunk
            // can put a plant in. Near levels sort first (their bounds'
            // centres stand nearer down the view), so the far ones are drawn
            // behind what is already in the depth buffer.
            Vector3 fwd = cam.transform.forward;
            Vector3 size = Vector3.one * (2f * (far + 1000f));
            lodBounds[0] = new Bounds(camPos + fwd * (lod0To * 0.5f), size);
            lodBounds[1] = new Bounds(camPos + fwd * ((lod0To + lod1To) * 0.5f), size);
            lodBounds[2] = new Bounds(camPos + fwd * ((lod1To + meshRange) * 0.5f), size);
            lodBounds[3] = new Bounds(camPos + fwd * (meshRange + 400f), size);
            int drawn = 0;
            for (int c = 0; c < mainCmds; c++)
            {
                if (!cmdOn[c]) continue;
                int v = cmds[c].variant, l = cmds[c].lod;
                if (!reachAll && ((v < 64 ? reachLo[l] >> v : reachHi[l] >> (v - 64)) & 1UL) == 0) continue;
                cmdRp[c].camera = cam;
                cmdRp[c].worldBounds = lodBounds[l];
                Graphics.RenderMeshIndirect(in cmdRp[c], cmds[c].mesh, argsBuf, 1, c);
                drawn++;
            }
            if (PerfClock.On) PerfClock.Add(PerfClock.Slot.ForestDraw, pc);
            if (StatsOn) GpuStats(drawn);
        }

        /// <summary>The mirrors and the reflection faces: each canopy
        /// species' list of the drawn near chunks, as impostors.</summary>
        void GpuOtherCamera(Camera cam)
        {
            if (!cardsKnown) return;
            var b = new Bounds(cam.transform.position, Vector3.one * (2f * (Mathf.Max(farRange, meshRange) + 1000f)));
            for (int c = mainCmds; c < cmds.Length; c++)
            {
                if (!cmdOn[c]) continue;
                cmdRp[c].camera = cam;
                cmdRp[c].worldBounds = b;
                Graphics.RenderMeshIndirect(in cmdRp[c], cmds[c].mesh, argsBuf, 1, c);
            }
        }

        // ---- the tour's count of what the cull kept (a read-back every 4th frame, a few frames late) ----
        int statTick;
        void GpuStats(int drawn)
        {
            if ((statTick++ & 3) != 0) return;
            // 0: no material in this dress (never drawn), 1: drawn, 2: has a material but no drawn chunk reached it
            var state = new byte[mainCmds];
            for (int c = 0; c < mainCmds; c++)
            {
                int v = cmds[c].variant, l = cmds[c].lod;
                state[c] = !cmdOn[c] ? (byte)0 : reachAll || ((v < 64 ? reachLo[l] >> v : reachHi[l] >> (v - 64)) & 1UL) != 0 ? (byte)1 : (byte)2;
            }
            var mine = cmds;
            AsyncGPUReadback.Request(argsBuf, req =>
            {
                if (req.hasError || !StatsOn) return;
                var a = req.GetData<GraphicsBuffer.IndirectDrawIndexedArgs>();
                long tris = 0, plants = 0, missed = 0;
                for (int c = 0; c < state.Length && c < a.Length; c++)
                {
                    long t = (long)(mine[c].indexCount / 3) * a[c].instanceCount;
                    if (state[c] == 1) { tris += t; if (mine[c].sub == 0) plants += a[c].instanceCount; }
                    else if (state[c] == 2) missed += t;     // must stay 0: the reach test may only skip empty lists
                }
                SumTris += tris; SumPlants += plants; SumDraws += drawn; StatFrames++;
                if (tris > MaxTris) MaxTris = tris;
                UnreachedTris += missed;
            });
        }
    }
}
