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
            new Model { key = "rx7_fd",       name = "Mazda RX-7 (FD)",           region = Region.Japan,   year = 1992, body = Body.Sports,   kg = 1280 },
            new Model { key = "supra_a80",    name = "Toyota Supra (A80)",        region = Region.Japan,   year = 1993, body = Body.GT,       kg = 1510 },
            new Model { key = "skyline_r32",  name = "Nissan Skyline GT-R (R32)", region = Region.Japan,   year = 1989, body = Body.Sports,   kg = 1480 },
            new Model { key = "jdm_pickup",   name = "Compact pickup",            region = Region.Japan,   year = 1983, body = Body.Pickup,   kg = 1100 },

            new Model { key = "gto_66",       name = "Pontiac GTO '66",           region = Region.America, year = 1966, body = Body.Muscle,   kg = 1650 },
            new Model { key = "mustang_67",   name = "Ford Mustang Fastback '67", region = Region.America, year = 1967, body = Body.Muscle,   kg = 1400 },
            new Model { key = "charger_69",   name = "Dodge Charger '69",         region = Region.America, year = 1969, body = Body.Muscle,   kg = 1700 },
            new Model { key = "daytona_69",   name = "Charger Daytona '69",       region = Region.America, year = 1969, body = Body.Muscle,   kg = 1750 },

            new Model { key = "bmw_e30",      name = "BMW 3-Series (E30)",        region = Region.Europe,  year = 1985, body = Body.Saloon,   kg = 1150 },
            new Model { key = "audi_saloon",  name = "Audi 80/100",               region = Region.Europe,  year = 1986, body = Body.Saloon,   kg = 1220 },
            new Model { key = "euro_hatch",   name = "European supermini",        region = Region.Europe,  year = 1983, body = Body.Hatch,    kg =  850 },
            new Model { key = "volvo_estate", name = "Volvo 240 Estate",          region = Region.Europe,  year = 1985, body = Body.Estate,   kg = 1350 },
            new Model { key = "citroen_cx",   name = "Citroen CX",                region = Region.Europe,  year = 1980, body = Body.Saloon,   kg = 1320 },
            new Model { key = "mb_pagoda",    name = "Mercedes-Benz SL 'Pagoda'", region = Region.Europe,  year = 1965, body = Body.Roadster, kg = 1350 },
            new Model { key = "landrover",    name = "Land Rover pickup",         region = Region.Europe,  year = 1985, body = Body.Offroad,  kg = 1900 },
            new Model { key = "classic_van",  name = "Classic panel van",         region = Region.Europe,  year = 1960, body = Body.Van,      kg = 1400 },
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
        //  Pass 1: hand-mapped
        // ------------------------------------------------------------------
        // Ordered; first match wins. Everything here is either a car the pack
        // literally modelled, a badge-engineered twin of one, or a call the
        // scorer would get wrong for a structural reason noted alongside it.
        static readonly (string pattern, string key)[] HandRules =
        {
            // Wagons before anything else: a Legacy Touring Wagon is an estate
            // before it is a turbo saloon, and a Stagea is a Skyline that grew a
            // tailgate. The Volvo IS the catalog's estate.
            ("Estate|Touring Wagon|Sport Wagon|STAGEA",          "volvo_estate"),

            // --- Japan ---
            // Tuner one-offs the catalog files under "eur" although they are a
            // Silvia and an Integra. These go first because the broad NISMO rule
            // below would otherwise swallow the 270R.
            ("SILEIGHTY|NISMO 270R",                             "rx7_fd"),
            ("Spoon INTEGRA",                                    "euro_hatch"),
            // The Cosmo Sport rides along with the rotary it started.
            ("Mazda RX-7|Mazda 110S",                            "rx7_fd"),
            // Every Skyline shares the shell family, the works cars are Skylines
            // with stickers, and the Lexus saloons are the same size and decade.
            ("SKYLINE|CALSONIC|PENNZOIL|NISMO|Lexus (IS|GS)",    "skyline_r32"),
            // The R32 is THE Japanese turbo-4WD performance saloon of the era, so
            // its rivals wear it rather than being scored into a European shell
            // on the strength of having four doors.
            ("Subaru IMPREZA|Lancer Evolution|Galant.*VR-4|LEGNUM|LEGACY B4", "skyline_r32"),
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
            ("Chevrolet Corvette|Ford GT40|Dodge VIPER|Chaparral", "mustang_67"),
            ("Chevrolet Camaro|BUICK",                           "gto_66"),

            // --- Europe ---
            ("Volvo 240",                                        "volvo_estate"),
            // The SL line, from the 300 SL the Pagoda replaced to the R129.
            ("Mercedes-Benz (300 SL|SL |SLK)",                   "mb_pagoda"),
            // E30-class German compact saloons: the 2002 is its ancestor, the
            // 190 E its period rival, and the DTM cars are those two. RUF builds
            // 911s, and with no rear-engined shell in the pack a compact German
            // two-door of the same decade beats the Audi saloon the scorer picks.
            ("BMW 2002|BMW M Coupe|Mercedes-Benz 190 E|Mercedes 190 E|RUF ", "bmw_e30"),
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
                if (m.body == Body.Van || m.body == Body.Pickup) continue;
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

            var go = Resources.Load<GameObject>(ResourceDir + key);
            var def = go != null ? go.GetComponent<CarModelDef>() : null;
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
