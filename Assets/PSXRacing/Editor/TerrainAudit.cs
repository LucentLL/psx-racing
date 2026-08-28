using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Measures the built scene where the ground meets everything standing on
    /// it, by dropping rays onto it rather than by re-running the maths that
    /// put it there.
    ///
    /// Every fault this looks for is silent, and every one of them has shipped:
    ///
    ///   * ground through the road — the landscape grid is coarse and the
    ///     tarmac rides 12 cm above it, so a crest too tight for the grid
    ///     resolution carpets the racing line in triangles of hillside;
    ///   * a gorge that was never dug — a bridge whose terrain carve did not
    ///     happen still looks completely normal from the driving line, because
    ///     from up there a bridge is only road;
    ///   * daylight under a building — the reason this file exists. These
    ///     meshes are hollow shells with no floor, so a base level with the
    ///     ground is a base you can see straight in under, and on sloping
    ///     ground the downhill corner hangs in the air.
    ///
    /// Rays, not formulas: the builder already believes its own arithmetic, and
    /// a second copy of it would agree with the first while both were wrong.
    /// What this asks is what a wheel would find.
    ///
    /// Menu: PSX Racing/Audit Terrain.
    /// </summary>
    public static class TerrainAudit
    {
        /// <summary>Metres of ground the road has to stand clear of before it
        /// counts as buried. The ribbon sits 12 cm up, so anything that leaves
        /// under 2 cm is about to poke through.</summary>
        const float RoadClearMin = 0.02f;
        /// <summary>How far under a bridge deck the ground has to fall away
        /// before the span reads as a span rather than as a hump.</summary>
        const float BridgeDropMin = 3f;

        [MenuItem("PSX Racing/Audit Terrain")]
        public static void Run()
        {
            var log = new StringBuilder();
            int failures = 0;
            foreach (var def in TrackCatalog.All) failures += AuditOne(def, log);
            log.AppendLine(failures == 0 ? "TERRAIN AUDIT OK" : "TERRAIN AUDIT: " + failures + " PROBLEM(S)");
            Debug.Log(log.ToString());
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "../PSXRacing_terrain_audit.txt"),
                log.ToString());
        }

        static int AuditOne(TrackCatalog.TrackDef def, StringBuilder log)
        {
            // The city has no baked ground to ray — its terrain is generated
            // per tile at runtime and audited by CityAudit. Counting its
            // by-design empty scene as a failure buried every real result
            // under a permanent "1 PROBLEM(S)".
            if (def.city) return 0;

            string scenePath = "Assets/PSXRacing/Scenes/" + def.id + ".unity";
            if (!System.IO.File.Exists(scenePath))
            {
                log.AppendLine("MISSING SCENE " + scenePath);
                return 1;
            }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var path = Object.FindFirstObjectByType<TrackPath>();
            var ground = GameObject.Find("Track/Ground");
            var road = GameObject.Find("Track/Road");
            if (path == null || path.Count == 0 || ground == null || road == null)
            {
                log.AppendLine(def.id + ": no track path, no ground or no road");
                return 1;
            }
            // Every ground MESH, not every ground COLLIDER.
            //
            // This gathered colliders for its whole life, and that is exactly
            // how the stage's far mountain chunks — 60 m cells, renderer-only,
            // no collision because nothing drivable ever reaches them — sat in
            // the middle of the parkway for a week with the audit reporting a
            // clean pass every run. A hillside you can drive through still
            // hides the corner. Temporary colliders go on for the audit and
            // come off below; the scene is never saved.
            var groundCols = new List<Collider>();
            var temporary = new List<MeshCollider>();
            foreach (var mf in ground.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var col = mf.GetComponent<Collider>();
                if (col == null)
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    temporary.Add(mc);
                    col = mc;
                }
                groundCols.Add(col);
            }
            var roadCol = road.GetComponent<Collider>();
            if (groundCols.Count == 0 || roadCol == null)
            {
                log.AppendLine(def.id + ": ground has no mesh, or road has no collider");
                return 1;
            }

            log.AppendLine("=== " + def.id + " ===");
            int problems = 0;

            // ---- 1. the ground never comes up through the tarmac ----
            // Sampled across the WIDTH, not only down the centreline. A crest
            // that carpets the outside of a corner leaves the middle of the
            // lane perfectly clean, so the centreline-only version of this
            // passed every single run while the corner was unreadable from the
            // driving seat.
            float half = Mathf.Max(1f, path.roadWidth * 0.5f - 0.6f);
            float worstClear = float.MaxValue;
            int worstIdx = -1;
            string worstWhat = null;
            int buried = 0, probes = 0;
            float lowY = float.MaxValue, highY = float.MinValue;
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 wp = path.GetPoint(i);
                lowY = Mathf.Min(lowY, wp.y); highY = Mathf.Max(highY, wp.y);
                // Skip the spans that are SUPPOSED to have nothing under them.
                if (BlendAt(def, i, path.spacing) > 0.02f) continue;
                Vector3 right = RightAt(path, i);
                foreach (float off in new[] { -half, 0f, half })
                {
                    Vector3 at = wp + right * off;
                    // Both surfaces measured, so the answer needs no copy of
                    // the builder constant that lifts the ribbon off the
                    // ground.
                    if (!DropAny(groundCols, at, out float gy, out string what)) continue;
                    if (!Drop(roadCol, at, out float ry)) continue;
                    probes++;
                    float clear = ry - gy;
                    if (clear < RoadClearMin) buried++;
                    if (clear < worstClear) { worstClear = clear; worstIdx = i; worstWhat = what; }
                }
            }
            if (worstIdx >= 0)
            {
                bool ok = worstClear >= RoadClearMin;
                if (!ok) problems++;
                log.AppendLine(string.Format(
                    "  {0} road stands {1:0.000} m clear of the ground at its tightest " +
                    "(waypoint {2}, {3}); {4} of {5} probes buried",
                    ok ? "ok  " : "FAIL", worstClear, worstIdx, worstWhat ?? "?", buried, probes));
            }
            log.AppendLine(string.Format("  ..   climbs {0:0.0} m ({1:0.0} to {2:0.0})",
                highY - lowY, lowY, highY));

            // ---- 2. every bridge has a hole under it ----
            if (def.bridges != null && def.bridges.Length > 0)
            {
                float deepest = 0f;
                int spanWaypoints = 0;
                for (int i = 0; i < path.Count; i++)
                {
                    if (BlendAt(def, i, path.spacing) < 0.98f) continue;
                    spanWaypoints++;
                    Vector3 wp = path.GetPoint(i);
                    // From UNDER the deck, so the deck itself is not what the
                    // ray finds first.
                    if (!DropAny(groundCols, wp - Vector3.up * 2.5f, out float gy)) continue;
                    deepest = Mathf.Max(deepest, wp.y - gy);
                }
                bool ok = deepest >= BridgeDropMin && spanWaypoints > 0;
                if (!ok) problems++;
                log.AppendLine(string.Format(
                    "  {0} gorge is {1:0.0} m deep under {2} waypoints of full-depth span",
                    ok ? "ok  " : "FAIL", deepest, spanWaypoints));

                var decks = new List<GameObject>();
                foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                    if (mf.gameObject.name.StartsWith("BridgeDeck")) decks.Add(mf.gameObject);
                bool deckOk = decks.Count == def.bridges.Length;
                if (!deckOk) problems++;
                log.AppendLine(string.Format("  {0} {1} deck(s) built for {2} span(s)",
                    deckOk ? "ok  " : "FAIL", decks.Count, def.bridges.Length));
            }

            // ---- 3. nothing you can see under ----
            float worstGap = float.MinValue;
            string worstName = null;
            int floating = 0, sampled = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var go = r.gameObject;
                if (go.name != "Building" && !go.name.StartsWith("Parked_")
                    && go.name != "Gas_station" && go.name != "Tree") continue;
                // Walk up to the thing the builder actually POSITIONED, which
                // is always a direct child of Scenery. Stopping one level short
                // measures a sub-object against the ground instead of against
                // what it is standing on: the forecourt props sit on the
                // station slab a metre up, and this reported them as floating.
                Transform root = go.transform;
                while (root.parent != null && root.parent.name != "Scenery") root = root.parent;

                var b = WorldBounds(root.gameObject);
                if (b.size.sqrMagnitude < 0.01f) continue;
                sampled++;
                // The corners of the footprint. A block set into a hill is fine
                // at its middle and hanging by 2 m at one corner, which is
                // exactly the shape of the fault.
                float gap = float.MinValue;
                for (int c = 0; c < 4; c++)
                {
                    float x = (c & 1) == 0 ? b.min.x : b.max.x;
                    float z = (c & 2) == 0 ? b.min.z : b.max.z;
                    // Pull in slightly so a corner exactly on a facet edge does
                    // not miss the mesh entirely.
                    x = Mathf.Lerp(x, b.center.x, 0.06f);
                    z = Mathf.Lerp(z, b.center.z, 0.06f);
                    if (!DropAny(groundCols, new Vector3(x, b.max.y + 5f, z), out float gy)) continue;
                    gap = Mathf.Max(gap, gy - b.min.y);   // >0 means buried
                }
                if (gap == float.MinValue) continue;
                float showing = -gap;                     // >0 means daylight under it
                if (showing > worstGap) { worstGap = showing; worstName = root.name; }
                if (showing > 0.05f) floating++;
            }
            foreach (var mc in temporary) Object.DestroyImmediate(mc);
            if (sampled > 0)
            {
                bool ok = floating == 0;
                if (!ok) problems++;
                log.AppendLine(string.Format(
                    "  {0} {1} of {2} props show daylight underneath (worst {3:0.00} m, {4})",
                    ok ? "ok  " : "FAIL", floating, sampled, Mathf.Max(0f, worstGap), worstName));
            }

            return problems;
        }

        /// <summary>Bridge blend at a waypoint index. A strip has none.</summary>
        static float BlendAt(TrackCatalog.TrackDef def, int i, float spacing) =>
            def.drag ? 0f : TrackCatalog.BridgeBlend(def, Mathf.Repeat(i * spacing, Mathf.Max(def.LengthM, 1f)));

        /// <summary>Straight down onto ONE collider, from well above. Raycasts
        /// the ground specifically rather than the scene, so a wall, a kerb or
        /// the deck itself cannot answer a question about the land.</summary>
        static bool Drop(Collider ground, Vector3 from, out float y)
        {
            y = 0f;
            var ray = new Ray(from + Vector3.up * 200f, Vector3.down);
            if (!ground.Raycast(ray, out RaycastHit hit, 600f)) return false;
            y = hit.point.y;
            return true;
        }

        /// <summary>The same drop against a SET of ground colliders (the
        /// stage's chunks), keeping the HIGHEST hit and naming what answered —
        /// in the near/far overlap ring both answer, and which of the two is
        /// the one standing in the road is the whole diagnosis.</summary>
        static bool DropAny(List<Collider> grounds, Vector3 from, out float y, out string what)
        {
            y = float.MinValue; what = null;
            bool any = false;
            foreach (var g in grounds)
            {
                var b = g.bounds;
                if (from.x < b.min.x - 1f || from.x > b.max.x + 1f ||
                    from.z < b.min.z - 1f || from.z > b.max.z + 1f) continue;
                if (Drop(g, from, out float gy) && gy > y) { y = gy; what = g.gameObject.name; any = true; }
            }
            return any;
        }

        static bool DropAny(List<Collider> grounds, Vector3 from, out float y) =>
            DropAny(grounds, from, out y, out _);

        /// <summary>Road-right at a waypoint, from the neighbouring points.
        /// Clamped rather than wrapped: one sample either side of the seam on a
        /// loop is not worth a special case in an audit.</summary>
        static Vector3 RightAt(TrackPath path, int i)
        {
            int a = Mathf.Max(0, i - 1), b = Mathf.Min(path.Count - 1, i + 1);
            Vector3 t = path.GetPoint(b) - path.GetPoint(a);
            t.y = 0f;
            if (t.sqrMagnitude < 1e-6f) return Vector3.right;
            return Vector3.Cross(Vector3.up, t.normalized).normalized;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<MeshRenderer>();
            if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
