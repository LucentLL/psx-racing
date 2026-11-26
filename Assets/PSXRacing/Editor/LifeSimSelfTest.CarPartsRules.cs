using System.Linq;
using UnityEngine;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CARPARTS STAGE 3: the rules (speed, place, cockpit, the bonnet cap), the
    /// one-control cycle, the LAB rows, the walker's targets and the wheel's
    /// OPEN slot. On the STAND-IN (CarPartsStandIn), edit mode.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        static void TestCarPartsRules()
        {
            Line("CARPARTS STAGE 3: the speed and place rules, the cycle, the cap, the LAB rows, the walker, the wheel slot");
            int keepOverride = CarPartsStandIn.Override, keepCap = CarParts.CapOverride;
            bool keepOn = CarParts.On;
            var anchor = new GameObject("CP_Test3");
            try
            {
                CarParts.On = true;
                CarPartsStandIn.KeyOverride = null;
                CarPartsStandIn.TestOpenBody = false;
                CarPartsStandIn.Override = 1;
                CarPartsStandIn.Forget();
                var def = CarModelLibrary.Load(CarPartsStandIn.Key);
                if (def == null) { Check(false, "the stand-in's shell loads"); return; }

                // ---- the cycle ----------------------------------------------
                int all4 = CarParts.Hood | CarParts.Trunk | CarParts.DoorL | CarParts.DoorR;
                string Walk(int avail, bool doors)
                {
                    var seq = new System.Collections.Generic.List<string>();
                    int m = 0;
                    for (int i = 0; i < 7; i++) { m = CarParts.NextStep(m, avail, doors); seq.Add(m.ToString()); if (m == 0) break; }
                    return string.Join(" ", seq);
                }
                Check(Walk(all4, true) == "1 2 12 15 0", "the cycle: SHUT, HOOD, TRUNK, DOORS, ALL, SHUT", Walk(all4, true));
                Check(Walk(all4, false) == "1 2 3 0", "in the cockpit view the doors are skipped: SHUT, HOOD, TRUNK, ALL (the lids), SHUT", Walk(all4, false));
                Check(Walk(CarParts.Hood | CarParts.DoorL | CarParts.DoorR, true) == "1 12 13 0", "a car with no trunk skips TRUNK", Walk(CarParts.Hood | CarParts.DoorL | CarParts.DoorR, true));
                Check(Walk(CarParts.Hood, true) == "1 0", "a car with only a hood: HOOD, SHUT", Walk(CarParts.Hood, true));
                Check(CarParts.NextStep(CarParts.DoorL, all4, true) == 0, "a state off the cycle (one door opened on foot) steps to SHUT");

                // ---- the speed and place rules ------------------------------
                Check(CarParts.SpeedOpens(4.9f) && !CarParts.SpeedOpens(5f) && !CarParts.SpeedShuts(15f) && CarParts.SpeedShuts(15.1f),
                      "parts open below 5 km/h and shut themselves above 15 km/h (between: as they are)");
                var shell = CarShell.Spawn(anchor.transform, def, 0, out _, solid: false);
                var mf = shell.Find("Body").GetComponent<MeshFilter>();
                bool g = CarParts.RequestAt(mf, def, CarParts.Hood, true, 10f, false, false);
                Check(!g && mf.GetComponent<CarParts>() == null, "at 10 km/h the hood is refused, nothing made");
                g = CarParts.RequestAt(mf, def, CarParts.Hood, true, 3f, false, false);
                Check(g && CarParts.MaskOf(mf) == CarParts.Hood, "at 3 km/h the hood opens");
                g = CarParts.RequestAt(mf, def, CarParts.Hood | CarParts.Trunk, true, 10f, false, false);
                Check(!g && CarParts.MaskOf(mf) == CarParts.Hood, "at 10 km/h the trunk is refused, the open hood stays (the hysteresis)");
                g = CarParts.RequestAt(mf, def, 0, true, 40f, true, false);
                Check(g && CarParts.MaskOf(mf) == 0, "closing is always granted (40 km/h, in a race)");
                g = CarParts.RequestAt(mf, def, CarParts.Hood, true, 0f, true, false);
                Check(!g && CarParts.MaskOf(mf) == 0, "in a race, a replay or a delivery nothing opens, even parked");
                g = CarParts.RequestAt(mf, def, CarParts.All, true, 0f, false, true);
                Check(!g && CarParts.MaskOf(mf) == CarParts.Lids, "in the cockpit view the doors stay shut, the lids open", CarParts.MaskOf(mf));
                CarParts.TearDown(mf);
                Object.DestroyImmediate(shell.gameObject);

                // ---- the bonnet cap -----------------------------------------
                foreach (int cap in new[] { 4, 2 })
                {
                    CarParts.CapOverride = cap;
                    var shells = new System.Collections.Generic.List<MeshFilter>();
                    for (int i = 0; i < cap + 2; i++)
                    {
                        var a = new GameObject("CP_Cap" + i);
                        a.transform.SetParent(anchor.transform, false);
                        a.transform.localPosition = new Vector3(i * 6f, 0f, 0f);
                        shells.Add(CarShell.Spawn(a.transform, def, 0, out _, solid: false).Find("Body").GetComponent<MeshFilter>());
                    }
                    int granted = 0;
                    for (int i = 0; i < cap + 1; i++) if (CarParts.RequestAt(shells[i], def, CarParts.Hood, false, 0f, false, false)) granted++;
                    Check(granted == cap, $"cap {cap}: {cap} other cars' bonnets open, the next is refused", granted);
                    bool mine = CarParts.RequestAt(shells[cap + 1], def, CarParts.Hood, true, 0f, false, false);
                    int open = shells.Count(s => (CarParts.MaskOf(s) & CarParts.Hood) != 0);
                    bool farShut = (CarParts.MaskOf(shells[0]) & CarParts.Hood) == 0;
                    Check(mine && open == cap && farShut, $"cap {cap}: the player's car still gets one; the farthest other bonnet shuts",
                          $"granted {mine}, open {open}, farthest shut {farShut}");
                    CarParts.ShutAllNow();
                    foreach (var s in shells) Object.DestroyImmediate(s.transform.parent.parent.gameObject);
                }
                CarParts.CapOverride = 0;
                Check(CarParts.BonnetCap == (PlatformProfile.PC ? 4 : 2), $"the profile's cap: {(PlatformProfile.PC ? "PC 4" : "non-PC 2")}", CarParts.BonnetCap);

                // ---- the LAB rows -------------------------------------------
                shell = CarShell.Spawn(anchor.transform, def, 0, out _, solid: false);
                mf = shell.Find("Body").GetComponent<MeshFilter>();
                CarParts.RequestAt(mf, def, CarParts.Hood | CarParts.DoorR, true, 0f, false, false);
                CarParts.Find(mf).Snap();
                CarParts.On = false; CarParts.ApplyLab();
                bool shutOff = CarParts.Find(mf) == null && mf.sharedMesh == def.bodyMesh;
                CarParts.On = true; CarParts.ApplyLab();
                Check(shutOff && CarParts.MaskOf(mf) == (CarParts.Hood | CarParts.DoorR),
                      "LAB carparts OFF shuts every car; ON again gives each its parts back (live both ways)", CarParts.MaskOf(mf));
                CarPartsStandIn.Override = -1;
                bool keepPrev = CarPartsStandIn.Preview;
                CarPartsStandIn.Preview = false; CarPartsStandIn.ApplyLab();
                bool prevOff = CarParts.Find(mf) == null && !CarParts.Allowed(def);
                CarPartsStandIn.Preview = true;
                bool prevOn = CarParts.Allowed(def);
                CarPartsStandIn.Preview = keepPrev;
                CarPartsStandIn.Override = 1;
                Check(prevOff && prevOn, "LAB STAND-IN PREVIEW: off shuts and bars the stand-in, on allows it");
                var row = LabSwitches.Find("carpartspreview");
                var row1 = LabSwitches.Find("carparts");
                Check(row != null && row.Present && !row.Default && row1 != null && row1.Present && row1.Default,
                      "LAB rows: OPEN HOOD, TRUNK AND DOORS default ON; OPENING PARTS: STAND-IN PREVIEW default OFF");

                // ---- the walker's targets ------------------------------------
                var feet = CarPartsFoot.Attach(shell, mf, def);
                Check(feet != null && feet.Targets.Count == 4 && feet.HoodTarget != null &&
                      feet.Targets.All(t => t.t.verb == "OPEN" && t.t.action.StartsWith("OPEN THE ")),
                      "the walker: one target per part (hood, trunk, both doors), each offering OPEN", feet != null ? feet.Targets.Count : 0);
                if (feet != null)
                {
                    var trunkT = feet.Targets.First(t => t.bit == CarParts.Trunk).t;
                    trunkT.onUse();
                    Check((CarParts.MaskOf(mf) & CarParts.Trunk) != 0 && trunkT.verb == "SHUT", "USE at the trunk opens it, and its prompt says SHUT");
                    trunkT.onUse();
                    Check((CarParts.MaskOf(mf) & CarParts.Trunk) == 0, "USE again shuts it");
                    CarPartsFoot.Detach(shell);
                }
                CarParts.TearDown(mf);
                Object.DestroyImmediate(shell.gameObject);
                CarPartsStandIn.Override = 0;
                shell = CarShell.Spawn(anchor.transform, def, 0, out _, solid: false);
                mf = shell.Find("Body").GetComponent<MeshFilter>();
                Check(CarPartsFoot.Attach(shell, mf, def) == null && shell.GetComponent<CarPartsFoot>() == null &&
                      shell.GetComponentsInChildren<FootTarget>(true).Length == 0, "a car without parts: no target, no component");
                Object.DestroyImmediate(shell.gameObject);

                // ---- the wheel's OPEN slot -----------------------------------
                Check(WheelCapture.ButtonBinding(WheelCapture.Target.Open) == WheelConfig.Data.open && WheelConfig.Data.open != null,
                      "the wheel: an OPEN slot appended (WheelInput's arrays sized from the enum), unbound by default",
                      WheelConfig.Data.open != null && WheelConfig.Data.open.Bound ? "BOUND" : "unbound");
                var more = WheelSetupCatalog.Items(System.Array.IndexOf(WheelSetupCatalog.TabNames, "MORE"));
                var buttons = WheelSetupCatalog.Items(System.Array.IndexOf(WheelSetupCatalog.TabNames, "BUTTONS"));
                Check(more.Any(r => r.name == "OPEN") && buttons.Count <= 7, "WHEEL SETUP > MORE has the OPEN row (BUTTONS keeps its seven)",
                      $"MORE {string.Join(",", more.Select(r => r.name))}; BUTTONS {buttons.Count} rows");
            }
            finally
            {
                CarParts.ShutAllNow();
                CarParts.On = keepOn;
                CarParts.CapOverride = keepCap;
                CarPartsStandIn.Override = keepOverride;
                CarPartsStandIn.Forget();
                Object.DestroyImmediate(anchor);
            }
        }
    }
}
