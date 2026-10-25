using UnityEditor;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The mounted views with a car nosed into ours (owner, 2026-10-16: "Hood
    /// cam makes hood seem comparatively small compared to other cars",
    /// "Cockpit cam does not give player's car a physical body", "bumper view
    /// sees through objects when touching"). One venue (PSX_SHOT_VENUE, else
    /// BeechGap), three player shells, a rival parked head-on with its
    /// collider touching ours, as in his screenshots. Per shell:
    ///   cockpit, clear road and the rival at the bumper (the cut shell);
    ///   bumper, the raw lens and the lens ChaseCamera.BumperOverlap pulls back;
    ///   hood, the lens as the game puts it (MountOffset) and the candidates
    ///   in PSX_HOOD_CANDS ("setback,rise,pitch;..." off the cowl line).
    /// Writes Screenshots/contact_&lt;shell&gt;_&lt;view&gt;.png and logs each lens.
    /// </summary>
    public static class CameraContactShots
    {
        [MenuItem("PSX Racing/Capture Contact Views")]
        public static void Capture()
        {
            var def = TrackCatalog.At(TrackCatalog.CheckVenue);
            string only = System.Environment.GetEnvironmentVariable("PSX_SHOT_VENUE");
            if (string.IsNullOrEmpty(only)) only = "BeechGap";
            foreach (var d in TrackCatalog.All) if (d.id == only) { def = d; break; }
            if (!PSXScreenshotTool.Open(def, out var cam, out var player)) return;

            var output = Object.FindFirstObjectByType<PSXCameraOutput>();
            if (output != null) output.height = 540;
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (globals != null && globals.sun != null) { TimeOfDay.Apply(TimeOfDay.Noon, globals.sun); globals.Apply(); }

            var t = player.transform;
            var car = player.GetComponent<CarController>();
            var box = player.GetComponent<BoxCollider>();
            var body = player.GetComponent<CarBody>();
            var cv = Object.FindFirstObjectByType<CockpitView>();
            float keepNear = cam.nearClipPlane;
            PSXScreenshotTool.AspectOverride = 16f / 9f;

            // The rival: a copy of the player's car with nothing that drives.
            var rival = Object.Instantiate(player);
            rival.name = "ContactRival";
            foreach (var ci0 in rival.GetComponentsInChildren<CockpitInterior>(true)) Object.DestroyImmediate(ci0.gameObject);
            var rBody = rival.GetComponent<CarBody>();
            var rBox = rival.GetComponent<BoxCollider>();

            string cands = System.Environment.GetEnvironmentVariable("PSX_HOOD_CANDS");
            if (cands == null) cands = "";
            string shells = System.Environment.GetEnvironmentVariable("PSX_CONTACT_SHELLS");
            if (string.IsNullOrEmpty(shells)) shells = "rx7_fd,gto_66,euro_hatch";
            string rivalKey = System.Environment.GetEnvironmentVariable("PSX_CONTACT_RIVAL");
            if (string.IsNullOrEmpty(rivalKey)) rivalKey = "gto_66";
            if (rBody != null) rBody.Apply(CarModelLibrary.Load(rivalKey), 1);

            CockpitInterior ci = null;
            ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
            foreach (var key in shells.Split(','))
            {
                var shell = CarModelLibrary.Load(key.Trim());
                if (shell == null || body == null) { Debug.LogError("[Contact] no shell " + key); continue; }
                body.Apply(shell, 0);
                Vector3 c = box.center, s = box.size;
                float ls = body.LengthScale, lift = body.BodyLift;

                // Head-on, colliders touching.
                float ourFront = c.z + s.z * 0.5f;
                float theirFront = rBox.center.z + rBox.size.z * 0.5f;
                rival.transform.SetPositionAndRotation(t.TransformPoint(new Vector3(0f, 0f, ourFront + theirFront)),
                                                       t.rotation * Quaternion.Euler(0f, 180f, 0f));
                Physics.SyncTransforms();

                // ---- cockpit
                if (cv != null) cv.PreviewShow(true);
                if (ci == null) ci = player.GetComponentInChildren<CockpitInterior>(true);
                if (ci == null) { Debug.LogError("[Contact] no CockpitInterior on the player"); break; }
                ci.Refit();
                ci.Show(true, true);
                ci.SetSteer(0f, 34f);
                ci.Tick(0f, null);
                foreach (var gc in Object.FindObjectsByType<GaugeCluster>(FindObjectsSortMode.None)) gc.Build();
                Canvas.ForceUpdateCanvases();
                Vector3 eye = ChaseCamera.MountOffset(ChaseCamera.View.Cockpit, c, s, shell, ls, lift, false);
                var eyeRot = t.rotation * Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Cockpit), 0f, 0f);
                cam.fieldOfView = ChaseCamera.FOVFor(ChaseCamera.View.Cockpit, 58f, 0f, 1f, 16f / 9f);
                cam.nearClipPlane = CockpitInterior.NearClipFor(ci.Current.scale);
                foreach (bool withRival in new[] { false, true })
                {
                    rival.SetActive(withRival);
                    ci.HideShell(true);
                    PSXScreenshotTool.ShotAs(cam, $"contact_{key}_cockpit{(withRival ? "_rival" : "")}", t.TransformPoint(eye), eyeRot);
                    ci.HideShell(false);
                }
                rival.SetActive(true);
                Vector2 cowl = ChaseCamera.CowlLine(c, s, shell, ls, lift, out bool measured);
                Debug.Log($"[Contact] {key}: eye {eye.x:0.000},{eye.y:0.000},{eye.z:0.000} scale {ci.Current.scale:0.000} " +
                          $"cut {ci.Current.cutZ:0.000} cowl z {cowl.x:0.000} y {cowl.y:0.000} measured {measured} " +
                          $"roof {(shell.roofY + lift):0.000} nose {(shell.noseZ * ls):0.000} drop {CockpitInterior.SightDrop:0.000}");
                ci.Show(false, false);
                if (cv != null) cv.PreviewShow(false);

                // ---- bumper, raw and pulled back
                Vector3 bump = ChaseCamera.MountOffset(ChaseCamera.View.Bumper, c, s, shell, ls, lift, false);
                float pull = ChaseCamera.BumperOverlap(t, bump, Mathf.Max(c.z, cowl.x));
                cam.fieldOfView = ChaseCamera.FOVFor(ChaseCamera.View.Bumper, 58f, 0f, 1f, 16f / 9f);
                cam.nearClipPlane = ChaseCamera.ViewNearClip(ChaseCamera.View.Bumper, keepNear);
                var bRot = t.rotation * Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Bumper), 0f, 0f);
                PSXScreenshotTool.ShotAs(cam, $"contact_{key}_bumper_raw", t.TransformPoint(bump), bRot);
                PSXScreenshotTool.ShotAs(cam, $"contact_{key}_bumper_pulled", t.TransformPoint(bump - Vector3.forward * pull), bRot);
                rival.SetActive(false); Physics.SyncTransforms();
                float clearPull = ChaseCamera.BumperOverlap(t, bump, Mathf.Max(c.z, cowl.x));
                rival.SetActive(true); Physics.SyncTransforms();
                Debug.Log($"[Contact] {key}: bumper lens z {bump.z:0.000} pull {pull:0.000} (clear road {clearPull:0.000})");

                // ---- hood: the game's lens, then the candidates
                cam.fieldOfView = ChaseCamera.FOVFor(ChaseCamera.View.Hood, 58f, 0f, 1f, 16f / 9f);
                cam.nearClipPlane = ChaseCamera.ViewNearClip(ChaseCamera.View.Hood, keepNear);
                Vector3 hood = ChaseCamera.MountOffset(ChaseCamera.View.Hood, c, s, shell, ls, lift, false);
                var hs = ChaseCamera.HoodFor(c, s, shell, ls, lift);
                if (hs.cut) CabinClip.Open(t, hs.cutZ, c, s);
                PSXScreenshotTool.ShotAs(cam, $"contact_{key}_hood_game", t.TransformPoint(hood),
                                         t.rotation * Quaternion.Euler(hs.pitch, 0f, 0f));
                CabinClip.Close();
                Vector2 edge = ChaseCamera.BonnetEdge(shell, ls, lift);
                Debug.Log($"[Contact] {key}: hood lens {hood.y:0.000},{hood.z:0.000} pitch {hs.pitch:0.0} back {(hs.cutZ - hood.z):0.00} " +
                          $"cut {hs.cut} reached {hs.reached} bonnet edge z {edge.x:0.000} y {edge.y:0.000}");
                int n = 0;
                foreach (var cand in cands.Split(';'))
                {
                    var p = cand.Split(',');
                    if (p.Length < 3) continue;
                    float back = float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture);
                    float rise = float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
                    float pitch = float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture);
                    Vector3 lens = new Vector3(0f, cowl.y + rise, cowl.x - back);
                    if (back > -0.04f) CabinClip.Open(t, cowl.x, c, s);
                    PSXScreenshotTool.ShotAs(cam, $"contact_{key}_hood_c{++n}", t.TransformPoint(lens),
                                             t.rotation * Quaternion.Euler(pitch, 0f, 0f));
                    CabinClip.Close();
                    Debug.Log($"[Contact] {key}: hood candidate {n} back {back} rise {rise} pitch {pitch}");
                }
            }
            Object.DestroyImmediate(rival);
            PSXScreenshotTool.AspectOverride = 0f;
            cam.nearClipPlane = keepNear;
            Debug.Log("[Contact] shots of " + def.id + " written");
        }
    }
}
