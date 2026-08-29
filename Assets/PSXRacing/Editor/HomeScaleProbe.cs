using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Diagnostic: how big is the house really, and does a car fit its garage?
    ///
    /// The pack is not built to real-world scale, and the only features in it
    /// with a known true dimension are its doors — so this measures them, works
    /// out the scale that makes an interior door 2.03 m, then stands the model
    /// up at that scale and RAYCASTS the garage to find the floor, the rear
    /// wall and the side walls. Everything the builder needs to place a car and
    /// a player correctly comes out of here rather than out of a guess.
    ///
    /// Writes PSXRacing_homescale.txt.
    /// </summary>
    public static class HomeScaleProbe
    {
        const string HouseDir = "Assets/PSXRacing/Art/LifeSim/House";
        /// <summary>A US residential interior door: 80 inches.</summary>
        const float RealDoorH = 2.03f;

        public static void Run()
        {
            var sb = new StringBuilder();
            try { Probe(sb); }
            catch (System.Exception e) { sb.AppendLine("PROBE THREW: " + e); }
            System.IO.File.WriteAllText("PSXRacing_homescale.txt", sb.ToString());
            Debug.Log("HOME SCALE PROBE\n" + sb);
        }

        static void Probe(StringBuilder sb)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var visPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HouseDir + "/house_hero.fbx");
            var colPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HouseDir + "/house_hero_colliders.fbx");
            if (visPrefab == null || colPrefab == null) { sb.AppendLine("house FBX missing"); return; }

            // ---- pass 1: unscaled, to read the doors ----
            var probe = (GameObject)Object.Instantiate(visPrefab);
            probe.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var doors = new List<float>();
            float garW = 0f, garH = 0f, garBase = 0f, garX = 0f, garZ = 0f;
            foreach (var r in probe.GetComponentsInChildren<MeshRenderer>(true))
            {
                var b = r.bounds;
                if (r.name.StartsWith("Garage_Door"))
                {
                    float w = Mathf.Max(b.size.x, b.size.z);
                    if (w > garW) { garW = w; garH = b.size.y; garBase = b.min.y; garX = b.center.x; garZ = b.center.z; }
                }
                else if (r.name == "Door" || (r.name.StartsWith("Door_0") && !r.name.Contains("frame")))
                    doors.Add(b.size.y);
            }
            doors.Sort();
            float median = doors.Count > 0 ? doors[doors.Count / 2] : 0f;
            float scale = median > 0.1f ? RealDoorH / median : 1f;
            sb.AppendLine("UNSCALED: " + doors.Count + " interior doors, median h " +
                          median.ToString("0.000"));
            sb.AppendLine("  garage door  w " + garW.ToString("0.00") + "  h " + garH.ToString("0.00") +
                          "  base y " + garBase.ToString("0.00") +
                          "  at x " + garX.ToString("0.00") + " z " + garZ.ToString("0.00"));
            sb.AppendLine("  => SCALE " + scale.ToString("0.0000") +
                          "   (garage door becomes " + (garW * scale).ToString("0.00") + " x " +
                          (garH * scale).ToString("0.00") + ", floor at y " +
                          (garBase * scale).ToString("0.00") + ")");
            Object.DestroyImmediate(probe);

            // ---- pass 2: at scale, with colliders, seated so the GARAGE FLOOR
            //      is y=0 — which is what the builder will do ----
            float lift = -garBase * scale;
            var vis = (GameObject)Object.Instantiate(visPrefab);
            vis.transform.localScale = Vector3.one * scale;
            vis.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            vis.transform.position = new Vector3(0f, lift, 0f);

            var cols = (GameObject)Object.Instantiate(colPrefab);
            cols.transform.localScale = Vector3.one * scale;
            cols.transform.rotation = vis.transform.rotation;
            cols.transform.position = vis.transform.position;
            foreach (var mf in cols.GetComponentsInChildren<MeshFilter>(true))
            {
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }
            // The house is a CLOSED shell and every measurement here is taken
            // from inside it. With the default setting a ray that starts inside
            // a mesh collider sees nothing at all — which is the whole of the
            // first run's "clear / NO FLOOR" report, and would have read as a
            // garage with no walls rather than as a probe with no backfaces.
            bool hitBackfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            Physics.SyncTransforms();

            // re-read the door now that it is scaled and seated
            float dx = 0f, dz = 0f, dw = 0f, dFloor = 0f;
            foreach (var r in vis.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.name.StartsWith("Garage_Door")) continue;
                var b = r.bounds;
                float w = Mathf.Max(b.size.x, b.size.z);
                if (w > dw) { dw = w; dx = b.center.x; dz = b.center.z; dFloor = b.min.y; }
            }
            sb.AppendLine("SCALED: garage door centre x " + dx.ToString("0.00") +
                          " z " + dz.ToString("0.00") + "  width " + dw.ToString("0.00") +
                          "  floor y " + dFloor.ToString("0.00"));
            var vb = Bounds(vis);
            sb.AppendLine("  house bounds " + vb.size + "  centre z " + vb.center.z.ToString("0.00") +
                          "  min y " + vb.min.y.ToString("0.00"));

            // Which way is INTO the garage? Toward the bulk of the house. Asked
            // of the bounds rather than of a ray: the door plane is exactly the
            // place a ray is least trustworthy.
            float eye = dFloor + 0.9f;
            float inward = Mathf.Sign(vb.center.z - dz);
            sb.AppendLine("  inward is z" + (inward > 0 ? "+" : "-"));
            Vector3 inside = new Vector3(dx, eye, dz + inward * 1.2f);
            sb.AppendLine("  INSIDE at " + inside);
            sb.AppendLine("    rear wall:  " + Ray(inside, new Vector3(0f, 0f, inward)));
            sb.AppendLine("    back out:   " + Ray(inside, new Vector3(0f, 0f, -inward)));
            sb.AppendLine("    left  (-x): " + Ray(inside, Vector3.left));
            sb.AppendLine("    right (+x): " + Ray(inside, Vector3.right));
            sb.AppendLine("    floor:      " + Ray(inside, Vector3.down));
            sb.AppendLine("    ceiling:    " + Ray(inside, Vector3.up));

            // Floor height sampled across the bay, so a sloped or stepped slab
            // shows up rather than being averaged away by one lucky ray.
            for (float d = 0.6f; d <= 5.0f; d += 1.1f)
            {
                Vector3 at = new Vector3(dx, dFloor + 1.4f, dz + inward * d);
                sb.AppendLine("    floor at " + d.ToString("0.0") + " m in: " +
                              (Physics.Raycast(at, Vector3.down, out var h, 4f)
                                  ? "y " + h.point.y.ToString("0.000") + " (" + h.collider.name + ")"
                                  : "NO FLOOR"));
            }

            // How far in can a car actually go? Walk the centreline until the
            // floor stops or a wall appears; a 4.3 m car needs that much clear.
            float lastFloor = -1f;
            for (float d = 0.4f; d <= 9f; d += 0.4f)
            {
                Vector3 at = new Vector3(dx, dFloor + 1.4f, dz + inward * d);
                if (!Physics.Raycast(at, Vector3.down, out var h, 4f)) break;
                lastFloor = d;
            }
            sb.AppendLine("  garage floor runs " + lastFloor.ToString("0.0") +
                          " m in from the door plane  (a car is 4.3 m)");

            // ---- INDEPENDENT scale checks ----
            //
            // The scale above is derived from door HEIGHT, so re-measuring door
            // height proves nothing — it comes back as 2.03 by construction,
            // which is exactly what the self-test has been cheerfully asserting.
            // These are the numbers the scale did NOT set, so they are the ones
            // that can disagree with it:
            //
            //   interior door leaf   0.76 m wide  (a 30" door)
            //   ceiling              2.44 m       (an 8-foot ceiling)
            //   garage door          2.44-2.74 m wide
            //
            // If the doors come out square-ish or the ceilings come out at
            // three and a half metres, the pack's doors are not proportioned
            // like real doors and scaling off their height put the whole house
            // out — which a player reads as being three feet tall.
            var dws = new List<float>();
            var dhs = new List<float>();
            foreach (var r in vis.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!(r.name == "Door" || (r.name.StartsWith("Door_0") && !r.name.Contains("frame")))) continue;
                var b = r.bounds;
                dws.Add(Mathf.Max(b.size.x, b.size.z));   // leaf is thin on one axis
                dhs.Add(b.size.y);
            }
            dws.Sort(); dhs.Sort();
            if (dws.Count > 0)
            {
                float mw = dws[dws.Count / 2], mh = dhs[dhs.Count / 2];
                sb.AppendLine("SCALE CHECK: interior door " + mw.ToString("0.000") + " m wide x " +
                              mh.ToString("0.000") + " m high   (real: 0.76 x 2.03)");
                sb.AppendLine("  => width says the house is " + (mw / 0.76f).ToString("0.000") +
                              "x real size");
            }

            // Ceiling, measured in the middle of the biggest interior span we
            // can find: straight up from the garage-door datum, a metre in.
            Vector3 mid = new Vector3(dx, dFloor + 0.30f, dz + inward * 2.0f);
            if (Physics.Raycast(mid, Vector3.up, out var ch, 12f))
                sb.AppendLine("  garage ceiling " + (ch.point.y - dFloor).ToString("0.00") +
                              " m above the slab   (real: 2.4-3.0)");
            else sb.AppendLine("  garage ceiling: no hit");

            sb.AppendLine("  a 6 ft player stands 1.83 m with eyes at ~1.70 m; " +
                          "the builder puts the head at 1.62 m");

            Physics.queriesHitBackfaces = hitBackfaces;
            Object.DestroyImmediate(vis);
            Object.DestroyImmediate(cols);

            ProbePump(sb);
        }

        /// <summary>
        /// The other half of the scale complaint: standing at a pump felt like
        /// being eight feet tall. The builder scales the whole station so a
        /// Fuel_pump object measures PumpHeightM — so if that object carries a
        /// canopy or a price sign, the pump BODY ends up far shorter than the
        /// number says and the player towers over it.
        /// </summary>
        static void ProbePump(StringBuilder sb)
        {
            const string station = "Assets/PSXRacing/Art/GasStation/Gas_station.fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(station);
            if (prefab == null) { sb.AppendLine("\nno gas station FBX to measure"); return; }
            var inst = (GameObject)Object.Instantiate(prefab);
            sb.AppendLine("\nGAS STATION (unscaled):");
            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Fuel_pump")) continue;
                var rs = t.GetComponentsInChildren<MeshRenderer>(true);
                if (rs.Length == 0) continue;
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                sb.AppendLine("  " + t.name + "  size " + b.size + "  (" + rs.Length + " renderers)");
                foreach (var r in rs)
                    sb.AppendLine("      part " + r.name + " h " + r.bounds.size.y.ToString("0.00"));
                break;
            }
            Object.DestroyImmediate(inst);
        }

        static string Ray(Vector3 from, Vector3 dir)
        {
            return Physics.Raycast(from, dir, out var h, 40f)
                ? h.distance.ToString("0.00") + " m (" + h.collider.name + ")" : "clear";
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<MeshRenderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
