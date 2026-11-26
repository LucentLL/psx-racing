using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE LAB PAGE: SETTINGS > LAB opens it over the settings menu (as CONTROLS > WHEEL SETUP opens its own),
    /// so it is wherever the settings are - the pause menu, MAIN's OPTIONS and CITY's OPTIONS - and a switch that
    /// shows in the menus can be flipped before the car pick. One tab per group, one row per decision
    /// (<see cref="LabSwitches"/>):
    ///   name      NEW / OLD (ON / OFF for an option) and, after it, DEFAULT, CHANGED, NEXT LOAD or LOCKED;
    ///   help line WHEN it shows (AT ONCE / NEXT LOAD / BUILT IN), what to look at, and the variable small.
    /// Every tab ends with RESET ALL TO DEFAULT; in a drive that can be restarted, with a row waiting for a load,
    /// RELOAD (pressed twice: it restarts the drive).
    /// </summary>
    public static class LabCatalog
    {
        public static readonly string[] TabNames = { "RAIN", "LIGHTS", "WORLD", "CARS", "TRAFFIC", "TAPES" };
        public static readonly string[] TabBlurbs =
        {
            "The rain: on the road, the glass and the cars, and the spray.",
            "Street lamps, their pools, car lamps and the night.",
            "Haze, tunnels, the fall, the race map.",
            "The cars' paint in shade and in the rain.",
            "How traffic and rivals behave.",
            "Camcorder replays, their hands and sound, and the tape shelf.",
        };

        public static readonly SettingsPages Pages = new SettingsPages
        {
            title = "LAB",
            tabNames = TabNames,
            tabBlurbs = TabBlurbs,
            items = (h, t, c) => Items(t),
            sortOrder = SettingsPanel.SortOrder + 2,
            hint = "Q / E  ·  LB / RB  PAGES     ESC  BACK",
            onClose = () => reloadArmedAt = -1f,
        };

        /// <summary>SETTINGS > LAB: over whichever settings page is open.</summary>
        public static void OpenFromSettings()
        {
            foreach (var p in Object.FindObjectsByType<SettingsPanel>(FindObjectsInactive.Exclude))
                if (p != null && p.IsOpen && p.pages == null) { p.OpenSub(Pages, 0); return; }
        }

        /// <summary>The SETTINGS row's value: how many rows are off their default.</summary>
        public static string Summary()
        {
            int n = LabSwitches.ChangedCount;
            return n == 0 ? "ALL DEFAULT" : n + " CHANGED";
        }

        static string When(LabWhen w) => w == LabWhen.Now ? "AT ONCE" : w == LabWhen.NextLoad ? "NEXT LOAD" : "BUILT IN";

        /// <summary>The value at the right of a row.</summary>
        public static string Value(LabRow r)
        {
            if (r.when == LabWhen.BuiltIn) return "BUILT IN";
            string s = r.StateWord(r.Chosen);
            if (r.EnvHeld) return s + " · LOCKED";
            if (LabSwitches.Pending(r)) return s + " · NEXT LOAD";
            return s + (r.Chosen == r.Default ? " · DEFAULT" : " · CHANGED");
        }

        /// <summary>The help line: when it shows, what to look at, the variable(s) small.</summary>
        public static string Help(LabRow r)
        {
            string vars = r.env.Length == 0 ? "" : r.env.Length == 1 ? r.env[0] : r.env[0] + " +" + (r.env.Length - 1);
            string lead = r.EnvHeld ? "SET BY THE LAUNCH FILE (" + vars + "). " : When(r.when) + ". ";
            string def = r.when == LabWhen.BuiltIn ? "" : " Default " + r.StateWord(r.Default) + ".";
            return lead + r.look + def + (r.EnvHeld || vars.Length == 0 ? "" : "  " + vars);
        }

        static float reloadArmedAt = -1f;

        public static List<SettingItem> Items(int tab)
        {
            var list = new List<SettingItem>();
            var g = (LabGroup)Mathf.Clamp(tab, 0, TabNames.Length - 1);
            foreach (var row in LabSwitches.Rows(g))
            {
                var r = row;
                list.Add(new SettingItem
                {
                    tab = SettingsTab.System, name = r.name,
                    value = () => Value(r), blurb = Help(r),
                    apply = r.when == LabWhen.BuiltIn || r.EnvHeld ? (System.Action)null : () => LabSwitches.Flip(r),
                    dim = r.when == LabWhen.BuiltIn || r.EnvHeld,
                });
            }
            // RELOAD: only where the game restarts a drive itself (the pause menu's RESTART), and only on a second
            // press - it throws the drive away.
            var pause = Object.FindAnyObjectByType<PauseMenu>();
            if (pause != null && CareerDrive.RestartOffered)
                list.Add(new SettingItem
                {
                    tab = SettingsTab.System, name = "RELOAD",
                    value = () => !LabSwitches.AnyPending ? "NOTHING WAITING"
                                  : Armed ? "PRESS AGAIN" : "RELOAD DRIVE",
                    blurb = "Rows marked NEXT LOAD show after a load. This restarts the drive now (press twice).",
                    apply = () =>
                    {
                        if (!LabSwitches.AnyPending) return;
                        if (!Armed) { reloadArmedAt = Time.unscaledTime; return; }
                        reloadArmedAt = -1f;
                        if (pause != null) pause.RestartForLab();
                    },
                });
            list.Add(new SettingItem
            {
                tab = SettingsTab.System, name = "RESET ALL TO DEFAULT",
                value = () => LabSwitches.ChangedCount == 0 ? "ALL DEFAULT" : LabSwitches.ChangedCount + " CHANGED",
                blurb = "Every row on every tab back to its default. Rows set by a launch file stay as it says.",
                apply = () => LabSwitches.ResetAll(!LabSwitches.TestRun && !PerfTour.Active),
            });
            return list;
        }

        static bool Armed => reloadArmedAt >= 0f && Time.unscaledTime - reloadArmedAt < 4f;
    }
}
