using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;

namespace PSXRacing
{
    /// <summary>
    /// Real mesh forest on the mountain stages (owner 2026-11-07: "overhauling
    /// the Mountain Tracks (tree models ... first) ... Focus on performance with
    /// LOD (low, mid high)"), from the owner's NatureManufacture packs.
    ///
    /// The stage builder still plants every tree where it always did, and the
    /// trunk table (TreeTrunks) still stops the car at each one. What changed:
    /// the broadleaf trees' crossed cards live in their own chunk meshes
    /// ("B" chunks), and this component holds the same trees - plus the extra
    /// canopy, the understory, the ground cover and the far slopes - as an
    /// instance table per chunk. Per chunk, per re-bucket:
    ///   - out of view or past <see cref="farRange"/>: the chunk's cards draw
    ///     (mirrors and reflection views looking back still see something);
    ///   - otherwise its cards are hidden and every plant in it is drawn,
    ///     GPU-instanced, at HIGH (LOD0) / MID (LOD1) / LOW (LOD2) by its own
    ///     distance, and past <see cref="meshRange"/> as the SAME tree's
    ///     impostor (LOD3, the pack's cross-card of that beech). Owner: "I'm
    ///     not sure I want different tree types used in distance. I'd rather
    ///     increase tree density (use lower LOD as necessary)".
    /// No GameObject per plant, no LODGroup.
    ///
    /// Leaves follow the season: each variant carries its own leaf and
    /// impostor material per dress (Winter, Spring, Summer, Fall, Snow).
    ///
    /// F8 flips between the mesh forest and the old cards everywhere (A/B).
    /// </summary>
    /// <summary>FOREST DETAIL (VISUALS): how far the mesh forest keeps its
    /// detail. Read when a stage loads.</summary>
    public static class ForestDetailPrefs
    {
        const string Key = "psx.forestdetail";
        static readonly string[] Names = { "LOW", "MEDIUM", "HIGH" };
        static readonly float[] Scales = { 0.65f, 1f, 1.5f };
        static int cached = -1;
        /// <summary>A perf tour's forest=0|1|2: this run only, never saved
        /// (-1: the player's own setting).</summary>
        public static int Override = -1;
        public static int Level
        {
            get { if (Override >= 0) return Mathf.Clamp(Override, 0, 2); if (cached < 0) cached = Mathf.Clamp(PlayerPrefs.GetInt(Key, 1), 0, 2); return cached; }
            set { cached = Mathf.Clamp(value, 0, 2); PlayerPrefs.SetInt(Key, cached); PlayerPrefs.Save(); }
        }
        public static float Scale => Scales[Level];
        public static string Label => Names[Level];
        public static void Cycle() => Level = (Level + 1) % 3;
    }

    // (after the chase camera's LateUpdate at 0 and the cockpit's at 1000:
    // the GPU path culls against the view as this frame will draw it)
    [DefaultExecutionOrder(1500)]
    public partial class MeshForest : MonoBehaviour
    {
        [System.Serializable]
        public class Variant
        {
            public Mesh[] lods = new Mesh[4];      // HIGH, MID, LOW, IMPOSTOR (may be null)
            public Material[] mats;                // per submesh (bark ... leaves)
            public bool[] leafSub;                 // per submesh: follows the dress
            public float height = 1f;              // model's own height (m)
            /// <summary>The LOD node's transform inside its FBX (unit scale,
            /// axis conversion), base at y = 0: applied before each plant's
            /// own placement.</summary>
            public Matrix4x4 pre = Matrix4x4.identity;
            /// <summary>The impostor node's transform, likewise.</summary>
            public Matrix4x4 impostorPre = Matrix4x4.identity;
            /// <summary>Leaves per dress (5); null entry = bare.</summary>
            public Material[] leafDress;
            /// <summary>Impostor material per dress (5).</summary>
            public Material[] impostorDress;
            /// <summary>Drawn no farther than this (m); 0 = the whole range.</summary>
            public float maxDist;
            /// <summary>&gt; 0: at its draw limit this plant THINS OUT - every
            /// instance leaves at its own distance between thin x the limit and
            /// the limit - instead of all of them on one ring (owner 2026-11-08:
            /// "after the blue line I drew ... there is no foliage").</summary>
            public float thin;
            /// <summary>Casts shadows at HIGH and MID (trees, not grass).</summary>
            public bool shadows = true;
            /// <summary>TREE MATCH (2026-11-16; owner: "trees full of leaves (or
            /// different color trees in Fall) pop-in as you approach and then
            /// the tree is leafless"): the far level made FROM THIS TREE - a
            /// cross of two cards wearing a picture of its own LOW mesh in
            /// each dress (bare where the mesh is bare, the mesh's own leaf
            /// colour where it is in leaf), in place of the pack's sheet of a
            /// tree in summer leaf, darkened or recoloured. Null on a kit made
            /// before it, and with PSX_TREE_MATCH=0 never used.</summary>
            public Mesh matchLod;
            /// <summary>The match card's material per dress (5).</summary>
            public Material[] matchDress;
            /// <summary>The match card's node transform (its origin the mesh's own).</summary>
            public Matrix4x4 matchPre = Matrix4x4.identity;

            /// <summary>This species with its match card as its far level.</summary>
            public Variant WithMatch()
            {
                var v = (Variant)MemberwiseClone();
                v.lods = new[] { lods.Length > 0 ? lods[0] : null, lods.Length > 1 ? lods[1] : null, lods.Length > 2 ? lods[2] : null, matchLod };
                v.impostorDress = matchDress;
                v.impostorPre = matchPre;
                // (a tree that had no far level - it thinned out and ended at the mesh range - now stands to the far range like the rest)
                if (lods.Length < 4 || lods[3] == null) v.thin = 0f;
                return v;
            }
            public bool HasMatch => matchLod != null && matchDress != null && matchDress.Length == 5;
        }

        [System.Serializable]
        public class Chunk
        {
            public Vector3 centre;                 // local to this transform
            public float radius;
            public Renderer[] cards;               // the chunk's "B" cards and crowns
            public int start, count;               // into the instance arrays
        }

