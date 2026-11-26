using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.People
{
    /// <summary>
    /// THE TEST MANNEQUIN (people, Stage 1 of Characters_Unity_Plan, 2026-11-26): a figure built in code
    /// that costs what the ordered art will cost, so the three ways of drawing people can be measured
    /// before any art exists. It follows the art brief's numbers ("Skeleton and binding", "Budgets"):
    ///   - the 45-bone layout of every figure that is not the driver (Root, Hips, Spine, Chest, UpperChest,
    ///     Neck, Head, Eye_L/R; per arm Shoulder, UpperArm, LowerArm, Hand and two bones a finger; per leg
    ///     UpperLeg, LowerLeg, Foot, Toes), an A-pose rest;
    ///   - about 5,000 triangles and 4 weights a vertex on PC, 1,800 and 2 on the non-PC profile;
    ///   - one material on one texture, 1024 x 1024 on PC, 256 x 256 otherwise (block-compressed, mips);
    ///   - a looping walk (1.0 s, 1.35 m/s) and a looping idle (6 s), made from bone curves, 30 frames a second.
    /// It is a body of tapered tubes, a ball for a head and two-bone fingers: a cost stand-in, not art.
    /// TEST ONLY: nothing builds one unless a perf tour or PSX_PEOPLE_TEST asks (PeopleCrowd).
    /// </summary>
    public static class Mannequin
    {
        public const int Fps = 30;
        public const float WalkCycle = 1.0f, WalkSpeed = 1.35f, IdleCycle = 6.0f;
        public const int WalkFrames = 30, IdleFrames = 180;
        /// <summary>Rows of the baked bone texture: the walk first, then the idle.</summary>
        public const int WalkRow0 = 0, IdleRow0 = WalkFrames;

        public struct Bone
        {
            public string name;
            public int parent;
            public Vector3 local;   // rest offset from the parent (the rest pose has no rotation)
        }

        static Bone[] bones;
        public static Bone[] Bones { get { if (bones == null) bones = MakeBones(); return bones; } }
        public static int BoneCount => Bones.Length;

        static readonly string[] Fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };

        static Bone[] MakeBones()
        {
            var l = new List<Bone>();
            int Add(string n, int p, Vector3 v) { l.Add(new Bone { name = n, parent = p, local = v }); return l.Count - 1; }
            int root = Add("Root", -1, Vector3.zero);
            int hips = Add("Hips", root, new Vector3(0f, 0.95f, 0f));
            int spine = Add("Spine", hips, new Vector3(0f, 0.10f, 0f));
            int chest = Add("Chest", spine, new Vector3(0f, 0.15f, 0f));
            int upper = Add("UpperChest", chest, new Vector3(0f, 0.15f, 0f));
            int neck = Add("Neck", upper, new Vector3(0f, 0.13f, 0f));
            int head = Add("Head", neck, new Vector3(0f, 0.10f, 0f));
            Add("Eye_L", head, new Vector3(-0.032f, 0.07f, 0.08f));
            Add("Eye_R", head, new Vector3(0.032f, 0.07f, 0.08f));
            // A-pose: the arm 45 degrees down from the shoulder
            for (int side = 0; side < 2; side++)
            {
                float s = side == 0 ? -1f : 1f;
                string suf = side == 0 ? "_L" : "_R";
                var down = new Vector3(s * 0.7071f, -0.7071f, 0f);
                int sh = Add("Shoulder" + suf, upper, new Vector3(s * 0.05f, 0.08f, 0f));
                int ua = Add("UpperArm" + suf, sh, new Vector3(s * 0.13f, 0f, 0f));
                int la = Add("LowerArm" + suf, ua, down * 0.28f);
                int hand = Add("Hand" + suf, la, down * 0.26f);
                float[] spread = { 0.035f, 0.022f, 0.004f, -0.014f, -0.030f };
                for (int f = 0; f < 5; f++)
                {
                    var b0 = down * (f == 0 ? 0.035f : 0.085f) + new Vector3(0f, 0f, spread[f]);
                    int f1 = Add(Fingers[f] + "1" + suf, hand, b0);
                    Add(Fingers[f] + "2" + suf, f1, down * (f == 0 ? 0.035f : 0.04f));
                }
            }
            for (int side = 0; side < 2; side++)
            {
                float s = side == 0 ? -1f : 1f;
                string suf = side == 0 ? "_L" : "_R";
                int ul = Add("UpperLeg" + suf, hips, new Vector3(s * 0.09f, -0.05f, 0f));
                int ll = Add("LowerLeg" + suf, ul, new Vector3(0f, -0.42f, 0f));
                int ft = Add("Foot" + suf, ll, new Vector3(0f, -0.40f, 0f));
                Add("Toes" + suf, ft, new Vector3(0f, -0.05f, 0.13f));
            }
            return l.ToArray();
        }

        public static int IndexOf(string n)
        {
            var b = Bones;
            for (int i = 0; i < b.Length; i++) if (b[i].name == n) return i;
            return -1;
        }

        /// <summary>Every bone's rest position in the model (the figure's own space).</summary>
        public static Vector3[] RestWorld()
        {
            var b = Bones;
            var w = new Vector3[b.Length];
            for (int i = 0; i < b.Length; i++) w[i] = (b[i].parent < 0 ? Vector3.zero : w[b[i].parent]) + b[i].local;
            return w;
        }

        // ------------------------------------------------------------------
        //  the mesh
        // ------------------------------------------------------------------

        struct Part
        {
            public int bone, child;      // child -1: no blend toward a child at the far end
            public Vector3 a, b;         // ends in the model
            public float ra, rb;         // radii
            public int radial, rings;    // the base resolution (scaled by detail)
            public bool ball;            // a ball (the head) centred between a and b
        }

        static List<Part> Parts()
        {
            var w = RestWorld();
            var p = new List<Part>();
            void Tube(string bone, string child, Vector3 a, Vector3 b, float ra, float rb, int radial, int rings)
                => p.Add(new Part { bone = IndexOf(bone), child = child == null ? -1 : IndexOf(child), a = a, b = b, ra = ra, rb = rb, radial = radial, rings = rings });
            Vector3 W(string n) => w[IndexOf(n)];
            Tube("Hips", "Spine", W("Hips") + new Vector3(0f, -0.11f, 0f), W("Spine"), 0.15f, 0.14f, 14, 4);
            Tube("Spine", "Chest", W("Spine"), W("Chest"), 0.14f, 0.15f, 14, 3);
            Tube("Chest", "UpperChest", W("Chest"), W("UpperChest"), 0.15f, 0.17f, 14, 3);
            Tube("UpperChest", "Neck", W("UpperChest"), W("Neck") + new Vector3(0f, -0.02f, 0f), 0.17f, 0.12f, 14, 3);
            Tube("Neck", "Head", W("Neck") - new Vector3(0f, 0.02f, 0f), W("Head") + new Vector3(0f, 0.02f, 0f), 0.055f, 0.05f, 10, 2);
            p.Add(new Part { bone = IndexOf("Head"), child = -1, a = W("Head") + new Vector3(0f, 0.01f, 0f), b = W("Head") + new Vector3(0f, 0.21f, 0f), ra = 0.10f, rb = 0.10f, radial = 16, rings = 12, ball = true });
            foreach (var suf in new[] { "_L", "_R" })
            {
                Tube("Shoulder" + suf, "UpperArm" + suf, W("Shoulder" + suf), W("UpperArm" + suf), 0.065f, 0.06f, 10, 2);
                Tube("UpperArm" + suf, "LowerArm" + suf, W("UpperArm" + suf), W("LowerArm" + suf), 0.055f, 0.045f, 10, 4);
                Tube("LowerArm" + suf, "Hand" + suf, W("LowerArm" + suf), W("Hand" + suf), 0.045f, 0.033f, 10, 4);
                Tube("Hand" + suf, null, W("Hand" + suf), W("Middle1" + suf), 0.038f, 0.034f, 8, 2);
                foreach (var f in Fingers)
                {
                    Tube(f + "1" + suf, f + "2" + suf, W(f + "1" + suf), W(f + "2" + suf), 0.010f, 0.009f, 5, 1);
                    var tip = W(f + "2" + suf) + (W(f + "2" + suf) - W(f + "1" + suf)).normalized * 0.03f;
                    Tube(f + "2" + suf, null, W(f + "2" + suf), tip, 0.009f, 0.007f, 5, 1);
                }
                Tube("UpperLeg" + suf, "LowerLeg" + suf, W("UpperLeg" + suf), W("LowerLeg" + suf), 0.085f, 0.06f, 12, 5);
                Tube("LowerLeg" + suf, "Foot" + suf, W("LowerLeg" + suf), W("Foot" + suf), 0.058f, 0.042f, 12, 5);
                Tube("Foot" + suf, "Toes" + suf, W("Foot" + suf) + new Vector3(0f, -0.02f, -0.04f), W("Toes" + suf), 0.048f, 0.04f, 10, 3);
                Tube("Toes" + suf, null, W("Toes" + suf), W("Toes" + suf) + new Vector3(0f, 0f, 0.07f), 0.04f, 0.03f, 8, 1);
            }
            return p;
        }

        static int Res(int baseN, float k, int min) => Mathf.Max(min, Mathf.RoundToInt(baseN * k));

        static int TrisAt(List<Part> parts, float k)
        {
            int t = 0;
            foreach (var p in parts)
            {
                int r = Res(p.radial, k, 3), n = Res(p.rings, k, 1);
                t += p.ball ? 2 * r * (n - 1) : 2 * r * n + 2 * (r - 2);
            }
            return t;
        }

        /// <summary>
        /// The mesh at about <paramref name="targetTris"/> triangles with up to <paramref name="maxWeights"/>
        /// bones a vertex. <paramref name="baked"/>: the bone indices and weights in uv3 / uv4 for the
        /// baked-bone shader (technique B) instead of Unity's bone weights.
        /// </summary>
        public static Mesh Build(int targetTris, int maxWeights, bool baked, out int tris)
        {
            var parts = Parts();
            float lo = 0.2f, hi = 6f;
            for (int i = 0; i < 30; i++) { float m = (lo + hi) * 0.5f; if (TrisAt(parts, m) > targetTris) hi = m; else lo = m; }
            float k = lo;
            var b = Bones;
            var verts = new List<Vector3>(); var nrms = new List<Vector3>(); var uvs = new List<Vector2>();
            var idx = new List<int>(); var bw = new List<BoneWeight>();
            int cell = 0;
            const int cellsAcross = 8;
            foreach (var p in parts)
            {
                int r = Res(p.radial, k, 3), n = Res(p.rings, k, 1);
                Vector3 axis = p.b - p.a; float len = axis.magnitude; Vector3 dir = len > 1e-5f ? axis / len : Vector3.up;
                Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
                Vector3 up2 = Vector3.Cross(side, dir);
                float cu = (cell % cellsAcross) / (float)cellsAcross, cv = (cell / cellsAcross) / (float)cellsAcross;
                cell++;
                int start = verts.Count;
                int parent = b[p.bone].parent;
                if (p.ball)
                {
                    Vector3 c = (p.a + p.b) * 0.5f;
                    for (int ring = 0; ring < n; ring++)
                    {
                        float th = Mathf.PI * ring / (n - 1);
                        for (int s = 0; s <= r; s++)
                        {
                            float ph = 2f * Mathf.PI * s / r;
                            var nr = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                            verts.Add(c + Vector3.Scale(nr, new Vector3(p.ra * 0.9f, p.ra * 1.1f, p.ra)));
                            nrms.Add(nr);
                            uvs.Add(new Vector2(cu + (s / (float)r) / cellsAcross, cv + (ring / (float)(n - 1)) / cellsAcross));
                            bw.Add(Weights(p.bone, parent, -1, ring >= n - 2 ? 0.15f : 0f, 0f, maxWeights));
                        }
                    }
                    for (int ring = 0; ring < n - 1; ring++)
                        for (int s = 0; s < r; s++)
                        {
                            int i0 = start + ring * (r + 1) + s, i1 = i0 + 1, i2 = i0 + r + 1, i3 = i2 + 1;
                            idx.Add(i0); idx.Add(i2); idx.Add(i1); idx.Add(i1); idx.Add(i2); idx.Add(i3);
                        }
                    continue;
                }
                for (int ring = 0; ring <= n; ring++)
                {
                    float f = ring / (float)n;
                    float rad = Mathf.Lerp(p.ra, p.rb, f);
                    Vector3 c = p.a + axis * f;
                    // near the start the parent shares the vertex; near the end, the child
                    float wp = f < 0.35f ? (0.35f - f) / 0.35f * 0.5f : 0f;
                    float wc = p.child >= 0 && f > 0.65f ? (f - 0.65f) / 0.35f * 0.5f : 0f;
                    for (int s = 0; s <= r; s++)
                    {
                        float ph = 2f * Mathf.PI * s / r;
                        var nr = side * Mathf.Cos(ph) + up2 * Mathf.Sin(ph);
                        verts.Add(c + nr * rad); nrms.Add(nr);
                        uvs.Add(new Vector2(cu + (s / (float)r) / cellsAcross, cv + f / cellsAcross));
                        bw.Add(Weights(p.bone, parent, p.child, wp, wc, maxWeights));
                    }
                }
                for (int ring = 0; ring < n; ring++)
                    for (int s = 0; s < r; s++)
                    {
                        int i0 = start + ring * (r + 1) + s, i1 = i0 + 1, i2 = i0 + r + 1, i3 = i2 + 1;
                        idx.Add(i0); idx.Add(i1); idx.Add(i2); idx.Add(i1); idx.Add(i3); idx.Add(i2);
                    }
                // the two caps, fans
                for (int end = 0; end < 2; end++)
                {
                    int ringStart = start + (end == 0 ? 0 : n * (r + 1));
                    for (int s = 1; s < r - 1; s++)
                    {
                        if (end == 0) { idx.Add(ringStart); idx.Add(ringStart + s + 1); idx.Add(ringStart + s); }
                        else { idx.Add(ringStart); idx.Add(ringStart + s); idx.Add(ringStart + s + 1); }
                    }
                }
            }
            var mesh = new Mesh { name = "PeopleMannequin_" + (maxWeights > 2 ? "PC" : "Phone") + (baked ? "_Baked" : "") };
            mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts); mesh.SetNormals(nrms); mesh.SetUVs(0, uvs);
            var cols = new Color32[verts.Count];
            for (int i = 0; i < cols.Length; i++) cols[i] = new Color32(255, 255, 255, 255);
            mesh.colors32 = cols;
            mesh.SetTriangles(idx, 0);
            if (baked)
            {
                var bi = new List<Vector4>(bw.Count); var bv = new List<Vector4>(bw.Count);
                foreach (var w in bw)
                {
                    bi.Add(new Vector4(w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3));
                    bv.Add(new Vector4(w.weight0, w.weight1, w.weight2, w.weight3));
                }
                mesh.SetUVs(3, bi); mesh.SetUVs(4, bv);
            }
            else
            {
                mesh.boneWeights = bw.ToArray();
                mesh.bindposes = BindPoses();
            }
            mesh.RecalculateBounds();
            // a walking figure's limbs leave the rest pose's box
            var bb = mesh.bounds; bb.Expand(new Vector3(0.6f, 0.2f, 0.9f)); mesh.bounds = bb;
            mesh.UploadMeshData(false);
            tris = idx.Count / 3;
            return mesh;
        }

        static BoneWeight Weights(int bone, int parent, int child, float wp, float wc, int maxW)
        {
            // up to four influences: the bone, its parent, its child, and the parent's parent (PC); the two
            // strongest normalised on the non-PC mesh (the art brief: 4 PC, 2 phone)
            var list = new List<(int i, float w)>();
            float wb = 1f - wp - wc;
            list.Add((bone, wb));
            if (parent >= 0 && wp > 0f) list.Add((parent, wp));
            if (child >= 0 && wc > 0f) list.Add((child, wc));
            if (maxW > 2 && parent >= 0)
            {
                int gp = Bones[parent].parent;
                if (gp >= 0 && list.Count < 4) list.Add((gp, 0.04f));
                if (list.Count < 4 && (parent >= 0) && wp <= 0f) list.Add((parent, 0.03f));
            }
            list.Sort((x, y) => y.w.CompareTo(x.w));
            int keep = Mathf.Min(maxW, list.Count);
            float sum = 0f; for (int i = 0; i < keep; i++) sum += list[i].w;
            var r = new BoneWeight();
            for (int i = 0; i < keep; i++)
            {
                float w = list[i].w / sum;
                switch (i)
                {
                    case 0: r.boneIndex0 = list[i].i; r.weight0 = w; break;
                    case 1: r.boneIndex1 = list[i].i; r.weight1 = w; break;
                    case 2: r.boneIndex2 = list[i].i; r.weight2 = w; break;
                    default: r.boneIndex3 = list[i].i; r.weight3 = w; break;
                }
            }
            return r;
        }

        public static Matrix4x4[] BindPoses()
        {
            var w = RestWorld();
            var bp = new Matrix4x4[w.Length];
            for (int i = 0; i < w.Length; i++) bp[i] = Matrix4x4.Translate(-w[i]);
            return bp;
        }

        // ------------------------------------------------------------------
        //  the two clips, as a pose function (the same numbers feed all three techniques)
        // ------------------------------------------------------------------

        /// <summary>Each bone's local rotation at time <paramref name="t"/> of the walk (clip 0) or the idle
        /// (clip 1), and the hips' local position. Every bone moves, the fingers included, so a sampler pays
        /// for all 45.</summary>
        public static void Pose(int clip, float t, Quaternion[] rot, out Vector3 hipsPos)
        {
            var b = Bones;
            for (int i = 0; i < b.Length; i++) rot[i] = Quaternion.identity;
            hipsPos = b[1].local;
            float Deg(float x) => x;
            if (clip == 0)
            {
                float p = 2f * Mathf.PI * (t / WalkCycle);
                float sp = Mathf.Sin(p), cp = Mathf.Cos(p);
                hipsPos += new Vector3(0f, 0.025f * Mathf.Cos(2f * p) - 0.02f, 0f);
                rot[IndexOf("Hips")] = Quaternion.Euler(2f, 6f * sp, 3f * cp);
                rot[IndexOf("Spine")] = Quaternion.Euler(1f, -3f * sp, -1.5f * cp);
                rot[IndexOf("Chest")] = Quaternion.Euler(1f, -4f * sp, -1f * cp);
                rot[IndexOf("UpperChest")] = Quaternion.Euler(0f, -2f * sp, 0f);
                rot[IndexOf("Neck")] = Quaternion.Euler(-2f, 3f * sp, 0f);
                rot[IndexOf("Head")] = Quaternion.Euler(2f * Mathf.Cos(2f * p), 2f * sp, 0f);
                for (int side = 0; side < 2; side++)
                {
                    float s = side == 0 ? -1f : 1f, ph = side == 0 ? 0f : Mathf.PI;
                    string suf = side == 0 ? "_L" : "_R";
                    float ls = Mathf.Sin(p + ph);
                    rot[IndexOf("UpperLeg" + suf)] = Quaternion.Euler(-24f * ls, 0f, s * 2f);
                    float knee = Mathf.Max(0f, Mathf.Sin(p + ph + 1.3f));
                    rot[IndexOf("LowerLeg" + suf)] = Quaternion.Euler(5f + 50f * knee * knee, 0f, 0f);
                    rot[IndexOf("Foot" + suf)] = Quaternion.Euler(10f * Mathf.Sin(p + ph - 0.6f), 0f, 0f);
                    rot[IndexOf("Toes" + suf)] = Quaternion.Euler(-12f * Mathf.Max(0f, Mathf.Sin(p + ph + 2.4f)), 0f, 0f);
                    // the arms swing against the legs, hanging nearer the body than the A-pose
                    rot[IndexOf("Shoulder" + suf)] = Quaternion.Euler(0f, 0f, s * 2f * Mathf.Cos(p));
                    rot[IndexOf("UpperArm" + suf)] = Quaternion.Euler(20f * ls, 0f, -s * 32f);
                    rot[IndexOf("LowerArm" + suf)] = Quaternion.Euler(-12f - 8f * Mathf.Max(0f, -ls), 0f, 0f);
                    rot[IndexOf("Hand" + suf)] = Quaternion.Euler(-4f * ls, 0f, 0f);
                    for (int f = 0; f < 5; f++)
                    {
                        float curl = (f == 0 ? 12f : 22f) + 3f * Mathf.Sin(p + f);
                        rot[IndexOf(Fingers[f] + "1" + suf)] = Quaternion.Euler(Deg(curl), 0f, 0f);
                        rot[IndexOf(Fingers[f] + "2" + suf)] = Quaternion.Euler(Deg(curl * 1.2f), 0f, 0f);
                    }
                }
            }
            else
            {
                float q = 2f * Mathf.PI * (t / IdleCycle);
                float br = Mathf.Sin(2f * q);    // a breath every 3 s
                hipsPos += new Vector3(0.015f * Mathf.Sin(q), -0.01f + 0.003f * br, 0f);
                rot[IndexOf("Hips")] = Quaternion.Euler(0f, 3f * Mathf.Sin(q), 2.5f * Mathf.Sin(q));
                rot[IndexOf("Spine")] = Quaternion.Euler(0.8f * br, 0f, -1.5f * Mathf.Sin(q));
                rot[IndexOf("Chest")] = Quaternion.Euler(1.2f * br, 0f, -0.8f * Mathf.Sin(q));
                rot[IndexOf("UpperChest")] = Quaternion.Euler(0.8f * br, 0f, 0f);
                rot[IndexOf("Neck")] = Quaternion.Euler(-1f, 6f * Mathf.Sin(q + 1f), 0f);
                rot[IndexOf("Head")] = Quaternion.Euler(3f * Mathf.Sin(2f * q + 0.5f), 8f * Mathf.Sin(q + 1.2f), 0f);
                for (int side = 0; side < 2; side++)
                {
                    float s = side == 0 ? -1f : 1f;
                    string suf = side == 0 ? "_L" : "_R";
                    float lean = side == 0 ? Mathf.Sin(q) : -Mathf.Sin(q);
                    rot[IndexOf("UpperLeg" + suf)] = Quaternion.Euler(-2f * lean, 0f, s * (3f - 2f * lean));
                    rot[IndexOf("LowerLeg" + suf)] = Quaternion.Euler(4f + 3f * Mathf.Max(0f, -lean), 0f, 0f);
                    rot[IndexOf("Foot" + suf)] = Quaternion.Euler(-1f, s * 6f, 0f);
                    rot[IndexOf("Toes" + suf)] = Quaternion.Euler(0f, 0f, 0f);
                    rot[IndexOf("Shoulder" + suf)] = Quaternion.Euler(0f, 0f, s * 1.5f * br);
                    rot[IndexOf("UpperArm" + suf)] = Quaternion.Euler(3f * Mathf.Sin(q + side), 0f, -s * 35f);
                    rot[IndexOf("LowerArm" + suf)] = Quaternion.Euler(-14f, 0f, 0f);
                    rot[IndexOf("Hand" + suf)] = Quaternion.Euler(-3f, 0f, 0f);
                    for (int f = 0; f < 5; f++)
                    {
                        float curl = (f == 0 ? 10f : 26f) + 2f * Mathf.Sin(q + f);
                        rot[IndexOf(Fingers[f] + "1" + suf)] = Quaternion.Euler(curl, 0f, 0f);
                        rot[IndexOf(Fingers[f] + "2" + suf)] = Quaternion.Euler(curl * 1.3f, 0f, 0f);
                    }
                }
            }
        }

        /// <summary>Each bone's model-space matrix in a pose (forward kinematics).</summary>
        public static void Model(Quaternion[] rot, Vector3 hipsPos, Matrix4x4[] model)
        {
            var b = Bones;
            for (int i = 0; i < b.Length; i++)
            {
                var local = Matrix4x4.TRS(i == 1 ? hipsPos : b[i].local, rot[i], Vector3.one);
                model[i] = b[i].parent < 0 ? local : model[b[i].parent] * local;
            }
        }

        /// <summary>
        /// THE BAKED BONE TEXTURE (technique B): one row a frame (the walk's 30, then the idle's 180), three
        /// RGBAHalf texels a bone holding the top three rows of bone-to-model x bind pose. Point-sampled.
        /// </summary>
        public static Texture2D BakeBoneTexture()
        {
            int nb = BoneCount, rows = WalkFrames + IdleFrames;
            var tex = new Texture2D(nb * 3, rows, TextureFormat.RGBAHalf, false, true)
            { name = "PeopleBoneTex", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[nb * 3 * rows];
            var rot = new Quaternion[nb]; var model = new Matrix4x4[nb]; var bind = BindPoses();
            for (int row = 0; row < rows; row++)
            {
                int clip = row < WalkFrames ? 0 : 1;
                float t = (row - (clip == 0 ? WalkRow0 : IdleRow0)) / (float)Fps;
                Pose(clip, t, rot, out var hp);
                Model(rot, hp, model);
                for (int i = 0; i < nb; i++)
                {
                    var m = model[i] * bind[i];
                    int o = row * nb * 3 + i * 3;
                    px[o] = new Color(m.m00, m.m01, m.m02, m.m03);
                    px[o + 1] = new Color(m.m10, m.m11, m.m12, m.m13);
                    px[o + 2] = new Color(m.m20, m.m21, m.m22, m.m23);
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>A static mesh in one pose of a clip (technique D, the floor): the skin done once on the CPU.</summary>
        public static Mesh BakePosed(Mesh skinned, int clip, float t)
        {
            int nb = BoneCount;
            var rot = new Quaternion[nb]; var model = new Matrix4x4[nb]; var bind = BindPoses();
            Pose(clip, t, rot, out var hp);
            Model(rot, hp, model);
            var skin = new Matrix4x4[nb];
            for (int i = 0; i < nb; i++) skin[i] = model[i] * bind[i];
            var v = skinned.vertices; var n = skinned.normals; var w = skinned.boneWeights;
            for (int i = 0; i < v.Length; i++)
            {
                var bw = w[i];
                Vector3 p = Vector3.zero, q = Vector3.zero;
                void Acc(int bi, float wt) { if (wt <= 0f) return; p += skin[bi].MultiplyPoint3x4(v[i]) * wt; q += skin[bi].MultiplyVector(n[i]) * wt; }
                Acc(bw.boneIndex0, bw.weight0); Acc(bw.boneIndex1, bw.weight1); Acc(bw.boneIndex2, bw.weight2); Acc(bw.boneIndex3, bw.weight3);
                v[i] = p; n[i] = q.normalized;
            }
            var m = new Mesh { name = skinned.name + "_Posed" + clip, indexFormat = skinned.indexFormat };
            m.vertices = v; m.normals = n; m.uv = skinned.uv; m.colors32 = skinned.colors32;
            m.triangles = skinned.triangles;
            m.RecalculateBounds();
            m.UploadMeshData(true);
            return m;
        }

        /// <summary>The figure's one texture: a plain cloth-and-skin sheet at the brief's size, block-compressed
        /// with mips, as an imported texture would be.</summary>
        public static Texture2D BuildTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "PeopleMannequinTex_" + size, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int cx = x * 8 / size, cy = y * 8 / size;
                    // grey cloth (the tint mask's garments), a darker trouser band, skin for the head and hands
                    byte g = (byte)(150 + ((x ^ y) & 7) * 3 - (cy > 3 ? 40 : 0));
                    bool skin = cy == 0 && (cx == 5) || cy >= 1 && cy <= 3 && (cx % 4 == 3);
                    px[y * size + x] = skin ? new Color32(176, 132, 104, 255) : new Color32(g, g, (byte)(g + 4), 255);
                }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            tex.Compress(false);
            tex.Apply(false, true);
            return tex;
        }

#if UNITY_EDITOR
        /// <summary>The walk or the idle as a generic AnimationClip (technique A): every bone's local rotation
        /// and the hips' position keyed at 30 frames a second, the way an imported clip arrives. Editor only:
        /// a clip can only be authored from code in the editor (the shipped path would import FBX clips).</summary>
        public static AnimationClip BuildClip(int clip, Dictionary<int, string> paths)
        {
            var c = new AnimationClip { name = clip == 0 ? "Mannequin_Walk" : "Mannequin_Idle", frameRate = Fps, legacy = false };
            int frames = clip == 0 ? WalkFrames : IdleFrames;
            int nb = BoneCount;
            var curves = new AnimationCurve[nb, 4];
            var hip = new AnimationCurve[3];
            for (int i = 0; i < nb; i++) for (int k = 0; k < 4; k++) curves[i, k] = new AnimationCurve();
            for (int k = 0; k < 3; k++) hip[k] = new AnimationCurve();
            var rot = new Quaternion[nb];
            for (int f = 0; f <= frames; f++)
            {
                float t = f / (float)Fps;
                Pose(clip, t % (clip == 0 ? WalkCycle : IdleCycle), rot, out var hp);
                for (int i = 0; i < nb; i++)
                {
                    curves[i, 0].AddKey(t, rot[i].x); curves[i, 1].AddKey(t, rot[i].y);
                    curves[i, 2].AddKey(t, rot[i].z); curves[i, 3].AddKey(t, rot[i].w);
                }
                hip[0].AddKey(t, hp.x); hip[1].AddKey(t, hp.y); hip[2].AddKey(t, hp.z);
            }
            string[] comp = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
            for (int i = 0; i < nb; i++)
                for (int k = 0; k < 4; k++)
                    UnityEditor.AnimationUtility.SetEditorCurve(c, UnityEditor.EditorCurveBinding.FloatCurve(paths[i], typeof(Transform), comp[k]), curves[i, k]);
            string[] pc = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };
            for (int k = 0; k < 3; k++)
                UnityEditor.AnimationUtility.SetEditorCurve(c, UnityEditor.EditorCurveBinding.FloatCurve(paths[1], typeof(Transform), pc[k]), hip[k]);
            var st = UnityEditor.AnimationUtility.GetAnimationClipSettings(c);
            st.loopTime = true;
            UnityEditor.AnimationUtility.SetAnimationClipSettings(c, st);
            c.EnsureQuaternionContinuity();
            return c;
        }
#endif
    }
}
