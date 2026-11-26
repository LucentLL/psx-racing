using System.Linq;
using UnityEngine;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CARPARTS (2026-11-26; the owner, of a reference game: "rigged cars
    /// (hood, doors, trunk)", "engine models in engine bay"). Stage 1's pins:
    /// nothing on a car without parts, a pop-up shell or lens B; the hinge
    /// chain puts every part vertex where the closed body draws it (0 mm) at
    /// every fit and never shears through the sweep; the one door time and
    /// the lid time; a re-dress starts shut; the LAB row off shuts every car;
    /// the open-body swap and its readers; ten open-shut cycles leak nothing.
    /// On the STAND-IN (CarPartsStandIn), the only car with parts so far.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        /// <summary>Only this (writes PSXRacing_selftest_carparts.txt).</summary>
        public static void RunCarParts()
        {
            log = new System.Text.StringBuilder();
            failures = 0;
            Guard(nameof(TestCarParts), TestCarParts);
            Line(failures == 0 ? "CARPARTS SELF-TEST OK" : "CARPARTS SELF-TEST FAILED (" + failures + ")");
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "../PSXRacing_selftest_carparts.txt"), log.ToString());
        }

        static void TestCarParts()
        {
            Line("CARPARTS: opening hood, trunk and doors (stage 1: the component and the STAND-IN)");
            int keepOverride = CarPartsStandIn.Override;
            bool keepOn = CarParts.On;
            var anchor = new GameObject("CP_Test");
            try
            {
                CarPartsStandIn.KeyOverride = null;
                CarPartsStandIn.TestOpenBody = false;
                CarPartsStandIn.Forget();
                CarParts.On = true;
                Check(Mathf.Approximately(CarParts.DoorOpenSeconds, 0.6f) && Mathf.Approximately(CarParts.LidOpenSeconds, 0.8f),
                      "one door time 0.6 s, lids 0.8 s", $"{CarParts.DoorOpenSeconds} / {CarParts.LidOpenSeconds}");
                Check(Mathf.Approximately(CarParts.OpenBelowKmh, 5f) && Mathf.Approximately(CarParts.ShutAboveKmh, 15f),
                      "open below 5 km/h, shut above 15 km/h");

                var def = CarModelLibrary.Load(CarPartsStandIn.Key);
                Check(def != null && !def.HasPopUps && !def.HasLensVariant, "the stand-in's shell " + CarPartsStandIn.Key + " loads, fixed lamps, no lens variant");
                if (def == null) return;

                // ---- no stand-in: today's car, nothing at all --------------
                CarPartsStandIn.Override = 0;
                var shell = CarShell.Spawn(anchor.transform, def, 0, out _, solid: false);
                var mf = shell.Find("Body").GetComponent<MeshFilter>();
                int rendsBefore = shell.GetComponentsInChildren<Renderer>(true).Length;
                Check(!def.HasParts && !CarParts.Allowed(def) && CarParts.Available(def) == 0,
                      "a shell without parts: not allowed, nothing available (no prompt, no button, no target)");
                var cp = CarParts.Set(mf, def, CarParts.All, true);
                Check(cp == null && mf.GetComponent<CarParts>() == null && shell.GetComponentsInChildren<Renderer>(true).Length == rendsBefore,
                      "asking a car without parts to open: refused, no component, no renderer");
                Object.DestroyImmediate(shell.gameObject);

                // ---- the stand-in on its shell -----------------------------
                CarPartsStandIn.Override = 1;
                var sdef = CarParts.PartsDef(def);
                Check(sdef != null && sdef.HasParts && CarParts.Available(def) == (CarParts.Hood | CarParts.Trunk | CarParts.DoorL | CarParts.DoorR),
                      "the stand-in: hood, trunk, both doors", CarParts.Available(def));
                Check(sdef != null && sdef.hasEngineMount && sdef.engineLayout == EngineLayout.Longitudinal && sdef.kitMounts != null &&
                      sdef.kitMounts.Select(k => k.name).OrderBy(n => n).SequenceEqual(new[] { "Kit_Battery", "Kit_Radiator" }),
                      "the stand-in: a longitudinal Engine_Mount and Kit_Battery + Kit_Radiator");
                Check(sdef != null && sdef.bodyOpenMesh == null, "the stand-in has no open body (its boxes sit over the intact car)");
                foreach (var l in CarPartsStandIn.Report) Line("    " + l);

                shell = CarShell.Spawn(anchor.transform, def, 0, out _, solid: false);
                mf = shell.Find("Body").GetComponent<MeshFilter>();
                rendsBefore = shell.GetComponentsInChildren<Renderer>(true).Length;
                Check(mf.GetComponent<CarParts>() == null, "a shut stand-in car carries no component (closed car = today's car)");
                cp = CarParts.Set(mf, def, CarParts.Hood, true);
                Check(cp != null && cp.OpenNow == CarParts.Hood && mf.sharedMesh == def.bodyMesh,
                      "hood open: the component is up, the closed body stays (no open body on the stand-in)");
                cp = CarParts.Set(mf, def, 0, true);
                Check(mf.GetComponent<CarParts>() == null && shell.GetComponentsInChildren<Renderer>(true).Length == rendsBefore && mf.sharedMesh == def.bodyMesh,
                      "the last shut: component and part renderers gone, the closed body on the filter");

                // ---- the hinge chain: 0 mm shut, no shear open -------------
                foreach (var s in new[] { new Vector3(0.7f, 1f, 0.7f), Vector3.one, new Vector3(1.2f, 1f, 1.2f),
                                          new Vector3(0.7f, 1f, 1.2f), new Vector3(1.2f, 1f, 0.7f) })
                {
                    mf.transform.localScale = s;
                    cp = CarParts.Set(mf, def, CarParts.All, true);
                    cp.Step(0f);
                    float err = CarPartsShots.ClosedError(cp, mf);
                    float shear = CarPartsShots.MaxEdgeChange(cp);
                    Check(err < 1e-5f, $"fit {s.x:0.0} across x {s.z:0.0} along: with Swing at identity every part vertex is where the closed body draws it",
                          (err * 1000f).ToString("0.0000") + " mm");
                    Check(shear < 1e-5f, $"fit {s.x:0.0} x {s.z:0.0}: no shear through the sweep (edge lengths unchanged)",
                          (shear * 1000f).ToString("0.0000") + " mm");
                    CarParts.TearDown(mf);
                }
                mf.transform.localScale = Vector3.one;

                // ---- the times: eased, the door in 0.6 s, the lid in 0.8 s --
                cp = CarParts.Set(mf, def, CarParts.Hood | CarParts.DoorR);
                int doorAt = -1, hoodAt = -1;
                for (int i = 1; i <= 120; i++)
                {
                    cp.Step(1f / 60f);
                    if (doorAt < 0 && Mathf.Approximately(cp.Angle(CarParts.DoorR), 62f)) doorAt = i;
                    if (hoodAt < 0 && Mathf.Approximately(cp.Angle(CarParts.Hood), 55f)) hoodAt = i;
                }
                // (36 and 48 frames of 1/60 s; one more where the float sum lands a hair short)
                Check((doorAt == 36 || doorAt == 37) && (hoodAt == 48 || hoodAt == 49),
                      "eased open: the door fully open after 0.6 s at 60 fps (frame 36), the hood after 0.8 s (frame 48)", $"{doorAt} / {hoodAt}");
                CarParts.Set(mf, def, 0);
                for (int i = 0; i < 60 && CarParts.Find(mf) != null; i++) CarParts.Find(mf).Step(1f / 60f);
                Check(CarParts.Find(mf) == null && mf.sharedMesh == def.bodyMesh, "eased shut: the component removes itself on the last shut");

                // ---- ten open-shut cycles leak nothing ---------------------
                int go0 = Resources.FindObjectsOfTypeAll<GameObject>().Length, me0 = Resources.FindObjectsOfTypeAll<Mesh>().Length,
                    ma0 = Resources.FindObjectsOfTypeAll<Material>().Length, co0 = Resources.FindObjectsOfTypeAll<Component>().Length;
                // (the first open makes the shared cabin panel mesh and its material once)
                CarParts.Set(mf, def, CarParts.All, true); CarParts.Set(mf, def, 0, true);
                go0 = Resources.FindObjectsOfTypeAll<GameObject>().Length; me0 = Resources.FindObjectsOfTypeAll<Mesh>().Length;
                ma0 = Resources.FindObjectsOfTypeAll<Material>().Length; co0 = Resources.FindObjectsOfTypeAll<Component>().Length;
                for (int c = 0; c < 10; c++)
                {
                    cp = CarParts.Set(mf, def, CarParts.All);
                    for (int i = 0; i < 60; i++) cp.Step(1f / 60f);
                    CarParts.Set(mf, def, 0);
                    for (int i = 0; i < 60 && CarParts.Find(mf) != null; i++) CarParts.Find(mf).Step(1f / 60f);
                }
                int go1 = Resources.FindObjectsOfTypeAll<GameObject>().Length, me1 = Resources.FindObjectsOfTypeAll<Mesh>().Length,
                    ma1 = Resources.FindObjectsOfTypeAll<Material>().Length, co1 = Resources.FindObjectsOfTypeAll<Component>().Length;
                Check(go1 == go0 && me1 == me0 && ma1 == ma0 && co1 == co0 && CarParts.LiveCount == 0,
                      "ten eased open-shut cycles: objects, components, meshes, materials as before, none live",
                      $"objects {go0}->{go1}, components {co0}->{co1}, meshes {me0}->{me1}, materials {ma0}->{ma1}, live {CarParts.LiveCount}");

                // ---- the LAB row off: every part shuts, nothing opens ------
                cp = CarParts.Set(mf, def, CarParts.All, true);
                CarParts.On = false;
                CarParts.ApplyLab();
                Check(CarParts.Find(mf) == null && mf.sharedMesh == def.bodyMesh, "LAB carparts OFF: every open part shut at once");
                Check(CarParts.Set(mf, def, CarParts.All, true) == null && mf.GetComponent<CarParts>() == null && CarParts.Available(def) != 0 && !CarParts.Allowed(def),
                      "LAB carparts OFF: nothing opens");
                CarParts.On = true;
                Object.DestroyImmediate(shell.gameObject);

                // ---- the open-body swap and its readers (a test open body) --
                CarPartsStandIn.TestOpenBody = true;
                sdef = CarParts.PartsDef(def);
                shell = CarShell.Spawn(anchor.transform, def, 0, out _, solid: false);
                mf = shell.Find("Body").GetComponent<MeshFilter>();
                var cover = mf.transform.Find(CarCovers.ChildName);
                bool coverShut = cover != null && cover.gameObject.activeSelf;
                cp = CarParts.Set(mf, def, CarParts.DoorL, true);
                Check(sdef.bodyOpenMesh != null && mf.sharedMesh == sdef.bodyOpenMesh && sdef.IsBodyOf(mf.sharedMesh),
                      "a part open on a shell with an open body: the filter wears it, and it is one of the shell's bodies");
                Check(cp.Renderers.Count() == 5, "with the open body every panel is drawn (4 parts) plus the cabin panel", cp.Renderers.Count());
                bool coverOpen = cover != null && cover.gameObject.activeSelf;
                Check(def.coverMesh == null || (coverShut && coverOpen), "the clear lamp covers stay over the open body", $"{coverShut} / {coverOpen}");
                CarParts.Set(mf, def, 0, true);
                Check(mf.sharedMesh == def.bodyMesh && (def.coverMesh == null || (cover != null && cover.gameObject.activeSelf)),
                      "the last shut: the closed body and its covers back");
                Object.DestroyImmediate(shell.gameObject);
                CarPartsStandIn.TestOpenBody = false;

                // ---- a re-dress always starts shut (CarBody.Apply) ---------
                var carGo = new GameObject("CP_Car");
                carGo.transform.SetParent(anchor.transform, false);
                var body = carGo.AddComponent<CarBody>();
                var bgo = new GameObject("Body");
                bgo.transform.SetParent(carGo.transform, false);
                body.bodyRoot = bgo.transform;
                body.bodyFilter = bgo.AddComponent<MeshFilter>();
                body.bodyRenderer = bgo.AddComponent<MeshRenderer>();
                body.Apply(def, 0);
                CarParts.Set(body.bodyFilter, def, CarParts.All, true);
                body.Apply(def, 0);
                Check(CarParts.Find(body.bodyFilter) == null && body.bodyFilter.sharedMesh == def.bodyMesh,
                      "CarBody.Apply (a repaint, the bench) with parts open: shut first, the closed body");
                CarParts.Set(body.bodyFilter, def, CarParts.All, true);
                body.SetLensCovers(true);
                Check(CarParts.Find(body.bodyFilter) == null, "choosing lens B on the bench shuts every part first");
                body.SetLensCovers(false);

                // ---- refused: a pop-up shell and lens B ---------------------
                var popKey = CarModelLibrary.Models.Select(m => m.key).FirstOrDefault(k => { var d = CarModelLibrary.Load(k); return d != null && d.HasPopUps; });
                if (popKey != null)
                {
                    CarPartsStandIn.KeyOverride = popKey;
                    var pd = CarModelLibrary.Load(popKey);
                    var ps = CarShell.Spawn(anchor.transform, pd, 0, out _, solid: false);
                    var pmf = ps.Find("Body").GetComponent<MeshFilter>();
                    Check(!CarParts.Allowed(pd) && CarParts.Set(pmf, pd, CarParts.All, true) == null && pmf.GetComponent<CarParts>() == null,
                          "a pop-up-lamp shell (" + popKey + ") with the stand-in asked for: refused, no component");
                    Object.DestroyImmediate(ps.gameObject);
                }
                else Check(false, "a pop-up shell to test the refusal on");
                var lensKey = CarModelLibrary.Models.Select(m => m.key).FirstOrDefault(k => { var d = CarModelLibrary.Load(k); return d != null && d.HasLensVariant && !d.HasPopUps; });
                if (lensKey != null)
                {
                    CarPartsStandIn.KeyOverride = lensKey;
                    CarPartsStandIn.Forget();
                    var ld = CarModelLibrary.Load(lensKey);
                    var ls = CarShell.Spawn(anchor.transform, ld, 0, out _, solid: false);
                    var lmf = ls.Find("Body").GetComponent<MeshFilter>();
                    lmf.sharedMesh = ld.lensBodyMesh;
                    bool refused = CarParts.Set(lmf, ld, CarParts.All, true) == null && lmf.GetComponent<CarParts>() == null;
                    lmf.sharedMesh = ld.bodyMesh;
                    bool grantedOnA = CarParts.Set(lmf, ld, CarParts.All, true) != null;
                    CarParts.TearDown(lmf);
                    Check(refused && grantedOnA, "lens B (" + lensKey + "): refused while the body wears B (the same shell on A may open)", $"{refused} / {grantedOnA}");
                    Object.DestroyImmediate(ls.gameObject);
                }
                else Check(false, "a lens-variant shell to test the refusal on");
                CarPartsStandIn.KeyOverride = null;
                CarPartsStandIn.Forget();
            }
            finally
            {
                CarParts.ShutAllNow();
                CarParts.On = keepOn;
                CarPartsStandIn.KeyOverride = null;
                CarPartsStandIn.TestOpenBody = false;
                CarPartsStandIn.Override = keepOverride;
                CarPartsStandIn.Forget();
                Object.DestroyImmediate(anchor);
            }
        }
    }
}
