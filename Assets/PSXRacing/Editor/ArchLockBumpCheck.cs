using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing.OnFoot;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// FULL LOCK AND FULL BUMP, AS DRAWN (tier1 11-23; the ArchHubs re-bake for the catalogue rows that had fallen
    /// back to the blunt arch field). Each car named in PSX_ARCHCHECK_CARS (catalogue ids, comma separated) is built
    /// on BumpStopShots' flat ground with the wheel held at FULL LOCK (steer input 1, then -1), dropped 1.0 m at
    /// 3 m/s so the front axle goes to its bump stop, and every physics tick the shell is posed as the game draws it
    /// (CarBodyLean.Pose: the seat and the steer hold read Resources/ArchHubs.bytes) and the drawn tyres are measured
    /// against the body mesh (ArchProbe.DrawnClip: tyre vertices and face samples against the body's triangles).
    /// Per car and lock side: the worst reach through the body, the points over 1 mm, and a side shot of the steered
    /// wheel at the frame of its deepest reach (else at full compression), to Screenshots/ArchLockBump/<tag>_<id>_<side>.png.
    /// Run once with the old tables and once with the new, in fresh processes, PSX_ARCHCHECK_TAG=before / after.
    /// Report: PSXRacing_arch_lockbump_<tag>.txt. Needs graphics (no -nographics).
    /// `-executeMethod PSXRacing.EditorTools.ArchLockBumpCheck.Run`
    /// </summary>
    public static class ArchLockBumpCheck
    {
        const float Dt = BumpStopShots.Dt;
        const string OutDir = "Screenshots/ArchLockBump";

        public static void Run()
        {
            string tag = Environment.GetEnvironmentVariable("PSX_ARCHCHECK_TAG") ?? "run";
            var ids = (Environment.GetEnvironmentVariable("PSX_ARCHCHECK_CARS") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var log = new StringBuilder();
            log.AppendLine($"ARCH LOCK + BUMP ({tag}): ArchHubs.bytes {new FileInfo("Assets/PSXRacing/Resources/ArchHubs.bytes").Length} bytes; full lock both ways, 1.0 m drop at 3 m/s");
            Directory.CreateDirectory(OutDir);
            var prevMode = Physics.simulationMode;
            float prevDt = Time.fixedDeltaTime;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            int code = 0;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                Time.fixedDeltaTime = Dt;
                ArchCeilings.ClearCache();
                ArchHubs.ClearCache();
                BumpStopShots.BuildWorld();
                foreach (var raw in ids)
                {
                    string id = raw.Trim();
                    var spec = CarCatalog.Get(id);
                    if (spec == null) { log.AppendLine($"SKIP {id}: not in the catalogue"); continue; }
                    var f = CarBody.Lifted(CarModelLibrary.Fit(CarShell.DefFor(spec), spec));
                    bool exact = ArchHubs.TryShellHubMax(f, 0, 0f, out _);
                    foreach (float steer in new[] { 1f, -1f })
                        log.AppendLine($"{id} ({CarShell.DefFor(spec)?.key}, {(exact ? "exact row" : "FIELD")}) lock {(steer > 0 ? "right" : "left")}: " + Case(spec, steer, tag, id));
                }
            }
            catch (Exception e) { log.AppendLine("FAIL " + e); code = 1; }
            finally
            {
                Physics.simulationMode = prevMode;
                Time.fixedDeltaTime = prevDt;
                if (BumpStopShots.world != null) UnityEngine.Object.DestroyImmediate(BumpStopShots.world);
                File.WriteAllText($"PSXRacing_arch_lockbump_{tag}.txt", log.ToString());
                Debug.Log("[ArchLockBumpCheck]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static string Case(CarSpec spec, float steer, string tag, string id)
        {
            BumpStopShots.kerbGo.SetActive(false);
            Physics.SyncTransforms();
            var car = BumpStopShots.MakeCar(spec, 0, new Vector3(0f, 1.0f, 0f), 3f);
            var body = car.GetComponent<CarBody>();
            var lean = car.GetComponent<CarBodyLean>();
            var comp = (float[])BumpStopShots.Compression.GetValue(car);
            // the steered wheel that swings its tyre's leading corner outward into the arch: the outer one of the turn
            int wheel = steer > 0f ? 0 : 1;
            float worstClip = 0f, deepest = -1f, deepClip = 0f; int worstOver = 0, worstWheel = -1;
            float steerAt = 0f, drawnYaw = 0f;
            Texture2D shot = null; bool shotIsClip = false;
            int ticks = Mathf.RoundToInt(2.2f / Dt);
            for (int t = 0; t < ticks; t++)
            {
                car.steerInput = steer; car.throttleInput = 0f; car.brakeInput = t > 40 ? 1f : 0f;
                BumpStopShots.CarFixed.Invoke(car, null);
                lean.Pose(Dt);
                float clip = ArchProbe.DrawnClip(body, out int n, out int w);
                bool deeper = comp[wheel] > deepest + 1e-4f;
                if (deeper) { deepest = comp[wheel]; deepClip = clip; }
                if (clip > worstClip + 1e-4f)
                {
                    worstClip = clip; worstOver = n; worstWheel = w;
                    steerAt = car.SteerAngleDeg;
                    drawnYaw = Mathf.DeltaAngle(0f, car.wheelHubs[wheel].localEulerAngles.y);
                    if (shot != null) UnityEngine.Object.DestroyImmediate(shot);
                    shot = Shoot(car, w >= 0 ? w : wheel); shotIsClip = true;
                }
                else if (deeper && !shotIsClip)
                {
                    steerAt = car.SteerAngleDeg;
                    drawnYaw = Mathf.DeltaAngle(0f, car.wheelHubs[wheel].localEulerAngles.y);
                    if (shot != null) UnityEngine.Object.DestroyImmediate(shot);
                    shot = Shoot(car, wheel);
                }
                Physics.Simulate(Dt);
            }
            string name = $"{tag}_{id}_{(steer > 0 ? "R" : "L")}";
            if (shot != null) { File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), shot.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(shot); }
            string s = $"worst reach through the body {worstClip * 1000f:0} mm on wheel {worstWheel} ({worstOver} points > 1 mm); " +
                       $"deepest front compression {deepest * 100f:0.0} cm (reach there {deepClip * 1000f:0} mm); steer {steerAt:0.0} deg physics, drawn hub yaw {drawnYaw:0.0}; shot {name}.png" +
                       (shotIsClip ? " (frame of the deepest reach)" : " (full compression)");
            UnityEngine.Object.DestroyImmediate(car.transform.parent.gameObject);
            return s;
        }

        /// <summary>The wheel from outside, level with the top of the tyre, a little ahead: the arch lip and the tread.</summary>
        static Texture2D Shoot(CarController car, int wheel)
        {
            var cam = BumpStopShots.world.GetComponentInChildren<Camera>();
            var hub = car.wheelHubs[wheel];
            Vector3 side = car.transform.right * (wheel % 2 == 0 ? -1f : 1f);
            Vector3 at = hub.position + car.transform.up * 0.22f;
            Vector3 from = at + side * 2.2f + car.transform.up * 0.55f + car.transform.forward * 0.9f;
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
            var rt = new RenderTexture(640, 480, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            Texture2D tex = null;
            var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest();
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request))
            {
                request.destination = rt;
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
            }
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            return tex;
        }
    }
}
