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
        const bool CliffsOn = false;
        const float CliffMinFaceM = 1.2f;
        /// <summary>How far the drawn smooth face steps back behind the cliffs (m).</summary>
        const float CliffBackM = 1.0f;

        struct CliffInfo { public int v; public float frontAngle, front, length; }
        struct CliffPlace { public Vector3 pos; public float yaw, scale; public int v; }
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
            return infos;
        }

        /// <summary>Cliff meshes along one bank's faced stations.</summary>
        static void PlaceCliffs(List<Vector3> pts, List<int> st, List<float> hh, bool[] faced, Vector3[] outwAt,
                                float[] distAt, int R, int seed, Vector3[] crest = null)
        {
            var kit = LoadMeshTreeKit();
            if (kit == null || kit.cliffs == null || kit.cliffs.Count == 0) return;
            float next = float.MinValue;
            for (int k = 0; k < R; k++)
            {
                if (!faced[k] || hh[k] < CliffMinFaceM || distAt[k] < next) continue;
                float h = hh[k];
                float r1 = RHash(k, seed, 201), r2 = RHash(seed, k, 203), r3 = RHash(k + 7, seed, 207);
                // a cliff of about the face's height: its own size scaled 0.6-2.4
                int pick = -1, tries = 0;
                for (int t = 0; t < kit.cliffs.Count && pick < 0; t++)
                {
                    var c = kit.cliffs[(int)((r1 * 9973f + t * 3f) % kit.cliffs.Count)];
                    float ht = kit.variants[c.v].height;
                    if (h / ht >= 0.6f && h / ht <= 2.4f) pick = (int)((r1 * 9973f + t * 3f) % kit.cliffs.Count);
                    tries++;
                }
                if (pick < 0)
                {
                    float best = float.MaxValue;
                    for (int t = 0; t < kit.cliffs.Count; t++)
                    {
                        float dd = Mathf.Abs(Mathf.Log(h / kit.variants[kit.cliffs[t].v].height));
                        if (dd < best) { best = dd; pick = t; }
                    }
                }
                var ci = kit.cliffs[pick];
                float s = Mathf.Clamp(h * Mathf.Lerp(1.0f, 1.2f, r2) / kit.variants[ci.v].height, 0.5f, 2.6f);
                int i = st[k];
                Vector3 outw = outwAt[k];
                Vector3 toe = pts[i] + outw * (RoadWidth * 0.5f + CutToeE);
                toe.y = pts[i].y - 0.35f;
                Vector3 pos = toe + outw * (ci.front * s - 0.15f);
                float want = Mathf.Atan2(-outw.x, -outw.z) * Mathf.Rad2Deg;
                float yaw = want - ci.frontAngle + (r3 - 0.5f) * 20f;
                cliffPlaces.Add(new CliffPlace { pos = pos, yaw = yaw, scale = s, v = ci.v });
                cliffsPlaced++;
                float topY = toe.y + kit.variants[ci.v].height * s;
                // A TALL face: a second tier above, set back up the batter, so
                // no smooth backing shows over a low row of rock.
                if (h > (topY - pts[i].y) + 1.2f)
                {
                    var c2 = kit.cliffs[(pick + 3) % kit.cliffs.Count];
                    float s2 = Mathf.Clamp((h - (topY - pts[i].y) + 1.0f) / kit.variants[c2.v].height, 0.5f, 2.4f);
                    Vector3 p2 = pos + outw * ((topY - pts[i].y) * BankBatter * 0.8f + (c2.front * s2 - ci.front * s));
                    p2.y = topY - 0.6f;
                    cliffPlaces.Add(new CliffPlace { pos = p2, yaw = want - c2.frontAngle + (r1 - 0.5f) * 24f, scale = s2, v = c2.v });
                    cliffsPlaced++;
                    topY = p2.y + kit.variants[c2.v].height * s2;
                }
                // FOLIAGE OVER THE ROCK (owner: cuts are "often covered in a lot
                // of foliage"): plants on the cliff's top and at its foot
                // (AddLedgePlants: grass and fern, cherry and maple, young beech).
                Vector3 side = new Vector3(outw.z, 0f, -outw.x);
                for (int q = 0; q < 3; q++)
                {
                    float a = (RHash(k * 3 + q, seed, 211) - 0.5f) * ci.length * s * 0.8f;
                    rockLedges.Add(new Vector3(pos.x, topY - 0.25f, pos.z) + side * a + outw * (0.3f + 0.4f * q));
                }
                rockLedges.Add(toe - outw * 0.2f + side * ((r2 - 0.5f) * 3f) + Vector3.up * 0.3f);
                next = distAt[k] + ci.length * s * Mathf.Lerp(0.38f, 0.52f, r3);
            }
        }

        /// <summary>The cliffs standing in one near chunk, into MeshForest's table.</summary>
        static void AddCliffs(float ox, float oz, float size, List<Vector4> inst, List<Vector2> scaleVar,
                              ref int added, ref float ySum)
        {
            foreach (var c in cliffPlaces)
            {
                if (c.pos.x < ox || c.pos.x >= ox + size || c.pos.z < oz || c.pos.z >= oz + size) continue;
                inst.Add(new Vector4(c.pos.x, c.pos.y, c.pos.z, c.yaw));
                scaleVar.Add(new Vector2(c.scale, c.v));
                ySum += c.pos.y;
                added++;
            }
        }
    }
}
