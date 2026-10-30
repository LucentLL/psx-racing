using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// REAL HEIGHTS round 11 (2026-10-30), the solve's side: the city-wide launches by class and
    /// round 10's leftovers. See Docs/CHARLOTTE.md "REAL HEIGHTS - round 11".
    /// </summary>
    public static partial class CityElevation
    {
        /// <summary>(round 11) The solve-side switches as one set (the look tool's A/B).</summary>
        public static bool[] Round11Flags() => new[] { R11ClimbC1On, R11StreetSeatOn, R11SpillUnderOn, R11CrestCapOn };
        public static void SetRound11Flags(bool[] f)
        {
            if (f == null) return;
            if (f.Length > 0) R11ClimbC1On = f[0];
            if (f.Length > 1) R11StreetSeatOn = f[1];
            if (f.Length > 2) R11SpillUnderOn = f[2];
            if (f.Length > 3) R11CrestCapOn = f[3];
        }

        // ------------------------------------------------------------------
        //  CRESTCAP: the drawn profile may lower a crest 0.30 m, not 0.15.
        //  A crest's curve lowers its apex by A L / 8; at 0.15 m a 9.8% crest
        //  (Carolina Place Parkway at node 8632, +1.6% -> -8.2%) got a 12 m
        //  curve of the 24.5 m its road's crest radius asks, and the car left
        //  the road over it at 76 km/h of a 90 km/h road. The land, the fans
        //  (MarkNodes), the minor arms (their eases), the walks and the lots
        //  all follow the drawn road, so a deeper apex opens no gap; structure
        //  keeps its 3 cm. PSX_CITY_R11_CRESTCAP=0: before (0.15 m).
        // ------------------------------------------------------------------
        public static bool R11CrestCapOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R11_CRESTCAP") != "0";
        static float R11CrestLowerMax => R11CrestCapOn ? 0.30f : VsLowerMaxM;

        // ------------------------------------------------------------------
        //  STREETSEAT: a street branch is ON its host where its ribbon is.
        //
        //  A street that forks off another at a shallow angle (a branch the
        //  tile clips onto its host: a divided road's split, a slip street, a
        //  minor road leaving at a mitred node) lies INSIDE the host's
        //  pavement for its first metres - the tile draws it collapsed onto
        //  the host's edge, or with its inner edge cut against it - so a car
        //  there drives on the host. Only ramps were seated there; a street
        //  branch kept its own measured line, and where the two roads climb
        //  differently (Todd Road off its through road at node 8859: 9.5%
        //  against 2.3%) the branch's ribbon came out of the host's pavement
        //  0.4 m under it - a step up into the host one way, a drop the
        //  other (the launch audit's "mitred joint" spots with the mesh off
        //  the data). The ramps' rule, for streets: seated (stations at the
        //  zone's end and no more than 5 m apart inside it) exactly where the
        //  ribbon runs INTO the host (CityMeshes.SeatIntoOnly), and eased off
        //  beyond on its own profile (ClimbOut, C1 with CLIMBC1).
        //  PSX_CITY_R11_STREETSEAT=0: before.
        // ------------------------------------------------------------------
        public static bool R11StreetSeatOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R11_STREETSEAT") != "0";
        /// <summary>(round 11, STREETSEAT) Street branch ends seated, last solve.</summary>
        public static int R11StreetSeats { get; private set; }

        // ------------------------------------------------------------------
        //  SPILLUNDER (the 1'47" sliver): round 10's SPILL held a twin deck's
        //  median land on the spill-through slope from its bridge seat BESIDE
        //  the deck only; under the deck's own pavement nothing held it, so a
        //  lattice vertex under the route deck's last metres stayed in the
        //  E 10th St valley and the triangle from it dipped a 1 m sliver of the
        //  median behind the route's rail start, inside the V's mouth. A real
        //  abutment's spill-through slope runs under the bridge's end too: on
        //  the deck's median half (union decks, and the deck they carry on
        //  into through a two-arm node), within R11SpillUnderM of the deck's
        //  end, the land is held on the same slope from the seat (the soffit's
        //  air cap tapering as round 10's). PSX_CITY_R11_SPILLUNDER=0: before.
        // ------------------------------------------------------------------
        public static bool R11SpillUnderOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R11_SPILLUNDER") != "0";
        /// <summary>How far under a deck from its end SPILLUNDER holds the land (a lattice cell and a half).</summary>
        public const float R11SpillUnderM = 12f;
        /// <summary>(SPILLUNDER) Ground samples it held, since load (the probe prints it).</summary>
        public static int R11SpillUnderHits;

        /// <summary>(round 11) What the round-11 rules did in the last solve (the audits print it).</summary>
        public static string R11Report =>
            $"round 11: CLIMBC1 {(R11ClimbC1On ? "on" : "OFF")}; STREETSEAT {(R11StreetSeatOn ? "on" : "OFF")}: {R11StreetSeats} street branch ends seated where their ribbon runs into the host; seat stations added {SeatStationsAdded}; SPILLUNDER {(R11SpillUnderOn ? "on" : "OFF")}; CRESTCAP {(R11CrestCapOn ? "on (0.30 m)" : "OFF (0.15 m)")}; meshes: GORESTART {(CityMeshes.R11GoreStartOn ? "on" : "OFF")}, GOREFAN {(CityMeshes.R11GoreFanOn ? "on" : "OFF")}, MITREPOKE {(CityMeshes.R11MitrePokeOn ? "on" : "OFF")}";

        // ------------------------------------------------------------------
        //  CLIMBC1: a seated branch leaves its host on the host's grade.
        //
        //  ClimbOut faded a seat's offset from the branch's own measured profile
        //  out on a cosine - zero slope at the seat's end - so the branch left
        //  the seat on ITS OWN grade while the seated stations behind it ran on
        //  the host's: a grade break the size of the difference at the seat's
        //  last station (+0.7% -> -6.3% on e3981, +4.5% -> -0.3% on e3215),
        //  which the drawn profile could not round there (a seat end may move
        //  0.10 m), and a car at the ramp's speed left the road (the launch
        //  audit's "ramp seat (climb-out)" class). Now the offset leaves on a
        //  cubic Hermite with the seat's own grade (value and slope at the
        //  seat's end, nothing at the fade's end): the profile is C1 off the
        //  host, and the fade is long enough that neither end of the Hermite
        //  bends sharper than the road's crest radius (CrestR), within the span.
        //  PSX_CITY_R11_CLIMBC1=0: the cosine (before).
        // ------------------------------------------------------------------
        public static bool R11ClimbC1On = System.Environment.GetEnvironmentVariable("PSX_CITY_R11_CLIMBC1") != "0";

        /// <summary>(round 11, CLIMBC1) The seat's offset's slope where it ends at station
        /// <paramref name="at"/>, leaving it along <paramref name="dir"/>: the grade the seated
        /// stations arrive with less the edge's own grade there. 0 with no seated station behind.</summary>
        static float R11SeatGradeOff(CityMap.Edge e, int at, int dir, System.Func<float, float> own, float spanM)
        {
            int back = at - dir;
            if (back < 0 || back >= e.stS.Length || !e.SeatedAt(back)) return 0f;
            float hb = Mathf.Abs(e.stS[at] - e.stS[back]);
            if (hb < 0.5f) return 0f;
            float gSeat = (e.stY[at] - e.stY[back]) / hb;
            float h = Mathf.Clamp(0.25f * spanM, 0.5f, 2f);
            float s0 = e.stS[at], s1 = Mathf.Clamp(s0 + dir * h, 0f, e.length);
            float hh = Mathf.Abs(s1 - s0);
            if (hh < 0.25f) return 0f;
            float gOwn = (own(s1) - own(s0)) / hh;
            return Mathf.Clamp(gSeat - gOwn, -0.15f, 0.15f);
        }

        /// <summary>(round 11, CLIMBC1) The fade's length: from <paramref name="l0"/> (the cosine's)
        /// up, until neither end of the Hermite bends sharper than <paramref name="r"/>, never past
        /// the span.</summary>
        static float R11ClimbLen(float d0, float m0, float l0, float span, float r)
        {
            float L = Mathf.Min(l0, span);
            if (L < 0.5f || r <= 0f) return L;
            for (int guard = 0; guard < 64 && L < span; guard++)
            {
                float c0 = Mathf.Abs(-6f * d0 - 4f * L * m0) / (L * L), c1 = Mathf.Abs(6f * d0 + 2f * L * m0) / (L * L);
                if (Mathf.Max(c0, c1) * r <= 1f) break;
                L = Mathf.Min(span, L + Mathf.Max(2f, 0.1f * L));
            }
            return L;
        }

        /// <summary>(round 11, CLIMBC1) The offset at <paramref name="a"/> m from the seat's end: a
        /// cubic Hermite from (d0, slope m0) to (0, 0) over L.</summary>
        static float R11Ease(float d0, float m0, float L, float a)
        {
            if (L <= 1e-3f || a >= L) return 0f;
            float t = a / L, t2 = t * t, t3 = t2 * t;
            return d0 * (2f * t3 - 3f * t2 + 1f) + m0 * L * (t3 - 2f * t2 + t);
        }
    }
}
