using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Renders the cabin overlay and the cockpit binnacle at 1280x720 — the
    /// resolution both canvases scale from — over a flat sky-and-road backdrop.
    ///
    /// The same argument as the touch-panel preview this is modelled on. The
    /// cabin is a dozen fractions of the frame: how deep the roof lining is,
    /// where the A-pillars land, how far right the car sits in a left-hand
    /// driver's view. Every one of them compiles perfectly whatever it is set
    /// to, and the only way to know whether the windscreen is a windscreen or a
    /// letterbox is to look at it. Going through the game to look costs a scene
    /// build and a capture pass per attempt, and comes back at 480 lines with
    /// the whole of Charlotte behind it.
    ///
    /// The backdrop is deliberately FLAT and high-contrast: over a real
    /// photograph of a city a badly-placed pillar hides in the buildings.
    /// </summary>
    public static class CockpitPreview
    {
        [MenuItem("PSX Racing/Preview Cockpit")]
        public static void Dump()
        {
            string outDir = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(outDir);

            foreach (var (label, touch, steer) in new[]
            {
                ("desktop", false, -0.35f),
                ("touch", true, 0f),
            })
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                const int W = 1280, H = 720;
                var camGO = new GameObject("PreviewCam");
                var cam = camGO.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.55f, 0.66f, 0.78f);
                cam.orthographic = true;
                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                cam.targetTexture = rt;

                Backdrop();

                // The touch panel decides whether the cabin draws a steering
                // wheel at all — it has its own, and the player's hand is on
                // that one — so both cases have to be photographed.
                if (touch)
                {
                    var host = new GameObject("TouchControls");
                    var tc = host.AddComponent<TouchControls>();
                    tc.forceShow = true;
                    typeof(TouchControls).GetMethod("Awake",
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance)?.Invoke(tc, null);
                }

                ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);

                var cabin = MakeCanvas("CockpitCanvas", 80).gameObject.AddComponent<CockpitView>();
                cabin.cabin = Art("cabin");
                cabin.wheel = Art("wheel");
                var cluster = MakeCanvas("ClusterCanvas", 90).gameObject.AddComponent<GaugeCluster>();

                // Point every canvas at the preview camera BEFORE anything is
                // built, and pin its scaler.
                //
                // A ScreenSpaceOverlay canvas takes its size from Screen, and
                // in batchmode Screen is whatever hidden 4:3 surface the editor
                // came up with — not the 1280x720 this renders into. So the
                // panel was being laid out for an 1100x830 frame and then
                // stretched unevenly into a 16:9 picture, which is a preview of
                // a resolution nobody plays at. Pinned to the camera at
                // ConstantPixelSize the canvas rect IS the render target, and
                // that is exactly what the shipped ScaleWithScreenSize canvases
                // resolve to on a 1280x720 display — the resolution they take
                // as their reference.
                PinCanvases(cam);
                Canvas.ForceUpdateCanvases();
                // Binnacle FIRST. The cabin hoods whatever the cluster reports,
                // and in the game it picks that up on its next frame — here
                // there is no next frame, so the order is the whole story.
                cluster.Build();
                cabin.PreviewShow(true);
                Report(cabin);

                // The wheel angle is the one moving part in here, and it turns
                // the wrong way exactly as easily as the right way. Posed at a
                // real steering angle rather than at rest, so the picture
                // answers "which way does it go" as well as "where is it".
                var wheelRT = FindDeep(cabin.transform, "Wheel");
                if (wheelRT != null)
                    wheelRT.localRotation = Quaternion.Euler(0f, 0f, -steer * 34f * cabin.steerRatio);

                PinCanvases(cam);
                Canvas.ForceUpdateCanvases();

                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                string path = Path.Combine(outDir, "cockpit_" + label + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log("[Cockpit] wrote " + path);

                Object.DestroyImmediate(tex);
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        /// <summary>Every canvas in the scene onto the preview camera at one
        /// canvas unit per rendered pixel. Called twice: once before anything
        /// is built, because the layout code measures its own rect, and once
        /// after, to catch canvases the panel made for itself.</summary>
        static void PinCanvases(Camera cam)
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (c.renderMode == RenderMode.WorldSpace) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = 10f;
                var s = c.GetComponent<CanvasScaler>();
                if (s != null)
                {
                    s.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                    s.scaleFactor = 1f;
                }
            }
        }

        /// <summary>
        /// The frame the cabin was actually laid out for, and the couple of
        /// numbers derived from it. A picture cannot tell a dial that is small
        /// because the fraction is wrong from one that is small because the
        /// canvas it measured was not the canvas it was drawn into — and those
        /// have opposite fixes.
        /// </summary>
        static void Report(CockpitView cabin)
        {
            var rt = (RectTransform)cabin.transform;
            Debug.Log($"[Cockpit] frame {rt.rect.width:0}x{rt.rect.height:0}  " +
                      $"cabin {(cabin.cabin != null ? cabin.cabin.texture.width + "x" + cabin.cabin.texture.height : "MISSING")}  " +
                      $"wheel {(cabin.wheel != null ? cabin.wheel.texture.width + "x" + cabin.wheel.texture.height : "MISSING")}  " +
                      $"wheelCentre {CockpitView.WheelCentre.x:0},{CockpitView.WheelCentre.y:0} r {CockpitView.WheelRadius:0}");
        }

        /// <summary>The cockpit artwork, straight off disk. The preview does
        /// not go through the scene builder, so it does its own load — and gets
        /// null for a sheet that has not been dropped in yet, which is the
        /// state the picture should show honestly rather than hide.</summary>
        static Sprite Art(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/PSXRacing/Art/Cockpit/" + name + ".png");

        /// <summary>Sky over road, with a lane line down the middle and a
        /// horizon — enough for the aperture to have a shape in it, and flat
        /// enough that nothing in the cabin can hide against it.</summary>
        static void Backdrop()
        {
            var canvas = MakeCanvas("Backdrop", 0);
            Block(canvas, new Color(0.62f, 0.72f, 0.84f), new Vector2(0f, 0.52f), new Vector2(1f, 1f));
            Block(canvas, new Color(0.30f, 0.30f, 0.32f), new Vector2(0f, 0f), new Vector2(1f, 0.52f));
            Block(canvas, new Color(0.16f, 0.34f, 0.16f), new Vector2(0f, 0.42f), new Vector2(0.28f, 0.52f));
            Block(canvas, new Color(0.16f, 0.34f, 0.16f), new Vector2(0.72f, 0.42f), new Vector2(1f, 0.52f));
            Block(canvas, new Color(0.86f, 0.82f, 0.35f), new Vector2(0.49f, 0f), new Vector2(0.51f, 0.5f));
        }

        static void Block(RectTransform parent, Color c, Vector2 min, Vector2 max)
        {
            var go = new GameObject("B");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            var rt = img.rectTransform;
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        static RectTransform MakeCanvas(string name, int order)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1280f, 720f);
            s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            s.matchWidthOrHeight = 0.5f;

            var child = new GameObject("Root", typeof(RectTransform));
            child.transform.SetParent(go.transform, false);
            var rt = (RectTransform)child.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindDeep(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
