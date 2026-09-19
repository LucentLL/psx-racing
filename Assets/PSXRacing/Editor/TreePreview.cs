using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// WALK ROUND A TREE: eight compass points, drawn the old way and the new.
    ///
    /// "Some trees are still 2 dimensions and flat." Whether a crossed
    /// billboard is an X depends entirely on where you stand — every single
    /// photograph of the one-sided trees was of SOME tree, from SOME side, and
    /// looked fine. So this stands one tree of each kind alone and orbits it:
    /// row one with the material culling its back faces (what shipped), row two
    /// drawing both (the fix), and a red post where the trunk's collider is,
    /// its own radius, a metre and a bit tall, so "the car stops at the trunk
    /// it can see" is a picture rather than a claim.
    ///
    /// Graphics ON (no -nographics): it renders. Writes Screenshots/Trees/.
    /// </summary>
    public static class TreePreview
    {
        const int W = 400, H = 300;

        /// <summary>Atlas cell (column, row from the bottom) the forest
        /// specimen is taken from: RedGroup's middle slot.</summary>
        static readonly Vector2Int SpecimenCell = new Vector2Int(1, 1);

        [MenuItem("PSX Racing/Preview Trees")]
        public static void Run()
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "Trees");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
            var log = new System.Text.StringBuilder();

            // ---- a circuit's roadside tree ----
            EditorSceneManager.OpenScene("Assets/PSXRacing/Scenes/CityCircuit.unity", OpenSceneMode.Single);
            Globals();
            MeshRenderer circuit = null;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (r.name == "Tree" && TreeKit.KindOf(r.sharedMaterial) == TreeKit.Kind.Tree) { circuit = r; break; }
            if (circuit != null)
            {
                var cap = circuit.GetComponentInChildren<CapsuleCollider>();
                Orbit(dir, "circuit", circuit, cap != null ? Base(cap) : circuit.transform.position,
                      cap != null ? cap.radius : 0f, both: true, log);
            }
            else log.AppendLine("circuit: NO TREE FOUND");

            // ---- the station's pack tree, same scene ----
            MeshRenderer pack = null;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (r.GetComponentInChildren<CapsuleCollider>() != null &&
                    TreeKit.KindOf(r.sharedMaterial) == TreeKit.Kind.Tree && r.name != "Tree") { pack = r; break; }
            if (pack != null)
            {
                var cap = pack.GetComponentInChildren<CapsuleCollider>();
                // The pack's own double-sided cards: only the new row means
                // anything, and it is about where the trunk landed.
                Orbit(dir, "station", pack, Base(cap), cap.radius, both: false, log, focusOnTrunk: true);
            }
            else log.AppendLine("station: NO PACK TREE WITH A TRUNK FOUND");

            // ---- one tree out of a mountain forest chunk ----
            EditorSceneManager.OpenScene("Assets/PSXRacing/Scenes/BlueRidge.unity", OpenSceneMode.Single);
            Globals();
            // THE SLOT, NOT WHICHEVER TREE COMES FIRST. Object order out of a
            // scene is not stable, so "tree 40 of the first chunk" was a red
            // maple one run and a spruce the next. The specimen is the first
            // tree (chunks by name) whose UVs sit in atlas cell SpecimenCell —
            // the red-cluster slot the owner asked about: tree022, one sheet
            // painted red on one side and green on the other, kept as is.
            var chunks = new List<MeshRenderer>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (r.name.StartsWith("Forest_")) chunks.Add(r);
            chunks.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            MeshRenderer chunk = null;
            int pick = -1;
            Vector3[] v = null;
            Vector2[] uv = null;
            foreach (var c in chunks)
            {
                var m = c.GetComponent<MeshFilter>().sharedMesh;
                if (!TreeKit.Read(m, 0, out var cv, out _)) continue;
                var cuv = m.uv;
                for (int t = 0; t + TreeKit.VertsPerTree <= cv.Length && pick < 0; t += TreeKit.VertsPerTree)
                    if (Mathf.FloorToInt(cuv[t].x * 4f) == SpecimenCell.x && Mathf.FloorToInt(cuv[t].y * 4f) == SpecimenCell.y)
                        pick = t;
                if (pick >= 0) { chunk = c; v = cv; uv = cuv; break; }
            }
            if (chunk == null && chunks.Count > 0)
            {
                chunk = chunks[0];
                var m = chunk.GetComponent<MeshFilter>().sharedMesh;
                TreeKit.Read(m, 0, out v, out _);
                uv = m.uv;
                pick = 0;
                log.AppendLine("forest: no tree in cell " + SpecimenCell + " - took the first");
            }
            var table = Object.FindFirstObjectByType<TreeTrunks>();
            if (chunk != null)
            {
                // A specimen: one tree's eight corners lifted out of the
                // merged chunk onto an object of its own, same material.
                var sv = new Vector3[TreeKit.VertsPerTree];
                var su = new Vector2[TreeKit.VertsPerTree];
                for (int k = 0; k < TreeKit.VertsPerTree; k++) { sv[k] = v[pick + k]; su[k] = uv[pick + k]; }
                var sm = new Mesh { vertices = sv, uv = su, triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 } };
                sm.RecalculateNormals();
                sm.RecalculateBounds();
                var spec = new GameObject("~specimen");
                spec.transform.SetPositionAndRotation(chunk.transform.position, chunk.transform.rotation);
                spec.AddComponent<MeshFilter>().sharedMesh = sm;
                var sr = spec.AddComponent<MeshRenderer>();
                sr.sharedMaterial = chunk.sharedMaterial;
                // Measured from the SPECIMEN's base every time. The first cut
                // moved the reference onto each better trunk as it found it,
                // walked off across the table and drew the post 16 m away.
                Vector3 specimenBase = chunk.transform.TransformPoint((sv[0] + sv[3]) * 0.5f);
                Vector3 at = specimenBase;
                float rad = 0f;
                if (table != null)
                {
                    float best = float.MaxValue;
                    for (int i = 0; i < table.Count; i++)
                    {
                        float d = (table.BaseOf(i) - specimenBase).sqrMagnitude;
                        if (d < best) { best = d; rad = table.RadiusOf(i); at = table.BaseOf(i); }
                    }
                    log.AppendLine("forest: nearest trunk in the table is " + Mathf.Sqrt(best).ToString("0.000") +
                                   " m from the specimen's base");
                }
                Orbit(dir, "forest", sr, at, rad, both: true, log);
                Object.DestroyImmediate(spec);
            }
            else log.AppendLine("forest: NO FOREST CHUNK FOUND");

            File.WriteAllText(Path.Combine(dir, "trees.txt"), log.ToString());
            Debug.Log("[Trees] " + log);
        }

        static Vector3 Base(CapsuleCollider cap)
        {
            var t = cap.transform;
            return t.TransformPoint(cap.center - Vector3.up * (cap.height * 0.5f));
        }

        /// <summary>The shader's globals, as HoistPreview sets them: without
        /// them every surface is black.</summary>
        static void Globals()
        {
            Shader.SetGlobalFloat("_PSXFogNear", 400f);
            Shader.SetGlobalFloat("_PSXFogFar", 900f);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.62f, 0.70f, 0.80f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.56f, 0.60f));
            Shader.SetGlobalColor("_PSXSkyAmbient", new Color(0.58f, 0.64f, 0.74f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.5f, 0.7f, -0.35f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.80f, 0.76f, 0.66f));
        }

        static void Orbit(string dir, string name, MeshRenderer tree, Vector3 trunkBase, float trunkR,
                          bool both, System.Text.StringBuilder log, bool focusOnTrunk = false)
        {
            // Everything else off.
            var hidden = new List<Renderer>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == tree || !r.enabled) continue;
                r.enabled = false;
                hidden.Add(r);
            }
            GameObject post = null;
            if (trunkR > 0f)
            {
                // The collider, drawn: its radius, 1.2 m of it, lit red.
                post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.name = "~trunkPost";
                Object.DestroyImmediate(post.GetComponent<Collider>());
                post.transform.position = trunkBase + Vector3.up * 0.6f;
                post.transform.localScale = new Vector3(trunkR * 2f, 0.6f, trunkR * 2f);
                var pm = new Material(Shader.Find("PSX/Lit")) { color = new Color(1f, 0.1f, 0.1f) };
                pm.SetFloat("_Emission", 1f);
                post.GetComponent<MeshRenderer>().sharedMaterial = pm;
            }
            var keep = tree.sharedMaterials;
            try
            {
                var b = tree.bounds;
                Vector3 centre = focusOnTrunk ? trunkBase : new Vector3(b.center.x, b.min.y, b.center.z);
                float h = b.size.y;
                float dist = Mathf.Max(8f, h * 1.35f);
                Vector3 look = centre + Vector3.up * (h * 0.42f);
                for (int pass = both ? 0 : 1; pass < 2; pass++)
                {
                    // Pass 0 is the material as it SHIPPED (backs culled);
                    // pass 1 is the fix. A copy each time — the asset on disk
                    // is never touched.
                    var mats = new Material[keep.Length];
                    for (int i = 0; i < keep.Length; i++)
                    {
                        mats[i] = new Material(keep[i]);
                        if (pass == 0) mats[i].SetFloat("_Cull", 2f);
                    }
                    tree.sharedMaterials = mats;
                    for (int a = 0; a < 8; a++)
                    {
                        float yaw = a * 45f * Mathf.Deg2Rad;
                        var from = centre + new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * dist +
                                   Vector3.up * 1.6f;
                        Shoot(dir, name + "_" + (pass == 0 ? "before" : "after") + "_" + (a * 45).ToString("000"),
                              from, Quaternion.LookRotation(look - from, Vector3.up));
                    }
                    foreach (var m in mats) Object.DestroyImmediate(m);
                }
                log.AppendLine(name + ": " + tree.name + "  h " + h.ToString("0.0") + " m  trunk r " +
                               trunkR.ToString("0.00") + " at " + trunkBase.ToString("0.0"));
            }
            finally
            {
                tree.sharedMaterials = keep;
                if (post != null) Object.DestroyImmediate(post);
                foreach (var r in hidden) if (r != null) r.enabled = true;
            }
        }

        static void Shoot(string dir, string name, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject("~treeCam");
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.70f, 0.80f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 400f;
            cam.fieldOfView = 50f;
            var rt = new RenderTexture(W, H, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
        }
    }
}
