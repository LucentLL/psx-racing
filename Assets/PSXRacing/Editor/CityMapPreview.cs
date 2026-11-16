using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The whole-city track preview (CityMapTiles under RouteMap), photographed: one PNG per venue with
    /// the card's map at the CITY race card's size and at MAIN's pre-race size, on a 1920x1080 frame.
    ///
    ///   -executeMethod PSXRacing.EditorTools.CityMapPreview.Capture
    ///
    /// Venues: UptownLoop, TryonSprint, the first deck run, and every course in the text file
    /// PSX_CITYMAP_CANDS names ("id|name|loop|drag|x,z;x,z;..." a line: courses that are not venues
    /// yet, drawn from their own points). PSX_TRACKMAP_CITY=0 photographs the per-venue pictures
    /// instead (the before). Out: (project)/Screenshots/citymap_(id).png, and a log line per card
    /// with the level and the tiles it used.
    /// </summary>
    public static class CityMapPreview
    {
        const int W = 1920, H = 1080;

        public static void Capture()
        {
            string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(outDir);
            var shots = new List<(TrackCatalog.TrackDef def, List<Vector3> pts)>();
            bool deck = false;
            for (int i = 0; i < TrackCatalog.Count; i++)
            {
                var t = TrackCatalog.At(i);
                if (!t.IsCityRace || t.Reversed) continue;
                if (t.IsDeckRun) { if (deck) continue; deck = true; }
                if (t.id == "UptownLoop" || t.id == "TryonSprint" || t.id == "IndependenceSprint" || t.IsDeckRun) shots.Add((t, null));
            }
            string cands = System.Environment.GetEnvironmentVariable("PSX_CITYMAP_CANDS");
            if (!string.IsNullOrEmpty(cands) && File.Exists(cands))
                foreach (var line in File.ReadAllLines(cands))
                {
                    var f = line.Split('|');
                    if (f.Length < 5) continue;
                    var pts = new List<Vector3>();
                    foreach (var p in f[4].Split(';'))
                    {
                        var xz = p.Split(',');
                        if (xz.Length == 2 && float.TryParse(xz[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                            float.TryParse(xz[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                            pts.Add(new Vector3(x, 0f, z));
                    }
                    if (pts.Count < 2) continue;
                    var def = new TrackCatalog.TrackDef { id = f[0], name = f[1], city = true, cityRoute = "cand_" + f[0], loop = f[2] == "1", laps = 1, roadWidth = 12f };
                    shots.Add((def, pts));
                }
            foreach (var (def, pts) in shots) Shoot(outDir, def, pts);
        }

        static void Shoot(string outDir, TrackCatalog.TrackDef def, List<Vector3> pts)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("PreviewCam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.04f, 0.09f);
            cam.orthographic = true;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            MenuKit.ScreenSizeOverride = new Vector2(W, H);
            try
            {
                var canvas = MenuKit.Canvas(null, "CityMapPreview", 0);
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 10f;
                var cs = canvas.GetComponent<CanvasScaler>();
                cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                cs.scaleFactor = H / MenuKit.DesignHeight;
                var root = (RectTransform)canvas.transform;
                MenuKit.Stretch(root, "Bg", Vector2.zero, Vector2.one, 0f, 0f, 0f, 0f, new Color(0.075f, 0.07f, 0.11f, 1f));
                float unitsW = W / cs.scaleFactor;
                MenuKit.Label(root, def.name + "  (" + (CityMapTiles.Enabled ? "WHOLE-CITY MAP" : "PER-VENUE PICTURE") + ")", 22,
                    new Vector2(0f, 1f), new Vector2(30f, -16f), TextAnchor.MiddleLeft, MenuKit.Accent, unitsW - 60f, height: 30f, bold: true);
                // the CITY race card's map (as big as the column allows) and MAIN's pre-race map
                const float Card = 560f, Pre = 236f;
                RouteMap.Build(root, def, pts, new Vector2(0f, 1f), new Vector2(30f, -56f), Card);
                string lv1 = Report(def.id + " card");
                RouteMap.Build(root, def, pts, new Vector2(0f, 1f), new Vector2(30f + Card + 30f, -56f), Pre);
                string lv2 = Report(def.id + " prerace");
                MenuKit.Label(root, RouteMap.LegendFor(def), MenuKit.Tiny, new Vector2(0f, 1f), new Vector2(30f, -56f - Card - 8f),
                    TextAnchor.MiddleLeft, MenuKit.Dim, Card, height: 26f);
                MenuKit.Label(root, "CARD " + Card.ToString("0") + " UNITS: " + lv1, MenuKit.Tiny, new Vector2(0f, 1f),
                    new Vector2(30f + Card + 30f, -56f - Pre - 12f), TextAnchor.MiddleLeft, Color.white, unitsW - Card - 90f, height: 26f);
                MenuKit.Label(root, "PRE-RACE " + Pre.ToString("0") + " UNITS: " + lv2, MenuKit.Tiny, new Vector2(0f, 1f),
                    new Vector2(30f + Card + 30f, -56f - Pre - 40f), TextAnchor.MiddleLeft, Color.white, unitsW - Card - 90f, height: 26f);
                Canvas.ForceUpdateCanvases();
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                string path = Path.Combine(outDir, "citymap_" + def.id + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log("[CityMapPreview] wrote " + path);
            }
            finally
            {
                MenuKit.ScreenSizeOverride = Vector2.zero;
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        static string Report(string what)
        {
            string s = CityMapTiles.Enabled ? "level " + CityMapTiles.LastLevel + " (" + CityMapTiles.LastMetresPerPixel.ToString("0.0") +
                       " m/px, card " + CityMapTiles.LastWantedMetresPerPixel.ToString("0.0") + " m/px), " + CityMapTiles.LastPlaced + " tiles"
                       : "per-venue picture";
            Debug.Log("[CityMapPreview] " + what + ": " + s);
            return s;
        }
    }
}
