using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using PSXRacing;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Photographs the streamed city without play mode — the runtime-built
    /// world is invisible until it is rendered, and every failure mode here
    /// (a floating road, a black facade, a deck with no piers) is visual and
    /// silent. Builds a 3x3 ring of real tiles at a handful of probe spots,
    /// shoots each top-down and at street level, and tears it all down.
    ///
    /// Menu: PSX Racing/Preview Charlotte. Headless: -executeMethod
    /// PSXRacing.EditorTools.CityPreview.Run — PNGs land in Screenshots/City.
    /// </summary>
    public static class CityPreview
    {
        [MenuItem("PSX Racing/Preview Charlotte")]
        public static void Run()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[CityPreview] no city data"); return; }

            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "Screenshots", "City");
            Directory.CreateDirectory(dir);

            var probes = new List<(string name, Vector2 at)>
            {
                ("uptown", map.uptown),
            };
            // the widest grade separation and a water span make the two rules
            // photographable
            if (map.crossings.Length > 0)
            {
                var c = map.crossings[map.crossings.Length / 3];
                probes.Add(("overpass", c.at));
            }
            if (map.wspans.Length > 0)
            {
                var ws = map.wspans[map.wspans.Length / 2];
                var e = map.edges[ws.edge];
                probes.Add(("bridge", e.PointAt((ws.s0 + ws.s1) * 0.5f)));
            }
            // somewhere on I-485 for the freeway look
            foreach (var e in map.edges)
                if (e.name == "I-485") { probes.Add(("i485", e.PointAt(e.length * 0.5f))); break; }

            var trims = CityMeshes.NodeTrims(map);
            var buildings = CityBuildings.Precompute(map);

            // PSX/Lit reads global fog + snap; give it a daylight look
            Shader.SetGlobalFloat("_PSXFogNear", 900f);
            Shader.SetGlobalFloat("_PSXFogFar", 2000f);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            // direction TO the light (see PSXLit.shader) — an up-ish vector, or
            // every surface reads ambient-only and the shot looks overcast
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));

            var world = TempWorld(map);
            var roots = new List<GameObject> { world };

            try
            {
                foreach (var (name, at) in probes)
                {
                    var tiles = BuildRing(map, trims, buildings, world, at);
                    roots.AddRange(tiles);

                    float midY = map.NearestRoadPoint(at, 300f, false, out int ei, out float s, out _)
                        ? map.edges[ei].YAt(s) : CityElevation.BaseY(at.x, at.y);

                    Shoot(dir, name + "_top",
                        new Vector3(at.x, midY + 260f, at.y),
                        Quaternion.Euler(90f, 0f, 0f), ortho: 200f);
                    Shoot(dir, name + "_street",
                        new Vector3(at.x - 40f, midY + 6f, at.y - 40f),
                        Quaternion.LookRotation(new Vector3(1f, -0.08f, 1f)), ortho: 0f);

                    foreach (var t in tiles) Object.DestroyImmediate(t);
                    roots.RemoveAll(r => r == null);
                }
                Debug.Log($"[CityPreview] wrote {probes.Count * 2} shots to {dir}");
            }
            finally
            {
                foreach (var r in roots) if (r != null) Object.DestroyImmediate(r);
            }
        }

        static GameObject TempWorld(CityMap map)
        {
            var go = new GameObject("~CityPreviewWorld");
            return go;
        }

        static List<GameObject> BuildRing(CityMap map, float[] trims,
            Dictionary<long, List<CityBuildings.B>> buildings, GameObject parent, Vector2 at)
        {
            var made = new List<GameObject>();
            int ptx = Mathf.FloorToInt(at.x / CityMeshes.TileSize);
            int ptz = Mathf.FloorToInt(at.y / CityMeshes.TileSize);
            var mats = CityMaterialsForPreview();
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var tm = CityMeshes.Build(map, trims, buildings, ptx + dx, ptz + dz);
                    var root = new GameObject($"~tile_{ptx + dx}_{ptz + dz}");
                    root.transform.SetParent(parent.transform, false);
                    root.transform.position = tm.origin;
                    Wrap(root, tm.ground, mats, new[] { CityMeshes.Slot.Ground });
                    Wrap(root, tm.roads, mats, tm.roadSlots);
                    Wrap(root, tm.water, mats, new[] { CityMeshes.Slot.Water });
                    Wrap(root, tm.buildings, mats, tm.buildingSlots);
                    made.Add(root);
                }
            return made;
        }

        static void Wrap(GameObject parent, Mesh mesh, Material[] mats, CityMeshes.Slot[] slots)
        {
            if (mesh == null) return;
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var use = new Material[slots.Length];
            for (int i = 0; i < slots.Length; i++) use[i] = mats[(int)slots[i]];
            mr.sharedMaterials = use;
        }

        static Material[] CityMaterialsForPreview()
        {
            // load the baked material assets straight from the project
            string MatPath(string n) => "Assets/PSXRacing/Materials/" + n + ".mat";
            var names = new[]
            {
                "CityGround", "CityRoadMinor", "CityRoadMajor", "CityRoadDivG", "CityRoadDivA",
                "CityMotorway", "CityRamp", "CityJunction", "CityConcrete", "CityWater",
                "CityFacadeTower", "CityFacadeMid", "CityFacadeBrick", "CityShops",
            };
            var mats = new Material[(int)CityMeshes.Slot.COUNT];
            for (int i = 0; i < names.Length; i++)
                mats[i] = AssetDatabase.LoadAssetAtPath<Material>(MatPath(names[i]));
            return mats;
        }

        static void Shoot(string dir, string name, Vector3 pos, Quaternion rot, float ortho)
        {
            var camGO = new GameObject("~previewCam");
            var cam = camGO.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.72f, 0.78f, 0.86f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 3000f;
            cam.fieldOfView = 60f;
            if (ortho > 0f) { cam.orthographic = true; cam.orthographicSize = ortho; }

            var rt = new RenderTexture(960, 540, 24);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGO);
        }
    }
}
