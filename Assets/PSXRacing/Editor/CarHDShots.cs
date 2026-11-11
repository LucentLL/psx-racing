using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE HD CAR TIER'S PROOF (CarHD): the owner's frame rebuilt - a car on
    /// the seller's drive (SellerLot) from the rear three-quarter in daylight -
    /// plus the chase camera's distance and a 30 m view, each drawn with the
    /// tier off (the 512 point-sampled sheet, as the game ships) and on, at
    /// the PC profile's picture (1920 x 1080, 8-bit + FXAA, graded). With each
    /// view a MASK pass (PSX/CarPaint's debug 5 over a seam mask cut from the
    /// HD sheet, point sampled) says which pixels land on a shut line, so
    /// tools/carmodels/carhd_sheet.py can measure a line's contrast against
    /// the paint around it at 5 / 15 / 40 m. Also: the FD at dusk, at night
    /// with its lamps lit and in the rain; the older cars' sheets point vs a
    /// mipped trilinear copy (CarHD.SmoothSheets); the tangent frame of every
    /// body checked on the CPU; a crude 8-car cost probe.
    ///
    /// Batch: -executeMethod PSXRacing.EditorTools.CarHDShots.Run, with
    /// PSX_CARHD_MASKS = the folder of mask_&lt;body&gt;__&lt;sheet&gt;.png. Writes
    /// Screenshots/carhd/*.png and carhd_shots.txt.
    /// </summary>
    public static class CarHDShots
    {
        static readonly (string key, string sheet)[] Cars =
        {
            ("rx7_fd_93", "fd_shared_512x256"),
            ("viper_gts", "coupe_96_red_512x256"),
            ("flatsix_coupe", "flatsix_coupe_blue_512x256"),
            ("rally_sedan_99", "rally_sedan_99_blue_512x256"),
            ("civic_eg", "hatch_93_red_512x256"),
            ("roadster_99", "roadster_99_red_512x256"),
        };
        static readonly string[] OldCars = { "gtr_99", "charger_69" };
        const int W = 1920, H = 1080;
        const float Fov = 50f;

        static string outDir;
        static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();
        static Camera cam;
        static Light sun;
        static Transform spot;

        /// <summary>Batch: re-bake ONLY the nine bodies with an HD set (wheel
        /// tangents, the keyword never saved), import the staged sets, and the
        /// bundle report. Writes Screenshots/carhd_bake.txt.</summary>
        public static void BakeImport()
        {
            var sb = new System.Text.StringBuilder();
            var keys = new HashSet<string> { "rx7_fd_93", "rx7_fd_99", "rx7_fd_nowing", "viper_gts", "flatsix_coupe", "flatsix_coupe_87", "rally_sedan_99", "civic_eg", "roadster_99" };
            try
            {
                CarModelBaker.BakeKeys(keys);
                foreach (var l in CarModelBaker.LastLog) sb.AppendLine(l);
                string err = CarHDTool.Import(out int n);
                sb.AppendLine("import: " + (err ?? "OK") + ", " + n + " sets");
                CarHDTool.BundleReport();
                sb.AppendLine("DONE");
            }
            catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); }
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "carhd_bake.txt"), sb.ToString());
        }

        [MenuItem("PSX Racing/Cars/HD Car Tier Shots")]
        public static void Run()
        {
            log.Clear();
            outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots", "carhd");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);
            System.Environment.SetEnvironmentVariable("PSX_CAR_HD", "1");
            System.Environment.SetEnvironmentVariable("PSX_PROFILE", "PC");
            CarHD.ResetMap();
            try { Shoot(); }
            catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); Debug.LogException(e); }
            finally
            {
                CarHD.ForceOff = false; CarHD.SmoothOverride = -1;
                Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
                File.WriteAllText(Path.Combine(outDir, "carhd_shots.txt"), log.ToString());
                Debug.Log("[CarHDShots]\n" + log);
            }
        }

        static void Shoot()
        {
            TangentCheck();

            EditorSceneManager.OpenScene(SellerLotSceneBuilder.ScenePath, OpenSceneMode.Single);
            var world = Object.FindFirstObjectByType<SellerLotWorld>();
            if (world == null) { log.AppendLine("no SellerLotWorld"); return; }
            world.PreviewBuild();
            var plot = world.plots[world.plots.Length / 2];
            spot = plot.Find("CarSpot");
            // turned to face the house: its rear to the street, where there is room to stand back
            spot.rotation = Quaternion.identity;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.transform.IsChildOf(world.player)) r.enabled = false;
            var output = Object.FindFirstObjectByType<PSXCameraOutput>();
            cam = output != null ? output.GetComponent<Camera>() : null;
            if (cam == null) cam = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault(c => c.targetTexture == null) ?? Object.FindFirstObjectByType<Camera>();
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
            log.AppendLine($"camera {(cam != null ? cam.name : "NONE")}, sun {(sun != null ? sun.name : "NONE")}, spot {spot.position}");
            if (cam == null) return;
            cam.fieldOfView = Fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 3000f;

            Hour(TimeOfDay.Noon);
            foreach (var (key, sheet) in Cars)
            {
                var def = CarModelLibrary.Load(key);
                int skin = def != null && def.skinNames != null ? System.Array.IndexOf(def.skinNames, sheet) : -1;
                if (skin < 0) { log.AppendLine($"{key}: no skin {sheet} (has {(def != null && def.skinNames != null ? string.Join(",", def.skinNames) : "-")})"); continue; }
                var shell = Park(def, skin, out var body);
                string mask = MaskPath(key, sheet);
                foreach (var (view, pos, rot) in Views())
                {
                    PairShot(key + "_" + view, pos, rot, body, mask);
                }
                foreach (float d in new[] { 5f, 15f, 40f })
                {
                    var (pos, rot) = Quarter(d, 1.5f);
                    PairShot(key + "_m" + d.ToString("0"), pos, rot, body, mask);
                }
                if (key == "rx7_fd_93")
                {
                    var (pos, rot) = Quarter(4.6f, 1.45f);
                    // the tangent frame on screen: debug 2 (the mapped normal) over both flanks
                    Shader.SetGlobalFloat("_PSXPaintDebug", 2f);
                    CarHD.ForceOff = false; Restamp();
                    Save(Render(pos, rot, true, false), key + "_dbgnormal_right");
                    var (pl, rl) = Quarter(4.6f, 1.45f, -1f);
                    Save(Render(pl, rl, true, false), key + "_dbgnormal_left");
                    Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
                    // dusk, night with the lamps lit, rain
                    Hour(TimeOfDay.Dusk); PairShot(key + "_dusk", pos, rot, body, null);
                    Hour(TimeOfDay.Night); Lamps(body, key, true); PairShot(key + "_night", pos, rot, body, null, lamps: key); Lamps(body, key, false);
                    Hour(TimeOfDay.Noon);
                    Shader.SetGlobalFloat("_PSXCarWet", 1f); Shader.SetGlobalVector("_PSXCarDrops", new Vector4(1f, 0f, 0f, 0f));
                    PairShot(key + "_rain", pos, rot, body, null);
                    Shader.SetGlobalFloat("_PSXCarWet", 0f); Shader.SetGlobalVector("_PSXCarDrops", Vector4.zero);
                    CostProbe(def, skin);
                }
                Object.DestroyImmediate(shell.gameObject);
            }

            // OLDER CARS: the Classic sheet point sampled (as it ships) vs CarHD.SmoothSheets
            foreach (var key in OldCars)
            {
                var def = CarModelLibrary.Load(key);
                if (def == null || def.SkinCount == 0) { log.AppendLine("old car missing " + key); continue; }
                var shell = Park(def, 0, out var body);
                var t = def.skinMaterials[0].mainTexture;
                log.AppendLine($"{key}: sheet {(t != null ? t.name + " " + t.width + "x" + t.height : "none")}");
                foreach (var (view, pos, rot) in Views())
                {
                    if (view == "chase") continue;
                    CarHD.SmoothOverride = 0; Restamp(); Save(Render(pos, rot, true, true), key + "_" + view + "_point");
                    CarHD.SmoothOverride = 1; Restamp(); Save(Render(pos, rot, true, true), key + "_" + view + "_smooth");
                }
                CarHD.SmoothOverride = -1; Restamp();
                Object.DestroyImmediate(shell.gameObject);
            }
            log.AppendLine("CARHD SHOTS WRITTEN");
        }

        static IEnumerable<(string, Vector3, Quaternion)> Views()
        {
            var (p1, r1) = Quarter(4.6f, 1.45f);
            yield return ("drive", p1, r1);
            Vector3 c = spot.position, f = spot.forward;
            Vector3 eye = c - f * 6.2f + Vector3.up * 2.5f;
            yield return ("chase", eye, Quaternion.LookRotation(c + Vector3.up * 0.8f + f * 14f - eye));
            var (p3, r3) = Quarter(30f, 1.6f);
            yield return ("far30", p3, r3);
        }

        /// <summary>The rear three-quarter at <paramref name="d"/> metres
        /// (side +1 = the car's right).</summary>
        static (Vector3, Quaternion) Quarter(float d, float h, float side = 1f)
        {
            Vector3 c = spot.position, f = spot.forward, r = spot.right;
            var dir = (-f * 0.75f + r * 0.66f * side).normalized;
            var eye = c + dir * d + Vector3.up * h;
            return (eye, Quaternion.LookRotation(c + Vector3.up * 0.65f - eye));
        }

        static Transform Park(CarModelDef def, int skin, out Renderer body)
        {
            var shell = CarShell.Spawn(spot, def, skin, out _, solid: false);
            body = shell != null ? shell.Find("Body")?.GetComponent<Renderer>() : null;
            if (shell != null) PSXTexDecode.StampTree(shell.gameObject);
            return shell;
        }

        static string MaskPath(string key, string sheet)
        {
            string dir = System.Environment.GetEnvironmentVariable("PSX_CARHD_MASKS");
            if (string.IsNullOrEmpty(dir)) return null;
            string p = Path.Combine(dir, "mask_" + key + "__" + sheet + ".png");
            return File.Exists(p) ? p : null;
        }

        static void Hour(int h)
        {
            if (sun != null) TimeOfDay.Apply(h, sun);
            var g = Object.FindFirstObjectByType<PSXGlobals>();
            if (g != null) g.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
        }

        static void Restamp()
        {
            CarHD.OffAll();
            CarLights.ForgetLampMaps();
            PSXTexDecode.StampAll();
        }

        static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        static void Lamps(Renderer body, string key, bool on)
        {
            if (body == null) return;
            body.GetPropertyBlock(block);
            var map = on ? CarLights.LampMapFor(key) : null;
            block.SetTexture("_LightMask", map != null ? (Texture)map : Texture2D.blackTexture);
            block.SetFloat("_RunningLights", on ? 1f : 0f);
            block.SetFloat("_HeadLights", on && CarLights.LampMapHasHead(key) ? 1f : 0f);
            block.SetFloat("_Brake", 0f);
            body.SetPropertyBlock(block);
            if (on) log.AppendLine($"  lamps {key}: map {(map != null ? map.name + " " + map.width + "x" + map.height : "none")}");
        }

        /// <summary>Before (tier off: the 512 point sheet) and after (tier on),
        /// and the seam mask's pass when there is one.</summary>
        static void PairShot(string name, Vector3 pos, Quaternion rot, Renderer body, string maskPath, string lamps = null)
        {
            CarHD.ForceOff = true; Restamp();
            if (lamps != null) Lamps(body, lamps, true);
            Save(Render(pos, rot, true, true), name + "_before");
            CarHD.ForceOff = false; Restamp();
            if (lamps != null) Lamps(body, lamps, true);
            Save(Render(pos, rot, true, true), name + "_after");
            if (maskPath != null && body != null) MaskShot(name, pos, rot, body, maskPath);
        }

        static readonly Dictionary<string, Texture2D> masks = new Dictionary<string, Texture2D>();
        static void MaskShot(string name, Vector3 pos, Quaternion rot, Renderer body, string maskPath)
        {
            if (!masks.TryGetValue(maskPath, out var mt))
            {
                mt = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                mt.LoadImage(File.ReadAllBytes(maskPath));
                mt.filterMode = FilterMode.Point;
                masks[maskPath] = mt;
            }
            var keep = body.sharedMaterial;
            var mm = new Material(keep);
            mm.DisableKeyword(CarHD.Keyword);
            mm.SetTexture("_MainTex", mt);
            mm.SetFloat("_MainTexRaw", 0f);
            body.sharedMaterial = mm;
            var wheels = body.transform.parent.GetComponentsInChildren<Renderer>().Where(r => r != body && r.enabled).ToList();
            foreach (var w in wheels) w.enabled = false;
            Shader.SetGlobalFloat("_PSXPaintDebug", 5f);
            var tex = Render(pos, rot, false, false);
            Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
            foreach (var w in wheels) w.enabled = true;
            body.sharedMaterial = keep;
            Object.DestroyImmediate(mm);
            Save(tex, name + "_mask");
        }

        static void Save(Texture2D t, string name)
        {
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), t.EncodeToPNG());
            Object.DestroyImmediate(t);
        }

        /// <summary>One frame at the PC profile's picture: the camera into a
        /// 1920 x 1080 target, then (shown) PSX/Blit at 8 bits with its dither,
        /// FXAA, the grade and the halation - what PSXCameraOutput does in the
        /// player under PCPicture. Unshown = the raw render (the mask pass).</summary>
        static Texture2D Render(Vector3 pos, Quaternion rot, bool shown, bool graded)
        {
            cam.transform.SetPositionAndRotation(pos, rot);
            StreetLights.Push(pos, rot * Vector3.forward);
            var rt = new RenderTexture(W, H, 32, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            var request = new RenderPipeline.StandardRequest { destination = rt };
            RenderPipeline.SubmitRenderRequest(cam, request);
            var src = rt;
            RenderTexture dst = null;
            if (shown)
            {
                var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                var mat = output != null && output.display != null ? output.display.material : null;
                if (mat != null)
                {
                    var tmp = new Material(mat) { hideFlags = HideFlags.HideAndDontSave };
                    if (tmp.HasProperty("_ColorDepth")) tmp.SetFloat("_ColorDepth", 8f);
                    if (tmp.HasProperty("_DitherStrength")) tmp.SetFloat("_DitherStrength", 1f);
                    if (tmp.HasProperty("_Fxaa")) tmp.SetFloat("_Fxaa", 1f);
                    if (tmp.HasProperty("_Grade")) tmp.SetFloat("_Grade", graded ? 1f : 0f);
                    dst = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
                    dst.Create();
                    var glow = Halation.Wanted(tmp) ? Halation.BuildNow(src) : null;
                    Halation.Apply(tmp, glow);
                    Graphics.Blit(src, dst, tmp);
                    Halation.Release(ref glow);
                    Object.DestroyImmediate(tmp);
                    src = dst;
                }
            }
            var prev = RenderTexture.active;
            RenderTexture.active = src;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            rt.Release(); Object.DestroyImmediate(rt);
            if (dst != null) { dst.Release(); Object.DestroyImmediate(dst); }
            return tex;
        }

        /// <summary>A crude cost probe: eight FDs in two rows at the chase
        /// camera, 30 frames each way, wall time including the read-back (a
        /// GPU sync). The editor's numbers, not a player's.</summary>
        static void CostProbe(CarModelDef def, int skin)
        {
            var grid = new List<Transform>();
            var anchors = new List<GameObject>();
            for (int i = 0; i < 8; i++)
            {
                var a = new GameObject("GridSpot" + i);
                a.transform.position = spot.position + spot.right * ((i % 2) * 3.2f - 1.6f) + spot.forward * (6f + (i / 2) * 6.5f);
                a.transform.rotation = spot.rotation;
                anchors.Add(a);
                grid.Add(CarShell.Spawn(a.transform, def, skin, out _, solid: false));
            }
            Vector3 c = spot.position, f = spot.forward;
            Vector3 eye = c - f * 6.2f + Vector3.up * 2.5f;
            var rot = Quaternion.LookRotation(c + Vector3.up * 0.8f + f * 14f - eye);
            foreach (bool hd in new[] { false, true, false, true })
            {
                CarHD.ForceOff = !hd; Restamp();
                Object.DestroyImmediate(Render(eye, rot, false, false));   // warm
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int k = 0; k < 30; k++) Object.DestroyImmediate(Render(eye, rot, false, false));
                log.AppendLine($"  cost probe, 9 FDs at the chase eye, 1920x1080, tier {(hd ? "ON " : "off")}: {sw.Elapsed.TotalMilliseconds / 30:0.00} ms a frame (editor, incl. read-back)");
            }
            foreach (var a in anchors) Object.DestroyImmediate(a);
        }

        /// <summary>THE TANGENT FRAME, on the CPU, per body: for every triangle
        /// with area in UV, does the imported tangent run along +U (dP/du) and
        /// does cross(N, T) * w run along +V (dP/dv)? Split by the car's two
        /// sides and by mirrored UV islands (negative UV winding), so a
        /// mirrored half lit the wrong way round would show as a disagreement.</summary>
        static void TangentCheck()
        {
            log.AppendLine("TANGENT FRAME (imported MikkTSpace vs the UV gradient, per triangle):");
            foreach (var (key, _) in Cars)
            {
                var def = CarModelLibrary.Load(key);
                foreach (var (part, mesh) in new[] { ("body", def != null ? def.bodyMesh : null), ("wheel", def != null ? def.wheelMesh : null) })
                {
                    if (mesh == null) { log.AppendLine($"  {key} {part}: no mesh"); continue; }
                    var v = mesh.vertices; var uv = mesh.uv; var n = mesh.normals; var t = mesh.tangents; var tri = mesh.triangles;
                    if (t == null || t.Length != v.Length) { log.AppendLine($"  {key} {part}: NO TANGENTS ({v.Length} verts)"); continue; }
                    int[] okT = new int[4], okB = new int[4], all = new int[4];   // [side L/R x mirrored no/yes]
                    for (int i = 0; i + 2 < tri.Length; i += 3)
                    {
                        int a = tri[i], b = tri[i + 1], c = tri[i + 2];
                        Vector3 e1 = v[b] - v[a], e2 = v[c] - v[a];
                        Vector2 d1 = uv[b] - uv[a], d2 = uv[c] - uv[a];
                        float det = d1.x * d2.y - d2.x * d1.y;
                        if (Mathf.Abs(det) < 1e-10f) continue;
                        Vector3 dPdu = (e1 * d2.y - e2 * d1.y) / det, dPdv = (e2 * d1.x - e1 * d2.x) / det;
                        var cen = (v[a] + v[b] + v[c]) / 3f;
                        int bin = (cen.x < 0f ? 0 : 1) + (det < 0f ? 2 : 0);
                        all[bin]++;
                        Vector3 T = (Vector3)t[a]; Vector3 N = n[a];
                        Vector3 B = Vector3.Cross(N, T) * t[a].w;
                        if (Vector3.Dot(T, dPdu) > 0f) okT[bin]++;
                        if (Vector3.Dot(B, dPdv) > 0f) okB[bin]++;
                    }
                    string Row(int k) => all[k] == 0 ? "-" : $"{all[k]} tris T {100f * okT[k] / all[k]:0.0}% B {100f * okB[k] / all[k]:0.0}%";
                    log.AppendLine($"  {key} {part}: -x side [{Row(0)}] mirrored [{Row(2)}] | +x side [{Row(1)}] mirrored [{Row(3)}]");
                }
            }
        }
    }
}
