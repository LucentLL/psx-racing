using System;
using System.Collections.Generic;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// The persistent life-sim state — a C# mirror of Racing Game 2's save
    /// schema (src/save/schema.ts, SAVE_VERSION 9.0.0), minus the 2D-world
    /// fields (player x/y, home/office anchors) that a menu-based build has
    /// no use for.
    ///
    /// JsonUtility cannot serialize dictionaries, so the schema's per-car
    /// Record&lt;carId, ...&gt; maps collapse into OwnedCar entries that carry
    /// their own odometer/condition/upgrades. Field names otherwise track the
    /// TS schema so the two codebases stay greppable against each other.
    /// </summary>
    [Serializable]
    public class LifeState
    {
        public int saveVersion = 1;

        // === Core economy / clock ===
        public int money;
        public int day = 1;              // absolute day counter; day 1 is a FRIDAY
        public int slotIndex;            // 0 morning / 1 afternoon / 2 night
        public int slotsActiveToday;     // non-rest slots burned since last sleep
        public bool workedToday;

        // === Player identity ===
        public string playerName = "";
        public int age = 25;

        // === Garage ===
        public string activeCar = "";
        public List<OwnedCar> cars = new List<OwnedCar>();

        // === Health / fitness ===
        public float health = 100f;
        public int daysSinceEat;
        public int daysSinceSleep;
        public bool ateToday;
        public string lastMealTier = ""; // junk / regular / premium
        public int foodStock;
        public float fitness = 50f;

        // === Work / pay / rep ===
        public string playerJob = "";
        public int basePay;
        public float payMultiplier = 1f;
        public float workRep;   // 0 unemployed; 25 set on hire
        public int workDaysTotal;
        public int workDaysPresent;
        public int consecutiveAbsences;
        public bool fired;
        public int pendingSalary;

        // === Street racing ===
        public float streetRep;
        public int streetRacesTotal;
        public int streetRacesWon;
        public int lastRaceDay;   // 0 = never; decay math day-lastRaceDay>7 relies on it

        // === Housing / finance ===
        public string housingType = "";
        public int monthlyHousingCost;
        public int missedPayments;
        public int creditScore = 650;
        public int garageSlots = 1;
        public List<CarLoan> carLoans = new List<CarLoan>();
        public List<BankLoan> bankLoans = new List<BankLoan>();

        // === Skills ===
        public float mechSkill;

        // === Mail / log ===
        public List<MailItem> mail = new List<MailItem>();
        public List<string> calendarLog = new List<string>();

        public OwnedCar FindCar(string id) =>
            cars.Find(c => c.id == id);

        public OwnedCar ActiveCar => FindCar(activeCar);
    }

    /// <summary>One owned car: identity plus everything the schema kept in
    /// per-car Records (carOdometers, carConditions, carUpgrades).</summary>
    [Serializable]
    public class OwnedCar
    {
        public string id;            // catalog id / display name
        public string displayName;
        public float odoMiles;       // RG2 is miles-native; every formula reads miles
        public float fuel = 100f;    // tank percent, per car (schema: fuel)
        public int paidPrice;

        // Condition, 0-100. RG2's four stats are engine / tires / carHP
        // (chassis-body hit points) / paint; brakes and suspension exist only
        // as fault effects and upgrade stages, not as wear stats.
        public float engine = 100f;
        public float tires = 100f;
        public float carHP = 100f;
        public float paint = 100f;

        public List<CarFault> faults = new List<CarFault>();

        // Upgrade stages 0-4 by category (H875/H879)
        public int upPower;
        public int upWeight;
        public int upBrakes;
        public int upSuspension;
        public int upTires;
    }

    [Serializable]
    public class CarFault
    {
        public string id;            // fault catalog key
        public string label;
        public bool hidden = true;   // not yet revealed to the player
        public bool diagnosed;
        public float severity = 1f;
    }

    [Serializable]
    public class CarLoan
    {
        public string carId;
        public int principal;
        public int monthlyPayment;
        public int monthsRemaining;
    }

    [Serializable]
    public class BankLoan
    {
        public int principal;
        public int monthlyPayment;
        public int monthsRemaining;
    }

    [Serializable]
    public class MailItem
    {
        public int day;
        public string subject;
        public string body;
        public bool read;
    }
}
