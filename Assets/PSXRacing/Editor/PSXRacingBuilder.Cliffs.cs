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
                toe.y = pts[i].y;
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
                float sc = Mathf.Clamp(h * Mathf.Lerp(Mathf.Lerp(0.45f, 0.85f, rocky), Mathf.Lerp(0.8f, 1.15f, rocky), r2) / ht, 0.35f, 2.6f);
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

        static void AddLedge(Vector3 p, int kind) { rockLedges.Add(p); rockLedgeKind.Add(kind); }

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
