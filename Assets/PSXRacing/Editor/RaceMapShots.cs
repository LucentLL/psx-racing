using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The race HUD map of a Charlotte race, BEFORE and AFTER its streets
    /// (2026-10-04, the owner: "on Race Maps, I'd like them to show the local
    /// streets in darker gray. Race Track Map is in brighter white to stand
    /// out"): the UptownLoop scene opened as the screenshot tool opens it, the
    /// city stood up round the grid, and the game's own chase view shot twice -
    /// the map without the streets (TrackCatalog.HudStreetsOff), then as the
    /// game draws it. Screenshots\racemap_uptown_{before,after}.png.
    /// </summary>
    public static class RaceMapShots
    {
        [MenuItem("PSX Racing/Race Map Shots (Uptown)")]
        public static void Capture()
        {
            var def = TrackCatalog.At(TrackCatalog.IndexOf("UptownLoop"));
            if (def == null || def.id != "UptownLoop" || !PSXScreenshotTool.Open(def, out var cam, out var player))
            {
                Debug.LogError("[RaceMapShots] the UptownLoop scene did not open");
                return;
            }
            var t = player.transform;
            var world = Object.FindAnyObjectByType<CityWorld>();
            if (world != null) world.EnsureRing(t.position, 2);
            ChaseCamera.SteadyPose(ChaseCamera.View.Chase, 16f / 9f, 0f, ChaseCamera.DefaultSpeedFullMps, t,
                                   ChaseCamera.FrameOf(player), default, out Vector3 eye, out Quaternion rot, out float fov, out _);
            float keepFov = cam.fieldOfView;
            cam.fieldOfView = fov;
            for (int pass = 0; pass < 2; pass++)
            {
                TrackCatalog.HudStreetsOff = pass == 0;
                TrackCatalog.ForgetThumbnails();
                foreach (var h in Object.FindObjectsByType<RaceHUD>(FindObjectsSortMode.None))
                {
                    h.RebuildPreviewMap();
                    HudOnTop.Apply(h.gameObject);
                }
                PSXScreenshotTool.ShotAs(cam, pass == 0 ? "racemap_uptown_before" : "racemap_uptown_after", eye, rot);
            }
            TrackCatalog.HudStreetsOff = false;
            TrackCatalog.ForgetThumbnails();
            cam.fieldOfView = keepFov;
            Debug.Log("[RaceMapShots] done");
        }
    }
}
