using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The edition an EDITOR JOB was asked to act for: <c>-psxEdition
    /// MAIN|CITY|ALL</c> on its command line, else <c>PSX_EDITION</c> in its
    /// environment, else ALL. The WebGL build takes its define from this, and
    /// the audits take their venue list from it (TrackCatalog.ScenedFor), so a
    /// verify run for MAIN measures exactly the scenes MAIN ships. Read from
    /// the same place as the runtime's own editor default (Edition.Current),
    /// so the two cannot disagree about what was asked for.
    /// </summary>
    public static class EditionTarget
    {
        public static EditionKind Current => Edition.FromEditorLaunch() ?? EditionKind.All;

        /// <summary>The define a player build of this edition compiles with,
        /// or null for ALL (no define: the whole game).</summary>
        public static string DefineFor(EditionKind e) =>
            e == EditionKind.Main ? "PSX_EDITION_MAIN" :
            e == EditionKind.City ? "PSX_EDITION_CITY" : null;
    }

    /// <summary>
    /// RESOURCES SHIP WHOLE, so an edition's build PARKS the other edition's.
    ///
    /// Leaving a scene out of a build drops everything only that scene
    /// references. Resources are different: Unity packs EVERY asset under a
    /// Resources folder into every player, referenced or not, because anything
    /// may Resources.Load it by name. So MAIN would still ship Charlotte's
    /// 5.7 MB of city data and CITY the pizza cargo and every stage bake,
    /// however the scene lists were cut. For the length of ONE BuildPlayer
    /// call those assets are moved out of Resources and back:
    ///
    ///   1. the manifest (PSXEditionParked.json, project root, outside Assets)
    ///      is written FIRST, so a kill at any later moment can be undone;
    ///   2. each item is AssetDatabase.MoveAsset'd to
    ///      Assets/PSXRacing/_EditionParked/&lt;same name&gt; — a path with no
    ///      Resources segment. GUIDs and .meta files travel with the files;
    ///      nothing is deleted, nothing re-imported;
    ///   3. the build runs inside try/finally and <see cref="Restore"/> moves
    ///      everything back and deletes the manifest;
    ///   4. a surviving manifest (a crash, a kill) is put back BEFORE anything
    ///      else touches the sandbox. The tools do it first: unity-wait.ps1,
    ///      which every sandbox tool dot-sources before its first robocopy,
    ///      finds the manifest and runs a one-minute
    ///      <c>-executeMethod ...EditionParking.RecoverIfNeeded</c> job (and checks again before
    ///      every Unity job it starts). That order matters: the additive
    ///      /E /XO copies of Resources (verify -NoMirror, the city and wall
    ///      tools) would otherwise put the source's charlotte_* files and
    ///      CityProps metas back BESIDE the parked copies, with the same
    ///      GUIDs, and every item would stick. Inside Unity, the scene build,
    ///      the WebGL build, the self-test and the play checks call
    ///      <see cref="RecoverIfNeeded"/> first; an editor start schedules it
    ///      as well, but that is a delayCall, which a batch -executeMethod job
    ///      runs its method before (2026-09-29: it never fired after a killed
    ///      MAIN build - the restore was run by hand).
    ///   5. A park that cannot be put back - its Resources path is occupied
    ///      again, or a move failed - is STUCK, and stuck is loud and final
    ///      until a person looks: the manifest stays, <see cref="Park"/>
    ///      refuses to build over it (overwriting it would lose its list, and
    ///      the next restore would delete the parked copies - possibly the
    ///      only copies), the tools refuse to copy into the sandbox, and the
    ///      park folder is deleted only when it is EMPTY.
    ///
    /// NOT a trailing "~" rename: renaming Resources/CityProps to CityProps~
    /// orphans CityProps.meta, Unity deletes the orphan, and the folder comes
    /// back with a NEW GUID — the churn Docs and the guid audit exist to stop.
    /// And ONLY around BuildPlayer: a scene build re-bakes those folders.
    ///
    /// Parking refuses ALL (nothing to leave out), and the editor's own
    /// "Build WebGL" menu is ALL, so the owner's editor never parks anything.
    /// </summary>
    [InitializeOnLoad]
    public static class EditionParking
    {
        public const string ResourcesRoot = "Assets/PSXRacing/Resources";
        public const string ParkRoot = "Assets/PSXRacing/_EditionParked";

        static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        public static string ManifestPath => Path.Combine(ProjectRoot, "PSXEditionParked.json");

        /// <summary>Set (per editor session, so it survives a domain reload)
        /// while THIS editor holds a park it is building under. Nothing in
        /// this editor restores behind a live park's back.</summary>
        const string LiveKey = "PSXRacing.EditionParking.Live";

        static EditionParking()
        {
            // Not in the constructor itself: the asset database may be
            // mid-import while static constructors run.
            EditorApplication.delayCall += AutoRecover;
        }

        /// <summary>The editor-start path. Never under a build this editor is
        /// running: that build's own park is live and its finally restores it.</summary>
        static void AutoRecover()
        {
            if (BuildPipeline.isBuildingPlayer) return;
            RecoverIfNeeded();
        }

        /// <summary>
        /// What <paramref name="edition"/> must NOT ship, as names under
        /// Resources (a file or a folder). By RULE, so a new venue's data
        /// lands on the right side without a list to remember:
        ///   MAIN: every Resources-root file named charlotte_* (the city graph,
        ///         buildings, ground, routes, the menu thumb) and CityProps/
        ///         (the 32 prefabs only a streamed Charlotte tile loads).
        ///   CITY: PizzaCargo/ (deliveries are a career job) and the stage bake
        ///         of every venue CITY does not carry (TrackDef.stageData,
        ///         held-back venues included).
        /// Engines, CarModels, Sfx, Sky and the two catalogs ship in both.
        /// </summary>
        public static List<string> ParkListFor(EditionKind edition)
        {
            var list = new List<string>();
            if (edition == EditionKind.All || !Directory.Exists(ResourcesRoot)) return list;
            var rootFiles = Directory.GetFiles(ResourcesRoot)
                .Select(Path.GetFileName)
                .Where(n => !n.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            if (edition == EditionKind.Main)
            {
                foreach (var n in rootFiles)
                    if (n.StartsWith("charlotte_", StringComparison.OrdinalIgnoreCase)) list.Add(n);
                if (Directory.Exists(ResourcesRoot + "/CityProps")) list.Add("CityProps");
            }
            else
            {
                if (Directory.Exists(ResourcesRoot + "/PizzaCargo")) list.Add("PizzaCargo");
                var bakes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var d in TrackCatalog.All.Concat(TrackCatalog.HeldBack))
                    if (d.stage && !string.IsNullOrEmpty(d.stageData) && !Edition.ShipsIn(d, EditionKind.City))
                        bakes.Add(d.stageData);
                foreach (var n in rootFiles)
                    if (bakes.Contains(Path.GetFileNameWithoutExtension(n))) list.Add(n);
            }
            return list;
        }

        /// <summary>Why a park cannot start here, or null. A manifest that a
        /// restore could not clear is a STUCK park; a non-empty park folder
        /// with no manifest is parked data nobody has a list for. Either way
        /// the only copy of something may be in <see cref="ParkRoot"/>.</summary>
        public static string BlockedReason()
        {
            if (File.Exists(ManifestPath))
                return ManifestPath + " is still there after a restore: an earlier park is STUCK (" +
                       string.Join(", ", ManifestItems()) + "). Parking over it would overwrite its list, " +
                       "and this build's restore would then delete the parked copies. Compare " + ParkRoot +
                       " with " + ResourcesRoot + ", put each item back by hand (or re-mirror the sandbox " +
                       "from the source and re-run the scene build), then delete the manifest.";
            if (ParkFolderHasEntries())
                return ParkRoot + " holds " + string.Join(", ", ParkFolderEntries()) +
                       " and there is no manifest listing it. Put it back in " + ResourcesRoot +
                       " (or delete it if Resources already has it) before building.";
            return null;
        }

        static IEnumerable<string> ManifestItems()
        {
            if (!File.Exists(ManifestPath)) return Enumerable.Empty<string>();
            return File.ReadAllLines(ManifestPath).Select(l => l.Trim())
                .Where(n => n.Length > 0 && !n.StartsWith("edition ", StringComparison.Ordinal) &&
                            !n.StartsWith("pid ", StringComparison.Ordinal));
        }

        static bool ParkFolderHasEntries() =>
            Directory.Exists(ParkRoot) && Directory.EnumerateFileSystemEntries(ParkRoot).Any();

        static IEnumerable<string> ParkFolderEntries() =>
            Directory.Exists(ParkRoot)
                ? Directory.EnumerateFileSystemEntries(ParkRoot).Select(Path.GetFileName)
                : Enumerable.Empty<string>();

        /// <summary>Move <paramref name="edition"/>'s park list out of
        /// Resources. Returns what moved. Throws (after putting back whatever
        /// had moved) when a move fails, so a build never runs half-parked, and
        /// throws BEFORE moving anything when an earlier park is stuck
        /// (<see cref="BlockedReason"/>) - ALL included, because a stuck park
        /// means this build's own Resources are missing items.</summary>
        public static List<string> Park(EditionKind edition)
        {
            RecoverIfNeeded();
            string blocked = BlockedReason();
            if (blocked != null) throw new Exception("[EditionParking] REFUSING TO PARK: " + blocked);
            var moved = new List<string>();
            if (edition == EditionKind.All)
            {
                Debug.Log("[EditionParking] ALL ships everything - nothing parked");
                return moved;
            }
            var items = ParkListFor(edition);
            // THE MANIFEST FIRST: from this line on, a kill is recoverable.
            var sb = new StringBuilder();
            sb.AppendLine("edition " + Edition.Name(edition));
            sb.AppendLine("pid " + System.Diagnostics.Process.GetCurrentProcess().Id);
            foreach (var n in items) sb.AppendLine(n);
            File.WriteAllText(ManifestPath, sb.ToString());
            SessionState.SetBool(LiveKey, true);

            if (!AssetDatabase.IsValidFolder(ParkRoot))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(ParkRoot).Replace('\\', '/'),
                                           Path.GetFileName(ParkRoot));
            try
            {
                foreach (var n in items)
                {
                    string from = ResourcesRoot + "/" + n, to = ParkRoot + "/" + n;
                    string err = AssetDatabase.MoveAsset(from, to);
                    if (!string.IsNullOrEmpty(err))
                        throw new Exception("could not park " + from + ": " + err);
                    moved.Add(n);
                }
            }
            catch
            {
                Restore();
                throw;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[EditionParking] " + Edition.Name(edition) + ": parked " + moved.Count +
                      " Resources item(s): " + string.Join(", ", moved));
            return moved;
        }

        /// <summary>Put every parked item back and delete the manifest. Safe
        /// to call with nothing parked. An item whose Resources path is
        /// occupied again (a copy from the source, a scene build's re-bake) is
        /// LEFT in the park and reported, never overwritten; the manifest then
        /// stays and the park is STUCK. The park folder is deleted only when
        /// it is empty - it may hold the only copy of something.</summary>
        public static void Restore()
        {
            SessionState.SetBool(LiveKey, false);
            if (!File.Exists(ManifestPath)) return;
            int back = 0, stuck = 0;
            foreach (var n in ManifestItems().ToList())
            {
                string from = ParkRoot + "/" + n, to = ResourcesRoot + "/" + n;
                bool parked = File.Exists(from) || Directory.Exists(from);
                if (!parked) continue;
                if (File.Exists(to) || Directory.Exists(to))
                {
                    Debug.LogError("[EditionParking] " + to + " exists again - leaving the parked copy at " +
                                   from + " for a person to look at");
                    stuck++;
                    continue;
                }
                string err = AssetDatabase.MoveAsset(from, to);
                if (!string.IsNullOrEmpty(err))
                {
                    Debug.LogError("[EditionParking] could not restore " + to + ": " + err);
                    stuck++;
                }
                else back++;
            }
            bool leftovers = ParkFolderHasEntries();
            if (stuck == 0 && !leftovers)
            {
                if (AssetDatabase.IsValidFolder(ParkRoot)) AssetDatabase.DeleteAsset(ParkRoot);
                else if (Directory.Exists(ParkRoot)) Directory.Delete(ParkRoot);   // empty, never imported
                File.Delete(ManifestPath);
            }
            else if (stuck == 0)
                Debug.LogError("[EditionParking] " + ParkRoot + " still holds " +
                               string.Join(", ", ParkFolderEntries()) +
                               ", which this manifest does not list - kept, with the manifest, for a person to look at");
            AssetDatabase.SaveAssets();
            Debug.Log("[EditionParking] restored " + back + " item(s)" +
                      (stuck > 0 || leftovers ? ", " + stuck + " LEFT PARKED, park folder " +
                       (leftovers ? "not empty" : "empty") + " (manifest kept: STUCK)" : ""));
        }

        /// <summary>A manifest on disk means a build was interrupted with
        /// something parked: restore it now. Left alone while THIS editor's
        /// own build holds the park (the build's finally restores it).</summary>
        public static void RecoverIfNeeded()
        {
            if (!File.Exists(ManifestPath)) return;
            if (SessionState.GetBool(LiveKey, false))
            {
                Debug.Log("[EditionParking] the park in " + ManifestPath + " is this editor's own live build - left alone");
                return;
            }
            Debug.LogWarning("[EditionParking] found " + ManifestPath +
                             " - an edition build did not finish; restoring Resources");
            Restore();
        }
    }
}
