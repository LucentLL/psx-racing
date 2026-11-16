using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// TREES ROUND 5 (2026-11-16): THE OWNER'S FAR-CROWN METHOD ON THE
    /// NATUREMANUFACTURE TREES. Codex's crown patches (CT4AddCard,
    /// CT4CardSampling, PSX_CROWN_CARD on PSX/Lit) give each Charlotte form a
    /// far card made from its own outgoing LOD2: four projections on one
    /// camera-facing plane over eight bearings, the alpha cut per bearing, a
    /// filtered far tint - so the hand-off at 220-240 m (MEDIUM) does not pop.
    /// Round 3's trees (what PC46 ships) still wear the packs' sheets on two
    /// crossed quads (or Big Oak's keyed billboard): a different picture from
    /// their LOD2, and the owner has reported tree pop-in since 11-11.
    ///
    /// PSX_CT5_NMCROWN=1 (kit build, DEFAULT OFF): every round 1-3 variant that
    /// has a far card gets Codex's card instead - rendered from its own LOD2,
    /// one sheet a dress from that dress's own leaf material (its tint, vivid,
    /// fall target and spread copied onto the card), bark only for a dress
    /// with no leaves. The fit, the meshes and the planting are untouched.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        /// <summary>FLOATING TREES (round 5b; the owner: "Some even seem to be floating in air"): a tree stands
        /// on the foot LoadVariant finds in its LOD0 (the bark's 2nd-percentile height, past the root tips), and
        /// every level is drawn on that foot. The packs' coarser levels have no roots: their trunk starts higher -
        /// Meadow poplar 03 (the pine stand-in) 0.48 m, so at x1.5 it hangs 0.43 m over the ground beyond 102 m
        /// (MEDIUM). Measured along Providence Rd: 1,467 of 7,463 planted trees float at LOD1, 1,890 at LOD2.
        /// PSX_CT5_FOOT=1 (kit build, default OFF): each level's trunk is drawn down to LOD0's trunk base - the
        /// bark in its lowest 0.8 m stretched, nothing above it moved (so no new pop at the switch).</summary>
        static void CT5Feet(List<MeshForest.Variant> list, List<string> names, List<byte> groups)
        {
            if (System.Environment.GetEnvironmentVariable("PSX_CT5_FOOT") != "1") return;
            int fixedLods = 0, trees = 0; float worst = 0f; string worstName = "";
            var done = new Dictionary<(Mesh, int), Mesh>();
            float Base(MeshForest.Variant v, Mesh m)
            {
                float mn = float.MaxValue;
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    if (v.leafSub != null && s < v.leafSub.Length && v.leafSub[s] && m.subMeshCount > 1) continue;
                    if (!TreeKit.Read(m, s, out var vv, out var tt)) continue;
                    foreach (int i in tt) mn = Mathf.Min(mn, v.pre.MultiplyPoint3x4(vv[i]).y);
                }
                return mn;
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (i < groups.Count && groups[i] == CityTreeKit.G4Charlotte) continue;
                var v = list[i];
                if (v == null || v.lods == null || v.lods[0] == null) continue;
                float b0 = Base(v, v.lods[0]);
                if (b0 == float.MaxValue) continue;
                bool any = false;
                for (int l = 1; l < 3 && l < v.lods.Length; l++)
                {
                    var m = v.lods[l];
                    if (m == null || m == v.lods[0]) continue;
                    float bl = Base(v, m);
                    float lift = bl - b0;
                    if (bl == float.MaxValue || lift <= 0.05f) continue;
                    int key = Mathf.RoundToInt(b0 * 1000f);
                    if (!done.TryGetValue((m, key), out var fixedM))
                    {
                        fixedM = Object.Instantiate(ReadableCopy(m));
                        fixedM.name = "CT5Foot_" + names[i] + "_L" + l;
                        var verts = fixedM.vertices;
                        var inv = v.pre.inverse;
                        var moved = new bool[verts.Length];
                        for (int s = 0; s < fixedM.subMeshCount; s++)
                        {
                            if (v.leafSub != null && s < v.leafSub.Length && v.leafSub[s] && fixedM.subMeshCount > 1) continue;
                            foreach (int vi in fixedM.GetTriangles(s))
                            {
                                if (moved[vi]) continue;
                                moved[vi] = true;
                                var q = v.pre.MultiplyPoint3x4(verts[vi]);
                                float t = (q.y - bl) / 0.8f;
                                if (t >= 1f) continue;
                                q.y -= lift * (1f - Mathf.Clamp01(t));
                                verts[vi] = inv.MultiplyPoint3x4(q);
                            }
                        }
                        fixedM.vertices = verts;
                        fixedM.RecalculateBounds();
                        fixedM = WorldKit.SaveMesh(fixedM, fixedM.name);
                        done[(m, key)] = fixedM;
                        fixedLods++;
                    }
                    v.lods[l] = fixedM;
                    any = true;
                    if (lift > worst) { worst = lift; worstName = names[i] + " LOD" + l; }
                }
                if (any) trees++;
            }
            Log($"City trees round 5b - FEET (PSX_CT5_FOOT=1): {trees} variants, {fixedLods} levels drawn down to their LOD0's trunk base (worst lift {worst:0.00} m at scale 1: {worstName})");
        }

        static void CT5NMCrowns(List<MeshForest.Variant> list, List<string> names, List<byte> groups)
        {
            if (System.Environment.GetEnvironmentVariable("PSX_CT5_NMCROWN") != "1") return;
            if (!AssetDatabase.IsValidFolder(CT4Gen))
            {
                if (!AssetDatabase.IsValidFolder(CT4Dir)) AssetDatabase.CreateFolder("Assets", "CharlotteTrees");
                AssetDatabase.CreateFolder(CT4Dir, "Gen");
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int cards = 0, sheets = 0;
            var sb = new System.Text.StringBuilder("City trees round 5 - THE NATUREMANUFACTURE TREES' FAR CROWNS FROM THEIR OWN LOD2 (PSX_CT5_NMCROWN=1):");
            for (int i = 0; i < list.Count; i++)
            {
                if (i < groups.Count && groups[i] == CityTreeKit.G4Charlotte) continue;
                var v = list[i];
                if (v == null || v.lods == null || v.lods.Length < 4 || v.lods[3] == null || v.lods[2] == null) continue;
                string bark = null;
                for (int s = 0; s < v.mats.Length; s++)
                    if (!(v.leafSub != null && s < v.leafSub.Length && v.leafSub[s]) && v.mats[s] != null && v.mats[s].mainTexture != null)
                    { bark = AssetDatabase.GetAssetPath(v.mats[s].mainTexture); break; }
                var geo = CT4Geometry(v);
                string tag = "NM5_" + names[i];
                var made = new Dictionary<Material, Material>();
                Material bareCard = null;
                var dress = new Material[5];
                for (int d = 0; d < 5; d++)
                {
                    var leaf = v.leafDress != null && d < v.leafDress.Length ? v.leafDress[d] : null;
                    if (leaf != null && made.TryGetValue(leaf, out var have)) { dress[d] = have; continue; }
                    if (leaf == null && bareCard != null) { dress[d] = bareCard; continue; }
                    // ("none": no such texture - its triangles are left out of the sheet)
                    string leafTex = leaf != null && leaf.mainTexture != null ? AssetDatabase.GetAssetPath(leaf.mainTexture) : "none";
                    var lv = leaf != null && leaf.HasProperty("_LeafVivid") ? leaf.GetVector("_LeafVivid") : new Vector4(1f, 1f, 1f, 0f);
                    var vivid = leaf != null ? new Vector2(Mathf.Max(0.05f, lv.x), Mathf.Max(0.05f, lv.y)) : Vector2.one;
                    string key = leaf != null ? "_" + leaf.name : "_bark";
                    string sheet = CT4Sheet(geo, tag + key, leafTex, bark ?? "none", 0f, vivid);
                    sheets++;
                    var m = CTMat("CT5_imp_" + names[i] + key, sheet, null, true, Color.white);
                    CT4CardSampling(m, sheet, vivid);
                    var gain = m.color;
                    if (leaf != null)
                    {
                        // the dress's own colour on the card: its tint (the packs' fall tints), its vivid step, fall target and spread
                        var t = leaf.color;
                        m.color = new Color(gain.r * t.r, gain.g * t.g, gain.b * t.b, 1f);
                        m.SetVector("_LeafVivid", lv);
                        if (leaf.HasProperty("_LeafTarget")) m.SetColor("_LeafTarget", leaf.GetColor("_LeafTarget"));
                        if (leaf.HasProperty("_LeafTulip")) m.SetColor("_LeafTulip", leaf.GetColor("_LeafTulip"));
                        made[leaf] = m;
                    }
                    else
                    {
                        // no leaves in this dress: the limbs alone, drawn as bark (no leaf step on them)
                        m.SetVector("_LeafVivid", new Vector4(1f, 1f, 1f, 0f));
                        bareCard = m;
                    }
                    EditorUtility.SetDirty(m);
                    dress[d] = m;
                }
                CT4AddCard(v, "CTImpostor5_" + names[i], dress, geo);
                cards++;
            }
            sb.Append($"\n  {cards} variants given a far crown from their own LOD2, {sheets} sheets of {4 * CT4Cell} x {CT4Cell} px ({sheets * 4L * CT4Cell * CT4Cell * 4 / 3 / 1048576.0:0.0} MB DXT5 + mips), {clock.Elapsed.TotalSeconds:0.0} s");
            Log(sb.ToString());
        }
    }
}
