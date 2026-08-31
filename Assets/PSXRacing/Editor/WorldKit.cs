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

        /// <summary>A horizontal ground plane, subdivided into ~<paramref
        /// name="cell"/>-metre squares. UVs are WORLD-anchored so two slabs
        /// meeting at a seam do not show one.</summary>
        public static GameObject GridSlab(Transform parent, string name, Vector3 centre,
                                          float sizeX, float sizeZ, float cell,
                                          Material mat, bool solid, float tile,
                                          int layer = 0)
        {
            int nx = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(sizeX) / cell));
            int nz = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(sizeZ) / cell));
            var verts = new Vector3[(nx + 1) * (nz + 1)];
            var uvs = new Vector2[verts.Length];
            var tris = new int[nx * nz * 6];

            for (int z = 0; z <= nz; z++)
                for (int x = 0; x <= nx; x++)
                {
                    float fx = (x / (float)nx - 0.5f) * sizeX;
                    float fz = (z / (float)nz - 0.5f) * sizeZ;
                    int v = z * (nx + 1) + x;
                    verts[v] = new Vector3(fx, 0f, fz);
                    uvs[v] = new Vector2((fx + centre.x) / tile, (fz + centre.z) / tile);
                }
            int t = 0;
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    int v = z * (nx + 1) + x;
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
                var col = go.AddComponent<BoxCollider>();
                col.size = new Vector3(Mathf.Abs(sizeX), 0.4f, Mathf.Abs(sizeZ));
                col.center = new Vector3(0f, -0.2f, 0f);
            }
            return go;
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
        /// <param name="yawOffsetDeg">The model's own front-facing correction.
        /// 180 for every EXTRACTED prop in this project — the pack models its
        /// fronts toward Blender -Y, which lands at Unity -Z — and that is what
        /// CityProps carries on every row, so it is the default.
        ///
        /// It is NOT universal, and getting it wrong is silent: house_hero.fbx
        /// is the whole showcase scene rather than a cut-out prop and its front
        /// arrives facing +Z, which is why GarageSceneBuilder turns it by a
        /// flat 180 and this needs to be told 0 for it. A house facing the
        /// wrong way still renders, still collides, and still measures — it
        /// just shows the street its back garden.</param>
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
                    float centreAlong = Vector3.Dot(gb.center, width);
                    float minAlong = Vector3.Dot(lb.min, width);
                    float maxAlong = Vector3.Dot(lb.max, width);
                    // The jamb: whichever end of this leaf is further from the
                    // middle of the opening. A single leaf falls out of this
                    // with its two ends equidistant, and either is a real door.
                    bool hingeAtMax = Mathf.Abs(maxAlong - centreAlong) >=
                                      Mathf.Abs(minAlong - centreAlong);
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
