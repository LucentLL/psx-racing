using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Turns the imported vehicle OBJs into the prefabs the game loads at
    /// runtime, one per model, under Resources/CarModels.
    ///
    /// The point of doing this in the editor rather than hand-authoring prefabs
    /// is that every number a shell needs is MEASURED off the imported mesh:
    /// where the axles are, how wide the track is, how big the tyre is, how big
    /// a box it fits in, and - the one that actually matters - which way round
    /// Unity's OBJ importer decided the car faces. The pack names its wheels
    /// WheelFL/wheel_FR/wheel.RL, so the front axle can simply be looked at.
    ///
    /// Run from Tools > PSX Racing > Bake Car Models, or let PSXRacingBuilder
    /// call it as part of a scene build.
    /// </summary>
    public static class CarModelBaker
    {
        const string Root = "Assets/PSXRacing";
        const string ModelDir = Root + "/Art/Car/Models";
        const string OutDir = Root + "/Resources/CarModels";
        const string MatDir = Root + "/Materials/CarModels";
        const string GenDir = Root + "/Generated/CarModels";

        /// <summary>The project's long-standing tyre fudge: the FD's 0.333 m
        /// wheel is drawn and driven at 0.31 so it sits in the arch instead of
        /// proud of it. Applied to every model so they are all treated the same
        /// and the reference car's numbers do not move.</summary>
        const float TyreScale = 0.93f;

        /// <summary>Collider as a fraction of the body's mesh bounds. These are
        /// the ratios the hand-tuned FD collider already had (1.72/1.88,
        /// 1.00/1.11, 4.10/4.29), so measuring reproduces it to the centimetre
        /// instead of replacing it with a guess.</summary>
        static readonly Vector3 ColliderFit = new Vector3(0.915f, 0.90f, 0.955f);

        /// <summary>Blob shadow relative to the body footprint, from the FD's
        /// hand-set 2.3 x 4.6 against its 1.88 x 4.29 body.</summary>
        static readonly Vector2 BlobFit = new Vector2(1.22f, 1.07f);

        /// <summary>
        /// The built-in FD's own axle midpoint, which is 8.9 cm ahead of its
        /// mesh origin — so its body slides that far BACK to sit on the rig's
        /// wheels. It is a literal because the FD is the one model with no axle
        /// OBJs to read: its wheels ship as a separate mesh and its body has no
        /// wheels in it at all.
        ///
        /// Measured from the arch cut-outs instead, by scanning the underside of
        /// the body at the wheel line and taking the middle of each opening:
        /// front 0.942..1.661, rear -1.493..-0.753, so the axles sit at 1.301
        /// and -1.123. That scan puts the wheelbase at 2.424 against the 2.425
        /// this car has always been driven on, which is the check that says the
        /// arches — not just the number below — are where it claims.
        /// </summary>
        const float BuiltInAxleMidZ = 0.089f;

        static readonly List<string> log = new List<string>();

        [MenuItem("Tools/PSX Racing/Bake Car Models")]
        public static void BakeMenu()
        {
            Bake();
            Debug.Log("[CarModelBaker]\n" + string.Join("\n", log));
        }

        public static IReadOnlyList<string> LastLog => log;

        public static void Bake()
        {
            log.Clear();
            Directory.CreateDirectory(OutDir);
            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory(GenDir);

            var shader = Shader.Find("PSX/Lit");
            if (shader == null) throw new Exception("PSX/Lit shader not found — did shaders compile?");

            foreach (var m in CarModelLibrary.Models)
            {
                try { BakeOne(m, shader); }
                catch (Exception e) { log.Add($"FAIL {m.key}: {e.Message}"); }
            }
            AssetDatabase.SaveAssets();
            CarModelLibrary.ClearCache();
        }

        // ------------------------------------------------------------------

        static void BakeOne(CarModelLibrary.Model model, Shader shader)
        {
            string dir = ModelDir + "/" + model.key;
            string objPath = dir + "/" + model.key + ".obj";
            string texDir = dir + "/textures";

            // The FD came into the project before the pack did and still lives
            // where it always has: body and wheel as two separate OBJs, with no
            // wheels in the body mesh to measure. Its geometry is therefore the
            // literal numbers the game shipped with, so re-baking cannot move
            // the one car every handling decision was made against.
            bool builtIn = model.key == CarModelLibrary.Default;
            if (builtIn)
            {
                objPath = Root + "/Art/Car/2_seater_coupe.obj";
                texDir = Root + "/Art/Car/textures";
            }

            var src = AssetDatabase.LoadAssetAtPath<GameObject>(objPath);
            if (src == null) throw new Exception("model not imported: " + objPath);

            Mesh bodyMesh = src.GetComponentInChildren<MeshFilter>()?.sharedMesh;
            if (bodyMesh == null) throw new Exception("body mesh missing");

            Mesh frontAxle = null, rearAxle = null, wheelSrc = null;
            if (builtIn)
            {
                var wheelObj = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Art/Car/wheel.obj");
                wheelSrc = wheelObj != null ? wheelObj.GetComponentInChildren<MeshFilter>()?.sharedMesh : null;
            }
            else
            {
                frontAxle = AxleMesh(dir + "/" + model.key + "_wheels_f.obj");
                rearAxle = AxleMesh(dir + "/" + model.key + "_wheels_r.obj");
            }

            float bodyYaw = 0f, wheelbase = 2.425f, track = 1.46f, tyre = 0.333f, bodyY = 0f, bodyZ = 0f;
            Bounds bodyBounds = bodyMesh.bounds;
            Mesh scratchL = null, scratchR = null;

            if (builtIn)
            {
                wheelbase = 2.425f; track = 1.46f; tyre = 0.31f / TyreScale; bodyYaw = 0f;
                bodyZ = -BuiltInAxleMidZ;
            }
            else
            {
                // The exporter guarantees which file is the front axle, so the
                // model's heading is read rather than inferred.
                float frontZ = frontAxle.bounds.center.z, rearZ = rearAxle.bounds.center.z;
                bodyYaw = frontZ < rearZ ? 180f : 0f;
                wheelbase = Mathf.Abs(frontZ - rearZ);

                // Left and right within one axle, which IS unambiguous: split on
                // the sign of x and take the centre of each half.
                SplitSides(frontAxle, model.key, out scratchL, out scratchR,
                           out var negCentre, out var posCentre);
                track = Mathf.Abs(posCentre.x - negCentre.x);

                // The rig draws the left pair with a 180 degree flip, so it
                // wants a right-hand wheel - and taking the one already on that
                // side beats mirroring a left one, which reverses the winding
                // and the texture with it.
                wheelSrc = bodyYaw == 0f ? scratchR : scratchL;

                var wb = wheelSrc.bounds;
                tyre = Mathf.Max(wb.size.y, wb.size.z) * 0.5f;
                // Put the model's own hub where the rig hangs its hubs, which is
                // wheelRadius = tyre * TyreScale off the ground. Lifting the
                // contact patch to y = 0 instead — what this did — is only the
                // same thing when the drawn tyre is the modelled one, and it
                // never is: the project runs every tyre at 0.93, so the hub came
                // out 2 cm below the middle of its arch on every car in the pack.
                bodyY = TyreScale * tyre - wb.center.y;
                // And slide the body until its axle midpoint is on the rig's
                // origin, which is the point the four wheels are hung around.
                // The yaw is applied to the body BEFORE this offset, so a car
                // that had to be turned round has its mesh Z mirrored first.
                bodyZ = -(bodyYaw == 0f ? 1f : -1f) * (frontZ + rearZ) * 0.5f;
                if (bodyYaw != 0f)
                    bodyBounds = new Bounds(
                        new Vector3(-bodyBounds.center.x, bodyBounds.center.y, -bodyBounds.center.z),
                        bodyBounds.size);
            }
            if (wheelSrc == null) throw new Exception("wheel mesh missing");

            // Wheel mesh: re-centred on its own axle so the rig can spin it.
            // Recentre saves an asset copy, so the split halves are scratch and
            // go away — an in-memory Mesh left behind here is a leak that only
            // shows up as editor memory creeping across rebuilds.
            Mesh wheel = wheelSrc;
            if (!builtIn)
            {
                wheel = Recentre(wheelSrc, bodyYaw, model.key);
                UnityEngine.Object.DestroyImmediate(scratchL);
                UnityEngine.Object.DestroyImmediate(scratchR);
            }

            var def = new GameObject(model.key);
            var d = def.AddComponent<CarModelDef>();
            d.key = model.key;
            d.displayName = model.name;
            d.bodyMesh = bodyMesh;
            d.wheelMesh = wheel;
            d.bodyYaw = bodyYaw;
            d.bodyYOffset = bodyY;
            d.bodyZOffset = bodyZ;
            d.wheelbase = wheelbase;
            d.trackWidth = track;
            d.wheelRadius = tyre * TyreScale;
            d.wheelMeshScale = TyreScale;

            if (builtIn)
            {
                // Verbatim from the builder, for the reasons above.
                // Verbatim, then slid with the body: a box left on the rig's
                // origin while the mesh moves is the same bug one level down.
                d.colliderCenter = new Vector3(0f, 0.72f, 0.05f + bodyZ);
                d.colliderSize = new Vector3(1.72f, 1.0f, 4.1f);
                d.blobSize = new Vector2(2.3f, 4.6f);
            }
            else
            {
                // In the BODY's frame, not the mesh's. Both offsets were
                // missing here: a '69 Charger sits 16 cm up on its own lift, so
                // its box bottom was under the road and its top cut the roof off.
                d.colliderCenter = new Vector3(0f, bodyBounds.center.y + bodyY + 0.02f,
                                               bodyBounds.center.z + bodyZ);
                d.colliderSize = Vector3.Scale(bodyBounds.size, ColliderFit);
                d.blobSize = new Vector2(bodyBounds.size.x * BlobFit.x, bodyBounds.size.z * BlobFit.y);
            }

            MeasureCowl(bodyMesh, bodyYaw, bodyY, bodyZ, d);

            BakeSkins(d, texDir, model.key, shader);

            string prefabPath = OutDir + "/" + model.key + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(def, prefabPath);
            UnityEngine.Object.DestroyImmediate(def);

            log.Add($"{model.key,-13} wb={wheelbase:0.00} track={track:0.00} tyre={d.wheelRadius:0.000} " +
                    $"yaw={bodyYaw:0} bodyY={bodyY:0.000} bodyZ={bodyZ:0.000} " +
                    $"box={d.colliderSize.x:0.00}x{d.colliderSize.y:0.00}x{d.colliderSize.z:0.00} " +
                    // The cowl's FRACTION back from the nose is the number worth
                    // reading: a bare Z says nothing without the car's length
                    // beside it, and every way this measurement has gone wrong
                    // shows up immediately as a fraction near 0 or near 1.
                    $"cowl=({d.cowlZ:0.00}, {d.cowlY:0.00}) nose={d.noseZ:0.00} " +
                    $"bonnet={(d.noseZ - d.cowlZ) / Mathf.Max(bodyBounds.size.z, 0.01f):0.00}L " +
                    $"roof={d.roofY:0.00} " +
                    $"skins={d.SkinCount} tris={bodyMesh.triangles.Length / 3}");
        }

        /// <summary>How many slices the top-surface scan cuts the car into, and
        /// how far off the centreline it samples. Three lanes rather than one
        /// because a ray straight down the seam of a mirrored model can slip
        /// between the two halves and hit nothing at all.</summary>
        const int ProfileSlices = 160;
        static readonly float[] ProfileLanes = { 0f, 0.14f, -0.14f };

        /// <summary>How steeply the bodywork has to fall away from the roof to
        /// still be windscreen. A screen runs at a 30-45 degree rake — grade
        /// 0.6 to 1.0 — and a bonnet at under 0.1, so anything in between
        /// separates them without a threshold that has to be tuned per car.
        /// </summary>
        const float ScreenGrade = 0.25f;

        /// <summary>
        /// The car's top surface along its length: for each slice, the highest
        /// bodywork a vertical ray down the centreline strikes.
        ///
        /// This replaced a scan that binned VERTICES and took the highest one
        /// in each bin, which is a different measurement and a much worse one.
        /// A 500-triangle body puts vertices only where its panels meet, so a
        /// slice halfway along a bonnet holds no bonnet vertex at all and
        /// reports whatever else happens to be in it — a sill, an arch lip, the
        /// floor. The profile that came out was sawtoothed: a 1966 GTO read
        /// 0.96 m at the front of its bonnet, 0.57 m ten centimetres further
        /// back, and 0.92 m ten past that, on a panel that is in fact smooth.
        ///
        /// Sampling the SURFACE has none of that. It also costs nothing: 160
        /// slices times three lanes over 500 triangles is a few hundred
        /// thousand barycentric tests per model, once, in the editor.
        /// </summary>
        static float[] TopProfile(Mesh mesh, out float z0, out float dz)
        {
            var b = mesh.bounds;
            z0 = b.min.z;
            dz = Mathf.Max(b.size.z, 1e-4f) / ProfileSlices;

            var top = new float[ProfileSlices];
            for (int i = 0; i < ProfileSlices; i++) top[i] = float.MinValue;

            var verts = mesh.vertices;
            var tris = mesh.triangles;
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                Vector3 a = verts[tris[t]], p = verts[tris[t + 1]], c = verts[tris[t + 2]];
                // Area of the triangle projected onto the ground. Zero means it
                // stands edge-on to a vertical ray, which no ray can land on.
                float det = (p.z - c.z) * (a.x - c.x) + (c.x - p.x) * (a.z - c.z);
                if (Mathf.Abs(det) < 1e-9f) continue;

                float minZ = Mathf.Min(a.z, Mathf.Min(p.z, c.z));
                float maxZ = Mathf.Max(a.z, Mathf.Max(p.z, c.z));
                int i0 = Mathf.Clamp(Mathf.FloorToInt((minZ - z0) / dz), 0, ProfileSlices - 1);
                int i1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - z0) / dz), 0, ProfileSlices - 1);
                for (int i = i0; i <= i1; i++)
                {
                    float z = z0 + (i + 0.5f) * dz;
                    for (int lane = 0; lane < ProfileLanes.Length; lane++)
                    {
                        float x = ProfileLanes[lane];
                        float l1 = ((p.z - c.z) * (x - c.x) + (c.x - p.x) * (z - c.z)) / det;
                        float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / det;
                        float l3 = 1f - l1 - l2;
                        if (l1 < -1e-5f || l2 < -1e-5f || l3 < -1e-5f) continue;
                        float y = l1 * a.y + l2 * p.y + l3 * c.y;
                        if (y > top[i]) top[i] = y;
                    }
                }
            }

            // A slice past either end of the bodywork takes its neighbour's
            // height, so the walks below never have to test for a hole.
            float carry = float.MinValue;
            for (int i = 0; i < ProfileSlices; i++)
            {
                if (top[i] > float.MinValue) carry = top[i];
                else if (carry > float.MinValue) top[i] = carry;
            }
            carry = float.MinValue;
            for (int i = ProfileSlices - 1; i >= 0; i--)
            {
                if (top[i] > float.MinValue) carry = top[i];
                else if (carry > float.MinValue) top[i] = carry;
            }
            return top;
        }

        /// <summary>
        /// Find the roof, the base of the windscreen and the nose by walking
        /// the top-surface profile from the back of the car forwards.
        ///
        /// A profile is the right instrument here because it needs no material
        /// names, no sub-object names and no UV convention — the pack has none
        /// of those consistently — and because "where does the bonnet stop" IS
        /// a question about height against length.
        ///
        /// The walk is: find the roof, cross its flat crown, ride the
        /// windscreen down, and stop where the surface flattens onto the
        /// bonnet. Anchoring on the roof is what makes it safe — the roof is
        /// the one landmark on a car that cannot be confused with anything
        /// else. Versions that anchored anywhere else all failed: comparing
        /// against a bonnet line sampled from the front third breaks when that
        /// sample already CONTAINS windscreen (fastbacks, vans), and taking the
        /// steepest rise finds the bumper climbing to the bonnet.
        ///
        /// Crossing the crown before looking for the descent is the part that
        /// is easy to leave out. The roof anchor lands on the frontmost slice
        /// within 6 cm of the peak, which on a saloon is still several slices
        /// of flat roof short of the screen — stopping at the first flat step
        /// leaves the cowl ON THE ROOF, which is where a Land Rover, a CX and
        /// an A80 all ended up.
        ///
        /// A cab-over van has no bonnet at all and the walk correctly returns a
        /// cowl within a few centimetres of its nose. That is not a failure, it
        /// is what a van is.
        /// </summary>
        static void MeasureCowl(Mesh mesh, float bodyYaw, float bodyY, float bodyZ, CarModelDef d)
        {
            if (mesh == null || mesh.vertexCount == 0) return;

            float[] top = TopProfile(mesh, out float z0, out float dz);

            // bodyYaw is the turn that puts the model's nose on +Z, so a yaw of
            // 180 means the nose is at -Z in MESH space and every Z the scan
            // reports has to be mirrored on the way out.
            bool noseIsPlusZ = Mathf.Abs(bodyYaw) < 90f;
            int noseSlice = noseIsPlusZ ? ProfileSlices - 1 : 0;
            int stepIn = noseIsPlusZ ? -1 : 1;
            int Slice(int k) => noseSlice + stepIn * k;
            bool InRange(int i) => i >= 0 && i < ProfileSlices && top[i] > float.MinValue;
            float RigZ(float meshZ) => (noseIsPlusZ ? meshZ : -meshZ) + bodyZ;

            // The roof is the highest point IN THE FRONT THREE QUARTERS.
            //
            // A plain maximum over the whole car makes a 1969 Daytona's rear
            // wing the roof, and an A80's spoiler pulls it up the same way —
            // the scan then walks forward from the TAIL and reports a bonnet
            // 95% of the car long. But no car's roof BEGINS after three
            // quarters of its length, and every wing is in the last tenth: that
            // one line separates them and needs no threshold.
            int roofEnd = (int)(ProfileSlices * 0.75f);
            float roofTop = float.MinValue;
            for (int k = 0; k <= roofEnd; k++)
            {
                int i = Slice(k);
                if (InRange(i) && top[i] > roofTop) roofTop = top[i];
            }
            if (roofTop <= float.MinValue)
                for (int i = 0; i < ProfileSlices; i++) if (top[i] > roofTop) roofTop = top[i];
            d.roofY = roofTop + bodyY;

            int roofK = 0;
            for (int k = 0; k <= roofEnd; k++)
            {
                int i = Slice(k);
                if (!InRange(i)) continue;
                if (top[i] >= roofTop - 0.06f) { roofK = k; break; }
            }

            int cowlK = roofK;
            bool onScreen = false;
            for (int k = roofK - 1; k >= 0; k--)
            {
                int i = Slice(k), j = Slice(k + 1);
                if (!InRange(i) || !InRange(j)) break;
                if ((top[j] - top[i]) / dz >= ScreenGrade) { cowlK = k; onScreen = true; continue; }
                if (onScreen) break;    // flattened onto the bonnet: done
                cowlK = k;              // still crossing the crown of the roof
            }
            // Never let it reach the bumper. A cowl in the first few percent of
            // the car is only true of a cab-over, and a cab-over's roof starts
            // there too — so the clamp costs nothing on a van and catches the
            // nose-slope case on everything else.
            cowlK = Mathf.Max(cowlK, (int)(ProfileSlices * 0.03f));
            int cowlSlice = Mathf.Clamp(Slice(cowlK), 0, ProfileSlices - 1);

            d.cowlZ = RigZ(z0 + (cowlSlice + 0.5f) * dz);

            // The bonnet camera has to clear everything from here to the nose,
            // not just the slice the cowl stands on. On this pack the cowl IS
            // the high point of every bonnet, but a scoop or a raised crest
            // would put the lens inside it and nothing else would say so.
            float bonnet = top[cowlSlice];
            for (int k = 0; k <= cowlK; k++)
            {
                int i = Slice(k);
                if (InRange(i) && top[i] > bonnet) bonnet = top[i];
            }
            // Below the roof by construction: a bonnet line AT roof height is
            // the failure this measurement exists to avoid, so it is clamped
            // rather than trusted.
            d.cowlY = Mathf.Min(bonnet + bodyY, d.roofY - 0.05f);

            d.noseZ = RigZ(noseIsPlusZ ? mesh.bounds.max.z : mesh.bounds.min.z);
        }

        static Mesh AxleMesh(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var m = go != null ? go.GetComponentInChildren<MeshFilter>()?.sharedMesh : null;
            if (m == null) throw new Exception("axle OBJ missing or empty: " + path);
            return m;
        }

        /// <summary>
        /// Cut a two-wheel axle mesh into its left and right halves on the sign
        /// of x, and hand back the centre of each. Triangles are assigned by
        /// their own centroid, so a tyre never ends up straddling the split.
        /// </summary>
        static void SplitSides(Mesh src, string key, out Mesh neg, out Mesh pos,
                               out Vector3 negCentre, out Vector3 posCentre)
        {
            var verts = src.vertices;
            var uv = src.uv;
            var tris = src.triangles;
            var negTris = new List<int>();
            var posTris = new List<int>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                float cx = (verts[tris[i]].x + verts[tris[i + 1]].x + verts[tris[i + 2]].x) / 3f;
                var into = cx < 0f ? negTris : posTris;
                into.Add(tris[i]); into.Add(tris[i + 1]); into.Add(tris[i + 2]);
            }
            if (negTris.Count == 0 || posTris.Count == 0)
                throw new Exception($"axle did not split into two wheels ({negTris.Count}/{posTris.Count} indices)");

            neg = Compact(verts, uv, negTris, key + "_wheel_l");
            pos = Compact(verts, uv, posTris, key + "_wheel_r");
            negCentre = neg.bounds.center;
            posCentre = pos.bounds.center;
        }

        /// <summary>Build a mesh from a subset of triangles, dropping the
        /// vertices it does not use.</summary>
        static Mesh Compact(Vector3[] verts, Vector2[] uv, List<int> tris, string name)
        {
            var map = new Dictionary<int, int>();
            var outV = new List<Vector3>();
            var outUV = new List<Vector2>();
            var outT = new List<int>(tris.Count);
            foreach (int i in tris)
            {
                if (!map.TryGetValue(i, out int j))
                {
                    j = outV.Count;
                    map[i] = j;
                    outV.Add(verts[i]);
                    if (uv != null && uv.Length == verts.Length) outUV.Add(uv[i]);
                }
                outT.Add(j);
            }
            var m = new Mesh { name = name };
            m.SetVertices(outV);
            if (outUV.Count == outV.Count) m.SetUVs(0, outUV);
            m.SetTriangles(outT, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// Copy a wheel to the origin, through the body's yaw, and save it as an
        /// asset. Saved rather than held in the prefab because a mesh created in
        /// memory and referenced by a prefab does not survive a domain reload.
        /// </summary>
        static Mesh Recentre(Mesh src, float yaw, string key)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var verts = src.vertices;
            var centre = rot * src.bounds.center;
            for (int i = 0; i < verts.Length; i++) verts[i] = rot * verts[i] - centre;

            var m = new Mesh { name = key + "_wheel" };
            m.vertices = verts;
            m.uv = src.uv;
            m.triangles = src.triangles;
            m.RecalculateNormals();
            m.RecalculateBounds();

            string p = GenDir + "/" + m.name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(p) != null) AssetDatabase.DeleteAsset(p);
            AssetDatabase.CreateAsset(m, p);
            return m;
        }

        // ------------------------------------------------------------------
        //  Liveries
        // ------------------------------------------------------------------
        static void BakeSkins(CarModelDef d, string texDir, string key, Shader shader)
        {
            var files = Directory.Exists(texDir)
                ? Directory.GetFiles(texDir, "*.png").OrderBy(f => f).ToArray()
                : new string[0];

            var mats = new List<Material>();
            var names = new List<string>();
            var colors = new List<Color>();

            foreach (var file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                string assetPath = texDir + "/" + name + ".png";
                PointFilter(assetPath);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (tex == null) continue;

                string matPath = MatDir + "/" + key + "_" + name + ".mat";
                bool isWheelSheet = name == "wheel";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                mat.shader = shader;
                mat.mainTexture = tex;
                mat.color = Color.white;
                mat.SetFloat("_Cutoff", 0f);
                mat.SetFloat("_Affine", 1f);
                EditorUtility.SetDirty(mat);

                // A sheet named "wheel" is exactly that — the one model in the
                // set whose wheels are UV'd onto their own texture rather than
                // onto a neutral corner of the body sheet. It is a wheel
                // material, never a paint colour.
                if (isWheelSheet) { d.wheelMaterial = mat; continue; }

                mats.Add(mat);
                names.Add(name);
                colors.Add(AverageColor(file));
            }

            d.skinMaterials = mats.ToArray();
            d.skinNames = names.ToArray();
            d.skinColors = colors.ToArray();

            // The FD's wheel UVs land on the painted half of its sheet, so a red
            // car would come out with red wheels. Pin them to the neutral skin,
            // which is what the builder has always done by hand.
            if (d.wheelMaterial == null && key == CarModelLibrary.Default)
            {
                int i = names.IndexOf("crystal_white");
                if (i >= 0) d.wheelMaterial = mats[i];
            }
        }

        /// <summary>PS1 look: point sampling, no compression artefacts on a
        /// 128 px sheet, and no mip chain to blur the pixels away at distance.
        /// The builder's own importer sweep already covers everything under
        /// Art/, so in a full build this is a no-op — it is here so the bake
        /// menu item stands on its own.</summary>
        static void PointFilter(string assetPath)
        {
            var imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (imp == null) return;
            if (imp.filterMode == FilterMode.Point && !imp.mipmapEnabled &&
                imp.textureCompression == TextureImporterCompression.Uncompressed) return;
            imp.filterMode = FilterMode.Point;
            imp.mipmapEnabled = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.maxTextureSize = 256;
            imp.SaveAndReimport();
        }

        /// <summary>
        /// Mean colour of a livery sheet, read straight off disk. Going through
        /// the imported Texture2D would mean flipping Read/Write on every skin
        /// in the pack, which doubles their memory in the build for a number
        /// that is only wanted once at bake time.
        ///
        /// Fully transparent pixels are skipped, and near-black is down-weighted
        /// so the window glass and tyre patches on a shared sheet do not drag
        /// every car's average toward grey.
        /// </summary>
        static Color AverageColor(string file)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(file))) return Color.grey;
                var px = tex.GetPixels32();
                double r = 0, g = 0, b = 0, w = 0;
                foreach (var p in px)
                {
                    if (p.a < 8) continue;
                    float lum = (p.r * 0.30f + p.g * 0.59f + p.b * 0.11f) / 255f;
                    // Weight by how much of a colour the pixel is at all.
                    float weight = Mathf.Clamp01(lum * 3f) * Mathf.Clamp01((1f - lum) * 4f + 0.25f);
                    r += p.r * weight; g += p.g * weight; b += p.b * weight; w += weight;
                }
                if (w < 1e-3) return Color.grey;
                return new Color((float)(r / w) / 255f, (float)(g / w) / 255f, (float)(b / w) / 255f);
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }
    }
}
