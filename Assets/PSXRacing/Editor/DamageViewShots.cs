using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// damageview's pictures (owner 2026-11-15: "in the garage view the car is
    /// in mint condition ... the current condition should be visible in all
    /// modes"). ONE process per switch state - PSX_VIEW_DAMAGE=0 (before) or
    /// unset (after); PSX_DV_TAG names the folder. A career NSX, 3 / 3 in the
    /// garage, wrecked through the game's own bank (DamageBank.ApplyIncident,
    /// as a drive's exit folds a crash: a front-left crumple with that headlamp
    /// out, a dented left door, cracked glass - hidden rows with their causes).
    /// Then every page that shows a career car, through LifeHomePreview.Shoot
    /// (the real page and its real CarViewer, 1280 x 720): SPECS (the car pages'
    /// side panel: the owner's spot), SERVICE (the mechanic), the car menu's
    /// stage, MY CARS, INSPECT, the paint booth. Then, on the turntable rig at
    /// its opening angle, the view's own frame beside the DRIVE's look (the
    /// drive's CarBody with DamageLook, the drive's code, placed where the
    /// turntable's car stands) and the on-foot shell (CarShell, as the garage
    /// bay and the pizza shop spawn it). Then &lt; &gt; to the second car (a
    /// clean EG), and the repair (the bench's CLEAR ALL FAULTS, a full repair).
    /// Writes Screenshots/damageview/&lt;tag&gt;/ and PSXRacing_damageview_&lt;tag&gt;.txt.
    /// </summary>
    public static class DamageViewShots
    {
        static readonly (string name, int w, int h)[] Size = { ("landscape_16x9", 1280, 720) };

        public static void Capture()
        {
            var log = new StringBuilder();
            string tag = System.Environment.GetEnvironmentVariable("PSX_DV_TAG");
            if (string.IsNullOrEmpty(tag)) tag = DamageView.On ? "after" : "before";
            string root = Directory.GetParent(Application.dataPath).FullName;
            string outDir = Path.Combine(root, "Screenshots", "damageview", tag);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);
            log.AppendLine("damageview shots: PSX_VIEW_DAMAGE " + (DamageView.On ? "ON" : "0 (OFF)") + ", tag " + tag);
            void Part(string name, System.Action a)
            {
                try { a(); log.AppendLine("ok   " + name + "  (cash " + (LifeSimManager.State != null ? LifeSimManager.State.money : -1) + ")"); }
                catch (System.Exception e) { log.AppendLine("FAIL " + name + ": " + e); Debug.LogError("[DamageView] " + name + " THREW: " + e); }
            }
            try
            {
                // the same dice in both processes: the same save, the same cash, the same lot
                UnityEngine.Random.InitState(1999);
                LifeSimManager.DeleteSave();
                LifeSimManager.StartNewGame("WRECK", 25, LifeRules.DefaultJobIndex);
                var st = LifeSimManager.State;
                log.AppendLine("cash after the new game: " + st.money + " (dice " + UnityEngine.Random.value.ToString("0.0000") + ")");
                LifeRules.SeedFallbackCar(st);
                var nsxSpec = CarCatalog.Get("honda_nsx__90");
                var egSpec = CarCatalog.Get("honda_civic_sir_ii__eg___91");
                if (nsxSpec == null || egSpec == null) { log.AppendLine("FAIL catalog rows missing"); return; }
                var eg = CarMarket.MakeOwnedCar(st, egSpec, 90, 88000f, egSpec.price);
                eg.id = "dv_eg";
                eg.faults.Clear();
                var nsx = CarMarket.MakeOwnedCar(st, nsxSpec, 90, 61000f, nsxSpec.price);
                nsx.id = "dv_nsx";
                nsx.faults.Clear();
                st.cars.Add(eg);
                st.cars.Add(nsx);   // the NSX is 3 / 3, as the owner's
                st.activeCar = nsx.id;
                bool banked = LifeSimSelfTest.DvCrash(st, nsx, 77001, DamageModel.ZFront, -1, 8.6f,
                                   (DamageModel.PanelId, 3, DamageModel.ZFront, -1), (DamageModel.LampId, 1, DamageModel.ZFront, -1))
                            & LifeSimSelfTest.DvCrash(st, nsx, 77002, DamageModel.ZSide, -1, 4.4f, (DamageModel.PanelId, 2, DamageModel.ZSide, -1))
                            & LifeSimSelfTest.DvCrash(st, nsx, 77003, DamageModel.ZRoof, 0, 3.2f, (DamageModel.GlassId, 1, DamageModel.ZRoof, 0));
                LifeSimManager.Save();
                log.AppendLine("cash after the cars and the wreck: " + st.money);
                log.AppendLine("the wreck banked: " + banked + "; rows:");
                foreach (var f in nsx.faults)
                    log.AppendLine("  " + f.id + " stage " + f.stage + " zone " + f.zone1 + " side " + f.hitSide + " hidden " + f.hidden +
                                   " hit " + f.hitId + " cause '" + f.causeNote + "'");

                Part("specs", () => LifeHomePreview.Shoot(outDir, "1_specs_sidepanel", "specs", garageCar: nsx.id, sizes: Size));
                Part("service", () => LifeHomePreview.Shoot(outDir, "2_service_mechanic", "service", garageCar: nsx.id, sizes: Size));
                Part("carmenu", () => LifeHomePreview.Shoot(outDir, "3_carmenu_stage", "carmenu", garageCar: nsx.id, sizes: Size));
                Part("garage", () => LifeHomePreview.Shoot(outDir, "4_mycars", "garage", sizes: Size,
                                                           fields: new[] { ("garageShowId", (object)nsx.id) }));
                Part("inspect", () => LifeHomePreview.Shoot(outDir, "5_inspect", "inspect", sizes: Size,
                                                            fields: new[] { ("inspectCarId", (object)nsx.id) }));
                Part("paint", () => LifeHomePreview.Shoot(outDir, "6_paintbooth", "paint", garageCar: nsx.id, sizes: Size));
                Part("rig", () => RigFrames(outDir, nsx, nsxSpec, log));
                Part("second", () => LifeHomePreview.Shoot(outDir, "8_second_car", "specs", garageCar: eg.id, sizes: Size));
                Part("repair", () =>
                {
                    int n = DebugCarOps.ClearFaults(nsx);
                    LifeSimManager.Save();
                    log.AppendLine("repaired: " + n + " rows fixed, " + nsx.faults.Count + " left");
                    LifeHomePreview.Shoot(outDir, "9_repaired", "specs", garageCar: nsx.id, sizes: Size);
                });
            }
            catch (System.Exception e) { log.AppendLine("FAIL " + e); Debug.LogError("[DamageView] " + e); }
            finally
            {
                File.WriteAllText(Path.Combine(root, "PSXRacing_damageview_" + tag + ".txt"), log.ToString());
                Debug.Log(log.ToString());
            }
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        static void SavePicture(CarViewer v, string path)
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

        /// <summary>The turntable at its opening angle (the front three-quarter
        /// from the left, 215 degrees): the view's own car; the drive's car
        /// (CarBody + DamageLook, the drive's code) where it stood; the on-foot
        /// shell (CarShell) there. The same camera and light for all three.</summary>
        static void RigFrames(string outDir, OwnedCar nsx, CarSpec spec, StringBuilder log)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var host = new GameObject("DV_Viewer");
            var viewer = host.AddComponent<CarViewer>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            viewer.ShowOwned(nsx, spec);
            double firstShow = sw.Elapsed.TotalMilliseconds;
            viewer.SetVisible(true);
            // the dressing alone, as each view opens (and each < >)
            sw.Restart();
            for (int i = 0; i < 200; i++) viewer.Dress(nsx);
            double dress = sw.Elapsed.TotalMilliseconds / 200.0;
            var body = viewer.BodyRenderer;
            var rig = body.transform.parent;
            var watch = rig.GetComponent<DamageViewWatch>();
            sw.Restart();
            for (int i = 0; i < 2000; i++) watch?.CheckNow();
            double check = sw.Elapsed.TotalMilliseconds / 2000.0;
            log.AppendLine($"cost: first ShowOwned (model + dress) {firstShow:0.00} ms; DamageView.Dress {dress:0.000} ms per open; " +
                           $"the watch's check (every {DamageViewWatch.Every} s, InvokeRepeating) {check * 1000.0:0.0} us; per frame 0 (no Update)");

            viewer.RenderNow();
            SavePicture(viewer, Path.Combine(outDir, "rig_1_view.png"));
            int layer = body.gameObject.layer;
            var hide = rig.GetComponentsInChildren<Renderer>(true);
            foreach (var r in hide) if (r.gameObject.name != "Shadow") r.enabled = false;

            // THE DRIVE: the drive's car body and the drive's look, where the turntable's car stands
            var def = CarModelLibrary.LoadFor(spec);
            int skin = Paint.SkinFor(nsx, spec, def);
            var go = new GameObject("DV_DriveCar");
            go.transform.SetPositionAndRotation(rig.TransformPoint(viewer.ShownSlide), rig.rotation);
            go.AddComponent<Rigidbody>().isKinematic = true;
            var car = go.AddComponent<CarController>();
            typeof(CarController).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(car, null);
            var box = go.AddComponent<BoxCollider>();
            var cb = go.AddComponent<CarBody>();
            cb.car = car;
            cb.box = box;
            var bodyGO = new GameObject("Body");
            bodyGO.transform.SetParent(go.transform, false);
            cb.bodyRoot = bodyGO.transform;
            cb.bodyFilter = bodyGO.AddComponent<MeshFilter>();
            cb.bodyRenderer = bodyGO.AddComponent<MeshRenderer>();
            cb.wheelHolders = new Transform[4];
            cb.wheelFilters = new MeshFilter[4];
            cb.wheelRenderers = new MeshRenderer[4];
            for (int i = 0; i < 4; i++)
            {
                var h = new GameObject("WheelHolder" + i);
                h.transform.SetParent(go.transform, false);
                var w = new GameObject("Wheel" + i);
                w.transform.SetParent(h.transform, false);
                cb.wheelHolders[i] = h.transform;
                cb.wheelFilters[i] = w.AddComponent<MeshFilter>();
                cb.wheelRenderers[i] = w.AddComponent<MeshRenderer>();
            }
            var blob = new GameObject("BlobShadow");
            blob.transform.SetParent(go.transform, false);
            cb.blobShadow = blob.transform;
            cb.ApplySpec(spec);
            cb.Apply(def, skin);
            SetLayer(go.transform, layer);
            var resp = go.AddComponent<CollisionResponder>();
            var L = new DamageLedger(car, resp, true, 1) { CarId = nsx.id };
            DamageLook.Attach(car, L);
            DamageLook.Refresh(car);
            var look = car.GetComponent<DamageLook>();
            log.AppendLine("the drive's look on its own body: " + (look != null
                ? $"zones {look.Now.zones} roof {look.Now.roof:0.00} lamps {look.Now.lampOut}" : "NO DamageLook (PSX_FAULT_LOOK=0?)"));
            var view = DamageView.LookOf(nsx);
            log.AppendLine($"the views' look from the same rows: zones {view.zones} roof {view.roof:0.00} lamps {view.lampOut}");
            viewer.RenderNow();
            SavePicture(viewer, Path.Combine(outDir, "rig_2_drive.png"));
            Object.DestroyImmediate(go);

            // ON FOOT: the shell the garage bay and the pizza shop spawn for this car
            var anchor = new GameObject("DV_ShellAnchor");
            anchor.transform.SetPositionAndRotation(rig.position, rig.rotation);
            var shell = CarShell.Spawn(anchor.transform, def, skin, out _, solid: false, spec: spec, owned: nsx);
            if (shell != null) SetLayer(shell, layer);
            viewer.RenderNow();
            SavePicture(viewer, Path.Combine(outDir, "rig_3_onfoot_shell.png"));
            Object.DestroyImmediate(anchor);
            foreach (var r in hide) if (r != null) r.enabled = true;
        }
    }
}
