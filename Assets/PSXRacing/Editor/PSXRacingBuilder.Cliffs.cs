using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE ROAD CUTS' ROCK: the owner's NatureManufacture Meadow Environment
    /// photoscanned cliffs (2026-11-08: "rock faces are still flat"; the pack
    /// bought for its "rock face cliffs"). PC scene pass with the store files
    /// only. Along every faced cut, cliff meshes stand at the toe, sized to the
    /// face's height, turned to face the road and set into the hill so their
    /// fronts stand at most ~0.15 m proud of the smooth face - which stays the
    /// drawn backing and the collider, so driving is unchanged. Drawn by
    /// MeshForest (instanced, the pack's LODs).
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        const string MeadowCliffDir = "Assets/StoreAssets/Meadow/Cliffs";
        const string MeadowMapleDir = "Assets/StoreAssets/Meadow/Maple";
        /// <summary>The cliffs with a face (the flat overpaint pieces 08-10 and
        /// base_02 are ground decals, not faces).</summary>
        static readonly string[] CliffNames =
        {
            "cliff_base_01", "cliff_base_03", "cliff_base_04", "cliff_base_05", "cliff_base_06", "cliff_base_07",
            "cliff_piece_01", "cliff_piece_02", "cliff_piece_03", "cliff_piece_04", "cliff_piece_05",
            "cliff_piece_06", "cliff_piece_07",
        };
        /// <summary>OFF (owner 2026-11-08, on the first placement: "This looks
        /// very unnatural" - a continuous ribbon of rock on a constant-height
        /// cut reads as a retaining wall). The cut's own SHAPE has to vary
        /// first: height, crest, the rock-top shelf.</summary>
        const bool CliffsOn = true;
        const float CliffMinFaceM = 1.2f;

        struct CliffInfo { public int v; public float frontAngle, front, length; }
        /// <summary>r &gt; 0: re-tested at the forest pass against walls and
        /// portals (a sphere of r, lift over pos); solid: its collider object.</summary>
        struct CliffPlace { public Vector3 pos; public float yaw, scale, r, lift; public int v; public GameObject solid; }
        static int rockDropped;
        /// <summary>Reset by BuildStageBanks' boulder pass (PlaceBoulders).</summary>
        static bool rockSynced;
        static readonly List<CliffPlace> cliffPlaces = new List<CliffPlace>();
        static int cliffsPlaced;

        /// <summary>Loads the cliffs into the kit (once) and measures each:
        /// which way its face looks (area-weighted normal, flattened), how far
        /// its front stands from its centre, how long it runs.</summary>
        static List<CliffInfo> LoadCliffs(MeshTreeKit kit, List<MeshForest.Variant> list)
        {
            var infos = new List<CliffInfo>();
            if (!CliffsOn) return infos;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MeadowCliffDir + "/" + CliffNames[0] + ".FBX") == null) return infos;
            // darker and warmer than the scan reads under PSX/Lit: the Parkway's
            // gneiss is dark grey-brown, weathered, often wet
            var mat = NMLitMat("NM_meadow_cliff", MeadowCliffDir, "T_Photoscanned_Cliff_01_BC", "T_Photoscanned_Cliff_01_Normal",
                               null, false, new Color(0.62f, 0.6f, 0.56f));
            foreach (var name in CliffNames)
            {
                var v = LoadVariant(MeadowCliffDir, name, mat);
                if (v == null) continue;
                v.thin = 0.9f;        // shrink, not dither, at the draw limit
                v.shadows = true;
                Vector3 acc = Vector3.zero;
                using (var data = Mesh.AcquireReadOnlyMeshData(v.lods[0]))
                {
                    var d = data[0];
                    var nrm = new NativeArray<Vector3>(d.vertexCount, Allocator.Temp);
                    var pos = new NativeArray<Vector3>(d.vertexCount, Allocator.Temp);
                    d.GetNormals(nrm); d.GetVertices(pos);
                    for (int i = 0; i < nrm.Length; i++) acc += v.pre.MultiplyVector(nrm[i]);
                    acc.y = 0f;
                    var front = acc.sqrMagnitude > 1e-6f ? acc.normalized : Vector3.forward;
                    var right = new Vector3(front.z, 0f, -front.x);
                    float f = 0f, rMin = float.MaxValue, rMax = float.MinValue;
                    for (int i = 0; i < pos.Length; i++)
                    {
                        var p = v.pre.MultiplyPoint3x4(pos[i]);
                        f = Mathf.Max(f, p.x * front.x + p.z * front.z);
                        float r = p.x * right.x + p.z * right.z;
                        rMin = Mathf.Min(rMin, r); rMax = Mathf.Max(rMax, r);
                    }
                    nrm.Dispose(); pos.Dispose();
                    infos.Add(new CliffInfo
                    {
                        v = list.Count, frontAngle = Mathf.Atan2(front.x, front.z) * Mathf.Rad2Deg,
                        front = f, length = Mathf.Max(1f, rMax - rMin),
                    });
                }
                list.Add(v);
            }
            Log($"Cliffs: {infos.Count} Meadow cliff meshes for the road cuts.");
            LoadBoulders(list);
            return infos;
        }

        /// <summary>One bank's faced stations dressed as a NATURAL cut (owner
        /// 2026-11-08: a continuous rock ribbon was "very unnatural"; real cuts
        /// are "often covered in a lot of foliage"): vegetation hanging over the
        /// crest, ferns and grass out of the face, a dense foot; and only now and
        /// then a rock OUTCROP - smaller than the face, sunk into it so only its
        /// bulges break through, with gaps of plain face between. Visual only:
        /// the cut's shape and collider are untouched.</summary>
        static void PlaceCliffs(List<Vector3> pts, List<int> st, List<float> hh, bool[] faced, Vector3[] outwAt,
                                float[] distAt, int R, int seed, Vector3[] crest = null)
        {
            var kit = LoadMeshTreeKit();
            if (kit == null) return;
            bool rock = kit.cliffs != null && kit.cliffs.Count > 0;
            float next = float.MinValue;
            for (int k = 0; k < R; k++)
            {
                if (!faced[k] || hh[k] < 0.8f) continue;
                float h = hh[k];
                int i = st[k];
                Vector3 outw = outwAt[k];
                Vector3 side = new Vector3(outw.z, 0f, -outw.x);
                Vector3 toe = pts[i] + outw * (RoadWidth * 0.5f + CutToeE);
                // on this side's banked edge (PSXRacingBuilder.Bank)
                float edgeY = pts[i].y + BankEdge(i, Vector3.Dot(outw, RightAt(pts, i)) >= 0f ? 1f : -1f);
                toe.y = edgeY;
                Vector3 top = crest != null ? crest[k] : toe + Vector3.up * h + outw * (h * BankBatter);
                float r1 = RHash(k, seed, 201), r2 = RHash(seed, k, 203), r3 = RHash(k + 7, seed, 207);

                // OVER THE CREST: bushes and young trees whose crowns hang over the edge
                if (r1 < 0.8f) AddLedge(top - outw * 0.35f + side * ((r2 - 0.5f) * 2f) + Vector3.down * 0.15f, 1);
                if (r2 < 0.55f) AddLedge(top + outw * 1.4f + side * ((r3 - 0.5f) * 2.5f), 1);
                // OUT OF THE FACE: ferns and grass in its cracks
                int faceN = h > 3f ? 3 : 2;
                for (int q = 0; q < faceN; q++)
                {
                    float t = Mathf.Lerp(0.18f, 0.85f, RHash(k * 5 + q, seed, 213));
                    Vector3 fp = Vector3.Lerp(toe, top, t) - outw * 0.12f + side * ((RHash(q, k, 215) - 0.5f) * 2f);
                    if (RHash(k, q, 217) < 0.7f) AddLedge(fp, 2);
                }
                // THE FOOT: a dense fringe where the verge meets the rock
                AddLedge(toe - outw * 0.25f + side * ((r3 - 0.5f) * 2f), 3);
                if (r1 > 0.35f) AddLedge(toe - outw * 0.6f + side * ((r1 - 0.5f) * 2f), 3);
                // TALUS: fallen blocks at the foot of a rock face, set into it
                // (at most 0.15 m in front of its solid: a car scraping the
                // face is not to drive through stone it cannot touch)
                // In CLUSTERS along the cut (a slow noise sets how much falls
                // here), each stone anywhere along the chord: the ground line
                // is not a straight edge.
                if (boulderVars.Count > 0 && h >= 1.5f)
                {
                    float heap = RNoise(distAt[k] / 9f, 0.4f, seed + 231);
                    int tries = 2 + Mathf.RoundToInt(heap * 5f * Mathf.InverseLerp(1.5f, 6f, h));
                    for (int q = 0; q < tries; q++)
                    {
                        float tq = RHash(k * 7 + q, seed, 221);
                        if (tq > 0.25f + 0.6f * heap) continue;
                        int bv = boulderVars[(int)(RHash(q, k, 223) * 9973f) % boulderVars.Count];
                        var bvar = kit.variants[bv];
                        float sz = RHash(k, q, 227);
                        float bs = Mathf.Lerp(0.18f, 0.95f, sz * sz) / Mathf.Max(bvar.height, 0.1f);
                        float br = BoulderRadius(bvar) * bs;
                        Vector3 tp = toe + outw * (br - 0.1f) + side * ((RHash(q, k + 5, 229) - 0.5f) * 4f);
                        tp.y = edgeY - bvar.height * bs * Mathf.Lerp(0.15f, 0.45f, RHash(q + 3, k, 233));
                        if (!RockClearOfSolids(tp + Vector3.up * (bvar.height * bs * 0.4f), br, rockOk: true)) continue;
                        cliffPlaces.Add(new CliffPlace { pos = tp, yaw = tq * 1000f, scale = bs, v = bv, r = br, lift = bvar.height * bs * 0.4f });
                        talusN++;
                    }
                    // and leaf litter banked against the foot
                    if (kit.debris[0].Length > 0 && RHash(k, seed, 237) < 0.3f + 0.5f * heap)
                    {
                        int lv = kit.debris[0][(int)(RHash(seed, k, 239) * 9973f) % kit.debris[0].Length];
                        Vector3 lp = toe - outw * 0.35f + side * ((RHash(k, 2, 241) - 0.5f) * 3f);
                        lp.y = edgeY - 0.05f;
                        // (clear of a wall's stone and rail by its own reach)
                        if (RockClearOfSolids(lp + Vector3.up * 0.3f, 1.6f, rockOk: true))
                        {
                            cliffPlaces.Add(new CliffPlace { pos = lp, yaw = RHash(k, 4, 243) * 360f, scale = Mathf.Lerp(0.7f, 1.1f, heap), v = lv, r = 1.6f, lift = 0.3f });
                            talusN++;
                        }
                    }
                }

                // ROCK BY THE CUT'S HEIGHT (the Parkway's own survey photographs,
                // HAER, Wikimedia Commons; owner 2026-11-08: "review real photos
                // of these popular roads"): a TALL cut is a near-vertical
                // fractured rock face from the shoulder up, trees and shrubs on
                // its ledges and crowding its irregular top; a LOW cut is a
                // vegetated bank with rock breaking through here and there.
                // rockiness 0 at 1.5 m of face, 1 from 6 m.
                float rocky = Mathf.InverseLerp(1.5f, 6f, h);
                if (!rock || !CliffsOn || h < CliffMinFaceM || distAt[k] < next || r3 > Mathf.Lerp(0.45f, 1.01f, rocky)) continue;
                var ci = kit.cliffs[(int)(r1 * 9973f) % kit.cliffs.Count];
                float ht = kit.variants[ci.v].height;
                // tall: about the face's height (its top uneven); low: a part of it
                // (to 1.3 x the face on a tall one: blocks standing over the
                // crest break the cut's skyline, as in the photographs)
                float sc = Mathf.Clamp(h * Mathf.Lerp(Mathf.Lerp(0.45f, 0.85f, rocky), Mathf.Lerp(0.8f, 1.3f, rocky), r2) / ht, 0.35f, 2.6f);
                // sunk well in on a low bank (only its bulges break the soil), barely on a tall face
                Vector3 pos = toe + outw * (ci.front * sc + Mathf.Lerp(0.4f, 0.08f, rocky));
                pos.y = toe.y - 0.4f + r1 * Mathf.Max(0f, h - ht * sc) * Mathf.Lerp(0.6f, 0.15f, rocky);
                float want = Mathf.Atan2(-outw.x, -outw.z) * Mathf.Rad2Deg;
                cliffPlaces.Add(new CliffPlace { pos = pos, yaw = want - ci.frontAngle + (r2 - 0.5f) * Mathf.Lerp(60f, 26f, rocky), scale = sc, v = ci.v });
                cliffsPlaced++;
                // the top of the rock: plants rooted in it, a young tree now and then
                AddLedge(new Vector3(pos.x, pos.y + ht * sc - 0.2f, pos.z) - outw * 0.2f, rocky > 0.5f && r1 < 0.4f ? 1 : 2);
                // gaps: wide on a low bank, the blocks overlapping on a tall face
                next = distAt[k] + ci.length * sc * Mathf.Lerp(Mathf.Lerp(1.8f, 3.2f, r1), Mathf.Lerp(0.45f, 0.65f, r1), rocky);
            }
        }

        /// <summary>On the TOP of a real rock face (a faced cut 1.5 m or more,
        /// past its crest): out of the clear zone in fact, since nothing drives
        /// up a rock face. The forest may come to the edge there - in the
        /// Parkway's photographs trees and shrubs crowd the top of every cut,
        /// where the clear-zone rule left a bare band (owner 2026-11-08).</summary>
        static bool OnRockTop(in RoadFoot f)
        {
            if (!PCContent || rsKind == null || rsCutFaced == null || rsFaceH == null) return false;
            int s = SideIx(f.side);
            int st = WrapIdx(Mathf.RoundToInt(f.station), stageWp.Count);
            if (rsKind[s][st] != Roadside.Cut) return false;
            rockTopAsk[0]++;
            if (!rsCutFaced[s][st]) return false;
            rockTopAsk[1]++;
            if (rsFaceH[s][st] < 1.5f) return false;
            rockTopAsk[2]++;
            if (rsCutGraded != null && rsCutGraded[s][st]) return false;
            rockTopAsk[3]++;
            bool past = f.d - RoadWidth * 0.5f >= CutCrestE(s, st) + 0.15f;
            if (past) rockTopAsk[4]++;
            return past;
        }

        static readonly int[] rockTopAsk = new int[5];

        static void AddLedge(Vector3 p, int kind) { rockLedges.Add(p); rockLedgeKind.Add(kind); }

        /// <summary>The forest pack's stones again as BOULDERS and the cuts'
        /// TALUS: their own variants, drawn to the near forest's whole range
        /// (the litter stones thin out from 100 m - a boulder that shrank away
        /// there would be pop-in), shadows on.</summary>
        static readonly List<int> boulderVars = new List<int>();

        static void LoadBoulders(List<MeshForest.Variant> list)
        {
            boulderVars.Clear();
            boulderLying.Clear();
            const string dir = "Assets/StoreAssets/NMForest/Rocks";
            var mat = NMLitMat("NM_boulder", dir, "T_beech_forest_stones_01_BC_SM", "T_beech_forest_stones_01_N", null, false,
                               new Color(0.86f, 0.86f, 0.84f), maskIsHdrp: false);
            for (int k = 1; k <= 10; k++)
            {
                string fbx = "beech_forest_stones_01_" + k;
                var v = LoadVariant(NMDebrisDir, fbx, mat, ".fbx") ?? LoadVariant(NMDebrisDir, fbx, mat, ".FBX")
                        ?? LoadVariant(dir, fbx, mat, ".fbx");
                if (v == null) continue;
                v.shadows = true;
                v.maxDist = 0f;
                v.thin = 0f;
                // lying or standing: the model's height against its widest
                // level extent (after its node transform)
                var bb = v.lods[0].bounds;
                Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
                for (int cc = 0; cc < 8; cc++)
                {
                    var q = v.pre.MultiplyPoint3x4(new Vector3((cc & 1) == 0 ? bb.min.x : bb.max.x,
                        (cc & 2) == 0 ? bb.min.y : bb.max.y, (cc & 4) == 0 ? bb.min.z : bb.max.z));
                    lo = Vector3.Min(lo, q); hi = Vector3.Max(hi, q);
                }
                float wMax = Mathf.Max(hi.x - lo.x, hi.z - lo.z);
                if ((hi.y - lo.y) <= 0.8f * wMax) boulderLying.Add(list.Count);
                boulderVars.Add(list.Count); list.Add(v);
            }
            Log($"Boulders: {boulderVars.Count} forest stone meshes for boulders and talus, {boulderLying.Count} of them lying (the boulders' pool).");
        }

        /// <summary>The cliffs standing in one near chunk, into MeshForest's table.</summary>
        static void AddCliffs(float ox, float oz, float size, List<Vector4> inst, List<Vector2> scaleVar,
                              ref int added, ref float ySum)
        {
            foreach (var c in cliffPlaces)
            {
                if (c.pos.x < ox || c.pos.x >= ox + size || c.pos.z < oz || c.pos.z >= oz + size) continue;
                // A boulder, a talus stone or a litter patch, tested AGAIN now
                // that the tunnels are built: at the banks the portal walls did
                // not exist yet (Little Switzerland's foot litter grew through
                // one, station 1045). One that meets a wall, rail or portal goes,
                // and its solid with it.
                if (c.r > 0f)
                {
                    if (!rockSynced) { Physics.SyncTransforms(); rockSynced = true; }
                    if (!RockClearOfSolids(c.pos + Vector3.up * c.lift, c.r, rockOk: true))
                    {
                        if (c.solid != null) Object.DestroyImmediate(c.solid);
                        rockDropped++;
                        Log($"Rock: dropped a {(c.solid != null ? "boulder" : "talus/litter")} by a wall or portal at ({c.pos.x:0.0}, {c.pos.y:0.0}, {c.pos.z:0.0}).");
                        continue;
                    }
                }
                inst.Add(new Vector4(c.pos.x, c.pos.y, c.pos.z, c.yaw));
                scaleVar.Add(new Vector2(c.scale, c.v));
                ySum += c.pos.y;
                added++;
            }
        }
    }
}
