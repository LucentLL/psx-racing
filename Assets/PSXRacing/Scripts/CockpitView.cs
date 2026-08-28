using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// The cabin you look out of in COCKPIT view: one artwork sheet for the
    /// interior and another for the steering wheel, with the game's own
    /// instruments sandwiched between them.
    ///
    /// It has to be an OVERLAY rather than geometry, and that is a property of
    /// the car models rather than a shortcut. The pack's shells are solid,
    /// single-sided bodies with their windows painted on — there is no hole to
    /// look out of and no inside to look at. Put a camera in the driver's seat
    /// and every surface around it is back-facing, so the car simply vanishes
    /// and you are a floating eye a metre off the road. Which is also why this
    /// is how the era did it: a PS1 cockpit was a foreground sprite with a hole
    /// in it.
    ///
    /// THREE LAYERS, and the order is the whole design:
    ///
    ///   cabin   sortingOrder 80, under everything. Stretched to fill the
    ///           frame; its transparent windscreen is the aperture.
    ///   gauges  the cluster's own canvas at 90 — not this component's, but
    ///           it lands between these two and that is deliberate.
    ///   wheel   an overriding canvas at 95, ABOVE the instruments and below
    ///           the touch panel at 100. A rim that passes behind the dials is
    ///           a rim behind the dashboard, which is nowhere.
    ///
    /// Where the wheel sits is PUBLISHED, in <see cref="WheelCentre"/> and
    /// <see cref="WheelRadius"/>, and the binnacle is placed against it. That
    /// is the same arrangement the touch panel has with the cluster, and for
    /// the same reason: two components each holding their own copy of where
    /// the other one is disagree the first time either is retuned.
    ///
    /// On a phone this draws no wheel at all. The touch panel already has one —
    /// the one the player's thumb is actually on — and two wheels turning
    /// together a hand apart is worse than either alone.
    /// </summary>
    public class CockpitView : MonoBehaviour
    {
        [Header("Artwork")]
        /// <summary>The cabin sheet: roof, pillars, dash, mirror. Transparent
        /// where the windscreen is. Wired by the scene builder from
        /// Art/Cockpit/cabin.png; null is a supported state and means the view
        /// is a camera in the driver's seat with no bodywork drawn.</summary>
        public Sprite cabin;
        /// <summary>The wheel, alone and centred in its own square. Rotated
        /// about the middle of the image.</summary>
        public Sprite wheel;

        public CarController car;
        /// <summary>The scene's PSX camera. The mirror copies its culling mask
        /// and clear settings so the reflection is the same world, and hangs
        /// off its transform so it inherits the mount and the impact shake.
        /// </summary>
        public Camera worldCamera;

        [Header("Wheel placement, as fractions of the frame")]
        /// <summary>Wheel diameter as a fraction of the frame HEIGHT.</summary>
        public float wheelFrac = 0.62f;
        /// <summary>How far left of centre the wheel sits, as a fraction of the
        /// frame width. The column is centred on the DRIVER, and the driver is
        /// the camera — but the camera sits left of the car's centreline, so
        /// the cabin around it is pushed right and the wheel is not quite
        /// centred in the picture.</summary>
        public float wheelX = 0.045f;
        /// <summary>How far the wheel's centre sits BELOW the bottom of the
        /// frame, as a fraction of its diameter. A wheel fully in shot is a
        /// wheel drawn from the back seat.</summary>
        public float wheelDrop = 0.10f;
        /// <summary>Degrees of wheel rotation per degree of road wheel. About
        /// 11:1 is a quick rack; enough that the wheel is plainly doing
        /// something at the small angles used at speed.</summary>
        public float steerRatio = 11f;

        [Header("Mirror")]
        /// <summary>
        /// A live rear-view render, OFF by default.
        ///
        /// It is a second pass over the whole scene, and it only makes sense if
        /// the cabin artwork has a transparent hole where the mirror glass
        /// should be. A cabin sheet with a mirror painted on it wants this left
        /// alone; one drawn with a window in it can turn it on and set the
        /// rectangle below to match.
        /// </summary>
        public bool mirrorEnabled;
        /// <summary>Glass rectangle, as fractions of the frame: width, then the
        /// centre measured from the top-centre of the screen.</summary>
        public float mirrorWidth = 0.20f, mirrorX = 0.28f, mirrorY = 0.08f;
        /// <summary>How far back the mirror can see. Short on purpose: the
        /// question a mirror answers is who is right behind you.</summary>
        public float mirrorFarClip = 150f;

        /// <summary>
        /// Where the wheel ended up, in canvas units from the bottom left of
        /// the frame, and how big it is. Empty radius means no wheel is drawn —
        /// on a phone, or with no artwork for one — and the binnacle falls back
        /// to its own corner layout.
        /// </summary>
        public static Vector2 WheelCentre { get; private set; }
        public static float WheelRadius { get; private set; }

        RectTransform root;
        RectTransform wheelRT;
        RawImage mirrorImg;
        Camera mirrorCam;
        RenderTexture mirrorRT;

        int builtHeight = -1;
        float builtWidth = -1f;
        bool builtTouch;
        Sprite builtCabin, builtWheel;
        bool shown;
        float lastWheelDeg = float.NaN;

        /// <summary>Mirror render target, in pixels. Tiny and staying that way:
        /// this is a second full pass over the scene, and every pixel of it is
        /// displayed in a strip a finger wide.</summary>
        const int MirrorW = 128, MirrorH = 44;

        void Start() => Build();

        void OnDestroy()
        {
            if (mirrorRT != null) { mirrorRT.Release(); Destroy(mirrorRT); }
            // Statics outlive the scene that set them. Leaving a wheel
            // published after this cabin is gone would have the next cluster
            // lay its binnacle out around a steering wheel that no longer
            // exists — and PlayerPrefs aside, a race restart is exactly this
            // sequence.
            WheelRadius = 0f;
            WheelCentre = Vector2.zero;
        }

        /// <summary>
        /// Build or rebuild the cabin. Idempotent and safe outside play mode,
        /// so the preview tool can photograph the view without entering it —
        /// which is the only way any of the placement numbers get checked,
        /// because every way they can be wrong is visual.
        /// </summary>
        public void Build()
        {
            var rt = transform as RectTransform;
            if (rt == null) return;
            int h = Mathf.RoundToInt(rt.rect.height);
            float w = rt.rect.width;
            if (h < 32) { h = 720; w = 1280f; }
            bool touch = TouchControls.Instance != null && TouchControls.Instance.Visible;

            if (root != null && h == builtHeight && Mathf.Approximately(w, builtWidth)
                && touch == builtTouch && cabin == builtCabin && wheel == builtWheel) return;
            builtHeight = h; builtWidth = w; builtTouch = touch;
            builtCabin = cabin; builtWheel = wheel;

            if (root != null) DestroyNow(root.gameObject);

            var rootGO = new GameObject("Cabin", typeof(RectTransform));
            rootGO.transform.SetParent(transform, false);
            root = (RectTransform)rootGO.transform;
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero; root.offsetMax = Vector2.zero;

            if (cabin != null) BuildCabin();
            if (mirrorEnabled) BuildMirror(w, h);
            // No wheel on a phone: the touch panel has the real one.
            if (!touch && wheel != null) BuildWheel(w, h);
            else { WheelRadius = 0f; WheelCentre = Vector2.zero; }

            // Built HIDDEN. Every other view in the game is a view of the car
            // from outside it, and a cabin left switched on because nothing had
            // asked for it yet would be pasted over the first frame of a chase
            // camera. Update turns it on the moment the cockpit is selected.
            shown = false;
            root.gameObject.SetActive(false);
        }

        /// <summary>Show or hide the cabin outside play mode, rebuilding it
        /// first. The preview tool photographs the view and nothing here runs
        /// on its own in edit mode.</summary>
        public void PreviewShow(bool on)
        {
            Build();
            shown = on;
            if (root != null) root.gameObject.SetActive(on);
        }

        static void DestroyNow(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        /// <summary>
        /// The cabin sheet, STRETCHED corner to corner with its aspect ignored.
        ///
        /// Deliberate. Preserving the aspect means either letterboxing — which
        /// shows the road through the gap where the door card should be, on the
        /// one view whose entire job is to have bodywork around the edge — or
        /// cropping, which eats the dash on a tall screen and the pillars on a
        /// wide one. A cockpit stretched a few percent wide is a cockpit; a
        /// cockpit with daylight down one side is a bug.
        /// </summary>
        void BuildCabin()
        {
            var go = new GameObject("Sheet");
            go.transform.SetParent(root, false);
            var img = go.AddComponent<Image>();
            img.sprite = cabin;
            img.type = Image.Type.Simple;
            img.preserveAspect = false;
            img.raycastTarget = false;
            var r = img.rectTransform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// The wheel, on a canvas of its own that sorts ABOVE the instrument
        /// cluster's.
        ///
        /// A nested Canvas with overrideSorting is the only way to get one
        /// child of this hierarchy in front of a different canvas entirely —
        /// and it has to be in front, because the rim of a real wheel crosses
        /// the bottom of the binnacle and a rim that passes behind the dials is
        /// a rim behind the dashboard.
        /// </summary>
        void BuildWheel(float w, float h)
        {
            var layer = new GameObject("WheelLayer", typeof(RectTransform));
            layer.transform.SetParent(root, false);
            var lrt = (RectTransform)layer.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var canvas = layer.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = WheelSortingOrder;

            float d = h * wheelFrac;
            var go = new GameObject("Wheel");
            go.transform.SetParent(layer.transform, false);
            var img = go.AddComponent<Image>();
            img.sprite = wheel;
            img.raycastTarget = false;
            wheelRT = img.rectTransform;
            wheelRT.anchorMin = wheelRT.anchorMax = new Vector2(0.5f, 0f);
            wheelRT.pivot = new Vector2(0.5f, 0.5f);
            wheelRT.anchoredPosition = new Vector2(-w * wheelX, -d * wheelDrop);
            wheelRT.sizeDelta = new Vector2(d, d);

            // Published from the bottom LEFT, because that is the corner the
            // cluster measures its own layout from.
            WheelCentre = new Vector2(w * 0.5f - w * wheelX, -d * wheelDrop);
            WheelRadius = d * 0.5f;
        }

        /// <summary>Above the cluster (90), below the touch panel (100).</summary>
        public const int WheelSortingOrder = 95;

        void BuildMirror(float w, float h)
        {
            float mw = w * mirrorWidth;
            float mh = mw * MirrorH / (float)MirrorW;
            var pos = new Vector2(w * mirrorX, -h * mirrorY);

            if (mirrorRT == null)
            {
                mirrorRT = new RenderTexture(MirrorW, MirrorH, 16)
                {
                    name = "MirrorRT",
                    filterMode = FilterMode.Point,
                    antiAliasing = 1,
                };
                mirrorRT.Create();
            }

            var glassGO = new GameObject("MirrorGlass");
            glassGO.transform.SetParent(root, false);
            mirrorImg = glassGO.AddComponent<RawImage>();
            mirrorImg.texture = mirrorRT;
            mirrorImg.raycastTarget = false;
            // Flipped in X, because that is what a mirror does. Without it the
            // car overtaking on your left appears on your right, which is
            // worse than having no mirror at all.
            mirrorImg.uvRect = new Rect(1f, 0f, -1f, 1f);
            var grt = mirrorImg.rectTransform;
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 1f);
            grt.pivot = new Vector2(0.5f, 0.5f);
            grt.anchoredPosition = pos;
            grt.sizeDelta = new Vector2(mw, mh);
            // BEHIND the cabin sheet, so the artwork's own bezel frames it and
            // only the hole drawn in that artwork shows any of it.
            glassGO.transform.SetAsFirstSibling();

            EnsureMirrorCamera();
        }

        /// <summary>
        /// The camera behind the glass: parented to the world camera, turned to
        /// face backwards, and rendering the same world at a hundred and
        /// twenty-eighth of the width.
        ///
        /// Parented rather than placed. In cockpit view the world camera is
        /// hard-mounted to the car, so following it costs nothing and gets the
        /// impact shake for free — a mirror that stays steady while the cabin
        /// is thrown about is a hole in the illusion at exactly the moment the
        /// player is paying attention.
        /// </summary>
        void EnsureMirrorCamera()
        {
            if (worldCamera == null || mirrorRT == null) return;
            if (mirrorCam != null) return;
            // Not in edit mode. The preview tool builds this cabin to
            // photograph it, and a spare camera spawned into a scene by a
            // preview tool is the kind of thing that gets saved and then
            // wondered about.
            if (!Application.isPlaying) return;

            var go = new GameObject("MirrorCamera");
            go.transform.SetParent(worldCamera.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.02f, -0.05f);
            go.transform.localRotation = Quaternion.Euler(4f, 180f, 0f);

            mirrorCam = go.AddComponent<Camera>();
            mirrorCam.CopyFrom(worldCamera);
            // CopyFrom brings the target texture and the tag with it, and a
            // second MainCamera in the scene is how Camera.main starts
            // returning the wrong one.
            go.tag = "Untagged";
            mirrorCam.targetTexture = mirrorRT;
            mirrorCam.aspect = MirrorW / (float)MirrorH;
            mirrorCam.fieldOfView = 30f;
            mirrorCam.nearClipPlane = 0.3f;
            mirrorCam.farClipPlane = Mathf.Min(worldCamera.farClipPlane, mirrorFarClip);
            mirrorCam.depth = worldCamera.depth - 1f;
            mirrorCam.useOcclusionCulling = false;
            mirrorCam.allowHDR = false;
            mirrorCam.allowMSAA = false;
            var listener = go.GetComponent<AudioListener>();
            if (listener != null) DestroyNow(listener.gameObject);
            go.SetActive(false);
        }

        void Update()
        {
            Build();

            bool want = ChaseCamera.Current == ChaseCamera.View.Cockpit;
            if (want != shown)
            {
                shown = want;
                if (root != null) root.gameObject.SetActive(want);
                // The mirror is a second render of the whole scene. It exists
                // only while it is being looked at.
                if (mirrorCam != null) mirrorCam.gameObject.SetActive(want);
            }
            if (!want || car == null || wheelRT == null) return;

            // Driven by the front ROAD wheel angle, not by the raw input, so it
            // lags and self-centres exactly as the car does.
            float deg = -car.SteerAngleDeg * steerRatio;
            if (float.IsNaN(lastWheelDeg) || Mathf.Abs(deg - lastWheelDeg) > 0.15f)
            {
                lastWheelDeg = deg;
                wheelRT.localRotation = Quaternion.Euler(0f, 0f, deg);
            }
        }
    }
}
