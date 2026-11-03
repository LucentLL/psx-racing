using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE WORKER-COMPILE CHECK (dividers round 4, the review's item 7). CityMap.Load starts
    /// compiling the divider plan's methods (CityMeshes.DivPrejitMethods) on a thread-pool
    /// worker beside the elevation solve. Mono may run a beforefieldinit class's static
    /// constructor while it compiles a method that reads that class's statics - so on the
    /// worker. A static constructor that touches a main-thread-only engine API would then
    /// leave its class broken for the session.
    ///
    /// This reads every compiled method's IL, collects each class it touches (fields, calls,
    /// constructions, type tokens), and for each class with a static constructor reports:
    ///   - a project class: it must be in CityMeshes.DivPrejitMainInit (initialised on the
    ///     main thread before the worker starts) - FAIL otherwise;
    ///   - an engine or BCL class: what its static constructor calls in the engine (an
    ///     extern / internal call or a UnityEngine.Object member is FLAGGED for review).
    /// Writes PSXRacing_prejit.txt at the project root. Headless:
    /// -executeMethod PSXRacing.EditorTools.DivPrejitCheck.Run
    /// </summary>
    public static class DivPrejitCheck
    {
        static readonly Dictionary<short, OpCode> ops = new Dictionary<short, OpCode>();

        public static void Run()
        {
            var log = new StringBuilder();
            bool ok = true;
            foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.GetValue(null) is OpCode oc) ops[oc.Value] = oc;
            var methods = CityMeshes.DivPrejitMethods();
            var types = new SortedDictionary<string, Type>();
            foreach (var m in methods) Collect(m, types);
            var mainInit = new HashSet<Type>(CityMeshes.DivPrejitMainInit);
            log.AppendLine($"BUILD HEAD: {Environment.GetEnvironmentVariable("PSX_BUILD_HEAD") ?? "(unset)"}");
            log.AppendLine($"=== the worker compiles {methods.Count} methods of CityMeshes; they touch {types.Count} classes ===");
            int withCctor = 0;
            foreach (var kv in types)
            {
                var t = kv.Value;
                var cc = t.TypeInitializer;
                if (cc == null) continue;
                withCctor++;
                bool bfi = (t.Attributes & TypeAttributes.BeforeFieldInit) != 0;
                bool ours = t.Assembly == typeof(CityMeshes).Assembly;
                if (ours)
                {
                    // a nested class of a main-initialised class is not initialised by it
                    bool covered = mainInit.Contains(t);
                    string calls = EngineCalls(cc, 0, new HashSet<MethodBase>());
                    if (!covered) ok = false;
                    log.AppendLine($"  {(covered ? "ok  " : "FAIL")} project {t.FullName} (static constructor, beforefieldinit {bfi}): {(covered ? "initialised on the main thread first (DivPrejitMainInit)" : "NOT in DivPrejitMainInit")}; engine calls in it:{(calls.Length > 0 ? calls : " none")}");
                }
                else
                {
                    string calls = EngineCalls(cc, 0, new HashSet<MethodBase>());
                    bool native = calls.Contains("[extern]") || calls.Contains("[Object]");
                    bool covered = mainInit.Contains(t);
                    bool flag = native && !covered;
                    if (flag) ok = false;
                    log.AppendLine($"  {(flag ? "FLAG" : "ok  ")} {(t.Namespace != null && t.Namespace.StartsWith("UnityEngine") ? "engine" : "BCL")} {t.FullName} (static constructor, beforefieldinit {bfi}): {(covered ? "initialised on the main thread first (DivPrejitMainInit); " : native ? "" : "managed only; ")}engine calls:{(calls.Length > 0 ? calls : " none")}");
                }
            }
            log.AppendLine($"=== {withCctor} of them have a static constructor ===");
            int ourN = 0; foreach (var t in types.Values) if (t.Assembly == typeof(CityMeshes).Assembly) ourN++;
            log.AppendLine($"(of the {types.Count} classes touched, {ourN} are the project's own; the rest are engine and BCL generics and primitives)");
            log.AppendLine(ok ? "PREJIT CHECK OK: no static constructor of ours can run on the worker, and none of the engine/BCL ones it may run calls the engine natively"
                              : "PREJIT CHECK FAILED");
            File.WriteAllText("PSXRacing_prejit.txt", log.ToString());
            UnityEngine.Debug.Log(log.ToString());
        }

        static void Add(Type t, SortedDictionary<string, Type> into)
        {
            if (t == null) return;
            if (t.IsArray || t.IsByRef || t.IsPointer) { Add(t.GetElementType(), into); return; }
            if (t.IsGenericParameter) return;
            if (t.IsGenericType) foreach (var g in t.GetGenericArguments()) Add(g, into);
            for (var d = t; d != null; d = d.DeclaringType)
            {
                string k = d.FullName ?? d.Name;
                if (!into.ContainsKey(k)) into[k] = d;
                // a base class's static constructor runs with a derived one's
                for (var b = d.BaseType; b != null && b != typeof(object) && b != typeof(ValueType); b = b.BaseType)
                    if (!into.ContainsKey(b.FullName ?? b.Name)) into[b.FullName ?? b.Name] = b;
            }
        }

        static void Collect(MethodBase m, SortedDictionary<string, Type> into)
        {
            Add(m.DeclaringType, into);
            var body = m.GetMethodBody();
            if (body == null) return;
            foreach (var lv in body.LocalVariables) Add(lv.LocalType, into);
            foreach (var (op, tok) in Tokens(body.GetILAsByteArray()))
            {
                try
                {
                    switch (op.OperandType)
                    {
                        case OperandType.InlineField: { var f = m.Module.ResolveField(tok, Generic(m.DeclaringType), Generic(m)); Add(f.DeclaringType, into); Add(f.FieldType, into); break; }
                        case OperandType.InlineMethod: { var c = m.Module.ResolveMethod(tok, Generic(m.DeclaringType), Generic(m)); Add(c.DeclaringType, into); if (c is MethodInfo mi) Add(mi.ReturnType, into); break; }
                        case OperandType.InlineType: Add(m.Module.ResolveType(tok, Generic(m.DeclaringType), Generic(m)), into); break;
                        case OperandType.InlineTok: { var mem = m.Module.ResolveMember(tok, Generic(m.DeclaringType), Generic(m)); if (mem is Type ty) Add(ty, into); else Add(mem.DeclaringType, into); break; }
                    }
                }
                catch (Exception) { }
            }
        }

        static Type[] Generic(Type t) => t != null && t.IsGenericType ? t.GetGenericArguments() : null;
        static Type[] Generic(MethodBase m) => m.IsGenericMethod ? m.GetGenericArguments() : null;

        /// <summary>The engine members a static constructor calls (two levels into managed
        /// callees): [extern] for an internal/extern call, [Object] for a UnityEngine.Object
        /// member.</summary>
        static string EngineCalls(MethodBase m, int depth, HashSet<MethodBase> seen)
        {
            if (!seen.Add(m)) return "";
            var body = m.GetMethodBody();
            if (body == null) return "";
            var sb = new StringBuilder();
            foreach (var (op, tok) in Tokens(body.GetILAsByteArray()))
            {
                if (op.OperandType != OperandType.InlineMethod) continue;
                MethodBase c;
                try { c = m.Module.ResolveMethod(tok, Generic(m.DeclaringType), Generic(m)); } catch (Exception) { continue; }
                var dt = c.DeclaringType;
                bool engine = dt?.Namespace != null && dt.Namespace.StartsWith("UnityEngine");
                bool ext = (c.MethodImplementationFlags & MethodImplAttributes.InternalCall) != 0 || (c.Attributes & MethodAttributes.PinvokeImpl) != 0;
                bool obj = dt != null && typeof(UnityEngine.Object).IsAssignableFrom(dt);
                if (engine || ext)
                {
                    string tag = (ext ? " [extern]" : "") + (obj ? " [Object]" : "");
                    string s = $" {dt?.Name}.{c.Name}{tag};";
                    if (sb.ToString().IndexOf(s, StringComparison.Ordinal) < 0) sb.Append(s);
                }
                if (depth < 2 && !ext && c.GetMethodBody() != null && (engine || dt?.Assembly == m.DeclaringType?.Assembly))
                    sb.Append(EngineCalls(c, depth + 1, seen));
            }
            return sb.ToString();
        }

        static IEnumerable<(OpCode op, int tok)> Tokens(byte[] il)
        {
            if (il == null) yield break;
            int i = 0;
            while (i < il.Length)
            {
                short v = il[i++];
                if (v == 0xFE && i < il.Length) v = (short)(0xFE00 | il[i++]);
                if (!ops.TryGetValue(v, out var op)) yield break;
                int size;
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: size = 0; break;
                    case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
                    case OperandType.InlineVar: size = 2; break;
                    case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
                    case OperandType.InlineSwitch: { int n = BitConverter.ToInt32(il, i); size = 4 + 4 * n; break; }
                    default: size = 4; break;
                }
                if (size == 4 && (op.OperandType == OperandType.InlineField || op.OperandType == OperandType.InlineMethod || op.OperandType == OperandType.InlineType || op.OperandType == OperandType.InlineTok))
                    yield return (op, BitConverter.ToInt32(il, i));
                i += size;
            }
        }
    }
}
