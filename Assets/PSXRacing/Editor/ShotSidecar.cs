using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// A JSON FILE BESIDE EVERY PNG THE TOOLS WRITE: what the frame was drawn
    /// with. The colour pass, 2026-09-29.
    ///
    /// Why it exists: the release budget (ReleaseBudget.SixteenBitCandidate,
    /// fc551ef, 09-27) gave ~926 opaque sRGB textures a WebGL-only RGB565
    /// override. WebGL2 has no sRGB 565 and the project is Linear, so on the
    /// WebGL target every such texel is read AS LINEAR - asphalt up to x9
    /// brighter. A texture is imported for the editor's ACTIVE build target,
    /// the sandboxes build with -buildTarget WebGL, and the shot tools
    /// launched with none: every editor frame after 09-27 carried the WebGL
    /// import, and nothing in a PNG said so. Frames rendered with two
    /// different imports were compared as if they were one game.
    ///
    /// So every frame now says which import it is (activeBuildTarget, and the
    /// probe texture city_road_track_120_asphalt_new - every CityCircuit road
    /// samples it - as the editor holds it: graphicsFormat, isDataSRGB, the
    /// WebGL override), the hour, weather, dress, quality, grade, lens, the
    /// exposure globals, and the commit. tools\colour\colour_stats.py refuses
    /// to compare two frames whose sidecars disagree on target.
    ///
    /// <see cref="WritePng"/> is the one call the tools use in place of
    /// File.WriteAllBytes(path, tex.EncodeToPNG()). A caller that knows more
    /// (the protocol spot, its projected regions, the camera) puts it in
    /// <see cref="Pending"/> before the write; it is merged into the next
    /// sidecar and cleared.
    /// </summary>
    public static class ShotSidecar
    {
        /// <summary>The texture whose import says which picture this is.</summary>
        public const string ProbeTexture = "city_road_track_120_asphalt_new";

        /// <summary>Fields merged into the NEXT sidecar written, then cleared.</summary>
        public static readonly Dictionary<string, object> Pending = new Dictionary<string, object>();

        /// <summary>Write the PNG and its sidecar (path + ".json").</summary>
        public static void WritePng(string path, byte[] png, IDictionary<string, object> extra = null)
        {
            File.WriteAllBytes(path, png);
            Write(path, extra);
        }

        /// <summary>Write (or rewrite) the sidecar of an existing PNG.</summary>
        public static void Write(string pngPath, IDictionary<string, object> extra = null)
        {
            try
            {
                var d = Describe(Path.GetFileName(pngPath));
                foreach (var kv in Pending) d[kv.Key] = kv.Value;
                Pending.Clear();
                if (extra != null) foreach (var kv in extra) d[kv.Key] = kv.Value;
                File.WriteAllText(pngPath + ".json", Json(d, 0) + "\n");
            }
            catch (System.Exception e)
            {
                // A sidecar that cannot be written must not lose the frame -
                // but it must not be silent either: a frame without one is
                // refused by the stats script.
                Pending.Clear();
                Debug.LogWarning("[ShotSidecar] no sidecar for " + pngPath + ": " + e.Message);
            }
        }

        /// <summary>What every frame carries.</summary>
        public static Dictionary<string, object> Describe(string file)
        {
            var d = new Dictionary<string, object>
            {
                ["file"] = file,
                ["written"] = System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                ["commit"] = System.Environment.GetEnvironmentVariable("PSX_COMMIT") ?? "",
                ["tool"] = Arg("-executeMethod") ?? "",
                ["unity"] = Application.unityVersion,
                ["activeBuildTarget"] = EditorUserBuildSettings.activeBuildTarget.ToString(),
                ["requestedBuildTarget"] = Arg("-buildTarget") ?? "",
                ["colorSpace"] = PlayerSettings.colorSpace.ToString(),
                ["playing"] = Application.isPlaying,
                ["probe"] = Probe(),
            };

            int hour = TimeOfDay.Current;
            d["hour"] = hour;
            d["hourName"] = TimeOfDay.At(hour).name;
            d["weather"] = Seasons.CurrentWeather.ToString();
            d["weatherOverride"] = RaceHandoff.WeatherOverride;
            d["calendarDay"] = RaceHandoff.CalendarDay;
            d["season"] = Seasons.Current.ToString();
            int dress = SeasonDress.AppliedDress;
            d["dress"] = dress;
            d["dressName"] = dress >= 0 && dress < Seasons.DressNames.Length ? Seasons.DressNames[dress] : "BAKED";

            var output = Object.FindAnyObjectByType<PSXCameraOutput>();
            d["quality"] = new Dictionary<string, object>
            {
                ["pixels"] = PSXQuality.Current.ToString(),
                ["colorDepth"] = PSXQuality.ColorDepth,
                ["dither"] = PSXQuality.Dither,
                ["lines"] = output != null ? output.height : 0,
            };
            // The editor's Dithered() reads PSX_GRADE; a play-mode frame is
            // the display material's own, which FilmGradePrefs drives.
            bool gradeOff = System.Environment.GetEnvironmentVariable("PSX_GRADE") == "0";
            d["grade"] = Application.isPlaying ? FilmGradePrefs.Amount : (gradeOff ? 0f : 1f);
            d["lens"] = LensFx.Active
                ? (object)new Dictionary<string, object> { ["rain"] = LensFx.Rain, ["dirt"] = LensFx.Dirt, ["time"] = LensFx.Time }
                : null;
            // The exposure chain (C2/C10). A global never set reads 0: null
            // here means "this build has no such stage", not "exposure 0".
            d["exposure"] = GlobalOrNull("_PSXExposure");
            d["adapt"] = GlobalOrNull("_PSXAdapt");
            d["toneOn"] = GlobalOrNull("_PSXToneOn");
            d["wetness"] = Shader.GetGlobalFloat("_PSXWetness");
            d["night"] = Shader.GetGlobalFloat("_PSXNight");
            d["headlightsPushed"] = CarLights.PushedCount;
            d["streetLampsPushed"] = StreetLights.PushedCount;
            return d;
        }

        static object GlobalOrNull(string name)
        {
            float v = Shader.GetGlobalFloat(name);
            return v == 0f ? null : (object)v;
        }

        static string cachedProbePath;

        /// <summary>The probe texture as the editor holds it right now.</summary>
        public static Dictionary<string, object> Probe()
        {
            var p = new Dictionary<string, object> { ["name"] = ProbeTexture };
            if (cachedProbePath == null)
            {
                cachedProbePath = "";
                foreach (var guid in AssetDatabase.FindAssets(ProbeTexture + " t:Texture2D"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileNameWithoutExtension(path) == ProbeTexture) { cachedProbePath = path; break; }
                }
            }
            if (cachedProbePath.Length == 0) { p["missing"] = true; return p; }
            p["path"] = cachedProbePath;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(cachedProbePath);
            if (tex != null)
            {
                p["graphicsFormat"] = tex.graphicsFormat.ToString();
                p["textureFormat"] = tex.format.ToString();
                p["isDataSRGB"] = tex.isDataSRGB;
            }
            var imp = AssetImporter.GetAtPath(cachedProbePath) as TextureImporter;
            if (imp != null)
            {
                p["sRGBTexture"] = imp.sRGBTexture;
                var web = imp.GetPlatformTextureSettings("WebGL");
                p["webglOverride"] = web != null && web.overridden ? web.format.ToString() : "none";
            }
            return p;
        }

        static string Arg(string key)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], key, System.StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        // ------------------------------------------------------------------
        //  A small JSON writer: dictionaries, lists, strings, numbers, bools,
        //  vectors (as arrays), rects ([x, y, w, h]) and null.
        // ------------------------------------------------------------------

        public static string Json(object v, int indent)
        {
            var sb = new StringBuilder();
            Emit(sb, v, indent);
            return sb.ToString();
        }

        static void Emit(StringBuilder sb, object v, int indent)
        {
            string pad = new string(' ', indent * 2), pad1 = new string(' ', (indent + 1) * 2);
            switch (v)
            {
                case null: sb.Append("null"); return;
                case string s: Str(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case float f: Num(sb, f); return;
                case double db: Num(sb, db); return;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); return;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case Vector2 v2: sb.Append('['); Num(sb, v2.x); sb.Append(", "); Num(sb, v2.y); sb.Append(']'); return;
                case Vector3 v3: sb.Append('['); Num(sb, v3.x); sb.Append(", "); Num(sb, v3.y); sb.Append(", "); Num(sb, v3.z); sb.Append(']'); return;
                case Rect r: sb.Append('['); Num(sb, r.x); sb.Append(", "); Num(sb, r.y); sb.Append(", "); Num(sb, r.width); sb.Append(", "); Num(sb, r.height); sb.Append(']'); return;
                case IDictionary<string, object> dict:
                {
                    if (dict.Count == 0) { sb.Append("{}"); return; }
                    sb.Append("{\n");
                    int k = 0;
                    foreach (var kv in dict)
                    {
                        sb.Append(pad1); Str(sb, kv.Key); sb.Append(": ");
                        Emit(sb, kv.Value, indent + 1);
                        sb.Append(++k < dict.Count ? ",\n" : "\n");
                    }
                    sb.Append(pad).Append('}');
                    return;
                }
                case IEnumerable seq:
                {
                    var items = new List<object>();
                    foreach (var o in seq) items.Add(o);
                    bool flat = items.TrueForAll(o => o == null || o is string || o is bool || o is float || o is double || o is int || o is long);
                    if (flat)
                    {
                        sb.Append('[');
                        for (int i2 = 0; i2 < items.Count; i2++) { if (i2 > 0) sb.Append(", "); Emit(sb, items[i2], indent); }
                        sb.Append(']');
                        return;
                    }
                    sb.Append("[\n");
                    for (int i2 = 0; i2 < items.Count; i2++)
                    {
                        sb.Append(pad1); Emit(sb, items[i2], indent + 1);
                        sb.Append(i2 + 1 < items.Count ? ",\n" : "\n");
                    }
                    sb.Append(pad).Append(']');
                    return;
                }
                default: Str(sb, v.ToString()); return;
            }
        }

        static void Num(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            sb.Append(d.ToString("0.######", CultureInfo.InvariantCulture));
        }

        static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
