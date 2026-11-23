using Unity.Collections;
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

        /// <summary>Cast every lit car's fan and fill the texture (PushGlobals, after the sort).</summary>
        static void OccUpdate(Camera cam)
        {
            for (int c = 0; c < sorted.Count; c++) sorted[c].occRow = -1;
            if (!OccOn || cam == null || sorted.Count == 0) { OccRays = 0; return; }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Vector3 eye = cam.transform.position;
            float span = PoolRes * PoolTexelM * 0.5f;
            int cars = 0;
            for (int c = 0; c < sorted.Count && cars < OccRows; c++)
            {
                var l = sorted[c];
                if ((l.transform.position - eye).sqrMagnitude > span * span) continue;
                l.occRow = cars++;
            }
            if (cars == 0) { OccRays = 0; return; }
            if (occTex == null)
            {
                occTex = new Texture2D(OccBins, OccRows, TextureFormat.RHalf, false, true)
                { name = "PSXHeadVis", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                occData = new ushort[OccBins * OccRows];
            }
            Physics.SyncTransforms();
            // a car within 80 m of the eye every frame; the rest every other frame (alternating by row),
            // each eased by the time since its own last look
            int cast = 0;
            for (int c = 0; c < sorted.Count; c++)
            {
                var l = sorted[c];
                l.occCast = -1;
                if (l.occRow < 0) continue;
                bool due = l.occCur == null || l.occFrame < Time.frameCount - 3 || ((l.occRow + Time.frameCount) & 1) == 0
                           || (l.transform.position - eye).sqrMagnitude < 80f * 80f;
                if (due) l.occCast = cast++;
            }
            int total = cast * OccBins;
            var cmds = new NativeArray<RaycastCommand>(total, Allocator.TempJob);
            var hits = new NativeArray<RaycastHit>(total * 2, Allocator.TempJob);
            var qp = new QueryParameters(~((1 << 2) | (1 << 10)), false, QueryTriggerInteraction.Ignore, false);
            float half = OccHalfDeg * Mathf.Deg2Rad;
            for (int c = 0; c < sorted.Count; c++)
            {
                var l = sorted[c];
                if (l.occCast < 0) continue;
                l.OccOrigin(out var o, out var f, out var r);
                for (int b = 0; b < OccBins; b++)
                {
                    float a = ((b + 0.5f) / OccBins - 0.5f) * 2f * half;
                    var dir = f * Mathf.Cos(a) + r * Mathf.Sin(a);
                    cmds[l.occCast * OccBins + b] = new RaycastCommand(o, dir, qp, BeamRange + 4f);
                }
            }
            if (total > 0) RaycastCommand.ScheduleBatch(cmds, hits, 32, 2).Complete();
            float now = Time.unscaledTime;
            for (int c = 0; c < sorted.Count; c++)
            {
                var l = sorted[c];
                if (l.occRow < 0) continue;
                if (l.occCast < 0)
                {
                    // not looked at this frame: its last reaches, in this frame's row
                    for (int b = 0; b < OccBins; b++) occData[l.occRow * OccBins + b] = Mathf.FloatToHalf(l.occCur[b]);
                    continue;
                }
                var up = l.transform.up;
                bool snap = l.occCur == null || l.occFrame < Time.frameCount - 3;
                if (l.occCur == null) l.occCur = new float[OccBins];
                float k = snap ? 1f : 1f - Mathf.Exp(-Mathf.Min(now - l.occAt, 0.1f) / Mathf.Max(OccEaseS, 1e-3f));
                l.occFrame = Time.frameCount; l.occAt = now;
                for (int b = 0; b < OccBins; b++)
                {
                    int ci = l.occCast * OccBins + b;
                    float reach = OccClear;
                    for (int m = 0; m < 2; m++)
                    {
                        var h = hits[ci * 2 + m];
                        if (h.normal == Vector3.zero) continue;
                        // the road rising ahead (a sag, the foot of a hill) is the road, not a wall
                        if (Vector3.Dot(h.normal, up) > 0.8f) continue;
                        reach = Mathf.Min(reach, h.distance);
                    }
                    float cur = l.occCur[b];
                    cur = snap ? reach : cur + (reach - cur) * k;
                    l.occCur[b] = cur;
                    occData[l.occRow * OccBins + b] = Mathf.FloatToHalf(cur);
                }
            }
            cmds.Dispose(); hits.Dispose();
            occTex.SetPixelData(occData, 0);
            occTex.Apply(false, false);
            Shader.SetGlobalTexture(OccTexId, occTex);
            OccRays = total;
            OccMs = (float)clock.Elapsed.TotalMilliseconds;
        }

        /// <summary>Between the two headlamps, a hair under them; the car's own forward and right.</summary>
        void OccOrigin(out Vector3 o, out Vector3 f, out Vector3 r)
        {
            var t = transform;
            f = t.forward; r = t.right;
            var a = beams[0] != null ? beams[0].transform.position : t.position + t.up * 0.65f;
            var b = beams[1] != null ? beams[1].transform.position : a;
            o = (a + b) * 0.5f - t.up * 0.05f;
        }

        /// <summary>The table's occlusion row for slot n (x v, y on).</summary>
        static Vector4 OccSlot(CarLights l)
        {
            float v = l.OccV;
            return v >= 0f ? new Vector4(v, 1f, 0f, 0f) : Vector4.zero;
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
