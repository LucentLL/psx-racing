using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// WHERE IS THAT DOOR, IN UNITY'S COORDINATES?
    ///
    /// Every prop in this project is placed by arithmetic off a landmark on the
    /// model — the garage door of house_simple, the nozzle of a pump, the
    /// counter of a shop — and every one of those numbers has so far been
    /// measured in BLENDER and then converted by hand. That conversion is where
    /// the mistakes live: Blender is Z-up right-handed, Unity is Y-up
    /// left-handed, the FBX between them is Y-up right-handed, and getting the
    /// sign of ONE axis wrong puts the landmark on the opposite side of the
    /// building. It did: the neighbourhood's driveways ran to the blank wall on
    /// the far side of every house because NbGarageOffsetZ was measured off the
    /// door's outer jamb in Blender and then signed by guess. Reported in one
    /// line: "driveways go to the wrong side of house."
    ///
    /// So stop converting. This asks UNITY where the landmark is, in the local
    /// space of the root a <see cref="WorldKit.Place"/> call would hand back,
    /// which is the only frame the placement arithmetic ever works in. The
    /// answer needs no interpretation and cannot be off by a handedness.
    ///
    /// Grouped BY MATERIAL, because that is the only name these packs carry
    /// that means anything — the meshes are one object called "House" with a
    /// dozen material slots, and "Garage_Door" is the slot the door is on.
    /// Split further into ISLANDS along each axis, because a material is used
    /// on more than one thing: house_simple has a wide garage door at the front
    /// and a narrow one at the back, and the bounding box of both together
    /// centres on neither.
    /// </summary>
    public static class PackProbe
    {
        /// <summary>Models worth having a permanent record of, and the reason
        /// each one is here. Every entry is a model some builder places by
        /// measurement rather than by its origin.</summary>
        static readonly string[] Models =
        {
            "Assets/PSXRacing/Art/LifeSim/House/house_simple.fbx",
            "Assets/PSXRacing/Art/LifeSim/House/house_hero.fbx",
            "Assets/PSXRacing/Art/LifeSim/House/house_hero_colliders.fbx",
            // The pizzeria's props: every drink in the pack, by material
            // island, so a bottle can be chosen by its measured shape rather
            // than by guessing at names. The baker's candidate list only ever
            // asked for "Soft_drinks_*" and got a small glass bottle; the two
            // litre ones the owner circled are in here under something else.
            "Assets/PSXRacing/Art/LifeSim/PizzeriaScene/Pizzeria_Props.fbx",
        };

        [MenuItem("PSX Racing/Probe Pack Models")]
        public static void Run()
        {
            var log = new StringBuilder();
            log.AppendLine("=== PACK PROBE: material landmarks in UNITY LOCAL space ===");
            log.AppendLine("(local space of the instantiated root, BEFORE any scale or rotation)");
            foreach (var path in Models) Probe(path, log);
            File.WriteAllText("PSXRacing_packprobe.txt", log.ToString());
            Debug.Log(log.ToString());
        }

        static void Probe(string path, StringBuilder log)
        {
            log.AppendLine();
            log.AppendLine("--- " + path);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { log.AppendLine("    MISSING"); return; }

            var go = (GameObject)Object.Instantiate(prefab);
            // Identity, deliberately: the placement arithmetic is written in the
            // model's own local frame and applies the scale and the rotation
            // itself. A probe that reported world numbers would be reporting
            // the answer to a different question every time it was called.
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var whole = new Bounds();
            bool first = true;
            var byMat = new Dictionary<string, List<Vector3>>();

            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                var mats = mr != null ? mr.sharedMaterials : null;
                var verts = mesh.vertices;
                // Into the ROOT's frame, not the world's and not the child's:
                // these packs hang their mesh off a node with a translation on
                // it, and reading the child's own vertices ignores that.
                var toRoot = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;

                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    string name = (mats != null && s < mats.Length && mats[s] != null)
                                ? mats[s].name.Replace(" (Instance)", "")
                                : "<slot " + s + ">";
                    if (!byMat.TryGetValue(name, out var pts))
                        byMat[name] = pts = new List<Vector3>();

                    var idx = mesh.GetTriangles(s);
                    for (int i = 0; i < idx.Length; i++)
                    {
                        Vector3 p = toRoot.MultiplyPoint3x4(verts[idx[i]]);
                        pts.Add(p);
                        if (first) { whole = new Bounds(p, Vector3.zero); first = false; }
                        else whole.Encapsulate(p);
                    }
                }
            }

            log.AppendLine(string.Format(
                "    WHOLE  centre=({0,7:0.000},{1,7:0.000},{2,7:0.000})  size=({3,6:0.000},{4,6:0.000},{5,6:0.000})",
                whole.center.x, whole.center.y, whole.center.z,
                whole.size.x, whole.size.y, whole.size.z));

            var names = new List<string>(byMat.Keys);
            names.Sort();
            foreach (var name in names)
            {
                foreach (var island in Islands(byMat[name]))
                    log.AppendLine(string.Format(
                        "    {0,-22} centre=({1,7:0.000},{2,7:0.000},{3,7:0.000})  size=({4,6:0.000},{5,6:0.000},{6,6:0.000})  pts={7}",
                        name, island.b.center.x, island.b.center.y, island.b.center.z,
                        island.b.size.x, island.b.size.y, island.b.size.z, island.n));
            }

            Object.DestroyImmediate(go);
        }

        struct Island { public Bounds b; public int n; }

        /// <summary>
        /// Split a material's points into separated clumps, so a material used
        /// on two things at opposite ends of a building reports two landmarks
        /// rather than one meaningless average.
        ///
        /// A one-dimensional split on the widest axis, repeated: it is enough
        /// for the case that matters — the same door on the front and the back
        /// wall — and a real connectivity flood fill would need the index
        /// buffer welded first, which these packs do not come welded.
        /// </summary>
        static List<Island> Islands(List<Vector3> pts)
        {
            var all = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) all.Encapsulate(p);

            int axis = all.size.x >= all.size.y && all.size.x >= all.size.z ? 0
                     : all.size.y >= all.size.z ? 1 : 2;
            // A gap has to be wider than a garage door is thick before it counts
            // as two things; below that it is just the space between two facets.
            const float Gap = 1.0f;
            if (all.size[axis] < Gap) return One(all, pts.Count);

            var sorted = new List<float>(pts.Count);
            foreach (var p in pts) sorted.Add(p[axis]);
            sorted.Sort();

            var cuts = new List<float>();
            for (int i = 1; i < sorted.Count; i++)
                if (sorted[i] - sorted[i - 1] > Gap) cuts.Add((sorted[i] + sorted[i - 1]) * 0.5f);
            if (cuts.Count == 0) return One(all, pts.Count);

            var groups = new List<List<Vector3>>();
            for (int i = 0; i <= cuts.Count; i++) groups.Add(new List<Vector3>());
            foreach (var p in pts)
            {
                int g = 0;
                while (g < cuts.Count && p[axis] > cuts[g]) g++;
                groups[g].Add(p);
            }

            var outp = new List<Island>();
            foreach (var g in groups)
            {
                if (g.Count == 0) continue;
                var b = new Bounds(g[0], Vector3.zero);
                foreach (var p in g) b.Encapsulate(p);
                outp.Add(new Island { b = b, n = g.Count });
            }
            return outp;
        }

        static List<Island> One(Bounds b, int n) =>
            new List<Island> { new Island { b = b, n = n } };
    }
}
