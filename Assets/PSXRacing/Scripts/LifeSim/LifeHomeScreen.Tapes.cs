using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// THE TAPES at home (2026-11-16, see <see cref="Tapes"/>): THE SHELF
    /// ("tapes"), where the races saved to tape stand with their labels and
    /// the VCR under the TV plays them, and the VIDEO STORE ("videostore"),
    /// a generic counter that buys them until the owner's movie-shop pack
    /// arrives. The door to the shelf is the last row of MY CARS (the shelf
    /// and the garage are the house's two rooms of things you keep); the
    /// store is reached from the shelf. Nothing here touches the save but
    /// money and one diary line: the tapes are files beside it.
    /// </summary>
    public partial class LifeHomeScreen
    {
        /// <summary>The tape picked on the shelf / at the counter (its id).</summary>
        string tapeSel = "";
        List<Tapes.Header> shelfCache;
        int shelfFrame = -1;

        List<Tapes.Header> ShelfNow()
        {
            // One read of the folder per page build, however many rows ask.
            if (shelfCache == null || shelfFrame != Time.frameCount)
            {
                shelfCache = Tapes.Shelf();
                shelfFrame = Time.frameCount;
            }
            return shelfCache;
        }

        Tapes.Header PickedTape(List<Tapes.Header> shelf)
        {
            foreach (var h in shelf) if (h.id == tapeSel) return h;
            if (shelf.Count > 0) { tapeSel = shelf[0].id; return shelf[0]; }
            return null;
        }

        /// <summary>The door from MY CARS: one full-width row under the list.</summary>
        void TapeShelfDoor(ref float y)
        {
            if (!Tapes.Enabled) return;
            y -= 10f;
            int n = Tapes.Count();
            MenuKit.Button(body, "THE TAPE SHELF (" + n + ")  ·  VCR  >>",
                new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(ColL, ColW), y), new Vector2(ColW, 46f),
                () => { tab = "tapes"; Rebuild(); }, 17, new Color(0.22f, 0.16f, 0.26f, 1f)).gameObject.name = "Btn_tapeshelf";
            y -= 54f;
        }

        static string HourWord(int hour) => TimeOfDay.At(hour).name.ToUpperInvariant();
        static string WeatherWord(int w) =>
            w == (int)Weather.Rain ? "RAIN" : w == (int)Weather.Snow ? "SNOW" : w == (int)Weather.Fog ? "FOG" : "CLEAR";

        /// <summary>One spine on the shelf: the label left, the length (or the
        /// clerk's offer) right, the picked one lit.</summary>
        void TapeRow(Tapes.Header h, string right, ref float y, string page)
        {
            bool picked = h.id == tapeSel;
            const float rowH = 50f;
            var row = MenuKit.Button(body, "", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL, ColW), y), new Vector2(ColW, rowH),
                () => { tapeSel = h.id; armKey = null; tab = page; Rebuild(); }, 16,
                picked ? MenuKit.TabOnBg : (Color?)null);
            row.gameObject.name = "Btn_tape_" + h.id;
            // The cassette's label stripe: a colour of its own, so a shelf of
            // them reads as a shelf of different tapes.
            float hue = (((h.id ?? "").GetHashCode() & 0x7fffffff) % 360) / 360f;
            var stripe = MenuKit.Rect(row.transform, "Stripe", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(10f, 0f), new Vector2(8f, rowH - 16f), Color.HSVToRGB(hue, 0.55f, 0.85f));
            var stripeImg = stripe.GetComponent<UnityEngine.UI.Image>();
            if (stripeImg != null) stripeImg.raycastTarget = false;
            float rightW = 150f;
            var label = MenuKit.Label(row.transform, h.Spine, 17, new Vector2(0f, 0.5f), new Vector2(28f, 0f),
                TextAnchor.MiddleLeft, picked ? MenuKit.Accent : Color.white, ColW - rightW - 50f, height: 26f, bold: picked);
            label.raycastTarget = false;
            MenuKit.FitOneLine(label, ColW - rightW - 50f);
            // Right-aligned text pivots on its right edge (MenuKit.PivotXFor).
            var r = MenuKit.Label(row.transform, right, 17, new Vector2(1f, 0.5f), new Vector2(-18f, 0f),
                TextAnchor.MiddleRight, MenuKit.Dim, rightW, height: 26f);
            r.raycastTarget = false;
            y -= rowH + 6f;
        }

        // =================== THE SHELF ===================
        void BuildTapes()
        {
            float y = -14f;
            PageHeader(ref y, "THE TAPE SHELF");
            var shelf = ShelfNow();
            if (shelf.Count == 0)
            {
                MenuKit.Para(body, "No tapes yet. After a race, SAVE TO TAPE keeps it: T on the keyboard, " +
                    "SELECT on a pad, or the SAVE TO TAPE button on the screen. The camcorder footage then " +
                    "plays on the VCR here, and the video store buys the good ones.",
                    18, new Vector2(0.5f, 1f), new Vector2(ColL, y), out float h0, TextAnchor.UpperLeft, MenuKit.Dim, ColW);
                y -= h0 + 14f;
                VideoStoreDoor(ref y, 0);
                return;
            }

            var pick = PickedTape(shelf);
            // The picked tape first: the deck and the store are what this page
            // is for, so they sit above the fold on a phone.
            MenuKit.Label(body, Clip(pick.Spine, 60), 19, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, MenuKit.Accent, ColW, height: 28f, bold: true).raycastTarget = false;
            y -= 32f;
            string facts = (string.IsNullOrEmpty(pick.PlayerCarName) ? "" : pick.PlayerCarName + "  ·  ") +
                           pick.Length + "  ·  " + HourWord(pick.hour) + "  ·  " + WeatherWord(pick.weather) +
                           "  ·  WORTH ABOUT " + MenuKit.Money(Tapes.Price(pick));
            var fl = MenuKit.Label(body, facts, MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, MenuKit.Dim, ColW, height: 24f);
            fl.raycastTarget = false;
            MenuKit.FitOneLine(fl, ColW);
            y -= 28f;
            MenuKit.Para(body, Tapes.Pitch(pick), MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y - 8f),
                out float ph, TextAnchor.UpperLeft, Color.white, ColW);
            y -= ph + 14f;

            float bw = Mathf.Min(360f, (ColW - 12f) * 0.5f);
            var captured = pick;
            MenuKit.Button(body, "PLAY ON THE VCR", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL, bw), y), new Vector2(bw, 50f),
                () =>
                {
                    if (!TapePlayback.Play(captured, out string why)) Toast("THE VCR WILL NOT TAKE IT: " + why);
                }, 18, new Color(0.18f, 0.30f, 0.20f, 1f)).gameObject.name = "Btn_vcrplay";
            MenuKit.Button(body, "TO THE VIDEO STORE  >>", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL + bw + 12f, bw), y), new Vector2(bw, 50f),
                () => { armKey = null; tab = "videostore"; Rebuild(); }, 18);
            y -= 58f;
            if (Tapes.MontageEnabled)
            {
                MenuKit.Button(body, "MONTAGE: MARK IN / OUT  >>", new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(ColL, bw), y), new Vector2(bw, 46f),
                    () => { armKey = null; MontagePick(captured); tab = "montage"; Rebuild(); }, 17);
                y -= 54f;
            }
            y -= 6f;

            MenuKit.Rect(body, "Rule", new Vector2(0.5f, 1f), new Vector2(0f, 1f),
                new Vector2(ColL, y), new Vector2(ColW, 2f), MenuKit.Line);
            y -= 14f;
            MenuKit.Label(body, shelf.Count + (shelf.Count == 1 ? " TAPE" : " TAPES") + " ON THE SHELF   ·   PICK ONE",
                16, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Dim, ColW, height: 24f)
                .raycastTarget = false;
            y -= 32f;
            foreach (var h in shelf) TapeRow(h, h.Length, ref y, "tapes");
        }

        void VideoStoreDoor(ref float y, int count)
        {
            float bw = Mathf.Min(420f, ColW);
            MenuKit.Button(body, "TO THE VIDEO STORE  >>", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL, bw), y), new Vector2(bw, 50f),
                () => { armKey = null; tab = "videostore"; Rebuild(); }, 18);
            y -= 60f;
        }

        // =================== THE VIDEO STORE ===================
        /// <summary>
        /// A generic VIDEO counter (the owner's movie-shop pack is to come):
        /// the clerk looks at the label, asks what is on it, and makes an
        /// offer off <see cref="Tapes.Price"/>. Two presses to sell, as every
        /// sale in the house is; the money goes in the wallet, one line in the
        /// diary, and the tape leaves the shelf for good.
        /// </summary>
        void BuildVideoStore()
        {
            float y = -14f;
            PageHeader(ref y, "VIDEO STORE");
            MenuKit.Para(body, "RENTALS  ·  NEW RELEASES  ·  WE BUY TAPES OF LOCAL RACING. The clerk keeps a shelf " +
                "of street racing tapes behind the counter, and pays for what is on them: a win, a close finish, " +
                "a near miss, a crash, a big number on the speedo.",
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y), out float ih, TextAnchor.UpperLeft, MenuKit.Dim, ColW);
            y -= ih + 14f;

            var shelf = ShelfNow();
            if (shelf.Count == 0)
            {
                MenuKit.Label(body, "You have no tapes to sell.", 18, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                    TextAnchor.MiddleLeft, Color.white, ColW, height: 28f).raycastTarget = false;
                y -= 40f;
                return;
            }

            var pick = PickedTape(shelf);
            int offer = Tapes.Price(pick);
            MenuKit.Label(body, Clip(pick.Spine, 60), 19, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, MenuKit.Accent, ColW, height: 28f, bold: true).raycastTarget = false;
            y -= 32f;
            MenuKit.Para(body, "\"" + Tapes.Pitch(pick) + " I'll give you " + MenuKit.Money(offer) + " for it.\"",
                18, new Vector2(0.5f, 1f), new Vector2(ColL, y - 8f), out float ph, TextAnchor.UpperLeft, Color.white, ColW);
            y -= ph + 14f;
            var captured = pick;
            ArmedButton("selltape:" + pick.id, "SELL IT FOR " + MenuKit.Money(offer), null,
                () => SellTape(captured), Mathf.Min(420f, ColW), ref y);
            y -= 6f;

            MenuKit.Rect(body, "Rule", new Vector2(0.5f, 1f), new Vector2(0f, 1f),
                new Vector2(ColL, y), new Vector2(ColW, 2f), MenuKit.Line);
            y -= 14f;
            MenuKit.Label(body, "YOUR TAPES   ·   THE CLERK'S OFFER", 16, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, MenuKit.Dim, ColW, height: 24f).raycastTarget = false;
            y -= 32f;
            foreach (var h in shelf) TapeRow(h, MenuKit.Money(Tapes.Price(h)), ref y, "videostore");
        }

        void SellTape(Tapes.Header h)
        {
            int offer = Tapes.Price(h);
            if (!Tapes.Remove(h)) { Toast("THE CLERK CANNOT FIND THAT TAPE"); Rebuild(); return; }
            S.money += offer;
            S.calendarLog.Add(LifeRules.LogDate(S.day) + ": sold a race tape (" + h.venueName + ") at the video store, " +
                              MenuKit.Money(offer));
            LifeSimManager.Save();
            tapeSel = "";
            shelfCache = null;
            Toast("SOLD: " + h.venueName.ToUpperInvariant() + " " + Camcorder.DateStamp(h.day) + " FOR " + MenuKit.Money(offer));
            Rebuild();
        }

        /// <summary>For the preview tool: sell the picked tape as the second press would.</summary>
        public void SellPickedForPreview()
        {
            var shelf = ShelfNow();
            var pick = PickedTape(shelf);
            if (pick != null) SellTape(pick);
        }

        // =================== THE MONTAGE ===================
        /// <summary>The clips so far (tape id, in, out), and the IN / OUT being
        /// marked on the picked tape.</summary>
        readonly List<(string id, float tIn, float tOut)> montClips = new List<(string, float, float)>();
        float montIn, montOut = -1f;
        string montTape = "";

        void MontagePick(Tapes.Header h)
        {
            if (h == null) return;
            tapeSel = h.id;
            if (montTape != h.id)
            {
                montTape = h.id;
                montIn = 0f;
                montOut = Mathf.Min(h.duration, 20f);
            }
        }

        void MarkRow(string label, float value, System.Action<float> nudge, ref float y)
        {
            float lw = 230f, bw = 86f, gap = 8f;
            MenuKit.Label(body, label + "  " + Camcorder.Counter(value) + "." + Mathf.FloorToInt((value % 1f) * 10f),
                18, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft, Color.white, lw, height: 40f)
                .raycastTarget = false;
            float[] steps = { -10f, -1f, 1f, 10f };
            string[] names = { "-10 s", "-1 s", "+1 s", "+10 s" };
            for (int i = 0; i < steps.Length; i++)
            {
                float d = steps[i];
                MenuKit.Button(body, names[i], new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(ColL + lw + i * (bw + gap), bw), y), new Vector2(bw, 40f),
                    () => { nudge(d); Rebuild(); }, 16).gameObject.name = "Btn_" + label + names[i];
            }
            y -= 48f;
        }

        /// <summary>
        /// MONTAGE: mark IN and OUT on a tape (steppers, so a pad and a thumb
        /// work it as well as a mouse), ADD CLIP, and SAVE joins the clips onto
        /// a new tape (<see cref="Tapes.Montage"/>). One road and one field per
        /// montage; the tapes cut from are taped over, and the montage sells
        /// for a little more than they would have.
        /// </summary>
        void BuildMontage()
        {
            float y = -14f;
            PageHeader(ref y, "MONTAGE");
            var shelf = ShelfNow();
            var pick = PickedTape(shelf);
            if (pick == null)
            {
                MenuKit.Label(body, "No tapes on the shelf.", 18, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                    TextAnchor.MiddleLeft, Color.white, ColW, height: 28f).raycastTarget = false;
                return;
            }
            MontagePick(pick);
            // Clips whose tape has left the shelf go with it.
            montClips.RemoveAll(c => shelf.Find(t => t.id == c.id) == null);
            Tapes.Header first = montClips.Count > 0 ? shelf.Find(t => t.id == montClips[0].id) : null;

            MenuKit.Para(body, "Mark IN and OUT on a tape and ADD CLIP. Clips of the same race join, in order, onto a " +
                "new tape when you SAVE; the tapes you cut from are taped over (the montage sells for a little more " +
                "than they would have).", MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y), out float ih,
                TextAnchor.UpperLeft, MenuKit.Dim, ColW);
            y -= ih + 12f;
            MenuKit.Label(body, Clip(pick.Spine, 60) + "   ·   " + pick.Length, 18, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Accent, ColW, height: 28f, bold: true).raycastTarget = false;
            y -= 36f;

            float dur = pick.duration;
            MarkRow("IN ", montIn, d => { montIn = Mathf.Clamp(montIn + d, 0f, Mathf.Max(0f, montOut - 1f)); }, ref y);
            MarkRow("OUT", montOut, d => { montOut = Mathf.Clamp(montOut + d, Mathf.Min(dur, montIn + 1f), dur); }, ref y);

            bool sameRace = first == null || Tapes.SameRace(first, pick);
            float len = Mathf.Max(0f, montOut - montIn);
            float w = Mathf.Min(460f, ColW);
            var capturedPick = pick;
            MenuKit.Button(body, sameRace ? "ADD CLIP  (" + Camcorder.Counter(len) + ")" : "NOT THE SAME RACE AS THE FIRST CLIP",
                new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(ColL, w), y), new Vector2(w, 46f),
                sameRace && len >= 1f ? (UnityEngine.Events.UnityAction)(() =>
                {
                    montClips.Add((capturedPick.id, montIn, montOut));
                    montIn = montOut;
                    montOut = Mathf.Min(capturedPick.duration, montIn + 20f);
                    Rebuild();
                }) : null, 17, sameRace && len >= 1f ? (Color?)null : MenuKit.BtnBgDisabled).gameObject.name = "Btn_addclip";
            y -= 56f;

            MenuKit.Rect(body, "Rule", new Vector2(0.5f, 1f), new Vector2(0f, 1f),
                new Vector2(ColL, y), new Vector2(ColW, 2f), MenuKit.Line);
            y -= 14f;
            float total = 0f;
            foreach (var c in montClips) total += c.tOut - c.tIn;
            MenuKit.Label(body, "CLIPS (" + montClips.Count + ")   ·   " + Camcorder.Counter(total), 16,
                new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Dim, ColW, height: 24f)
                .raycastTarget = false;
            y -= 30f;
            for (int i = 0; i < montClips.Count; i++)
            {
                var c = montClips[i];
                var t = shelf.Find(x => x.id == c.id);
                int idx = i;
                var lab = MenuKit.Label(body, (i + 1) + ".  " + (t != null ? t.venueName.ToUpperInvariant() + " " + Camcorder.DateStamp(t.day) : "?") +
                    "   " + Camcorder.Counter(c.tIn) + " - " + Camcorder.Counter(c.tOut), 17, new Vector2(0.5f, 1f),
                    new Vector2(ColL, y), TextAnchor.MiddleLeft, Color.white, ColW - 150f, height: 36f);
                lab.raycastTarget = false;
                MenuKit.FitOneLine(lab, ColW - 150f);
                MenuKit.Button(body, "REMOVE", new Vector2(0.5f, 1f), new Vector2(MenuKit.ColRight(ColR, 130f), y),
                    new Vector2(130f, 36f), () => { if (idx < montClips.Count) montClips.RemoveAt(idx); Rebuild(); }, 15)
                    .gameObject.name = "Btn_removeclip" + i;
                y -= 42f;
            }
            y -= 6f;
            string refusal = montClips.Count == 0 ? "ADD A CLIP FIRST" : total < Tapes.MinSeconds ? "AT LEAST 5 SECONDS" : null;
            ArmedButton("montage:save", "SAVE THE MONTAGE", refusal, SaveMontage, w, ref y);
        }

        void SaveMontage()
        {
            var shelf = ShelfNow();
            var clips = new List<Tapes.Clip>();
            foreach (var c in montClips)
            {
                var t = shelf.Find(x => x.id == c.id);
                if (t != null) clips.Add(new Tapes.Clip { tape = t, tIn = c.tIn, tOut = c.tOut });
            }
            var h = Tapes.Montage(clips, out string why);
            if (h == null) { Toast("THE MONTAGE DID NOT TAKE: " + why); Rebuild(); return; }
            montClips.Clear();
            montTape = "";
            shelfCache = null;
            tapeSel = h.id;
            S.calendarLog.Add(LifeRules.LogDate(S.day) + ": cut a montage (" + h.venueName + ", " + clips.Count +
                              (clips.Count == 1 ? " clip" : " clips") + ")");
            LifeSimManager.Save();
            Toast("MONTAGE ON THE SHELF: " + h.venueName.ToUpperInvariant() + " · " + h.Length + " · WORTH " + MenuKit.Money(Tapes.Price(h)));
            tab = "tapes";
            Rebuild();
        }
    }
}
