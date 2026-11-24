using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE WASH, photographed (spray-fix2, 2026-11-23). MY CARS with its WASH button reached by
    /// each input - a pad's focus walked onto it, a pointer over it, and the phone-wide page a
    /// thumb presses - at the owner's 2000x1077, a wide phone and 16:9; then the car on the
    /// turntable before the wash, during it (water sheeting and streams), at the run-off, just
    /// after (beaded), drying and dry, each as the page shows it and as the viewer's own full
    /// studio frame. The clip and overlap reports and the pad-reach check of LifeHomePreview.Shoot
    /// come with every page shot. Writes Screenshots/wash/. "[Wash] DONE" at the end.
    /// </summary>
    public static partial class LifeHomePreview
    {
        static readonly (string name, int w, int h) WOwner = ("owner_2000x1077", 2000, 1077);
        static readonly (string name, int w, int h) WPhone = ("phone_wide", 1998, 891);
        static readonly (string name, int w, int h) W169 = ("landscape_16x9", 1280, 720);

        static Button FindWash(LifeHomeScreen screen)
        {
            foreach (var b in screen.GetComponentsInChildren<Button>(true))
            {
                var t = b.GetComponentInChildren<Text>(true);
                if (t != null && t.text.StartsWith("WASH")) return b;
            }
            return null;
        }

        static void SaveStudio(string outDir, string name)
        {
            foreach (var v in Object.FindObjectsByType<CarViewer>(FindObjectsInactive.Exclude))
            {
                if (!v.Studio) continue;
                Canvas.ForceUpdateCanvases();
                v.RenderNow();
                var rt = v.StudioTexture;
                if (rt == null) continue;
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(outDir, "studio_" + name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                // how many millimetres of the car one pixel of this frame covers: the body's length on screen
                var cam = typeof(CarViewer).GetField("cam", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(v) as Camera;
                var body = v.BodyRenderer;
                if (cam != null && body != null)
                {
                    var bnd = body.bounds;
                    Vector3 a = bnd.center - body.transform.forward * bnd.extents.magnitude * 0.5f;
                    Vector3 c = bnd.center + body.transform.forward * bnd.extents.magnitude * 0.5f;
                    float dm = Vector3.Distance(a, c);
                    var pa = cam.WorldToViewportPoint(a); var pc = cam.WorldToViewportPoint(c);
                    float px = Vector2.Distance(new Vector2(pa.x * rt.width, pa.y * rt.height), new Vector2(pc.x * rt.width, pc.y * rt.height));
                    float mmPerPx = px > 1f ? dm * 1000f / px : -1f;
                    Debug.Log("[Wash] " + name + ": studio " + rt.width + "x" + rt.height + ", " + mmPerPx.ToString("0.0") + " mm of car per pixel: beads 2-6 mm are " +
                              (2f / mmPerPx).ToString("0.0") + "-" + (6f / mmPerPx).ToString("0.0") + " px, the merged 10 mm " + (10f / mmPerPx).ToString("0.0") +
                              " px; water shown " + v.WaterShown);
                }
                return;
            }
            Debug.Log("[Wash] " + name + ": no studio viewer on the page");
        }

        public static void CaptureWash()
        {
            string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "wash");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);
            LifeSimManager.StartNewGame("VINCE", 25, LifeRules.DefaultJobIndex);
            LifeRules.SeedFallbackCar(LifeSimManager.State);
            var st = LifeSimManager.State;
            st.money = 500;
            var car = st.cars.Find(c => c.id == st.activeCar) ?? st.cars[0];
            car.dirt = 0.7f;
            CarWater.Reset();
            CarWater.ClockOverride = 100f;
            int fails = 0;
            try
            {
                // ---- WASH on every input, before the wash (the car dry and dirty)
                Shoot(outDir, "wash_pad", "garage", sizes: new[] { WOwner }, probe: (screen, where) =>
                {
                    var b = FindWash(screen);
                    if (b == null) { Debug.Log("[Wash] FAIL no WASH button on " + where); fails++; return; }
                    // a pad's focus on it, as MenuNav moves it (the selected state draws)
                    b.Select();
                    b.OnSelect(new BaseEventData(null));
                    Debug.Log("[Wash] pad: WASH selected on " + where + " interactable " + b.interactable +
                              " nav up " + (b.navigation.selectOnUp != null) + " down " + (b.navigation.selectOnDown != null) +
                              " left " + (b.navigation.selectOnLeft != null) + " right " + (b.navigation.selectOnRight != null));
                    SaveStudio(outDir, "0_before");
                });
                Shoot(outDir, "wash_mouse", "garage", sizes: new[] { W169 }, probe: (screen, where) =>
                {
                    var b = FindWash(screen);
                    if (b == null) { Debug.Log("[Wash] FAIL no WASH button on " + where); fails++; return; }
                    b.OnPointerEnter(new PointerEventData(null));
                    Debug.Log("[Wash] mouse: pointer over WASH on " + where);
                });
                Shoot(outDir, "wash_touch", "garage", sizes: new[] { WPhone }, probe: (screen, where) =>
                {
                    var b = FindWash(screen);
                    if (b == null) { Debug.Log("[Wash] FAIL no WASH button on " + where); fails++; return; }
                    var r = b.GetComponent<RectTransform>().rect;
                    float scale = b.GetComponentInParent<Canvas>().rootCanvas.scaleFactor;
                    Debug.Log("[Wash] touch: WASH on " + where + " is " + (r.width * scale).ToString("0") + " x " + (r.height * scale).ToString("0") + " px (a thumb wants 88+ px on a 891-line phone: " +
                              (r.height * scale >= 60f ? "ok" : "SMALL") + ")");
                    // the press itself: the page's own click
                    int money0 = st.money;
                    b.onClick.Invoke();
                    Debug.Log("[Wash] pressed: money " + money0 + " -> " + st.money + ", dirt " + car.dirt.ToString("0.00") + ", wet " + car.wet.ToString("0.00") +
                              " (day " + car.wetDay + " slot " + car.wetSlot + ")");
                    if (st.money != money0 - LifeRules.WashCost || car.dirt > 0f || car.wet < 0.99f) { Debug.Log("[Wash] FAIL the press did not wash"); fails++; }
                });
                // ---- the wash on the turntable (the press above started it at clock 100)
                var stages = new (string name, float t)[]
                {
                    ("1_during", 100f + 2f), ("2_runoff", 100f + CarWater.WashSeconds - 1f), ("3_after", 100f + CarWater.WashSeconds + 2f),
                    ("4_drying", 100f + CarWater.WashSeconds + 100f), ("5_dry", 100f + CarWater.WashSeconds + CarWater.DrySeconds + 5f),
                };
                foreach (var (name, t) in stages)
                {
                    CarWater.ClockOverride = t;
                    Shoot(outDir, "wash_" + name, "garage", sizes: new[] { WOwner }, probe: (screen, where) => SaveStudio(outDir, name));
                }
            }
            finally
            {
                CarWater.ClockOverride = -1f;
                Debug.Log("[Wash] DONE, " + fails + " FAIL(s), " + clipCount + " clip line(s) so far");
            }
        }
    }
}
