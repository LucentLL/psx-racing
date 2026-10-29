using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.City
{
    /// <summary>
    /// THE FAR BUILDINGS (city-popin, 2026-10-28; owner, with NFS 2015 frames
    /// as the bar: "Buildings pop into existence slowly as you approach").
    /// The streamed tiles stop at the camera's 500 m far plane and only the
    /// skyline's towers and mid-rises (CitySkyline: 60 m+, 20 m+ round uptown)
    /// stood past it, so every lower building - a house, a shop, a three-storey
    /// block - appeared out of nothing at 500 m, and later than that at speed,
    /// when the tile builds fell behind. Now every footprint the skyline does
    /// not draw stands as a PROXY out to <see cref="RadiusM"/>: the tile's own
    /// building emitted the skyline's way (CityMeshes.EmitProxy - the same atlas
    /// column, tint, storeys, facade structure and night windows), in 512 m
    /// chunks built a slice a frame as the eye goes, merged into ONE mesh (a
    /// submesh a material) drawn by the skyline's three own-depth draws
    /// (CitySkyline.Draws): 3 x 4 draw calls whatever the radius.
    ///
    /// The hand-over is PSX/Lit's CITY POP-IN (CityTileFade): nearer than the
    /// squeeze's start a live tile draws its buildings and the proxy steps out
    /// of it; past it the proxy (squeezed in front of the far plane) draws
    /// them and the tile's own copy steps out - one dither for both sides, so
    /// the two never fight in depth. Where a tile is not built yet, its
    /// proxies stand near too, so a late tile no longer leaves a street
    /// without buildings; when it stands up its buildings fade in over the
    /// proxies, pixel for pixel. A chunk entering range fades in. Fog: off.
    /// PSX_CITY_PROXIES=0: none (the A/B).
    /// </summary>
    public class CityProxies : MonoBehaviour
    {
        public const float ChunkM = 512f;
        /// <summary>The far buildings round the eye (a phone: fewer).</summary>
        public static float RadiusM => Application.isMobilePlatform ? 1200f : 2000f;
        /// <summary>A shown chunk stays until it is this much further out
        /// (the mesh is not rebuilt for a chunk that wanders back and forth
        /// over the edge).</summary>
        const float KeepM = 400f;
        /// <summary>Built chunks are kept (CPU) this much past the radius.</summary>
        const float CacheM = 1200f;
        public static bool Enabled = System.Environment.GetEnvironmentVariable("PSX_CITY_PROXIES") != "0";
        /// <summary>A tool hides them for a before frame (the A/B in one run).</summary>
        public static bool Suppress;
        /// <summary>The merged mesh is rebuilt at most this often (s).</summary>
        const float SwapEveryS = 1.0f;
        /// <summary>A rebuild uploads this many vertices a frame into the
        /// mesh not being drawn, then the two swap.</summary>
        const int UploadVertsPerFrame = 24576;

        CityMap map;
        CityMeshes.Trims trims;
        System.Func<CityMeshes.Slot, Material> matFor;
        readonly Dictionary<long, List<int>> byChunk = new Dictionary<long, List<int>>();

        sealed class Chunk
        {
            public long key;
            public int cx, cz;
            public CityMeshes.ProxyVert[] v;
            public int[][] t;
        }
        readonly Dictionary<long, Chunk> built = new Dictionary<long, Chunk>();

        // the chunk being emitted
        long jobKey;
        List<int> jobList;
        int jobAt;
        bool jobOpen;
        readonly CityMeshes.ProxyOut jobOut = new CityMeshes.ProxyOut();
        double jobMs;

        // the merged mesh: the one drawn, and the one a rebuild fills
        Mesh front, back;
        readonly MeshFilter[] mfs = new MeshFilter[CitySkyline.DrawCount];
        readonly MeshRenderer[] mrs = new MeshRenderer[CitySkyline.DrawCount];
        readonly Material[][] mats = new Material[(int)CityMeshes.Slot.COUNT][];
        readonly HashSet<long> shown = new HashSet<long>();
        bool frontAny;
        List<Chunk> next;
        int nextAt, nextVBase;
        readonly List<int> nextSlots = new List<int>();
        readonly int[] nextIBase = new int[(int)CityMeshes.Slot.COUNT], nextICount = new int[(int)CityMeshes.Slot.COUNT], nextIRun = new int[(int)CityMeshes.Slot.COUNT];
        int[] idxScratch = new int[65536];
        float lastSwap = -1e9f;
        const MeshUpdateFlags Quiet = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontResetBoneBounds |
                                      MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontRecalculateBounds;

        // the eye, as the last refresh saw it
        Vector3 eye, fwd;
        bool eyeValid;
        readonly List<(long key, float score)> wantedOrder = new List<(long, float)>(256);

        // ---- numbers, for the logs and the probes ----
        public int Footprints { get; private set; }
        public int ShownChunks => shown.Count;
        public int BuiltChunks => built.Count;
        public int ShownVerts { get; private set; }
        public int ShownTris { get; private set; }
        public int EmittedFootprints { get; private set; }
        public double EmitMsTotal { get; private set; }
        public int Rebuilds { get; private set; }
        public string Summary => $"proxies: {ShownChunks} chunks shown ({ShownVerts} verts, {ShownTris} tris, {(front != null ? front.subMeshCount : 0)} submeshes), {BuiltChunks} built, {EmittedFootprints} footprints emitted in {EmitMsTotal:0} ms ({(EmittedFootprints > 0 ? EmitMsTotal / EmittedFootprints : 0):0.000} ms each), {Rebuilds} rebuilds";

        static long Key(int cx, int cz) => ((long)cx << 24) ^ (cz & 0xFFFFFF);
        static int ChunkOf(float v) => Mathf.FloorToInt(v / ChunkM);

        /// <summary>Index the footprints the far buildings draw: every one the
        /// skyline does not (its towers and mid-rises, and any part of them),
        /// that no model stands on and no part draws for it.</summary>
        public static CityProxies Create(Transform parent, CityMap map, CityMeshes.Trims trims,
                                         System.Func<CityMeshes.Slot, Material> matFor, ICollection<int> skylineOutlines)
        {
            if (!Enabled || map == null || map.footprints == null) return null;
            var go = new GameObject("CityProxies") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var cp = go.AddComponent<CityProxies>();
            cp.map = map; cp.trims = trims; cp.matFor = matFor;
            int models = 0, sky = 0, houses = 0;
            var fs = map.footprints;
            for (int i = 0; i < fs.Length; i++)
            {
                var f = fs[i];
                if (f.hidden || f.pts == null || f.pts.Length < 3) continue;
                if (f.propKind != 0) { models++; continue; }
                if (skylineOutlines != null && (skylineOutlines.Contains(i) || (f.part && f.outline >= 0 && skylineOutlines.Contains(f.outline)))) { sky++; continue; }
                long k = Key(ChunkOf(f.centre.x), ChunkOf(f.centre.y));
                if (!cp.byChunk.TryGetValue(k, out var list)) { list = new List<int>(64); cp.byChunk[k] = list; }
                list.Add(i);
                cp.Footprints++;
                if (f.gable || f.style == 3) houses++;
            }
            for (int d = 0; d < CitySkyline.DrawCount; d++)
            {
                var dgo = new GameObject("CityProxies " + (d + 1)) { hideFlags = HideFlags.DontSave };
                dgo.transform.SetParent(go.transform, false);
                cp.mfs[d] = dgo.AddComponent<MeshFilter>();
                var r = dgo.AddComponent<MeshRenderer>();
                r.enabled = false;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                SunShadows.Exclude(dgo);
                cp.mrs[d] = r;
            }
            cp.front = new Mesh { name = "CityProxies A", hideFlags = HideFlags.DontSave };
            cp.back = new Mesh { name = "CityProxies B", hideFlags = HideFlags.DontSave };
            cp.front.MarkDynamic(); cp.back.MarkDynamic();
            CityTileFade.Ensure();
            Debug.Log($"[CityProxies] {cp.Footprints} footprints ({houses} houses) in {cp.byChunk.Count} chunks of {ChunkM:0} m to {RadiusM:0} m; left out: {sky} the skyline draws, {models} model lots");
            return cp;
        }

        /// <summary>A tile's buildings are wholly the proxies' (the core OSM
        /// maps buildings for, where no frontage box or fill house stands):
        /// only there does the tile's own copy step out past the squeeze.</summary>
        bool Covered(int tx, int tz)
        {
            var b = map.footprintBounds;
            float x0 = tx * CityMeshes.TileSize, z0 = tz * CityMeshes.TileSize;
            return b.Contains(new Vector2(x0, z0)) && b.Contains(new Vector2(x0 + CityMeshes.TileSize, z0 + CityMeshes.TileSize));
        }

        Material[] Mats(int slot)
        {
            if (mats[slot] != null) return mats[slot];
            var src = matFor((CityMeshes.Slot)slot);
            if (src == null) return null;
            var set = new Material[CitySkyline.DrawCount];
            for (int d = 0; d < set.Length; d++)
            {
                var m = new Material(src) { name = src.name + " (proxy " + (d + 1) + ")", hideFlags = HideFlags.DontSave };
                CitySkyline.SetDraw(m, d);
                m.SetFloat("_SkyFarLand", 0f);
                m.SetFloat(CityTileFade.ModeId, CityTileFade.ModeProxy);
                set[d] = m;
            }
            mats[slot] = set;
            return set;
        }

        static float NearestD(Vector3 eye, int cx, int cz)
        {
            float nx = Mathf.Clamp(eye.x, cx * ChunkM, (cx + 1) * ChunkM), nz = Mathf.Clamp(eye.z, cz * ChunkM, (cz + 1) * ChunkM);
            return Mathf.Sqrt((nx - eye.x) * (nx - eye.x) + (nz - eye.z) * (nz - eye.z));
        }

        /// <summary>This camera's far buildings: shown or not, their bounds;
        /// in a tool's shot (edit mode) everything in range is built now.</summary>
        public void Refresh(Camera cam)
        {
            bool on = cam != null && !Suppress && Seasons.CurrentWeather != Weather.Fog;
            CityTileFade.SetProxiesOn(on && frontAny);
            if (!on) { Show(false); return; }
            if (eyeValid)
            {
                var mv = cam.transform.position - eye;
                mv.y = 0f;
                if (mv.sqrMagnitude > 1f) fwd = mv.normalized;
            }
            else fwd = cam.transform.forward;
            eye = cam.transform.position;
            eyeValid = true;
            if (!Application.isPlaying) FillNow();
            Show(frontAny);
            if (frontAny)
            {
                var b = new Bounds(eye, Vector3.one * (cam.farClipPlane * 2.2f));
                foreach (var r in mrs) r.bounds = b;
            }
        }

        void Show(bool on)
        {
            foreach (var r in mrs) if (r != null && r.enabled != on) r.enabled = on;
        }

        /// <summary>Everything in range, built and shown now (the tools).</summary>
        public void FillNow()
        {
            for (int guard = 0; guard < 4096; guard++)
            {
                if (next != null) { UploadStep(int.MaxValue); continue; }
                if (jobOpen) { EmitStep(double.MaxValue); continue; }
                if (StartNextJob()) continue;
                if (StartRebuild(true)) continue;
                break;
            }
        }

        /// <summary>A frame's share of the far buildings' work (CityWorld.Update):
        /// a rebuild's upload, else a chunk's footprints for
        /// <paramref name="budgetMs"/>.</summary>
        public void Step(double budgetMs)
        {
            if (!eyeValid || Suppress) return;
            if (next != null) { UploadStep(UploadVertsPerFrame); return; }
            if (StartRebuild(false)) return;
            if (!jobOpen && !StartNextJob()) return;
            EmitStep(budgetMs);
        }

        /// <summary>The nearest chunk in range that is not built (ahead
        /// first), opened as the job.</summary>
        bool StartNextJob()
        {
            float R = RadiusM;
            int ecx = ChunkOf(eye.x), ecz = ChunkOf(eye.z);
            int r = Mathf.CeilToInt(R / ChunkM) + 1;
            long best = 0; float bestS = float.MaxValue; bool any = false;
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int cx = ecx + dx, cz = ecz + dz;
                long k = Key(cx, cz);
                if (built.ContainsKey(k) || !byChunk.ContainsKey(k)) continue;
                float d = NearestD(eye, cx, cz);
                if (d > R) continue;
                var c = new Vector2((cx + 0.5f) * ChunkM - eye.x, (cz + 0.5f) * ChunkM - eye.z);
                float ahead = c.sqrMagnitude > 1f ? Vector2.Dot(c.normalized, new Vector2(fwd.x, fwd.z)) : 0f;
                float score = d * (1f - 0.35f * ahead);
                if (score < bestS) { bestS = score; best = k; any = true; }
            }
            if (!any) return false;
            jobKey = best;
            jobList = byChunk[best];
            jobAt = 0;
            jobOut.Clear();
            jobOpen = true;
            jobMs = 0.0;
            return true;
        }

        static readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();

        void EmitStep(double budgetMs)
        {
            clock.Restart();
            while (jobAt < jobList.Count)
            {
                int fi = jobList[jobAt++];
                var f = map.footprints[fi];
                int tx = Mathf.FloorToInt(f.centre.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(f.centre.y / CityMeshes.TileSize);
                var tile = new Vector2(tx + (Covered(tx, tz) ? 0f : 512f), tz);
                CityMeshes.EmitProxy(map, trims, fi, jobOut, tile);
                EmittedFootprints++;
                if (clock.Elapsed.TotalMilliseconds >= budgetMs) break;
            }
            double ms = clock.Elapsed.TotalMilliseconds;
            jobMs += ms; EmitMsTotal += ms;
            if (jobAt < jobList.Count) return;
            // the chunk is done
            var ch = new Chunk { key = jobKey, v = jobOut.v.ToArray(), t = new int[(int)CityMeshes.Slot.COUNT][] };
            ch.cx = (int)(jobKey >> 24); ch.cz = (int)((jobKey << 40) >> 40);
            for (int s = 0; s < ch.t.Length; s++)
                if (jobOut.t[s] != null && jobOut.t[s].Count > 0) ch.t[s] = jobOut.t[s].ToArray();
            built[jobKey] = ch;
            builtVersion++;
            jobOut.Clear();
            jobOpen = false;
            // forget chunks far behind
            List<long> drop = null;
            foreach (var kv in built)
                if (NearestD(eye, kv.Value.cx, kv.Value.cz) > RadiusM + CacheM && !shown.Contains(kv.Key)) (drop ??= new List<long>()).Add(kv.Key);
            if (drop != null) foreach (var k in drop) built.Remove(k);
        }

        /// <summary>Start filling the hidden mesh with the chunks that should
        /// show now, if that is not what shows (at most every
        /// <see cref="SwapEveryS"/> in play).</summary>
        int builtVersion, checkedVersion = -1;
        Vector3 checkedEye;
        readonly List<Chunk> wantScratch = new List<Chunk>(128);
        bool StartRebuild(bool now)
        {
            if (next != null) return false;
            // only when a chunk was built or the eye moved on
            if (!now && checkedVersion == builtVersion && (eye - checkedEye).sqrMagnitude < 64f * 64f) return false;
            if (!now && Time.unscaledTime - lastSwap < SwapEveryS && shown.Count > 0) return false;
            checkedVersion = builtVersion; checkedEye = eye;
            float R = RadiusM;
            var want = wantScratch;
            want.Clear();
            bool same = true;
            int nWant = 0;
            foreach (var kv in built)
            {
                float d = NearestD(eye, kv.Value.cx, kv.Value.cz);
                bool keep = d <= R || (shown.Contains(kv.Key) && d <= R + KeepM);
                if (!keep) continue;
                want.Add(kv.Value);
                nWant++;
                if (!shown.Contains(kv.Key)) same = false;
            }
            if (same && nWant == shown.Count) return false;
            // order: by chunk key (stable), so the index buffer is laid out the same each time
            want.Sort((a, b) => a.key.CompareTo(b.key));
            next = want;
            nextAt = 0; nextVBase = 0;
            int V = 0;
            System.Array.Clear(nextICount, 0, nextICount.Length);
            foreach (var c in want)
            {
                V += c.v.Length;
                for (int s = 0; s < c.t.Length; s++) if (c.t[s] != null) nextICount[s] += c.t[s].Length;
            }
            nextSlots.Clear();
            int I = 0;
            for (int s = 0; s < nextICount.Length; s++)
            {
                if (nextICount[s] == 0 || Mats(s) == null) { nextICount[s] = 0; continue; }
                nextSlots.Add(s);
                nextIBase[s] = I; nextIRun[s] = 0;
                I += nextICount[s];
            }
            back.Clear();
            if (V > 0 && I > 0)
            {
                back.SetVertexBufferParams(V, CityMeshes.ProxyLayout);
                back.SetIndexBufferParams(I, IndexFormat.UInt32);
            }
            if (V == 0 || I == 0) { nextAt = want.Count; }
            return true;
        }

        void UploadStep(int maxVerts)
        {
            int up = 0;
            while (nextAt < next.Count && up < maxVerts)
            {
                var c = next[nextAt++];
                if (c.v.Length > 0 && nextSlots.Count > 0)
                {
                    back.SetVertexBufferData(c.v, 0, nextVBase, c.v.Length, 0, Quiet);
                    foreach (int s in nextSlots)
                    {
                        var src = c.t[s];
                        if (src == null || src.Length == 0) continue;
                        if (idxScratch.Length < src.Length) idxScratch = new int[Mathf.NextPowerOfTwo(src.Length)];
                        for (int k = 0; k < src.Length; k++) idxScratch[k] = src[k] + nextVBase;
                        back.SetIndexBufferData(idxScratch, 0, nextIBase[s] + nextIRun[s], src.Length, Quiet);
                        nextIRun[s] += src.Length;
                    }
                }
                nextVBase += c.v.Length;
                up += c.v.Length;
            }
            if (nextAt < next.Count) return;
            // finished: the submeshes, then swap
            int tris = 0;
            if (nextSlots.Count > 0 && nextVBase > 0)
            {
                back.subMeshCount = nextSlots.Count;
                for (int i = 0; i < nextSlots.Count; i++)
                {
                    int s = nextSlots[i];
                    back.SetSubMesh(i, new SubMeshDescriptor(nextIBase[s], nextICount[s], MeshTopology.Triangles), Quiet);
                    tris += nextICount[s] / 3;
                }
                back.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            }
            var oldShown = new HashSet<long>(shown);
            (front, back) = (back, front);
            frontAny = nextSlots.Count > 0 && nextVBase > 0;
            var mset = new Material[nextSlots.Count];
            for (int d = 0; d < mfs.Length; d++)
            {
                for (int i = 0; i < nextSlots.Count; i++) mset[i] = Mats(nextSlots[i])[d];
                mfs[d].sharedMesh = frontAny ? front : null;
                mrs[d].sharedMaterials = mset;
            }
            shown.Clear();
            foreach (var c in next) shown.Add(c.key);
            next.Clear();
            // the proxies' share in the fade table: in (fading) where a chunk
            // came into the mesh, out where one left it
            foreach (var k in oldShown)
                if (!shown.Contains(k)) SetChunkA(k, false);
            foreach (var k in shown)
                if (!oldShown.Contains(k)) SetChunkA(k, true);
            ShownVerts = nextVBase; ShownTris = tris;
            next = null;
            lastSwap = Time.unscaledTime;
            Rebuilds++;
        }

        void SetChunkA(long key, bool on)
        {
            int cx = (int)(key >> 24), cz = (int)((key << 40) >> 40);
            int per = Mathf.RoundToInt(ChunkM / CityMeshes.TileSize);
            for (int j = 0; j < per; j++)
            for (int i = 0; i < per; i++)
            {
                int tx = cx * per + i, tz = cz * per + j;
                if (!on) CityTileFade.Set(tx, tz, CityTileFade.A, 0f);
                else if (Covered(tx, tz)) CityTileFade.FadeIn(tx, tz, CityTileFade.A);
                else CityTileFade.Set(tx, tz, CityTileFade.A, 0f);
            }
        }

        void OnDestroy()
        {
            foreach (var k in shown) SetChunkA(k, false);
            if (front != null) Kill(front);
            if (back != null) Kill(back);
            foreach (var set in mats) if (set != null) foreach (var m in set) if (m != null) Kill(m);
            CityTileFade.SetProxiesOn(false);
        }

        static void Kill(Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
