using UnityEngine;

namespace PSXRacing
{
    /// <summary>Which game a build is. See <see cref="Edition"/>.</summary>
    public enum EditionKind
    {
        /// <summary>Everything: the editor, every tool, and the one game the
        /// two editions become again when they are united.</summary>
        All = 0,
        /// <summary>The game at the site root: every venue except Charlotte,
        /// the career, the house, the town.</summary>
        Main = 1,
        /// <summary>The Charlotte test page: the city, its races, a car picker
        /// and the options. No career, no mountains, no circuits.</summary>
        City = 2,
    }

    /// <summary>
    /// THE TWO EDITIONS, AS ONE CODEBASE.
    ///
    /// The owner, 2026-09-28: "Charlotte map and Charlotte tracks should be in
    /// their own version until being united." Until then the site root ships
    /// MAIN (no Charlotte at all, not even its data) and /city/ ships CITY (the
    /// city and nothing else). Both are this code, identical to the byte, told
    /// apart by ONE marker the WebGL build packs into the player's data
    /// (Resources/<see cref="MarkerResource"/>.txt, see <see cref="Baked"/>):
    ///
    ///   "MAIN"     -> <see cref="EditionKind.Main"/>
    ///   "CITY"     -> <see cref="EditionKind.City"/>
    ///   no marker  -> <see cref="EditionKind.All"/>
    ///
    /// The editor never reads the marker, so the editor, the scene builder,
    /// the self-test and every audit see ALL — the whole game — unless a tool
    /// asks to see one edition (<see cref="Simulate"/>, or
    /// <c>-psxEdition MAIN|CITY</c> / <c>PSX_EDITION</c> on the editor's
    /// command line or environment).
    ///
    /// UNITING THEM AGAIN is the build writing no marker: <see cref="Baked"/>
    /// is ALL, every filter below lets everything through, and the game is
    /// the one it was on 2026-09-28. Nothing here deletes or reorders a venue;
    /// catalog indices, scene files and saves are the same in all three.
    ///
    /// THE RULE is one line, not a list: a venue belongs to CITY when it is
    /// raced on the streamed city (<see cref="TrackCatalog.TrackDef.city"/>),
    /// and to MAIN otherwise. Reverse twins copy <c>city</c>, sprints are cut
    /// from mountain loops, and a venue appended later lands in the right
    /// edition without anybody remembering to say so. Sunset City GP is a
    /// fictional circuit (<c>city = false</c>) and stays in MAIN.
    /// </summary>
    public static class Edition
    {
        /// <summary>Resources path of the edition marker: a one-word text
        /// asset ("MAIN" / "CITY") that PSXBuildWebGL writes for the length
        /// of ONE BuildPlayer call and deletes after. No marker = ALL.</summary>
        public const string MarkerResource = "PSXEdition";

        /// <summary>
        /// The edition this player was BUILT as. Until 2026-10-24 this was a
        /// const off a scripting define, which made the two editions' script
        /// assemblies differ by one line and forced a full IL2CPP + wasm
        /// recompile for each (~20 min a build). Now the code is byte-for-byte
        /// the same in both and the edition rides in the build's DATA, so the
        /// second build reuses the first one's compile. Still not a runtime
        /// choice: the marker is packed into the player, nothing on the page
        /// or the URL reaches it. The editor never reads it - it is always ALL.
        /// </summary>
        public static EditionKind Baked
        {
            get
            {
#if UNITY_EDITOR
                return EditionKind.All;
#else
                if (!bakedRead)
                {
                    bakedRead = true;
                    var marker = Resources.Load<TextAsset>(MarkerResource);
                    string word = marker != null ? marker.text.Trim().ToUpperInvariant() : "";
                    baked = word == "MAIN" ? EditionKind.Main : word == "CITY" ? EditionKind.City : EditionKind.All;
                }
                return baked;
#endif
            }
        }
#if !UNITY_EDITOR
        static bool bakedRead;
        static EditionKind baked;
#endif

#if UNITY_EDITOR
        static EditionKind? simulated;
        static bool envRead;
        static EditionKind? fromEnv;
#endif

