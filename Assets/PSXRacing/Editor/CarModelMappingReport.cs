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
            sb.AppendLine();

            foreach (var m in CarModelLibrary.Models)
            {
                byKey.TryGetValue(m.key, out var list);
                int n = list?.Count ?? 0;
                sb.AppendLine($"=== {m.key}  ({m.name}, {m.region} {m.year} {m.body})  — {n} car{(n == 1 ? "" : "s")}");
                var def = CarModelLibrary.Load(m.key);
                if (def != null)
                    sb.AppendLine($"    wheelbase {def.wheelbase:0.00} m, track {def.trackWidth:0.00} m, " +
                                  $"tyre {def.wheelRadius:0.000} m, {def.SkinCount} liveries");
                if (n == 0)
                {
                    sb.AppendLine("    (no catalog car — used as roadside scenery)");
                    sb.AppendLine();
                    continue;
                }
                foreach (var c in list.OrderBy(c => c.modelYear).ThenBy(c => c.name))
                    sb.AppendLine($"    {(CarModelLibrary.HandKey(c) != null ? "=" : "~")} " +
                                  $"{c.modelYear} {c.origin} {c.drv,-3} {c.kg,4}kg {c.hp,4}hp " +
                                  $"{CarModelLibrary.BodyOf(c),-8} {c.name}");
                sb.AppendLine();
            }

            string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, OutPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, sb.ToString());
            Debug.Log($"[CarModelMappingReport] wrote {OutPath} — {cars.Count} cars, {hand} hand-mapped.");
        }
    }
}
