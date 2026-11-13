using UnityEngine;
using UnityEngine.SceneManagement;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// R1c-A: the pages a crash leaves before anything else (CrashFates): the
    /// one hospital summary, the write-off decision, and the obituary that ends
    /// a career (only NEW GAME from there).
    /// </summary>
    public partial class LifeHomeScreen
    {
        /// <summary>Rebuild's first question: a fate page is due (the pending
        /// injury is processed here, once).</summary>
        bool FateDue()
        {
            CrashFates.Process(S);
            return CrashFates.PageDue(S);
        }

        void FateLine(ref float y, string text, int size, Color color, bool bold = false)
        {
            MenuKit.Para(body, text, size, new Vector2(0.5f, 1f), new Vector2(ColL, y), out float h,
                         TextAnchor.UpperLeft, color, ColW, bold);
            y -= h + 6f;
        }

        void FateRow(ref float y, string left, string right, Color color)
        {
            float half = ColW * 0.5f;
            MenuKit.FitOneLine(MenuKit.Label(body, left, MenuKit.Small, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, MenuKit.Dim, half, 30f), half);
            MenuKit.FitOneLine(MenuKit.Label(body, right, MenuKit.Small, new Vector2(0.5f, 1f), new Vector2(ColL + half, y),
                TextAnchor.MiddleRight, color, half, 30f), half);
            y -= 34f;
        }

        void BuildFate()
        {
            var f = S.fate;
            float y = -18f;
            if (f.dead) { BuildObituary(ref y); return; }
            if (f.page == "hospital") { BuildHospital(ref y); return; }
            BuildWriteOff(ref y);
        }

        void BuildHospital(ref float y)
        {
            var f = S.fate;
            FateLine(ref y, "YOU WAKE UP IN HOSPITAL", MenuKit.Head, MenuKit.Accent, true);
            FateLine(ref y, "The crash " + f.pendingWhere + " broke more than the car: " + f.injuryWhat + ".", MenuKit.Small, Color.white);
            y -= 4f;
            FateRow(ref y, "IN HOSPITAL", f.hospDays + " DAY" + (f.hospDays == 1 ? "" : "S"), Color.white);
            FateRow(ref y, "THE BILL", MenuKit.Money(f.hospBill), Color.white);
            FateRow(ref y, "INSURANCE PAID", f.hospInsurer > 0 ? MenuKit.Money(f.hospInsurer) : "NOTHING", f.hospInsurer > 0 ? MenuKit.Good : MenuKit.Bad);
            FateRow(ref y, "YOUR DEDUCTIBLE", MenuKit.Money(f.hospDeductible), Color.white);
            FateRow(ref y, "YOU PAID", MenuKit.Money(f.hospPaid), Color.white);
            if (f.hospitalOwed > 0) FateRow(ref y, "YOU STILL OWE", MenuKit.Money(f.hospitalOwed), MenuKit.Bad);
            if (!string.IsNullOrEmpty(f.hospNote)) FateLine(ref y, f.hospNote, MenuKit.Tiny, MenuKit.Dim);
            y -= 6f;
            int left = Mathf.Max(0, f.injuredUntil - S.day);
            FateLine(ref y, "NO DRIVING AND NO WORK UNTIL " + LifeRules.DateLabel(f.injuredUntil) + " (" + left + " MORE DAY" +
                     (left == 1 ? "" : "S") + "). Missed shifts are unpaid medical leave, not held against you.", MenuKit.Small, MenuKit.Accent);
            y -= 8f;
            MenuKit.Button(body, "CONTINUE", new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(460f, 46f), () =>
            {
                CrashFates.HospitalSeen(S);
                tab = "main";
                Rebuild();
            }, 16);
            y -= 54f;
        }

        void BuildWriteOff(ref float y)
        {
            var f = S.fate;
            var car = CrashFates.WriteOffCar(S);
            if (car == null) { f.writeOffCar = ""; tab = "main"; BuildMain(); return; }
            FateLine(ref y, "THE " + car.displayName.ToUpperInvariant() + " IS A WRITE-OFF", MenuKit.Head, MenuKit.Accent, true);
            FateLine(ref y, "Putting it right would cost more than three quarters of what it was worth. Keep it on a salvage title, " +
                            "or let it go for salvage.", MenuKit.Small, Color.white);
            y -= 4f;
            FateRow(ref y, "REPAIRS, ABOUT", MenuKit.Money(f.writeOffEstimate), Color.white);
            FateRow(ref y, "IT WAS WORTH", MenuKit.Money(f.writeOffValue), Color.white);
            FateRow(ref y, "INSURANCE PAYS", f.writeOffPayout > 0 ? MenuKit.Money(f.writeOffPayout) : "NOTHING", f.writeOffPayout > 0 ? MenuKit.Good : MenuKit.Bad);
            if (!string.IsNullOrEmpty(f.writeOffNote)) FateLine(ref y, f.writeOffNote, MenuKit.Tiny, MenuKit.Dim);
            y -= 8f;
            int keep = CrashFates.KeepNet(S, car), junk = CrashFates.JunkNet(S, car);
            MenuKit.Button(body, "KEEP IT, SALVAGE TITLE" + (keep > 0 ? " (+" + MenuKit.Money(keep) + ")" : ""), new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(560f, 46f), () =>
                {
                    CrashFates.KeepSalvage(S);
                    tab = "main";
                    Rebuild();
                }, 16);
            y -= 54f;
            string why = CrashFates.JunkRefusal(S);
            MenuKit.Button(body, why == null ? "LET IT GO FOR SALVAGE (" + (junk >= 0 ? "+" : "") + MenuKit.Money(junk) + ")" : "SALVAGE: " + why.ToUpperInvariant(),
                new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(560f, 46f), () =>
                {
                    if (CrashFates.JunkWreck(S) != null) return;
                    tab = "main";
                    Rebuild();
                }, 16, why == null ? (Color?)null : MenuKit.BtnBgDisabled);
            y -= 54f;
        }

        void BuildObituary(ref float y)
        {
            var f = S.fate;
            FateLine(ref y, "THE CHARLOTTE HERALD", MenuKit.Head, MenuKit.Accent, true);
            FateLine(ref y, LifeRules.DateLabel(f.deathDay + 1) + "   ·   OBITUARIES", MenuKit.Tiny, MenuKit.Dim);
            y -= 6f;
            FateLine(ref y, (string.IsNullOrEmpty(S.playerName) ? "LOCAL DRIVER" : S.playerName.ToUpperInvariant()) + ", " + S.age,
                     MenuKit.Body, Color.white, true);
            FateLine(ref y, f.obituary, MenuKit.Small, Color.white);
            y -= 6f;
            FateLine(ref y, "GAME OVER. This career is closed and kept as it ended.", MenuKit.Small, MenuKit.Bad);
            y -= 8f;
            BuildStartOverBlock(ref y);   // the one NEW GAME door (erase, confirm, the wizard)
        }
    }
}
