using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Small sandbox helpers from the Lake Lure round (2026-11-16).
    ///
    /// WriteSceneList: the build settings' scene list exactly as a full build
    /// writes it at its end (PSXRacingBuilder.SceneOrder, the ALL edition),
    /// WITHOUT building anything. For a sandbox whose scenes are all built
    /// when the catalog grows a venue: the new venue's scene is built by the
    /// stage lab, but every play check finds a venue's scene through
    /// TrackCatalog.SceneIndex into this list, and an appended venue moves the
    /// garage and the rest along by one.
    ///
    ///   Unity -batchmode -quit -executeMethod PSXRacing.EditorTools.LakeLureTools.WriteSceneList
    ///   -> PSXRacing_scene_list.txt (every scene, and any that is missing)
    /// </summary>
    public static class LakeLureTools
    {
        public static void WriteSceneList()
        {
            var sb = new StringBuilder();
            var list = new List<EditorBuildSettingsScene>();
            int missing = 0;
            foreach (var p in PSXRacingBuilder.SceneOrder())
            {
                bool there = File.Exists(p);
                if (!there) missing++;
                list.Add(new EditorBuildSettingsScene(p, true));
                sb.AppendLine((there ? "  ok      " : "  MISSING ") + p);
            }
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets();
            sb.Insert(0, $"scene list: {list.Count} scenes written to the build settings, {missing} missing\n");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_scene_list.txt"), sb.ToString());
            Debug.Log("[LakeLure] " + sb.ToString().Split('\n')[0]);
        }

        /// <summary>
        /// Frames from fixed eye points in a built venue's scene, for showing the
        /// same place in two venues (the Park Road's start in LakeLure and in
        /// ChimneyRock):
        ///   PSX_LOOK_VENUE = a scened or held-back venue id
        ///   PSX_LOOK = "name:ex,ey,ez:tx,ty,tz;..." (world metres, the venue's own frame)
        ///   PSX_SHOT_NOON=1 lights it at the game's noon.
        /// Writes Screenshots/psx_look_(venue)_(name).png.
        /// </summary>
        public static void LookShots()
        {
            string id = System.Environment.GetEnvironmentVariable("PSX_LOOK_VENUE") ?? "LakeLure";
            TrackCatalog.TrackDef def = null;
            foreach (var d in TrackCatalog.Scened) if (d.id == id) def = d;
            foreach (var d in TrackCatalog.HeldBack) if (d.id == id) def = d;
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out var player)) { Debug.LogError("[LakeLure] no scene for " + id); return; }
            if (System.Environment.GetEnvironmentVariable("PSX_SHOT_NOON") == "1")
            {
                Light sun = null;
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
                if (sun != null) TimeOfDay.Apply(TimeOfDay.Noon, sun);
                var g = Object.FindFirstObjectByType<PSXGlobals>();
                if (g != null) g.Apply();
            }
            // the car stays out of the frame: parked well under the land
            player.transform.position += Vector3.down * 500f;
            Physics.SyncTransforms();
            // no HUD over a pose matched to the owner's video (round 3)
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) c.enabled = false;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            cam.fieldOfView = 50f;
            foreach (var part in (System.Environment.GetEnvironmentVariable("PSX_LOOK") ?? "").Split(';'))
            {
                var kv = part.Split(':');
                if (kv.Length < 3) continue;
                var e = kv[1].Split(','); var t = kv[2].Split(',');
                var eye = new Vector3(float.Parse(e[0], ci), float.Parse(e[1], ci), float.Parse(e[2], ci));
                var tgt = new Vector3(float.Parse(t[0], ci), float.Parse(t[1], ci), float.Parse(t[2], ci));
                // an optional fourth field: the vertical field of view (round 3: the dash camera's 68 degrees across)
                cam.fieldOfView = kv.Length >= 4 ? float.Parse(kv[3], ci) : 50f;
                PSXScreenshotTool.Shot(cam, "look_" + def.id + "_" + kv[0].Trim(), eye, Quaternion.LookRotation(tgt - eye, Vector3.up));
            }
            Debug.Log("[LakeLure] look shots of " + def.id + " written");
        }
    }
}
