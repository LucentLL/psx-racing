using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;
using PSXRacing.City;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Damage round 2's DRIVE (a play check: watched). Charlotte free roam, a
    /// career red EG Civic, every die at 0.1, real PhysX, nothing planted:
    ///   1 along West Trade Street (the block nearest the Square), ten degrees
    ///     into its curb at 25 km/h: the front right wheel's side meets the
    ///     curb - WheelScuff reads the strike, the ledger banks that wheel's row;
    ///   2 nose-in to the nearest wall at 30 km/h: the nose crumpled, the bar
    ///     pushed back, the lamps - and the windscreen's web low in front;
    ///   3 back-in to it at 25 km/h: the tail, and the rear glass;
    ///   pictures after each, by day, then the same poses at night: outside,
    ///   the chase camera, the seat;
    ///   4 the drive ends (the fold), the editor leaves play, and the same car
    ///     is shown in MY CARS and on the car menu's stage.
    /// PSX_DMG_GLASS2=0 PSX_DMG_WHEELS=0 = the same drive before the round
    /// (PSX_D2_TAG names the folder). Report PSXRacing_damage2_drive_&lt;tag&gt;.txt,
    /// pictures Screenshots/damage2/drive_&lt;tag&gt;/.
    /// </summary>
    public static class Damage2Drive
    {
        internal static StringBuilder log = new StringBuilder();
        internal static int failures;
        internal static string tag = "after", outDir = "", ownedId = "";
        const string Street = "West Trade Street";

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }
        internal static void Note(string what) => log.AppendLine("  note " + what);

        internal static void Finish(int code)
        {
            log.AppendLine(failures == 0 ? "DAMAGE2 DRIVE: ALL HOLD." : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_damage2_drive_" + tag + ".txt"), log.ToString());
            Debug.Log(log.ToString());
            LifeSimManager.UseCareerSave();
            EditorApplication.Exit(code);
        }

        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            log = new StringBuilder();
            failures = 0;
            tag = System.Environment.GetEnvironmentVariable("PSX_D2_TAG");
            if (string.IsNullOrEmpty(tag)) tag = DamageGlass.On ? "after" : "before";
            outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "damage2", "drive_" + tag);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);
            log.AppendLine("damage2 drive: PSX_DMG_GLASS2 " + (DamageGlass.On ? "ON" : "0") + ", PSX_DMG_WHEELS " + (WheelScuff.On ? "ON" : "0") + ", tag " + tag);
            int roam = TrackCatalog.IndexOf("Charlotte");
            var scenes = EditorBuildSettings.scenes;
            int s = roam >= 0 ? TrackCatalog.SceneIndex(roam) : -1;
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path)) { Check(false, "the Charlotte scene is built"); Finish(1); return; }
            LifeSimManager.UseScratchSave("damage2drive");
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = true;
            RaceHandoff.TrackIndex = roam;
            RaceHandoff.CalendarDay = 0;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Noon;
            RaceHandoff.StartFuelPct = 100f;
            var spec = CarCatalog.Get("honda_civic_sir_ii__eg___91") ?? (CarCatalog.All.Count > 0 ? CarCatalog.All[0] : null);
            if (spec != null) RaceHandoff.CarSpecId = spec.id;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange st)
        {
            if (st == PlayModeStateChange.EnteredPlayMode)
            {
                new GameObject("Damage2DriveRunner").AddComponent<Damage2DriveRunner>();
                return;
            }
            if (st != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            // 4 the same car in the menus, from the save the drive folded into
            try
            {
                var s = LifeSimManager.State;
                var car = s != null ? s.FindCar(ownedId) : null;
                Check(car != null, "the car is in the career after the drive");
                if (car != null)
                {
                    var look = DamageLook.Read(car.faults, null);
                    Note("the menus read: panes " + look.panes + ", rash " + look.rash + ", zones " + look.zones + ", lamps " + look.lampOut);
                    var size = new[] { ("landscape_16x9", 1280, 720) };
                    LifeHomePreview.Shoot(outDir, "mycars", "garage", sizes: size, fields: new[] { ("garageShowId", (object)car.id) });
                    LifeHomePreview.Shoot(outDir, "carmenu", "carmenu", garageCar: car.id, sizes: size);
                    LifeHomePreview.Shoot(outDir, "specs", "specs", garageCar: car.id, sizes: size);
                }
            }
            catch (System.Exception e) { Check(false, "the menus THREW: " + e.Message); Debug.LogException(e); }
            Finish(failures == 0 ? 0 : 1);
        }

        internal static void LeavePlay() => EditorApplication.ExitPlaymode();
    }

    public class Damage2DriveRunner : MonoBehaviour
    {
        CarController car;
        Rigidbody rb;
        CollisionResponder resp;
        Camera cam;
        OwnedCar owned;
        CityWorld world;
        bool hold;
        float holdMps, steer;
        int groundMask = ~0;

        static void Check(bool ok, string what, object got = null) => Damage2Drive.Check(ok, what, got);
        static void Note(string what) => Damage2Drive.Note(what);
        static float[] Low() { var d = new float[DamageModel.Dice]; for (int i = 0; i < d.Length; i++) d[i] = 0.1f; return d; }

        void FixedUpdate()
        {
            if (car == null || rb == null) return;
            float vf = Vector3.Dot(rb.linearVelocity, car.transform.forward);
            car.throttleInput = hold ? Mathf.Clamp01(0.35f + (holdMps - vf) * 0.6f) : 0f;
            car.brakeInput = hold ? 0f : 1f;
            car.steerInput = steer;
            car.handbrakeInput = false;
            var tank = car.GetComponent<FuelTank>();
            if (tank != null) tank.percent = 100f;
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return Guarded(Body());
            RaceHandoff.ClearAll();
            Damage2Drive.LeavePlay();
        }

        IEnumerator Guarded(IEnumerator body)
        {
            while (true)
            {
                object cur = null;
                bool more;
                try { more = body.MoveNext(); if (more) cur = body.Current; }
                catch (System.Exception e)
                {
                    more = false;
                    Check(false, "a step THREW: " + e.GetType().Name + " - " + e.Message);
                    Debug.LogException(e);
                }
                if (!more) yield break;
                if (cur is IEnumerator nested) yield return Guarded(nested);
                else yield return cur;
            }
        }

        float GroundY(Vector3 at, float y0)
        {
            float top = float.NaN;
            foreach (var h in Physics.RaycastAll(new Vector3(at.x, y0 + 1.5f, at.z), Vector3.down, 6f, groundMask, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.attachedRigidbody != null) continue;
                if (float.IsNaN(top) || h.point.y > top) top = h.point.y;
            }
            return top;
        }

        void Shot(string name, Vector3 pos, Quaternion rot)
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) { Check(false, name + ": no camera"); return; }
            PSXScreenshotTool.ShotAs(cam, "damage2/drive_" + Damage2Drive.tag + "/" + name, pos, rot);
            Note("shot " + name);
        }

        void ShotAt(string name, Vector3 local, Vector3 lookLocal)
        {
            var t = car.transform;
            Vector3 p = t.TransformPoint(local);
            Shot(name, p, Quaternion.LookRotation(t.TransformPoint(lookLocal) - p, Vector3.up));
        }

        IEnumerator Place(Vector3 at, Quaternion rot)
        {
            hold = false;
            steer = 0f;
            car.TeleportTo(at + Vector3.up * 3f, rot);
            world.EnsureRing(at, 1);
            for (int k = 0; k < 6; k++) { car.TeleportTo(at + Vector3.up * 3f, rot); yield return null; }
            Physics.SyncTransforms();
            float gy = GroundY(at, at.y + 1f);
            if (!float.IsNaN(gy)) at.y = gy;
            car.TeleportTo(at + Vector3.up * 0.55f, rot);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            for (int k = 0; k < 40; k++) yield return new WaitForFixedUpdate();
        }

        IEnumerator Stop(float seconds)
        {
            hold = false;
            steer = 0f;
            float t = 0f;
            while (t < seconds) { t += Time.fixedDeltaTime; yield return new WaitForFixedUpdate(); }
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        string Rows(DamageLedger L) =>
            string.Join(" | ", L.incidents.Select(i => DamageModel.KindName(i.kind) + " " + DamageModel.ZoneName(i.zone) + (i.corner >= 0 ? " " + DamageModel.CornerNames[i.corner] : "") +
                                                        " dv " + i.dv.ToString("0.0") + " b" + i.band + " '" + i.what + "': " +
                                                        string.Join(",", i.outcome.rows.Select(r => r.id + " s" + r.stage))));

        IEnumerator Pictures(string when, DamageLook look)
        {
            // outside, in front of the nose and beside the tail; the chase camera; the seat
            ShotAt("front_" + when, new Vector3(-1.9f, 1.45f, 4.6f), new Vector3(0f, 0.85f, 0.6f));
            ShotAt("rear_" + when, new Vector3(2.2f, 1.5f, -5.4f), new Vector3(0f, 0.8f, -0.4f));
            ShotAt("wheel_far_" + when, new Vector3(4.2f, 1.2f, 4.6f), new Vector3(0.6f, 0.4f, 0.9f));
            ChaseCamera.SetView(ChaseCamera.View.Chase);
            yield return new WaitForSeconds(1.5f);
            if (cam != null) Shot("chase_" + when, cam.transform.position, cam.transform.rotation);
            ChaseCamera.SetView(ChaseCamera.View.Cockpit);
            yield return new WaitForSeconds(2.5f);
            if (cam != null) Shot("cockpit_" + when, cam.transform.position, cam.transform.rotation);
            if (when == "day")
            {
                var ci = car.GetComponentInChildren<CockpitInterior>(true);
                bool crack = ci != null && ci.GetComponentsInChildren<Transform>(true).Any(x => x.name == "GlassCrack");
                if (DamageGlass.On)
                    Check(look.CrackLevel == DamageGlass.Level(look.Now.panes.x) && (look.CrackLevel > 0) == crack,
                          "from the seat: the windscreen's break at its level", "level " + look.CrackLevel + ", quad " + crack);
                else Note("from the seat (before): crack quad " + crack);
            }
            ChaseCamera.SetView(ChaseCamera.View.Chase);
            yield return new WaitForSeconds(0.5f);
        }

        IEnumerator Body()
        {
            CityMode mode = null;
            for (int i = 0; i < 600 && (mode == null || mode.player == null || mode.world == null); i++) { mode = CityMode.Instance; yield return null; }
            Check(mode != null && mode.player != null && mode.world != null, "a free-roam session in Charlotte");
            if (mode == null || mode.player == null || mode.world == null) yield break;
            car = mode.player;
            world = mode.world;
            resp = car.GetComponent<CollisionResponder>();
            rb = car.GetComponent<Rigidbody>();
            cam = Camera.main;
            groundMask = car.SuspensionMask != 0 ? car.SuspensionMask : ~0;
            foreach (var mb in car.GetComponents<MonoBehaviour>())
                if (mb is PlayerCarInput || mb is StuckRecovery) mb.enabled = false;
            foreach (var sr in car.GetComponentsInChildren<StuckRecovery>(true)) sr.enabled = false;
            car.manualMode = false;

            // the career car, armed the real way
            var s = LifeSimManager.State;
            var spec = CarCatalog.Get(RaceHandoff.CarSpecId) ?? CarCatalog.All[0];
            owned = CarMarket.MakeOwnedCar(s, spec, 92, 88000f, spec.price);
            owned.id = "d2#drive";
            owned.faults.Clear();
            owned.carHP = owned.paint = owned.radiator = 92f;
            var def = Paint.DefFor(spec);
            if (def != null)
                for (int i = 0; i < def.SkinCount; i++)
                    if (Paint.LabelOf(def, i).ToUpperInvariant() == "RED") { owned.paintSkin = def.skinNames[i]; break; }
            s.activeCar = owned.id;
            Damage2Drive.ownedId = owned.id;
            RaceHandoff.CarId = owned.id;
            var cb = car.GetComponent<CarBody>();
            if (cb != null && def != null) cb.Apply(def, Paint.SkinFor(owned, spec, def));
            foreach (var p in DamageBank.Files()) File.Delete(p);
            resp.Damage = null;
            resp.Ledger = null;
            resp.ResetDamage();
            CrashStopGap.Arm(car);
            var L = resp.Damage;
            Check(L != null && L.Banks, "the career car's ledger armed");
            if (L == null) yield break;
            L.DrawOverride = Low;
            var look = car.GetComponent<DamageLook>();
            var scuff = car.GetComponent<WheelScuff>();
            Check(look != null, "DamageLook rides the car");
            Check((scuff != null) == WheelScuff.On, WheelScuff.On ? "WheelScuff rides the car" : "PSX_DMG_WHEELS=0: no WheelScuff on the car");
            if (look == null) yield break;
            Note("car " + spec.name + ", " + rb.mass.ToString("0") + " kg; every die 0.1; HD tier " + CarHD.Want);

            // the street and its curb
            var map = world.Map;
            CityMap.Edge edge = null;
            float best = float.MaxValue;
            foreach (var e in map.edges)
            {
                if (e.a == e.b || e.length < 60f || e.name != "West Trade Street" || !CityMeshes.WalkEdge(map, e)) continue;
                float d = Vector2.Distance(e.PointAt(e.length * 0.5f), map.uptown);
                if (d < best) { best = d; edge = e; }
            }
            Check(edge != null, "a block of West Trade Street with a walk", edge != null ? "edge " + edge.index + ", " + edge.length.ToString("0") + " m, " + best.ToString("0") + " m from the Square" : "none");
            if (edge == null) yield break;
            float s0 = edge.length * 0.5f;
            Vector2 c2 = edge.PointAt(s0), t2 = edge.TangentAt(s0).normalized;
            Vector3 centre = new Vector3(c2.x, edge.YAt(s0), c2.y), dir = new Vector3(t2.x, 0f, t2.y);
            yield return Place(centre, Quaternion.LookRotation(dir, Vector3.up));
            float roadY = GroundY(centre, centre.y + 1f);
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            float foot = float.NaN;
            for (int pass = 0; pass < 2 && float.IsNaN(foot); pass++)
            {
                for (float d = 1.5f; d < 16f; d += 0.05f)
                {
                    float y = GroundY(centre + right * d, roadY + 0.5f);
                    if (!float.IsNaN(y) && y - roadY >= 0.10f && y - roadY < 0.5f) { foot = d; break; }
                }
                if (float.IsNaN(foot)) { dir = -dir; right = -right; }
            }
            Check(!float.IsNaN(foot), "its curb, to the right of the lane", float.IsNaN(foot) ? "none" : foot.ToString("0.00") + " m right of the centre line");
            if (float.IsNaN(foot)) yield break;
            var prof = new StringBuilder();
            for (float d = foot - 0.5f; d <= foot + 0.5f; d += 0.1f)
                prof.Append(((GroundY(centre + right * d, roadY + 0.5f) - roadY) * 100f).ToString("0")).Append(' ');
            Note("the curb's collider, cm over the road every 10 cm across it: " + prof);
            Note("place: " + WheelScuff.Place(centre));

            // 1 ten degrees into the curb at 25 km/h
            float halfTrack = car.TrackOf(0) * 0.5f, halfW = cb != null ? cb.TyreHalfWidth(1) : 0.1f;
            const float V = 25f / 3.6f, Ang = 10f;
            Quaternion into = Quaternion.LookRotation(Quaternion.AngleAxis(Ang, Vector3.up) * dir, Vector3.up);
            Vector3 start = centre - dir * 18f + right * (foot - 0.35f - 1.5f - halfTrack - halfW);
            yield return Place(start, into);
            int strikes0 = scuff != null ? scuff.Strikes : 0, inc0 = L.incidents.Count;
            car.SetRolling(V);
            rb.linearVelocity = car.transform.forward * V;
            hold = true; holdMps = V;
            float run = 0f, struckAt = -1f;
            while (run < 6f)
            {
                run += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
                bool struck = scuff != null ? scuff.Strikes > strikes0 : false;
                if (struck && struckAt < 0f) struckAt = run;
                if (struckAt >= 0f && run - struckAt > 0.5f) break;
                if (scuff == null && run > 3.2f) break;
            }
            steer = -0.35f;
            for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
            yield return Stop(1.5f);
            var wheelInc = L.incidents.FirstOrDefault(i => i.kind == DamageModel.KWheel);
            if (WheelScuff.On)
            {
                Check(wheelInc != null && wheelInc.corner == 1 && wheelInc.outcome.rows.Count == 1 && wheelInc.outcome.rows[0].id == "wheel_rash_fr",
                      "the front right wheel's side met the curb: its row, from the contact", Rows(L));
                Check(wheelInc != null && wheelInc.what.StartsWith("curb") && wheelInc.what.Contains("West Trade"), "  its cause and place", wheelInc != null ? wheelInc.what : "none");
                if (scuff != null) Note("pressed in at " + scuff.PressOf(1).ToString("0.00") + " m/s, rubbed " + scuff.RubOf(1).ToString("0.00") + " m, stage " + scuff.ToldOf(1) + ", struck " + struckAt.ToString("0.00") + " s into the run");
                Check(Mathf.Approximately(look.Now.rash.y, wheelInc != null ? wheelInc.band / 3f : -1f) || Mathf.Approximately(DamageLook.Read(owned.faults, L).rash.y, wheelInc != null ? wheelInc.band / 3f : -1f),
                      "  the drive shows it on that wheel", look.Now.rash);
            }
            else Note("before: the same line; incidents " + (L.incidents.Count - inc0) + " (" + Rows(L) + ")");
            yield return new WaitForSeconds(0.6f);
            Vector3 hubFR = car.transform.InverseTransformPoint(car.wheelHubs[1].position);
            ShotAt("wheel_close_day", hubFR + new Vector3(1.25f, 0.22f, 0.55f), hubFR);
            ShotAt("wheel_chase_day", new Vector3(3.6f, 1.5f, 6.2f), new Vector3(0.5f, 0.4f, 1.0f));

            // 2 nose-in to the nearest wall at 30 km/h
            Vector3 from = car.transform.position + Vector3.up * 0.8f;
            RaycastHit wall = default;
            float wallD = float.MaxValue;
            int wallMask = ~((1 << 2) | (1 << CityTrees.FoliageLayer));
            for (int a = 0; a < 360; a += 10)
            {
                Vector3 d = Quaternion.AngleAxis(a, Vector3.up) * dir;
                foreach (var h in Physics.RaycastAll(from, d, 70f, wallMask, QueryTriggerInteraction.Ignore))
                {
                    if (h.collider.attachedRigidbody != null || Mathf.Abs(h.normal.y) > 0.3f || h.distance < 12f) continue;
                    if (Vector3.Dot(h.normal, -d) < 0.92f) continue;                     // near square on
                    if (Physics.Raycast(from + Vector3.up * 0.6f, d, h.distance - 0.5f, wallMask, QueryTriggerInteraction.Ignore)) continue;   // a clear run to it
                    if (!Physics.Raycast(h.point + h.normal * 0.2f + Vector3.up * 1.2f, -h.normal, 0.6f, wallMask, QueryTriggerInteraction.Ignore)) continue;   // it stands over the bonnet too
                    if (h.distance < wallD) { wallD = h.distance; wall = h; }
                }
            }
            Check(wall.collider != null, "a wall to meet nose-on", wall.collider != null ? wall.collider.name + ", " + wallD.ToString("0") + " m off" : "none within 70 m");
            if (wall.collider != null)
            {
                Vector3 n = Vector3.ProjectOnPlane(wall.normal, Vector3.up).normalized;
                Vector3 foot3 = new Vector3(wall.point.x, roadY, wall.point.z);
                yield return Place(foot3 + n * 11f, Quaternion.LookRotation(-n, Vector3.up));
                int before = L.incidents.Count;
                const float V2 = 30f / 3.6f;
                car.SetRolling(V2);
                rb.linearVelocity = car.transform.forward * V2;
                hold = true; holdMps = V2;
                float t = 0f;
                while (t < 6f && !L.incidents.Skip(before).Any(i => i.kind == DamageModel.KHit)) { t += Time.fixedDeltaTime; yield return new WaitForFixedUpdate(); }
                yield return Stop(1.5f);
                Note("30 km/h nose-in: " + Rows(L));
                Check(look.Now.zones.x >= 0.66f && (look.Now.lampOut & 3) != 0, "30 km/h nose-in: the front dented or worse, a headlamp out",
                      "front " + look.Now.zones.x.ToString("0.00") + ", lamps " + look.Now.lampOut);
                if (DamageGlass.On)
                    Check(look.Now.panes.x >= DamageGlass.Crack && look.Now.hitW.y < 0.3f, "  and the windscreen broken low in front (the nose crumpled; a web with the bar pushed back)",
                          "windscreen " + look.Now.panes.x.ToString("0.00") + " at " + look.Now.hitW);
                yield return new WaitForSeconds(0.6f);
                // back off the wall for the pictures, then 3 back-in at 25 km/h
                yield return Place(foot3 + n * 6.5f, Quaternion.LookRotation(n, Vector3.up));
                before = L.incidents.Count;
                rb.linearVelocity = -car.transform.forward * (25f / 3.6f);
                t = 0f;
                hold = false;
                car.brakeInput = 0f;
                while (t < 4f && !L.incidents.Skip(before).Any(i => i.kind == DamageModel.KHit)) { t += Time.fixedDeltaTime; car.brakeInput = 0f; rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, -car.transform.forward * (25f / 3.6f), 0.2f); yield return new WaitForFixedUpdate(); }
                yield return Stop(1.5f);
                Note("25 km/h back-in: " + Rows(L));
                Check(look.Now.zones.y >= 0.66f, "25 km/h back-in: the tail dented or worse", look.Now.zones.y.ToString("0.00"));
                if (DamageGlass.On) Note("the rear glass: " + look.Now.panes.y.ToString("0.00") + " (0.5 crazed, 1 shattered)");
                yield return Place(foot3 + n * 9f + Vector3.Cross(Vector3.up, n) * 0.5f, Quaternion.LookRotation(Vector3.Cross(Vector3.up, n), Vector3.up));
            }
            yield return new WaitForSeconds(0.8f);
            yield return Pictures("day", look);

            // the same poses at night
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (globals != null && globals.sun != null) { TimeOfDay.Apply(TimeOfDay.Night, globals.sun); globals.Apply(); }
            yield return new WaitForSeconds(1.5f);
            yield return Pictures("night", look);
            hubFR = car.transform.InverseTransformPoint(car.wheelHubs[1].position);
            ShotAt("wheel_close_night", hubFR + new Vector3(1.25f, 0.22f, 0.55f), hubFR);

            // 4 the drive ends: folded onto the career
            int folded = CrashStopGap.FoldDamage(car);
            Note("folded " + folded + "; rows " + string.Join(", ", owned.faults.Select(f => f.id + " s" + f.stage + " '" + f.causeNote + "' $" + f.cost + (f.hidden ? " hidden" : ""))));
            var after = DamageLook.Read(owned.faults, null);
            Check(after.panes == look.Now.panes && after.rash == look.Now.rash && after.zones == look.Now.zones && after.lampOut == look.Now.lampOut,
                  "the banked rows alone give the same look (the menus, the next drive)",
                  after.panes + " " + after.rash + " vs " + look.Now.panes + " " + look.Now.rash);
            if (WheelScuff.On)
                Check(owned.faults.Any(f => f.id == "wheel_rash_fr" && f.hidden && f.cause == FaultCause.Impact && f.causeNote.Contains("rim: curb")),
                      "the wheel's row is on the car: hidden, with its cause and place");
            LifeSimManager.Save();
        }
    }
}
