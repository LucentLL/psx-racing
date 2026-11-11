using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE ARCHES FIT THE SHEET'S TYRES (2026-11-11, owner: "No car may sit
    /// lifted or tilted at rest").
    ///
    /// 94 of 319 catalogue rows wear a borrowed shell (mb_pagoda for the
    /// Alpines, Elans and Cobras; gto_66 for the Celica TA22 and the Camaros;
    /// euro_hatch for thirty hatches and rally cars; supra_a80 for the Group C
    /// cars ...) on the row's REAL tyres off the GT4 sheet - up to 763 mm in a
    /// shell drawn round a 651 mm one. At the static ride those tyres' crowns
    /// stood in their arches, so CarBodyLean.Seat held the whole shell up off
    /// them, every frame, by up to 11 cm and 2 deg of pitch: the "monster
    /// truck", on cars the owner had not driven yet.
    ///
    /// Three things could give: the tyre (shrink it off its spec - no: the
    /// sheet is real data and physics drives on it), the ride (raise the body
    /// level by the worst axle - a car sitting up to 11 cm high on stilts),
    /// or the ARCH. This takes the arch: where a row's tyre needs more room at
    /// rest than the borrowed shell gives, the bodywork over that wheel is
    /// raised by exactly the shortfall (+5 mm), in the vertex shader
    /// (PSXArchFlare.cginc in PSX/CarPaint) - a flared wheel opening over a
    /// bigger tyre, which is what a real car on bigger tyres has. The body sits
    /// at its fitted height, level; the tyre is the sheet's; no tyre in paint.
    /// ArchCeilings.HubMax reports the flared arch, so the per-frame seat is
    /// back to what it was made for: travel under load.
    ///
    /// Solved once per fit (CarModelLibrary.Fit), no bake: the shortfall comes
    /// from the same ArchHubs table the seat reads. Rows with room get 0 and
    /// draw exactly as before. PSX_ARCH_FLARE=0 = no flare (the old seat lift).
    /// </summary>
    public static class ArchFlare
    {
        public static bool On = System.Environment.GetEnvironmentVariable("PSX_ARCH_FLARE") != "0";
        /// <summary>The seat's own paint margin (CarBodyLean.SeatMargin) plus
        /// a little over: an arch flared exactly to the tyre reads as touching.</summary>
        const float Margin = 0.005f;

        static readonly int HubId = Shader.PropertyToID("_PSXArchHub");
        static readonly int FlareId = Shader.PropertyToID("_PSXArchFlare");
        static readonly int XId = Shader.PropertyToID("_PSXArchX");
        static MaterialPropertyBlock block;

        /// <summary>
        /// Fill f.flareF / flareR: how far each axle's arch is short of its
        /// tyre at the static ride. In the fit's own frame (contact patch on
        /// y = 0, hub at the radius), the physics' static hub is the radius
        /// +/- half the sheet's rake (CarBody.Lifted moves both by the same).
        /// </summary>
        public static void Solve(CarModelDef def, ref CarModelLibrary.ShellFit f, float rakeM)
        {
            f.flareF = f.flareR = 0f;
            if (!On || def == null) return;
            float hF = Mathf.Min(ArchCeilings.HubMax(def, f, 0), ArchCeilings.HubMax(def, f, 1));
            float hR = Mathf.Min(ArchCeilings.HubMax(def, f, 2), ArchCeilings.HubMax(def, f, 3));
            float needF = f.radiusF + 0.5f * rakeM + Margin - hF;
            float needR = f.radiusR - 0.5f * rakeM + Margin - hR;
            if (!float.IsInfinity(needF) && needF > 0f) f.flareF = needF;
            if (!float.IsInfinity(needR) && needR > 0f) f.flareR = needR;
        }

        /// <summary>
        /// Tell a body renderer where its arches are and how far to raise them
        /// (all in the body MESH's own frame; the body's transform adds the
        /// yaw and the stretch). Zero flare = the shader leaves every vertex
        /// where it was. Keeps whatever else is on the renderer's block.
        /// </summary>
        public static void Stamp(Renderer r, CarModelDef def, CarModelLibrary.ShellFit f)
        {
            if (r == null || def == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            if (f.flareF <= 0f && f.flareR <= 0f)
            {
                block.SetVector(FlareId, Vector4.zero);
                r.SetPropertyBlock(block);
                return;
            }
            float sgn = Mathf.Abs(def.bodyYaw) < 90f ? 1f : -1f;
            float hubY = def.wheelRadius - def.bodyYOffset;
            float zF = sgn * (def.wheelbase * 0.5f - def.bodyZOffset);
            float zR = sgn * (-def.wheelbase * 0.5f - def.bodyZOffset);
            float R = Mathf.Max(f.radiusF, f.radiusR);
            float sz = Mathf.Max(f.sz, 1e-3f), sx = Mathf.Max(f.sx, 1e-3f);
            float meshW = def.wheelMesh != null ? def.wheelMesh.bounds.size.x : 0.2f;
            float innerF = (f.trackF * 0.5f - f.wheelScaleF.x * meshW * 0.5f) / sx;
            float innerR = (f.trackR * 0.5f - f.wheelScaleR.x * meshW * 0.5f) / sx;
            block.SetVector(HubId, new Vector4(hubY, zF, zR, R));
            block.SetVector(FlareId, new Vector4(f.flareF, f.flareR, R / sz, 1f));
            block.SetVector(XId, new Vector4(innerF, innerR, 0f, 0f));
            r.SetPropertyBlock(block);
        }
    }
}
