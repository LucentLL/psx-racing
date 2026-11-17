using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Damage round 2's still pictures (owner on PC48, 2026-11-16, on a red EG
    /// Civic in the turntable view: "the broken glass effect is very poor and
    /// does not communicate the damage to me. I'd also like to see scuffing for
    /// wheels"). ONE process per switch state: PSX_DMG_GLASS2=0 PSX_DMG_WHEELS=0
    /// (before) or unset (after); PSX_D2_TAG names the folder. A career red EG
    /// Civic takes each case's rows through the game's own bank
    /// (DamageBank.ApplyIncident), and each case is shown
    ///   - on the CAR MENU's showroom stage (the owner's view: the real page,
    ///     LifeHomePreview.Shoot, 1280 x 720), and
    ///   - on the turntable rig at 1280 x 800 with the camera moved in: the
    ///     windscreen, the left glass, the front-left lamp, the front-left
    ///     wheel, and from behind at the chase camera's distance.
    /// Cases: the windscreen's chip, crack and web (roof_glass 1 / 1 / 2, the
    /// web's with every tempered pane crazed), a crushed roof (every tempered
    /// pane shattered), a front hit with the bar pushed back (the web low on
    /// the struck side, that lamp broken), a dented and a crumpled left door
    /// (crazed, shattered), and wheel rash at stages 1, 2 and 3.
    /// Writes Screenshots/damage2/&lt;tag&gt;/ and PSXRacing_damage2_&lt;tag&gt;.txt.
    /// </summary>
    public static class Damage2Shots
    {
        static readonly (string name, int w, int h)[] Size = { ("landscape_16x9", 1280, 720) };

        const int F = DamageModel.ZFront, S = DamageModel.ZSide, T = DamageModel.ZRoof;

        static readonly (string name, System.Action<LifeState, OwnedCar> wreck, string frames)[] Cases =
        {
            ("g1_chip", (s, c) => LifeSimSelfTest.DvCrash(s, c, 88101, T, 0, 3.5f, (DamageModel.GlassId, 1, T, 0)), "front,screen"),
            ("g2_crack", (s, c) => LifeSimSelfTest.DvCrash(s, c, 88102, T, 0, 6f, (DamageModel.GlassId, 1, T, 0)), "front,screen"),
            ("g3_web", (s, c) => LifeSimSelfTest.DvCrash(s, c, 88103, T, 0, 7f, (DamageModel.GlassId, 2, T, 0)), "front,screen,side,chase"),
            ("g4_crushed", (s, c) => LifeSimSelfTest.DvCrash(s, c, 88104, T, 0, 12f, (DamageModel.GlassId, 3, T, 0)), "front,side,chase"),
            ("f_web", (s, c) => LifeSimSelfTest.DvCrash(s, c, 88105, F, -1, 9f, (DamageModel.PanelId, 3, F, -1), (DamageModel.BarId, 2, F, 0),
                                                        (DamageModel.LampId, 1, F, -1)), "front,screen,lamp"),
            ("s2_crazed", (s, c) => LifeSimSelfTest.DvCrash(s, c, 88106, S, -1, 4.4f, (DamageModel.PanelId, 2, S, -1)), "front,side"),
            ("s3_shattered", (s, c) => LifeSimSelfTest.DvCrash(s, c, 88107, S, -1, 7f, (DamageModel.PanelId, 3, S, -1)), "front,side"),
            ("w1_rash", (s, c) => { LifeSimSelfTest.D2Wheel(s, c, 88111, 0, 1, "curb, West Trade Street"); LifeSimSelfTest.D2Wheel(s, c, 88112, 2, 1, "curb, West Trade Street"); }, "wheel,chase"),
            ("w2_rash", (s, c) => { LifeSimSelfTest.D2Wheel(s, c, 88113, 0, 2, "curb, West Trade Street"); LifeSimSelfTest.D2Wheel(s, c, 88114, 2, 2, "curb, West Trade Street"); }, "wheel,chase"),
            ("w3_rash", (s, c) => { LifeSimSelfTest.D2Wheel(s, c, 88115, 0, 3, "curb, West Trade Street"); LifeSimSelfTest.D2Wheel(s, c, 88116, 2, 3, "curb, West Trade Street"); }, "front,wheel,chase"),
        };

        public static void Capture()
        {
            var log = new StringBuilder();
            string tag = System.Environment.GetEnvironmentVariable("PSX_D2_TAG");
            if (string.IsNullOrEmpty(tag)) tag = DamageGlass.On ? "after" : "before";
            string root = Directory.GetParent(Application.dataPath).FullName;
            string outDir = Path.Combine(root, "Screenshots", "damage2", tag);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);
            log.AppendLine("damage2 shots: PSX_DMG_GLASS2 " + (DamageGlass.On ? "ON" : "0") + ", PSX_DMG_WHEELS " + (WheelScuff.On ? "ON" : "0") + ", tag " + tag);
            try
            {
                UnityEngine.Random.InitState(1999);
                LifeSimManager.DeleteSave();
                LifeSimManager.StartNewGame("GLASS", 25, LifeRules.DefaultJobIndex);
                var st = LifeSimManager.State;
                LifeRules.SeedFallbackCar(st);
                var spec = CarCatalog.Get(System.Environment.GetEnvironmentVariable("PSX_D2_CAR") ?? "honda_civic_sir_ii__eg___91");
                if (spec == null) { log.AppendLine("FAIL no catalog row"); return; }
                var eg = CarMarket.MakeOwnedCar(st, spec, 92, 88000f, spec.price);
                eg.id = "d2_eg";
                var def = Paint.DefFor(spec);
                if (def != null)
                    for (int i = 0; i < def.SkinCount; i++)
                        if (Paint.LabelOf(def, i).ToUpperInvariant() == "RED") { eg.paintSkin = def.skinNames[i]; break; }
                st.activeCar = eg.id;
                log.AppendLine("car " + spec.name + ", paint " + (string.IsNullOrEmpty(eg.paintSkin) ? "factory" : eg.paintSkin) + ", HD tier " + CarHD.Want);
                foreach (var c in Cases)
                {
                    try
                    {
                        eg.faults.Clear();
                        if (eg.history != null) eg.history.Clear();
                        eg.paint = 92f;
                        eg.carHP = 92f;
                        c.wreck(st, eg);
                        LifeSimManager.Save();
                        var look = DamageLook.Read(eg.faults, null);
                        log.AppendLine(c.name + ": rows " + string.Join(", ", eg.faults.ConvertAll(f => f.id + " s" + f.stage + " '" + f.causeNote + "'")) +
                                       " -> panes " + look.panes + " rash " + look.rash);
                        LifeHomePreview.Shoot(outDir, c.name, "carmenu", garageCar: eg.id, sizes: Size);
                        Rig(outDir, c.name, c.frames, eg, spec, log);
                    }
                    catch (System.Exception e) { log.AppendLine("FAIL " + c.name + ": " + e); Debug.LogError("[Damage2] " + c.name + " THREW: " + e); }
                }
            }
            catch (System.Exception e) { log.AppendLine("FAIL " + e); Debug.LogError("[Damage2] " + e); }
            finally
            {
                File.WriteAllText(Path.Combine(root, "PSXRacing_damage2_" + tag + ".txt"), log.ToString());
                Debug.Log(log.ToString());
            }
        }

        /// <summary>The turntable rig at 1280 x 800, its camera moved in on the car's own frame.</summary>
        static void Rig(string outDir, string name, string frames, OwnedCar car, CarSpec spec, StringBuilder log)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var host = new GameObject("D2_Viewer");
            var viewer = host.AddComponent<CarViewer>();
            viewer.width = 1280;
            viewer.height = 800;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            viewer.ShowOwned(car, spec);
            double ms = sw.Elapsed.TotalMilliseconds;
            viewer.SetVisible(true);
            viewer.RenderNow();   // (sets the turntable's opening angle)
            var cam = host.GetComponentInChildren<Camera>(true);
            var rig = viewer.BodyRenderer.transform.parent;
            if (cam == null) { log.AppendLine("  no rig camera"); return; }
            var def = viewer.Shown;
            var fit = CarModelLibrary.Fit(def, spec);
            Vector3 hubFL = fit.Hub(0), o = viewer.ShownSlide;
            float half = def.colliderSize.z * fit.sz * 0.5f, cz = fit.P(def.colliderCenter).z;
            float cowl = def.cowlZ * fit.sz, belt = def.cowlY;
            Vector3 P(Vector3 local) => rig.TransformPoint(o + local);
            void Frame(string f, Vector3 from, Vector3 at, float fov)
            {
                float was = cam.fieldOfView;
                Vector3 wasP = cam.transform.position; Quaternion wasR = cam.transform.rotation;
                cam.fieldOfView = fov;
                cam.transform.SetPositionAndRotation(P(from), Quaternion.LookRotation(P(at) - P(from), Vector3.up));
                viewer.RenderNow();
                Save(viewer, Path.Combine(outDir, "rig_" + name + "_" + f + ".png"));
                cam.fieldOfView = was;
                cam.transform.SetPositionAndRotation(wasP, wasR);
            }
            foreach (var f in frames.Split(','))
            {
                switch (f)
                {
                    case "front": viewer.RenderNow(); Save(viewer, Path.Combine(outDir, "rig_" + name + "_front.png")); break;
                    case "screen": Frame(f, new Vector3(-1.0f, belt + 0.75f, cowl + 1.9f), new Vector3(0f, belt + 0.18f, cowl - 0.35f), 30f); break;
                    case "side": Frame(f, new Vector3(-2.7f, belt + 0.35f, cowl - 0.5f), new Vector3(-0.6f, belt + 0.12f, cowl - 0.85f), 30f); break;
                    case "lamp": Frame(f, new Vector3(-1.5f, 0.85f, cz + half + 1.5f), new Vector3(-0.45f, 0.6f, cz + half - 0.25f), 30f); break;
                    case "wheel": Frame(f, new Vector3(hubFL.x - 1.25f, hubFL.y + 0.22f, hubFL.z + 0.55f), hubFL, 30f); break;
                    case "chase": Frame(f, new Vector3(-2.6f, 2.0f, cz - half - 5.6f), new Vector3(0f, 0.75f, cz), 34f); break;
                }
            }
            log.AppendLine("  rig " + name + ": " + frames + "; first ShowOwned " + ms.ToString("0.0") + " ms, dresses " + DamageView.Dresses);
        }

        static void Save(CarViewer v, string path)
        {
            var rt = v.Picture;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
