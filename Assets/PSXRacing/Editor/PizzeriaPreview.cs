using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Photograph the built pizza shop from the player's own eyes.
    ///
    /// Everything this scene does is placed by MEASUREMENT — the spawn off the
    /// door, the order off the pack's own box stack, the car off a raycast for
    /// the pavement — and every one of those fails silently. A player spawned
    /// facing a wall, an order hook on a box behind the counter glass, a car
    /// sunk through the kerb: none of them throw, and all of them look like the
    /// scene never loaded. The first shot is from exactly where the player
    /// wakes up, because that is the frame that has to be right.
    ///
    /// Writes to Screenshots/Pizzeria.
    /// </summary>
    public static class PizzeriaPreview
    {
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(PizzeriaSceneBuilder.ScenePath,
                                                     OpenSceneMode.Single);

            var player = GameObject.Find("Player");
            var shift = Object.FindFirstObjectByType<PizzaShift>();
            var car = GameObject.Find("PlayerCar");
            if (player == null) { Debug.LogError("[PizzaShot] no Player in the scene"); return; }

            Transform head = null;
            foreach (var t in player.GetComponentsInChildren<Transform>(true))
                if (t.name == "Head") head = t;

            Debug.Log("[PizzaShot] player at " + player.transform.position.ToString("0.00") +
                      " facing " + player.transform.eulerAngles.y.ToString("0") +
                      "  eye " + (head != null ? head.position.ToString("0.00") : "?") +
                      "  order " + (shift != null && shift.counterOrder != null
                                    ? shift.counterOrder.name + " @ " +
                                      shift.counterOrder.position.ToString("0.00") : "NONE") +
                      "  car " + (car != null ? car.transform.position.ToString("0.00") : "NONE"));

            // Push the SCENE'S OWN PSXGlobals, rather than inventing lighting
            // here.
            //
            // This tool used to set the uniforms itself, and that is precisely
            // why it certified a shop that the game rendered in solid black: it
            // supplied what the scene had failed to provide, photographed the
            // result, and reported everything fine. A preview that lights the
            // scene for itself is not previewing the scene.
            //
            // PSXGlobals pushes from Update, which edit mode does not run, so it
            // has to be poked by hand — but it has to be the scene's own, and
            // its absence has to be LOUD.
            var globals = Object.FindFirstObjectByType<PSXRacing.PSXGlobals>();
            if (globals == null)
                Debug.LogError("[PizzaShot] the scene has NO PSXGlobals — the game " +
                               "will render this room entirely black. Shots below are " +
                               "meaningless.");
            else globals.Apply();

            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                                      "Screenshots", "Pizzeria");
            Directory.CreateDirectory(dir);

            var eye = head != null ? head.position
                                   : player.transform.position + Vector3.up * FootRig.EyeH;
            var fwd = player.transform.forward;

            // 1. what the player sees the instant the shift starts
            Shoot(dir, "shift_1_spawn", eye, Quaternion.LookRotation(fwd, Vector3.up));

            // 2. the counter, from the player's side of it
            if (shift != null && shift.counterOrder != null)
            {
                var o = shift.counterOrder.position;
                var from = o - fwd * 2.0f + Vector3.up * 0.55f;
                Shoot(dir, "shift_2_counter", from, Quaternion.LookRotation(o - from, Vector3.up));
            }

            // 3. turn round: the way out
            Shoot(dir, "shift_3_doorway", eye, Quaternion.LookRotation(-fwd, Vector3.up));

            // 4. the car on the street, and whether it is standing on it
            if (car != null)
            {
                var c = car.transform.position;
                var from = c + new Vector3(-4.5f, 2.2f, -4.5f);
                Shoot(dir, "shift_4_car", from, Quaternion.LookRotation(c - from, Vector3.up));
            }

            // 5. the block from above, to see the shop in its street
            var over = eye + Vector3.up * 26f;
            Shoot(dir, "shift_5_block", over, Quaternion.Euler(90f, 0f, 0f));

            // 6. THE VERTEX-SNAP A/B, at the resolution the game actually
            //    renders at.
            //
            // This pair exists because the previous version of this tool forced
            // _PSXSnap to 0 before every shot — as do CityPreview, TownPreview,
            // TireFxPreview and HoistPreview — while the game shipped with it
            // on. So every screenshot the look was ever signed off from was of a
            // renderer the player never saw, and the report that came back was
            // "many textures are interfering when moving", which is the snap and
            // nothing else: it moves a vertex in SCREEN space and leaves its
            // depth alone, so two surfaces lying on each other swap order per
            // pixel. It is invisible at 960 lines and vicious at 240, which is
            // the other half of why the tool never caught it.
            //
            // The shots above use whatever the SCENE says (off, now). These two
            // are the evidence, and they are worth keeping: if the artefact ever
            // comes back, this is the pair that shows what it is.
            var dining = Quaternion.LookRotation(fwd, Vector3.up);
            Shader.SetGlobalFloat("_PSXSnap", 1f);
            Shoot(dir, "snap_ON_240", eye, dining, 426, 240);
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shoot(dir, "snap_OFF_240", eye, dining, 426, 240);
            if (globals != null) globals.Apply();

            Debug.Log("[PizzaShot] wrote 7 shots to " + dir +
                      "  (scene vertexSnap = " +
                      (globals != null ? globals.vertexSnap.ToString() : "NO GLOBALS") + ")");
            EditorSceneManager.CloseScene(scene, false);
        }

        static void Shoot(string dir, string name, Vector3 pos, Quaternion rot,
                          int w = 960, int h = 540)
        {
            var go = new GameObject("~pizzaCam");
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.45f, 0.52f);
            // The GAME's clip planes, not a wider pair. Depth precision is one
            // of the two things this tool is now being asked to reproduce.
            cam.nearClipPlane = 0.08f;
            cam.farClipPlane = 160f;
            cam.fieldOfView = FootRig.FovDeg;

            var rt = new RenderTexture(w, h, 24) { filterMode = FilterMode.Point };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
        }
    }
}
