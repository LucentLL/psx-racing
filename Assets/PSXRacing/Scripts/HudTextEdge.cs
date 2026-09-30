using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// THE HUD TEXT'S EDGE (the colour pass, C9, 2026-09-29): a dark ring
    /// <see cref="radius"/> pixels wide round every stroke, on all sides.
    ///
    /// The labels had a one-sided drop Shadow (1, -1), and Unity's Outline is
    /// no better at this size: it draws its four copies on the DIAGONALS
    /// only, so at the HUD's 12 px on a 480-line framebuffer - one-pixel
    /// strokes, anti-aliased - the pixels straight above, below and beside a
    /// stroke were half covered or not at all, and over a noon sky the white
    /// letters stood on the sky itself (the owner's street name: 1.2:1). One
    /// ring of eight copies covered every pixel touching a stroke but only
    /// at the stroke's own anti-aliased alpha, a grey ring one pixel wide
    /// (2.1-4.4:1 at noon, measured); two rings (the 24 offsets of a 5x5
    /// square) give the letters a solid dark bed two pixels deep - the 90s
    /// arcade HUD's black-outlined type - and the white on the grade's floor
    /// is what the eye reads. Still low-res, dithered and graded, like
    /// everything else in the picture (HudOnTop says why that matters).
    ///
    /// A Shadow subclass, so HudOnTop.OutlineText (which converts exactly
    /// <see cref="Shadow"/>) leaves it alone; <see cref="Shadow.effectDistance"/>
    /// is one step of the ring in canvas units (1 = one framebuffer pixel).
    /// </summary>
    public class HudTextEdge : Shadow
    {
        /// <summary>Rings of copies: 1 = the eight neighbours, 2 = the 24 of
        /// a 5x5 square.</summary>
        public int radius = 2;

        /// <summary>
        /// ONE LIST FOR EVERY EDGE, REUSED (review, 2026-09-29). The first cut
        /// made a new List per rebuild, sized for 25 copies of the text: the
        /// race clock is Set every frame (RaceHUD), so an 8-glyph "1'23"456"
        /// was ~48 vertices x 25 x 108 bytes = ~130 KB of garbage a frame,
        /// ~7.8 MB/s at 60 fps, on WebGL's non-incremental collector - a
        /// hitch every few seconds on a phone. Unity's own Shadow borrows a
        /// pooled list; so does this. Rebuilds happen on the main thread only
        /// (the canvas update), so one static list is enough, and after the
        /// first rebuild of the longest text its capacity never grows again:
        /// zero bytes a frame.
        /// </summary>
        static readonly List<UIVertex> scratch = new List<UIVertex>(1024);

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            int r = Mathf.Clamp(radius, 1, 3);
            var verts = scratch;
            verts.Clear();
            vh.GetUIVertexStream(verts);
            int copies = (2 * r + 1) * (2 * r + 1);   // the rings and the text itself
            int needed = verts.Count * copies;
            if (verts.Capacity < needed) verts.Capacity = needed;
            // Each call puts a shadow where the text was and moves the text
            // to the end (Shadow.ApplyShadowZeroAlloc), so the next call
            // copies the text again, not the shadow before it: one ring
            // copy per offset, the text drawn last, over all of them. The
            // outer ring first, so the inner one is drawn over it.
            int start = 0, end = verts.Count;
            float sx = Mathf.Abs(effectDistance.x), sy = Mathf.Abs(effectDistance.y);
            for (int ring = r; ring >= 1; ring--)
                for (int dy = -ring; dy <= ring; dy++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != ring) continue;
                        ApplyShadowZeroAlloc(verts, effectColor, start, verts.Count, dx * sx, dy * sy);
                        start = end;
                        end = verts.Count;
                    }
            vh.Clear();
            vh.AddUIVertexTriangleStream(verts);
            verts.Clear();
        }
    }
}
