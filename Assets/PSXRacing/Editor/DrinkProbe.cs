using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Every drink in the pizzeria props pack, ONE TRANSFORM AT A TIME.
    ///
    /// PackProbe lists a pack by material island, and on this pack that is the
    /// wrong grain: the bottles all wear one material and stand touching on a
    /// shelf, so the island pass merges the whole shelf into a single blob
    /// nearly a metre across. The baker, meanwhile, asked for three names and
    /// took the first one taller than it was wide — a small glass bottle — and
    /// the two litre ones the owner circled were never looked at. This lists
    /// each object with its own measured size so the bottle can be chosen by
    /// what it IS, sorted tallest first, which is where the two litre ones are.
    /// </summary>
    public static class DrinkProbe
    {
        const string PackFbx = "Assets/PSXRacing/Art/LifeSim/PizzeriaScene/Pizzeria_Props.fbx";
        static readonly string[] Keys = { "soft", "soda", "drink", "bottl", "cola", "juice", "water" };

        [MenuItem("PSX Racing/Probe Drinks")]
        public static void Run()
        {
            var sb = new StringBuilder();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PackFbx);
            if (prefab == null) { sb.AppendLine("MISSING " + PackFbx); Write(sb); return; }

            var go = (GameObject)Object.Instantiate(prefab);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var rows = new List<(string name, Vector3 size, int children, string path)>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                bool hit = false;
                foreach (var k in Keys) if (n.Contains(k)) { hit = true; break; }
                if (!hit) continue;
                var rs = t.GetComponentsInChildren<MeshRenderer>(true);
                if (rs.Length == 0) continue;
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                string path = t.name;
                for (var p = t.parent; p != null && p != go.transform; p = p.parent) path = p.name + "/" + path;
                rows.Add((t.name, b.size, rs.Length, path));
            }
            rows.Sort((a, b) => b.size.y.CompareTo(a.size.y));

            sb.AppendLine("--- " + PackFbx + "  (" + rows.Count + " drink-ish transforms, tallest first)");
            sb.AppendLine("    aspect = height / max(width, depth): a two litre bottle is about 3, a can under 1.5");
            foreach (var r in rows)
            {
                float plan = Mathf.Max(r.size.x, r.size.z);
                float aspect = plan > 1e-4f ? r.size.y / plan : 0f;
                sb.AppendLine(string.Format(
                    "    {0,-26} h {1,6:0.000}  plan {2,6:0.000}  aspect {3,4:0.0}  renderers {4,2}   {5}",
                    r.name, r.size.y, plan, aspect, r.children, r.path));
            }
            Object.DestroyImmediate(go);
            Write(sb);
        }

        /// <summary>
        /// PHOTOGRAPH EACH ONE, alone. The listing above says what size a
        /// thing is and nothing about what it looks like, and the question
        /// here is a picture question: which of these is the shelf of two
        /// litre bottles the owner drew a ring round. Every other renderer in
        /// the pack is switched off, a camera is stood off the object's own
        /// bounds, and the frame goes to Screenshots/Drinks/<name>.png.
        /// </summary>
        [MenuItem("PSX Racing/Shoot Drinks")]
        public static void Shoot()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PackFbx);
            if (prefab == null) { Debug.LogError("[DrinkProbe] missing " + PackFbx); return; }
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                                      "Screenshots", "Drinks");
            Directory.CreateDirectory(dir);

            var go = (GameObject)Object.Instantiate(prefab);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var lightGO = new GameObject("~key");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGO.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);

            var camGO = new GameObject("~drinkCam");
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.18f, 0.18f, 0.2f, 1f);
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 50f;

            var all = go.GetComponentsInChildren<MeshRenderer>(true);
            int shot = 0;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                bool hit = false;
                foreach (var k in Keys) if (n.Contains(k)) { hit = true; break; }
                if (!hit) continue;
                var mine = t.GetComponentsInChildren<MeshRenderer>(true);
                if (mine.Length == 0) continue;

                foreach (var r in all) r.enabled = false;
                foreach (var r in mine) r.enabled = true;
                var b = mine[0].bounds;
                foreach (var r in mine) b.Encapsulate(r.bounds);

                // Stand off far enough that the whole bounds fits the 35 degree
                // lens, from front-right and a little above, looking at the
                // middle: the framing that shows a bottle's label and a shelf's
                // rows.
                float radius = b.extents.magnitude;
                float dist = radius / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f;
                var dirv = new Vector3(0.6f, 0.45f, -1f).normalized;
                cam.transform.position = b.center + dirv * dist;
                cam.transform.LookAt(b.center);

                var rt = new RenderTexture(640, 480, 24);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(640, 480, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 640, 480), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;
                string safe = t.name.Replace('.', '_');
                File.WriteAllBytes(Path.Combine(dir, safe + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                rt.Release();
                Object.DestroyImmediate(rt);
                shot++;
            }
            Object.DestroyImmediate(camGO);
            Object.DestroyImmediate(lightGO);
            Object.DestroyImmediate(go);
            Debug.Log("[DrinkProbe] shot " + shot + " drinks into " + dir);
        }

        static void Write(StringBuilder sb)
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                                       "PSXRacing_drinkprobe.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[DrinkProbe] wrote " + path + "\n" + sb);
        }
    }
}
