using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE MEDIAN DRESS'S COST AND CENSUS (2026-11-09). Stands the 3 x 3 tiles
    /// round a place up through CityWorld (as the game does) and reports, per
    /// tile and in all: the island's top by kind (metres of half island:
    /// concrete, bed, grass), the plants (cards, vertices, triangles: one
    /// render-only mesh a tile), the ground mesh's submeshes (the bed is one
    /// more where a tile has one) - and the frame's cost: the driver's-eye
    /// view rendered <see cref="Frames"/> times with the plants' renderers on
    /// and <see cref="Frames"/> times with them off, each frame read back so
    /// the GPU has finished it (editor, 1280 x 720; a comparison, not a
    /// player's frame time).
    /// PSX_MED_PROBE=lat,lon,heading;... (default: Brookshire Blvd by N
    /// Crigler St, W.T. Harris Blvd). To PSXRacing_median_dress.txt.
    /// Headless: -executeMethod PSXRacing.EditorTools.MedianDressProbe.Run
    /// </summary>
    public static class MedianDressProbe
    {
        const int Frames = 60, W = 1280, H = 720;

        [MenuItem("PSX Racing/Probe Median Dress")]
        public static void Run()
        {
            var sb = new StringBuilder();
            string outPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PSXRacing_median_dress.txt");
            var map = CityMap.Get();
            if (map == null) { File.WriteAllText(outPath, "no city data\n"); return; }
            PSXRacingBuilder.EnsureCityTextures();
            Shader.SetGlobalFloat("_PSXFogNear", 300f);
            Shader.SetGlobalFloat("_PSXFogFar", 500f);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));
            string spec = System.Environment.GetEnvironmentVariable("PSX_MED_PROBE");
            if (string.IsNullOrWhiteSpace(spec)) spec = "35.26456,-80.88093,322;35.23020,-80.72833,304";
            sb.AppendLine("MEDIAN DRESS PROBE  " + CityMeshes.MedianDressLine());
            var go = new GameObject("~medianProbe");
            var world = go.AddComponent<CityWorld>();
            try
            {
                foreach (var part in spec.Split(';'))
                {
                    var f = part.Split(',');
                    if (f.Length < 3) continue;
                    double lat = double.Parse(f[0], System.Globalization.CultureInfo.InvariantCulture), lon = double.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture);
                    float hdg = float.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture);
                    var at = CityRefSpots.LL(lat, lon);
                    if (!map.NearestRoadPoint(at, 200f, false, out int ei, out float s, out _)) { sb.AppendLine($"{part}: no road near"); continue; }
                    var e = map.edges[ei];
                    var p = e.PointAt(s);
                    var eye = new Vector3(p.x, e.YAt(s) + 1.2f, p.y);
                    CityMeshes.MedianDressReset();
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    world.EnsureRing(eye, 1);
                    double buildMs = clock.Elapsed.TotalMilliseconds;
                    sb.AppendLine($"--- {part}: e{ei} '{e.name}' cls {e.cls} speed {e.speedKmh} at ({p.x:0},{p.y:0}); 3 x 3 tiles stood up in {buildMs:0} ms");
                    sb.AppendLine("  " + CityMeshes.MedianDressLine());
                    int plantR = 0, plantV = 0, plantT = 0, groundSub = 0, tiles = 0, bedTiles = 0, renderers = 0, colliders = 0;
                    var plants = new List<MeshRenderer>();
                    foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        renderers++;
                        var mf = mr.GetComponent<MeshFilter>();
                        if (mr.gameObject.name == "MedianPlants")
                        {
                            plants.Add(mr); plantR++;
                            if (mf != null && mf.sharedMesh != null) { plantV += mf.sharedMesh.vertexCount; plantT += (int)(mf.sharedMesh.GetIndexCount(0) / 3); }
                            if (mr.GetComponent<Collider>() != null) colliders++;
                            sb.AppendLine($"  plants on tile '{mr.transform.parent.name}': {(mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount / 8 : 0)} plants, {(mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : 0)} vertices, layer {mr.gameObject.layer}, material {(mr.sharedMaterial != null ? mr.sharedMaterial.name : "NONE")}");
                        }
                        if (mr.gameObject.name == "Ground" && mf != null && mf.sharedMesh != null)
                        {
                            tiles++; groundSub += mf.sharedMesh.subMeshCount;
                            foreach (var m in mr.sharedMaterials) if (m != null && m.name.StartsWith("CityMedianBed")) { bedTiles++; break; }
                        }
                    }
                    sb.AppendLine($"  {tiles} tiles, {renderers} renderers in all; ground submeshes {groundSub} ({bedTiles} tiles carry the bed's: +{bedTiles} draws); plant renderers {plantR} (+{plantR} draws), {plantV / 8} plants, {plantV} vertices, {plantT} triangles; colliders on plant objects {colliders}");
                    // the frame, plants on and off (alternating blocks, so drift falls on both)
                    float h = hdg * Mathf.Deg2Rad;
                    var rot = Quaternion.LookRotation(new Vector3(Mathf.Sin(h), 0f, Mathf.Cos(h)) * 50f + Vector3.down * 1.0f);
                    var camGO = new GameObject("~medCam");
                    var cam = camGO.AddComponent<Camera>();
                    cam.transform.SetPositionAndRotation(eye, rot);
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.72f, 0.78f, 0.86f);
                    cam.nearClipPlane = 0.3f; cam.farClipPlane = 500f; cam.fieldOfView = 58f;
                    var rt = new RenderTexture(W, H, 24);
                    cam.targetTexture = rt;
                    var tex = new Texture2D(8, 8, TextureFormat.RGB24, false);
                    double on = 0, off = 0; int nOn = 0, nOff = 0;
                    for (int block = 0; block < 8; block++)
                    {
                        bool show = (block & 1) == 0;
                        foreach (var mr in plants) mr.enabled = show;
                        for (int k = 0; k < Frames / 4 + 2; k++)
                        {
                            var c = System.Diagnostics.Stopwatch.StartNew();
                            cam.Render();
                            RenderTexture.active = rt;
                            tex.ReadPixels(new Rect(W / 2, H / 2, 8, 8), 0, 0);   // waits for the frame
                            RenderTexture.active = null;
                            double ms = c.Elapsed.TotalMilliseconds;
                            if (k < 2) continue;                                  // the block's first frames settle
                            if (show) { on += ms; nOn++; } else { off += ms; nOff++; }
                        }
                    }
                    foreach (var mr in plants) mr.enabled = true;
                    sb.AppendLine($"  frame at the driver's eye, {W} x {H}, editor: plants ON {on / Mathf.Max(1, nOn):0.000} ms ({nOn} frames), plants OFF {off / Mathf.Max(1, nOff):0.000} ms ({nOff} frames), difference {(on / Mathf.Max(1, nOn) - off / Mathf.Max(1, nOff)):+0.000;-0.000} ms");
                    cam.targetTexture = null;
                    Object.DestroyImmediate(tex); Object.DestroyImmediate(rt); Object.DestroyImmediate(camGO);
                    world.DropAll();
                }
            }
            catch (System.Exception ex) { sb.AppendLine("EXCEPTION " + ex); }
            finally
            {
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
            }
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("[MedianDressProbe]\n" + sb);
        }
    }
}
