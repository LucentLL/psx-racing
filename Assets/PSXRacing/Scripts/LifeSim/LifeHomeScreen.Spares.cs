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

        // =================== block lists (the no-clip rule) ===================

        /// <summary>
        /// A list of fixed-height blocks inside this page, sized to the room
        /// left above <paramref name="reserve"/> so it shows whole blocks only
        /// (BlockSnap), with "N MORE ABOVE / BELOW" on a line under it. Rows
        /// go into the returned content, block i's top at -i x step, x
        /// measured from the list's centre.
        /// </summary>
        RectTransform SpareBlockList(ref float y, int count, float blockH, float gap, float reserve)
        {
            float step = blockH + gap;
            float avail = BodyH - MenuKit.ScrollPad - (-y) - reserve - 28f;
            int shown = BlockSnap.Fit(avail, step, gap, count);
            float viewH = shown * step - gap;
            var view = MenuKit.Rect(body, "SpareList", new Vector2(0.5f, 1f), new Vector2(0f, 1f),
                new Vector2(ColL, y), new Vector2(ColW, viewH));
            y -= viewH + 2f;
            var above = MenuKit.Label(body, "", MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, MenuKit.Accent, ColW * 0.45f, height: 24f);
            var below = MenuKit.Label(body, "", MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColR, y),
                TextAnchor.MiddleRight, MenuKit.Accent, ColW * 0.45f, height: 24f);
            y -= 28f;
            return BlockSnap.Make(view, count, shown, step, gap, above, below);
        }

        /// <summary>The first of <paramref name="tries"/> that fits on one line
        /// in <paramref name="width"/>: never cut, never an ellipsis.</summary>
        static Text SpareLine(Transform parent, float x, float y, float width, Color ink, int size,
                              bool bold, params string[] tries)
        {
            var t = MenuKit.Label(parent, tries[0], size, new Vector2(0.5f, 1f), new Vector2(x, y),
                TextAnchor.MiddleLeft, ink, width, height: 22f, bold: bold);
            for (int i = 1; i < tries.Length && t.preferredWidth > width; i++) t.text = tries[i];
            return t;
        }

        float ExitReserve => 52f + (Town.TownReturn.Pending ? 54f : 0f);

        // =================== STORED PARTS ===================
        const float ShelfBlockH = 100f, ShelfGap = 6f;

        void BuildStoredParts()
        {
            float y = -14f;
            int n = S.partsShelf != null ? S.partsShelf.Count : 0;
            PageHeader(ref y, "STORED PARTS  " + n + "/" + Spares.Capacity);
            SpareLine(body, ColL, y, ColW, MenuKit.Dim, MenuKit.Tiny, false,
                "Spares you own. Fit one to any car it suits, now or after you buy the car; scrap pays " +
                Mathf.RoundToInt(Spares.ScrapShare * 100f) + "% of what it cost.",
                "Spares you own. Fit one to any car it suits; scrap pays " +
                Mathf.RoundToInt(Spares.ScrapShare * 100f) + "% of what it cost.",
                "Fit a spare to any car it suits, or scrap it.");
            y -= 30f;

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
            var content = SpareBlockList(ref y, list.Count, ShelfBlockH, ShelfGap, ExitReserve);
            for (int i = 0; i < list.Count; i++) DrawShelfRow(content, list[i], -i * (ShelfBlockH + ShelfGap));
            ExitRow(ref y, "BACK TO MY CARS", () => { tab = "garage"; Rebuild(); });
        }

        /// <summary>One spare as one fixed block: what it is, what it fits and
        /// what it cost, where it came from, who can take it now - and on the
        /// right FIT TO A CAR over SELL FOR SCRAP.</summary>
        void DrawShelfRow(RectTransform parent, SparePart p, float y)
        {
            float left = -ColW * 0.5f;
            var bg = MenuKit.Rect(parent, "Spare_" + p.id, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(ColW, ShelfBlockH), SpareRowBg);
            bg.GetComponent<Image>().raycastTarget = false;
            float bw = Mathf.Min(260f, ColW * 0.32f);
            float ix = left + 14f, iw = ColW - 28f - bw - 16f;

            SpareLine(parent, ix, y - 4f, iw, GradeInk(p.grade), 18, true, SpareHead(p), p.label);
            SpareLine(parent, ix, y - 30f, iw, MenuKit.Dim, MenuKit.Tiny, false,
                Spares.FitsLine(p).ToLowerInvariant() + "   ·   paid " + MenuKit.Money(p.paid),
                Spares.FitsLine(p).ToLowerInvariant(),
                p.IsUpgrade ? "any car to stage " + p.maxStage : "any car");
            SpareLine(parent, ix, y - 52f, iw, MenuKit.Dim, MenuKit.Tiny, false,
                "from " + p.donorName + ", " + LifeRules.DateLabel(p.dayPulled).ToLowerInvariant(),
                "from " + p.donorName, "pulled " + LifeRules.DateLabel(p.dayPulled).ToLowerInvariant());

            var takers = Spares.CarsThatTakeIt(S, p);
            string all = "", first = "";
            for (int i = 0; i < takers.Count; i++)
            {
                var plan = Spares.Plan(S, takers[i], p, false, Spares.Use.Auto, null, gates: false);
                string one = CarTag(takers[i]) + " - " + plan.what.ToLowerInvariant();
                if (i == 0) first = one;
                all += (i > 0 ? ";  " : "") + one;
            }
            if (takers.Count == 0)
                SpareLine(parent, ix, y - 74f, iw, MenuKit.Accent, MenuKit.Tiny, false,
                    "none of your cars can take it now - it keeps", "no car takes it now");
            else
                SpareLine(parent, ix, y - 74f, iw, MenuKit.Good, MenuKit.Tiny, false,
                    "takes it now: " + all,
                    takers.Count == 1 ? "takes it now: " + first : "takes it now: " + takers.Count + " of your cars",
                    "takes it now: " + takers.Count + (takers.Count == 1 ? " car" : " cars"));

            var captured = p;
            float bx = ColW * 0.5f - 14f - bw * 0.5f;
            MenuKit.Button(parent, "FIT TO A CAR", new Vector2(0.5f, 1f), new Vector2(bx, y - 8f), new Vector2(bw, 40f),
                () => { sparePickId = captured.id; spareSellArmed = ""; tab = "partfit"; Rebuild(); }, 16,
                takers.Count > 0 ? (Color?)new Color(0.20f, 0.30f, 0.24f, 1f) : null)
                .gameObject.name = "Btn_fit_" + p.id;
            bool armed = spareSellArmed == p.id;
            MenuKit.Button(parent, armed ? "YES, SCRAP " + MenuKit.Money(Spares.ScrapValue(p))
                                         : "SELL FOR SCRAP  " + MenuKit.Money(Spares.ScrapValue(p)),
                new Vector2(0.5f, 1f), new Vector2(bx, y - 54f), new Vector2(bw, 40f),
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
        }

        /// <summary>A car named so two of the same model can be told apart:
        /// its name, its miles, and whether you are in it.</summary>
        string CarTag(OwnedCar c) =>
            LifeRules.ShortName(c) + " (" + c.odoMiles.ToString("N0") + " MI" +
            (c.id == S.activeCar ? ", YOURS NOW" : "") + ")";

        // =================== PICK A CAR ===================
        const float PickBlockH = 76f, PickGap = 6f;

        void BuildPartFit()
        {
            float y = -14f;
            var p = Spares.Find(S, sparePickId);
            if (p == null) { tab = "parts"; Rebuild(); return; }
            PageHeader(ref y, "FIT A SPARE");
            SpareLine(body, ColL, y, ColW, GradeInk(p.grade), 20, true, SpareHead(p), p.label);
            y -= 30f;
            Paragraph("From " + p.donorName + ". At home: no charge (the part is paid for), just the days and " +
                      "the skill. The mechanic fits it for labour only: BRING YOUR OWN PART on their page." +
                      (Junkyard.FaultRisk(p.grade) > 0
                          ? " A " + Junkyard.GradeWord(p.grade).ToLowerInvariant() + " part: " +
                            Junkyard.FaultRisk(p.grade) + "% it brings something with it."
                          : ""), ref y);

            MenuKit.Label(body, "WHICH CAR?", MenuKit.Small, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, MenuKit.Accent, ColW, height: 28f, bold: true);
            y -= 32f;

            var cars = S.cars.FindAll(c => c != null);
            var content = SpareBlockList(ref y, cars.Count, PickBlockH, PickGap, ExitReserve);
            float bw = Mathf.Min(240f, ColW * 0.30f);
            float left = -ColW * 0.5f, tw = ColW - bw - 44f;
            for (int i = 0; i < cars.Count; i++)
            {
                var car = cars[i];
                float by = -i * (PickBlockH + PickGap);
                var plan = Spares.Plan(S, car, p, false);
                var fits = Spares.Plan(S, car, p, false, Spares.Use.Auto, null, gates: false);
                var bg = MenuKit.Rect(content, "Pick_" + car.id, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, by), new Vector2(ColW, PickBlockH), SpareRowBg);
                bg.GetComponent<Image>().raycastTarget = false;
                float x = left + 14f;
                SpareLine(content, x, by - 4f, tw, Color.white, 18, true,
                    car.displayName.ToUpperInvariant(), LifeRules.ShortName(car));
                SpareLine(content, x, by - 28f, tw, MenuKit.Dim, MenuKit.Tiny, false,
                    car.odoMiles.ToString("N0") + " mi   ·   " +
                    (car.id == S.activeCar ? "you are driving this one   ·   " : "") +
                    Upgrades.TotalStages(car) + " stages fitted",
                    car.odoMiles.ToString("N0") + " mi   ·   " + Upgrades.TotalStages(car) + " stages");
                // ONE line, straight under the miles: what it does here, or why not.
                if (plan.ok)
                    SpareLine(content, x, by - 50f, tw, MenuKit.Good, MenuKit.Tiny, false,
                        plan.what.ToLowerInvariant() + "   ·   " + plan.days + "d to fit   ·   skill " + plan.skillReq,
                        plan.what.ToLowerInvariant() + "   ·   " + plan.days + "d",
                        plan.what.ToLowerInvariant());
                else
                    SpareLine(content, x, by - 50f, tw, MenuKit.Bad, MenuKit.Tiny, false,
                        fits.ok ? fits.what.ToLowerInvariant() + " - but " + plan.reason : "won't take it: " + plan.reason,
                        fits.ok ? "but " + plan.reason : plan.reason,
                        "can't fit it here");

                var captured = car;
                var b = MenuKit.Button(content, plan.ok ? "FIT TO THIS ONE" : "CAN'T",
                    new Vector2(0.5f, 1f), new Vector2(ColW * 0.5f - 14f - bw * 0.5f, by - 16f), new Vector2(bw, 44f),
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
