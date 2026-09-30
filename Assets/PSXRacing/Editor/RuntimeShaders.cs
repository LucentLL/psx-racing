using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// EVERY SHADER THE RUNTIME NAMES, AND WHETHER EVERY PLAYER CAN HAVE IT.
    ///
    /// 2026-09-29: the CITY edition went live with no headlight, tail-light or
    /// brake-light glow on any car and "CarLights: PSX/Glow shader missing"
    /// six times in the console. CarLights makes its lens materials at
    /// runtime, through Shader.Find, and Shader.Find in a PLAYER finds only a
    /// shader the build packed. The whole game packed PSX/Glow by accident -
    /// the garage and the stage lamp posts have materials on it - and CITY
    /// ships neither, so its build stripped the shader. Nothing could notice:
    /// the editor finds every shader in the project, so every editor test,
    /// play check and preview had its glow.
    ///
    /// THE RULE, DERIVED FROM THE SOURCE (never a list to remember - two lists
    /// that must agree, will not): a shader is NAMED AT RUNTIME when a runtime
    /// .cs file (under Assets, in no Editor folder) holds a string literal that
    ///   * is the declared name of a .shader under Assets (Shader "PSX/Glow"),
    ///     wherever it stands - CarLights hands "PSX/Glow" to a helper that
    ///     calls Shader.Find(shaderName), so the literal is the only trace; or
    ///   * is the argument of Shader.Find("...") - that catches the engine's
    ///     own (UI/Default, Sprites/Default) - or the const string an
    ///     identifier argument names (Shader.Find(LensFx.ShaderName)).
    /// Comments do not count. A name only compared against (SunShadows asks
    /// "is this PSX/CarPaint?") counts too: the code expects to meet it.
    ///
    /// EVERY ONE IS IN GRAPHICSSETTINGS' ALWAYS INCLUDED SHADERS, which every
    /// edition's player carries whatever its scenes reference. Measured
    /// against the other robust way (a Resources material per shader): the
    /// project's lookups already lived in that list (Beam, Halo, Rain, Lens,
    /// ShadowCaster, SkyEquirect), none of these shaders has a keyword, so
    /// "all variants" is one per pass, and a shader a scene already ships is
    /// packed ONCE either way - so only a shader the edition was missing adds
    /// bytes (CITY: PSX/Glow, a few KiB). A Resources material would ship in
    /// every edition as well and add a material and a place to forget.
    ///
    /// CHECKED THREE TIMES, and each failure stops a build or a publish:
    ///   1. PSXBuildWebGL, BEFORE BuildPlayer (<see cref="Gaps"/>): every
    ///      named shader exists and is always included - a second, not twenty
    ///      minutes;
    ///   2. PSXBuildWebGL, AFTER it (<see cref="ReportLines"/>): the build
    ///      report packed every project shader named, when the report lists
    ///      packed assets at all (an incremental build that reuses its content
    ///      lists none), and the list goes into psx-build-report.txt as
    ///      "runtime-shader" lines;
    ///   3. tools/webgl-contents.mjs, the WebGL CONTENTS proof every publish
    ///      runs: the same rule applied independently to the same files, each
    ///      name looked up among the Shader objects IN WebGL.data, and its
    ///      list compared with the report's.
    ///
    /// PSX/Lit HAS ONE KEYWORD NOW (PSX_ATLAS_RECT, shader_feature_local:
    /// WP-07's city prop atlas). Always included means every variant, so it
    /// ships both, a pair per pass - the atlas variant the city's merged
    /// restaurants and houses draw with included, whatever the scenes hold.
    ///
    /// THE CITY KIT (Resources/CityKit.asset, WP-07/08) is the other way the
    /// runtime reaches a shader: CityKit.Get() is a Resources.Load, and the
    /// streamed city, its canopy trees and its lamp posts draw with the kit's
    /// materials - no scene holds them. Those shaders need not be always
    /// included (an edition that ships the kit ships them with it, and one
    /// that parks it has no city), so they are a list of their own,
    /// <see cref="Result.Kit"/>: every shader a kit material uses (slots,
    /// lamp posts, the five tree dresses, the reserved fields) and every one
    /// its own shaders list names. The pre-flight fails on a kit material
    /// with no shader or a kit shader that is gone; the build report lists
    /// them as "kit-shader" lines and fails the build on one it did not pack;
    /// and webgl-contents.mjs finds each in WebGL.data whenever the edition
    /// ships the kit, and compares the list with its own read of the asset.
    /// </summary>
    public static class RuntimeShaders
    {
        public sealed class Need
        {
            /// <summary>"PSX/Glow".</summary>
            public string Name;
            /// <summary>What Shader.Find gives the editor; null if nothing has the name.</summary>
            public Shader Shader;
            /// <summary>Assets/.../PSXGlow.shader, or where the engine keeps its own.</summary>
            public string AssetPath;
            public bool AlwaysIncluded;
            /// <summary>"PSXRacing/Scripts/CarLights.cs:644", one per literal.</summary>
            public readonly List<string> Sites = new List<string>();
        }

        public sealed class Result
        {
            public readonly List<Need> Needs = new List<Need>();
            /// <summary>Shader.Find calls whose argument is neither a literal nor
            /// a const ("PSXRacing/Scripts/CarLights.cs:629 Shader.Find(shaderName)").
            /// Their names reach them as literals, which the first rule counts;
            /// listed so a reader can see every lookup.</summary>
            public readonly List<string> Indirect = new List<string>();
            public int RuntimeFiles, ShaderFiles;

            /// <summary>The city kit's shaders (see the class notes), by name;
            /// each Need's Sites say which kit fields reach it ("CityKit.trees x5").
            /// Empty, with <see cref="KitNote"/> saying why, when there is no kit.</summary>
            public readonly List<Need> Kit = new List<Need>();
            /// <summary>Kit entries that would draw pink or not at all: a
            /// material with no shader, a shaders-list entry that is gone.</summary>
            public readonly List<string> KitErrors = new List<string>();
            public string KitNote;
        }

        /// <summary>The one city kit (PSXRacingBuilder.EnsureCityKit writes it).</summary>
        public const string KitPath = "Assets/PSXRacing/Resources/" + PSXRacing.City.CityKit.ResourcePath + ".asset";

        static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        /// <summary>Is <paramref name="assetsRelative"/> (a path under Assets,
        /// either separator) runtime code? Anything in an Editor folder at any
        /// depth is not.</summary>
        public static bool IsRuntimePath(string assetsRelative) =>
            !assetsRelative.Replace('\\', '/').Split('/').Any(seg => seg == "Editor");

        static readonly Regex ShaderDecl = new Regex(@"^\s*Shader\s+""([^""]+)""", RegexOptions.Multiline);
        static readonly Regex ConstString = new Regex(@"\bconst\s+string\s+([A-Za-z_]\w*)\s*=\s*""((?:[^""\\\n]|\\.)*)""");
        static readonly Regex FindCall = new Regex(
            @"\bShader\s*\.\s*Find\s*\(\s*(?:@?""((?:[^""\\\n]|\\.)*)""|([A-Za-z_][\w.]*))\s*\)");

        public static Result Scan()
        {
            var r = new Result();
            string assets = Application.dataPath;

            // Every shader the project declares, by the name Shader.Find takes.
            var declared = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in Directory.GetFiles(assets, "*.shader", SearchOption.AllDirectories))
            {
                r.ShaderFiles++;
                var m = ShaderDecl.Match(Lex(File.ReadAllText(f), out _));
                if (m.Success && !declared.ContainsKey(m.Groups[1].Value))
                    declared[m.Groups[1].Value] = "Assets/" + Rel(assets, f);
            }

            var needs = new Dictionary<string, Need>(StringComparer.Ordinal);
            void Add(string name, string site)
            {
                if (!needs.TryGetValue(name, out var n)) needs[name] = n = new Need { Name = name };
                if (!n.Sites.Contains(site)) n.Sites.Add(site);
            }

            var consts = new Dictionary<string, string>(StringComparer.Ordinal);
            var idents = new List<(string ident, string site, string call)>();
            foreach (var f in Directory.GetFiles(assets, "*.cs", SearchOption.AllDirectories)
                                       .OrderBy(p => p, StringComparer.Ordinal))
            {
                string rel = Rel(assets, f);
                if (!IsRuntimePath(rel)) continue;
                r.RuntimeFiles++;
                string code = Lex(File.ReadAllText(f), out var lits);
                foreach (var (text, line) in lits)
                    if (declared.ContainsKey(text)) Add(text, rel + ":" + line);
                foreach (Match m in ConstString.Matches(code))
                    consts[m.Groups[1].Value] = m.Groups[2].Value;
                foreach (Match m in FindCall.Matches(code))
                {
                    string site = rel + ":" + LineAt(code, m.Index);
                    if (m.Groups[1].Success) Add(m.Groups[1].Value, site);
                    else idents.Add((m.Groups[2].Value, site, m.Value));
                }
            }
            foreach (var (ident, site, call) in idents)
            {
                string last = ident.Substring(ident.LastIndexOf('.') + 1);
                if (consts.TryGetValue(last, out var name)) Add(name, site);
                else r.Indirect.Add(site + " " + Regex.Replace(call, @"\s+", ""));
            }

            var always = AlwaysIncluded();
            foreach (var n in needs.Values.OrderBy(n => n.Name, StringComparer.Ordinal))
            {
                n.Shader = Shader.Find(n.Name);
                n.AssetPath = n.Shader != null ? AssetDatabase.GetAssetPath(n.Shader)
                            : declared.TryGetValue(n.Name, out var p) ? p : null;
                n.AlwaysIncluded = n.Shader != null && always.Contains(n.Shader);
                r.Needs.Add(n);
            }
            ScanKit(r, always);
            return r;
        }

        /// <summary>Every shader the city kit reaches: its materials' and its
        /// shaders list's. Sites are the kit fields, counted.</summary>
        static void ScanKit(Result r, HashSet<Shader> always)
        {
            var kit = AssetDatabase.LoadAssetAtPath<PSXRacing.City.CityKit>(KitPath);
            if (kit == null) { r.KitNote = "no city kit at " + KitPath; return; }
            var found = new Dictionary<string, (Shader sh, Dictionary<string, int> fields)>(StringComparer.Ordinal);
            void Add(Shader sh, string field)
            {
                if (!found.TryGetValue(sh.name, out var e))
                    found[sh.name] = e = (sh, new Dictionary<string, int>(StringComparer.Ordinal));
                e.fields.TryGetValue(field, out int c);
                e.fields[field] = c + 1;
            }
            void Mat(Material m, string field)
            {
                if (m == null) return;               // an empty slot draws nothing; EnsureCityKit warns
                if (m.shader == null) { r.KitErrors.Add("CityKit." + field + " material " + m.name + " has no shader"); return; }
                Add(m.shader, "CityKit." + field);
            }
            if (kit.slots != null) foreach (var m in kit.slots) Mat(m, "slots");
            Mat(kit.lampPost, "lampPost");
            if (kit.trees != null) foreach (var m in kit.trees) Mat(m, "trees");
            Mat(kit.furniture, "furniture");
            Mat(kit.paint, "paint");
            Mat(kit.signs, "signs");
            if (kit.shaders != null)
                for (int i = 0; i < kit.shaders.Length; i++)
                {
                    if (kit.shaders[i] == null) r.KitErrors.Add("CityKit.shaders[" + i + "] is missing (a shader the kit held is gone)");
                    else Add(kit.shaders[i], "CityKit.shaders");
                }
            foreach (var kv in found.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var n = new Need
                {
                    Name = kv.Key,
                    Shader = kv.Value.sh,
                    AssetPath = AssetDatabase.GetAssetPath(kv.Value.sh),
                    AlwaysIncluded = always.Contains(kv.Value.sh),
                };
                foreach (var f in kv.Value.fields.OrderBy(k => k.Key, StringComparer.Ordinal))
                    n.Sites.Add(f.Key + " x" + f.Value);
                r.Kit.Add(n);
            }
        }

        /// <summary>The shaders GraphicsSettings' Always Included list holds.</summary>
        public static HashSet<Shader> AlwaysIncluded()
        {
            var set = new HashSet<Shader>();
            var gs = GraphicsSettings.GetGraphicsSettings();
            if (gs == null) return set;
            var list = new SerializedObject(gs).FindProperty("m_AlwaysIncludedShaders");
            if (list == null) return set;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is Shader sh) set.Add(sh);
            return set;
        }

        /// <summary>What stops a build before it starts: a name nothing
        /// answers to, or a shader only a scene keeps (the PSX/Glow shape).</summary>
        public static List<string> Gaps(Result r)
        {
            var gaps = new List<string>();
            if (r.RuntimeFiles == 0) gaps.Add("no runtime .cs found under " + Application.dataPath + " - cannot know what the player looks up");
            foreach (var n in r.Needs)
            {
                string at = " (" + string.Join(", ", n.Sites) + ")";
                if (n.Shader == null)
                    gaps.Add(n.Name + " is named at runtime and no shader has that name" + at);
                else if (!n.AlwaysIncluded)
                    gaps.Add(n.Name + " is named at runtime and is NOT in GraphicsSettings' Always Included Shaders" + at +
                             " - an edition whose scenes do not reference it strips it and Shader.Find returns null in the player." +
                             " Add " + (n.AssetPath ?? n.Name) + " to Project Settings > Graphics > Always Included Shaders.");
            }
            // The kit's shaders ride in with the kit (Resources), so only a
            // broken entry stops the build: it would draw pink in every edition.
            foreach (var e in r.KitErrors)
                gaps.Add(e + " - rebuild the kit (PSXRacingBuilder.EnsureCityKit, any city scene build)");
            return gaps;
        }

        /// <summary>
        /// The psx-build-report.txt section, and what fails the build after
        /// BuildPlayer: a project shader the report did not pack. Built-in
        /// shaders are packed from the engine's own resources under one
        /// source path, so they are left to tools/webgl-contents.mjs, which
        /// finds every name among the player's Shader objects.
        /// </summary>
        public static List<string> ReportLines(Result r, BuildReport report, out List<string> failures) =>
            ReportLines(r, report, null, out failures);

        /// <summary><see cref="ReportLines(Result, BuildReport, out List{string})"/>,
        /// plus the city kit's "kit-shader" lines: none when this build parked
        /// the kit (<paramref name="parked"/>: EditionParking's list, as
        /// Resources-relative names), else each kit shader with the same
        /// packing verdict, and a project one the report did not pack fails
        /// the build.</summary>
        public static List<string> ReportLines(Result r, BuildReport report, IList<string> parked, out List<string> failures)
        {
            failures = new List<string>();
            var packed = new HashSet<string>(StringComparer.Ordinal);
            if (report != null)
                foreach (var pa in report.packedAssets)
                    foreach (var info in pa.contents)
                        if (!string.IsNullOrEmpty(info.sourceAssetPath)) packed.Add(info.sourceAssetPath);
            bool listed = packed.Count > 0;

            var lines = new List<string>
            {
                "runtime shaders " + r.Needs.Count + " (named by runtime code: RuntimeShaders; " + r.RuntimeFiles +
                " runtime .cs, " + r.ShaderFiles + " .shader; tools/webgl-contents.mjs checks each in the player)"
            };
            foreach (var n in r.Needs)
            {
                bool project = n.AssetPath != null &&
                               (n.AssetPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                                n.AssetPath.StartsWith("Packages/", StringComparison.Ordinal));
                string packing = !project ? "engine"
                               : !listed ? "packing not listed"
                               : packed.Contains(n.AssetPath) ? "packed" : "NOT PACKED";
                if (packing == "NOT PACKED")
                    failures.Add(n.Name + " (" + n.AssetPath + ") is named at runtime and the build did not pack it (" +
                                 string.Join(", ", n.Sites) + ")");
                lines.Add("  runtime-shader " + n.Name + " | " + (n.AlwaysIncluded ? "always-included" : "NOT always-included") +
                          " | " + packing + " | " + (n.AssetPath ?? "(no such shader)") + " | " + string.Join(" ", n.Sites));
            }
            foreach (var s in r.Indirect)
                lines.Add("  runtime-shader-indirect " + s + " - its names reach it as literals, counted above");

            // The city kit's, by the same packing rule. A parked kit shipped
            // nothing, so it asks for nothing (webgl-contents asks the same).
            string kitFile = Path.GetFileName(KitPath);
            bool kitParked = parked != null && parked.Any(p => string.Equals(p, kitFile, StringComparison.OrdinalIgnoreCase));
            if (r.KitNote != null) lines.Add("kit-shaders none (" + r.KitNote + ")");
            else if (kitParked) lines.Add("kit-shaders none (the city kit was parked for this edition)");
            else
            {
                lines.Add("kit-shaders " + r.Kit.Count + " (the city kit's materials and shaders list: RuntimeShaders.Kit; " +
                          "tools/webgl-contents.mjs checks each in the player)");
                foreach (var n in r.Kit)
                {
                    bool project = n.AssetPath != null &&
                                   (n.AssetPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                                    n.AssetPath.StartsWith("Packages/", StringComparison.Ordinal));
                    string packing = !project ? "engine"
                                   : !listed ? "packing not listed"
                                   : packed.Contains(n.AssetPath) ? "packed" : "NOT PACKED";
                    if (packing == "NOT PACKED")
                        failures.Add(n.Name + " (" + n.AssetPath + ") is a city kit shader and the build did not pack it (" +
                                     string.Join(", ", n.Sites) + ")");
                    lines.Add("  kit-shader " + n.Name + " | " + (n.AlwaysIncluded ? "always-included" : "with the kit") +
                              " | " + packing + " | " + (n.AssetPath ?? "(no asset)") + " | " + string.Join(" ", n.Sites));
                }
            }
            return lines;
        }

        // ------------------------------------------------------------------
        // A C# lexer just big enough to tell a string from a comment.

        /// <summary>
        /// <paramref name="src"/> with every comment blanked (newlines kept,
        /// so line numbers and offsets hold) and every string kept, plus the
        /// text of each string literal and the line it starts on. Regular,
        /// verbatim (@"" with "" for a quote) and interpolated ($"") strings;
        /// an interpolation hole is lexed as code (a string inside one is a
        /// literal of its own) and the interpolated string itself is not
        /// reported. Char literals are skipped whole, so '"' opens nothing.
        /// tools/webgl-contents.mjs carries the same lexer.
        /// </summary>
        public static string Lex(string src, out List<(string text, int line)> literals)
        {
            var lx = new Lexer(src);
            lx.Code(false);
            literals = lx.Lits;
            return lx.Out.ToString();
        }

        sealed class Lexer
        {
            readonly string s;
            int i, line = 1;
            public readonly StringBuilder Out;
            public readonly List<(string, int)> Lits = new List<(string, int)>();

            public Lexer(string src) { s = src; Out = new StringBuilder(src.Length); }

            char At(int k) => k < s.Length ? s[k] : '\0';
            void Copy() { char c = s[i++]; Out.Append(c); if (c == '\n') line++; }
            void Blank() { char c = s[i++]; Out.Append(c == '\n' ? '\n' : ' '); if (c == '\n') line++; }

            /// <summary>Code to the end, or (<paramref name="hole"/>) to the '}'
            /// that closes an interpolation hole, which is left unread.</summary>
            public void Code(bool hole)
            {
                int depth = 0;
                while (i < s.Length)
                {
                    char c = s[i], d = At(i + 1);
                    if (c == '/' && d == '/') { while (i < s.Length && s[i] != '\n') Blank(); continue; }
                    if (c == '/' && d == '*')
                    {
                        Blank(); Blank();
                        while (i < s.Length && !(s[i] == '*' && At(i + 1) == '/')) Blank();
                        if (i < s.Length) { Blank(); Blank(); }
                        continue;
                    }
                    if (hole)
                    {
                        if (c == '{') depth++;
                        else if (c == '}') { if (depth == 0) return; depth--; }
                    }
                    if (c == '\'')
                    {
                        Copy();
                        while (i < s.Length && s[i] != '\'' && s[i] != '\n') { if (s[i] == '\\') Copy(); if (i < s.Length) Copy(); }
                        if (i < s.Length && s[i] == '\'') Copy();
                        continue;
                    }
                    int j = i;
                    bool verbatim = false, interp = false;
                    while (j < s.Length && j - i < 2 && (s[j] == '@' || s[j] == '$'))
                    {
                        if (s[j] == '@') verbatim = true; else interp = true;
                        j++;
                    }
                    if (j < s.Length && s[j] == '"')
                    {
                        while (i < j) Copy();
                        Str(verbatim, interp);
                        continue;
                    }
                    Copy();
                }
            }

            void Str(bool verbatim, bool interp)
            {
                int at = line;
                Copy();                                   // the opening quote
                var text = new StringBuilder();
                bool holes = false;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (verbatim && c == '"' && At(i + 1) == '"') { Copy(); Copy(); text.Append('"'); continue; }
                    if (c == '"') { Copy(); break; }
                    if (!verbatim && c == '\n') break;    // unterminated: stop at the line
                    if (!verbatim && c == '\\')
                    {
                        Copy();
                        if (i < s.Length) { text.Append('\u0001'); Copy(); }   // an escape is never part of a name
                        continue;
                    }
                    if (interp && c == '{')
                    {
                        if (At(i + 1) == '{') { Copy(); Copy(); text.Append('{'); continue; }
                        holes = true;
                        Copy();
                        Code(true);
                        if (i < s.Length) Copy();         // the closing brace
                        continue;
                    }
                    if (interp && c == '}' && At(i + 1) == '}') { Copy(); Copy(); text.Append('}'); continue; }
                    text.Append(c);
                    Copy();
                }
                if (!holes) Lits.Add((text.ToString(), at));
            }
        }

        static string Rel(string root, string file) =>
            file.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');

        static int LineAt(string code, int index)
        {
            int n = 1;
            for (int k = 0; k < index && k < code.Length; k++) if (code[k] == '\n') n++;
            return n;
        }
    }
}
