using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The primitives every small hand-built lot needs: a material, a saved
    /// mesh, a subdivided ground slab, a textured panel and a post.
    ///
    /// Lifted out of <see cref="GarageSceneBuilder"/> when the town and the
    /// seller's driveway both wanted the same five helpers. Its private copies
    /// stay where they are — that builder is a shipped scene and moving its
    /// floor out from under it buys nothing — but nothing NEW should carry a
    /// third copy of "how do you make a ground plane that does not swim".
    ///
    /// Two rules are baked in and both were bugs first:
    ///   * a ground slab is SUBDIVIDED. The PSX shader snaps vertices to a
    ///     coarse grid, so a 60 x 40 m surface drawn as two triangles has its
    ///     whole area interpolated from four snapping corners and the ground
    ///     visibly swims as you walk across it.
    ///   * a slab's collider is a thin BOX, never a MeshCollider. A plane has
    ///     no underside, and a car dropped onto a one-sided mesh at the wrong
    ///     moment falls through it.
    /// </summary>
    public static class WorldKit
    {
        public const string Root = "Assets/PSXRacing";
        public const string MatDir = Root + "/Materials";
        public const string GenDir = Root + "/Generated";

        /// <summary>Layer the wheels use to decide they are on tarmac.
        /// CarController.roadLayer is 8 and it compares by LAYER, not by name —
        /// a driving surface left on layer 0 is a surface the car drives on
        /// with off-road grip for ever, and nothing on screen says so.</summary>
        public const int RoadLayer = 8;
        /// <summary>Walls and buildings. Kept off the suspension ray mask so a
        /// wheel cannot "ground" on the side of a shop.</summary>
        public const int SolidLayer = 9;

        static Shader lit;
        public static Shader Lit => lit != null ? lit : lit = Shader.Find("PSX/Lit");

        public static Material Mat(string name, string texPath, Vector2 tiling,
                                   Color? tint = null, float cutoff = 0f)
        {
            string path = MatDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Lit);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = Lit;
            if (!string.IsNullOrEmpty(texPath))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if (tex == null) Debug.LogWarning("[WorldKit] texture missing: " + texPath);
                mat.mainTexture = tex;
            }
            mat.mainTextureScale = tiling;
            mat.color = tint ?? Color.white;
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", cutoff);
            if (cutoff > 0f) mat.renderQueue = 2450;
            // Affine off, like everything else in this project since the owner
            // asked for it: the warp is only ever right on small triangles and
            // nothing here is made of small triangles.
            if (mat.HasProperty("_Affine")) mat.SetFloat("_Affine", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static Mesh SaveMesh(Mesh mesh, string name)
        {
            if (!AssetDatabase.IsValidFolder(GenDir))
                AssetDatabase.CreateFolder(Root, "Generated");
            string path = GenDir + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>
        /// THE WIDE GARAGE DOOR of a house from the LifeSim house pack, in
        /// world space, or false if it has not got one.
        ///
        /// BY MATERIAL, and that is the whole reason this exists. Three
        /// builders already look for this door and all three search TRANSFORM
        /// NAMES — which works on house_hero, whose pack ships the door as a
        /// node called Garage_Door, and finds nothing whatever on
        /// house_simple, whose entire building is ONE mesh called "House" with
        /// a dozen material slots on it. The neighbourhood's first attempt at
        /// checking its own driveways reported "no wide Garage_Door on
        /// house_simple" and passed, which is the failure mode this project
        /// keeps paying for: a check that cannot see its subject is a check
        /// that always agrees with you.
        ///
        /// THE WIDEST, and split into islands first. The same material carries
        /// a 2.17 m shed door on the back wall, and the bounding box of the
        /// two together centres on the middle of the house — a number that
        /// would make a drive to nowhere look perfectly aimed. Islands are a
        /// gap test along the widest axis, which is enough for two doors on
        /// opposite walls and does not need the index buffer welded.
        /// </summary>
        public static bool GarageDoorOf(GameObject root, out Bounds door)
        {
            door = new Bounds();
            if (root == null) return false;
            var pts = new List<Vector3>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mesh == null || mr == null) continue;
                var mats = mr.sharedMaterials;
                var verts = mesh.vertices;
                var toWorld = mf.transform.localToWorldMatrix;
                // CONTAINS, not StartsWith, and the difference is a whole
                // build. Every model placed by Place() has already been through
                // ConvertToPSXMaterials, which rebuilds each slot as
                // "<texture>_<original name>" — the pack's Garage_Door arrives
                // here called "scenery_Garage_Door_Garage_Door". StartsWith
                // matched none of them, and the neighbourhood's garage check
                // reported "no wide Garage_Door on house_simple" on a street
                // where all fourteen of them have one.
                //
                // The TRANSFORM name too, because house_hero's pack ships its
                // door as a node and those are not renamed. One helper that
                // answers for both models is the point: two door-finders is
                // how the town and the street came to disagree about which
                // side of a house the garage is on.
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                    bool isDoor = (mats[s] != null && mats[s].name.Contains("Garage_Door"))
                               || mf.transform.name.StartsWith("Garage_Door");
                    if (!isDoor) continue;
                    var idx = mesh.GetTriangles(s);
                    for (int i = 0; i < idx.Length; i++)
                        pts.Add(toWorld.MultiplyPoint3x4(verts[idx[i]]));
                }
            }
            if (pts.Count == 0) return false;

            var all = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) all.Encapsulate(p);
            int axis = all.size.x >= all.size.z ? 0 : 2;      // horizontal only
            const float Gap = 1.0f;

            var groups = new List<List<Vector3>>();
            if (all.size[axis] < Gap) groups.Add(pts);
            else
            {
                var sorted = new List<float>(pts.Count);
                foreach (var p in pts) sorted.Add(p[axis]);
                sorted.Sort();
                var cuts = new List<float>();
                for (int i = 1; i < sorted.Count; i++)
                    if (sorted[i] - sorted[i - 1] > Gap)
                        cuts.Add((sorted[i] + sorted[i - 1]) * 0.5f);
                for (int i = 0; i <= cuts.Count; i++) groups.Add(new List<Vector3>());
                foreach (var p in pts)
                {
                    int g = 0;
                    while (g < cuts.Count && p[axis] > cuts[g]) g++;
                    groups[g].Add(p);
                }
            }

            float widest = 0f;
            bool any = false;
            foreach (var g in groups)
            {
                if (g.Count == 0) continue;
                var b = new Bounds(g[0], Vector3.zero);
                foreach (var p in g) b.Encapsulate(p);
                float w = Mathf.Max(b.size.x, b.size.z);
                // A DOOR, not a doorstep: wide enough for a car and tall
                // enough to drive through. Without the height test a flat
                // threshold strip carrying the same material wins on width.
                if (w <= 2.4f || b.size.y <= 1.8f || w <= widest) continue;
                widest = w; door = b; any = true;
            }
            return any;
        }

        /// <summary>
        /// Which sides of a slab are EDGES OF SOMETHING — a kerb of concrete
        /// standing proud of the lawn beside it — rather than seams into
        /// another surface.
        ///
        /// Named in the slab's own axes, before the transform: MinX is the
        /// face at <c>centre.x - sizeX/2</c>. Nothing here is rotated, so those
        /// are also world directions, but a call site should still be thinking
        /// about which END of its drive it means rather than about compass
        /// points.
        /// </summary>
        [System.Flags]
        public enum SlabEdge
        {
            None = 0,
            MinX = 1, MaxX = 2, MinZ = 4, MaxZ = 8,
            /// <summary>Both faces along the slab's long axis when it runs in
            /// X — the two lawn edges of a driveway, which is the common
            /// case.</summary>
            SidesZ = MinZ | MaxZ,
            SidesX = MinX | MaxX,
            All = MinX | MaxX | MinZ | MaxZ,
        }

        /// <summary>How far a skirt hangs below the surface it edges.
        ///
        /// NOT the thickness anybody sees. Every lawn edge of a DRIVABLE slab
        /// (the town's and the street's; the seller's walk-in drive has no
        /// collider to meet) has a <see cref="Feather"/> graded up to it,
        /// stopping the owner's inch (<see cref="RoadsideRules.EdgeDropM"/>)
        /// below the slab's top, so an inch of skirt is what shows and the rest
        /// hangs behind the feather.
        /// The depth is still the point: the lawn mesh under a slab is a 3 m
        /// grid, it is sunk further wherever its chords would reach the slab
        /// (see PSXRacingBuilder.SolveNbLattice), and a skirt cut to the
        /// visible inch would show daylight wherever that happened.
        /// </summary>
        public const float SkirtDepth = 0.30f;

        /// <summary>How many cells GridSlab cuts a side of this length into.
        /// ONE definition, because the neighbourhood samples the lawn mesh's
        /// own triangles (<see cref="SlabGrid"/>) and a second copy of this
        /// rounding that disagreed by one cell would measure a surface that
        /// was never built.</summary>
        public static int GridCount(float size, float cell) =>
            Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(size) / cell));

        /// <summary>
        /// THE GRID A GridSlab IS MADE OF, as numbers rather than a mesh: where
        /// its vertices are and which way each quad is split.
        ///
        /// It exists so a builder can ask what the ground mesh ACTUALLY is at a
        /// point — the triangle surface, not the height function it sampled.
        /// The two disagree between vertices by the chord sag, and on a 3 m
        /// lattice over a 15% drive meeting a level bench that is 16 cm: the
        /// lawn stood through the edge of every downhill driveway on the
        /// street, and a function-based check called it 9 cm under the
        /// concrete. A feather that has to tuck under the lawn, and a solver
        /// that has to hold the lawn under a road, both need the triangles.
        /// </summary>
        public readonly struct SlabGrid
        {
            public readonly Vector3 centre;
            public readonly float sizeX, sizeZ;
            public readonly int nx, nz;

            public SlabGrid(Vector3 centre, float sizeX, float sizeZ, float cell)
            {
                this.centre = centre;
                this.sizeX = sizeX;
                this.sizeZ = sizeZ;
                nx = GridCount(sizeX, cell);
                nz = GridCount(sizeZ, cell);
            }

            public int VertexCount => (nx + 1) * (nz + 1);
            /// <summary>Z major, X minor — GridSlab's own vertex order.</summary>
            public int Index(int x, int z) => z * (nx + 1) + x;
            /// <summary>A vertex column's offset from the centre, exactly as
            /// GridSlab lays it out.</summary>
            public float LocalX(int x) => (x / (float)nx - 0.5f) * sizeX;
            public float LocalZ(int z) => (z / (float)nz - 0.5f) * sizeZ;
            public float WorldX(int x) => LocalX(x) + centre.x;
            public float WorldZ(int z) => LocalZ(z) + centre.z;
            /// <summary>The vertex column nearest a world x — how a height
            /// function handed GridSlab's world points finds its own table.</summary>
            public int NearestX(float worldX) =>
                Mathf.Clamp(Mathf.RoundToInt(((worldX - centre.x) / sizeX + 0.5f) * nx), 0, nx);
            public int NearestZ(float worldZ) =>
                Mathf.Clamp(Mathf.RoundToInt(((worldZ - centre.z) / sizeZ + 0.5f) * nz), 0, nz);

            /// <summary>
            /// The height of the TRIANGLE over a world point, given the world Y
            /// of every vertex. GridSlab splits each quad along the diagonal
            /// from (x, z) to (x+1, z+1): triangles (x,z)(x,z+1)(x+1,z+1) and
            /// (x,z)(x+1,z+1)(x+1,z), which in cell coordinates are the halves
            /// above and below u = w. Points off the grid read its edge.
            /// </summary>
            public float SurfaceY(float[] worldY, float worldX, float worldZ)
            {
                float u = ((worldX - centre.x) / sizeX + 0.5f) * nx;
                float w = ((worldZ - centre.z) / sizeZ + 0.5f) * nz;
                int i = Mathf.Clamp(Mathf.FloorToInt(u), 0, nx - 1);
                int j = Mathf.Clamp(Mathf.FloorToInt(w), 0, nz - 1);
                float fu = Mathf.Clamp01(u - i), fw = Mathf.Clamp01(w - j);
                float h00 = worldY[Index(i, j)], h10 = worldY[Index(i + 1, j)];
                float h01 = worldY[Index(i, j + 1)], h11 = worldY[Index(i + 1, j + 1)];
                return fw >= fu
                    ? h00 + (h11 - h01) * fu + (h01 - h00) * fw
                    : h00 + (h10 - h00) * fu + (h11 - h10) * fw;
            }
        }

        /// <summary>A horizontal ground plane, subdivided into ~<paramref
        /// name="cell"/>-metre squares. UVs are WORLD-anchored so two slabs
        /// meeting at a seam do not show one.</summary>
        /// <param name="skirt">Which edges get a visible thickness. A slab
        /// with none is what this always built: an infinitely thin skin, which
        /// is right for a road (its edge is a kerb, or the next slab) and
        /// wrong for a driveway — "driveways do not have any depth/thickness.
        /// they should be a few inches thick."
        ///
        /// The skirt is a SEPARATE, RENDER-ONLY object. It gets no collider,
        /// deliberately: the player's body box sits 8-12 cm over the surface
        /// at the lowest ride height (RoadsideRules.CarClearanceFloorM), so a
        /// solid lip along the side of a drive is a wall the car stops dead
        /// against. What a car meets at a lawn edge is the <see cref="Feather"/>
        /// the builder grades up to it, which carries the collider.</param>
        /// <param name="heightAt">Optional. Given a WORLD (x, z), returns the
        /// world Y this slab should reach there. Null keeps the slab dead flat,
        /// which is what it always was.
        ///
        /// THIS IS ALSO A COLLIDER SWITCH, and that is the whole trap. A flat
        /// slab gets a BoxCollider — cheap, and correct for a plane. A slab
        /// given a height function that kept it would RENDER as hills and
        /// COLLIDE as the flat box underneath, so the car would drive along an
        /// invisible plane through the middle of the visible landscape, with
        /// nothing in the scene or the log to say so. A shaped slab gets a
        /// MeshCollider on the mesh it is actually drawing.</param>
        public static GameObject GridSlab(Transform parent, string name, Vector3 centre,
                                          float sizeX, float sizeZ, float cell,
                                          Material mat, bool solid, float tile,
                                          int layer = 0,
                                          System.Func<float, float, float> heightAt = null,
                                          SlabEdge skirt = SlabEdge.None)
        {
            var grid = new SlabGrid(centre, sizeX, sizeZ, cell);
            int nx = grid.nx, nz = grid.nz;
            var verts = new Vector3[grid.VertexCount];
            var uvs = new Vector2[verts.Length];
            var tris = new int[nx * nz * 6];

            for (int z = 0; z <= nz; z++)
                for (int x = 0; x <= nx; x++)
                {
                    float fx = grid.LocalX(x);
                    float fz = grid.LocalZ(z);
                    int v = grid.Index(x, z);
                    // LOCAL, so the height function is asked about the world
                    // point and the answer is stored relative to the slab's own
                    // origin — the transform carries centre.y, and adding it
                    // twice is how a road ends up two centimetres above itself.
                    float wy = heightAt != null
                             ? heightAt(fx + centre.x, fz + centre.z) - centre.y
                             : 0f;
                    verts[v] = new Vector3(fx, wy, fz);
                    uvs[v] = new Vector2((fx + centre.x) / tile, (fz + centre.z) / tile);
                }
            // The split SlabGrid.SurfaceY reads: (x,z)(x,z+1)(x+1,z+1) and
            // (x,z)(x+1,z+1)(x+1,z). Change one, change both.
            int t = 0;
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    int v = grid.Index(x, z);
                    tris[t++] = v; tris[t++] = v + nx + 1; tris[t++] = v + nx + 2;
                    tris[t++] = v; tris[t++] = v + nx + 2; tris[t++] = v + 1;
                }

            var mesh = new Mesh { name = name };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            SaveMesh(mesh, name);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.isStatic = true;
            go.layer = layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (solid)
            {
                if (heightAt != null)
                {
                    // The mesh it is drawing, not a box around it. See the
                    // heightAt parameter note.
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh;
                }
                else
                {
                    var col = go.AddComponent<BoxCollider>();
                    col.size = new Vector3(Mathf.Abs(sizeX), 0.4f, Mathf.Abs(sizeZ));
                    col.center = new Vector3(0f, -0.2f, 0f);
                }
            }
            if (skirt != SlabEdge.None)
                BuildSkirt(go.transform, name, centre, verts, nx, nz, tile, mat, skirt);
            return go;
        }

        /// <summary>
        /// The SIDE of a slab: the band of concrete between its surface and the
        /// ground it is lying on.
        ///
        /// Hung from the slab's own boundary vertices, which is the only way it
        /// can be right — the surface follows a height function and a skirt
        /// built from the slab's nominal rectangle would part company with it
        /// the moment the ground moved. Every top vertex here is a vertex the
        /// surface mesh already has, so the two share an edge exactly and no
        /// seam can open between them.
        ///
        /// A CHILD, and render-only. Separate so the collider stays the flat
        /// surface (see the skirt parameter on GridSlab); a child so it moves,
        /// hides and gets deleted with the slab it belongs to rather than
        /// becoming litter in the hierarchy the next builder has to know about.
        ///
        /// WOUND OUTWARD, tested rather than reasoned. The four edges want four
        /// different windings and the two axes are not symmetric — Unity's
        /// front face is <c>Cross(b - a, c - a)</c>, which for the same vertex
        /// order gives +Z on one pair of edges and -X on the other. A slab
        /// wound the wrong way renders NOTHING and says nothing about it, which
        /// this project has now paid for twice; so build the quad, measure the
        /// normal it came out with, and flip it if it is facing into the lawn.
        /// </summary>
        static void BuildSkirt(Transform slab, string name, Vector3 centre,
                               Vector3[] verts, int nx, int nz, float tile,
                               Material mat, SlabEdge edges)
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();

            void Edge(SlabEdge which, Vector3 outward, Vector3 along,
                      System.Func<int, int> index, int count)
            {
                if ((edges & which) == 0) return;
                for (int i = 0; i < count; i++)
                {
                    Vector3 t0 = verts[index(i)], t1 = verts[index(i + 1)];
                    Vector3 b0 = t0 - Vector3.up * SkirtDepth;
                    Vector3 b1 = t1 - Vector3.up * SkirtDepth;
                    // Runs with the edge in world metres so a long drive's side
                    // is not one stretched texel, and DOWN the face on v — the
                    // same world-anchored convention the surface uses, so the
                    // grain of the concrete carries over the lip.
                    float u0 = Vector3.Dot(t0 + centre, along) / tile;
                    float u1 = Vector3.Dot(t1 + centre, along) / tile;
                    int b = v.Count;
                    v.Add(t0); v.Add(t1); v.Add(b0); v.Add(b1);
                    uv.Add(new Vector2(u0, 0f)); uv.Add(new Vector2(u1, 0f));
                    uv.Add(new Vector2(u0, SkirtDepth / tile));
                    uv.Add(new Vector2(u1, SkirtDepth / tile));

                    // t0, b0, b1 then t0, b1, t1 — one winding; the test below
                    // decides whether it is this one or its mirror.
                    bool flip = Vector3.Dot(Vector3.Cross(b0 - t0, b1 - t0), outward) < 0f;
                    if (flip)
                    {
                        tri.Add(b + 0); tri.Add(b + 3); tri.Add(b + 2);
                        tri.Add(b + 0); tri.Add(b + 1); tri.Add(b + 3);
                    }
                    else
                    {
                        tri.Add(b + 0); tri.Add(b + 2); tri.Add(b + 3);
                        tri.Add(b + 0); tri.Add(b + 3); tri.Add(b + 1);
                    }
                }
            }

            // The surface's vertex grid, exactly as GridSlab laid it out: z
            // major, x minor. The two Z edges are its first and last ROWS and
            // run in x; the two X edges are its first and last COLUMNS and run
            // in z.
            int Row(int z, int x) => z * (nx + 1) + x;
            Edge(SlabEdge.MinZ, Vector3.back,    Vector3.right,   i => Row(0, i),  nx);
            Edge(SlabEdge.MaxZ, Vector3.forward, Vector3.right,   i => Row(nz, i), nx);
            Edge(SlabEdge.MinX, Vector3.left,    Vector3.forward, i => Row(i, 0),  nz);
            Edge(SlabEdge.MaxX, Vector3.right,   Vector3.forward, i => Row(i, nx), nz);
            if (tri.Count == 0) return;

            var mesh = new Mesh { name = name + "Edge" };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            SaveMesh(mesh, name + "Edge");

            var go = new GameObject(name + "Edge");
            go.transform.SetParent(slab, false);
            go.transform.localPosition = Vector3.zero;
            go.isStatic = true;
            go.layer = slab.gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>
        /// A GROUND surface faces UP. Cheap, and it is the check that was
        /// missing: a mesh wound the wrong way renders NOTHING from above while
        /// its MeshCollider carries on working perfectly, so the car drives
        /// across a piece of tarmac that is not there and no log line, no
        /// self-test and no audit says a word. The cul-de-sac shipped that way.
        ///
        /// Averaged rather than sampled, because a single vertex on a steep
        /// batter can legitimately lean past horizontal while the surface as a
        /// whole is still a floor.
        /// </summary>
        static void AssertFacesUp(Mesh mesh, string name)
        {
            var ns = mesh.normals;
            if (ns == null || ns.Length == 0) return;
            Vector3 sum = Vector3.zero;
            foreach (var n in ns) sum += n;
            if (sum.y < 0f)
                Debug.LogError("[WorldKit] " + name + " is wound INSIDE OUT — its " +
                               "average normal points down (" + (sum / ns.Length) +
                               "), so it will be invisible from above and solid " +
                               "underfoot. Reverse the triangle winding.");
        }

        /// <summary>
        /// A DISC, in rings and sectors, with the same contract as GridSlab:
        /// world-anchored UVs so it seams with the slabs around it, an optional
        /// world height function, and a MeshCollider when it is shaped.
        ///
        /// The one shape a rectangular grid cannot make, and a cul-de-sac is
        /// exactly that shape. Written for the turning head at the top of the
        /// player's street, where a hammerhead of boxes would have read as a
        /// widened road rather than as the end of one.
        /// </summary>
        /// <param name="cell">Target edge length, used for BOTH the ring pitch
        /// and the sector count, so the facets stay roughly square rather than
        /// becoming long thin wedges near the rim.</param>
        public static GameObject Disc(Transform parent, string name, Vector3 centre,
                                      float radius, float cell,
                                      Material mat, bool solid, float tile,
                                      int layer = 0,
                                      System.Func<float, float, float> heightAt = null)
        {
            int rings = Mathf.Max(2, Mathf.RoundToInt(radius / cell));
            int sectors = DiscSectors(radius, cell);

            var verts = new Vector3[1 + rings * sectors];
            var uvs = new Vector2[verts.Length];

            void Put(int idx, float lx, float lz)
            {
                // LOCAL, for the reason GridSlab spells out: the transform
                // already carries centre.y, and adding it twice puts the
                // surface two centimetres above itself.
                float wy = heightAt != null
                         ? heightAt(lx + centre.x, lz + centre.z) - centre.y
                         : 0f;
                verts[idx] = new Vector3(lx, wy, lz);
                uvs[idx] = new Vector2((lx + centre.x) / tile, (lz + centre.z) / tile);
            }

            Put(0, 0f, 0f);
            for (int r = 1; r <= rings; r++)
            {
                float rad = radius * r / rings;
                for (int s = 0; s < sectors; s++)
                {
                    float a = s * 2f * Mathf.PI / sectors;
                    Put(1 + (r - 1) * sectors + s, Mathf.Cos(a) * rad, Mathf.Sin(a) * rad);
                }
            }

            // WOUND TO FACE UP, and the first version was not.
            //
            // Sectors run anticlockwise in the XZ plane (increasing angle), and
            // fanning centre -> s -> s+1 that way gives a DOWNWARD normal:
            // Cross((1,0,0), (0,0,1)) = (0,-1,0). GridSlab's quads go +z then
            // +x, which is the other way round, and that is the one that works.
            // The mesh rendered nothing at all from above — back-face culled —
            // while its MeshCollider carried on working perfectly, so the car
            // drove across a turning head made of grass. Reported in three
            // words: "the culdasec has no asphalt."
            var tris = new int[sectors * 3 + (rings - 1) * sectors * 6];
            int t = 0;
            for (int s = 0; s < sectors; s++)                    // the middle fan
            {
                int a = 1 + s, b = 1 + (s + 1) % sectors;
                tris[t++] = 0; tris[t++] = b; tris[t++] = a;
            }
            for (int r = 1; r < rings; r++)                      // and the rings
                for (int s = 0; s < sectors; s++)
                {
                    int i0 = 1 + (r - 1) * sectors + s;
                    int i1 = 1 + (r - 1) * sectors + (s + 1) % sectors;
                    int o0 = 1 + r * sectors + s;
                    int o1 = 1 + r * sectors + (s + 1) % sectors;
                    tris[t++] = i0; tris[t++] = o1; tris[t++] = o0;
                    tris[t++] = i0; tris[t++] = i1; tris[t++] = o1;
                }

            var mesh = new Mesh { name = name };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssertFacesUp(mesh, name);
            SaveMesh(mesh, name);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.isStatic = true;
            go.layer = layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var dmr = go.AddComponent<MeshRenderer>();
            if (mat != null) dmr.sharedMaterial = mat;
            dmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dmr.receiveShadows = false;
            if (solid)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
            }
            return go;
        }

        /// <summary>How many sectors <see cref="Disc"/> cuts a circle into, and
        /// therefore how many vertices its rim has. Public because a kerb
        /// round a turning head has to stand on the rim the disc ACTUALLY has
        /// — a polygon whose chords sag 5 cm inside the true circle at a 10 m
        /// radius — or a slot of lawn opens between the two.</summary>
        public static int DiscSectors(float radius, float cell) =>
            Mathf.Max(8, Mathf.RoundToInt(2f * Mathf.PI * radius / cell));

        // ------------------------------------------------------------------
        //  Where a surface meets the lawn: kerbs and feathers
        // ------------------------------------------------------------------
        /// <summary>
        /// The kerb's face above the tarmac: 14 cm, the drawn height the town
        /// and the street have always had (the old kerb boxes stood 16 cm
        /// over a road laid at 2 cm). PSXRacingBuilder.StreetKerbHeight's
        /// 15 cm US barrier curb is the circuits' version of the same stone.
        /// </summary>
        public const float KerbHeightM = 0.14f;
        /// <summary>
        /// The kerb's width, and the length of its collider's climb.
        ///
        /// 0.45 m for the reason PSXRacingBuilder.StreetKerbRamp is 0.45: a
        /// 0.14 m face climbed over 0.45 m is a 31% ramp whose normal is 0.95
        /// from vertical, above CollisionResponder's 0.7 landing test, so a
        /// body touch is a landing and never a crash. The drawn stone was
        /// 0.40 wide, which would have made the climb 35% — past the 33%
        /// every mountable kerb in this project is held to — so the stone
        /// grew 5 cm rather than the ramp outrunning it.
        /// </summary>
        public const float KerbWidthM = 0.45f;
        /// <summary>How far the top of the drawn face leans out over its foot
        /// — PSXRacingBuilder.StreetKerbFaceBatter's 3 cm. Real kerb stones
        /// are battered, and a face with a hair of lean never reads as a
        /// degenerate vertical sliver when it shrinks to nothing at a
        /// dropped kerb.</summary>
        public const float KerbFaceBatterM = 0.03f;
        /// <summary>
        /// A DROPPED KERB's transition: the stone falls from full height to
        /// flush over this length on either side of an entrance. Real
        /// transition kerbs are about a metre; half again that, because the
        /// end of a raised stone is otherwise a 14 cm block standing across
        /// the line of a wheel running along the gutter.
        /// </summary>
        public const float KerbTaperM = 1.5f;
        /// <summary>How far the drawn back face hangs below the verge it meets,
        /// so no angle shows daylight under the stone.</summary>
        const float KerbBackHangM = 0.10f;

        /// <summary>
        /// The steepest a <see cref="Feather"/> falls from a surface's edge to
        /// the lawn: 1V:8H, 12.5%. Under RoadsideRules.RecoverableSlope (1V:6H)
        /// and under the 13% the town slabs were specified to feather at, so a
        /// feather is recoverable ground whatever it borders.
        /// </summary>
        public const float FeatherGrade = 0.125f;
        /// <summary>A feather is never narrower than this, however little it
        /// has to fall — a 5 cm drop over less would still be a visible
        /// bevel rather than lawn meeting concrete.</summary>
        public const float FeatherMinRunM = 0.6f;
        /// <summary>...nor wider than this. A feather that would need more is
        /// standing over a hole in the lawn, and past here it steepens rather
        /// than wandering across the garden.</summary>
        public const float FeatherMaxRunM = 2.5f;
        /// <summary>Columns across a feather before its toe. The lawn under
        /// it is a 3 m lattice with creases; five samples across a 1.6 m
        /// verge follow a crease closely enough that the two surfaces only
        /// ever cross at the toe.</summary>
        const int FeatherColumns = 4;
        /// <summary>A feather's stations are no further apart than this along
        /// a straight edge.</summary>
        const float FeatherStationPitchM = 2f;

        /// <summary>One cross-section of a kerb run.</summary>
        public struct EdgeStation
        {
            /// <summary>The kerb's foot: on the edge of the surface it borders,
            /// at that surface's height there.</summary>
            public Vector3 foot;
            /// <summary>Horizontal, unit, away from the surface.</summary>
            public Vector3 outward;
            /// <summary>The face's height here: <see cref="KerbHeightM"/> on a
            /// run, falling to zero at a dropped kerb.</summary>
            public float lift;
            /// <summary>The top of the stone at its back edge, where the
            /// verge behind it takes over.</summary>
            public Vector3 BackTop => foot + outward * KerbWidthM + Vector3.up * lift;
        }

        /// <summary>
        /// SPLIT A SURFACE'S EDGE INTO KERB RUNS, dropped at every entrance.
        ///
        /// The edge is a polyline ON the surface — its boundary vertices, so
        /// the stone's foot lies on the tarmac's own edge and no sliver opens
        /// between them — with an outward vector at each point. Wherever
        /// <paramref name="inGap"/> says a point is inside an entrance there
        /// is no kerb at all (the entrance's own slab runs out to the road),
        /// and for <see cref="KerbTaperM"/> either side the stone falls to
        /// flush, so a run never ENDS standing up.
        ///
        /// Gaps are found by walking the edge at 5 cm and bisecting each change
        /// to a millimetre, not by testing the polyline's vertices — a 5 m
        /// driveway between two 2 m street stations would otherwise land its
        /// dropped kerb up to a metre off the concrete.
        /// </summary>
        /// <param name="taperStart">Drop the kerb to flush at the first point
        /// too — a street that simply ends.</param>
        public static List<List<EdgeStation>> KerbRuns(IList<Vector3> edge, IList<Vector3> outward,
                                                       System.Func<Vector3, bool> inGap, float height,
                                                       bool taperStart, bool taperEnd, float maxPitch)
        {
            int n = edge.Count;
            var runs = new List<List<EdgeStation>>();
            if (n < 2) return runs;
            var arc = new float[n];
            for (int i = 1; i < n; i++)
                arc[i] = arc[i - 1] + HorizontalDistance(edge[i - 1], edge[i]);
            float total = arc[n - 1];

            void At(float a, out Vector3 p, out Vector3 o)
            {
                int k = 1;
                while (k < n - 1 && arc[k] < a) k++;
                float span = arc[k] - arc[k - 1];
                float t = span > 1e-6f ? Mathf.Clamp01((a - arc[k - 1]) / span) : 0f;
                p = Vector3.Lerp(edge[k - 1], edge[k], t);
                o = Vector3.Lerp(outward[k - 1], outward[k], t);
                o.y = 0f;
                o = o.sqrMagnitude > 1e-8f ? o.normalized : outward[k].normalized;
            }
            bool Gap(float a) { At(a, out var p, out _); return inGap(p); }

            // ---- the entrances, as arc intervals ----
            const float Walk = 0.05f;
            var gaps = new List<Vector2>();
            bool open = Gap(0f);
            float from = 0f;
            for (float a = Walk; ; a += Walk)
            {
                float here = Mathf.Min(a, total);
                bool g = Gap(here);
                if (g != open)
                {
                    float lo = here - Walk, hi = here;
                    for (int it = 0; it < 14; it++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (Gap(mid) == open) lo = mid; else hi = mid;
                    }
                    float edgeAt = (lo + hi) * 0.5f;
                    if (open) gaps.Add(new Vector2(from, edgeAt));
                    else from = edgeAt;
                    open = g;
                }
                if (here >= total) break;
            }
            if (open) gaps.Add(new Vector2(from, total));

            bool InGapArc(float a)
            {
                foreach (var g in gaps) if (a > g.x && a < g.y) return true;
                return false;
            }
            float Lift(float a)
            {
                float d = float.MaxValue;
                foreach (var g in gaps)
                {
                    if (g.x > 0f) d = Mathf.Min(d, Mathf.Abs(a - g.x));
                    if (g.y < total) d = Mathf.Min(d, Mathf.Abs(a - g.y));
                }
                if (taperStart) d = Mathf.Min(d, a);
                if (taperEnd) d = Mathf.Min(d, total - a);
                return height * Mathf.Clamp01(d / KerbTaperM);
            }

            // ---- every place the section changes ----
            var breaks = new List<float> { 0f, total };
            breaks.AddRange(arc);
            foreach (var g in gaps)
            {
                breaks.Add(g.x); breaks.Add(g.y);
                breaks.Add(g.x - KerbTaperM); breaks.Add(g.y + KerbTaperM);
            }
            if (taperStart) breaks.Add(KerbTaperM);
            if (taperEnd) breaks.Add(total - KerbTaperM);
            breaks.RemoveAll(b => b < 0f || b > total);
            breaks.Sort();
            var stations = new List<float>();
            foreach (float b in breaks)
            {
                if (stations.Count > 0 && b - stations[stations.Count - 1] < 1e-3f) continue;
                if (stations.Count > 0)
                {
                    float prev = stations[stations.Count - 1];
                    int split = Mathf.CeilToInt((b - prev) / Mathf.Max(0.1f, maxPitch));
                    for (int s = 1; s < split; s++) stations.Add(prev + (b - prev) * s / split);
                }
                stations.Add(b);
            }

            // ---- runs: the stretches between entrances ----
            List<EdgeStation> run = null;
            for (int i = 0; i + 1 < stations.Count; i++)
            {
                float a0 = stations[i], a1 = stations[i + 1];
                if (InGapArc((a0 + a1) * 0.5f)) { run = null; continue; }
                if (run == null)
                {
                    run = new List<EdgeStation>();
                    runs.Add(run);
                    At(a0, out var p0, out var o0);
                    run.Add(new EdgeStation { foot = p0, outward = o0, lift = Lift(a0) });
                }
                At(a1, out var p1, out var o1);
                run.Add(new EdgeStation { foot = p1, outward = o1, lift = Lift(a1) });
            }
            return runs;
        }

        static float HorizontalDistance(Vector3 a, Vector3 b) =>
            Mathf.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));

        /// <summary>
        /// A RAISED KERB THAT A CAR CAN MOUNT: the stone you see, and a ramp
        /// under it that the car actually meets.
        ///
        /// The town's two kerbs were 280 m BoxColliders — WorldKit.Box is solid
        /// unless told otherwise, and nobody told it — standing 14 cm over the
        /// road and 22 cm over the lawn, unbroken past all six lot entrances.
        /// The car's body box leads every wheel ray, so it met the vertical
        /// face first: a snag for a stock FD from the grass, a wall for a
        /// lowered one, and a lowered car could not leave the street at all.
        ///
        /// So the two halves of a kerb are two meshes, the way the circuits'
        /// street curb already works:
        ///   * the STONE, render-only: a battered face, a flat top, and a back
        ///     face hanging below the verge behind it;
        ///   * the RAMP, a child MeshCollider on <paramref name="colliderLayer"/>:
        ///     from the foot straight to the back of the top, a 31% climb
        ///     (<see cref="KerbWidthM"/>). Over its first 30 cm a wheel sits a
        ///     few centimetres into the drawn stone; at 240 lines from the chase
        ///     camera that is the same compromise as the render-only skirts.
        /// Where the lift is zero the stone is a flat strip at the road's
        /// height and the ramp a flat collider — which is exactly what a dropped
        /// kerb is.
        /// </summary>
        public static GameObject Kerb(Transform parent, string name, List<EdgeStation> run,
                                      Material mat, int colliderLayer)
        {
            if (run == null || run.Count < 2) return null;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            var cv = new List<Vector3>();
            var ctri = new List<int>();
            float dist = 0f;
            for (int i = 0; i < run.Count; i++)
            {
                var st = run[i];
                if (i > 0) dist += HorizontalDistance(run[i - 1].foot, st.foot);
                Vector3 up = Vector3.up;
                Vector3 a = st.foot;
                Vector3 b = st.foot + st.outward * KerbFaceBatterM + up * st.lift;
                Vector3 c = st.BackTop;
                Vector3 d = c - up * (RoadsideRules.EdgeDropM + KerbBackHangM);
                float u = dist / 2f;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                uv.Add(new Vector2(u, 0f)); uv.Add(new Vector2(u, 0.25f));
                uv.Add(new Vector2(u, 0.5f)); uv.Add(new Vector2(u, 1f));
                cv.Add(a); cv.Add(c);
            }
            for (int i = 0; i + 1 < run.Count; i++)
            {
                int s0 = i * 4, s1 = (i + 1) * 4;
                Vector3 o = run[i].outward;
                // Face toward the road, top up, back toward the verge. The
                // desired directions carry a little UP so a face that has shrunk
                // to a flat strip at a dropped kerb still resolves to facing
                // the sky rather than a coin flip.
                Quad(v, tri, s0 + 0, s1 + 0, s1 + 1, s0 + 1, (-o + Vector3.up * 0.2f));
                Quad(v, tri, s0 + 1, s1 + 1, s1 + 2, s0 + 2, Vector3.up);
                Quad(v, tri, s0 + 2, s1 + 2, s1 + 3, s0 + 3, (o + Vector3.up * 0.2f));
                Quad(cv, ctri, i * 2, (i + 1) * 2, (i + 1) * 2 + 1, i * 2 + 1, Vector3.up);
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            SaveMesh(mesh, name);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var ramp = new Mesh { name = name + "Ramp" };
            ramp.SetVertices(cv);
            ramp.SetTriangles(ctri, 0);
            ramp.RecalculateNormals();
            ramp.RecalculateBounds();
            SaveMesh(ramp, name + "Ramp");
            var col = new GameObject(name + "Ramp");
            col.transform.SetParent(go.transform, false);
            col.isStatic = true;
            col.layer = colliderLayer;
            col.AddComponent<MeshCollider>().sharedMesh = ramp;
            return go;
        }

        /// <summary>The ground a surface's lawn edges meet, for
        /// <see cref="Feather"/>: a world (x, z) to the height of the lawn's
        /// COLLIDER there.</summary>
        public delegate float GroundAt(float x, float z);

        /// <summary>
        /// THE LAWN GRADED UP TO A SURFACE'S EDGE.
        ///
        /// "Most roads aren't more than an inch above the shoulder dirt", and
        /// the owner's rule behind it: roads that sit centimetres above the
        /// ground meet it properly, not with a wall. Every slab in the town
        /// and on the street was a flat skin 7-11 cm over a lawn sunk to keep
        /// it from z-fighting the tarmac — a square step along every edge, and
        /// a face the body box of a lowered car met before its wheels did.
        ///
        /// Raising the lawn is not the answer: it was sunk because a big
        /// ground triangle's depth crosses the road's somewhere in the middle
        /// distance and draws a band of grass across the carriageway. So the
        /// lawn stays where it is and a strip of it is added over the gap, per
        /// station along the edge:
        ///   * its inner edge the owner's inch (<see cref="RoadsideRules.EdgeDropM"/>)
        ///     under the surface's edge — the skirt shows that inch;
        ///   * falling at <see cref="FeatherGrade"/> until it reaches the lawn,
        ///     measured against the lawn's COLLIDER (<paramref name="groundAt"/>),
        ///     and closing on it column by column, so a lattice crease under the
        ///     feather is followed rather than cut through;
        ///   * then its toe TUCKED under the lawn (<see cref="RoadsideRules.ToeTuckM"/>
        ///     over <see cref="RoadsideRules.ToeTuckRunM"/>), so the two surfaces
        ///     CROSS instead of abutting. A car rides the higher of two continuous
        ///     surfaces, which is continuous: there is no lip to find.
        /// Grass material with the lawn's own world UVs, so where the two do
        /// fight for depth at the crossing, identical texels fight.
        ///
        /// A MeshCollider, off the road layer, named by the caller so audits
        /// can tell it from the lawn. Stations that repeat an inner point with
        /// a turning outward vector make a corner fan; the degenerate triangles
        /// that leaves are dropped.
        /// </summary>
        /// <param name="edge">The surface's edge, AT the surface's height.</param>
        public static GameObject Feather(Transform parent, string name, IList<Vector3> edge,
                                         IList<Vector3> outward, GroundAt groundAt,
                                         Material mat, float tile, int layer = 0)
        {
            int n = edge.Count;
            if (n < 2) return null;
            int per = FeatherColumns + 2;
            var v = new List<Vector3>(n * per);
            var uv = new List<Vector2>(n * per);
            for (int k = 0; k < n; k++)
            {
                Vector3 o = outward[k]; o.y = 0f; o.Normalize();
                Vector3 p0 = edge[k] - Vector3.up * RoadsideRules.EdgeDropM;
                float above = p0.y - groundAt(p0.x, p0.z);
                float run = Mathf.Clamp(above / FeatherGrade, FeatherMinRunM, FeatherMaxRunM);
                for (int c = 0; c <= FeatherColumns; c++)
                {
                    float u = c / (float)FeatherColumns;
                    Vector3 p = p0 + o * (run * u);
                    // u = 0 is the edge exactly, whatever the lawn does.
                    p.y = c == 0 ? p0.y : groundAt(p.x, p.z) + above * (1f - u);
                    v.Add(p);
                    uv.Add(new Vector2(p.x / tile, p.z / tile));
                }
                Vector3 toe = p0 + o * (run + RoadsideRules.ToeTuckRunM);
                toe.y = groundAt(toe.x, toe.z) - RoadsideRules.ToeTuckM;
                v.Add(toe);
                uv.Add(new Vector2(toe.x / tile, toe.z / tile));
            }
            var tri = new List<int>();
            for (int k = 0; k + 1 < n; k++)
                for (int c = 0; c + 1 < per; c++)
                    Quad(v, tri, k * per + c, (k + 1) * per + c, (k + 1) * per + c + 1,
                         k * per + c + 1, Vector3.up);
            if (tri.Count == 0) return null;

            var mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssertFacesUp(mesh, name);
            SaveMesh(mesh, name);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.isStatic = true;
            go.layer = layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        /// <summary>The verge behind a kerb run: a <see cref="Feather"/> from
        /// the back of the stone's top, station for station with the stone.</summary>
        public static GameObject KerbVerge(Transform parent, string name, List<EdgeStation> run,
                                           GroundAt groundAt, Material mat, float tile)
        {
            if (run == null || run.Count < 2) return null;
            var edge = new List<Vector3>(run.Count);
            var outs = new List<Vector3>(run.Count);
            foreach (var st in run) { edge.Add(st.BackTop); outs.Add(st.outward); }
            return Feather(parent, name, edge, outs, groundAt, mat, tile);
        }

        /// <summary>A stretch of a slab's edge that is NOT lawn — a building
        /// standing on it, or another slab carrying on from it. In the world
        /// coordinate that edge runs along: x for a Z edge, z for an X edge.</summary>
        public readonly struct SlabCut
        {
            public readonly SlabEdge edge;
            public readonly float from, to;
            public SlabCut(SlabEdge edge, float from, float to)
            {
                this.edge = edge;
                this.from = Mathf.Min(from, to);
                this.to = Mathf.Max(from, to);
            }
        }

        /// <summary>
        /// Feathers round the lawn edges of a FLAT, axis-aligned slab: the
        /// town's aprons, pads and lot entrances.
        ///
        /// The edges in <paramref name="edges"/>, less every <see cref="SlabCut"/>,
        /// walked round the rectangle in one direction. Where two feathered
        /// edges meet at a corner the strip carries on round it as a fan, so a
        /// car cutting the corner diagonally meets graded lawn and not the
        /// 7 cm point of the slab that the two straight feathers would have
        /// left standing in the gap between them.
        /// </summary>
        public static void FeatherRect(Transform parent, string name,
                                       float minX, float maxX, float minZ, float maxZ, float topY,
                                       SlabEdge edges, GroundAt groundAt, Material mat, float tile,
                                       params SlabCut[] cuts)
        {
            // Round the rectangle: MinZ west to east, MaxX south to north,
            // MaxZ east to west, MinX north to south.
            var flags = new[] { SlabEdge.MinZ, SlabEdge.MaxX, SlabEdge.MaxZ, SlabEdge.MinX };
            var starts = new[] { new Vector3(minX, topY, minZ), new Vector3(maxX, topY, minZ),
                                 new Vector3(maxX, topY, maxZ), new Vector3(minX, topY, maxZ) };
            var outs = new[] { Vector3.back, Vector3.right, Vector3.forward, Vector3.left };
            float[] lens = { maxX - minX, maxZ - minZ, maxX - minX, maxZ - minZ };

            // Each side's feathered intervals, as distances from its start corner.
            var spans = new List<(int side, float t0, float t1)>();
            for (int s = 0; s < 4; s++)
            {
                if ((edges & flags[s]) == 0) continue;
                var parts = new List<Vector2> { new Vector2(0f, lens[s]) };
                foreach (var cut in cuts)
                {
                    if (cut.edge != flags[s]) continue;
                    // World coordinate along the side -> distance from its start.
                    float a, b;
                    switch (s)
                    {
                        case 0: a = cut.from - minX; b = cut.to - minX; break;
                        case 1: a = cut.from - minZ; b = cut.to - minZ; break;
                        case 2: a = maxX - cut.to; b = maxX - cut.from; break;
                        default: a = maxZ - cut.to; b = maxZ - cut.from; break;
                    }
                    var next = new List<Vector2>();
                    foreach (var p in parts)
                    {
                        if (b <= p.x || a >= p.y) { next.Add(p); continue; }
                        if (a > p.x) next.Add(new Vector2(p.x, a));
                        if (b < p.y) next.Add(new Vector2(b, p.y));
                    }
                    parts = next;
                }
                foreach (var p in parts)
                    if (p.y - p.x > 0.05f) spans.Add((s, p.x, p.y));
            }
            if (spans.Count == 0) return;

            bool Joins(int i)          // does span i carry on round a corner into span i+1?
            {
                var a = spans[i];
                var b = spans[(i + 1) % spans.Count];
                return b.side == (a.side + 1) % 4 && a.t1 >= lens[a.side] - 1e-3f && b.t0 <= 1e-3f;
            }
            // Start the walk at a span nothing runs into, unless the ring is closed.
            int first = 0;
            bool closed = true;
            for (int i = 0; i < spans.Count; i++)
                if (!Joins((i - 1 + spans.Count) % spans.Count)) { first = i; closed = false; break; }

            var edge = new List<Vector3>();
            var outward = new List<Vector3>();
            int chain = 0;
            void Flush()
            {
                if (edge.Count >= 2)
                    Feather(parent, name + chain++, edge, outward, groundAt, mat, tile);
                edge.Clear();
                outward.Clear();
            }
            for (int k = 0; k < spans.Count; k++)
            {
                int i = (first + k) % spans.Count;
                var sp = spans[i];
                Vector3 dir = (starts[(sp.side + 1) % 4] - starts[sp.side]).normalized;
                int steps = Mathf.Max(1, Mathf.CeilToInt((sp.t1 - sp.t0) / FeatherStationPitchM));
                for (int st = 0; st <= steps; st++)
                {
                    edge.Add(starts[sp.side] + dir * Mathf.Lerp(sp.t0, sp.t1, st / (float)steps));
                    outward.Add(outs[sp.side]);
                }
                bool last = k == spans.Count - 1;
                if (Joins(i) && (!last || closed))
                {
                    // The fan: the corner point again, the outward vector turned
                    // a quarter of the way at a time toward the next side's.
                    Vector3 corner = edge[edge.Count - 1];
                    Vector3 nextOut = outs[(sp.side + 1) % 4];
                    for (int f = 1; f < 4; f++)
                    {
                        edge.Add(corner);
                        outward.Add(Vector3.Slerp(outs[sp.side], nextOut, f / 4f).normalized);
                    }
                    if (last) { edge.Add(edge[0]); outward.Add(outward[0]); }
                }
                else Flush();
            }
            Flush();
        }

        /// <summary>
        /// Two triangles over a quad, each wound to face <paramref name="want"/>
        /// and dropped if it has no area — the fan at a corner and a kerb face
        /// at a dropped kerb both produce triangles with two vertices in the
        /// same place, and a MeshCollider has no use for them.
        /// </summary>
        static void Quad(List<Vector3> v, List<int> tri, int a, int b, int c, int d, Vector3 want)
        {
            Tri(v, tri, a, b, c, want);
            Tri(v, tri, a, c, d, want);
        }

        static void Tri(List<Vector3> v, List<int> tri, int a, int b, int c, Vector3 want)
        {
            Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (n.sqrMagnitude < 1e-10f) return;
            if (Vector3.Dot(n, want) < 0f) { tri.Add(a); tri.Add(c); tri.Add(b); }
            else { tri.Add(a); tri.Add(b); tri.Add(c); }
        }

        /// <summary>A box. Sizes are FULL extents, the way a person measures a
        /// wall, not Unity's half-extents.</summary>
        public static GameObject Box(Transform parent, string name, Vector3 centre,
                                     Vector3 size, Material mat, bool solid = true,
                                     float yaw = 0f, int layer = 0)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = size;
            go.layer = layer;
            var col = go.GetComponent<Collider>();
            if (!solid) Object.DestroyImmediate(col);
            var mr = go.GetComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        /// <summary>
        /// An upright textured panel — a fence run, a hoarding, a sign board.
        /// Two triangles with UVs scaled to the panel's own size, so one
        /// material tiles correctly across a 4 m gate and a 40 m fence.
        ///
        /// Double-sided, because a fence you can see through from one side is
        /// a fence somebody will walk round the back of.
        /// </summary>
        public static GameObject Panel(Transform parent, string name, Vector3 centre,
                                       float width, float height, float yaw,
                                       Material mat, bool solid,
                                       float uTile = 4f, float vTile = 2.5f,
                                       int layer = 0)
        {
            string key = name + "_" + Mathf.RoundToInt(width * 10f) + "x" +
                         Mathf.RoundToInt(height * 10f);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(GenDir + "/" + key + ".asset");
            if (mesh == null)
            {
                float hw = width * 0.5f, hh = height * 0.5f;
                float u = width / Mathf.Max(0.01f, uTile), v = height / Mathf.Max(0.01f, vTile);
                mesh = new Mesh { name = key };
                // EIGHT vertices, not four with two windings. Sharing them
                // would be cheaper and would break the lighting: the back face
                // wants the opposite normal, RecalculateNormals AVERAGES the
                // normals of every triangle touching a vertex, and front plus
                // back averages to zero. PSX/Lit guards a zero-length normal
                // and falls back to straight up, so the fence would be lit as
                // though it were lying flat on the ground.
                mesh.vertices = new[]
                {
                    new Vector3(-hw, -hh, 0f), new Vector3(-hw, hh, 0f),
                    new Vector3(hw, hh, 0f),   new Vector3(hw, -hh, 0f),
                    new Vector3(-hw, -hh, 0f), new Vector3(-hw, hh, 0f),
                    new Vector3(hw, hh, 0f),   new Vector3(hw, -hh, 0f),
                };
                mesh.uv = new[]
                {
                    new Vector2(0f, 0f), new Vector2(0f, v),
                    new Vector2(u, v),   new Vector2(u, 0f),
                    new Vector2(u, 0f),  new Vector2(u, v),
                    new Vector2(0f, v),  new Vector2(0f, 0f),
                };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6 };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                SaveMesh(mesh, key);
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.layer = layer;
            go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (solid)
            {
                var col = go.AddComponent<BoxCollider>();
                col.size = new Vector3(width, height, 0.25f);
            }
            return go;
        }

        /// <summary>A post, pole or bollard. Unity's cylinder is one unit
        /// across and TWO tall, which is the trap in every call site that ever
        /// gets this wrong.</summary>
        public static GameObject Post(Transform parent, string name, Vector3 baseAt,
                                      float diameter, float height, Material mat,
                                      bool solid = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = baseAt + new Vector3(0f, height * 0.5f, 0f);
            go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
            var col = go.GetComponent<Collider>();
            if (!solid) Object.DestroyImmediate(col); else go.layer = SolidLayer;
            var mr = go.GetComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        /// <summary>
        /// Stand a pack model up: instantiate, PSX-dress it, scale it and face
        /// it. Returns the instance.
        /// </summary>
        /// <param name="frontToward">Which way the model's FRONT should end up
        /// pointing.</param>
        /// <param name="yawOffsetDeg">The model's own front-facing correction:
        /// 180 for a model whose front is Unity local -Z, 0 for one whose front
        /// is local +Z. LookRotation aims local +Z at <paramref
        /// name="frontToward"/>, so the 180 is a correction, not a convention.
        ///
        /// IT IS PER-PACK, AND THE DEFAULT OF 180 IS ONLY RIGHT FOR SOME OF
        /// THEM. This comment used to claim 180 was right for "every EXTRACTED
        /// prop in this project" and excuse house_hero as a special case for
        /// being a showcase scene rather than a cut-out. That reasoning is
        /// wrong, and it propagated: the HOUSE pack's fronts are at local +Z,
        /// measured in Blender off the shipped FBXs — house_simple's garage
        /// door, front door and concrete path are all at +Z and only its
        /// veranda is at -Z, and house_hero carries the same door at the same
        /// coordinates. Both want 0. The neighbourhood took the default and
        /// built a whole street showing the road its back gardens.
        ///
        /// The 180 IS right for the restaurant pack, where it was originally
        /// derived from an observation ("every restaurant politely showed the
        /// street its back"). One pack's measurement was generalised to four
        /// that were never checked to agree. MEASURE THE MODEL before trusting
        /// the default — a house facing the wrong way still renders, still
        /// collides and still measures; it just faces the wrong way.</param>
        public static GameObject Place(Transform parent, string fbxPath, string name,
                                       Vector3 at, Vector3 frontToward, float scale,
                                       bool glass = false, float yawOffsetDeg = 180f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (prefab == null)
            {
                Debug.LogWarning("[WorldKit] model missing: " + fbxPath);
                return null;
            }
            var go = (GameObject)Object.Instantiate(prefab);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var flat = new Vector3(frontToward.x, 0f, frontToward.z);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            go.transform.rotation = Quaternion.Euler(0f, yawOffsetDeg, 0f) *
                                    Quaternion.LookRotation(flat.normalized, Vector3.up);
            go.transform.localScale = Vector3.one * scale;
            PSXRacingBuilder.ConvertToPSXMaterials(go, glass);
            return go;
        }

        /// <summary>
        /// Stand a pack model up AT A DECLARED HEIGHT, whatever units it
        /// happens to have been exported in.
        ///
        /// The packs in this project do not agree with each other and there is
        /// no importer setting that tells them apart: `pizzeria.fbx` arrives at
        /// a real 11.5 m and `city_building_05.fbx` — extracted from the same
        /// pack, imported with the same globalScale — arrives at 2.2. The town
        /// stood one of them up beside the other and got a 2.6 m car
        /// dealership, which renders, collides and measures perfectly.
        ///
        /// HEIGHT rather than footprint, because height is the one dimension
        /// that does not depend on which way round the model ended up, and
        /// because <see cref="City.CityProps"/> already carries a measured
        /// height for every one of these — so this reads its authority off the
        /// same table the streamed city does.
        /// </summary>
        public static GameObject PlaceTall(Transform parent, string fbxPath, string name,
                                           Vector3 at, Vector3 frontToward, float metresTall,
                                           bool glass = false, float yawOffsetDeg = 180f)
        {
            var go = Place(parent, fbxPath, name, at, frontToward, 1f, glass, yawOffsetDeg);
            if (go == null) return null;
            float h = BoundsOf(go).size.y;
            if (h > 0.01f && metresTall > 0.01f)
            {
                float k = Mathf.Clamp(metresTall / h, 0.02f, 50f);
                go.transform.localScale = Vector3.one * k;
                if (k < 0.85f || k > 1.15f)
                    Debug.Log("[WorldKit] " + name + " came in " + h.ToString("0.0") +
                              " m tall, scaled x" + k.ToString("0.00") + " to " +
                              metresTall.ToString("0.0") + " m");
            }
            return go;
        }

        /// <summary>
        /// Colliders on everything worth bumping into and nothing smaller.
        ///
        /// The same rule PizzeriaSceneBuilder uses, and for the same reason: a
        /// mesh collider on a 6 cm bottle is a thing for a character controller
        /// to catch on, and these packs have fifteen of them on one shelf.
        /// </summary>
        public static int AddColliders(GameObject root, int layer = 0)
        {
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                // Already collided by somebody with a better idea. HingeDoors
                // gives a swinging leaf a BOX on purpose — a moving non-convex
                // MeshCollider is the one shape PhysX charges for — and a
                // second collider on top of it would put the shut door's mesh
                // back in the doorway for ever.
                if (r.GetComponent<Collider>() != null) continue;
                var s = r.bounds.size;
                if (!((s.x > 0.35f && s.z > 0.35f) || s.y > 0.6f)) continue;
                var mc = r.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                r.gameObject.layer = layer;
                n++;
            }
            return n;
        }

        /// <summary>
        /// Hang the pack's door leaves on hinges, so they swing open as the
        /// player walks up and shut again behind them.
        ///
        /// This USED to be OpenDoors, which disabled every mesh named Door /
        /// Door.NNN / Door_NN. That fixed "I am unable to go inside Pizzeria"
        /// — a leaf with a MeshCollider is a shop you can see into and never
        /// enter — and it introduced the next report word for word: "the doors
        /// are missing to Pizzeria and Convenience store. They should swing
        /// open as player moves through." A doorway with no leaf in it is a
        /// hole in a wall.
        ///
        /// Nothing about the geometry is authored, because the two packs with
        /// real doors disagree about which way round they are modelled and
        /// neither is going to be re-exported. Everything is MEASURED:
        ///
        ///   * Leaves are GROUPED by plan position, because a shop front is a
        ///     double door and the two halves hinge on opposite jambs. Two
        ///     leaves within 3 m of each other are one doorway.
        ///   * The doorway's WIDTH AXIS is whichever horizontal extent of the
        ///     group is longer. A double door is metres wide and centimetres
        ///     thick, so this is never a close call even on the forecourt,
        ///     whose whole model is yawed three degrees off the world axes.
        ///   * Each leaf HINGES on its outer jamb — the end furthest from the
        ///     group centre — which is what makes a pair open outward from the
        ///     middle instead of both swinging the same way.
        ///
        /// The leaf keeps a collider, and gets a BOX rather than the mesh: it
        /// moves now, and a moving non-convex MeshCollider is the one shape
        /// PhysX charges real money for. <see cref="AddColliders"/> skips
        /// anything already collided, so the order of the two calls no longer
        /// matters.
        /// </summary>
        /// <param name="tallEnough">Ignore anything shorter than this. The
        /// packs name cupboard fronts and oven doors "Door" too, and a
        /// self-opening fridge in the back of the kitchen is not the feature
        /// anybody asked for.</param>
        public static int HingeDoors(GameObject root, float tallEnough = 1.6f)
        {
            var leaves = new System.Collections.Generic.List<Transform>();
            int named = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == root.transform) continue;
                string name = t.name;
                if (name != "Door" && !name.StartsWith("Door.") && !name.StartsWith("Door_"))
                    continue;
                var r = t.GetComponentInChildren<MeshRenderer>(true);
                if (r == null) continue;
                named++;
                if (r.bounds.size.y < tallEnough) continue;
                // And WIDE enough. The forecourt pack calls its door FRAME
                // uprights "Door" too — 8 cm square and two metres tall — and
                // a frame post on a hinge is a post that swings out of its own
                // frame when you walk past it.
                if (Mathf.Max(r.bounds.size.x, r.bounds.size.z) < 0.35f) continue;
                leaves.Add(t);
            }
            // Said out loud, because every way this goes wrong is silent. A
            // model whose leaves are all named something else hinges nothing
            // and looks exactly like a shop with its doors open; one whose
            // leaves are all under the height gate is a shop you cannot walk
            // into and looks exactly like a shop with its doors shut.
            Debug.Log("[WorldKit] " + root.name + ": " + named + " door mesh(es), " +
                      leaves.Count + " tall enough to hinge");
            if (leaves.Count == 0) return 0;

            // Group by plan position AND by which way the leaf runs: one
            // doorway per group.
            //
            // The orientation half is not fussiness. The forecourt has a corner
            // entrance — two doors on PERPENDICULAR walls, 2.5 m apart — and
            // grouping on distance alone put them in one group, whose combined
            // box then picked a width axis that was right for one leaf and
            // ninety degrees wrong for the other. A leaf hinged across its own
            // short side pivots about a point 8 cm from its middle: it does not
            // open, it spins in place and sweeps the doorway it is meant to
            // clear. Caught by the build log printing a 0.08 m leaf.
            var groups = new System.Collections.Generic.List<
                             System.Collections.Generic.List<Transform>>();
            var groupAxisIsX = new System.Collections.Generic.List<bool>();
            foreach (var leaf in leaves)
            {
                var lb0 = LeafBounds(leaf);
                bool axisIsX = lb0.size.x >= lb0.size.z;
                Vector3 c = lb0.center;
                int into = -1;
                for (int gi = 0; gi < groups.Count; gi++)
                {
                    if (groupAxisIsX[gi] != axisIsX) continue;
                    Vector3 gc = LeafBounds(groups[gi][0]).center;
                    if (Mathf.Abs(gc.x - c.x) < 3f && Mathf.Abs(gc.z - c.z) < 3f)
                    { into = gi; break; }
                }
                if (into < 0)
                {
                    groups.Add(new System.Collections.Generic.List<Transform>());
                    groupAxisIsX.Add(axisIsX);
                    into = groups.Count - 1;
                }
                groups[into].Add(leaf);
            }

            int n = 0;
            for (int gi = 0; gi < groups.Count; gi++)
            {
                var g = groups[gi];
                var gb = LeafBounds(g[0]);
                foreach (var leaf in g) gb.Encapsulate(LeafBounds(leaf));
                // Which way the doorway runs, and which way you go through it.
                bool widthIsX = groupAxisIsX[gi];
                Vector3 width = widthIsX ? Vector3.right : Vector3.forward;
                Vector3 through = widthIsX ? Vector3.forward : Vector3.right;

                foreach (var leaf in g)
                {
                    var lb = LeafBounds(leaf);
                    float minAlong = Vector3.Dot(lb.min, width);
                    float maxAlong = Vector3.Dot(lb.max, width);
                    bool hingeAtMax = HingeAtMax(root, leaf, g, gb, lb, width,
                                                 minAlong, maxAlong);
                    float hingeAlong = hingeAtMax ? maxAlong : minAlong;
                    float freeAlong = hingeAtMax ? minAlong : maxAlong;

                    Vector3 hinge = lb.center;
                    hinge += width * (hingeAlong - Vector3.Dot(lb.center, width));
                    hinge.y = lb.min.y;

                    var pivot = new GameObject(leaf.name + "_Hinge");
                    pivot.transform.SetParent(leaf.parent, true);
                    pivot.transform.SetPositionAndRotation(hinge, Quaternion.identity);
                    leaf.SetParent(pivot.transform, true);

                    // IN THE BUILDING'S FRAME, not the world's. These get baked
                    // into prefabs that CityProps then stands up at whatever
                    // yaw the street runs at, and a world vector would describe
                    // the door of the building as it sat on the bake
                    // turntable. The pivot's own frame is no good either — it
                    // is the thing that turns.
                    var door = pivot.AddComponent<SwingDoor>();
                    var frame = pivot.transform.parent;
                    Vector3 leafDir = width * (freeAlong - hingeAlong);
                    door.hingeToFree = frame != null
                        ? frame.InverseTransformDirection(leafDir) : leafDir;
                    door.throughNormal = frame != null
                        ? frame.InverseTransformDirection(through) : through;
                    // A leaf that came out shorter than a doorknob is one whose
                    // hinge landed in the middle of it, which is a door that
                    // spins rather than opens. Nothing else in this pass can
                    // see that, so it says so.
                    if (Mathf.Abs(freeAlong - hingeAlong) < 0.3f)
                        Debug.LogWarning("[WorldKit] " + root.name + "/" + leaf.name +
                            " hinged on a " + Mathf.Abs(freeAlong - hingeAlong).ToString("0.00") +
                            " m edge — that leaf is grouped across its own short side");

                    // A box on the leaf's own local bounds. Local, because the
                    // pivot turns it: a world AABB baked at bake time would be
                    // the shape of a shut door for as long as the door was
                    // open.
                    foreach (var stale in leaf.GetComponents<Collider>())
                        Object.DestroyImmediate(stale);
                    var mf = leaf.GetComponentInChildren<MeshFilter>(true);
                    if (mf != null && mf.sharedMesh != null)
                    {
                        var target = mf.gameObject;
                        foreach (var stale in target.GetComponents<Collider>())
                            Object.DestroyImmediate(stale);
                        var mb = mf.sharedMesh.bounds;
                        var bc = target.AddComponent<BoxCollider>();
                        bc.center = mb.center;
                        bc.size = mb.size;
                        target.layer = SolidLayer;
                    }
                    leaf.gameObject.layer = SolidLayer;
                    n++;
                }
            }
            return n;
        }

        /// <summary>
        /// WHICH EDGE THE LEAF HANGS ON, in three tries.
        ///
        /// Reported as "the gas station door swings open from the wrong side —
        /// the door handle is hinged to the building." Exactly the failure this
        /// exists to stop: a leaf swinging about its handle sweeps the doorway
        /// it is meant to clear and buries its own free edge in the wall.
        ///
        /// The old rule was one line — hinge on whichever end is further from
        /// the middle of the opening — which is right for a matched pair and
        /// meaningless for a single leaf, whose two ends are equidistant from
        /// its own centre. So a single door hinged on whichever end the
        /// comparison happened to favour, which is a coin flip.
        ///
        /// In order of how much it knows:
        ///
        ///   1. THE ARTIST'S OWN PIVOT. A door modelled to open has its
        ///      transform origin at the hinge — that is what the origin is FOR.
        ///      If the leaf's own position sits near one end of its span and
        ///      nowhere near the other, that is the answer and no probe beats
        ///      it.
        ///   2. WHICH END IS AGAINST SOMETHING. A hinge is bolted to a jamb, so
        ///      the end with the building on it hangs and the end with air in
        ///      front of it swings. Probed against the model's own renderer
        ///      bounds — no colliders exist yet at this point in the bake, and
        ///      adding them to ask would change what the piece pass then sees.
        ///   3. THE PAIR RULE. Two leaves in one opening hinge outward, on the
        ///      jambs, meeting in the middle. Still right, still last, because
        ///      it is the only one that says nothing about a single door.
        /// </summary>
        static bool HingeAtMax(GameObject root, Transform leaf,
                               System.Collections.Generic.List<Transform> group,
                               Bounds groupBounds, Bounds lb, Vector3 width,
                               float minAlong, float maxAlong)
        {
            float span = maxAlong - minAlong;

            // ---- 1. the authored pivot ----
            if (span > 0.2f)
            {
                float pivotAlong = Vector3.Dot(leaf.position, width);
                float fromMin = Mathf.Abs(pivotAlong - minAlong);
                float fromMax = Mathf.Abs(pivotAlong - maxAlong);
                // Near one end and clearly not the other: a quarter of the span
                // against three quarters. A pivot in the middle is a mesh
                // origin, not a hinge, and says nothing.
                if (fromMin < span * 0.25f && fromMax > span * 0.6f) return false;
                if (fromMax < span * 0.25f && fromMin > span * 0.6f) return true;
            }

            // ---- 2. which end is against the building ----
            bool minJamb = EndTouchesBuilding(root, group, lb, width, minAlong, -1f);
            bool maxJamb = EndTouchesBuilding(root, group, lb, width, maxAlong, 1f);
            if (minJamb != maxJamb) return maxJamb;

            // ---- 3. the pair rule ----
            float centreAlong = Vector3.Dot(groupBounds.center, width);
            return Mathf.Abs(maxAlong - centreAlong) >= Mathf.Abs(minAlong - centreAlong);
        }

        /// <summary>
        /// Is there model on the far side of one end of this leaf?
        ///
        /// Probes a point a hand's width beyond the end, at the leaf's own
        /// mid-height and thickness, against every renderer in the model except
        /// the doors themselves. Bounds rather than a raycast: HingeDoors runs
        /// BEFORE the collider pass on every path that calls it, so at this
        /// moment the model has no colliders to cast at — and adding some to
        /// ask would change what that pass then finds.
        /// </summary>
        static bool EndTouchesBuilding(GameObject root,
                                       System.Collections.Generic.List<Transform> group,
                                       Bounds lb, Vector3 width, float endAlong, float outward)
        {
            Vector3 probe = lb.center;
            probe += width * (endAlong + outward * 0.18f - Vector3.Dot(lb.center, width));
            probe.y = lb.min.y + lb.size.y * 0.5f;

            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null) continue;
                bool isDoor = false;
                foreach (var d in group)
                    if (r.transform == d || r.transform.IsChildOf(d)) { isDoor = true; break; }
                if (isDoor) continue;
                // A hair of slack: the jamb and the leaf are modelled touching,
                // and a probe exactly on a shared face is a coin flip about
                // floating point.
                var b = r.bounds;
                b.Expand(0.06f);
                if (b.Contains(probe)) return true;
            }
            return false;
        }

        /// <summary>
        /// Where the way IN is: the combined world bounds of the widest door
        /// group on a model, and the direction that leads out of it.
        ///
        /// Call it BEFORE <see cref="HingeDoors"/> — a hinged leaf has already
        /// been reparented and can be standing open in the baked scene, and a
        /// doorway measured off an open door is a doorway ninety degrees round
        /// the corner from itself.
        ///
        /// It exists because a walk-up prompt hung on the model's BOUNDING BOX
        /// is a prompt on the middle of a wall. The town's pizzeria is 21 m of
        /// frontage and its door is nowhere near the centre of it, so the hook
        /// that was meant to say "clock on" stood eight metres to one side of
        /// the only door in the building — which is most of "I drove to work
        /// but was unable to find a pizza inside".
        /// </summary>
        public static bool DoorwayOf(GameObject root, out Bounds doorway, out Vector3 outward,
                                     float tallEnough = 1.6f)
        {
            doorway = new Bounds();
            outward = Vector3.forward;
            bool any = false;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string name = t.name;
                if (name != "Door" && !name.StartsWith("Door.") && !name.StartsWith("Door_"))
                    continue;
                var r = t.GetComponentInChildren<MeshRenderer>(true);
                if (r == null || r.bounds.size.y < tallEnough) continue;
                if (!any) { doorway = r.bounds; any = true; }
                else doorway.Encapsulate(r.bounds);
            }
            if (!any) return false;

            // Out of the building is away from its middle, along whichever
            // horizontal axis the door is furthest off centre. Measured rather
            // than assumed: these packs face four different ways and the
            // forecourt's is yawed three degrees on top of that.
            var shell = BoundsOf(root);
            Vector3 off = doorway.center - shell.center;
            outward = Mathf.Abs(off.x) >= Mathf.Abs(off.z)
                ? new Vector3(Mathf.Sign(off.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(off.z));
            return true;
        }

        /// <summary>World bounds of a leaf's renderers, or a point at its own
        /// position when it somehow has none.</summary>
        static Bounds LeafBounds(Transform leaf)
        {
            var rs = leaf.GetComponentsInChildren<MeshRenderer>(true);
            if (rs.Length == 0) return new Bounds(leaf.position, Vector3.zero);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        /// <summary>World-space bounds of every renderer under a root, or an
        /// empty box centred on it when there are none.</summary>
        public static Bounds BoundsOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        /// <summary>Drop a model so its LOWEST point sits at
        /// <paramref name="groundY"/>. These packs pivot anywhere — half a
        /// metre above their own base in the skyscraper set, on a foundation
        /// slab in the house set — so seating by the origin buries some and
        /// floats others.</summary>
        public static void SeatOnGround(GameObject go, float groundY)
        {
            if (go == null) return;
            var b = BoundsOf(go);
            go.transform.position += new Vector3(0f, groundY - b.min.y, 0f);
        }
    }
}
