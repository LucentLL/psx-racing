using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Writes out which body shell every catalog car ends up wearing.
    ///
    /// Sixteen models against 317 cars is a curation problem, not a lookup, and
    /// the only way to judge it is to read the whole list. This dumps it to
    /// Docs/car_model_mapping.txt: shells in order, cars under each, and a mark
    /// showing whether the assignment was hand-made or scored.
    /// </summary>
    public static class CarModelMappingReport
    {
        const string OutPath = "Docs/car_model_mapping.txt";

        [MenuItem("Tools/PSX Racing/Dump Car Model Mapping")]
        public static void Dump()
        {
            var sb = new StringBuilder();
            var cars = CarCatalog.All;
            var byKey = new Dictionary<string, List<CarSpec>>();
            foreach (var c in cars)
            {
                string k = CarModelLibrary.KeyFor(c);
                if (!byKey.TryGetValue(k, out var list)) byKey[k] = list = new List<CarSpec>();
                list.Add(c);
            }

            int hand = cars.Count(c => CarModelLibrary.HandKey(c) != null);
            sb.AppendLine("PSX Racing — catalog car to body shell");
            sb.AppendLine($"{cars.Count} cars across {byKey.Count} of {CarModelLibrary.Models.Length} shells; " +
                          $"{hand} hand-mapped, {cars.Count - hand} scored.");
            sb.AppendLine("  =  hand-mapped (the real car, its twin, or a deliberate call)");
            sb.AppendLine("  ~  scored on body style, region, era and weight");
            sb.AppendLine("  Per car, TO SPEC (CarModelLibrary.Fit, the owner's GT4 Specs sheet): wheelbase sheet/shell");
            sb.AppendLine("  (the stretched shell's axle spacing), track F/R sheet against the stretched model's own,");
            sb.AppendLine("  tyre diameter x section F/R, along/across stretch, camber F/R (top in) and any poke past");
            sb.AppendLine("  the model's own wheel left after it. !CLAMP = a stretch hit its fence; !POKE = still out.");
            sb.AppendLine();

            // The fit, summarised first: every car with camber, every fence,
            // every tyre still standing out (a model edit for the owner).
            var cambered = new List<string>();
            var fenced = new List<string>();
            var poking = new List<string>();
            foreach (var c in cars)
            {
                var d = CarModelLibrary.LoadFor(c);
                if (d == null) continue;
                var f = CarModelLibrary.Fit(d, c);
                string who = $"{c.name} [{d.key}]";
                if (f.camberF > 0.05f || f.camberR > 0.05f)
                    cambered.Add($"{who}: F {f.camberF:0.0} R {f.camberR:0.0} deg");
                if (f.clampX || f.clampZ)
                    fenced.Add($"{who}: along x{f.sz:0.000} (wants x{c.wheelbaseMm / 1000f / Mathf.Max(d.wheelbase, 0.5f):0.000}), across x{f.sx:0.000}");
                if (f.Pokes)
                    poking.Add($"{who}: F {f.pokeF * 1000f:0} mm R {f.pokeR * 1000f:0} mm out after F {f.camberF:0.0} R {f.camberR:0.0} deg");
            }
            sb.AppendLine($"CAMBER ({cambered.Count} cars)");
            foreach (var l in cambered) sb.AppendLine("    " + l);
            sb.AppendLine($"STRETCH FENCED ({fenced.Count} cars)");
            foreach (var l in fenced) sb.AppendLine("    " + l);
            sb.AppendLine($"STILL POKING after the {CarModelLibrary.CamberCapDeg:0} deg cap ({poking.Count} cars) - model edits");
            foreach (var l in poking) sb.AppendLine("    " + l);
            sb.AppendLine();

            foreach (var m in CarModelLibrary.Models)
            {
                byKey.TryGetValue(m.key, out var list);
                int n = list?.Count ?? 0;
                sb.AppendLine($"=== {m.key}  ({m.name}, {m.region} {m.year} {m.body})  — {n} car{(n == 1 ? "" : "s")}");
                var def = CarModelLibrary.Load(m.key);
                if (def != null)
                    sb.AppendLine($"    model: wheelbase {def.wheelbase * 1000f:0} mm, track {def.trackWidth * 1000f:0}/" +
                                  $"{(def.trackRear > 0.1f ? def.trackRear : def.trackWidth) * 1000f:0} mm, " +
                                  $"tyre {def.wheelRadius / Mathf.Max(def.wheelMeshScale, 0.01f) * 2000f:0}/" +
                                  $"{(def.tyreRadiusRear > 0.05f ? def.tyreRadiusRear : def.wheelRadius / Mathf.Max(def.wheelMeshScale, 0.01f)) * 2000f:0} mm, " +
                                  $"lower body {CarModelLibrary.BodyWidth(def) * 1000f:0} mm, {def.SkinCount} liveries");
                if (n == 0)
                {
                    sb.AppendLine("    (no catalog car — used as roadside scenery)");
                    sb.AppendLine();
                    continue;
                }
                foreach (var c in list.OrderBy(c => c.modelYear).ThenBy(c => c.name))
                {
                    sb.AppendLine($"    {(CarModelLibrary.HandKey(c) != null ? "=" : "~")} " +
                                  $"{c.modelYear} {c.origin} {c.drv,-3} {c.kg,4}kg {c.hp,4}hp " +
                                  $"{CarModelLibrary.BodyOf(c),-8} {c.name}");
                    if (def == null) continue;
                    var f = CarModelLibrary.Fit(def, c);
                    float mtr = def.trackRear > 0.1f ? def.trackRear : def.trackWidth;
                    sb.AppendLine($"        wb {c.wheelbaseMm}/{def.wheelbase * f.sz * 1000f:0}  " +
                                  $"track F {c.trackFMm}/{def.trackWidth * f.sx * 1000f:0} R {c.trackRMm}/{mtr * f.sx * 1000f:0}  " +
                                  $"tyre {c.tyreFDiaMm}x{c.tyreFWidthMm} / {c.tyreRDiaMm}x{c.tyreRWidthMm}  " +
                                  $"along x{f.sz:0.000} across x{f.sx:0.000}  " +
                                  $"camber {f.camberF:0.0}/{f.camberR:0.0}  poke {f.pokeF * 1000f:0}/{f.pokeR * 1000f:0} mm" +
                                  $"{(f.clampX || f.clampZ ? "  !CLAMP" : "")}" +
                                  $"{(f.Pokes ? "  !POKE" : "")}");
                }
                sb.AppendLine();
            }

            string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, OutPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, sb.ToString());
            Debug.Log($"[CarModelMappingReport] wrote {OutPath} — {cars.Count} cars, {hand} hand-mapped.");
        }
    }
}
