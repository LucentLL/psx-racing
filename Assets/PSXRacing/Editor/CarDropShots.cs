using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE RAIN AND SNOW ON THE CARS, in pictures (owner, 2026-10-24: "The
    /// water particle effects on windows in cockpit view would look nice on
    /// the exterior of cars in rain ... (Could be white and move differently
    /// when its snow)"). One venue (PSX_SHOT_VENUE, else BeechGap) at noon,
    /// the player's car dressed as PSX_DROPS_CAR (else the R34) with a copy of
    /// it 9 m ahead; for dry, rain and snow, parked and at speed (the air
    /// over each car as CarLights writes it), the CHASE and CLOSE lenses
    /// (ChaseCamera.SteadyPose) and a replay's trackside camera at the front
    /// quarter. Edit mode has no LateUpdate, so the weather's globals are set
    /// here as CarLights would set them.
    /// Writes Screenshots/drops_&lt;weather&gt;_&lt;parked|speed&gt;_&lt;lens&gt;.png.
    /// </summary>
    public static class CarDropShots
    {
        public static void Run()
        {
            var def = TrackCatalog.At(TrackCatalog.CheckVenue);
            string only = System.Environment.GetEnvironmentVariable("PSX_SHOT_VENUE");
            if (string.IsNullOrEmpty(only)) only = "BeechGap";
            foreach (var d in TrackCatalog.All) if (d.id == only) { def = d; break; }
            if (!PSXScreenshotTool.Open(def, out var cam, out var player)) { Debug.LogError("[Drops] venue did not open"); return; }
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = false;
            var output = Object.FindFirstObjectByType<PSXCameraOutput>();
            if (output != null) output.height = 720;
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            TimeOfDay.Apply(TimeOfDay.Noon, sun);
            if (globals != null) globals.Apply();
            PSXScreenshotTool.AspectOverride = 16f / 9f;

            string key = System.Environment.GetEnvironmentVariable("PSX_DROPS_CAR");
            if (string.IsNullOrEmpty(key)) key = "gtr_99";
            var body = player.GetComponent<CarBody>();
            var shell = CarModelLibrary.Load(key);
            if (body != null && shell != null) body.Apply(shell, 0);
            var t = player.transform;
            var ahead = Object.Instantiate(player);
            ahead.name = "DropsAhead";
            foreach (var ci in ahead.GetComponentsInChildren<CockpitInterior>(true)) Object.DestroyImmediate(ci.gameObject);
            ahead.transform.SetPositionAndRotation(t.TransformPoint(new Vector3(-0.6f, 0f, 9f)), t.rotation);
            Physics.SyncTransforms();

            var rends = new List<Renderer>();
            var tmp = new List<Renderer>();
            PaintGloss.Collect(t, tmp); rends.AddRange(tmp);
            PaintGloss.Collect(ahead.transform, tmp); rends.AddRange(tmp);
            var frame = ChaseCamera.FrameOf(player);
            var dials = ChaseCamera.LiveDials();
            var block = new MaterialPropertyBlock();
            try
            {
                foreach (var w in new[] { "dry", "rain", "snow" })
                {
                    Shader.SetGlobalFloat(PaintGloss.CarWetId, w == "rain" ? 1f : 0f);
                    Shader.SetGlobalVector(PaintGloss.CarDropsId, new Vector4(w == "rain" ? 1f : 0f, w == "snow" ? 1f : 0f, 0f, 0f));
                    if (w == "dry") WeatherFx.PreviewClear();
                    else WeatherFx.Preview(w == "rain" ? Weather.Rain : Weather.Snow, cam, 2.5f);
                    foreach (bool fast in new[] { false, true })
                    {
                        if (w == "dry" && fast) continue;
                        float wind = fast ? 1f : 0f;
                        foreach (var r in rends)
                        {
                            if (r == null) continue;
                            Vector3 a = r.transform.InverseTransformDirection(-r.transform.root.forward);
                            r.GetPropertyBlock(block);
                            block.SetVector(PaintGloss.CarWindId, new Vector4(a.x, a.y, a.z, wind));
                            block.SetVector(PaintGloss.CarRunId, new Vector4(0f, 0f, Mathf.Lerp(CarLights.BlowRateLo, CarLights.BlowRateHi, wind), 0f));
                            r.SetPropertyBlock(block);
                        }
                        float speed = fast ? 30f : 0f;
                        string stem = $"drops_{w}_{(fast ? "speed" : "parked")}";
                        foreach (var v in new[] { ChaseCamera.View.Chase, ChaseCamera.View.Close })
                        {
                            ChaseCamera.SteadyPose(v, 16f / 9f, speed, 40f, t, frame, dials, out var eye, out var rot, out var fov, out _);
                            cam.fieldOfView = fov;
                            PSXScreenshotTool.ShotAs(cam, $"{stem}_{(v == ChaseCamera.View.Chase ? "chase" : "close")}", eye, rot);
                        }
                        // a replay's trackside camera: low at the front quarter, looking back at the car
                        cam.fieldOfView = 34f;
                        Vector3 tv = t.TransformPoint(new Vector3(2.6f, 1.1f, 4.2f));
                        PSXScreenshotTool.ShotAs(cam, stem + "_replay", tv, Quaternion.LookRotation(t.TransformPoint(new Vector3(0f, 0.7f, -0.3f)) - tv));
                        if (fast)
                        {
                            // a moment later: the streaks' clock 0.2 s on at full rate
                            foreach (var r in rends)
                            {
                                if (r == null) continue;
                                r.GetPropertyBlock(block);
                                block.SetVector(PaintGloss.CarRunId, new Vector4(0.2f * CarLights.BlowRateHi, 0f, CarLights.BlowRateHi, 0f));
                                r.SetPropertyBlock(block);
                            }
                            PSXScreenshotTool.ShotAs(cam, stem + "_replay_later", tv, Quaternion.LookRotation(t.TransformPoint(new Vector3(0f, 0.7f, -0.3f)) - tv));
                        }
                        Debug.Log($"[Drops] {stem}: shots written");
                    }
                }
            }
            finally
            {
                WeatherFx.PreviewClear();
                Shader.SetGlobalVector(PaintGloss.CarDropsId, Vector4.zero);
                Shader.SetGlobalFloat(PaintGloss.CarWetId, 0f);
                PSXScreenshotTool.AspectOverride = 0f;
                Object.DestroyImmediate(ahead);
            }
            Debug.Log("[Drops] DONE");
        }
    }
}
