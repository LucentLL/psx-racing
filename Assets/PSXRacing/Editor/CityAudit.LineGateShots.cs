using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LINE GATE'S PICTURES: for each place in a list, the tiles round it
    /// built as the game builds them (ground, roads, kerbs, barriers,
    /// buildings; the game's own materials) and two real editor frames:
    ///   _plan    straight down, the road running up the frame, 0.1 m a pixel
    ///            (128 x 72 m)
    ///   _drive   the driver's eye: 1.15 m over the lane the road's traffic
    ///            uses, 45 m before the place, looking along the road
    /// No trees, poles, signs or traffic are in these frames (they are not the
    /// road's paint); nothing is staged.
    ///   -executeMethod PSXRacing.EditorTools.CityAudit.RunLineGateShots   (no -nographics)
    ///   PSX_LG_SHOTS=file   rows "id x z edge s dir" (tab separated; dir +1 along a->b, -1 against;
    ///                       0 = the side of the road the place lies on); default city_linegate_shots.tsv
    /// Writes Screenshots\City\linegate\&lt;id&gt;_plan.png / _drive.png and linegate_shots.txt.
    /// </summary>
    public static partial class CityAudit
    {
        public static void RunLineGateShots()
        {
            var inv = CultureInfo.InvariantCulture;
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[LineGate] no city data"); return; }
            string root = Directory.GetParent(Application.dataPath).FullName;
            string dir = Path.Combine(root, "Screenshots", "City", "linegate");
            Directory.CreateDirectory(dir);
            string list = Environment.GetEnvironmentVariable("PSX_LG_SHOTS");
            if (string.IsNullOrWhiteSpace(list)) list = Path.Combine(root, "city_linegate_shots.tsv");
            if (!File.Exists(list)) { Debug.LogError("[LineGate] no shots list " + list); return; }
            PSXRacingBuilder.EnsureCityTextures();
            var t = CityMeshes.ComputeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            Shader.SetGlobalFloat("_PSXFogNear", 900f); Shader.SetGlobalFloat("_PSXFogFar", 2000f);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f)); Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));
            var mats = PSXRacingBuilder.CityMaterials();
            var world = new GameObject("~LineGateShots");
            var said = new StringBuilder("id\tedge\troad\tlatlon\teye\tyaw\n");
            var live = new Dictionary<long, GameObject>();
            int done = 0;
            try
            {
                foreach (var row in File.ReadAllLines(list))
                {
                    var c = row.Split('\t');
                    if (c.Length < 5 || c[0] == "id") continue;
                    string id = c[0];
                    float x = float.Parse(c[1], inv), z = float.Parse(c[2], inv);
                    int ei = int.Parse(c[3], inv); float s = float.Parse(c[4], inv);
                    int dirWant = c.Length > 5 ? int.Parse(c[5], inv) : 0;
                    if (ei < 0 || ei >= map.edges.Length) continue;
                    var e = map.edges[ei];
                    s = Mathf.Clamp(s, 0f, e.length);
                    // the tiles: a 3 x 3 ring round the place (kept while the next place uses them)
                    int ptx = Mathf.FloorToInt(x / CityMeshes.TileSize), ptz = Mathf.FloorToInt(z / CityMeshes.TileSize);
                    var keep = new HashSet<long>();
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            long k = ((long)(ptx + dx) << 32) ^ (uint)(ptz + dz);
                            keep.Add(k);
                            if (live.ContainsKey(k)) continue;
                            var tm = CityMeshes.Build(map, t, buildings, ptx + dx, ptz + dz);
                            var go = new GameObject($"~tile_{ptx + dx}_{ptz + dz}");
                            go.transform.SetParent(world.transform, false);
                            if (tm != null)
                            {
                                go.transform.position = tm.origin;
                                LgWrap(go, tm.ground, mats, tm.groundSlots);
                                LgWrap(go, tm.roads, mats, tm.roadSlots);
                                LgWrap(go, tm.barriers, mats, new[] { CityMeshes.Slot.Concrete });
                                LgWrap(go, tm.kerbs, mats, new[] { CityMeshes.Slot.Concrete });
                                LgWrap(go, tm.buildings, mats, tm.buildingSlots);
                            }
                            live[k] = go;
                        }
                    var drop = new List<long>();
                    foreach (var kv in live) if (!keep.Contains(kv.Key)) drop.Add(kv.Key);
                    foreach (long k in drop)
                    {
                        foreach (var mf in live[k].GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null) UnityEngine.Object.DestroyImmediate(mf.sharedMesh);
                        UnityEngine.Object.DestroyImmediate(live[k]); live.Remove(k);
                    }
                    // the direction: the side of the road the place lies on drives a->b on the right (minus) side
                    var tan = e.TangentAt(s); var pc = e.PointAt(s);
                    float latAt = (x - pc.x) * -tan.y + (z - pc.y) * tan.x;
                    int dirGo = dirWant != 0 ? dirWant : e.oneway ? 1 : latAt > 0.3f ? -1 : 1;
                    // walk 45 m back along the road (through mitred joints)
                    var ce = e; float cs = s; int cd = dirGo; float left = 45f;
                    for (int guard = 0; guard < 8 && left > 0.01f; guard++)
                    {
                        float room = cd > 0 ? cs : ce.length - cs;
                        if (room >= left) { cs += cd > 0 ? -left : left; left = 0f; break; }
                        left -= room; cs = cd > 0 ? 0f : ce.length;
                        int node = cd > 0 ? ce.a : ce.b;
                        if (!t.mitre[node]) break;
                        int o = t.throughA[node] == ce.index ? t.throughB[node] : t.throughB[node] == ce.index ? t.throughA[node] : -1;
                        if (o < 0 || o == ce.index) break;
                        var oe = map.edges[o];
                        // we arrive at the node; the travel direction on the next edge runs TOWARD this node
                        cd = oe.b == node ? 1 : -1; cs = oe.b == node ? oe.length : 0f; ce = oe;
                    }
                    var ct = ce.TangentAt(cs) * cd; var cp = ce.PointAt(cs);
                    // the lane the traffic uses: right of the centre on a two-way, the middle of a one-way
                    LineModel.Extents(ce, cs, out float eM, out float eP);
                    float lane = ce.oneway ? 0.5f * (eP - eM) : (cd > 0 ? -Mathf.Min(1.9f, 0.5f * eM) : Mathf.Min(1.9f, 0.5f * eP));
                    var leftN = new Vector2(-ce.TangentAt(cs).y, ce.TangentAt(cs).x);
                    var eye2 = cp + leftN * lane;
                    float ey = ce.YAt(cs) + 1.15f;
                    var eye = new Vector3(eye2.x, ey, eye2.y);
                    var fwd = new Vector3(ct.x, 0f, ct.y);
                    float ty = e.YAt(s);
                    var target = new Vector3(x, ty, z);
                    // look along the road, pitched to the place's height 45 m on
                    var aim = eye + fwd * 45f; aim.y = ty + 0.6f;
                    LgShoot(dir, id + "_drive", eye, Quaternion.LookRotation(aim - eye), 0f, 1280, 720, 60f);
                    var upDir = new Vector3(tan.x * dirGo, 0f, tan.y * dirGo);
                    LgShoot(dir, id + "_plan", target + Vector3.up * 80f, Quaternion.LookRotation(Vector3.down, upDir), 36f, 1280, 720, 60f);
                    said.AppendLine($"{id}\t{ei}\t{LgRoad(e)}\t{LatLon(x, z)}\t{eye.x.ToString("0.0", inv)},{eye.y.ToString("0.00", inv)},{eye.z.ToString("0.0", inv)}\t{(Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg).ToString("0", inv)}");
                    done++;
                    if (done % 10 == 0) Debug.Log($"[LineGate] {done} places shot");
                }
            }
            finally
            {
                foreach (var kv in live)
                    foreach (var mf in kv.Value.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null) UnityEngine.Object.DestroyImmediate(mf.sharedMesh);
                UnityEngine.Object.DestroyImmediate(world);
            }
            File.WriteAllText(Path.Combine(dir, "linegate_shots.txt"), said.ToString());
            Debug.Log($"[LineGate] DONE {done} places shot to {dir}");
        }

        static void LgWrap(GameObject parent, Mesh mesh, Material[] mats, CityMeshes.Slot[] slots)
        {
            if (mesh == null || slots == null) return;
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var use = new Material[slots.Length];
            for (int i = 0; i < slots.Length; i++) use[i] = mats[(int)slots[i]];
            mr.sharedMaterials = use;
        }

        static void LgShoot(string dir, string name, Vector3 pos, Quaternion rot, float ortho, int w, int h, float fov)
        {
            var camGO = new GameObject("~lgCam");
            var cam = camGO.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.72f, 0.78f, 0.86f);
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 3000f; cam.fieldOfView = fov;
            if (ortho > 0f) { cam.orthographic = true; cam.orthographicSize = ortho; }
            var rt = new RenderTexture(w, h, 24) { filterMode = FilterMode.Bilinear, antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(camGO);
        }
    }
}
