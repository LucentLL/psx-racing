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
            float rightW = 150f;
            var label = MenuKit.Label(row.transform, h.Spine, 17, new Vector2(0f, 0.5f), new Vector2(18f, 0f),
                TextAnchor.MiddleLeft, picked ? MenuKit.Accent : Color.white, ColW - rightW - 40f, height: 26f, bold: picked);
            label.raycastTarget = false;
            MenuKit.FitOneLine(label, ColW - rightW - 40f);
            var r = MenuKit.Label(row.transform, right, 17, new Vector2(1f, 0.5f), new Vector2(-18f - rightW, 0f),
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
            y -= 64f;

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
    }
}
