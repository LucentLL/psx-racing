using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE UPTOWN BLOCK FACES (charlotte-uptown-street-dress, 2026-11-09): the
    /// road-level places the street dress was judged at, each shot from the
    /// same pose BEFORE (CityMeshes.StreetDressOn / SkybridgesOn off: the
    /// walks as they were) and AFTER, in one job, so a triple with the real
    /// place's photo can be laid side by side (tools/city/uptown_sheet.py).
    /// A spot is a place and a compass heading only - the lat/lon and heading
    /// a public street-level frame was looked at from; nothing of that frame
    /// is in the repo. 960x540 PNGs (58 deg: the reference frames' 90 deg
    /// across) to Screenshots/City/uptown, with uptown_spots.txt: where each
    /// camera stood, the dress counts, the skybridge list (built / left out
    /// and why) and the cost figures:
    ///
    ///   COST. The densest Uptown site (the Square, Trade and Tryon) is stood
    ///   up as the game does (EnsureRing 2: 25 tiles) with the dress off and
    ///   on, five times each (A B A B ...): every mesh's triangles and renderers are
    ///   counted, and one 1920x1080 driver's-eye camera is rendered 240 times
    ///   with a pixel read back each time (the GPU must finish the frame) -
    ///   the median milliseconds, the best round of each reported (a shared machine). An editor batch number on this machine's
    ///   GPU, for the DIFFERENCE between the two, not a frame rate.
    ///
    /// Headless (with graphics): -executeMethod PSXRacing.EditorTools.CityRefSpots.RunUptown
    /// PSX_UPTOWN_ONLY=id,id: just those spots. PSX_UPTOWN_COST=0: no cost pass.
    /// </summary>
    public static partial class CityRefSpots
    {
        static readonly Spot[] UptownSpots =
        {
            new Spot("up_ctyd_3rd", Kind.StreetView, 35.22548, -80.84508, "3rd", 135f, "E 3rd St beside 237 S Tryon (the owner's photo), looking to College"),
            new Spot("up_s_tryon_200", Kind.StreetView, 35.22585, -80.84503, "Tryon", 50f, "S Tryon St, 3rd to 4th"),
            new Spot("up_s_tryon_100", Kind.StreetView, 35.22660, -80.84395, "Tryon", 50f, "S Tryon St, 4th to Trade"),
            new Spot("up_n_tryon_100", Kind.StreetView, 35.22745, -80.84275, "Tryon", 50f, "N Tryon St, Trade to 5th"),
            new Spot("up_n_tryon_300", Kind.StreetView, 35.22930, -80.84010, "Tryon", 230f, "N Tryon St, 7th back to 6th"),
            new Spot("up_e_trade", Kind.StreetView, 35.22690, -80.84272, "Trade", 140f, "E Trade St, Tryon to College (a skybridge)"),
            new Spot("up_w_trade", Kind.StreetView, 35.22750, -80.84345, "Trade", 320f, "W Trade St, Tryon to Church"),
            new Spot("up_s_college", Kind.StreetView, 35.22525, -80.84345, "College", 230f, "S College St, 4th to 3rd (a skybridge)"),
            new Spot("up_n_college", Kind.StreetView, 35.22735, -80.84045, "College", 50f, "N College St, 5th to 6th"),
            new Spot("up_s_church", Kind.StreetView, 35.22705, -80.84573, "Church", 230f, "S Church St, 4th to 3rd"),
            new Spot("up_e_4th", Kind.StreetView, 35.22610, -80.84385, "4th", 140f, "E 4th St, Tryon to College (a skybridge)"),
            new Spot("up_w_4th", Kind.StreetView, 35.22675, -80.84465, "4th", 320f, "W 4th St, Tryon to Church (a skybridge)"),
            new Spot("up_e_5th", Kind.StreetView, 35.22765, -80.84150, "5th", 140f, "E 5th St, Tryon to College (a skybridge)"),
            new Spot("up_w_6th", Kind.StreetView, 35.22915, -80.84130, "6th", 320f, "W 6th St, Tryon to Church"),
            new Spot("up_e_mlk", Kind.StreetView, 35.22455, -80.84595, "Martin Luther King", 140f, "E MLK Blvd, Tryon to College (a skybridge)"),
            new Spot("up_stonewall", Kind.StreetView, 35.22310, -80.84810, "Brooklyn Village", 140f, "E Brooklyn Village Ave (Stonewall), Tryon to College"),
            new Spot("up_s_tryon_400", Kind.StreetView, 35.22460, -80.84690, "Tryon", 230f, "S Tryon St, MLK to Levine"),
            // the ground floors' round (charlotte-ground-floors, 2026-11-10): Church, College, Brevard, Brooklyn Village by the loop, Trade's far blocks, the Square
            new Spot("up_square", Kind.StreetView, 35.22700, -80.84338, "Tryon", 50f, "The Square (Trade and Tryon) from S Tryon St"),
            new Spot("up_n_church_500", Kind.StreetView, 35.22932, -80.84247, "Church", 50f, "N Church St, 5th to 6th"),
            new Spot("up_s_church_mlk", Kind.StreetView, 35.22623, -80.84692, "Church", 50f, "S Church St, MLK to 3rd"),
            new Spot("up_s_college_mlk", Kind.StreetView, 35.22325, -80.84636, "College", 50f, "S College St, Brooklyn Village to MLK"),
            new Spot("up_n_college_700", Kind.StreetView, 35.22896, -80.83818, "College", 50f, "N College St, 7th to 8th"),
            new Spot("up_s_brevard_3rd", Kind.StreetView, 35.22265, -80.84282, "Brevard", 50f, "S Brevard St, MLK to 3rd"),
            new Spot("up_n_brevard_600", Kind.StreetView, 35.22654, -80.83725, "Brevard", 50f, "N Brevard St, 6th to 7th"),
            new Spot("up_stonewall_e", Kind.StreetView, 35.22165, -80.84647, "Brooklyn Village", 140f, "E Brooklyn Village Ave by the I-277 loop, College to Brevard"),
            new Spot("up_e_trade_brevard", Kind.StreetView, 35.22540, -80.84101, "Trade", 140f, "E Trade St, College to Brevard (under the rail bridge)"),
            new Spot("up_w_trade_church", Kind.StreetView, 35.22855, -80.84464, "Trade", 320f, "W Trade St, Church to Poplar"),
            // the owner's skyline photos (day and night): the ramp signed Sixth St / McDowell St (OSM way 159022597, a motorway_link
            // whose destination names them), its eye raised as the photographer's is
            new Spot("up_ramp_6th", Kind.StreetView, 35.22158, -80.83060, "", 287f, "The skyline from the Sixth St / McDowell St ramp (the owner's photos)"),
        };

        const int UpW = 960, UpH = 540;

        /// <summary>BEFORE: the city as origin/main draws it (the street dress and the skybridges on, as live);
        /// AFTER: with this round's ground floors, street furniture and dress corrections.</summary>
        static void UptownDress(bool on) { CityMeshes.GroundFloorsOn = on; CityMeshes.StreetFurnitureOn = on; CityMeshes.DressFixOn = on; }

        public static void RunUptown()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[CityRefSpots] no city data"); return; }
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "City", "uptown");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
            PSXRacingBuilder.EnsureCityTextures();
            Shader.SetGlobalFloat("_PSXFogNear", 300f);
            Shader.SetGlobalFloat("_PSXFogFar", FarM);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));

            bool gfWas = CityMeshes.GroundFloorsOn, furnWas = CityMeshes.StreetFurnitureOn, fixWas = CityMeshes.DressFixOn;
            var go = new GameObject("~cityUptownSpots");
            var world = go.AddComponent<CityWorld>();
            var log = new StringBuilder("id\tsnapped_to\teye_x\teye_y\teye_z\thdg\twhat\n");
            string only = System.Environment.GetEnvironmentVariable("PSX_UPTOWN_ONLY");
            int shots = 0;
            try
            {
                CityMeshes.DressStats.Reset(); CityTrees.StreetPlantedTotal = CityTrees.StreetNoFitTotal = 0;
                CityMeshes.GroundStats.Reset(); CityMeshes.FurnitureStats.Reset();
                foreach (var sp in UptownSpots)
                {
                    if (!string.IsNullOrEmpty(only) && System.Array.IndexOf(only.Split(','), sp.id) < 0) continue;
                    if (!Snap(map, LL(sp.lat, sp.lon), sp.road, out var e, out float s, out bool named))
                    { Debug.LogWarning($"[CityRefSpots] {sp.id}: no road near"); continue; }
                    var p = e.PointAt(s);
                    float hr = sp.hdg * Mathf.Deg2Rad;
                    var eye = new Vector3(p.x, e.YAt(s) + EyeM, p.y);
                    var look = eye + new Vector3(Mathf.Sin(hr), 0f, Mathf.Cos(hr)) * 50f + Vector3.down * 1.0f;
                    if (sp.id.StartsWith("up_ramp")) { eye.y += 2.6f; look = eye + new Vector3(Mathf.Sin(hr), 0f, Mathf.Cos(hr)) * 50f + Vector3.up * 5.0f; }
                    for (int pass = 0; pass < 2; pass++)
                    {
                        UptownDress(pass == 1);
                        world.EnsureRing(eye, 2);
                        Shoot(dir, sp.id + (pass == 0 ? "_before" : "_after"), eye, Quaternion.LookRotation(look - eye), 0f, UpW, UpH);
                        shots++;
                        world.DropAll();
                    }
                    log.Append($"{sp.id}\te{e.index} '{e.name}' s={s:0}{(named ? "" : " (NOT the named road)")}\t{eye.x:0.0}\t{eye.y:0.00}\t{eye.z:0.0}\t{sp.hdg:0}\t{sp.what}\n");
                }
                log.Append("\n" + CityMeshes.DressStats.Line() + $"; street trees planted {CityTrees.StreetPlantedTotal}, grates left empty (no crown fits clear of the lane) {CityTrees.StreetNoFitTotal} (the after passes of the spots above, tiles counted once a spot){(CityTrees.StreetNoFitWhy.Length > 0 ? "; first empty:" + CityTrees.StreetNoFitWhy : "")}\n");
                log.Append("\n" + CityMeshes.GroundStats.Line() + " (the after passes of the spots above)\n");
                log.Append(CityMeshes.FurnitureStats.Line() + "\n");
                UptownDress(true);
                log.Append("\n" + CityMeshes.GroundFloorReport(map));
                log.Append("\n" + CityMeshes.SkybridgeReport(map));
                if (System.Environment.GetEnvironmentVariable("PSX_UPTOWN_COST") != "0") log.Append("\n" + UptownCost(map, world));
            }
            finally
            {
                CityMeshes.GroundFloorsOn = gfWas; CityMeshes.StreetFurnitureOn = furnWas; CityMeshes.DressFixOn = fixWas;
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
            }
            File.WriteAllText(Path.Combine(dir, "uptown_spots.txt"), log.ToString());
            Debug.Log($"[CityRefSpots] uptown: {shots} shots to {dir}");
        }

        /// <summary>The cost pass (the class comment): the Square, dress off and on, A B A B.</summary>
        static string UptownCost(CityMap map, CityWorld world)
        {
            var sb = new StringBuilder("COST at the Square (Trade and Tryon), EnsureRing 2 (25 tiles), editor batch, 1920x1080, 240 renders each, a pixel read back a frame:\n");
            var at = LL(35.22721, -80.84309);
            if (!Snap(map, at, "Tryon", out var e, out float s, out _)) return "COST: no road at the Square\n";
            var p = e.PointAt(s);
            var eye = new Vector3(p.x, e.YAt(s) + EyeM, p.y);
            float hr = 50f * Mathf.Deg2Rad;
            var rot = Quaternion.LookRotation(new Vector3(Mathf.Sin(hr), -0.02f, Mathf.Cos(hr)));
            var med = new float[] { float.MaxValue, float.MaxValue }; var tris = new long[2]; var rends = new int[2]; var build = new float[] { float.MaxValue, float.MaxValue };
            const int Rounds = 5;
            for (int round = 0; round < Rounds; round++)
                for (int pass = 0; pass < 2; pass++)
                {
                    UptownDress(pass == 1);
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    world.EnsureRing(eye, 2);
                    float buildMs = (float)clock.Elapsed.TotalMilliseconds;
                    long t = 0; int r = 0;
                    foreach (var mf in world.GetComponentsInChildren<MeshFilter>(false))
                    {
                        var mr = mf.GetComponent<MeshRenderer>();
                        if (mf.sharedMesh == null || mr == null || !mr.enabled) continue;
                        r++;
                        for (int sm = 0; sm < mf.sharedMesh.subMeshCount; sm++) t += mf.sharedMesh.GetIndexCount(sm) / 3;
                    }
                    float m = RenderMedianMs(eye, rot, 240);
                    sb.Append($"  round {round + 1} ground floors + furniture {(pass == 1 ? "ON " : "off")}: {t} triangles in {r} renderers, ring build {buildMs:0} ms, render median {m:0.000} ms\n");
                    med[pass] = Mathf.Min(med[pass], m); build[pass] = Mathf.Min(build[pass], buildMs); tris[pass] = t; rends[pass] = r;
                    world.DropAll();
                }
            sb.Append($"  best of {Rounds} rounds each (the machine is shared: the least-disturbed round): triangles {tris[0]} -> {tris[1]} ({(tris[1] - tris[0]) * 100f / Mathf.Max(1, tris[0]):+0.0;-0.0}%), renderers {rends[0]} -> {rends[1]}, render median {med[0]:0.000} -> {med[1]:0.000} ms, ring build {build[0]:0} -> {build[1]:0} ms\n");
            return sb.ToString();
        }

        static float RenderMedianMs(Vector3 pos, Quaternion rot, int frames)
        {
            var camGO = new GameObject("~costCam");
            var cam = camGO.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.72f, 0.78f, 0.86f);
            cam.nearClipPlane = 0.3f; cam.farClipPlane = FarM; cam.fieldOfView = FovDeg;
            var rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt;
            var tex = new Texture2D(1, 1, TextureFormat.RGB24, false);
            var ms = new List<float>(frames);
            for (int i = 0; i < frames + 20; i++)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(960, 540, 1, 1), 0, 0);
                RenderTexture.active = null;
                if (i >= 20) ms.Add((float)clock.Elapsed.TotalMilliseconds);
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(tex); Object.DestroyImmediate(rt); Object.DestroyImmediate(camGO);
            ms.Sort();
            return ms[ms.Count / 2];
        }
    }
}