        public Variant[] variants;
        public Chunk[] chunks;
        public Vector4[] inst;                     // local x, y, z, yaw (deg)
        public Vector2[] instScaleVar;             // x = scale, y = variant index
        /// <summary>Optional per plant (empty = all upright): pitch and roll
        /// in degrees, laying ground litter, branches, logs and stones on
        /// the slope.</summary>
        public Vector2[] instTilt;
        public Material[] leafByDress = new Material[5];
        /// <summary>TREE MATCH (stages): the "B" cards' and crowns' atlas per dress (5), cut from the kit's
        /// own match sheets - the cells of the broadleaf trees the meshes stand for; the conifers' cells as
        /// they were. Null (a scene built before it, or PSX_TREE_MATCH=0 / PSX_TREE_MATCH_CARDS=0): the
        /// retro billboards, as before.</summary>
        public Texture2D[] matchCardAtlas;

        // The MEDIUM distances (ForestDetailPrefs scales them): the 100/200/320
        // of the pop-in round dropped the owner under 20 FPS with leaves on.
        public float lod0To = 65f, lod1To = 140f, meshRange = 240f, farRange = 2200f;
        /// <summary>Hand-over bands (m) ending at lod0To, lod1To and meshRange:
        /// inside one, a plant draws at both levels, each dithered (PSX/Lit's
        /// PSX_LODFADE) so the two add up to one - no pop, no shape jump.
        /// Owner 2026-11-08: "I see plants, bushes, trees pop-in often. Trees
        /// change shape (especially in winter) as they pop-in."</summary>
        public float band0 = 12f, band1 = 16f, band2 = 20f;
        /// <summary>A plant with a draw limit dissolves over this share of it.</summary>
        public float limitBand = 0.2f;

        /// <summary>A STREAMED forest (Charlotte's mesh trees, MeshForest.Pool.cs):
        /// fixed slots of instances that tiles fill and give back. Set before
        /// the component wakes (CreatePool).</summary>
        [System.NonSerialized] public bool pooled;
        /// <summary>Where the dress comes from (null: SeasonDress.AppliedDress).</summary>
        [System.NonSerialized] public System.Func<int> dressSource;
        /// <summary>THIS forest's hand-over bands as a share of each level's
        /// distance (&lt;= 0: the fixed band0/1/2). Set before the component wakes
        /// (CreatePool); the perf tour's bandshare= still wins. The tree pop
        /// round of 2026-11-16 (Providence Rd walk, 300 -> 20 m): PopFix pushed
        /// the city's levels out to 102/140/240 m (HIGH 124/210/360) but left
        /// the bands at 12/16/20 m (18/24/30), so every switch dissolved in a
        /// few metres and read as a pop; the bands at their widest cut the
        /// walk's visible pop 35 % on LOW, 49 % on MEDIUM and 82 % on HIGH.</summary>
        [System.NonSerialized] public float bandShare = -1f;

        public static bool MeshTreesOn = true;
        /// <summary>A/B (perf tour ab=MeshForest.PopFix:0 = the forest as PC26
        /// drew it): the pop-in round of 2026-11-11.</summary>
        public static bool PopFix = true;
        /// <summary>PopFix: each FOREST DETAIL's level distances x (LOW, MEDIUM,
        /// HIGH): LOW 91/109/203 m, MEDIUM 120/140/240 m, HIGH 146/210/360 m.
        /// Second step (fall runs from the PC26 exe): the nearest-level switch
        /// (crowns losing branches) is the only forest level change the lag pair
        /// can see on any setting, so only it moves again: LOW 0.40 -> 0.27 for
        /// +0.7 M triangles, HIGH 0.12 -> 0.05 for +2.2 M (+0.55 ms GPU at 1080p);
        /// the middle switch and the impostor swap measured no part of it.</summary>
        static readonly Vector3[] PopLodMul = { new Vector3(2.15f, 1.2f, 1.3f), new Vector3(1.85f, 1f, 1f), new Vector3(1.5f, 1f, 1f) };
        public static Vector3 TourLodMul = Vector3.one;
        public static float TourBandShare = -1f;
        static MeshForest instance;
        bool inited;

        const int L = 4;
        Matrix4x4[] localM;
        Matrix4x4[] worldM;                        // localM under the root as of the last Bake
        Vector3[] worldP;
        Matrix4x4 bakedRoot;
        Matrix4x4[] impFix;                        // per variant: pre^-1 * impostorPre
        // Far chunks (wholly past meshRange) draw their impostors from lists
        // made once, per chunk per variant: no per-tree work at distance.
        Matrix4x4[][][] farLists;                  // [chunk][variant]
        readonly System.Collections.Generic.List<int> farVisible = new System.Collections.Generic.List<int>();
        // THE OTHER CAMERAS (mirrors, the car-reflection probe's faces): the
        // canopy near the car as impostors only - two cards a tree, one draw a
        // species. Owner 2026-11-09: "performance is very poor when leaves are
        // on trees ... especially in cockpit view with mirrors" - every one of
        // those cameras drew the WHOLE mesh forest again.
        Matrix4x4[][] mirrorM;
        int[] mirrorN;
        Camera mainCam;
        Matrix4x4[][] bucket;                      // [variant * L + lod]
        int[] bucketN;
        Vector3 lastCam = new Vector3(1e9f, 0f, 0f);
        Matrix4x4 lastRoot;
        int lastDress = -2;
        bool lastOn = true;
        readonly Plane[] planes = new Plane[6];
        Material[][] matsNow;
        Material[] impNow;
        float[] maxD2;
        Vector4[][] fadeOf;                        // [variant][lod]: _PSXLodFade
        bool[][] handOver;                         // [variant][lod]: dithers into lod + 1
        readonly System.Collections.Generic.Dictionary<(Material, Vector4, float, Vector4), Material> faded =
            new System.Collections.Generic.Dictionary<(Material, Vector4, float, Vector4), Material>();

        void Awake() => Init();

