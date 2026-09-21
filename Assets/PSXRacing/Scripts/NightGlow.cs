using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Scenery that only lights up after dark: the lamps along the barriers and
    /// the pools they throw on the tarmac.
    ///
    /// One of these per scene, on a parent holding every glow quad, rather than
    /// a component per lamp — a circuit carries thirty of them and thirty
    /// Awake calls to toggle a renderer is thirty too many. The lamp POSTS are
    /// ordinary scenery and stay visible all day; only the light does not.
    ///
    /// WHAT IS LIT CHANGED ON 2026-09-21 (the Need for Speed 2015 night pass:
    /// "how street lights bathe the road"). The light on the road is no longer
    /// a quad under this object at all:
    ///   * The 16 m flat "Pool" quads are RETIRED. An additive disc lay ON the
    ///     tarmac and never lit it - nor the kerb, the wall, or the car parked
    ///     under it. Every lamp head is registered with
    ///     <see cref="StreetLights"/> instead, and PSXLamps.cginc lights every
    ///     PSX surface from the twelve nearest per pixel.
    ///   * The flat "Glow" quads under each head are retired too - a flat quad
    ///     eight metres up is an edge-on sliver from the driver's seat - but
    ///     the builders KEEP making them: each one's position is where a lamp
    ///     head is (that is how a baked scene tells this component where its
    ///     lamps are), and LampGlow.mat on them is what keeps PSX/Glow in the
    ///     WebGL build for the cars' lenses.
    ///   * Each head gets a camera-facing HALO instead (PSX/Halo): one merged
    ///     mesh per NightGlow, one draw call however many lamps, built at
    ///     runtime under a "Halos" child and never saved.
    /// Retired quads are switched off with SetActive(false), never with
    /// renderer.enabled - the hour switches renderers, and it would switch a
    /// retired disc straight back on. What the hour switches now is the halo
    /// (and anything else under here that is not a retired marker; nothing,
    /// today). The street lamps' light itself follows
    /// <see cref="StreetLights.StreetOn"/>, which <see cref="SetAll"/> sets.
    ///
    /// Three ways in. A baked scene's Awake reads its Glow markers (play
    /// mode). A streamed city tile adds this to an empty object and hands its
    /// heads to <see cref="Init"/>. A tool in edit mode - where Unity calls
    /// neither Awake nor OnEnable on this - runs <see cref="PreviewAll(bool)"/>.
    /// All three end in the same idempotent Build + register.
    /// </summary>
    public class NightGlow : MonoBehaviour
    {
        static readonly List<NightGlow> all = new List<NightGlow>();
        static bool on;

        /// <summary>Whether the hour has the street lamps lit.</summary>
        public static bool On => on;

        /// <summary>Name of the runtime child holding the merged halo mesh.</summary>
        public const string HaloName = "Halos";
        const string GlowName = "Glow", PoolName = "Pool";
        const string HaloMeshName = "NightGlowHalos";
        /// <summary>
        /// Metres: a halo's half-size before the per-halo multiplier and the
        /// wet-mist growth. Written onto the halo material as _Size, and the
        /// mesh bounds are grown by it - the four vertices of a halo all sit
        /// at its centre, so bounds from the vertices alone are a POINT, and
        /// a lamp whose centre is just off-screen would be culled while its
        /// glow was still on it.
        /// </summary>
        public const float HaloSize = 2.6f;

        Renderer[] glows;                                     // what the hour switches
        readonly List<Vector3> heads = new List<Vector3>();   // lamp heads, world space
        bool explicitHeads;                                   // Init gave them: never re-collect
        bool built;
        GameObject haloGo;
        Mesh haloMesh;
        static readonly List<Vector3> scratch = new List<Vector3>();

        bool Live => enabled && gameObject.activeInHierarchy;

        void Awake()
        {
            // Play mode only. The city's holder arrives here empty and is
            // filled by Init straight after AddComponent.
            if (!explicitHeads) CollectBaked();
            Build();
        }

        void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
            if (built) Register();
            Apply();
        }

        void OnDisable()
        {
            all.Remove(this);
            StreetLights.RemoveAll(this);
        }

        /// <summary>A streamed city tile is dropped with Destroy: the halo
        /// mesh is ours, not an asset, and would leak one per tile.</summary>
        void OnDestroy()
        {
            StreetLights.RemoveAll(this);
            DropHalo();
        }

        /// <summary>
        /// The explicit path (a city tile): these world positions are the lamp
        /// heads. Self-sufficient and idempotent - it builds the halos and
        /// registers the lamps itself, so it works right after AddComponent in
        /// play mode AND in edit mode, where AddComponent runs no Awake.
        /// </summary>
        public void Init(IList<Vector3> worldHeads)
        {
            explicitHeads = true;
            heads.Clear();
            if (worldHeads != null)
                for (int i = 0; i < worldHeads.Count; i++) heads.Add(worldHeads[i]);
            Build();
            Enlist();
        }

        /// <summary>
        /// Edit-mode init for one NightGlow, then light (or not) every lamp:
        /// read the markers (or keep Init's heads), retire the old quads, build
        /// the halos (DontSave: a tool that saves the scene never bakes them
        /// in), register the heads, and set the hour's state. Idempotent - a
        /// second call rebuilds the same thing.
        /// </summary>
        public void PreviewBuild(bool lit)
        {
            PreviewInit();
            SetAll(lit);
        }

        /// <summary><see cref="PreviewBuild"/> for every active NightGlow in
        /// the loaded scenes, then <see cref="SetAll"/>. The screenshot tools'
        /// entry point: edit mode never runs Awake.</summary>
        public static void PreviewAll(bool lit)
        {
            var found = Object.FindObjectsByType<NightGlow>(FindObjectsInactive.Exclude);
            for (int i = 0; i < found.Length; i++) found[i].PreviewInit();
            SetAll(lit);
        }

        /// <summary><see cref="PreviewAll(bool)"/> at the current hour's state.</summary>
        public static void PreviewAll() => PreviewAll(on);

        void PreviewInit()
        {
            if (!explicitHeads) CollectBaked();
            Build();
            Enlist();
        }

        void Enlist()
        {
            if (!all.Contains(this)) all.Add(this);
            if (Live) Register();
            else StreetLights.RemoveAll(this);
            Apply();
        }

        /// <summary>
        /// A baked scene's lamp heads are its "Glow" markers (inactive ones
        /// too: a second pass, or a scene saved after a preview, has them
        /// retired already). The markers and any "Pool" discs are retired as
        /// they are read.
        /// </summary>
        void CollectBaked()
        {
            heads.Clear();
            var ts = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < ts.Length; i++)
            {
                var t = ts[i];
                if (t == transform) continue;
                string n = t.name;
                if (n == GlowName) { heads.Add(t.position); Retire(t.gameObject); }
                else if (n == PoolName) Retire(t.gameObject);
            }
        }

        static void Retire(GameObject go)
        {
            if (go.activeSelf) go.SetActive(false);
        }

        void Build()
        {
            DropHalo();
            if (heads.Count > 0) MakeHalo();
            var rs = GetComponentsInChildren<Renderer>(true);
            var keep = new List<Renderer>(rs.Length);
            for (int i = 0; i < rs.Length; i++)
            {
                var go = rs[i].gameObject;
                string n = go.name;
                if (n == GlowName || n == PoolName) continue;
                // A halo on its way out: Destroy is deferred in play mode.
                if (n == HaloName && go != haloGo) continue;
                keep.Add(rs[i]);
            }
            glows = keep.ToArray();
            built = true;
        }

        void Register()
        {
            StreetLights.RemoveAll(this);
            for (int i = 0; i < heads.Count; i++)
                StreetLights.Add(this, heads[i], StreetLights.StreetRadius, StreetLights.Sodium,
                                 StreetLights.StreetIntensity, StreetLights.Kind.Street);
        }

        void MakeHalo()
        {
            var mat = HaloMaterial;
            if (mat == null) return;   // warned once; the lamps still light the road
            haloGo = new GameObject(HaloName);
            haloGo.layer = gameObject.layer;
            var t = haloGo.transform;
            t.SetParent(transform, false);
            scratch.Clear();
            for (int i = 0; i < heads.Count; i++) scratch.Add(t.InverseTransformPoint(heads[i]));
            haloMesh = BuildHaloMesh(scratch, 1f);
            var mf = haloGo.AddComponent<MeshFilter>();
            mf.sharedMesh = haloMesh;
            var mr = haloGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            mr.enabled = on;
            if (!Application.isPlaying)
            {
                // Never saved with the scene. The mesh is NOT DontUnloadUnusedAsset
                // (unlike HideFlags.DontSave): edit mode never calls OnDestroy
                // here, so once the scene holding its renderer closes, the next
                // asset unload is what frees it.
                haloGo.hideFlags = HideFlags.DontSave;
                mf.hideFlags = HideFlags.DontSave;
                mr.hideFlags = HideFlags.DontSave;
                haloMesh.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            }
        }

        void DropHalo()
        {
            if (haloGo != null) Kill(haloGo);
            if (haloMesh != null) Kill(haloMesh);
            haloGo = null;
            haloMesh = null;
            if (Application.isPlaying) return;
            // Edit mode: a halo this instance no longer knows it built - a
            // domain reload forgets the field but not the DontSave object in
            // the open scene - would otherwise be doubled by the next build.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i);
                if (c.name != HaloName) continue;
                var mf = c.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name == HaloMeshName)
                    Kill(mf.sharedMesh);
                Kill(c.gameObject);
            }
        }

        static void Kill(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        static Material haloMat;
        static bool haloWarned;
        /// <summary>One runtime material for every halo in the game. PSX/Halo
        /// reaches the build only through ProjectSettings' Always Included
        /// Shaders (no .mat uses it); if it is missing anyway, say so once and
        /// go without halos - the lamps still light the road.</summary>
        static Material HaloMaterial
        {
            get
            {
                if (haloMat != null) return haloMat;
                var sh = Shader.Find("PSX/Halo");
                if (sh == null)
                {
                    if (!haloWarned)
                    {
                        haloWarned = true;
                        Debug.LogWarning("NightGlow: PSX/Halo shader missing - lamp heads get no halo (the lamps still light the road).");
                    }
                    return null;
                }
                haloMat = new Material(sh) { name = "LampHalo", hideFlags = HideFlags.HideAndDontSave };
                // SetColor takes sRGB and Unity linearises it, the same as
                // StreetLights does for the light: the halo and the pool it
                // stands over are one colour.
                haloMat.SetColor("_Color", StreetLights.Sodium);
                haloMat.SetFloat("_Size", HaloSize);
                return haloMat;
            }
        }

        /// <summary>
        /// The merged halo mesh PSX/Halo draws: four vertices per halo, all AT
        /// its centre (the shader builds the quad in view space, facing the
        /// camera), uv0 = the corner, uv1.x = this halo's size multiplier,
        /// vertex colour white (the tint is the material's _Color, which Unity
        /// linearises; a vertex colour would not be). Bounds are set by hand:
        /// the vertices alone span only the centres.
        /// </summary>
        public static Mesh BuildHaloMesh(IList<Vector3> localCentres, float sizeMul)
        {
            int n = localCentres != null ? localCentres.Count : 0;
            var mesh = new Mesh { name = HaloMeshName };
            if (n == 0) return mesh;
            int nv = n * 4;
            if (nv > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var verts = new Vector3[nv];
            var uv0 = new Vector2[nv];
            var uv1 = new Vector2[nv];
            var cols = new Color32[nv];
            var tris = new int[n * 6];
            var white = new Color32(255, 255, 255, 255);
            var mul = new Vector2(sizeMul, 0f);
            var b = new Bounds(localCentres[0], Vector3.zero);
            for (int i = 0; i < n; i++)
            {
                Vector3 c = localCentres[i];
                b.Encapsulate(c);
                int v = i * 4;
                verts[v] = verts[v + 1] = verts[v + 2] = verts[v + 3] = c;
                uv0[v] = new Vector2(0f, 0f);
                uv0[v + 1] = new Vector2(1f, 0f);
                uv0[v + 2] = new Vector2(1f, 1f);
                uv0[v + 3] = new Vector2(0f, 1f);
                uv1[v] = uv1[v + 1] = uv1[v + 2] = uv1[v + 3] = mul;
                cols[v] = cols[v + 1] = cols[v + 2] = cols[v + 3] = white;
                int k = i * 6;
                tris[k] = v; tris[k + 1] = v + 1; tris[k + 2] = v + 2;
                tris[k + 3] = v; tris[k + 4] = v + 2; tris[k + 5] = v + 3;
            }
            mesh.vertices = verts;
            mesh.uv = uv0;
            mesh.uv2 = uv1;
            mesh.colors32 = cols;
            mesh.triangles = tris;
            // Last: assigning triangles recomputes bounds from the vertices.
            // Grown by the biggest a halo gets (x1.5 in the wet) plus the pull
            // toward the eye.
            b.Expand(2f * (HaloSize * 1.5f * Mathf.Abs(sizeMul) + 0.6f));
            mesh.bounds = b;
            return mesh;
        }

        /// <summary>Called by <see cref="TimeOfDay.Apply"/>; the hour owns
        /// this, the same as it owns the cars' headlights. Lights the halos
        /// and, through <see cref="StreetLights.StreetOn"/>, the lamps'
        /// light on the world - including the lamps of city tiles that have
        /// not streamed in yet.</summary>
        public static void SetAll(bool lit)
        {
            on = lit;
            StreetLights.StreetOn = lit;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (all[i] == null) { all.RemoveAt(i); continue; }
                all[i].Apply();
            }
        }

        void Apply()
        {
            if (glows == null) return;
            foreach (var r in glows) if (r != null) r.enabled = on;
        }
    }
}
