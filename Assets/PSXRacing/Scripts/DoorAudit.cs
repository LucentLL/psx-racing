using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// EVERY DOOR, RESOLVED IN THE BUILD THAT IS RUNNING.
    ///
    /// Since the editions (see <see cref="Edition"/>) every door in the game -
    /// each race, twin and sprint, free roam, IN TOWN, your street, the
    /// walk-in, the seller's driveway, a delivery - finds its scene through
    /// ONE call, <see cref="TrackCatalog.BuildIndexOfScene"/>, and in a PLAYER
    /// that call takes a branch (SceneUtility) that no editor tool can run:
    /// the self-test, the play checks and the menu previews all resolve
    /// against EditorBuildSettings. A path the player spells differently would
    /// pass all of them and turn every race on the live site into an "IS NOT
    /// IN THIS EDITION" toast (review, 2026-09-29).
    ///
    /// So the player asks itself at boot, with the same properties the doors
    /// call - SceneIndex(venue), TownSceneIndex, NeighborhoodSceneIndex, ... -
    /// and checks each answer against the scene at that index:
    ///   * every venue THIS edition carries resolves to its own scene
    ///     (a twin to its road's, a sprint to its loop's), loadable;
    ///   * every venue it does not carry resolves to -1 (nothing of the other
    ///     edition slipped into the player);
    ///   * the career's scenes are there exactly when there is a career;
    ///   * and every scene in the build is some door's (LifeHome at 0).
    /// One line to the console - "[Doors] OK ..." or "[Doors] FAIL ..." with
    /// every problem - which tools\door-tour.mjs reads before it walks the
    /// doors for real. The editor runs the same audit through the self-test
    /// against each edition's player list (EditorSceneListOverride).
    /// </summary>
    public static class DoorAudit
    {
        /// <summary>What one audit found.</summary>
        public class Result
        {
            public EditionKind edition;
            public int scenes;
            public int venueDoors, venueOk;
            public int absentDoors, absentOk;
            public int careerDoors, careerOk;
            public readonly List<string> problems = new List<string>();
            public bool Ok => problems.Count == 0;

            public string Line()
            {
                string head = (Ok ? "[Doors] OK " : "[Doors] FAIL ") + Edition.Name(edition) + " build, " + scenes +
                              " scenes: " + venueOk + "/" + venueDoors + " venue doors reach their scene, " +
                              absentOk + "/" + absentDoors + " other-edition doors absent, " +
                              careerOk + "/" + careerDoors + " career doors, every scene some door's";
                return Ok ? head : head + " - " + problems.Count + " problem(s): " + string.Join(" | ", problems);
            }
        }

        /// <summary>The career's scenes, by name, and the property each door
        /// reads. The Pizzeria and the old walk-in Garage are only fallbacks
        /// now (the shift is at the town's counter, the walk-in is your own
        /// street) but they ship, so they are doors here.</summary>
        static readonly string[] CareerScenes = { "Garage", "Pizzeria", "Town", "SellerLot", "Neighborhood" };

        static int CareerIndex(string name)
        {
            switch (name)
            {
                case "Garage": return TrackCatalog.GarageSceneIndex;
                case "Pizzeria": return TrackCatalog.PizzeriaSceneIndex;
                case "Town": return TrackCatalog.TownSceneIndex;
                case "SellerLot": return TrackCatalog.SellerLotSceneIndex;
                case "Neighborhood": return TrackCatalog.NeighborhoodSceneIndex;
            }
            return TrackCatalog.BuildIndexOfScene(name);
        }

        /// <summary>
        /// Walk every door of <paramref name="edition"/> through this build's
        /// scene list. In a player, the edition is <see cref="Edition.Current"/>
        /// and the list is the player's; in the editor, whatever
        /// TrackCatalog.EditorSceneListOverride holds.
        /// </summary>
        public static Result Run(EditionKind edition)
        {
            var r = new Result { edition = edition, scenes = TrackCatalog.ScenesInBuild };
            var reached = new HashSet<string>();
            string home = TrackCatalog.ScenePathOf("LifeHome");
            if (TrackCatalog.ScenePathAt(0) != home)
                r.problems.Add("scene 0 is '" + TrackCatalog.ScenePathAt(0) + "', not LifeHome");
            else reached.Add(home);

            var all = TrackCatalog.All;
            for (int i = 0; i < all.Length; i++)
            {
                var def = all[i];
                string want = TrackCatalog.ScenePathOf(TrackCatalog.SceneIdOf(i));
                int idx = TrackCatalog.SceneIndex(i);
                if (Edition.ShipsIn(def, edition))
                {
                    r.venueDoors++;
                    string got = TrackCatalog.ScenePathAt(idx);
                    if (!TrackCatalog.SceneShipped(idx))
                        r.problems.Add(def.id + " resolves to " + idx + " (not loadable)");
                    else if (got != want)
                        r.problems.Add(def.id + " resolves to #" + idx + " '" + got + "', wants '" + want + "'");
                    else { r.venueOk++; reached.Add(want); }
                }
                else
                {
                    r.absentDoors++;
                    if (idx >= 0) r.problems.Add(def.id + " is " + Edition.Name(Edition.Of(def)) +
                                                 "'s but resolves to #" + idx + " in this build");
                    else r.absentOk++;
                }
            }

            bool career = edition != EditionKind.City;
            foreach (var name in CareerScenes)
            {
                r.careerDoors++;
                int idx = CareerIndex(name);
                string want = TrackCatalog.ScenePathOf(name);
                if (career)
                {
                    string got = TrackCatalog.ScenePathAt(idx);
                    if (!TrackCatalog.SceneShipped(idx) || got != want)
                        r.problems.Add(name + " resolves to " + idx + (idx >= 0 ? " '" + got + "'" : "") + ", wants '" + want + "'");
                    else { r.careerOk++; reached.Add(want); }
                }
                else
                {
                    if (idx >= 0) r.problems.Add(name + " is the career's but resolves to #" + idx + " in this build");
                    else r.careerOk++;
                }
            }

            for (int b = 0; b < r.scenes; b++)
            {
                string p = TrackCatalog.ScenePathAt(b);
                if (!reached.Contains(p)) r.problems.Add("scene #" + b + " '" + p + "' is no door's");
            }
            return r;
        }

#if !UNITY_EDITOR
        /// <summary>In a player, once, before the first scene: the line
        /// door-tour.mjs (and anyone with the console open) reads.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AtBoot()
        {
            Result r;
            try { r = Run(Edition.Current); }
            catch (System.Exception e) { Debug.LogError("[Doors] FAIL the audit threw: " + e); return; }
            if (r.Ok) Debug.Log(r.Line());
            else Debug.LogError(r.Line());
        }
#endif
    }
}
