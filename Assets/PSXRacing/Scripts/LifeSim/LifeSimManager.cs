using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// Owns the LifeState: load, save, new game. Pure state — no UI.
    ///
    /// Persistence is PlayerPrefs carrying one JSON blob, which is the direct
    /// equivalent of what RG2 shipped (a JSON blob in localStorage under
    /// 'driverCitySave'). On WebGL, PlayerPrefs is backed by browser storage
    /// and Unity handles the async IndexedDB flush; raw File IO there needs a
    /// manual JS_FileSystem_Sync and loses data on tab close if you forget it,
    /// so PlayerPrefs is deliberately the boring, safe choice.
    /// </summary>
    public static class LifeSimManager
    {
        const string CareerKey = "psxRacingLifeSave";

        /// <summary>The PlayerPrefs key the career lives under: the player's
        /// own, unless a test run moved it aside (<see cref="UseScratchSave"/>).</summary>
        static string saveKey = CareerKey;

        static LifeState state;

        /// <summary>What <see cref="State"/> hands out in an edition with no
        /// career - see there.</summary>
        static LifeState noCareerScratch;
        static bool warnedNoCareer;

        /// <summary>The live state. Loads (or creates) lazily so any scene can
        /// run first in the editor without a boot ceremony.
        ///
        /// NOT IN THE CITY EDITION. The Charlotte test page has no career
        /// (Edition.HasCareer) and promises never to create one: its only
        /// memory is the car, hour and weather picks (CityFrontEnd). Every
        /// career surface there is closed at its door - the drive-thru windows
        /// (DriveThru.Serves), the food signpost, the fuel truck, the pumps,
        /// the debug bench - and this is the net under all of them: a CITY
        /// build that still asks gets a blank, THROWAWAY state that is never
        /// loaded from and never saved (Save() is a no-op there), with one
        /// warning in the log naming the leak, so nothing reaches the test
        /// page's save database and nothing shows a career's pocket.</summary>
        public static LifeState State
        {
            get
            {
                if (!Edition.HasCareer)
                {
                    if (!warnedNoCareer)
                    {
                        warnedNoCareer = true;
                        Debug.LogWarning("[LifeSim] the " + Edition.Name(Edition.Current) +
                                         " edition has no career, but something asked for one - handing it a " +
                                         "throwaway state that is never saved\n" + System.Environment.StackTrace);
                    }
                    return noCareerScratch ??= new LifeState();
                }
                if (state == null) state = Load() ?? NewGame();
                return state;
            }
        }

        /// <summary>Does this build keep a career on disk at all? False in the
        /// CITY edition, where <see cref="Save"/> writes nothing.</summary>
        public static bool Persists => Edition.HasCareer;

        public static bool HasSave => Persists && PlayerPrefs.HasKey(saveKey);

        /// <summary>
        /// A TEST RUN'S OWN CAREER (2026-11-04). The perf tour drives a seeded
        /// career, and a Windows player's PlayerPrefs are the owner's own (one
        /// registry key that every PC build shares): a tour on the owner's PC
        /// loaded THEIR career, refuelled its car and saved it back. A test
        /// calls this before anything loads; its career then lives under its
        /// own key, emptied first so every run starts from the same new game,
        /// and the owner's save is never read or written.
        /// </summary>
        public static void UseScratchSave(string name)
        {
            saveKey = CareerKey + "." + name;
            // The scratch career's own copies go with it (never the career's:
            // DeleteCopies refuses the real key).
            DeleteCopies();
            PlayerPrefs.DeleteKey(saveKey);
            state = null;
            ResetGuard();
        }

        /// <summary>Back onto the player's own key after a test run's
        /// <see cref="UseScratchSave"/>. Reads nothing and writes nothing:
        /// the career loads lazily, as at boot.</summary>
        public static void UseCareerSave()
        {
            saveKey = CareerKey;
            state = null;
            ResetGuard();
        }

        /// <summary>The PlayerPrefs key the career is under right now (the
        /// player's own, or a test's scratch key).</summary>
        public static string SaveKey => saveKey;

        /// <summary>Drop the live career and load it from disk again, as a
        /// fresh boot would (the save guard's verdict included). For tools
        /// and the self-test.</summary>
        public static LifeState Reload()
        {
            state = null;
            return State;
        }

        public static void Save()
        {
            if (!Persists) return;   // CITY: no career, nothing written (see State)
            if (state == null) return;
            // SAVE SAFETY: a newer build's career (read-only) and a career this
            // build could not load (held until an explicit NEW GAME) are never
            // written over. Every caller that saves comes through here.
            if (Guard && (readOnly || holdWrites))
            {
                if (!warnedHeld)
                {
                    warnedHeld = true;
                    Debug.LogWarning("[LifeSim] not saving: " + (readOnly
                        ? "the career on disk was saved by a newer build (v" + TroubleVersion + "); this build (v" +
                          CodeVersion + ") plays it read-only"
                        : "the career on disk could not be loaded; it is kept as it is until NEW GAME"));
                }
                return;
            }
            // The writer stamp, always (even with the guard off: a build that
            // writes back a version it does not know is then caught by the
            // next one that does - see LifeState.writerVersion).
            state.writerVersion = CodeVersion;
            string json = JsonUtility.ToJson(state);
            PlayerPrefs.SetString(saveKey, json);
            if (lastDue && Guard)
            {
                // The first save after a daily rollover: this build's own copy,
                // with the date, for the day an older exe writes over the career.
                // A copy that cannot be written (a full store: WebGL caps all
                // PlayerPrefs at 1 MB) never costs the save itself.
                try
                {
                    PlayerPrefs.SetString(saveKey + LastSuffix, json);
                    PlayerPrefs.SetString(saveKey + LastWhenSuffix,
                        System.DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
                }
                catch (System.Exception e) { Debug.LogWarning("[LifeSim] could not write the .last copy: " + e.Message); }
            }
            lastDue = false;
            PlayerPrefs.Save();
        }

        public static LifeState Load()
        {
            ResetGuard();
            if (!PlayerPrefs.HasKey(saveKey)) return null;
            return Guard ? LoadGuarded() : LoadUnguarded();
        }

        /// <summary>Load as it was before R0a (PSX_SAVE_GUARD=0): no backup,
        /// a newer save is migrated and written back like any other, and any
        /// throw falls through to a new game that the next save writes over
        /// the career.</summary>
        static LifeState LoadUnguarded()
        {
            try
            {
                var s = JsonUtility.FromJson<LifeState>(PlayerPrefs.GetString(saveKey));
                if (s == null || s.saveVersion < 1) return null;
                Migrate(s);
                // After the migration, never inside it: this is not a save
                // version (the editions remove nothing from the catalog) but a
                // question asked of THIS build on every load.
                EditionSanitize(s);
                StripBuiltCars(s);
                return s;
            }
            catch { return null; }   // corrupt save: fall through to new game
        }

        // =================== SAVE SAFETY (R0a, 2026-11-12) ===================
        //
        // Every PC build shares ONE registry key (UseScratchSave above), and
        // more than one build is usually on disk (Win64_PC29 and Win64_PC30 on
        // 11-11). Before R0a, Load() had no guard:
        //   * an older exe drops the fields it does not know, and its next
        //     Save() writes the stripped career back with the NEWER version
        //     number still on it, so the newer build never re-seeds them;
        //   * any throw in Load or Migrate returned null and started a new
        //     game, which the next save wrote over the career.
        // So, before any round changes the save (Fault_System_Design §2):
        //   * BACKUP: before Migrate, the raw string is copied to
        //     <key>.bak.v<old>.<date>; the last three are kept.
        //   * A NEWER SAVE IS READ-ONLY: Save() writes nothing and the title
        //     says so. Never migrated, never written back.
        //   * WRITER STAMP: every Save() stamps LifeState.writerVersion, and
        //     the first save after each daily rollover also writes <key>.last.
        //     A save at v25+ whose writerVersion is below its version was
        //     written back by an exe from before R0a: its steps from v25 up are
        //     run again (they are idempotent; that exe dropped their fields)
        //     and the title offers <key>.last, this build's own copy, instead.
        //   * NO NEW GAME OVER A CAREER: a load or upgrade that throws keeps
        //     the raw string as <key>.failed.v<old>.<date>, leaves the main key
        //     alone, and the title says so. Only an explicit NEW GAME
        //     (StartNewGame / DeleteSave) writes the main key again.
        // Version steps themselves are untouched: one per shipped round,
        // numbered at merge (LifeSimSelfTest.TestSaveVersionLadder).

        /// <summary>PSX_SAVE_GUARD=0 is the save as it was before R0a: no
        /// backup, no read-only newer saves, no .last, and a failed load
        /// starts a new game. The writer stamp stays (it is only a number in
        /// the save). Public for the self-test.</summary>
        public static bool Guard = System.Environment.GetEnvironmentVariable("PSX_SAVE_GUARD") != "0";

        /// <summary>The newest save version a build from before R0a can have
        /// written. Those builds never stamp writerVersion, so a save above
        /// this whose stamp is below its version was rewritten by one.</summary>
        public const int LastUnstampedVersion = 24;

        /// <summary>The last three pre-migration copies are kept.</summary>
        public const int BackupKeep = 3;
        /// <summary>Failed loads keep up to five copies (each is a career
        /// nothing else holds; trimmed only past this).</summary>
        public const int FailedKeep = 5;

        const string BakTag = ".bak.v", BakIndex = ".bak";
        const string FailTag = ".failed.v", FailIndex = ".failed";
        const string LastSuffix = ".last", LastWhenSuffix = ".last.when";

        /// <summary>What the last load found wrong with the career on disk.</summary>
        public enum SaveTrouble
        {
            None,
            /// <summary>Saved by a NEWER build: loaded read-only, never written back.</summary>
            Newer,
            /// <summary>A v25+ save that an exe from before R0a wrote back
            /// (writerVersion below its version). The fields it dropped are
            /// seeded again; the title offers this build's own .last copy.</summary>
            Rewritten,
            /// <summary>Reading or upgrading it threw. Kept untouched; nothing
            /// is saved until an explicit NEW GAME.</summary>
            Failed,
        }

        /// <summary>What the last <see cref="Load"/> found (None when nothing
        /// was wrong, or no save). Read by the title screen.</summary>
        public static SaveTrouble Trouble { get; private set; }
        /// <summary>The stored save's own version when <see cref="Trouble"/>
        /// was set (0 when it could not be read).</summary>
        public static int TroubleVersion { get; private set; }
        /// <summary>A newer build's career, played without saving.</summary>
        public static bool ReadOnly => Guard && readOnly;
        /// <summary>Where the last failed load's raw string was kept.</summary>
        public static string FailedCopyKey { get; private set; }

        static bool readOnly, holdWrites, lastDue, warnedHeld, readOnlyNoteDue;

        /// <summary>The save version THIS build writes (a new career's).</summary>
        public static int CodeVersion => simulatedCodeVersion > 0 ? simulatedCodeVersion : BuiltVersion;
        static int builtVersion;
        static int BuiltVersion => builtVersion > 0 ? builtVersion : (builtVersion = new LifeState().saveVersion);
        static int simulatedCodeVersion;

        /// <summary>SELF-TEST ONLY: pretend this build writes version
        /// <paramref name="v"/> (0 = the real one), so the guard can be driven
        /// against a v25 save before any round ships v25.</summary>
        public static void SimulateCodeVersion(int v)
        {
            simulatedCodeVersion = Mathf.Max(0, v);
        }

        /// <summary>SELF-TEST ONLY: called by a guarded load after the backup
        /// is written and before the first migration step runs - to look at
        /// the backup at that moment, or to throw as a broken step would.</summary>
        public static System.Action<LifeState> BeforeMigrateHook;

        static void ResetGuard()
        {
            Trouble = SaveTrouble.None;
            TroubleVersion = 0;
            FailedCopyKey = null;
            readOnly = holdWrites = lastDue = warnedHeld = readOnlyNoteDue = false;
        }

        static LifeState LoadGuarded()
        {
            string raw = PlayerPrefs.GetString(saveKey);
            int code = CodeVersion;
            LifeState s;
            try { s = JsonUtility.FromJson<LifeState>(raw); }
            catch (System.Exception e) { return Fail(raw, PeekVersion(raw), "it could not be read (" + e.GetType().Name + ": " + e.Message + ")"); }
            if (s == null || s.saveVersion < 1) return Fail(raw, PeekVersion(raw), "it holds no save version");

            int stored = s.saveVersion;
            if (stored > code)
            {
                // A NEWER BUILD'S CAREER. Nothing this build knows how to do to
                // it is safe to write back: shown, played, never saved.
                readOnly = true;
                readOnlyNoteDue = true;
                Trouble = SaveTrouble.Newer;
                TroubleVersion = stored;
                Debug.LogWarning("[LifeSim] the career was saved by a newer build (v" + stored + "; this build is v" +
                                 code + ") - loaded READ-ONLY, nothing will be saved");
                // In memory only (never written back): what this build cannot
                // show, it does not try to - a booking at a venue it lacks
                // would otherwise load a scene that is not there.
                try { EditionSanitize(s); StripBuiltCars(s); }
                catch (System.Exception e) { Debug.LogWarning("[LifeSim] read-only career: sanitize threw " + e.Message); }
                return s;
            }

            // WRITTEN BACK BY AN EXE FROM BEFORE R0a: every step it could not
            // know runs again, from the last version it could have known.
            bool rewritten = stored > LastUnstampedVersion && s.writerVersion < stored;
            if (rewritten) s.saveVersion = LastUnstampedVersion;

            // THE BACKUP, before the first step can touch anything.
            if (s.saveVersion < code) KeepCopy(raw, stored, BakTag, BakIndex, BackupKeep);
            try
            {
                BeforeMigrateHook?.Invoke(s);
                Migrate(s);
                // After the migration, never inside it: this is not a save
                // version (the editions remove nothing from the catalog) but a
                // question asked of THIS build on every load.
                EditionSanitize(s);
                StripBuiltCars(s);
            }
            catch (System.Exception e)
            {
                return Fail(raw, stored, "the upgrade from v" + stored + " threw (" + e.GetType().Name + ": " + e.Message + ")");
            }

            if (rewritten)
            {
                Trouble = SaveTrouble.Rewritten;
                TroubleVersion = stored;
                Debug.LogWarning("[LifeSim] the v" + stored + " career was written back by a build from before the " +
                                 "save guard (writerVersion " + s.writerVersion + "); its v" + (LastUnstampedVersion + 1) +
                                 "+ steps ran again" + (HasLastCopy ? ", and this build's own last copy is offered" : ""));
            }
            return s;
        }

        /// <summary>NO NEW GAME OVER A CAREER: the raw string is kept, the
        /// main key is not touched, and nothing is saved until an explicit
        /// NEW GAME. Returns null, so State hands out a blank career that
        /// <see cref="Save"/> refuses to write.</summary>
        static LifeState Fail(string raw, int version, string why)
        {
            holdWrites = true;
            Trouble = SaveTrouble.Failed;
            TroubleVersion = version;
            try { FailedCopyKey = KeepCopy(raw, version, FailTag, FailIndex, FailedKeep); }
            catch (System.Exception e) { Debug.LogError("[LifeSim] could not keep a copy of the failed save: " + e.Message); }
            Debug.LogError("[LifeSim] the career could not be loaded: " + why + ". The save is untouched (a copy is kept as " +
                           (FailedCopyKey ?? "nothing") + ") and nothing is written over it until NEW GAME.");
            return null;
        }

        static readonly System.Text.RegularExpressions.Regex VersionRx =
            new System.Text.RegularExpressions.Regex("\"saveVersion\"\\s*:\\s*(-?\\d+)");

        /// <summary>The saveVersion written in a raw save string, without
        /// parsing the rest (0 when there is none).</summary>
        public static int PeekVersion(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return 0;
            var m = VersionRx.Match(raw);
            return m.Success && int.TryParse(m.Groups[1].Value, out int v) ? v : 0;
        }

        /// <summary>Copy a raw save under
        /// <c>&lt;key&gt;&lt;tag&gt;&lt;version&gt;.&lt;yyyyMMdd-HHmmss&gt;.&lt;n&gt;</c>
        /// (n counts up within the list, so a trimmed name is never reused)
        /// and list it in <c>&lt;key&gt;&lt;index&gt;</c>, keeping the newest
        /// <paramref name="keep"/>. A string identical to the newest copy is
        /// not copied twice (a career that loads and quits without saving is
        /// backed up once, not on every boot). Returns the copy's key.</summary>
        static string KeepCopy(string raw, int version, string tag, string indexSuffix, int keep)
        {
            string indexKey = saveKey + indexSuffix;
            var keys = ReadIndex(indexKey);
            int seq = 1;
            if (keys.Count > 0)
            {
                string newest = keys[keys.Count - 1];
                if (PlayerPrefs.GetString(newest, null) == raw) return newest;
                int dot = newest.LastIndexOf('.');
                if (dot >= 0 && int.TryParse(newest.Substring(dot + 1), out int last)) seq = last + 1;
            }
            string unique = saveKey + tag + version + "." +
                            System.DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) +
                            "." + seq;
            while (keys.Contains(unique) || PlayerPrefs.HasKey(unique)) unique = unique.Substring(0, unique.LastIndexOf('.') + 1) + (++seq);
            try
            {
                PlayerPrefs.SetString(unique, raw ?? "");
                keys.Add(unique);
                while (keys.Count > keep)
                {
                    PlayerPrefs.DeleteKey(keys[0]);
                    keys.RemoveAt(0);
                }
                PlayerPrefs.SetString(indexKey, string.Join("|", keys));
                PlayerPrefs.Save();
                return unique;
            }
            catch (System.Exception e)
            {
                // A full store (WebGL caps PlayerPrefs at 1 MB): no copy, and
                // the load or erase carries on as it did before the guard.
                Debug.LogWarning("[LifeSim] could not keep a copy of the save as " + unique + ": " + e.Message);
                try { PlayerPrefs.DeleteKey(unique); } catch { }
                return null;
            }
        }

        static System.Collections.Generic.List<string> ReadIndex(string indexKey)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var k in PlayerPrefs.GetString(indexKey, "").Split('|'))
                if (!string.IsNullOrEmpty(k)) list.Add(k);
            return list;
        }

        /// <summary>The pre-migration copies, oldest first (keys).</summary>
        public static System.Collections.Generic.List<string> BackupKeys() => ReadIndex(saveKey + BakIndex);

        /// <summary>The kept copies of saves that failed to load, oldest first.</summary>
        public static System.Collections.Generic.List<string> FailedKeys() => ReadIndex(saveKey + FailIndex);

        /// <summary>A scratch career's copies (backups, failed copies, .last).
        /// Never the player's own: refused on the career key.</summary>
        static void DeleteCopies()
        {
            if (saveKey == CareerKey) return;
            foreach (var k in BackupKeys()) PlayerPrefs.DeleteKey(k);
            foreach (var k in FailedKeys()) PlayerPrefs.DeleteKey(k);
            PlayerPrefs.DeleteKey(saveKey + BakIndex);
            PlayerPrefs.DeleteKey(saveKey + FailIndex);
            PlayerPrefs.DeleteKey(saveKey + LastSuffix);
            PlayerPrefs.DeleteKey(saveKey + LastWhenSuffix);
        }

        /// <summary>The live career's daily rollover (LifeRules.Rollover): the
        /// next <see cref="Save"/> also writes <c>&lt;key&gt;.last</c>. Any
        /// other state (a test's, the CITY throwaway) is ignored.</summary>
        public static void NoteRollover(LifeState s)
        {
            if (s != null && ReferenceEquals(s, state)) lastDue = true;
        }

        /// <summary>This build's own last copy (<c>&lt;key&gt;.last</c>), when
        /// there is one this build can load: written by a guarded build that
        /// knew its version, and no newer than this build.</summary>
        public static bool HasLastCopy => LastCopy(out _, out _);

        /// <summary>The day the <c>.last</c> copy stands on and when it was
        /// written ("yyyy-MM-dd HH:mm"). False when there is none usable.</summary>
        public static bool LastCopy(out int day, out string when)
        {
            day = 0;
            when = PlayerPrefs.GetString(saveKey + LastWhenSuffix, "");
            string last = PlayerPrefs.GetString(saveKey + LastSuffix, null);
            if (string.IsNullOrEmpty(last)) return false;
            try
            {
                var s = JsonUtility.FromJson<LifeState>(last);
                if (s == null || s.saveVersion < 1 || s.saveVersion > CodeVersion || s.writerVersion < s.saveVersion ||
                    string.IsNullOrEmpty(s.playerName)) return false;
                day = s.day;
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// The title's answer to <see cref="SaveTrouble.Rewritten"/>: carry on
        /// from THIS build's own last copy. The career on disk (the older
        /// build's) is backed up first, then <c>.last</c> becomes the career
        /// and is loaded. False (nothing changed) when there is no such copy.
        /// </summary>
        public static bool UseLastCopy()
        {
            if (Trouble != SaveTrouble.Rewritten || !LastCopy(out _, out _)) return false;
            string last = PlayerPrefs.GetString(saveKey + LastSuffix, null);
            string onDisk = PlayerPrefs.GetString(saveKey, null);
            if (!string.IsNullOrEmpty(onDisk)) KeepCopy(onDisk, PeekVersion(onDisk), BakTag, BakIndex, BackupKeep);
            PlayerPrefs.SetString(saveKey, last);
            PlayerPrefs.Save();
            state = null;
            state = Load() ?? NewGame();
            Debug.Log("[LifeSim] carried on from this build's own last copy (day " + state.day + ")");
            return true;
        }

        /// <summary>The title's other answer: keep the older build's career
        /// (its dropped fields already seeded again by the load), and save it
        /// now so the question is not asked again.</summary>
        public static void KeepStoredCopy()
        {
            if (Trouble != SaveTrouble.Rewritten) return;
            Trouble = SaveTrouble.None;
            Save();
        }

        /// <summary>
        /// What the title says about the career on disk, or null when the
        /// load found nothing wrong. Plain words: the player does not know
        /// what a save version is.
        /// </summary>
        public static string TroubleTitle()
        {
            switch (Trouble)
            {
                case SaveTrouble.Newer:
                    return "THIS CAREER WAS SAVED BY A NEWER BUILD OF THE GAME. THIS BUILD WILL NOT SAVE OVER IT: " +
                           "WHAT YOU DO HERE IS NOT KEPT.";
                case SaveTrouble.Rewritten:
                    if (LastCopy(out int day, out string when))
                    {
                        string older = state != null ? " OR THE OLDER BUILD'S (" + LifeRules.DateLabel(state.day) + ")" : "";
                        return "AN OLDER BUILD SAVED OVER THIS CAREER. CARRY ON FROM THIS BUILD'S OWN COPY (" +
                               LifeRules.DateLabel(day) + (string.IsNullOrEmpty(when) ? "" : ", SAVED " + when) + ")" +
                               older + ".";
                    }
                    return "AN OLDER BUILD SAVED OVER THIS CAREER. WHAT IT LEFT OUT HAS BEEN SET UP AGAIN.";
                case SaveTrouble.Failed:
                    return "THIS BUILD COULD NOT LOAD YOUR CAREER. IT IS KEPT, UNTOUCHED, AS A BACKUP. " +
                           "NEW GAME STARTS A SEPARATE CAREER.";
            }
            return null;
        }

        static string editionNote;

        /// <summary>The last load's edition note, once — see
        /// <see cref="EditionSanitize"/>. The home screen toasts it. A
        /// newer build's career (read-only) says so here first, once.</summary>
        public static string TakeEditionNote()
        {
            string n = editionNote;
            editionNote = null;
            if (readOnlyNoteDue && ReadOnly)
            {
                readOnlyNoteDue = false;
                return ReadOnlyNote;
            }
            return n;
        }

        /// <summary>The home screen's toast for a read-only career (at most
        /// <see cref="NoteMax"/> characters).</summary>
        public const string ReadOnlyNote = "READ-ONLY: A NEWER BUILD'S SAVE";

        /// <summary>
        /// A SAVE FROM BEFORE THE EDITIONS, loaded by one of them.
        ///
        /// Saves store venues by catalog INDEX, and the catalog is the same in
        /// every edition (nothing is ever deleted — see Edition), so an old
        /// career loads in MAIN pointing at Charlotte venues MAIN has no scene
        /// for: a race booked on Tryon, a blacklist series opened on the 277,
        /// the pre-race page's own default. Left alone, the booked block's RACE
        /// button would load a scene that is not there — a black screen.
        ///
        /// So, on every load (idempotent; nothing to do in ALL):
        ///   * a BOOKING at a venue this build does not carry is CANCELLED, with
        ///     a line in the log naming it. Bookings are free, so nothing is
        ///     owed back; the block is simply open again.
        ///   * a live blacklist SERIES on such a road moves to one of the
        ///     rival's roads that this build has (legs already run stand).
        ///   * the pre-race DEFAULT venue goes back to the first one offered.
        /// The first screen then says so in a toast (<see cref="TakeEditionNote"/>).
        /// Public for the self-test (TestEditions).
        /// </summary>
        public static string EditionSanitize(LifeState s)
        {
            if (s == null || Edition.Current == EditionKind.All) return null;
            var said = new System.Collections.Generic.List<string>();

            if (s.bookings != null)
            {
                for (int i = s.bookings.Count - 1; i >= 0; i--)
                {
                    var b = s.bookings[i];
                    if (b == null || TrackCatalog.Offered(b.trackIndex)) continue;
                    string name = b.trackIndex >= 0 && b.trackIndex < TrackCatalog.Count
                        ? TrackCatalog.At(b.trackIndex).name : "a venue";
                    // The verb FIRST: RECENTLY cuts a long line short, and
                    // "the race booked at TRYON STREET SPRI..." never got
                    // as far as saying what happened to it.
                    s.calendarLog.Add(LifeRules.LogDate(b.day) + ": cancelled, the race at " + name +
                                      " (not in this edition of the game)");
                    s.bookings.RemoveAt(i);
                    said.Add(name);
                }
            }

            string seriesWas = Blacklist.RepointSeries(s);
            if (seriesWas != null)
            {
                string now = TrackCatalog.At(Blacklist.SeriesTrack(s)).name;
                s.calendarLog.Add(LifeRules.LogDate(s.day) + ": series moved to " + now + ", off " +
                                  seriesWas + " (not in this edition of the game)");
            }

            if (!TrackCatalog.Offered(s.trackIndex)) s.trackIndex = TrackCatalog.FirstOffered();

            if (said.Count > 0 || seriesWas != null)
            {
                // ONE SHORT toast line; RECENTLY has the whole story. At most
                // NoteMax characters: the toast is centred over the bottom of
                // the page, and on a 19.5:9 phone a 51-character one ran over
                // the WEEK button's label (menu preview, main_oldsave). Only
                // MAIN ever gets here (CITY has no career to load), and the
                // only venues MAIN leaves out are Charlotte's.
                string what = Edition.Current == EditionKind.Main ? "CHARLOTTE " : "";
                string note =
                    said.Count > 0 && seriesWas != null ? what + "RACES OFF, SERIES MOVED"
                    : said.Count > 1 ? said.Count + " " + what + "RACES CANCELLED"
                    : said.Count == 1 ? what + "RACE CANCELLED"
                    : "SERIES MOVED OFF " + (what.Length > 0 ? what.Trim() : "ITS ROAD");
                if (note.Length > NoteMax) note = Short(note, NoteMax);
                editionNote = note;
                return note;
            }
            return null;
        }

        /// <summary>The longest edition toast (see EditionSanitize).</summary>
        public const int NoteMax = 34;

        static string Short(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max - 3).TrimEnd() + "...";

        /// <summary>
        /// Forward-migrate an older save in place. Added fields need nothing —
        /// JsonUtility leaves them at their initializer defaults — so this only
        /// handles cases where an old value would be WRONG rather than absent.
        /// </summary>
        /// <summary>
        /// A BUILT car (CarSpec.IsBuiltToTune: race, rally, touring) takes no
        /// shop part (2026-10-07, the owner: race and rally cars "can't receive
        /// different parts"). Race cars were stripped at v16; a rally or
        /// touring car could still buy stages, the weld, the blower and a
        /// turbo kit until this build. Every load rather than a save version,
        /// like EditionSanitize: idempotent, a stripped car refunds nothing.
        /// Returns the money handed back. Public for the self-test.
        /// </summary>
        public static int StripBuiltCars(LifeState s)
        {
            if (s == null || s.cars == null) return 0;
            int refunded = 0, cars = 0;
            foreach (var car in s.cars)
            {
                var spec = car != null ? CarCatalog.Get(car.specId) : null;
                if (spec == null || !spec.IsBuiltToTune) continue;
                bool had = car.turbo;
                int back = Upgrades.StripRaceCar(s, car, spec);
                if (back <= 0 && !had) continue;
                refunded += back;
                cars++;
            }
            if (cars > 0 && s.calendarLog != null)
                s.calendarLog.Add(LifeRules.LogDate(s.day) +
                    ": race and rally cars come built — the parts on " + cars +
                    (cars == 1 ? " car" : " cars") + " went back (" + MenuKit.Money(refunded) + ")");
            return refunded;
        }

        /// <summary>Bring an older save forward. PUBLIC rather than private
        /// because the self-test lives in the editor assembly, which cannot
        /// see internals of this one - and the v7 rule is the only thing
        /// between an old career and a fault list it never earned, which makes
        /// it exactly the code that has to be pinned rather than assumed.
        /// Idempotent: it runs on every load.</summary>
        public static void Migrate(LifeState s)
        {
            if (s.saveVersion < 2)
            {
                // v1 wrote synthetic faults ("worn-engine") with no catalog
                // backing: no cost, no repair time, no effect entry. They cannot
                // be quoted or repaired, so retire them and let the next race
                // re-roll a real catalogued fault from the same worn stat.
                foreach (var car in s.cars)
                    car.faults.RemoveAll(f => f.id != null &&
                                              (f.id.StartsWith("worn-") || f.id.StartsWith("severe-")));
                s.saveVersion = 2;
            }
            if (s.saveVersion < 3)
            {
                // v3 added the catalog link. Existing cars were all the built-in
                // RX-7, so give them its MSRP as a value base — leaving
                // catalogPrice at 0 would make every owned car worthless.
                foreach (var car in s.cars)
                {
                    if (car.catalogPrice <= 0) car.catalogPrice = 36000;
                    if (car.specId == null) car.specId = "";
                }
                s.saveVersion = 3;
            }
            if (s.saveVersion < 4)
            {
                // v4 added the blacklist ladder. The lists are ADDED fields, so
                // JsonUtility already left them at their empty initializers — but
                // a save from before the ladder existed has a race history the
                // ladder would read as unclaimed progress. That is the intended
                // reading: an existing career walks into an open bottom rung
                // (or several) rather than having to re-earn wins it already has.
                if (s.blDefeated == null) s.blDefeated = new System.Collections.Generic.List<int>();
                if (s.blPaged == null) s.blPaged = new System.Collections.Generic.List<int>();
                // Rep decay moved off lastRaceDay onto lastAnyRaceDay. Left at 0
                // an existing save reads as "never raced", and the first rollover
                // would decay rep for a 300-day inactivity that never happened.
                if (s.lastAnyRaceDay == 0) s.lastAnyRaceDay = s.lastRaceDay;
                s.saveVersion = 4;
            }

            if (s.saveVersion < 5)
            {
                // v5 added the toolbox and per-car inspection latches. Every
                // field is ADDED, so JsonUtility has already left them at their
                // initializers — except the per-car ones, which JsonUtility
                // fills with 0 rather than the -1 they default to. Day 0 is
                // before the game starts, so a 0 would read as "inspected on
                // day 0" and silently eat the first inspection of every car
                // the player already owns.
                if (s.tools == null) s.tools = new System.Collections.Generic.List<string>();
                foreach (var car in s.cars)
                {
                    if (car.inspectedSubs == null)
                        car.inspectedSubs = new System.Collections.Generic.List<string>();
                    if (car.inspectDay == 0) car.inspectDay = -1;
                    if (car.floorCheckedDay == 0) car.floorCheckedDay = -1;
                }
                s.saveVersion = 5;
            }

            if (s.saveVersion < 6)
            {
                // v6 turned the apartment ladder into houses (the player now
                // starts in the small house the walk-in scene builds). Same
                // rents, same slots — only the KEY and the label change, so an
                // old save is renamed onto the new rung rather than left with
                // a housingType no table row can label.
                s.housingType = s.housingType switch
                {
                    "apt1br" => "house1g",
                    "apt2br" => "house2g",
                    "rentHouse" => "house3g",
                    _ => string.IsNullOrEmpty(s.housingType) ? "house1g" : s.housingType,
                };
                s.saveVersion = 6;
            }

            if (s.saveVersion < 7)
            {
                // v7 is the save-side half of "nothing diagnoses itself". Until
                // 2026-08-28 RollWearFault stamped every wear, threshold and
                // impact fault hidden=false/diagnosed=true, so a career carried
                // over from before that change lists parts by name on the MAIN
                // and GARAGE screens that nobody ever inspected for - reported
                // exactly that way, as fault pop-ups after a race.
                //
                // The save records WHAT was found, never WHO found it, so the
                // discriminator has to be the car's own inspection history: a
                // car nobody has ever had a lamp under is a car nobody has
                // found anything on. Faults on a car the player HAS inspected
                // stay visible, because that reading is at least as likely to
                // be the true one and taking knowledge back is the worse error.
                //
                // BookPro was the one hole - it revealed without stamping the
                // car - so it stamps proInspectDay now. That cannot be
                // recovered for an existing save, and under the old rules it
                // had nothing to reveal anyway: every fault was born visible,
                // so its `if (!f.hidden) continue` skipped the lot. Under-
                // revealing is the safe direction regardless - the fault is
                // still on the car and still slowing it down, and one
                // inspection finds it again.
                foreach (var car in s.cars)
                {
                    if (car == null) continue;
                    // proInspectDay is an ADDED field, so JsonUtility hands it
                    // back as 0 rather than the -1 it initialises to - and 0 is
                    // a day BEFORE the game starts, so left alone it would read
                    // as "a dealer looked at this car" for every car in the
                    // save and re-hide nothing at all. Same trap v5 hit with
                    // inspectDay; normalise before anything reads it.
                    if (car.proInspectDay == 0) car.proInspectDay = -1;
                    if (car.faults == null) continue;
                    bool everLooked = car.inspectDay >= 0 || car.floorCheckedDay >= 0 ||
                                      car.proInspectDay >= 0 ||
                                      (car.inspectedSubs != null && car.inspectedSubs.Count > 0);
                    if (everLooked) continue;
                    foreach (var f in car.faults)
                    {
                        if (f == null) continue;
                        f.hidden = true;
                        f.diagnosed = false;
                    }
                }
                s.saveVersion = 7;
            }

            if (s.saveVersion < 8)
            {
                // v8 closed the job book down to the one job the game is about.
                // A career carried over from the old book is holding a job title
                // that no longer exists in LifeRules.Jobs — and nothing in the
                // game can pay, rate or fire a job that is not in the table, so
                // it would silently become an unpaid title with an attendance
                // ladder still ticking underneath it.
                //
                // Moved across rather than cleared: taking the job away would
                // drop the player onto the JOBS tab with a 55% roll to get any
                // work at all, which is a punishment for a change they did not
                // make. Standing carries too — work rep and attendance are what
                // the player earned, whoever they earned it from. Only the pay
                // rate is rewritten, because a tanker driver's $231 salary
                // against a tip roll is two different economies at once.
                if (!string.IsNullOrEmpty(s.playerJob))
                {
                    bool known = false;
                    foreach (var j in LifeRules.Jobs) if (j.name == s.playerJob) known = true;
                    if (!known)
                    {
                        s.calendarLog.Add(LifeRules.LogDate(s.day) + ": the shop took you on — " +
                                          LifeRules.DeliveryJobName);
                        s.playerJob = LifeRules.DeliveryJobName;
                        s.basePay = LifeRules.DeliveryBasePay;
                    }
                }

                // NOBODY IS FIRED ANY MORE, and a save that was is taken back
                // on. There is one job and it is the player's; the old ladder
                // sacked the owner mid-test and left the pizzeria's counter
                // able to sell and nothing else. Standing is kept — the rep
                // the ladder took is not given back, it simply stops mattering.
                if (string.IsNullOrEmpty(s.playerJob) || s.fired)
                {
                    s.calendarLog.Add(LifeRules.LogDate(s.day) + ": back on the roster at " +
                                      LifeRules.DeliveryJobName + " — nobody gets fired");
                    s.playerJob = LifeRules.DeliveryJobName;
                    s.basePay = LifeRules.DeliveryBasePay;
                    if (s.workRep <= 0f) s.workRep = LifeRules.NewHireWorkRep;
                    s.fired = false;
                    s.consecutiveAbsences = 0;
                }

                // The absence counter meant something different yesterday: it
                // only ever counted weekdays, because the weekend was free. It
                // now counts every day, so a save that came in mid-weekend with
                // two strikes on it would be one rollover from being fired by
                // the rule change alone. Everyone starts the new roster clean.
                s.consecutiveAbsences = 0;
                s.saveVersion = 8;
            }

            if (s.saveVersion < 9)
            {
                // v9 added the salvage yard. The list itself needs nothing —
                // JsonUtility leaves an added field at its initializer — but an
                // EMPTY yard is not the same as a yard nobody has looked at:
                // the shelves only fill on a rollover, so a career that came in
                // from before this would open the advert onto three empty
                // shelves and have to sleep to find out the page works at all.
                // Stock it here so it is a yard the first time it is opened.
                if (s.junkyard == null)
                    s.junkyard = new System.Collections.Generic.List<YardPart>();
                Junkyard.RefreshStock(s);
                s.saveVersion = 9;
            }

            if (s.saveVersion < 10)
            {
                // v10 added the town: dealerLot and viewings are both ADDED
                // fields and need nothing. The lot is stocked here for the same
                // reason the yard was — a forecourt you drive to should have
                // cars on it the first time you arrive, not after a sleep.
                if (s.dealerLot == null)
                    s.dealerLot = new System.Collections.Generic.List<CarListing>();
                if (s.viewings == null)
                    s.viewings = new System.Collections.Generic.List<Viewing>();
                CarMarket.RefreshLot(s);

                // The one thing in this version that is NOT free.
                //
                // ADJUSTABLE AERO became a race-car-only part. A road car in an
                // existing save can therefore be carrying a kit that the shop
                // will no longer sell and the gate will no longer honour: two
                // padlocked rows where two sliders used to be, and money spent
                // on nothing. Unfit it and hand the money back, at the price
                // the part would cost on THAT car — the kit is priced per car,
                // so a flat refund would be wrong in both directions.
                int refunded = 0, cars = 0;
                foreach (var car in s.cars)
                {
                    if (!car.aeroKit) continue;
                    var spec = CarCatalog.Get(car.specId);
                    if (Upgrades.AeroKitAllowed(spec)) continue;
                    car.aeroKit = false;
                    var setup = car.setup;
                    if (setup != null)
                    {
                        setup.Set(SetupParam.AeroLevel, 0f);
                        setup.Set(SetupParam.AeroBalance, 0f);
                    }
                    int back = Upgrades.OfferFor(s, car, spec, Upgrades.Mod.AeroKit).price;
                    s.money += back;
                    refunded += back;
                    cars++;
                }
                if (cars > 0)
                    s.calendarLog.Add(LifeRules.LogDate(s.day) +
                        ": returned the adjustable aero on " + cars +
                        (cars == 1 ? " car" : " cars") + " — race parts only (" +
                        MenuKit.Money(refunded) + " back)");
                s.saveVersion = 10;
            }

            // VENUE INDICES ARE REMAPPED ONCE, from the layout the save was
            // written against straight to the V21 LAYOUT (the list from v20
            // to v21, frozen in TrackCatalog.V21Ids). Each RemapV<n>Index
            // lands there (it counts twins from where the twins start in it),
            // so running v10's remap and then v11's on the same save would
            // move a twin twice: a v10 RIDGE PASS II came out as BLUE RIDGE
            // PARKWAY II that way on 2026-09-11. v22 then carries every
            // stored venue from the V21 layout onto today's list BY ID.
            int written = s.saveVersion;
            if (s.saveVersion < 11)
            {
                // v11 appended three Charlotte venues to the AUTHORED list.
                // Appending is the rule precisely because a save stores its
                // venue by index — but the reverse twins live AFTER the
                // authored list, so three more authored entries move every
                // twin three places along, and a career that remembered
                // RIDGE PASS II now points at UPTOWN LOOP. The twins keep their
                // ORDER (they are generated in authored order and the new
                // venues sit at the end), so a twin's new home is "the same
                // twin, counted from wherever the twins start now".
                s.trackIndex = TrackCatalog.RemapV10Index(s.trackIndex);
                if (s.bookings != null)
                    foreach (var b in s.bookings)
                        if (b != null) b.trackIndex = TrackCatalog.RemapV10Index(b.trackIndex);
                s.saveVersion = 11;
            }

            if (s.saveVersion < 12)
            {
                // v12 appended the two Parkway loops to the authored list,
                // and the twins moved two places along again. Same remap,
                // one layout later -- for a save WRITTEN under v11. A v10
                // save has already landed on today's list above.
                if (written >= 11)
                {
                    s.trackIndex = TrackCatalog.RemapV11Index(s.trackIndex);
                    if (s.bookings != null)
                        foreach (var b in s.bookings)
                            if (b != null) b.trackIndex = TrackCatalog.RemapV11Index(b.trackIndex);
                }
                s.saveVersion = 12;
            }

            if (s.saveVersion < 13)
            {
                // v13 put every booking into a BLOCK of its day. A booking
                // written before blocks existed meant "some time that day",
                // and JsonUtility hands the new field back as 0 — the
                // morning — which would draw a race booked for "Friday" over
                // Friday breakfast and offer the RACE button at dawn. The
                // night is what the old booking meant in practice: it is
                // when the street runs, and it is the block a race made from
                // the old launch screen usually landed in.
                // (Index 2: the night of the THREE-block day this save was
                // written in. v21 below moves it onto the six.)
                if (s.bookings != null)
                    foreach (var b in s.bookings)
                        if (b != null) b.slot = 2;
                // The day's record starts empty rather than absent, so the
                // first sleep can stamp it without growing a list. Three
                // words, for the same reason.
                if (s.slotActs == null) s.slotActs = new System.Collections.Generic.List<string>();
                while (s.slotActs.Count < 3) s.slotActs.Add("");
                if (s.dayLog == null) s.dayLog = new System.Collections.Generic.List<DayRecord>();
                s.saveVersion = 13;
            }

            if (s.saveVersion < 14)
            {
                // v14 PUT THE PLAYER ON THE BOARD. The blacklist used to be ten
                // fixed rungs the career walked past, remembered as a list of
                // ranks beaten; it is an ORDER now, with the player standing in
                // it, and the order is the only place a rank is written down.
                //
                // Where does an existing career stand? Exactly where it had
                // fought to: above every name it had beaten and below every
                // name it had not. So the old list is read once — the highest
                // rank ever taken — and the player is slotted in directly above
                // that name. A career that had beaten nobody starts at the
                // bottom, which is where a new one starts too.
                //
                // Records start empty on purpose. A win-loss column invented
                // out of blDefeated would be a scoreboard for races that were
                // never run on these rules.
                Blacklist.SeedBoard(s);
                int highestTaken = 11;   // nothing taken: stay at the bottom
                if (s.blDefeated != null)
                    foreach (int r in s.blDefeated)
                        if (r >= 1 && r <= 10 && r < highestTaken) highestTaken = r;
                if (highestTaken <= 10)
                {
                    // Move the player's row from the bottom to just above the
                    // best name they had taken — the board is still in start
                    // order here, so that name is standing on its own rank.
                    var board = s.blBoard;
                    var me = board[board.Count - 1];
                    board.RemoveAt(board.Count - 1);
                    board.Insert(highestTaken - 1, me);
                    s.calendarLog.Add(LifeRules.LogDate(s.day) +
                        ": the board reshuffled — you are #" + highestTaken +
                        ", and the names below you want it back");
                }
                s.blChallenge = new RankChallenge();
                if (s.blNews == null) s.blNews = new System.Collections.Generic.List<string>();
                // A career mid-ladder should not be called out on its first
                // morning back: give it the usual quiet gap first.
                s.blIncomingReadyDay = s.day + Blacklist.IncomingGapDays;
                s.saveVersion = 14;
            }

            if (s.saveVersion < 15)
            {
                // v15 GAVE EVERY CAR A COOLING SYSTEM — a radiator, a fan, a set
                // of hoses and something in them (see CoolingModel), because the
                // temperature gauge now reads a real one.
                //
                // The ADDED fields come back as 100, which would hand a car with
                // a 12% engine and 180,000 miles on it a radiator off the
                // showroom floor. So they are SEEDED from what the save does
                // know: the engine lane, which is the one number that already
                // says how hard this car's life has been, and the cooling_fail
                // fault, which is literally the catalog's "Radiator & Hoses"
                // row and has to mean something about the parts it names.
                //
                // Through CoolingModel.Seed, which is the same call the
                // classifieds, a seller's driveway and the starting lane make.
                // One definition of "what cooling system does this car have,
                // given the life it has had" — a migration with its own copy is
                // a migration that drifts from the game the day either moves.
                foreach (var c in s.cars) CoolingModel.Seed(c, c != null ? c.engine : 100f);
                s.saveVersion = 15;
            }

            if (s.saveVersion < 16)
            {
                // v16: A RACE CAR IS ALREADY BUILT (the owner, 2026-09-21:
                // "Race cars are assumed to already be maxed out by default so
                // they don't get upgrades"). The shop used to sell them stages
                // and parts at a 1.5x premium; the physics now ignores those
                // stages and gives the car race hardware instead, so anything a
                // career bought for one is money spent on nothing. Take it off
                // and hand the money back, stage by stage at this car's own
                // prices — the same shape as v10's aero refund. The driver's
                // tune is left as it is: every row of it is open on a race car
                // now, so none of it is lost.
                int refunded = 0, cars = 0;
                foreach (var car in s.cars)
                {
                    var spec = car != null ? CarCatalog.Get(car.specId) : null;
                    if (spec == null || !spec.IsRaceCar) continue;
                    int back = Upgrades.StripRaceCar(s, car, spec);
                    if (back <= 0) continue;
                    refunded += back;
                    cars++;
                }
                if (cars > 0)
                    s.calendarLog.Add(LifeRules.LogDate(s.day) +
                        ": race cars come built — the parts on " + cars +
                        (cars == 1 ? " race car" : " race cars") + " went back (" +
                        MenuKit.Money(refunded) + ")");
                s.saveVersion = 16;
            }

            if (s.saveVersion < 17)
            {
                // v17: TWO POWER PATHS (the owner, 2026-09-25: "cars need
                // options for turbos ... an alternate power path than NA
                // builds"). The old single ladder was a turbo build in all but
                // name - "ECU + BOOST", "TURBO / CAMS", up to the engine's
                // turbo ceiling - so an NA car that bought stages on it is put
                // on the TURBO path and keeps every horsepower it paid for.
                // A car with the blower was building NA: it stays NA, whose
                // ceiling is lower, and the difference comes back as money at
                // the price the shop charged for it.
                int moved = 0, refunded = 0;
                foreach (var car in s.cars)
                {
                    var spec = car != null ? CarCatalog.Get(car.specId) : null;
                    if (spec == null || !spec.CanFitTurboKit) continue;
                    bool pendingPower = s.pendingParts.Exists(p => p.carId == car.id && p.upgradeKind == "power");
                    if (car.upPower <= 0 && !pendingPower) continue;
                    if (!car.supercharged) { car.turbo = true; moved++; continue; }
                    int lost = spec.HpAtStage(car.upPower, true) - spec.HpAtStage(car.upPower, false);
                    if (lost > 0) { int back = lost * Upgrades.PerHp; s.money += back; refunded += back; }
                }
                if (moved > 0)
                    s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + moved +
                        (moved == 1 ? " power build is" : " power builds are") + " TURBO builds now");
                if (refunded > 0)
                    s.calendarLog.Add(LifeRules.LogDate(s.day) + ": blown NA builds top out lower — " +
                        MenuKit.Money(refunded) + " back for the difference");
                s.saveVersion = 17;
            }

            if (s.saveVersion < 18)
            {
                // v18 appended NC 226A (down from Little Switzerland) to the
                // authored list and every twin moved one place along. (The
                // Blowing Rock Parkway sprint races on the loop's own scene
                // and is appended after the twins, so it moves nothing.) Only for a save WRITTEN
                // under v12..v17: older ones were remapped onto today's list
                // above. Every stored venue: the last one picked, the diary's
                // bookings, and a blacklist series' road.
                if (written >= 12)
                {
                    s.trackIndex = TrackCatalog.RemapV17Index(s.trackIndex);
                    if (s.bookings != null)
                        foreach (var b in s.bookings)
                            if (b != null) b.trackIndex = TrackCatalog.RemapV17Index(b.trackIndex);
                    if (s.blChallenge != null && s.blChallenge.trackIndex >= 0)
                        s.blChallenge.trackIndex = TrackCatalog.RemapV17Index(s.blChallenge.trackIndex);
                }
                s.saveVersion = 18;
            }

            if (s.saveVersion < 19)
            {
                // v19 appended Gillespie Gap (NC 226) to the authored list: its
                // twin went in after the other twins and every sprint section
                // moved two places. Only for a save WRITTEN under v18; older ones
                // were mapped straight onto today's list above.
                if (written >= 18)
                {
                    s.trackIndex = TrackCatalog.RemapV18Index(s.trackIndex);
                    if (s.bookings != null)
                        foreach (var b in s.bookings)
                            if (b != null) b.trackIndex = TrackCatalog.RemapV18Index(b.trackIndex);
                    if (s.blChallenge != null && s.blChallenge.trackIndex >= 0)
                        s.blChallenge.trackIndex = TrackCatalog.RemapV18Index(s.blChallenge.trackIndex);
                }
                s.saveVersion = 19;
            }

            if (s.saveVersion < 20)
            {
                // v20 appended Chimney Rock's park road to the authored list
                // after Gillespie Gap: its twin went in at the end of the
                // twins, Gillespie's twin moved one place and every sprint
                // section two. Only for a save WRITTEN under v19; older ones
                // were mapped straight onto today's list above (RemapV18Index
                // leaves Chimney Rock out of the v18 list for that reason).
                // The three venue indices a save stores: the last one picked,
                // the diary's bookings, and a blacklist series' road.
                if (written >= 19)
                {
                    s.trackIndex = TrackCatalog.RemapV19Index(s.trackIndex);
                    if (s.bookings != null)
                        foreach (var b in s.bookings)
                            if (b != null) b.trackIndex = TrackCatalog.RemapV19Index(b.trackIndex);
                    if (s.blChallenge != null && s.blChallenge.trackIndex >= 0)
                        s.blChallenge.trackIndex = TrackCatalog.RemapV19Index(s.blChallenge.trackIndex);
                }
                s.saveVersion = 20;
            }

            if (s.saveVersion < 21)
            {
                MigrateToSixBlocks(s);
                s.saveVersion = 21;
            }

            if (s.saveVersion < 22)
            {
                // v22 REMOVED THE FICTIONAL VENUES (owner, 2026-10-25: "Current
                // fictional circuits to be removed from the game. New ones may
                // be made in the future."). Every save, whatever it was written
                // at, holds V21-layout indices by now (the remaps above), so
                // this one step reads them all across by id.
                RetireFictionalVenues(s);
                s.saveVersion = 22;
            }

            if (s.saveVersion < 23) MigrateToEnforcedWeek(s);
            if (s.saveVersion < 24) MigrateToPartsShelf(s);
        }

        /// <summary>
        /// v24, THE PARTS SHELF: a pulled part became a thing you own (see
        /// Spares). Nothing an old career did is re-read: every part it ever
        /// pulled was fitted the day it was paid for, so the shelf starts
        /// empty. The fields are ADDED; this only makes sure the list is a
        /// list. Public for the self-test.
        /// </summary>
        public static void MigrateToPartsShelf(LifeState s)
        {
            if (s == null) return;
            if (s.partsShelf == null) s.partsShelf = new System.Collections.Generic.List<SparePart>();
            s.partsShelf.RemoveAll(p => p == null);
            if (s.spareSeq < s.partsShelf.Count) s.spareSeq = s.partsShelf.Count;
            s.saveVersion = 24;
        }

        /// <summary>
        /// v23, THE ENFORCED WEEK: the job onto its schedule, no club, and
        /// NOTHING missed retroactively - obligations are owed from the block
        /// the save is standing in. The fields are all ADDED, so JsonUtility
        /// has already left them at their defaults; this writes the clock in
        /// and makes the nested ones non-null (the empty-alias pattern: no
        /// club is "", never a null object). Public for the self-test.
        /// </summary>
        public static void MigrateToEnforcedWeek(LifeState s)
        {
            if (s == null) return;
            int now = Duty.NowOrd(s);
            s.dutyFrom = now;
            s.jobFrom = now;
            if (s.dutyLog == null) s.dutyLog = new System.Collections.Generic.List<DutyRecord>();
            s.dutyLog.Clear();
            s.laidOff = false;
            s.callBackOffered = false;
            if (s.clubs == null) s.clubs = new System.Collections.Generic.List<ClubStanding>();
            s.clubs.Clear();
            if (s.clubNews == null) s.clubNews = new System.Collections.Generic.List<string>();
            // The old absence ladder is retired for a scheduled job; its
            // counter means nothing now.
            s.consecutiveAbsences = 0;
            if (!string.IsNullOrEmpty(s.playerJob) && WorkSchedule.HasSchedule(s.playerJob))
            {
                if (s.calendarLog == null) s.calendarLog = new System.Collections.Generic.List<string>();
                s.calendarLog.Add(LifeRules.LogDate(s.day) + ": Tony's put you on a schedule - " +
                                  WorkSchedule.RosterShort(s.playerJob).ToLowerInvariant());
            }
            s.saveVersion = 23;
        }

        /// <summary>
        /// v22: the six fictional venues (Sunset City GP, Harbor Point, Ridge
        /// Pass, Airfield Sprint, the 1/4 and 1/8 mile strips) and their four
        /// twins left the catalog, and every index after them moved. The three
        /// venues a save stores are read from the V21 layout by id
        /// (<see cref="TrackCatalog.RemapV21Index"/>), and one on a venue that
        /// is gone is dealt with the way the editions deal with a venue a
        /// build lacks:
        ///  - a BOOKING there is CANCELLED, with a diary line naming it (a
        ///    booking is free, so nothing is owed back; the block is open);
        ///  - a blacklist SERIES there moves to one of the rival's own roads
        ///    (<see cref="Blacklist.RehomeSeries"/>; legs already run stand),
        ///    and a dead series' road is simply "none";
        ///  - the pre-race page's own venue goes to the first one offered.
        /// The toast says so once. Best times are kept per deck (by way, not
        /// by index) and no deck moved, so nothing else is stored by venue.
        /// Public for the self-test, which pins it.
        /// </summary>
        public static void RetireFictionalVenues(LifeState s)
        {
            if (s == null) return;
            int cancelled = 0;
            if (s.bookings != null)
            {
                for (int i = s.bookings.Count - 1; i >= 0; i--)
                {
                    var b = s.bookings[i];
                    if (b == null) continue;
                    int now = TrackCatalog.RemapV21Index(b.trackIndex);
                    if (now >= 0) { b.trackIndex = now; continue; }
                    if (s.calendarLog == null) s.calendarLog = new System.Collections.Generic.List<string>();
                    s.calendarLog.Add(LifeRules.LogDate(b.day) + ": cancelled, the race at " +
                                      TrackCatalog.V21NameAt(b.trackIndex) + " (the circuit has closed)");
                    s.bookings.RemoveAt(i);
                    cancelled++;
                }
            }

            bool moved = false;
            var ch = s.blChallenge;
            if (ch != null && ch.trackIndex >= 0)
            {
                int was = ch.trackIndex;
                int now = TrackCatalog.RemapV21Index(was);
                if (now >= 0) ch.trackIndex = now;
                else if (ch.Live)
                {
                    Blacklist.RehomeSeries(s);
                    if (s.calendarLog == null) s.calendarLog = new System.Collections.Generic.List<string>();
                    s.calendarLog.Add(LifeRules.LogDate(s.day) + ": series moved to " +
                                      TrackCatalog.At(ch.trackIndex).name + ", off " +
                                      TrackCatalog.V21NameAt(was) + " (the circuit has closed)");
                    moved = true;
                }
                else ch.trackIndex = -1;
            }

            int own = TrackCatalog.RemapV21Index(s.trackIndex);
            s.trackIndex = own >= 0 && TrackCatalog.Offered(own) ? own : TrackCatalog.FirstOffered();

            if (cancelled > 0 || moved)
            {
                string note = cancelled > 0 && moved ? "RACES OFF, SERIES MOVED"
                            : cancelled > 1 ? cancelled + " RACES CANCELLED"
                            : cancelled == 1 ? "RACE CANCELLED" : "SERIES MOVED";
                editionNote = note.Length > NoteMax ? Short(note, NoteMax) : note;
            }
        }

        /// <summary>
        /// v21: the three-block day (MORNING 4-12, DAY 12-20, NIGHT 20-4)
        /// becomes six (DAWN, MORNING, NOON, AFTERNOON, EVENING, NIGHT).
        /// Each old block is two new ones, in order, so:
        ///  - the clock moves to the FIRST of its two (old k -> new 2k): a
        ///    career standing in the old NIGHT is at EVENING, with the whole
        ///    of the night still ahead of it — nothing it had is lost;
        ///  - today's record and the day log spread each old word over both
        ///    of its blocks, so a worked DAY reads as a worked NOON and
        ///    AFTERNOON and nothing turns into SHIFT SKIPPED;
        ///  - a booking moves to the block that holds the HOUR it was going
        ///    to run at (its chosen hour, or the old block's own pick for its
        ///    day) and the hour is written in, so the race runs under the sky
        ///    it was booked for. Still one race a day: each moves within its
        ///    own day, so nothing can be double-booked;
        ///  - a job's ready block: the old MORNING (what every shop promises,
        ///    "first thing") is MORNING, when the shops open; DAY is NOON;
        ///    NIGHT is EVENING, its first block.
        /// Public for the self-test, which pins it.
        /// </summary>
        public static void MigrateToSixBlocks(LifeState s)
        {
            if (s == null) return;
            int oldSlot = Mathf.Clamp(s.slotIndex, 0, 2);
            var oldActs = s.slotActs ?? new System.Collections.Generic.List<string>();
            s.slotIndex = oldSlot * 2;
            s.slotActs = new System.Collections.Generic.List<string>();
            for (int k = 0; k < LifeRules.SlotCount; k++)
            {
                int ok = k / 2;
                s.slotActs.Add(k < s.slotIndex && ok < oldActs.Count ? oldActs[ok] ?? "" : "");
            }

            if (s.dayLog != null)
                foreach (var r in s.dayLog)
                {
                    if (r == null) continue;
                    r.acts = new System.Collections.Generic.List<string>
                        { r.morning ?? "", r.morning ?? "", r.afternoon ?? "", r.afternoon ?? "",
                          r.night ?? "", r.night ?? "" };
                    r.morning = r.afternoon = r.night = "";
                }

            if (s.bookings != null)
                foreach (var b in s.bookings)
                {
                    if (b == null) continue;
                    int hour = OldBookingHour(b);
                    b.slot = LifeRules.SlotOfHour(hour);
                    b.hourPick = hour + 1;
                }

            if (s.pendingParts != null)
                foreach (var p in s.pendingParts)
                    if (p != null)
                    {
                        int r = Mathf.Clamp(p.readySlot, 0, 2);
                        p.readySlot = r == 0 ? LifeRules.MorningSlot : r == 1 ? LifeRules.NoonSlot
                                                                      : LifeRules.EveningSlot;
                    }
        }

        /// <summary>The hour a booking ran at under the three-block rules
        /// (TimeOfDay as it stood at v20): its chosen hour when that sat in
        /// its block, else the block's own pick for the day. Frozen here
        /// because TimeOfDay itself now answers for six blocks.</summary>
        static int OldBookingHour(RaceBooking b)
        {
            int slot = Mathf.Clamp(b.slot, 0, 2);
            int[][] hoursIn =
            {
                new[] { TimeOfDay.Dawn, TimeOfDay.Morning },
                new[] { TimeOfDay.Noon, TimeOfDay.Afternoon, TimeOfDay.Sunset },
                new[] { TimeOfDay.Dusk, TimeOfDay.Night },
            };
            int[][] bands =
            {
                new[] { TimeOfDay.Morning, TimeOfDay.Dawn, TimeOfDay.Morning },
                new[] { TimeOfDay.Noon, TimeOfDay.Afternoon, TimeOfDay.Sunset },
                new[] { TimeOfDay.Night },
            };
            int picked = b.hourPick - 1;
            if (System.Array.IndexOf(hoursIn[slot], picked) >= 0) return picked;
            var band = bands[slot];
            return band[Mathf.Abs(b.day * 7 + slot * 3) % band.Length];
        }

        /// <summary>An explicit NEW GAME (the title's, START OVER) erases the
        /// career. One this build could not vouch for - a newer build's, or
        /// one it could not load - keeps a copy first (save safety).</summary>
        public static void DeleteSave()
        {
            ReleaseForNewGame();
            PlayerPrefs.DeleteKey(saveKey);
            PlayerPrefs.Save();
            state = null;
        }

        /// <summary>
        /// The one door past the save guard: an explicit NEW GAME. A newer
        /// build's career is copied into the backups first, and a failed
        /// load's raw string was already kept by the load - so nothing this
        /// build could not vouch for is ever lost to the new career.
        /// </summary>
        static void ReleaseForNewGame()
        {
            if (Guard && PlayerPrefs.HasKey(saveKey))
            {
                string raw = PlayerPrefs.GetString(saveKey);
                int v = PeekVersion(raw);
                if (readOnly || v > CodeVersion) KeepCopy(raw, v, BakTag, BakIndex, BackupKeep);
                else if (holdWrites && FailedCopyKey == null) KeepCopy(raw, v, FailTag, FailIndex, FailedKeep);
            }
            ResetGuard();
        }

        /// <summary>Fresh anonymous state — the home screen sees the empty
        /// player name and routes into the new-game wizard.</summary>
        public static LifeState NewGame()
        {
            state = new LifeState();
            return state;   // deliberately unsaved: the wizard commits
        }

        /// <summary>Commit the wizard: named character, chosen job, rolled
        /// starting savings (applyStartingConditions bands). The explicit
        /// NEW GAME: the only thing that writes over a career the save guard
        /// was holding (see <see cref="ReleaseForNewGame"/>).</summary>
        public static void StartNewGame(string name, int age, int jobIdx)
        {
            ReleaseForNewGame();
            state = LifeRules.SeedNewGame(name, age, jobIdx);
            Save();
        }
    }
}
