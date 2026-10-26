using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The NFS-style driving map (HudMinimap, 2026-10-26) in the game's own
    /// scenes: Charlotte free roam (GPS route to the nearest food), a city
    /// race, a mountain stage in the chase view and from the cockpit, on a
    /// 16:9 desktop frame and on the owner's 2000x923 phone frame with the
    /// touch panel up. Each frame is CHECKED as well as photographed: every
    /// HUD text through LifeHomePreview.ReportClippedText (CLIP / OVERLAP,
    /// goal 0), plus the map's square against every other text and against
    /// the dials. Screenshots\minimap_*.png; "[MinimapShots] done" at the end.
    /// Opens scenes, never saves them.
    /// </summary>
    public static class MinimapShots
    {
        const float Phone = 2000f / 923f, Desk = 16f / 9f;

        /// <summary>One job: DriveHudPreview's placement checks (map vs wheel,
        /// MENU, pizza cam and the dials, at three phone aspects and a PC
        /// frame), then the scene shots.</summary>
        public static void CaptureWithDriveHud()
        {
            try { DriveHudPreview.Capture(); }
            catch (System.Exception e) { Debug.LogException(e); }
            Capture();
        }

        [MenuItem("PSX Racing/Minimap Shots (NFS map)")]
        public static void Capture()
        {
            int issues = 0;
            issues += Shoot("Charlotte", "roam_pc", Desk, false, false);
            issues += Shoot("Charlotte", "roam_phone", Phone, true, false);
            issues += Shoot("UptownLoop", "cityrace_pc", Desk, false, false);
            issues += Shoot("UptownLoop", "cityrace_phone", Phone, true, false);
            issues += Shoot("ChimneyRock", "stage_pc", Desk, false, false);
            issues += Shoot("ChimneyRock", "stage_cockpit_pc", Desk, false, true);
            issues += Shoot("ChimneyRock", "stage_phone", Phone, true, false);
            Debug.Log("[MinimapShots] TOTAL CLIP/OVERLAP " + issues);
            Debug.Log("[MinimapShots] done");
        }

        static int Shoot(string venue, string label, float aspect, bool touch, bool cockpit)
        {
            var def = TrackCatalog.At(TrackCatalog.IndexOf(venue));
            PSXScreenshotTool.AspectOverride = aspect;
            if (def == null || def.id != venue || !PSXScreenshotTool.Open(def, out var cam, out var player))
            {
                Debug.LogError("[MinimapShots] " + venue + " did not open");
                PSXScreenshotTool.AspectOverride = 0f;
                return 1;
            }
            var keepView = ChaseCamera.Current;
            var t = player.transform;
            int found = 0;
            RenderTexture frame = null;
            CockpitInterior ci = null;
            var cv = Object.FindAnyObjectByType<CockpitView>();
            try
            {
                var world = Object.FindAnyObjectByType<CityWorld>();
                if (world != null) world.EnsureRing(t.position, 2);

                // A city race builds its path from the graph at runtime
                // (CityMode.BuildPath); in edit mode the scene's is empty, so
                // the menu's route polyline stands in for it.
                var rm = Object.FindAnyObjectByType<RaceManager>();
                if (rm != null && (rm.path == null || rm.path.Count < 2) && def.IsCityRace)
                {
                    var stand = new GameObject("RouteStandIn").AddComponent<TrackPath>();
                    var pts = TrackCatalog.Sample(def, TrackCatalog.Spacing);
                    stand.waypoints = pts.ToArray();
                    stand.spacing = TrackCatalog.Spacing;
                    stand.pointToPoint = !def.loop;
                    stand.finishIndex = def.loop ? -1 : Mathf.Clamp(def.FinishIndex, 1, pts.Count - 1);
                    rm.path = stand;
                    Debug.Log("[MinimapShots] " + venue + " route stand-in: " + pts.Count + " stations");
                }

                if (touch) ShowTouch(cam);
                if (cockpit)
                {
                    ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
                    if (cv != null) cv.PreviewShow(true);
                    ci = player.GetComponentInChildren<CockpitInterior>(true);
                    if (ci != null)
                    {
                        ci.Refit();
                        ci.Show(true, true);
                        ci.HideShell(true);
                        ci.Tick(0f, null);
                    }
                }
                else ChaseCamera.PreviewView(ChaseCamera.View.Chase);

                // THE FRAME THE HUD IS LAID OUT IN: the shot's own size, so
                // the canvases measure the picture they are drawn into.
                var output = Object.FindAnyObjectByType<PSXCameraOutput>();
                int h = output != null ? Mathf.Clamp(output.height, 120, 720) : 240;
                int w = Mathf.RoundToInt(h * aspect) & ~1;
                frame = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                frame.Create();
                cam.targetTexture = frame;
                Canvas.ForceUpdateCanvases();
                foreach (var c in Object.FindObjectsByType<GaugeCluster>(FindObjectsSortMode.None)) c.Build();
                RaceHUD hud = null;
                foreach (var hh in Object.FindObjectsByType<RaceHUD>(FindObjectsSortMode.None))
                {
                    hud = hh;
                    hh.PreviewMap();
                    HudOnTop.Apply(hh.gameObject);
                }
                Canvas.ForceUpdateCanvases();
                // the second pass: the distance text and the marks after the
                // layout has settled
                if (hud != null && hud.Minimap != null) hud.Minimap.PreviewNow();
                Canvas.ForceUpdateCanvases();

                Vector3 eye; Quaternion rot;
                if (cockpit)
                {
                    var box = player.GetComponent<BoxCollider>();
                    var body = player.GetComponent<CarBody>();
                    Vector3 cc = box != null ? box.center : new Vector3(0f, 0.72f, 0.05f);
                    Vector3 sz = box != null ? box.size : new Vector3(1.72f, 1.0f, 4.1f);
                    var car = player.GetComponent<CarController>();
                    eye = t.TransformPoint(ChaseCamera.MountOffset(ChaseCamera.View.Cockpit, cc, sz,
                        body != null ? body.Def : null, body != null ? body.LengthScale : 1f,
                        body != null ? body.BodyLift : 0f, car != null && DriveSide.IsRHD(car)));
                    rot = t.rotation * Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Cockpit), 0f, 0f);
                    cam.fieldOfView = ChaseCamera.FOVFor(ChaseCamera.View.Cockpit, 58f, 0f, 1f, aspect);
                    if (ci != null) cam.nearClipPlane = CockpitInterior.NearClipFor(ci.Current.scale, 0f);
                }
                else
                {
                    ChaseCamera.SteadyPose(ChaseCamera.View.Chase, aspect, 0f, ChaseCamera.DefaultSpeedFullMps, t,
                                           ChaseCamera.FrameOf(player), default, out eye, out rot, out float fov, out _);
                    cam.fieldOfView = fov;
                }

                found = Check(cam, venue + "/" + label, w, h, hud);
                PSXScreenshotTool.ShotAs(cam, "minimap_" + label, eye, rot);
                Debug.Log("[MinimapShots] " + label + " " + w + "x" + h + (touch ? " touch" : "") +
                          (cockpit ? " cockpit" : "") + " CLIP/OVERLAP " + found +
                          (hud != null && hud.Minimap != null && hud.Minimap.DistanceText != null
                              ? "  distance \"" + hud.Minimap.DistanceText.text + "\"" : "  NO MAP"));
            }
            finally
            {
                if (ci != null) ci.HideShell(false);
                if (cv != null) cv.PreviewShow(false);
                ChaseCamera.PreviewView(keepView);
                cam.targetTexture = null;
                if (frame != null) { frame.Release(); Object.DestroyImmediate(frame); }
                PSXScreenshotTool.AspectOverride = 0f;
            }
            return found;
        }

        /// <summary>The touch panel up, as on a phone, on the shot camera.</summary>
        static void ShowTouch(Camera cam)
        {
            var tc = Object.FindAnyObjectByType<TouchControls>();
            if (tc == null) tc = new GameObject("TouchControls").AddComponent<TouchControls>();
            tc.forceShow = true;
            Invoke(tc, "Awake");
            Invoke(tc, "SetVisible", true);
            foreach (var c in tc.GetComponentsInChildren<Canvas>(true))
            {
                if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = 1f;
            }
        }

        static void Invoke(object o, string method, params object[] args)
        {
            var m = o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) { Debug.LogError("[MinimapShots] no " + method + " on " + o.GetType().Name); return; }
            m.Invoke(o, args);
        }

        /// <summary>CLIP / OVERLAP over every text (LifeHomePreview's own
        /// test), then the map's square against every other text and the
        /// dials. Returns the number of findings.</summary>
        static int Check(Camera cam, string where, int w, int h, RaceHUD hud)
        {
            int n = 0;
            void Count(string msg, string trace, LogType type)
            {
                if (msg.StartsWith("[HomePreview] CLIP")) n++;
            }
            Application.logMessageReceived += Count;
            try { LifeHomePreview.ReportClippedText(cam, where, w, h); }
            finally { Application.logMessageReceived -= Count; }

            var mm = hud != null ? hud.Minimap : null;
            if (mm == null || mm.Root == null)
            {
                if (where.StartsWith("Charlotte") || where.StartsWith("Uptown") || where.StartsWith("Chimney"))
                { Debug.LogWarning("[MinimapShots] " + where + " NO MAP built"); n++; }
                return n;
            }
            var map = ScreenRect(cam, (RectTransform)mm.Root.transform);
            Debug.Log("[MinimapShots] " + where + " map x " + map.xMin.ToString("0") + "-" + map.xMax.ToString("0") +
                      " y " + map.yMin.ToString("0") + "-" + map.yMax.ToString("0") + " of " + w + "x" + h);
            if (map.xMin < -1f || map.yMin < -1f || map.xMax > w + 1f || map.yMax > h + 1f)
            { Debug.LogWarning("[MinimapShots] CLIP " + where + " the map runs off the frame"); n++; }

            // other texts printed inside the map's square
            foreach (var tx in Object.FindObjectsByType<Text>(FindObjectsSortMode.None))
            {
                if (!tx.isActiveAndEnabled || string.IsNullOrEmpty(tx.text) || tx.color.a < 0.05f) continue;
                if (tx.transform.IsChildOf(mm.Root.transform)) continue;
                if (tx.canvas != null && tx.canvas.renderMode == RenderMode.WorldSpace) continue;
                var r = GlyphRect(cam, tx);
                if (r.width <= 0f) continue;
                float ox = Mathf.Min(r.xMax, map.xMax) - Mathf.Max(r.xMin, map.xMin);
                float oy = Mathf.Min(r.yMax, map.yMax) - Mathf.Max(r.yMin, map.yMin);
                if (ox > 4f && oy > 4f)
                {
                    Debug.LogWarning("[MinimapShots] CLIP " + where + " OVERLAP map over \"" + tx.text.Replace("\n", " ") + "\"");
                    n++;
                }
            }
            // the dials
            foreach (var gc in Object.FindObjectsByType<GaugeCluster>(FindObjectsSortMode.None))
                foreach (var name in new[] { "Tach", "Speedo", "Panel/Gear", "Cockpit" })
                {
                    var tr = gc.transform.Find(name) as RectTransform;
                    if (tr == null || !tr.gameObject.activeInHierarchy) continue;
                    var r = ScreenRect(cam, tr);
                    float ox = Mathf.Min(r.xMax, map.xMax) - Mathf.Max(r.xMin, map.xMin);
                    float oy = Mathf.Min(r.yMax, map.yMax) - Mathf.Max(r.yMin, map.yMin);
                    if (ox > 1f && oy > 1f && name != "Cockpit")
                    {
                        Debug.LogWarning("[MinimapShots] CLIP " + where + " OVERLAP map over the " + name);
                        n++;
                    }
                }
            return n;
        }

        static Rect ScreenRect(Camera cam, RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        static Rect GlyphRect(Camera cam, Text t)
        {
            var gen = t.cachedTextGenerator;
            if (gen == null || gen.vertexCount == 0) return Rect.zero;
            float upp = 1f / Mathf.Max(0.0001f, t.pixelsPerUnit);
            var verts = gen.verts;
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            for (int i = 0; i < verts.Count; i++)
            {
                Vector2 sp = RectTransformUtility.WorldToScreenPoint(cam, t.rectTransform.TransformPoint(verts[i].position * upp));
                x0 = Mathf.Min(x0, sp.x); x1 = Mathf.Max(x1, sp.x);
                y0 = Mathf.Min(y0, sp.y); y1 = Mathf.Max(y1, sp.y);
            }
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }
    }
}
