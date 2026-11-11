using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// THE PARTS SHELF's pages (v24, see <see cref="Spares"/>): STORED PARTS
    /// (beside STORED WHEELS at the MY CARS base), the pick-a-car step a fit
    /// goes through, and the "you have this on the shelf" lines on a car's own
    /// pages - its faults, the parts page's next stages, and the mechanic's
    /// BRING YOUR OWN PART rows (labour only).
    /// </summary>
    public partial class LifeHomeScreen
    {
        /// <summary>The spare the pick-a-car page is fitting.</summary>
        string sparePickId = "";
        /// <summary>Two-step scrap sale: the part armed for SELL.</summary>
        string spareSellArmed = "";

        static readonly Color SpareRowBg = new Color(0.12f, 0.12f, 0.12f, 0.97f);

        static Color GradeInk(int grade) =>
            grade >= 70 ? MenuKit.Good : grade >= 40 ? Color.white : MenuKit.Bad;

        static string SpareHead(SparePart p) => Junkyard.GradeWord(p.grade) + "   " + p.label;

        /// <summary>The two doors at the MY CARS base: the parts shelf and the
        /// wheel rack, each with its count.</summary>
        void StorageDoors(float x, float w, ref float y)
        {
            float dw = Mathf.Min(320f, (w - 10f) * 0.5f);
            MenuKit.Button(body, "STORED PARTS (" + (S.partsShelf != null ? S.partsShelf.Count : 0) + ")",
                new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(x, dw), y), new Vector2(dw, 46f),
                () => { spareSellArmed = ""; tab = "parts"; Rebuild(); }, 17).gameObject.name = "Btn_storedparts";
            MenuKit.Button(body, "STORED WHEELS (" + S.wheelRack.Count + ")",
                new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(x + dw + 10f, dw), y), new Vector2(dw, 46f),
                () => { wheelTargetId = S.activeCar; wheelsFromInspect = false; tab = "wheels"; Rebuild(); }, 17);
            y -= 52f;
        }

        // =================== STORED PARTS ===================
        void BuildStoredParts()
        {
            float y = -14f;
            int n = S.partsShelf != null ? S.partsShelf.Count : 0;
            PageHeader(ref y, "STORED PARTS  " + n + "/" + Spares.Capacity);
            Paragraph("Pulled off a wreck or bought off the yard's rack, and not on a car yet. " +
                      "Fit one to any car it suits, now or after you buy the car. The yard takes " +
                      "a spare back for scrap at " + Mathf.RoundToInt(Spares.ScrapShare * 100f) +
                      "% of what it cost you.", ref y);

            if (n == 0)
            {
                Paragraph("Nothing on the shelf. At the salvage yard, KEEP AS A SPARE puts a part here.", ref y,
                          Color.white);
                ExitRow(ref y, "BACK TO MY CARS", () => { tab = "garage"; Rebuild(); });
                return;
            }

            var list = new System.Collections.Generic.List<SparePart>(S.partsShelf);
            list.Sort((a, b) =>
            {
                int k = string.CompareOrdinal(Spares.KindWord(a), Spares.KindWord(b));
                return k != 0 ? k : b.grade.CompareTo(a.grade);
            });
            foreach (var p in list) DrawShelfRow(p, ref y);
            ExitRow(ref y, "BACK TO MY CARS", () => { tab = "garage"; Rebuild(); });
        }

        void DrawShelfRow(SparePart p, ref float y)
        {
            var bg = MenuKit.Rect(body, "Spare_" + p.id, new Vector2(0.5f, 1f), new Vector2(0f, 1f),
                new Vector2(ColL, y), new Vector2(ColW, 60f), SpareRowBg);
            bg.GetComponent<Image>().raycastTarget = false;
            float top = y;
            float ix = ColL + 14f, iw = ColW - 28f;

            MenuKit.Label(body, SpareHead(p), 18, new Vector2(0.5f, 1f), new Vector2(ix, y - 6f),
                TextAnchor.MiddleLeft, GradeInk(p.grade), iw, height: 26f, bold: true);
            y -= 34f;
            MenuKit.Para(body, Spares.FitsLine(p) + "   ·   FROM " + p.donorName + ", " +
                    LifeRules.DateLabel(p.dayPulled).ToUpperInvariant() + "   ·   PAID " + MenuKit.Money(p.paid),
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ix, y), out float h1,
                TextAnchor.UpperLeft, MenuKit.Dim, iw);
            y -= h1 + 2f;

            var takers = Spares.CarsThatTakeIt(S, p);
            string now;
            if (takers.Count == 0) now = "NONE OF YOUR CARS CAN TAKE IT NOW - IT KEEPS";
            else
            {
                var sb = new System.Text.StringBuilder("TAKES IT NOW: ");
                for (int i = 0; i < takers.Count; i++)
                {
                    var plan = Spares.Plan(S, takers[i], p, false, Spares.Use.Auto, null, gates: false);
                    if (i > 0) sb.Append(";  ");
                    sb.Append(CarTag(takers[i])).Append(" - ").Append(plan.what);
                }
                now = sb.ToString();
            }
            MenuKit.Para(body, now, MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ix, y), out float h2,
                TextAnchor.UpperLeft, takers.Count > 0 ? MenuKit.Good : MenuKit.Accent, iw);
            y -= h2 + 6f;

            float bw = Mathf.Min(300f, (iw - 12f) * 0.5f);
            var captured = p;
            MenuKit.Button(body, "FIT TO A CAR", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ix, bw), y), new Vector2(bw, 42f),
                () => { sparePickId = captured.id; spareSellArmed = ""; tab = "partfit"; Rebuild(); }, 16,
                takers.Count > 0 ? (Color?)new Color(0.20f, 0.30f, 0.24f, 1f) : null)
                .gameObject.name = "Btn_fit_" + p.id;
            bool armed = spareSellArmed == p.id;
            MenuKit.Button(body, armed ? "YES, SCRAP IT FOR " + MenuKit.Money(Spares.ScrapValue(p))
                                       : "SELL FOR SCRAP  " + MenuKit.Money(Spares.ScrapValue(p)),
                new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(ix + bw + 12f, bw), y), new Vector2(bw, 42f),
                () =>
                {
                    if (spareSellArmed != captured.id)
                    {
                        // The cursor stays put: the button keeps its name
                        // (Btn_sell_<id>) through the rebuild that arms it.
                        spareSellArmed = captured.id;
                        Rebuild();
                        return;
                    }
                    spareSellArmed = "";
                    int got = Spares.SellForScrap(S, captured);
                    LifeSimManager.Save();
                    Rebuild();
                    Toast("scrapped " + captured.label.ToLowerInvariant() + " for " + MenuKit.Money(got));
                }, 16, armed ? (Color?)new Color(0.40f, 0.14f, 0.12f, 1f) : null)
                .gameObject.name = "Btn_sell_" + p.id;
            y -= 50f;

            bg.sizeDelta = new Vector2(ColW, top - y);
            y -= 10f;
        }

        /// <summary>A car named so two of the same model can be told apart:
        /// its name, its miles, and whether you are in it.</summary>
        string CarTag(OwnedCar c) =>
            LifeRules.ShortName(c) + " (" + c.odoMiles.ToString("N0") + " MI" +
            (c.id == S.activeCar ? ", YOURS NOW" : "") + ")";

        // =================== PICK A CAR ===================
        void BuildPartFit()
        {
            float y = -14f;
            var p = Spares.Find(S, sparePickId);
            if (p == null) { tab = "parts"; Rebuild(); return; }
            PageHeader(ref y, "FIT A SPARE");
            MenuKit.Label(body, SpareHead(p), 20, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, GradeInk(p.grade), ColW, height: 28f, bold: true);
            y -= 32f;
            Paragraph("From " + p.donorName + ".  At home, in your own garage: no charge (the part " +
                      "is paid for), just the days and the skill. The mechanic will fit it for labour " +
                      "only - take the car there and look under BRING YOUR OWN PART." +
                      (Junkyard.FaultRisk(p.grade) > 0
                          ? "  A " + Junkyard.GradeWord(p.grade).ToLowerInvariant() + " part: " +
                            Junkyard.FaultRisk(p.grade) + "% it brings something with it."
                          : ""), ref y);

            MenuKit.Label(body, "WHICH CAR?", MenuKit.Small, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, MenuKit.Accent, ColW, height: 28f, bold: true);
            y -= 34f;

            float bw = Mathf.Min(240f, ColW * 0.32f);
            float tw = ColW - bw - 40f;
            foreach (var car in S.cars)
            {
                if (car == null) continue;
                var plan = Spares.Plan(S, car, p, false);
                var fits = Spares.Plan(S, car, p, false, Spares.Use.Auto, null, gates: false);
                var bg = MenuKit.Rect(body, "Pick_" + car.id, new Vector2(0.5f, 1f), new Vector2(0f, 1f),
                    new Vector2(ColL, y), new Vector2(ColW, 60f), SpareRowBg);
                bg.GetComponent<Image>().raycastTarget = false;
                float top = y;
                MenuKit.Para(body, car.displayName.ToUpperInvariant(), 18, new Vector2(0.5f, 1f),
                    new Vector2(ColL + 14f, y - 6f), out float nh, TextAnchor.UpperLeft, Color.white, tw, bold: true);
                float ly = y - 6f - nh;
                MenuKit.Para(body, car.odoMiles.ToString("N0") + " mi" +
                        (car.id == S.activeCar ? "   ·   YOU ARE DRIVING THIS ONE" : "") +
                        "   ·   " + Upgrades.TotalStages(car) + " STAGES FITTED",
                    MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL + 14f, ly), out float mh,
                    TextAnchor.UpperLeft, MenuKit.Dim, tw);
                ly -= mh;
                string line = plan.ok ? plan.what + "   ·   " + plan.days + "d to fit   ·   skill " + plan.skillReq
                            : fits.ok ? fits.what + "   ·   but " + plan.reason
                                      : "won't take it: " + plan.reason;
                MenuKit.Para(body, line, MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL + 14f, ly),
                    out float rh, TextAnchor.UpperLeft, plan.ok ? MenuKit.Good : MenuKit.Bad, tw);
                ly -= rh + 8f;

                var captured = car;
                var b = MenuKit.Button(body, plan.ok ? "FIT TO THIS ONE" : "CAN'T",
                    new Vector2(0.5f, 1f), new Vector2(MenuKit.ColRight(ColR - 14f, bw), y - 10f), new Vector2(bw, 44f),
                    plan.ok ? (UnityEngine.Events.UnityAction)(() =>
                    {
                        var pick = Spares.Find(S, sparePickId);
                        string label = pick != null ? pick.label : "it";
                        int days = Spares.Plan(S, captured, pick, false).days;
                        string err = Spares.Fit(S, captured, pick, false);
                        LifeSimManager.Save();
                        tab = "parts";
                        Rebuild();
                        Toast(err ?? (label.ToLowerInvariant() + " going onto " + LifeRules.ShortName(captured).ToLowerInvariant() +
                                      ", " + days + "d"));
                    }) : null, 16, plan.ok ? (Color?)new Color(0.20f, 0.30f, 0.24f, 1f) : MenuKit.BtnBgDisabled);
                b.gameObject.name = "Btn_pick_" + car.id;
                float h = Mathf.Max(top - ly, 64f);
                bg.sizeDelta = new Vector2(ColW, h);
                y = top - h - 10f;
            }
            ExitRow(ref y, "BACK TO STORED PARTS", () => { tab = "parts"; Rebuild(); });
        }

        // =================== on a car's own pages ===================

        /// <summary>
        /// Under a fault on the car's page: a spare on the shelf that replaces
        /// it, fitted at home (no charge, skill and days). Nothing when there
        /// is none.
        /// </summary>
        void SpareForFaultRow(OwnedCar car, CarFault f, ref float y)
        {
            var p = Spares.ForFault(S, car, f);
            if (p == null) return;
            var plan = Spares.Plan(S, car, p, false, Spares.Use.Replace, f.id);
            float w = Mathf.Min(ColW, 640f);
            var captured = p;
            MenuKit.Button(body, "ON THE SHELF: " + p.label + " - FIT IT YOURSELF",
                new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(ColL, w), y), new Vector2(w, 40f),
                plan.ok ? (UnityEngine.Events.UnityAction)(() =>
                {
                    string err = Spares.Fit(S, car, captured, false, Spares.Use.Replace, f.id);
                    Booked(err, captured.label + " (spare) going on");
                }) : null, 14, plan.ok ? (Color?)new Color(0.20f, 0.30f, 0.24f, 1f) : MenuKit.BtnBgDisabled);
            y -= 44f;
            MenuKit.Para(body, "  you have this on the shelf: " + Junkyard.GradeWord(p.grade).ToLowerInvariant() +
                    ", from " + p.donorName + "   ·   " +
                    (plan.ok ? plan.days + "d, no charge" : plan.reason),
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y), out float h, TextAnchor.UpperLeft,
                plan.ok ? MenuKit.Good : MenuKit.Bad, ColW);
            y -= h + 8f;
        }

        /// <summary>Under an upgrade row on the parts page: the next stage is
        /// on the shelf already.</summary>
        void SpareForUpgradeRow(OwnedCar car, Upgrades.Kind kind, ref float y)
        {
            var p = Spares.ForUpgrade(S, car, kind);
            if (p == null) return;
            var plan = Spares.Plan(S, car, p, false, Spares.Use.Upgrade);
            float w = Mathf.Min(ColW, 640f);
            var captured = p;
            MenuKit.Button(body, "ON THE SHELF: " + p.label + " - FIT IT YOURSELF",
                new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(ColL, w), y), new Vector2(w, 40f),
                plan.ok ? (UnityEngine.Events.UnityAction)(() =>
                {
                    string err = Spares.Fit(S, car, captured, false, Spares.Use.Upgrade);
                    Booked(err, captured.label + " (spare) going on");
                }) : null, 14, plan.ok ? (Color?)new Color(0.20f, 0.30f, 0.24f, 1f) : MenuKit.BtnBgDisabled);
            y -= 44f;
            MenuKit.Para(body, "  you have this on the shelf: " + Junkyard.GradeWord(p.grade).ToLowerInvariant() +
                    " " + plan.what.ToLowerInvariant() + ", no charge   ·   " +
                    (plan.ok ? plan.days + "d" : plan.reason),
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y), out float h, TextAnchor.UpperLeft,
                plan.ok ? MenuKit.Good : MenuKit.Bad, ColW);
            y -= h + 10f;
        }

        /// <summary>On the car's own page, above its faults: how many spares on
        /// the shelf this car can take, and the door to them.</summary>
        void SpareSummaryLine(OwnedCar car, ref float y)
        {
            if (S.partsShelf == null || S.partsShelf.Count == 0) return;
            int fit = 0;
            var sb = new System.Text.StringBuilder();
            foreach (var p in S.partsShelf)
            {
                var plan = Spares.Plan(S, car, p, false, Spares.Use.Auto, null, gates: false);
                if (!plan.ok) continue;
                if (fit > 0) sb.Append(";  ");
                sb.Append(p.label.ToLowerInvariant()).Append(" (").Append(plan.what.ToLowerInvariant()).Append(")");
                fit++;
            }
            if (fit == 0) return;
            MenuKit.Para(body, "ON THE SHELF FOR THIS CAR: " + sb, MenuKit.Tiny, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), out float h, TextAnchor.UpperLeft, MenuKit.Good, ColW);
            y -= h + 4f;
            float w = Mathf.Min(ColW, 320f);
            MenuKit.Button(body, "STORED PARTS (" + S.partsShelf.Count + ")", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL, w), y), new Vector2(w, 40f),
                () => { spareSellArmed = ""; tab = "parts"; Rebuild(); }, 15);
            y -= 50f;
        }

        /// <summary>
        /// The mechanic's BRING YOUR OWN PART rows: every spare this car can
        /// take, fitted for LABOUR only (Spares.Plan's labour - the same 1.6x
        /// shop markup a stage is sold at, less the part; 60% of the new part
        /// on a service job), and the car stays for the days it takes.
        /// </summary>
        void MechanicSpareRows(OwnedCar car, string elsewhere, ref float y)
        {
            if (S.partsShelf == null || S.partsShelf.Count == 0) return;
            bool any = false;
            foreach (var p in new System.Collections.Generic.List<SparePart>(S.partsShelf))
            {
                var fits = Spares.Plan(S, car, p, true, Spares.Use.Auto, null, gates: false);
                if (!fits.ok) continue;
                if (!any)
                {
                    y -= 4f;
                    MenuKit.Label(body, "BRING YOUR OWN PART", 15, new Vector2(0.5f, 1f),
                        new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Accent, 400f, height: 26f, bold: true);
                    y -= 28f;
                    Paragraph("Off your shelf, fitted here for labour only. The car stays until it is done.", ref y);
                    any = true;
                }
                var plan = Spares.Plan(S, car, p, true);
                bool ok = plan.ok && elsewhere == null;
                var captured = p;
                float w = Mathf.Min(560f, ColW);
                MenuKit.Button(body, p.label + "  -  LABOUR " + MenuKit.Money(plan.labour) + "  ·  " + plan.days + "d",
                    new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(ColL, w), y), new Vector2(w, 44f),
                    ok ? (UnityEngine.Events.UnityAction)(() =>
                    {
                        string err = Spares.Fit(S, car, captured, true);
                        Booked(err, captured.label + " (spare) booked in");
                    }) : null, 15, ok ? (Color?)null : MenuKit.BtnBgDisabled);
                y -= 48f;
                MenuKit.Para(body, "  " + Junkyard.GradeWord(p.grade).ToLowerInvariant() + ": " +
                        plan.what.ToLowerInvariant() + (plan.ok ? "" : "   ·   " + plan.reason),
                    MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y), out float h, TextAnchor.UpperLeft,
                    plan.ok ? MenuKit.Dim : MenuKit.Bad, ColW);
                y -= h + 8f;
            }
            if (any) y -= 6f;
        }
    }
}
