using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LAB (2026-11-23; the owner: "Why can't they be toggles in the main build and I can approve to keep as
    /// option or make default?"). LabSwitches holds the test launchers' switches as rows of the settings menu's LAB
    /// page. Checked here, in edit mode, on the statics themselves:
    ///  - every row resolves, is unique, and its timing fits its members (a NOW / NEXT LOAD row writes them);
    ///  - every row flips and restores (a NOW row at once, a NEXT LOAD row only at a load), every member back;
    ///  - a choice persists (psx.lab.&lt;id&gt;, under a scratch prefix here) and comes back at the next boot;
    ///  - a set environment variable wins over a saved choice;
    ///  - a test run (a tour, batch mode, the editor) ignores the saved choices and never writes them;
    ///  - the changed-rows line names what is off its default; a row whose switch does not exist never shows;
    ///  - the page: a LAB row on SETTINGS (front end and drive), every tab's rows, RESET ALL on every tab.
    /// The owner's own keys are never touched (KeyPrefix is a scratch one while this runs).
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        /// <summary>Only this (writes PSXRacing_selftest_lab.txt).</summary>
        public static void RunLab()
        {
            log = new System.Text.StringBuilder();
            failures = 0;
            Guard(nameof(TestLab), TestLab);
            Line(failures == 0 ? "LAB SELF-TEST OK" : "LAB SELF-TEST FAILED (" + failures + ")");
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "../PSXRacing_selftest_lab.txt"), log.ToString());
        }

        static void TestLab()
        {
            Line("LAB: the test switches as in-game rows (LabSwitches, LabCatalog)");
            string prefixWas = LabSwitches.KeyPrefix;
            const string Scratch = "psx.lab.selftest.";
            LabSwitches.KeyPrefix = Scratch;
            LabSwitches.Boot(true);
            var rows = LabSwitches.Rows();
            var snap = rows.ToDictionary(r => r.id, r => LabSwitches.MemberValues(r));
            var envWas = new Dictionary<string, string>();
            try
            {
                foreach (var r in rows) PlayerPrefs.DeleteKey(Scratch + r.id);
                // ---- the registry -------------------------------------------------
                Check(rows.Count >= (PlatformProfile.PC ? 17 : 12), "the rows are registered (17 on the PC profile, 12 without its PC-only rows)", rows.Count);
                Check(rows.Select(r => r.id).Distinct().Count() == rows.Count, "every row id is unique");
                foreach (var r in rows)
                {
                    bool fits = r.when == LabWhen.BuiltIn ? true : r.members.Length > 0 && r.env.Length > 0;
                    Check(fits && !string.IsNullOrEmpty(r.look) && r.name == r.name.ToUpperInvariant() && r.name.Length <= 26,
                          "row " + r.id + " (" + r.when + "): a plain upper-case name, a line to look at, its variables and statics",
                          r.name + " / " + LabSwitches.MemberList(r));
                }
                foreach (System.Enum g in System.Enum.GetValues(typeof(LabGroup)))
                    Check(LabSwitches.Rows((LabGroup)g).Count > 0, "the " + g + " tab has rows");
                Check(LabCatalog.TabNames.Length == System.Enum.GetValues(typeof(LabGroup)).Length, "one LAB tab per group");

                // ---- a row whose switch does not exist (an unmerged branch) never shows
                LabSwitches.Register(new LabRow { id = "selftest_missing", group = LabGroup.Lights, name = "MISSING", when = LabWhen.Now,
                                                  look = "x", env = new[] { "PSX_SELFTEST_MISSING" }, members = new[] { "StreetLights.NoSuchSwitchAtAll" } });
                Check(LabSwitches.Find("selftest_missing") != null && !LabSwitches.Find("selftest_missing").Present &&
                      !LabSwitches.Rows().Any(r => r.id == "selftest_missing"),
                      "a row registered by a name that does not resolve is hidden (NIGHT 4 / TUNNEL 3 before their merge)");
                LabSwitches.Boot(true);
                rows = LabSwitches.Rows();

                // ---- every row flips and restores ---------------------------------
                foreach (var r in rows.Where(r => r.when != LabWhen.BuiltIn))
                {
                    bool was = LabSwitches.Live(r);
                    var before = LabSwitches.MemberValues(r);
                    bool setOk = LabSwitches.Set(r, !r.Chosen, false);
                    bool afterSet = LabSwitches.Live(r);
                    bool pending = LabSwitches.Pending(r);
                    LabSwitches.ApplyPending();
                    bool afterLoad = LabSwitches.Live(r);
                    var flipped = LabSwitches.MemberValues(r);
                    bool allFlipped = flipped.Select((v, i) => v != before[i]).All(x => x);
                    LabSwitches.Set(r, !r.Chosen, false);
                    LabSwitches.ApplyPending();
                    var back = LabSwitches.MemberValues(r);
                    bool restored = back.SequenceEqual(before) && LabSwitches.Live(r) == was && !LabSwitches.Pending(r);
                    bool timing = r.when == LabWhen.Now ? afterSet != was && !pending : afterSet == was && pending && afterLoad != was;
                    Check(setOk && timing && allFlipped && restored,
                          "row " + r.id + " flips (" + (r.when == LabWhen.Now ? "at once" : "at the next load, not before") + "), every member, and restores",
                          $"set {setOk} timing {timing} all {allFlipped} back {restored}");
                }
                foreach (var r in rows.Where(r => r.when == LabWhen.BuiltIn))
                    Check(!LabSwitches.Set(r, !r.Chosen, false), "built-in row " + r.id + " cannot be flipped at run time");

                // ---- the changed line ---------------------------------------------
                var n3 = LabSwitches.Find("night3");
                var haze = LabSwitches.Find("farhaze2");
                Check(LabSwitches.ChangedLine == null, "nothing changed: no LAB line", LabSwitches.ChangedLine);
                LabSwitches.Set(n3, false, false);
                LabSwitches.Set(haze, false, false);
                string line = LabSwitches.ChangedLine ?? "";
                Check(line.StartsWith("LAB: ") && line.Contains("NIGHT 3 old") && line.Contains("FAR HAZE old"),
                      "two rows changed: the pause / debug line names both", line);
                Check(LabCatalog.Summary() == "2 CHANGED", "and SETTINGS > LAB says 2 CHANGED", LabCatalog.Summary());
                LabSwitches.ResetAll(false);
                Check(LabSwitches.ChangedLine == null && StreetLights.Night3 && TimeOfDay.FarHaze2,
                      "RESET ALL TO DEFAULT puts both back (and the line goes)", LabSwitches.ChangedLine);

                // ---- persistence: saved, read at the next boot ----------------------
                LabSwitches.Boot(false);   // as the owner's game: saved choices read and written
                LabSwitches.Set(LabSwitches.Find("night3"), false, true);
                Check(PlayerPrefs.GetInt(Scratch + "night3", -1) == 0, "a flip in his game is saved (psx.lab.night3 = 0)");
                StreetLights.Night3 = true;   // a new launch: the static from its default
                LabSwitches.Boot(false);
                Check(!StreetLights.Night3 && LabSwitches.Find("night3").Changed,
                      "the next launch reads it back: NIGHT 3 old before the first scene");
                LabSwitches.Set(LabSwitches.Find("night3"), true, true);
                Check(!PlayerPrefs.HasKey(Scratch + "night3") && StreetLights.Night3,
                      "choosing the default again forgets the key (a later default change reaches him)");
                // a NEXT LOAD row's saved choice is in place before the first load
                PlayerPrefs.SetInt(Scratch + "spray2", 0);
                TireSpray.Spray2 = true;
                LabSwitches.Boot(false);
                Check(!TireSpray.Spray2, "a saved NEXT LOAD choice is written at boot, before the first level builds anything");
                PlayerPrefs.DeleteKey(Scratch + "spray2");
                TireSpray.Spray2 = true;

                // ---- a set variable wins ------------------------------------------
                PlayerPrefs.SetInt(Scratch + "night3", 0);
                envWas["PSX_NIGHT3"] = System.Environment.GetEnvironmentVariable("PSX_NIGHT3");
                System.Environment.SetEnvironmentVariable("PSX_NIGHT3", "1");
                StreetLights.Night3 = true;
                LabSwitches.Boot(false);
                var held = LabSwitches.Find("night3");
                Check(held.EnvHeld && StreetLights.Night3 && !LabSwitches.Set(held, false, true) && StreetLights.Night3,
                      "PSX_NIGHT3 set: the saved OLD is not applied and the row will not flip (the tools' variable wins)");
                Check(LabCatalog.Value(held).Contains("LOCKED"), "and the page says LOCKED", LabCatalog.Value(held));
                System.Environment.SetEnvironmentVariable("PSX_NIGHT3", envWas["PSX_NIGHT3"]);

                // ---- a test run ignores the saved choices and never writes them ------
                StreetLights.Night3 = true;
                LabSwitches.Boot(true);
                Check(StreetLights.Night3 && !LabSwitches.Find("night3").Changed,
                      "a tour / batch run / the editor: the saved OLD is ignored (NIGHT 3 stays NEW)");
                PlayerPrefs.DeleteKey(Scratch + "night3");
                LabSwitches.Flip(LabSwitches.Find("night3"));
                Check(!PlayerPrefs.HasKey(Scratch + "night3") && !StreetLights.Night3,
                      "and a flip in it changes the game but writes nothing");
                LabSwitches.Flip(LabSwitches.Find("night3"));
                Check(LabSwitches.TestRun, "the editor counts as a test run unless PSX_LAB_PREFS=1");

                // ---- the page -----------------------------------------------------
                var front = SettingsCatalog.Items(null, SettingsTab.System);
                var drive = SettingsCatalog.Items(new SettingsHost { debugInfo = () => false, toggleDebugInfo = () => { }, openBench = () => { } },
                                                  SettingsTab.System, () => { });
                Check(front.Any(i => i.name == "LAB") && drive.Any(i => i.name == "LAB"),
                      "SETTINGS has a LAB row in the front end's OPTIONS and in the pause menu");
                for (int t = 0; t < LabCatalog.TabNames.Length; t++)
                {
                    var items = LabCatalog.Items(t);
                    int rowsHere = LabSwitches.Rows((LabGroup)t).Count;
                    Check(items.Count >= rowsHere + 1 && items.Last().name == "RESET ALL TO DEFAULT" &&
                          items.All(i => i.value != null && !string.IsNullOrEmpty(i.blurb)),
                          "LAB > " + LabCatalog.TabNames[t] + ": " + rowsHere + " rows and RESET ALL, each with a value and a help line",
                          string.Join(", ", items.Select(i => i.name + "=" + i.value())));
                }
            }
            finally
            {
                foreach (var kv in envWas) System.Environment.SetEnvironmentVariable(kv.Key, kv.Value);
                foreach (var r in LabSwitches.Rows())
                {
                    PlayerPrefs.DeleteKey(Scratch + r.id);
                    if (snap.TryGetValue(r.id, out var v)) LabSwitches.RestoreMembers(r, v);
                }
                LabSwitches.KeyPrefix = prefixWas;
                LabSwitches.Boot(true);
                Rain2.PushGlass2();
                TireSpray.PushCarDrops2();
            }
        }
    }
}
