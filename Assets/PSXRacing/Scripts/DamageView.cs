using System.Collections.Generic;
using UnityEngine;
using PSXRacing.LifeSim;

namespace PSXRacing
{
    /// <summary>
    /// THE CAR AS IT IS, IN EVERY VIEW (owner 2026-11-15: "I wrecked my NSX
    /// during the race. The damage was shown during the race and replay, but in
    /// the garage view the car is in mint condition, visibly. ... the current
    /// condition should be visible in all modes").
    ///
    /// ONE call dresses any display model with a car's current condition, by
    /// the drive's own path: <see cref="DamageLook.Read"/> on the car's rows
    /// (hidden or found: a dent shows whether or not anyone has written it
    /// down) plus a drive under way on that car (its ledger, as the drive reads
    /// it), written per renderer on PSX/CarPaint by
    /// <see cref="DamageLook.WriteBlock"/> - the same panels, glass and lamp
    /// lenses, the same seed - and the paint's shine (<see cref="PaintGloss.Of"/>:
    /// the paint, its paint faults, the road dirt; what the drive's CarLights
    /// writes on the career car). A view has no running engine (no steam) and
    /// draws no cockpit (no SRS bag; the drive shows it in the cockpit).
    ///
    /// Callers: CarViewer (every menu turntable and the showroom stage: the car
    /// panels, MY CARS, the car menu, the inspection, the paint booth, the
    /// stored wheels; a car for sale is shown mint, or with the rows of the
    /// visit's car where a viewing holds one) and CarShell (the garage bays and
    /// the pizza shop's kerb on foot; the seller's drive with the visit's car).
    /// It applies when the model is made and again when the rows change while
    /// it shows (<see cref="DamageViewWatch"/>: the rows' signature every half
    /// second by InvokeRepeating - no per-frame work). A mint car, or none,
    /// clears what an earlier dress left on a reused model.
    ///
    /// PSX_VIEW_DAMAGE=0: none of it - every view as before (mint, and the
    /// turntable at full shine). PSX_FAULT_LOOK / _BODY / _LAMPS switch the
    /// views with the drive.
    /// </summary>
    public static class DamageView
    {
        public static bool On = System.Environment.GetEnvironmentVariable("PSX_VIEW_DAMAGE") != "0";

        static readonly List<Renderer> rends = new List<Renderer>();
        static MaterialPropertyBlock block;

        /// <summary>Dresses written since load (the self-test and the cost note).</summary>
        public static int Dresses;

        /// <summary>
        /// Dress one display model. <paramref name="frame"/> is the car's own
        /// frame (nose +Z, up +Y, at rest); <paramref name="boxCentre"/> and
        /// <paramref name="boxSize"/> are the body box in it, as CarBody sizes
        /// its BoxCollider (ShellFit.P(colliderCenter) + the model's slide;
        /// colliderSize x (sx, 1, sz)). <paramref name="body"/>: the body's
        /// renderer - it and every PSX/CarPaint renderer under it is dressed
        /// (never the wheels, stands or brakes beside it). <paramref name="car"/>
        /// null = mint. True when the model now shows damage.
        /// </summary>
        public static bool Dress(Transform frame, Renderer body, Vector3 boxCentre, Vector3 boxSize, OwnedCar car)
        {
            if (!On || frame == null || body == null) return false;
            PaintGloss.Collect(body.transform, rends);
            var look = LookOf(car);
            bool any = look.Any;
            float gloss = car != null ? PaintGloss.Of(car) : 1f;
            Vector3 cW = frame.TransformPoint(boxCentre);
            Vector3 ls = frame.lossyScale;
            Vector3 half = Vector3.Max(Vector3.Scale(boxSize * 0.5f, new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z))),
                                       Vector3.one * 0.1f);
            float seed = DamageLook.Seed(car != null ? car.id : "");
            if (block == null) block = new MaterialPropertyBlock();
            foreach (var r in rends)
            {
                if (r == null) continue;
                // a mint car on a model that never wore a block: leave it untouched
                if (!any && gloss >= 1f && !r.HasPropertyBlock()) continue;
                var t = r.transform;
                DamageLook.WriteBlock(r, look, any, t.localToWorldMatrix, t.rotation, cW, half,
                                      frame.forward, frame.right, frame.up, seed);
                r.GetPropertyBlock(block);
                block.SetFloat(PaintGloss.GlossId, gloss);
                r.SetPropertyBlock(block);
            }
            Dresses++;
            Watch(frame, body, boxCentre, boxSize, car);
            return any;
        }

        /// <summary>The same, off a model's def and fit (CarShell, CarViewer).</summary>
        public static bool Dress(Transform frame, Renderer body, CarModelDef def, CarModelLibrary.ShellFit fit,
                                 Vector3 slide, OwnedCar car)
        {
            if (!On || def == null) return false;
            return Dress(frame, body, fit.P(def.colliderCenter) + slide,
                         new Vector3(def.colliderSize.x * fit.sx, def.colliderSize.y, def.colliderSize.z * fit.sz), car);
        }

        /// <summary>
        /// What a car looks like out of the drive: its rows, plus this drive's
        /// incidents when a drive of that very car is under way (the drive's
        /// own ledger, alive). The drive's switches; no bag, no steam.
        /// </summary>
        public static DamageLook.Look LookOf(OwnedCar car)
        {
            var k = new DamageLook.Look();
            if (car == null || !DamageLook.On) return k;
            k = DamageLook.Read(car.faults, LiveLedger(car));
            if (!DamageLook.LampsOn) k.lampOut = 0;
            if (!DamageLook.BodyOn) { k.zones = Vector4.zero; k.roof = 0f; }
            k.bags = 0;
            k.radStage = 0;
            return k;
        }

        static DamageLedger LiveLedger(OwnedCar car)
        {
            var cur = DamageLedger.Current;
            if (cur == null || !cur.Banks || string.IsNullOrEmpty(car.id) || cur.CarId != car.id || cur.Car == null) return null;
            var cr = cur.Car.GetComponent<CollisionResponder>();
            return cr != null && cr.Damage == cur ? cur : null;
        }

        /// <summary>
        /// The rows and the shine as one number: what the watch compares. A
        /// repair (a row gone or lowered), a new row, a wash or a respray
        /// changes it.
        /// </summary>
        public static int Signature(OwnedCar car)
        {
            if (car == null) return 0;
            unchecked
            {
                int h = 17;
                if (car.faults != null)
                    foreach (var f in car.faults)
                    {
                        if (f == null) continue;
                        h = h * 31 + (f.id ?? "").GetHashCode();
                        h = h * 31 + Mathf.RoundToInt(f.stage * 8f) + (f.zone1 << 8) + ((f.hitSide + 2) << 12) + (f.corner1 << 16);
                    }
                h = h * 31 + Mathf.RoundToInt(PaintGloss.Of(car) * 1000f);
                return h;
            }
        }

        static void Watch(Transform frame, Renderer body, Vector3 centre, Vector3 size, OwnedCar car)
        {
            var w = frame.GetComponent<DamageViewWatch>();
            if (car == null) { if (w != null) w.Stop(); return; }
            if (w == null) w = frame.gameObject.AddComponent<DamageViewWatch>();
            w.Set(body, centre, size, car);
        }
    }
}
