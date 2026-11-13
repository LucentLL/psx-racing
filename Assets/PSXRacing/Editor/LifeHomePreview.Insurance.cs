using System.IO;
using UnityEngine;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    public static partial class LifeHomePreview
    {
        /// <summary>
        /// R1c-B insurance pages at the narrowest aspect (4:3), with the CLIP
        /// report: the health plan picked at hire (the new-game wizard's job
        /// card), BILLS with a line per car (liability, and a car on a loan at
        /// full), FILE A CLAIM for a crash repair at Delmar, and THE JOB page
        /// asking an older career for its plan. Writes Screenshots/insurance/.
        /// </summary>
        public static void CaptureInsurance()
        {
            string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "insurance");
            Directory.CreateDirectory(outDir);
            var narrow = new[] { ("tablet_4x3", 1024, 768) };
            Insurance.Install();

            LifeSimManager.DeleteSave();
            Shoot(outDir, "ins_hire_plan", sizes: narrow);

            LifeSimManager.StartNewGame("VINCE", 25, LifeRules.DefaultJobIndex);
            LifeRules.SeedFallbackCar(LifeSimManager.State);
            var st = LifeSimManager.State;
            var spec = CarCatalog.Get("nissan_skyline_gt_r__r32___89");
            if (spec == null) { Debug.LogError("[Insurance] no R32 row"); return; }
            var r32 = new OwnedCar
            {
                id = "ins_r32", specId = spec.id, displayName = spec.name,
                catalogPrice = spec.price, paidPrice = spec.price, odoMiles = 61000f,
            };
            st.cars.Add(r32);
            st.carLoans.Add(new CarLoan { carId = r32.id, principal = spec.price, monthlyPayment = 410, monthsRemaining = 40, apr = 0.09f });
            Insurance.Sync(st);
            Insurance.ChoosePlan(st, "standard");
            Insurance.AddPoints(st, 1, "preview");
            var crash = new CarFault
            {
                id = "panel_damage", label = "Crumpled front end", hidden = false, diagnosed = true, stat = "hp",
                cost = 1900, days = 3, add = 25, cause = FaultCause.Impact, hitId = 41,
            };
            r32.faults.Add(crash);
            st.money = 4000;
            LifeSimManager.Save();
            Debug.Log("[Insurance] bills " + LifeRules.MonthlyInsurance(st) + " = " +
                      string.Join(" + ", st.cars.ConvertAll(c => c.displayName + " " + Insurance.MonthlyPremium(st, c))));
            Shoot(outDir, "ins_bills", "bills", sizes: narrow);
            Shoot(outDir, "ins_claim", "claim", sizes: narrow, fields: new[]
            {
                ("claimCarId", (object)r32.id), ("claimFaultId", (object)crash.id),
                ("claimVenue", (object)(int)FaultCatalog.Venue.Mechanic), ("claimBack", (object)"service"),
            });
            st.healthAsked = false; st.healthPlan = "";
            LifeSimManager.Save();
            Shoot(outDir, "ins_job_plan", "jobs", sizes: narrow);
        }
    }
}
