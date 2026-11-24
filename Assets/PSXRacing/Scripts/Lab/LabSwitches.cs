using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PSXRacing
{
    /// <summary>When a LAB row's flip shows: on the next frame, on the next level load, or never at run time
    /// (baked into the scenes or the city tiles by the build).</summary>
    public enum LabWhen { Now, NextLoad, BuiltIn }

    /// <summary>The LAB page's tabs, in strip order.</summary>
    public enum LabGroup { Rain, Lights, World, Cars, Traffic }

    /// <summary>
    /// One DECISION the owner has to make (not one environment variable): a plain name, one line saying what to
    /// look at, and the statics it writes. NEW = every member at its <see cref="newValues"/> value (true unless
    /// said), OLD = the opposite. The game's own code keeps reading its own static; the lab only writes it.
    /// </summary>
    public sealed class LabRow
    {
        public string id;
        public LabGroup group;
        public string name;
        /// <summary>What to look at, one short line (the page's help line).</summary>
        public string look;
        public LabWhen when;
        /// <summary>The variables: one set in the environment holds the row (the test tools depend on it).</summary>
        public string[] env = new string[0];
        /// <summary>"Type.Member" (namespace PSXRacing or PSXRacing.City may be left off): static bool fields or
        /// writable properties. A member that does not exist hides the row (a switch from an unmerged branch).</summary>
        public string[] members = new string[0];
        /// <summary>Per member, its value for NEW (null: true for all).</summary>
        public bool[] newValues;
        /// <summary>The default state, true = NEW (or ON). Null: NEW.</summary>
        public Func<bool> def;
        /// <summary>ON / OFF wording (a player option such as the tunnel lamps) instead of NEW / OLD.</summary>
        public bool option;
        /// <summary>Hidden off the PC profile (the feature is a PC one).</summary>
        public bool pcOnly;
        /// <summary>For a BUILT IN row with no members: a member that must exist for the row to show.</summary>
        public string requires;
        /// <summary>Run after the members are written (a shader global push, a material re-mark...).</summary>
        public Action apply;

        // ---- resolved at boot --------------------------------------------
        internal MemberInfo[] resolved;
        internal bool present;
        internal bool envHeld;
        /// <summary>What the page shows and what is saved: live for a NOW row, pending for a NEXT LOAD row.</summary>
        internal bool chosen;

        public bool Default => def == null || def();
        public bool Chosen => chosen;
        public bool EnvHeld => envHeld;
        public bool Present => present;
        public bool Changed => present && when != LabWhen.BuiltIn && chosen != Default;
        public string StateWord(bool v) => option ? (v ? "ON" : "OFF") : (v ? "NEW" : "OLD");
        public bool NewValue(int i) => newValues == null || i >= newValues.Length || newValues[i];
    }

    /// <summary>
    /// THE LAB (owner, 2026-11-23, PC64, over the build folder's eight PSXRacing_*.cmd files: "I really don't
    /// like all these being individual builds that I have to load up separately to test each. Why can't they be
    /// toggles in the main build and I can approve to keep as option or make default?").
    ///
    /// Every rendering / behaviour switch that was a launcher is a row here (<see cref="Register"/>), flipped
    /// in the settings menu's LAB page (<see cref="LabCatalog"/>), live where that is honest, remembered in
    /// PlayerPrefs (psx.lab.&lt;id&gt;).
    ///
    /// PRECEDENCE: a set environment variable wins (the tools set them), then the saved choice, then the default.
    /// TEST RUNS (a perf tour, -batchmode, the editor, -titleshots, PSX_LAB_IGNORE=1) neither read nor write the
    /// saved choices: a Windows player's PlayerPrefs are the owner's, and every measurement would silently follow
    /// his toggles. COST: nothing per frame - the switches stay plain statics read by their own code; this class
    /// runs at boot, on a flip and at a level load.
    ///
    /// RETIRING A ROW (his verdict per row):
    ///  - MAKE IT THE DEFAULT and delete the old path: delete the row's Register line, then the old branch of the
    ///    code and the switch (keep the environment variable only if a tool still sets it).
    ///  - KEEP IT AS AN OPTION: move the row to a normal settings tab as a Step row (SettingsCatalog) over a
    ///    *Prefs class, and delete its Register line here (its saved key can be read once as the option's first
    ///    value: <see cref="SavedChoice"/>).
    ///  - DROP THE NEW PATH: set the row's default to OLD for one build if wanted, then delete the row and the new
    ///    branch of the code.
    /// A retired row's saved key is simply never read again.
    /// </summary>
    public static class LabSwitches
    {
        /// <summary>The saved keys' prefix (the self-test uses its own).</summary>
        public static string KeyPrefix = "psx.lab.";
        static readonly List<LabRow> all = new List<LabRow>();
        static bool booted;

        /// <summary>A test run: saved choices are neither read nor written.</summary>
        public static bool TestRun { get; private set; }

        /// <summary>"LAB: NIGHT 3 old, FAR HAZE old" while any row is off its default; null when none.</summary>
        public static string ChangedLine { get; private set; }
        /// <summary>Bumped on every change (the pause screen re-reads the line by it).</summary>
        public static int Version { get; private set; }

        // =====================================================================
        //  THE ROWS. One Register call per decision; a row is retired by deleting its line.
        // =====================================================================
        static void RegisterAll()
        {
            all.Clear();
            // ---- RAIN -----------------------------------------------------------
            Register(new LabRow
            {
                id = "rain2", group = LabGroup.Rain, name = "RAIN 2", when = LabWhen.NextLoad, pcOnly = true,
                look = "Pools, rings and splashes on the road, wipers, tyre spray, the rain's sound.",
                env = new[] { "PSX_RAIN2" }, members = new[] { "Rain2.On" },
                def = () => PlatformProfile.PC,
            });
            Register(new LabRow
            {
                id = "rainglass1", group = LabGroup.Rain, name = "RAIN ON GLASS: ROUND 1", when = LabWhen.Now, pcOnly = true,
                look = "Cockpit: the beads and the wipe. Chase: rain on your car, finer streaks in the air.",
                env = new[] { "PSX_RAIN2_STREAKS", "PSX_RAIN2_BEADS2", "PSX_RAIN2_WIPERMAP", "PSX_RAIN2_CARGLASS" },
                members = new[] { "Rain2.Streaks", "Rain2.Beads2", "Rain2.WiperMap", "Rain2.CarGlass" },
                apply = () => Rain2.PushGlass2(),
            });
            Register(new LabRow
            {
                id = "rainglass2", group = LabGroup.Rain, name = "RAIN ON GLASS: ROUND 2", when = LabWhen.Now, pcOnly = true,
                look = "Cockpit: water pushed by the wiper, a covered screen parked. Chase: smaller lens drops.",
                env = new[] { "PSX_RAIN2_BANK2", "PSX_RAIN2_COVER", "PSX_RAIN2_CLEAR", "PSX_RAIN2_LENSDROPS", "PSX_RAIN2_CARBEADS" },
                members = new[] { "Rain2.Bank2", "Rain2.Cover", "Rain2.Clear", "Rain2.LensDrops", "Rain2.CarBeads" },
                apply = () => Rain2.PushGlass2(),
            });
            Register(new LabRow
            {
                id = "spray2", group = LabGroup.Rain, name = "SPRAY FROM TYRES", when = LabWhen.NextLoad, pcOnly = true,
                look = "Rain spray from the rear tyres, low and tyre-sized, not out of the tail lamps.",
                env = new[] { "PSX_SPRAY2" }, members = new[] { "TireSpray.Spray2" },
                // the traffic's shared mist lives for the session: built again with the new look
                apply = () => Rain2Traffic.Discard(),
            });
            // ---- LIGHTS ---------------------------------------------------------
            Register(new LabRow
            {
                id = "lightlook", group = LabGroup.Lights, name = "LIGHT LOOK", when = LabWhen.Now,
                look = "Lamp heads as points to the horizon, far road pools, car lamps far off, the glow on wet glass.",
                env = new[] { "PSX_LIGHT_LOOK", "PSX_RAIN2_LAMPGLOW" }, members = new[] { "LightLook.On", "Rain2.LampGlow" },
                apply = () => { LightLook.CarPointsAtStart = LightLook.On && LightLook.CarPoints; Rain2.PushGlass2(); },
            });
            Register(new LabRow
            {
                id = "nopop", group = LabGroup.Lights, name = "LIGHTS: NO POP-IN", when = LabWhen.Now,
                look = "Street lamps and their pools fade in as you drive up; none switch on in front of you.",
                env = new[] { "PSX_LIGHT_NOPOP" }, members = new[] { "StreetLights.NoPop" },
            });
            Register(new LabRow
            {
                id = "night3", group = LabGroup.Lights, name = "NIGHT 3", when = LabWhen.Now,
                look = "The city at night: how dark it is between lamps, the lamp pools, the sky glow.",
                env = new[] { "PSX_NIGHT3" }, members = new[] { "StreetLights.Night3" },
            });
            // NIGHT 4 (city-night4): flips live - the static field is gathered and splatted again (each lamp's
            // view is kept, found once), the sky, the albedo cap and the car reach follow the static every frame
            Register(new LabRow
            {
                id = "night4", group = LabGroup.Lights, name = "NIGHT 4", when = LabWhen.Now,
                look = "Lamps and headlights blocked by walls, buildings, medians and rock; one smooth night sky; pale pavements no brighter than the road.",
                env = new[] { "PSX_NIGHT4" }, members = new[] { "StreetLights.Night4" },
                apply = () => StreetLights.N4Touch(),
            });
            Register(new LabRow
            {
                id = "citylamps3", group = LabGroup.Lights, name = "CITY LAMP ROWS 3", when = LabWhen.BuiltIn,
                look = "Built into the city tiles: the lamp posts' spacing. Changed by a city bake only.",
                env = new[] { "PSX_CITY_LAMPS3" }, requires = "CityMeshes.Lamps3",
            });
            // ---- WORLD ----------------------------------------------------------
            Register(new LabRow
            {
                id = "farhaze2", group = LabGroup.World, name = "FAR HAZE", when = LabWhen.Now,
                look = "Far ridges in haze, rain or fog: never brighter than the sky behind them.",
                env = new[] { "PSX_FARHAZE2" }, members = new[] { "TimeOfDay.FarHaze2" },
                apply = () => TimeOfDay.RefreshSky(),
            });
            Register(new LabRow
            {
                id = "tunnellamps", group = LabGroup.World, name = "TUNNEL WALL LAMPS", when = LabWhen.NextLoad, option = true,
                look = "The amber lamp row along the Parkway tunnels' walls. OFF: unlit, as the real bores.",
                env = new[] { "PSX_TUNNEL_LAMPS" }, members = new[] { "TunnelLamps.WallLampsOn" },
                def = () => false,
            });
            Register(new LabRow
            {
                id = "maparrow", group = LabGroup.World, name = "MAP ARROWS", when = LabWhen.Now,
                look = "The race map's arrows: a slender notched head that points one way only.",
                env = new[] { "PSX_MAP_ARROW_OLD" }, members = new[] { "RouteMapGraphic.OldArrow" },
                newValues = new[] { false },
                apply = () => { foreach (var g in UnityEngine.Object.FindObjectsByType<RouteMapGraphic>(FindObjectsInactive.Exclude)) g.SetVerticesDirty(); },
            });
            Register(new LabRow
            {
                id = "fall2", group = LabGroup.World, name = "FALL COLOURS 2", when = LabWhen.BuiltIn,
                look = "Built into the stage scenes: the fall leaves, verges and leaves on the road. A scene build.",
                env = new[] { "PSX_FALL2" }, requires = "Seasons.Fall2Live",
            });
            Register(new LabRow
            {
                id = "tunnel2", group = LabGroup.World, name = "PARKWAY TUNNELS", when = LabWhen.BuiltIn,
                look = "Built into the stage scenes: the tunnels' bore, walls and fixtures. A scene build.",
                env = new[] { "PSX_TUNNEL2" }, requires = "TunnelLamps.Tunnel2",
            });
            // parkway-tunnel2 (not merged yet): both build-time, so listed as built in; they show by themselves
            // once TunnelHeadlights exists.
            Register(new LabRow
            {
                id = "tunnel3", group = LabGroup.World, name = "TUNNELS: ROUND 3", when = LabWhen.BuiltIn,
                look = "Built into the stage scenes: the real portals, hillside and signs. A scene build.",
                env = new[] { "PSX_TUNNEL3" }, requires = "TunnelHeadlights.On",
            });
            Register(new LabRow
            {
                id = "roaduv2", group = LabGroup.World, name = "ROAD TEXTURE LENGTH", when = LabWhen.BuiltIn,
                look = "Built into the stage scenes: the road texture's joints at their real spacing. A scene build.",
                env = new[] { "PSX_ROADUV2" }, requires = "TunnelHeadlights.On",
            });
            // tunnel round 2 (merged 11-23): the cars' lights come on by day through a tunnel; read every frame, so live.
            Register(new LabRow
            {
                id = "tunnellights", group = LabGroup.World, name = "HEADLIGHTS IN TUNNELS", when = LabWhen.Now,
                look = "By day the cars' lights come on from 50 m before a tunnel mouth to 50 m past it. OLD: lights follow the hour.",
                env = new[] { "PSX_TUNNEL3" }, members = new[] { "TunnelHeadlights.On" },
            });
            // ---- CARS -----------------------------------------------------------
            Register(new LabRow
            {
                id = "cardots", group = LabGroup.Cars, name = "NO DOTS ON PAINT IN SHADE", when = LabWhen.Now, pcOnly = true,
                look = "A car's paint in shade, indoors and in the menus: no dark speckle.",
                env = new[] { "PSX_CARDOTS" }, members = new[] { "CarDots.On" },
                apply = RemarkCarPaint,
            });
            Register(new LabRow
            {
                id = "cardrops2", group = LabGroup.Cars, name = "SMALL BEADS ON CARS", when = LabWhen.Now,
                look = "Rain on the paint at real size (2-6 mm beads, thin runs); cars stay wet in the menus.",
                env = new[] { "PSX_CARDROPS2" }, members = new[] { "TireSpray.CarDrops2" },
                apply = TireSpray.PushCarDrops2,
            });
            // ---- TRAFFIC --------------------------------------------------------
            Register(new LabRow
            {
                id = "onewayhold", group = LabGroup.Traffic, name = "ONE-WAY STREETS", when = LabWhen.Now,
                look = "Oncoming cars wait short of a one-way street and turn off out of view.",
                env = new[] { "PSX_TRAFFIC_ONEWAY_HOLD" }, members = new[] { "TrafficSystem.OneWayHoldOn" },
            });
            Register(new LabRow
            {
                id = "aishutbrake", group = LabGroup.Traffic, name = "RIVALS SHUT IN: BRAKE", when = LabWhen.Now,
                look = "A rival boxed in on its right brakes fully and drops back, instead of a head-on.",
                env = new[] { "PSX_AI_SHUT_BRAKE" }, members = new[] { "AIDriver.ShutBrakeOn" },
            });
        }

        /// <summary>Add a row (at boot, in RegisterAll). Resolves its members by name; a row whose member is missing
        /// (a branch not merged) is kept but not <see cref="LabRow.Present"/>, so it never shows.</summary>
        public static void Register(LabRow r)
        {
            if (r == null || string.IsNullOrEmpty(r.id)) return;
            all.RemoveAll(x => x.id == r.id);
            r.resolved = new MemberInfo[r.members.Length];
            bool ok = true;
            for (int i = 0; i < r.members.Length; i++)
            {
                r.resolved[i] = Resolve(r.members[i], r.when != LabWhen.BuiltIn);
                if (r.resolved[i] == null) ok = false;
            }
            if (ok && !string.IsNullOrEmpty(r.requires)) ok = Resolve(r.requires, false) != null;
            if (ok && r.members.Length == 0 && r.when != LabWhen.BuiltIn) ok = false;
            r.present = ok;
            all.Add(r);
        }

        const BindingFlags SF = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static MemberInfo Resolve(string path, bool writable)
        {
            int dot = path.LastIndexOf('.');
            if (dot <= 0) return null;
            string tn = path.Substring(0, dot), mn = path.Substring(dot + 1);
            var asm = typeof(LabSwitches).Assembly;
            var t = asm.GetType(tn) ?? asm.GetType("PSXRacing." + tn) ?? asm.GetType("PSXRacing.City." + tn);
            if (t == null) return null;
            var f = t.GetField(mn, SF);
            if (f != null) return f.FieldType == typeof(bool) && (!writable || (!f.IsInitOnly && !f.IsLiteral)) ? f : null;
            var p = t.GetProperty(mn, SF);
            if (p != null && p.PropertyType == typeof(bool) && p.CanRead && (!writable || p.CanWrite)) return p;
            return null;
        }

        static bool Read(MemberInfo m)
        {
            try
            {
                if (m is FieldInfo f) return (bool)f.GetValue(null);
                if (m is PropertyInfo p) return (bool)p.GetValue(null);
            }
            catch (Exception e) { Debug.LogWarning("[Lab] read " + m.Name + ": " + e.Message); }
            return false;
        }

        static void Write(MemberInfo m, bool v)
        {
            if (m is FieldInfo f) { if ((bool)f.GetValue(null) != v) f.SetValue(null, v); }
            else if (m is PropertyInfo p) { if ((bool)p.GetValue(null) != v) p.SetValue(null, v); }
        }

        // =====================================================================
        //  Boot, load, persistence
        // =====================================================================
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BootHook() => Boot();

        /// <summary>Resolve every row; unless this is a test run, put each row's saved choice into its statics.
        /// Before the first scene, so a NEXT LOAD row's choice is in place for the first load too.</summary>
        public static void Boot() => Boot(DetectTestRun());

        /// <summary>The boot with a given test-run verdict (the self-test's way in).</summary>
        public static void Boot(bool testRun)
        {
            TestRun = testRun;
            RegisterAll();
            foreach (var r in all)
            {
                if (!r.present) continue;
                r.envHeld = false;
                foreach (var e in r.env)
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(e))) { r.envHeld = true; break; }
                if (r.when == LabWhen.BuiltIn) { r.chosen = r.Default; continue; }
                r.chosen = Live(r);
                if (r.envHeld || TestRun) continue;
                int saved = PlayerPrefs.GetInt(KeyPrefix + r.id, -1);
                if (saved < 0) continue;
                r.chosen = saved == 1;
                if (r.chosen != Live(r)) WriteRow(r, r.chosen);
            }
            if (!booted)
            {
                booted = true;
                SceneManager.sceneUnloaded += _ => ApplyPending();
                SceneManager.sceneLoaded += (_, __) => ApplyPending();
            }
            Rebuild();
        }

        /// <summary>A level load: every NEXT LOAD row's pending choice goes into its statics now (on the way out of
        /// the old scene and again as the new one arrives: whichever comes first, nothing of the new level is built
        /// with the old value).</summary>
        public static void ApplyPending()
        {
            bool any = false;
            foreach (var r in all)
                if (r.present && r.when == LabWhen.NextLoad && r.chosen != Live(r)) { WriteRow(r, r.chosen); any = true; }
            if (any) Rebuild();
        }

        /// <summary>Every member's value now (tests: a snapshot to compare and restore).</summary>
        public static bool[] MemberValues(LabRow r)
        {
            var v = new bool[r.resolved != null ? r.resolved.Length : 0];
            for (int i = 0; i < v.Length; i++) v[i] = r.resolved[i] != null && Read(r.resolved[i]);
            return v;
        }

        /// <summary>Put a snapshot back as it was (tests), without the row's apply.</summary>
        public static void RestoreMembers(LabRow r, bool[] v)
        {
            for (int i = 0; i < v.Length && i < r.resolved.Length; i++) if (r.resolved[i] != null) Write(r.resolved[i], v[i]);
        }

        /// <summary>The members' names as resolved (the hand-back table, the self-test).</summary>
        public static string MemberList(LabRow r) => string.Join(", ", r.members);

        /// <summary>The state the statics hold right now (the first member).</summary>
        public static bool Live(LabRow r)
        {
            if (r == null || r.resolved == null || r.resolved.Length == 0 || r.resolved[0] == null) return r != null && r.Default;
            return Read(r.resolved[0]) == r.NewValue(0);
        }

        /// <summary>True when a NEXT LOAD row waits for a load.</summary>
        public static bool Pending(LabRow r) => r != null && r.present && r.when == LabWhen.NextLoad && r.chosen != Live(r);
        public static bool AnyPending { get { foreach (var r in all) if (Pending(r)) return true; return false; } }

        static void WriteRow(LabRow r, bool v)
        {
            for (int i = 0; i < r.resolved.Length; i++) Write(r.resolved[i], r.NewValue(i) ? v : !v);
            try { r.apply?.Invoke(); }
            catch (Exception e) { Debug.LogWarning("[Lab] " + r.id + " apply: " + e.Message); }
        }

        /// <summary>The page's press: NEW &lt;-&gt; OLD. False when the row cannot change (built in, held by a variable).</summary>
        public static bool Flip(LabRow r) => Set(r, !r.chosen, !TestRun && !PerfTour.Active);

        /// <summary>Choose a state. <paramref name="persist"/>: remember it (never in a test run). A NOW row is
        /// written at once, a NEXT LOAD row at the next load.</summary>
        public static bool Set(LabRow r, bool v, bool persist)
        {
            if (r == null || !r.present || r.when == LabWhen.BuiltIn || r.envHeld) return false;
            r.chosen = v;
            if (persist && !TestRun)
            {
                if (v == r.Default) PlayerPrefs.DeleteKey(KeyPrefix + r.id);
                else PlayerPrefs.SetInt(KeyPrefix + r.id, v ? 1 : 0);
                PlayerPrefs.Save();
            }
            if (r.when == LabWhen.Now && v != Live(r)) WriteRow(r, v);
            Rebuild();
            return true;
        }

        /// <summary>RESET ALL TO DEFAULT (rows held by a variable stay as the variable says).</summary>
        public static void ResetAll(bool persist)
        {
            foreach (var r in all) if (r.present && r.when != LabWhen.BuiltIn && !r.envHeld) Set(r, r.Default, persist);
        }

        /// <summary>A row's saved choice (-1 none, 0 OLD/OFF, 1 NEW/ON): for a row retired into a player option.</summary>
        public static int SavedChoice(string id) => PlayerPrefs.GetInt(KeyPrefix + id, -1);

        /// <summary>Every row that shows on this platform.</summary>
        public static List<LabRow> Rows(LabGroup? g = null)
        {
            if (!booted) Boot();
            var list = new List<LabRow>();
            foreach (var r in all)
                if (r.present && (!r.pcOnly || PlatformProfile.PC) && (g == null || r.group == g.Value)) list.Add(r);
            return list;
        }

        public static LabRow Find(string id)
        {
            if (!booted) Boot();
            foreach (var r in all) if (r.id == id) return r;
            return null;
        }

        public static int ChangedCount { get { int n = 0; foreach (var r in Rows()) if (r.Changed) n++; return n; } }

        static void Rebuild()
        {
            Version++;
            StringBuilder sb = null;
            foreach (var r in all)
            {
                if (!r.Changed || (r.pcOnly && !PlatformProfile.PC)) continue;
                if (sb == null) sb = new StringBuilder("LAB: "); else sb.Append(", ");
                sb.Append(r.name).Append(' ').Append(r.option ? r.StateWord(r.chosen) : r.chosen ? "new" : "old");
                if (Pending(r)) sb.Append(" (next load)");
                if (r.envHeld) sb.Append(" (env)");
            }
            ChangedLine = sb?.ToString();
        }

        // =====================================================================
        //  Test runs
        // =====================================================================
        static readonly string[] TestArgs = { "-perftour", "-titleshots", "-psx-title-hour", "-psx-lab-ignore", "-batchmode", "-nographics" };

        /// <summary>A run whose numbers or pictures must not follow the owner's toggles.</summary>
        static bool DetectTestRun()
        {
            if (Environment.GetEnvironmentVariable("PSX_LAB_IGNORE") == "1") return true;
            if (Application.isBatchMode) return true;
#if UNITY_EDITOR
            // the editor's PlayerPrefs are not the owner's, and every agent's play check runs here:
            // saved choices only when asked for (PSX_LAB_PREFS=1)
            return Environment.GetEnvironmentVariable("PSX_LAB_PREFS") != "1";
#elif UNITY_WEBGL
            string url = Application.absoluteURL ?? "";
            return url.Contains("perftour=") || url.Contains("shot=");
#else
            foreach (var a in Environment.GetCommandLineArgs())
                foreach (var t in TestArgs)
                    if (string.Equals(a, t, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
#endif
        }

        /// <summary>Every PSX/CarPaint material in the scene marked (or unmarked) as CarDots.On now says.</summary>
        static void RemarkCarPaint()
        {
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) CarDots.Mark(mats[i]);
            }
        }
    }
}
