using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// SAVE SAFETY (R0a, Fault_System_Design_2026-11-11 §2 "Save safety" and
    /// "Versions"). Every PC build shares one registry key and several builds
    /// are on disk, so before any round changes the save:
    ///   * a newer build's career loads READ-ONLY and is never written back;
    ///   * the raw save is backed up before the first migration step;
    ///   * every Save() stamps writerVersion, and a rollover's save writes
    ///     &lt;key&gt;.last, so a v25+ save an older exe wrote back is caught
    ///     and this build's own copy is offered;
    ///   * a load that throws never starts a new game on top of the career.
    /// TestSaveVersionLadder pins the other half of §2: one migration step per
    /// version, numbered in order. Everything here runs on a SCRATCH key
    /// (UseScratchSave), never the career's.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        const string GuardScratch = "selftest.saveguard";

        /// <summary>Only the save-safety pins: seconds, no scene build.
        /// Writes PSXRacing_selftest_savesafety.txt.</summary>
        public static void RunSaveSafety()
        {
            log = new StringBuilder();
            failures = 0;
            Guard(nameof(TestSaveDowngrade), TestSaveDowngrade);
            Guard(nameof(TestSaveVersionLadder), TestSaveVersionLadder);
            Line(failures == 0 ? "SAVE SAFETY SELF-TEST OK" : "SAVE SAFETY SELF-TEST FAILED (" + failures + ")");
            Debug.Log(log.ToString());
            File.WriteAllText(Path.Combine(Application.dataPath, "../PSXRacing_selftest_savesafety.txt"), log.ToString());
        }

        static string GuardKey => LifeSimManager.SaveKey;
        static string Stored => PlayerPrefs.GetString(GuardKey, "<none>");

        /// <summary>
        /// THE SAVE GUARD (design gate TestSaveDowngrade): a save stamped
        /// above the code loads read-only and the stored string is
        /// byte-identical after a day's play; a v25 save with its v25 fields
        /// and writerVersion stripped (what a pre-R0a exe writes back) is
        /// detected and the .last copy is offered; a step that throws leaves
        /// the main key byte-identical and starts no career; the .bak copy
        /// exists before the first step runs. Plus: nothing but LifeSimManager
        /// writes the career key, PSX_SAVE_GUARD=0 is the old behaviour, and
        /// what the guard costs a save.
        /// </summary>
        static void TestSaveDowngrade()
        {
            Line("save safety (R0a: backup, read-only newer saves, writer stamp, no new game over a career):");
            var editionWas = Edition.Simulated;
            Edition.Simulate(EditionKind.All);
            bool guardWas = LifeSimManager.Guard;
            LifeSimManager.Guard = true;
            try
            {
                SaveGuardWriters();
                SaveGuardBackup();
                SaveGuardNewer();
                SaveGuardFailed();
                SaveGuardRewritten();
                SaveGuardOff();
                SaveGuardCost();
            }
            finally
            {
                LifeSimManager.BeforeMigrateHook = null;
                LifeSimManager.SimulateCodeVersion(0);
                LifeSimManager.Guard = guardWas;
                // Empty the scratch career and every copy it made, then back
                // onto the career key (nothing is read or written there).
                LifeSimManager.UseScratchSave(GuardScratch);
                LifeSimManager.UseCareerSave();
                Edition.Simulate(editionWas);
            }
        }

        /// <summary>A named career on the scratch key, written by Save() as
        /// a build would. Returns the stored string.</summary>
        static string GuardCareer(string name, int day, int money)
        {
            LifeSimManager.UseScratchSave(GuardScratch);
            LifeSimManager.StartNewGame(name, 30, LifeRules.DefaultJobIndex);
            var st = LifeSimManager.State;
            st.day = day;
            st.money = money;
            LifeSimManager.Save();
            return Stored;
        }

        /// <summary>The same career as an older build wrote it: version
        /// <paramref name="version"/>, no writer stamp.</summary>
        static string AsWrittenAt(string json, int version, int money = -1)
        {
            var s = JsonUtility.FromJson<LifeState>(json);
            s.saveVersion = version;
            s.writerVersion = 0;
            if (money >= 0) s.money = money;
            return JsonUtility.ToJson(s);
        }

        // ------------------------------------------------------------------
        /// <summary>EVERY WRITER GOES THROUGH THE GUARD: no file but
        /// LifeSimManager writes or deletes the career key, and nothing calls
        /// PlayerPrefs.DeleteAll. (This file writes the SCRATCH key on purpose,
        /// to play the older exe, and is left out.)</summary>
        static void SaveGuardWriters()
        {
            var writeRx = new Regex(@"PlayerPrefs\.(SetString|SetInt|SetFloat|DeleteKey|DeleteAll)\s*\(([^;]*)");
            var offenders = new List<string>();
            int saveCalls = 0, saveFiles = 0, scanned = 0;
            foreach (var f in Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(f);
                if (name == "LifeSimManager.cs" || name == "LifeSimSelfTest.SaveSafety.cs") continue;
                string text;
                try { text = File.ReadAllText(f); } catch { continue; }
                scanned++;
                int calls = Regex.Matches(text, @"LifeSimManager\.Save\(\)").Count;
                if (calls > 0) { saveCalls += calls; saveFiles++; }
                if (!text.Contains("PlayerPrefs.")) continue;
                foreach (Match m in writeRx.Matches(text))
                {
                    string args = m.Groups[2].Value;
                    if (m.Groups[1].Value == "DeleteAll" || args.Contains("psxRacingLifeSave") ||
                        args.Contains("SaveKey") || args.Contains("saveKey") || args.Contains("CareerKey"))
                    {
                        int line = 1 + text.Substring(0, m.Index).Split('\n').Length - 1;
                        offenders.Add(name + ":" + line);
                    }
                }
            }
            Check(offenders.Count == 0,
                  "nothing but LifeSimManager writes or deletes the career key, and nothing calls PlayerPrefs.DeleteAll (" +
                  scanned + " files)", offenders.Count == 0 ? null : string.Join(", ", offenders));
            Line("    " + saveCalls + " calls to LifeSimManager.Save() in " + saveFiles +
                 " files: every one goes through the guard inside Save()");
        }

        // ------------------------------------------------------------------
        /// <summary>BACKUP BEFORE MIGRATE: the raw string, byte for byte,
        /// under &lt;key&gt;.bak.v&lt;old&gt;.&lt;date&gt;, written before the
        /// first step runs; once per distinct string; the last three kept.</summary>
        static void SaveGuardBackup()
        {
            int cur = LifeSimManager.CodeVersion;
            string raw = GuardCareer("BAK", 30, 1111);
            Check(JsonUtility.FromJson<LifeState>(raw).writerVersion == cur,
                  "every Save() stamps writerVersion with this build's version", cur);
            string old = AsWrittenAt(raw, cur - 1);
            PlayerPrefs.SetString(GuardKey, old);

            string seenKey = null;
            bool seenFirst = false;
            LifeSimManager.BeforeMigrateHook = s =>
            {
                var keys = LifeSimManager.BackupKeys();
                seenKey = keys.Count > 0 ? keys[keys.Count - 1] : null;
                seenFirst = seenKey != null && PlayerPrefs.GetString(seenKey) == old && s.saveVersion == cur - 1;
            };
            var st = LifeSimManager.Reload();
            LifeSimManager.BeforeMigrateHook = null;
            Check(seenFirst, "the .bak copy exists, byte for byte, before the first step runs", seenKey);
            Check(seenKey != null && seenKey.StartsWith(GuardKey + ".bak.v" + (cur - 1) + "."),
                  "named <key>.bak.v<old>.<date>", seenKey);
            Check(st.playerName == "BAK" && st.saveVersion == cur && LifeSimManager.Trouble == LifeSimManager.SaveTrouble.None,
                  "and the career loads, migrated to v" + cur, st.playerName + " v" + st.saveVersion + " " + LifeSimManager.Trouble);
            Check(Stored == old, "the load itself writes nothing to the main key");
            LifeSimManager.Save();
            var back = JsonUtility.FromJson<LifeState>(Stored);
            Check(back.saveVersion == cur && back.writerVersion == cur,
                  "the next save is v" + cur + ", stamped by this build", back.saveVersion + "/" + back.writerVersion);

            // A v24 save from a build before the guard (no stamp at all) is
            // an ordinary save: every such build knew v24.
            PlayerPrefs.SetString(GuardKey, AsWrittenAt(raw, LifeSimManager.LastUnstampedVersion));
            st = LifeSimManager.Reload();
            Check(LifeSimManager.Trouble == LifeSimManager.SaveTrouble.None && st.playerName == "BAK",
                  "a v" + LifeSimManager.LastUnstampedVersion + " save with no writer stamp (any build before the guard) loads as it always did",
                  LifeSimManager.Trouble);

            // Loading the same old save again (quit without saving) adds no copy.
            PlayerPrefs.SetString(GuardKey, old);
            int before = LifeSimManager.BackupKeys().Count;
            LifeSimManager.Reload();
            LifeSimManager.Reload();
            Check(LifeSimManager.BackupKeys().Count == before,
                  "booting the same old save again adds no second copy", LifeSimManager.BackupKeys().Count);

            // The ring keeps three.
            string first = LifeSimManager.BackupKeys()[0];
            for (int i = 0; i < 4; i++)
            {
                PlayerPrefs.SetString(GuardKey, AsWrittenAt(raw, cur - 1, 2000 + i));
                LifeSimManager.Reload();
            }
            var ring = LifeSimManager.BackupKeys();
            bool allThere = ring.TrueForAll(k => PlayerPrefs.HasKey(k));
            var newest = ring.Count > 0 ? JsonUtility.FromJson<LifeState>(PlayerPrefs.GetString(ring[ring.Count - 1])) : null;
            Check(ring.Count == LifeSimManager.BackupKeep && allThere && !PlayerPrefs.HasKey(first) &&
                  newest != null && newest.money == 2003,
                  "the last " + LifeSimManager.BackupKeep + " copies are kept and the oldest is deleted",
                  ring.Count + " kept, oldest gone " + !PlayerPrefs.HasKey(first) + ", newest $" + (newest != null ? newest.money : -1));
        }

        // ------------------------------------------------------------------
        /// <summary>A NEWER SAVE IS READ-ONLY: never migrated, never written
        /// back, not even by a rollover's .last; an explicit NEW GAME over it
        /// keeps a copy first.</summary>
        static void SaveGuardNewer()
        {
            int cur = LifeSimManager.CodeVersion;
            string raw = GuardCareer("NEWER", 50, 5555);
            var s = JsonUtility.FromJson<LifeState>(raw);
            s.saveVersion = cur + 2;
            s.writerVersion = cur + 2;
            // ...carrying a field this build has never heard of.
            string newer = JsonUtility.ToJson(s).Insert(1, "\"futureField\":7,");
            PlayerPrefs.SetString(GuardKey, newer);

            var st = LifeSimManager.Reload();
            Check(LifeSimManager.Trouble == LifeSimManager.SaveTrouble.Newer && LifeSimManager.ReadOnly &&
                  LifeSimManager.TroubleVersion == cur + 2,
                  "a save stamped above this build (v" + (cur + 2) + ") loads READ-ONLY",
                  LifeSimManager.Trouble + " v" + LifeSimManager.TroubleVersion);
            Check(st.playerName == "NEWER" && st.day == 50 && st.saveVersion == cur + 2,
                  "the career is shown as it is, not migrated", st.playerName + " day " + st.day + " v" + st.saveVersion);
            string title = LifeSimManager.TroubleTitle() ?? "";
            Check(title.Contains("NEWER BUILD") && title.Contains("NOT SAVE"),
                  "the title says it was saved by a newer build and will not be saved", title);
            Check(LifeSimManager.TakeEditionNote() == LifeSimManager.ReadOnlyNote && LifeSimManager.TakeEditionNote() == null &&
                  LifeSimManager.ReadOnlyNote.Length <= LifeSimManager.NoteMax,
                  "the home screen toasts READ-ONLY once (a toast-length line)", LifeSimManager.ReadOnlyNote);

            // A day of play on it: money, a night's sleep (the rollover that
            // would write .last), and the saves every door makes.
            st.money += 100;
            LifeRules.SleepUntilMorning(st);
            LifeSimManager.Save();
            LifeSimManager.Save();
            Check(Stored == newer, "after a day's play and its saves the stored string is byte-identical");
            Check(!PlayerPrefs.HasKey(GuardKey + ".last"), "and no .last copy was written from it");

            // NEW GAME over it (title / START OVER) keeps a copy first.
            LifeSimManager.DeleteSave();
            var bak = LifeSimManager.BackupKeys();
            Check(!PlayerPrefs.HasKey(GuardKey) && bak.Count == 1 && PlayerPrefs.GetString(bak[0]) == newer &&
                  !LifeSimManager.ReadOnly,
                  "an explicit NEW GAME over it keeps the newer career in the backups first", bak.Count);
            // The wizard's commit straight over it does too (one copy: same string).
            PlayerPrefs.SetString(GuardKey, newer);
            LifeSimManager.Reload();
            LifeSimManager.StartNewGame("FRESH", 22, LifeRules.DefaultJobIndex);
            bak = LifeSimManager.BackupKeys();
            var fresh = JsonUtility.FromJson<LifeState>(Stored);
            Check(fresh.playerName == "FRESH" && fresh.saveVersion == cur && bak.Count == 1 &&
                  PlayerPrefs.GetString(bak[bak.Count - 1]) == newer,
                  "and a new career committed over it writes the key only after that copy", fresh.playerName + ", " + bak.Count + " copy");
        }

        // ------------------------------------------------------------------
        /// <summary>NO NEW GAME OVER A CAREER: a save that will not parse, and
        /// an upgrade step that throws, both leave the main key byte-identical,
        /// keep the raw string and start nothing - until an explicit NEW GAME.</summary>
        static void SaveGuardFailed()
        {
            int cur = LifeSimManager.CodeVersion;
            GuardCareer("X", 1, 1);
            string junk = "{\"saveVersion\":" + cur + ",\"money\":12,\"cars\":[{\"id\":";   // cut off mid-write
            PlayerPrefs.SetString(GuardKey, junk);
            var st = LifeSimManager.Reload();
            Check(LifeSimManager.Trouble == LifeSimManager.SaveTrouble.Failed && string.IsNullOrEmpty(st.playerName),
                  "a save that will not parse is a FAILED load: a blank career in memory, not a new game on disk",
                  LifeSimManager.Trouble);
            st.playerName = "OOPS";
            st.money = 1;
            LifeSimManager.Save();
            LifeSimManager.Save();
            Check(Stored == junk, "Save() writes nothing over it (byte-identical)");
            var failed = LifeSimManager.FailedKeys();
            Check(failed.Count == 1 && PlayerPrefs.GetString(failed[0]) == junk &&
                  failed[0].StartsWith(GuardKey + ".failed.v" + cur + ".") && LifeSimManager.FailedCopyKey == failed[0],
                  "the raw string is kept as <key>.failed.v<old>.<date>", failed.Count > 0 ? failed[0] : "none");
            string title = LifeSimManager.TroubleTitle() ?? "";
            Check(title.Contains("COULD NOT LOAD") && title.Contains("KEPT"), "the title says so", title);
            LifeSimManager.Reload();
            Check(LifeSimManager.FailedKeys().Count == 1, "booting it again keeps no second copy", LifeSimManager.FailedKeys().Count);

            // A STEP THAT THROWS: an older career whose upgrade fails.
            string raw = GuardCareer("STEP", 12, 3456);
            string old = AsWrittenAt(raw, cur - 1);
            PlayerPrefs.SetString(GuardKey, old);
            bool bakFirst = false;
            LifeSimManager.BeforeMigrateHook = s =>
            {
                var keys = LifeSimManager.BackupKeys();
                bakFirst = keys.Count == 1 && PlayerPrefs.GetString(keys[0]) == old;
                throw new System.InvalidOperationException("a broken step (simulated)");
            };
            st = LifeSimManager.Reload();
            LifeSimManager.BeforeMigrateHook = null;
            Check(bakFirst, "a step that throws finds the .bak copy already written");
            Check(LifeSimManager.Trouble == LifeSimManager.SaveTrouble.Failed && string.IsNullOrEmpty(st.playerName) &&
                  LifeSimManager.TroubleVersion == cur - 1,
                  "and starts no career (FAILED, blank state)", LifeSimManager.Trouble + " v" + LifeSimManager.TroubleVersion);
            st.playerName = "OOPS";
            LifeSimManager.Save();
            Check(Stored == old, "the main key is byte-identical after the throw and a save");

            // Only an explicit NEW GAME writes the key again; the copy stays.
            string kept = LifeSimManager.FailedCopyKey;
            LifeSimManager.StartNewGame("AGAIN", 25, LifeRules.DefaultJobIndex);
            var now = JsonUtility.FromJson<LifeState>(Stored);
            Check(now.playerName == "AGAIN" && kept != null && PlayerPrefs.GetString(kept) == old &&
                  LifeSimManager.Trouble == LifeSimManager.SaveTrouble.None,
                  "an explicit NEW GAME writes the main key, and the failed career's copy stays", now.playerName);
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// THE WRITER STAMP, played as the v25 build the next round ships
        /// (SimulateCodeVersion): a rollover's save writes .last; an exe from
        /// before the guard loads the v25 career, drops its v25 fields and the
        /// stamp it does not know, plays on and writes it back still marked
        /// v25. The next guarded load catches it, writes nothing, backs it up,
        /// runs the v25+ steps again and offers this build's .last copy -
        /// and either answer sticks.
        /// </summary>
        static void SaveGuardRewritten()
        {
            int v25 = LifeSimManager.LastUnstampedVersion + 1;
            LifeSimManager.SimulateCodeVersion(v25);
            try
            {
                LifeSimManager.UseScratchSave(GuardScratch);
                LifeSimManager.StartNewGame("STAMP", 30, LifeRules.DefaultJobIndex);
                var st = LifeSimManager.State;
                st.saveVersion = v25;   // a v25 build's career
                st.money = 4000;
                LifeRules.SleepUntilMorning(st);   // the rollover marks .last
                LifeSimManager.Save();
                string mine = Stored;
                var mineS = JsonUtility.FromJson<LifeState>(mine);
                string when = PlayerPrefs.GetString(GuardKey + ".last.when", "");
                Check(PlayerPrefs.GetString(GuardKey + ".last", "") == mine && mineS.writerVersion == v25,
                      "the first save after a daily rollover also writes <key>.last, stamped v" + v25);
                Check(Regex.IsMatch(when, @"^\d{4}-\d\d-\d\d \d\d:\d\d$"), "with the date it was written", when);
                LifeSimManager.Save();
                Check(PlayerPrefs.GetString(GuardKey + ".last", "") == mine, "(a save with no rollover leaves .last alone)");

                // THE OLD EXE. The v25 career on disk carries v25 fields (this
                // stands for them); the old exe drops those and writerVersion,
                // plays two more days and writes it back still marked v25.
                string onDisk = mine.Insert(1, "\"v25Field\":7,");
                PlayerPrefs.SetString(GuardKey, onDisk);
                var oldExe = JsonUtility.FromJson<LifeState>(onDisk);
                oldExe.day += 2;
                oldExe.money = 9000;
                string stripped = Regex.Replace(JsonUtility.ToJson(oldExe), "\"writerVersion\":-?\\d+,?", "");
                PlayerPrefs.SetString(GuardKey, stripped);
                Check(!stripped.Contains("writerVersion") && !stripped.Contains("v25Field") &&
                      LifeSimManager.PeekVersion(stripped) == v25,
                      "(the old exe's string: still v" + v25 + ", no writer stamp, no v25 fields)");

                st = LifeSimManager.Reload();
                Check(LifeSimManager.Trouble == LifeSimManager.SaveTrouble.Rewritten,
                      "a v25 save with its v25 fields and writerVersion stripped is detected", LifeSimManager.Trouble);
                Check(Stored == stripped, "the load writes nothing back");
                var bak = LifeSimManager.BackupKeys();
                Check(bak.Count > 0 && PlayerPrefs.GetString(bak[bak.Count - 1]) == stripped,
                      "the older build's string is backed up before its v25+ steps run again");
                Check(st.day == oldExe.day && st.money == 9000, "until a choice is made the career is the older build's",
                      "day " + st.day + " $" + st.money);
                bool offered = LifeSimManager.LastCopy(out int lastDay, out string lastWhen);
                Check(offered && lastDay == mineS.day && lastWhen == when && LifeSimManager.HasLastCopy,
                      "and the .last copy is offered, with its day and date", lastDay + " / " + lastWhen);
                string title = LifeSimManager.TroubleTitle() ?? "";
                Check(title.Contains(LifeRules.DateLabel(mineS.day)) && title.Contains(when) &&
                      title.Contains(LifeRules.DateLabel(oldExe.day)),
                      "the title names this build's copy (game date + when saved) and the older build's", title);

                // Answer 1: this build's own copy.
                Check(LifeSimManager.UseLastCopy(), "choosing this build's copy...");
                var now = LifeSimManager.State;
                Check(now.day == mineS.day && now.money == mineS.money && Stored == mine &&
                      LifeSimManager.Trouble == LifeSimManager.SaveTrouble.None,
                      "...makes it the career, byte for byte, and the question is gone", "day " + now.day + " $" + now.money);
                bool strippedKept = LifeSimManager.BackupKeys().Exists(k => PlayerPrefs.GetString(k) == stripped);
                Check(strippedKept, "the older build's copy stays in the backups");

                // Answer 2: the older build's.
                PlayerPrefs.SetString(GuardKey, stripped);
                LifeSimManager.Reload();
                LifeSimManager.KeepStoredCopy();
                var keptS = JsonUtility.FromJson<LifeState>(Stored);
                Check(keptS.day == oldExe.day && keptS.money == 9000 && keptS.writerVersion == v25,
                      "keeping the older build's copy saves it at once, stamped by this build",
                      "day " + keptS.day + " writer v" + keptS.writerVersion);
                LifeSimManager.Reload();
                Check(LifeSimManager.Trouble == LifeSimManager.SaveTrouble.None, "and the next boot does not ask again",
                      LifeSimManager.Trouble);

                // With no usable .last there is nothing to choose: the career
                // carries on as the older build's, its dropped fields seeded.
                PlayerPrefs.DeleteKey(GuardKey + ".last");
                PlayerPrefs.SetString(GuardKey, stripped);
                st = LifeSimManager.Reload();
                Check(LifeSimManager.Trouble == LifeSimManager.SaveTrouble.Rewritten && !LifeSimManager.HasLastCopy &&
                      !LifeSimManager.UseLastCopy() && st.day == oldExe.day,
                      "with no .last copy it is still caught, and nothing is offered", LifeSimManager.TroubleTitle());
            }
            finally
            {
                LifeSimManager.SimulateCodeVersion(0);
            }
        }

        // ------------------------------------------------------------------
        /// <summary>PSX_SAVE_GUARD=0 IS THE OLD SAVE: a newer career loads and
        /// is written back, a broken one falls through to a new game, no
        /// copies. The writer stamp stays, which is what lets the next guarded
        /// build catch a write like that one.</summary>
        static void SaveGuardOff()
        {
            int cur = LifeSimManager.CodeVersion;
            LifeSimManager.Guard = false;
            try
            {
                string raw = GuardCareer("OFF", 9, 99);
                var s = JsonUtility.FromJson<LifeState>(raw);
                s.saveVersion = cur + 1;
                s.writerVersion = cur + 1;
                string newer = JsonUtility.ToJson(s);
                PlayerPrefs.SetString(GuardKey, newer);
                var st = LifeSimManager.Reload();
                Check(LifeSimManager.Trouble == LifeSimManager.SaveTrouble.None && !LifeSimManager.ReadOnly && st.playerName == "OFF",
                      "guard off: a newer save loads like any other", LifeSimManager.Trouble);
                LifeSimManager.Save();
                var back = JsonUtility.FromJson<LifeState>(Stored);
                Check(back.saveVersion == cur + 1 && back.writerVersion == cur,
                      "and is written back (the old behaviour), stamped by this older build so a guarded one can tell",
                      "v" + back.saveVersion + " writer v" + back.writerVersion);
                PlayerPrefs.SetString(GuardKey, "{\"saveVersion\":" + cur + ",\"money\":");
                st = LifeSimManager.Reload();
                Check(string.IsNullOrEmpty(st.playerName) && LifeSimManager.Trouble == LifeSimManager.SaveTrouble.None &&
                      LifeSimManager.BackupKeys().Count == 0 && LifeSimManager.FailedKeys().Count == 0,
                      "a broken one falls through to a new game and nothing is kept (the old behaviour)");
            }
            finally
            {
                LifeSimManager.Guard = true;
            }
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// WHAT THE GUARD COSTS A SAVE (design: "PerfTour reports save size and
        /// save time"; over 50 ms moves the career to a file). Printed, never
        /// failed: an editor's registry is not a phone. A worked career - ten
        /// cars and sixty days - saved with the guard off (the old Save) and
        /// on, the first save after a rollover (which also writes .last), and
        /// a load plain and with a migration (which also writes a backup).
        /// </summary>
        static void SaveGuardCost()
        {
            int cur = LifeSimManager.CodeVersion;
            LifeSimManager.UseScratchSave(GuardScratch);
            LifeSimManager.StartNewGame("COST", 30, LifeRules.DefaultJobIndex);
            int freshBytes = Encoding.UTF8.GetByteCount(Stored);
            var st = LifeSimManager.State;
            LifeRules.SeedFallbackCar(st);
            int added = 0;
            if (CarCatalog.Ready)
                foreach (var spec in CarCatalog.All)
                {
                    if (added >= 9) break;
                    if (spec == null || spec.IsRaceCar || spec.IsRallyCar) continue;
                    CarMarket.MakeOwnedCar(st, spec, 70, 60000f + added * 9000f, spec.price / 2);
                    added++;
                }
            for (int d = 0; d < 60; d++) LifeRules.SleepUntilMorning(st);
            LifeSimManager.Save();
            string json = Stored;
            int bytes = Encoding.UTF8.GetByteCount(json);

            var sw = new System.Diagnostics.Stopwatch();
            double Ms(int n, System.Action a)
            {
                a();   // warm
                sw.Restart();
                for (int i = 0; i < n; i++) a();
                sw.Stop();
                return sw.Elapsed.TotalMilliseconds / n;
            }
            LifeSimManager.Guard = false;
            double saveOff = Ms(20, LifeSimManager.Save);
            LifeSimManager.Guard = true;
            double saveOn = Ms(20, LifeSimManager.Save);
            double saveRoll = Ms(10, () => { LifeSimManager.NoteRollover(LifeSimManager.State); LifeSimManager.Save(); });
            int lastBytes = Encoding.UTF8.GetByteCount(PlayerPrefs.GetString(GuardKey + ".last", ""));
            double load = Ms(10, () => LifeSimManager.Reload());
            int k = 0;
            double loadMig = Ms(5, () =>
            {
                PlayerPrefs.SetString(GuardKey, AsWrittenAt(json, cur - 1, 100000 + k++));
                LifeSimManager.Reload();
            });
            int copies = 0;
            foreach (var key in LifeSimManager.BackupKeys()) copies += Encoding.UTF8.GetByteCount(PlayerPrefs.GetString(key, ""));
            // The steady state once copies exist: on the registry each value is
            // its own write, but a store rewritten whole on every save (Android's
            // XML) carries them all.
            double saveWithCopies = Ms(20, LifeSimManager.Save);

            Line("    SAVE SIZE: a new career " + freshBytes + " bytes; a worked career (" + LifeSimManager.State.cars.Count +
                 " cars, 60 days) " + bytes + " bytes (" + (bytes / 1024f).ToString("0.0") + " KB); its .last copy " + lastBytes +
                 " bytes; " + LifeSimManager.BackupKeys().Count + " backups " + (copies / 1024f).ToString("0.0") + " KB");
            Line("    SAVE TIME (editor, registry, mean): Save() guard off (the old Save) " + saveOff.ToString("0.00") +
                 " ms, guard on " + saveOn.ToString("0.00") + " ms, first save after a rollover (+ .last) " +
                 saveRoll.ToString("0.00") + " ms, with .last and " + LifeSimManager.BackupKeys().Count + " backups in the store " +
                 saveWithCopies.ToString("0.00") + " ms");
            Line("    LOAD TIME: " + load.ToString("0.00") + " ms; with a migration (+ backup copy) " + loadMig.ToString("0.00") + " ms");
            Check(bytes > freshBytes && saveOn > 0.0, "the save cost was measured", bytes + " bytes");
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// ONE STEP PER VERSION (design §2 "Versions"). If two separately
        /// shipped rounds share a number, the first stamps the career and the
        /// second's step never runs - its fields read back as 0 on every
        /// existing car. So: Migrate's guards are exactly 2, 3, ... current,
        /// once each and in order; each number is stamped exactly once (in
        /// Migrate or its MigrateTo* step); and a save at every version from 1
        /// up migrates to the current one, and migrating it again changes
        /// nothing.
        /// </summary>
        static void TestSaveVersionLadder()
        {
            Line("save version ladder (one migration step per version, numbered in order):");
            int cur = new LifeState().saveVersion;
            string path = Path.Combine(Application.dataPath, "PSXRacing/Scripts/LifeSim/LifeSimManager.cs");
            string src = File.Exists(path) ? File.ReadAllText(path) : null;
            Check(src != null, "LifeSimManager.cs is readable", path);
            if (src == null) return;
            string body = MethodBody(src, "public static void Migrate(LifeState s)");
            Check(body != null, "Migrate's body found");
            if (body == null) return;

            var guards = new List<int>();
            foreach (Match m in Regex.Matches(body, @"if\s*\(\s*s\.saveVersion\s*<\s*(\d+)\s*\)"))
                guards.Add(int.Parse(m.Groups[1].Value));
            bool inOrder = guards.Count == cur - 1;
            for (int i = 0; inOrder && i < guards.Count; i++) inOrder = guards[i] == i + 2;
            Check(inOrder, "Migrate's steps are exactly v2..v" + cur + ", one each, in order",
                  string.Join(",", guards));

            var stampBad = new List<string>();
            for (int n = 2; n <= cur; n++)
            {
                int c = Regex.Matches(src, @"\bs\.saveVersion\s*=\s*" + n + @"\s*;").Count;
                if (c != 1) stampBad.Add("v" + n + " x" + c);
            }
            Check(stampBad.Count == 0, "each step stamps its own number exactly once",
                  stampBad.Count == 0 ? null : string.Join(", ", stampBad));

            var wrong = new List<string>();
            var unstable = new List<int>();
            for (int v = 1; v < cur; v++)
            {
                var s = new LifeState { saveVersion = v, day = 40, money = 500 };
                s.cars.Add(new OwnedCar { id = "ladder#" + v, specId = "", displayName = "LADDER" });
                try
                {
                    LifeSimManager.Migrate(s);
                    if (s.saveVersion != cur) wrong.Add("v" + v + "->v" + s.saveVersion);
                    string once = JsonUtility.ToJson(s);
                    LifeSimManager.Migrate(s);
                    if (JsonUtility.ToJson(s) != once) unstable.Add(v);
                }
                catch (System.Exception e) { wrong.Add("v" + v + " threw " + e.GetType().Name); }
            }
            Check(wrong.Count == 0, "a save at every version 1..v" + (cur - 1) + " migrates to v" + cur,
                  wrong.Count == 0 ? null : string.Join(", ", wrong));
            Check(unstable.Count == 0, "and migrating it again changes nothing",
                  unstable.Count == 0 ? null : string.Join(",", unstable));
            var fresh = new LifeState();
            string freshJson = JsonUtility.ToJson(fresh);
            LifeSimManager.Migrate(fresh);
            Check(fresh.saveVersion == cur && JsonUtility.ToJson(fresh) == freshJson,
                  "a new career (born at v" + cur + ") runs no step");
            Check(LifeSimManager.LastUnstampedVersion <= cur,
                  "the guard's last unstamped version (v" + LifeSimManager.LastUnstampedVersion + ") is not above the code's");
        }

        /// <summary>The text between the braces of the member whose
        /// declaration is <paramref name="signature"/> (comments and strings
        /// skipped when counting braces), or null.</summary>
        static string MethodBody(string src, string signature)
        {
            int at = src.IndexOf(signature, System.StringComparison.Ordinal);
            if (at < 0) return null;
            int open = src.IndexOf('{', at);
            if (open < 0) return null;
            int depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                char c = src[i];
                if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
                {
                    int nl = src.IndexOf('\n', i);
                    if (nl < 0) return null;
                    i = nl;
                    continue;
                }
                if (c == '"')
                {
                    for (i++; i < src.Length && src[i] != '"'; i++) if (src[i] == '\\') i++;
                    continue;
                }
                if (c == '\'')
                {
                    for (i++; i < src.Length && src[i] != '\''; i++) if (src[i] == '\\') i++;
                    continue;
                }
                if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return src.Substring(open + 1, i - open - 1);
            }
            return null;
        }
    }
}
