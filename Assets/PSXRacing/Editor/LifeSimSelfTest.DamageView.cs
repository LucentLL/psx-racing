using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// damageview (owner 2026-11-15: "in the garage view the car is in mint
    /// condition ... the current condition should be visible in all modes"):
    /// a damaged career car's display model carries the damage block, a mint one
    /// does not, a repaired one goes clean, &lt; &gt; to another car shows that
    /// car's own condition, the on-foot shell wears it too, a car for sale stays
    /// mint, and PSX_VIEW_DAMAGE=0 is the old mint view.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        static void RunDamageViewTests()
        {
            bool on = DamageView.On, look = DamageLook.On, body = DamageLook.BodyOn, lamps = DamageLook.LampsOn;
            try
            {
                DamageView.On = DamageLook.On = DamageLook.BodyOn = DamageLook.LampsOn = true;
                Guard(nameof(TestDamageView), TestDamageView);
            }
            finally
            {
                DamageView.On = on; DamageLook.On = look; DamageLook.BodyOn = body; DamageLook.LampsOn = lamps;
            }
        }

        static readonly MaterialPropertyBlock dvBlock = new MaterialPropertyBlock();

        static float DvOn(Renderer r)
        {
            if (r == null || !r.HasPropertyBlock()) return 0f;
            r.GetPropertyBlock(dvBlock);
            return dvBlock.GetFloat(DamageLook.OnId);
        }

        static Vector4 DvVec(Renderer r, int id)
        {
            if (r == null || !r.HasPropertyBlock()) return Vector4.zero;
            r.GetPropertyBlock(dvBlock);
            return dvBlock.GetVector(id);
        }

        /// <summary>A crash's rows, banked the way a drive banks them
        /// (DamageBank.ApplyIncident stamps each row's cause).</summary>
        internal static bool DvCrash(LifeState s, OwnedCar car, int hitId, int zone, int side, float dv,
                                     params (string id, int stage, int zone, int side)[] rows)
        {
            var rec = new DamageBank.IncidentRec
            {
                hitId = hitId, kind = DamageModel.KHit, zone = zone, side = side, corner = -1,
                band = dv >= 8f ? 4 : dv >= 5f ? 3 : 2, dv = dv, what = "a planted crash (damageview)",
            };
            foreach (var r in rows)
                rec.rows.Add(new DamageBank.RowRec { id = r.id, stage = r.stage, zone = r.zone, side = r.side, corner = -1 });
            return DamageBank.ApplyIncident(s, car, rec);
        }

        static void TestDamageView()
        {
            Line("damageview: every view of a career car shows its current condition (owner 11-15):");
            var spec = CarCatalog.Get("honda_nsx__90") ?? CrashSpec("jpn", 1990);
            if (spec == null) { Check(false, "an NSX (or a Japanese road car) to show"); return; }
            var s = LifeRules.SeedNewGame("DMGVIEW", 25, LifeRules.DefaultJobIndex);
            var hurt = DmgCar(s, spec, spec.id + "#dv1");
            var mint = DmgCar(s, spec, spec.id + "#dv2");
            s.cars.Add(hurt);
            s.cars.Add(mint);
            bool banked = DvCrash(s, hurt, 91001, DamageModel.ZFront, -1, 8.6f,
                                  (DamageModel.PanelId, 3, DamageModel.ZFront, -1), (DamageModel.LampId, 1, DamageModel.ZFront, -1))
                        & DvCrash(s, hurt, 91002, DamageModel.ZSide, -1, 4.4f, (DamageModel.PanelId, 2, DamageModel.ZSide, -1))
                        & DvCrash(s, hurt, 91003, DamageModel.ZRoof, 0, 3.2f, (DamageModel.GlassId, 1, DamageModel.ZRoof, 0));
            Check(banked && hurt.faults.Exists(f => f.id == DamageModel.PanelId && f.hitId == 91001 && f.hidden),
                  "the crash rows are on the car, hidden and with their cause", hurt.faults.Count + " rows");

            var go = new GameObject("DamageViewTest") { hideFlags = HideFlags.HideAndDontSave };
            var anchor = new GameObject("DamageViewShellTest") { hideFlags = HideFlags.HideAndDontSave };
            var viewer = go.AddComponent<CarViewer>();
            try
            {
                viewer.ShowOwned(mint, spec);
                var r = viewer.BodyRenderer;
                Check(r != null && DvOn(r) == 0f && DvVec(r, DamageLook.LampOutId) == Vector4.zero,
                      "a mint car's display model carries no damage", DvOn(r));

                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                viewer.ShowOwned(hurt, spec);   // > to the wrecked one (the same shell: the early path)
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                var a = DvVec(r, DamageLook.AId);
                var b = DvVec(r, DamageLook.BId);
                var lo = DvVec(r, DamageLook.LampOutId);
                Check(DvOn(r) == 1f && a.x >= 0.99f && a.z >= 0.6f && a.w == 0f && b.x > 0.3f && b.y < 0f && lo.x == 1f && lo.y == 0f,
                      "> to the damaged car: its model wears the drive's look (front crumpled on the left, the left door dented, cracked glass, the left headlamp out)",
                      $"A {a} B {b} lamps {lo}; re-dress {ms:0.000} ms");

                var look = DamageLook.Read(hurt.faults, null);
                Check(a == look.zones && Mathf.Approximately(b.x, look.roof) && Mathf.Approximately(b.w, DamageLook.Seed(hurt.id)),
                      "the same look and seed the drive reads from the same rows (DamageLook.Read / Seed)");

                viewer.ShowOwned(mint, spec);   // < back
                Check(DvOn(r) == 0f && DvVec(r, DamageLook.LampOutId) == Vector4.zero, "< to the mint car: its own condition, clean");

                viewer.ShowOwned(hurt, spec);
                var watch = r.transform.parent.GetComponent<DamageViewWatch>();
                Check(watch != null && watch.Car == hurt, "the shown car is watched for a change of its rows");
                Check(watch != null && !watch.CheckNow(), "nothing changed: the watch does nothing");
                int fixedRows = DebugCarOps.ClearFaults(hurt);   // the bench's CLEAR ALL FAULTS: a full repair
                bool redressed = watch != null && watch.CheckNow();
                Check(redressed && DvOn(r) == 0f && DvVec(r, DamageLook.LampOutId) == Vector4.zero,
                      "repaired: the open view goes clean at once", fixedRows + " rows fixed");

                DvCrash(s, hurt, 91004, DamageModel.ZRear, 1, 6f, (DamageModel.PanelId, 2, DamageModel.ZRear, 1),
                        (DamageModel.LampId, 1, DamageModel.ZRear, 1));
                Check(watch != null && watch.CheckNow() && DvOn(r) == 1f && DvVec(r, DamageLook.AId).y > 0.6f && DvVec(r, DamageLook.LampOutId).w == 1f,
                      "a new incident shows (a dented tail, the right tail lamp out)");

                viewer.Show(spec);   // a car for sale (no rows held): mint, unwatched
                Check(DvOn(r) == 0f && DvVec(r, DamageLook.LampOutId) == Vector4.zero && (watch == null || watch.Car == null),
                      "a car for sale with no rows: mint, the watch stopped");

                // the shell on foot (the garage bay, the pizza shop)
                var def = CarModelLibrary.LoadFor(spec);
                var sh = CarShell.Spawn(anchor.transform, def, 0, out _, solid: false, spec: spec, owned: hurt);
                var sb = sh != null ? sh.Find("Body") : null;
                var sr = sb != null ? sb.GetComponent<Renderer>() : null;
                Check(sr != null && DvOn(sr) == 1f && DvVec(sr, DamageLook.AId).y > 0.6f, "the on-foot shell of the car wears it too");
                var w0 = sh != null ? sh.Find("Wheel0") : null;
                Check(w0 == null || DvOn(w0.GetComponent<Renderer>()) == 0f, "never on a wheel");
                var sh2 = CarShell.Spawn(anchor.transform, def, 0, out _, solid: false, spec: spec);
                var sr2 = sh2 != null && sh2.Find("Body") != null ? sh2.Find("Body").GetComponent<Renderer>() : null;
                Check(sr2 != null && DvOn(sr2) == 0f, "a shell with no owner (a lot, a meet, traffic): untouched");

                DamageView.On = false;   // PSX_VIEW_DAMAGE=0
                var go2 = new GameObject("DamageViewTestOff") { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    var v2 = go2.AddComponent<CarViewer>();
                    v2.ShowOwned(hurt, spec);
                    Check(DvOn(v2.BodyRenderer) == 0f, "PSX_VIEW_DAMAGE=0: the old mint view");
                }
                finally { Object.DestroyImmediate(go2); DamageView.On = true; }
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(anchor);
            }
        }
    }
}
