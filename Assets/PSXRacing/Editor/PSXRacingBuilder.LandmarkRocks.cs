using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LINN COVE GRANITE (linncove, owner 2026-11-16, with two photographs
    /// of the viaduct in peak fall colour: "would also be nice to add some of
    /// the iconic rock face to the track and the title shot angle"). The
    /// viaduct was built round Grandfather Mountain's boulder field so as not
    /// to blast it, and the 2018 lidar shows every block of it: the ground
    /// minus a smoothed copy of itself stands up a metre or more over a few
    /// metres (tools/roads/linncove_rocks.mjs, read offline from the road
    /// bake's cache). Each block it found, under the deck and on the slope
    /// above and below the road, is placed here where it really lies, at its
    /// own footprint, length and height, as the owner's NatureManufacture
    /// forest stones turned to grey granite: a block of 9 m or more as two to
    /// four stones along its long axis, a low one as a slab.
    ///
    /// Rules: nothing within a lane's reach of an at-grade road, nothing
    /// through a deck, pier, wall or rock cut (an overlap test against what is
    /// built), and the forest keeps out of every block (StageKeepOut). Drawn by
    /// plain renderers per 128 m cell, solid by a mesh collider of the same
    /// stones (named "Boulder", as PlaceBoulders' are: the plant fit and
    /// PlantSolidAudit take it as a solid). PC scene pass with the store
    /// files only; PSX_LINNCOVE_ROCKS=0 switches the root off at load
    /// (Scripts/LandmarkRocks.cs).
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        [System.Serializable]
        class LandmarkRock
        {
            public float x, z, y, wide, axis, peak, steepShare, slope, fall, station, offset, road, roadY;
            public float @long;
            public int area;
            public bool underDeck;
        }
        [System.Serializable] class LandmarkRockFile { public float baseM; public float[] viaduct; public LandmarkRock[] rocks; }

        /// <summary>The forest's keep-out over the blocks: (x, z, r) in a 16 m
        /// hash, for the stage whose far DEM it was built against.</summary>
        static readonly Dictionary<long, List<Vector3>> landmarkKeep = new Dictionary<long, List<Vector3>>();
        static short[] landmarkKeepFor;
        const float LandmarkKeepCell = 16f;

        /// <summary>Is (x, z) inside a landmark block (this stage's only)?</summary>
        internal static bool LandmarkKeepOut(float x, float z)
        {
            if (landmarkKeepFor == null || landmarkKeepFor != demFar || landmarkKeep.Count == 0) return false;
            long k = ((long)Mathf.FloorToInt(x / LandmarkKeepCell) << 32) ^ (uint)Mathf.FloorToInt(z / LandmarkKeepCell);
            if (!landmarkKeep.TryGetValue(k, out var list)) return false;
            foreach (var c in list) { float dx = x - c.x, dz = z - c.y; if (dx * dx + dz * dz < c.z * c.z) return true; }
            return false;
        }

        static void LandmarkKeepAdd(float x, float z, float r)
        {
            int c0 = Mathf.FloorToInt((x - r) / LandmarkKeepCell), c1 = Mathf.FloorToInt((x + r) / LandmarkKeepCell);
            int r0 = Mathf.FloorToInt((z - r) / LandmarkKeepCell), r1 = Mathf.FloorToInt((z + r) / LandmarkKeepCell);
            for (int cz = r0; cz <= r1; cz++)
                for (int cx = c0; cx <= c1; cx++)
                {
                    long k = ((long)cx << 32) ^ (uint)cz;
                    if (!landmarkKeep.TryGetValue(k, out var list)) landmarkKeep[k] = list = new List<Vector3>();
                    list.Add(new Vector3(x, z, r));
                }
        }

        /// <summary>The window of the route the blocks are taken from: the
        /// viaduct and this far either side of it (m), and this far out (m).</summary>
        const float LandmarkRockEndsM = 90f, LandmarkRockReachM = 230f;
        const float LandmarkCellM = 128f;

        static void BuildLandmarkRocks(List<Vector3> pts, Transform parent)
        {
            landmarkKeep.Clear();
            landmarkKeepFor = null;
            if (!PCContent || stageWp == null) return;
            // PSX_LINNCOVE_ROCKS=0 at the scene build: none, and no clearing for them (the base exactly)
            if (System.Environment.GetEnvironmentVariable("PSX_LINNCOVE_ROCKS") == "0") { Log("Landmark rocks: OFF at the build (PSX_LINNCOVE_ROCKS=0)."); return; }
            string path = ProjectRootPath(StageArtDir + "/" + StagePrefix + "_rocks.json");
            if (!File.Exists(path)) return;
            var file = JsonUtility.FromJson<LandmarkRockFile>(File.ReadAllText(path));
            if (file == null || file.rocks == null || file.rocks.Length == 0) return;

            // the stones, turned to Grandfather's granite: the forest stones'
            // scan is a dark mossy olive (linear mean 0.041, 0.042, 0.017) -
            // greyed and lifted to weathered, lichen-pale granite
            var mat = NMLitMat("NM_granite_linncove", "Assets/StoreAssets/NMForest/Rocks", "T_beech_forest_stones_01_BC_SM",
                               "T_beech_forest_stones_01_N", null, false, new Color(0.97f, 0.98f, 1.0f), maskIsHdrp: false);
            mat.SetVector("_LeafVivid", new Vector4(0.28f, 4.2f, 0f, 0f));
            EditorUtility.SetDirty(mat);
            var stones = new List<MeshForest.Variant>();
            for (int k = 1; k <= 14; k++)
            {
                var v = LoadVariant(NMDebrisDir, "beech_forest_stones_01_" + k, mat, ".fbx");
                if (v != null && v.lods[0] != null) stones.Add(v);
            }
            if (stones.Count == 0) { Log("Landmark rocks: no forest stone meshes (store packs missing) - none placed."); return; }
            // each stone's box in its own frame (after the FBX node transform)
            var lo = new Vector3[stones.Count]; var hi = new Vector3[stones.Count];
            for (int s = 0; s < stones.Count; s++)
            {
                var bb = stones[s].lods[0].bounds;
                Vector3 a = Vector3.positiveInfinity, b = Vector3.negativeInfinity;
                for (int c = 0; c < 8; c++)
                {
                    var q = stones[s].pre.MultiplyPoint3x4(new Vector3((c & 1) == 0 ? bb.min.x : bb.max.x,
                        (c & 2) == 0 ? bb.min.y : bb.max.y, (c & 4) == 0 ? bb.min.z : bb.max.z));
                    a = Vector3.Min(a, q); b = Vector3.Max(b, q);
                }
                lo[s] = a; hi[s] = b;
            }

            float v0 = file.viaduct != null && file.viaduct.Length == 2 ? file.viaduct[0] : float.MinValue;
            float v1 = file.viaduct != null && file.viaduct.Length == 2 ? file.viaduct[1] : float.MaxValue;
            float half = RoadWidth * 0.5f;
            Physics.SyncTransforms();
            var cells = new Dictionary<long, (List<CombineInstance> draw, List<CombineInstance> solid)>();
            var spots = new List<Vector4>();   // x, y, z, r of each stone placed
            int used = 0, stonesPlaced = 0, skipLane = 0, skipSolid = 0, skipDeck = 0, skipShape = 0, skipFar = 0, skipCrowd = 0;
            foreach (var rk in file.rocks)
            {
                if (rk.station < v0 - LandmarkRockEndsM || rk.station > v1 + LandmarkRockEndsM || rk.road > LandmarkRockReachM) { skipFar++; continue; }
                // a road's cut or fill edge, a path, a wall: long, thin and low
                if (rk.area < 8 || rk.peak < 0.9f || (rk.@long > 4f * Mathf.Max(rk.wide, 0.5f) && rk.peak < 2.5f)) { skipShape++; continue; }
                float L = Mathf.Clamp(rk.@long, 1.6f, 24f), Wd = Mathf.Clamp(rk.wide, 1.3f, 16f);
                if (Wd > L) { float t = L; L = Wd; Wd = t; }
                float Ht = Mathf.Clamp(Mathf.Max(rk.peak * 1.25f, 0.32f * Wd), 0.8f, 9f);
                int n = L > 9f ? Mathf.Clamp(Mathf.RoundToInt(L / 6f), 2, 4) : 1;
                float ax = rk.axis * Mathf.Deg2Rad;
                var axis = new Vector3(Mathf.Cos(ax), 0f, Mathf.Sin(ax));   // the block's long axis (x east, z north)
                bool any = false;
                for (int j = 0; j < n; j++)
                {
                    float hj = FringeHash(Mathf.RoundToInt(rk.x * 10f) + j * 7, Mathf.RoundToInt(rk.z * 10f) - j * 3);
                    float hk = FringeHash(Mathf.RoundToInt(rk.z * 10f) + j * 11, Mathf.RoundToInt(rk.x * 10f) + 5);
                    float along = n == 1 ? 0f : (j / (float)(n - 1) - 0.5f) * (L - L / n);
                    float sl = n == 1 ? L : L / n * 1.35f, sw = Wd * (n == 1 ? 1f : Mathf.Lerp(0.75f, 1f, hk));
                    float sh = Ht * (n == 1 ? 1f : Mathf.Lerp(0.7f, 1f, hj));
                    var c = new Vector3(rk.x, 0f, rk.z) + axis * along + new Vector3(hj - 0.5f, 0f, hk - 0.5f) * (0.15f * sw);
                    float r = 0.5f * Mathf.Max(sl, sw);
                    // NOTHING IN A LANE: an at-grade road's tarmac, shoulder and wall
                    // reach; under a deck, clear of its underside by 2.5 m
                    float gy = float.MaxValue;
                    for (int q = 0; q < 5; q++)
                    {
                        float ang = q * 1.2566f;
                        float qx = c.x + (q == 4 ? 0f : Mathf.Cos(ang) * r * 0.7f), qz = c.z + (q == 4 ? 0f : Mathf.Sin(ang) * r * 0.7f);
                        gy = Mathf.Min(gy, Mathf.Min(StageGroundHeightAt(qx, qz), StageLatticeY(qx, qz)));
                    }
                    float sink = sh * (0.22f + 0.1f * hk);
                    float top = gy - sink + sh;
                    if (StageCorridor(c.x, c.z, half + 6f + r, out RoadFoot f))
                    {
                        bool deck = BridgeAt(f.station) > 0.5f;
                        if (!deck) { skipLane++; continue; }
                        if (top > f.roadY - 4.5f) { skipDeck++; continue; }
                    }
                    // the stone: its own box scaled to the block, turned along it,
                    // tilted half way to the slope it lies on
                    int s = (int)(FringeHash(Mathf.RoundToInt(c.x * 3f), Mathf.RoundToInt(c.z * 3f)) * 9973f) % stones.Count;
                    Vector3 size = hi[s] - lo[s];
                    // the model's longer level side along the block
                    bool swap = size.z > size.x;
                    float mx = swap ? size.z : size.x, mz = swap ? size.x : size.z;
                    var scale = new Vector3(sl / Mathf.Max(mx, 0.05f), sh / Mathf.Max(size.y, 0.05f), sw / Mathf.Max(mz, 0.05f));
                    if (swap) scale = new Vector3(scale.z, scale.y, scale.x);
                    float yaw = -rk.axis + (swap ? 90f : 0f) + (hj - 0.5f) * 24f;
                    float gx = StageGroundHeightAt(c.x + 2f, c.z) - StageGroundHeightAt(c.x - 2f, c.z);
                    float gz = StageGroundHeightAt(c.x, c.z + 2f) - StageGroundHeightAt(c.x, c.z - 2f);
                    var nrm = new Vector3(-gx / 4f, 1f, -gz / 4f).normalized;
                    var rot = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, nrm, 0.5f)) * Quaternion.Euler(0f, yaw, 0f);
                    // seated: the box's bottom at the ground less the sink
                    var pos = new Vector3(c.x, gy - sink - lo[s].y * scale.y, c.z);
                    var centre = pos + rot * Vector3.Scale((lo[s] + hi[s]) * 0.5f, scale);
                    var ext = Vector3.Scale(size * 0.5f, scale);
                    // NOTHING THROUGH A DECK, PIER, WALL OR CUT: what is built, tested
                    bool clear = true;
                    foreach (var col in Physics.OverlapBox(centre, ext * 1.05f + Vector3.one * 0.3f, rot, ~0, QueryTriggerInteraction.Ignore))
                    {
                        string nm = col.gameObject.name;
                        if (nm.StartsWith("Wall") || nm.StartsWith("BankColl") || nm.StartsWith("BridgeDeck") || nm == "Pier" ||
                            nm.StartsWith("Pier") || nm.StartsWith("Parapet") || nm.StartsWith("Rail") || nm.StartsWith("BridgeJoint") ||
                            nm.StartsWith("RetainWall")) { clear = false; break; }
                    }
                    if (!clear) { skipSolid++; continue; }
                    // not wholly inside a stone already placed
                    bool crowded = false;
                    foreach (var o in spots)
                        if (new Vector2(o.x - c.x, o.z - c.z).magnitude < 0.45f * (o.w + r) && j == 0) { crowded = true; break; }
                    if (crowded) { skipCrowd++; continue; }
                    spots.Add(new Vector4(c.x, top, c.z, r));
                    var trs = Matrix4x4.TRS(pos, rot, scale) * stones[s].pre;
                    long key = ((long)Mathf.FloorToInt(c.x / LandmarkCellM) << 32) ^ (uint)Mathf.FloorToInt(c.z / LandmarkCellM);
                    if (!cells.TryGetValue(key, out var cell)) cells[key] = cell = (new List<CombineInstance>(), new List<CombineInstance>());
                    bool nearRoad = rk.road < 60f;
                    var drawMesh = stones[s].lods[nearRoad ? 0 : 1];
                    for (int sm = 0; sm < drawMesh.subMeshCount; sm++)
                        cell.draw.Add(new CombineInstance { mesh = drawMesh, subMeshIndex = sm, transform = trs });
                    var solidMesh = stones[s].lods[2];
                    for (int sm = 0; sm < solidMesh.subMeshCount; sm++)
                        cell.solid.Add(new CombineInstance { mesh = solidMesh, subMeshIndex = sm, transform = trs });
                    LandmarkKeepAdd(c.x, c.z, r * 0.85f + 1.0f);   // the block's own footprint and a metre: the woods grow between the
                    // blocks (2.5 m round each cleared Grandfather's flank to dark bare ground, and the title's foreground trees)
                    stonesPlaced++;
                    any = true;
                }
                if (any) used++;
            }
            if (stonesPlaced == 0) { Log("Landmark rocks: none placed."); return; }

            var root = new GameObject("LandmarkRocks");
            root.transform.SetParent(parent, false);
            root.AddComponent<LandmarkRocks>();
            root.AddComponent<PCOnly>();
            var phys = GetOrCreatePhysMat("WallPhys", 0.05f, 0.05f);
            int cellN = 0, tris = 0;
            foreach (var kv in cells)
            {
                var (draw, solid) = kv.Value;
                if (draw.Count == 0) continue;
                // local to the cell's first stone, so the mesh's floats stay small
                Vector3 o = draw[0].transform.GetColumn(3);
                var toLocal = Matrix4x4.Translate(-o);
                var dl = new CombineInstance[draw.Count];
                for (int i = 0; i < dl.Length; i++) { var ci = draw[i]; ci.transform = toLocal * ci.transform; dl[i] = ci; }
                var sl = new CombineInstance[solid.Count];
                for (int i = 0; i < sl.Length; i++) { var ci = solid[i]; ci.transform = toLocal * ci.transform; sl[i] = ci; }
                var dm = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = "LinnCoveRocks_" + cellN };
                dm.CombineMeshes(dl, true, true, false);
                dm.RecalculateBounds();
                SaveMesh(dm, "LandmarkRocks_" + StagePrefix + "_" + cellN);
                var cm = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = "LinnCoveRocksSolid_" + cellN };
                cm.CombineMeshes(sl, true, true, false);
                cm.RecalculateBounds();
                SaveMesh(cm, "LandmarkRocksSolid_" + StagePrefix + "_" + cellN);
                var go = new GameObject("Rocks_" + cellN);
                go.transform.SetParent(root.transform, false);
                go.transform.position = o;
                go.AddComponent<MeshFilter>().sharedMesh = dm;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                go.isStatic = true;
                var hull = new GameObject("Boulder");
                hull.transform.SetParent(go.transform, false);
                var mc = hull.AddComponent<MeshCollider>();
                mc.sharedMesh = cm;
                mc.sharedMaterial = phys;
                hull.layer = SolidLayer;
                hull.isStatic = true;
                tris += dm.triangles.Length / 3;
                cellN++;
            }
            landmarkKeepFor = demFar;
            Physics.SyncTransforms();
            Log($"Landmark rocks ({StagePrefix}_rocks.json, the 2018 lidar): {stonesPlaced} granite stones for {used} of {file.rocks.Length} blocks in {cellN} cells, " +
                $"{tris} tris; skipped {skipFar} out of the window, {skipShape} by shape (edges, paths), {skipLane} at a road's reach, " +
                $"{skipDeck} too close under a deck, {skipSolid} against a deck/pier/wall/cut, {skipCrowd} crowded; the forest keeps out of every one.");
        }
    }
}
