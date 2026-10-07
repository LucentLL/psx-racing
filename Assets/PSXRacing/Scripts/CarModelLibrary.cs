using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Which body shell each of the 317 catalog cars wears.
    ///
    /// There are sixteen models and 317 cars, so most of this is deliberately
    /// approximate. The order of preference is the one a player would apply
    /// looking at a grid: the right car if the pack has it, otherwise the right
    /// KIND of car - same continent, same body style, same era, same rough size
    /// - and never a 1960s American coupe standing in for a kei hatchback.
    ///
    /// Two passes:
    ///   1. HandRules - the cars the pack actually modelled, their direct
    ///      siblings (a Superbird IS a winged Charger; a Cougar is a re-badged
    ///      Mustang), and the handful of calls worth making by hand because the
    ///      scorer gets them wrong on principle.
    ///   2. Score - everything else, on body class first, then region, era and
    ///      size. Body class leads because a Civic in a Charger shell reads as
    ///      broken, while a Civic in a French supermini shell only reads as a
    ///      substitution.
    ///
    /// Run Tools > PSX Racing > Dump Car Model Mapping to see every assignment.
    /// </summary>
    public static class CarModelLibrary
    {
        public enum Body { Hatch, Saloon, Estate, Coupe, Sports, GT, Muscle, Roadster, Pickup, Offroad, Van }
        public enum Region { Japan, America, Europe }

        public class Model
        {
            public string key, name;
            public Region region;
            public int year;
            public Body body;
            /// <summary>Kerb weight of the real car, as the size axis. Weight is
            /// a better proxy than length here because the catalog carries it
            /// per car - comparing a 834 kg Civic against a 1700 kg Charger
            /// separates them the way a player's eye does.</summary>
            public int kg;
            /// <summary>Worn only by the cars its hand rule names — never
            /// offered to the scorer. The owner's own models come in for a
            /// specific car; letting the scorer hand them to every car of the
            /// same body and era is a separate decision.</summary>
            public bool handOnly;
            /// <summary>The reference car's real width, mm, mirrors excluded -
            /// what a car with no catalog row (traffic, the starter FD, a
            /// parked car) is scaled across to. See WidthScale.</summary>
            public int widthMm;
        }

        /// <summary>
        /// The pack, identified. Every entry was matched to its real car from
        /// the reference art the author shipped alongside the meshes (period
        /// blueprints for the Americans and the R32, a Supra cutaway for the
        /// A80) or, for the European folder, from the shape and the factory
        /// colour names on the skins - "Signal Red" and "Ivory" on a 1960s
        /// hardtop roadster is a Pagoda.
        /// </summary>
        public static readonly Model[] Models =
        {
            new Model { key = "rx7_fd",       name = "Mazda RX-7 (FD)",           region = Region.Japan,   year = 1992, body = Body.Sports,   kg = 1280, widthMm = 1760 },
            new Model { key = "supra_a80",    name = "Toyota Supra (A80)",        region = Region.Japan,   year = 1993, body = Body.GT,       kg = 1510, widthMm = 1810 },
            new Model { key = "skyline_r32",  name = "Nissan Skyline GT-R (R32)", region = Region.Japan,   year = 1989, body = Body.Sports,   kg = 1480, widthMm = 1755 },
            new Model { key = "jdm_pickup",   name = "Compact pickup",            region = Region.Japan,   year = 1983, body = Body.Pickup,   kg = 1100, widthMm = 1650 },
            // The owner's own model (2026-09-25), sized to GT4's SiR-II sheet.
            new Model { key = "civic_eg",     name = "Honda Civic (EG)",          region = Region.Japan,   year = 1991, body = Body.Hatch,    kg = 1076, handOnly = true, widthMm = 1695 },
            new Model { key = "nissan_180sx", name = "Nissan 180SX / 240SX (S13)", region = Region.Japan,  year = 1989, body = Body.Sports,   kg = 1262, handOnly = true, widthMm = 1690 },
            // The owner's FD (2026-10-04): one body in its three spoilers,
            // pop-ups on all three, sized to GT4's FD sheet. The catalog's
            // FDs wear them by year (HandRules); the built-in rx7_fd above
            // stays the scene's own car and every other car's fallback.
            new Model { key = "rx7_fd_93",     name = "Mazda RX-7 (FD), 1993 spoiler", region = Region.Japan, year = 1993, body = Body.Sports, kg = 1260, handOnly = true, widthMm = 1760 },
            new Model { key = "rx7_fd_99",     name = "Mazda RX-7 (FD), 1999 wing",    region = Region.Japan, year = 1999, body = Body.Sports, kg = 1280, handOnly = true, widthMm = 1760 },
            new Model { key = "rx7_fd_nowing", name = "Mazda RX-7 (FD), no spoiler",   region = Region.Japan, year = 1993, body = Body.Sports, kg = 1260, handOnly = true, widthMm = 1760 },
            new Model { key = "viper_gts",    name = "Dodge Viper GTS",           region = Region.America, year = 1996, body = Body.Sports,   kg = 1532, handOnly = true, widthMm = 1923 },
            // The owner's FlatSix Coupe (2026-09-26): the 911 shape, for every
            // RUF (all three are 911s underneath) and any Porsche 911.
            new Model { key = "flatsix_coupe", name = "Flat-six coupe (911)",     region = Region.Europe,  year = 1987, body = Body.Sports,   kg = 1272, handOnly = true, widthMm = 1652 },
            // The owner's 2026-10-01 set, each sized to its GT4 sheet: the
            // 993-era FlatSix Turbo (RUF CTR2 `96), the Midship Coupe (every
            // NSX) and the Classic Roadster (every MX-5 Miata).
            new Model { key = "flatsix_turbo_96", name = "Flat-six turbo (993)", region = Region.Europe, year = 1996, body = Body.Sports,   kg = 1380, handOnly = true, widthMm = 1735 },
            new Model { key = "midship_coupe", name = "Midship coupe (NSX)",      region = Region.Japan,   year = 1990, body = Body.Sports,   kg = 1349, handOnly = true, widthMm = 1810 },
            new Model { key = "classic_roadster", name = "Classic roadster (Miata)", region = Region.Japan, year = 1989, body = Body.Roadster, kg = 1006, handOnly = true, widthMm = 1675 },
            // The owner's 2026-10-05 pop-up pair: the first-generation roadster
            // (1989-97, the fixed-lamp second generation stays on the Classic
            // roadster above) and the 1983 pop-up hatch. Both swap to a raised-
            // lamp body when the lights come on (CarLights.PopUps).
            new Model { key = "roadster_na_popup", name = "Classic roadster (pop-up)", region = Region.Japan, year = 1989, body = Body.Roadster, kg = 1006, handOnly = true, widthMm = 1675 },
            new Model { key = "hatch_83_popup", name = "Hatch 83 (pop-up)",      region = Region.Japan,   year = 1983, body = Body.Sports,   kg = 1006, handOnly = true, widthMm = 1625 },
            // The owner's 2026-10-06 Hatch 76: an unbadged 1976 GTI-style three-door,
            // sized to GT4's Golf I GTI `76 sheet (3705 mm, 1630 wide, 878 kg).
            new Model { key = "hatch_76",    name = "Hatch 76",                 region = Region.Europe,  year = 1976, body = Body.Hatch,    kg =  878, handOnly = true, widthMm = 1630 },
            // The owner's 2026-10-07 Rally Coupe 98 (v02): an unbadged 1998 widebody
            // turbo-4WD coupe, sized to GT4's `98 widebody coupe sheet (4365 mm, 1770 wide, 1269 kg).
            // Worn by every car of its family - saloons, wagon and coupes - each
            // fitted to its own sheet (the saloons are 1690 wide).
            new Model { key = "rally_coupe_98", name = "Rally Coupe 98",        region = Region.Japan,   year = 1998, body = Body.Coupe,    kg = 1269, handOnly = true, widthMm = 1770 },
            // The owner's 2026-10-02 set, each sized to its GT4 sheet: the
            // Liftback 95 (every Integra Type R DC2, Spoon's too), the Roadster
            // 99 (the S2000) and the Coupe 99 (the fifth-generation Preludes).
            new Model { key = "liftback_95", name = "Liftback (Integra DC2)",   region = Region.Japan,   year = 1995, body = Body.Sports,   kg = 1096, handOnly = true, widthMm = 1695 },
            new Model { key = "roadster_99", name = "Roadster 99 (S2000)",      region = Region.Japan,   year = 1999, body = Body.Roadster, kg = 1284, handOnly = true, widthMm = 1750 },
            new Model { key = "coupe_99",    name = "Coupe 99 (Prelude)",       region = Region.Japan,   year = 1996, body = Body.Sports,   kg = 1260, handOnly = true, widthMm = 1750 },

            new Model { key = "gto_66",       name = "Pontiac GTO '66",           region = Region.America, year = 1966, body = Body.Muscle,   kg = 1650, widthMm = 1880 },
            new Model { key = "mustang_67",   name = "Ford Mustang Fastback '67", region = Region.America, year = 1967, body = Body.Muscle,   kg = 1400, widthMm = 1811 },
            new Model { key = "charger_69",   name = "Dodge Charger '69",         region = Region.America, year = 1969, body = Body.Muscle,   kg = 1700, widthMm = 1951 },
            new Model { key = "daytona_69",   name = "Charger Daytona '69",       region = Region.America, year = 1969, body = Body.Muscle,   kg = 1750, widthMm = 1951 },

            new Model { key = "bmw_e30",      name = "BMW 3-Series (E30)",        region = Region.Europe,  year = 1985, body = Body.Saloon,   kg = 1150, widthMm = 1645 },
            new Model { key = "audi_saloon",  name = "Audi 80/100",               region = Region.Europe,  year = 1986, body = Body.Saloon,   kg = 1220, widthMm = 1700 },
            new Model { key = "euro_hatch",   name = "European supermini",        region = Region.Europe,  year = 1983, body = Body.Hatch,    kg =  850, widthMm = 1572 },
            new Model { key = "volvo_estate", name = "Volvo 240 Estate",          region = Region.Europe,  year = 1985, body = Body.Estate,   kg = 1350, widthMm = 1715 },
            new Model { key = "citroen_cx",   name = "Citroen CX",                region = Region.Europe,  year = 1980, body = Body.Saloon,   kg = 1320, widthMm = 1730 },
            new Model { key = "mb_pagoda",    name = "Mercedes-Benz SL 'Pagoda'", region = Region.Europe,  year = 1965, body = Body.Roadster, kg = 1350, widthMm = 1760 },
            new Model { key = "landrover",    name = "Land Rover pickup",         region = Region.Europe,  year = 1985, body = Body.Offroad,  kg = 1900, widthMm = 1790 },
            new Model { key = "classic_van",  name = "Classic panel van",         region = Region.Europe,  year = 1960, body = Body.Van,      kg = 1400, widthMm = 1750 },

            // Ripped PS1-era cars (GT1/GT2 via the owner's Cars folder), added
            // 2026-09-25 as parked TRAFFIC on the race tracks. Hand-only so the
            // scorer never hands a catalog car a police-spec Crown Vic.
            new Model { key = "crown_victoria", name = "Ford Crown Victoria", region = Region.America, year = 1998, body = Body.Saloon, kg = 1790, handOnly = true, widthMm = 1987 },
            new Model { key = "camry_2001",     name = "Toyota Camry (XV20)", region = Region.Japan,   year = 1997, body = Body.Saloon, kg = 1400, handOnly = true, widthMm = 1785 },
            new Model { key = "ford_transit",   name = "Ford Transit (Mk5)",  region = Region.Europe,  year = 1994, body = Body.Van,    kg = 1900, handOnly = true, widthMm = 1938 },
        };

        static Dictionary<string, Model> byKey;
        public static Model Get(string key)
        {
            if (byKey == null)
            {
                byKey = new Dictionary<string, Model>();
                foreach (var m in Models) byKey[m.key] = m;
            }
            return key != null && byKey.TryGetValue(key, out var v) ? v : null;
        }

        public const string Default = "rx7_fd";

        // ------------------------------------------------------------------
        //  Real width
        // ------------------------------------------------------------------
        /// <summary>
        /// "I want all cars to be realistic width" (owner, 2026-09-27). The
        /// pack drew its shells chunky - most stand 10-18% wider than the car
        /// they are of - and one shell dresses many cars. So a car's shell is
        /// scaled ACROSS to the real width: its own catalog row's (GT4's `wid`,
        /// CarSpec.widthMm) or, with no row, its model's reference car
        /// (Model.widthMm). Clamped: a stretch past these reads as a different
        /// car, not a narrower one.
        /// </summary>
        public static float WidthScale(CarModelDef def, int widthMm)
        {
            if (def == null) return 1f;
            if (widthMm <= 0) { var m = Get(def.key); widthMm = m != null ? m.widthMm : 0; }
            float body = BodyWidth(def);
            if (widthMm <= 0 || body < 0.5f) return 1f;
            return Mathf.Clamp(widthMm / 1000f / body, WidthScaleMin, WidthScaleMax);
        }

        /// <summary>
        /// A shell's body width in the car's frame, the way a spec sheet gives
        /// it: across the LOWER body (bumpers, arches, sills - the bottom 55% of
        /// its height), so mirrors at the window line are not counted. Cached
        /// per mesh; the bounds when the mesh cannot be read.
        /// </summary>
        public static float BodyWidth(CarModelDef def)
        {
            if (def == null || def.bodyMesh == null) return 0f;
            // Baked (CarModelBaker): the pack imports its OBJs unreadable, so
            // the runtime path below only ever saw the bounds, mirrors and all.
            if (def.lowerBodyWidth > 0.5f) return def.lowerBodyWidth;
            if (bodyWidths.TryGetValue(def.bodyMesh, out float w)) return w;
            w = MeasureLowerWidth(def.bodyMesh, def.bodyYaw, def.bodyMesh.isReadable);
            bodyWidths[def.bodyMesh] = w;
            return w;
        }

        /// <summary>The lower-body width of a mesh turned by
        /// <paramref name="yaw"/>; the bounds when it may not be read.</summary>
        public static float MeasureLowerWidth(Mesh mesh, float yaw, bool canRead)
        {
            float w;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var v = canRead ? mesh.vertices : null;
            if (v == null || v.Length == 0)
            {
                var e = mesh.bounds.extents;
                w = 2f * (Mathf.Abs((rot * new Vector3(e.x, 0f, 0f)).x) + Mathf.Abs((rot * new Vector3(0f, 0f, e.z)).x));
            }
            else
            {
                float minY = float.MaxValue, maxY = float.MinValue;
                foreach (var p in v) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
                float cut = minY + (maxY - minY) * 0.55f;
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var p in v)
                {
                    if (p.y > cut) continue;
                    float x = (rot * p).x;
                    lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x);
                }
                w = hi > lo ? hi - lo : mesh.bounds.size.x;
            }
            return w;
        }
        static readonly Dictionary<Mesh, float> bodyWidths = new Dictionary<Mesh, float>();

        // ------------------------------------------------------------------
        //  TO SPEC: one fit every assembler reads
        // ------------------------------------------------------------------
        // Owner, 2026-10-07: "The 3D models should match the wheelbases.
        // Everything should be designed to spec and scale." And: "If we need
        // to add camber and toe to get wheels to fit, so be it."
        //
        // The chassis is built to the SPEC SHEET (wheelbase, each axle's track
        // and tyre); the shell is stretched to it (across to the real width,
        // along until its axles land on the real wheelbase); and each tyre is
        // drawn at the sheet's diameter and section width. Where the real tyre
        // would stand out past the wheel the model drew, it is cambered (top
        // in) just enough to tuck its top edge back under the arch.
        //
        // Driven cars (CarBody), parked scenery (the builder's DressProp), the
        // garage turntable, the on-foot shell, the X-ray and the preview
        // renders all ask THIS, so a car cannot be one size on the road and
        // another in the garage. A car with no sheet row - the starter FD,
        // traffic, a parked prop - gets exactly the fit it had before.

        /// <summary>Same fence as the across-scale: a shell stretched further
        /// than this reads as a different car, not a longer one.</summary>
        public const float LengthScaleMin = 0.7f, LengthScaleMax = 1.2f;
        public const float WidthScaleMin = 0.7f, WidthScaleMax = 1.2f;
        /// <summary>The most negative camber the fit will draw, degrees.</summary>
        public const float CamberCapDeg = 8f;
        /// <summary>How far a spec tyre may stand past the model's own wheel
        /// before it is cambered in, metres.</summary>
        public const float PokeTolerance = 0.004f;

        /// <summary>The fitted car, in the rig's frame (+Z forward, the axle
        /// midpoint at the origin, wheel hubs at the radius).</summary>
        public struct ShellFit
        {
            /// <summary>Any dimension came off the spec sheet.</summary>
            public bool toSpec;
            /// <summary>Body scale across / along the car, and whether either
            /// hit its fence (then the wheels cannot all be in the arches).</summary>
            public float sx, sz;
            public bool clampX, clampZ;
            /// <summary>The body root's local position. <see cref="dy"/> is the
            /// lift against the baked bodyYOffset: every measurement the baker
            /// took in the shell's frame (cowl, roof, lamps, collider) moves by
            /// it, and its Z and X by <see cref="sz"/> and <see cref="sx"/> -
            /// see <see cref="P"/>.</summary>
            public float bodyY, bodyZ, dy;
            public float wheelbase, trackF, trackR;
            /// <summary>Each axle's tyre radius as simulated and drawn, and the
            /// driven axle's (gearing, top speed, RPM).</summary>
            public float radiusF, radiusR, driveRadius;
            /// <summary>The wheel holder's local scale per axle: x = section
            /// width, y/z = diameter, against the front wheel mesh.</summary>
            public Vector3 wheelScaleF, wheelScaleR;
            /// <summary>Visual camber per axle, degrees, top IN.</summary>
            public float camberF, camberR;
            /// <summary>How far each axle's tyre still stands past the model's
            /// own wheel after the camber, metres (&lt;= 0: tucked).</summary>
            public float pokeF, pokeR;

            /// <summary>A tyre still stands out past the model's own wheel
            /// after the capped camber: a model edit, not a fit.</summary>
            public bool Pokes => pokeF > PokeTolerance + 5e-4f || pokeR > PokeTolerance + 5e-4f;

            public float Track(int i) => i < 2 ? trackF : trackR;
            public float Radius(int i) => i < 2 ? radiusF : radiusR;
            public float Camber(int i) => i < 2 ? camberF : camberR;
            public Vector3 WheelScale(int i) => i < 2 ? wheelScaleF : wheelScaleR;

            /// <summary>A point the baker measured in the shell's frame (cowl,
            /// lamp, collider centre) where the fitted body puts it.</summary>
            public Vector3 P(Vector3 shell) => new Vector3(shell.x * sx, shell.y + dy, shell.z * sz);

            /// <summary>Wheel <paramref name="i"/>'s hub at rest (0 FL, 1 FR,
            /// 2 RL, 3 RR), rig frame, contact patch on y = 0.</summary>
            public Vector3 Hub(int i) => new Vector3((i % 2 == 0 ? -0.5f : 0.5f) * Track(i), Radius(i),
                                                     (i < 2 ? 0.5f : -0.5f) * wheelbase);

            /// <summary>The wheel holder under a hub: the left-hand flip, the
            /// camber, and the slide that makes the camber pivot on the contact
            /// patch (the track is measured at the ground, so the patch stays
            /// on it and the top leans in).</summary>
            public void Holder(int i, out Vector3 localPos, out Quaternion localRot)
            {
                bool left = i % 2 == 0;
                float c = Camber(i) * Mathf.Deg2Rad, r = Radius(i);
                localRot = Quaternion.Euler(0f, left ? 180f : 0f, 0f) * Quaternion.Euler(0f, 0f, Camber(i));
                localPos = new Vector3((left ? 1f : -1f) * r * Mathf.Sin(c), -r * (1f - Mathf.Cos(c)), 0f);
            }

            /// <summary>Put a wheel holder where this fit wants it.</summary>
            public void PlaceHolder(Transform holder, int i)
            {
                if (holder == null) return;
                Holder(i, out var p, out var q);
                holder.localPosition = p;
                holder.localRotation = q;
                holder.localScale = WheelScale(i);
            }

            /// <summary>A wheel with no hub above it (a parked prop, the
            /// turntable, a preview render): hub, flip, camber and tyre size
            /// all on the one transform, slid by <paramref name="shift"/>.</summary>
            public void PlaceWheel(Transform t, int i, Vector3 shift = default)
            {
                if (t == null) return;
                Holder(i, out var p, out var q);
                t.localPosition = Hub(i) + p + shift;
                t.localRotation = q;
                t.localScale = WheelScale(i);
            }

            /// <summary>The body transform: yaw, the fitted offsets, the stretch.</summary>
            public void PlaceBody(Transform t, CarModelDef def, Vector3 shift = default)
            {
                if (t == null || def == null) return;
                t.localPosition = new Vector3(0f, bodyY, bodyZ) + shift;
                t.localRotation = Quaternion.Euler(0f, def.bodyYaw, 0f);
                t.localScale = BodyScale(def.bodyYaw);
            }

            /// <summary>The body root's local scale: across and along the CAR,
            /// whichever of the body's own axes those are after its yaw.</summary>
            public Vector3 BodyScale(float bodyYaw)
            {
                float s = Mathf.Abs(Mathf.Sin(bodyYaw * Mathf.Deg2Rad));
                return s > 0.7f ? new Vector3(sz, 1f, sx) : new Vector3(sx, 1f, sz);
            }
        }

        /// <summary>The sheet's numbers for one car, mm (0 = not on the sheet).
        /// Serializable so a car keeps them across a respray.</summary>
        [System.Serializable]
        public struct SpecGeometry
        {
            public int widthMm, wheelbaseMm, trackFMm, trackRMm;
            public int tyreFDiaMm, tyreRDiaMm, tyreFWidthMm, tyreRWidthMm;
            public string drv;

            public static SpecGeometry Of(CarSpec s) => s == null ? default : new SpecGeometry
            {
                widthMm = s.widthMm, wheelbaseMm = s.wheelbaseMm, trackFMm = s.trackFMm, trackRMm = s.trackRMm,
                tyreFDiaMm = s.tyreFDiaMm, tyreRDiaMm = s.tyreRDiaMm,
                tyreFWidthMm = s.tyreFWidthMm, tyreRWidthMm = s.tyreRWidthMm, drv = s.drv,
            };
            public static SpecGeometry WidthOnly(int widthMm) => new SpecGeometry { widthMm = widthMm };
        }

        public static ShellFit Fit(CarModelDef def, CarSpec spec) => Fit(def, SpecGeometry.Of(spec));

        /// <summary>THE fit. See the block comment above.</summary>
        public static ShellFit Fit(CarModelDef def, SpecGeometry g)
        {
            var f = new ShellFit { sx = 1f, sz = 1f, wheelbase = 2.425f, trackF = 1.46f, trackR = 1.46f,
                                   radiusF = 0.31f, radiusR = 0.31f, driveRadius = 0.31f,
                                   wheelScaleF = Vector3.one * 0.93f, wheelScaleR = Vector3.one * 0.93f };
            if (def == null) return f;

            // Across: the real width (the reference car's with no row).
            int wmm = g.widthMm;
            if (wmm <= 0) { var m = Get(def.key); wmm = m != null ? m.widthMm : 0; }
            float body = BodyWidth(def);
            float wantX = wmm > 0 && body >= 0.5f ? wmm / 1000f / body : 1f;
            f.sx = Mathf.Clamp(wantX, WidthScaleMin, WidthScaleMax);
            f.clampX = Mathf.Abs(f.sx - wantX) > 1e-4f;

            // Along: the model's axles onto the real wheelbase.
            float mWb = Mathf.Max(def.wheelbase, 0.5f);
            f.wheelbase = g.wheelbaseMm > 0 ? g.wheelbaseMm / 1000f : def.wheelbase;
            float wantZ = f.wheelbase / mWb;
            f.sz = Mathf.Clamp(wantZ, LengthScaleMin, LengthScaleMax);
            f.clampZ = Mathf.Abs(f.sz - wantZ) > 1e-4f;

            // The model's own wheels, as drawn (no 0.93), per axle.
            Bounds wb = def.wheelMesh != null ? def.wheelMesh.bounds
                : new Bounds(Vector3.zero, new Vector3(0.2f, 0.666f, 0.666f));
            float meshR = Mathf.Max(Mathf.Max(wb.size.y, wb.size.z) * 0.5f, 0.05f);
            float meshW = Mathf.Max(wb.size.x, 0.05f);
            float mTrackF = def.trackWidth;
            float mTrackR = def.trackRear > 0.1f ? def.trackRear : mTrackF;
            float mWidthR = def.tyreWidthRear > 0.05f ? def.tyreWidthRear : meshW;

            // The chassis: the sheet's track and tyres, else today's.
            f.trackF = g.trackFMm > 0 ? g.trackFMm / 1000f : def.trackWidth * f.sx;
            f.trackR = g.trackRMm > 0 ? g.trackRMm / 1000f : (g.trackFMm > 0 ? f.trackF : def.trackWidth * f.sx);
            f.radiusF = g.tyreFDiaMm > 0 ? g.tyreFDiaMm / 2000f : def.wheelRadius;
            f.radiusR = g.tyreRDiaMm > 0 ? g.tyreRDiaMm / 2000f : f.radiusF;
            f.driveRadius = g.drv == "FF" ? f.radiusF
                          : g.drv == "4WD" ? (f.radiusF + f.radiusR) * 0.5f
                          : f.radiusR;
            f.toSpec = g.wheelbaseMm > 0 || g.trackFMm > 0 || g.tyreFDiaMm > 0;

            // The tyres: the front mesh at every corner, scaled to each axle's
            // diameter and section width. No sheet tyre = the old uniform 0.93.
            f.wheelScaleF = TyreScale(f.radiusF, g.tyreFWidthMm, meshR, meshW, def.wheelMeshScale, g.tyreFDiaMm > 0);
            f.wheelScaleR = TyreScale(f.radiusR, g.tyreRWidthMm > 0 ? g.tyreRWidthMm : g.tyreFWidthMm,
                                      meshR, meshW, def.wheelMeshScale, g.tyreRDiaMm > 0 || g.tyreFDiaMm > 0);

            // The body rides with its hub on the (mean) tyre radius, as the
            // baker set it for its own tyre, and slides with the stretch so its
            // axle midpoint stays on the rig's origin.
            f.bodyY = def.bodyYOffset + ((f.radiusF + f.radiusR) * 0.5f - def.wheelRadius);
            f.dy = f.bodyY - def.bodyYOffset;
            f.bodyZ = def.bodyZOffset * f.sz;

            // Camber: only where the sheet moved the tyre out past the model's.
            float drawnWF = f.wheelScaleF.x * meshW, drawnWR = f.wheelScaleR.x * meshW;
            float limitF = f.sx * (mTrackF + meshW) * 0.5f, limitR = f.sx * (mTrackR + mWidthR) * 0.5f;
            if (f.toSpec)
            {
                Tuck(f.trackF, drawnWF, f.radiusF, limitF, out f.camberF, out f.pokeF);
                Tuck(f.trackR, drawnWR, f.radiusR, limitR, out f.camberR, out f.pokeR);
            }
            else
            {
                f.pokeF = (f.trackF + drawnWF) * 0.5f - limitF;
                f.pokeR = (f.trackR + drawnWR) * 0.5f - limitR;
            }
            ReportFence(def, f, wantX, wantZ);
            return f;
        }

        static Vector3 TyreScale(float radius, int widthMm, float meshR, float meshW, float legacy, bool spec)
        {
            if (!spec) return Vector3.one * legacy;
            float d = radius / meshR;
            float w = widthMm > 0 ? widthMm / 1000f / meshW : d;
            return new Vector3(w, d, d);
        }

        /// <summary>
        /// The least camber (top in, about the contact patch, capped) that
        /// brings a tyre's top outer edge back inside <paramref name="limit"/>
        /// - the outer face of the wheel the model drew, after the stretch.
        /// About the patch the top outer corner sits at (w/2, 2r), so it moves
        /// to x = a cos c - b sin c with a = w/2, b = 2r: solve that = room.
        /// </summary>
        static void Tuck(float track, float width, float r, float limit, out float camberDeg, out float poke)
        {
            float a = width * 0.5f, b = 2f * r;
            float upright = track * 0.5f + a - limit;
            camberDeg = 0f;
            poke = upright;
            if (upright <= PokeTolerance) return;
            float room = limit - track * 0.5f;
            float h = Mathf.Sqrt(a * a + b * b);
            float c = Mathf.Acos(Mathf.Clamp(room / h, -1f, 1f)) - Mathf.Atan2(b, a);
            camberDeg = Mathf.Clamp(c * Mathf.Rad2Deg, 0f, CamberCapDeg);
            float cr = camberDeg * Mathf.Deg2Rad;
            poke = track * 0.5f + a * Mathf.Cos(cr) - b * Mathf.Sin(cr) - limit;
        }

        static readonly HashSet<string> fenceLogged = new HashSet<string>();
        static void ReportFence(CarModelDef def, ShellFit f, float wantX, float wantZ)
        {
            if (!f.clampX && !f.clampZ) return;
            string k = def.key + "|" + wantX.ToString("0.000") + "|" + wantZ.ToString("0.000");
            if (!fenceLogged.Add(k)) return;
            Debug.LogWarning($"[CarFit] {def.key}: stretch fenced - across {wantX:0.000} -> {f.sx:0.000}, " +
                             $"along {wantZ:0.000} -> {f.sz:0.000}; the wheels cannot all sit in its arches");
        }

        // ------------------------------------------------------------------
        //  Pass 1: hand-mapped
        // ------------------------------------------------------------------
        // Ordered; first match wins. Everything here is either a car the pack
        // literally modelled, a badge-engineered twin of one, or a call the
        // scorer would get wrong for a structural reason noted alongside it.
        static readonly (string pattern, string key)[] HandRules =
        {
            // 2026-10-06, the owner's Car Models Tracker remap (approved "all look good"):
            // 38 cars moved onto the existing shell closest to the real car. Exact names,
            // first so they win; review page https://claude.ai/artifact/MvczJsa8sF44Jj2YkECfAw
            (@"^Alfa\ Romeo\ Giulia\ Sprint\ GTA\ 1600\ `65$", "bmw_e30"),
            (@"^Chevrolet\ Camaro\ IROC\-Z\ Concept\ `88$", "citroen_cx"),
            (@"^Chevrolet\ Camaro\ Z28\ Coupe\ `97$", "supra_a80"),
            (@"^Chevrolet\ Corvette\ Convertible\ \(C1\)\ `54$", "mb_pagoda"),
            (@"^Chevrolet\ Corvette\ GRAND\ SPORT\ \(C4\)\ `96$", "rx7_fd"),
            (@"^Chevrolet\ Corvette\ ZR\-1\ \(C4\)\ `90$", "rx7_fd"),
            (@"^Honda\ 1300\ Coupe\ 9\ S\ `70$", "bmw_e30"),
            (@"^Honda\ ACCORD\ Coupe\ `88$", "citroen_cx"),
            (@"^Honda\ CIVIC\ TYPE\ R\ \(EK\)\ `97$", "civic_eg"),
            (@"^Honda\ CIVIC\ TYPE\ R\ \(EK\)\ `98$", "civic_eg"),
            (@"^Honda\ CR\-X\ del\ Sol\ SiR\ `92$", "civic_eg"),
            (@"^Honda\ Gathers\ Drider\ CIVIC\ Race\ Car\ `98$", "civic_eg"),
            (@"^Honda\ S800\ RSC\ Race\ Car\ `68$", "mb_pagoda"),
            (@"^Isuzu\ 117\ Coupe\ `68$", "gto_66"),
            (@"^Isuzu\ Bellett\ 1600\ GT\-R\ `69$", "bmw_e30"),
            (@"^Isuzu\ PIAZZA\ XE\ `81$", "citroen_cx"),
            (@"^Jensen\ Interceptor\ MkIII\ `74$", "mustang_67"),
            (@"^Lancia\ STRATOS\ Rally\ Car\ `77$", "mb_pagoda"),
            (@"^Mazda\ 110S\ \(L10A\)\ `67$", "mb_pagoda"),
            (@"^Mazda\ 110S\ \(L10B\)\ `68$", "mb_pagoda"),
            (@"^Mitsubishi\ GALANT\ GTO\ MR\ `70$", "mustang_67"),
            (@"^Mitsubishi\ Lancer\ 1600\ GSR\ Rally\ Car\ `74$", "bmw_e30"),
            (@"^Mitsubishi\ Lancer\ 1600\ GSR\ `73$", "bmw_e30"),
            (@"^Mitsubishi\ Lancer\ EX\ 1800GSR\ IC\ Turbo\ `83$", "audi_saloon"),
            (@"^Nissan\ 240RS\ Rally\ Car\ `85$", "citroen_cx"),
            (@"^Nissan\ 240ZG\ \(HS30\)\ `71$", "mustang_67"),
            (@"^Nissan\ BLUEBIRD\ Rally\ Car\ \(510\)\ `69$", "bmw_e30"),
            (@"^Nissan\ Fairlady\ Z\ 280Z\-L\ 2seater\ \(S130\)\ `78$", "mustang_67"),
            (@"^Nissan\ SILVIA\ 240RS\ \(S100\)\ `83$", "citroen_cx"),
            (@"^Nissan\ SILVIA\ K's\ \(S13\)\ `88$", "nissan_180sx"),
            (@"^Nissan\ SILVIA\ K's\ \(S13\)\ `91$", "nissan_180sx"),
            (@"^Nissan\ SILVIA\ Q's\ \(S13\)\ `88$", "nissan_180sx"),
            (@"^Nissan\ SILVIA\ Q's\ \(S13\)\ `91$", "nissan_180sx"),
            (@"^SILEIGHTY\ `98$", "nissan_180sx"),
            (@"^TVR\ Griffith\ 500\ `94$", "mb_pagoda"),
            (@"^Toyota\ 2000GT\ `67$", "mb_pagoda"),
            (@"^Toyota\ CELICA\ 1600GT\ \(TA22\)\ `70$", "gto_66"),
            (@"^Toyota\ MR2\ Spyder\ `99$", "mb_pagoda"),
            // 2026-10-07: the owner's Rally Coupe 98 dresses its whole family - the
            // GC saloons, the GF wagon, the coupes, the rally car and the widebody
            // `98 - so it goes ahead of the wagon rule (the GF wagon was on the
            // Volvo) and the R32 rule below (the rest were on the R32).
            (@"Subaru IMPREZA",                                 "rally_coupe_98"),
            // Wagons before anything else: a Legacy Touring Wagon is an estate
            // before it is a turbo saloon, and a Stagea is a Skyline that grew a
            // tailgate. The Volvo IS the catalog's estate.
            ("Estate|Touring Wagon|Sport Wagon|STAGEA",          "volvo_estate"),

            // --- Japan ---
            // Tuner one-offs the catalog files under "eur" although they are a
            // Silvia and an Integra. These go first because the broad NISMO rule
            // below would otherwise swallow the 270R.
            ("SILEIGHTY|NISMO 270R",                             "rx7_fd"),
            // The EG hatch, every year GT4 sells it.
            (@"CIVIC SiR-II \(EG\)",                            "civic_eg"),
            // The S13 hatch. GT4 files it as "240SX `96"; the S14 has its own
            // row, "240SX (S14)", and its own body, so the backtick keeps it out.
            ("Nissan 240SX `",                                   "nissan_180sx"),
            // The owner's Midship Coupe and Classic Roadster (2026-10-01):
            // every NSX, Honda or Acura, and every MX-5 Miata (the one NB too -
            // it is far nearer this than any other shell the game has).
            ("Honda NSX|Acura NSX",                              "midship_coupe"),
            // 2026-10-05: the first generation's pop-up-lamp rows (every "(NA"
            // row) take the owner's pop-up roadster; the rest, the NB, keep the
            // Classic Roadster. The 1983 pop-up hatch is the catalog's one
            // pop-up variant of that car; its fixed-lamp twin stays scored.
            (@"Miata.*\(NA\b",                                  "roadster_na_popup"),
            (@"SPRINTER TRUENO GT-APEX \(AE86\)",              "hatch_83_popup"),
            // 2026-10-06, the owner's Car Models Tracker: the fixed-lamp twin
            // reads closest as the boxy 80s liftback (the shell in his sheet's
            // F5), not the RX-7 it was scored onto.
            (@"COROLLA LEVIN GT-APEX \(AE86\)",                "citroen_cx"),
            // 2026-10-06: the owner's Hatch 76 is the catalog's 1976 GTI, ahead of
            // the European hatch rule below that takes every other Golf.
            (@"Golf I GTI `76",                                 "hatch_76"),
            ("Mazda MX-5|Miata|Eunos Roadster",                  "classic_roadster"),
            // And the 2026-10-02 set: every Integra Type R DC2 (Spoon's was on
            // the European hatch until there was an Integra to wear), the
            // S2000, and the fifth-generation Prelude (`96-`98: SiR, Type S,
            // SiR S spec - the `91 Si VTEC is the pop-up third generation and
            // keeps the scorer's pick).
            (@"INTEGRA TYPE R \(DC2\)",                        "liftback_95"),
            ("Honda S2000",                                      "roadster_99"),
            (@"Honda PRELUDE (SiR|Type S)",                      "coupe_99"),
            // The owner's FD (2026-10-04), by spoiler year: the `98 Type RS is
            // the 280 PS series-5 car with the 1999 adjustable wing; every FD
            // before it has the 1993 hoop spoiler. (No catalog FD is wingless;
            // rx7_fd_nowing waits for one.)
            (@"Mazda RX-7 .*\(FD.*`9[89]",                     "rx7_fd_99"),
            (@"Mazda RX-7 .*\(FD",                              "rx7_fd_93"),
            // The Cosmo Sport rides along with the rotary it started.
            ("Mazda RX-7|Mazda 110S",                            "rx7_fd"),
            // Every Skyline shares the shell family, the works cars are Skylines
            // with stickers, and the Lexus saloons are the same size and decade.
            ("SKYLINE|CALSONIC|PENNZOIL|NISMO|Lexus (IS|GS)",    "skyline_r32"),
            // The R32 is THE Japanese turbo-4WD performance saloon of the era, so
            // its rivals wear it rather than being scored into a European shell
            // on the strength of having four doors.
            ("Lancer Evolution|Galant.*VR-4|LEGNUM|LEGACY B4",   "skyline_r32"),
            // Celica XX is the Supra's own name in Japan; the 3000GT, the Z32
            // and the Soarer are the same long-nose turbo GT coupe idea.
            ("Toyota SUPRA|Toyota CELICA XX|3000GT|300ZX|Lexus SC", "supra_a80"),

            // --- America ---
            ("Pontiac Tempest Le Mans GTO",                      "gto_66"),
            // Shelby's car IS this fastback; the Cougar is its Mercury twin.
            ("Shelby Mustang|Mercury Cougar",                    "mustang_67"),
            // The Superbird is the Daytona's Plymouth sister - same nose, same wing.
            ("Plymouth Super Bird",                              "daytona_69"),
            ("Dodge Charger|Plymouth Cuda|Chevrolet Chevelle",   "charger_69"),
            // The pack has no post-1970 American shell, so left to the scorer a
            // C4 Corvette lands in an RX-7 on shape alone. An American V8 two-
            // seater belongs in an American V8 two-seater whatever the decade.
            // The owner's own Viper GTS (2026-09-25).
            ("Dodge VIPER",                                      "viper_gts"),
            ("Chevrolet Corvette|Ford GT40|Chaparral",           "mustang_67"),
            ("Chevrolet Camaro|BUICK",                           "gto_66"),

            // --- Europe ---
            ("Volvo 240",                                        "volvo_estate"),
            // The SL line, from the 300 SL the Pagoda replaced to the R129.
            ("Mercedes-Benz (300 SL|SL |SLK)",                   "mb_pagoda"),
            // Every RUF is a 911 underneath - the BTR, the CTR "Yellow Bird",
            // the CTR2 - and wears the owner's FlatSix Coupe (2026-09-26); so
            // does any Porsche 911 the catalog ever carries. (They borrowed
            // the E30 while the pack had no rear-engined shell.)
            // The CTR2 `96 is a 993 and wears the owner's FlatSix Turbo 96
            // (2026-10-01); first, so the rule below cannot take it.
            ("RUF CTR2",                                         "flatsix_turbo_96"),
            (@"RUF |Porsche 911|Porsche.*911|\b911\b",       "flatsix_coupe"),
            // E30-class German compact saloons: the 2002 is its ancestor, the
            // 190 E its period rival, and the DTM cars are those two.
            ("BMW 2002|BMW M Coupe|Mercedes-Benz 190 E|Mercedes 190 E", "bmw_e30"),
            ("Audi quattro|Audi S4|Opel Calibra|Lotus Carlton",  "audi_saloon"),
            ("Volkswagen Golf|Peugeot 20[56]|Renault 5|Citroen Xsara|Opel Tigra|Mercedes-Benz A 160|Ford (Escort|FOCUS)", "euro_hatch"),
            ("Peugeot 406|Alfa Romeo 1[556][56]",                "citroen_cx"),
            // The catalog's only off-roaders, and the only thing the Land Rover
            // shell can honestly be.
            ("PAJERO|ESCUDO",                                    "landrover"),
        };

        static Regex[] hand;

        // ------------------------------------------------------------------
        //  Pass 2: body class, inferred from the catalog entry
        // ------------------------------------------------------------------
        static readonly (string pattern, Body body)[] BodyRules =
        {
            ("Estate|Wagon|STAGEA",                                                     Body.Estate),
            ("PAJERO|ESCUDO|Rally Raid|Dirt Trial",                                     Body.Offroad),
            // Open cars. Wide, because "roadster" is spelled a dozen ways here:
            // Miata, Spider, Duetto, Barchetta, Spyder, Convertible, plus the
            // British mid-engined two-seaters that read the same on a grid.
            ("Convertible|Spider|Spyder|Duetto|Miata|MX-5|Elise|Europa|Barchetta|" +
             "S2000|Cobra|427 S/C|MGF|Fairlady 2000|Alpine A1|Roadster|Boxster",        Body.Roadster),
            // Superminis and three-door hatches, whatever continent they are from.
            // "Peugeot 20[56]" is spelled out rather than left as a bare number:
            // a loose 205 also matches the Celica's ST205 chassis code.
            ("3door|CIVIC|STARLET|DEMIO|MIRAGE|CR-X|del Sol|CITY Turbo|BALLADE|" +
             "Golf|Peugeot 20[56]|Renault 5|Xsara|Tigra|A 160|SERA|323F|DELTA|COROLLA Rally", Body.Hatch),
            // Four-door bodies. Rally cars built on them stay saloons - a Lancer
            // Evolution is a saloon with a wing, not a coupe.
            ("Sedan|Saloon|Lancer 1600|Lancer EX|CARINA|BLUEBIRD|G20|GS300|IS200|" +
             "Taurus|156|166|155|406",                                                  Body.Saloon),
            // Purpose-built prototypes and exotica: nothing in the pack is one of
            // these, but they read as long low GTs rather than as saloons.
            ("Esprit|XJ220|Diablo|Cizeta|NSX|Aston Martin|XKR|E-Type|" +
             "Jensen|Interceptor|Griffith|Cerbera|V8S|Storm|Esperante|" +
             "XJR-9|787B|R39[02]|R89C|R92CP|88C-V|GT-ONE|905|C 9|CLK-GTR|" +
             "McLaren|LMR|DOME|Hommell|Panoz|Toyota 7|2000GT|110S",                     Body.GT),
        };

        static Regex[] bodyRe;

        /// <summary>
        /// Body style of a catalog car. Runs the table above first, then falls
        /// back on the numbers: an early American pushrod V8 is muscle, a small
        /// light front-driver is a hatchback, and anything else is a coupe.
        /// </summary>
        public static Body BodyOf(CarSpec c)
        {
            if (bodyRe == null)
            {
                bodyRe = new Regex[BodyRules.Length];
                for (int i = 0; i < BodyRules.Length; i++)
                    bodyRe[i] = new Regex(BodyRules[i].pattern, RegexOptions.IgnoreCase);
            }
            for (int i = 0; i < bodyRe.Length; i++)
                if (bodyRe[i].IsMatch(c.name)) return BodyRules[i].body;

            bool bigOldV8 = c.modelYear <= 1975 && c.dispCc >= 4000 &&
                            !string.IsNullOrEmpty(c.eType) && c.eType.StartsWith("V8");
            if (bigOldV8) return Body.Muscle;
            if (c.origin == "usa" && c.modelYear <= 1975) return Body.Muscle;
            // A light front-driver under two litres is an economy hatch whatever
            // the badge says - this is what catches the Civics, Starlets and
            // Miratges the name table missed.
            if (c.drv == "FF" && c.kg <= 1150 && c.dispCc > 0 && c.dispCc <= 1800) return Body.Hatch;
            return Body.Coupe;
        }

        public static Region RegionOf(CarSpec c)
        {
            switch (c.origin)
            {
                case "jpn": return Region.Japan;
                case "usa": return Region.America;
                default: return Region.Europe;
            }
        }

        /// <summary>
        /// How well one shell suits another body style. Not symmetric in spirit:
        /// the question is always "would putting this car in that shell look
        /// like a substitution or like a bug".
        /// </summary>
        static float BodyAffinity(Body car, Body model)
        {
            if (car == model) return 1f;
            switch (car)
            {
                case Body.Sports:   return model == Body.GT ? 0.85f : model == Body.Coupe ? 0.8f : model == Body.Muscle ? 0.35f : model == Body.Saloon ? 0.3f : 0.1f;
                case Body.GT:       return model == Body.Sports ? 0.85f : model == Body.Coupe ? 0.7f : model == Body.Muscle ? 0.4f : model == Body.Roadster ? 0.3f : 0.1f;
                case Body.Coupe:    return model == Body.Sports ? 0.8f : model == Body.GT ? 0.7f : model == Body.Roadster ? 0.5f : model == Body.Saloon ? 0.45f : model == Body.Muscle ? 0.4f : model == Body.Hatch ? 0.3f : 0.1f;
                case Body.Muscle:   return model == Body.GT ? 0.4f : model == Body.Sports ? 0.3f : model == Body.Saloon ? 0.3f : 0.1f;
                case Body.Saloon:   return model == Body.Estate ? 0.6f : model == Body.Hatch ? 0.5f : model == Body.Coupe ? 0.4f : model == Body.Sports ? 0.35f : 0.1f;
                case Body.Estate:   return model == Body.Saloon ? 0.6f : model == Body.Van ? 0.45f : model == Body.Hatch ? 0.35f : 0.1f;
                case Body.Hatch:    return model == Body.Saloon ? 0.45f : model == Body.Coupe ? 0.35f : model == Body.Estate ? 0.3f : 0.1f;
                case Body.Roadster: return model == Body.Sports ? 0.6f : model == Body.Coupe ? 0.5f : model == Body.GT ? 0.45f : 0.1f;
                case Body.Offroad:  return model == Body.Pickup ? 0.75f : model == Body.Van ? 0.5f : model == Body.Estate ? 0.3f : 0.1f;
                case Body.Pickup:   return model == Body.Offroad ? 0.75f : model == Body.Van ? 0.5f : 0.1f;
                case Body.Van:      return model == Body.Pickup ? 0.5f : model == Body.Estate ? 0.45f : 0.1f;
            }
            return 0.1f;
        }

        // Weights. Body leads, then continent; size is a tie-breaker.
        const float WBody = 60f, WRegion = 34f, WSize = 14f;
        // Era is the one axis that can go NEGATIVE. Rewarding a close year is not
        // enough on its own: without a penalty the scorer happily dresses a 1992
        // supercar in a 1965 roadster because the body class lines up, and three
        // decades of styling is exactly the mismatch a player notices first.
        const float EraTop = 22f, EraSlope = 30f, EraSpan = 30f;

        public static float Score(CarSpec car, Model m)
        {
            float s = WBody * BodyAffinity(BodyOf(car), m.body);
            if (RegionOf(car) == m.region) s += WRegion;
            s += EraTop - EraSlope * Mathf.Clamp01(Mathf.Abs(car.modelYear - m.year) / EraSpan);
            s += WSize * Mathf.Clamp01(1f - Mathf.Abs(car.kg - m.kg) / 750f);
            return s;
        }

        /// <summary>The shell this catalog car should wear.</summary>
        public static string KeyFor(CarSpec car)
        {
            if (car == null || string.IsNullOrEmpty(car.name)) return Default;

            string handKey = HandKey(car);
            if (handKey != null) return handKey;

            string best = Default;
            float bestScore = float.MinValue;
            foreach (var m in Models)
            {
                // The van and the pickup are scenery. Nothing in a GT4-derived
                // catalog is a 1950s delivery van or a work truck, and letting
                // the scorer reach for them just means the worst-matched car on
                // the grid turns up to a race in a van.
                if (m.body == Body.Van || m.body == Body.Pickup || m.handOnly) continue;
                float s = Score(car, m);
                if (s > bestScore) { bestScore = s; best = m.key; }
            }
            return best;
        }

        /// <summary>The hand-mapped shell for this car, or null if it is scored.
        /// Exposed so the mapping report can mark which is which.</summary>
        public static string HandKey(CarSpec car)
        {
            if (car == null || string.IsNullOrEmpty(car.name)) return null;
            if (hand == null)
            {
                hand = new Regex[HandRules.Length];
                for (int i = 0; i < HandRules.Length; i++)
                    hand[i] = new Regex(HandRules[i].pattern, RegexOptions.IgnoreCase);
            }
            for (int i = 0; i < hand.Length; i++)
                if (hand[i].IsMatch(car.name)) return HandRules[i].key;
            return null;
        }

        // ------------------------------------------------------------------
        //  Loading
        // ------------------------------------------------------------------
        public const string ResourceDir = "CarModels/";
        static readonly Dictionary<string, CarModelDef> cache = new Dictionary<string, CarModelDef>();

        /// <summary>
        /// Load a shell. Cached, because a four-car grid asks for the same two
        /// or three keys and Resources.Load is not free on WebGL.
        /// </summary>
        public static CarModelDef Load(string key)
        {
            if (string.IsNullOrEmpty(key)) key = Default;
            if (cache.TryGetValue(key, out var hit)) return hit;

            var go = PSXTexDecode.LoadPrefab(ResourceDir + key);
            var def = go != null ? go.GetComponent<CarModelDef>() : null;
            if (def != null)
            {
                // The liveries hang off the def, not off a renderer of the
                // prefab: stamp them here too (PSXTexDecode).
                if (def.skinMaterials != null) foreach (var m in def.skinMaterials) PSXTexDecode.Stamp(m);
                PSXTexDecode.Stamp(def.wheelMaterial);
            }
            if (def == null && key != Default)
            {
                Debug.LogWarning("CarModelLibrary: no baked model '" + key + "' - using " + Default);
                def = Load(Default);
            }
            cache[key] = def;
            return def;
        }

        public static CarModelDef LoadFor(CarSpec car) => Load(KeyFor(car));

        /// <summary>Drop the loaded shells. Editor-side only: re-baking replaces
        /// the prefab assets under a cache that is still holding the previous
        /// ones, and a scene built off those points at objects that no longer
        /// exist.</summary>
        public static void ClearCache() => cache.Clear();
    }
}
