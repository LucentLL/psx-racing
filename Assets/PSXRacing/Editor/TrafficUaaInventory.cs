using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// INVENTORY of the owner's "UAA - City Props - Vehicles" pack (2026-11-15,
    /// traffic-uaa): every prefab as the pack ships it - its parts, sizes,
    /// triangles, materials, textures, whether its wheels are separate - and
    /// two plain pictures of each, so what is used and what is left out is
    /// decided from the files and not from the store page.
    ///
    /// The pack is store content (gitignored): tools/store-assets.ps1 extracts
    /// it into Assets/StoreAssets/UAAVehicles of a sandbox first.
    ///
    ///   -executeMethod PSXRacing.EditorTools.TrafficUaaInventory.Run
    ///   -> PSXRacing_uaa_inventory.txt, Screenshots/uaa_inv/*.png
    /// </summary>
    public static class TrafficUaaInventory
    {
        const string Dir = "Assets/StoreAssets/UAAVehicles";
        const string OutFile = "PSXRacing_uaa_inventory.txt";
        const string ShotDir = "Screenshots/uaa_inv";

        public static void Run()
        {
            int code = 0;
            try { Dump(); }
            catch (System.Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static void Dump()
        {
            var sb = new StringBuilder();
            if (!AssetDatabase.IsValidFolder(Dir))
            {
                File.WriteAllText(OutFile, "NO PACK: " + Dir + " is missing (tools/store-assets.ps1)\n");
                return;
            }
            Directory.CreateDirectory(ShotDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // the model files' own meshes
            foreach (var fbx in new[] { Dir + "/CarPack.FBX", Dir + "/TrafficCars.FBX" })
            {
                var imp = AssetImporter.GetAtPath(fbx) as ModelImporter;
                sb.AppendLine($"FBX {fbx} globalScale={(imp != null ? imp.globalScale : -1f)} useFileScale={(imp != null && imp.useFileScale)} readable={(imp != null && imp.isReadable)}");
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
                    if (o is Mesh m)
                        sb.AppendLine($"  mesh {m.name,-12} tris={Tris(m),-5} verts={m.vertexCount,-5} subs={m.subMeshCount} size=({m.bounds.size.x:0.000} {m.bounds.size.y:0.000} {m.bounds.size.z:0.000}) centre=({m.bounds.center.x:0.000} {m.bounds.center.y:0.000} {m.bounds.center.z:0.000}) colours={m.HasVertexAttribute(VertexAttribute.Color)} uv2={m.HasVertexAttribute(VertexAttribute.TexCoord1)}");
            }
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { Dir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (t != null) sb.AppendLine($"TEX {Path.GetFileName(p),-18} {t.width}x{t.height} maxSize={(ti != null ? ti.maxTextureSize : 0)} alpha={(ti != null && ti.DoesSourceTextureHaveAlpha())}");
            }

            var cam = new GameObject("InvCam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.45f, 0.50f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 300f;
            var unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            var temp = new Dictionary<Material, Material>();

            var paths = new List<string>();
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { Dir }))
                paths.Add(AssetDatabase.GUIDToAssetPath(g));
            paths.Sort(System.StringComparer.OrdinalIgnoreCase);
            int n = 0;
            foreach (var path in paths)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (pf == null) { sb.AppendLine("PREFAB " + path + " did not load"); continue; }
                var go = Object.Instantiate(pf);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                string name = Path.GetFileNameWithoutExtension(path);
                var rends = go.GetComponentsInChildren<MeshRenderer>(true);
                var bounds = new Bounds(); bool any = false; int tris = 0;
                var lines = new List<string>();
                foreach (var r in rends)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    var m = mf != null ? mf.sharedMesh : null;
                    if (m == null) { lines.Add($"  part {Rel(go.transform, r.transform)} NO MESH"); continue; }
                    if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
                    tris += Tris(m);
                    var t = r.transform;
                    var mats = new StringBuilder();
                    foreach (var mat in r.sharedMaterials)
                    {
                        if (mat == null) { mats.Append(" [null]"); continue; }
                        var tex = mat.mainTexture;
                        Color c = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                        mats.Append($" [{mat.name} shader={mat.shader.name} tex={(tex != null ? tex.name : "none")} colour=({c.r:0.00} {c.g:0.00} {c.b:0.00})]");
                    }
                    lines.Add($"  part {Rel(go.transform, t),-22} mesh={m.name,-10} tris={Tris(m),-4} pos=({t.position.x:0.000} {t.position.y:0.000} {t.position.z:0.000}) rot=({t.eulerAngles.x:0} {t.eulerAngles.y:0} {t.eulerAngles.z:0}) scale=({t.lossyScale.x:0.000} {t.lossyScale.y:0.000} {t.lossyScale.z:0.000}) size=({r.bounds.size.x:0.00} {r.bounds.size.y:0.00} {r.bounds.size.z:0.00}) min=({r.bounds.min.x:0.00} {r.bounds.min.y:0.00} {r.bounds.min.z:0.00}){mats}");
                }
                var others = new Dictionary<string, int>();
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) { Inc(others, "MISSING SCRIPT"); continue; }
                    if (c is Transform || c is MeshFilter || c is MeshRenderer) continue;
                    Inc(others, c.GetType().Name);
                }
                var os = new StringBuilder();
                foreach (var kv in others) os.Append($" {kv.Key}x{kv.Value}");
                sb.AppendLine($"PREFAB {name} | parts={rends.Length} tris={tris} W={bounds.size.x:0.00} H={bounds.size.y:0.00} L={bounds.size.z:0.00} minY={bounds.min.y:0.00} centre=({bounds.center.x:0.00} {bounds.center.z:0.00}) other:{(os.Length > 0 ? os.ToString() : " none")}");
                foreach (var l in lines) sb.AppendLine(l);

                // two plain pictures: unlit, the pack's texture times its material colour
                foreach (var r in rends)
                {
                    var src = r.sharedMaterials;
                    var dst = new Material[src.Length];
                    for (int i = 0; i < src.Length; i++)
                    {
                        if (src[i] == null) continue;
                        if (!temp.TryGetValue(src[i], out var um))
                        {
                            um = new Material(unlit);
                            var tex = src[i].mainTexture;
                            if (um.HasProperty("_BaseMap")) um.SetTexture("_BaseMap", tex); else um.mainTexture = tex;
                            Color c = src[i].HasProperty("_Color") ? src[i].GetColor("_Color") : Color.white;
                            if (um.HasProperty("_BaseColor")) um.SetColor("_BaseColor", c);
                            temp[src[i]] = um;
                        }
                        dst[i] = um;
                    }
                    r.sharedMaterials = dst;
                }
                if (any)
                {
                    float rad = Mathf.Max(bounds.extents.magnitude, 1f);
                    string safe = name.Replace(' ', '_');
                    Shot(cam, safe + "_a", bounds.center + new Vector3(0.75f, 0.42f, 1f).normalized * rad * 3.6f, bounds.center);
                    Shot(cam, safe + "_b", bounds.center + new Vector3(-0.75f, 0.42f, -1f).normalized * rad * 3.6f, bounds.center);
                }
                Object.DestroyImmediate(go);
                n++;
            }
            sb.AppendLine($"DONE {n} prefabs");
            File.WriteAllText(OutFile, sb.ToString());
            Debug.Log("[UaaInventory] " + n + " prefabs -> " + OutFile);
        }

        static int Tris(Mesh m)
        {
            long idx = 0;
            for (int s = 0; s < m.subMeshCount; s++) idx += m.GetIndexCount(s);
            return (int)(idx / 3);
        }

        static void Inc(Dictionary<string, int> d, string k) { d.TryGetValue(k, out int v); d[k] = v + 1; }

        static string Rel(Transform root, Transform t)
        {
            string s = t.name;
            while (t.parent != null && t.parent != root) { t = t.parent; s = t.name + "/" + s; }
            return t == root ? "(root)" : s;
        }

        static void Shot(Camera cam, string name, Vector3 pos, Vector3 lookAt)
        {
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos));
            var rt = new RenderTexture(480, 320, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var request = new RenderPipeline.StandardRequest();
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                request.destination = rt;
                RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
