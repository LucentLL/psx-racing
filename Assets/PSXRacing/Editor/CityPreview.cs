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
    /// (a floating road, a black facade, a deck with no piers, a gore that
    /// missed its mainline) is visual and silent. Builds a ring of real tiles
    /// at a handful of probe spots, shoots each top-down and at street level,
    /// and tears it all down.
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

            var probes = new List<(string name, Vector2 at, int ring)>
            {
                ("uptown", map.uptown, 1),
            };
            // a freeway-over-freeway viaduct, a street over a trenched
            // freeway, a water bridge, a ramp gore, I-485
            for (int i = 0; i < map.crossings.Length; i++)
            {
                var c = map.crossings[i];
                var o = map.edges[c.over]; var u = map.edges[c.under];
                if (o.cls >= 5 && !o.link && u.cls >= 5 && !u.link) { probes.Add(("overpass", c.at, 1)); break; }
            }
            for (int i = 0; i < map.crossings.Length; i++)
                if (CityElevation.TrenchedCrossings != null && CityElevation.TrenchedCrossings[i])
                {
                    var c = map.crossings[i];
                    if (Vector2.Distance(c.at, map.uptown) < 1500f) { probes.Add(("trench", c.at, 1)); break; }
                }
            if (map.wspans.Length > 0)
            {
                var ws = map.wspans[map.wspans.Length / 2];
                var e = map.edges[ws.edge];
                probes.Add(("bridge", e.PointAt((ws.s0 + ws.s1) * 0.5f), 1));
            }
            foreach (var e in map.edges)
                if (e.link && e.cls >= 5 && Vector2.Distance(map.nodes[e.b], map.uptown) > 3000f)
                { probes.Add(("gore", map.nodes[e.b], 1)); break; }
            foreach (var e in map.edges)
                if (e.name == "I-485" && !e.link && e.length > 300f) { probes.Add(("i485", e.PointAt(e.length * 0.5f), 1)); break; }

            PSXRacingBuilder.EnsureCityTextures();
            var trims = CityMeshes.NodeTrims(map);
            var buildings = CityBuildings.Precompute(map);

            // The prop lots are the thing most likely to be silently wrong (a
            // floating house, a restaurant in a junction): photograph a
            // drive-thru, the housiest prefab suburb, a real-footprint
            // neighbourhood and a filled block on every run.
            Vector2? burgerAt = null, pizzaAt = null, suburbAt = null;
            int bestHouses = 0;
            foreach (var kv in buildings)
            {
                int houses = 0;
                Vector2 first = Vector2.zero;
                foreach (var b in kv.Value)
                {
                    if (b.kind == CityProps.Burger && burgerAt == null) burgerAt = b.pos;
                    if (b.kind == CityProps.Pizzeria && pizzaAt == null) pizzaAt = b.pos;
                    if (b.kind == CityProps.House) { houses++; first = b.pos; }
                }
                if (houses > bestHouses) { bestHouses = houses; suburbAt = first; }
            }
            if (suburbAt.HasValue) probes.Add(("suburb", suburbAt.Value, 1));
            // a footprint neighbourhood: the tile with the most gabled footprints
            int bestGables = 0; Vector2 gableAt = map.uptown;
            var gableCount = new Dictionary<long, (int n, Vector2 at)>();
            foreach (var f in map.footprints)
            {
                if (!f.gable) continue;
                long k = ((long)Mathf.FloorToInt(f.centre.x / 256f) << 24) ^ (Mathf.FloorToInt(f.centre.y / 256f) & 0xFFFFFF);
                gableCount.TryGetValue(k, out var cur);
                gableCount[k] = (cur.n + 1, f.centre);
            }
            foreach (var kv in gableCount) if (kv.Value.n > bestGables) { bestGables = kv.Value.n; gableAt = kv.Value.at; }
            if (bestGables > 0) probes.Add(("footprints", gableAt, 1));
            // a filled block outside the footprint data
            foreach (var e in map.edges)
            {
                if (e.cls != 2 || e.link) continue;
                var p = e.PointAt(e.length * 0.5f);
                float d = Vector2.Distance(p, map.uptown);
                if (d < 9000f || d > 12000f || map.footprintBounds.Contains(p)) continue;
                probes.Add(("interior", p, 1)); break;
            }
            if (burgerAt.HasValue) probes.Add(("burger", burgerAt.Value, 1));
            if (pizzaAt.HasValue) probes.Add(("pizzeria", pizzaAt.Value, 1));
            // the skyline: uptown from the south, with a bigger ring so the
            // towers stand in a city and not on an island
            probes.Add(("skyline", map.uptown, 2));

            // PSX/Lit reads global fog + snap; give it a daylight look
            Shader.SetGlobalFloat("_PSXFogNear", 900f);
            Shader.SetGlobalFloat("_PSXFogFar", 2000f);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));

            var world = new GameObject("~CityPreviewWorld");
            var roots = new List<GameObject> { world };

            try
            {
                foreach (var (name, at, ring) in probes)
                {
                    var tiles = BuildRing(map, trims, buildings, world, at, ring, out var stats);
                    roots.AddRange(tiles);
                    Debug.Log($"[CityPreview] {name}: {stats}");

                    float midY = map.NearestRoadPoint(at, 300f, false, out int ei, out float s, out _)
                        ? map.edges[ei].YAt(s) : CityElevation.BaseY(at.x, at.y);

                    if (name == "skyline")
                    {
                        Shoot(dir, "skyline", new Vector3(at.x - 120f, midY + 28f, at.y - 620f),
                              Quaternion.LookRotation(new Vector3(0.18f, 0.04f, 1f)), ortho: 0f, far: 2500f);
                    }
                    else
                    {
                        Shoot(dir, name + "_top",
                            new Vector3(at.x, midY + 260f, at.y),
                            Quaternion.Euler(90f, 0f, 0f), ortho: 200f);
                        Shoot(dir, name + "_street",
                            new Vector3(at.x - 40f, midY + 6f, at.y - 40f),
                            Quaternion.LookRotation(new Vector3(1f, -0.08f, 1f)), ortho: 0f);
                    }

                    foreach (var t in tiles) Object.DestroyImmediate(t);
                    roots.RemoveAll(r => r == null);
                }
                Debug.Log($"[CityPreview] wrote shots for {probes.Count} probes to {dir}");
            }
            finally
            {
                foreach (var r in roots) if (r != null) Object.DestroyImmediate(r);
            }
        }

        static List<GameObject> BuildRing(CityMap map, float[] trims,
            Dictionary<long, List<CityBuildings.B>> buildings, GameObject parent, Vector2 at, int ring,
            out string stats)
        {
            var made = new List<GameObject>();
            int ptx = Mathf.FloorToInt(at.x / CityMeshes.TileSize);
            int ptz = Mathf.FloorToInt(at.y / CityMeshes.TileSize);
            var mats = CityMaterialsForPreview();
            int roadV = 0, bldV = 0, foot = 0, houses = 0, gores = 0, facing = 0, barrierV = 0;
            for (int dz = -ring; dz <= ring; dz++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    var tm = CityMeshes.Build(map, trims, buildings, ptx + dx, ptz + dz);
                    var root = new GameObject($"~tile_{ptx + dx}_{ptz + dz}");
                    root.transform.SetParent(parent.transform, false);
                    root.transform.position = tm.origin;
                    Wrap(root, tm.ground, mats, tm.groundSlots);
                    Wrap(root, tm.roads, mats, tm.roadSlots);
                    Wrap(root, tm.barriers, mats, new[] { CityMeshes.Slot.Concrete });
                    Wrap(root, tm.water, mats, new[] { CityMeshes.Slot.Water });
                    Wrap(root, tm.buildings, mats, tm.buildingSlots);
                    roadV += tm.roads != null ? tm.roads.vertexCount : 0;
                    bldV += tm.buildings != null ? tm.buildings.vertexCount : 0;
                    barrierV += tm.barriers != null ? tm.barriers.vertexCount : 0;
                    foot += tm.footprintCount; houses += tm.houseCount; gores += tm.goreCount;
                    facing += tm.wallFacingErrors;

                    // the prop lots, exactly as CityWorld stands them up
                    long key = ((long)(ptx + dx) << 24) ^ ((ptz + dz) & 0xFFFFFF);
                    if (buildings.TryGetValue(key, out var lots))
                        foreach (var b in lots)
                        {
                            if (b.kind == 0) continue;
                            var prefab = CityProps.Prefab(b.kind);
                            if (prefab == null) continue;
                            var def = CityProps.Defs[b.kind];
                            var inst = (GameObject)Object.Instantiate(prefab, root.transform);
                            inst.transform.position = new Vector3(b.pos.x,
                                CityBuildings.SeatY(map, b.pos, b.w, b.d, b.yaw) - def.sink, b.pos.y);
                            inst.transform.rotation = Quaternion.Euler(
                                0f, b.yaw * Mathf.Rad2Deg + def.yawOffsetDeg, 0f);
                            if (b.scale.sqrMagnitude > 0.01f) inst.transform.localScale = b.scale;
                        }
                    made.Add(root);
                }
            stats = $"{made.Count} tiles, {roadV} road verts, {bldV} building verts, {barrierV} barrier verts, " +
                    $"{foot} footprints, {houses} filled houses, {gores} gores, {facing} facing errors";
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

        /// <summary>The materials the GAME uses, not a copy of them.</summary>
        static Material[] CityMaterialsForPreview() =>
            PSXRacingBuilder.CityMaterials();

        static void Shoot(string dir, string name, Vector3 pos, Quaternion rot, float ortho, float far = 3000f)
        {
            var camGO = new GameObject("~previewCam");
            var cam = camGO.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.72f, 0.78f, 0.86f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = far;
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
