using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// THE BAKED CITY's streaming (CityTileCache, phase 1 of Forza-style
    /// streaming, 2026-11-02). Where the bake holds a tile the streaming ring
    /// wants, nothing is generated on the main thread:
    ///
    ///   1. a worker READS it (file, inflate, decode into plain arrays) - up to
    ///      <see cref="BakedInFlight"/> at a time, in the order the car needs them;
    ///   2. the main thread MAKES its meshes from the arrays (one tile a frame);
    ///   3. a worker COOKS its colliders (Physics.BakeMesh, as a build cooks them
    ///      a step each) so the stand-up assigns cooked meshes;
    ///   4. the main thread STANDS IT UP through <see cref="StandUp"/> - the very
    ///      path a built tile takes (renderers, colliders, props, stations, lamps,
    ///      fades, its trees queued) - on a frame of its own, with the lattice its
    ///      build cached swapped into the builder's cache for the stand-up (a live
    ///      job or a tree frame open meanwhile keeps its own).
    /// A tile under a car (EnsureTile) does all four at once. A tile the bake
    /// lacks, or whose file fails, is built live exactly as before.
    /// </summary>
    public partial class CityWorld
    {
        sealed class BakedLoad
        {
            public int tx, tz;
            /// <summary>0 reading (worker), 1 read, 2 cooking (worker), 3 ready, -1 failed (built live).</summary>
            public int stage;
            public CityTileCache.Decoded dec;
            public CityMeshes.TileMeshes tm;
            public bool dropped;
            public float need;
            public double makeMs;
        }

        /// <summary>The bake's tiles in hand: being read, made, cooked or
        /// waiting for their stand-up frame.</summary>
        readonly Dictionary<long, BakedLoad> baked = new Dictionary<long, BakedLoad>();
        /// <summary>Tiles read ahead at once (the workers take them in turn).</summary>
        public static int BakedInFlight = 6;
        /// <summary>Since load: tiles stood up from the bake, and bake files
        /// that failed (those were built live).</summary>
        public static int BakedStood { get; private set; }
        public static int BakedFailed { get; private set; }
        /// <summary>Baked tiles in hand (the probe's and the overlay's count).</summary>
        public int BakedPending => baked.Count;

        /// <summary>A frame of the baked city: wanted tiles the bake holds taken
        /// out of <see cref="wanted"/> and read ahead; one tile stood up, else one
        /// tile's meshes made. True when this frame did main-thread tile work.</summary>
        bool StreamBaked()
        {
            if (CityTileCache.On && wanted.Count > 0)
            {
                wanted.Sort((a, b) => a.d2.CompareTo(b.d2));
                int inFlight = 0;
                foreach (var b in baked.Values) if (b.stage == 0 || b.stage == 2) inFlight++;
                for (int i = 0; i < wanted.Count; i++)
                {
                    var w = wanted[i];
                    long k = Key(w.tx, w.tz);
                    if (baked.TryGetValue(k, out var have))
                    {
                        if (have.stage < 0) continue;   // its file failed: built live
                        have.need = Mathf.Min(have.need, w.d2);
                        wanted.RemoveAt(i--);
                        continue;
                    }
                    if (!CityTileCache.Has(w.tx, w.tz)) continue;
                    wanted.RemoveAt(i--);   // the bake's, read ahead or waiting for a reader
                    if (inFlight >= BakedInFlight) continue;
                    StartBaked(k, w.tx, w.tz, w.d2);
                    inFlight++;
                }
            }
            if (baked.Count == 0) return false;
            // the stand-up first (its meshes are made and cooked), else a make
            BakedLoad best = null; long bestK = 0;
            foreach (var kv in baked)
                if (kv.Value.stage == 3 && (best == null || kv.Value.need < best.need)) { best = kv.Value; bestK = kv.Key; }
            if (best != null)
            {
                baked.Remove(bestK);
                StandUpBaked(bestK, best, 1);
                return true;
            }
            foreach (var kv in baked)
                if (kv.Value.stage == 1 && (best == null || kv.Value.need < best.need)) { best = kv.Value; bestK = kv.Key; }
            if (best != null) { MakeBaked(best, true); return true; }
            return false;
        }

        void StartBaked(long k, int tx, int tz, float need)
        {
            var bl = new BakedLoad { tx = tx, tz = tz, need = need, stage = 0 };
            baked[k] = bl;
            string folder = CityTileCache.Dir;
            CityTileCache.Decoded dec = null;
            CityWorker.Run(() => dec = CityTileCache.Read(folder, tx, tz), () =>
            {
                if (bl.dropped) { dec?.Release(); return; }
                bl.dec = dec;
                if (dec == null || dec.error != null)
                {
                    bl.stage = -1;
                    BakedFailed++;
                    Debug.LogWarning($"[CityWorld] baked tile {tx},{tz} unreadable ({dec?.error}) - built live");
                    return;
                }
                bl.stage = 1;
            });
        }

        /// <summary>A read tile's meshes made (main thread), and its colliders
        /// cooked on a worker (<paramref name="cookOnWorker"/>) or left to the
        /// stand-up's MeshColliders (a tile under a car cannot wait a frame).</summary>
        void MakeBaked(BakedLoad bl, bool cookOnWorker)
        {
            long pc = PerfClock.On ? PerfClock.Now() : 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bl.tm = CityTileCache.Apply(bl.dec);
            // city perf round 1: each mesh sent to the GPU ahead, as a build sends them
            if (CityMeshes.PreUpload && Application.isPlaying && bl.tm != null)
                foreach (var m in new[] { bl.tm.ground, bl.tm.roads, bl.tm.buildings, bl.tm.water, bl.tm.structure, bl.tm.barriers, bl.tm.guardrails, bl.tm.kerbs, bl.tm.lampPosts, bl.tm.banks, bl.tm.medianPlants })
                    if (m != null) m.UploadMeshData(false);
            bl.makeMs = clock.Elapsed.TotalMilliseconds;
            if (PerfClock.On) PerfClock.Add(PerfClock.Slot.TileBaked, pc);
            if (bl.tm == null) { bl.stage = -1; BakedFailed++; return; }
            if (!cookOnWorker || !CityMeshes.BakeColliders || !CityWorker.Threaded) { bl.stage = 3; return; }
            // the colliders cooked ahead (Physics.BakeMesh is thread-safe), with the
            // options the stand-up's MeshColliders are told, so nothing cooks twice
            var ids = new List<EntityId>(5);
            foreach (var m in CityTileCache.ColliderMeshes(bl.tm)) if (m != null) ids.Add(m.GetEntityId());
            var opts = CityMeshes.CookOptions;
            bl.stage = 2;
            CityWorker.Run(() => { foreach (var id in ids) Physics.BakeMesh(id, false, opts); }, () =>
            {
                if (bl.dropped) { KillMeshes(bl.tm); return; }
                bl.stage = 3;
            });
        }

        /// <summary>A made tile stood up the way a built one is, on the lattice
        /// its build cached; <paramref name="slices"/> 0 = at once (no fade in,
        /// its stations now), as a tile built whole.</summary>
        void StandUpBaked(long k, BakedLoad bl, int slices)
        {
            if (live.ContainsKey(k)) { KillMeshes(bl.tm); return; }
            string sig = VerifyBaked && job == null && treeSteps == null ? CityTileCache.DataHash(bl.tm) : null;
            var prev = CityMeshes.SwapLattice(bl.dec.lattice);
            try { StandUp(k, bl.tx, bl.tz, bl.tm, bl.dec.readMs + bl.makeMs, slices, (float)bl.makeMs); }
            finally { CityMeshes.SwapLattice(prev); }
            BakedStood++;
            if (sig != null) Verify(bl.tx, bl.tz, sig);
        }

        // ---- the gate in the shipping runtime: -psx-tilecache-verify ---------
        /// <summary>Every baked tile is also BUILT here, by this player's own
        /// generator, and the two compared field for field (a verification run
        /// only: each tile costs its whole build in one frame).
        /// PSX_TILECACHE_VERIFY=1 or -psx-tilecache-verify.</summary>
        public static bool VerifyBaked = System.Environment.GetEnvironmentVariable("PSX_TILECACHE_VERIFY") == "1" ||
                                         System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-psx-tilecache-verify") >= 0;
        public static int VerifiedEqual { get; private set; }
        public static int VerifiedDiffer { get; private set; }

        void Verify(int tx, int tz, string bakedSig)
        {
            // (no job and no tree frame open: the build's scratch and lattice are free)
            var tm = CityMeshes.Build(Map, nodeTrims, buildings, tx, tz);
            CityMeshes.TakeLattice();
            string builtSig = CityTileCache.DataHash(tm);
            KillMeshes(tm);
            if (builtSig == bakedSig) VerifiedEqual++;
            else
            {
                VerifiedDiffer++;
                if (VerifiedDiffer <= 20) Debug.LogWarning($"[CityTileCache] VERIFY tile {tx},{tz}: baked {bakedSig} / built here {builtSig} - DIFFER");
            }
            if ((VerifiedEqual + VerifiedDiffer) % 25 == 0 || VerifiedDiffer == 1)
                Debug.Log($"[CityTileCache] verify: {VerifiedEqual} tiles equal, {VerifiedDiffer} differ (baked vs built by this player)");
        }

        /// <summary>EnsureTile's half: the tile now, from the bake, if it holds
        /// it (whatever stage its load in hand had reached). False: build it.</summary>
        bool EnsureBakedNow(long key, int tx, int tz)
        {
            if (!CityTileCache.Has(tx, tz)) return false;
            if (baked.TryGetValue(key, out var bl))
            {
                if (bl.stage == 3) { baked.Remove(key); StandUpBaked(key, bl, 0); return true; }
                if (bl.stage == 1) { baked.Remove(key); MakeBaked(bl, false); if (bl.tm == null) return false; StandUpBaked(key, bl, 0); return true; }
                if (bl.stage < 0) return false;
                // being read or cooked on a worker: that one is let go, this one read here
                Cancel(key, bl);
            }
            long pc = PerfClock.On ? PerfClock.Now() : 0;
            var dec = CityTileCache.Read(CityTileCache.Dir, tx, tz);
            if (PerfClock.On) PerfClock.Add(PerfClock.Slot.TileSync, pc);
            if (dec.error != null)
            {
                BakedFailed++;
                Debug.LogWarning($"[CityWorld] baked tile {tx},{tz} unreadable ({dec.error}) - built live");
                return false;
            }
            var now = new BakedLoad { tx = tx, tz = tz, dec = dec, stage = 1 };
            MakeBaked(now, false);
            if (now.tm == null) return false;
            StandUpBaked(key, now, 0);
            return true;
        }

        /// <summary>The course preload's read-ahead: the next tiles of the
        /// queue the bake holds, read on the workers while the loading frame
        /// stands the ones before them up.</summary>
        void PrefetchCourse()
        {
            if (!CityTileCache.On || !CityWorker.Threaded) return;
            int ahead = 0;
            for (int i = 0; i < courseQueue.Count && ahead < 2 * BakedInFlight; i++)
            {
                long k = courseQueue[i];
                if (live.ContainsKey(k)) continue;
                ahead++;
                if (baked.ContainsKey(k)) continue;
                int tx = (int)(k >> 24), tz = (int)((k << 40) >> 40);
                if (CityTileCache.Has(tx, tz)) StartBaked(k, tx, tz, i);
            }
        }

        /// <summary>The course's next tile is still on a worker (read or cooked).</summary>
        bool CourseTileReading(long k) => baked.TryGetValue(k, out var bl) && (bl.stage == 0 || bl.stage == 2);

        /// <summary>Loads in hand outside every ring (the drop rule's +1, the
        /// course's pins, the anchors' rings) let go.</summary>
        void DropBakedOutside(int ptx, int ptz)
        {
            List<long> gone = null;
            foreach (var kv in baked)
            {
                int tx = kv.Value.tx, tz = kv.Value.tz;
                if (Mathf.Abs(tx - ptx) <= ring + 1 && Mathf.Abs(tz - ptz) <= ring + 1) continue;
                if (pinned.Contains(kv.Key)) continue;
                bool held = false;
                foreach (var a in anchors)
                {
                    if (a == null || !a.gameObject.activeInHierarchy) continue;
                    int ax = Mathf.FloorToInt(a.position.x / CityMeshes.TileSize);
                    int az = Mathf.FloorToInt(a.position.z / CityMeshes.TileSize);
                    if (Mathf.Abs(tx - ax) <= AnchorRing + 1 && Mathf.Abs(tz - az) <= AnchorRing + 1) { held = true; break; }
                }
                if (!held) (gone ??= new List<long>()).Add(kv.Key);
            }
            if (gone != null) foreach (var k in gone) Cancel(k, baked[k]);
        }

        /// <summary>Let every load in hand go (null), or the one tile's.</summary>
        void DropBaked(long? only)
        {
            if (only.HasValue) { if (baked.TryGetValue(only.Value, out var b)) Cancel(only.Value, b); return; }
            foreach (var k in new List<long>(baked.Keys)) Cancel(k, baked[k]);
        }

        void Cancel(long k, BakedLoad bl)
        {
            baked.Remove(k);
            bl.dropped = true;
            // a worker still holds stage 0 (its apply hands the arrays back) and
            // stage 2 (its apply destroys the meshes)
            if (bl.stage == 1) bl.dec?.Release();
            else if (bl.stage == 3) KillMeshes(bl.tm);
        }

        static void KillMeshes(CityMeshes.TileMeshes tm)
        {
            if (tm == null) return;
            foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.guardrails, tm.kerbs, tm.lampPosts, tm.banks, tm.water, tm.buildings, tm.buildingCollider, tm.structure, tm.medianPlants })
                Kill(m);
        }
    }
}
