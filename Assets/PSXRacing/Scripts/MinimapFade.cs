using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>The only backing the map has: a soft dark radial fade for
    /// legibility, gone well before the edge. Built once.</summary>
    // CanvasRenderer REQUIRED, as Image and Text declare it: Graphic only
    // adds one itself in a player, and in the editor its GetComponent returns
    // a fake null - the graphic is created, registered and never drawn.
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    public class MinimapFade : MaskableGraphic
    {
        static readonly Color32 Centre = new Color32(0, 4, 13, 76);
        static readonly Color32 Mid = new Color32(0, 4, 13, 52);
        static readonly Color32 Rim = new Color32(0, 4, 13, 0);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = rectTransform.rect;
            float r = Mathf.Min(rect.width, rect.height) * 0.5f;
            if (r < 4f) return;
            const int N = 40;
            var v = UIVertex.simpleVert;
            v.color = Centre; v.position = Vector2.zero; vh.AddVert(v);
            for (int k = 0; k < N; k++)
            {
                float a = k * Mathf.PI * 2f / N;
                var u = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                v.color = Mid; v.position = u * (r * 0.55f); vh.AddVert(v);
                v.color = Rim; v.position = u * r; vh.AddVert(v);
            }
            for (int k = 0; k < N; k++)
            {
                int m0 = 1 + k * 2, r0 = m0 + 1;
                int m1 = 1 + ((k + 1) % N) * 2, r1 = m1 + 1;
                vh.AddTriangle(0, m0, m1);
                vh.AddTriangle(m0, r0, r1);
                vh.AddTriangle(m0, r1, m1);
            }
        }
    }
}
