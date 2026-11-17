using System.Linq;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Damage round 2 (owner on PC48, 2026-11-16: "the broken glass effect is
    /// very poor and does not communicate the damage to me. I'd also like to
    /// see scuffing for wheels (hitting curbs, etc)"): which pane breaks, how
    /// and where, from the rows (DamageGlass); a wheel's curb rash from a
    /// strike to the save, the views, the value and the shop (WheelScuff).
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        static void RunDamage2Tests()
        {
            bool view = DamageView.On, look = DamageLook.On, body = DamageLook.BodyOn, lamps = DamageLook.LampsOn,
                 glass = DamageGlass.On, wheels = WheelScuff.On;
            try
            {
                DamageView.On = DamageLook.On = DamageLook.BodyOn = DamageLook.LampsOn = DamageGlass.On = WheelScuff.On = true;
                Guard(nameof(TestGlassPanes), TestGlassPanes);
                Guard(nameof(TestWheelRash), TestWheelRash);
            }
            finally
            {
                DamageView.On = view; DamageLook.On = look; DamageLook.BodyOn = body; DamageLook.LampsOn = lamps;
                DamageGlass.On = glass; WheelScuff.On = wheels;
            }
        }

        static readonly int D2GlsOn = Shader.PropertyToID("_PSXGlsOn"), D2GlsS = Shader.PropertyToID("_PSXGlsS"),
            D2GlsW = Shader.PropertyToID("_PSXGlsW"), D2Rash = Shader.PropertyToID("_PSXWheelRash");

        static DamageLook.Look D2Look(LifeState s, CarSpec spec, string id, System.Action<OwnedCar> wreck)
        {
            var car = DmgCar(s, spec, id);
            wreck(car);
            return DamageLook.Read(car.faults, null);
        }

        static void TestGlassPanes()
        {
            Line("damage round 2 - glass: which pane breaks, how and where, from the rows (owner 11-16):");
            var spec = CarCatalog.Get("honda_civic_sir_ii__eg___91") ?? CrashSpec("jpn", 1990);
            if (spec == null) { Check(false, "an EG Civic (or a Japanese road car) to break"); return; }
            var s = LifeRules.SeedNewGame("DMG2G", 25, LifeRules.DefaultJobIndex);
            const int F = DamageModel.ZFront, R = DamageModel.ZRear, S = DamageModel.ZSide, T = DamageModel.ZRoof;
            string P = DamageModel.PanelId, B = DamageModel.BarId, G = DamageModel.GlassId;

            var k = D2Look(s, spec, "g#1", c => DvCrash(s, c, 5101, F, -1, 6.5f, (P, 3, F, -1)));
            Check(k.panes.x == DamageGlass.Crack && k.panes.y == 0f && k.panes.z == 0f && k.panes.w == 0f && k.hitW.x < 0f && k.hitW.y < 0.3f && k.hitW.w > 0f,
                  "a front hit that crumpled the nose on the left: the windscreen cracked, low on the left, the crack running right", k.panes + " at " + k.hitW);
            k = D2Look(s, spec, "g#2", c => DvCrash(s, c, 5102, F, -1, 9f, (P, 3, F, -1), (B, 2, F, 0)));
            Check(k.panes.x == DamageGlass.Web && k.hitW.y < 0.3f, "  with the bar pushed back: a spider web there", k.panes + " at " + k.hitW);
            k = D2Look(s, spec, "g#3", c => DvCrash(s, c, 5103, F, 1, 4f, (P, 2, F, 1)));
            Check(k.panes == Vector4.zero, "a dented nose (stage 2): the glass whole");
            k = D2Look(s, spec, "g#4", c => DvCrash(s, c, 5104, S, -1, 4.4f, (P, 2, S, -1)));
            Check(k.panes.z == DamageGlass.Crazed && k.panes.w == 0f && k.panes.x == 0f, "a dented left door: the left glass crazed, the right whole", k.panes);
            k = D2Look(s, spec, "g#5", c => DvCrash(s, c, 5105, S, 1, 7f, (P, 3, S, 1)));
            Check(k.panes.w == DamageGlass.Shattered && k.panes.z == 0f, "a crumpled right door: the right glass shattered", k.panes);
            k = D2Look(s, spec, "g#6", c => DvCrash(s, c, 5106, R, 0, 7f, (P, 3, R, 0)));
            var k2 = D2Look(s, spec, "g#7", c => DvCrash(s, c, 5107, R, 0, 9f, (P, 3, R, 0), (B, 2, R, 0)));
            Check(k.panes.y == DamageGlass.Crazed && k2.panes.y == DamageGlass.Shattered, "a crumpled tail: the rear glass crazed; with its bar pushed back, shattered",
                  k.panes.y + " / " + k2.panes.y);
            k = D2Look(s, spec, "g#8", c => DvCrash(s, c, 5108, T, 0, 3.5f, (G, 1, T, 0)));
            k2 = D2Look(s, spec, "g#9", c => DvCrash(s, c, 5109, T, 0, 6f, (G, 1, T, 0)));
            Check(k.panes.x == DamageGlass.Chip && k2.panes.x == DamageGlass.Crack && k.panes.z == 0f && k2.panes.w == 0f,
                  "a brush of the glass (roof_glass stage 1): a stone chip under 4.5 m/s, a crack over it; the side glass whole", k.panes.x + " / " + k2.panes.x);
            k = D2Look(s, spec, "g#10", c => DvCrash(s, c, 5110, T, 0, 7f, (G, 2, T, 0)));
            k2 = D2Look(s, spec, "g#11", c => DvCrash(s, c, 5111, T, 0, 12f, (G, 3, T, 0)));
            Check(k.panes == new Vector4(DamageGlass.Web, DamageGlass.Crazed, DamageGlass.Crazed, DamageGlass.Crazed) &&
                  k2.panes == new Vector4(DamageGlass.Web, DamageGlass.Shattered, DamageGlass.Shattered, DamageGlass.Shattered),
                  "a dented roof: a web and every tempered pane crazed; a crushed one: every tempered pane shattered", k.panes + " / " + k2.panes);

            // placed by the incident, the same every time
            var again = D2Look(s, spec, "g#12", c => DvCrash(s, c, 5110, T, 0, 7f, (G, 2, T, 0)));
            var other = D2Look(s, spec, "g#13", c => DvCrash(s, c, 5999, T, 0, 7f, (G, 2, T, 0)));
            Check(again.hitW == k.hitW && again.hitL == k.hitL && other.hitW != k.hitW,
                  "the strike is the incident's: the same hitId the same place, another incident another", k.hitW + " / " + other.hitW);

            // the view carries it; a mint car, a repair and the switch do not
            var hurt = DmgCar(s, spec, "g#view");
            DvCrash(s, hurt, 5120, T, 0, 7f, (G, 2, T, 0));
            var mint = DmgCar(s, spec, "g#mint");
            var go = new GameObject("Damage2ViewTest") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var viewer = go.AddComponent<CarViewer>();
                viewer.ShowOwned(hurt, spec);
                var r = viewer.BodyRenderer;
                var b = new MaterialPropertyBlock();
                r.GetPropertyBlock(b);
                var look = DamageLook.Read(hurt.faults, null);
                Check(b.GetFloat(D2GlsOn) == 1f && b.GetVector(D2GlsS) == look.panes && b.GetVector(D2GlsW).z == look.hitW.z,
                      "the turntable's model carries the panes and the strike (the drive's own look)", b.GetVector(D2GlsS) + " seed " + b.GetVector(D2GlsW).z);
                viewer.ShowOwned(mint, spec);
                r.GetPropertyBlock(b);
                Check(b.GetFloat(D2GlsOn) == 0f, "> to a mint car: no broken glass");
                viewer.ShowOwned(hurt, spec);
                DebugCarOps.ClearFaults(hurt);
                var watch = r.transform.parent.GetComponent<DamageViewWatch>();
                bool redressed = watch != null && watch.CheckNow();
                r.GetPropertyBlock(b);
                Check(redressed && b.GetFloat(D2GlsOn) == 0f, "repaired: the open view's glass is whole at once");
                DvCrash(s, hurt, 5121, T, 0, 7f, (G, 2, T, 0));
                DamageGlass.On = false;   // PSX_DMG_GLASS2=0
                watch.CheckNow();
                viewer.Dress(hurt);
                r.GetPropertyBlock(b);
                Check(b.GetFloat(D2GlsOn) == 0f && b.GetFloat(DamageLook.OnId) == 1f && DamageLook.Read(hurt.faults, null).panes == Vector4.zero,
                      "PSX_DMG_GLASS2=0: the first cut's look (the roof row, no panes)");
                DamageGlass.On = true;
            }
            finally { DamageGlass.On = true; Object.DestroyImmediate(go); }

            // the cockpit's break never hides the road
            foreach (int lvl in new[] { 1, 2, 3 })
            {
                var tex = DamageGlass.CockpitTexture(lvl, 0.37f, 1f);
                var px = tex.GetPixels32();
                int solid = px.Count(c => c.a > 160);
                float share = solid / (float)px.Length;
                Check(solid > 20 && share < 0.06f, "the cockpit's break, level " + lvl + ": thin lines, under 6% of its square drawn",
                      (share * 100f).ToString("0.0") + "% of a " + DamageGlass.QuadMetres(lvl).ToString("0.00") + " m square");
                Object.DestroyImmediate(tex);
            }
            Check(DamageGlass.Level(DamageGlass.Chip) == 1 && DamageGlass.Level(DamageGlass.Crack) == 2 && DamageGlass.Level(DamageGlass.Web) == 3 && DamageGlass.Level(0f) == 0,
                  "the windscreen's levels: chip 1, crack 2, web 3");
        }

        static void TestWheelRash()
        {
            Line("damage round 2 - wheels: curb rash from a strike to the save, the views, the value and the shop (owner 11-16):");
            // the rules
            Check(WheelScuff.StageFor(0.05f, 0.1f, false) == 0 && WheelScuff.StageFor(0.3f, 0f, false) == 1 && WheelScuff.StageFor(1.2f, 0f, false) == 2 &&
                  WheelScuff.StageFor(2.5f, 0f, false) == 3 && WheelScuff.StageFor(0f, 3f, false) == 2 && WheelScuff.StageFor(1f, 7f, false) == 3,
                  "a kiss is nothing; pressed in at 0.3 / 1.2 / 2.5 m/s: stage 1 / 2 / 3; a 3 m rub: 2; a hard strike rubbed 7 m: 3");
            Check(WheelScuff.StageFor(0.5f, 0f, true) == 0 && WheelScuff.StageFor(1.5f, 0f, true) == 1 && WheelScuff.StageFor(4f, 0f, true) == 2 && WheelScuff.StageFor(7f, 0f, true) == 3,
                  "through the body's side (a wall at the wheel) it takes more: 1.5 / 4 / 7 m/s");
            Check(WheelScuff.IsStrike(0.15f, 0.03f, 0.9f) && !WheelScuff.IsStrike(0.10f, 0.03f, 0.9f) && !WheelScuff.IsStrike(0.06f, 0.03f, 0.9f),
                  "a 15 cm curb with its face in the tyre is a strike; a 10 cm rolled curb and a 6 cm racing kerb are kerbs the tread rides");
            Check(!WheelScuff.IsStrike(0.15f, 0.03f, 0.3f) && !WheelScuff.IsStrike(0.15f, 0f, 0.9f) && !WheelScuff.IsStrike(0.6f, 0.03f, 0.9f),
                  "a curb met nose-on (its face 70 degrees off the axle), one not yet reached, and a wall: not a wheel strike");
            Check(Mathf.Approximately(WheelScuff.Press(2f, 0.5f), 1f) && WheelScuff.Press(-1f, 1f) == 0f, "the press on the wheel's side: the closing speed by how square the face stands to the axle");

            var spec = CarCatalog.Get("honda_civic_sir_ii__eg___91") ?? CrashSpec("jpn", 1990);
            if (spec == null) { Check(false, "an EG Civic (or a Japanese road car) to curb"); return; }
            var s = LifeRules.SeedNewGame("DMG2W", 25, LifeRules.DefaultJobIndex);
            var owned = DmgCar(s, spec, spec.id + "#d2w");
            s.activeCar = owned.id;
            var car = DmgBench(spec, out _, out var resp);
            var go = new GameObject("Damage2WheelTest") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                DmgUseState(s);
                foreach (var p in DamageBank.Files()) System.IO.File.Delete(p);
                DmgCareerDrive(owned, spec);
                CrashStopGap.Arm(car);
                var L = resp.Damage;
                if (L == null) { Check(false, "a career ledger"); return; }
                int value0 = CarMarket.CarValue(owned);
                float paint0 = owned.paint;
                var inc = L.Wheel(1, 2, 1.2f, "curb, West Trade Street", 10f);
                Check(inc != null && inc.kind == DamageModel.KWheel && inc.outcome.rows.Count == 1 && inc.outcome.rows[0].id == "wheel_rash_fr" &&
                      inc.outcome.rows[0].stage == 2 && inc.fail == 0 && inc.injury == 0 && inc.outcome.alignSeverity == 0,
                      "a hard curb strike on the front right: one incident, one row (wheel_rash_fr stage 2), no FAIL, no injury, no alignment");
                var live = DamageLook.Read(owned.faults, L);
                Check(Mathf.Approximately(live.rash.y, 2f / 3f) && live.rash.x == 0f && live.rash.z == 0f, "the drive shows it on that wheel at once", live.rash);
                L.Wheel(1, 1, 0.3f, "curb, West Trade Street", 10.2f);
                Check(L.incidents.Count == 1 && L.incidents[0].band == 2, "a lighter touch of the same strike: the same incident, not lowered");
                L.Flush(11f);
                int folded = DamageLedger.FoldNow(L);
                var row = owned.faults.FirstOrDefault(f => f.id == "wheel_rash_fr");
                Check(folded == 1 && row != null && row.hidden && row.cause == FaultCause.Impact && row.causeNote == "front-right rim: curb, West Trade Street" &&
                      row.corner1 == 2 && row.stage == 2f && row.cost > 0 && row.hitId == inc.hitId,
                      "banked onto the car: hidden, its cause the strike and the place, priced",
                      row != null ? "'" + row.label + "' '" + row.causeNote + "' $" + row.cost + " stage " + row.stage : "no row");
                int value1 = CarMarket.CarValue(owned);
                Check(value1 < value0 && owned.paint < paint0, "it counts: the value and the paint lane fall",
                      "$" + value0 + " -> $" + value1 + ", paint " + paint0.ToString("0.0") + " -> " + owned.paint.ToString("0.0"));

                // the next drive: a second wheel, and the first one worse
                foreach (var f in DamageBank.Files()) System.IO.File.Delete(f);
                CrashStopGap.Arm(car);
                L = resp.Damage;
                int cost2 = row.cost;
                L.Wheel(1, 3, 2.6f, "curb, South Tryon Street", 20f);
                L.Wheel(2, 1, 0.3f, "rock, Gillespie Gap", 30f);
                L.Flush(31f);
                DamageLedger.FoldNow(L);
                var rows = owned.faults.Where(f => WheelScuff.Is(f.id)).ToList();
                row = rows.FirstOrDefault(f => f.id == "wheel_rash_fr");
                var rear = rows.FirstOrDefault(f => f.id == "wheel_rash_rl");
                Check(rows.Count == 2 && row != null && row.stage == 3f && row.cost > cost2 && row.label.Contains("Gouged") && rear != null && rear.stage == 1f && rear.cost < cost2,
                      "a worse strike raises that wheel's row (never doubled) and its price; another wheel gets its own",
                      string.Join(" | ", rows.Select(f => f.id + " s" + f.stage + " $" + f.cost + " '" + f.causeNote + "'")));

                // it persists: through the save's own JSON
                var back = JsonUtility.FromJson<LifeState>(JsonUtility.ToJson(s));
                var bcar = back.FindCar(owned.id);
                var brow = bcar != null ? bcar.faults.FirstOrDefault(f => f.id == "wheel_rash_fr") : null;
                Check(brow != null && brow.stage == 3f && brow.corner1 == 2 && brow.causeNote == row.causeNote && brow.hitId == row.hitId,
                      "it persists in the save (the rows' own fields: no new save version)");

                // every view shows it; a mint car and the switch do not
                var viewer = go.AddComponent<CarViewer>();
                viewer.ShowOwned(owned, spec);
                var b = new MaterialPropertyBlock();
                Vector4 Rash(Renderer r) { if (r == null || !r.HasPropertyBlock()) return Vector4.zero; r.GetPropertyBlock(b); return b.GetVector(D2Rash); }
                Check(Mathf.Approximately(Rash(viewer.WheelRenderer(1)).x, 1f) && Mathf.Approximately(Rash(viewer.WheelRenderer(2)).x, 1f / 3f) &&
                      Rash(viewer.WheelRenderer(0)).x == 0f && Rash(viewer.WheelRenderer(3)).x == 0f && Rash(viewer.WheelRenderer(1)).z > 0.2f,
                      "the turntable's wheels carry it: the front right gouged, the rear left scuffed, the others clean",
                      Rash(viewer.WheelRenderer(0)).x + " " + Rash(viewer.WheelRenderer(1)).x + " " + Rash(viewer.WheelRenderer(2)).x + " " + Rash(viewer.WheelRenderer(3)).x);
                D2Block(viewer.BodyRenderer, b);
                Check(b.GetFloat(DamageLook.OnId) == 0f, "  and never the body's damage block (a rash is the wheel's)");
                var def = CarModelLibrary.LoadFor(spec);
                var sh = CarShell.Spawn(go.transform, def, 0, out _, solid: false, spec: spec, owned: owned);
                var w1 = sh != null ? sh.Find("Wheel1") : null;
                Check(w1 != null && Mathf.Approximately(Rash(w1.GetComponent<Renderer>()).x, 1f), "the on-foot shell's wheel carries it too");

                // the shop: a quote for that wheel; done, that wheel alone is clean
                row.hidden = false;
                s.money += 5000;
                var q = FaultCatalog.GetQuote(s, owned, row, FaultCatalog.Venue.Mechanic);
                Check(q.available && q.price > 0, "the shop quotes that wheel", "$" + q.price + ", " + q.days + " d");
                owned.faults.RemoveAll(x => x.id == row.id);   // what the finished job does (LifeRules: by the row's id)
                var watch = viewer.BodyRenderer.transform.parent.GetComponent<DamageViewWatch>();
                bool redressed = watch != null && watch.CheckNow();
                Check(redressed && Rash(viewer.WheelRenderer(1)).x == 0f && Mathf.Approximately(Rash(viewer.WheelRenderer(2)).x, 1f / 3f),
                      "the job done: that wheel is clean in the open view at once, the other keeps its own");
                WheelScuff.On = false;   // PSX_DMG_WHEELS=0
                viewer.Dress(owned);
                Check(Rash(viewer.WheelRenderer(2)).x == 0f && DamageLook.Read(owned.faults, null).rash == Vector4.zero, "PSX_DMG_WHEELS=0: no rash shown");
                WheelScuff.On = true;
                Check(FaultClass.Of("wheel_rash_fl") != FaultClassKind.Other && FaultCatalog.MakeById("wheel_rash_rr", "jpn") != null &&
                      FaultCatalog.MakeById("wheel_rash_rr", "eur") != null, "every wheel's row has a class and a catalog entry in every family");
            }
            finally
            {
                WheelScuff.On = true;
                foreach (var f in DamageBank.Files()) System.IO.File.Delete(f);
                DmgUseState(null);
                RaceHandoff.ClearAll();
                TuneDrop(car);
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>A wheel's curb strike, banked the way a drive banks it
        /// (DamageBank.ApplyIncident: the row of DamageModel.Resolve's KWheel).</summary>
        internal static bool D2Wheel(LifeState s, OwnedCar car, int hitId, int corner, int stage, string what)
        {
            var rec = new DamageBank.IncidentRec
            {
                hitId = hitId, kind = DamageModel.KWheel, zone = DamageModel.ZCorner, side = corner % 2 == 0 ? -1 : 1, corner = corner,
                band = stage, dv = stage >= 3 ? 2.6f : stage >= 2 ? 1.2f : 0.3f, what = what, paint = WheelScuff.PaintLoss[Mathf.Clamp(stage, 0, 3)],
            };
            rec.rows.Add(new DamageBank.RowRec
            {
                id = WheelScuff.IdFor(corner), stage = stage, zone = DamageModel.ZCorner, side = rec.side, corner = corner,
                mag = WheelScuff.ValuePct[Mathf.Clamp(stage, 0, 3)],
            });
            return DamageBank.ApplyIncident(s, car, rec);
        }

        static void D2Block(Renderer rend, MaterialPropertyBlock b)
        {
            b.Clear();
            if (rend != null && rend.HasPropertyBlock()) rend.GetPropertyBlock(b);
        }
    }
}
