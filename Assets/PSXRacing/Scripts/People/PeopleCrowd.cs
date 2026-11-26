using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PSXRacing.People
{
    /// <summary>
    /// THE TEST CROWD (people, Stage 1 of Characters_Unity_Plan, 2026-11-26): the test mannequin
    /// (<see cref="Mannequin"/>) stood in the meet lot by three techniques, so their costs can be compared in one
    /// run before any art is ordered:
    ///   A  one SkinnedMeshRenderer a figure (GPU skinning, the project's setting) and an Animator driven from code
    ///      through the Playables API (an AnimationClipPlayable on an AnimationPlayableOutput; no controller asset);
    ///   B  the bone matrices baked into a texture, every figure in ONE Graphics.RenderMeshInstanced call, skinned in
    ///      PSX/Lit's own vertex shader (PSX_BAKEDSKIN, Shaders/PSXBakedSkin.cginc); a figure costs the CPU one float4;
    ///   D  a static posed mesh a figure (a MeshRenderer): the floor, nothing animates.
    /// Every figure uses PSX/Lit, the material of the walls, roads and props: the sun and sky per vertex, the street
    /// lamps (PSXLamps.cginc) and the headlights (PSXHeadlights.cginc) per pixel. Half the figures idle at the cars,
    /// half walk the aisles (moved by code at the walk clip's own speed, turning at the ends).
    ///
    /// TEST ONLY. Nothing builds a crowd unless the perf tour asks (place=town) or PSX_PEOPLE_TEST=10|20|40 is set
    /// (with PSX_PEOPLE_TECH=A|B|D, default B) and the scene has the meet lot. The owner's game never shows one.
    /// </summary>
    public class PeopleCrowd : MonoBehaviour
    {
        public enum Tech { A, B, D }

        public struct Spot { public Vector3 pos; public Quaternion rot; public bool walk; public Vector3 a, b; }

        public static PeopleCrowd Instance { get; private set; }

        /// <summary>The figures each technique has built (all of them, hidden but for <see cref="Count"/>).</summary>
        public int Built { get; private set; }
        public Tech Current { get; private set; } = Tech.B;
        public int Count { get; private set; }
        public bool PC { get; private set; }
        public int TrisPerFigure { get; private set; }
        public int VertsPerFigure { get; private set; }

        Spot[] spots;
        Material matLit, matSkin;
        Texture2D tex, boneTex;
        Mesh meshSkinned, meshBaked, meshPosedWalk, meshPosedIdle;
        // A
        readonly List<GameObject> aFigs = new List<GameObject>();
        readonly List<PlayableGraph> aGraphs = new List<PlayableGraph>();
        AnimationClip clipWalk, clipIdle;
        // D
        readonly List<GameObject> dFigs = new List<GameObject>();
        // B
        Matrix4x4[] bMats;
        Vector4[] bFrames;
        MaterialPropertyBlock bProps;
        // the walkers: where along their line, which way
        float[] along, phase;
        int[] dirSign;

        /// <summary>A crowd of <paramref name="n"/> figures at <paramref name="at"/>, every technique built,
        /// none shown (<see cref="Show"/>).</summary>
        public static PeopleCrowd Build(Spot[] at, bool pc)
        {
            var go = new GameObject("PeopleTestCrowd");
            var c = go.AddComponent<PeopleCrowd>();
            c.Make(at, pc);
            return c;
        }

        void Make(Spot[] at, bool pc)
        {
            Instance = this;
            PC = pc;
            spots = at;
            Built = at.Length;
            meshSkinned = Mannequin.Build(pc ? 5000 : 1800, pc ? 4 : 2, false, out int tris);
            meshBaked = Mannequin.Build(pc ? 5000 : 1800, pc ? 4 : 2, true, out _);
            TrisPerFigure = tris; VertsPerFigure = meshSkinned.vertexCount;
            meshPosedWalk = Mannequin.BakePosed(meshSkinned, 0, 0.25f);
            meshPosedIdle = Mannequin.BakePosed(meshSkinned, 1, 0f);
            tex = Mannequin.BuildTexture(pc ? 1024 : 256);
            boneTex = Mannequin.BakeBoneTexture();
            var sh = Shader.Find("PSX/Lit");
            matLit = new Material(sh) { name = "PeopleLit", enableInstancing = true };
            matLit.SetTexture("_MainTex", tex);
            matSkin = new Material(sh) { name = "PeopleBakedSkin", enableInstancing = true };
            matSkin.SetTexture("_MainTex", tex);
            matSkin.EnableKeyword("PSX_BAKEDSKIN");
            matSkin.SetTexture("_PSXSkinTex", boneTex);
            matSkin.SetVector("_PSXSkinTex_TexelSize", new Vector4(1f / boneTex.width, 1f / boneTex.height, boneTex.width, boneTex.height));

            int n = at.Length;
            along = new float[n]; phase = new float[n]; dirSign = new int[n];
            var rng = new System.Random(1999);
            for (int i = 0; i < n; i++) { along[i] = (float)rng.NextDouble(); phase[i] = (float)rng.NextDouble() * 10f; dirSign[i] = rng.Next(2) == 0 ? 1 : -1; }

            // A: a bone hierarchy, a skinned renderer and an animator with its own graph per figure
            var boneDefs = Mannequin.Bones;
            var paths = new Dictionary<int, string>();
            for (int i = 0; i < boneDefs.Length; i++)
            {
                string p = boneDefs[i].name;
                for (int q = boneDefs[i].parent; q >= 0; q = boneDefs[q].parent) p = boneDefs[q].name + "/" + p;
                paths[i] = p;
            }
#if UNITY_EDITOR
            clipWalk = Mannequin.BuildClip(0, paths);
            clipIdle = Mannequin.BuildClip(1, paths);
#endif
            for (int i = 0; i < n; i++)
            {
                var fig = new GameObject("FigA" + i);
                fig.transform.SetParent(transform, false);
                var tr = new Transform[boneDefs.Length];
                for (int b = 0; b < boneDefs.Length; b++)
                {
                    var bt = new GameObject(boneDefs[b].name).transform;
                    bt.SetParent(boneDefs[b].parent < 0 ? fig.transform : tr[boneDefs[b].parent], false);
                    bt.localPosition = boneDefs[b].local;
                    tr[b] = bt;
                }
                var body = new GameObject("Body");
                body.transform.SetParent(fig.transform, false);
                var smr = body.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = meshSkinned; smr.bones = tr; smr.rootBone = tr[0];
                smr.sharedMaterial = matLit;
                smr.quality = pc ? SkinQuality.Bone4 : SkinQuality.Bone2;
                smr.localBounds = meshSkinned.bounds;
                smr.shadowCastingMode = ShadowCastingMode.On;
                var anim = fig.AddComponent<Animator>();
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                var g = PlayableGraph.Create("PeopleA" + i);
                g.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                var outp = AnimationPlayableOutput.Create(g, "out", anim);
                var clip = at[i].walk ? clipWalk : clipIdle;
                if (clip != null)
                {
                    var cp = AnimationClipPlayable.Create(g, clip);
                    cp.SetTime(phase[i]);
                    outp.SetSourcePlayable(cp);
                }
                aGraphs.Add(g);
                fig.SetActive(false);
                aFigs.Add(fig);
            }
            // D: the posed meshes
            for (int i = 0; i < n; i++)
            {
                var fig = new GameObject("FigD" + i);
                fig.transform.SetParent(transform, false);
                fig.AddComponent<MeshFilter>().sharedMesh = at[i].walk ? meshPosedWalk : meshPosedIdle;
                var mr = fig.AddComponent<MeshRenderer>();
                mr.sharedMaterial = matLit;
                fig.SetActive(false);
                dFigs.Add(fig);
            }
            // B: one instanced draw
            bMats = new Matrix4x4[n]; bFrames = new Vector4[n]; bProps = new MaterialPropertyBlock();
            Place(0f);
            Debug.Log("[People] test crowd built: " + n + " figures a technique, " + TrisPerFigure + " triangles and " + VertsPerFigure +
                      " vertices a figure, " + Mannequin.BoneCount + " bones, " + (pc ? "4" : "2") + " weights a vertex, texture " + tex.width + " (" + tex.format +
                      "), bone texture " + boneTex.width + "x" + boneTex.height + " " + boneTex.format + "; clips " + (clipWalk != null ? "built" : "NONE (player: technique A has no clip)"));
        }

        /// <summary>Show <paramref name="n"/> figures by <paramref name="t"/>; 0 shows none.</summary>
        public void Show(Tech t, int n)
        {
            Current = t; Count = Mathf.Clamp(n, 0, Built);
            for (int i = 0; i < Built; i++)
            {
                bool a = t == Tech.A && i < Count, d = t == Tech.D && i < Count;
                if (aFigs[i].activeSelf != a) aFigs[i].SetActive(a);
                if (a && !aGraphs[i].IsPlaying()) aGraphs[i].Play();
                if (!a && aGraphs[i].IsValid() && aGraphs[i].IsPlaying()) aGraphs[i].Stop();
                if (dFigs[i].activeSelf != d) dFigs[i].SetActive(d);
            }
        }

        /// <summary>The first shown walker's left thigh, as its Animator left it (a check that A animates).</summary>
        public string APose()
        {
            for (int i = 0; i < Count && i < aFigs.Count; i++)
                if (spots[i].walk && aFigs[i].activeSelf)
                {
                    var t = aFigs[i].transform.Find("Root/Hips/UpperLeg_L");
                    return t != null ? t.localEulerAngles.ToString("0.0") : "no thigh";
                }
            return "no walker shown";
        }

        void Place(float dt)
        {
            for (int i = 0; i < Built; i++)
            {
                var s = spots[i];
                Vector3 p = s.pos; Quaternion r = s.rot;
                if (s.walk)
                {
                    float len = Mathf.Max(0.5f, Vector3.Distance(s.a, s.b));
                    along[i] += dirSign[i] * Mannequin.WalkSpeed * dt / len;
                    if (along[i] > 1f) { along[i] = 1f; dirSign[i] = -1; }
                    if (along[i] < 0f) { along[i] = 0f; dirSign[i] = 1; }
                    p = Vector3.Lerp(s.a, s.b, along[i]);
                    r = Quaternion.LookRotation((s.b - s.a) * dirSign[i], Vector3.up);
                }
                if (i < aFigs.Count && aFigs[i].activeSelf) aFigs[i].transform.SetPositionAndRotation(p, r);
                if (i < dFigs.Count && dFigs[i].activeSelf) dFigs[i].transform.SetPositionAndRotation(p, r);
                bMats[i] = Matrix4x4.TRS(p, r, Vector3.one);
            }
        }

        void Update()
        {
            if (Count <= 0) return;
            Place(Time.deltaTime);
            if (Current != Tech.B) return;
            // one float4 a figure: this frame's row, the next row, the blend
            float now = Time.time;
            for (int i = 0; i < Count; i++)
            {
                bool w = spots[i].walk;
                int frames = w ? Mannequin.WalkFrames : Mannequin.IdleFrames, row0 = w ? Mannequin.WalkRow0 : Mannequin.IdleRow0;
                float f = (now + phase[i]) * Mannequin.Fps;
                float fl = Mathf.Floor(f);
                int k = (int)fl % frames;
                bFrames[i] = new Vector4(row0 + k, row0 + (k + 1) % frames, f - fl, 0f);
            }
            bProps.SetVectorArray("_PSXSkinFrame", bFrames);
            var rp = new RenderParams(matSkin)
            {
                matProps = bProps,
                shadowCastingMode = ShadowCastingMode.On,
                receiveShadows = true,
                worldBounds = WorldBounds(),
                layer = gameObject.layer,
            };
            Graphics.RenderMeshInstanced(rp, meshBaked, 0, bMats, Count);
        }

        Bounds WorldBounds()
        {
            var b = new Bounds(bMats[0].GetColumn(3), Vector3.one);
            for (int i = 1; i < Count; i++) b.Encapsulate((Vector3)bMats[i].GetColumn(3));
            b.Expand(new Vector3(2f, 4f, 2f));
            return b;
        }

        /// <summary>What each technique holds in memory, by its parts (bytes), for the tour's table.</summary>
        public string MemoryReport()
        {
            long M(Object o) => o != null ? UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(o) : 0;
            long stride = meshSkinned.GetVertexBufferStride(0);
            // GPU skinning keeps a skinned copy of the vertices for every SkinnedMeshRenderer (position, normal, tangent)
            long skinOut = (long)VertsPerFigure * 40;
            return $"texture {M(tex) / 1024f:0} KB ({tex.width}x{tex.height} {tex.format}, mips); " +
                   $"A: skinned mesh {M(meshSkinned) / 1024f:0} KB (vertex stride {stride} B, + {meshSkinned.vertexCount} x 32 B bone weights), clips walk {M(clipWalk) / 1024f:0} KB idle {M(clipIdle) / 1024f:0} KB, " +
                   $"+ a skinned vertex copy a figure ~{skinOut / 1024f:0} KB (estimate: verts x 40 B) and {Mannequin.BoneCount} transforms a figure; " +
                   $"B: mesh with indices/weights in uv3/uv4 {M(meshBaked) / 1024f:0} KB, bone texture {M(boneTex) / 1024f:0} KB ({boneTex.width}x{boneTex.height} {boneTex.format}), nothing a figure but one float4 and one matrix; " +
                   $"D: two posed meshes {(M(meshPosedWalk) + M(meshPosedIdle)) / 1024f:0} KB";
        }

        void OnDestroy()
        {
            foreach (var g in aGraphs) if (g.IsValid()) g.Destroy();
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------
        //  where they stand, and the play-check switch
        // ------------------------------------------------------------------

        /// <summary>
        /// Spots in the meet lot seen from <paramref name="eye"/>: idlers beside the cars' noses (each stall's
        /// two sides, facing the aisle) and walkers up and down the aisles, nearest and in view first. Half idle,
        /// half walk.
        /// </summary>
        public static Spot[] MeetSpots(Transform[] stalls, Camera view, int n)
        {
            var idle = new List<Spot>(); var walk = new List<Spot>();
            var planes = view != null ? GeometryUtility.CalculateFrustumPlanes(view) : null;
            Vector3 eye = view != null ? view.transform.position : Vector3.zero;
            float minX = float.MaxValue, maxX = float.MinValue;
            var aisleZ = new List<float>();
            foreach (var s in stalls)
            {
                if (s == null) continue;
                minX = Mathf.Min(minX, s.position.x); maxX = Mathf.Max(maxX, s.position.x);
                foreach (float side in new[] { -0.95f, 0.95f })
                {
                    var p = s.position + s.forward * 3.25f + s.right * side;
                    idle.Add(new Spot { pos = p, rot = Quaternion.LookRotation(-s.forward + s.right * side * 0.4f, Vector3.up) });
                }
                float az = s.position.z + s.forward.z * 6.2f;   // half a stall (2.7) + half the aisle (3.5)
                bool have = false;
                foreach (var z in aisleZ) if (Mathf.Abs(z - az) < 1.5f) have = true;
                if (!have) aisleZ.Add(az);
            }
            float y = stalls.Length > 0 && stalls[0] != null ? stalls[0].position.y : 0f;
            for (int k = 0; k < n; k++)
            {
                if (aisleZ.Count == 0) break;
                float z = aisleZ[k % aisleZ.Count] + ((k / aisleZ.Count) % 2 == 0 ? -0.9f : 0.9f);
                float x0 = Mathf.Lerp(minX, maxX, (k * 0.37f) % 1f);
                float x1 = Mathf.Min(maxX, x0 + 7f + (k % 3) * 3f);
                if (x1 - x0 < 4f) x0 = x1 - 7f;
                var a = new Vector3(x0, y, z); var b = new Vector3(x1, y, z);
                walk.Add(new Spot { pos = a, a = a, b = b, walk = true, rot = Quaternion.identity });
            }
            int Score(Spot s)
            {
                var c = s.walk ? (s.a + s.b) * 0.5f : s.pos;
                bool seen = planes == null || GeometryUtility.TestPlanesAABB(planes, new Bounds(c + Vector3.up, new Vector3(1f, 2f, 1f)));
                return (seen ? 0 : 100000) + Mathf.RoundToInt(Vector3.Distance(eye, c) * 10f);
            }
            idle.Sort((p, q) => Score(p).CompareTo(Score(q)));
            walk.Sort((p, q) => Score(p).CompareTo(Score(q)));
            var r = new Spot[n];
            int wi = 0, ii = 0;
            for (int i = 0; i < n; i++) r[i] = (i % 2 == 1 && wi < walk.Count) || ii >= idle.Count ? walk[wi++] : idle[ii++];
            return r;
        }

        /// <summary>PSX_PEOPLE_TEST=10|20|40 (+ PSX_PEOPLE_TECH=A|B|D): the crowd at the meet lot on a play check.
        /// Off unless set. The load line below runs in every build (one log line, nothing drawn): it says which
        /// technique's types and shader keyword are present, so a player build shows they survived stripping.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            LoadLine();
            if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("PSX_PEOPLE_TEST"))) return;
            SceneManager.sceneLoaded += (s, m) => TryEnvCrowd();
            TryEnvCrowd();
        }

        static void LoadLine()
        {
            var sh = Shader.Find("PSX/Lit");
            bool kw = false;
            if (sh != null)
                foreach (var k in sh.keywordSpace.keywordNames) if (k == "PSX_BAKEDSKIN") kw = true;
            Debug.Log("[People] load: technique B (baked bone texture, Graphics.RenderMeshInstanced, PSX/Lit PSX_BAKEDSKIN) " +
                      "types " + typeof(RenderParams).Name + "/" + typeof(MaterialPropertyBlock).Name + " present, shader " + (sh != null ? "found" : "MISSING") +
                      ", keyword " + (kw ? "declared" : "MISSING") + ", RGBAHalf " + (SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf) ? "yes" : "NO") +
                      ", instancing " + (SystemInfo.supportsInstancing ? "yes" : "NO") +
                      "; technique A types " + typeof(Animator).Name + "/" + typeof(AnimationClipPlayable).Name + "/" + typeof(SkinnedMeshRenderer).Name + " present");
        }

        static void TryEnvCrowd()
        {
            if (Instance != null) return;
            int.TryParse(System.Environment.GetEnvironmentVariable("PSX_PEOPLE_TEST"), out int n);
            if (n <= 0) return;
            var tw = Object.FindAnyObjectByType<PSXRacing.Town.TownWorld>();
            if (tw == null || tw.meetSpots == null || tw.meetSpots.Length == 0) return;
            var t = Tech.B;
            System.Enum.TryParse(System.Environment.GetEnvironmentVariable("PSX_PEOPLE_TECH") ?? "B", true, out t);
            var c = Build(MeetSpots(tw.meetSpots, Camera.main, n), PlatformProfile.PC);
            c.Show(t, n);
        }
    }
}
