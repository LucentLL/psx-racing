using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Where a tree's TRUNK is, read off the tree's own geometry.
    ///
    /// Every tree in the game is cards: two crossed quads from the builders,
    /// and anywhere from two to nine intersecting planes (each drawn twice,
    /// back to back) in the art packs. The trunk is painted where the cards
    /// CROSS — that is the one line through a crossed billboard that looks
    /// the same from every side, so it is where an artist puts it — and on
    /// the gas station's pack trees that crossing is a metre or more off the
    /// middle of either card (the painted trunk is 0.10 and 0.19 of the sheet
    /// off centre). A collider at the card's bounding-box centre would stand
    /// in the grass beside the drawn trunk.
    ///
    /// So: the cards are found as position-welded islands, each flattened to
    /// its plan segment; islands that are the same card drawn from its other
    /// side are folded together; cards whose segments cross near the middle
    /// of both are one plant, and the trunk is where they cross.
    ///
    /// A stage forest chunk is ten thousand crossings in one mesh, close
    /// enough that neighbouring crowns overlap. It is read by its LAYOUT
    /// instead — <see cref="VertsPerTree"/> vertices a tree, bottom corners
    /// first and last — which is the builder's own contract, written down
    /// here so the forest pass and the audit share it.
    /// </summary>
    public static class TreeKit
    {
        public const int RoadLayer = 8;
        public const int SolidLayer = 9;

        // ------------------------------------------------------------------
        //  What is a tree
        // ------------------------------------------------------------------
        /// <summary>A texture that is a TREE: "Tree", "Tree_01", "Trees_03",
        /// "TreeAtlas_Summer", "tree016" — but not "streetlight", whose
        /// "tree" follows a letter. The circuits' own billboards are the
        /// "Ar (n)" sheets in Art/Roads, which say nothing about trees in
        /// their name and are matched by path.</summary>
        static readonly Regex TreeTex = new Regex(@"(^|[^a-z])trees?([^a-z]|\d|$)|treeatlas", RegexOptions.IgnoreCase);
        static readonly Regex ShrubTex = new Regex(@"bush|hedge|plant|flower|nature", RegexOptions.IgnoreCase);

        public enum Kind { None, Tree, Shrub }

        public static Kind KindOf(Material m)
        {
            if (m == null) return Kind.None;
            var t = m.mainTexture;
            if (t == null) return Kind.None;
            string path = UnityEditor.AssetDatabase.GetAssetPath(t);
            if (path.Contains("/Art/Roads/Ar (")) return Kind.Tree;
            if (TreeTex.IsMatch(t.name)) return Kind.Tree;
            if (ShrubTex.IsMatch(t.name)) return Kind.Shrub;
            return Kind.None;
        }

        /// <summary>
        /// Every tree in an imported model — the gas station's, the pizzeria
        /// street's — gets a trunk, and loses the collider on its cards.
        ///
        /// The collider passes (<c>AddStationPieceColliders</c>, the pizzeria's
        /// <c>AddColliders</c>) give anything tall a MeshCollider of its own
        /// mesh, which for a tree is its CARDS: solid across the whole
        /// transparent crown, a wall ten metres wide round a trunk thirty
        /// centimetres thick, standing on the forecourt of every circuit.
        /// Taken off only where every material on the renderer is foliage, so
        /// no building that happens to wear a leaf texture on one slot loses
        /// its walls. Run AFTER the collider pass, so it has the last word.
        /// </summary>
        public static int PlantTrunks(GameObject root)
        {
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mats = r.sharedMaterials;
                bool anyTree = false, allFoliage = true;
                for (int s = 0; s < mats.Length; s++)
                {
                    var k = KindOf(mats[s]);
                    if (k == Kind.Tree) anyTree = true;
                    if (k == Kind.None) allFoliage = false;
                }
                if (!anyTree) continue;
                if (allFoliage)
                    foreach (var c in r.GetComponents<Collider>())
                        if (!c.isTrigger) Object.DestroyImmediate(c);
                // A trunk already hung here by an earlier pass is not planted
                // twice.
                if (r.transform.Find("Trunk") != null) continue;
                var mesh = mf.sharedMesh;
                for (int s = 0; s < mats.Length && s < mesh.subMeshCount; s++)
                {
                    if (KindOf(mats[s]) != Kind.Tree) continue;
                    foreach (var p in FindPlants(mesh, s, r.transform.localToWorldMatrix))
                    {
                        // A plant shorter than a person is a pot plant, not
                        // something a car should stop against.
                        if (p.height < 2f) continue;
                        AddTrunk(r.transform, p.trunk, p.trunkRadius, Mathf.Min(4f, 0.5f * p.height));
                        n++;
                    }
                }
            }
            return n;
        }

        /// <summary>AddTreeQuads writes each tree as two quads of four
        /// corners: bottom-left, top-left, top-right, bottom-right.</summary>
        public const int VertsPerTree = 8;

        /// <summary>The drawn trunk is about 5% of its card's width at the
        /// base — measured across all five season atlases (0.023-0.10, median
        /// 0.05) and the circuits' three sheets (0.034-0.056). Clamped so a
        /// sapling is not a pencil and the widest crown is not a silo.</summary>
        public static float TrunkRadiusFor(float cardWidth) =>
            Mathf.Clamp(0.025f * cardWidth, 0.12f, 0.40f);

        public struct Plant
        {
            /// <summary>World position of the trunk at the base of the tree.</summary>
            public Vector3 trunk;
            public float trunkRadius;
            /// <summary>Base to crown top, metres.</summary>
            public float height;
            public float width;
            public Bounds bounds;
            /// <summary>Distinct vertical cards (a card drawn from both sides
            /// counts once). 2 is an X.</summary>
            public int cards;
        }

        /// <summary>
        /// A mesh's corners and one submesh's triangles, readable or not.
        ///
        /// The art packs import with Read/Write off, and <c>mesh.vertices</c>
        /// on those is an error and an empty array — which the obstacle audit
        /// gave up on and measured as boxes. The read-only MeshData API is
        /// allowed to see a non-readable mesh in the EDITOR, which is the only
        /// place this runs. False when neither route answers.
        /// </summary>
        public static bool Read(Mesh mesh, int submesh, out Vector3[] verts, out int[] tris)
        {
            verts = null; tris = null;
            if (mesh == null || submesh < 0 || submesh >= mesh.subMeshCount) return false;
            if (mesh.isReadable)
            {
                verts = mesh.vertices;
                tris = mesh.GetTriangles(submesh);
                return true;
            }
            try
            {
                using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
                {
                    var md = data[0];
                    var v = new Unity.Collections.NativeArray<Vector3>(md.vertexCount,
                                Unity.Collections.Allocator.Temp);
                    md.GetVertices(v);
                    verts = v.ToArray();
                    v.Dispose();
                    var sm = md.GetSubMesh(submesh);
                    var idx = new Unity.Collections.NativeArray<int>(sm.indexCount,
                                  Unity.Collections.Allocator.Temp);
                    md.GetIndices(idx, submesh);
                    tris = idx.ToArray();
                    idx.Dispose();
                    return verts.Length > 0 && tris.Length > 0;
                }
            }
            catch (System.Exception)
            {
                verts = null; tris = null;
                return false;
            }
        }

        /// <summary>Every plant in one submesh, in world space.</summary>
        public static List<Plant> FindPlants(Mesh mesh, int submesh, Matrix4x4 toWorld)
        {
            if (mesh.name.Contains("StageForest")) return ForestPlants(mesh, toWorld);
            var result = new List<Plant>();
            if (!Read(mesh, submesh, out var v, out var tri)) return result;
            var w = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) w[i] = toWorld.MultiplyPoint3x4(v[i]);
            var islands = Islands(w, tri);

            // Each island as a card (a vertical plane) or a blob.
            var parts = new List<Part>();
            foreach (var isl in islands) parts.Add(Describe(w, tri, isl));

            // Fold a card's back face into its front: same line in plan.
            var alive = new List<Part>();
            foreach (var p in parts)
            {
                bool dup = false;
                foreach (var q in alive)
                {
                    if (!p.card || !q.card) continue;
                    if (Mathf.Abs(Vector3.Dot(p.dir, q.dir)) < 0.98f) continue;
                    if ((p.mid - q.mid).sqrMagnitude > 0.05f * 0.05f + 0.0025f * p.width * p.width) continue;
                    if (Mathf.Abs(p.width - q.width) > 0.1f + 0.05f * p.width) continue;
                    q.Absorb(p);
                    dup = true;
                    break;
                }
                if (!dup) alive.Add(p);
            }

            // Cards that cross near the middle of both are one plant.
            int n = alive.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            var crossings = new List<(int a, int b, Vector3 at)>();
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    var a = alive[i];
                    var b = alive[j];
                    if (a.card && b.card)
                    {
                        if (!Cross(a, b, out Vector3 at)) continue;
                        if (Mathf.Abs(a.baseY - b.baseY) > 0.5f + 0.1f * Mathf.Max(a.height, b.height)) continue;
                        Union(parent, i, j);
                        crossings.Add((i, j, at));
                    }
                    else
                    {
                        // A blob (a modelled trunk, a star of small faces)
                        // joins whatever it stands in the middle of.
                        var blob = a.card ? b : a;
                        var other = a.card ? a : b;
                        Vector3 d = blob.mid - other.mid; d.y = 0f;
                        if (d.magnitude < 0.25f * Mathf.Max(other.width, blob.width)) Union(parent, i, j);
                    }
                }

            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int r = Find(parent, i);
                if (!groups.TryGetValue(r, out var l)) groups[r] = l = new List<int>();
                l.Add(i);
            }
            foreach (var kv in groups)
            {
                var members = kv.Value;
                Vector3 sum = Vector3.zero;
                int crosses = 0;
                foreach (var c in crossings)
                    if (Find(parent, c.a) == kv.Key) { sum += c.at; crosses++; }
                Bounds b = alive[members[0]].bounds;
                float width = 0f, baseY = float.MaxValue;
                int cards = 0;
                foreach (int m in members)
                {
                    b.Encapsulate(alive[m].bounds);
                    width = Mathf.Max(width, alive[m].width);
                    baseY = Mathf.Min(baseY, alive[m].baseY);
                    if (alive[m].card) cards++;
                }
                // A MODELLED trunk, where the tree has one — a narrow solid
                // standing on the plant's base (the pizzeria street's are
                // 0.2 x 0.5 m and 3.8 m tall under two crossed crowns) — is
                // the trunk, exactly, and the best answer there is. Then the
                // cards' crossing, then a lone card's middle.
                Part trunkPart = null;
                foreach (int m in members)
                {
                    var q = alive[m];
                    if (q.card) continue;
                    float plan = Mathf.Max(q.bounds.size.x, q.bounds.size.z);
                    if (plan < 1.2f && q.height > 1f && q.baseY - baseY < 0.5f &&
                        (trunkPart == null || q.height > trunkPart.height))
                        trunkPart = q;
                }
                Vector3 at;
                float radius = TrunkRadiusFor(width);
                if (trunkPart != null)
                {
                    at = trunkPart.bounds.center;
                    radius = Mathf.Clamp(0.5f * Mathf.Max(trunkPart.bounds.size.x, trunkPart.bounds.size.z),
                                         0.12f, 0.40f);
                }
                else if (crosses > 0) at = sum / crosses;
                else if (members.Count == 1) at = alive[members[0]].mid;
                else at = b.center;
                at.y = baseY;
                result.Add(new Plant
                {
                    trunk = at,
                    trunkRadius = radius,
                    height = b.max.y - baseY,
                    width = width,
                    bounds = b,
                    cards = cards,
                });
            }
            return result;
        }

        /// <summary>A stage forest chunk, by the builder's own layout. A tree
        /// whose second quad does not cross its first counts as ONE card —
        /// the flat tree the owner reported, however it came about.</summary>
        public static List<Plant> ForestPlants(Mesh mesh, Matrix4x4 toWorld)
        {
            var result = new List<Plant>();
            if (!Read(mesh, 0, out var v, out _)) return result;
            for (int t = 0; t + VertsPerTree <= v.Length; t += VertsPerTree)
            {
                Vector3 bl = toWorld.MultiplyPoint3x4(v[t]);
                Vector3 tl = toWorld.MultiplyPoint3x4(v[t + 1]);
                Vector3 br = toWorld.MultiplyPoint3x4(v[t + 3]);
                Vector3 bl2 = toWorld.MultiplyPoint3x4(v[t + 4]);
                Vector3 br2 = toWorld.MultiplyPoint3x4(v[t + 7]);
                Vector3 at = (bl + br) * 0.5f;
                float width = Vector3.Distance(bl, br);
                Vector3 da = br - bl, db = br2 - bl2;
                da.y = 0f; db.y = 0f;
                bool crossed = da.sqrMagnitude > 1e-4f && db.sqrMagnitude > 1e-4f &&
                               Mathf.Abs(Vector3.Dot(da.normalized, db.normalized)) < 0.5f &&
                               ((bl2 + br2) * 0.5f - at).sqrMagnitude < 0.01f;
                var b = new Bounds(at, Vector3.zero);
                for (int k = 0; k < VertsPerTree; k++) b.Encapsulate(toWorld.MultiplyPoint3x4(v[t + k]));
                result.Add(new Plant
                {
                    trunk = at,
                    trunkRadius = TrunkRadiusFor(width),
                    height = tl.y - bl.y,
                    width = width,
                    bounds = b,
                    cards = crossed ? 2 : 1,
                });
            }
            return result;
        }

        /// <summary>
        /// Triangles of CARDS that are drawn from one side only: the island
        /// is a card (see <see cref="Describe"/>) and the triangle has no
        /// mirrored twin on the same corners. A closed shape — a modelled
        /// trunk — is one-sided correctly, because nobody can see its inside,
        /// so it is not counted; the card is the one thing whose back is in
        /// plain view. WORLD-space corners: an art pack's mesh is Z-up in its
        /// own frame, and "standing up" only means something in the world's.
        /// </summary>
        public static int OneSidedCardTris(Vector3[] v, int[] tri)
        {
            int bad = 0;
            foreach (var isl in Islands(v, tri))
            {
                if (!Describe(v, tri, isl).card) continue;
                var fwd = new Dictionary<string, int>();
                foreach (int f in isl)
                {
                    string k = TriKey(v[tri[f * 3]], v[tri[f * 3 + 1]], v[tri[f * 3 + 2]], out bool flip);
                    fwd.TryGetValue(k, out int c);
                    fwd[k] = c + (flip ? 1000 : 1);
                }
                foreach (var kv in fwd) bad += Mathf.Abs(kv.Value % 1000 - kv.Value / 1000);
            }
            return bad;
        }

        static string TriKey(Vector3 a, Vector3 b, Vector3 c, out bool flip)
        {
            // Rotate the corners so the lowest-keyed one leads, then read the
            // winding off whether the other two run one way or the other.
            string[] k = { Q(a), Q(b), Q(c) };
            int lead = 0;
            for (int i = 1; i < 3; i++) if (string.CompareOrdinal(k[i], k[lead]) < 0) lead = i;
            string n1 = k[(lead + 1) % 3], n2 = k[(lead + 2) % 3];
            flip = string.CompareOrdinal(n1, n2) > 0;
            return k[lead] + "|" + (flip ? n2 + "|" + n1 : n1 + "|" + n2);
        }

        static string Q(Vector3 p) =>
            Mathf.RoundToInt(p.x * 200f) + "," + Mathf.RoundToInt(p.y * 200f) + "," + Mathf.RoundToInt(p.z * 200f);

        // ------------------------------------------------------------------
        class Part
        {
            public bool card;
            public Vector3 dir;       // along the card, in plan (unit), when card
            public Vector3 mid;       // plan midpoint of the card / blob centre
            public float width, height, baseY, t0, t1;
            public Bounds bounds;
            public void Absorb(Part o)
            {
                bounds.Encapsulate(o.bounds);
                baseY = Mathf.Min(baseY, o.baseY);
            }
        }

        /// <summary>
        /// A CARD is a sheet standing up whose faces mostly agree about which
        /// way they look — gently bent is still a card: the pizzeria's crowns
        /// and the forecourt props' are four-by-two quads with a curl in them,
        /// and a strict every-face-parallel test called them blobs, and a tree
        /// made of blobs has no crossing to put its trunk on. The measure is
        /// the length of the area-weighted, sign-folded horizontal normal over
        /// the total area: 1 for a flat card, ~0.9 for a 25-degree curl, ~0
        /// for a closed trunk whose faces look every way at once.
        /// </summary>
        static Part Describe(Vector3[] w, int[] tri, List<int> isl)
        {
            var p = new Part();
            Vector3 nSum = Vector3.zero, first = Vector3.zero;
            float area = 0f, uprightArea = 0f;
            bool haveFirst = false;
            var verts = new HashSet<int>();
            foreach (int f in isl)
            {
                Vector3 a = w[tri[f * 3]], b = w[tri[f * 3 + 1]], c = w[tri[f * 3 + 2]];
                verts.Add(tri[f * 3]); verts.Add(tri[f * 3 + 1]); verts.Add(tri[f * 3 + 2]);
                Vector3 n = Vector3.Cross(b - a, c - a);
                float ar = n.magnitude * 0.5f;
                if (ar < 1e-8f) continue;
                area += ar;
                n /= ar * 2f;
                if (Mathf.Abs(n.y) > 0.5f) continue;
                uprightArea += ar;
                Vector3 h = new Vector3(n.x, 0f, n.z).normalized;
                if (!haveFirst) { first = h; haveFirst = true; }
                nSum += (Vector3.Dot(h, first) >= 0f ? h : -h) * ar;
            }
            bool any = false;
            foreach (int i in verts)
            {
                if (!any) { p.bounds = new Bounds(w[i], Vector3.zero); any = true; }
                else p.bounds.Encapsulate(w[i]);
            }
            p.baseY = p.bounds.min.y;
            p.height = p.bounds.size.y;
            bool card = haveFirst && area > 0f && uprightArea >= 0.7f * area &&
                        nSum.magnitude >= 0.8f * uprightArea;
            if (card)
            {
                Vector3 nrm = nSum.normalized;
                p.card = true;
                p.dir = new Vector3(-nrm.z, 0f, nrm.x);
                float t0 = float.MaxValue, t1 = float.MinValue, s = 0f;
                foreach (int i in verts)
                {
                    float t = Vector3.Dot(w[i], p.dir);
                    t0 = Mathf.Min(t0, t); t1 = Mathf.Max(t1, t);
                    s += Vector3.Dot(w[i], nrm);
                }
                s /= verts.Count;
                p.t0 = t0; p.t1 = t1;
                p.width = t1 - t0;
                p.mid = nrm * s + p.dir * ((t0 + t1) * 0.5f);
                p.mid.y = 0f;
            }
            else
            {
                p.card = false;
                p.mid = new Vector3(p.bounds.center.x, 0f, p.bounds.center.z);
                p.width = Mathf.Max(p.bounds.size.x, p.bounds.size.z);
            }
            return p;
        }

        /// <summary>Do two cards cross in plan, somewhere in the middle 80%
        /// of both? A crown overlapping its neighbour's crosses near one
        /// card's end, not near both middles.</summary>
        static bool Cross(Part a, Part b, out Vector3 at)
        {
            at = Vector3.zero;
            float cr = a.dir.x * b.dir.z - a.dir.z * b.dir.x;
            if (Mathf.Abs(cr) < 0.2f) return false;                 // parallel-ish
            Vector3 d = b.mid - a.mid;
            float ta = (d.x * b.dir.z - d.z * b.dir.x) / cr;        // along a from a.mid
            float tb = (d.x * a.dir.z - d.z * a.dir.x) / cr;        // along b from b.mid
            if (Mathf.Abs(ta) > 0.4f * a.width || Mathf.Abs(tb) > 0.4f * b.width) return false;
            at = a.mid + a.dir * ta;
            return true;
        }

        static List<List<int>> Islands(Vector3[] w, int[] tri)
        {
            int faces = tri.Length / 3;
            var weld = new Dictionary<Vector3Int, int>();
            var id = new int[w.Length];
            for (int i = 0; i < w.Length; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(w[i].x * 100f), Mathf.RoundToInt(w[i].y * 100f),
                                       Mathf.RoundToInt(w[i].z * 100f));
                if (!weld.TryGetValue(k, out int g)) weld[k] = g = weld.Count;
                id[i] = g;
            }
            var parent = new int[faces];
            for (int f = 0; f < faces; f++) parent[f] = f;
            var owner = new Dictionary<int, int>();
            for (int f = 0; f < faces; f++)
                for (int c = 0; c < 3; c++)
                {
                    int g = id[tri[f * 3 + c]];
                    if (owner.TryGetValue(g, out int o)) Union(parent, f, o);
                    else owner[g] = f;
                }
            var groups = new Dictionary<int, List<int>>();
            for (int f = 0; f < faces; f++)
            {
                int r = Find(parent, f);
                if (!groups.TryGetValue(r, out var l)) groups[r] = l = new List<int>();
                l.Add(f);
            }
            return new List<List<int>>(groups.Values);
        }

        static int Find(int[] p, int i)
        {
            while (p[i] != i) { p[i] = p[p[i]]; i = p[i]; }
            return i;
        }

        static void Union(int[] p, int a, int b)
        {
            a = Find(p, a); b = Find(p, b);
            if (a != b) p[a] = b;
        }

        /// <summary>Stand a trunk collider on a tree that is its own object:
        /// a CHILD called "Trunk", on the solid layer, so a wheel's
        /// suspension ray never lands on it and the audits find it by name.
        /// World-space inputs; the child is positioned in world space and
        /// keeps its parent's scale out of the capsule.</summary>
        public static CapsuleCollider AddTrunk(Transform tree, Vector3 baseWorld, float radius, float height)
        {
            var go = new GameObject("Trunk");
            go.layer = SolidLayer;
            go.isStatic = true;
            go.transform.SetParent(tree, true);
            go.transform.position = baseWorld;
            go.transform.rotation = Quaternion.identity;
            // Undo the parent's scale so the capsule is sized in metres.
            Vector3 ls = tree.lossyScale;
            go.transform.localScale = new Vector3(
                ls.x != 0f ? 1f / ls.x : 1f, ls.y != 0f ? 1f / ls.y : 1f, ls.z != 0f ? 1f / ls.z : 1f);
            var cap = go.AddComponent<CapsuleCollider>();
            cap.direction = 1;
            cap.radius = radius;
            cap.height = Mathf.Max(height, radius * 2f + 0.01f);
            cap.center = new Vector3(0f, height * 0.5f, 0f);
            return cap;
        }
    }
}
