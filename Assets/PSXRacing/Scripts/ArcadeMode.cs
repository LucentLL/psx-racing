using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PSXRacing
{
    /// <summary>
    /// ARCADE (owner, 2026-11-03, with GT4's Arcade Mode screens as the
    /// reference for the flow): from the title, RACE against AI drivers in
    /// cars like yours, TIME TRIAL alone against the clock, or CRUISE the
    /// city - any car in the catalog, in any colour its shell carries, and
    /// only the driving options to set ("Modifications should be minimal"):
    /// the gearbox, the aids, the tyres, how many rivals.
    ///
    /// NOTHING IS BANKED. While a session is on, <see cref="Edition.HasCareer"/>
    /// reads false, so every career surface a drive can reach is shut at its
    /// own door exactly as it is in the CITY edition - the pumps and the fuel
    /// truck are free, an engine cannot seize the career's car, the debug
    /// bench stays shut, the result is not banked - and LifeSimManager.State
    /// hands anything that still asks a throwaway state that is never saved.
    /// The career on disk, and the one in memory, are untouched; a drive's
    /// result comes back to the arcade's own page (LifeSim.ArcadeFrontEnd)
    /// and is dropped there.
    ///
    /// ON from the title's ARCADE until the arcade's BACK TO TITLE. Kept here
    /// rather than in RaceHandoff on purpose: every launch runs
    /// RaceHandoff.ClearAll, and the session has to outlive all of them.
    /// </summary>
    public static class ArcadeMode
    {
        /// <summary>An arcade session is on (see the class notes).</summary>
        public static bool Active { get; private set; }

        public enum Kind { Race, TimeTrial, Cruise }

        /// <summary>The title's ARCADE: the session starts, and scene 0 opens
        /// on the arcade's own front end instead of the career.</summary>
        public static void Enter()
        {
            Active = true;
            RaceHandoff.ClearAll();
            SceneManager.LoadScene(0);
        }

        /// <summary>Tools only (the menu preview, a check): a session on or off
        /// with no scene loaded, as Edition.Simulate stands in for an edition.</summary>
        public static void Simulate(bool on) => Active = on;

        /// <summary>BACK TO TITLE: the session ends, and the cold boot's title
        /// screen is shown again (TitleScreen.TryBoot takes scene 0's start).</summary>
        public static void ExitToTitle()
        {
            Active = false;
            RaceHandoff.ClearAll();
            TitleScreen.Shown = false;
            SceneManager.LoadScene(0);
        }

        // =================== the driving options ===================
        // Per viewer, in PlayerPrefs, like the CITY front end's picks.
        const string PrefGearbox = "psx.arcade.gearbox", PrefAids = "psx.arcade.aids",
                     PrefTyres = "psx.arcade.tyres", PrefRivals = "psx.arcade.rivals";

        /// <summary>The drive starts in the manual gearbox (a shift press
        /// takes the automatic over to manual mid-drive anyway, and M hands it
        /// back - PlayerCarInput).</summary>
        public static bool Manual
        {
            get => PlayerPrefs.GetInt(PrefGearbox, 0) == 1;
            set { PlayerPrefs.SetInt(PrefGearbox, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>The driving aids: how much the car helps catch a slide
        /// (CarController.countersteerAssist). There is no ABS step on purpose:
        /// the player's car has none (the owner's rule of 2026-09-30, see
        /// CarController.hasAbs).</summary>
        public static readonly string[] AidNames = { "OFF", "STANDARD", "FULL" };
        public static int Aids
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(PrefAids, 1), 0, AidNames.Length - 1);
            set { PlayerPrefs.SetInt(PrefAids, Mathf.Clamp(value, 0, AidNames.Length - 1)); PlayerPrefs.Save(); }
        }
        public static float AssistOf(int aids) =>
            aids <= 0 ? 0f : aids == 1 ? CarController.DefaultCountersteerAssist : 1f;

        /// <summary>The tyres: the tyre stage a car's grip is scaled by
        /// (CarTune.GripStageMult, x1.0 stock to x1.2 at stage 4). A
        /// built-to-tune car races on its own whatever is picked.</summary>
        public static readonly string[] TyreNames = { "STOCK", "SPORT", "RACING" };
        static readonly int[] TyreStages = { 0, 2, 4 };
        public static int Tyres
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(PrefTyres, 0), 0, TyreNames.Length - 1);
            set { PlayerPrefs.SetInt(PrefTyres, Mathf.Clamp(value, 0, TyreNames.Length - 1)); PlayerPrefs.Save(); }
        }
        public static int TyreStage => TyreStages[Tyres];

        /// <summary>How many rivals RACE asks for; a course that races fewer
        /// (a drag event, a parking deck: RaceField.MaxFor) takes its own.</summary>
        public static int Rivals
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(PrefRivals, RaceField.MaxCars - 1), 1, RaceField.MaxCars - 1);
            set { PlayerPrefs.SetInt(PrefRivals, Mathf.Clamp(value, 1, RaceField.MaxCars - 1)); PlayerPrefs.Save(); }
        }

        /// <summary>The most rivals a course races (the player is the rest).</summary>
        public static int RivalsAt(TrackCatalog.TrackDef venue) => Mathf.Min(Rivals, RaceField.MaxFor(venue) - 1);

        /// <summary>RaceHandoffApplier, after the spec: the gearbox the drive
        /// starts in and the slide help. A career drive never comes here and
        /// keeps the car as it came.</summary>
        public static void ApplyDriving(CarController car)
        {
            if (car == null) return;
            car.manualMode = Manual;
            car.countersteerAssist = AssistOf(Aids);
        }

        // =================== RACE's field: cars like yours ===================
        /// <summary>
        /// "Race against similar spec vehicles controlled by AI": the
        /// <paramref name="count"/> cars nearest the player's on PACE - power
        /// to weight, the same hp / max(600, kg) the car meets judge a car by
        /// - with top speed as the tie-break, road cars against road cars and
        /// race cars against race cars, drawn at random from twice as many of
        /// the nearest so two races in one car are not the same seven. Written
        /// into the handoff as the career's draw writes it, at the career's
        /// middle street tier of skill; false leaves the scene's own grid.
        /// </summary>
        public static bool FillField(CarSpec player, int count)
        {
            if (player == null || !CarCatalog.Ready || count <= 0) return false;
            float pace = Pace(player), vmax = player.topSpeedMps;
            bool racer = player.IsRaceCar || player.IsRallyCar;
            var scored = new List<KeyValuePair<CarSpec, float>>();
            foreach (var c in CarCatalog.All)
            {
                if (c == null || c.id == player.id || (c.IsRaceCar || c.IsRallyCar) != racer) continue;
                float d = Mathf.Abs(Mathf.Log(Pace(c) / pace));
                if (vmax > 1f && c.topSpeedMps > 1f) d += 0.5f * Mathf.Abs(Mathf.Log(c.topSpeedMps / vmax));
                scored.Add(new KeyValuePair<CarSpec, float>(c, d));
            }
            if (scored.Count == 0) return false;
            scored.Sort((a, b) => a.Value.CompareTo(b.Value));
            var pool = new List<CarSpec>();
            for (int i = 0; i < scored.Count && i < Mathf.Max(count * 2, count + 4); i++) pool.Add(scored[i].Key);
            count = Mathf.Min(count, pool.Count);

            var ids = new System.Text.StringBuilder();
            var skills = new System.Text.StringBuilder();
            const float BaseSkill = 0.92f;   // LifeRules.FillOpponentFieldFor at street tier 1
            for (int i = 0; i < count; i++)
            {
                int pick = Random.Range(0, pool.Count);
                var spec = pool[pick];
                pool.RemoveAt(pick);
                float skill = Mathf.Clamp(BaseSkill + 0.04f - i * (0.08f / Mathf.Max(1, count - 1)), 0.80f, 1.05f);
                if (i > 0) { ids.Append(';'); skills.Append(';'); }
                ids.Append(spec.id);
                skills.Append(skill.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            RaceHandoff.OpponentSpecIds = ids.ToString();
            RaceHandoff.OpponentSkills = skills.ToString();
            EngineVoiceLibrary.PrefetchHandoff();
            return true;
        }

        static float Pace(CarSpec c) => Mathf.Max(1f, c.hp) / Mathf.Max(600f, c.kg);

        // =================== TIME TRIAL's bests ===================
        // One per course (a reverse twin and a sprint are courses of their
        // own), whatever the car - with the car's name beside it.
        const string PrefBest = "psx.arcade.best.", PrefBestCar = "psx.arcade.bestcar.";

        public static float Best(string venueId) =>
            string.IsNullOrEmpty(venueId) ? 0f : PlayerPrefs.GetFloat(PrefBest + venueId, 0f);

        public static string BestCar(string venueId) =>
            string.IsNullOrEmpty(venueId) ? "" : PlayerPrefs.GetString(PrefBestCar + venueId, "");

        /// <summary>Keep <paramref name="seconds"/> if it beats the course's
        /// best (or there is none). True when it did.</summary>
        public static bool OfferBest(string venueId, float seconds, string carName)
        {
            if (string.IsNullOrEmpty(venueId) || seconds <= 0f) return false;
            float best = Best(venueId);
            if (best > 0f && seconds >= best) return false;
            PlayerPrefs.SetFloat(PrefBest + venueId, seconds);
            PlayerPrefs.SetString(PrefBestCar + venueId, carName ?? "");
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>m:ss.cc, as the race HUD's sheet writes a time.</summary>
        public static string Clock(float seconds)
        {
            if (seconds <= 0f) return "-:--.--";
            int m = Mathf.FloorToInt(seconds / 60f);
            float s = seconds - m * 60f;
            return m + ":" + s.ToString("00.00", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
