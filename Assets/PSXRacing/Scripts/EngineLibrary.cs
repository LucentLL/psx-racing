using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// CARPARTS STAGE 4: WHICH ENGINE SITS IN A CAR'S BAY (decision 10: engines
    /// are a shared library by block shape, never part of a car). The model is
    /// chosen from the catalog row's block shape (LifeSim.CarXray.ShapeOf), the
    /// gearbox from the layout, the dress (turbo, supercharger, a V8's top end)
    /// from the row.
    ///
    ///   inline-2..4, inline-5 -> ENG_I4    inline-6 -> ENG_I6
    ///   V6 -> ENG_V6    V8, V10, V12, V16 -> ENG_V8 (overhead-cam top end)
    ///   flat-4 -> ENG_F4    flat-6 -> ENG_F6    2- and 4-rotor -> ENG_R2
    ///
    /// UNKNOWN TYPES. ShapeOf reads anything it cannot parse as an inline-4
    /// (CarXray.cs:64), which is right for the X-ray's drawing and wrong for a
    /// model: the four catalog rows with eType "-" take their shape from
    /// engineFamily's prefix instead (v6 -> V6, i4 -> I4, v8 -> V8, ...).
    /// ShapeOf itself is unchanged.
    ///
    /// LAYOUT. A car with an Engine_Mount says it (layout); otherwise (the
    /// stand-in, a car without 5A art) FF is transverse and FR, MR, RR, 4WD
    /// are longitudinal, unless the per-car table says otherwise.
    ///
    /// THE PER-CAR TABLE (<see cref="Table"/>) stays in the game for cars
    /// without an Engine_Mount and for the stand-in: position, rotation, scale,
    /// gearbox, dress, V8 top end, yaw 180 and reflect. A 5A car takes only
    /// the scale from it (0.92-1.08, default 1.0).
    /// </summary>
    public static class EngineLibrary
    {
        public const string I4 = "ENG_I4", I6 = "ENG_I6", V6 = "ENG_V6", V8 = "ENG_V8", F4 = "ENG_F4", F6 = "ENG_F6", R2 = "ENG_R2";
        public const string BayKit = "Bay_Kit";
        public static readonly string[] Keys = { I4, I6, V6, V8, F4, F6, R2 };

        public const float ScaleMin = 0.92f, ScaleMax = 1.08f;

        /// <summary>What a car's bay holds, chosen once.</summary>
        public struct Choice
        {
            public string key;
            public EngineLayout layout;
            /// <summary>Eng_Gearbox_Transaxle or Eng_Gearbox_Inline.</summary>
            public string gearbox;
            public int turbos;          // 0, 1 (Eng_Turbo), 2 (+ Eng_Turbo_2)
            public bool supercharger;
            /// <summary>A V8's top end: pushrod (OHV) rather than overhead cam.</summary>
            public bool pushrod;
            /// <summary>The front-mounted intercooler (Bay_Intercooler) or the
            /// flat-4's top mount (Bay_Intercooler_Top); none without a turbo.</summary>
            public bool intercooler, intercoolerTop;
            /// <summary>An electric fan at the radiator (Bay_Fan) rather than a
            /// belt-driven one on the engine (Bay_Fan_Clutch).</summary>
            public bool electricFan;
            public float scale;
            /// <summary>Why: the eType, or the engineFamily it fell back to.</summary>
            public string source;
        }

        /// <summary>One row of the per-car table (by catalog id, or a shell key for the stand-in).</summary>
        public class Row
        {
            public float scale = 1f;
            public EngineLayout layout = EngineLayout.Unknown;
            public bool hasMount;
            public Vector3 pos;            // body mesh space, the mount's origin
            public Quaternion rot = Quaternion.identity;
            public bool yaw180, reflect;
            public string key;             // force a model
        }

        /// <summary>The per-car table. Empty today but for the stand-in: every
        /// car takes the defaults until an Engine_Mount or a hand row says more.</summary>
        public static readonly Dictionary<string, Row> Table = new Dictionary<string, Row>();

        /// <summary>The block shape for the ENGINE CHOICE: ShapeOf, except an
        /// eType it cannot read ("-") takes engineFamily's prefix.</summary>
        public static LifeSim.CarXray.EngineShape ShapeFor(CarSpec spec, out string source)
        {
            string e = spec != null ? spec.eType ?? "" : "";
            source = e;
            string head = e.Trim();
            int sp = head.IndexOf(' ');
            if (sp >= 0) head = head.Substring(0, sp);
            if (head == "-" || head.Length == 0)
            {
                string fam = spec != null ? (spec.engineFamily ?? "").ToLowerInvariant() : "";
                source = "engineFamily " + fam;
                var s = new LifeSim.CarXray.EngineShape();
                if (fam.StartsWith("v6")) { s.kind = "vee"; s.perBank = 3; s.banks = 2; }
                else if (fam.StartsWith("v8")) { s.kind = "vee"; s.perBank = 4; s.banks = 2; }
                else if (fam.StartsWith("v10")) { s.kind = "vee"; s.perBank = 5; s.banks = 2; }
                else if (fam.StartsWith("v12")) { s.kind = "vee"; s.perBank = 6; s.banks = 2; }
                else if (fam.StartsWith("i6")) { s.kind = "inline"; s.perBank = 6; s.banks = 1; }
                else if (fam.StartsWith("boxer")) { s.kind = "flat"; s.perBank = 2; s.banks = 2; }
                else if (fam.StartsWith("rotary")) { s.kind = "rotary"; s.perBank = 2; s.banks = 1; }
                else { s.kind = "inline"; s.perBank = 4; s.banks = 1; }
                return s;
            }
            return LifeSim.CarXray.ShapeOf(e);
        }

        /// <summary>The model key for a shape (the stand-in mapping above).</summary>
        public static string KeyFor(LifeSim.CarXray.EngineShape s)
        {
            int cyl = s.perBank * s.banks;
            switch (s.kind)
            {
                case "vee": return cyl <= 6 ? V6 : V8;
                case "flat": return cyl <= 4 ? F4 : F6;
                case "rotary": return R2;
                default: return cyl >= 6 ? I6 : I4;
            }
        }

        /// <summary>The engine for this car: the catalog row (may be null: an
        /// inline-4), the shell (its Engine_Mount's layout) and its table row.</summary>
        public static Choice Choose(CarSpec spec, CarModelDef partsDef)
        {
            var shape = ShapeFor(spec, out string source);
            var c = new Choice { key = KeyFor(shape), source = source, scale = 1f };
            Row row = null;
            if (spec != null) Table.TryGetValue(spec.id, out row);
            if (row == null && partsDef != null) Table.TryGetValue(partsDef.key, out row);
            if (row != null && !string.IsNullOrEmpty(row.key)) c.key = row.key;
            if (row != null) c.scale = Mathf.Clamp(row.scale, ScaleMin, ScaleMax);
            // the layout: the mount's word, else the table's, else by the driven wheels
            c.layout = partsDef != null && partsDef.hasEngineMount ? partsDef.engineLayout : EngineLayout.Unknown;
            if (c.layout == EngineLayout.Unknown && row != null) c.layout = row.layout;
            if (c.layout == EngineLayout.Unknown) c.layout = spec != null && spec.drv == "FF" ? EngineLayout.Transverse : EngineLayout.Longitudinal;
            bool transverseOk = c.key == I4 || c.key == V6;
            c.gearbox = c.layout == EngineLayout.Transverse && transverseOk ? "Eng_Gearbox_Transaxle" : "Eng_Gearbox_Inline";
            string asp = spec != null ? (spec.asp ?? "") : "";
            bool turbo = asp.ToUpperInvariant().Contains("TURBO");
            // the catalog does not say twin: one turbo a bank on a V or a flat-6, one otherwise
            c.turbos = !turbo ? 0 : c.key == V6 || c.key == V8 || c.key == F6 ? 2 : 1;
            c.supercharger = asp.ToUpperInvariant().Contains("SUPER");
            string e = spec != null ? (spec.eType ?? "").ToUpperInvariant() : "";
            string fam = spec != null ? (spec.engineFamily ?? "").ToLowerInvariant() : "";
            c.pushrod = c.key == V8 && (e.Contains("OHV") || (e.Contains("(-)") && fam.Contains("american")));
            c.intercooler = turbo && c.key != F4;
            c.intercoolerTop = turbo && c.key == F4;
            c.electricFan = c.layout == EngineLayout.Transverse;
            return c;
        }

        /// <summary>The engine's words for the player: its SHAPE, never a family key or a catalog id.</summary>
        public static string ShapeWords(string key)
        {
            switch (key)
            {
                case I4: return "INLINE-4";
                case I6: return "INLINE-6";
                case V6: return "V6";
                case V8: return "V8";
                case F4: return "FLAT-4";
                case F6: return "FLAT-6";
                case R2: return "2-ROTOR";
            }
            return "ENGINE";
        }
    }
}
