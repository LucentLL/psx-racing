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
            problems += AuditAtlases(log);
            log.AppendLine(problems == 0 ? "FOLIAGE OK" : "FOLIAGE PROBLEMS: " + problems);
            Debug.Log(log.ToString());
            File.WriteAllText(Path.Combine(Application.dataPath, "../PSXRacing_foliage_audit.txt"), log.ToString());
        }

        /// <summary>
        /// THE PAINTED TRUNK IS UNDER THE CROSSING, in every cell of every
        /// season's forest atlas.
        ///
        /// A forest tree's collider stands where its two cards cross: the
        /// middle of its atlas cell. "I just drove straight through a tree
        /// without impact" was a winter oak whose billboard paints its trunk
        /// 18 px — a metre and a half — to one side of that, so the car aimed
        /// at the trunk it could see and missed the one that was there. The
        /// composer slides every billboard onto the line now
        /// (TreeKit.CentreOnTrunk); this reads the PNGs back and holds it to
        /// that, a pixel and a half either way, so a billboard swapped into
        /// <c>DressTreeFiles</c> tomorrow cannot bring the bug back for one
        /// season of the year.
        /// </summary>
        public static int AuditAtlases(StringBuilder log)
        {
            const int cellPx = 128;
            int problems = 0, atlases = 0;
            log.AppendLine("");
            log.AppendLine("forest atlases — the painted trunk under the crossing of the cards:");
            // The atlases the forest MATERIALS wear, not every file of that
            // name: the first run of this failed on Art/MtMitchell/Gen and
            // Art/BeechGap/Gen — copies from before the mountains shared
            // Art/BRP's, which nothing points at and no build ships.
            var paths = new SortedSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("_Forest t:Material", new[] { "Assets/PSXRacing/Materials" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || m.mainTexture == null) continue;
                string p = AssetDatabase.GetAssetPath(m.mainTexture);
                if (p.Contains("TreeAtlas")) paths.Add(p);
            }
            foreach (var path in paths)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(File.ReadAllBytes(path));
                if (tex.width != cellPx * 4 || tex.height != cellPx * 4) { Object.DestroyImmediate(tex); continue; }
                atlases++;
                var all = tex.GetPixels32();
                Object.DestroyImmediate(tex);
                float worst = 0f; int worstCell = -1, off = 0;
                for (int i = 0; i < 16; i++)
                {
                    var cell = new Color32[cellPx * cellPx];
                    for (int y = 0; y < cellPx; y++)
                        System.Array.Copy(all, ((i / 4) * cellPx + y) * cellPx * 4 + (i % 4) * cellPx, cell, y * cellPx, cellPx);
                    TreeKit.FindTrunk(cell, cellPx, cellPx, out float x, out int w, out _);
                    if (w == 0) continue;      // an empty cell has no trunk to be wrong
                    float d = Mathf.Abs(x - (cellPx - 1) * 0.5f);
                    if (d > worst) { worst = d; worstCell = i; }
                    if (d > 1.5f) off++;
                }
                problems += off;
                log.AppendLine("  " + (off == 0 ? "ok  " : "FAIL") + " " + path.Replace("Assets/PSXRacing/Art/", "") +
                               "  worst " + worst.ToString("0.0") + " px (cell " + worstCell + ")" +
                               (off > 0 ? "  " + off + " TRUNK(S) OFF THE CROSSING" : ""));
            }
            if (atlases < Seasons.DressCount)
            {
                log.AppendLine("  FAIL " + atlases + " forest atlas(es) found, and there are " + Seasons.DressCount + " season dresses");
                problems++;
            }
            return problems;
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

            int acrossLane = CardsAcrossTheLane(tables, out string worstLane, out int canopy);
            problems += acrossLane;

            log.AppendLine("");
            log.AppendLine("foliage — " + scene + (tables.Length > 0
                ? "  (trunk table: " + TotalTrunks(tables) + " trunks)" : ""));
            if (tables.Length > 0)
                // The count of trees that DO reach over the road, from high
                // enough, is the check's own control: zero of those as well
                // would mean it was measuring nothing.
                log.AppendLine("  " + (acrossLane == 0 ? "ok  " : "FAIL") + " no billboard across the lane below four metres (" +
                               canopy + " reach over it from above that: the canopy)" +
                               (acrossLane > 0 ? "  " + acrossLane + " ACROSS THE LANE, worst: " + worstLane : ""));

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

        /// <summary>
        /// NO BILLBOARD ACROSS THE LANE AT A HEIGHT A CAR OR ITS CAMERA REACHES.
        ///
        /// A forest tree is two flat cards up to sixteen metres wide, and the
        /// forest pass plants them from seven metres off the centreline: the
        /// end of a card can lie over the tarmac. High up that is canopy; on a
        /// falling verge, where the whole crown sits at road level, it was
        /// orange leaves through the guard wall into the lane at eye height
        /// (seen in the first thick forest's driver's-eye shots, 2026-09-19).
        /// The builder shrinks or drops such trees; this reads every tree in
        /// the table back against the path and counts the ones that still
        /// reach over the road with foliage starting under four metres above
        /// it. Species 12-14 are the conifers (narrow card, foliage from a
        /// tenth of the height); the rest are broadleaf (from 28%).
        /// </summary>
        static int CardsAcrossTheLane(TreeTrunks[] tables, out string worst, out int canopy)
        {
            worst = ""; canopy = 0;
            var path = Object.FindFirstObjectByType<TrackPath>();
            if (path == null || path.Count < 2 || tables.Length == 0) return 0;
            float roadHalf = path.roadWidth * 0.5f;
            int bad = 0; float worstOver = 0f;
            foreach (var t in tables)
                for (int i = 0; i < t.Count; i++)
                {
                    float w = t.CardWidthOf(i);
                    if (w <= 0f) continue;
                    Vector3 b = t.BaseOf(i);
                    // Only a tree within reach of the road can offend.
                    float best = float.MaxValue; int at = -1;
                    for (int k = 0; k < path.Count; k += 3)
                    {
                        Vector3 p = path.waypoints[k];
                        float dx = p.x - b.x, dz = p.z - b.z, d2 = dx * dx + dz * dz;
                        if (d2 < best) { best = d2; at = k; }
                    }
                    if (at < 0 || best > 20f * 20f) continue;
                    for (int k = Mathf.Max(0, at - 3); k <= Mathf.Min(path.Count - 1, at + 3); k++)
                    {
                        Vector3 p = path.waypoints[k];
                        float dx = p.x - b.x, dz = p.z - b.z, d2 = dx * dx + dz * dz;
                        if (d2 < best) { best = d2; at = k; }
                    }
                    float d = Mathf.Sqrt(best);
                    float over = roadHalf + 0.3f - (d - w * 0.5f);
                    if (over <= 0f) continue;
                    int cell = t.AtlasCellOf(i);
                    bool conifer = cell >= 12 && cell <= 14;
                    float h = conifer ? w / 0.62f : w;
                    float foliageFoot = b.y + 0.25f + h * (conifer ? 0.10f : 0.28f);
                    if (foliageFoot - path.waypoints[at].y >= 4.0f) { canopy++; continue; }
                    // A tree far BELOW the road (under a bridge deck) is not in the lane either.
                    if (path.waypoints[at].y - (b.y + h) > 1.5f) continue;
                    bad++;
                    if (over > worstOver)
                    {
                        worstOver = over;
                        worst = "tree " + i + " reaches " + over.ToString("0.0") + " m over the tarmac, foliage from " +
                                (foliageFoot - path.waypoints[at].y).ToString("0.0") + " m above the road";
                    }
                }
            return bad;
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