        /// <summary>TREE MATCH: every tree's far level is a picture of its own
        /// mesh in today's dress (Variant.matchLod), standing on the mesh's own
        /// origin so both draw the same per-tree colour. PSX_TREE_MATCH=0 (or a
        /// perf tour's ab=MeshForest.TreeMatch:0 before the stage loads): the
        /// pack's sheets as before.</summary>
        public static bool TreeMatch = System.Environment.GetEnvironmentVariable("PSX_TREE_MATCH") != "0";
        /// <summary>The tree match's far levels for the species that had none (Charlotte's young trees and crape
        /// myrtle stand-ins: they thinned out and ended at the mesh range). PSX_TM_NEWFAR=0 (the cost tools'
        /// decomposition): they keep none.</summary>
        public static bool NewFarLevels = System.Environment.GetEnvironmentVariable("PSX_TM_NEWFAR") != "0";
        /// <summary>THE STAGES' HAND-OVER BANDS as a share of each level's
        /// distance (the city's pool has its own: CityMeshTrees.LodFadeShare).
        /// The 11-11 lag-pair round found wider bands no help on a stage - with
        /// far cards that did not match their meshes, a longer dissolve only
        /// showed the mismatch for longer. With the tree-match cards both sides
        /// of a hand-over are the same tree. PSX_FOREST_LODFADE=0.6 (a share
        /// between 0 and 1) widens them; unset or 0: the fixed 12/16/20 m
        /// (x FOREST DETAIL) as before, until the stage walks say otherwise.</summary>
        public static float StageBandShare = StageShare();
        static float StageShare()
        {
            var s = System.Environment.GetEnvironmentVariable("PSX_FOREST_LODFADE");
            return s != null && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) && f > 0f && f < 1f ? f : -1f;
        }
        /// <summary>A FAR CARD'S GROWTH WITH DISTANCE (PSX/Lit _PSXFarGrow: x 2.4
        /// by 1.8 km since 2026-11-09, so a far slope of narrow cards stays a
        /// closed canopy). A tree that is 2.4 times its size at 1.8 km and its
        /// own size at 450 m shrinks as it is approached, and the trees it hid
        /// come out from behind it. PSX_FOREST_FARGROW=1 (or 1.5 ...) sets the
        /// factor for this process; unset: each card's own.</summary>
        public static float FarGrowZ = FarGrowEnv();
        static float FarGrowEnv()
        {
            var s = System.Environment.GetEnvironmentVariable("PSX_FOREST_FARGROW");
            return s != null && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) && f >= 1f ? f : -1f;
        }
        /// <summary>Per species: its far card stands on its mesh's origin (the
        /// card's matrix takes the mesh's translation bit for bit).</summary>
        bool[] sameOrigin;
        public int MatchCount { get; private set; }

        void Init()
        {
            instance = this;
            inited = true;
            // (a copy: the kit's own list - a scene's, the city kit asset's - stays as it was built)
            sameOrigin = new bool[variants.Length];
            MatchCount = 0;
            if (TreeMatch)
            {
                Variant[] mine = null;
                for (int v = 0; v < variants.Length; v++)
                {
                    if (variants[v] == null || !variants[v].HasMatch) continue;
                    // (cost tools, PSX_TM_NEWFAR=0: a species that had no far level keeps none)
                    if (!NewFarLevels && (variants[v].lods.Length < 4 || variants[v].lods[3] == null)) continue;
                    mine ??= (Variant[])variants.Clone();
                    mine[v] = variants[v].WithMatch();
                    sameOrigin[v] = true;
                    MatchCount++;
                }
                if (mine != null) variants = mine;
            }
            float q = ForestDetailPrefs.Scale;
            lod0To *= q; lod1To *= q; meshRange *= q;
            band0 *= q; band1 *= q; band2 *= q;
            if (PopFix)
            {
                // THE POP-IN ROUND (2026-11-11, measured with the perf tour's lag
                // pair on NC 226A): what reads as pop-in is the pack beeches'
                // LOD0 -> LOD1 step (branches go) and, on LOW, every level that
                // close to the car. Wider dither bands did not help (they bring
                // the lower level nearer); pushing the levels out did: LOW's
                // level change 1.93 -> 0.68 (x 1e-3 over a 2.42 noise floor) for
                // 1.9 -> 3.0 M triangles, MEDIUM's 0.65 -> 0.30 for 3.9 -> 4.7 M.
                // HIGH as it was (0.19).
                var mul = PopLodMul[ForestDetailPrefs.Level];
                lod0To *= mul.x; lod1To *= mul.y; meshRange *= mul.z;
            }
            // the pop measure's knobs (perf tour lodmul=a,b,c&bandshare=s): each level's distance x, bands as a share
            lod0To *= TourLodMul.x; lod1To *= TourLodMul.y; meshRange *= TourLodMul.z;
            float share = TourBandShare > 0f ? TourBandShare : bandShare > 0f ? bandShare : !pooled ? StageBandShare : -1f;
            if (share > 0f)
            {
                // a hand-over band as a share of its distance, not a fixed few metres
                band0 = Mathf.Max(band0, lod0To * share);
                band1 = Mathf.Max(band1, lod1To * share);
                band2 = Mathf.Max(band2, meshRange * share);
            }
            band0 = Mathf.Min(band0, lod0To * 0.6f);
            band1 = Mathf.Min(band1, (lod1To - lod0To) * 0.9f);
            band2 = Mathf.Min(band2, (meshRange - lod1To) * 0.9f);
            // THE FAR RING AND THE MIRRORS IN THE TREES' OWN FAR CARDS (MatchCardsOn): the far range - the chunk
            // pass's, each canopy species' draw limit and the far cards' dissolve - moves out to the ring, and a
            // chunk out of the view's cone within otherRange feeds the other cameras' list (GpuFrame, flag 3)
            RingOn = TreeMatch && MatchCardsOn && MatchCount > 0 && GpuDriven;
            atlasOn = TreeMatch && MatchCardsOn && MatchCount > 0;
            if (RingOn)
            {
                farRange = Mathf.Max(farRange, pooled ? CityRingM : StageRingM);
                otherRange = Mathf.Min(OtherRangeM, Mathf.Max(farRange, meshRange));
            }
            matsNow = new Material[variants.Length][];
            impNow = new Material[variants.Length];
            maxD2 = new float[variants.Length];
            impFix = new Matrix4x4[variants.Length];
            fadeOf = new Vector4[variants.Length][];
            handOver = new bool[variants.Length][];
            float far = Mathf.Max(farRange, meshRange);
            for (int v = 0; v < variants.Length; v++)
            {
                var vr = variants[v];
                float m = vr.maxDist > 0f ? Mathf.Min(vr.maxDist * ForestDetailPrefs.Scale, far) : far;
                if (vr.lods.Length < L || vr.lods[3] == null) m = Mathf.Min(m, meshRange);
                maxD2[v] = m * m;
                impFix[v] = vr.pre.inverse * vr.impostorPre;
                fadeOf[v] = new Vector4[L];
                handOver[v] = new bool[L];
                float[] edge = { lod0To, lod1To, meshRange, far };
                float[] band = { band0, band1, band2, 0f };
                for (int l = 0; l < L; l++)
                {
                    bool has = l < vr.lods.Length && vr.lods[l] != null;
                    if (!has) continue;
                    var f = Vector4.zero;
                    if (l > 0) { f.x = edge[l - 1] - band[l - 1]; f.y = edge[l - 1]; }
                    bool next = l + 1 < L && l + 1 < vr.lods.Length && vr.lods[l + 1] != null && edge[l] < m;
                    if (next) { f.z = edge[l] - band[l]; f.w = edge[l]; handOver[v][l] = true; }
                    else if (m <= edge[l]) { f.z = m * (1f - limitBand); f.w = m; }
                    fadeOf[v][l] = f;
                }
            }
            // The hidden cards stay the canopy's SHADOW (SunShadows draws its
            // casters itself and these meshes are no renderers).
            foreach (var c in chunks)
                if (c.cards != null)
                    foreach (var r in c.cards) if (r != null) SunShadows.ShadowStandIns.Add(r);
            // THE GPU PATH (MeshForest.Gpu.cs) where the machine has compute
            // shaders and the scene carries the cull; the old per-frame
            // re-bucket everywhere else, and its tables only when it runs.
            bool gpu = GpuDriven && InitGpu();
            if (!gpu && !pooled) EnsureLegacy();
            Debug.Log($"[MeshForest] {inst.Length} plants in {chunks.Length} chunks, {variants.Length} variants; " +
                      $"LOD {lod0To}/{lod1To}/{meshRange} m (hand-over bands {band0:0}/{band1:0}/{band2:0} m), impostors to {farRange} m; F8 = A/B; " +
                      (gpu ? GpuDescribe() : "CPU lists (RenderMeshInstanced)") +
                      (TreeMatch ? $"; tree match: {MatchCount} species' far cards made from their own meshes" : "; tree match OFF (PSX_TREE_MATCH=0)") + ".");
        }

        /// <summary>A plant's matrix under the forest's root, as the builder placed it.</summary>
        Matrix4x4 LocalOf(int i)
        {
            var p = inst[i];
            float s = instScaleVar[i].x;
            var tl = instTilt != null && i < instTilt.Length ? instTilt[i] : Vector2.zero;
            return Matrix4x4.TRS(new Vector3(p.x, p.y, p.z),
                Quaternion.Euler(tl.x, 0f, tl.y) * Quaternion.Euler(0f, p.w, 0f), new Vector3(s, s, s))
                * variants[(int)instScaleVar[i].y].pre;
        }

        /// <summary>A plant's far card matrix from its mesh matrix. A match
        /// card stands on the mesh's own origin, and PSX/Lit works each tree's
        /// shade, hue and fall colour out of that origin - so it is the mesh's
        /// to the last bit, not to the rounding of two matrix products (a
        /// millimetre's difference is another number altogether).</summary>
        Matrix4x4 FarMatrix(Matrix4x4 m, int v)
        {
            var mi = m * impFix[v];
            if (sameOrigin != null && sameOrigin[v]) { mi.m03 = m.m03; mi.m13 = m.m13; mi.m23 = m.m23; }
            return mi;
        }

        /// <summary>The CPU path's tables (a matrix a plant, twice): made the
        /// first time that path draws.</summary>
        void EnsureLegacy()
        {
            if (localM != null) return;
            localM = new Matrix4x4[inst.Length];
            for (int i = 0; i < inst.Length; i++) localM[i] = LocalOf(i);
            int nb = variants.Length * L;
            bucket = new Matrix4x4[nb][];
            bucketN = new int[nb];
            for (int i = 0; i < nb; i++) bucket[i] = new Matrix4x4[256];
            mirrorM = new Matrix4x4[variants.Length][];
            mirrorN = new int[variants.Length];
            for (int v = 0; v < variants.Length; v++) mirrorM[v] = new Matrix4x4[256];
            Bake(transform.localToWorldMatrix);
        }

        /// <summary>World matrices and the far chunks' impostor lists under
        /// this root (again only if the root ever moves).</summary>
        void Bake(Matrix4x4 root)
        {
            bakedRoot = root;
            worldM = new Matrix4x4[localM.Length];
            worldP = new Vector3[localM.Length];
            for (int i = 0; i < localM.Length; i++)
            {
                worldM[i] = root * localM[i];
                worldP[i] = new Vector3(worldM[i].m03, worldM[i].m13, worldM[i].m23);
            }
            farLists = new Matrix4x4[chunks.Length][][];
            var tmp = new System.Collections.Generic.List<Matrix4x4>[variants.Length];
            for (int v = 0; v < variants.Length; v++) tmp[v] = new System.Collections.Generic.List<Matrix4x4>();
            for (int ci = 0; ci < chunks.Length; ci++)
            {
                var c = chunks[ci];
                foreach (var t in tmp) t.Clear();
                for (int i = c.start, e = c.start + c.count; i < e; i++)
                {
                    int vi = (int)instScaleVar[i].y;
                    var vr = variants[vi];
                    if (vr.lods.Length < L || vr.lods[3] == null || vr.maxDist > 0f) continue;
                    tmp[vi].Add(FarMatrix(worldM[i], vi));
                }
                farLists[ci] = new Matrix4x4[variants.Length][];
                for (int v = 0; v < variants.Length; v++)
                    farLists[ci][v] = tmp[v].Count > 0 ? tmp[v].ToArray() : null;
            }
        }

        /// <summary>Edit-mode shots (ForestShots): Update never runs there, so
        /// bucket and queue the plants for this one camera now, before its
        /// Render.</summary>
        public void DrawNow(Camera cam, bool viewFrustum = false)
        {
            if (!inited) Init();
            int dress = DressNow();
            DressMaterials(dress);
            lastDress = dress;
            // (a shot's camera takes its aspect from the texture it is about
            // to be drawn into: the chunk pass's wide cone for the plants too,
            // unless the caller has set the camera's aspect and asks for the
            // player's own cull - viewFrustum)
            if (UseGpu) { GpuDressChanged(); GpuFrame(cam, !viewFrustum); return; }
            if (pooled) return;   // a streamed forest draws on the GPU path only
            EnsureLegacy();
            cardsKnown = false;
            Rebucket(cam, transform.localToWorldMatrix, cam.transform.position);
            Draw(cam);
        }

        /// <summary>Edit-mode shots (the tree-cost round's mirror check): what an OTHER camera - a mirror, a
        /// reflection face - draws of this forest after the main view's cull (DrawNow), queued for
        /// <paramref name="cam"/>'s next render. Edit mode has no camera hook (OnEnable's), so a tool asks.</summary>
        public void DrawOtherNow(Camera cam)
        {
            if (cam == null || !MeshTreesOn) return;
            if (UseGpu) GpuOtherCamera(cam);
            else OtherCameraLegacy(cam);
        }

        /// <summary>Edit-mode shots: the old cards everywhere.</summary>
        public void CardsNow()
        {
            foreach (var c in chunks) SetCards(c, true);
            cardsKnown = false;
        }

        void OnEnable() => RenderPipelineManager.beginCameraRendering += OtherCamera;
        void OnDisable() => RenderPipelineManager.beginCameraRendering -= OtherCamera;

        void OtherCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (!MeshTreesOn || cam == null || mainCam == null) return;
            if (cam == mainCam) { if (StatsOn && UseGpu) StatsView(cam); return; }
            if (cam.cameraType != CameraType.Game || (cam.cullingMask & (1 << gameObject.layer)) == 0) return;
            long pc = PerfClock.On ? PerfClock.Now() : 0;
            if (UseGpu) GpuOtherCamera(cam);
            else OtherCameraLegacy(cam);
            if (PerfClock.On) PerfClock.Add(PerfClock.Slot.ForestOther, pc);
        }

        void OtherCameraLegacy(Camera cam)
        {
            if (mirrorM == null) return;
            for (int v = 0; v < variants.Length; v++)
            {
                int n = mirrorN[v];
                var mat = impNow != null ? impNow[v] : null;
                if (n == 0 || mat == null) continue;
                var mesh = variants[v].lods[3];
                var rp = new RenderParams(mat)
                {
                    shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true,
                    layer = gameObject.layer, camera = cam,
                };
                for (int s = 0; s < mesh.subMeshCount; s++)
                    for (int at = 0; at < n; at += 1023)
                        Graphics.RenderMeshInstanced(rp, mesh, s, mirrorM[v], Mathf.Min(1023, n - at), at);
            }
        }

        void OnDestroy()
        {
            ReleaseGpu();
            // (the B cards' tree-match copies are this forest's own: gone with it)
            foreach (var m in bCardCopy.Values) if (m != null) { if (Application.isPlaying) Destroy(m); else DestroyImmediate(m); }
            bCardCopy.Clear();
            if (chunks != null)
                foreach (var c in chunks)
                    if (c.cards != null)
                        foreach (var r in c.cards) if (r != null) SunShadows.ShadowStandIns.Remove(r);
            if (instance == this) instance = null;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (instance == this && kb != null && kb.f8Key.wasPressedThisFrame)
            {
                MeshTreesOn = !MeshTreesOn;
                Debug.Log("[MeshForest] " + (MeshTreesOn ? "MESH forest" : "CARD trees (old)"));
            }

            if (UseGpu || pooled) return;     // that path draws from LateUpdate, once the camera has moved
            var cam = FrameStart();
            if (cam == null) return;
            EnsureLegacy();
            if (cardsKnown) { cardsKnown = false; lastOn = false; }   // the GPU path was drawing: bucket afresh

            var root = transform.localToWorldMatrix;
            Vector3 camPos = cam.transform.position;
            bool moved = (camPos - lastCam).sqrMagnitude > 1f || root != lastRoot || !lastOn;
            // The view turns faster than the car moves: re-cull every 2nd
            // frame even standing still.
            long pc = PerfClock.On ? PerfClock.Now() : 0;
            if (moved || (Time.frameCount & 1) == 0) Rebucket(cam, root, camPos);
            if (PerfClock.On) PerfClock.Add(PerfClock.Slot.ForestCull, pc);
            if (moved) { lastCam = camPos; lastRoot = root; }
            lastOn = true;
            mainCam = cam;
            pc = PerfClock.On ? PerfClock.Now() : 0;
            Draw(cam);
            if (PerfClock.On) PerfClock.Add(PerfClock.Slot.ForestDraw, pc);
        }

        void LateUpdate()
        {
            if (!UseGpu) return;
            var cam = FrameStart();
            if (cam == null) return;
            GpuFrame(cam);
            lastOn = true;
            mainCam = cam;
        }

        /// <summary>The frame's camera and dress; null when nothing is to be
        /// drawn (no camera, or the A/B switch shows the old cards).</summary>
        Camera FrameStart()
        {
            var cam = Camera.main;
            if (cam == null || variants == null || variants.Length == 0) return null;
            if (!MeshTreesOn)
            {
                if (lastOn) { foreach (var c in chunks) SetCards(c, true); cardsKnown = false; }
                lastOn = false;
                return null;
            }
            int dress = DressNow();
            if (dress != lastDress) { DressMaterials(dress); lastDress = dress; GpuDressChanged(); }
            return cam;
        }

        int DressNow() => dressSource != null ? dressSource() : SeasonDress.AppliedDress;

        static void SetCards(Chunk c, bool on)
        {
            if (c.cards == null) return;
            foreach (var r in c.cards) if (r != null && r.enabled != on) r.enabled = on;
        }

        // ---- TREE MATCH: THE B CARDS IN THE KIT'S OWN TREES (2026-11-17) ----
        // Where the mesh forest does not draw - out of the view's cone (the mirrors' trees behind the car,
        // the reflection faces) and past the far range - a stage's broadleaf trees are the chunks' "B"
        // cards and crowns: the retro billboard pack's photographs in the dress (other trees, other
        // colours). Here they wear matchCardAtlas, the same cells cut from the kit's own trees in the
        // dress, through copies of whatever material SeasonDress gives them (it does not know the copies,
        // so this re-dresses them when the dress changes).
        /// <summary>PSX_TREE_MATCH_CARDS=1 puts them on. DEFAULT OFF (the billboards, as before):
        /// MEASURED 2026-11-17 (CityRefSpots.RunTreeCards / TreeMatchAudit.RunStageCards, the trees alone, same eye,
        /// before = PSX_TREE_MATCH=0): the kit's own trees on the cards match the meshes' colour and leaf state, but
        /// the CARD MESHES are the old plantings' - 1/1.3 (Charlotte's tiles) and 1/1.6 (a stage's B cards) of their
        /// mesh trees' height - and the kit's trees are narrower than the retro billboards' round crowns: the cards'
        /// cover fell to 0.3-0.4 of the mesh trees' in winter and 0.4-0.7 in fall (the retro cards: 0.9-1.1). A
        /// sparse far ring and sparse trees in the mirrors, where the owner has asked for full slopes. So OFF until
        /// the cards are baked at their mesh trees' size (a city tile re-bake; a stage scene pass with the B cards
        /// sized from their mesh variant): PSX_TREE_MATCH_CARDS=1 turns them on.
        /// TREE-COST ROUND (2026-11-17): DEFAULT ON. Where are the cards seen? NOT in the view past the far range:
        /// on the PC profile a stage camera ends every layer but the land at the far ground's edge (StageCulling,
        /// 2240 m on NC 226A, the detail fade from 1980 m) and the city camera's far plane is the live ring's edge
        /// (CityWorld.RingFarM, 1012 m) - so no B card past 2.2 km and no tile card past 1.2 km is ever drawn there.
        /// They are seen OUT OF THE VIEW'S CONE: the mirrors and the paint's reflection faces (and, in Charlotte,
        /// CityFar's far woods past the plane wear the same city atlas). So with the switch on:
        ///   1. a chunk out of the cone within <see cref="OtherRangeM"/> (1200 m) puts ITS TREES' OWN FAR CARDS in the
        ///      other cameras' list and hides its cards (<see cref="RingOn"/>, cull flag 3): the mirrors show the very
        ///      trees, the same size, picture, dress and per-tree colour as the view's far level - no card to size,
        ///      no tile re-bake, no scene pass;
        ///   2. the cards left (a mirror's view past that, F8, a city tile before its fit, CityFar's woods) wear the
        ///      kit's atlas as above (stage: here; city: CityMeshTrees.CardAtlas).
        /// MEASURED (RunStageRing / RunCityRing, a rear camera at the driver's eye drawn as an other camera, NC 226A
        /// 7900 m and Charlotte arterial): the mirror's tree cover, cards / the trees' own far cards - retro 1.00 /
        /// 0.97 but leafy in winter (226A: hue 61 v 51), kit atlas 0.87 / 0.98 (stage winter / fall), 0.94 / 0.97
        /// (city); with 1 they are the far cards (1.00). The other cameras' list grows 11.9 k -> 50.9 k far cards
        /// (226A), 2.1 k -> 11.5 k (Charlotte); the mirror's own render at 256 x 64, 192 x 64 and a 512 x 512
        /// reflection face, A/B in one process: -0.04 .. +0.18 ms (stage), -0.57 .. +0.39 ms (city), inside the
        /// editor's noise. "0": the retro billboards and the old cards in the mirrors, as before.</summary>
        public static bool MatchCardsOn = System.Environment.GetEnvironmentVariable("PSX_TREE_MATCH_CARDS") == "1";
        /// <summary>THE FAR RING on a stage: its far cards drawn to this distance (m) when <see cref="RingOn"/>, in
        /// place of the B cards past the far range. 0 (the default): the far range as it is - on the PC profile the
        /// stage camera ends every layer but the land at the far ground's edge (StageCulling: 2240 m on NC 226A, the
        /// detail fade from 1980 m), so past the 2.2 km far range no B card is drawn in the view at all; the land's
        /// far canopy is the forest there. PSX_TM_RING_M.</summary>
        public static float StageRingM = EnvM("PSX_TM_RING_M", 0f);
        /// <summary>...and a city pool's. 0 (the default): as it is - the city camera's far plane is the live ring's
        /// edge (CityWorld.RingFarM: 1012 m on the PC's baked 9 x 9 ring), so no tile card past the 1.2 km far range
        /// is ever drawn in the view; the woods past the plane are CityFar's cards (the skyline's squeeze).
        /// PSX_TM_CITYRING_M.</summary>
        public static float CityRingM = EnvM("PSX_TM_CITYRING_M", 0f);
        /// <summary>THE MIRRORS: a chunk out of the view's cone this near (m, its nearest point) puts its trees'
        /// far cards in the other cameras' list (the mirrors, the paint's reflection faces) and hides its cards;
        /// past it the cards stand (in the kit's atlas). 1200 m: the whole live city ring (its far plane is 1012 m),
        /// a stage's forest out to where a tree in a 256 x 64 mirror is a pixel or two. PSX_TM_OTHER_M (0: none).</summary>
        public static float OtherRangeM = EnvM("PSX_TM_OTHER_M", 1200f);
        static float EnvM(string k, float d)
        {
            var s = System.Environment.GetEnvironmentVariable(k);
            return s != null && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) && f >= 0f ? f : d;
        }
        /// <summary>This forest draws the mirrors' trees (and the far ring, where a ring range is set) in its trees' own
        /// far cards (Init: the tree match on, <see cref="MatchCardsOn"/>, at least one species with a match card,
        /// the GPU path).</summary>
        public bool RingOn { get; private set; }
        float otherRange;
        bool atlasOn;
        /// <summary>The B cards' light normal (PSX/Lit _PSXCrownCard), as Charlotte's tile cards take it.</summary>
        static readonly Vector2 BCardLight = new Vector2(0.32f, 0.4f);
        /// <summary>The B cards' albedo gain over the kit atlas: MEASURED (stcards226A / stcardsLSA, 2026-11-17: the
        /// forest from 25 m over the road, the foliage's mean luma, mesh forest / cards: 0.59 in fall on both stages,
        /// 0.64 and 0.78 in winter). PSX_TM_BCARDGAIN sets it for a process.</summary>
        static readonly float BCardGain = BCardGainEnv();
        static float BCardGainEnv()
        {
            var s = System.Environment.GetEnvironmentVariable("PSX_TM_BCARDGAIN");
            return s != null && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) && f > 0f ? f : 0.65f;
        }
        Renderer[] bCardR; Material[] bCardBase;
        readonly System.Collections.Generic.Dictionary<Material, Material> bCardCopy = new System.Collections.Generic.Dictionary<Material, Material>();
        int bCardDress = -1;
        public bool BCardsMatched => bCardDress >= 0;

        /// <summary>Tools (two forests of one scene share the far forest's B renderers): this forest's B cards put
        /// back in the dress's own material; the next dress applied matches them again.</summary>
        public void RestoreBCards()
        {
            if (bCardR == null) return;
            for (int i = 0; i < bCardR.Length; i++)
                if (bCardR[i] != null) bCardR[i].sharedMaterial = SeasonDress.Substitute(bCardBase[i]);
            bCardDress = -1;
        }

        void MatchBCards(int di)
        {
            // (this forest's own switch, taken at Init: two forests of one process - the cost tools' A/B - may differ)
            if (di == bCardDress || pooled || !atlasOn || chunks == null || matchCardAtlas == null || matchCardAtlas.Length < 5) return;
            if (bCardR == null)
            {
                var rs = new System.Collections.Generic.List<Renderer>();
                foreach (var c in chunks)
                    if (c != null && c.cards != null)
                        foreach (var r in c.cards) if (r != null && r.sharedMaterial != null) rs.Add(r);
                bCardR = rs.ToArray();
                bCardBase = new Material[bCardR.Length];
                for (int i = 0; i < bCardR.Length; i++) bCardBase[i] = bCardR[i].sharedMaterial;
            }
            var atlas = matchCardAtlas[di];
            for (int i = 0; i < bCardR.Length; i++)
            {
                var r = bCardR[i];
                if (r == null) continue;
                var want = SeasonDress.Substitute(bCardBase[i]);
                if (atlas == null || want == null || !want.HasProperty("_MainTex")) { r.sharedMaterial = want; continue; }
                if (!bCardCopy.TryGetValue(want, out var copy) || copy == null)
                {
                    copy = new Material(want) { name = want.name + " (tree match)", hideFlags = HideFlags.DontSave };
                    copy.SetVector("_PSXCrownCard", new Vector4(BCardLight.x, BCardLight.y, 0f, 0f));
                    if (copy.HasProperty("_Color")) copy.color = want.color * new Color(BCardGain, BCardGain, BCardGain, 1f);
                    bCardCopy[want] = copy;
                    // (the view only: the canopy's shadow - these cards are its stand-ins - stays the dress's own)
                    SunShadows.CastLike(copy, want);
                }
                // (the texel decode is read off the texture the slot holds: stamped after the swap)
                if (copy.mainTexture != atlas) { copy.mainTexture = atlas; PSXTexDecode.Stamp(copy); }
                if (r.sharedMaterial != copy) r.sharedMaterial = copy;
            }
            bCardDress = di;
        }

        void DressMaterials(int dress)
        {
            int di = dress >= 0 && dress < 5 ? dress : 2;
            MatchBCards(di);
            for (int v = 0; v < variants.Length; v++)
            {
                var vr = variants[v];
                var own = vr.leafDress;
                Material leaf = own != null && own.Length == 5 ? own[di]
                              : di < leafByDress.Length ? leafByDress[di] : null;
                var m = new Material[vr.mats.Length];
                for (int s = 0; s < m.Length; s++)
                    m[s] = vr.leafSub[s] ? leaf : vr.mats[s];
                matsNow[v] = m;
                impNow[v] = vr.impostorDress != null && vr.impostorDress.Length == 5 ? vr.impostorDress[di] : null;
            }
        }

        Vector3 bucketCam;   // the eye the lists were last made for

        void Rebucket(Camera cam, Matrix4x4 root, Vector3 camPos)
        {
            if (root != bakedRoot) Bake(root);
            bucketCam = camPos;
            // A WIDER cone than the camera's: a chunk swaps its cards for meshes
            // before it reaches the screen's edge, not on it (owner 2026-11-08:
            // "a lot of foliage and tree pop-in even at extremely low speeds").
            var wide = Matrix4x4.Perspective(Mathf.Min(cam.fieldOfView * 1.5f + 10f, 170f), cam.aspect * 1.3f,
                                             cam.nearClipPlane, cam.farClipPlane);
            GeometryUtility.CalculateFrustumPlanes(wide * cam.worldToCameraMatrix, planes);
            for (int i = 0; i < bucketN.Length; i++) bucketN[i] = 0;
            for (int v = 0; v < mirrorN.Length; v++) mirrorN[v] = 0;
            if (lastPut == null || lastPut.Length != variants.Length) lastPut = new int[variants.Length];
            for (int v = 0; v < lastPut.Length; v++) lastPut[v] = -1;
            farVisible.Clear();
            float far = Mathf.Max(farRange, meshRange);
            float r2 = meshRange * meshRange, l0 = lod0To * lod0To, l1 = lod1To * lod1To;
            float scale = root.lossyScale.x;
            for (int ci = 0; ci < chunks.Length; ci++)
            {
                var c = chunks[ci];
                Vector3 wc = root.MultiplyPoint3x4(c.centre);
                float rad = c.radius * scale;
                float dist = Vector3.Distance(wc, camPos);
                bool draw = dist - rad < far &&
                            GeometryUtility.TestPlanesAABB(planes, new Bounds(wc, Vector3.one * rad * 2f));
                SetCards(c, !draw);
                if (!draw) continue;
                if (dist - rad >= meshRange) { farVisible.Add(ci); continue; }
                for (int i = c.start, e = c.start + c.count; i < e; i++)
                {
                    int vi = (int)instScaleVar[i].y;
                    float d2 = (worldP[i] - camPos).sqrMagnitude;
                    if (d2 > maxD2[vi]) continue;
                    int lod = d2 < l0 ? 0 : d2 < l1 ? 1 : d2 < r2 ? 2 : 3;
                    Put(vi, lod, i);
                    // Inside a hand-over band - and Margin either side of it, so
                    // a bucket a metre or two stale never leaves a tree with
                    // neither level drawn: the neighbouring level too. The
                    // shader's fade (by the tree's own distance) decides what
                    // of each shows; out of its band it draws nothing.
                    float d = Mathf.Sqrt(d2);
                    if (lod < 3 && handOver[vi][lod] && d > fadeOf[vi][lod].z - Margin) Put(vi, lod + 1, i);
                    if (lod > 0 && handOver[vi][lod - 1] && d < fadeOf[vi][lod - 1].w + Margin) Put(vi, lod - 1, i);
                }
            }
        }

        const float Margin = 4f;
        int[] lastPut;   // per species: the instance last put in the other cameras' list (a hand-over puts one twice)

        void Put(int vi, int lod, int i)
        {
            int b = vi * L + lod;
            if (bucketN[b] == bucket[b].Length)
                System.Array.Resize(ref bucket[b], bucket[b].Length * 2);
            var m = lod == 3 ? FarMatrix(worldM[i], vi) : worldM[i];
            bucket[b][bucketN[b]++] = m;
            // the other cameras: a canopy tree once, as its impostor
            var vr = variants[vi];
            if (vr.lods.Length > 3 && vr.lods[3] != null && vr.maxDist <= 0f && lastPut[vi] != i)
            {
                if (mirrorN[vi] == mirrorM[vi].Length) System.Array.Resize(ref mirrorM[vi], mirrorM[vi].Length * 2);
                mirrorM[vi][mirrorN[vi]++] = lod == 3 ? m : FarMatrix(m, vi);
                lastPut[vi] = i;
            }
        }

        /// <summary>This material with a level's fade band (a copy, made once).</summary>
        Material Faded(Material m, Vector4 f, float thin = 0f, int variant = -1, int lod = 0)
        {
            if (m == null) return null;
            if (f == Vector4.zero) return m;
            // (leaflight) THE CROWN'S FRAME for its light (PSX/Lit PSXLeafLight): the tree's foot in this level's own
            // object space (xyz: the plant's point, under the mesh's pre-transform or the far card's matrix) and the
            // species' height per unit of this level's matrix scale (w; PSX/Lit reads the scale off the whole matrix),
            // so the mesh and its far card put one crown on one spot
            var crown = Vector4.zero;
            if (variant >= 0 && variants != null && variant < variants.Length && variants[variant] != null)
            {
                var vr = variants[variant];
                var mx = lod >= 3 && impFix != null && variant < impFix.Length ? FarMatrix(vr.pre, variant) : vr.pre;
                float sy = ((Vector3)mx.GetColumn(1)).magnitude;
                Vector3 foot = mx.inverse.MultiplyPoint3x4(Vector3.zero);
                if (sy > 1e-6f) crown = new Vector4(foot.x, foot.y, foot.z, vr.height / sy);
            }
            if (faded.TryGetValue((m, f, thin, crown), out var c) && c != null) return c;
            c = new Material(m) { name = m.name + "_fade", enableInstancing = true };
            if (crown.w > 0f) c.SetVector("_PSXCrownFrame", crown);
            // (the far cards' growth with distance as this process asks: PSX_FOREST_FARGROW)
            if (FarGrowZ >= 1f && m.HasProperty("_PSXFarGrow"))
            {
                var fg = m.GetVector("_PSXFarGrow");
                if (fg.z > 1f) c.SetVector("_PSXFarGrow", new Vector4(fg.x, fg.y, FarGrowZ, fg.w));
            }
            c.SetVector("_PSXLodFade", f);
            c.SetVector("_PSXLodThin", new Vector4(thin, 0f, 0f, 0f));
            c.EnableKeyword("PSX_LODFADE");
            faded[(m, f, thin, crown)] = c;
            return c;
        }

        /// <summary>The thinning for a level: only where that level's fade-out
        /// is the plant's draw limit, not a hand-over to the next level.</summary>
        float ThinFor(int v, int lod) =>
            !handOver[v][lod] && fadeOf[v][lod].w > 0f ? variants[v].thin : 0f;

        void Draw(Camera only = null)
        {
            for (int v = 0; v < variants.Length; v++)
            {
                var vr = variants[v];
                var mats = matsNow[v];
                for (int lod = 0; lod < L; lod++)
                {
                    int b = v * L + lod, n = bucketN[b];
                    if (n == 0 || lod >= vr.lods.Length) continue;
                    var mesh = vr.lods[lod];
                    if (mesh == null) continue;
                    bool imp = lod == 3;
                    int subs = imp ? mesh.subMeshCount : Mathf.Min(mesh.subMeshCount, mats.Length);
                    for (int s = 0; s < subs; s++)
                    {
                        var mat = Faded(imp ? impNow[v] : mats[s], fadeOf[v][lod], ThinFor(v, lod), v, lod);
                        if (mat == null) continue;   // bare: no leaves
                        var rp = new RenderParams(mat)
                        {
                            shadowCastingMode = ShadowCastingMode.Off,   // the sun's shadow: SunShadows' card stand-ins
                            receiveShadows = true,
                            layer = gameObject.layer,
                            camera = only,
                        };
                        for (int at = 0; at < n; at += 1023)
                            Graphics.RenderMeshInstanced(rp, mesh, s, bucket[b], Mathf.Min(1023, n - at), at);
                        if (StatsOn) { long st = (long)(mesh.GetIndexCount(s) / 3) * n; statTris += st; StatAdd(v, lod, st); statDraws += (n + 1022) / 1023; if (s == 0) statPlants += n; }
                    }
                }
            }
            // The far chunks: each one's impostor lists, as baked.
            foreach (int ci in farVisible)
            {
                var lists = farLists[ci];
                for (int v = 0; v < variants.Length; v++)
                {
                    var arr = lists[v];
                    var mat = Faded(impNow[v], fadeOf[v][3], 0f, v, 3);
                    if (arr == null || mat == null) continue;
                    var mesh = variants[v].lods[3];
                    var rp = new RenderParams(mat)
                    {
                        shadowCastingMode = ShadowCastingMode.Off,
                        receiveShadows = true,
                        layer = gameObject.layer,
                        camera = only,
                    };
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        for (int at = 0; at < arr.Length; at += 1023)
                            Graphics.RenderMeshInstanced(rp, mesh, s, arr, Mathf.Min(1023, arr.Length - at), at);
                        if (StatsOn) { long st = (long)(mesh.GetIndexCount(s) / 3) * arr.Length; statTris += st; StatAdd(v, 3, st); statDraws += (arr.Length + 1022) / 1023; if (s == 0) statPlants += arr.Length; }
                    }
                }
            }
            if (StatsOn) StatsFrame();
        }
    }
}
