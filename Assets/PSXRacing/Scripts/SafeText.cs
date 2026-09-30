using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// A UI Text that only ever holds characters the player's font can draw
    /// (see <see cref="Glyphs"/>): whatever is assigned to <see cref="text"/>
    /// goes through <see cref="Glyphs.Safe"/> on the way in. Every label the
    /// game builds at runtime is one of these (MenuKit, the pause menu, the
    /// gauges, the touch panels, the HUD's own labels), so the 300-odd em
    /// dashes in the code and the catalogs stop printing as holes in a WebGL
    /// build without anybody having to remember them. The HUD labels baked
    /// into the race scenes are plain Text; RaceHUD's setter makes those safe.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("")]
    public class SafeText : Text
    {
        public override string text
        {
            get => base.text;
            set => base.text = Glyphs.Safe(value);
        }
    }
}
