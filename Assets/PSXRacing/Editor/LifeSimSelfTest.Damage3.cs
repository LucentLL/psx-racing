using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Damage round 3 (owner, 2026-11-17: "I'm not a fan of the low-poly blocky
    /// interior when windows are broken. If shown, the cockpit model should be
    /// used." - and "The wavy lines when glass breaks also appear on the car
    /// body"): the cockpit model through a shattered pane (DamageCabin), its
    /// fit, its stencil gate, its per-camera rules, its pool and its reduced
    /// copy; the cockpit's own side and rear glass; no glass test by a door
    /// mirror's sail; no wavy contour lines on the body.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        /// <summary>Only damage round 3's pins: seconds, no scene build.
        /// Writes PSXRacing_selftest_damage3.txt.</summary>
        public static void RunDamage3()
        {
            log = new System.Text.StringBuilder();
            failures = 0;
            RunDamage3Tests();
            Line(failures == 0 ? "DAMAGE3 SELF-TEST OK" : "DAMAGE3 SELF-TEST FAILED (" + failures + ")");
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../PSXRacing_selftest_damage3.txt"), log.ToString());
        }

        static void RunDamage3Tests()
        {
            bool view = DamageView.On, look = DamageLook.On, body = DamageLook.BodyOn, glass = DamageGlass.On, cab = DamageCabin.On;
            try
            {
                DamageView.On = DamageLook.On = DamageLook.BodyOn = DamageGlass.On = DamageCabin.On = true;
                Guard(nameof(TestCabinThroughHole), TestCabinThroughHole);
                Guard(nameof(TestGlassTrimAndLines), TestGlassTrimAndLines);
            }
            finally
            {
                DamageView.On = view; DamageLook.On = look; DamageLook.BodyOn = body; DamageGlass.On = glass; DamageCabin.On = cab;
                DamageCabin.Suspend = false;
            }
        }

        static float D3F(Renderer r, string name)
        {
            var m = r != null ? r.sharedMaterial : null;
            return m != null && m.HasProperty(name) ? m.GetFloat(name) : float.NaN;
        }

        static void TestCabinThroughHole()
        {
            Line("damage round 3 - the cockpit model through a shattered pane (owner 11-17):");
            var spec = CarCatalog.Get("honda_civic_sir_ii__eg___91") ?? CrashSpec("jpn", 1990);
            if (spec == null) { Check(false, "an EG Civic (or a Japanese road car) to break"); return; }
            var s = LifeRules.SeedNewGame("DMG3", 25, LifeRules.DefaultJobIndex);
            const int R = DamageModel.ZRear, S = DamageModel.ZSide;
            string P = DamageModel.PanelId, B = DamageModel.BarId;
            var hurt = DmgCar(s, spec, "d3#holed");
            DvCrash(s, hurt, 6101, S, -1, 7f, (P, 3, S, -1));
            DvCrash(s, hurt, 6102, R, 0, 7.5f, (P, 3, R, 0), (B, 2, R, 0));
            var crazed = DmgCar(s, spec, "d3#crazed");
            DvCrash(s, crazed, 6103, S, -1, 4.4f, (P, 2, S, -1));
            var mint = DmgCar(s, spec, "d3#mint");
            s.cars.Add(hurt); s.cars.Add(crazed); s.cars.Add(mint);
            var k = DamageLook.Read(hurt.faults, null);
            Check(k.panes.z >= DamageCabin.HoleState && k.panes.y >= DamageCabin.HoleState && k.panes.w == 0f,
                  "a crumpled left door and a crumpled tail with its bar pushed back: the left and rear glass shattered", k.panes);

            var go = new GameObject("Damage3Test") { hideFlags = HideFlags.HideAndDontSave };
            var viewer = go.AddComponent<CarViewer>();
            var camGo = new GameObject("Damage3TestCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            RenderTexture rt16 = null, rt24 = null;
            try
            {
                viewer.ShowOwned(hurt, spec);
                var br = viewer.BodyRenderer;
                var frame = br != null ? br.transform.parent : null;
                var dc = frame != null ? frame.GetComponent<DamageCabin>() : null;
                Check(dc != null && dc.Holed && dc.HasCabin, "the turntable's holed car has its cockpit model (built at once in a tool)");
                if (dc == null || !dc.HasCabin) return;
                Check(viewer.Picture != null && GraphicsFormatUtility.IsStencilFormat(viewer.Picture.depthStencilFormat),
                      "the turntable's target has a stencil (24 bits)", viewer.Picture != null ? viewer.Picture.depthStencilFormat.ToString() : "none");

                // THE FIT: exactly the cockpit view's (CockpitInterior.FitFor + Fit3), placed on the rig's frame
                var def = viewer.Shown;
                var fit = CarModelLibrary.Fit(def, spec);
                Vector3 c = fit.P(def.colliderCenter), sz = new Vector3(def.colliderSize.x * fit.sx, def.colliderSize.y, def.colliderSize.z * fit.sz);
                bool rhd = DriveSide.IsRHD(spec.id);
                var f = CockpitInterior.FitFor(c, sz, def, fit.sz, fit.dy, rhd);
                f = CockpitInterior.Fit3(f, def, fit.sz, fit.dy, fit.sx);
                var got = dc.CabinFit;
                Check((got.eye - f.eye).magnitude < 1e-4f && Mathf.Abs(got.scale - f.scale) < 1e-4f && Mathf.Abs(got.ScaleY - f.ScaleY) < 1e-4f
                      && Mathf.Abs(got.widthFit - f.widthFit) < 1e-4f && got.rhd == rhd,
                      "its fit is the cockpit view's own (dash on the cowl, header under the roof, inside the side glass)",
                      $"eye {got.eye} vs {f.eye}, scale {got.scale:0.000}/{got.ScaleY:0.000} fit3 {got.fit3}");
                var rootLocal = frame.InverseTransformPoint(dc.CabinRoot.position);
                Check((rootLocal - (f.eye + viewer.ShownSlide)).magnitude < 1e-3f, "placed at the driver's eye on the rig's frame (the turntable's slide in)",
                      rootLocal + " vs " + (f.eye + viewer.ShownSlide));

                // THE GATE: the mark, the pushed depth, the cabin on the mark
                var mk = dc.MarkRenderer; var dp = dc.DepthRenderer; var cr = dc.CabinRenderer;
                Check(mk != null && dp != null && mk.transform.parent == br.transform && dp.transform.parent == br.transform
                      && mk.GetComponent<MeshFilter>().sharedMesh == br.GetComponent<MeshFilter>().sharedMesh,
                      "two companions of the body (its mesh, its frame)");
                Check(D3F(mk, "_PSXHoleMode") == 1f && D3F(mk, "_PSXStPass") == (float)StencilOp.Replace && D3F(mk, "_PSXColorMask") == 0f
                      && D3F(mk, "_PSXZWrite") == 0f && mk.sharedMaterial.renderQueue == DamageCabin.QueueMark,
                      "the mark: the hole's pixels into the stencil, no colour, no depth, after every shell");
                Check(D3F(dp, "_PSXHoleMode") == 2f && D3F(dp, "_PSXStComp") == (float)CompareFunction.Equal && D3F(dp, "_PSXZTest") == (float)CompareFunction.Always
                      && dp.sharedMaterial.renderQueue == DamageCabin.QueueDepth,
                      "the depth: pushed back only where marked");
                bool cabOk = cr != null && cr.sharedMaterials.Length > 0;
                if (cabOk) foreach (var m in cr.sharedMaterials)
                        cabOk &= m != null && m.shader.name == "PSX/Cabin" && m.GetFloat("_PSXStComp") == (float)CompareFunction.Equal
                                 && m.GetFloat("_PSXCabOut") == 1f && m.GetFloat("_PSXCabCull") == 0f && m.renderQueue == DamageCabin.QueueCabin && m.GetFloat("_PSXStRef") == DamageCabin.StencilBit;
                Check(cabOk, "the cockpit's own materials, drawn only on the mark, lit and fogged as seen from outside",
                      cr != null ? cr.sharedMaterials.Length + " materials" : "none");
                var bb = new MaterialPropertyBlock(); var mb = new MaterialPropertyBlock();
                br.GetPropertyBlock(bb);
                DamageCabin.Step(true);
                mk.GetPropertyBlock(mb);
                Check(bb.GetVector("_PSXGlsS") == mb.GetVector("_PSXGlsS") && bb.GetVector("_PSXGlsS") != Vector4.zero,
                      "the mark carries the body's own panes (its block)", mb.GetVector("_PSXGlsS"));
                var cockpitMat = CockpitModel.Mat("vinyl");
                Check(cockpitMat != null && cockpitMat.GetFloat("_PSXStComp") == (float)CompareFunction.Always && cockpitMat.GetFloat("_PSXCabOut") == 0f,
                      "the cockpit view's own materials untouched (no stencil test, lit from inside)");

                // PER CAMERA
                Vector3 at = br.bounds.center;
                rt16 = new RenderTexture(64, 64, 16); rt24 = new RenderTexture(64, 64, 24);
                cam.transform.position = at + frame.right * -3f;
                cam.targetTexture = rt24;
                DamageCabin.ForCamera(cam);
                Check(!mk.forceRenderingOff && !dp.forceRenderingOff && !cr.forceRenderingOff, "a camera 3 m off with a stencil draws it (the full mesh)");
                var low = dc.CabinLowRenderer;
                cam.transform.position = at + frame.right * -(DamageCabin.FullM + 4f);
                DamageCabin.ForCamera(cam);
                Check(!mk.forceRenderingOff && (low == null || !CockpitLod.Ready || (cr.forceRenderingOff && !low.forceRenderingOff)),
                      $"past {DamageCabin.FullM} m: the reduced copy", CockpitLod.Ready ? CockpitLod.CabinTris + " tris" : "not built");
                cam.transform.position = at + frame.right * -(DamageCabin.ReachM + 10f);
                DamageCabin.ForCamera(cam);
                Check(mk.forceRenderingOff && dp.forceRenderingOff && cr.forceRenderingOff && (low == null || low.forceRenderingOff),
                      $"past {DamageCabin.ReachM} m: nothing drawn (the dark cabin)");
                cam.transform.position = at + frame.right * -3f;
                cam.targetTexture = rt16;
                DamageCabin.ForCamera(cam);
                Check(mk.forceRenderingOff && cr.forceRenderingOff, "a camera whose target has no stencil (16 bits) never draws it");
                cam.targetTexture = rt24;
                DamageCabin.Suspend = true;
                DamageCabin.ForCamera(cam);
                Check(mk.forceRenderingOff && cr.forceRenderingOff, "Suspend (the tools' A/B): hidden");
                DamageCabin.Suspend = false;
                DamageCabin.ForCamera(cam);

                // THE REDUCED COPY
                CockpitLod.BuildNow();
                Check(CockpitLod.Ready && CockpitLod.CabinTris > 5000 && CockpitLod.CabinTris < 55169 * 6 / 10 && CockpitLod.WheelTris < 7402 / 2,
                      "the reduced cockpit: the same mesh clustered at 3 cm", $"cabin {CockpitLod.CabinTris} of 55169, wheel {CockpitLod.WheelTris} of 7402");

                // THE POOL, AND WHO GETS NONE
                int built = DamageCabin.Built;
                viewer.ShowOwned(crazed, spec);
                Check(!dc.Holed && !dc.HasCabin, "crazed glass (no hole): no cabin, the instance back to the pool");
                viewer.ShowOwned(mint, spec);
                Check(!dc.HasCabin, "a mint car: none");
                viewer.ShowOwned(hurt, spec);
                Check(dc.HasCabin && DamageCabin.Built == built, "holed again: the pooled instance reused, nothing new built", DamageCabin.Built - built);
                DamageCabin.On = false;   // PSX_DMG_CABIN=0
                viewer.ShowOwned(hurt, spec);
                Check(!dc.HasCabin, "PSX_DMG_CABIN=0: none (round 2's boxes in the paint)");
                DamageCabin.On = true;
                viewer.ShowOwned(hurt, spec);

                // THE COCKPIT'S OWN GLASS (round 2 left it: only the windscreen broke from the seat)
                var pz = DamageGlass.CockpitPane(k.panes.z, k.hitL, false);
                var py = DamageGlass.CockpitPane(k.panes.y, k.hitX, true);
                var pc = DamageGlass.CockpitPane(DamageGlass.Crazed, k.hitL, false);
                Check(pz.x == DamageGlass.Shattered && py.x == DamageGlass.Shattered && pc.x == DamageGlass.Crazed
                      && DamageGlass.CockpitPane(0f, k.hitL, false) == Vector4.zero && pz.z > 0f && pz.z < 1f && pz.w > 0f && pz.w < 1f,
                      "the cockpit's side and rear panes get the shell's state and where it was struck", $"left {pz} rear {py}");
                var cg = Shader.Find("PSX/CabinGlass");
                var cgm = cg != null ? new Material(cg) : null;
                Check(cgm != null && cgm.HasProperty("_PSXBreak") && cgm.GetVector("_PSXBreak") == Vector4.zero,
                      "PSX/CabinGlass takes a break per pane (none by default)");
                if (cgm != null) Object.DestroyImmediate(cgm);
            }
            finally
            {
                cam.targetTexture = null;
                if (rt16 != null) Object.DestroyImmediate(rt16);
                if (rt24 != null) Object.DestroyImmediate(rt24);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(go);
            }
        }

        static void TestGlassTrimAndLines()
        {
            Line("damage round 3 - the glass test by the mirrors; no wavy lines on the body (owner 11-17):");
            var def = CarModelLibrary.Load("civic_eg");
            if (def == null) { Check(false, "the EG shell"); return; }
            // the body box's frame as the identity: the mirrors' mounts in metres off the box's centre
            var mirrors = DamageGlass.Mirrors(def, Matrix4x4.identity, Vector3.zero, Vector3.right, Vector3.up, Vector3.forward);
            bool both = DoorMirrors.TryGet("civic_eg", out var e) && e.glass.Length >= 2;
            Check(!both || (mirrors.x < 50f && mirrors.z < 50f && mirrors.y > mirrors.x - 10f),
                  "both door mirrors' mounts known (no pane ahead of their backs and under their tops)", mirrors);
            Check(DamageGlass.Mirrors(null, Matrix4x4.identity, Vector3.zero, Vector3.right, Vector3.up, Vector3.forward) == new Vector4(99f, 0f, 99f, 0f),
                  "a shell with no mirror row: no exclusion");
            var sh = Shader.Find("PSX/CarPaint");
            var m = sh != null ? new Material(sh) : null;
            Check(m != null && m.GetFloat("_PSXHoleMode") == 0f && m.GetFloat("_PSXStComp") == 8f && m.GetFloat("_PSXZWrite") == 1f
                  && m.GetFloat("_PSXColorMask") == 15f && m.GetFloat("_PSXZTest") == 4f,
                  "every car's paint keeps its state (no hole pass, stencil untouched, depth and colour as before)");
            if (m != null) Object.DestroyImmediate(m);
            // a block written: the body's wavy lines off (the global the paint reads)
            var go = new GameObject("Damage3Lines") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var r = go.AddComponent<MeshRenderer>();
                var look = new DamageLook.Look { zones = new Vector4(0f, 0f, 1f, 0f), panes = new Vector4(0f, 0f, 1f, 0f) };
                DamageLook.WriteBlock(r, look, true, Matrix4x4.identity, Quaternion.identity, Vector3.zero, new Vector3(0.9f, 0.7f, 2.2f),
                                      Vector3.forward, Vector3.right, Vector3.up, 0.5f, default, def);
                Check(DamageGlass.NoWavyOn && Shader.GetGlobalFloat("_PSXDmgLines") == 0f,
                      "no wavy contour lines on the body (the cracked-cover and crease lines: PSX_DMG_NOWAVY)");
                var b = new MaterialPropertyBlock();
                r.GetPropertyBlock(b);
                Check(b.GetVector("_PSXGlsM").x < 50f || !both, "the body's block carries the mirrors' mounts", b.GetVector("_PSXGlsM"));
                Check(Shader.GetGlobalFloat("_PSXGlsDark") == 1f, "a hole shows the dark cabin (the cockpit model drawn into it), not boxes");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
