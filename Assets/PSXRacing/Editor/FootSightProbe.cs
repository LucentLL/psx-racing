using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Diagnostic: what would a player standing HERE be offered?
    ///
    /// Written for one report — "I can work on my car in the garage while in the
    /// upstairs bedroom" — and it is the only instrument that can settle it. A
    /// compile proves nothing about a raycast, a screenshot shows a room with no
    /// prompt in it because the prompt only exists at runtime, and the thing
    /// being tested is a geometric relationship between three positions and a
    /// collider mesh nobody authored.
    ///
    /// It answers two questions the fix depends on:
    ///
    ///  1. Does the house collider shell HAVE storey floors? If it does not, the
    ///     player would fall through them, and line of sight cannot block on a
    ///     floor that is not there. Measured by casting down the stairwell-free
    ///     middle of the house and reporting every surface on the way.
    ///
    ///  2. From each place a player actually stands — the driveway they spawn
    ///     on, beside the car, at the bench, and upstairs — which target does
    ///     <see cref="FootInteractor.PickFrom"/> return, and for the ones it
    ///     rejects, was it range, angle or a wall?
    ///
    /// It calls the SHIPPED rule rather than reimplementing it. FootTarget.All
    /// is filled by OnEnable and the editor never calls that, so the candidate
    /// list is gathered by hand and handed in; everything after that is the same
    /// code the game runs.
    ///
    /// Writes PSXRacing_footsight.txt.
    /// </summary>
    public static class FootSightProbe
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            try { Probe(sb); }
            catch (System.Exception e) { sb.AppendLine("PROBE THREW: " + e); }
            System.IO.File.WriteAllText("PSXRacing_footsight.txt", sb.ToString());
            Debug.Log("FOOT SIGHT PROBE\n" + sb);
        }

        static void Probe(StringBuilder sb)
        {
            EditorSceneManager.OpenScene(GarageSceneBuilder.ScenePath, OpenSceneMode.Single);

            // A car in bay 0, because half of what this probe is checking only
            // exists when there is one: the shell carries a box collider you
            // walk around, it sits directly between the player and the roof
            // point the hook aims at, and the sight test has to forgive it.
            // An empty bay would pass this probe and ship a garage in which no
            // car can ever be selected.
            var save = PSXRacing.LifeSim.LifeSimManager.State;
            if (save.cars.Count == 0) PSXRacing.LifeSim.LifeRules.SeedFallbackCar(save);

            // The room is baked; the CONTENTS are spawned from the save at
            // runtime, and the cars are half of what this probe is about.
            var world = Object.FindFirstObjectByType<GarageWorld>();
            if (world == null) { sb.AppendLine("no GarageWorld in the scene"); return; }
            world.PreviewBuild();
            var interactor = Object.FindFirstObjectByType<FootInteractor>();
            if (interactor == null) { sb.AppendLine("no FootInteractor in the scene"); return; }
            var player = interactor.transform;

            var targets = new List<FootTarget>(
                Object.FindObjectsByType<FootTarget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            sb.AppendLine("targets in scene: " + targets.Count);
            foreach (var t in targets)
                sb.AppendLine("  " + Path(t.transform) + "  focus " + V(t.FocusPoint) +
                              "  range " + t.range.ToString("0.0") +
                              "  ignore=" + (t.IgnoreRoot != null ? t.IgnoreRoot.name : "-"));
            sb.AppendLine();

            // ---- 1. storeys ----------------------------------------------
            // Straight down through the middle of the house, and down through
            // the bay the car parks in. Every surface on the way is a floor or a
            // ceiling; if the list has one entry the shell is a box and the sight
            // test has nothing to block on.
            var bay = world.bays != null && world.bays.Length > 0 ? world.bays[0] : null;
            sb.AppendLine("STOREYS (downward cast from y=12):");
            var floors = new List<float>();
            if (bay != null) floors = CastDown(sb, "over bay 0", bay.position + Vector3.up * 12f);
            var houseMid = bay != null ? bay.position + new Vector3(3.4f, 12f, 1.2f) : Vector3.up * 12f;
            CastDown(sb, "over the house interior", houseMid);
            sb.AppendLine();

            // ---- 2. what is offered from where ---------------------------
            // The upstairs eye is derived rather than guessed: the second
            // surface down over the bay IS the bedroom floor, and a player
            // stands 1.70 m above whatever they are stood on.
            float upstairsFloor = floors.Count >= 2 ? floors[floors.Count - 2] : float.NaN;

            var spots = new List<(string name, Vector3 at, Vector3 look)>();
            spots.Add(("spawn (driveway)", player.position + Vector3.up * FootRig.EyeH, player.forward));
            var exit = targets.Find(t => t.transform.parent != null && t.transform.parent.name == "ExitDoor");
            if (bay != null)
            {
                // Bay 0 is 2.4 m inside a 4.6 m garage whose walls are 1.55 m
                // either side of it, and the car in it is 1.8 m wide and 4.4 m
                // long. That leaves two 0.65 m aisles and a strip of doorway,
                // and these are the only places a 0.52 m capsule can actually
                // BE — a probe standing where the car is proves nothing.
                float eye = FootRig.EyeH;
                Vector3 carAim = bay.position + Vector3.up * 1.03f;

                Vector3 door = new Vector3(bay.position.x, eye, bay.position.z - 2.9f);
                spots.Add(("in the doorway, looking at the car", door, (carAim - door).normalized));

                Vector3 aisleR = new Vector3(bay.position.x + 1.25f, eye, bay.position.z + 0.4f);
                spots.Add(("right aisle, level with the tool board", aisleR, Vector3.left));

                Vector3 rear = new Vector3(bay.position.x + 1.25f, eye, bay.position.z + 1.9f);
                spots.Add(("right aisle, at the back wall", rear, new Vector3(-1f, 0f, -0.4f).normalized));

                // Can the player still LEAVE? The porch door hook sits 3.2 m to
                // the side of the garage opening, on the far side of the garage
                // wall — so the sight test is the difference between a front
                // door you walk up to and a room with no way out of it. There is
                // no pause menu in this scene: that hook is the only exit.
                if (exit != null)
                {
                    Vector3 porch = exit.FocusPoint + new Vector3(0f, 0f, -2.2f);
                    porch.y = eye;
                    spots.Add(("on the porch, in front of the front door",
                               porch, (exit.FocusPoint - porch).normalized));
                }

                if (!float.IsNaN(upstairsFloor))
                {
                    Vector3 up = new Vector3(bay.position.x, upstairsFloor + eye, bay.position.z);
                    spots.Add(("UPSTAIRS, directly over the car, looking down",
                               up, (carAim - up).normalized));
                    Vector3 up2 = new Vector3(bay.position.x - 1.2f, upstairsFloor + eye,
                                              bay.position.z + 1.4f);
                    spots.Add(("UPSTAIRS, over the bench end of the garage",
                               up2, Vector3.down));
                }
            }

            foreach (var (name, at, look) in spots)
            {
                sb.AppendLine("FROM " + name + "  eye " + V(at));
                var picked = interactor.PickFrom(targets, at, look.sqrMagnitude > 0.0001f ? look.normalized : Vector3.forward);
                sb.AppendLine("  OFFERED (looking as described): " +
                              (picked != null ? Path(picked.transform) : "nothing"));
                // Per target, the cone is taken OUT of the question: each line
                // is answered as if the player turned and looked straight at
                // that one thing. Otherwise "outside the cone" hides whether
                // range and sight would have let it through, which is the half
                // this probe exists to measure.
                foreach (var t in targets)
                {
                    Vector3 to = t.FocusPoint - at;
                    float d = to.magnitude;
                    string why = d > t.range ? "out of range"
                               : interactor.Blocked(t, at, to / d, d, targets) ? "BLOCKED (no line of sight)"
                               : "REACHABLE if looked at";
                    sb.AppendLine("    " + Pad(Label(t), 26) +
                                  " d " + d.ToString("0.00") + " m  -> " + why);
                }
                sb.AppendLine();
            }
        }

        /// <summary>Bay hooks are all called "Hook" or "Bay_...", so the parent
        /// is the name worth printing — except the raise rig, which is a second
        /// hook on the same bay and would otherwise be indistinguishable from
        /// the car itself in a report about which one you were offered.</summary>
        static string Label(FootTarget t) =>
            t.transform.parent == null ? t.name
            : t.name == "RaiseHook" ? t.transform.parent.name + "/jack"
            : t.transform.parent.name;
        /// <summary>Everything a ray hits on the way down, deepest last. Uses
        /// RaycastAll rather than stepping a ray, because a stepped ray that
        /// starts inside a collider silently skips it.</summary>
        static List<float> CastDown(StringBuilder sb, string label, Vector3 from)
        {
            var ys = new List<float>();
            var hits = Physics.RaycastAll(from, Vector3.down, 24f, Physics.DefaultRaycastLayers,
                                          QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => b.point.y.CompareTo(a.point.y));
            sb.AppendLine("  " + label + " at x " + from.x.ToString("0.00") + " z " + from.z.ToString("0.00") +
                          " — " + hits.Length + " surface(s)");
            foreach (var h in hits)
            {
                ys.Add(h.point.y);
                sb.AppendLine("      y " + h.point.y.ToString("0.00").PadLeft(6) + "  " + Path(h.collider.transform));
            }
            return ys;
        }

        static string V(Vector3 v) =>
            "(" + v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + v.z.ToString("0.00") + ")";

        static string Pad(string s, int n) => s.Length >= n ? s.Substring(0, n) : s.PadRight(n);

        static string Path(Transform t)
        {
            string p = t.name;
            for (var q = t.parent; q != null; q = q.parent) p = q.name + "/" + p;
            return p;
        }
    }
}
