using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// MOUNTAIN CONIFERS AS MODELS (agent "conifers", 2026-11-27). The owner, from the driver's seat at dusk: "I'm
    /// still seeing x-shaped tree cards on PC. Am I missing 3D models for tree types?" No owned pack has a conifer,
    /// so every stage conifer was two crossed cards. Codex delivered four species (eastern hemlock, eastern white
    /// pine, red spruce, Fraser fir: young, mature and edge forms, plus a hemlock and a Fraser fir snag) to the
    /// art workspace's Mountain_Conifers; tools/mountain-conifers.ps1 copies them to the gitignored
    /// Assets/MountainConifers.
    ///
    /// THE SWITCH (OFF by default; a scene build): PSX_CONIFER_MODELS = a comma list of the species to draw as
    /// models - hemlock, pine, spruce, fir, snags - or "all". Unset, empty or "0": every conifer stays crossed cards
    /// exactly as before (the kit is not even loaded). A species ships only once it matches the NM beech beside it
    /// in the game (the owner's rule "looks before species"); the LAB row "conifers" says what the build has.
    ///
    /// Each conifer site keeps the stream, the trunk, the collider and the card it always had (the card goes to
    /// the chunk's B mesh that MeshForest hides where it draws meshes, as the broadleaf trees' do); the model is
    /// picked by real elevation and the card's own species cell:
    ///   above 1,500 m: the fir cell -> Fraser fir, the two spruce cells -> red spruce;
    ///   1,350-1,500 m: red spruce, the dark spruce cell -> hemlock;
    ///   below: the spruce and fir cells -> hemlock, the dark spruce cell -> white pine;
    ///   a few sites (by hash) a standing snag: Fraser fir above 1,500 m, hemlock below.
    /// The form by the wanted height and the road: within EdgeFormM of the road the edge form, turned so its open
    /// side faces the road; a short (understory) tree the young form; otherwise the mature form.
    /// No understory, no extra canopy beech round a conifer site: the stand stays what the cards drew.
    /// The far card is the tree's own picture (tree match, everyTree), as the beeches'.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        const string ConiferDir = "Assets/MountainConifers";
        /// <summary>Within this many metres of the road's centre a conifer stands as its edge form.</summary>
        const float EdgeFormM = 28f;
        /// <summary>The share of conifer sites that are standing snags.</summary>
        const float ConiferSnagShare = 0.06f;

        static readonly string[] ConiferSpecies = { "EasternHemlock", "EasternWhitePine", "RedSpruce", "FraserFir" };
        static readonly string[] ConiferKeys = { "hemlock", "pine", "spruce", "fir" };
        static readonly string[] ConiferForms = { "Young", "Mature", "Edge" };

        /// <summary>The switch as the builder reads it: which species (0..3) and the snags.</summary>
        internal static bool[] ConiferModelsWanted(out bool snags)
        {
            snags = false;
            var want = new bool[4];
            string env = (System.Environment.GetEnvironmentVariable("PSX_CONIFER_MODELS") ?? "").Trim().ToLowerInvariant();
            if (env.Length == 0 || env == "0" || env == "off") return want;
            foreach (var part in env.Split(','))
            {
                string p = part.Trim();
                if (p == "all" || p == "1" || p == "on") { for (int i = 0; i < 4; i++) want[i] = true; snags = true; continue; }
                if (p == "snags" || p == "snag") { snags = true; continue; }
                for (int i = 0; i < 4; i++) if (p == ConiferKeys[i]) want[i] = true;
            }
            return want;
        }

        /// <summary>The conifer variants into the kit's list (after every other plant: no existing index moves).
        /// Returns the new variants for the tree-match pass, or null when the switch is off or the files are missing.</summary>
        static List<MeshForest.Variant> AddConiferVariants(MeshTreeKit kit, List<MeshForest.Variant> list)
        {
            kit.conifer = new int[4][];
            kit.coniferSnag = new int[2] { -1, -1 };
            var want = ConiferModelsWanted(out bool snags);
            bool any = snags; foreach (var w in want) any |= w;
            if (!any) return null;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ConiferDir + "/Models/EasternHemlock_Mature.fbx") == null)
            {
                Log("WARN: PSX_CONIFER_MODELS is set but " + ConiferDir + " has no models (tools/mountain-conifers.ps1) - conifers stay cards");
                return null;
            }
            var added = new List<MeshForest.Variant>();
            var names = new List<string>();
            // the GPU cull's chunk masks hold 128 species
            int room = 128 - list.Count;
            MeshForest.Variant Load(string sp, string form, string barkStem, string leafStem, string leafMatName)
            {
                if (room <= 0) { Log("WARN: conifers: no room for " + sp + "_" + form + " (128 species)"); return null; }
                var bark = NMLitMat("MC_" + barkStem, ConiferDir, "Textures/" + barkStem + "_overhaul1_hd", "Textures/" + barkStem + "_overhaul1_hd_normal", null, false, Color.white);
                var needles = NMLitMat("MC_" + leafMatName, ConiferDir, "Textures/" + leafStem + "_overhaul1_hd", "Textures/" + leafStem + "_overhaul1_hd_normal", null, true, Color.white);
                var v = LoadVariant(ConiferDir + "/Models", sp + "_" + form, bark, ".fbx");
                if (v == null) return null;
                // THE FOOT IS THE ORIGIN (the delivery's contract: root flare at 0, the trunk on 0.5 m below for
                // slopes). LoadVariant stands a tree on its bark's 2nd percentile - here the buried end - which
                // would lift the flare half a metre: stand it on the origin instead.
                float y0 = v.pre.MultiplyPoint3x4(Vector3.zero).y;
                v.pre = Matrix4x4.Translate(new Vector3(0f, -y0, 0f)) * v.pre;
                v.height = Mathf.Max(0.3f, v.height - y0);
                // EVERGREEN: the same needles in every dress - no fall recolour, no bare winter
                v.leafDress = new[] { needles, needles, needles, needles, needles };
                v.conifer = true;
                room--;
                added.Add(v); names.Add(sp + "_" + form);
                Log($"Conifers: {sp}_{form} {v.height:0.0} m stood on its origin (lifted {-y0:+0.00;-0.00} m from the bark's low end)");
                return v;
            }
            for (int s = 0; s < 4; s++)
            {
                if (!want[s]) continue;
                var forms = new int[3];
                for (int f = 0; f < 3; f++)
                {
                    string sp = ConiferSpecies[s];
                    var v = Load(sp, ConiferForms[f], sp + "_Bark", sp + "_Needles", sp + "_Needles");
                    forms[f] = v != null ? list.Count : -1;
                    if (v != null) list.Add(v);
                }
                kit.conifer[s] = forms;
            }
            if (snags)
            {
                var hs = Load("EasternHemlock", "Snag", "EasternHemlockSnag_Bark", "Dead_Twigs", "Dead_Twigs");
                if (hs != null) { kit.coniferSnag[0] = list.Count; list.Add(hs); }
                var fs = Load("FraserFir", "Snag", "FraserFirSnag_Bark", "Dead_Twigs", "Dead_Twigs");
                if (fs != null) { kit.coniferSnag[1] = list.Count; list.Add(fs); }
            }
            Log($"Conifers as models (PSX_CONIFER_MODELS={System.Environment.GetEnvironmentVariable("PSX_CONIFER_MODELS")}): {added.Count} forms " +
                $"({string.Join(", ", names)}), {list.Count} species in the forest.");
            coniferNames = names;
            return added.Count > 0 ? added : null;
        }

        static List<string> coniferNames;

        /// <summary>The far cards of the conifer models: each its own LOW mesh, every dress (the beeches' tree match).</summary>
        static void ConiferMatchCards(List<MeshForest.Variant> added)
        {
            if (added == null || added.Count == 0) return;
            int made = TreeMatchKit(added, coniferNames, "stageconifer", added[0].leafDress, true);
            Log($"Conifers: {made} far cards made from their own meshes.");
        }

        /// <summary>The model for a conifer site, or -1 (stays a card). <paramref name="cell"/> is the StageTrees
        /// index (12 spruce, 13 dark spruce, 14 fir); <paramref name="elevM"/> the real elevation; <paramref name="want"/>
        /// the wanted height in the mesh trees' scale.</summary>
        static int PickConifer(MeshTreeKit kit, int cell, float elevM, float want, float roadD, bool young, int hsh, out float scale, out bool edge)
        {
            scale = 1f; edge = false;
            if (kit == null || kit.conifer == null) return -1;
            bool high = elevM >= 1500f, mid = elevM >= 1350f;
            // a standing snag, now and then
            if (kit.coniferSnag != null && ((hsh >> 3) % 1000) < ConiferSnagShare * 1000f && !young)
            {
                int sv = kit.coniferSnag[high ? 1 : 0];
                if (sv >= 0) { scale = Mathf.Clamp(want / kit.variants[sv].height, 0.75f, 1.25f); return sv; }
            }
            int s = high ? (cell == 14 ? 3 : 2)
                  : mid ? (cell == 13 ? 0 : 2)
                  : (cell == 13 ? 1 : 0);
            var forms = kit.conifer[s];
            if (forms == null) return -1;
            int f = young ? 0 : roadD <= EdgeFormM ? 2 : 1;
            int vi = forms[f];
            if (vi < 0) vi = forms[1] >= 0 ? forms[1] : forms[0];
            if (vi < 0) return -1;
            edge = f == 2 && vi == forms[2];
            // the species' real size wins over the card's game height: a model scaled by no more than 0.7-1.25
            scale = Mathf.Clamp(want / kit.variants[vi].height, 0.7f, 1.25f);
            return vi;
        }

        /// <summary>The yaw that turns an edge form's open side (its local -X) toward the road point.</summary>
        static float EdgeYawToward(float x, float z, Vector3 roadPt, float fallback)
        {
            float dx = roadPt.x - x, dz = roadPt.z - z;
            if (dx * dx + dz * dz < 1e-4f) return fallback;
            return Mathf.Atan2(dz, -dx) * Mathf.Rad2Deg;
        }
    }
}
