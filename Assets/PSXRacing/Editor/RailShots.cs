using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// WHAT A STAGE'S BARRIERS LOOK LIKE, from the seat and from over the
    /// edge. Owner, 2026-09-26: "I would like to see more guardrails instead of
    /// always using stone barriers." Four guard runs spread down the stage:
    /// from the driver's eye on the far lane looking up the road at the run,
    /// and from beside the road past the barrier looking back at it.
    ///
    /// Venue: PSX_RAIL_VENUE (a built scene - stage-lab builds held-back ones).
    /// Writes Screenshots/psx_rail_*.png.
    /// </summary>
    public static class RailShots
    {
        public static void Capture()
        {
            string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "psx_rail_*.png")) File.Delete(f);

            string id = System.Environment.GetEnvironmentVariable("PSX_RAIL_VENUE");
            if (string.IsNullOrEmpty(id)) id = "SwissNC226A";
            TrackCatalog.TrackDef def = null;
            foreach (var d in TrackCatalog.Scened) if (d.id == id) def = d;
            foreach (var d in TrackCatalog.HeldBack) if (d.id == id) def = d;
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out _)) return;

            var path = Object.FindFirstObjectByType<TrackPath>();
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            int noon = 0;
            for (int h = 0; h < TimeOfDay.Count; h++)
                if (TimeOfDay.At(h).name.ToLower() == "noon") noon = h;
            if (sun != null) TimeOfDay.Apply(noon, sun);
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);

            // The drawn runs, longest first (a long run is a long view of it).
            var walls = GameObject.Find("Walls");
            if (path == null || walls == null) { Debug.LogError("[RailShots] no path or no Walls"); return; }
            var runs = new List<Renderer>();
            foreach (var r in walls.GetComponentsInChildren<Renderer>())
                if (r.name.StartsWith("Wall")) runs.Add(r);
            runs.Sort((a, b) => b.bounds.size.sqrMagnitude.CompareTo(a.bounds.size.sqrMagnitude));

            int shot = 0;
            float half = path.roadWidth * 0.5f;
            for (int q = 0; q < runs.Count && shot < 4; q++)
            {
                // The run's middle vertex, and the station beside it.
                var mf = runs[q].GetComponent<MeshFilter>();
                var v = mf.sharedMesh.vertices;
                Vector3 mid = runs[q].transform.TransformPoint(v[v.Length / 2]);
                int i = path.NearestIndex(mid);
                if (i < 12 || i > path.Count - 12) continue;
                Vector3 right = Vector3.Cross(Vector3.up, path.GetTangent(i)).normalized;
                float side = Vector3.Dot(mid - path.GetPoint(i), right) >= 0f ? 1f : -1f;

                // From the far lane, eight stations back, eye height.
                Vector3 back = path.GetPoint(i - 8);
                Vector3 rb = Vector3.Cross(Vector3.up, path.GetTangent(i - 8)).normalized;
                Vector3 eye = back - rb * (side * half * 0.5f) + Vector3.up * 1.3f;
                Vector3 look = path.GetPoint(i) + right * (side * (half + 0.6f)) + Vector3.up * 0.5f;
                PSXScreenshotTool.Shot(cam, "rail_" + id + "_" + shot + "_seat", eye, Quaternion.LookRotation(look - eye));

                // Close, at the rail: standing on the shoulder three stations
                // on, looking back along it.
                Vector3 on = path.GetPoint(i + 3);
                Vector3 ro = Vector3.Cross(Vector3.up, path.GetTangent(i + 3)).normalized;
                eye = on + ro * (side * (half - 0.8f)) + Vector3.up * 1.1f;
                look = path.GetPoint(i - 2) + right * (side * (half + 0.8f)) + Vector3.up * 0.4f;
                PSXScreenshotTool.Shot(cam, "rail_" + id + "_" + shot + "_close", eye, Quaternion.LookRotation(look - eye));

                // From past it, looking back at the road over the rail.
                eye = path.GetPoint(i) + right * (side * (half + 7f)) + Vector3.up * 2.2f;
                look = path.GetPoint(i) + Vector3.up * 0.3f;
                PSXScreenshotTool.Shot(cam, "rail_" + id + "_" + shot + "_out", eye, Quaternion.LookRotation(look - eye));
                Debug.Log("[RailShots] run " + runs[q].name + " at wp " + i + (side > 0 ? " R" : " L"));
                shot++;
            }
            Debug.Log("[RailShots] " + shot + " runs written to " + outDir);
        }
    }
}
