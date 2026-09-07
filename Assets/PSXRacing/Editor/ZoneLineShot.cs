using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The zone line, photographed the way the PLAYER meets it — and counted.
    ///
    /// The first version of this tool stood a temp camera 25 m OUTSIDE each
    /// line looking in, at a driver's eye, 960x540, FOV 60, with no fog
    /// globals applied. Everything about that framing was the wrong way
    /// round: the player sees the line from INSIDE the zone on the way out,
    /// from the chase rig, at 240 or 480 lines, through the game's own
    /// camera. A row of three-pixel specks passed its only review because the
    /// review was taken at twice the resolution from the side nobody drives in
    /// from. "I still don't see a barrier line."
    ///
    /// Now: for each line a virtual car is stood on the road D metres inside
    /// the zone heading OUT; the scene's own PSXCamera is put where
    /// ChaseCamera would put it (5.4 m back, 1.8 m up, looking at the car's
    /// waist 1.5 m ahead, 58 deg); the scene's PSXGlobals are applied so the
    /// fog fade is the baked hour's; and the frame is rendered at the game's
    /// own line counts into point-filtered targets.
    ///
    /// AND IT ASSERTS. A picture nobody opens is a test nobody ran: each frame
    /// is rendered twice, with the ZoneLine row on and off, and the pixels
    /// that differ are counted. Below the thresholds at the top of the class
    /// it logs "[ZoneLine] FAIL", which tools/zone-shots.ps1 greps for. The
    /// diff mask is written beside each frame so a number can be checked by
    /// eye. The scene is never saved.
    ///
    /// NOT -nographics: a null device reads back black, which is zero
    /// differing pixels and a FAIL for the wrong reason.
    /// </summary>
    public static class ZoneLineShot
    {
        // ---- thresholds: ESTIMATES from the geometry, to be calibrated ----
        // From the chase rig the 2.8 m x 11-13 m curtain plus its posts should
        // cover ~900 px at 40 m / 240 lines and ~140 px at 100 m / 240 lines;
        // the old knots gave ~10-20 and ~0. Set at roughly two thirds and half
        // of the estimate. After the first real render, set each to about half
        // of what it measured, so a regression trips it and a retune does not.
        /// <summary>Differing pixels wanted at 40 m, 240 lines, baked hour.</summary>
        const int MinPixels40m240 = 30; /* measured 66-80 for the dotted line at 240 lines; half the minimum */
        /// <summary>Differing pixels wanted at 100 m, 240 lines, baked hour.</summary>
        const int MinPixels100m240 = 5; /* measured 10-20 */
        /// <summary>The same 40 m frame at Noon — the daylight-sky case the
        /// additive line failed against. Same geometry, so the same bar.</summary>
        const int MinPixels40mNoon240 = 30; /* measured 66-80; opaque dots do not care about the sky */
        /// <summary>A pixel "differs" when any channel moves by more than this
        /// out of 255: above the dither's own noise (a 6-bit step is 4/255,
        /// and PSXBlit's Bayer adds a step or two) and below the faintest
        /// tint the curtain's top lays on the sky.</summary>
        const int DiffThreshold = 24;

        /// <summary>Distances INSIDE the zone the virtual car is stood at:
        /// the far approach, the distance at which the cue text turns to
        /// "THE LINE", and the last look before the crossing.</summary>
        static readonly float[] Distances = { 100f, 40f, 12f };
        /// <summary>The game's own line counts — RETRO and SHARP (the
        /// default); CLASSIC's 360 lies between them.</summary>
        static readonly int[] LineCounts = { 240, 480 };
        /// <summary>The arrival shot kept from the first version: 25 m outside
        /// the line at a driver's eye, looking in.</summary>
        const float ArrivalDistance = 25f, ArrivalEye = 1.2f;

        // ---- the chase rig: ChaseCamera's defaults, read off the scene's own
        // component where there is one so a retune of the rig moves these
        // shots with it. LengthFit's 0.9-1.3 on the distance is ignored.
        const float ChaseDistance = 5.4f, ChaseHeight = 1.8f, ChaseLookHeight = 0.9f,
                    ChaseLookAhead = 1.5f, ChaseFov = 58f;

        static bool failed;

        [MenuItem("PSX Racing/Shoot Zone Lines")]
        public static void Run()
        {
            failed = false;
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                                      "Screenshots", "ZoneLines");
            Directory.CreateDirectory(dir);

            Shoot(PSXRacingBuilder.NeighborhoodScenePath, "junction", dir);
            Shoot(PSXRacingBuilder.TownScenePath, "town_west", dir, "TownEdgeW");
            Shoot(PSXRacingBuilder.TownScenePath, "town_east", dir, "TownEdgeE");

            if (failed)
                Debug.LogError("[ZoneLine] FAIL — a line is below its pixel threshold; " +
                               "see the counts above and the _diff.png masks in " + dir);
            else Debug.Log("[ZoneLine] every line is above threshold");
        }

        static void Fail(string why)
        {
            failed = true;
            Debug.LogError("[ZoneLine] FAIL " + why);
        }

        static void Require(int n, int min, string tag, float d, int lines, string variant)
        {
            if (n >= min) return;
            Fail(Label(tag, d, lines, variant) + ": " + n + " px, wanted >= " + min);
        }

        static string Label(string tag, float d, int lines, string variant) =>
            tag + (variant.Length > 0 ? " " + variant : "") + " " +
            Mathf.RoundToInt(d) + "m @" + lines;

        static void Shoot(string scenePath, string tag, string dir, string edgeName = null)
        {
            if (!File.Exists(scenePath)) { Debug.LogWarning("[ZoneLine] no scene " + scenePath); return; }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            Town.TownEdge edge = null;
            foreach (var e in Object.FindObjectsByType<Town.TownEdge>(FindObjectsSortMode.None))
                if (edgeName == null || e.name == edgeName) { edge = e; break; }
            if (edge == null) { Fail(tag + ": no TownEdge in " + scenePath); return; }

            var line = edge.transform.Find("ZoneLine");
            if (line == null)
            {
                // The neighbourhood's row hangs off the map root beside the
                // volume rather than under it. Only one scene is loaded here,
                // so a name search is safe in a way it is not in the self-test.
                var go = GameObject.Find("ZoneLine");
                line = go != null ? go.transform : null;
            }
            if (line == null) { Fail(tag + ": no ZoneLine row"); return; }

            // THE SCENE'S OWN CAMERA, so the clip planes, the URP renderer and
            // the culling mask are the game's. A temp camera was the first
            // version's third mistake.
            var camGO = GameObject.Find("PSXCamera");
            var cam = camGO != null ? camGO.GetComponent<Camera>() : null;
            if (cam == null) { Fail(tag + ": scene has no PSXCamera"); return; }
            var chase = camGO.GetComponent<ChaseCamera>();
            float back = chase != null ? chase.distance : ChaseDistance;
            float high = chase != null ? chase.height : ChaseHeight;
            float look = chase != null ? chase.lookHeight : ChaseLookHeight;
            float fov = chase != null ? chase.baseFOV : ChaseFov;

            // The baked hour's fog, ambient and snap flag. Nothing ticks in
            // batch mode, so the ExecuteAlways Update never runs this for us
            // — and it is applied as the scene has it, snap included: a
            // preview that overrides a global is not previewing the game.
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (globals != null) globals.Apply();

            Vector3 inward = edge.inward.normalized;
            Vector3 outward = -inward;

            // ---- the departure approach: inside the zone, heading out ----
            foreach (float d in Distances)
            {
                ChaseRig(line, inward, d, back, high, look, out var eye, out var rot);
                foreach (int lines in LineCounts)
                {
                    int n = ShootAndCount(cam, line, dir, tag, d, lines, eye, rot, fov, "");
                    if (lines == 240 && Mathf.Approximately(d, 40f))
                        Require(n, MinPixels40m240, tag, d, lines, "");
                    if (lines == 240 && Mathf.Approximately(d, 100f))
                        Require(n, MinPixels100m240, tag, d, lines, "");
                }
            }

            // ---- arrival: 25 m outside, driver's eye, looking in ----
            {
                Vector3 eye = OnRoad(line.position + outward * ArrivalDistance, line.position.y)
                              + Vector3.up * ArrivalEye;
                var rot = Quaternion.LookRotation((line.position - eye).normalized + Vector3.up * 0.02f,
                                                  Vector3.up);
                ShootAndCount(cam, line, dir, tag, ArrivalDistance, 480, eye, rot, fov, "arrival");
            }

            // ---- Noon: the daylight-sky case ----
            // TimeOfDay.Apply is field writes on the sun and the globals, a
            // sky material instance, and two SetAll loops over lists that are
            // empty in edit mode — cheap. LAST, because it changes the scene;
            // the baked hour is put back after and the scene is never saved.
            if (globals != null)
            {
                TimeOfDay.Apply(TimeOfDay.Noon, globals.sun);
                globals.Apply();
                ChaseRig(line, inward, 40f, back, high, look, out var eye, out var rot);
                int n = ShootAndCount(cam, line, dir, tag, 40f, 240, eye, rot, fov, "noon");
                Require(n, MinPixels40mNoon240, tag, 40f, 240, "noon");
                TimeOfDay.Apply(TimeOfDay.Sunset, globals.sun);
                globals.Apply();
            }
        }

        /// <summary>Where ChaseCamera.Follow would put the eye for a car D
        /// metres inside the zone heading out: behind it along `inward`,
        /// looking at its waist a little ahead of it. The car is seated on the
        /// road by raycast — the neighbourhood's street falls five metres
        /// between the 100 m mark and the line.</summary>
        static void ChaseRig(Transform line, Vector3 inward, float d, float back, float high,
                             float look, out Vector3 eye, out Quaternion rot)
        {
            Vector3 car = OnRoad(line.position + inward * d, line.position.y);
            eye = car + inward * back + Vector3.up * high;
            Vector3 at = car + Vector3.up * look - inward * ChaseLookAhead;
            rot = Quaternion.LookRotation(at - eye, Vector3.up);
        }

        /// <summary>The road surface under a point, ignoring triggers; the
        /// fallback is the line's own height, which is right on the flat.</summary>
        static Vector3 OnRoad(Vector3 p, float fallbackY)
        {
            bool found = Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var hit, 90f, ~0,
                                         QueryTriggerInteraction.Ignore);
            return new Vector3(p.x, found ? hit.point.y : fallbackY, p.z);
        }

        /// <summary>Render the frame twice — line on, line off — write the ON
        /// frame and the diff mask, and return how many pixels the line
        /// changed.</summary>
        static int ShootAndCount(Camera cam, Transform line, string dir, string tag, float d,
                                 int lines, Vector3 eye, Quaternion rot, float fov, string variant)
        {
            var on = Render(cam, lines, eye, rot, fov);
            line.gameObject.SetActive(false);
            var off = Render(cam, lines, eye, rot, fov);
            line.gameObject.SetActive(true);

            var a = on.GetPixels32();
            var b = off.GetPixels32();
            var m = new Color32[a.Length];
            int n = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int diff = Mathf.Max(Mathf.Abs(a[i].r - b[i].r),
                                     Mathf.Abs(a[i].g - b[i].g),
                                     Mathf.Abs(a[i].b - b[i].b));
                bool hit = diff > DiffThreshold;
                if (hit) n++;
                m[i] = hit ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
            }
            var mask = new Texture2D(on.width, on.height, TextureFormat.RGB24, false);
            mask.SetPixels32(m);
            mask.Apply();

            string stem = "zoneline_" + tag + (variant.Length > 0 ? "_" + variant : "") +
                          "_" + Mathf.RoundToInt(d) + "m_" + lines;
            File.WriteAllBytes(Path.Combine(dir, stem + ".png"), on.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(dir, stem + "_diff.png"), mask.EncodeToPNG());
            Object.DestroyImmediate(on);
            Object.DestroyImmediate(off);
            Object.DestroyImmediate(mask);
            Debug.Log("[ZoneLine] " + Label(tag, d, lines, variant) + ": " + n + " px");
            return n;
        }

        /// <summary>One frame through the scene's camera at the given line
        /// count, point-filtered, the way PSXScreenshotTool.Shot does it.
        /// Width from the lines at 16:9 — the owner's phone is 2.17:1, which
        /// only adds sky at the sides; the curtain's pixel count keys off the
        /// LINES. The camera is put back exactly as it was.</summary>
        static Texture2D Render(Camera cam, int lines, Vector3 eye, Quaternion rot, float fov)
        {
            int w = Mathf.RoundToInt(lines * 16f / 9f) & ~1;
            var oldPos = cam.transform.position;
            var oldRot = cam.transform.rotation;
            float oldFov = cam.fieldOfView;
            var oldTarget = cam.targetTexture;
            cam.transform.SetPositionAndRotation(eye, rot);
            cam.fieldOfView = fov;

            var rt = new RenderTexture(w, lines, 24, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Point,
                antiAliasing = 1,
            };
            rt.Create();
            var request = new RenderPipeline.StandardRequest();
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                request.destination = rt;
                RenderPipeline.SubmitRenderRequest(cam, request);
            }
            else
            {
                cam.targetTexture = rt;
                cam.Render();
            }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, lines, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, lines), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            cam.targetTexture = oldTarget;
            cam.transform.SetPositionAndRotation(oldPos, oldRot);
            cam.fieldOfView = oldFov;
            rt.Release();
            Object.DestroyImmediate(rt);
            return tex;
        }
    }
}
