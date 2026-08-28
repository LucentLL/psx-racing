using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The black lines a sliding tyre leaves on the tarmac.
    ///
    /// One mesh per car, in WORLD space, on a GameObject of its own at the
    /// origin — a strip parented to the car would drag its own history around
    /// with it, which is the classic way this effect ships broken.
    ///
    /// Each wheel owns a ring of quads inside that one mesh. A ring rather than
    /// a growing list because this runs for a whole race: at a segment every
    /// 30 cm, an eight-minute stint of drifting would be tens of thousands of
    /// quads, and the oldest of them are half a lap behind the player. When the
    /// ring wraps, the segments about to be overwritten fade out first, so the
    /// end of the trail dissolves instead of snapping off.
    ///
    /// Quads are INDEPENDENT (four vertices each) rather than a shared strip:
    /// a strip cannot be broken, and this one has to be, every time a wheel
    /// leaves the ground, stops sliding, or is teleported back to the racing
    /// line by the stuck watchdog. Consecutive quads still meet seamlessly
    /// because each one is built from the previous sample's position AND its
    /// alpha, so the shared edge is identical on both sides of it.
    ///
    /// The mesh is uploaded at most ONCE PER FRAME, and only on frames where a
    /// segment was actually laid. Vertex buffers upload whole, so pushing on
    /// every segment would mean a 74 KB upload per wheel per 30 cm — about
    /// 200 of them a second at speed, for a car that is not even sliding most
    /// of the time.
    /// </summary>
    public class SkidMarks : MonoBehaviour
    {
        public CarController car;
        public Material material;

        /// <summary>Quads per wheel. At the segment lengths below this is
        /// roughly 60 m of trail for the player and 20 for an opponent, which
        /// is as far back as either is ever looked at.</summary>
        public int capacity = 192;
        /// <summary>Mark width in metres. A little narrower than a tyre: the
        /// shoulders of a tread carry less load than its middle and mark
        /// lighter, and a full-width black band reads as paint.</summary>
        public float width = 0.24f;

        /// <summary>Scrub speed at which a tyre starts leaving anything, and
        /// the speed at which it leaves everything it is going to. Both in m/s
        /// of rubber over tarmac — see CarController.WheelContact.slide, which
        /// is already zero for a tyre working inside its grip envelope.</summary>
        const float SlideStart = 1.1f;
        const float SlideFull = 5.0f;

        /// <summary>Darkest a mark gets. Not 1: a skid is soot on grey tarmac,
        /// and full black reads as a hole in the road.</summary>
        const float MaxAlpha = 0.85f;

        /// <summary>How far the tyre travels before a new quad is laid, and how
        /// that grows with speed. A fixed 30 cm is a segment every 10 ms at
        /// 30 m/s, which spends the whole ring in two seconds.</summary>
        const float MinSegment = 0.28f;
        const float SegmentPerSpeed = 0.016f;

        /// <summary>A jump further than this in one sample is not driving, it
        /// is a respawn — break the strip rather than drawing a line across the
        /// map to wherever the car reappeared.</summary>
        const float BreakDistance = 4f;

        /// <summary>Lift off the road surface, in metres. The depth offset in
        /// PSX/Decal does the real work; this stops a mark sinking into a
        /// surface whose collision mesh is coarser than its visible one.</summary>
        const float Lift = 0.015f;

        /// <summary>Segments at the tail that dissolve as the ring comes round
        /// to overwrite them.</summary>
        const int FadeSegments = 16;

        Mesh mesh;
        MeshFilter filter;
        GameObject holder;

        Vector3[] verts;
        Vector2[] uvs;
        Color32[] colors;
        /// <summary>Per-segment alpha at its two ends, kept so the tail fade can
        /// be re-derived without remembering what the road looked like.</summary>
        float[] segA0, segA1;

        /// <summary>Write cursor per wheel, counting up forever; the ring slot is
        /// this modulo <see cref="capacity"/>. Kept unwrapped so a segment's age
        /// is a subtraction rather than a modular comparison.</summary>
        readonly int[] head = new int[4];
        readonly bool[] laid = new bool[4];
        readonly Vector3[] lastPoint = new Vector3[4];
        readonly Vector3[] lastEdge = new Vector3[4];
        readonly float[] lastAlpha = new float[4];

        bool dirty;
        float staticLoad = 3000f;
        /// <summary>Distance travelled along the road, for the tread pattern's
        /// V coordinate. Per wheel, so two wheels laying marks side by side do
        /// not have their tread in lockstep.</summary>
        readonly float[] runLength = new float[4];

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            if (car != null) staticLoad = Mathf.Max(500f, car.massKg * 9.81f * 0.25f);

            capacity = Mathf.Clamp(capacity, 16, 1024);
            int quads = capacity * 4;
            verts = new Vector3[quads * 4];
            uvs = new Vector2[quads * 4];
            colors = new Color32[quads * 4];
            segA0 = new float[quads];
            segA1 = new float[quads];

            var tris = new int[quads * 6];
            for (int q = 0; q < quads; q++)
            {
                int v = q * 4, t = q * 6;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }

            mesh = new Mesh { name = "SkidMarks" };
            // Above 65k vertices the 16-bit index buffer silently wraps. A
            // 1024-quad ring is 16k, so this is headroom rather than a fix, but
            // the failure mode is triangles fanning to the origin and it is not
            // worth leaving to a future change of `capacity`.
            mesh.indexFormat = quads * 4 > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.MarkDynamic();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors32 = colors;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);

            // Its own object at the world origin. Parenting to the car would
            // make every mark follow the car that laid it; parenting to the
            // scene root and leaving the transform at identity means the
            // vertices ARE world positions and nothing has to be inverted.
            holder = new GameObject(name + " Marks");
            holder.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            filter = holder.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var mr = holder.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        void OnDestroy()
        {
            // The holder is not a child of this car, so nothing else will take
            // it down with the scene object it belongs to.
            if (holder != null) Destroy(holder);
            if (mesh != null) Destroy(mesh);
        }

        void LateUpdate()
        {
            if (car == null || material == null) return;

            for (int i = 0; i < 4; i++) Step(i);

            if (!dirty) return;
            dirty = false;
            FadeTails();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors32 = colors;
            // Bounds grown from the segments actually laid, NOT
            // RecalculateBounds. Every slot in the ring exists from Awake, and
            // the ones nothing has been written into yet sit at the world
            // origin — so a recalculated box always stretches from the origin
            // to wherever the car is, which on Charlotte is a bounding box
            // eight kilometres across. Growing it by hand also never shrinks
            // it, which is the safe direction: the worst case is a mesh that
            // fails to cull on a track it has marks all over anyway.
            mesh.bounds = grown;
        }

        Bounds grown = new Bounds(Vector3.zero, Vector3.one * 4f);
        bool anyLaid;

        void Step(int i)
        {
            var c = car.wheelContacts[i];

            // Rubber marks tarmac. On grass and dirt a sliding tyre throws a
            // cloud rather than drawing a line, and that is TireSmoke's job —
            // laying black rubber across a field is the version of this effect
            // everyone recognises as wrong.
            float a = c.grounded && c.onRoad
                ? Mathf.Clamp01((c.slide - SlideStart) / (SlideFull - SlideStart)) *
                  Mathf.Clamp01(c.load / staticLoad) * MaxAlpha
                : 0f;

            if (a <= 0.01f) { laid[i] = false; return; }

            Vector3 p = c.point + c.normal * Lift;
            // Across the direction of travel, in the plane of the road. Taken
            // from the WHEEL's heading, not the car's: a locked front wheel on
            // opposite lock leaves its mark square to the tyre.
            Vector3 side = Vector3.Cross(c.normal, c.forward);
            if (side.sqrMagnitude < 1e-6f) { laid[i] = false; return; }
            Vector3 edge = side.normalized * (width * 0.5f);

            if (!laid[i])
            {
                laid[i] = true;
                lastPoint[i] = p; lastEdge[i] = edge; lastAlpha[i] = 0f;
                return;
            }

            float d = Vector3.Distance(p, lastPoint[i]);
            if (d > BreakDistance) { laid[i] = false; return; }

            float speed = car.Body != null ? car.Body.linearVelocity.magnitude : 0f;
            if (d < MinSegment + speed * SegmentPerSpeed) return;

            Emit(i, p, edge, a, d);
            lastPoint[i] = p; lastEdge[i] = edge; lastAlpha[i] = a;
        }

        void Emit(int i, Vector3 p, Vector3 edge, float a, float length)
        {
            int slot = head[i] % capacity;
            int seg = i * capacity + slot;
            int v = seg * 4;

            float v0 = runLength[i] / TreadPeriod;
            runLength[i] += length;
            float v1 = runLength[i] / TreadPeriod;

            verts[v] = lastPoint[i] - lastEdge[i];
            verts[v + 1] = lastPoint[i] + lastEdge[i];
            verts[v + 2] = p + edge;
            verts[v + 3] = p - edge;

            if (!anyLaid) { anyLaid = true; grown = new Bounds(verts[v], Vector3.one * 0.5f); }
            for (int k = 0; k < 4; k++) grown.Encapsulate(verts[v + k]);

            uvs[v] = new Vector2(0f, v0);
            uvs[v + 1] = new Vector2(1f, v0);
            uvs[v + 2] = new Vector2(1f, v1);
            uvs[v + 3] = new Vector2(0f, v1);

            segA0[seg] = lastAlpha[i];
            segA1[seg] = a;

            head[i]++;
            dirty = true;
        }

        /// <summary>Metres of road per repeat of the tread texture.</summary>
        const float TreadPeriod = 1.4f;

        /// <summary>
        /// Colour the segment just laid, and dissolve the ones the ring is
        /// about to overwrite.
        ///
        /// "About to be overwritten" is a subtraction, not a timestamp: the
        /// slot `back` places behind the write cursor gets reused after
        /// exactly `capacity - 1 - back` more writes, so that count IS the
        /// segment's remaining life and the last few of them are the tail. The
        /// middle of the ring is at the alpha it was laid with and nothing
        /// here has to touch it.
        /// </summary>
        void FadeTails()
        {
            for (int i = 0; i < 4; i++)
            {
                int written = Mathf.Min(head[i], capacity);
                for (int back = 0; back < written; back++)
                {
                    int remaining = capacity - 1 - back;
                    // The newest segment has never had a colour written at all;
                    // everything else outside the tail window already has the
                    // right one.
                    if (remaining >= FadeSegments && back != 0) continue;

                    float fade = remaining >= FadeSegments
                        ? 1f : (remaining + 1f) / (FadeSegments + 1f);
                    int slot = (head[i] - 1 - back) % capacity;
                    if (slot < 0) slot += capacity;
                    int seg = i * capacity + slot;
                    WriteColor(seg, segA0[seg] * fade, segA1[seg] * fade);
                }
            }
        }

        void WriteColor(int seg, float a0, float a1)
        {
            int v = seg * 4;
            var c0 = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a0) * 255f));
            var c1 = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a1) * 255f));
            colors[v] = c0; colors[v + 1] = c0;
            colors[v + 2] = c1; colors[v + 3] = c1;
        }
    }
}
