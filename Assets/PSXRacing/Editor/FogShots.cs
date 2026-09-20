using System.IO;
using UnityEditor;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// WHAT THE DISTANCE ACTUALLY LOOKS LIKE — the same view down the same
    /// road, under every candidate fog setting, so the band is chosen from
    /// pictures instead of from arithmetic.
    ///
    /// Written for "I'm not a fan of objects in the distance being
    /// white/foggy". The three knobs are independent and only one of them is
    /// free, so they have to be seen apart before they are seen together:
    ///   BAND   — fogNear/fogFar, multiplied over the hour preset. Longer
    ///            band means less fog at any distance, but the far plane has
    ///            to move with it or geometry pops through the end.
    ///   CLIP   — the camera's far plane. The only knob that costs frame rate,
    ///            which matters: this game is played on a phone.
    ///   CURVE  — PSXGlobals.fogCurve. Free, and changes neither end.
    ///
    /// NOT through PSXScreenshotTool's overview path, which pushes the fog out
    /// of the way on purpose — every reference shot this project has taken of
    /// a whole circuit was taken with the fog disabled, which is one reason
    /// this was never looked at. PSXGlobals is [ExecuteAlways] and re-pushes
    /// its fields every editor tick, so the settings go on the COMPONENT; a
    /// Shader.SetGlobalFloat here would be overwritten between the call and
    /// the render.
    ///
    ///   PSX_FOG_VENUE  venue id (default CityCircuit)
    ///   PSX_FOG_HOUR   hour name (default noon)
    /// Menu: PSX Racing/Capture Fog Views. Writes Screenshots/psx_fog_*.png.
    /// </summary>
    public static class FogShots
    {
        struct Variant
        {
            public string label;
            /// <summary>Multiplier on the scene's baked fog band.</summary>
            public float band;
            /// <summary>Multiplier on the scene's baked camera far plane.</summary>
            public float clip;
            public float curve;
        }

        static readonly Variant[] Variants =
        {
            // The picture the player is complaining about.
            new Variant { label = "0now",      band = 1f,   clip = 1f,   curve = 1f },
            // Curve alone — costs nothing at all.
            new Variant { label = "1curve18",  band = 1f,   clip = 1f,   curve = 1.8f },
            new Variant { label = "2curve22",  band = 1f,   clip = 1f,   curve = 2.2f },
            new Variant { label = "3curve30",  band = 1f,   clip = 1f,   curve = 3.0f },
            // Band alone, with the far plane moved to match.
            new Variant { label = "4band16",   band = 1.6f, clip = 1.6f, curve = 1f },
            // Both.
            new Variant { label = "5band16c22", band = 1.6f, clip = 1.6f, curve = 2.2f },
            new Variant { label = "6band24c22", band = 2.4f, clip = 2.4f, curve = 2.2f },
            // The control: no fog and a far plane past everything, which says
            // whether there is anything out there worth seeing in the first
            // place. If the world simply ENDS at 400 m then pushing the band
            // out buys an empty horizon and the curve is the whole answer.
            new Variant { label = "7nofog",    band = 30f,  clip = 8f,   curve = 1f },
            // And the actual proposal, which is not a multiple of anything:
            // filled in per venue from the constants below.
            new Variant { label = "8proposed", band = 0f,   clip = 0f,   curve = TimeOfDay.FogCurve },
        };

        /// <summary>
        /// The proposal, absolute rather than relative, because the two kinds
        /// of venue start from different places and are limited by different
        /// things. A circuit (and the streamed city) is held back by the CITY
        /// TILE RING — 2 tiles of 256 m, so the built world can be as little
        /// as 512 m away and a far plane past that would show its edge. A
        /// stage already draws to 1,500 m and closes its fog at 1,136, so the
        /// last 364 m of what it draws is painted flat fog colour: raising its
        /// scale is free, and buys back terrain that is already being
        /// rasterised.
        /// </summary>
        const float CircuitScale = 1.4f, CircuitClip = 500f;
        const float StageScale = 4.0f, StageClip = 1500f;

        [MenuItem("PSX Racing/Capture Fog Views")]
        public static void Capture()
        {
            string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
            Directory.CreateDirectory(outDir);

            string venues = System.Environment.GetEnvironmentVariable("PSX_FOG_VENUE");
            if (string.IsNullOrEmpty(venues)) venues = "CityCircuit";
            string hourName = System.Environment.GetEnvironmentVariable("PSX_FOG_HOUR");
            if (string.IsNullOrEmpty(hourName)) hourName = "noon";

            // One Unity launch for the whole sweep: opening a scene is seconds
            // and starting the editor is minutes.
            foreach (string one in venues.Split(','))
            {
                string id = one.Trim();
                if (id.Length > 0) CaptureOne(id, hourName, outDir);
            }
            Debug.Log("[FogShots] written to " + outDir);
        }

        static void CaptureOne(string id, string hourName, string outDir)
        {
            foreach (var f in Directory.GetFiles(outDir, "psx_fog_" + id + "_*.png")) File.Delete(f);

            TrackCatalog.TrackDef def = null;
            for (int i = 0; i < TrackCatalog.Count; i++)
                if (TrackCatalog.At(i).id == id) def = TrackCatalog.At(i);
            if (def == null) { Debug.LogError("[FogShots] no venue " + id); return; }
            if (!PSXScreenshotTool.Open(def, out var cam, out _)) return;

            var path = Object.FindFirstObjectByType<TrackPath>();
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (path == null || path.Count < 50) { Debug.LogError("[FogShots] no path"); return; }
            if (globals == null) { Debug.LogError("[FogShots] no PSXGlobals"); return; }

            int hour = 0;
            for (int h = 0; h < TimeOfDay.Count; h++)
                if (TimeOfDay.At(h).name.ToLower() == hourName.ToLower()) hour = h;
            if (sun != null) TimeOfDay.Apply(hour, sun);

            // The baked band AFTER the hour has been applied — that is what the
            // player is shown, and it already carries the scene's fogScale and
            // the day's weather multiplier.
            float baseNear = globals.fogNear, baseFar = globals.fogFar;
            float baseClip = cam.farClipPlane;
            Debug.Log("[FogShots] " + id + " " + TimeOfDay.At(hour).name +
                      ": band " + baseNear.ToString("0") + ".." + baseFar.ToString("0") +
                      " m, far plane " + baseClip.ToString("0") + " m, fogScale " +
                      globals.fogScale.ToString("0.00"));

            // Stations 0-2 are the driver's eye, a metre and a half up,
            // looking where the road goes. Station 3 is the same road from
            // 30 m up — the crest-of-a-hill view, and the only one of the four
            // that puts real distance in frame. At eye height on a road lined
            // with walls and trees almost nothing is even as far away as
            // fogNear, which is exactly why this was worth measuring before
            // picking a number.
            var stations = new[] { 0.10f, 0.42f, 0.74f };
            const float VistaUp = 30f;

            // The proposal expressed as a multiplier on what this scene baked,
            // so it goes through the same two lines as every other variant.
            bool isStage = def.stage;
            float wantScale = isStage ? StageScale : CircuitScale;
            float propBand = wantScale / Mathf.Max(0.01f, globals.fogScale);
            float propClip = (isStage ? StageClip : CircuitClip) / Mathf.Max(1f, baseClip);

            foreach (var v in Variants)
            {
                float band = v.band > 0f ? v.band : propBand;
                float clip = v.clip > 0f ? v.clip : propClip;
                globals.fogNear = baseNear * band;
                globals.fogFar = baseFar * band;
                globals.fogCurve = v.curve;
                globals.Apply();
                cam.farClipPlane = baseClip * clip;

                for (int s = 0; s <= stations.Length; s++)
                {
                    bool vista = s == stations.Length;
                    float f = vista ? 0.42f : stations[s];
                    int at = Mathf.Clamp(Mathf.RoundToInt(f * path.Count), 2, path.Count - 12);
                    Vector3 eye = path.GetPoint(at) + Vector3.up * (vista ? VistaUp : 1.5f);
                    Vector3 look = path.GetPoint(at + (vista ? 60 : 10)) + Vector3.up * 1.2f;
                    PSXScreenshotTool.Shot(cam, "fog_" + id + "_" + s + "_" + v.label, eye,
                                           Quaternion.LookRotation(look - eye));
                }
            }

            globals.fogNear = baseNear;
            globals.fogFar = baseFar;
            globals.fogCurve = TimeOfDay.FogCurve;
            globals.Apply();
            cam.farClipPlane = baseClip;
        }
    }
}
