using System.Text;

namespace PSXRacing
{
    /// <summary>
    /// THE PLAYER'S FONT HAS HOLES.
    ///
    /// Every label in the game is drawn in Unity's built-in LegacyRuntime.ttf,
    /// and in a WebGL player that font is ALL there is: no operating system
    /// sits behind it to borrow a missing glyph from. It has no em dash. The
    /// editor falls back to the OS fonts and draws one, so every menu preview
    /// looked right while the browser printed "UPTOWN LOOP  I-277" with a hole
    /// where the dash was (the CITY front page, 2026-09-29, in a real WebGL
    /// build). The middle dot and the copyright sign are in the font; the
    /// dashes, the ellipsis, the curly quotes and the arrows are not safe.
    ///
    /// So text is made safe WHERE IT IS DRAWN, not where it is written: the
    /// catalog names, the store stock, the fault blurbs in rg2_faults.json and
    /// three hundred string literals keep their typography, and
    /// <see cref="SafeText"/> (every label the game makes) and RaceHUD's
    /// setter pass what they are given through <see cref="Safe"/> first. The
    /// editor draws the same substitutes, so a preview shows what the player
    /// will, and measures it: "..." is wider than an ellipsis.
    /// </summary>
    public static class Glyphs
    {
        /// <summary><paramref name="s"/> with every character the player font
        /// cannot draw replaced by one it can. Returns the same instance (no
        /// allocation) when there is nothing to replace, which is nearly
        /// always.</summary>
        public static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int i = 0;
            while (i < s.Length && Swap(s[i]) == null) i++;
            if (i == s.Length) return s;
            var sb = new StringBuilder(s.Length + 8);
            sb.Append(s, 0, i);
            for (; i < s.Length; i++)
            {
                string r = Swap(s[i]);
                if (r != null) sb.Append(r); else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        /// <summary>True when <paramref name="s"/> has nothing <see cref="Safe"/>
        /// would change - for the self-test's sweep of what the game draws.</summary>
        public static bool IsSafe(string s) => ReferenceEquals(Safe(s), s);

        /// <summary>The replacement for one character, or null to keep it.
        /// Plain ASCII and Latin-1 (the middle dot, the copyright sign, the
        /// degree sign) are kept: the font has them.</summary>
        static string Swap(char c)
        {
            if (c < '\u00a0') return null;
            switch (c)
            {
                case '\u00a0': return " ";            // no-break space
                case '\u2010':                        // hyphen
                case '\u2011':                        // non-breaking hyphen
                case '\u2012':                        // figure dash
                case '\u2013':                        // en dash
                case '\u2014':                        // em dash
                case '\u2015':                        // horizontal bar
                case '\u2212': return "-";            // minus sign
                case '\u2026': return "...";          // ellipsis
                case '\u2018':
                case '\u2019':
                case '\u201a':
                case '\u2032': return "'";            // single quotes, prime
                case '\u201c':
                case '\u201d':
                case '\u201e':
                case '\u2033': return "\"";           // double quotes, double prime
                case '\u2022': return "\u00b7";       // bullet -> middle dot
                case '\u2190': return "<-";
                case '\u2192': return "->";
                case '\u2191': return "^";
                case '\u2193': return "v";
                case '\u2248': return "~";            // almost equal
                case '\u2264': return "<=";
                case '\u2265': return ">=";
            }
            return null;
        }
    }
}
