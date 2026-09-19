using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// EVERY TREE IN THE GAME: IS IT AN X FROM EVERY SIDE, AND DOES ITS TRUNK
    /// STOP A CAR?
    ///
    /// Owner, 2026-09-19: "All trees need the x-pattern to simulate 3D. Some
    /// trees are still 2 dimensions and flat. Trees should also occupy space
    /// with their trunks, stopping a car if it drives into it."
    ///
    /// The builders already emitted crossed quads — and drew each plane ONE
    /// side only, under PSX/Lit's Cull Back. A crossed pair drawn that way is an
    /// X from one quarter of the compass, a single flat card from two more, and
    /// nothing at all from the last: the comment beside the code said each plane
    /// "is still visible from both sides via the other plane of the cross",
    /// which is true of neither plane from behind both. Nothing looked at a tree
    /// from more than one side, so nothing said so. The first run of this audit
    /// found all 61,450 stage trees and all 153 circuit trees that way, no
    /// trunk under any tree in the game, and a MeshCollider across the whole
    /// crown of every pack tree on every forecourt.
    ///
    /// So this opens every built scene and asks, of every renderer wearing a
    /// tree or shrub texture:
    ///
    ///   * X — is every tree at least two crossing cards?
    ///   * SIDES — is every flat card drawn from both sides? Either the
    ///     material culls nothing (<c>_Cull</c> 0) or the card carries a
    ///     mirrored twin (the art packs do it that way). A closed shape — a
    ///     modelled trunk — is one-sided correctly and is not counted.
    ///   * TRUNK — for a tree, is there a collider standing where its trunk is?
    ///     A tree that is its own object carries a capsule; a merged forest
    ///     chunk is covered by the scene's <see cref="TreeTrunks"/> table,
    ///     checked tree for tree against the chunk's own vertices.
    ///   * NO CARD COLLIDER — a MeshCollider or box on a tree's cards is solid
    ///     across the whole transparent crown.
    ///   * ONE SPECIES — both cards of a forest tree cut from the same atlas
    ///     cell, and neither reaching into the next one.
    ///
    /// Shrubs are listed for the record and graded on sides only; nobody asked
    /// for a hedge to stop a car.
    ///
    /// Menu: PSX Racing/Audit Foliage. Batch: PSXRacing.EditorTools.FoliageAudit.Run,
    /// writes PSXRacing_foliage_audit.txt and ends it with FOLIAGE OK or the
    /// count of problems.
    /// </summary>
    public static class FoliageAudit
    {
        [MenuItem("PSX Racing/Audit Foliage")]
        public static void Run()
        {
            var log = new StringBuilder();
            int problems = AuditScenes(PSXRacingBuilder.SceneOrder(), log);
            log.AppendLine(problems == 0 ? "FOLIAGE OK" : "FOLIAGE PROBLEMS: " + problems);
            Debug.Log(log.ToString());
            File.WriteAllText(Path.Combine(Application.dataPath, "../PSXRacing_foliage_audit.txt"), log.ToString());
        }

        /// <summary>Open each scene (single) and audit it. A scene that is not
        /// built is a problem, not a skip.</summary>
        public static int AuditScenes(IEnumerable<string> paths, StringBuilder log)
        {
            int problems = 0;
            foreach (var path in paths)
            {
                if (!File.Exists(path)) { log.AppendLine("MISSING " + path); problems++; continue; }
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                Physics.SyncTransforms();
                problems += AuditScene(Path.GetFileNameWithoutExtension(path), log);
            }
            return problems;
        }

        class Row
        {
            public string what;
            public bool tree;
            public int count, tris, oneSidedTris, plants, flat, trunked, cardColliders, mixed;
        }

        /// <summary>
        /// Trees in a merged forest chunk whose two cards are not the SAME
        /// species: the second quad's UV rectangle differs from the first's,
        /// or either spills out of its own 128 px atlas cell into a neighbour.
        ///
        /// "I just wanted to make sure trees weren't being cross contaminated."
        /// A forest tree is two quads cut from one 4x4 atlas, so this is the
        /// one place two species COULD meet on one tree — and one sheet in the
        /// pack (tree022, red one side, green the other) looks exactly like
        /// that when it has not happened. Point-filtered, unmipped, and each
        /// rectangle held 1.5 px inside its cell, so a rectangle that stays in
        /// its cell cannot pick up a neighbour's pixels at any distance.
        /// </summary>
        static int MixedSpecies(Mesh mesh)
        {
            if (!mesh.isReadable) return 0;
            var uv = mesh.uv;
            int bad = 0;
            for (int t = 0; t + TreeKit.VertsPerTree <= uv.Length; t += TreeKit.VertsPerTree)
            {
                // Corners 0 and 2 of each quad span its rectangle.
                Vector2 a0 = uv[t], a1 = uv[t + 2], b0 = uv[t + 4], b1 = uv[t + 6];
                bool same = (a0 - b0).sqrMagnitude < 1e-6f && (a1 - b1).sqrMagnitude < 1e-6f;
                bool oneCell = Mathf.FloorToInt(a0.x * 4f) == Mathf.FloorToInt(a1.x * 4f) &&
                               Mathf.FloorToInt(a0.y * 4f) == Mathf.FloorToInt(a1.y * 4f);
                if (!same || !oneCell) bad++;
            }
            return bad;
        }

        /// <summary>Audit whatever scene is open. Returns the problem count;
        /// the self-test calls this on the scenes it already has open.</summary>
        public static int AuditScene(string scene, StringBuilder log)
        {
            int problems = 0, unreadable = 0;
            var rows = new Dictionary<string, Row>();
            var tables = Object.FindObjectsByType<TreeTrunks>(FindObjectsSortMode.None);

            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh;
                var mats = r.sharedMaterials;
                for (int s = 0; s < mats.Length && s < mesh.subMeshCount; s++)
                {
                    var kind = TreeKit.KindOf(mats[s]);
                    if (kind == TreeKit.Kind.None) continue;
                    var m = mats[s];
                    bool cullOff = m.HasProperty("_Cull") && m.GetFloat("_Cull") < 0.5f;
                    bool isTree = kind == TreeKit.Kind.Tree;

                    bool readable = TreeKit.Read(mesh, s, out var verts, out var tri);
                    if (!readable) { unreadable++; continue; }

                    string key = (isTree ? "TREE  " : "SHRUB ") + Group(r.transform) +
                                 "  [" + m.name + (cullOff ? ", both faces" : "") + "]";
                    if (!rows.TryGetValue(key, out var row)) rows[key] = row = new Row { what = key, tree = isTree };
                    row.count++;
                    row.tris += tri.Length / 3;
                    if (!cullOff)
                    {
                        var toWorld = r.transform.localToWorldMatrix;
                        var world = new Vector3[verts.Length];
                        for (int i = 0; i < verts.Length; i++) world[i] = toWorld.MultiplyPoint3x4(verts[i]);
                        row.oneSidedTris += TreeKit.OneSidedCardTris(world, tri);
                    }

                    if (!isTree) continue;
                    foreach (var c in r.GetComponents<Collider>())
                        if (!c.isTrigger && (c is MeshCollider || c is BoxCollider)) row.cardColliders++;

                    foreach (var p in TreeKit.FindPlants(mesh, s, r.transform.localToWorldMatrix))
                    {
                        // What PlantTrunks leaves alone, this leaves alone: a
                        // pot plant is not a tree a car could hit.
                        if (p.height < 2f) continue;
                        row.plants++;
                        if (p.cards < 2) row.flat++;
                        if (TrunkAt(p, tables)) row.trunked++;
                    }
                    if (mesh.name.Contains("StageForest")) row.mixed += MixedSpecies(mesh);
                }
            }

            log.AppendLine("");
            log.AppendLine("foliage — " + scene + (tables.Length > 0
                ? "  (trunk table: " + TotalTrunks(tables) + " trunks)" : ""));

            // THE OTHER FOUR SEASONS. SeasonDress swaps the forest's material
            // for a variant at load, so a variant that culls its backs puts
            // the flat trees back for every month but October — and the
            // renderer pass above only ever sees the fall one.
            foreach (var dress in Object.FindObjectsByType<SeasonDress>(FindObjectsSortMode.None))
            {
                if (dress.entries == null) continue;
                foreach (var e in dress.entries)
                {
                    if (e == null || e.baseMat == null || e.variants == null) continue;
                    if (TreeKit.KindOf(e.baseMat) != TreeKit.Kind.Tree) continue;
                    bool baseOff = e.baseMat.HasProperty("_Cull") && e.baseMat.GetFloat("_Cull") < 0.5f;
                    int culled = 0;
                    foreach (var v in e.variants)
                        if (v != null && !(v.HasProperty("_Cull") && v.GetFloat("_Cull") < 0.5f)) culled++;
                    bool ok = !baseOff || culled == 0;
                    if (!ok) problems++;
                    log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + "SEASONS " + e.baseMat.name + "  " +
                                   (e.variants.Length - culled) + "/" + e.variants.Length +
                                   " dresses draw both faces");
                }
            }
            if (unreadable > 0)
            {
                // Measuring nothing and calling it clean is the failure this
                // audit exists to stop.
                log.AppendLine("  FAIL " + unreadable + " foliage submeshes could not be read");
                problems++;
            }
            if (rows.Count == 0) { log.AppendLine("  no trees or shrubs"); return problems; }
            var sorted = new List<Row>(rows.Values);
            sorted.Sort((a, b) => string.CompareOrdinal(a.what, b.what));
            foreach (var row in sorted)
            {
                bool sidesOk = row.oneSidedTris == 0;
                bool xOk = !row.tree || row.flat == 0;
                bool trunkOk = !row.tree || row.trunked == row.plants;
                bool cardOk = row.cardColliders == 0;
                bool speciesOk = row.mixed == 0;
                // A shrub is graded on its sides and nothing else.
                bool ok = row.tree ? sidesOk && xOk && trunkOk && cardOk && speciesOk : true;
                if (!ok) problems++;
                bool forest = row.what.Contains("Forest");
                log.AppendLine("  " + (ok ? (row.tree || sidesOk ? "ok   " : "note ") : "FAIL ") + row.what +
                               "  x" + row.count + "  tris " + row.tris +
                               (sidesOk ? "  both sides" : "  ONE-SIDED " + row.oneSidedTris + " tris") +
                               (row.tree ? "  trees " + row.plants +
                                           (xOk ? " all X" : ", " + row.flat + " FLAT") +
                                           "  trunks " + row.trunked + "/" + row.plants : "") +
                               (forest ? (speciesOk ? "  one species a tree"
                                                    : "  " + row.mixed + " MIXED SPECIES") : "") +
                               (cardOk ? "" : "  CARD COLLIDERS " + row.cardColliders));
            }
            return problems;
        }

        static int TotalTrunks(TreeTrunks[] tables)
        {
            int n = 0;
            foreach (var t in tables) n += t.Count;
            return n;
        }

        /// <summary>Scenery/Tree, Forest/Forest_*, Gas_station/Trees_001 —
        /// the path with numbered siblings folded, so 49 trees are one row.</summary>
        static string Group(Transform t)
        {
            string p = Regex.Replace(t.name, @"_-?\d+(_-?\d+)*$|\.\d+$|\s*\(\d+\)$", "*");
            if (t.parent != null) p = t.parent.name + "/" + p;
            return p;
        }

        /// <summary>Is something solid standing where this tree's trunk is?
        /// A capsule within reach of the base in plan and covering bumper
        /// height — or, for a merged chunk, an entry in the scene's trunk
        /// table at that base.</summary>
        static bool TrunkAt(TreeKit.Plant p, TreeTrunks[] tables)
        {
            float reach = Mathf.Max(0.6f, p.trunkRadius * 2.5f);
            foreach (var t in tables)
                if (t.Has(p.trunk, reach)) return true;
            var hits = Physics.OverlapCapsule(p.trunk + Vector3.up * 0.4f, p.trunk + Vector3.up * 1.4f,
                                              reach, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
                if ((h is CapsuleCollider || h is SphereCollider) && h.gameObject.layer == TreeKit.SolidLayer)
                    return true;
            return false;
        }
    }
}
