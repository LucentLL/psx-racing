using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CARPARTS STAGE 4: the engine library (the catalog's shapes, the four
    /// unknown rows), the STAND-IN engines and kit, the mirror rule on both
    /// synthetic test cars with a one-sided engine (and that the test fails
    /// when the rule is off), the clear-box fit, both kit placement paths, the
    /// engine shown only with the hood off its seat, and ten load/release and
    /// open/shut cycles leaking nothing. Edit mode (the loads are at once
    /// here; the async load and the lid's wait are the play check's).
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        static void TestCarPartsEngine()
        {
            Line("CARPARTS STAGE 4: the engine in the bay (STAND-IN engines and kit)");
            var anchor = new GameObject("CP_Test4");
            int keepOverride = CarPartsStandIn.Override;
            try
            {
                // ---- the library ------------------------------------------------
                var cars = CarCatalog.All;
                var byKey = new Dictionary<string, int>();
                foreach (var c in cars) { var k = EngineLibrary.Choose(c, null).key; byKey.TryGetValue(k, out int n); byKey[k] = n + 1; }
                Line("    the catalog by engine model: " + string.Join(", ", byKey.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + kv.Value)));
                var unknown = new (string id, string want)[] { ("mitsubishi_pajero_rally_raid_car__85", EngineLibrary.V6), ("spoon_integra_type_r__dc2___99", EngineLibrary.I4),
                                                               ("ford_gt40_race_car__69", EngineLibrary.V8), ("panoz_esperante_gtr_1_race_car__98", EngineLibrary.V8) };
                foreach (var (id, want) in unknown)
                {
                    var c = CarCatalog.Get(id);
                    var ch = EngineLibrary.Choose(c, null);
                    Check(c != null && ch.key == want, $"eType \"-\": row {id} takes its shape from engineFamily -> {want}", c != null ? ch.key + " (" + ch.source + ")" : "no row");
                }
                foreach (var c in cars.Where(c => (c.eType ?? "").Contains("(-)")))
                {
                    var ch = EngineLibrary.Choose(c, null);
                    Line($"    top end \"-\": {c.id} {c.eType} ({c.engineFamily}) -> {ch.key}{(ch.key == EngineLibrary.V8 ? (ch.pushrod ? " pushrod" : " overhead-cam") : "")}, {ch.layout}, gearbox {ch.gearbox}");
                }
                var undecided = cars.Where(c => c.drv == "MR" || c.drv == "RR" || c.drv == "4WD").Count();
                Line($"    layout by default (no Engine_Mount, no table row): FF transverse, FR/MR/RR/4WD longitudinal - {undecided} MR / RR / 4WD rows taken as longitudinal by default (the catalog does not say)");

                // ---- the stand-in assets ---------------------------------------
                foreach (var key in EngineLibrary.Keys.Concat(new[] { EngineLibrary.BayKit }))
                {
                    var d = CarEngineBay.EngineAssets.Get(key);
                    bool kit = key == EngineLibrary.BayKit;
                    int hd = d != null ? d.Tris(true) : 0, low = d != null ? d.Tris(false) : 0;
                    Check(d != null && d.standIn && d.pieces.Length >= (kit ? 10 : 25) && hd <= (kit ? 3600 : 15000) && low <= (kit ? 1300 : 5000),
                          $"{key}: a STAND-IN in Resources/EngineModels, {(kit ? "the kit's items" : "one piece per fault-table object")}",
                          d != null ? $"{d.pieces.Length} pieces, every option {hd} tris HD / {low} low, exhaust {(d.exhaustSide > 0 ? "+X" : d.exhaustSide < 0 ? "-X" : "both")}" : "missing");
                }

                // ---- THE MIRROR, on both test cars, a one-sided engine -----------
                foreach (var key in CarModelBaker.TestKeys)
                {
                    var def = CarModelBaker.LoadTestCar(key);
                    if (def == null) { CarModelBaker.BakeTestCars(); def = CarModelBaker.LoadTestCar(key); }
                    if (def == null || !def.hasEngineMount) { Check(false, key + ": baked with an Engine_Mount"); continue; }
                    bool four = key.Contains("4door");
                    foreach (bool rule in new[] { true, false })
                    {
                        EngineLibrary.Table[def.key] = new EngineLibrary.Row { key = EngineLibrary.I4 };
                        CarEngineBay.NoReflect = !rule;
                        var shell = CarShell.Spawn(anchor.transform, def, 0, out _, solid: false);
                        var mf = shell.Find("Body").GetComponent<MeshFilter>();
                        var bay = CarEngineBay.Preload(mf, def);
                        if (bay == null || !bay.Ready) { Check(false, key + ": the engine loads"); Object.DestroyImmediate(shell.gameObject); continue; }
                        var eng = CarEngineBay.EngineAssets.Get(EngineLibrary.I4);
                        var man = eng.pieces.First(p => p.name == "Eng_ExhaustManifold");
                        var c = (man.min + man.max) * 0.5f;
                        if (bay.Reflected) c.x = -c.x;
                        var et = bay.EngineRenderer.transform;
                        // in the CAR's frame (the shell root: +X right, +Z forward), against what was AUTHORED:
                        // the two-door's downpipe is on the car's right; the four-door's behind its transverse engine
                        var m = shell.InverseTransformPoint(et.TransformPoint(c));
                        var o = shell.InverseTransformPoint(et.position);
                        bool rightSide = four ? m.z < o.z : m.x > o.x;
                        string where = four ? (m.z < o.z ? "behind the engine (the downpipe's side)" : "IN FRONT of the engine") : (m.x > o.x ? "on the car's right (the downpipe's side)" : "ON THE LEFT");
                        if (rule)
                            Check(rightSide, $"{key} ({(four ? "transverse" : "longitudinal")}): the inline-4's exhaust manifold on the downpipe's side, {where}",
                                  $"reflected {bay.Reflected}, car downpipe side {def.engineDownpipeSide}, engine exhaust {eng.exhaustSide}");
                        else
                            Check(!rightSide, $"{key}: with the mirror rule off the same test FAILS (it can tell a wrong sign): {where}");
                        CarEngineBay.Release(mf);
                        Object.DestroyImmediate(shell.gameObject);
                    }
                    CarEngineBay.NoReflect = false;
                    EngineLibrary.Table.Remove(def.key);

                    // ---- the fit, the kit's two paths, shown only with the hood up ----
                    var sh2 = CarShell.Spawn(anchor.transform, def, 0, out _, solid: false);
                    var mf2 = sh2.Find("Body").GetComponent<MeshFilter>();
                    var b2 = CarEngineBay.Preload(mf2, def);
                    Check(b2 != null && b2.Scale >= EngineLibrary.ScaleMin && b2.Scale <= EngineLibrary.ScaleMax,
                          $"{key}: the engine at x{b2?.Scale:0.000} (0.92-1.08) for its clear box{(b2 != null && b2.Why.Length > 0 ? " - " + b2.Why : "")}", b2 != null ? b2.Choice.key + " " + b2.Choice.layout : "-");
                    var kp = b2 != null ? b2.KitPieces : new List<string>();
                    Check(kp.Contains("Bay_Battery @Kit_Battery") && kp.Contains("Bay_Radiator @Kit_Radiator") && kp.Any(p => p.EndsWith("@default")),
                          $"{key}: the kit at the car's Kit_ empties where it has them, at the defaults otherwise", string.Join(", ", kp));
                    Check(b2 != null && !b2.EngineRenderer.enabled && !b2.KitRenderer.enabled, $"{key}: loaded, the engine and kit drawn not at all while the hood is shut");
                    var cp = CarParts.Set(mf2, def, CarParts.Hood, true);
                    Check(b2 != null && b2.EngineRenderer.enabled && b2.KitRenderer.enabled, $"{key}: hood open, the engine and kit drawn");
                    CarParts.Set(mf2, def, 0, true);
                    Check(b2 != null && !b2.EngineRenderer.enabled, $"{key}: hood shut, off again");
                    // ten open-shut cycles and ten load / release cycles
                    int me0 = Resources.FindObjectsOfTypeAll<Mesh>().Length, go0 = Resources.FindObjectsOfTypeAll<GameObject>().Length;
                    for (int i = 0; i < 10; i++)
                    {
                        CarParts.Set(mf2, def, CarParts.Hood, true);
                        CarParts.Set(mf2, def, 0, true);
                        CarEngineBay.Release(mf2);
                        CarEngineBay.Preload(mf2, def);
                    }
                    int me1 = Resources.FindObjectsOfTypeAll<Mesh>().Length, go1 = Resources.FindObjectsOfTypeAll<GameObject>().Length;
                    Check(me1 == me0 && go1 == go0 && CarEngineBay.LiveCount == 1, $"{key}: ten open-shut and ten load-release cycles: meshes, objects as before, one bay live",
                          $"meshes {me0}->{me1}, objects {go0}->{go1}, live {CarEngineBay.LiveCount}");
                    CarEngineBay.Release(mf2);
                    Check(CarEngineBay.EngineAssets.Users(b2 != null ? b2.Choice.key : "") == 0, $"{key}: released, nobody holds its engine");
                    Object.DestroyImmediate(sh2.gameObject);
                }
                if (CarEngineBay.FitFails.Count > 0) Line("    still too big for their clear box at 0.92: " + string.Join(", ", CarEngineBay.FitFails));

                // ---- a car without parts: nothing --------------------------------
                CarPartsStandIn.Override = 0;
                var plain = CarModelLibrary.Load(CarPartsStandIn.Key);
                var ps = CarShell.Spawn(anchor.transform, plain, 0, out _, solid: false);
                var pmf = ps.Find("Body").GetComponent<MeshFilter>();
                Check(CarEngineBay.Preload(pmf, plain) == null && pmf.GetComponent<CarEngineBay>() == null, "a car without parts: no engine loaded, no component");
                Object.DestroyImmediate(ps.gameObject);
            }
            finally
            {
                CarEngineBay.NoReflect = false;
                CarParts.ShutAllNow();
                CarPartsStandIn.Override = keepOverride;
                CarPartsStandIn.Forget();
                Object.DestroyImmediate(anchor);
            }
        }
    }
}