        /// <summary>
        /// The edition the game is behaving as. In a player this is always
        /// <see cref="Baked"/>. In the editor it is ALL unless a tool
        /// simulated an edition, or the editor was started with
        /// <c>-psxEdition MAIN|CITY</c> or <c>PSX_EDITION=MAIN|CITY</c> (which
        /// is how tools\race-play-check.ps1 and tools\city-play-check.ps1 play
        /// a test AS one edition). An environment variable, not only a static,
        /// because it has to survive a domain reload into play mode.
        /// </summary>
        public static EditionKind Current
        {
            get
            {
#if UNITY_EDITOR
                if (simulated.HasValue) return simulated.Value;
                if (!envRead) { envRead = true; fromEnv = FromEditorLaunch(); }
                if (fromEnv.HasValue) return fromEnv.Value;
#endif
                return Baked;
            }
        }

        /// <summary>
        /// Editor tooling only: behave as <paramref name="e"/> until told
        /// otherwise; null goes back to the launch default. A no-op in a
        /// player, where the edition is packed into the build.
        /// </summary>
        public static void Simulate(EditionKind? e)
        {
#if UNITY_EDITOR
            simulated = e;
#endif
        }

        /// <summary>What a tool last simulated, or null — so a tool that must
        /// run as ALL (the scene builder, the self-test) can put back whatever
        /// it found. Always null in a player.</summary>
        public static EditionKind? Simulated
        {
            get
            {
#if UNITY_EDITOR
                return simulated;
#else
                return null;
#endif
            }
        }

        /// <summary>The edition a venue belongs to. See the class notes for
        /// why this is a rule and not a list.</summary>
        public static EditionKind Of(TrackCatalog.TrackDef def) =>
            def != null && def.city ? EditionKind.City : EditionKind.Main;

        /// <summary>Does <paramref name="edition"/> carry this venue?</summary>
        public static bool ShipsIn(TrackCatalog.TrackDef def, EditionKind edition) =>
            def != null && (edition == EditionKind.All || Of(def) == edition);

        /// <summary>Is this venue in THIS build? Every picker, every roll and
        /// every lazy loader asks this before touching a venue's data.</summary>
        public static bool Ships(TrackCatalog.TrackDef def) => ShipsIn(def, Current);

        /// <summary>The career — the house, the calendar, the town, money,
        /// saves — exists in this edition.</summary>
        public static bool HasCareer => Current != EditionKind.City;

        /// <summary>Charlotte — the free-roam city and its races — exists in
        /// this edition.</summary>
        public static bool HasCharlotte => Current != EditionKind.Main;

        /// <summary>MAIN / CITY / ALL, as the tools and the result files
        /// write it.</summary>
        public static string Name(EditionKind e) =>
            e == EditionKind.Main ? "MAIN" : e == EditionKind.City ? "CITY" : "ALL";

        /// <summary>Parse MAIN / CITY / ALL (any case). False on anything else.</summary>
        public static bool TryParse(string s, out EditionKind e)
        {
            e = EditionKind.All;
            if (string.IsNullOrEmpty(s)) return false;
            switch (s.Trim().ToUpperInvariant())
            {
                case "ALL": e = EditionKind.All; return true;
                case "MAIN": e = EditionKind.Main; return true;
                case "CITY": e = EditionKind.City; return true;
            }
            return false;
        }

#if UNITY_EDITOR
        /// <summary>
        /// <c>-psxEdition X</c> on the editor's command line, else the
        /// <c>PSX_EDITION</c> environment variable, else null (ALL). Read by
        /// the editor-side build and tools through
        /// <c>EditorTools.EditionTarget</c> as well, so the two can never
        /// disagree about what was asked for.
        /// </summary>
        public static EditionKind? FromEditorLaunch()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "-psxEdition", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParse(args[i + 1], out var fromArg)) return fromArg;
                    Debug.LogError("[Edition] -psxEdition '" + args[i + 1] + "' is not MAIN, CITY or ALL");
                }
            if (TryParse(System.Environment.GetEnvironmentVariable("PSX_EDITION"), out var fromVar))
                return fromVar;
            return null;
        }
#endif
    }
}
