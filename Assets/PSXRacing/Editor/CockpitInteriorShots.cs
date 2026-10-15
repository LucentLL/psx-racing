using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The owner's universal cockpit (CockpitInterior) photographed the way the
    /// game shows it: the player's car on one built venue, the cockpit eye and
    /// lens (ChaseCamera.MountOffset / FOVFor / ViewNearClip), the look-over
    /// (CockpitLook), a full order on the passenger seat (three boxes and two
    /// bottles, built with PizzaCargo.Spawn and settled by stepping physics by
    /// hand, as PizzaCargoSim does) mirrored in by CockpitCargoMirror, and the
    /// needles off made-up car readings. Six frames,
    /// Screenshots\fdcockpit_*.png - tools\cockpit\cockpit-shots.ps1.
    ///
    /// It also pins the cockpit textures' imports (point filter, no mips, the
    /// atlas at max 1024 for the WebGL build's size) and the models' (no
    /// imported materials: CockpitInterior puts PSX/Lit on them), only when
    /// they differ; logs what the importer made of the models; and MEASURES
    /// the rest gap under the bottom box, on the island and in the cabin.
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
            var car = player.GetComponent<CarController>();
            var box = player.GetComponent<BoxCollider>();
            var body = player.GetComponent<CarBody>();
            Vector3 c = box != null ? box.center : new Vector3(0f, 0.72f, 0.05f);
            Vector3 s = box != null ? box.size : new Vector3(1.72f, 1.0f, 4.1f);
            Vector3 eye = ChaseCamera.MountOffset(ChaseCamera.View.Cockpit, c, s, body != null ? body.Def : null,
                                                  body != null ? body.LengthScale : 1f, body != null ? body.BodyLift : 0f);
            Vector3 pos = t.TransformPoint(eye);
            float keepFov = cam.fieldOfView, keepNear = cam.nearClipPlane;
            var keepView = ChaseCamera.Current;
            ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);

            var cargo = SettledCargo(car);
            var cv = Object.FindFirstObjectByType<CockpitView>();
            if (cv != null) cv.PreviewShow(true);
            var ci = player.GetComponentInChildren<CockpitInterior>(true);
            if (ci == null) Debug.LogError("[Cockpit] no CockpitInterior was built on the player");

            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            // (name, hour, aspect, look yaw, steer deg, rpm, mph)
            var shots = new (string, int, float, float, float, float, float)[]
            {
                ("day_forward_16x9", TimeOfDay.Noon, 16f / 9f, 0f, 0f, 3200f, 45f),
                ("day_look_16x9", TimeOfDay.Noon, 16f / 9f, CockpitLook.MaxYaw, 0f, 3200f, 45f),
                ("night_forward_16x9", TimeOfDay.Night, 16f / 9f, 0f, 0f, 3200f, 45f),
                ("phone_forward", TimeOfDay.Noon, 19.5f / 9f, 0f, 0f, 3200f, 45f),
                ("phone_look", TimeOfDay.Noon, 19.5f / 9f, CockpitLook.MaxYaw, 0f, 3200f, 45f),
                ("day_steer_16x9", TimeOfDay.Noon, 16f / 9f, 0f, -18f, 6400f, 82f),
            };
            int lastHour = -1;
            foreach (var (name, hour, aspect, yaw, steer, rpm, mph) in shots)
            {
                if (globals != null && globals.sun != null && hour != lastHour)
                {
                    TimeOfDay.Apply(hour, globals.sun);
                    globals.Apply();
                    lastHour = hour;
                }
                PSXScreenshotTool.AspectOverride = aspect;
                if (car != null) { car.currentRPM = rpm; car.speedKmh = mph * 1.609344f; }
                if (cv != null) cv.PreviewShow(true);
                if (ci != null)
                {
                    // A phone hides the 3D wheel (its own is under the thumb),
                    // which also shows the dials whole.
                    if (name.StartsWith("phone")) ci.Show(true, false);
                    ci.HideShell(true);
                    ci.SetSteer(steer, car != null ? car.maxSteerLowSpeedDeg : 34f);
                    ci.Tick(0f, cargo);
                }
                foreach (var gc in Object.FindObjectsByType<GaugeCluster>(FindObjectsSortMode.None)) gc.Build();
                foreach (var h in Object.FindObjectsByType<RaceHUD>(FindObjectsSortMode.None))
                    HudOnTop.Apply(h.gameObject);
                Canvas.ForceUpdateCanvases();
                cam.fieldOfView = ChaseCamera.FOVFor(ChaseCamera.View.Cockpit, 58f, 0f, 1f, aspect);
                cam.nearClipPlane = ChaseCamera.ViewNearClip(ChaseCamera.View.Cockpit, keepNear);
                var rot = t.rotation * Quaternion.Euler(
                    ChaseCamera.MountPitch(ChaseCamera.View.Cockpit) + CockpitLook.DownFor(yaw), yaw, 0f);
                PSXScreenshotTool.ShotAs(cam, "fdcockpit_" + name, pos, rot);
                if (ci != null && ci.Needles != null)
                {
                    var sb = new System.Text.StringBuilder("[Cockpit] " + name + " needles (deg clockwise):");
                    var lay = CockpitInterior.L;
                    for (int i = 0; i < ci.Needles.Length; i++)
                        if (ci.Needles[i] != null)
                            sb.Append(" " + lay.needles[i].key + " " +
                                      Mathf.DeltaAngle(0f, -ci.Needles[i].localEulerAngles.z).ToString("0.0"));
                    Debug.Log(sb.ToString());
                }
            }
            PSXScreenshotTool.AspectOverride = 0f;
            if (ci != null) ci.HideShell(false);
            if (ci != null && System.Environment.GetEnvironmentVariable("PSX_COCKPIT_DIAG") == "1")
                Diagnose(ci, player, cam, pos, t.rotation * Quaternion.Euler(
                    ChaseCamera.MountPitch(ChaseCamera.View.Cockpit) + CockpitLook.DownFor(CockpitLook.MaxYaw), CockpitLook.MaxYaw, 0f));

            if (ci != null) Report(ci, cargo);
            if (cargo != null) Object.DestroyImmediate(cargo.gameObject);
            if (cv != null) cv.PreviewShow(false);
            ChaseCamera.PreviewView(keepView);
            cam.fieldOfView = keepFov;
            cam.nearClipPlane = keepNear;
            Debug.Log("[Cockpit] shots of " + def.id + " written");
        }

        /// <summary>The full look-over shot with one thing at a time taken
        /// out (diag_*.png, not copied to the shots folder): what is what.</summary>
        static void Diagnose(CockpitInterior ci, GameObject player, Camera cam, Vector3 pos, Quaternion rot)
        {
            PSXScreenshotTool.AspectOverride = 16f / 9f;
            var shell = new List<Renderer>();
            foreach (var r in player.GetComponentsInChildren<Renderer>(true))
                if (!r.transform.IsChildOf(ci.transform) && r.enabled) shell.Add(r);
            var parts = new List<Transform>();
            foreach (Transform c in ci.Model) parts.Add(c);
            foreach (var p in parts)
            {
                bool was = p.gameObject.activeSelf;
                p.gameObject.SetActive(false);
                PSXScreenshotTool.ShotAs(cam, "diag_no_" + p.name, pos, rot);
                p.gameObject.SetActive(was);
            }
            foreach (var r in shell) r.enabled = false;
            PSXScreenshotTool.ShotAs(cam, "diag_no_shell", pos, rot);
            foreach (var r in shell) r.enabled = true;
            var names = new System.Text.StringBuilder("[Cockpit] diag shell renderers:");
            foreach (var r in shell) names.Append(" " + r.name + "(" + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "-") + ")");
            Debug.Log(names.ToString());
            PSXScreenshotTool.AspectOverride = 0f;
        }

        /// <summary>A full order on the stock seat, settled: three boxes and
        /// two bottles, stepped at rest for two seconds with everything else in
        /// the scene held still (the car must not move under the lens).</summary>
        static PizzaCargo SettledCargo(CarController car)
        {
            var cargo = PizzaCargo.Spawn(car, new[] { 0, 3, 6 }, 2, seatStage: 0);
            if (cargo == null || cargo.BoxCount == 0) { Debug.LogError("[Cockpit] the cargo did not build"); return cargo; }
            var held = new List<Rigidbody>();
            foreach (var rb in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
                if (!rb.isKinematic && !rb.transform.IsChildOf(cargo.transform)) { rb.isKinematic = true; held.Add(rb); }
            var mode = Physics.simulationMode;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                const float dt = 1f / 60f;
                for (int i = 0; i < 120; i++)
                {
                    cargo.Tick(Vector3.zero, Quaternion.identity, dt);
                    Physics.Simulate(dt);
                }
            }
            finally
            {
                Physics.simulationMode = mode;
                foreach (var rb in held) if (rb != null) rb.isKinematic = false;
            }
            Debug.Log($"[Cockpit] cargo settled: {cargo.BoxCount} boxes, condition {cargo.Condition:0.000}");
            return cargo;
        }

        static void Report(CockpitInterior ci, PizzaCargo cargo)
        {
            var f = ci.Current;
            Debug.Log($"[Cockpit] fit eye {f.eye.x:0.000},{f.eye.y:0.000},{f.eye.z:0.000} scale {f.scale:0.000} " +
                      $"measured {f.measured} near {CockpitInterior.ActiveNearClip:0.0000}");
            int tris = 0, draws = 0;
            var mirrorRoot = ci.Model.Find("CargoMirror");
            foreach (var mf in ci.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m == null || (mirrorRoot != null && mf.transform.IsChildOf(mirrorRoot))) continue;
                tris += m.triangles.Length / 3;
                draws += m.subMeshCount;
                var b = m.bounds;
                Debug.Log($"[Cockpit] mesh {mf.transform.parent.name}/{mf.name}: {m.triangles.Length / 3} tris, " +
                          $"{m.vertexCount} verts, local bounds min {b.min.x:0.000},{b.min.y:0.000},{b.min.z:0.000} " +
                          $"max {b.max.x:0.000},{b.max.y:0.000},{b.max.z:0.000}");
            }
            var atlas = Resources.Load<Texture2D>("Cockpit/universal_atlas");
            Debug.Log($"[Cockpit] cabin: {tris} tris in {draws} draw calls (wheel, needles, hands included; " +
                      $"cargo copies not: {(ci.Mirror != null ? ci.Mirror.Count : 0)} of those); atlas " +
                      (atlas != null ? atlas.width + "x" + atlas.height + " " + atlas.format : "MISSING"));

            // THE REST GAP under the bottom box: on the island, from the pan;
            // in the cabin, a ray down onto the owner's seat from its base's
            // middle and four corners (cabin metres, i.e. divided by the fit).
            if (cargo == null || cargo.BoxCount == 0 || ci.Mirror == null) return;
            var parts = cargo.ReplayParts();
            Transform box0 = parts.Count > 1 ? parts[1] : null;
            if (box0 == null) return;
            var tray = cargo.Tray;
            var seatQ = tray.rotation * cargo.StackSeatRotLocal;
            float island = (Quaternion.Inverse(seatQ) * (box0.position - tray.TransformPoint(cargo.StackSeatLocal))).y;
            // Straight down in the MODEL frame (the cabin mesh is authored
            // there, so this is the owner's metres) onto the cabin's triangles.
            var cabin = ci.Model.Find("Cabin");
            var mf0 = cabin != null ? cabin.GetComponentInChildren<MeshFilter>(true) : null;
            if (mf0 == null || mf0.sharedMesh == null) return;
            var toMesh = mf0.transform.worldToLocalMatrix * ci.Model.localToWorldMatrix;
            var vs = mf0.sharedMesh.vertices;
            var ts = mf0.sharedMesh.triangles;
            var gaps = new List<string>();
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var (u, v) in new[] { (0f, 0f), (-0.18f, -0.18f), (0.18f, -0.18f), (-0.18f, 0.18f), (0.18f, 0.18f) })
            {
                Vector3 isl = box0.position + seatQ * new Vector3(u, 0f, v);
                Vector3 q = toMesh.MultiplyPoint3x4(ci.Mirror.MapSeat(isl));
                float best = float.NaN;
                for (int k = 0; k < ts.Length; k += 3)
                {
                    Vector3 a = vs[ts[k]], b = vs[ts[k + 1]], cc = vs[ts[k + 2]];
                    float d = (b.z - cc.z) * (a.x - cc.x) + (cc.x - b.x) * (a.z - cc.z);
                    if (Mathf.Abs(d) < 1e-9f) continue;
                    float l1 = ((b.z - cc.z) * (q.x - cc.x) + (cc.x - b.x) * (q.z - cc.z)) / d;
                    float l2 = ((cc.z - a.z) * (q.x - cc.x) + (a.x - cc.x) * (q.z - cc.z)) / d;
                    float l3 = 1f - l1 - l2;
                    if (l1 < 0f || l2 < 0f || l3 < 0f) continue;
                    float y = l1 * a.y + l2 * b.y + l3 * cc.y;
                    if (y > q.y + 0.10f) continue;          // above the box: not under it
                    if (float.IsNaN(best) || y > best) best = y;
                }
                if (float.IsNaN(best)) { gaps.Add("miss"); continue; }
                float g = q.y - best;
                lo = Mathf.Min(lo, g); hi = Mathf.Max(hi, g);
                gaps.Add((g * 100f).ToString("0.0"));
            }
            Debug.Log($"[Cockpit] rest gap, bottom box: island {island * 100f:0.0} cm off its pan; cabin (centre, 4 corners) " +
                      string.Join(" / ", gaps) + $" cm (min {lo * 100f:0.0}, max {hi * 100f:0.0}; negative = sunk)");
        }

        static void Imports()
        {
            foreach (var (name, max) in new[] { ("universal_atlas.png", 1024), ("hands_skin.png", 2048) })
            {
                var imp = AssetImporter.GetAtPath(Dir + name) as TextureImporter;
                if (imp == null) { Debug.LogError("[Cockpit] missing " + Dir + name); continue; }
                var web = imp.GetPlatformTextureSettings("WebGL");
                if (imp.filterMode == FilterMode.Point && !imp.mipmapEnabled && imp.wrapMode == TextureWrapMode.Clamp
                    && imp.maxTextureSize == max && (!web.overridden || web.maxTextureSize <= max)) continue;
                imp.filterMode = FilterMode.Point;
                imp.mipmapEnabled = false;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.maxTextureSize = max;
                if (web.overridden && web.maxTextureSize > max) { web.maxTextureSize = max; imp.SetPlatformTextureSettings(web); }
                imp.SaveAndReimport();
            }
            foreach (var guid in AssetDatabase.FindAssets("universal_", new[] { Dir.TrimEnd('/') }))
            {
                var imp = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as ModelImporter;
                if (imp == null) continue;
                if (imp.materialImportMode == ModelImporterMaterialImportMode.None && !imp.isReadable) continue;
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.isReadable = false;
                imp.SaveAndReimport();
            }
        }
    }
}
