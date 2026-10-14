using System.IO;
using UnityEditor;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The generic 3D cockpit (CockpitInterior) photographed the way the game
    /// shows it: the player's car on one built venue, the cockpit eye and lens
    /// (ChaseCamera.MountOffset / FOVFor / ViewNearClip), at NOON and at NIGHT,
    /// at 16:9 and on a 19.5:9 phone - four frames,
    /// Screenshots\cockpit_{day,night}_{16x9,phone}.png. tools\cockpit\cockpit-shots.ps1.
    ///
    /// It also pins the two cockpit textures' imports (point filter, no mips:
    /// one sheet of regions, and a mip would bleed them into each other) and
    /// the three models' (no imported materials: CockpitInterior puts PSX/Lit
    /// on them), only when they differ, and logs what the importer made of
    /// the models - the axis and scale traps of a Blender export show up in
    /// those bounds first.
    /// </summary>
    public static class CockpitInteriorShots
    {
        const string Dir = "Assets/PSXRacing/Resources/Cockpit/";

        [MenuItem("PSX Racing/Capture Cockpit Interior")]
        public static void Capture()
        {
            Imports();
            var def = TrackCatalog.At(0);
            string only = System.Environment.GetEnvironmentVariable("PSX_SHOT_VENUE");
            if (!string.IsNullOrEmpty(only))
                foreach (var d in TrackCatalog.All) if (d.id == only) { def = d; break; }
            if (!PSXScreenshotTool.Open(def, out var cam, out var player)) return;

            var t = player.transform;
            var box = player.GetComponent<BoxCollider>();
            var body = player.GetComponent<CarBody>();
            Vector3 c = box != null ? box.center : new Vector3(0f, 0.72f, 0.05f);
            Vector3 s = box != null ? box.size : new Vector3(1.72f, 1.0f, 4.1f);
            Vector3 eye = ChaseCamera.MountOffset(ChaseCamera.View.Cockpit, c, s, body != null ? body.Def : null,
                                                  body != null ? body.LengthScale : 1f, body != null ? body.BodyLift : 0f);
            Vector3 pos = t.TransformPoint(eye);
            Quaternion rot = t.rotation * Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Cockpit), 0f, 0f);
            float keepFov = cam.fieldOfView, keepNear = cam.nearClipPlane;
            var keepView = ChaseCamera.Current;
            ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);

            var cv = Object.FindFirstObjectByType<CockpitView>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            foreach (var (hourName, hour) in new[] { ("day", TimeOfDay.Noon), ("night", TimeOfDay.Night) })
            {
                if (globals != null && globals.sun != null)
                {
                    TimeOfDay.Apply(hour, globals.sun);
                    globals.Apply();
                }
                foreach (var (shape, aspect) in new[] { ("16x9", 16f / 9f), ("phone", 19.5f / 9f) })
                {
                    PSXScreenshotTool.AspectOverride = aspect;
                    if (cv != null) cv.PreviewShow(true);
                    foreach (var h in Object.FindObjectsByType<RaceHUD>(FindObjectsSortMode.None))
                        HudOnTop.Apply(h.gameObject);
                    Canvas.ForceUpdateCanvases();
                    cam.fieldOfView = ChaseCamera.FOVFor(ChaseCamera.View.Cockpit, 58f, 0f, 1f, aspect);
                    cam.nearClipPlane = ChaseCamera.ViewNearClip(ChaseCamera.View.Cockpit, keepNear);
                    PSXScreenshotTool.ShotAs(cam, "cockpit_" + hourName + "_" + shape, pos, rot);
                }
            }
            PSXScreenshotTool.AspectOverride = 0f;

            var ci = player.GetComponentInChildren<CockpitInterior>(true);
            if (ci != null)
            {
                var f = ci.Current;
                Debug.Log($"[Cockpit] fit eye {f.eye.x:0.000},{f.eye.y:0.000},{f.eye.z:0.000} scale {f.scale:0.000} stretch {f.stretch:0.000}");
                foreach (var mf in ci.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = mf.sharedMesh;
                    if (m == null) continue;
                    var b = m.bounds;
                    Debug.Log($"[Cockpit] mesh {mf.name}: {m.triangles.Length / 3} tris, local bounds " +
                              $"min {b.min.x:0.00},{b.min.y:0.00},{b.min.z:0.00} max {b.max.x:0.00},{b.max.y:0.00},{b.max.z:0.00}");
                }
            }
            else Debug.LogError("[Cockpit] no CockpitInterior was built on the player");
            Debug.Log($"[Cockpit] WheelCentre {CockpitView.WheelCentre} r {CockpitView.WheelRadius}");

            if (cv != null) cv.PreviewShow(false);
            ChaseCamera.PreviewView(keepView);
            cam.fieldOfView = keepFov;
            cam.nearClipPlane = keepNear;
            Debug.Log("[Cockpit] shots of " + def.id + " written");
        }

        static void Imports()
        {
            foreach (var name in new[] { "cockpit_sheet.png", "hands_skin.png" })
            {
                var imp = AssetImporter.GetAtPath(Dir + name) as TextureImporter;
                if (imp == null) { Debug.LogError("[Cockpit] missing " + Dir + name); continue; }
                if (imp.filterMode == FilterMode.Point && !imp.mipmapEnabled && imp.wrapMode == TextureWrapMode.Clamp) continue;
                imp.filterMode = FilterMode.Point;
                imp.mipmapEnabled = false;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
            foreach (var name in new[] { "generic_cockpit.obj", "generic_wheel.obj", "generic_hands.obj" })
            {
                var imp = AssetImporter.GetAtPath(Dir + name) as ModelImporter;
                if (imp == null) { Debug.LogError("[Cockpit] missing " + Dir + name); continue; }
                if (imp.materialImportMode == ModelImporterMaterialImportMode.None && !imp.isReadable) continue;
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.isReadable = false;
                imp.SaveAndReimport();
            }
        }
    }
}
