using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>Where the restaurants actually stand, relative to where the
    /// player is put down. Writes PSXRacing_food.txt.</summary>
    public static class FoodProbe
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            var map = CityMap.Get();
            if (map == null) { sb.AppendLine("no city data"); goto done; }

            map.NearestRoadPoint(map.uptown, 400f, false, out int se, out float ss, out _);
            var spawn = map.edges[se].PointAt(ss);
            sb.AppendLine("spawn: " + map.edges[se].name + " at (" + spawn.x.ToString("0") +
                          ", " + spawn.y.ToString("0") + ")");

            var buildings = CityBuildings.Precompute(map);
            int n = 0;
            foreach (var kv in buildings)
                foreach (var b in kv.Value)
                {
                    if (b.kind != CityProps.Burger && b.kind != CityProps.Pizzeria) continue;
                    n++;
                    float dist = Vector2.Distance(b.pos, spawn);
                    Vector2 d = b.pos - spawn;
                    // screen bearing: +y is north on this map
                    float bear = Mathf.Repeat(Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, 360f);
                    string street = "";
                    if (map.NearestRoadPoint(b.pos, 120f, false, out int ei, out _, out _))
                        street = map.edges[ei].name;
                    sb.AppendLine(string.Format(
                        "{0,-10} at ({1,7:0}, {2,7:0})  {3,6:0} m from spawn, bearing {4,3:0}  on {5}",
                        b.kind == CityProps.Burger ? "BURGER" : "PIZZERIA",
                        b.pos.x, b.pos.y, dist, bear, street));
                }
            sb.AppendLine("total restaurants: " + n);
        done:
            System.IO.File.WriteAllText("PSXRacing_food.txt", sb.ToString());
            Debug.Log("FOOD PROBE\n" + sb);
        }
    }
}
