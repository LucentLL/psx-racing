using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// R1c-A: what the driver sees when a crash hurts them (LifeSim.CrashFates).
    /// The frame at the hit is frozen and smeared (the world camera rendered
    /// small and stretched back up, a second ghost of it beside the first),
    /// then the picture goes dark and the sound with it. A CONCUSSION holds a
    /// few seconds and the drive ends with the tow; HOSPITAL and DEATH go
    /// fully black first. The drive ends through the pause menu's tow, which
    /// banks the leg (the injury rides in the damage file, DamageBank).
    /// </summary>
    public class InjuryFx : MonoBehaviour
    {
        public static InjuryFx Active { get; private set; }
        /// <summary>Play checks: called instead of ending the drive.</summary>
        public static System.Action<int> EndDriveOverride;

        public int outcome;
        public float T => Time.unscaledTime - t0;
        float t0;
        bool captured, ended;
        RenderTexture small;
        Canvas canvas;
        RawImage blur, ghost;
        Image black;
        CarController car;

        public static void Begin(CarController car, int outcome)
        {
            if (Active != null) { Active.outcome = Mathf.Max(Active.outcome, outcome); return; }
            var go = new GameObject("InjuryFx");
            var fx = go.AddComponent<InjuryFx>();
            fx.outcome = outcome;
            fx.car = car;
            fx.t0 = Time.unscaledTime;
            Active = fx;
            if (car != null) car.faultAccelMult = 0f;   // nobody is driving it now
        }

        /// <summary>Seconds before the drive ends.</summary>
        public float HoldSeconds => outcome >= LifeSim.CrashFates.Death ? 6f : outcome >= LifeSim.CrashFates.Hospital ? 5f : 4f;

        void Build()
        {
            canvas = LifeSim.MenuKit.Canvas(transform, "InjuryCanvas", 900);
            blur = Full("Blur");
            ghost = Full("Ghost");
            var b = new GameObject("Black");
            b.transform.SetParent(canvas.transform, false);
            black = b.AddComponent<Image>();
            Stretch(black.rectTransform, 0f);
            black.color = new Color(0f, 0f, 0f, 0f);
            black.raycastTarget = false;
        }

        RawImage Full(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(canvas.transform, false);
            var r = go.AddComponent<RawImage>();
            r.raycastTarget = false;
            r.color = new Color(1f, 1f, 1f, 0f);
            Stretch(r.rectTransform, name == "Ghost" ? 0.035f : 0f);
            return r;
        }

        static void Stretch(RectTransform rt, float shift)
        {
            rt.anchorMin = new Vector2(shift, -shift * 0.4f);
            rt.anchorMax = new Vector2(1f + shift, 1f - shift * 0.4f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>The frame at the hit, small (three bilinear halvings smear it).</summary>
        void Capture()
        {
            captured = true;
            var cam = Camera.main;
            if (cam == null) return;
            int w = Mathf.Max(64, Screen.width > 0 ? Screen.width : 1024), h = Mathf.Max(48, Screen.height > 0 ? Screen.height : 768);
            var a = RenderTexture.GetTemporary(w / 3, h / 3, 24);
            var was = cam.targetTexture;
            try
            {
                cam.targetTexture = a;
                cam.Render();
            }
            catch (System.Exception e) { Debug.LogWarning("[InjuryFx] no frame to blur: " + e.Message); }
            finally { cam.targetTexture = was; }
            var b = RenderTexture.GetTemporary(w / 6, h / 6, 0);
            small = new RenderTexture(Mathf.Max(8, w / 12), Mathf.Max(6, h / 12), 0) { filterMode = FilterMode.Bilinear };
            a.filterMode = b.filterMode = FilterMode.Bilinear;
            Graphics.Blit(a, b);
            Graphics.Blit(b, small);
            RenderTexture.ReleaseTemporary(a);
            RenderTexture.ReleaseTemporary(b);
            blur.texture = ghost.texture = small;
        }

        void LateUpdate()
        {
            if (canvas == null) Build();
            if (!captured) Capture();
            float t = T;
            bool hard = outcome >= LifeSim.CrashFates.Hospital;
            float smear = Mathf.Clamp01(t / 0.5f);
            float dark = hard ? Mathf.Clamp01((t - 0.6f) / 1.6f)
                              : Mathf.Clamp01((t - 0.8f) / 2.2f) * (0.82f + 0.12f * Mathf.Sin(t * 5f));
            if (blur.texture != null)
            {
                blur.color = new Color(1f, 1f, 1f, smear);
                ghost.color = new Color(1f, 0.92f, 0.92f, smear * 0.45f);
            }
            black.color = new Color(0f, 0f, 0f, blur.texture != null ? dark : Mathf.Max(dark, smear * 0.6f));
            AudioListener.volume = 1f - Mathf.Clamp01(Mathf.Max(dark, smear * 0.5f));
            if (!ended && t >= HoldSeconds)
            {
                ended = true;
                if (EndDriveOverride != null) EndDriveOverride(outcome);
                else PauseMenu.EndDriveForInjury(car);
            }
        }

        void OnDestroy()
        {
            AudioListener.volume = 1f;
            if (small != null) small.Release();
            if (Active == this) Active = null;
        }

        /// <summary>The picture as the player sees it now, into a PNG (play checks).</summary>
        public bool ShootPng(string path, int w, int h)
        {
            if (canvas == null) return false;
            var go = new GameObject("InjuryShotCam");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 1 << canvas.gameObject.layer;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            var mode = canvas.renderMode;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            canvas.renderMode = mode;
            cam.targetTexture = null;
            Destroy(tex);
            rt.Release();
            Destroy(go);
            return true;
        }
    }
}
