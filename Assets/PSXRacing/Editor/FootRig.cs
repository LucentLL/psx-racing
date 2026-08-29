using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The on-foot player: capsule, neck, camera, and the two components that
    /// drive them.
    ///
    /// Extracted from GarageSceneBuilder the moment a SECOND walk-in scene
    /// existed. The height is the reason: a player who is six feet tall in the
    /// garage and five-eight in the pizza shop is a bug nobody would think to
    /// look for, and two copies of a number is how that happens. Everything
    /// about how tall the player is lives here and nowhere else.
    /// </summary>
    public static class FootRig
    {
        /// <summary>Six feet, in metres. The owner asked for a six-foot player
        /// after the first walk-in scene read as waist-height on a door.</summary>
        public const float StandingH = 1.83f;
        /// <summary>Eye height for that. The usual 0.93 of standing height —
        /// eyes are not on top of the head.</summary>
        public const float EyeH = 1.70f;

        /// <summary>
        /// VERTICAL field of view, which is what Unity's property means and the
        /// reason a correctly-scaled house still read as a dolls' house at the
        /// old 60. On a phone held landscape (about 2.2:1) 60 vertical is 104
        /// HORIZONTAL — a fisheye that pushes every wall away and makes the
        /// floor rush off. 52 is about 94 horizontal there and 82 on a 16:9
        /// screen, which is where every other first-person game sits.
        /// </summary>
        public const float FovDeg = 52f;

        public static GameObject Build(Vector3 at, float yawDeg, out Camera cam)
        {
            var go = new GameObject("Player");
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);

            var body = go.AddComponent<CharacterController>();
            body.height = StandingH;
            // Slimmer than a real pair of shoulders, deliberately. 0.32 gives
            // a 64 cm capsule, and an interior door LEAF is 76 cm before its
            // frame and stops eat into it — which left about five centimetres
            // either side and a bathroom doorway that could catch and hold the
            // player. A first-person body has no visible width to contradict
            // this, so the narrower one costs nothing and clears everything.
            body.radius = 0.26f;
            body.center = new Vector3(0f, StandingH * 0.5f + 0.02f, 0f);
            body.slopeLimit = 50f;
            // Low. A parked car's collider starts about 22 cm off the floor, and
            // the default step height is enough to walk straight up onto the
            // bonnet of one — which is the sort of thing a player finds in the
            // first thirty seconds and never unsees.
            body.stepOffset = 0.15f;

            var headGO = new GameObject("Head");
            headGO.transform.SetParent(go.transform, false);
            headGO.transform.localPosition = new Vector3(0f, EyeH, 0f);

            var camGO = new GameObject("PSXCamera");
            camGO.tag = "MainCamera";
            camGO.transform.SetParent(headGO.transform, false);
            cam = camGO.AddComponent<Camera>();
            cam.fieldOfView = FovDeg;
            cam.nearClipPlane = 0.08f;
            cam.farClipPlane = 160f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.63f, 0.72f, 0.83f);
            camGO.AddComponent<AudioListener>();

            var walk = go.AddComponent<PSXRacing.OnFoot.FirstPersonWalk>();
            walk.head = headGO.transform;

            var interactor = go.AddComponent<PSXRacing.OnFoot.FootInteractor>();
            interactor.eye = camGO.transform;

            return go;
        }

        /// <summary>
        /// The PSX display chain: render the world into a low-line texture and
        /// BLIT IT BACK to the screen.
        ///
        /// Both halves are load-bearing and the second one is easy to forget.
        /// PSXCameraOutput takes the camera OFF the screen — it renders into a
        /// RenderTexture instead — so a scene that adds the component and
        /// nothing else draws no world at all. On WebGL the framebuffer is not
        /// cleared between frames either, so what you get is the previous
        /// scene's pixels sitting there with the new scene's overlay UI on top:
        /// the pizza shop shipped exactly like that, and it reads as "the menu
        /// is still up and there is a walk button over it", which is how it was
        /// reported. The garage had all of this and the new scene copied only
        /// the first line of it.
        ///
        /// Here rather than in either scene builder for the same reason the
        /// player is: two walk-in scenes already exist and a third will not get
        /// a second chance to remember the blit.
        /// </summary>
        public static string BuildDisplay(Camera cam, string matDir)
        {
            var output = cam.gameObject.AddComponent<PSXCameraOutput>();
            output.height = PSXQuality.Height;

            // Clears the real framebuffer to black behind the blit, so nothing
            // from the scene before survives in the letterbox bars.
            var outCamGO = new GameObject("OutputCamera");
            var outCam = outCamGO.AddComponent<Camera>();
            outCam.clearFlags = CameraClearFlags.SolidColor;
            outCam.backgroundColor = Color.black;
            outCam.cullingMask = 0;
            outCam.depth = 50f;

            var canvasGO = new GameObject("DisplayCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;
            canvasGO.AddComponent<CanvasScaler>();

            var rawGO = new GameObject("PSXDisplay");
            rawGO.transform.SetParent(canvasGO.transform, false);
            var raw = rawGO.AddComponent<RawImage>();
            var fitter = rawGO.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 16f / 9f;
            var rrt = raw.rectTransform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;

            var blitShader = Shader.Find("PSX/Blit");
            if (blitShader != null)
            {
                var blit = AssetDatabase.LoadAssetAtPath<Material>(matDir + "/Blit.mat");
                if (blit == null)
                {
                    blit = new Material(blitShader);
                    AssetDatabase.CreateAsset(blit, matDir + "/Blit.mat");
                }
                blit.shader = blitShader;
                raw.material = blit;
            }
            output.display = raw;
            return output.height + " lines";
        }

        /// <summary>
        /// Sun, sky and — the part that matters — PSXGlobals.
        ///
        /// PSX/Lit does not read Unity's lights. It reads GLOBAL SHADER
        /// UNIFORMS (_PSXLightDir, _PSXLightColor, _PSXAmbient, _PSXFogColor,
        /// _PSXFogNear/_PSXFogFar), and PSXGlobals is the only thing in the game
        /// that pushes them. A scene with a Light and no PSXGlobals leaves every
        /// one of them at ZERO: ambient black, sun black, and a fog that is
        /// black from zero metres. The result is not "dark", it is a perfectly
        /// working scene rendered entirely in black — which is how the pizza
        /// shop shipped, with the interaction prompts readable over a void.
        ///
        /// This is the SECOND thing the new walk-in scene copied only half of,
        /// after the display blit, so it lives here with the rest of the rig
        /// now. A scene builder should not be able to produce a room the player
        /// cannot see.
        /// </summary>
        public static GameObject BuildLighting(string matDir, bool indoors)
        {
            var go = new GameObject("Sun");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.93f, 0.80f);
            light.intensity = 1.1f;
            light.shadows = LightShadows.None;
            go.transform.rotation = Quaternion.Euler(38f, 145f, 0f);

            var globals = go.AddComponent<PSXGlobals>();
            globals.sun = light;
            globals.ambient = new Color(0.50f, 0.49f, 0.52f);
            globals.fogColor = new Color(0.63f, 0.72f, 0.83f);
            // Indoors the fog has to start beyond the far wall or the back of
            // the room washes out; a shop is fifteen metres deep and the street
            // outside its window is another forty.
            globals.fogNear = indoors ? 160f : 70f;
            globals.fogFar = indoors ? 400f : 220f;

            var skyShader = Shader.Find("PSX/Sky");
            if (skyShader != null)
            {
                string p = matDir + "/HomeSky.mat";
                var sky = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (sky == null)
                {
                    sky = new Material(skyShader);
                    AssetDatabase.CreateAsset(sky, p);
                }
                sky.shader = skyShader;
                sky.SetColor("_TopColor", new Color(0.25f, 0.44f, 0.75f));
                sky.SetColor("_HorizonColor", new Color(0.70f, 0.78f, 0.87f));
                sky.SetColor("_BottomColor", new Color(0.63f, 0.72f, 0.83f));
                sky.SetFloat("_HorizonSharpness", 1.4f);
                EditorUtility.SetDirty(sky);
                RenderSettings.skybox = sky;
            }
            else RenderSettings.skybox = null;
            return go;
        }
    }
}
