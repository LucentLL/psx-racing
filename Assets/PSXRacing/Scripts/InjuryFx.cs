using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// R1c-A: what the driver sees when a crash hurts them (LifeSim.CrashFates).
    /// The frame at the hit is frozen and smeared (the world camera rendered
    /// small and stretched back up, a second ghost of it beside the first),
    /// then the picture goes dark and the sound with it. A CONCUSSION holds a
    /// few seconds; HOSPITAL and DEATH go fully black first and end the drive
    /// through the pause menu's tow, which banks the leg (the injury rides in
    /// the damage file, DamageBank). Fix pass 1 (owner 11-14: "drive doesn't
    /// have to end after a concussion, but it likely will if car inoperable"):
    /// a CONCUSSION in a car that can still go comes round - the dark lifts,
    /// the smear fades, the controls come back - and the drive goes on; it
    /// ends only when the car cannot go on (the structural FAIL, a seized
    /// engine: the tow) or the player leaves. PSX_CONCUSSION_DRIVE=0: every
    /// concussion ends the drive (R1c-A).
    /// </summary>
    public class InjuryFx : MonoBehaviour
    {
        public static InjuryFx Active { get; private set; }
        /// <summary>Play checks: called instead of ending the drive.</summary>
        public static System.Action<int> EndDriveOverride;
        public static bool CarryOnOn = System.Environment.GetEnvironmentVariable("PSX_CONCUSSION_DRIVE") != "0";
        /// <summary>Concussions the driver came round from this session (play checks).</summary>
        public static int CameRound;

        /// <summary>Whether the hold ends the drive: hospital and death always;
        /// a concussion only in a car that cannot go on.</summary>
        public static bool EndsDrive(int outcome, bool carCannotGo) =>
            !CarryOnOn || outcome >= LifeSim.CrashFates.Hospital || carCannotGo;

        /// <summary>The car cannot go on: the structural FAIL or a seized engine
        /// (the pause menu's TOW CAR HOME test).</summary>
        public static bool CarCannotGo(CarController car)
        {
            if (car == null) return true;
            if (DamageRules.Wrecked(car)) return true;
            var temp = car.GetComponent<EngineTemp>();
            return temp != null && temp.Seized;
        }

        /// <summary>Seconds the driver takes to come round (the dark lifts in the
        /// first, the smear fades over all of it), and when the controls return.</summary>
        public const float WakeSeconds = 2.5f, ControlsBackAt = 0.5f;
        bool waking, controlsBack;
        float tWake, darkAtWake, accelWas = 1f;

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
            if (Active != null)
            {
                Active.outcome = Mathf.Max(Active.outcome, outcome);
                if (Active.waking) Active.Relapse();   // hit again while coming round: dark again
                return;
            }
            var go = new GameObject("InjuryFx");
            var fx = go.AddComponent<InjuryFx>();
            fx.outcome = outcome;
            fx.car = car;
            fx.t0 = Time.unscaledTime;
            Active = fx;
            if (car != null) { fx.accelWas = car.faultAccelMult; car.faultAccelMult = 0f; }   // nobody is driving it now
        }

        void Relapse()
        {
            waking = controlsBack = ended = false;
            t0 = Time.unscaledTime;
            if (car != null) car.faultAccelMult = 0f;
        }

        /// <summary>The concussion lifts: the controls come back after
        /// ControlsBackAt, the dark lifts over a second, the smear (the frame
        /// at the hit) fades over WakeSeconds, then the effect is gone.</summary>
        void Wake()
        {
            float w = Time.unscaledTime - tWake;
            if (!controlsBack && w >= ControlsBackAt)
            {
                controlsBack = true;
                if (car != null && !CarCannotGo(car)) car.faultAccelMult = accelWas;
            }
            float smear = 1f - Mathf.Clamp01(w / WakeSeconds);
            float dark = darkAtWake * (1f - Mathf.Clamp01(w / 1f));
            if (blur.texture != null)
            {
                blur.color = new Color(1f, 1f, 1f, smear * smear);
                ghost.color = new Color(1f, 0.92f, 0.92f, smear * 0.45f);
            }
            black.color = new Color(0f, 0f, 0f, dark);
            AudioListener.volume = Mathf.Clamp01(1f - Mathf.Max(dark, smear * 0.35f));
            if (w >= WakeSeconds) Destroy(gameObject);
        }

        /// <summary>Seconds of the black-out (then the drive ends, or a
        /// concussion in a car that still goes comes round).</summary>
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
            if (waking) { Wake(); return; }
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
                if (!EndsDrive(outcome, CarCannotGo(car)))
                {
                    // the driver comes round in a car that still goes: the drive goes on
                    waking = true;
                    tWake = Time.unscaledTime;
                    darkAtWake = black.color.a;
                    CameRound++;
                    Debug.Log("[Fates] concussion: the driver comes round after " + HoldSeconds.ToString("0") +
                              " s and the car still goes - the drive goes on");
                    return;
                }
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
