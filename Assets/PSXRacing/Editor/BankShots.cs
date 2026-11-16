using System.Globalization;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE BANK ROUND'S PROOF FRAMES (2026-11-16, owner: "many of the mountain
    /// road tracks are very flat. They have turns, but almost no (if any)
    /// banking"). The player's car stood in the right-hand lane at a named
    /// waypoint of each stage, SEATED on the road under it and rolled with it
    /// (CarController.RollToRoad, the recovery's own rule), and two frames:
    ///
    ///   _cockpit  the driver's seat (ChaseCamera's cockpit mount, the cabin
    ///             drawn), which rolls with the car as the game's does;
    ///   _low      a low chase angle with a LEVEL horizon, 8 m behind and 1.3 m
    ///             over the road, which shows the road's tilt against the land.
    ///
    /// Run once on scenes built with PSX_STAGE_BANK=0 and once with it on, in
    /// fresh processes, and the two sets are the same poses before and after.
    ///
    ///   PSX_BANK_SHOTS = "ChimneyRock:715:hairpin,1049:hairpin;BlueRidge:1141:linncove"
    ///   PSX_BANK_TAG   = off | on        -> Screenshots\psx_bank_(venue)_(wp)_(name)_(tag)_(view).png
    ///
    /// On the built scenes only (no build); graphics ON.
    /// </summary>
    public static class BankShots
    {
        const int RoadMask = 1 << 8;

        public static void Capture()
        {
            string spec = System.Environment.GetEnvironmentVariable("PSX_BANK_SHOTS") ?? "";
            string tag = System.Environment.GetEnvironmentVariable("PSX_BANK_TAG") ?? "shot";
            var log = new System.Text.StringBuilder();
            foreach (var venuePart in spec.Split(';'))
            {
                int colon = venuePart.IndexOf(':');
                if (colon < 0) continue;
                string venue = venuePart.Substring(0, colon).Trim();
                TrackCatalog.TrackDef def = null;
                foreach (var d in TrackCatalog.Scened) if (d.id == venue) def = d;
                foreach (var d in TrackCatalog.HeldBack) if (d.id == venue) def = d;
                if (def == null) { log.AppendLine("no venue " + venue); continue; }
                if (!PSXScreenshotTool.Open(def, out var cam, out var player)) { log.AppendLine("no scene " + venue); continue; }
                var path = Object.FindFirstObjectByType<TrackPath>();
                if (path == null || path.Count < 4) continue;
                var t = player.transform;
                // The car's height over the road under it, as built.
                float lift = 0.35f;
                if (Physics.Raycast(t.position + Vector3.up * 2f, Vector3.down, out var h0, 6f, RoadMask, QueryTriggerInteraction.Ignore))
                    lift = t.position.y - h0.point.y;
                var box = player.GetComponent<BoxCollider>();
                Vector3 c = box != null ? box.center : new Vector3(0f, 0.72f, 0.05f);
                Vector3 s = box != null ? box.size : new Vector3(1.72f, 1.0f, 4.1f);
                var shell = player.GetComponent<CarBody>();
                float keepFov = cam.fieldOfView, keepNear = cam.nearClipPlane;
                var keepView = ChaseCamera.Current;

                foreach (var part in venuePart.Substring(colon + 1).Split(','))
                {
                    var kv = part.Split(':');
                    if (!int.TryParse(kv[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int wp)) continue;
                    string name = kv.Length > 1 ? kv[1].Trim() : "spot";
                    int i = Mathf.Clamp(wp, 1, path.Count - 2);
                    Vector3 at = path.GetPoint(i);
                    Vector3 fwd = path.GetPoint(i + 1) - path.GetPoint(i - 1);
                    fwd.y = 0f; fwd.Normalize();
                    Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
                    float lateral = path.roadWidth * 0.25f;
                    Vector3 lane = at + right * lateral;
                    float roadY = at.y + PSXRacingBuilder.RoadLift;
                    if (Physics.Raycast(lane + Vector3.up * 3f, Vector3.down, out var hit, 8f, RoadMask, QueryTriggerInteraction.Ignore))
                        roadY = hit.point.y;
                    Vector3 seat = new Vector3(lane.x, roadY + lift, lane.z);
                    Quaternion rot = CarController.RollToRoad(seat, Quaternion.LookRotation(fwd, Vector3.up));
                    t.SetPositionAndRotation(seat, rot);
                    Physics.SyncTransforms();
                    float roll = Vector3.SignedAngle(Vector3.up, t.up, fwd);
                    string stem = "bank_" + def.id + "_" + wp + "_" + name + "_" + tag;

                    // The driver's seat: the cabin drawn, the mount the game uses.
                    ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
                    foreach (var gc in Object.FindObjectsByType<GaugeCluster>(FindObjectsSortMode.None)) gc.Build();
                    foreach (var cv in Object.FindObjectsByType<CockpitView>(FindObjectsSortMode.None)) cv.PreviewShow(true);
                    foreach (var hud in Object.FindObjectsByType<RaceHUD>(FindObjectsSortMode.None)) HudOnTop.Apply(hud.gameObject);
                    Canvas.ForceUpdateCanvases();
                    cam.fieldOfView = ChaseCamera.ViewFOV(ChaseCamera.View.Cockpit, 58f);
                    cam.nearClipPlane = ChaseCamera.ViewNearClip(ChaseCamera.View.Cockpit, keepNear);
                    Vector3 eye = t.TransformPoint(ChaseCamera.MountOffset(ChaseCamera.View.Cockpit, c, s, shell != null ? shell.Def : null));
                    Quaternion eyeRot = t.rotation * Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Cockpit), 0f, 0f);
                    PSXScreenshotTool.Shot(cam, stem + "_cockpit", eye, eyeRot);

                    // Low behind, level horizon: the road's tilt against the land.
                    foreach (var cv in Object.FindObjectsByType<CockpitView>(FindObjectsSortMode.None)) cv.PreviewShow(false);
                    ChaseCamera.PreviewView(ChaseCamera.View.Chase);
                    cam.fieldOfView = 58f;
                    cam.nearClipPlane = keepNear;
                    Vector3 back = at - fwd * 8f + right * (lateral * 0.5f);
                    float backY = at.y + PSXRacingBuilder.RoadLift;
                    if (Physics.Raycast(back + Vector3.up * 3f, Vector3.down, out var hb, 8f, RoadMask, QueryTriggerInteraction.Ignore))
                        backY = hb.point.y;
                    Vector3 low = new Vector3(back.x, backY + 1.3f, back.z);
                    Vector3 look = seat + fwd * 12f + Vector3.up * 0.2f;
                    PSXScreenshotTool.Shot(cam, stem + "_low", low, Quaternion.LookRotation(look - low, Vector3.up));

                    log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0} wp {1} {2}: car roll {3:+0.0;-0.0} deg, lane road {4:0.000} m over the centre line's tarmac",
                        def.id, wp, name, roll, roadY - (at.y + PSXRacingBuilder.RoadLift)));
                    // THE BUILT CROSS-SECTION at this station, for the plots: the
                    // first surface under each point every 0.25 m across, as metres
                    // over the waypoint (+ = left of travel, the bake's sign).
                    t.position += Vector3.up * 60f;   // the car out of the rays
                    Physics.SyncTransforms();
                    var xs = new System.Text.StringBuilder("xsec " + def.id + " " + wp + " baseY " +
                        (at.y).ToString("0.000", CultureInfo.InvariantCulture) + ":");
                    float half = path.roadWidth * 0.5f;
                    for (float off = -(half + 3f); off <= half + 3f + 1e-3f; off += 0.25f)
                    {
                        Vector3 q = at - right * off;   // + off = left
                        string v = "nan";
                        if (Physics.Raycast(q + Vector3.up * 4f, Vector3.down, out var hx, 12f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                            v = (hx.point.y - at.y).ToString("0.000", CultureInfo.InvariantCulture) + "/" + hx.collider.name;
                        xs.Append(' ').Append(off.ToString("0.00", CultureInfo.InvariantCulture)).Append('=').Append(v);
                    }
                    log.AppendLine(xs.ToString());
                }
                ChaseCamera.PreviewView(keepView);
                cam.fieldOfView = keepFov;
                cam.nearClipPlane = keepNear;
            }
            string outPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "PSXRacing_bank_shots_" + tag + ".txt");
            System.IO.File.WriteAllText(outPath, log.ToString());
            Debug.Log("[BankShots] " + log);
        }
    }
}
