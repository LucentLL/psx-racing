using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using PSXRacing.OnFoot;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CARPARTS, STAGE 1's PROOF (EDITOR frames, STAND-IN): the stand-in
    /// (CarPartsStandIn, on the pilot's shell) on a flat pad (the seller's
    /// drive, a CarShell as the town and the garage spawn it) and on the
    /// showroom stage (CarViewer's studio): all shut, hood, trunk, doors, all
    /// open, from a front three-quarter and the side; a body fitted 0.7 across
    /// x 1.2 along and 1.2 x 0.7 with every part open (no shear); the draws and
    /// shadow casters per state against today's car; and an in-process A/B of
    /// the render time (all shut vs all open, with an A/A column).
    ///
    /// Batch: -executeMethod PSXRacing.EditorTools.CarPartsShots.Run, with
    /// PSX_CP_OUT = the folder for the PNGs and carparts_shots.txt (default
    /// Screenshots/carparts) and PSX_CP_TAG = a prefix for the file names.
    /// Run once with PSX_PROFILE=PC (the frames, the PC numbers) and once
    /// without (the non-PC numbers, a relative figure).
    /// </summary>
    public static class CarPartsShots
    {
        const int W = 1920, H = 1080;
        static string outDir, tag;
        static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();
        static Camera cam;
        static Light sun;
        static Transform spot;

        static readonly (string name, int mask)[] States =
        {
            ("shut", 0), ("hood", CarParts.Hood), ("trunk", CarParts.Trunk), ("doors", CarParts.Doors), ("all", CarParts.All),
        };

        /// <summary>Batch: the shots, then the self-test (one editor start).</summary>
        public static void RunWithSelfTest()
        {
            Run();
            LifeSimSelfTest.RunCarParts();
        }

        [MenuItem("PSX Racing/Cars/Car Parts Shots (stage 1)")]
        public static void Run()
        {
            log.Clear();
            outDir = System.Environment.GetEnvironmentVariable("PSX_CP_OUT");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots", "carparts");
            tag = System.Environment.GetEnvironmentVariable("PSX_CP_TAG") ?? "";
            Directory.CreateDirectory(outDir);
            bool shots = System.Environment.GetEnvironmentVariable("PSX_CP_NOSHOTS") != "1";
            int keepOverride = CarPartsStandIn.Override;
            CarPartsStandIn.Override = 1;
            CarPartsStandIn.Forget();
            try
            {
                log.AppendLine($"CARPARTS SHOTS (EDITOR, STAND-IN) profile {(PlatformProfile.PC ? "PC" : "non-PC (relative figure)")}, " +
                               $"PSX_PROFILE={System.Environment.GetEnvironmentVariable("PSX_PROFILE") ?? "-"}, Unity {Application.unityVersion}");
                Pad(shots);
                if (shots) Studio();
            }
            catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); Debug.LogException(e); }
            finally
            {
                CarParts.ShutAllNow();
                CarParts.On = true;
                CarPartsStandIn.Override = keepOverride;
                CarPartsStandIn.Forget();
                File.WriteAllText(Path.Combine(outDir, tag + "carparts_shots.txt"), log.ToString());
                Debug.Log("[CarPartsShots]\n" + log);
            }
        }

        // ------------------------------------------------------------------
        //  The pad: the seller's drive, rebuilt from this branch's code
        // ------------------------------------------------------------------
        static void Pad(bool shots)
        {
            SellerLotSceneBuilder.Build();
            EditorSceneManager.OpenScene(SellerLotSceneBuilder.ScenePath, OpenSceneMode.Single);
            var world = Object.FindAnyObjectByType<SellerLotWorld>();
            if (world == null) { log.AppendLine("no SellerLotWorld"); return; }
            world.PreviewBuild();
            var plot = world.plots[world.plots.Length / 2];
            spot = plot.Find("CarSpot");
            spot.rotation = Quaternion.identity;
            foreach (var r in Object.FindObjectsByType<Renderer>())
                if (world.player != null && r.transform.IsChildOf(world.player)) r.enabled = false;
            var output = Object.FindAnyObjectByType<PSXCameraOutput>();
            cam = output != null ? output.GetComponent<Camera>() : null;
            if (cam == null) cam = Object.FindObjectsByType<Camera>().FirstOrDefault(c => c.targetTexture == null);
            foreach (var l in Object.FindObjectsByType<Light>()) if (l.type == LightType.Directional) { sun = l; break; }
            if (cam == null) { log.AppendLine("no camera"); return; }
            cam.fieldOfView = 40f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 3000f;
            if (sun != null) TimeOfDay.Apply(TimeOfDay.Noon, sun);
            var g = Object.FindAnyObjectByType<PSXGlobals>();
            if (g != null) g.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);

            var def = CarModelLibrary.Load(CarPartsStandIn.Key);
            if (def == null) { log.AppendLine("no shell " + CarPartsStandIn.Key); return; }
            var standIn = CarParts.PartsDef(def);
            foreach (var l in CarPartsStandIn.Report) log.AppendLine(l);
            log.AppendLine($"shell {def.key}: pop-ups {def.HasPopUps}, lens variant {def.HasLensVariant}, covers {(def.coverMesh != null)}, " +
                           $"stand-in {(standIn != null ? "built" : "MISSING")}, parts available mask {CarParts.Available(def)}");

            // ---- today's car: the LAB row off (nothing of this exists) ---
            CarParts.On = false;
            var shell = CarShell.Spawn(spot, def, 0, out _, solid: false);
            PSXTexDecode.StampTree(shell.gameObject);
            var mf = shell.Find("Body").GetComponent<MeshFilter>();
            var (fp, fr) = Front34();
            var (sp, sr) = Side();
            var todayFront = Count(shell, fp, fr);
            var todaySide = Count(shell, sp, sr);
            log.AppendLine($"DRAWS (renderers x submeshes in view; shadow = casters), today's car (LAB carparts OFF): front3/4 {todayFront}, side {todaySide}");
            if (shots) Save(Render(fp, fr), "pad_today_front34");
            CarParts.On = true;

            // ---- each state -------------------------------------------------
            foreach (var (name, mask) in States)
            {
                var cp = CarParts.Set(mf, def, mask, instant: true);
                var cf = Count(shell, fp, fr);
                var cs = Count(shell, sp, sr);
                log.AppendLine($"  {name,-6} mask {mask,2}: component {(cp != null ? "UP" : "none")}, front3/4 {cf}, side {cs}" +
                               (mask == 0 ? (cf.Equals(todayFront) && cs.Equals(todaySide) ? "  == today's car" : "  != TODAY'S CAR") : ""));
                if (shots)
                {
                    Save(Render(fp, fr), $"pad_{name}_front34");
                    Save(Render(sp, sr), $"pad_{name}_side");
                }
            }
            // all open, then ease shut through Step: the component goes with the last shut
            CarParts.Set(mf, def, CarParts.All, instant: true);
            var c2 = CarParts.Set(mf, def, 0);
            for (int i = 0; i < 60 && CarParts.Find(mf) != null; i++) CarParts.Find(mf).Step(1f / 60f);
            log.AppendLine($"  eased shut from all open: component {(CarParts.Find(mf) != null ? "STILL UP" : "gone")}, body {(mf.sharedMesh == def.bodyMesh ? "closed mesh" : "OTHER MESH")}");

            // ---- the fitted extremes, every part open -----------------------
            var keepScale = mf.transform.localScale;
            foreach (var s in new[] { new Vector3(0.7f, 1f, 1.2f), new Vector3(1.2f, 1f, 0.7f) })
            {
                mf.transform.localScale = s;
                var cp = CarParts.Set(mf, def, CarParts.All, instant: true);
                cp.Step(0f);   // re-solve for the new scale
                float shear = MaxEdgeChange(cp);
                float closedErr = ClosedError(cp, mf);
                log.AppendLine($"  fit {s.x:0.0} across x {s.z:0.0} along, all open: worst edge-length change through the parts {shear * 1000f:0.000} mm (0 = no shear); " +
                               $"closed-pose error {closedErr * 1000f:0.000} mm");
                if (shots)
                {
                    Save(Render(fp, fr), $"pad_fit{s.x:0.0}x{s.z:0.0}_all_front34");
                    Save(Render(sp, sr), $"pad_fit{s.x:0.0}x{s.z:0.0}_all_side");
                }
                CarParts.TearDown(mf);
            }
            mf.transform.localScale = keepScale;

            // ---- the render time: all shut vs all open, A/A ----------------
            Timing(shell, mf, def, fp, fr);
            Object.DestroyImmediate(shell.gameObject);
        }

        static (Vector3, Quaternion) Front34()
        {
            Vector3 c = spot.position, f = spot.forward, r = spot.right;
            var dir = (f * 0.80f + r * 0.62f).normalized;
            var eye = c + dir * 7.2f + Vector3.up * 2.1f;
            return (eye, Quaternion.LookRotation(c + Vector3.up * 0.6f - eye));
        }

        static (Vector3, Quaternion) Side()
        {
            Vector3 c = spot.position, f = spot.forward, r = spot.right;
            var eye = c + r * 7.4f + f * 0.2f + Vector3.up * 1.5f;
            return (eye, Quaternion.LookRotation(c + Vector3.up * 0.65f - eye));
        }

        struct Draws
        {
            public int draws, casters, renderers;
            public override string ToString() => $"{draws} draws / {casters} casters ({renderers} renderers)";
            public override bool Equals(object o) => o is Draws d && d.draws == draws && d.casters == casters && d.renderers == renderers;
            public override int GetHashCode() => draws * 31 + casters;
        }

        /// <summary>The car's draws for one camera: every enabled renderer in
        /// the view, one draw per submesh (the PSX passes are single-pass
        /// forward); a caster = a renderer that casts a shadow.</summary>
        static Draws Count(Transform shell, Vector3 pos, Quaternion rot)
        {
            cam.transform.SetPositionAndRotation(pos, rot);
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            var d = new Draws();
            foreach (var r in shell.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r.forceRenderingOff) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                var mfr = r.GetComponent<MeshFilter>();
                int sub = mfr != null && mfr.sharedMesh != null ? mfr.sharedMesh.subMeshCount : 1;
                d.renderers++;
                d.draws += Mathf.Max(1, Mathf.Min(sub, r.sharedMaterials.Length));
                if (r.shadowCastingMode != ShadowCastingMode.Off) d.casters++;
            }
            return d;
        }

        /// <summary>The worst change of any part-mesh edge's world length
        /// through the opening sweep (0 / 25 / 50 / 75 / 100 %) against the
        /// closed pose: a rigid swing changes none.</summary>
        public static float MaxEdgeChange(CarParts cp)
        {
            float worst = 0f;
            foreach (var bit in new[] { CarParts.Hood, CarParts.Trunk, CarParts.DoorL, CarParts.DoorR, CarParts.DoorRL, CarParts.DoorRR })
            {
                var part = cp.PartTransform(bit);
                var swing = cp.SwingTransform(bit);
                if (part == null) continue;
                var mesh = part.GetComponent<MeshFilter>().sharedMesh;
                var v = mesh.vertices;
                var t = mesh.triangles;
                var keep = swing.localRotation;
                var axisAngle = keep;
                keep.ToAngleAxis(out float deg, out Vector3 axis);
                if (deg < 1e-3f) axis = Vector3.right;
                swing.localRotation = Quaternion.identity;
                var m0 = part.localToWorldMatrix;
                float openDeg = Mathf.Max(deg, cp.Angle(bit));
                foreach (float k in new[] { 0.25f, 0.5f, 0.75f, 1f })
                {
                    swing.localRotation = Quaternion.AngleAxis(openDeg * k, axis);
                    var m1 = part.localToWorldMatrix;
                    for (int i = 0; i < t.Length; i += 3)
                        for (int e = 0; e < 3; e++)
                        {
                            var a = v[t[i + e]]; var b = v[t[i + (e + 1) % 3]];
                            float l0 = (m0.MultiplyPoint3x4(a) - m0.MultiplyPoint3x4(b)).magnitude;
                            float l1 = (m1.MultiplyPoint3x4(a) - m1.MultiplyPoint3x4(b)).magnitude;
                            worst = Mathf.Max(worst, Mathf.Abs(l1 - l0));
                        }
                }
                swing.localRotation = axisAngle;
            }
            return worst;
        }

        /// <summary>With every Swing at identity, the worst distance between a
        /// part vertex and where the body's own transform (the closed body's
        /// frame) puts the same authored mesh-space point.</summary>
        public static float ClosedError(CarParts cp, MeshFilter body)
        {
            float worst = 0f;
            var mb = body.transform.localToWorldMatrix;
            foreach (var bit in new[] { CarParts.Hood, CarParts.Trunk, CarParts.DoorL, CarParts.DoorR, CarParts.DoorRL, CarParts.DoorRR })
            {
                var part = cp.PartTransform(bit);
                var swing = cp.SwingTransform(bit);
                if (part == null) continue;
                var keep = swing.localRotation;
                swing.localRotation = Quaternion.identity;
                var mp = part.localToWorldMatrix;
                foreach (var v in part.GetComponent<MeshFilter>().sharedMesh.vertices)
                    worst = Mathf.Max(worst, (mp.MultiplyPoint3x4(v) - mb.MultiplyPoint3x4(v)).magnitude);
                swing.localRotation = keep;
            }
            return worst;
        }

        // ------------------------------------------------------------------
        //  The render time (EDITOR, in process): blocks of N frames, ON = all
        //  open, OFF = all shut, and an A/A column (shut vs shut)
        // ------------------------------------------------------------------
        static void Timing(Transform shell, MeshFilter mf, CarModelDef def, Vector3 pos, Quaternion rot)
        {
            const int N = 20, Blocks = 6;
            var rt = new RenderTexture(W, H, 32, RenderTextureFormat.ARGB32);
            rt.Create();
            cam.transform.SetPositionAndRotation(pos, rot);
            var req = new RenderPipeline.StandardRequest { destination = rt };
            var buf = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            double Block(int mask)
            {
                if (mask == 0) CarParts.TearDown(mf); else CarParts.Set(mf, def, mask, instant: true);
                RenderPipeline.SubmitRenderRequest(cam, req);   // warm
                Sync(rt, buf);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int k = 0; k < N; k++) { RenderPipeline.SubmitRenderRequest(cam, req); Sync(rt, buf); }
                return sw.Elapsed.TotalMilliseconds / N;
            }
            var shutA = new List<double>(); var shutB = new List<double>(); var open = new List<double>();
            for (int b = 0; b < Blocks; b++)
            {
                shutA.Add(Block(0));
                open.Add(Block(CarParts.All));
                shutB.Add(Block(0));
            }
            CarParts.TearDown(mf);
            rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(buf);
            double Med(List<double> l) { var s = l.OrderBy(x => x).ToList(); return (s[s.Count / 2] + s[(s.Count - 1) / 2]) * 0.5; }
            log.AppendLine($"TIME (EDITOR, {W}x{H}, front3/4 view, {Blocks} blocks x {N} frames, median ms a frame incl. a 4x4 read-back sync; " +
                           $"the machine is shared with three other jobs, read the DIFFERENCES): " +
                           $"shut A {Med(shutA):0.000}, all open {Med(open):0.000}, shut B (A/A) {Med(shutB):0.000}; " +
                           $"open - shut {Med(open) - Med(shutA):+0.000;-0.000}, A/A {Med(shutB) - Med(shutA):+0.000;-0.000}");
            log.AppendLine("  per block shut A / open / shut B: " + string.Join("  ", Enumerable.Range(0, Blocks).Select(i => $"{shutA[i]:0.00}/{open[i]:0.00}/{shutB[i]:0.00}")));
        }

        static void Sync(RenderTexture rt, Texture2D buf)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            buf.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
            RenderTexture.active = prev;
        }

        // ------------------------------------------------------------------
        //  The showroom stage (CarViewer's studio)
        // ------------------------------------------------------------------
        static void Studio()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var canvasGo = new GameObject("CP_Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var panelGo = new GameObject("CP_Panel", typeof(RectTransform));
            panelGo.transform.SetParent(canvasGo.transform, false);
            var panel = (RectTransform)panelGo.transform;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(W, H);
            var host = new GameObject("CP_Viewer");
            var viewer = host.AddComponent<CarViewer>();
            var def = CarModelLibrary.Load(CarPartsStandIn.Key);
            viewer.Show(def, 0);
            viewer.AttachStudio(panel);
            viewer.ResetStudioAngle();
            var mf = viewer.BodyRenderer.GetComponent<MeshFilter>();
            if (System.Environment.GetEnvironmentVariable("PSX_CP_YAWPROBE") == "1")
                foreach (float y in new[] { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f })
                {
                    viewer.TurnBy(y - viewer.Yaw); viewer.Tick(0f, 0f); viewer.RenderNow();
                    Save(Read(viewer.StudioTexture), $"studio_yawprobe_{y:0}");
                }
            foreach (var (name, mask) in States)
            {
                CarParts.Set(mf, def, mask, instant: true);
                foreach (var (view, yaw) in new[] { ("front34", 145f), ("side", 90f) })
                {
                    viewer.TurnBy(yaw - viewer.Yaw);
                    viewer.Tick(0f, 0f);
                    viewer.RenderNow();
                    var t = Read(viewer.StudioTexture);
                    Save(t, $"studio_{name}_{view}");
                }
            }
            // a re-dress (another shell, then this one again) always starts shut
            CarParts.Set(mf, def, CarParts.All, instant: true);
            var other = CarModelLibrary.Load("civic_eg");
            if (other != null) viewer.Show(other, 0);
            log.AppendLine($"STUDIO: re-dress to {(other != null ? other.key : "-")} with all open -> component {(CarParts.Find(mf) != null ? "STILL UP" : "gone")}, " +
                           $"body {(other != null && mf.sharedMesh == other.bodyMesh ? "its closed mesh" : "?")}");
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(canvasGo);
        }

        static Texture2D Read(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            return t;
        }

        static void Save(Texture2D t, string name)
        {
            File.WriteAllBytes(Path.Combine(outDir, tag + name + ".png"), t.EncodeToPNG());
            Object.DestroyImmediate(t);
        }

        /// <summary>One frame at the PC profile's picture (as CarHDShots): the
        /// camera into a 1920 x 1080 target, then PSX/Blit at 8 bits with FXAA
        /// and the grade.</summary>
        static Texture2D Render(Vector3 pos, Quaternion rot)
        {
            cam.transform.SetPositionAndRotation(pos, rot);
            StreetLights.Push(pos, rot * Vector3.forward);
            var rt = new RenderTexture(W, H, 32, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            var request = new RenderPipeline.StandardRequest { destination = rt };
            for (int k = 0; k < 2; k++) RenderPipeline.SubmitRenderRequest(cam, request);
            var src = rt;
            RenderTexture dst = null;
            var output = Object.FindAnyObjectByType<PSXCameraOutput>();
            var mat = output != null && output.display != null ? output.display.material : null;
            if (mat != null)
            {
                var tmp = new Material(mat) { hideFlags = HideFlags.HideAndDontSave };
                if (tmp.HasProperty("_ColorDepth")) tmp.SetFloat("_ColorDepth", 8f);
                if (tmp.HasProperty("_DitherStrength")) tmp.SetFloat("_DitherStrength", 1f);
                if (tmp.HasProperty("_Fxaa")) tmp.SetFloat("_Fxaa", 1f);
                if (tmp.HasProperty("_Grade")) tmp.SetFloat("_Grade", 1f);
                dst = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
                dst.Create();
                var glow = Halation.Wanted(tmp) ? Halation.BuildNow(src) : null;
                Halation.Apply(tmp, glow);
                Graphics.Blit(src, dst, tmp);
                Halation.Release(ref glow);
                Object.DestroyImmediate(tmp);
                src = dst;
            }
            var tex = Read(src);
            rt.Release(); Object.DestroyImmediate(rt);
            if (dst != null) { dst.Release(); Object.DestroyImmediate(dst); }
            return tex;
        }
    }
}
