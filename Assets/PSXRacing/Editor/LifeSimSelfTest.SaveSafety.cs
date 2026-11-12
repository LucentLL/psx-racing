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
    ///   * every Save() stamps writerVersion, and a new career's first save
    ///     and a rollover's save write &lt;key&gt;.last, so a v25+ save that a
    ///     build which did not know its version wrote back is caught and this
    ///     build's own copy is offered;
    ///   * a load that throws never starts a new game on top of the career;
    ///   * the copies are files, never PlayerPrefs.
    /// TestSaveVersionLadder pins the other half of §2: one migration step per
    /// version, numbered in order. Everything here runs on a SCRATCH key
    /// (UseScratchSave), never the career's; the copies of a scratch key are
    /// files in this project's Library and go with it.
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
        /// <summary>This build's .last copy as stored (null when there is none).</summary>
        static string LastText => LifeSimManager.ReadCopy(LifeSimManager.LastCopyName);
        static string Copy(string name) => LifeSimManager.ReadCopy(name);
        static string Unstamped(string json) => Regex.Replace(json, "\"writerVersion\":-?\\d+,?", "");

        /// <summary>
        /// THE SAVE GUARD (design gate TestSaveDowngrade): a save stamped
        /// above the code loads read-only and the stored string is
        /// byte-identical after a day's play; a v25 save with its v25 fields
        /// and writerVersion stripped (what a pre-R0a exe writes back) is
        /// detected and the .last copy is offered; a step that throws leaves
        /// the main key byte-identical and starts no career; the .bak copy
        /// exists before the first step runs. Plus the review's (11-12): the
        /// copies are files, a NEW GAME takes .last with the career it
        /// belongs to, the steps that run again start where the writer
        /// stopped, a .last copy that will not load is refused untouched, and
        /// the doors to NEW GAME are listed. And what the guard costs a save.
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
                SaveGuardStore();
                SaveGuardBackup();
                SaveGuardNewer();
                SaveGuardFailed();
                SaveGuardRewritten();
                SaveGuardNewGameLast();
                SaveGuardWriterFrom();
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

        /// <summary>A WORKED CAREER, in place: <paramref name="cars"/> cars
        /// with real catalog specs, <paramref name="days"/> nights slept, a
        /// fault on each of the first two cars, a part on the shelf and a race
        /// booked - what a version step has to carry across (the re-run check
        /// in TestSaveVersionLadder) and what a save weighs (SaveGuardCost).</summary>
        static void WorkCareer(LifeState st, int cars, int days)
        {
            if (st.cars.Count == 0) LifeRules.SeedFallbackCar(st);
            if (CarCatalog.Ready)
                foreach (var spec in CarCatalog.All)
                {
                    if (st.cars.Count >= cars) break;
                    if (spec == null || spec.IsRaceCar || spec.IsRallyCar) continue;
                    int added = st.cars.Count;
                    CarMarket.MakeOwnedCar(st, spec, 70, 60000f + added * 9000f, spec.price / 2);
                }
            for (int d = 0; d < days; d++) LifeRules.SleepUntilMorning(st);
            string[] faultIds = { "bumper_crack", "alignment" };
            for (int i = 0; i < faultIds.Length && i < st.cars.Count; i++)
            {
                var f = FaultCatalog.MakeById(faultIds[i], "jpn");
                if (f != null) st.cars[i].faults.Add(f);
            }
            st.partsShelf.Add(new SparePart
            {
                id = "sp" + (++st.spareSeq), label = "ALTERNATOR, USED", stat = "engine", add = 10,
                basePrice = 80, paid = 40, dayPulled = st.day,
            });
            st.bookings.Add(new RaceBooking { day = st.day + 2, trackIndex = TrackCatalog.FirstOffered() });
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// EVERY WRITER GOES THROUGH THE GUARD. The key's name is a private
        /// const of LifeSimManager, so outside it the career can be reached
        /// only (a) by spelling the name out, (b) through LifeSimManager.SaveKey
        /// (or its copies through CopyDir), (c) by wiping every pref at once,
        /// or (d) through the two public doors to an explicit NEW GAME,
        /// DeleteSave and StartNewGame, which release a career the guard is
        /// holding. A tripwire on all four:
        ///   * nothing wipes every pref;
        ///   * the name is spelled out only by LifeSimSelfTest.cs (TestEditions
        ///     READS it to prove the CITY throwaway never wrote it);
        ///   * SaveKey and CopyDir are used only by tools that first move onto
        ///     a scratch key (UseScratchSave);
        ///   * the runtime doors to NEW GAME are exactly the title, the home
        ///     screen (wizard, START OVER) and the two tours on their scratch
        ///     keys. A new one - a debug RESET CAREER, say - would wipe a
        ///     career held after a failed load, so it fails here until it has
        ///     been looked at and added. Editor tools run on the editor's own
        ///     prefs and are only counted.
        /// A new file on any of these lists fails here and has to be looked at.
        /// The Save() count is a count, not a check: the guard is inside Save().
        /// (The tokens are assembled below so this file does not trip itself.)
        /// </summary>
        static void SaveGuardWriters()
        {
            string wipeAll = "PlayerPrefs." + "DeleteAll";
            string keyName = "psxRacing" + "LifeSave";
            string[] byHand = { "LifeSimManager." + "SaveKey", "LifeSimManager." + "CopyDir" };
            string[] doorsCalls = { "LifeSimManager." + "DeleteSave(", "LifeSimManager." + "StartNewGame(" };
            var doorsAllowed = new HashSet<string> { "TitleScreen.cs", "LifeHomeScreen.cs", "DoorTour.cs", "PerfTour.cs" };
            var offenders = new List<string>();
            var excused = new List<string>();
            var doors = new List<string>();
            int toolDoors = 0, saveCalls = 0, saveFiles = 0, scanned = 0;
            foreach (var f in Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(f);
                if (name == "LifeSimManager.cs") continue;   // the guard itself
                string text;
                try { text = File.ReadAllText(f); } catch { continue; }
                scanned++;
                bool editorOnly = f.Replace('\\', '/').Contains("/Editor/");
                int calls = Regex.Matches(text, @"LifeSimManager\.Save\(\)").Count;
                if (calls > 0) { saveCalls += calls; saveFiles++; }
                if (text.Contains(wipeAll)) offenders.Add(name + " (" + wipeAll + ")");
                if (text.Contains(keyName) && name != "LifeSimSelfTest.cs")
                    offenders.Add(name + " (spells out the career key)");
                foreach (var hand in byHand)
                {
                    if (!text.Contains(hand)) continue;
                    if (text.Contains("LifeSimManager.UseScratchSave(")) { if (!excused.Contains(name)) excused.Add(name); }
                    else offenders.Add(name + " (" + hand + " without a scratch key)");
                }
                if (text.Contains(doorsCalls[0]) || text.Contains(doorsCalls[1]))
                {
                    if (editorOnly) toolDoors++;
                    else if (doorsAllowed.Contains(name)) doors.Add(name);
                    else offenders.Add(name + " (calls DeleteSave/StartNewGame: a new runtime door to NEW GAME)");
                }
            }
            doors.Sort();
            Check(offenders.Count == 0,
                  "outside LifeSimManager nothing spells out the career key, reaches it or its copies without a scratch key, " +
                  "wipes every pref, or opens a new runtime door to NEW GAME (" + scanned + " files)",
                  offenders.Count == 0 ? null : string.Join(", ", offenders));
            Line("    runtime doors to an explicit NEW GAME (DeleteSave / StartNewGame), each looked at: " +
                 string.Join(", ", doors) + "; editor tools calling them on the editor's own prefs: " + toolDoors + " files");
            Line("    scratch-key tools that write their own key by hand: " +
                 (excused.Count == 0 ? "none" : string.Join(", ", excused)));
            Line("    " + saveCalls + " calls to LifeSimManager.Save() in " + saveFiles +
                 " files (a count, not a check: the guard sits inside Save() itself)");
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// THE COPIES ARE FILES, NEVER PLAYERPREFS (review 11-12): on Android
        /// every PlayerPrefs.Save() rewrites the whole store, so a copy kept
        /// there would ride along with every save the career makes. In the
        /// editor they live in this project's Library - never the player's own
        /// folder, which an editor's persistentDataPath is.
        /// </summary>
        static void SaveGuardStore()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string dir = Path.GetFullPath(LifeSimManager.CopyDir);
            string player = Path.GetFullPath(Application.persistentDataPath);
            Check(dir.StartsWith(Path.Combine(project, "Library"), System.StringComparison.OrdinalIgnoreCase) &&
                  !dir.StartsWith(player, System.StringComparison.OrdinalIgnoreCase),
                  "the editor keeps its copies in its own project's Library, never the player's folder", dir);
            int cur = LifeSimManager.CodeVersion;
            string raw = GuardCareer("STORE", 20, 2020);
            PlayerPrefs.SetString(GuardKey, AsWrittenAt(raw, cur - 1));
            LifeSimManager.Reload();
            var bak = LifeSimManager.BackupKeys();
            bool files = bak.Count == 1 && File.Exists(Path.Combine(dir, bak[0])) &&
                         File.Exists(Path.Combine(dir, LifeSimManager.LastCopyName));
            bool inPrefs = (bak.Count > 0 && PlayerPrefs.HasKey(bak[0])) || PlayerPrefs.HasKey(GuardKey + ".bak") ||
                           PlayerPrefs.HasKey(GuardKey + ".last") || PlayerPrefs.HasKey(LifeSimManager.LastCopyName);
            Check(files && !inPrefs,
                  "a backup and the .last copy are files beside the save; PlayerPrefs carries the career alone",
                  bak.Count + " backup(s), files " + files + ", in PlayerPrefs " + inPrefs);
        }

        // ------------------------------------------------------------------
        /// <summary>BACKUP BEFORE MIGRATE: the raw string, byte for byte,
        /// as &lt;key&gt;.bak.v&lt;old&gt;.&lt;date&gt;.&lt;n&gt;.json, written
        /// before the first step runs; once per distinct string; the last three
        /// kept.</summary>
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
                seenFirst = seenKey != null && Copy(seenKey) == old && s.saveVersion == cur - 1;
            };
            var st = LifeSimManager.Reload();
            LifeSimManager.BeforeMigrateHook = null;
            Check(seenFirst, "the .bak copy exists, byte for byte, before the first step runs", seenKey);
            Check(seenKey != null && seenKey.StartsWith(GuardKey + ".bak.v" + (cur - 1) + ".") && seenKey.EndsWith(".json"),
                  "named <key>.bak.v<old>.<date>.<n>.json", seenKey);
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
            bool allThere = ring.TrueForAll(k => LifeSimManager.CopyExists(k));
            var newest = ring.Count > 0 ? JsonUtility.FromJson<LifeState>(Copy(ring[ring.Count - 1])) : null;
            Check(ring.Count == LifeSimManager.BackupKeep && allThere && !LifeSimManager.CopyExists(first) &&
                  newest != null && newest.money == 2003,
                  "the last " + LifeSimManager.BackupKeep + " copies are kept and the oldest is deleted",
                  ring.Count + " kept, oldest gone " + !LifeSimManager.CopyExists(first) + ", newest $" + (newest != null ? newest.money : -1));
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
            string lastBefore = LastText;   // this build's own copy from the career's first save

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
            Check(LastText == lastBefore, "and the .last copy was not written from it");

            // NEW GAME over it (title / START OVER) keeps a copy first.
            LifeSimManager.DeleteSave();
            var bak = LifeSimManager.BackupKeys();
            Check(!PlayerPrefs.HasKey(GuardKey) && bak.Exists(k => Copy(k) == newer) && !LifeSimManager.ReadOnly,
                  "an explicit NEW GAME over it keeps the newer career in the backups first", bak.Count);
            // The wizard's commit straight over it does too.
            PlayerPrefs.SetString(GuardKey, newer);
            LifeSimManager.Reload();
            LifeSimManager.StartNewGame("FRESH", 22, LifeRules.DefaultJobIndex);
            bak = LifeSimManager.BackupKeys();
            var fresh = JsonUtility.FromJson<LifeState>(Stored);
            Check(fresh.playerName == "FRESH" && fresh.saveVersion == cur && bak.Exists(k => Copy(k) == newer),
                  "and a new career committed over it writes the key only after that copy", fresh.playerName + ", " + bak.Count + " copies");
        }

        // ------------------------------------------------------------------
        /// <summary>NO NEW GAME OVER A CAREER: a save that will not parse, and
        /// an upgrade step that throws, both leave the main key byte-identical,
        /// keep the raw string and start nothing - until an explicit NEW GAME,
        /// which the title now confirms first (TitleScreen.Confirm, pictured
        /// by SaveGuardPreview).</summary>
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
            Check(failed.Count == 1 && Copy(failed[0]) == junk &&
                  failed[0].StartsWith(GuardKey + ".failed.v" + cur + ".") && LifeSimManager.FailedCopyKey == failed[0],
                  "the raw string is kept as <key>.failed.v<old>.<date>.<n>.json", failed.Count > 0 ? failed[0] : "none");
            // Only what is true (review 11-12): the career is whole on the
            // main key, where a fixed build will load it; NEW GAME replaces it.
            string title = LifeSimManager.TroubleTitle() ?? "";
            Check(title.Contains("COULD NOT LOAD") && title.Contains("LEFT AS IT WAS") && title.Contains("FIXED BUILD") &&
                  title.Contains("NEW GAME REPLACES IT"),
                  "the title says so: left as it was for a fixed build to load, and NEW GAME replaces it", title);
            Check(LifeSimManager.FailedEraseLine.Contains("COULD NOT LOAD") && LifeSimManager.FailedEraseLine.Contains("fixed build"),
                  "and NEW GAME's own confirm says what replacing it costs", LifeSimManager.FailedEraseLine);
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
                bakFirst = keys.Count == 1 && Copy(keys[0]) == old;
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
            Check(now.playerName == "AGAIN" && kept != null && Copy(kept) == old &&
                  LifeSimManager.Trouble == LifeSimManager.SaveTrouble.None,
                  "an explicit NEW GAME writes the main key, and the failed career's copy stays", now.playerName);
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// THE WRITER STAMP, played as the v25 build the next round ships
        /// (SimulateCodeVersion), with a stand-in v25 step (BeforeMigrateHook)
        /// because Migrate has nothing above v24 yet: a rollover's save writes
        /// .last; an exe from before the guard loads the v25 career, drops its
        /// v25 fields and the stamp it does not know, plays on and writes it
        /// back still marked v25. The next guarded load catches it, writes
        /// nothing, backs it up, runs the v25 step again and offers this
        /// build's .last copy - which is tried in memory first - and either
        /// answer sticks.
        /// </summary>
        static void SaveGuardRewritten()
        {
            int v25 = LifeSimManager.LastUnstampedVersion + 1;
            LifeSimManager.SimulateCodeVersion(v25);
            // THE v25 STEP: records the version it ran from and the newest
            // backup at that moment, and stamps v25 as every step does.
            int ranFrom = -1;
            string bakAtStep = null;
            System.Action<LifeState> v25Step = s =>
            {
                ranFrom = s.saveVersion;
                var keys = LifeSimManager.BackupKeys();
                bakAtStep = keys.Count > 0 ? Copy(keys[keys.Count - 1]) : null;
                if (s.saveVersion < v25) s.saveVersion = v25;
            };
            LifeSimManager.BeforeMigrateHook = v25Step;
            try
            {
                LifeSimManager.UseScratchSave(GuardScratch);
                LifeSimManager.StartNewGame("STAMP", 30, LifeRules.DefaultJobIndex);
                var firstLast = LastText != null ? JsonUtility.FromJson<LifeState>(LastText) : null;
                Check(firstLast != null && firstLast.playerName == "STAMP" && LastText == Stored,
                      "a new career's first save writes its own .last copy at once");
                var st = LifeSimManager.State;
                st.saveVersion = v25;   // a v25 build's career
                st.money = 4000;
                LifeRules.SleepUntilMorning(st);   // the rollover marks .last
                LifeSimManager.Save();
                string mine = Stored;
                var mineS = JsonUtility.FromJson<LifeState>(mine);
                LifeSimManager.LastCopy(out _, out string when);
                Check(LastText == mine && mineS.writerVersion == v25,
                      "the first save after a daily rollover writes <key>.last.json again, stamped v" + v25);
                Check(Regex.IsMatch(when, @"^\d{4}-\d\d-\d\d \d\d:\d\d$"), "with the date it was written", when);
                LifeSimManager.Save();
                Check(LastText == mine, "(a save with no rollover leaves .last alone)");

                // THE OLD EXE. The v25 career on disk carries v25 fields (this
                // stands for them); the old exe drops those and writerVersion,
                // plays two more days and writes it back still marked v25.
                string onDisk = mine.Insert(1, "\"v25Field\":7,");
                PlayerPrefs.SetString(GuardKey, onDisk);
                var oldExe = JsonUtility.FromJson<LifeState>(onDisk);
                oldExe.day += 2;
                oldExe.money = 9000;
                string stripped = Unstamped(JsonUtility.ToJson(oldExe));
                PlayerPrefs.SetString(GuardKey, stripped);
                Check(!stripped.Contains("writerVersion") && !stripped.Contains("v25Field") &&
                      LifeSimManager.PeekVersion(stripped) == v25,
                      "(the old exe's string: still v" + v25 + ", no writer stamp, no v25 fields)");

                ranFrom = -1;
                bakAtStep = null;
                st = LifeSimManager.Reload();
                Check(LifeSimManager.Trouble == LifeSimManager.SaveTrouble.Rewritten,
                      "a v25 save with its v25 fields and writerVersion stripped is detected", LifeSimManager.Trouble);
                Check(ranFrom == LifeSimManager.LastUnstampedVersion && st.saveVersion == v25,
                      "its v" + v25 + " step runs again, from v" + LifeSimManager.LastUnstampedVersion +
                      " (no stamp: a build from before the guard)", "from v" + ranFrom + " to v" + st.saveVersion);
                Check(bakAtStep == stripped, "the older build's string is backed up before that step runs");
                Check(Stored == stripped, "the load writes nothing back");
                Check(st.day == oldExe.day && st.money == 9000, "until a choice is made the career is the older build's",
                      "day " + st.day + " $" + st.money);
                bool offered = LifeSimManager.LastCopy(out int lastDay, out string lastWhen);
                Check(offered && lastDay == mineS.day && lastWhen == when && LifeSimManager.HasLastCopy,
                      "and the .last copy is offered, with its day and date", lastDay + " / " + lastWhen);
                string title = LifeSimManager.TroubleTitle() ?? "";
                Check(title.Contains("THIS BUILD'S COPY: " + LifeSimManager.DayNoYear(mineS.day) + ", SAVED " + when) &&
                      title.Contains("THE OLDER BUILD'S: " + LifeSimManager.DayNoYear(oldExe.day)),
                      "the title names this build's copy (game date + when saved) and the older build's", title);

                // THIS BUILD'S COPY THAT WILL NOT LOAD (review 11-12): tried in
                // memory first, refused, and nothing on disk or in play moves.
                LifeSimManager.BeforeMigrateHook = s =>
                {
                    if (s.money == mineS.money) throw new System.InvalidOperationException("a step that breaks on this copy (simulated)");
                    v25Step(s);
                };
                bool used = LifeSimManager.UseLastCopy();
                LifeSimManager.BeforeMigrateHook = v25Step;
                Check(!used && Stored == stripped && LifeSimManager.Trouble == LifeSimManager.SaveTrouble.Rewritten &&
                      LifeSimManager.State.money == 9000 && LastText == mine,
                      "a .last copy whose upgrade throws is refused: the main key, the career in play and the copy are untouched",
                      "used " + used + ", $" + LifeSimManager.State.money);
                string refusedTitle = LifeSimManager.TroubleTitle() ?? "";
                Check(!LifeSimManager.HasLastCopy && refusedTitle.Contains("WOULD NOT LOAD"),
                      "and it is not offered again this run; the title says the older build's copy carries on", refusedTitle);
                LifeSimManager.Reload();
                Check(LifeSimManager.Trouble == LifeSimManager.SaveTrouble.Rewritten && LifeSimManager.HasLastCopy,
                      "(the next boot asks again)", LifeSimManager.Trouble);

                // Answer 1: this build's own copy.
                Check(LifeSimManager.UseLastCopy(), "choosing this build's copy...");
                var now = LifeSimManager.State;
                Check(now.day == mineS.day && now.money == mineS.money && Stored == mine &&
                      LifeSimManager.Trouble == LifeSimManager.SaveTrouble.None,
                      "...makes it the career, byte for byte, and the question is gone", "day " + now.day + " $" + now.money);
                Check(LifeSimManager.BackupKeys().Exists(k => Copy(k) == stripped), "the older build's copy stays in the backups");
                LifeSimManager.Reload();
                Check(LifeSimManager.Trouble == LifeSimManager.SaveTrouble.None && LifeSimManager.State.day == mineS.day,
                      "and the next boot loads it as an ordinary save", LifeSimManager.Trouble);

                // Answer 2: the older build's.
                PlayerPrefs.SetString(GuardKey, stripped);
                LifeSimManager.Reload();
                LifeSimManager.KeepStoredCopy();
                var keptS = JsonUtility.FromJson<LifeState>(Stored);
                Check(keptS.day == oldExe.day && keptS.money == 9000 && keptS.saveVersion == v25 && keptS.writerVersion == v25,
                      "keeping the older build's copy saves it at once: v" + v25 + " again (its step re-run), stamped by this build",
                      "day " + keptS.day + " v" + keptS.saveVersion + " writer v" + keptS.writerVersion);
                LifeSimManager.Reload();
                Check(LifeSimManager.Trouble == LifeSimManager.SaveTrouble.None,
                      "and the next boot does not ask again (its stamp now equals its version)", LifeSimManager.Trouble);

                // With no usable .last there is nothing to choose: the career
                // carries on as the older build's, its dropped fields seeded.
                File.Delete(Path.Combine(LifeSimManager.CopyDir, LifeSimManager.LastCopyName));
                PlayerPrefs.SetString(GuardKey, stripped);
                st = LifeSimManager.Reload();
                Check(LifeSimManager.Trouble == LifeSimManager.SaveTrouble.Rewritten && !LifeSimManager.HasLastCopy &&
                      !LifeSimManager.UseLastCopy() && st.day == oldExe.day,
                      "with no .last copy it is still caught, and nothing is offered", LifeSimManager.TroubleTitle());
            }
            finally
            {
                LifeSimManager.BeforeMigrateHook = null;
                LifeSimManager.SimulateCodeVersion(0);
            }
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// .LAST AFTER A NEW GAME (review 11-12): the copy belongs to the
        /// career it was written from. An explicit NEW GAME moves the erased
        /// career's .last into the backups, and the new career's first save
        /// writes its own - so when an older exe writes the NEW career back
        /// before its first night, the copy offered (THIS BUILD'S COPY, the
        /// title's first button) is the new career's, never the erased one.
        /// </summary>
        static void SaveGuardNewGameLast()
        {
            int v25 = LifeSimManager.LastUnstampedVersion + 1;
            LifeSimManager.SimulateCodeVersion(v25);
            LifeSimManager.BeforeMigrateHook = s => { if (s.saveVersion < v25) s.saveVersion = v25; };
            try
            {
                LifeSimManager.UseScratchSave(GuardScratch);
                LifeSimManager.StartNewGame("ERASED", 30, LifeRules.DefaultJobIndex);
                var a = LifeSimManager.State;
                a.saveVersion = v25;
                a.day = 40;
                LifeRules.SleepUntilMorning(a);
                LifeSimManager.Save();
                string aLast = LastText;
                Check(aLast != null && JsonUtility.FromJson<LifeState>(aLast).playerName == "ERASED",
                      "(career ERASED has its .last after a night)");

                LifeSimManager.DeleteSave();   // the title's YES - START OVER
                Check(!LifeSimManager.CopyExists(LifeSimManager.LastCopyName) &&
                      LifeSimManager.BackupKeys().Exists(k => Copy(k) == aLast),
                      "an explicit NEW GAME moves the erased career's .last into the backups and off the .last name");
                LifeSimManager.StartNewGame("NEWBIE", 25, LifeRules.DefaultJobIndex);
                var b = LifeSimManager.State;
                b.saveVersion = v25;
                LifeSimManager.Save();
                var bLast = LastText != null ? JsonUtility.FromJson<LifeState>(LastText) : null;
                Check(bLast != null && bLast.playerName == "NEWBIE", "the new career's first save writes its own .last at once",
                      bLast != null ? bLast.playerName : "none");

                // An exe from before the guard writes NEWBIE back before its first night.
                var oldExe = JsonUtility.FromJson<LifeState>(Stored);
                oldExe.money += 50;
                PlayerPrefs.SetString(GuardKey, Unstamped(JsonUtility.ToJson(oldExe)));
                LifeSimManager.Reload();
                var offered = LastText != null ? JsonUtility.FromJson<LifeState>(LastText) : null;
                Check(LifeSimManager.Trouble == LifeSimManager.SaveTrouble.Rewritten && LifeSimManager.HasLastCopy &&
                      offered != null && offered.playerName == "NEWBIE",
                      "when an older exe then writes the new career back, the copy offered is the new career's, not the erased one's",
                      offered != null ? offered.playerName : "none");
                Check(LifeSimManager.UseLastCopy() && LifeSimManager.State.playerName == "NEWBIE",
                      "and taking it carries on as the new career", LifeSimManager.State.playerName);
            }
            finally
            {
                LifeSimManager.BeforeMigrateHook = null;
                LifeSimManager.SimulateCodeVersion(0);
            }
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// THE STEPS THAT RUN AGAIN START WHERE THE WRITER STOPPED (review
        /// 11-12). A v25 build run with PSX_SAVE_GUARD=0 loads a v26 career,
        /// keeps every field up to v25 and writes it back stamped 25: the next
        /// guarded v26 build re-runs the v26 step only, because a v25 step run
        /// again would act on live data, not on fields that were dropped. A
        /// save with no stamp (an exe from before the guard) still re-runs
        /// every step from v25.
        /// </summary>
        static void SaveGuardWriterFrom()
        {
            int v25 = LifeSimManager.LastUnstampedVersion + 1, v26 = v25 + 1;
            int ranFrom = -1;
            try
            {
                // The v25 build, guard off, writes a v26 career back.
                LifeSimManager.SimulateCodeVersion(v25);
                string raw = GuardCareer("WRITER", 60, 6000);
                var s = JsonUtility.FromJson<LifeState>(raw);
                s.saveVersion = v26;
                s.writerVersion = v26;
                PlayerPrefs.SetString(GuardKey, JsonUtility.ToJson(s));
                LifeSimManager.Guard = false;
                LifeSimManager.Reload();
                LifeSimManager.Save();
                LifeSimManager.Guard = true;
                var back = JsonUtility.FromJson<LifeState>(Stored);
                Check(back.saveVersion == v26 && back.writerVersion == v25,
                      "(a v" + v25 + " build with the guard off writes a v" + v26 + " career back stamped " + v25 + ")",
                      "v" + back.saveVersion + " writer v" + back.writerVersion);

                // The guarded v26 build, with a stand-in v26 step.
                LifeSimManager.SimulateCodeVersion(v26);
                LifeSimManager.BeforeMigrateHook = st => { ranFrom = st.saveVersion; if (st.saveVersion < v26) st.saveVersion = v26; };
                LifeSimManager.Reload();
                Check(LifeSimManager.Trouble == LifeSimManager.SaveTrouble.Rewritten && ranFrom == v25,
                      "a guarded v" + v26 + " build catches it and re-runs only the v" + v26 + " step (from v" + v25 + ", the writer's own)",
                      LifeSimManager.Trouble + ", from v" + ranFrom);
                PlayerPrefs.SetString(GuardKey, Unstamped(JsonUtility.ToJson(back)));
                LifeSimManager.Reload();
                Check(LifeSimManager.Trouble == LifeSimManager.SaveTrouble.Rewritten && ranFrom == LifeSimManager.LastUnstampedVersion,
                      "while the same save with no stamp (a build from before the guard) re-runs every step from v" + v25,
                      LifeSimManager.Trouble + ", from v" + ranFrom);
            }
            finally
            {
                LifeSimManager.Guard = true;
                LifeSimManager.BeforeMigrateHook = null;
                LifeSimManager.SimulateCodeVersion(0);
            }
        }

        // ------------------------------------------------------------------
        /// <summary>PSX_SAVE_GUARD=0 IS THE OLD SAVE: a newer career loads and
        /// is written back, a broken one falls through to a new game, no
        /// copies. The writer stamp stays, which is what lets the next guarded
        /// build catch a write like that one - shown here too.</summary>
        static void SaveGuardOff()
        {
            int cur = LifeSimManager.CodeVersion;
            LifeSimManager.Guard = false;
            try
            {
                string raw = GuardCareer("OFF", 9, 99);
                Check(!LifeSimManager.CopyExists(LifeSimManager.LastCopyName),
                      "guard off: a new career's first save writes no .last copy");
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
                      "and is written back (the old behaviour), stamped by this older build...",
                      "v" + back.saveVersion + " writer v" + back.writerVersion);
                LifeSimManager.Guard = true;
                LifeSimManager.SimulateCodeVersion(cur + 1);
                LifeSimManager.Reload();
                Check(LifeSimManager.Trouble == LifeSimManager.SaveTrouble.Rewritten,
                      "...so a guarded v" + (cur + 1) + " build catches that write-back", LifeSimManager.Trouble);
                LifeSimManager.SimulateCodeVersion(0);
                LifeSimManager.Guard = false;
                PlayerPrefs.SetString(GuardKey, "{\"saveVersion\":" + cur + ",\"money\":");
                st = LifeSimManager.Reload();
                Check(string.IsNullOrEmpty(st.playerName) && LifeSimManager.Trouble == LifeSimManager.SaveTrouble.None &&
                      LifeSimManager.FailedKeys().Count == 0,
                      "a broken one falls through to a new game and no failed copy is kept (the old behaviour)");
            }
            finally
            {
                LifeSimManager.SimulateCodeVersion(0);
                LifeSimManager.Guard = true;
            }
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// WHAT THE GUARD COSTS A SAVE (design: "PerfTour reports save size and
        /// save time"; over 50 ms moves the career to a file). Printed, never
        /// failed: an editor's registry is not a phone. A worked career (ten
        /// cars, sixty days, faults, a shelf part, a booking) saved with the
        /// guard off and on, the first save after a rollover (which also
        /// writes the .last file), and a load plain and with a migration (which
        /// also writes a backup file). The size before R0a is the same string
        /// without its writer stamp - the one field R0a added. Then
        /// MeasureSaveCost, the line PerfTour writes in a player.
        /// </summary>
        static void SaveGuardCost()
        {
            int cur = LifeSimManager.CodeVersion;
            LifeSimManager.UseScratchSave(GuardScratch);
            LifeSimManager.StartNewGame("COST", 30, LifeRules.DefaultJobIndex);
            int freshBytes = Encoding.UTF8.GetByteCount(Stored);
            var st = LifeSimManager.State;
            WorkCareer(st, 10, 60);
            LifeSimManager.Save();
            string json = Stored;
            int bytes = Encoding.UTF8.GetByteCount(json);
            int unstamped = Encoding.UTF8.GetByteCount(Unstamped(json));

            var sw = new System.Diagnostics.Stopwatch();
            double Ms(int n, System.Action a)
            {
                a();   // warm
                sw.Restart();
                for (int i = 0; i < n; i++) a();
                sw.Stop();
                return sw.Elapsed.TotalMilliseconds / n;
            }
            // Off and on INTERLEAVED, five rounds, medians: a registry write
            // wanders by tenths of a millisecond from one batch to the next.
            var offs = new List<double>();
            var ons = new List<double>();
            for (int r = 0; r < 5; r++)
            {
                LifeSimManager.Guard = false;
                offs.Add(Ms(10, LifeSimManager.Save));
                LifeSimManager.Guard = true;
                ons.Add(Ms(10, LifeSimManager.Save));
            }
            offs.Sort();
            ons.Sort();
            double saveOff = offs[offs.Count / 2], saveOn = ons[ons.Count / 2];
            double saveRoll = Ms(10, () => { LifeSimManager.NoteRollover(LifeSimManager.State); LifeSimManager.Save(); });
            int lastBytes = Encoding.UTF8.GetByteCount(LastText ?? "");
            double load = Ms(10, () => LifeSimManager.Reload());
            // A load with a migration: every old string is made first and only
            // the load is timed (median of five, after one to warm).
            var olds = new List<string>();
            for (int i = 0; i < 6; i++) olds.Add(AsWrittenAt(json, cur - 1, 100000 + i));
            PlayerPrefs.SetString(GuardKey, olds[0]);
            LifeSimManager.Reload();
            var migs = new List<double>();
            for (int i = 1; i < olds.Count; i++)
            {
                PlayerPrefs.SetString(GuardKey, olds[i]);
                sw.Restart();
                LifeSimManager.Reload();
                sw.Stop();
                migs.Add(sw.Elapsed.TotalMilliseconds);
            }
            migs.Sort();
            double loadMig = migs[migs.Count / 2];
            int copies = 0;
            foreach (var key in LifeSimManager.BackupKeys()) copies += Encoding.UTF8.GetByteCount(Copy(key) ?? "");
            double saveWithCopies = Ms(20, LifeSimManager.Save);
            string measured = LifeSimManager.MeasureSaveCost();

            Line("    SAVE SIZE (measured on this branch): a new career " + freshBytes + " bytes; a worked career (" +
                 LifeSimManager.State.cars.Count + " cars, 60 days, faults, a shelf part, a booking) " + bytes + " bytes (" +
                 (bytes / 1024f).ToString("0.0") + " KB), of which the writer stamp is " + (bytes - unstamped) +
                 " bytes, so " + unstamped + " bytes before R0a. PlayerPrefs holds the career only; the copies are files: " +
                 ".last " + lastBytes + " bytes, " + LifeSimManager.BackupKeys().Count + " backups " +
                 (copies / 1024f).ToString("0.0") + " KB");
            Line("    SAVE TIME (editor, registry; median of 5 interleaved batches of 10): Save() with the guard off (this " +
                 "branch's Save with PSX_SAVE_GUARD=0, the pre-R0a Save plus the stamp) " + saveOff.ToString("0.00") +
                 " ms, guard on " + saveOn.ToString("0.00") + " ms, first save after a rollover (+ the .last file) " +
                 saveRoll.ToString("0.00") + " ms, with .last and " + LifeSimManager.BackupKeys().Count +
                 " backup files beside it " + saveWithCopies.ToString("0.00") + " ms");
            Line("    LOAD TIME: " + load.ToString("0.00") + " ms; with a migration (+ a backup file; only the load timed) " +
                 loadMig.ToString("0.00") + " ms");
            Line("    PerfTour's env line (LifeSimManager.MeasureSaveCost): " + measured);
            Check(bytes > freshBytes && saveOn > 0.0, "the save cost was measured", bytes + " bytes");
            Check(measured.StartsWith("career "), "MeasureSaveCost (PerfTour's save line) measures a scratch career", measured);
            LifeSimManager.UseCareerSave();
            string refused = LifeSimManager.MeasureSaveCost();
            LifeSimManager.UseScratchSave(GuardScratch);
            Check(refused.StartsWith("not measured"), "and never times the player's own career", refused);
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// ONE STEP PER VERSION (design §2 "Versions"). If two separately
        /// shipped rounds share a number, the first stamps the career and the
        /// second's step never runs - its fields read back as 0 on every
        /// existing car. So: Migrate's guards are exactly 2, 3, ... current,
        /// once each and in order; each number is stamped exactly once (in
        /// Migrate or its MigrateTo* step); a save at every version from 1 up
        /// migrates to the current one; and a second pass on a migrated save
        /// runs nothing (Migrate has no unguarded code).
        ///
        /// THE RE-RUN (review 11-12): a save that a build from before the
        /// guard wrote back has every step above v24 run AGAIN on data that
        /// already went through them. That is checked on a worked career -
        /// set back to v24 and migrated again, it must not change - once a
        /// step above v24 exists; until then the log says it is not tested.
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
            var unguarded = new List<int>();
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
                    if (JsonUtility.ToJson(s) != once) unguarded.Add(v);
                }
                catch (System.Exception e) { wrong.Add("v" + v + " threw " + e.GetType().Name); }
            }
            Check(wrong.Count == 0, "a save at every version 1..v" + (cur - 1) + " migrates to v" + cur,
                  wrong.Count == 0 ? null : string.Join(", ", wrong));
            Check(unguarded.Count == 0, "Migrate has no unguarded code (a second pass on a v" + cur + " save runs nothing)",
                  unguarded.Count == 0 ? null : string.Join(",", unguarded));
            var fresh = new LifeState();
            string freshJson = JsonUtility.ToJson(fresh);
            LifeSimManager.Migrate(fresh);
            Check(fresh.saveVersion == cur && JsonUtility.ToJson(fresh) == freshJson,
                  "a new career (born at v" + cur + ") runs no step");
            Check(LifeSimManager.LastUnstampedVersion <= cur,
                  "the guard's last unstamped version (v" + LifeSimManager.LastUnstampedVersion + ") is not above the code's");

            // THE RE-RUN, as LoadGuarded does it for a write-back by a build
            // from before the guard: back to v24, every step above it again.
            int top = LifeSimManager.LastUnstampedVersion;
            var worked = LifeRules.SeedNewGame("LADDER", 30, LifeRules.DefaultJobIndex);
            WorkCareer(worked, 4, 20);
            LifeSimManager.Migrate(worked);
            string migrated = JsonUtility.ToJson(worked);
            worked.saveVersion = Mathf.Min(worked.saveVersion, top);
            string rerunError = null;
            try { LifeSimManager.Migrate(worked); }
            catch (System.Exception e) { rerunError = e.GetType().Name + ": " + e.Message; }
            bool same = rerunError == null && JsonUtility.ToJson(worked) == migrated;
            if (cur > top)
                Check(same, "a worked career's steps v" + (top + 1) + "..v" + cur + " run a second time with no change " +
                            "(what a write-back by a build from before the guard runs again)",
                      rerunError ?? (same ? null : "the career changed"));
            else
                Line("    step re-run above v" + top + ": NOT TESTED - there is no step above v" + top + " yet; " +
                     "the first one (R0b's) is run twice on a worked career here when it lands" +
                     (rerunError != null ? " (and the empty re-run threw: " + rerunError + ")" : ""));
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
