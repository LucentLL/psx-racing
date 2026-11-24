using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// NIGHT 4 (night round 4, 2026-11-23): A HEADLIGHT LIGHTS ONLY WHAT IT CAN SEE.
    /// The owner, on a PC61 replay of a mountain stage: "why does light go through
    /// walls, highway medians, etc?" - a car round a bend behind a rock wall laid
    /// its pool on the road on the near side of the rock. Neither the per-pixel
    /// beams (PSXHeadlights.cginc) nor NIGHT 3's road pools (CarLights.Pools.cs)
    /// knew of anything in the way.
    ///
    /// Each frame, for every lit car, 64 rays fan out from between its headlamps
    /// at the lamps' height, parallel to the car's own floor (so a steady grade is
    /// no wall), across the beam and its spill (75 degrees either side), batched
    /// on the job threads (RaycastCommand). On each bearing the distance to the
    /// first solid that is not the road rising ahead (a wall, a barrier, a rock
    /// face, a building, a parked thing; cars excluded) is the beam's reach there.
    /// A rising or falling distance eases over about a tenth of a second, so a
    /// wall cutting in slides the light off the road instead of switching it. The
    /// reaches go into one small texture (64 x 128, a row per car), read by the
    /// per-pixel beams (one fetch per beam per pixel inside its spread) and by the
    /// pool splat. A Jersey barrier (0.81 m) stands over a headlamp (0.6-0.7 m):
    /// the far carriageway takes none of the beam. PSX_NIGHT4=0: no occlusion.
    /// </summary>
    public partial class CarLights
    {
        public const int OccBins = 64;
        public const int OccRows = 128;
        /// <summary>The half angle the bins cover (degrees): past the spill's 70.</summary>
        public const float OccHalfDeg = 75f;
        /// <summary>"Nothing in the way" (m): past the beam's range.</summary>
        public const float OccClear = 99f;
        /// <summary>The time a reach takes to follow a change (s).</summary>
        public static float OccEaseS = 0.10f;
        static Texture2D occTex;
        static ushort[] occData;
        static readonly int OccTexId = Shader.PropertyToID("_PSXHeadVis");
        static readonly int OccId = Shader.PropertyToID("_PSXHeadOcc");
        static readonly Vector4[] gOcc = new Vector4[MaxLights];
        /// <summary>Rays cast last frame and their ms (main thread, tools).</summary>
        public static int OccRays { get; private set; }
        public static float OccMs { get; private set; }

        [System.NonSerialized] float[] occCur;
        [System.NonSerialized] int occRow = -1;
        [System.NonSerialized] int occFrame = -1;
        [System.NonSerialized] int occCast = -1;
        [System.NonSerialized] float occAt;

        static bool OccOn => StreetLights.Night4 && lightsOn;

        /// <summary>The v of this car's row this frame, or -1 (no occlusion known).</summary>
        float OccV => OccOn && occRow >= 0 && occTex != null ? (occRow + 0.5f) / OccRows : -1f;

        /// <summary>Round 4b: the fan's rays are cast on the job threads and their hits read on a LATER
        /// frame (never a Complete() that waits); false = the round-4 way (cast and wait, the "before").</summary>
        public static bool OccAsync = System.Environment.GetEnvironmentVariable("PSX_N4_ASYNC") != "0";
        static NativeArray<RaycastCommand> occCmds;
        static NativeArray<RaycastHit> occHits;
        static Unity.Jobs.JobHandle occJob;
        static bool occPending;
        static readonly List<CarLights> occCastCars = new List<CarLights>(64);
        static float[] occCos, occSin;
        static NativeArray<Vector3> occO, occF, occR;
        static NativeArray<Vector2> occDirs;
        static bool occQuitHooked;

        static void OccDisposeAll()
        {
            if (occPending) { occJob.Complete(); occPending = false; }
            if (occCmds.IsCreated) occCmds.Dispose();
            if (occHits.IsCreated) occHits.Dispose();
            if (occO.IsCreated) { occO.Dispose(); occF.Dispose(); occR.Dispose(); }
            if (occDirs.IsCreated) occDirs.Dispose();
        }

        /// <summary>Cast every lit car's fan and fill the texture (PushGlobals, after the sort).</summary>
        static void OccUpdate(Camera cam)
        {
            for (int c = 0; c < sorted.Count; c++) sorted[c].occRow = -1;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (!OccOn || cam == null || sorted.Count == 0) { OccRays = 0; OccCollect(true); OccMs = (float)clock.Elapsed.TotalMilliseconds; Night4Clock.Occ(OccMs); return; }
            if (!occQuitHooked) { occQuitHooked = true; Application.quitting += OccDisposeAll; }
            if (occCos == null)
            {
                occCos = new float[OccBins]; occSin = new float[OccBins];
                float half = OccHalfDeg * Mathf.Deg2Rad;
                for (int b = 0; b < OccBins; b++) { float a = ((b + 0.5f) / OccBins - 0.5f) * 2f * half; occCos[b] = Mathf.Cos(a); occSin[b] = Mathf.Sin(a); }
            }
            Vector3 eye = cam.transform.position;
            float span = PoolRes * PoolTexelM * 0.5f;
            // 1. the last frame's rays, if the job threads are done with them (never waited for)
            OccCollect(!OccAsync);
            // 2. the rows: a car keeps its row while it stays lit in the window (4b: a row is written only when
            // its reaches change, and the texture goes up only then)
            if (occTex == null)
            {
                occTex = new Texture2D(OccBins, OccRows, TextureFormat.RHalf, false, true)
                { name = "PSXHeadVis", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                occData = new ushort[OccBins * OccRows];
            }
            int cars = 0;
            for (int r = 0; r < OccRows; r++)
            {
                var ow = occRowOwner[r];
                if (ow == null) continue;
                if (!ow.isActiveAndEnabled || !ow.LitNow || (ow.transform.position - eye).sqrMagnitude > span * span || ow.occRowFrame < Time.frameCount - 2)
                { ow.occRowHeld = -1; occRowOwner[r] = null; }
            }
            for (int c = 0; c < sorted.Count; c++)
            {
                var l = sorted[c];
                if ((l.transform.position - eye).sqrMagnitude > span * span) continue;
                if (l.occRowHeld < 0 || occRowOwner[l.occRowHeld] != l)
                {
                    int free = -1;
                    for (int r = 0; r < OccRows; r++) if (occRowOwner[r] == null) { free = r; break; }
                    if (free < 0) continue;
                    occRowOwner[free] = l; l.occRowHeld = free;
                    OccWriteRow(l);
                    occTexDirty = true;
                }
                l.occRow = l.occRowHeld; l.occRowFrame = Time.frameCount;
                cars++;
            }
            if (occTexDirty)
            {
                occTexDirty = false;
                occTex.SetPixelData(occData, 0);
                occTex.Apply(false, false);
            }
            if (cars > 0) Shader.SetGlobalTexture(OccTexId, occTex);
            // 3. the next rays, while none are out: near cars (80 m) every look, to 250 m every other, past it every fourth
            int total = 0;
            if (!occPending && cars > 0)
            {
                occCastCars.Clear();
                for (int c = 0; c < sorted.Count; c++)
                {
                    var l = sorted[c];
                    if (l.occRow < 0) continue;
                    float d2c = (l.transform.position - eye).sqrMagnitude;
                    int every = d2c < 80f * 80f ? 2 : d2c < 250f * 250f ? 4 : 8;
                    bool due = l.occCur == null || l.occFrame < Time.frameCount - 8 || ((l.occRow + Time.frameCount) % every) == 0;
                    if (due) occCastCars.Add(l);
                }
                total = occCastCars.Count * OccBins;
                if (total > 0)
                {
                    if (!occCmds.IsCreated || occCmds.Length < total)
                    {
                        if (occCmds.IsCreated) occCmds.Dispose();
                        if (occHits.IsCreated) occHits.Dispose();
                        int cap = Mathf.Max(total, OccBins * 48);
                        occCmds = new NativeArray<RaycastCommand>(cap, Allocator.Persistent);
                        occHits = new NativeArray<RaycastHit>(cap * 2, Allocator.Persistent);
                    }
                    var qp = new QueryParameters(~((1 << 2) | (1 << 10)), false, QueryTriggerInteraction.Ignore, false);
                    int nCars = occCastCars.Count;
                    if (!occO.IsCreated || occO.Length < nCars)
                    {
                        if (occO.IsCreated) { occO.Dispose(); occF.Dispose(); occR.Dispose(); }
                        int capC = Mathf.Max(nCars, 64);
                        occO = new NativeArray<Vector3>(capC, Allocator.Persistent);
                        occF = new NativeArray<Vector3>(capC, Allocator.Persistent);
                        occR = new NativeArray<Vector3>(capC, Allocator.Persistent);
                    }
                    if (!occDirs.IsCreated)
                    {
                        occDirs = new NativeArray<Vector2>(OccBins, Allocator.Persistent);
                        for (int b = 0; b < OccBins; b++) occDirs[b] = new Vector2(occCos[b], occSin[b]);
                    }
                    for (int i = 0; i < nCars; i++)
                    {
                        occCastCars[i].OccOrigin(out var o, out var f, out var r);
                        occO[i] = o; occF[i] = f; occR[i] = r;
                    }
                    // the commands written on the job threads, the rays cast after them: the main thread only schedules
                    var fill = new N4FanFillJob { origins = occO, fwds = occF, rights = occR, dirs = occDirs, qp = qp, distance = BeamRange + 4f, cmds = occCmds.GetSubArray(0, total) }
                        .Schedule(total, 64);
                    occJob = RaycastCommand.ScheduleBatch(occCmds.GetSubArray(0, total), occHits.GetSubArray(0, total * 2), 32, 2, fill);
                    occPending = true;
                    Unity.Jobs.JobHandle.ScheduleBatchedJobs();
                    if (!OccAsync) OccCollect(true);
                }
            }
            OccRays = total;
            OccMs = (float)clock.Elapsed.TotalMilliseconds;
            Night4Clock.Occ(OccMs);
        }

        static readonly CarLights[] occRowOwner = new CarLights[OccRows];
        static bool occTexDirty;
        [System.NonSerialized] int occRowHeld = -1, occRowFrame = -9;

        static void OccWriteRow(CarLights l)
        {
            if (occData == null || l.occRowHeld < 0) return;
            int o = l.occRowHeld * OccBins;
            if (l.occCur == null) { ushort clear = Mathf.FloatToHalf(OccClear); for (int b = 0; b < OccBins; b++) occData[o + b] = clear; return; }
            for (int b = 0; b < OccBins; b++) occData[o + b] = Mathf.FloatToHalf(l.occCur[b]);
        }

        /// <summary>Read the rays that are back (all of them when <paramref name="wait"/>), ease each car's reaches.</summary>
        static void OccCollect(bool wait)
        {
            if (!occPending) return;
            if (!wait && !occJob.IsCompleted) return;
            occJob.Complete();
            occPending = false;
            float now = Time.unscaledTime;
            for (int i = 0; i < occCastCars.Count; i++)
            {
                var l = occCastCars[i];
                if (l == null) continue;
                var up = l.transform.up;
                bool snap = l.occCur == null || l.occFrame < Time.frameCount - 8;
                if (l.occCur == null) l.occCur = new float[OccBins];
                float k = snap ? 1f : 1f - Mathf.Exp(-Mathf.Min(now - l.occAt, 0.1f) / Mathf.Max(OccEaseS, 1e-3f));
                l.occFrame = Time.frameCount; l.occAt = now;
                for (int b = 0; b < OccBins; b++)
                {
                    int ci = i * OccBins + b;
                    float reach = OccClear;
                    for (int m = 0; m < 2; m++)
                    {
                        var h = occHits[ci * 2 + m];
                        if (h.normal == Vector3.zero) continue;
                        // the road rising ahead (a sag, the foot of a hill) is the road, not a wall
                        if (Vector3.Dot(h.normal, up) > 0.8f) continue;
                        reach = Mathf.Min(reach, h.distance);
                    }
                    l.occCur[b] = snap ? reach : l.occCur[b] + (reach - l.occCur[b]) * k;
                }
                OccWriteRow(l);
                occTexDirty = true;
            }
            occCastCars.Clear();
        }

        /// <summary>The fan's origin (plan x, z) for the shaders: the per-pixel beams and the pools take the
        /// bearing from HERE, the point the reaches were measured from (round 4 took it from each lamp: at a
        /// corner the two lamps' bearings fell either side of it, and half the beam leaked past).</summary>
        internal Vector2 OccOriginXZ()
        {
            OccOrigin(out var o, out _, out _);
            return new Vector2(o.x, o.z);
        }

        /// <summary>Between the two headlamps, a hair under them; the car's own forward and right.</summary>
        void OccOrigin(out Vector3 o, out Vector3 f, out Vector3 r)
        {
            var t = transform;
            f = t.forward; r = t.right;
            var a = beams[0] != null ? beams[0].transform.position : t.position + t.up * 0.65f;
            var b = beams[1] != null ? beams[1].transform.position : a;
            // LOW: the light that reaches the ground beyond a solid passes it well under the lamps' own
            // height (from 0.85 m to the road 11 m on, a Jersey 5 m out is crossed at 0.47 m), so the fan
            // looks at 0.45 m under the lamps: a solid that tall (a Jersey, a rail, a wall, rock) takes
            // the beam beyond it; a kerb or a low verge does not
            o = (a + b) * 0.5f - t.up * 0.45f;
        }

        /// <summary>The table's occlusion row for slot n (x v, y on).</summary>
        /// <summary>Tools (the cost probe): the per-pixel beams read the reach (false: only the pools do).</summary>
        public static bool OccTable = true;
        static Vector4 OccSlot(CarLights l)
        {
            if (!OccTable) return Vector4.zero;
            float v = l.OccV;
            if (v < 0f) return Vector4.zero;
            var oxz = l.OccOriginXZ();
            return new Vector4(v, 1f, oxz.x, oxz.y);
        }

        static void OccPushTable(int n)
        {
            for (int i = n; i < MaxLights; i++) gOcc[i] = Vector4.zero;
            Shader.SetGlobalVectorArray(OccId, gOcc);
        }

        /// <summary>Tools (the leak probe): no car in the per-pixel table, every car's light from the pools.</summary>
        public static bool ToolPoolOnly;
        static Vector2 poolOriginAt; static float poolSpanAt;
        internal static void PoolWindowWas(Vector2 o, float span) { poolOriginAt = o; poolSpanAt = span; }
        /// <summary>Tools: the pool splat's R (the beams' light, before the halogen) at p, or -1.</summary>
        public static float PoolAt(Vector3 p)
        {
            if (poolTex == null || !poolTex.IsCreated() || poolSpanAt <= 0f) return -1f;
            int x = Mathf.FloorToInt((p.x - poolOriginAt.x) / PoolTexelM), z = Mathf.FloorToInt((p.z - poolOriginAt.y) / PoolTexelM);
            if (x < 0 || z < 0 || x >= PoolRes || z >= PoolRes) return -1f;
            var req = UnityEngine.Rendering.AsyncGPUReadback.Request(poolTex, 0, x, 1, z, 1, 0, 1, TextureFormat.RGBAFloat, null);
            req.WaitForCompletion();
            return req.hasError ? -1f : req.GetData<Color>()[0].r;
        }

        /// <summary>Tools (the leak probe): a car's reach on the bearing toward p (m, eased), or -1.</summary>
        public float OccReachToward(Vector3 p)
        {
            if (occCur == null) return -1f;
            var t = transform;
            var d = p - t.position;
            float a = Mathf.Atan2(Vector3.Dot(d, t.right), Vector3.Dot(d, t.forward));
            float u = 0.5f + a / (2f * OccHalfDeg * Mathf.Deg2Rad);
            int b = Mathf.Clamp(Mathf.FloorToInt(u * OccBins), 0, OccBins - 1);
            return occCur[b];
        }
    }
}
