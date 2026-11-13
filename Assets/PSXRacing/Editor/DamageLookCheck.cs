using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// R1a part 2a's picture check: a watched editor in play on the check venue's
    /// flat strip (as DamagePlayCheck, plus a visible ground), a career car armed
    /// the real way (CrashStopGap.Arm), every die at 0.1, real PhysX:
    ///   1 NIGHT, a 10 mph nose-in: a lamp_out row, that headlamp out, the bags fired;
    ///   2 noon, a 30 km/h nose-in (band 4): the front at its worst, both headlamps
    ///     out, the core crushed; the engine hot: steam from under the bonnet;
    ///   3 a 20 km/h back-in: the rear crumpled, seen from the chase camera;
    ///   4 the cockpit: the driver's bag on the wheel, the passenger's on the dash;
    ///   F the drive folded onto the career: srs_deployed hidden and stamped.
    /// PSX_LOOK_CAR = a spec id (default the '99 Viper GTS: dual bags, a lamp map).
    /// Shots: Screenshots/look_*.png; report PSXRacing_damage_look.txt.
    /// </summary>
    public static class DamageLookCheck
    {
        internal static StringBuilder log = new StringBuilder();
        internal static int failures;

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Note(string what) => log.AppendLine("  note " + what);

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "LOOK CHECK: ALL HOLD." : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_damage_look.txt"), log.ToString());
            Debug.Log(log.ToString());
        }

        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            var scenes = EditorBuildSettings.scenes;
            int s = TrackCatalog.SceneIndex(TrackCatalog.CheckVenue);
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                Check(false, "the check venue is built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }
            LifeSimManager.UseScratchSave("damagelook");
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = TrackCatalog.CheckVenue;
            RaceHandoff.CalendarDay = 0;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Night;
            RaceHandoff.Solo = true;
            string want = System.Environment.GetEnvironmentVariable("PSX_LOOK_CAR");
            if (string.IsNullOrEmpty(want)) want = "dodge_viper_gts__99";
            var spec = CarCatalog.Get(want) ?? (CarCatalog.All.Count > 0 ? CarCatalog.All[0] : null);
            if (spec != null) RaceHandoff.CarSpecId = spec.id;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("DamageLookRunner").AddComponent<DamageLookRunner>();
        }
    }

    public class DamageLookRunner : MonoBehaviour
    {
        CarController car;
        CollisionResponder resp;
        Rigidbody rb;
        EngineTemp temp;
        Camera cam;
        bool hold;
        float holdMps, holdC = -1f, surfaceY;
        OwnedCar owned;
        readonly List<GameObject> props = new List<GameObject>();

        static void Check(bool ok, string what, object got = null) => DamageLookCheck.Check(ok, what, got);
        static void Note(string what) => DamageLookCheck.Note(what);
        static float[] Low() { var d = new float[DamageModel.Dice]; for (int i = 0; i < d.Length; i++) d[i] = 0.1f; return d; }

        void FixedUpdate()
        {
            if (car == null || rb == null) return;
            float vf = Vector3.Dot(rb.linearVelocity, car.transform.forward);
            car.throttleInput = hold ? Mathf.Clamp01(0.35f + (holdMps - vf) * 0.6f) : 0f;
            car.brakeInput = 0f;
            car.steerInput = 0f;
            car.handbrakeInput = false;
            var tank = car.GetComponent<FuelTank>();
            if (tank != null) tank.percent = 100f;
            if (temp != null && holdC > 0f) temp.celsius = holdC;
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return Guarded(Body());
            foreach (var p in props) if (p != null) Destroy(p);
            RaceHandoff.ClearAll();
            DamageLookCheck.Finish();
            EditorApplication.Exit(DamageLookCheck.failures == 0 ? 0 : 1);
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

        static string Rows(DamageLedger L) =>
            string.Join(" | ", L.incidents.Select(i => DamageModel.ZoneName(i.zone) + " dv " + i.dv.ToString("0.0") + " b" + i.band +
                                                        " cos " + i.frontCos.ToString("0.00") + ": " + i.outcome));

        void Shot(string name, Vector3 pos, Quaternion rot)
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) { Check(false, name + ": no camera"); return; }
            PSXScreenshotTool.ShotAs(cam, name, pos, rot);
            Note("shot " + name);
        }

        void ShotAt(string name, Vector3 local, Vector3 lookLocal)
        {
            var t = car.transform;
            Vector3 p = t.TransformPoint(local);
            Shot(name, p, Quaternion.LookRotation(t.TransformPoint(lookLocal) - p, Vector3.up));
        }

        GameObject Ground(Material mat)
        {
            var go = new GameObject("LookGround");
            go.transform.position = new Vector3(-600f, surfaceY + 0.01f, -14000f);
            var m = new Mesh { name = "LookGround" };
            const float W = 30f, Z0 = -40f, Z1 = 90f, T = 0.25f;
            m.vertices = new[] { new Vector3(-W, 0, Z0), new Vector3(-W, 0, Z1), new Vector3(W, 0, Z1), new Vector3(W, 0, Z0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(0, (Z1 - Z0) * T), new Vector2(2 * W * T, (Z1 - Z0) * T), new Vector2(2 * W * T, 0) };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            props.Add(go);
            return go;
        }

        IEnumerator Hit(float v, bool nose, string label)
        {
            var L = resp.Damage;
            int before = L.incidents.Count;
            var t = car.transform;
            Vector3 at = t.position + (nose ? t.forward * 6.6f : -t.forward * 4.8f);
            at.y = surfaceY + 1.2f;
            var wall = new GameObject("LookWall");
            wall.transform.SetPositionAndRotation(at, Quaternion.LookRotation(t.forward, Vector3.up));
            wall.AddComponent<BoxCollider>().size = new Vector3(6f, 2.4f, 1f);
            props.Add(wall);
            yield return new WaitForFixedUpdate();
            if (nose) { car.SetRolling(v); hold = true; holdMps = v; }
            else { hold = false; rb.linearVelocity = -t.forward * v; }
            float s = 0f;
            while (s < 6f && L.incidents.Count == before) { s += Time.fixedDeltaTime; yield return new WaitForFixedUpdate(); }
            hold = false;
            for (int i = 0; i < 75; i++) yield return new WaitForFixedUpdate();
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            Destroy(wall);
            Note(label + ": " + Rows(L));
        }

        IEnumerator Body()
        {
            for (int i = 0; i < 25; i++) yield return null;
            var rm = RaceManager.Instance;
            car = rm != null ? rm.playerCar : null;
            Check(car != null, "there is a player car");
            if (car == null) yield break;
            resp = car.GetComponent<CollisionResponder>();
            rb = car.GetComponent<Rigidbody>();
            temp = car.GetComponent<EngineTemp>();
            cam = Camera.main;
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) input.enabled = false;
            foreach (var sr in car.GetComponentsInChildren<StuckRecovery>(true)) sr.enabled = false;
            car.manualMode = false;
            Check(DamageModel.Live && DamageLook.On, "the located model and the look are on");

            // the strip (DamagePlayCheck's), and a ground to see it on
            var strip = new GameObject("LookStrip");
            strip.layer = car.roadLayer;
            strip.transform.position = new Vector3(0f, 4000f, 0f);
            strip.AddComponent<BoxCollider>().size = new Vector3(3000f, 2f, 30000f);
            surfaceY = 4001f;
            props.Add(strip);
            var lit = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Where(r => r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name == "PSX/Lit")
                .OrderByDescending(r => r.name.ToLowerInvariant().Contains("road") ? 1 : 0).FirstOrDefault();
            if (lit != null) Ground(lit.sharedMaterial);
            Note("ground: " + (lit != null ? lit.name + " / " + lit.sharedMaterial.name : "none"));

            var s = LifeSimManager.State;
            var spec = CarCatalog.Get(RaceHandoff.CarSpecId) ?? CarCatalog.All[0];
            owned = CarMarket.MakeOwnedCar(s, spec, 80, 40000f, spec.price);
            owned.id = "look#play";
            owned.faults.Clear();
            owned.carHP = owned.paint = owned.radiator = 80f;
            s.activeCar = owned.id;
            RaceHandoff.CarId = owned.id;
            foreach (var p in DamageBank.Files()) File.Delete(p);
            car.TeleportTo(new Vector3(-600f, surfaceY + 0.6f, -14000f), Quaternion.identity);
            for (int i = 0; i < 20; i++) yield return new WaitForFixedUpdate();
            resp.Damage = null;
            resp.Ledger = null;
            resp.ResetDamage();
            CrashStopGap.Arm(car);
            var L = resp.Damage;
            Check(L != null, "the career car's ledger armed (CrashStopGap.Arm -> DamageLedger)");
            if (L == null) yield break;
            L.DrawOverride = Low;
            var look = car.GetComponent<DamageLook>();
            Check(look != null, "DamageLook rides the armed car");
            if (look == null) yield break;
            var lights = car.GetComponentInChildren<CarLights>(true);
            var fact = CarFacts.Get(spec.id);
            Note("car " + spec.name + " (" + spec.id + "), airbags " + (fact != null ? fact.airbags : "?") + ", " +
                 rb.mass.ToString("0") + " kg; every die 0.1; lamp map " + (lights != null && lights.HasLampMap) +
                 ", heads on it " + (lights != null && lights.HasHeadMap));

            // 1 NIGHT: a 10 mph nose-in
            yield return Hit(4.47f, true, "10 mph nose-in");
            yield return new WaitForSeconds(0.5f);
            Check(L.incidents.Any(i => i.outcome.HasRow(DamageModel.LampId)), "10 mph nose-in: a lamp_out row");
            Check((look.Now.lampOut & 3) != 0 && lights != null && lights.lampOut == look.Now.lampOut,
                  "  that headlamp is out (CarLights.lampOut = the look's)", look.Now.lampOut + " / " + (lights != null ? lights.lampOut : -1));
            Check(L.incidents.Any(i => i.outcome.HasRow(SrsBag.Id)) && look.Now.bags == 2, "  the bags fired (frontal, dual)", look.Now.bags);
            ShotAt("look_1_night_lamp", new Vector3(1.4f, 1.1f, 9.5f), new Vector3(0f, 0.55f, 1.6f));

            // noon for the rest
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (globals != null && globals.sun != null) { TimeOfDay.Apply(TimeOfDay.Noon, globals.sun); globals.Apply(); }
            yield return null;

            // 2 a 30 km/h nose-in, then a hot engine
            yield return Hit(30f / 3.6f, true, "30 km/h nose-in");
            Check(look.Now.zones.x >= 0.99f && (look.Now.lampOut & 3) == 3 && look.Now.radStage >= 3,
                  "30 km/h nose-in (band 4): the front at its worst, both headlamps out, the core crushed",
                  "front " + look.Now.zones.x.ToString("0.00") + ", lamps " + look.Now.lampOut + ", radiator s" + look.Now.radStage);
            holdC = 108f;
            yield return new WaitForSeconds(3f);
            var steam = car.GetComponentsInChildren<ParticleSystem>(true).FirstOrDefault(p => p.name == "RadiatorSteam");
            Check(steam != null && steam.particleCount > 0, "  hot (108 C): steam from under the bonnet", steam != null ? steam.particleCount : -1);
            ShotAt("look_2_steam", new Vector3(3.6f, 1.8f, 6.2f), new Vector3(0f, 0.8f, 1.2f));

            // 3 a 20 km/h back-in: the rear, from the chase camera
            yield return Hit(20f / 3.6f, false, "20 km/h back-in");
            Check(look.Now.zones.y >= 0.66f, "20 km/h back-in: the rear dented or worse", look.Now.zones.y.ToString("0.00"));
            holdC = -1f;
            ChaseCamera.SetView(ChaseCamera.View.Chase);
            yield return new WaitForSeconds(1.5f);
            if (cam != null) Shot("look_4_chase_panels", cam.transform.position, cam.transform.rotation);

            // a roof strike (planted, 4 m/s): roof_glass - the dented roof and, from the seat, a cracked windscreen
            L.PlantHit(DamageModel.ZRoof, 0, -1, 4f, Time.time);
            yield return new WaitForSeconds(0.5f);
            Check(look.Now.roof > 0f, "a planted 4 m/s roof strike: roof_glass shows", look.Now.roof.ToString("0.00"));

            // 4 the cockpit
            ChaseCamera.SetView(ChaseCamera.View.Cockpit);
            yield return new WaitForSeconds(2.5f);
            var ci = car.GetComponentInChildren<CockpitInterior>(true);
            bool bagD = ci != null && ci.GetComponentsInChildren<Transform>(true).Any(x => x.name == "SRS_Driver");
            bool bagP = ci != null && ci.GetComponentsInChildren<Transform>(true).Any(x => x.name == "SRS_Passenger");
            Check(bagD && bagP, "the cockpit shows the driver's bag and the passenger's", bagD + " / " + bagP);
            bool cracked = ci != null && ci.GetComponentsInChildren<Transform>(true).Any(x => x.name == "GlassCrack");
            Check(cracked, "  and the cracked windscreen");
            if (cam != null) Shot("look_3_cockpit_bags", cam.transform.position, cam.transform.rotation);
            ChaseCamera.SetView(ChaseCamera.View.Chase);

            // F: the drive onto the career
            int folded = CrashStopGap.FoldDamage(car);
            var srs = owned.faults.FirstOrDefault(f => f.id == SrsBag.Id);
            Check(srs != null && srs.hidden && !string.IsNullOrEmpty(srs.cause) && srs.cost > 0 && srs.hitMps > 0f,
                  "srs_deployed banked onto the car: hidden, its cause stamped, priced",
                  srs != null ? srs.cause + " zone " + srs.zone1 + " dv " + srs.hitMps.ToString("0.0") + " $" + srs.cost + " mag " + srs.mag : "none");
            Check(owned.faults.All(f => f.hidden), "every row hidden (no part named before a person finds it)");
            Note("folded " + folded + "; rows " + string.Join(",", owned.faults.Select(f => f.id + " s" + f.stage + " z" + f.zone1 + (f.hitSide < 0 ? "L" : f.hitSide > 0 ? "R" : ""))));
            var after = DamageLook.Read(owned.faults, null);
            Check(after.lampOut == look.Now.lampOut && after.bags == look.Now.bags && after.zones == look.Now.zones,
                  "the next drive shows the same from the banked rows alone",
                  after.lampOut + " " + after.bags + " " + after.zones + " vs " + look.Now.lampOut + " " + look.Now.bags + " " + look.Now.zones);
        }
    }
}
