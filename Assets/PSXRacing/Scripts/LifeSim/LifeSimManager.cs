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
        const string SaveKey = "psxRacingLifeSave";

        static LifeState state;

        /// <summary>The live state. Loads (or creates) lazily so any scene can
        /// run first in the editor without a boot ceremony.</summary>
        public static LifeState State
        {
            get
            {
                if (state == null) state = Load() ?? NewGame();
                return state;
            }
        }

        public static bool HasSave => PlayerPrefs.HasKey(SaveKey);

        public static void Save()
        {
            if (state == null) return;
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(state));
            PlayerPrefs.Save();
        }

        public static LifeState Load()
        {
            if (!PlayerPrefs.HasKey(SaveKey)) return null;
            try
            {
                var s = JsonUtility.FromJson<LifeState>(PlayerPrefs.GetString(SaveKey));
                return s != null && s.saveVersion >= 1 ? s : null;
            }
            catch { return null; }   // corrupt save: fall through to new game
        }

        public static void DeleteSave()
        {
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
            state = null;
        }

        /// <summary>Fresh anonymous state — the home screen sees the empty
        /// player name and routes into the new-game wizard.</summary>
        public static LifeState NewGame()
        {
            state = new LifeState();
            return state;   // deliberately unsaved: the wizard commits
        }

        /// <summary>Commit the wizard: named character, chosen job, rolled
        /// starting savings (applyStartingConditions bands).</summary>
        public static void StartNewGame(string name, int age, int jobIdx)
        {
            state = LifeRules.SeedNewGame(name, age, jobIdx);
            Save();
        }
    }
}
