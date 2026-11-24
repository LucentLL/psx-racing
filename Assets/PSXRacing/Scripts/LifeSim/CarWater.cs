using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// THE WATER ON AN OWNED CAR (spray-fix2, 2026-11-23; the owner: "Cars can be wet in the menu.
    /// I was actually going to request an option to 'wash' car in garage. My complaint was about the
    /// size of the water beads"). A car that came home from a drive in the rain, or was just washed,
    /// is WET on the turntable (CarViewer: the garage, MY CARS, the showroom, every viewer) at the
    /// real bead size (PSXCarDrops.cginc, PSX_CARDROPS2) and dries.
    ///
    /// Saved with the car (OwnedCar.wet, wetDay, wetSlot): how wet it was when it got wet, and the
    /// time slot it got wet in. The life sim's clock moves in slots of hours, so a car is dry once
    /// a slot has passed; inside its slot it dries over <see cref="DrySeconds"/> of play (real
    /// seconds while the game runs - the menus have no finer clock), the film first, the last
    /// beads shrinking away after it.
    ///
    /// THE WASH (LifeRules.WashCar, the existing WASH button on MY CARS, $8, no time slot):
    /// <see cref="WashSeconds"/> of water sheeting over the body with streams running down it, a
    /// run-off as the last streams go, then every panel beaded and drying like after rain. It
    /// clears OwnedCar.dirt (the only grime the game keeps: road dirt that dulls the shine).
    /// </summary>
    public static class CarWater
    {
        /// <summary>The wash itself: water sheeting and running for this long (s) ...</summary>
        public const float WashSeconds = 6f;
        /// <summary>... the last of it the streams running off.</summary>
        public const float RunOffSeconds = 2.5f;
        /// <summary>Beads gone (s of play) after the car got wet; the film goes in FilmSeconds.</summary>
        public const float DrySeconds = 180f, FilmSeconds = 45f;

        struct Live { public float level, t0, washAt; }
        static readonly Dictionary<string, Live> live = new Dictionary<string, Live>();

        /// <summary>For the preview tools: the clock (s) to read instead of the real one, or below 0.</summary>
        public static float ClockOverride = -1f;
        static float Clock => ClockOverride >= 0f ? ClockOverride : Time.realtimeSinceStartup;

        /// <summary>The car got wet now (rain on the drive home, a wash).</summary>
        public static void Soak(LifeState s, OwnedCar car, float level)
        {
            if (s == null || car == null) return;
            car.wet = Mathf.Clamp01(level);
            car.wetDay = s.day;
            car.wetSlot = s.slotIndex;
            live[car.id ?? ""] = new Live { level = car.wet, t0 = Clock, washAt = -1f };
        }

        /// <summary>Washed now: the wash runs, then it dries from wet through.</summary>
        public static void Wash(LifeState s, OwnedCar car)
        {
            if (s == null || car == null) return;
            Soak(s, car, 1f);
            var l = live[car.id ?? ""];
            l.washAt = Clock;
            l.t0 = Clock + WashSeconds;
            live[car.id ?? ""] = l;
        }

        /// <summary>The water on the car now: x the beads (0..1, the shader's rain amount), y the film
        /// (0..1, _PSXCarWet), z the running water (1 the wash's streams, -1 drying: no new drops land).</summary>
        public static Vector3 Now(LifeState s, OwnedCar car)
        {
            if (s == null || car == null || car.wet <= 0f) return Vector3.zero;
            // a slot (hours) has passed since it got wet: dry
            if (car.wetDay != s.day || car.wetSlot != s.slotIndex) return Vector3.zero;
            string key = car.id ?? "";
            if (!live.TryGetValue(key, out var l))
            {
                // loaded from a save in the slot it got wet in: it dries from what was saved, from now
                l = new Live { level = car.wet, t0 = Clock, washAt = -1f };
                live[key] = l;
            }
            float now = Clock;
            if (l.washAt >= 0f && now < l.washAt + WashSeconds)
            {
                float tw = now - l.washAt;
                // the streams, then their run-off; the film whole throughout
                float run = 1f - Mathf.Clamp01((tw - (WashSeconds - RunOffSeconds)) / RunOffSeconds);
                // the beads come in under the running water as it thins (a sheet carries none)
                float beads = Mathf.Lerp(0.35f, 1f, 1f - run);
                return new Vector3(beads, 1f, Mathf.Max(run, 0.02f));
            }
            float t = Mathf.Max(0f, now - l.t0);
            float b = l.level * Mathf.Clamp01(1f - t / DrySeconds);
            float f = l.level * Mathf.Clamp01(1f - t / FilmSeconds);
            if (b <= 0f)
            {
                car.wet = 0f;
                return Vector3.zero;
            }
            return new Vector3(b, f, -1f);
        }

        /// <summary>What is left to save: the level now (the save keeps the slot).</summary>
        public static void Bank(LifeState s, OwnedCar car)
        {
            if (s == null || car == null || car.wet <= 0f) return;
            var w = Now(s, car);
            car.wet = Mathf.Max(Mathf.Max(w.x, w.y), 0f);   // (mid-wash the film is whole)
        }

        /// <summary>A new game or a load: forget the clocks (the saved levels stand).</summary>
        public static void Reset() => live.Clear();
    }
}
