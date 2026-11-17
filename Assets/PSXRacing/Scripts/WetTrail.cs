using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE DAMP TRAIL (RAIN 2, 2026-11-16; the brief's "the tyres leave a wake
    /// and a damp trail"). A tyre rolling over a wet road pushes the film
    /// aside: for a second or two the strip behind it holds less water than
    /// the road round it, so it is darker and has lost its mirror, and then
    /// the water closes over it again. That is drawn here: a short ribbon
    /// behind each wheel of every car near the camera, on a wet road, laid by
    /// distance (longer pieces the faster the car goes), each piece fading
    /// out over <see cref="Life"/> seconds. In standing water the strip is
    /// far fainter - a pool closes at once (the sheet and the burst thrown
    /// there are TireSpray's).
    ///
    /// Added by WeatherFx with the spray; like it, nothing is built until a
    /// car actually lays a piece, the object is NOT a child of the car, and
    /// the mesh's bounds are set by hand (a pre-allocated world-space mesh
    /// recalculates to a box reaching the origin: the tyre-marks' trap). One
    /// shared material (PSX/Decal: unlit, vertex alpha, the road's own fog),
    /// one draw per car that has a trail. Rain2.Trails off (or PSX_RAIN2=0):
    /// nothing is added.
    /// </summary>
    [DisallowMultipleComponent]
    public class WetTrail : MonoBehaviour
    {
        public CarController car;

        /// <summary>Pieces per wheel; a piece's life; the strip's width; its lift off the road.</summary>
        public const int Segs = 36;
        public const float Life = 1.7f, Width = 0.21f, Lift = 0.012f;
        /// <summary>Cars further than this from the camera lay nothing.</summary>
        public const float Reach = 90f;
        /// <summary>How dark the strip is on the film, and in standing water.</summary>
        public const float AlphaFilm = 0.30f, AlphaPool = 0.08f;
        const float MinSeg = 0.45f, SegPerSpeed = 0.028f, BreakDist = 5f, FadeEvery = 0.05f, MinSpeed = 2f;

        static Material sharedMat;
        static Texture2D sharedTex;

        GameObject holder;
        Mesh mesh;
        Vector3[] verts;
        Color32[] cols;
        readonly float[] born = new float[4 * Segs];
        readonly byte[] baseA = new byte[4 * Segs];
        readonly Vector3[] lastP = new Vector3[4], lastEdge = new Vector3[4];
        readonly bool[] have = new bool[4];
        readonly int[] next = new int[4];
        bool anyLive, dirty;
        float nextFade;

        /// <summary>For the checks: pieces laid since this car got its trail.</summary>
        public int Laid { get; private set; }

        void Awake() { if (car == null) car = GetComponent<CarController>(); }
        void OnDestroy()
        {
            if (holder != null) Destroy(holder);
            if (mesh != null) Destroy(mesh);
        }

        void LateUpdate()
        {
            if (!Rain2.Measure) { Step(); return; }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp(), a0 = System.GC.GetAllocatedBytesForCurrentThread();
            Step();
            Rain2.SprayTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            Rain2.SprayAlloc += System.GC.GetAllocatedBytesForCurrentThread() - a0;
        }

        void Step()
        {
            if (car == null) return;
            bool on = Rain2.On && Rain2.Trails;
            var contacts = car.wheelContacts;
            float speed = Mathf.Abs(car.speedKmh) / 3.6f;
            bool near = false;
            if (on && speed >= MinSpeed && contacts != null && contacts.Length >= 4)
            {
                var cam = Camera.main;
                near = cam != null && (cam.transform.position - car.transform.position).sqrMagnitude < Reach * Reach;
            }
            if (!near) { have[0] = have[1] = have[2] = have[3] = false; }
            else
            {
                float segLen = Mathf.Max(MinSeg, speed * SegPerSpeed);
                for (int k = 0; k < 4; k++)
                {
                    var c = contacts[k];
                    if (!c.grounded || !c.onRoad || c.wetT < 0.3f) { have[k] = false; continue; }
                    Vector3 n = c.normal.sqrMagnitude > 1e-6f ? c.normal.normalized : Vector3.up;
                    Vector3 fwd = c.forward.sqrMagnitude > 1e-6f ? c.forward.normalized : car.transform.forward;
                    Vector3 edge = Vector3.Cross(n, fwd);
                    edge = edge.sqrMagnitude > 1e-6f ? edge.normalized * (Width * 0.5f) : car.transform.right * (Width * 0.5f);
                    Vector3 p = c.point + n * Lift;
                    if (!have[k]) { lastP[k] = p; lastEdge[k] = edge; have[k] = true; continue; }
                    float d = (p - lastP[k]).magnitude;
                    if (d > BreakDist) { lastP[k] = p; lastEdge[k] = edge; continue; }      // a jump, a teleport
                    if (d < segLen) continue;
                    if (mesh == null && !Build()) return;
                    int s = k * Segs + next[k];
                    next[k] = (next[k] + 1) % Segs;
                    int v = s * 4;
                    verts[v] = lastP[k] - lastEdge[k]; verts[v + 1] = lastP[k] + lastEdge[k];
                    verts[v + 2] = p + edge; verts[v + 3] = p - edge;
                    born[s] = Time.time;
                    float pool = Rain2.PoolLevel > 0.001f ? Rain2.PoolAt(c.point, n) : 0f;
                    baseA[s] = (byte)(255f * Mathf.Lerp(AlphaFilm, AlphaPool, pool) * Mathf.Clamp01(c.wetT));
                    lastP[k] = p; lastEdge[k] = edge;
                    dirty = true; anyLive = true;
                    Laid++;
                }
            }

            if (mesh == null || (!anyLive && !dirty)) return;
            float now = Time.time;
            if (!dirty && now < nextFade) return;
            nextFade = now + FadeEvery;
            bool live = false;
            for (int s = 0; s < born.Length; s++)
            {
                float age = now - born[s];
                byte a = 0;
                if (baseA[s] != 0 && age < Life)
                {
                    float f = 1f - age / Life;
                    a = (byte)(baseA[s] * f * f);
                    live = true;
                }
                else baseA[s] = 0;
                int v = s * 4;
                if (cols[v].a == a) continue;
                var c32 = new Color32(255, 255, 255, a);
                cols[v] = c32; cols[v + 1] = c32; cols[v + 2] = c32; cols[v + 3] = c32;
            }
            anyLive = live;
            if (dirty)
            {
                mesh.SetVertices(verts);
                mesh.bounds = new Bounds(car.transform.position, new Vector3(220f, 60f, 220f));
                dirty = false;
            }
            mesh.SetColors(cols);
            if (holder.activeSelf != live) holder.SetActive(live);
        }

        bool Build()
        {
            var mat = TrailMaterial();
            if (mat == null) { enabled = false; return false; }
            int quads = 4 * Segs;
            verts = new Vector3[quads * 4];
            cols = new Color32[quads * 4];
            var uv = new Vector2[quads * 4];
            var tris = new int[quads * 6];
            for (int q = 0; q < quads; q++)
            {
                int v = q * 4, t = q * 6;
                uv[v] = new Vector2(0f, 0.5f); uv[v + 1] = new Vector2(1f, 0.5f);
                uv[v + 2] = new Vector2(1f, 0.5f); uv[v + 3] = new Vector2(0f, 0.5f);
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            mesh = new Mesh { name = "WetTrail" };
            mesh.MarkDynamic();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uv);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0, false);
            mesh.bounds = new Bounds(car.transform.position, new Vector3(220f, 60f, 220f));
            holder = new GameObject(car.name + " WetTrail") { layer = CarReflections.NoReflectLayer };
            holder.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = holder.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return true;
        }

        static Material TrailMaterial()
        {
            if (sharedMat != null) return sharedMat;
            var shader = Shader.Find("PSX/Decal");
            if (shader == null) return null;
            if (sharedTex == null)
            {
                // soft-edged across, and (the tyre marks' lesson) a high MEAN: a
                // strip a few pixels wide is read from a deep mip
                const int n = 16;
                sharedTex = new Texture2D(n, 4, TextureFormat.RGBA32, true)
                {
                    name = "WetTrail strip", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                var px = new Color32[n * 4];
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n;
                        float a = Mathf.Clamp01(Mathf.Min(u, 1f - u) / 0.22f);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(255f * a));
                    }
                sharedTex.SetPixels32(px);
                sharedTex.Apply(true, true);
            }
            sharedMat = new Material(shader)
            {
                name = "WetTrail (runtime)", hideFlags = HideFlags.HideAndDontSave, mainTexture = sharedTex,
                // under the tyre marks (2800) and the blob shadow (2900): after the wet road's two passes
                renderQueue = 2790,
            };
            // damp tarmac with the film pushed off it: near black, a touch cool
            sharedMat.SetColor("_Tint", new Color(0.012f, 0.013f, 0.016f, 1f));
            return sharedMat;
        }
    }
}
